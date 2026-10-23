# THE JACKDAWS — they settle on whoever did it

*Creature proposal, Sloe (queue #709, ARCHITECTURE §8 note 1004), 23 Oct 2026, for the director's rule "every creature's purpose is to foil some sort of behavior" (`docs/playtest/2026-10-23.md` line 36). **Status: a proposal for the director; nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **Make it right, or they take you.**

## 1. What it is

Black birds that follow the train from town to town, and know who did what. When a crewmate wrongs another (shoves them off
a moving train, pulls the pin on a car a friend is standing in, drives off and leaves them, puts a gun round into them), a
jackdaw comes down out of the dark and settles on the one who did it. Everyone can see it. More wrongs, more birds. At three,
they lift.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE JACKDAWS** · *on the shoulders of whoever did it*
A black bird on your shoulder, watching the one you wronged.
> **RULE: make it right, or they take you.**

**Sense:** the night's grudges (who wronged whom). **Zone:** anywhere. **Want:** Split. **Cost:** 0 (they're the
griefer's own; the director's budget isn't spent on them).

## 3. Silhouette and look

A jackdaw: small, grey-naped, a pale eye. On the shoulder, looking at the wronged crewmate wherever they are (so the crew
sees whom). Three on one player: a hunched mantle of birds. Clips: `settle`, `watch` (head turning to the wronged),
`shoo`, `lift` (all three, the player off the ground), `scatter`.

## 4. The tell (spec A.3/A.4)

- **The chack** (tier 1): a jackdaw's call, 1.4–2.4 kHz, sharp, as each settles; and once a minute from each bird after.
  Told from the Gannet's cry (lower, longer, open country) by being short and close.
- **Seen:** the birds, and which way they look. The blame is on the griefer's shoulder, which is the joke.

## 5. The rule (App. A.4 format)

```
SETTLE    a wrong: Shove that drops a crewmate off a train moving over 4 m/s (World.ShovedBy), a coupling pulled with a
          crewmate on the far car, the train driven off with a crewmate left behind within 40 m, a gun round or a blow
          landed on a crewmate → a jackdaw settles on the one who did it, watching the one wronged
          (horseplay that harms nobody — a shove on a standing train, the gags — never counts)
          └ TELEGRAPH: the chack, and the bird, for everyone
MAKE RIGHT ├ the wronged crewmate holds Use on the griefer for 2 s (forgiven) → one bird goes
          ├ the griefer rescues, carries or revives the one they wronged → one goes
          └ else they stay the night
LIFT      three on one player → they lift them 4 m and carry them off the train or into the dark (8 s)
          └ the crew (anyone but the griefer) shoos them: 3 blows on the birds → they scatter, one comes back
PUNISH    carried off: left behind
```

## 6. Aboard or afoot; what it counters

**Both.** It foils **griefing**, and turns the blame into something the crew can see and the wronged can lift: the victim
holds the power. It never stops horseplay; the first wrong is a warning everyone sees.

## 7. Compositions

- **The Jackdaws + anything:** a bird on your shoulder is a crewmate everyone is watching, and the wronged one is the one
  who can forgive. With the Bellman's bell, the griefer's back is a crewmate's job, which makes them ask.
- **The Jackdaws + the Mourners:** a crewmate killed by a shove off the train lies where the Mourners come.

## 8. Early versus late

- **Early and late alike:** a rule of the crew, not a difficulty; three wrongs lift, every night.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The birds that sit on whoever pushed you off." |
| 2 | Answered better by two | ✔ Only the wronged can forgive; only the crew can shoo. |
| 3 | Consequence now, death later | ✔ The bird now; three wrongs, then 8 s. |
| 4 | A verb, not just a "don't" | ✔ Forgive, make it right, shoo them. |
| 5 | Someone gets blamed | ✔ Literally, on their shoulder. |
| 6 | Fun to scream | ✔ "THREE BIRDS. THREE. WHAT DID YOU DO?" |

## 10. Build cost

**Small.** The wrongs are already logged: `World.ShovedBy`, the uncouple, `LeftBehind`, the guns' hits; the incident log
(`IncidentLog`) names who. A bird prop on a shoulder bone, a lift (the Gannet's). About 1 agent-day of sim and bots (bots
never grief, but forgive), half a day of art.
