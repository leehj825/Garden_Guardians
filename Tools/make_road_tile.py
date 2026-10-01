"""Builds the road's cobblestone tile from the game's own rock texture (Tools/procedural/stone.png, the plant-free
stone the boulders use): irregular stones with dark gaps between them, each stone showing a different patch of the rock
surface, slightly different in shade. Seamless. Writes Assets/Textures/Ground/road_stone.png.
    python3 Tools/make_road_tile.py     (needs Pillow and numpy)"""
import numpy as np
from PIL import Image

N, K = 512, 9  # tile pixels; stones across it
rng = np.random.default_rng(7)

# The rock texture, made seamless, as detail around 1.0.
tex = Image.open('Tools/procedural/stone.png').convert('L')
tex = tex.crop((14, 14, tex.width - 14, tex.height - 14)).resize((256, 256), Image.LANCZOS)  # the picture's rim is left out
t = np.asarray(tex).astype(float)
t = np.block([[t, t[:, ::-1]], [t[::-1], t[::-1, ::-1]]])  # mirrored 2x2: seamless by construction

# Keep the grain, drop the blotches: subtract a wrapped blur.
from PIL import ImageFilter
big = Image.fromarray(np.clip(np.tile(t, (3, 3)), 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6))
t = t - np.asarray(big).astype(float)[512:1024, 512:1024]
detail = np.clip(1 + t / 45.0, 0.8, 1.2)

# Jittered stone centres on a wrapping grid.
cell = N / K
pts = np.array([((i + 0.5 + rng.uniform(-0.32, 0.32)) * cell, (j + 0.5 + rng.uniform(-0.32, 0.32)) * cell) for j in range(K) for i in range(K)])
yy, xx = np.mgrid[0:N, 0:N].astype(float)
d = np.empty((len(pts), N, N))
for k, (px, py) in enumerate(pts):
    dx = np.abs(xx - px); dx = np.minimum(dx, N - dx)
    dy = np.abs(yy - py); dy = np.minimum(dy, N - dy)
    d[k] = np.sqrt(dx * dx + dy * dy)
order = np.argsort(d, axis=0)
d1 = np.take_along_axis(d, order[:1], 0)[0]
d2 = np.take_along_axis(d, order[1:2], 0)[0]
stone = order[0]

edge = (d2 - d1)                      # 0 on the line between two stones
gap = np.clip(1 - (edge - 3.0) / 5.0, 0, 1)         # dark mortar, about 6 px wide
round_ = np.clip(edge / 26.0, 0, 1) ** 0.6           # stones darken toward their edge, like rounded tops

# Each stone: its own shade and its own patch of rock surface.
shade = rng.uniform(0.86, 1.08, len(pts))[stone]
warm = rng.uniform(-3, 3, len(pts))[stone]
ox = rng.integers(0, 512, len(pts))[stone]
oy = rng.integers(0, 512, len(pts))[stone]
grain = detail[(yy.astype(int) + oy) % 512, (xx.astype(int) + ox) % 512]
grain = grain * (1 + rng.normal(0, 0.035, (N, N)))  # fine grit
base = np.array([150.0, 148.0, 142.0])
img = base[None, None, :] * (shade * grain * (0.62 + 0.38 * round_))[..., None]
img[..., 0] += warm
img[..., 2] -= warm * 0.6
mortar = np.array([48.0, 44.0, 38.0])
img = img * (1 - gap[..., None]) + mortar[None, None, :] * gap[..., None]
Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save('Assets/Textures/Ground/road_stone.png', optimize=True)
print('ok')
