"""
Shared helpers for building Crucible of Ages assets in Blender (4.2 LTS or newer).

Conventions (the exporter and the game rely on them):
  * Blender units = game units. A map hex has a corner radius of 1.0.
  * Z is up and models face -Y (Blender's "front" view). The ground is z = 0.
  * Colour lives in a face-corner colour attribute named "Col" (flat colours, low-poly look), stored
    linear like any Blender colour attribute (so vertex painting in Blender just works); palette
    values below are sRGB and converted when painted, and the exporter converts back.
  * Parts painted TEAM (pure magenta) or TEAM_DARK (dark magenta) are recoloured with the owner's
    colour in game.
  * Every exported model is one mesh object named <category>_<name> (unit_sword, prop_conifer,
    city_house...) inside the collection of the same category.
"""
import math
import random

import bpy
import bmesh
from mathutils import Euler, Matrix, Vector

TEAM = (1.0, 0.0, 1.0)
TEAM_DARK = (0.5, 0.0, 0.5)

# Palette shared with the procedural art in View/Art (sRGB).
def hexcol(h):
    return ((h >> 16 & 255) / 255.0, (h >> 8 & 255) / 255.0, (h & 255) / 255.0)

SKIN = hexcol(0xD9A77C)
CLOTH = hexcol(0x4A3F36)
LEATHER = hexcol(0x6B4A2E)
STEEL = hexcol(0xB8BEC6)
IRON = hexcol(0x4A4E55)
WOOD = hexcol(0x8A5E3A)
WOOD_DARK = hexcol(0x5E3E26)
HORSE = hexcol(0x7A4E2E)
HORSE_DARK = hexcol(0x4E3220)
CANVAS = hexcol(0xEDE6D2)
OLIVE = hexcol(0x5C6B3A)
NAVY = hexcol(0x6B7784)
GOLD = hexcol(0xE3B94F)
PLINTH = hexcol(0x2A2A30)
STRAW = hexcol(0xD6B25E)
STONE = hexcol(0xA8A198)
STONE_DARK = hexcol(0x7E776F)
PLASTER = hexcol(0xE8DDC4)
ROOF = hexcol(0xB4553A)
PINE = hexcol(0x2C5A36)
LEAF = hexcol(0x4E8A36)
LEAF_LIGHT = hexcol(0x78B04E)
JUNGLE = hexcol(0x2F7A34)
BARK = hexcol(0x6A4A30)
ROCK = hexcol(0x7C7068)
GLASS = hexcol(0x7FD8E0)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)


def collection(name):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    return col


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def linear_to_srgb(c):
    c = max(0.0, min(1.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def _paint(obj, color):
    mesh = obj.data
    attr = mesh.color_attributes.get("Col") or mesh.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    rgba = (srgb_to_linear(color[0]), srgb_to_linear(color[1]), srgb_to_linear(color[2]), 1.0)
    for i in range(len(attr.data)):
        attr.data[i].color = rgba


def _finish(obj, color, loc, rot, bevel, segments):
    obj.location = loc
    obj.rotation_euler = Euler([math.radians(a) for a in rot], "XYZ")
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
    _paint(obj, color)
    return obj


def _new(bm, name):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def box(size, color, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, segments=1, taper=None):
    """Box of full size (sx, sy, sz) centred on loc. taper=(tx, ty) scales the top face."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z *= size[2]
        if taper and v.co.z > 0:
            v.co.x *= taper[0]
            v.co.y *= taper[1]
    return _finish(_new(bm, "box"), color, loc, rot, bevel, segments)


def cylinder(radius, depth, color, loc=(0, 0, 0), rot=(0, 0, 0), sides=8, top=None, bevel=0.0, segments=1, cap=True):
    """Cylinder/frustum along local Z from -depth/2 to +depth/2; top = top radius (0 makes a cone)."""
    bm = bmesh.new()
    top = radius if top is None else top
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=sides,
                          radius1=radius, radius2=max(top, 0.0), depth=depth)
    return _finish(_new(bm, "cyl"), color, loc, rot, bevel, segments)


def sphere(radius, color, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), subdiv=1, uv=None, jitter=0.0, seed=0):
    """Low-poly sphere: an icosphere (subdiv) or a UV sphere (uv=(segments, rings))."""
    bm = bmesh.new()
    if uv:
        bmesh.ops.create_uvsphere(bm, u_segments=uv[0], v_segments=uv[1], radius=radius)
    else:
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    rnd = random.Random(seed)
    for v in bm.verts:
        if jitter:
            v.co *= 1.0 + rnd.uniform(-jitter, jitter)
        v.co.x *= scale[0]
        v.co.y *= scale[1]
        v.co.z *= scale[2]
    return _finish(_new(bm, "sph"), color, loc, rot, 0.0, 1)


def poly(verts, faces, color, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, segments=1):
    """Arbitrary mesh from vertices and faces (counter-clockwise seen from outside)."""
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in verts]
    for f in faces:
        bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _finish(_new(bm, "poly"), color, loc, rot, bevel, segments)


def torus_arc(major, minor, start_deg, end_deg, color, loc=(0, 0, 0), rot=(0, 0, 0), steps=8, sides=5):
    """A bent rod (bows, arches): a tube along an arc in the XZ plane."""
    bm = bmesh.new()
    rings = []
    for i in range(steps + 1):
        a = math.radians(start_deg + (end_deg - start_deg) * i / steps)
        centre = Vector((math.cos(a) * major, 0, math.sin(a) * major))
        radial = Vector((math.cos(a), 0, math.sin(a)))
        ring = []
        for k in range(sides):
            b = 2 * math.pi * k / sides
            ring.append(bm.verts.new(centre + radial * math.cos(b) * minor + Vector((0, 1, 0)) * math.sin(b) * minor))
        rings.append(ring)
    for i in range(steps):
        for k in range(sides):
            k2 = (k + 1) % sides
            bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _finish(_new(bm, "arc"), color, loc, rot, 0.0, 1)


def hex_ring(outer, inner, height, color, z=0.0, sides=6, phase_deg=30):
    """A flat ring (no overlap with what's inside it) raised by height."""
    verts, faces = [], []
    for k in range(sides):
        a = math.radians(phase_deg + k * 360 / sides)
        for r in (outer, inner):
            verts.append((math.cos(a) * r, math.sin(a) * r, z))
            verts.append((math.cos(a) * r, math.sin(a) * r, z + height))
    for k in range(sides):
        j = (k + 1) % sides
        o0, o1, i0, i1 = 4 * k, 4 * j, 4 * k + 2, 4 * j + 2
        faces.append((o0 + 1, o1 + 1, i1 + 1, i0 + 1))  # top
        faces.append((o0, o1, o1 + 1, o0 + 1))          # outer wall
        faces.append((i1, i0, i0 + 1, i1 + 1))          # inner wall
    return poly(verts, faces, color)


def hex_plinth(radius=0.34, height=0.035):
    """The dark hex base every land model stands on, with an owner-coloured rim."""
    base = cylinder(radius, height, PLINTH, loc=(0, 0, height / 2), sides=6, top=radius * 0.95, rot=(0, 0, 30))
    rim = hex_ring(radius * 0.93, radius * 0.8, 0.005, TEAM, z=height)
    return [base, rim]


def group(parts, offset=(0, 0, 0), rot_z=0.0, scale=1.0):
    """Moves, turns and scales a list of parts as one unit (about the origin)."""
    m = Matrix.Translation(offset) @ Matrix.Rotation(math.radians(rot_z), 4, "Z") @ Matrix.Scale(scale, 4)
    for p in parts:
        # matrix_basis is rebuilt from location/rotation/scale immediately (matrix_world only after an update).
        p.matrix_basis = m @ p.matrix_basis
    return parts


def join(parts, name, category):
    """Applies modifiers, joins the parts into one flat-shaded mesh object and files it by category."""
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    color_layer = None
    for p in parts:
        ev = p.evaluated_get(deps)
        mesh = ev.to_mesh()
        mesh.transform(p.matrix_world)
        tmp = bmesh.new()
        tmp.from_mesh(mesh)
        ev.to_mesh_clear()
        src = tmp.loops.layers.float_color.get("Col")
        # Copy geometry and colours into the combined bmesh.
        color_layer = color_layer or bm.loops.layers.float_color.new("Col")
        vmap = {v: bm.verts.new(v.co) for v in tmp.verts}
        for f in tmp.faces:
            try:
                nf = bm.faces.new([vmap[v] for v in f.verts])
            except ValueError:
                continue  # duplicate face after bevel: skip
            for nl, l in zip(nf.loops, f.loops):
                nl[color_layer] = l[src] if src else (1, 0, 1, 1)
        tmp.free()
    for p in parts:
        bpy.data.objects.remove(p, do_unlink=True)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for poly_ in mesh.polygons:
        poly_.use_smooth = False
    mesh.color_attributes.active_color = mesh.color_attributes["Col"]
    obj = bpy.data.objects.new(name, mesh)
    collection(category).objects.link(obj)
    obj.data.materials.append(vertex_color_material())
    return obj


def vertex_color_material():
    """A material that shows the "Col" attribute, for viewing and rendering in Blender."""
    mat = bpy.data.materials.get("CrucibleVertexColor")
    if mat:
        return mat
    mat = bpy.data.materials.new("CrucibleVertexColor")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    attr = nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    mat.node_tree.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    return mat
