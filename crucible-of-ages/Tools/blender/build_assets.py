"""
Builds the Crucible of Ages model library in Blender and saves it as crucible_assets.blend.

    blender -b -P Tools/blender/build_assets.py                    (from the repo root)
    blender -b -P Tools/blender/build_assets.py -- --export        (then export to the game)
    blender -b -P Tools/blender/build_assets.py -- --export --render

The .blend is the source of truth once you start editing by hand: open it, model or vertex-paint
(colour attribute "Col"; pure magenta = the owner's colour), save, and run export_assets.py.
Re-running this script regenerates everything from code and overwrites hand edits, so keep a copy.

Models face -Y and stand on z = 0, one object per model, named <category>_<name>:
  unit_*  one per unit symbol, Civ V style (see build_units.py): squads of 3-4 soldiers, riders,
          engines with crews, ships and aircraft, standing straight on the terrain
  prop_*  terrain decorations (trees, palms, rocks)
  city_*  settlement parts (house, keep, wall segment, tower) assembled by the game per city
  tile_*  terrain tile tops (grassland, hills, desert, water, ice...) and prop_peak_* mountain peaks,
          see build_tiles.py
"""
import math
import os
import random
import sys

import bpy

def _here():
    """This folder, whether run with -P from a terminal or from Blender's Text Editor (use the .blend's folder)."""
    try:
        here = os.path.dirname(os.path.abspath(__file__))
        if os.path.exists(os.path.join(here, "crucible_art.py")):
            return here
    except NameError:
        pass
    return os.path.dirname(bpy.data.filepath)


HERE = _here()
sys.path.append(HERE)
import crucible_art as a  # noqa: E402
from crucible_art import (BARK, JUNGLE, LEAF, LEAF_LIGHT, PLASTER, ROCK, ROOF, STONE, STONE_DARK, TEAM, WOOD, WOOD_DARK, box, cylinder, join, poly, sphere)  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND_PATH = os.path.join(REPO, "Tools", "blender", "crucible_assets.blend")

# ---------------------------------------------------------------------------------------- units

# Civ V-style formations, riders, engines, ships and aircraft live in build_units.py.
import build_units  # noqa: E402
UNITS = build_units.UNITS

# ---------------------------------------------------------------------------------------- props

def conifer(seed):
    rnd = random.Random(seed)
    p = [cylinder(0.025, 0.1, BARK, loc=(0, 0, 0.05), sides=5)]
    h = 0.5
    for k, (z0, r) in enumerate(((0.06, 0.17), (0.2, 0.13), (0.32, 0.09))):
        shade = a.hexcol(0x2C5A36) if k == 0 else a.hexcol(0x33663C) if k == 1 else a.hexcol(0x3F7A45)
        p.append(cylinder(r, 0.22, shade, loc=(0, 0, z0 + 0.11), sides=7, top=0.0, rot=(0, 0, rnd.uniform(0, 60))))
    del h
    return p


def broadleaf(seed):
    rnd = random.Random(seed)
    p = [cylinder(0.028, 0.18, BARK, loc=(0, 0, 0.09), sides=6, top=0.02)]
    for k in range(3):
        ang = k * 2.1 + rnd.uniform(0, 0.5)
        p.append(sphere(rnd.uniform(0.1, 0.13), LEAF if k else LEAF_LIGHT,
                        loc=(math.cos(ang) * 0.05, math.sin(ang) * 0.05, 0.25 + rnd.uniform(0, 0.05)), subdiv=1, jitter=0.12, seed=seed + k))
    return p


def jungle_tree(seed):
    rnd = random.Random(seed)
    p = [cylinder(0.03, 0.34, BARK, loc=(0, 0, 0.17), sides=6, top=0.02)]
    p.append(sphere(0.17, JUNGLE, loc=(0, 0, 0.38), subdiv=1, scale=(1.2, 1.2, 0.55), jitter=0.12, seed=seed))
    p.append(sphere(0.1, a.hexcol(0x4FA046), loc=(0.05, -0.03, 0.44), subdiv=1, jitter=0.15, seed=seed + 1))
    for k in range(3):
        p.append(cylinder(0.006, 0.12, BARK, loc=(rnd.uniform(-0.08, 0.08), rnd.uniform(-0.08, 0.08), 0.3), sides=4))  # vines
    return p


def palm(seed):
    rnd = random.Random(seed)
    p = []
    x = y = 0.0
    z = 0.0
    lean = rnd.uniform(6, 14)
    for k in range(4):
        tilt = lean * (k + 1) * 0.5
        seg = 0.1
        p.append(cylinder(0.022 - k * 0.002, seg, BARK, loc=(x, y, z + seg / 2), rot=(0, tilt, 0), sides=6))
        x += math.sin(math.radians(tilt)) * seg
        z += math.cos(math.radians(tilt)) * seg
    for k in range(7):
        ang = k * 360 / 7 + rnd.uniform(-10, 10)
        r = math.radians(ang)
        tip = (x + math.cos(r) * 0.2, y + math.sin(r) * 0.2, z - 0.06)
        mid = (x + math.cos(r) * 0.1, y + math.sin(r) * 0.1, z + 0.03)
        side = (-math.sin(r) * 0.03, math.cos(r) * 0.03, 0)
        verts = [(x, y, z), (mid[0] + side[0], mid[1] + side[1], mid[2]), tip, (mid[0] - side[0], mid[1] - side[1], mid[2])]
        p.append(poly(verts, [(0, 1, 2, 3)], a.hexcol(0x5E9E3A) if k % 2 else a.hexcol(0x4E8A32)))
    p.append(sphere(0.03, BARK, loc=(x, y, z), subdiv=1))
    return p


def rock(seed):
    return [sphere(0.08, ROCK, loc=(0, 0, 0.04), subdiv=1, scale=(1.3, 1.0, 0.7), jitter=0.25, seed=seed),
            sphere(0.045, a.hexcol(0x5A504A), loc=(0.08, 0.04, 0.025), subdiv=1, jitter=0.25, seed=seed + 7)]


PROPS = {
    "conifer": lambda: conifer(1),
    "conifer_b": lambda: conifer(2),
    "broadleaf": lambda: broadleaf(3),
    "broadleaf_b": lambda: broadleaf(4),
    "jungle": lambda: jungle_tree(5),
    "palm": lambda: palm(6),
    "rock": lambda: rock(7),
}

# ---------------------------------------------------------------------------------------- city parts

def house():
    p = [box((0.13, 0.16, 0.12), PLASTER, loc=(0, 0, 0.06), bevel=0.004)]
    p.append(poly([(-0.08, -0.095, 0.12), (0.08, -0.095, 0.12), (0.08, 0.095, 0.12), (-0.08, 0.095, 0.12), (0, -0.095, 0.2), (0, 0.095, 0.2)],
                  [(0, 1, 4), (3, 5, 2), (0, 4, 5, 3), (1, 2, 5, 4), (0, 3, 2, 1)], ROOF, bevel=0.003))
    p.append(box((0.035, 0.005, 0.06), WOOD_DARK, loc=(0, -0.081, 0.03)))                                        # door
    for side in (-1, 1):
        p.append(box((0.005, 0.03, 0.03), a.hexcol(0x6B7784), loc=(0.066 * side, 0, 0.08)))                     # windows
    p.append(box((0.03, 0.03, 0.06), STONE, loc=(0.04, 0.05, 0.2)))                                              # chimney
    return p


def keep():
    p = [cylinder(0.16, 0.36, STONE, loc=(0, 0, 0.18), sides=8, top=0.14, bevel=0.006)]
    p.append(cylinder(0.165, 0.03, STONE_DARK, loc=(0, 0, 0.37), sides=8))
    for k in range(8):
        r = math.radians(k * 45 + 22.5)
        p.append(box((0.045, 0.03, 0.04), STONE, loc=(math.cos(r) * 0.14, math.sin(r) * 0.14, 0.405), rot=(0, 0, k * 45 + 22.5)))
    p.append(box((0.05, 0.006, 0.08), WOOD_DARK, loc=(0, -0.155, 0.04)))                                          # gate
    p.append(cylinder(0.006, 0.26, WOOD, loc=(0, 0, 0.5), sides=5))
    p.append(box((0.1, 0.004, 0.07), TEAM, loc=(0.052, 0, 0.58)))                                                # banner
    return p


def wall_segment():
    """One straight stretch of curtain wall, 1.0 long along X, centred on the origin."""
    p = [box((1.0, 0.05, 0.14), STONE, loc=(0, 0, 0.07), bevel=0.004)]
    for k in range(9):
        p.append(box((0.06, 0.055, 0.035), STONE_DARK, loc=(-0.44 + k * 0.11, 0, 0.155)))
    return p


def tower():
    return [cylinder(0.065, 0.24, STONE, loc=(0, 0, 0.12), sides=8, top=0.058, bevel=0.004),
            cylinder(0.075, 0.1, TEAM, loc=(0, 0, 0.29), sides=8, top=0.0)]


CITY = {
    "house": house,
    "keep": keep,
    "wall": wall_segment,
    "tower": tower,
}

# ---------------------------------------------------------------------------------------- build

def build():
    a.reset_scene()
    import build_tiles
    props = dict(PROPS, **build_tiles.all_peaks())
    for cat, table in (("unit", UNITS), ("prop", props), ("city", CITY), ("tile", build_tiles.all_tiles())):
        a.collection(cat)
        for name, make in table.items():
            join(make(), f"{cat}_{name}", cat)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Saved {BLEND_PATH}")


if __name__ == "__main__":
    build()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    if "--export" in args:
        import export_assets
        export_assets.export_all()
        if "--render" in args:
            export_assets.render_previews()
