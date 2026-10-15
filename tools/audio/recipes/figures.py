"""The figures heard (queue #503, note 773; P1's notes 570, 571, 572 and 489; asked of AU1 in #569): Dave at his easel
past a stop, Nicki's party in the one house up late, Jacob fishing at the water's edge (docs/design/gdd.md §3.2, "A
very few people live outside the walls"; content/world/figures.json).

What they are decides the build. They're people, not creatures, in a dark, dead world: warm, small and human, and none
of it may sound like a creature or a tell. So:
- Dave, the wandering painter, at his easel on open ground away from the stop: a brush on canvas (soft bristle strokes,
  dabs, the brush worked in the paint and tapped on the palette now and then) and his humming, low, tuneless and
  unhurried, a man alone and content. The hum is no tune anyone knows: its notes are wherever his voice wanders, never
  on a scale, a few at a time with a breath between. Held to the mid band (150 Hz-5 kHz). Struck, he lets out a breath
  of a laugh through his nose, warm and amused, nothing hurt; at the fourth blow he sets his brush down on the easel's
  ledge and scrapes his stool round on the ground to face you; at the fifth his hand takes a collar: cloth wrenched
  tight in a fist and a sharp breath in, short. Nothing heavy, nothing wet, no bone: his kill is the spine's, and the
  sound is a hand.
- Nicki's party, heard from the street through a house's walls in a hushed, masked town: the only house up late. Music
  kept low (a jig of our own in 6/8, sixteen bars round, folk-like and nobody's tune), people talking and laughing under
  it with their masks down (no words), glasses touched, feet on the boards. Warm (200 Hz-6 kHz).
- Jacob, the fisherman, at a lake's edge: the water lapping at the stones, his line cast now and then (the rod's swish,
  the reel's clicker ticking as the line pays out, the weight's plop far out), a quiet, then the reel wound in slowly,
  click by click, and a quiet again. Calm. His blessing as the whole train's made new, once a car down the train,
  staggered: a soft, rising shimmer (bowed glass, chimes, a breath of warm air), kind and a little magical, never an
  alarm; its attack soft, all in D major, so the cars' overlapping takes ring as one chord.

Each cue has two candidates. The first is kitbashed from the packs' real recordings: a real throat (the packs' roar, its
steady middle cut into single pulses and laid back down one per period of a hum, through closed lips) and a real gasp's
air for the breath; real dry textures (a straw-like rustle, a belt's cloth crackling, pages sliding) cut into grains for
the bristles and the canvas; real wood, stones, creaks, cloth and leather; a real held reedy tone played as an accordion
(the packs have no instruments); real glass, real boots on boards, real water, real air swept into the rod's swish, real
ratchets and a real ticking mechanism cut into a reel's clicks; real struck glass held out into bowed glass. The second
is modelled: a glottis through a tract, stick-slip friction, struck modes, a bowed string through a violin's body, free
reeds, bubbles. The packs have no voices: the party's talk and laughter are modelled in both. Loops are built periodic
(events placed round the cycle and their tails folded back, the room and the walls circular), so build.py's seam gives
the cycle back exactly. Every one-shot keeps its weight out of the squish under 150 Hz (the director's rule).
"""

import functools

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import kit
from recipes import town
from recipes import water as WA
from recipes import world_kit as W
from recipes.beasts import unit
from recipes.boarders import grain
from recipes.crew_feet import BOOTS            # real boots: heard_foley.stamp, step and tap cut them into heels and toes
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe, R, S, K
from recipes.g1_ground import GRAVEL, gravel_grains, wander
from recipes.heard_foley import stamp, step, tap
from recipes.house_folk import core, drawn
from recipes.warnings import tooth, loop_of as tile

SR = dsp.SR
LINE = "figures"

# ---- Sources ---------------------------------------------------------------------------------------------------------

ROAR = "sfx_100_v2:misc_24"                      # a real throat, voiced round 200 Hz: cut into single pulses for a voice
GASP = "sfx_100_v2:misc_05"                      # a real gasp: air through a mouth (breath through the nose, an intake)
BRISTLE = "sfx_100_v2:misc_02"                   # a real dry, straw-like rustle: a brush's bristles
FIBRES = R("clothBelt2")[0]                      # a real belt's cloth crackling: fibres (the canvas's weave)
PAGES = R("bookFlip2", "bookFlip3")              # real pages sliding over each other: a dry sheet brushed
TEXTURE = [BRISTLE, FIBRES] + PAGES
FLAT = R("bookPlace1")[0]                        # a real book set down flat: the soft 'tup' of a stretched canvas
CHOP = "kenney_rpg-audio:chop"                   # a real blade chopped into a board: a dry, woody tick
WOOD_L = K("impactWood_light")                   # light wood knocked: a palette, an easel's ledge, a stool's leg
CREAKS = R("creak1", "creak3")                   # real small wooden creaks: a stool's joints
STONES = S("stones_03", "stones_01")             # real stones scraping and crackling: legs dragged over grit
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4")
DROP_LEATHER = R("dropLeather")[0]               # a real heavy leather drop: a hand landing on a coat
SOFT = K("impactSoft_medium")                    # real soft blows: a hand on a body (only their slap kept)
REED = "sfx_100_v2:misc_12"                      # a real held, reedy tone round 683 Hz, a second voice beating sharp
CHINA = S("glass_01", "glass_04", "glass_06") + K("impactGlass_light")   # real glasses knocked
WATER = S("loop_water_01", "loop_water_02")      # real water: a mass of it moving; pouring and trickling
AIR = S("air_03", "air_01")                      # real air: a short burst of it; a long, even hiss
TICKER = "sfx_100_v2:misc_28"                    # a real mechanism ticking fast and even, sixty a second: a reel's clicker
TEETH = S("misc_20", "lock_open_01")             # real ratchets and a lock's pawl, cut into single teeth
RINGING = S("glass_05", "glass_02")              # real struck glass that rings on

# ---- Bands, lengths and bodies ---------------------------------------------------------------------------------------

MID = (150, 5000)           # Dave's easel: the mid band
WARM = (200, 6000)          # the party through its walls
PAINT_L = 10.0              # Dave's cycle (s)
PARTY_L = 16.0              # the party's: sixteen bars of 6/8 at 120 to the dotted crotchet, the tune round once
FISH_L = 12.0               # Jacob's: one cast and the line wound in

REED_HZ = 683.0             # the real reed's pitch (Hz)
CANVAS = [190, 300, 430, 610]               # a stretched canvas's low modes: a soft drum, damped by the paint
PALETTE = [780, 1450, 2350, 3600]           # a thin wooden palette's (or a ledge's) modes, knocked at its edge
STOOL = [520, 980, 1650, 2500]              # a small wooden stool's joints
GOBLET = [1.0, 2.32, 4.25, 6.63]            # a wine glass's modes over its first
VIOLIN = [275, 460, 540, 700, 1100, 2500, 3100]   # a violin's body: the air and wood modes, the bridge's hill
TINE = [1.0, 2.76, 5.40, 8.93]              # a chime's tube or tine (free bar) over its first


# ---- Shared parts ----------------------------------------------------------------------------------------------------

def at(x, lufs):
    """Set a part's loudness, so a mix is balanced by how loud each thing is rather than by its peaks."""
    x = np.asarray(x, np.float32)
    l = dsp.loudness(x)
    return dsp.gain(x, lufs - l) if l > -70 else x


def loop(y, n, rng, wet=0.08, space="night", shape=None):
    """A cycle of n samples built from events placed round it: its room as a circular convolution (the tail rings on into
    the head), an EQ as a circular filter, then handed to build.py so its 0.3 s seam gives exactly that cycle back."""
    c = W.croom(dsp.wrap(y, n), space, wet, rng)
    if shape is not None:
        c = W.cfilter(c, shape)
    return W.seamless(c)


def band_shape(lo, hi, order=2):
    """A band's response (Butterworth magnitudes), for W.cfilter: a loop held to its band without a filter's start-up."""
    return lambda f: 1 / np.sqrt(1 + (lo / f) ** (2 * order)) / np.sqrt(1 + (f / hi) ** (2 * order))


def once(y, rng, wet=0.08, tail=0.25, lo=150):
    """A one-shot out on open ground: a little of the night's reverb, the tail cut short, nothing under `lo` Hz."""
    y = dsp.trim_silence(y, -60)
    z = dsp.room(y, "night", wet=wet, rng=rng)[:len(y) + samples(tail)]
    return dsp.fade(hp(z, lo, 2), 0.0, tail)


def round_on(x, n, xf=0.4):
    """A sound that runs on (talk, a murmur) made into an exact n-sample cycle: what it says past the end crossfades into
    its head (equal power: the two are unrelated), so the cycle's end runs straight on into its start."""
    k = samples(xf)
    x = dsp.fit(np.asarray(x, np.float32), n + k)
    y = x[:n].copy()
    t = np.linspace(0, np.pi / 2, k)
    y[:k] = x[:k] * np.sin(t) + x[n:n + k] * np.cos(t)
    return y


def shut(x):
    """Through closed lips: a hum's 'mm'. Most of it the nose's murmur round 260 Hz, the mouth's resonances above it damped
    (a lowpass at 1.3 kHz) and a dip near 1 kHz where the closed mouth swallows it (the nasal antiformant)."""
    y = dsp.peak(lp(x, 1300, 2), 260, 1.2, 5)
    return hp(dsp.peak(y, 1050, 3, -8), 90, 2)


# ---- A real throat, cut into pulses ----------------------------------------------------------------------------------

@functools.lru_cache(maxsize=1)
def _pulses():
    """The packs' real throat (misc_24, voiced round 200 Hz) cut into its single pulses: its steadiest fifth of a second,
    each period found from the last (the lowpassed wave's peak a period on), cut two periods wide under a Hann window and
    aligned to one another. Returns the aligned pulses and the period (samples)."""
    x = core(ROAR, 0.18)
    s = x - np.mean(x)
    r = np.correlate(s, s, "full")[len(s) - 1:]
    lo_, hi_ = int(SR / 260), int(SR / 150)
    P = lo_ + int(np.argmax(r[lo_:hi_]))
    g = lp(x, 600)
    marks, a = [], int(np.argmax(g[:P]))
    while a + P + P // 4 < len(x):
        marks.append(a)
        w0 = a + P - P // 4
        a = w0 + int(np.argmax(g[w0:a + P + P // 4]))
    win = np.hanning(2 * P).astype(np.float32)
    cuts = [x[m - P:m + P] * win for m in marks if m - P >= 0 and m + P <= len(x)]
    ref = cuts[len(cuts) // 2]
    out = []
    for c in cuts:
        k = int(np.argmax(np.correlate(c, ref, "full"))) - (len(c) - 1)
        out.append(np.roll(c, -k))
    return np.array(out, np.float32), P


def pulse_voice(rng, f, amp=None):
    """A voice from the real throat's own pulses (pitch-synchronous, the way a voice is re-pitched): one laid down per
    period of `f` (Hz per sample), each the average of eight of the real ones (so the roar's roughness goes and its throat
    stays), with a real voice's small unevenness in period and level. `amp` a per-sample level."""
    pulses, P = _pulses()
    n = len(f)
    bank = [np.mean(pulses[rng.choice(len(pulses), min(8, len(pulses)), replace=False)], axis=0) * np.hanning(2 * P)
            for _ in range(6)]
    out = np.zeros(n + 2 * P, np.float32)
    t = float(rng.uniform(0, 30))
    while True:
        a = int(t)
        if a >= n:
            break
        g = 1.0 if amp is None else float(amp[a])
        if g > 1e-4:
            out[a:a + 2 * P] += bank[int(rng.integers(len(bank)))] * g * rng.uniform(0.93, 1.07)
        t += SR / max(float(f[a]), 40.0) * (1 + rng.normal(0, 0.003))
    return out[:n]


def glottal_voice(rng, f, amp=None, rough=0.0):
    """A voice, modelled: a glottis at `f` (Hz per sample), a man's small unevenness in it, through a man's tract on 'm'."""
    L = len(f) / SR
    g = synth.glottis(f, L, rng, jitter=0.006, shimmer=0.06, oq=0.7, rough=rough)
    v = synth.tract(g, "m", "man", breath=0.12, rng=rng)
    return v if amp is None else v * amp[:len(v)]


def nose_air(rng, L, real=True):
    """Breath through the nose: the real gasp drawn out and darkened, or plain air coloured by a closed mouth."""
    if real:
        x = drawn(rng, GASP, L, rng.uniform(-4, -2), 0.05)
    else:
        x = mix(unit(lp(hp(synth.noise(L, rng, "pink"), 300, 1), 3000, 2)) * 0.7,
                unit(synth.breath(L, [(0, "m"), (L, "h")], "man", rng)) * 0.4)
    return unit(lp(dsp.peak(bp(x, 300, 3000), 600, 1.5, 4), 2800, 2))


# ---- Dave's hum ------------------------------------------------------------------------------------------------------

def hum_line(rng, L, base, lead=0.3, tail=0.5):
    """Where his hum goes over L s: a few phrases of two to five notes with a breath between them. Each note is wherever his
    voice wanders to from the last, a semitone or three up or down and never on a scale (so it's no tune: a murmur), with a
    slide between and a slow drift through it, each phrase sagging at its end. Returns the pitch (Hz per sample), the level
    (per sample) and the phrases [(start, length)]."""
    n = samples(L)
    phrases, t = [], lead
    while True:
        P = rng.uniform(1.5, 2.8)
        if t + P > L - tail:
            break
        phrases.append((t, P))
        t += P + rng.uniform(0.8, 1.6)
    tgt = np.zeros(n, np.float32)
    lvl = np.zeros(n, np.float32)
    s = rng.uniform(-1.5, 1.0)
    for a, P in phrases:
        u = a
        while u < a + P - 0.12:
            d = min(rng.uniform(0.3, 0.8), a + P - u)
            i0, i1 = samples(u), samples(u + d)
            tgt[i0:i1] = s
            dip = np.ones(i1 - i0, np.float32)
            k = min(samples(0.05), len(dip) // 3)
            if k:
                dip[:k] = 0.75 + 0.25 * np.linspace(0, 1, k)
            lvl[i0:i1] = dip * rng.uniform(0.8, 1.0)
            s = float(np.clip(s + rng.uniform(0.7, 3.0) * rng.choice([-1, 1]), -4.5, 3.0))
            u += d
        i0, i1 = samples(a), samples(a + P)
        tgt[i1:] = tgt[i1 - 1]
        sag = np.clip((np.arange(i1 - i0) / SR - P * 0.7) / (P * 0.3), 0, 1) * rng.uniform(0.8, 1.6)
        tgt[i0:i1] -= sag.astype(np.float32)
        lvl[i0:i1] *= dsp.fit(env([(0, 0), (0.12, 1), (P * 0.7, 0.85), (P - 0.05, 0.3), (P, 0)], P), i1 - i0)
    first = samples(phrases[0][0]) if phrases else 0
    tgt[:first] = tgt[first]
    k = 1 - np.exp(-1 / (0.06 * SR))             # a slide between notes
    semis = signal.lfilter([k], [1, -(1 - k)], tgt, zi=[tgt[0] * (1 - k)])[0]
    semis = semis + wander(rng, n, 0.7, 0.18)
    return (base * 2 ** (semis / 12)).astype(np.float32), lvl, phrases


def humming(rng, L, voice, real=True):
    """His hum round a cycle of L s, through closed lips, a soft breath in through the nose before each phrase (the first
    one's coming round from the end of the cycle)."""
    f, lvl, phrases = hum_line(rng, L, rng.uniform(108, 122))
    v = shut(voice(rng, f, lvl))
    ev = [(0.0, unit(v), 1.0)]
    for a, _ in phrases:
        d = rng.uniform(0.3, 0.42)
        br = nose_air(rng, d, real) * env([(0, 0), (d * 0.7, 1), (d, 0)], d)
        ev.append(((a - d - 0.03) % L, unit(br), dsp.db2a(-22)))
    return W.place(samples(L), ev)


# ---- Dave at his easel: the brush (150 Hz-5 kHz) ---------------------------------------------------------------------

def bristles(rng, L, density=80, semis=(-4, 0)):
    """A brush's bristles drawn over a dry weave, from real textures: grains of 20-45 ms cut from anywhere in the packs'
    straw-like rustle, a belt's cloth crackling and pages sliding, each pitched a little down and evened, overlapped
    `density` a second."""
    n = samples(L)
    out = np.zeros(n + samples(0.06), np.float32)
    t = 0.0
    while t < L:
        key = TEXTURE[int(rng.integers(len(TEXTURE)))]
        g = grain(rng, key, rng.uniform(0.02, 0.045), rng.uniform(*semis), attack=False)
        r = np.sqrt(np.mean(g ** 2))
        if r > 1e-6:
            a = samples(t)
            out[a:a + len(g)] += g / r * rng.uniform(0.5, 1.0)
        t += rng.exponential(1 / density)
    return out[:n]


def tup(rng, real=True):
    """The brush landing on the stretched canvas: a soft 'tup', the canvas a damped drum (a real book set down flat,
    pitched up and choked; or its low modes knocked)."""
    if real:
        x = ck.choke(dsp.vari(hit_of(FLAT, 0, 0.08), rng.uniform(3, 6)), 0.01, 0.015)
        return unit(lp(hp(x, 180, 2), 2500))
    return unit(lp(ck.hollow(rng, CANVAS, 0.07, q=3, hit=0.002), 2500))


def stroke_env(L, press=1.0):
    """A stroke: the brush lands, is drawn (fullest a third of the way, easing as the paint runs thin) and lifts away."""
    return env([(0, 0), (0.02, press), (L * 0.35, press), (L * 0.85, press * 0.7), (L, 0)], L)


def stroke_rec(rng, L):
    """One stroke from real textures: the bristles (real grains) held to a soft bristle's band, drawn and lifted, the
    canvas's tup as it lands."""
    x = bp(bristles(rng, L, rng.uniform(60, 100)), 900, 6500) * stroke_env(L, rng.uniform(0.8, 1.0))
    return ck.place([(0, unit(x), 0), (0, tup(rng), -12)])


def stroke_model(rng, L):
    """One stroke, modelled: the bristles catching and slipping over the weave (stick-slip, quick and fine), a dry rustle
    of their tips over it, the canvas's tup as it lands."""
    sh = stroke_env(L, rng.uniform(0.8, 1.0))
    rub = ck.friction(rng, L, rng.uniform(500, 800), 1000, 6500, 0.9, sh)
    tips = synth.rustle(L, 500 * sh + 30, rng, f=(2000, 7000), ticks=0.0) * sh
    return ck.place([(0, unit(mix(unit(rub), unit(tips) * 0.6)), 0), (0, tup(rng, False), -12)])


def dab_rec(rng, L):
    """A dab: the brush pressed onto the canvas and lifted, bristles splaying (real grains), the canvas's tup."""
    x = bp(bristles(rng, L, 120), 900, 6000) * env([(0, 0), (0.008, 1), (L, 0)], L)
    return ck.place([(0, unit(x), 0), (0, tup(rng), -6)])


def dab_model(rng, L):
    """A dab, modelled: a short press of stick-slip and rustle, the canvas's modes knocked softly."""
    sh = env([(0, 0), (0.008, 1), (L, 0)], L)
    rub = ck.friction(rng, L, 650, 1000, 6000, 0.9, sh)
    tips = synth.rustle(L, 700 * sh + 30, rng, f=(2000, 7000), ticks=0.0) * sh
    return ck.place([(0, unit(mix(unit(rub), unit(tips) * 0.6)), 0), (0, tup(rng, False), -6)])


def load_rec(rng, L):
    """The brush worked round in the paint on the palette: two slow turns of the real grains, muffled by the wet paint."""
    sh = env([(0, 0), (L * 0.2, 1), (L * 0.45, 0.4), (L * 0.7, 1), (L, 0)], L)
    return unit(lp(bp(bristles(rng, L, 70, (-6, -2)), 600, 5000), 3200) * sh)


def load_model(rng, L):
    """The brush worked in the paint, modelled: slow, soft stick-slip in two turns, muffled."""
    sh = env([(0, 0), (L * 0.2, 1), (L * 0.45, 0.4), (L * 0.7, 1), (L, 0)], L)
    return unit(lp(ck.friction(rng, L, 300, 600, 5000, 0.9, sh), 3000))


def palette_rec(rng, k, n=2):
    """The brush tapped on the palette's edge to shake it off: its wooden handle knocking the thin board (a real light
    wood knock pitched up an octave, choked), a dry woody tick on each (a real blade's chop), the second tap softer."""
    parts = []
    t = 0.0
    for i in range(n):
        x = ck.choke(dsp.vari(hit_of(WOOD_L[(k + i) % 5], 0, 0.08), rng.uniform(10, 13)), 0.005, 0.012)
        tick = ck.choke(dsp.vari(hit_of(CHOP, 0, 0.03), rng.uniform(2, 5)), 0.002, 0.006)
        parts += [(t, unit(hp(x, 400)), -3.0 * i), (t, unit(hp(tick, 1200)), -9 - 3.0 * i)]
        t += rng.uniform(0.09, 0.13)
    return ck.place(parts)


def palette_model(rng, k, n=2):
    """The brush tapped on the palette, modelled: a thin board's modes knocked at its edge, the second tap softer."""
    parts, t = [], 0.0
    for i in range(n):
        x = ck.hollow(rng, [f * rng.uniform(0.95, 1.05) for f in PALETTE], 0.05, q=7, hit=0.0008)
        parts.append((t, x, -3.0 * i))
        t += rng.uniform(0.09, 0.13)
    return ck.place(parts)


def painting(rng, L):
    """What his hands do over a cycle of L s, as [(t, what, length)]: the brush loaded and tapped off, a few long strokes
    (laying in a sky), a while stood back looking, a run of dabs (leaves), a few shorter strokes, a tap now and then, and
    nothing in the last moments, so the cycle comes round on a pause."""
    ev, t = [], rng.uniform(0.15, 0.35)
    d = rng.uniform(0.5, 0.7)
    ev.append((t, "load", d))
    t += d + rng.uniform(0.25, 0.4)
    ev.append((t, "tap", 0.25))
    t += rng.uniform(0.4, 0.55)
    for _ in range(int(rng.integers(3, 5))):
        d = rng.uniform(0.45, 0.8)
        ev.append((t, "stroke", d))
        t += d + rng.uniform(0.08, 0.25)
    t += rng.uniform(0.5, 0.8)
    for _ in range(int(rng.integers(5, 8))):
        d = rng.uniform(0.05, 0.1)
        ev.append((t, "dab", d))
        t += rng.uniform(0.14, 0.26)
    t += rng.uniform(0.25, 0.4)
    while t < L - 1.4:
        d = rng.uniform(0.25, 0.5)
        ev.append((t, "stroke", d))
        t += d + rng.uniform(0.1, 0.25)
    if t < L - 0.6:
        ev.append((t + 0.1, "tap", 0.25))
    return ev


def easel(rng, parts, voice, real):
    """Dave's cycle: the brush on the canvas and the palette (`parts` the stroke, dab, load and tap makers) and his hum
    under it, on open ground, held to the mid band."""
    L = PAINT_L
    n = samples(L)
    stroke, dab, load, tap_ = parts
    ev = []
    for i, (t, what, d) in enumerate(painting(rng, L)):
        if what == "stroke":
            ev.append((t, stroke(rng, d), dsp.db2a(rng.uniform(-2, 0))))
        elif what == "dab":
            ev.append((t, dab(rng, d), dsp.db2a(rng.uniform(-4, -1))))
        elif what == "load":
            ev.append((t, load(rng, d), dsp.db2a(-5)))
        else:
            ev.append((t, tap_(rng, i, int(rng.integers(2, 4))), dsp.db2a(-6)))
    brush = at(W.place(n, ev), -26)
    hum = at(humming(rng, L, voice, real), -29.5)
    return loop(mix(brush, hum), n, rng, 0.07, "night", band_shape(*MID))


@recipe(LINE, "dave-paint", "canvas",
        "Dave at his easel: a brush's soft strokes and dabs on canvas, a tap on the palette, his low, tuneless humming",
        """From real recordings: the brush's bristles drawn over the canvas's weave (grains cut from anywhere in the
        packs' straw-like rustle, a belt's cloth crackling and pages sliding, evened and held to a soft bristle's band),
        the canvas's soft 'tup' as the brush lands (a real book set down flat, pitched up and choked); the brush worked
        round in the paint and tapped off on the palette's edge (a real light wood knock an octave up and a dry woody
        tick), a few long strokes laying in a sky, a while stood back looking, a run of dabs, shorter strokes. Under it his
        hum: the packs' real throat (its steady middle cut into single pulses, eight averaged into each so its roughness
        goes) laid back down one per period of a man's hum round 110-120 Hz, through closed lips; a few phrases of two to
        five notes, each wherever his voice wanders from the last and never on a scale, so it's no tune, with a soft
        breath in through the nose between (the packs' real gasp, darkened). Open ground at night. A 10 s loop, held to
        150 Hz-5 kHz.""",
        sources=TEXTURE + [FLAT, CHOP, ROAR, GASP] + WOOD_L, takes=1, loop=True, band=MID, lufs=-26, seconds=PAINT_L)
def paint_canvas(rng, k):
    return easel(rng, (stroke_rec, dab_rec, load_rec, palette_rec), pulse_voice, True)


@recipe(LINE, "dave-paint", "easel",
        "Dave at his easel, modelled: bristles slipping over a canvas's weave, dabs, a palette's tap, a man humming low",
        """Modelled: each stroke the bristles catching and slipping over the weave (stick-slip, fine and quick) with a dry
        rustle of their tips, the canvas's low modes knocked softly as the brush lands; dabs, the brush worked in the paint
        and tapped on a thin board's edge; the same rhythm of work as 'canvas'. His hum a glottis through a man's tract
        on 'm', 110-120 Hz, through closed lips: phrases of two to five notes that wander a semitone or three at a time,
        never on a scale (a murmur, no tune), a soft breath through the nose between them. Open ground at night. A 10 s
        loop, held to 150 Hz-5 kHz.""",
        takes=1, loop=True, band=MID, lufs=-26, seconds=PAINT_L)
def paint_easel(rng, k):
    return easel(rng, (stroke_model, dab_model, load_model, palette_model), glottal_voice, False)


# ---- Dave struck: a breath of a laugh --------------------------------------------------------------------------------

def puff(rng, L, real=True):
    """An amused breath out through the nose: quick in, falling away."""
    return nose_air(rng, L, real) * env([(0, 0), (0.02, 1), (L * 0.35, 0.45), (L, 0)], L)


def sigh_out(rng, L, real=True):
    """The rest of the air let out through the nose after it, easing in and dying away."""
    return nose_air(rng, L, real) * env([(0, 0), (0.06, 1), (L * 0.4, 0.6), (L, 0)], L)


def hm(rng, L, f0, voice, fall=0.85):
    """A short 'hm' through closed lips, a little lift at its start and falling away."""
    n = samples(L)
    f = env([(0, f0 * 1.04), (0.04, f0), (L, f0 * fall)], L, "exp")
    a = env([(0, 0), (0.025, 1), (L * 0.5, 0.7), (L, 0)], L)
    return unit(shut(voice(rng, dsp.fit(f, n), dsp.fit(a, n))))


def chuckle(rng, k, voice, real):
    """Three breaths of a laugh: a single amused 'hm' with the air let out through the nose after it; two quick puffs
    through the nose and a low 'hm'; a soft 'hm-hm' (two short voiced pulses, the second lower and softer) and a long
    breath out. Never a cry, never a word."""
    f0 = rng.uniform(112, 126)
    if k % 3 == 0:
        parts = [(0.0, puff(rng, 0.14, real), -4), (0.01, hm(rng, 0.24, f0 * 1.06, voice), 0),
                 (0.18, sigh_out(rng, rng.uniform(0.3, 0.36), real), -10)]
    elif k % 3 == 1:
        g = rng.uniform(0.12, 0.15)
        parts = [(0.0, puff(rng, 0.09, real), -2), (g, puff(rng, 0.1, real), -4),
                 (2 * g + 0.01, hm(rng, 0.17, f0, voice, 0.82), -1), (2 * g + 0.12, sigh_out(rng, 0.22, real), -12)]
    else:
        g = rng.uniform(0.12, 0.15)
        parts = [(0.0, hm(rng, 0.11, f0 * 1.12, voice, 0.9), 0), (0.0, puff(rng, 0.09, real), -8),
                 (g, hm(rng, 0.1, f0 * 1.0, voice, 0.9), -3), (g, puff(rng, 0.08, real), -10),
                 (2 * g + 0.02, sigh_out(rng, rng.uniform(0.3, 0.36), real), -10)]
    return once(ck.place(parts), rng, 0.06, 0.15, 120)


@recipe(LINE, "dave-laugh", "breath",
        "A breath of a laugh as he's struck: a soft, warm, amused 'hm' and air out through the nose",
        """From real recordings: the air through his nose the packs' real gasp, drawn out and darkened (300 Hz-3 kHz), in
        quick, amused puffs; his 'hm' the packs' real throat cut into single pulses (eight averaged into each) and laid
        back down at a man's 110-130 Hz, through closed lips, a little lift at its start and falling away. Takes: a single
        'hm' and the air let out after it; two quick puffs and a low 'hm'; a soft 'hm-hm', the second lower, and a long
        breath out. Nothing hurt, nothing angry. Half a second or so, on open ground.""",
        sources=[GASP, ROAR], takes=3, lufs=-22)
def laugh_breath(rng, k):
    return chuckle(rng, k, pulse_voice, True)


@recipe(LINE, "dave-laugh", "chuckle",
        "A breath of a laugh, modelled: an amused 'hm' through closed lips, a puff of air through the nose",
        """Modelled: a man's voice (a glottis through a tract on 'm', 110-130 Hz) through closed lips for the 'hm', and
        breath through the nose (plain air coloured by a closed mouth) in quick puffs. The same three takes as 'breath':
        a single 'hm'; two puffs and a low 'hm'; a soft 'hm-hm' and a long breath out. Half a second or so.""",
        takes=3, lufs=-22)
def laugh_chuckle(rng, k):
    return chuckle(rng, k, glottal_voice, False)


# ---- Dave turns: the brush set down, the stool scraped round ---------------------------------------------------------

def ledge_rec(rng, k):
    """The brush laid on the easel's ledge: the handle's end and then its length coming down on the wood (real light wood
    knocks pitched up for a small, thin ledge, choked), a dry tick of its ferrule (a real blade's chop), a small roll."""
    a = ck.choke(dsp.vari(hit_of(WOOD_L[k % 5], 0, 0.08), rng.uniform(8, 11)), 0.006, 0.014)
    b = ck.choke(dsp.vari(hit_of(WOOD_L[(k + 2) % 5], 0, 0.08), rng.uniform(10, 13)), 0.004, 0.01)
    tick = ck.choke(dsp.vari(hit_of(CHOP, 0, 0.03), rng.uniform(3, 6)), 0.002, 0.006)
    g = rng.uniform(0.035, 0.06)
    return ck.place([(0, unit(hp(a, 300)), 0), (0.001, unit(hp(tick, 1200)), -8), (g, unit(hp(b, 400)), -5),
                     (g + rng.uniform(0.04, 0.07), unit(hp(b, 600)), -16)])


def ledge_model(rng, k):
    """The brush laid on the ledge, modelled: a thin wooden ledge's modes knocked twice (the handle's end, then its
    length), a small tick of the ferrule, a little roll."""
    a = ck.hollow(rng, [f * rng.uniform(0.9, 1.1) for f in PALETTE], 0.05, q=6, hit=0.0008)
    b = ck.hollow(rng, [f * rng.uniform(1.1, 1.3) for f in PALETTE], 0.04, q=6, hit=0.0006)
    tick = ck.tick(rng, rng.uniform(3500, 5000), q=12, length=0.01)
    g = rng.uniform(0.035, 0.06)
    return ck.place([(0, a, 0), (0.001, unit(tick), -10), (g, b, -5), (g + rng.uniform(0.04, 0.07), b, -16)])


def scrape_rec(rng, L):
    """The stool's legs dragged round over the dirt and grit: real stones scraping and crackling, dropped a little for
    wood on loose ground, real gravel shoved and rolling ahead of the legs, moving only while he turns."""
    v = env([(0, 0), (L * 0.2, 1), (L * 0.7, 0.75), (L, 0)], L)
    s = dsp.vari(ck.get(STONES[int(rng.integers(2))]), rng.uniform(-4, -2))
    a = int(rng.integers(0, max(1, len(s) - samples(L))))
    s = unit(dsp.fit(s[a:], samples(L)))
    roll = gravel_grains(rng, L, 30 + 140 * v, (-3, 1))
    return mix(unit(hp(s, 300)) * v, roll * v ** 0.7 * 0.45)


def scrape_model(rng, L):
    """The stool's legs dragged over grit, modelled: wood juddering on the ground (stick-slip through a leg's modes) and
    grit breaking and rolling under it."""
    v = env([(0, 0), (L * 0.2, 1), (L * 0.7, 0.75), (L, 0)], L)
    rub = ck.friction(rng, L, rng.uniform(140, 220), 500, 5000, 0.8)
    grit = unit(dsp.fit(ck.grains(rng, int(110 * L), L, 900, 6000, q=(3, 8), length=(0.004, 0.014), shape=0.0), samples(L)))
    return mix(unit(rub) * v, grit * v ** 0.7 * 0.55)


def creak_rec(rng, L, k):
    """The stool's joints as his weight goes round on it: a real small wooden creak, dropped a little, cut to `L` s."""
    c = dsp.vari(ck.align(ck.get(CREAKS[k % 2])), rng.uniform(-4, -1))[:samples(L)]
    n = len(c) / SR
    return unit(bp(c, 350, 4000)) * env([(0, 0), (0.02, 1), (n * 0.7, 0.8), (n, 0)], n)


def creak_model(rng, L, k):
    """The stool's joints, modelled: slow stick-slip through a small stool's dry modes."""
    s = synth.creak(L, env([(0, 30), (L, 60)], L, "exp"), rng, body=[f * rng.uniform(0.9, 1.1) for f in STOOL], q=10,
                    jitter=0.4, grit=0.4)
    return unit(hp(s, 350)) * env([(0, 0), (0.02, 1), (L * 0.7, 0.8), (L, 0)], L)


def cloth_model(rng, L):
    """His clothes moving, modelled: a soft breath of cloth's broad band."""
    return unit(bp(synth.noise(L, rng, "pink"), 500, 4000) * env([(0, 0), (L * 0.3, 1), (L, 0)], L))


def turning(rng, k, ledge, scrape, creak, cloth):
    """The brush set down, then he turns on his stool to face you: one long scrape round (take one), or two shorter
    shoves (take two); its joints creaking under him, his clothes moving, a leg set down at the end."""
    parts = [(0.0, ledge(rng, k), 0)]
    t = rng.uniform(0.22, 0.3)
    parts.append((t - 0.06, cloth(rng, 0.3), -12))
    if k % 2 == 0:
        D = rng.uniform(0.55, 0.7)
        parts += [(t, scrape(rng, D), -3), (t + 0.1, creak(rng, rng.uniform(0.3, 0.4), k), -9)]
        end = t + D
    else:
        a, b = rng.uniform(0.25, 0.32), rng.uniform(0.28, 0.36)
        gap = rng.uniform(0.1, 0.16)
        parts += [(t, scrape(rng, a), -4), (t + 0.05, creak(rng, 0.22, k), -10),
                  (t + a + gap, scrape(rng, b), -3), (t + a + gap + 0.08, creak(rng, 0.25, k + 1), -9)]
        end = t + a + gap + b
    parts.append((end - 0.02, cloth(rng, 0.25), -15))
    return once(ck.place(parts), rng, 0.07, 0.2, 150)


def leg_down_rec(rng, k):
    """A stool's leg set down square on the ground: a real light wood knock, dulled by the dirt."""
    x = ck.choke(dsp.vari(hit_of(WOOD_L[(k + 1) % 5], 0, 0.1), rng.uniform(3, 5)), 0.01, 0.02)
    return unit(lp(hp(x, 250, 2), 3000))


@recipe(LINE, "dave-turn", "ledge",
        "His last warning: the brush set down on the easel's ledge with a small wooden tap, his stool scraped round",
        """From real recordings: the brush laid on the easel's ledge, its handle's end and then its length coming down on
        the wood (real light wood knocks pitched up for a thin ledge, choked) with a dry tick of its ferrule (a real
        blade's chop); then he turns on his stool to face you: its legs dragged round over the dirt and grit (real stones
        scraping and crackling, dropped a little, real gravel shoved and rolling), its joints creaking under him (a real
        small creak), his clothes moving (the packs' cloth), a leg set down square at the end. Takes: one long scrape
        round; two shorter shoves. About 1.2 s, on open ground, nothing under 150 Hz.""",
        sources=WOOD_L + [CHOP] + GRAVEL + CREAKS + CLOTH, takes=2, lufs=-21)
def turn_ledge(rng, k):
    y = turning(rng, k, ledge_rec, scrape_rec, creak_rec, lambda r, L: unit(lp(ck.cloth(r, k, L), 5000)))
    return mix(y, ck.place([(len(y) / SR - 0.18, leg_down_rec(rng, k), -12)], len(y) / SR))


@recipe(LINE, "dave-turn", "stool",
        "His last warning, modelled: a brush laid on a wooden ledge, a stool's legs juddering round over grit",
        """Modelled: the brush laid on the ledge (a thin wooden ledge's modes knocked twice, the ferrule's tick, a little
        roll), then the stool's legs dragged round over the dirt (wood juddering on the ground in stick-slip, grit
        breaking and rolling), its joints creaking (slow stick-slip through a small stool's dry modes), his clothes
        moving. The same two takes as 'ledge'. About 1.2 s, nothing under 150 Hz.""",
        takes=2, lufs=-21)
def turn_stool(rng, k):
    y = turning(rng, k, ledge_model, scrape_model, creak_model, cloth_model)
    leg = ck.hollow(rng, [f * rng.uniform(0.9, 1.1) for f in STOOL[:3]], 0.05, q=4, hit=0.002)
    return mix(y, ck.place([(len(y) / SR - 0.18, leg, -12)], len(y) / SR))


# ---- Dave's grab: a collar taken in his fist -------------------------------------------------------------------------

def land_rec(rng, k):
    """His hand landing on the collar: a real heavy leather drop (a hand on a coat) and a real soft blow, only their slap
    kept (nothing under 250 Hz), the cloth snatched taut at once (the sharp start of a real cloth handling)."""
    drop = ck.choke(hit_of(DROP_LEATHER, 0, 0.15), 0.02, 0.03)
    soft = ck.choke(hit_of(SOFT[k % 5], 0, 0.1), 0.01, 0.02)
    snatch = hp(ck.cloth(rng, k, 0.08), 500)
    return ck.place([(0, unit(hp(drop, 250, 4)), 0), (0, unit(hp(soft, 300, 4)), -6), (0.003, unit(snatch), -2)])


def land_model(rng, k):
    """His hand landing on the collar, modelled: skin and cloth meeting fast (a short crack of noise, 400 Hz-5 kHz), the
    cloth snapping taut (a burst of broad cloth noise)."""
    slap = bp(synth.noise(0.06, rng), 400, 5000) * env([(0, 1), (0.005, 0.5), (0.06, 0)], 0.06, "exp")
    taut = bp(synth.noise(0.08, rng, "pink"), 700, 6000) * env([(0, 0), (0.004, 1), (0.08, 0)], 0.08)
    return ck.place([(0, unit(slap), 0), (0.003, unit(taut), -2)])


def wrench_rec(rng, k, L):
    """The cloth wrenched up tight in his fist: a real cloth handling's loudest stretch, bunching."""
    return unit(dsp.fit(hp(ck.cloth(rng, k + 1, L), 300), samples(L))) * env([(0, 0), (0.01, 1), (L * 0.6, 0.7), (L, 0)], L)


def wrench_model(rng, k, L):
    """The cloth wrenched tight, modelled: its threads rubbing and bunching (fine stick-slip and a rustle)."""
    sh = env([(0, 0), (0.01, 1), (L * 0.6, 0.7), (L, 0)], L)
    rub = ck.friction(rng, L, 420, 900, 6000, 0.9, sh)
    bunch = synth.rustle(L, 400 * sh + 20, rng, f=(900, 5000), ticks=0.0) * sh
    return unit(mix(unit(rub), unit(bunch) * 0.6))


def intake(rng, L, real=True):
    """A sharp breath in through an open mouth, rising fast, stopped short as the grip closes: the real gasp drawn out and
    held to a mouth's band, or modelled air drawn in on 'ah' closing to 'h'."""
    sh = env([(0, 0), (L * 0.55, 1), (L * 0.92, 1), (L, 0)], L)
    if real:
        x = lp(bp(drawn(rng, GASP, L, rng.uniform(0, 2), 0.04), 700, 6000), 5000)
    else:
        x = mix(unit(synth.breath(L, [(0, "a"), (L, "h")], "man", rng)) * 0.55,
                unit(lp(hp(synth.noise(L, rng, "pink"), 600), 5000)) * 0.5)
    return unit(x) * sh


def grabbing(rng, k, land, wrench, real):
    """The grab, half a second: his hand lands on the collar and snatches it taut, the cloth wrenched up in his fist (in
    one pull, or two as he twists it), a sharp breath in, cut short, and the cloth straining in his grip after."""
    parts = [(0.0, land(rng, k), 0)]
    if k % 2 == 0:
        parts.append((0.012, wrench(rng, k, rng.uniform(0.18, 0.24)), -3))
        parts.append((0.04, intake(rng, rng.uniform(0.26, 0.3), real), -5))
        parts.append((0.26, wrench(rng, k + 3, 0.16), -13))
    else:
        parts.append((0.012, wrench(rng, k, 0.12), -3))
        parts.append((0.15, wrench(rng, k + 2, 0.16), -5))
        parts.append((0.035, intake(rng, rng.uniform(0.2, 0.24), real), -6))
        parts.append((0.31, wrench(rng, k + 3, 0.14), -14))
    return once(ck.place(parts), rng, 0.05, 0.12, 180)


@recipe(LINE, "dave-grab", "collar",
        "The snap as he takes someone by the neck: a hand landing on a collar, the cloth wrenched tight, a sharp breath in",
        """From real recordings: his hand landing hard on the collar (a real heavy leather drop and a real soft blow, only
        their slap kept: nothing under 250 Hz), the cloth snatched taut and wrenched up in his fist (the packs' real cloth
        handlings, the sharp start and the bunching of them), and a sharp breath in through the mouth, rising and cut
        short as the grip closes (the packs' real gasp, held to a mouth's band). Takes: one pull; a pull and a twist.
        A hand, never a creature: nothing wet, no bone. About half a second.""",
        sources=[DROP_LEATHER, GASP] + SOFT + CLOTH, takes=2, lufs=-20)
def grab_collar(rng, k):
    return grabbing(rng, k, land_rec, wrench_rec, True)


@recipe(LINE, "dave-grab", "grip",
        "The grab, modelled: skin and cloth meeting fast, the cloth bunched tight in a fist, a sharp breath in",
        """Modelled: his hand landing on the collar (a short crack of noise, skin and cloth, 400 Hz-5 kHz) and the cloth
        snapping taut, wrenched up in his fist (its threads rubbing and bunching: fine stick-slip and a rustle), and a sharp
        breath drawn in through an open mouth ('ah' closing to 'h'), cut short. The same two takes as 'collar'. About half
        a second.""",
        takes=2, lufs=-20)
def grab_grip(rng, k):
    return grabbing(rng, k, land_model, wrench_model, False)


# ---- Nicki's party: the tune (a jig of our own, 6/8, D major) --------------------------------------------------------

# (MIDI note, eighths) per bar: eight bars of A, eight of B, each bar six eighths. Nobody's tune: written for this.
TUNE = [
    [(69, 1), (74, 2), (73, 1), (74, 1), (76, 1)], [(78, 2), (76, 1), (74, 2), (71, 1)],
    [(69, 1), (66, 1), (69, 1), (71, 2), (69, 1)], [(67, 1), (66, 1), (64, 1), (66, 3)],
    [(69, 1), (74, 2), (73, 1), (74, 1), (76, 1)], [(78, 1), (79, 1), (78, 1), (76, 2), (74, 1)],
    [(71, 1), (73, 1), (74, 1), (76, 1), (73, 1), (69, 1)], [(74, 3), (74, 2), (69, 1)],
    [(78, 1), (81, 2), (78, 1), (76, 1), (74, 1)], [(76, 1), (78, 1), (76, 1), (73, 2), (69, 1)],
    [(71, 2), (74, 1), (73, 2), (76, 1)], [(74, 1), (73, 1), (71, 1), (69, 3)],
    [(78, 1), (81, 2), (78, 1), (79, 1), (81, 1)], [(83, 2), (81, 1), (79, 1), (78, 1), (76, 1)],
    [(74, 1), (71, 1), (73, 1), (76, 1), (73, 1), (69, 1)], [(74, 3), (74, 2), (69, 1)],
]
CHORDS = "D D D A D G A D D A G A D G A D".split()
BASS = {"D": 50, "A": 45, "G": 43}
TRIAD = {"D": [57, 62, 66], "A": [57, 61, 64], "G": [59, 62, 67]}
BAR = PARTY_L / len(TUNE)            # one bar of 6/8 (s)
LILT = [0.0, 0.35, 0.68]             # where a beat's three eighths fall in it: the first leaned on (a jig's lilt)


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def eighth(i):
    """When the i-th eighth of the tune falls (s): two beats to a bar, three eighths to a beat, lilted."""
    bar, j = divmod(i, 6)
    beat, e = divmod(j, 3)
    return bar * BAR + (beat + LILT[e]) * BAR / 2


def notes(rng):
    """The tune as [(t, midi, length, level)]: lilted, a hair loose in time like a player's, each beat's first note
    leaned on; and the left hand's bass on each bar's one and its chord on the four."""
    mel, i = [], 0
    for bar in TUNE:
        for m, d in bar:
            t = eighth(i) + rng.normal(0, 0.006)
            L = eighth(i + d) - eighth(i) if i + d < 96 else PARTY_L - eighth(i)
            mel.append((t, m, L + 0.015, (1.0, 0.72, 0.84)[(i % 6) % 3] * rng.uniform(0.92, 1.0)))
            i += d
    left = []
    for b, c in enumerate(CHORDS):
        left.append((b * BAR + rng.normal(0, 0.005), [BASS[c]], BAR / 2 * 0.55, 1.0))
        left.append((b * BAR + BAR / 2 + rng.normal(0, 0.005), TRIAD[c], BAR / 2 * 0.45, 0.7))
    return mel, left


@functools.lru_cache(maxsize=64)
def _reed(semis):
    """The real reed's steady held stretch (0.1-1.0 s), varispeeded `semis` (a sampler's note: pitch and length move
    together, as a tape's do)."""
    x = ck.get(REED)[samples(0.1):samples(1.0)]
    return dsp.vari(x, semis)


def reed_rec(rng, m, L, vel):
    """One note on the squeezebox from the real reed: its held stretch played at the note's pitch, the bellows opening it
    and the button closing it."""
    semis = round(12 * np.log2(hz(m) / REED_HZ) + rng.normal(0, 0.03), 2)
    x = _reed(semis)
    a = int(rng.integers(0, max(1, len(x) - samples(L) - 1)))
    y = dsp.fit(x[a:], samples(L))
    return y * env([(0, 0), (0.012, 1), (max(L - 0.025, 0.02), 0.9), (L, 0)], L) * vel


def reed_model(rng, m, L, vel):
    """One note on a squeezebox, modelled: two free reeds a few cents apart (narrow pulses, so every harmonic's there),
    through the reed chamber's hollow."""
    n = samples(L)
    t = np.arange(n) / SR
    y = np.zeros(n, np.float32)
    for d in (1.0, 2 ** (rng.uniform(6, 12) / 1200)):
        ph = (hz(m) * d * t + rng.uniform(0, 1)) % 1.0
        y += (ph < 0.28).astype(np.float32) - 0.28
    y = mix(lp(y, 5000, 2) * 0.6, dsp.resonate(y, [900, 1700, 2600], q=3, gains=[1, 0.6, 0.3]) * 0.4)
    return unit(hp(y, 80)) * env([(0, 0), (0.012, 1), (max(L - 0.025, 0.02), 0.9), (L, 0)], L) * vel


def squeezebox(rng, play):
    """The tune on a squeezebox (`play` one note): the melody, and under it the left hand's bass and chords, softer."""
    mel, left = notes(rng)
    n = samples(PARTY_L)
    ev = [(t, play(rng, m, L, v), 1.0) for t, m, L, v in mel]
    for t, ms, L, v in left:
        for m in ms:
            ev.append((t, play(rng, m, L, v), 0.42 if len(ms) > 1 else 0.6))
    return W.place(n, ev)


def bowed(rng, f, amp):
    """A fiddle, modelled: the bowed string's sawtooth (Helmholtz motion) at `f` with the bow's hiss on it, through a
    violin's body (its air and wood modes, the bridge's hill)."""
    n = len(f)
    ph = np.cumsum(f) / SR
    saw = (2 * (ph % 1.0) - 1).astype(np.float32)
    hiss = bp(synth.noise(n / SR, rng), 2000, 7000) * 0.04
    s = (saw + hiss[:n]) * amp
    body = dsp.resonate(s, VIOLIN, q=6, gains=[1.0, 0.8, 0.9, 0.6, 0.5, 0.7, 0.5])
    return unit(lp(mix(lp(s, 4000) * 0.35, unit(body) * 0.65), 7000))


def fiddle(rng):
    """The tune on a fiddle, modelled: one long bowed line (a slide between notes, a vibrato that blooms on the long
    ones, a fresh bow on each beat), and the left hand's bass and chords on modelled reeds under it."""
    mel, left = notes(rng)
    n = samples(PARTY_L)
    seq = []
    for i, (t, m, L, v) in enumerate(mel):
        nxt = mel[i + 1][0] if i + 1 < len(mel) else PARTY_L
        seq.append((m, (nxt - t) / (BAR / 6)))
    f0, onsets, total = kit.melody_f0(seq, BAR / 6, rng, scoop=18, glide=0.012, vib=(5.8, 0.005), drift=4)
    a = kit.note_env(onsets, total, att=0.025, rel=0.04, dip=0.45)
    lv = np.ones(len(a), np.float32)
    for (t, m, L, v), o in zip(mel, onsets):
        i0 = samples(o)
        lv[i0:] = v
    y = dsp.fade(bowed(rng, dsp.fit(f0, len(a)), a * lv), 0.02, 0.03)    # the last bow's end and the first's start meet
    start = mel[0][0]
    line = W.place(n, [(start, y, 1.0)])
    ev = []
    for t, ms, L, v in left:
        for m in ms:
            ev.append((t, reed_model(rng, m, L, v), 0.42 if len(ms) > 1 else 0.6))
    return mix(at(line, -20), at(W.place(n, ev), -25))


# ---- Nicki's party: the people ---------------------------------------------------------------------------------------

def laugh(rng, f0, count, tract="man", real=True):
    """A laugh: `count` 'ha's at 4.5-6 a second, each an 'h' of air and a short voiced 'a', the pitch high at the start
    and falling through the run, each 'ha' falling a little too, the last ones softer; breathy, masks down."""
    rate = rng.uniform(4.5, 6.0)
    L = count / rate + 0.3
    n = samples(L)
    on = [i / rate * rng.uniform(0.94, 1.06) for i in range(count)]
    vg = np.zeros(n, np.float32)
    ag = np.zeros(n, np.float32)
    ch = np.zeros(n, np.float32)
    k = np.arange(n) / SR
    for i, t in enumerate(on):
        lvl = 0.55 + 0.45 * (1 - i / max(count, 1)) ** 1.2
        h = rng.uniform(0.02, 0.035)
        d = rng.uniform(0.06, 0.09)
        a0, a1 = samples(t), samples(t + h)
        ag[a0:a1] = np.maximum(ag[a0:a1], np.hanning(max(a1 - a0, 2))[:a1 - a0] * lvl)
        seg = env([(0, 0), (0.008, 1), (d * 0.6, 0.8), (d, 0)], d) * lvl
        b0 = samples(t + h * 0.7)
        m = min(len(seg), n - b0)
        vg[b0:b0 + m] = np.maximum(vg[b0:b0 + m], seg[:m])
        ch[b0:] = np.maximum(ch[b0:], 0.6 * np.exp(-k[:n - b0] / 0.05))
    f = env([(0, f0 * 1.3), (L, f0 * 0.92)], L, "exp") * 2 ** (dsp.fit(ch, n) / 12)
    g = synth.glottis(f, L, rng, jitter=0.012, shimmer=0.15, oq=0.55)
    vows = sorted((t, ("a", "e")[int(rng.integers(2))]) for t in on) or [(0, "a")]
    v = synth.tract(g, vows, tract, breath=0.35, rng=rng) * vg
    air = drawn(rng, GASP, L, rng.uniform(-2, 1), 0.04) if real else synth.breath(L, "h", tract, rng)
    return unit(mix(unit(v), unit(bp(air, 500, 6000)) * ag * 0.45))


def talk(rng, L, voices):
    """People talking under the music with their masks down: each voice wordless syllables on mixed vowels, phrases and
    pauses (town.mumble), livelier than the town's murmur, a few metres apart across the room; round the cycle."""
    n = samples(L)
    out = np.zeros(n, np.float32)
    for f0, tract, rate, db in voices:
        v = town.mumble(rng, L + 0.5, f0 * rng.uniform(0.95, 1.05), tract, rate)
        out += round_on(v, n) * dsp.db2a(db)
    return out


def laughs(rng, n, real):
    """Laughter round the cycle: a man's short laugh, the room laughing together at something (three voices, one after
    another), and later a woman's laugh, bright and long (Nicki's, maybe)."""
    ev = []
    t = rng.uniform(1.8, 3.0)
    ev.append((t, laugh(rng, rng.uniform(115, 135), int(rng.integers(3, 5)), "man", real), dsp.db2a(-3)))
    t = rng.uniform(6.0, 7.0)
    for i, (f0, tract) in enumerate([(rng.uniform(210, 240), "woman"), (rng.uniform(120, 140), "man"),
                                      (rng.uniform(100, 115), "man")]):
        ev.append((t + i * rng.uniform(0.08, 0.2), laugh(rng, f0, int(rng.integers(4, 7)), tract, real),
                   dsp.db2a(-2 - 2 * i)))
    t = rng.uniform(11.5, 12.8)
    ev.append((t, laugh(rng, rng.uniform(225, 255), int(rng.integers(5, 8)), "woman", real), dsp.db2a(0)))
    return W.place(n, ev)


def clink_rec(rng, i):
    """A glass touched or set down: a real glass knocked, a little pitched."""
    return unit(hp(dsp.vari(hit_of(CHINA[i % len(CHINA)], 0, 0.25), rng.uniform(-1.5, 1.5)), 600))


def clink_model(rng, i):
    """A glass touched, modelled: a wine glass's modes struck at its rim, ringing briefly."""
    f = rng.uniform(1100, 1700)
    L = 0.35
    x = np.zeros(samples(L), np.float32)
    x[:samples(0.0006)] = rng.standard_normal(samples(0.0006))
    y = dsp.resonate(x, [f * r * rng.uniform(0.99, 1.01) for r in GOBLET], q=90, gains=[1.0, 0.5, 0.25, 0.12])
    return unit(y * env([(0, 1), (L, 0.001)], L, "exp"))


def glasses(rng, n, clink):
    """Glasses now and then: one set down, a toast (three touched within a fifth of a second), another set down."""
    ev = [(rng.uniform(0.5, 2.0), clink(rng, 0), dsp.db2a(-4))]
    t = rng.uniform(8.5, 9.5)
    for j in range(3):
        ev.append((t + j * rng.uniform(0.05, 0.09), clink(rng, j + 1), dsp.db2a(-1 - 2 * j)))
    ev.append((rng.uniform(13.5, 15.0), clink(rng, 5), dsp.db2a(-6)))
    return W.place(n, ev)


def board_rec(rng, i, kind):
    """A dancer's boot on the boards: real boots cut into heels and toes (heard_foley.stamp, step, tap)."""
    return {"stamp": lambda: stamp(rng, i), "step": lambda: step(rng, i), "tap": lambda: tap(rng, i)}[kind]()


def board_model(rng, i, kind):
    """A dancer's boot on the boards, modelled: a sole's slap and the floorboards' modes knocked, harder for a stamp."""
    w = {"stamp": 1.0, "step": 0.6, "tap": 0.35}[kind]
    slap = bp(synth.noise(0.03, rng), 600, 4500) * env([(0, 1), (0.004, 0.5), (0.03, 0)], 0.03, "exp")
    board = ck.hollow(rng, [f * rng.uniform(0.9, 1.1) for f in (150, 260, 430, 720)], 0.1 + 0.08 * w, q=3.5, hit=0.002)
    return ck.place([(0, unit(slap), -6), (0.001, board, 20 * np.log10(w))])


def dancing(rng, n, board):
    """Two or three dancing, each their own way round the jig's beat: a stamp on a bar's one, light steps and taps on the
    beats and the eighths between, now and then a pause as they turn."""
    figures = [[(0, "stamp"), (2, "step"), (3, "step"), (5, "tap")], [(0, "step"), (3, "stamp"), (4, "tap")],
               [(0, "stamp"), (1, "tap"), (3, "step"), (5, "step")]]
    ev = []
    for d in range(int(rng.integers(2, 4))):
        fig = figures[d % 3]
        lvl = rng.uniform(-4, 0)
        for b in range(len(TUNE)):
            if rng.random() < 0.12:
                continue
            for e, kind in fig:
                t = eighth(b * 6 + e) + rng.normal(0, 0.012)
                ev.append((t, board(rng, b * 6 + e + d, kind), dsp.db2a(lvl + rng.uniform(-3, 0))))
    return W.place(n, ev)


def walls(f):
    """The house's walls, as heard from the street: the timber and plaster let the low middle through and little above
    1.4 kHz; the windows a little of the top; nothing under 180 Hz carries far."""
    lo = 1 / np.sqrt(1 + (180 / f) ** 4)
    return lo * (0.85 / np.sqrt(1 + (f / 1400) ** 4) + 0.15 / np.sqrt(1 + (f / 6000) ** 4))


def party(rng, music, board, clink, real):
    """Sixteen bars of the party: the music, the talk under it, laughs, glasses, feet on the boards; in a small lit room,
    then through its walls to the street. Each part is set to its level as it's heard outside (the walls take more from a
    glass's ring than from a voice), the music on top, the talk and the laughter under it, the feet and the glasses
    under them."""
    n = samples(PARTY_L)
    voices = [(rng.uniform(105, 125), "man", rng.uniform(4.2, 5.0), -2), (rng.uniform(200, 225), "woman",
              rng.uniform(4.4, 5.2), -3), (rng.uniform(125, 140), "man", rng.uniform(3.8, 4.6), -5),
              (rng.uniform(215, 240), "woman", rng.uniform(4.0, 4.8), -7)]
    parts = [(music, -23), (talk(rng, PARTY_L, voices), -28.5), (laughs(rng, n, real), -28), (dancing(rng, n, board), -30),
             (glasses(rng, n, clink), -32)]
    y = sum(at(W.cfilter(dsp.wrap(x, n), walls), lufs) for x, lufs in parts)
    return loop(W.croom(y, "car", 0.22, rng), n, rng, 0.1, "night")


@recipe(LINE, "party", "squeezebox",
        "Nicki's party through the walls: a squeezebox jig kept low, talk and laughter under it, glasses, feet on boards",
        """The one house up late, heard from the street through its walls. The music a jig of our own (6/8, D major,
        sixteen bars round at 120 to the dotted crotchet, lilted: folk-like and nobody's tune) on a squeezebox made from a
        real held, reedy tone (sfx_100 misc_12, a bright harmonic tone round 683 Hz with a second voice beating a little
        sharp of it, as an accordion's two reeds do) played at each note's pitch, the left hand's bass and chords under
        it. Dancers on the boards (the packs' real boots cut into heels and toes: stamps, steps, taps on the jig's beats),
        glasses set down and touched in a toast (real glasses knocked), and under the music people talking with their
        masks down, laughing now and then (the packs have no voices: wordless voices modelled, their laughter's breath the
        packs' real gasp). In a small lit room, then through timber and plaster (little above 1.4 kHz, nothing under
        180 Hz). A 16 s loop. Warm, 200 Hz-6 kHz.""",
        sources=[REED, GASP] + BOOTS + CHINA, takes=1, loop=True, band=WARM, lufs=-25, seconds=PARTY_L)
def party_squeezebox(rng, k):
    return party(rng, squeezebox(rng, reed_rec), board_rec, clink_rec, True)


@recipe(LINE, "party", "fiddle",
        "Nicki's party through the walls, modelled: a fiddle's jig kept low over reeds, talk and laughter, glasses, feet",
        """Modelled: the same jig (6/8, D major, sixteen bars, our own) on a fiddle (a bowed string's sawtooth through a
        violin's body, sliding between notes, a vibrato blooming on the long ones), the bass and chords on two free reeds
        a few cents apart; dancers' boots on floorboards (a sole's slap and the boards' modes), glasses touched (a wine
        glass's modes rung at the rim), and people talking and laughing under it, masks down, no words (a glottis through
        a tract). In a small lit room, then through the walls to the street. A 16 s loop. Warm, 200 Hz-6 kHz.""",
        takes=1, loop=True, band=WARM, lufs=-25, seconds=PARTY_L)
def party_fiddle(rng, k):
    return party(rng, fiddle(rng), board_model, clink_model, False)


# ---- Jacob at the water's edge ---------------------------------------------------------------------------------------

CAST = 0.8          # when he casts in the cycle (s)
WIND = (5.4, 9.9)   # when he winds the line in


def wavelets(rng, L):
    """When the wavelets come in over a cycle of L s: every 2-4 s, the first once his cast's weight has landed and the
    last well before the next cast, so the cast is heard clear of them."""
    times, t = [], CAST + rng.uniform(2.1, 2.5)
    while t < L + CAST - 1.6:
        times.append(t % L)
        t += rng.uniform(2.0, 4.0)
    return times


def lapping_rec(rng, n):
    """Still water lapping at a stony shore, from real water: the packs' moving water slowed and darkened into wavelets
    every 2-4 s (a soft slosh up over a tenth of a second, dying away), its trickle sucking back between the stones after
    each, and a faint wash of it under all of it."""
    L = n / SR
    mass = W.norm(tile(WATER[0], n, -5))
    trickle = W.norm(tile(WATER[1], n, -2))
    body = W.cfilter(mass, band_shape(150, 1400))
    suck = W.cfilter(trickle, band_shape(1200, 6000))
    up, back = np.zeros(n, np.float32), np.zeros(n, np.float32)
    for t in wavelets(rng, L):
        g = rng.uniform(0.5, 1.0)
        up += W.place(n, [(t, env([(0, 0), (0.1, 1), (0.3, 0.5), (1.1, 0)], 1.1), g)])
        back += W.place(n, [(t + 0.35, env([(0, 0), (0.2, 1), (1.2, 0)], 1.2), g)])
    return mix(W.norm(body * (0.12 + up)), W.norm(suck * back) * 0.35)


def lapping_model(rng, n):
    """Still water lapping, modelled: water.py's wavelets at a stony shore (a slosh, a plop or two, the water sucking back
    between the stones) every 2-4 s over a faint wash."""
    laps = W.place(n, [(t, WA.lap(rng, rng.uniform(0.85, 1.15)), rng.uniform(0.5, 1.0)) for t in wavelets(rng, n / SR)])
    air = W.norm(WA.wash(rng, n, 300, 2000))
    return mix(W.norm(laps), air * 0.08)


@functools.lru_cache(maxsize=1)
def _clicker():
    """The real fast ticker's ticks, each cut to 12 ms."""
    x = ck.get(TICKER)
    out = []
    for s, _ in ck.hits(x, floor_db=-24, gap=0.008, prom=4)[:60]:
        out.append(unit(ck.cut(x, s - samples(0.0005), s + samples(0.012), 0.0003, 0.005)))
    return out


def payout_rec(rng, L):
    """The reel's clicker as the line pays out behind the cast: ticks of a real fast mechanism, quick at first and slowing
    to a stop as the weight's flight ends, quieter as it slows; the line's thin hiss through the guides with it (a real
    even hiss of air, held high)."""
    g = _clicker()
    b = Bus(L + 0.05)
    t, rate = 0.0, rng.uniform(38, 46)
    while t < L:
        p = t / L
        r = rate * (1 - p) ** 1.6 + 2
        b.at(t, dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(1, 3)), 20 * np.log10(0.35 + 0.65 * (1 - p)))
        t += (1 / r) * rng.uniform(0.85, 1.15)
    h = ck.get(AIR[1])
    a = int(rng.integers(0, max(1, len(h) - samples(L))))
    hiss = unit(bp(dsp.fit(h[a:], samples(L)), 3000, 8000)) * env([(0, 0), (0.02, 1), (L, 0)], L, "exp")
    return mix(unit(hp(b.x, 900)), hiss * 0.12)


def payout_model(rng, L):
    """The clicker paying out, modelled: a small pawl ticking over a gear's teeth (a hard tick rung through a little
    brass housing), quick and slowing to a stop, the line's hiss with it."""
    b = Bus(L + 0.05)
    t, rate = 0.0, rng.uniform(38, 46)
    body = [2900, 4700, 6900]
    while t < L:
        p = t / L
        r = rate * (1 - p) ** 1.6 + 2
        b.at(t, ck.tick(rng, rng.uniform(3000, 3600), q=14, length=0.012, body=body), 20 * np.log10(0.35 + 0.65 * (1 - p)))
        t += (1 / r) * rng.uniform(0.85, 1.15)
    hiss = unit(bp(synth.noise(L, rng), 3000, 8000)) * env([(0, 0), (0.02, 1), (L, 0)], L, "exp")
    return mix(unit(hp(b.x, 900)), hiss * 0.12)


def swish_rec(rng, take):
    """The rod's swish as he casts, from real air: the packs' short burst of air dropped most of an octave (slower, lower,
    longer) and swept through a band that rises and falls with the rod tip's speed."""
    x = dsp.vari(ck.get(AIR[0]), rng.uniform(-11, -8))
    L = len(x) / SR
    pk = L * rng.uniform(0.4, 0.5)
    fc = env([(0, 350), (pk, rng.uniform(1800, 2400)), (L, 450)], L, "exp")
    y = dsp.sweep_filter(x, "bp", dsp.fit(fc, len(x)), q=1.3) * dsp.fit(env([(0, 0), (pk, 1), (L, 0)], L), len(x)) ** 1.5
    return unit(hp(y, 250))


def swish_model(rng, take):
    """The rod's swish, modelled: a band of noise rising and falling with the rod tip's speed, the line's hiss after."""
    rod = ck.whoosh(rng, rng.uniform(0.35, 0.45), 250, rng.uniform(1800, 2600), 0.6, 1.1)
    line = ck.whoosh(rng, 0.25, 2500, 7000, 0.3, 2.0)
    return ck.place([(0, unit(rod), 0), (0.08, unit(line), -16)])


def plop(rng, real=True):
    """The weight landing out on the water: a small plop (a bubble's ring), a little of real water splashing round it."""
    b = WA.burble(rng, 500, 1400, (1, 3), 0.05)
    if real:
        w = dsp.vari(ck.get(WATER[1]), rng.uniform(1, 3))
        a = int(rng.integers(0, len(w) - samples(0.2)))
        s = unit(w[a:a + samples(0.2)]) * env([(0, 0), (0.005, 1), (0.2, 0)], 0.2, "exp")
        return mix(unit(b), s * 0.5)
    return mix(unit(b), unit(bp(synth.noise(0.15, rng), 800, 5000) * env([(0, 1), (0.15, 0)], 0.15, "exp")) * 0.35)


def ratchet_rec(rng):
    """A bank of the reel's clicks as it's wound in: single teeth cut from the packs' real ratchets and lock, pitched for a
    small reel's pawl."""
    return [tooth(TEETH[j % 2], i, rng.uniform(-3, 0), 0.008) for j in range(2) for i in range(4)]


def ratchet_model(rng):
    """A bank of the reel's clicks, modelled: a small pawl dropping off a gear tooth in a brass housing."""
    return [ck.norm(ck.tick(rng, rng.uniform(3800, 5200), q=10, length=0.015, body=[4300, 6100, 8200])) for _ in range(8)]


def winding(rng, a, b, bank):
    """The line wound in slowly between `a` and `b` s: the handle turned about once a second, ten clicks a turn, the turns
    a little uneven; a pause halfway as he waits on it."""
    ev, t = [], a
    half = (a + b) / 2
    paused = False
    while t < b:
        if not paused and t > half - 0.4:
            t += rng.uniform(0.6, 0.9)
            paused = True
            continue
        turn = rng.uniform(0.9, 1.15)
        for j in range(10):
            ev.append((t + turn * j / 10 + rng.normal(0, 0.004), bank[int(rng.integers(len(bank)))],
                       dsp.db2a(rng.uniform(-4, 0) - (2 if j % 10 else 0))))
        t += turn
    return [(tt, x, g) for tt, x, g in ev if tt < b]


def fishing(rng, lapping, payout, swish, bank, real):
    """Jacob's cycle: the water lapping all through it; a cast (the rod's swish, the clicker paying out, the weight's plop
    far out), a quiet, the line wound in click by click with a pause, a quiet again. Close beside him, at night."""
    n = samples(FISH_L)
    pay = rng.uniform(1.2, 1.5)
    y = mix(at(lapping(rng, n), -28), at(W.place(n, [(CAST, swish(rng, 0), 1.0)]), -28),
            at(W.place(n, [(CAST + 0.12, payout(rng, pay), 1.0)]), -31),
            at(W.place(n, [(CAST + pay + rng.uniform(0.0, 0.15), plop(rng, real), 1.0)]), -35),
            at(W.place(n, winding(rng, *WIND, bank)), -30))
    return loop(y, n, rng, 0.1, "night", band_shape(120, 9000))


@recipe(LINE, "jacob-fish", "reel",
        "Jacob at the water's edge: water lapping at the stones, his rod's swish, the reel ticking out, wound slowly in",
        """From real recordings: the lake lapping at its stony shore (the packs' moving water slowed and darkened into a
        wavelet every 2-4 s, its trickle sucking back between the stones after each, a faint wash under it); once a cycle
        he casts: the rod's swish (the packs' real burst of air, dropped most of an octave and swept with the tip's
        speed), the reel's clicker ticking as the line pays out (a real mechanism's fast, even ticks, quick at first and
        slowing to a stop) and the line's thin hiss through the guides (a real hiss, held high), the weight's small plop
        far out (a bubble's ring and a little real water);
        a quiet; then the line wound slowly in, about a turn a second, ten clicks a turn (single teeth cut from the packs'
        real ratchets and lock), a pause halfway as he waits on it; a quiet again. Calm. A 12 s loop.""",
        sources=WATER + AIR + [TICKER] + TEETH, takes=1, loop=True, lufs=-26, seconds=FISH_L)
def fish_reel(rng, k):
    return fishing(rng, lapping_rec, payout_rec, swish_rec, ratchet_rec(rng), True)


@recipe(LINE, "jacob-fish", "line",
        "Jacob at the water's edge, modelled: wavelets on stones, a rod's swish, a pawl ticking out, the reel wound in",
        """Modelled: the lake's wavelets at its stony shore (water.py's: a slosh, a plop or two, the water sucking back
        between the stones) every 2-4 s over a faint wash; the cast once a cycle: the rod's swish (a band of noise rising
        and falling with the tip's speed), the clicker's pawl ticking over a gear in a brass housing as the line pays out,
        slowing to a stop, the weight's plop far out; a quiet; the line wound slowly in, ten clicks a turn, a pause halfway;
        a quiet. The same cycle as 'reel'. A 12 s loop.""",
        takes=1, loop=True, lufs=-26, seconds=FISH_L)
def fish_line(rng, k):
    return fishing(rng, lapping_model, payout_model, swish_model, ratchet_model(rng), False)


# ---- Jacob's blessing: a rising shimmer down the cars ----------------------------------------------------------------

BLESS = [(74, 78, 81, 86, 90), (69, 74, 78, 81, 86)]     # the two takes' voicings: D major, so overlapping cars ring as one
SPARKLE = [(81, 86, 90, 93, 98, 102), (78, 81, 86, 90, 93, 98)]  # the chimes over each, climbing the chord


@functools.lru_cache(maxsize=4)
def _glass_core(key):
    """A real struck glass's ring from just past its strike (its steady tone, the knock left out)."""
    x = ck.align(ck.get(key))
    return x[samples(0.02):samples(0.32)].copy()


def ring_pitch(x):
    """Where a real glass's ring sits (Hz): its strongest partial."""
    f, p = signal.welch(x, SR, nperseg=min(4096, len(x)))
    m = (f > 400) & (f < 6000)
    return float(f[m][np.argmax(p[m])])


def bowed_glass_rec(rng, m, L, key):
    """One bowed glass from a real struck glass: its ring pulled down to the note and held out (paulstretched, so the
    strike's gone and the glass sings on, its cluster of close partials beating), and the note drawn out of it (the held
    ring through the glass's own resonance at the note: a rim rubbed into song)."""
    base = ring_pitch(_glass_core(key))
    x = dsp.vari(_glass_core(key), 12 * np.log2(hz(m) / base))
    y = unit(dsp.fit(dsp.smear(x, (L + 0.3) * SR / len(x), min(0.12, 0.45 * len(x) / SR), rng), samples(L)))
    sung = unit(dsp.resonate(y, [hz(m)], q=50))
    return unit(mix(sung * 0.7, lp(y, hz(m) * 4) * 0.3))


def bowed_glass_model(rng, m, L):
    """One bowed glass, modelled: a wine glass's partials sounding together, the rim's rub breathing in them."""
    n = samples(L)
    t = np.arange(n) / SR
    f = hz(m)
    y = np.zeros(n, np.float32)
    for i, r in enumerate(GOBLET[:3]):
        y += np.sin(2 * np.pi * f * r * t + rng.uniform(0, 6.28)).astype(np.float32) / (1 + 1.5 * i)
    rub = dsp.resonate(synth.noise(L, rng), [f], q=60) * 0.15
    return unit(dsp.tremolo(mix(y, unit(rub) * 0.1), rng.uniform(4, 6), 0.12, rng, jitter=0.3))


def chime_rec(rng, m):
    """A chime from a real light glass knocked, pitched to the note, its knock's click softened (only its ring wanted)."""
    x = hit_of(CHINA[3 + int(rng.integers(5))], 0, 0.3)
    return unit(dsp.fade(lp(dsp.vari(x, 12 * np.log2(hz(m) / ring_pitch(x))), 7000), 0.004, 0.03))


def chime_model(rng, m):
    """A chime, modelled: a small bar's partials struck softly, the high ones dying first."""
    L = 0.5
    t = np.arange(samples(L)) / SR
    y = np.zeros_like(t)
    for i, r in enumerate(TINE):
        y += np.sin(2 * np.pi * hz(m) * r * t) * np.exp(-t / (0.35 / (1 + 1.2 * i))) / (1 + 1.5 * i)
    y[:samples(0.004)] *= np.linspace(0, 1, samples(0.004))     # struck softly: no click
    return unit(y.astype(np.float32))


def warm_air(rng, L, real=True):
    """A breath of warm air going by: the real gasp's air drawn out and darkened (or pink noise), swelling and easing."""
    x = drawn(rng, GASP, L, rng.uniform(-6, -4), 0.08) if real else synth.noise(L, rng, "pink")
    return unit(bp(x, 250, 2500)) * env([(0, 0), (L * 0.45, 1), (L, 0)], L)


def blessing(rng, k, glass, chime, real):
    """The blessing, about two seconds: the voicing's glasses coming in one above another from the bottom, a fifth of a
    second apart (each a slow swell, no strike), so it rises, brightening as it goes; chimes sparkling upward over it on
    the chord's notes; a breath of warm air through it. Soft in, soft out, and in tune however the cars' takes overlap."""
    L = rng.uniform(1.9, 2.3)
    chord = BLESS[k % 2]
    n = samples(L)
    b = Bus(L + 0.6)
    for i, m in enumerate(chord):
        t = i * rng.uniform(0.16, 0.21)
        d = L - t
        g = glass(rng, m, d)
        g = dsp.shaped(g, [(0, 0), (d * 0.45, 1), (d * 0.75, 0.8), (d, 0)])
        b.at(t, g, -2.5 * i)
    pad = b.x[:n] if len(b.x) >= n else dsp.fit(b.x, n)
    pad = dsp.sweep_filter(pad, "lp", dsp.fit(env([(0, 1500), (L * 0.6, 7000), (L, 5000)], L, "exp"), n), q=0.7)
    sparkle = Bus(L + 0.6)
    steps = SPARKLE[k % 2]
    t = L * rng.uniform(0.18, 0.25)
    for j, m in enumerate(steps):
        sparkle.at(t, chime(rng, m), -6 - 1.5 * abs(j - len(steps) * 0.6))
        t += rng.uniform(0.08, 0.14)
    y = mix(at(pad, -24), at(sparkle.x, -29), at(warm_air(rng, L, real), -33))
    y = dsp.shaped(y, [(0, 0), (0.35, 0.8), (L * 0.6, 1.0), (L, 0.0)])
    return hp(dsp.room(y, "night", wet=0.12, rng=rng)[:samples(L + 0.25)], 150, 2)


@recipe(LINE, "jacob-bless", "glass",
        "Jacob's blessing: a soft, rising shimmer of bowed glass and chimes, a breath of warm air, down the cars",
        """From real recordings: bowed glass made from the packs' real struck glass (its ring held out, paulstretched so the
        strike's gone and the glass sings on, pulled to each note), a D major chord coming in from the bottom up, a glass
        at a time a fifth of a second apart, each swelling in slowly, so it rises and brightens; chimes sparkling upward
        over it on the chord's notes (real light glasses knocked, pitched, soft); a breath of warm air through it (the
        packs' real gasp drawn out and darkened). Soft attack and soft end: it plays once a car down the train, staggered,
        and two takes in the same key ring as one chord. Kind and a little magical: nothing like a creature or an alarm.
        About two seconds.""",
        sources=RINGING + CHINA + [GASP], takes=2, lufs=-21)
def bless_glass(rng, k):
    return blessing(rng, k, lambda r, m, d: bowed_glass_rec(r, m, d, RINGING[(k + m) % 2]), chime_rec, True)


@recipe(LINE, "jacob-bless", "chimes",
        "Jacob's blessing, modelled: glass partials swelling up a D major chord, chimes rising over them, warm air",
        """Modelled: bowed glasses (a wine glass's partials sounding together, the rim's rub breathing in them) coming in
        from the bottom of a D major chord a glass at a time, each swelling slowly, so it rises and brightens; chimes (a
        small bar's partials struck softly) sparkling upward over it on the chord's notes; a breath of warm air (pink
        noise, darkened). Soft in, soft out, the two takes in the same key. About two seconds.""",
        takes=2, lufs=-21)
def bless_chimes(rng, k):
    return blessing(rng, k, bowed_glass_model, chime_model, False)
