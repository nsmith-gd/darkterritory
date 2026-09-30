"""Concepts for the crew's headgear (art direction: steampunk, post-apocalyptic, masked so nothing needs lip sync or eyes,
"still have some fun to us", "more detailing"). Not a recipe: nothing here goes into the game. It builds the crew figure
bare-headed (tools/models/crewfigure) with one concept's headgear modelled on it at full detail, and renders it:

    CONCEPT=beak tools/models/concepts/render.sh     # or diver, welder, sallet; all four with no CONCEPT
    -> out/review/concept-<name>-high-{full,head,profile,back}.png

The one chosen becomes the crew's (its game mesh in tools/blender/crew.py, its detail baked by crewfigure).
Each has a place for the player's colour (here a stand-in oxide red): the beak's hood, the diver's band, the welder's cap,
the sallet's top gorget plate.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import crewfigure  # noqa: E402
from overbake import bell, smooth01  # noqa: E402

CONCEPT = os.environ.get("CONCEPT", "beak")


# ----------------------------------------------------------------------------------------------------------------
# Modelling helpers: ellipsoids and shells, sculpted (subdivided, then pushed along their normals by a field), rings of
# rivets, lathed profiles.

def ellipsoid(centre, radii, material, name, seg=48, rings=32, keep=None, rot=None):
    """An ellipsoid; `keep(face centre) -> bool` cuts it down to a shell (a hood's opening, a visor's front)."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=1.0)
    o = bpy.context.view_layer.objects.active
    o.name = name
    m = Matrix.Diagonal((*radii, 1.0))
    if rot is not None:
        m = rot @ m
    o.data.transform(Matrix.Translation(Vector(centre)) @ m)
    if keep is not None:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep(f.calc_center_median())], context="FACES")
        bm.to_mesh(o.data)
        bm.free()
    o.data.materials.clear()
    o.data.materials.append(material)
    for f in o.data.polygons:
        f.use_smooth = True
    return o


def lathe(axis_from, axis_to, profile, material, name, n=40, cap=True):
    """A surface of revolution: `profile` [(t 0..1 along the axis, radius)], round the axis from `axis_from` to `axis_to`."""
    a, b = Vector(axis_from), Vector(axis_to)
    ax = (b - a).normalized()
    u = ax.orthogonal().normalized()
    v = ax.cross(u)
    bm = bmesh.new()
    rings = []
    for t, r in profile:
        c = a.lerp(b, t)
        rings.append([bm.verts.new(c + (u * math.cos(2 * math.pi * k / n) + v * math.sin(2 * math.pi * k / n)) * r) for k in range(n)])
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(material)
    for f in me.polygons:
        f.use_smooth = True
    return o


def sculpt(o, fn, level=2):
    """Subdivides the part smooth, then moves each vertex along its normal by fn(positions, normals) metres."""
    bpy.context.view_layer.objects.active = o
    mod = o.modifiers.new("sub", "SUBSURF")
    mod.levels = level
    mod.render_levels = level
    bpy.ops.object.modifier_apply(modifier=mod.name)
    me = o.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nm = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("normal", nm)
    nm = nm.reshape(-1, 3)
    co += nm * np.asarray(fn(co, nm), np.float32)[:, None]
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    return o


def rivets(make, centre, axis, radius, count, material, size=0.0045, phase=0.0, name="rivet"):
    """A ring of rivet heads round `axis` through `centre`, each a flattened dome facing out from the ring."""
    c, ax = Vector(centre), Vector(axis).normalized()
    u = ax.orthogonal().normalized()
    v = ax.cross(u)
    out = []
    for k in range(count):
        a = phase + 2 * math.pi * k / count
        d = u * math.cos(a) + v * math.sin(a)
        out.append(make.nail(c + d * radius, d, material, r=size, name=name))
    return out


def rivets_on(make, points_normals, material, size=0.0045, name="rivet"):
    return [make.nail(p, n, material, r=size, name=name) for p, n in points_normals]


def fine(p, amount, scale, seed):
    return amount * cook.noise_np(p, seed, scale)


def materials(make):
    m = {
        "leather": make.lib("leather", 7.0, (0.36, 0.26, 0.19), 0.5),
        "leather_dark": make.lib("leather", 7.0, (0.2, 0.15, 0.12), 0.55),
        "brass": make.lib("brass", 8.0, (1.0, 0.9, 0.7), 0.28),
        "copper": make.lib("copper_pipe", 5.0, (1.0, 0.72, 0.5), 0.3),
        "iron": make.lib("iron_smokebox", 5.0, (0.62, 0.6, 0.58), 0.45),
        "rust": make.lib("rust_heavy", 4.0, (0.78, 0.66, 0.58), 0.7),
        "rubber": make.flat("concept_rubber", (0.03, 0.029, 0.028), rough=0.5),
        "tin": make.lib("paint_olive", 6.0, (0.55, 0.57, 0.46), 0.5),
        "paint": make.lib("paint_oxide", 4.0, (1.0, 1.0, 1.0), 0.45),   # the player's colour, a stand-in
        "dyed": make.lib("leather", 7.0, (0.62, 0.2, 0.14), 0.5),        # leather dyed the player's colour
        "canvas": make.lib("coat_oilskin", 8.0, (0.62, 0.58, 0.48), 0.8),
        "wool": make.lib("wool", 10.0, (0.3, 0.28, 0.27), 0.95),
    }
    # The lenses: dark glass, lit dimly from inside by the wearer's own lamp (the one thing you see of a face).
    g = bpy.data.materials.new("concept_glass")
    g.use_nodes = True
    b = g.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.02, 0.022, 0.025, 1)
    b.inputs["Roughness"].default_value = 0.05
    b.inputs["Emission Color"].default_value = (1.0, 0.55, 0.2, 1)
    b.inputs["Emission Strength"].default_value = 0.12
    m["glass"] = g
    return m


def lights():
    """A cold rim from behind and a warm fill low in front, so the silhouette and the detail both read."""
    for name, rot, energy, colour in (("rim", (math.radians(60), 0, math.radians(200)), 3.0, (0.7, 0.8, 1.0)),
                                      ("fill", (math.radians(75), 0, math.radians(-20)), 1.2, (1.0, 0.8, 0.6))):
        l = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        l.data.energy = energy
        l.data.color = colour
        l.rotation_euler = rot
        bpy.context.scene.collection.objects.link(l)


# ----------------------------------------------------------------------------------------------------------------
# 1. The plague stoker: a stitched leather hood and cowl, a long banded beak with the filters in it.

def beak(tip, hc, make):
    M = materials(make)
    out = []
    hood_c = Vector((0, 0.012, 1.686))

    def stitched(p, n):
        x, y, z = p[:, 0], p[:, 1] - hood_c.y, p[:, 2] - hood_c.z
        a = np.arctan2(x, y)
        # Six panels sewn from the crown down, each seam a groove with its stitching either side of it.
        seam = np.abs(np.angle(np.exp(1j * 6 * a))) / 6
        d = -0.003 * bell(seam / 0.012) * smooth01(-0.08, 0.02, z)
        d += 0.0012 * (np.sin((z + a * 0.1) * 700) > 0.4) * bell((seam - 0.03) / 0.008) * smooth01(-0.08, 0.02, z)
        # Worn, creased where it's drawn in at the neck and bunched under the jaw.
        d += 0.004 * np.sin(z * 110 + a * 4) * smooth01(1.64, 1.56, p[:, 2]) + 0.003 * cook.noise_np(p, 3, 18)
        return d + fine(p, 0.0012, 60, 1)

    hood = ellipsoid(hood_c, (0.116, 0.142, 0.165), M["dyed"], "hood", keep=lambda c: c.z > 1.56 or c.y > -0.02 and c.z > 1.5)
    out.append(sculpt(hood, stitched))
    # The cowl over the shoulders: leather, cut in scallops at the hem, folds hanging from the neck.
    cowl = lathe((0, -0.01, 1.6), (0, -0.01, 1.39),
                 [(0.0, 0.1), (0.25, 0.14), (0.55, 0.2), (0.85, 0.245), (1.0, 0.255)], M["leather"], "cowl", n=64, cap=False)

    def folds(p, n):
        a = np.arctan2(p[:, 0], p[:, 1])
        t = smooth01(1.6, 1.4, p[:, 2])
        return 0.012 * t * np.sin(a * 11 + 0.6 * np.sin(a * 3)) + 0.004 * np.sin(a * 36) * t ** 3 + fine(p, 0.0012, 45, 2)
    out.append(sculpt(cowl, folds))
    for k in range(18):   # the scalloped hem: a leather tongue at each fold
        a = 2 * math.pi * k / 18
        d = Vector((math.sin(a), math.cos(a), 0))
        out.append(make.box(Vector((0, -0.01, 1.385)) + d * 0.25, (0.03, 0.006, 0.025), M["leather"], bevel=0.004,
                            name="scallop", rot=Matrix.Rotation(-a, 4, "Z"), low=False))
    # The goggles, set deep in the hood: leather cups, brass bezels riveted, glass.
    for sx in (-1, 1):
        e = Vector((sx * 0.046, 0.142, 1.705))
        ax = Vector((sx * 0.25, 1, 0)).normalized()
        out.append(make.cyl(e - ax * 0.02, e + ax * 0.012, 0.031, M["leather_dark"], n=32, bevel=0.004, name="cup", low=0))
        out.append(make.torus(e + ax * 0.013, ax, 0.029, 0.0055, M["brass"], n=40, m=10, name="bezel", low=None))
        out.append(make.cyl(e + ax * 0.008, e + ax * 0.013, 0.025, M["glass"], n=32, bevel=0.001, name="lens", low=0))
        out += rivets(make, e + ax * 0.016, ax, 0.029, 8, M["brass"], size=0.0028)
    out.append(make.cyl((-0.02, 0.154, 1.708), (0.02, 0.154, 1.708), 0.006, M["brass"], n=10, name="bridge", low=0))
    # The beak: leather over a brass frame, banded at each joint, curving down to a capped point; the filter vents
    # slotted along its sides; a canister either cheek.
    path = [Vector((0, 0.135, 1.648)), Vector((0, 0.215, 1.635)), Vector((0, 0.29, 1.612)), Vector((0, 0.35, 1.582)),
            Vector((0, 0.385, 1.56))]
    radii = [0.056, 0.044, 0.031, 0.017, 0.005]
    for i in range(len(path) - 1):
        seg = make.cyl(path[i], path[i + 1], radii[i], M["leather"], n=36, bevel=0, r1=radii[i + 1], name="beak", low=0)
        out.append(seg)
        if i < 3:
            ax = (path[i + 1] - path[i]).normalized()
            out.append(make.torus(path[i + 1], ax, radii[i + 1] + 0.0006, 0.0042, M["brass"], n=36, m=8, name="band", low=None))
            out += rivets(make, path[i + 1] - ax * 0.008, ax, radii[i + 1] + 0.004, 10, M["brass"], size=0.0022)
    out.append(make.cyl(path[-2] + (path[-1] - path[-2]) * 0.4, path[-1] + Vector((0, 0.01, -0.006)), 0.011, M["brass"], n=20,
                        r1=0.001, name="beak_tip", low=0))
    for sx in (-1, 1):
        for k in range(4):
            t = 0.2 + k * 0.17
            p = path[0].lerp(path[1], t) if t < 1 else path[1]
            out.append(make.box(p + Vector((sx * (radii[0] - 0.006 - k * 0.003), 0, 0.004)), (0.0015, 0.012, 0.004), M["iron"],
                                bevel=0.001, name="vent", rot=Matrix.Rotation(sx * 0.25, 4, "Z"), low=False))
        c0 = Vector((sx * 0.086, 0.1, 1.625))
        c1 = c0 + Vector((sx * 0.03, 0.035, -0.012))
        out.append(make.cyl(c0, c1, 0.02, M["tin"], n=28, bevel=0.003, name="canister", low=0))
        out.append(make.torus(c1, c1 - c0, 0.017, 0.003, M["brass"], n=24, m=6, name="grille", low=None))
        for k in range(3):
            out.append(make.cyl(c1 + Vector((0, 0, (k - 1) * 0.009)) - Vector((sx * 0.012, 0, 0)),
                                c1 + Vector((0, 0, (k - 1) * 0.009)) + Vector((sx * 0.002, 0.012, 0)), 0.0018, M["brass"],
                                n=6, bevel=0, name="grille_bar", low=0))
    # The buckled strap round the back of the head, a brass buckle on it.
    strap = make.torus((0, -0.005, 1.705), (0, 0, 1), 0.116, 0.006, M["leather_dark"], n=64, m=6, name="strap", low=None)
    strap.scale = (1.0, 1.12, 0.7)
    out.append(strap)
    out.append(make.box((0.0, -0.137, 1.705), (0.016, 0.004, 0.012), M["brass"], bevel=0.002, name="buckle", low=False))
    # The air: two brass tubes from the cheek canisters back under the cowl.
    for sx in (-1, 1):
        out += make.pipe([(sx * 0.1, 0.1, 1.615), (sx * 0.125, 0.04, 1.585), (sx * 0.13, -0.06, 1.57), (sx * 0.1, -0.15, 1.52)],
                         0.008, M["brass"], name="tube", n=12, low=0)
    return out


# ----------------------------------------------------------------------------------------------------------------
# 2. The rail diver: a hammered copper helmet on a riveted corselet, caged ports.

def diver(tip, hc, make):
    M = materials(make)
    out = []
    c = Vector((0, 0.025, 1.705))
    R = 0.162

    def hammered(p, n):
        return -0.0015 * np.maximum(0, cook.noise_np(p, 11, 60)) ** 2 - 0.004 * np.maximum(0, cook.noise_np(p, 12, 8)) ** 4 \
            + fine(p, 0.0005, 150, 13)
    dome = ellipsoid(c, (R, R * 1.02, R * 1.05), M["copper"], "dome", keep=lambda f: f.z > 1.575)
    out.append(sculpt(dome, hammered))
    # The corselet: a riveted breastplate over the shoulders, and the brass neck ring the helmet screws down onto.
    cors = lathe((0, 0.01, 1.582), (0, 0.01, 1.43), [(0.0, 0.15), (0.2, 0.175), (0.45, 0.215), (0.75, 0.25), (0.95, 0.265), (1.0, 0.267)],
                 M["copper"], "corselet", n=72, cap=False)
    cors.data.transform(Matrix.Translation((0, 0.01, 0)) @ Matrix.Diagonal((1.0, 0.8, 1.0, 1.0)) @ Matrix.Translation((0, -0.01, 0)))
    out.append(sculpt(cors, hammered))
    lip = make.torus((0, 0.01, 1.432), (0, 0, 1), 0.266, 0.009, M["brass"], n=72, m=10, name="corselet_lip", low=None)
    lip.scale = (1.0, 0.8, 1.0)
    out.append(lip)
    out.append(make.torus((0, 0.02, 1.572), (0, 0, 1), 0.15, 0.013, M["brass"], n=64, m=12, name="neck_ring", low=None))
    out += rivets(make, (0, 0.02, 1.585), (0, 0, 1), 0.157, 28, M["brass"], size=0.004)
    # The wing nuts round the corselet's edge, studs through its lip.
    for k in range(10):
        a = 2 * math.pi * (k + 0.5) / 10
        d = Vector((math.sin(a) * 0.245, math.cos(a) * 0.195, 0))
        p = Vector((0, 0.01, 1.446)) + d
        out.append(make.cyl(p - Vector((0, 0, 0.004)), p + Vector((0, 0, 0.018)), 0.007, M["brass"], n=12, name="stud", low=0))
        out.append(make.box(p + Vector((0, 0, 0.022)), (0.018, 0.003, 0.007), M["brass"], bevel=0.002, name="wing",
                            rot=Matrix.Rotation(-a, 4, "Z"), low=False))
    # The ports: a big caged face port, a side port either side, a small one on top; each in a thick brass rim, riveted.

    def port(at, axis, r, bars):
        at, axis = Vector(at), Vector(axis).normalized()
        out.append(make.cyl(at - axis * 0.012, at + axis * 0.014, r + 0.014, M["brass"], n=48, bevel=0.004, name="port_rim", low=0))
        out.append(make.torus(at + axis * 0.015, axis, r + 0.006, 0.006, M["brass"], n=48, m=10, name="port_lip", low=None))
        out.append(make.cyl(at + axis * 0.008, at + axis * 0.012, r, M["glass"], n=48, bevel=0.001, name="port_glass", low=0))
        out.extend(rivets(make, at + axis * 0.015, axis, r + 0.012, 12, M["brass"], size=0.0032))
        u = axis.cross(Vector((0, 0, 1))).normalized() if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        up = u.cross(axis)
        for k in range(bars):
            off = (k - (bars - 1) / 2) * r * 0.55
            out.append(make.cyl(at + axis * 0.03 + up * off - u * r, at + axis * 0.03 + up * off + u * r, 0.0035, M["brass"],
                                n=10, bevel=0, name="cage", low=0))
        if bars:
            out.append(make.cyl(at + axis * 0.03 - up * r, at + axis * 0.03 + up * r, 0.0035, M["brass"], n=10, bevel=0, name="cage", low=0))
            for s in (-1, 1):
                out.append(make.cyl(at + axis * 0.012 + u * s * r * 1.05, at + axis * 0.03 + u * s * r, 0.004, M["brass"], n=8, bevel=0,
                                    name="cage_post", low=0))
    port(c + Vector((0, R * 1.02 - 0.006, -0.005)), (0, 1, 0), 0.068, 3)
    for sx in (-1, 1):
        d = Vector((sx * 0.94, 0.34, 0)).normalized()
        port(c + Vector((d.x * R, d.y * R * 1.02, 0.0)), d, 0.042, 2)
    port(c + Vector((0, 0.02, R * 1.05 - 0.01)), (0, 0.2, 1), 0.03, 0)
    # The player's colour: a painted band round the dome under the top port.
    zb0, zb1 = 0.075, 0.115
    out.append(lathe(c + Vector((0, 0, zb0)), c + Vector((0, 0, zb1)),
                     [(t, R * 1.012 * math.sqrt(max(0.0, 1 - ((zb0 + t * (zb1 - zb0)) / (R * 1.05)) ** 2))) for t in np.linspace(0, 1, 6)],
                     M["paint"], "band", n=72, cap=False))
    # The exhaust valve on the right, the air inlet elbow at the back, the hose away to the tank.
    v0 = c + Vector((0.12, 0.08, -0.085))
    out.append(make.cyl(v0, v0 + Vector((0.035, 0.03, 0)), 0.014, M["brass"], n=24, bevel=0.003, name="valve", low=0))
    out += make.handwheel(v0 + Vector((0.04, 0.034, 0)), (1, 0.8, 0), 0.016, M["brass"], spokes=4, name="valve_wheel")
    out += make.pipe([c + Vector((0, -R, 0.01)), c + Vector((0, -R - 0.05, 0.0)), c + Vector((0.02, -R - 0.08, -0.1)),
                      Vector((0.04, -0.24, 1.42))], 0.015, M["rubber"], name="hose", n=16, low=0)
    out.append(make.torus(c + Vector((0, -R - 0.02, 0.01)), (0, 1, 0), 0.017, 0.005, M["brass"], n=24, m=8, name="coupling", low=None))
    # The seam where the bonnet meets the breastplate, riveted.
    out += rivets(make, (0, 0.025, 1.62), (0, 0, 1), 0.148, 30, M["copper"], size=0.0035)
    return out


# ----------------------------------------------------------------------------------------------------------------
# 3. The gas-mask welder: a rubber hood, brass eyepieces, a chin filter on a corrugated hose, an aviator cap over it,
# and a welder's visor flipped up.

def welder(tip, hc, make):
    M = materials(make)
    out = []
    hc2 = Vector((0, 0.014, 1.682))

    def rubber(p, n):
        x, y, z = p[:, 0], p[:, 1] - hc2.y, p[:, 2] - hc2.z
        d = 0.002 * bell(x / 0.003) * (y > 0)                                # the moulding's seam down the face
        d += 0.0025 * np.sin(z * 160 + x * 40) * smooth01(1.62, 1.58, p[:, 2]) * (y > -0.02)   # wrinkles at the chin
        d += 0.003 * bell((np.hypot(x - np.sign(x) * 0.04, z - 0.017) - 0.036) / 0.006) * (y > 0.06)   # eyepiece welts
        return d + fine(p, 0.0006, 90, 21)
    hood = ellipsoid(hc2 + Vector((0, 0, -0.008)), (0.106, 0.138, 0.158), M["rubber"], "mask", keep=lambda f: f.z > 1.55 or f.y > -0.02 and f.z > 1.52)
    out.append(sculpt(hood, rubber))
    # The eyepieces: threaded brass rings, dark glass.
    for sx in (-1, 1):
        e = Vector((sx * 0.042, 0.138, 1.702))
        ax = Vector((sx * 0.35, 1, 0.05)).normalized()
        out.append(make.cyl(e - ax * 0.01, e + ax * 0.012, 0.03, M["brass"], n=40, bevel=0.003, name="eye_ring", low=0))
        for k in range(3):
            out.append(make.torus(e + ax * (0.002 + k * 0.004), ax, 0.031, 0.0016, M["brass"], n=40, m=6, name="thread", low=None))
        out.append(make.cyl(e + ax * 0.006, e + ax * 0.013, 0.024, M["glass"], n=40, bevel=0.001, name="lens", low=0))
    # The chin filter: a drum, knurled, its grille; two little cheek filters angled back.
    f0 = Vector((0, 0.148, 1.612))
    f1 = f0 + Vector((0, 0.06, -0.03))
    out.append(make.cyl(f0, f1, 0.038, M["tin"], n=48, bevel=0.004, name="filter", low=0))
    for k in range(24):
        a = 2 * math.pi * k / 24
        ax = (f1 - f0).normalized()
        u = ax.orthogonal().normalized()
        d = u * math.cos(a) + ax.cross(u) * math.sin(a)
        out.append(make.box(f0.lerp(f1, 0.72) + d * 0.038, (0.0025, 0.0025, 0.009), M["tin"], bevel=0.001, name="knurl",
                            rot=d.to_track_quat("X", "Z").to_matrix().to_4x4(), low=False))
    out.append(make.torus(f1, f1 - f0, 0.03, 0.004, M["brass"], n=32, m=8, name="filter_rim", low=None))
    for k in range(5):
        off = Vector(((k - 2) * 0.011, 0, 0))
        out.append(make.cyl(f1 + off + Vector((0, 0.002, 0.026)), f1 + off + Vector((0, 0.002, -0.026)), 0.002, M["brass"], n=6, bevel=0,
                            name="grille_bar", low=0))
    for sx in (-1, 1):
        c0 = Vector((sx * 0.083, 0.1, 1.612))
        c1 = c0 + Vector((sx * 0.035, 0.0, -0.02))
        out.append(make.cyl(c0, c1, 0.02, M["tin"], n=28, bevel=0.003, name="cheek_filter", low=0))
        out.append(make.torus(c1, c1 - c0, 0.017, 0.003, M["brass"], n=24, m=6, name="cheek_rim", low=None))
    # A wool muffler wound round the neck, where the hood stops.
    for k, (z, r) in enumerate(((1.565, 0.072), (1.54, 0.079), (1.515, 0.086))):
        wrap = make.torus((0, 0.012, z), (0, -0.3 + 0.08 * k, 1), r, 0.02, M["wool"], n=48, m=10, name="muffler", low=None)
        wrap.scale = (1.0, 1.22, 1.0)
        out.append(wrap)
    # The corrugated hose from the filter down to the chest.
    pts = [f1 + Vector((0, 0.005, -0.02)), Vector((0, 0.2, 1.54)), Vector((0.03, 0.2, 1.45)), Vector((0.06, 0.18, 1.36))]
    out += make.pipe(pts, 0.018, M["rubber"], name="hose", n=16, low=0)
    for i in range(len(pts) - 1):
        for t in np.linspace(0.08, 0.92, 7):
            p = pts[i].lerp(pts[i + 1], t)
            out.append(make.torus(p, pts[i + 1] - pts[i], 0.019, 0.0035, M["rubber"], n=20, m=6, name="corrugation", low=None))
    # The aviator cap over it all: leather (the player's colour), fleece at its edge, earflaps with their buckles.
    cap = ellipsoid((0, 0.004, 1.698), (0.114, 0.144, 0.15), M["dyed"], "cap",
                    keep=lambda f: f.z > 1.72 - 0.25 * max(0.0, -f.y) - 0.0 and not (f.y > 0.05 and f.z < 1.74))

    def quilted(p, n):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        return -0.002 * (bell(np.sin(np.arctan2(x, -y) * 5) / 0.12)) + fine(p, 0.001, 50, 22)
    out.append(sculpt(cap, quilted))
    fleece = make.torus((0, 0.006, 1.728), (0, -0.25, 1), 0.117, 0.009, M["wool"], n=64, m=10, name="fleece", low=None)
    fleece.scale = (1.0, 1.15, 1.0)
    out.append(fleece)
    for sx in (-1, 1):
        flap = ellipsoid((sx * 0.108, 0.008, 1.655), (0.012, 0.048, 0.06), M["dyed"], "earflap")
        out.append(sculpt(flap, lambda p, n: fine(p, 0.001, 50, 23), level=1))
        out.append(make.cyl((sx * 0.106, 0.03, 1.603), (sx * 0.07, 0.11, 1.57), 0.005, M["leather_dark"], n=8, bevel=0, name="chinstrap", low=0))
        out.append(make.box((sx * 0.108, 0.03, 1.608), (0.004, 0.012, 0.01), M["brass"], bevel=0.0015, name="buckle", low=False))
    # The welder's visor, flipped up on the forehead: a riveted steel shield on its pivots, a dark glass slot in it.
    # It turns about its own centre, as a real one does on its temple pivots, so flipped up it lies close over the cap.
    pivot = Vector((0, 0.012, 1.702))
    rot = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(62), 4, "X") @ Matrix.Translation(-pivot)
    shield = ellipsoid(pivot, (0.13, 0.158, 0.16), M["iron"], "visor", keep=lambda f: f.y > 0.095 and f.z > 1.62 and f.z < 1.8)
    shield.data.transform(rot)
    out.append(sculpt(shield, lambda p, n: -0.002 * np.maximum(0, cook.noise_np(p, 24, 20)) ** 2 + fine(p, 0.0005, 120, 25), level=1))
    # Its dark glass: a band across the shield, following it.
    glass = ellipsoid(pivot, (0.1315, 0.1595, 0.1615), M["glass"], "visor_glass",
                      keep=lambda f: f.y > 0.14 and 1.69 < f.z < 1.715 and abs(f.x) < 0.05)
    glass.data.transform(rot)
    out.append(glass)
    for sx in (-1, 1):
        p = Vector((sx * 0.13, pivot.y, pivot.z))
        out.append(make.cyl(p - Vector((sx * 0.004, 0, 0)), p + Vector((sx * 0.012, 0, 0)), 0.012, M["brass"], n=20, bevel=0.002, name="pivot", low=0))
    for k in range(9):
        a = -0.9 + 1.8 * k / 8
        p = Vector((math.sin(a) * 0.118, pivot.y + math.cos(a) * 0.144, 1.632))
        out.append(make.nail(rot @ p, rot.to_3x3() @ Vector((math.sin(a), math.cos(a), -0.3)), M["iron"], r=0.0035))
    return out


# ----------------------------------------------------------------------------------------------------------------
# 4. The boiler-plate sallet: a riveted iron helm with a tail over the neck, an eye slit, a breathing grille, a gauge.

def sallet(tip, hc, make):
    M = materials(make)
    out = []
    c = Vector((0, 0.005, 1.705))

    def plate(p, n):
        return -0.0025 * np.maximum(0, cook.noise_np(p, 31, 12)) ** 3 + fine(p, 0.0006, 110, 32)

    def flared(p, n):
        # The sallet's tail: the bowl sweeping out and down over the nape, as one plate.
        y, z = p[:, 1] - c.y, p[:, 2]
        tail = smooth01(0.0, -0.1, y) * smooth01(1.72, 1.6, z)
        # The crest: a ridge raised in the plate itself over the crown, front to back.
        ridge = 0.011 * bell(p[:, 0] / 0.007) * smooth01(1.74, 1.8, z)
        return plate(p, n) + 0.045 * tail + ridge
    bowl = ellipsoid(c, (0.122, 0.148, 0.142), M["rust"], "bowl", keep=lambda f: f.z > 1.62 or f.y < -0.01)
    out.append(sculpt(bowl, flared))
    # The visor: a front plate from the chin to the brow, its eye slit dark and glowing faintly.
    visor = ellipsoid((0, 0.012, 1.662), (0.13, 0.165, 0.15), M["iron"], "visor", keep=lambda f: f.y > 0.02 and f.z < 1.725)
    out.append(sculpt(visor, plate))
    out.append(make.box((0, 0.172, 1.702), (0.05, 0.007, 0.005), M["glass"], bevel=0.0015, name="slit", low=False))
    out.append(make.box((0, 0.175, 1.711), (0.056, 0.006, 0.0035), M["iron"], bevel=0.002, name="brow_lip", low=False))
    # The breathing holes, in rows under the slit.
    for row in range(3):
        for k in range(7 - row):
            x = (k - (6 - row) / 2) * 0.014
            z = 1.665 - row * 0.014
            y = 0.012 + 0.165 * math.sqrt(max(0.0, 1 - (x / 0.13) ** 2 - ((z - 1.662) / 0.15) ** 2)) + 0.001
            out.append(make.cyl((x, y - 0.004, z), (x, y + 0.001, z), 0.0035, M["glass"], n=10, bevel=0, name="hole", low=0))
            out.append(make.torus((x, y + 0.001, z), (0, 1, 0), 0.0045, 0.0012, M["iron"], n=12, m=4, name="hole_rim", low=None))
    # The crest ridge along the top (the player's colour), riveted; rivets round the visor's edge and the brim.
    for k in range(9):
        y = -0.12 + k * 0.03
        z = c.z + 0.138 * math.sqrt(max(0.0, 1 - (y / 0.142) ** 2)) + 0.002
        out.append(make.nail((0.012, y, z), (1, 0, 0.6), M["iron"], r=0.004))
        out.append(make.nail((-0.012, y, z), (-1, 0, 0.6), M["iron"], r=0.004))
    for k in range(16):
        a = -1.2 + 2.4 * k / 15
        p = Vector((math.sin(a) * 0.127, 0.012 + math.cos(a) * 0.161, 1.722))
        out.append(make.nail(p, (math.sin(a), math.cos(a), 0.3), M["iron"], r=0.0045))
    out.append(make.torus((0, 0.005, 1.63), (0, 0, 1), 0.121, 0.006, M["iron"], n=64, m=8, name="brim", low=None))
    # The visor's pivots, and the brass gauge on the left cheek, its pipe away round the back.
    for sx in (-1, 1):
        p = Vector((sx * 0.13, 0.03, 1.69))
        out.append(make.cyl(p, p + Vector((sx * 0.012, 0, 0)), 0.013, M["brass"], n=24, bevel=0.003, name="pivot", low=0))
    g0 = Vector((-0.126, 0.07, 1.655))
    ga = Vector((-1, 0.4, 0)).normalized()
    out.append(make.cyl(g0, g0 + ga * 0.016, 0.022, M["brass"], n=32, bevel=0.003, name="gauge", low=0))
    out.append(make.cyl(g0 + ga * 0.014, g0 + ga * 0.017, 0.018, M["canvas"], n=32, bevel=0, name="gauge_face", low=0))
    out.append(make.box(g0 + ga * 0.018, (0.001, 0.012, 0.0012), M["rust"], bevel=0, name="needle",
                        rot=Matrix.Rotation(0.5, 4, "X"), low=False))
    out += make.pipe([g0 - ga * 0.005, (-0.13, 0.02, 1.62), (-0.12, -0.08, 1.58), (-0.06, -0.17, 1.5)], 0.005, M["brass"],
                     name="gauge_pipe", n=10, low=0)
    # The gorget: three lames round the neck, riveted, overlapping down onto the shoulders.
    for k, (z, r) in enumerate(((1.585, 0.118), (1.56, 0.13), (1.535, 0.145))):
        # (The top lame painted: the player's colour, round the neck where it reads from every side.)
        lame = lathe((0, 0.0, z + 0.018), (0, 0.0, z - 0.018), [(0.0, r - 0.01), (1.0, r + 0.012)], M["paint"] if k == 0 else M["iron"],
                     f"lame{k}", n=56, cap=False)
        out.append(sculpt(lame, plate, level=1))
        out += rivets(make, (0, 0.0, z - 0.006), (0, 0, 1), r + 0.008, 16, M["iron"], size=0.0035, phase=k * 0.2)
    return out


GEAR = {"beak": beak, "diver": diver, "welder": welder, "sallet": sallet}


def gear(tip, hc, make):
    lights()
    return GEAR[CONCEPT](tip, hc, make)


VIEWS = [("full", (0.45, 1, 0.12), (0, 0, 0.98), 4.3), ("head", (0.55, 1, 0.12), (0, 0.04, 1.64), 1.05),
         ("profile", (1, 0.08, 0.04), (0, 0.06, 1.64), 1.0), ("back", (-0.55, -1, 0.35), (0, -0.05, 1.5), 1.5)]
crewfigure.build(f"concept-{CONCEPT}", crewfigure.Style(
    figure="bare", preview="CONCEPT_PREVIEW", gear=gear, views=VIEWS,
    what=f"a headgear concept ({CONCEPT}), not in the game"))
