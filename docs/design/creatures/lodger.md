# THE LODGER — it shrieks, then it kills

*Creature design, G1 (enemy design), 9 Oct 2026. **Status: the director's pick (9 Oct 2026), from G1's house-creature
proposals (queue #316, ARCHITECTURE §8 note 583).** Calls the director's answer left open are marked **(G1's call)**.
Numbers are a first pass in the roster's units (player health 100, run 5.5 m/s, a crowbar blow = 1).*

> **The director, 9 Oct 2026:** "I think it's okay if it's like one of those terrifying hides in the darkness. It's a, like
> a one-hit kill. It telegraphs with its shrill shriek to really scare the player. And then it maybe just has like a
> slightly wider than normal pursuit radius before it'll go back to its little room."

---

## 1. What it is

A long, starved thing folded into the darkest corner of a village house: the back room, the corner furthest from the door.
You hear it before you see it, breathing, from the next room. Stay in its house a moment, or come close to it, and it
SHRIEKS: a shrill scream, the only warning you get. Then it lunges at where you were. If it reaches you, you're dead. Missed,
it chases you out of the house, further than most creatures will, shrieking before every lunge, until you're far enough
from its house that it gives up and walks back to its corner.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE LODGER** · *in the dark of a village house*
A long, starved thing folded into the corner of a back room, breathing.
> **RULE: when it shrieks, put a door between you.**

**Sense:** sound (whoever's in its house). **Zone:** outside (the villages). **Want:** Kill. **Cost:** none (the house's
own, not the director's).

## 3. What it looks like (art brief)

Human-ish but wrong: very long limbs, ash-grey skin stretched over bone, a narrow ribcage, long-fingered hands, and a head
that's mostly a jaw that unhinges wide to shriek. About 2.1 m when it rises; it hides folded into corners. Clips: `hidden`
(folded, breathing), `shriek` (rearing, jaw wide), `lunge`, `chase` (a fast, low lope), `break` (bashing a door), `return`
(a slow walk), `hit`, `death`.

## 4. How it sounds (a request to the audio chat)

- **Breathing** (the tell): slow, wet, audible from the next room.
- **The shriek** (the telegraph): shrill, rising, a second and a half; the loudest thing in a village.
- **The lunge:** a thump of bare feet. **Breaking a door:** splintering blows.

## 5. Behaviour tree (App. A.4 format)

### THE LODGER · sound
```
HIDDEN    folded in its house's darkest corner (the floor furthest from its doors), breathing
WAKE      someone in its house 2.5 s, or within 4.5 m of it with no wall between → it wakes on the nearest of them
SHRIEK    the telegraph: 1.5 s (the spine's least), facing them
LUNGE     straight at where they were, 9 m/s for up to 4.5 m: within 0.9 m of them, no wall between → it has them
          (a grab of 0.25 s: the one-hit kill, the director's call)
CHASE     missed: after them at 5 m/s, out of the door and beyond; close again with nothing between → SHRIEK, LUNGE
          ├ a shut door in its way → it breaks it open (2 s)
          └ they're more than 25 m from its house → it gives up
RETURN    it walks back to its corner (1.6 m/s) and folds up again; it won't wake for 3 s
HIT       only blows from behind as it walks back hurt it (4 kill it); a blow in its corner wakes it on whoever struck
```

## 6. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The thing in the back room that screams and then kills you." |
| 2 | Answered better by two | ✔ One shuts the door behind the one running; or two strike it as it walks back. |
| 3 | Consequence now, death later | ✗ Death now, by the director's call. The shriek is the warning. |
| 4 | A verb, not just a "don't" | ✔ Listen at the door, run, shut the door. |
| 5 | Someone gets blamed | ✔ "Who went in without listening?" |
| 6 | Fun to scream | ✔ (It screams first.) |

## 7. Spawn rules

In a village house, as the train comes up to the stop: one of the house creatures (`dwellings`: a share of each stop's open
houses by tier, its kind by weight). Never the Gaunt's roost. Every tier.

## 8. First-pass numbers (`enemies.json` `lodger`)

| Field | Value | Why |
|---|---|---|
| `noticeSeconds` / `shriekWithin` | 2.5 s / 4.5 m | a moment in its house to hear it and back out |
| `shriekSeconds` | 1.5 s | the spine's least telegraph |
| `lungeSpeed` / `lungeReach` / `killWithin` | 9 m/s / 4.5 m / 0.9 m | step sideways or behind a door |
| `killWindow` | 0.25 s | the one-hit kill |
| `chase` | 5.0 m/s | under a run: you can get away |
| `pursuitRadius` | 25 m | "slightly wider than normal" |
| `breakSeconds` | 2 s | a shut door buys you that |
| `walk` / `restSeconds` / `health` | 1.6 m/s / 3 s / 4 | |

## 9. What the harness verifies

`HouseCreatureTests`: asleep till someone's in its house, then a shriek before any lunge (the fairness check); stood still
where it lunges, a one-hit kill; a shut door between stops the lunge and it has to break it; past its pursuit radius it
gives up and goes back to its corner.

## 10. Decisions (G1's calls, for the director)

1. **A shut door stops a lunge** and it breaks the door in 2 s: the rule ("put a door between you") and the director's
   one-hit kill together.
2. **Killable only from behind as it walks back**, 4 blows: a crew can end it, but never by standing their ground.
