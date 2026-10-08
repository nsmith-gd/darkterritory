"""Where each texel of a baked atlas is on the model, for the train's own creatures' recipes that paint by position
(tools/models/recipes/knotter.py, hotbox.py; the Gannet's and the Ribbit's way): the joined game mesh's triangles
rasterized from its unwrap, at rest, into per-texel position, normal, region (the kit material's name after its dot) and
part, grown a few texels past every island's edge so filtering never reads the empty atlas between them. Deterministic:
no render. It sits beside texels.py, the outside creatures' own (G1.6's), whose API differs.

    t = traintexels.Texels(atlas, size)
    t.P, t.N (SxSx3), t.is_("knotter_claw", ...) (SxS bool), t.part("body"), t.noise(seed, scale), t.field(fn)
"""
import numpy as np

import cook


def suffix(name):
    """A kit material's region: "wool.brakeman_coat" -> "brakeman_coat" (Blender's ".001" dropped)."""
    return name.split(".")[1] if "." in name else name


class Texels:
    def __init__(self, atlas, S):
        self.S = S
        me = atlas.low.data
        me.calc_loop_triangles()
        nv, nl = len(me.vertices), len(me.loops)
        co = np.empty(nv * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        no = np.empty(nv * 3, np.float32)
        me.vertices.foreach_get("normal", no)
        no = no.reshape(-1, 3)
        uv = np.empty(nl * 2, np.float32)
        me.uv_layers["UVMap"].data.foreach_get("uv", uv)
        uv = uv.reshape(-1, 2)
        lv = np.empty(nl, np.int32)
        me.loops.foreach_get("vertex_index", lv)
        nt = len(me.loop_triangles)
        tl = np.empty(nt * 3, np.int32)
        me.loop_triangles.foreach_get("loops", tl)
        tl = tl.reshape(-1, 3)
        tp = np.empty(nt, np.int32)
        me.loop_triangles.foreach_get("polygon_index", tp)
        self.regions = sorted({suffix(n) for n in atlas.slot_name if n})
        region_of = np.array([self.regions.index(suffix(n)) if n else -1 for n in atlas.slot_name], np.int32)
        self.baked = list(atlas.baked)
        P = np.zeros((S, S, 3), np.float32)
        N = np.zeros((S, S, 3), np.float32)
        K = np.full((S, S), -1, np.int32)
        PART = np.full((S, S), -1, np.int32)
        for t in range(nt):
            loops = tl[t]
            if atlas.keep[tp[t]]:
                continue
            x, y = uv[loops][:, 0] * S - 0.5, (1 - uv[loops][:, 1]) * S - 0.5
            x0, x1 = int(max(0, np.floor(x.min()))), int(min(S - 1, np.ceil(x.max())))
            y0, y1 = int(max(0, np.floor(y.min()))), int(min(S - 1, np.ceil(y.max())))
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1, dtype=np.float32), np.arange(y0, y1 + 1, dtype=np.float32))
            d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
            if abs(d) < 1e-12:
                continue
            a = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
            b = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
            c = 1 - a - b
            inside = (a >= -0.02) & (b >= -0.02) & (c >= -0.02)
            if not inside.any():
                continue
            vi = lv[loops]
            w = np.stack([a, b, c], -1)[inside]
            rows, cols = gy[inside].astype(np.int32), gx[inside].astype(np.int32)
            P[rows, cols] = w @ co[vi]
            N[rows, cols] = w @ no[vi]
            K[rows, cols] = region_of[tp[t]]
            PART[rows, cols] = atlas.part_of[tp[t]]
        for _ in range(4):
            empty = K < 0
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                take = empty & (np.roll(np.roll(K, dy, 0), dx, 1) >= 0)
                for arr in (K, PART, P, N):
                    arr[take] = np.roll(np.roll(arr, dy, 0), dx, 1)[take]
                empty = K < 0
        N /= np.maximum(np.linalg.norm(N, axis=-1, keepdims=True), 1e-6)
        self.P, self.N, self.K, self.PART = P, N, K, PART
        self.flat = P.reshape(-1, 3)
        print(f"[dt] {atlas.name} atlas", f"{(K >= 0).mean():.0%} covered by", {k: int((K == i).sum()) for i, k in enumerate(self.regions)})

    def field(self, fn):
        return np.asarray(fn(self.flat), np.float32).reshape(self.S, self.S)

    def noise(self, seed, scale):
        return self.field(lambda q: cook.noise_np(q, seed, scale))

    def is_(self, *names):
        return np.isin(self.K, [self.regions.index(n) for n in names if n in self.regions])

    def part(self, name):
        return self.PART == self.baked.index(name)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


def with_glow(name, emit):
    """cook.bake_layers, with the atlas material's emission image (`emit` SxSx3, linear): the layer's emissive mask, drawn by
    the engine in the glow's colour and scaled by the instance's glow (the Gannet's sacs' way)."""
    import bpy
    real = cook.bake_layers
    S = emit.shape[0]

    def bake(layer, objs, **kw):
        for m in bpy.data.materials:
            if m.name == f"{name}_final" and m.use_nodes:
                img = bpy.data.images.new(f"{name}_emit", S, S, alpha=True)
                rgba = np.ones((S, S, 4), np.float32)
                rgba[..., :3] = emit
                img.pixels.foreach_set(rgba[::-1].ravel())
                tex = m.node_tree.nodes.new("ShaderNodeTexImage")
                tex.image = img
                bsdf = m.node_tree.nodes["Principled BSDF"]
                m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
        return real(layer, objs, **kw)
    cook.bake_layers = bake
