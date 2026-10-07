"""Makes store/feature-graphic-1024x500.png: a game screenshot as the backdrop, the icon, the name and a line. Run from the repo root."""
from PIL import Image, ImageDraw, ImageFont, ImageFilter

W, H = 1024, 500
bg = Image.open('store/screenshot-oak-1920x1080.png').convert('RGB')
# The lower right of the picture, clear of the game's buttons along the top and left.
bg = bg.crop((700, 1080 - 596, 1920, 1080)).resize((W, H), Image.LANCZOS)
img = bg

# A dark fade from the left so the text reads on any backdrop.
fade = Image.new('L', (W, H))
fd = ImageDraw.Draw(fade)
for x in range(W):
    fd.line([(x, 0), (x, H)], fill=max(0, int(190 * (1 - x / (W * 0.8)))))
img = Image.composite(Image.new('RGB', (W, H), (18, 30, 20)), img, fade)

icon = Image.open('store/icon-512.png').convert('RGBA').resize((250, 250), Image.LANCZOS)
mask = Image.new('L', (250, 250), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, 249, 249), 44, fill=255)
shadow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
shadow.paste((0, 0, 0, 150), (46, 124), mask)
img = Image.alpha_composite(img.convert('RGBA'), shadow.filter(ImageFilter.GaussianBlur(10)))
img.paste(icon, (40, 110), mask)

d = ImageDraw.Draw(img)
bold = '/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf'
title, sub = ImageFont.truetype(bold, 92), ImageFont.truetype(bold, 31)
def text(xy, s, font, fill):
    x, y = xy
    d.text((x + 3, y + 3), s, font=font, fill=(0, 0, 0, 255))
    d.text(xy, s, font=font, fill=fill)
text((322, 150), 'Bramblekin', title, (255, 232, 160, 255))
text((326, 270), 'Tiny Garden Kingdoms', ImageFont.truetype(bold, 40), (255, 255, 255, 255))
text((326, 330), 'A tiny world that lives on its own.', sub, (230, 240, 220, 255))
img.convert('RGB').save('store/feature-graphic-1024x500.png')
