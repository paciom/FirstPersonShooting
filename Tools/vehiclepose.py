"""Offline preview of the fold TransformRigForge bakes.

The forge runs inside Unity, which cannot be launched from the agent, so this
replays the same solve against the raw glTF and reports what shape each robot
ends up as. It is a sanity check on the POSE SPEC, not a substitute for looking
at it: what it can prove is that the silhouette gets lower and longer and stays
above the floor, which is the difference between a vehicle and a crumpled heap.

Keep VEHICLE_AIMS in step with TransformRigForge.VehicleAims.

  python vehiclepose.py            # every robot
  python vehiclepose.py ranger     # one
"""
import glob
import json
import math
import os
import struct
import sys

D = "D:/Claude/FirstPersongShooting/Assets/Models/Meshy"

# (bone, child, direction) in character space, +Z forward, +Y up.
VEHICLE_AIMS = [
    ("Hips",    "Spine02", (0.0,  0.25,  0.97)),
    ("Spine02", "Spine01", (0.0,  0.12,  0.99)),
    ("Spine01", "Spine",   (0.0,  0.05,  1.00)),
    ("Spine",   "neck",    (0.0, -0.15,  0.99)),
    ("neck",    "Head",    (0.0, -0.50,  0.87)),
    ("Head",    "head_end",(0.0, -0.80,  0.60)),
    ("LeftShoulder", "LeftArm",     (-0.90, -0.20, 0.39)),
    ("LeftArm",      "LeftForeArm", (-0.55, -0.35, 0.76)),
    ("LeftForeArm",  "LeftHand",    (-0.30, -0.15, 0.94)),
    ("LeftUpLeg", "LeftLeg",    (-0.15, -0.25, -0.95)),
    ("LeftLeg",   "LeftFoot",   ( 0.00, -0.55,  0.83)),
    ("LeftFoot",  "LeftToeBase",( 0.00, -0.15,  0.99)),
]


# ----------------------------------------------------------------- quaternions

def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def qconj(q):
    return (-q[0], -q[1], -q[2], q[3])


def qrot(q, v):
    x, y, z, w = q
    t = (2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0]))
    return (
        v[0] + w * t[0] + (y * t[2] - z * t[1]),
        v[1] + w * t[1] + (z * t[0] - x * t[2]),
        v[2] + w * t[2] + (x * t[1] - y * t[0]),
    )


def normalize(v):
    n = math.sqrt(sum(c * c for c in v))
    return tuple(c / n for c in v) if n > 1e-9 else v


def from_to(a, b):
    """Shortest rotation taking unit vector a onto unit vector b."""
    d = sum(x * y for x, y in zip(a, b))
    if d > 0.999999:
        return (0.0, 0.0, 0.0, 1.0)
    if d < -0.999999:
        axis = (1.0, 0.0, 0.0) if abs(a[0]) < 0.9 else (0.0, 1.0, 0.0)
        axis = normalize((a[1] * axis[2] - a[2] * axis[1],
                          a[2] * axis[0] - a[0] * axis[2],
                          a[0] * axis[1] - a[1] * axis[0]))
        return (axis[0], axis[1], axis[2], 0.0)
    axis = (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
    q = (axis[0], axis[1], axis[2], 1.0 + d)
    n = math.sqrt(sum(c * c for c in q))
    return tuple(c / n for c in q)


# ------------------------------------------------------------------- rig model

def load(path):
    with open(path, "rb") as f:
        struct.unpack("<3I", f.read(12))
        clen, _ = struct.unpack("<2I", f.read(8))
        return json.loads(f.read(clen).decode())


class Rig:
    def __init__(self, name):
        gltf = load(f"{D}/{name}-rig.glb")
        self.nodes = gltf["nodes"]
        self.by_name, self.parent, self.depth = {}, {}, {}

        def walk(index, parent, depth):
            node = self.nodes[index]
            self.by_name[node.get("name")] = index
            self.parent[index] = parent
            self.depth[index] = depth
            for child in node.get("children", []):
                walk(child, index, depth + 1)

        for root in gltf["scenes"][gltf.get("scene", 0)]["nodes"]:
            walk(root, None, 0)

        self.joints = [j for j in gltf["skins"][0]["joints"]]

    def local_t(self, i):
        return tuple(self.nodes[i].get("translation", (0.0, 0.0, 0.0)))

    def local_r(self, i):
        return tuple(self.nodes[i].get("rotation", (0.0, 0.0, 0.0, 1.0)))

    def local_s(self, i):
        return self.nodes[i].get("scale", (1.0, 1.0, 1.0))[0]

    def world_rot(self, i, overrides):
        if i is None:
            return (0.0, 0.0, 0.0, 1.0)
        if i in overrides:
            return overrides[i]
        return qmul(self.world_rot(self.parent[i], overrides), self.local_r(i))

    def positions(self, local_rot):
        """World position of every joint, given a local-rotation override map."""
        out = {}

        def walk(i, pos, rot, scale):
            t = self.local_t(i)
            here = tuple(p + c for p, c in zip(pos, qrot(rot, tuple(v * scale for v in t))))
            here_rot = qmul(rot, local_rot.get(i, self.local_r(i)))
            here_scale = scale * self.local_s(i)
            out[i] = here
            for child in self.nodes[i].get("children", []):
                walk(child, here, here_rot, here_scale)

        for root, parent in self.parent.items():
            if parent is None:
                walk(root, (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), 1.0)
        return out


def solve(rig):
    aims = []
    for bone, child, direction in VEHICLE_AIMS:
        aims.append((bone, child, direction))
        if bone.startswith("Left"):
            aims.append((
                "Right" + bone[4:],
                "Right" + child[4:] if child.startswith("Left") else child,
                (-direction[0], direction[1], direction[2]),
            ))

    # Which way does it face? Same test the forge uses.
    rest = rig.positions({})
    facing = 0.0
    for side in ("Left", "Right"):
        foot, toe = rig.by_name.get(f"{side}Foot"), rig.by_name.get(f"{side}ToeBase")
        if foot is not None and toe is not None:
            facing += rest[toe][2] - rest[foot][2]
    facing = 1.0 if facing >= 0 else -1.0

    aims.sort(key=lambda a: rig.depth.get(rig.by_name.get(a[0], -1), 1 << 30))

    world, local = {}, {}
    for bone, child, direction in aims:
        bi, ci = rig.by_name.get(bone), rig.by_name.get(child)
        if bi is None or ci is None or rig.parent[ci] != bi:
            continue
        parent_world = rig.world_rot(rig.parent[bi], world)
        unchanged = qmul(parent_world, rig.local_r(bi))
        current = normalize(qrot(unchanged, rig.local_t(ci)))
        target = normalize((direction[0], direction[1], direction[2] * facing))
        w = qmul(from_to(current, target), unchanged)
        world[bi] = w
        local[bi] = qmul(qconj(parent_world), w)
    return local, facing


def report(name):
    rig = Rig(name)
    local, facing = solve(rig)

    before = rig.positions({})
    after = rig.positions(local)

    def box(points):
        js = [p for i, p in points.items() if i in rig.joints]
        xs, ys, zs = zip(*js)
        return (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs), min(ys))

    bw, bh, bl, blow = box(before)
    aw, ah, al, alow = box(after)

    # The folded pose leaves the lowest joint hanging above the old floor line,
    # so the forge lowers the Hips by this much to set the chassis back down.
    # Same number, same sign as TransformRigForge's "hips drop" log line.
    drop = alow - blow
    verdict = []
    if ah > bh * 0.75:
        verdict.append("NOT LOW ENOUGH")
    if al < bl * 1.4:
        verdict.append("NOT LONG ENOUGH")
    status = "ok" if not verdict else "; ".join(verdict)

    print(f"{name:9s} facing {facing:+.0f}  "
          f"robot {bw:6.1f}w {bh:6.1f}h {bl:6.1f}l  ->  "
          f"vehicle {aw:6.1f}w {ah:6.1f}h {al:6.1f}l   "
          f"height {ah / bh:4.0%}  length x{al / max(bl, 1e-6):4.1f}  "
          f"hips drop {drop:5.2f}   {status}")


if __name__ == "__main__":
    names = sys.argv[1:] or sorted(
        os.path.basename(p)[: -len("-rig.glb")] for p in glob.glob(f"{D}/*-rig.glb")
    )
    for n in names:
        report(n)
