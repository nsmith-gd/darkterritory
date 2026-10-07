"""The noisy toys a crewmate can carry (crew-noisy-toys: a rubber squeaker, a wind-up music box, a wind-up tin drummer,
GDD §19, App. C item 4) and the stranded ending's last sounds (ui-stranded-outro, App. E.9: the dead boiler ticking as it
cools, the cars' oil lamps guttering out one by one).

All of it is gameplay foley, so each must be plainly the real thing. The packs have no toys, flames or cooling iron, so
each object is built from what it's made of: real recordings where the packs hold that material (tin cans for the drum,
small steel and glass for pings and the lamp chimney, hinge squeals for squeaking rubber, ratchets and a motor for
clockwork, leather for a hand on rubber, wood for the music box's case) and small physical models where they don't (a
squeaker's reed blown by the squeeze, a music box comb's clamped steel teeth, a tin drumhead's modes, a boiler shell's
rings, a lamp flame's flutter). Two candidates per cue take different routes, one leaning on recordings and one on the
model, so the director can hear which reads truer.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import SR, samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes.kit import MEMBRANE, hz

# build.build_one crossfades a loop's last 0.3 s into its head (equal power).
SEAM = 0.3


def seamless(y, n):
    """One exact cycle of a loop whose events and tails run past its end: fold the overrun onto the head (dsp.wrap),
    high-pass it as a periodic signal (no DC, no filter start-up at the seam), then hand build.py a cycle plus a copy of
    its head, both pre-scaled so build's equal-power seam crossfade gives back exactly the cycle (a crossfade of a signal
    with itself would otherwise swell 3 dB, and a beat would double or drop at the seam of a rhythmic loop)."""
    c = dsp.wrap(y, n)
    c = hp(np.concatenate([c, c, c]), 25, 2)[n:2 * n]
    m = samples(SEAM)
    t = np.linspace(0, np.pi / 2, m)
    g = (1 / (np.sin(t) + np.cos(t))).astype(np.float32)
    head = c[:m] * g
    return np.concatenate([head, c[m:], head]).astype(np.float32)


def glide(x, ratio):
    """Read a recording at a speed that moves (`ratio` a pitch ratio per output sample): a tape pushed faster and slower
    by hand, so a held tone follows a pitch contour. Stretched first (pitch kept) if it's too short to last."""
    pos = np.cumsum(ratio) - ratio[0]
    if len(x) < pos[-1] + 2:
        x = dsp.stretch(x, (pos[-1] + 2) / len(x) * 1.05, smooth=True)
    return np.interp(pos, np.arange(len(x)), x).astype(np.float32)


def unit(x):
    return ck.norm(x, 0.0)


# ---- The squeaker ---------------------------------------------------------------------------------------------------------
# A squeeze toy: a hollow rubber body with a reed whistle in its base. Squeezing drives the air out through the reed (the
# squeak, its pitch riding the pressure: up as the fingers clamp, sagging as the air runs out); letting go, the rubber
# springs back and sucks air in through the same reed (a weaker, lower, breathier wheeze). The reed only speaks above a
# threshold, so each end of a squeak is a hiss, and it squawks as it catches and lets go.

HAND = ck.R("handleSmallLeather", "handleSmallLeather2")


def reed(rng, p, f0, squawk=0.5, air=0.35):
    """A squeaker's reed blown by the pressure curve `p` (0..1 a sample). A beating reed lets air through in short
    puffs, one a cycle: narrow pulses, narrower (brighter) the harder it's driven; while it's barely driven every other
    cycle falters (period doubling: the squawk). Turbulent air rides the puffs and hisses on below the threshold."""
    n = len(p)
    drive = np.clip((p - 0.2) / 0.8, 0, 1).astype(np.float32)
    flutter = lp(rng.standard_normal(n).astype(np.float32), 22)
    flutter /= np.max(np.abs(flutter)) + 1e-9
    f = f0 * (0.8 + 0.3 * p) * (1 + 0.01 * flutter)
    ph = np.cumsum(f) / SR
    cyc = np.floor(ph)
    width = 0.11 - 0.05 * drive
    pulse = np.exp(-0.5 * ((ph - cyc - 0.5) / width) ** 2).astype(np.float32)
    pulse *= 1 - squawk * np.clip(1 - drive * 3, 0, 1) * (cyc % 2)
    tone = hp(pulse, 400, 2) * drive ** 0.7
    hiss = bp(rng.standard_normal(n).astype(np.float32), 1200, 9000) * (0.25 + 0.75 * pulse) * np.sqrt(np.clip(p, 0, 1))
    y = tone / (np.max(np.abs(tone)) + 1e-9) + hiss / (np.max(np.abs(hiss)) + 1e-9) * air
    # the whistle's little horn and the toy's hollow
    body = dsp.resonate(y, [f0 * 1.7, f0 * 2.7, f0 * 4.1], q=6, gains=[1, 0.6, 0.3])
    return lp(unit(y) * 0.6 + unit(body) * 0.4, 9000, 2)


def rubber(rng, length, size=1.0):
    """The rubber body under the fingers: a soft low whump as the hand closes, a squeak of skin on the rubber (stick-slip
    through the toy's soft walls), the walls popping back when let go."""
    whump = lp(synth.noise(0.09, rng, "pink"), 260 / size, 2) * env([(0, 0), (0.008, 1), (0.09, 0)], 0.09) ** 1.5
    skin = synth.creak(length, env([(0, 180), (length, 320)], length), rng, body=[x / size for x in (520, 1130, 1900, 2800)],
                       q=7, jitter=0.4, shape=env([(0, 0), (length * 0.3, 1), (length, 0)], length))
    pop = lp(synth.noise(0.05, rng, "pink"), 420 / size, 2) * env([(0, 0), (0.004, 1), (0.05, 0)], 0.05)
    return unit(whump), unit(skin), unit(pop)


def pressures(rng, hold, refill, force):
    """A squeeze's pressure in the toy (clamp, hold while the air runs out, release) and the suction of its refill
    (sharp as the walls spring back, dying as the toy fills), each 0..1 a sample, and when each starts."""
    clamp = rng.uniform(0.03, 0.07)
    L1 = clamp + hold + 0.05
    p1 = lp(env([(0, 0), (clamp, force), (clamp + hold * 0.4, force * rng.uniform(0.85, 1.0)),
                 (clamp + hold, force * rng.uniform(0.5, 0.75)), (L1, 0)], L1), 40, 1)
    p2 = lp(env([(0, 0), (0.02, force * rng.uniform(0.6, 0.8)), (refill * 0.45, force * 0.42), (refill, 0)], refill), 40, 1)
    return p1, p2, clamp, L1 + rng.uniform(0.03, 0.08)


def squeak_reed(rng, hold, refill, f0, force):
    """One squeeze and let-go of the small toy, its reed modelled: the squeak, a short gap as the walls spring back, the
    wheeze of the refill. The hand gripping is real leather handling."""
    p1, p2, clamp, back = pressures(rng, hold, refill, force)
    L1 = len(p1) / SR
    gap = back - L1
    sq = reed(rng, p1, f0 * rng.uniform(0.98, 1.02), squawk=rng.uniform(0.3, 0.7))
    wh = reed(rng, p2, f0 * 0.74, squawk=0.8, air=0.8)
    whump, skin, pop = rubber(rng, min(0.12, hold + 0.03))
    parts = [(0, whump, -14), (0.005, grip(rng), -15), (clamp * 0.5, sq, 0), (L1 + gap * 0.3, pop, -17), (back, wh, -9)]
    if rng.random() < 0.6:
        parts.append((0.01, skin, -19))
    return ck.place(parts)


def grip(rng):
    """The hand closing on the toy: a real leather handling, cut to one of its creaks."""
    hand = ck.get(HAND[int(rng.integers(2))])
    h = ck.hits(hand, floor_db=-12, gap=0.04)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    return unit(ck.cut(hand, a - samples(0.01), a + samples(0.12), 0.004, 0.04))


def squeezes(rng, L, slow=1.0):
    """How the carrier squeezes it over one cycle, [(t, hold, refill, force), ...] at uneven gaps: a hand fidgeting as
    it walks, mostly short squeezes, now and then a long slow one, a quick double, a pause. A harder squeeze is louder
    and higher. `slow` stretches the long ones (a bigger toy takes longer to empty and fill)."""
    out, t = [], rng.uniform(0.1, 0.4)
    kinds = ["short", "short", "long", "double", "short", "long"]
    while t < L:
        k = kinds[int(rng.integers(len(kinds)))]
        if k == "double":
            out.append((t, rng.uniform(0.07, 0.12), rng.uniform(0.12, 0.18), rng.uniform(0.75, 1.0)))
            t += rng.uniform(0.3, 0.4) * slow
            out.append((t, rng.uniform(0.08, 0.14), rng.uniform(0.18, 0.28), rng.uniform(0.7, 1.0)))
            t += 0.4 * slow
        elif k == "short":
            out.append((t, rng.uniform(0.08, 0.16), rng.uniform(0.14, 0.24), rng.uniform(0.65, 1.0)))
            t += 0.45 * slow
        else:
            out.append((t, rng.uniform(0.3, 0.55) * slow, rng.uniform(0.28, 0.45) * slow, rng.uniform(0.8, 1.0)))
            t += 1.0 * slow
        t += rng.uniform(1.6, 2.4) if rng.random() < 0.18 else rng.uniform(0.3, 1.3)
    return out


@recipe("crew-noisy-toys", "squeaker", "reed",
        "A small rubber squeeze toy, its reed whistle modelled: a bright squeak in, a breathy wheeze out, at uneven gaps",
        """The toy's reed is modelled: a squeeze drives it with a pressure curve (clamp, hold, the air running out), it
        speaks only above a threshold, its pitch rides the pressure (about 1.5 kHz) and it squawks as it catches and lets
        go. Letting go, the walls spring back and draw air in through the same reed: a lower, weaker, breathier wheeze.
        Under it the rubber: a soft whump as the hand closes, skin squeaking on the rubber (stick-slip), the walls popping
        back, and a real leather handling for the grip. Squeezes come at uneven gaps over 12 s, short, long and quick
        doubles.""",
        sources=HAND, takes=1, loop=True, seconds=12.0)
def squeaker_reed(rng, k):
    L = 12.0
    b = Bus(L + 2)
    for t, hold, refill, force in squeezes(rng, L):
        b.at(t, squeak_reed(rng, hold, refill, 1480, force), 8 * (force - 1))
    y = dsp.room(b.x, "car", wet=0.12, rng=np.random.default_rng(11))
    return seamless(y, samples(L))


# The steady stretches of the packs' hinge squeals (a squeaking hinge is stick-slip singing, the same as rubber squeaking
# on itself): (source, start, end, its pitch in Hz).
SQUEALS = [("kenney_rpg-audio:creak3", 0.04, 0.28, 1091), ("kenney_rpg-audio:creak2", 0.03, 0.19, 1155),
           ("kenney_rpg-audio:creak1", 0.37, 0.58, 1152)]


def squeal(rng, p, f0, which):
    """A real squeal sung along a squeeze's pressure: one of the hinge squeals' steady tones, read faster and slower so
    its pitch rides the pressure the way a reed's does, faded in and out where the pressure crosses the reed's
    threshold."""
    key, a, b, f_src = SQUEALS[which]
    x = ck.get(key)[samples(a):samples(b)]
    x = dsp.fade(x / (np.max(np.abs(x)) + 1e-9), 0.01, 0.01)
    x = dsp.stretch(x, 3.0, smooth=True)
    y = glide(x[int(rng.integers(samples(0.05))):], f0 * (0.82 + 0.28 * p) / f_src)
    drive = np.clip((p - 0.2) / 0.8, 0, 1) ** 0.7
    # rubber squeaks buzzier than a hinge: driven a little harder into the soft walls
    return unit(dsp.saturate(unit(hp(y, 500, 2)), 9) * drive)


def squeak_squeal(rng, hold, refill, f0, force):
    """One squeeze and let-go of the bigger, softer toy: a recorded squeal for the reed, the refill a breathy wheeze of
    air drawn back through it with the squeal low and weak inside, the hand on the rubber real leather."""
    p1, p2, clamp, back = pressures(rng, hold, refill, force)
    n1, n2 = len(p1), len(p2)
    which = int(rng.integers(len(SQUEALS)))
    air1 = bp(rng.standard_normal(n1).astype(np.float32), 1000, 7000) * np.sqrt(p1)
    sq = mix(squeal(rng, p1, f0 * rng.uniform(0.98, 1.02), which), unit(air1) * 0.22)
    air2 = bp(rng.standard_normal(n2).astype(np.float32), 700, 5000) * np.sqrt(p2)
    wh = mix(unit(air2) * 0.7, squeal(rng, p2 * 0.8, f0 * 0.68, (which + 1) % len(SQUEALS)) * 0.45)
    wh = dsp.resonate(wh, [f0 * 0.9, f0 * 1.9], q=5) * 0.4 + wh
    whump, skin, pop = rubber(rng, min(0.16, hold + 0.04), size=1.4)
    parts = [(0, whump, -16), (0.0, grip(rng), -15), (clamp * 0.5, sq, 0), (len(p1) / SR + 0.01, pop, -17),
             (back, unit(wh), -8)]
    if rng.random() < 0.7:
        parts.append((0.01, skin, -17))
    return ck.place(parts)


@recipe("crew-noisy-toys", "squeaker", "squeal",
        "A bigger, softer squeeze toy from real squeals: a lower squeak bent along the squeeze, a long wheezing refill",
        """The squeak is real: the steady stretches of the packs' hinge squeals (stick-slip singing, the same thing rubber
        does), read faster and slower so the pitch rides the squeeze's pressure (about 1 kHz, up as the hand clamps,
        sagging as the air runs out), faded where a reed would catch and let go. The refill is mostly air drawn back
        through it, the squeal low and weak inside, rung through the toy's hollow. A soft whump of the rubber, real
        leather handling for the hand, the walls popping back. A bigger toy than 'reed': slower squeezes, longer
        wheezes, uneven gaps over 13 s.""",
        sources=HAND + [s[0] for s in SQUEALS], takes=1, loop=True, seconds=13.0)
def squeaker_squeal(rng, k):
    L = 13.0
    b = Bus(L + 2.5)
    for t, hold, refill, force in squeezes(rng, L, slow=1.3):
        b.at(t, squeak_squeal(rng, hold, refill, 1000, force), 8 * (force - 1))
    y = dsp.room(b.x, "car", wet=0.12, rng=np.random.default_rng(11))
    return seamless(y, samples(L))


# ---- The music box --------------------------------------------------------------------------------------------------------
# A steel comb plucked by the pins of a turning cylinder. Each tooth is a bar clamped at one end: its modes sit at 1, 6.27
# and 17.5 times its note (inharmonic, which is the music box's glassy shimmer), the upper ones dying in a moment, the
# note ringing a second or more. Melody teeth come in pairs a few cents apart (a pin for each, so a repeated note doesn't
# re-pluck a ringing tooth), so held notes beat slowly. When a pin does come round to a tooth still ringing, the steel
# damper touches it first: a tiny buzz. The comb is screwed to a wooden case; a fan governor whirrs under it all.

# Brahms, Wiegenlied Op. 49 No. 4 (1868), its first strain in 3/4: (semitones above the tonic, beats), from the downbeat
# after the pickup, with the pickup's two quavers closing the last bar so the tune runs straight round again.
DO, RE, MI, FA, SO, LA, TI, DO2 = 0, 2, 4, 5, 7, 9, 11, 12
WIEGENLIED = [(SO, 1.5), (MI, .5), (MI, 1), (SO, 2), (MI, .5), (SO, .5), (DO2, 1), (TI, 1.5), (LA, .5),
              (LA, 1), (SO, 1), (RE, .5), (MI, .5), (FA, 1), (RE, 1), (RE, .5), (MI, .5), (FA, 2), (RE, .5), (FA, .5),
              (TI, .5), (LA, .5), (SO, 1), (TI, 1), (DO2, 2), (MI, .5), (MI, .5)]
# The bass teeth, one a beat: root, fifth, third of the tonic (bars 1, 2, 8) and the dominant seventh (bars 3-7).
TONIC, DOM7 = (-12, -5, -8), (-17, -10, -7)
HARMONY = [TONIC, TONIC, DOM7, DOM7, DOM7, DOM7, DOM7, TONIC]
BEATS = 24
CANTILEVER = (1.0, 6.27, 17.55)
WOOD = ck.K("impactWood_light")
TICKS = ["kenney_rpg-audio:handleCoins2", "sfx_100_v2:metal_04"]
MOTOR = "sfx_100_v2:loop_machine_03"


def score(rng):
    """The tune and its bass as [(beat, semitones, velocity, tooth), ...]. A melody note alternates between its pair of
    teeth each time it comes round; the bass teeth are single."""
    ev, b, turn = [], 0.0, {}
    for m, d in WIEGENLIED:
        turn[m] = turn.get(m, -1) + 1
        ev.append((b, m, rng.uniform(0.85, 1.0), (m, turn[m] % 2)))
        b += d
    for bar, chord in enumerate(HARMONY):
        for beat, m in enumerate(chord):
            ev.append((bar * 3.0 + beat, m, (0.3 if beat == 0 else 0.2) * rng.uniform(0.85, 1.1), (m, 0)))
    return sorted(ev, key=lambda e: e[0])


TEETH = sorted({e[3] for e in score(np.random.default_rng(0))})


def ring_time(f):
    """How long a tooth rings (to -60 dB): the long, lead-weighted bass teeth for seconds, the short top ones about one."""
    return float(np.clip(2.4 * (660 / f) ** 0.6, 0.9, 4.0))


def tooth(f, length, vel, r2, ring, struck=True):
    """One tooth's ring: its three clamped-bar modes, the upper two brighter the harder the pin flicks it. `struck`
    starts it from its full bend (the step is the pin's snap); otherwise it's rung from rest by whatever excites it."""
    n = samples(length)
    t = np.arange(n) / SR
    ph = 0.0 if struck else -np.pi / 2
    y = np.cos(2 * np.pi * f * t + ph) * np.exp(-6.9 * t / ring)
    for ratio, a, d in ((r2, 0.45, 0.11), (CANTILEVER[2] * r2 / CANTILEVER[1], 0.15, 0.035)):
        if f * ratio < 18000:
            y += a * vel * np.cos(2 * np.pi * f * ratio * t + ph + 0.6) * np.exp(-6.9 * t / (ring * d))
    return (y * vel).astype(np.float32)


def damper(rng, f, level):
    """The damper touching a still-ringing tooth just before its pin: the tooth's note chattering against a wire."""
    L = rng.uniform(0.012, 0.022)
    n = samples(L)
    t = np.arange(n) / SR
    chatter = (np.sin(2 * np.pi * rng.uniform(170, 260) * t) > 0.2).astype(np.float32)
    return (np.sin(2 * np.pi * f * t) * chatter * env([(0, 0), (0.002, 1), (L, 0.3)], L) * level).astype(np.float32)


def comb(rng, times, tonic, tuning, excite=None, broken=(), slop=0.003):
    """Play the score on a comb: `times(beat)` gives when each pin comes round, `tuning` a tooth's error in cents, a
    tooth in `broken` snapped off short (a dull tick with no ring). Each tooth rings until its own next pluck, the
    damper buzzing first if it's still sounding. `excite` (a list of recorded clicks) rings the teeth from real pin
    clicks instead of a modelled snap."""
    ev = score(rng)
    L = times(BEATS)
    out = np.zeros(samples(L + 7), np.float32)
    teeth = {}
    for b, m, vel, key in ev:
        teeth.setdefault(key, []).append(((times(b) + rng.normal(0, slop)) % L, vel))
    for key, notes in teeth.items():
        notes.sort()
        m, alt = key
        f = tonic * 2 ** ((m + tuning(key) / 100) / 12)
        r2 = CANTILEVER[1] * (1 + 0.04 * np.sin(m * 1.7 + alt))       # each tooth filed and weighted its own way
        ring = 0.12 if key in broken else ring_time(f)
        # the loop goes round: a tooth's last note rings into its first, so its next pluck is the first one again
        nxt = [t for t, _ in notes[1:]] + [notes[0][0] + L]
        for (t, vel), t2 in zip(notes, nxt):
            gap = t2 - t
            y = tooth(f, min(ring * 1.2, gap + 0.01), vel, r2, ring, struck=excite is None)
            if excite is not None:
                x = excite[int(rng.integers(len(excite)))]
                y = np.convolve(x, y)[:len(y)] * 4
            else:
                snap = hp(rng.standard_normal(samples(0.003)).astype(np.float32), 2500) * np.linspace(1, 0, samples(0.003))
                y[:len(snap)] += snap * 0.3 * vel
            if gap < ring * 1.2:
                y = ck.choke(y, gap - 0.004, 0.004)
                left = vel * np.exp(-6.9 * gap / ring)
                if left > 0.08:
                    dz = damper(rng, f, left * 0.5)
                    a = samples(t2) - len(dz) - samples(0.002)
                    if a >= 0:
                        out[a:a + len(dz)] += dz
            a = samples(t)
            out[a:a + len(y)] += y[:len(out) - a]
    return out


def periodic_noise(rng, n, lo, hi):
    """Band-limited noise that is exactly periodic over n samples (filtered round the circle), for a loop's steady bed."""
    X = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / SR)
    g = np.clip(np.minimum((f - lo * 0.7) / (lo * 0.3), (hi * 1.3 - f) / (hi * 0.3)), 0, 1)
    y = np.fft.irfft(X * g, n=n).astype(np.float32)
    return y / (np.max(np.abs(y)) + 1e-9)


def fan(rng, n, turns):
    """The governor: a two-vaned fan on a worm, spinning the whole time the box plays, beating the air twice a turn
    (`turns` whole turns a cycle, so it loops): a soft, fast whirr."""
    t = np.arange(n) / n
    vanes = (0.5 + 0.5 * np.cos(2 * np.pi * 2 * turns * t)) ** 4
    return (periodic_noise(rng, n, 700, 4500) * (0.35 + 0.65 * vanes)).astype(np.float32)


def case(y, rng):
    """The comb's sound through its little wooden case: the box's air and lid resonances, its small hollow."""
    y = hp(y, 170, 2)
    y = dsp.peak(dsp.peak(y, 470, 1.4, 3), 1900, 2.0, 3)
    return dsp.room(y, "box", wet=0.18, rng=np.random.default_rng(4))


@recipe("crew-noisy-toys", "music-box", "comb",
        "A wind-up music box playing Brahms' lullaby: a modelled steel comb, bright and tinkly, the governor whirring",
        """The first strain of Brahms' Wiegenlied (1868), melody and bass, at a music box's 100 beats a minute, one pass a
        loop (14.4 s). Each tooth is modelled as a clamped steel bar (modes at 1, 6.27 and 17.5 times its note, the upper
        ones gone in a moment), snapped off a pin; melody teeth come in pairs a few cents apart so held notes beat, and a
        pin meeting a tooth still ringing buzzes its damper first. Every tooth a few cents out (worn), each pin a hair
        early or late. Through a small wooden case, the fan governor whirring softly under it.""",
        takes=1, loop=True, seconds=14.4)
def music_box_comb(rng, k):
    beat = 60 / 100
    n = samples(BEATS * beat)
    tune = {key: rng.normal(0, 4) + (1.5 if key[1] else -1.5) for key in TEETH}
    y = comb(rng, lambda b: b * beat, hz(74), lambda key: tune[key])
    y = case(unit(y), rng)
    y = mix(unit(y), fan(rng, n, 470) * 0.022)
    y = dsp.room(y, "car", wet=0.1, rng=np.random.default_rng(11))
    return seamless(y, n)


def clicks(keys, n=6, length=0.0025):
    """Real pin clicks: the first few milliseconds of each hit in some recorded mechanisms, to ring teeth with."""
    out = []
    for key in keys:
        x = ck.get(key)
        for s, _ in ck.hits(x, floor_db=-24, gap=0.02)[:n]:
            c = hp(x[s:s + samples(length)], 1200, 2) * np.linspace(1, 0.2, samples(length))
            out.append((c / (np.max(np.abs(c)) + 1e-9)).astype(np.float32))
    return out


def texture(rng, x, length, period, grain=120, xf=16):
    """A steady recording (a motor's buzz) carried on for `length`: period-aligned grains from anywhere in it, joined by
    in-phase crossfades so its buzz never cancels itself."""
    g, f = grain * period, xf * period
    win = np.ones(g, np.float32)
    win[:f], win[-f:] = np.linspace(0, 1, f), np.linspace(1, 0, f)
    out = np.zeros(samples(length) + g, np.float32)
    a = 0
    while a < samples(length):
        o = int(rng.integers((len(x) - g) // period)) * period
        out[a:a + g] += x[o:o + g] * win
        a += g - f
    return out


def fold(x, n):
    """A steady texture made one cycle long: its overrun crossfaded into its head."""
    m = samples(SEAM)
    c = x[:n].copy()
    t = np.linspace(0, np.pi / 2, m)
    c[:m] = c[:m] * np.sin(t) + x[n:n + m] * np.cos(t)
    return c


@recipe("crew-noisy-toys", "music-box", "worn",
        "An old, worn music box playing Brahms' lullaby: out of tune, dragging, one tooth broken, its motor whirring",
        """The same strain of Brahms' Wiegenlied and the same steel-comb model, but an old box: the teeth are rung by real
        pin clicks (the first milliseconds of recorded mechanism ticks) rather than a modelled snap, and the sound goes
        through a real wooden case (a light wood knock used as the case's response). Its teeth have drifted (pairs well
        apart so they beat, two a quarter-tone off), one bass tooth has snapped short and only ticks, the pins are worn
        uneven, and the spring drags: the tune sags and catches up through each pass. Slower, 90 a minute, one pass a
        16 s loop; the governor is a real small motor's buzz, slowed and darkened.""",
        sources=TICKS + WOOD[:1] + [MOTOR], takes=1, loop=True, seconds=16.0)
def music_box_worn(rng, k):
    L = 16.0
    n = samples(L)
    # the spring's drag: the governor hunts, so the pins come round unevenly, slowing and catching up
    bb = np.linspace(0, BEATS, 2401)
    ph = rng.uniform(0, 6.28, 2)
    speed = 1 + 0.06 * np.sin(2 * np.pi * bb / BEATS + ph[0]) + 0.025 * np.sin(2 * np.pi * 3 * bb / BEATS + ph[1])
    tt = np.concatenate([[0], np.cumsum(1 / speed[1:] * np.diff(bb))])
    tt *= L / tt[-1]
    tune = {key: rng.normal(0, 14) + (6 if key[1] else -6) for key in TEETH}
    tune[(LA, 0)] += 45
    tune[(-7, 0)] -= 40
    y = comb(rng, lambda b: float(np.interp(b, bb, tt)), hz(72) * 2 ** (-25 / 1200), lambda key: tune[key],
             excite=clicks(TICKS), broken={(-10, 0)}, slop=0.012)
    wood = ck.align(ck.get(WOOD[0]))[:samples(0.2)]
    wood = dsp.fade(wood / np.sqrt(np.sum(wood ** 2)), 0, 0.05)
    y = unit(y)
    y = case(mix(y * 0.7, unit(np.convolve(y, wood)[:len(y)]) * 0.45), rng)
    motor = dsp.vari(texture(rng, ck.get(MOTOR), L * 0.7, 601), -6)
    motor = fold(unit(lp(bp(motor, 300, 2500, 2), 1800, 1)), n)
    y = mix(unit(y), motor * 0.035)
    y = dsp.room(y, "car", wet=0.1, rng=np.random.default_rng(11))
    return seamless(y, n)


# ---- The tin drummer ------------------------------------------------------------------------------------------------------
# A lithographed tin drummer boy: a cam on the spring motor lifts each arm in turn and lets it fall, so the wire sticks
# beat a little tin drum in a steady march, one now and then bouncing a second stroke, and the other arm tinks a tiny
# cymbal on the one. Under it the clockwork: the motor's governor whirring and the cam's ratchet clicking each stroke.

TIN = ck.K("impactTin_medium")
LIGHT_METAL = ck.K("impactMetal_light")
RATCHET = ["sfx_100_v2:lock_open_01", "sfx_100_v2:misc_20"]


def load(n, times, fall=0.08):
    """How hard the spring is working through the cycle: a jolt at each stroke falling away over `fall` s, wrapping
    round the loop's end."""
    out = np.zeros(n, np.float32)
    k = samples(fall)
    for t in times:
        out[(samples(t) + np.arange(k)) % n] += np.linspace(1, 0, k, dtype=np.float32)
    return np.clip(out, 0, 1)


def tin_tap(rng, vel, key):
    """A wire stick on the toy drum: a real tin can's hit pitched up a fifth to a drum a few inches across and choked
    by its head, the stick's tip ticking on the tin, the head's loose edge chattering a moment."""
    x = unit(ck.align(ck.get(key)))
    y = ck.choke(dsp.vari(x, 7 + rng.uniform(-0.3, 0.3)), 0.03, 0.03)
    tip = ck.tick(rng, rng.uniform(4000, 6000), q=6, length=0.006)
    chat = ck.grains(rng, int(rng.integers(2, 5)), 0.025, 2500, 6500, q=(8, 16), length=(0.003, 0.008), shape=0.5)
    # a drum this small has no bottom: the can's thump goes
    return (unit(hp(mix(unit(y), unit(tip) * 0.35, unit(chat) * 0.12), 320, 2)) * vel).astype(np.float32)


def tin_cymbal(rng, vel):
    """The tiny cymbal: a small piece of real struck steel pitched up into a tin disc's tink, with its shimmer."""
    x = unit(ck.align(ck.get(LIGHT_METAL[int(rng.integers(5))])))
    y = ck.choke(dsp.vari(x, 5 + rng.uniform(-0.5, 0.5)), 0.12, 0.12)
    sh = hp(synth.noise(0.3, rng), 5000) * env([(0, 1), (0.3, 0)], 0.3, "exp")
    return (mix(unit(y), sh * 0.15) * vel).astype(np.float32)


def ratchet(rng):
    """The cam's pawl dropping off a tooth: one click cut from a real ratchet."""
    x = ck.get(RATCHET[int(rng.integers(2))])
    h = ck.hits(x, floor_db=-20, gap=0.02)
    a = h[int(rng.integers(len(h)))][0]
    return unit(ck.cut(x, a - samples(0.001), a + samples(0.03), 0.0005, 0.01))


@recipe("crew-noisy-toys", "drummer", "tin",
        "A wind-up tin drummer from real tin: a toy drum beaten in a steady march, a cymbal's tink, the motor whirring",
        """The drum is real tin: the packs' tin-can hits pitched up a fifth to a toy drum a few inches across, choked by its
        head, with the wire stick's tip ticking and the loose head chattering. The cam drops the arms in turn, about five
        strokes a second, the right a touch weaker and later than the left; now and then a stick bounces a second stroke.
        A tiny cymbal (a small piece of real struck steel pitched up) tinks on the one every eight strokes, once in a while
        only grazed. Under it a real small motor's buzz for the governor, sagging as each stroke loads it, and a real
        ratchet's pawl clicking between strokes. One 12.2 s cycle.""",
        sources=TIN + LIGHT_METAL + RATCHET + [MOTOR], takes=1, loop=True, seconds=12.16)
def drummer_tin(rng, k):
    strokes, period = 64, 0.19
    L = strokes * period
    n = samples(L)
    b = Bus(L + 1)
    hits = []
    doubles = set(rng.choice([i for i in range(strokes) if i % 8], 6, replace=False).tolist())
    for i in range(strokes):
        right = i % 2
        t = i * period + (0.012 if right else 0) + rng.uniform(0, 0.004)
        hits.append(t)
        vel = (0.78 if right else 1.0) * rng.uniform(0.9, 1.05) * (1.1 if i % 8 == 0 else 1.0)
        key = TIN[(2 * right + int(rng.integers(2))) % 5]
        b.at(t, tin_tap(rng, vel, key))
        if i in doubles:
            b.at(t + rng.uniform(0.035, 0.05), tin_tap(rng, vel * 0.4, key))
        if i % 8 == 0:
            b.at(t + 0.004, tin_cymbal(rng, 0.25 if rng.random() < 0.15 else rng.uniform(0.6, 0.75)))
        b.at(t + period * 0.55 + rng.normal(0, 0.003), ratchet(rng), -20 + rng.uniform(-2, 2))
    motor = dsp.vari(texture(rng, ck.get(MOTOR), L * 1.2, 601), 3)
    motor = fold(unit(bp(motor, 900, 6000, 2)), n) * (1 - 0.3 * load(n, hits))
    y = mix(b.x, motor * 0.1)
    y = dsp.room(y, "car", wet=0.12, rng=np.random.default_rng(11))
    return seamless(y, n)


def drumhead(rng, vel, f0=640):
    """A wire stick on a small tin drum, modelled: a hard tap rings the tin head's round-membrane modes (high Q: a metal
    head, not skin) and the shell's own bright modes, choked fast by the stick's weight."""
    x = np.zeros(samples(0.16), np.float32)
    k = samples(0.0004)
    x[:k] = 1
    x[:samples(0.002)] += rng.standard_normal(samples(0.002)) * np.linspace(0.5, 0, samples(0.002))
    f = f0 * rng.uniform(0.99, 1.01)
    head = dsp.resonate(x, [f * m for m in MEMBRANE], q=45, gains=[1, 0.8, 0.6, 0.5, 0.4, 0.3, 0.25, 0.2])
    shell = dsp.resonate(x, [m * 2.2 for m in synth.TIN], q=70, gains=[0.6, 0.5, 0.5, 0.4, 0.3, 0.2])
    y = mix(unit(head), unit(shell) * 0.4, x * 0.3)
    y = hp(y * env([(0, 1), (0.03, 0.4), (0.16, 0)], 0.16, "exp"), 380, 2)
    return (unit(y) * vel).astype(np.float32)


def bell_tink(rng, vel, f=2900):
    """A tiny tin cymbal, modelled: a thin disc's inharmonic partials, the top ones dying first."""
    L = 0.45
    t = np.arange(samples(L)) / SR
    y = np.zeros_like(t)
    for i, r in enumerate((1.0, 1.47, 2.09, 2.56, 3.18, 3.73)):
        y += np.sin(2 * np.pi * f * r * rng.uniform(0.99, 1.01) * t) * np.exp(-t / (0.16 / (1 + 0.5 * i))) / (1 + 0.4 * i)
    y += hp(synth.noise(L, rng), 6000) * np.exp(-t / 0.02) * 0.4
    return (unit(y.astype(np.float32)) * vel).astype(np.float32)


@recipe("crew-noisy-toys", "drummer", "cam",
        "A wind-up drummer with a buzzing escapement: a modelled tin drum on a march cam, a cymbal tink, the clockwork's trill",
        """The drum is modelled: a hard wire tap rings a small tin head's drumhead modes (high Q, a metal head) and the
        shell's own tin modes, choked fast. A different cam from 'tin': a march figure, four strokes a bar with the
        first leaned on, a quick double at the end of every fourth bar and now and then elsewhere, a tiny modelled cymbal
        tinking on each bar's one. Under it the cheap clockwork's verge escapement: a buzz of real metal ticks (cut from
        recorded mechanisms) chattering about 26 times a second through the tin body, slowing a moment as each stroke
        loads the spring. One 13.2 s cycle.""",
        sources=TICKS, takes=1, loop=True, seconds=13.2)
def drummer_cam(rng, k):
    bars, beat = 15, 0.22
    L = bars * 4 * beat
    n = samples(L)
    b = Bus(L + 1)
    hits = []
    extra = set(rng.choice([i for i in range(bars) if i % 4 != 3], 2, replace=False).tolist())
    for bar in range(bars):
        for s in range(4):
            t = (bar * 4 + s) * beat + rng.uniform(0, 0.004)
            hits.append(t)
            vel = (1.0 if s == 0 else 0.7) * rng.uniform(0.9, 1.05)
            b.at(t, drumhead(rng, vel))
            if s == 3 and (bar % 4 == 3 or bar in extra):
                b.at(t + beat * 0.5, drumhead(rng, vel * 0.85))
            if s == 0:
                b.at(t + 0.003, bell_tink(rng, rng.uniform(0.3, 0.42)))
    # the escapement: its pallets tick about 26 times a second, slowed as each stroke takes the spring's force
    rate = 26 * (1 - 0.18 * load(n, hits, 0.1))
    ex = clicks(TICKS, n=8, length=0.004)
    buzz = np.zeros(n + SR, np.float32)
    ph = np.cumsum(rate) / SR
    for i in np.nonzero(np.diff(np.floor(ph)))[0]:
        c = ex[int(rng.integers(len(ex)))] * rng.uniform(0.5, 1.0) * (0.75 if int(ph[i]) % 2 else 1.0)
        buzz[i:i + len(c)] += c
    buzz = mix(buzz * 0.5, dsp.resonate(buzz, [m * 1.6 for m in synth.TIN], q=25) * 0.5)
    y = mix(b.x, unit(buzz) * 0.22)
    y = dsp.room(y, "car", wet=0.12, rng=np.random.default_rng(11))
    return seamless(y, n)
