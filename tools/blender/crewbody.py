"""The crew's body, modelled rather than lofted (the director: "Player model is a bit low quality... I think you can do
better with a complete overhaul"). The kit's lofts made a twenty-two-sided coat, twelve-sided sleeves and gloves of
three boxes; this builds the figure the way a character artist would, in four steps:

  1. Blocking: the clothed figure in overlapping solids, in the T-pose of SK_Human: the coat's body to the collar,
     sleeves with their turned-back cuffs, gloves with a gauntlet, a palm, four fingers and a thumb, trousers, boots
     with a shaft, a foot, a toe cap and a thick sole, the belt and its buckle and pouches, the bandolier and the
     satchel.
  2. Union: one watertight surface from them all, voxel-remeshed (the joins flow into one another as cloth over a body
     does), then relaxed. This dense mesh is the high copy the bake reads (tools/models/crewfigure sculpts its folds).
  3. Retopology: QuadriFlow down to the game budget, smooth-shaded: the frame.
  4. Weights: by position, the rig's own region functions (the torso up the spine, the arms along them, the legs
     down them, the gloves' fingers and thumb), so the joins between the solids deform as one.

The coat's skirt and collar are thin shells a retopology can't keep, so they're built directly as clean two-sided
lofts (the coat part): the skirt open at the front below the belt, ragged at the hem; the collar stood up round the
mask's hood. Each solid carries its material (the atlas's cells: coat, sleeve, trouser, boot, gloves, belt, strap,
satchel, iron), given to the frame's faces by whichever solid they came from.

    import crewbody; crewbody.build(globals())   # from tools/blender/crew.py, after kit.build()
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

VOXEL = 0.0035          # the union's voxel (m): the strap's 8 mm and the fingers' grooves survive it
FRAME_FACES = 2700      # QuadriFlow's target (quads): the frame's share of the crew's (and the husk's) 9k


# ----------------------------------------------------------------------------------------------------------------
# Solids, in bmesh

def _frame(t, ref):
    """Two unit axes square to the tangent `t`, the first as near `ref` as it can be."""
    u = Vector(ref) - t * t.dot(Vector(ref))
    if u.length < 1e-6:
        u = t.orthogonal()
    u.normalize()
    return u, t.cross(u)


def sweep(bm, pts, radii, n=24, ref=(0, 0, 1), sq=1.0, caps=True):
    """A solid swept along `pts` (a list of points), its section an ellipse (or a squarer superellipse, `sq` < 1) of
    radii (along ref's side, across) at each point, closed at both ends."""
    pts = [Vector(p) for p in pts]
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        u, v = _frame(t, ref)
        ru, rv = radii[i] if isinstance(radii[i], (tuple, list)) else (radii[i], radii[i])
        ring = []
        for j in range(n):
            a = 2 * math.pi * j / n
            ca, sa = math.cos(a), math.sin(a)
            ca, sa = math.copysign(abs(ca) ** sq, ca), math.copysign(abs(sa) ** sq, sa)
            ring.append(bm.verts.new(p + u * (ca * ru) + v * (sa * rv)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    if caps:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])


def ellipsoid(bm, centre, radii, rot=None, seg=24, rings=14):
    m = Matrix.Translation(Vector(centre)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((*radii, 1.0))
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0, matrix=m)


def box(bm, centre, half, rot=None, round_=0.0):
    """A box (its corners rounded by `round_` m in the union: it's grown by it and the remesh smooths the rest)."""
    h = Vector(half) + Vector((round_,) * 3)
    m = Matrix.Translation(Vector(centre)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((*(h * 2), 1.0))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def loft(bm, rings, caps=True):
    """A solid through closed rings of points (each the same count), capped."""
    vs = [[bm.verts.new(Vector(p)) for p in r] for r in rings]
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    if caps:
        bm.faces.new(list(reversed(vs[0])))
        bm.faces.new(vs[-1])


def superellipse(z, rx, ry, dy=0.0, sq=0.8, n=40, x0=0.0):
    out = []
    for j in range(n):
        a = 2 * math.pi * j / n
        sa, ca = math.sin(a), math.cos(a)
        sa, ca = math.copysign(abs(sa) ** sq, sa), math.copysign(abs(ca) ** sq, ca)
        out.append((x0 + sa * rx, dy + ca * ry, z))
    return out


# ----------------------------------------------------------------------------------------------------------------
# The figure's blocking: (material, builder) pairs

# The coat's body, belt to collar: (z, rx, ry, y offset, squareness). The chest full, the waist in under the belt,
# the shoulders broad and sloped into the sleeves.
TORSO_RINGS = [(0.93, 0.19, 0.146, -0.005, 0.85), (0.98, 0.184, 0.14, -0.005, 0.85), (1.04, 0.185, 0.141, -0.003, 0.82),
               (1.1, 0.19, 0.143, 0.0, 0.8), (1.16, 0.197, 0.148, 0.005, 0.78), (1.22, 0.205, 0.151, 0.009, 0.75),
               (1.28, 0.211, 0.15, 0.009, 0.72), (1.33, 0.214, 0.146, 0.006, 0.7), (1.38, 0.214, 0.139, 0.0, 0.67),
               (1.415, 0.207, 0.129, -0.004, 0.65), (1.45, 0.186, 0.117, -0.008, 0.68), (1.48, 0.152, 0.104, -0.01, 0.75),
               (1.51, 0.11, 0.092, -0.002, 0.95), (1.545, 0.098, 0.088, 0.002, 1.0)]

STRAP_PATH = [(0.21, 0.11, 0.96), (0.13, 0.16, 1.13), (0.03, 0.172, 1.27), (-0.08, 0.154, 1.39), (-0.135, 0.087, 1.475),
              (-0.147, -0.02, 1.49), (-0.122, -0.132, 1.41), (-0.02, -0.16, 1.27), (0.1, -0.155, 1.12), (0.2, -0.112, 0.97)]


def blocking():
    """[(material key, fn(bm))]: the solids the union is made of. Keys are crew.py's material globals."""
    out = []
    out.append(("COAT", lambda bm: loft(bm, [superellipse(z, rx, ry, dy, sq) for z, rx, ry, dy, sq in TORSO_RINGS])))
    # The lapped front: a lapel standing proud from the collar down across the chest to the belt, a double row of the
    # coat's front, and the yoke across the shoulders behind.
    lapel = [(0.02, 0.156, 1.0), (0.03, 0.162, 1.12), (0.05, 0.166, 1.24), (0.075, 0.158, 1.34), (0.095, 0.134, 1.43),
             (0.1, 0.11, 1.49)]
    out.append(("COAT", lambda bm: sweep(bm, lapel, [(0.009, 0.034)] * len(lapel), n=10, ref=(0, 1, 0), sq=0.6)))
    out.append(("COAT", lambda bm: sweep(bm, [(-0.17, -0.1, 1.405), (0, -0.135, 1.415), (0.17, -0.1, 1.405)],
                                         [(0.008, 0.05)] * 3, n=10, ref=(0, -1, 0.2), sq=0.6)))
    for sx in (-1, 1):
        # Sleeves: the shoulder's seam into the coat, the elbow, the forearm, then the cuff turned back over the glove.
        xs = [0.1, 0.17, 0.26, 0.36, 0.45, 0.55, 0.64, 0.7]
        rs = [(0.082, 0.092), (0.08, 0.088), (0.074, 0.08), (0.069, 0.074), (0.064, 0.069), (0.06, 0.064), (0.056, 0.06),
              (0.054, 0.058)]
        out.append(("SLEEVE", lambda bm, sx=sx: sweep(bm, [(sx * x, 0.0, 1.448) for x in xs], rs, n=22, ref=(0, 0, 1))))
        out.append(("SLEEVE", lambda bm, sx=sx: sweep(bm, [(sx * 0.672, 0, 1.448), (sx * 0.692, 0, 1.448), (sx * 0.728, 0, 1.448)],
                                                      [(0.064, 0.068), (0.068, 0.072), (0.066, 0.07)], n=22, ref=(0, 0, 1))))
        # Gloves: the gauntlet out of the cuff, the palm (flat, facing down in the T-pose), four fingers side by side
        # (their grooves stay in the union), the thumb forward and down.
        out.append(("GLOVE", lambda bm, sx=sx: sweep(bm, [(sx * 0.69, 0, 1.447), (sx * 0.73, 0, 1.446), (sx * 0.755, 0, 1.445)],
                                                     [(0.047, 0.054), (0.04, 0.05), (0.03, 0.046)], n=18, ref=(0, 0, 1))))
        out.append(("GLOVE", lambda bm, sx=sx: ellipsoid(bm, (sx * 0.782, 0.0, 1.444), (0.044, 0.047, 0.022))))
        for y, length in ((0.031, 0.093), (0.011, 0.1), (-0.009, 0.096), (-0.029, 0.082)):
            out.append(("GLOVE", lambda bm, sx=sx, y=y, length=length: sweep(
                bm, [(sx * 0.8, y, 1.443), (sx * (0.8 + length * 0.5), y, 1.443), (sx * (0.8 + length), y, 1.441)],
                [(0.0115, 0.0105), (0.011, 0.01), (0.0095, 0.009)], n=12, ref=(0, 0, 1))))
        out.append(("GLOVE", lambda bm, sx=sx: sweep(bm, [(sx * 0.752, 0.03, 1.438), (sx * 0.775, 0.058, 1.434), (sx * 0.797, 0.078, 1.43)],
                                                     [(0.013, 0.014), (0.012, 0.012), (0.01, 0.0105)], n=12, ref=(0, 0, 1))))
        # Trousers, hip to boot, the knee forward.
        leg = [(sx * 0.093, 0.0, 1.0), (sx * 0.1, 0.004, 0.85), (sx * 0.104, 0.01, 0.7), (sx * 0.105, 0.016, 0.53),
               (sx * 0.105, 0.004, 0.4), (sx * 0.105, -0.006, 0.31), (sx * 0.105, -0.01, 0.26)]
        out.append(("TROUSER", lambda bm, leg=leg: sweep(bm, leg, [(0.098, 0.106), (0.09, 0.096), (0.08, 0.086), (0.066, 0.07),
                                                                  (0.064, 0.068), (0.057, 0.059), (0.055, 0.057)], n=22, ref=(1, 0, 0))))
        # Boots: the shaft with its folded top, the foot heel to toe, a toe cap, a thick sole and a heel block.
        x = sx * 0.105
        out.append(("BOOT", lambda bm, x=x: sweep(bm, [(x, -0.012, 0.3), (x, -0.012, 0.28), (x, -0.012, 0.18), (x, -0.012, 0.1)],
                                                  [(0.07, 0.071), (0.067, 0.068), (0.066, 0.067), (0.064, 0.066)], n=22, ref=(1, 0, 0))))
        foot = []
        for y, hw, top in ((-0.075, 0.056, 0.105), (-0.03, 0.062, 0.116), (0.05, 0.062, 0.092), (0.13, 0.058, 0.072),
                           (0.19, 0.053, 0.058), (0.222, 0.04, 0.046), (0.232, 0.022, 0.036)):
            ring = []
            for j in range(20):
                a = 2 * math.pi * j / 20
                sa, ca = math.sin(a), math.cos(a)
                sa, ca = math.copysign(abs(sa) ** 0.55, sa), math.copysign(abs(ca) ** 0.55, ca)
                ring.append((x + sa * hw, y, top / 2 + 0.014 + ca * (top / 2 - 0.004)))
            foot.append(ring)
        out.append(("BOOT", lambda bm, foot=foot: loft(bm, foot)))
        # The sole: the foot's own outline, a finger's width proud of the upper all round, heel to toe.
        outline = [(-0.085, 0.052), (-0.06, 0.062), (0.0, 0.066), (0.08, 0.068), (0.15, 0.064), (0.2, 0.056), (0.228, 0.042),
                   (0.242, 0.022), (0.246, 0.0)]
        ring = [(x + hw, y) for y, hw in outline] + [(x - hw, y) for y, hw in reversed(outline[:-1])]
        out.append(("BOOT", lambda bm, ring=ring: loft(bm, [[(px, py, z) for px, py in ring] for z in (0.0, 0.022)])))
    # The hips under it all (the crotch filled between the legs, inside the coat).
    out.append(("TROUSER", lambda bm: ellipsoid(bm, (0, -0.005, 0.985), (0.17, 0.125, 0.09))))
    # The belt: a band proud of the coat, its iron buckle, two pouches forward and one at the back.
    out.append(("BELT", lambda bm: loft(bm, [superellipse(z, 0.193, 0.149, -0.004, 0.84) for z in (0.965, 1.035)])))
    out.append(("IRON", lambda bm: box(bm, (0, 0.152, 1.0), (0.03, 0.007, 0.028), round_=0.002)))
    for sx in (-1, 1):
        out.append(("BELT", lambda bm, sx=sx: box(bm, (sx * 0.125, 0.13, 0.985), (0.034, 0.022, 0.042),
                                                   rot=Matrix.Rotation(math.radians(sx * 32), 4, "Z"), round_=0.004)))
    out.append(("BELT", lambda bm: box(bm, (0.0, -0.152, 0.99), (0.05, 0.02, 0.038), round_=0.004)))
    # The bandolier, left shoulder to right hip, and the satchel on it.
    refs = [Vector((p[0] * 0.6, p[1], 0.0 if p[2] < 1.44 else 1.0)).normalized() for p in STRAP_PATH]
    out.append(("STRAP", lambda bm: _strap(bm, refs)))
    out.append(("SATCHEL", lambda bm: box(bm, (0.228, 0.02, 0.86), (0.04, 0.122, 0.102),
                                          rot=Matrix.Rotation(math.radians(-6), 4, "Y"), round_=0.008)))
    out.append(("SATCHEL", lambda bm: box(bm, (0.272, 0.02, 0.905), (0.008, 0.124, 0.06),
                                          rot=Matrix.Rotation(math.radians(-6), 4, "Y"), round_=0.003)))
    return out


def _strap(bm, refs):
    """The bandolier: a flat band 8 mm thick along its path, faces out from the body."""
    pts = [Vector(p) for p in STRAP_PATH]
    n = 8
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        out = (refs[i] - t * t.dot(refs[i])).normalized()
        side = t.cross(out)
        ring = []
        for j in range(n):
            a = 2 * math.pi * j / n
            ca, sa = math.cos(a), math.sin(a)
            ca, sa = math.copysign(abs(ca) ** 0.4, ca), math.copysign(abs(sa) ** 0.4, sa)
            ring.append(bm.verts.new(p + side * (ca * 0.025) + out * (sa * 0.004)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])


# ----------------------------------------------------------------------------------------------------------------
# The coat's shells: skirt and collar, two-sided lofts (their own part)

def _shell_part(name, rings, thickness, bones_fn, mat, open_=False):
    """A thin shell from rings (all the same count, `open_` for an arc rather than a loop), solidified inward."""
    bm = bmesh.new()
    vs = [[bm.verts.new(Vector(p)) for p in r] for r in rings]
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for j in range(n - 1 if open_ else n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    # Faces out, away from the body's axis (whichever way round the rings went), before it's thickened inward.
    bm.normal_update()
    out = sum(f.normal.dot(Vector((f.calc_center_median().x, f.calc_center_median().y, 0))) for f in bm.faces)
    if out < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(mat)
    # Outward faces: the rings go round anticlockwise seen from above, bottom to top.
    bpy.context.view_layer.objects.active = o
    mod = o.modifiers.new("solid", "SOLIDIFY")
    mod.thickness, mod.offset, mod.use_rim = thickness, -1.0, True
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


def coat_shells(g):
    """The skirt, belt to below the knee, open at the front (a gap that widens to the hem), the front hanging a little
    lower and the hem ragged; the collar stood up round the hood, open at the throat; pocket flaps on the hips."""
    rig = g["rig"]
    skirt_rings = []
    for z, rx, ry, dy, sq in [(1.0, 0.186, 0.142, -0.005, 0.85), (0.95, 0.19, 0.146, -0.005, 0.85), (0.89, 0.198, 0.152, -0.005, 0.85),
                              (0.83, 0.205, 0.158, -0.003, 0.88), (0.76, 0.212, 0.165, 0.0, 0.9), (0.69, 0.22, 0.171, 0.0, 0.9),
                              (0.62, 0.228, 0.178, 0.0, 0.9), (0.56, 0.234, 0.184, 0.0, 0.9), (0.5, 0.24, 0.19, 0.0, 0.9)]:
        # (The front's overlap parting a little towards the hem as the coat hangs.)
        gap = math.radians(1.5 + 5 * max(0.0, (0.98 - z) / 0.48))
        ring = []
        n = 32
        for j in range(n):
            a = gap + (2 * math.pi - 2 * gap) * j / (n - 1)
            sa, ca = math.sin(a), math.cos(a)
            sa, ca = math.copysign(abs(sa) ** sq, sa), math.copysign(abs(ca) ** sq, ca)
            drop = 0.02 * max(0.0, ca) if z < 0.55 else 0.0
            rag = 0.012 * rig.noise3(Vector((sa * 4, ca * 4, z)), 3, 3.0) if z < 0.55 else 0.0
            ring.append((sa * rx, dy + ca * ry + 0.006, z - drop + rag))
        skirt_rings.append(ring)
    skirt = _shell_part("coat_skirt", skirt_rings, 0.009, None, g["_mat"]("COAT"), open_=True)
    collar_rings = []
    for z, r, dy in ((1.49, 0.118, 0.0), (1.535, 0.126, 0.004), (1.585, 0.134, 0.008), (1.62, 0.14, 0.01)):
        ring = []
        n = 26
        for j in range(n):
            # Open at the throat past the cheek filters: a greatcoat's collar turned up behind the head.
            a = math.radians(62) + math.radians(236) * j / (n - 1)
            ring.append((math.sin(a) * r, dy + math.cos(a) * r * 0.92, z))
        collar_rings.append(ring)
    collar = _shell_part("coat_collar", collar_rings, 0.01, None, g["_mat"]("COAT"), open_=True)
    bm = bmesh.new()
    for sx in (-1, 1):
        box(bm, (sx * 0.135, 0.168, 0.8), (0.072, 0.007, 0.032), rot=Matrix.Rotation(math.radians(-sx * 28), 4, "Z"))
    me = bpy.data.meshes.new("coat_flaps")
    bm.to_mesh(me)
    bm.free()
    flaps = bpy.data.objects.new("coat_flaps", me)
    bpy.context.scene.collection.objects.link(flaps)
    me.materials.append(g["_mat"]("COAT"))
    bpy.ops.object.select_all(action="DESELECT")
    for o in (skirt, collar, flaps):
        o.select_set(True)
    bpy.context.view_layer.objects.active = skirt
    bpy.ops.object.join()
    skirt.name = skirt.data.name = "coat"
    for f in skirt.data.polygons:
        f.use_smooth = True
    return skirt


# ----------------------------------------------------------------------------------------------------------------
# Weights: the rig's region functions, by position

def frame_weights(g):
    TORSO, ARM, LEGW, coat_weights, sided = g["TORSO"], g["ARM"], g["LEGW"], g["coat_weights"], g["sided"]

    def w(p):
        ax = abs(p.x)
        s = "r" if p.x >= 0 else "l"
        if ax > 0.745 and p.z > 1.35:
            # The glove: fingers past the knuckles, the thumb forward of the palm, the palm on the hand.
            if ax > 0.81:
                return {f"fingers_{s}": 1.0} if ax > 0.825 else {f"fingers_{s}": 0.5, f"hand_{s}": 0.5}
            if p.y > 0.028 and ax > 0.755:
                return {f"thumb_{s}": 1.0}
            return {f"hand_{s}": 1.0}
        if ax > 0.13 and p.z > 1.33:
            out = ARM(p)
            return {k.format(s=s): v for k, v in out.items()}
        if p.x > 0.2 and 0.74 < p.z < 1.0:
            # The satchel, out on the right hip past the thigh: the pelvis, a little of the thigh.
            return {"pelvis": 0.7, "thigh_r": 0.3}
        if p.z < 1.02 and ax > 0.03:
            # The legs, from under the belt down.
            return LEGW(p) if p.z < 0.93 else _mix(LEGW(p), TORSO(p), (p.z - 0.93) / 0.09)
        return coat_weights(p)
    return w


def _mix(a, b, t):
    t = max(0.0, min(1.0, t))
    out = {k: v * (1 - t) for k, v in a.items()}
    for k, v in b.items():
        out[k] = out.get(k, 0) + v * t
    tot = sum(out.values())
    return {k: v / tot for k, v in out.items() if v / tot > 0.02}


def skin(o, weights, rig):
    """Vertex groups by `weights(p)`, parented to the armature with its modifier: the kit's parts' way."""
    groups = {}
    for v in o.data.vertices:
        for bname, wt in weights(v.co).items():
            gr = groups.get(bname)
            if gr is None:
                gr = groups[bname] = o.vertex_groups.new(name=bname)
            gr.add([v.index], wt, "REPLACE")
    o.parent = rig
    mod = o.modifiers.new("Armature", "ARMATURE")
    mod.object = rig


# ----------------------------------------------------------------------------------------------------------------

def build(g):
    """The frame (the game mesh), its dense high copy (`g["FRAME_HIGH"]`, not a part), and the coat's shells, from
    crew.py's globals (its materials and weight functions). Runs after kit.build(), so the kit's materials exist."""
    sk = g["sk"]
    arm = sk.rig
    rig = g["rig"]

    def mat(key):
        m = g[key]
        return bpy.data.materials.get(m.name) or rig.make_material(m)
    g["_mat"] = mat

    # 1. Blocking, each solid its own mesh for the union and for telling the faces' materials after.
    solids = []
    bm_all = bmesh.new()
    for key, fn in blocking():
        bm = bmesh.new()
        fn(bm)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        solids.append((key, BVHTree.FromBMesh(bm)))
        tmp = bpy.data.meshes.new("solid")
        bm.to_mesh(tmp)
        bm.free()
        bm_all.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)

    def key_at(p):
        best, best_d = None, 1e9
        for key, tree in solids:
            hit = tree.find_nearest(p)
            if hit[0] is not None and hit[3] < best_d:
                best, best_d = key, hit[3]
        return best

    # 2. The union, voxel-remeshed and relaxed: the high copy.
    me = bpy.data.meshes.new("frame_high")
    bm_all.to_mesh(me)
    bm_all.free()
    high = bpy.data.objects.new("frame_high", me)
    bpy.context.scene.collection.objects.link(high)
    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    bpy.context.view_layer.objects.active = high
    me.remesh_voxel_size = VOXEL
    me.remesh_voxel_adaptivity = 0.0
    bpy.ops.object.voxel_remesh()
    mod = high.modifiers.new("relax", "SMOOTH")
    mod.factor, mod.iterations = 0.5, 4
    bpy.ops.object.modifier_apply(modifier=mod.name)

    # 3. The frame: QuadriFlow over a copy of it, smooth-shaded.
    frame = high.copy()
    frame.data = high.data.copy()
    frame.name = frame.data.name = "frame"
    bpy.context.scene.collection.objects.link(frame)
    bpy.ops.object.select_all(action="DESELECT")
    frame.select_set(True)
    bpy.context.view_layer.objects.active = frame
    bpy.ops.object.quadriflow_remesh(target_faces=FRAME_FACES, use_mesh_symmetry=False, use_preserve_sharp=False,
                                     use_preserve_boundary=False, smooth_normals=False, seed=0)

    # Each face of both wears the material of the solid it's nearest.
    keys = sorted({k for k, _ in solids})
    for o in (high, frame):
        o.data.materials.clear()
        for k in keys:
            o.data.materials.append(mat(k))
        slot = {k: i for i, k in enumerate(keys)}
        for f in o.data.polygons:
            f.material_index = slot[key_at(f.center)]
            f.use_smooth = True

    # 4. Weights, and the coat's shells.
    skin(frame, frame_weights(g), arm)
    coat = coat_shells(g)
    skin(coat, g["coat_weights"], arm)
    high.hide_render = True
    high["dt_scrap"] = True
    g["FRAME_HIGH"] = high
    print(f"[dt] crew frame: {len(frame.data.polygons)} faces (high {len(high.data.polygons)}), coat {len(coat.data.polygons)}")
    return frame, high, coat
