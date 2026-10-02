"""Creature sounds for five horrors (GDD §21): the Choir, the Gaunt, the Soot Children, the Switchman and the Stoker.

What each body is made of decides the build:
- The Choir: small ghosts, each a veined membrane bell with one child's mouth singing, six tendrils. Every voice it uses
  is the tell's own (recipes/choir.py: the Locrian lullaby round and the Shepard rise imported, and `ghost` builds a
  throat exactly as the round does, on a pitch curve of our own), so these are the same creatures; the rest is a bell
  of wet skin: struck, rubbed, swimming, lashing. The director's note asks for melody from
  the music theory of unease, so the sung parts fall on Locrian tritones and minor seconds, slide, sag flat and never
  cadence on the tonic.
- The Gaunt: dry bark on stilts, silent until the blow. Hollow wood, cracking joints, bark crumbs; nothing before the hit.
- The Soot Children: a child's throat bent wrong (a tract that grows while the pitch stays small), a child's breath,
  wet swallowing; ash and soot crumbling. No child's speech: the packs have none, so nothing here pretends to talk.
- The Switchman: its junction lamp is an oil lamp, so flame (a guttering draught, wick spits) and hot glass, not
  electricity; spindly joints on an iron lever.
- The Stoker: something alive in the coals: the firebox's own fire, iron and coal, moved by a body that breathes.
"""

import numpy as np
from scipy import signal
from scipy.ndimage import minimum_filter1d, uniform_filter1d

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import crew_kit as ck
from recipes.beasts import STEEL_TICKS, steel_ticks, ring_of, through, main_hit
from recipes.choir import round_voice, shepard_stack, VOICES, LOOP, BEAT, D, Eb, F, G, Ab
from recipes.kit import melody_f0, bell_body, MEMBRANE, BELL, hz, any_of, scatter, slap, squelch, snap, thud, whoosh, twigs, \
    gravel

SR = dsp.SR

WHIP = ["sfx_100_v2:switch_01", "sfx_100_v2:switch_02"]
CLOTH = ["kenney_rpg-audio:cloth1", "kenney_rpg-audio:cloth2", "kenney_rpg-audio:cloth3", "kenney_rpg-audio:cloth4"]
FLAP = ["kenney_rpg-audio:cloth3", "kenney_rpg-audio:dropLeather"]


def K(name, takes=range(5)):
    return [f"kenney_impact-sounds:{name}_{i:03d}" for i in takes]


# ---- Shared parts -------------------------------------------------------------------------------------------------------

def bend(x, semis):
    """Varispeed along a pitch curve (semitones; a constant or per output sample): a doppler pass, a voice sagging like a
    tape slowing down. The sound gets shorter going up and longer going down, as a tape would."""
    c = synth.curve(semis, len(x))
    n = int(len(x) * 2 ** (max(-float(np.min(c)), 0) / 12)) + 2
    r = 2 ** (synth.curve(c, n) / 12)
    pos = np.concatenate([[0.0], np.cumsum(r)[:-1]])
    pos = pos[pos < len(x) - 1]
    return np.interp(pos, np.arange(len(x)), x).astype(np.float32)


def rec(rng, keys, semis=0.0, length=None):
    """A recording picked from `keys`, its lead-in silence cut so the attack lands on time, varispeeded, shortened."""
    x = dsp.vari(dsp.trim_silence(any_of(rng, keys), -40, 0.002), semis)
    return cut(x, length) if length else x


def cut(x, length, tail=None):
    """A recording shortened to `length` s with its end faded (a knock without its ring, a grain of a crunch)."""
    y = dsp.fit(x, samples(length))
    return dsp.fade(y, 0.001, tail if tail is not None else length * 0.6)


def norm(x):
    return (x / (np.max(np.abs(x)) + 1e-9)).astype(np.float32)


def finish(x, lufs=-20.0, ceiling=-1.5):
    """Level a take, then pull its loudest peaks under the ceiling with a look-ahead limiter (the gain dips smoothly for
    a few milliseconds round each peak, never clipping it), so build.py's own levelling has nothing to clip and every
    take of a cue lands at the same loudness. A 25 Hz highpass first takes off any DC an asymmetric knock leaves."""
    y = hp(np.asarray(x, np.float32), 25)
    c = dsp.db2a(ceiling)
    w = samples(0.003)
    for _ in range(12):
        y = dsp.gain(y, lufs - dsp.loudness(y))
        need = np.minimum(1.0, c / (np.abs(y) + 1e-9))
        if need.min() > 0.97:
            break
        # a window's minimum, then a shorter average of it: smooth, and never above what any sample needs
        y = (y * uniform_filter1d(minimum_filter1d(need, 2 * w + 1), w + 1)).astype(np.float32)
    return y


def morph_tract(x, vowels, k, breath=0.0, rng=None):
    """synth.tract with a tract that changes size while it sounds (k: 1 a child's, 0.5 a beast's; a constant or per
    sample): a child's throat growing into something else's while the pitch stays where it was."""
    n = len(x)
    kc = synth.curve(k, n)
    if isinstance(vowels, str):
        vowels = [(0.0, vowels)]
    ts = [t for t, _ in vowels] + [n / SR]
    t = np.arange(n) / SR
    if breath:
        x = x + breath * hp((rng or np.random.default_rng(1)).standard_normal(n).astype(np.float32), 300) * 0.3
    out = np.zeros(n, np.float32)
    for i, g in enumerate((1.0, 0.7, 0.35)):
        fs = [synth.VOWELS[v][0][i] for _, v in vowels]
        bs = [synth.VOWELS[v][1][i] for _, v in vowels]
        fc = np.interp(t, ts, fs + [fs[-1]]).astype(np.float32) * kc
        out += g * dsp.sweep_filter(x, "bp", fc, q=float(np.median(np.array(fs) / np.array(bs))), block=128)
    return norm(out)


def knock(rng, f, length=0.5, q=25, body=synth.BARK):
    """A hollow body struck: a short burst rung through its modes (`body` scaled so its lowest sits at f)."""
    x = np.zeros(samples(length), np.float32)
    k = samples(rng.uniform(0.002, 0.004))
    x[:k] = rng.standard_normal(k) * np.linspace(1, 0, k)
    y = dsp.resonate(x, [b * f / body[0] for b in body], q=q, gains=[1 / (1 + 0.4 * i) for i in range(len(body))])
    return norm(y * env([(0, 1), (length, 0)], length, "exp")[:len(y)])


def drumhead(rng, f, length=0.8, q=10, drop=4.0):
    """A wet membrane struck: a knock through a drumhead's modes, its pitch bending down as the struck skin relaxes
    (a talking drum's bend), with a jelly wobble in the ring."""
    y = knock(rng, f * 2 ** (drop / 12), length * 1.4, q=q, body=[m * 100 for m in MEMBRANE])
    st = env([(0, 0), (0.25, -drop), (length * 1.4, -drop - 0.3)], length * 1.4)
    y = bend(y, st)
    wob = 1 - 0.35 * np.exp(-np.arange(len(y)) / SR / 0.25) * (0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(6, 9) * np.arange(len(y)) / SR))
    return norm(dsp.fit(y * wob, samples(length)) * env([(0, 1), (length, 0)], length, "exp"))


def ghost(rng, f0, vowels="u", bell=560, breath=0.22):
    """One of the Choir's throats on a pitch curve of our own, built exactly as the tell's round builds its voices (a
    child's glottis on 'oo', rung through a thin bell of membrane)."""
    g = synth.glottis(f0, len(f0) / SR, rng, jitter=0.006, shimmer=0.07, oq=0.65)
    v = synth.tract(g, vowels, "child", breath=breath, rng=rng)
    return bell_body(v, bell * rng.uniform(0.97, 1.03), q=25, wet=0.18)


def swim(rng, f):
    """One stroke of a bell swimming, jellyfish-fashion: the membrane contracts and pushes a soft breath of air out
    through itself (a cloth flap two octaves down, and a puff rung through the bell)."""
    L = 0.7
    flap = cut(dsp.vari(any_of(rng, FLAP), rng.uniform(-16, -10)), L)
    puff = synth.noise(L, rng, "pink") * env([(0, 0), (0.07, 1), (L, 0)], L) ** 2
    puff = dsp.resonate(bp(puff, 100, 1200), [f * m for m in MEMBRANE[:5]], q=6) * 0.5 + bp(puff, 150, 900) * 0.6
    return norm(hp(mix(lp(flap, 1400) * 0.8, puff), 120))


def moving_lp(x, cutoff):
    return dsp.sweep_filter(x, "lp", synth.curve(cutoff, len(x)), q=0.7)


# ---- The Choir ----------------------------------------------------------------------------------------------------------

def _flyby(rng, voice, at, near, speed):
    """One ghost passing: a fragment of its round bent by doppler (sharp coming, flat going), louder and brighter at its
    closest, its bell swimming as it goes."""
    L = rng.uniform(1.8, 3.0)
    s = rng.uniform(0, len(voice) / SR - L - 0.1)
    x = dsp.trim(voice, s, L)
    t = np.arange(len(x)) / SR - L * at
    d = np.sqrt(near ** 2 + (speed * t) ** 2)
    x = bend(x, rng.uniform(0.7, 1.3) * np.tanh(-t * speed / near))
    m = len(x)
    d = synth.curve(d, m)
    g = (near / d) * env([(0, 0), (0.3, 1), (m / SR - 0.4, 1), (m / SR, 0)], m / SR)[:m]
    x = moving_lp(x, 16000 / (1 + d / 20)) * g
    bf = rng.uniform(170, 260)
    strokes = np.zeros(m, np.float32)
    tt = rng.uniform(0, 0.3)
    while tt < m / SR:
        a = samples(tt)
        sw = swim(rng, bf)[:m - a]
        strokes[a:a + len(sw)] += sw * rng.uniform(0.5, 1.0)
        tt += rng.uniform(0.65, 1.05)
    tendril = synth.rustle(m / SR, 70, rng, f=(700, 3500), ticks=0.2)
    return x + (strokes * 0.35 + tendril * 0.25) * g


@recipe("cs-choir", "arrive", "flyby",
        "Ghosts flitting past all round, each singing a scrap of the lullaby bent by its own speed, bells swimming",
        """Fragments of the tell's own round (the Locrian lullaby voices from the Choir's tell, imported), each sent past
        the listener like a ghost flying by: sharp as it comes, sagging flat as it goes, loudest and brightest at its
        closest, from a couple of metres to thirty. Under each, its bell swimming like a jellyfish (a cloth flap two
        octaves down and a puff of air rung through a drumhead's modes, a stroke a second) and its tendrils trailing (a
        soft low rustle). Eight passes round the loop, near and far, so they're all round you; a night's reverb folded
        back so the loop is seamless.""",
        sources=FLAP, takes=1, loop=True)
def choir_arrive_flyby(rng, take):
    n = samples(LOOP)
    voices = [round_voice(rng, k, octave, cents) for k, octave, cents in VOICES]
    b = Bus(LOOP + 4)
    starts = ((np.arange(8) + rng.uniform(-0.3, 0.3, 8)) * LOOP / 8) % LOOP   # an early one goes round the end
    for i, t0 in enumerate(starts):
        near = [2.5, 14, 4, 30, 6, 3, 18, 9][i]
        x = _flyby(rng, voices[i % 4], rng.uniform(0.35, 0.65), near, rng.uniform(5, 9))
        b.at(t0, x, -2 if near < 8 else 0)
    y = dsp.room(b.x, "night", wet=0.25, rng=rng)
    return finish(dsp.wrap(y, n))


def rubbed_bell(rng, f, length, pulse):
    """A membrane bell rubbed into song, like a wet finger round a glass: friction noise through a bell's partials at a
    very high Q, so it rings and wavers. Each swim stroke (`pulse` Hz) tightens the skin: louder and a few cents sharp."""
    n = samples(length)
    t = np.arange(n) / SR
    rub = bp(synth.noise(length, rng), f * 0.4, f * 6) * (1 + 0.6 * norm(lp(rng.standard_normal(n).astype(np.float32), 25)))
    y = norm(dsp.resonate(rub, [f * p for p in BELL], q=260, gains=[0.5, 1, 0.8, 0.6, 0.6, 0.4, 0.4, 0.3, 0.2, 0.15]))
    ph = rng.uniform(0, 6.28)
    stroke = (0.5 + 0.5 * np.sin(2 * np.pi * pulse * t + ph)) ** 3
    y = y * (0.45 + 0.55 * stroke)
    return bend(y, 0.12 * stroke)


@recipe("cs-choir", "arrive", "bells",
        "The bells themselves: a slow cluster of membrane bells rubbed into song on the lullaby's tritones, pulsing as they swim",
        """Each ghost's bell is rubbed into a tone like a wet finger round a glass (friction noise through a bell's
        partials, with its minor-third tierce): D, A flat, E flat, G and F in low registers, so the cluster always holds
        a tritone and a minor second and never settles. Every bell swells and fades on its own slow arc and pulses as it
        swims, a stroke every second or so, going a few cents sharp as the skin tightens, so the chord throbs and beats.
        Two of the bells are hummed along with by their child's mouth (the tell's throat, closed to 'mm', an octave
        up). Night reverb folded back for a seamless loop.""",
        takes=1, loop=True)
def choir_arrive_bells(rng, take):
    n = samples(LOOP)
    b = Bus(LOOP + 9)
    notes = [D - 12, Ab - 12, Eb, G - 12, D, F - 12, Ab]
    for i, m in enumerate(notes):
        L = rng.uniform(4.5, 7.5)
        t0 = i * LOOP / len(notes) + rng.uniform(-0.4, 0.4)
        x = rubbed_bell(rng, hz(m) * 2 ** (rng.uniform(-12, 12) / 1200), L, rng.uniform(0.65, 1.0))
        x = dsp.shaped(x, [(0, 0), (L * rng.uniform(0.3, 0.45), 1), (L * 0.7, 0.8), (L, 0)])
        b.at(max(t0, 0), x, -3 if m > 64 else 0)
        if i in (1, 4):
            f0 = env([(0, hz(m + 12)), (L, hz(m + 12) * 2 ** (-0.6 / 12))], L).astype(np.float32)
            hum = ghost(rng, f0, "m", breath=0.15)
            hum = dsp.shaped(hum, [(0, 0), (L * 0.5, 0.7), (L * 0.75, 0.5), (L, 0)])
            b.at(max(t0, 0) + 0.4, hum, -11)
    y = dsp.room(hp(b.x, 90), "night", wet=0.3, rng=rng)
    return finish(dsp.wrap(y, n))


def lashes(rng, count, spacing=(0.05, 0.11), wet=0.6):
    """Tendrils whipping round something: thin wet lashes in quick succession, each a whip's crack bent about and a wet
    smack, getting quieter as they wind tighter. Returns (sound, the time of each lash)."""
    b = Bus(count * spacing[1] + 0.4)
    t, times = 0.0, []
    for i in range(count):
        w = cut(dsp.vari(any_of(rng, WHIP), rng.uniform(-5, 3)), 0.12)
        x = mix(lp(w, 5000), slap(rng, 0.08, 1800, wet) * 0.5)
        b.at(t, x, -2.5 * i)
        times.append(t)
        t += rng.uniform(*spacing)
    return b.x, times


def bell_inside(x, rng, f_bell, throb=1.5):
    """A voice heard from inside the bell that has capped your head: close and dry, muffled, ringing in the bell, and
    squeezing in slow throbs as it contracts."""
    y = bell_body(lp(x, 1300), f_bell, q=30, wet=0.55)
    y = dsp.room(y, "box", wet=0.35, rng=rng)[:len(x)]
    return dsp.tremolo(y, throb, 0.5, rng, jitter=0.2)


@recipe("cs-choir", "seize", "dive",
        "A ghost diving mouth-first, singing faster as it falls; tendrils lash round the head, and the song goes on inside its bell",
        """The tell's throat sings a falling scrap of the lullaby an octave up (A flat, G, E flat, D; or F, E flat, D),
        each note shorter than the last as it dives in from twenty-odd metres, sharp with doppler and brightening, a low
        rush of its bell under it. On the last note it caps the head: a wet membrane slap and a drumhead's dull knock,
        then four to six tendril lashes in a tenth of a second winding tighter, and the skin squeaking taut. The held
        note (D, or a tritone leap to A flat) carries on from inside the bell (muffled, close, ringing, squeezing in slow
        throbs), closes to a hum and sags a semitone flat, never onto the home note.""",
        sources=WHIP, takes=2)
def choir_seize_dive(rng, take):
    # the held note sags a semitone: off the tonic into C sharp, or off the tritone onto G; never down onto D
    fall = [(Ab + 12, 0.34), (G + 12, 0.24), (Eb + 12, 0.17), (D + 12, 1.8)] if take == 0 else \
           [(F + 12, 0.32), (Eb + 12, 0.22), (D + 12, 0.16), (Ab + 12, 1.8)]
    f0, onsets, total = melody_f0(fall, 1.0, rng, scoop=40, glide=0.03, vib=(5.5, 0.008), drift=10)
    cap = onsets[3]
    st = env([(0, rng.uniform(0.9, 1.3)), (cap, 0.1), (cap + 0.4, 0), (cap + 0.6, 0), (total, -1.1)], total)
    f0 = f0 * 2 ** (dsp.fit(st, len(f0)) / 12)
    v = ghost(rng, f0, [(0, "o"), (cap - 0.1, "u"), (cap + 0.35, "u"), (cap + 0.9, "m")])
    n = len(v)
    pre = moving_lp(v, env([(0, 2200), (cap, 11000)], total, "exp")) * env([(0, 0.06), (cap, 1)], total, "exp")[:n]
    post = bell_inside(v, rng, hz(D), throb=rng.uniform(1.3, 1.8)) * 1.4
    k = np.clip((np.arange(n) / SR - cap) / 0.04, 0, 1).astype(np.float32)
    y = pre * (1 - k) + post * k
    y = y * env([(0, 1), (total - 0.5, 1), (total, 0)], total)[:n]
    b = Bus(total + 0.5)
    b.at(0, y)
    b.at(0, whoosh(rng, cap + 0.05, 140, 1600, peak=0.95), -9)
    b.at(cap, slap(rng, 0.15, 1500, wet=0.9), -3)
    b.at(cap, drumhead(rng, rng.uniform(130, 170), 0.35, q=8, drop=3), -4)
    b.at(cap + 0.02, squelch(rng, 0.35, 250, 1100), -8)
    lx, lt = lashes(rng, int(rng.integers(4, 7)))
    b.at(cap + 0.04, lx, -3)
    taut = synth.creak(0.6, env([(0, 45), (0.6, 12)], 0.6), rng, body=[m * 380 for m in MEMBRANE], q=20, grit=0.1)
    b.at(cap + lt[-1] + 0.08, bp(taut, 300, 3500) * env([(0, 1), (0.6, 0)], 0.6), -14)
    return finish(b.x)


@recipe("cs-choir", "seize", "close",
        "One voice sliding up a tritone as it comes close, then closing its mouth into a hum over your head as tendrils tighten",
        """No rush and no impact: one of the Choir's throats holds a note and slides slowly up a tritone as it closes in
        from fifteen metres, the slide the unsettling part, drier and nearer all the way. On contact its 'oo' closes to
        'mm' and drops a minor second, and you hear it from inside its bell (muffled, ringing, squeezing). Under the hum
        the tendrils tighten: a rubbery stick-slip squeak through a drumhead's modes, quickening as they wind, and wet
        slithers. The hum stops dead when the seal is complete.""",
        takes=2)
def choir_seize_close(rng, take):
    a, b_ = (D + 12, Ab + 12) if take == 0 else (G, G + 6)   # a tritone up either way
    T = 3.8
    hit = rng.uniform(1.3, 1.5)
    f0 = env([(0, hz(a)), (0.3, hz(a)), (hit - 0.1, hz(b_)), (hit + 0.05, hz(b_) * 2 ** (-1 / 12)),
              (T, hz(b_) * 2 ** (-1.4 / 12))], T, "exp")
    f0 = f0 * (1 + 0.006 * np.sin(2 * np.pi * 5.2 * np.arange(len(f0)) / SR))
    v = ghost(rng, f0.astype(np.float32), [(0, "o"), (hit - 0.3, "u"), (hit + 0.1, "m")])
    n = len(v)
    far = dsp.room(v, "night", wet=0.5, rng=rng)[:n]
    pre = moving_lp(far, env([(0, 2500), (hit, 10000)], T, "exp")) * env([(0, 0.08), (hit, 1)], T, "exp")[:n]
    post = bell_inside(v, rng, hz(b_ - 12), throb=1.2) * 1.5
    k = np.clip((np.arange(n) / SR - hit) / 0.12, 0, 1).astype(np.float32)
    y = (pre * (1 - k) + post * k) * env([(0, 0), (0.2, 1), (T - 0.03, 1), (T, 0)], T)[:n]
    bus = Bus(T)
    bus.at(0, y)
    L = T - hit - 0.1
    taut = synth.creak(L, env([(0, 12), (L, 55)], L, "exp"), rng, body=[m * rng.uniform(220, 260) for m in MEMBRANE],
                       q=22, grit=0.15)
    bus.at(hit + 0.1, bp(taut, 200, 3000) * env([(0, 0), (0.3, 1), (L, 0.8)], L), -12)
    for t in (hit + 0.2, hit + rng.uniform(0.9, 1.4)):
        bus.at(t, squelch(rng, 0.6, 200, 800, depth=0.5), -15)
    return finish(bus.x)


# Doors: the material is the door (a car's wooden door, an iron door or hatch), heard from inside the car.
DOOR = {
    "wood": dict(panel=K("impactWood_light") + K("impactWood_medium"), frame=K("impactWood_light"),
                 latch=["kenney_rpg-audio:metalLatch", "kenney_rpg-audio:metalClick"], body=synth.WOOD, room="car",
                 ring=(170, 14)),
    "grate": dict(panel=K("impactPlate_light") + K("impactPlate_medium"), frame=K("impactMetal_light"),
                  latch=["kenney_rpg-audio:metalLatch", "kenney_rpg-audio:metalClick"], body=synth.IRON, room="cab",
                  ring=(150, 45)),
}


def door_strike(rng, mat, hard=1.0):
    """A tendril tip striking the door: the panel knocked (a real knock on that material), a whip's crack and a wet smack
    at the tip, the panel ringing on through its own modes."""
    d = DOOR[mat]
    panel = cut(dsp.vari(any_of(rng, d["panel"]), rng.uniform(-3, 2)), 0.35)
    w = cut(dsp.vari(any_of(rng, WHIP), rng.uniform(-4, 2)), 0.1)
    f, q = d["ring"]
    ring = knock(rng, f * rng.uniform(0.9, 1.15), 0.5 if mat == "grate" else 0.25, q=q, body=d["body"])
    x = mix(panel, lp(w, 6000) * 0.6, slap(rng, 0.07, 2000, 0.7) * 0.25, ring * (0.35 if mat == "grate" else 0.25))
    return x * hard


def door_rattle(rng, mat, count, rate=(25, 45)):
    """The door jumping in its frame after a blow: quick small knocks against the stop and the latch chattering."""
    d = DOOR[mat]
    b = Bus(count / rate[0] + 0.4)
    t = 0.0
    for i in range(count):  # each knock smaller than the last as the door settles
        if rng.random() < 0.6:
            x = cut(dsp.vari(any_of(rng, d["frame"]), rng.uniform(2, 7)), 0.06)
        else:
            x = cut(dsp.vari(any_of(rng, d["latch"]), rng.uniform(-6, 0)), 0.08)
        b.at(t, x, -4 - 2.5 * i)
        t += 1 / rng.uniform(*rate)
    return b.x


def door_drag(rng, mat, length):
    """A tendril dragged across the door: a squeaking wet stick-slip through the door's own modes."""
    d = DOOR[mat]
    k = 4.0 if mat == "wood" else 2.2
    x = synth.creak(length, env([(0, 90), (length, 160)], length), rng, body=[f * k for f in d["body"]], q=12, grit=0.4)
    return bp(x, 500, 4000) * env([(0, 0), (0.08, 1), (length * 0.7, 0.7), (length, 0)], length)


def door_room(x, mat, rng):
    return finish(dsp.room(lp(x, 7000), DOOR[mat]["room"], wet=0.22, rng=rng), DOOR_LUFS)


DOOR_LUFS = -23.0   # all attack: quieter integrated loudness leaves the knocks their peaks


def _bang_lash(rng, take, mat):
    """One flurry: one to five tendrils striking in a quick ragged figure, the door rattling after; some takes a drag."""
    count = [1, 3, 2, 5, 4, 3][take]
    b = Bus(2.4)
    t = 0.02
    for i in range(count):   # several tendrils, so the strikes tumble over each other rather than keep time
        b.at(t, door_strike(rng, mat), -1.5 * i + rng.uniform(-3, 1))
        t += rng.uniform(0.03, 0.09) if rng.random() < 0.7 else rng.uniform(0.14, 0.22)
    b.at(t, door_rattle(rng, mat, int(rng.integers(4, 9))))
    if take in (2, 5):
        L = rng.uniform(0.5, 0.8)
        b.at(t + 0.1, door_drag(rng, mat, L), -9)
    return door_room(b.x, mat, rng)


def _bang_shake(rng, take, mat):
    """Tendrils wound round the handle shaking the door: it bangs to and fro in its frame in uneven pulls."""
    d = DOOR[mat]
    L = rng.uniform(0.6, 1.2)
    b = Bus(L + 1.0)
    if take % 3 == 0:
        b.at(0, door_strike(rng, mat), -1)
    t = 0.12 if take % 3 == 0 else 0.0
    rate = rng.uniform(8, 12)
    pull = env([(0, 0.6), (L * rng.uniform(0.2, 0.5), 1), (L * 0.8, 0.7), (L, 0.35)], L + 0.2)
    i = 0
    while t < L:
        g = float(pull[min(samples(t), len(pull) - 1)])
        if i % 2 == 0:   # the door hitting its stop
            x = cut(dsp.vari(any_of(rng, d["panel"]), rng.uniform(-2, 3)), 0.12)
        else:            # and snapping back against the latch
            x = mix(cut(dsp.vari(any_of(rng, d["latch"]), rng.uniform(-5, 1)), 0.07),
                    cut(dsp.vari(any_of(rng, d["frame"]), rng.uniform(1, 5)), 0.06) * 0.6)
        b.at(t, x, 20 * np.log10(g * rng.uniform(0.6, 1.0)) - (0 if i % 2 == 0 else 4))
        t += (1 / rate) * rng.uniform(0.75, 1.3)
        i += 1
    body = synth.IRON if mat == "grate" else synth.WOOD
    hinge = synth.creak(L, env([(0, 20), (L, 35)], L), rng, body=[f * (1.6 if mat == "grate" else 1.0) for f in body], q=16)
    b.at(0.05, hinge * pull[:len(hinge)] * 0.3, -6)
    b.at(t, door_rattle(rng, mat, int(rng.integers(2, 5))), -3)
    return door_room(b.x, mat, rng)


for _mat in ("wood", "grate"):
    _what = "a car's wooden door" if _mat == "wood" else "an iron door or hatch"
    _how = "wood knocks (Kenney light and medium wood impacts)" if _mat == "wood" else \
        "sheet-iron knocks (Kenney plate impacts) ringing on through a heavy iron part's modes"
    recipe("cs-choir", "bang-door", "lash",
           f"Tendrils lashing {_what} in ragged flurries, heard from inside, the door rattling after each",
           f"""Each take is one flurry of one to five tendril strikes in a quick, uneven figure: {_how} for the panel,
           with a whip's crack and a small wet smack at the tip (they're whips of wet skin, not fists), then the door
           jumping in its frame (small knocks against the stop and the latch chattering). Two takes add a tendril
           dragged squeaking across the door. Heard from inside, in the {DOOR[_mat]['room']}'s reverb; no voices, since
           the tell carries those.""",
           sources=DOOR[_mat]["panel"] + DOOR[_mat]["frame"] + DOOR[_mat]["latch"] + WHIP, takes=6, mat=_mat, lufs=DOOR_LUFS,
           preview=lambda takes, rng: scatter(takes, rng, (0.2, 1.1)))(lambda rng, take, m=_mat: _bang_lash(rng, take, m))
    recipe("cs-choir", "bang-door", "shake",
           f"Tendrils wound round the handle shaking {_what} to and fro in its frame",
           f"""The rattle as the main event: the tendrils have hold of the handle and pull, so the door bangs against its
           stop and snaps back against the latch eight to twelve times a second in uneven pulls ({_how} alternating
           with the latch's metal clack), the hinge straining under it, a lash first on some takes. Heard from inside,
           in the {DOOR[_mat]['room']}'s reverb.""",
           sources=DOOR[_mat]["panel"] + DOOR[_mat]["frame"] + DOOR[_mat]["latch"] + WHIP, takes=6, mat=_mat, lufs=DOOR_LUFS,
           preview=lambda takes, rng: scatter(takes, rng, (0.2, 1.1)))(lambda rng, take, m=_mat: _bang_shake(rng, take, m))


# The iron door again. 'lash' and 'shake' were kept on the wooden door and judged not to sound like iron on the iron one:
# thin sheet hits, a modelled ring, and the frame's knocks made of small pinged bars (dings) don't add up to a steel door.
# These two are built from heavy steel itself: a real blow on a steel door's sheet and real heavy plate hits for the
# panel, the tendrils' strikes convolved through that panel's own recorded ring (so a lash rings the same door a bang
# does), and real metal-on-metal contacts (a latch, a click, small steel hits) for the frame and the bolt, in a small iron
# room.

STEEL_DOOR = "sfx_100_v2:metal_hit_01"      # a real blow on a steel door's sheet
PANEL = [STEEL_DOOR] + K("impactPlate_medium")
HOLLOW = K("impactPlate_heavy")            # a heavy plate's dull, hollow weight
LATCH = ["kenney_rpg-audio:metalLatch", "kenney_rpg-audio:metalClick"]
IRON_ECHO = [(0.0055 * i, 0.55 * 0.72 ** i) for i in range(1, 9)]     # flutter between close parallel plates


def iron_room(x):
    """Inside a small iron room (the cab, a steel-sided car): short and bright, with the flutter of close parallel steel
    walls. The same room for every take."""
    h = dsp.ir(0.45, 0.3, 7500, rng=np.random.default_rng(23), early=IRON_ECHO)
    return mix(x * 0.8, signal.fftconvolve(x, h).astype(np.float32) * 0.3)


def door_ring(rng, take):
    """The door's own ring: the decay of a real blow on a steel sheet or a thick plate, the door a little bigger or
    smaller take to take."""
    return dsp.vari(ring_of(PANEL[take % len(PANEL)], 0.7), rng.uniform(-1.5, 1.0))


def any_of_key(rng, keys):
    return keys[int(rng.integers(len(keys)))]


def iron_strike(rng, ring):
    """A tendril striking the steel: a wet whip's crack at the tip, that crack rung through the door's own ring, the
    panel's real bang (a steel sheet or thick plate struck), and its hollow weight under it."""
    w = cut(dsp.vari(any_of(rng, WHIP), rng.uniform(-4, 2)), 0.1)
    tip = mix(hp(w, 800), slap(rng, 0.06, 2200, 0.6) * 0.3)
    b = Bus(1.0)
    b.at(0, lp(tip, 7000), -7)
    b.at(0, through(tip, ring), -5)
    b.at(0, norm(dsp.vari(main_hit(any_of_key(rng, PANEL), 0.6), rng.uniform(-2, 1))), -4)
    b.at(0, norm(lp(main_hit(any_of_key(rng, HOLLOW), 0.4), 500)), -10)
    return b.x


def frame_knock(rng):
    """The door's edge striking its iron frame, or the bolt its keeper: a real metal-on-metal contact (a latch, a
    click, a small steel hit) dropped half an octave or more to a door's size."""
    if rng.random() < 0.5:
        x = dsp.vari(main_hit(any_of_key(rng, LATCH), 0.06), rng.uniform(-9, -4))
    else:
        t = steel_ticks()
        x = dsp.vari(t[int(rng.integers(len(t)))], rng.uniform(-12, -7))
    return norm(dsp.fade(x, 0.0005, 0.02))


def iron_rattle(rng, ring, count, rate=(22, 40)):
    """The door jumping in its frame after a blow: knocks against the stop and the bolt chattering in its keeper, each
    ringing the door a little, smaller as it settles."""
    ex = Bus(count / rate[0] + 0.3)
    t = 0.0
    for i in range(count):
        ex.at(t, frame_knock(rng), -3 - 2.5 * i)
        t += 1 / rng.uniform(*rate)
    return mix(ex.x, through(ex.x, ring) * 0.35)


def _iron_clang(rng, take):
    """One flurry of tendril strikes on a steel door, the door rattling in its frame after."""
    count = [1, 3, 2, 4, 2, 3][take]
    ring = door_ring(rng, take)
    b = Bus(2.6)
    t = 0.02
    for i in range(count):
        b.at(t, iron_strike(rng, ring), -1.5 * i + rng.uniform(-3, 1))
        t += rng.uniform(0.04, 0.1) if rng.random() < 0.65 else rng.uniform(0.15, 0.24)
    b.at(t, iron_rattle(rng, ring, int(rng.integers(4, 8))), -5)
    if take in (2, 5):    # a tendril dragged across the steel, juddering, the door ringing under it
        L = rng.uniform(0.4, 0.6)
        drag = ck.friction(rng, L, rng.uniform(70, 120), 500, 3500, shape=env([(0, 0), (0.06, 1), (L, 0)], L))
        b.at(t + 0.12, mix(drag * 0.5, through(drag, ring)), -14)
    return finish(iron_room(lp(b.x, 9000)), DOOR_LUFS)


def _iron_shake(rng, take):
    """Tendrils round the handle shaking a steel door: it bangs its stop and snaps back on the bolt in uneven pulls, every
    knock ringing the same door."""
    ring = door_ring(rng, take + 3)
    L = rng.uniform(0.6, 1.1)
    ex = Bus(L + 0.6)
    b = Bus(L + 1.2)
    if take % 3 == 0:
        b.at(0, iron_strike(rng, ring), -2)
    t = 0.14 if take % 3 == 0 else 0.0
    rate = rng.uniform(7, 10)
    pull = env([(0, 0.6), (L * rng.uniform(0.2, 0.5), 1), (L * 0.8, 0.7), (L, 0.35)], L + 0.2)
    i = 0
    while t < L:
        g = float(pull[min(samples(t), len(pull) - 1)])
        if i % 2 == 0:   # the door's weight hitting its stop: the panel's real bang, choked short by the frame
            x = norm(ck.choke(main_hit(any_of_key(rng, PANEL), 0.25), 0.012, rng.uniform(0.02, 0.04)))
            x = mix(x, norm(lp(main_hit(any_of_key(rng, HOLLOW), 0.2), 600)) * 0.5)
        else:            # and snapping back against the bolt: a hard steel clack and the latch's rattle
            x = mix(frame_knock(rng), frame_knock(rng) * 0.5)
        ex.at(t, x, 20 * np.log10(g * rng.uniform(0.6, 1.0)) - (0 if i % 2 == 0 else 3))
        t += (1 / rate) * rng.uniform(0.75, 1.3)
        i += 1
    b.at(0, mix(ex.x, through(ex.x, ring) * 0.3))
    b.at(t, iron_rattle(rng, ring, int(rng.integers(2, 5))), -4)
    return finish(iron_room(lp(b.x, 9000)), DOOR_LUFS)


_IRON_SRC = PANEL + HOLLOW + LATCH + WHIP + STEEL_TICKS


@recipe("cs-choir", "bang-door", "clang",
        "Tendrils lashing a steel door or hatch, heard from inside: real steel bangs ringing on, the door clattering in its iron frame",
        """Built from heavy steel, not thin sheet. Each strike is a wet whip's crack at the tendril's tip convolved
        through the door's own ring (the decay of a real blow on a steel door, sfx_100's metal hit, or a Kenney thick
        plate), so every lash rings the same door, with the panel's real bang and a heavy plate's hollow weight under it.
        One to four strikes in a ragged flurry, then the door jumps in its frame: real metal-on-metal knocks (Kenney's
        latch and click, small steel hits, dropped to a door's size) against the stop and the bolt, each ringing the
        door a little. Two takes drag a tendril juddering across the steel. Heard from inside a small iron room: short,
        bright, the flutter of close steel walls.""",
        sources=_IRON_SRC, takes=6, mat="grate", lufs=DOOR_LUFS, preview=lambda takes, rng: scatter(takes, rng, (0.2, 1.1)))
def choir_bang_clang(rng, take):
    return _iron_clang(rng, take)


@recipe("cs-choir", "bang-door", "rattle",
        "Tendrils wound round the handle shaking a steel door or hatch: it bangs its iron stop and clacks back on the bolt",
        """The rattle as the main event, on steel. Pulled to and fro seven to ten times a second in uneven pulls, the
        door's weight hits its stop (a real steel bang choked short by the frame, a heavy plate's hollow under it) and
        snaps back on the bolt (real metal-on-metal clacks: Kenney's latch and click, small steel hits, dropped half an
        octave), and every knock is convolved through the door's own recorded ring so the whole door sings under the
        shaking. A lash first on some takes; the bolt chattering as it settles. Heard from inside a small iron room.""",
        sources=_IRON_SRC, takes=6, mat="grate", lufs=DOOR_LUFS, preview=lambda takes, rng: scatter(takes, rng, (0.2, 1.1)))
def choir_bang_rattle(rng, take):
    return _iron_shake(rng, take)


@recipe("cs-choir", "hit", "membrane",
        "A blow on a bell of wet skin: a bending drumhead knock with a jelly wobble, and its song knocked off the note",
        """The club lands on a taut wet membrane: a wet slap and a soft heavy knock, rung through a drumhead's modes
        tuned to the lullaby's notes, the pitch bending down as the struck skin relaxes and wobbling like jelly. The
        child's mouth was singing (the tell's throat): the note lurches down off pitch with a crack in the voice, shakes
        with the body, and only half recovers, left a quarter-tone flat. A puff of air is knocked out through the bell.""",
        sources=K("impactSoft_heavy"), takes=4, lufs=-22)
def choir_hit(rng, take):
    L = 1.3
    root = [Ab - 36, D - 24, Eb - 24, G - 36][take]
    note = [D + 12, Ab, Eb + 12, F + 12][take]
    b = Bus(L)
    b.at(0, slap(rng, 0.1, 1800, wet=0.8), -4)
    b.at(0, cut(dsp.vari(any_of(rng, K("impactSoft_heavy")), rng.uniform(-3, 0)), 0.3), -10)
    b.at(0, drumhead(rng, hz(root) * rng.uniform(0.98, 1.02), 0.9, q=10, drop=rng.uniform(3, 5)), -1)
    b.at(0.01, squelch(rng, 0.3, 300, 1200), -11)
    t = np.arange(samples(L)) / SR
    wob = rng.uniform(7, 9)
    st = env([(0, 0), (0.03, -1.7), (0.5, -0.6), (L, -0.5)], L) + 0.6 * np.exp(-t / 0.3) * np.sin(2 * np.pi * wob * t)
    f0 = (hz(note) * 2 ** (st / 12)).astype(np.float32)
    v = ghost(rng, f0, [(0, "u"), (0.04, "o"), (0.35, "u")])
    crack = synth.glottis(f0 * 0.5, L, rng, jitter=0.02, shimmer=0.2, sub=0.8, oq=0.5)
    crack = synth.tract(crack, "o", "child", breath=0.3, rng=rng) * env([(0, 0), (0.02, 1), (0.12, 0)], L)[:len(crack)]
    v = (v * env([(0, 0.5), (0.02, 1), (0.15, 0.55), (L * 0.75, 0.25), (L, 0)], L)[:len(v)] + crack * 0.5)
    b.at(0, v, -3)
    puff = synth.breath(0.35, "h", "child", rng, shape=env([(0, 0), (0.03, 1), (0.35, 0)], 0.35))
    b.at(0.02, hp(puff, 500), -12)
    y = dsp.room(b.x, "box", wet=0.12, rng=rng)
    return finish(y * env([(0, 1), (L - 0.3, 1), (L + 0.1, 0)], L + 0.3)[:len(y)], -22)


@recipe("cs-choir", "hit", "toll",
        "A blow that rings its bell like a bell, choked by its own wet skin; its song leaps up a tritone in a cry and slides down",
        """The club strikes a bell of skin and it rings like a bell for an instant (a knock through a church bell's
        partials, minor-third tierce and all, pitched to the lullaby's notes), then its own wet skin chokes it and the
        pitch rises as the membrane tightens in pain. The child's mouth (the tell's throat) cries out: the note it was
        singing leaps up a tritone, opens to 'ah', and slides back down past where it started. A wet slap at the
        strike, and a gasp knocked out of it.""",
        takes=4, lufs=-22)
def choir_hit_toll(rng, take):
    L = 1.3
    note = [D + 12, Ab, Eb + 12, F + 12][take]
    prime = hz([D, Ab - 12, Eb, G - 12][take]) * rng.uniform(0.99, 1.01)
    b = Bus(L)
    b.at(0, slap(rng, 0.1, 2200, wet=0.7), -6)
    ring = knock(rng, prime * 0.5, 1.2, q=70, body=[p * 100 for p in BELL])
    ring = bend(ring * env([(0, 1), (0.08, 0.6), (0.7, 0.05), (1.2, 0)], 1.2, "exp"), env([(0, 0), (0.5, 1.6)], 1.2))
    b.at(0, ring, -1)
    b.at(0, drumhead(rng, prime * 0.5, 0.4, q=6, drop=1), -9)
    t = np.arange(samples(L)) / SR
    st = env([(0, 0), (0.05, 6), (0.22, 6.3), (0.8, -1.2), (L, -1.6)], L) + 0.25 * np.sin(2 * np.pi * 6 * t)
    v = ghost(rng, (hz(note) * 2 ** (st / 12)).astype(np.float32), [(0, "u"), (0.06, "a"), (0.5, "o"), (L, "u")])
    b.at(0, v * env([(0, 0.4), (0.05, 1), (0.25, 0.8), (L * 0.8, 0.15), (L, 0)], L), -4)
    gasp = synth.breath(0.3, "a", "child", rng, shape=env([(0, 0), (0.25, 1), (0.3, 0)], 0.3))
    b.at(0.75, lp(hp(gasp, 500), 5000), -21)
    y = dsp.room(b.x, "box", wet=0.12, rng=rng)
    return finish(y * env([(0, 1), (L - 0.3, 1), (L + 0.1, 0)], L + 0.3)[:len(y)], -22)


@recipe("cs-choir", "disperse", "sink",
        "The swarm's round sagging down together like a tape running out as it drifts off, over an endless fall; one far voice left on the wrong note",
        """Four of the tell's round voices (the Locrian lullaby, imported) singing together, then all of them sagging in
        pitch at once like a tape slowing down, a tritone or a fifth over three seconds, while they drift away: darker,
        wetter and quieter, their bells' swim strokes fading off. Under them, far off, the tell's Shepard rise (also
        imported) run the other way: an endless fall that never arrives either. Last, far off, one voice sings a single
        note on the mode's flat second (or its tritone) and stops: the tune never comes home.""",
        sources=FLAP, takes=2)
def choir_disperse_sink(rng, take):
    voices = [round_voice(rng, k, octave, cents) for k, octave, cents in VOICES]
    s = rng.uniform(1.0, LOOP - 4.5)
    grp = sum(dsp.trim(v, s, 3.8) for v in voices) * 0.5
    depth = -6.0 if take == 0 else -7.0
    y = bend(grp, env([(0, 0), (0.7, 0), (3.8, depth)], 3.8))
    m = len(y)
    Ly = m / SR
    wet = dsp.room(y, "night", wet=0.75, rng=rng)[:m]
    k = env([(0, 0), (Ly, 1)], Ly)[:m]
    y = (y * (1 - k) + wet * k) * env([(0, 1), (0.6, 1), (Ly, 0.06)], Ly, "exp")[:m]
    y = moving_lp(y, env([(0, 9000), (Ly, 1400)], Ly, "exp"))
    y = dsp.fade(y, 0.01, 0.8)
    b = Bus(Ly + 2.6)
    b.at(0, y)
    Lf = Ly + 2.0   # the tell's Shepard rise with its period negated glides down forever; centred low so no voice pops in
    fall = shepard_stack(rng, Lf, 75.0 * 2 ** rng.uniform(0, 1), -9.0, centre=500, sigma=0.9, bell=600)
    fall = dsp.distance(fall, 60, rng)[:samples(Lf)]
    b.at(0, fall * env([(0, 0), (1.5, 1), (Ly, 0.7), (Lf, 0)], Lf)[:len(fall)], -7)
    t = 0.3
    for i in range(6):
        st = swim(rng, rng.uniform(180, 250))
        b.at(t, lp(st, 2500 - 300 * i), -8 - 3 * i)
        t += rng.uniform(0.5, 0.75)
    last = Eb + 12 if take == 0 else Ab + 12
    f0 = env([(0, hz(last) * 0.97), (0.12, hz(last)), (1.4, hz(last) * 2 ** (-0.3 / 12))], 1.4, "exp")
    v = ghost(rng, f0.astype(np.float32), "u") * env([(0, 0), (0.25, 1), (1.1, 0.8), (1.4, 0)], 1.4)
    b.at(Ly - 0.5, dsp.distance(v, 80, rng), 2)
    return finish(b.x)


@recipe("cs-choir", "disperse", "hush",
        "One voice sings the lullaby's last bar but stops a step short of home, while the swarm hushes all round and drifts away",
        """One of the Choir's throats, near, sings the lullaby's last bar slower and slower (A flat, G, E flat) and holds
        the E flat: the D that would end it never comes. All round, the swarm hushes: long breathy 'shh's from child
        mouths, each further off than the last as they drift away, and a few swim strokes of their bells fading into
        the dark.""",
        sources=FLAP, takes=2)
def choir_disperse_hush(rng, take):
    o = 0 if take == 0 else 12
    notes = [(Ab + o, 1.5), (G + o, 0.5), (Eb + o, 2.6)]
    f0, onsets, total = melody_f0(notes, BEAT * rng.uniform(1.15, 1.3), rng, scoop=60, glide=0.12, vib=(4.8, 0.007))
    rit = env([(0, 0), (total, -0.35)], total)
    v = ghost(rng, (f0 * 2 ** (dsp.fit(rit, len(f0)) / 12)).astype(np.float32), [(0, "u"), (onsets[2], "o"), (total - 1, "u")])
    v = v * env([(0, 0), (0.2, 1), (total - 1.5, 0.8), (total, 0)], total)[:len(v)]
    b = Bus(total + 1.5)
    b.at(0.3, dsp.distance(v, 6, rng), -3)
    t = 0.0
    for i in range(7):
        L = rng.uniform(1.0, 1.7)
        sh = synth.breath(L, rng.choice(["i", "e"]), "child", rng, shape=env([(0, 0), (L * 0.3, 1), (L, 0)], L))
        sh = hp(sh, 1800)
        b.at(t, dsp.distance(sh, 4 + 7 * i, rng), 4)
        t += rng.uniform(0.4, 0.8)
    for i in range(3):
        b.at(rng.uniform(0.5, total - 1), lp(swim(rng, rng.uniform(180, 250)), 1800), -14 - 2 * i)
    return finish(b.x)


# ---- The Gaunt ----------------------------------------------------------------------------------------------------------
# It listens for silence and makes none: every blow take starts on the impact itself, nothing before it.

CRACK = ["sfx_100_v2:misc_35", "sfx_100_v2:misc_34"]
BLOW_LUFS = -23.0   # all attack: a quieter integrated loudness leaves the hit its peak


def bark_debris(rng, length, density=60):
    """Crumbs of bark and twig ends pattering down after a blow."""
    return twigs(rng, length, density, 1500, 7000) * env([(0, 1), (length, 0.02)], length, "exp")


def joint_creak(rng, length, rates, k=1.0, q=15):
    """Its knees bending: a dry stick-slip groan through a hollow trunk's modes."""
    return synth.creak(length, env(rates, length), rng, body=[f * k for f in synth.BARK], q=q) * \
        env([(0, 0), (0.05, 1), (length, 0)], length)


@recipe("cs-gaunt", "blow", "branch",
        "Its long limb whipping down like a dry branch: a hard, cracking strike, the limb ringing, bark crumbs falling",
        """Starts on the impact, nothing before it. The strike is hard dry wood, not a fist: a crack (a sharp burst rung
        through a split branch's modes), a medium wood impact and a heavy whip's slap pitched down for the limb's
        weight. Under it the crewman takes it (a dropping sub-thud and a coat struck). The limb rings on like a long
        hollow branch, bark crumbs patter down, and its knees creak as it takes the weight back over its back.""",
        sources=K("impactWood_medium") + WHIP + CLOTH, takes=3, lufs=BLOW_LUFS)
def gaunt_blow_branch(rng, take):
    b = Bus(1.5)
    b.at(0, snap(rng, 0.12, rng.uniform(1300, 1900)), 0)
    b.at(0, rec(rng, K("impactWood_medium"), rng.uniform(-3, 0), 0.3), 0)
    b.at(0, rec(rng, WHIP, rng.uniform(-7, -4), 0.15), -4)
    b.at(0.004, thud(rng, rng.uniform(55, 70), 0.4, 1.0), -11)
    b.at(0.004, rec(rng, CLOTH, rng.uniform(-4, -1), 0.35), -5)
    b.at(0.002, knock(rng, rng.uniform(110, 150), 0.45, q=22), -5)
    b.at(0.03, bark_debris(rng, 0.7), -13)
    L = rng.uniform(0.4, 0.6)
    b.at(rng.uniform(0.3, 0.45), joint_creak(rng, L, [(0, 25), (L, 60)], 0.8), -17)
    return finish(hp(dsp.room(b.x, "car", wet=0.12, rng=rng), 35), BLOW_LUFS)


@recipe("cs-gaunt", "blow", "stake",
        "Its stilt driven down like a stake: a hollow-log knock, its joints locking under the shock, the boards taking it",
        """Starts on the impact. The limb lands with all its height behind it: a hollow dry trunk knocked (a burst rung
        through a hollow log's modes, with a plank and a heavy wood impact pitched down), its three knee joints locking
        in a quick dry ripple as the shock goes through them, and the crewman driven into the boards (a deep thud,
        floorboards, a coat). Then the bark strains, creaking slowly as it leans its weight in, and a few crumbs fall.""",
        sources=K("impactPlank_medium") + K("impactWood_heavy") + K("footstep_wood") + CLOTH, takes=3, lufs=BLOW_LUFS)
def gaunt_blow_stake(rng, take):
    b = Bus(2.0)
    b.at(0, knock(rng, rng.uniform(95, 125), 0.6, q=30), 0)
    b.at(0, rec(rng, K("impactPlank_medium"), rng.uniform(-4, -2), 0.5), -1)
    b.at(0, rec(rng, K("impactWood_heavy"), rng.uniform(-3, 0), 0.4), -2)
    for i, dt in enumerate(np.cumsum(rng.uniform(0.012, 0.03, 3))):
        b.at(dt, snap(rng, 0.06, rng.uniform(800, 1400)), -3 - 2 * i)
    b.at(0.005, thud(rng, 50, 0.5, 1.0), -11)
    b.at(0.01, rec(rng, K("footstep_wood"), -5, 0.25), -4)
    b.at(0.005, rec(rng, CLOTH, -3, 0.3), -7)
    L = rng.uniform(0.7, 1.0)
    b.at(0.25, joint_creak(rng, L, [(0, 10), (L * 0.6, 25), (L, 8)], 0.7, q=18), -13)
    b.at(0.25 + L * 0.7, bark_debris(rng, 0.5, 30), -16)
    return finish(hp(dsp.room(b.x, "car", wet=0.12, rng=rng), 35), BLOW_LUFS)


def needle_breath(rng, length, inhale=False):
    """Breath through a pucker of needle teeth: a thin dry hiss with a whistle in it."""
    shape = [(0, 0), (length * 0.8, 1), (length, 0)] if inhale else [(0, 0), (0.12, 1), (length, 0)]
    br = hp(synth.breath(length, "i", "woman", rng, shape=env(shape, length)), 1600)
    fc = env([(0, 2200), (length, 3300)] if inhale else [(0, 3200), (length, 2300)], length, "exp")
    whistle = dsp.sweep_filter(synth.noise(length, rng), "bp", fc, q=40) * env(shape, length)
    return br * 0.8 + norm(whistle) * 0.35


def heap(rng, b, t, weight=1.0):
    """Branches landing in a heap: a hollow thud, a plank, a burst of twigs knocking together, then settling."""
    b.at(t, thud(rng, 48, 0.5, weight), -9)
    b.at(t, rec(rng, K("impactPlank_medium"), rng.uniform(-4, -2), 0.6), -3)
    b.at(t, twigs(rng, 1.4, 160, 900, 6000) * env([(0, 1), (0.25, 0.5), (1.4, 0)], 1.4), -6)
    for i in range(8):
        b.at(t + rng.uniform(0.02, 0.6), rec(rng, K("impactWood_light"), rng.uniform(0, 6), 0.12), -9 - i)
    b.at(t + 0.8, twigs(rng, 1.0, 12, 1500, 6000) * env([(0, 1), (1.0, 0)], 1.0), -16)


@recipe("cs-gaunt", "death", "fold",
        "Its four stilts giving one after another, cracking and folding, until it drops into a heap of branches",
        """Each of its four legs goes in turn: a dry crack (a split branch and a wood break pitched down), the knee
        creaking as it bends the wrong way, the body dropping a stage with a hollow knock. Then the whole thing lands as
        the heap of branches it sleeps as: a hollow thud, a plank, twigs and light wood knocking together and settling.
        Last, the only sound its mouth ever makes: a thin dry hiss out through the needle teeth, with a whistle in it.""",
        sources=CRACK + K("impactPlank_medium") + K("impactWood_light"), takes=3, lufs=-22)
def gaunt_death_fold(rng, take):
    b = Bus(5.5)
    t = 0.0
    for leg in range(4):
        b.at(t, snap(rng, 0.1, rng.uniform(700, 1500)), -4)
        b.at(t, rec(rng, CRACK, rng.uniform(-6, -2), 0.35), -8)
        L = rng.uniform(0.3, 0.55)
        b.at(t + 0.03, joint_creak(rng, L, [(0, 70), (L, 12)], rng.uniform(0.8, 1.1), q=16), -10)
        b.at(t + L * 0.8, knock(rng, rng.uniform(90, 130), 0.4, q=20), -8 - 2 * leg)
        b.at(t + L * 0.8, thud(rng, 60, 0.3, 0.6), -12)
        t += rng.uniform(0.45, 0.8)
    heap(rng, b, t)
    b.at(t + rng.uniform(1.0, 1.4), needle_breath(rng, 1.3), -15)
    return finish(hp(dsp.room(b.x, "night", wet=0.12, rng=rng), 35), -22)


@recipe("cs-gaunt", "death", "timber",
        "A hiss drawn in through needle teeth, its frame straining like a trunk about to go, one great crack and the crash",
        """It draws one thin breath in through its needle teeth (a dry hiss with a rising whistle). Its frame strains
        like a trunk about to split, a stick-slip groan through a hollow log that quickens and tightens with small
        cracks running through it, then breaks in one crack (split branch, a wood break, a heavy wood impact pitched
        down). A moment of branches rushing through the air, then the crash into a heap: a hollow thud, a plank, twigs
        and light wood knocking together and settling.""",
        sources=CRACK + K("impactWood_heavy") + K("impactPlank_medium") + K("impactWood_light"), takes=3)
def gaunt_death_timber(rng, take):
    b = Bus(5.5)
    b.at(0, needle_breath(rng, 0.6, inhale=True), -12)
    Ls = rng.uniform(1.2, 1.6)
    strain = synth.creak(Ls, env([(0, 6), (Ls, 70)], Ls, "exp"), rng, body=[f * 0.6 for f in synth.BARK], q=20, grit=0.5)
    b.at(0.4, strain * env([(0, 0), (Ls * 0.3, 0.6), (Ls, 1)], Ls), -6)
    for i, f in enumerate(np.sort(rng.uniform(0.3, 1.0, 6))):
        b.at(0.4 + Ls * f, snap(rng, 0.05, rng.uniform(1500, 3000)), -15 + i)
    tb = 0.4 + Ls
    b.at(tb, snap(rng, 0.15, rng.uniform(800, 1000)), -2)
    b.at(tb, rec(rng, CRACK, rng.uniform(-5, -3), 0.5), -2)
    b.at(tb + 0.01, snap(rng, 0.1, rng.uniform(1400, 1800)), -6)
    b.at(tb, rec(rng, K("impactWood_heavy"), rng.uniform(-6, -4), 0.4), -4)
    tf = tb + rng.uniform(0.3, 0.45)
    b.at(tb + 0.05, synth.rustle(tf - tb, 150, rng, f=(800, 5000)) * env([(0, 0), (tf - tb, 1)], tf - tb), -14)
    heap(rng, b, tf, 1.2)
    return finish(hp(dsp.room(b.x, "night", wet=0.12, rng=rng), 35))


# ---- The Soot Children --------------------------------------------------------------------------------------------------
# Wordless: a child's breath, a child's throat bent wrong. Its call is a recorded child's voice still to come (tell line).

def throat(rng, f0, vowels, k=1.0, breath=0.25, rough=0.0, sub=0.0, jitter=0.008, shimmer=0.1):
    """A throat on a pitch curve through a tract that can change size as it goes (morph_tract)."""
    f0 = np.asarray(f0, np.float32)
    g = synth.glottis(f0, len(f0) / SR, rng, jitter=jitter, shimmer=shimmer, sub=sub, rough=rough, oq=0.6)
    return morph_tract(g, vowels, k, breath=breath, rng=rng)


def airflow(rng, length, vowels, k, shape):
    """Breath through a mouth that can change size: noise through the tract, shaped as an inhale or exhale."""
    return morph_tract(synth.noise(length, rng), vowels, k) * env(shape, length)


def soot(rng, length, rate):
    """Soot and ash sifting: the finest dry crackle (a curve of grains a second)."""
    return synth.crackle(length, rate, rng, size=(0.0001, 0.0008), hi=2500)


@recipe("cs-soot-children", "turn", "sob",
        "A child's little sob, and on the third the throat behind it grows into something big while the pitch stays small",
        """Two small wordless sobs with a catch of breath between, a child's throat (a synthesised glottis through a
        child's vocal tract, very breathy). The third goes on too long: its pitch sags only a little but the tract
        behind it lengthens to a beast's, so the formants fall away under a child's pitch, and the voice splits into
        two (period doubling, a fry). It ends in a long exhale through the big throat, with soot sifting dry through it
        and a small crack of knuckles.""",
        takes=3)
def soot_turn_sob(rng, take):
    b = Bus(4.0)
    base = rng.uniform(360, 430)
    t = 0.05
    for i in range(2):
        L = rng.uniform(0.22, 0.32)
        v = throat(rng, env([(0, base * 1.08), (L, base * 0.85)], L, "exp"), [(0, "a"), (L, "u")], 1.0, breath=0.45)
        b.at(t, v * env([(0, 0), (0.03, 1), (L * 0.6, 0.7), (L, 0)], L), -4)
        Li = 0.18
        b.at(t + L + 0.03, hp(airflow(rng, Li, "h", 1.0, [(0, 0), (Li * 0.7, 1), (Li, 0)]), 600), -14)
        t += L + rng.uniform(0.25, 0.35)
    L = rng.uniform(1.8, 2.2)
    f0 = env([(0, base * 1.05), (0.25, base * 0.95), (L * 0.6, base * 0.82), (L, base * 0.6)], L, "exp")
    k = env([(0, 1.0), (0.3, 1.0), (L * 0.75, 0.42), (L, 0.4)], L)
    vow = [(0, "a"), (L * 0.5, "o"), (L, "u")]
    clean = throat(rng, f0, vow, k, breath=0.35)
    split = throat(rng, f0, vow, k, breath=0.3, sub=0.8, rough=0.5, jitter=0.02)
    x = env([(0, 0), (0.4, 0), (L * 0.8, 1), (L, 1)], L)
    v = (clean * (1 - x) + split * x) * env([(0, 0), (0.04, 1), (L * 0.8, 0.8), (L, 0)], L)
    b.at(t, v, -3)
    Le = 1.1
    b.at(t + L - 0.15, airflow(rng, Le, [(0, "a"), (Le, "h")], 0.4, [(0, 0), (0.1, 1), (Le, 0)]), -9)
    b.at(0, soot(rng, t + L + Le, env([(0, 5), (t, 20), (t + L + Le, 300)], t + L + Le)), -16)
    b.at(t + L * rng.uniform(0.5, 0.8), snap(rng, 0.03, rng.uniform(2500, 3500)), -18)
    return finish(dsp.room(b.x, "car", wet=0.1, rng=rng))


@recipe("cs-soot-children", "turn", "breath",
        "A child's quick frightened breathing that slows and deepens until it's a long rattling draw through lungs too big for it",
        """Wordless and unvoiced at first: a child's quick, scared breathing (noise through a child's mouth), each breath
        a little slower and longer than the last while the mouth behind it grows (the tract lengthening to a beast's),
        so it deepens while it slows. The last two catch on a low rattle in the throat, and the last is a long draw in
        that no child could take. Blackened fingers crack as they curl; soot sifts.""",
        takes=3)
def soot_turn_breath(rng, take):
    b = Bus(5.0)
    t = 0.0
    nb = 6
    for i in range(nb):
        p = i / (nb - 1)
        k = 1.0 - 0.58 * p ** 1.4
        Lin, Lout = 0.15 + 0.85 * p ** 2.2, 0.17 + 0.45 * p ** 2
        vin = rng.choice(["h", "a"])
        inhale = airflow(rng, Lin, vin, k, [(0, 0), (Lin * 0.85, 1), (Lin, 0)])
        if p > 0.6:   # the draw catching on a rattle low in the throat
            f0 = env([(0, rng.uniform(55, 70)), (Lin, rng.uniform(45, 55))], Lin)
            rattle = throat(rng, f0, vin, k, breath=0.6, rough=0.8, sub=0.6, jitter=0.03, shimmer=0.3)
            inhale = inhale + rattle * env([(0, 0), (Lin * 0.5, 0.6), (Lin, 0)], Lin) * 0.6
        b.at(t, inhale, -6 + 4 * p)
        t += Lin + 0.02
        b.at(t, airflow(rng, Lout, "h", k, [(0, 0), (0.04, 1), (Lout, 0)]), -9 + 3 * p)
        t += Lout + 0.05 + 0.25 * p
    for c in np.sort(rng.uniform(t * 0.4, t, 4)):
        b.at(c, snap(rng, 0.03, rng.uniform(2500, 4000)), -18)
    b.at(0, soot(rng, t, env([(0, 5), (t, 250)], t)), -17)
    return finish(dsp.room(b.x, "car", wet=0.1, rng=rng))


@recipe("cs-soot-children", "lunge", "call",
        "Its call's rising 'ah?' bursting out with a second, huge throat under it, then small feet and its weight landing on you",
        """No words: a child's 'ah' with the same rising end as its call for help (a question's lift), suddenly sung in
        parallel by a second throat an octave and a tritone below with a beast's tract and a snarl's roughness, both
        overdriven. Small feet on the boards (wood footsteps pitched up), its rags rushing, and the weight of it landing
        on you: a light thud, a coat, both blackened hands slapping on, and a puff of soot shaken off.""",
        sources=K("footstep_wood") + CLOTH, takes=2)
def soot_lunge_call(rng, take):
    b = Bus(1.8)
    L = rng.uniform(0.42, 0.55)
    base = rng.uniform(400, 460)
    f0 = env([(0, base), (L * 0.3, base * 1.05), (L, base * 1.45)], L, "exp")
    child = throat(rng, f0, [(0, "a"), (L, "e")], 1.0, breath=0.3)
    under = throat(rng, f0 / (2 * 2 ** 0.5), [(0, "a"), (L, "o")], 0.48, breath=0.2, rough=0.6, sub=0.4)
    v = dsp.saturate(child * 0.8 + under * 0.9, 8) * env([(0, 0), (0.015, 1), (L * 0.7, 0.9), (L, 0)], L)
    b.at(0, v, -1)
    for dt in (0.05, 0.05 + rng.uniform(0.1, 0.14)):
        b.at(dt, rec(rng, K("footstep_wood"), rng.uniform(3, 5), 0.15), -10)
    b.at(0.08, rec(rng, ["kenney_rpg-audio:cloth3"], 2, 0.35), -8)
    tl = L + rng.uniform(0.0, 0.1)
    b.at(tl, thud(rng, 85, 0.3, 0.7), -7)
    b.at(tl, rec(rng, CLOTH, 0, 0.35), -5)
    b.at(tl, slap(rng, 0.08, 3000, wet=0.1), -6)
    b.at(tl + rng.uniform(0.02, 0.05), slap(rng, 0.08, 2800, wet=0.1), -8)
    b.at(tl, lp(synth.noise(0.4, rng), 1500) * env([(0, 0), (0.02, 1), (0.4, 0)], 0.4) + soot(rng, 0.4, 300) * 2, -14)
    return finish(dsp.room(b.x, "car", wet=0.12, rng=rng))


@recipe("cs-soot-children", "lunge", "scramble",
        "It scrambles at you on all fours, too fast, lands on you, and draws a breath through a throat too long for a child",
        """The child makes no sound as it comes: hands and feet slapping and knocking on the boards (a dry skin slap on
        light wood, wood footsteps pitched up) faster and faster, its rags rustling. It lands on you (a light thud, a
        coat, hands), and then draws one breath in, ready to drink: a child's gasp, but through a mouth and throat far too
        long for it, so it comes out low and hollow.""",
        sources=K("impactWood_light") + K("footstep_wood") + CLOTH, takes=2)
def soot_lunge_scramble(rng, take):
    b = Bus(1.8)
    t, gap, i = 0.0, rng.uniform(0.09, 0.11), 0
    while t < 0.6:
        if i % 2 == 0:
            b.at(t, slap(rng, 0.05, 3500, wet=0.05), -8)
            b.at(t, rec(rng, K("impactWood_light"), rng.uniform(5, 8), 0.08), -12)
        else:
            b.at(t, rec(rng, K("footstep_wood"), rng.uniform(2, 4), 0.12), -9)
        t += gap * rng.uniform(0.75, 1.2)
        gap *= 0.86
        i += 1
    b.at(0, synth.rustle(t, 300, rng, f=(500, 4000)) * env([(0, 0.3), (t, 1)], t), -16)
    tl = t + 0.04
    b.at(tl, thud(rng, 85, 0.3, 0.8), -7)
    b.at(tl, rec(rng, CLOTH, 0, 0.35), -5)
    b.at(tl + 0.01, slap(rng, 0.08, 2800, wet=0.1), -7)
    Lg = rng.uniform(0.4, 0.5)
    gasp = airflow(rng, Lg, [(0, "h"), (Lg, "a")], 0.55, [(0, 0), (Lg * 0.85, 1), (Lg, 0)])
    b.at(tl + 0.12, gasp, -5)
    return finish(dsp.room(b.x, "car", wet=0.12, rng=rng))


def gulp(rng, size=1.0):
    """A swallow: the tongue's click, the gulp dropping down the throat (a falling ring), a wet squeeze behind it.
    `size` above 1 is a bigger throat than the body it's in."""
    L = 0.25
    t = np.arange(samples(L)) / SR
    f = (330 / size) * (0.5 + 0.5 * np.exp(-t / 0.05))
    glunk = np.sin(2 * np.pi * np.cumsum(f) / SR) * env([(0, 0), (0.004, 1), (0.03, 0.6), (L, 0)], L, "exp")
    b = Bus(L + 0.05)
    b.at(0, synth.click(rng.uniform(1200, 1800), q=5, length=0.02, rng=rng), -10)
    b.at(0.012, glunk.astype(np.float32), -2)
    b.at(0.01, squelch(rng, 0.15, 200 / size, 700 / size, depth=0.4), -8)
    b.at(0.015, synth.thump(110 / size, 0.12, drop=0.4), -6)
    return b.x


def suck(rng, length=0.14):
    """Lips sealed on skin, drawing: a small wet seal smack and a squeak of suction pulling in."""
    b = Bus(length + 0.05)
    b.at(0, slap(rng, 0.03, 2500, wet=0.6), -12)
    b.at(0.01, squelch(rng, length, rng.uniform(600, 800), rng.uniform(1600, 2400), depth=0.6), -4)
    return b.x


def nose(rng, length, out=True, k=1.0):
    """A quick breath through a child's nose (noise through a closed-mouth hum's tract)."""
    shape = [(0, 0), (0.03, 1), (length, 0)] if out else [(0, 0), (length * 0.8, 1), (length, 0)]
    return hp(airflow(rng, length, "m", k, shape), 400)


CHANT = [G, Eb + 1, Ab + 1, G, Eb + 1]   # sol-mi-la-sol-mi: G, E, A, G, E, the playground chant's notes


@recipe("cs-soot-children", "drink", "hum",
        "Swallowing in slow gulps, breathing through its nose, and between the gulps humming a playground chant, contented",
        """Close and dry, as the pinned crewman hears it: lips sealing and drawing (a wet smack and a squeak of
        suction), then a swallow (a tongue click and a gulp dropping down the throat, a falling ring with a wet squeeze),
        a breath out through the nose, and between swallows a child humming, mouth closed, the notes of the playground
        chant (sol-mi-la-sol-mi) a phrase at a time, sagging a little flat at the end of each as the next gulp cuts it
        off. Now and then the cloth of the pin shifts. One seamless 12-second cycle.""",
        sources=CLOTH, takes=1, loop=True)
def soot_drink_hum(rng, take):
    T = 12.0
    b = Bus(T + 3)
    t = 0.2
    note = 0
    while t < T:
        for _ in range(1 if rng.random() < 0.7 else 2):   # now and then it takes two swallows before it breathes
            b.at(t, suck(rng), -2)
            t += rng.uniform(0.16, 0.22)
            b.at(t, gulp(rng, rng.uniform(1.0, 1.25)), 0)
            t += rng.uniform(0.25, 0.35)
        Ln = rng.uniform(0.22, 0.3)
        b.at(t, nose(rng, Ln, True), -12)
        t += Ln + 0.05
        if rng.random() < 0.3:   # and sometimes only breathes
            t += rng.uniform(0.3, 0.6)
            continue
        cnt = int(rng.integers(1, 4))
        notes = [(CHANT[(note + j) % len(CHANT)], 1) for j in range(cnt)]
        note += cnt
        f0, onsets, total = melody_f0(notes, rng.uniform(0.33, 0.42), rng, scoop=30, glide=0.05, vib=(5.5, 0.004), drift=8)
        f0 = f0 * 2 ** (env([(0, 0), (total * 0.6, 0), (total, -0.5)], total)[:len(f0)] / 12)
        hum = throat(rng, f0, "m", 1.0, breath=0.15) * env([(0, 0), (0.06, 1), (total - 0.08, 0.8), (total, 0)], total)
        b.at(t, lp(hum, 1800), -8)
        t += total + 0.05
        Li = rng.uniform(0.18, 0.25)
        b.at(t, nose(rng, Li, False), -13)
        t += Li + rng.uniform(0.05, 0.15)
    for c in rng.uniform(0, T, 2):
        b.at(c, rec(rng, CLOTH, rng.uniform(-4, -1), 0.4), -16)
    y = dsp.room(b.x, "box", wet=0.1, rng=rng)
    return finish(dsp.wrap(y, samples(T)))


@recipe("cs-soot-children", "drink", "greedy",
        "Greedy wet suckling in bursts, gulps from a throat too deep for a child, quick panting through the nose between",
        """Close and dry: bursts of fast suckling (lips sealed on skin, three or four draws a second, each a wet smack and
        a squeak of suction), each burst ending in two swallows from a throat much too deep for the child (the gulp's
        ring and thump pitched well down), then quick shallow panting through a small nose before the next burst. No
        voice, no chewing: drinking. One seamless 10-second cycle.""",
        takes=1, loop=True)
def soot_drink_greedy(rng, take):
    T = 10.0
    b = Bus(T + 3)
    t = 0.1
    while t < T:
        L = rng.uniform(1.2, 2.0)
        end = t + L
        while t < end:
            b.at(t, suck(rng, rng.uniform(0.1, 0.16)), rng.uniform(-4, 0))
            t += 1 / rng.uniform(3.4, 4.6)
        for _ in range(2):
            b.at(t, gulp(rng, rng.uniform(1.8, 2.2)), 0)
            t += rng.uniform(0.28, 0.36)
        for _ in range(int(rng.integers(3, 6))):
            Ln = rng.uniform(0.09, 0.13)
            b.at(t, lp(nose(rng, Ln, False), 3500), -15)
            b.at(t + Ln, lp(nose(rng, Ln, True), 3500), -14)
            t += 2 * Ln + 0.02
        t += rng.uniform(0.05, 0.2)
    y = dsp.room(b.x, "box", wet=0.1, rng=rng)
    return finish(dsp.wrap(y, samples(T)))


CHAR = ["sfx_100_v2:stones_01", "sfx_100_v2:stones_03", "sfx_100_v2:misc_34"]


@recipe("cs-soot-children", "death", "ash",
        "Its last breath out goes on far longer than a child's could, turning to ash as it goes, the body crumbling like charcoal",
        """A child's sigh out (a breathy 'ah' through a child's tract, voiced only at first) that keeps going for three
        seconds and more, getting drier and grainier until the breath has turned into sifting ash (a fine dry crackle
        that thickens as the breath thins). The body gives way under it like burnt wood: soft crunches of charcoal
        (stone crunches and a crumbling break, pitched up and darkened) and one soft whump of ash.""",
        sources=CHAR, takes=3)
def soot_death_ash(rng, take):
    L = rng.uniform(3.2, 3.8)
    b = Bus(L + 1.2)
    br = airflow(rng, L, [(0, "a"), (L * 0.4, "h")], 1.0, [(0, 0), (0.1, 1), (L * 0.5, 0.6), (L, 0)])
    k = env([(0, 0), (L * 0.3, 0.2), (L, 1)], L)
    Lv = 0.8
    sigh = throat(rng, env([(0, 340), (Lv, 250)], Lv, "exp"), "a", 1.0, breath=0.5) * env([(0, 0), (0.05, 1), (Lv, 0)], Lv)
    b.at(0, br * (1 - k), -4)
    b.at(0, sigh, -8)
    b.at(0, soot(rng, L + 0.8, env([(0, 30), (L, 900), (L + 0.8, 40)], L + 0.8)) *
         env([(0, 0), (L * 0.5, 0.6), (L, 1), (L + 0.8, 0)], L + 0.8), -6)
    for t in np.sort(rng.uniform(0.5, 2.8, 3)):
        b.at(t, lp(rec(rng, CHAR, rng.uniform(1, 4), 0.4), 5000), -12)
    tw = rng.uniform(1.2, 1.6)
    b.at(tw, lp(synth.noise(0.5, rng), 400) * env([(0, 0), (0.03, 1), (0.5, 0)], 0.5), -8)
    b.at(tw, thud(rng, 70, 0.3, 0.5), -8)
    return finish(dsp.room(b.x, "car", wet=0.1, rng=rng))


@recipe("cs-soot-children", "death", "whimper",
        "The inhuman throat shrinking back into a child's, ending in a tiny whimper; the small body drops and soot sifts",
        """It dies the turn in reverse: a low rough throat (a beast's tract, a snarl's roughness) slides up and shrinks,
        the tract shortening and the pitch rising until it is a child's again, and ends in two tiny wordless whimpers.
        Then a small body dropping (a light thud, cloth on the boards) and soot sifting off it.""",
        sources=CLOTH, takes=3)
def soot_death_whimper(rng, take):
    b = Bus(3.6)
    L = rng.uniform(1.3, 1.7)
    f0 = env([(0, rng.uniform(100, 120)), (0.3, 120), (L, rng.uniform(360, 400))], L, "exp")
    k = env([(0, 0.45), (0.3, 0.45), (L, 1.0)], L)
    vow = [(0, "a"), (L, "u")]
    rough = throat(rng, f0, vow, k, breath=0.3, rough=0.6, sub=0.5, jitter=0.02)
    clean = throat(rng, f0, vow, k, breath=0.4)
    x = env([(0, 0), (L * 0.4, 0), (L, 1)], L)
    b.at(0, (rough * (1 - x) + clean * x) * env([(0, 0), (0.05, 1), (L * 0.85, 0.7), (L, 0.2)], L), -3)
    t = L + 0.05
    for i in range(2):
        Lw = rng.uniform(0.2, 0.28)
        w = throat(rng, env([(0, 410), (Lw, 320)], Lw, "exp"), "u", 1.0, breath=0.55)
        b.at(t, w * env([(0, 0), (0.03, 1), (Lw, 0)], Lw), -8 - 4 * i)
        t += Lw + rng.uniform(0.12, 0.2)
    b.at(t, thud(rng, 95, 0.25, 0.5), -6)
    b.at(t, rec(rng, CLOTH, -2, 0.4), -8)
    b.at(t, soot(rng, 1.0, env([(0, 300), (1.0, 10)], 1.0)), -12)
    return finish(dsp.room(b.x, "car", wet=0.1, rng=rng))


# ---- The Switchman ------------------------------------------------------------------------------------------------------
# Its junction lamp is an oil lamp: flame and hot glass, never electricity.

GLASS_TICK = K("impactGlass_light")
TIN = K("impactTin_medium") + ["kenney_rpg-audio:metalClick"]


def flame(rng, length, level):
    """An oil lamp's flame in a draught: a soft fluttering roar (burning gas tearing and lapping at 5-14 Hz) with a
    little hiss and the wick's tiny spits, following `level` (0 out, 1 burning well; a curve)."""
    n = samples(length)
    lv = synth.curve(level, n)
    flutter = np.clip(0.45 + 0.65 * norm(lp(rng.standard_normal(n).astype(np.float32), 14)), 0.0, 1.2) ** 1.5
    roar = bp(synth.noise(length, rng, "brown"), 50, 450) * flutter
    gas = bp(synth.noise(length, rng), 800, 3000) * 0.1 * flutter
    spits = synth.crackle(length, 8 + 25 * lv, rng, size=(0.0003, 0.002), hi=1200)
    pops = synth.crackle(length, 0.5 + 2.5 * lv, rng, size=(0.002, 0.006), hi=300)
    return norm((roar + gas) * lv + (spits * 1.2 + pops * 1.5) * np.sqrt(lv))


def junction(x, rng, tail=0.8):
    """Out at the junction, a few metres off in the open: a little air and a short outdoor tail, cut off before the
    reverb drags the take out."""
    y = dsp.room(lp(x, 10000), "night", wet=0.15, rng=rng)
    L = len(x) / SR
    return y[:samples(L + tail)] * env([(0, 1), (L, 1), (L + tail, 0)], L + tail)[:samples(L + tail)]


def whump(rng, length=0.35):
    """Lamp vapour catching: a soft low puff."""
    return mix(lp(synth.noise(length, rng), 500) * env([(0, 0), (0.03, 1), (length, 0)], length), synth.thump(90, 0.2) * 0.5)


def glass_chatter(rng, b, t, count):
    """The lamp's glass chimney rattling in its gallery: small clinks of thin glass, quick and uneven."""
    for i in range(count):
        b.at(t, rec(rng, GLASS_TICK, rng.uniform(5, 9), 0.06), -14 - 2 * i)
        t += rng.uniform(0.02, 0.06)


def lever_creak(rng, length):
    """The iron lever taking up a grip: a slow stick-slip groan through a heavy iron part."""
    return synth.creak(length, env([(0, 15), (length, 40)], length), rng, body=[f * 1.6 for f in synth.IRON], q=20) * \
        env([(0, 0), (0.1, 1), (length, 0)], length)


def knuckles(rng, b, t, count, span):
    """Long fingers closing: a ripple of small dry joints, far too many of them."""
    for c in np.sort(rng.uniform(0, span, count)):
        b.at(t + c, snap(rng, 0.03, rng.uniform(2200, 3800)), -14 + rng.uniform(-3, 2))


@recipe("cs-switchman", "flicker", "gutter",
        "Its oil lamp guttering as it grips the lever: the flame fluttering nearly out and catching again, the glass chattering",
        """An oil flame, not a bulb: a soft fluttering roar (brown noise lapping at 5-14 Hz) with the wick's small spits,
        which gutters down nearly to nothing as the lamp jerks and catches again with a soft whump of vapour, twice. The
        lamp's glass chimney chatters in its gallery (light glass impacts pitched up), the tin bail clinks, and the
        iron lever creaks as its grip takes up the slack. A few metres off, in the open.""",
        sources=GLASS_TICK + TIN, takes=2)
def switchman_flicker_gutter(rng, take):
    T = 3.2
    g1, g2 = rng.uniform(0.9, 1.2), rng.uniform(2.1, 2.5)
    lv = env([(0, 0.8), (g1 - 0.4, 0.9), (g1, 0.25), (g1 + 0.3, 0.06), (g1 + 0.45, 0.9), (g1 + 0.7, 0.7), (g2, 0.3),
              (g2 + 0.15, 0.12), (g2 + 0.3, 0.8), (T, 0.6)], T)
    b = Bus(T + 0.5)
    b.at(0, flame(rng, T, lv) * env([(0, 0), (0.15, 1), (T - 0.3, 1), (T, 0)], T))
    b.at(g1 + 0.4, whump(rng), -6)
    b.at(g2 + 0.25, whump(rng, 0.25), -10)
    glass_chatter(rng, b, rng.uniform(0.2, 0.4), 4)
    glass_chatter(rng, b, g2 - 0.1, 3)
    for c in rng.uniform(0, T, 4):
        b.at(c, synth.click(rng.uniform(4500, 7000), q=30, length=0.04, rng=rng), -22)
    b.at(0.35, lever_creak(rng, 0.8), -14)
    for c in (0.3, g2):
        b.at(c, rec(rng, TIN, rng.uniform(1, 4), 0.08), -16)
    return finish(junction(b.x, rng))


@recipe("cs-switchman", "flicker", "breath",
        "The lamp's flame breathing: drawn down and flaring again in the rhythm of slow breaths, its glass singing, long fingers closing",
        """It starts with its hand: a ripple of small dry knuckles, too many of them, closing on the iron lever. Then the
        flame does what a flame can't: it breathes. Twice it's drawn down low with a faint draw of air through the
        burner, and flares out again with a soft whump and a spray of spits, slow as someone breathing. The hot glass
        chimney sings faintly over it (friction noise rung through a thin glass's partials, wavering) and the iron
        lever creaks under the grip at the end.""",
        takes=2)
def switchman_flicker_breath(rng, take):
    T = 3.8
    b = Bus(T + 0.5)
    knuckles(rng, b, 0.0, int(rng.integers(9, 13)), 0.35)
    i1, o1 = 0.4, rng.uniform(1.3, 1.5)
    i2, o2 = o1 + 0.6, o1 + rng.uniform(1.5, 1.7)
    lv = env([(0, 0.7), (i1, 0.75), (o1 - 0.1, 0.12), (o1 + 0.1, 1.0), (i2, 0.7), (o2 - 0.1, 0.1), (o2 + 0.1, 1.0),
              (T, 0.7)], T)
    b.at(0.1, flame(rng, T, lv) * env([(0, 0), (0.25, 1), (T - 0.3, 1), (T, 0)], T))
    for a, o in ((i1, o1), (i2, o2)):
        L = o - a
        b.at(a + 0.1, bp(synth.noise(L, rng), 1500, 4000) * env([(0, 0), (L * 0.85, 1), (L, 0)], L), -20)
        b.at(o + 0.1, whump(rng, 0.4), -6)
        b.at(o + 0.1, synth.crackle(0.5, 60, rng, size=(0.0005, 0.003), hi=1000) * env([(0, 1), (0.5, 0)], 0.5), -8)
    n = samples(T)
    rub = synth.noise(T, rng) * (1 + 0.7 * norm(lp(rng.standard_normal(n).astype(np.float32), 3)))
    fg = rng.uniform(2300, 2800)
    sing = norm(dsp.resonate(bp(rub, 1500, 12000), [fg, fg * 2.32, fg * 4.25], q=400, gains=[1, 0.5, 0.25]))
    sing = bend(sing, 0.15 * np.sin(2 * np.pi * 0.4 * np.arange(n) / SR))
    b.at(0, sing * env([(0, 0), (1, 1), (T, 0.6)], T)[:len(sing)], -24)
    b.at(T - 1.0, lever_creak(rng, 0.8), -12)
    return finish(junction(b.x, rng))


# The throw itself: the game plays the stand's lever, the blades and the latch (crew-switch) for any throw, so these are
# what only the Switchman adds on top of them, at the lever: its grip, its lamp, its strength.

@recipe("cs-switchman", "throw", "grip",
        "Its long fingers closing round the lever and heaving it over: the knuckles, the lamp swinging on its arm, the glass",
        """Laid over the stand's own lever and blades (the crew's switch sounds, which the game plays for any throw):
        a ripple of far too many dry knuckles closing on the iron, the lever creaking under a grip no man has (a slow
        iron stick-slip pitched up, strained), then the heave: its oil lamp swung hard on its bail (the tin bail's
        creak and clink), the glass chimney chattering in its gallery and the flame fluttering low and flaring back with
        a soft whump. A few metres off, in the open.""",
        sources=GLASS_TICK + TIN, takes=3)
def switchman_throw_grip(rng, take):
    T = 1.6
    b = Bus(T + 0.6)
    knuckles(rng, b, 0.0, int(rng.integers(8, 12)), 0.3)
    b.at(0.2, lever_creak(rng, 0.7), -9)
    heave = rng.uniform(0.55, 0.7)
    b.at(heave, rec(rng, TIN, rng.uniform(-2, 2), 0.12), -12)
    b.at(heave + rng.uniform(0.2, 0.3), rec(rng, TIN, rng.uniform(1, 4), 0.1), -16)
    glass_chatter(rng, b, heave + 0.05, int(rng.integers(3, 6)))
    lv = env([(0, 0.8), (heave, 0.85), (heave + 0.12, 0.15), (heave + 0.35, 0.95), (T, 0.7)], T)
    b.at(0, flame(rng, T, lv) * env([(0, 0), (0.1, 1), (T - 0.2, 1), (T, 0)], T), -8)
    b.at(heave + 0.33, whump(rng, 0.3), -10)
    return finish(junction(b.x, rng))


@recipe("cs-switchman", "throw", "strain",
        "The heave in its two throats: a man's grunt and a thin reedy breath over it, the knuckles and the lamp's glass",
        """Laid over the stand's own lever and blades: it puts its weight on the lever, and you hear both of it. A man's
        effort (a short rough grunt through a man's throat, pushed out on the heave) and, a moment behind it, the
        spindly half's breath (a thin reedy 'ee' high up, breathy, sliding up as the lever goes over), the knuckles
        tightening first and the lamp's glass chattering on the heave. A few metres off, in the open.""",
        sources=GLASS_TICK + TIN, takes=3)
def switchman_throw_strain(rng, take):
    T = 1.5
    b = Bus(T + 0.6)
    knuckles(rng, b, 0.0, int(rng.integers(6, 9)), 0.22)
    heave = rng.uniform(0.3, 0.4)
    Lg = rng.uniform(0.35, 0.5)
    man = throat(rng, env([(0, 130), (Lg * 0.3, 150), (Lg, 105)], Lg, "exp"), [(0, "a"), (Lg, "u")], 0.72, breath=0.45,
                 rough=0.5, jitter=0.02)
    b.at(heave, man * env([(0, 0), (0.03, 1), (Lg * 0.6, 0.7), (Lg, 0)], Lg), -6)
    Lr = rng.uniform(0.7, 0.9)
    reed = throat(rng, env([(0, 420), (Lr, 640)], Lr, "exp"), "i", 0.65, breath=0.8, jitter=0.01)
    b.at(heave + 0.08, bp(reed, 1200, 6000) * env([(0, 0), (0.1, 1), (Lr * 0.6, 0.6), (Lr, 0)], Lr), -12)
    glass_chatter(rng, b, heave + 0.05, int(rng.integers(3, 5)))
    b.at(heave + 0.02, rec(rng, TIN, rng.uniform(-1, 3), 0.1), -15)
    b.at(0.1, lever_creak(rng, 0.5), -14)
    return finish(junction(b.x, rng))


GLASS_BREAK = ["sfx_100_v2:glass_03", "sfx_100_v2:misc_26", "sfx_100_v2:misc_27"]


def ballast_fall(rng, b, t, weight=1.0):
    """A body landing on the ballast: weight, a coat, stones shifting under it."""
    b.at(t, thud(rng, 70, 0.4, weight), -9)
    b.at(t, rec(rng, CLOTH, -3, 0.35), -6)
    b.at(t + 0.01, gravel(rng, 14, 0.12, 900, 5000, body=0), -8)


def folding_rule(rng, b, t, count):
    """Its spindly half folding up like a carpenter's rule: dry joints snapping shut one after another, quicker each time.
    Returns when the last one closes."""
    gap = rng.uniform(0.06, 0.08)
    for i in range(count):
        b.at(t, snap(rng, 0.04, rng.uniform(1500, 2800)), -10 - 0.5 * i)
        t += gap
        gap *= 0.86
    return t


@recipe("cs-switchman", "death", "lamp",
        "Its oil lamp smashing on the ballast and the spilt oil catching, its spindly half folding shut, the body falling",
        """The lamp goes first: thin glass breaking (glass breaks from the sfx pack), the tin lamp body bouncing on the
        stones, and the spilt oil catching with a soft whoomph into a little crackling fire. Its spindly half folds up
        like a carpenter's rule (dry joints snapping shut, quicker each time), the man half drops onto the ballast, and
        its last breath goes out of two throats at once: a man's exhale and a thin reedy whistle.""",
        sources=GLASS_BREAK + TIN + CLOTH, takes=3)
def switchman_death_lamp(rng, take):
    T = 4.2
    b = Bus(T + 0.5)
    b.at(0, rec(rng, GLASS_BREAK, rng.uniform(-1, 2), 0.7), 0)
    b.at(rng.uniform(0.02, 0.05), rec(rng, GLASS_BREAK, rng.uniform(-4, -1), 0.6), -4)
    b.at(0, rec(rng, TIN, -3, 0.2), -4)
    b.at(rng.uniform(0.15, 0.22), rec(rng, TIN, 1, 0.15), -11)
    b.at(0.02, gravel(rng, 10, 0.12, 900, 5000, body=0), -10)
    tf = rng.uniform(0.35, 0.6)
    L = T - tf
    fc = env([(0, 300), (0.15, 1500), (0.6, 600)], 0.6, "exp")
    b.at(tf, dsp.sweep_filter(synth.noise(0.6, rng, "pink"), "bp", fc, q=0.8) * env([(0, 0), (0.05, 1), (0.6, 0)], 0.6), -4)
    b.at(tf, synth.fire(L, env([(0, 0.1), (0.3, 0.55), (L, 0.3)], L), rng) * env([(0, 0), (0.2, 1), (L - 0.5, 0.8), (L, 0)], L), -9)
    t = folding_rule(rng, b, rng.uniform(0.3, 0.6), int(rng.integers(8, 12)))
    ballast_fall(rng, b, t)
    Lb = 1.4
    man = airflow(rng, Lb, [(0, "a"), (Lb, "h")], 0.72, [(0, 0), (0.1, 1), (Lb, 0)])
    reed = dsp.sweep_filter(synth.noise(Lb, rng), "bp", env([(0, 1800), (Lb, 1450)], Lb, "exp"), q=25)
    b.at(t + 0.15, man * 0.8 + norm(reed) * env([(0, 0), (0.2, 0.5), (Lb, 0)], Lb) * 0.4, -10)
    return finish(junction(b.x, rng, 1.0))


@recipe("cs-switchman", "death", "split",
        "Its voice coming apart into two: a man's groan sinking and a thin reed of a throat rising, as it lets the lever go and folds",
        """The two halves of it part in its voice: a man's groaning breath (a man's throat through his tract, rough)
        sinking while a thin reedy throat (a narrow, breathy 'ee' up high) rises against it, contrary motion that opens
        to more than an octave. Its grip lets go of the lever (a heavy metal clank, the ratchet slipping), the spindly
        half folds like a carpenter's rule, and the man half drops onto the ballast.""",
        sources=K("impactMetal_heavy") + ["sfx_100_v2:misc_20"] + CLOTH, takes=3)
def switchman_death_split(rng, take):
    b = Bus(4.0)
    L = rng.uniform(2.0, 2.4)
    man = throat(rng, env([(0, 115), (L, 72)], L, "exp"), [(0, "a"), (L, "o")], 0.72, breath=0.5, rough=0.35, jitter=0.015)
    reed = throat(rng, env([(0, 330), (L, 560)], L, "exp"), "i", 0.65, breath=0.7, jitter=0.01)
    shape = env([(0, 0), (0.15, 1), (L * 0.7, 0.7), (L, 0)], L)
    b.at(0, man * shape, -4)
    b.at(0.1, bp(reed, 1200, 6000) * shape, -5)
    b.at(0.05, rec(rng, K("impactMetal_heavy"), rng.uniform(-6, -4), 0.4), -6)
    b.at(0.1, rec(rng, ["sfx_100_v2:misc_20"], -3, 0.35), -10)
    t = folding_rule(rng, b, rng.uniform(0.6, 0.9), int(rng.integers(7, 10)))
    ballast_fall(rng, b, max(t, L * 0.8))
    return finish(junction(b.x, rng, 1.0))


# ---- The Stoker ---------------------------------------------------------------------------------------------------------
# Something alive in the coals. Its clubbed shriek is kept from the earlier pass; these sit round it.

COAL = ["sfx_100_v2:stones_01", "sfx_100_v2:stones_02", "sfx_100_v2:stones_03"]
STEAM = ["sfx_100_v2:misc_22", "sfx_100_v2:loop_water_03"]


def cycle(c, T, rest=0.0):
    """A control curve made periodic over T s: whatever runs past the end (above its resting value) comes round onto the
    head, so a flare that starts near the loop's end carries on at its start."""
    n = samples(T)
    head = c[:n].copy()
    extra = c[n:] - rest
    head[:len(extra)] += extra
    return head


def firebox(x, rng, wet=0.3):
    """Inside the firebox: an iron box's ring and short reflections."""
    y = mix(x, norm(dsp.resonate(x, [f * 1.4 for f in synth.IRON], q=10)) * 0.12 * np.max(np.abs(x)))
    return dsp.room(y, "box", wet=wet, rng=rng)


def coal_shift(rng, length):
    """Coals turning over under a weight: hollow lumps knocking (clicks rung low and loose), dense at first then fewer,
    with a stone crunch darkened under them and a clink or two on the grate bars."""
    out = Bus(length + 0.3)
    n = int(length * 40)
    for i in range(n):
        t = length * (i / n) ** 1.8
        out.at(t, synth.click(np.exp(rng.uniform(np.log(500), np.log(2500))), q=rng.uniform(3, 6), length=0.03, rng=rng),
               -6 - 10 * i / n + rng.uniform(-4, 0))
    out.at(0, lp(rec(rng, COAL, rng.uniform(-6, -3)), 3500), -6)
    for _ in range(2):
        out.at(rng.uniform(0, length), synth.click(rng.uniform(1800, 3200), q=25, length=0.08, rng=rng), -10)
    return out.x


def sizzle(rng, length):
    """Wet flesh on hot iron: a dense fine crackle over a hiss."""
    n = samples(length)
    hiss = bp(synth.noise(length, rng), 3000, 9000) * np.clip(0.5 + 0.5 * norm(lp(rng.standard_normal(n).astype(np.float32), 8)), 0, 1)
    return (hiss * 0.5 + synth.crackle(length, 500, rng, size=(0.0001, 0.0006), hi=3000) * 3) * \
        env([(0, 0), (0.05, 1), (length * 0.6, 0.7), (length, 0)], length)


def throat_in_fire(rng, length):
    """A groan smeared out and dropped an octave into the roar, as if the fire had a throat (the kept tell's trick)."""
    core = dsp.trim(src.get("sfx_100_v2:misc_25"), 0.06, 0.28)   # the steady middle of the groan, not its fade
    g = dsp.vari(dsp.smear(core, length / 0.28 / 2 + 0.5, rng=rng), -12)[:samples(length)]
    level_ = lp(np.abs(g), 1.5) + 1e-3
    return lp(g / level_ * np.mean(level_), 600)


@recipe("cs-stoker", "in-fire", "coals",
        "Something heavy turning over in the coals: the bed shifting and clinking, the fire flaring, wet flesh hissing on hot iron",
        """The firebox's own fire (a synthesised fire: roar, gas hiss, crackle) inside an iron box. Four times round the
        loop something heavy turns over in it: the coal bed shifts (hollow lumps knocking, a stone crunch darkened under
        them, a clink on the grate bars), the fire flares up with each move, and where its body meets the hot plate it
        sizzles. Under the roar a groan smeared into a held tone and dropped an octave swells with each move, the same
        throat the hissing tell has. One seamless 12-second cycle.""",
        sources=COAL + ["sfx_100_v2:misc_25"], takes=1, loop=True)
def stoker_in_fire_coals(rng, take):
    T = 12.0
    moves = (np.arange(4) + rng.uniform(0.1, 0.6, 4)) * T / 4
    pts = [(0, 0.45)]
    for m in moves:
        pts += [(m - 0.1, 0.45), (m + 0.4, 0.85), (m + 1.6, 0.5)]
    b = Bus(T + 3)
    b.at(0, synth.fire(T, cycle(env(pts + [(T + 2, 0.45)], T + 2), T, 0.45), rng), -2)
    groan = norm(throat_in_fire(rng, T))
    swell = 0.35 + cycle(sum(env([(0, 0), (m, 0), (m + 0.6, 0.65), (m + 2.0, 0), (T + 2, 0)], T + 2) for m in moves), T)
    b.at(0, groan[:len(swell)] * swell[:len(groan)], -12)
    for m in moves:
        L = rng.uniform(0.5, 1.0)
        b.at(m, coal_shift(rng, L), -4)
        b.at(m + rng.uniform(0.1, 0.4), sizzle(rng, rng.uniform(0.4, 0.8)), -12)
    y = firebox(b.x, rng)
    return finish(dsp.wrap(y, samples(T)))


@recipe("cs-stoker", "in-fire", "bellows",
        "The fire breathing: drawn down with a pull of air through the grate, then blooming out, the shut door rattling on its latch",
        """Something in the firebox is breathing the fire like bellows. Three slow breaths a loop: on the in-breath the
        roar is pulled down and air is drawn hissing in through the grate; on the out-breath it blooms, crackle and
        rendering fat pop, and the shut firebox door jumps on its latch (light iron clinks). A groan smeared into a held
        tone rides each out-breath, very low. One seamless 12-second cycle.""",
        sources=K("impactMetal_light") + ["sfx_100_v2:misc_25"], takes=1, loop=True)
def stoker_in_fire_bellows(rng, take):
    T, P = 12.0, 4.0
    pts, outs = [], []
    for k in range(3):
        a = k * P + rng.uniform(-0.15, 0.15)
        i_end = a + rng.uniform(1.4, 1.8)
        pts += [(max(a, 0), 0.45), (i_end, 0.18), (i_end + 0.5, 0.95), (i_end + 1.9, 0.45)]
        outs.append((a, i_end))
    b = Bus(T + 3)
    b.at(0, synth.fire(T, cycle(env(pts + [(T + 2, 0.45)], T + 2), T, 0.45), rng), -2)
    groan = norm(throat_in_fire(rng, T))
    for a, i_end in outs:
        L = i_end - max(a, 0)
        b.at(max(a, 0), bp(synth.noise(L, rng, "pink"), 300, 1500) * env([(0, 0), (L * 0.8, 1), (L, 0)], L), -10)
        b.at(i_end, synth.crackle(1.6, 40, rng, size=(0.003, 0.012), hi=300) * env([(0, 1), (1.6, 0)], 1.6), -6)
        g = dsp.trim(groan, i_end, 2.0)
        b.at(i_end, g * env([(0, 0), (0.4, 1), (2.0, 0)], 2.0)[:len(g)], -12)
        t = i_end + 0.35
        for j in range(int(rng.integers(3, 6))):
            b.at(t, rec(rng, K("impactMetal_light"), rng.uniform(-5, -2), 0.06), -14 - 2 * j)
            t += rng.uniform(0.05, 0.1)
    y = firebox(b.x, rng)
    return finish(dsp.wrap(y, samples(T)))


@recipe("cs-stoker", "burn", "flare",
        "A gout of flame thrown out of the open door at whoever swung, its breath roaring in it, embers spraying, a sleeve scorching",
        """Each swing gets the fire thrown back: a fireball's whoomph out of the door (a band of noise sweeping up and
        back down over a low roar) with the thing's breath in it (an exhale through a beast's tract, rough), a spray of
        embers (dense crackle dying away) and the swinger's sleeve scorching (a fine crisp crackle and a hiss).""",
        takes=3)
def stoker_burn_flare(rng, take):
    b = Bus(1.8)
    L = rng.uniform(1.0, 1.3)
    fc = env([(0, 200), (rng.uniform(0.06, 0.1), rng.uniform(1500, 2200)), (L, 350)], L, "exp")
    b.at(0, dsp.sweep_filter(synth.noise(L, rng, "pink"), "bp", fc, q=0.8) * env([(0, 0), (0.04, 1), (0.25, 0.6), (L, 0)], L), 0)
    b.at(0, lp(synth.noise(L, rng, "brown"), 300) * env([(0, 0), (0.06, 1), (L, 0)], L), -4)
    Lb = 0.9
    br = airflow(rng, Lb, [(0, "a"), (Lb, "h")], 0.45, [(0, 0), (0.05, 1), (Lb, 0)])
    rasp = throat(rng, env([(0, 85), (Lb, 60)], Lb), "a", 0.45, breath=0.6, rough=0.7, sub=0.4)
    b.at(0.02, br + rasp * env([(0, 0), (0.05, 0.5), (Lb, 0)], Lb), -6)
    b.at(0.05, synth.crackle(0.9, env([(0, 500), (0.9, 30)], 0.9), rng, size=(0.0003, 0.003), hi=1500), -2)
    b.at(0.1, sizzle(rng, 0.5), -8)
    return finish(dsp.room(b.x, "cab", wet=0.15, rng=rng))


@recipe("cs-stoker", "burn", "scald",
        "The blow bursts something wet in it, which flashes to scalding steam over the coals and over the swinger",
        """The swing lands in something wet (a squelch and a wet slap), and what spurts out hits the coals and flashes
        to steam: a sharp blast of hiss (steam hiss recordings, highpassed, slammed in) and droplets spitting and
        sizzling off the hot iron, then the coal bed slumping. The burn is a scald: the steam over the swinger's
        hands.""",
        sources=STEAM + COAL, takes=3)
def stoker_burn_scald(rng, take):
    b = Bus(1.8)
    b.at(0, squelch(rng, 0.25, 300, 1300), -6)
    b.at(0, slap(rng, 0.1, 2000, wet=0.9), -8)
    L = rng.uniform(1.0, 1.3)
    st = any_of(rng, STEAM)
    s0 = rng.uniform(0, len(st) / SR - L - 0.1)
    steam = hp(dsp.trim(st, s0, L), 1500)
    blast = bp(synth.noise(L, rng), 2000, 9000)
    shape = env([(0, 0), (0.015, 1), (0.25, 0.55), (L, 0)], L)
    b.at(0.03, (norm(steam) * 0.8 + blast * 0.4) * shape, 0)
    for t in np.sort(rng.uniform(0.05, 0.9, int(rng.integers(8, 14)))):
        Ls = rng.uniform(0.03, 0.07)
        sp = synth.click(rng.uniform(2500, 5000), q=4, length=0.01, rng=rng)
        b.at(t, mix(sp, bp(synth.noise(Ls, rng), 3000, 10000) * env([(0, 1), (Ls, 0)], Ls) * 0.4), -8 + rng.uniform(-6, 0))
    b.at(rng.uniform(0.4, 0.6), coal_shift(rng, 0.5), -10)
    return finish(firebox(b.x, rng, 0.2))
