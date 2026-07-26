"""Extracts the base-colour texture out of each Meshy -rig.glb.

The prompts these robots were generated from were never written down, and the
refine step's texture_prompt overrode several of the colours the base prompt
asked for — so the only reliable record of what a robot actually looks like is
the atlas baked into its own GLB. Pull it out to match anything new to it.

  python rigtexture.py <out-dir> [robot ...]
"""
import glob
import json
import os
import struct
import sys

D = "D:/Claude/FirstPersongShooting/Assets/Models/Meshy"


def load(path):
    with open(path, "rb") as f:
        struct.unpack("<3I", f.read(12))
        clen, _ = struct.unpack("<2I", f.read(8))
        gltf = json.loads(f.read(clen).decode())
        blen, _ = struct.unpack("<2I", f.read(8))
        return gltf, f.read(blen)


def base_colour_image(gltf):
    """Index of the image the first material uses as its base colour map."""
    for material in gltf.get("materials", []):
        pbr = material.get("pbrMetallicRoughness", {})
        texture = pbr.get("baseColorTexture")
        if texture is not None:
            return gltf["textures"][texture["index"]]["source"]
    return 0 if gltf.get("images") else None


def extract(name, out_dir):
    gltf, buffer = load(f"{D}/{name}-rig.glb")
    index = base_colour_image(gltf)
    if index is None:
        return f"{name:9s} no embedded texture"

    image = gltf["images"][index]
    view = gltf["bufferViews"][image["bufferView"]]
    start = view.get("byteOffset", 0)
    data = buffer[start:start + view["byteLength"]]

    extension = ".jpg" if image.get("mimeType") == "image/jpeg" else ".png"
    path = os.path.join(out_dir, f"{name}{extension}")
    with open(path, "wb") as f:
        f.write(data)
    return f"{name:9s} {len(data) / 1024:7.0f} KB  {path}"


if __name__ == "__main__":
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    names = sys.argv[2:] or sorted(
        os.path.basename(p)[: -len("-rig.glb")] for p in glob.glob(f"{D}/*-rig.glb")
    )
    for n in names:
        print(extract(n, out_dir))
