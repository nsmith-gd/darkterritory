"""A friend on the crane's hook heard (queue #525, note 798; K1's #1046, note 726: "the cable creaking under a rider, and a
yelp on a shake-off"), as tools/audio/cues.py lists them and GameAudio.Hoist plays them off the crane's replicated riders
and the Hoisted flag.

What each is decides the build:
- The cable under a rider: a yard crane's wire rope and its hook block taking a crewmate's weight (or two), swaying a
  little as the crane moves: the swivel's iron creaking at each end of the sway, the rope's strands groaning under the
  load, the block's sheave ticking. A seamless 10 s loop, outdoors at a yard, held while anyone rides.
- The yelp: a crewmate coming off the hook up in the air (shaken off by the operator, or letting go high): a short,
  startled cry, an 'aah!' or a 'whoa!' as the drop takes them, the coat flapping. Each take a different crewmate.
- The landing: down off the hook, which the director made "a silly gag, not a big grief" (15 Oct, note 802: at most 10
  health, by the height): a padded thump and a comic 'oof!', the wind knocked out of them, never a body falling dead.

Each cue has two candidates. The creak's first is the packs' real creaks pitched down to iron and rope, with real loose
iron for the block; the second is modelled stick-slip through an iron body. The yelp's first is the crew's modelled throat
breathing through the packs' real gasp; the second the same throat on its own, sharper and shorter.
"""

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, env, mix
from recipes import crew_kit as ck
from recipes.beasts import unit
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe
from recipes.rescue_finds import effort, VOICES, GASP, CREAKS, RATTLE, SWISH, DROP

L_ = "crew-hoist"
SOFT = [f"kenney_impact-sounds:impactSoft_heavy_00{i}" for i in range(5)]   # real soft heavy impacts: a body on the ground
CYCLE = 10.0


def sway_turns(rng, length):
    """When the load turns at each end of its sway: a pendulum of a few metres of rope, every 1.3-2.4 s, never even."""
    t, out = rng.uniform(0.1, 0.6), []
    while t < length:
        out.append(t)
        t += rng.uniform(1.3, 2.4)
    return out


def creak_real(rng, at):
    """One turn's creak from the packs' real creaks: pitched down to an iron swivel and a loaded rope, the block's loose
    iron ticking with it now and then."""
    c = dsp.vari(hit_of(CREAKS[int(rng.integers(len(CREAKS)))], int(rng.integers(2)), rng.uniform(0.5, 0.9)), rng.uniform(-11, -6))
    parts = [(0.0, unit(hp(c, 150, 2)), 0)]
    if rng.random() < 0.45:
        parts.append((rng.uniform(0.05, 0.3), unit(hp(dsp.vari(hit_of(RATTLE, int(rng.integers(3)), 0.12), rng.uniform(-3, 2)), 400, 2)), -14))
    return parts


def creak_model(rng):
    """One turn's creak modelled: stick-slip through a heavy iron part (the swivel) catching and letting go as the weight
    swings through, the rope's strands a slower groan under it."""
    L = rng.uniform(0.5, 0.9)
    swivel = synth.creak(L, env([(0, rng.uniform(18, 26)), (L, rng.uniform(40, 60))], L), rng, body=synth.IRON, q=22, jitter=0.35,
                         shape=env([(0, 0), (0.08, 1), (L * 0.7, 0.7), (L, 0)], L))
    rope = synth.creak(L, env([(0, 9), (L, 14)], L), rng, body=[220, 410, 690, 1150], q=8, jitter=0.5, grit=0.6,
                       shape=env([(0, 0), (0.15, 0.8), (L, 0)], L))
    return unit(hp(mix(unit(swivel), unit(rope) * 0.5), 150, 2))


def loop(rng, piece):
    """A seamless cycle of the turns' creaks, the yard's open air round them, each tail folded onto the head."""
    n = samples(CYCLE)
    y = np.zeros(n + samples(2.0), np.float32)
    for t in sway_turns(rng, CYCLE):
        for dt, x, db in piece(t):
            i = samples(t + dt)
            m = min(len(x), len(y) - i)
            y[i:i + m] += x[:m] * 10 ** (db / 20) * rng.uniform(0.6, 1.0)
    # The squish kept out (the iron's lowest mode and the open air's bloom sit under 150 Hz): a creak, not a rumble.
    return hp(dsp.wrap(dsp.room(y, "night", 0.18), n), 180, 4)


@recipe(L_, "creak", "swivel", "The cable under a rider: the hook's iron swivel and the loaded rope creaking at each turn of the sway",
        """The packs' real creaks pitched down six to eleven semitones, so a hinge's creak becomes an iron swivel and a
        loaded rope, laid at the turns of a slow, uneven sway (every 1.3-2.4 s); the block's real loose iron ticking now
        and then. Open air round it. A 10 s cycle, its tails folded onto its head.""", sources=CREAKS + [RATTLE], loop=True,
        takes=1, lufs=-23)
def creak_swivel(rng, k):
    return loop(rng, lambda t: creak_real(rng, t))


@recipe(L_, "creak", "strain", "The cable under a rider, modelled: iron stick-slip at the swivel, the rope's strands groaning",
        """Modelled: at each turn of the sway, stick-slip through a heavy iron part's modes (the swivel catching and letting
        go as the weight swings through) with a slower groan through the rope's strands under it. The same uneven sway as
        the other candidate; open air round it. A 10 s cycle.""", loop=True, takes=1, lufs=-23)
def creak_strain(rng, k):
    return loop(rng, lambda t: [(0.0, creak_model(rng), 0)])


def cry(rng, voice, real, L, top):
    """A startled cry as the drop takes them: the breath caught, then an open vowel jumping up in pitch and falling away."""
    return effort(rng, voice, L, [(0, 1.3), (0.05, top), (L * 0.55, top * 0.88), (L, top * 0.62)],
                  [(0, "u"), (0.05, "o"), (L * 0.35, "a"), (L, "a")],
                  [(0, 0), (0.02, 0.8), (0.07, 1), (L * 0.6, 0.7), (L, 0)], press=0.5, rough=0.2, voiced=0.82, real=real)


@recipe(L_, "yelp", "whoa", "Off the hook up in the air: a startled 'whoa!' as the drop takes them, the coat flapping",
        """The crew's modelled throat (each take a different crewmate: a man, a woman, a bigger man) breathing through the
        packs' real gasp: the breath caught, then an open 'whoa' jumping up in pitch and falling away as they drop, and
        their coat flapping (the packs' real cloth swished).""", sources=[GASP, SWISH], takes=3, lufs=-18)
def yelp_whoa(rng, k):
    v = cry(rng, VOICES[k % 3], GASP, rng.uniform(0.5, 0.62), rng.uniform(1.75, 1.95))
    coat = unit(hp(dsp.vari(ck.get(SWISH), rng.uniform(-2, 2)), 300, 2))[:samples(0.4)]
    return unit(mix(v, np.concatenate([np.zeros(samples(0.08), np.float32), coat]) * 0.3))


@recipe(L_, "yelp", "aah", "Off the hook up in the air, modelled: a short sharp 'aah!', the breath caught first",
        """The crew's modelled throat on its own breath (each take a different crewmate): a sharp intake, then a short 'aah!'
        higher and quicker than the other candidate's, cut off as they land on nothing yet.""", takes=3, lufs=-18)
def yelp_aah(rng, k):
    return cry(rng, VOICES[k % 3], None, rng.uniform(0.34, 0.42), rng.uniform(2.0, 2.2))


def oof(rng, voice, real, L, top):
    """The wind knocked out of them, played for the laugh: a short, pressed 'oof' starting high and dropping away."""
    return effort(rng, voice, L, [(0, top), (0.03, top * 0.96), (L * 0.5, top * 0.82), (L, top * 0.66)],
                  [(0, "u"), (0.03, "o"), (L * 0.6, "o"), (L, "h")],
                  [(0, 0), (0.005, 1), (L * 0.45, 0.75), (L, 0)], press=0.38, rough=0.25, voiced=0.7, real=real)


def thumped(thump, rng, voice, real, top):
    """The landing, then the 'oof' a beat after it (the comic timing: the body first, the breath it knocks out second)."""
    v = oof(rng, voice, real, rng.uniform(0.22, 0.3), top)
    gap = np.zeros(samples(rng.uniform(0.05, 0.09)), np.float32)
    # the body heard landing first and plainly: the voice under it, not over it
    return unit(mix(unit(thump) * 1.4, np.concatenate([gap, v]) * 0.5))


@recipe(L_, "land", "whump", "Down off the hook, a gag: a padded thump on the ground and a comic 'oof!' a beat after it",
        """The director's call (15 Oct, note 802): a drop off the hook is "a silly gag, not a big grief". The packs' real
        soft heavy impact (a body on the ground) with their heavy leather drop over it (the coat), pitched down a little,
        the boom taken out so it lands soft, not dead; then, a beat later, the crew's modelled throat (each take a different crewmate) breathing a short
        pressed 'oof!' through the packs' real gasp, high and dropping away.""", sources=SOFT + [DROP, GASP], takes=3, lufs=-17)
def land_whump(rng, k):
    n = samples(0.35)
    body = unit(ck.align(dsp.vari(ck.get(SOFT[(k + int(rng.integers(5))) % 5]), rng.uniform(-3, 0))))[:n]
    coat = unit(ck.align(dsp.vari(ck.get(DROP), rng.uniform(-4, -1))))[:n]
    body = np.pad(body, (0, n - len(body)))
    coat = np.pad(coat, (0, n - len(coat)))
    # (the soft impacts are mostly under 150 Hz as recorded: a landing that's a gag thumps, it doesn't boom)
    thump = unit(hp(mix(body, coat * 0.5), 180, 4))
    return thumped(thump, rng, VOICES[k % 3], GASP, rng.uniform(1.15, 1.3))


@recipe(L_, "land", "flop", "Down off the hook, modelled: a soft flop on the ground and a sillier, higher 'oof!'",
        """Modelled: a soft body's flop (a short burst of dark noise through a padded thud's modes, the coat's slap on top,
        nothing booming under it) and the crew's modelled throat on its own breath, an 'oof!' pitched higher and
        squeezed shorter than the other candidate's, for more of a cartoon.""", takes=3, lufs=-17)
def land_flop(rng, k):
    L = 0.18
    body = synth.noise(L, rng, "pink") * env([(0, 1), (0.012, 0.6), (L, 0)], L)
    thud = synth.creak(L, env([(0, 30), (L, 30)], L), rng, body=[160, 290, 520], q=4, jitter=0.1,
                       shape=env([(0, 1), (0.03, 0.4), (L, 0)], L))
    slap = hp(synth.noise(0.04, rng, "white"), 1800, 2) * env([(0, 1), (0.04, 0)], 0.04)
    thump = unit(hp(mix(unit(lp(body, 900, 2)), unit(thud) * 0.6, np.concatenate([unit(slap) * 0.3, np.zeros(samples(L - 0.04), np.float32)])), 150, 2))
    return thumped(thump, rng, VOICES[k % 3], None, rng.uniform(1.35, 1.5))

