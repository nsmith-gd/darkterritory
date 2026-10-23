# THE RODRIDER — the thing riding the rods under your car

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **When the floor knocks, get off it.**

## 1. What it is

Hobos once rode the rods: the truss rods under a freight car, a foot above the sleepers. Something rides them now. It
catches the train at a slow bend or a stop, and once it's under, it knocks its way down the car's floor, feeling for feet,
and punches up through the boards to take an ankle.

The director, 23 Oct (line 28): "Now that we're like on the train, I just don't feel like there's any threat ... we need
more threats that can show up when I'm on the train." Boarding-first (App. F.1): it gets on at the known points, slow
bends and stands, like the Climbers.

## 2. Roster entry (GDD §21, INTERIOR)

**THE RODRIDER** · *under a car's floor, on the move*
Knocking under the boards, moving toward your feet.
> **RULE: when the floor knocks, get up high.**

**Sense:** vibration (footsteps through the floor). **Zone:** interior (under). **Want:** Kill. **Cost:** 3.

## 3. Silhouette and look

Seen only when it comes through: a long, flat, many-jointed arm, grey and greasy, ending in a hand with too many knuckles,
through a ragged hole in the boards; under the car (a crewmate leaning out over the side sees it), a flattened body like a
skinned seal hooked into the truss rods by its other limbs. Clips: `ride`, `knock`, `punch` (through), `grip`, `drag`,
`hit`, `drop` (death, falling to the track).

## 4. The tell (spec A.3/A.4)

- **The knock** (tier 1): two knocks under your feet, then two more a step along, and the nails squealing as a board
  lifts, 4–6 kHz. The knocks are low (the bed's band), so the squeal and the *pattern* (knock-knock, move, knock-knock)
  carry it; the floorboard under the next knock visibly lifts. Told from the Climbers' scrabble (2–5 kHz) by the squeal
  and that it comes from below.
- Nameable: "it's under the floor".

## 5. The rule (App. A.4 format)

```
BOARD     the train under 6 m/s (a slow bend, a stop) for 4 s → one gets under a car the crew is in
KNOCK     it follows the nearest footsteps on that car's floor, a board a second
          └ TELEGRAPH: the knocks and the board lifting under where it'll come (2 s)
PUNCH     someone on the boards over it when it punches → GRAB: their ankle; dragged toward the hole (10 s; at the hole,
          under the car)
          ├ a friend's blow on the arm, or the victim's own → it lets go and drops back under
          └ nobody on the floor (on crates, a bunk, a locker top, the roof, in the next car) → it misses, knocks on
KILL      a crewmate on the running board leaning down with a lamp sees it; 4 blows from there, or a gun round down the
          hole, and it drops to the track
```

## 6. Aboard or afoot; what it counters

**Aboard, at speed.** It counters **turtling in one car** (the crew holed up in a lit, shut car, safe from the Climbers and
the Choir) and **standing still in the cargo** (a crowd on the floor is many feet to knock under).

## 7. Compositions

- **Rodrider + Climbers / Choir:** the lit, shut car keeps those out (note 633); the floor isn't safe.
- **Rodrider + the Gannet:** get off the floor, up high, onto the roof, where the Gannet hunts walkers.
- **Rodrider + loose cargo:** a crate to stand on is the answer; the crates slide.

## 8. Early versus late

- **Early:** not before Frontier; one a night, in the rearmost crewed car.
- **Late:** two, in different cars; it knocks faster (a board each 0.7 s).

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The hand that comes up through the floor." |
| 2 | Answered better by two | ✔ One to free the grabbed, one out on the running board to kill it. |
| 3 | Consequence now, death later | ✔ The knock now; the drag is 10 s. |
| 4 | A verb, not just a "don't" | ✔ Get up high, hit the arm, lean out and kill it. |
| 5 | Someone gets blamed | ✔ Whoever stood still on the knock. |
| 6 | Fun to scream | ✔ "GET OFF THE FLOOR!" |

## 10. Build cost

**Medium.** New: a creature under a car (the Hotbox's place in a bogie is the model for it, note 367) that tracks a car-frame
position (the moving-frames rule: in the car's frame), "on the floor" per crewmate (the motor knows what they're standing
on), the hole drawn as a decal, the arm and the underbody models. About 2 agent-days of sim and bots (bots step onto a
crate on the knock), 1–2 of art.

## 11. Open calls for the director

1. Does the hole it leaves stay (a hole in the floor, cargo can fall through) until repaired with a wrench? Moth's call: no,
   it's a hole to look at only.
