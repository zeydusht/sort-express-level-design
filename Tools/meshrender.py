import re, struct, sys, math
from PIL import Image, ImageDraw

def parse_mesh(path):
    t = open(path).read()
    vc = int(re.search(r'm_VertexCount: (\d+)', t).group(1))
    ch_block = t[t.index('m_Channels:'):t.index('m_DataSize') if 'm_DataSize' in t else t.index('_typelessdata')]
    chans = re.findall(r'stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', ch_block)
    chans = [tuple(map(int, c)) for c in chans]
    fmt_size = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 8: 2, 9: 2, 10: 4, 11: 4}
    stride = 0
    for s, o, f, d in chans:
        if s == 0 and d > 0:
            stride = max(stride, o + fmt_size[f] * (d & 0xF))
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', t).group(1))
    idx = bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', t).group(1))
    fmt16 = re.search(r'm_IndexFormat: (\d)', t).group(1) == '0'
    inds = struct.unpack('<%dH' % (len(idx)//2), idx) if fmt16 else struct.unpack('<%dI' % (len(idx)//4), idx)
    pos = []; nrm = []
    for i in range(vc):
        b = i * stride
        pos.append(struct.unpack_from('<3f', data, b + chans[0][1]))
        nrm.append(struct.unpack_from('<3f', data, b + chans[1][1]) if len(chans) > 1 and chans[1][3] == 3 else (0, 0, 1))
    tris = [inds[i:i+3] for i in range(0, len(inds) - 2, 3)]
    return pos, nrm, tris

def render(meshes, size=512, view='front', bg=(0,0,0,0), pad=0.05):
    # meshes: list of (pos, nrm, tris, color, offset)
    allp = []
    for pos, nrm, tris, col, off in meshes:
        allp += [(p[0]+off[0], p[1]+off[1], p[2]+off[2]) for p in pos]
    def proj(p):
        x, y, z = p
        if view == 'front': return (x, y, z)
        if view == 'back': return (-x, y, -z)
        if view == 'top': return (x, -z, y)
        if view == 'side': return (z, y, -x)
    P = [proj(p) for p in allp]
    minx = min(p[0] for p in P); maxx = max(p[0] for p in P)
    miny = min(p[1] for p in P); maxy = max(p[1] for p in P)
    span = max(maxx-minx, maxy-miny) * (1+2*pad)
    cx = (minx+maxx)/2; cy = (miny+maxy)/2
    S = size*2
    img = Image.new('RGBA', (S, S), bg)
    dr = ImageDraw.Draw(img)
    faces = []
    L = (0.35, 0.55, -0.75); ln = math.sqrt(sum(c*c for c in L)); L = tuple(c/ln for c in L)
    for pos, nrm, tris, col, off in meshes:
        for t in tris:
            ps = [proj((pos[i][0]+off[0], pos[i][1]+off[1], pos[i][2]+off[2])) for i in t]
            ns = [proj(nrm[i]) for i in t]
            n = tuple(sum(c[k] for c in ns)/3 for k in range(3))
            # face normal from geometry
            ax = [ps[1][k]-ps[0][k] for k in range(3)]; bx = [ps[2][k]-ps[0][k] for k in range(3)]
            fn = (ax[1]*bx[2]-ax[2]*bx[1], ax[2]*bx[0]-ax[0]*bx[2], ax[0]*bx[1]-ax[1]*bx[0])
            z = sum(p[2] for p in ps)/3
            lam = max(0.0, -(n[0]*L[0]+n[1]*L[1]+n[2]*L[2]))
            shade = 0.45 + 0.55*lam
            c = tuple(min(255, int(col[k]*shade)) for k in range(3)) + (255,)
            pts = [((p[0]-cx)/span*S + S/2, S/2 - (p[1]-cy)/span*S) for p in ps]
            faces.append((z, pts, c))
    faces.sort(key=lambda f: -f[0])  # far first (larger z farther when camera looks +z)
    for z, pts, c in faces:
        dr.polygon(pts, fill=c)
    return img.resize((size, size), Image.LANCZOS)

if __name__ == '__main__':
    out = sys.argv[1]
    ms = []
    for a in sys.argv[2:]:
        path, col = a.split('@')
        pos, nrm, tris = parse_mesh(path)
        ms.append((pos, nrm, tris, tuple(int(col[i:i+2], 16) for i in (0, 2, 4)), (0, 0, 0)))
    imgs = [render(ms, 300, v, (40, 60, 95, 255)) for v in ('front', 'back', 'top', 'side')]
    sheet = Image.new('RGBA', (1220, 300), (255, 255, 255, 255))
    for i, im in enumerate(imgs): sheet.paste(im, (i*305, 0))
    sheet.save(out)
