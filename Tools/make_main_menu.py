"""Rebuilds Sort Express' home screen from the game's own MainScene.prefab (AssetRipper export).

    python3 tools/make_main_menu.py <export Assets folder> <SortexIndex.tsv> <out Resources/Sortex/Menu> [preview.png]

The index (guid -> file) comes from Unity: Sortex > Tools > Index Asset Export (Editor/SortexExportIndex.cs).

Reads the HomePage (background, LoadLevelButton, TopUI) and the NavigationBar of the prefab: every GameObject's
local position / scale / rotation, its SpriteRenderer (sprite, sliced size, flip, colour, sorting order) and its
SortingGroup, plus the TextMeshPro text boxes and their materials (face / outline colours). Writes
  menu.json        the node tree per screen part + sprite table + text styles (read by Scripts/Game/MainMenuView.cs)
  <Sprite>.png     every sprite cut out of its atlas
and optionally renders a preview of a 1080 x 1920 phone screen with the same sorting rules as Unity.
"""
import json
import math
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_yaml import load, ref  # noqa: E402

REF_WIDTH = 4.616601        # the game's screen width in world units (its menu pages sit exactly this far apart)
REF_HEIGHT = 10.0           # reference screen height (Foreground collider: 4.6166 x 10)
PARTS = {
    # name: (path in the prefab, anchor, offset from that screen edge in the reference screen)
    'background': ('MainScene/MainSceneUIContainer/MainMenuContentContainer/HomePage/MainSceneBackground', 'center', 0.0),
    'levelButton': ('MainScene/MainSceneUIContainer/MainMenuContentContainer/HomePage/LoadLevelButton', 'bottom', None),
    'topUI': ('MainScene/MainSceneUIContainer/MainMenuContentContainer/HomePage/TopUI', 'top', None),
    'navigation': ('MainScene/MainSceneUIContainer/NavigationBar', 'bottom', None),
}
EXTRA_SPRITES = ['PlayButtonHardLevel', 'PlayButtonHardLevelBackground', 'PlayButtonSuperHardLevel', 'PlayButtonSuperHardLevelBackground']
SKIP = {'LiveOpsContainer', 'LoadLevelButtonUpdateVisualParticle', 'Particles', 'NotificationBadge', 'Foreground'}


def num(v, d=0.0):
    try:
        return float(v)
    except (TypeError, ValueError):
        return d


def zrot(q):
    if not q:
        return 0.0
    x, y, z, w = (num(q.get(k)) for k in 'xyzw')
    return math.degrees(math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z)))


def color(c, default=(1, 1, 1, 1)):
    if not isinstance(c, dict):
        return list(default)
    return [round(num(c.get(k), d), 4) for k, d in zip('rgba', default)]


class Prefab:
    def __init__(self, path):
        self.objs = load(path)
        self.tf_of = {}
        for fid, o in self.objs.items():
            if o['type'] in ('Transform', 'RectTransform'):
                self.tf_of[ref(o['data'].get('m_GameObject'))] = fid

    def go(self, tf):
        return ref(self.objs[tf]['data'].get('m_GameObject'))

    def name(self, tf):
        return self.objs[self.go(tf)]['data'].get('m_Name')

    def children(self, tf):
        return [ref(c) for c in self.objs[tf]['data'].get('m_Children') or [] if ref(c) in self.objs]

    def find(self, path):
        cur = [t for t, o in self.objs.items()
               if o['type'] in ('Transform', 'RectTransform') and ref(o['data'].get('m_Father')) == '0']
        node = None
        for part in path.split('/'):
            node = next((t for t in cur if self.name(t) == part), None)
            if node is None:
                raise KeyError(path + ' (missing ' + part + ')')
            cur = self.children(node)
        return node

    def nodes(self, root):
        out = []

        def walk(tf, parent):
            o = self.objs[tf]
            d = o['data']
            g = self.objs[self.go(tf)]['data']
            name = g.get('m_Name')
            if name in SKIP:
                return
            p = d.get('m_LocalPosition') or {}
            s = d.get('m_LocalScale') or {}
            n = dict(name=name, parent=parent, active=str(g.get('m_IsActive', 1)) == '1',
                     pos=[num(p.get('x')), num(p.get('y')), num(p.get('z'))],
                     scale=[num(s.get('x'), 1), num(s.get('y'), 1)], rot=round(zrot(d.get('m_LocalRotation')), 3))
            rect = None
            if o['type'] == 'RectTransform':
                rect = dict(size=[num(d['m_SizeDelta']['x']), num(d['m_SizeDelta']['y'])],
                            anchored=[num(d['m_AnchoredPosition']['x']), num(d['m_AnchoredPosition']['y'])])
            for c in g.get('m_Component', []):
                co = self.objs.get(ref(c.get('component')))
                if not co:
                    continue
                cd = co['data']
                if co['type'] == 'SpriteRenderer':
                    spr = cd.get('m_Sprite') or {}
                    n['sprite'] = dict(guid=spr.get('guid'), color=color(cd.get('m_Color')),
                                       flipX=str(cd.get('m_FlipX')) == '1', flipY=str(cd.get('m_FlipY')) == '1',
                                       drawMode=int(num(cd.get('m_DrawMode'))),
                                       size=[num(cd['m_Size']['x']), num(cd['m_Size']['y'])],
                                       layer=int(num(cd.get('m_SortingLayer'))), order=int(num(cd.get('m_SortingOrder'))),
                                       enabled=str(cd.get('m_Enabled', 1)) == '1')
                elif co['type'] == 'SortingGroup':
                    n['group'] = dict(layer=int(num(cd.get('m_SortingLayer'))), order=int(num(cd.get('m_SortingOrder'))))
                elif co['type'] == 'MeshRenderer' and rect is not None:
                    mats = [m.get('guid') for m in cd.get('m_Materials') or []]
                    n['text'] = dict(material=mats[0] if mats else None, size=rect['size'], anchored=rect['anchored'],
                                     layer=int(num(cd.get('m_SortingLayer'))), order=int(num(cd.get('m_SortingOrder'))))
            out.append(n)
            index = len(out) - 1
            for c in self.children(tf):
                walk(c, index)

        walk(root, -1)
        return out


def read_sprite(path):
    t = open(path, encoding='utf-8').read()

    def rect(key):
        m = re.search(key + r':\s*\n\s*serializedVersion: 2\s*\n\s*x: ([-\d.e]+)\s*\n\s*y: ([-\d.e]+)\s*\n\s*width: ([-\d.e]+)\s*\n\s*height: ([-\d.e]+)', t)
        return [float(v) for v in m.groups()]

    def vec(key, n):
        m = re.search(key + r': \{([^}]*)\}', t)
        parts = {k.strip(): v for k, v in (kv.split(':') for kv in m.group(1).split(','))}
        return [float(parts[k].strip()) for k in 'xyzw'[:n]]

    return dict(name=re.search(r'm_Name: (.*)', t).group(1).strip(),
                rect=rect('m_Rect'), textureRect=rect('textureRect'), border=vec('m_Border', 4), pivot=vec('m_Pivot', 2),
                ppu=float(re.search(r'm_PixelsToUnits: ([\d.]+)', t).group(1)),
                texture=re.search(r'm_RD:\s*\n\s*serializedVersion: \d+\s*\n\s*texture: \{fileID: \d+, guid: (\w+)', t).group(1),
                settings=int(re.search(r'settingsRaw: (\d+)', t).group(1)))


def read_material(path):
    t = open(path, encoding='utf-8').read()

    def flt(k, d=0.0):
        m = re.search(r'\b' + k + r': ([-\d.e]+)', t)
        return float(m.group(1)) if m else d

    def col(k):
        m = re.search(r'\b' + k + r': \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+), a: ([-\d.e]+)\}', t)
        return [round(float(v), 4) for v in m.groups()] if m else None

    return dict(name=re.search(r'm_Name: (.*)', t).group(1).strip(),
                face=col('_FaceColor'), outline=col('_OutlineColor') if 'OUTLINE_ON' in t else None,
                outlineWidth=flt('_OutlineWidth') if 'OUTLINE_ON' in t else 0.0,
                underlay=col('_UnderlayColor') if 'UNDERLAY_ON' in t else None,
                underlayOffset=[flt('_UnderlayOffsetX'), flt('_UnderlayOffsetY')], dilate=flt('_FaceDilate'))


def main():
    export, index_file, out_dir = sys.argv[1:4]
    preview = sys.argv[4] if len(sys.argv) > 4 else None
    index = {}
    for line in open(index_file, encoding='utf-8'):
        guid, rel, _ = line.rstrip('\n').split('\t')
        index[guid] = rel
    prefab = Prefab(os.path.join(export, 'GameObject', 'MainScene.prefab'))

    parts = {}
    for key, (path, anchor, _) in PARTS.items():
        parts[key] = dict(anchor=anchor, nodes=prefab.nodes(prefab.find(path)))
        # the HomePage parts share the sorting group of MainMenuContentContainer (the navigation bar draws above it)
        container = 'MainScene/MainSceneUIContainer/MainMenuContentContainer'
        if path.startswith(container + '/'):
            group = prefab.nodes(prefab.find(container))[0].get('group')
            parts[key]['container'] = dict(name='MainMenuContentContainer', group=group)

    # anchors: distance of each part root from the screen edge it follows, in the 4.6166 x 10 reference screen
    for key, part in parts.items():
        y = part['nodes'][0]['pos'][1]
        if part['anchor'] == 'top':
            part['offset'] = round(y - REF_HEIGHT / 2, 4)
        elif part['anchor'] == 'bottom':
            part['offset'] = round(y + REF_HEIGHT / 2, 4)
        else:
            part['offset'] = round(y, 4)

    os.makedirs(out_dir, exist_ok=True)
    sprites, styles, atlases = {}, {}, {}
    for part in parts.values():
        for n in part['nodes']:
            s = n.get('sprite')
            if s and s['guid'] in index:
                info = read_sprite(os.path.join(export, index[s['guid']]))
                s['key'] = info['name']
                if info['name'] not in sprites:
                    tex_path = os.path.join(export, index[info['texture']])
                    if tex_path not in atlases:
                        atlases[tex_path] = Image.open(tex_path).convert('RGBA')
                    atlas = atlases[tex_path]
                    x, y, w, h = info['textureRect']
                    top = atlas.height - (y + h)
                    img = atlas.crop((int(round(x)), int(round(top)), int(round(x + w)), int(round(top + h))))
                    if info['settings'] >> 2 & 15:
                        raise SystemExit(info['name'] + ': rotated atlas packing is not supported')
                    if info['name'] == 'MainSceneBackground':
                        img.convert('RGB').save(os.path.join(out_dir, info['name'] + '.jpg'), quality=90)
                    else:
                        img.save(os.path.join(out_dir, info['name'] + '.png'))
                    sprites[info['name']] = dict(ppu=info['ppu'], pivot=info['pivot'], border=info['border'],
                                                 width=img.width, height=img.height,
                                                 jpg=info['name'] == 'MainSceneBackground')
            t = n.get('text')
            if t:
                mat = t.pop('material')
                if mat in index and index[mat].endswith('.mat'):
                    m = read_material(os.path.join(export, index[mat]))
                    t['style'] = m['name'].replace('Bango Pro SDF - ', '')
                    styles[t['style']] = m
                else:
                    t['style'] = 'Default'
    # variants the game swaps in at runtime: the play button of Hard and Super Hard levels
    by_name = {rel[len('Sprite/'):-len('.asset')]: rel for rel in index.values() if rel.startswith('Sprite/') and rel.endswith('.asset')}
    for name in EXTRA_SPRITES:
        if name in sprites or name not in by_name:
            continue
        info = read_sprite(os.path.join(export, by_name[name]))
        tex_path = os.path.join(export, index[info['texture']])
        if tex_path not in atlases:
            atlases[tex_path] = Image.open(tex_path).convert('RGBA')
        atlas = atlases[tex_path]
        x, y, w, h = info['textureRect']
        top = atlas.height - (y + h)
        img = atlas.crop((int(round(x)), int(round(top)), int(round(x + w)), int(round(top + h))))
        img.save(os.path.join(out_dir, name + '.png'))
        sprites[name] = dict(ppu=info['ppu'], pivot=info['pivot'], border=info['border'], width=img.width, height=img.height, jpg=False)
    styles.setdefault('Default', dict(name='Default', face=[1, 1, 1, 1], outline=[0.0627, 0.2350, 0.6706, 1], outlineWidth=0.2,
                                      underlay=None, underlayOffset=[0, 0], dilate=0))
    for part in parts.values():
        for n in part['nodes']:
            if 'sprite' in n:
                n['sprite'].pop('guid', None)
    data = dict(source='Sort Express MainScene.prefab (HomePage + NavigationBar)', refWidth=REF_WIDTH, refHeight=REF_HEIGHT,
                font='Bango Pro', parts=parts, sprites=sprites, styles=styles)
    json.dump(data, open(os.path.join(out_dir, 'menu.json'), 'w'), indent=1)
    print(len(sprites), 'sprites,', len(styles), 'text styles,', sum(len(p['nodes']) for p in parts.values()), 'nodes')
    if preview:
        render(data, out_dir, os.path.join(export, 'Font', 'Bango Pro.otf'), preview)


# ====================================================================== preview (same rules as MainMenuView.cs)
TEXTS = {
    'LoadLevelButton/Root/Button/PressDownTransform/DefaultText/ShadowText': 'Level 1',
    'LoadLevelButton/Root/Button/PressDownTransform/DefaultText/BaseText': 'Level 1',
    'TopUI/LivesButton/Button/PressDownTransform/LifeTimerContainer/Icon/IconText': '5',
    'TopUI/LivesButton/Button/PressDownTransform/LifeTimerContainer/FullText': 'Full',
    'TopUI/CoinsButton/Button/PressDownTransform/Text': '1000',
    'NavigationBar/Root/HomeButton/Text': 'Home',
}
HIDDEN = {'TopUI/LivesButton/Button/PressDownTransform/LifeTimerContainer/TimerText'}


def paths(nodes):
    out = []
    for i, n in enumerate(nodes):
        p, j = [], i
        while j >= 0:
            p.append(nodes[j]['name'])
            j = nodes[j]['parent']
        out.append('/'.join(reversed(p)))
    return out


def render(data, out_dir, font_path, out_file, width_px=1080, height_px=1920):
    ppu_screen = width_px / data['refWidth']
    half_h = height_px / ppu_screen / 2
    canvas = Image.new('RGBA', (width_px, height_px), (40, 60, 120, 255))
    items = []   # (sort key, draw callable)
    seq = [0]

    for key, part in data['parts'].items():
        nodes = part['nodes']
        root_y = {'top': half_h + part['offset'], 'bottom': -half_h + part['offset'], 'center': part['offset']}[part['anchor']]
        names = paths(nodes)
        world = [None] * len(nodes)
        visible = [False] * len(nodes)
        groups = [None] * len(nodes)
        for i, n in enumerate(nodes):
            if n['parent'] < 0:
                pm = (n['pos'][0], root_y, n['scale'][0], n['scale'][1], n['rot'])
                visible[i] = n['active']
                c = part.get('container')
                gpath = ((c['group']['layer'], c['group']['order'], -1),) if c and c.get('group') else ()
            else:
                px, py, sx, sy, r = world[n['parent']]
                a = math.radians(r)
                lx, ly = n['pos'][0] * sx, n['pos'][1] * sy
                pm = (px + lx * math.cos(a) - ly * math.sin(a), py + lx * math.sin(a) + ly * math.cos(a),
                      sx * n['scale'][0], sy * n['scale'][1], r + n['rot'])
                visible[i] = visible[n['parent']] and n['active']
                gpath = groups[n['parent']]
            if 'group' in n:
                gpath = gpath + ((n['group']['layer'], n['group']['order'], seq[0]),)
                seq[0] += 1
            groups[i] = gpath
            world[i] = pm
        for i, n in enumerate(nodes):
            if not visible[i] or names[i] in HIDDEN:
                continue
            x, y, sx, sy, r = world[i]
            if 'sprite' in n and n['sprite'].get('enabled', True) and n['sprite'].get('key') in data['sprites']:
                s = n['sprite']
                k = groups[i] + ((s['layer'], s['order'], seq[0]),)
                seq[0] += 1
                items.append((k, ('sprite', s, x, y, sx, sy)))
            if 'text' in n and names[i] in TEXTS:
                t = dict(n['text'])
                if n['name'] == 'ShadowText':
                    # like MainMenuView.cs: the shadow takes the darkened outline colour of its BaseText
                    base = next((m['text'] for j, m in enumerate(nodes) if names[j] == names[i].rsplit('/', 1)[0] + '/BaseText'), None)
                    bs = data['styles'].get(base['style'] if base else '', {})
                    dark = [v * 0.7 for v in (bs.get('outline') or [0.08, 0.55, 0.06])[:3]] + [1]
                    t['styleData'] = dict(face=dark, outline=dark, outlineWidth=bs.get('outlineWidth', 0.2), underlay=None)
                k = groups[i] + ((t['layer'], t['order'], seq[0]),)
                seq[0] += 1
                items.append((k, ('text', t, x, y, sx, sy, TEXTS[names[i]])))

    def to_px(wx, wy):
        return width_px / 2 + wx * ppu_screen, height_px / 2 - wy * ppu_screen

    cache = {}

    def sprite_img(name):
        if name not in cache:
            meta = data['sprites'][name]
            cache[name] = Image.open(os.path.join(out_dir, name + ('.jpg' if meta.get('jpg') else '.png'))).convert('RGBA')
        return cache[name]

    def sliced(img, meta, w_px, h_px, scale):
        l, b, r_, t = [v * scale for v in meta['border']]
        L, B, R, T = meta['border']
        W, H = img.size
        out = Image.new('RGBA', (max(1, int(w_px)), max(1, int(h_px))))
        cols = [(0, L, 0, l), (L, W - R, l, w_px - r_), (W - R, W, w_px - r_, w_px)]
        rows = [(0, T, 0, t), (T, H - B, t, h_px - b), (H - B, H, h_px - b, h_px)]
        for sx0, sx1, dx0, dx1 in cols:
            for sy0, sy1, dy0, dy1 in rows:
                if sx1 - sx0 < 1 or sy1 - sy0 < 1 or dx1 - dx0 < 1 or dy1 - dy0 < 1:
                    continue
                part = img.crop((int(sx0), int(sy0), int(sx1), int(sy1))).resize((int(round(dx1 - dx0)), int(round(dy1 - dy0))), Image.LANCZOS)
                out.alpha_composite(part, (int(round(dx0)), int(round(dy0))))
        return out

    font_cache = {}

    def font(px):
        px = max(4, int(px))
        if px not in font_cache:
            font_cache[px] = ImageFont.truetype(font_path, px)
        return font_cache[px]

    for _, item in sorted(items, key=lambda it: it[0]):
        if item[0] == 'sprite':
            _, s, x, y, sx, sy = item
            meta = data['sprites'][s['key']]
            img = sprite_img(s['key'])
            if s['key'] == 'MainSceneBackground':
                # cover the whole screen, centred (the game swaps the placeholder for this picture)
                scale = max(width_px / img.width, height_px / img.height)
                bg = img.resize((int(img.width * scale), int(img.height * scale)), Image.LANCZOS)
                canvas.alpha_composite(bg, ((width_px - bg.width) // 2, (height_px - bg.height) // 2))
                continue
            unit = ppu_screen / meta['ppu']           # screen pixels per sprite pixel
            if s['drawMode'] in (1, 2):
                w_world, h_world = s['size'][0] * abs(sx), s['size'][1] * abs(sy)
                spx = sliced(img, meta, w_world * ppu_screen, h_world * ppu_screen, unit * abs(sx))
            else:
                w_world, h_world = img.width / meta['ppu'] * abs(sx), img.height / meta['ppu'] * abs(sy)
                spx = img.resize((max(1, int(round(w_world * ppu_screen))), max(1, int(round(h_world * ppu_screen)))), Image.LANCZOS)
            px_, py_ = meta['pivot']
            if s['flipX']:
                spx = spx.transpose(Image.FLIP_LEFT_RIGHT)
                px_ = 1 - px_
            if s['flipY']:
                spx = spx.transpose(Image.FLIP_TOP_BOTTOM)
                py_ = 1 - py_
            c = s['color']
            if c != [1, 1, 1, 1]:
                r_, g_, b_, a_ = spx.split()
                r_ = r_.point(lambda v: v * c[0]); g_ = g_.point(lambda v: v * c[1]); b_ = b_.point(lambda v: v * c[2])
                a_ = a_.point(lambda v: v * c[3])
                spx = Image.merge('RGBA', (r_, g_, b_, a_))
            cx, cy = to_px(x, y)
            canvas.alpha_composite(spx, (int(round(cx - px_ * spx.width)), int(round(cy - (1 - py_) * spx.height))))
        else:
            _, t, x, y, sx, sy, text = item
            style = t.get('styleData') or data['styles'].get(t['style'], data['styles']['Default'])
            w, h = t['size'][0] * abs(sx) * ppu_screen, t['size'][1] * abs(sy) * ppu_screen
            cx, cy = to_px(x + t['anchored'][0] * sx, y + t['anchored'][1] * sy)
            draw_text(canvas, text, style, cx, cy, w, h, font)
    canvas.convert('RGB').save(out_file)
    print('preview', out_file)


def rgba(c):
    return tuple(int(max(0, min(1, v)) * 255) for v in c)


def draw_text(canvas, text, style, cx, cy, w, h, font):
    size = h * 0.95
    f = font(size)
    while size > 6:
        f = font(size)
        bb = f.getbbox(text)
        if bb[2] - bb[0] <= w * 0.98:
            break
        size *= 0.94
    bb = f.getbbox(text)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    ox, oy = cx - tw / 2 - bb[0], cy - th / 2 - bb[1]
    layer = Image.new('RGBA', canvas.size)
    d = ImageDraw.Draw(layer)
    stroke = int(round(size * style.get('outlineWidth', 0) * 0.35)) if style.get('outline') else 0
    if style.get('underlay'):
        uo = style['underlayOffset']
        d.text((ox + uo[0] * size * 0.08, oy - uo[1] * size * 0.08), text, font=f, fill=rgba(style['underlay']),
               stroke_width=stroke, stroke_fill=rgba(style['underlay']))
    d.text((ox, oy), text, font=f, fill=rgba(style['face']), stroke_width=stroke,
           stroke_fill=rgba(style['outline']) if style.get('outline') else None)
    canvas.alpha_composite(layer)


if __name__ == '__main__':
    main()
