"""Splits a tank stage into a hull and a turret that can turn on its own.

The vehicle form is a single generated mesh -- one node, one primitive, one
material -- so the barrel points wherever the chassis points and a tank can only
shoot the way it drives. Every real tank turns its turret instead, which is the
whole reason a tank reads as a tank rather than as a gun on wheels.

Nothing in the pipeline can produce the two pieces separately: image-to-3D gives
back one closed shell, and generating a turret on its own would be a different
model that happens to sit nearby. So the shell is CUT.

WHERE THE CUT GOES. Measured, not authored. The horizontal cross-section of
every one of these tanks collapses at the deck -- full hull width below it,
turret width above -- so the tool scans cross-sections up the model and cuts at
the first height where the section is narrower than <see cref="RING_FRACTION"/>
of the widest one. Two robots wear their turret on a pedestal above a shaped
canopy, where the first collapse is bodywork rather than the ring; those name
their own height in <see cref="CUT_OVERRIDE"/>, as a fraction of model height.

WHY THE CUT IS A REAL SLICE. Triangles that straddle the plane are split rather
than assigned whole: a jagged seam shows as a ring of holes the moment the
turret turns a few degrees. Both openings are then capped, because the material
is double-sided -- an uncapped hull would show its own inside walls through the
gap the turret leaves when it turns.

Everything above the plane that is NOT connected to the turret (aerials off the
rear deck) stays with the hull, so only the thing with the barrel turns.

The turret's pivot is the centre of the cut ring, and a TurretMuzzle node marks
the barrel tip; TankTurret.cs aims by turning the pivot until the muzzle points
at the target, which means neither side has to agree about which way is forward.

  python tankturret.py                 # every robot, in place
  python tankturret.py titan ranger    # named robots
  python tankturret.py --preview P     # render the result with the turret at 40
"""
import json
import math
import os
import struct
import sys

import stopmotion as S

STAGE_DIR = "Assets/Models/Stages"

#: The tank is the last stage of the transformation -- see VehicleSkin.
STAGE_FILE = "stage8.glb"

#: A cross-section this much narrower than the widest one is the turret ring.
RING_FRACTION = 0.72

#: Cut height as a fraction of model height, for tanks whose turret sits on a
#: pedestal above shaped bodywork. Read off the cross-section profile by eye,
#: because no threshold tells "canopy" from "turret" -- both are narrower than
#: the hull, and only one of them is meant to turn.
CUT_OVERRIDE = {
    "ranger": 0.72,
    "samurai": 0.62,
}

#: Positions welded to this many decimals when chaining the cut edges into
#: loops. The models are ~2 units long, so this is well under a pixel.
WELD = 6


# --------------------------------------------------------------------- reading

def accessor(gltf, buf, index, comps):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    fmt = {5126: "f", 5125: "I", 5123: "H", 5121: "B"}[acc["componentType"]]
    return list(struct.unpack_from(f"<{acc['count'] * comps}{fmt}", buf, start))


def read_mesh(gltf, buf):
    """Vertices as (x, y, z, nx, ny, nz, u, v) plus a flat triangle index list."""
    prim = gltf["meshes"][0]["primitives"][0]
    pos = accessor(gltf, buf, prim["attributes"]["POSITION"], 3)
    nrm = accessor(gltf, buf, prim["attributes"]["NORMAL"], 3)
    uv = accessor(gltf, buf, prim["attributes"]["TEXCOORD_0"], 2)
    idx = accessor(gltf, buf, prim["indices"], 1)
    verts = [(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2],
              nrm[3 * i], nrm[3 * i + 1], nrm[3 * i + 2],
              uv[2 * i], uv[2 * i + 1]) for i in range(len(pos) // 3)]
    return verts, idx


# ------------------------------------------------------------------ the cut

def section_width(verts, tris, y):
    """Z extent of the horizontal cross-section at height y."""
    lo, hi = math.inf, -math.inf
    for t in range(0, len(tris), 3):
        tri = (tris[t], tris[t + 1], tris[t + 2])
        ys = [verts[v][1] for v in tri]
        if min(ys) > y or max(ys) < y:
            continue
        for i in range(3):
            p, q = tri[i], tri[(i + 1) % 3]
            y0, y1 = verts[p][1], verts[q][1]
            if y0 == y1 or (y0 - y) * (y1 - y) > 0:
                continue
            f = (y - y0) / (y1 - y0)
            z = verts[p][2] + (verts[q][2] - verts[p][2]) * f
            lo, hi = min(lo, z), max(hi, z)
    return 0.0 if lo > hi else hi - lo


def find_cut(verts, tris, samples=40):
    """
    The turret ring: the lowest height whose cross-section has collapsed to
    turret width. Scanned from the middle up, because the tracks and the hull
    skirt below it also narrow and would read as a ring from further down.

    The collapse takes a few samples to finish -- the deck slopes into the ring
    on most of these hulls -- so the scan follows the narrowing all the way to
    the bottom of the waist rather than stopping the moment it starts. Cutting
    on the way in leaves a plate of deck sitting on top of the turret, turning
    with it.
    """
    ys = [v[1] for v in verts]
    lo, hi = min(ys), max(ys)
    profile = [(lo + (hi - lo) * (k + 0.5) / samples,
                section_width(verts, tris, lo + (hi - lo) * (k + 0.5) / samples))
               for k in range(samples)]
    widest = max(w for _, w in profile)

    for i, (y, w) in enumerate(profile):
        if y <= lo + (hi - lo) * 0.35 or w >= widest * RING_FRACTION:
            continue
        # Keep descending while the section is still narrowing at all, and no
        # further than a quarter of the model up — a hull that tapers all the
        # way to its roof would otherwise walk the cut into the barrel.
        ceiling = min(y + (hi - lo) * 0.25, hi)
        while (i + 1 < len(profile) and profile[i + 1][0] <= ceiling
               and profile[i + 1][1] < profile[i][1] * 0.995):
            i += 1
        return profile[i][0]
    return lo + (hi - lo) * 0.5


def slice_mesh(verts, tris, cut):
    """
    Split the triangle soup at y = cut, cutting the ones that straddle it.

    Returns (above, below) triangle lists; `verts` grows with the vertices the
    cut creates, which both sides share -- the seam is the same ring of points
    on both parts, so the caps line up exactly.
    """
    above, below = [], []
    made = {}

    def crossing(p, q):
        """Vertex where edge p->q meets the plane, one per edge for the whole mesh."""
        key = (p, q) if p < q else (q, p)
        if key in made:
            return made[key]
        a, b = verts[p], verts[q]
        f = (cut - a[1]) / (b[1] - a[1])
        made[key] = len(verts)
        verts.append(tuple(a[i] + (b[i] - a[i]) * f for i in range(8)))
        # Exactly on the plane, whatever the arithmetic rounded to: the cap
        # hunts for edges lying in the plane and a float short of it is a hole.
        v = list(verts[-1])
        v[1] = cut
        verts[-1] = tuple(v)
        return made[key]

    for t in range(0, len(tris), 3):
        tri = (tris[t], tris[t + 1], tris[t + 2])
        up = [verts[v][1] > cut for v in tri]
        if all(up):
            above.extend(tri)
            continue
        if not any(up):
            below.extend(tri)
            continue

        # Rotate the triangle so the vertex on its own is first, which keeps the
        # winding of both halves the same as the winding of the original.
        lone = up.index(True) if sum(up) == 1 else up.index(False)
        l, m, n = tri[lone], tri[(lone + 1) % 3], tri[(lone + 2) % 3]
        p, q = crossing(l, m), crossing(n, l)

        (above if up[lone] else below).extend((l, p, q))
        (below if up[lone] else above).extend((p, m, n, p, n, q))

    return above, below


# ------------------------------------------------------------------ components

def weld_map(verts):
    """Index -> lowest index sharing its position, so seam duplicates count as one."""
    seen, rep = {}, [0] * len(verts)
    for i, v in enumerate(verts):
        rep[i] = seen.setdefault((round(v[0], WELD), round(v[1], WELD), round(v[2], WELD)), i)
    return rep


def largest_island(tris, rep):
    """
    The connected piece with the most triangles, and everything else.

    The turret is by far the biggest thing above the deck; aerials and roof
    boxes that touch nothing come back as leftovers and are handed to the hull,
    where standing still is the correct behaviour for them.
    """
    parent = {}

    def find(a):
        while parent.setdefault(a, a) != a:
            parent[a] = parent.setdefault(parent[a], parent[a])
            a = parent[a]
        return a

    def union(a, b):
        a, b = find(a), find(b)
        if a != b:
            parent[a] = b

    for t in range(0, len(tris), 3):
        a, b, c = rep[tris[t]], rep[tris[t + 1]], rep[tris[t + 2]]
        union(a, b)
        union(b, c)

    groups = {}
    for t in range(0, len(tris), 3):
        groups.setdefault(find(rep[tris[t]]), []).extend(tris[t:t + 3])
    if not groups:
        return [], []
    best = max(groups.values(), key=len)
    rest = [i for key, group in groups.items() if group is not best for i in group]
    return best, rest


# ----------------------------------------------------------------------- caps

def boundary_loops(verts, tris, cut, rep):
    """
    The open rings the cut left, walked in the winding direction of the
    triangles that own them. An edge is on the boundary when it lies in the cut
    plane and no triangle uses it the other way round.
    """
    directed = set()
    for t in range(0, len(tris), 3):
        tri = (rep[tris[t]], rep[tris[t + 1]], rep[tris[t + 2]])
        for i in range(3):
            directed.add((tri[i], tri[(i + 1) % 3]))

    nxt = {}
    for a, b in directed:
        if (b, a) in directed:
            continue
        if abs(verts[a][1] - cut) > 1e-9 or abs(verts[b][1] - cut) > 1e-9:
            continue
        nxt.setdefault(a, []).append(b)

    loops, used = [], set()
    for start in list(nxt):
        for first in nxt[start]:
            if (start, first) in used:
                continue
            loop, a, b = [start], start, first
            while True:
                used.add((a, b))
                loop.append(b)
                if b == start or b not in nxt:
                    break
                step = next((c for c in nxt[b] if (b, c) not in used), None)
                if step is None:
                    break
                a, b = b, step
            if len(loop) > 3 and loop[0] == loop[-1]:
                loops.append(loop[:-1])
    return loops


def cap(verts, loops, up):
    """
    Close each ring with a fan from its own centre. The lid is flat, so its
    normals are stated rather than interpolated, and it takes the ring's texture
    coordinates -- it is only ever glimpsed through the gap a turned turret
    opens, and reading as more tank is the whole requirement.
    """
    tris = []
    for loop in loops:
        cx = sum(verts[v][0] for v in loop) / len(loop)
        cy = verts[loop[0]][1]
        cz = sum(verts[v][2] for v in loop) / len(loop)
        cu = sum(verts[v][6] for v in loop) / len(loop)
        cv = sum(verts[v][7] for v in loop) / len(loop)
        ny = 1.0 if up else -1.0

        centre = len(verts)
        verts.append((cx, cy, cz, 0.0, ny, 0.0, cu, cv))
        ring = []
        for v in loop:
            ring.append(len(verts))
            s = verts[v]
            verts.append((s[0], s[1], s[2], 0.0, ny, 0.0, s[6], s[7]))

        for i, a in enumerate(ring):
            b = ring[(i + 1) % len(ring)]
            # Wind so the lid faces the way it was asked to: for points in a
            # horizontal plane the cross product is a single term.
            area = ((verts[a][0] - cx) * (verts[b][2] - cz) -
                    (verts[b][0] - cx) * (verts[a][2] - cz))
            tris.extend((centre, b, a) if (area > 0) == up else (centre, a, b))
    return tris


# ---------------------------------------------------------------------- output

def barrel_tip(model, turret):
    """
    Where the gun comes out: the middle of the turret's leading edge.

    "Leading" is the low end of the model's long horizontal axis, which is the
    end every one of these tanks was generated pointing -- the same fact
    VehicleSkin leans on when it turns a stage's long axis onto +Z with one
    shared offset for the whole roster. Taking the turret's furthest point from
    the pivot instead would be right for the eight tanks with a barrel and
    exactly backwards for the one without: Panther's turret is a visored wedge
    whose longest reach is its own back end.

    Averaged over the leading edge rather than taken from the single furthest
    vertex, so the muzzle sits in the centre of the barrel's mouth instead of on
    whichever corner of it happened to poke out.
    """
    span = [max(v[i] for v in model) - min(v[i] for v in model) for i in (0, 1, 2)]
    axis = 0 if span[0] >= span[2] else 2
    front = min(v[axis] for v in turret)
    edge = [v for v in turret if v[axis] <= front + span[axis] * 0.02]
    return tuple(sum(v[i] for v in edge) / len(edge) for i in range(3))


def compact(verts, tris):
    """Renumber one part's triangles onto just the vertices it actually uses."""
    remap, out, kept = {}, [], []
    for i in tris:
        if i not in remap:
            remap[i] = len(kept)
            kept.append(verts[i])
        out.append(remap[i])
    return kept, out


def pack(gltf, blob, verts, tris, offset):
    """Append one part's buffers and return its mesh index."""
    def view(data):
        blob.extend(b"\x00" * ((-len(blob)) % 4))
        start = len(blob)
        blob.extend(data)
        gltf["bufferViews"].append({"buffer": 0, "byteOffset": start, "byteLength": len(data)})
        return len(gltf["bufferViews"]) - 1

    def acc(data, comps, kind, ctype, bounds):
        entry = {"bufferView": view(data), "componentType": ctype,
                 "count": len(data) // (comps * (4 if ctype != 5123 else 2)),
                 "type": kind}
        if bounds:
            entry["min"], entry["max"] = bounds
        gltf["accessors"].append(entry)
        return len(gltf["accessors"]) - 1

    ox, oy, oz = offset
    pos = [(v[0] - ox, v[1] - oy, v[2] - oz) for v in verts]
    lo = [min(p[i] for p in pos) for i in range(3)]
    hi = [max(p[i] for p in pos) for i in range(3)]

    attributes = {
        "POSITION": acc(struct.pack(f"<{len(pos) * 3}f", *[c for p in pos for c in p]),
                        3, "VEC3", 5126, (lo, hi)),
        "NORMAL": acc(struct.pack(f"<{len(verts) * 3}f",
                                  *[c for v in verts for c in v[3:6]]), 3, "VEC3", 5126, None),
        "TEXCOORD_0": acc(struct.pack(f"<{len(verts) * 2}f",
                                      *[c for v in verts for c in v[6:8]]), 2, "VEC2", 5126, None),
    }
    indices = acc(struct.pack(f"<{len(tris)}I", *tris), 1, "SCALAR", 5125, None)
    gltf["meshes"].append({"primitives": [{"attributes": attributes, "indices": indices,
                                           "mode": 4, "material": 0}]})
    return len(gltf["meshes"]) - 1


def rig(path, out_path, name):
    gltf, buf = S.parse(path)
    if any(n.get("name") == "TurretPivot" for n in gltf.get("nodes", [])):
        print(f"  {name}: already rigged, left alone")
        return None

    verts, tris = read_mesh(gltf, buf)
    ys = [v[1] for v in verts]
    height = max(ys) - min(ys)
    cut = (min(ys) + height * CUT_OVERRIDE[name]) if name in CUT_OVERRIDE \
        else find_cut(verts, tris)

    above, below = slice_mesh(verts, tris, cut)
    rep = weld_map(verts)
    turret, strays = largest_island(above, rep)
    hull = below + strays

    # Both sets of rings are found BEFORE either lid goes on: a capped part has
    # no boundary left to find, and the weld map does not cover the vertices a
    # cap adds.
    hull_loops = boundary_loops(verts, hull, cut, rep)
    turret_loops = boundary_loops(verts, turret, cut, rep)
    if not turret_loops:
        print(f"  {name}: no closed ring at the cut — skipped")
        return None

    # The pivot is the centre of the ring the turret was cut off at, so it turns
    # on its own mounting rather than around the middle of a mesh the barrel
    # drags a long way forward.
    ring = [v for loop in turret_loops for v in loop]
    pivot = (sum(verts[v][0] for v in ring) / len(ring), cut,
             sum(verts[v][2] for v in ring) / len(ring))

    hull += cap(verts, hull_loops, up=True)
    turret += cap(verts, turret_loops, up=False)

    turret_verts, turret_tris = compact(verts, turret)
    hull_verts, hull_tris = compact(verts, hull)

    tip = barrel_tip(verts, turret_verts)

    # Rebuild the buffer around the texture, which is the only thing kept.
    image_view = gltf["bufferViews"][gltf["images"][0]["bufferView"]]
    image = buf[image_view.get("byteOffset", 0):
                image_view.get("byteOffset", 0) + image_view["byteLength"]]
    blob = bytearray()
    gltf["bufferViews"] = []
    gltf["accessors"] = []
    gltf["meshes"] = []
    blob.extend(image)
    gltf["bufferViews"].append({"buffer": 0, "byteOffset": 0, "byteLength": len(image)})
    gltf["images"][0]["bufferView"] = 0

    hull_mesh = pack(gltf, blob, hull_verts, hull_tris, (0.0, 0.0, 0.0))
    turret_mesh = pack(gltf, blob, turret_verts, turret_tris, pivot)

    gltf["nodes"] = [
        {"name": "Hull", "mesh": hull_mesh},
        {"name": "TurretPivot", "mesh": turret_mesh,
         "translation": list(pivot), "children": [2]},
        {"name": "TurretMuzzle",
         "translation": [tip[0] - pivot[0], tip[1] - pivot[1], tip[2] - pivot[2]]},
    ]
    gltf["scenes"] = [{"nodes": [0, 1]}]
    gltf["buffers"] = [{"byteLength": len(blob)}]

    with open(out_path, "wb") as handle:
        handle.write(S.build(gltf, bytes(blob)))

    print(f"  {name}: cut at y={cut:+.3f} ({(cut - min(ys)) / height:.0%} of height), "
          f"turret {len(turret_tris) // 3} tris, hull {len(hull_tris) // 3} tris, "
          f"pivot ({pivot[0]:+.2f}, {pivot[2]:+.2f}), barrel {math.dist(tip[:3], pivot):.2f} long")
    return cut


# --------------------------------------------------------------------- preview

def preview(path, degrees, tilt=0.0):
    """
    Merge the rigged model back into one mesh with the turret turned, so the
    existing single-mesh renderer can show what a turned turret looks like.
    Written to a temporary file next to the source and returned.

    <paramref name="tilt"/> pitches the whole tank nose-down afterwards, which
    is the only way to see the deck -- and so the only way to catch a seam that
    a turned turret has left open.
    """
    gltf, buf = S.parse(path)
    parts = []
    for node in gltf["nodes"]:
        if "mesh" not in node:
            continue
        offset = node.get("translation", [0.0, 0.0, 0.0])
        turn = math.radians(degrees) if node.get("name") == "TurretPivot" else 0.0
        prim = gltf["meshes"][node["mesh"]]["primitives"][0]
        pos = accessor(gltf, buf, prim["attributes"]["POSITION"], 3)
        nrm = accessor(gltf, buf, prim["attributes"]["NORMAL"], 3)
        uv = accessor(gltf, buf, prim["attributes"]["TEXCOORD_0"], 2)
        idx = accessor(gltf, buf, prim["indices"], 1)
        c, s = math.cos(turn), math.sin(turn)
        verts = []
        for i in range(len(pos) // 3):
            x, y, z = pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]
            nx, ny, nz = nrm[3 * i], nrm[3 * i + 1], nrm[3 * i + 2]
            verts.append((x * c + z * s + offset[0], y + offset[1], -x * s + z * c + offset[2],
                          nx * c + nz * s, ny, -nx * s + nz * c, uv[2 * i], uv[2 * i + 1]))
        parts.append((verts, idx))

    verts, tris = [], []
    for part_verts, part_tris in parts:
        base = len(verts)
        verts.extend(part_verts)
        tris.extend(i + base for i in part_tris)

    if tilt:
        c, s = math.cos(math.radians(tilt)), math.sin(math.radians(tilt))
        verts = [(v[0], v[1] * c - v[2] * s, v[1] * s + v[2] * c,
                  v[3], v[4] * c - v[5] * s, v[4] * s + v[5] * c, v[6], v[7])
                 for v in verts]

    image_view = gltf["bufferViews"][gltf["images"][0]["bufferView"]]
    image = buf[image_view.get("byteOffset", 0):
                image_view.get("byteOffset", 0) + image_view["byteLength"]]
    blob = bytearray(image)
    gltf["bufferViews"] = [{"buffer": 0, "byteOffset": 0, "byteLength": len(image)}]
    gltf["accessors"] = []
    gltf["meshes"] = []
    gltf["images"][0]["bufferView"] = 0
    mesh = pack(gltf, blob, verts, tris, (0.0, 0.0, 0.0))
    gltf["nodes"] = [{"mesh": mesh}]
    gltf["scenes"] = [{"nodes": [0]}]
    gltf["buffers"] = [{"byteLength": len(blob)}]

    temp = path + f".turned{int(degrees)}.glb"
    with open(temp, "wb") as handle:
        handle.write(S.build(gltf, bytes(blob)))
    return temp


# ------------------------------------------------------------------------ main

if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = [a for a in sys.argv[1:] if a.startswith("--")]
    out_dir = next((f.split("=", 1)[1] for f in flags if f.startswith("--out=")), None)

    robots = args or sorted(d for d in os.listdir(STAGE_DIR)
                            if os.path.isfile(f"{STAGE_DIR}/{d}/{STAGE_FILE}"))
    print(f"rigging {len(robots)} tank(s)")
    for robot in robots:
        source = f"{STAGE_DIR}/{robot}/{STAGE_FILE}"
        target = f"{out_dir}/{robot}-{STAGE_FILE}" if out_dir else source
        rig(source, target, robot)
