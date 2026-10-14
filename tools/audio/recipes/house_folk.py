"""The house creatures heard (queue #497, note 767; G1's notes 583-584): the Lodger in the dark of a village house and the
Householder at its set table in the one house that looks lived in (docs/design/creatures/lodger.md and householder.md, §4,
"a request to the audio chat").

What each is decides the build:
- The Lodger: a long, starved thing, ash-grey skin over bone, folded into the darkest corner of a back room, its head
  mostly a jaw that unhinges wide to shriek. Its tell is its breathing, heard from the next room: slow, deep and wet, a
  long chest drawing air over phlegm, its loose lips fluttering on the way out, spit clicking in its mouth. Held low-mid
  (150 Hz-2 kHz) so its telegraph owns the top: the shriek, shrill and climbing for a second and a half, held to 2-6 kHz
  and driven hard, the loudest thing in a village. One ragged throat with no words, no tune and no pulse in it: never the
  Gannet's scream (a seabird's two-voiced, syllabic rasp, a pitch an octave and more below), never the Choir's voices or a
  Soot Child's call for help. When it comes its feet are bare: skin slapping the boards and the boards' knock, with no
  sole and no heel-and-toe roll (never the crew's boots). A shut door it batters in with its whole weight, the planks
  splitting, the latch torn out (never the crew's own door, never the Choir's tendrils lashing one).
- The Householder: a gaunt figure in rotting Sunday clothes at a laid table by a lit stove. Its tell is its house heard
  from the door: the stove's tick and crackle, a clock, a kettle at the edge of the boil, the one warm, homely sound in a
  dead village (200 Hz-5 kHz, and the only thing in a village that keeps time like a clock). Watching, nothing but the
  creak of its chair and of its neck as its head follows you (400 Hz-3 kHz). Its telegraph is its rising: the chair
  scraped back hard on the boards and the cutlery rattling on the table (300 Hz-5 kHz). Hunting, a stiff, even tread in
  old shoes, flat-footed with the leather creaking (never a crewmate's heel-and-toe), and a dry, rasping breath; the grab
  a hard clutch on a coat and a grunt; going home, the thing set back down and the chair drawn in.

Each cue has two candidates. The first is the packs' real things (boards, doors, a chair's creak, cutlery and china, a
clock's and a stove's metal ticks, water, cloth and leather) with the throats and lungs drawn out of the packs' real
breaths and cries (paulstretched, varispeeded, bent); where the packs have nothing (a chair's legs dragged over boards) it's
stick-slip friction made for it. The second is modelled: a glottis through a tract, bubbles, stick-slip and struck modes.
The rooms are a village house's, small and bare (the Householder's furnished and softer); the game's space does the rest.
"""

import functools

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import kit
from recipes import town
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_items import hit_of
from recipes.beasts import fry, unit
from recipes.g1_ground import splinter, splinters, wander
from recipes.gannet_tells import bend
from recipes.moose_tells import held

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

GASP = "sfx_100_v2:misc_05"                      # a real gasp: a short breathy burst of air through a mouth
GROAN = "sfx_100_v2:misc_25"                     # a real groan and wail: a voiced throat
ROAR = "sfx_100_v2:misc_24"                      # a real roar: a throat round 200 Hz
WET = S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")   # real wet squelches: cut into a mouth's spit
WATER = S("loop_water_02", "loop_water_03")      # real water pouring and trickling
TICKS = R("metalClick") + S("misc_01")           # small real metal ticks: a clock's escapement, iron taking heat
POT = R("metalPot1", "metalPot2", "metalPot3")   # a real pot knocked: a kettle's lid
CREAKS = R("creak1", "creak2", "creak3")         # real wooden creaks
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4")
LEATHER = R("handleSmallLeather", "handleSmallLeather2")   # real leather handled: an old shoe's upper, a coat's
DROP_LEATHER = R("dropLeather")[0]               # a real heavy leather drop: a hand landing on a coat
BELT = R("clothBelt", "beltHandle2")             # a real belt and buckle handled
SLAPS = R("bookPlace1", "bookPlace2")            # a real book slapped down flat
SOFT = K("impactSoft_medium") + K("impactSoft_heavy")      # real soft blows: flesh
STEPS = K("footstep_wood")                       # real steps on boards
WOOD_L, WOOD_M, WOOD_H = K("impactWood_light"), K("impactWood_medium"), K("impactWood_heavy")
PLANK = K("impactPlank_medium")                  # a plank's hollow knock
DECK = S("wood_hit_01", "wood_hit_02")           # heavy blows on planking
SLAMS = S("door_03", "misc_36")                  # a real door slammed, a heavy knock-slam
CRACKS = S("misc_34", "misc_35")                 # real wood cracking and breaking (g1_ground.splinters cuts them)
LATCHES = R("metalLatch", "metalClick")
CUTLERY = S("metal_02", "metal_06", "metal_05")  # small real metal clinks and jangles
RATTLE = S("metal_03")[0]                        # a real loose-metal rattle: five knocks in a seventh of a second
CHINA = K("impactGlass_light") + S("glass_01", "glass_04")  # real light glass and china knocked

# ---- Bands (spec A.4: each tell its own) and shapes -----------------------------------------------------------------------

BREATHING = (150, 2000)     # the Lodger's breathing: low-mid, under its shriek
SHRIEK = (2000, 6000)       # its shriek: the top, alone
HOUSE = (200, 5000)         # the Householder's house: warm, broad, keeping time
CREAK = (400, 3000)         # its chair and its neck
RISE = (300, 5000)          # its rising: the chair's scrape, the cutlery

DEAL = [310, 520, 870, 1340]                # thin deal: a plank door's, a chair's, a floorboard's modes
DOOR = [118, 205] + DEAL                    # a ledged plank door in its frame: the leaf's low modes, then its planks'
SEAT = [480, 960, 1580, 2450]               # a kitchen chair's joints and seat: small and dry
GRISTLE = [520, 870, 1450, 2300]            # a neck's gristle: soft, broad, no ring
SPOON = [1.0, 1.52, 2.23]                   # a plated spoon's bending modes over its first
PORCELAIN = [1750, 3550, 5650]              # a plate's rim rung (Hz)
KETTLE = [480, 1150, 2050]                  # a kettle's hollow (Hz)

SEAM = 0.3                  # build.py's loop-seam crossfade
LEAD = 0.5                  # a loop's bed starts this far early, so its filters and its room have settled by the cycle


# ---- Shared parts -------------------------------------------------------------------------------------------------------

@functools.lru_cache(maxsize=16)
def _core(key, length):
    x = ck.get(key)
    w = min(samples(length), len(x))
    if w >= len(x):
        return x.copy()
    c = np.concatenate([[0.0], np.cumsum(x.astype(np.float64) ** 2)])
    a = int(np.argmax(c[w:] - c[:-w]))
    return x[a:a + w].copy()


def core(key, length=0.25):
    """The loudest `length` s of a real recording: its steady middle, past the attack and before the fade, the grain a
    held breath or cry is drawn out of."""
    return _core(key, length).copy()


def drawn(rng, key, length, semis=0.0, window=0.08):
    """A real breath drawn out to `length` s: its loudest quarter second varispeeded by `semis` (down: a longer chest) and
    paulstretched, so its grain and colour stay and its own envelope goes, for a breath's shape of ours."""
    x = core(key)
    if semis:
        x = dsp.vari(x, semis)
    window = min(window, 0.45 * len(x) / SR)
    y = dsp.smear(x, (length + 2 * window) * SR / len(x), window, rng)
    return unit(dsp.fit(y, samples(length)))


def held_cry(rng, key, L, semis, window=0.1):
    """A real cry held out (paulstretched from its loudest 0.22 s, so it keeps the throat's grain without its syllables)
    and pulled along `semis` (varispeed per output sample, gannet_tells.bend) to `L` s, its own swell flattened."""
    c = synth.curve(semis, samples(L))
    need = float(np.sum(2 ** (c / 12))) / SR + 0.2
    x = core(key, 0.22)
    window = min(window, 0.45 * len(x) / SR)
    y = dsp.smear(x, need * SR / len(x), window, rng)
    y = y / (lp(np.abs(y), 6) + 0.05 * np.max(np.abs(y)) + 1e-9)
    return unit(dsp.fit(bend(y, c), samples(L)))


def bed(rng, key, length, fade=0.08):
    """A real recording laid end to end with crossfades, each pass from a fresh start, until it runs `length` s."""
    x = ck.get(key)
    b = Bus(length + 0.2)
    t = 0.0
    while t < length:
        seg = dsp.fade(x[int(rng.integers(0, max(1, len(x) // 2))):], fade, fade)
        b.at(t, seg)
        t += max(len(seg) / SR - fade, 0.05)
    return b.x[:samples(length)]


@functools.lru_cache(maxsize=1)
def _wet():
    out = []
    for key in WET:
        x = ck.denoise(ck.get(key))
        for i, (s, _) in enumerate(ck.hits(x, floor_db=-30, gap=0.01, prom=4)):
            out.append(unit(ck.cut(x, s - samples(0.001), s + samples(0.008 + 0.006 * (i % 3)), 0.0005, 0.005)))
    return out or [unit(ck.cut(ck.get(WET[0]), 0, samples(0.02)))]


def wet_grain(rng, semis=(1.0, 6.0)):
    """One little wet contact cut from the packs' real squelches (8-20 ms), pitched up: the sticky part of a mouth."""
    g = _wet()
    return dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis))


def spit(rng, length, rate):
    """Spit and phlegm crackling in a mouth: real wet grains strewn at `rate` a second (a constant or a curve), a few
    loud and many quiet."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.05)
    t = rng.exponential(1 / max(rc[0], 0.5))
    while t < length:
        b.at(t, wet_grain(rng), 20 * np.log10(rng.uniform(0.1, 1) ** 1.6))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 0.5))
    return b.x[:n]


def crack(rng, semis=(-7.0, -4.0)):
    """One dry crack of bone or gristle: a grain of the packs' real wood cracking, dropped and darkened."""
    g = splinters()
    return unit(lp(dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis)), 3000))


def air(rng, L, vowels, tract, lo=150, hi=3000):
    """Air through a mouth, modelled: mostly plain noise through a soft hump, the tract (synth.breath) only colouring it,
    since a tract on noise alone whistles its formants."""
    plain = unit(lp(hp(synth.noise(L, rng, "pink"), lo, 1), hi, 2))
    return unit(mix(plain * 0.7, unit(synth.breath(L, vowels, tract, rng)) * 0.45))


def shape(inhale, exhale, catch=0.12, tail=0.5):
    """A breath as two envelopes over one length: in (swelling, fullest just before the top), a catch at the top, then
    out (a quick peak falling away slowly)."""
    L = inhale + catch + exhale + tail
    i = env([(0, 0), (inhale * 0.75, 1), (inhale, 0.25), (inhale + catch * 0.5, 0), (L, 0)], L)
    o0 = inhale + catch
    o = env([(0, 0), (o0, 0), (o0 + exhale * 0.12, 1), (o0 + exhale * 0.5, 0.5), (o0 + exhale, 0), (L, 0)], L)
    return i, o, L


def loop_of(events, bedx, length, band=None, order=2):
    """A loop of exactly `length` s from two kinds of thing. `events` (breaths, ticks, a lid's rattle, their reverb tails)
    are folded round onto the cycle's head (dsp.wrap), and the head is appended weighted so build.py's 0.3 s equal-power
    seam sums back to exactly that head: a beat keeps its time across the seam. `bedx` (a fire's murmur, a simmer: never
    still, nothing to fold) starts LEAD early and runs on past the cycle for the seam to crossfade into its head. For a
    tell both are held to its band: the events three times round with the middle kept, the bed from its lead-in, so a
    filter's edges never land on the seam."""
    n, k = samples(length), samples(SEAM)
    c = dsp.wrap(events, n)
    if band:
        c = dsp.band(np.concatenate([c, c, c]), *band, order=order)[n:2 * n]
    w = np.tan(np.pi / 4 - np.linspace(0, np.pi / 2, k) / 2)    # sin t + w cos t = 1
    y = np.concatenate([c, c[:k] * w])
    if bedx is not None:
        b = dsp.band(bedx, *band, order=order) if band else bedx
        y = mix(y, dsp.fit(b[samples(LEAD):], n + k), length=n + k)
    return y.astype(np.float32)


def back_room(x, wet=0.22):
    """A village house's back room: small, bare boards and plaster."""
    return ck.space(x, 0.35, wet, 4500, ((0.004, 0.5), (0.009, 0.35), (0.015, 0.25)))


def kitchen(x, wet=0.15):
    """The Householder's kitchen: as small, but furnished (a dresser, a rug, the table), so softer and darker."""
    return ck.space(x, 0.28, wet, 3800, ((0.005, 0.45), (0.011, 0.3), (0.017, 0.2)))


def dry(x):
    """A whisker of the nearest walls' reflections: the game's world reverb does the rest."""
    return ck.space(x, 0.12, 0.08, 6000, ((0.003, 0.4), (0.007, 0.25)))


def knock(rng, keys, take, semis=0.0, tau=0.04):
    """One real knock from `keys`, pitched and choked (a thing on a table, a chair's leg on a board: it knocks, it
    doesn't ring)."""
    x = dsp.vari(hit_of(keys[take % len(keys)], int(rng.integers(2)), 0.25, -18), semis + rng.uniform(-0.6, 0.6))
    return ck.norm(ck.choke(x, 0.012, tau))


# ---- The Lodger: its breathing (150 Hz-2 kHz) -----------------------------------------------------------------------------

BREATH_L = 12.0     # three slow breaths


def rattle_water(rng, L, take):
    """Phlegm in the airway, from real water: the packs' trickle darkened to a gurgle and gated into a slow wet rattle."""
    w = lp(hp(bed(rng, WATER[take % 2], L), 120), 900)
    return unit(w) * fry(rng, rng.uniform(16, 24), L, jitter=0.45, decay=0.02)


def lungs(rng, inhale, exhale, take):
    """One of the Lodger's breaths from real air: the packs' gasp drawn out and dropped most of an octave for a long chest,
    drawn in through a narrowed throat and let out slower and lower, the loose lips fluttering on the way out; a real
    trickle gated into a phlegm rattle under both, spit crackling, and a wet smack as the mouth closes on some."""
    i_e, o_e, L = shape(inhale, exhale, rng.uniform(0.08, 0.16))
    air_in = dsp.peak(bp(drawn(rng, GASP, L, -8 + rng.uniform(-1, 1)), 250, 2400), 850, 3, 6) * i_e ** 1.3
    flutter = 0.5 + 0.5 * fry(rng, rng.uniform(13, 19), L, jitter=0.4, decay=0.02)
    air_out = dsp.peak(bp(drawn(rng, GASP, L, -10 + rng.uniform(-1, 1)), 150, 1800), 480, 2.5, 6) * o_e * flutter
    wet = rattle_water(rng, L, take) * (0.5 * i_e + o_e)
    s = spit(rng, L, 3 + 20 * (i_e + o_e))
    y = mix(unit(air_in) * 0.8, unit(air_out), wet * 0.3, unit(s) * 0.25)
    if take != 1:
        y = mix(y, ck.place([(inhale + 0.12 + exhale * 0.9, wet_grain(rng, (-2, 2)), -12)], L))
    return y


def gurgle(rng, inhale, exhale, take):
    """One of the Lodger's breaths, modelled: air through a long throat (plain air coloured by a long tract, 'h' opening to
    'o' on the way in, 'o' to 'u' on the way out), bubbles of phlegm bursting low in it in a slow rattle, spit crackling,
    the lips fluttering; a faint groan voiced on one outbreath."""
    i_e, o_e, L = shape(inhale, exhale, rng.uniform(0.08, 0.16))
    air_in = air(rng, L, [(0, "h"), (inhale, "o")], 0.6, 200, 2200) * i_e ** 1.3
    air_out = air(rng, L, [(0, "o"), (inhale + exhale * 0.5, "u"), (L, "h")], 0.55, 120, 1600)
    air_out = air_out * o_e * (0.5 + 0.5 * fry(rng, rng.uniform(13, 19), L, jitter=0.4, decay=0.02))
    bub = synth.bubbles(L, 10 + 60 * (0.4 * i_e + o_e), 140, 600, rng, rise=(0.02, 0.15))
    bub = unit(bub) * fry(rng, rng.uniform(17, 24), L, jitter=0.45, decay=0.02) * (0.5 * i_e + o_e)
    spitx = unit(synth.crackle(L, 4 + 25 * (i_e + o_e), rng, size=(0.0004, 0.002), hi=500))
    y = mix(lp(unit(air_in), 2200) * 0.8, lp(unit(air_out), 1800), bub * 0.35, lp(spitx, 2500) * 0.2)
    if take == 1:
        o0 = inhale + 0.12
        g = synth.glottis(env([(0, 78), (o0, 78), (L, 64)], L, "exp"), L, rng, jitter=0.03, shimmer=0.4, oq=0.6, rough=0.4)
        moan = synth.tract(g, "u", 0.55, breath=0.6, rng=rng) * o_e ** 2
        y = mix(y, unit(lp(moan, 1200)) * 0.18)
    return y


def breathing(rng, one):
    """Three slow breaths in a 12 s cycle, each a little different in length and depth, in the back room."""
    b = Bus(BREATH_L + 6)
    t = rng.uniform(0.1, 0.4)
    for j in range(3):
        b.at(t, one(rng, rng.uniform(1.25, 1.55), rng.uniform(1.7, 2.1), j), rng.uniform(-1.5, 0))
        t += BREATH_L / 3 + rng.uniform(-0.15, 0.15)
    return loop_of(back_room(b.x), None, BREATH_L, BREATHING)


@recipe("tell-lodger", "breathing", "lungs",
        "Slow, deep, wet breathing from a long chest in a dark back room: real air, a phlegm rattle, spit, lips fluttering",
        """From real air: the packs' gasp (sfx_100 misc_05) drawn out (paulstretched) and dropped most of an octave for a
        long, starved chest, drawn in through a narrowed throat and let out slower and lower, the loose lips fluttering on
        the way out. Under it the wet: a real trickle darkened to a gurgle and gated into a slow phlegm rattle, spit crackling
        (grains of the packs' real wet squelches) and a wet smack as the mouth closes. Three breaths, about 15 a minute, each
        its own length and depth, in a small bare back room. A 12 s loop. Held to 150 Hz-2 kHz, under its shriek.""",
        sources=[GASP] + WATER + WET, takes=1, loop=True, band=BREATHING, lufs=-26, seconds=BREATH_L)
def breathing_lungs(rng, k):
    return breathing(rng, lungs)


@recipe("tell-lodger", "breathing", "gurgle",
        "Slow, deep, wet breathing, modelled: air through a long throat over bubbling phlegm, a faint groan on one breath",
        """Modelled: air through a long throat (plain noise coloured by a long vocal tract, 'h' opening to 'o' on the way in,
        'o' to 'u' on the way out), bubbles of phlegm bursting low in it (140-600 Hz) in a slow gated rattle, spit
        crackling, the lips fluttering; on one outbreath of three a faint, low groan voiced through it. About 15 breaths a
        minute in a small bare back room. A 12 s loop. Held to 150 Hz-2 kHz, under its shriek.""",
        takes=1, loop=True, band=BREATHING, lufs=-26, seconds=BREATH_L)
def breathing_gurgle(rng, k):
    return breathing(rng, gurgle)


# ---- The Lodger: its shriek (2-6 kHz) -------------------------------------------------------------------------------------

SHRIEK_L = 1.5


def climb(rng, L, base, rise):
    """The shriek's pitch (semitones over `base`): a scoop up into it as the jaw opens, then a climb of `rise` semitones to
    the end, never still (a slow waver and a ragged tremble)."""
    n = samples(L)
    c = env([(0, base - 4), (0.12, base), (L * 0.5, base + rise * 0.4), (L, base + rise)], L)
    t = np.arange(n) / SR
    return (c + wander(rng, n, 3, 0.35) + 0.25 * np.sin(2 * np.pi * rng.uniform(7, 10) * t)).astype(np.float32)


def scream_env(L):
    """In at once (the telegraph lands on its first frame), loudest at the very end, then cut off: it lunges."""
    return env([(0, 0), (0.04, 0.75), (0.3, 0.7), (L * 0.75, 1.0), (L - 0.05, 1.0), (L, 0)], L)


def rough(rng, y, L):
    """A scream's roughness: the voice flickering 70-120 times a second (the flutter that makes a scream alarming)."""
    return y * (0.6 + 0.4 * fry(rng, env([(0, 70), (L, 120)], L), L, jitter=0.3, decay=0.004))


def jaw(rng):
    """The jaw unhinging as it rears: a wet pop as the lips unstick (a real squelch's grain) and the hinge's dry crack."""
    return ck.place([(0, unit(wet_grain(rng, (-3, 0))), 0), (0.008, crack(rng, (-5, -2)), -4)])


def shriek_end(rng, x):
    """The back room round it, a short tail, then held hard to 2-6 kHz: it carries its level in its body, not in spikes."""
    y = back_room(x, 0.12)[:len(x) + samples(0.25)]
    return dsp.fade(held(y, *SHRIEK, drive=4), 0.0, 0.2)


@recipe("tell-lodger", "shriek", "wail",
        "A shrill, climbing shriek from a jaw unhinged wide: real cries held out, pulled up two octaves, torn rough",
        """Kitbashed from real throats: the packs' groan-and-wail and roar (sfx_100 misc_25, misc_24) held out
        (paulstretched, so they keep their grain without their syllables), pulled up two octaves and on up six to eight
        semitones across the second and a half (varispeed along a rising curve), laid a hair out of tune so they beat;
        flickered 70-120 times a second (a scream's roughness), rung through the open jaw's hollow (a resonance climbing
        with it from 2.6 kHz), the air tearing through the jaw over it (the packs' gasp, up an octave), driven hard. It
        opens on the jaw's wet pop and crack, lands at full cry at once, is loudest at the end and cuts off as it lunges.
        One ragged throat, no pulse and no tune: not the Gannet's scream. 1.5 s. Held to 2-6 kHz.""",
        sources=[GROAN, ROAR, GASP] + WET + CRACKS, takes=3, band=SHRIEK, lufs=-18)
def shriek_wail(rng, k):
    L = SHRIEK_L + rng.uniform(0, 0.05)
    base, rise = rng.uniform(22, 25), rng.uniform(6, 8)
    c = climb(rng, L, base, rise)
    y = mix(held_cry(rng, GROAN, L, c), held_cry(rng, ROAR, L, c + rng.uniform(0.3, 0.6)) * 0.55)
    y = rough(rng, y, L)
    gape = 2600 * 2 ** ((c - base) / 12 * 0.8)
    y = mix(unit(y), unit(dsp.sweep_filter(y, "bp", gape, q=3, block=64)) * 1.2)
    y = mix(unit(y), unit(hp(drawn(rng, GASP, L, 12), 3000)) * 0.25 * scream_env(L))
    y = dsp.saturate(unit(y) * scream_env(L), 9)
    return shriek_end(rng, ck.place([(0, jaw(rng), -6), (0.015, y, 0)], L + 0.05))


@recipe("tell-lodger", "shriek", "throat",
        "A shrill, climbing shriek, modelled: a sharp wet gasp in, then one ragged throat screaming up from 1 kHz",
        """Modelled: a sharp wet gasp in through the teeth as it rears (breath through a short tract, 'h' to 'i', and a
        spit's click), then the shriek: a glottis from about 1.1 kHz climbing six to eight semitones, jittery and breaking
        into period doubling (a scream's chaos), through a wide-open 'a' brightening to 'e' and 'i', with the unhinged jaw's
        hollow ringing at 2.9 and 4.4 kHz; air hissing through the teeth over it, flickered 70-120 times a second, driven
        hard. Loudest at the end, cut off as it lunges. 1.5 s in all. Held to 2-6 kHz.""",
        takes=3, band=SHRIEK, lufs=-18)
def shriek_throat(rng, k):
    gasp_L = rng.uniform(0.13, 0.17)
    L = SHRIEK_L - gasp_L + rng.uniform(0, 0.05)
    c = climb(rng, L, 0.0, rng.uniform(6, 8))
    g = synth.glottis(rng.uniform(1050, 1150) * 2 ** (c / 12), L, rng, jitter=0.015, shimmer=0.25, oq=0.4, sub=0.3, rough=0.35)
    v = synth.tract(g, [(0, "a"), (L * 0.5, "e"), (L, "i")], 1.0, breath=0.2, rng=rng, extra=[(2900, 5, 0.7), (4400, 5, 0.5)])
    teeth = bp(synth.noise(L, rng), 3500, 6500) * scream_env(L)
    y = rough(rng, mix(unit(v), unit(teeth) * 0.12), L)
    y = dsp.saturate(unit(y) * scream_env(L), 5)
    gs = env([(0, 0), (gasp_L * 0.8, 1), (gasp_L, 0)], gasp_L)
    gasp = hp(synth.breath(gasp_L, [(0, "h"), (gasp_L, "i")], "woman", rng, shape=gs), 1500)
    click = synth.click(rng.uniform(2200, 2800), q=4, length=0.015, rng=rng)
    return shriek_end(rng, ck.place([(0, unit(gasp), -4), (0.01, click, -10), (gasp_L - 0.01, y, 0)], SHRIEK_L + 0.06))


# ---- The Lodger: the lunge, a door broken in ----------------------------------------------------------------------------

def bare_foot(rng, take, weight=1.0):
    """A bare foot slammed down on boards, from real hits: the skin's flat slap (a real book slapped down, dropped), the
    flesh of the heel thudding (a real soft blow), the board's dry knock and the joists under it. No sole, no roll."""
    skin = ck.choke(dsp.vari(hit_of(SLAPS[take % 2], 0, 0.12), -3 + rng.uniform(-1, 1)), 0.008, 0.02)
    flesh = ck.choke(hit_of(SOFT[(take + int(rng.integers(3))) % len(SOFT)], 0, 0.2), 0.02, 0.035)
    board = ck.choke(dsp.vari(hit_of(PLANK[(take + 1) % 5], 0, 0.2), -2 + rng.uniform(-0.6, 0.6)), 0.02, 0.035)
    joist = ck.hollow(rng, [rng.uniform(110, 150), rng.uniform(240, 300)], 0.1, q=2)
    return ck.place([(0, skin, -4), (0.002, flesh, -2 + 3 * np.log2(weight)), (0.003, ck.norm(board), -6),
                     (0.004, joist, -10 + 3 * np.log2(weight))])


def pad_foot(rng, weight=1.0):
    """A bare foot on boards, modelled: skin meeting wood (a short crack of noise with a little sweat in it), the sole's
    flesh (a dull swell of low noise), the board's broad deal modes and the joists' short thump."""
    skin = kit.slap(rng, 0.06, 3200, wet=0.15)
    flesh = ck.pad(rng, 0.06, 240)
    board = ck.norm(ck.choke(ck.hollow(rng, [f * rng.uniform(0.9, 1.1) for f in DEAL], 0.08, q=3.5, hit=0.002), 0.006, 0.02))
    joist = ck.hollow(rng, [rng.uniform(110, 150)], 0.08, q=1.5, hit=0.003)
    return ck.place([(0, ck.norm(skin), -3), (0.001, flesh, -2 + 3 * np.log2(weight)), (0.002, board, -5),
                     (0.003, joist, -9 + 3 * np.log2(weight))])


def lunge(rng, k, foot):
    """Its lunge, 9 m/s for 4.5 m: both feet driven off the boards a hair apart, a long stride slapped down mid-flight
    (most takes), both feet landing hard where you were and a loose board chattering after; the air it moves under it."""
    parts = [(0, foot(0, 1.3), 0), (rng.uniform(0.025, 0.05), foot(1, 1.0), -2)]
    if k % 3:
        parts.append((0.22 + rng.uniform(-0.02, 0.03), foot(2, 0.9), -3))
    land = 0.44 + rng.uniform(-0.03, 0.04)
    parts += [(land, foot(3, 1.6), 0), (land + rng.uniform(0.02, 0.04), foot(4, 1.3), -1),
              (land + 0.012, ck.grains(rng, 4, 0.08, 400, 1800, q=(4, 8), length=(0.01, 0.03)), -16),
              (0.02, ck.whoosh(rng, land, 150, 1200, 0.7), -22)]
    return dry(ck.place(parts))


@recipe("cs-lodger", "lunge", "soles",
        "The lunge: bare feet thumping off the boards and landing hard, skin slapping wood",
        """Bare feet on a house's boards from real hits: each foot the skin's flat slap (a real book slapped down flat,
        dropped), the heel's flesh thudding (Kenney's soft blows) and the board's dry knock (a real plank's) with the joists
        under it; no sole and no heel-and-toe, so never a crewmate's boots. Both feet driven off a hair apart, a long stride
        slapped down mid-flight on most takes, and both landing hard where you were half a second later, a loose board
        chattering after.""",
        sources=SLAPS + SOFT + PLANK, takes=3, lufs=-19)
def lunge_soles(rng, k):
    return lunge(rng, k, lambda i, w: bare_foot(rng, k + i, w))


@recipe("cs-lodger", "lunge", "pads",
        "The lunge, modelled: bare feet slapping off the boards and landing hard",
        """Modelled bare feet: skin meeting wood (a short crack of noise, a little sweat in it), the sole's flesh (a dull swell
        of low noise), the board's broad deal modes and the joists' short thump. Both feet driven off a hair apart, a stride
        slapped down mid-flight on most takes, both landing hard half a second later, a loose board chattering.""",
        takes=3, lufs=-19)
def lunge_pads(rng, k):
    return lunge(rng, k, lambda i, w: pad_foot(rng, w))


BREAK_AT = 1.85     # the door gives at the end of the sim's 2 s break; the blows before it
BLOWS = [(0.0, 0.75, 1.45), (0.0, 0.45, 0.95, 1.4), (0.0, 0.9)]


def blow_boards(rng, take, force, split):
    """Its whole weight thrown at a shut plank door, from real hits: the planks taking it (a real heavy blow on planking
    and a real door's slam, dropped for a cottage's ledged door), its body behind it (a real soft blow), the door jumping
    in its frame (the latch chattering, the frame knocking) and, as the boards start to go, real splinters."""
    deck = dsp.vari(hit_of(DECK[take % 2], int(rng.integers(2)), 0.35), -2 + rng.uniform(-1, 1))
    slam = dsp.vari(hit_of(SLAMS[(take + 1) % 2], 0, 0.4), -1 + rng.uniform(-1, 1))
    body = ck.choke(hit_of(SOFT[take % len(SOFT)], 0, 0.25), 0.03, 0.05)
    chatter = [(0.03 + i / rng.uniform(25, 40), knock(rng, LATCHES + WOOD_L, take + i, rng.uniform(-4, 2), 0.02), -10 - 3 * i)
               for i in range(int(rng.integers(3, 6)))]
    parts = [(0, deck, 0), (0.002, slam, -4), (0, body, -8)] + chatter
    if split:
        parts.append((0.004, splinter(rng, 0.2 + 0.3 * split, env([(0, 260), (0.5, 20)], 0.5)), -12 + 8 * split))
    return ck.place([(t, x, g + 4 * np.log2(force)) for t, x, g in parts])


def burst_boards(rng, take):
    """The door giving: a real board breaking with a crack, splinters flying, the latch's keeper torn out (a real latch's
    snap and the keeper ticking down the floor), the leaf slamming back into the wall, bits of board falling."""
    main = dsp.vari(hit_of(CRACKS[take % 2], 0, 0.5, -20), rng.uniform(-2, 0))
    keeper = ck.bounce(rng, knock(rng, LATCHES, take, 3, 0.02), n=3, first=0.12)
    wall = dsp.vari(hit_of(SLAMS[take % 2], 0, 0.4), rng.uniform(-2, 0))
    bits = ck.place([(float(t), knock(rng, WOOD_L + PLANK, take + i, rng.uniform(2, 8), 0.02), rng.uniform(-18, -10))
                     for i, t in enumerate(0.15 + 0.5 * rng.random(5))], 0.8)
    return ck.place([(0, blow_boards(rng, take + 1, 1.3, 1.0), 0), (0.01, main, -2),
                     (0.02, splinter(rng, 0.6, env([(0, 400), (0.6, 20)], 0.6), (-2, 5)), -4),
                     (0.05, keeper, -10), (0.22 + rng.uniform(0, 0.08), wall, -5), (0.25, bits, -4)])


@recipe("cs-lodger", "break", "boards",
        "A shut door battered in: heavy blows on the planks, the boards splitting, the latch torn out, the door slammed back",
        """A cottage's ledged plank door broken in over two seconds, from real hits: its whole weight thrown at the planks
        two to four times (a real heavy blow on planking and a real door's slam, dropped, its body thudding behind them), the
        door jumping in its frame each time (the latch chattering, the frame knocking), splinters cracking out of it more
        with each blow (the packs' real wood breaking, cut into grains), then it gives: a board breaking with a crack, a
        burst of splinters, the latch's keeper torn out and ticking down the floor, the leaf slamming back into the wall,
        bits of board falling. No fists and no lashing: a body against wood.""",
        sources=DECK + SLAMS + SOFT + LATCHES + WOOD_L + PLANK + CRACKS, takes=3, lufs=-18)
def break_boards(rng, k):
    times = BLOWS[k % 3]
    parts = [(t + rng.uniform(-0.03, 0.03), blow_boards(rng, k + i, 0.8 + 0.25 * i, 0.3 * i), 0) for i, t in enumerate(times)]
    parts.append((BREAK_AT + rng.uniform(-0.05, 0.05), burst_boards(rng, k), 0))
    return dry(ck.place(parts))


def blow_model(rng, force, split):
    """Its weight into a plank door, modelled: a broad burst rung through the leaf's modes (the low ones booming in its
    frame, the planks knocking above), the frame's sharp knock at the front, a dull thump of a body, and fibres tearing."""
    L = 0.35
    x = np.zeros(samples(L), np.float32)
    k = samples(0.006)
    x[:k] = rng.standard_normal(k) * np.linspace(1, 0, k)
    leaf = dsp.resonate(x, [f * rng.uniform(0.95, 1.05) for f in DOOR], q=5, gains=[1.0, 0.9, 0.8, 0.6, 0.45, 0.3])
    leaf = leaf * env([(0, 1), (L, 0.001)], L, "exp")
    frame = ck.hollow(rng, [rng.uniform(700, 900), rng.uniform(1500, 1900)], 0.04, q=3, hit=0.002)
    parts = [(0, unit(leaf), 0), (0, frame, -6), (0.002, ck.pad(rng, 0.12, 200), -4)]
    if split:
        parts.append((0.004, tear(rng, 0.15 + 0.25 * split), -14 + 8 * split))
    return ck.place([(t, y, g + 4 * np.log2(force)) for t, y, g in parts])


def tear(rng, length):
    """Wood fibres tearing: sharp crackle thinning out, over a fast stick-slip rasp."""
    c = synth.crackle(length, env([(0, 900), (length, 40)], length, "exp"), rng, size=(0.0005, 0.004), hi=600)
    f = ck.friction(rng, length, rate=400, lo=600, hi=5000, rough=0.9, shape=env([(0, 1), (length, 0)], length))
    return unit(mix(unit(c), f * 0.4))


@recipe("cs-lodger", "break", "splits",
        "A shut door battered in, modelled: booming blows on a plank door, its fibres tearing, then it bursts",
        """Modelled: each blow a broad burst rung through a ledged door's modes (its leaf booming in the frame at 120-200 Hz,
        its planks knocking above), the frame's sharp knock and a body's dull thump, fibres tearing a little more each time
        (a sharp crackle thinning over a rasp of stick-slip); then it gives: a long tear and a burst of splinters, the
        latch's iron keeper pinging off and bouncing, the leaf slamming back into the wall. Two to four blows over two
        seconds.""",
        takes=3, lufs=-18)
def break_splits(rng, k):
    times = BLOWS[(k + 1) % 3]
    parts = [(t + rng.uniform(-0.03, 0.03), blow_model(rng, 0.8 + 0.25 * i, 0.3 * i), 0) for i, t in enumerate(times)]
    at = BREAK_AT + rng.uniform(-0.05, 0.05)
    keeper = ck.bounce(rng, ck.norm(ck.tick(rng, rng.uniform(2400, 3000), q=20, length=0.04)), n=3, first=0.12)
    parts += [(at, blow_model(rng, 1.3, 1.0), 0), (at + 0.01, tear(rng, 0.5), -2), (at + 0.04, keeper, -12),
              (at + 0.25, blow_model(rng, 0.7, 0.0), -6)]
    return dry(ck.place(parts))


# ---- The Householder: its house (200 Hz-5 kHz) ----------------------------------------------------------------------------

HOUSE_L = 10.0


def clock(rng, length, beat=1.0):
    """A wall clock keeping time: the escapement's tick and its slightly different tock every `beat` s (the packs' real
    small metal clicks, pitched for brass on steel), each rung through the clock's wooden case."""
    parts = []
    for i in range(int(round(length / beat))):
        x = dsp.vari(hit_of(TICKS[i % 2], 0, 0.08, -18), (2.0, -1.0)[i % 2])
        x = ck.norm(ck.choke(hp(x, 400), 0.003, 0.012))
        body = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(WOOD_L[i % 2]), 5))), 0.004, 0.02)
        t = 0.25 + i * beat
        parts += [(t, x, -2 + rng.uniform(-0.4, 0.4)), (t, body, -15), (t, ck.hollow(rng, [240, 380, 610], 0.1, q=8), -21)]
    return ck.place(parts, length + 0.5)


def mantel(rng, length, beat=0.5):
    """A mantel clock, modelled: a quicker brass tick and tock (a click rung through a small escapement's modes) in its
    small wooden case."""
    parts = []
    for i in range(int(round(length / beat))):
        f = (3300.0, 2750.0)[i % 2]
        x = synth.click(f, q=28, length=0.03, rng=rng, body=[f, f * 1.61, f * 2.37])
        t = 0.2 + i * beat
        parts += [(t, ck.norm(x), -2 + rng.uniform(-0.4, 0.4)), (t, ck.hollow(rng, [420, 690, 1100], 0.08, q=8), -16)]
    return ck.place(parts, length + 0.5)


def stove(rng, length):
    """A lit iron stove: the fire shut in iron (a low, lapping murmur through the plate), sap ticking and cracking now and
    then through the door's vents."""
    n = samples(length)
    inside = lp(synth.fire(length, 0.5, rng), 600, 2) * town.lapping(rng, n, 2, 0.4)
    vents = synth.crackle(length, 7, rng, size=(0.0005, 0.004), hi=700)
    return mix(unit(inside), unit(vents) * 0.3)


def kettle(rng, length, water=True):
    """A kettle on the hob at the edge of the boil: the simmer's rush rung through the kettle's hollow (the packs' real
    trickle darkened, or pink noise), coming and going in slow surges, small bubbles bursting in it, and at the top of a
    surge a breathy whistle that starts to sing and never quite does."""
    n = samples(length)
    surge = np.clip(town.lapping(rng, n, 0.35, 0.9), 0, None)
    w = lp(hp(bed(rng, WATER[0], length), 200), 2200) if water else lp(synth.noise(length, rng, "pink"), 1800)
    simmer = unit(mix(w * 0.5, dsp.resonate(w, KETTLE, q=5, gains=[1, 0.7, 0.4]) * 0.5)) * (0.35 + 0.65 * surge)
    bub = unit(synth.bubbles(length, 6 + 40 * surge, 700, 3200, rng, rise=(0.1, 0.4)))
    pre = unit(dsp.sweep_filter(synth.noise(length, rng), "bp", 2350 * (1 + 0.03 * wander(rng, n, 2, 1.0)), q=9, block=128))
    return mix(simmer * 0.5, bub * 0.18, pre * np.clip(surge - 0.75, 0, None) * 0.25)


def lid(rng, length, real=True):
    """The kettle's lid lifted by the steam and ticking down, twice a cycle: a real pot's knock pitched up and choked, or
    small modelled tin ticks."""
    parts = []
    for t in (rng.uniform(1.5, 4.0), rng.uniform(6.0, 8.5)):
        for j in range(int(rng.integers(2, 4))):
            if real:
                x = ck.choke(dsp.vari(hit_of(POT[j % 3], int(rng.integers(3)), 0.1, -18), rng.uniform(8, 11)), 0.004, 0.015)
            else:
                x = ck.tick(rng, rng.uniform(2200, 3200), q=14, length=0.02)
            parts.append((t + 0.05 * j + rng.uniform(0, 0.02), ck.norm(hp(x, 900)), -6 - 4 * j))
    return ck.place(parts, length + 0.3)


def pings(rng, length, rate=0.5):
    """Hot iron pinging as it takes the heat, modelled: high, narrow little rings, Poisson in time."""
    parts = [(float(t), ck.tick(rng, rng.uniform(1300, 2600), q=35, length=0.05), rng.uniform(-22, -12))
             for t in W.poisson(length, rate, rng)]
    return ck.place(parts, length + 0.3) if parts else np.zeros(samples(length + 0.3), np.float32)


@recipe("tell-householder", "house", "kitchen",
        "Its house from the door: a stove's tick and crackle, a wall clock, a kettle at the edge of the boil",
        """The one warm, homely sound in a dead village, from real things: a wall clock's tick and tock a second apart (the
        packs' small metal clicks pitched for brass, in a wooden case), the iron stove ticking as it takes the heat (the
        packs' real metal ticks, low and dull) over its fire's murmur shut in iron and sap crackling through the vents, and a
        kettle on the hob at the edge of the boil: a real trickle darkened and rung through its hollow, surging and easing,
        bubbles bursting, a breathy whistle that starts at the top of a surge and never quite sings, the lid ticking twice
        as the steam lifts it (a real pot's knock, pitched up). In a small furnished kitchen. A 10 s loop, ten beats.
        Held to 200 Hz-5 kHz; the only thing in a village that keeps time.""",
        sources=TICKS + WOOD_L + WATER[:1] + POT, takes=1, loop=True, band=HOUSE, lufs=-26, seconds=HOUSE_L)
def house_kitchen(rng, k):
    L = HOUSE_L
    BL = L + LEAD + SEAM + 0.2
    events = mix(clock(rng, L), town.ticks(rng, L, TICKS, 0.45, -5, (-22, -13)) * 0.8, lid(rng, L) * 0.5)
    bedx = mix(stove(rng, BL) * 0.45, kettle(rng, BL) * 0.6)
    return loop_of(kitchen(events), kitchen(hp(bedx, 150)), L, HOUSE)


@recipe("tell-householder", "house", "parlour",
        "Its house from the door, modelled: a mantel clock's quick tick, a stove's fire and pinging iron, a kettle simmering",
        """Modelled: a mantel clock's quicker brass tick and tock, two a second (a click rung through an escapement's modes,
        in a small wooden case), the stove's fire murmuring behind its iron with sap crackling through the vents and the
        iron pinging as it heats, and a kettle on the hob at the edge of the boil: a simmer's rush (noise rung through its
        hollow) surging and easing, bubbles bursting, a breathy whistle at the top of a surge that never comes, the lid
        ticking twice. In a small furnished kitchen. A 10 s loop, twenty beats. Held to 200 Hz-5 kHz.""",
        takes=1, loop=True, band=HOUSE, lufs=-26, seconds=HOUSE_L)
def house_parlour(rng, k):
    L = HOUSE_L
    BL = L + LEAD + SEAM + 0.2
    events = mix(mantel(rng, L) * 0.8, pings(rng, L) * 0.8, lid(rng, L, real=False) * 0.5)
    bedx = mix(stove(rng, BL) * 0.45, kettle(rng, BL, water=False) * 0.6)
    return loop_of(kitchen(events), kitchen(hp(bedx, 150)), L, HOUSE)


# ---- The Householder: watching (400 Hz-3 kHz) -----------------------------------------------------------------------------

def chair(rng, take, length, semis=3.0):
    """A kitchen chair's joints creaking as the weight on it shifts: one of the packs' real wooden creaks, pitched up for
    a small chair's tenons and cut to `length` s."""
    c = dsp.vari(ck.align(ck.get(CREAKS[take % 3])), semis + rng.uniform(-1, 1))[:samples(length)]
    n = len(c) / SR
    return ck.norm(hp(c, 350)) * env([(0, 1), (n * 0.7, 1), (n, 0)], n)


def neck(rng, take, length):
    """Its neck as its head turns to follow you: a slow, dry creak (a real wooden creak dropped and narrowed, so it's
    gristle, not a hinge) with small dry cracks popping through it (real cracking cut into grains, dropped and darkened:
    cartilage, not wood)."""
    c = dsp.vari(ck.align(ck.get(CREAKS[(take + 1) % 3])), -5 + rng.uniform(-1, 1))[:samples(length)]
    L = len(c) / SR
    c = ck.norm(bp(c, 450, 2400)) * env([(0, 0), (L * 0.2, 1), (L * 0.8, 0.8), (L, 0)], L)
    return mix(c, lp(splinter(rng, L, 9, semis=(-9, -6)), 2500) * 0.5)


def chair_model(rng, length):
    """A kitchen chair creaking, modelled: slow stick-slip in its joints through the seat's small, dry modes."""
    s = synth.creak(length, env([(0, 25), (length, 55)], length, "exp"), rng, body=[f * rng.uniform(0.9, 1.1) for f in SEAT],
                    q=10, jitter=0.4, grit=0.4)
    return ck.norm(hp(s, 350)) * env([(0, 0), (0.02, 1), (length * 0.7, 0.9), (length, 0)], length)


def neck_model(rng, length):
    """Its neck turning, modelled: slow, catching stick-slip through soft gristle (broad, low-q modes: it grinds rather
    than rings), and a few small dry clicks of bone."""
    s = synth.creak(length, env([(0, 14), (length * 0.5, 30), (length, 18)], length, "exp"), rng, body=GRISTLE, q=4,
                    jitter=0.6, grit=0.6)
    s = ck.norm(hp(s, 300)) * env([(0, 0), (length * 0.2, 1), (length * 0.8, 0.8), (length, 0)], length)
    clicks = [(float(t), ck.norm(kit.snap(rng, 0.03, rng.uniform(1100, 1500))), rng.uniform(-14, -8))
              for t in np.sort(rng.uniform(0.05, length - 0.05, int(rng.integers(1, 4))))]
    return mix(s, ck.place(clicks, length))


def watching(rng, k, chair_, neck_):
    """Three ways of watching: the chair creaking under it, then its neck turning; its neck alone in one long slow turn;
    the chair creaking twice as it leans, and a short turn of the head."""
    if k % 3 == 0:
        parts = [(0, chair_(k, 0.4), 0), (0.3, neck_(k, 0.9), -2)]
    elif k % 3 == 1:
        parts = [(0, neck_(k, 1.4), 0)]
    else:
        parts = [(0, chair_(k, 0.3), 0), (0.45, chair_(k + 1, 0.25), -3), (0.6, neck_(k, 0.6), -4)]
    return dsp.band(kitchen(ck.place(parts), 0.12), *CREAK, order=2)


@recipe("tell-householder", "creak", "chair",
        "Watching: its chair creaking, and its neck creaking as its head follows you",
        """Nothing else while it watches. Its kitchen chair's joints creaking as its weight shifts (the packs' real wooden
        creaks, pitched up for a small chair), and its neck as its head turns after you: a real creak dropped and narrowed
        till it's gristle rather than a hinge, small dry cracks of cartilage popping through it (real cracking cut into
        grains, dropped and darkened). One take the chair then the neck, one the neck alone in a long slow turn, one the
        chair twice and a short turn. Held to 400 Hz-3 kHz.""",
        sources=CREAKS + CRACKS, takes=3, band=CREAK, lufs=-23)
def creak_chair(rng, k):
    return watching(rng, k, lambda t, L: chair(rng, t, L), lambda t, L: neck(rng, t, L))


@recipe("tell-householder", "creak", "sinew",
        "Watching, modelled: its chair's joints creaking, its neck grinding as its head turns, a click of bone",
        """Modelled: the chair's joints a slow stick-slip through a small seat's dry modes, the neck a slower, catching
        stick-slip through soft gristle (broad modes: it grinds, it doesn't ring) with a few small dry clicks of bone. One take
        the chair then the neck, one the neck alone in a long slow turn, one the chair twice and a short turn. Held to
        400 Hz-3 kHz.""",
        takes=3, band=CREAK, lufs=-23)
def creak_sinew(rng, k):
    return watching(rng, k, lambda t, L: chair_model(rng, L), lambda t, L: neck_model(rng, L))


# ---- The Householder: rising (300 Hz-5 kHz) -------------------------------------------------------------------------------

def scrape(rng, length, hard=1.0):
    """Chair legs dragged over floorboards: stick-slip, judder and all (the legs catching and skipping on the boards'
    grain, faster as it's shoved harder), rung through the chair's and the floor's deal. The packs have no chair."""
    rate = env([(0, 30 + 10 * hard), (length * 0.3, 50 + 25 * hard), (length, 35 + 10 * hard)], length, "exp")
    judder = synth.creak(length, rate, rng, body=[f * rng.uniform(0.9, 1.15) for f in DEAL + [2100]], q=6, jitter=0.5,
                         grit=0.7)
    rub = ck.friction(rng, length, rate=160 + 120 * hard, lo=400, hi=4000, rough=0.8)
    return ck.norm(mix(hp(judder, 200), rub * 0.6)) * env([(0, 0), (0.015, 1), (length * 0.7, 0.85), (length, 0)], length)


def cutlery(rng, length, n=12):
    """The table jolted: knife, fork and spoon jumping on its boards and against the plate (the packs' real small metal
    clinks and a loose-metal rattle, pitched up for thin plated steel and choked: they lie on wood), the plate and the cup
    chattering (real light china), most as the table jumps, a few settling after."""
    parts = [(0.0, ck.norm(dsp.vari(hit_of(RATTLE, 0, 0.2, -20), 3 + rng.uniform(-1, 1))), -4)]
    for t in np.sort(length * rng.random(n) ** 1.8):
        x = ck.choke(dsp.vari(hit_of(CUTLERY[int(rng.integers(len(CUTLERY)))], int(rng.integers(3)), 0.15, -18),
                              4 + rng.uniform(-2, 2)), 0.01, 0.03)
        parts.append((float(t), ck.norm(hp(x, 800)), rng.uniform(-14, -4)))
    for t in np.sort(length * 0.7 * rng.random(n // 2) ** 1.5):
        x = ck.choke(dsp.vari(hit_of(CHINA[int(rng.integers(len(CHINA)))], int(rng.integers(3)), 0.15, -18),
                              rng.uniform(-1, 2)), 0.008, 0.04)
        parts.append((float(t), ck.norm(x), rng.uniform(-16, -8)))
    return ck.place(parts, length + 0.2)


def hands(rng, take):
    """Both long hands slapped flat on the table as it pushes itself up: a real flat slap and the tabletop's knock."""
    return ck.place([(0, knock(rng, SLAPS, take, -2, 0.03), 0), (0.002, knock(rng, WOOD_M, take, -3, 0.05), -4),
                     (0.03, knock(rng, SLAPS, take + 1, -2, 0.03), -3), (0.032, ck.hollow(rng, [150, 260, 430], 0.15, q=4), -10)])


@recipe("tell-householder", "rise", "scrape",
        "Rising: the chair scraped back hard on the boards, the cutlery and plate rattling on the table",
        """The telegraph, a second and a half: its hands slapped flat on the table as it pushes up (a real flat slap and the
        tabletop's knock), the table jolting and everything on it jumping: knife, fork and spoon against the plate and
        the boards (the packs' real small metal clinks and a loose-metal rattle, pitched up for plated steel, choked), the
        plate and cup chattering (real light china); the chair shoved back hard on the floorboards (stick-slip judder through
        deal: the packs have no chair), its joints racking (a real creak), its back legs catching a board with a knock; then
        its coat swinging up and its back and knees straightening with a dry creak, the cup settling last. Held to
        300 Hz-5 kHz.""",
        sources=SLAPS + WOOD_M + CUTLERY + [RATTLE] + CHINA + CREAKS + CLOTH + CRACKS, takes=2, band=RISE, lufs=-18)
def rise_scrape(rng, k):
    drag = rng.uniform(0.5, 0.62)
    parts = [(0, hands(rng, k), 0), (0.01, cutlery(rng, 0.9), -3), (0.04, scrape(rng, drag), -1),
             (0.06, chair(rng, k + 2, 0.3), -8), (0.04 + drag, knock(rng, WOOD_M, k + 1, -2), -4),
             (0.5, ck.cloth(rng, k, 0.35), -10), (0.75, neck(rng, k, 0.6), -10),
             (1.2 + rng.uniform(0, 0.1), knock(rng, CHINA, k + 2, 1, 0.03), -16)]
    return dsp.band(kitchen(ck.place(parts, 1.6)), *RISE, order=2)


def spoon(rng):
    """A spoon or a fork jumping on the table, modelled: a thin plated bar's bending modes struck, bouncing twice."""
    f = rng.uniform(2300, 3400)
    x = synth.click(f, q=40, length=0.08, rng=rng, body=[f * r for r in SPOON])
    return ck.bounce(rng, ck.norm(x), n=2, first=rng.uniform(0.05, 0.09), drop_db=7)


def plate(rng):
    """The plate jumping on the table, modelled: a porcelain rim rung briefly, choked by the cloth under it."""
    L = 0.15
    x = np.zeros(samples(L), np.float32)
    x[:samples(0.001)] = rng.standard_normal(samples(0.001))
    y = dsp.resonate(x, [f * rng.uniform(0.97, 1.03) for f in PORCELAIN], q=40, gains=[1.0, 0.6, 0.35])
    return ck.norm(y * env([(0, 1), (L, 0.001)], L, "exp"))


@recipe("tell-householder", "rise", "judder",
        "Rising, modelled: the chair juddering back over the boards, the table jolted, spoons and the plate jumping",
        """Modelled: its hands landing on the table (a tabletop's low modes knocked), the table jolting and everything on it
        jumping (spoons and forks as thin plated bars rung and bouncing, the plate's porcelain rim ringing short, choked by
        the cloth), the chair shoved back hard, its legs juddering over the boards' grain (stick-slip through deal, skipping
        as it goes) and knocking down square, its joints creaking as it stands. A second and a half. Held to 300 Hz-5 kHz.""",
        takes=2, band=RISE, lufs=-18)
def rise_judder(rng, k):
    drag = rng.uniform(0.5, 0.62)
    table = ck.hollow(rng, [150, 260, 430, 720], 0.18, q=4)
    parts = [(0, table, 0), (0.03, ck.hollow(rng, [160, 280, 450], 0.15, q=4), -3), (0.04, scrape(rng, drag, 1.4), -1),
             (0.06, chair_model(rng, 0.3), -9), (0.04 + drag, ck.hollow(rng, [520, 980, 1650], 0.05, q=3.5, hit=0.002), -4),
             (0.75, neck_model(rng, 0.6), -10)]
    parts += [(float(t), spoon(rng), rng.uniform(-12, -4)) for t in np.sort(0.8 * rng.random(7) ** 1.8)]
    parts += [(float(t), plate(rng), rng.uniform(-16, -9)) for t in np.sort(0.6 * rng.random(3) ** 1.5)]
    return dsp.band(kitchen(ck.place(parts, 1.6)), *RISE, order=2)


# ---- The Householder: hunting ----------------------------------------------------------------------------------------------

def tread(rng, take):
    """One of its steps, from real hits: a stiff leg set down flat, so heel and sole land as one slap (a real book slapped
    down flat, dropped for a leather sole, on a real step on boards, choked), the old shoe's dry leather creaking as it
    takes the weight, and on some steps a knee's dry click as the leg locks (a real crack's grain, dropped)."""
    step = ck.choke(dsp.vari(hit_of(STEPS[take % 5], 0, 0.2), -3 + rng.uniform(-0.4, 0.4)), 0.025, 0.04)
    slap = ck.choke(dsp.vari(hit_of(SLAPS[take % 2], 0, 0.15), -4 + rng.uniform(-0.4, 0.4)), 0.012, 0.03)
    shoe = lp(hit_of(LEATHER[take % 2], int(rng.integers(2)), 0.14), 4000)
    parts = [(0, ck.norm(step), -2), (0.003, ck.norm(slap), -3), (0.03, shoe, -15)]
    if take % 2 == 0:
        parts.append((0.07 + rng.uniform(0, 0.02), crack(rng, (-8, -6)), -18))
    return dry(ck.place(parts))


def tread_model(rng, take):
    """One of its steps, modelled: the whole foot coming down at once (a flat slap of leather on wood, the floor's dull
    weight and its knock), stiff leather creaking under the weight, and on some steps a knee's dry click."""
    slap = ck.norm(bp(synth.noise(0.03, rng), 600, 4500) * env([(0, 1), (0.004, 0.5), (0.03, 0)], 0.03, "exp"))
    board = ck.norm(ck.choke(ck.hollow(rng, [f * rng.uniform(0.9, 1.1) for f in DEAL], 0.07, q=3.5, hit=0.002), 0.006, 0.02))
    weight = ck.pad(rng, 0.08, 230)
    creak = synth.creak(0.1, rng.uniform(70, 110), rng, body=[700, 1300, 2100], q=5, jitter=0.4, grit=0.5)
    creak = ck.norm(creak) * env([(0, 0), (0.02, 1), (0.1, 0)], 0.1)
    parts = [(0, slap, -4), (0.001, weight, -2), (0.002, board, -5), (0.035, creak, -16)]
    if take % 2 == 0:
        parts.append((0.07 + rng.uniform(0, 0.02), ck.norm(kit.snap(rng, 0.03, rng.uniform(1100, 1400))), -18))
    return dry(ck.place(parts))


@recipe("cs-householder", "step", "shoes",
        "Hunting: a stiff, even tread in old leather shoes, flat-footed, the leather creaking",
        """A stiff leg set down flat, so heel and sole land as one slap, never a crewmate's heel-and-toe: a real book slapped
        down flat (dropped for a leather sole) on a real step on boards, choked short; the old shoe's dry leather creaking as
        it takes the weight (the packs' leather handling), and on every other step a knee's dry click as the leg locks (a
        real crack's grain, dropped). Four takes the same weight and distance, for an even tread that never hurries.""",
        sources=STEPS + SLAPS + LEATHER + CRACKS, takes=4, lufs=-21, preview=lambda t, r: ck.gait(t, r, 0.5, 12, sway=0.003))
def step_shoes(rng, k):
    return tread(rng, k)


@recipe("cs-householder", "step", "stiff",
        "Hunting, modelled: a stiff, flat-footed tread, the leather creaking, a knee clicking",
        """Modelled: the whole foot down at once (a short flat slap of leather on wood, the floor's dull weight and its
        knock through deal), stiff shoe leather creaking under the weight (a short stick-slip through leather's modes), and
        on every other step a knee's dry click. Four takes, even, for a tread that never hurries.""",
        takes=4, lufs=-21, preview=lambda t, r: ck.gait(t, r, 0.5, 12, sway=0.003))
def step_stiff(rng, k):
    return tread_model(rng, k)


BREATH_HL = 8.0     # four even breaths


def rasp(rng, inhale, exhale):
    """One of its breaths from real air: the packs' gasp drawn out, dragged in through a narrowed, dry throat (it whistles a
    little) and out again, both rattling dry (the air gated 30-40 times a second: dry folds, no spit)."""
    i_e, o_e, L = shape(inhale, exhale, 0.06, 0.25)
    grate = 0.35 + 0.65 * fry(rng, rng.uniform(32, 40), L, jitter=0.35, decay=0.006)
    air_in = dsp.peak(bp(drawn(rng, GASP, L, -3 + rng.uniform(-0.5, 0.5)), 500, 4000), 1600, 4, 8)
    air_out = dsp.peak(bp(drawn(rng, GASP, L, -5 + rng.uniform(-0.5, 0.5)), 300, 3500), 900, 3, 6)
    return mix(unit(air_in) * i_e * (0.55 + 0.45 * grate) * 0.8, unit(air_out) * o_e * grate)


def croak(rng, inhale, exhale):
    """One of its breaths, modelled: air hoarse through a narrowed throat on the way in (plain air coloured 'h' to 'i'), and
    out through a dry, creaking voice (a glottis at 40-50 Hz: vocal fry, the voice of a throat with nothing wet left in it)."""
    i_e, o_e, L = shape(inhale, exhale, 0.06, 0.25)
    air_in = air(rng, L, [(0, "h"), (inhale, "i")], "man", 500, 4000) * i_e
    air_out = air(rng, L, [(0, "a"), (L, "h")], "man", 250, 3000) * o_e
    g = synth.glottis(rng.uniform(40, 50), L, rng, jitter=0.08, shimmer=0.5, oq=0.3, sub=0.5)
    voice = synth.tract(g, [(0, "a"), (L, "o")], "man", breath=0.8, rng=rng) * o_e
    return mix(unit(air_in) * 0.7, unit(air_out) * 0.8, unit(voice) * 0.4)


def hunting(rng, one):
    """Four breaths in an 8 s cycle, exactly two seconds apart: it never hurries, never stops."""
    b = Bus(BREATH_HL + 3)
    for j in range(4):
        b.at(0.1 + 2.0 * j, one(rng, rng.uniform(0.7, 0.8), rng.uniform(0.9, 1.0)), rng.uniform(-0.8, 0))
    return loop_of(dry(b.x), None, BREATH_HL)


@recipe("cs-householder", "breath", "rasp",
        "Hunting: a dry, rasping breath, even as a clock",
        """From real air: the packs' gasp (sfx_100 misc_05) drawn out and dragged in through a narrowed, dry throat (it
        whistles a little, round 1.6 kHz) and out again lower, both rattling dry (the air gated 30-40 times a second: dry
        folds, no spit, nothing like the Lodger's wet). Four breaths in an 8 s cycle, exactly two seconds apart, as even as
        its tread. A loop.""",
        sources=[GASP], takes=1, loop=True, lufs=-24, seconds=BREATH_HL)
def breath_rasp(rng, k):
    return hunting(rng, rasp)


@recipe("cs-householder", "breath", "fry",
        "Hunting, modelled: a hoarse breath in, a dry creaking voice out",
        """Modelled: air hoarse through a narrowed throat on the way in, and out through a dry, creaking voice (a glottis at
        40-50 Hz through a man's tract: vocal fry, a throat with nothing wet left in it). Four breaths in an 8 s cycle,
        exactly two seconds apart. A loop.""",
        takes=1, loop=True, lufs=-24, seconds=BREATH_HL)
def breath_fry(rng, k):
    return hunting(rng, croak)


def grunt(rng, take):
    """A grunt from the packs' real throats: a quarter second of the groan (or the roar), dropped a little, clipped to a
    hard 'hnh' (a fast swell, choked), dry."""
    x = dsp.vari(core((GROAN, ROAR)[take % 2], 0.3), -2 + rng.uniform(-1, 1))[:samples(0.24)]
    L = len(x) / SR
    return unit(lp(dsp.saturate(unit(x) * env([(0, 0), (0.015, 1), (L * 0.4, 0.7), (L, 0)], L), 4), 3000))


def grunt_model(rng, f0, L=0.22):
    """A grunt, modelled: a short, rough, dry voice falling on 'a' to a shut 'm', through a man's tract."""
    g = synth.glottis(env([(0, f0 * 1.15), (L, f0 * 0.8)], L, "exp"), L, rng, jitter=0.03, shimmer=0.3, oq=0.5, sub=0.4,
                      rough=0.5)
    v = synth.tract(g, [(0, "a"), (L * 0.6, "u"), (L, "m")], "man", breath=0.5, rng=rng)
    return unit(lp(dsp.saturate(v * env([(0, 0), (0.012, 1), (L * 0.5, 0.6), (L, 0)], L), 5), 3500))


def strap(rng):
    """A coat's belt or a strap creaking under the pull."""
    s = synth.creak(0.2, rng.uniform(40, 70), rng, body=[300, 700, 1300], q=6)
    return ck.norm(s) * env([(0, 0), (0.04, 1), (0.2, 0)], 0.2)


@recipe("cs-householder", "grab", "clutch",
        "The grab: a hand slammed onto a coat and the cloth wrenched up in a fist, and a grunt",
        """A hard clutch: the long hand landing on the coat (a real heavy leather drop) with the body under it taking it (a
        real soft blow), the cloth bunched hard in the fist (two of the packs' cloth handlings, the sharp starts of them), a
        belt and its buckle creaking under the pull, and its grunt: a quarter second of the packs' real groan (or roar),
        dropped and clipped to a hard 'hnh'. Dry: out in the village, the world's reverb is the game's.""",
        sources=[DROP_LEATHER] + SOFT + CLOTH + BELT + [GROAN, ROAR], takes=2, lufs=-19)
def grab_clutch(rng, k):
    body = ck.choke(hit_of(SOFT[k % len(SOFT)], 0, 0.25), 0.04, 0.05)
    return dry(ck.place([(0, hit_of(DROP_LEATHER, 0, 0.25), -2), (0.002, body, -6),
                         (0.02, ck.cloth(rng, k, 0.28), 0), (0.07, ck.cloth(rng, k + 2, 0.3), -2),
                         (0.08, strap(rng), -14), (0.09, hit_of(BELT[k % 2], 0, 0.1), -16), (0.06, grunt(rng, k), -3)]))


@recipe("cs-householder", "grab", "grunt",
        "The grab, modelled: a hand thudding onto a coat, the knuckles cracking as it clenches, the cloth wrenched, a grunt",
        """Mostly modelled: the hand's weight landing on a body through a coat (a dull thud and a slap of cloth, synth), its
        long knuckles cracking as it clenches (small dry clicks of bone), the cloth wrenched up in the fist (one of the
        packs' cloth handlings), a strap creaking under the pull, and a short, rough, dry grunt from a modelled throat
        (a glottis falling on 'a' to a shut 'm').""",
        sources=CLOTH, takes=2, lufs=-19)
def grab_grunt(rng, k):
    thud = kit.thud(rng, 90, 0.2, 0.8)
    slap = kit.slap(rng, 0.08, 1800, wet=0.0)
    knuckles = [(0.03 + 0.012 * i + rng.uniform(0, 0.006), ck.norm(kit.snap(rng, 0.025, rng.uniform(1300, 1900))), -12 - 2 * i)
                for i in range(int(rng.integers(2, 4)))]
    return dry(ck.place([(0, ck.norm(thud), -4), (0, ck.norm(slap), -6), (0.04, ck.cloth(rng, k + 1, 0.3), -2),
                         (0.09, strap(rng), -14), (0.05, grunt_model(rng, rng.uniform(85, 100)), -3)] + knuckles))


# ---- The Householder: going home ------------------------------------------------------------------------------------------

@recipe("cs-householder", "settle", "home",
        "Going home: the thing set back down on the table, the chair drawn in, its weight settling",
        """The thing set back down where it belongs (a real wooden knock on the table, a china clink under it), the hand
        drawn back (cloth), then the chair drawn in over the boards, slow and short this time (stick-slip through deal), its
        legs knocking square and its joints creaking as its weight comes down on it (the packs' real creak), and the cutlery
        touched straight with a small clink. In its kitchen, about two seconds.""",
        sources=WOOD_L + SLAPS + CHINA + CLOTH + CREAKS + WOOD_M + CUTLERY, takes=2, lufs=-21)
def settle_home(rng, k):
    tink = ck.choke(dsp.vari(hit_of(CUTLERY[k % len(CUTLERY)], 0, 0.15, -18), 4 + rng.uniform(-1, 1)), 0.01, 0.03)
    parts = [(0, knock(rng, (WOOD_L, SLAPS)[k % 2], k, 1, 0.04), 0), (0.003, knock(rng, CHINA, k, 0, 0.03), -9),
             (0.3, ck.cloth(rng, k + 1, 0.25), -12), (0.7, scrape(rng, 0.32, 0.4), -7), (0.72, chair(rng, k, 0.3), -12),
             (1.04, knock(rng, WOOD_M, k + 2, -3, 0.04), -8), (1.12, chair(rng, k + 1, 0.45), -5),
             (1.6 + rng.uniform(0, 0.15), ck.norm(hp(tink, 800)), -16)]
    return kitchen(ck.place(parts, 2.1), 0.12)


@recipe("cs-householder", "settle", "sit",
        "Going home, modelled: a thing set down on the table, the chair drawn in, a creak as it sits",
        """Modelled: the thing set down on the table (a small knock through the tabletop's modes), the chair drawn in over
        the boards (a short, slow stick-slip judder through deal) and its legs knocking square, its joints creaking as it
        takes the weight (stick-slip through a seat's small modes), a spoon's small ring as the cutlery is set straight.
        About two seconds.""",
        takes=2, lufs=-21)
def settle_sit(rng, k):
    f = rng.uniform(2600, 3200)
    tink = ck.norm(synth.click(f, q=40, length=0.06, rng=rng, body=[f * r for r in SPOON]))
    parts = [(0, ck.hollow(rng, [210, 380, 640, 1100], 0.1, q=5, hit=0.002), 0), (0.7, scrape(rng, 0.32, 0.4), -7),
             (1.04, ck.hollow(rng, [520, 980, 1650], 0.05, q=3.5, hit=0.002), -6), (1.1, chair_model(rng, 0.45), -5),
             (1.6 + rng.uniform(0, 0.15), tink, -16)]
    return kitchen(ck.place(parts, 2.1), 0.12)
