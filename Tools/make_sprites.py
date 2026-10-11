"""Generates the 2D sprites of the prototype (shelves, lock door, mystery box, blocked cage, UI).

Shapes and colours follow the Sort Express assets found in the APK export:
  * shelf back wall colour  #263E5E  (ShelfBackWallMaterial)
  * frame palette           BackgroundFront.png (blue #3D63CD / #87AFEE, navy #2C3751)
  * lock                    obstacle_lock_main_door + inside_circle + handle meshes (steel door, dark dial, gold lever)
Everything is drawn 4x larger and downscaled, so edges are anti-aliased.

    python3 tools/make_sprites.py <out_dir>
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

SS = 4  # supersampling
FONT = '/usr/share/fonts/truetype/google-fonts/Poppins-Bold.ttf'


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(4))


def canvas(w, h):
    return Image.new('RGBA', (w * SS, h * SS), (0, 0, 0, 0))


def down(img):
    return img.resize((img.width // SS, img.height // SS), Image.LANCZOS)


def rrect_mask(w, h, r, box=None):
    m = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle(box or (0, 0, w - 1, h - 1), r, fill=255)
    return m


def vgrad(w, h, top, bottom):
    g = Image.new('RGBA', (w, h))
    px = g.load()
    for y in range(h):
        c = mix(top, bottom, y / max(1, h - 1))
        for x in range(w):
            px[x, y] = c
    return g


def paste_masked(dst, src, mask, xy=(0, 0)):
    layer = Image.new('RGBA', dst.size, (0, 0, 0, 0))
    layer.paste(src, xy, mask)
    dst.alpha_composite(layer)


# --------------------------------------------------------------------------- shelf
THEMES = {
    # light rims on a dark slate interior, as in the live game; hard tiers get a tinted rim
    'blue':   dict(base='#A9C8F0', light='#DCEBFC', dark='#5F84BD', lip='#C3DAF7'),
    'purple': dict(base='#C1A6F0', light='#E9DDFC', dark='#7C5DB8', lip='#D3BEF7'),
    'red':    dict(base='#F0AFA6', light='#FCE3DE', dark='#B8665E', lip='#F6C7C0'),
}


def shelf_frame(theme):
    """9-slice rim drawn in front of the items: thin sides and top, a slightly thicker bottom edge."""
    t = THEMES[theme]
    W, H = 160, 160
    S = SS
    img = canvas(W, H)
    side, top, lip, r = 7 * S, 7 * S, 12 * S, 14 * S
    w, h = W * S, H * S
    outer = rrect_mask(w, h, r)
    inner = Image.new('L', (w, h), 0)
    ImageDraw.Draw(inner).rounded_rectangle((side, top, w - 1 - side, h - 1 - lip), 6 * S, fill=255)
    ring = Image.composite(Image.new('L', (w, h), 0), outer, inner)
    body = vgrad(w, h, hexc(t['light']), hexc(t['base']))
    paste_masked(img, body, ring)
    d = ImageDraw.Draw(img)
    lip_top = h - lip
    d.rectangle((side, lip_top, w - 1 - side, lip_top + 3 * S), fill=hexc(t['light']))
    d.rounded_rectangle((0, 0, w - 1, h - 1), r, outline=hexc(t['dark']), width=2 * S)
    d.rounded_rectangle((side, top, w - 1 - side, lip_top), 6 * S, outline=hexc(t['dark'], 220), width=2 * S)
    return down(img)


def shelf_back():
    """Interior back wall (behind the items), navy like ShelfBackWallMaterial."""
    W, H = 128, 128
    w, h = W * SS, H * SS
    img = canvas(W, H)
    body = vgrad(w, h, hexc('#22324F'), hexc('#2E4368'))
    paste_masked(img, body, rrect_mask(w, h, 14 * SS))
    # soft inner shadow at the top (AmbientShadowTop in the original)
    sh = vgrad(w, 34 * SS, (8, 14, 28, 150), (8, 14, 28, 0))
    layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    layer.paste(sh, (0, 0))
    img.alpha_composite(Image.composite(layer, Image.new('RGBA', (w, h), (0, 0, 0, 0)), rrect_mask(w, h, 14 * SS)))
    return down(img)


# --------------------------------------------------------------------------- lock door
def lock_door():
    W, H = 400, 256          # covers a 3-slot shelf interior (1.565 x 1.0 world units at 256 ppu; the game draws x at half the level scale)
    w, h = W * SS, H * SS
    img = canvas(W, H)
    r = 26 * SS
    body = vgrad(w, h, hexc('#D5DDEB'), hexc('#8994AE'))
    paste_masked(img, body, rrect_mask(w, h, r))
    d = ImageDraw.Draw(img)
    # brushed lines
    for i in range(9):
        x = int(w * (i + 0.5) / 9)
        d.line((x, 10 * SS, x, h - 10 * SS), fill=(255, 255, 255, 18), width=2 * SS)
    # inner bevel panel
    d.rounded_rectangle((16 * SS, 16 * SS, w - 16 * SS, h - 16 * SS), 18 * SS, outline=(90, 102, 128, 255), width=3 * SS)
    d.rounded_rectangle((19 * SS, 19 * SS, w - 19 * SS, h - 19 * SS), 16 * SS, outline=(255, 255, 255, 90), width=2 * SS)
    d.rounded_rectangle((0, 0, w - 1, h - 1), r, outline=(60, 70, 96, 255), width=4 * SS)
    # rivets
    for (x, y) in [(36, 36), (W - 36, 36), (36, H - 36), (W - 36, H - 36)]:
        x, y = x * SS, y * SS
        d.ellipse((x - 9 * SS, y - 9 * SS, x + 9 * SS, y + 9 * SS), fill=(110, 120, 146, 255))
        d.ellipse((x - 7 * SS, y - 8 * SS, x + 5 * SS, y + 4 * SS), fill=(230, 236, 245, 255))
    # hinges on the left
    for y in (70, H - 70):
        d.rounded_rectangle((0, (y - 18) * SS, 22 * SS, (y + 18) * SS), 6 * SS, fill=(74, 84, 110, 255))
    # dial: dark circle + ring (inside_circle mesh), counter text is drawn in the game on top of it
    cx, cy, R = w // 2 - 22 * SS, h // 2, 76 * SS
    d.ellipse((cx - R - 10 * SS, cy - R - 10 * SS, cx + R + 10 * SS, cy + R + 10 * SS), fill=(96, 108, 136, 255))
    d.ellipse((cx - R, cy - R, cx + R, cy + R), fill=(38, 48, 74, 255))
    for k in range(24):  # dial ticks
        a = 2 * math.pi * k / 24
        r1, r2 = R - 6 * SS, R - (18 if k % 6 == 0 else 11) * SS
        d.line((cx + r1 * math.cos(a), cy + r1 * math.sin(a), cx + r2 * math.cos(a), cy + r2 * math.sin(a)),
               fill=(150, 162, 190, 255), width=3 * SS)
    # gold lever (handle_01 mesh) on the right side of the dial
    hx = cx + R + 56 * SS
    d.rounded_rectangle((hx - 18 * SS, cy - 56 * SS, hx + 18 * SS, cy + 56 * SS), 16 * SS, fill=(178, 120, 20, 255))
    d.rounded_rectangle((hx - 13 * SS, cy - 51 * SS, hx + 9 * SS, cy + 45 * SS), 13 * SS, fill=(240, 176, 48, 255))
    d.rounded_rectangle((hx - 8 * SS, cy - 44 * SS, hx - 2 * SS, cy + 26 * SS), 3 * SS, fill=(255, 222, 140, 255))
    return down(img)


# --------------------------------------------------------------------------- mystery / blocked
def mystery_box():
    W = H = 128
    w, h = W * SS, H * SS
    img = canvas(W, H)
    r = 22 * SS
    body = vgrad(w, h, hexc('#A879FF'), hexc('#5A2FC0'))
    paste_masked(img, body, rrect_mask(w, h, r, (6 * SS, 6 * SS, w - 6 * SS, h - 6 * SS)))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((6 * SS, 6 * SS, w - 6 * SS, h - 6 * SS), r, outline=(48, 20, 120, 255), width=4 * SS)
    d.rounded_rectangle((14 * SS, 14 * SS, w - 14 * SS, h - 14 * SS), 16 * SS, outline=(255, 255, 255, 70), width=3 * SS)
    font = ImageFont.truetype(FONT, 92 * SS)
    tw = d.textbbox((0, 0), '?', font=font)
    x = (w - (tw[2] - tw[0])) / 2 - tw[0]
    y = (h - (tw[3] - tw[1])) / 2 - tw[1]
    d.text((x + 4 * SS, y + 6 * SS), '?', font=font, fill=(40, 12, 100, 200))
    d.text((x, y), '?', font=font, fill=(255, 255, 255, 255))
    return down(img)


def blocked_cage():
    """Cage drawn over a blocked item: it cannot be moved, only matched where it stands."""
    W = H = 128
    w, h = W * SS, H * SS
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    steel, dark, hi = (150, 162, 186, 255), (58, 66, 90, 255), (230, 236, 246, 255)
    m = 8 * SS
    for x in (m + 20 * SS, w // 2, w - m - 20 * SS):  # vertical bars
        d.rounded_rectangle((x - 6 * SS, m, x + 6 * SS, h - m), 5 * SS, fill=dark)
        d.rounded_rectangle((x - 4 * SS, m + 2 * SS, x + 3 * SS, h - m - 2 * SS), 4 * SS, fill=steel)
        d.line((x - 2 * SS, m + 6 * SS, x - 2 * SS, h - m - 6 * SS), fill=hi, width=2 * SS)
    d.rounded_rectangle((m, m, w - m, h - m), 18 * SS, outline=dark, width=11 * SS)
    d.rounded_rectangle((m + 2 * SS, m + 2 * SS, w - m - 2 * SS, h - m - 2 * SS), 16 * SS, outline=steel, width=6 * SS)
    # small padlock badge at the bottom
    cx, cy = w // 2, h - 22 * SS
    d.arc((cx - 13 * SS, cy - 34 * SS, cx + 13 * SS, cy - 6 * SS), 180, 360, fill=dark, width=6 * SS)
    d.rounded_rectangle((cx - 20 * SS, cy - 18 * SS, cx + 20 * SS, cy + 16 * SS), 7 * SS, fill=(240, 176, 48, 255),
                        outline=(150, 96, 12, 255), width=3 * SS)
    d.ellipse((cx - 4 * SS, cy - 6 * SS, cx + 4 * SS, cy + 2 * SS), fill=(110, 70, 10, 255))
    return down(img)


# --------------------------------------------------------------------------- UI & fx
def ui_round(button=False):
    W = H = 96
    w, h = W * SS, H * SS
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    r = 30 * SS
    if button:
        d.rounded_rectangle((0, 8 * SS, w - 1, h - 1), r, fill=(150, 150, 150, 255))   # bottom edge (darker after tint)
        d.rounded_rectangle((0, 0, w - 1, h - 9 * SS), r, fill=(255, 255, 255, 255))
        d.rounded_rectangle((8 * SS, 6 * SS, w - 8 * SS, 26 * SS), 12 * SS, fill=(255, 255, 255, 255))
    else:
        d.rounded_rectangle((0, 0, w - 1, h - 1), r, fill=(255, 255, 255, 255))
    return down(img)


def circle(size=128, soft=False):
    if soft:
        img = Image.new('RGBA', (size, size), (255, 255, 255, 0))
        px = img.load()
        c = (size - 1) / 2
        for y in range(size):
            for x in range(size):
                dd = math.hypot(x - c, y - c) / c
                a = max(0.0, 1 - dd) ** 1.6
                px[x, y] = (255, 255, 255, int(255 * a))
        return img
    img = canvas(size, size)
    ImageDraw.Draw(img).ellipse((0, 0, size * SS - 1, size * SS - 1), fill=(255, 255, 255, 255))
    return down(img)


def sparkle():
    W = 64
    w = W * SS
    img = canvas(W, W)
    d = ImageDraw.Draw(img)
    c = w / 2
    pts = []
    for k in range(8):
        a = math.pi / 4 * k - math.pi / 2
        rr = c * (0.98 if k % 2 == 0 else 0.26)
        pts.append((c + rr * math.cos(a), c + rr * math.sin(a)))
    d.polygon(pts, fill=(255, 255, 255, 255))
    return down(img).filter(ImageFilter.GaussianBlur(0.4))


def item_shadow():
    W, H = 96, 32
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    px = img.load()
    for y in range(H):
        for x in range(W):
            dx = (x - (W - 1) / 2) / (W / 2)
            dy = (y - (H - 1) / 2) / (H / 2)
            dd = math.sqrt(dx * dx + dy * dy)
            px[x, y] = (5, 10, 25, int(150 * max(0.0, 1 - dd) ** 1.3))
    return img


def slot_glow():
    """Rounded outline used to highlight the drop target."""
    W = H = 96
    w, h = W * SS, H * SS
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((4 * SS, 4 * SS, w - 4 * SS, h - 4 * SS), 22 * SS, outline=(255, 255, 255, 255), width=7 * SS)
    glow = down(img).filter(ImageFilter.GaussianBlur(3))
    out = Image.alpha_composite(glow, down(img))
    return out


# --------------------------------------------------------------------------- frozen / shutter / cargo / bg
def frozen_ice():
    """Ice block drawn over a frozen item (it cannot move; match it where it stands)."""
    W = H = 128
    w, h = W * SS, H * SS
    img = canvas(W, H)
    d = ImageDraw.Draw(img)
    m = 6 * SS
    body = vgrad(w, h, (205, 238, 255, 120), (120, 190, 240, 150))
    paste_masked(img, body, rrect_mask(w, h, 20 * SS, (m, m, w - m, h - m)))
    d.rounded_rectangle((m, m, w - m, h - m), 20 * SS, outline=(235, 250, 255, 235), width=5 * SS)
    # facets and shine
    d.line((m + 10 * SS, h * 0.35, w * 0.35, m + 10 * SS), fill=(255, 255, 255, 200), width=6 * SS)
    d.line((m + 14 * SS, h * 0.55, w * 0.55, m + 14 * SS), fill=(255, 255, 255, 110), width=3 * SS)
    d.line((w * 0.62, h - m - 10 * SS, w - m - 10 * SS, h * 0.62), fill=(255, 255, 255, 120), width=4 * SS)
    for (x, y) in [(0.78, 0.22), (0.2, 0.8)]:
        cx, cy = w * x, h * y
        for a in range(0, 180, 60):
            dx, dy = math.cos(math.radians(a)) * 9 * SS, math.sin(math.radians(a)) * 9 * SS
            d.line((cx - dx, cy - dy, cx + dx, cy + dy), fill=(255, 255, 255, 230), width=2 * SS)
    return down(img)


def shutter():
    """Orange roller shutter that closes a shelf for good (3-slot interior, 1.565 x 1 units at 256 ppu)."""
    W, H = 400, 256
    w, h = W * SS, H * SS
    img = canvas(W, H)
    paste_masked(img, vgrad(w, h, hexc('#FFC15A'), hexc('#E2771C')), rrect_mask(w, h, 18 * SS))
    d = ImageDraw.Draw(img)
    n = 7
    for i in range(1, n):
        y = int(h * i / n)
        d.rectangle((0, y - 4 * SS, w, y + 2 * SS), fill=(170, 80, 15, 255))
        d.rectangle((0, y + 2 * SS, w, y + 5 * SS), fill=(255, 220, 150, 200))
    d.rounded_rectangle((0, h - 30 * SS, w - 1, h - 1), 10 * SS, fill=(120, 130, 155, 255))
    d.rounded_rectangle((w // 2 - 60 * SS, h - 24 * SS, w // 2 + 60 * SS, h - 12 * SS), 6 * SS, fill=(60, 70, 96, 255))
    d.rounded_rectangle((0, 0, w - 1, h - 1), 18 * SS, outline=(150, 70, 10, 255), width=5 * SS)
    return down(img)


def cargo_door():
    """Purple cargo door behind the front row: the rows behind it are hidden."""
    W, H = 400, 256
    w, h = W * SS, H * SS
    img = canvas(W, H)
    paste_masked(img, vgrad(w, h, hexc('#6D4BD6'), hexc('#3E2A92')), rrect_mask(w, h, 18 * SS))
    d = ImageDraw.Draw(img)
    for i in range(1, 4):
        x = int(w * i / 4)
        d.rectangle((x - 2 * SS, 10 * SS, x + 2 * SS, h - 10 * SS), fill=(40, 25, 100, 180))
    for cx in (w * 0.18, w * 0.82):  # chevrons
        sgn = -1 if cx < w / 2 else 1
        for k in range(2):
            ox = cx + sgn * k * 22 * SS
            d.line((ox - sgn * 18 * SS, h * 0.32, ox, h * 0.5, ox - sgn * 18 * SS, h * 0.68), fill=(170, 150, 255, 255), width=8 * SS)
    d.rounded_rectangle((0, 0, w - 1, h - 1), 18 * SS, outline=(30, 18, 80, 255), width=5 * SS)
    return down(img)


def bg_gradient():
    W, H = 64, 512
    return vgrad(W, H, hexc('#3A62C7'), hexc('#1E3A8C'))


if __name__ == '__main__':
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for th in THEMES:
        shelf_frame(th).save(os.path.join(out, 'shelf_frame_%s.png' % th))
    shelf_back().save(os.path.join(out, 'shelf_back.png'))
    lock_door().save(os.path.join(out, 'lock_door.png'))
    ui_round(False).save(os.path.join(out, 'ui_panel.png'))
    ui_round(True).save(os.path.join(out, 'ui_button.png'))
    circle().save(os.path.join(out, 'ui_circle.png'))
    circle(128, soft=True).save(os.path.join(out, 'fx_glow.png'))
    sparkle().save(os.path.join(out, 'fx_sparkle.png'))
    item_shadow().save(os.path.join(out, 'item_shadow.png'))
    slot_glow().save(os.path.join(out, 'slot_glow.png'))
    frozen_ice().save(os.path.join(out, 'frozen_ice.png'))
    shutter().save(os.path.join(out, 'shutter.png'))
    cargo_door().save(os.path.join(out, 'cargo_door.png'))
    bg_gradient().save(os.path.join(out, 'bg_gradient.png'))
    print('sprites written to', out)
