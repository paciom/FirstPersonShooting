"""Repairs a re-rigged model's skin weights by transplanting them from that
robot's ORIGINAL rig.

WHEN YOU NEED THIS. Meshy's auto-rigger is a lottery, and skincheck.py is how
you find out you lost: titan's 2026-08-01 re-rig bound thigh vertices to ARM
bones. The weights were formally valid — summing to 1, indices in range — so
nothing errored, and since a rig's rest pose IS its bind pose the model looked
correct standing still. Every posed clip smeared it into taffy.

WHY A TRANSPLANT WORKS. The Fight models are re-rigs of geometry that was
already rigged well in July: meshyfight.py uploads a geometry-only rebuild of
<robot>-rig.glb, so the vertices are the same vertices. Both rigs use the same
Mixamo-style joint names. So the old rig's per-vertex weights fit the new mesh
one-for-one and address the new skeleton by name — no re-spend, no re-download.

WHAT CHANGES. Only the JOINTS_0 / WEIGHTS_0 bytes. Geometry, skeleton,
bindposes and animations are untouched, so every Fight/<robot>-*.glb clip keeps
playing on the same skeleton. The original is backed up to RetiredModels/
(gitignored) and the patch aborts itself if the p95 ratio does not improve.

  python fixskinweights.py titan          # patch its walking + rigged models
  python fixskinweights.py titan --dry    # report only, write nothing
"""
import math
import os
import shutil
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from skincheck import load, offset_of, profile   # noqa: E402

ROOT = "D:/Claude/FirstPersongShooting"
D = f"{ROOT}/Assets/Models/Meshy"
BACKUP_DIR = f"{ROOT}/RetiredModels"

# A transplant must land at least this close to the donor's own p95 ratio.
RATIO_LIMIT = 1.35


def skin_arrays(path):
    """(joint names by slot, positions, joints, weights, write offsets)."""
    gltf, data, bin_start = load(path)
    skin = gltf["skins"][0]
    names = [gltf["nodes"][j].get("name") for j in skin["joints"]]

    prim = next(m["primitives"][0] for m in gltf["meshes"]
                if "JOINTS_0" in m["primitives"][0]["attributes"])
    for attr in ("POSITION", "JOINTS_0", "WEIGHTS_0"):
        view = gltf["bufferViews"][gltf["accessors"][prim["attributes"][attr]]["bufferView"]]
        assert not view.get("byteStride"), \
            f"{path}: interleaved {attr} — this patcher assumes tight packing"

    pa, poff = offset_of(gltf, prim["attributes"]["POSITION"], bin_start)
    ja, joff = offset_of(gltf, prim["attributes"]["JOINTS_0"], bin_start)
    wa, woff = offset_of(gltf, prim["attributes"]["WEIGHTS_0"], bin_start)
    assert wa["componentType"] == 5126, f"{path}: expected float weights"
    jfmt, jsize = {5121: ("<4B", 4), 5123: ("<4H", 8)}[ja["componentType"]]

    n = pa["count"]
    pos = [struct.unpack_from("<3f", data, poff + i * 12) for i in range(n)]
    joints = [struct.unpack_from(jfmt, data, joff + i * jsize) for i in range(n)]
    weights = [struct.unpack_from("<4f", data, woff + i * 16) for i in range(n)]
    return names, pos, joints, weights, (data, joff, jfmt, jsize, woff)


def nearest_map(src_pos, dst_pos, cell=0.05):
    """dst index -> nearest src index, via a coarse spatial hash."""
    grid = {}
    for i, p in enumerate(src_pos):
        grid.setdefault((int(p[0] / cell), int(p[1] / cell), int(p[2] / cell)), []).append(i)

    out, worst = [], 0.0
    for p in dst_pos:
        kx, ky, kz = int(p[0] / cell), int(p[1] / cell), int(p[2] / cell)
        best, best_d = -1, 1e9
        for reach in (1, 3):    # widen only when the tight ring comes up empty
            for dx in range(-reach, reach + 1):
                for dy in range(-reach, reach + 1):
                    for dz in range(-reach, reach + 1):
                        for i in grid.get((kx + dx, ky + dy, kz + dz), ()):
                            d = math.dist(p, src_pos[i])
                            if d < best_d:
                                best, best_d = i, d
            if best >= 0:
                break
        assert best >= 0, "no donor vertex found — do these meshes overlap?"
        worst = max(worst, best_d)
        out.append(best)
    return out, worst


def patch(robot, suffix, dry):
    donor_path = f"{D}/{robot}-rig.glb"
    target_path = f"{D}/Fight/{robot}-{suffix}.glb"
    if not os.path.exists(target_path):
        return None

    _, base_p95, _, _ = profile(donor_path)
    _, before_p95, _, _ = profile(target_path)
    before_ratio = before_p95 / base_p95
    print(f"{robot}-{suffix}: p95 {before_p95:.3f} vs donor {base_p95:.3f} "
          f"= {before_ratio:.2f}x")

    # Idempotent: a model that already binds like its donor needs nothing, so
    # re-running over a fixed fleet is a no-op rather than an error.
    if before_ratio <= RATIO_LIMIT:
        print(f"{robot}-{suffix}: already binds healthily — skipped")
        return before_ratio

    dnames, dpos, djoints, dweights, _ = skin_arrays(donor_path)
    tnames, tpos, tjoints, tweights, (tdata, joff, jfmt, jsize, woff) = \
        skin_arrays(target_path)

    assert sorted(dnames) == sorted(tnames), "joint name sets differ — unsafe"
    slot_map = [tnames.index(name) for name in dnames]   # donor slot -> target slot

    # A re-rig may be built at a different height_meters (titan: 1.7 vs 1.8),
    # leaving the meshes a uniform scale apart. Weights are scale-free, so
    # fold the donor into the target's scale before matching vertices.
    ratios = []
    for axis in range(3):
        dspan = max(p[axis] for p in dpos) - min(p[axis] for p in dpos)
        tspan = max(p[axis] for p in tpos) - min(p[axis] for p in tpos)
        if dspan > 1e-4:
            ratios.append(tspan / dspan)
    scale = sum(ratios) / len(ratios)
    assert max(ratios) - min(ratios) < 0.01, f"non-uniform scale {ratios} — refusing"
    scaled = [(p[0] * scale, p[1] * scale, p[2] * scale) for p in dpos]

    mapping, worst_gap = nearest_map(scaled, tpos)
    print(f"{robot}-{suffix}: donor rescaled x{scale:.4f}, "
          f"{len(dpos)} -> {len(tpos)} verts, worst gap {worst_gap:.4f} m")
    assert worst_gap < 0.02, "meshes do not line up — refusing to transplant"

    patched = bytearray(tdata)
    for i, src in enumerate(mapping):
        struct.pack_into(jfmt, patched, joff + i * jsize,
                         *[slot_map[j] for j in djoints[src]])
        struct.pack_into("<4f", patched, woff + i * 16, *dweights[src])

    tmp = target_path + ".patched"
    with open(tmp, "wb") as handle:
        handle.write(bytes(patched))
    _, after_p95, _, _ = profile(tmp)
    ratio = after_p95 / base_p95
    print(f"{robot}-{suffix}: p95 after {after_p95:.3f} = {ratio:.2f}x donor")

    if dry:
        os.remove(tmp)
        print(f"{robot}-{suffix}: dry run — nothing written")
        return ratio
    if ratio >= before_ratio or ratio > RATIO_LIMIT:
        os.remove(tmp)
        raise SystemExit(f"{robot}-{suffix}: transplant did not land "
                         f"({ratio:.2f}x) — file left untouched")

    os.makedirs(BACKUP_DIR, exist_ok=True)
    backup = f"{BACKUP_DIR}/{robot}-{suffix}.pre-weight-fix.glb"
    if not os.path.exists(backup):
        shutil.copyfile(target_path, backup)
    os.replace(tmp, target_path)
    print(f"{robot}-{suffix}: PATCHED (original at {backup})")
    return ratio


if __name__ == "__main__":
    names = [a for a in sys.argv[1:] if not a.startswith("-")]
    if not names:
        raise SystemExit(__doc__)
    dry = "--dry" in sys.argv
    for robot in names:
        for suffix in ("walking", "rigged"):
            patch(robot, suffix, dry)
