"""Serve Build/WebGL on the LAN for testing on a phone or tablet.

Unity's build has the decompression fallback OFF, so the browser is what
unpacks the .gz blobs -- which only works if each one is served with
Content-Encoding: gzip AND the content type of the file *inside* the archive.
python -m http.server does neither, so the player fails with "not a valid Unity
content" and a loading bar that never appears. This mirrors the header logic in
Tools/deploy_webgl.ps1 so local testing matches what Azure serves.

Byte ranges are honoured for uncompressed files. iOS Safari will not play a
<video> at all unless the server answers Range with a 206, which matters for
the StreamingAssets transformation clips.

    python Tools/serve_webgl.py                 # 0.0.0.0:8080
    python Tools/serve_webgl.py --port 9000
"""

import argparse
import os
import posixpath
import re
import socket
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlparse

CONTENT_TYPES = {
    ".html": "text/html",
    ".js": "text/javascript",
    ".wasm": "application/wasm",
    ".data": "application/octet-stream",
    ".symbols": "application/octet-stream",
    ".json": "application/json",
    ".css": "text/css",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".svg": "image/svg+xml",
    ".ico": "image/x-icon",
    ".mp4": "video/mp4",
    ".webm": "video/webm",
}

ENCODINGS = {".gz": "gzip", ".br": "br"}

RANGE_RE = re.compile(r"bytes=(\d*)-(\d*)")


class Handler(BaseHTTPRequestHandler):
    root = "."
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        sys.stderr.write("  %s %s\n" % (self.address_string(), fmt % args))

    def resolve(self):
        path = unquote(urlparse(self.path).path)
        path = posixpath.normpath(path).lstrip("/")
        if path in ("", "."):
            path = "index.html"
        full = os.path.join(self.root, *path.split("/"))
        # Refuse anything that escapes the build directory.
        if not os.path.abspath(full).startswith(os.path.abspath(self.root)):
            return None
        return full if os.path.isfile(full) else None

    def headers_for(self, full):
        name = os.path.basename(full)
        encoding = None
        for suffix, enc in ENCODINGS.items():
            if name.endswith(suffix):
                encoding = enc
                name = name[: -len(suffix)]
                break
        ext = os.path.splitext(name)[1].lower()
        return CONTENT_TYPES.get(ext, "application/octet-stream"), encoding

    def serve(self, include_body):
        full = self.resolve()
        if not full:
            self.send_error(404, "Not found")
            return

        ctype, encoding = self.headers_for(full)
        size = os.path.getsize(full)
        start, end = 0, size - 1
        partial = False

        # Ranges only on unencoded files -- slicing a gzip stream is meaningless
        # to a client that was told the whole body is gzip.
        rng = self.headers.get("Range")
        if rng and not encoding:
            match = RANGE_RE.fullmatch(rng.strip())
            if match:
                lo, hi = match.group(1), match.group(2)
                if lo:
                    start = int(lo)
                    end = int(hi) if hi else size - 1
                elif hi:                                  # bytes=-N, the tail
                    start = max(0, size - int(hi))
                if start >= size or start > end:
                    self.send_response(416)
                    self.send_header("Content-Range", f"bytes */{size}")
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
                end = min(end, size - 1)
                partial = True

        length = end - start + 1
        self.send_response(206 if partial else 200)
        self.send_header("Content-Type", ctype)
        if encoding:
            self.send_header("Content-Encoding", encoding)
        else:
            self.send_header("Accept-Ranges", "bytes")
        if partial:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.send_header("Content-Length", str(length))
        # No caching: the whole point of a local server is seeing the new build.
        self.send_header("Cache-Control", "no-store")
        self.end_headers()

        if not include_body:
            return
        with open(full, "rb") as handle:
            handle.seek(start)
            remaining = length
            while remaining > 0:
                chunk = handle.read(min(256 * 1024, remaining))
                if not chunk:
                    break
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionAbortedError):
                    return                                # client navigated away
                remaining -= len(chunk)

    def do_GET(self):
        self.serve(include_body=True)

    def do_HEAD(self):
        self.serve(include_body=False)


def lan_address():
    """Best-guess routable address, without needing anything reachable."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("8.8.8.8", 80))
        return sock.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        sock.close()


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dir", default=os.path.join(here, "..", "Build", "WebGL"))
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--host", default="0.0.0.0")
    args = parser.parse_args()

    root = os.path.abspath(args.dir)
    if not os.path.isfile(os.path.join(root, "index.html")):
        print(f"No index.html in {root} -- build first "
              f"(Photon Arena -> Build WebGL Player).", file=sys.stderr)
        return 1

    Handler.root = root
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.daemon_threads = True

    print(f"Serving {root}")
    print(f"  this machine : http://localhost:{args.port}/")
    print(f"  on the LAN   : http://{lan_address()}:{args.port}/")
    print("Ctrl+C to stop.\n")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
