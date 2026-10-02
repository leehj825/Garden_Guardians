"""Close-up of both hands of a posed kin: python3 Tools/hand_closeup.py mesh.glb clip.glb out.png [time]."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from PIL import Image
import preview_pose as pp

mesh, clip, out = sys.argv[1:4]
t = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
Q, QN, uv, I, tex = pp.skinned(mesh, t, clip)
tiles = []
for side in (1, -1):
    sel = (Q[:, 0] * side > 0.12) & (Q[:, 1] < 0.36) & (Q[:, 1] > 0.12)
    c = Q[sel].mean(axis=0)
    Qz = (Q - c) * 3.2 + np.array([0, 0.45, 0])
    pp.render(Qz, QN, uv, I, tex, out + f".{side}.png", 0.0, 0.0, 420)
    tiles.append(Image.open(out + f".{side}.png"))
W = Image.new("RGB", (840, 420))
for i, im in enumerate(tiles):
    W.paste(im, (i * 420, 0))
W.save(out)
