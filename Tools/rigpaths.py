"""Dumps the joint hierarchy of each Meshy -rig.glb as Unity-style animation
paths, then checks every robot agrees.

Why this matters: a Unity AnimationClip addresses bones by PATH relative to the
Animator's GameObject, not by name. If all nine robots share one path set, a
single forged transform clip drives the whole fleet; if they don't, the forge
has to emit one clip per robot from the same curve template.

  python rigpaths.py            # compare every robot, print the shared paths
  python rigpaths.py ranger     # dump one robot's tree in full
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
        return json.loads(f.read(clen).decode())


def paths(gltf):
    """Every node's path, keyed by node index, rooted at the scene's top node.

    glTFast recreates the glTF node tree one-for-one as GameObjects, and the
    forge puts the Animator on the imported root, so a node's Unity animation
    path is its glTF path minus that root's own name.
    """
    out = {}
    nodes = gltf["nodes"]
    scene_roots = gltf["scenes"][gltf.get("scene", 0)]["nodes"]

    def walk(index, prefix):
        name = nodes[index].get("name", f"node{index}")
        path = f"{prefix}/{name}" if prefix else name
        out[index] = path
        for child in nodes[index].get("children", []):
            walk(child, path)

    for root in scene_roots:
        walk(root, "")
    return out


def joint_paths(name):
    gltf = load(f"{D}/{name}-rig.glb")
    all_paths = paths(gltf)
    skin = gltf["skins"][0]
    return [all_paths[j] for j in skin["joints"]]


def strip_root(path):
    """Drop the leading root node — the Animator lives there, so it is not
    part of the animation path."""
    return path.split("/", 1)[1] if "/" in path else ""


if __name__ == "__main__":
    names = sys.argv[1:] or sorted(
        os.path.basename(p)[: -len("-rig.glb")] for p in glob.glob(f"{D}/*-rig.glb")
    )

    if len(names) == 1:
        gltf = load(f"{D}/{names[0]}-rig.glb")
        for index, path in sorted(paths(gltf).items(), key=lambda kv: kv[1]):
            mesh = " [mesh]" if "mesh" in gltf["nodes"][index] else ""
            print(f"{path}{mesh}")
        sys.exit(0)

    reference, baseline = None, None
    for name in names:
        jp = joint_paths(name)
        if baseline is None:
            reference, baseline = name, jp
            print(f"{name:9s} {len(jp)} joints (reference)")
            continue
        if jp == baseline:
            print(f"{name:9s} {len(jp)} joints  MATCHES {reference}")
        else:
            only_here = set(jp) - set(baseline)
            only_there = set(baseline) - set(jp)
            print(f"{name:9s} {len(jp)} joints  DIFFERS from {reference}")
            for p in sorted(only_here):
                print(f"            + {p}")
            for p in sorted(only_there):
                print(f"            - {p}")

    print("\n--- animation paths (root stripped) ---")
    for p in baseline:
        print(strip_root(p))
