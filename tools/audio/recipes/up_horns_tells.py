"""U1's upgrades heard (queue #505, note 775; U1's #967, notes 678, 697 and 698; the checklist's up- rows, U1's requests
of 13 Oct): the whistles bought at the blacksmith in its place (up-horns), and the moment each tool upgrade does its job
(up-tells). Built ahead of #967 for the director to judge; hooked when it lands.

The director (docs/design/upgrades.md §8.1): "all of these are going to need hard signs and feedback for the player to
understand what the consequence is." So each upgrade's moment is a sound of its own, heard where it happens: the train's
own sound, changed by what was bought. Never a creature's tell (spec A.4) and never one of the train's alarms. What each
is decides the build:
- The honk (whimsy): a clown's rubber bulb horn where the whistle was, a honk on every pull of the cord. Squeezing the
  bulb drives air through a beating reed into a short brass horn: buzzy and nasal, the pitch scooping up into the note
  as the squeeze builds and sagging as the bulb empties (the joke is the sag), the rubber squeaking under the hand, and
  as the bulb fills again back through the reed, a small, higher 'hink'. Above the cab where the whistle was, loud,
  out in the night.
- The gold-plated whistle (silly): the cab's three-chime whistle (E flat, G, B flat: crew_train's), brighter and a
  quarter louder, and that isn't better. The same three bells and the same chord, but each bell's harmonics stand up
  (falling off as 1/h^0.85 to the tenth, where the plain whistle's fall as 1/h^1.4 to the sixth), a little shrill on
  top, and 3 dB over the plain whistle (a quarter more loudness). The onset, the held loop (an exact 3 s cycle) and the
  tail match each other as crew_train's do; the tail thins back towards the plain whistle's tone as the pressure goes.
- The speed recorder's bell (tool): a small brass gong on the backhead, struck once by the recorder's clockwork as a
  bend comes up, earlier than the warning would be. One small, bright stroke at 2.4-2.7 kHz with the tick of its trip
  before it: never the overspeed's alarm (the communication bell over the driver, a 1.2 kHz bell struck twice,
  warnings.py).
- Pre-cut boards: a breach boarded in half the time: crew-repair's nails driven light and quick, a tap and a blow each,
  four or five blows a second, into boards already cut to the hole, the last one flush.
- Gun rammers: a sprung steel rammer seating the ball: its catch let go, the rod's slide down the bore and a clean steel
  clack on the ball (crew_cannon's bore round it), the spring throwing it back onto its catch. Quicker and cleaner than
  the wooden rammer's strokes.
- The trailing-truck booster: the second engine under the cab cutting in as the train pulls away: its idler gear
  dropping into mesh, its throttle's steam, and its own exhaust, quicker and lighter than the chuff (world_bed's blast
  model), four beats a turn of its geared cylinders, quickening, under the chuff for a second or two.
- Composition brake shoes: on a long descent the shoes bite without the iron shoes' scream: the bed's brake friction
  (world_bed, world_state) through a softer shoe, the wheel's ring damped, a lesser, duller moan near 1 kHz where the
  iron shoes squeal at 1.9 kHz and glaze into a whine.
- Axle oilers: a Hotbox's knock (g1_train's casting) eased by oil: slower and softer, each knock cushioned and dull, the
  oil cup on the box ticking with it, a drop of oil falling between. Never the tell's steady knock once a wheel turn.
- The lucky horseshoe (whimsy): a glint when a foul didn't happen: a tiny, bright run of high tings rising, a shimmer
  hanging after it, under a second: the only sparkle on the train.

Each cue has two candidates. The first is from the packs' real recordings, or kitbashed from the real sources of the
train sound it's a variant of: a real held reed note (sfx_100 misc_12) for the horn and real leather for its rubber;
real locomotive steam (loop_water_03, misc_22) sounding the whistle's bells; real small steel and clockwork ticks for
the gong; the repair's own hammer, nail and wooden knocks; a real blade drawn for the rammer's slide and real latches and
iron for its clack; the real steam's own puffs for the booster's beats; a real metal scrape and a real hinge's squeal for
the shoes; the tell's real heavy strikes and a real plop for the oil; real steel, glass and coins for the glint. The
second is modelled from the same physics as the train's own models: a beating reed in a horn, the three-chime whistle's
additive bells, struck modes for the gong, the nails, the boards, the rammer, the casting and the horseshoe,
stick-slip friction, the blast-pipe exhaust and a drop's ring.

Every take keeps its weight out of the squish under 150 Hz (the director's rule): impacts are high-passed and nothing is
a boom. The gold loop is built periodic (its notes a whole number of cycles, its noise, filters and room run round the
cycle) and handed to build.py's seam exactly.
"""

import functools

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import kit
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_items import hit_of
from recipes.crew_cannon import bore
from recipes.crew_train import cab, iron
from recipes.g1_train import BOX, CASTING, box_thud, cast_knock, loopify, peaks
from recipes.gannet_tells import bend
from recipes.house_folk import core, drawn, held_cry
from recipes.mishaps_world import CHIME, above_cab
from recipes.stress import strongest
from recipes.world_bed import CHIMNEY, WHEEL, exhaust, outdoors, rigging

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

REED = "sfx_100_v2:misc_12"                  # a real held reed note: buzzy, 685 Hz, harmonics to 10 kHz, a second's blow
REED_F = 685.0
LEATHER = R("handleSmallLeather", "handleSmallLeather2")    # real leather handled: the rubber bulb under the hand
STEAM = "sfx_100_v2:loop_water_03"           # a real steam hiss off a locomotive, with its own exhaust puffs
VALVE = "sfx_100_v2:misc_22"                 # a steam vehicle's hiss: a valve's rush
STEELS = K("impactMetal_light")              # small steel struck: a clean ring at 2.3-3 kHz over a partial near 0.9 kHz
TICK = "sfx_100_v2:metal_04"                 # a real small mechanism's ticks (crew_items' wick winder): clockwork
HAMMER = "sfx_100_v2:misc_19"                # a hammer's face on metal (crew-repair's tak)
METAL_H = K("impactMetal_heavy")             # heavy metal hits: crew-repair's nail, pitched up
PLANK = K("impactPlank_medium")              # a plank's hollow knock: the board set against the hole
DRAW = R("drawKnife2", "drawKnife1")         # a real blade drawn: steel sliding on steel
LATCHES = S("door_04", "door_05", "door_02") # real latches dropping: iron into iron
CLANK = "sfx_100_v2:metal_hit_02"            # a sharp, bright hit of metal
RATCHET = "sfx_100_v2:misc_20"               # a real ratchet and pawl: two teeth
SCRAPE = "sfx_100_v2:misc_10"                # a real metal scrape in a few strokes
HINGES = R("creak2", "creak3")               # real hinge squeals: stick-slip singing near 1.1 kHz
HINGE_F = 1089.0
PLATE_H = K("impactPlate_heavy")             # heavy iron plate struck (the tell's knock)
PLATE_M = K("impactPlate_medium")
MINING = K("impactMining")                   # a pick into rock: a heavy dull strike (the tell's knock)
PLOP = K("impactGeneric_light")              # a real 'plop': a low click and a tone dying in 60 ms
WET = S("footstep_wet_01")                   # a real wet squelch
GLASS = K("impactGlass_light")               # light glass knocked
RINGING = S("glass_05")                      # struck glass that rings on (3.6 kHz)
COINS = R("handleCoins2")                    # coins handled: a few small bright chinks
CLICK = R("metalClick")                      # a small real metal click


def semis(hz, base):
    """Semitones from a recording's own pitch `base` to `hz` (a constant or a curve)."""
    return (12 * np.log2(np.asarray(hz, np.float64) / base)).astype(np.float32)


def lv(x, lufs):
    """A take as build.py levels it, for the previews that string several cues together."""
    return ck.leveled(hp(np.asarray(x, np.float32), 25, 2), lufs)


def low_cut(x, f=150, order=2):
    """The weight kept in the mids: nothing left to boom under `f` (the director's rule for every take here)."""
    return hp(np.asarray(x, np.float32), f, order)


def by_train(y, rng, wet=0.1, tail=0.3):
    """Outdoors by the train (world_bed.outdoors: two slaps off its sides and a little night), the night's long tail cut
    short so the moment ends when it does and a take fired again doesn't pile up a wash."""
    return dsp.fade(dsp.fit(outdoors(y, rng, wet), len(y) + samples(tail)), 0.0, tail)


# ---- The honk -------------------------------------------------------------------------------------------------------------
# A rubber bulb horn: a squeeze drives air through a beating reed into a short brass horn. The reed buzzes at its own
# pitch (the horn's modes pull it into tune and make it nasal); the pitch scoops up as the squeeze builds and sags as
# the bulb empties. Let go, the bulb fills again back through the reed: a smaller, higher 'hink' on a good squeeze.

HONK_HZ = (520.0, 560.0, 490.0)                  # each take's reed (Hz): a bulb horn's buzz
HORN = [575, 1150, 1725, 2300, 2875, 3450]       # a short brass horn's modes (about 30 cm, open at the bell)


def honk_plan(k):
    """Three pulls on the cord: a firm honk; a quick jab; a long squeeze that runs out and sags. Its length (s), its
    pitch [(t, semitones)], the pressure in the bulb [(t, 0..1)] and whether the bulb's filling is heard as a 'hink'."""
    if k % 3 == 0:
        L = 0.4
        return (L, [(0, -1.2), (0.04, 0), (0.25, 0.25), (0.3, 0.1), (L, -2.6)],
                [(0, 0), (0.012, 1), (0.28, 0.95), (0.34, 0.6), (L, 0)], True)
    if k % 3 == 1:
        L = 0.27
        return (L, [(0, -0.8), (0.03, 0.1), (0.17, 0), (L, -1.8)], [(0, 0), (0.01, 1), (0.17, 0.9), (0.22, 0.5), (L, 0)],
                False)
    L = 0.52
    return (L, [(0, -1.5), (0.05, 0), (0.22, 0.4), (0.32, 0.1), (L, -4.2)],
            [(0, 0), (0.015, 1), (0.24, 1.0), (0.36, 0.7), (0.45, 0.35), (L, 0)], True)


def reed_rec(rng, L, hz, pitch):
    """The packs' real held reed note (sfx_100 misc_12) from its steady middle, pulled along `pitch` (semitones per sample
    over `hz`, varispeed): its own buzz and grain at the horn's pitch."""
    n = samples(L)
    c = semis(hz, REED_F) + synth.curve(pitch, n)
    need = float(np.sum(2 ** (c / 12))) / SR
    x = core(REED, round(min(0.9, max(L, need) + 0.03), 2))     # bend reads its curve over the source's length
    return ck.norm(dsp.fit(bend(x, c), n))


def reed_model(rng, L, hz, pitch):
    """A beating reed, modelled: its flow a train of short pulses (the reed shut most of each cycle, so it buzzes), rung
    through the horn's near-harmonic modes, and air hissing through the reed's slit each time it opens."""
    n = samples(L)
    f = hz * 2 ** (synth.curve(pitch, n) / 12)
    src = synth.glottis(f, L, rng, jitter=0.002, shimmer=0.03, oq=0.3)
    horn = dsp.resonate(src, HORN, q=7, gains=[1, 0.8, 0.65, 0.5, 0.4, 0.3])
    ph = np.cumsum(f) / SR % 1.0
    slit = bp(synth.noise(L, rng), 2000, 7000) * (ph < 0.3)
    return ck.norm(mix(ck.norm(src) * 0.45, ck.norm(horn) * 0.8, ck.norm(slit) * 0.07))


def squeak_rec(rng, k):
    """The rubber bulb squeezed: the packs' real leather handled, its loudest few hundredths, the hand's grip on rubber."""
    x = core(LEATHER[k % 2], 0.07)
    return ck.norm(dsp.fade(hp(dsp.vari(x, rng.uniform(1, 3)), 500), 0.008, 0.03))


def squeak_model(rng, k):
    """The rubber bulb squeezed, modelled: a hand's grip slipping on rubber (fast stick-slip, a squeak's band)."""
    L = 0.07
    return ck.friction(rng, L, rng.uniform(700, 1000), 900, 4500, 0.3, env([(0, 0), (0.015, 1), (L, 0)], L))


def honk(rng, k, tone, squeak):
    """One pull: the bulb squeezed (the rubber under the hand), the reed's honk riding the bulb's pressure (brighter at
    full squeeze, dull at either end), the horn's nasal peak; then the bulb filling again: a breathy 'hink' a fifth up,
    or only the air going back in."""
    L, pitch, press, refill = honk_plan(k)
    hz = HONK_HZ[k % 3]
    p = env(press, L)
    y = tone(rng, L, hz, env(pitch, L)) * p
    y = dsp.sweep_filter(y, "lp", 1800 + 6500 * p, q=0.7)
    y = dsp.peak(y, 1500, 1.0, 3)
    parts = [(0, squeak(rng, k), -16), (0.02, hp(y, 300, 2), 0)]
    Lh = 0.09
    air = bp(synth.noise(Lh, rng), 2000, 6000) * env([(0, 0), (0.02, 1), (Lh, 0)], Lh)
    if refill:
        hink = tone(rng, Lh, hz * 1.5, env([(0, 0.3), (Lh, -0.8)], Lh)) * env([(0, 0), (0.015, 1), (0.05, 0.6), (Lh, 0)], Lh)
        parts.append((0.02 + L + 0.04, lp(mix(ck.norm(hink), ck.norm(air) * 0.35), 5000), -14))
    else:
        parts.append((0.02 + L + 0.03, ck.norm(air), -24))
    return above_cab(ck.place(parts), 0.2)


@recipe("up-horns", "honk", "horn",
        "The bulb horn where the whistle was: a clown's honk, buzzy and nasal, scooping up and sagging, from a real reed",
        """From real recordings: the packs' real held reed note (sfx_100 misc_12, a buzzy 685 Hz with its harmonics to
        10 kHz) from its steady middle, pulled down to a bulb horn's 490-560 Hz and along its pitch: scooping up a
        semitone into the note as the bulb is squeezed, a hair sharp at the top of the squeeze, sagging two to four
        semitones as the bulb empties; brighter at full squeeze and dull at either end, with a brass horn's nasal peak.
        The rubber bulb squeaks under the hand (the packs' real leather handled) and fills again after with a small
        breathy 'hink' a fifth up. Takes: a firm honk; a quick jab; a long squeeze that runs out and sags. Above the cab
        where the whistle was, out in the night.""",
        sources=[REED] + LEATHER, takes=3, lufs=-16)
def honk_horn(rng, k):
    return honk(rng, k, reed_rec, squeak_rec)


@recipe("up-horns", "honk", "reed",
        "The bulb horn, modelled: a beating reed in a short brass horn, squeezed, scooping up and sagging",
        """Modelled: a beating reed (its flow a train of short pulses, the reed shut most of each cycle, so it buzzes) at
        490-560 Hz, rung through a short brass horn's near-harmonic modes (575 Hz up), air hissing through the reed's slit
        each time it opens; the same pitch and pressure as 'horn' (scooping up into the note, sagging as the bulb empties,
        brighter at full squeeze), a hand's grip squeaking on the rubber, the bulb filling again with a small 'hink'. The
        same three takes as 'horn'. Above the cab, out in the night.""",
        takes=3, lufs=-16)
def honk_reed(rng, k):
    return honk(rng, k, reed_model, squeak_model)


# ---- The gold-plated whistle ----------------------------------------------------------------------------------------------
# crew_train's three-chime whistle (E flat, G, B flat): three bells blown by one jet, each a breathy harmonic tone. Gold
# plate doesn't change the notes; what's bought is a whistle blown harder that rings brighter: each bell's harmonics
# stand up (1/h^0.85 to the 10th against the plain whistle's 1/h^1.4 to the 6th), a presence on top that's a little
# shrill, and a quarter more loudness (3 dB). Both candidates put the same harmonic weights on each bell's phase
# (locked onto real steam, or modelled), so the onset, the loop and the tail of each are one whistle.

GOLD_TILT = 0.85          # the gold bells' harmonics fall off as 1/h^GOLD_TILT ...
GOLD_HARM = 10            # ... to the GOLD_HARM-th
PLAIN_TILT = 1.4          # crew_train.whistle_tone's, which the tail thins back towards as the pressure goes
BELL_GAIN = (1.0, 0.85, 0.75)    # the three bells' levels (crew_train's)
GOLD_Q = 160              # a bell locked onto real steam: the jet's noise rung until a tone a couple of hertz wide is left
GOLD_LOOP = 3.0           # the held loop's cycle (s)
GOLD_ROOM = 9             # crew_train's whistle space (its night room's seed)
PRE = 0.5                 # the real steam's run-in before a one-shot: the bells locked and their level settled unheard
WANDER = 0.003            # how far the jet pulls a locked bell off its note (a share: about 5 cents)


def harmonics(tilt, count=GOLD_HARM):
    """A bell's harmonic weights: 1/h^tilt to the count-th (tilt a constant or a curve per sample)."""
    return [np.asarray(1.0 / h ** tilt, np.float32) for h in range(1, count + 1)]


def shrill(y):
    """The gold's top: a presence 3 dB up round 3.2 kHz, a little shrill."""
    return dsp.peak(y, 3200, 0.9, 3.0)


def phase(f, loop=False):
    """A frequency curve's phase (radians per sample), summed in double precision (in single, three seconds of a 300 Hz
    note drift a third of a cycle and the loop's seam clicks); in a loop, a whole number of cycles round it."""
    f = np.asarray(f, np.float64)
    if loop:
        cycles = float(np.sum(f)) / SR
        f = f * (round(cycles) / cycles)
    return 2 * np.pi * np.cumsum(f) / SR


def smooth(v, f, loop=False):
    """Zero-phase smoothing under `f` Hz: round the cycle for a loop, else over the curve mirrored onto its own end (so
    neither end is pulled towards the other)."""
    shape = lambda fr: 1 / (1 + (fr / f) ** 4)       # noqa: E731
    if loop:
        return W.cfilter(np.asarray(v, np.float32), shape)
    v = np.asarray(v, np.float32)
    return W.cfilter(np.concatenate([v, v[::-1]]), shape)[:len(v)]


def lock(x, f, loop=False):
    """One bell sounded by the jet: the noise `x` rung at the bell's note `f` (Hz, a constant, or a curve per sample for a
    bell coming into tune or sagging) until a band a couple of hertz wide is left, and the bell's tone locked onto that
    band: it takes the band's own frequency moment to moment, weighted by the band's strength (where the noise cancels
    itself out the bell holds its note rather than jump, as a whistle's edge tone does). The bell's phase (radians per
    sample); in a loop, a whole number of cycles round it."""
    if loop:
        r = W.cyclic(lambda z: dsp.resonate(z, [float(f)], q=GOLD_Q), x)
    else:
        r = dsp.sweep_filter(x, "bp", synth.curve(f, len(x)), q=GOLD_Q, block=64)
    z = signal.hilbert(r)
    a2 = (np.abs(z) ** 2).astype(np.float32)
    step = np.diff(np.unwrap(np.angle(z)))
    # the step into the first sample: round the cycle from the last in a loop, none at the start of a one-shot
    first = np.angle(z[0] * np.conj(z[-1])) if loop else 0.0
    fi = (np.concatenate([[first], step]) * SR / (2 * np.pi)).astype(np.float32)
    fs = smooth(fi * a2, 4, loop) / (smooth(a2, 4, loop) + 1e-12)
    # held within a few cents of the note: the bell sets the pitch, the jet only nudges it
    ft = synth.curve(f, len(x)).astype(np.float64)
    fs = ft * (1 + WANDER * np.tanh((fs / ft - 1) / WANDER))
    return phase(fs, loop)


def steam_rec(length, at=1.0):
    """The packs' real locomotive steam (sfx_100 loop_water_03) from `at` s, its puffs evened out (its level followed
    from half a second before, so it starts even): the jet, steady."""
    x = ck.get(STEAM)
    a = samples(at)
    a0 = max(0, a - samples(0.5))
    y = x[a0:a + samples(length)]
    y = y / (lp(np.abs(y), 3, 2) + 0.02 * np.max(np.abs(y)) + 1e-9)
    return dsp.fit(y[a - a0:].astype(np.float32), samples(length))


def steam_chord(rng, jet, bend_=0.0, tilt=GOLD_TILT, loop=False):
    """The gold whistle's three bells sounded by real steam: each bell locked onto the jet's noise at its note (bent by
    `bend_` semitones per sample), so its pitch wanders as the steam's turbulence moves it; the gold's harmonics on
    that; the jet's own gusts in their level (one jet blows all three); the steam rung round each partial breathing
    under it."""
    n = len(jet)
    out = np.zeros(n, np.float32)
    b = synth.curve(bend_, n)
    w = harmonics(synth.curve(tilt, n) if not np.isscalar(tilt) else tilt)
    e = smooth(np.abs(jet), 25, loop)
    gust = np.clip(1 + 0.1 * (e - np.mean(e)) / (np.std(e) + 1e-9), 0.7, 1.3)
    for i, f0 in enumerate(CHIME):
        ph = lock(jet, f0 if loop else f0 * 2 ** (b / 12), loop) + rng.uniform(0, 2 * np.pi)
        tone = sum(w[h - 1] * np.sin(h * ph) for h in range(1, GOLD_HARM + 1))
        bands = [f0 * h for h in (1, 2, 3)]
        if loop:
            breath = W.cyclic(lambda z: dsp.resonate(z, bands, q=25, gains=[1, 0.4, 0.2]), jet)
        else:
            breath = dsp.resonate(jet, bands, q=25, gains=[1, 0.4, 0.2])
        out += (ck.norm(tone) * gust + ck.norm(breath) * 0.3) * BELL_GAIN[i]
    return ck.norm(out)


def gilt_chord(rng, n, bend_=0.0, tilt=GOLD_TILT, loop=False):
    """The gold whistle's three bells, modelled: crew_train.whistle_tone's breathy harmonic bells with the gold's
    harmonics (1/h^0.85 to the 10th), a turbulent flutter in each bell's level and steam noise rung round each partial.
    `loop`: every note a whole number of cycles over n, its wander, flutter and noise periodic, so the held loop loops."""
    L = n / SR
    b = synth.curve(bend_, n)
    w = harmonics(synth.curve(tilt, n) if not np.isscalar(tilt) else tilt)
    out = np.zeros(n, np.float32)
    for i, f0 in enumerate(CHIME):
        if loop:
            f = f0 * (1 + 0.004 * W.slow(n, 2, rng))
            flutter = 1 + 0.08 * W.slow(n, 30, rng)
            breath = W.cyclic(lambda z: dsp.resonate(z, [f0, f0 * 2, f0 * 3], q=25, gains=[1, 0.4, 0.2]), W.pnoise(n, rng))
        else:
            f = f0 * 2 ** (b / 12) * (1 + lp(rng.standard_normal(n).astype(np.float32), 2) * 4 * 0.004)
            flutter = 1 + 0.08 * lp(rng.standard_normal(n).astype(np.float32), 30) * 15
            breath = dsp.resonate(synth.noise(L, rng), [f0, f0 * 2, f0 * 3], q=25, gains=[1, 0.4, 0.2])
        ph = phase(f, loop) + rng.uniform(0, 2 * np.pi)
        tone = sum(w[h - 1] * np.sin(h * ph) for h in range(1, GOLD_HARM + 1))
        out += (ck.norm(tone) + ck.norm(breath) * 0.3) * flutter * BELL_GAIN[i]
    return ck.norm(out)


def gold_start(rng, k, chord, rush):
    """The cord pulled: the valve's rush of steam, then the three bells speaking, scooping up into tune as the pressure
    comes up (a semitone and a bit, as crew_train's), ending at full voice for the held loop to take over (the game
    crossfades) with only a short tail of its own."""
    L = 0.9
    tone = chord(rng, L, -(1.0 + 0.3 * k) * np.exp(-np.arange(samples(L)) / SR / 0.12))
    tone = tone * env([(0, 0), (0.05, 0), (0.22, 1), (L, 1)], L)
    y = mix(rush(rng, L) * env([(0, 0), (0.04, 1), (0.15, 0.4), (L, 0.3)], L) * 0.35, shrill(tone))
    y = dsp.room(hp(y, 200, 2), "night", wet=0.12, rng=np.random.default_rng(GOLD_ROOM))
    return dsp.fade(y[:samples(L + 0.15)], 0.002, 0.15)


def shrill_loop(y):
    """shrill() for a loop: run round the cycle."""
    return W.cyclic(shrill, y)


def gold_held(rng, chord, hiss):
    """The bells held: the same chord at full voice, the steam's hiss under it, the night's echo folded round. An exact
    GOLD_LOOP cycle, periodic in everything, handed to build.py seamless."""
    n = samples(GOLD_LOOP)
    y = mix(shrill_loop(chord(rng, n)), hiss(rng, n) * 0.1)
    y = W.cfilter(y, lambda f: (f / 200) ** 2 / (1 + (f / 200) ** 2))
    return W.seamless(W.croom(y, "night", 0.12, np.random.default_rng(GOLD_ROOM)))


def gold_stop(rng, k, chord, hiss):
    """The cord let go: the pressure falling, the bells sagging flat (one semitone, or two), their harmonics
    thinning back towards the plain whistle's and then to breath as it goes, the last of the steam hissing off; on the
    slower take, condensate spitting at the end."""
    L = (0.6, 1.0)[k % 2]
    p = env([(0, 1), (L * 0.35, 0.75), (L * 0.75, 0.15), (L, 0)], L)
    sag = (1.0, 2.2)[k % 2]
    tone = chord(rng, L, -sag * (1 - p) ** 1.5, GOLD_TILT + (PLAIN_TILT + 0.6 - GOLD_TILT) * (1 - p))
    tone = shrill(tone) * p ** 1.2
    Lh = L + 0.4
    h = hiss(rng, samples(Lh)) * env([(0, 0.25), (L * 0.5, 0.5), (Lh, 0)], Lh)
    parts = [(0, tone, 0), (0, h, -12)]
    if k % 2:
        parts.append((L * 0.6, synth.crackle(0.5, 25, rng, size=(0.002, 0.008), hi=600), -12))
    return dsp.room(hp(ck.place(parts), 200, 2), "night", wet=0.15, rng=np.random.default_rng(GOLD_ROOM))


def steam_chord_at(at):
    """The steam candidate's chord over L s (or n samples) of the real steam from `at` s, the bells locked onto it over
    PRE s of run-in first (held at their first pitch and tilt) so the lock and its level have settled when it's heard."""
    def chord(rng, L_or_n, bend_=0.0, tilt=GOLD_TILT):
        n = L_or_n if isinstance(L_or_n, (int, np.integer)) else samples(L_or_n)
        p = samples(PRE)

        def pad(c):
            return c if np.isscalar(c) else np.concatenate([np.full(p, c[0], np.float32), np.asarray(c, np.float32)])

        return steam_chord(rng, steam_rec(PRE + n / SR, at - PRE), pad(bend_), pad(tilt))[p:p + n]
    return chord


def steam_loop_chord(rng, n):
    """The steam candidate's chord for the loop: the real steam made one cycle (what ran past it crossfaded over its head)."""
    jet = loopify(steam_rec(n / SR + 0.4, 2.0), n)
    return steam_chord(rng, jet, loop=True)


def steam_hiss(rng, n):
    """The real steam's hiss above the bells (n samples, or a loop's cycle when it's GOLD_LOOP long)."""
    if n == samples(GOLD_LOOP):
        x = loopify(steam_rec(n / SR + 0.4, 5.6), n)
        return W.norm(W.cfilter(x, lambda f: (f / 1200) ** 2 / (1 + (f / 1200) ** 2) / (1 + (f / 8000) ** 2)))
    return W.norm(bp(steam_rec(n / SR, 5.6), 1200, 8000))


def valve_rush(rng, L):
    """The valve opening: the packs' real steam vehicle's hiss (sfx_100 misc_22) from its loudest, held to 0.8-8 kHz."""
    return ck.norm(bp(dsp.fit(core(VALVE, round(L, 2)), samples(L)), 800, 8000))


def gilt(rng, L_or_n, bend_=0.0, tilt=GOLD_TILT):
    """The modelled candidate's chord over L s (or n samples)."""
    n = L_or_n if isinstance(L_or_n, (int, np.integer)) else samples(L_or_n)
    return gilt_chord(rng, n, bend_, tilt)


def gilt_loop(rng, n):
    """The modelled candidate's chord for the loop: one exact cycle."""
    return gilt_chord(rng, n, loop=True)


def jet_hiss(rng, n):
    """The modelled steam's hiss above the bells (crew_train.steam's jet; periodic when it's the loop's cycle)."""
    if n == samples(GOLD_LOOP):
        return W.norm(W.cfilter(W.jet(n / SR, rng, n=n, peak=3000, low=0.0, eddy=0.9),
                                lambda f: (f / 1000) ** 2 / (1 + (f / 1000) ** 2) / (1 + (f / 8000) ** 2)))
    return W.norm(bp(W.jet(n / SR, rng, peak=3000, low=0.0, eddy=0.9), 1000, 8000))


def jet_rush(rng, L):
    """The valve opening, modelled: the steam jet's rush held to 0.8-8 kHz."""
    return W.norm(bp(W.jet(L, rng, peak=2500, low=0.3, eddy=0.8), 800, 8000))


GOLD = {
    "steam": dict(start=lambda rng, k: gold_start(rng, k, steam_chord_at(1.0 + 0.7 * k), valve_rush),
                  loop=lambda rng: gold_held(rng, steam_loop_chord, steam_hiss),
                  stop=lambda rng, k: gold_stop(rng, k, steam_chord_at(6.3 + 0.8 * k), steam_hiss)),
    "gilt": dict(start=lambda rng, k: gold_start(rng, k, gilt, jet_rush),
                 loop=lambda rng: gold_held(rng, gilt_loop, jet_hiss),
                 stop=lambda rng, k: gold_stop(rng, k, gilt, jet_hiss)),
}


def pull(takes, key, which):
    """The whole pull of the cord, so the page plays each take as the event it is: onset, held (the loop as build.py
    makes it, a cycle and a half) and let go, each take of the cue in turn with the other cues' takes round it at their
    own levels. The loop comes in under the onset's own 0.15 s fade at equal power (as the game's crossfade would), and
    the tail takes over from it at full voice."""
    cyc = dsp.level(dsp.loop_seam(hp(GOLD[key]["loop"](np.random.default_rng(40)), 25, 2), 0.3), -13)
    held_ = np.concatenate([cyc, cyc])[:samples(4.5)]
    k = samples(0.15)
    held_[:k] *= np.sqrt(1 - (1 - np.arange(k) / k) ** 4)      # under the onset's (1 - t)^2 fade: equal power
    j = samples(0.1)
    xf = np.linspace(0, np.pi / 2, j)
    held_[-j:] *= np.cos(xf)
    parts, t = [], 0.3
    for i, x in enumerate(takes):
        start = x if which == "start" else lv(GOLD[key]["start"](np.random.default_rng(41 + i), i), -13)
        stop = x if which == "stop" else lv(GOLD[key]["stop"](np.random.default_rng(43 + i), i), -15)
        stop = stop.copy()
        stop[:j] *= np.sin(xf)                                  # and the tail over the loop's end, equal power
        t_h = t + len(start) / SR - 0.15
        t_s = t_h + len(held_) / SR - 0.1
        parts += [(t, start, 0), (t_h, held_, 0), (t_s, stop, 0)]
        t = t_s + len(stop) / SR + 0.6
    return ck.place(parts, t)


@recipe("up-horns", "gold-start", "steam",
        "The gold-plated whistle sounding, its bells blown by real steam: the chord brighter, brassier, a quarter louder",
        """From real recordings: the packs' real locomotive steam (sfx_100 loop_water_03, its puffs evened out) blowing
        the cab whistle's three bells (E flat, G, B flat, crew_train's): each bell's note rung out of its noise (a band a
        couple of hertz wide) and the bell locked onto that band, its pitch nudged a few cents by the steam's turbulence
        and its level gusting with the jet's own; on that, the gold's harmonics (falling off as 1/h^0.85 to the tenth,
        where the plain whistle's fall as 1/h^1.4 to the sixth), a presence on top that's a touch shrill, and the steam
        rung round each partial breathing under it. The valve opens with a rush (the packs' real steam vehicle's hiss, misc_22) and the chord
        scoops up into tune as the pressure comes. 3 dB over the plain whistle. Ends at full voice for 'gold' to take
        over. The preview plays the whole pull: onset, held, let go.""",
        sources=[STEAM, VALVE], takes=2, lufs=-13, preview=lambda t, r: pull(t, "steam", "start"))
def gold_start_steam(rng, k):
    return GOLD["steam"]["start"](rng, k)


@recipe("up-horns", "gold", "steam",
        "The gold-plated whistle held, its bells blown by real steam: the same bright, brassy chord, an exact 3 s cycle",
        """The same as 'steam''s onset held: the real locomotive steam made one 3 s cycle (what runs past the cycle
        crossfaded over its head) blowing the three bells, each locked onto it and shaped into the gold's harmonics, the
        steam's own hiss above (held to 1.2-8 kHz), the night's echo folded round. Every filter runs round the cycle, so
        it loops without a seam.""",
        sources=[STEAM], loop=True, takes=1, lufs=-13, seconds=GOLD_LOOP)
def gold_steam(rng, k):
    return GOLD["steam"]["loop"](rng)


@recipe("up-horns", "gold-stop", "steam",
        "The gold-plated whistle let go: the bright chord sagging flat, thinning, the real steam hissing off",
        """The cord let go: the real steam's pressure falling under the bells, the chord sagging flat (one and two
        semitones), its harmonics thinning back past the plain whistle's to breath as it goes, the last of the steam's
        hiss dying away (the packs' real locomotive steam). The slower take spits condensate at the end. The preview plays
        the whole pull.""",
        sources=[STEAM], takes=2, lufs=-15, preview=lambda t, r: pull(t, "steam", "stop"))
def gold_stop_steam(rng, k):
    return GOLD["steam"]["stop"](rng, k)


@recipe("up-horns", "gold-start", "gilt",
        "The gold-plated whistle sounding, modelled: crew_train's three bells with their harmonics standing up",
        """Modelled as crew_train's whistle is (three bells blown by one jet, each a breathy harmonic tone with a turbulent
        flutter in its level and steam noise round each partial, E flat, G, B flat), gilded: each bell's harmonics
        falling off as 1/h^0.85 to the tenth where the plain whistle's fall as 1/h^1.4 to the sixth, a presence on top
        that's a touch shrill, 3 dB louder. The valve's rush is the modelled steam jet; the chord scoops up into tune as
        the pressure comes and ends at full voice for 'gold' to take over. The preview plays the whole pull.""",
        takes=2, lufs=-13, preview=lambda t, r: pull(t, "gilt", "start"))
def gold_start_gilt(rng, k):
    return GOLD["gilt"]["start"](rng, k)


@recipe("up-horns", "gold", "gilt",
        "The gold-plated whistle held, modelled: the bright three-bell chord, an exact 3 s cycle",
        """The gilded bells held: every note a whole number of cycles in 3 s, their wander, flutter and breath noise all
        periodic, the modelled jet's hiss above (periodic too), the night's echo folded round. Loops without a seam.""",
        loop=True, takes=1, lufs=-13, seconds=GOLD_LOOP)
def gold_gilt(rng, k):
    return GOLD["gilt"]["loop"](rng)


@recipe("up-horns", "gold-stop", "gilt",
        "The gold-plated whistle let go, modelled: the bright chord sagging flat and thinning into a hiss",
        """The cord let go: the gilded bells sagging flat as the pressure falls (one and two semitones), their harmonics
        thinning back past the plain whistle's to breath, the modelled jet's hiss dying away after; the slower take spits
        condensate at the end. The preview plays the whole pull.""",
        takes=2, lufs=-15, preview=lambda t, r: pull(t, "gilt", "stop"))
def gold_stop_gilt(rng, k):
    return GOLD["gilt"]["stop"](rng, k)


# ---- The speed recorder's bell ----------------------------------------------------------------------------------------------
# A small brass gong on the backhead, struck once by the recorder's clockwork: a trip lets a little sprung hammer go.
# One stroke, small and bright, its ring a little under a second. The overspeed's alarm (warnings.py) is the
# communication bell over the driver: a 1.2 kHz bell with a hum an octave under, struck twice, the cab's loudest bell.

GONG = [1.0, 1.73, 2.33, 3.91, 4.11]         # a small flat gong's modes (a free disc) over its first
GONG_DECAY = [0.42, 0.28, 0.2, 0.11, 0.09]
GONG_LEVEL = [1.0, 0.55, 0.45, 0.22, 0.16]
GONG_HZ = (2350.0, 2480.0)                   # each take's gong: a hair different in tune, struck a little differently


def trip_rec(rng, k):
    """The recorder's clockwork letting its hammer go: a real small mechanism's tick (sfx_100 metal_04, the lantern's wick
    winder in crew_items), pitched up for a clock-sized trip."""
    return dsp.vari(hit_of(TICK, k, 0.025, -20), 4 + rng.uniform(-0.5, 0.5))


def trip_model(rng, k):
    """The trip, modelled: an escapement's two ticks, the second smaller."""
    a = synth.click(rng.uniform(3800, 4600), q=14, length=0.01, rng=rng)
    b = synth.click(rng.uniform(5000, 6000), q=10, length=0.008, rng=rng)
    return ck.place([(0, a, 0), (0.011, b, -8)])


def ting_rec(rng, k):
    """The gong struck: a real small piece of steel struck (Kenney's light metal), pitched a hair so its clean partials
    sit at about 2.7 and 4 kHz, its ring drawn out to a small gong's (rubberband, the strike kept crisp), the low partial
    near 0.9 kHz held back so nothing of it sits where the overspeed's bell does."""
    x = ck.align(ck.get(STEELS[(3, 0)[k % 2]]))
    x = dsp.vari(x, (-1.5, 0.5)[k % 2] + rng.uniform(-0.2, 0.2))
    return ck.norm(hp(dsp.stretch(x, 1.8), 1500, 2))


def ting_model(rng, k):
    """The gong struck, modelled: a small flat brass gong's modes (a free disc's, from 2.35-2.5 kHz), each a doublet so
    it shimmers, the high ones dying first, struck by a hard little hammer (a steel tick on the front)."""
    f = GONG_HZ[k % 2] * rng.uniform(0.99, 1.01)
    fs, ds, am = [], [], []
    for r, d, a in zip(GONG, GONG_DECAY, GONG_LEVEL):
        split = rng.uniform(0.0006, 0.0012)
        for s, g in ((1 - split, 1.0), (1 + split, rng.uniform(0.25, 0.45))):
            fs.append(f * r * s)
            ds.append(d * rng.uniform(0.9, 1.1))
            am.append(a * g * (1 + 0.3 * k * (r - 1) / 3))
    ring = dsp.fade(W.modal(fs, ds, am, 0.9, rng, contact=0.00015), 0.0, 0.25)
    tick = synth.click(rng.uniform(5000, 6500), q=8, length=0.006, rng=rng)
    return ck.norm(mix(ck.norm(ring), ck.norm(tick) * 0.3))


def recorder(rng, k, trip, ting):
    """The trip, then the stroke a few hundredths later, in the iron cab."""
    return cab(ck.place([(0, trip(rng, k), -9), (0.04 + 0.01 * k, ting(rng, k), 0)]), 0.12)


@recipe("up-tells", "recorder-bell", "steel",
        "The speed recorder's bell: one small bright ting from a gong on the backhead, a clockwork tick before it",
        """From real recordings: the recorder's clockwork letting its hammer go (a real small mechanism's tick, sfx_100
        metal_04, pitched up), then one stroke on a small gong: a real small piece of steel struck (Kenney's light metal,
        its clean partials tuned to about 2.7 and 4 kHz), its ring drawn out to about two thirds of a second with the
        strike kept crisp, the low partial held back. In the iron cab's short slap. One stroke, small and high: never the
        overspeed's alarm (a 1.2 kHz bell with its hum, struck twice). Takes differ in the piece struck and its tune.""",
        sources=[TICK] + [STEELS[3], STEELS[0]], takes=2, band=(1800, 9000), lufs=-22)
def recorder_steel(rng, k):
    return recorder(rng, k, trip_rec, ting_rec)


@recipe("up-tells", "recorder-bell", "gong",
        "The speed recorder's bell, modelled: a small flat brass gong struck once, an escapement's tick before it",
        """Modelled: the recorder's escapement ticking its hammer free (two hard little ticks), then one stroke on a small
        flat brass gong: a free disc's modes from 2.35-2.5 kHz (1, 1.73, 2.33, 3.91, 4.11 times it), each a doublet so it
        shimmers, the high ones dying first, the whole gone in under a second; a hard little steel tick on its front. In
        the iron cab. One stroke, small and high: never the overspeed's bell.""",
        takes=2, band=(1800, 9000), lufs=-22)
def recorder_gong(rng, k):
    return recorder(rng, k, trip_model, ting_model)


# ---- Pre-cut boards ---------------------------------------------------------------------------------------------------------
# crew-repair's breach boarded in 4 s, not 8: the boards come cut to the hole, so each nail is a light tap to start it
# and one blow to drive it (sometimes the blow alone), four or five blows a second, the last one flush.

BOARD = [255, 585, 1070, 1810, 2950, 4300]   # a short pre-cut board's bending modes (synth.WOOD's, a shorter plank)
PRECUT_LUFS = -18.0       # crew-repair's blows are -17 each: these are lighter, and many


def precut_plan(rng, k):
    """The blows: [(t, nail, depth 0 a tap .. 1 driven, force)], after the board's knock at 0. Three nails, tap and blow;
    four, two of them a single blow; five, quickest."""
    nails, gap_in, gap_out = ((3, 0.13, 0.21), (4, 0.12, 0.19), (5, 0.1, 0.16))[k % 3]
    singles = {1, 3} if k % 3 == 1 else set()
    out, t = [], 0.24
    for i in range(nails):
        if i not in singles:
            out.append((t, i, 0.0, 0.55))
            t += gap_in * rng.uniform(0.9, 1.1)
        out.append((t, i, 1.0, 1.0))
        t += gap_out * rng.uniform(0.9, 1.1)
    return out


def blow_rec(rng, i, depth, force, last=False):
    """One blow, crew-repair's made light: the hammer's face on the nail head (the packs' hammer on metal, choked short),
    the nail's ring (a heavy metal hit pitched up for a short nail, higher and shorter as it goes in), and the board and
    the car wall taking it (the packs' wooden knocks, a lighter blow than the repair's)."""
    tak = ck.choke(hit_of(HAMMER, 0, 0.2), 0.003, 0.012 + 0.012 * (1 - depth))
    nail = ck.norm(ck.align(dsp.vari(ck.get(METAL_H[i % 5]), 6 + 5 * depth + rng.uniform(-0.4, 0.4))))
    nail = ck.choke(nail, 0.002, 0.012 + 0.02 * (1 - depth))
    wood = ck.floor(rng, "wood", i, (1.4 if last else 0.55 + 0.35 * depth), 0.9)
    return ck.place([(0, tak, -3 - 4 * (1 - force)), (0, nail, -11 - 5 * depth),
                     (0.002, wood, -6 + 3 * depth + (2 if last else 0))])


def board_rec(rng, k):
    """The pre-cut board set against the hole: a plank's knock (Kenney's), short."""
    return ck.choke(hit_of(PLANK[k % 5], 0, 0.3), 0.04, 0.05)


def blow_model(rng, i, depth, force, last=False):
    """One blow, modelled: the hammer's steel face on the nail head (a hard click), the nail ringing as a rod clamped in
    the wood (its first two modes, 1 and 6.27 times; its free length shortening as it goes in, so the ring rises from
    about 1.3 to 5 kHz and dies sooner), the short board's bending modes and the car wall's hollow taking the blow."""
    f1 = 1300 * 2 ** (2 * depth) * rng.uniform(0.95, 1.05)
    nail = W.modal([f1, f1 * 6.27], [0.035 * (1 - 0.6 * depth), 0.01], [1, 0.35], 0.12, rng, contact=0.0001)
    tak = synth.click(rng.uniform(3500, 5500), q=6, length=0.008, rng=rng)
    board = W.modal([f * rng.uniform(0.96, 1.04) for f in BOARD], [0.05, 0.04, 0.03, 0.02, 0.015, 0.01],
                    [1, 0.8, 0.6, 0.45, 0.3, 0.2], 0.2, rng, contact=0.0007)
    wall = ck.hollow(rng, [165, 240, 350], 0.16 if last else 0.12, q=4)
    y = ck.place([(0, tak, -4 - 4 * (1 - force)), (0, nail, -9 - 4 * depth),
                  (0.001, board, -4 + 2 * depth + (2 if last else 0)), (0.002, wall, -10 + 2 * depth + (3 if last else 0))])
    return dsp.room(y, "car", wet=0.12, rng=np.random.default_rng(11))


def board_model(rng, k):
    """The board set against the hole, modelled: its modes knocked by a soft blow, the wall's hollow."""
    b = W.modal([f * rng.uniform(0.96, 1.04) for f in BOARD], [0.06, 0.05, 0.035, 0.025, 0.015, 0.01],
                [1, 0.7, 0.5, 0.3, 0.2, 0.1], 0.25, rng, contact=0.002)
    return mix(ck.norm(b), ck.hollow(rng, [165, 240, 350], 0.15, q=4) * 0.4)


def precut(rng, k, blow, board):
    """The board set against the hole, then the blows (precut_plan), the last flush; levelled with its peaks held under
    build.py's knee (g1_train.peaks), the way the train's own impacts are."""
    plan = precut_plan(rng, k)
    parts = [(0, board(rng, k), -3)]
    for j, (t, i, depth, force) in enumerate(plan):
        parts.append((t, blow(rng, 3 * k + i, depth, force, j == len(plan) - 1), 0))
    return peaks(low_cut(ck.place(parts), 140), PRECUT_LUFS)


@recipe("up-tells", "precut", "nails",
        "Pre-cut boards going up: quick, light hammering, a tap and a blow a nail, three to five nails, the last flush",
        """From crew-repair's own real sources, made light and quick: the board set against the hole (Kenney's plank
        knock), then each nail a light tap to start it and one blow to drive it, four or five blows a second: the
        hammer's face on the nail head (the packs' hammer on metal, choked short), the nail's ring (a heavy metal hit
        pitched up for a short nail, rising and dying sooner as it goes in) and the board and the car wall taking it (the
        packs' wooden knocks, a lighter blow than the repair's), the last blow flush and solid. Takes: three nails; four
        (two of them one blow); five, quickest. Inside the car.""",
        sources=[HAMMER] + METAL_H + PLANK + ck.WOOD_KNOCKS, takes=3, lufs=PRECUT_LUFS)
def precut_nails(rng, k):
    return precut(rng, k, blow_rec, board_rec)


@recipe("up-tells", "precut", "planks",
        "Pre-cut boards going up, modelled: quick light blows, each nail's ring rising as it goes in, the last flush",
        """Modelled: the board's bending modes knocked as it's set against the hole, then each nail tapped and driven:
        the hammer's steel face on the head (a hard click), the nail ringing as a rod clamped in the wood (its free length
        shortening as it goes in, so its ring rises from about 1.3 to 5 kHz and dies sooner), the short board's modes and
        the car wall's hollow under each blow, the last flush and solid. The same three takes as 'nails'.""",
        takes=3, lufs=PRECUT_LUFS)
def precut_planks(rng, k):
    return precut(rng, k, blow_model, board_model)


# ---- Gun rammers ------------------------------------------------------------------------------------------------------------
# A sprung steel rammer on a guide at the muzzle: its catch let go, the spring drives the rod down the bore to seat the
# ball with one clean clack, and throws it back onto its catch. One stroke where the wooden rammer took several.

RAMMER_LUFS = -17.0       # between the wooden rammer's strokes (-20) and its rammed-home confirmation (-14)


def slide_rec(rng, k, L, down=True):
    """The rod sliding in its guide: a real blade drawn (steel on steel, Kenney's drawKnife), its loudest part, pitched
    down a little for a rod; quickening as the spring drives it (or easing as it comes back)."""
    x = core(DRAW[k % 2], round(L * 1.2, 2))
    x = dsp.fit(dsp.vari(x, -3 + rng.uniform(-0.5, 0.5)), samples(L))
    shape = [(0, 0.2), (L * 0.8, 1), (L, 0.7)] if down else [(0, 1), (L, 0.15)]
    return ck.norm(hp(x, 500, 2) * env(shape, L))


def clack_rec(rng, k, force=1.0):
    """The rammer's steel head seating the ball: the packs' heavy iron for the ball (crew_train.iron, choked: cast iron
    clanks) and a real sharp, bright hit of metal choked short for the steel's clean edge."""
    edge = ck.choke(dsp.vari(hit_of(CLANK, 0, 0.12), -4 + rng.uniform(-0.4, 0.4)), 0.004, 0.02)
    return ck.norm(mix(iron(rng, k, -2, 0.03, 0.6), lp(edge, 8000) * 0.7 * force))


def catch_rec(rng, k, which):
    """The catch: a real latch dropping (the packs' door latches), its first click."""
    return dsp.vari(hit_of(LATCHES[(k + which) % 3], 0, 0.08), 1 + rng.uniform(-0.4, 0.4))


def slide_model(rng, k, L, down=True):
    """The rod sliding, modelled: steel in a brass guide, fast, fine stick-slip (bright: 1.5-7 kHz)."""
    shape = env([(0, 0.2), (L * 0.8, 1), (L, 0.7)] if down else [(0, 1), (L, 0.15)], L)
    return ck.friction(rng, L, rng.uniform(600, 750), 1500, 7000, 0.35, shape)


def clack_model(rng, k, force=1.0):
    """The seat, modelled: the rammer's steel head (a bar's modes from about 1.15 kHz, hard contact) on the iron ball (a
    casting's close modes from 430 Hz, choked), a hard click on the front."""
    head = W.body(rng, rng.uniform(1100, 1200), W.BAR, decay=0.05, contact=0.00015, length=0.25)
    ball = W.body(rng, rng.uniform(410, 450), CASTING, decay=0.045, contact=0.0005, length=0.25)
    tick = synth.click(rng.uniform(4300, 5200), q=6, length=0.006, rng=rng)
    return ck.norm(mix(ck.norm(ball), ck.norm(head) * 0.6 * force, ck.norm(tick) * 0.35 * force))


def catch_model(rng, k, which):
    """The catch, modelled: a sprung pawl dropping in: a hard click and its small bar's ring."""
    c = synth.click(rng.uniform(3500, 4200), q=14, length=0.012, rng=rng)
    return ck.norm(mix(ck.norm(c), ck.norm(W.body(rng, rng.uniform(2400, 2800), W.BAR, decay=0.02, length=0.06)) * 0.4))


def spring_model(rng, L=0.2):
    """The spring's coil ringing as it throws the rod back."""
    return ck.norm(W.modal([540, 1290, 2130], [0.08, 0.05, 0.035], [1, 0.5, 0.3], L, rng, contact=0.0008))


def rammer(rng, k, slide, clack, catch, spring=None):
    """The catch let go, the spring driving the rod down the bore, the clack on the ball in the bore's hollow
    (crew_cannon.bore), and the rod thrown back onto its catch. Takes: a stroke; a harder seat with a small bounce; a
    quicker stroke."""
    Ls = (0.2, 0.17, 0.14)[k % 3]
    at = 0.015 + Ls
    parts = [(0, catch(rng, k, 0), -10), (0.015, bore(slide(rng, k, Ls, True), 0.25), -7),
             (at, bore(clack(rng, k, 1.0), 0.35), 0)]
    back = at + 0.12
    if k % 3 == 1:
        parts.append((at + 0.07, bore(clack(rng, k + 1, 0.35), 0.35), -12))
        back += 0.05
    parts += [(back, slide(rng, k + 1, 0.12, False), -15), (back + 0.12, catch(rng, k, 1), -6)]
    if spring:
        parts.append((back, spring(rng), -18))
    return peaks(low_cut(ck.place(parts), 150), RAMMER_LUFS)


@recipe("up-tells", "rammer", "clack",
        "The gun rammer seating the ball: its catch let go, the steel rod's slide, a clean clack, sprung back home",
        """From real recordings: the catch let go (a real latch's click), the steel rod driven down the bore by its spring
        (a real blade drawn, steel on steel, pitched down for a rod, quickening), then the seat: one clean clack (the
        packs' heavy iron for the ball, choked as cast iron is, and a real sharp, bright hit of metal choked short for the
        steel's edge), rung through the gun's bore (crew_cannon's hollow, 300/600/900 Hz); the rod thrown back out and its
        catch dropping in. Takes: a stroke; a harder seat that bounces once; a quicker stroke.""",
        sources=DRAW + LATCHES + [CLANK] + PLATE_H + METAL_H, takes=3, lufs=RAMMER_LUFS)
def rammer_clack(rng, k):
    return rammer(rng, k, slide_rec, clack_rec, catch_rec)


@recipe("up-tells", "rammer", "spring",
        "The gun rammer, modelled: a sprung steel rod's slide, its head clacking on the iron ball, the coil throwing it back",
        """Modelled: a sprung pawl let go, the steel rod sliding in its brass guide (fast, fine stick-slip, bright) as the
        spring drives it down the bore, the seat (the rod's steel head, a bar's modes from about 1.15 kHz, on the iron ball,
        a casting's close modes from 430 Hz, choked; a hard click on the front) rung through the bore, then the spring's
        coil ringing as it throws the rod back and the pawl dropping in. The same three takes as 'clack'.""",
        takes=3, lufs=RAMMER_LUFS)
def rammer_spring(rng, k):
    return rammer(rng, k, slide_model, clack_model, catch_model, spring_model)


# ---- The trailing-truck booster ---------------------------------------------------------------------------------------------
# A small two-cylinder engine geared to the trailing truck's axle under the cab: cut in as the train pulls away, its
# idler gear drops into mesh and steam goes to it, and its exhaust joins the chuff: four beats a turn of its geared
# cylinders, quicker and lighter than the big engine's, quickening as the train gathers way.

BOOST_LEN = (1.9, 2.3)       # each take's moment (s): it settles under the chuff and fades


def booster_times(rng, k):
    """The booster's beats: from about 4.5 to 7 a second over a second and a half, the four of a turn uneven (front and
    back of two cylinders), swelling in as the steam reaches it and fading as it settles under the chuff. [(t, level)]"""
    end = BOOST_LEN[k % 2]
    t0 = 0.3 + 0.04 * k
    out, t, i = [], t0, 0
    while t < end - 0.05:
        rate = 4.5 + 2.5 * min(1.0, (t - t0) / 1.5)
        swell = min(1.0, 0.45 + (t - t0) / 0.4) * min(1.0, (end - t) / 0.7)
        out.append((t + rng.normal(0, 0.004), (1.0, 0.72, 0.9, 0.66)[i % 4] * swell * rng.uniform(0.9, 1.05)))
        t += 1 / rate
        i += 1
    return out


@functools.lru_cache(maxsize=1)
def _puffs():
    """Every puff in the packs' real locomotive steam (sfx_100 loop_water_03, a hiss with the engine's own exhaust puffs
    in it), cut to its first 0.16 s: real exhaust beats."""
    x = ck.get(STEAM)
    return [ck.cut(x, s - samples(0.005), s + samples(0.16), 0.002, 0.08)
            for s, _ in ck.hits(x, floor_db=-12, gap=0.25, prom=4)]


def puff_rec(rng, i):
    """One of the booster's beats from a real puff: pitched up for smaller cylinders and a quicker beat, choked short,
    rung a little through the chimney's pipe modes (world_bed.CHIMNEY)."""
    p = _puffs()
    x = dsp.vari(p[(i * 3 + int(rng.integers(len(p)))) % len(p)], 4 + rng.uniform(-0.7, 0.7))
    x = ck.choke(ck.norm(hp(x, 180, 2)), 0.03, 0.04)
    return ck.norm(mix(x, ck.norm(dsp.resonate(x, CHIMNEY[1:], q=3, gains=[1, 0.7, 0.45])) * 0.4))


def puff_model(rng, i):
    """One of the booster's beats, modelled: world_bed's blast-pipe exhaust from a smaller cylinder, a pulse that rises in
    a few ms and dies in four hundredths (the big engine's in a tenth), its jet noise peaking higher, rung through the
    chimney's pipe modes, the thump of it leaving the stack kept small."""
    L = 0.3
    n = samples(L)
    t = np.arange(n) / SR
    p = (1 - np.exp(-t / rng.uniform(0.002, 0.004))) * np.exp(-t / rng.uniform(0.032, 0.045))
    p = (p / p.max()).astype(np.float32)
    jet = W.jet(L, rng, pressure=p, peak=rng.uniform(900, 1150), low=0.2, eddy=0.5)
    pipe = dsp.resonate(jet, [f * rng.uniform(0.95, 1.05) for f in CHIMNEY[1:]], q=3, gains=[1, 0.7, 0.45])
    dq = lp(np.diff(np.sqrt(p), prepend=0).astype(np.float32) * SR / 1000, 500, 2)
    return ck.norm(mix(W.norm(jet) * 0.7, W.norm(pipe) * 0.45, W.norm(dsp.resonate(dq, [190], q=2)) * 0.4))


def gear_rec(rng, k):
    """The idler gear dropping into mesh: an iron clunk with a ring in it (crew_train.iron: the packs' heavy plate and
    metal, choked), a real bright hit of metal for the teeth meeting, and two more teeth catching (the packs' real
    ratchet, pitched down)."""
    teeth = [(0.03 + 0.024 * j, dsp.vari(hit_of(RATCHET, j, 0.03, -20), -5 + rng.uniform(-0.5, 0.5)), -6 - 3 * j)
             for j in range(2)]
    edge = ck.choke(dsp.vari(hit_of(CLANK, 0, 0.1), -7 + rng.uniform(-0.5, 0.5)), 0.006, 0.03)
    return ck.place([(0, iron(rng, k, -1, 0.05, 0.9), 0), (0.002, lp(edge, 6000), -6)] + teeth)


def gear_model(rng, k):
    """The idler gear into mesh, modelled: a gear wheel's knock (a thick bar's modes from 450 Hz, choked), the teeth
    meeting (a hard click) and catching: three more in quick succession."""
    g = W.body(rng, rng.uniform(430, 470), W.BAR, decay=0.06, contact=0.0004, length=0.3)
    hit = synth.click(rng.uniform(2500, 3200), q=6, length=0.012, rng=rng)
    teeth = [(0.025 + 0.022 * j, synth.click(rng.uniform(1800, 3000), q=10, length=0.015, rng=rng), -6 - 3 * j)
             for j in range(3)]
    return ck.place([(0, ck.norm(g), 0), (0, hit, -6)] + teeth)


def valve_model(rng, L):
    """Steam let to the booster, modelled: the jet through its throttle opening, held to 0.6-6 kHz."""
    return W.norm(bp(W.jet(L, rng, opening=env([(0, 0.2), (0.1, 1), (L, 1)], L), peak=1600, low=0.2), 600, 6000))


def booster(rng, k, beat, gear, valve):
    """The cut-in: the gear into mesh, steam to its cylinders (a swell), then its beats, quickening and settling."""
    L = BOOST_LEN[k % 2]
    Lv = 0.6
    g = gear(rng, k)
    g = dsp.gain(g, -19.0 - dsp.loudness(low_cut(g, 150)))       # a little over a beat (about -20): the cut-in heard
    parts = [(0, g, 0),
             (0.08, valve(rng, Lv) * env([(0, 0), (0.08, 1), (0.3, 0.35), (Lv, 0)], Lv), -16)]
    parts += [(t, beat(rng, i), 20 * np.log10(max(lvl, 1e-3)) - 2) for i, (t, lvl) in enumerate(booster_times(rng, k))]
    return by_train(low_cut(ck.place(parts, L + 0.2), 150), rng, 0.08)


def under_chuff(takes, rng):
    """The preview: each take as it plays, over the big engine's chuff at its own level (world_bed's blast model, two and
    a half beats a second as the train pulls away), so the booster is heard where it sits: under it."""
    parts, t = [], 0.2
    for x in takes:
        c, i = t, 0
        while c < t + len(x) / SR + 1.2:
            parts.append((c, lv(exhaust(np.random.default_rng(60 + i), heavy=True), -30), 0))
            c += 1 / (2.2 + 0.6 * min(1.0, (c - t) / 2.0))
            i += 1
        parts.append((t + 0.5, x, 0))
        t = c + 0.8
    return ck.place(parts, t)


@recipe("up-tells", "booster", "puffs",
        "The booster cutting in: its gear into mesh, then a second, quicker exhaust beat under the chuff, from real puffs",
        """From real recordings: the idler gear dropping into mesh (the packs' heavy plate and metal choked, crew_train's
        iron, a real bright hit of metal for the teeth meeting and two teeth of a real ratchet catching), steam reaching
        the booster (the packs' real steam vehicle's hiss), then its exhaust: the real puffs in the packs' locomotive steam (sfx_100 loop_water_03) cut
        to their fronts, pitched up for smaller cylinders, choked short and rung through the chimney (world_bed's pipe
        modes): four beats a turn, uneven like two cylinders, from about 4.5 to 7 a second over a second and a half,
        swelling in and settling under the chuff. Outdoors by the engine. The preview lays each take over the big
        engine's chuff at its own level.""",
        sources=[STEAM, VALVE, RATCHET, CLANK] + PLATE_H + METAL_H, takes=2, lufs=-28, preview=under_chuff)
def booster_puffs(rng, k):
    return booster(rng, k, puff_rec, gear_rec, lambda r, L: valve_rush(r, L))


@recipe("up-tells", "booster", "beats",
        "The booster cutting in, modelled: a gear knocking into mesh, then smaller, quicker blast-pipe beats under the chuff",
        """Modelled: the idler gear's knock and its teeth catching, steam let to the booster (the modelled jet through its
        throttle), then its exhaust: world_bed's blast-pipe model from smaller cylinders (a pulse rising in a few ms and
        dying in four hundredths, the big engine's in a tenth; its jet peaking higher, rung through the chimney's pipe
        modes, a small thump), four a turn, uneven, quickening from 4.5 to 7 a second and settling under the chuff. The
        same two takes as 'puffs'; the preview lays each over the chuff.""",
        takes=2, lufs=-28, preview=under_chuff)
def booster_beats(rng, k):
    return booster(rng, k, puff_model, gear_model, valve_model)


# ---- Composition brake shoes ------------------------------------------------------------------------------------------------
# On a long descent the cast-iron shoes grind, squeal at the wheel's 1.9 kHz mode in bursts and glaze into a glassy
# whine (bed-brake, state-brake-fade). A composition shoe is softer and grips evenly: the grind is duller (it damps the
# wheel's ring, so less top), and where the iron ones scream there's only a lesser moan near 1 kHz that swells and
# lets go as they take hold.

COMP_LEN = (1.6, 2.05)
TURN = 4.2                   # wheel turns a second on a descent (12 m/s on 0.9 m wheels): the grind's pulse


def comp_shape(L):
    """The shoes coming on and holding: the grind rising in a quarter second, steady, easing at the end."""
    return env([(0, 0), (0.08, 0.3), (0.3, 1), (L - 0.4, 0.9), (L, 0)], L)


def grind_rec(rng, L):
    """The shoe on the tyre: the packs' real metal scrape (sfx_100 misc_10) drawn out (paulstretched, so its grain stays
    and its strokes go), dropped seven semitones and darkened (composition doesn't ring the wheel), a little of the
    wheel's modes in it, pulsing once a wheel turn."""
    x = drawn(rng, SCRAPE, L, -7, 0.08)
    x = lp(hp(x, 200, 2), 1300, 4)
    x = mix(ck.norm(x), ck.norm(dsp.resonate(x, WHEEL[:3], q=6)) * 0.5)
    t = np.arange(len(x)) / SR
    return ck.norm(x * (1 + 0.12 * np.sin(2 * np.pi * TURN * t)))


def moan_rec(rng, L, k):
    """The lesser shriek: a real hinge's squeal (stick-slip singing, Kenney's creak) held out and pulled down to about
    1 kHz, wavering, its upper partials taken off (dull)."""
    c = semis(rng.uniform(960, 1040), HINGE_F) + np.sin(np.linspace(0, 2 * np.pi * L * 1.3, samples(L))) * 0.12
    return ck.norm(lp(held_cry(rng, HINGES[k % 2], L, c, 0.05), 1400, 4))


def rig_rec(rng, k):
    """The rigging taking up: world_bed.rigging (the packs' light metal hits pitched down for heavy iron) and the shoes
    meeting the tyres (a medium plate pitched down, softened: a composition shoe lands soft)."""
    shoe = lp(W.rec(PLATE_M[k % 5], semis=-rng.uniform(4, 6)), 1500, 2)
    return ck.place([(0, rigging(rng, 2 + k, 0.12), -2), (0.14, ck.norm(shoe), -4)])


def grind_model(rng, L):
    """The shoe on the tyre, modelled: world_bed's brake friction (contact noise rung through a car wheel's modes) through
    a softer shoe: the modes' Q low (damped), the asperities' roughness and the hiss down, the top rolled off; a low hum
    of the shoe in its hanger; pulsing once a wheel turn."""
    n = samples(L)
    g = W.friction(n, rng, WHEEL, rough=0.6, grit=0.1, q=6)
    g = W.cfilter(g, lambda f: (f / 200) ** 2 / (1 + (f / 200) ** 2) / (1 + (f / 2400) ** 2))
    body = W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(240)) / 0.6) ** 2))
    t = np.arange(n) / SR
    return ck.norm((W.norm(g) + W.norm(body) * 0.2) * (1 + 0.12 * np.sin(2 * np.pi * TURN * t)))


def moan_model(rng, L, k):
    """The lesser shriek, modelled: a wheel mode near 1 kHz caught by the shoe (W.squeal: a near-pure tone shivering in
    pitch), never the iron's 1.9 kHz; held soft."""
    n = samples(L)
    return ck.norm(W.squeal(n, rng, [rng.uniform(980, 1060)], np.ones(n, np.float32), wander=0.006, rate=3.0, harm=0.12))


def rig_model(rng, k):
    """The rigging and the shoes coming on, modelled: rods knocking in their hangers (small iron bars' modes) and the
    shoes landing soft (a pad and the tyre's dull modes)."""
    knocks = [(0.04 * j + rng.uniform(0, 0.03), ck.norm(W.body(rng, rng.uniform(600, 900), W.BAR, decay=0.03, length=0.1)),
               -3 * j) for j in range(2 + k)]
    tyre = W.body(rng, 380, [f / 380 for f in WHEEL], decay=0.02, damp=0.3, length=0.12)
    shoe = mix(ck.pad(rng, 0.08, 400), ck.norm(tyre) * 0.4)
    return ck.place(knocks + [(0.14, ck.norm(shoe), -4)])


def composition(rng, k, grind, moan, rig):
    """The shoes coming on, the duller grind holding, the lesser moan swelling in and letting go as they take hold."""
    L = COMP_LEN[k % 2]
    g = grind(rng, L) * comp_shape(L)
    a, b = (0.35, 1.25) if k % 2 == 0 else (0.5, 1.7)
    Lm = b - a
    m = moan(rng, Lm, k) * env([(0, 0), (Lm * 0.35, 1), (Lm * 0.6, 0.7), (Lm, 0)], Lm)
    y = ck.place([(0, rig(rng, k), -8), (0.12, g, 0), (0.12 + a, m, -9)])
    return by_train(low_cut(y, 150), rng, 0.1, 0.25)


@recipe("up-tells", "composition", "scrape",
        "Composition shoes on a descent: a duller grind holding, a lesser moan near 1 kHz where the iron ones shriek",
        """From real recordings: the rigging taking up (world_bed's: the packs' light metal hits pitched down for heavy
        iron) and the shoes meeting the tyres soft (a medium plate pitched down and softened), then the grind: the packs'
        real metal scrape (sfx_100 misc_10) drawn out, dropped seven semitones and darkened (a composition shoe damps the
        wheel's ring), a little of a car wheel's modes in it, pulsing once a wheel turn; and the lesser shriek, a real
        hinge's squeal (Kenney's creak, stick-slip singing) held out and pulled down to about 1 kHz, swelling in and
        letting go as the shoes take hold. Never the iron shoes' 1.9 kHz squeal or their glazed whine. Takes: about 1.9
        and 2.3 s. Outdoors by the wheels.""",
        sources=[SCRAPE] + HINGES + PLATE_M + K("impactMetal_light"), takes=2, lufs=-23)
def composition_scrape(rng, k):
    return composition(rng, k, grind_rec, moan_rec, rig_rec)


@recipe("up-tells", "composition", "shoes",
        "Composition shoes on a descent, modelled: the brake friction damped and dulled, a soft moan near 1 kHz",
        """Modelled from the bed's brake friction (contact noise rung through a car wheel's modes) through a softer shoe:
        the modes' Q low (the composition damps the ring), the asperities' roughness and the hiss down, the top rolled off
        above 2.4 kHz, a low hum of the shoe in its hanger, pulsing once a wheel turn; the rigging's rods knocking and the
        shoes landing soft first; a wheel mode near 1 kHz caught for a moment (a soft, shivering moan) where the iron
        shoes squeal at 1.9 kHz. The same two takes as 'scrape'.""",
        takes=2, lufs=-23)
def composition_shoes(rng, k):
    return composition(rng, k, grind_model, moan_model, rig_model)


# ---- Axle oilers ------------------------------------------------------------------------------------------------------------
# A Hotbox in a truck that has oil cups on its boxes: its knock (tell-hotbox's, a hammer on a casting) comes slower and
# softer, cushioned by the oil, the cup ticking on the box with each knock, a drop of oil falling between. Three knocks
# easing off: nothing like the tell's even knock once a wheel turn.

OIL_TIMES = ((0.0, 0.46, 1.1), (0.0, 0.52, 1.22))    # the knocks easing off: slower each time
OIL_DROPS = ((0.27,), (0.3, 0.86))
OIL_LEN = 1.6
OIL_LUFS = -26.0          # under tell-hotbox's knock (-22) and the hot box's squeal (-24): softer


def knock_rec(rng, k, j):
    """A knock cushioned by oil: one of the tell's real heavy strikes (Kenney's heavy plate, a pick into rock) pitched
    down, choked sooner than the tell's (the oil takes it), its top rolled off lower (softer), a little of the box's
    casting under it."""
    key = (PLATE_H[0], MINING[1], PLATE_H[2])[(k + j) % 3]
    x = dsp.vari(hit_of(key, 0, 0.3), -rng.uniform(3, 5))
    x = ck.choke(ck.norm(hp(x, 170, 4)), 0.025, 0.03)
    body = dsp.resonate(x, [BOX[(k + j) % 4] * r for r in CASTING[:4]], q=8, gains=[1, 0.7, 0.5, 0.35])
    return ck.norm(lp(mix(ck.norm(x), ck.norm(body) * 0.35), 750, 2))


def drop_rec(rng, j):
    """A drop of oil off the cup's wick onto the journal: a real plop (Kenney's, a click and a tone dying in 60 ms) pitched
    down (thick oil: slower, duller) with a grain of a real wet squelch on its front."""
    p = dsp.vari(hit_of(PLOP[(1, 3, 4)[j % 3]], 0, 0.1), -3 + rng.uniform(-0.5, 0.5))
    wet = hp(core(WET[0], 0.015), 1000)
    return ck.norm(lp(mix(ck.norm(p), ck.norm(wet) * 0.3), 2600))


def cup_rec(rng, j):
    """The oil cup on the box ticking with the knock: a small real metal click (Kenney's), pitched up."""
    return dsp.vari(hit_of(CLICK[0], 0, 0.04, -20), 3 + rng.uniform(-0.5, 0.5))


def knock_model(rng, k, j):
    """A knock cushioned by oil, modelled: g1_train's casting (close, inharmonic modes from about 190-250 Hz) struck
    through a longer contact (the oil film: only the low modes sound) and damped sooner, the box's dull bump under it."""
    f1 = BOX[(k + j) % 4] * rng.uniform(0.97, 1.03)
    return ck.norm(lp(mix(cast_knock(rng, f1, contact=0.004, decay=0.035), box_thud(rng) * 0.4), 750, 2))


def drop_model(rng, j):
    """A drop of oil, modelled: a thick drop landing on an oil film: a soft tick and a small dull ring rising in pitch
    as the little cavity closes, slower and lower than water's."""
    L = 0.08
    t = np.arange(samples(L)) / SR
    f = rng.uniform(650, 800) * (1 + 0.4 * t / L)
    ring = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.014)
    tick = synth.click(rng.uniform(2200, 2800), q=4, length=0.006, rng=rng)
    return ck.norm(mix(ck.norm(ring.astype(np.float32)), ck.norm(tick) * 0.4))


def cup_model(rng, j):
    """The oil cup ticking on the box, modelled: a small brass cup's lid (a plate's modes from about 3.2 kHz, short)."""
    return ck.norm(W.body(rng, rng.uniform(3000, 3400), W.PLATE[:5], decay=0.03, contact=0.0002, length=0.08))


def oilers(rng, k, knock, drop, cup):
    """Three knocks easing off, each quieter and further apart, the cup ticking after each; oil dropping between."""
    parts = []
    for j, t in enumerate(OIL_TIMES[k % 2]):
        parts += [(t, knock(rng, k, j), -5 * j), (t + 0.012, cup(rng, j), -16 - 4 * j)]
    parts += [(t, drop(rng, j), -9 - 3 * j) for j, t in enumerate(OIL_DROPS[k % 2])]
    y = ck.place(parts, OIL_LEN)
    return peaks(by_train(low_cut(y, 150, 4), rng, 0.08), OIL_LUFS)


@recipe("up-tells", "oilers", "drip",
        "The oilers at work: the axle box's knock slower and softer, cushioned by oil, a drop of oil falling between",
        """From the tell's own real sources: three knocks in the axle box easing off, each further apart and quieter (the
        packs' heavy plate and a pick's blow pitched down, as tell-hotbox's 'struck' is, but choked sooner and rolled off
        lower: the oil takes the blow), a little of the box's casting under each, the oil cup on the box ticking with it (a
        small real metal click, pitched up); between them a drop of oil off the cup's wick (a real plop pitched down,
        thick and dull, a grain of a real wet squelch on its front). Takes: knocks half a second then 0.64 s apart, one
        drop; further apart, two drops. Never the tell's even knock once a wheel turn.""",
        sources=PLATE_H + MINING + PLOP + WET + CLICK, takes=2, lufs=OIL_LUFS)
def oilers_drip(rng, k):
    return oilers(rng, k, knock_rec, drop_rec, cup_rec)


@recipe("up-tells", "oilers", "cups",
        "The oilers at work, modelled: the casting's knock cushioned and easing off, the cup ticking, oil dropping",
        """Modelled: g1_train's axle-box casting (close, inharmonic modes from about 190-250 Hz) struck through a longer
        contact (the oil film: only the low modes sound) and damped sooner, three times, easing off and quieter, the box's
        dull bump under each; the brass oil cup's lid ticking on the box (a small plate's modes from 3.2 kHz); between,
        a drop of oil landing on the film (a soft tick, a small dull ring rising as its cavity closes). The same two takes
        as 'drip'.""",
        takes=2, lufs=OIL_LUFS)
def oilers_cups(rng, k):
    return oilers(rng, k, knock_model, drop_model, cup_model)


# ---- The lucky horseshoe ----------------------------------------------------------------------------------------------------
# A glint when a foul didn't happen: the horseshoe over the cab door catching the light. A tiny, bright run of high
# tings rising (a major chord in the seventh octave), a shimmer hanging after: the only sparkle on the train, and under
# a second.

# Each take's run: [(step, MIDI note, dB)] and the step (s): A7 C#8 E8 A8; E7 A7 C#8 E8, and E8 again, softer
GLINT = (([(0, 105, 0), (1, 109, -1.5), (2, 112, -3), (3, 117, -4.5)], 0.062),
         ([(0, 100, 0), (1, 105, -1.5), (2, 109, -3), (3, 112, -4.5), (4.6, 112, -11)], 0.05))


def ting_glint(rng, j, hz):
    """One tiny ting from the packs' real small things struck (light steel, ringing glass, light glass), pitched so its
    loudest partial lands on `hz`, cut short."""
    key = (STEELS[3], RINGING[0], STEELS[2], GLASS[1], STEELS[0])[j % 5]
    x = ck.align(ck.get(key))
    x = dsp.vari(x, float(semis(hz, strongest(x[:samples(0.1)], 500, 9000))))
    return ck.norm(ck.choke(hp(x, hz * 0.7, 4), 0.02, 0.09))


def ting_shoe(rng, j, hz):
    """One tiny ting, modelled: a small steel bar's modes (1, 2.756, 5.404 times `hz`), each a doublet so it shimmers."""
    fs, ds, am = [], [], []
    for r, d, a in zip(W.BAR[:3], (0.32, 0.14, 0.06), (1.0, 0.35, 0.15)):
        s = rng.uniform(0.0015, 0.003)
        fs += [hz * r * (1 - s), hz * r * (1 + s)]
        ds += [d, d * 0.9]
        am += [a, a * 0.7]
    return ck.norm(W.modal(fs, ds, am, 0.4, rng, contact=0.00012))


def glint_run(rng, k, ting):
    """The run: the tings rising, each a little quieter (GLINT)."""
    run, gap = GLINT[k % 2]
    return ck.place([(step * gap, ting(rng, j, kit.hz(m)), db) for j, (step, m, db) in enumerate(run)])


def glint_rec(rng, k):
    """The real glint: the run of real tings, a few real coins' chinks under its front for the glitter, and the run's own
    ring held out (paulstretched) into a shimmer that hangs after it."""
    run = glint_run(rng, k, ting_glint)
    coins = hp(hit_of(COINS[0], 1 + k, 0.08, -20), 4000)
    shimmer = hp(dsp.smear(run, 3.0, 0.04, rng), 2500)
    Ls = len(shimmer) / SR
    shimmer = shimmer * env([(0, 0), (0.12, 1), (Ls, 0)], Ls)
    return ck.place([(0, coins, -16), (0.01, run, 0), (0.08, ck.norm(shimmer), -12)])


def glint_model(rng, k):
    """The modelled glint: the horseshoe itself rung once (a bent steel bar's modes from 1.85 kHz, doublets), then the run
    of tiny bar tings rising, and a breath of high air swelling and fading under it: the gleam."""
    shoe = ting_shoe(rng, 0, rng.uniform(1800, 1900))
    run = glint_run(rng, k, ting_shoe)
    La = 0.5
    gleam = bp(synth.noise(La, rng), 5000, 12000) * env([(0, 0), (0.18, 1), (La, 0)], La)
    return ck.place([(0, shoe, -6), (0.04, run, 0), (0.04, ck.norm(gleam), -24)])


def horseshoe(rng, k, glint):
    """Over the cab door, in the night: a little of its reverb so the sparkle blooms, the take done inside a second."""
    y = dsp.room(low_cut(glint(rng, k), 300), "night", wet=0.16, rng=np.random.default_rng(GOLD_ROOM))
    return dsp.fade(dsp.fit(y, samples(0.95)), 0.001, 0.25)


@recipe("up-tells", "horseshoe", "glint",
        "The lucky horseshoe's glint: a tiny, bright run of high tings rising, a shimmer hanging after, from real things",
        """From real recordings: four or five tiny tings rising through a major chord in the seventh octave (E, G sharp, B,
        E; or B, E, G sharp, B and B again), each a real small thing struck (Kenney's light steel, the packs' ringing glass
        and light glass) pitched so its loudest partial lands on the note and cut short, a few real coins' chinks under
        the front for the glitter, the run's own ring held out (paulstretched) into a shimmer hanging after it. A little
        of the night round it; under a second. The only sparkle on the train: nothing like the recorder's single gong
        stroke.""",
        sources=[STEELS[3], STEELS[2], STEELS[0], RINGING[0], GLASS[1], COINS[0]], takes=2, band=(2500, 12000), lufs=-24)
def horseshoe_glint(rng, k):
    return horseshoe(rng, k, glint_rec)


@recipe("up-tells", "horseshoe", "shoe",
        "The lucky horseshoe's glint, modelled: the shoe rung once, a run of tiny bar tings rising, a gleam of high air",
        """Modelled: the horseshoe itself rung once (a bent steel bar's modes from about 1.85 kHz, each a doublet so it
        shimmers), then the same run as 'glint' in tiny steel bar tings (1, 2.756, 5.404 times each note), and a breath of
        high air (5-12 kHz) swelling and fading under it: the gleam. Under a second.""",
        takes=2, band=(2500, 12000), lufs=-24)
def horseshoe_shoe(rng, k):
    return horseshoe(rng, k, glint_model)
