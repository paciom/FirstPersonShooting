"""Re-textures the rigged robots with full PBR maps and injects them back.

WHY. The robots shipped with a single albedo image and nothing else -- no normal
map, no metallic/roughness map. Every surface therefore has identical uniform
roughness and zero surface relief, so all detail has to come from the albedo,
and Meshy's albedo is flat colour fills. That reads as smooth plastic blobs the
moment real lighting is applied. (Full-strength emission used to hide it; see
Tools/fixmeshymaterials.py for that story.)

THE LOAD-BEARING FLAG is `enable_original_uv`. It tells Meshy to keep the
model's existing UV layout instead of unwrapping afresh, which means the maps
that come back line up with the UVs already baked into the rigged GLBs. So the
new textures can be injected straight into -rig/-walk/-run without re-rigging,
without re-exporting animations, and without invalidating the forged controllers.

Uploading the 6.6 MB rig would be wasteful and may exceed the request body, so
`submit` sends a geometry-only GLB rebuilt from the rig's POSITION, NORMAL,
TEXCOORD_0 and indices -- about 210 KB. Texturing only needs the surface and its
UVs; the skeleton and animation are irrelevant to it.

Meshy returns metallic and roughness as SEPARATE greyscale images, but glTF
wants one texture with roughness in G and metallic in B. `download` packs them.

The emission map (meshy-6 returns one when enable_pbr is set) is downloaded but
deliberately NOT wired into the material. Whole-body emission is the exact
defect this whole effort removed. Wire it by hand later if you want controlled
glowing strips.

  python meshyretexture.py submit panther          # spends 10 credits each
  python meshyretexture.py status
  python meshyretexture.py download panther
  python meshyretexture.py inject panther          # rewrites -rig/-walk/-run
"""
import base64
import json
import os
import struct
import sys
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meshyvehicles import API, VEHICLES, balance, call  # noqa: E402

ROOT = "D:/Claude/FirstPersongShooting"
MODELS = f"{ROOT}/Assets/Models/Meshy"
STATE_PATH = f"{ROOT}/Tools/retexture_tasks.json"
MAPS_DIR = f"{ROOT}/Tools/.retexture_maps"

# Matches the STYLE line the vehicles were generated with, so a robot and its
# vehicle form finally read as the same machine. The colour half is pulled from
# VEHICLES -- the robots' own original prompts were never recorded, and several
# came back orange-and-teal regardless of what they asked for.
STYLE = ("clean flat panels with crisp panel seams, glowing light strips, "
         "chunky stylised low-poly toy look, friendly sci-fi, matte finish")

VARIANTS = ("rig", "walk", "run")


# ------------------------------------------------------------------- GLB codec

def parse(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset, chunks = 12, {}
    while offset < len(data):
        length, kind = struct.unpack("<I4s", data[offset:offset + 8])
        chunks[kind] = data[offset + 8:offset + 8 + length]
        offset += 8 + length
    json_key = next(k for k in chunks if k.startswith(b"JSON"))
    bin_key = next(k for k in chunks if k.startswith(b"BIN"))
    return json.loads(chunks[json_key].decode("utf-8")), chunks[bin_key]


def build(gltf, buffer):
    payload = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    payload += b" " * ((-len(payload)) % 4)
    blob = buffer + b"\x00" * ((-len(buffer)) % 4)
    body = (struct.pack("<I4s", len(payload), b"JSON") + payload
            + struct.pack("<I4s", len(blob), b"BIN\x00") + blob)
    return struct.pack("<4sII", b"glTF", 2, 12 + len(body)) + body


def accessor_bytes(gltf, buffer, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    width = {5126: 4, 5125: 4, 5123: 2, 5121: 1}[acc["componentType"]]
    count = {"VEC3": 3, "VEC2": 2, "SCALAR": 1}[acc["type"]]
    return buffer[start:start + acc["count"] * width * count], acc


def geometry_only(name):
    """A minimal GLB carrying just the surface and its UVs -- what texturing needs."""
    gltf, buffer = parse(f"{MODELS}/{name}-rig.glb")
    prim = gltf["meshes"][0]["primitives"][0]
    blob, views, accessors = b"", [], []
    for index in (prim["attributes"]["POSITION"], prim["attributes"]["NORMAL"],
                  prim["attributes"]["TEXCOORD_0"], prim["indices"]):
        data, acc = accessor_bytes(gltf, buffer, index)
        blob += b"\x00" * ((-len(blob)) % 4)
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data)})
        blob += data
        entry = {"bufferView": len(views) - 1, "componentType": acc["componentType"],
                 "count": acc["count"], "type": acc["type"]}
        if "min" in acc:
            entry["min"], entry["max"] = acc["min"], acc["max"]
        accessors.append(entry)
    return build({
        "asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{
            "attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3}]}],
        "accessors": accessors, "bufferViews": views,
        "buffers": [{"byteLength": len(blob)}],
    }, blob)


# -------------------------------------------------------------------- task state

def state():
    if not os.path.exists(STATE_PATH):
        return {}
    with open(STATE_PATH) as handle:
        return json.load(handle)


def save(data):
    with open(STATE_PATH, "w") as handle:
        json.dump(data, handle, indent=2)


# ------------------------------------------------------------------------ submit

def submit(names):
    data = state()
    print(f"balance before: {balance()}")
    for name in names:
        glb = geometry_only(name)
        _, colours = VEHICLES[name]
        result = call("POST", "/v1/retexture", {
            "model_url": "data:application/octet-stream;base64,"
                         + base64.b64encode(glb).decode(),
            "text_style_prompt": f"{colours}, {STYLE}"[:600],
            "enable_original_uv": True,
            "enable_pbr": True,
            "remove_lighting": True,
            "texture_resolution": "2k",
            "ai_model": "meshy-6",
        })
        task = result.get("result") or result.get("id")
        data.setdefault(name, {})["task"] = task
        print(f"{name:9s} {len(glb) / 1024:6.0f} KB uploaded -> {task}")
    save(data)
    print(f"balance after:  {balance()}")


def status():
    data = state()
    for name, entry in sorted(data.items()):
        info = call("GET", f"/v1/retexture/{entry['task']}")
        line = f"{name:9s} {info['status']:10s} {info.get('progress', 0):3d}%"
        urls = info.get("texture_urls") or []
        if urls:
            entry["maps"] = urls[0]
            line += "  maps: " + ",".join(sorted(urls[0]))
        # The key is present and explicitly null on success, so a .get default
        # never fires -- it has to be coalesced after the lookup.
        error = (info.get("task_error") or {}).get("message")
        if error:
            line += "  ERROR " + error
        print(line)
    save(data)


# ---------------------------------------------------------------------- download

def download(names):
    from PIL import Image
    data = state()
    os.makedirs(MAPS_DIR, exist_ok=True)
    for name in names:
        maps = data.get(name, {}).get("maps")
        if not maps:
            print(f"{name:9s} no maps yet -- run status")
            continue
        for slot, url in sorted(maps.items()):
            path = f"{MAPS_DIR}/{name}_{slot}.png"
            urllib.request.urlretrieve(url, path)
            print(f"{name:9s} {slot:11s} {os.path.getsize(path) / 1024:7.0f} KB")

        # glTF packs both into one texture: roughness in G, metallic in B.
        metallic = f"{MAPS_DIR}/{name}_metallic.png"
        roughness = f"{MAPS_DIR}/{name}_roughness.png"
        if os.path.exists(metallic) and os.path.exists(roughness):
            m = Image.open(metallic).convert("L")
            r = Image.open(roughness).convert("L")
            if m.size != r.size:
                m = m.resize(r.size, Image.LANCZOS)
            packed = Image.merge("RGB", (Image.new("L", r.size, 0), r, m))
            packed.save(f"{MAPS_DIR}/{name}_mr.png")
            print(f"{name:9s} {'packed mr':11s} "
                  f"{os.path.getsize(f'{MAPS_DIR}/{name}_mr.png') / 1024:7.0f} KB")


# ------------------------------------------------------------------------ inject

def rebuild_buffer(gltf, buffer, replace, append):
    """Recopies every bufferView (indices preserved) then appends new ones.

    Accessor byteOffsets are relative to their bufferView, so copying each view
    intact and only rewriting byteOffset/byteLength keeps every accessor valid.
    """
    blob = b""
    for index, view in enumerate(gltf["bufferViews"]):
        start = view.get("byteOffset", 0)
        data = replace.get(index, buffer[start:start + view["byteLength"]])
        blob += b"\x00" * ((-len(blob)) % 4)
        view["byteOffset"], view["byteLength"] = len(blob), len(data)
        blob += data
    added = []
    for data in append:
        blob += b"\x00" * ((-len(blob)) % 4)
        gltf["bufferViews"].append(
            {"buffer": 0, "byteOffset": len(blob), "byteLength": len(data)})
        added.append(len(gltf["bufferViews"]) - 1)
        blob += data
    gltf["buffers"][0]["byteLength"] = len(blob)
    return blob, added


def inject(names):
    for name in names:
        base = open(f"{MAPS_DIR}/{name}_base_color.png", "rb").read()
        normal = open(f"{MAPS_DIR}/{name}_normal.png", "rb").read()
        mr = open(f"{MAPS_DIR}/{name}_mr.png", "rb").read()

        for variant in VARIANTS:
            path = f"{MODELS}/{name}-{variant}.glb"
            gltf, buffer = parse(path)
            material = gltf["materials"][0]
            pbr = material["pbrMetallicRoughness"]

            albedo_image = gltf["textures"][pbr["baseColorTexture"]["index"]]["source"]
            albedo_view = gltf["images"][albedo_image]["bufferView"]

            buffer, added = rebuild_buffer(
                gltf, buffer, {albedo_view: base}, [normal, mr])

            for view in added:
                gltf["images"].append({"bufferView": view, "mimeType": "image/png"})
            normal_image, mr_image = len(gltf["images"]) - 2, len(gltf["images"]) - 1

            sampler = gltf["textures"][0].get("sampler")
            for source in (normal_image, mr_image):
                entry = {"source": source}
                if sampler is not None:
                    entry["sampler"] = sampler
                gltf["textures"].append(entry)

            material["normalTexture"] = {"index": len(gltf["textures"]) - 2}
            pbr["metallicRoughnessTexture"] = {"index": len(gltf["textures"]) - 1}
            # With a map present the factors multiply it, so they go to 1.0 and
            # let the texture drive metallic and roughness outright.
            pbr["metallicFactor"], pbr["roughnessFactor"] = 1.0, 1.0

            with open(path, "wb") as handle:
                handle.write(build(gltf, buffer))
            print(f"{name}-{variant:5s} {os.path.getsize(path) / 1024:8.0f} KB  "
                  f"albedo+normal+mr wired")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "status"
    rest = sys.argv[2:] or list(VEHICLES)
    if command == "submit":
        submit(rest)
    elif command == "status":
        status()
    elif command == "download":
        download(rest)
    elif command == "inject":
        inject(rest)
    else:
        raise SystemExit(__doc__)
