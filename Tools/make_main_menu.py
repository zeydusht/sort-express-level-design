"""Rebuilds Sort Express screens from the game's own prefabs (AssetRipper export): the home screen, the in-level
HUD and the pause dialog.

    python3 tools/make_main_menu.py <export Assets folder> <SortexIndex.tsv> <out folder> [preview.png] [--screen=menu|level|pause]

The index (guid -> file) comes from Unity: Sortex > Tools > Index Asset Export (Editor/SortexExportIndex.cs).

  menu   MainScene.prefab: HomePage (background, LoadLevelButton, TopUI) + NavigationBar  -> Resources/Sortex/Menu/menu.json
  level  LevelSceneUI.prefab: TopUI (level number, timer, pause) + BoosterContainer      -> Resources/Sortex/Level/level.json
  pause  LevelPauseDialog.prefab                                                          -> Resources/Sortex/Level/pause.json

For every GameObject it keeps the local position / scale / rotation, its SpriteRenderer (sprite, sliced size, flip,
colour, sorting order) and its SortingGroup, plus the TextMeshPro text boxes and their materials (face / outline
colours). Writes the node tree per screen part + sprite table + text styles (read by Scripts/Game/PrefabScreen.cs)
and every sprite cut out of its atlas, and optionally renders a 1080 x 1920 preview with Unity's sorting rules.
The boosters are 3D models in the game; their 256 px icons (VacuumIcon256...) stand in for them here.
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
BOOSTERS = [('VacuumButton', 'VacuumIcon256'), ('ShuffleButton', 'MagicHatIcon256'),
            ('PainterButton', 'PainterIcon256'), ('FreezeButton', 'FreezeIcon256')]
SCREENS = {
    'menu': dict(
        prefab='MainScene.prefab', json='menu.json', jpg={'MainSceneBackground'},
        source='Sort Express MainScene.prefab (HomePage + NavigationBar)',
        # name: (path in the prefab, anchor: the screen edge the part keeps its distance to)
        parts={
            'background': ('MainScene/MainSceneUIContainer/MainMenuContentContainer/HomePage/MainSceneBackground', 'center'),
            'levelButton': ('MainScene/MainSceneUIContainer/MainMenuContentContainer/HomePage/LoadLevelButton', 'bottom'),
            'topUI': ('MainScene/MainSceneUIContainer/MainMenuContentContainer/HomePage/TopUI', 'top'),
            'navigation': ('MainScene/MainSceneUIContainer/NavigationBar', 'bottom'),
        },
        container='MainScene/MainSceneUIContainer/MainMenuContentContainer',
        extra=['PlayButtonHardLevel', 'PlayButtonHardLevelBackground', 'PlayButtonSuperHardLevel', 'PlayButtonSuperHardLevelBackground'],
        skip={'LiveOpsContainer', 'LoadLevelButtonUpdateVisualParticle', 'Particles', 'NotificationBadge', 'Foreground'},
        inject={}),
    'level': dict(
        prefab='LevelSceneUI.prefab', json='level.json', jpg=set(),
        source='Sort Express LevelSceneUI.prefab (TopUI + BoosterContainer)',
        parts={
            'topUI': ('LevelSceneUI/TopUI', 'top'),
            'boosters': ('LevelSceneUI/BoosterContainer', 'bottom'),
        },
        container=None,
        extra=['LevelNumberBaseHard', 'LevelNumberBaseSuperHard'] + [icon for _, icon in BOOSTERS],
        skip={'CollectibleCounterController', 'DebugButton', 'BoosterVacuum', 'BoosterShuffle', 'BoosterPainter',
              'BoosterFreezer', 'FreezeTimerBackground', 'FreezeTimerFrame', 'TimerFreezeFill'},
        # the 3D booster models -> their icons, behind the count badge
        inject={'boosters': [('BoosterContainer/' + button, dict(name='Icon', pos=[0.0, 0.0, 0.0], scale=[1.05, 1.05],
                                                                    sprite=dict(key=icon, layer=0, order=-1)))
                             for button, icon in BOOSTERS]}),
    'pause': dict(
        prefab='LevelPauseDialog.prefab', json='pause.json', jpg=set(),
        source='Sort Express LevelPauseDialog.prefab',
        parts={'dialog': ('LevelPauseDialog', 'center')},
        container=None, extra=[], skip={'Particles'}, inject={}),
    # level end: won / out of space or time (the last-chance offer) / failed
    'win': dict(
        prefab='LevelWinDialog.prefab', json='win.json', jpg=set(),
        source='Sort Express LevelWinDialog.prefab',
        parts={'dialog': ('LevelWinDialog', 'center')},
        container=None, extra=[],
        skip={'Particles', 'LogoAnimation', 'BottomContainer', 'DialogMultiplierTag', 'DoubleReward', 'SingleReward'},
        inject={}),
    'nomore': dict(
        prefab='NoMoreMatchesPanel.prefab', json='nomore.json', jpg=set(),
        source='Sort Express NoMoreMatchesPanel.prefab',
        parts={'dialog': ('NoMoreMatchesPanel', 'center')},
        container=None, extra=['HourglassIcon256'],
        skip={'Particles', 'Offer', 'FreeEgoTutorial', 'Peek', 'FirstPage', 'SecondPage', 'LightRays'},
        inject={}),
    'lose': dict(
        prefab='LevelLoseDialog.prefab', json='lose.json', jpg=set(),
        source='Sort Express LevelLoseDialog.prefab',
        parts={'dialog': ('LevelLoseDialog', 'center')},
        container=None, extra=[],
        skip={'Particles', 'BottomContainer', 'LightRays', 'InfiniteHeart', 'LoseText'},
        inject={}),
}
SKIP = set()


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


def crop_sprite(export, index, atlases, info):
    tex_path = os.path.join(export, index[info['texture']])
    if tex_path not in atlases:
        atlases[tex_path] = Image.open(tex_path).convert('RGBA')
    atlas = atlases[tex_path]
    x, y, w, h = info['textureRect']
    top = atlas.height - (y + h)
    if info['settings'] >> 2 & 15:
        raise SystemExit(info['name'] + ': rotated atlas packing is not supported')
    return atlas.crop((int(round(x)), int(round(top)), int(round(x + w)), int(round(top + h))))


def main():
    global SKIP
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    screen = next((a.split('=', 1)[1] for a in sys.argv[1:] if a.startswith('--screen=')), 'menu')
    cfg = SCREENS[screen]
    SKIP = cfg['skip']
    export, index_file, out_dir = args[0:3]
    preview = args[3] if len(args) > 3 else None
    index = {}
    for line in open(index_file, encoding='utf-8'):
        guid, rel, _ = line.rstrip('\n').split('\t')
        index[guid] = rel
    prefab = Prefab(os.path.join(export, 'GameObject', cfg['prefab']))

    parts = {}
    for key, (path, anchor) in cfg['parts'].items():
        parts[key] = dict(anchor=anchor, nodes=prefab.nodes(prefab.find(path)))
        # parts inside a shared sorting group (MainMenuContentContainer) keep it; the navigation bar draws above it
        container = cfg.get('container')
        if container and path.startswith(container + '/'):
            group = prefab.nodes(prefab.find(container))[0].get('group')
            parts[key]['container'] = dict(name=container.split('/')[-1], group=group)
        for parent_path, node in cfg['inject'].get(key, []):
            names = paths(parts[key]['nodes'])
            parent = names.index(parent_path)
            n = dict(name=node['name'], parent=parent, active=True, pos=list(node['pos']), scale=list(node['scale']), rot=0.0)
            n['sprite'] = dict(key=node['sprite']['key'], color=[1, 1, 1, 1], flipX=False, flipY=False, drawMode=0,
                               size=[1.0, 1.0], layer=node['sprite']['layer'], order=node['sprite']['order'], enabled=True)
            parts[key]['nodes'].append(n)

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

    def keep(name, info, img):
        jpg = name in cfg['jpg']
        if jpg:
            img.convert('RGB').save(os.path.join(out_dir, name + '.jpg'), quality=90)
        else:
            img.save(os.path.join(out_dir, name + '.png'))
        sprites[name] = dict(ppu=info['ppu'], pivot=info['pivot'], border=info['border'], width=img.width, height=img.height, jpg=jpg)

    for part in parts.values():
        for n in part['nodes']:
            s = n.get('sprite')
            if s and s.get('guid') in index:
                info = read_sprite(os.path.join(export, index[s['guid']]))
                # two sprite assets can share a name (PlusIcon.asset / PlusIcon_0.asset are different pictures):
                # the asset's file name keeps them apart in a folder that several screens share
                stem = os.path.basename(index[s['guid']])[:-len('.asset')]
                if stem != info['name']:
                    info['name'] = stem
                s['key'] = info['name']
                if info['name'] not in sprites:
                    keep(info['name'], info, crop_sprite(export, index, atlases, info))
            t = n.get('text')
            if t:
                mat = t.pop('material')
                if mat in index and index[mat].endswith('.mat'):
                    m = read_material(os.path.join(export, index[mat]))
                    t['style'] = m['name'].replace('Bango Pro SDF - ', '')
                    styles[t['style']] = m
                else:
                    t['style'] = 'Default'
    # sprites the game swaps in at runtime (Hard / Super Hard variants) and the booster icons
    by_name = {rel[len('Sprite/'):-len('.asset')]: rel for rel in index.values() if rel.startswith('Sprite/') and rel.endswith('.asset')}
    for name in cfg['extra']:
        if name in sprites or name not in by_name:
            if name not in by_name:
                print('missing sprite', name)
            continue
        info = read_sprite(os.path.join(export, by_name[name]))
        keep(name, info, crop_sprite(export, index, atlases, info))
    styles.setdefault('Default', dict(name='Default', face=[1, 1, 1, 1], outline=[0.0627, 0.2350, 0.6706, 1], outlineWidth=0.2,
                                      underlay=None, underlayOffset=[0, 0], dilate=0))
    for part in parts.values():
        for n in part['nodes']:
            if 'sprite' in n:
                n['sprite'].pop('guid', None)
    if screen == 'level':
        level_extras(out_dir, sprites, parts)
    data = dict(source=cfg['source'], refWidth=REF_WIDTH, refHeight=REF_HEIGHT,
                font='Bango Pro', parts=parts, sprites=sprites, styles=styles)
    json.dump(data, open(os.path.join(out_dir, cfg['json']), 'w'), indent=1)
    print(len(sprites), 'sprites,', len(styles), 'text styles,', sum(len(p['nodes']) for p in parts.values()), 'nodes')
    if preview:
        render(data, out_dir, os.path.join(export, 'Font', 'Bango Pro.otf'), preview, screen)


# ====================================================================== preview (same rules as MainMenuView.cs)
TEXTS = {
    'menu': {
        'LoadLevelButton/Root/Button/PressDownTransform/DefaultText/ShadowText': 'Level 1',
        'LoadLevelButton/Root/Button/PressDownTransform/DefaultText/BaseText': 'Level 1',
        'TopUI/LivesButton/Button/PressDownTransform/LifeTimerContainer/Icon/IconText': '5',
        'TopUI/LivesButton/Button/PressDownTransform/LifeTimerContainer/FullText': 'Full',
        'TopUI/CoinsButton/Button/PressDownTransform/Text': '1000',
        'NavigationBar/Root/HomeButton/Text': 'Home',
    },
    'level': dict([
        ('TopUI/LevelNumber/Root/Text', 'Level 263'),
        ('TopUI/Timer/TextParent/:', '2:30')]
        + [('BoosterContainer/%s/Badge/Counter/Text' % b, n) for (b, _), n in zip(BOOSTERS, ('14', '13', '8', '12'))]),
    'pause': {
        'LevelPauseDialog/Root/Ribbon/Text': 'Paused',
        'LevelPauseDialog/Root/ContinueButton/Button/PressDownTransform/BaseText': 'Continue',
        'LevelPauseDialog/Root/QuitButton/Button/PressDownTransform/BaseText': 'Quit',
    },
    'win': dict([('LevelWinDialog/Root/Base/BigTitleName/' + k, 'Level Completed!') for k in ('Bottom2', 'Bottom1', 'Center', 'Main')] + [
        ('LevelWinDialog/Root/Base/Reward/Coin/Root/Text', '20'),
        ('LevelWinDialog/Root/Base/Time/Text', '1:24'),
        ('LevelWinDialog/Root/Base/ContinueButton/Button/PressDownTransform/BaseText', 'Continue')]),
    'nomore': dict([('NoMoreMatchesPanel/Root/Top/TitleRoot/InfoText/' + k, 'Out of Space!') for k in ('Bottom2', 'Bottom1', 'Center', 'Main')] + [
        ('NoMoreMatchesPanel/Root/Top/CoinsButton/Button/PressDownTransform/Text', '1000'),
        ('NoMoreMatchesPanel/Root/Center/LastChance/Root/EGO/EGOText', 'Shuffle the items with a Magic Hat!'),
        ('NoMoreMatchesPanel/Root/PlayOn/PlayOnButton/Button/PressDownTransform/BaseText', 'Play On'),
        ('NoMoreMatchesPanel/Root/PlayOn/PlayOnButton/Button/PressDownTransform/CostText', '900')]),
    'lose': dict([('LevelLoseDialog/Root/Panel/InfoText/' + k, 'Level Failed') for k in ('Bottom2', 'Bottom1', 'Center', 'Main')] + [
        ('LevelLoseDialog/Root/PreLevelBoosterButtons/SelectText', 'Select boosters:'),
        ('LevelLoseDialog/Root/PreLevelBoosterButtons/PreLevelBoosterHourglassButton/Badge/Counter/Text', '1'),
        ('LevelLoseDialog/Root/PreLevelBoosterButtons/PreLevelBoosterMagnetButton/Badge/Counter/Text', '1'),
        ('LevelLoseDialog/Root/TryAgainButton/Button/PressDownTransform/BaseText', 'Try Again')]),
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


def render(data, out_dir, font_path, out_file, screen='menu', width_px=1080, height_px=1920):
    texts = TEXTS[screen]
    ppu_screen = width_px / data['refWidth']
    half_h = height_px / ppu_screen / 2
    canvas = Image.new('RGBA', (width_px, height_px), (40, 60, 120, 255) if screen == 'menu' else (42, 75, 168, 255))
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
            # one sequence number in hierarchy order for groups, sprites and texts (like PrefabScreen.cs)
            if 'group' in n:
                gpath = gpath + ((n['group']['layer'], n['group']['order'], seq[0]),)
                seq[0] += 1
            groups[i] = gpath
            world[i] = pm
            if not visible[i] or names[i] in HIDDEN:
                continue
            x, y, sx, sy, r = world[i]
            if 'sprite' in n and n['sprite'].get('enabled', True) and n['sprite'].get('key') in data['sprites']:
                s = n['sprite']
                k = groups[i] + ((s['layer'], s['order'], seq[0]),)
                seq[0] += 1
                items.append((k, ('sprite', s, x, y, sx, sy)))
            if 'text' in n and names[i] in texts:
                t = dict(n['text'])
                if n['name'] == 'ShadowText':
                    # like MainMenuView.cs: the shadow takes the darkened outline colour of its BaseText
                    base = next((m['text'] for j, m in enumerate(nodes) if names[j] == names[i].rsplit('/', 1)[0] + '/BaseText'), None)
                    bs = data['styles'].get(base['style'] if base else '', {})
                    dark = [v * 0.7 for v in (bs.get('outline') or [0.08, 0.55, 0.06])[:3]] + [1]
                    t['styleData'] = dict(face=dark, outline=dark, outlineWidth=bs.get('outlineWidth', 0.2), underlay=None)
                k = groups[i] + ((t['layer'], t['order'], seq[0]),)
                seq[0] += 1
                items.append((k, ('text', t, x, y, sx, sy, texts[names[i]])))

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


# ====================================================================== level HUD pieces drawn here
def level_extras(out_dir, sprites, parts):
    """The timer's green ring and the booster plank are drawn by custom shaders / 3D models in the game:
    TimerFill is a WhiteBox with a progress shader, the boosters stand on a wooden 3D shelf. Rebuilt as sprites:
      TimerRing    the ring (white, tinted green by its node), cut exactly along TimerEmpty (the dark track it covers)
      TimerInside  the see-through navy inside of the timer
      BoosterPlank the wooden shelf under the boosters (colours sampled from a screenshot of the game)
    """
    import numpy as np
    from PIL import ImageFilter
    half = Image.open(os.path.join(out_dir, 'TimerEmpty.png')).convert('RGBA')
    full = Image.new('RGBA', (half.width * 2, half.height))
    full.paste(half, (0, 0))
    full.paste(half.transpose(Image.FLIP_LEFT_RIGHT), (half.width, 0))
    a = np.array(full.split()[3], dtype=float) / 255
    h, w = a.shape
    # outside = transparent pixels connected to the border; inside = the remaining transparent pixels
    clear = a < 0.5
    outside = np.zeros_like(clear)
    stack = [(y, x) for y in range(h) for x in (0, w - 1)] + [(y, x) for x in range(w) for y in (0, h - 1)]
    while stack:
        y, x = stack.pop()
        if y < 0 or x < 0 or y >= h or x >= w or outside[y, x] or not clear[y, x]:
            continue
        outside[y, x] = True
        stack += [(y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)]
    inside = clear & ~outside
    yy = np.linspace(0, 1, h)[:, None] * np.ones((1, w))
    top, bottom = np.array([255, 255, 255]), np.array([160, 160, 160])   # tinted green (or red) by the node colour
    rgb = top[None, None, :] * (1 - yy[:, :, None]) + bottom[None, None, :] * yy[:, :, None]
    ring = np.dstack([rgb, a * 255]).astype(np.uint8)
    Image.fromarray(ring, 'RGBA').save(os.path.join(out_dir, 'TimerRing.png'))
    m = Image.fromarray((inside * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(1))
    ins = Image.new('RGBA', (w, h), (10, 10, 60, 0))
    ins.putalpha(m.point(lambda v: int(v * 0.45)))
    ins.save(os.path.join(out_dir, 'TimerInside.png'))
    base = sprites['TimerEmpty']
    for name in ('TimerRing', 'TimerInside'):
        sprites[name] = dict(ppu=base['ppu'], pivot=[0.5, base['pivot'][1]], border=[0, 0, 0, 0], width=w, height=h, jpg=False)

    # wooden plank: top face (seen from above) + front face, rounded ends, soft shadow
    ppu, ss = 278.0, 4
    W, H = int(3.85 * ppu), int(0.46 * ppu)
    top_h, front_h, r = 0.15 * ppu, 0.20 * ppu, 0.05 * ppu
    img = Image.new('RGBA', (W * ss, H * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    y0 = 0.02 * ppu
    shadow = Image.new('L', (W * ss, H * ss), 0)
    ImageDraw.Draw(shadow).rounded_rectangle((10 * ss, (y0 + top_h + 0.06 * ppu) * ss, (W - 10) * ss, (H - 2) * ss), r * ss * 2, fill=120)
    shadow = shadow.filter(ImageFilter.GaussianBlur(6 * ss))
    img.paste(Image.new('RGBA', img.size, (8, 12, 40, 255)), (0, 0), shadow)
    d.rounded_rectangle((4 * ss, (y0 + top_h - r) * ss, (W - 4) * ss, (y0 + top_h + front_h) * ss), r * ss, fill=(196, 124, 62, 255))
    for i in range(int(front_h * ss)):
        t = i / (front_h * ss)
        c = tuple(int(v) for v in np.array([207, 129, 59]) * (1 - t) + np.array([158, 97, 71]) * t)
        d.line((6 * ss, (y0 + top_h) * ss + i, (W - 6) * ss, (y0 + top_h) * ss + i), fill=c + (255,))
    d.rounded_rectangle((2 * ss, y0 * ss, (W - 2) * ss, (y0 + top_h + 2) * ss), r * ss, fill=(156, 80, 48, 255))
    rnd = np.random.RandomState(7)
    for k in range(26):
        yk = (y0 + 3 + rnd.rand() * (top_h - 6)) * ss
        x0 = rnd.rand() * W * 0.8 * ss
        x1 = x0 + (0.15 + rnd.rand() * 0.5) * W * ss
        d.line((x0, yk, min(x1, (W - 8) * ss), yk + rnd.randn() * 2 * ss), fill=(128, 62, 36, 160), width=ss * 2)
    for k in range(14):
        yk = (y0 + top_h + 4 + rnd.rand() * (front_h - 8)) * ss
        x0 = rnd.rand() * W * 0.8 * ss
        x1 = x0 + (0.1 + rnd.rand() * 0.4) * W * ss
        d.line((x0, yk, min(x1, (W - 8) * ss), yk + rnd.randn() * 1.5 * ss), fill=(170, 100, 48, 140), width=ss * 2)
    d.line((6 * ss, (y0 + top_h) * ss, (W - 6) * ss, (y0 + top_h) * ss), fill=(222, 150, 80, 255), width=ss * 3)
    img = img.resize((W, H), Image.LANCZOS)
    img.save(os.path.join(out_dir, 'BoosterPlank.png'))
    sprites['BoosterPlank'] = dict(ppu=ppu, pivot=[0.5, 0.5], border=[0, 0, 0, 0], width=W, height=H, jpg=False)

    # wire them into the node tree
    nodes = parts['topUI']['nodes']
    names = paths(nodes)
    fill = nodes[names.index('TopUI/Timer/NewTimerSetup/TimerFill')]
    fill['scale'] = [1.0, 1.0]
    fill['sprite'].update(key='TimerRing', drawMode=0, size=[w / base['ppu'], h / base['ppu']], color=[0.157, 1.0, 0.157, 1.0])
    nodes.append(dict(name='TimerInside', parent=names.index('TopUI/Timer/NewTimerSetup'), active=True, pos=[0.0, 0.0, 0.0],
                      scale=[1.0, 1.0], rot=0.0, sprite=dict(key='TimerInside', color=[1, 1, 1, 1], flipX=False, flipY=False,
                                                              drawMode=0, size=[1.0, 1.0], layer=1, order=-6, enabled=True)))
    nodes = parts['boosters']['nodes']
    nodes.append(dict(name='Plank', parent=0, active=True, pos=[0.012, -0.115, 0.0], scale=[1.0, 1.0], rot=0.0,
                      sprite=dict(key='BoosterPlank', color=[1, 1, 1, 1], flipX=False, flipY=False, drawMode=0,
                                  size=[1.0, 1.0], layer=0, order=-2, enabled=True)))


if __name__ == '__main__':
    main()
