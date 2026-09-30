"""The crew figure (GDD §29), as tools/models/recipes/crew.py builds it and its variants reuse it (ARCHITECTURE §8
note 58): tools/blender/crew.py run with its export held back (its mesh, rig, weights, variants and clips), the egg of a
head swapped for Lee Perry-Smith's scan, a high-resolution copy of every part modelled over the game mesh and dressed
in the texture library, baked by group into one 1024 atlas and sooted.

    crewfigure.build("crew", crewfigure.Style())            # recipes/crew.py
    crewfigure.build("husk", crewfigure.Style(head=..., dress={...}, shapes={...}, masks={...}, grade=...))

A Style's hooks: `head(objs, centre)` deforms the scan after its mouth is closed; `dress` overrides DRESS entries (kit
material prefix: (layer, repeats a metre, tint, roughness, subdivision)); `shapes` adds to a kit material's sculpt (its
displacement, metres along the normal); `masks` are painted on the high copy and baked (overbake.Atlas.bake);
`grade(base, atlas, face)` has the last word on the atlas's colour.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

import cook
import overbake
from overbake import bell, fine, smooth01


class Style:
    def __init__(self, head=None, dress=None, shapes=None, masks=None, grade=None, preview="CREW_PREVIEW",
                 what="the crew's clothes and kit, modelled over tools/blender/crew.py", lamp=True, mask=None, figure="helm",
                 gear=None, views=None):
        self.head, self.dress, self.shapes = head, dress or {}, shapes or {}
        self.masks, self.grade, self.preview, self.what = masks or {}, grade, preview, what
        # The chest lamp lit (crew_atlas's glass, kept as a pure light), or dead and baked with the rest (a dress entry
        # for "crew_atlas.lamp" then says what it's made of).
        self.lamp = lamp
        # The respirator and goggles over the scan's face: "on", "torn" (hanging loose at the collar, the goggles gone), or
        # None. Only for the bare figure: the crew's own heads are the helm.
        self.mask = mask
        # "helm": the crew as they play (tools/blender/crew.py's smokebox helm, the tank on the back, the paint in the
        # player's colour); "bare": the figure bare-headed in a cap or a helmet, Lee Perry-Smith's scan for its face.
        self.figure = figure
        # Concept headgear (tools/models/concepts): gear(tip, head_centre, make) -> high parts, previewed on the figure.
        self.gear = gear
        # The preview's views, [(name, direction, target, distance)], in place of the default five.
        self.views = views


def _shell(centre, radii, keep, material, segments, rings, name):
    """An ellipsoid's front (the respirator's shell): a sphere, scaled, the faces behind `keep` (a y) cut away."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1.0)
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.transform(Matrix.Translation(Vector(centre)) @ Matrix.Diagonal((*radii, 1.0)))
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().y < keep], context="FACES")
    bm.to_mesh(o.data)
    bm.free()
    o.data.materials.clear()
    o.data.materials.append(material)
    for f in o.data.polygons:
        f.use_smooth = True
    return o


def respirator(how, tip, make):
    """The crew's face kit on the scan's landmarks (`tip`, the nose): a leather half-mask over the nose and mouth with its
    filter canister and straps, and smoked goggles on a band round the head under the cap. `how` "torn": the mask hangs
    at the collar by one strap and the goggles are gone. Returns (high parts, game-mesh parts)."""
    leather = make.lib("leather", 8.0, (0.3, 0.26, 0.23), 0.55)
    rubber = make.flat("mask_rubber", (0.03, 0.028, 0.027), rough=0.6)
    brass = make.lib("brass", 8.0, (0.8, 0.75, 0.6), 0.35)
    tin = make.lib("paint_olive", 6.0, (0.5, 0.52, 0.42), 0.5)
    glass = make.flat("goggle_glass", (0.02, 0.022, 0.024), rough=0.08)
    before = len(make.LOW)
    high = []
    m_c = Vector((0, tip.y - 0.03, tip.z - 0.035))
    high.append(_shell(m_c, (0.052, 0.047, 0.054), m_c.y - 0.006, leather, 32, 16, "mask_shell"))
    make.LOW.append(_shell(m_c + Vector((0, 0.002, 0)), (0.053, 0.048, 0.055), m_c.y - 0.006, leather, 14, 8, "mask_shell_low"))
    # Its rim, rolled rubber where it seals on the face; the canister out of its front, its grille; the exhale valve.
    high.append(make.torus(m_c + Vector((0, -0.004, 0)), (0, 1, 0), 0.051, 0.005, rubber, n=32, m=6, name="mask_rim", low=None))
    can_a = m_c + Vector((0, 0.04, -0.012))
    can_b = m_c + Vector((0, 0.085, -0.03))
    high.append(make.cyl(can_a, can_b, 0.028, tin, n=24, bevel=0.004, name="canister", low=10))
    high.append(make.torus(can_b, (can_b - can_a).normalized(), 0.022, 0.004, brass, n=20, m=5, name="grille", low=None))
    for k in range(4):
        a = k * math.pi / 4
        d = Vector((math.cos(a), 0, math.sin(a))) * 0.02
        high.append(make.cyl(can_b + d, can_b - d, 0.0025, brass, n=6, bevel=0, name="grille_bar", low=0))
    high.append(make.cyl(m_c + Vector((0.03, 0.035, -0.03)), m_c + Vector((0.034, 0.046, -0.036)), 0.011, rubber, n=14,
                         name="valve", low=6))
    # The straps from the mask's sides back round the head.
    for sx in (-1, 1):
        high.append(make.cyl(m_c + Vector((sx * 0.05, -0.01, 0.012)), Vector((sx * 0.085, 0.0, tip.z - 0.005)), 0.006, leather,
                             n=8, bevel=0, name="strap", low=4))
        high.append(make.cyl(m_c + Vector((sx * 0.05, -0.01, -0.03)), Vector((sx * 0.08, -0.02, tip.z - 0.07)), 0.006, leather,
                             n=8, bevel=0, name="strap", low=4))
    if how == "on":
        for sx in (-1, 1):
            e = Vector((sx * 0.032, tip.y - 0.042, tip.z + 0.036))
            high.append(make.cyl(e + Vector((0, -0.014, 0)), e + Vector((0, 0.02, 0)), 0.023, leather, n=24, bevel=0.004,
                                 name="cup", low=10))
            high.append(make.torus(e + Vector((0, 0.02, 0)), (0, 1, 0), 0.022, 0.0045, brass, n=24, m=6, name="bezel", low=None))
            high.append(make.cyl(e + Vector((0, 0.016, 0)), e + Vector((0, 0.021, 0)), 0.0195, glass, n=24, bevel=0.002,
                                 name="lens", low=10))
        e = Vector((0, tip.y - 0.042, tip.z + 0.036))
        high.append(make.cyl(e + Vector((-0.012, 0.012, 0)), e + Vector((0.012, 0.012, 0)), 0.004, brass, n=8, bevel=0,
                             name="bridge", low=4))
        # The goggles' band round the head, over the ears, under the cap.
        band = make.torus(Vector((0, 0.012, e.z)), (0, 0, 1), 0.098, 0.006, rubber, n=40, m=6, name="band", low=(16, 3))
        for o in (band, make.LOW[-1]):
            o.data.transform(Matrix.Translation(Vector((0, 0.012, e.z))) @ Matrix.Diagonal((0.84, 1.08, 1.8, 1.0))
                             @ Matrix.Translation(-Vector((0, 0.012, e.z))))
        high.append(band)
    low = make.LOW[before:]
    del make.LOW[before:]
    if how == "torn":
        # Hanging loose at the collar: dropped and swung down on its lower straps, out in front of the coat.
        hang = (Matrix.Translation(m_c + Vector((0, 0.07, -0.13))) @ Matrix.Rotation(math.radians(65), 4, "X")
                @ Matrix.Translation(-m_c))
        for o in high + low:
            o.data.transform(hang)
    return high, low


# The coat's patches (world boxes: centre, half size): squares of other cloth sewn over where it's torn, low on the back,
# on the chest, at the right elbow, at the hem.
PATCHES = [((0.14, -0.12, 0.72), (0.09, 0.1, 0.07)), ((-0.1, 0.15, 1.2), (0.06, 0.08, 0.055)),
           ((0.45, 0.0, 1.45), (0.05, 0.1, 0.1)), ((-0.16, 0.12, 0.62), (0.07, 0.1, 0.06)),
           ((-0.16, -0.12, 1.28), (0.06, 0.08, 0.06))]


def _box_edge(p, c, h):
    """How far inside the box each point is, in metres to its nearest face (negative outside)."""
    return (np.array(h, np.float32) - np.abs(p - np.array(c, np.float32))).min(-1)


def patches(p):
    out = np.zeros(len(p), np.float32)
    for c, h in PATCHES:
        out = np.maximum(out, smooth01(-0.002, 0.004, _box_edge(p, c, h)))
    return out


def stitches(p):
    """The stitching round each patch: a dashed line just inside its edge."""
    out = np.zeros(len(p), np.float32)
    for c, h in PATCHES:
        e = _box_edge(p, c, h)
        dash = (np.sin((p[:, 0] + p[:, 1] + p[:, 2]) * 420) > 0.1).astype(np.float32)
        out = np.maximum(out, bell((e - 0.006) / 0.0018) * dash)
    return out


def helm_grade(base, atlas):
    """The helm's paint laid pale and neutral (the engine tints it the player's colour), chipped to the iron at its edges
    and worn through in patches; the coat's patches in other cloths, browner and paler than the oilskin they cover."""
    S = atlas.size
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / S
    def nz(scale, seed):
        return cook.noise_np(np.stack([xx.ravel() * scale, yy.ravel() * scale, np.full(S * S, seed * 0.37)], 1), seed, 1.0).reshape(S, S)
    paint = atlas.masks.get("paint")
    if paint is not None and paint.any():
        lum = base.mean(-1)
        m = float(np.median(lum[paint]))
        neutral = np.clip(lum / max(m, 1e-3) * 0.4, 0.03, 0.75)[..., None] * np.ones(3, np.float32)
        # Chipped through to the iron; rust run down from the rivets and the seams in streaks; soot settled low on it; the
        # scarf (wool, under the helm) a good deal darker than the enamel.
        chip = smooth01(0.3, 0.42, nz(48, 61) * 0.7 + nz(140, 62) * 0.3)
        iron = np.array([0.035, 0.033, 0.03], np.float32)
        streak = np.clip(atlas.maps["streak"], 0, 1)[..., None]
        neutral = neutral * (1 - 0.55 * streak) + np.array([0.09, 0.045, 0.025], np.float32) * 0.55 * streak
        neutral = neutral * (1 - chip)[..., None] + iron * chip[..., None]
        z = atlas.height()
        neutral = neutral * np.where(z < 1.585, 0.38, 1.0)[..., None]
        base = np.where(paint[..., None], neutral, base)
    patch = atlas.maps.get("patch")
    if patch is not None:
        k = np.clip(patch, 0, 1)[..., None] * (1 - (paint[..., None] if paint is not None else 0))
        cloth = np.where((nz(6, 63) > 0)[..., None], np.array([1.3, 1.08, 0.78], np.float32), np.array([0.62, 0.58, 0.56], np.float32))
        base = base * (1 - k) + base * cloth * k
    return base


def build(name, style):
    # The crew as tools/blender builds it, its export held back until the game mesh wears the bake (tools/models overbake).
    os.environ["DT_CREW"] = style.figure
    kit, g, arm, parts = overbake.hold("crew.py")
    scan = style.figure == "bare"
    print(f"[dt] {name} parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
    mat_named = overbake.mat_slots


    # ----------------------------------------------------------------------------------------------------------------
    # The head: the scan in place of the egg (the kit's face, hair, nose and ears go; the neck stays under the collar).
    body = parts["body"]
    doomed = mat_named(body, "crew_atlas.face") | mat_named(body, "crew_atlas.hair")
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in doomed], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(body.data)
    bm.free()

    HEAD_C = g["HEAD_C"]
    head, TIP = [], None
    if scan:
        head = cook.load("threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/LeePerrySmith.glb")
        skin_img = os.path.join(cook.SOURCES, "threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/Map-COL.jpg")
        skin = bpy.data.materials.new("scan_skin")
        skin.use_nodes = True
        tex = skin.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(skin_img)
        skin.node_tree.links.new(tex.outputs["Color"], skin.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
        for o in head:
            o.data.materials.clear()
            o.data.materials.append(skin)
        cook.fit(head, height=0.46)
        cook.rotate(head, 180)
        cook.cut(head, lambda c: c.z > 0.2)
        lo, hi = cook.bounds(head)
        # Crown at the egg's crown, centred over the neck, the face as far forward as the egg's was.
        S = 0.93
        cook.transform(head, Matrix.Scale(S, 4))
        lo, hi = cook.bounds(head)
        cook.move(head, (-(lo.x + hi.x) / 2, HEAD_C.y + 0.006 - (lo.y + hi.y) / 2, HEAD_C.z + 0.118 - hi.z))
        lo, hi = cook.bounds(head)
        print("[dt] head", tuple(round(c, 3) for c in lo), tuple(round(c, 3) for c in hi))


        def close_mouth(o):
            """The scan's lips are parted on a cavity (a mouth, teeth, a tongue): everything a few mm behind the lips' own front
            surface, in the band between them, goes, and the slit is capped. A game mesh collapsed from it would otherwise cave
            the lips into the hole."""
            me = o.data
            co = np.array([v.co for v in me.vertices], np.float32)
            tip = co[co[:, 1].argmax()]
            lo_z, hi_z = tip[2] - 0.068, tip[2] - 0.025
            band = (np.abs(co[:, 0]) < 0.034) & (co[:, 2] > lo_z) & (co[:, 2] < hi_z)
            pts = co[band]
            bm = bmesh.new()
            bm.from_mesh(me)
            doomed = []
            for f in bm.faces:
                c = f.calc_center_median()
                if abs(c.x) >= 0.03 or not (lo_z + 0.006 < c.z < hi_z - 0.006):
                    continue
                near = pts[(np.abs(pts[:, 0] - c.x) < 0.005) & (np.abs(pts[:, 2] - c.z) < 0.005)]
                if len(near) and c.y < near[:, 1].max() - 0.005:
                    doomed.append(f)
            bmesh.ops.delete(bm, geom=doomed, context="FACES")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            rim = [e for e in bm.edges if e.is_boundary and abs(e.verts[0].co.x) < 0.04 and lo_z - 0.01 < e.verts[0].co.z < hi_z + 0.01]
            bmesh.ops.holes_fill(bm, edges=rim, sides=0)
            bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
            bm.to_mesh(me)
            bm.free()
            me.update()
            print("[dt] mouth closed:", len(doomed), "faces out")


        for o in head:
            close_mouth(o)
        # The scan's landmarks, before any style reshapes it: the nose tip, and the eyes above and behind it.
        hco = np.concatenate([np.array([v.co for v in o.data.vertices], np.float32) for o in head])
        TIP = Vector(hco[hco[:, 1].argmax()])
        if style.head is not None:
            style.head(head, HEAD_C)


        # The game mesh's head: the scan decimated, on the head bone, blending into the neck under the collar.
        head_low = cook.duplicate(head)
        cook.decimate(head_low, 2600)
        for o in head_low:
            co = np.array([v.co for v in o.data.vertices], np.float32)
            # The whole face rides the head (a chin on the neck bone shears off it when the clips tip the two apart); only the
            # last of the neck, under the collar, blends.
            w = np.clip((co[:, 2] - 1.545) / 0.02, 0, 1)
            gh, gn = o.vertex_groups.new(name="head"), o.vertex_groups.new(name="neck")
            for vi, k in enumerate(w):
                if k > 0.02:
                    gh.add([vi], float(k), "REPLACE")
                if k < 0.98:
                    gn.add([vi], float(1 - k), "REPLACE")
        bpy.ops.object.select_all(action="DESELECT")
        for o in head_low:
            o.select_set(True)
        body.select_set(True)
        bpy.context.view_layer.objects.active = body
        bpy.ops.object.join()

    # ----------------------------------------------------------------------------------------------------------------
    # The high copy, dressed in the library. Each kit material (by name) wears a library layer at the library's scale
    # (repeats a metre), tinted toward the kit's palette; soft ones (cloth, leather over a form) are subdivided smooth,
    # hard ones (buckles, the lamp's box, the badge) keep their edges, bevelled.
    import make  # noqa: E402

    make.USED.clear()
    make._mats.clear()
    make.LOW.clear()
    DRESS = {
        # Cloth at several times the library's repeat: at a person's scale the weave is a texture, not a pattern.
        "crew_atlas.coat": ("coat_oilskin", 7.0, (0.8, 0.8, 0.66), 0.5, 3),
        "crew_atlas.sleeve": ("coat_oilskin", 7.0, (0.8, 0.8, 0.66), 0.5, 3),
        "crew_atlas.trouser": ("wool", 9.0, (0.42, 0.44, 0.5), 0.9, 3),
        "crew_atlas.boot": ("leather", 4.0, (0.36, 0.3, 0.27), 0.55, 2),
        "crew_atlas.gloves": ("leather", 6.0, (0.5, 0.41, 0.34), 0.7, 0),
        "skin.neck": ("skin", 4.0, (0.9, 0.76, 0.68), 0.6, 2),
        "crew_atlas.cap": ("wool", 10.0, (0.3, 0.32, 0.37), 0.9, 2),
        "crew_atlas.helmet": ("paint_olive", 3.0, (0.8, 0.8, 0.7), 0.5, 2),
        "crew_atlas.belt": ("leather", 5.0, (0.42, 0.32, 0.25), 0.5, 0),
        "leather.strap": ("leather", 5.0, (0.42, 0.32, 0.25), 0.5, 0),
        "crew_atlas.satchel": ("coat_oilskin", 9.0, (0.75, 0.68, 0.52), 0.85, 0),
        "crew_atlas.badge": ("brass", 6.0, (1, 1, 1), 0.35, 0),
        "crew_atlas.scarf": ("wool", 10.0, (0.38, 0.3, 0.3), 0.95, 2),
        "iron_plate.crew": ("iron_plate", 4.0, (0.55, 0.55, 0.55), 0.5, 0),
        # The helm and the tank. The paint is laid down pale and neutral: the engine tints it the player's colour.
        "helm.paint": ("paint_olive", 3.0, (0.95, 0.95, 0.95), 0.35, 2),
        "helm.door": ("paint_olive", 3.0, (0.95, 0.95, 0.95), 0.35, 2),
        "helm.brass": ("brass", 6.0, (1.0, 0.95, 0.85), 0.3, 1),
        "helm.glass": ("glass_dirty", 6.0, (0.2, 0.22, 0.24), 0.08, 0),
        "helm.rubber": ("paint_black", 6.0, (0.45, 0.43, 0.42), 0.7, 2),
        "helm.stack": ("iron_smokebox", 5.0, (0.6, 0.58, 0.56), 0.5, 1),
        "tank.copper": ("copper_pipe", 4.0, (1.0, 0.95, 0.9), 0.35, 2),
        "leather.harness": ("leather", 5.0, (0.42, 0.32, 0.25), 0.5, 0),
    }


    DRESS.update(style.dress)

    def dress(m):
        """The kit material's library layer and subdivision; the lamp's glass and the scan's skin aren't in the copy."""
        if m.name.startswith(("scan_skin", "helm.glass")) or (style.lamp and m.name.startswith("crew_atlas.lamp")):
            return None
        key = next((k for k in DRESS if m.name.startswith(k)), None)
        if key is None:
            raise KeyError(f"{name}: no dress for {m.name}")
        layer, scale, tint, rough, subdiv = DRESS[key]
        return make.lib(layer, scale, tint, rough), subdiv


    def coat(p, n):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        a = np.arctan2(x, y)
        wobble = cook.noise_np(p, 3, 5) * 1.4
        # The skirt's weight: long folds from the belt down, deepening to the hem, fewer across the lapped front.
        hang = smooth01(0.97, 0.56, z) * (0.35 + 0.65 * smooth01(0.1, 0.5, np.abs(a)))
        d = 0.016 * hang * np.sin(a * 7 + wobble)
        # Gathered under the belt; the back dragged across from the shoulder blades; a roll at the collar.
        d += 0.004 * np.sin(a * 26) * bell((z - 1.005) / 0.05)
        d += 0.0035 * np.sin(a * 9 + z * 22 + wobble) * bell((z - 1.25) / 0.12) * (np.abs(a) > 1.6)
        d += 0.004 * bell((z - 1.525) / 0.012)
        # Seams: down the back and both sides.
        for seam in (np.pi / 2, -np.pi / 2, np.pi):
            da = np.abs(np.angle(np.exp(1j * (a - seam))))
            d -= 0.0022 * bell(da * 0.2 / 0.01)
        return d + fine(p)


    def sleeve(p, n):
        u, b = np.abs(p[:, 0]), np.arctan2(p[:, 2] - 1.448, p[:, 1])
        wobble = cook.noise_np(p, 5, 9)
        d = 0.006 * np.sin(u * 190 + wobble * 2) * bell((u - 0.47) / 0.05) * (0.6 + 0.4 * wobble)
        d += 0.004 * np.sin(u * 260 + b) * bell((u - 0.64) / 0.03)
        d += 0.005 * np.sin(b * 6 + wobble) * bell((u - 0.24) / 0.05) * (p[:, 2] < 1.45)
        d -= 0.002 * bell(np.abs(np.angle(np.exp(1j * (b + np.pi / 2)))) * 0.07 / 0.008)
        # The cuff turned back: a ridge where it folds.
        d += 0.003 * bell((u - 0.698) / 0.006)
        return d + fine(p)


    def trouser(p, n):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        c = np.arctan2(x - np.sign(x) * 0.105, y)
        wobble = cook.noise_np(p, 7, 8)
        d = 0.005 * np.sin(z * 150 + c * 2 + wobble * 2) * bell((z - 0.53) / 0.06)
        d += 0.006 * np.sin(z * 210 + c * 3 + wobble) * bell((z - 0.3) / 0.04)
        d += 0.002 * bell(c / 0.15)
        return d + fine(p, 0.0009, 90)


    def boot(p, n):
        z = p[:, 2]
        c = np.arctan2(p[:, 0] - np.sign(p[:, 0]) * 0.105, p[:, 1])
        d = 0.0025 * np.sin(z * 300 + c * 2) * bell((z - 0.13) / 0.025)
        # The welt: a lip where the upper meets the sole.
        d += 0.0025 * bell((z - 0.02) / 0.004)
        return d + fine(p, 0.0006, 120)


    def glove(p, n):
        ax, y = np.abs(p[:, 0]), p[:, 1]
        fingers = smooth01(0.82, 0.835, ax)
        d = np.zeros(len(p), np.float32)
        for gy in (-0.022, 0.0, 0.022):
            d -= 0.0045 * bell((y - gy) / 0.004) * fingers
        d += 0.002 * bell((ax - 0.815) / 0.004)
        return d + fine(p, 0.0008, 110)


    def cap(p, n):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        a = np.arctan2(x, y - 0.012)
        d = np.zeros(len(p), np.float32)
        crown = z > 1.765
        for k in range(6):
            da = np.abs(np.angle(np.exp(1j * (a - k * np.pi / 3))))
            d -= 0.0018 * bell(da * 0.11 / 0.004) * crown
        d += 0.002 * bell((z - 1.748) / 0.004)
        return d + fine(p, 0.0007, 120)


    def helmet(p, n):
        return 0.0025 * cook.noise_np(p, 11, 22) + fine(p, 0.0006, 100)


    def scarf(p, n):
        return 0.004 * cook.noise_np(p, 13, 30) + 0.0012 * np.sin(p[:, 2] * 400) + fine(p, 0.001, 90)


    HC = g.get("HC")

    def drum(p, n):
        # Rivets round the drum by each band, a few dents, the paint's orange peel.
        x, y, z = p[:, 0], p[:, 1] - HC.y, p[:, 2] - HC.z
        a = np.arctan2(x, z)
        d = np.zeros(len(p), np.float32)
        for yb in (-0.104, 0.084, 0.118):
            d += 0.0035 * bell((y - yb) / 0.004) * np.maximum(0, np.cos(a * 18)) ** 8
        d -= 0.004 * np.maximum(0, cook.noise_np(p, 41, 9)) ** 3
        return d + fine(p, 0.0004, 160, 42)

    def door(p, n):
        # The door's rivets round its rim, its dish pressed in, hammered.
        x, z = p[:, 0], p[:, 2] - HC.z
        r, a = np.hypot(x, z), np.arctan2(x, z)
        d = 0.003 * bell((r - 0.122) / 0.004) * np.maximum(0, np.cos(a * 12)) ** 8
        return d + 0.0012 * cook.noise_np(p, 43, 30) + fine(p, 0.0005, 140, 44)

    def brass(p, n):
        # The grille's slots; the rest just worn.
        x, y, z = p[:, 0], p[:, 1] - HC.y, p[:, 2] - HC.z
        grille = (np.abs(x) < 0.045) & (np.abs(z + 0.072) < 0.016) & (y > 0.15)
        return -0.004 * grille * (np.sin(z * 520) > 0.2) + fine(p, 0.0003, 200, 45)

    def rubber(p, n):
        # The hose's corrugations, the seal's lip.
        return 0.0025 * np.sin((p[:, 0] + p[:, 1] + p[:, 2]) * 380) + fine(p, 0.0004, 150, 46)

    def copper(p, n):
        # A seam down the tank's back and its rivets; hammered dents.
        x = p[:, 0]
        d = 0.003 * bell(x / 0.004) * np.maximum(0, np.sin(p[:, 2] * 150)) ** 6
        return d - 0.002 * np.maximum(0, cook.noise_np(p, 47, 14)) ** 2 + fine(p, 0.0004, 150, 48)

    def patched(p, n):
        # The coat's patches: squares of other cloth sewn on over the holes, their stitched edges standing proud.
        return 0.0035 * np.clip(patches(p), 0, 1) - 0.0018 * stitches(p)

    SHAPE = {"crew_atlas.coat": coat, "crew_atlas.sleeve": sleeve, "crew_atlas.trouser": trouser, "crew_atlas.boot": boot,
             "crew_atlas.gloves": glove, "crew_atlas.cap": cap, "crew_atlas.helmet": helmet, "crew_atlas.scarf": scarf,
             "crew_atlas.satchel": lambda p, n: fine(p, 0.001, 60), "crew_atlas.belt": lambda p, n: fine(p, 0.0005, 150),
             "leather.strap": lambda p, n: fine(p, 0.0005, 150)}
    if not scan:
        SHAPE.update({"helm.paint": drum, "helm.door": door, "helm.brass": brass, "helm.rubber": rubber,
                      "helm.stack": lambda p, n: 0.0015 * cook.noise_np(p, 49, 40) + fine(p, 0.0005, 140, 50),
                      "tank.copper": copper, "helm.glass": lambda p, n: 0.0004 * cook.noise_np(p, 51, 60)})
        SHAPE["crew_atlas.coat"] = (lambda base: lambda p, n: base(p, n) + patched(p, n))(SHAPE["crew_atlas.coat"])
        SHAPE["crew_atlas.sleeve"] = (lambda base: lambda p, n: base(p, n) + patched(p, n))(SHAPE["crew_atlas.sleeve"])

    # Which highs bake onto which game part (a cap never shadows the helmet it isn't worn with).
    BAKED = ["body", "hat_cap", "hat_helmet", "scarf"] if scan else ["body", "stack_short", "stack_tall", "scarf"]
    for k, fn in style.shapes.items():
        SHAPE[k] = (lambda f, base: (lambda p, n: base(p, n) + f(p, n)) if base else f)(fn, SHAPE.get(k))
    highs = {part: overbake.high_of(parts[part], dress, SHAPE) for part in BAKED}
    # The respirator and goggles (Style.mask), modelled on the scan's landmarks, their game mesh on the head bone.
    if style.mask and scan:
        mask_high, mask_low = respirator(style.mask, TIP, make)
        head += mask_high
        mat = bpy.data.materials.new("crew_mask")
        for o in mask_low:
            o.data.materials.clear()
            o.data.materials.append(mat)
            g_ = o.vertex_groups.new(name="head" if style.mask == "on" else "spine_03")
            g_.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
        bpy.ops.object.select_all(action="DESELECT")
        for o in mask_low:
            o.select_set(True)
        body.select_set(True)
        bpy.context.view_layer.objects.active = body
        bpy.ops.object.join()
    if style.gear is not None:
        head += style.gear(TIP, HEAD_C, make)
    # The scan's own head, full resolution, in the body's group.
    highs["body"] += head

    from mathutils.bvhtree import BVHTree  # noqa: E402

    coat_high = next(h for h in highs["body"] if h["dt_kind"].startswith("crew_atlas.coat"))
    tree = BVHTree.FromObject(coat_high, bpy.context.evaluated_depsgraph_get())
    BUTTON = make.lib("paint_black", 8.0, (0.6, 0.55, 0.5), 0.4)
    for z in (0.8, 0.9, 1.13, 1.23, 1.33, 1.43):
        hit = tree.ray_cast(Vector((0.07, 0.5, z)), Vector((0, -1, 0)))
        if hit[0] is not None:
            at, nrm = hit[0], hit[1]
            highs["body"].append(make.cyl(at - nrm * 0.002, at + nrm * 0.005, 0.011, BUTTON, n=12, bevel=0.002, name="button",
                                          r1=0.009, low=0))
    # Laces up the front of each boot, crossing between eyelets.
    LACE = make.lib("leather", 8.0, (0.35, 0.3, 0.26), 0.6)
    for sx in (-1, 1):
        x = sx * 0.105
        for k in range(5):
            z = 0.125 + k * 0.03
            for s in (-1, 1):
                highs["body"].append(make.cyl((x - s * 0.02, 0.056, z), (x + s * 0.02, 0.056, z + 0.026), 0.0028, LACE, n=6,
                                              bevel=0, name="lace", low=0))
                highs["body"].append(make.torus((x + s * 0.022, 0.055, z), (0, 1, 0), 0.0045, 0.0015, BUTTON, n=8, m=4,
                                                name="eyelet", low=None))
    if scan:
        # The helmet's rolled rim.
        RIM = make.lib("paint_olive", 2.0, (0.8, 0.8, 0.7), 0.5)
        rim = make.torus((0, 0.006, 1.712), (0, 0, 1), 0.153, 0.006, RIM, n=48, m=8, name="rim", low=None)
        rim.scale = (1.0, 1.066, 1.0)
        bpy.context.view_layer.objects.active = rim
        bpy.ops.object.transform_apply(scale=True)
        highs["hat_helmet"].append(rim)
    print(f"[dt] {name} highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

    if os.environ.get(style.preview):
        out = os.path.join(cook.ROOT, "out", "review")
        os.makedirs(out, exist_ok=True)
        scene = bpy.context.scene
        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.samples = 16
        scene.cycles.use_denoising = False
        scene.render.resolution_x, scene.render.resolution_y = 500, 700
        scene.world = bpy.data.worlds.new("w")
        scene.world.use_nodes = True
        scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.rotation_euler = (0.7, 0.3, 2.6)
        sun.data.energy = 4
        scene.collection.objects.link(sun)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        scene.collection.objects.link(cam)
        scene.camera = cam
        variant = os.environ.get(style.preview)
        for o in parts.values():
            o.hide_render = True
        for part, hs in highs.items():
            for h in hs:
                h.hide_render = part not in ("body", BAKED[2] if variant in ("helmet", "tall") else BAKED[1]) or variant == "none" and part != "body"
        for view, d, c, dist in style.views or (("front", (0.2, 1, 0.1), (0, 0, 0.95), 4.2), ("back", (-0.3, -1, 0.1), (0, 0, 0.95), 4.2),
                                 ("head", (0.4, 1, 0.15), (0, 0.02, 1.68 if not scan else 1.62), 1.0 if scan else 1.2), ("hand", (0.2, 0.6, 1), (0.75, 0, 1.44), 0.7),
                                 ("boot", (0.5, 1, 0.3), (0.1, 0.05, 0.15), 0.9)):
            cam.location = Vector(c) + Vector(d).normalized() * dist
            cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(out, f"{name}-high-{view}.png")
            bpy.ops.render.render(write_still=True)
        print(f"[dt] {name} preview -> out/review/{name}-high-*.png")
        sys.exit(0)

    # ----------------------------------------------------------------------------------------------------------------
    # One atlas over every baked part. The lamp's glass keeps crew_atlas's lit cell (its faces keep their UVs and their
    # material: a pure light). The face (kind 1) and the hands (kind 3) get more of the atlas than their area would: they're
    # seen closest. The head is unwrapped in one piece, round a cylinder on the neck (the seam down the back, under the
    # collar and the cap), in metres, so it lies at the same density as the rest before the boost.
    FACE, HANDS, MASK, HELM, PAINT = 1, 3, 4, 5, 6


    def kind(m):
        if style.lamp and m.name.startswith("crew_atlas.lamp") or m.name.startswith("helm.glass"):
            return overbake.Atlas.KEEP
        if not scan and m.name.startswith(("helm.paint", "helm.door", "crew_atlas.scarf")):
            return PAINT
        return (FACE if m.name.startswith("scan_skin") else HANDS if m.name.startswith("crew_atlas.gloves")
                else MASK if m.name.startswith("crew_mask") else HELM if m.name.startswith(("helm.", "tank.")) else 0)


    def head_uv(f, uvl):
        us = []
        for loop in f.loops:
            q = loop.vert.co - HEAD_C
            # 0.5 at the face, 0 and 1 at the back.
            us.append(math.atan2(q.x, q.y) / (2 * math.pi) + 0.5)
        if max(us) - min(us) > 0.5:
            us = [u + 1.0 if u < 0.5 else u for u in us]
        for loop, u in zip(f.loops, us):
            loop[uvl].uv = (u * 0.62, (loop.vert.co.z - 1.5) * 1.0)


    atlas = overbake.Atlas(name, parts, BAKED, kind)
    atlas.unwrap(boosts={FACE: 3.5, HANDS: 1.8, MASK: 2.5, HELM: 2.2, PAINT: 2.2}, special={FACE: head_uv} if scan else {})
    # Each part bakes from its own high copy (a cap never shadows the helmet it isn't worn with). The head bakes as a group
    # of its own, from the scan, with a tight cage: the cloth's high copy is subdivided in from its game mesh by a
    # centimetre or two, but a face's cage that deep projects the cheeks onto the nose.
    highs["head"] = [h for h in highs["body"] if h in head]
    highs["body"] = [h for h in highs["body"] if h not in head]
    groups = {part: ((atlas.part_of == pi) & (atlas.kind_of != FACE) & (atlas.kind_of != MASK) & (atlas.kind_of != PAINT),
                     highs[part]) for pi, part in enumerate(BAKED)}
    if scan:
        groups["head"] = ((atlas.kind_of == FACE) | (atlas.kind_of == MASK), highs["head"])
    else:
        # The paint bakes as a group of its own (the drum and the scarf), so the grade knows its texels.
        groups["paint"] = (atlas.kind_of == PAINT, [h for part in ("body", "scarf") for h in highs[part]
                                                    if h.get("dt_kind", "").startswith(("helm.paint", "helm.door", "crew_atlas.scarf"))])
    groups = {k: v for k, v in groups.items() if v[0].any()}
    masks = dict(style.masks)
    if not scan:
        masks["patch"] = patches
        # Rust run down the helm from its rivets and seams: streaks drawn out vertically.
        masks["streak"] = lambda p: smooth01(0.35, 0.8, cook.noise_np(p * np.array([90.0, 90.0, 6.0], np.float32), 53, 1.0)
                                             * 0.7 + cook.noise_np(p, 54, 30) * 0.3).astype(np.float32)
    atlas.bake(groups, cages={"head": (0.006, 0.02)} if scan else {}, hide=[parts["shovel"]], masks=masks)

    # The colour, darkened by the occlusion and sooted: soot in every crease, the mud and wet of the ballast climbing the
    # boots and the coat's hem, the shoulders and cap dulled where the smoke settles on them.
    face = atlas.masks["head"] if scan else np.zeros((atlas.size, atlas.size), bool)
    soot = np.array([0.018, 0.016, 0.014], np.float32)
    base = atlas.base(soot=soot, gentle=face)
    z = atlas.height()
    mud = np.array([0.05, 0.04, 0.03], np.float32)
    low_down = np.clip((0.55 - z) / 0.5, 0, 1) ** 1.5 * 0.55
    base = base * (1 - low_down)[..., None] + mud * low_down[..., None]
    settled = np.clip((z - 1.35) / 0.4, 0, 1) * 0.2
    base = base * (1 - settled)[..., None] + soot * settled[..., None]
    # The skin: the scan's is a clean, warm studio face. Out here it's sallow with cold and smoke, soot worked into it in
    # smudges (the atlas's own noise: the head's one island takes it as blotches across the face).
    SIZE = atlas.size
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
    smudge = np.clip(cook.noise_np(np.stack([xx.ravel() * 9, yy.ravel() * 9, np.zeros(SIZE * SIZE)], 1), 17, 1.0).reshape(SIZE, SIZE)
                     * 0.8 + cook.noise_np(np.stack([xx.ravel() * 31, yy.ravel() * 31, np.ones(SIZE * SIZE)], 1), 19, 1.0).reshape(SIZE, SIZE) * 0.4, 0, 1)
    tone = np.array([0.66, 0.58, 0.52], np.float32)
    grey = base.mean(-1, keepdims=True)
    sallow = (base * 0.55 + grey * 0.45) * tone
    sallow = sallow * (1 - 0.55 * smudge)[..., None] + soot * (0.55 * smudge)[..., None]
    base = np.where(face[..., None], sallow, base)
    if not scan:
        base = helm_grade(base, atlas)
    if style.grade is not None:
        base = style.grade(base, atlas, face)
    atlas.finish(base, kit, arm, source_ids=["threejs-leeperrysmith"] if scan else [],
                 made=make.provenance(name, style.what), split={} if scan else {PAINT: "paint"})
