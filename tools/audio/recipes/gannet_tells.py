"""The Gannet's tells (spec A.4; G1's design, docs/design/creatures/gannet.md §4): one enormous corrupted seabird riding
a fast train.

What it is: a northern gannet grown to a 7 m wingspan, sooted grey-white, a sulphur head, a metre of serrated beak, pale
sacs glowing in its throat and breast. It holds station 20-35 m over the cars in the smoke and the wake, hangs over a
roof-walker (its calls stop: the silence is the tell), folds and drops beak-first, and when someone hurts it, banks round
screaming and comes down on them. Everything here is heard from above, over the train's wind and rolling bed (heavy at
60 Hz-1 kHz), so each sound is built from what a real gannet does at the size of this one and held in a band the bed
can't cover:

- Presence (1-3 kHz, 800 Hz-5 kHz): a gannet's harsh pulsed "arrr-arrr", slowed and huge. A bird's voice is a
  syrinx, not a larynx: the rasp is the voice broken into pulses (25-35 a second here), and both sides of the syrinx can
  sound at once. Then one great wingbeat now and then: air torn through the primaries, the shafts creaking.
- The fold (1-6 kHz, 2-5 kHz): the wings snapped back like a sail filling, then 1.6 s of air whistling past a folded
  dart, rising with its speed and loudest at the end, where the strike is.
- The bank (600 Hz-4 kHz, 150 Hz-4 kHz): one long rising scream, raw and two-voiced, and the heavy wingbeats coming in
  low, nearly two a second.

The voice and the air are synthesised (a two-sided syrinx through a long neck's tract and a beak's hollow; noise shaped
by a wing's speed); the cloth, the whip and the wood are real recordings, bent. It mustn't sound like another creature's
tell (A.1): no chorus of human-ish voices (the Choir), nothing steady and tonal low down (the Whistler), no long tonal
moan (the Moose), no howl (the hounds). Its syllabic rasp is what makes it the Gannet's.
"""

import numpy as np

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes.beasts import fry, unit
from recipes.moose_tells import burst, ring, held, cycle

SR = dsp.SR

CALL = (1000, 3000)                   # spec A.4's Gannet row: the calls at 1-3 kHz
WINGS = (800, 5000)
CRACK = (1000, 6000)
WHISTLE = (2000, 5000)
SCREAM = (600, 4000)
BEATS = (150, 4000)

TRACT = 0.5                           # its neck: a vocal tract half a child's (synth.TRACT), longer than a man's
BEAK = [(1900, 5, 0.5), (2700, 6, 0.45)]   # the hollow of a metre-long bill and the pouch under it (Hz, q, gain)
QUILL = [1250, 1900, 2750, 3900]      # a primary's hollow shaft, 60 cm of keratin tube: its modes
LOOP = 8.0                            # the bank's wingbeats: 14 beats, 1.75 a second

ROAR = "sfx_100_v2:misc_24"           # a real roar: a throat at 200 Hz
GROAN = "sfx_100_v2:misc_25"          # a real groan and wail
CLOTH = src.R("cloth1", "cloth2")     # heavy cloth whacked and flapped
LEATHER = "kenney_rpg-audio:dropLeather"   # a leather drop: a heavy flap of air
RUSTLE = src.R("cloth3", "cloth4")    # cloth rustling
CREAK = src.R("creak1", "creak3")     # real wood creaks, small and tight
WHIP = src.S("switch_01", "switch_02")   # a switch cracking through the air
RUSH = "sfx_100_v2:misc_37"           # a car rushing past: air, broad and long


# ---- Shared parts ----------------------------------------------------------------------------------------------------

def overhead(x, rng, wet=0.08, tail=0.5):
    """Heard from 20-35 m overhead in open air: a little of the night's reverb (nothing to reflect off up there but the
    train), the air taking the very top off, the tail cut short so the next call doesn't pile onto it."""
    x = dsp.trim_silence(x, -60)
    y = dsp.room(lp(x, 9000), "night", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def syrinx(rng, pts, length, shape, vowels="a", pulse=30.0, rasp=0.85, two=0.0, ratio=(1.3, 1.45), sub=0.3,
           tract=TRACT, drive=8.0, jitter=0.02, oq=0.5, gape=0.0, gate=0.6):
    """The Gannet's voice. A bird sings from a syrinx, two vibrating sides where the windpipes meet, not a larynx: pitch
    from `pts` [(t, Hz)], envelope `shape`. The rasp is the voice and a breath noise gated together in pulses, `pulse` a
    second (a constant or a curve), `rasp` how deep. `two` mixes in the syrinx's other side at an inharmonic, wandering
    `ratio`: the two notes beat into a raw, broken sound. Then up a long neck and out through the bill's hollow, driven
    hard. `gape` (a multiple of the pitch) opens the bill as the note climbs: a bird tunes its gape to its note, so a
    resonance follows the pitch up. `gate` how deep the pulses cut again after the drive."""
    n = samples(length)
    sh = dsp.fit(env(shape, length), n)
    f0 = env(pts, length, "exp")
    g = synth.glottis(f0, length, rng, jitter=jitter, shimmer=0.25, sub=sub, oq=oq)
    if two:
        w = lp(rng.standard_normal(n + SR).astype(np.float32), 3)[:n]
        r = ratio[0] + (ratio[1] - ratio[0]) * (0.5 + 0.5 * w / (np.max(np.abs(w)) + 1e-9))
        g = mix(g, synth.glottis(f0 * r, length, rng, jitter=jitter * 1.5, shimmer=0.3, oq=oq * 0.9) * two)
    fr = fry(rng, pulse, length, jitter=0.3, decay=0.009)
    rms = float(np.sqrt(np.mean(g ** 2)))
    x = g * (1 - rasp + rasp * fr) + hp(synth.noise(length, rng), 700) * fr * rms * 1.6 * rasp
    v = synth.tract(x, vowels, tract, breath=0.15, rng=rng, extra=[(f / tract, q, gn) for f, q, gn in BEAK])
    # a bird's voice is bright for its size: the bill radiates the upper harmonics, the neck's soft walls damp the low
    v = dsp.peak(dsp.peak(hp(v, 400), 1500, 1.0, 6), 2600, 1.0, 7)
    if gape:
        v = mix(unit(v), unit(dsp.sweep_filter(v, "bp", f0 * gape, q=3, block=64)) * 1.2)
    v = dsp.saturate(unit(v * sh), drive)
    # the pulses again after the drive, which would otherwise fill the gaps between them: the rasp stays a rasp
    return lp(v * (1 - gate * rasp + gate * rasp * fr), 7000)


def clap(rng, f=2200):
    """The hard 'k' a syllable starts on: the syrinx slapping shut and open, a click in the bill's hollow."""
    x = burst(rng, 0.003, attack=0.1)
    return unit(ring(x, [f, f * 1.45, f * 0.62], q=6, tail=0.03))


def quills(rng, length, rate, shape):
    """Flight-feather shafts creaking as the stroke loads them: stick-slip where the vanes rub, rung through the shafts'
    hollow modes, three feathers each a little different."""
    b = np.zeros(samples(length), np.float32)
    for i in range(3):
        body = [f * rng.uniform(0.85, 1.15) for f in QUILL]
        c = synth.creak(length, rate * rng.uniform(0.8, 1.25), rng, body=body, q=rng.uniform(10, 16), jitter=0.5, grit=0.4)
        b += c * rng.uniform(0.4, 1.0)
    return unit(b) * dsp.fit(env(shape, length), len(b))


def primaries(rng, length, speed, lo=700, hi=3800, flutter=(55, 110), depth=0.55):
    """Air torn through the long flight feathers: noise whose band climbs with the wing's speed (`speed` 0-1, a curve),
    the trailing vanes fluttering in it (a buzz in the rush) hardest when it's fastest."""
    n = samples(length)
    sp = np.clip(synth.curve(speed, n), 0, 1)
    air = dsp.sweep_filter(synth.noise(length, rng, "pink"), "bp", lo * (hi / lo) ** sp, q=0.9)
    fl = fry(rng, flutter[0] + (flutter[1] - flutter[0]) * sp, length, jitter=0.4, decay=0.004)
    return unit(air * (1 - depth * sp + depth * sp * fl)) * sp ** 2


def wingbeat(rng, length, heavy=0.0, creak=1.0):
    """One stroke of a 7 m wing: the downstroke loads the shafts (a creak), drives the air through the primaries (a rush
    climbing with its speed, fluttering at the top) and, if `heavy`, shoves a deep whump of air before it; the upstroke
    comes back softer, the coverts rustling."""
    down = length * rng.uniform(0.5, 0.6)
    up = length - down
    speed = env([(0, 0.02), (down * 0.6, 1), (down * 0.85, 0.25), (down, 0.06), (down + up * 0.45, 0.3), (length, 0)],
                length)
    b = Bus(length + 0.2)
    b.at(0, primaries(rng, length, speed), 0)
    cr = 20 * np.log10(creak + 1e-3)
    b.at(0, quills(rng, 0.16, rng.uniform(70, 110), [(0, 0), (0.06, 1), (0.16, 0)]), -10 + cr)
    b.at(down * 0.8, quills(rng, 0.1, rng.uniform(120, 170), [(0, 0), (0.02, 1), (0.1, 0)]), -13 + cr)
    cov = synth.rustle(up, 900, rng, f=(2000, 7000), shape=env([(0, 0), (up * 0.4, 1), (up, 0)], up), ticks=0.5)
    b.at(down, unit(cov), -20)
    if heavy:
        sp = synth.curve(speed, samples(length))
        whump = bp(synth.noise(length, rng, "pink"), 150, 600) * np.clip(sp, 0, 1) ** 2
        b.at(0, unit(whump), -6 + 20 * np.log10(heavy))
    return b.x


def bend(x, semis):
    """Varispeed along a curve of semitones (per output sample): a held cry pulled up in pitch as it goes."""
    c = synth.curve(semis, len(x))
    n = int(len(x) * 2 ** (max(-float(np.min(c)), 0) / 12)) + 2
    r = 2 ** (synth.curve(c, n) / 12)
    pos = np.concatenate([[0.0], np.cumsum(r)[:-1]])
    pos = pos[pos < len(x) - 1]
    return np.interp(pos, np.arange(len(x)), x).astype(np.float32)


def clip(key, length=0.2, semis=0.0, start=None, pre=0.003):
    """A slice of a recording from its first strong attack (or from `start` s), varispeeded, with short fades."""
    x = src.get(key)
    if start is None:
        start = max(0.0, int(np.argmax(np.abs(x) > 0.3 * np.max(np.abs(x)))) / SR - pre)
    y = dsp.trim(x, start, length)
    if semis:
        y = dsp.vari(y, semis)
    return dsp.fade(y, 0.002, min(0.03, len(y) / SR / 3))


# ---- Presence: calls overhead (1-3 kHz) and a wingbeat (800 Hz-5 kHz) -------------------------------------------------

PATTERNS = [(0.34, 0.3), (0.26, 0.2, 0.3), (0.22, 0.2, 0.2, 0.28), (0.3, 0.16)]   # syllable lengths: arr-arr, kurr-uk ...


def arr(rng, L, f0, pulse, last=False):
    """One syllable: a hard clap, then the rasp, its pitch lifting and falling away, 'o' opening to 'ah'; the last of a
    call drops lower and drags."""
    drop = 0.72 if last else 0.82
    pts = [(0, f0 * 0.85), (L * 0.25, f0 * 1.08), (L, f0 * drop)]
    vow = [(0, "o"), (L * 0.2, "a"), (L * 0.75, "a"), (L, "o")]
    shape = [(0, 0), (0.012, 1), (L * 0.45, 0.9), (L * 0.72, 0.45), (L * 0.86, 0.06), (L, 0)]
    p = env([(0, pulse * 1.12), (L, pulse * (0.75 if last else 0.9))], L)
    v = syrinx(rng, pts, L, shape, vow, pulse=p, rasp=0.9, two=0.25, sub=0.35)
    b = Bus(L + 0.05)
    b.at(0, v, 0)
    b.at(0, clap(rng, rng.uniform(1900, 2600)), -9)
    return b.x


@recipe("tell-gannet-calls", "call", "rasp",
        "A harsh pulsed 'arrr-arrr' overhead, slowed and huge: a seabird's rasp from a throat the size of a man",
        """A gannet's colony call grown to this one's size, synthesised: each syllable starts on a hard clap of the
        syrinx and is a rasp, the voice (180-240 Hz, its two sides beating) broken into pulses 25-35 times a second
        with breath in the same pulses, pitched up and falling away, up a long neck and out through a metre of bill.
        Two to four syllables a call ('arrr-arrr', 'kurr-uk', a run of four), the last dropping lower. Driven hard so the
        rasp's harmonics reach 3 kHz, over the train. Held to 1-3 kHz.""",
        takes=4, band=CALL)
def calls_call_rasp(rng, k):
    pat = PATTERNS[k % 4]
    f0 = rng.uniform(180, 240)
    pulse = rng.uniform(25, 33)
    b = Bus(sum(pat) + 0.6)
    t = 0.0
    for i, L in enumerate(pat):
        L *= rng.uniform(0.92, 1.1)
        b.at(t, arr(rng, L, f0 * rng.uniform(0.95, 1.05), pulse, i == len(pat) - 1), -1.5 * (i % 2))
        t += L + rng.uniform(0.08, 0.14)
    return held(overhead(b.x, rng, tail=0.3), *CALL, drive=4)


def roar_syllable(rng, L, semis, pulse):
    """A real roar's hardest stretch, dropped to the call's pitch and cut to a syllable, broken into the rasp's pulses
    (and noise in them, so the pulses rasp rather than throb) and rung through the bill's hollow."""
    x = dsp.trim_silence(src.get(ROAR), -30)
    a = rng.uniform(0.12, 0.2)
    y = dsp.vari(dsp.trim(x, a, L * 2 ** (semis / 12) + 0.02), semis)[:samples(L)]
    n = len(y)
    fr = fry(rng, env([(0, pulse * 1.1), (L, pulse * 0.85)], L), L, jitter=0.3, decay=0.008)[:n]
    rms = float(np.sqrt(np.mean(y ** 2)))
    z = y * (0.1 + 0.9 * fr) + hp(synth.noise(L, rng), 600)[:n] * fr * rms * 1.5
    z = mix(z, unit(dsp.resonate(z, [f for f, _, _ in BEAK], q=6)) * rms * 2)
    z = dsp.peak(hp(z, 300), 2200, 0.7, 9)
    return dsp.shaped(dsp.saturate(unit(z), 10), [(0, 0), (0.01, 1), (L * 0.5, 0.8), (L * 0.75, 0.4), (L * 0.88, 0.06), (L, 0)])


@recipe("tell-gannet-calls", "call", "roar",
        "The same pulsed call kitbashed: a real roar cut into syllables and broken into a rasp of 25-35 pulses a second",
        """A real big-cat roar (a throat at about 200 Hz) cut into two to four short syllables, each dropped a little
        further, broken into pulses 25-35 times a second with a hiss of breath in every pulse (the rasp that makes it a
        gannet's 'arrr' and not a roar), rung through the bill's hollow at 1.9 and 2.7 kHz and driven hard. Each starts on
        the syrinx's hard clap. The same patterns as 'rasp' ('arrr-arrr', 'kurr-uk', a run of four), so the two compare as
        recording against model. Held to 1-3 kHz.""",
        sources=[ROAR], takes=4, band=CALL)
def calls_call_roar(rng, k):
    pat = PATTERNS[k % 4]
    pulse = rng.uniform(25, 33)
    b = Bus(sum(pat) + 0.6)
    t = 0.0
    for i, L in enumerate(pat):
        L *= rng.uniform(0.92, 1.1)
        semis = rng.uniform(-2, 0) - (2.5 if i == len(pat) - 1 else 0)
        b.at(t, roar_syllable(rng, L, semis, pulse), -1.5 * (i % 2))
        b.at(t, clap(rng, rng.uniform(1900, 2600)), -10)
        t += L + rng.uniform(0.08, 0.14)
    return held(overhead(b.x, rng, tail=0.3), *CALL, drive=4)


@recipe("tell-gannet-calls", "wings", "stroke",
        "One great wingbeat overhead: air torn through big primaries, the feather shafts creaking as the stroke loads",
        """Modelled from a wing 7 m across: the downstroke loads the flight feathers' shafts (stick-slip where the vanes
        rub, rung through a hollow quill's modes), then drives the air through the primaries (noise whose band climbs
        with the wing's speed, the trailing vanes fluttering in it at the top of the stroke); the shafts creak again as
        it turns, and the upstroke comes back softer with the coverts rustling. One beat, 0.5-0.9 s. Held to
        800 Hz-5 kHz.""",
        takes=3, band=WINGS)
def calls_wings_stroke(rng, k):
    L = (0.58, 0.7, 0.64)[k % 3] * rng.uniform(0.95, 1.05)
    return held(overhead(wingbeat(rng, L), rng, tail=0.15), *WINGS, drive=3)


def canvas_beat(rng, L, heavy=0.0):
    """A wingbeat out of real cloth: a heavy flap whacked and dropped to a wing's size, the air it shoves (a leather
    drop's whoosh, lowered), a real creak cut short and pitched up for the shafts, cloth rustling as it comes back."""
    b = Bus(L + 0.4)
    down = L * rng.uniform(0.5, 0.6)
    flap = clip(CLOTH[int(rng.integers(2))], 0.3, rng.uniform(-6, -3))
    b.at(0.02, dsp.shaped(unit(flap), [(0, 0), (down * 0.5, 1), (down, 0.3), (0.3, 0)]), -2)
    air = clip(LEATHER, 0.35, rng.uniform(-5, -2), start=0.05)
    b.at(0.0, dsp.shaped(unit(air), [(0, 0), (down * 0.6, 1), (0.35, 0)]), -4)
    b.at(0.0, primaries(rng, L, env([(0, 0.05), (down * 0.55, 1), (down, 0.3), (L, 0)], L)), -6)
    shaft = clip(CREAK[int(rng.integers(2))], 0.1, rng.uniform(1, 4))
    b.at(0.0, unit(hp(shaft, 900)), -15)
    rus = RUSTLE[int(rng.integers(2))]
    b.at(down, dsp.shaped(unit(hp(clip(rus, L - down, rng.uniform(2, 5), start=0.05), 1500)), [(0, 0), (0.05, 1), (L - down, 0)]),
         -12)
    if heavy:
        whump = bp(synth.noise(down, rng, "pink"), 150, 600) * env([(0, 0), (down * 0.55, 1), (down, 0)], down) ** 2
        b.at(0, unit(whump), -6 + 20 * np.log10(heavy))
    return b.x


@recipe("tell-gannet-calls", "wings", "canvas",
        "One great wingbeat built from real cloth: a heavy flap dropped to a wing's size, its air, a shaft's creak",
        """Kitbashed: a real heavy cloth whacked and flapped, dropped a few semitones to the size of a wing 7 m across,
        with the air it shoves (a real leather drop's whoosh, lowered) and the primaries' rush over it; a real wood creak
        cut to a tenth of a second and pitched up for the shafts loading; real cloth rustling as the wing comes back up.
        One beat, 0.5-0.9 s. Held to 800 Hz-5 kHz.""",
        sources=CLOTH + [LEATHER] + CREAK + RUSTLE, takes=3, band=WINGS)
def calls_wings_canvas(rng, k):
    L = (0.58, 0.7, 0.64)[k % 3] * rng.uniform(0.95, 1.05)
    return held(overhead(canvas_beat(rng, L), rng, tail=0.15), *WINGS, drive=3)


# ---- The fold: a crack of wings (1-6 kHz), then the whistle (2-5 kHz, 1.6 s) -----------------------------------------

@recipe("tell-gannet-fold", "crack", "whip",
        "The wings snapped back: a sharp crack like a sail snapping taut, the feathers rattling as they lock",
        """Kitbashed from real air and cloth: a switch cracking through the air (the shock as 7 m of wing snaps back),
        dropped a little for its size, on top of a heavy cloth's whack (a sail filling taut) cut short; the shove of air
        behind it, and the feathers rattling into place as they lock. Short and sharp. Held to 1-6 kHz.""",
        sources=WHIP + CLOTH, takes=3, band=CRACK)
def fold_crack_whip(rng, k):
    b = Bus(0.5)
    w = clip(WHIP[k % 2], 0.12, rng.uniform(-4, -2))
    b.at(0.03, unit(w), 0)
    c = clip(CLOTH[(k + 1) % 2], 0.1, rng.uniform(-3, 0))
    b.at(0.028, dsp.shaped(unit(c), [(0, 1), (0.1, 0)], "exp"), -5)
    b.at(0.0, primaries(rng, 0.06, env([(0, 0.3), (0.05, 1), (0.06, 0.4)], 0.06)), -10)
    rat = synth.skitter(0.1, 160, rng, f=(1800, 4500), q=(4, 8), legs=8, shape=env([(0, 1), (0.1, 0)], 0.1))
    b.at(0.05, unit(rat), -15)
    return held(overhead(b.x, rng, 0.1, 0.1), *CRACK, drive=4)


def snap_taut(rng, f=1100):
    """Canvas snapping taut: a short luff (the slack cloth fluttering, quickening), then the jolt, a membrane's modes
    struck all at once and dying fast, with the air's crack on top."""
    b = Bus(0.4)
    luff = rng.uniform(0.06, 0.09)
    fl = fry(rng, env([(0, 45), (luff, 100)], luff), luff, decay=0.004)
    b.at(0, unit(bp(synth.noise(luff, rng), 800, 3500) * fl) * env([(0, 0.2), (luff, 1)], luff), -10)
    x = burst(rng, 0.0015, attack=0.05)
    body = ring(x, [f * m for m in (1.0, 1.59, 2.14, 2.3, 2.65, 2.92, 3.5)], q=rng.uniform(7, 11), tail=0.09)
    b.at(luff, dsp.shaped(unit(body), [(0, 1), (0.09, 0)], "exp"), 0)
    b.at(luff, unit(hp(burst(rng, 0.012, attack=0.05), 2500)), -4)
    return b.x


@recipe("tell-gannet-fold", "crack", "sail",
        "The wings snapped back, modelled: a short luff, then a big canvas snapping taut, its jolt ringing for a moment",
        """Modelled as a sail filling in a gust: the slack wings flutter for a tenth of a second, quickening, then snap
        taut: a jolt that strikes a stretched membrane's modes all at once (round 1-3.5 kHz) and dies within a tenth of a
        second, with the air's crack on top and the feathers rattling as they lock. Held to 1-6 kHz.""",
        takes=3, band=CRACK)
def fold_crack_sail(rng, k):
    b = Bus(0.5)
    b.at(0, snap_taut(rng, rng.uniform(950, 1300)), 0)
    rat = synth.skitter(0.1, 180, rng, f=(1800, 5000), q=(4, 8), legs=8, shape=env([(0, 1), (0.1, 0)], 0.1))
    b.at(0.12, unit(rat), -16)
    return held(overhead(b.x, rng, 0.1, 0.1), *CRACK, drive=3)


WHISTLE_L = 1.6


def stoop_speed(rng, n):
    """Its speed through the 1.6 s of the drop as a 0-1 curve: gathering like a falling stone, the body yawing a little."""
    t = np.arange(n) / n
    yaw = lp(rng.standard_normal(n + SR).astype(np.float32), 5)[:n]
    return (t ** 1.25 * (1 + 0.02 * yaw / (np.max(np.abs(yaw)) + 1e-9))).astype(np.float32)


@recipe("tell-gannet-fold", "whistle", "dive",
        "The air whistling past a folded dart as it drops: a noisy, airy whistle rising 2-5 kHz, loudest at the end",
        """Modelled from the dive: as its speed gathers the air past the bill and the folded wingtips whistles, noise
        through a narrow resonance that climbs from 2 to 4.8 kHz across the 1.6 s (a falling-bomb whistle, never a
        tone), a fainter edge-tone above it, the broad rush of air growing under both and the wingtips buzzing faster as
        it goes. It crescendos to the end, where the strike is. High, rising and airy: nothing like the train's steady
        whistle at 200-800 Hz. Exactly 1.6 s. Held to 2-5 kHz.""",
        takes=3, band=WHISTLE)
def fold_whistle_dive(rng, k):
    L = WHISTLE_L
    n = samples(L)
    sp = stoop_speed(rng, n)
    lo, hi = rng.uniform(1950, 2150), rng.uniform(4600, 4900)
    fc = lo * (hi / lo) ** sp
    q = rng.uniform(14, 20)
    tone = dsp.sweep_filter(synth.noise(L, rng), "bp", fc, q=q, block=64)
    edge = dsp.sweep_filter(synth.noise(L, rng), "bp", fc * rng.uniform(1.17, 1.24), q=q * 0.8, block=64)
    rush = bp(synth.noise(L, rng, "pink"), 1800, 6000)
    buzz = fry(rng, 40 + 90 * sp, L, jitter=0.3, decay=0.004)
    y = mix(unit(tone), unit(edge) * 0.35, unit(rush) * (0.25 + 0.35 * sp) * (0.7 + 0.3 * buzz))
    y = y * (0.85 + 0.15 * buzz)
    rise = 10 ** ((-26 + 26 * sp ** 1.6) / 20)
    y = dsp.fit(unit(y) * rise, n)
    return dsp.fit(held(y, *WHISTLE, drive=3), n)


@recipe("tell-gannet-fold", "whistle", "rush",
        "A real rush of air sped up into the dive, a resonance whistling up through it 2-5 kHz, the wingtips buzzing",
        """Kitbashed: a real car's rush of air going by, varispeeded up an octave into the dive's 1.6 s and reshaped to
        swell to the end, with a resonance swept up through it from 2 to 4.8 kHz (the whistle is the rush
        itself, ringing) and the folded wingtips buzzing faster and faster in it. Airier and rougher than 'dive'. Loudest
        at the very end, where the strike is. Exactly 1.6 s. Held to 2-5 kHz.""",
        sources=[RUSH], takes=3, band=WHISTLE)
def fold_whistle_rush(rng, k):
    L = WHISTLE_L
    n = samples(L)
    semis = rng.uniform(11.8, 12.2)       # its 3.2 s of steady rush, an octave up, is the 1.6 s of the dive
    x = dsp.vari(dsp.trim(src.get(RUSH), rng.uniform(0.4, 0.5), L * 2 ** (semis / 12) + 0.05), semis)
    x = dsp.fit(hp(x, 1500), n)
    x = x / (lp(np.abs(x), 4) + 0.05 * np.max(np.abs(x)))     # its own pass-by swell flattened: the dive sets the shape
    sp = stoop_speed(rng, n)
    fc = rng.uniform(1950, 2150) * (rng.uniform(4600, 4900) / 2050) ** sp
    whistle = dsp.sweep_filter(x, "bp", fc, q=rng.uniform(7, 10), block=64)
    buzz = fry(rng, 35 + 100 * sp, L, jitter=0.35, decay=0.005)
    y = mix(unit(whistle), unit(x) * 0.45 * (0.6 + 0.4 * buzz))
    rise = 10 ** ((-24 + 24 * sp ** 1.5) / 20)
    return dsp.fit(held(unit(y) * rise, *WHISTLE, drive=3), n)


# ---- The bank: the scream (600 Hz-4 kHz) and the wingbeats closing (150 Hz-4 kHz) ------------------------------------

@recipe("tell-gannet-bank", "scream", "scream",
        "A long rising seabird scream from something huge: raw, two-voiced, climbing in pitch and force",
        """Synthesised from its syrinx: both sides sounding at once a wandering, inharmonic interval apart, so the
        scream beats and breaks like a real bird's; it starts as the call's rasp, opens into a scream climbing from
        about 330 to 800 Hz across two and more seconds, growing harsher (period doubling, a rough flutter) and louder as
        it climbs, its upper harmonics ringing in the bill's hollow at 2-3 kHz. Piercing, raw, rising: 'it's coming for
        you'. Held to 600 Hz-4 kHz.""",
        takes=3, band=SCREAM)
def bank_scream(rng, k):
    L = rng.uniform(1.95, 2.15)
    f0, top = rng.uniform(310, 350), rng.uniform(760, 880)
    pts = [(0, f0 * 0.8), (0.18, f0), (L * 0.6, (f0 * top) ** 0.5 * 1.05), (L * 0.92, top), (L, top * 0.94)]
    vow = [(0, "o"), (0.2, "a"), (L * 0.6, "e"), (L, "a")]
    shape = [(0, 0), (0.02, 0.6), (0.2, 0.5), (L * 0.55, 0.75), (L * 0.92, 1.0), (L, 0)]
    pulse = env([(0, 28), (0.25, 40), (L, 60)], L)
    v = syrinx(rng, pts, L, shape, vow, pulse=pulse, rasp=env([(0, 0.85), (0.3, 0.3), (L, 0.45)], L), two=0.45,
               ratio=(1.18, 1.42), sub=0.4, tract=0.72, drive=10, jitter=0.03, oq=0.4, gape=3)
    b = Bus(L + 0.1)
    b.at(0, v, 0)
    b.at(0, clap(rng, 2300), -8)
    return held(overhead(b.x, rng, 0.12, 0.25), *SCREAM, drive=4)


@recipe("tell-gannet-bank", "scream", "wail",
        "The scream kitbashed: a real roar and wail held out into one long cry and pulled up an octave, torn rough",
        """A real roar and a real wail, each held out into a long cry (paulstretched, so they keep their grain without
        their syllables), pitched up into a bird's register and pulled up nearly an octave more across the 2 s
        (varispeed along a rising curve), laid together a little out of tune so they beat; broken with a rough flutter
        that gets faster as it climbs, rung through the bill's hollow (one resonance following the pitch up, as a bird
        opens its gape to its note) and driven hard. Starts on the call's rasp so it's the same bird. Held to 600 Hz-4 kHz.""",
        sources=[ROAR, GROAN], takes=3, band=SCREAM)
def bank_wail(rng, k):
    L = rng.uniform(1.95, 2.15)
    n = samples(L)
    rise = env([(0, 0), (0.2, 1), (L * 0.9, 10), (L, 9.5)], L * 1.6)   # semitones, over the source's own time
    parts = []
    up = rng.uniform(9, 11)
    for key, semis in ((ROAR, up), (GROAN, rng.uniform(1, 3))):
        x = dsp.trim(dsp.trim_silence(src.get(key), -30), rng.uniform(0.04, 0.1), 0.22)
        held_ = dsp.smear(x, L * 1.7 / 0.22, 0.12, rng)
        y = dsp.fit(bend(dsp.shift(held_, semis), rise), n)
        parts.append(unit(y / (lp(np.abs(y), 6) + 0.05 * np.max(np.abs(y)))))    # its own swell flattened
    y = mix(parts[0], parts[1] * 0.6)
    fl = fry(rng, env([(0, 28), (L, 70)], L), L, decay=0.006)
    y = y * (0.55 + 0.45 * fl)
    y = mix(y, unit(dsp.resonate(y, [f for f, _, _ in BEAK], q=6)) * 0.6)
    gape = 3 * 200 * 2 ** ((up + dsp.fit(rise, n)) / 12)      # the bill opening as it climbs: the roar's third harmonic
    y = mix(unit(y), unit(dsp.sweep_filter(y, "bp", gape, q=3, block=64)) * 1.2)
    shape = env([(0, 0), (0.03, 0.6), (0.25, 0.55), (L * 0.6, 0.8), (L * 0.9, 1.0), (L, 0)], L)
    y = dsp.saturate(unit(y) * shape, 10)
    b = Bus(L + 0.1)
    b.at(0, y, 0)
    b.at(0, arr(rng, 0.22, rng.uniform(200, 230), 30), -6)
    return held(overhead(b.x, rng, 0.12, 0.25), *SCREAM, drive=4)


@recipe("tell-gannet-bank", "wingbeats", "beat",
        "Heavy wingbeats closing low along the train, 1.75 a second: a deep whump of air, the feathers' rush and creak",
        """Fourteen strokes of a wing 7 m across in one 8 s cycle, a steady 1.75 a second: each a deep whump of air shoved
        down (low noise swelling with the stroke, never a sub), the primaries' rush climbing with the wing's speed and
        fluttering at the top of the stroke, the shafts creaking as it loads and turns, the coverts rustling on the way
        back up. Strokes vary a little in strength and timing, as a real bird's do. Seamless. Held to 150 Hz-4 kHz.""",
        takes=1, loop=True, band=BEATS, seconds=LOOP)
def bank_wingbeats_beat(rng, k):
    b = Bus(LOOP + 1.0)
    period = LOOP / 14
    for i in range(14):
        t = 0.02 + i * period + rng.uniform(-0.012, 0.012)
        b.at(t, wingbeat(rng, period * rng.uniform(0.92, 1.0), heavy=1.0, creak=rng.uniform(0.6, 1.0)), rng.uniform(-1.5, 0))
    return cycle(b.x, LOOP, rng, BEATS, drive=4, wet=0.1)


@recipe("tell-gannet-bank", "wingbeats", "canvas",
        "The heavy wingbeats from real cloth: a great flap dropped to a wing's size each beat, its air, the shafts creaking",
        """Fourteen beats in an 8 s cycle, 1.75 a second, each kitbashed: a real heavy cloth whacked and flapped, dropped a
        few semitones to a 7 m wing, a deep whump of air under it (low noise, never a sub), a real leather drop's whoosh
        for the air it shoves, the primaries' rush over it, a real creak cut short and pitched up for the shafts, cloth
        rustling as it comes back up. Seamless. Held to 150 Hz-4 kHz.""",
        sources=CLOTH + [LEATHER] + CREAK + RUSTLE, takes=1, loop=True, band=BEATS, seconds=LOOP)
def bank_wingbeats_canvas(rng, k):
    b = Bus(LOOP + 1.0)
    period = LOOP / 14
    for i in range(14):
        t = 0.02 + i * period + rng.uniform(-0.012, 0.012)
        b.at(t, canvas_beat(rng, period * rng.uniform(0.92, 1.0), heavy=1.0), rng.uniform(-1.5, 0))
    return cycle(b.x, LOOP, rng, BEATS, drive=4, wet=0.1)
