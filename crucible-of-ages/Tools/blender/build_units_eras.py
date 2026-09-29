"""
Era-specific unit models (used by build_assets.py alongside build_units.py).

build_units.py draws one model per unit *symbol*; this module adds models for individual units so
each age looks its own: clubmen and slingers in the Neolithic, galleys with oars, men-at-arms and
knights in caparisons, gatling guns and landships, helicopters, stealth bombers and a giant death
robot at the end of the tree. They are exported as unit_<unit id> and the game prefers them over
the symbol model (UnitModels.BuildFor).

Also adds the symbol models for the late-game symbols (unit_helicopter, unit_robot,
unit_submarine), which double as the models of those units.
"""
import math

from mathutils import Vector

import crucible_art as a
from crucible_art import (CANVAS, GLASS, IRON, OLIVE, PLINTH, TEAM, TEAM_DARK,
                          WOOD, WOOD_DARK, box, cylinder, group, poly, segment, sphere, torus_arc)
from build_units import (BLACK, BRONZE, LINE3, SQUAD3, SQUAD4, WHITE, cavalry, formation, hull_mesh, sail,
                         soldier, spoked_wheel)

GUNMETAL = a.hexcol(0x3E4247)
GREY = a.hexcol(0x7A8590)
LIGHT_GREY = a.hexcol(0xA3ACB5)
DESERT = a.hexcol(0xB29C6E)
GLOW = a.hexcol(0x5FE3F0)
SANDBAG = a.hexcol(0xA8956A)
RUBBER = a.hexcol(0x2A2A2A)

# ---------------------------------------------------------------------------------------- helpers

def prism(profile, x0, x1, color, bevel=0.004):
    """A solid from a side profile [(y, z)...] (convex, bow toward -Y) extruded across x0..x1."""
    n = len(profile)
    verts = [(x0, y, z) for (y, z) in profile] + [(x1, y, z) for (y, z) in profile]
    faces = [tuple(range(n)), tuple(range(n, 2 * n))]
    faces += [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    return poly(verts, faces, color, bevel=bevel)


def slab(outline, z0, z1, color, bevel=0.0):
    """A solid from a top-view outline [(x, y)...] extruded from z0 up to z1."""
    n = len(outline)
    verts = [(x, y, z0) for (x, y) in outline] + [(x, y, z1) for (x, y) in outline]
    faces = [tuple(range(n)), tuple(range(n, 2 * n))]
    faces += [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    return poly(verts, faces, color, bevel=bevel)


def mirrored(half):
    """A symmetric outline from its right half [(x, y)...] listed nose to tail (x >= 0)."""
    return half + [(-x, y) for (x, y) in reversed(half) if x > 0]


def flight_stand(z):
    return [segment((0, 0, 0), (0, 0, z - 0.02), 0.005, 0.004, IRON, sides=6),
            cylinder(0.05, 0.012, PLINTH, loc=(0, 0, 0.006), sides=10)]


def tracks(x, length, height=0.06, width=0.04, wheels=5, z=0.036):
    p = [box((width, length, height), RUBBER, loc=(x, 0, z), bevel=min(0.018, height * 0.3), segments=2)]
    for k in range(wheels):
        y = -length * 0.36 + k * length * 0.72 / max(1, wheels - 1)
        p.append(cylinder(height * 0.4, width * 1.1, GUNMETAL, loc=(x, y, z - 0.004), rot=(0, 90, 0), sides=10))
    return p


def tyre(x, y, r, width=0.022):
    return [cylinder(r, width, RUBBER, loc=(x, y, r), rot=(0, 90, 0), sides=12),
            cylinder(r * 0.5, width * 1.08, GUNMETAL, loc=(x, y, r), rot=(0, 90, 0), sides=8)]


def crew(kit, spots, seed):
    p = []
    for i, (x, y, turn) in enumerate(spots):
        p += group(soldier(kit, seed + i), offset=(x, y, 0), rot_z=turn)
    return p

# ---------------------------------------------------------------------------------------- engines

def trebuchet():
    p = []
    pivot = Vector((0, 0.02, 0.3))
    for side in (-1, 1):
        x = 0.07 * side
        p.append(box((0.02, 0.36, 0.022), WOOD, loc=(x, 0.0, 0.03), bevel=0.003))                          # sole beams
        p.append(segment((x, -0.12, 0.04), (x * 0.8, pivot.y, pivot.z), 0.009, 0.008, WOOD_DARK))          # A-frame
        p.append(segment((x, 0.15, 0.04), (x * 0.8, pivot.y, pivot.z), 0.009, 0.008, WOOD_DARK))
        p.append(segment((x, -0.04, 0.04), (x * 0.8, -0.05, 0.18), 0.006, 0.006, WOOD_DARK, sides=5))    # brace
    for y in (-0.15, 0.0, 0.15):
        p.append(box((0.16, 0.02, 0.018), WOOD, loc=(0, y, 0.045)))
    p.append(segment((-0.07, pivot.y, pivot.z), (0.07, pivot.y, pivot.z), 0.008, 0.008, IRON, sides=6))  # axle
    tip = Vector((0, 0.3, 0.07))
    short = pivot + (pivot - tip).normalized() * 0.1
    p.append(segment(short, tip, 0.012, 0.006, WOOD))                                                    # throwing arm, cocked
    p.append(box((0.1, 0.07, 0.08), WOOD_DARK, loc=(0, short.y - 0.005, short.z - 0.07), bevel=0.006))  # counterweight
    p.append(box((0.104, 0.074, 0.02), TEAM, loc=(0, short.y - 0.005, short.z - 0.05)))
    p.append(segment(short, (0, short.y - 0.005, short.z - 0.03), 0.004, 0.004, IRON, sides=4))
    p.append(segment(tip, (0, 0.2, 0.03), 0.0015, 0.0015, CANVAS, sides=3))                              # sling
    p.append(sphere(0.02, a.hexcol(0x8A8278), loc=(0, 0.19, 0.03), subdiv=1, jitter=0.15, seed=9))
    p += crew("worker", [(-0.17, -0.12, 20), (0.18, 0.02, -35)], 41)
    return p


def artillery():
    """A First World War field gun: steel wheels, shield, long barrel and box trail."""
    p = []
    for side in (-1, 1):
        p += spoked_wheel(0.075 * side, 0.0, 0.075, 0.075, color=GUNMETAL, spokes=12)
    p.append(cylinder(0.006, 0.17, IRON, loc=(0, 0, 0.075), rot=(0, 90, 0), sides=6))
    p.append(box((0.13, 0.008, 0.1), OLIVE, loc=(0, -0.03, 0.14), rot=(-8, 0, 0), bevel=0.003))         # shield
    p.append(box((0.132, 0.01, 0.012), TEAM, loc=(0, -0.036, 0.185), rot=(-8, 0, 0)))
    p.append(segment((0, 0.07, 0.12), (0, -0.3, 0.16), 0.012, 0.01, a.hexcol(0x4A5230), sides=10))        # barrel
    p.append(segment((0, 0.05, 0.1), (0, -0.14, 0.12), 0.012, 0.012, OLIVE, sides=8))                    # recuperator
    p.append(box((0.04, 0.26, 0.03), OLIVE, loc=(0, 0.17, 0.05), rot=(14, 0, 0), bevel=0.004))           # trail
    p.append(box((0.07, 0.02, 0.012), IRON, loc=(0, 0.29, 0.012)))                                       # spade
    for k in range(3):
        p.append(cylinder(0.01, 0.05, a.hexcol(0xC9A24A), loc=(0.12 + k * 0.022, 0.12, 0.025), sides=6))  # shells
    p += crew("infantry", [(-0.16, 0.13, 20), (0.17, 0.1, -25)], 43)
    return p


def gatling_gun():
    p = []
    for side in (-1, 1):
        p += spoked_wheel(0.065 * side, 0.02, 0.06, 0.06, spokes=10)
        p.append(box((0.012, 0.12, 0.04), WOOD, loc=(0.04 * side, 0.04, 0.08), rot=(10, 0, 0), bevel=0.003))
    p.append(cylinder(0.006, 0.14, IRON, loc=(0, 0.02, 0.06), rot=(0, 90, 0), sides=6))
    p.append(box((0.035, 0.16, 0.022), WOOD, loc=(0, 0.14, 0.04), rot=(12, 0, 0), bevel=0.004))          # trail
    front, back = Vector((0, -0.17, 0.11)), Vector((0, 0.04, 0.11))
    for k in range(6):
        ang = math.radians(k * 60)
        off = Vector((math.cos(ang) * 0.011, 0, math.sin(ang) * 0.011))
        p.append(segment(back + off, front + off, 0.0035, 0.0035, GUNMETAL, sides=5))
    for t in (0.0, 0.55, 1.0):
        c = back.lerp(front, t)
        p.append(cylinder(0.017, 0.012, BRONZE, loc=c, rot=(90, 0, 0), sides=10))
    p.append(box((0.04, 0.05, 0.04), BRONZE, loc=(0, 0.06, 0.12), bevel=0.004))                           # breech
    p.append(box((0.022, 0.02, 0.05), BRONZE, loc=(0, 0.02, 0.155)))                                      # magazine
    p.append(segment((0.026, 0.07, 0.12), (0.045, 0.08, 0.14), 0.003, 0.003, IRON, sides=4))             # crank
    p.append(box((0.03, 0.03, 0.004), TEAM, loc=(0.041, 0.04, 0.1)))
    p += crew("rifleman", [(-0.15, 0.12, 20), (0.16, 0.12, -25)], 45)
    return p


def sandbags(radius, arc_deg, rows=2, seed=0):
    p = []
    count = max(3, int(radius * math.radians(arc_deg) / 0.05))
    for r in range(rows):
        for k in range(count):
            t = (k + (0.5 if r % 2 else 0)) / count
            ang = math.radians(-90 - arc_deg / 2 + t * arc_deg)
            p.append(sphere(0.03, SANDBAG if (k + r) % 3 else a.hexcol(0x968456),
                            loc=(math.cos(ang) * radius, math.sin(ang) * radius, 0.015 + r * 0.024),
                            rot=(0, 0, math.degrees(ang) + 90), subdiv=1, scale=(1.0, 0.62, 0.45)))
    return p


def machine_gun():
    """A Vickers team behind a sandbag emplacement."""
    p = sandbags(0.16, 150, rows=3)
    base = Vector((0, -0.06, 0.1))
    for ang in (0, 125, 235):
        r = math.radians(ang - 90)
        p.append(segment(base, (math.cos(r) * 0.05, -0.06 + math.sin(r) * 0.05, 0.0), 0.004, 0.003, GUNMETAL, sides=4))
    p.append(box((0.03, 0.07, 0.032), GUNMETAL, loc=(0, -0.07, 0.12), bevel=0.004))                      # receiver
    p.append(cylinder(0.014, 0.1, OLIVE, loc=(0, -0.15, 0.125), rot=(90, 0, 0), sides=10))              # water jacket
    p.append(segment((0, -0.2, 0.125), (0, -0.23, 0.125), 0.004, 0.006, BLACK, sides=6))
    p.append(box((0.04, 0.03, 0.03), OLIVE, loc=(0.04, -0.05, 0.1)))                                     # ammo box
    p.append(box((0.03, 0.005, 0.02), TEAM, loc=(0, -0.21, 0.07)))
    p += crew("infantry", [(-0.07, 0.07, 0), (0.08, 0.09, -10)], 47)
    return p


def anti_tank_gun():
    p = []
    for side in (-1, 1):
        p += tyre(0.07 * side, -0.01, 0.045)
        p.append(segment((0.02 * side, 0.0, 0.05), (0.13 * side, 0.2, 0.01), 0.008, 0.008, OLIVE, sides=5))  # split trail
    p.append(box((0.13, 0.008, 0.09), OLIVE, loc=(0, -0.05, 0.1), rot=(-12, 0, 0), bevel=0.003))          # shield
    for side in (-1, 1):
        p.append(box((0.045, 0.007, 0.08), OLIVE, loc=(0.085 * side, -0.035, 0.095), rot=(-12, 0, 35 * side)))
    p.append(box((0.132, 0.01, 0.012), TEAM, loc=(0, -0.058, 0.135), rot=(-12, 0, 0)))
    p.append(segment((0, 0.03, 0.1), (0, -0.33, 0.11), 0.01, 0.007, a.hexcol(0x4A5230), sides=8))         # long barrel
    p.append(cylinder(0.012, 0.03, BLACK, loc=(0, -0.33, 0.11), rot=(90, 0, 0), sides=8))                  # muzzle brake
    p += crew("rifle", [(-0.17, 0.08, 20), (0.2, 0.05, -25)], 49)
    return p

# ---------------------------------------------------------------------------------------- armour

def landship():
    """A rhomboid First World War tank with sponson guns and tracks running all the way round."""
    p = []
    track = [(-0.2, 0.09), (-0.13, 0.2), (0.13, 0.19), (0.2, 0.07), (0.15, 0.0), (-0.13, 0.0)]
    hull = [(-0.17, 0.09), (-0.11, 0.18), (0.11, 0.17), (0.17, 0.07), (0.13, 0.02), (-0.11, 0.02)]
    for side in (-1, 1):
        p.append(prism(track, 0.06 * side, 0.1 * side, a.hexcol(0x3A3A36)))
        p.append(prism([(y * 0.92, z * 0.92 + 0.008) for (y, z) in track], 0.058 * side, 0.102 * side, a.hexcol(0x6E6A58)))
        p.append(box((0.03, 0.08, 0.06), a.hexcol(0x6E6A58), loc=(0.115 * side, 0.0, 0.1), taper=(0.7, 0.8), bevel=0.004))  # sponson
        p.append(segment((0.12 * side, -0.02, 0.1), (0.13 * side, -0.12, 0.1), 0.007, 0.006, BLACK, sides=6))
        p.append(box((0.004, 0.1, 0.03), TEAM, loc=(0.103 * side, 0.0, 0.14)))
    p.append(prism(hull, -0.06, 0.06, a.hexcol(0x7A7560)))
    p.append(box((0.06, 0.07, 0.03), a.hexcol(0x6E6A58), loc=(0, -0.05, 0.195), bevel=0.004))              # cab
    for side in (-1, 1):
        p.append(segment((0.03 * side, 0.18, 0.06), (0.03 * side, 0.27, 0.03), 0.005, 0.005, IRON, sides=5))
        p += tyre(0.03 * side, 0.27, 0.03, 0.014)                                                         # steering tail
    return p


def modern_armor():
    p = []
    hull_c = DESERT
    profile = [(-0.2, 0.065), (-0.15, 0.115), (0.19, 0.12), (0.2, 0.07), (0.17, 0.03), (-0.17, 0.03)]
    p.append(prism(profile, -0.085, 0.085, hull_c, bevel=0.003))
    for side in (-1, 1):
        p += tracks(0.1 * side, 0.36, height=0.056, width=0.036, wheels=6, z=0.032)
        p.append(box((0.036, 0.37, 0.028), hull_c, loc=(0.1 * side, 0.0, 0.074), bevel=0.003))            # side skirts
    turret = mirrored([(0.0, -0.11), (0.05, -0.1), (0.085, -0.02), (0.085, 0.1), (0.06, 0.12)])
    p.append(slab(turret, 0.12, 0.172, hull_c, bevel=0.003))
    p.append(slab([(x * 0.9, y * 0.95 + 0.004) for (x, y) in turret], 0.172, 0.176, a.hexcol(0x9E8A5E)))
    p.append(segment((0, -0.1, 0.148), (0, -0.36, 0.15), 0.01, 0.008, a.hexcol(0x8E7C58), sides=8))        # smoothbore
    p.append(cylinder(0.014, 0.05, a.hexcol(0x8E7C58), loc=(0, -0.2, 0.149), rot=(90, 0, 0), sides=8))      # fume extractor
    p.append(box((0.03, 0.03, 0.02), GUNMETAL, loc=(0.05, 0.02, 0.186)))                                    # sight
    p.append(box((0.172, 0.004, 0.02), TEAM, loc=(0, 0.119, 0.15)))
    p.append(box((0.004, 0.14, 0.02), TEAM, loc=(0.086, 0.02, 0.146)))
    p.append(box((0.004, 0.14, 0.02), TEAM, loc=(-0.086, 0.02, 0.146)))
    return p


def mech_infantry():
    """A tracked infantry fighting vehicle with a small autocannon turret and troop door."""
    p = []
    body_c = a.hexcol(0x6E7A4E)
    profile = [(-0.18, 0.05), (-0.11, 0.14), (0.17, 0.14), (0.17, 0.03), (-0.15, 0.03)]
    p.append(prism(profile, -0.08, 0.08, body_c))
    for side in (-1, 1):
        p += tracks(0.093 * side, 0.33, height=0.054, width=0.032, wheels=5, z=0.03)
        p.append(box((0.004, 0.24, 0.028), TEAM, loc=(0.081 * side, 0.02, 0.1)))
    p.append(slab(mirrored([(0.0, -0.06), (0.04, -0.05), (0.05, 0.02), (0.035, 0.05)]), 0.14, 0.18, body_c, bevel=0.003))
    p.append(segment((0, -0.05, 0.16), (0, -0.22, 0.165), 0.005, 0.004, BLACK, sides=6))                   # autocannon
    p.append(box((0.02, 0.04, 0.03), GUNMETAL, loc=(-0.045, 0.02, 0.18)))                                  # missile box
    p.append(box((0.07, 0.004, 0.08), a.hexcol(0x5E6A40), loc=(0, 0.172, 0.085)))                          # rear door
    return p


def giant_death_robot():
    """Civ V's Giant Death Robot: a walking tank on reverse-jointed legs with arm cannons."""
    p = []
    dark = a.hexcol(0x3A3F48)
    mid = a.hexcol(0x5A6270)
    for side in (-1, 1):
        x = 0.07 * side
        hip, knee, ankle = Vector((x, 0.02, 0.32)), Vector((x * 1.15, -0.07, 0.18)), Vector((x * 1.1, 0.03, 0.045))
        p.append(sphere(0.035, dark, loc=hip, subdiv=1))
        p.append(segment(hip, knee, 0.03, 0.024, mid, sides=8))
        p.append(sphere(0.026, TEAM, loc=knee, subdiv=1))
        p.append(segment(knee, ankle, 0.022, 0.018, mid, sides=8))
        p.append(box((0.07, 0.12, 0.03), dark, loc=(x * 1.1, -0.01, 0.018), taper=(0.8, 0.8), bevel=0.006))   # foot
        p.append(box((0.03, 0.05, 0.02), dark, loc=(x * 1.1, 0.07, 0.014)))                                 # heel spur
    torso = box((0.2, 0.15, 0.13), mid, loc=(0, 0.0, 0.41), taper=(1.2, 1.1), bevel=0.014)
    p.append(torso)
    p.append(box((0.12, 0.012, 0.06), TEAM, loc=(0, -0.082, 0.42), bevel=0.004))                              # chest plate
    p.append(box((0.06, 0.06, 0.05), dark, loc=(0, -0.05, 0.495), bevel=0.008))                                # head
    p.append(box((0.05, 0.004, 0.012), GLOW, loc=(0, -0.081, 0.5)))                                            # visor
    p.append(box((0.08, 0.06, 0.08), dark, loc=(0, 0.1, 0.43), bevel=0.01))                                    # reactor
    p.append(sphere(0.012, GLOW, loc=(0, 0.132, 0.43), subdiv=1))
    for side in (-1, 1):
        sh = Vector((0.13 * side, 0.0, 0.45))
        p.append(sphere(0.04, TEAM_DARK, loc=sh, subdiv=1, scale=(1, 1, 0.85)))
        p.append(segment(sh, (0.15 * side, -0.02, 0.36), 0.02, 0.018, mid, sides=8))
        p.append(box((0.05, 0.14, 0.05), dark, loc=(0.15 * side, -0.07, 0.34), bevel=0.008))                  # cannon arm
        for dx in (-0.012, 0.012):
            p.append(segment((0.15 * side + dx, -0.13, 0.34), (0.15 * side + dx, -0.22, 0.34), 0.008, 0.008, GUNMETAL, sides=6))
    p.append(segment((0.05, 0.1, 0.47), (0.05, 0.12, 0.58), 0.003, 0.002, BLACK, sides=4))                    # antenna
    return p

# ---------------------------------------------------------------------------------------- aircraft

def helicopter():
    z = 0.3
    body_c = a.hexcol(0x5A6440)
    p = [sphere(0.05, body_c, loc=(0, -0.02, z), subdiv=2, scale=(0.8, 1.7, 0.9))]
    p.append(sphere(0.03, GLASS, loc=(0, -0.08, z + 0.02), subdiv=1, scale=(0.9, 1.6, 0.9)))                  # canopy
    p.append(segment((0, 0.05, z + 0.01), (0, 0.3, z + 0.03), 0.02, 0.008, body_c, sides=8))                  # tail boom
    p.append(poly([(0, 0.26, z + 0.03), (0, 0.31, z + 0.03), (0, 0.32, z + 0.1), (0, 0.29, z + 0.1)], [(0, 1, 2, 3)], body_c))
    p.append(box((0.004, 0.04, 0.024), TEAM, loc=(0.002, 0.3, z + 0.07)))
    p.append(cylinder(0.035, 0.004, a.hexcol(0x3A3A3A), loc=(0.01, 0.305, z + 0.06), rot=(0, 90, 0), sides=3))  # tail rotor
    p.append(cylinder(0.018, 0.03, GUNMETAL, loc=(0, 0.0, z + 0.055), sides=8))                                # mast
    for k in range(4):
        ang = 45 + k * 90
        r = math.radians(ang)
        p.append(box((0.22, 0.018, 0.003), a.hexcol(0x3A3A3A), loc=(math.cos(r) * 0.11, math.sin(r) * 0.11, z + 0.072), rot=(0, 0, ang)))
    for side in (-1, 1):
        p.append(box((0.07, 0.03, 0.006), body_c, loc=(0.06 * side, 0.0, z - 0.01)))                          # stub wing
        p.append(cylinder(0.012, 0.06, GUNMETAL, loc=(0.085 * side, -0.01, z - 0.022), rot=(90, 0, 0), sides=8))  # rocket pod
        p.append(segment((0.03 * side, -0.05, z - 0.045), (0.035 * side, -0.05, z - 0.07), 0.003, 0.003, IRON, sides=4))
        p.append(segment((0.03 * side, 0.04, z - 0.045), (0.035 * side, 0.04, z - 0.07), 0.003, 0.003, IRON, sides=4))
        p.append(segment((0.036 * side, -0.1, z - 0.066), (0.036 * side, 0.08, z - 0.07), 0.004, 0.004, IRON, sides=4))  # skids
        p.append(cylinder(0.014, 0.003, TEAM, loc=(0.041 * side, 0.0, z), rot=(0, 90, 0), sides=10))             # roundel
    p.append(segment((0, -0.1, z - 0.03), (0, -0.14, z - 0.03), 0.005, 0.003, BLACK, sides=5))                  # chin gun
    p += flight_stand(z - 0.06)
    return p


def stealth_bomber():
    z = 0.32
    wing = mirrored([(0.0, -0.17), (0.34, 0.07), (0.31, 0.1), (0.2, 0.025), (0.11, 0.1), (0.0, 0.04)])
    p = [slab(wing, z - 0.004, z + 0.004, a.hexcol(0x3A3E46))]
    p.append(sphere(0.06, a.hexcol(0x444852), loc=(0, -0.04, z + 0.002), subdiv=2, scale=(1.3, 1.9, 0.35)))       # centre hump
    p.append(box((0.04, 0.012, 0.006), a.hexcol(0x1E2A34), loc=(0, -0.12, z + 0.016), rot=(-20, 0, 0)))        # cockpit
    for side in (-1, 1):
        p.append(box((0.03, 0.03, 0.008), a.hexcol(0x2E3238), loc=(0.07 * side, 0.02, z + 0.017)))             # intakes
        p.append(cylinder(0.018, 0.002, TEAM, loc=(0.24 * side, 0.04, z + 0.005), sides=10))                    # roundel
    p += flight_stand(z)
    return p

# ---------------------------------------------------------------------------------------- ships

def galley(banks=1, catapult=False):
    """A trireme (banks=1) or quadrireme: oars out, bronze ram, one square sail with a team stripe."""
    length = 0.6 if banks == 1 else 0.68
    p = hull_mesh(length, 0.13 + banks * 0.02, 0.06, a.hexcol(0x5A3E26), a.hexcol(0xB08A5E), sheer=0.03)
    p.append(box((0.12 + banks * 0.02, length * 0.7, 0.012), TEAM, loc=(0, 0.02, 0.05)))                       # painted strake
    p.append(segment((0, -length / 2 + 0.02, 0.02), (0, -length / 2 - 0.06, 0.02), 0.014, 0.006, BRONZE, sides=6))  # ram
    for side in (-1, 1):
        p.append(sphere(0.012, WHITE, loc=(0.04 * side, -length / 2 + 0.05, 0.07), subdiv=1, scale=(0.4, 1, 1)))   # painted eye
        p.append(sphere(0.006, BLACK, loc=(0.045 * side, -length / 2 + 0.05, 0.07), subdiv=0))
        for bank in range(banks):
            for k in range(7):
                y = -length * 0.28 + k * length * 0.085
                out = 0.2 + bank * 0.03
                p.append(segment((0.07 * side, y, 0.055 - bank * 0.018), (out * side, y + 0.03, -0.01), 0.003, 0.003, WOOD, sides=4))
    p.append(torus_arc(0.05, 0.008, -30, 150, a.hexcol(0x5A3E26), loc=(0, length / 2 - 0.02, 0.1), rot=(0, 0, 90), steps=8))  # stern post
    p.append(segment((0, 0.0, 0.06), (0, 0.0, 0.38), 0.006, 0.004, WOOD_DARK, sides=6))                        # mast
    p += sail(-0.01, 0.14, 0.36, 0.13, belly=0.02)
    p.append(box((0.002, 0.04, 0.02), TEAM, loc=(0, 0.02, 0.38)))
    if catapult:
        p.append(box((0.08, 0.1, 0.02), WOOD, loc=(0, -0.18, 0.07)))
        p.append(segment((0, -0.15, 0.08), (0, -0.23, 0.17), 0.005, 0.004, WOOD_DARK, sides=5))
        p.append(sphere(0.015, a.hexcol(0x8A8278), loc=(0, -0.235, 0.175), subdiv=1))
        p.append(box((0.1, 0.08, 0.05), WOOD_DARK, loc=(0, 0.22, 0.1), bevel=0.004))                            # stern castle
    return p


def ironclad():
    p = hull_mesh(0.6, 0.17, 0.05, a.hexcol(0x3A3E44), a.hexcol(0x5A5E64), sheer=0.01, transom=0.7)
    p.append(box((0.12, 0.26, 0.06), a.hexcol(0x4A4E55), loc=(0, 0.02, 0.08), taper=(0.6, 0.85), bevel=0.004))  # casemate
    for side in (-1, 1):
        for y in (-0.06, 0.02, 0.1):
            p.append(box((0.004, 0.016, 0.014), BLACK, loc=(0.047 * side, y, 0.08), rot=(0, -25 * side, 0)))
    p.append(cylinder(0.022, 0.1, BLACK, loc=(0, 0.03, 0.15), sides=10))
    p.append(cylinder(0.023, 0.02, TEAM, loc=(0, 0.03, 0.17), sides=10))
    p.append(segment((0, 0.2, 0.05), (0, 0.2, 0.22), 0.003, 0.003, BLACK, sides=4))
    p.append(box((0.003, 0.05, 0.03), TEAM, loc=(0, 0.225, 0.2)))
    return p


def side_stripes(beam, depth, length):
    """The owner's colour along both sides of a hull_mesh, just under the deck edge."""
    return [box((0.004, length, 0.012), TEAM_DARK, loc=(side * beam / 2 * 0.99, 0.0, depth - 0.01)) for side in (-1, 1)]


def gun_turret(y, z, facing=-1, barrels=2, scale=1.0, color=GREY):
    p = [cylinder(0.04 * scale, 0.025 * scale, color, loc=(0, y, z), sides=10, top=0.034 * scale, bevel=0.003)]
    p.append(box((0.06 * scale, 0.05 * scale, 0.028 * scale), color, loc=(0, y + 0.01 * facing * scale, z + 0.016 * scale), taper=(0.85, 0.8)))
    for k in range(barrels):
        x = (k - (barrels - 1) / 2) * 0.016 * scale
        p.append(segment((x, y + 0.02 * facing * scale, z + 0.016 * scale), (x, y + 0.14 * facing * scale, z + 0.02 * scale), 0.005 * scale, 0.004 * scale, a.hexcol(0x5A6068), sides=6))
    return p


def battleship():
    p = hull_mesh(0.84, 0.18, 0.075, GREY, a.hexcol(0x9A8C74), sheer=0.02, transom=0.75)
    p += side_stripes(0.18, 0.075, 0.4)
    p += gun_turret(-0.24, 0.08, -1, 3, 1.1)
    p += gun_turret(-0.14, 0.11, -1, 3, 1.1)
    p.append(cylinder(0.03, 0.03, GREY, loc=(0, -0.14, 0.095), sides=10))                                        # barbette
    p += gun_turret(0.26, 0.08, 1, 3, 1.1)
    p.append(box((0.1, 0.18, 0.05), GREY, loc=(0, 0.03, 0.105), bevel=0.005))
    p.append(box((0.07, 0.07, 0.1), GREY, loc=(0, -0.04, 0.17), taper=(0.7, 0.8), bevel=0.004))                 # tower
    p.append(box((0.06, 0.004, 0.012), a.hexcol(0x9FC4D8), loc=(0, -0.07, 0.18)))
    p.append(box((0.05, 0.05, 0.03), GREY, loc=(0, -0.03, 0.24)))                                                # director
    p.append(cylinder(0.025, 0.1, a.hexcol(0x5A6068), loc=(0, 0.08, 0.17), sides=10, top=0.022, rot=(-8, 0, 0)))
    p.append(cylinder(0.026, 0.02, TEAM, loc=(0, 0.084, 0.21), sides=10, rot=(-8, 0, 0)))
    for side in (-1, 1):
        for y in (-0.03, 0.05, 0.13):
            p.append(segment((0.05 * side, y, 0.1), (0.075 * side, y - 0.03, 0.1), 0.004, 0.004, a.hexcol(0x5A6068), sides=5))  # secondaries
    p.append(segment((0, 0.0, 0.26), (0, 0.0, 0.35), 0.003, 0.002, BLACK, sides=4))
    p.append(box((0.002, 0.04, 0.025), TEAM, loc=(0, 0.02, 0.34)))
    return p


def missile_cruiser():
    p = hull_mesh(0.8, 0.16, 0.07, LIGHT_GREY, a.hexcol(0x70787F), sheer=0.025, transom=0.8)
    p += side_stripes(0.16, 0.07, 0.38)
    p += gun_turret(-0.28, 0.075, -1, 1, 0.8, LIGHT_GREY)
    for gy in (-0.2, 0.2):                                                                                       # vertical launch cells
        for i in range(3):
            for j in range(2):
                p.append(box((0.02, 0.02, 0.004), a.hexcol(0x4A4E55), loc=(-0.024 + i * 0.024, gy + j * 0.024, 0.079)))
    p.append(box((0.11, 0.2, 0.07), LIGHT_GREY, loc=(0, -0.03, 0.11), taper=(0.85, 0.9), bevel=0.004))          # deckhouse
    for side in (-1, 1):
        p.append(box((0.004, 0.05, 0.05), a.hexcol(0x8A949C), loc=(0.05 * side, -0.08, 0.13), rot=(0, 20 * side, 0)))  # radar panels
    p.append(box((0.03, 0.05, 0.03), TEAM, loc=(0, 0.06, 0.16), bevel=0.004))                                    # funnel band
    p.append(box((0.09, 0.1, 0.05), LIGHT_GREY, loc=(0, 0.13, 0.1), bevel=0.004))
    p.append(segment((0, -0.03, 0.14), (0, -0.03, 0.3), 0.005, 0.003, a.hexcol(0x5A6068), sides=4))               # mast
    p.append(box((0.06, 0.004, 0.004), a.hexcol(0x5A6068), loc=(0, -0.03, 0.25)))
    p.append(sphere(0.018, WHITE, loc=(0, 0.13, 0.14), subdiv=1))                                                  # radome
    return p


def submarine(nuclear=False):
    length = 0.62 if nuclear else 0.54
    hull_c = a.hexcol(0x22252A) if nuclear else a.hexcol(0x4E565E)
    p = [sphere(0.05, hull_c, loc=(0, 0, 0.028), subdiv=2, scale=(1.0, length / 0.1, 0.9))]
    p.append(box((0.07, length * 0.6, 0.006), a.hexcol(0x3A3E44) if not nuclear else a.hexcol(0x2E3238), loc=(0, 0.0, 0.07)))  # casing
    sail_y = -0.1 if nuclear else -0.04
    p.append(box((0.035, 0.09, 0.08), hull_c, loc=(0, sail_y, 0.11), taper=(0.9, 0.7), bevel=0.006))            # sail
    p.append(box((0.037, 0.03, 0.02), TEAM, loc=(0, sail_y - 0.01, 0.13)))
    p.append(segment((0, sail_y + 0.02, 0.15), (0, sail_y + 0.02, 0.19), 0.003, 0.002, BLACK, sides=4))          # periscope
    if nuclear:
        p.append(box((0.12, 0.03, 0.004), hull_c, loc=(0, sail_y, 0.12)))                                          # fairwater planes
        for i in range(2):
            for j in range(4):
                p.append(cylinder(0.009, 0.004, a.hexcol(0x3A3E44), loc=(-0.012 + i * 0.024, 0.02 + j * 0.03, 0.074), sides=8))  # missile hatches
        p.append(box((0.004, 0.05, 0.08), hull_c, loc=(0, length / 2 - 0.02, 0.04)))                               # rudder
        p.append(box((0.1, 0.04, 0.004), hull_c, loc=(0, length / 2 - 0.02, 0.03)))
    else:
        p += gun_turret(-0.15, 0.07, -1, 1, 0.55, a.hexcol(0x4E565E))                                              # deck gun
        p.append(segment((0, -0.24, 0.07), (0, 0.24, 0.07), 0.0012, 0.0012, BLACK, sides=3))                       # net cutter wire
        p.append(box((0.08, 0.03, 0.004), hull_c, loc=(0, length / 2 - 0.02, 0.028)))
    return p

# ---------------------------------------------------------------------------------------- table

UNITS = {
    # Symbols introduced with the ages.
    "helicopter": helicopter,
    "robot": giant_death_robot,
    "submarine": lambda: submarine(False),
    # Foot troops, one look per age.
    "warrior": lambda: formation("club", SQUAD4, 51),
    "militia": lambda: formation("militia", SQUAD3, 52),
    "slinger": lambda: formation("sling", LINE3, 53),
    "composite_bowman": lambda: formation("compbow", LINE3, 54),
    "aurel_legionary": lambda: formation("legionary", SQUAD4, 55),
    "pikeman": lambda: formation("pike", SQUAD4, 56),
    "longswordsman": lambda: formation("longsword", SQUAD4, 57),
    "rifleman": lambda: formation("rifleman", SQUAD4, 58),
    "infantry": lambda: formation("infantry", SQUAD3, 59),
    "exosuit_infantry": lambda: formation("exosuit", SQUAD3, 60),
    # Riders.
    "horseman": lambda: cavalry([(-0.09, 0.05, 0), (0.09, -0.06, 0)], "javelin", "topknot", seed=61),
    "sky_rider": lambda: cavalry([(-0.09, 0.05, 0), (0.09, -0.06, 0)], "bow", "fur_hat", seed=62,
                                 coat=a.hexcol(0xC8B89A), dark=a.hexcol(0x8A7A60)),
    "knight": lambda: cavalry([(-0.09, 0.05, 0), (0.09, -0.06, 0)], "lance", "greathelm", seed=63, armor="plate", caparison=True,
                              coat=a.hexcol(0xD8D2C4), dark=a.hexcol(0xA8A090)),
    "lancer": lambda: cavalry([(-0.09, 0.05, 0), (0.09, -0.06, 0)], "lance", "plumed", seed=64, armor="plate"),
    "cavalry": lambda: cavalry([(-0.09, 0.05, 0), (0.09, -0.06, 0)], "sabre", "kepi", seed=65,
                               coat=a.hexcol(0x3A2A22), dark=a.hexcol(0x22160F)),
    # Engines.
    "trebuchet": trebuchet,
    "artillery": artillery,
    "gatling_gun": gatling_gun,
    "machine_gun": machine_gun,
    "anti_tank_gun": anti_tank_gun,
    # Armour.
    "landship": landship,
    "mech_infantry": mech_infantry,
    "modern_armor": modern_armor,
    "giant_death_robot": giant_death_robot,
    # Aircraft.
    "stealth_bomber": stealth_bomber,
    # Ships.
    "trireme": lambda: galley(1),
    "quadrireme": lambda: galley(2, catapult=True),
    "ironclad": ironclad,
    "battleship": battleship,
    "missile_cruiser": missile_cruiser,
    "nuclear_submarine": lambda: submarine(True),
}

