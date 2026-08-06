"""Bake a 180-degree turn into the weapon GLBs that Meshy generated facing
the wrong way, so every file obeys ONE runtime convention (muzzle forward
under WeaponArt.NormalizeAlongZ's -90 yaw — the same sign BuildBlaster uses).

Tools/weaponorient.py is the probe that finds them; this is the fix. It
wraps the scene's root nodes in one new node carrying the rotation, which
touches nothing else in the file — geometry, textures and the BIN chunk are
byte-identical. Run the probe again afterwards: it should report 60/60 ok.

  python Tools/weaponflip.py       # flips the known outlier list in place
"""
import json
import os
import struct
import sys

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "Assets", "Resources", "Weapons")

YAW180 = [0.0, 1.0, 0.0, 0.0]    # about Y: for barrel-along-X guns
ROLL180 = [0.0, 0.0, 1.0, 0.0]   # about Z: keeps a vertical (Y-long) prop upright

# The 2026-08-06 batch's outliers, measured by weaponorient.py.
FLIPS = {
    "bubble-blower.glb": YAW180,
    "drumline-cannon.glb": YAW180,
    "frostbite-beam.glb": YAW180,
    "ricochet-disc.glb": YAW180,
    "siege-torpedo.glb": YAW180,
    "time-bubble-bomb.glb": YAW180,
    "repulsor-palm.glb": ROLL180,
}

WRAPPER = "OrientFix"


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, _, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, f"{path}: not a GLB"
    offset, chunks = 12, []
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        chunks.append((kind, data[offset + 8:offset + 8 + length]))
        offset += 8 + length
    return chunks


def write_glb(path, chunks):
    body = b""
    for kind, payload in chunks:
        pad = (4 - len(payload) % 4) % 4
        payload += (b" " if kind == 0x4E4F534A else b"\x00") * pad
        body += struct.pack("<II", len(payload), kind) + payload
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body)


def flip(path, rotation):
    chunks = read_glb(path)
    gltf = json.loads(chunks[0][1])

    scene = gltf["scenes"][gltf.get("scene", 0)]
    nodes = gltf["nodes"]
    if any(nodes[i].get("name") == WRAPPER for i in scene["nodes"]):
        print(f"already flipped  {os.path.basename(path)}")
        return

    nodes.append({"name": WRAPPER, "rotation": rotation,
                  "children": scene["nodes"]})
    scene["nodes"] = [len(nodes) - 1]

    payload = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    chunks[0] = (chunks[0][0], payload)
    write_glb(path, chunks)
    print(f"flipped          {os.path.basename(path)}")


def main():
    for name, rotation in FLIPS.items():
        path = os.path.join(ROOT, name)
        if not os.path.exists(path):
            sys.exit(f"missing: {path}")
        flip(path, rotation)


if __name__ == "__main__":
    main()
