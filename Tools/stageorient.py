"""Where does each stop-motion stage actually FACE? Run this before wiring a
new jet (or other craft) set into JetPawn.

A generated set does NOT share one frame: Meshy normalizes each reconstruction
to its own canonical front, and mid-set — at the frame where it stops reading
the image as a creature and starts reading it as a craft — the canonical flips
a quarter turn. JetPawn therefore carries a per-set ROBOT-FRAMED STAGE COUNT
(JetRobotFramedStages / TankRobotFramedStages), and this probe is how that
number is measured: the robot-framed stages read toes ~180 / head ~0 (facing
0), the craft-framed ones read head ~+90 (facing -90). Ranger's sets measured
jet = 5, tank = 4 (2026-08-04).

Unlike a translations-only walk (whose frame is nobody's), this composes
translation + rotation + scale down the node tree, so angles are in the glTF
file's own frame; glTFast's import then MIRRORS the yaw (theta_unity =
-theta_gltf), pinned by two calibration points — the flight-confirmed jet
nose and the tank's barrel markers. Signals per stage:
  - toes yaw (feet-centroid offset, bottom 15%) — a humanoid's forward is
    opposite its kicked-back feet; trust it when its magnitude m is large
  - head yaw (top 15% centroid offset) — the head leans INTO the facing on
    fold stages; on craft stages the "head" is the tail fins, i.e. the REAR
  - taper yaw — thin end of the long horizontal axis; only meaningful when
    the long axis is actually the fuselage (a delta wing's is its span)

  python Tools/stageorient.py            # both ranger sets, as measured
"""
import json
import math
import struct
import sys

sys.path.insert(0, "D:/Claude/FirstPersongShooting/Tools")
import stopmotion as S


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def qrot(q, v):
    qv = (v[0], v[1], v[2], 0.0)
    qc = (-q[0], -q[1], -q[2], q[3])
    r = qmul(qmul(q, qv), qc)
    return (r[0], r[1], r[2])


def walk(gltf, blob, want_markers=False):
    verts = []
    markers = {}

    def accessor(index):
        acc = gltf["accessors"][index]
        view = gltf["bufferViews"][acc["bufferView"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        stride = view.get("byteStride", 12)
        for i in range(acc["count"]):
            yield struct.unpack_from("<3f", blob, start + i * stride)

    def rec(index, t, q, s):
        node = gltf["nodes"][index]
        nt = node.get("translation", [0, 0, 0])
        nq = node.get("rotation", [0, 0, 0, 1])
        ns = node.get("scale", [1, 1, 1])
        # compose: world = T * R * S applied to child locals
        def xform(v):
            v = (v[0] * ns[0], v[1] * ns[1], v[2] * ns[2])
            v = qrot(tuple(nq), v)
            return (v[0] + nt[0], v[1] + nt[1], v[2] + nt[2])
        # parent transform closure
        def full(v):
            v = xform(v)
            v = (v[0] * s[0], v[1] * s[1], v[2] * s[2])
            v = qrot(q, v)
            return (v[0] + t[0], v[1] + t[1], v[2] + t[2])
        wt = full((0, 0, 0))
        # child accumulators: world rotation q*nq, world scale s*ns (uniform-ish)
        wq = qmul(q, tuple(nq))
        ws = (s[0] * ns[0], s[1] * ns[1], s[2] * ns[2])
        name = node.get("name", "")
        if want_markers and (name.startswith("TurretPivot") or name.startswith("TurretMuzzle")):
            markers[name.split("_")[0] if False else name] = wt
        if "mesh" in node:
            for prim in gltf["meshes"][node["mesh"]]["primitives"]:
                for v in accessor(prim["attributes"]["POSITION"]):
                    verts.append(full(v))
        for child in node.get("children", []):
            rec(child, wt, wq, ws)

    scene = gltf["scenes"][gltf.get("scene", 0)]
    for index in scene["nodes"]:
        rec(index, (0, 0, 0), (0, 0, 0, 1), (1, 1, 1))
    return verts, markers


def yaw_of(dx, dz):
    """Angle in degrees where 0 = +Z, 90 = +X (Unity-style atan2(x, z))."""
    return math.degrees(math.atan2(dx, dz))


def analyse(path, markers=False):
    gltf, blob = S.parse(path)
    verts, marks = walk(gltf, blob, markers)
    lo = [min(v[k] for v in verts) for k in range(3)]
    hi = [max(v[k] for v in verts) for k in range(3)]
    size = [hi[k] - lo[k] for k in range(3)]
    cx = sum(v[0] for v in verts) / len(verts)
    cz = sum(v[2] for v in verts) / len(verts)

    out = {"size": size}

    # toes: bottom 15% centroid offset from body centroid
    cut = lo[1] + 0.15 * size[1]
    feet = [v for v in verts if v[1] < cut]
    if feet:
        fx = sum(v[0] for v in feet) / len(feet) - cx
        fz = sum(v[2] for v in feet) / len(feet) - cz
        out["toes"] = (yaw_of(fx, fz), math.hypot(fx, fz))

    # head: top 15%
    cut = hi[1] - 0.15 * size[1]
    head = [v for v in verts if v[1] > cut]
    if head:
        hx = sum(v[0] for v in head) / len(head) - cx
        hz = sum(v[2] for v in head) / len(head) - cz
        out["head"] = (yaw_of(hx, hz), math.hypot(hx, hz))

    # taper on long horizontal axis
    axis = 0 if size[0] >= size[2] else 2
    lo_end = [v for v in verts if v[axis] < lo[axis] + 0.18 * size[axis]]
    hi_end = [v for v in verts if v[axis] > hi[axis] - 0.18 * size[axis]]

    def spread(vs):
        if not vs:
            return 1e9
        r = 1.0
        for k in range(3):
            if k == axis:
                continue
            m = sum(v[k] for v in vs) / len(vs)
            r *= (sum((v[k] - m) ** 2 for v in vs) / len(vs)) ** 0.5
        return r

    nose_positive = spread(hi_end) < spread(lo_end)
    nose_vec = [0, 0, 0]
    nose_vec[axis] = 1 if nose_positive else -1
    out["taper"] = yaw_of(nose_vec[0], nose_vec[2])

    if marks:
        pivot = next((p for n, p in marks.items() if n.startswith("TurretPivot")), None)
        tip = next((p for n, p in marks.items() if n.startswith("TurretMuzzle")), None)
        if pivot and tip:
            out["barrel"] = yaw_of(tip[0] - pivot[0], tip[2] - pivot[2])
    return out


ROOT = "D:/Claude/FirstPersongShooting/Assets/Models/Stages"
print("=== ranger-jet set ===")
for i in range(1, 9):
    r = analyse(f"{ROOT}/ranger-jet/stage{i}.glb")
    s = r["size"]
    line = f"stage{i}  size {s[0]:.2f} {s[1]:.2f} {s[2]:.2f} "
    line += f" toes {r['toes'][0]:7.1f} (m {r['toes'][1]:.3f})" if "toes" in r else ""
    line += f" head {r['head'][0]:7.1f} (m {r['head'][1]:.3f})" if "head" in r else ""
    line += f" taper {r['taper']:7.1f}"
    print(line)

print("=== ranger tank set (calibration) ===")
for i in (1, 8):
    r = analyse(f"{ROOT}/ranger/stage{i}.glb", markers=(i == 8))
    s = r["size"]
    line = f"stage{i}  size {s[0]:.2f} {s[1]:.2f} {s[2]:.2f} "
    line += f" toes {r['toes'][0]:7.1f} (m {r['toes'][1]:.3f})" if "toes" in r else ""
    line += f" taper {r['taper']:7.1f}"
    if "barrel" in r:
        line += f"  BARREL {r['barrel']:7.1f}"
    print(line)
