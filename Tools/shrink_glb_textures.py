"""Downscale the textures embedded in Meshy's .glb files.

Meshy hands back 2048x2048 maps on every model. Unity imports a glTF image as
an uncompressed RGBA32 Texture2D at whatever resolution it finds, so each one
costs 2048 * 2048 * 4 = 16 MB in the player regardless of how well the JPEG
compressed on disk. Four maps on the laser blaster is 64 MB of the WebGL
build for a prop that is a few dozen pixels across on screen.

Shrinking the image *inside* the .glb is what fixes it -- there is no import
setting on glTFast's ScriptedImporter that caps sub-asset texture size, so the
source has to be the thing that changes.

    python Tools/shrink_glb_textures.py --dry-run          # report only
    python Tools/shrink_glb_textures.py --size 512         # rewrite in place
    python Tools/shrink_glb_textures.py --out /tmp/copies  # rewrite to a copy

Normal maps are kept at twice the colour budget: they carry geometric detail
that reads as faceting when it is crushed, while a base colour map at 512 on a
toy-scale robot is indistinguishable from the original.
"""

import argparse
import io
import json
import os
import struct
import sys

from PIL import Image

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def read_glb(path):
    data = open(path, "rb").read()
    if data[:4] != b"glTF":
        raise ValueError(f"{path} is not a binary glTF")
    version, _length = struct.unpack_from("<II", data, 4)
    if version != 2:
        raise ValueError(f"{path} is glTF version {version}, expected 2")

    offset, gltf, binary = 12, None, b""
    while offset < len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        chunk = data[offset:offset + chunk_len]
        offset += chunk_len
        if chunk_type == JSON_CHUNK:
            gltf = json.loads(chunk)
        elif chunk_type == BIN_CHUNK:
            binary = chunk
    if gltf is None:
        raise ValueError(f"{path} has no JSON chunk")
    return gltf, binary


def write_glb(path, gltf, binary):
    json_bytes = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    json_bytes += b" " * (-len(json_bytes) % 4)          # pad with spaces
    binary += b"\x00" * (-len(binary) % 4)               # pad with zeroes

    total = 12 + 8 + len(json_bytes) + (8 + len(binary) if binary else 0)
    out = bytearray()
    out += b"glTF" + struct.pack("<II", 2, total)
    out += struct.pack("<II", len(json_bytes), JSON_CHUNK) + json_bytes
    if binary:
        out += struct.pack("<II", len(binary), BIN_CHUNK) + binary

    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(out)


def normal_map_images(gltf):
    """Indices of images used as a normal map by any material."""
    textures = gltf.get("textures", [])
    flagged = set()
    for material in gltf.get("materials", []):
        entry = material.get("normalTexture")
        if not entry:
            continue
        source = textures[entry["index"]].get("source")
        if source is not None:
            flagged.add(source)
    return flagged


def resize_payload(blob, mime, target):
    """Return re-encoded bytes at <= target on the long edge, or None."""
    image = Image.open(io.BytesIO(blob))
    if max(image.size) <= target:
        return None, image.size, image.size

    before = image.size
    scale = target / max(image.size)
    after = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    image = image.resize(after, Image.LANCZOS)

    # Keep the original encoding. Re-coding a PNG to JPEG would save a little
    # on disk and nothing in the player -- Unity expands both to RGBA32 -- but
    # glTFast names its texture sub-assets from the image, and a renamed
    # sub-asset silently drops the material's reference to it.
    buffer = io.BytesIO()
    if mime == "image/jpeg":
        image.convert("RGB").save(buffer, format="JPEG", quality=90, optimize=True)
    else:
        image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue(), before, after, mime


def rebuild_binary(gltf, binary, replacements):
    """Repack the BIN chunk, substituting new bytes for the given bufferViews."""
    views = gltf.get("bufferViews", [])
    packed = bytearray()
    for index, view in enumerate(views):
        payload = replacements.get(index)
        if payload is None:
            start = view.get("byteOffset", 0)
            payload = binary[start:start + view["byteLength"]]
        packed += b"\x00" * (-len(packed) % 4)           # keep views 4-aligned
        view["byteOffset"] = len(packed)
        view["byteLength"] = len(payload)
        packed += payload

    if gltf.get("buffers"):
        gltf["buffers"][0]["byteLength"] = len(packed)
    return bytes(packed)


def process(path, target, normal_target, out_path, dry_run):
    gltf, binary = read_glb(path)
    images = gltf.get("images", [])
    if not images:
        return None

    normals = normal_map_images(gltf)
    replacements, notes = {}, []

    for index, image in enumerate(images):
        view_index = image.get("bufferView")
        if view_index is None:
            continue                                      # external / data-uri
        view = gltf["bufferViews"][view_index]
        start = view.get("byteOffset", 0)
        blob = binary[start:start + view["byteLength"]]
        mime = image.get("mimeType", "image/png")

        budget = normal_target if index in normals else target
        result = resize_payload(blob, mime, budget)
        if result[0] is None:
            continue
        new_bytes, before, after, new_mime = result

        replacements[view_index] = new_bytes
        if not dry_run:
            image["mimeType"] = new_mime
        notes.append(f"{before[0]}x{before[1]} -> {after[0]}x{after[1]}"
                     f"{' (normal)' if index in normals else ''}")

    if not replacements:
        return None

    old_texels = sum(1 for _ in images)
    before_bytes = os.path.getsize(path)
    if dry_run:
        # Cost in the player is 4 bytes per texel, not the on-disk size.
        return notes, before_bytes, None

    packed = rebuild_binary(gltf, binary, replacements)
    write_glb(out_path, gltf, packed)
    return notes, before_bytes, os.path.getsize(out_path)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default="Assets/Models")
    parser.add_argument("--size", type=int, default=512,
                        help="max edge for colour/metallic/roughness maps")
    parser.add_argument("--normal-size", type=int, default=1024,
                        help="max edge for normal maps")
    parser.add_argument("--out", help="write beside this root instead of in place")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    targets = []
    for base, _dirs, files in os.walk(args.root):
        targets += [os.path.join(base, f) for f in files if f.endswith(".glb")]
    targets.sort()

    if not targets:
        print(f"No .glb files under {args.root}", file=sys.stderr)
        return 1

    total_before = total_after = 0
    touched = 0
    for path in targets:
        out_path = path
        if args.out:
            out_path = os.path.join(args.out, os.path.relpath(path, args.root))
        try:
            result = process(path, args.size, args.normal_size, out_path, args.dry_run)
        except Exception as error:                        # keep going on one bad file
            print(f"  !! {path}: {error}", file=sys.stderr)
            continue
        if result is None:
            continue
        notes, before, after = result
        touched += 1
        total_before += before
        total_after += after if after else before
        shown = ", ".join(notes[:3]) + (f", +{len(notes) - 3} more" if len(notes) > 3 else "")
        size = f"{before / 1048576:.1f} MB"
        if after:
            size += f" -> {after / 1048576:.1f} MB"
        print(f"  {os.path.relpath(path, args.root):<44} {size:>22}  [{shown}]")

    verb = "would shrink" if args.dry_run else "shrank"
    print(f"\n{verb} {touched} of {len(targets)} .glb file(s)")
    if not args.dry_run and total_before:
        print(f"on disk: {total_before / 1048576:.0f} MB -> {total_after / 1048576:.0f} MB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
