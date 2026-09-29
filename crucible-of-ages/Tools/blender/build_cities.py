"""
City parts for every age (used by build_assets.py).

The game assembles a city from four parts: a centrepiece (keep), houses ringed around it, and on
fortified cities a wall segment (1.0 long along X, scaled to each hex side) with a tower at every
corner. Each age has its own set, named city_<style>_<part>, and houses come in variants
(city_<style>_house, city_<style>_house_2): the owner's age picks the style (CityModels.StyleFor),
so settlements grow from thatched huts to mud brick, stone, brick and smokestacks, concrete and
finally glass towers. The medieval set is the plain city_house / city_keep / city_wall / city_tower
in build_assets.py.

Parts face -Y (doors toward the keep) and stand on z = 0. TEAM paint marks the owner.
"""
import math

import crucible_art as a
from crucible_art import (IRON, STONE, STONE_DARK, STRAW, TEAM, TEAM_DARK, WOOD, WOOD_DARK, box, cylinder, poly,
                          segment, sphere)

DAUB = a.hexcol(0xA88A60)
HIDE = a.hexcol(0xB89A72)
MUDBRICK = a.hexcol(0xC9A77A)
MUDBRICK_DARK = a.hexcol(0xA88760)
BRICK = a.hexcol(0x9A4A38)
BRICK_DARK = a.hexcol(0x7A3A2C)
SLATE = a.hexcol(0x4E545C)
WINDOW = a.hexcol(0x2E3A48)
LIT = a.hexcol(0xF2D98A)
CONCRETE = a.hexcol(0xC8C4BA)
CONCRETE_DARK = a.hexcol(0x9A968E)
WHITE = a.hexcol(0xEEF0F2)
BLUE_GLASS = a.hexcol(0x5E8CB0)
GLOW = a.hexcol(0x5FE3F0)
GREEN = a.hexcol(0x5E9E3A)
FIRE = a.hexcol(0xF08A2A)


def gable(w, d, z, rise, color, overhang=1.12, y=0.0, ridge_x=False):
    """A gabled roof over a w x d footprint (centred on (0, y)) starting at height z, ridge along Y
    (or along X with ridge_x)."""
    hx, hy = w / 2 * overhang, d / 2 * overhang
    if ridge_x:
        verts = [(-hx, y - hy, z), (hx, y - hy, z), (hx, y + hy, z), (-hx, y + hy, z), (-hx, y, z + rise), (hx, y, z + rise)]
        faces = [(0, 1, 5, 4), (3, 4, 5, 2), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]
    else:
        verts = [(-hx, y - hy, z), (hx, y - hy, z), (hx, y + hy, z), (-hx, y + hy, z), (0, y - hy, z + rise), (0, y + hy, z + rise)]
        faces = [(0, 1, 4), (3, 5, 2), (0, 4, 5, 3), (1, 2, 5, 4), (0, 3, 2, 1)]
    return poly(verts, faces, color, bevel=0.003)


def windows(w, d, z0, z1, rows, cols, color=WINDOW, faces=(-1, 1), side_faces=(-1, 1), size=(0.018, 0.022)):
    """Window panes on the front/back (faces, +-Y) and the sides (side_faces, +-X) of a w x d block."""
    p = []
    for r in range(rows):
        z = z0 + (z1 - z0) * (r + 0.5) / rows
        for c in range(cols):
            t = (c + 0.5) / cols - 0.5
            for f in faces:
                p.append(box((size[0], 0.004, size[1]), color, loc=(t * w * 0.9, f * (d / 2 + 0.001), z)))
            for f in side_faces:
                p.append(box((0.004, size[0], size[1]), color, loc=(f * (w / 2 + 0.001), t * d * 0.9, z)))
    return p

# ---------------------------------------------------------------------------------------- Neolithic

def neo_hut():
    p = [cylinder(0.068, 0.06, DAUB, loc=(0, 0, 0.03), sides=10)]
    p.append(cylinder(0.09, 0.11, STRAW, loc=(0, 0, 0.11), sides=10, top=0.008))
    p.append(cylinder(0.02, 0.012, a.hexcol(0xB08E48), loc=(0, 0, 0.17), sides=6))
    p.append(box((0.03, 0.01, 0.045), WOOD_DARK, loc=(0, -0.066, 0.023)))
    return p


def neo_tent():
    p = [cylinder(0.075, 0.14, HIDE, loc=(0, 0, 0.07), sides=8, top=0.006)]
    for k in range(4):
        ang = math.radians(k * 90 + 20)
        p.append(segment((math.cos(ang) * 0.02, math.sin(ang) * 0.02, 0.12), (math.cos(ang) * 0.04, math.sin(ang) * 0.04, 0.19), 0.003, 0.002, WOOD, sides=4))
    p.append(box((0.08, 0.01, 0.014), TEAM, loc=(0, -0.05, 0.07), rot=(-28, 0, 0)))                    # painted band
    p.append(poly([(-0.02, -0.074, 0.0), (0.02, -0.074, 0.0), (0.0, -0.05, 0.07)], [(0, 1, 2)], WOOD_DARK))
    return p


def neo_keep():
    """A longhouse with a painted totem pole and a fire pit."""
    p = [box((0.16, 0.3, 0.07), DAUB, loc=(0, 0.02, 0.035), bevel=0.004)]
    p.append(gable(0.16, 0.3, 0.07, 0.12, STRAW, y=0.02))
    p.append(box((0.04, 0.01, 0.055), WOOD_DARK, loc=(0, -0.132, 0.028)))
    for side in (-1, 1):
        p.append(segment((0, 0.02 + 0.17 * side, 0.18), (0, 0.02 + 0.2 * side, 0.22), 0.004, 0.003, WOOD_DARK, sides=4))  # gable horns
    x, y = 0.13, -0.14
    p.append(cylinder(0.018, 0.34, WOOD, loc=(x, y, 0.17), sides=8))                                     # totem
    for k, col in enumerate((TEAM, a.hexcol(0x2E2A26), TEAM_DARK, a.hexcol(0xE3B94F))):
        p.append(cylinder(0.021, 0.035, col, loc=(x, y, 0.08 + k * 0.07), sides=8))
    p.append(box((0.1, 0.012, 0.02), TEAM, loc=(x, y, 0.33)))                                             # wings
    for k in range(7):
        ang = math.radians(k * 360 / 7)
        p.append(sphere(0.012, STONE_DARK, loc=(-0.13 + math.cos(ang) * 0.035, -0.14 + math.sin(ang) * 0.035, 0.008), subdiv=0))
    p.append(cylinder(0.022, 0.04, FIRE, loc=(-0.13, -0.14, 0.02), sides=5, top=0.0))
    return p


def neo_wall():
    """A palisade of sharpened logs."""
    p = [box((1.0, 0.03, 0.03), WOOD_DARK, loc=(0, 0.02, 0.07))]
    n = 17
    for k in range(n):
        x = -0.48 + k * 0.96 / (n - 1)
        h = 0.15 + (0.012 if k % 2 else 0)
        p.append(cylinder(0.022, h, WOOD, loc=(x, 0, h / 2), sides=6))
        p.append(cylinder(0.022, 0.035, WOOD, loc=(x, 0, h + 0.0175), sides=6, top=0.0))
    return p


def neo_tower():
    """A watch platform on poles."""
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(segment((0.045 * sx, 0.045 * sy, 0), (0.035 * sx, 0.035 * sy, 0.2), 0.007, 0.006, WOOD, sides=5))
    p.append(box((0.1, 0.1, 0.014), WOOD_DARK, loc=(0, 0, 0.2)))
    p.append(cylinder(0.075, 0.06, TEAM, loc=(0, 0, 0.26), sides=8, top=0.0))
    return p

# ---------------------------------------------------------------------------------------- Bronze and Iron Age

def mud_house():
    p = [box((0.13, 0.13, 0.1), MUDBRICK, loc=(0, 0, 0.05), bevel=0.003)]
    p.append(box((0.136, 0.136, 0.012), MUDBRICK_DARK, loc=(0, 0, 0.104)))                                # parapet
    p.append(box((0.03, 0.006, 0.05), WOOD_DARK, loc=(0, -0.066, 0.025)))
    p.append(box((0.02, 0.005, 0.014), WINDOW, loc=(0.035, -0.066, 0.075)))
    p.append(poly([(-0.05, 0.0, 0.14), (0.05, 0.0, 0.14), (0.05, 0.05, 0.11), (-0.05, 0.05, 0.11)], [(0, 1, 2, 3)], TEAM))  # awning
    for x in (-0.05, 0.05):
        p.append(segment((x, 0.0, 0.11), (x, 0.0, 0.14), 0.002, 0.002, WOOD, sides=3))
    for k in range(3):
        p.append(segment((-0.066, -0.03 + k * 0.03, 0.09), (-0.08, -0.03 + k * 0.03, 0.09), 0.003, 0.003, WOOD, sides=3))  # beam ends
    return p


def mud_house_2():
    p = [box((0.15, 0.12, 0.08), MUDBRICK, loc=(0, 0, 0.04), bevel=0.003)]
    p.append(box((0.08, 0.08, 0.07), MUDBRICK, loc=(0.03, 0.015, 0.115), bevel=0.003))                    # upper storey
    p.append(box((0.03, 0.006, 0.05), WOOD_DARK, loc=(-0.03, -0.061, 0.025)))
    p.append(box((0.018, 0.005, 0.014), WINDOW, loc=(0.03, -0.026, 0.12)))
    p.append(segment((-0.03, 0.02, 0.08), (-0.01, 0.03, 0.16), 0.002, 0.002, WOOD, sides=3))              # ladder
    p.append(segment((-0.045, 0.02, 0.08), (-0.025, 0.03, 0.16), 0.002, 0.002, WOOD, sides=3))
    p.append(cylinder(0.018, 0.035, a.hexcol(0xB4553A), loc=(-0.05, -0.03, 0.098), sides=8, top=0.012))   # storage jar
    p.append(box((0.084, 0.084, 0.006), TEAM, loc=(0.03, 0.015, 0.152)))
    return p


def ziggurat():
    p = []
    for k, (s, h) in enumerate(((0.3, 0.07), (0.22, 0.07), (0.14, 0.06))):
        z = sum(hh for (_, hh) in ((0.3, 0.07), (0.22, 0.07), (0.14, 0.06))[:k])
        p.append(box((s, s, h), MUDBRICK if k % 2 == 0 else MUDBRICK_DARK, loc=(0, 0, z + h / 2), taper=(0.94, 0.94), bevel=0.003))
    p.append(poly([(-0.03, -0.26, 0.0), (0.03, -0.26, 0.0), (0.03, -0.07, 0.2), (-0.03, -0.07, 0.2),
                   (-0.03, -0.15, 0.0), (0.03, -0.15, 0.0)],
                  [(0, 1, 2, 3), (1, 5, 2), (4, 0, 3), (5, 4, 3, 2), (4, 5, 1, 0)], a.hexcol(0xD8BC8C)))  # stair
    p.append(box((0.09, 0.09, 0.06), a.hexcol(0xE8DDC4), loc=(0, 0, 0.23), bevel=0.003))                  # shrine
    p.append(box((0.096, 0.096, 0.014), TEAM, loc=(0, 0, 0.265)))
    p.append(box((0.03, 0.005, 0.04), WOOD_DARK, loc=(0, -0.046, 0.22)))
    for side in (-1, 1):
        p.append(segment((0.1 * side, -0.1, 0.14), (0.1 * side, -0.1, 0.3), 0.003, 0.003, WOOD, sides=4))
        p.append(box((0.003, 0.04, 0.05), TEAM, loc=(0.1 * side, -0.08, 0.27)))
    return p


def mud_wall():
    p = [box((1.0, 0.06, 0.14), MUDBRICK, loc=(0, 0, 0.07), bevel=0.004)]
    for k in range(10):
        p.append(box((0.05, 0.062, 0.03), MUDBRICK_DARK, loc=(-0.45 + k * 0.1, 0, 0.155), bevel=0.008))   # rounded merlons
    for x in (-0.25, 0.25):
        p.append(box((0.05, 0.09, 0.14), MUDBRICK_DARK, loc=(x, 0, 0.07), taper=(0.8, 0.8)))              # buttresses
    return p


def mud_tower():
    return [box((0.12, 0.12, 0.24), MUDBRICK, loc=(0, 0, 0.12), taper=(0.85, 0.85), bevel=0.004),
            box((0.12, 0.12, 0.02), MUDBRICK_DARK, loc=(0, 0, 0.245)),
            cylinder(0.075, 0.05, TEAM, loc=(0, 0, 0.28), sides=4, top=0.0, rot=(0, 0, 45))]

# ---------------------------------------------------------------------------------------- Industrial

def rowhouse():
    w, d, h = 0.11, 0.15, 0.16
    p = [box((w, d, h), BRICK, loc=(0, 0, h / 2), bevel=0.002)]
    p.append(gable(w, d, h, 0.05, SLATE, overhang=1.05))
    p += windows(w, d, 0.03, h - 0.01, 2, 2, color=WINDOW, faces=(-1,), side_faces=())
    p.append(box((0.028, 0.006, 0.05), TEAM_DARK, loc=(0, -d / 2 - 0.002, 0.025)))                       # front door
    for y in (-0.05, 0.05):
        p.append(box((0.03, 0.025, 0.05), BRICK_DARK, loc=(0.035, y, h + 0.04)))                          # chimneys
    return p


def factory():
    w, d = 0.16, 0.14
    p = [box((w, d, 0.08), BRICK, loc=(0, 0, 0.04), bevel=0.002)]
    for k in range(3):                                                                                     # sawtooth roof
        y0 = -d / 2 + k * d / 3
        p.append(poly([(-w / 2, y0, 0.08), (w / 2, y0, 0.08), (w / 2, y0 + d / 3, 0.08), (-w / 2, y0 + d / 3, 0.08),
                       (-w / 2, y0, 0.12), (w / 2, y0, 0.12)],
                      [(0, 1, 5, 4), (4, 5, 2, 3), (0, 4, 3), (1, 2, 5), (3, 2, 1, 0)], SLATE))
        p.append(box((w * 0.9, 0.004, 0.03), a.hexcol(0x9FC4D8), loc=(0, y0 + 0.002, 0.1)))
    p.append(cylinder(0.022, 0.26, BRICK_DARK, loc=(0.05, 0.04, 0.13), sides=8, top=0.016))                # smokestack
    p.append(cylinder(0.018, 0.016, TEAM, loc=(0.05, 0.04, 0.24), sides=8))
    p += windows(w, d, 0.02, 0.07, 1, 4, color=LIT, faces=(-1,), side_faces=(), size=(0.022, 0.03))
    return p


def town_hall():
    p = [box((0.26, 0.16, 0.13), a.hexcol(0xC8BCA4), loc=(0, 0.02, 0.065), bevel=0.003)]
    p.append(gable(0.26, 0.16, 0.13, 0.05, SLATE, overhang=1.03, y=0.02, ridge_x=True))
    p += windows(0.26, 0.16, 0.03, 0.12, 2, 5, color=WINDOW, faces=(-1,), side_faces=())
    for k in range(4):
        p.append(cylinder(0.008, 0.1, WHITE, loc=(-0.045 + k * 0.03, -0.075, 0.05), sides=6))             # portico
    p.append(poly([(-0.06, -0.08, 0.1), (0.06, -0.08, 0.1), (0.0, -0.08, 0.14)], [(0, 1, 2)], WHITE))
    p.append(box((0.07, 0.07, 0.34), a.hexcol(0xB8AC94), loc=(0, 0.04, 0.17), bevel=0.003))              # clock tower
    for f in (-1, 1):
        p.append(cylinder(0.022, 0.004, WHITE, loc=(0, 0.04 + f * 0.036, 0.29), rot=(90, 0, 0), sides=12))
        p.append(cylinder(0.022, 0.004, WHITE, loc=(f * 0.036, 0.04, 0.29), rot=(0, 90, 0), sides=12))
    p.append(cylinder(0.052, 0.09, SLATE, loc=(0, 0.04, 0.385), sides=4, top=0.0, rot=(0, 0, 45)))
    p.append(segment((0, 0.04, 0.43), (0, 0.04, 0.52), 0.003, 0.002, IRON, sides=4))
    p.append(box((0.004, 0.05, 0.03), TEAM, loc=(0, 0.065, 0.5)))
    return p


def rampart():
    """A low brick-faced earthwork with a stone coping and gun embrasures."""
    p = [poly([(-0.5, -0.06, 0), (0.5, -0.06, 0), (0.5, 0.05, 0), (-0.5, 0.05, 0),
               (-0.5, -0.02, 0.13), (0.5, -0.02, 0.13), (0.5, 0.04, 0.13), (-0.5, 0.04, 0.13)],
              [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7), (3, 2, 1, 0)], BRICK)]
    p.append(box((1.0, 0.07, 0.014), STONE, loc=(0, 0.01, 0.137)))
    for x in (-0.3, 0.0, 0.3):
        p.append(box((0.04, 0.074, 0.02), a.hexcol(0x2E2A26), loc=(x, 0.01, 0.14)))
    return p


def bastion():
    return [cylinder(0.08, 0.16, BRICK, loc=(0, 0, 0.08), sides=5, top=0.07, bevel=0.003),
            cylinder(0.075, 0.016, STONE, loc=(0, 0, 0.168), sides=5),
            segment((0, 0, 0.17), (0, 0, 0.3), 0.003, 0.003, IRON, sides=4),
            box((0.004, 0.06, 0.04), TEAM, loc=(0, 0.03, 0.28))]

# ---------------------------------------------------------------------------------------- Modern

def apartment():
    w, d, h = 0.12, 0.12, 0.28
    p = [box((w, d, h), CONCRETE, loc=(0, 0, h / 2), bevel=0.002)]
    for k in range(6):                                                                                     # window bands
        z = 0.035 + k * 0.042
        p.append(box((w + 0.003, d * 0.86, 0.016), WINDOW, loc=(0, 0, z)))
        p.append(box((w * 0.86, d + 0.003, 0.016), WINDOW, loc=(0, 0, z)))
    p.append(box((0.04, 0.04, 0.03), CONCRETE_DARK, loc=(0.02, 0.02, h + 0.015)))                          # lift housing
    p.append(cylinder(0.014, 0.03, WOOD_DARK, loc=(-0.03, -0.02, h + 0.015), sides=8))                     # water tank
    p.append(box((w + 0.004, d + 0.004, 0.012), TEAM, loc=(0, 0, h - 0.006)))
    return p


def apartment_2():
    w, d, h = 0.15, 0.1, 0.18
    p = [box((w, d, h), a.hexcol(0xB08060), loc=(0, 0, h / 2), bevel=0.002)]
    p += windows(w, d, 0.02, h - 0.01, 4, 4, color=WINDOW, faces=(-1, 1), side_faces=(), size=(0.02, 0.018))
    p.append(box((w * 0.9, 0.03, 0.006), TEAM, loc=(0, -d / 2 - 0.012, 0.045)))                            # shop awning
    p.append(box((w * 0.9, 0.004, 0.028), LIT, loc=(0, -d / 2 - 0.001, 0.017)))                            # shopfront
    p.append(box((w, d, 0.01), CONCRETE_DARK, loc=(0, 0, h + 0.005)))
    return p


def office_tower():
    p = []
    z = 0.0
    for (s, h) in ((0.2, 0.22), (0.15, 0.2), (0.11, 0.16)):                                               # setbacks
        p.append(box((s, s, h), a.hexcol(0xB8B4AA), loc=(0, 0, z + h / 2), bevel=0.002))
        for k in range(int(h / 0.035)):
            zz = z + 0.02 + k * 0.035
            p.append(box((s + 0.003, s * 0.88, 0.014), BLUE_GLASS, loc=(0, 0, zz)))
            p.append(box((s * 0.88, s + 0.003, 0.014), BLUE_GLASS, loc=(0, 0, zz)))
        z += h
    p.append(box((0.116, 0.116, 0.024), TEAM, loc=(0, 0, z - 0.008)))
    p.append(box((0.06, 0.06, 0.03), CONCRETE_DARK, loc=(0, 0, z + 0.015)))                               # plant room
    p.append(segment((0, 0, z), (0, 0, z + 0.14), 0.006, 0.002, IRON, sides=4))                            # antenna
    return p


def bunker_wall():
    p = [poly([(-0.5, -0.05, 0), (0.5, -0.05, 0), (0.5, 0.04, 0), (-0.5, 0.04, 0),
               (-0.5, -0.02, 0.11), (0.5, -0.02, 0.11), (0.5, 0.04, 0.11), (-0.5, 0.04, 0.11)],
              [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7), (3, 2, 1, 0)], CONCRETE_DARK)]
    for k in range(9):                                                                                     # wire posts
        x = -0.44 + k * 0.11
        p.append(segment((x, 0.01, 0.11), (x, 0.01, 0.16), 0.003, 0.003, IRON, sides=3))
    p.append(segment((-0.48, 0.01, 0.15), (0.48, 0.01, 0.15), 0.0015, 0.0015, IRON, sides=3))
    p.append(segment((-0.48, 0.01, 0.135), (0.48, 0.01, 0.135), 0.0015, 0.0015, IRON, sides=3))
    for x in (-0.25, 0.25):
        p.append(box((0.06, 0.006, 0.02), TEAM, loc=(x, -0.036, 0.06), rot=(-17, 0, 0)))
    return p


def pillbox():
    return [cylinder(0.08, 0.12, CONCRETE, loc=(0, 0, 0.06), sides=8, top=0.07, bevel=0.004),
            box((0.07, 0.01, 0.014), a.hexcol(0x2E2A26), loc=(0, -0.074, 0.085)),
            cylinder(0.07, 0.03, TEAM, loc=(0, 0, 0.135), sides=8, top=0.05)]

# ---------------------------------------------------------------------------------------- Information and Future

def glass_tower():
    h = 0.34
    p = [box((0.12, 0.12, h), BLUE_GLASS, loc=(0, 0, h / 2), taper=(0.75, 0.75))]
    for side in (-1, 1):                                                                                   # white fins
        p.append(box((0.012, 0.125, h), WHITE, loc=(0.052 * side, 0, h / 2), taper=(1.0, 0.75)))
    p.append(box((0.092, 0.092, 0.012), GREEN, loc=(0, 0, h + 0.006)))                                     # roof garden
    p.append(sphere(0.02, a.hexcol(0x4E8A36), loc=(0.02, 0.01, h + 0.02), subdiv=1))
    p.append(box((0.123, 0.123, 0.014), TEAM, loc=(0, 0, 0.02)))
    return p


def glass_tower_2():
    p = [cylinder(0.065, 0.28, a.hexcol(0x7FA8C8), loc=(0, 0, 0.14), sides=12, top=0.05)]
    for k in range(5):                                                                                     # balcony rings
        z = 0.05 + k * 0.05
        r = 0.065 - (0.015 * z / 0.28)
        p.append(cylinder(r + 0.01, 0.006, WHITE, loc=(0, 0, z), sides=12))
    p.append(cylinder(0.052, 0.01, TEAM, loc=(0, 0, 0.285), sides=12))
    p.append(sphere(0.03, GREEN, loc=(0, 0, 0.29), subdiv=1, scale=(1, 1, 0.5)))
    return p


def spire():
    p = [cylinder(0.18, 0.06, WHITE, loc=(0, 0, 0.03), sides=6, top=0.15, bevel=0.004)]                   # podium
    p.append(cylinder(0.1, 0.56, BLUE_GLASS, loc=(0, 0, 0.34), sides=6, top=0.03))
    for k in range(3):                                                                                     # ribs
        ang = k * 120
        r = math.radians(ang)
        p.append(segment((math.cos(r) * 0.1, math.sin(r) * 0.1, 0.06), (math.cos(r) * 0.03, math.sin(r) * 0.03, 0.62), 0.008, 0.004, WHITE, sides=4))
    p.append(cylinder(0.09, 0.01, GLOW, loc=(0, 0, 0.2), sides=12))                                        # sky rings
    p.append(cylinder(0.07, 0.012, TEAM, loc=(0, 0, 0.42), sides=12))
    p.append(segment((0, 0, 0.62), (0, 0, 0.78), 0.006, 0.001, WHITE, sides=4))
    p.append(sphere(0.012, GLOW, loc=(0, 0, 0.7), subdiv=1))
    return p


def energy_wall():
    p = [box((1.0, 0.05, 0.05), WHITE, loc=(0, 0, 0.025), bevel=0.006)]
    p.append(box((0.96, 0.008, 0.08), GLOW, loc=(0, 0, 0.09)))                                            # barrier field
    p.append(box((0.96, 0.052, 0.008), TEAM, loc=(0, 0, 0.05)))
    return p


def pylon():
    return [cylinder(0.05, 0.3, WHITE, loc=(0, 0, 0.15), sides=6, top=0.02, bevel=0.003),
            sphere(0.03, TEAM, loc=(0, 0, 0.31), subdiv=1),
            cylinder(0.04, 0.006, GLOW, loc=(0, 0, 0.18), sides=12)]


STYLES = {
    "neolithic": {"house": neo_hut, "house_2": neo_tent, "keep": neo_keep, "wall": neo_wall, "tower": neo_tower},
    "ancient": {"house": mud_house, "house_2": mud_house_2, "keep": ziggurat, "wall": mud_wall, "tower": mud_tower},
    "industrial": {"house": rowhouse, "house_2": factory, "keep": town_hall, "wall": rampart, "tower": bastion},
    "modern": {"house": apartment, "house_2": apartment_2, "keep": office_tower, "wall": bunker_wall, "tower": pillbox},
    "future": {"house": glass_tower, "house_2": glass_tower_2, "keep": spire, "wall": energy_wall, "tower": pylon},
}


def all_parts():
    """{"<style>_<part>": builder} for build_assets' city table."""
    return {f"{style}_{part}": make for style, parts in STYLES.items() for part, make in parts.items()}

