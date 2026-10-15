"""U1's yard cat and the guard van's dog (up-pets; #967, note 745; queue #534, note 806) and the bow, the salute and the
conga on the emote wheel (crew-emotes; U1's "more emotes", note 714; C1's clips, note 791), as tools/audio/cues.py lists
them.

What each is decides the build:
- The cat and the dog are tells: the cat hisses at a creature aboard its car or the next, the dog barks at one aboard or
  closing in behind. So each must read at once as what it is: an ordinary yard cat, a medium dog, close, in a car.
  - The hiss: a cat's back up, mouth wide and lips drawn off the teeth: a spit first (the mouth snapped open on air
    under pressure, a short loud burst), then breathy air forced out through the open mouth, 2-9 kHz, fluttering as the
    breath pushes, falling away as it runs out; sometimes a low growl rattling under it.
  - The meow: a 'mi-a-ow', the mouth opening and closing over half a second or so (the formants gliding up as the jaw
    drops and down as it closes), its pitch round 400-700 Hz rising then falling, a little hoarse (breath in the voice).
  - The bark: a medium dog's sharp alarm bark, close, in the guard van: the throat snapping open on a burst of air, the
    pitch leaping up in the first 20-30 ms and falling away (a bark's inverted V), harsh, the jaw opening and closing on
    it; one or two barks a take, the van's boards round it.
  There are no animals in the packs. The nearest real thing is a throat: the packs' big cat's roar (misc_24), whose
  single pulses (figures._pulses) are a real larynx's. Shrunk (each pulse resampled shorter, so its resonances rise like a
  smaller throat's) and laid one per period at the animal's own pitch, they make the kitbashed candidates' voices; the
  packs' real gasp (air forced through a mouth) and long air hiss are the breath. The second candidates are modelled: a
  glottis through a vocal tract of the animal's size, and noise through a cat's open mouth.
- The emotes are gameplay foley for grown men and women in heavy work coats, boots and leather gloves, close, in a car:
  - The bow: the coat creasing as they bend, one boot drawn back over the boards, the coat settling as they come up.
  - The salute: boot heels brought together with a clack, the sleeve snapped up to the brim.
  - The conga: a loop while a crewmate leads one: boots in step on beats one, two and three and a kick on four, and the
    leader humming 'da-da-da-da-da-DA' under it (a man's voice, lips nearly closed, never a band), whole bars at 126 bpm.
    It plays for as long as the conga goes, so it's soft-edged: no hard top, nothing booming.
  Kitbashed: the crew's own boots (crew_feet's heels and toes, the packs' real boots on boards), the packs' real cloth,
  leather, wood knocks and creaks, the hum from the real throat's pulses. Modelled: plank modes for boots, wool's
  stick-slip for coats, struck modes for heels, a glottis through a man's tract for the hum.
Every impact and rub keeps under 45% of its energy below 150 Hz (the squish rule); the animals and the hum are high-passed
so almost nothing of them sits down there.
"""

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import SR, samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.beasts import fry, unit
from recipes.crew_feet import heel, toe, on_wood, scrape, BOOTS
from recipes.crew_items import hit_of, CLOTH
from recipes.crew_kit import recipe, R, K
from recipes.figures import _pulses, shut, loop as cycle, band_shape, at as loud
from recipes.house_folk import drawn
from recipes.moose_tells import burst
from recipes.odd_emotes import emote
from recipes.rescue_finds import plank, wool, body, at, GRIPS, CREAKS, SWISH

# ---- Sources ------------------------------------------------------------------------------------------------------------

ROAR = "sfx_100_v2:misc_24"          # a real big cat's roar: a real larynx, voiced round 200 Hz (its pulses are the voices)
GASP = "sfx_100_v2:misc_05"          # a real gasp: air forced through an open mouth, broadband, centred near 7 kHz
AIR = "sfx_100_v2:air_02"            # a real long hiss of air, a second, fading as it goes
WOOD_L = K("impactWood_light")       # real light wood knocks: small hard blocks
SLAP = R("bookPlace2")[0]            # a real book slapped flat: a padded leathery pat (a glove on a cap's brim)

CAT = 1.35                           # a yard cat's mouth and throat, as a share of a child's (synth.TRACT): formants up a third
DOG = 0.8                            # a medium dog's (world_cues' farm dog's): 15-25 kg


def lead(x, t=0.02):
    """A one-shot with a little silence before it, so build.py's 2 ms fade-in lands before the first sound."""
    return np.concatenate([np.zeros(samples(t), np.float32), np.asarray(x, np.float32)])


def van(y, wet=0.16):
    """The guard van: a small wooden car, its boards close round the animal."""
    return dsp.room(y, "car", wet=wet, rng=np.random.default_rng(11))


def low_cut(x, f=150.0):
    """Nothing of an animal's voice under `f`: the high-pass is steep, so a voice keeps its pitch (the ear hears the
    harmonics) and loses its rumble."""
    return hp(x, f, 4)


# ---- A real throat, shrunk ----------------------------------------------------------------------------------------------

def throat(rng, f, shrink, amp=None, avg=8, sub=0.0, jit=0.004):
    """The packs' real throat (the roar's single pulses, figures._pulses) laid one per period of `f` (Hz per sample):
    each pulse the average of `avg` real ones (fewer: the roar's roughness stays in), resampled `shrink` times shorter
    (a smaller throat: its resonances rise with it), every other one `sub` quieter (period doubling: harsh or hoarse),
    the periods a little uneven like a real voice's. `amp` a per-sample level."""
    pulses, P = _pulses()
    n = len(f)
    bank = []
    for _ in range(8):
        idx = rng.choice(len(pulses), min(avg, len(pulses)), replace=False)
        p = np.mean(pulses[idx], axis=0) * np.hanning(2 * P)
        bank.append(dsp.resample(p.astype(np.float32), 1 / shrink))
    m = max(len(b) for b in bank)
    out = np.zeros(n + m, np.float32)
    t, i = float(rng.uniform(0, 4)), 0
    while True:
        a = int(t)
        if a >= n:
            break
        g = 1.0 if amp is None else float(amp[a])
        if sub and i % 2:
            g *= 1 - sub
        if g > 1e-4:
            b = bank[int(rng.integers(len(bank)))]
            out[a:a + len(b)] += b * g * rng.uniform(0.9, 1.1)
        t += SR / max(float(f[a]), 40.0) * (1 + rng.normal(0, jit))
        i += 1
    return out[:n]


def mouth_of(x, vowels, k, q=(4.0, 5.0, 5.0), gains=(1.0, 0.8, 0.45)):
    """An animal's mouth: synth.tract's formants (a vowel name or [(t, vowel)], gliding between them) at `k` times a
    child's, but broad (low q): an animal's high voice has few harmonics, and through narrow formants each one swells
    and dies as it crosses them; a real mouth's are wide and damped."""
    n = len(x)
    if isinstance(vowels, str):
        vowels = [(0.0, vowels)]
    ts = [t for t, _ in vowels] + [n / SR]
    out = np.zeros(n, np.float32)
    for i in range(3):
        fs = [synth.VOWELS[v][0][i] * k for _, v in vowels]
        fc = np.interp(np.arange(n) / SR, ts, fs + [fs[-1]]).astype(np.float32)
        out += gains[i] * dsp.sweep_filter(x, "bp", fc, q=q[i], block=128)
    return unit(out)


# ---- up-pets: the cat -------------------------------------------------------------------------------------------------------

PETS = "up-pets"

# Each meow take: (length, start, top, end pitch Hz, mouth [(share of length, vowel)], a 'mrr' trill at its start)
MEOWS = [
    (0.70, 520, 680, 440, [(0, "m"), (0.06, "i"), (0.3, "e"), (0.5, "a"), (0.8, "o"), (1.0, "u")], False),   # mi-a-ow
    (0.42, 600, 700, 560, [(0, "m"), (0.08, "i"), (0.45, "e"), (0.8, "e"), (1.0, "u")], False),               # a short mew
    (0.80, 440, 630, 420, [(0, "m"), (0.16, "u"), (0.3, "e"), (0.5, "a"), (0.82, "o"), (1.0, "u")], True),    # mrr-a-ow
    (0.88, 470, 600, 400, [(0, "m"), (0.05, "e"), (0.2, "a"), (0.7, "a"), (0.88, "o"), (1.0, "u")], False),   # maaa-ow
]


def meow_pitch(rng, L, f0, top, end, trill):
    """The meow's pitch: up from `f0` to `top` as the mouth opens, held a moment, down to `end` as it closes; a little
    wobble in it, and for a 'mrr' a trill's flutter over its first tenth of a second."""
    f = env([(0, f0), (L * 0.35, top), (L * 0.6, top * rng.uniform(0.93, 0.98)), (L, end)], L, "exp")
    n = len(f)
    t = np.arange(n) / SR
    f = f * (1 + 0.012 * np.sin(2 * np.pi * rng.uniform(5, 7) * t + rng.uniform(0, 6)))
    if trill:
        f = f * (1 - 0.05 * (t < 0.12) * (0.5 + 0.5 * np.sin(2 * np.pi * 26 * t)))
    return f.astype(np.float32)


def meow_shape(L, trill):
    """In soft (a cat's meow swells), loudest as the mouth's widest, falling away as it closes; a trill's gating first."""
    e = env([(0, 0), (0.04, 0.6), (L * 0.4, 1.0), (L * 0.75, 0.75), (L, 0)], L)
    if trill:
        t = np.arange(len(e)) / SR
        e = e * np.where(t < 0.13, 0.55 + 0.45 * np.sin(2 * np.pi * 26 * t) ** 2, 1.0)
    return e.astype(np.float32)


def mouth(L, vowels):
    return [(L * s, v) for s, v in vowels]


def finish_meow(y, L, trill):
    """The meow's shape on it, evened a little (a harmonic crossing a formant swells; a real meow's level moves less),
    its top rolled off and nothing under 300 Hz."""
    y = dsp.compress(unit(y * dsp.fit(meow_shape(L, trill), len(y))), -12, 2.5, 0.004, 0.06)
    return unit(low_cut(lp(y, 9000), 300))


def meow_real(rng, k):
    """A meow from the real throat: the roar's pulses shrunk to a cat's and laid at the meow's pitch (four averaged in
    each, so a little of the roar's rasp stays: hoarse), through the cat's mouth opening and closing ('mi-a-ow'), with
    the real gasp's air drawn out under it for the breath in a hoarse voice."""
    L, f0, top, end, vow, trill = MEOWS[k]
    L *= rng.uniform(0.95, 1.05)
    f = meow_pitch(rng, L, f0 * rng.uniform(0.97, 1.03), top * rng.uniform(0.97, 1.03), end, trill)
    src_ = throat(rng, f, rng.uniform(2.4, 2.7), avg=4, sub=0.08 if k == 3 else 0.0, jit=0.006)
    v = mouth_of(src_, mouth(L, vow), CAT)
    air = mouth_of(drawn(rng, GASP, L, rng.uniform(0, 2), 0.05), mouth(L, vow), CAT)
    y = mix(v, unit(hp(src_, 2500)) * 0.1, unit(hp(air, 1200)) * (0.2 if k != 3 else 0.3))
    return finish_meow(y, L, trill)


def meow_model(rng, k):
    """A meow, modelled: a glottis at the meow's pitch (a cat's small, slightly rough folds) through a cat-sized tract
    gliding 'mi-a-ow', breath through the same mouth for the hoarseness."""
    L, f0, top, end, vow, trill = MEOWS[k]
    L *= rng.uniform(0.95, 1.05)
    f = meow_pitch(rng, L, f0 * rng.uniform(0.97, 1.03), top * rng.uniform(0.97, 1.03), end, trill)
    g = synth.glottis(f, L, rng, jitter=0.01, shimmer=0.12, oq=0.5, rough=0.12, sub=0.1 if k == 3 else 0.0)
    v = mouth_of(g, mouth(L, vow), CAT)
    air = mouth_of(synth.noise(L, rng), mouth(L, vow), CAT)
    y = mix(v, unit(hp(air, 1200)) * (0.2 if k != 3 else 0.3))
    return finish_meow(y, L, trill)


@recipe(PETS, "meow", "throat",
        "The yard cat's meow: an ordinary cat's 'mi-a-ow', a little hoarse, from a real throat shrunk to a cat's",
        """No cat in the packs, so a real throat made one: the packs' big cat's roar cut into its single pulses (a real
        larynx's), each shrunk to a house cat's throat and laid back down one per period at a meow's pitch (rising from
        about 450-600 Hz to 600-700 and falling away), a little of the roar's rasp left in for the hoarseness, through a
        cat's mouth opening and closing ('m', 'i', 'a', 'ow'), the packs' real gasp drawn out under it for the breath.
        Four meows: a 'mi-a-ow', a short 'mew', a 'mrr-a-ow' with a trill at its start, and a longer, hoarser
        complaint.""", sources=[ROAR, GASP], takes=4, lufs=-20, gap=0.8)
def meow_throat(rng, k):
    return lead(meow_real(rng, k))


@recipe(PETS, "meow", "tract",
        "The yard cat's meow, modelled: a cat's small throat through its mouth, 'mi-a-ow', a little hoarse",
        """Modelled: a glottis (a cat's small, slightly rough vocal folds) at a meow's pitch, rising from about 450-600 Hz
        to 600-700 and falling away, through a cat-sized vocal tract whose formants glide as the mouth opens and closes
        ('m', 'i', 'a', 'ow'), breath through the same mouth for the hoarseness. The same four meows as 'throat': a
        'mi-a-ow', a short 'mew', a 'mrr-a-ow' with a trill, a longer hoarser complaint.""", takes=4, lufs=-20, gap=0.8)
def meow_tract(rng, k):
    return lead(meow_model(rng, k))


# The hiss: a spit, then the breath forced out, its length; take 1 has a growl under it, take 2 hisses twice.
HISSES = [(0.68,), (0.5,), (0.34, 0.3)]


def spit(rng, real, force=1.0):
    """The spit that starts a hiss: the mouth snapped open on air held under pressure, a few milliseconds of hard click and
    a short, very loud burst of air that's gone in 30-40 ms. `real`: the packs' real gasp's own burst, its first 40 ms."""
    L = rng.uniform(0.028, 0.04)
    if real:
        g = ck.get(GASP)
        a = int(np.argmax(np.abs(g) > 0.25 * np.max(np.abs(g))))
        b = hp(ck.cut(g, a - samples(0.002), a + samples(L), 0.0005, L * 0.6), 1500, 2)
    else:
        b = burst(rng, L, 1500, 13000, attack=0.04)
    tick = synth.click(rng.uniform(2800, 4200), q=3, length=0.005, rng=rng)
    return unit(mix(unit(b), unit(hp(tick, 1500)) * 0.45)) * force


def open_mouth(rng, x):
    """Air through a cat's mouth wide open, lips drawn back off the teeth and the tongue grooved: held to 1.8-11 kHz,
    its resonances near 3.3, 5.8 and 8.5 kHz, each a little different from cat to cat."""
    y = bp(x, 1800, 11000, 2)
    for f, q, g in ((3300, 2.5, 7), (5800, 3.0, 5), (8500, 3.0, 3)):
        y = dsp.peak(y, f * rng.uniform(0.92, 1.08), q, g)
    return y


def hiss_air(rng, L, real):
    """The hiss itself: breath forced out through the open mouth, swelling as the chest pushes, fluttering (the breath's
    own unevenness and the cat's belly pumping it, 6-12 a second), falling away as it runs out. `real`: the packs' real
    gasp drawn out, with the real air hiss's tail under it; otherwise plain air."""
    if real:
        a = drawn(rng, GASP, L, rng.uniform(0, 2), 0.04)
        b = ck.get(AIR)
        b = b[samples(0.05):samples(0.05) + samples(L)]
        x = mix(unit(a), unit(dsp.fit(b, samples(L))) * 0.5)
    else:
        x = synth.noise(L, rng)
    n = samples(L)
    t = np.arange(n) / SR
    pump = 1 + 0.16 * np.sin(2 * np.pi * rng.uniform(6, 12) * t + rng.uniform(0, 6))
    flutter = 1 + 0.25 * W.norm(lp(rng.standard_normal(n).astype(np.float32), 30))
    shape = env([(0, 0.55), (0.03, 0.8), (L * 0.3, 1.0), (L * 0.7, 0.55), (L, 0)], L)
    return unit(open_mouth(rng, dsp.fit(x, n)) * dsp.fit(shape * pump * flutter, n))


def cat_growl(rng, L, real):
    """A low growl under the hiss: the cat's throat rattling in a growl's irregular pulses (22-30 a second), mouth nearly
    shut. `real`: the real throat's pulses, only two averaged so the rasp stays; otherwise a modelled glottis."""
    f = env([(0, rng.uniform(150, 170)), (L * 0.5, rng.uniform(165, 185)), (L, rng.uniform(140, 155))], L, "exp")
    if real:
        g = throat(rng, f, 1.8, avg=2, sub=0.3, jit=0.02)
    else:
        g = synth.glottis(f, L, rng, jitter=0.04, shimmer=0.3, sub=0.4, rough=0.4)
    x = g * (0.25 + 0.75 * fry(rng, rng.uniform(22, 30), L)) + synth.noise(L, rng) * fry(rng, 26, L) * 0.15
    v = synth.tract(x, [(0, "m"), (L * 0.5, "u"), (L, "m")], CAT, breath=0.2, rng=rng)
    return unit(low_cut(v, 200) * env([(0, 0), (0.06, 1), (L * 0.75, 0.8), (L, 0)], L))


def hiss(rng, k, real):
    """A take: a spit and its hiss (take 1 with a growl under it, rising first; take 2 two hisses, the second spat
    harder), close, the car's boards round it."""
    parts, t = [], 0.0
    if k == 1:
        gL = rng.uniform(0.75, 0.85)
        parts.append((0.0, cat_growl(rng, gL, real), -8))
        t = rng.uniform(0.2, 0.26)
    for i, L in enumerate(HISSES[k]):
        L *= rng.uniform(0.94, 1.06)
        parts.append((t, spit(rng, real, 1.0 if i == 0 else 1.15), 0))
        parts.append((t + 0.012, hiss_air(rng, L, real), -4))
        t += L + rng.uniform(0.07, 0.11)
    y = ck.place(parts)
    return lead(dsp.room(low_cut(y, 300), "car", wet=0.08, rng=np.random.default_rng(11)))


@recipe(PETS, "hiss", "gasp",
        "The yard cat hissing at something aboard: a sharp spit, then breath forced out through bared teeth, its back up",
        """No cat in the packs, so a mouth's real air made one: the spit is the packs' real gasp's own first burst (air
        forced through a mouth, snapped open with a tick), and the hiss that follows is that gasp drawn out with the real
        air hiss's tail under it, held to 1.8-11 kHz through a cat's wide-open mouth (resonances near 3.3, 5.8 and
        8.5 kHz), fluttering and pumping as the breath is pushed out and falling away as it runs out. One take has a low
        growl rattling under it (the packs' real roar's throat, shrunk to a cat's and gated in a growl's pulses), one
        hisses twice. Close, a breath of the car's boards.""", sources=[GASP, AIR, ROAR], takes=3, lufs=-18, gap=0.8)
def hiss_gasp(rng, k):
    return hiss(rng, k, True)


@recipe(PETS, "hiss", "spit",
        "The yard cat hissing at something aboard, modelled: a spit, then a breathy hiss through an open mouth",
        """Modelled: the spit a tick and a very short loud burst of air as the mouth snaps open, then air forced out
        through a cat's wide-open mouth (noise held to 1.8-11 kHz, its resonances near 3.3, 5.8 and 8.5 kHz) swelling,
        fluttering and pumping with the breath, falling away as it runs out. One take with a low growl rattling under it
        (a glottis through the cat's nearly shut mouth, gated in a growl's pulses), one hissing twice.""",
        takes=3, lufs=-18, gap=0.8)
def hiss_spit(rng, k):
    return hiss(rng, k, False)


# ---- up-pets: the dog -----------------------------------------------------------------------------------------------------

# Each take's barks: (when, pitch ratio, length, dB); the dog's own pitch 380-460 Hz.
BARKS = [
    [(0.0, 1.0, 0.2, 0)],                                    # one sharp bark
    [(0.0, 1.0, 0.18, 0), (0.31, 0.95, 0.2, -1)],            # two, the second a little lower
    [(0.0, 1.1, 0.16, 0)],                                   # one, higher and shorter: alarm
    [(0.0, 1.0, 0.17, 0), (0.25, 1.07, 0.17, 0)],            # two quick, the second higher: it's coming
]


def bark_pitch(f0, L):
    """A bark's pitch: snapped up from 70% to 110% in the first 22 ms, then falling away to 60% as the breath goes."""
    return env([(0, f0 * 0.72), (0.022, f0 * 1.12), (0.05, f0 * 1.02), (L * 0.7, f0 * 0.84), (L, f0 * 0.6)], L, "exp")


def bark_shape(L):
    """In at once, loudest in its first 30 ms, falling away through the rest."""
    return env([(0, 0), (0.003, 1), (0.03, 1), (L * 0.5, 0.55), (L * 0.85, 0.25), (L, 0)], L)


def jaw(L):
    """The jaw: shut, snapped open on 'a' in 12 ms, closing through 'o' to 'u' at the end ('ruff')."""
    return [(0, "u"), (0.012, "a"), (L * 0.6, "a"), (L * 0.85, "o"), (L, "u")]


def flews(rng, L=0.035):
    """The air bursting out past the loose lips as the jaw snaps open: a short burst of noise fluttering at the lips'
    40-50 Hz."""
    t = np.arange(samples(L)) / SR
    b = burst(rng, L, 500, 6000, attack=0.05)
    return unit(b * (0.55 + 0.45 * np.sin(2 * np.pi * rng.uniform(40, 50) * t)))


def chaos(rng, g, L):
    """The air forced through the folds with each pulse (noise gated by the voice), and a bark's chaotic break: its first
    30 ms more noise than voice."""
    n = len(g)
    e = W.norm(lp(np.abs(g), 300))
    onset = env([(0, 1.0), (0.02, 0.7), (0.045, 0.0), (L, 0.0)], L)
    return dsp.fit(synth.noise(L, rng), n) * dsp.fit((0.3 + 0.7 * e) * 0.4 + onset * 0.7, n)


def dog_mouth(src_, L):
    """The dog's mouth on a bark's source: the jaw's formants, broad (a dog's flews and wide mouth damp them), and the
    harsh top three formants miss (the source itself, 1.5-6 kHz)."""
    v = mouth_of(src_, jaw(L), DOG, q=(2.5, 3.0, 3.5), gains=(1.0, 0.9, 0.6))
    return mix(v, unit(bp(src_, 1500, 6000)) * 0.35)


def bark_real(rng, f0, L):
    """One bark from the real throat: the roar's pulses, two averaged so its rasp stays, shrunk to a medium dog's throat
    and laid at the bark's leaping pitch with period doubling (harsh), breath forced through them, through the dog's jaw
    snapping open and closing; snapped open on the real gasp's first burst of air."""
    f = bark_pitch(f0, L)
    g = unit(throat(rng, f, rng.uniform(1.6, 1.8), avg=2, sub=rng.uniform(0.3, 0.45), jit=0.02))
    gs = ck.get(GASP)
    a = int(np.argmax(np.abs(gs) > 0.25 * np.max(np.abs(gs))))
    pop = hp(ck.cut(gs, a, a + samples(0.035), 0.0005, 0.02), 500)
    y = mix(dog_mouth(g + chaos(rng, g, L), L), g * 0.2, unit(pop) * 0.45, flews(rng) * 0.2)
    return unit(dsp.saturate(unit(y), 8) * dsp.fit(bark_shape(L), len(y)))


def bark_model(rng, f0, L):
    """One bark, modelled: a glottis snapping up into a bark's pitch, harsh (period doubling, a rough flutter, breath
    forced through each pulse), through a medium dog's mouth whose jaw snaps open and closes, the chest's low harmonics
    under it, the air bursting out past the lips at its start."""
    f = bark_pitch(f0, L)
    g = unit(synth.glottis(f, L, rng, jitter=0.04, shimmer=0.35, oq=0.4, sub=rng.uniform(0.35, 0.5),
                           rough=rng.uniform(0.35, 0.55)))
    y = mix(dog_mouth(g + chaos(rng, g, L), L), unit(lp(g, 900, 2)) * 0.2, flews(rng) * 0.4)
    return unit(dsp.saturate(unit(y), 8) * dsp.fit(bark_shape(L), len(y)))


def sniff(rng, L=0.07):
    """A quick snatched breath in between barks, through the nose."""
    return unit(hp(synth.breath(L, "m", DOG, rng, env([(0, 0), (L * 0.7, 1), (L, 0)], L)), 1200))


def barks(rng, k, one):
    f0 = rng.uniform(380, 460)
    parts = []
    for i, (t, p, L, db) in enumerate(BARKS[k]):
        L *= rng.uniform(0.94, 1.06)
        parts.append((t + rng.uniform(-0.01, 0.01), one(rng, f0 * p * rng.uniform(0.97, 1.03), L), db))
        if i + 1 < len(BARKS[k]):
            parts.append((t + L + 0.02, sniff(rng), -22))
    return lead(van(low_cut(ck.place(parts), 160)))


@recipe(PETS, "bark", "throat",
        "The guard van's dog barking an alarm: a medium dog's sharp bark, close, in the van, from a real throat",
        """No dog in the packs, so a real throat made one: the packs' big cat's roar cut into its single pulses (a real
        larynx), only two averaged into each so its rasp stays, shrunk to a medium dog's throat and laid back down at a
        bark's pitch (380-460 Hz, snapped up in the first 22 ms and falling away), every other pulse weaker for a bark's
        harshness, through the dog's jaw snapping open and closing ('ruff'); the packs' real gasp's first burst for the
        air bursting out. One bark, two, one higher and shorter, two quick with the second higher; a snatched breath
        between two. Close, in the guard van's wooden car.""", sources=[ROAR, GASP], takes=4, lufs=-16, gap=0.8)
def bark_throat(rng, k):
    return barks(rng, k, bark_real)


@recipe(PETS, "bark", "tract",
        "The guard van's dog barking an alarm, modelled: a medium dog's sharp bark, close, in the van",
        """Modelled: a glottis snapping up into a bark's pitch (380-460 Hz) in the first 22 ms and falling away, harsh
        (period doubling, a rough flutter, breath forced through each pulse), through a medium dog's vocal tract with the
        jaw snapping open and closing, the chest's low harmonics under it and the air bursting out past the loose lips.
        The same barks as 'throat': one, two, one higher and shorter, two quick. Close, in the guard van.""",
        takes=4, lufs=-16, gap=0.8)
def bark_tract(rng, k):
    return barks(rng, k, bark_model)


# ---- crew-emotes.bow --------------------------------------------------------------------------------------------------------

CE = "crew-emotes"

# Two bows: (when the boot goes back, its drag's length, when they bend, how long they're down, how deep: the coat's
# creasing louder and longer)
BOWS = [(0.0, 0.26, 0.18, 0.75, 1.0),        # the boot drawn back, then down into the bow
        (0.12, 0.34, 0.0, 0.95, 1.25)]       # a deeper, slower bow, the boot going back as they bend


def coat_crease_rec(rng, k, L):
    """The coat creasing as they bend: the packs' real cloth handlings slowed for heavy wool, two laid end to end so it
    lasts the bend, the top rolled off."""
    a = dsp.vari(ck.cloth(rng, k, L * 0.55), -4)
    b = dsp.vari(ck.cloth(rng, k + 1, L * 0.55), -3)
    y = ck.place([(0, a, 0), (len(a) / SR * 0.7, b, -3)])
    return unit(lp(y, 4500)) * dsp.fit(env([(0, 0), (0.08, 1), (L * 0.7, 0.8), (L, 0)], L), len(y))


def bow(rng, k, crease, drag, step, leather, creak, settle):
    back, dL, bend, down, deep = BOWS[k]
    parts = [(back, drag(rng, k, dL), -3),
             (back + dL + rng.uniform(0.0, 0.03), step(rng, k), -6),
             (bend, crease(rng, k, down * deep), 0),
             (bend + 0.1, leather(rng, k), -14),
             (bend + down * 0.5, creak(rng, k), -18),
             (bend + down + rng.uniform(0.12, 0.2), settle(rng, k), -9)]
    return emote(parts)


def drag_rec(rng, k, L):
    """The boot drawn back: the crew's own toe set on the boards (the packs' real boots), the sole dragged back over them."""
    return ck.place([(0, unit(on_wood(rng, toe(rng, k), 0.7, "toe")), -4), (0.01, scrape(rng, "wood", L, 1.0), 0)])


def step_rec(rng, k):
    """The heel set down behind: the crew's own heel, light."""
    return unit(on_wood(rng, heel(rng, k + 1), 0.6, "heel"))


def leather_rec(rng, k):
    """The boot's leather creasing at the ankle: a real glove's leather taking hold, slowed."""
    return unit(hp(dsp.vari(hit_of(GRIPS[k % 2], 0, 0.15), -4), 300))


def creak_rec(rng, k):
    """A board under the shifted weight: a real creak, pitched down a little, quiet."""
    return unit(hp(dsp.vari(hit_of(CREAKS[k % 3], 0, 0.3), rng.uniform(-4, -2)), 200))


def settle_rec(rng, k):
    """The coat falling straight as they come up: a softer real cloth handling."""
    return unit(lp(dsp.vari(ck.cloth(rng, k + 2, 0.3), -2), 4000))


@recipe(CE, "bow", "coat",
        "A low bow: the coat creasing as they bend, a boot drawn back over the boards, the coat falling straight after",
        """The crew's own boots (the packs' real boots on boards, cut into heels and toes): one toe set on the boards and
        the sole drawn back over them, the heel set down behind; the heavy coat creasing as they bend (the packs' real
        cloth handlings slowed for heavy wool, two laid end to end so it lasts the bend), the boot's leather creasing at
        the ankle (a real glove's leather, slowed), a board creaking under the shifted weight (a real creak), and the coat
        falling straight as they come up. One bow with the boot back first, one deeper and slower. Close, in the car.""",
        sources=BOOTS + CLOTH + GRIPS + CREAKS, takes=2, lufs=-21)
def bow_coat(rng, k):
    return bow(rng, k, coat_crease_rec, drag_rec, step_rec, leather_rec, creak_rec, settle_rec)


def crease_model(rng, k, L):
    """The coat creasing, modelled: heavy wool bunching (stick-slip and crackle), slow."""
    return wool(rng, L, 0.8, env([(0, 0), (0.1, 1), (L * 0.7, 0.75), (L, 0)], L))


def drag_model(rng, k, L):
    """The boot drawn back, modelled: a toe's knock on a plank, then a sole's stick-slip over the boards."""
    sh = env([(0, 0), (0.02, 1), (L * 0.6, 0.7), (L, 0)], L)
    return ck.place([(0, plank(rng, 0.7, True), -4), (0.01, ck.friction(rng, L, 150, 300, 3500, 0.6, sh), 0)])


def step_model(rng, k):
    return plank(rng, 0.6, False)


def leather_model(rng, k):
    """The boot's leather creasing, modelled: a short slow stick-slip of thick hide."""
    L = 0.14
    return unit(hp(synth.creak(L, 70, rng, body=[700, 1300, 2100], q=6, jitter=0.4,
                               shape=env([(0, 0), (0.03, 1), (L, 0)], L)), 300))


def creak_model(rng, k):
    """A board under the shifted weight, modelled: pine's stick-slip, slow."""
    L = 0.28
    return unit(hp(synth.creak(L, env([(0, 40), (L, 70)], L), rng, body=[m * 1.2 for m in synth.WOOD], q=14, jitter=0.3,
                               shape=env([(0, 0), (0.08, 1), (L, 0)], L)), 200))


def settle_model(rng, k):
    return wool(rng, 0.3, 0.6)


@recipe(CE, "bow", "wool",
        "A low bow, modelled: heavy wool creasing as they bend, a boot drawn back over the boards, the coat falling straight",
        """Modelled: one toe knocked on a plank (a boot's blow through a plank's modes over the car's hollow) and the
        sole's stick-slip drawn back over the boards, the heel set down behind; the heavy coat's wool bunching and
        crackling as they bend, the boot's thick leather creasing at the ankle (a short slow stick-slip), a board creaking
        under the weight, and the wool falling straight as they come up. The same two bows as 'coat'.""",
        takes=2, lufs=-21)
def bow_wool(rng, k):
    return bow(rng, k, crease_model, drag_model, step_model, leather_model, creak_model, settle_model)


# ---- crew-emotes.salute -----------------------------------------------------------------------------------------------------

# Three salutes: (when the heels meet, when the arm starts up, how long it takes, heels' pitch in semitones)
SALUTES = [(0.0, 0.05, 0.16, 0.0),           # heels, then the arm snapped up
           (0.11, 0.0, 0.13, 1.0),           # arm and heels together, quick and smart
           (0.0, 0.09, 0.2, -2.0)]           # the bigger man: heavier heels, the arm a little slower


def salute(rng, k, heels, sleeve, brim):
    th, ta, L, semis = SALUTES[k]
    snap = ta + L * 0.88
    parts = [(th, heels(rng, k, semis), 0), (ta, sleeve(rng, k, L), -4), (snap + 0.012, brim(rng, k), -15)]
    return emote(parts)


HEEL_KNOCKS = [WOOD_L[1], WOOD_L[3], WOOD_L[4]]


def heels_rec(rng, k, semis):
    """The heels brought together: the moving heel coming down on the boards (the crew's own heel, the packs' real boots)
    and the two heels' clack (a real light wood knock pitched up and choked to a heel's small hard block of stacked
    leather)."""
    knock = unit(hp(ck.choke(at(rng, ck.get(HEEL_KNOCKS[k]), 5 + semis), 0.01, 0.018), 600))
    floor = unit(ck.choke(on_wood(rng, heel(rng, k + 2), 1.0, "heel"), 0.025, 0.015))
    return ck.place([(0, knock, 0), (0.003, floor, -7)])


def sleeve_rec(rng, k, L):
    """The sleeve snapped up: the arm's quick swish, and the sleeve snapping taut as it stops (the sharp start of the
    packs' real cloth handling, and the cloth's crack)."""
    throw = ck.whoosh(rng, L, 220, rng.uniform(1500, 2100), 0.8, 1.1)
    taut = unit(hp(ck.cloth(rng, k + 1, 0.1), 300))
    crack = unit(bp(synth.noise(0.012, rng), 900, 5000) * env([(0, 1), (0.012, 0)], 0.012, "exp"))
    return ck.place([(0, unit(throw), -2), (L * 0.88, taut, 0), (L * 0.88, crack, -10)])


def brim_rec(rng, k):
    """The glove's edge at the cap's brim: a real leathery pat, small and dull."""
    return unit(lp(ck.choke(at(rng, hit_of(SLAP, 1, 0.06), 4), 0.008, 0.012), 4000))


@recipe(CE, "salute", "heels",
        "A salute: boot heels brought together with a clack, the sleeve snapped up to the brim",
        """The heels brought together: the moving heel coming down on the boards (the crew's own heels, the packs' real
        boots) with the two heels' clack (a real light wood knock pitched up and choked short, for stacked leather
        heels); the arm snapped up (a quick swish of a heavy sleeve) and the sleeve snapping taut as it stops (the sharp
        start of the packs' real cloth handling, a crack of cloth), the glove's edge meeting the cap's brim (a real
        leathery pat, small). Heels then arm; both together, quick and smart; the bigger man, heavier and a little
        slower.""", sources=BOOTS + WOOD_L[1:2] + WOOD_L[3:5] + CLOTH + [SLAP], takes=3, lufs=-20)
def salute_heels(rng, k):
    return salute(rng, k, heels_rec, sleeve_rec, brim_rec)


HEEL = [1150, 1900, 2950, 4300]           # a boot heel's stacked leather and its iron tip: a small hard dead block


def heels_model(rng, k, semis):
    """The heels together, modelled: two small hard blocks knocked (a heel's stacked leather, its few dead modes) and the
    heel's blow on a plank over the car's hollow."""
    r = 2 ** (semis / 12)
    knock = body(rng, [m * r for m in HEEL], 7, 0.05, 0.0012, 6500, gains=[1, 0.7, 0.45, 0.25])
    return ck.place([(0, unit(hp(knock, 400)), 0), (0.003, ck.choke(plank(rng, 0.9, False, r), 0.025, 0.015), -4)])


def sleeve_model(rng, k, L):
    """The sleeve snapped up, modelled: a heavy sleeve's swish (a band of noise rising with the arm's speed), the wool
    rubbing at the shoulder, the cloth's crack as it's stopped."""
    sh = env([(0, 0), (L * 0.8, 1), (L, 0)], L)
    throw = mix(unit(ck.whoosh(rng, L, 220, rng.uniform(1500, 2100), 0.8, 1.1)),
                unit(ck.friction(rng, L, 260, 900, 5500, 0.9, sh)) * 0.35)
    crack = unit(burst(rng, 0.014, 700, 5000, attack=0.06))
    return ck.place([(0, unit(throw), -5), (L * 0.88, crack, -3)])


def brim_model(rng, k):
    """The glove at the brim, modelled: a short soft pat of leather."""
    return unit(burst(rng, 0.01, 400, 3000, attack=0.15))


@recipe(CE, "salute", "block",
        "A salute, modelled: heels clacked together, a heavy sleeve snapped up to the brim",
        """Modelled: the heels' clack (two small hard blocks of stacked leather knocked, a few dead modes 1.1-4.3 kHz) and
        the heel's blow on a plank over the car's hollow; the arm snapped up (a heavy sleeve's swish, the wool rubbing at
        the shoulder) and the cloth's crack as it stops, a glove's soft pat on the brim. The same three salutes as
        'heels'.""", takes=3, lufs=-20)
def salute_block(rng, k):
    return salute(rng, k, heels_model, sleeve_model, brim_model)


# ---- crew-emotes.conga ------------------------------------------------------------------------------------------------------

BPM = 126.0
BEAT = 60 / BPM
BARS = 4
CONGA_L = BARS * 4 * BEAT                   # four bars of 4/4: 7.62 s
G3 = 196.0
# The hum, bar by bar: (eighth, semitones over G3, accented DA). 'da-da-da-da-da' on one-and-two-and-three, a rest, and
# the DA on four with the kick. I, V, I, then V back home to I so the loop comes round on its own.
TUNE = [
    [(0, 4), (1, 4), (2, 7), (3, 4), (4, 0), (6, 7, True)],
    [(0, 2), (1, 2), (2, 5), (3, 2), (4, -1), (6, 7, True)],
    [(0, 4), (1, 4), (2, 7), (3, 4), (4, 0), (6, 9, True)],
    [(0, 5), (1, 4), (2, 2), (3, -1), (4, 2), (6, 0, True)],
]


def hum_line(rng, n):
    """The hum over the cycle: the pitch (Hz per sample, sliding between notes, a little vibrato on each DA), the level
    (each 'da' started by the tongue's soft 'd', the DA longer and stronger), and the mouth's opening (a hum's 'mm'
    opening a little on each 'da')."""
    e8 = BEAT / 2
    tgt = np.zeros(n, np.float32)
    lvl = np.zeros(n, np.float32)
    jaw_ = np.zeros(n, np.float32)
    vib = np.zeros(n, np.float32)
    for b, notes in enumerate(TUNE):
        for note in notes:
            q, s = note[0], note[1]
            da = len(note) > 2
            t0 = b * 4 * BEAT + q * e8 + rng.uniform(0.0, 0.012)
            d = e8 * (1.6 if da else 0.96)
            i0, i1 = samples(t0), min(n, samples(t0 + d))
            m = i1 - i0
            tgt[i0:] = s
            tt = np.arange(m) / SR
            g = (1.15 if da else 0.85 + 0.1 * (q == 0)) * rng.uniform(0.92, 1.0)
            shape = np.interp(tt, [0, 0.03, 0.07, d * 0.75, d], [0.35, 0.9, 1.0, 0.85, 0.0 if da else 0.45])
            lvl[i0:i1] = np.maximum(lvl[i0:i1], (shape * g).astype(np.float32))
            jaw_[i0:i1] = np.maximum(jaw_[i0:i1], np.interp(tt, [0, 0.03, 0.09, d], [0.0, 1.0, 0.5, 0.1]).astype(np.float32))
            if da:
                vib[i0:i1] = np.clip((tt - 0.1) / 0.1, 0, 1).astype(np.float32)
    k = 1 - np.exp(-1 / (0.025 * SR))       # the slide between notes, round the cycle so it comes back to where it began
    sm = np.concatenate([tgt, tgt])
    sm = signal.lfilter([k], [1, -(1 - k)], sm, zi=[sm[0] * (1 - k)])[0][n:]
    t = np.arange(n) / SR
    semis = sm + 0.22 * vib * np.sin(2 * np.pi * 5.4 * t)
    return (G3 * 2 ** (semis / 12)).astype(np.float32), lvl, jaw_


def hum(rng, n, voice):
    """The leader's hum: a man's voice (`voice(rng, f, level)`) at the tune's pitch, mostly through closed lips (the
    nose's murmur), a little of it through a mouth opening on each 'da' and closing back to 'mm'."""
    f, lvl, jaw_ = hum_line(rng, n)
    v = voice(rng, f, lvl)
    closed = shut(v)
    opened = synth.tract(v, "o", "man") * jaw_
    y = mix(unit(closed), unit(opened) * 0.45)
    return unit(lp(y, 3200, 2))


def real_voice(rng, f, lvl):
    """The real throat's pulses at the hum's pitch (eight averaged into each, so the roar's roughness goes and its throat
    stays): figures' hum, here on a tune."""
    return throat(rng, f, 1.0, amp=lvl, avg=8, jit=0.003)


def model_voice(rng, f, lvl):
    """A glottis at the hum's pitch, a man's small unevenness in it, through his tract on 'm'."""
    g = synth.glottis(f, len(f) / SR, rng, jitter=0.006, shimmer=0.06, oq=0.7)
    return synth.tract(g, "m", "man", breath=0.1, rng=rng) * lvl[:len(g)]


def conga(rng, step, kick, voice):
    """Four bars: every bar three steps in time (one, two, three) and a kick on four, the leader's boots and two more down
    the line a hair behind and quieter; the hum over it. Built round the cycle (tails into the head), the car's boards
    round it, held to 150 Hz-7 kHz, so whole bars tile."""
    n = samples(CONGA_L)
    ev = []
    for who, (db, late) in enumerate(((0.0, 0.0), (-7.0, 0.018), (-10.0, 0.032))):
        for b in range(BARS):
            for beat in range(3):
                t = b * 4 * BEAT + beat * BEAT + late + rng.normal(0, 0.006 + 0.006 * who)
                acc = 1.0 if beat == 0 else 0.85
                ev.append((t, step(rng, who * 5 + b * 3 + beat, acc), db + rng.uniform(-1, 0.5)))
            if who == 0:
                ev.append((b * 4 * BEAT + 3 * BEAT - 0.06, kick(rng, b), -6))
            elif rng.random() < 0.7:
                ev.append((b * 4 * BEAT + 3 * BEAT - 0.06 + late, kick(rng, b + who), db - 8))
    feet = W.place(n, [(t, x, dsp.db2a(db)) for t, x, db in ev])
    feet = loud(hp(feet, 150, 4), -24)
    voice_ = loud(hum(rng, n, voice), -27)
    return cycle(mix(feet, voice_), n, rng, 0.08, "car", band_shape(150, 7000))


def conga_step_rec(rng, take, acc):
    """A conga step on the boards: the crew's own heel and toe (the packs' real boots), a short, light shuffle."""
    return ck.place([(0, unit(on_wood(rng, heel(rng, take), 1.0, "heel")), 0),
                     (rng.uniform(0.045, 0.06), unit(on_wood(rng, toe(rng, take + 1), 1.0, "toe")), -5)]) * acc


def conga_kick_rec(rng, b):
    """The kick: the leg flicked out, the coat's skirt swinging with it (the packs' real cloth swish), a toe brushing the
    boards as it goes."""
    sw = unit(lp(dsp.vari(ck.get(SWISH), rng.uniform(-1, 1)), 5000))[:samples(0.3)]
    brush = scrape(rng, "wood", 0.06, 1.0)
    return ck.place([(0, unit(brush), -8), (0.03, dsp.fade(sw, 0.01, 0.08), 0)])


@recipe(CE, "conga", "boots",
        "The conga line: boots in step on the boards, a kick every fourth beat, the leader humming 'da-da-da-da-da-DA'",
        """Four bars at 126 bpm, whole bars so the loop tiles: in every bar three steps in time on the car's boards (the
        crew's own boots, the packs' real heels and toes, a light shuffle) and a kick on four (a real cloth swish for
        the coat swinging out, a toe brushing the boards), the leader's boots and two more down the line a hair behind
        and quieter. Under it the leader hums the tune, 'da-da-da-da-da-DA' with the DA on the kick: the packs' real
        throat cut into its pulses and laid back down at a man's humming pitch (G3 to E4), mostly through closed lips,
        the mouth opening a little on each 'da'. Soft-edged: held to 150 Hz-7 kHz, the car's boards round it.""",
        sources=BOOTS + [SWISH, ROAR], loop=True, takes=1, lufs=-22, seconds=round(CONGA_L, 2))
def conga_boots(rng, k):
    return conga(rng, conga_step_rec, conga_kick_rec, real_voice)


def conga_step_model(rng, take, acc):
    """A conga step, modelled: a heel's blow and then the toe's, each through a plank's modes over the car's hollow."""
    return ck.place([(0, plank(rng, 1.0, False), 0), (rng.uniform(0.045, 0.06), plank(rng, 0.8, True), -5)]) * acc


def conga_kick_model(rng, b):
    """The kick, modelled: the coat's wool swung out (a low swish and the wool's rub), a sole's brush on the boards."""
    L = 0.26
    sw = mix(unit(ck.whoosh(rng, L, 150, 900, 0.45, 0.9)), wool(rng, L, 0.8) * 0.25)
    brush = ck.friction(rng, 0.06, 150, 300, 3500, 0.6, env([(0, 0), (0.01, 1), (0.06, 0)], 0.06))
    return ck.place([(0, unit(brush), -8), (0.03, unit(sw), 0)])


@recipe(CE, "conga", "planks",
        "The conga line, modelled: boots in step on planks, a kick every fourth beat, a man humming 'da-da-da-da-da-DA'",
        """Modelled, the same four bars at 126 bpm as 'boots': three steps a bar (a heel's and a toe's blow through a
        plank's modes over the car's hollow) and a kick on four (the coat's wool swung out, a sole's brush on the boards),
        the leader and two down the line a hair behind; the hum a glottis at a man's humming pitch through his tract,
        mostly through closed lips, opening a little on each 'da', the DA on the kick. Soft-edged, held to
        150 Hz-7 kHz.""", loop=True, takes=1, lufs=-22, seconds=round(CONGA_L, 2))
def conga_planks(rng, k):
    return conga(rng, conga_step_model, conga_kick_model, model_voice)
