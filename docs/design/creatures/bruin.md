# THE BRUIN — the bear that gets into the car

*Creature proposal, Sloe (queue #709, ARCHITECTURE §8 note 1004), 23 Oct 2026: one of the forest's heavies (`docs/playtest/2026-10-23.md` line 36: "big, kind of scary, that come out of the trees ... they can kill the player pretty easily if they get grabbed ... They can get into cars and cause problems ... relatively hard to take down. Like, stopping in the wild feels like it should feel scary"), for #711's big forests (Oak, note 1006). **Status: built behind a flag, off, awaiting the director's verdict** (`enemies.json` `bruin.on`; the `r712-bruin` edition turns it on; Rook, queue #712, note 1007; the inbox's `nathaniel2-sloe-forest-heavies`). Numbers are a first pass in the roster's units.*

> **Never alone with it; never face it.**

## 1. What it is

What's left of a great bear: twice the weight it should be, its fur gone to grey moss, its chest opened and something
living in there that breathes for it. It comes out of the forest at a standing train for the cargo, climbs into the car it
can smell food in (the BBQ deck's stores, the loot, a body), and stays there feeding. It mauls whoever stands in its way.
Nobody kills it alone: it only charges what's in front of it, and it's always facing someone.

## 2. Roster entry (GDD §21, OUTSIDE: the wild)

**THE BRUIN** · *out of the forest, into a car*
A huffing, and the car rocking on its springs.
> **RULE: never alone with it; never face it.**

**Sense:** scent (cargo, food, the dead). **Zone:** outside, the wilderness, then interior. **Want:** Cargo, then Kill.
**Cost:** 4 (a heavy).

## 3. Silhouette and look

On all fours, as tall as a man's shoulder; up on its hind legs in a car, its head in the roof. The chest's opening shows a
pale thing breathing inside the ribs. Clips: `come` (a lumbering run), `climb` (into a car's door), `feed` (head down
in the cargo, the car rocking), `rise`, `charge`, `maul`, `turn`, `stagger`, `death`.

## 4. The tell (spec A.3/A.4)

- **The huff** (tier 1): a deep chuffing huff, 60–150 Hz, with a wet whistle from its chest at 700–900 Hz, from 40 m;
  then, inside, the car's springs rocking (the car's own creak, louder) and tearing crates. Told from the Moose (a bellow,
  higher, in the open) by the chest's whistle.
- **Seen:** the car rocking at a stand; its breath steaming in the door.

## 5. The rule (App. A.4 format)

```
COME      the train stood in the wilderness for 45 s, with cargo, food or a body aboard → one comes out of the trees to the
          car with the most of it, at 4 m/s
          └ TELEGRAPH: the huff from 40 m (10 s)
FEED      it climbs in and feeds: 30 cargo a second gone, the car rocking
          ├ the train moving off → it rides along, feeding, until driven off (it's aboard now)
          └ anyone in the car or its door, facing it → it rises and CHARGES them (3 s)
MAUL      the charged crewmate, caught within 2 m → GRAB: down under it, 12 a second, dead in 8 s
          └ a blow from behind or the side → it drops them and turns on the hitter (it always charges whoever's in front)
DRIVE OFF 20 blows (5 gun rounds) to kill it, or 8 to drive it out of the car and back to the trees
          └ alone: whoever's in front gets charged; with three, it spins between them and never lands a maul
PUNISH    a car of cargo eaten, or a maul
```

## 6. Aboard or afoot; what it counters

**Aboard (in a car) and afoot at a stand in the wild.** It foils **leaving the train unwatched to go loot** at a
wilderness stop (everyone afield: Rook's `afield` for the whole crew, nobody aboard) and **the lone fighter**: one person
can't hurt it without being in front of it.

## 7. Compositions

- **The Bruin + the Windfall:** the Bruin wants you round its sides; flanking is moving, and moving is what the Windfall
  feels. Flank slowly.
- **The Bruin + the Freight Beetle / the Pickers:** the cargo eaten twice over; guard the car or lose it.
- **The Bruin + the Head Waiter:** the BBQ deck's food is what it smells first.

## 8. Early versus late

- **Early:** one on Frontier's wilderness stands, after 60 s; 30 blows' worth of patience: it leaves a car after 40 s fed.
- **Late:** after 30 s; it never leaves; two when the night's cargo is high.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The rotten bear that climbs in and eats the cargo." |
| 2 | Answered better by two | ✔ Two flank it; one alone is always in front. |
| 3 | Consequence now, death later | ✔ The huff now, the cargo going; the maul is 8 s. |
| 4 | A verb, not just a "don't" | ✔ Flank it, drive it out, take turns in front. |
| 5 | Someone gets blamed | ✔ Whoever left the loot car alone in the woods. |
| 6 | Fun to scream | ✔ "IT'S IN THE VAN! IT'S EATING EVERYTHING!" |

## 10. Build cost

**Medium–large.** A quadruped rig (the Moose's, heavier), a cargo drain (the Freight Beetle's, built), the "faces whoever
hit it" rule (shared with the Windfall), riding in a car (the Passenger's car frame). About 2.5 agent-days of sim and bots,
2.5 of art, the huff.
