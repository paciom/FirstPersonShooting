"""Dumps each rig's REST POSE — the local TRS every joint sits at in the bind
pose — and reports how far the nine robots differ.

This decides whether one forged transform clip can drive the whole fleet.
Matching joint PATHS (see rigpaths.py) is not enough: an animation curve writes
an ABSOLUTE local rotation, so a clip authored as "rest * delta" only transfers
between robots whose rest rotations agree. Where they disagree, the forge has
to bake per-robot clips from the same delta template.

  python rigrest.py             # cross-robot comparison summary
  python rigrest.py ranger      # one robot's rest pose, in full
"""
import glob
import json
import math
import os
import struct
import sys

D = "D:/Claude/FirstPersongShooting/Assets/Models/Meshy"


def load(path):
    with open(path, "rb") as f:
        struct.unpack("<3I", f.read(12))
        clen, _ = struct.unpack("<2I", f.read(8))
        return json.loads(f.read(clen).decode())


def rest(name):
    """{joint path (root stripped): (translation, rotation quaternion)}."""
    gltf = load(f"{D}/{name}-rig.glb")
    nodes = gltf["nodes"]
    out = {}

    def walk(index, prefix):
        node = nodes[index]
        path = f"{prefix}/{node.get('name')}" if prefix else node.get("name")
        out[path] = (
            tuple(node.get("translation", (0.0, 0.0, 0.0))),
            tuple(node.get("rotation", (0.0, 0.0, 0.0, 1.0))),
        )
        for child in node.get("children", []):
            walk(child, path)

    for root in gltf["scenes"][gltf.get("scene", 0)]["nodes"]:
        walk(root, "")
    return out


def angle_between(q1, q2):
    """Degrees between two unit quaternions."""
    dot = min(1.0, abs(sum(a * b for a, b in zip(q1, q2))))
    return math.degrees(2.0 * math.acos(dot))


if __name__ == "__main__":
    names = sys.argv[1:] or sorted(
        os.path.basename(p)[: -len("-rig.glb")] for p in glob.glob(f"{D}/*-rig.glb")
    )

    if len(names) == 1:
        for path, (t, r) in sorted(rest(names[0]).items()):
            print(f"{path:70s} t=({t[0]:8.3f},{t[1]:8.3f},{t[2]:8.3f})  "
                  f"r=({r[0]:6.3f},{r[1]:6.3f},{r[2]:6.3f},{r[3]:6.3f})")
        sys.exit(0)

    poses = {n: rest(n) for n in names}
    reference = names[0]
    shared = set.intersection(*(set(p) for p in poses.values()))

    print(f"reference: {reference}\n")
    worst = []
    for path in sorted(shared):
        if "/" not in path:
            continue
        angles = [angle_between(poses[reference][path][1], poses[n][path][1])
                  for n in names[1:]]
        lengths = [poses[n][path][0][1] for n in names]   # y of translation
        spread = max(lengths) - min(lengths)
        worst.append((max(angles), spread, path))

    worst.sort(reverse=True)
    print(f"{'joint':60s} {'max rot diff':>13s} {'bone-len spread':>16s}")
    for angle, spread, path in worst:
        print(f"{path.split('/', 1)[1]:60s} {angle:12.2f}° {spread:15.3f}")

    print(f"\nlargest rest-rotation difference across the fleet: {worst[0][0]:.2f}°")
