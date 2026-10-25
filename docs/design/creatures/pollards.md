# THE POLLARDS — the trees that move when the lamp's off them

> **CUT by the director, 23 Oct 2026** (`docs/playtest/2026-10-23.md` line 36): "I also don't really like the pollards. I'm not a huge fan of keep a lamp on it." Kept here for the record (queue #709, note 1004); not to be built.

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **Every time you look back, the woods are closer.**

## 1. What it is

Stunted, cut-back trees, the kind the railway lopped along its cuttings, that walk while no light is on them. They come
out of the woods whenever the train stands where there's no stop: a red signal, a bend taken slow, a Hotbox pulled, a
Knotter killed. Lit, they're trees. Dark, they come on.

The director, 23 Oct (line 28): "if we stop like this in the wilderness, we really should feel like there's a potential for
something dangerous."

## 2. Roster entry (GDD §21, OUTSIDE / at a stand)

**THE POLLARDS** · *beside a standing train, out in the wild*
Lopped trees by the line that weren't there a minute ago.
> **RULE: keep a lamp on them.**

**Sense:** light (its absence). **Zone:** outside, at a stand. **Want:** Kill. **Cost:** 2 (a stand of 3–6).

## 3. Silhouette and look

Waist-to-head-high trunks, cut back hard years ago so each top is a knuckled fist of short shoots; two low limbs grown out
like arms. Bark like wet slate. A face only in the knot-holes, and only once you're close. Lit, they never move and never
sway, which is how a careful player tells them from the real trees: no wind moves them. Clips: `stand`, `step` (a lurch),
`reach`, `hold`, `split` (death).

## 4. The tell (spec A.3/A.4)

- **The step** (tier 1): wood splitting, one dry crack 600 Hz–2 kHz, each time one moves (never while lit), from its
  place to within 30°. Told from the Tower Jaw's chewing (a run of gnaws) and the car fire's crackle (6–9 kHz) by being
  single and lower.
- **Seen:** a lamp swung back finds a trunk where there was ground; they never move in the light.

## 5. The rule (App. A.4 format)

```
COME      the train stands outside any stop, fort or yard for 20 s (Local 40 s) → 3–6 rise in the treeline, 25–40 m off,
          both sides
STEP      each one unlit by a lamp, a car's lamp or the headlamp moves 1.2 m/s toward the nearest crewmate afoot or at a
          car's open door; lit, it stands
          └ TELEGRAPH: the crack as it moves
REACH     within 1.5 m of someone, unlit for 1.5 s → GRAB: it holds them, its limbs round them (10 s)
          └ a lamp on it, or a blow from a friend → it lets go
          └ 6 blows fell one (a gun round is 4)
GO        the train moves off over 3 m/s → they stand where they are, trees again
```

A shut car door keeps them off (they can't climb); a lamp thrown on the ground (senses.json, 25 s) holds those in its
reach.

## 6. Aboard or afoot; what it counters

**Afoot, and at a standing train in the wild.** It counters **stopping anywhere costs nothing**, the crew that **runs
dark** to dodge the Fire Flies and the Gannet, and **one person fixing alone** (the Hotbox, the Knotter's coupling): one
can't watch both sides and work.

## 7. Compositions

- **Pollards + Fire Flies:** lamps on to hold the woods back, lamps off to keep the flies out of the cars.
- **Pollards + Hotbox / Knotter:** both need the train stopped and a crewmate at a wheel or coupling; someone has to hold
  the light.
- **Pollards + Tippy Toesie:** standing still holding a lamp on the trees is just what Tippy Toesie wants.

## 8. Early versus late

- **Early:** 3, only after 40 s at a stand, one side.
- **Late:** 6, after 20 s, both sides, and the step speeds to 1.6 m/s.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The trees that walk when you're not looking." |
| 2 | Answered better by two | ✔ One lamp for each side; one works while one lights. |
| 3 | Consequence now, death later | ✔ They're closer now; the hold is 10 s. |
| 4 | A verb, not just a "don't" | ✔ Light them, throw a lamp, fell one. |
| 5 | Someone gets blamed | ✔ Whoever put their lamp away, or stopped the train out here. |
| 6 | Fun to scream | ✔ "LIGHT! LEFT SIDE! LIGHT!" |

## 10. Build cost

**Medium.** The sim's light query is built (`World.Emitters(light, at)`, note 728); a stand is the director's own trigger
(the train standing outside a stop). New: a "lit" test per creature against every lamp's cone, deterministic; the trees
from the existing tree kit, re-cut, with two limbs (`tools/models`); one sound. 1–2 agent-days of sim and bots (bots hold
a lamp on the nearest), 1 of art.

## 11. Open calls for the director

1. Only the wild, or the halts too (where `n1-A3-halt-own-creature` already sends one)? Moth's call: the wild only.
2. Does the headlamp count? Moth's call: yes, ahead only, so the rear is where they come from.
