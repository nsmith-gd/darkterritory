# THE LURCHER — it runs down whoever runs off alone

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units (player health 100, a
crowbar blow = 1, a gun round = 4 blows; a walk 1.6 m/s, a run 4.5 m/s, spec B.2).*

> **Two of you, it circles. One of you, it comes.**

## 1. What it is

A long, starved sighthound the height of a man's chest, all ribs and tendon, its coat gone to grey felt. It lies flat in
the grass at a village's or yard's edge and gets up only for someone on their own. It is faster than a walk and as fast as
a run, so the answer is never "outpace it": the answer is to be with someone.

The director, 23 Oct (line 25): the Ribbits are "outrun at a walk". The Ribbits' pace is #702's; the Lurcher is the
creature whose *whole point* is that you can't get away on your own.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE LURCHER** · *at the edges of villages and yards, afoot*
A grey sighthound, all ribs, flat in the long grass.
> **RULE: run to a friend, not away.**

**Sense:** sight (movement, alone). **Zone:** outside. **Want:** Kill. **Cost:** 2.

## 3. Silhouette and look

Low and long when it lies, tall and narrow when it stands: a greyhound's deep chest and wasp waist stretched a third too
long, legs like rake handles, the head a narrow wedge with the lips drawn back from small even teeth. Its eyes catch the
lamp green at 40 m, two points low in the grass (the visual tell). Clips: `lie`, `rise`, `stalk`, `run`, `circle`,
`lunge`, `drag`, `hit`, `flee`, `death`.

## 4. The tell (spec A.3/A.4)

- **The rise** (tier 1): a single high yip, then its panting at a gallop's cadence, a rhythmic huff 1.5–3 kHz in
  threes. Told from the Cinder Hounds' howl (500 Hz–3 kHz, a long tone) by rhythm, and the hounds are never off the
  train's rear where the Lurcher is never aboard.
- **Seen:** two green eyes low in the grass, then a grey shape running in the lamp.
- Nameable in three words: "the dog's up".

## 5. The rule (App. A.4 format)

```
LIE       at a village or yard edge, 25–40 m from the nearest building, flat
PICK      a crewmate afoot, 12 m or more from every other living crewmate, for 3 s → it rises on them
          └ TELEGRAPH: the yip and the panting (2.5 s before it runs)
RUN       at 4.6 m/s (a run is 4.5): it closes on a walker, matches a runner
          ├ the target comes within 4 m of another crewmate → it breaks off and circles at 10 m (it never takes a pair)
          ├ a blow → it backs off 8 m and circles; 3 blows kill it (a gun round is 4)
          └ the target reaches the train and climbs aboard → it gives up and lies down again
LUNGE     within 2 m → GRAB: down, dragged by the leg toward the grass at 1 m/s (12 s)
          └ a friend's blow, or Shove, frees them → it flees 30 m and lies down
PUNISH    dragged into the treeline: dead
```

## 6. Aboard or afoot; what it counters

**Afoot.** It counters **sprinting off alone** (the crewmate who loots the far house while the rest work the yard) and
**dawdling at the edge** of a stop. It makes "stay in pairs" a rule you feel, which the Ribbits ("never be outnumbered")
only do once you're caught.

## 7. Compositions

- **Lurcher + Ribbits:** the Ribbits' answer is to be in a group at least their size; the Lurcher's is to be in one at
  all. Together they herd a crew into one knot, which is what the Hollow House and the Close punish.
- **Lurcher + the Hanger / the Lodger:** the house you ran into to get away from it has its own rule; you went in alone.
- **Lurcher + the Pickers or Mouse Moose:** they pull crewmates to guard one crate each, apart; it takes the one guarding
  the far one.

## 8. Early versus late

- **Early (Local):** one, only at villages, gives up at 30 m from its edge. A lesson: two crew is safe.
- **Late (Dead Lines, Deep Territory):** two at a stop, from opposite edges; the second picks whoever the first's victim's
  friend left behind to help.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The dog that runs down whoever's alone." |
| 2 | Answered better by two | ✔ It never takes a pair; a friend's blow frees the grabbed. |
| 3 | Consequence now, death later | ✔ The yip and the chase now; the drag is 12 s, room for a friend. |
| 4 | A verb, not just a "don't" | ✔ Run *to* someone; club it off. |
| 5 | Someone gets blamed | ✔ Whoever wandered off. |
| 6 | Fun to scream | ✔ "IT'S ON ME — WHERE ARE YOU?" |

## 10. Build cost

**Small to medium.** The sim reuses the Ribbits' chase and the Whistler's drag (`EnemyContext.Carry`); its pick is a
distance test over the crew. New: one model (a quadruped rig like the Cinder Hounds', re-proportioned) and its clips, one
sound. About 1 agent-day of sim and bots (the bots already pair up for the Ribbits), 1–2 of art.

## 11. Open calls for the director

1. Faster than a run (it catches anyone alone), or as fast (a runner who knows where the friend is gets there)? Moth's
   call: 4.6 m/s, just over a run.
2. Every tier, or from Frontier on?
