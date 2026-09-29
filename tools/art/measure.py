#!/usr/bin/env python3
"""Mean linear albedo of each diffuse (alpha > 0.5 texels, sRGB decoded with a 2.2 power - the
same measure the engine team uses), next to the grade target in texgen/grade.py.

    python3 tools/art/measure.py [content/art/textures]
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from texgen.grade import ALBEDO  # noqa: E402

d = Path(sys.argv[1]) if len(sys.argv) > 1 else HERE.parents[1] / "content" / "art" / "textures"
bad = 0
for e in json.loads((d / "index.json").read_text()):
    a = np.asarray(Image.open(d / e["diffuse"]).convert("RGBA"), np.float32) / 255
    m = a[..., 3] > 0.5
    lin = float((a[..., :3][m] ** 2.2).mean()) if m.any() else 0.0
    t = ALBEDO.get(e["name"])
    flag = ""
    if t is not None and abs(lin - t) > 0.25 * t:
        flag = "  <-- off target"
        bad += 1
    print(f"{e['name']:18s} {lin:.3f}  target {t if t is not None else '-':>5}{flag}")
sys.exit(1 if bad else 0)
