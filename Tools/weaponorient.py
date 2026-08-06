"""Which way does each generated weapon GLB point once WeaponArt has
normalized it? Run after a meshyweapons.py batch, before trusting the
viewmodel.

WeaponArt.NormalizeAlongZ turns the longest axis down +Z but concedes that
"which END is the muzzle is still a coin flip". This probe settles the flip
per file: a gun is fat at the receiver/grip end and thin at the muzzle, so
after aligning the long axis to Z we compare the radial spread of the two
ends. The thin end is the muzzle; if it lands at -Z the gun appears
backwards in the player's hands.

Reproduces the exact runtime path:
  - glTFast import: unity = (-x, y, z) of the glTF vertex
  - NormalizeAlongZ: X-longest -> Euler(0,-90,0) (maps +X to +Z, matching
    BuildBlaster), Y-longest -> Euler(90,0,0) (maps +Y to +Z),
    Z-longest -> identity

  python Tools/weaponorient.py           # verdict per weapon + tally
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import stopmotion as S
from stageorient import walk

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "Assets", "Resources", "Weapons")

END_SLICE = 0.25     # how much of the length counts as an "end"


def final_frame(verts):
    """Vertices as WeaponArt leaves them: Unity import + long axis down Z."""
    verts = [(-x, y, z) for (x, y, z) in verts]
    lo = [min(v[k] for v in verts) for k in range(3)]
    hi = [max(v[k] for v in verts) for k in range(3)]
    size = [hi[k] - lo[k] for k in range(3)]

    if size[0] > size[2] and size[0] > size[1]:
        # Euler(0,-90,0): +X -> +Z, +Z -> -X
        return [(-z, y, x) for (x, y, z) in verts], "x"
    if size[1] > size[2] and size[1] > size[0]:
        # Euler(90,0,0): +Y -> +Z, +Z -> -Y
        return [(x, -z, y) for (x, y, z) in verts], "y"
    return verts, "z"


def spread(vs, cx, cy):
    """Radial thickness of an end slice, robust to a stray sight post."""
    if not vs:
        return 0.0
    radii = sorted(math.hypot(x - cx, y - cy) for (x, y, _) in vs)
    return radii[int(0.95 * (len(radii) - 1))]


def analyse(path):
    gltf, blob = S.parse(path)
    raw, _ = walk(gltf, blob)
    verts, axis = final_frame(raw)

    zs = [v[2] for v in verts]
    lo, hi = min(zs), max(zs)
    length = hi - lo
    cx = sum(v[0] for v in verts) / len(verts)
    cy = sum(v[1] for v in verts) / len(verts)

    front = [v for v in verts if v[2] > hi - END_SLICE * length]
    back = [v for v in verts if v[2] < lo + END_SLICE * length]
    front_r = spread(front, cx, cy)
    back_r = spread(back, cx, cy)

    # Thin end forward = muzzle forward = correct.
    ok = front_r < back_r
    margin = abs(front_r - back_r) / max(front_r, back_r, 1e-6)
    return ok, margin, axis


def main():
    files = sorted(f for f in os.listdir(ROOT) if f.endswith(".glb"))
    backwards = []
    for name in files:
        ok, margin, axis = analyse(os.path.join(ROOT, name))
        verdict = "ok      " if ok else "BACKWARD"
        flag = "  (weak)" if margin < 0.10 else ""
        print(f"{verdict}  axis={axis}  margin={margin:4.0%}  {name}{flag}")
        if not ok:
            backwards.append(name)
    print(f"\n{len(backwards)}/{len(files)} backwards")


if __name__ == "__main__":
    main()
