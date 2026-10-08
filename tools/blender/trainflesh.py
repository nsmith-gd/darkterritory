"""Organic skins for the train's own creatures (tools/blender/brakeman.py, knotter.py, hotbox.py): the Ribbit's
smooth-blended flesh (a signed-distance field whose terms are also the parts' solids) and rig.fuse with the passes the
Ribbit and the Moose added to it (`settle` onto that field, `relax`, `quads`). It sits beside flesh.py, the outside
creatures' own (G1.6's: the Mourners, the Freight Beetle, the Tower Jaw), whose API differs; each set of scripts imports
its own. Use: build the parts, `kit.build()` with no fusions, then `trainflesh.fuse(kit, ...)` per skin.
"""
import math

import bpy  # noqa: F401  (first: as a Python module, Blender's bmesh is only importable after it)
import bmesh
import numpy as np
from mathutils import Vector



def _smin(a, b, k):
    """The polynomial smooth minimum: the union of two volumes with the crease between them filled over `k` metres."""
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def _ellipsoid(c, r, R=None):
    c, r = np.array(c, np.float64), np.array(r, np.float64)
    M = None if R is None else np.array([[R[i][j] for j in range(3)] for i in range(3)], np.float64)

    def f(P):
        q = P - c
        if M is not None:
            q = q @ M
        k0 = np.linalg.norm(q / r, axis=1)
        k1 = np.linalg.norm(q / (r * r), axis=1)
        return k0 * (k0 - 1) / np.maximum(k1, 1e-9)
    return f


def _round_cone(a, b, r1, r2):
    """A limb's length between two joints, tapering from r1 to r2 (rounded at both ends)."""
    a, b = np.array(a, np.float64), np.array(b, np.float64)
    ba = b - a
    l2 = float(ba @ ba)
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2

    def f(P):
        pa = P - a
        y = pa @ ba
        z = y - l2
        x2 = np.sum((pa * l2 - np.outer(y, ba)) ** 2, axis=1)
        y2 = y * y * l2
        z2 = z * z * l2
        k = math.copysign(1.0, rr) * rr * rr * x2 if rr != 0 else np.zeros_like(x2)
        mid = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - r1
        end = np.where(np.sign(z) * a2 * z2 > k, np.sqrt(x2 + z2) * il2 - r2, mid)
        return np.where(np.sign(y) * a2 * y2 < k, np.sqrt(x2 + y2) * il2 - r1, end)
    return f


class Flesh:
    """A skin's volumes: each `blob`/`limb` both models a solid in `part` (the union fuse remeshes, and where the skin's
    materials and bone weights come from) and adds a term to the field, smooth-blended over its `k` metres into what's
    there already; `carve` takes a volume out softly (an eye socket, a mouth). `settle` moves points onto the field's
    surface (fuse: the union's voxels, before the retopology). `name` labels its print."""

    def __init__(self, part, name="flesh"):
        self.part, self.name = part, name
        self.ops = []

    def __call__(self, P):
        d = None
        for fn, k, cut in self.ops:
            v = fn(P)
            if d is None:
                d = v if not cut else -v
            elif cut:
                d = -_smin(-d, v, k)
            else:
                d = _smin(d, v, k)
        return d

    def blob(self, c, r, k, mat, bones, rot=None, around=18, rings=10, fmat=None):
        R = None if rot is None else rot.to_3x3()
        self.part.blob(Vector(c), r, around, rings, mat, bones, rot=rot, fmat=fmat)
        self.ops.append((_ellipsoid(tuple(c), r, R), k, False))

    def limb(self, points, radii, k, mat, bones, sides=14, ref=(1, 0, 0), fmat=None):
        pts = [Vector(p) for p in points]
        self.part.tube(pts, radii, sides, mat, bones, ref=ref, cap0="point", cap1="point", fmat=fmat)
        cones = [_round_cone(tuple(a), tuple(b), ra, rb) for a, b, ra, rb in zip(pts, pts[1:], radii, radii[1:])]
        self.ops.append((lambda P, cones=cones: np.min([f(P) for f in cones], axis=0), k, False))

    def carve(self, c, r, k, rot=None):
        self.ops.append((_ellipsoid(tuple(c), r, None if rot is None else rot.to_3x3()), k, True))

    def surface(self, origin, direction, reach=0.5):
        """Where a ray from `origin` (inside) along `direction` leaves the flesh so far."""
        o, d = np.array(tuple(origin), np.float64), np.array(tuple(direction), np.float64)
        d /= np.linalg.norm(d)
        lo, hi = 0.0, reach
        for _ in range(40):
            mid = (lo + hi) / 2
            if self(o[None, :] + d[None, :] * mid)[0] < 0:
                lo = mid
            else:
                hi = mid
        return Vector(tuple(o + d * lo))

    def settle(self, P, steps=8):
        P = np.asarray(P, np.float64).copy()
        e = 0.0008
        for _ in range(steps):
            d = self(P)
            g = np.stack([(self(P + np.array(a) * e) - self(P - np.array(a) * e)) / (2 * e)
                          for a in ((1, 0, 0), (0, 1, 0), (0, 0, 1))], axis=1)
            step = (d / np.maximum((g * g).sum(axis=1), 0.05))[:, None] * g
            n = np.linalg.norm(step, axis=1)
            P -= step * np.minimum(1.0, 0.015 / np.maximum(n, 1e-9))[:, None]
        print(f"[dt] {self.name}: {len(P)} points settled, worst {np.abs(self(P)).max() * 1000:.2f} mm off")
        return P


def fuse(kit, name, parts, voxel, faces, cut=None, lose=0.015, settle=None, relax=3, quads=True, symmetric=True):
    """One skin from several built parts (a Look Review ask: the kit's limbs were tubes pushed into a body, a seam and
    often a gap at every hip and shoulder). The crew body's way (tools/blender/crewbody.py), for any creature:
      1. the parts' union, voxel-remeshed at `voxel` m: the solids flow into one another, a haunch into a flank, a
         finger into a hand;
      2. QuadriFlow down to about `faces` quads, smooth-shaded: the game mesh;
      3. each new face takes the material of the part's face nearest it, each new vertex the bone weights of the
         nearest point on the parts (interpolated over that face), then the weights are relaxed over the skin's
         edges so the joins bend as one, at most four bones a vertex.
    The union is closed (the parts' holes filled to remesh them): `cut` opens it again where an opening was meant.
    UVs are projected (smart_project): tools/models re-unwraps for its atlas. The parts are replaced by the skin.
    Beyond rig.fuse: `settle(points Nx3) -> Nx3` moves the union's voxels onto a smooth-blended field before the
    retopology (`Flesh.settle`: the creases where the parts meet filled as flesh fills them, the Ribbit's way), `relax`
    passes of smoothing before the remesh (the Moose's), `quads=False` goes straight to the collapse (iron, horn), and
    `symmetric=False` lets the retopology be lopsided (a rope's lay turns one way)."""
    from mathutils.bvhtree import BVHTree
    from mathutils.interpolate import poly_3d_calc

    rig_ = kit.skeleton.rig
    srcs = [o for o in bpy.data.objects if o.type == "MESH" and o.parent is rig_ and o.name in parts]
    if not srcs:
        return None
    # The reference: the parts as one mesh (materials and vertex groups merged by name), for the lookups.
    mats, groups = [], []
    verts, polys, pmat, vw = [], [], [], []
    for o in sorted(srcs, key=lambda o: o.name):
        names = {g.index: g.name for g in o.vertex_groups}
        base = len(verts)
        for v in o.data.vertices:
            verts.append(o.matrix_world @ v.co)
            vw.append({names[g.group]: g.weight for g in v.groups if g.weight > 0})
        for g in o.vertex_groups:
            if g.name not in groups:
                groups.append(g.name)
        for f in o.data.polygons:
            m = o.data.materials[f.material_index]
            if m not in mats:
                mats.append(m)
            polys.append([base + i for i in f.vertices])
            pmat.append(mats.index(m))
    tree = BVHTree.FromPolygons(verts, polys)

    # 1. The union. The parts are open (a tube's UV seam is a column of doubled vertices, its ends often uncapped): welded
    # and their holes filled first, or the remesh can't tell inside from out and wraps both sides of every surface.
    me = bpy.data.meshes.new(f"{kit.name}_{name}")
    me.from_pydata([tuple(v) for v in verts], [], polys)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()
    skin = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(skin)
    # (A second fuse in one script: the first one's removed parts linger in the view layer as nothing until it updates.)
    bpy.context.view_layer.update()
    for o in bpy.context.view_layer.objects:
        if o is not None:
            o.select_set(False)
    bpy.context.view_layer.objects.active = skin
    skin.select_set(True)
    me.remesh_voxel_size = voxel
    me.remesh_voxel_adaptivity = 0.0
    bpy.ops.object.voxel_remesh()
    if settle is not None:
        # Each vertex moved onto the script's own surface, then remeshed again so wherever two sides met in the move it's
        # one surface.
        co = np.empty(len(skin.data.vertices) * 3, np.float32)
        skin.data.vertices.foreach_get("co", co)
        skin.data.vertices.foreach_set("co", np.asarray(settle(co.reshape(-1, 3)), np.float32).ravel())
        skin.data.update()
        bpy.ops.object.voxel_remesh()
    # Only the outer surfaces: a cavity the union closed over is a shell of its own, facing in (a negative volume).
    bm = bmesh.new()
    bm.from_mesh(skin.data)
    shells, seen = [], set()
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, shell = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            shell.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        shells.append(shell)

    def volume(shell):
        return sum(f.verts[0].co.dot(f.verts[i].co.cross(f.verts[i + 1].co)) for f in shell for i in range(1, len(f.verts) - 1)) / 6

    drop = [f for sh in shells if len(sh) < 40 or volume(sh) <= 0 for f in sh]
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(skin.data)
    bm.free()
    mod = skin.modifiers.new("relax", "SMOOTH")
    mod.factor, mod.iterations = 0.5, relax
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # QuadriFlow refuses a mesh with any edge under 0.1 mm a side, which the remesh and the relax leave where two
    # surfaces just touch.
    bm = bmesh.new()
    bm.from_mesh(skin.data)
    bmesh.ops.dissolve_degenerate(bm, dist=0.0003, edges=bm.edges)
    # (And any edge left with no face on it, a wire the dissolve leaves where a fold closed up: not a surface, and enough
    # on its own for QuadriFlow to call the whole skin not manifold.)
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    if cut is not None:
        # Opened again where it was meant to be open, on the dense union so the opening's edge is the voxel's fine one.
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if cut(f.calc_center_median())], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    # (QuadriFlow takes an open edge, but not three faces on one edge nor two faces turned against each other.)
    ready = all(len(e.link_loops) in (1, 2) and (len(e.link_loops) == 1 or e.link_loops[0].vert != e.link_loops[1].vert)
                for e in bm.edges) and not any(
        all(abs(a - b) <= 1e-4 for a, b in zip(e.verts[0].co, e.verts[1].co)) for e in bm.edges)
    bm.to_mesh(skin.data)
    bm.free()
    # 2. The game mesh: QuadriFlow's quads, symmetric across x. It can drop a limb thinner than its quads (a shin, an
    # ankle) or refuse a mesh outright (a sheet thinner than the voxel pinches the union into a non-manifold seam): its
    # result is checked against the union, and where it lost anything a collapse of the union to the same count (which
    # never loses a form, only spends its triangles less evenly) is used instead.
    dense = skin.data.copy()
    before = len(dense.polygons)
    lost = None
    # (QuadriFlow's result isn't the same run to run, a good one or a ruin from the same union: a few tries.)
    for seed in range(4) if ready and quads else ():
        skin.data = dense.copy()
        bpy.ops.object.quadriflow_remesh(target_faces=faces, use_mesh_symmetry=symmetric, use_preserve_sharp=False,
                                         use_preserve_boundary=cut is not None, smooth_normals=False, seed=seed)
        if len(skin.data.polygons) > faces * 2:
            continue
        got = BVHTree.FromPolygons([v.co for v in skin.data.vertices], [list(f.vertices) for f in skin.data.polygons])
        probe = [v.co for v in dense.vertices][::max(1, len(dense.vertices) // 4000)]
        lost = sum(1 for p in probe if got.find_nearest(p)[3] > max(4 * voxel, 0.008)) / len(probe)
        print(f"[dt] fuse {name}: QuadriFlow (seed {seed}) {len(skin.data.polygons)} quads, {lost:.1%} of the union off it")
        if lost <= lose:
            break
    if lost is None or lost > lose:
        why = "asked not to" if not quads else "not manifold" if not ready else "it failed" if lost is None else f"it lost {lost:.1%} of the form"
        print(f"[dt] fuse {name}: no QuadriFlow ({why}), collapsing instead")
        skin.data = dense
        mod = skin.modifiers.new("dt_dec", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        # (The remesh is all quads, `before` of them twice that in triangles; QuadriFlow lands about a fifth under `faces`:
        # the collapse is aimed at the same count.)
        mod.ratio = min(1.0, 0.8 * faces / before)
        mod.use_symmetry = symmetric
        mod.symmetry_axis = "X"
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if cut is not None:
        # (And again on the game mesh: whatever of it still spans the opening.)
        bm = bmesh.new()
        bm.from_mesh(skin.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if cut(f.calc_center_median())], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(skin.data)
        bm.free()
    me = skin.data
    bpy.ops.object.shade_smooth()
    for m in mats:
        me.materials.append(m)
    # 3. Materials and weights from the parts.
    for f in me.polygons:
        _, _, i, _ = tree.find_nearest(f.center)
        f.material_index = pmat[i]
    w = []
    for v in me.vertices:
        hit, _, i, _ = tree.find_nearest(v.co)
        ring = polys[i]
        k = poly_3d_calc([verts[j] for j in ring], hit)
        acc = {}
        for j, kk in zip(ring, k):
            for b, x in vw[j].items():
                acc[b] = acc.get(b, 0.0) + x * kk
        w.append(acc)
    nbr = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        nbr[a].append(b)
        nbr[b].append(a)
    for _ in range(4):
        nw = []
        for i, acc in enumerate(w):
            out = {b: x * 0.5 for b, x in acc.items()}
            if nbr[i]:
                share = 0.5 / len(nbr[i])
                for j in nbr[i]:
                    for b, x in w[j].items():
                        out[b] = out.get(b, 0.0) + x * share
            else:
                out = dict(acc)
            nw.append(out)
        w = nw
    vg = {g: skin.vertex_groups.new(name=g) for g in groups}
    for i, acc in enumerate(w):
        top = sorted(acc.items(), key=lambda t: (-t[1], t[0]))[:4]
        tot = sum(x for _, x in top)
        for b, x in top:
            if x / tot > 0.02:
                vg[b].add([i], x / tot, "REPLACE")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in srcs:
        bpy.data.objects.remove(o)
    skin.name = name
    skin.parent = rig_
    mod = skin.modifiers.new("Armature", "ARMATURE")
    mod.object = rig_
    skin["dt_fused"] = 1
    return skin


