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
  also means fewer monsters brought into town (#322). Not built until the director says (`nightFleesSeconds`).
- **The hills move.** Across the dark land, the shapes the crew took for hills settle and shift.
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

A train whose engine has crossed the town's gate (`Run.Tuning.TerminusZone`) is in. The Wakers stop `stopShort` metres
out and stand there, roaring, while the town's guns fire at them. That's the picture the crew rolls in under. A train
stopped short of the gate to clear its cars (#322, #323) is still out, and the clock's still running.

## 5. The catch, and being eaten

The director: "the picking up of the train, especially with you in it, you should experience that if you're still
playing."

- **The grab.** The Waker closes on the last car. The train drags to a stop against it (`dragDecel`), whatever the
  driver does.
- **The lift.** The last car goes up, nose-down, then the next as the coupling hauls it, a car every `liftSeconds`.
  Everyone aboard stays in first person and in control of their eyes. They are pinned in their car (the frame they live
  in tilts and rises), and can look out at the land falling away and the thing holding them.
- **No cutaway while you're playing.** Held by a Waker is not D1.4's held camera (#312): that cuts to third person for
  the night creatures' holds. This one stays in your eyes.
- **Eaten.** From the rear, a car every `eatSeconds`, the crew in it with it. The screen goes when your car goes. Anyone
  off the train is taken by the other Wakers (or the first, after).
- **The end.** When the engine goes, or the last of the crew does, the run ends as "Taken at dawn". The cause card
  names the Waker and the minute. The film (A1's) can show the catch from outside.

## 6. Numbers (first cut; content/tuning/enemies.json `wakers` when built)

| | |
|---|---|
| `stirSeconds` | 180: the stir before dawn |
| `nightFleesSeconds` | 60: the night's creatures go to ground in the last minute (not built: the director's call) |
| `count` | 1 / 2 / 3 by tier |
| `riseBehind` | 1200 m behind the last car |
| `riseAside` | 600 m off the line, the others |
| `startSpeed` → `topSpeed` | 6 → 26 m/s over `rampSeconds` 45 |
| `stopShort` | 300 m short of the gate |
| `dragDecel` | 3 m/s² once it has the last car |
| `liftSeconds` | 2.5 a car |
| `eatSeconds` | 4 a car |

A train stood still at dawn is caught in about 70 s. One at cruise (14 m/s) is caught after about 150 s, so it makes it
from within ~2.5 km of the gate. One flat out (22 m/s) makes it from within ~3.5 km.

## 7. What it replaces, and what it costs

- `RunEnd.DawnMissed` stays the enum's value (saves, the harness, the balance reports), named "Taken at dawn" on screen.
- `dawnGraceSeconds` (two minutes of nothing after dawn) goes: the chase is the grace.
- **Settlement.** A train taken at dawn is lost with everything on it, and the crew in it (the director, 9 Oct, Sea of
  Thieves: "the ship goes down and you lose everything"). *For the director:* what does the company put under a crew who
  lost the train? A crew needs something to run the next night on. My suggestion: the campaign's starting consist,
  on credit against the next nights' scrip.

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
