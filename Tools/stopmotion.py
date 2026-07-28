"""Merges the transformation stages into one GLB that plays as stop motion.

Each stage is a separate model generated from a different video frame, so there
is nothing to interpolate between -- the meshes share no topology. The playback
therefore POPS from stage to stage, which is exactly the thing under test: if
the silhouettes read as a continuous transformation at 5 frames, a real rig is
worth building; if they read as five unrelated objects, the idea is dead and
this was a cheap way to find out.

Visibility is animated through node scale with STEP interpolation, because glTF
has no visibility track. Scale 0 collapses a stage to a point (invisible),
scale 1 shows it. STEP, not LINEAR, so stages snap rather than inflate.

Every stage is normalised to a common height and centred on its own bounds, as
the generated models arrive at unrelated scales and origins. Height alone is the
wrong yardstick for the later stages -- a tank is long and low, so matching it to
the robot's height would leave it tiny -- so stages are matched on the largest
horizontal dimension once they stop being humanoid.

  python stopmotion.py out.glb stage1.glb stage2.glb ...
"""
import json
import math
import struct
import sys

JSON_CHUNK = b"JSON"
SECONDS_PER_STAGE = 0.6
TARGET_SIZE = 2.0   # largest dimension of any stage, in units


def parse(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset, chunks = 12, {}
    while offset < len(data):
        length, kind = struct.unpack("<I4s", data[offset:offset + 8])
        chunks[kind] = data[offset + 8:offset + 8 + length]
        offset += 8 + length
    json_key = next(k for k in chunks if k.startswith(b"JSON"))
    bin_key = next((k for k in chunks if k.startswith(b"BIN")), None)
    return json.loads(chunks[json_key].decode("utf-8")), (chunks[bin_key] if bin_key else b"")


def build(gltf, buffer):
    payload = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    payload += b" " * ((-len(payload)) % 4)
    blob = buffer + b"\x00" * ((-len(buffer)) % 4)
    body = (struct.pack("<I4s", len(payload), JSON_CHUNK) + payload
            + struct.pack("<I4s", len(blob), b"BIN\x00") + blob)
    return struct.pack("<4sII", b"glTF", 2, 12 + len(body)) + body


def bounds(gltf, node_indices, nodes):
    """World-space AABB from POSITION accessor min/max, walking node transforms."""
    lo = [math.inf] * 3
    hi = [-math.inf] * 3

    def walk(index, offset, scale):
        node = nodes[index]
        t = node.get("translation", [0, 0, 0])
        s = node.get("scale", [1, 1, 1])
        off = [offset[k] + t[k] * scale[k] for k in range(3)]
        sc = [scale[k] * s[k] for k in range(3)]
        if "mesh" in node:
            # glTF: the transform of a node referencing a SKINNED mesh is
            # ignored -- the joints place those vertices, and the accessor
            # values are already in their final space. Applying the skeleton's
            # transforms here (bones are authored in centimetres) shrinks the
            # measured model by ~100x and the stage gets scaled up to match.
            skinned = "skin" in node
            mo = [0, 0, 0] if skinned else off
            ms = [1, 1, 1] if skinned else sc
            for prim in gltf["meshes"][node["mesh"]]["primitives"]:
                acc = gltf["accessors"][prim["attributes"]["POSITION"]]
                if "min" not in acc:
                    continue
                for k in range(3):
                    lo[k] = min(lo[k], mo[k] + acc["min"][k] * ms[k])
                    hi[k] = max(hi[k], mo[k] + acc["max"][k] * ms[k])
        for child in node.get("children", []):
            walk(child, off, sc)

    for index in node_indices:
        walk(index, [0, 0, 0], [1, 1, 1])
    if lo[0] is math.inf:
        return [0, 0, 0], [1, 1, 1]
    return lo, hi


def merge(paths, out_path):
    merged = {"asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": []}],
              "nodes": [], "meshes": [], "accessors": [], "bufferViews": [],
              "materials": [], "textures": [], "images": [], "samplers": [],
              "animations": [], "buffers": [{"byteLength": 0}]}
    blob = b""
    stage_roots = []

    for stage, path in enumerate(paths):
        gltf, buf = parse(path)
        base = {k: len(merged[k]) for k in
                ("nodes", "meshes", "accessors", "bufferViews", "materials",
                 "textures", "images", "samplers")}

        blob += b"\x00" * ((-len(blob)) % 4)
        byte_base = len(blob)
        blob += buf

        for view in gltf.get("bufferViews", []):
            view = dict(view)
            view["byteOffset"] = view.get("byteOffset", 0) + byte_base
            view["buffer"] = 0
            merged["bufferViews"].append(view)
        for acc in gltf.get("accessors", []):
            acc = dict(acc)
            if "bufferView" in acc:
                acc["bufferView"] += base["bufferViews"]
            merged["accessors"].append(acc)
        for image in gltf.get("images", []):
            image = dict(image)
            if "bufferView" in image:
                image["bufferView"] += base["bufferViews"]
            merged["images"].append(image)
        merged["samplers"].extend(gltf.get("samplers", []))
        for texture in gltf.get("textures", []):
            texture = dict(texture)
            if "source" in texture:
                texture["source"] += base["images"]
            if "sampler" in texture:
                texture["sampler"] += base["samplers"]
            merged["textures"].append(texture)
        for material in json.loads(json.dumps(gltf.get("materials", []))):
            for holder in (material, material.get("pbrMetallicRoughness", {})):
                for slot in ("baseColorTexture", "metallicRoughnessTexture",
                             "normalTexture", "occlusionTexture", "emissiveTexture"):
                    ref = holder.get(slot)
                    if isinstance(ref, dict) and "index" in ref:
                        ref["index"] += base["textures"]
            merged["materials"].append(material)
        for mesh in json.loads(json.dumps(gltf.get("meshes", []))):
            for prim in mesh["primitives"]:
                prim["attributes"] = {k: v + base["accessors"]
                                      for k, v in prim["attributes"].items()}
                if "indices" in prim:
                    prim["indices"] += base["accessors"]
                if "material" in prim:
                    prim["material"] += base["materials"]
            merged["meshes"].append(mesh)
        skinned_roots = []
        for local, node in enumerate(json.loads(json.dumps(gltf.get("nodes", [])))):
            if "mesh" in node:
                node["mesh"] += base["meshes"]
            if "children" in node:
                node["children"] = [c + base["nodes"] for c in node["children"]]
            if node.pop("skin", None) is not None and "mesh" in node:
                # A stage is a still frame, so the skeleton is dead weight. But
                # the vertices are in skin space, which is only correct with an
                # identity transform -- keeping the node's own TRS (or its
                # parents') would re-apply the centimetre-scale bone hierarchy.
                # Detach it: identity transform, promoted to a stage root.
                for key in ("translation", "rotation", "scale", "matrix"):
                    node.pop(key, None)
                node.pop("children", None)
                skinned_roots.append(local + base["nodes"])
            merged["nodes"].append(node)

        if skinned_roots:
            roots = skinned_roots
        else:
            scene = gltf.get("scenes", [{}])[gltf.get("scene", 0)]
            roots = [r + base["nodes"] for r in scene.get("nodes", [])]

        lo, hi = bounds(merged, roots, merged["nodes"])
        size = [hi[k] - lo[k] for k in range(3)]
        # Fit each stage on its LARGEST dimension, not its height. A humanoid is
        # tall and a tank is long, so height-fitting blows the tank up until it
        # dwarfs the robot it just folded out of; largest-dimension fitting puts
        # the tank's length at roughly the robot's height, which is the right
        # read for a machine that turns into itself.
        extent = max(size)
        factor = TARGET_SIZE / max(1e-6, extent)
        centre = [(hi[k] + lo[k]) / 2 for k in range(3)]

        holder = {
            "name": f"stage{stage + 1}",
            "children": roots,
            "scale": [factor] * 3,
            # Centre horizontally, sit on the ground plane.
            "translation": [-centre[0] * factor, -lo[1] * factor, -centre[2] * factor],
        }
        merged["nodes"].append(holder)
        holder_index = len(merged["nodes"]) - 1
        merged["scenes"][0]["nodes"].append(holder_index)
        stage_roots.append((holder_index, holder["scale"]))

    merged["buffers"][0]["byteLength"] = len(blob)
    blob = add_animation(merged, blob, stage_roots)
    with open(out_path, "wb") as handle:
        handle.write(build(merged, blob))
    return out_path


def add_animation(gltf, blob, stage_roots):
    """One STEP-interpolated scale track per stage: visible for its own slot."""
    count = len(stage_roots)
    times = [i * SECONDS_PER_STAGE for i in range(count + 1)]

    blob += b"\x00" * ((-len(blob)) % 4)
    time_offset = len(blob)
    blob += struct.pack(f"<{len(times)}f", *times)
    gltf["bufferViews"].append({"buffer": 0, "byteOffset": time_offset,
                                "byteLength": len(times) * 4})
    gltf["accessors"].append({"bufferView": len(gltf["bufferViews"]) - 1,
                              "componentType": 5126, "count": len(times),
                              "type": "SCALAR", "min": [times[0]], "max": [times[-1]]})
    time_accessor = len(gltf["accessors"]) - 1

    channels, samplers = [], []
    for stage, (node_index, scale) in enumerate(stage_roots):
        values = []
        for step in range(len(times)):
            on = step == stage
            values.extend(scale if on else [0.0, 0.0, 0.0])
        blob += b"\x00" * ((-len(blob)) % 4)
        offset = len(blob)
        blob += struct.pack(f"<{len(values)}f", *values)
        gltf["bufferViews"].append({"buffer": 0, "byteOffset": offset,
                                    "byteLength": len(values) * 4})
        gltf["accessors"].append({"bufferView": len(gltf["bufferViews"]) - 1,
                                  "componentType": 5126, "count": len(times),
                                  "type": "VEC3"})
        samplers.append({"input": time_accessor, "interpolation": "STEP",
                         "output": len(gltf["accessors"]) - 1})
        channels.append({"sampler": len(samplers) - 1,
                         "target": {"node": node_index, "path": "scale"}})

    gltf["animations"].append({"name": "StopMotion", "channels": channels,
                               "samplers": samplers})
    gltf["buffers"][0]["byteLength"] = len(blob)
    return blob


if __name__ == "__main__":
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    out = merge(sys.argv[2:], sys.argv[1])
    print("wrote", out)
