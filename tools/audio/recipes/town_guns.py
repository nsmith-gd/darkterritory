"""The town's guns at the walls (queue #502, note 772; note 599, docs/design/creatures/wakers.md §4: "the Wakers stop
stopShort metres out and stand there, roaring, while the town's guns fire at them"), as tools/audio/cues.py lists them and
GameAudio.Wakers.cs plays them on the beat the scene draws (Effects.WallGunBeat).

What each is decides the build:
- A gun firing: the terminus's guns stand on its gatehouse and wall towers, twenty to two hundred metres from a crew
  pulled in behind the gate, and fire over the wall at a Waker stood 300 m out. Cast-iron muzzle-loaders like the gun
  car's own (crew-cannon-fire), so the same shot, heard from the yard: the crack dulled by the distance and the stone
  between, the boom and the land's long roll kept. Every few seconds from each tower, never in step.
- A shot landing: the ball striking a hill of a body 300 m out and forty metres up. Heard from the town it's no crack: a
  deep dull whump into meat, a little of what it throws out, and the land rolling it back.

Each cue has two candidates. The first is the gun car's own shot and the cannon's own impact on flesh, kitbashed from the
packs' real thunder, iron and soft heavy blows (crew_cannon.shot, heard_foley's flesh_meat), put at the distance. The
second is lighter and modelled more: a smaller gun (the blast pitched up, its roll shorter) and the flesh models scaled up
(heard_foley's flesh_mass). Both out in the open; the low end kept under the director's squish line (the guns
high-passed at 90 Hz, the landings at 140).
"""

import numpy as np

import dsp
from dsp import hp, env
from recipes import crew_kit as ck
from recipes.crew_kit import recipe
from recipes.crew_cannon import shot, THUNDER, PLATE_H, METAL_H
from recipes.heard_foley import flesh_meat, flesh_mass, MEAT, CRACK, WET

L = "town-guns"


def from_the_yard(x, rng, metres, cut=90):
    """Put a gun's shot or a ball's landing out where the crew hears it from: darker and wetter for the distance (the
    build levels it; the game's own rolloff makes it quieter), the squish under `cut` Hz taken out."""
    return hp(dsp.distance(x, metres, rng), cut, 4)


def lighter(rng, k):
    """A smaller gun: the shot pitched up three to four semitones and its roll cut shorter."""
    y = dsp.vari(shot(rng, k), rng.uniform(3, 4))
    n = min(len(y), dsp.samples(3.2))
    return y[:n] * env([(0, 1), (2.4, 1), (3.2, 0)], n / dsp.SR)


@recipe(L, "fire", "tower", "A gun on the town's walls firing over them: the boom and the roll, the crack dulled",
        """The gun car's own shot (the packs' real thunder for the roll, their heavy plate and iron for the gun bucking,
        the modelled blast) put 60-120 m away, as a tower's gun is from a crew in the yard: the top taken off, more of it
        the land's roll than the blast.""", sources=THUNDER + PLATE_H + METAL_H, takes=3, lufs=-15)
def tower(rng, k):
    return from_the_yard(shot(rng, k), rng, rng.uniform(60, 120))


@recipe(L, "fire", "swivel", "A lighter gun on the walls: a higher, shorter bark and less roll",
        """A smaller piece than the gun car's: its shot pitched up three to four semitones and its roll cut at three
        seconds, put 50-100 m away. The towers' guns heard as a battery of different pieces, not one gun again and
        again.""", sources=THUNDER + PLATE_H + METAL_H, takes=3, lufs=-16)
def swivel(rng, k):
    return from_the_yard(lighter(rng, k), rng, rng.uniform(50, 100))


@recipe(L, "hit", "meat", "A ball landing on a Waker's back 300 m out: a deep dull whump into a hill of meat",
        """The cannon's own impact on flesh (real soft heavy blows and a leather drop pitched down, a bone giving, the
        gore coming down) pitched down three semitones more for a body that size, and put 300 m off: no crack left, the
        whump and the land rolling it back.""", sources=MEAT + CRACK + WET, takes=3, lufs=-19)
def meat(rng, k):
    return from_the_yard(dsp.vari(flesh_meat(rng, k), -3), rng, 300, 140)


@recipe(L, "hit", "mass", "A ball into a Waker's back, modelled: a dense whump and the body quivering, far off",
        """The flesh models the creatures share scaled up (a dense low whump of noise, a slap on wet hide, the body
        quivering after), pitched down three semitones more and put 300 m off.""", sources=WET, takes=3, lufs=-19)
def mass(rng, k):
    return from_the_yard(dsp.vari(flesh_mass(rng, k), -3), rng, 300, 140)
