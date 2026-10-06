"""Diegetic paper and dials (GDD §24: the cab is read, not HUD'd). Text is rendered large and
box-filtered down so it lands crunchy but legible. Needles are NOT painted: the engine draws
them, so index.json gives each dial's centre, sweep and scale."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from . import core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture

FONTS = {
    "sans_bold": ["/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf",
                  "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"],
    "serif_bold": ["/usr/share/fonts/truetype/dejavu/DejaVuSerifCondensed-Bold.ttf",
                   "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"],
    "serif": ["/usr/share/fonts/truetype/dejavu/DejaVuSerifCondensed.ttf",
              "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf"],
    "mono": ["/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"],
    "hand": ["/usr/share/fonts/truetype/freefont/FreeSansOblique.ttf",
             "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf"],
}


def font(kind, size):
    """System fonts (DejaVu/FreeFont ship with every Linux image we build on). The fallback
    is Pillow's built-in font, so a machine without them still produces a texture."""
    for p in FONTS[kind]:
        if Path(p).exists():
            return ImageFont.truetype(p, size)
    return ImageFont.load_default(size=size)


def font_sources():
    return [{"font": Path(p).name, "license": "Bitstream Vera / DejaVu (free) or GNU FreeFont (GPL+font exception)",
             "note": "rendered glyphs only; no font file is redistributed"}
            for k in ("sans_bold", "serif_bold", "serif", "mono", "hand") for p in FONTS[k][:1] if Path(p).exists()]


def aged_paper(rng, H, W, base="#B4A684", dark="#6E6048"):
    """Aged paper: fibre noise, foxing spots, a darker, dirtier edge, a soot thumbprint."""
    fib = noise.fbm01(rng, (H, W), 1.5, octaves=3, stretch=(2.0, 0.6))
    blot = noise.fbm01(rng, (H, W), H / 8, octaves=5)
    d = lerp(hexc(dark), hexc(base), saturate(0.55 + 0.3 * blot + 0.15 * fib))
    ys, xs = noise.grid(H, W)[1], noise.grid(H, W)[0]
    edge = np.minimum(np.minimum(xs, W - 1 - xs), np.minimum(ys, H - 1 - ys)) / (min(H, W) * 0.12)
    d = d * (0.7 + 0.3 * saturate(edge + 0.4 * blot))[..., None]
    fox = smoothstep(0.93, 0.97, noise.fbm01(rng, (H, W), 3, octaves=2)) * blot
    d = lerp(d, hexc("#6A4A2A"), fox * 0.5)
    return d


def ink(d, mask, colour, amount=0.9):
    return lerp(d, hexc(colour), saturate(mask) * amount)


# --- gauges -----------------------------------------------------------------------------------

DIALS = [
    # name, label, numbered values, min, max, major tick step, minor tick step, redline, units.
    # Few, big numerals (every 100 psi, every 20 mph): legible when the dial is 64 px on screen.
    ("pressure", "PRESSURE", [0, 100, 200], 0, 250, 50, 10, 200, "PSI"),
    ("heat", "HEAT", [100, 300, 500], 100, 500, 100, 20, 450, "°F"),
    ("water", "TENDER", None, 0, 1, None, None, None, ""),
    ("speed", "SPEED", [0, 20, 40, 60], 0, 60, 10, 5, None, "MPH"),
]
SWEEP = (-120.0, 120.0)   # degrees clockwise from straight up


@texture("gauge_face", "paper", tile=None)
def gauge_face(ctx):
    """2x2 atlas of 128 px brass-bezel dials (built at 4x). Numbers big and few so they read at
    64 px on screen; aged cream faces with dirt and a hazed glass (spec)."""
    K = 4                      # supersample
    S = 128 * K
    A = 2 * S
    rng = ctx.rng("gauges")
    D = np.zeros((A, A, 3), np.float32)
    Sp = np.zeros((A, A), np.float32)
    G = np.zeros((A, A), np.float32)
    meta = []
    xs, ys = noise.grid(S, S)
    c = S / 2
    r = np.sqrt((xs - c) ** 2 + (ys - c) ** 2) / (S / 2)
    face_r = 0.80
    for i, (name, label, vals, vmin, vmax, major_step, minor_step, red, units) in enumerate(DIALS):
        cy, cx = divmod(i, 2)
        face = aged_paper(ctx.rng("face", name), S, S, base="#C4B894", dark="#7A6C50")
        # Dirt collects at the bottom of the face behind the glass; a soot haze on top.
        face = face * (1 - 0.25 * smoothstep(0.2, 0.8, (ys - c) / c * r))[..., None]
        im = Image.new("L", (S, S), 0)
        dr = ImageDraw.Draw(im)
        red_im = Image.new("L", (S, S), 0)
        rdr = ImageDraw.Draw(red_im)
        R = S / 2 * face_r
        if name == "water":
            # A gauge glass: a vertical tube in brass fittings, a painted scale beside it.
            tube = [c - 30 * K, c - R * 0.52, c - 12 * K, c + R * 0.6]
            dr.rectangle(tube, outline=255, width=3 * K)
            for k, lab in enumerate(["F", "3/4", "1/2", "1/4", "E"]):
                y = tube[1] + (tube[3] - tube[1]) * (k / 4)
                dr.line([tube[2] + 4 * K, y, tube[2] + 14 * K, y], fill=255, width=2 * K)
                f = font("sans_bold", 13 * K)
                dr.text((tube[2] + 17 * K, y - 8 * K), lab, fill=255, font=f)
            rdr.line([tube[0] - 14 * K, tube[3] - 4 * K, tube[0] - 4 * K, tube[3] - 4 * K], fill=255, width=3 * K)
            meta.append({"name": name, "kind": "glass", "cell": [cx * 128, cy * 128, 128, 128],
                         "tube": [round(v / K + (cx * 128, cy * 128)[j % 2], 1) for j, v in enumerate(tube)],
                         "min": 0, "max": 1})
        else:
            a0, a1 = np.radians(SWEEP[0]), np.radians(SWEEP[1])
            # Red band from the redline to the end of the scale.
            if red is not None:
                f0 = (red - vmin) / (vmax - vmin)
                # PIL angles: 0 = 3 o'clock, clockwise. Ours: 0 = 12 o'clock, clockwise.
                s0 = SWEEP[0] + f0 * (SWEEP[1] - SWEEP[0]) - 90
                rdr.arc([c - R * 0.93, c - R * 0.93, c + R * 0.93, c + R * 0.93], s0, SWEEP[1] - 90, fill=255,
                        width=int(9 * K))
            def ang(v):
                return a0 + (v - vmin) / (vmax - vmin) * (a1 - a0)
            # Ticks: minor every minor_step, major every major_step (longer, heavier).
            for v in np.arange(vmin, vmax + 1e-6, minor_step):
                a = ang(v)
                major = abs((v - vmin) / major_step - round((v - vmin) / major_step)) < 1e-6
                r0 = R * (0.72 if major else 0.84)
                x0, y0 = c + np.sin(a) * r0, c - np.cos(a) * r0
                x1, y1 = c + np.sin(a) * R * 0.95, c - np.cos(a) * R * 0.95
                dr.line([x0, y0, x1, y1], fill=255, width=int((3.5 if major else 1.8) * K))
            f = font("sans_bold", int(16 * K))
            for v in vals:
                a = ang(v)
                txt = str(v)
                tw = dr.textlength(txt, font=f)
                rr = R * 0.55
                x, y = c + np.sin(a) * rr, c - np.cos(a) * rr
                dr.text((x - tw / 2, y - 10 * K), txt, fill=255, font=f)
            meta.append({"name": name, "kind": "dial", "cell": [cx * 128, cy * 128, 128, 128],
                         "centre": [cx * 128 + 64, cy * 128 + 64], "sweepDegrees": list(SWEEP),
                         "min": vmin, "max": vmax, "redline": red, "units": units})
        # Dial name in the free bottom sector below the hub, units above the hub; the maker's mark
        # small at the bottom. The water glass puts its name above the tube.
        fl = font("serif_bold", int(10 * K))
        tw = dr.textlength(label, font=fl)
        dr.text((c - tw / 2, (c + R * 0.52) if name != "water" else (c - R * 0.8)), label, fill=255, font=fl)
        if units:
            fu = font("sans_bold", int(9 * K))
            tw = dr.textlength(units, font=fu)
            dr.text((c - tw / 2, c - R * 0.34), units, fill=255, font=fu)
        if name == "water":
            # Maker's mark (only where there's room: on the dials it would crowd the numerals).
            fm = font("serif", int(6 * K))
            mk = "D.T.R. SHOPS"
            tw = dr.textlength(mk, font=fm)
            dr.text((c - tw / 2, c + R * 0.66), mk, fill=255, font=fm)
        inkm = np.asarray(im, np.float32) / 255
        redm = np.asarray(red_im, np.float32) / 255
        d = ink(face, inkm, "#151210", 0.92)
        d = ink(d, redm, "#8A2A1A", 0.85)
        if name == "water":
            # The glass tube's inside: dark, with a meniscus band the engine can slide.
            tube_in = (xs > tube[0] + 3 * K) & (xs < tube[2] - 3 * K) & (ys > tube[1] + 3 * K) & (ys < tube[3] - 3 * K)
            d = lerp(d, hexc("#1A1C1C"), tube_in * 0.85)
        # Brass bezel ring + shadow it casts on the face.
        bez = smoothstep(face_r, face_r + 0.01, r) * smoothstep(1.0, 0.985, r)
        brass = core.apply_ramp(saturate(0.45 + 0.3 * noise.fbm01(ctx.rng("bz", name), (S, S), 10 * K)), "brass") * 0.42
        hl = np.exp(-((r - 0.86) / 0.025) ** 2) * saturate(-(ys - c) / c + 0.3)
        brass = brass * (1 + 1.2 * hl - 0.5 * np.exp(-((r - 0.96) / 0.02) ** 2))[..., None]
        shadow = smoothstep(face_r, face_r - 0.08, r) * smoothstep(face_r - 0.2, face_r, r)
        d = d * (1 - 0.45 * shadow)[..., None]
        d = lerp(d, brass, bez)
        d = lerp(d, hexc("#0C0C0C"), smoothstep(0.985, 1.0, r))
        # Glass haze: soot smudges over the face.
        haze = smoothstep(0.5, 0.8, noise.fbm01(ctx.rng("haze", name), (S, S), 12 * K)) * (r < face_r)
        d = lerp(d, d * 0.6 + hexc("#2A2620") * 0.2, haze * 0.5)
        s = lerp(np.where(r < face_r, 0.35, 0.0), 0.55, bez) * (1 - 0.5 * haze)
        g = lerp(np.where(r < face_r, 0.8, 0.0), 0.6, bez)
        D[cy * S:(cy + 1) * S, cx * S:(cx + 1) * S] = d
        Sp[cy * S:(cy + 1) * S, cx * S:(cx + 1) * S] = s
        G[cy * S:(cy + 1) * S, cx * S:(cx + 1) * S] = g
    ctx.src.used += font_sources()
    return ctx.out(D, Sp, G, tiling=False, factor=K, dials=meta, needlesPainted=False,
                   procedural="dial faces, ticks, numerals, brass bezels, glass haze", grain=0.03, quant=64)


# --- the train order ---------------------------------------------------------------------------

@texture("paper_form", "paper", tile=None)
def paper_form(ctx):
    """512x512 aged railway form: FORM 19 - TRAIN ORDER, typed, with pencil notes. Built at 2x."""
    K = 2
    H = W = 512 * K
    rng = ctx.rng("form")
    d = aged_paper(rng, H, W)
    im = Image.new("L", (W, H), 0)
    dr = ImageDraw.Draw(im)
    pr = Image.new("L", (W, H), 0)          # printed rules and headings (form stock)
    pdr = ImageDraw.Draw(pr)
    pen = Image.new("L", (W, H), 0)         # pencil
    pdn = ImageDraw.Draw(pen)
    m = 34 * K

    def centred(drw, y, txt, f):
        tw = drw.textlength(txt, font=f)
        drw.text(((W - tw) / 2, y), txt, fill=255, font=f)

    centred(pdr, 22 * K, "DARK TERRITORY RAILWAY COMPANY", font("serif_bold", 17 * K))
    centred(pdr, 46 * K, "FORM 19  —  TRAIN ORDER", font("serif_bold", 27 * K))
    pdr.line([m, 82 * K, W - m, 82 * K], fill=255, width=2 * K)
    pdr.line([m, 86 * K, W - m, 86 * K], fill=255, width=1 * K)
    fs = font("serif", 12 * K)
    pdr.text((m, 100 * K), "Train Order No.", fill=255, font=fs)
    pdr.text((W / 2 + 20 * K, 100 * K), "Date", fill=255, font=fs)
    pdr.text((m, 128 * K), "To", fill=255, font=fs)
    pdr.text((W / 2 + 20 * K, 128 * K), "At", fill=255, font=fs)
    for y, x0, x1 in [(116, m + 110 * K, W / 2), (116, W / 2 + 60 * K, W - m), (144, m + 30 * K, W / 2),
                      (144, W / 2 + 45 * K, W - m)]:
        pdr.line([x0, y * K, x1, y * K], fill=255, width=K)
    # Ruled body lines.
    for k in range(10):
        y = (178 + k * 24) * K
        pdr.line([m, y, W - m, y], fill=160, width=K)
    pdr.line([m, 430 * K, W - m, 430 * K], fill=255, width=K)
    pdr.text((m, 438 * K), "Made", fill=255, font=fs)
    pdr.text((m + 130 * K, 438 * K), "Time", fill=255, font=fs)
    pdr.text((m + 270 * K, 438 * K), "Dispr.", fill=255, font=fs)
    for x0, x1 in [(m + 40 * K, m + 120 * K), (m + 170 * K, m + 260 * K), (m + 320 * K, W - m)]:
        pdr.line([x0, 454 * K, x1, 454 * K], fill=255, width=K)
    fsm = font("serif", 9 * K)
    centred(pdr, 474 * K, "EACH OPERATOR MUST REPEAT THIS ORDER BACK. NO TRAIN MAY PROCEED ON A ", fsm)
    centred(pdr, 486 * K, "LAPSED ORDER. MEN ON THE GROUND AFTER DARK DO SO AT THEIR OWN RISK.", fsm)

    # Typed entries: typewriter mono, slightly uneven baseline and strike.
    ft = font("mono", 14 * K)

    def typed(x, y, txt):
        cx = x
        for ch in txt:
            j = rng.normal(0, 0.6 * K)
            v = int(np.clip(rng.normal(215, 30), 120, 255))
            dr.text((cx, y + j), ch, fill=v, font=ft)
            cx += dr.textlength("M", font=ft)

    typed(m + 118 * K, 99 * K, "47")
    typed(W / 2 + 64 * K, 99 * K, "NOV 3")
    typed(m + 36 * K, 127 * K, "C&E ENG 2417")
    typed(W / 2 + 50 * K, 127 * K, "WEST FORK")
    body = [
        "ENG 2417 RUN EXTRA WEST FORK TO",
        "CINDER GAP. DO NOT EXCEED FIFTEEN",
        "15 MPH OVER GAUNT TRESTLE MP 31.",
        "NO LAMPS LIT BETWEEN MP 31 AND",
        "MP 38. HOLD MAIN AT HOLLOWAY TANK",
        "FOR WATER. SIDING SWITCH AT",
        "HOLLOWAY REPORTED FOULED. CREW",
        "TO CLEAR BY HAND. DO NOT LEAVE",
        "MEN BEHIND.",
    ]
    for k, line in enumerate(body):
        typed(m + 6 * K, (160 + k * 24) * K, line)
    typed(m + 42 * K, 436 * K, "OK")
    typed(m + 176 * K, 436 * K, "3:14 AM")
    typed(m + 330 * K, 436 * K, "R.H.")
    # Pencil: a hurried note and a scribble, a circled mile post.
    fh = font("hand", 17 * K)
    pdn.text((W * 0.50, 395 * K), "lamps OUT past 31 !!", fill=230, font=fh)
    pdn.text((W * 0.08, 400 * K), "2 men short", fill=210, font=fh)
    pdn.ellipse([m + 170 * K, 205 * K, m + 250 * K, 232 * K], outline=220, width=2 * K)
    sig = draw.wander_line(rng, W * 0.62, 470 * K - 30 * K, -0.1, 160 * K, 30, 0.9)
    pdn.line(sig, fill=200, width=2 * K, joint="curve")
    # Composite: print ink brownish-black, type black-violet (ribbon), pencil graphite grey.
    d = ink(d, np.asarray(pr, np.float32) / 255, "#2A2018", 0.85)
    tm = np.asarray(im, np.float32) / 255
    tm = tm * (0.75 + 0.25 * noise.fbm01(ctx.rng("ribbon"), (H, W), 3))
    d = ink(d, tm, "#141018", 0.95)
    d = ink(d, np.asarray(pen, np.float32) / 255, "#4A4A4C", 0.7)
    # Fold creases (in thirds) and handling: soot thumbprint, a ring stain, a dog-ear.
    xs, ys = noise.grid(H, W)
    for fy in (H / 3, 2 * H / 3):
        cr = np.exp(-((ys - fy - 3 * np.sin(xs / 90)) / 2.5) ** 2)
        d = d * (1 - 0.25 * cr)[..., None] * (1 + 0.1 * np.exp(-((ys - fy - 5) / 3) ** 2))[..., None]
    thumb = draw.ellipses((H, W), [(W * 0.86, H * 0.8, 36 * K, 48 * K, 1.0)], wrap=False)
    thumb = noise.blur(thumb, 6) * noise.fbm01(ctx.rng("tp"), (H, W), 3)
    d = lerp(d, d * 0.45, thumb * 0.8)
    rr = np.sqrt((xs - W * 0.2) ** 2 + (ys - H * 0.3) ** 2)
    ring = np.exp(-((rr - 70 * K) / (4 * K)) ** 2) * smoothstep(0.3, 0.6, noise.fbm01(ctx.rng("ring"), (H, W), 20))
    d = lerp(d, hexc("#6A4A2A"), ring * 0.35)
    dog = (xs + ys) > (W + H - 70 * K)
    d = lerp(d, hexc("#5A4E3A"), dog * 1.0)
    d = d * (1 - 0.4 * np.exp(-(((xs + ys) - (W + H - 70 * K)) / (3 * K)) ** 2))[..., None]
    d = d * 0.85
    s = np.full((H, W), 0.04, np.float32)
    g = np.full((H, W), 0.1, np.float32)
    ctx.src.used += font_sources()
    return ctx.out(d, s, g, tiling=False, factor=K, procedural="form stock, typed order, pencil, creases, stains",
                   grain=0.035, quant=64)


# --- car numbers ------------------------------------------------------------------------------

@texture("stencil_numerals", "paper", tile=None)
def stencil_numerals(ctx):
    """The cars' stencilled numbers (GDD §32: something players can say out loud, "car four"): the digits 0-9 in a
    5 x 2 atlas (each cell 0.2 x 0.5 of it, digit k at column k % 5, row k // 5), stencil-cut white paint, the bridges
    left in the counters, chipped and dirtied, the rest cut away (alpha test) so a digit lies on any car's side."""
    K = 2
    H, W = 512 * K, 1024 * K
    cw, ch = W // 5, H // 2
    mask = Image.new("L", (W, H), 0)
    dr = ImageDraw.Draw(mask)
    f = font("sans_bold", int(ch * 0.86))
    for k in range(10):
        cx, cy = (k % 5) * cw + cw / 2, (k // 5) * ch + ch / 2
        box = dr.textbbox((0, 0), str(k), font=f)
        dr.text((cx - (box[0] + box[2]) / 2, cy - (box[1] + box[3]) / 2), str(k), fill=255, font=f)
    m = np.asarray(mask, np.float32) / 255
    xs, ys = noise.grid(H, W)
    # The stencil's bridges: a narrow band across each digit at a third and two thirds of its height, where the
    # cut sheet held its counters (so a 0 or an 8 is two pieces of paint, as painted through a plate).
    yc = (ys % ch) / ch
    for b in (0.36, 0.64):
        m = m * (1 - np.exp(-((yc - b) / 0.012) ** 6))
    # Overspray soft round the edges, then worn: chips where the paint's gone, thin where it was dabbed.
    wear = noise.fbm01(ctx.rng("wear"), (H, W), 6 * K, octaves=4)
    chips = smoothstep(0.72, 0.8, noise.fbm01(ctx.rng("chip"), (H, W), 3 * K, octaves=3))
    m = noise.blur(m, 1.2 * K)
    alpha = saturate((m - 0.5) * 6 + 0.5) * (1 - chips)
    alpha = saturate((alpha - 0.25 * wear) * 2.2)
    paint = core.apply_ramp(saturate(0.55 + 0.35 * wear), ["#6E6A60", "#9C968A", "#BDB6A6", "#CFC8B6"])
    # Soot and rust run down off the top of each stroke.
    runs = noise.fbm01(ctx.rng("runs"), (H, W), 2 * K, octaves=3, stretch=(0.15, 4))
    d = lerp(paint, paint * 0.55 + hexc("#4A3424") * 0.45, smoothstep(0.6, 0.8, runs) * 0.6)
    a = alpha[..., None]
    bleed = noise.blur(d * a, 6) / np.maximum(noise.blur(alpha, 6)[..., None], 1e-3)
    d = d * a + bleed * (1 - a)
    s = np.full((H, W), 0.06, np.float32)
    g = np.full((H, W), 0.2, np.float32)
    ctx.src.used += font_sources()
    return ctx.out(d, s, g, alpha=alpha, tiling=False, alpha_test=True, factor=K,
                   procedural="stencilled digits (DejaVu Sans Bold), bridges, overspray, chips, soot runs")
