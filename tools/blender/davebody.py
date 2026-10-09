"""Dave's body (ARCHITECTURE §8 note 491; the director, 8 Oct: "a unique model so he's recognizable from afar ... Dave
doesn't have a beard, he's a bit tubby on the belly and has glasses. He's extremely fashionable and wears Birks sandals
often. He loves vests and cool hats"). The crew's figure built the crew's way (tools/blender/crewbody.py: blocked in
solids, voxel-remeshed, retopologised, weighted by the rig's regions), in his own clothes:

  * a linen shirt over a belly he's fond of, open at the throat, its collar turned down, its sleeves rolled past the
    elbow; bare forearms and hands (a painter's: no gloves);
  * trousers turned up above the ankle, a belt under the belly;
  * bare feet in sandals: a cork footbed on a dark tread, two broad straps across, each with its buckle outside.

His five waistcoats are the figure's variants (`vest_0`..`vest_4`, P1's `DaveKit.Vests` in order: mustard corduroy,
plum velvet, tartan, teal brocade, a patchwork of his paints): each a shell cut from the body's own dense surface where a
waistcoat covers it (the V to the top button, the points at the hem, the armholes), let out over the belly and
hanging straight off it below, weighted as the body under it is, so it moves with him and nothing comes through it.

    DT_CREW=dave blender -b --python tools/blender/crew.py     # (tools/models/recipes/dave.py bakes it)
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

import crewbody
from crewbody import box, ellipsoid, loft, sweep
from rig import Mat, hexc

# The shirt's body, under the belt to the collar: (z, half-width, front, back, squareness). The belly's the front out
# low down (1.0-1.2 m: about 5 cm before the chest's line), the back as the crew's.
TORSO = [(0.93, 0.186, 0.152, 0.142, 0.85), (0.98, 0.192, 0.172, 0.139, 0.83), (1.04, 0.198, 0.196, 0.138, 0.8),
         (1.1, 0.201, 0.206, 0.14, 0.78), (1.16, 0.201, 0.202, 0.143, 0.76), (1.22, 0.203, 0.187, 0.146, 0.74),
         (1.28, 0.207, 0.172, 0.148, 0.72), (1.33, 0.209, 0.155, 0.145, 0.7), (1.38, 0.208, 0.142, 0.138, 0.67),
         (1.415, 0.2, 0.131, 0.128, 0.65), (1.45, 0.18, 0.118, 0.116, 0.68), (1.48, 0.148, 0.104, 0.104, 0.75),
         (1.51, 0.106, 0.09, 0.092, 0.95), (1.54, 0.078, 0.074, 0.078, 1.0)]

# The sandals' footbed, heel to toe: (y, half-width) about the foot's line.
SOLE = [(-0.085, 0.036), (-0.06, 0.046), (0.0, 0.047), (0.08, 0.053), (0.15, 0.057), (0.2, 0.051), (0.226, 0.038),
        (0.238, 0.02)]
HEEL, TOE = -0.092, 0.244
BED = 0.03          # the footbed's top: the foot stands on it
FOOT_X = 0.105      # each foot's line (the crew's boots')
STRAPS = (0.035, 0.125)   # the instep's strap and the toes'


def ring(z, rx, front, back, sq, n=40, notch=None):
    """A ring of the torso at z, its front half out to `front` and its back to `back`; `notch(x)` (m) takes the front
    in where the shirt's open at the throat."""
    out = []
    for j in range(n):
        a = 2 * math.pi * j / n
        sa, ca = math.sin(a), math.cos(a)
        sa, ca = math.copysign(abs(sa) ** sq, sa), math.copysign(abs(ca) ** sq, ca)
        x, y = sa * rx, ca * (front if ca > 0 else back)
        if notch is not None and ca > 0:
            y -= notch(x)
        out.append((x, y, z))
    return out


def front_at(z):
    """The torso's front (y at x = 0) at z, between its rings."""
    zs = [t[0] for t in TORSO]
    return float(np.interp(z, zs, [t[2] for t in TORSO]))


def throat(z):
    """The open neck: how far in the shirt's front is at x, a V from the second button (1.44 m) to the collar."""
    w = max(0.0, (z - 1.44) / 0.1) * 0.05
    return (lambda x: 0.022 * max(0.0, 1 - abs(x) / w)) if w > 0 else None


def foot_rings(x0):
    """The bare foot, heel to the ball (the toes are their own): rings of (y, half-width, height over the footbed)."""
    out = []
    for y, hw, h in ((-0.068, 0.026, 0.055), (-0.04, 0.032, 0.068), (0.0, 0.036, 0.07), (0.05, 0.041, 0.058),
                     (0.1, 0.045, 0.04), (0.14, 0.044, 0.028), (0.165, 0.038, 0.021)):
        r = []
        for j in range(20):
            a = 2 * math.pi * j / 20
            sa, ca = math.sin(a), math.cos(a)
            sa, ca = math.copysign(abs(sa) ** 0.8, sa), math.copysign(abs(ca) ** 0.8, ca)
            # (Flat on the bed: the sole's half of the ring pressed down onto it.)
            z = BED + h / 2 * (1 + ca) if ca >= 0 else BED + h / 2 * (1 - abs(ca) ** 0.5)
            r.append((x0 + sa * hw, y, z))
        out.append(r)
    return out


def foot_top(y):
    """The foot's height over the footbed and its half-width at y (where the straps go over it)."""
    ys, hws, hs = (-0.068, -0.04, 0.0, 0.05, 0.1, 0.14, 0.165), (0.026, 0.032, 0.036, 0.041, 0.045, 0.044, 0.038), \
        (0.055, 0.068, 0.07, 0.058, 0.04, 0.028, 0.021)
    return float(np.interp(y, ys, hs)), float(np.interp(y, ys, hws))


def blocking():
    """[(material key, fn(bm))]: Dave's solids, in crew.py's material keys (the shirt wears the coat's, the bare arms
    the gloves', the feet the neck's skin; tools/models/recipes/dave.py dresses them)."""
    out = []
    out.append(("COAT", lambda bm: loft(bm, [ring(z, rx, f, b, sq, notch=throat(z)) for z, rx, f, b, sq in TORSO])))
    # The skin in the open neck, and the placket down the shirt's front (proud of it, the buttons on it baked).
    out.append(("NECK", lambda bm: ellipsoid(bm, (0, 0.058, 1.49), (0.05, 0.034, 0.055))))
    placket = [(0, front_at(z) + 0.001, z) for z in (0.97, 1.05, 1.13, 1.21, 1.29, 1.37, 1.43)]
    out.append(("COAT", lambda bm: sweep(bm, placket, [(0.017, 0.0028)] * len(placket), n=10, ref=(1, 0, 0), sq=0.5)))
    for sx in (-1, 1):
        # Shirt sleeves, full in the linen, rolled to a fat cuff just past the elbow.
        xs = [0.1, 0.17, 0.26, 0.36, 0.44, 0.49]
        rs = [(0.08, 0.088), (0.077, 0.084), (0.071, 0.077), (0.065, 0.07), (0.06, 0.065), (0.058, 0.062)]
        out.append(("SLEEVE", lambda bm, sx=sx: sweep(bm, [(sx * x, 0.0, 1.448) for x in xs], rs, n=22, ref=(0, 0, 1))))
        out.append(("SLEEVE", lambda bm, sx=sx: sweep(bm, [(sx * 0.475, 0, 1.448), (sx * 0.5, 0, 1.448), (sx * 0.528, 0, 1.448)],
                                                      [(0.061, 0.066), (0.065, 0.07), (0.059, 0.064)], n=22, ref=(0, 0, 1))))
        # The bare forearm to the wrist (flatter than it's wide, palm down in the T-pose), the hand: its palm, four
        # fingers and the thumb (the gloves' places, a size down: the rig's hand regions are the gloves').
        fore = [(sx * x, 0, 1.447) for x in (0.5, 0.58, 0.65, 0.71, 0.745, 0.77)]
        out.append(("GLOVE", lambda bm, fore=fore: sweep(bm, fore, [(0.043, 0.046), (0.039, 0.043), (0.031, 0.037), (0.023, 0.031),
                                                                  (0.02, 0.03), (0.017, 0.03)], n=18, ref=(0, 0, 1))))
        out.append(("GLOVE", lambda bm, sx=sx: ellipsoid(bm, (sx * 0.785, 0.0, 1.444), (0.04, 0.043, 0.0205))))
        for y, length in ((0.029, 0.09), (0.01, 0.097), (-0.009, 0.093), (-0.027, 0.078)):
            out.append(("GLOVE", lambda bm, sx=sx, y=y, length=length: sweep(
                bm, [(sx * 0.8, y, 1.443), (sx * (0.8 + length * 0.5), y, 1.443), (sx * (0.8 + length), y, 1.441)],
                [(0.0098, 0.0092), (0.0092, 0.0086), (0.0078, 0.0074)], n=12, ref=(0, 0, 1))))
        out.append(("GLOVE", lambda bm, sx=sx: sweep(bm, [(sx * 0.752, 0.028, 1.438), (sx * 0.774, 0.055, 1.434), (sx * 0.795, 0.074, 1.43)],
                                                     [(0.0115, 0.012), (0.0105, 0.0105), (0.009, 0.0092)], n=12, ref=(0, 0, 1))))
        # Trousers, hip to a turn-up above the ankle, the knee forward; loose (no coat over them).
        leg = [(sx * 0.094, 0.0, 1.0), (sx * 0.1, 0.004, 0.85), (sx * 0.104, 0.01, 0.7), (sx * 0.105, 0.016, 0.53),
               (sx * 0.105, 0.006, 0.4), (sx * 0.105, -0.004, 0.28), (sx * 0.105, -0.008, 0.17), (sx * 0.105, -0.008, 0.135)]
        out.append(("TROUSER", lambda bm, leg=leg: sweep(bm, leg, [(0.1, 0.108), (0.092, 0.099), (0.083, 0.089), (0.071, 0.075),
                                                                  (0.067, 0.071), (0.063, 0.066), (0.061, 0.063), (0.06, 0.062)],
                                                         n=22, ref=(1, 0, 0))))
        x = sx * FOOT_X
        out.append(("TROUSER", lambda bm, x=x: sweep(bm, [(x, -0.008, 0.172), (x, -0.008, 0.152), (x, -0.008, 0.13)],
                                                     [(0.064, 0.066), (0.067, 0.069), (0.065, 0.067)], n=22, ref=(1, 0, 0))))
        # The bare ankle and foot on the footbed, the toes (the big one inside), the footbed and its tread.
        out.append(("NECK", lambda bm, x=x: sweep(bm, [(x, -0.012, 0.16), (x, -0.014, 0.11), (x, -0.012, 0.075)],
                                                  [(0.034, 0.037), (0.031, 0.035), (0.034, 0.042)], n=18, ref=(1, 0, 0))))
        out.append(("NECK", lambda bm, x=x: loft(bm, foot_rings(x))))
        for d, ty, (rx, ry, rz) in ((-0.025, 0.197, (0.0135, 0.021, 0.012)), (-0.0055, 0.19, (0.0085, 0.016, 0.009)),
                                    (0.0095, 0.183, (0.008, 0.015, 0.0085)), (0.0225, 0.173, (0.0075, 0.013, 0.008)),
                                    (0.034, 0.161, (0.007, 0.011, 0.0075))):
            out.append(("NECK", lambda bm, c=(x + sx * d, ty, BED + rz * 0.9), r=(rx, ry, rz): ellipsoid(bm, c, r, seg=12, rings=8)))
        outline = [(x, HEEL)] + [(x + hw, y) for y, hw in SOLE] + [(x, TOE)] + [(x - hw, y) for y, hw in reversed(SOLE)]
        out.append(("BOOT", lambda bm, o=outline: loft(bm, [[(px, py, z) for px, py in o] for z in (0.009, BED)])))
        out.append(("SATCHEL", lambda bm, o=outline: loft(bm, [[(px, py, z) for px, py in o] for z in (0.0, 0.009)])))
        # The two straps over the foot, standing on it, each buckled on the outside.
        for sy in STRAPS:
            h, hw = foot_top(sy)
            path = []
            for k in range(9):
                t = -math.pi / 2 + math.pi * k / 8
                s, c = math.sin(t), math.cos(t)
                path.append((x + s * (hw + 0.006), sy, BED + 0.002 + (h + 0.005) * (abs(c) ** 0.8 if k not in (0, 8) else 0.0)))
            out.append(("STRAP", lambda bm, p=path: sweep(bm, p, [(0.015, 0.0036)] * len(p), n=10, ref=(0, 1, 0), sq=0.45)))
            out.append(("BADGE", lambda bm, c=(x + sx * (hw + 0.012), sy, BED + h * 0.55): box(bm, c, (0.0035, 0.012, 0.01), round_=0.0015)))
    # The hips under it all, and the belt under the belly, its buckle.
    out.append(("TROUSER", lambda bm: ellipsoid(bm, (0, -0.005, 0.985), (0.175, 0.13, 0.09))))
    out.append(("BELT", lambda bm: loft(bm, [ring(z, 0.191, 0.17, 0.145, 0.84) for z in (0.94, 0.98)])))
    out.append(("BADGE", lambda bm: box(bm, (0, 0.174, 0.96), (0.024, 0.005, 0.019), round_=0.002)))
    return out


def shells(g):
    """The shirt's collar: a band round the neck open at the throat, its points turned down onto the chest."""
    mat = g["_mat"]("COAT")
    band = []
    for z, r, dy in ((1.49, 0.074, 0.002), (1.52, 0.07, 0.004), (1.548, 0.066, 0.006)):
        band.append([(math.sin(a) * r, dy + math.cos(a) * r * 0.95, z)
                     for a in (math.radians(24) + math.radians(312) * j / 25 for j in range(26))])
    collar = crewbody._shell_part("dave_collar", band, 0.004, None, mat, open_=True)
    points = []
    for sx in (-1, 1):
        # Rows across each point, from the band's front end out and down over the collarbone.
        rows = []
        for t in (0.0, 0.35, 0.7, 1.0):
            z = 1.545 - t * 0.085
            y = front_at(z) + 0.004 + (0.02 if t < 0.3 else 0.0) * (1 - t / 0.3)
            inner = 0.03 + t * 0.03
            outer = 0.075 + t * 0.03 - t * t * 0.035
            rows.append([(sx * inner, y + 0.004, z), (sx * outer, y - 0.012 * (1 - t), z + 0.004)])
        points.append(crewbody._shell_part("dave_point", rows, 0.003, None, mat, open_=True))
    bpy.ops.object.select_all(action="DESELECT")
    for o in [collar] + points:
        o.select_set(True)
    bpy.context.view_layer.objects.active = collar
    bpy.ops.object.join()
    collar.name = collar.data.name = "coat"
    for f in collar.data.polygons:
        f.use_smooth = True
    return collar


# ----------------------------------------------------------------------------------------------------------------
# The waistcoats

# P1's DaveKit.VestOf, in order: the cloth's colour and its back's (satin, darker).
VESTS = [("mustard", "#c8902a", "#5a3a14"), ("plum", "#6a1e4a", "#3a1028"), ("tartan", "#2a5a36", "#1e2a3a"),
         ("teal", "#1a6a70", "#123a40"), ("patch", "#c86a30", "#3a3028")]
TOP_BUTTON = 1.2      # where the V closes
COLLAR = 1.53         # the shoulders' top, by the neck


def _smooth01(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def covered(p):
    """Where a waistcoat is on the body (p: Nx3): from its hem (the points either side of the front's last button, up
    to the side seams, the back straight across) to the V, the shoulders and the back's neck, out of the armholes."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    front = _smooth01(-0.05, 0.05, y)
    hem_front = np.where(ax < 0.03, 0.915 + (0.03 - ax) * 1.5, 0.915 + 0.055 * np.clip((ax - 0.03) / 0.16, 0, 1) ** 0.8)
    hem = hem_front * front + 0.972 * (1 - front)
    v = 0.09 * np.clip((z - TOP_BUTTON) / (COLLAR - TOP_BUTTON), 0, 1)
    neck = np.where(y > 0, ax < v, (ax < 0.085) & (z > 1.5))
    arm = ax > 0.21 - 0.055 * _smooth01(1.24, 1.34, z)
    return (z > hem) & (z < COLLAR) & ~neck & ~arm


def vest(k, high, g):
    """Waistcoat k: the dense body's faces where it's covered, relaxed over the shirt's placket and the belt, let out
    over the chest and hanging straight off the belly, thickened, and collapsed to the game's budget; skinned as the body
    under it is."""
    name, cloth, satin = VESTS[k]
    rig = g["rig"]
    front_mat = rig.make_material(Mat(f"dave_vest.{k}", hexc(cloth)))
    back_mat = rig.make_material(Mat(f"dave_back.{k}", hexc(satin), shine=0.3))
    bm = bmesh.new()
    bm.from_mesh(high.data)
    centres = np.array([f.calc_center_median() for f in bm.faces], np.float32)
    keep = covered(centres)
    bmesh.ops.delete(bm, geom=[f for f, kf in zip(bm.faces, keep) if not kf], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    # Only the one piece (a stray island under an arm or at the belt goes).
    bm.verts.ensure_lookup_table()
    seen, best = set(), []
    for v0 in bm.verts:
        if v0.index in seen:
            continue
        island, stack = [], [v0]
        seen.add(v0.index)
        while stack:
            v = stack.pop()
            island.append(v)
            for e in v.link_edges:
                o = e.other_vert(v)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        if len(island) > len(best):
            best = island
    keepv = {v.index for v in best}
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keepv], context="VERTS")
    # Drape: relaxed over what's under it (the placket's edge, the buttons, the belt), its edges evened out.
    for _ in range(12):
        bmesh.ops.smooth_vert(bm, verts=bm.verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.normal_update()
    co = np.array([v.co for v in bm.verts], np.float32)
    no = np.array([v.normal for v in bm.verts], np.float32)
    # The cloth's out from the shirt by its own thickness and a little air.
    co += no * 0.006
    # Hanging off the belly: below its fullest (1.1 m) the front comes in no faster than a quarter of the drop.
    fr = co[:, 1] > 0.02
    bins = np.round(co[:, 0] / 0.01).astype(int)
    belly = {}
    for b, yy, zz, f in zip(bins, co[:, 1], co[:, 2], fr):
        if f and 1.07 < zz < 1.14:
            belly[b] = max(belly.get(b, -1.0), yy)
    for i in np.nonzero(fr & (co[:, 2] < 1.1))[0]:
        top = belly.get(bins[i])
        if top is not None:
            co[i, 1] = max(co[i, 1], top - (1.1 - co[i, 2]) * 0.25)
    for v, c in zip(bm.verts, co):
        v.co = Vector(c)
    me = bpy.data.meshes.new(f"vest_{k}")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(f"vest_{k}", me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(front_mat)
    me.materials.append(back_mat)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    # To the game's budget, then thickened (the cloth's edge, its lining): the bake reads the folds back in.
    dec = o.modifiers.new("collapse", "DECIMATE")
    dec.decimate_type, dec.ratio, dec.use_symmetry, dec.symmetry_axis = "COLLAPSE", 1.0, True, "X"
    dec.ratio = min(1.0, 300 / max(1, len(me.polygons)))
    bpy.ops.object.modifier_apply(modifier=dec.name)
    sol = o.modifiers.new("solid", "SOLIDIFY")
    sol.thickness, sol.offset, sol.use_rim = 0.003, 1.0, True
    bpy.ops.object.modifier_apply(modifier=sol.name)
    # The back panel's satin: behind the side seams, below the shoulders.
    for f in me.polygons:
        c = f.center
        f.material_index = 1 if c.y < -0.03 and c.z < 1.47 else 0
        f.use_smooth = True
    crewbody.skin(o, crewbody.frame_weights(g), g["sk"].rig)
    o["variants"] = str(k)
    return o


def build(g):
    """The frame, its dense high copy and the collar (crewbody's way, Dave's solids), and his five waistcoats."""
    frame, high, coat = crewbody.build(g, blocks=blocking, shells=shells)
    vests = [vest(k, high, g) for k in range(len(VESTS))]
    print(f"[dt] dave vests: {[len(v.data.polygons) for v in vests]} faces")
    return frame, high, coat
