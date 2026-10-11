"""Sprites for the newer Sort Express obstacles, cut from the game's own tutorial pictures
(TutorialPinata / TutorialDragonEgg / TutorialFerris / TutorialIris / TutorialFuseBox)."""
import colorsys, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
src = sys.argv[1]; out = sys.argv[2]
os.makedirs(out, exist_ok=True)
def load(n): return Image.open(os.path.join(src, n + '.png')).convert('RGBA')

def cut_item(img, box, save, size=128):
    """Separate a colourful item from the dark blue shelf interior: flood fill from the crop border."""
    c = np.array(img.crop(box)).astype(float) / 255
    H, W = c.shape[:2]
    rgb = c[:, :, :3]
    mx = rgb.max(2); mn = rgb.min(2)
    sat = (mx - mn) / np.maximum(mx, 1e-6)
    hue = np.zeros((H, W))
    for y in range(H):
        for x in range(W):
            hue[y, x] = colorsys.rgb_to_hsv(*rgb[y, x])[0]
    bg_like = (hue > 0.55) & (hue < 0.72) & (mx < 0.62) | (mx < 0.18)
    mask = np.zeros((H, W), bool)
    stack = [(y, x) for y in range(H) for x in (0, W - 1)] + [(y, x) for x in range(W) for y in (0, H - 1)]
    while stack:
        y, x = stack.pop()
        if y < 0 or x < 0 or y >= H or x >= W or mask[y, x] or not bg_like[y, x]: continue
        mask[y, x] = True
        stack += [(y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)]
    alpha = Image.fromarray(((~mask) * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
    im = Image.fromarray((c * 255).astype(np.uint8)); im.putalpha(alpha)
    bb = im.getbbox(); im = im.crop(bb)
    s = max(im.size) + 8
    sq = Image.new('RGBA', (s, s), (0, 0, 0, 0)); sq.alpha_composite(im, ((s - im.width) // 2, s - im.height - 4))
    sq = sq.resize((size, size), Image.LANCZOS)
    sq.save(os.path.join(out, save))
    return sq

def hue_shift(im, deg):
    a = np.array(im).astype(float) / 255
    out_ = a.copy()
    H, W = a.shape[:2]
    for y in range(H):
        for x in range(W):
            if a[y, x, 3] == 0: continue
            h, s, v = colorsys.rgb_to_hsv(*a[y, x, :3])
            out_[y, x, :3] = colorsys.hsv_to_rgb((h + deg / 360) % 1, s, v)
    return Image.fromarray((out_ * 255).astype(np.uint8))

# piñata: six colours (pci 0-5)
pin = cut_item(load('TutorialPinata'), (160, 100, 360, 356), 'pinata_0.png')
for i, deg in enumerate([0, 55, 115, 180, 235, 295]):
    (pin if deg == 0 else hue_shift(pin, deg)).save(os.path.join(out, 'pinata_%d.png' % i))
# dragon egg
cut_item(load('TutorialDragonEgg'), (160, 100, 360, 356), 'dragon_egg_0.png')

# rotating door (ferris): the closed door panel and its lamps
fer = load('TutorialFerris')
door = fer.crop((20, 84, 494, 332))
door.save(os.path.join(out, 'ferris_door.png'))
lamp = fer.crop((168, 60, 222, 96))
lamp.save(os.path.join(out, 'ferris_lamp_on.png'))
la = np.array(lamp).astype(float)
off = la.copy(); off[:, :, :3] = off[:, :, :3] * np.array([0.55, 0.52, 0.5])
Image.fromarray(off.astype(np.uint8)).save(os.path.join(out, 'ferris_lamp_off.png'))
br = la.copy(); br[:, :, :3] = br[:, :, :3] * np.array([0.35, 0.3, 0.3])
bimg = Image.fromarray(br.astype(np.uint8)); d = ImageDraw.Draw(bimg)
d.line((18, 10, 26, 18, 22, 22, 32, 30), fill=(20, 15, 15, 255), width=2)
bimg.save(os.path.join(out, 'ferris_lamp_broken.png'))

# iris: closed (hole filled with blade colour) and the frame alone (open)
iris = load('TutorialIris').crop((14, 62, 500, 336))
a = np.array(iris).astype(float) / 255
H, W = a.shape[:2]
blade = np.array([0.24, 0.55, 0.52])
closed = a.copy()
cx, cy = W * 0.5, H * 0.47
for y in range(H):
    for x in range(W):
        r, g, b = a[y, x, :3]
        h, s, v = colorsys.rgb_to_hsv(r, g, b)
        teal = 0.42 < h < 0.55 and s > 0.25
        if abs(x - cx) < W * 0.2 and abs(y - cy) < H * 0.42 and not teal:
            closed[y, x, :3] = blade * (0.9 + 0.1 * (1 - abs(y - cy) / H))
            closed[y, x, 3] = 1
ci = Image.fromarray((closed * 255).astype(np.uint8))
d = ImageDraw.Draw(ci)
for ang in range(6):
    import math
    t = math.radians(ang * 60 + 15)
    d.line((cx, cy, cx + math.cos(t) * W * 0.17, cy + math.sin(t) * H * 0.3), fill=(40, 105, 100, 255), width=3)
ci.save(os.path.join(out, 'iris_closed.png'))
frame = iris.copy(); fa = np.array(frame)
inset = 34
fa[inset:H - inset, inset:W - inset, 3] = 0
Image.fromarray(fa).save(os.path.join(out, 'iris_frame.png'))

# fuse box: lightning tile, an empty window (item painted out), a lit window
fb = load('TutorialFuseBox')
fb.crop((20, 72, 176, 372)).save(os.path.join(out, 'fuse_bolt.png'))
win = np.array(fb.crop((184, 60, 334, 356))).astype(float)
h, w = win.shape[:2]
x0, x1 = 16, w - 18      # inside the window frame
for y in range(52, h - 12):
    left = win[y, x0].copy(); right = win[y, x1].copy()
    for x in range(x0, x1):
        t = (x - x0) / (x1 - x0)
        win[y, x] = left * (1 - t) + right * t
Image.fromarray(win.astype(np.uint8)).save(os.path.join(out, 'fuse_window.png'))
lit = win.copy(); lit[52:h - 12, x0:x1, :3] = lit[52:h - 12, x0:x1, :3] * np.array([1.0, 0.93, 0.55])
Image.fromarray(np.clip(lit, 0, 255).astype(np.uint8)).save(os.path.join(out, 'fuse_window_done.png'))
bg = np.array(fb.crop((8, 54, 506, 380)))
Image.fromarray(bg).save(os.path.join(out, 'fuse_frame.png'))
print('ok')
