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

#: Stage sets that fold into an aircraft rather than a tank (Dogfight's jet
#: form). Skipped: no ring, no turret, nothing to elevate.
JET_SUFFIX = "-jet"

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


def plane_at(point, normal):
    """A cut plane as (point, unit normal). Positive side is along the normal."""
    length = math.sqrt(sum(c * c for c in normal)) or 1.0
    return (tuple(point), tuple(c / length for c in normal))


def height(plane, v):
    """Signed distance from the plane -- positive on the normal's side."""
    point, normal = plane
    return sum((v[i] - point[i]) * normal[i] for i in range(3))


def slice_mesh(verts, tris, plane):
    """
    Split the triangle soup at a plane, cutting the ones that straddle it.

    Returns (front, back) triangle lists -- front being the normal's side;
    `verts` grows with the vertices the cut creates, which both sides share, so
    the seam is the same ring of points on both parts and the caps line up
    exactly.

    Takes a plane rather than a height because the same cut serves twice: level
    for the turret ring, and standing on end across the barrel for the gun.
    """
    front, back = [], []
    made = {}
    normal = plane[1]

    def crossing(p, q):
        """Vertex where edge p->q meets the plane, one per edge for the whole mesh."""
        key = (p, q) if p < q else (q, p)
        if key in made:
            return made[key]
        a, b = verts[p], verts[q]
        ha, hb = height(plane, a), height(plane, b)
        f = ha / (ha - hb)
        made[key] = len(verts)
        cut_vert = [a[i] + (b[i] - a[i]) * f for i in range(8)]
        # Exactly on the plane, whatever the arithmetic rounded to: the cap
        # hunts for edges lying in the plane and a float short of it is a hole.
        drift = height(plane, cut_vert)
        for i in range(3):
            cut_vert[i] -= drift * normal[i]
        verts.append(tuple(cut_vert))
        return made[key]

    for t in range(0, len(tris), 3):
        tri = (tris[t], tris[t + 1], tris[t + 2])
        up = [height(plane, verts[v]) > 0 for v in tri]
        if all(up):
            front.extend(tri)
            continue
        if not any(up):
            back.extend(tri)
            continue

        # Rotate the triangle so the vertex on its own is first, which keeps the
        # winding of both halves the same as the winding of the original.
        lone = up.index(True) if sum(up) == 1 else up.index(False)
        l, m, n = tri[lone], tri[(lone + 1) % 3], tri[(lone + 2) % 3]
        p, q = crossing(l, m), crossing(n, l)

        (front if up[lone] else back).extend((l, p, q))
        (back if up[lone] else front).extend((p, m, n, p, n, q))

    return front, back


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

def boundary_loops(verts, tris, plane, rep):
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
        if abs(height(plane, verts[a])) > 1e-9 or abs(height(plane, verts[b])) > 1e-9:
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


def cap(verts, loops, plane, outward):
    """
    Close each ring with a fan from its own centre. The lid is flat, so its
    normals are stated rather than interpolated, and it takes the ring's texture
    coordinates -- it is only ever glimpsed through the gap a turned turret
    opens, and reading as more tank is the whole requirement.

    <paramref name="outward"/> is which way the lid faces: along the plane's
    normal for the piece sitting behind it, against for the piece in front.
    """
    normal = plane[1]
    face = tuple(c * (1.0 if outward else -1.0) for c in normal)
    tris = []
    for loop in loops:
        centre_pos = tuple(sum(verts[v][i] for v in loop) / len(loop) for i in range(3))
        cu = sum(verts[v][6] for v in loop) / len(loop)
        cv = sum(verts[v][7] for v in loop) / len(loop)

        centre = len(verts)
        verts.append(centre_pos + face + (cu, cv))
        ring = []
        for v in loop:
            ring.append(len(verts))
            s = verts[v]
            verts.append((s[0], s[1], s[2]) + face + (s[6], s[7]))

        for i, a in enumerate(ring):
            b = ring[(i + 1) % len(ring)]
            # Wind so the lid faces the way it was asked to: the fan triangle's
            # own normal, read against the face direction.
            ea = tuple(verts[a][i] - centre_pos[i] for i in range(3))
            eb = tuple(verts[b][i] - centre_pos[i] for i in range(3))
            cross = (ea[1] * eb[2] - ea[2] * eb[1],
                     ea[2] * eb[0] - ea[0] * eb[2],
                     ea[0] * eb[1] - ea[1] * eb[0])
            tris.extend((centre, b, a) if sum(cross[i] * face[i] for i in range(3)) > 0
                        else (centre, a, b))
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


def find_gun_cut(verts, tris, pivot, tip, axis, steps=60):
    """
    Where the barrel stops being a barrel: the deepest plane across the gun that
    still cuts nothing but the gun.

    Scanned from the muzzle backwards, one plane at a time, measuring how far
    the cut edges stray from the barrel's own axis. A plane through open barrel
    crosses a thin ring around that line; the first plane that also catches the
    mantlet, a roof box or the turret's own shoulders picks up points far off
    it, and the scan stops one step short. That is the trunnion.

    Returns the distance along the axis from the pivot, or None for a turret
    with no gun to speak of -- Panther's is a visored wedge, and there is
    nothing on it to elevate but the whole block.
    """
    reach = sum((tip[i] - pivot[i]) * axis[i] for i in range(3))
    if reach < 0.25:
        return None

    def stray(s):
        """How far the cut edges at this plane sit from the barrel's axis."""
        plane = plane_at([pivot[i] + axis[i] * s for i in range(3)], axis)
        worst = 0.0
        hit = False
        for t in range(0, len(tris), 3):
            tri = (tris[t], tris[t + 1], tris[t + 2])
            hs = [height(plane, verts[v]) for v in tri]
            if min(hs) > 0 or max(hs) < 0:
                continue
            for i in range(3):
                p, q = tri[i], tri[(i + 1) % 3]
                if hs[i] * hs[(i + 1) % 3] > 0 or hs[i] == hs[(i + 1) % 3]:
                    continue
                f = hs[i] / (hs[i] - hs[(i + 1) % 3])
                point = [verts[p][j] + (verts[q][j] - verts[p][j]) * f for j in range(3)]
                # Distance from the line through the muzzle along the axis.
                off = [point[j] - tip[j] for j in range(3)]
                slide = sum(off[j] * axis[j] for j in range(3))
                worst = max(worst, math.sqrt(sum(
                    (off[j] - slide * axis[j]) ** 2 for j in range(3))))
                hit = True
        return worst if hit else None

    best, bore = None, None
    for k in range(steps):
        s = reach - 0.04 - (reach * 0.85) * k / steps
        if s < reach * 0.1:
            break
        radius = stray(s)
        if radius is None:
            continue
        if bore is None:
            bore = radius
        elif radius > max(bore * 1.9, bore + 0.03):
            break     # the plane has reached the mantlet
        bore = min(bore, radius)
        best = s

    # A stub is not a gun: too short to read as elevating, and cutting one off
    # only buys a seam.
    return best if best is not None and reach - best > 0.22 else None


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
    tall = max(ys) - min(ys)
    cut = (min(ys) + tall * CUT_OVERRIDE[name]) if name in CUT_OVERRIDE \
        else find_cut(verts, tris)

    ring_plane = plane_at((0.0, cut, 0.0), (0.0, 1.0, 0.0))
    above, below = slice_mesh(verts, tris, ring_plane)
    rep = weld_map(verts)
    turret, strays = largest_island(above, rep)
    hull = below + strays

    # Both sets of rings are found BEFORE either lid goes on: a capped part has
    # no boundary left to find, and the weld map does not cover the vertices a
    # cap adds.
    hull_loops = boundary_loops(verts, hull, ring_plane, rep)
    turret_loops = boundary_loops(verts, turret, ring_plane, rep)
    if not turret_loops:
        print(f"  {name}: no closed ring at the cut — skipped")
        return None

    # The pivot is the centre of the ring the turret was cut off at, so it turns
    # on its own mounting rather than around the middle of a mesh the barrel
    # drags a long way forward.
    ring = [v for loop in turret_loops for v in loop]
    pivot = (sum(verts[v][0] for v in ring) / len(ring), cut,
             sum(verts[v][2] for v in ring) / len(ring))

    hull += cap(verts, hull_loops, ring_plane, outward=True)
    turret += cap(verts, turret_loops, ring_plane, outward=False)

    tip = barrel_tip(verts, [verts[i] for i in set(turret)])

    # ---- the gun: cut again, across the barrel, so it can elevate on its own
    flat = [tip[0] - pivot[0], 0.0, tip[2] - pivot[2]]
    reach = math.hypot(flat[0], flat[2])
    gun, trunnion = [], None
    if reach > 1e-3:
        axis = [flat[0] / reach, 0.0, flat[2] / reach]
        s = find_gun_cut(verts, turret, pivot, tip, axis)
        if s is not None:
            gun_plane = plane_at([pivot[i] + axis[i] * s for i in range(3)], axis)
            front, rest = slice_mesh(verts, turret, gun_plane)
            rep = weld_map(verts)
            gun, leftovers = largest_island(front, rep)
            turret = rest + leftovers

            mantlet = boundary_loops(verts, turret, gun_plane, rep)
            breech = boundary_loops(verts, gun, gun_plane, rep)
            if breech:
                # The trunnion is the centre of the bore the gun was cut at, so
                # it elevates around its own mounting and the barrel's back end
                # stays inside the mantlet however far it rises.
                bore = [v for loop in breech for v in loop]
                trunnion = tuple(sum(verts[v][i] for v in bore) / len(bore) for i in range(3))
                turret += cap(verts, mantlet, gun_plane, outward=True)
                gun += cap(verts, breech, gun_plane, outward=False)
            else:
                turret, gun = turret + gun, []


    turret_verts, turret_tris = compact(verts, turret)
    hull_verts, hull_tris = compact(verts, hull)
    gun_verts, gun_tris = compact(verts, gun) if gun else ([], [])

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

    # The muzzle marker hangs off whichever part actually swings it: the gun
    # when there is one, the turret when the whole block has to do the
    # elevating. Either way TankTurret reads the barrel from pivot to marker.
    gltf["nodes"] = [
        {"name": "Hull", "mesh": hull_mesh},
        {"name": "TurretPivot", "mesh": turret_mesh,
         "translation": list(pivot), "children": [2]},
    ]
    if trunnion is not None:
        gun_mesh = pack(gltf, blob, gun_verts, gun_tris, trunnion)
        gltf["nodes"].append({"name": "GunPivot", "mesh": gun_mesh,
                              "translation": [trunnion[i] - pivot[i] for i in range(3)],
                              "children": [3]})
        gltf["nodes"].append({"name": "TurretMuzzle",
                              "translation": [tip[i] - trunnion[i] for i in range(3)]})
    else:
        gltf["nodes"].append({"name": "TurretMuzzle",
                              "translation": [tip[i] - pivot[i] for i in range(3)]})

    gltf["scenes"] = [{"nodes": [0, 1]}]
    gltf["buffers"] = [{"byteLength": len(blob)}]

    with open(out_path, "wb") as handle:
        handle.write(S.build(gltf, bytes(blob)))

    barrel = (f"gun {len(gun_tris) // 3} tris, "
              f"{math.dist(tip[:3], trunnion):.2f} long from its trunnion"
              if trunnion is not None else "no separable gun — the turret elevates whole")
    print(f"  {name}: ring at y={cut:+.3f} ({(cut - min(ys)) / tall:.0%} of height), "
          f"turret {len(turret_tris) // 3} tris, hull {len(hull_tris) // 3} tris, {barrel}")
    return cut


# --------------------------------------------------------------------- preview

def preview(path, degrees, tilt=0.0, elevation=0.0):
    """
    Merge the rigged model back into one mesh with the turret turned and the gun
    raised, so the existing single-mesh renderer can show what the rig does.
    Written to a temporary file next to the source and returned.

    <paramref name="tilt"/> pitches the whole tank nose-down afterwards, which
    is the only way to see the deck -- and so the only way to catch a seam that
    a turned turret has left open.

    The transform chain is walked the way the runtime walks it: the gun rides
    the turret, so it takes the elevation about its trunnion FIRST and the
    turret's yaw afterwards, exactly as parenting would apply them.
    """
    gltf, buf = S.parse(path)
    turn = math.radians(degrees)
    rise = math.radians(elevation)

    # Whichever part carries the muzzle marker is the part that elevates —
    # the gun where the rig found one, the whole turret where it did not.
    muzzle = next(n for n in gltf["nodes"] if n.get("name") == "TurretMuzzle")
    barrel = muzzle.get("translation", [1.0, 0.0, 0.0])
    riser = "GunPivot" if any(n.get("name") == "GunPivot" for n in gltf["nodes"]) \
        else "TurretPivot"
    hinge_node = next((n for n in gltf["nodes"] if n.get("name") == "TurretHinge"), None)
    hinge = hinge_node.get("translation", [0.0, 0.0, 0.0]) if hinge_node else [0.0, 0.0, 0.0]

    parts = []
    for node in gltf["nodes"]:
        if "mesh" not in node:
            continue
        name = node.get("name")
        offset = node.get("translation", [0.0, 0.0, 0.0])
        prim = gltf["meshes"][node["mesh"]]["primitives"][0]
        pos = accessor(gltf, buf, prim["attributes"]["POSITION"], 3)
        nrm = accessor(gltf, buf, prim["attributes"]["NORMAL"], 3)
        uv = accessor(gltf, buf, prim["attributes"]["TEXCOORD_0"], 2)
        idx = accessor(gltf, buf, prim["indices"], 1)

        # Which stage of the chain this mesh sits at. A gun's own translation is
        # relative to the turret pivot, so it needs the pivot added back.
        gun = name == "GunPivot"
        turret = gun or name == "TurretPivot"
        raises = name == riser
        base = [0.0, 0.0, 0.0]
        if gun:
            pivot = next(n for n in gltf["nodes"] if n.get("name") == "TurretPivot")
            base = pivot.get("translation", [0.0, 0.0, 0.0])

        # The trunnion axis: horizontal, across the barrel.
        span = math.hypot(barrel[0], barrel[2]) or 1.0
        ax, az = barrel[0] / span, barrel[2] / span

        verts = []
        for i in range(len(pos) // 3):
            p = [pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]]
            n = [nrm[3 * i], nrm[3 * i + 1], nrm[3 * i + 2]]
            if raises and rise:
                # Raise about the horizontal axis across the barrel, through
                # the hinge: the component along the barrel trades with height.
                for k, v in enumerate((p, n)):
                    o = hinge if k == 0 else (0.0, 0.0, 0.0)
                    d = [v[j] - o[j] for j in range(3)]
                    along = d[0] * ax + d[2] * az
                    lift = along * math.sin(rise) + d[1] * math.cos(rise)
                    slide = along * math.cos(rise) - d[1] * math.sin(rise)
                    v[0] = o[0] + d[0] + (slide - along) * ax
                    v[2] = o[2] + d[2] + (slide - along) * az
                    v[1] = o[1] + lift
            if gun:
                p = [p[0] + offset[0], p[1] + offset[1], p[2] + offset[2]]
            if turret:
                c, s = math.cos(turn), math.sin(turn)
                p = [p[0] * c + p[2] * s, p[1], -p[0] * s + p[2] * c]
                n = [n[0] * c + n[2] * s, n[1], -n[0] * s + n[2] * c]
                p = [p[0] + base[0], p[1] + base[1], p[2] + base[2]] if gun \
                    else [p[0] + offset[0], p[1] + offset[1], p[2] + offset[2]]
            verts.append(tuple(p) + tuple(n) + (uv[2 * i], uv[2 * i + 1]))
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

    # The jet sets share this directory and must be left alone: an aircraft has
    # no turret ring, and cutting one anyway would hand TankTurret a piece of
    # somebody's wing to spin. Named explicitly rather than detected, because
    # "the front half of this is narrower than the back" is true of a jet too.
    robots = args or sorted(d for d in os.listdir(STAGE_DIR)
                            if os.path.isfile(f"{STAGE_DIR}/{d}/{STAGE_FILE}")
                            and not d.endswith(JET_SUFFIX))
    print(f"rigging {len(robots)} tank(s)")
    for robot in robots:
        source = f"{STAGE_DIR}/{robot}/{STAGE_FILE}"
        target = f"{out_dir}/{robot}-{STAGE_FILE}" if out_dir else source
        rig(source, target, robot)
