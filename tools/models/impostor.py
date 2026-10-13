"""Impostors: a modelled thing seen far off drawn as crossed cards of itself (ARCHITECTURE §8 note 696; the corrupted trees'
distance copies, tools/models/recipes/corrupted_trees.py). A tree out past the modelled spruce's reach is a silhouette in
the fog, so it's drawn as two cards at right angles, each a picture of the tree from that side, rendered here from the
model itself in the library's own layers and tints: its colour (lit by nothing but its own hollows, so the engine's moon
and lamps light it), its normals seen from the card's side (so they do), and what of it glows. A kind's three trees
share one layer, six columns of it (each tree from two sides); a card is 8 triangles where the model is thousands.

  shoot(obj, columns, col)    -- render `obj` from its two sides into `columns` at `col` and `col + 1`
  bake(name, columns, what)   -- the columns as one layer, content/art/textures/models/<name>_0*.png, indexed
  cards(name, layer, view)    -- the crossed cards of one tree on that layer, as a prop

Deterministic: Cycles at a fixed seed and sample count, the same columns every build.
"""
from __future__ import annotations

import json
import math
import os

import bpy
import numpy as np
from mathutils import Vector

import cook

LIBRARY = os.path.join(cook.ROOT, "content", "art", "textures")
SIZE = 512                 # the layer (look.json layerSize)
SHOT = (320, 512)          # each side rendered at, then squeezed into its column
COLUMNS = 6                # a kind's three trees, two sides each
CARD_TRIANGLES = 8         # two quads, both faces

_library = None


def library():
    global _library
    if _library is None:
        _library = {e["name"]: e for e in json.load(open(os.path.join(LIBRARY, "index.json")))}
    return _library


def _image(layer, map="diffuse"):
    e = library().get(layer)
    if e is None or map not in e:
        return None
    name = f"lib_{layer}_{map}"
    img = bpy.data.images.get(name)
    if img is None:
        img = bpy.data.images.load(os.path.join(LIBRARY, e[map]))
        img.name = name
        if map != "diffuse":
            img.colorspace_settings.name = "Non-Color"
    return img


def _shade(mat, mode):
    """`mat` (a library material: its layer by name, its dt_tint) as an unlit shader for one pass: its colour darkened in
    its hollows, its normal seen from the camera, or whether it glows; cut where its layer is cut."""
    layer = mat.name.split(".")[0]
    e = library().get(layer, {})
    tint = list(mat.get("dt_tint", [1, 1, 1]))
    m = mat.copy()
    m.name = f"{mat.name}~{mode}"
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    tex = None
    img = _image(layer)
    if img is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.interpolation = "Linear"
    if mode == "colour":
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs[0].default_value = 1.0
        mul.inputs[7].default_value = (*tint, 1)
        if tex is not None:
            nt.links.new(tex.outputs["Color"], mul.inputs[6])
        else:
            mul.inputs[6].default_value = (0.5, 0.5, 0.5, 1)
        ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
        ao.inputs["Distance"].default_value = 0.6
        dim = nt.nodes.new("ShaderNodeMapRange")
        dim.inputs["To Min"].default_value = 0.5
        nt.links.new(ao.outputs["AO"], dim.inputs["Value"])
        shade = nt.nodes.new("ShaderNodeMix")
        shade.data_type = "RGBA"
        shade.blend_type = "MULTIPLY"
        shade.inputs[0].default_value = 1.0
        nt.links.new(mul.outputs[2], shade.inputs[6])
        nt.links.new(dim.outputs["Result"], shade.inputs[7])
        nt.links.new(shade.outputs[2], emit.inputs["Color"])
    elif mode == "normal":
        # World space: shoot() turns it onto the card's own axes (Cycles' camera space looks down +Z, not -Z).
        # A hanging card (lichen, leaves) faces whoever looks at it, as it's lit in the world (a card bends, it doesn't
        # turn its back): its sideways normals would light one side of the far card and leave the other to the cold sky.
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        half = nt.nodes.new("ShaderNodeVectorMath")
        half.operation = "MULTIPLY_ADD"
        half.inputs[1].default_value = (0.5, 0.5, 0.5)
        half.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(geo.outputs["Incoming" if layer.endswith("_card") else "Normal"], half.inputs[0])
        nt.links.new(half.outputs["Vector"], emit.inputs["Color"])
    else:
        # What glows: the material's own glow, or its layer's emissive mask (the spec map's B: a pod's lit cells, not its
        # veins), whichever's the more, as scene.frag takes it.
        glow = float(mat.get("dt_emissive", 0.0))
        emit.inputs["Color"].default_value = (glow, glow, glow, 1)
        spec = _image(layer, "spec")
        if spec is not None and tex is not None:
            mask = nt.nodes.new("ShaderNodeTexImage")
            mask.image = spec
            mask.interpolation = "Linear"
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(mask.outputs["Color"], sep.inputs["Color"])
            most = nt.nodes.new("ShaderNodeMath")
            most.operation = "MAXIMUM"
            most.inputs[1].default_value = glow
            nt.links.new(sep.outputs["Blue"], most.inputs[0])
            nt.links.new(most.outputs[0], emit.inputs["Color"])
    emit.inputs["Strength"].default_value = 1.0
    if tex is not None and e.get("alphaTest"):
        cut = nt.nodes.new("ShaderNodeMath")
        cut.operation = "GREATER_THAN"
        cut.inputs[1].default_value = 0.5
        nt.links.new(tex.outputs["Alpha"], cut.inputs[0])
        clear = nt.nodes.new("ShaderNodeBsdfTransparent")
        mix = nt.nodes.new("ShaderNodeMixShader")
        nt.links.new(cut.outputs[0], mix.inputs[0])
        nt.links.new(clear.outputs[0], mix.inputs[1])
        nt.links.new(emit.outputs[0], mix.inputs[2])
        nt.links.new(mix.outputs[0], out.inputs["Surface"])
    else:
        nt.links.new(emit.outputs[0], out.inputs["Surface"])
    return m


def views(obj):
    """The two sides a tree's cards are pictures from: looking +Y (the card in the X-Z plane, its picture's right +X) and
    looking -X (in the Y-Z plane, its right +Y); each as (rotation, right, forward)."""
    return [((math.radians(90), 0, 0), Vector((1, 0, 0)), Vector((0, 1, 0))),
            ((math.radians(90), 0, math.radians(90)), Vector((0, 1, 0)), Vector((-1, 0, 0)))]


def frame(obj, right):
    """Where a side's picture is: across `right` from `left` to `left + width`, up from `bottom` to `top` (m), the width
    the shot's aspect gives the height, centred on what's there."""
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    bottom = min(min(p.z for p in pts), 0.0) - 0.05
    top = max(p.z for p in pts) + 0.15
    height = top - bottom
    width = height * SHOT[0] / SHOT[1]
    lo, hi = min(p.dot(right) for p in pts), max(p.dot(right) for p in pts)
    centre = (lo + hi) / 2
    return centre - width / 2, width, bottom, top


def _render(path, mode):
    scene = bpy.context.scene
    scene.view_settings.view_transform = "Standard" if mode == "colour" else "Raw"
    scene.view_settings.look = "None"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = "Non-Color"  # the file's values as written: sRGB colour, raw normals
    a = np.array(img.pixels[:], np.float32).reshape(SHOT[1], SHOT[0], 4)[::-1].copy()
    bpy.data.images.remove(img)
    os.remove(path)
    return a


def shoot(obj, tmp):
    """`obj` from its two sides: for each, (its colour RGBA (sRGB), its normals (tangent space: x right, y down, z out),
    whether it glows, its frame)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.seed = 696
    scene.cycles.use_denoising = False
    scene.cycles.device = "CPU"
    scene.render.film_transparent = True
    scene.render.resolution_x, scene.render.resolution_y = SHOT
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("impostor")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.0
    cam_data = bpy.data.cameras.new("impostor")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("impostor", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    originals = [s.material for s in obj.material_slots]
    shots = []
    for rot, right, forward in views(obj):
        left, width, bottom, top = frame(obj, right)
        cam.rotation_euler = rot
        cam_data.ortho_scale = top - bottom
        cam.location = right * (left + width / 2) - forward * 60 + Vector((0, 0, (bottom + top) / 2))
        cam_data.clip_end = 200
        passes = {}
        for mode in ("colour", "normal", "glow"):
            for slot, m in zip(obj.material_slots, originals):
                slot.material = _shade(m, mode)
            passes[mode] = _render(os.path.join(tmp, f"{obj.name}_{mode}.png"), mode)
        for slot, m in zip(obj.material_slots, originals):
            slot.material = m
        # The world normal onto the card's axes: x its right, y down it, z out of it toward where it was shot from (a
        # card's back face, seen through, turned to face out too).
        n = passes["normal"][..., :3] * 2 - 1
        tangent = np.stack([n @ np.array(right), -n[..., 2], n @ -np.array(forward)], -1)
        tangent[tangent[..., 2] < 0] *= -1
        shots.append((passes["colour"], tangent, passes["glow"][..., 0], (left, width, bottom, top)))
    bpy.data.objects.remove(cam)
    return shots


def _squeeze(a, w):
    """Columns averaged down to `w` (area-weighted), rows kept."""
    h, W = a.shape[:2]
    edges = np.linspace(0, W, w + 1)
    c = np.concatenate([np.zeros((h, 1) + a.shape[2:], a.dtype), np.cumsum(a, axis=1)], axis=1)

    def at(x):
        i = np.clip(np.floor(x).astype(int), 0, W - 1)
        f = (x - i)[None, :]
        lo, hi = c[:, i], c[:, i + 1]
        if a.ndim == 3:
            f = f[..., None]
        return lo + (hi - lo) * f

    s = at(edges[1:]) - at(edges[:-1])
    width = (edges[1:] - edges[:-1])[None, :]
    return s / (width[..., None] if a.ndim == 3 else width)


def bake(name, shots, what, used):
    """A kind's trees' sides, each into its column of one 512 layer: content/art/textures/models/<name>_0.png (colour and
    its cut), _0_s (a little sheen; what glows), _0_n (its normals), indexed in index.models.json with where it came
    from. Returns the layer's name."""
    layer = f"{name}_0"
    colour = np.zeros((SIZE, SIZE, 4), np.float32)
    normal = np.zeros((SIZE, SIZE, 3), np.float32)
    normal[..., 2] = 1
    glow = np.zeros((SIZE, SIZE), np.float32)
    for k, (rgba, tangent, lit, _) in enumerate(shots):
        x0, x1 = round(k * SIZE / COLUMNS), round((k + 1) * SIZE / COLUMNS)
        w = x1 - x0
        a = rgba[..., 3]
        pre = _squeeze(np.concatenate([rgba[..., :3] * a[..., None], a[..., None]], -1), w)
        alpha = pre[..., 3]
        rgb = pre[..., :3] / np.maximum(alpha[..., None], 1e-4)
        nrm = _squeeze(tangent * a[..., None], w) / np.maximum(alpha[..., None], 1e-4)
        g = _squeeze(lit * a, w) / np.maximum(alpha, 1e-4)
        # A row of empty texels down each column's edge, so the mips don't bleed one tree into the next.
        alpha[:, :1] = 0
        alpha[:, -1:] = 0
        colour[:, x0:x1, :3] = rgb
        colour[:, x0:x1, 3] = (alpha > 0.5).astype(np.float32)
        normal[:, x0:x1] = nrm
        glow[:, x0:x1] = g
    # The colour bled out under the cut, so a far mip doesn't fringe the tree in black.
    solid = colour[..., 3] > 0.5
    rgb = colour[..., :3] * solid[..., None]
    have = solid.astype(np.float32)
    for _ in range(12):
        grown = np.zeros_like(rgb)
        count = np.zeros_like(have)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            grown += np.roll(np.roll(rgb * have[..., None], dy, 0), dx, 1)
            count += np.roll(np.roll(have, dy, 0), dx, 1)
        fill = (have == 0) & (count > 0)
        rgb[fill] = grown[fill] / count[fill][..., None]
        have = np.maximum(have, fill.astype(np.float32))
    colour[..., :3] = np.where(solid[..., None], colour[..., :3], rgb)
    n = normal / np.maximum(np.linalg.norm(normal, axis=-1, keepdims=True), 1e-4)
    n[~solid] = (0, 0, 1)
    spec = np.stack([np.full((SIZE, SIZE), 0.06), np.full((SIZE, SIZE), 0.22), np.clip(glow, 0, 1) * solid], -1).astype(np.float32)
    out = cook.TEXTURES
    os.makedirs(out, exist_ok=True)
    cook._save(np.clip(colour, 0, 1), os.path.join(out, f"{layer}.png"))
    cook._save(spec, os.path.join(out, f"{layer}_s.png"))
    cook._save(np.clip(n * np.array([0.5, 0.5, 0.5]) + 0.5, 0, 1), os.path.join(out, f"{layer}_n.png"))
    sources = [{"generator": "tools/models/recipes/corrupted_trees.py", "what": what, "author": "Dark Territory",
                "license": "CC0-1.0", "attribution": "Dark Territory (modelled in tools/models from the texture library)"}]
    seen = set()
    for lib in sorted(used):
        for s in library().get(lib, {}).get("sources", []):
            if s.get("repo") and (s["repo"], s.get("path")) not in seen:
                seen.add((s["repo"], s.get("path")))
                sources.append({"repo": s["repo"], "commit": s["commit"], "path": s.get("path"), "license": s["license"],
                                "attribution": f"{s.get('author', 'unknown')} ({s['license']}), via the library's {lib}"})
    entry = {"name": layer, "diffuse": f"models/{layer}.png", "spec": f"models/{layer}_s.png", "normal": f"models/{layer}_n.png",
             "size": SIZE, "tileMetres": None, "tiling": False, "family": "model", "alphaTest": True, "sources": sources}
    cook._merge_index(name, [entry])
    return layer


def cards(name, layer, col, frames):
    """One tree's distance copy: two cards crossed at its trunk, each its picture from that side (`col`, `col + 1` of
    `layer`), two-sided, as the prop content/art/models/props/<name>.glb. Returns its triangles."""
    import bmesh
    mat = bpy.data.materials.new(f"{layer}.card")
    mat["dt_library"] = True
    mat["dt_shine"] = 0.05
    mat["dt_emissive"] = 0.0
    mat["dt_glow"] = 0.0
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for k, ((_, right, forward), (left, width, bottom, top)) in enumerate(zip(views(None), frames)):
        u0, u1 = (col + k) / COLUMNS, (col + k + 1) / COLUMNS
        a, b = right * left, right * (left + width)
        corners = [a + Vector((0, 0, bottom)), b + Vector((0, 0, bottom)), b + Vector((0, 0, top)), a + Vector((0, 0, top))]
        uvs = [(u0, 0.0), (u1, 0.0), (u1, 1.0), (u0, 1.0)]
        for flip in (False, True):
            vs = [bm.verts.new(c) for c in corners]
            order = [0, 1, 2, 3]
            # The card's front faces the camera it was shot from (back along `forward`).
            if (corners[1] - corners[0]).cross(corners[3] - corners[0]).dot(-forward) < 0:
                order = order[::-1]
            if flip:
                order = order[::-1]
            f = bm.faces.new([vs[i] for i in order])
            f.smooth = False
            for loop, i in zip(f.loops, order):
                loop[uv].uv = uvs[i]
    mesh = bpy.data.meshes.new(name)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    tris = len(bm.faces)
    bm.free()
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    path = cook.rig_and_export(name, [obj])
    print(f"[dt] {name}: {tris} tris, crossed cards on {layer} -> {os.path.relpath(path, cook.ROOT)}")
    return tris
