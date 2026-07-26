"""Rig QA gate: reads a Meshy -rig.glb and checks the auto-rigger actually put
the skeleton where the limbs are.

The failure this catches (hit on bolt v1): anything wing-like at shoulder height
gets bound AS the arm, so the arm chain rises instead of hanging. Bind positions
come from the skin's inverseBindMatrices, which is the only exact source — you
cannot get them by summing node translations, since the joints carry rotations.

  python rigcheck.py [robot ...]
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


def accessor(g, b, index, comps):
    a = g["accessors"][index]
    bv = g["bufferViews"][a["bufferView"]]
    off = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    v = struct.unpack_from("<" + "f" * (a["count"] * comps), b, off)
    return [v[i * comps:(i + 1) * comps] for i in range(a["count"])]


def bind_position(m):
    """Translation of inverse(m), for a column-major matrix with uniform scale."""
    rot = [[m[0], m[4], m[8]], [m[1], m[5], m[9]], [m[2], m[6], m[10]]]
    t = [m[12], m[13], m[14]]
    scale_sq = sum(rot[i][0] ** 2 for i in range(3))
    return [-sum(rot[k][i] * t[k] for k in range(3)) / scale_sq for i in range(3)]


def check(name):
    path = f"{D}/{name}-rig.glb"
    if not os.path.exists(path):
        return f"{name:9s} MISSING {path}"
    g, b = load(path)
    skin = g["skins"][0]
    ibms = accessor(g, b, skin["inverseBindMatrices"], 16)
    pos = {g["nodes"][j].get("name"): bind_position(m) for j, m in zip(skin["joints"], ibms)}

    problems = []
    for side in ("Left", "Right"):
        shoulder, elbow, hand = (pos[f"{side}{p}"][1] for p in ("Arm", "ForeArm", "Hand"))
        if not (elbow < shoulder - 0.05 and hand < elbow - 0.05):
            problems.append(f"{side} arm rises (shoulder {shoulder:.2f} > elbow {elbow:.2f} > hand {hand:.2f})")
        hip, knee, foot = (pos[f"{side}{p}"][1] for p in ("UpLeg", "Leg", "Foot"))
        if not (knee < hip - 0.05 and foot < knee - 0.05):
            problems.append(f"{side} leg wrong (hip {hip:.2f} knee {knee:.2f} foot {foot:.2f})")
        if pos[f"{side}Foot"][1] > 0.45:
            problems.append(f"{side} foot too high ({pos[f'{side}Foot'][1]:.2f})")
    if pos["Head"][1] < pos["Hips"][1]:
        problems.append("head below hips")

    problems += loop_problems(name)

    verdict = "OK" if not problems else "BROKEN: " + "; ".join(problems)
    arm = " ".join(f"{pos['Left'+p][1]:.2f}" for p in ("Arm", "ForeArm", "Hand"))
    return f"{name:9s} left-arm y {arm}   {verdict}"


def loop_problems(name, tolerance_cm=0.5):
    """Root motion check: the hips must end a cycle where they started, or the
    body slides out of its collider and snaps back every loop. NOTE the units —
    the Armature node carries scale 0.01, so hips translation is in CENTIMETRES.
    Comparing these against a metre-scale epsilon reports false failures.
    """
    out = []
    for kind in ("walk", "run"):
        path = f"{D}/{name}-{kind}.glb"
        if not os.path.exists(path):
            continue
        g, b = load(path)
        anim = g["animations"][0]
        for ch in anim["channels"]:
            node = g["nodes"][ch["target"]["node"]]
            if node.get("name") != "Hips" or ch["target"]["path"] != "translation":
                continue
            v = accessor(g, b, anim["samplers"][ch["sampler"]]["output"], 3)
            drift = max(abs(v[-1][k] - v[0][k]) for k in range(3))
            if drift > tolerance_cm:
                out.append(f"{kind} has root motion ({drift:.1f}cm hip drift per cycle)")
    return out


if __name__ == "__main__":
    names = sys.argv[1:] or sorted(
        os.path.basename(p)[:-len("-rig.glb")] for p in glob.glob(f"{D}/*-rig.glb")
    )
    for n in names:
        print(check(n))
