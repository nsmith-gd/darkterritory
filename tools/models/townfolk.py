"""The townsfolk lookbook's clothes (ARCHITECTURE §8 note 694), modelled over the survivors' figure as tools/models
crewfigure builds it: each garment a game mesh made to the figure's own surface (crewfigure.Fit: ray-cast off the
frame's dense copy and the head scan), in kit materials named "town.<what>" that a recipe's DRESS says the library
layer of, so the bake gives them the crew's depth: their weave, their seams, their folds and their dirt.

    import townfolk as tf
    def garments(fit):
        return [tf.bucket_hat(fit, "hat"), tf.gas_mask(fit)]
    crewfigure.build("townsfolk_trapper", crewfigure.Style(..., garments=garments))

Blender's axes: Z up, the figure facing +Y; a bearing is 0 at the front, π/2 the figure's left (+X).
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import crewfigure

TAU = 2 * math.pi


def mat(name):
    """The kit material "town.<name>" (one per name; the recipe's DRESS dresses it)."""
    key = "town." + name
    return bpy.data.materials.get(key) or bpy.data.materials.new(key)


def _object(name, verts, faces, material):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(material)
    for p in me.polygons:
        p.use_smooth = True
    return o


def _apply(o, mod):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=mod.name)


def sheet(rows, material, name, closed=False, outward=None, thick=0.0, cap=None):
    """A surface through `rows` (lists of points, all the same length) in quads, its outside facing away from
    `outward` (a point: each face's own way from it; None: as wound), given a thickness `thick` inward (its edges
    then show, a hem's or a brim's); `cap` "top" or "bottom" fans the last or first row shut (a crown's top)."""
    n = len(rows[0])
    verts = [Vector(v) for r in rows for v in r]
    faces = []
    for i in range(len(rows) - 1):
        for j in range(n if closed else n - 1):
            j1 = (j + 1) % n
            faces.append((i * n + j, i * n + j1, (i + 1) * n + j1, (i + 1) * n + j))
    if cap:
        row = len(rows) - 1 if cap == "top" else 0
        ring = [row * n + j for j in range(n)]
        verts.append(sum((verts[k] for k in ring), Vector()) / n)
        c = len(verts) - 1
        faces += [(ring[j], ring[(j + 1) % n], c) for j in range(n)]
    o = _object(name, verts, faces, material)
    if outward is not None:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        for f in bm.faces:
            if f.normal.dot(f.calc_center_median() - Vector(outward)) < 0:
                f.normal_flip()
        bm.to_mesh(o.data)
        bm.free()
    if thick > 0:
        mod = o.modifiers.new("thick", "SOLIDIFY")
        # (Not evened: where a cloth folds back on itself, a cowl's edge, an even offset throws a corner metres out. Clamped
        # to its edges' length for the same reason.)
        mod.thickness, mod.offset, mod.use_even_offset = thick, -1.0, False
        mod.thickness_clamp = 1.0
        _apply(o, mod)
    return o


def tube(a, b, r, material, name="tube", n=12, caps=True, r1=None, rings=2):
    """A cylinder from `a` to `b`, radius `r` (to `r1` at b), closed at its ends; `rings` round it along its length (more
    for a high copy to sculpt along: a can's ribs)."""
    a, b = Vector(a), Vector(b)
    t = (b - a).normalized()
    u = t.orthogonal().normalized()
    v = t.cross(u)
    rows = []
    for k in range(rings):
        f = k / (rings - 1)
        p, rr = a.lerp(b, f), r + ((r if r1 is None else r1) - r) * f
        rows.append([p + (u * math.cos(TAU * j / n) + v * math.sin(TAU * j / n)) * rr for j in range(n)])
    o = sheet(rows, material, name, closed=True, outward=(a + b) / 2)
    if caps:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(o.data)
        bm.free()
    return o


def ring(centre, axis, major, minor, material, name="ring", n=16, m=6):
    """A torus round `axis` at `centre` (an eyepiece's rim, a key ring)."""
    axis = Vector(axis).normalized()
    u = axis.orthogonal().normalized()
    v = axis.cross(u)
    rows = []
    for i in range(m):
        b = TAU * i / m
        rows.append([Vector(centre) + (u * math.cos(TAU * j / n) + v * math.sin(TAU * j / n)) * (major + minor * math.cos(b))
                     + axis * (minor * math.sin(b)) for j in range(n)])
    o = sheet(rows + [rows[0]], material, name, closed=True)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data)
    bm.free()
    return o


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    objs[0].name = name
    return objs[0]


def bearings(n, start=0.0, end=TAU, closed=True):
    return [start + (end - start) * j / (n if closed else n - 1) for j in range(n)]


def worn(objs, name, bone=None, stiff=0.0):
    """The pieces as one garment on `bone` (None: the frame's weights where it lies)."""
    return crewfigure.Garment(join(objs, name) if len(objs) > 1 else objs[0], bone=bone, stiff=stiff)


# ----------------------------------------------------------------------------------------------------------------
# Hats

def crown(fit, band, ease, top, profile, n=24, dent=0.0, lean=0.0):
    """A hat's crown: from its band round the head at `band` (a height, or fn(bearing) for a tilted band), `ease` off
    the skull, up to `top`, its radius at each step of the way the band's times `profile` [(t up, factor)]; the top's
    middle pressed down by `dent`, the whole drawn forward by `lean` (m at the top)."""
    zb = band if callable(band) else (lambda a, b=band: b)
    rows = []
    for t, k in profile:
        row = []
        for a in bearings(n):
            z0 = zb(a)
            r = fit.radius("head", z0, a) + ease
            p = fit.head_c + Vector((math.sin(a) * r * k, math.cos(a) * r * k, 0))
            row.append(Vector((p.x, p.y + lean * t * t, z0 + (top - z0) * t)))
        rows.append(row)
    centre = sum(rows[-1], Vector()) / n - Vector((0, 0, dent))
    return rows, centre


def capped(rows, centre, material, name, thick=0.0):
    o = sheet(rows, material, name, closed=True, outward=centre - Vector((0, 0, 0.12)), thick=thick)
    n = len(rows[0])
    bm = bmesh.new()
    bm.from_mesh(o.data)
    top = [v for v in bm.verts if any((v.co - Vector(p)).length < 1e-6 for p in rows[-1])]
    c = bm.verts.new(centre)
    ordered = sorted(top, key=lambda v: math.atan2(v.co.x - centre.x, v.co.y - centre.y))
    for j in range(len(ordered)):
        f = bm.faces.new((ordered[j], ordered[(j + 1) % len(ordered)], c))
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data)
    bm.free()
    return o


def brim(fit, band, ease, wide, droop, material, name, n=24, back=0.0, front=0.0, roll=0.0, thick=0.006):
    """A brim out from the band: `wide` (more behind and before by `back` and `front`), its edge dropped by `droop`
    (negative: turned up), rolled up at its very edge by `roll`."""
    zb = band if callable(band) else (lambda a, b=band: b)
    rows = []
    for t in (0.0, 0.5, 1.0) if roll == 0 else (0.0, 0.4, 0.75, 1.0):
        row = []
        for a in bearings(n):
            c = math.cos(a)
            w = wide + back * max(0.0, -c) + front * max(0.0, c)
            r = fit.radius("head", zb(a), a) + ease + w * t
            z = zb(a) - droop * t * t + roll * max(0.0, t - 0.75) / 0.25
            row.append(fit.head_c + Vector((math.sin(a) * r, math.cos(a) * r, 0)) + Vector((0, 0, z - fit.head_c.z)))
        rows.append(row)
    return sheet(rows, material, name, closed=True, thick=thick, outward=fit.head_c - Vector((0, 0, 0.5)))


def bucket_hat(fit, what="hat"):
    """A bucket hat in oilskin: a soft crown dented on top, its brim turned down all round."""
    m = mat(what)
    # (Its stitching, round the crown's foot and round and round the brim, is sculpted on its high copy: SHAPES.)
    rows, c = crown(fit, 1.70, 0.016, 1.83, [(0, 1.0), (0.3, 0.98), (0.6, 0.93), (0.82, 0.84), (0.94, 0.7), (1.0, 0.45)], n=20, dent=0.01)
    return worn([capped(rows, c, m, "bucket_crown"), brim(fit, 1.70, 0.016, 0.065, 0.06, m, "bucket_brim", n=20, back=0.012)], "bucket", "head")


def flat_cap(fit, what="cap", goggles=None):
    """A flat cap, full and drawn forward over a short stiff peak; with goggles pushed up on its front."""
    m = mat(what)
    band = lambda a: 1.722 - 0.018 * math.cos(a)
    rows, c = crown(fit, band, 0.014, 1.775, [(0, 1.0), (0.4, 1.13), (0.75, 1.12), (0.92, 0.96), (1.0, 0.4)], lean=0.035)
    peak = []
    for t, drop in ((0.0, 0.0), (0.5, 0.006), (1.0, 0.016)):
        row = []
        for a in bearings(11, -0.95, 0.95, closed=False):
            r = fit.radius("head", band(a), a) + 0.014 + t * 0.055 * max(0.0, math.cos(a)) ** 0.7
            row.append(fit.head_c + Vector((math.sin(a) * r, math.cos(a) * r, band(a) - drop - fit.head_c.z)))
        peak.append(row)
    parts = [capped(rows, c, m, "cap_crown"), sheet(peak, m, "cap_peak", thick=0.005, outward=fit.head_c - Vector((0, 0, 0.5)))]
    if goggles:
        brass, glass = mat(goggles[0]), mat(goggles[1])
        for sx in (-1, 1):
            at = fit.head(1.748, sx * 0.3, 0.055)
            nrm = Vector((sx * 0.15, 1.0, 0.7)).normalized()
            parts.append(tube(at - nrm * 0.014, at + nrm * 0.008, 0.028, brass, "goggle"))
            parts.append(tube(at + nrm * 0.008, at + nrm * 0.0095, 0.022, glass, "goggle_glass"))
    return worn(parts, "flatcap", "head")


# ----------------------------------------------------------------------------------------------------------------
# Masks

def facepiece(fit, material, brow=1.705, chin=1.505, half=1.05, ease=0.012, snout=0.03, thick=0.005):
    """A rubber facepiece moulded over the face from the brow to under the chin, pushed out at the mouth."""
    rows = []
    levels, n = 7, 11
    for i in range(levels + 1):
        z = brow + (chin - brow) * i / levels
        w = half * (0.72 + 0.28 * math.sin(math.pi * i / levels))
        row = []
        for a in bearings(n, -w, w, closed=False):
            front = max(0.0, math.cos(a))
            push = snout * front * front * math.exp(-((z - 1.585) / 0.045) ** 2)
            under = (1.55 - z) * 0.8 if z < 1.55 else 0.0
            p = fit.head(max(z, 1.53), a, ease + push - under)
            row.append(Vector((p.x, p.y, z)))
        rows.append(row)
    return sheet(rows, material, "facepiece", thick=thick, outward=fit.head_c)


CAN = Vector((0, 1.0, -0.5)).normalized()


def gas_mask(fit, rubber="rubber", brass="brass", glass="glass", tin="tin", strap="strap"):
    """A gas mask: the facepiece, two round eyepieces in brass, a filter can screwed in under the nose, its straps."""
    parts = [facepiece(fit, mat(rubber))]
    for sx in (-1, 1):
        at = fit.head(1.659, sx * 0.36, 0.02)
        nrm = Vector((sx * 0.25, 1.0, 0.05)).normalized()
        parts.append(tube(at - nrm * 0.006, at + nrm * 0.012, 0.027, mat(brass), "eyepiece", n=10))
        parts.append(tube(at + nrm * 0.012, at + nrm * 0.0135, 0.021, mat(glass), "lens", n=10, caps=False))
    # The can's ribs are sculpted on its high copy (SHAPES), not modelled.
    root = fit.head(1.585, 0.0, 0.03)
    d = CAN
    parts.append(tube(root - d * 0.01, root + d * 0.025, 0.026, mat(rubber), "can_neck", n=10, r1=0.029, caps=False))
    parts.append(tube(root + d * 0.025, root + d * 0.085, 0.036, mat(tin), "can", rings=7))
    for z in (1.6, 1.705):
        rows = [[fit.head(zz, a, 0.006) for a in bearings(9, -math.pi * 0.7 + math.pi, math.pi * 0.7 + math.pi, closed=False)]
                for zz in (z - 0.011, z + 0.011)]
        parts.append(sheet(rows, mat(strap), "mask_strap", thick=0.003, outward=fit.head_c))
    return worn(parts, "gas_mask", "head")


# ----------------------------------------------------------------------------------------------------------------
# Cloth made here

def plaid(name, base, bands, lines, repeat=4.0, size=256):
    """A tartan woven in code (the market woman's shawl): `base`, crossed both ways by broad `bands` and thin `lines`
    (linear RGB), box-projected in object space `repeat` times a metre, as make.lib projects the library's layers."""
    import numpy as np
    u = np.arange(size) / size
    def sett(x):
        broad = (np.abs(((x * 2) % 1) - 0.5) < 0.12).astype(np.float32)
        thin = (np.abs(((x * 2 + 0.25) % 1) - 0.5) < 0.02).astype(np.float32)
        return broad, thin
    bx, tx = sett(u)
    b = (bx[None, :] + bx[:, None]) / 2
    t = np.maximum(tx[None, :], tx[:, None])
    # A twill's grain: the weave's diagonal, faint.
    grain = 0.92 + 0.08 * ((np.arange(size)[None, :] + np.arange(size)[:, None]) % 4 < 2)
    col = (np.array(base)[None, None, :] * (1 - b[..., None]) + np.array(bands)[None, None, :] * b[..., None])
    col = col * (1 - t[..., None]) + np.array(lines)[None, None, :] * t[..., None]
    col = col * grain[..., None]
    img = bpy.data.images.new(name, size, size)
    img.colorspace_settings.name = "Linear Rec.709"
    img.pixels.foreach_set(np.concatenate([col, np.ones((size, size, 1))], -1).astype(np.float32).ravel())
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    coord, mapping, tex = nt.nodes.new("ShaderNodeTexCoord"), nt.nodes.new("ShaderNodeMapping"), nt.nodes.new("ShaderNodeTexImage")
    mapping.inputs["Scale"].default_value = (repeat, repeat, repeat)
    nt.links.new(coord.outputs["Object"], mapping.inputs["Vector"])
    tex.image, tex.projection, tex.projection_blend = img, "BOX", 0.25
    nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.95
    m["dt_metal"] = 0.0
    return m


# ----------------------------------------------------------------------------------------------------------------
# Over the head

def hooded(fit, material, name, foot=1.5, brow=1.72, half=1.0, ease=0.012, fullness=0.01, thick=0.004, top=1.8,
           n=24, levels=10, cowl=None, peak=0.0):
    """A cloth over the head (a headscarf, a hood): from `foot` up over the top, open round the face below `brow` by
    `half` (rad either side of the front), `ease` off the skull and `fullness` more behind; below the ears it falls
    straight. `cowl` (a height): it goes on down over the shoulders to it, out over them; `peak`: its top drawn
    forward (m)."""
    rows = []
    lo = cowl if cowl is not None else foot
    for i in range(levels + 1):
        t = i / levels
        z = lo + (top - lo) * math.sin(t * math.pi / 2)
        g = half if z < brow else half * max(0.0, 1 - (z - brow) / 0.035)
        if cowl is not None and z < foot:
            g = half * (0.75 + 0.25 * (z - cowl) / (foot - cowl))
        g = max(g, 0.03)
        shrink = math.cos(min(1.0, max(0.0, (z - 1.72) / (top - 1.72 + 0.005))) ** 2.2 * math.pi / 2)
        row = []
        for a in bearings(n, g, TAU - g, closed=False):
            back = max(0.0, -math.cos(a))
            if cowl is not None and z < foot:
                s = (foot - z) / (foot - cowl)
                r = (fit.radius("head", 1.56, a) + ease + 0.03) * (1 - s) + (fit.radius("body", max(z, 1.4), a) + 0.03) * s
                c = fit.head_c * (1 - s)
            else:
                # The cloth's edge round the face tucked onto the head (eased off it, the edge stood proud of the face: a
                # hard rim round a flat face, the director's note 993).
                tuck = 1.0 if g >= TAU / 2 else min(1.0, max(0.25, (min(a - g, TAU - g - a)) / 0.35))
                r = max(fit.radius("head", min(z, 1.79), a), fit.radius("head", 1.62, a) * 0.93 if z < 1.62 else 0.0) + ease * tuck + fullness * back
                if z > 1.72:
                    r = (fit.radius("head", 1.72, a) + ease * tuck + fullness * back) * max(0.02, shrink)
                c = fit.head_c
            fwd = peak * max(0.0, (z - 1.72) / (top - 1.72))
            row.append(Vector((c.x + math.sin(a) * r, c.y + math.cos(a) * r + fwd, z)))
        rows.append(row)
    return sheet(rows, material, name, thick=thick, outward=fit.head_c)


def headscarf(fit, what="scarf"):
    """A headscarf over the hair and the ears, from the brow round the face and down the nape, knotted under the chin."""
    m = mat(what)
    parts = [hooded(fit, m, "headscarf", foot=1.5, brow=1.72, half=0.95, ease=0.012, fullness=0.012, thick=0.004, n=20, levels=8)]
    knot = fit.head(1.515, 0.0, 0.03)
    for sx in (-1, 1):
        side = fit.head(1.6, sx * 0.95, 0.014)
        pts = [side.lerp(knot, t) - Vector((0, 0, 0.012 * math.sin(t * math.pi))) for t in (0, 0.25, 0.5, 0.75, 1.0)]
        parts.append(band(pts, 0.028, 0.006, m, "scarf_tie", centre=fit.head_c))
        parts.append(band([knot, knot + Vector((sx * 0.02, 0.01, -0.04)), knot + Vector((sx * 0.035, 0.002, -0.09))], 0.03, 0.004, m,
                          "scarf_tail", centre=fit.head_c))
    parts.append(tube(knot - Vector((0, 0, 0.015)), knot + Vector((0, 0, 0.015)), 0.02, m, "scarf_knot", n=8))
    return worn(parts, "headscarf", "head")


def band(path, width, thick, material, name, centre):
    """A strap or a tie along `path`, `width` across (lying across the way round `centre`), `thick`."""
    path = [Vector(p) for p in path]
    a, b = [], []
    for i, p in enumerate(path):
        along = path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]
        out = Vector((p.x - centre.x, p.y - centre.y, 0))
        out = out.normalized() if out.length > 1e-6 else Vector((0, 1, 0))
        across = along.cross(out)
        across = across.normalized() if across.length > 1e-9 else Vector((1, 0, 0))
        a.append(p - across * width / 2)
        b.append(p + across * width / 2)
    rows = [[a[i], b[i]] for i in range(len(path))]
    return sheet(rows, material, name, thick=thick, outward=Vector((centre.x, centre.y, sum(p.z for p in path) / len(path))))


# ----------------------------------------------------------------------------------------------------------------
# Over the body

def front(fit, z, x, out=0.0):
    """The body's surface at height `z`, `x` across its front, `out` off it."""
    a = 0.0
    for _ in range(4):
        r = max(fit.radius("body", z, a) + out, abs(x) + 0.001)
        a = math.asin(max(-1.0, min(1.0, x / r)))
    return fit.body(z, a, out)


def back_of(fit, z, x, out=0.0):
    a = math.pi
    for _ in range(4):
        r = max(fit.radius("body", z, a) + out, abs(x) + 0.001)
        a = math.pi - math.asin(max(-1.0, min(1.0, x / r)))
    return fit.body(z, a, out)


def hang(fit, top, z, a):
    """How far out cloth hung from `top` is at `z`: the furthest the body comes out anywhere between."""
    r, h = 0.0, z
    while h <= top + 1e-4:
        r = max(r, fit.radius("body", h, a))
        h += 0.03
    return max(r, fit.radius("body", top, a))


def skirt_radius(fit, top, hem, flare, ease, z, a):
    t = max(0.0, min(1.0, (top - z) / (top - hem)))
    folds = 0.012 * t * (0.6 * math.sin(a * 8 + z * 3) + 0.4 * math.sin(a * 13.6 - z * 3.9 + 1.3))
    return hang(fit, top, z, a) + ease + flare * t ** 1.4 + folds


def skirt(fit, material, name, top=0.98, hem=0.3, flare=0.11, ease=0.012, n=22, levels=6):
    """A skirt from the waist down to `hem`, hung from the hips and flaring, folded; its waistband."""
    rows = []
    for i in range(levels + 1):
        z = top + (hem - top) * i / levels
        rows.append([Vector((math.sin(a) * r, math.cos(a) * r, z)) for a in bearings(n)
                     for r in (skirt_radius(fit, top, hem, flare, ease, z, a),)])
    return sheet(rows, material, name, closed=True, thick=0.006, outward=Vector((0, 0, 0.6)))


def apron(fit, material, name, top=0.975, hem=0.38, skirt_of=(0.98, 0.3, 0.11, 0.012), half=(0.19, 0.26)):
    """A long apron lying over the skirt's front from the waist, tied round it."""
    rows = []
    for i in range(7):
        t = i / 6
        z = top + (hem - top) * t
        w = half[0] + (half[1] - half[0]) * t
        row = []
        for j in range(9):
            x = -w + 2 * w * j / 8
            a = 0.0
            for _ in range(5):
                r = max(skirt_radius(fit, *skirt_of[:2], skirt_of[2], skirt_of[3], z, a) + 0.014, abs(x) + 0.001)
                a = math.asin(max(-1.0, min(1.0, x / r)))
            r = max(skirt_radius(fit, skirt_of[0], skirt_of[1], skirt_of[2], skirt_of[3], z, a + d) for d in (-0.08, 0, 0.08)) + 0.014
            row.append(Vector((math.sin(a) * r, math.cos(a) * r, z)))
        rows.append(row)
    parts = [sheet(rows, material, name, thick=0.004, outward=Vector((0, -0.3, 0.7)))]
    waist = [[fit.body(z, a, 0.03) for a in bearings(28)] for z in (0.955, 0.99)]
    parts.append(sheet(waist, material, name + "_band", closed=True, thick=0.004, outward=Vector((0, 0, 0.97))))
    return join(parts, name)


def shawl(fit, material, name):
    """A shawl round the shoulders like a short cape, hanging clear of the arms, down the back to its point; its ends
    brought across the chest, the left's over the right's, to the waist."""
    def radius(z, a):
        side = abs(math.sin(a))
        cape = side * (0.21 + max(0.0, 1.5 - z) * 0.45)
        return max(fit.radius("body", max(z, 1.1), a) + 0.02, cape) + 0.004 * max(0.0, 1.5 - z) / 0.4
    rows = []
    levels, n = 6, 17
    for i in range(levels + 1):
        t = i / levels
        z = 1.51 - (1.51 - 1.08) * t
        opening = 0.42 + 0.45 * min(1.0, t * 2.5)
        w = (math.pi - opening) if t < 0.45 else (math.pi - opening) * max(0.04, (1 - t) / 0.55)
        rows.append([Vector((math.sin(a) * radius(z, a), math.cos(a) * radius(z, a), z)) for a in bearings(n, math.pi - w, math.pi + w, closed=False)])
    parts = [sheet(rows, material, name, thick=0.005, outward=Vector((0, 0, 1.3)))]
    for sx in (-1, 1):
        rows = []
        for i in range(9):
            t = i / 8
            z = 1.44 - (1.44 - 0.98) * t
            x, half = sx * (0.13 - 0.25 * t), 0.085 - 0.02 * t
            o = 0.03 + (0.008 if sx > 0 else 0.0) + 0.01 * (1 - t)
            rows.append([front(fit, z, x + (j - 1.5) * half / 1.5, o) for j in range(4)])
        parts.append(sheet(rows, material, name + "_end", thick=0.005, outward=Vector((0, -0.3, 1.2))))
    return join(parts, name)


# ----------------------------------------------------------------------------------------------------------------
# The rest of the lookbook's hats

def ushanka(fit, fur="fur", crown_of="fur"):
    """A fur ushanka: deep pile all round and over, its flaps down over the ears to the jaw, the front flap turned up."""
    m, mc = mat(fur), mat(crown_of)
    rows, c = crown(fit, 1.69, 0.03, 1.87, [(0, 1.0), (0.3, 1.02), (0.6, 0.98), (0.85, 0.85), (1.0, 0.5)], dent=-0.004)
    parts = [capped(rows, c, mc, "ushanka_crown")]
    for sx in (-1, 1):
        rows = []
        for i in range(5):
            t = i / 4
            z = 1.71 - 0.2 * t
            w = 0.62 * math.sqrt(max(0.15, 1 - t ** 4))
            rows.append([Vector((p.x, p.y, z)) for a in bearings(7, sx * math.pi / 2 - w, sx * math.pi / 2 + w, closed=False)
                         for p in (fit.head(max(z, 1.56), a, 0.022 + 0.01 * t),)])
        parts.append(sheet(rows, m, "ushanka_flap", thick=0.026, outward=fit.head_c))
    rows = [[Vector((p.x, p.y, z)) for a in bearings(11, -1.15, 1.15, closed=False) for p in (fit.head(1.69, a, lean),)]
            for z, lean in ((1.69, 0.03), (1.75, 0.045), (1.8, 0.058))]
    parts.append(sheet(rows, m, "ushanka_front", thick=0.024, outward=fit.head_c + Vector((0, 0, 0.06))))
    rows = [[Vector((p.x, p.y, z)) for a in bearings(9, math.pi - 1.0, math.pi + 1.0, closed=False) for p in (fit.head(1.69, a, lean),)]
            for z, lean in ((1.69, 0.03), (1.74, 0.04))]
    parts.append(sheet(rows, m, "ushanka_back", thick=0.022, outward=fit.head_c + Vector((0, 0, 0.05))))
    return worn(parts, "ushanka", "head")


def wide_hat(fit, what="felt", ribbon="ribbon"):
    """A wide-brimmed hat of felt, its crown low and flat, a ribbon round it, the brim's edge rolled up a little."""
    m = mat(what)
    rows, c = crown(fit, 1.72, 0.016, 1.865, [(0, 1.0), (0.4, 0.99), (0.8, 0.96), (0.95, 0.9), (1.0, 0.7)], dent=0.008)
    hb = [[Vector((p.x * 1.0, p.y, p.z)) for p in r] for r in (rows[0], [q.lerp(s, 0.2) for q, s in zip(rows[0], rows[1])])]
    hb = [[fit.head_c + Vector(((p - fit.head_c).x * 1.025, (p - fit.head_c).y * 1.025, (p - fit.head_c).z)) for p in r] for r in hb]
    return worn([capped(rows, c, m, "widehat_crown"), sheet(hb, mat(ribbon), "widehat_band", closed=True, thick=0.002, outward=fit.head_c),
                 brim(fit, 1.72, 0.016, 0.16, 0.015, m, "widehat_brim", n=32, roll=0.022, thick=0.007)], "widehat", "head")


def beanie(fit, what="knit"):
    """A knitted cap close to the head, its cuff turned up."""
    m = mat(what)
    rows, c = crown(fit, 1.69, 0.013, 1.835, [(0, 1.0), (0.4, 0.98), (0.75, 0.86), (0.92, 0.66), (1.0, 0.35)])
    cuff = [[fit.head(z, a, 0.026) for a in bearings(24)] for z in (1.685, 1.735)]
    return worn([capped(rows, c, m, "beanie_crown"), sheet(cuff, m, "beanie_cuff", closed=True, thick=0.012, outward=fit.head_c)], "beanie", "head")


def captains_cap(fit, cloth="navy", peak="peak", braid="braid"):
    """A peaked cap: its crown wide and flat over the band, a black shining peak, a gold cord, a badge."""
    m = mat(cloth)
    rows = []
    for z, more, k in ((1.705, 0.014, 1.0), (1.75, 0.016, 1.0), (1.785, 0.05, 1.0), (1.8, 0.058, 1.0), (1.808, 0.05, 1.0)):
        row = []
        for a in bearings(28):
            front = max(0.0, math.cos(a))
            r = fit.radius("head", min(z, 1.75), a) + more + front * (0.02 if z > 1.75 else 0)
            tilt = 0.012 * front - 0.005 * max(0.0, -math.cos(a)) if z > 1.75 else 0.0
            row.append(fit.head_c + Vector((math.sin(a) * r, math.cos(a) * r, z + tilt - fit.head_c.z)))
        rows.append(row)
    c = sum(rows[-1], Vector()) / len(rows[-1]) + Vector((0, 0, 0.002))
    parts = [capped(rows, c, m, "captcap_crown")]
    pk = []
    for t, drop in ((0.0, 0.0), (0.5, 0.01), (1.0, 0.03)):
        pk.append([fit.head_c + Vector((math.sin(a) * r, math.cos(a) * r, 1.705 - drop - fit.head_c.z))
                   for a in bearings(13, -0.95, 0.95, closed=False)
                   for r in (fit.radius("head", 1.705, a) + 0.014 + t * 0.062 * max(0.0, math.cos(a)) ** 0.7,)])
    parts.append(sheet(pk, mat(peak), "captcap_peak", thick=0.005, outward=fit.head_c - Vector((0, 0, 0.5))))
    parts.append(band([fit.head(1.716, a, 0.021) for a in bearings(7, -0.75, 0.75, closed=False)], 0.008, 0.004, mat(braid), "cord", fit.head_c))
    badge = fit.head(1.755, 0.0, 0.02)
    parts.append(tube(badge - Vector((0, 0.004, 0)), badge + Vector((0, 0.006, 0)), 0.016, mat(braid), "badge", n=10))
    return worn(parts, "captcap", "head")


# ----------------------------------------------------------------------------------------------------------------
# Faces and hair

def wrap(fit, what="wrap"):
    """A wool wrap wound over the nose and mouth and round the neck, in folded turns, knotted behind, its tails down."""
    m = mat(what)
    rows = []
    for i in range(7):
        t = i / 6
        z = 1.49 + (1.645 - 1.49) * t
        turn = 0.007 if i % 2 else 0.0
        rows.append([fit.head_c + Vector((math.sin(a) * r, math.cos(a) * r, z - fit.head_c.z)) for a in bearings(24)
                     for r in (max(fit.radius("head", max(z, 1.5), a), fit.radius("head", 1.6, a) * 0.86) + 0.02 + turn,)])
    parts = [sheet(rows, m, "wrap", closed=True, thick=0.008, outward=fit.head_c)]
    knot = fit.head(1.57, math.pi, 0.035)
    parts.append(tube(knot - Vector((0, 0, 0.018)), knot + Vector((0, 0, 0.018)), 0.022, m, "wrap_knot", n=8))
    for sx in (-1, 1):
        parts.append(band([knot, knot + Vector((sx * 0.02, -0.02, -0.06)), knot + Vector((sx * 0.03, -0.03, -0.13))], 0.035, 0.005, m,
                          "wrap_tail", fit.head_c))
    return worn(parts, "wrap", "head")


def hair_up(fit, what="hair"):
    """Hair drawn back off the face and up into a bun at the back of the head, close to the skull, its hairline over
    the brow and round the ears."""
    m = mat(what)
    rows = []
    n = 24
    def line(a):
        # The hairline: over the brow in front, round above the ears, down to the nape behind.
        c = math.cos(a)
        return 1.725 if c > 0.6 else 1.725 - (0.6 - c) / 1.6 * 0.16
    for i in range(9):
        t = i / 8
        row = []
        for a in bearings(n):
            z0 = line(a)
            z = z0 + (1.80 - z0) * math.sin(t * math.pi / 2)
            r = (fit.radius("head", min(z, 1.79), a) + 0.009) * (math.cos(max(0.0, (z - 1.77) / 0.035) * math.pi / 2) if z > 1.77 else 1.0)
            row.append(Vector((fit.head_c.x + math.sin(a) * r, fit.head_c.y + math.cos(a) * r, z)))
        rows.append(row)
    c = sum(rows[-1], Vector()) / n + Vector((0, 0, 0.004))
    parts = [capped(rows, c, m, "hair_cap")]
    bun = fit.head(1.72, math.pi, 0.03)
    rows = []
    for k in range(7):
        b = math.pi * k / 6
        rows.append([bun + Vector((math.sin(b) * math.cos(TAU * j / 12) * 0.045, -math.cos(b) * 0.04, math.sin(b) * math.sin(TAU * j / 12) * 0.04))
                     for j in range(12)])
    parts.append(sheet(rows, m, "bun", closed=True, outward=bun))
    return worn(parts, "hair", "head")

def beard(fit, what="beard"):
    """A full white beard from below the lip round the jaw, sideburns up to the ears, and a moustache over the lip."""
    m = mat(what)
    parts = []
    rows = []
    for i in range(6):
        t = i / 5
        z = 1.585 - (1.585 - 1.47) * t
        out = 0.008 + 0.028 * t
        row = []
        for a in bearings(13, -1.65, 1.65, closed=False):
            p = fit.head(max(z, 1.52), a, out)
            under = (1.53 - z) * 0.6 if z < 1.53 else 0.0
            d = Vector((p.x - fit.head_c.x, p.y - fit.head_c.y, 0))
            row.append(Vector((p.x, p.y, z)) - d * under)
        rows.append(row)
    parts.append(sheet(rows, m, "beard", thick=0.01, outward=fit.head_c))
    for sx in (-1, 1):
        rows = [[fit.head(z, sx * a, 0.007) for a in (1.05, 1.3, 1.6)] for z in (1.67, 1.63, 1.59)]
        parts.append(sheet(rows, m, "sideburn", thick=0.006, outward=fit.head_c))
    lip = [fit.head(1.603, a, 0.01) + Vector((0, 0, -0.004 * abs(a) / 0.5)) for a in bearings(7, -0.5, 0.5, closed=False)]
    parts.append(band(lip, 0.016, 0.008, m, "moustache", fit.head_c))
    return worn(parts, "beard", "head")


# ----------------------------------------------------------------------------------------------------------------
# On the body

def bib_apron(fit, what="apron", tie="tie"):
    """A butcher's bib apron from the chest to the knees, lying on the belly and hanging from it; its neck strap and ties."""
    m = mat(what)
    def p(z, x):
        a = 0.0
        for _ in range(5):
            rr = (fit.radius("body", z, a) if z >= 1.05 else hang(fit, 1.12, z, a)) + 0.016
            a = math.asin(max(-1.0, min(1.0, x / max(rr, abs(x) + 0.001))))
        rr = (fit.radius("body", z, a) if z >= 1.05 else hang(fit, 1.12, z, a)) + 0.016
        return Vector((math.sin(a) * rr, math.cos(a) * rr, z))
    rows = [[p(z, -h + 2 * h * j / 8) for j in range(9)]
            for z, h in ((1.42, 0.12), (1.3, 0.13), (1.15, 0.17), (1.02, 0.22), (0.86, 0.235), (0.7, 0.24), (0.52, 0.245))]
    parts = [sheet(rows, m, "bib", thick=0.004, outward=Vector((0, -0.4, 1.0)))]
    for sx in (-1, 1):
        neck = [p(1.42, sx * 0.1), front(fit, 1.47, sx * 0.1, 0.016), Vector((sx * 0.1, 0.09, 1.52)), Vector((sx * 0.1, 0.0, 1.545)),
                Vector((sx * 0.1, -0.08, 1.52))]
        parts.append(band(neck, 0.022, 0.003, mat(tie), "neck_strap", Vector((0, 0, 1.45))))
        parts.append(band([fit.body(0.99, sx * a, 0.016) for a in bearings(8, 1.0, math.pi - 0.05, closed=False)], 0.022, 0.003, mat(tie),
                          "apron_tie", Vector((0, 0, 0.99))))
    return worn(parts, "bib_apron", None, stiff=0.5)


def over_shoulder(fit, x, out, back, frontz, n=5):
    """A path up the back from `back` m, over the shoulder's top at `x` and down the front to `frontz`."""
    path = [back_of(fit, back + (1.46 - back) * i / n, x, out) for i in range(n)]
    path += [Vector((x, -0.08 - out * 0.5, 1.5 + out * 0.7)), Vector((x, 0.0, 1.53 + out)), Vector((x, 0.09 + out * 0.5, 1.5 + out * 0.7))]
    path += [front(fit, 1.46 + (frontz - 1.46) * i / n, x, out) for i in range(n + 1)]
    return path


def stole(fit, what="stole", cross="cross_ink"):
    """A priest's stole round the back of his neck and down his front to the knees, a cross near each end, fringed."""
    parts = []
    for sx in (-1, 1):
        path = over_shoulder(fit, sx * 0.1, 0.01, 1.47, 1.42, 3)[2:]
        path += [front(fit, 1.42 - (1.42 - 0.6) * i / 10, sx * (0.1 - 0.03 * min(1.0, i / 3)), 0.01 + 0.004 * i / 10) for i in range(1, 11)]
        parts.append(band(path, 0.085, 0.004, mat(what), "stole", Vector((0, 0, 1.0))))
        c = front(fit, 0.74, sx * 0.07, 0.017)
        parts.append(tube(c - Vector((0, 0, 0.04)), c + Vector((0, 0, 0.04)), 0.006, mat(cross), "stole_cross", n=4))
        parts.append(tube(c + Vector((-0.024, 0, 0.012)), c + Vector((0.024, 0, 0.012)), 0.006, mat(cross), "stole_cross", n=4))
    return worn(parts, "stole", None, stiff=0.3)


def pectoral_cross(fit, what="brass"):
    m = mat(what)
    c = front(fit, 1.22, 0.0, 0.02)
    parts = [tube(c - Vector((0, 0, 0.05)), c + Vector((0, 0, 0.05)), 0.011, m, "cross", n=4),
             tube(c + Vector((-0.033, 0, 0.022)), c + Vector((0.033, 0, 0.022)), 0.011, m, "cross", n=4)]
    for sx in (-1, 1):
        chain = [c + Vector((0, 0, 0.05))] + [front(fit, 1.27 + 0.21 * t, sx * 0.09 * t, 0.016) for t in (0.3, 0.6, 1.0)] + [Vector((sx * 0.09, 0.0, 1.53))]
        parts += [tube(a, b, 0.0025, m, "chain", n=4) for a, b in zip(chain, chain[1:])]
    return worn(parts, "pectoral", "spine_03")


def binoculars(fit, body="binocular", brass="brass", glass="glass", strap="strap"):
    """Binoculars hung on the chest by their strap round the neck."""
    parts = []
    for sx in (-1, 1):
        top, foot = front(fit, 1.36, sx * 0.042, 0.04), front(fit, 1.2, sx * 0.042, 0.045)
        parts.append(tube(foot, top, 0.033, mat(body), "barrel"))
        parts.append(tube(top, top + Vector((0, -0.004, 0.03)), 0.026, mat(body), "eyecup"))
        parts.append(tube(foot - Vector((0, 0, 0.008)), foot + Vector((0, 0, 0.014)), 0.036, mat(brass), "barrel_rim"))
        parts.append(tube(foot - Vector((0, 0, 0.0095)), foot - Vector((0, 0, 0.008)), 0.028, mat(glass), "objective"))
    mid = front(fit, 1.32, 0.0, 0.032)
    parts.append(tube(mid - Vector((0, 0, 0.04)), mid + Vector((0, 0, 0.04)), 0.016, mat(body), "hinge"))
    # Up from the left barrel, over the shoulder, across the back of the neck, and down to the right one.
    path = [front(fit, 1.38, -0.06, 0.035)] + over_shoulder(fit, -0.085, 0.012, 1.44, 1.4)[::-1]
    path += over_shoulder(fit, 0.085, 0.012, 1.44, 1.4) + [front(fit, 1.38, 0.06, 0.035)]
    parts.append(band(path, 0.016, 0.004, mat(strap), "binocular_strap", Vector((0, 0, 1.4))))
    return worn(parts, "binoculars", "spine_03")


def bottles(fit, steel="bottle", brass="brass", strap="strap", hose="rubber"):
    """Two bottles on the back in a harness, their valves; the harness's straps over the shoulders and a band round."""
    parts = []
    for x in (-0.065, 0.065):
        b = back_of(fit, 0.98, x, 0.07)
        prof = [(0.05, 0.005), (0.06, 0.03), (0.06, 0.4), (0.045, 0.45), (0.016, 0.47)]
        rows = [[b + Vector((math.cos(TAU * j / 10) * r, math.sin(TAU * j / 10) * r, h)) for j in range(10)] for r, h in prof]
        parts.append(sheet(rows, mat(steel), "bottle", closed=True, outward=b + Vector((0, 0, 0.25))))
        parts.append(tube(b + Vector((0, 0, 0.47)), b + Vector((0, 0, 0.52)), 0.016, mat(brass), "valve", n=8))
    for sx in (-1, 1):
        parts.append(band(over_shoulder(fit, sx * 0.13, 0.014, 1.1, 1.2), 0.035, 0.005, mat(strap), "harness", Vector((0, 0, 1.3))))
    parts.append(band([fit.body(1.1, a, 0.012) for a in bearings(9, math.pi - 0.9, math.pi + 0.9, closed=False)], 0.035, 0.005, mat(strap),
                      "harness_band", Vector((0, 0, 1.1))))
    return worn(parts, "bottles", "spine_03")


def coil(fit, wire="wire", strap="strap"):
    """A coil of cable at the left hip, on a strap across the chest from over the right shoulder."""
    c = fit.body(0.98, -math.pi / 2, 0.09)
    parts = []
    for w in (0.0, 0.012, -0.012):
        parts.append(ring(c + Vector((w, 0, 0)), (1, 0, 0), 0.16, 0.012, mat(wire), "coil", n=24, m=6))
    path = [c + Vector((0.0, 0.0, 0.17))] + [front(fit, 1.12 + 0.32 * t, -0.2 + 0.33 * t, 0.016) for t in (0.0, 0.25, 0.5, 0.75, 1.0)]
    path += over_shoulder(fit, 0.13, 0.016, 1.12, 1.44)[::-1][1:]
    parts.append(band(path, 0.035, 0.005, mat(strap), "coil_strap", Vector((0, 0, 1.2))))
    return worn(parts, "coil", "spine_02")


def buttons(fit, what="brass", rows_x=(-0.075, 0.075)):
    """Brass buttons down a double-breasted front."""
    parts = []
    for x in rows_x:
        for i in range(4):
            z = 1.36 - i * 0.115
            at = front(fit, z, x * (1 - 0.06 * i), 0.002)
            nrm = Vector((at.x, at.y, 0)).normalized()
            parts.append(tube(at, at + nrm * 0.006, 0.013, mat(what), "button", n=10))
    return worn(parts, "buttons", None)


# ----------------------------------------------------------------------------------------------------------------
# A recipe's shared parts

VIEWS = [("front", (0.2, 1, 0.1), (0, 0, 0.95), 4.2), ("back", (-0.3, -1, 0.1), (0, 0, 0.95), 4.2),
         ("face", (0.15, 1, -0.1), (0, 0.05, 1.62), 0.7), ("profile", (1, 0.12, 0.08), (0, 0.03, 1.2), 2.2)]


def softer(objs, centre):
    """The scan's face made a woman's (the market woman, the schoolteacher): the jaw and the chin drawn in, the brow
    smoothed back, the nose a little smaller."""
    import numpy as np
    for o in objs:
        me = o.data
        co = np.array([v.co for v in me.vertices], np.float32)
        x, y, z = co[:, 0] - centre.x, co[:, 1] - centre.y, co[:, 2]
        jaw = np.clip((1.64 - z) / 0.12, 0, 1) * np.clip((z - 1.48) / 0.04, 0, 1)
        # (The jaw's sides only: drawn in across the mouth too, it pursed the lips, note 1033.)
        co[:, 0] = centre.x + x * (1 - 0.1 * jaw * np.clip((np.abs(x) - 0.025) / 0.03, 0, 1))
        nose = np.exp(-((z - 1.62) / 0.035) ** 2) * np.exp(-(x / 0.02) ** 2) * (y > 0.09)
        co[:, 1] -= nose * (y - 0.09) * 0.25
        brow = np.exp(-((z - 1.69) / 0.015) ** 2) * (y > 0.08)
        co[:, 1] -= brow * 0.004
        for v, c in zip(me.vertices, co):
            v.co = c
        me.update()


def soften_face(base, face, amount=0.6):
    """The scan's stubble taken off the lower face (a box blur in the atlas, over the face's texels)."""
    import numpy as np
    k = 6
    pad = np.pad(base, ((k, k), (k, k), (0, 0)), mode="edge")
    c = pad.cumsum(0).cumsum(1)
    blur = (c[2 * k:, 2 * k:] - c[:-2 * k, 2 * k:] - c[2 * k:, :-2 * k] + c[:-2 * k, :-2 * k]) / (2 * k) ** 2
    blur = blur[: base.shape[0], : base.shape[1]]
    lift = blur * 1.06
    return np.where(face[..., None], base * (1 - amount) + lift * amount, base)


def recipe(name, what, dress, garments, coat=True, off=("strap", "satchel", "lamp"), head=None, grade=None, low_shapes=None,
           masks=None, shapes=None, scarf=False, head_tris=2000):
    crewfigure.build(name, crewfigure.Style(
        dress=dress, preview="TOWN_PREVIEW", figure="bare", hats=False, coat=coat, body_off=off, garments=garments, head=head,
        grade=grade, low_shapes=low_shapes, masks=masks, shapes={**SHAPES, **(shapes or {})}, views=VIEWS, scarf=scarf, face=True, head_tris=head_tris,
        what=f"the townsfolk lookbook's {what}, modelled over tools/blender/crew.py"))


# ----------------------------------------------------------------------------------------------------------------
# Sculpted on the high copies (crewfigure's shapes: metres along the normal at each point)

def _bell(x):
    import numpy as np
    return np.exp(-x * x)


def hat_stitching(p, n):
    """A bucket hat's stitching: a groove round the crown's foot, and the brim stitched round and round."""
    import numpy as np
    z = p[:, 2]
    r = np.hypot(p[:, 0], p[:, 1] - 0.006)
    d = -0.0018 * _bell((z - 1.722) / 0.003) - 0.0014 * _bell((z - 1.738) / 0.003)
    brim = z < 1.703
    d += -0.0012 * _bell(np.sin(r * np.pi / 0.011) / 0.15) * brim
    return d.astype(np.float32)


def ribbed(p, n):
    """A filter can's ribs, round it every two centimetres, and its grille's dimples at its end."""
    import numpy as np
    along = p @ np.array(CAN, np.float32)
    return (0.0025 * _bell(np.sin(along * np.pi / 0.02) / 0.2)).astype(np.float32)


SHAPES = {"town.hat": hat_stitching, "town.tin": ribbed}
