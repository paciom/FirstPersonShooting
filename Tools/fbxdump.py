"""Minimal binary-FBX reader: pulls each Geometry's Vertices and replays the
nose heuristic from MissileModels.Measure so we can check it before shipping."""
import struct, zlib, sys, math

def read_props(buf, off, n):
    props = []
    for _ in range(n):
        t = chr(buf[off]); off += 1
        if t in 'YCIFDL':
            size = {'Y':2,'C':1,'I':4,'F':4,'D':8,'L':8}[t]
            fmt = {'Y':'<h','C':'<b','I':'<i','F':'<f','D':'<d','L':'<q'}[t]
            props.append(struct.unpack_from(fmt, buf, off)[0]); off += size
        elif t in 'fdlib':
            length, enc, clen = struct.unpack_from('<III', buf, off); off += 12
            raw = buf[off:off+clen]; off += clen
            if enc == 1:
                raw = zlib.decompress(raw)
            fmt = {'f':'f','d':'d','l':'q','i':'i','b':'b'}[t]
            props.append(list(struct.unpack('<%d%s' % (length, fmt), raw)))
        elif t in 'SR':
            length = struct.unpack_from('<I', buf, off)[0]; off += 4
            props.append(buf[off:off+length]); off += length
        else:
            raise ValueError('unknown property type %r' % t)
    return props, off

def read_nodes(buf, off, end, out):
    while off < end:
        end_off, nprops, plen, namelen = struct.unpack_from('<IIIB', buf, off)
        off += 13
        if end_off == 0:
            return off
        name = buf[off:off+namelen].decode('utf8', 'replace'); off += namelen
        props, off = read_props(buf, off, nprops)
        node = (name, props, [])
        out.append(node)
        if off < end_off:
            read_nodes(buf, off, end_off - 13, node[2])
        off = end_off
    return off

def walk(nodes, name):
    for n in nodes:
        if n[0] == name:
            yield n
        for hit in walk(n[2], name):
            yield hit

buf = open(sys.argv[1], 'rb').read()
root = []
read_nodes(buf, 27, len(buf), root)

for geom in walk(root, 'Geometry'):
    label = next((p for p in geom[1] if isinstance(p, bytes)), b'?')
    verts = None
    for child in geom[2]:
        if child[0] == 'Vertices':
            verts = child[1][0]
    if not verts:
        continue
    pts = [(verts[i], verts[i+1], verts[i+2]) for i in range(0, len(verts), 3)]
    lo = [min(p[a] for p in pts) for a in range(3)]
    hi = [max(p[a] for p in pts) for a in range(3)]
    size = [hi[a] - lo[a] for a in range(3)]
    centre = [(hi[a] + lo[a]) / 2 for a in range(3)]
    axis = max(range(3), key=lambda a: size[a])

    ahead = behind = 0
    ar = br = 0.0
    for p in pts:
        radial = math.sqrt(sum((p[a] - centre[a])**2 for a in range(3) if a != axis))
        if p[axis] >= centre[axis]:
            ar += radial; ahead += 1
        else:
            br += radial; behind += 1
    amean = ar / ahead if ahead else 0
    bmean = br / behind if behind else 0
    nose = '+' if amean <= bmean else '-'
    print('%-28s verts=%-5d axis=%s len=%7.2f  +half r=%6.3f  -half r=%6.3f  nose=%s%s'
          % (label.decode('utf8', 'replace')[:28], len(pts), 'XYZ'[axis], size[axis],
             amean, bmean, nose, 'XYZ'[axis]))
