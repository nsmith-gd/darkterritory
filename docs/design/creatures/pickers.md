# THE PICKERS — they take the yard's loot if you don't get there first

*Creature design, G1 (enemy design), 9 Oct 2026. **Status: the director's pick to prototype (9 Oct 2026), from G1's
proposal of new house and yard creatures (queue #315, ARCHITECTURE §8 note 574).** Every call the director's answer left
open is taken below and marked **(G1's call)**. Numbers are a first pass in the roster's units (player health 100, run
5.5 m/s, a crowbar blow = 1).*

> **The director, 9 Oct 2026:** "Right now I'm feeling like I'm walking through an abandoned village and there is nothing
> ... other stuff that is more frequently active in the yard." On the proposal: "The picker is interesting. I think that's
> up there for something that we should prototype and put in."

---

## 1. What it is

Small scavengers that live in a yard's drains. When a train comes to a stand at the yard, they come up out of the drains at
the foot of the sheds a little after, and race the crew for the loose crates on the ground. Each one takes a crate and runs
it back down its drain, gone. A heavy crate takes two of them. They keep away from people: walk up to one carrying a crate
and it bites you and drops it. A blow kills one, and the rest scatter back to their drains for a few seconds before coming
out again. Nobody dies of them: they put a clock on the stop and make the yard move.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE PICKERS** · *in a yard's drains, out when a train stands*
Knee-high, long-armed scavengers that scuttle out of the drains and carry off whatever crate nobody's standing by.
> **RULE: get there first.**

**Sense:** sight (who's near the crates). **Zone:** outside (facilities). **Want:** Cargo. **Cost:** none (the stop's own,
not the director's; G1's call).

## 3. What it looks like (art brief, GDD §26.5; G1's draft, for the director's look)

Knee-high (about 0.5 m at the shoulder, 0.9 m long), lean and quick, built to carry: a narrow, hunched body like a
starved hare's, grey-brown and patchy, the skin tight over the ribs; long forelimbs ending in big, pale, many-jointed
hands for gripping a crate's edges; short hind legs for scuttling; a small eyeless head mostly mouth, needle teeth, and
wide, cupped ears. Coal-black from the drains to the elbows and knees. They run on all fours, and carry a crate over their
heads on their long arms, upright on their hind legs, so a yard of them reads at a distance as crates hurrying along by
themselves.

**Clips:** `scuttle` (all fours, quick), `carry` (upright, the crate over its head), `heave` (two under a heavy crate),
`grab` (taking a crate up), `bite` (a lunge and snap), `scatter` (a startled bolt), `emerge` (out of a drain),
`descend` (into a drain), `hit`, `death`.

## 4. How it sounds (a request to the audio chat)

- **Out of the drains** (the tell): a grating's scrape and a chitter, then many small feet on gravel.
- **Carrying:** a crate's knock and scrape and a chattering pant.
- **Biting:** a snap and a hiss. **Scattering:** a burst of squeals.

## 5. Behaviour tree (App. A.4 format)

### THE PICKERS · sight
```
WAIT      a train comes to a stand at a facility with loose crates within 80 m → after 20 s, a group comes up
          out of the drains at the foot of the yard's sheds (3 Local, 4 Frontier, 6 Dead Lines, 8 Deep, + 1 for every two
          of the crew past two), one drain to each in turn
PICK      each goes for the nearest loose crate on the ground that no one's guarding (a living crewmate within 4 m)
          and no other Picker has; a heavy crate takes two (the second joins the first's)
CARRY     it takes the crate up and runs it to its drain (2.6 m/s; a heavy crate 1.6 m/s with two), the shortest way
          ├ at its drain → down it goes with the crate: the crate's gone (the run's report counts it)
          ├ a crewmate within 1.2 m → it BITES them (8; never a kill), drops the crate and scatters
          └ the crate's taken up by a crewmate meanwhile → it lets go
SCATTER   any of them struck, or one killed within 10 m → every one within 10 m drops what it has and runs to its drain
          for 6 s, then comes out again
HIT       one blow kills one
LEAVE     the train starts away from the stop → every one goes down its drain
NEVER     they never take a crate in a car, on the train, or carried; never one being guarded; they never kill
```

## 6. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "Little things that steal the crates if nobody's standing by them." |
| 2 | Answered better by two | ✔ One guards the pile while the other loads; or one chases while one carries. |
| 3 | Consequence now, death later | ✔ The loot going now; the scrip it would have paid later. |
| 4 | A verb, not just a "don't" | ✔ Race them, guard the pile, chase them down, take it back. |
| 5 | Someone gets blamed | ✔ "You left the pile!" |
| 6 | Fun to scream | ✔ "THEY'VE GOT THE CRATE, THEY'VE GOT THE CRATE!" |

## 7. Spawn rules (App. B.4 row)

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Pickers** | Out of the drains at a yard's sheds, 20 s after the train stands | **Every tier** · a facility with loose crates · one group at a stop · not the director's (no cost, not on the caps) | 3 / 4 / 6 / 8 by tier, + 1 per two crew past two |

## 8. Contradictions (App. B.1 conflict table)

| Pair | The bind |
|---|---|
| **Facility loading + the Pickers** | Load fast vs. someone has to stand by the pile |
| **The Freight Beetle + the Pickers** | Stand where you want the crate not to go vs. stand by the crate so it isn't taken |
| **Followers + the Pickers** | Stand on the grounds to guard the pile vs. standing on the grounds is what the Followers take |

## 9. First-pass numbers (`enemies.json` `pickers`)

| Field | Value | Why |
|---|---|---|
| `after` | 20 s | the crew's head start: a crew that moves at once beats them |
| `countByTier` / `perTwoCrew` | 3, 4, 6, 8 / 1 | |
| `facilityReach` | 80 m | the Beetle's |
| `guardWithin` | 4 m | standing by the pile saves it |
| `run` / `carry` / `carryHeavy` | 4.8 / 2.6 / 1.6 m/s | a player (5.5) can catch one, just |
| `biteWithin` / `bite` / `biteEvery` | 1.2 m / 8 / 2 s | a crate taken back costs a little |
| `scatterWithin` / `scatterSeconds` | 10 m / 6 s | |
| `health` | 1 | frail |
| `drains` | 3 | at most, the yard's sheds' |

## 10. What the harness verifies

- `PickersTests`: a group comes out 20 s after the train stands at a facility with loose crates (none at a stop with
  none); each takes the nearest unguarded crate and runs it down its drain (gone); a guarded crate is never taken; a heavy
  crate takes two; a crewmate close is bitten (never killed) and the crate dropped; a blow kills one and scatters those
  near; they go down their drains when the train leaves; deterministic.
- The bots: the stop's crate hands club a Picker carrying a crate within reach, and take the crate back.

## 11. Decisions taken (G1's calls, for the director)

1. **Crates only** (the yard's loose loot); never a village house's finds, never what's in a car.
2. **Not the director's:** they come to every yard stop with loose crates, every tier, and cost nothing.
3. **Drains at the yard's sheds,** found from the stop's sheds when the train arrives, not a site of the stop generator's
   own (so no stop's layout or its seeds change).
4. **Harmless but for the bite;** one blow kills one.
