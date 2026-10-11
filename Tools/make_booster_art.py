"""Art for the boosters that the prototype draws itself (the game does these with 3D models and shaders).

    python3 tools/make_booster_art.py <export Assets folder> <SortexIndex.tsv> <Resources/Sortex folder>

  Art/painter_alien.png             the painter's green alien: the game's booster_painter_alien mesh with its texture,
                                    rendered front-on like the item icons (128 px, Tools/meshrender_textured.py)
  Level/FreezeTimerBackground.png   the frozen timer (behind and over the timer) and the frost on the screen edges,
  Level/FreezeTimerFrame.png        cut from the game's sprite atlas (Scripts/Game/LevelHud.cs: SetFreeze)
  Level/FreezeEffectOverlay.png
"""
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_main_menu as M  # noqa: E402
from meshrender_textured import parse, render  # noqa: E402


def main():
    export, index_file, res = sys.argv[1:4]
    index = {}
    for line in open(index_file, encoding='utf-8'):
        guid, rel, _ = line.rstrip('\n').split('\t')
        index[guid] = rel
    pos, nrm, uv, tris = parse(os.path.join(export, 'Mesh', 'booster_painter_alien.asset'))
    tex = Image.open(os.path.join(export, 'Texture2D', 'booster_painter_alien_texture.png'))
    alien = render(pos, nrm, uv, tris, tex, 512, 180, 8, pad=0.115).resize((128, 128), Image.LANCZOS)
    alien.save(os.path.join(res, 'Art', 'painter_alien.png'))
    atlases = {}
    for name in ('FreezeTimerBackground', 'FreezeTimerFrame', 'FreezeEffectOverlay'):
        info = M.read_sprite(os.path.join(export, 'Sprite', name + '.asset'))
        M.crop_sprite(export, index, atlases, info).save(os.path.join(res, 'Level', name + '.png'))
        print(name, 'ppu', info['ppu'], 'pivot', info['pivot'], 'border', info['border'])
    print('booster art written')


if __name__ == '__main__':
    main()
