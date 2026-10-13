"""Flesh: a creature's body as smooth volumes that are both rig.fuse's solids and a signed-distance field's terms (the
Ribbit's way, tools/blender/ribbit.py; the Look Review: organic, one continuous skin, not primitives bolted together).

Each `blob` (an ellipsoid) or `limb` (a chain of tapering round cones) models its solid into a rig Part (the union's
input, and where the skin's materials and bone weights come from) and adds a term to the field, smooth-blended into what
is there already over its own `k` metres; `carve` takes a volume out softly. `settle` moves the dense union's vertices
onto the field's surface before QuadriFlow (rig.fuse's `settle`), so a shoulder swells out of a flank with the crease
filled as flesh fills it. Shared by the Mourners, the Freight Beetle and Tower Jaw (notes 362, 363, 366).
"""
import math

import numpy as np

import rig
from rig import Vector, noise3


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def h01(seed, *k):
    """A fixed hash in 0..1 (no RNG: rebuilds are identical)."""
    k = list(k) + [0, 0, 0]
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, k[1] * 2.11 + 0.37, k[2] * 0.71 + 0.53)), seed, 1.0)


def R(rx=0.0, ry=0.0, rz=0.0):
    return rig.rot(rx, ry, rz).to_matrix()


def _smin(a, b, k):
    """The polynomial smooth minimum: the union of two volumes with the crease between them filled over `k` metres."""
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def _ellipsoid(c, r, M=None):
    c, r = np.array(c, np.float64), np.array(r, np.float64)
    M = None if M is None else np.array([[M[i][j] for j in range(3)] for i in range(3)], np.float64)

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
    """A skin's volumes (see the module): `blob`, `limb`, `carve`; `surface` finds where a ray leaves it; `settle` is
    rig.fuse's."""

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
        M = None if rot is None else rot.to_3x3()
        self.part.blob(Vector(c), r, around, rings, mat, bones, rot=rot, fmat=fmat)
        self.ops.append((_ellipsoid(c, r, M), k, False))

    def limb(self, points, radii, k, mat, bones, sides=14, ref=(1, 0, 0), fmat=None):
        pts = [Vector(p) for p in points]
        self.part.tube(pts, radii, sides, mat, bones, ref=ref, cap0="point", cap1="point", fmat=fmat)
        cones = [_round_cone(tuple(a), tuple(b), ra, rb) for a, b, ra, rb in zip(pts, pts[1:], radii, radii[1:])]
        self.ops.append((lambda P, cones=cones: np.min([f(P) for f in cones], axis=0), k, False))

    def carve(self, c, r, k, rot=None):
        self.ops.append((_ellipsoid(c, r, None if rot is None else rot.to_3x3()), k, True))

    def surface(self, origin, direction, reach=0.5):
        """Where a ray from `origin` (inside) along `direction` leaves the flesh so far."""
        o, d = np.array(origin, np.float64), np.array(direction, np.float64)
        d /= np.linalg.norm(d)
        lo, hi = 0.0, reach
        for _ in range(40):
            mid = (lo + hi) / 2
            if self(o[None, :] + d[None, :] * mid)[0] < 0:
                lo = mid
            else:
                hi = mid
        return Vector(tuple(o + d * lo))

    def normal(self, p, e=0.002):
        p = np.array(tuple(p), np.float64)
        g = [(self((p + np.array(a) * e)[None, :])[0] - self((p - np.array(a) * e)[None, :])[0]) / (2 * e)
             for a in ((1, 0, 0), (0, 1, 0), (0, 0, 1))]
        return Vector(g).normalized()

    def settle(self, P, steps=8, most=0.015):
        P = np.asarray(P, np.float64).copy()
        e = 0.0008
        for _ in range(steps):
            d = self(P)
            g = np.stack([(self(P + np.array(a) * e) - self(P - np.array(a) * e)) / (2 * e)
                          for a in ((1, 0, 0), (0, 1, 0), (0, 0, 1))], axis=1)
            step = (d / np.maximum((g * g).sum(axis=1), 0.05))[:, None] * g
            n = np.linalg.norm(step, axis=1)
            P -= step * np.minimum(1.0, most / np.maximum(n, 1e-9))[:, None]
        print(f"[dt] {self.name}: {len(P)} points settled, worst {np.abs(self(P)).max() * 1000:.2f} mm off")
        return P


def norm(w):
    w = {b: v for b, v in w.items() if v > 0.02}
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


def mix(a, b, k):
    """Weights `a` going over to `b` by k (0..1)."""
    out = {n: v * (1 - k) for n, v in a.items()}
    for n, v in b.items():
        out[n] = out.get(n, 0) + v * k
    return norm(out)


def chain(joints, bones, soft=0.035):
    """Weights along a limb: each point its nearest length's bone, blended half and half at a joint over `soft` metres."""
    joints = [Vector(j) for j in joints]

    def fn(p):
        p = Vector(p)
        best, bi, bt = 1e9, 0, 0.0
        for i, (a, b) in enumerate(zip(joints, joints[1:])):
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
            d = (a + ab * t - p).length
            if d < best:
                best, bi, bt = d, i, t
        L = (joints[bi + 1] - joints[bi]).length
        w = {bones[bi]: 1.0}
        if bi > 0 and bt * L < soft:
            w = mix(w, {bones[bi - 1]: 1.0}, 0.5 * (1 - bt * L / soft))
        if bi < len(bones) - 1 and (1 - bt) * L < soft:
            w = mix(w, {bones[bi + 1]: 1.0}, 0.5 * (1 - (1 - bt) * L / soft))
        return norm(w)
    return fn
