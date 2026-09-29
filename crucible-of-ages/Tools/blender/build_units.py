"""
Unit models in the spirit of Civilization V (used by build_assets.py).

Civ V draws a unit as a small formation standing straight on the terrain: foot troops as a squad
of three or four properly proportioned soldiers, cavalry as a few riders, engines with their crew.
The owner's colour sits on tunics, shields, caparisons, sails and funnels (painted TEAM here and
recoloured in game); there is no base, since the unit flag floats above.

Figures are about 0.29 tall and face -Y. Arms are posed by aiming each hand at a point (a two-bone
arm with the elbow bent outward), so weapons are really held.
"""
import math
import random

from mathutils import Vector

import crucible_art as a
from crucible_art import (CANVAS, GOLD, HORSE, HORSE_DARK, IRON, LEATHER, NAVY, OLIVE, PLINTH,
                          SKIN, STEEL, STRAW, TEAM, TEAM_DARK, WOOD, WOOD_DARK, GLASS, box, cylinder, group,
                          poly, segment, sphere, torus_arc)

BRONZE = a.hexcol(0xB08A3E)
WHITE = a.hexcol(0xEDEAE0)
BLACK = a.hexcol(0x26262A)
FUR = a.hexcol(0x7A5A3A)
HAIR = a.hexcol(0x3A2A1E)
KHAKI = a.hexcol(0x8C7F5A)

# ---------------------------------------------------------------------------------------- people

# Joint heights (z) for a standing figure.
ANKLE, KNEE, HIP, WAIST, SHOULDER, NECK, HEAD = 0.015, 0.068, 0.122, 0.148, 0.222, 0.238, 0.262
SHOULDER_X = 0.034


def arm(side, hand, sleeve, skin=SKIN, bend=(0.02, 0.015, -0.01)):
    """Upper arm and forearm from the shoulder to `hand`, elbow pushed out and back."""
    s = Vector((SHOULDER_X * side, 0.0, SHOULDER - 0.004))
    h = Vector(hand)
    e = (s + h) / 2 + Vector((bend[0] * side, bend[1], bend[2]))
    return [segment(s, e, 0.0095, 0.0085, sleeve), segment(e, h, 0.0085, 0.007, sleeve),
            sphere(0.0085, skin, loc=h, subdiv=1)]


def body(tunic, legs, boots, stride=0.02, skirt=0.034, coat=False, skin=SKIN):
    """Legs mid-stride, a belted tunic in the given colour, neck and head."""
    p = []
    for side, fwd in ((-1, -stride), (1, stride * 0.6)):
        hip = Vector((0.018 * side, 0.0, HIP))
        knee = Vector((0.02 * side, fwd * 0.6, KNEE))
        ankle = Vector((0.02 * side, fwd, ANKLE + 0.006))
        p.append(segment(hip, knee, 0.0125, 0.0105, legs))
        p.append(segment(knee, ankle, 0.0105, 0.0085, legs))
        p.append(box((0.018, 0.034, 0.018), boots, loc=(0.02 * side, fwd - 0.006, 0.009), bevel=0.003))
    bottom = 0.075 if coat else 0.105
    skirt_obj = cylinder(skirt, WAIST - bottom, tunic, loc=(0, 0, (WAIST + bottom) / 2), sides=8, top=0.026)
    skirt_obj.scale = (1.0, 0.85, 1.0)
    torso = cylinder(0.025, SHOULDER - WAIST + 0.006, tunic, loc=(0, 0, (SHOULDER + WAIST) / 2), sides=8, top=0.032)
    torso.scale = (1.12, 0.72, 1.0)
    p += [skirt_obj, torso]
    p.append(cylinder(0.027, 0.008, LEATHER, loc=(0, 0, WAIST + 0.002), sides=8))                        # belt
    p.append(segment((0, 0, SHOULDER - 0.004), (0, 0, NECK + 0.004), 0.008, 0.007, skin))              # neck
    head = sphere(0.021, skin, loc=(0, -0.002, HEAD), subdiv=2, scale=(0.9, 0.95, 1.08))
    p.append(head)
    p.append(sphere(0.0045, skin, loc=(0, -0.022, HEAD - 0.002), subdiv=0))                              # nose
    return p


def helmet(kind):
    z = HEAD + 0.008
    if kind == "legion":
        return [sphere(0.024, STEEL, loc=(0, 0, z), subdiv=2, scale=(1, 1.05, 0.85)),
                box((0.006, 0.05, 0.022), TEAM, loc=(0, 0.004, z + 0.026), bevel=0.003),                 # crest
                box((0.05, 0.006, 0.014), STEEL, loc=(0, 0.022, z - 0.014))]                             # neck guard
    if kind == "hoplite":
        return [sphere(0.025, BRONZE, loc=(0, 0.001, z - 0.002), subdiv=2, scale=(1, 1.05, 1.0)),
                box((0.005, 0.055, 0.03), TEAM_DARK, loc=(0, 0.006, z + 0.03), bevel=0.003)]
    if kind == "kettle":
        return [sphere(0.023, STEEL, loc=(0, 0, z), subdiv=2, scale=(1, 1, 0.75)),
                cylinder(0.036, 0.004, STEEL, loc=(0, 0, z - 0.006), sides=10)]
    if kind == "cap":
        return [sphere(0.022, LEATHER, loc=(0, 0.002, z), subdiv=1, scale=(1, 1.05, 0.7))]
    if kind == "tricorn":
        return [cylinder(0.034, 0.016, BLACK, loc=(0, 0, z + 0.006), sides=3, top=0.024, rot=(0, 0, 90)),
                cylinder(0.02, 0.012, BLACK, loc=(0, 0, z + 0.014), sides=8)]
    if kind == "kepi":
        return [cylinder(0.02, 0.022, NAVY, loc=(0, 0.002, z + 0.008), sides=8, top=0.018, rot=(-8, 0, 0)),
                box((0.03, 0.02, 0.003), BLACK, loc=(0, -0.018, z - 0.002))]
    if kind == "modern":
        return [sphere(0.026, OLIVE, loc=(0, 0.002, z), subdiv=2, scale=(1.05, 1.1, 0.72)),
                cylinder(0.034, 0.004, OLIVE, loc=(0, 0.002, z - 0.008), sides=10)]
    if kind == "hood":
        return [sphere(0.025, a.hexcol(0x3E5A3A), loc=(0, 0.004, z - 0.002), subdiv=2, scale=(1, 1.1, 1.05))]
    if kind == "straw":
        return [cylinder(0.042, 0.018, STRAW, loc=(0, 0, z + 0.004), sides=10, top=0.012)]
    if kind == "hair":
        return [sphere(0.022, HAIR, loc=(0, 0.004, z - 0.002), subdiv=1, scale=(1, 1.05, 0.8))]
    return []


def round_shield(center, color=TEAM, r=0.042):
    c = Vector(center)
    return [cylinder(r, 0.007, color, loc=c, rot=(90, 0, 0), sides=12, bevel=0.002),
            cylinder(r * 1.04, 0.004, BRONZE, loc=c + Vector((0, 0.003, 0)), rot=(90, 0, 0), sides=12),
            sphere(0.009, BRONZE, loc=c + Vector((0, -0.005, 0)), subdiv=1)]


def scutum(center, color=TEAM):
    c = Vector(center)
    return [box((0.055, 0.01, 0.09), color, loc=c, bevel=0.004),
            box((0.058, 0.006, 0.006), GOLD, loc=c + Vector((0, -0.004, 0.043))),
            box((0.058, 0.006, 0.006), GOLD, loc=c + Vector((0, -0.004, -0.043))),
            sphere(0.01, GOLD, loc=c + Vector((0, -0.008, 0)), subdiv=1)]


def soldier(kit, seed=0):
    """One figure. kit: sword, spear, pike, bow, crossbow, musket, rifle, scout, worker, settler."""
    rnd = random.Random(seed)
    stride = rnd.uniform(0.012, 0.024)
    p = []
    if kit == "sword":
        p += body(TEAM, a.hexcol(0x6A4A34), LEATHER, stride)
        p += helmet("legion")
        p.append(box((0.058, 0.04, 0.05), STEEL, loc=(0, 0, 0.198), bevel=0.006))                       # lorica
        p += arm(1, (0.042, -0.05, 0.17), TEAM)
        p.append(segment((0.042, -0.052, 0.168), (0.044, -0.1, 0.225), 0.0055, 0.0015, STEEL, sides=4))  # gladius
        p.append(box((0.022, 0.006, 0.005), GOLD, loc=(0.042, -0.054, 0.172)))
        p += arm(-1, (-0.034, -0.045, 0.18), TEAM)
        p += scutum((-0.036, -0.058, 0.17))
    elif kit in ("spear", "pike"):
        p += body(TEAM, a.hexcol(0x6A4A34), LEATHER, stride)
        p += helmet("hoplite" if kit == "spear" else "kettle")
        p += arm(1, (0.044, -0.03, 0.17), TEAM)
        top = (0.05, -0.1, 0.48) if kit == "spear" else (0.046, -0.3, 0.42)
        p.append(segment((0.042, 0.03, 0.02 if kit == "spear" else 0.1), top, 0.0045, 0.004, WOOD, sides=5))
        tip = Vector(top) + (Vector(top) - Vector((0.042, 0.03, 0.02))).normalized() * 0.03
        p.append(segment(top, tip, 0.008, 0.0, STEEL, sides=4))
        p += arm(-1, (-0.036, -0.04, 0.18), TEAM)
        p += round_shield((-0.04, -0.052, 0.18)) if kit == "spear" else [box((0.05, 0.008, 0.06), TEAM, loc=(-0.04, -0.05, 0.17), bevel=0.004)]
    elif kit == "bow":
        p += body(TEAM, a.hexcol(0x5A5A3A), LEATHER, stride)
        p += helmet("cap")
        bow_hand = Vector((-0.012, -0.11, 0.225))
        p += arm(-1, bow_hand, TEAM, bend=(0.005, -0.01, 0.0))
        p += arm(1, (0.022, -0.012, 0.228), TEAM, bend=(0.03, 0.02, 0.0))
        # Bow limbs curve forward from the grip in the left hand; the string runs to the drawing hand.
        p.append(torus_arc(0.075, 0.0038, -62, 62, WOOD_DARK, loc=bow_hand + Vector((0, 0.075, 0)), rot=(0, 0, -90), steps=10))
        p.append(segment(bow_hand + Vector((0, 0.04, 0.066)), (0.02, -0.012, 0.228), 0.0012, 0.0012, CANVAS, sides=3))
        p.append(segment(bow_hand + Vector((0, 0.04, -0.066)), (0.02, -0.012, 0.228), 0.0012, 0.0012, CANVAS, sides=3))
        p.append(segment((0.02, -0.01, 0.228), (-0.012, -0.14, 0.226), 0.002, 0.002, WOOD, sides=3))     # arrow
        p.append(segment((0.025, 0.03, 0.14), (0.045, 0.045, 0.25), 0.012, 0.011, LEATHER))             # quiver
        for k in range(3):
            p.append(segment((0.043 + k * 0.004, 0.044, 0.25), (0.047 + k * 0.004, 0.05, 0.28), 0.003, 0.003, WHITE, sides=3))
    elif kit == "crossbow":
        p += body(TEAM, a.hexcol(0x5A5A3A), LEATHER, stride)
        p += helmet("kettle")
        p.append(box((0.058, 0.038, 0.05), a.hexcol(0x8A8478), loc=(0, 0, 0.198), bevel=0.006))       # gambeson
        p += arm(1, (0.018, -0.05, 0.205), TEAM)
        p += arm(-1, (-0.012, -0.11, 0.21), TEAM)
        p.append(segment((0.018, 0.005, 0.205), (0.0, -0.14, 0.212), 0.007, 0.006, WOOD, sides=5))
        p.append(torus_arc(0.06, 0.004, 25, 155, IRON, loc=(0.0, -0.105, 0.208), rot=(90, 0, 0), steps=8))
    elif kit == "musket":
        p += body(TEAM, WHITE, BLACK, stride, skirt=0.036, coat=True)
        p += helmet("tricorn")
        for side in (-1, 1):  # white cross belts
            p.append(box((0.006, 0.004, 0.1), WHITE, loc=(0, -0.019, 0.19), rot=(0, 35 * side, 0)))
        p += arm(1, (0.042, -0.012, 0.13), TEAM)
        p += arm(-1, (-0.038, -0.01, 0.13), TEAM)
        p.append(segment((0.046, -0.005, 0.1), (0.04, 0.012, 0.36), 0.005, 0.004, WOOD_DARK, sides=5))
        p.append(segment((0.04, 0.012, 0.36), (0.04, 0.014, 0.41), 0.0018, 0.0, STEEL, sides=3))       # bayonet
    elif kit == "rifle":
        p += body(OLIVE, a.hexcol(0x5E6A40), BLACK, stride)
        p += helmet("modern")
        p.append(box((0.05, 0.03, 0.06), a.hexcol(0x4E5A34), loc=(0, 0.032, 0.2), bevel=0.006))         # pack
        p.append(box((0.03, 0.004, 0.022), TEAM, loc=(0.02, -0.02, 0.205)))                             # armband / patch
        p += arm(1, (0.03, -0.03, 0.2), OLIVE)
        p += arm(-1, (-0.006, -0.1, 0.214), OLIVE)
        p.append(segment((0.028, 0.01, 0.214), (0.004, -0.17, 0.222), 0.005, 0.0035, a.hexcol(0x3A3A3A), sides=5))
    elif kit == "scout":
        p += body(a.hexcol(0x6B7A4E), a.hexcol(0x5A4A34), LEATHER, stride)
        p += helmet("hood")
        cloak = cylinder(0.04, 0.16, a.hexcol(0x3E5A3A), loc=(0, 0.008, 0.16), sides=8, top=0.03, cap=False)
        cloak.scale = (1.1, 0.9, 1.0)
        p.append(cloak)
        p.append(box((0.05, 0.006, 0.014), TEAM, loc=(0, -0.02, 0.215), rot=(0, 20, 0)))               # sash
        p += arm(1, (0.042, -0.02, 0.2), a.hexcol(0x6B7A4E))
        p.append(segment((0.044, 0.0, 0.02), (0.046, -0.04, 0.38), 0.004, 0.004, WOOD, sides=5))
        p += arm(-1, (-0.038, -0.02, 0.14), a.hexcol(0x6B7A4E))
    elif kit == "worker":
        p += body(TEAM, a.hexcol(0x6A5A44), LEATHER, stride)
        p += helmet("straw")
        p += arm(1, (0.04, -0.03, 0.2), TEAM)
        p += arm(-1, (0.005, -0.035, 0.17), TEAM)
        p.append(segment((0.0, -0.035, 0.12), (0.05, -0.03, 0.29), 0.0045, 0.0045, WOOD, sides=5))
        p.append(segment((0.02, -0.03, 0.29), (0.09, -0.03, 0.27), 0.006, 0.002, IRON, sides=4))       # pick head
    elif kit == "settler":
        p += body(TEAM, a.hexcol(0x6A5A44), LEATHER, stride)
        p += helmet("hair")
        p += arm(1, (0.042, -0.035, 0.15), TEAM)
        p += arm(-1, (-0.038, -0.02, 0.14), TEAM)
        p.append(segment((0.046, -0.04, 0.02), (0.046, -0.05, 0.3), 0.0035, 0.0035, WOOD, sides=5))
        p.append(box((0.05, 0.04, 0.06), CANVAS, loc=(0, 0.035, 0.2), bevel=0.01))                      # pack
        p.append(segment((-0.028, 0.045, 0.24), (0.028, 0.045, 0.24), 0.012, 0.012, a.hexcol(0x9A7C55)))  # bedroll
    return p


def formation(kit, slots, seed=0):
    """Figures at (x, y, turn) slots, each slightly different."""
    parts = []
    for i, (x, y, t) in enumerate(slots):
        rnd = random.Random(seed * 31 + i)
        parts += group(soldier(kit, seed * 7 + i), offset=(x + rnd.uniform(-0.01, 0.01), y + rnd.uniform(-0.01, 0.01), 0),
                       rot_z=t + rnd.uniform(-6, 6))
    return parts


SQUAD4 = [(-0.075, -0.07, 0), (0.075, -0.07, 0), (-0.075, 0.09, 0), (0.075, 0.09, 0)]
SQUAD3 = [(0.0, -0.09, 0), (-0.11, 0.06, 4), (0.11, 0.06, -4)]
LINE3 = [(-0.12, 0.0, 0), (0.0, -0.03, 0), (0.12, 0.0, 0)]


def robe_figure(robe, trim, headwear=None):
    """A civilian in a floor-length robe (great people)."""
    p = []
    r = cylinder(0.042, 0.2, robe, loc=(0, 0, 0.1), sides=10, top=0.028, bevel=0.003)
    r.scale = (1.0, 0.85, 1.0)
    p.append(r)
    p.append(cylinder(0.043, 0.012, trim, loc=(0, 0, 0.012), sides=10))
    torso = cylinder(0.028, 0.05, robe, loc=(0, 0, 0.215), sides=8, top=0.033)
    torso.scale = (1.1, 0.75, 1.0)
    p.append(torso)
    p.append(segment((0, 0, SHOULDER - 0.004), (0, 0, NECK + 0.004), 0.008, 0.007, SKIN))
    p.append(sphere(0.021, SKIN, loc=(0, -0.002, HEAD), subdiv=2, scale=(0.9, 0.95, 1.08)))
    p.append(box((0.04, 0.012, 0.09), trim, loc=(0, -0.026, 0.19)))                                     # stole
    p += helmet(headwear or "hair")
    return p


def great_person(kind):
    color = {"scientist": (a.hexcol(0xE8DDC4), a.hexcol(0x6BB8FF)), "engineer": (a.hexcol(0x9A8A6A), a.hexcol(0xED9152)),
             "merchant": (a.hexcol(0x7A3A6A), GOLD), "artist": (a.hexcol(0xE8DDC4), a.hexcol(0xCC85F5)),
             "prophet": (WHITE, a.hexcol(0xF2EDB8))}[kind]
    p = robe_figure(color[0], TEAM)
    if kind == "scientist":
        p += arm(1, (0.03, -0.05, 0.19), color[0])
        p.append(sphere(0.016, GLASS, loc=(0.03, -0.062, 0.2), subdiv=2))
        p += arm(-1, (-0.03, -0.04, 0.17), color[0])
        p.append(box((0.04, 0.03, 0.006), a.hexcol(0x6A4A30), loc=(-0.03, -0.05, 0.17), rot=(30, 0, 0)))  # book
    elif kind == "engineer":
        p += arm(1, (0.03, -0.05, 0.19), color[0])
        p += arm(-1, (-0.03, -0.05, 0.19), color[0])
        p.append(box((0.08, 0.05, 0.003), CANVAS, loc=(0, -0.07, 0.195), rot=(35, 0, 0)))                # plans
        p.append(segment((0.07, 0.02, 0.02), (0.075, 0.02, 0.2), 0.004, 0.004, WOOD, sides=4))          # measuring rod
    elif kind == "merchant":
        p += arm(1, (0.035, -0.045, 0.17), color[0])
        p.append(box((0.03, 0.022, 0.02), GOLD, loc=(0.036, -0.056, 0.172), bevel=0.004))
        p += arm(-1, (-0.035, -0.02, 0.14), color[0])
        p.append(sphere(0.022, LEATHER, loc=(-0.04, -0.025, 0.12), subdiv=1, scale=(1, 1, 1.2)))           # purse
        p.append(cylinder(0.032, 0.02, color[0], loc=(0, 0, HEAD + 0.018), sides=8, top=0.028))             # hat
    elif kind == "artist":
        p += arm(1, (0.04, -0.05, 0.22), color[0])
        p.append(segment((0.04, -0.05, 0.22), (0.05, -0.08, 0.27), 0.002, 0.0015, WOOD, sides=3))
        p += arm(-1, (-0.035, -0.045, 0.18), color[0])
        p.append(cylinder(0.034, 0.004, a.hexcol(0xC9A874), loc=(-0.04, -0.06, 0.18), rot=(70, 0, 0), sides=10))
        for k, c in enumerate((0xD94A3A, 0x3A7AD9, 0xF2D24B)):
            p.append(sphere(0.006, a.hexcol(c), loc=(-0.05 + k * 0.01, -0.07, 0.19 - k * 0.004), subdiv=0))
    elif kind == "prophet":
        p += arm(1, (0.042, -0.03, 0.2), color[0])
        p.append(segment((0.046, -0.03, 0.02), (0.046, -0.035, 0.36), 0.004, 0.004, WOOD, sides=5))
        p.append(sphere(0.014, a.hexcol(0xFFB347), loc=(0.046, -0.035, 0.37), subdiv=1))
        p += arm(-1, (-0.04, -0.05, 0.24), color[0], bend=(0.02, 0.0, 0.02))                              # raised in blessing
    return p


def crowd(builder, slots):
    parts = []
    for (x, y, t) in slots:
        parts += group(builder(), offset=(x, y, 0), rot_z=t)
    return parts

# ---------------------------------------------------------------------------------------- horses

def horse(coat=HORSE, dark=HORSE_DARK, barding=TEAM, gait=0.0):
    """A horse about 0.25 at the shoulder, facing -Y, with a team-coloured saddle cloth."""
    p = []
    p.append(sphere(0.042, coat, loc=(0, 0.0, 0.15), subdiv=2, scale=(0.85, 2.0, 0.95)))                 # barrel
    p.append(sphere(0.04, coat, loc=(0, -0.062, 0.158), subdiv=2, scale=(0.9, 1.0, 1.0)))                # chest
    p.append(sphere(0.042, coat, loc=(0, 0.064, 0.16), subdiv=2, scale=(0.95, 1.0, 0.95)))               # rump
    p.append(segment((0, -0.075, 0.17), (0, -0.112, 0.245), 0.026, 0.017, coat, sides=8))               # neck
    p.append(segment((0, -0.112, 0.25), (0, -0.162, 0.205), 0.017, 0.011, coat, sides=7))               # head
    p.append(sphere(0.012, dark, loc=(0, -0.164, 0.202), subdiv=1))                                       # muzzle
    for side in (-1, 1):
        p.append(segment((0.008 * side, -0.105, 0.262), (0.011 * side, -0.1, 0.285), 0.005, 0.0, coat, sides=4))  # ears
    p.append(box((0.008, 0.06, 0.02), dark, loc=(0, -0.093, 0.225), rot=(-55, 0, 0)))                     # mane
    swing = gait
    legs = (((-0.022, -0.062), swing), ((0.022, -0.062), -swing), ((-0.022, 0.066), -swing * 0.7), ((0.022, 0.066), swing * 0.7))
    for (x, y), sw in legs:
        top = Vector((x, y, 0.135))
        knee = Vector((x, y + sw * 0.4 + (0.008 if y > 0 else -0.004), 0.07))
        hoof = Vector((x, y + sw, 0.012))
        p.append(segment(top, knee, 0.012, 0.008, coat, sides=6))
        p.append(segment(knee, hoof, 0.0075, 0.0065, coat, sides=6))
        p.append(cylinder(0.009, 0.012, BLACK, loc=hoof + Vector((0, 0, -0.006)), sides=6))
    p.append(segment((0, 0.1, 0.17), (0, 0.13, 0.085), 0.01, 0.004, dark, sides=5))                      # tail
    cloth = box((0.1, 0.075, 0.05), barding, loc=(0, 0.004, 0.17), bevel=0.01)
    p.append(cloth)
    p.append(box((0.03, 0.05, 0.012), LEATHER, loc=(0, 0.0, 0.195), bevel=0.004))                       # saddle
    return p


def rider(weapon="lance", tunic=TEAM, head="kettle"):
    """A seated rider on top of `horse()`."""
    p = []
    seat = 0.2
    for side in (-1, 1):
        p.append(segment((0.018 * side, 0.004, seat + 0.004), (0.036 * side, -0.03, seat - 0.035), 0.011, 0.009, a.hexcol(0x5A4A34)))
        p.append(segment((0.036 * side, -0.03, seat - 0.035), (0.036 * side, -0.018, seat - 0.085), 0.009, 0.008, a.hexcol(0x5A4A34)))
        p.append(box((0.016, 0.03, 0.016), LEATHER, loc=(0.036 * side, -0.024, seat - 0.09)))
    torso = cylinder(0.025, 0.075, tunic, loc=(0, 0.004, seat + 0.04), sides=8, top=0.031)
    torso.scale = (1.12, 0.72, 1.0)
    p.append(torso)
    top = seat + 0.078
    p.append(segment((0, 0.004, top), (0, 0.002, top + 0.015), 0.008, 0.007, SKIN))
    p.append(sphere(0.021, SKIN, loc=(0, 0.0, top + 0.037), subdiv=2, scale=(0.9, 0.95, 1.08)))
    hz = top + 0.045
    if head == "kettle":
        p += [sphere(0.023, STEEL, loc=(0, 0, hz), subdiv=2, scale=(1, 1, 0.75)), cylinder(0.034, 0.004, STEEL, loc=(0, 0, hz - 0.006), sides=10)]
    elif head == "crown":
        p += [cylinder(0.022, 0.018, GOLD, loc=(0, 0, hz + 0.004), sides=8, top=0.025)]
    else:
        p += [sphere(0.022, LEATHER, loc=(0, 0.002, hz), subdiv=1, scale=(1, 1.05, 0.7))]
    sh = Vector((0.034, 0.004, top - 0.005))
    if weapon == "lance":
        hand = Vector((0.04, -0.03, seat + 0.03))
        p += [segment(sh, (0.05, -0.01, seat + 0.03), 0.009, 0.008, tunic), sphere(0.008, SKIN, loc=hand, subdiv=1)]
        p.append(segment((0.042, 0.12, seat + 0.0), (0.05, -0.34, seat + 0.1), 0.0045, 0.0035, WOOD, sides=5))
        p.append(segment((0.05, -0.34, seat + 0.1), (0.051, -0.37, seat + 0.107), 0.007, 0.0, STEEL, sides=4))
        p.append(box((0.003, 0.03, 0.02), TEAM, loc=(0.05, -0.3, seat + 0.1)))                             # pennon
    elif weapon == "banner":
        p += [segment(sh, (0.045, -0.02, seat + 0.06), 0.009, 0.008, tunic)]
        p.append(segment((0.045, -0.02, seat - 0.02), (0.045, -0.02, seat + 0.3), 0.004, 0.004, WOOD, sides=5))
        p.append(box((0.003, 0.08, 0.06), TEAM, loc=(0.046, 0.02, seat + 0.26)))
        p.append(sphere(0.008, GOLD, loc=(0.045, -0.02, seat + 0.305), subdiv=1))
    p.append(segment((-0.034, 0.004, top - 0.005), (-0.02, -0.05, seat + 0.03), 0.009, 0.008, tunic))       # reins arm
    p += round_shield((-0.045, 0.01, seat + 0.03), TEAM_DARK, r=0.034)
    return p


def cavalry(riders, weapon="lance", head="kettle", tunic=TEAM, seed=0):
    parts = []
    for i, (x, y, t) in enumerate(riders):
        unit = horse(gait=0.012 * (1 if i % 2 else -1)) + rider(weapon, tunic, head)
        parts += group(unit, offset=(x, y, 0), rot_z=t)
    return parts

# ---------------------------------------------------------------------------------------- engines

def spoked_wheel(x, y, z, r, color=WOOD_DARK, spokes=8, width=0.016):
    p = [torus_arc(r, width * 0.35, 0, 360, color, loc=(x, y, z), rot=(0, 0, 90), steps=16, sides=5)]  # rim in the YZ plane
    for k in range(spokes):
        ang = math.radians(k * 360 / spokes)
        p.append(segment((x, y, z), (x, y + math.cos(ang) * r, z + math.sin(ang) * r), 0.0028, 0.0028, color, sides=4))
    p.append(cylinder(r * 0.22, width * 1.4, IRON, loc=(x, y, z), rot=(0, 90, 0), sides=8))
    return p


def catapult():
    p = []
    for side in (-1, 1):
        p.append(box((0.018, 0.3, 0.022), WOOD, loc=(0.06 * side, 0.0, 0.05), bevel=0.003))            # rails
        p += spoked_wheel(0.08 * side, -0.1, 0.045, 0.045)
        p += spoked_wheel(0.08 * side, 0.1, 0.045, 0.045)
        p.append(segment((0.06 * side, 0.08, 0.06), (0.05 * side, 0.0, 0.2), 0.008, 0.008, WOOD_DARK))  # A-frame
        p.append(segment((0.06 * side, -0.07, 0.06), (0.05 * side, 0.0, 0.2), 0.008, 0.008, WOOD_DARK))
    for y in (-0.12, 0.0, 0.12):
        p.append(box((0.13, 0.02, 0.018), WOOD, loc=(0, y, 0.05)))
    p.append(segment((-0.05, 0.0, 0.2), (0.05, 0.0, 0.2), 0.008, 0.008, WOOD_DARK))                     # cross beam
    p.append(cylinder(0.022, 0.1, a.hexcol(0x9A7C55), loc=(0, 0.1, 0.075), rot=(0, 90, 0), sides=8))   # torsion rope
    p.append(segment((0, 0.1, 0.075), (0, -0.09, 0.26), 0.009, 0.007, WOOD))                            # throwing arm
    p.append(sphere(0.028, WOOD_DARK, loc=(0, -0.1, 0.27), subdiv=1, scale=(1, 1, 0.55)))
    p.append(sphere(0.02, a.hexcol(0x8A8278), loc=(0, -0.1, 0.285), subdiv=1, jitter=0.15, seed=5))
    p.append(box((0.12, 0.004, 0.05), TEAM, loc=(0, 0.155, 0.08)))                                       # hide screen
    p += group(soldier("worker", 3), offset=(-0.17, 0.08, 0), rot_z=35)
    p += group(soldier("sword", 4), offset=(0.17, 0.1, 0), rot_z=-30)
    return p


def cannon():
    p = []
    for side in (-1, 1):
        p += spoked_wheel(0.07 * side, 0.0, 0.07, 0.07, spokes=10)
        p.append(box((0.012, 0.16, 0.05), WOOD, loc=(0.035 * side, 0.03, 0.08), rot=(14, 0, 0), bevel=0.003))  # cheeks
    p.append(cylinder(0.006, 0.16, IRON, loc=(0, 0, 0.07), rot=(0, 90, 0), sides=6))                    # axle
    p.append(box((0.04, 0.2, 0.025), WOOD, loc=(0, 0.14, 0.035), rot=(14, 0, 0), bevel=0.004))          # trail
    barrel = segment((0, 0.05, 0.11), (0, -0.2, 0.14), 0.026, 0.017, a.hexcol(0x3E3A36), sides=10)
    p.append(barrel)
    for t in (0.1, 0.45, 0.95):
        c = Vector((0, 0.05, 0.11)).lerp(Vector((0, -0.2, 0.14)), t)
        p.append(segment(c + Vector((0, 0.005, 0)), c - Vector((0, 0.005, 0)), 0.03 - t * 0.009, 0.03 - t * 0.009, a.hexcol(0x2E2A26), sides=10))
    p.append(sphere(0.022, a.hexcol(0x3E3A36), loc=(0, 0.06, 0.11), subdiv=1))                          # cascabel
    for k in range(3):
        p.append(sphere(0.012, BLACK, loc=(0.1 + k * 0.022, 0.16, 0.012), subdiv=1))                    # shot pile
    p += group(soldier("musket", 11), offset=(-0.16, 0.12, 0), rot_z=20)
    p += group(soldier("musket", 12), offset=(0.17, 0.08, 0), rot_z=-25)
    return p


def rocket_truck():
    p = []
    body = OLIVE
    p.append(box((0.13, 0.34, 0.035), a.hexcol(0x3A3A3A), loc=(0, 0.0, 0.06)))                          # chassis
    p.append(poly([(-0.065, -0.17, 0.08), (0.065, -0.17, 0.08), (0.065, -0.08, 0.08), (-0.065, -0.08, 0.08),
                   (-0.06, -0.15, 0.17), (0.06, -0.15, 0.17), (0.06, -0.09, 0.17), (-0.06, -0.09, 0.17)],
                  [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7), (3, 2, 1, 0)], body, bevel=0.004))  # cab
    p.append(box((0.1, 0.004, 0.04), a.hexcol(0x9FC4D8), loc=(0, -0.158, 0.14), rot=(-15, 0, 0)))        # windscreen
    p.append(box((0.13, 0.22, 0.03), body, loc=(0, 0.05, 0.09), bevel=0.005))                            # bed
    launcher = []
    for i in range(4):
        for j in range(3):
            launcher.append(cylinder(0.011, 0.2, a.hexcol(0x4A4E45), loc=(-0.036 + i * 0.024, 0.0, j * 0.024), rot=(90, 0, 0), sides=6))
    p += group(launcher, offset=(0, 0.06, 0.14))
    for o in launcher:
        o.rotation_euler = (math.radians(90 - 25), 0, 0)
    for side in (-1, 1):
        for y in (-0.12, 0.05, 0.13):
            p.append(cylinder(0.034, 0.028, BLACK, loc=(0.07 * side, y, 0.034), rot=(0, 90, 0), sides=10))
            p.append(cylinder(0.015, 0.03, a.hexcol(0x6A6A6A), loc=(0.071 * side, y, 0.034), rot=(0, 90, 0), sides=6))
    p.append(box((0.05, 0.004, 0.03), TEAM, loc=(0, -0.172, 0.1)))
    p.append(box((0.004, 0.04, 0.03), TEAM, loc=(0.066, -0.12, 0.13)))
    return p


def ram():
    p = []
    for side in (-1, 1):
        for y in (-0.12, 0.12):
            p.append(segment((0.07 * side, y, 0.05), (0.03 * side, y, 0.2), 0.008, 0.008, WOOD_DARK))
        p += spoked_wheel(0.085 * side, -0.1, 0.04, 0.04, spokes=6)
        p += spoked_wheel(0.085 * side, 0.1, 0.04, 0.04, spokes=6)
        p.append(box((0.016, 0.3, 0.02), WOOD, loc=(0.07 * side, 0, 0.05)))
    roof = TEAM
    p.append(poly([(-0.095, -0.16, 0.07), (0.095, -0.16, 0.07), (0.095, 0.16, 0.07), (-0.095, 0.16, 0.07), (0, -0.16, 0.22), (0, 0.16, 0.22)],
                  [(0, 1, 4), (3, 5, 2), (0, 4, 5, 3), (1, 2, 5, 4)], roof, bevel=0.003))
    for y in (-0.1, 0.0, 0.1):  # battens over the hides
        p.append(segment((-0.097, y, 0.072), (0, y, 0.223), 0.004, 0.004, WOOD_DARK, sides=4))
        p.append(segment((0.097, y, 0.072), (0, y, 0.223), 0.004, 0.004, WOOD_DARK, sides=4))
    p.append(segment((0, 0.16, 0.12), (0, -0.24, 0.12), 0.028, 0.026, WOOD, sides=8))                   # log
    p.append(sphere(0.036, IRON, loc=(0, -0.25, 0.12), subdiv=1, scale=(1, 1.3, 1)))                      # ram head
    return p


def siege_tower():
    p = [box((0.19, 0.19, 0.46), WOOD, loc=(0, 0, 0.28), taper=(0.78, 0.78), bevel=0.005)]
    for k in range(5):
        p.append(box((0.196, 0.196, 0.01), WOOD_DARK, loc=(0, 0, 0.09 + k * 0.09), taper=(0.97, 0.97)))
    for side in (-1, 1):
        p.append(segment((0.07 * side, -0.092, 0.06), (0.058 * side, -0.075, 0.46), 0.005, 0.005, WOOD_DARK))
        p += spoked_wheel(0.1 * side, -0.07, 0.045, 0.045, spokes=6)
        p += spoked_wheel(0.1 * side, 0.07, 0.045, 0.045, spokes=6)
    p.append(box((0.12, 0.012, 0.15), WOOD_DARK, loc=(0, -0.09, 0.44), rot=(-18, 0, 0)))                  # drawbridge
    p.append(box((0.19, 0.19, 0.02), WOOD_DARK, loc=(0, 0, 0.51)))
    p.append(segment((0.07, 0.07, 0.51), (0.07, 0.07, 0.66), 0.004, 0.004, WOOD, sides=4))
    p.append(box((0.004, 0.08, 0.05), TEAM, loc=(0.072, 0.03, 0.63)))
    p.append(box((0.14, 0.006, 0.12), TEAM, loc=(0, 0.097, 0.3)))                                        # hide on the back
    return p


def tank():
    p = []
    hull = a.hexcol(0x5C6B3A)
    L, W = 0.17, 0.085
    p.append(poly([(-W, -L, 0.05), (W, -L, 0.05), (W, L, 0.05), (-W, L, 0.05),
                   (-W, -L + 0.06, 0.12), (W, -L + 0.06, 0.12), (W, L - 0.02, 0.12), (-W, L - 0.02, 0.12)],
                  [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7), (3, 2, 1, 0)], hull, bevel=0.006))
    for side in (-1, 1):
        p.append(box((0.04, 0.36, 0.06), a.hexcol(0x2E2E2E), loc=(0.098 * side, 0, 0.036), bevel=0.018, segments=2))
        for y in (-0.12, -0.06, 0.0, 0.06, 0.12):
            p.append(cylinder(0.024, 0.044, a.hexcol(0x4A4E45), loc=(0.098 * side, y, 0.032), rot=(0, 90, 0), sides=10))
        p.append(box((0.03, 0.35, 0.008), hull, loc=(0.1 * side, 0, 0.07)))                               # fenders
    turret = cylinder(0.068, 0.06, hull, loc=(0, 0.02, 0.15), sides=10, top=0.055, bevel=0.006)
    turret.scale = (1.0, 1.25, 1.0)
    p.append(turret)
    p.append(segment((0, -0.06, 0.155), (0, -0.28, 0.16), 0.011, 0.009, a.hexcol(0x3E4A2A), sides=8))
    p.append(segment((0, -0.27, 0.16), (0, -0.3, 0.16), 0.015, 0.015, a.hexcol(0x3E4A2A), sides=8))
    p.append(cylinder(0.022, 0.018, hull, loc=(0.025, 0.05, 0.185), sides=8))                           # hatch
    p.append(segment((0.025, 0.04, 0.2), (0.025, -0.02, 0.205), 0.003, 0.003, BLACK, sides=4))          # MG
    p.append(box((0.07, 0.004, 0.028), TEAM, loc=(0, -0.071, 0.15)))                                     # turret band
    p.append(box((0.004, 0.08, 0.028), TEAM, loc=(0.068, 0.02, 0.15)))
    p.append(box((0.004, 0.08, 0.028), TEAM, loc=(-0.068, 0.02, 0.15)))
    return p

# ---------------------------------------------------------------------------------------- ships

def hull_mesh(length, beam, depth, side, deck, sheer=0.02, stations=7, transom=0.55):
    """A ship hull from cross-sections along Y (bow toward -Y) with a rising sheer line."""
    verts, faces = [], []
    rows = []
    for i in range(stations + 1):
        t = i / stations                                      # 0 bow .. 1 stern
        y = -length / 2 + t * length
        width = beam / 2 * (math.sin(math.pi * min(1.0, t * 1.15)) ** 0.6 if t < 0.8 else transom + (1 - transom) * (1 - (t - 0.8) / 0.2 * 0.3))
        width = max(width, 0.004) if i else 0.002
        top = depth + sheer * ((1 - t) ** 2 * 2 + t ** 3)     # higher at the bow and a little at the stern
        keel = 0.0 if 0.1 < t < 0.95 else depth * 0.25
        row = [len(verts), len(verts) + 1, len(verts) + 2, len(verts) + 3, len(verts) + 4]
        verts += [(-width, y, top), (-width * 0.7, y, depth * 0.35), (0.0, y, keel), (width * 0.7, y, depth * 0.35), (width, y, top)]
        rows.append(row)
    for i in range(stations):
        r0, r1 = rows[i], rows[i + 1]
        for k in range(4):
            faces.append((r0[k], r1[k], r1[k + 1], r0[k + 1]))
    stern = rows[-1]
    faces.append((stern[0], stern[1], stern[2], stern[3], stern[4]))
    h = poly(verts, faces, side, bevel=0.003)
    # Deck: counter-clockwise seen from above (starboard bow to stern, back along port), facing up.
    deck_pts = [verts[r[4]] for r in rows] + [verts[r[0]] for r in reversed(rows)]
    d = poly([(x * 0.96, y, z - 0.004) for (x, y, z) in deck_pts], [tuple(range(len(deck_pts)))], deck, recalc=False)
    return [h, d]


def sail(y, z0, z1, width, color=CANVAS, belly=0.012):
    """A square sail billowing forward (-Y), with a team stripe."""
    p = [poly([(-width, y, z0), (width, y, z0), (width * 0.9, y - belly, (z0 + z1) / 2), (width, y, z1), (-width, y, z1), (-width * 0.9, y - belly, (z0 + z1) / 2)],
              [(0, 1, 2, 5), (5, 2, 3, 4)], color)]
    mid = (z0 + z1) / 2
    p.append(poly([(-width * 0.9, y - belly - 0.002, mid - 0.012), (width * 0.9, y - belly - 0.002, mid - 0.012),
                   (width * 0.9, y - belly - 0.002, mid + 0.012), (-width * 0.9, y - belly - 0.002, mid + 0.012)], [(0, 1, 2, 3)], TEAM))
    p.append(segment((-width * 1.1, y, z1), (width * 1.1, y, z1), 0.003, 0.003, WOOD_DARK, sides=4))    # yard
    return p


def sail_ship():
    p = hull_mesh(0.62, 0.17, 0.08, a.hexcol(0x6A4A30), a.hexcol(0xB08A5E), sheer=0.03)
    p.append(box((0.15, 0.1, 0.05), a.hexcol(0x5A3E26), loc=(0, 0.24, 0.12), bevel=0.005))                  # quarterdeck
    p.append(box((0.172, 0.4, 0.012), a.hexcol(0xD8C090), loc=(0, 0.0, 0.065)))                           # wale stripe
    for k in range(6):
        for side in (-1, 1):
            p.append(box((0.004, 0.016, 0.012), BLACK, loc=(0.086 * side, -0.15 + k * 0.06, 0.05)))       # gun ports
    for (y, h) in ((-0.13, 0.36), (0.02, 0.44), (0.17, 0.32)):
        p.append(segment((0, y, 0.08), (0, y, 0.08 + h), 0.006, 0.004, WOOD_DARK, sides=6))
        p += sail(y - 0.01, 0.13, 0.08 + h * 0.55, 0.075 + h * 0.08)
        p += sail(y - 0.01, 0.08 + h * 0.58, 0.08 + h * 0.9, 0.05 + h * 0.06, belly=0.009)
        p.append(box((0.002, 0.04, 0.02), TEAM, loc=(0, y + 0.02, 0.08 + h)))                               # pennant
    p.append(segment((0, -0.3, 0.1), (0, -0.44, 0.17), 0.005, 0.003, WOOD_DARK, sides=5))                  # bowsprit
    p.append(poly([(0, -0.43, 0.165), (0, -0.14, 0.34), (0, -0.2, 0.12)], [(0, 1, 2)], CANVAS))              # jib
    p.append(box((0.004, 0.06, 0.04), TEAM, loc=(0, 0.3, 0.2)))                                              # ensign
    return p


def steamship(carrier=False):
    grey = a.hexcol(0x7A8590)
    p = hull_mesh(0.72, 0.16, 0.07, grey, a.hexcol(0x8C949C), sheer=0.02, transom=0.7)
    p.append(box((0.162, 0.66, 0.012), TEAM_DARK, loc=(0, 0.0, 0.012)))                                  # boot topping
    if carrier:
        p.append(box((0.24, 0.76, 0.016), a.hexcol(0x5E6670), loc=(0, 0.0, 0.1), bevel=0.004))
        p.append(box((0.045, 0.12, 0.1), grey, loc=(0.1, 0.06, 0.16), bevel=0.006))
        p.append(cylinder(0.018, 0.04, BLACK, loc=(0.1, 0.07, 0.225), sides=8))
        p.append(box((0.004, 0.62, 0.002), WHITE, loc=(-0.02, 0.0, 0.109)))
        p.append(box((0.08, 0.08, 0.002), TEAM, loc=(-0.03, 0.3, 0.109)))
        for k, y in enumerate((-0.25, -0.12, 0.02)):   # parked aircraft
            p.append(box((0.07, 0.012, 0.004), a.hexcol(0x6E7A5A), loc=(-0.04, y, 0.114)))
            p.append(box((0.01, 0.05, 0.006), a.hexcol(0x6E7A5A), loc=(-0.04, y + 0.01, 0.116)))
        return p
    p.append(box((0.1, 0.2, 0.05), grey, loc=(0, 0.02, 0.105), bevel=0.006))
    p.append(box((0.07, 0.08, 0.05), grey, loc=(0, -0.05, 0.15), bevel=0.006))                          # bridge
    p.append(box((0.072, 0.004, 0.012), a.hexcol(0x9FC4D8), loc=(0, -0.092, 0.165)))
    for y in (0.04, 0.12):
        p.append(cylinder(0.02, 0.1, a.hexcol(0x5A6068), loc=(0, y, 0.17), sides=10, top=0.018, rot=(-10, 0, 0)))
        p.append(cylinder(0.021, 0.022, TEAM, loc=(0, y + 0.004, 0.2), sides=10, rot=(-10, 0, 0)))
    for (y, d) in ((-0.2, -1), (0.25, 1)):
        p.append(cylinder(0.034, 0.03, grey, loc=(0, y, 0.1), sides=10, bevel=0.004))
        for x in (-0.012, 0.012):
            p.append(segment((x, y, 0.104), (x, y + 0.09 * d, 0.112), 0.005, 0.004, BLACK, sides=5))
    p.append(segment((0, -0.02, 0.17), (0, -0.02, 0.3), 0.004, 0.003, BLACK, sides=4))                    # mast
    p.append(box((0.002, 0.04, 0.025), TEAM, loc=(0, 0.0, 0.29)))
    return p

# ---------------------------------------------------------------------------------------- aircraft

def plane(kind):
    z = 0.32
    metal = a.hexcol(0x9AA4AE) if kind == "jet" else a.hexcol(0x6E7A5A)
    p = []
    L = 0.3 if kind == "bomber" else 0.24
    rmax = 0.03 if kind == "bomber" else 0.022
    # Fuselage: segments with a nose and a tapering tail.
    stations = [(-0.5, 0.35), (-0.4, 0.85), (-0.2, 1.0), (0.1, 0.9), (0.35, 0.55), (0.5, 0.2)]
    for (t0, r0), (t1, r1) in zip(stations, stations[1:]):
        p.append(segment((0, t0 * L * 2, z), (0, t1 * L * 2, z + (0.01 if t1 > 0.3 else 0)), rmax * r0, rmax * r1, metal, sides=8))
    p.append(sphere(rmax * 0.8, a.hexcol(0x9FC4D8), loc=(0, -L * 0.35, z + rmax * 0.8), subdiv=1, scale=(0.8, 1.8, 0.8)))  # canopy
    if kind == "jet":
        p.append(poly([(0, -L * 0.5, z), (L * 0.75, L * 0.35, z), (L * 0.2, L * 0.42, z), (-L * 0.2, L * 0.42, z), (-L * 0.75, L * 0.35, z)],
                      [(0, 1, 2, 3, 4)], metal, recalc=False))
        p.append(poly([(0, L * 0.1, z + 0.005), (0, L * 0.48, z + 0.005), (0, L * 0.5, z + 0.09)], [(0, 1, 2)], metal))
        p.append(poly([(0.002, L * 0.25, z + 0.04), (0.002, L * 0.4, z + 0.04), (0.002, L * 0.42, z + 0.07)], [(0, 1, 2)], TEAM))
    else:
        span = 0.34 if kind == "bomber" else 0.24
        for side in (-1, 1):
            p.append(poly([(0, -0.03, z), (span * side, -0.005, z + 0.01), (span * side, 0.03, z + 0.01), (0, 0.05, z)],
                          [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], metal, recalc=False))
            p.append(poly([(0, L * 0.82, z + 0.01), (0.075 * side, L * 0.86, z + 0.012), (0.07 * side, L * 0.93, z + 0.012), (0, L * 0.95, z + 0.01)],
                          [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], metal, recalc=False))
            p.append(cylinder(0.02, 0.003, TEAM, loc=(span * 0.7 * side, 0.012, z + 0.014), sides=10))      # roundels
            p.append(cylinder(0.009, 0.004, WHITE, loc=(span * 0.7 * side, 0.012, z + 0.015), sides=8))
        p.append(poly([(0, L * 0.78, z + 0.01), (0, L * 0.98, z + 0.012), (0, L, z + 0.07)], [(0, 1, 2)], metal))
        engines = (-0.17, -0.08, 0.08, 0.17) if kind == "bomber" else (None,)
        for x in engines:
            if x is None:
                p.append(cylinder(0.05, 0.003, a.hexcol(0x3A3A3A), loc=(0, -L - 0.005, z), rot=(90, 0, 0), sides=3))  # propeller
                p.append(sphere(0.01, TEAM, loc=(0, -L - 0.008, z), subdiv=1))
            else:
                p.append(segment((x, 0.02, z - 0.004), (x, -0.06, z - 0.004), 0.013, 0.011, metal, sides=8))
                p.append(cylinder(0.035, 0.003, a.hexcol(0x3A3A3A), loc=(x, -0.063, z - 0.004), rot=(90, 0, 0), sides=3))
    p.append(segment((0, 0.0, 0.0), (0, 0.0, z - 0.02), 0.005, 0.004, IRON, sides=6))                      # flight stand
    p.append(cylinder(0.05, 0.012, PLINTH, loc=(0, 0, 0.006), sides=10))
    return p

# ---------------------------------------------------------------------------------------- civilians

def settler():
    p = []
    wagon = []
    wagon.append(box((0.1, 0.2, 0.03), WOOD, loc=(0, 0, 0.085), bevel=0.004))
    for k in range(4):   # canvas hoops
        wagon.append(torus_arc(0.052, 0.006, 0, 180, CANVAS, loc=(0, -0.075 + k * 0.05, 0.1), rot=(0, 0, 0), steps=8))
    cover = cylinder(0.05, 0.19, CANVAS, loc=(0, 0, 0.1), rot=(90, 0, 0), sides=12, cap=True)
    cover.scale = (1, 1, 1)
    wagon.append(cover)
    wagon.append(box((0.101, 0.02, 0.004), TEAM, loc=(0, -0.04, 0.15)))
    for side in (-1, 1):
        wagon += spoked_wheel(0.065 * side, -0.065, 0.045, 0.045, spokes=6)
        wagon += spoked_wheel(0.065 * side, 0.065, 0.045, 0.045, spokes=6)
    wagon.append(segment((0, -0.1, 0.06), (0, -0.2, 0.06), 0.004, 0.004, WOOD_DARK, sides=4))
    p += group(wagon, offset=(0.06, 0.06, 0))
    p += group(soldier("settler", 21), offset=(-0.1, -0.08, 0), rot_z=-10)
    p += group(soldier("settler", 22), offset=(-0.14, 0.08, 0), rot_z=8, scale=0.92)
    return p


def workers():
    p = []
    p += group(soldier("worker", 31), offset=(-0.08, -0.03, 0), rot_z=-15)
    p += group(soldier("worker", 32), offset=(0.08, 0.04, 0), rot_z=20)
    p.append(box((0.06, 0.04, 0.03), WOOD, loc=(0.02, -0.12, 0.015), bevel=0.004))                        # crate
    p.append(sphere(0.022, a.hexcol(0x8A8278), loc=(-0.05, 0.12, 0.012), subdiv=1, jitter=0.2, seed=3))
    return p


def general():
    """Civ V's Great General: a mounted commander carrying the standard."""
    p = horse(coat=a.hexcol(0xE8E2D4), dark=a.hexcol(0xB8B0A0), barding=TEAM) + rider("banner", TEAM, "crown")
    return p


UNITS = {
    "sword": lambda: formation("sword", SQUAD4, 1),
    "spear": lambda: formation("spear", SQUAD4, 2),
    "bow": lambda: formation("bow", LINE3, 3),
    "crossbow": lambda: formation("crossbow", LINE3, 4),
    "musket": lambda: formation("musket", SQUAD4, 5),
    "helmet": lambda: formation("rifle", SQUAD3, 6),
    "scout": lambda: formation("scout", [(0.0, 0.0, 0)], 7),
    "horse": lambda: cavalry([(-0.09, 0.05, 0), (0.09, -0.06, 0)]),
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
    "settler": settler,
    "worker": workers,
    "general": general,
    "scientist": lambda: great_person("scientist"),
    "engineer": lambda: great_person("engineer"),
    "merchant": lambda: great_person("merchant"),
    "artist": lambda: great_person("artist"),
    "prophet": lambda: great_person("prophet"),
}
