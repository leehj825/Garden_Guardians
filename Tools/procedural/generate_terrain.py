"""A seeded terrain: ground, ponds, oak, rocks and plants — the prototype of what the game will make at run time.

    python Tools/procedural/generate_terrain.py --seed 7 --preview out.png [--json out.json] [--size 100]
    python Tools/procedural/generate_terrain.py --seeds 1-12 --preview sheet.png

Uses the prop kits (extract_props.py, in ./kit) for the oak, boulders and plant clumps, and the tiles
(extract_tiles.py, in ./tiles) for the ground's look. Needs only numpy, scipy and pillow. The same seed
always gives the same garden: the C# port will use its own small random generator so it does too.
"""
import argparse
import glob
import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

HERE = os.path.dirname(os.path.abspath(__file__))
STEP = 0.5
WATER_LEVEL = -1.2  # m, relative to the mean ground (0)
GUARD = 0.5  # dry ground stays this far above the water
TILE_METRES = 6.0


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def fbm(rng, n, octaves):
    """Zero-mean smooth noise on an n x n grid: sum of gaussian-blurred white noise, (sigma in cells, amplitude) per octave."""
    out = np.zeros((n, n))
    for sigma, amplitude in octaves:
        layer = ndi.gaussian_filter(rng.standard_normal((n, n)), sigma, mode="reflect")
        out += amplitude * layer / max(layer.std(), 1e-9)
    return out - out.mean()


def load_kit():
    items = []
    for path in sorted(glob.glob(os.path.join(HERE, "kit", "*.json"))):
        with open(path) as f:
            items += json.load(f)
    return items


def load_tiles():
    tiles = {}
    for kind in ("grass", "dirt", "sand"):
        tiles[kind] = [np.array(Image.open(p).convert("RGB")) for p in sorted(glob.glob(os.path.join(HERE, "tiles", kind + "_*.png")))]
    return tiles


class Garden:
    def __init__(self, seed, size=100.0):
        self.seed = seed
        self.rng = np.random.default_rng(seed)
        self.half = size / 2
        self.n = int(size / STEP) + 1
        xs = -self.half + np.arange(self.n) * STEP
        self.gx, self.gz = np.meshgrid(xs, xs)  # rows = z, cols = x
        self.ponds = []
        self.props = []
        self.oak = None
        self.spring = None

    # --- ground ---------------------------------------------------------------------------------
    def make_ground(self):
        rng = self.rng
        # Gentle rolling ground: a few metres across the big swells, a little grain on top.
        h = fbm(rng, self.n, [(24, 0.55), (10, 0.30), (4, 0.08)])
        self.natural = h.copy()
        margin = 12.0
        count = int(rng.choice([1, 2, 2, 3], p=[0.3, 0.4, 0.2, 0.1]))
        tries = 0
        while len(self.ponds) < count and tries < 200:
            tries += 1
            r = float(rng.uniform(5.5, 12.0))
            stretch = float(rng.uniform(1.0, 1.9))
            angle = float(rng.uniform(0, math.pi))
            reach = r * stretch + margin
            if reach >= self.half - 2:
                continue
            cx, cz = (float(v) for v in rng.uniform(-self.half + reach, self.half - reach, 2))
            if any(math.hypot(cx - p["x"], cz - p["z"]) < (r * stretch + p["r"] * p["stretch"]) + 14 for p in self.ponds):
                continue
            self.ponds.append(dict(x=cx, z=cz, r=r, stretch=stretch, angle=angle, wobble=rng.uniform(0, 100, 3)))
        # Ponds: a bowl below the water level, banks easing back up to the ground.
        d_min = np.full_like(h, 9.0)
        bowl = np.full_like(h, np.inf)
        blend = np.zeros_like(h)
        floor = WATER_LEVEL + GUARD
        land = np.maximum(h, floor + 0.0) if False else h
        lifted = floor + 0.5 * ((h - floor) + np.sqrt((h - floor) ** 2 + 0.25))  # a smooth max(h, floor): the land is never wet
        for p in self.ponds:
            dx, dz = self.gx - p["x"], self.gz - p["z"]
            c, s = math.cos(p["angle"]), math.sin(p["angle"])
            u, v = (dx * c + dz * s) / p["stretch"], -dx * s + dz * c
            theta = np.arctan2(v, u)
            ring = 1 + 0.16 * np.sin(2 * theta + p["wobble"][0]) + 0.10 * np.sin(3 * theta + p["wobble"][1]) + 0.06 * np.sin(5 * theta + p["wobble"][2])
            d = np.hypot(u, v) / (p["r"] * ring)
            profile = np.where(d < 1, WATER_LEVEL - 0.55 * (1 - d ** 2) ** 0.8, WATER_LEVEL + 0.8 * (d - 1))
            w = smoothstep((1.9 - d) / 0.9)
            bowl = np.where(w > blend, profile, bowl)
            blend = np.maximum(blend, w)
            p["d"] = d
            d_min = np.minimum(d_min, d)
        ground = lifted
        for p in self.ponds:
            d = p["d"]
            profile = np.where(d < 1, WATER_LEVEL - 0.55 * (1 - d ** 2) ** 0.8, WATER_LEVEL + 0.8 * (d - 1))
            w = smoothstep((1.9 - d) / 0.9)
            ground = ground * (1 - w) + profile * w
        self.ground = ground
        self.d_min = d_min
        self.water = ground < WATER_LEVEL

    def height_at(self, x, z):
        j = int(np.clip(round((z + self.half) / STEP), 0, self.n - 1))
        i = int(np.clip(round((x + self.half) / STEP), 0, self.n - 1))
        return float(self.ground[j, i])

    def dist_to_pond_edge(self, x, z):
        best = 1e9
        for p in self.ponds:
            best = min(best, math.hypot(x - p["x"], z - p["z"]) - p["r"] * max(1.0, p["stretch"]))
        return best

    # --- props ----------------------------------------------------------------------------------
    def place(self, item, x, z, yaw, scale):
        base = self.height_at(x, z)
        c, s = math.cos(yaw), math.sin(yaw)
        circles = []
        for cx, cz, r in item["circles"]:
            circles.append((x + (cx * c - cz * s) * scale, z + (cx * s + cz * c) * scale, r * scale))
        return dict(name=item["name"], kind=item["kind"], x=x, z=z, base=base, yaw=yaw, scale=scale, circles=circles, radius=item["radius"] * scale)

    def clear_of_everything(self, prop, margin=0.3):
        for other in [self.oak] + self.props if self.oak else self.props:
            for ax, az, ar in prop["circles"]:
                for bx, bz, br in other["circles"]:
                    if math.hypot(ax - bx, az - bz) < ar + br + margin:
                        return False
        return True

    def make_props(self, kit):
        rng = self.rng
        oaks = [k for k in kit if k["kind"] == "oak"]
        rocks = [k for k in kit if k["kind"] == "rock" and k["radius"] < 6]
        plants = [k for k in kit if k["kind"] == "plant" and k["radius"] < 5]
        if oaks:
            item = oaks[int(rng.integers(len(oaks)))]
            for _ in range(300):
                margin = item["radius"] + 8
                x, z = (float(v) for v in rng.uniform(-self.half + margin, self.half - margin, 2))
                if self.dist_to_pond_edge(x, z) < item["radius"] + 10:
                    continue
                # Level the ground under the trunk and roots so it stands on a flat.
                rr = item["radius"] + 4
                inside = np.hypot(self.gx - x, self.gz - z) < rr
                target = float(np.median(self.ground[np.hypot(self.gx - x, self.gz - z) < item["radius"]]))
                w = smoothstep((rr - np.hypot(self.gx - x, self.gz - z)) / 4.0)
                self.ground = self.ground * (1 - w) + target * w
                self.oak = self.place(item, x, z, float(rng.uniform(0, math.tau)), 1.0)
                self.oak["base"] = target
                break
        if self.oak is None:
            self.oak = dict(name="none", kind="oak", x=0, z=0, base=0, yaw=0, scale=1, circles=[], radius=0)
        self.water = self.ground < WATER_LEVEL
        self.props = []

        def try_place(pool, x, z):
            if not pool:
                return False
            for _ in range(6):
                prop = self.place(pool[int(rng.integers(len(pool)))], x, z, float(rng.uniform(0, math.tau)), float(rng.uniform(0.8, 1.3)))
                if self.d_min_at(x, z) < 0.98 and prop["kind"] == "rock":
                    continue
                if self.clear_of_everything(prop):
                    self.props.append(prop)
                    return True
            return False

        for p in self.ponds:
            # A ring of plants along the bank, a pile of boulders on one side.
            perimeter = 2 * math.pi * p["r"] * (1 + p["stretch"]) / 2
            plants_n = int(perimeter / 9) + 2
            for k in range(plants_n):
                theta = rng.uniform(0, math.tau)
                x, z = self.bank_point(p, theta, float(rng.uniform(1.0, 1.25)))
                if abs(x) < self.half - 3 and abs(z) < self.half - 3:
                    try_place(plants, x, z)
            theta = rng.uniform(0, math.tau)
            for k in range(int(rng.integers(2, 6))):
                x, z = self.bank_point(p, theta + rng.normal(0, 0.25), float(rng.uniform(1.0, 1.45)))
                if abs(x) < self.half - 3 and abs(z) < self.half - 3:
                    try_place(rocks, x, z)
        for _ in range(int(self.half * self.half / 900)):  # a few strays on dry ground
            x, z = (float(v) for v in rng.uniform(-self.half + 4, self.half - 4, 2))
            if self.height_at(x, z) > WATER_LEVEL + GUARD and self.d_min_at(x, z) > 1.6:
                try_place(rocks if rng.random() < 0.5 else plants, x, z)

    def d_min_at(self, x, z):
        j = int(np.clip(round((z + self.half) / STEP), 0, self.n - 1))
        i = int(np.clip(round((x + self.half) / STEP), 0, self.n - 1))
        return float(self.d_min[j, i])

    @staticmethod
    def bank_point(p, theta, d):
        c, s = math.cos(p["angle"]), math.sin(p["angle"])
        ring = 1 + 0.16 * math.sin(2 * theta + p["wobble"][0]) + 0.10 * math.sin(3 * theta + p["wobble"][1]) + 0.06 * math.sin(5 * theta + p["wobble"][2])
        u = math.cos(theta) * p["r"] * ring * d * p["stretch"]
        v = math.sin(theta) * p["r"] * ring * d
        return p["x"] + u * c - v * s, p["z"] + u * s + v * c

    # --- creek ----------------------------------------------------------------------------------
    def pick_spring(self):
        g = np.round(self.ground * 100) / 100
        wet_dist = ndi.distance_transform_edt(~self.water) * STEP
        oak_dist = np.full_like(g, 99.0)
        if self.oak["circles"]:
            for cx, cz, r in self.oak["circles"]:
                oak_dist = np.minimum(oak_dist, np.hypot(self.gx - cx, self.gz - cz) - r)
        edge = np.maximum(np.abs(self.gx), np.abs(self.gz))
        ok = (edge >= self.half * 0.6) & (edge <= self.half - 5) & (wet_dist > 20) & (oak_dist > 14)
        if not ok.any():
            ok = (edge >= self.half * 0.6) & (edge <= self.half - 5) & (wet_dist > 8)
        best, best_score = None, -1.0
        for j, i in zip(*np.nonzero(ok)):
            if (i + j) % 3:
                continue
            steps = self.creek_steps(g, self.gx[j, i], self.gz[j, i])
            score = steps + 0.001 * g[j, i]
            if score > best_score:
                best, best_score = (float(self.gx[j, i]), float(self.gz[j, i]), steps), score
        self.spring = best

    def creek_steps(self, g, x, z):
        def h(px, pz):
            fx = min(max((px + self.half) / STEP, 0.0), self.n - 1.001)
            fz = min(max((pz + self.half) / STEP, 0.0), self.n - 1.001)
            ix, iz = int(fx), int(fz)
            tx, tz = fx - ix, fz - iz
            return g[iz, ix] * (1 - tx) * (1 - tz) + g[iz, ix + 1] * tx * (1 - tz) + g[iz + 1, ix] * (1 - tx) * tz + g[iz + 1, ix + 1] * tx * tz
        steps = 0
        for _ in range(60):
            e = 0.05
            sx = (h(x + e, z) - h(x - e, z)) / (2 * e)
            sz = (h(x, z + e) - h(x, z - e)) / (2 * e)
            norm = math.hypot(sx, sz)
            if norm < 0.02:
                break
            nx, nz = x - sx / norm, z - sz / norm
            if h(nx, nz) >= h(x, z) - 0.005 or abs(nx) > self.half - 4 or abs(nz) > self.half - 4:
                break
            x, z = nx, nz
            steps += 1
        return steps

    # --- everything ------------------------------------------------------------------------------
    def generate(self, kit):
        self.make_ground()
        self.make_props(kit)
        self.pick_spring()
        return self

    def stats(self):
        gy, gx = np.gradient(self.ground, STEP)
        slope = np.degrees(np.arctan(np.hypot(gx, gy)))
        dry = ~self.water
        return dict(seed=self.seed, ponds=len(self.ponds), pond_area=float(self.water.sum() * STEP * STEP),
                    mean_slope=float(slope[dry].mean()), p95_slope=float(np.percentile(slope[dry], 95)),
                    props={k: sum(1 for p in self.props if p["kind"] == k) for k in ("rock", "plant")},
                    oak=[self.oak["x"], self.oak["z"]], spring=self.spring)

    # --- picture ---------------------------------------------------------------------------------
    def picture(self, tiles, px_per_m=4):
        n = int(2 * self.half * px_per_m)
        xs = -self.half + (np.arange(n) + 0.5) / px_per_m
        X, Z = np.meshgrid(xs, xs)
        rng = np.random.default_rng(self.seed + 1000)

        def noise(sigma, size=n):
            a = ndi.gaussian_filter(rng.standard_normal((size, size)), sigma * px_per_m, mode="reflect")
            return a / a.std()

        def tile_sample(kind, k, offset):
            tile = tiles[kind][k % len(tiles[kind])]
            t = tile.shape[0]
            u = ((X + offset[0]) / TILE_METRES % 1.0) * t
            v = ((Z + offset[1]) / TILE_METRES % 1.0) * t
            return tile[v.astype(int) % t, u.astype(int) % t].astype(np.float32)

        # Grass: the tiles mixed by slow noise, so no one tile shows its repeat.
        weights = np.stack([np.exp(noise(6.0) * 1.2) for _ in range(len(tiles["grass"]))])
        weights /= weights.sum(0)
        grass = sum(w[..., None] * tile_sample("grass", k, (k * 2.3, k * 1.7)) for k, w in enumerate(weights))
        grass *= (1 + 0.10 * noise(9.0))[..., None]
        ground_px = ndi.zoom(self.ground, n / self.n, order=1)[:n, :n]
        gy, gx = np.gradient(self.ground, STEP)
        slope_px = ndi.zoom(np.hypot(gx, gy), n / self.n, order=1)[:n, :n]
        d_px = ndi.zoom(self.d_min, n / self.n, order=1)[:n, :n]
        dirt = tile_sample("dirt", 0, (0, 0)) if tiles["dirt"] else grass
        patch = smoothstep((noise(5.0) - 0.9) / 0.5) * smoothstep((0.12 - slope_px) / 0.06)
        img = grass * (1 - patch[..., None]) + dirt * patch[..., None]
        sand = tile_sample("sand", 0, (1, 1)) if tiles["sand"] else dirt
        shore = smoothstep((1.35 - d_px) / 0.3) * (d_px > 0.9)
        img = img * (1 - shore[..., None]) + sand * shore[..., None]
        water_px = ground_px < WATER_LEVEL
        deep = smoothstep((WATER_LEVEL - ground_px) / 0.6)[..., None]
        water_col = np.array([90, 140, 205]) * (1 - deep) + np.array([50, 95, 160]) * deep
        img = np.where(water_px[..., None], water_col * (1 + 0.04 * noise(1.0))[..., None], img)
        # A little relief shading.
        gy2, gx2 = np.gradient(ndi.gaussian_filter(ground_px, 1.5))
        img *= (1 + np.clip((-gx2 - gy2) * 6.0 * px_per_m, -0.25, 0.25))[..., None]
        out = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
        draw = ImageDraw.Draw(out)

        def to_px(x, z):
            return (x + self.half) * px_per_m, (z + self.half) * px_per_m
        colours = {"rock": (150, 150, 150), "plant": (30, 90, 40), "oak": (110, 70, 40)}
        for prop in [self.oak] + self.props:
            for cx, cz, r in prop["circles"]:
                x, y = to_px(cx, cz)
                rr = max(r * px_per_m, 1.5)
                draw.ellipse((x - rr, y - rr, x + rr, y + rr), fill=colours[prop["kind"]])
        if self.spring:
            x, y = to_px(self.spring[0], self.spring[1])
            draw.ellipse((x - 5, y - 5, x + 5, y + 5), outline=(255, 255, 255), width=2)
        return out


def parse_seeds(text):
    if "-" in text:
        a, b = text.split("-")
        return list(range(int(a), int(b) + 1))
    return [int(s) for s in text.split(",")]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int)
    ap.add_argument("--seeds")
    ap.add_argument("--size", type=float, default=100.0)
    ap.add_argument("--preview", required=True)
    ap.add_argument("--json")
    args = ap.parse_args()
    kit, tiles = load_kit(), load_tiles()
    seeds = parse_seeds(args.seeds) if args.seeds else [args.seed if args.seed is not None else 1]
    pictures = []
    for seed in seeds:
        garden = Garden(seed, args.size).generate(kit)
        print(json.dumps(garden.stats()))
        pictures.append((seed, garden.picture(tiles, 3 if len(seeds) > 1 else 4)))
        if args.json and len(seeds) == 1:
            with open(args.json, "w") as f:
                json.dump(dict(stats=garden.stats(), ponds=garden.ponds and [{k: (v if k != "d" and k != "wobble" else None) for k, v in p.items()} for p in garden.ponds],
                               oak=garden.oak, props=garden.props), f, default=lambda o: None)
    if len(pictures) == 1:
        pictures[0][1].save(args.preview)
        return
    cols = 4
    w, h = pictures[0][1].size
    rows = math.ceil(len(pictures) / cols)
    sheet = Image.new("RGB", (cols * w, rows * h), (30, 30, 30))
    for k, (seed, pic) in enumerate(pictures):
        ImageDraw.Draw(pic).text((6, 4), "seed %d" % seed, fill=(255, 255, 255))
        sheet.paste(pic, ((k % cols) * w, (k // cols) * h))
    sheet.save(args.preview)


if __name__ == "__main__":
    main()
