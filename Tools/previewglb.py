"""Renders GLB stages to a strip and an animated GIF, without opening Unity.

Exists so a stop-motion sequence can be judged at a glance rather than by
opening five files in a viewer one at a time. Deliberately simple: Lambert
shading from the same three-point rig the game's previews use, no specular, no
shadows. The question it answers is whether the SILHOUETTES read as a
continuous transformation, and silhouette survives crude shading.

Stages are fitted the way Tools/stopmotion.py fits them, so the strip and the
merged GLB agree about relative size.

  python previewglb.py out_prefix stage1.glb stage2.glb ...
"""
import math
import struct
import sys
from io import BytesIO

from PIL import Image

sys.path.insert(0, "D:/Claude/FirstPersongShooting/Tools")
import stopmotion as S  # noqa: E402

LIGHTS = [((1.7, 1.9, 2.1), (1.00, 0.97, 0.90), 11.0),
          ((-1.9, 0.5, 1.7), (0.55, 0.72, 1.00), 4.5),
          ((0.0, 1.4, -2.3), (0.80, 0.90, 1.00), 6.0)]
AMBIENT = (0.20, 0.22, 0.30)
RANGE = 6.0
BG = (14, 20, 34)
SIZE = 360
YAW = 35.0


def accessor(gltf, buf, index, comps):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    fmt = {5126: "f", 5125: "I", 5123: "H", 5121: "B"}[acc["componentType"]]
    width = {5126: 4, 5125: 4, 5123: 2, 5121: 1}[acc["componentType"]]
    return struct.unpack_from(f"<{acc['count'] * comps}{fmt}", buf,
                              start), acc


def texture_image(gltf, buf):
    material = gltf["materials"][0]
    ref = material.get("pbrMetallicRoughness", {}).get("baseColorTexture")
    if ref is None:
        return None
    source = gltf["textures"][ref["index"]]["source"]
    view = gltf["bufferViews"][gltf["images"][source]["bufferView"]]
    start = view.get("byteOffset", 0)
    return Image.open(BytesIO(buf[start:start + view["byteLength"]])).convert("RGB")


def render(path, size=SIZE, yaw=YAW):
    gltf, buf = S.parse(path)
    prim = gltf["meshes"][0]["primitives"][0]
    pos, _ = accessor(gltf, buf, prim["attributes"]["POSITION"], 3)
    nrm, _ = accessor(gltf, buf, prim["attributes"]["NORMAL"], 3)
    uv, _ = accessor(gltf, buf, prim["attributes"]["TEXCOORD_0"], 2)
    idx, _ = accessor(gltf, buf, prim["indices"], 1)
    tex = texture_image(gltf, buf)
    TW, TH = tex.size
    tp = tex.load()

    xs, ys, zs = pos[0::3], pos[1::3], pos[2::3]
    span = [max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)]
    # Same fit rule as stopmotion.py: largest dimension, so a long tank and a
    # tall robot both sit inside the frame at comparable apparent size.
    extent = max(span)
    factor = 2.0 / max(1e-6, extent)
    cx = (max(xs) + min(xs)) / 2
    cy = (max(ys) + min(ys)) / 2
    cz = (max(zs) + min(zs)) / 2

    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    P, N = [], []
    for i in range(len(pos) // 3):
        x, y, z = ((pos[i * 3] - cx) * factor, (pos[i * 3 + 1] - cy) * factor,
                   (pos[i * 3 + 2] - cz) * factor)
        P.append((x * c + z * s, y, -x * s + z * c))
        nx, ny, nz = nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]
        N.append((nx * c + nz * s, ny, -nx * s + nz * c))

    view_span = 2.7
    img = Image.new("RGB", (size, size), BG)
    px = img.load()
    zb = [[-1e9] * size for _ in range(size)]

    def proj(v):
        x, y, z = P[v]
        return x / view_span * size + size / 2, size / 2 - y / view_span * size, z

    for t in range(0, len(idx), 3):
        a, b_, c_ = idx[t], idx[t + 1], idx[t + 2]
        (ax, ay, az), (bx, by, bz), (cx2, cy2, cz2) = proj(a), proj(b_), proj(c_)
        area = (bx - ax) * (cy2 - ay) - (cx2 - ax) * (by - ay)
        if area >= 0:
            continue
        lox = max(0, int(min(ax, bx, cx2))); hix = min(size - 1, int(max(ax, bx, cx2)) + 1)
        loy = max(0, int(min(ay, by, cy2))); hiy = min(size - 1, int(max(ay, by, cy2)) + 1)
        for y in range(loy, hiy + 1):
            for x in range(lox, hix + 1):
                w0 = ((bx - ax) * (y + .5 - ay) - (x + .5 - ax) * (by - ay)) / area
                w1 = ((x + .5 - ax) * (cy2 - ay) - (cx2 - ax) * (y + .5 - ay)) / area
                w2 = 1 - w0 - w1
                if w0 < 0 or w1 < 0 or w2 < 0:
                    continue
                z = w2 * az + w1 * bz + w0 * cz2
                if z <= zb[y][x]:
                    continue
                zb[y][x] = z
                u = w2 * uv[a * 2] + w1 * uv[b_ * 2] + w0 * uv[c_ * 2]
                v = w2 * uv[a * 2 + 1] + w1 * uv[b_ * 2 + 1] + w0 * uv[c_ * 2 + 1]
                col = tp[min(TW - 1, max(0, int(u * TW))),
                         min(TH - 1, max(0, int(v * TH)))]
                p = tuple(w2 * P[a][k] + w1 * P[b_][k] + w0 * P[c_][k] for k in range(3))
                n = [w2 * N[a][k] + w1 * N[b_][k] + w0 * N[c_][k] for k in range(3)]
                ln = math.sqrt(sum(q * q for q in n)) or 1.0
                n = [q / ln for q in n]
                r, g, bb = AMBIENT
                for (lx, ly, lz), (cr, cg, cb), inten in LIGHTS:
                    dx, dy, dz = lx - p[0], ly - p[1], lz - p[2]
                    d2 = dx * dx + dy * dy + dz * dz
                    d = math.sqrt(d2)
                    if d > RANGE:
                        continue
                    ndl = (n[0] * dx + n[1] * dy + n[2] * dz) / max(1e-6, d)
                    if ndl <= 0:
                        continue
                    fade = max(0.0, 1.0 - (d / RANGE) ** 4) ** 2
                    amt = inten * fade / max(0.05, d2) * ndl
                    r += cr * amt; g += cg * amt; bb += cb * amt
                px[x, y] = (min(255, int(col[0] * r)), min(255, int(col[1] * g)),
                            min(255, int(col[2] * bb)))
    return img


if __name__ == "__main__":
    prefix, paths = sys.argv[1], sys.argv[2:]
    frames = []
    for i, path in enumerate(paths):
        frames.append(render(path))
        print(f"rendered stage {i + 1}/{len(paths)}: {path}", flush=True)

    strip = Image.new("RGB", (SIZE * len(frames), SIZE), BG)
    for i, frame in enumerate(frames):
        strip.paste(frame, (i * SIZE, 0))
    strip.save(f"{prefix}_strip.jpg", quality=92)

    # Ping-pong so the loop shows the transformation and its reverse, which is
    # how the effect would actually be used in game.
    loop = frames + frames[-2:0:-1]
    loop[0].save(f"{prefix}_stopmotion.gif", save_all=True, append_images=loop[1:],
                 duration=600, loop=0)
    print("wrote", f"{prefix}_strip.jpg", "and", f"{prefix}_stopmotion.gif")
