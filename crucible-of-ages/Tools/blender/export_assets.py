"""
Exports every model in the open .blend to the game.

    blender -b crucible_assets.blend -P Tools/blender/export_assets.py            (from the repo root)
    blender -b crucible_assets.blend -P Tools/blender/export_assets.py -- --render (also render previews)

Each mesh object named <category>_<name> in a collection called unit / prop / city / tile is written to
UnityProject/Assets/Resources/Art/<category>_<name>.bytes: "CRM1", a little-endian int32 triangle
count, then per vertex float32 x, y, z (Unity axes: x right, y up, z forward) and uint8 r, g, b
(sRGB). Blender's -Y (front) becomes Unity's +Z, and winding is flipped for Unity's left-handed
space. Magenta (team) colours pass through; the game swaps in the owner's colour.
"""
import os
import struct
import sys

import bpy
import bmesh

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
import crucible_art as art  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(REPO, "UnityProject", "Assets", "Resources", "Art")
PREVIEW_DIR = os.path.join(REPO, "docs", "images")
CATEGORIES = ("unit", "prop", "city", "tile")


def export_object(obj, path):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    mesh = ev.to_mesh()
    mesh.transform(obj.matrix_world)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    ev.to_mesh_clear()
    bmesh.ops.triangulate(bm, faces=bm.faces)
    col = bm.loops.layers.float_color.get("Col")
    data = bytearray(b"CRM1")
    data += struct.pack("<i", len(bm.faces))
    for face in bm.faces:
        loops = list(face.loops)
        for loop in (loops[0], loops[2], loops[1]):  # flip winding for Unity
            v = loop.vert.co
            c = loop[col] if col else (1.0, 0.0, 1.0, 1.0)
            data += struct.pack("<fff", -v.x, v.z, -v.y)
            data += bytes(int(round(art.linear_to_srgb(c[i]) * 255)) for i in range(3))
    bm.free()
    with open(path, "wb") as f:
        f.write(data)
    return len(data)


def export_all():
    os.makedirs(OUT_DIR, exist_ok=True)
    count = 0
    for cat in CATEGORIES:
        col = bpy.data.collections.get(cat)
        if col is None:
            continue
        for obj in col.objects:
            if obj.type != "MESH" or not obj.name.startswith(cat + "_"):
                continue
            size = export_object(obj, os.path.join(OUT_DIR, obj.name + ".bytes"))
            count += 1
            print(f"  {obj.name}: {size // 1024} KB")
    print(f"Exported {count} models to {OUT_DIR}")


def render_previews(cols=7, spacing=0.95, size=(1400, 820), samples=24):
    """Lays each category out in a grid and renders a preview sheet with Cycles (CPU, headless-safe)."""
    import math
    from mathutils import Vector
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.get("Preview") or bpy.data.worlds.new("Preview")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.42, 0.55, 0.36, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    scene.world = world

    sun = bpy.data.objects.get("PreviewSun")
    if sun is None:
        sun = bpy.data.objects.new("PreviewSun", bpy.data.lights.new("PreviewSun", "SUN"))
        scene.collection.objects.link(sun)
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(35))

    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        scene.collection.objects.link(cam)
    scene.camera = cam

    ground = bpy.data.objects.get("PreviewGround")
    if ground is None:
        ground = art.box((40, 40, 0.02), (0.45, 0.6, 0.35), loc=(0, 0, -0.01))
        ground.name = "PreviewGround"
        ground.data.materials.append(art.vertex_color_material())

    os.makedirs(PREVIEW_DIR, exist_ok=True)
    for cat in CATEGORIES:
        col = bpy.data.collections.get(cat)
        objs = [o for o in (col.objects if col else []) if o.type == "MESH"]
        if not objs:
            continue
        saved = {o.name: o.location.copy() for o in objs}
        cols_here = min(max(cols, math.ceil(math.sqrt(len(objs) * size[0] / size[1]))), len(objs))  # wider sheets for long lists
        spacing_here = {"unit": spacing, "prop": spacing * 0.9, "city": spacing * 1.15, "tile": spacing * 1.9}.get(cat, spacing)
        rows = (len(objs) + cols_here - 1) // cols_here
        for i, o in enumerate(sorted(objs, key=lambda o: o.name)):
            o.location = Vector((((i % cols_here) - (cols_here - 1) / 2) * spacing_here, ((i // cols_here) - (rows - 1) / 2) * spacing_here * 1.1, 0))
            o.rotation_euler = (0, 0, math.radians(-30))  # three-quarter view, as in the game
            if cat == "tile":
                o.location.z = 0.06  # tile tops dip below zero: lift them off the preview ground
        # Hide the other categories.
        for other in CATEGORIES:
            oc = bpy.data.collections.get(other)
            if oc:
                oc.hide_render = other != cat
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = max(cols_here * spacing_here * 1.1, rows * spacing_here * 0.9 * size[0] / size[1])
        cam.location = Vector((0, -8, 7.2))
        cam.rotation_euler = (math.radians(48), 0, 0)
        # Show team parts in a player colour (blue) instead of the magenta marker.
        restore = []
        for o in objs:
            attr = o.data.color_attributes.get("Col")
            for d in attr.data if attr else []:
                c = d.color
                if c[1] < 0.02 and abs(c[0] - c[2]) < 0.02 and c[0] > 0.1:
                    restore.append((d, tuple(c)))
                    k = c[0]
                    d.color = (0.033 * k, 0.17 * k, 0.89 * k, 1)
        scene.render.filepath = os.path.join(PREVIEW_DIR, f"blender-{cat}.png")
        bpy.ops.render.render(write_still=True)
        print("Rendered", scene.render.filepath)
        for d, c in restore:
            d.color = c
        for o in objs:
            o.location = saved[o.name]
            o.rotation_euler = (0, 0, 0)
    for oc in CATEGORIES:
        c = bpy.data.collections.get(oc)
        if c:
            c.hide_render = False
    for name in ("PreviewGround",):
        o = bpy.data.objects.get(name)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)


if __name__ == "__main__":
    export_all()
    if "--render" in sys.argv:
        render_previews()
