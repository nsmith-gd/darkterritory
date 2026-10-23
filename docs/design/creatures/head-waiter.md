# THE HEAD WAITER — your table is ready

*Creature proposal, Sloe (queue #709, ARCHITECTURE §8 note 1004), 23 Oct 2026, for the director's rule "every creature's purpose is to foil some sort of behavior" (`docs/playtest/2026-10-23.md` line 36). **Status: built behind a flag, off, awaiting the director's verdict (`nathaniel2-sloe-conduct-foils`)** (enemies.json `headWaiter.on`; `editions/a713-headwaiter` turns it on; note 1008, queue #713). Numbers are a first pass in the roster's units.*

> **The director, 23 Oct (line 36):** "we have this whole unlock the patio with the chairs and the beers and the barbecue,
> which I think is great. It's one of my absolute favorite cosmetics ... But if you spend too much time up there, something
> should come and get you."

> **When the glass rings, back to work.**

## 1. What it is

A tall, thin figure in a black waistcoat and a long white apron, a napkin over one arm, a silver tray balanced on its
fingertips. It comes up the side of the car onto the BBQ deck, the hot tub or the dining car, wherever a crewmate has been
lounging too long, and waits on them: it sets down a plate under a cover, and rings a spoon on a glass. Then it waits for
them to finish. When they don't get up, it clears the table, and them with it.

## 2. Roster entry (GDD §21, CORRUPTED HUMANS)

**THE HEAD WAITER** · *at the BBQ deck, the hot tub, the dining car*
A spoon rung on a glass, behind your chair.
> **RULE: when the glass rings, back to work.**

**Sense:** sight (a lounger). **Zone:** the train's cosmetic stations. **Want:** Split (off the train). **Cost:** 1.

## 3. Silhouette and look

Too tall for the deck's awning, bowing under it; a white face with a waiter's thin moustache painted on; the tray never
tips. The covered plate it sets down is the joke and the horror: the cover lifted (by anyone) shows a crewmate's own hat,
or their name in the tally's hand. Clips: `climb`, `serve` (the plate down, the glass rung), `wait` (hands behind its
back, bent at the waist), `clear` (the lounger lifted like a tablecloth), `dismissed` (a bow, and over the side),
`death`.

## 4. The tell (spec A.3/A.4)

- **The glass** (tier 1): a spoon on a wine glass, three clear rings, 4.6–5.6 kHz, from right behind the lounger. Told
  from the Fire Flies (9–12 kHz) and the car fire (6–9 kHz) by band and by being rung; from the cab's bell (1.2 kHz) by
  pitch.
- **Seen:** the covered plate set down beside them; from anywhere aboard, a tall figure on the deck.

## 5. The rule (App. A.4 format)

```
COME      a crewmate lounging (PlayerFlags.Lounging: a deck chair, the hot tub, the hammock, a dining seat) for 90 s of the
          last 120, while the train runs or something needs doing (a fire low, a breach, a stop's work) → it climbs aboard
SERVE     it sets down the plate and rings
          └ TELEGRAPH: the glass; then 15 s
          ├ the lounger gets up and does something (any doing that isn't lounging, riding or idling, for 5 s) → it bows,
          │ clears the plate and goes over the side
          ├ a crewmate tips it (Use on it: "the bill", a coin from the night's cargo, 20) → it bows and goes
          ├ 3 blows → it drops the tray and goes; 6 kill it
          └ else
CLEAR     GRAB: the lounger lifted out of the seat and carried to the rail (8 s), then thrown off
          └ a friend's blow or Shove frees them
PUNISH    thrown off: left behind (#695's)
```

The 15 s are long on purpose: it's a joke first, a threat second, and the getting up is the whole lesson.

## 6. Aboard or afoot; what it counters

**Aboard, at the cosmetics.** It foils **lounging on the cosmetics without doing the objectives**, without ever stopping
anyone enjoying them: a few minutes in the hot tub on a quiet stretch are free; it comes when there's work and the lounger
lets the others do it.

## 7. Compositions

- **The Head Waiter + the Stoker / the Hotbox:** the jobs it sends you back to are the ones being let slide.
- **The Head Waiter + the Gannet:** thrown off in open country, the Gannet's ground.
- **The Head Waiter + the Close:** go back to work, but not all into one car.

## 8. Early versus late

- **Early:** whenever a cosmetic station is bought (the catalogue's, note 678); 90 s, 15 s to get up.
- **Late:** 60 s, 10 s; the tip costs 50.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The waiter that comes for you if you sit in the hot tub too long." |
| 2 | Answered better by two | ✔ A friend can tip it, or pull you off it. |
| 3 | Consequence now, death later | ✔ The glass now; 15 s, then an 8 s carry. |
| 4 | A verb, not just a "don't" | ✔ Get up and work, tip it, hit it. |
| 5 | Someone gets blamed | ✔ "He was in the hot tub the whole time the fire was out." |
| 6 | Fun to scream | ✔ "THE WAITER'S GOT DAVE! TIP HIM, SOMEBODY TIP HIM!" |

## 10. Build cost

**Small–medium.** `PlayerFlags.Lounging` and `Bbq.LoungingIn` already say who's lounging where; the rest is the
townsfolk kit's tall figure, a tray and plate prop, a carry (the Gannet's pin to the rail). About 1 agent-day of sim and
bots, 1.5 of art (the figure is the cost), one sound. The tally's clerk gets a line ("one diner, cleared").
