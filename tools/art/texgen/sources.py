"""CC0 photo sources, with provenance and a procedural fallback.

Intake rule (intake/README.md): sources are read from intake/_sources/ and never edited; every
texture records which source files fed it; anything without a clear licence is not used.
`tools/art/fetch_sources.sh` clones the pinned commit. If it's missing, each lookup falls back
to a procedural stand-in of roughly the same tone and logs a warning, so the generator still
runs (the committed outputs are made with the sources present).
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

from . import core, noise

REPO_URL = "https://github.com/amazingsammed/mini_textures"
COMMIT = "b080ca4df63813b3b764a8f31b3f418e3d6b6a2a"
SUBDIR = "mini_texture/textures"

# The twelve sets the add-on bundles from ambientCG; the rest were made by its author.
AMBIENTCG = {"Asphalt033", "Bricks105", "Concrete034", "Grass005", "Ground111", "Marble012", "Metal063",
             "PavingStones151", "Rock064", "Tiles141", "Wood095", "WoodFloor051"}

# Rough mean sRGB colour of each set we use, for the fallback when the clone is missing.
FALLBACK_TONE = {
    "Asphalt033": (82, 80, 76), "Bricks105": (170, 140, 100), "Concrete034": (184, 184, 180),
    "Grass005": (90, 130, 40), "Ground111": (130, 110, 95), "Marble012": (175, 178, 182),
    "Metal063": (110, 110, 108), "PavingStones151": (112, 108, 100), "Rock064": (105, 110, 80),
    "Tiles141": (205, 200, 195), "Wood095": (200, 150, 100), "WoodFloor051": (130, 100, 75),
    "metal_rust": (140, 120, 110), "gravel_ground": (110, 108, 104), "leather_brown": (130, 85, 55),
    "fabric_canvas": (180, 175, 160), "moss_ground": (70, 90, 50), "dirt_ground": (104, 85, 65),
    "cobblestone": (104, 104, 104), "rock_granite": (121, 121, 121), "metal_copper": (170, 100, 70),
    "wood_dark_planks": (52, 36, 25),
}


class Sources:
    def __init__(self, root: Path):
        self.dir = root / "intake" / "_sources" / "mini_textures" / SUBDIR
        self.available = self.dir.is_dir()
        self.used: list[dict] = []
        self._warned = set()
        self._cache = {}
        if not self.available:
            print(f"warning: {self.dir} missing (run tools/art/fetch_sources.sh); "
                  "photo-sourced textures fall back to procedural", file=sys.stderr)

    def begin(self):
        """Start recording provenance for one texture."""
        self.used = []

    def provenance(self) -> list[dict]:
        seen, out = set(), []
        for u in self.used:
            key = u.get("path") or u.get("font")
            if key not in seen:
                seen.add(key)
                out.append(u)
        return out

    def _file(self, name: str, kind: str) -> Path:
        return self.dir / f"{name}_{kind}.jpg"

    def has(self, name, kind):
        return self.available and self._file(name, kind).is_file()

    def _raw(self, name, kind) -> np.ndarray | None:
        key = (name, kind)
        if key in self._cache:
            return self._cache[key]
        p = self._file(name, kind)
        if not (self.available and p.is_file()):
            if key not in self._warned:
                self._warned.add(key)
                print(f"warning: source {p.name} unavailable, using procedural stand-in", file=sys.stderr)
            self._cache[key] = None
            return None
        im = Image.open(p)
        im = im.convert("RGB") if kind in ("diffuse", "normal") else im.convert("L")
        arr = np.asarray(im, dtype=np.float32) / 255.0
        self._cache[key] = arr
        return arr

    def _record(self, name, kind):
        self.used.append({
            "repo": REPO_URL,
            "commit": COMMIT,
            "path": f"{SUBDIR}/{name}_{kind}.jpg",
            "author": "ambientCG" if name in AMBIENTCG else "Mini Texture",
            "license": "CC0-1.0",
        })

    def load(self, name: str, kind: str, size=core.WORK, crop=None, repeat=(1, 1), linear_colour=True, rot90=0,
             shape=None):
        """One map at `size` x `size` (box filtered), tiling.

        crop     (x, y, w, h) in source pixels: a crop isn't periodic, so it gets the seam fix.
        repeat   (rx, ry): the (whole, seamless) source repeats this many times across the tile,
                 e.g. (1, 2) for a 1024x512 brick sheet filling a square tile.
        rot90    quarter turns (counter-clockwise), applied before the crop: grain along v.
        shape    (h, w) instead of a square `size`.
        Diffuse comes back linear RGB; data maps (ao, roughness, displacement, metallic) as
        0..1; normal as raw 0..1 RGB. None-safe: missing sources return a stand-in.
        """
        raw = self._raw(name, kind)
        oh, ow = shape if shape else (size, size)
        if raw is None:
            return self._fallback(name, kind, (oh, ow))
        self._record(name, kind)
        img = np.rot90(raw, rot90) if rot90 else raw
        cropped = crop is not None
        if cropped:
            x, y, w, h = crop
            img = img[y:y + h, x:x + w]
        rx, ry = repeat
        th, tw = oh // ry, ow // rx
        img = noise.box_resize(img, th, tw)
        if cropped:
            img = noise.make_tileable(img)
        if rx > 1 or ry > 1:
            reps = (ry, rx, 1) if img.ndim == 3 else (ry, rx)
            img = np.tile(img, reps)
        if kind == "diffuse" and linear_colour:
            img = core.srgb_to_lin(img)
        return img.astype(np.float32)

    def _fallback(self, name, kind, shape):
        rng = core.rng_for("fallback", name, kind)
        n = noise.fbm01(rng, shape, shape[1] / 10, octaves=5)
        if kind == "diffuse":
            tone = core.srgb_to_lin(np.array(FALLBACK_TONE.get(name, (110, 110, 110)), np.float32) / 255)
            return (tone[None, None, :] * (0.6 + 0.8 * n[..., None])).astype(np.float32)
        if kind == "normal":
            nm = noise.normals_from_height(n, 4.0)
            return (nm * 0.5 + 0.5).astype(np.float32)
        if kind == "ao":
            return (0.75 + 0.25 * n).astype(np.float32)
        if kind == "metallic":
            return np.zeros(shape, np.float32)
        return n  # roughness, displacement


def procedural_entry(what: str) -> dict:
    return {"generator": "tools/art/textures.py", "what": what, "author": "Dark Territory", "license": "CC0-1.0"}
