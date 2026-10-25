# THE WAKERS — what gets up at dawn

*Ending design, D1 (queue #321, ARCHITECTURE §8 note 588). **Status: the design approved by the director, 9 Oct 2026**
("The new Dawn ending design is excellent. This is exactly what I was looking for. The gargantuan creatures, the picking up
of the train, especially with you in it, you should experience that if you're still playing. And then the tension before
it is really, really important"). The numbers in §6 are a first cut for the Sim; the tuning's are the truth once built.
The model goes to C1/E1 on the art checklist, the sounds to AU1.*

> **The night is the safe part.**

*The name:* the crews call them the Wakers, since what matters is that they wake. ("Sleepers" were the railway's ties
long before they were monsters, and the track debris already has the word.)

The fantasy (GDD §1, the director, 9 Oct): "we are a crew sent out into a dangerous world at night when most of the really
bad monsters are asleep." The Wakers are those monsters. Every night the crew works the line in the hours the Wakers
lie down; dawn is when they get up. The night's creatures are the small fry of the world, and the towns' walls and guns are
built for the day.

This replaces "the line is live" (GDD §8): a timer running out, then nothing happening, then a failed run. The director's
playtest, 9 Oct: a bad ending, and "run lost" with nothing happening feels bad.

---

## 1. The shape of the ending

1. **The stir** (the last 3 minutes before dawn): the tension. The crew can tell what's coming and how close.
2. **The rise** (dawn): the Wakers get up out of the land, far behind the train and to its sides.
3. **The chase:** they come down the line after the train, slow at first, then faster than any engine.
4. **Racing in:** a train that reaches the town's walls first is safe. The Wakers stop short of the guns, and roar.
5. **The catch:** one that reaches the train takes the last car and lifts. The train goes up from the rear, car by car,
   with the crew inside it, in first person, still playing.
6. **Eaten:** it eats the cars from the back towards the engine. The run ends when the engine goes, or the last of the crew
   does.

A crew near the terminus at dawn can race them in. A crew far out almost never does.

## 2. The stir: the tension before dawn

The director: "the tension before it is really, really important." Three minutes (`stirSeconds`), growing to the end:

- **The sky.** The horizon behind the train goes from black to a bruise to a thin cold line. The light comes from where
  the Wakers will rise. (The lighting's `DawnOf` already brightens it; the stir gives it a direction and a colour.)
- **The ground.** Far-off thuds you feel more than hear: a camera tremor, the lamps swinging, coal sliding in the tender,
  each stronger and closer together.
- **Their calls.** Long, low calls from behind the hills, below the night creatures' range so they never mask a tell
  (spec A.3; AU1).
- **The night things run.** In the last minute the night's creatures go to ground: boarders drop off and run for the
  trees, packs break off. *For the director:* this helps a late crew home and is the clearest sign the night's over. It
  also means fewer monsters brought into town (#322). Built (note 635; the director, the inbox, 12 Oct 2026: "creatures
  should leave all except the knotter and the hotbox who are parasites that have train functions"): from
  `nightFleesSeconds` before dawn each night creature lets go and is gone, as one driven out of a fort is, and the
  director sends no more of them. The Knotter and Hotbox stay (`staysAtDawn`); the director ticks each creature in the
  Creature Tweaker (claude.ai/artifact/TdoxfYnmZTgv2jc9JmRkkw). Seen going since note 642: each one drops
  off and runs for the trees rather than being gone (`CreatureArt`'s going-to-ground; note 1026).
- **The hills move.** Across the dark land, the shapes the crew took for hills settle and shift (note 962: the far ridges toward the stir heave in slow swells against its line, from `hillsFrom` of the way through it, up to `hills` degrees by dawn).
- **The clock.** The HUD's dawn clock shows in the last stretch already (note 459). In the stir it turns to the
  Wakers' distance once they're up.

## 3. The rise and the chase

At dawn, `count` Wakers rise (by tier: 1 on the Frontier, 2 on the Dead Lines, 3 on the Black Grade):

- The first rises on the line, `riseBehind` metres behind the train's last car.
- Any others rise `riseAside` metres off to either side, further back, and come at the train across the land, joining
  the line behind it. Only the first one decides the catch; the others are the sight of the world getting up.
- Each runs at `startSpeed`, building to `topSpeed` over `rampSeconds`. Its top speed is faster than the engine's
  flat-out 22 m/s. So a train at cruise is caught from ~2.5 km out, and one at full speed ~1 km later, but a crew that's
  close can make it.
- **It's deterministic.** A Waker's place along the line is a function of the seconds since dawn and where the train's
  rear was at dawn. No randomness, no pathing: the host and every client agree, and the bots can reckon it.
- It comes over everything: down the cutting, through the trees, along the embankment. Nothing on the line stops it.

## 4. Racing in: the walls

A train whose last car is in through the town's gate is in (note 940; the director, 20 Oct 2026: "unless I'm behind those
walls fully, it should really make a good effort to try and get me"). Only then do the Wakers stop: `stopShort` metres out
(150, halved from 300: "it should stop maybe halfway a bit closer"), standing there roaring while the town's guns fire at
them. That's the picture the crew rolls in under. Short of the gate, by however little, the train is still theirs: they come
on past where they'd stand and take it. A train stopped short of the gate to clear its cars (#322, #323) is still out, and
the clock's still running. One backed out of the gate again comes back to them, and they have it as it does.

## 5. The catch, and being eaten

The director: "the picking up of the train, especially with you in it, you should experience that if you're still
playing."

- **The grab.** The Waker closes on the last car, whichever way the train's going (note 940: "pick up the train while it's
  moving"; "I'm going straight between its legs"): run down from behind, or backed into him, it's his. The train is brought
  to a stand in his hands at `holdDecel` (6 m/s²) at the least, whatever the driver does: stopped before the last car's up.
- **Shot.** Up out of the land his body's in the open (`bodies.waker`, his legs to his head; on his knees `kneelStoop` of
  it), so a ball lands: the hit's sound and marker, a jolt through him, and he roars. Nothing hurts him (note 940): he's the
  dawn, and the only answer is the walls.
- **The lift** (note 902; the director's playtest of 16 Oct 2026: "stop behind the train and like scoop it up with its
  hands and pick it up like it's like a two-handed hot dog"). It stops `catchShort` m behind the last car, drops to its
  knees on the line and gets both hands under the car where it stands on the rails, tears it off its coupling and lifts
  it to its mouth, end first, its far end tipped down. It takes it in three bites (`bites` of the car's `eatSeconds`, the
  last as the sim has it eaten), then its hands go down for the next and it shuffles on after it on its knees. Only the
  car in its hands leaves the rails; the rest of the train stands where it is. Whoever comes down off the car in its hands
  once it's `fallFrom` of the way up, onto the ground where it stood (within `fallAround` m), fell from it, and dies.
  Everyone aboard stays in first person and in control of their eyes. They are pinned in their car (the frame they live
  in tilts and rises), and can look out at the land falling away and the thing holding them.
- **No cutaway while you're playing.** Held by a Waker is not D1.4's held camera (#312): that cuts to third person for
  the night creatures' holds. This one stays in your eyes.
- **Eaten.** From the rear, a car every `eatSeconds`, the crew in it with it. The screen goes when your car goes.
- **Whoever ran is taken** (note 710; the director, 14 Oct 2026: "It should capture players who escape the train and eat them
  too"). While one holds the train, another rises out of the land `pickAside` m beyond each crewmate off it, over
  `pickRiseSeconds` (the telegraph: the earth heaving up beside you). It strides over at `pickSpeed` and kneels, takes
  them in its hand within `pickReach`, lifts them `pickLiftSeconds` to its mouth, and eats them. The held stay in their
  own eyes, rising past its fingers to its face. Up to `pickers` rise at once; the rest of who ran are next. Only in
  under a fort's guns is anyone left alone.
- **The end.** When the engine goes, or the last of the crew does, the run ends as "Taken at dawn". The cause card
  names the Waker and the minute. The film (A1's) can show the catch from outside.

## 6. Numbers (first cut; content/tuning/enemies.json `wakers` when built)

| | |
|---|---|
| `stirSeconds` | 180: the stir before dawn |
| `nightFleesSeconds` | 60: the night's creatures go to ground in the last minute (note 635); `staysAtDawn` the Knotter and Hotbox |
| `count` | 1 / 2 / 3 by tier |
| `riseBehind` | 1200 m behind the last car |
| `riseAside` | 600 m off the line, the others |
| `startSpeed` → `topSpeed` | 6 → 26 m/s over `rampSeconds` 45 |
| `stopShort` | 150 m short of the gate, once the train's last car is in past it (note 940; it was 300) |
| `holdDecel` | 6 m/s²: a train he has is stopped in his hands (note 940) |
| `dragDecel` | 3 m/s² once it has the last car |
| `catchShort` | 12 m behind the last car, where it kneels to take it (note 902) |
| `scoopFrom`, `scoopHold` | the last car goes up from 1.2 s into the hold till 1 s before `eatAfter` (6) |
| `eatSeconds` | 4 a car: `reachShare` 0.35 of `scoopShare` 0.45 of it its hands going down, the rest up; bites at 0.55 and 0.775, the third as it ends |
| `fallFrom`, `fallAround` | 0.15 of the way up; 3 m round the car |
| `pickers`, `pickAside`, `pickRiseSeconds` | 3 at once, 40 m beyond whoever ran, 4 s up (note 710) |
| `pickSpeed`, `pickReach`, `pickLiftSeconds` | 12 m/s over, in hand within 18 m, 4 s up to its mouth (22 m up, 10 m out) |

A train stood still at dawn is caught in about 70 s. One at cruise (14 m/s) is caught after about 150 s, so it makes it
from within ~2.5 km of the gate. One flat out (22 m/s) makes it from within ~3.5 km.

## 7. What it replaces, and what it costs

- `RunEnd.DawnMissed` stays the enum's value (saves, the harness, the balance reports), named "Taken at dawn" on screen.
- `dawnGraceSeconds` (two minutes of nothing after dawn) goes: the chase is the grace.
- **Settlement.** A train taken at dawn is lost with everything on it, and the crew in it (the director, 9 Oct, Sea of
  Thieves: "the ship goes down and you lose everything"). *For the director:* what does the company put under a crew who
  lost the train? A crew needs something to run the next night on. My suggestion: the campaign's starting consist,
  on credit against the next nights' scrip.
  - *Answered (the director, the inbox, 12 Oct, note 632): "c, The campaign ends", roguelike.* The company puts nothing
    under them: the night is settled, the save is closed, and the slot's next start is a new crew from a fortress.

## 8. Building it

1. **Sim** (D1): a `Waker` enemy (loose, its along-line place and phase replicated through the enemy records), the
   catch holding the train and taking the cars and crew, `RunEnd` from it, the night's creatures fleeing. `dt wakers
   --route frontier:7 --late 120` plays a late night's last minutes headless and writes the chase. Tests: a stopped train
   is caught; a train at the gate is not; the catch is the same on a client as on the host.
2. **Presentation** (D1): the stir (sky, tremor, the lamps), the Waker drawn as a placeholder until the model comes,
   the lifted cars' frames tilting and rising with the crew in them, screenshot views `waker`, `wakerlift`, `stir`.
3. **Art** (C1/E1, on the art checklist): the Waker. It is gargantuan: a car is a mouthful. It is long-limbed enough to
   run down a train, and earth-coloured, so it reads as the land getting up. A model, with run, grab, lift and eat clips.
4. **Audio** (AU1): the stir's calls and thuds, the rise, the run, the grab and the eating, and the guns at the walls.
