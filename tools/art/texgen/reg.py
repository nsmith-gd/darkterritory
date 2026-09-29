"""The texture registry: each material module decorates its builders with @texture, and
textures.py runs them in registration order. Names are the content contract (index.json)."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Callable

import numpy as np

from . import core, noise
from .sources import Sources, procedural_entry


@dataclass
class Entry:
    name: str
    family: str
    tile: float | None
    fn: Callable


REGISTRY: list[Entry] = []


def texture(name, family, tile=1.0):
    def deco(fn):
        REGISTRY.append(Entry(name, family, tile, fn))
        return fn
    return deco


class Ctx:
    """What a builder gets: the sources, a per-texture seeded RNG, and output helpers."""

    def __init__(self, entry: Entry, src: Sources):
        self.entry = entry
        self.src = src
        self.W = core.WORK
        self.notes: list[str] = []

    def rng(self, *keys) -> np.random.Generator:
        return core.rng_for(self.entry.name, *keys)

    def load(self, *a, **k):
        return self.src.load(*a, **k)

    def out(self, diffuse, spec, gloss, emissive=None, alpha=None, tiling=True, alpha_test=False,
            procedural="", **kw) -> core.Tex:
        """Box-filter work-res maps down to their final size and wrap them as a Tex."""
        # Built at WORK (2x) unless told otherwise; cards and atlases pass their own factor.
        factor = kw.pop("factor", None)
        if factor is None:
            factor = core.WORK // core.N if diffuse.shape[1] == core.WORK else 1
        dn = (lambda x: None if x is None else noise.box_down(x, factor)) if factor > 1 else (lambda x: x)
        # Tex knobs pass through; anything else is extra index.json data (e.g. atlas "cells").
        knobs = {k: kw.pop(k) for k in ("quant", "grain", "chroma_block", "dither") if k in kw}
        sources = self.src.provenance()
        if procedural:
            sources = sources + [procedural_entry(procedural)]
        return core.Tex(
            name=self.entry.name, family=self.entry.family,
            diffuse=dn(diffuse), spec=dn(spec), gloss=dn(gloss), emissive=dn(emissive), alpha=dn(alpha),
            tile_metres=self.entry.tile, tiling=tiling, alpha_test=alpha_test, sources=sources,
            extra=kw, **knobs)
