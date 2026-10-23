# THE SUMP — the thing in the cellar water

*Creature proposal, Sloe (queue #709, ARCHITECTURE §8 note 1004), 23 Oct 2026, for the director's rule "every creature's purpose is to foil some sort of behavior" (`docs/playtest/2026-10-23.md` line 36). **Status: a proposal for the director; nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **Off the floor, or not down here alone.**

## 1. What it is

Every flooded basement (#710's, note 1005: dark, the better finds lower and deeper) has an inch of black water on its
floor. Something lives in the drain under it: long, eel-pale, all mouth. It never rises for two. When one crewmate is down
there alone, the water starts moving toward them.

## 2. Roster entry (GDD §21, HOUSE)

**THE SUMP** · *the water on a basement floor*
Rings on the water, coming toward you.
> **RULE: off the floor, or not alone.**

**Sense:** vibration (steps in the water). **Zone:** village basements. **Want:** Kill. **Cost:** 1 (the house's own, laid
with the basement; the director's targeting weights it toward a lone looter, Rook's `looting` below ground).

## 3. Silhouette and look

Nothing, at first: the basement's water, lit by your lamp. Then rings spreading from the drain, a wake coming toward your
feet, and a pale back breaking the surface once. Clips: `still`, `wake`, `rise` (out of the water to the waist,
mouth open), `grab` (round the ankles), `sink` (back into the drain), `death`.

## 4. The tell (spec A.3/A.4)

- **The drip stops** (tier 1): every basement drips (its bed, 1.2–2 kHz ticks); when the Sump moves, the drips go
  silent and a slow gulp, 200–400 Hz, comes from the drain. The *absence* is the first tell, the gulp the second.
- **Seen:** the rings, toward you, in the lamp's light (the basement is dark, but water shines in any light).

## 5. The rule (App. A.4 format)

```
WAIT      a crewmate in a basement, nobody else down there, for 15 s → it wakes
          └ TELEGRAPH: the drips stop, the gulp, the rings (6 s before it reaches them)
          ├ a second crewmate comes down → it sinks back
          ├ the crewmate gets off the floor (the stairs, a crate, a shelf, the window's sill) → it circles below and waits
          └ else
GRAB      round the ankles, dragged toward the drain at 0.5 m/s (12 s)
          └ a friend's blow, or a carry, frees them → it sinks 30 s
PUNISH    pulled into the drain: dead (the body stays in the water: Mourners' ground)
```

## 6. Aboard or afoot; what it counters

**Afoot, in a village basement.** It foils **the basement alone**. High risk, high reward (the director): the basement's
better finds are still there for two.

## 7. Compositions

- **The Sump + the Nailer:** boarded into the basement with the water moving: get up on the sill and crawl out the window,
  where the Nailer waits. Two together, or the house is a trap.
- **The Sump + the Lurcher:** the friend you call down crosses the yard alone.
- **The Sump + the Hanger / the Lodger:** the house's own things above, the water below.

## 8. Early versus late

- **Early:** from the first nights with #710's basements; 15 s, 6 s of tell.
- **Late:** 8 s; 4 s; it stays up 10 s after a second crewmate comes down.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The thing in the cellar water." |
| 2 | Answered better by two | ✔ It never rises for two. |
| 3 | Consequence now, death later | ✔ The drips stop now; the drag is 12 s. |
| 4 | A verb, not just a "don't" | ✔ Climb up, call someone down, hit it. |
| 5 | Someone gets blamed | ✔ Whoever went down alone. |
| 6 | Fun to scream | ✔ "THE WATER'S MOVING! COME DOWN, COME DOWN!" |

## 10. Build cost

**Small**, after #710. Its water is a floor plane with ripples (the lakes' shader, small), its body a simple eel rig;
"in a basement" is #710's floor index under `StopWalls.HouseAt`. About 1 agent-day of sim and bots, 1 of art, the drip bed
and the gulp.
