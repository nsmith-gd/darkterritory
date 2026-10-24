# THE TAPPER — the hand on the gunner's shoulder

*Creature proposal, Sloe (queue #709, ARCHITECTURE §8 note 1004), 23 Oct 2026, for the director's rule "every creature's purpose is to foil some sort of behavior" (`docs/playtest/2026-10-23.md` line 36). **Status: built behind a flag, off, awaiting the director's verdict** (`enemies.json` `tapper.on`; the `r714-tapper` edition turns it on; Rook, queue #714, note 1011). Numbers are a first pass in the roster's units.*

> **When it taps, look round. Or have someone at your back.**

## 1. What it is

A long, folded thing that rides the train's roofs facing forward like a passenger, arms twice the length of its body. The
rear gun faces backward, and whoever's on it watches the line behind for the Cinder Hounds. The Tapper comes over the roof
from in front, where the gunner can't see, and taps them on the shoulder. The second time, it takes them.

## 2. Roster entry (GDD §21, FLANK)

**THE TAPPER** · *the roof behind the rear gun*
Two knuckle-knocks on the gun's shield, from behind you.
> **RULE: when it taps, look round.**

**Sense:** sight (a crewmate's back). **Zone:** flank (the rear car's roof). **Want:** Split (off the train). **Cost:** 2.

## 3. Silhouette and look

On the roof, flat and folded like a broken umbrella; standing, three metres of arms and a small blank head. Its hand on
the shield, at the edge of the gunner's view: grey fingers, too many joints. Clips: `crawl`, `tap`, `reach`, `lift`
(the gunner out of the seat), `flinch` (looked at: it folds flat), `knocked` (off the roof), `death`.

## 4. The tell (spec A.3/A.4)

- **The tap** (tier 1): two dry knocks on the gun's iron shield, 600–900 Hz, close behind the gunner's head. Told from the
  Hotbox's knock (in a truck, under the car, rhythmic) by being two and overhead; from the guns' own clatter by coming
  when the gun's quiet.
- **Seen:** its fingers on the shield at the edge of the view; from anywhere else on the train, a long shape on the rear roof.

## 5. The rule (App. A.4 format)

```
COME      aboard at speed, one crewmate on the rear gun, nobody else on that car or its roof, for 60 s → it climbs on
TAP       it taps
          └ TELEGRAPH: the tap; then 5 s
          ├ the gunner turns the camera to it (a look of 1 s) → it flinches flat and backs off 20 s (a look holds it, not kills)
          ├ a second crewmate on the car or its roof → it folds and goes over the side
          ├ a blow knocks it off (2 blows from the gun's seat can't be landed: the gunner has to stand); a gun round kills it
          └ else
LIFT      GRAB: lifted out of the seat over the roof's edge (10 s, held over the line)
          └ a friend's blow, or Shove, makes it drop them on the roof
PUNISH    dropped off the train at speed: left behind (#695's), the Mourners if it kills
```

## 6. Aboard or afoot; what it counters

**Aboard, at speed.** It foils **the back gun alone too long** (Rook's `gunning`, past `dwell` seconds, nobody else on the
car). Taking turns, or two on the rear, answers it.

## 7. Compositions

- **The Tapper + Cinder Hounds:** the Hounds want the gun crewed and its eyes on the line; the Tapper wants someone
  watching the gunner's back. The rear needs two.
- **The Tapper + the Gannet / the Drops:** the friend who keeps them company is on a roof at speed.
- **The Tapper + the Close:** everyone else in one car, safe from the Tapper and not from the Close.

## 8. Early versus late

- **Early:** from Frontier; after 90 s alone; 5 s after the tap; the first look holds it 30 s.
- **Late:** after 45 s; 3 s; a look holds it 10 s, and two can come.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The thing that taps the gunner on the shoulder." |
| 2 | Answered better by two | ✔ It never comes with two on the car. |
| 3 | Consequence now, death later | ✔ The tap now; the lift is 10 s over the line. |
| 4 | A verb, not just a "don't" | ✔ Look round, stand and hit it, bring a friend. |
| 5 | Someone gets blamed | ✔ "You left him back there alone for ten minutes." |
| 6 | Fun to scream | ✔ "SOMETHING JUST TAPPED ME!" |

## 10. Build cost

**Medium.** A long-limbed rig (the Hanger's arms on the Brakeman's roof walk), a seat-lift grab (the Gannet's pin, from a
seat), the look check (Tippy Toesie's, built). About 1.5 agent-days of sim and bots, 1.5 of art, one sound.
