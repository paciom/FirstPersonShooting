"""Make Meshy/Blender-exported GLBs shade like the rest of the fleet.

Three separate defects, all in the material JSON, all of which made the robots
look wrong in ways that read as lighting or texture bugs.

1. SELF-EMISSION. Meshy's rigged robot exports come through Blender's glTF exporter with an
Emission node driving the base texture, so the exporter writes
`emissiveFactor: [1,1,1]` plus an `emissiveTexture` pointing at the SAME image
as `baseColorTexture`. glTFast imports that faithfully, so every robot renders
its own albedo as full-strength emission on top of the lit result. Emission
bypasses N.L entirely, which means no light/dark falloff at any light rig, and
roughly doubled colour that reads as over-saturation. It also feeds bloom from
the whole body rather than the parts meant to glow.

2. BLOWN SPECULAR COLOUR. The same exports carry
`KHR_materials_specular.specularColorFactor: [2,2,2]`. Specular colour is meant
to stay at or below 1; 2x adds white highlight blowout. Clamping to 1.0 makes it
the identity value whether or not the importer supports the extension.

3. MISSING PBR FACTORS -- the one that survives stripping the emission and then
looks like a broken texture. The exporter writes `pbrMetallicRoughness` with a
`baseColorTexture` and NOTHING else, and the glTF defaults for the two omitted
fields are `metallicFactor: 1.0` and `roughnessFactor: 1.0`. A fully metallic
surface has no diffuse term at all -- its base colour becomes specular F0 -- and
at roughness 1.0 that specular lobe is spread so wide that small point lights
barely register. The body goes near-black and the albedo shows only where a face
happens to catch a highlight, which reads as a corrupt or mottled texture. It is
neither: the texture is fine and the mesh is fine, the material is just claiming
to be rough bare metal. Writing the factors explicitly fixes it.

The `*-vehicle.glb` files came out of the pygltflib path instead, which already
writes 0.0/0.8, and that is exactly why vehicles shade correctly and robots do
not. The defaults here match those numbers so a robot and the vehicle it folds
into are the same material.

Only the JSON chunk is rewritten. The binary chunk -- meshes, skins, animations,
textures -- is copied through byte for byte (verified: the BIN sha1 is unchanged
across a run). The textures array is left intact so every existing index stays
valid; the now-unreferenced entry costs nothing because both entries shared one
image.

Usage:
    python Tools/fixmeshymaterials.py --dry-run Assets/Models/Meshy/*-rig.glb
    python Tools/fixmeshymaterials.py Assets/Models/Meshy/*.glb
"""

import argparse
import glob
import json
import struct
import sys

JSON_CHUNK = b"JSON"


def read_glb(path):
    """Returns (version, [[chunk_type, chunk_bytes], ...])."""
    with open(path, "rb") as handle:
        data = handle.read()
    magic, version, _total = struct.unpack("<4sII", data[:12])
    if magic != b"glTF":
        raise ValueError(f"{path}: not a GLB (magic {magic!r})")
    chunks = []
    offset = 12
    while offset < len(data):
        length, kind = struct.unpack("<I4s", data[offset : offset + 8])
        chunks.append([kind, data[offset + 8 : offset + 8 + length]])
        offset += 8 + length
    return version, chunks


def write_glb(path, version, chunks):
    """Re-pads every chunk to 4 bytes and fixes both the chunk and file lengths."""
    body = b""
    for kind, payload in chunks:
        padding = (-len(payload)) % 4
        if padding:
            # Spec: JSON pads with spaces, BIN pads with zeroes.
            payload += (b" " if kind == JSON_CHUNK else b"\x00") * padding
        body += struct.pack("<I4s", len(payload), kind) + payload
    with open(path, "wb") as handle:
        handle.write(struct.pack("<4sII", b"glTF", version, 12 + len(body)) + body)


def fix_materials(gltf, metallic, roughness):
    """Applies all three fixes. Returns a list of human-readable edits."""
    edits = []
    for index, material in enumerate(gltf.get("materials", [])):
        removed = []
        for key in ("emissiveFactor", "emissiveTexture"):
            if material.pop(key, None) is not None:
                removed.append(key)

        # Only fill in what the exporter omitted. A material that states its own
        # factors, or drives them from a metallicRoughnessTexture, is left alone
        # -- it knows what it wants and the spec defaults are not in play.
        pbr = material.get("pbrMetallicRoughness")
        if pbr is not None and "metallicRoughnessTexture" not in pbr:
            for key, value in (("metallicFactor", metallic),
                               ("roughnessFactor", roughness)):
                if key not in pbr:
                    pbr[key] = value
                    removed.append(f"{key} default(1.0) -> {value}")

        extensions = material.get("extensions", {})
        if extensions.pop("KHR_materials_emissive_strength", None) is not None:
            removed.append("KHR_materials_emissive_strength")

        specular = extensions.get("KHR_materials_specular")
        if specular and "specularColorFactor" in specular:
            factor = specular["specularColorFactor"]
            if any(channel > 1.0 for channel in factor):
                specular["specularColorFactor"] = [min(1.0, c) for c in factor]
                removed.append(f"specularColorFactor {factor} -> 1.0")

        if not extensions:
            material.pop("extensions", None)
        if removed:
            edits.append(f"  material[{index}] ({material.get('name', '?')}): "
                         + ", ".join(removed))
    return edits


def process(path, dry_run, metallic, roughness):
    version, chunks = read_glb(path)
    json_chunk = next((c for c in chunks if c[0] == JSON_CHUNK), None)
    if json_chunk is None:
        print(f"{path}: no JSON chunk, skipped")
        return False

    gltf = json.loads(json_chunk[1].decode("utf-8"))
    edits = fix_materials(gltf, metallic, roughness)
    if not edits:
        print(f"{path}: already clean")
        return False

    print(f"{path}:")
    for edit in edits:
        print(edit)
    if not dry_run:
        json_chunk[1] = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
        write_glb(path, version, chunks)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="+", help="GLB files (globs allowed)")
    parser.add_argument("--dry-run", action="store_true",
                        help="report what would change without writing")
    # Defaults match Tools/meshyvehicles.py, so a robot and the vehicle it folds
    # into shade identically.
    parser.add_argument("--metallic", type=float, default=0.0,
                        help="metallicFactor to write when the exporter omitted it")
    parser.add_argument("--roughness", type=float, default=0.8,
                        help="roughnessFactor to write when the exporter omitted it")
    args = parser.parse_args()

    files = sorted({match for pattern in args.paths for match in glob.glob(pattern)})
    if not files:
        print("no files matched", file=sys.stderr)
        return 1

    changed = sum(process(path, args.dry_run, args.metallic, args.roughness)
                  for path in files)
    verb = "would change" if args.dry_run else "changed"
    print(f"\n{verb} {changed} of {len(files)} file(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
