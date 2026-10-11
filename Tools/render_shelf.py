"""Renders Sort Express' real shelf (GameObject/BaseShelf.prefab + its meshes) into 2D sprites.

The prefab is a 3D box: bottom/top planks, side walls, a back wall and rounded front edges / corners.
It is rendered with a perspective camera in front of the shelf (like the game's camera), in two layers:
  shelf_w<N>_back.png   inside of the box (back wall, floor, ceiling, inner walls)  -> drawn behind items
  shelf_w<N>_front.png  front rim (faces looking at the camera)                     -> drawn over items
for N = 1, 3, 4, 5, 6 slots (slot = 3.13 / 3 / 2 world units, shelf height = 1 unit). 128 pixels per unit.
The levels place 3-slot shelves 3.13 apart and rows 1 apart; the game draws x at half that scale (a 3-slot shelf
is about 1.6 x 1 on screen), so the slot here is half the level spacing.

    python3 tools/render_shelf.py <ExportedProject/Assets> <out_dir>
"""
import glob
import math
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meshrender import parse_mesh  # noqa: E402
from prefab_dump import build  # noqa: E402

PPU = 128
SS = 4
SLOT = 3.13 / 3 * 0.5          # world units per slot (level x spacing x 0.5)
D = 7.0                      # camera distance in front of the shelf's front plane
FRONT_Z = -0.75              # z of the front plane in prefab space

RIM = (196, 218, 246)        # front rim (light blue, live game)
INNER = (64, 92, 140)        # inner walls / floor / ceiling
BACK = (38, 56, 92)          # back wall


def cube():
    v, n, t = [], [], []
    faces = [((1, 0, 0), (0, 1, 0), (0, 0, 1)), ((-1, 0, 0), (0, 0, 1), (0, 1, 0)),
             ((0, 1, 0), (0, 0, 1), (1, 0, 0)), ((0, -1, 0), (1, 0, 0), (0, 0, 1)),
             ((0, 0, 1), (1, 0, 0), (0, 1, 0)), ((0, 0, -1), (0, 1, 0), (1, 0, 0))]
    for nrm, a, b in faces:
        base = len(v)
        for sa, sb in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            v.append(tuple(0.5 * nrm[i] + 0.5 * sa * a[i] + 0.5 * sb * b[i] for i in range(3)))
            n.append(nrm)
        t += [(base, base + 1, base + 2), (base, base + 2, base + 3)]
    return v, n, t


def load_meshes(assets):
    guid2path = {}
    for m in glob.glob(os.path.join(assets, 'Mesh', '*.meta')):
        g = re.search(r'guid: (\w+)', open(m).read()).group(1)
        guid2path[g] = m[:-5]
    return guid2path


def norm(a):
    l = math.sqrt(sum(x * x for x in a)) or 1.0
    return tuple(x / l for x in a)


def collect(assets):
    guid2path = load_meshes(assets)
    parts = []
    for o in build(os.path.join(assets, 'GameObject', 'BaseShelf.prefab')):
        p = o['path']
        if not o['active'] or '/Models/' not in p or '/Gradients/' in p or 'BehindBlackWall' in p:
            continue
        fid, guid = o['mesh']
        if guid and guid.startswith('0000000000000000e'):
            if fid != '10202':
                continue
            pos, nrm, tris = cube()
        else:
            path = guid2path.get(guid)
            if not path or not os.path.exists(path):
                print('missing mesh for', p)
                continue
            pos, nrm, tris = parse_mesh(path)
        M = o['M']
        cols = [[M[r][c] for r in range(3)] for c in range(3)]
        scales = [math.sqrt(sum(x * x for x in col)) or 1e-9 for col in cols]
        R = [[M[r][c] / scales[c] for c in range(3)] for r in range(3)]
        wv = [tuple(sum(M[r][c] * v[c] for c in range(3)) + M[r][3] for r in range(3)) for v in pos]
        wn = []
        for nv in nrm:
            ns = [nv[c] / scales[c] for c in range(3)]
            wn.append(norm(tuple(sum(R[r][c] * ns[c] for c in range(3)) for r in range(3))))
        name = p.split('/')[-1]
        spans = name in ('Base', 'Top', 'BaseFront', 'TopFront') or name.startswith('Back')
        center_x = M[0][3]
        parts.append(dict(name=name, v=wv, n=wn, t=tris, spans=spans, cx=center_x,
                          back=name.startswith('Back')))
    return parts


def render(parts, slots, out_dir):
    inner_half = 1.28 - 0.035          # prefab: walls at +-1.28, 0.07 thick
    target_half = slots * SLOT / 2      # inner half width we want
    k = target_half / inner_half
    W = slots * SLOT + 0.30
    H = 1.30
    w, h = int(W * PPU) * SS, int(H * PPU) * SS
    layers = {'back': Image.new('RGBA', (w, h), (0, 0, 0, 0)), 'front': Image.new('RGBA', (w, h), (0, 0, 0, 0))}
    draws = {kk: ImageDraw.Draw(im) for kk, im in layers.items()}
    cam = (0.0, 0.5, FRONT_Z - D)
    f = D
    L = norm((-0.35, 0.55, -0.75))
    faces = []
    for p in parts:
        for tri in p['t']:
            vs = []
            for i in tri:
                x, y, z = p['v'][i]
                if p['spans']:
                    x = x * k
                else:
                    x = x + (abs(p['cx']) * k - abs(p['cx'])) * (1 if p['cx'] > 0 else -1 if p['cx'] < 0 else 0)
                vs.append((x, y, z))
            n = norm(tuple(sum(p['n'][i][a] for i in tri) for a in range(3)))
            centroid = tuple(sum(v[a] for v in vs) / 3 for a in range(3))
            to_cam = norm(tuple(cam[a] - centroid[a] for a in range(3)))
            if sum(n[a] * to_cam[a] for a in range(3)) <= 0.02:
                continue                                     # back face
            front = n[2] < -0.6 and centroid[2] < FRONT_Z + 0.2 and not p['back']
            lam = max(0.0, sum(n[a] * L[a] for a in range(3)))
            if p['back']:
                col, shade = BACK, 0.85 + 0.15 * lam
            elif front:
                col, shade = RIM, 0.78 + 0.22 * lam
            else:
                col, shade = INNER, 0.55 + 0.6 * lam
            c = tuple(min(255, int(col[a] * shade)) for a in range(3)) + (255,)
            pts = []
            for (x, y, z) in vs:
                dz = z - cam[2]
                sx = (x - cam[0]) * f / dz
                sy = (y - cam[1]) * f / dz
                pts.append(((sx + W / 2) * PPU * SS, (H / 2 - sy) * PPU * SS))
            depth = sum(v[2] for v in vs) / 3
            faces.append((depth, 'front' if front else 'back', pts, c))
    faces.sort(key=lambda fc: -fc[0])
    for depth, layer, pts, c in faces:
        draws[layer].polygon(pts, fill=c)
    for name, im in layers.items():
        im = im.resize((w // SS, h // SS), Image.LANCZOS)
        if name == 'front':
            # the live game shows a slightly thicker rim than the raw prefab: grow it by ~2 px
            grown = im.filter(ImageFilter.MaxFilter(5))
            im = Image.alpha_composite(grown, im)
        if name == 'back':
            # soft shadow at the top of the inside (AmbientShadowTop in the real prefab)
            shade = Image.new('L', im.size, 0)
            sd = ImageDraw.Draw(shade)
            for i in range(26):
                y0 = int((H / 2 - 0.47) * PPU) + i
                sd.line((0, y0, im.size[0], y0), fill=int(110 * (1 - i / 26)))
            dark = Image.new('RGBA', im.size, (8, 12, 26, 255))
            dark.putalpha(Image.composite(shade, Image.new('L', im.size, 0), im.split()[3]))
            im = Image.alpha_composite(im, dark)
        im.save(os.path.join(out_dir, 'shelf_w%d_%s.png' % (slots, name)))
    return W, H


if __name__ == '__main__':
    assets, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    parts = collect(assets)
    print(len(parts), 'mesh parts')
    for n in (1, 3, 4, 5, 6):
        print(n, render(parts, n, out))
