# THE CLOSE — the thing that fills a crowded car

*Creature proposal, Moth (queue #706, note 1000), 23 Oct 2026; **reworked by Sloe (queue #709, note 1004)** on the
director's verdict. **Status: a proposal for the director; nothing is built until the director approves it.** Numbers are
a first pass in the roster's units.*

> **The director, 23 Oct (line 36):** "I like the concept of if everybody's crowded into one car ... something that does a
> little bit of crowd control, but I really don't like that the air goes bad. That feels gimmicky. It should always be a
> creature with a clear tell and a clear counter." So the bad air is gone; the crowd control stays, and it's a creature.

> **When the roof bows, someone get out.**

## 1. What it is

A great soft grey bladder of a thing, all skin and no bones, that lies along the roofs of a moving train. When too many of
the crew pack into one shut car, it pours itself in through the roof hatch (or the vents of a car without one) and swells
to fill the car, pressing everyone flat against the walls. It can't hold a car with a door open and people leaving it, and
it can't stand being hit from outside, where it's thin.

## 2. Roster entry (GDD §21, INTERIOR)

**THE CLOSE** · *on the roof of a crowded car*
The roof boards bowing in over your heads.
> **RULE: when the roof bows, someone get out.**

**Sense:** weight (crew in one car). **Zone:** interior (entered from the roof). **Want:** Kill. **Cost:** 2.

## 3. Silhouette and look

From inside: the roof boards bowing down between the ribs, then grey skin bulging through the hatch like dough, a slow
pulse in it. From the roof: a long grey sack slumped over the car, its edges in the gaps. Clips: `lie`, `bow` (the roof
pressed), `pour` (through the hatch), `swell`, `pin` (the car full), `burst` (hit from outside: it sags and slides off the
car), `death`.

## 4. The tell (spec A.3/A.4)

- **The bowing** (tier 1): the roof's boards creaking under a slow weight, 150–400 Hz, swelling and easing like breath,
  every 3 s, then the hatch's latch rattling. Told from the Brakeman's boots (a walk, steps) by having no steps, and from the
  Climbers' scrabble by being slow.
- **Seen:** the lamp swinging on its hook as the roof presses down; the roof sagging between the ribs.

## 5. The rule (App. A.4 format)

```
LIE       aboard at speed: 3 or more crew (2 on a crew of 2–3) in one car with its doors shut, for 40 s → it settles on that roof
          └ TELEGRAPH: the bowing (10 s before it pours)
          ├ the car down to fewer than 3 (or 2) → it slides off into the next gap and is gone
          └ else
POUR      in through the hatch; it swells over 4 s
PIN       everyone in the car pinned to the walls (crowd control: no walking, no Use, a look and a voice only); each takes a
          crush of 5 a second after 6 s
          ├ a crewmate outside the car (on its roof, or at the open door) lands 3 blows on it → it bursts out and slides off
          ├ a door opened from inside by the one nearest it (the pinned can still Use a door they're against) → one gets
          │ out a second, and when fewer than 3 are left it lets go
          └ a gun round into it (the rear gun, if the car is in its arc) → it bursts
PUNISH    a car pinned for 20 s: the crush kills whoever's weakest first
```

## 6. Aboard or afoot; what it counters

**Aboard, at speed.** It counters **turtling in one car** (Rook's `hiding`, several at once): the shut car where nothing
else reaches. Its counter is teamwork in the other direction: somebody has to be *outside*.

## 7. Compositions

- **The Close + Climbers:** Climbers take any gap held by fewer than there are of them, so the crew can't all be inside or
  all at one gap. Hold the gaps in twos, keep the cars light.
- **The Close + the Choir:** the Choir says shut every door; the Close says not all of you behind one.
- **The Close + the Gannet or the Drops:** the friend who goes up to burst it is on a roof at speed.

## 8. Early versus late

- **Early:** from Frontier; 60 s to settle, 8 s before the crush.
- **Late:** 30 s; 4 s; one on every crowded car at once.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The thing that squashes you when you all hide in one car." |
| 2 | Answered better by two | ✔ Only someone outside can burst it. |
| 3 | Consequence now, death later | ✔ The bowing now; 20 s pinned before the first death. |
| 4 | A verb, not just a "don't" | ✔ Go out and hit it; open the door; split up. |
| 5 | Someone gets blamed | ✔ "Who said we should all hide in the van?" |
| 6 | Fun to scream | ✔ "SOMEBODY GET ON THE ROOF AND HIT IT!" |

## 10. Build cost

**Small–medium.** One soft-body model (a sack with a skin shader and a swell blend; no limbs), the roof's bow as a vertex
sag on the car's roof boards, a pinned state on the motor (a held pose, the Grab's), and a crew-per-shut-car count (the
census already has each crewmate's car). About 1.5 agent-days of sim and bots (bots leave a bowing car; one goes up), 1
of art, one sound.

## 11. Open calls for the director

1. The trigger: 3 crew in one shut car, or more than half the crew? Sloe's call: 3 (2 on a small crew), as Moth's.
