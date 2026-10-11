"""Builds the level select dialog (Resources/Sortex/Menu/levels.json) from pieces of the game's own screens.

    python3 tools/make_level_select.py <Resources/Sortex folder>

Sort Express has no level list (its Packs page fills its cards from a prefab the APK export does not contain), so the
dialog is put together from parts the game already uses, taken from the screens make_main_menu.py rebuilt from its
prefabs:

  LevelPauseDialog.prefab  (Level/pause.json)  the veil, the blue pop-up panel, the ribbon title, the close button
                                               and the light blue inner panel (its ButtonsFrame) for every row
  MainScene.prefab         (Menu/menu.json)    the green / red / purple Level button of the home screen (Play)
  LevelLoseDialog.prefab   (Level/lose.json)   the check mark and the cream-on-navy text style
  LevelSceneUI.prefab      (Level/level.json)  the level number pill of the level HUD

Every row: inner panel, level number pill, name, a line under it (tier and time) and the Play button; the check mark
shows on levels already won. SortexGame fills the rows (LevelSelectDialog in LevelEndDialogs.cs) and lays the panel
out for the number of rows it uses. The sprites the dialog needs are copied next to the json.
"""
import copy
import json
import os
import shutil
import sys

ROWS = 5                 # row slots in the json; the game shows as many as it has levels (up to this)
ROW_STEP = 1.32          # distance between two rows (world units of the 4.6166 x 10 reference screen)


def load(root, name):
    return json.load(open(os.path.join(root, name + '.json')))


def subtree(nodes, index):
    """The node at index and everything under it, re-indexed (parent of the first one = -1)."""
    keep = [index]
    for i in range(index + 1, len(nodes)):
        p = nodes[i]['parent']
        if p in keep:
            keep.append(i)
    remap = {old: new for new, old in enumerate(keep)}
    out = []
    for old in keep:
        n = copy.deepcopy(nodes[old])
        n['parent'] = remap.get(n['parent'], -1) if old != index else -1
        out.append(n)
    return out


def find(nodes, path):
    names = []
    for i, n in enumerate(nodes):
        p, j = [], i
        while j >= 0:
            p.append(nodes[j]['name'])
            j = nodes[j]['parent']
        names.append('/'.join(reversed(p)))
    return names.index(path)


def graft(dst, parent, nodes, name=None, pos=None, scale=None, group=None):
    """Appends a subtree under dst[parent]; returns the index of its root."""
    base = len(dst)
    for i, n in enumerate(nodes):
        n = copy.deepcopy(n)
        n['parent'] = parent if i == 0 else n['parent'] + base
        dst.append(n)
    root = dst[base]
    if name:
        root['name'] = name
    if pos is not None:
        root['pos'] = [pos[0], pos[1], 0.0]
    if scale is not None:
        root['scale'] = [scale, scale]
    if group is not None:
        root['group'] = group
    return base


def node(dst, parent, name, pos=(0.0, 0.0), scale=1.0, **extra):
    n = dict(name=name, parent=parent, active=True, pos=[pos[0], pos[1], 0.0], scale=[scale, scale], rot=0.0)
    n.update(extra)
    dst.append(n)
    return len(dst) - 1


def sprite(key, size, flip=False, order=0, draw=1, color=(1, 1, 1, 1)):
    return dict(key=key, color=list(color), flipX=flip, flipY=False, drawMode=draw, size=list(size), layer=1, order=order,
                enabled=True)


def text(style, size, anchored, order=1, align='center'):
    return dict(size=list(size), anchored=list(anchored), layer=1, order=order, style=style, align=align)


def main():
    root = sys.argv[1]
    pause, menu, lose, level = (load(root, n) for n in ('Level/pause', 'Menu/menu', 'Level/lose', 'Level/level'))
    pn = pause['parts']['dialog']['nodes']

    nodes = []
    top = node(nodes, -1, 'LevelSelect', group=dict(layer=3, order=100))
    graft(nodes, top, subtree(pn, find(pn, 'LevelPauseDialog/Foreground')))
    r = node(nodes, top, 'Root')
    # the pop-up panel (resized in code for the number of rows), its ribbon and close button
    for i in (find(pn, 'LevelPauseDialog/Root/BaseLeft'), find(pn, 'LevelPauseDialog/Root/BaseRight')):
        graft(nodes, r, subtree(pn, i))
    graft(nodes, r, subtree(pn, find(pn, 'LevelPauseDialog/Root/Ribbon')))
    graft(nodes, r, subtree(pn, find(pn, 'LevelPauseDialog/Root/CloseButton')))

    button = subtree(menu['parts']['levelButton']['nodes'], 0)
    for b in button:
        if b['name'] == 'LoadLevelButtonUpdateVisualParticle':
            raise SystemExit('unexpected particle node')
    pill = subtree(level['parts']['topUI']['nodes'], find(level['parts']['topUI']['nodes'], 'TopUI/LevelNumber/Root'))
    for i in range(ROWS):
        row = node(nodes, r, 'Level%d' % i, pos=(0.0, -i * ROW_STEP), group=dict(layer=1, order=1))
        # the inner panel of the pause dialog (ButtonsFrame): light blue, 9-sliced; scaled so its corners stay small
        k = 0.8
        node(nodes, row, 'PanelLeft', scale=k, sprite=sprite('PopUpBackgroundLt', (2.06 / k, 1.16 / k)))
        node(nodes, row, 'PanelRight', scale=k, sprite=sprite('PopUpBackgroundLt', (2.06 / k, 1.16 / k), flip=True))
        # level number: the level HUD's pill (navy / red / purple)
        graft(nodes, row, pill, name='Number', pos=(-1.62, 0.0), scale=0.9)
        nodes[-1]['text']['size'] = [0.62, 0.30]
        for n in nodes[len(nodes) - len(pill):]:
            if n.get('sprite'):
                n['sprite']['size'] = [0.44, 0.4172662]
        # name and the line under it, left-aligned between the pill and the Play button
        node(nodes, row, 'Name', text=text('Dark Blue Outline 3', (1.86, 0.40), (-0.19, 0.15), align='left'))
        node(nodes, row, 'Info', text=text('Dark Blue Default', (1.86, 0.24), (-0.19, -0.23), align='left'))
        node(nodes, row, 'Check', pos=(-1.33, -0.27), scale=0.85, sprite=sprite('CheckIcon', (1, 1), draw=0, order=5))
        graft(nodes, row, button, name='Play', pos=(1.42, 0.0), scale=0.5, group=dict(layer=1, order=2))

    styles, sprites = {}, {}
    sources = [('Level', pause), ('Menu', menu), ('Level', lose), ('Level', level)]
    for n in nodes:
        t = n.get('text')
        if t and t['style'] not in styles:
            for _, src in sources:
                if t['style'] in src['styles']:
                    styles[t['style']] = src['styles'][t['style']]
                    break
            else:
                raise SystemExit('missing style ' + t['style'])
    keys = {n['sprite']['key'] for n in nodes if n.get('sprite')}
    keys |= {'PlayButtonHardLevel', 'PlayButtonHardLevelBackground', 'PlayButtonSuperHardLevel',
             'PlayButtonSuperHardLevelBackground', 'LevelNumberBaseHard', 'LevelNumberBaseSuperHard'}
    out_dir = os.path.join(root, 'Menu')
    for key in sorted(keys):
        for folder, src in sources:
            if key in src['sprites']:
                sprites[key] = src['sprites'][key]
                ext = '.jpg' if src['sprites'][key].get('jpg') else '.png'
                target = os.path.join(out_dir, key + ext)
                if not os.path.exists(target):
                    shutil.copyfile(os.path.join(root, folder, key + ext), target)
                break
        else:
            raise SystemExit('missing sprite ' + key)
    styles.setdefault('Default', menu['styles']['Default'])
    data = dict(source='Level select: Sort Express LevelPauseDialog + MainScene LoadLevelButton + LevelSceneUI LevelNumber '
                       '(tools/make_level_select.py)',
                refWidth=pause['refWidth'], refHeight=pause['refHeight'], font='Bango Pro',
                parts=dict(dialog=dict(anchor='center', offset=0.0, nodes=nodes)), sprites=sprites, styles=styles,
                rows=ROWS, rowStep=ROW_STEP)
    json.dump(data, open(os.path.join(out_dir, 'levels.json'), 'w'), indent=1)
    print(len(nodes), 'nodes,', len(sprites), 'sprites,', len(styles), 'styles')


if __name__ == '__main__':
    main()
