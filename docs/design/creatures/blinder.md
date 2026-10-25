# THE BLINDER — the thing that grows over the cab's glass

> **CUT by the director, 23 Oct 2026** (`docs/playtest/2026-10-23.md` line 36): "I don't really like the blinder." Kept here for the record (queue #709, note 1004); not to be built.

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **The driver can't see. Someone else has to go out there.**

## 1. What it is

A flat, wet, leech-like sheet of a thing the size of a bedspread that comes out of the dark at the headlamp like a moth,
lands on the engine's smokebox front, and creeps up over the cab's front windows (the spectacle glass), spreading as it
goes. In a minute the driver is driving blind. The driver can't fix it from the regulator: someone else has to climb out
along the running board, beside the boiler at speed, and scrape it off.

## 2. Roster entry (GDD §21, STRUCTURAL)

**THE BLINDER** · *on the engine's front, on the move*
A grey wet sheet creeping over the cab's glass.
> **RULE: someone else goes out and scrapes it.**

**Sense:** light (the headlamp). **Zone:** structural (the engine). **Want:** Derail (through the driver). **Cost:** 2.

## 3. Silhouette and look

A ragged sheet of grey-green flesh, thin as a skate's wing, a ring of small hooked mouths on its underside and a fringe of
cilia that ripple. On the glass from inside: a translucent smear spreading from the bottom up, veined, lit from behind by
the headlamp's spill. Clips: `fly` (rippling in the dark), `land`, `creep`, `cover`, `scraped` (peeling back), `death`
(dropping off into the dark).

## 4. The tell (spec A.3/A.4)

- **The landing** (tier 1): a wet slap on the smokebox, then a suckering squeak, 5–8 kHz, rhythmic as it creeps (one
  squeak a second). Heard in the cab and on the engine. Told from the car fire (6–9 kHz, crackling, in a car) by its
  steady rhythm and that it's only at the engine.
- **Seen:** the smear rising from the bottom of the glass, a band of 10% every 6 s.

## 5. The rule (App. A.4 format)

```
COME      the headlamp on, the train over 10 m/s in open country for 60 s, the same crewmate at the regulator for 3
          minutes or more → one comes in at the lamp
LAND      the slap and the squeak; it creeps up the glass: 0 → 100% covered over 60 s
          └ TELEGRAPH: the squeak and the smear rising (the driver has the full minute)
COVER     each 10% is the forward view (and the lamp's reach on the cab map's preview) gone that much
SCRAPE    a crewmate on the running board beside the smokebox, holding Use: 6 s to peel it off (it bites: 10 health, once)
          ├ a blow from there: 3 kill it
          ├ the driver leaves the regulator to do it → the cab's empty (the Track Doll's opening; speed holds)
          └ the train under 4 m/s for 5 s → it drops off by itself
```

**It kills nobody.** The danger is a blind driver at a bend they can't see: the bends' boards and the cab map's red marks
still stand (§22), so a blind derailment is still the driver's, who could slow.

## 6. Aboard or afoot; what it counters

**Aboard, at speed.** It counters **one player hogging the cab** (it comes for a driver who's held the regulator 3 minutes,
and its answer is someone else) and **the solo top-speed strategy** (fast, lamp on, alone up front).

## 7. Compositions

- **Blinder + Track Doll:** "stop before you hit the doll", with the glass smeared; the driver who goes out to scrape it
  leaves the cab to the doll.
- **Blinder + Gannet:** the running board at speed, in open country, is where the Gannet hunts walkers.
- **Blinder + Stoker:** the fireman is the obvious one to go out; then nobody watches the coal.

## 8. Early versus late

- **Early:** from Frontier, one a night, 90 s to cover.
- **Late:** 45 s to cover; a second can come the minute the first is off.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The thing that smears over the driver's window." |
| 2 | Answered better by two | ✔ Built on it: the driver can't scrape it and drive. |
| 3 | Consequence now, death later | ✔ The glass now; the bend later. |
| 4 | A verb, not just a "don't" | ✔ Climb out and scrape it; slow down. |
| 5 | Someone gets blamed | ✔ Whoever was meant to go out, or the driver who didn't slow. |
| 6 | Fun to scream | ✔ "I CAN'T SEE! SOMEBODY GET OUT THERE!" |

## 10. Build cost

**Small to medium.** The running board walk exists (the engine's sides); the cover is a material on the cab's glass and a
cut to the forward view's draw distance; the driver's time at the regulator is counted already for the cab (who's at the
controls). New: the sheet model and its clips, one sound. About 1 agent-day of sim and bots (a bot fireman goes out), 1 of
art.

## 11. Open calls for the director

1. The 3-minute tenure trigger (it counters the cab hog), or any driver? Moth's call: tenure, so swapping drivers is a
   real answer.
2. Does it dim the headlamp too? Moth's call: yes, half, so the doll's face shows later.
