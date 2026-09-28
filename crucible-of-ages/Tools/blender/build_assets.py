"""
Builds the Crucible of Ages model library in Blender and saves it as crucible_assets.blend.

    blender -b -P Tools/blender/build_assets.py                    (from the repo root)
    blender -b -P Tools/blender/build_assets.py -- --export        (then export to the game)
    blender -b -P Tools/blender/build_assets.py -- --export --render

The .blend is the source of truth once you start editing by hand: open it, model or vertex-paint
(colour attribute "Col"; pure magenta = the owner's colour), save, and run export_assets.py.
Re-running this script regenerates everything from code and overwrites hand edits, so keep a copy.

Models face -Y and stand on z = 0, one object per model, named <category>_<name>:
  unit_*  one per unit symbol (squads of three for foot troops, rider pairs, engines, ships, planes)
  prop_*  terrain decorations (trees, palms, rocks)
  city_*  settlement parts (house, keep, wall segment, tower) assembled by the game per city
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
from crucible_art import (BARK, CANVAS, CLOTH, GOLD, HORSE, HORSE_DARK, IRON, JUNGLE, LEAF, LEAF_LIGHT, LEATHER,  # noqa: E402
                          NAVY, OLIVE, PINE, PLASTER, PLINTH, ROCK, ROOF, SKIN, STEEL, STONE, STONE_DARK, STRAW,
                          TEAM, TEAM_DARK, WOOD, WOOD_DARK, GLASS, box, cylinder, group, hex_plinth, join, poly,
                          sphere, torus_arc)

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND_PATH = os.path.join(REPO, "Tools", "blender", "crucible_assets.blend")

# ---------------------------------------------------------------------------------------- people

def figure(weapon, helmet="steel", body=TEAM, legs=CLOTH, x=0.0, y=0.0, turn=0.0):
    """One soldier about 0.4 tall facing -Y. weapon: sword, spear, bow, crossbow, musket, rifle, staff, pick, pack, none."""
    p = []
    for side in (-1, 1):
        p.append(cylinder(0.019, 0.15, legs, loc=(0.024 * side, 0, 0.11), sides=6, top=0.016))           # legs
        p.append(box((0.03, 0.05, 0.022), LEATHER, loc=(0.024 * side, -0.01, 0.045)))                    # boots
    p.append(cylinder(0.052, 0.12, body, loc=(0, 0, 0.245), sides=8, top=0.058, bevel=0.006))              # torso
    p.append(cylinder(0.058, 0.035, body, loc=(0, 0, 0.195), sides=8, top=0.052))                          # tunic skirt
    p.append(cylinder(0.054, 0.014, LEATHER, loc=(0, 0, 0.215), sides=8))                                  # belt
    for side in (-1, 1):
        p.append(cylinder(0.015, 0.11, body, loc=(0.066 * side, -0.01, 0.25), rot=(15, -12 * side, 0), sides=6))  # arms
        p.append(sphere(0.016, SKIN, loc=(0.072 * side, -0.03, 0.196), subdiv=1))                          # hands
    p.append(cylinder(0.018, 0.02, SKIN, loc=(0, 0, 0.312), sides=6))                                       # neck
    p.append(sphere(0.036, SKIN, loc=(0, 0, 0.345), subdiv=2, scale=(0.92, 0.95, 1.05)))                    # head

    if helmet == "steel":
        p.append(sphere(0.04, STEEL, loc=(0, 0, 0.36), subdiv=2, scale=(1, 1, 0.8)))
        p.append(box((0.012, 0.01, 0.03), STEEL, loc=(0, -0.037, 0.34)))                                    # nasal
    elif helmet == "crest":
        p.append(sphere(0.04, STEEL, loc=(0, 0, 0.36), subdiv=2, scale=(1, 1, 0.8)))
        p.append(box((0.012, 0.07, 0.03), TEAM, loc=(0, 0.005, 0.402), bevel=0.004))
    elif helmet == "tricorn":
        p.append(cylinder(0.06, 0.03, CLOTH, loc=(0, 0, 0.38), sides=3, top=0.035, rot=(0, 0, 90)))
    elif helmet == "modern":
        p.append(sphere(0.043, OLIVE, loc=(0, 0, 0.357), subdiv=2, scale=(1.05, 1.1, 0.75)))
    elif helmet == "hood":
        p.append(sphere(0.043, a.hexcol(0x3E5A3A), loc=(0, 0.004, 0.355), subdiv=2, scale=(1, 1.05, 1)))
        p.append(cylinder(0.07, 0.24, a.hexcol(0x3E5A3A), loc=(0, 0.01, 0.2), sides=7, top=0.045, cap=False))  # cloak
    elif helmet == "straw":
        p.append(cylinder(0.07, 0.03, STRAW, loc=(0, 0, 0.375), sides=10, top=0.02))
    elif helmet == "cap":
        p.append(sphere(0.038, CLOTH, loc=(0, 0, 0.362), subdiv=1, scale=(1, 1, 0.6)))

    hand = (0.075, -0.035, 0.2)
    if weapon == "sword":
        p.append(box((0.012, 0.004, 0.16), STEEL, loc=(0.075, -0.07, 0.27), rot=(25, 0, 0), taper=(0.4, 1)))
        p.append(box((0.05, 0.012, 0.01), GOLD, loc=(0.075, -0.042, 0.2), rot=(25, 0, 0)))
        p.append(cylinder(0.065, 0.014, TEAM_DARK, loc=(-0.075, -0.045, 0.24), rot=(90, 0, 10), sides=10, bevel=0.004))  # round shield
        p.append(sphere(0.016, GOLD, loc=(-0.078, -0.055, 0.24), subdiv=1))
    elif weapon == "spear":
        p.append(cylinder(0.007, 0.5, WOOD, loc=(0.075, -0.035, 0.3), sides=5))
        p.append(cylinder(0.016, 0.06, STEEL, loc=(0.075, -0.035, 0.58), sides=4, top=0.0))
        p.append(box((0.018, 0.12, 0.15), TEAM_DARK, loc=(-0.07, -0.04, 0.23), rot=(0, 0, 8), bevel=0.006))  # kite shield
    elif weapon == "bow":
        p.append(torus_arc(0.12, 0.006, -65, 65, WOOD_DARK, loc=(-0.02 + 0.075, -0.07, 0.25), rot=(0, 0, 90), steps=8))
        p.append(cylinder(0.002, 0.21, CANVAS, loc=(0.075, -0.018, 0.25), sides=3))
        p.append(cylinder(0.016, 0.1, LEATHER, loc=(-0.03, 0.05, 0.28), rot=(-15, 20, 0), sides=6))          # quiver
    elif weapon == "crossbow":
        p.append(box((0.018, 0.14, 0.018), WOOD, loc=(0.0, -0.1, 0.24)))
        p.append(torus_arc(0.09, 0.006, 20, 160, IRON, loc=(0.0, -0.16, 0.22), rot=(90, 0, 0), steps=6))
    elif weapon in ("musket", "rifle"):
        p.append(cylinder(0.007, 0.32, IRON if weapon == "rifle" else WOOD_DARK, loc=(0.06, -0.06, 0.3), rot=(-10, 25, 0), sides=5))
        p.append(box((0.02, 0.02, 0.08), WOOD_DARK, loc=(0.03, -0.05, 0.19), rot=(-10, 25, 0)))
        if weapon == "musket":
            p.append(cylinder(0.003, 0.07, STEEL, loc=(0.1, -0.072, 0.47), rot=(-10, 25, 0), sides=3, top=0.0))
    elif weapon == "staff":
        p.append(cylinder(0.006, 0.42, WOOD, loc=(0.075, -0.035, 0.22), sides=5))
    elif weapon == "pick":
        p.append(cylinder(0.007, 0.26, WOOD, loc=(0.075, -0.04, 0.26), rot=(0, -25, 0), sides=5))
        p.append(box((0.1, 0.012, 0.014), IRON, loc=(0.13, -0.04, 0.37), rot=(0, -25, 0), taper=(0.3, 1)))
    elif weapon == "pack":
        p.append(box((0.09, 0.06, 0.1), CANVAS, loc=(0, 0.06, 0.26), bevel=0.01))
        p.append(cylinder(0.022, 0.1, a.hexcol(0x9A7C55), loc=(0, 0.07, 0.33), rot=(0, 90, 0), sides=8))       # bedroll
    del hand
    return group(p, offset=(x, y, 0), rot_z=turn)


def squad(weapon, helmet, **kw):
    parts = hex_plinth()
    for (x, y, t) in ((0.0, -0.1, 0), (-0.13, 0.08, 6), (0.13, 0.08, -6)):
        parts += group(figure(weapon, helmet, **kw), offset=(x, y, 0.035), rot_z=t)
    return parts


def robed(accent, prop):
    """Great people: a robed figure with an emblem of their calling."""
    p = hex_plinth()
    robe = TEAM if prop == "general" else a.hexcol(0xE8DDC4)
    p.append(cylinder(0.075, 0.28, robe, loc=(0, 0, 0.175), sides=9, top=0.045, bevel=0.005))
    p.append(cylinder(0.078, 0.02, accent, loc=(0, 0, 0.05), sides=9))
    p.append(cylinder(0.05, 0.02, TEAM, loc=(0, 0, 0.3), sides=9))                                            # stole
    for side in (-1, 1):
        p.append(cylinder(0.018, 0.12, robe, loc=(0.06 * side, -0.02, 0.24), rot=(25, -10 * side, 0), sides=6))
    p.append(sphere(0.038, SKIN, loc=(0, 0, 0.36), subdiv=2))
    if prop == "general":
        p.append(cylinder(0.042, 0.03, GOLD, loc=(0, 0, 0.395), sides=8, top=0.046))                            # crown
        p.append(cylinder(0.006, 0.5, WOOD, loc=(0.1, 0, 0.3), sides=5))
        p.append(box((0.004, 0.09, 0.07), TEAM, loc=(0.1, 0.045, 0.49)))                                         # standard
        p.append(cylinder(0.08, 0.2, TEAM_DARK, loc=(0, 0.04, 0.2), sides=8, top=0.05, cap=False))              # cape
    elif prop == "scientist":
        p.append(sphere(0.025, GLASS, loc=(0.07, -0.05, 0.22), subdiv=2))
        p.append(cylinder(0.008, 0.04, GLASS, loc=(0.07, -0.05, 0.255), sides=6))
    elif prop == "engineer":
        p.append(box((0.08, 0.06, 0.005), CANVAS, loc=(0.06, -0.06, 0.22), rot=(40, 0, 0)))                      # plans
        p.append(cylinder(0.012, 0.09, CANVAS, loc=(-0.07, -0.03, 0.22), rot=(0, 80, 0), sides=6))
    elif prop == "merchant":
        p.append(box((0.05, 0.04, 0.04), GOLD, loc=(0.07, -0.05, 0.2), bevel=0.006))
        p.append(sphere(0.03, LEATHER, loc=(-0.07, -0.03, 0.17), subdiv=1))
    elif prop == "artist":
        p.append(cylinder(0.05, 0.006, WOOD, loc=(-0.07, -0.05, 0.23), rot=(70, 0, 0), sides=10))               # palette
        p.append(cylinder(0.004, 0.12, WOOD, loc=(0.075, -0.04, 0.26), rot=(0, -25, 0), sides=4))
    elif prop == "prophet":
        p.append(cylinder(0.006, 0.44, WOOD, loc=(0.08, 0, 0.24), sides=5))
        p.append(sphere(0.025, a.hexcol(0xFFB347), loc=(0.08, 0, 0.48), subdiv=1))
    return p


def rider(x, y, team_part=TEAM, lance=True):
    p = []
    p.append(sphere(0.072, HORSE, loc=(0, 0, 0.2), scale=(0.85, 1.9, 0.95), subdiv=2))                         # barrel
    for (lx, ly) in ((-0.035, -0.09), (0.035, -0.09), (-0.035, 0.09), (0.035, 0.09)):
        p.append(cylinder(0.013, 0.16, HORSE_DARK, loc=(lx, ly, 0.09), sides=6, top=0.011))
        p.append(box((0.024, 0.026, 0.02), (0.15, 0.12, 0.1), loc=(lx, ly - 0.004, 0.012)))                  # hooves
    p.append(cylinder(0.03, 0.14, HORSE, loc=(0, -0.13, 0.26), rot=(38, 0, 0), sides=7, top=0.024))          # neck
    p.append(box((0.04, 0.1, 0.04), HORSE, loc=(0, -0.2, 0.31), rot=(-25, 0, 0), bevel=0.01, taper=(0.8, 0.8)))  # head
    for side in (-1, 1):
        p.append(cylinder(0.008, 0.03, HORSE_DARK, loc=(0.013 * side, -0.16, 0.35), sides=4, top=0.0))       # ears
    p.append(box((0.012, 0.03, 0.12), HORSE_DARK, loc=(0, -0.115, 0.29), rot=(38, 0, 0)))                         # mane
    p.append(cylinder(0.014, 0.12, HORSE_DARK, loc=(0, 0.15, 0.16), rot=(150, 0, 0), sides=5, top=0.004))      # tail
    p.append(box((0.13, 0.1, 0.012), team_part, loc=(0, 0.0, 0.25), bevel=0.004))                             # caparison
    # Rider
    p.append(cylinder(0.045, 0.1, team_part, loc=(0, 0.01, 0.32), sides=8, top=0.05))
    for side in (-1, 1):
        p.append(cylinder(0.014, 0.1, CLOTH, loc=(0.05 * side, -0.01, 0.25), rot=(-60, 0, 0), sides=5))
    p.append(sphere(0.032, SKIN, loc=(0, 0.01, 0.405), subdiv=2))
    p.append(sphere(0.036, STEEL, loc=(0, 0.01, 0.42), subdiv=2, scale=(1, 1, 0.8)))
    if lance:
        p.append(cylinder(0.006, 0.55, WOOD, loc=(0.07, -0.12, 0.38), rot=(70, 0, 0), sides=5))
        p.append(cylinder(0.01, 0.05, STEEL, loc=(0.07, -0.385, 0.475), rot=(70, 0, 0), sides=4, top=0.0))
    return group(p, offset=(x, y, 0))


def cavalry():
    parts = hex_plinth()
    parts += group(rider(-0.12, 0.03), offset=(0, 0, 0.035))
    parts += group(rider(0.12, -0.05), offset=(0, 0, 0.035))
    return parts

# ---------------------------------------------------------------------------------------- machines

def wheel(x, y, z, r, color=WOOD_DARK, spokes=True):
    p = [cylinder(r, 0.025, color, loc=(x, y, z), rot=(0, 90, 0), sides=10, bevel=0.004)]
    if spokes:
        p.append(cylinder(r * 0.3, 0.035, IRON, loc=(x, y, z), rot=(0, 90, 0), sides=6))
    return p


def catapult():
    p = hex_plinth()
    z = 0.035
    p.append(box((0.16, 0.3, 0.035), WOOD, loc=(0, 0, z + 0.07), bevel=0.005))
    for side in (-1, 1):
        p += wheel(0.1 * side, -0.1, z + 0.055, 0.055)
        p += wheel(0.1 * side, 0.1, z + 0.055, 0.055)
        p.append(box((0.022, 0.03, 0.17), WOOD_DARK, loc=(0.06 * side, 0.02, z + 0.17), rot=(8, 0, 0)))
    p.append(box((0.14, 0.02, 0.02), WOOD_DARK, loc=(0, 0.03, z + 0.24)))
    p.append(box((0.025, 0.025, 0.34), WOOD, loc=(0, -0.02, z + 0.2), rot=(-55, 0, 0)))                        # arm
    p.append(sphere(0.04, WOOD_DARK, loc=(0, -0.16, z + 0.3), subdiv=1, scale=(1, 1, 0.6)))                    # cup
    p.append(sphere(0.028, ROCK, loc=(0, -0.16, z + 0.33), subdiv=1))
    p.append(box((0.16, 0.012, 0.05), TEAM, loc=(0, 0.15, z + 0.1)))
    p += group(figure("none", "cap", x=0, y=0), offset=(-0.17, 0.13, z), rot_z=20)
    return p


def cannon():
    p = hex_plinth()
    z = 0.035
    p.append(box((0.06, 0.24, 0.035), WOOD, loc=(0, 0.07, z + 0.05), rot=(12, 0, 0), bevel=0.005))           # trail
    p.append(cylinder(0.04, 0.3, IRON, loc=(0, -0.06, z + 0.13), rot=(78, 0, 0), sides=10, top=0.03, bevel=0.004))
    p.append(cylinder(0.035, 0.03, IRON, loc=(0, -0.2, z + 0.16), rot=(78, 0, 0), sides=10))                 # muzzle ring
    p += wheel(-0.07, 0.0, z + 0.085, 0.085)
    p += wheel(0.07, 0.0, z + 0.085, 0.085)
    p.append(box((0.05, 0.03, 0.02), TEAM, loc=(0, 0.19, z + 0.03)))
    p += group(figure("musket", "tricorn"), offset=(-0.2, 0.12, z), rot_z=15)
    p += group(figure("musket", "tricorn"), offset=(0.2, 0.12, z), rot_z=-15)
    return p


def rocket_truck():
    p = hex_plinth()
    z = 0.035
    body = OLIVE
    p.append(box((0.15, 0.34, 0.07), body, loc=(0, 0, z + 0.09), bevel=0.01))
    p.append(box((0.14, 0.1, 0.08), body, loc=(0, -0.12, z + 0.16), bevel=0.012))                             # cab
    p.append(box((0.12, 0.01, 0.04), a.hexcol(0x9FC4D8), loc=(0, -0.172, z + 0.175)))                         # windscreen
    p.append(box((0.15, 0.2, 0.08), IRON, loc=(0, 0.06, z + 0.2), rot=(22, 0, 0), bevel=0.006))              # launcher
    for i in range(3):
        for j in range(2):
            p.append(cylinder(0.014, 0.02, PLINTH, loc=(-0.045 + i * 0.045, -0.04, z + 0.19 + j * 0.04), rot=(-68, 0, 0), sides=6))
    for side in (-1, 1):
        for y in (-0.11, 0.02, 0.12):
            p += wheel(0.08 * side, y, z + 0.045, 0.045, PLINTH, spokes=False)
    p.append(box((0.08, 0.004, 0.04), TEAM, loc=(0, -0.172, z + 0.1)))
    return p


def ram():
    p = hex_plinth()
    z = 0.035
    p.append(box((0.16, 0.32, 0.025), WOOD, loc=(0, 0, z + 0.07)))
    hide = TEAM_DARK
    p.append(poly([(-0.1, -0.17, 0.08), (0.1, -0.17, 0.08), (0.1, 0.17, 0.08), (-0.1, 0.17, 0.08), (0, -0.17, 0.24), (0, 0.17, 0.24)],
                  [(0, 1, 4), (3, 5, 2), (0, 4, 5, 3), (1, 2, 5, 4)], hide, loc=(0, 0, z)))
    p.append(cylinder(0.035, 0.46, WOOD_DARK, loc=(0, -0.03, z + 0.13), rot=(90, 0, 0), sides=8))
    p.append(sphere(0.05, IRON, loc=(0, -0.27, z + 0.13), subdiv=1, scale=(1, 1.3, 1)))                        # ram head
    for side in (-1, 1):
        p += wheel(0.1 * side, -0.1, z + 0.05, 0.05)
        p += wheel(0.1 * side, 0.1, z + 0.05, 0.05)
    return p


def siege_tower():
    p = hex_plinth()
    z = 0.035
    p.append(box((0.2, 0.2, 0.5), WOOD, loc=(0, 0, z + 0.3), taper=(0.75, 0.75), bevel=0.006))
    for k in range(4):
        p.append(box((0.205, 0.205, 0.012), WOOD_DARK, loc=(0, 0, z + 0.12 + k * 0.11), taper=(0.97, 0.97)))  # planks
    p.append(box((0.13, 0.02, 0.15), WOOD_DARK, loc=(0, -0.09, z + 0.48), rot=(-15, 0, 0)))                 # drawbridge
    p.append(box((0.1, 0.005, 0.07), TEAM, loc=(0, -0.104, z + 0.47), rot=(-15, 0, 0)))
    for side in (-1, 1):
        p += wheel(0.1 * side, -0.07, z + 0.045, 0.045)
        p += wheel(0.1 * side, 0.07, z + 0.045, 0.045)
    return p


def tank():
    p = hex_plinth()
    z = 0.035
    for side in (-1, 1):
        p.append(box((0.055, 0.34, 0.07), PLINTH, loc=(0.095 * side, 0, z + 0.05), bevel=0.02, segments=2))
        for y in (-0.12, -0.04, 0.04, 0.12):
            p.append(cylinder(0.028, 0.06, IRON, loc=(0.095 * side, y, z + 0.045), rot=(0, 90, 0), sides=8))
    hull = a.hexcol(0x5C6B3A)
    p.append(box((0.19, 0.32, 0.07), hull, loc=(0, 0, z + 0.11), taper=(0.9, 0.85), bevel=0.012))
    p.append(cylinder(0.075, 0.06, hull, loc=(0, 0.02, z + 0.175), sides=8, top=0.06, bevel=0.008))
    p.append(cylinder(0.012, 0.24, IRON, loc=(0, -0.14, z + 0.18), rot=(-90, 0, 0), sides=8))
    p.append(cylinder(0.02, 0.03, IRON, loc=(0, -0.25, z + 0.18), rot=(-90, 0, 0), sides=8))                  # muzzle brake
    p.append(box((0.07, 0.005, 0.03), TEAM, loc=(0, -0.061, z + 0.175)))
    p.append(box((0.03, 0.03, 0.02), hull, loc=(0.03, 0.05, z + 0.215)))                                        # hatch
    return p

# ---------------------------------------------------------------------------------------- ships and planes

def hull(length, beam, depth, side_color, deck_color, z=0.0):
    """Pointed bow toward -Y."""
    L, B, D = length / 2, beam / 2, depth
    top = [(-B, L, D), (B, L, D), (B, -L * 0.55, D), (0, -L, D * 1.15), (-B, -L * 0.55, D)]
    bottom = [(-B * 0.55, L * 0.9, 0), (B * 0.55, L * 0.9, 0), (B * 0.55, -L * 0.45, 0), (0, -L * 0.85, 0), (-B * 0.55, -L * 0.45, 0)]
    verts = [(x, y, zz + z) for (x, y, zz) in top + bottom]
    faces = [(0, 1, 2, 3, 4)[::-1], (5, 6, 7, 8, 9)]
    for i in range(5):
        j = (i + 1) % 5
        faces.append((i, j, 5 + j, 5 + i))
    h = poly(verts, faces, side_color, bevel=0.006)
    deck = poly([(x * 0.94, y * 0.94, D + z + 0.003) for (x, y, _) in top], [(0, 1, 2, 3, 4)[::-1]], deck_color)
    return [h, deck]


def sail_ship():
    p = hull(0.62, 0.2, 0.1, WOOD, a.hexcol(0xB08A5E))
    p.append(box((0.2, 0.08, 0.05), WOOD_DARK, loc=(0, 0.24, 0.13), bevel=0.006))                             # stern castle
    for (y, h) in ((-0.06, 0.42), (0.14, 0.34)):
        p.append(cylinder(0.009, h, WOOD_DARK, loc=(0, y, 0.1 + h / 2), sides=6))
        p.append(poly([(-0.13, y + 0.005, 0.18), (0.13, y + 0.005, 0.18), (0.11, y - 0.02, 0.1 + h - 0.05), (-0.11, y - 0.02, 0.1 + h - 0.05)],
                      [(0, 1, 2, 3)], CANVAS))
        p.append(poly([(-0.13, y - 0.001, 0.2), (0.13, y - 0.001, 0.2), (0.125, y - 0.006, 0.25), (-0.125, y - 0.006, 0.25)], [(0, 1, 2, 3)], TEAM))
    p.append(box((0.004, 0.06, 0.03), TEAM, loc=(0, -0.03, 0.56)))                                            # pennant
    p.append(cylinder(0.005, 0.18, WOOD_DARK, loc=(0, -0.36, 0.14), rot=(-65, 0, 0), sides=5))                 # bowsprit
    return p


def steamship(carrier=False):
    p = hull(0.7, 0.22, 0.09, NAVY, a.hexcol(0x8C949C))
    p.append(box((0.2, 0.62, 0.008), TEAM_DARK, loc=(0, 0.02, 0.087)))                                       # boot stripe
    if carrier:
        p.append(box((0.3, 0.72, 0.02), a.hexcol(0x5E6670), loc=(0, 0, 0.13), bevel=0.006))
        p.append(box((0.04, 0.12, 0.1), NAVY, loc=(0.12, 0.05, 0.19), bevel=0.006))
        p.append(box((0.02, 0.3, 0.002), (0.9, 0.9, 0.85), loc=(-0.03, 0, 0.141)))
        p.append(box((0.12, 0.1, 0.008), TEAM, loc=(0, 0.28, 0.141)))
        return p
    p.append(box((0.13, 0.22, 0.06), NAVY, loc=(0, 0.05, 0.13), bevel=0.008))
    p.append(box((0.09, 0.1, 0.05), NAVY, loc=(0, 0.04, 0.18), bevel=0.006))
    p.append(cylinder(0.025, 0.12, PLINTH, loc=(0, 0.12, 0.2), sides=8, top=0.022))
    p.append(cylinder(0.026, 0.02, TEAM, loc=(0, 0.12, 0.255), sides=8))
    for (y, d) in ((-0.17, -1), (0.25, 1)):
        p.append(cylinder(0.04, 0.04, IRON, loc=(0, y, 0.12), sides=8, bevel=0.005))
        p.append(cylinder(0.008, 0.12, IRON, loc=(0, y - 0.07 * d, 0.13), rot=(-90 * d, 0, 0), sides=6))
    p.append(cylinder(0.004, 0.14, IRON, loc=(0, 0.02, 0.27), sides=4))                                        # mast
    return p


def plane(kind):
    p = []
    z = 0.34
    body = a.hexcol(0x9AA4AE) if kind == "jet" else a.hexcol(0x6E7A5A)
    span, length = (0.34, 0.3) if kind == "bomber" else (0.25, 0.24)
    p.append(cylinder(0.035 if kind != "bomber" else 0.045, length, body, loc=(0, 0, z), rot=(90, 0, 0), sides=8, top=0.02, bevel=0.004))
    p.append(sphere(0.03, a.hexcol(0x9FC4D8), loc=(0, -0.05, z + 0.03), subdiv=1, scale=(0.7, 1.3, 0.7)))    # canopy
    if kind == "jet":
        p.append(poly([(0, -0.1, z), (0.22, 0.1, z), (0.04, 0.12, z), (-0.04, 0.12, z), (-0.22, 0.1, z)],
                      [(0, 1, 2, 3, 4)[::-1]], body, bevel=0.0))
        p.append(box((0.006, 0.06, 0.07), body, loc=(0, 0.1, z + 0.04), taper=(1, 0.5)))
    else:
        p.append(box((span * 2, 0.07, 0.012), body, loc=(0, -0.01, z), bevel=0.006))
        p.append(box((0.14, 0.04, 0.008), body, loc=(0, 0.12, z + 0.01)))
        p.append(box((0.006, 0.05, 0.06), body, loc=(0, 0.125, z + 0.04)))
        props = (-0.2, -0.1, 0.1, 0.2) if kind == "bomber" else (0,)
        for x in props:
            if kind == "bomber":
                p.append(cylinder(0.018, 0.08, IRON, loc=(x, -0.03, z - 0.005), rot=(90, 0, 0), sides=8))
            p.append(cylinder(0.05, 0.004, IRON, loc=(x, -0.075 if kind == "bomber" else -0.125, z), rot=(90, 0, 0), sides=3))
    for side in (-1, 1):
        p.append(cylinder(0.024, 0.004, TEAM, loc=(0.15 * side if kind != "jet" else 0.11 * side, 0, z + 0.008), sides=10))  # roundels
    p.append(cylinder(0.008, z, IRON, loc=(0, 0, z / 2), sides=6))                                               # flight stand
    p.append(cylinder(0.09, 0.02, PLINTH, loc=(0, 0, 0.01), sides=6, rot=(0, 0, 30)))
    p.append(cylinder(0.08, 0.004, TEAM, loc=(0, 0, 0.022), sides=6, rot=(0, 0, 30)))
    return p


def settler():
    p = hex_plinth()
    z = 0.035
    p += group(figure("pack", "cap"), offset=(-0.12, -0.02, z), rot_z=-10)
    p.append(box((0.12, 0.2, 0.03), WOOD, loc=(0.12, 0.02, z + 0.08)))
    p.append(cylinder(0.07, 0.2, CANVAS, loc=(0.12, 0.02, z + 0.11), rot=(90, 0, 0), sides=10, cap=True))    # covered wagon
    for side in (-1, 1):
        p += wheel(0.12 + 0.07 * side, -0.05, z + 0.045, 0.045)
        p += wheel(0.12 + 0.07 * side, 0.09, z + 0.045, 0.045)
    p.append(box((0.12, 0.004, 0.03), TEAM, loc=(0.12, -0.08, z + 0.16)))
    return p


def worker():
    p = hex_plinth()
    p += group(figure("pick", "straw"), offset=(-0.04, 0, 0.035))
    p.append(box((0.08, 0.06, 0.04), WOOD, loc=(0.14, 0.06, 0.055), bevel=0.005))                              # crate
    p.append(sphere(0.03, ROCK, loc=(0.14, -0.08, 0.06), subdiv=1, jitter=0.2, seed=3))
    return p


def scout():
    p = hex_plinth()
    p += group(figure("staff", "hood", body=a.hexcol(0x5A6E4A)), offset=(0, 0, 0.035))
    p.append(box((0.05, 0.004, 0.04), TEAM, loc=(0.0, -0.058, 0.3)))                                            # sash
    return p


UNITS = {
    "sword": lambda: squad("sword", "steel"),
    "spear": lambda: squad("spear", "crest"),
    "bow": lambda: squad("bow", "cap"),
    "crossbow": lambda: squad("crossbow", "steel"),
    "musket": lambda: squad("musket", "tricorn"),
    "helmet": lambda: squad("rifle", "modern", body=a.hexcol(0x6B7A4E), legs=a.hexcol(0x4E5A38)),
    "horse": cavalry,
    "catapult": catapult,
    "cannon": cannon,
    "rocket": rocket_truck,
    "ram": ram,
    "siegetower": siege_tower,
    "tank": tank,
    "sailship": sail_ship,
    "steamship": lambda: steamship(False),
    "carrier": lambda: steamship(True),
    "fighter": lambda: plane("fighter"),
    "bomber": lambda: plane("bomber"),
    "jet": lambda: plane("jet"),
    "scout": scout,
    "settler": settler,
    "worker": worker,
    "general": lambda: robed(GOLD, "general"),
    "scientist": lambda: robed(a.hexcol(0x6BB8FF), "scientist"),
    "engineer": lambda: robed(a.hexcol(0xED9152), "engineer"),
    "merchant": lambda: robed(GOLD, "merchant"),
    "artist": lambda: robed(a.hexcol(0xCC85F5), "artist"),
    "prophet": lambda: robed(a.hexcol(0xF2EDB8), "prophet"),
}

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
    for cat, table in (("unit", UNITS), ("prop", PROPS), ("city", CITY)):
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
