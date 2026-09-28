"""
Terrain tile tops and mountain peaks for Crucible of Ages (used by build_assets.py).

A tile top fills the flat inside of a map hex: a pointy-top hexagon with a corner radius of 0.84
(the game draws the bevelled rim, beaches, cliffs and rivers around it). Its outline stays at
height 0 so neighbouring tiles always meet cleanly, and the middle is kept fairly level so units
and cities stand on it. The game picks one of the variants per hex and turns it by a multiple of
60 degrees, so repeats are hard to spot.

Names: tile_<kind>_<n>. Kinds: grassland, plains, desert, tundra, snow (flat land), hills_<same>
(elevation 2+), mountain, marsh, ocean, coast, lake, ice. Peaks: prop_peak_<n>.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector, noise

import crucible_art as a

INNER = 0.84           # corner radius of the insert (TerrainArt.InnerRadius x HexSize)
APOTHEM = INNER * math.cos(math.radians(30))
RINGS = 5              # triangle rings from centre to edge (150 triangles)


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def hexnorm(x, y):
    """0 at the centre, 1 on the hexagon's edge (edge normals at 0, 60, 120 degrees)."""
    return max(abs(x * math.cos(math.radians(k)) + y * math.sin(math.radians(k))) for k in (0, 60, 120)) / APOTHEM


def n2(x, y, freq, seed):
    return noise.noise(Vector((x * freq + seed * 17.13, y * freq - seed * 5.71, seed * 0.37)))


def mix(c1, c2, t):
    return tuple(c1[i] + (c2[i] - c1[i]) * t for i in range(3))


def scale(c, k):
    return tuple(min(1.0, v * k) for v in c)


def hex_surface(height_fn, color_fn, name, seed=0):
    """A triangulated hexagon (RINGS rings) displaced by height_fn and flat-coloured per face by color_fn.
    Interior vertices are nudged sideways a little so the triangle grid doesn't read as a grid."""
    rnd = random.Random(seed * 7919 + 1)
    bm = bmesh.new()
    col = bm.loops.layers.float_color.new("Col")
    corners = [Vector((math.cos(math.radians(60 * i - 30)) * INNER, math.sin(math.radians(60 * i - 30)) * INNER, 0)) for i in range(6)]
    verts = {}

    def vert(p):
        key = (round(p.x, 5), round(p.y, 5))
        v = verts.get(key)
        if v is None:
            edge = hexnorm(p.x, p.y) > 0.999
            x, y = p.x, p.y
            if not edge and (x or y):
                x += rnd.uniform(-0.035, 0.035)
                y += rnd.uniform(-0.035, 0.035)
            z = 0.0 if edge else height_fn(x, y)
            v = verts[key] = bm.verts.new((x, y, z))
        return v

    n = RINGS
    for s in range(6):
        c0, c1 = corners[s], corners[(s + 1) % 6]
        def at(i, j):  # barycentric grid point in sector s: i steps toward c0, j toward c1
            return (c0 * i + c1 * j) / n
        for i in range(n):
            for j in range(n - i):
                tri = [at(i, j), at(i + 1, j), at(i, j + 1)]
                faces = [tri]
                if i + j < n - 1:
                    faces.append([at(i + 1, j), at(i + 1, j + 1), at(i, j + 1)])
                for f in faces:
                    vs = [vert(p) for p in f]
                    try:
                        face = bm.faces.new(vs)
                    except ValueError:
                        continue
                    cen = sum((v.co for v in vs), Vector()) / 3
                    normal = (vs[1].co - vs[0].co).cross(vs[2].co - vs[0].co).normalized()
                    if normal.z < 0:
                        face.normal_flip()
                        normal = -normal
                    rgb = color_fn(cen.x, cen.y, cen.z, normal.z)
                    lin = tuple(a.srgb_to_linear(c) for c in rgb) + (1.0,)
                    for loop in face.loops:
                        loop[col] = lin
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def scatter(rnd, count, min_r=0.18, max_edge=0.82):
    """Random points inside the hexagon, away from the centre (units stand there) and the edge."""
    pts = []
    tries = 0
    while len(pts) < count and tries < count * 30:
        tries += 1
        x, y = rnd.uniform(-INNER, INNER), rnd.uniform(-INNER, INNER)
        if hexnorm(x, y) < max_edge and math.hypot(x, y) > min_r:
            pts.append((x, y))
    return pts


# ---------------------------------------------------------------------------------------- land

LAND = {
    #            base         light        dark         accent       relief
    "grassland": (0x6B9B3C, 0x80B04A, 0x5A8A34, 0x93AE4C, 0.035),
    "plains":    (0xB2A65A, 0xC9B96A, 0x9C9450, 0x8E9E4A, 0.03),
    "desert":    (0xE2C98B, 0xEFDAA2, 0xCCB070, 0xD8B878, 0.045),
    "tundra":    (0x8C917A, 0x9CA086, 0x767B66, 0x7C8A60, 0.035),
    "snow":      (0xEEF2F5, 0xFAFCFD, 0xC9D6E4, 0xDDE6EE, 0.04),
}
HILL_TINT = 0xA08E62


def land_tile(kind, seed, hills=False):
    base, light, dark, accent, relief = (a.hexcol(v) if isinstance(v, int) else v for v in LAND[kind])
    if hills:
        base, light, dark = (mix(c, a.hexcol(HILL_TINT), 0.18) for c in (base, light, dark))
    rnd = random.Random(seed)
    mounds = []
    if hills:
        for k in range(rnd.choice((2, 3))):
            ang = rnd.uniform(0, 2 * math.pi) + k * 2.1
            r = rnd.uniform(0.38, 0.5)
            mounds.append((math.cos(ang) * r, math.sin(ang) * r, rnd.uniform(0.2, 0.27), rnd.uniform(0.09, 0.14)))

    def height(x, y):
        edge = smoothstep(0.0, 0.3, 1 - hexnorm(x, y))
        centre = 0.35 + 0.65 * smoothstep(0.08, 0.3, math.hypot(x, y))
        h = relief * (n2(x, y, 3.2, seed) * 0.7 + n2(x, y, 7.5, seed + 3) * 0.3)
        if kind == "desert":  # wind ripples across the tile
            h += 0.018 * math.sin((x * 0.8 + y * 0.6) * 22 + n2(x, y, 2, seed) * 2)
        for (mx, my, mr, mh) in mounds:
            d = math.hypot(x - mx, y - my) / mr
            h += mh * max(0.0, 1 - d * d) ** 1.5
        return h * edge * centre

    def color(x, y, z, up):
        patch = n2(x, y, 4.5, seed + 11)
        c = base
        if patch > 0.25:
            c = light
        elif patch < -0.3:
            c = dark
        if kind in ("grassland", "plains") and n2(x, y, 9, seed + 5) > 0.45:
            c = accent
        if kind == "desert" and up < 0.97:
            c = dark if (x * 0.8 + y * 0.6) > 0 else light  # lit and shaded ripple faces
        if kind == "snow" and z < -0.005:
            c = dark  # blue shade in hollows
        c = scale(c, 0.94 + 0.12 * max(0.0, min(1.0, z / 0.08 + 0.5)))  # crests lighter
        if hills and up < 0.9:
            c = mix(c, a.hexcol(0x8A7A5A), 0.35)  # bare earth on steep hill flanks
        return c

    parts = [hex_surface(height, color, f"tile_{kind}", seed)]
    # Surface details.
    for (x, y) in scatter(rnd, {"grassland": 7, "plains": 6, "desert": 3, "tundra": 5, "snow": 2}[kind]):
        z = height(x, y)
        roll = rnd.random()
        if kind in ("grassland", "plains") and roll < 0.65:
            tuft = mix(dark, a.hexcol(0x4E7A2E), 0.4) if kind == "grassland" else a.hexcol(0xC8B566)
            for b in range(3):
                parts.append(a.cylinder(0.012, 0.05, tuft, loc=(x + (b - 1) * 0.01, y, z + 0.022),
                                        rot=(rnd.uniform(-20, 20), rnd.uniform(-20, 20), 0), sides=3, top=0.0))
        elif kind == "grassland" and roll < 0.85:
            petal = rnd.choice((0xF4F1E4, 0xF2D24B, 0xE88FB0))
            parts.append(a.sphere(0.012, a.hexcol(petal), loc=(x, y, z + 0.012), subdiv=0))
        else:
            rock = a.hexcol(0x9A968C) if kind != "desert" else a.hexcol(0xB89A6A)
            parts.append(a.sphere(rnd.uniform(0.02, 0.035), rock, loc=(x, y, z + 0.008), subdiv=0,
                                  scale=(1.2, 1.0, 0.6), jitter=0.2, seed=seed + len(parts)))
    return parts


def mountain_tile(seed):
    rnd = random.Random(seed)

    def height(x, y):
        edge = smoothstep(0.0, 0.3, 1 - hexnorm(x, y))
        return 0.07 * (abs(n2(x, y, 4, seed)) + 0.4 * n2(x, y, 9, seed + 1)) * edge

    def color(x, y, z, up):
        c = a.hexcol(0x7C7068) if n2(x, y, 5, seed + 2) > -0.1 else a.hexcol(0x5A504A)
        if up > 0.97 and z > 0.04:
            c = a.hexcol(0x8D8278)
        return c

    parts = [hex_surface(height, color, "tile_mountain", seed)]
    for (x, y) in scatter(rnd, 4, min_r=0.1):
        parts.append(a.sphere(rnd.uniform(0.03, 0.05), a.hexcol(0x6A625A), loc=(x, y, height(x, y) + 0.01), subdiv=0, jitter=0.25, seed=seed + len(parts)))
    return parts


def marsh_tile(seed):
    rnd = random.Random(seed)
    pools = [(math.cos(k * 2.2 + seed) * 0.42, math.sin(k * 2.2 + seed) * 0.42, rnd.uniform(0.15, 0.22)) for k in range(3)]

    def pool(x, y):
        return max(max(0.0, 1 - math.hypot(x - px, y - py) / pr) for (px, py, pr) in pools)

    def height(x, y):
        edge = smoothstep(0.0, 0.3, 1 - hexnorm(x, y))
        return (0.015 * n2(x, y, 5, seed) - 0.03 * smoothstep(0.0, 0.3, pool(x, y))) * edge

    def color(x, y, z, up):
        if z < -0.012:
            return a.hexcol(0x456E62)  # standing water
        return a.hexcol(0x5E7A56) if n2(x, y, 6, seed) > 0 else a.hexcol(0x6E8A5A)

    parts = [hex_surface(height, color, "tile_marsh", seed)]
    for (x, y) in scatter(rnd, 10, min_r=0.15):
        if pool(x, y) > 0.5:
            continue
        parts.append(a.cylinder(0.01, rnd.uniform(0.1, 0.17), a.hexcol(0x8A9A4E), loc=(x, y, 0.05), sides=3, top=0.0,
                                rot=(rnd.uniform(-12, 12), rnd.uniform(-12, 12), 0)))
    return parts

# ---------------------------------------------------------------------------------------- water

WATER = {"ocean": (0x1C3F6E, 0x28528A), "coast": (0x2F72A0, 0x4A8CBC), "lake": (0x3C95B4, 0x5AAFCB)}


def water_tile(kind, seed):
    deep, crest = (a.hexcol(v) for v in WATER[kind])

    def height(x, y):
        edge = smoothstep(0.0, 0.25, 1 - hexnorm(x, y))
        return 0.012 * (math.sin(x * 13 + n2(x, y, 3, seed) * 3) * 0.6 + n2(x, y, 8, seed + 1) * 0.4) * edge

    def color(x, y, z, up):
        return crest if z > 0.006 else mix(deep, crest, 0.25) if z > 0 else deep

    return [hex_surface(height, color, f"tile_{kind}", seed)]


def ice_tile(seed):
    rnd = random.Random(seed)
    # Floes: Voronoi cells around a few seeds; cracks where two cells meet.
    seeds_ = [(rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), rnd.uniform(0.0, 0.035)) for _ in range(6)]

    def cell(x, y):
        d = sorted((math.hypot(x - sx, y - sy), h) for (sx, sy, h) in seeds_)
        return d[0][1], d[1][0] - d[0][0]

    def height(x, y):
        h, gap = cell(x, y)
        edge = smoothstep(0.0, 0.2, 1 - hexnorm(x, y))
        return (h + 0.02 if gap > 0.05 else -0.005) * edge

    def color(x, y, z, up):
        h, gap = cell(x, y)
        if gap <= 0.05:
            return a.hexcol(0x2F5A7A)
        return a.hexcol(0xE4EEF3) if up > 0.98 else a.hexcol(0xA9C4D6)

    return [hex_surface(height, color, "tile_ice", seed)]

# ---------------------------------------------------------------------------------------- peaks

def peak(seed, height=1.25, snow=True):
    """A craggy peak: a jittered cone with a snow cap, plus a smaller shoulder peak."""
    rnd = random.Random(seed)
    parts = []

    def crag(x, y, r, h, s):
        """Stacked, jittered rings narrowing to a summit; the upper bands are snow."""
        rr = random.Random(s)
        sides = 7
        rings = [(0.0, 1.0), (0.3, 0.72), (0.55, 0.46), (0.78, 0.22)]
        snow_from = 2 if snow else 99
        bm = bmesh.new()
        col = bm.loops.layers.float_color.new("Col")
        layer = []
        for k, (fz, fr) in enumerate(rings):
            row = []
            for i in range(sides):
                ang = 2 * math.pi * i / sides + (k * 0.35)
                rad = r * fr * (rr.uniform(0.85, 1.15) if k else 1.0)
                z = fz * h + (rr.uniform(-0.03, 0.03) if k else 0.0)
                row.append(bm.verts.new((math.cos(ang) * rad, math.sin(ang) * rad, z)))
            layer.append(row)
        apex = bm.verts.new((rr.uniform(-0.03, 0.03), rr.uniform(-0.03, 0.03), h))
        base_c = bm.verts.new((0, 0, 0))

        def paint(face, band):
            face.normal_update()
            lit = face.normal.x + face.normal.y > 0
            if band >= snow_from:
                c = a.hexcol(0xF4F7FA) if lit else a.hexcol(0xCFD9E4)
            elif band == snow_from - 1 and rr.random() < 0.3:
                c = a.hexcol(0xE6ECF1)  # ragged snow line
            else:
                c = a.hexcol(0x857A70) if lit else a.hexcol(0x5A504A)
            lin = tuple(a.srgb_to_linear(v) for v in c) + (1.0,)
            for loop in face.loops:
                loop[col] = lin

        for k in range(len(rings) - 1):
            for i in range(sides):
                j = (i + 1) % sides
                v00, v01, v10, v11 = layer[k][i], layer[k][j], layer[k + 1][i], layer[k + 1][j]
                paint(bm.faces.new((v00, v01, v11)), k)
                paint(bm.faces.new((v00, v11, v10)), k)
        for i in range(sides):
            j = (i + 1) % sides
            paint(bm.faces.new((layer[-1][i], layer[-1][j], apex)), len(rings) - 1)
            paint(bm.faces.new((layer[0][j], layer[0][i], base_c)), 0)
        mesh = bpy.data.meshes.new("crag")
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new("crag", mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = (x, y, 0)
        obj.rotation_euler = (0, 0, rnd.uniform(0, 6.28))
        return obj

    parts.append(crag(0, 0, 0.62, height, seed))
    parts.append(crag(0.36, -0.2, 0.36, height * 0.6, seed + 1))
    parts.append(crag(-0.3, 0.3, 0.3, height * 0.5, seed + 2))
    return parts


def all_tiles():
    """{name: builder} for every tile top (exported as tile_<name>)."""
    table = {}
    for kind in LAND:
        for n in range(1, 4):
            table[f"{kind}_{n}"] = (lambda k=kind, s=n: land_tile(k, 100 * s + len(k)))
        for n in range(1, 3):
            table[f"hills_{kind}_{n}"] = (lambda k=kind, s=n: land_tile(k, 500 + 100 * s + len(k), hills=True))
    for n in range(1, 3):
        table[f"mountain_{n}"] = (lambda s=n: mountain_tile(900 + s))
        table[f"marsh_{n}"] = (lambda s=n: marsh_tile(950 + s))
        table[f"ice_{n}"] = (lambda s=n: ice_tile(990 + s))
        for kind in WATER:
            table[f"{kind}_{n}"] = (lambda k=kind, s=n: water_tile(k, 1000 + s + len(k)))
    return table


def all_peaks():
    """Mountain peaks (exported as prop_peak_<n>)."""
    return {f"peak_{n}": (lambda s=n: peak(1200 + s)) for n in range(1, 4)}
