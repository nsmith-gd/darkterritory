"""K1's merged asks heard (queue #504, note 774; K1 on #1012): the mail catch at a lineside crane (note 727), a comet
fragment's hum (note 718) and the speaking tube's whistle-call (note 720), as tools/audio/cues.py lists them and
GameAudio.Mail.cs and VoiceChat.cs play them.

What each is decides the build:
- The mail catch: a crewmate in a car's open side door holds the catcher arm out and snags the bag off the crane's arm
  as the train runs by. Caught, it's three things inside a second, close at the door: the canvas struck hard at speed (a
  flat thwack with a crack in it, never a pillow), the iron arm clanking as it takes the bag and swings onto its stop,
  rattling on its pin, and the bag's weight slapped against the car's side. Hooked over the crane's posted speed it
  tears: the hook bites, the canvas rips open in one go, the bag's torn off and gone, the empty arm clangs back and the
  letters go fluttering off down the train in its wind. The weight is in the low mids: every take keeps its energy out
  of the squish under 150 Hz (the director's rule), the slaps high-passed, nothing a boom.
- A comet fragment: a lump of black slag split by sick green crystal, glowing, lying at a stop or carried aboard. Its
  hum is held near it: low and wrong, felt in the teeth, two close tones beating slowly against each other in 80-200 Hz
  with their pitch never quite still, and over them, very quiet, the crystal's own shimmer and a fizz (2-6 kHz). Not a
  creature (no throat, no breath) and not a machine (no mains hum's even harmonics, no motor's beat): a thing that
  shouldn't sound. A seamless 8 s loop, exactly one cycle (built periodic: the tones whole cycles round it, the grains
  placed round it, the room a circular one), so build.py's seam gives the cycle back.
- The speaking tube's call: when a voice comes down the tube after a quiet, the whistle-plug in the far end's horn
  shrills first, as someone blows down the mouthpiece. A small brass whistle, thin, breathy and a little unsteady, the
  breath's pressure in it (scooping up into its note, wobbling, sagging as it runs out), heard out of the horn: through
  a long brass pipe, hollow and tinny, held to what the pipe passes and ringing at its note (600 Hz-2 kHz, +8 dB at
  1150 Hz, as content/audio/sounds/voice-tube.json holds the voice). Never the engine's three-chime whistle, never a
  guard's pea whistle.

Each cue has two candidates. The first is kitbashed from the packs' real recordings, bent: a real leather drop and books
slapped flat for the canvas, a real blow on steel, a real iron ring, latches and a loose-metal rattle for the arm, a
real punch and a plank's knock for the car's side, real tearing drags and cloth crackling laid grain by grain for the
rip, real page turns for the letters and a real car rushing past for the wind; a real glass's ring, whose two doublets
beat, dropped five octaves for the hum, that glass and another at their own pitch for its shimmer and real glass
breaking cut into grains for its fizz; a real glass ringing almost pure, held out into a breathy whine and blown along
a breath, for the whistle. The second is modelled: struck modes for the arm, the canvas and the car's side, threads
breaking at the weave's rate for the rip, paper flapping in fast air, sine tones beating with a stretched octave over
them, an edge tone locked to a small chamber.
"""

import functools

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import kit
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.beasts import main_hit, unit
from recipes.crew_items import hit_of
from recipes.g1_ground import far, wander
from recipes.g1_train import cband
from recipes.gannet_tells import bend
from recipes.house_beasts import close, held, knocks, pitches, rattle, strand
from recipes.house_folk import core, drawn
from recipes.mishaps_body import flap
from recipes.moose_tells import burst, ring
from recipes.stress import strongest

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

LEATHER = R("dropLeather")[0]                    # a real heavy leather drop: a full bag's flat slap
SLAPS = R("bookPlace1", "bookPlace2", "bookPlace3")   # a real book slapped down flat: the canvas's hard face on iron
SNAPS = R("cloth2", "cloth4")                    # the brightest of the packs' cloth handlings: canvas cracking taut
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4")
CLANK = "sfx_100_v2:metal_hit_01"                # a real blow on a steel sheet: the catcher arm struck
RING = K("impactMetal_medium")                   # medium iron struck: the arm's short ring, 600 Hz-2 kHz
LATCHES = S("door_02", "door_04", "door_05")     # real latches dropping: iron into iron, the arm onto its stop
RATTLE = S("metal_03")                           # a real loose-metal rattle: the arm's pin in its eye
PUNCH = K("impactPunch_medium")                  # a real punch landing: the bag's weight
PLANK = K("impactPlank_medium")                  # a plank's hollow knock: the car's side
RUSH = "sfx_100_v2:misc_37"                      # a car rushing past: the train's wind at the open door
TEARS = R("handleSmallLeather2", "drawKnife2")   # real tearing drags: a leather strap's, a blade drawn from its sheath
CRINKLE = R("clothBelt2")[0]                     # a belt's cloth crackling: fibres giving
PAGES = R("bookFlip1", "bookFlip2", "bookFlip3")  # real pages turned and riffled: the letters
SWISH = R("bookFlip1")[0]                        # its swish (0.27-0.56 s): a leaf carried through the air
GLASS = "sfx_100_v2:glass_05"                    # a real glass struck: two doublets, 3.59/3.63 and 3.84/3.88 kHz
GLASS_B = "sfx_100_v2:glass_02"                  # another, its doublet at 2.21/2.24 kHz and a partial at 3.7 kHz
SHARDS = S("misc_26", "misc_27")                 # real glass breaking and shattering: cut to grains for the fizz
PURE = "sfx_100_v2:glass_01"                     # a real glass ringing almost pure: 2.76 kHz, a twin 25 Hz under it
GASP = "sfx_100_v2:misc_05"                      # a real gasp: air forced through a mouth

# ---- Bands and bodies ---------------------------------------------------------------------------------------------------

SQUISH = 150.0              # the director's line: an impact keeps most of its energy above it
CANVAS = [210, 360, 620, 980]                    # a full mailbag's canvas, stiff with its load: broad, dull modes
ARM_F = 230.0               # the catcher arm's first bending mode (Hz): forged iron, a metre long
PIN = [1150, 1900, 3100, 4300]                   # the arm's pin knocking in its eye: small and hard
SIDE = [150, 205, 310, 520, 870, 1340]           # a car's side: its frame's low modes, then its deal boards'

HUM_L = 8.0                 # the hum's cycle (s)
DROP = -60.0                # the hum's glass dropped five octaves: its 3.6 kHz doublet to 112/113 Hz
HUM = (80, 200)             # where the hum's two tones sit
GLINT = (2000, 6000)        # its shimmer and fizz, very quiet over it

TUBE = (600, 2000)          # what a brass pipe the train's length passes (content/audio/sounds/voice-tube.json)
PIPE = 1150.0               # the pipe's note, where it rings
SOUND = 343.0               # m/s, for the pipe's length answering


# ---- crew-mail.catch: the bag snatched into the catcher arm ---------------------------------------------------------------

def thwack_rec(rng, take, force=1.0):
    """The canvas struck at speed, from real recordings: a real heavy leather drop for the full bag's flat slap, dropped a
    few semitones for its weight; a real book slapped down flat for the canvas's hard face meeting iron; the brightest
    of the packs' cloth handlings for the canvas cracking taut round the jaw."""
    body = unit(hp(dsp.vari(main_hit(LEATHER, 0.25), rng.uniform(-4, -2)), SQUISH, 4))
    face = unit(dsp.vari(main_hit(SLAPS[take % 3], 0.15), rng.uniform(-3, -1)))
    snap = unit(hp(dsp.vari(hit_of(SNAPS[take % 2], int(rng.integers(2)), 0.12), rng.uniform(-3, 0)), 700))
    return ck.place([(0, body, 0), (0.001, face, -1), (0.002, snap, -8 + 3 * np.log2(force))])


def clank_rec(rng, take, free=False):
    """The catcher arm struck, from real iron: a real blow on a steel sheet dropped for a forged arm and a real iron ring
    under it, both choked short by the bag in its jaw (or let ring on, empty, when the bag's torn off it)."""
    tau = 0.14 if free else 0.05
    blow = ck.choke(dsp.vari(main_hit(CLANK, 0.4), rng.uniform(-4, -2)), 0.05, tau)
    rung = ck.choke(dsp.vari(hit_of(RING[take % 5], 0, 0.3), rng.uniform(-3, 0)), 0.04, tau * 1.2)
    return ck.place([(0, unit(hp(blow, 250)), 0), (0.001, unit(rung), -5)])


def stop_rec(rng, take, length=0.16):
    """The arm swung onto its stop and rattling on its pin, from real iron: a real latch dropping, pitched down for a
    heavier piece, and a real loose-metal rattle and latches cut to their first hundredths, quick and dying away."""
    edge = dsp.vari(hit_of(LATCHES[take % 3], 0, 0.15), rng.uniform(-6, -3))
    shake = rattle(rng, length, env([(0, 32), (length, 7)], length), RATTLE + LATCHES, (-6, -2), band=(400, 5000))
    return ck.place([(0, unit(edge), 0), (0.008, unit(shake), -8)])


def side_rec(rng, take):
    """The bag's weight swung round against the car's side, from real recordings: a real punch landing for its mass, a
    plank's hollow knock for the boards it lands on, the leather's slap again; high-passed at 150 Hz, so it lands as
    weight, not a boom."""
    mass = dsp.vari(main_hit(PUNCH[take % 5], 0.2), rng.uniform(-2, 0))
    board = ck.choke(dsp.vari(hit_of(PLANK[(take + 2) % 5], 0, 0.3), rng.uniform(-1, 2)), 0.04, 0.06)
    slap = dsp.vari(main_hit(LEATHER, 0.2), rng.uniform(-1, 1))
    y = ck.place([(0, unit(mass), -2), (0.002, unit(board), -3), (0.001, unit(slap), -4)])
    return unit(hp(y, SQUISH, 4))


@functools.lru_cache(maxsize=1)
def _leaves():
    """Every flick in the packs' real page turns, cut to its first 50 ms: a letter's edge snapping."""
    out = []
    for key in PAGES:
        x = ck.get(key)
        for s, _ in ck.hits(x, floor_db=-30, gap=0.02, prom=5):
            out.append(unit(hp(ck.cut(x, s - samples(0.001), s + samples(0.05), 0.0005, 0.02), 900)))
    return out


def leaf_rec(rng, semis=(-1.0, 5.0)):
    """One letter's edge flicking: a real page's flick, pitched for a letter or an envelope."""
    g = _leaves()
    return unit(dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis)))


def letters_rec(rng, length=0.18):
    """The letters inside shifting as the bag lands: a few real page flicks, quick, close and small."""
    b = Bus(length + 0.1)
    t = 0.0
    while t < length:
        b.at(t, leaf_rec(rng, (2.0, 6.0)), rng.uniform(-10, 0))
        t += rng.uniform(0.015, 0.05)
    return b.x


def wind_rec(rng, length):
    """The train's wind at the open door: a slice of a real car rushing past, its rumble taken off."""
    x = ck.get(RUSH)
    a = int(rng.integers(samples(0.4), len(x) - samples(length) - 1))
    y = unit(hp(x[a:a + samples(length)], 300, 2))
    return y * dsp.fit(env([(0, 0), (0.06, 1), (length * 0.6, 0.8), (length, 0)], length), len(y))


def at_door(x, rng, length):
    """At the open door of a moving car: a slap off the train's own side and a breath of the night (world_kit.space), cut
    to `length` and faded, so a take ends when the catch does; the squish under 150 Hz kept down."""
    y = dsp.fit(W.space(x, rng, "night", wet=0.08), samples(length))
    return hp(dsp.fade(y, 0.0, min(0.15, length * 0.25)), 120, 4)


def catch(rng, k, thwack, clank, stop, side, letters, wind):
    """Three catches: a clean one (the bag hit, the arm's clank, the arm onto its stop and rattling, the bag's weight
    swung against the side); one where the bag swings back off the side and slaps it again; one taken harder, the arm
    rattling longer on its pin."""
    hard = k % 3 == 2
    L = (0.62, 0.8, 0.72)[k % 3] * rng.uniform(0.96, 1.04)
    t_stop = rng.uniform(0.05, 0.08)
    t_side = t_stop + rng.uniform(0.07, 0.11)
    parts = [(0.0, thwack(rng, k, 1.4 if hard else 1.0), 0), (0.002, clank(rng, k), -1 if hard else -3),
             (t_stop, stop(rng, k, 0.28 if hard else 0.16), -9), (t_side, side(rng, k), -2),
             (t_side + 0.008, letters(rng), -17), (0.0, wind(rng, L), -24)]
    if k % 3 == 1:
        parts.append((t_side + rng.uniform(0.16, 0.22), side(rng, k + 1), -11))
    return at_door(ck.place(parts, L + 0.3), rng, L)


@recipe("crew-mail", "catch", "sack",
        "The bag snatched into the catcher arm at speed: a hard canvas thwack, the iron arm's clank and rattle, its weight",
        """From real recordings, close at the open door: the canvas struck at speed (a real heavy leather drop for the full
        bag's flat slap, dropped for its weight, a real book slapped down flat for the hard face of it meeting iron, a real
        cloth snapped taut for the crack as it folds round the jaw); the catcher arm taking it (a real blow on a steel
        sheet dropped for a forged arm and a real iron ring, choked short by the bag in its jaw), swung onto its stop (a
        real latch dropped) and rattling on its pin (a real loose-metal rattle, cut to its first hundredths); a moment
        later the bag's weight slapped against the car's side (a real punch for its mass, a plank's hollow knock, the
        leather's slap again), the letters shifting inside (real page flicks); the train's wind under it (a real car rushing
        past). Takes: a clean catch; the bag swinging back off the side and slapping it again; one taken harder, the arm
        rattling longer. 0.6-0.8 s. The squish under 150 Hz cut away: weight, not a boom.""",
        sources=[LEATHER, CLANK, RUSH] + SLAPS + SNAPS + RING + LATCHES + RATTLE + PUNCH + PLANK + PAGES, takes=3, lufs=-20)
def catch_sack(rng, k):
    return catch(rng, k, thwack_rec, clank_rec, stop_rec, side_rec, letters_rec, wind_rec)


def thwack_model(rng, take, force=1.0):
    """The canvas struck at speed, modelled: heavy canvas slapped flat over the jaw (a few milliseconds of noise through a
    stiff, loaded sack's broad modes), its crack as it folds taut, the dull weight of the bundle in it."""
    sack = ring(burst(rng, 0.004, attack=0.15), [f * rng.uniform(0.92, 1.08) for f in CANVAS], q=3, tail=0.08)
    crack = burst(rng, 0.0025, 1500, 9000, attack=0.1)
    L = 0.06
    weight = bp(synth.noise(L, rng, "pink"), 160, 650) * env([(0, 0), (0.002, 1), (L, 0)], L)
    return ck.place([(0, unit(sack), 0), (0, unit(crack), -7 + 3 * np.log2(force)), (0, unit(weight), -3)])


def clank_model(rng, take, free=False):
    """The catcher arm struck, modelled: a forged iron arm's bending modes (a free bar's, the first near 230 Hz) through
    half a millisecond of contact, damped short by the bag in its jaw (or, empty, clanging brighter and ringing on a
    moment till it settles on its stop), the crack of the blow on its front."""
    x = W.body(rng, ARM_F * rng.uniform(0.92, 1.08), W.BAR, decay=0.3 if free else 0.2, damp=1.0, contact=0.0005,
               spread=0.02, tilt=0.15 if free else 0.3)
    x = ck.choke(x, 0.12, 0.06) if free else ck.choke(x, 0.035, 0.05)
    crack = burst(rng, 0.002, 2000, 9000, attack=0.1)
    return unit(hp(ck.place([(0, unit(x), 0), (0, unit(crack), -8)]), 300))    # its first mode under: a clank, not a bonk


def stop_model(rng, take, length=0.16):
    """The arm swung onto its stop and rattling on its pin, modelled: the arm knocked against the stop near its end (its
    upper modes), and the pin knocking in its eye, quick ticks through small hard modes, dying away."""
    edge = W.body(rng, ARM_F * 1.8, W.BAR, decay=0.08, contact=0.0003, count=4)
    shake = knocks(rng, length, env([(0, 32), (length, 7)], length), PIN, q=6, tail=0.02)
    return ck.place([(0, unit(hp(edge, 300)), 0), (0.008, unit(shake), -8)])


def side_model(rng, take):
    """The bag's weight against the car's side, modelled: the side's frame and boards knocked through a soft, broad
    contact (a full bag lands over a hand's breadth, so only the low modes speak), the canvas slapping flat on them, the
    bundle inside thumping; high-passed at 150 Hz, weight, not a boom."""
    boards = ck.hollow(rng, SIDE, 0.16, q=4, hit=0.006)
    slap = kit.slap(rng, 0.07, 2000, wet=0.0)
    pad = ck.pad(rng, 0.08, 700)
    return unit(hp(ck.place([(0, boards, -1), (0, unit(slap), -5), (0, pad, -3)]), SQUISH, 4))


def letters_model(rng, length=0.18):
    """The letters inside shifting, modelled: paper rustling, tiny scrapes in paper's band and a few stiff ticks."""
    return unit(synth.rustle(length, env([(0, 900), (length, 60)], length, "exp"), rng, f=(2000, 9000), ticks=0.5))


def wind_model(rng, length):
    """The train's wind at the open door, modelled: broad moving air, its rumble off, buffeting a little."""
    n = samples(length)
    y = lp(hp(synth.noise(length, rng, "pink"), 300, 2), 2500, 2)
    buffet = np.clip(1 + 0.3 * wander(rng, n, 6, 1.0), 0.3, None)
    return unit(y * buffet) * dsp.fit(env([(0, 0), (0.06, 1), (length * 0.6, 0.8), (length, 0)], length), n)


@recipe("crew-mail", "catch", "arm",
        "The bag snatched into the catcher arm, modelled: canvas slapped flat on iron, the arm's modes choked, its pin rattling",
        """Modelled, close at the open door: heavy canvas slapped flat over the jaw at speed (a few milliseconds of noise
        through a stiff, loaded sack's broad modes, 210 Hz-1 kHz, with its crack as it folds taut and the bundle's dull
        weight); the catcher arm struck (a forged iron arm's bending modes, a free bar's from 230 Hz, through half a
        millisecond of contact, choked short by the bag in its jaw), swung onto its stop (its upper modes knocked) and its
        pin rattling in its eye (quick ticks through small hard modes, dying away); the bag's weight against the car's side
        (its frame and deal boards' low modes through a soft, broad contact, the canvas slapping flat, the bundle
        thumping), paper rustling inside, the train's wind under it. The same three takes as 'sack'. 0.6-0.8 s, the
        squish under 150 Hz cut away.""",
        takes=3, lufs=-20)
def catch_arm(rng, k):
    return catch(rng, k, thwack_model, clank_model, stop_model, side_model, letters_model, wind_model)


# ---- crew-mail.tear: hooked too fast, the bag torn off and gone -----------------------------------------------------------

@functools.lru_cache(maxsize=8)
def _loudest(key, cut_at):
    """The loudest 0.2 s of a real recording, high-passed at `cut_at`: the texture a tear is laid from."""
    return hp(core(key, 0.2), cut_at, 2)


def lay(rng, length, rate, semis, keys, cuts):
    """Real recordings laid grain by grain: Hann-windowed grains (20-35 ms) from anywhere in the loudest part of one of
    `keys` (each high-passed at its `cuts`), `rate` a second (a curve), each pitched along `semis` (a curve), so a
    texture runs as long and as fast as it's made to."""
    n = samples(length)
    rc, sc = synth.curve(rate, n), synth.curve(semis, n)
    b = Bus(length + 0.1)
    t = 0.0
    while t < length:
        i = min(samples(t), n - 1)
        j = int(rng.integers(len(keys)))
        x = dsp.vari(_loudest(keys[j], cuts[j]), float(sc[i]) + rng.uniform(-0.7, 0.7))
        m = min(samples(rng.uniform(0.02, 0.035)), len(x))
        a = int(rng.integers(0, max(1, len(x) - m)))
        b.at(t, x[a:a + m] * np.hanning(m).astype(np.float32), 20 * np.log10(rng.uniform(0.5, 1.0)))
        t += rng.uniform(0.6, 1.4) / max(float(rc[i]), 1.0)
    return unit(b.x[:n])


def rip_rec(rng, length, take):
    """Heavy canvas ripping open in one go, from real recordings: the packs' tearing drags (a leather strap's, a blade
    drawn from its sheath: their zipper-like rasp) laid grain by grain over the tear's length, dropped a few semitones for
    a coarse, heavy weave; the canvas shaking as it splits under them (the packs' real cloth handlings laid the same way,
    slower and low); a belt's real cloth crackle over it for the fibres giving. It jerks in, runs faster, higher and
    louder as the bag gives, and stops dead as it parts."""
    n = samples(length)
    pull = env([(0, 0.5), (0.012, 1.0), (length * 0.7, 0.9), (length - 0.012, 1.0), (length, 0.0)], length)
    rasp = lay(rng, length, env([(0, 40), (length, 120)], length), env([(0, -7), (length, -3)], length), TEARS, (250, 400))
    shake = lay(rng, length, env([(0, 20), (length, 45)], length), env([(0, -4), (length, -1)], length), CLOTH,
                (150,) * 4)
    c = hp(ck.get(CRINKLE), 1500)
    a = int(rng.integers(0, max(1, len(c) - n)))
    y = mix(rasp, shake * 0.5, unit(dsp.fit(c[a:], n)) * 0.3, length=n)
    return unit(y * pull)


def snap_rec(rng, take):
    """The canvas parting and the bag gone off the hook: a real cloth snapped taut, its crack."""
    return unit(hp(dsp.vari(hit_of(SNAPS[(take + 1) % 2], int(rng.integers(2)), 0.1), rng.uniform(-2, 1)), 600))


def flutter_rec(rng, length, metres):
    """One letter carried off in the wind, from real paper: a run of real page flicks at a sheet's flutter (20-35 a second,
    uneven), with a real page's swish through the air under them, going `metres` off and darker as it goes."""
    b = Bus(length + 0.1)
    t, rate = 0.0, rng.uniform(20, 35)
    while t < length:
        b.at(t, leaf_rec(rng, (0.0, 6.0)), rng.uniform(-9, 0) - 8 * t / length)
        t += rng.uniform(0.6, 1.4) / rate
    sw = dsp.vari(dsp.trim(ck.get(SWISH), 0.27, 0.3), rng.uniform(1, 5))
    b.at(0.0, unit(hp(sw, 900)) * dsp.fit(env([(0, 0), (0.05, 1), (0.25, 0)], 0.25), len(sw)), -6)
    return far(b.x, metres)


def bag_rec(rng, take):
    """The torn bag tumbling away down the train: two or three real cloth flaps, dropped for heavy canvas, each farther."""
    parts = [(i * rng.uniform(0.08, 0.13), far(unit(dsp.vari(hit_of(CLOTH[(take + i) % 4], i, 0.15), rng.uniform(-4, -1))),
                                                6 + 10 * i), -2 * i) for i in range(int(rng.integers(2, 4)))]
    return ck.place(parts)


def flurry(rng, k, length, leaf):
    """The letters let go into the train's wind: a burst of them as the bag splits, thinning, each carried off on its own
    and farther and darker than the last."""
    count = (6, 8, 11)[k % 3]
    parts = []
    for i in range(count):
        t = length * 0.6 * (i / count) ** 1.4 + rng.uniform(0, 0.03)
        parts.append((t, leaf(rng, rng.uniform(0.12, 0.3), 3 + 30 * t / length + rng.uniform(0, 6)), rng.uniform(-6, 0)))
    return ck.place(parts)


def tear(rng, k, thwack, clank, stop, rip, snap, leaf, bag, wind):
    """Three tears: one long rip and the letters gone; a rip that catches a moment and goes on; a quick one, the bag gone
    at once and more letters with it. The hook bites, the canvas rips, the bag goes, the empty arm clangs back and rings,
    the letters flutter away in the wind."""
    rips = ([(0.015, 0.4)], [(0.015, 0.14), (0.2, 0.24)], [(0.01, 0.24)])[k % 3]
    free = rips[-1][0] + rips[-1][1]
    Lf = rng.uniform(0.75, 0.9)
    L = free + Lf
    parts = [(0.0, thwack(rng, k, 0.8), -2), (0.002, clank(rng, k), -4)]
    parts += [(t, rip(rng, d, k), 0) for t, d in rips]
    parts += [(free, snap(rng, k), -3), (free + 0.02, clank(rng, k + 1, True), -7),
              (free + 0.06, stop(rng, k + 1, 0.2), -13),
              (free - 0.04, flurry(rng, k, Lf, leaf), -10), (free + 0.01, bag(rng, k), -10),
              (0.0, wind(rng, L), -21), (free - 0.05, wind(rng, Lf * 0.8), -17)]
    return at_door(ck.place(parts, L + 0.3), rng, L)


@recipe("crew-mail", "tear", "rip",
        "Hooked too fast: the canvas ripping open in one tear, the bag torn off the hook, letters fluttering off",
        """From real recordings: the hook bites (the canvas struck and the arm's clank, as in 'sack'), then the canvas rips
        open in one violent tear: the packs' real tearing drags (a leather strap's, a blade drawn from its sheath) laid
        grain by grain over the tear's length, dropped for a coarse, heavy weave, jerking in and running faster, higher and
        louder as the bag gives, the canvas shaking under it (real cloth handlings laid the same way, slower and low) and a
        belt's real cloth crackle in it for the fibres; it stops dead as the bag parts (a real cloth's snap), the empty arm
        clangs back and rings on (a real blow on steel and an iron ring, let ring) and rattles on its pin, and the bag
        tumbles away (real cloth flaps, farther each time). The letters go fluttering off down the train in its wind: each
        a run of real page flicks at a sheet's flutter with a real page's swish under it, farther and darker as it goes;
        the wind rising as the bag bursts (a real car rushing past). Takes: one long rip; a rip that catches and goes on; a
        quick one with more letters. 1-1.3 s, the squish under 150 Hz cut away.""",
        sources=[LEATHER, CLANK, RUSH, CRINKLE] + SLAPS + CLOTH + RING + LATCHES + RATTLE + TEARS + PAGES,
        takes=3, lufs=-21)
def tear_rip(rng, k):
    return tear(rng, k, thwack_rec, clank_rec, stop_rec, rip_rec, snap_rec, flutter_rec, bag_rec, wind_rec)


def rip_model(rng, length, take, rate=320.0):
    """Heavy canvas ripping open, modelled: threads breaking one after another as the tear runs, so fast they fuse into a
    rasp at the weave's rate (heard_foley.rip), but a coarse, heavy weave's: thicker threads, so each break a longer,
    lower tick and fewer of them a second (250-650 as the pull comes on), a few thick ones cracking; the weave creaking
    under the strain, the torn flap thrashing behind the tear. It jerks in and stops dead as the bag parts."""
    n = samples(length)
    pull = env([(0, 0.4), (0.012, 1.0), (length * 0.7, rng.uniform(0.8, 0.95)), (length - 0.012, 1.0), (length, 0.0)],
               length)
    x = np.zeros(n, np.float32)
    t = 0.0
    while True:
        p = float(pull[min(samples(t), n - 1)])
        t += max(0.3, 1 + 0.35 * rng.standard_normal()) / (rate * (0.6 + 1.4 * p * min(1.0, 0.5 + t / length)))
        a = samples(t)
        if a >= n:
            break
        x[a] = rng.uniform(0.25, 1.0) * p * (3.0 if rng.random() < 0.04 else 1.0)
    k = samples(0.0025)
    tick = (rng.standard_normal(k) * np.exp(-np.linspace(0, 6, k))).astype(np.float32)
    threads = bp(np.convolve(x, tick)[:n].astype(np.float32), 500, 7000)
    flapx = synth.rustle(length, 220 * pull + 40, rng, f=(500, 3000), ticks=0.0) * pull
    creak = ck.friction(rng, length, 140, 400, 3000, 1.2, pull)
    return unit(mix(unit(threads), unit(flapx) * 0.3, creak * 0.08))


def snap_model(rng, take):
    """The canvas parting, modelled: a sharp crack of the last threads going at once, the cloth's flap after it."""
    return ck.place([(0, unit(burst(rng, 0.003, 1200, 8000, attack=0.1)), 0),
                     (0.002, unit(bp(synth.noise(0.05, rng, "pink"), 400, 3000)) * env([(0, 1), (0.05, 0)], 0.05), -6)])


def flutter_model(rng, length, metres):
    """One letter carried off in the wind, modelled: the snap of its edge as it's whipped away, then a sheet of paper
    flapping in fast air (noise in paper's band chopped 25-45 times a second, catching and slackening in gusts), going
    `metres` off and darker as it goes."""
    y = flap(rng, length, rng.uniform(25, 45), lo=1200, hi=7000, sharp=2.5, gusts=4.0)
    y = y * env([(0, 0), (0.01, 1), (length, 0.15)], length)
    return far(ck.place([(0, unit(burst(rng, 0.003, 2500, 9000, attack=0.1)), -4), (0.002, y, 0)]), metres)


def bag_model(rng, take):
    """The torn bag tumbling away, modelled: heavy canvas flapping slow in the wind, going."""
    L = 0.35
    y = flap(rng, L, rng.uniform(12, 16), lo=250, hi=2200, sharp=3.0) * env([(0, 1), (L, 0)], L)
    return far(y, 10)


@recipe("crew-mail", "tear", "threads",
        "Hooked too fast, modelled: canvas threads breaking in one rasping tear, the empty arm clanging, paper flapping",
        """Modelled: the hook bites (canvas slapped flat on the jaw, the arm's modes choked, as in 'arm'), then the canvas
        rips: threads breaking one after another so fast they fuse into a rasp at the weave's rate, a coarse, heavy weave's
        (each break a longer, lower tick, 250-650 a second as the pull comes on, a few thick threads cracking), the weave
        creaking under the strain and the torn flap thrashing behind; it stops dead as the bag parts (a crack of the last
        threads), the empty arm clangs back and rings on (its modes let ring) and rattles on its pin, the bag tumbles away
        (heavy canvas flapping slow). The letters fly off down the train: each the snap of its edge and then a sheet of
        paper flapping in fast air (noise in paper's band chopped 25-45 times a second, in gusts), farther and darker as
        it goes, over the train's wind. The same three takes as 'rip'. 1.1-1.3 s, the squish under 150 Hz cut away.""",
        takes=3, lufs=-21)
def tear_threads(rng, k):
    return tear(rng, k, thwack_model, clank_model, stop_model, rip_model, snap_model, flutter_model, bag_model, wind_model)


# ---- world-comet.hum: a comet fragment, glowing ---------------------------------------------------------------------------

@functools.lru_cache(maxsize=4)
def _dropped(key, semis):
    """A real recording varispeeded by `semis` (five octaves down is 32 times as long)."""
    return dsp.vari(ck.get(key), semis)


def still(rng, key, semis, n, sharpen=True, weight=None):
    """A real ring held still for a loop of n samples: varispeeded by `semis`, and the stretch of it from just after the
    strike, as long as the loop, taken apart into its partials and put back once with fresh phases (a one-frame spectral
    freeze: stress.freeze's overlapping frames make each partial flutter, one frame keeps it still), exactly periodic
    over n. `sharpen` gathers each partial (a peak standing 10 dB clear of its skirts) into its loudest bin, so it holds as
    a steady tone and partials a fraction of a hertz apart beat as they did when the glass was struck, the rest kept 30
    dB down for a breath of the glass's own noise; without it each partial is a narrow band of noise, a ring that won't
    settle. `weight(f)` an EQ on the way."""
    x = _dropped(key, semis)
    top = int(np.argmax(np.abs(x)))
    seg = dsp.fit(x[top + samples(0.005):], n)
    p = np.abs(np.fft.rfft(seg * np.hanning(n))) ** 2
    if weight is not None:
        p = p * weight(np.maximum(np.fft.rfftfreq(n, 1 / SR), 1e-3)) ** 2
    if sharpen:
        db = 10 * np.log10(p / np.max(p) + 1e-20)
        keep = np.zeros_like(p)
        for i in signal.find_peaks(db, height=-45, prominence=10, distance=6)[0]:
            keep[i] = np.sum(p[max(0, i - 3):i + 4])
        p = keep + p * 1e-3
    y = np.fft.irfft(np.sqrt(p) * np.exp(2j * np.pi * rng.random(len(p))), n)
    return W.norm(y)


def waver(x, cents, rate, rng):
    """A loop's pitch never quite still: read back along a time-warp whose speed wanders `cents` (rms), nothing faster
    than `rate` Hz, the warp coming back to where it started each cycle, so the loop stays a loop."""
    n = len(x)
    dev = W.slow(n, rate, rng) * (2 ** (cents / 1200) - 1)
    pos = np.arange(n) + np.cumsum(dev - np.mean(dev))
    return np.interp(pos % n, np.arange(n + 1), np.append(x, x[0])).astype(np.float32)


@functools.lru_cache(maxsize=1)
def _shards():
    """Every crack in the packs' real glass breaking, cut to its first 6 ms: a splinter of crystal."""
    out = []
    for key in SHARDS:
        x = ck.get(key)
        for s, _ in ck.hits(x, floor_db=-36, gap=0.004, prom=4):
            out.append(unit(hp(ck.cut(x, s, s + samples(0.006), 0.0003, 0.003), 2500)))
    return out


def fizz_rec(rng, n, rate=70.0):
    """A fizz in the crystal: grains of real glass breaking, a few milliseconds each and pitched up, scattered `rate` a
    second, a few loud and many faint, coming round the loop."""
    g = _shards()
    ev = [(t, unit(dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(2, 8))), min(1.0, 0.05 * rng.pareto(1.6) + 0.05))
          for t in W.poisson(n / SR, rate, rng)]
    return W.place(n, ev)


def glint(n, rng, rate=3.0):
    """The crystal's light catching: a level that flickers up and dies back unevenly a few times a second, periodic."""
    return (0.25 + np.clip(W.slow(n, rate, rng), 0, None) ** 2).astype(np.float32)


def upper_down(f):
    """The glass's upper doublet (120/121 Hz, dropped) 8 dB down, so the slow beat of the lower one leads and the upper
    only roughens it."""
    return np.where((f > 117) & (f < 125), 0.4, 1.0)


def rms(x):
    return float(np.sqrt(np.mean(np.square(x))) + 1e-12)


def hum_mix(rng, tone, shimmer, fizz, n, shine=-35.0, fizzing=-37.0):
    """The hum with its shimmer and fizz over it, very quiet: each held to its band circularly and set `shine` and
    `fizzing` dB (rms) under the hum, about 27 dB under it as the loudness meter weighs them; a little of the open night
    round it (circular), levelled round three cycles and handed over seamless (house_beasts.close)."""
    s, z = cband(shimmer, *GLINT), cband(fizz, *GLINT)
    y = tone / rms(tone) + s / rms(s) * dsp.db2a(shine) + z / rms(z) * dsp.db2a(fizzing)
    return close(y, rng, None, wet=0.06, lufs=-26, room="night")


@recipe("world-comet", "hum", "glass",
        "A comet fragment: a real glass's ring dropped five octaves into a low, wrong hum, its doublets beating, a faint fizz",
        """From real glass: a glass struck in the packs (sfx_100 glass_05) rings in two doublets, two modes a few tens of
        hertz apart at 3.59/3.63 and 3.84/3.88 kHz. Dropped five octaves they are the hum: 112 and 113 Hz beating slowly
        (a little over once a second), 120 and 121 Hz, taken 8 dB down, grinding against them eight times a second (felt
        in the teeth), the glass's upper partials at 230-250 Hz faint over that, inharmonic, so no chord and nothing like
        mains hum. Held still for the loop (the ring after the strike taken apart into its partials and put back, each a
        steady tone) and its pitch wavering a few cents, never quite still. Over it, very quiet: the same glass and another
        at their own pitches, 2.2-3.9 kHz, each partial a narrow band of noise (a ring that won't settle), glinting a few
        times a second; and a fizz of grains of real glass breaking, a few milliseconds each, pitched up. A breath of the
        open night round it. 8 s exact cycle.""",
        sources=[GLASS, GLASS_B] + SHARDS, takes=1, loop=True, band=HUM, lufs=-26, seconds=HUM_L)
def hum_glass(rng, k):
    n = samples(HUM_L)
    tone = waver(still(rng, GLASS, DROP, n, weight=upper_down), 8, 0.3, rng)
    shimmer = mix(still(rng, GLASS, 0.0, n, sharpen=False), still(rng, GLASS_B, 2.0, n, sharpen=False) * 0.7) * glint(n, rng)
    return hum_mix(rng, tone, shimmer, fizz_rec(rng, n), n)


def sines(n, rng, freqs, cents, rate, harm=0.0, level=0.0, amps=None):
    """Near-sine tones whole cycles round a loop of n samples: each pitch drifting `cents` (rms) on its own, nothing
    faster than `rate` Hz, a faint second harmonic (`harm`) and its level (`amps`) wandering by `level`. The phase is
    summed in double precision from the first sample, so the cycle closes exactly (summed in single precision over eight
    seconds it drifts by a large part of a cycle, and the seam clicks)."""
    out = np.zeros(n)
    for f, a in zip(freqs, amps or [1.0] * len(freqs)):
        fi = f * 2 ** (cents / 1200 * W.slow(n, rate, rng).astype(np.float64))
        fi *= round(fi.sum() / SR) / (fi.sum() / SR)
        ph = 2 * np.pi * (np.cumsum(fi) - fi[0]) / SR + rng.uniform(0, 2 * np.pi)
        lvl = a * (1 + level * W.slow(n, 0.5, rng))
        out += (np.sin(ph) + harm * np.sin(2 * ph + 0.7)) * lvl
    return out.astype(np.float32)


def fizz_model(rng, n, rate=70.0):
    """A fizz in the crystal, modelled: tiny hard ticks rung through little glassy shells (2.5-7 kHz), `rate` a second, a
    few loud and many faint, coming round the loop."""
    ev = [(t, unit(synth.click(rng.uniform(2500, 7000), q=rng.uniform(15, 40), length=0.006, rng=rng)),
           min(1.0, 0.05 * rng.pareto(1.6) + 0.05)) for t in W.poisson(n / SR, rate, rng)]
    return W.place(n, ev)


@recipe("world-comet", "hum", "beat",
        "A comet fragment, modelled: two close sine tones beating slowly, a stretched octave grinding over them, a fizz",
        """Modelled: two near-sine tones at 91 and 92.1 Hz beating against each other about once a second (the second a
        little quieter, so each beat dips without quite vanishing), each with a faint second harmonic (so they beat twice as
        fast an octave up), their pitches drifting apart and back a few cents on their own, so the beat never settles; over
        them a stretched octave at 2.1 times the lower (191.5 Hz), quiet, grinding against those harmonics several times a
        second: felt in the teeth, and wrong, an octave that isn't one; the whole rounded off a little, for some bite.
        Over it all, very quiet: a crystal's shimmer (five narrow bands of noise at random pitches in 2.2-5.8 kHz, none in
        unison, each trembling and glinting) and a fizz of tiny hard ticks rung through little glassy shells. A breath of
        the open night round it. 8 s exact cycle.""",
        takes=1, loop=True, band=HUM, lufs=-26, seconds=HUM_L)
def hum_beat(rng, k):
    n = samples(HUM_L)
    pair = sines(n, rng, [91.0, 92.125], 4, 0.25, harm=0.15, level=0.06, amps=[1.0, 0.75])
    octave = sines(n, rng, [191.5], 5, 0.35, level=0.15)
    tone = dsp.saturate(W.norm(W.norm(pair) + W.norm(octave) * 0.18), 3)
    shimmer = sum(strand(n, rng, f, rng.uniform(250, 450)) * rng.uniform(0.5, 1.0)
                  for f in pitches(rng, 5, 2200, 5800, 1.06)) * glint(n, rng)
    return hum_mix(rng, tone, shimmer, fizz_model(rng, n), n)


# ---- crew-speaking-tube.call: the whistle-plug shrilling at the far end ---------------------------------------------------

def breath_of(rng, k):
    """One breath blown down the mouthpiece (pressure 0-1 per sample, and its length): a short sharp blow; a long call
    that dips and comes again; one blown in two quick pushes."""
    if k % 3 == 0:
        L = rng.uniform(0.42, 0.5)
        pts = [(0, 0), (0.03, 0.85), (0.07, 1.0), (L * 0.7, 0.92), (L - 0.06, 0.7), (L, 0)]
    elif k % 3 == 1:
        L = rng.uniform(0.68, 0.74)
        pts = [(0, 0), (0.05, 0.8), (0.17, 1.0), (0.32, 0.78), (0.44, 0.95), (L - 0.1, 0.85), (L, 0)]
    else:
        L = rng.uniform(0.6, 0.68)
        pts = [(0, 0), (0.035, 1.0), (0.19, 0.88), (0.25, 0.4), (0.3, 1.0), (L - 0.08, 0.8), (L, 0)]
    return env(pts, L), L


def pitch_of(rng, p, L, f0):
    """The whistle's pitch on that breath (Hz per sample): a small whistle sounds sharper the harder it's blown (about a
    semitone across its range), so it scoops up into its note as it speaks and sags as the breath runs out; and it wobbles
    with the breath (an unsteady 5-7 Hz tremor of a fifth of a semitone, a slower drift)."""
    n = len(p)
    t = np.arange(n) / SR
    rate = rng.uniform(5, 7) * (1 + 0.15 * wander(rng, n, 2, 1.0))
    tremor = 0.18 * np.sin(2 * np.pi * np.cumsum(rate) / SR + rng.uniform(0, 2 * np.pi))
    tremor = tremor * (0.6 + 0.4 * wander(rng, n, 3, 1.0))
    semis = 1.1 * (p - 1.0) - 1.0 * np.exp(-t / 0.035) + tremor + wander(rng, n, 1.5, 0.15)
    return (f0 * 2 ** (semis / 12)).astype(np.float32)


def speaks(p, lag=0.015, at=0.22, over=0.3):
    """How much the whistle's note sounds (0-1): it speaks once the pressure passes `at`, a breath behind it (`lag` s), and
    drops out as the breath goes under it again; the rush of air leads it in (the chiff) and outlasts it."""
    s = np.clip((p - at) / over, 0, 1) ** 1.5
    return np.concatenate([np.zeros(samples(lag), np.float32), s[:len(s) - samples(lag)]]).astype(np.float32)


def horn(rng, x, length):
    """Out of the far end's horn: held to what a long brass pipe passes (600 Hz-2 kHz, two passes each side, as the tube's
    voice is), ringing at the pipe's note (+8 dB at 1150 Hz), the horn's short brass throat round it (a quick, bright
    flutter of reflections, a little of the metal ringing), and, faint and dark, the pipe's length answering as the call
    runs back down it and returns; cut to `length` and faded."""
    y = hp(hp(x, TUBE[0], 2), TUBE[0], 2)
    y = lp(lp(y, TUBE[1], 2), TUBE[1], 2)
    y = dsp.peak(y, PIPE, 3, 8)
    metal = dsp.resonate(y, [PIPE, PIPE * 1.52, PIPE * 1.97], q=40, gains=[1.0, 0.5, 0.35])
    y = mix(dsp.room(y, "box", wet=0.3, rng=np.random.default_rng(7)), unit(metal) * 0.12 * np.max(np.abs(y)))
    back = 2 * rng.uniform(12, 20) / SOUND
    y = ck.place([(0, y, 0), (back, lp(y, 1500), -20)])
    return dsp.fade(dsp.fit(y, samples(length)), 0.002, 0.06)


def call(rng, k, whistle):
    """One call: a breath blown down the tube, the whistle at the far end shrilling on it, heard out of the horn."""
    p, L = breath_of(rng, k)
    f = pitch_of(rng, p, L, (1380.0, 1290.0, 1440.0)[k % 3] * rng.uniform(0.98, 1.02))
    return horn(rng, whistle(rng, p, L, f), L + 0.12)


@functools.lru_cache(maxsize=1)
def _pure_f():
    """The real glass's ring (Hz), found as house_beasts.held finds it."""
    return float(strongest(W.rec(PURE)[:samples(0.2)], 500, 9000))


def whistle_rec(rng, p, L, f):
    """The whistle from real recordings: a real glass ringing almost pure (2.76 kHz) set at the whistle's note, its
    partial held as a steady tone (still: its twin 25 Hz under it, which beats against it in a trill, taken 12 dB down to
    a flicker) and, under it, held out into a whine (a short window of its ring frozen, so the partial is a narrow band of
    noise: airy, a whistle's breath in its tone, which alone would flutter like a pea's), both pulled along the
    whistle's pitch; a real gasp drawn out under it, rung round the note, for the breath through the plug, leading the
    tone in and outlasting it."""
    n = len(p)
    f0 = float(np.median(f))
    m = samples(L * 1.6)
    steady = still(rng, PURE, 12 * np.log2(f0 / _pure_f()), m, weight=lambda F: np.where(np.abs(F - f0) < 5, 1.0, 0.25))
    x = unit(steady) + unit(held(rng, PURE, f0, m, window=0.06)) * 0.25
    tone = dsp.fit(bend(x, 12 * np.log2(f / f0)), n)
    air = drawn(rng, GASP, L, rng.uniform(0, 2))
    air = mix(unit(bp(air, f0 * 0.6, f0 * 2.2)), unit(dsp.resonate(air, [f0], q=10)) * 0.8, length=n)
    s = speaks(p)
    return mix(unit(tone) * s * p, unit(air) * p * (0.5 + 0.5 * (1 - s)) * 0.8, length=n)


@recipe("crew-speaking-tube", "call", "blown",
        "The far end's whistle-plug shrilling as someone blows down the tube: thin, breathy, wobbling, out of a brass pipe",
        """From real recordings: the whistle is a real glass ringing almost pure (sfx_100 glass_01, 2.76 kHz), set at
        1.3-1.45 kHz: its partial held as a steady tone (its twin 25 Hz under it taken down to a flicker) and the same ring
        frozen short under it into a narrow band of noise (airy, the breath in a small whistle's note), both pulled along
        a breath's pitch: scooping up into its note as it speaks, sharper the harder it's blown, wobbling with the breath,
        sagging as it runs out; a real gasp drawn out under it and rung round the note for the air through the plug,
        leading the note in and outlasting it. Heard out of the far end's horn: through a long brass pipe (600 Hz-2 kHz,
        ringing at its note, 1150 Hz, as the tube's voice is), the horn's short brass throat round it, and the pipe's
        length answering faintly. Takes: a short sharp blow; a long call that dips and comes again; two quick pushes.
        0.5-0.8 s. Not the engine's whistle.""",
        sources=[PURE, GASP], takes=3, band=TUBE, lufs=-22)
def call_blown(rng, k):
    return call(rng, k, whistle_rec)


def whistle_model(rng, p, L, f):
    """The whistle modelled: the jet's edge tone locked to the plug's little chamber, a near-sine with a faint second and
    third harmonic, its level and pitch flickering with the jet's turbulence; the jet's own rush rung through the chamber
    round the note (q 12), loudest as the note speaks (the chiff) and as the breath gives out, and its hiss over it."""
    n = len(p)
    f0 = float(np.median(f))
    flick = 1 + 0.003 * wander(rng, n, 60, 1.0)
    ph = 2 * np.pi * np.cumsum(f * flick) / SR
    tone = (np.sin(ph) + 0.1 * np.sin(2 * ph + 0.4) + 0.04 * np.sin(3 * ph + 1.1)) * (1 + 0.12 * wander(rng, n, 25, 1.0))
    noise = synth.noise(L, rng)[:n]
    jet = unit(dsp.resonate(noise, [f0], q=12))
    hiss = unit(bp(synth.noise(L, rng, "pink")[:n], 900, 5000))
    s = speaks(p)
    return mix(unit(tone) * s * p, jet * p * (0.5 + 0.5 * (1 - s)) * 0.8, hiss * p * 0.25, length=n)


@recipe("crew-speaking-tube", "call", "edge",
        "The whistle-plug shrilling, modelled: an edge tone on a breath, breathy and wobbling, out of a long brass pipe",
        """Modelled: the breath blown down the mouthpiece drives a small whistle at the far end, its jet's edge tone locked
        to the plug's little chamber at 1.3-1.45 kHz: a near-sine with a faint second and third harmonic, its level and
        pitch flickering with the jet's turbulence, sharper the harder it's blown (scooping up into its note as it speaks,
        sagging as the breath runs out) and wobbling with the breath; the jet's own rush rung round the note, loudest as
        the note speaks (the chiff) and as the breath gives out. Heard out of the far end's horn: through a long brass pipe
        (600 Hz-2 kHz, ringing at its note, 1150 Hz, as the tube's voice is), the horn's short brass throat round it, and
        the pipe's length answering faintly. The same three takes as 'blown'. 0.5-0.8 s. Not the engine's whistle.""",
        takes=3, band=TUBE, lufs=-22)
def call_edge(rng, k):
    return call(rng, k, whistle_model)
