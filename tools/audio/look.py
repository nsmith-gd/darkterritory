#!/usr/bin/env python3
"""Look at a sound: waveform over a log-frequency spectrogram, with a tell band marked. For checking builds by eye.

  python3 tools/audio/look.py out/audio/takes/tell-choir/voices/round_00.wav [--band 300 4000] [--out x.png]
"""

import os
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from scipy import signal  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dsp  # noqa: E402


def look(paths, out, band=None, title=None):
    n = len(paths)
    fig, axes = plt.subplots(n * 2, 1, figsize=(12, 3.2 * n), gridspec_kw={"height_ratios": [1, 3] * n})
    for k, p in enumerate(paths):
        x = dsp.load(p)
        t = np.arange(len(x)) / dsp.SR
        a = axes[2 * k]
        a.plot(t, x, lw=0.4, color="k")
        a.set_xlim(0, t[-1] if len(t) else 1)
        a.set_ylim(-1, 1)
        a.set_title(f"{os.path.basename(p)}  {len(x) / dsp.SR:.2f}s  {dsp.loudness(x):.1f} LUFS  centre {dsp.centroid(x):.0f} Hz"
                    + (f"  {dsp.band_fraction(x, *band):.0%} in band" if band else ""), fontsize=9)
        a.set_xticks([])
        f, tt, S = signal.spectrogram(x, dsp.SR, nperseg=2048, noverlap=1792)
        S = 10 * np.log10(S + 1e-12)
        b = axes[2 * k + 1]
        b.pcolormesh(tt, f[1:], S[1:], shading="auto", vmin=S.max() - 80, vmax=S.max(), cmap="magma")
        b.set_yscale("log")
        b.set_ylim(40, 20000)
        if band:
            for y in band:
                b.axhline(y, color="c", lw=0.8, ls="--")
        b.set_ylabel("Hz")
    if title:
        fig.suptitle(title)
    fig.tight_layout()
    fig.savefig(out, dpi=80)
    plt.close(fig)
    return out


if __name__ == "__main__":
    args = sys.argv[1:]
    band = None
    out = "out/audio/look.png"
    if "--band" in args:
        i = args.index("--band")
        band = (float(args[i + 1]), float(args[i + 2]))
        del args[i:i + 3]
    if "--out" in args:
        i = args.index("--out")
        out = args[i + 1]
        del args[i:i + 2]
    print(look(args, out, band))
