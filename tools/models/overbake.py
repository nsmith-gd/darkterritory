"""Over-baking a tools/blender character (ARCHITECTURE §8 note 58): the script stays the source of the game mesh, the
rig, the weights, the variants and the clips; a recipe runs it with its export held back, models a high-resolution
copy of each part over the game mesh (subdivided, displaced, dressed in the texture library), and bakes that down
into one atlas on the game mesh itself. tools/models/recipes/crew.py and cinder_hound.py use it.

    kit, g, arm, parts = overbake.hold("crew.py")
    highs = {name: overbake.high_of(parts[name], dress, shapes) ...}
    atlas = overbake.Atlas("crew", parts, ["body", "hat_cap"], kind_of_material)
    atlas.unwrap(boosts={...})
    atlas.bake({"body": highs["body"], ...})
    atlas.finish(base_linear, keep, source_ids=..., made=..., kit=kit, arm=arm)
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.path.join(os.path.dirname(HERE), "blender")
sys.path.insert(0, BLENDER)
import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import cook  # noqa: E402
import rig  # noqa: E402

RAY_FLAGS = ("visible_diffuse", "visible_glossy", "visible_shadow", "visible_transmission", "visible_volume_scatter")


def hold(script):
    """Runs tools/blender/<script> with its export held back. Returns (kit, the script's globals, the armature, the
    parts by name), the armature in its rest pose (the clips' last key is still on it): the pose to model and bake in."""
    real = rig.export
    held = {}
    rig.export = lambda path, kit: held.update(kit=kit)
    path = os.path.join(BLENDER, script)
    g = {"__name__": "overbake_source", "__file__": path}
    try:
        exec(compile(open(path).read(), path, "exec"), g)
    finally:
        rig.export = real
    kit = held["kit"]
    arm = kit.skeleton.rig
    parts = {o.name: o for o in bpy.data.objects if o.type == "MESH" and o.parent is arm}
    arm.data.pose_position = "REST"
    return kit, g, arm, parts


def mat_slots(o, prefix):
    return {i for i, m in enumerate(o.data.materials) if m is not None and m.name.startswith(prefix)}


# ----------------------------------------------------------------------------------------------------------------
# The high copy

def high_of(o, dress, shapes=None, dense=False):
    """The part's high copy, split by material. `dress(material) -> (library material, subdiv) | None` (None: not in the
    high copy); subdiv > 0 is Catmull-Clark that many levels (cloth, hide over a form), 0 keeps the edges bevelled (a
    buckle, a claw). `shapes` {kit material prefix: fn(positions, normals) -> metres along the normal} sculpts each.
    `dense`: `o` is already a high copy (a modelled union, tools/blender/crewbody.py): it's split and sculpted, not
    subdivided."""
    out = []
    used = {f.material_index for f in o.data.polygons}
    for mi, m in enumerate(o.data.materials):
        dressed = dress(m) if m is not None and mi in used else None
        if dressed is None:
            continue
        mat, subdiv = dressed
        me = o.data.copy()
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != mi], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        if not bm.faces:
            bm.free()
            continue
        bm.to_mesh(me)
        bm.free()
        h = bpy.data.objects.new(f"{o.name}_{m.name}_high", me)
        bpy.context.scene.collection.objects.link(h)
        h.matrix_world = o.matrix_world
        h.vertex_groups.clear()
        me.materials.clear()
        me.materials.append(mat)
        bpy.context.view_layer.objects.active = h
        if dense:
            pass
        elif subdiv:
            # A game mesh already subdivided once (rig.densify) needs a level less for the same high copy.
            mod = h.modifiers.new("sub", "SUBSURF")
            mod.levels = mod.render_levels = max(1, subdiv - (1 if o.get("dt_densified") else 0))
            bpy.ops.object.modifier_apply(modifier=mod.name)
        else:
            mod = h.modifiers.new("bevel", "BEVEL")
            mod.width, mod.segments, mod.limit_method = 0.0025, 2, "ANGLE"
            bpy.ops.object.modifier_apply(modifier=mod.name)
            mod = h.modifiers.new("sub", "SUBSURF")
            mod.subdivision_type = "SIMPLE"
            mod.levels = mod.render_levels = 2
            bpy.ops.object.modifier_apply(modifier=mod.name)
        for f in me.polygons:
            f.use_smooth = True
        fn = next((f for k, f in (shapes or {}).items() if m.name.startswith(k)), None)
        if fn is not None:
            displace(h, fn)
        h["dt_kind"] = m.name
        out.append(h)
    return out


def bell(x):
    return np.exp(-x * x)


def smooth01(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def fine(p, amount=0.0012, scale=70, seed=0):
    """A fine grain over a surface (weave, pores, grit)."""
    return amount * cook.noise_np(p, seed, scale)


def displace(o, fn):
    """Pushes every vertex along its normal by fn(positions Nx3, normals Nx3) metres."""
    me = o.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    no = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("normal", no)
    co, no = co.reshape(-1, 3), no.reshape(-1, 3)
    d = np.asarray(fn(co, no), np.float32)
    me.vertices.foreach_set("co", (co + no * d[:, None]).ravel())
    me.update()


# ----------------------------------------------------------------------------------------------------------------
# The atlas

class Atlas:
    """The baked parts joined into one mesh for the unwrap and the bakes (each face keeps its part and a kind as
    attributes), then split back into the parts the engine draws per variant. `kind(material) -> int` sorts the faces:
    0 plain, KEEP for faces that keep their own material and UVs (a lamp's lit glass, glowing cracks), any other int a
    class a recipe treats apart (a face, the hands: more texels, a gentler grade)."""

    KEEP = -1

    def __init__(self, name, parts, baked, kind, size=1024):
        self.name, self.parts, self.baked, self.size = name, parts, list(baked), size
        self.props = {n: {k: parts[n][k] for k in parts[n].keys()} for n in baked}
        self.kept = {}
        for pi, n in enumerate(baked):
            o = parts[n]
            kinds = []
            for f in o.data.polygons:
                m = o.data.materials[f.material_index]
                k = kind(m)
                if k == Atlas.KEEP:
                    self.kept.setdefault(m.name, m)
                kinds.append(k)
            o.data.attributes.new("dt_part", "INT", "FACE").data.foreach_set("value", [pi] * len(o.data.polygons))
            o.data.attributes.new("dt_kind", "INT", "FACE").data.foreach_set("value", kinds)
        # (Which material each face wore, by name: the join renumbers the slots, and a kept face keeps its own.)
        slot_names = {n: [m.name if m else "" for m in parts[n].data.materials] for n in baked}
        slot_of = []
        for n in baked:
            names = slot_names[n]
            slot_of += [names[f.material_index] for f in parts[n].data.polygons]
        bpy.ops.object.select_all(action="DESELECT")
        for n in baked:
            parts[n].select_set(True)
        bpy.context.view_layer.objects.active = parts[baked[0]]
        bpy.ops.object.join()
        self.low = parts[baked[0]]
        self.low.name = f"{name}_joined"
        nf = len(self.low.data.polygons)
        self.part_of = np.empty(nf, np.int32)
        self.low.data.attributes["dt_part"].data.foreach_get("value", self.part_of)
        self.kind_of = np.empty(nf, np.int32)
        self.low.data.attributes["dt_kind"].data.foreach_get("value", self.kind_of)
        # The join keeps faces in part order, each part's in its own order: the names line up.
        self.slot_name = np.array(slot_of if len(slot_of) == nf else [""] * nf, object)
        self.keep = self.kind_of == Atlas.KEEP

    def unwrap(self, boosts=None, special=None):
        """Smart-projects every face but the kept ones (and those `special` lays out itself: {kind: fn(bm face, uv
        layer)}), evens the islands' texel density, gives the kinds in `boosts` {kind: scale} more of it, and packs."""
        low = self.low
        low.data.uv_layers.active = low.data.uv_layers.new(name="baked")
        bpy.ops.object.select_all(action="DESELECT")
        low.select_set(True)
        bpy.context.view_layer.objects.active = low
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_mode(type="FACE")
        special = special or {}
        bm = bmesh.from_edit_mesh(low.data)
        for f in bm.faces:
            f.select = not self.keep[f.index] and int(self.kind_of[f.index]) not in special
        bmesh.update_edit_mesh(low.data)
        # 75 degrees and a tight margin: the islands come out bigger and pack closer (a creature's atlas went from 38-55%
        # used to 61-68%, the Car Hugger's from 23% to 53%: GDD §27's texel density for the same texture).
        bpy.ops.uv.smart_project(angle_limit=math.radians(75), island_margin=0.002, area_weight=0.0, scale_to_bounds=True)
        bm = bmesh.from_edit_mesh(low.data)
        uvl = bm.loops.layers.uv.active
        for f in bm.faces:
            fn = special.get(int(self.kind_of[f.index]))
            if fn is not None and not self.keep[f.index]:
                f.select = True
                fn(f, uvl)
        bmesh.update_edit_mesh(low.data)
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.average_islands_scale()
        bm = bmesh.from_edit_mesh(low.data)
        uvl = bm.loops.layers.uv.active
        for f in bm.faces:
            k = (boosts or {}).get(int(self.kind_of[f.index]), 1.0)
            if k != 1.0 and f.select:
                for loop in f.loops:
                    loop[uvl].uv *= k
        bmesh.update_edit_mesh(low.data)
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.pack_islands(rotate=True, margin=0.002 if self.size >= 2048 else 0.003)
        bpy.ops.object.mode_set(mode="OBJECT")
        low.data.uv_layers.remove(low.data.uv_layers["UVMap"])
        low.data.uv_layers["baked"].name = "UVMap"

    def bake(self, groups, cages=None, samples=24, height=1.8, hide=(), masks=None):
        """Bakes each group {name: (face mask over the joined mesh, highs)} from its own high copy only: colour, normal,
        occlusion and a height mask (z / `height`, emitted). A Cycles bake clears the whole image and only the colour
        pass leaves alpha where it didn't write, so each group bakes into images of its own, and its colour coverage
        masks all four into the atlas. `masks` {name: fn(positions Nx3) -> 0..1} are painted on the high copy and baked
        across too (where a recipe's grade goes: eye pits, rot, tar). A mask fn may take the high copy's kit material
        name as a second argument (paint that only goes on a face, not the hair over it), and may return Nx3: three
        masks in one bake, kept HxWx3. Sets self.maps {DIFFUSE, NORMAL, AO, EMIT: HxWx4, and each mask: HxW or HxWx3}
        and self.masks {group: HxW}."""
        S = self.size
        low = self.low
        bake_mat = bpy.data.materials.new(f"{self.name}_bake")
        bake_mat.use_nodes = True
        bake_tex = bake_mat.node_tree.nodes.new("ShaderNodeTexImage")
        scrap_mat = bpy.data.materials.new(f"{self.name}_scrap")
        scrap_mat.use_nodes = True
        scrap_tex = scrap_mat.node_tree.nodes.new("ShaderNodeTexImage")
        scrap_tex.image = bpy.data.images.new(f"{self.name}_scrap", 8, 8)
        scrap_mat.node_tree.nodes.active = scrap_tex
        low.data.materials.clear()
        low.data.materials.append(bake_mat)
        low.data.materials.append(scrap_mat)
        for flag in RAY_FLAGS:
            setattr(low, flag, False)
        for o in hide:
            o.hide_render = True
        zmat = _height_material(height)
        scene = bpy.context.scene
        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.use_denoising = False
        scene.cycles.samples = samples
        scene.render.bake.use_selected_to_active = True
        scene.render.bake.margin = 3
        all_highs = list(dict.fromkeys(h for _, hs in groups.values() for h in hs))  # (a high may bake in two groups)
        self.maps = {"NORMAL": np.tile(np.array([0.5, 0.5, 1.0, 1.0], np.float32), (S, S, 1)),
                     "AO": np.ones((S, S, 4), np.float32), "DIFFUSE": np.full((S, S, 4), 0.2, np.float32),
                     "EMIT": np.full((S, S, 4), 0.5, np.float32)}
        for mname in (masks or {}):
            self.maps[mname] = None
        mask_mat = _attribute_material("dt_mask")
        self.masks = {}
        for name, (faces, highs) in groups.items():
            low.data.polygons.foreach_set("material_index", np.where(faces & ~self.keep, 0, 1).astype(np.int32))
            low.data.update()
            for h in all_highs:
                h.hide_render = h not in highs
            scene.render.bake.cage_extrusion, scene.render.bake.max_ray_distance = (cages or {}).get(name, (0.02, 0.05))
            wrote = None
            for kind in ("DIFFUSE", "NORMAL", "AO", "EMIT"):
                if kind == "EMIT":
                    for h in highs:
                        h.data.materials.clear()
                        h.data.materials.append(zmat)
                img = bpy.data.images.new(f"{self.name}_{kind}_{name}", S, S, alpha=True)
                img.generated_color = (0, 0, 0, 0)
                img.colorspace_settings.name = "sRGB" if kind == "DIFFUSE" else "Non-Color"
                bake_tex.image = img
                bake_mat.node_tree.nodes.active = bake_tex
                bpy.ops.object.select_all(action="DESELECT")
                for h in highs:
                    h.select_set(True)
                low.select_set(True)
                bpy.context.view_layer.objects.active = low
                if kind == "NORMAL":
                    scene.render.bake.normal_space = "TANGENT"
                    bpy.ops.object.bake(type="NORMAL")
                elif kind == "DIFFUSE":
                    scene.render.bake.use_pass_direct = False
                    scene.render.bake.use_pass_indirect = False
                    scene.render.bake.use_pass_color = True
                    bpy.ops.object.bake(type="DIFFUSE")
                else:
                    bpy.ops.object.bake(type=kind)
                px = cook._image_array(img, S)
                if wrote is None:
                    wrote = px[..., 3] > 0.5
                self.maps[kind][wrote] = px[wrote]
                bpy.data.images.remove(img)
            for mname, fn in sorted((masks or {}).items()):
                for h in highs:
                    co = np.empty(len(h.data.vertices) * 3, np.float32)
                    h.data.vertices.foreach_get("co", co)
                    args = (co.reshape(-1, 3), h.get("dt_kind", "")) if fn.__code__.co_argcount >= 2 else (co.reshape(-1, 3),)
                    v = np.clip(np.asarray(fn(*args), np.float32), 0, 1)
                    if "dt_mask" in h.data.color_attributes:
                        h.data.color_attributes.remove(h.data.color_attributes["dt_mask"])
                    attr = h.data.color_attributes.new("dt_mask", "FLOAT_COLOR", "POINT")
                    rgba = np.ones((len(v), 4), np.float32)
                    rgba[:, :3] = v if v.ndim == 2 else v[:, None]
                    if self.maps[mname] is None:
                        self.maps[mname] = np.zeros((S, S, 3) if v.ndim == 2 else (S, S), np.float32)
                    attr.data.foreach_set("color", rgba.ravel())
                    h.data.materials.clear()
                    h.data.materials.append(mask_mat)
                img = bpy.data.images.new(f"{self.name}_{mname}_{name}", S, S, alpha=True)
                img.colorspace_settings.name = "Non-Color"
                bake_tex.image = img
                bake_mat.node_tree.nodes.active = bake_tex
                bpy.ops.object.select_all(action="DESELECT")
                for h in highs:
                    h.select_set(True)
                low.select_set(True)
                bpy.context.view_layer.objects.active = low
                bpy.ops.object.bake(type="EMIT")
                px = cook._image_array(img, S)
                if self.maps[mname] is not None:
                    self.maps[mname][wrote] = px[wrote][..., :3] if self.maps[mname].ndim == 3 else px[wrote][..., 0]
                bpy.data.images.remove(img)
            self.masks[name] = wrote
        cook.delete(all_highs)
        for o in hide:
            o.hide_render = False
        if os.environ.get("DT_BAKE_DEBUG"):
            for kind, arr in self.maps.items():
                if arr is None:
                    continue
                if arr.ndim == 2:
                    arr = np.dstack([arr, arr, arr, np.ones_like(arr)])
                elif arr.shape[-1] == 3:
                    arr = np.dstack([arr, np.ones_like(arr[..., 0])])
                cook._save(arr, os.path.join(cook.ROOT, "out", "review", f"bake-{self.name}_{kind.lower()}.png"))

    def base(self, soot=(0.018, 0.016, 0.014), crease=0.45, ao_floor=0.35, gentle=None):
        """The colour (linear) darkened by the occlusion and sooted in the creases; `gentle` (HxW bool) takes both
        softly (a face: the lips' line would soot into an open mouth)."""
        ao = self.maps["AO"][..., 0]
        if gentle is not None:
            ao = np.where(gentle, ao ** 0.35, ao)
        base = cook.srgb_to_lin(self.maps["DIFFUSE"][..., :3])
        base = base * (ao_floor + (1 - ao_floor) * ao)[..., None]
        k = np.clip((1 - ao) * 2.2, 0, 1) * crease * (np.where(gentle, 0.3, 1.0) if gentle is not None else 1.0)
        s = np.array(soot, np.float32)
        return base * (1 - k)[..., None] + s * k[..., None]

    def height(self, scale=1.8):
        return self.maps["EMIT"][..., 0] * scale

    def finish(self, base, kit, arm, source_ids=(), made=(), family="creature", split=None, rough=None):
        """Writes the atlas (`base` linear HxWx3), with `rough` (HxW, 0 glazed .. 1 matte; default 0.75 all over) its gloss,
        splits the parts back out with
        their names and extras, bakes the layers, and exports content/art/models/<name>.glb with the rig and clips.
        `split` {kind: suffix}: those faces draw the atlas under a material of their own, <name>_0.<suffix> (the same
        texture; the engine tells them apart by name: the crew's paint, tinted per player)."""
        S = self.size
        low = self.low
        bimg = bpy.data.images.new(f"{self.name}_base", S, S, alpha=True)
        rgba = np.ones((S, S, 4), np.float32)
        rgba[..., :3] = cook.lin_to_srgb(base)
        bimg.pixels.foreach_set(rgba[::-1].ravel())
        nimg = bpy.data.images.new(f"{self.name}_normal", S, S, alpha=False)
        nimg.colorspace_settings.name = "Non-Color"
        nimg.pixels.foreach_set(self.maps["NORMAL"][::-1].ravel())
        final = bpy.data.materials.new(f"{self.name}_final")
        final.use_nodes = True
        nt = final.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bimg
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
        ntex = nt.nodes.new("ShaderNodeTexImage")
        ntex.image = nimg
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        bsdf.inputs["Roughness"].default_value = 0.75
        if rough is not None:
            rimg = bpy.data.images.new(f"{self.name}_rough", S, S, alpha=False)
            rimg.colorspace_settings.name = "Non-Color"
            r = np.clip(np.asarray(rough, np.float32), 0, 1)
            rimg.pixels.foreach_set(np.dstack([r, r, r, np.ones_like(r)])[::-1].ravel())
            rtex = nt.nodes.new("ShaderNodeTexImage")
            rtex.image = rimg
            nt.links.new(rtex.outputs["Color"], bsdf.inputs["Roughness"])
        bsdf.inputs["Metallic"].default_value = 0.0
        # The joined mesh's slots: the atlas, then each kept material.
        kept = sorted(self.kept)
        split = dict(split or {})
        suffixes = sorted(set(split.values()))
        low.data.materials.clear()
        low.data.materials.append(final)
        for sfx in suffixes:
            a = final.copy()
            a.name = f"{self.name}_final.{sfx}"
            a["dt_alias"], a["dt_suffix"] = final.name, sfx
            low.data.materials.append(a)
        for n in kept:
            m = self.kept[n]
            m["dt_library"] = True
            low.data.materials.append(m)
        base_slot = 1 + len(suffixes)
        idx = [base_slot + kept.index(self.slot_name[i]) if self.keep[i]
               else (1 + suffixes.index(split[self.kind_of[i]]) if self.kind_of[i] in split else 0)
               for i in range(len(self.keep))]
        low.data.polygons.foreach_set("material_index", idx)
        for f in low.data.polygons:
            f.use_smooth = True
        for flag in RAY_FLAGS:
            setattr(low, flag, True)
        lows = []
        for pi, name in enumerate(self.baked):
            o = low.copy()
            o.data = low.data.copy()
            bpy.context.scene.collection.objects.link(o)
            bm = bmesh.new()
            bm.from_mesh(o.data)
            bm.faces.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if self.part_of[f.index] != pi], context="FACES")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            bm.to_mesh(o.data)
            bm.free()
            for a in ("dt_part", "dt_kind"):
                if a in o.data.attributes:
                    o.data.attributes.remove(o.data.attributes[a])
            used = sorted({f.material_index for f in o.data.polygons})
            for i in reversed(range(len(o.data.materials))):
                if i not in used:
                    o.data.materials.pop(index=i)
            for k in list(o.keys()):
                del o[k]
            for k, v in self.props[name].items():
                o[k] = v
            o.name = name
            o.data.name = f"{self.name}_{name}"
            lows.append(o)
        bpy.data.objects.remove(low, do_unlink=True)
        print(f"[dt] {self.name} parts baked", {o.name: len(o.data.polygons) for o in lows})
        layers = cook.bake_layers(self.name, lows, family=family, source_ids=list(source_ids), made=list(made), size=S)
        cook._merge_index(self.name, layers)
        # (Scraps the bake left: nothing but the rig and its parts may go in the file.)
        for o in [o for o in bpy.data.objects if o.type == "MESH" and o.parent is not arm]:
            bpy.data.objects.remove(o, do_unlink=True)
        arm.data.pose_position = "POSE"
        rig.export(os.path.join(cook.ROOT, "content", "art", "models", f"{self.name}.glb"), kit)


def _attribute_material(name):
    """Emits a colour attribute of the surface (a mask painted on the high copy)."""
    m = bpy.data.materials.new(f"overbake_{name}")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = name
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(attr.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], nt.nodes["Material Output"].inputs["Surface"])
    return m


def _height_material(height):
    zmat = bpy.data.materials.new("overbake_z")
    zmat.use_nodes = True
    znt = zmat.node_tree
    znt.nodes.remove(znt.nodes["Principled BSDF"])
    geo = znt.nodes.new("ShaderNodeNewGeometry")
    sep = znt.nodes.new("ShaderNodeSeparateXYZ")
    div = znt.nodes.new("ShaderNodeMath")
    div.operation = "DIVIDE"
    div.inputs[1].default_value = height
    emit = znt.nodes.new("ShaderNodeEmission")
    znt.links.new(geo.outputs["Position"], sep.inputs["Vector"])
    znt.links.new(sep.outputs["Z"], div.inputs[0])
    znt.links.new(div.outputs[0], emit.inputs["Color"])
    znt.links.new(emit.outputs["Emission"], znt.nodes["Material Output"].inputs["Surface"])
    return zmat
