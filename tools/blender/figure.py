"""Humanoid building blocks for the corrupted people (the Switchman, the Hollow, the Soot children): tubes that
follow an SK_Human's bones, a torso lofted up the spine, a head. The crew (crew.py) is modelled by hand in more
detail; these are for figures seen at the edge of the lamplight, where proportion and outline are everything
(GDD §26.1) and a coat is a coat.
"""
import math

import rig
from rig import Vector, along, noise3


def chain_points(sk, bones, end=True):
    """Heads of `bones` in order, then the last one's tail."""
    pts = [sk[b].head.copy() for b in bones]
    if end:
        pts.append(sk[bones[-1]].tail.copy())
    return pts


def limb(part, sk, bones, radii, sides, mat, extra_bones=(), ref=(0, 1, 0), k=6.0, shape=None, cap1=False, sub=2, **kw):
    """A tube down a bone chain, `sub` rings per bone, radii per joint (len(bones) + 1), weighted to the chain by
    distance (soft at the joints)."""
    joints = chain_points(sk, bones)
    pts, rads = [], []
    for i in range(len(joints) - 1):
        for s in range(sub):
            t = s / sub
            pts.append(joints[i].lerp(joints[i + 1], t))
            r0, r1 = radii[i], radii[i + 1]
            if isinstance(r0, (tuple, list)):
                rads.append(tuple(a + (b - a) * t for a, b in zip(r0, r1)))
            else:
                rads.append(r0 + (r1 - r0) * t)
    pts.append(joints[-1])
    rads.append(radii[-1])
    return part.tube(pts, rads, sides, mat, (list(bones) + list(extra_bones), k), ref=ref, shape=shape, cap1=cap1, **kw)


def torso(part, sk, rings, sides, mat, square=0.8, shape=None, fmat=None, fuv=None, spine=None):
    """A torso lofted up through (z, rx, ry, dy) rings, weighted up the spine by height."""
    zs = {n: (sk[n].head.z + sk[n].tail.z) / 2 for n in ("pelvis", "spine_01", "spine_02", "spine_03", "neck")}
    stops = spine or [(zs["pelvis"], "pelvis"), (zs["spine_01"], "spine_01"), (zs["spine_02"], "spine_02"),
                      (zs["spine_03"], "spine_03"), (zs["neck"], "neck")]
    wfn = along("z", stops)
    loops, cents = [], []
    for z, rx, ry, dy in rings:
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides
            sa, ca = math.sin(a), math.cos(a)
            sa = math.copysign(abs(sa) ** square, sa)
            ca = math.copysign(abs(ca) ** square, ca)
            p = Vector((sa * rx, dy + ca * ry, z))
            if shape is not None:
                p = Vector(shape(len(loops), j, a, p))
            ring.append(p)
        loops.append(ring)
        cents.append(Vector((0, dy, z)))
    return part.loft(loops, mat, wfn, centres=cents, fmat=fmat, fuv=fuv)


def tatters(part, sk, anchors, mat, seed, length=0.18, width=0.07):
    """Ragged flaps hanging from points on the body: burnt cloth, sloughed soot. Each (point, bone, droop dir)."""
    for k, (p, bone, down) in enumerate(anchors):
        p = Vector(p)
        d = Vector(down).normalized()
        side = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
        L = length * (0.7 + 0.5 * (noise3(p, seed + k, 7.0) * 0.5 + 0.5))
        W = width * (0.7 + 0.4 * (noise3(p, seed + k + 50, 5.0) * 0.5 + 0.5))
        outline = [p - side * W / 2, p + side * W / 2, p + side * W * 0.3 + d * L * 0.8, p + d * L,
                   p - side * W * 0.4 + d * L * 0.6]
        n = side.cross(d).normalized()
        part.slab(outline, 0.008, mat, bone, down=tuple(-n))
