"""Modelling things from scratch at high resolution, to be baked down (cook.bake_down) the way the scans are.

For the game's own objects no free model exists for (the crew's stores, freight, the cab's controls): each is built
here as a high-poly model, bevelled, planked, nailed and stencilled, wearing the texture library's own maps projected
onto it, and then baked down onto a game mesh that wears all that as its diffuse, normal and occlusion. The library
stays the one look: a crate's boards are the library's wood_crate at the library's scale, only now with real edges.
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

import cook

LIBRARY = os.path.join(cook.ROOT, "content", "art", "textures")
_mats = {}
USED = set()  # the library layers a model has worn (its provenance)
LOW = []  # the game mesh modelled alongside: each board, strap and pipe again, plain (cook.bake_down(low=make.LOW))


def lib(layer, scale=1.0, tint=(1, 1, 1), rough=0.7, metal=0.0):
    """A material wearing the library's `layer` (its diffuse and normal maps), box-projected in object space at
    `scale` repeats a metre, so any shape takes the map at the library's own size. Tinted by `tint`."""
    USED.add(layer)
    key = (layer, scale, tuple(tint), rough, metal)
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new(f"lib_{layer}")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    coord = nt.nodes.new("ShaderNodeTexCoord")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (scale, scale, scale)
    nt.links.new(coord.outputs["Object"], mapping.inputs["Vector"])
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(LIBRARY, f"{layer}.png"), check_existing=True)
    tex.projection = "BOX"
    tex.projection_blend = 0.25
    nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    nt.links.new(tex.outputs["Color"], mix.inputs[6])
    mix.inputs[7].default_value = (*tint, 1)
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    _mats[key] = m
    return m


def flat(name, colour, rough=0.6, metal=0.0):
    """A plain material: paint, stencil ink, rubber."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    return m


def _finish(o, material, bevel, segments, name):
    o.name = name
    o.data.materials.clear()
    o.data.materials.append(material)
    if bevel > 0:
        mod = o.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for f in o.data.polygons:
        f.use_smooth = True
    return o


def _low(o, name):
    """The plain game-mesh twin of a part just made (made before its bevel)."""
    c = o.copy()
    c.data = o.data.copy()
    c.name = name + "_low"
    bpy.context.scene.collection.objects.link(c)
    LOW.append(c)
    return c


def box(centre, half, material, bevel=0.006, segments=2, name="box", rot=None, low=True):
    """A box with bevelled edges (a board, a band of strap iron), centred, `rot` a Matrix about its centre; and its
    plain twin in LOW."""
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.view_layer.objects.active
    o.data.transform(Matrix.Diagonal((half[0] * 2, half[1] * 2, half[2] * 2, 1)))
    if rot is not None:
        o.data.transform(rot)
    o.data.transform(Matrix.Translation(Vector(centre)))
    o.data.materials.clear()
    o.data.materials.append(material)
    if low:
        _low(o, name)
    return _finish(o, material, bevel, segments, name)


def low_box(centre, half, material, name="shell"):
    """Only a game-mesh box (the outside of a crate the boards are baked onto)."""
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.view_layer.objects.active
    o.data.transform(Matrix.Translation(Vector(centre)) @ Matrix.Diagonal((half[0] * 2, half[1] * 2, half[2] * 2, 1)))
    o.data.materials.append(material)
    o.name = name + "_low"
    LOW.append(o)
    return o


def cyl(a, b, r, material, n=16, bevel=0.003, name="cyl", r1=None, low=8):
    """A cylinder (or cone, with r1) from a to b; its twin in LOW with `low` sides (0: none, too small to keep)."""
    a, b = Vector(a), Vector(b)
    place = Matrix.Translation((a + b) / 2) @ Vector((0, 0, 1)).rotation_difference(b - a).to_matrix().to_4x4()
    r1 = r if r1 is None else r1
    if low:
        bpy.ops.mesh.primitive_cone_add(vertices=low, radius1=r, radius2=r1, depth=(b - a).length)
        t = bpy.context.view_layer.objects.active
        t.data.transform(place)
        t.data.materials.append(material)
        t.name = name + "_low"
        LOW.append(t)
    bpy.ops.mesh.primitive_cone_add(vertices=n, radius1=r, radius2=r1, depth=(b - a).length)
    o = bpy.context.view_layer.objects.active
    o.data.transform(place)
    return _finish(o, material, bevel, 1, name)


def torus(centre, axis, major, minor, material, n=24, m=8, name="ring", low=(10, 4)):
    place = Matrix.Translation(Vector(centre)) @ Vector((0, 0, 1)).rotation_difference(Vector(axis)).to_matrix().to_4x4()
    if low:
        bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=low[0], minor_segments=low[1])
        t = bpy.context.view_layer.objects.active
        t.data.transform(place)
        t.data.materials.append(material)
        t.name = name + "_low"
        LOW.append(t)
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=n, minor_segments=m)
    o = bpy.context.view_layer.objects.active
    o.data.transform(place)
    return _finish(o, material, 0, 1, name)


def nail(at, normal, material, r=0.006, name="nail"):
    """A nail head proud of a board: a flattened dome."""
    n = Vector(normal).normalized()
    return cyl(Vector(at) - n * 0.002, Vector(at) + n * 0.003, r, material, n=8, bevel=0.002, name=name, r1=r * 0.7, low=0)


def stencil(text, at, normal, up, size, material, name="stencil", depth=0.0008):
    """Stencilled lettering, laid on a face a hair proud of it (it bakes into the diffuse as paint)."""
    bpy.ops.object.text_add()
    t = bpy.context.view_layer.objects.active
    t.data.body = text
    t.data.align_x = "CENTER"
    t.data.align_y = "CENTER"
    t.data.size = size
    t.data.extrude = depth
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.view_layer.objects.active
    n, u = Vector(normal).normalized(), Vector(up).normalized()
    r = u.cross(n)
    basis = Matrix((r, u, n)).transposed().to_4x4()
    o.data.transform(Matrix.Translation(Vector(at) + n * 0.0015) @ basis)
    o.matrix_world = Matrix.Identity(4)
    return _finish(o, material, 0, 1, name)


def planks(lo, hi, axis, count, material, gap=0.004, bevel=0.005, name="plank", wobble=0.0, seed=0, low=False):
    """A face of boards filling the box lo..hi, `count` boards along `axis` (0 x, 1 y, 2 z), each with a small gap
    and bevelled edges, a little uneven. (No game-mesh twins by default: a face of boards bakes onto a shell.)"""
    lo, hi = Vector(lo), Vector(hi)
    out = []
    step = (hi[axis] - lo[axis]) / count
    for i in range(count):
        a, b = lo.copy(), hi.copy()
        a[axis] = lo[axis] + i * step + gap / 2
        b[axis] = lo[axis] + (i + 1) * step - gap / 2
        c = (a + b) / 2
        h = (b - a) / 2
        if wobble:
            c += Vector((cook.noise3(c, seed + i, 7), cook.noise3(c, seed + i + 50, 7), cook.noise3(c, seed + i + 90, 7))) * wobble
        out.append(box(c, h, material, bevel=bevel, name=f"{name}_{i}", low=low))
    return out


def join(objs, name):
    return cook.join([o for o in objs if o is not None], name)


def centre_on_origin(objs):
    """Moves things (and their game-mesh twins) so their bounds are centred on the origin (a physics body's centre)."""
    lo, hi = cook.bounds(objs)
    cook.move(objs + LOW, tuple(-(lo + hi) / 2))


def provenance(recipe, what):
    """Where a model made here came from: this recipe (Dark Territory's own work, CC0), and the pinned sources of every
    library layer it wore (their photographs and scans, as content/art/textures/index.json records them)."""
    import json
    library = {e["name"]: e for e in json.load(open(os.path.join(LIBRARY, "index.json")))}
    out = [{"generator": f"tools/models/recipes/{recipe}.py", "what": what, "author": "Dark Territory",
            "license": "CC0-1.0", "attribution": "Dark Territory (modelled in tools/models from the texture library)"}]
    seen = set()
    for layer in sorted(USED):
        for s in library.get(layer, {}).get("sources", []):
            if s.get("repo") and (s["repo"], s.get("path")) not in seen:
                seen.add((s["repo"], s.get("path")))
                out.append({"repo": s["repo"], "commit": s["commit"], "path": s.get("path"), "license": s["license"],
                            "attribution": f"{s.get('author', 'unknown')} ({s['license']}), via the library's {layer}"})
    return out
