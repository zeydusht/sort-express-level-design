"""Renders a level JSON the way the Unity prototype draws it (same sprites, same geometry).
Used to review designs offline and for the README pictures.

    python3 tools/render_level.py <level.json> <out.png> [resources_dir]
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFont

PPU = int(os.environ.get('PPU', 110))                     # pixels per world unit in the picture
XS = 0.5                       # the game draws level x at half scale (3-slot shelves 3.13 apart -> 1.565)
SW = 3.13 / 3 * XS             # slot width
ITEM = 0.92
LIP_TOP = -0.44
INNER_TOP = 0.465
FRONT_Y = LIP_TOP + ITEM / 2 + 0.005
BACK_Y = FRONT_Y + 0.10
BACK_SCALE = 0.84
FONT = '/usr/share/fonts/truetype/google-fonts/Poppins-Bold.ttf'


def nine_slice(img, w, h, l, b, r, t):
    """Scale a 9-slice sprite to w x h pixels (borders in source pixels, kept at source size)."""
    W, H = img.size
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    xs = [(0, l, 0, l), (l, W - r, l, w - r), (W - r, W, w - r, w)]
    ys = [(0, t, 0, t), (t, H - b, t, h - b), (H - b, H, h - b, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            piece = img.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS)
            out.alpha_composite(piece, (dx0, dy0))
    return out


def tint(img, rgb):
    r, g, b, a = img.split()
    r = r.point(lambda v: int(v * rgb[0]))
    g = g.point(lambda v: int(v * rgb[1]))
    b = b.point(lambda v: int(v * rgb[2]))
    return Image.merge('RGBA', (r, g, b, a))


def convex_hull(pts):
    pts = sorted(set(pts))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0:
            up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]


def mystery_stone(icon, art):
    """Same look as SortexArt.MysteryTexture: the item's convex hull in stone with a question mark."""
    im = np.array(icon.convert('RGBA'), float) / 255
    H, W = im.shape[:2]
    a = im[:, :, 3]
    pts = []
    for y in range(H):
        xs = np.where(a[y] > 0.5)[0]
        if len(xs):
            pts += [(xs[0], y), (xs[0], y + 1), (xs[-1] + 1, y), (xs[-1] + 1, y + 1)]
    hull = convex_hull(pts)
    if len(hull) < 3:
        return icon
    cx = sum(p[0] for p in hull) / len(hull)
    cy = sum(p[1] for p in hull) / len(hull)
    yy, xx = np.mgrid[0:H, 0:W] + 0.5
    dist = np.full((H, W), 1e9)
    nx = np.zeros((H, W))
    ny = np.zeros((H, W))
    for i in range(len(hull)):
        x0, y0 = hull[i]
        x1, y1 = hull[(i + 1) % len(hull)]
        ex, ey = x1 - x0, y1 - y0
        L = math.hypot(ex, ey) or 1
        inx, iny = -ey / L, ex / L
        if (cx - x0) * inx + (cy - y0) * iny < 0:
            inx, iny = -inx, -iny
        d = (xx - x0) * inx + (yy - y0) * iny
        upd = d < dist
        dist = np.where(upd, d, dist)
        nx = np.where(upd, -inx, nx)
        ny = np.where(upd, -iny, ny)
    alpha = np.clip(dist + 0.5, 0, 1)
    bevel = np.clip(dist / (W * 0.10), 0, 1)
    facing = nx * -0.55 + ny * -0.83
    light = 0.82 + 0.18 * bevel + (1 - bevel) ** 1.5 * (0.42 * facing - 0.12)
    stone = np.array(Image.open(os.path.join(art, 'mystery_stone.png')).convert('RGB'), float) / 255
    st = stone[(yy * 2).astype(int) % stone.shape[0], (xx * 2).astype(int) % stone.shape[1]].mean(axis=2)
    col = np.clip(np.array([0.60, 0.61, 0.68]) * ((0.62 + 0.62 * st) * light)[:, :, None], 0, 1)
    out = Image.fromarray((np.dstack([col, alpha]) * 255).astype(np.uint8), 'RGBA')
    q = Image.open(os.path.join(art, 'mystery_question.png')).convert('RGBA')
    qs = int(W * 0.44)
    q = q.resize((qs, qs), Image.LANCZOS)
    out.alpha_composite(q, (int(cx - qs / 2), int(cy - qs / 2 + H * 0.02)))
    out.putalpha(Image.fromarray((alpha * 255).astype(np.uint8)))
    return out


def horizontal_tube(src, length, thick):
    """The frame bar sprite scaled to a thickness, stretched to a length with its round ends kept."""
    k = thick / src.height
    cap = max(1, int(round(src.height / 2 * k)))
    scaled = src.resize((max(2, int(round(src.width * k))), thick), Image.LANCZOS)
    out = Image.new('RGBA', (max(length, 2 * cap), thick), (0, 0, 0, 0))
    out.paste(scaled.crop((0, 0, cap, thick)), (0, 0))
    out.paste(scaled.crop((scaled.width - cap, 0, scaled.width, thick)), (out.width - cap, 0))
    mid = scaled.crop((cap, 0, scaled.width - cap, thick)).resize((max(1, out.width - 2 * cap), thick), Image.LANCZOS)
    out.paste(mid, (cap, 0))
    return out


def render(level_path, out_path, res):
    lv = json.load(open(level_path))
    art = os.path.join(res, 'Art')
    theme = 'red' if lv['ld'] >= 20 else 'purple' if lv['ld'] >= 10 else 'blue'
    frame_src = Image.open(os.path.join(art, 'shelf_frame_%s.png' % theme))
    back_src = Image.open(os.path.join(art, 'shelf_back.png'))
    door_src = Image.open(os.path.join(art, 'lock_door.png'))
    ice_src = Image.open(os.path.join(art, 'frozen_ice.png'))
    shutter_src = Image.open(os.path.join(art, 'shutter.png'))
    cargo_src = Image.open(os.path.join(art, 'cargo_door.png'))
    shadow_src = Image.open(os.path.join(art, 'item_shadow.png'))
    panel_src = Image.open(os.path.join(art, 'ui_panel.png'))

    shelves = lv['s']
    for sh in shelves:
        sh['pos'] = dict(x=sh['pos']['x'] * XS, y=sh['pos']['y'])
    minx = min(s['pos']['x'] - s['sicw'] * SW / 2 for s in shelves)
    maxx = max(s['pos']['x'] + s['sicw'] * SW / 2 for s in shelves)
    miny = min(s['pos']['y'] - 0.5 for s in shelves)
    maxy = max(s['pos']['y'] + 0.5 for s in shelves)
    pad = 0.55
    W = int((maxx - minx + 2 * pad) * PPU)
    H = int((maxy - miny + 2 * pad) * PPU) + 70
    img = Image.new('RGBA', (W, H), (42, 75, 168, 255))

    def px(x, y):
        return (int((x - minx + pad) * PPU), int(H - 70 - (y - miny + pad) * PPU))

    def put(src, cx, cy, w, h, layer):
        s = src.resize((max(1, int(w * PPU)), max(1, int(h * PPU))), Image.LANCZOS)
        x, y = px(cx, cy)
        layer.alpha_composite(s, (x - s.width // 2, y - s.height // 2))

    # cabinet: one light plate per shelf (any cabinet shape)
    for sh in shelves:
        iw = sh['sicw'] * SW
        plate = nine_slice(panel_src, int((iw + 0.34) * PPU), int(1.30 * PPU), 34, 34, 34, 34)
        plate = tint(plate, (0x7F / 255, 0x9F / 255, 0xD3 / 255))
        x0, y0 = px(sh['pos']['x'] - (iw + 0.34) / 2, sh['pos']['y'] - 0.02 + 0.65)
        img.alpha_composite(plate, (x0, y0))

    layers = {k: Image.new('RGBA', (W, H), (0, 0, 0, 0)) for k in ('back', 'items_back', 'items', 'frame', 'door')}
    icon_cache = {}

    def icon(iid):
        if iid not in icon_cache:
            p = os.path.join(res, 'Items', iid + '.png')
            icon_cache[iid] = Image.open(p).convert('RGBA') if os.path.exists(p) else Image.new('RGBA', (128, 128), (255, 0, 255, 255))
        return icon_cache[iid]

    for s in shelves:
        X, Y, w = s['pos']['x'], s['pos']['y'], s['sicw']
        iw = w * SW
        wa = min((1, 3, 4, 5, 6), key=lambda n: abs(n - w))
        b = Image.open(os.path.join(art, 'shelf_w%d_back.png' % wa)).resize((int((iw + 0.30) * PPU), int(1.30 * PPU)), Image.LANCZOS)
        bx, by = px(X - (iw + 0.30) / 2, Y + 0.65)
        layers['back'].alpha_composite(b, (bx, by))

        lay = [l['rd'] for l in s['si']]
        while len(lay) > 1 and all(c.get('it') != 1 for c in lay[0]):
            lay = lay[1:]
        inner_h = INNER_TOP - LIP_TOP
        if s.get('isdp') and len(lay) > 1:
            put(cargo_src, X, Y + LIP_TOP + inner_h / 2, iw, inner_h, layers['back'])
        for li, layer in enumerate(lay[:2]):
            if li == 1 and s.get('isdp'):
                continue
            for k, c in enumerate(layer):
                if c.get('it') != 1:
                    continue
                cx = X - iw / 2 + SW * (k + 0.5)
                pr = c.get('pr', {})
                src = icon(c['iid'])
                if pr.get('imy'):
                    src = mystery_stone(src, art)
                if li == 0:
                    put(shadow_src, cx, Y + LIP_TOP + 0.02, 0.87, 0.29, layers['items'])
                    put(src, cx, Y + FRONT_Y, ITEM, ITEM, layers['items'])
                    if pr.get('ib'):
                        put(ice_src, cx, Y + FRONT_Y, ITEM * 1.06, ITEM * 1.06, layers['items'])
                else:
                    dark = tint(src, (0.33, 0.38, 0.52))
                    put(dark, cx, Y + BACK_Y, ITEM * BACK_SCALE, ITEM * BACK_SCALE, layers['items_back'])
        if s.get('icl'):
            put(shutter_src, X, Y + INNER_TOP - 0.03, iw, 0.06, layers['frame'])
        hidden = 0 if s.get('isdp') else max(0, len(lay) - 2)
        d = ImageDraw.Draw(layers['frame'])
        for i in range(hidden):
            cx = X + (i - (hidden - 1) / 2) * 0.11
            x, y = px(cx, Y - 0.485)
            d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(50, 75, 125, 230))
        if s.get('lmc', 0) > 0:
            put(door_src, X, Y + (LIP_TOP + INNER_TOP) / 2, iw, INNER_TOP - LIP_TOP, layers['door'])
            dd = ImageDraw.Draw(layers['door'])
            font = ImageFont.truetype(FONT, int(0.42 * PPU))
            x, y = px(X, Y + (LIP_TOP + INNER_TOP) / 2 + 0.01)
            dd.text((x, y), str(s.get('lmc', 0)), font=font, fill=(255, 255, 255, 255), anchor='mm')

    # one shared frame of bars, like BoardView.BuildFrame: collinear shelf edges that touch become one bar
    tube_src = Image.open(os.path.join(art, 'shelf_tube.png')).convert('RGBA')
    T = 0.08

    def merge(segs):
        segs = sorted(segs, key=lambda q: (round(q[0], 2), q[1]))
        out = []
        for q in segs:
            if out and abs(out[-1][0] - q[0]) <= 0.01 and q[1] <= out[-1][2] + 0.01:
                out[-1] = (out[-1][0], out[-1][1], max(out[-1][2], q[2]))
            else:
                out.append(q)
        return out

    hs, vs = [], []
    for sh in shelves:
        X, Y, iw = sh['pos']['x'], sh['pos']['y'], sh['sicw'] * SW
        hs += [(Y + 0.5, X - iw / 2, X + iw / 2), (Y - 0.5, X - iw / 2, X + iw / 2)]
        vs += [(X - iw / 2, Y - 0.5, Y + 0.5), (X + iw / 2, Y - 0.5, Y + 0.5)]
    th = max(2, int(round(T * PPU)))
    for line, a, b in merge(vs):
        length = int(round((b - a + T) * PPU))
        bar = horizontal_tube(tube_src, length, th).rotate(90, expand=True)
        x, y = px(line, (a + b) / 2)
        layers['frame'].alpha_composite(bar, (x - bar.width // 2, y - bar.height // 2))
    for line, a, b in merge(hs):
        length = int(round((b - a + T) * PPU))
        bar = horizontal_tube(tube_src, length, th)
        x, y = px((a + b) / 2, line)
        layers['frame'].alpha_composite(bar, (x - bar.width // 2, y - bar.height // 2))

    for k in ('back', 'items_back', 'items', 'frame', 'door'):
        img.alpha_composite(layers[k])

    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, 30)
    n_items = sum(1 for s in shelves for l in s['si'] for c in l['rd'] if c.get('it') == 1)
    d.text((W // 2, H - 38), '%s  ·  %d shelves  ·  %d items  ·  %d s' % (lv.get('nm', 'Level'), len(shelves), n_items, lv['tl']),
           font=font, fill=(236, 175, 36, 255), anchor='mm')
    img.convert('RGB').save(out_path)


if __name__ == '__main__':
    here = os.path.dirname(os.path.abspath(__file__))
    default = os.path.join(here, '..', 'Assets', 'Sortex', 'Resources', 'Sortex')
    if not os.path.isdir(default):
        default = os.path.join(here, '..', 'unity', 'Assets', 'Sortex', 'Resources', 'Sortex')
    render(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else default)
