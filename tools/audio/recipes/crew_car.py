"""K1's crew car and crew memory heard (queue #506, note 776; K1's #1016, notes 716 and 721): the kettle and the cuckoo
clock on the crew car's back wall, and the crew photo and the logbook at the end of a delivered night, as
tools/audio/cues.py lists them.

What each is decides the build:
- The kettle: the engine's cab is an 11 m crew car now, and on the shelf at its back wall (the boiler's warm backhead end)
  sits a small iron kettle with a two-hole whistle in its spout, singing louder as the boiler gauge nears the redline
  (the game plays one level of the loop and scales its volume 0-1). So the loop is the kettle in full song: the steam
  jet through the whistle's two holes sounding two close tones near 2.1-2.2 kHz that beat against each other (a shrill,
  rough waver), its pitch rising a little as the boil surges and the push comes on, catching and wavering as the jet's
  turbulence shakes it, a second hole joining a little sharper on the hardest surges; round the tones the jet's own
  breath (a narrow band of air at the note, and a hiss over it), and under it the water's rumble-hiss rung in the
  kettle's hollow. The air is kept loud enough that turned right down it still reads as a kettle coming to the whistle,
  not a test tone. Heard over a locomotive's roar in a cab, so it lives in 1-4 kHz and nothing sounds under 300 Hz. A
  seamless 8 s loop, exactly one cycle (every part built periodic: tones whole cycles round it, textures laid round it,
  the room a circular one), so build.py's seam gives the cycle back.
- The cuckoo clock: on the same back wall, its bird out three times in the last three minutes before dawn, 3 s each
  time. A wooden clock's strike: its little door clacking open against its stop, the works whirring as the train runs
  (the fan fly spinning, the wheels' teeth ticking) and pushing the bird out, a cam's click as each bellows is lifted and
  dropped, the two notes "cuck-oo" (a falling minor third, about 700 then 590 Hz) from two small wooden pipes blown by
  their bellows (breathy and reedy, each scooping into its note as the bellows' weight comes on and sagging as it
  empties, the second longer), the bird sitting out a moment, the works whirring it back in, a last click of the
  locking lever and the door clacking shut, and the pendulum's quick tick-tock keeping time under it all. Homely, a
  clock in a small wooden case, and a little ominous for what it means (the Wakers are coming at dawn, and it's
  counting), but never a creature: no throat, no breath but the bellows'. 2.6-2.9 s. Its clacks keep their energy out of
  the squish under 150 Hz (the director's rule).
- The crew photo: at the gate at the end of a delivered night, taken the old way. The flash powder in its tray goes off:
  a sharp whump, the 'fffump' of magnesium powder flaring in a breath (a fast front, a puff of hot gas, not a blast:
  nothing under 150 Hz to speak of, no boom), its fizz and spit of burning grains dying away; and the big camera's
  shutter, a leaf shutter's mechanical snap (tripped as the flash goes, or closed after it, or both). Outdoors, the
  train beside it. Under 1.5 s.
- The logbook: heavy and leather-bound, on a bench in the crew car, close and dry. Opened: the spine creaking as the
  cover is lifted, the thick boards swung over and thumped back onto the wood of the bench, a few pages riffling down
  after it (about 1 s). A page turned: dry heavy paper lifted at its corner, a soft crackle as it bends, swept over and
  laid down flat (0.5-0.8 s; the most frequent of them, turned with the movement keys, so its four takes are each built
  differently: none two in a row alike). Shut: the pages flattening as the cover comes down, the air pushed out of them,
  the cover's dull, papery clap on the block (0.6 s). The thumps and claps keep their weight in the low mids: high-passed
  at 150-160 Hz, so they land as a heavy book, not a boom.

Each cue has two candidates. The first is kitbashed from the packs' real recordings, bent: for the kettle a real glass
ringing almost pure (its two partials 28 Hz apart are the two holes) held still as steady tones and held out into an
airy whine, pulled along the boil's push, with a real long hiss of air for the jet and real moving water for the boil;
for the cuckoo light wood knocks pitched up for its little door, tiny real clicks, a real fast mechanism's ticks for the
works and a real pawl for its cams, a real held reedy tone played at the pipes' two notes for the bird, a real breath
through them and a real burst of air for the bellows, real small metal clicks for the pendulum; for the photo a real burst with a hissing tail and a real
whoomph of moving air for the flare, a real switch's crack for the cap, a real hiss and real dry crackle cut into grains
for the fizz, and a real camera shutter; for the logbook the packs' real books set down, shut and flipped, a
real creak and real leather for the spine and the cover, a real belt's cloth crackle for the paper's fibres. The second
is modelled: sine tones beating round an exact cycle with a jet's air and bubbles; struck modes, a gear train's ticks
and a stopped flue pipe's tone; a flare's noise through an opening and closing filter, crackle and a leaf shutter's
blades; stick-slip, struck boards and paper's friction, crackle and flicks.
"""

import functools

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import kit
from recipes import world_kit as W
from recipes.beasts import main_hit, unit
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe, R, S, K
from recipes.g1_ground import struck
from recipes.g1_train import cband
from recipes.gannet_tells import bend
from recipes.house_beasts import close, cloud, held
from recipes.house_folk import drawn
from recipes.k1_asks import lay, leaf_rec, rms, still
from recipes.moose_tells import burst

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

PURE = "sfx_100_v2:glass_01"                     # a real glass ringing almost pure: 2759 Hz, a twin 28 Hz under it
STEAM = "sfx_100_v2:air_01"                      # a real long, even hiss of air: the steam jet
BOIL = S("loop_water_01", "loop_water_02")       # real water: a mass of it moving; pouring and trickling
REED = "sfx_100_v2:misc_12"                      # a real held, reedy tone round 681 Hz, a second voice beating sharp
GASP = "sfx_100_v2:misc_05"                      # a real gasp: air forced through a mouth
PUFF = "sfx_100_v2:air_03"                       # a real short burst of air
TICKER = "sfx_100_v2:misc_28"                    # a real mechanism ticking fast and even, sixty a second
PAWLS = S("misc_20", "lock_open_01")             # a real ratchet and pawl; a real lock's pawl
TICKS = R("metalClick") + S("misc_01")           # small real metal ticks: a clock's escapement
DOORS = K("impactWood_light")                    # light wood knocked: 250-340 Hz bodies, short
CLICKS = ["kenney_ui-audio:click3", "kenney_ui-audio:click4", "kenney_ui-audio:click5"]   # tiny dry clicks, 1.5-2.4 kHz
FLARE = "sfx_100_v2:misc_03"                     # a real sharp broadband burst trailing off in a hiss
WHUMP = "sfx_100_v2:misc_32"                     # a soft low whoomph of moving air (its head, before the knock at its end)
CRACKS = S("switch_01", "switch_02")             # a real switch cracked through the air: a snap's sharp front
SPARKS = S("misc_02", "stones_03")               # a real dry, straw-like rustle; real stones scraping and crackling
SHUTTER = "sfx_100_v2:misc_17"                   # a camera's shutter mechanism: its strike at 0.12 s, two small clicks after
BOOK_CLOSE = R("bookClose")[0]                   # a real book shut: the cover's slap, a 260 Hz body
PLACES = R("bookPlace1", "bookPlace2", "bookPlace3")   # a real book set down flat: boards on a table
FLIPS = R("bookFlip1", "bookFlip2", "bookFlip3")       # real pages turned and riffled
CREAK = R("creak1")[0]                           # a real wooden creak: a squeak round 380 Hz and its harmonics, then 1.1 kHz
LEATHER = R("handleSmallLeather", "handleSmallLeather2")   # real leather handled: a strap, a pouch
DROP_LEATHER = R("dropLeather")[0]               # a real heavy leather drop
FIBRES = R("clothBelt2")[0]                      # a real belt's cloth crackling: fibres giving
BRISTLE = "sfx_100_v2:misc_02"                   # a real dry, straw-like rustle
BENCH = K("impactWood_medium")                   # a block of wood knocked: the bench's boards

# ---- Bands and bodies ---------------------------------------------------------------------------------------------------

SQUISH = 150.0              # the director's line: an impact keeps most of its energy above it
LEAD = 0.015                # silence before a one-shot's first part (lead_in)

KETTLE_L = 8.0              # the kettle's cycle (s)
SONG = (1000, 4000)         # where the kettle sings: over a locomotive's roar
KEPT = (300, 6000)          # what the loop is held to: nothing under 300 Hz
WHISTLE = 2150.0            # the whistle's note at full song (Hz)
GLASS_HZ = 2759.0           # the real glass's upper partial (its twin at 2731 Hz)
PUSH = 1.8                  # semitones of pitch per unit of push: a whistle sharpens a little as the boil surges
HOLLOW = [480, 1150, 2050]  # a kettle's hollow (house_folk.KETTLE)

CUCK, OO = 700.0, 590.0     # the bird's two pipes (Hz): a falling minor third
REED_HZ = 681.0             # the real reed's pitch (Hz)
LEAF = [880, 1630, 2550, 3800]       # the clock's little door: a leaf of thin wood a few centimetres across
CASE = [520, 940, 1500, 2300]        # the clock's case and the door's frame: small, thin boards
BRASS = [3100, 4700, 6900]           # a small brass wheel's tooth knocked
FLY = 0.45                  # the fan fly's air against its tick rate: two blades, a little slip
PENDULUM = 0.42             # the pendulum's beat (s): a cuckoo clock's quick tick-tock

SPINE = [620, 1350, 2200, 3300]      # a logbook's glued leather spine, flexed: small, stiff and dry
COVER = [190, 320, 540, 860, 1350]   # its thick boards, leather over them, landing flat
BENCH_M = [230, 410, 680, 1100]      # the bench's boards under it


# ---- Shared parts -------------------------------------------------------------------------------------------------------

def lead_in(x):
    """A one-shot with `LEAD` s of silence before it: build.py fades every take in over 2 ms from its first sound less
    10 ms, so a part placed at 0 would lose its attack (a door's crack, a page's flick); this keeps the fade in the
    silence."""
    return np.concatenate([np.zeros(samples(LEAD), np.float32), np.asarray(x, np.float32)])


def sung(x, semis):
    """A loop `x` read back along a pitch curve `semis` (per sample, periodic): faster where it's sharp, its read speed
    scaled so one cycle of the output reads exactly one cycle of x, so the loop stays a loop."""
    n = len(x)
    r = 2 ** (np.asarray(semis, np.float64) / 12)
    r = r * n / np.sum(r)
    pos = np.concatenate([[0.0], np.cumsum(r)[:-1]])
    return np.interp(pos % n, np.arange(n + 1), np.append(x, x[0])).astype(np.float32)


def boil(n, rng):
    """The steam's push through the whistle over one cycle (about 1, periodic): the boil surging and easing a few times
    a cycle (nothing faster than 0.4 Hz) and the jet's turbulence shaking it (a fast flutter, under 12 Hz)."""
    return (1 + 0.1 * W.slow(n, 0.4, rng) + 0.035 * W.slow(n, 12, rng)).astype(np.float32)


def speaks(p, at=0.8, over=0.15, floor=0.35):
    """How fully the whistle sings on push `p`: full at the boil's mean, wavering down towards `floor` (the jet breaking
    off its edge into air) where the boil eases."""
    return (floor + (1 - floor) * np.clip((p - at) / over, 0, 1) ** 1.2).astype(np.float32)


def resonance(f, q):
    """A resonance's amplitude response at f (Hz), for W.cfilter."""
    return lambda F: 1 / np.sqrt(1 + (q * (F / f - f / F)) ** 2)


def hollow_of(F):
    """The kettle's hollow: its three broad modes (q 4) over a floor."""
    return 0.35 + sum(resonance(m, 4)(F) * g for m, g in zip(HOLLOW, (1.0, 0.7, 0.45)))


def kettle_mix(rng, n, p, tone, upper, whine, jet, hiss, water):
    """The kettle in full song: the whistle's two tones (`tone`) sung on the push, its second hole (`upper`) joining on
    the hardest surges, the airy whine of the jet at the note, the jet's breath round it and its hiss over it, the
    water's rumble-hiss under it; each set by its rms. A whisker of the iron cab round it (circular), held to 300 Hz-6
    kHz, levelled round three cycles and handed over seamless (house_beasts.close)."""
    s = speaks(p)
    lift = np.clip((p - 1.05) / 0.08, 0, 1) ** 1.5
    y = (tone / rms(tone) * s * p ** 2
         + upper / rms(upper) * lift * 0.4
         + whine / rms(whine) * p * 0.5
         + jet / rms(jet) * p * (0.4 + 0.25 * (1 - s))
         + hiss / rms(hiss) * p * 0.5
         + water / rms(water) * 0.35)
    return close(y, rng, KEPT, wet=0.06, drive=2.0, lufs=-24, room="cab")


# ---- crew-car.kettle: the kettle on the back wall, singing ----------------------------------------------------------------

def jet_rec(rng, n):
    """The steam jet's air, from a real long, even hiss laid round the loop: rung round the whistle's note (its breath)
    and held high (its hiss)."""
    c = cloud(rng, STEAM, n, 0.0, grain_s=0.5)
    return W.cfilter(c, resonance(WHISTLE, 9)), cband(c, 1500, 7000)


def water_rec(rng, n):
    """The water's rumble-hiss on the boil, from real water laid round the loop: a mass of it moving and a trickle's
    bubbling, its rumble taken off under 300 Hz and rung in the kettle's hollow, swelling with the boil."""
    a = cband(cloud(rng, BOIL[0], n, 0.0, grain_s=0.6), 300, 2400)
    b = cband(cloud(rng, BOIL[1], n, 2.0, grain_s=0.4), 400, 3000)
    y = W.cfilter(W.norm(a) + W.norm(b) * 0.6, hollow_of)
    return y * (0.75 + 0.25 * W.norm(W.slow(n, 2.0, rng)))


@recipe("crew-car", "kettle", "spout",
        "The kettle singing: a real glass's two close partials held as a whistle, beating, shrill, the jet's air round it",
        """From real recordings: a small iron kettle with a two-hole whistle, on the warm shelf at the back wall, in full
        song (the game scales its level with the boiler gauge). A real glass ringing almost pure (sfx_100 glass_01) has
        two partials 28 Hz apart; set at 2.15 kHz and held still as steady tones they are the whistle's two holes, beating
        22 times a second (a shrill, rough waver); the same ring frozen short under it into a narrow band of noise (the jet's
        breath in the note). Both are pulled along the boil's push: the pitch rising a few tens of cents as each surge
        comes on, fluttering with the jet's turbulence, the tone wavering towards air where the boil eases; on the hardest
        surges the second hole joins a little sharper. Round it the jet's air (a real long, even hiss laid round the loop,
        rung round the note and held high), under it the water's rumble-hiss (real moving water and a trickle, rung in the
        kettle's hollow). A whisker of the iron cab. 8 s exact cycle, held to 300 Hz-6 kHz, its weight in 1-4 kHz.""",
        sources=[PURE, STEAM] + BOIL, takes=1, loop=True, band=SONG, lufs=-24, seconds=KETTLE_L)
def kettle_spout(rng, k):
    n = samples(KETTLE_L)
    p = boil(n, rng)
    semis = PUSH * (p - 1)
    s0 = round(12 * np.log2(WHISTLE / GLASS_HZ), 3)
    tone = sung(still(rng, PURE, s0, n), semis)
    upper = sung(still(rng, PURE, round(s0 + 0.45, 3), n), semis)
    whine = sung(held(rng, PURE, WHISTLE, n, window=0.05), semis)
    jet, hiss = jet_rec(rng, n)
    return kettle_mix(rng, n, p, tone, upper, whine, jet, hiss, water_rec(rng, n))


def tones(n, rng, freqs, semis, drift=4.0, harm=0.05, flicker=0.12):
    """Near-sine tones whole cycles round a loop of n samples, pulled along `semis` together and each drifting `drift`
    cents (rms) on its own, a faint second harmonic over each, their levels flickering with the jet (`flicker`, under 25
    Hz). The phase is summed in double precision from the first sample, so the cycle closes exactly."""
    out = np.zeros(n)
    for f in freqs:
        fi = f * 2 ** ((np.asarray(semis, np.float64) + drift / 100 * W.slow(n, 0.3, rng)) / 12)
        fi *= round(fi.sum() / SR) / (fi.sum() / SR)
        ph = 2 * np.pi * (np.cumsum(fi) - fi[0]) / SR + rng.uniform(0, 2 * np.pi)
        out += (np.sin(ph) + harm * np.sin(2 * ph + 0.6)) * (1 + flicker * W.slow(n, 25, rng))
    return out.astype(np.float32)


def bubbles_loop(rng, n, rate, lo=450, hi=2600):
    """The boil's bubbles round a loop: Minnaert rings `rate` a second, sizes spread, placed round the cycle."""
    ev = [(t, synth.bubble(float(np.exp(rng.uniform(np.log(lo), np.log(hi)))), rise=rng.uniform(0.05, 0.3)),
           rng.uniform(0.2, 1.0) ** 1.5) for t in W.poisson(n / SR, rate, rng)]
    return W.place(n, ev)


@recipe("crew-car", "kettle", "jet",
        "The kettle singing, modelled: two close sine tones beating on a steam jet, its pitch rising with each surge",
        """Modelled: the steam jet through a two-hole whistle, in full song. Two near-sine tones (the two holes, 2.15 kHz
        and one 18 cents sharp) beating about 22 times a second, a faint second harmonic over each, each drifting a few
        cents on its own so the beat never settles; both pulled along the boil's push (rising a few tens of cents as a
        surge comes on, fluttering with the jet's turbulence), their levels flickering, the tone wavering towards air
        where the boil eases, and a third tone a little sharper joining on the hardest surges. The jet's own air rung round
        the note (q 9) and a hiss over it; under it the water's rumble-hiss (noise rung in the kettle's hollow, 480 Hz-2
        kHz) with the boil's bubbles bursting in it. A whisker of the iron cab. 8 s exact cycle, held to 300 Hz-6 kHz,
        its weight in 1-4 kHz.""",
        takes=1, loop=True, band=SONG, lufs=-24, seconds=KETTLE_L)
def kettle_jet(rng, k):
    n = samples(KETTLE_L)
    p = boil(n, rng)
    semis = PUSH * (p - 1)
    tone = tones(n, rng, [WHISTLE, WHISTLE * 2 ** (18 / 1200)], semis)
    upper = tones(n, rng, [WHISTLE * 2 ** (45 / 1200)], semis, harm=0.03)
    whine = sung(W.cfilter(W.pnoise(n, rng), resonance(WHISTLE, 40)), semis)
    jet = W.cfilter(W.pnoise(n, rng), resonance(WHISTLE, 9))
    hiss = cband(W.pnoise(n, rng), 1500, 7000)
    rumble = W.cfilter(cband(W.pnoise(n, rng, lambda F: 1 / np.sqrt(F / 300)), 300, 2200), hollow_of)
    water = (W.norm(rumble) * (0.75 + 0.25 * W.norm(W.slow(n, 2.0, rng))) + W.norm(bubbles_loop(rng, n, 70)) * 0.5)
    return kettle_mix(rng, n, p, tone, upper, whine, jet, hiss, water)


# ---- crew-car.cuckoo: the cuckoo clock before dawn ----------------------------------------------------------------------

@functools.lru_cache(maxsize=8)
def _reed(semis):
    """The real reed's steady held stretch (0.1-1.0 s), varispeeded `semis` (a sampler's note)."""
    x = ck.get(REED)[samples(0.1):samples(1.0)]
    return dsp.vari(x, semis)


def blown(L, sag):
    """A bellows pipe's pitch (semitones over its note) and level over a note `L` s long: the bellows' weight coming on
    scoops it up into its note, it holds, and it sags `sag` semitones and dies away as the bellows empties."""
    pitch = env([(0, -0.45), (0.035, 0.0), (L * 0.55, 0.0), (L, -sag)], L)
    level = env([(0, 0), (0.012, 0.75), (0.035, 1.0), (L * 0.55, 0.85), (L - 0.035, 0.55), (L, 0)], L)
    return pitch, level


def pipe_rec(rng, f, L, sag):
    """One pipe blown by its bellows, from a real reed's held tone: its steady stretch played at the pipe's note, pulled
    along the bellows' pitch, its top rolled off for a small wooden pipe; a real breath drawn out through it (a real
    gasp, rung round the note and its second), strongest as the note speaks (the chiff) and as it dies."""
    pitch, level = blown(L, sag)
    x = _reed(round(12 * np.log2(f / REED_HZ), 2))
    m = samples(L + 0.1)
    a = int(rng.integers(0, max(1, len(x) - m)))
    tone = lp(dsp.fit(bend(x[a:a + m], pitch), samples(L)), 2800, 2) * level
    air = drawn(rng, GASP, L, rng.uniform(-2, 0))
    air = mix(unit(bp(air, f * 0.9, f * 5)), unit(dsp.resonate(air, [f, 2 * f], q=10, gains=[1, 0.5])) * 0.8,
              length=samples(L))
    chiff = env([(0, 0), (0.008, 1), (0.05, 0.3), (L - 0.05, 0.25), (L, 0)], L)
    return unit(mix(unit(tone), unit(air) * chiff * 0.4))


def puff_rec(rng, k):
    """The bellows dropping: a slice of a real short burst of air, darkened and taken off its rumble."""
    x = ck.get(PUFF)
    a = samples(0.04 + 0.03 * (k % 3))
    y = ck.cut(x, a, a + samples(0.07), 0.006, 0.04)
    return unit(lp(hp(y, 350, 2), 1800, 2))


def door_rec(rng, k, shut=False):
    """The little door clacking against its stop (or shut in its frame), from real wood: a light wood knock pitched up
    an octave and more for a leaf a few centimetres across, choked short; a tiny dry click for its wire hinge, and shut,
    a second for its catch dropping. High-passed at 250 Hz: a clack, never a thump."""
    knock = dsp.vari(hit_of(DOORS[(k + 2 * shut) % 5], 0, 0.12), rng.uniform(12, 14) - (1.5 if shut else 0))
    knock = ck.choke(knock, 0.01, 0.018)
    tick = dsp.vari(hit_of(CLICKS[(k + shut) % 3], 0, 0.04), rng.uniform(-2, 1))
    parts = [(0, unit(knock), 0), (0.002, unit(tick), -9)]
    if shut:
        parts.append((rng.uniform(0.012, 0.02), unit(dsp.vari(hit_of(CLICKS[(k + 2) % 3], 0, 0.04), 2)), -12))
    return hp(ck.place(parts), 250, 4)


@functools.lru_cache(maxsize=1)
def _teeth():
    """The real fast mechanism's ticks, each cut to 10 ms."""
    x = ck.get(TICKER)
    return [unit(ck.cut(x, s - samples(0.0005), s + samples(0.01), 0.0003, 0.004))
            for s, _ in ck.hits(x, floor_db=-24, gap=0.008, prom=4)[:80]]


def fly_air(rng, L, rate):
    """The fan fly's two blades beating the air: a slice of a real even hiss, held to 0.8-4 kHz and chopped at the
    blades' rate as the fly spins (`rate` a curve)."""
    n = samples(L)
    h = ck.get(STEAM)
    a = int(rng.integers(0, max(1, len(h) - n - 1)))
    ph = 2 * np.pi * np.cumsum(synth.curve(rate, n) * 2 * FLY) / SR
    return unit(bp(dsp.fit(h[a:], n), 800, 4000)) * (0.3 + 0.7 * (0.5 + 0.5 * np.sin(ph)) ** 3)


def whirr_rec(rng, L, rate):
    """The works running: the strike train's wheels ticking (a real fast mechanism's ticks, pitched up for a clock's
    small brass wheels) at `rate` a second (a curve: spinning up, coasting), and its fan fly beating the air."""
    g = _teeth()
    n = samples(L)
    rc = synth.curve(rate, n)
    b = Bus(L + 0.03)
    t = 0.0
    while t < L:
        r = float(rc[min(samples(t), n - 1)])
        b.at(t, dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(3, 6)), rng.uniform(-7, 0))
        t += rng.uniform(0.9, 1.1) / max(r, 5.0)
    return hp(mix(unit(b.x), fly_air(rng, L, rc) * 0.18), 700, 2)


def cam_rec(rng, k):
    """A cam's click as a bellows is lifted and let drop, or the locking lever falling: a real pawl's tooth, pitched up."""
    x = hit_of(PAWLS[k % 2], int(rng.integers(3)), 0.03)
    return unit(hp(dsp.vari(x, rng.uniform(2, 5)), 600, 2))


def pendulum_rec(rng, length, phase):
    """The clock keeping time under it all: the escapement's tick and its duller tock every `PENDULUM` s from `phase`
    (the packs' real small metal clicks, a clock's escapement, pitched for brass on steel and choked)."""
    parts = []
    t, i = phase, 0
    while t < length:
        x = dsp.vari(hit_of(TICKS[i % 2], 0, 0.06, -18), (2.0, -1.5)[i % 2] + rng.uniform(-0.3, 0.3))
        parts.append((t, unit(ck.choke(hp(x, 600, 2), 0.003, 0.01)), (0, -2)[i % 2] + rng.uniform(-0.5, 0.5)))
        t, i = t + PENDULUM, i + 1
    return ck.place(parts, length + 0.1)


def spin(L, top, start=0.4, end=0.5):
    """A run of the works: the fly spinning up to `top` ticks a second and coasting to a stop, as a curve over L s."""
    return env([(0, top * start), (min(0.06, L * 0.3), top), (L * 0.7, top), (L, top * end)], L)


def in_case(x, rng):
    """In its small wooden case on the back wall: the case's hollow ringing a little round it, then a whisker of the car,
    the squish under 150 Hz cut away."""
    y = mix(x, dsp.resonate(x, CASE, q=6, gains=[0.5, 0.4, 0.3, 0.2]) * 0.25)
    y = ck.space(y, 0.1, 0.16, 6000, ((0.0015, 0.5), (0.0032, 0.35)))
    return hp(dsp.room(y, "car", wet=0.06, rng=np.random.default_rng(13)), SQUISH, 4)


def cuckoo(rng, k, door, whirr, cam, pipe, puff, tick):
    """One appearance, about 2.7 s, the pendulum ticking under it all: the door clacks open, the works whirr the bird
    out, a cam's click lifts the first bellows, "cuck", a click, "oo" (longer, sagging), and the bird sits out a moment;
    then the works whirr it back in, the locking lever clicks and the door clacks shut on it. Three takes: an even one;
    one with the notes slower and the 'oo' held; one where the works run stiff, catching once on the way back. The pitch
    drifts a few cents take to take: an old clock."""
    v = k % 3
    drift = 2 ** (rng.uniform(-0.12, 0.06) / 12)
    t_c = (0.4, 0.44, 0.38)[v] + rng.uniform(-0.01, 0.01)
    L_c = (0.2, 0.19, 0.22)[v]
    t_o = t_c + L_c + (0.12, 0.15, 0.1)[v]
    L_o = (0.4, 0.48, 0.38)[v]
    t_s = (2.62, 2.66, 2.6)[v] + rng.uniform(-0.02, 0.02)
    out = t_c - 0.1
    under = t_o + L_o - t_c
    parts = [(0.0, door(rng, k), 3),
             (0.03, whirr(rng, out, spin(out, 70)), -12),
             (t_c - 0.05, cam(rng, k), -8), (t_c - 0.012, puff(rng, k), -15), (t_c, pipe(rng, CUCK * drift, L_c, 0.18), 0),
             (t_c + 0.02, whirr(rng, under, spin(under, 45, 1.0, 0.6)), -22),
             (t_o - 0.05, cam(rng, k + 1), -9), (t_o - 0.012, puff(rng, k + 1), -15),
             (t_o, pipe(rng, OO * drift, L_o, 0.32), -1),
             (t_s, door(rng, k, True), 4)]
    if v == 2:      # the works catch on the way back: a run, a stop, a click, and they run on
        runs = [(t_s - 0.8, 0.3), (t_s - 0.38, 0.35)]
        parts.append((t_s - 0.5, cam(rng, k + 3), -9))
    else:
        L_b = (0.62, 0.55)[v]
        runs = [(t_s - 0.03 - L_b, L_b)]
    parts += [(t, whirr(rng, L, spin(L, 75)), -11) for t, L in runs]
    parts.append((t_s - 0.025, cam(rng, k + 2), -8))
    parts.append((0.0, tick(rng, t_s + 0.3, rng.uniform(0, PENDULUM)), -17))
    return in_case(lead_in(ck.place(parts, t_s + 0.3)), rng)


@recipe("crew-car", "cuckoo", "bellows",
        "The cuckoo clock before dawn: its little door clacks, the works whirr, two reedy wooden pipes call 'cuck-oo'",
        """From real recordings: a wooden cuckoo clock on the crew car's back wall. Its little door clacks open against
        its stop (a light wood knock pitched up an octave and more for a leaf a few centimetres across, choked short, and
        a tiny dry click for its wire hinge), the works whirr the bird out (a real fast mechanism's ticks pitched up for a
        clock's small brass wheels, spinning up and coasting, and the fan fly beating the air, a real hiss chopped at its
        blades' rate), a cam clicks (a real pawl's tooth) as each bellows is lifted and dropped (a real burst of air,
        darkened), and the bird calls "cuck-oo": two small wooden pipes, about 700 then 590 Hz, a falling minor third,
        from a real held reedy tone (sfx_100 misc_12) played at each note, scooping up into it as the bellows' weight
        comes on and sagging as it empties, its top rolled off for wood, with a real breath drawn through it (the chiff as
        it speaks); the bird sits out a moment, the works whirr it back in, the locking lever clicks, and the door clacks
        shut on its catch. Under it all the pendulum's quick tick-tock (real small metal clicks, a clock's escapement),
        keeping time towards dawn. In its small wooden case. Takes: an even call; a slower one, the 'oo' held; the works
        running stiff and catching once on the way back.
        2.6-2.9 s. Homely, a little ominous, never a creature. The clacks high-passed: nothing under 150 Hz.""",
        sources=[REED, GASP, PUFF, TICKER, STEAM] + PAWLS + DOORS + CLICKS + TICKS, takes=3, lufs=-22)
def cuckoo_bellows(rng, k):
    return cuckoo(rng, k, door_rec, whirr_rec, cam_rec, pipe_rec, puff_rec, pendulum_rec)


def wobble(rng, n, rate):
    """A small, uneven wander (unit-peaked), nothing faster than `rate` Hz."""
    w = lp(rng.standard_normal(n + SR).astype(np.float32), rate)[SR:]
    return (w / (np.max(np.abs(w)) + 1e-9)).astype(np.float32)


def pipe_model(rng, f, L, sag):
    """One pipe blown by its bellows, modelled: a small stopped wooden flue pipe (its odd harmonics strong, the even ones
    faint), its jet's air rung round the note (q 10) and a breath of hiss over it, a chiff as it speaks (the jet's noise
    rung round its third harmonic), pulled along the bellows' pitch and level."""
    pitch, level = blown(L, sag)
    n = samples(L)
    fi = f * 2 ** ((pitch + 0.05 * wobble(rng, n, 5.0)) / 12)
    ph = 2 * np.pi * np.cumsum(fi) / SR + rng.uniform(0, 2 * np.pi)
    amps = [1.0, 0.1, 0.4, 0.05, 0.16, 0.03, 0.07]
    tone = sum(a * np.sin((i + 1) * ph + 0.3 * i) for i, a in enumerate(amps)) * level
    noise = synth.noise(L, rng)[:n]
    air = unit(dsp.resonate(noise, [f, 3 * f], q=10, gains=[1, 0.5]))
    hiss = unit(bp(synth.noise(L, rng, "pink")[:n], 1200, 5000))
    chiff = unit(dsp.resonate(noise, [3 * f], q=4)) * env([(0, 0), (0.006, 1), (0.04, 0)], L)
    breath = env([(0, 0), (0.008, 1), (0.05, 0.3), (L - 0.05, 0.25), (L, 0)], L)
    y = unit(mix(unit(tone.astype(np.float32)), air * breath * 0.3, hiss * breath * 0.08, chiff * 0.35))
    return y * 0.65         # a steady sum of partials carries more for its peak than the real reed: matched to it


def puff_model(rng, k):
    """The bellows dropping, modelled: a soft breath of air, dark, swelling and gone."""
    L = 0.07
    return unit(lp(hp(synth.noise(L, rng, "pink"), 350, 2), 1600, 2) * env([(0, 0), (0.015, 1), (L, 0)], L))


def door_model(rng, k, shut=False):
    """The little door clacking, modelled: a leaf of thin wood a few centimetres across struck at its edge (its modes
    from 880 Hz, a quick, hard contact, and the crack of it), the frame it meets knocked under it, and a tiny tick of
    its wire hinge (and, shut, its catch)."""
    leaf = struck(rng, [m * rng.uniform(0.95, 1.05) * (0.92 if shut else 1.0) for m in LEAF], 7, 0.04, hit=0.0006)
    frame = struck(rng, [m * rng.uniform(0.95, 1.05) for m in CASE], 5, 0.05, hit=0.0012)
    crack = burst(rng, 0.0008, 1500, 9000, attack=0.1)
    parts = [(0, leaf, 0), (0, unit(crack), -3), (0.001, frame, -5 if shut else -8),
             (0.002, unit(ck.tick(rng, rng.uniform(2200, 3200), q=14, length=0.015)), -10)]
    if shut:
        parts.append((rng.uniform(0.012, 0.02), unit(ck.tick(rng, rng.uniform(2600, 3600), q=16, length=0.015)), -12))
    return unit(hp(ck.place(parts), 250, 4)) * 1.4       # as loud as the real knock: its modes ring longer for their peak


def whirr_model(rng, L, rate):
    """The works running, modelled: a small brass wheel's teeth ticking at `rate` a second (a curve), each a hard little
    tick through its modes, a little different each time, and the fan fly beating the air at its blades' rate."""
    n = samples(L)
    rc = synth.curve(rate, n)
    b = Bus(L + 0.03)
    t = 0.0
    while t < L:
        r = float(rc[min(samples(t), n - 1)])
        body = [m * rng.uniform(0.94, 1.06) for m in BRASS]
        b.at(t, unit(synth.click(body[0], q=18, length=0.01, rng=rng, body=body)), rng.uniform(-7, 0))
        t += rng.uniform(0.9, 1.1) / max(r, 5.0)
    ph = 2 * np.pi * np.cumsum(rc * 2 * FLY) / SR
    fly = unit(bp(synth.noise(L, rng, "pink"), 800, 4000)) * (0.3 + 0.7 * (0.5 + 0.5 * np.sin(ph)) ** 3)
    return hp(mix(unit(b.x), fly * 0.18), 700, 2)


def pendulum_model(rng, length, phase):
    """The clock keeping time, modelled: a brass tick and a duller tock every `PENDULUM` s, each a click rung through an
    escapement's small modes."""
    parts = []
    t, i = phase, 0
    while t < length:
        f = (3300.0, 2600.0)[i % 2] * rng.uniform(0.98, 1.02)
        x = synth.click(f, q=24, length=0.025, rng=rng, body=[f, f * 1.61, f * 2.37])
        parts.append((t, unit(x), (0, -2)[i % 2] + rng.uniform(-0.5, 0.5)))
        t, i = t + PENDULUM, i + 1
    return ck.place(parts, length + 0.1)


def cam_model(rng, k):
    """A cam's click, modelled: a steel lever dropping off its cam, a hard tick through small steel modes."""
    return unit(synth.click(rng.uniform(2200, 2800), q=12, length=0.025, rng=rng))


@recipe("crew-car", "cuckoo", "flue",
        "The cuckoo clock before dawn, modelled: a wooden door's clack, a gear train's whirr, two stopped flue pipes",
        """Modelled: the clock's little door, a leaf of thin wood struck at its edge (its modes from 880 Hz) against its
        frame, with its wire hinge's tick; the works whirring the bird out (a small brass wheel's teeth ticking 45-75
        times a second as the fly spins up and coasts, each through its modes, and the fan fly's two blades beating the
        air); a steel lever's click off its cam as each bellows drops (a soft, dark breath of air), and the two notes from
        small stopped wooden flue pipes (700 then 590 Hz, a falling minor third: odd harmonics strong, even ones faint,
        the jet's air rung round the note, a chiff as each speaks), scooping into the note as the bellows' weight comes on
        and sagging as it empties; the works whirring it back in, the lever's click, the door's clack and its catch; the
        pendulum's brass tick-tock under it all (clicks rung through an escapement's small modes). In its small wooden
        case. The same three takes as 'bellows'. 2.6-2.9 s, nothing under 150 Hz.""",
        takes=3, lufs=-22)
def cuckoo_flue(rng, k):
    return cuckoo(rng, k, door_model, whirr_model, cam_model, pipe_model, puff_model, pendulum_model)


# ---- crew-memory.flash: the crew photo at the gate ----------------------------------------------------------------------

def outdoors(x, rng, length):
    """At the gate in the open, the train beside it: a slap off its side and a breath of the night (world_kit.space),
    cut to `length` and faded, the squish under 150 Hz cut away."""
    y = dsp.fit(W.space(x, rng, "night", wet=0.06), samples(length))
    return hp(dsp.fade(y, 0.0, min(0.2, length * 0.3)), SQUISH, 4)


def _first(key, length):
    """A recording's first hit as (start, end) samples, `length` s long."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=-20, gap=0.01)
    a = (h[0][0] if h else 0) - samples(0.0005)
    return a, a + samples(length)


def flare_rec(rng, k):
    """The powder flaring, from real recordings: a real switch's crack cut to its first instant for the cap's snap; a
    real sharp burst with a hissing tail for the flare's front (the 'fff'); the head of a real whoomph of moving air with
    a quick front put on it for the hot gas puffing out (the 'ump'), high-passed at 320 Hz so it's a puff, not a boom
    (and the whoomph's own low hum, 140 and 245 Hz, is gone); a real short burst of air for the rush of it."""
    snap = hp(ck.cut(ck.get(CRACKS[k % 2]), *_first(CRACKS[k % 2], 0.006), 0.0003, 0.003), 1500, 2)
    front = hp(dsp.vari(main_hit(FLARE, 0.3), rng.uniform(-1.5, 0.5)), 400, 4)
    w = ck.get(WHUMP)
    gas = dsp.vari(ck.cut(w, samples(0.03), samples(0.2), 0.001, 0.06), rng.uniform(-2, 0))
    Lg = len(gas) / SR
    gas = hp(gas * dsp.fit(env([(0, 0), (0.004, 1), (Lg * 0.4, 0.6), (Lg, 0)], Lg), len(gas)), 320, 4)
    rush = hp(dsp.vari(main_hit(PUFF, 0.22), rng.uniform(-3, -1)), 400, 2)
    return ck.place([(0, unit(snap), -12), (0.002, unit(front), 0), (0.004, unit(gas), -3), (0.006, unit(rush), -7)])


@functools.lru_cache(maxsize=1)
def _sparks():
    """Every crackle in the packs' dry rustle and stones' crackling, cut to its first 6 ms: a grain of powder spitting."""
    out = []
    for key in SPARKS:
        x = ck.get(key)
        for s, _ in ck.hits(x, floor_db=-36, gap=0.004, prom=4):
            out.append(unit(hp(ck.cut(x, s, s + samples(0.006), 0.0003, 0.003), 2000)))
    return out


def fizz_rec(rng, length):
    """The powder's fizz dying away, from real recordings: grains of real dry crackle pitched up, spitting fast and
    thinning (300 a second down to a few), over a slice of a real even hiss held high and dying with them."""
    g = _sparks()
    b = Bus(length + 0.02)
    t = 0.0
    while t < length:
        p = t / length
        level = rng.uniform(0.2, 1.0) * (1 - p) ** 1.5 + 1e-3
        b.at(t, dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(3, 8)), 20 * np.log10(level))
        t += rng.exponential(1 / (300 * (1 - p) ** 2 + 4))
    h = ck.get(STEAM)
    n = samples(length)
    a = int(rng.integers(0, max(1, len(h) - n - 1)))
    hiss = unit(bp(dsp.fit(h[a:], n), 2500, 10000)) * env([(0, 1), (length, 0)], length, "exp")[:n]
    return mix(unit(b.x), hiss * 0.35)


def shutter_rec(rng, k, closing=False):
    """The big camera's leaf shutter snapping, from a real camera's shutter: its strike and the small clicks after,
    pitched a little down for a bigger mechanism (closing, its first strike alone, a little brighter)."""
    x = ck.get(SHUTTER)
    h = ck.hits(x, floor_db=-12, gap=0.02)
    a = h[0][0] if h else 0
    L = 0.05 if closing else 0.11
    y = ck.cut(x, a - samples(0.001), a + samples(L), 0.0005, 0.02)
    return unit(hp(dsp.vari(y, rng.uniform(-3, -2) + (1.0 if closing else 0.0)), 300, 2))


def flash(rng, k, flare, fizz, shutter):
    """Three photos: the shutter tripped and the powder flaring a beat after; the powder flaring and the shutter
    snapped shut on it; the shutter opened, the flare, the shutter closed (the old way, on 'bulb'). Each with the
    fizz dying under it, outdoors."""
    v = k % 3
    Lf = rng.uniform(0.5, 0.7)
    if v == 0:
        t_f, parts = 0.08, [(0.0, shutter(rng, k), 0)]
    elif v == 1:
        t_f, parts = 0.0, [(rng.uniform(0.2, 0.26), shutter(rng, k, True), -1)]
    else:
        t_f, parts = 0.1, [(0.0, shutter(rng, k), -2), (rng.uniform(0.7, 0.8), shutter(rng, k + 1, True), -2)]
    parts += [(t_f, flare(rng, k), 0), (t_f + 0.02, fizz(rng, Lf), -13)]
    L = max(t_f + 0.02 + Lf, max(t + len(x) / SR for t, x, _ in parts)) + 0.15
    return outdoors(lead_in(ck.place(parts, L + 0.3)), rng, L + LEAD)


@recipe("crew-memory", "flash", "powder",
        "The crew photo: flash powder going off with a sharp 'fffump' and a fizz, the big camera's shutter snapping",
        """From real recordings, outdoors at the gate with the train beside it: the flash powder in its tray goes off, a
        real switch's crack cut to its first instant for the cap's snap, a real sharp burst with a hissing tail for the
        flare's front (the 'fff'), the head of a real whoomph of moving air given a quick front for the hot gas puffing
        out (the 'ump': high-passed at 320 Hz, a puff, never a boom) and a real short burst of air for the rush of it;
        then its fizz dying away (real dry crackle cut into grains and pitched up, spitting fast and thinning, over a real
        even hiss held high). The big camera's leaf shutter snaps (a real camera's shutter mechanism, a little lower for a
        bigger one). Takes: the shutter tripped, the flare a beat after; the flare, the shutter snapped shut on it; the
        shutter opened, the flare, the shutter closed. 0.8-1.1 s, nothing under 150 Hz to speak of.""",
        sources=[FLARE, WHUMP, PUFF, STEAM, SHUTTER] + CRACKS + SPARKS, takes=3, lufs=-20)
def flash_powder(rng, k):
    return flash(rng, k, flare_rec, fizz_rec, shutter_rec)


def flare_model(rng, k):
    """The powder flaring, modelled: a hard front (a cap's crack, then broadband noise rising in a millisecond and a
    half) through a lowpass that flies open to 9 kHz and closes to 1.5 kHz over a fifth of a second as the flame
    burns out; a puff of hot gas under it (noise in 250 Hz-1.5 kHz swelling in 5 ms and gone in 0.1 s); high-passed at
    170 Hz."""
    L = 0.3
    n = samples(L)
    nz = synth.noise(L, rng)
    fc = env([(0, 3000), (0.004, 9000), (0.05, 4500), (L, 1500)], L, "exp")
    fff = dsp.sweep_filter(nz, "lp", fc, q=0.8) * env([(0, 0), (0.0015, 1), (0.03, 0.55), (0.12, 0.2), (L, 0)], L)
    gas = bp(synth.noise(L, rng, "pink"), 250, 1500) * env([(0, 0), (0.005, 1), (0.04, 0.5), (0.11, 0)], L)
    crack = burst(rng, 0.002, 2000, 10000, attack=0.1)
    y = ck.place([(0, unit(crack), -10), (0.001, unit(fff[:n]), 0), (0.002, unit(gas[:n]), -4)])
    return hp(y, 170, 4)


def fizz_model(rng, length):
    """The fizz, modelled: burning grains spitting (sharp ticks, many quiet and a few loud, 400 a second thinning to a
    few) over a high hiss flickering and dying away."""
    n = samples(length)
    rate = env([(0, 400), (length, 8)], length, "exp")
    spits = synth.crackle(length, rate, rng, size=(0.0002, 0.0015), hi=2500)
    flick = np.clip(1 + 0.5 * wobble(rng, n, 40), 0.2, None)
    hiss = unit(bp(synth.noise(length, rng), 3000, 11000)) * flick * env([(0, 1), (length, 0)], length, "exp")[:n]
    return mix(unit(spits), hiss * 0.3)


def shutter_model(rng, k, closing=False):
    """A leaf shutter, modelled: the release lever's click, the spring snapping the blades open and shut (two hard
    ticks through small steel modes, 12-25 ms apart, a short rasp of the blades between); closing, the blades' snap
    alone."""
    blades = [2600, 4100, 6300, 8800]

    def snap():
        return unit(struck(rng, [m * rng.uniform(0.95, 1.05) for m in blades], 25, 0.02, hit=0.0003))

    rasp = ck.friction(rng, 0.012, rate=1500, lo=3000, hi=9000, rough=0.4)
    if closing:
        return unit(ck.place([(0, snap(), 0), (0.004, rasp, -14)]))
    gap = rng.uniform(0.012, 0.025)
    lever = unit(synth.click(rng.uniform(1500, 2000), q=6, length=0.02, rng=rng))
    return unit(ck.place([(0, lever, -8), (0.01, snap(), 0), (0.012, rasp, -14), (0.01 + gap, snap(), -3)]))


@recipe("crew-memory", "flash", "magnesium",
        "The crew photo, modelled: a flare's hard 'fff' and a puff of hot gas, burning grains fizzing, a leaf shutter",
        """Modelled, outdoors at the gate: the powder flares, a cap's crack and then broadband noise rising in a
        millisecond and a half, through a lowpass that flies open to 9 kHz and closes to 1.5 kHz over a fifth of a
        second as it burns out, with a puff of hot gas under it (250 Hz-1.5 kHz, gone in a tenth of a second; nothing
        under 170 Hz: a puff, no boom); burning grains spitting (sharp ticks, 400 a second thinning to a few) over a high
        hiss flickering away. The camera's leaf shutter: the release lever's click, the spring snapping the blades open and
        shut (hard ticks through small steel modes, a rasp of the blades). The same three takes as 'powder'. 0.8-1.1 s.""",
        takes=3, lufs=-20)
def flash_magnesium(rng, k):
    return flash(rng, k, flare_model, fizz_model, shutter_model)


# ---- crew-memory.open, page, shut: the logbook on the bench ---------------------------------------------------------------

def dry(x):
    """Close and dry on the bench: a whisker of the car's nearest walls, nothing more (ui.tidy's spirit, with a room's
    first reflections)."""
    return ck.space(x, 0.12, 0.06, 6000, ((0.003, 0.35), (0.007, 0.2)))


def seg(key, start, length, semis=0.0, fade_in=0.004, fade_out=0.02):
    """A stretch of a real recording from `start` s, `length` s long, varispeeded `semis`, with short fades."""
    x = ck.get(key)
    y = ck.cut(x, samples(start), samples(start + length), fade_in, fade_out)
    return dsp.vari(y, semis) if semis else y


def tap_rec(rng, which=0, semis=0.0):
    """A sheet or a cover laid flat: a real book set down, its first hit only, darkened and choked to a soft tap."""
    x = hit_of(PLACES[which % 3], 0, 0.1)
    return unit(lp(ck.choke(dsp.vari(x, semis + rng.uniform(-0.5, 0.5)), 0.015, 0.014), 3000))


def spine_rec(rng, length, semis):
    """The spine creaking as the cover is lifted: real leather handled (a strap's creak and a pouch's crackle), pitched
    down for a thick binding, with the low squeak of a real wooden creak (its first 0.16 s) under it, pitched down and
    its top rolled off, so it's glued leather giving and not a hinge."""
    c = seg(CREAK, 0.14, 0.16, semis, 0.01, 0.04)
    c = dsp.fit(c, samples(length)) * env([(0, 0.6), (length * 0.3, 1), (length, 0)], length)
    lea = dsp.fit(dsp.vari(main_hit(LEATHER[0], 0.18), rng.uniform(-3, -1)), samples(length))
    crk = dsp.fit(dsp.vari(main_hit(LEATHER[1], 0.2), rng.uniform(-4, -2)), samples(length))
    return mix(unit(hp(lea, 300)), unit(hp(crk, 500)) * 0.7, unit(lp(c, 1800)) * 0.4)


def cover_rec(rng, take, force=1.0):
    """The thick cover thumped back flat onto the bench: a real book set down (pitched down for heavy boards), the
    leather of it (a real heavy leather drop), the bench's boards knocked under it (a real block of wood, choked);
    high-passed at 160 Hz, so it's a heavy book, not a boom."""
    board = dsp.vari(hit_of(PLACES[take % 3], 0, 0.2), rng.uniform(-3.5, -2) - 0.5 * (force - 1))
    skin = dsp.vari(main_hit(DROP_LEATHER, 0.15), rng.uniform(-1, 1))
    wood = ck.choke(dsp.vari(hit_of(BENCH[(take + 1) % 5], 0, 0.2), rng.uniform(-1, 1)), 0.02, 0.03)
    y = ck.place([(0, unit(board), 0), (0.001, unit(skin), -5), (0.002, unit(wood), -11 + 3 * (force - 1))])
    return hp(y, 160, 4)


def riffle_rec(rng, length, rate, semis=(-3.0, 1.0)):
    """Pages riffling down after it: real page flicks (k1_asks.leaf_rec) fired fast and thinning, quieter as they go."""
    b = Bus(length + 0.08)
    t = 0.0
    while t < length:
        p = t / length
        b.at(t, leaf_rec(rng, semis), rng.uniform(-8, 0) - 10 * p)
        t += rng.uniform(0.6, 1.4) / (rate * (1 - p) + 6)
    return b.x


def swing_rec(rng, length, semis=-6.0):
    """The cover swung over through the air: a real page's swish slowed and darkened."""
    x = seg(FLIPS[0], 0.27, 0.29, semis + rng.uniform(-1, 1), 0.02, 0.06)
    x = dsp.fit(x, samples(length))
    return unit(lp(hp(x, 300, 2), 3000, 2)) * env([(0, 0), (length * 0.6, 1), (length, 0.2)], length)


def logbook_open(rng, k, spine, swing, cover, riffle):
    """The logbook opened: the spine creaks as the cover's lifted, the cover swings over and thumps back flat on the
    bench, pages riffle down after it. Takes: an even one; a stiff new binding, the spine creaking longer; the cover let
    fall, harder, the pages riffling more."""
    v = k % 3
    Lc = (0.16, 0.26, 0.12)[v]
    Ls = (0.2, 0.22, 0.14)[v]
    t_th = Lc * 0.7 + Ls
    Lr = (0.45, 0.42, 0.55)[v]
    force = (1.0, 0.9, 1.4)[v]
    parts = [(0.0, spine(rng, Lc, (-3.0, -4.5, -2.5)[v]), -6), (Lc * 0.7, swing(rng, Ls), -16),
             (t_th, cover(rng, k, force), 0),
             (t_th + 0.025, riffle(rng, Lr, (70, 55, 90)[v]), -8)]
    if v == 1:     # the binding creaks again as the cover settles
        parts.append((t_th + 0.08, spine(rng, 0.1, -5.0), -14))
    return dry(lead_in(ck.place(parts, t_th + Lr + 0.25)))


@recipe("crew-memory", "open", "leather",
        "The logbook opened: its spine creaking, the heavy leather cover thumped back on the bench, pages riffling",
        """From real recordings, close and dry on the bench: the spine creaking as the cover is lifted (real leather
        handled, a strap's creak and a pouch's crackle, pitched down for a thick binding, with a real wooden creak's low
        squeak under it, darkened), the cover swung over (a real
        page's swish, slowed and darkened) and thumped back flat onto the bench (a real book set down, pitched down for
        heavy boards, a real heavy leather drop for its leather, a real block of wood knocked under it for the bench's
        boards), and a few pages riffling down after it (real page flicks, thinning). Takes: an even one; a stiff new
        binding, the spine creaking longer and again as it settles; the cover let fall, harder, the pages riffling more.
        0.8-1 s. High-passed at 160 Hz: a heavy book, never a boom.""",
        sources=[CREAK, DROP_LEATHER] + LEATHER + PLACES + BENCH + FLIPS, takes=3, lufs=-22)
def open_leather(rng, k):
    return logbook_open(rng, k, spine_rec, swing_rec, cover_rec, riffle_rec)


def spine_model(rng, length, semis):
    """The spine creaking, modelled: glued leather flexing, stick-slip (150-260 slips a second, catching) rung through
    the spine's modes, pitched by `semis`."""
    body = [m * 2 ** ((semis + 3) / 12) for m in SPINE]
    rate = env([(0, 150), (length * 0.5, 260), (length, 120)], length)
    c = synth.creak(length, rate, rng, body=body, q=5, jitter=0.5, grit=0.5)
    y = unit(hp(c, 350, 2) * env([(0, 0), (0.01, 1), (length * 0.7, 0.8), (length, 0)], length))
    return y * 0.5          # a dense run of slips carries more for its peak than the real leather's few: matched to it


def cover_model(rng, take, force=1.0):
    """The cover thumped flat onto the bench, modelled: its thick boards' broad modes rung by a soft, wide contact, the
    leather's slap on the wood, the bench's boards knocked under it; high-passed at 160 Hz."""
    boards = ck.hollow(rng, [m * (1 - 0.04 * (force - 1)) for m in COVER], 0.14, q=4, hit=0.003)
    slap = kit.slap(rng, 0.05, 2600, wet=0.0)
    wood = struck(rng, [m * rng.uniform(0.95, 1.05) for m in BENCH_M], 8, 0.05, hit=0.0015)
    y = ck.place([(0, boards, 0), (0, unit(slap), -6 + 2 * (force - 1)), (0.001, wood, -11 + 3 * (force - 1))])
    return hp(y, 160, 4)


def flick_model(rng, semis=(-3.0, 1.0)):
    """One page's edge flicking, modelled: a crisp burst in paper's band."""
    s = 2 ** (rng.uniform(*semis) / 12)
    return unit(burst(rng, rng.uniform(0.0015, 0.004), 1800 * s, 9000, attack=0.15))


def riffle_model(rng, length, rate, semis=(-3.0, 1.0)):
    """Pages riffling down, modelled: page edges flicking fast and thinning over a soft rustle of paper."""
    b = Bus(length + 0.05)
    t = 0.0
    while t < length:
        p = t / length
        b.at(t, flick_model(rng, semis), rng.uniform(-8, 0) - 10 * p)
        t += rng.uniform(0.6, 1.4) / (rate * (1 - p) + 6)
    ru = synth.rustle(length, env([(0, 300), (length, 30)], length, "exp"), rng, f=(1500, 7000), ticks=0.0)
    return mix(unit(b.x) * 0.7, unit(ru) * env([(0, 1), (length, 0)], length) * 0.5)


def swing_model(rng, length, semis=-6.0):
    """The cover swung over, modelled: a soft whoosh of air, its band rising and falling with the swing."""
    return unit(ck.whoosh(rng, length, 250, 1400, 0.65))


@recipe("crew-memory", "open", "board",
        "The logbook opened, modelled: glued leather creaking, thick boards thumped flat on wood, pages riffling",
        """Modelled, close and dry: the spine creaking as the cover is lifted (glued leather flexing, stick-slip 150-260
        times a second, catching, through the spine's modes), the cover swung over (a soft whoosh of air) and thumped
        flat onto the bench (its thick boards' broad modes, 190 Hz-1.4 kHz, rung by a soft, wide contact, the leather's
        slap, the bench's boards knocked under it), and pages riffling down (edges flicking fast and thinning over a soft
        rustle of paper). The same three takes as 'leather'. 0.8-1 s, high-passed at 160 Hz.""",
        takes=3, lufs=-22)
def open_board(rng, k):
    return logbook_open(rng, k, spine_model, swing_model, cover_model, riffle_model)


# ---- crew-memory.page: one page turned ------------------------------------------------------------------------------------

# Each take is built differently, so none two in a row alike: what lifts the sheet, how it crackles, what carries it over
# and what lays it down, and how long it takes.
PAGE_TAKES = [
    # length, lift, crackle (start, length, level), sweep, land, and when the sweep starts (a share of the length)
    dict(L=0.64, lift="corner", crack=[(0.08, 0.18, -10)], sweep="swish", land="flick"),
    dict(L=0.52, lift="snap", crack=[(0.05, 0.12, -6)], sweep="brush", land="tap"),
    dict(L=0.76, lift="drag", crack=[(0.07, 0.1, -9), (0.22, 0.12, -11)], sweep="slow", land="flick-tap"),
    dict(L=0.6, lift="corner", crack=[(0.05, 0.2, -7)], sweep="stiff", land="slap", at=0.33),
]


def page_rec(rng, k):
    """One page turned, from real recordings: the sheet lifted (a real page's flick, or its snap, or a slide of paper on
    paper), crackling as it bends (a real belt's cloth crackle and a real dry rustle laid grain by grain), carried over
    (a real page's swish or sweep, slowed for heavy paper), laid down (its landing flick, or a soft tap of a book set
    down)."""
    d = PAGE_TAKES[k % 4]
    L = d["L"] * rng.uniform(0.97, 1.03)
    parts = []
    lift = {"corner": lambda: seg(FLIPS[2], 0.025, 0.05, -3 + rng.uniform(-0.5, 0.5), 0.001, 0.02),
            "snap": lambda: seg(FLIPS[1], 0.035, 0.06, -1.5 + rng.uniform(-0.5, 0.5), 0.001, 0.02),
            "drag": lambda: seg(FLIPS[1], 0.1, 0.1, -2.5, 0.02, 0.03)}[d["lift"]]()
    parts.append((0.0, unit(hp(lift, 700, 2)), -4))
    for t, Lc, db in d["crack"]:
        c = lay(rng, Lc, env([(0, 40), (Lc * 0.5, 160), (Lc, 40)], Lc), env([(0, -4), (Lc, -1)], Lc), [FIBRES, BRISTLE],
                (1200, 1500))
        parts.append((t * L / d["L"], c * env([(0, 0), (Lc * 0.3, 1), (Lc, 0)], Lc), db))
    t_sw = L * d.get("at", 0.2)
    Lsw = L * 0.55 + 0.03
    src_sw = {"swish": (FLIPS[0], 0.27, 0.29, -2), "brush": (FLIPS[1], 0.06, 0.3, -2),
              "slow": (FLIPS[0], 0.27, 0.29, -5), "stiff": (FLIPS[2], 0.05, 0.17, -3)}[d["sweep"]]
    sw = seg(src_sw[0], src_sw[1], src_sw[2], src_sw[3] + rng.uniform(-0.7, 0.7), 0.02, 0.05)
    Lsw = min(Lsw, len(sw) / SR)            # the leaf lands as its sweep ends: no hole between them
    sw = dsp.fit(sw, samples(Lsw))
    t_land = t_sw + Lsw - 0.03
    parts.append((t_sw, unit(hp(sw, 400, 2)) * env([(0, 0.2), (Lsw * 0.5, 1), (Lsw, 0.4)], Lsw), -3))
    land = d["land"]
    if "flick" in land:
        parts.append((t_land, unit(lp(seg(FLIPS[0], 0.565, 0.07, -2.5 + rng.uniform(-0.7, 0.7), 0.001, 0.03), 7000)), -2))
    if "tap" in land:
        parts.append((t_land + (0.01 if "flick" in land else 0.0), tap_rec(rng, k, -1), -4 if land == "tap" else -9))
    if land == "slap":
        parts.append((t_land, unit(lp(seg(FLIPS[1], 0.285, 0.06, -2.5, 0.001, 0.03), 7000)), -2))
        parts.append((t_land + 0.004, tap_rec(rng, k + 1, 1), -10))
    return dry(lead_in(hp(ck.place(parts, t_land + 0.25), 250, 2)))


@recipe("crew-memory", "page", "sheet",
        "A logbook page turned: dry heavy paper lifted at its corner, crackling as it bends, swept over and laid down",
        """From real recordings, close and dry: the sheet lifted at its corner (a real page's flick or snap pitched down
        for heavy paper, or a slide of paper on paper), a soft crackle as it bends (a real belt's cloth crackle and a real
        dry rustle laid grain by grain, swelling and easing), swept over (a real page's swish or sweep, slowed for heavy
        paper) and laid down flat (its landing flick, or the soft tap of a real book set down, darkened and choked). Four
        takes each built differently, so no two in a row alike: a full turn, a quick one, a slow heavy leaf that crackles
        twice, a stiff page that crackles as it's bent back and flips over to slap flat. 0.5-0.8 s.""",
        sources=FLIPS + PLACES + [FIBRES, BRISTLE], takes=4, lufs=-24)
def page_sheet(rng, k):
    return page_rec(rng, k)


def page_model(rng, k):
    """One page turned, modelled: the corner lifted (paper sliding on paper: fine stick-slip, light and bright, or a
    flick), crackling as it bends (fibres buckling: sharp ticks in paper's band), carried over (a whoosh of air and the
    sheet's flutter), laid down (a soft flat slap and its edge's flick)."""
    d = PAGE_TAKES[k % 4]
    L = d["L"] * rng.uniform(0.97, 1.03)
    parts = []
    if d["lift"] == "drag":
        Ll = 0.12
        parts.append((0.0, ck.friction(rng, Ll, rate=rng.uniform(500, 700), lo=1500, hi=8000, rough=0.5,
                                       shape=env([(0, 0.2), (Ll * 0.8, 1), (Ll, 0)], Ll)), -6))
    else:
        parts.append((0.0, flick_model(rng, (-3.0, -1.0) if d["lift"] == "corner" else (0.0, 2.0)), -5))
    for t, Lc, db in d["crack"]:
        rate = env([(0, 30), (Lc * 0.5, 240), (Lc, 30)], Lc)
        c = bp(synth.crackle(Lc, rate, rng, size=(0.0003, 0.0025), hi=900), 900, 7000)
        parts.append((t * L / d["L"], unit(c), db - 2))
    t_sw = L * d.get("at", 0.25)
    t_land = L * 0.75
    Lsw = t_land - t_sw + 0.03
    hi = {"swish": 3000, "brush": 3600, "slow": 2200, "stiff": 2600}[d["sweep"]]
    sw = ck.whoosh(rng, Lsw, 600, hi, 0.5)
    flut = synth.rustle(Lsw, env([(0, 40), (Lsw * 0.5, 200), (Lsw, 40)], Lsw), rng, f=(1500, 6000), ticks=0.0)
    parts.append((t_sw, unit(mix(unit(sw), unit(flut) * 0.35)), -4))
    land = d["land"]
    if "flick" in land or land == "slap":
        parts.append((t_land, flick_model(rng, (-4.0, -1.0)), -3))
    if "tap" in land or land == "slap":
        Lt = 0.04
        flat = bp(synth.noise(Lt, rng, "pink"), 350, 3500) * env([(0, 0), (0.002, 1), (Lt, 0)], Lt, "exp")
        parts.append((t_land + 0.004, unit(flat), -4 if land == "tap" else -8))
    return dry(lead_in(hp(ck.place(parts, L + 0.15), 250, 2)))


@recipe("crew-memory", "page", "rag",
        "A logbook page turned, modelled: heavy rag paper slid and lifted, its fibres crackling, swept over, laid flat",
        """Modelled, close and dry: the corner lifted (paper sliding on paper, fine bright stick-slip, or the corner's
        flick), a soft crackle as the sheet bends (its fibres buckling: sharp ticks in paper's band, swelling and easing),
        swept over (a whoosh of air, the sheet's flutter) and laid flat (a soft slap in the low mids and its edge's flick).
        The same four takes as 'sheet', each built differently. 0.5-0.8 s.""",
        takes=4, lufs=-24)
def page_rag(rng, k):
    return page_model(rng, k)


# ---- crew-memory.shut: the logbook clapped shut ---------------------------------------------------------------------------

def air_rec(rng, length, rising=True):
    """The air pushed out of the pages as the cover comes down: a slice of a real short burst of air, darkened and
    rising into the clap (or, after it, a slice of a real even hiss of air puffing out of the edges and gone)."""
    if rising:
        x = seg(PUFF, 0.03, 0.2, rng.uniform(-5, -3), 0.01, 0.04)
    else:
        x = seg(STEAM, rng.uniform(0.2, 1.0), length + 0.05, -3, 0.002, 0.04)
    x = dsp.fit(x, samples(length))
    shape = [(0, 0), (length * 0.85, 1), (length, 0.3)] if rising else [(0, 1), (length * 0.2, 0.6), (length, 0)]
    return unit(lp(hp(x, 400, 2), 2500, 2)) * env(shape, length, "lin")


def clap_rec(rng, take, force=1.0):
    """The cover clapped down on the block of pages, from real recordings: a real book shut (the cover's soft slap and
    the pages' air), a real book slapped down flat for the weight of the boards, a real heavy leather drop for its
    leather; high-passed at 150 Hz: dull and papery, not a boom."""
    shut = dsp.vari(hit_of(BOOK_CLOSE, 0, 0.18), rng.uniform(-2.5, -1) - 0.5 * (force - 1))
    slap = ck.choke(dsp.vari(hit_of(PLACES[(0, 2)[take % 2]], 0, 0.15), rng.uniform(-3, -2)), 0.03, 0.03)
    skin = dsp.vari(main_hit(DROP_LEATHER, 0.12), rng.uniform(0, 2))
    y = ck.place([(0, unit(shut), 0), (0.0015, unit(slap), -4 + 2 * (force - 1)), (0.001, unit(skin), -8)])
    return hp(y, SQUISH, 4)


def logbook_shut(rng, k, flatten, clap, puff, spine):
    """The logbook shut: the pages flattening, the air pushed out of them rising into the clap of the cover on the
    block, the last of the air puffing out of its edges. Takes: an even clap; a harder one, the cover bouncing once;
    a slow close, the spine creaking first."""
    v = k % 3
    t0 = (0.0, 0.0, 0.1)[v]
    Lf = (0.14, 0.1, 0.2)[v]
    force = (1.0, 1.4, 0.85)[v]
    parts = [(t0, flatten(rng, Lf, True), -9), (t0 + Lf, clap(rng, k, force), 0),
             (t0 + Lf + 0.012, flatten(rng, 0.3, False), -15)]
    if v == 1:     # the cover bounces once on the block and settles
        parts.append((t0 + Lf + rng.uniform(0.05, 0.065), clap(rng, k + 1, 0.6), -14))
    if v == 2:
        parts.append((0.0, spine(rng, 0.12, -4.0), -12))
    return dry(lead_in(ck.place(parts, t0 + Lf + 0.5)))


@recipe("crew-memory", "shut", "clap",
        "The logbook shut: the pages' air pushed out, the heavy cover clapped down on them, a dull, papery thud",
        """From real recordings, close and dry: the pages flattening as the cover comes down, the air pushed out of them
        (a slice of a real burst of air, darkened, rising into the clap), the cover clapped down on the block (a real
        book shut for the cover's soft slap and the pages' air, a real book slapped down flat for the boards' weight, a
        real heavy leather drop for its leather), and the last of the air puffing out of the edges. Takes: an even clap;
        a harder one, the cover bouncing once; a slow close with the spine creaking first. About 0.5-0.6 s. High-passed
        at 150 Hz: dull and papery, never a boom.""",
        sources=[PUFF, STEAM, BOOK_CLOSE, DROP_LEATHER, CREAK] + PLACES + LEATHER, takes=3, lufs=-21)
def shut_clap(rng, k):
    return logbook_shut(rng, k, air_rec, clap_rec, air_rec, spine_rec)


def air_model(rng, length, rising=True):
    """The air pushed out of the pages, modelled: a band of air rising into the clap (or puffing out after it)."""
    if rising:
        return unit(ck.whoosh(rng, length, 400, 2200, 0.9))
    nz = lp(hp(synth.noise(length, rng, "pink"), 500, 2), 2500, 2)
    return unit(nz * env([(0, 1), (length * 0.2, 0.6), (length, 0)], length))


def clap_model(rng, take, force=1.0):
    """The cover clapped down, modelled: its boards' broad modes rung by a wide contact, the crack of air squeezed out
    between them, the block of pages under it thudding dull (low-mid noise, a quick swell and gone); high-passed at
    150 Hz."""
    boards = ck.hollow(rng, [m * rng.uniform(0.95, 1.05) for m in COVER], 0.1, q=3, hit=0.002)
    crack = burst(rng, 0.0025, 1200, 7000, attack=0.1)
    Lb = 0.06
    block = bp(synth.noise(Lb, rng, "pink"), 250, 900) * env([(0, 0), (0.003, 1), (Lb, 0)], Lb) ** 1.5
    y = ck.place([(0, boards, -1), (0, unit(crack), -9 + 3 * (force - 1)), (0.001, unit(block), -2)])
    return hp(y, SQUISH, 4)


@recipe("crew-memory", "shut", "block",
        "The logbook shut, modelled: air squeezed out of the pages, thick boards clapped down on a dull block of paper",
        """Modelled, close and dry: the air pushed out of the pages rising into the clap (a band of air sweeping up), the
        cover clapped down (its thick boards' broad modes rung by a wide contact, the crack of the air squeezed out between
        them) on the block of pages (a dull thud, low-mid noise swelling and gone), the last of the air puffing out of the
        edges. The same three takes as 'clap'. About 0.5-0.6 s, high-passed at 150 Hz.""",
        takes=3, lufs=-21)
def shut_block(rng, k):
    return logbook_shut(rng, k, air_model, clap_model, air_model, spine_model)
