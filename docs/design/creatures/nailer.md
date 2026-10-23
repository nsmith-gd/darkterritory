# THE NAILER — it boards you in

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **Hear the hammer, get out. Or have a friend outside.**

## 1. What it is

A corrupted carpenter, hunched, a nail-bag at its belt and a mouth full of nails, that follows the crew to a village. When
someone's searching a house on their own, it comes up outside and boards the doors and windows shut, one after another,
hammering. Then it waits at the last window for the one inside to climb out.

Rook's countermeasure table (note 999, queue #703): "no director-sent threat finds a looter inside a house". The house
creatures (the Lodger, the Hanger, the Hollow House, the Householder) are the houses' own; the Nailer is the one the
director can send *at* the looter.

## 2. Roster entry (GDD §21, CORRUPTED HUMANS)

**THE NAILER** · *outside a village house with someone in it*
Hammering at the house you're in.
> **RULE: hear the hammer, get out.**

**Sense:** sound (a crewmate searching). **Zone:** outside (village houses). **Want:** Kill. **Cost:** 2.

## 3. Silhouette and look

Short and wide, bent nearly double, a carpenter's apron black with age, both arms too long so the hammer hangs at its
ankle; its mouth bristles with nails held in its lips, and it spits them into its hand. Clips: `walk`, `board` (a plank
up, three blows), `wait` (crouched under the last window), `grab` (through the window), `prised` (staggering back), `flee`,
`death`.

## 4. The tell (spec A.3/A.4)

- **The hammering** (tier 1): three hammer blows on a nail, a hard ring 2.2–3.5 kHz, a pause, three more, from the
  door or window it's boarding, heard inside the house. Told from the Climbers' scrabble and the Stragglers' rail taps by
  its threes, and from the house creatures' sounds (the boards over what waits, note 732) by being outside and metallic.
- **Seen:** the doorway darkening plank by plank.

## 5. The rule (App. A.4 format)

```
COME      a crewmate searching a village house alone (nobody else within 10 m) for 20 s → it comes to that house
BOARD     each opening (door, window) boarded in 4 s, the nearest to the looter last
          └ TELEGRAPH: the hammering, from the first opening (the looter has the whole boarding to get out)
          ├ a crewmate outside comes within 4 m → it flees (it never stands against two, or one outside)
          └ 3 blows kill it
WAIT      all but one opening boarded → it crouches under that one; whoever climbs out through it → GRAB: dragged into the
          dark (10 s; a friend's blow frees)
PRISE     a board is prised off from either side with the crowbar (6 s, or 3 s with two)
```

## 6. Aboard or afoot; what it counters

**Afoot, in a village.** It counters **looting alone** (one crewmate clearing the houses while the rest hold the yard)
and pairs with Rook's targeting (`looting`).

## 7. Compositions

- **Nailer + the Lodger or the Hanger:** boarded in with the house's own thing.
- **Nailer + the Stragglers / dawn:** time spent prising boards is time at the stop.
- **Nailer + the Lurcher:** the friend who'd come to prise you out has to cross the open alone.

## 8. Early versus late

- **Early:** none on Local; one on Frontier, slow (6 s an opening).
- **Late:** 3 s an opening; two at once on a village.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The thing that boards you into the house." |
| 2 | Answered better by two | ✔ A friend outside drives it off or prises you out. |
| 3 | Consequence now, death later | ✔ The hammering now; the grab only at the last window, 10 s. |
| 4 | A verb, not just a "don't" | ✔ Get out, prise, drive it off. |
| 5 | Someone gets blamed | ✔ Whoever went in alone. |
| 6 | Fun to scream | ✔ "IT'S BOARDING ME IN! SOMEBODY COME ROUND THE BACK!" |

## 10. Build cost

**Medium.** The houses' openings are built (`StopWalls.OpenHouses`, real doors and windows, note 326/586); a boarded
opening is a shut door that won't open, plus a plank prop; the figure from the townsfolk kit. About 2 agent-days of sim and
bots, 1 of art, one sound.

## 11. Open calls for the director

1. Do the boards stay for the rest of the night (a house closed), or come down when it's driven off? Moth's call: they stay
   until prised.
