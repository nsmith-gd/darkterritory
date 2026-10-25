"""A modelled face on Lee Perry-Smith's scan (the townsfolk, note 993; the director, 24 Oct, on a hooded townsperson close
up: "this can't seriously look like a human face"). crewfigure.build runs it when a Style asks (`face=True`):

  * the scan made one face, both sides the same (one side mirrored onto the other: the scan's two eyes weren't, one a
    shut slit, the other flecked), and subdivided once, so what's modelled on it is smooth; its map cleaned (`skin`);
  * the face's forms modelled into the mesh, a PS2-era figure's planes the bake and a low mesh both read (the scan's own
    are a few millimetres deep: decimated, they went flat): the brow ridge over the eyes, the cheekbones and the hollows
    under them, the nose's bridge, the jaw's angle and the chin;
  * the eyes open: the scan's shut lids drawn back into an almond, an eyeball behind each (white, an iris, a pupil),
    which the bake paints into the opening;
  * the game mesh decimated with the face kept dense and the skull under the hat let go (the same triangles);
  * the face's texels: the head's unwrap stretched over the face (the skull behind and above it, under a hat or hair,
    takes fewer), so the face holds at a close-up;
  * the face shaded where the hat, hood, hair or collar is over it (`contact`): the clothes' own occlusion, baked as a
    mask (the head bakes from the scan alone, so the face came out evenly lit to its edge, a plate against the hood).

Everything is measured from the scan's own landmarks, found on its texture map (`landmarks`)."""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

# Where the scan's map (Map-COL.jpg, 1024 px, measured down it) has the closed eyes' corners, each eye's two.
EYES_PX = (((410, 305), (470, 305)), ((550, 305), (610, 305)))


def landmarks(objs):
    """The nose's tip (the scan's most forward point), the eyes' middles on the shut lids (between the vertices whose
    texture coordinates fall at each eye's corners on its map) and their half width, in the scan's placed coordinates
    (its map the right way up: crewfigure turns it)."""
    co, uv = [], []
    for o in objs:
        me = o.data
        vco = np.array([v.co for v in me.vertices], np.float32)
        layer = me.uv_layers.active.data
        co.append(vco[np.array([lp.vertex_index for lp in me.loops])])
        uv.append(np.array([layer[lp.index].uv for lp in me.loops], np.float32))
    co, uv = np.concatenate(co), np.concatenate(uv)
    allco = np.concatenate([np.array([v.co for v in o.data.vertices], np.float32) for o in objs])
    tip = allco[allco[:, 1].argmax()]
    eyes, widths = [], []
    for corners in EYES_PX:
        ends = []
        for px in corners:
            d = np.linalg.norm(uv - np.array([px[0] / 1024, 1 - px[1] / 1024], np.float32), axis=1)
            ends.append(co[d.argsort()[:6]].mean(0))
        e = (ends[0] + ends[1]) / 2
        widths.append(abs(ends[0][0] - ends[1][0]) / 2)
        # On the map that's the lashes' line; the eye's middle is a little above it, on the lid's surface (the most
        # forward of the scan's vertices there).
        e[2] += 0.004
        near = allco[(np.abs(allco[:, 0] - e[0]) < 0.003) & (np.abs(allco[:, 2] - e[2]) < 0.003) & (allco[:, 1] > tip[1] - 0.08)]
        e[1] = near[:, 1].max()
        eyes.append(e)
    # One eye height, mirrored either side of the middle (the face is made symmetric).
    ex = (abs(eyes[0][0]) + abs(eyes[1][0])) / 2
    ey, ez = (eyes[0][1] + eyes[1][1]) / 2, (eyes[0][2] + eyes[1][2]) / 2
    return {"tip": Vector(tip), "eyes": [Vector((-ex, ey, ez)), Vector((ex, ey, ez))], "eye_w": float(np.mean(widths))}


def skin(path):
    """The scan's map, cleaned: below the jaw (its last 150 rows: the cut neck's flaps and a pale lozenge rimmed in red,
    which the throat takes) the rows above it mirrored down, skin continuing; and the lips' studio flush calmed."""
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)[::-1]          # (Blender's rows run up the map; these, down it as it's drawn)
    cut = int(h * 870 / 1024)
    for k in range(h - cut):
        px[cut + k] = px[cut - 1 - k]
    y0, y1, x0, x1 = int(h * 452 / 1024), int(h * 504 / 1024), int(w * 430 / 1024), int(w * 594 / 1024)
    lips = px[y0:y1, x0:x1, :3]
    red = np.clip((lips[..., 0] - lips[..., 1]) / 0.25 - 0.4, 0, 1)[..., None]
    calm = lips.mean(-1, keepdims=True) * np.array([1.12, 0.92, 0.86], np.float32)
    px[y0:y1, x0:x1, :3] = lips * (1 - 0.35 * red) + calm * 0.35 * red
    img.pixels.foreach_set(np.ascontiguousarray(px[::-1]).ravel())
    img.update()
    return img


def symmetric(objs):
    """Each object's +X half mirrored onto its -X half (bmesh's symmetrize: the mirrored half keeps its texture
    coordinates, so the map is the same either side too), then subdivided once (flat: its shape kept)."""
    for o in objs:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.symmetrize(bm, input=bm.verts[:] + bm.edges[:] + bm.faces[:], direction="X", dist=0.0005)
        bmesh.ops.remove_doubles(bm, verts=[v for v in bm.verts if abs(v.co.x) < 0.0008], dist=0.0004)
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bm.to_mesh(o.data)
        bm.free()
        # (The scan's own split normals and sharp edges don't survive the mirror: a seam down the nose.)
        for a in ("custom_normal", "sharp_edge", "sharp_face"):
            if a in o.data.attributes:
                o.data.attributes.remove(o.data.attributes[a])
        o.data.shade_smooth()
        o.data.update()


def model(objs, lm, brow=1.0):
    """The face's forms pushed into the scan, about the landmarks (`lm`): the brow ridge, the cheekbones and the hollows
    under them, the bridge of the nose, the jaw's angle and the chin. `brow` scales the brow ridge."""
    tip, (el, er) = lm["tip"], lm["eyes"]
    ex, ey, ez = er.x, er.y, er.z
    for o in objs:
        me = o.data
        co = np.array([v.co for v in me.vertices], np.float32)
        x, y, z = co[:, 0], co[:, 1], co[:, 2]
        ax = np.abs(x)
        # Only the front of the face moves (nothing behind the ears).
        front = np.clip((y - (tip.y - 0.12)) / 0.03, 0, 1)
        d = np.zeros_like(co)
        # The brow ridge: a bar of bone over each eye, out over the eye's middle and fading to the temple, and a groove
        # above it; the glabella between the brows a little forward.
        ridge = np.exp(-((z - (ez + 0.017)) / 0.0075) ** 2) * np.exp(-((ax - ex * 0.85) / 0.03) ** 2)
        d[:, 1] += 0.0035 * brow * ridge
        d[:, 2] += 0.0012 * brow * ridge
        d[:, 1] -= 0.0012 * brow * np.exp(-((z - (ez + 0.036)) / 0.008) ** 2) * np.exp(-((ax - ex * 0.8) / 0.03) ** 2)
        # The eye sockets deepened under the ridge and at the inner corners (the ridge reads by its shadow).
        d[:, 1] -= 0.0025 * np.exp(-((z - (ez + 0.006)) / 0.006) ** 2) * np.exp(-((ax - ex * 0.55) / 0.012) ** 2)
        # The nose's bridge between the eyes, standing out of the sockets.
        d[:, 1] += 0.0022 * np.exp(-((z - ez) / 0.012) ** 2) * np.exp(-(x / 0.006) ** 2)
        # The cheekbones: out and forward under the outer corner of each eye; the hollow under each, in.
        cheek = np.exp(-((z - (ez - 0.024)) / 0.011) ** 2) * np.exp(-((ax - (ex + 0.012)) / 0.016) ** 2)
        d[:, 0] += 0.0028 * np.sign(x) * cheek
        d[:, 1] += 0.0032 * cheek
        hollow = np.exp(-((z - (ez - 0.058)) / 0.013) ** 2) * np.exp(-((ax - (ex + 0.012)) / 0.014) ** 2)
        d[:, 0] -= 0.0018 * np.sign(x) * hollow
        d[:, 1] -= 0.0012 * hollow
        # The jaw's angle: out at the back corner, a hard line from it toward the chin.
        jz = tip.z - 0.088
        jaw = np.exp(-((z - jz) / 0.012) ** 2) * np.exp(-((ax - 0.056) / 0.016) ** 2)
        d[:, 0] += 0.003 * np.sign(x) * jaw
        line = np.exp(-((z - (jz - 0.004 + (0.056 - ax) * 0.25)) / 0.006) ** 2) * (ax < 0.06) * (ax > 0.012)
        d[:, 0] += 0.0012 * np.sign(x) * line
        d[:, 1] += 0.0012 * line
        # The chin: forward and down a little, squared.
        chin = np.exp(-((z - (tip.z - 0.088)) / 0.012) ** 2) * np.exp(-(x / 0.018) ** 4)
        d[:, 1] += 0.0035 * chin
        d[:, 2] -= 0.0015 * chin
        co += d * front[:, None]
        for v, c in zip(me.vertices, co):
            v.co = c
        me.update()


def decimate(objs, target, lm, first=1.2):
    """The game mesh's head: collapsed to about `target` triangles, symmetrically, the face kept dense and the skull
    behind and above it, under the hat or hair, collapsed most: first the whole to `first` times the target, then only
    the skull (a vertex group the collapse is held to) the rest of the way."""
    def tris():
        return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)

    def collapse(ratio, group=None):
        for o in objs:
            m = o.modifiers.new("decimate", "DECIMATE")
            m.decimate_type = "COLLAPSE"
            m.ratio = ratio
            m.use_collapse_triangulate = True
            m.use_symmetry = True
            m.symmetry_axis = "X"
            if group:
                m.vertex_group = group
                m.invert_vertex_group = True
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.modifier_apply(modifier=m.name)

    if tris() > target * first:
        collapse(target * first / tris())
    tip = lm["tip"]
    for o in objs:
        co = np.array([v.co for v in o.data.vertices], np.float32)
        face = np.clip((co[:, 1] - (tip.y - 0.11)) / 0.04, 0, 1) * np.clip((tip.z + 0.1 - co[:, 2]) / 0.03, 0, 1)
        # (And the ears: a skull collapsed round them takes them off.)
        ears = (np.abs(co[:, 0]) > 0.07) & (np.abs(co[:, 2] - (tip.z + 0.015)) < 0.045) & (co[:, 1] > tip.y - 0.21)
        face = np.maximum(face, ears.astype(np.float32))
        g = o.vertex_groups.new(name="face")
        for vi, k in enumerate(face):
            g.add([vi], float(k), "REPLACE")
    if tris() > target:
        collapse(target / tris(), "face")
    for o in objs:
        o.vertex_groups.remove(o.vertex_groups["face"])


def head_uv(head_c, tip):
    """The head's one island (crewfigure's cylinder round the neck, its seam down the back), its face stretched: the
    bearings across the face and the heights from chin to brow take about twice the texels of the skull's."""
    def warp(a):
        # Integral of 1 + 1.6 bell(a / 0.9): the front's bearings spread.
        return a + 1.6 * 0.9 * math.sqrt(math.pi) / 2 * math.erf(a / 0.9)

    def lift(z):
        return z + 1.1 * 0.06 * math.sqrt(math.pi) / 2 * math.erf((z - (tip.z - 0.005)) / 0.06)

    def uv(f, uvl):
        # Each face's bearings taken round from its first corner's (a face across the seam at the back, or round the
        # crown, stays in one piece: no sliver across the island).
        bs = [math.atan2(lp.vert.co.x - head_c.x, lp.vert.co.y - head_c.y) for lp in f.loops]
        bs = [bs[0] + math.remainder(b - bs[0], 2 * math.pi) for b in bs]
        for lp, a in zip(f.loops, bs):
            lp[uvl].uv = ((warp(a) - warp(-math.pi)) * 0.62 / (2 * math.pi), lift(lp.vert.co.z) - 1.5)
    return uv


def contact(head, covers, reach=0.05, rays=14):
    """How much of the sky each of the scan's vertices sees past the clothes over it (`covers`: the hat, the hood, the
    hair, the collar; objects in place): 0 open, 1 shut in. Returns a mask fn for overbake's bake (positions -> 0..1,
    the scan's own vertices only; every other high copy 0)."""
    dg = bpy.context.evaluated_depsgraph_get()
    trees = [BVHTree.FromObject(o, dg) for o in covers]
    # Directions over a hemisphere (a golden spiral), turned onto each vertex's normal.
    k = np.arange(rays) + 0.5
    cz = 1 - k / rays
    ph = k * math.pi * (3 - math.sqrt(5))
    sr = np.sqrt(1 - cz * cz)
    hemi = np.stack([sr * np.cos(ph), sr * np.sin(ph), cz], 1)
    pts, vals = [], []
    for o in head:
        me = o.data
        for v in me.vertices:
            n = v.normal
            t = n.orthogonal().normalized()
            b = n.cross(t)
            p0 = v.co + n * 0.0015
            shut = 0.0
            for hx, hy, hz in hemi:
                d = t * hx + b * hy + n * hz
                for tree in trees:
                    hit = tree.ray_cast(p0, d, reach)
                    if hit[0] is not None:
                        shut += 1.0 - hit[3] / reach * 0.5
                        break
            pts.append(v.co.copy())
            vals.append(shut / rays)
    print("[dt] face contact: mean %.2f, max %.2f" % (float(np.mean(vals)), float(np.max(vals))))
    kd = KDTree(len(pts))
    for i, p in enumerate(pts):
        kd.insert(p, i)
    kd.balance()
    vals = np.array(vals, np.float32)

    def mask(co, kind=""):
        if kind:
            return np.zeros(len(co), np.float32)
        out = np.zeros(len(co), np.float32)
        for i, p in enumerate(co):
            _, j, dist = kd.find(Vector(p))
            if dist < 0.002:
                out[i] = vals[j]
        return out
    return mask


def where(head_c):
    """A mask fn: each point's place on the head, (|x|, y, z) about its centre, 0..1 (mirrored: the face's grade, its
    soot and sallow, the same either side)."""
    def mask(co, kind=""):
        q = co - np.array([head_c.x, head_c.y, head_c.z], np.float32)
        return np.clip(np.stack([np.abs(q[:, 0]) / 0.2, q[:, 1] / 0.4 + 0.5, q[:, 2] / 0.4 + 0.5], 1), 0, 1)
    return mask


# The box round the eyes the `eyes` mask measures in (m): its 8 bits a quarter of a millimetre across it.
EYE_BOX = (0.064, 0.04, 0.04)


def eyes_at(lm):
    """A mask fn: each point's place in the box round the eyes, (|x|, z, y) from its corner, 0..1, fine enough to paint
    an eye by (`paint_eyes`)."""
    e = lm["eyes"][1]
    lo = np.array([0.0, e.z - EYE_BOX[1] / 2, e.y - EYE_BOX[2] * 0.75], np.float32)

    def mask(co, kind=""):
        q = np.stack([np.abs(co[:, 0]), co[:, 2], co[:, 1]], 1) - lo
        return np.clip(q / np.array(EYE_BOX, np.float32), 0, 1)
    return mask


def paint_eyes(base, at, face, lm, iris=(0.16, 0.1, 0.06)):
    """Both eyes painted open on the shut lids, the same either side (`at`: the `eyes` mask's bake, HxWx3; `face`: the
    head's texels), as a figure of the PS2's was: an almond of white, dulled into its corners and under the upper lid's
    shadow, the iris under the lid's edge, the pupil, the lashes' dark line along the top and a fainter one under it,
    the lid's crease above. The geometry keeps the scan's shut lid, smooth over the eyeball's curve."""
    e = lm["eyes"][1]
    w = lm["eye_w"] * 0.88
    x = at[..., 0] * EYE_BOX[0]
    z = at[..., 1] * EYE_BOX[1] + e.z - EYE_BOX[1] / 2
    y = at[..., 2] * EYE_BOX[2] + e.y - EYE_BOX[2] * 0.75
    near = face & (at[..., 2] > 0.02) & (at[..., 2] < 0.98) & (np.abs(y - e.y) < 0.008)
    u = (x - e.x) / w
    inside_u = np.clip(1 - u * u, 0, 1)
    top = e.z + 0.0005 + 0.0042 * inside_u ** 0.75
    bottom = e.z - 0.0008 - 0.0026 * inside_u ** 0.9
    soft = 0.00035
    almond = np.clip((top - z) / soft, 0, 1) * np.clip((z - bottom) / soft, 0, 1) * (np.abs(u) < 1) * near
    out = base.copy()
    sclera = np.array([0.36, 0.32, 0.28], np.float32)
    # The white dulled toward its corners and under the lid's edge (the lid's shadow on the ball).
    dull = 0.55 + 0.45 * (1 - np.abs(u) ** 2) * np.clip((top - z) / 0.0025, 0.35, 1)
    eye = sclera * dull[..., None]
    # The iris, its top under the lid, darker at its rim; the pupil.
    ic = (e.x, e.z + 0.0004)
    r = np.hypot(x - ic[0], z - ic[1])
    ir = np.clip((0.0052 - r) / 0.0004, 0, 1)
    rim = np.clip((r - 0.0036) / 0.0016, 0, 1)
    irisc = np.array(iris, np.float32) * (1 - 0.5 * rim[..., None])
    eye = eye * (1 - ir[..., None]) + irisc * ir[..., None]
    pup = np.clip((0.0021 - r) / 0.0003, 0, 1)
    eye = eye * (1 - pup[..., None]) + np.array([0.015, 0.012, 0.01], np.float32) * pup[..., None]
    # A glint, the same on both (up and toward the nose's side).
    gl = np.clip((0.0007 - np.hypot(x - (ic[0] - 0.0017), z - (ic[1] + 0.0017))) / 0.0003, 0, 1)
    eye = eye * (1 - 0.7 * gl[..., None]) + 0.7 * gl[..., None] * np.array([0.7, 0.68, 0.64], np.float32)
    out = out * (1 - almond[..., None]) + eye * almond[..., None]
    # The lashes along the top edge, thickest at its outer half; a fainter line along the bottom; the crease above.
    band = (np.abs(u) < 1.08) & near
    lash = np.clip(1 - np.abs(z - top - 0.0003) / (0.00055 + 0.0003 * np.clip(u, 0, 1)), 0, 1) * band
    lower = np.clip(1 - np.abs(z - bottom + 0.0002) / 0.0004, 0, 1) * band * 0.45
    crease = np.clip(1 - np.abs(z - (top + 0.0036 + 0.0008 * inside_u)) / 0.0011, 0, 1) * band * 0.3 * inside_u
    dark = np.clip(lash + lower + crease, 0, 1)
    ink = np.array([0.025, 0.018, 0.015], np.float32)
    return out * (1 - dark[..., None]) + ink * dark[..., None]
