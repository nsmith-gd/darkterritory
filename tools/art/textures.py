#!/usr/bin/env python3
"""Dark Territory texture generator.

    python3 tools/art/textures.py                 # everything -> content/art/textures/
    python3 tools/art/textures.py --only brass,coal
    python3 tools/art/textures.py --sheet out/texture_sheet.png   # + contact sheets for review

Writes, per texture, <name>.png (RGBA8 sRGB diffuse; alpha = cutout/coverage) and <name>_s.png
(RGB8: R specular intensity, G gloss -> Phong exponent 4..128 in the engine, B emissive mask),
plus index.json with each texture's tiling size, family and source provenance.

The target is GDD §25-28: late-PS2 / early-PS3 industrial horror - rough, low-res, grainy,
lighting and grime painted in, one restrained palette. Deterministic: every random draw comes
from a generator seeded by the texture's name, so reruns are byte-identical (see --check).

Photo sources are CC0 sets fetched by tools/art/fetch_sources.sh into intake/_sources/ (see
intake/README.md for the intake rule). Without them the generator still runs, procedural-only.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import numpy as np  # noqa: E402

from texgen import convert, core, grade, noise, reg  # noqa: E402
from texgen.sources import Sources  # noqa: E402
# Importing a material module registers its textures (order = index.json order).
from texgen import mat_metal, mat_wood, mat_ground, mat_masonry, mat_foliage, mat_cloth  # noqa: E402,F401
from texgen import mat_corruption, mat_emissive, mat_paper, mat_sky, mat_fx  # noqa: E402,F401

ROOT = HERE.parents[1]
OUT = ROOT / "content" / "art" / "textures"


def build(entry: reg.Entry, src: Sources):
    src.begin()
    core.CAPTURED.clear()
    ctx = reg.Ctx(entry, src)
    tex = entry.fn(ctx)
    tex.normal = convert.normal_map(tex, core.CAPTURED)
    tex = grade.grade(tex)          # lift to the material's target mean albedo (texgen/grade.py)
    tex = convert.finish(tex, core.rng_for(entry.name, "finish"))
    diffuse, spec = convert.encode(tex, core.rng_for(entry.name, "encode"))
    return tex, diffuse, spec


def index_entry(tex: core.Tex, diffuse) -> dict:
    h, w = diffuse.shape[:2]
    e = {
        "name": tex.name,
        "diffuse": f"{tex.name}.png",
        "spec": f"{tex.name}_s.png",
        **({"normal": f"{tex.name}_n.png"} if tex.normal is not None else {}),
        "size": w if w == h else [w, h],
        "tileMetres": tex.tile_metres,
        "tiling": tex.tiling,
        "family": tex.family,
        "alphaTest": tex.alpha_test,
    }
    e.update(tex.extra)
    e["sources"] = tex.sources
    e["license"] = "CC0-1.0"
    return e


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", help="comma-separated texture names (others in index.json are kept)")
    ap.add_argument("--out", type=Path, default=OUT)
    ap.add_argument("--sheet", type=Path, help="also write contact sheets (PATH and PATH-<n>.png pages)")
    ap.add_argument("--check", action="store_true", help="print a hash of every output (for determinism diffs)")
    args = ap.parse_args()

    only = set(args.only.split(",")) if args.only else None
    names = [e.name for e in reg.REGISTRY]
    if only:
        unknown = only - set(names)
        if unknown:
            sys.exit(f"unknown texture(s): {', '.join(sorted(unknown))}")
    args.out.mkdir(parents=True, exist_ok=True)
    src = Sources(ROOT)

    index_path = args.out / "index.json"
    old = {}
    if only and index_path.exists():
        old = {e["name"]: e for e in json.loads(index_path.read_text())}

    entries, built = [], []
    for entry in reg.REGISTRY:
        if only and entry.name not in only:
            if entry.name in old:
                entries.append(old[entry.name])
            continue
        t0 = time.time()
        tex, diffuse, spec = build(entry, src)
        convert.save_png(diffuse, args.out / f"{entry.name}.png")
        convert.save_png(spec, args.out / f"{entry.name}_s.png")
        normal_path = args.out / f"{entry.name}_n.png"
        if tex.normal is not None:
            convert.save_png(convert.encode_normal(tex.normal), normal_path)
        elif normal_path.exists():
            normal_path.unlink()
        entries.append(index_entry(tex, diffuse))
        built.append((tex, diffuse, spec))
        rgb = diffuse[..., :3].astype(np.float32) / 255
        a = diffuse[..., 3] > 127
        luma = float((rgb[..., 0] * .2126 + rgb[..., 1] * .7152 + rgb[..., 2] * .0722)[a].mean()) if a.any() else 0
        seam = noise.seam_ratio(rgb) if tex.tiling else float("nan")
        print(f"{entry.name:18s} {diffuse.shape[1]:4d}x{diffuse.shape[0]:<4d} luma {luma:.3f} "
              f"seam {seam:4.2f} spec {spec[..., 0].mean() / 255:.2f} gloss {spec[..., 1].mean() / 255:.2f} "
              f"{time.time() - t0:5.1f}s")

    index_path.write_text(json.dumps(entries, indent=2) + "\n")
    total = sum(p.stat().st_size for p in args.out.glob("*") if p.is_file())
    shown = args.out.relative_to(ROOT) if args.out.is_relative_to(ROOT) else args.out
    print(f"{len(entries)} textures, {total / 1e6:.2f} MB in {shown}")

    if args.check:
        for p in sorted(args.out.glob("*")):
            print(hashlib.sha256(p.read_bytes()).hexdigest()[:16], p.name)

    if args.sheet:
        from texgen import sheet
        sheet.contact_sheets(args.out, entries, args.sheet)


if __name__ == "__main__":
    main()
