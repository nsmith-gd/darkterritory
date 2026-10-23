# THE BELLMAN — he rings, and everything about comes for who he points at

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **He doesn't hurt you. He tells the others where you are.**

## 0. Reworked on the director's verdict (Sloe, queue #709, note 1004)

> **The director, 23 Oct (line 36):** "I like what the Bellman is going for, but I don't like its delivery. It doesn't feel
> cool. I think if we're going to have, like, monsters go for one player, it should be something like having something
> attached to that player that you can get off of it, or having something that only that player can see. That sort of,
> like, acts as a weight."

**This section replaces §1, §4, §5 and §6 below** (kept for the record). The aim stays (one player marked, everything about
weighted toward them); the porter pointing from a roof goes. Two deliveries, for the director's choice
(`nathaniel2-sloe-bellman`):

**A. The bell on your back (Sloe's recommendation).** The Bellman is small now: a shrivelled thing the size of a cat, a
brass bell sewn into its chest, that drops from a stop's eaves or a car's roof onto one crewmate's back and clings there.
- **Tell:** the bell, rung by the crewmate's own steps, 1.8–3.5 kHz, heard by everyone, louder when they run; seen, the
  thing on their shoulders (third person, other crewmates see it plainly; first person, its hands at the edge of the view
  and the bell's knock with each step).
- **The weight:** while it clings, every creature about that can reach the marked player weighs them ×3 in its choice (by
  its own rule: a Ribbit pack still won't take a group its size), and the marked player moves at 85%.
- **The counter:** *you can't reach your own back.* A crewmate within 2 m holds Use for 3 s to tear it off (it's thrown 4
  m and scuttles off; a blow then kills it). Alone, you can shake it only by going into water or into a fire's heat (a lit
  stove, a burning car) for 2 s.
- **Rule:** "get it off each other's backs."
- **What it foils:** **min-maxing** (it drops on whoever is carrying the night: the most banked or carried this night, or
  the one who has held one post longest) and the lone hero. It needs a friend, and the friend has to come close.

**B. The one only you can see.** The Bellman is a tall porter only the marked crewmate can see, keeping pace 10 m off and
ringing; every creature about hears the bell, and nobody else does. The marked player has to talk the crew onto it ("left,
by the shed, he's on the roof") for a friend to hit it blind (it shows to all for 2 s when hit). Stronger horror, weaker
counter: the crew acts on one player's word. **It suits quiet on the mic better than min-maxing**, so it's offered there as
its own creature (the Witness, `witness.md`), and the Bellman takes A.

**Cost (A):** **small–medium**, less than Moth's: the weight is one multiplier in the targeting Rook's #703 builds
(`Targeting.cs`'s `pull`), not a rewrite of every creature's; a small rig (the Followers' kit), a cling (the Grab's
attach), a friend's Use. About 1.5 agent-days of sim and bots, 1 of art, the bell (built for Moth's version).

**Social test (A):** 1 ✔ "the bell thing on your back"; 2 ✔ only a friend can pull it off; 3 ✔ the bell now, the weight
only matters when something comes; 4 ✔ tear it off; 5 ✔ whoever let it drop on them, and whoever wouldn't come and help;
6 ✔ "GET IT OFF ME, GET IT OFF!"

## 1. What it is

A corrupted station porter, tall and very thin in a long platform coat, a handbell in one hand. He stands somewhere high at
a stop (a platform's end, a shed roof, a water tower's ladder) and watches the crew. Then he rings, and points, and every
creature about turns toward the one he points at. He never touches anyone. He's the composition's conductor: the director
asked that "the novelty ... is always about the composition of the different monsters" (line 29), and he's what makes the
composition act together and at someone in particular (line 35: pressure that targets players specifically).

## 2. Roster entry (GDD §21, CORRUPTED HUMANS)

**THE BELLMAN** · *high up at a stop*
A thin porter with a handbell, pointing.
> **RULE: silence the bell first.**

**Sense:** sight. **Zone:** outside (stops). **Want:** none of his own. **Cost:** 2, only with two or more other
creatures about.

## 3. Silhouette and look

A head taller than a man, so thin his coat hangs off the shoulders like a sack off a peg; a porter's cap; one arm always
out straight, pointing, the finger too long. The bell is brass, old and bright, the only clean thing on him. Clips:
`watch`, `ring` (the bell over his head, swung), `point` (turning with the target), `climb` (away), `hit`, `fall` (death,
the bell rolling).

## 4. The tell (spec A.3/A.4)

- **The bell** (tier 1): a handbell's peal, 1.8–3.5 kHz, loud on purpose, heard across the stop. One peal before each mark.
  Told from the cab's overspeed bell (1.2 kHz strikes, partials 2.6 and 4.1 kHz, twice every 1.5 s, in the cab) by being a
  rung peal, out of doors, and by where it comes from.
- **The mark:** whoever he points at hears a second, softer bell on them, and their crewmates see him pointing.

## 5. The rule (App. A.4 format)

```
COME      a stop with 2 or more other creatures about (a Ribbit pack, Stragglers, a Lurcher, the Pollards) → he climbs to a
          high place 20–35 m from the train
MARK      every 25 s he picks the crewmate furthest from the others (or carrying the most), rings and points
          └ TELEGRAPH: the peal; the mark's own bell (3 s before anything turns)
TURN      for 15 s every creature about that can reach the mark goes for the mark, by its own rule (it doesn't change
          their rules: a Ribbit pack still ignores a group its size)
SILENCE   2 blows (or a gun round) and he drops the bell → the creatures go back to their own wants; 4 blows kill him
          └ he's high up: someone has to climb, or shoot, while the rest are being hunted
```

He never grabs, so he needs no rescue window: the creatures he sends grab by their own rules.

## 6. Aboard or afoot; what it counters

**Afoot, at a stop.** It counters **one strong crewmate carrying the night** (the loot-runner, the lone hero: he points at
them) and a crew that deals with creatures one at a time, because he makes several come at once.

## 7. Compositions

He is a composition. **Bellman + Stragglers:** the gang walks for one crewmate, not the train. **+ Lurcher:** it doesn't
need them to be alone if he's pointing. **+ the Moose:** the charge turns on the marked. **+ Pickers:** they take the
crate the marked is guarding. Each pair is worse than either because what was a crowd becomes a hunt.

## 8. Early versus late

- **Early:** none. He comes from Dead Lines on, when there are compositions to conduct.
- **Late (Deep Territory):** every 15 s; two marks at once on a crew of 6 or more.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The porter who rings and points at you." |
| 2 | Answered better by two | ✔ One climbs to silence him while the rest cover the marked. |
| 3 | Consequence now, death later | ✔ The mark now; whatever's sent comes later, with its own window. |
| 4 | A verb, not just a "don't" | ✔ Silence the bell; protect the marked. |
| 5 | Someone gets blamed | ~ The marked is chosen by him, not by a choice of theirs; whoever didn't go for the bell is. |
| 6 | Fun to scream | ✔ "HE'S POINTING AT YOU! RUN TO US!" |

Five of six, with 1 and 3.

## 10. Build cost

**Medium.** One new behaviour hook: the director's creatures read a "marked" crewmate as their target where their own rule
allows (each kind's target choice already exists; this weights it). The figure from the townsfolk kit, re-proportioned, and
a bell prop; one sound. About 2 agent-days of sim (the hook through every creature that picks a target, and its tests),
1 of art. The riskiest of the set: it touches every creature's targeting.

## 11. Open calls for the director

1. Is a creature that only directs others welcome, or should he have a want of his own?
2. Furthest from the others, or carrying the most? Moth's call: furthest, which also counters running off alone.
