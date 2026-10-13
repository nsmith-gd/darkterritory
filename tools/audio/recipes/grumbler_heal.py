"""The Grumbler's healing heard (queue #231, note 494; E1's #224, note 487, its healing seen; GDD App. A.8 "REGEN heals if
only one player has hit it in the last ~5s", "gang up or leave it alone"). A lone crewmate's blow on it closes again in
under a second and more; E1 draws the stuff of it pulled back into the body, a wet pulse. This is what that sounds like,
so the rule is learned by ear too (and by whoever reads the captions).

- Healing (`cs-grumbler.heal`): held at it while its health climbs, louder the further down it is. Wet sucks as what the
  blow knocked out of it is drawn back in (a seal pulling inward, the tissue giving and closing round it), tissue ticking
  as it knits, a low wet throb under it, and his muttering, low and pleased with himself. It's beasts.py's Grumbler (a
  man gone wrong): flesh, not chitin, and his own voice.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, env, mix, lp, hp, bp
from recipes import world_kit as W
from recipes import crew_kit as ck
from recipes import kit
from recipes.beasts import done, unit, squish, mutter

LOOP = 2.4      # a heal runs a second to four (enemies.json grumbler: health 6, regenPerSecond 1.5)


@recipe("cs-grumbler", "heal", "knit",
        "The Grumbler healing: wet sucks as the stuff of it is drawn back in, tissue ticking, a throb, his muttering",
        """While a lone crewmate's blow closes again (App. A.8): what the blow knocked out of it drawn back into the body in
        wet sucks every half second or so, each a seal pulling inward as its colour opens late, then the flesh giving
        and closing round it (wet tissue squishing, small bubbles); tissue ticking as it knits; a low wet throb under it
        like a pulse; and his muttering, low, shut-mouthed and pleased with himself (the scuttle's and the bellow's
        voice). Held while its health climbs, louder the further down it is. 2.4 s exact cycle.""",
        loop=True, takes=2, lufs=-24, seconds=LOOP)
def heal(rng, k):
    n = samples(LOOP)
    ev, closes = [], []
    t = rng.uniform(0.02, 0.1)
    while t < LOOP - 0.25:
        L = rng.uniform(0.16, 0.28)
        ev.append((t, unit(ck.slurp(rng, L, 160, rng.uniform(800, 1100), rise=True, bubbles=0.2)), rng.uniform(0.6, 1.0)))
        closes.append((t + L * rng.uniform(0.55, 0.75), squish(rng, rng.uniform(0.1, 0.16), 450, 2000, wet=0.9),
                       rng.uniform(0.35, 0.6)))
        t += rng.uniform(0.38, 0.62)
    draws = W.place(n, ev) + W.place(n, closes)
    # the tissue knitting: wet ticks, thicker as each draw lands
    ticks = W.cyclic(lambda z: lp(z, 3200), dsp.fit(synth.crackle(LOOP, 45, rng, size=(0.0003, 0.0012), hi=900), n))
    # a low wet throb under it, about every 0.6 s (four to the loop)
    beats = int(round(LOOP / 0.6))
    throb = W.place(n, [(j * LOOP / beats, unit(kit.thud(rng, rng.uniform(55, 70), 0.14, 0.8)), rng.uniform(0.6, 0.9))
                        for j in range(beats)])
    voice = W.cyclic(lambda z: lp(z, 900, 2), dsp.fit(mutter(rng, LOOP, f0=rng.uniform(88, 100), rise=0.95, rate=3.2,
                                                              open_=0.08), n))
    y = unit(draws) * 0.75 + unit(ticks) * 0.12 + unit(throb) * 0.3 + unit(voice) * 0.16
    return W.seamless(done(y))
