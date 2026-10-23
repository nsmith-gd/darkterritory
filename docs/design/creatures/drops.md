# THE DROPS — they fall onto the roofs from the trees overhead

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **Under the trees, off the roof.**

## 1. What it is

Where the line runs through old forest, the boughs meet over the cutting. Things live up there: a troop of long-armed,
hairless, monkey-sized things that drop onto whatever passes underneath. At speed they're flung along the roofs, hooking
on; then they go for anyone up top, dragging them to the edge to throw them off.

## 2. Roster entry (GDD §21, FLANK)

**THE DROPS** · *in the trees over the line, on the move*
Pale shapes in the boughs ahead, chattering.
> **RULE: under the trees, off the roof.**

**Sense:** movement (on the roofs). **Zone:** flank (the roofs). **Want:** Split (a crewmate off the train). **Cost:** 2.

## 3. Silhouette and look

Gibbon-thin, pale and hairless, arms twice their legs' length, hands and feet alike, a short blunt face all eyes. In the
boughs: pale knots in the lamp, too many. Clips: `perch`, `drop`, `cling`, `scuttle`, `haul` (a crewmate to the edge),
`hit`, `thrown` (off the roof), `death`.

## 4. The tell (spec A.3/A.4)

- **The troop** (tier 1): chattering in the trees ahead, 3–5 kHz, quick and many-voiced, from 150 m before the forest's
  canopy, heard by whoever's on a roof (`roofOnly`, like the roof warnings). Told from the low clearance's cord slaps
  (4–7 kHz, a run of slaps) by being voices.
- **Seen:** the boughs meeting over the line in the headlamp; pale shapes in them.

## 5. The rule (App. A.4 format)

```
PERCH     a forest stretch with a canopy over the line (the line generator marks them, as it does tunnels)
          └ TELEGRAPH: the chattering ahead, 150 m before (over 10 s at 15 m/s)
DROP      the train passes under → 3–5 drop onto the cars that have someone on the roof; they miss an empty roof
CLING     on the roof: a blow knocks one off (it's thrown by the train's speed); 2 blows kill one
HAUL      two together on a lone crewmate → GRAB: dragged to the roof's edge (8 s), then thrown off the train
          └ a friend's blow on either, or the victim dropping into a hatch or a gap → freed
          └ Shove knocks one off
```

Thrown off is the Split: a crewmate on the ground behind a moving train (#695's left-behind, the Mourners if it kills).

## 6. Aboard or afoot; what it counters

**Aboard, at speed.** It counters **riding the roofs** as a safe lookout (the roof-gunner, the crewmate who lives up there)
and pairs with the Rodrider's "get up high" and the Gannet's open country: forest is where the Gannet can't fly.

## 7. Compositions

- **The Drops + the Rodrider:** up off the floor, or down off the roof, but not both at once.
- **The Drops + Climbers / Brakeman:** the crew up top for those is the crew the Drops take.
- **The Drops + the Lurcher:** thrown off in the forest, alone.

## 8. Early versus late

- **Early:** Frontier on, 2–3, one canopy a night.
- **Late:** 5, every canopy, and they stay aboard on the next stretch.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The monkeys that drop off the trees and throw you off." |
| 2 | Answered better by two | ✔ It takes two of them on one; a friend knocks them off. |
| 3 | Consequence now, death later | ✔ On the roof now; the haul is 8 s; thrown off is a walk, not a death. |
| 4 | A verb, not just a "don't" | ✔ Get down, knock them off, shove. |
| 5 | Someone gets blamed | ✔ Whoever stayed up there. |
| 6 | Fun to scream | ✔ "TREES! GET DOWN! GET DOWN!" |

## 10. Build cost

**Medium to large.** New: canopy stretches in the line generator (`linegen`, beside its tunnels; the art's tree kit
leaning in), the troop's model (a small biped, the Pickers' rig re-proportioned), the haul to the roof's edge (Draggers'
hang-over-the-side is near). About 2 agent-days of sim, linegen and bots, 2 of art (the canopy and the troop).

## 11. Open calls for the director

1. Thrown off (Split, a walk back to the train), or thrown under (a death)? Moth's call: off, never under.
