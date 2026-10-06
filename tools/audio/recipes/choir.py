"""The Choir (GDD §21, App. A.7): singing membrane bells, each a jellyfish-like bell with one child's mouth on its front.

The tell is the voices multiplying as it gathers (spec A.4: 300 Hz-4 kHz, wide; the game adds a voice per layer, 1 to 6,
then 8 in the swarm). The director's note: melodic and creepy, from the music theory of unease. Two ways in:

- A round: one lullaby line in D Locrian (the mode whose fifth is a tritone, so it never comes to rest), sung by each
  voice a bar behind the last. Alone it's a child singing; stacked, the canon piles the line onto itself into tritones
  and minor seconds, which is what the game's adding voices does.
- A Shepard rise: voices on an endless glissando (Risset's illusion), always climbing and never arriving, a second stack
  a tritone away: the long rising telegraph made literal.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, env, mix, Bus
from recipes.kit import hz, melody_f0, note_env, bell_body

BAND = (300, 4000)
BPM = 54
BEAT = 60 / BPM
# D Locrian, 3/4: four bars, a lullaby's rocking dotted line that keeps falling back to D and never settles there.
D, Eb, F, G, Ab, Bb, C = 62, 63, 65, 67, 68, 70, 72
PHRASE = [[(F, 2), (Eb, 1)], [(D, 2), (F, 1)], [(Ab, 1.5), (G, 0.5), (Eb, 1)], [(D, 2.5), (Eb, 0.5)]]
BAR = 3 * BEAT
LOOP = 4 * BAR


def round_voice(rng, k, octave=0, detune=0.0, bell=560):
    """Voice k of the round: the phrase rotated k bars, so any number of them stacked is the canon."""
    bars = PHRASE[k % 4:] + PHRASE[:k % 4]
    notes = [(m + 12 * octave, b) for bar in bars for m, b in bar]
    f0, onsets, total = melody_f0(notes, BEAT, rng, scoop=70, glide=0.09, vib=(5.0 + rng.uniform(-0.4, 0.4), 0.007),
                                  detune=detune, drift=14)
    g = synth.glottis(f0, total, rng, jitter=0.006, shimmer=0.07, oq=0.65)
    vow = [(o, "u" if i % 3 else "o") for i, o in enumerate(onsets)]
    v = synth.tract(g, vow, "child", breath=0.22, rng=rng)
    v = v * note_env(onsets, total)
    v = bell_body(v, bell * rng.uniform(0.97, 1.03), q=25, wet=0.18)
    v = dsp.compress(v, -14, 2.5, 0.02, 0.3)
    v = dsp.wow(v, 10, 0.3, rng)
    return dsp.band(dsp.wrap(dsp.room(v, "night", wet=0.25, rng=rng), len(v)), 280, 4500, 2)


VOICES = [(0, 0, 0.0), (1, 0, 18.0), (2, 1, -12.0), (3, 0, -26.0)]   # (bars behind, octave, cents off)


@recipe("tell-choir", "voices", "round",
        "A round: a Locrian lullaby sung by each voice a bar behind the last, so stacking voices makes the canon",
        """One child's line in D Locrian (the mode with a tritone for a fifth, so it never resolves), a slow rocking 3/4
        lullaby. Synthesised throats (a glottis through a child's vocal tract on 'oo' and 'oh', scooping up into each note,
        a late vibrato, a slow tape-like drift), each rung through a thin bell of membrane so its harmonics catch bell
        partials. Each take is one voice, the same line rotated a bar further on and a few cents off the others: two
        voices make thirds and tritones, four make a cluster of D, E flat, F, G and A flat. The preview gathers them one
        by one from far off, as the game adds them.""",
        takes=4, loop=True, band=BAND, lufs=-22,
        preview=lambda takes, rng: _gather(takes, rng))
def choir_round(rng, k):
    bars_behind, octave, cents = VOICES[k]
    return round_voice(rng, bars_behind, octave, cents)


def _gather(takes, rng, ranges=(300, 160, 70, 25)):
    """Voices joining one by one, each nearer than the last (the game closes them from 300 m to 25 m)."""
    L = len(takes[0]) / dsp.SR
    total = L * (len(takes) + 1)
    b = Bus(total)
    for i, x in enumerate(takes):
        start = i * L
        # every voice already sounding comes in closer too
        for j in range(i, len(takes) + 1):
            near = dsp.distance(x, ranges[min(j, len(ranges) - 1)], rng)[:len(x)]
            b.at(j * L, dsp.shaped(near, [(0, 0), (1.5, 1)]) if j == i else near, 0)
    return b.x


def shepard_stack(rng, length, base, period, comps=5, tract="child", centre=900, sigma=1.1, bell=600):
    """Risset's endless glissando, sung: `comps` voices an octave apart, all gliding up one octave per `period`, each
    fading in at the bottom and out at the top (a bell curve over log pitch), so the whole always rises and never gets
    anywhere. A loop of exactly one period is seamless."""
    n = samples(length)
    t = np.arange(n) / dsp.SR
    out = np.zeros(n, np.float32)
    for c in range(comps):
        ph = (t / period + c / comps * 0 + c) % comps
        lf = np.log2(base) + ph            # log2 frequency, `comps` octaves of range
        f0 = (2 ** lf).astype(np.float32)
        amp = np.exp(-0.5 * ((lf - np.log2(centre)) / sigma) ** 2).astype(np.float32)
        g = synth.glottis(f0, length, rng, jitter=0.005, shimmer=0.06, oq=0.62)
        # open the vowel as it climbs: 'oo' low, 'ah' high
        v1 = synth.tract(g, "u", tract, breath=0.2, rng=rng)
        v2 = synth.tract(g, "a", tract, breath=0.2, rng=rng)
        k = np.clip((lf - np.log2(300)) / 3, 0, 1).astype(np.float32)
        out += (v1 * (1 - k) + v2 * k) * amp
    out = bell_body(out / (np.max(np.abs(out)) + 1e-9), bell, q=25, wet=0.15)
    return out


@recipe("tell-choir", "voices", "rise",
        "A Shepard rise: voices climbing forever and never arriving, a second choir a tritone away",
        """Risset's endless glissando, sung: five child throats an octave apart glide up an octave every 14 s, each
        fading in at the bottom and out at the top so the whole seems to climb forever (the long rising telegraph,
        literally). Vowels open from 'oo' to 'ah' as they climb, and they're rung through a membrane bell. The second take
        is the same choir a tritone away and a little slower, so stacked they grind against each other and drift in and
        out of phase. Each loop is exactly one cycle, so it's seamless.""",
        takes=2, loop=True, band=BAND, lufs=-22,
        preview=lambda takes, rng: _gather2(takes, rng))
def choir_rise(rng, k):
    period = 14.0 if k == 0 else 14.0 * 1.06
    base = 75.0 * (1 if k == 0 else 2 ** (6 / 12))
    y = shepard_stack(rng, period, base, period, bell=600 if k == 0 else 600 * 2 ** 0.5)
    y = dsp.wow(y, 8, 0.25, rng)
    return dsp.band(dsp.wrap(dsp.room(y, "night", wet=0.3, rng=rng), len(y)), 280, 4500, 2)


def _gather2(takes, rng):
    a, b2 = takes
    L = len(a) / dsp.SR
    bus = Bus(L * 3.2)
    bus.at(0, dsp.distance(np.concatenate([a, a]), 250, rng))
    bus.at(L * 1.2, dsp.shaped(dsp.distance(np.concatenate([b2, b2]), 60, rng), [(0, 0), (2, 1)]), -2)
    bus.at(L * 2.0, dsp.distance(a, 25, rng), 0)
    return bus.x
