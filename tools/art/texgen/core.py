"""Colour, palette and determinism helpers shared by every texture.

All working images are float32 numpy arrays, channel-last, in *linear* light.
They are sRGB-encoded only when written (core.encode). Keeping the maths linear means
AO, cavity and grime multiply the way light does; encoding at the end is what the
engine expects (it decodes sRGB on sample, Palette.cs uses the same 2.2-ish curve).
"""
from __future__ import annotations

import hashlib
from dataclasses import dataclass, field

import numpy as np

# Final texture size. The fidelity target moved to 2008-2012 (ARCHITECTURE §8 note 53): 512 px a map, which at the
# library's tile sizes is 256-512 px/m (the GDD §27 hero density everywhere). The CC0 sources are 1024.
N = 512
# The builders work at 512 (their layouts are authored in its pixels: board widths, brick courses), and in the "ps3"
# era that is also the final size; photo sources come in from 1024.
WORK = 512
# The era: "ps3" (the benchmarks) keeps the full 8 bits, no chroma blocking and a fine grain; "ps2" is the old
# compressed-texture feel (quantised levels, 2x2 chroma, a one-texel grain).
ERA = "ps3"

# Height fields (and normal maps) the builder passed through the shading helpers while it ran: what the texture's
# relief is, for its normal map (textures.py resets it per texture).
CAPTURED: list = []


def capture(kind: str, field):
    if field is not None:
        CAPTURED.append((kind, field))


def srgb_to_lin(x):
    x = np.asarray(x, dtype=np.float32)
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4).astype(np.float32)


def lin_to_srgb(x):
    x = np.clip(np.asarray(x, dtype=np.float32), 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055).astype(np.float32)


def hexc(h: str) -> np.ndarray:
    """'#6B3A26' -> linear RGB float32[3]."""
    h = h.lstrip("#")
    return srgb_to_lin(np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], dtype=np.float32))


def lum(rgb: np.ndarray) -> np.ndarray:
    """Rec.709 luminance of linear RGB (last axis)."""
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def saturate(x):
    return np.clip(x, 0.0, 1.0)


def lerp(a, b, t):
    """a + (b - a) t, where a/b may be colours (3,) or images and t a scalar or HxW map
    (broadcast over the colour channels)."""
    t = np.asarray(t, dtype=np.float32)
    if t.ndim == 2 and any(np.ndim(x) in (1, 3) and np.shape(x)[-1] == 3 for x in (a, b)):
        t = t[..., None]
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    t = saturate((np.asarray(x, dtype=np.float32) - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


def rgb(h, w, c) -> np.ndarray:
    """A solid image of linear colour c."""
    return np.broadcast_to(np.asarray(c, dtype=np.float32), (h, w, 3)).copy()


def rng_for(*keys) -> np.random.Generator:
    """A generator seeded from a stable hash of the keys (never Python's per-process hash())."""
    digest = hashlib.sha256("/".join(str(k) for k in keys).encode()).digest()
    return np.random.default_rng(int.from_bytes(digest[:8], "little"))


# --- GDD §28 palette -------------------------------------------------------------------------
# World base. These are the greybox's own colours (src/DarkTerritory.Game/Palette.cs), so a
# textured surface averages out to the colour its greybox stand-in had.
PAL = {
    "charcoal": "#2B2B2B",
    "soot": "#1A1A1A",
    "brown": "#3A2E26",
    "rust": "#6B3A26",
    "iron": "#4A4D52",
    "brass": "#7A6440",
    "bluegrey": "#4A5563",
    "olive": "#3C3F2E",
    "ballast": "#4B4640",
    "pine": "#1F2620",
    # Warm accents: emissive only.
    "furnace": "#D06020",
    "amber": "#E0A050",
    "ember": "#A02810",
    # Corruption accents, sparingly and never neon.
    "violet": "#4A3446",
    "ivory": "#B8AE96",
    "blackred": "#3A1414",
    "fungal": "#8A7A5E",
    "bile": "#6A7038",
}

# Tone ramps: a material's values run dark -> light through these stops. The hand pass maps the
# photo's (or procedural) luminance through one, which is what pulls every texture into the
# same restrained palette (§28) regardless of what the source photo looked like.
RAMPS = {
    # Metal ramps are raw metal albedo: pbr_to_legacy darkens metals to ~45% (linear), which
    # lands them on the palette's dull iron grey and tarnished brass.
    "iron": ["#24262A", "#3C3F44", "#585C62", "#767B82", "#9095A0"],
    "iron_warm": ["#28241F", "#403A34", "#5C554C", "#787066", "#908678"],
    "soot": ["#0C0C0C", "#141414", "#1C1B1A", "#282624", "#383432"],
    "rust": ["#1A110C", "#2E1D14", "#4A2A1C", "#6B3A26", "#80502F"],
    "oxide_paint": ["#1E120E", "#34201A", "#4E2E22", "#643C2C", "#7A5040"],
    "olive_paint": ["#16170F", "#24261A", "#343726", "#454833", "#5A5D46"],
    "black_paint": ["#0E0E0F", "#161718", "#1F2022", "#2C2E31", "#3E4145"],
    "brass": ["#302616", "#524228", "#7E6840", "#A08658", "#B49C6E"],
    "copper": ["#2E1C12", "#503022", "#7A4A36", "#9A644C", "#AE7C62"],
    "wood": ["#110D0A", "#1E1712", "#2E241D", "#3E3128", "#524336"],
    "wood_grey": ["#1A1A19", "#2A2927", "#3C3A36", "#504D48", "#67635C"],
    "sleeper": ["#0E0C0A", "#17130F", "#221C17", "#302822", "#40362E"],
    "ground": ["#121110", "#1E1C19", "#2C2925", "#3E3A34", "#534D45"],
    "ballast": ["#161513", "#26241F", "#38352F", "#4B4640", "#625C54"],
    "mud": ["#0E0C0A", "#191511", "#241E18", "#322A21", "#433A2E"],
    "grass": ["#121309", "#1D1F12", "#2A2C1B", "#3C3F2E", "#50523B"],
    "forest": ["#0A0B09", "#121410", "#1B1D17", "#27281F", "#36352A"],
    "marsh": ["#08090A", "#0F1310", "#171D17", "#222A1F", "#30382A"],
    "rock": ["#0F1011", "#1A1B1D", "#28292B", "#393A3C", "#4E4E4E"],
    "stone": ["#16140F", "#262320", "#37332E", "#4B4640", "#615B52"],
    "brick": ["#140D0A", "#24160F", "#382219", "#4E3024", "#634034"],
    "concrete": ["#1A1A19", "#2B2A28", "#3E3D39", "#54524D", "#6B6862"],
    "plaster": ["#262320", "#3A3631", "#524C44", "#6B645A", "#7E766A"],
    "slate": ["#0E0F11", "#17191C", "#212428", "#2E3238", "#3E434A"],
    "pine": ["#07090A", "#0E1210", "#151B17", "#1F2620", "#2D3530"],
    "canvas": ["#15130E", "#221F17", "#302B20", "#403A2B", "#524A38"],
    "leather": ["#120C08", "#21160F", "#342218", "#4A3222", "#5E4230"],
    "wool": ["#101012", "#18181B", "#222226", "#2E2E33", "#3C3C42"],
    "skin": ["#3A2A22", "#5A4234", "#7A5C4A", "#94735E", "#A88A74"],
    "flesh": ["#1E1418", "#3A2830", "#6A5A52", "#9A8C78", "#B8AE96"],
    "fungal": ["#1A1610", "#3A3224", "#5E523E", "#8A7A5E", "#9E9072"],
    "mineral": ["#101114", "#20222A", "#363842", "#50535C", "#6E7078"],
    "tar": ["#050505", "#0A0909", "#110F0F", "#1A1716", "#262120"],
    "glass": ["#0A0C0D", "#121517", "#1B1F22", "#262B2F", "#343A3E"],
    "coal": ["#070707", "#0D0D0E", "#141416", "#1E1F22", "#2C2E32"],
    "slag": ["#0E0D0D", "#1A1817", "#28241F", "#3A342C", "#4E463A"],
}


def ramp_lut(name_or_stops, n=256) -> np.ndarray:
    """A 256-entry linear-RGB LUT interpolated (in sRGB, as a painter would) between ramp stops."""
    stops = RAMPS[name_or_stops] if isinstance(name_or_stops, str) else name_or_stops
    cols = np.array([[int(s.lstrip("#")[i:i + 2], 16) / 255.0 for i in (0, 2, 4)] for s in stops], dtype=np.float32)
    xs = np.linspace(0, 1, len(stops))
    t = np.linspace(0, 1, n)
    out = np.stack([np.interp(t, xs, cols[:, c]) for c in range(3)], axis=-1)
    return srgb_to_lin(out)


def apply_ramp(t: np.ndarray, ramp) -> np.ndarray:
    """Map t in 0..1 through a ramp -> linear RGB."""
    lut = ramp_lut(ramp)
    idx = np.clip((t * 255.0).astype(np.int32), 0, 255)
    return lut[idx]


@dataclass
class Tex:
    """One finished texture, final size, ready to encode."""
    name: str
    family: str
    diffuse: np.ndarray            # HxWx3 linear
    spec: np.ndarray               # HxW 0..1
    gloss: np.ndarray              # HxW 0..1
    emissive: np.ndarray | None = None  # HxW 0..1
    alpha: np.ndarray | None = None     # HxW 0..1 (None = opaque)
    normal: np.ndarray | None = None    # HxWx3 tangent space (x right, y down the image, z out), None = flat
    tile_metres: float | None = 1.0
    alpha_test: bool = False
    tiling: bool = True
    sources: list = field(default_factory=list)
    extra: dict = field(default_factory=dict)
    quant: int = 40                # sRGB levels per channel for the "compression feel"
    grain: float = 0.05            # per-texel grain amplitude (fraction of value)
    chroma_block: int = 2          # chroma averaged over NxN blocks: a whiff of DXT/JPEG
    dither: bool = True
