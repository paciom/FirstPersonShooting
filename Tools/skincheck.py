"""Rig QA gate #2: does the auto-rigger's SKIN BINDING hold up?

rigcheck.py answers "are the joints in the right places". This answers the
other half — "is each vertex bound to the right joints" — which nothing
checked until titan's 2026-08-01 re-rig shipped a mesh whose thigh vertices
were strongest-bound to ARM bones. The weights were formally perfect (sum to
1, indices in range) so no tool complained, and because a rig's rest pose IS
its bind pose the model looked fine standing still. It only smeared into
taffy once a clip posed it — after 451 credits of animation had been
generated on top of it.

THE MEASURE. For each vertex, the distance to the bind-pose position of its
strongest-weighted joint. Bulky robots score higher than slim ones purely
from armour thickness, so an absolute threshold is useless (titan's honest
p50 is 0.32 where ranger's is 0.22). Instead each re-rigged Fight model is
compared against THAT SAME ROBOT's original -rig.glb, which shares the
geometry and is known good — so bulk cancels out and only binding quality
remains. A p95 ratio near 1.0 is healthy; titan's was 1.69.

  python skincheck.py                # audit the whole Fight fleet
  python skincheck.py titan hawk     # just these
  python skincheck.py --file path.glb   # raw profile of any skinned glb
"""
import math
import os
import struct
import sys

D = "D:/Claude/FirstPersongShooting/Assets/Models/Meshy"

ROBOTS = ("ranger", "titan", "scout", "hawk", "bolt",
          "samurai", "panther", "knight", "racer")

# A re-rig whose p95 exceeds its own original rig's by more than this is
# reporting a different binding, not a slightly different one.
RATIO_LIMIT = 1.35


def load(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, _, _ = struct.unpack_from("<3I", data, 0)
    assert magic == 0x46546C67, f"not a glb: {path}"
    json_len, _ = struct.unpack_from("<2I", data, 12)
    import json as _json
    gltf = _json.loads(data[20:20 + json_len].decode())
    return gltf, data, 20 + json_len + 8


def offset_of(gltf, index, bin_start):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    return acc, bin_start + view.get("byteOffset", 0) + acc.get("byteOffset", 0)


def profile(path):
    """(p50, p95, max) vertex-to-strongest-joint distance, plus vertex count."""
    gltf, data, bin_start = load(path)
    skin = gltf["skins"][0]

    # Bind positions: translation of the inverse of each inverseBindMatrix.
    acc, off = offset_of(gltf, skin["inverseBindMatrices"], bin_start)
    bind = []
    for slot in range(len(skin["joints"])):
        m = struct.unpack_from("<16f", data, off + slot * 64)
        rot = [[m[0], m[4], m[8]], [m[1], m[5], m[9]], [m[2], m[6], m[10]]]
        t = [m[12], m[13], m[14]]
        scale_sq = sum(rot[i][0] ** 2 for i in range(3))
        bind.append([-sum(rot[k][i] * t[k] for k in range(3)) / scale_sq
                     for i in range(3)])

    prim = next(m["primitives"][0] for m in gltf["meshes"]
                if "JOINTS_0" in m["primitives"][0]["attributes"])
    pa, poff = offset_of(gltf, prim["attributes"]["POSITION"], bin_start)
    ja, joff = offset_of(gltf, prim["attributes"]["JOINTS_0"], bin_start)
    wa, woff = offset_of(gltf, prim["attributes"]["WEIGHTS_0"], bin_start)
    jfmt, jsize = {5121: ("<4B", 4), 5123: ("<4H", 8)}[ja["componentType"]]

    dists = []
    for i in range(pa["count"]):
        p = struct.unpack_from("<3f", data, poff + i * 12)
        jj = struct.unpack_from(jfmt, data, joff + i * jsize)
        ww = struct.unpack_from("<4f", data, woff + i * 16)
        k = max(range(4), key=lambda kk: ww[kk])
        dists.append(math.dist(p, bind[jj[k]]))

    # Scale-normalise: a re-rig may be built at a different height_meters.
    height = max(struct.unpack_from("<3f", data, poff + i * 12)[1]
                 for i in range(pa["count"]))
    dists = [d / height for d in dists] if height > 1e-3 else dists
    dists.sort()
    n = len(dists)
    return dists[n // 2], dists[int(n * 0.95)], dists[-1], n


def audit(names):
    print(f"{'robot':10s} {'file':16s} {'p50':>6s} {'p95':>6s} {'max':>6s}  "
          f"{'p95 vs own -rig':>16s}")
    bad = []
    for robot in names:
        base = f"{D}/{robot}-rig.glb"
        if not os.path.exists(base):
            print(f"{robot:10s} no original -rig.glb — cannot compare")
            continue
        b50, b95, bmax, _ = profile(base)
        print(f"{robot:10s} {'-rig.glb':16s} {b50:6.3f} {b95:6.3f} {bmax:6.3f}  "
              f"{'(reference)':>16s}")

        for suffix in ("walking", "rigged"):
            target = f"{D}/Fight/{robot}-{suffix}.glb"
            if not os.path.exists(target):
                continue
            t50, t95, tmax, _ = profile(target)
            ratio = t95 / b95 if b95 > 1e-6 else 0.0
            flag = "  <-- MISBOUND" if ratio > RATIO_LIMIT else ""
            print(f"{'':10s} {suffix + '.glb':16s} {t50:6.3f} {t95:6.3f} {tmax:6.3f}  "
                  f"{ratio:15.2f}x{flag}")
            if ratio > RATIO_LIMIT:
                bad.append(f"{robot}-{suffix}")
    print()
    if bad:
        print("BROKEN SKIN BINDING: " + ", ".join(bad))
        print("Fix with:  python fixskinweights.py <robot>   "
              "(transplants the original rig's weights)")
    else:
        print("All re-rigged models bind like their originals.")
    return bad


if __name__ == "__main__":
    if "--file" in sys.argv:
        path = sys.argv[sys.argv.index("--file") + 1]
        p50, p95, pmax, n = profile(path)
        print(f"{path}: verts {n}  p50 {p50:.3f}  p95 {p95:.3f}  max {pmax:.3f}")
        sys.exit(0)
    names = [a for a in sys.argv[1:] if not a.startswith("-")] or list(ROBOTS)
    sys.exit(1 if audit(names) else 0)
