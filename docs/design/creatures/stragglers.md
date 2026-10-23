# THE STRAGGLERS — the gang that walks out of the woods for your train

*Creature proposal, Moth (queue #706, ARCHITECTURE §8 note 1000), 23 Oct 2026. **Status: a proposal for the director;
nothing is built until the director approves it.** Numbers are a first pass in the roster's units.*

> **Every minute you stand, more of them come.**

## 1. What it is

A section gang that never came home: rail navvies gone grey and slow, still in their rotted work coats, each with a spike
maul or a shovel. When the train stands at a stop, they walk out of the woods and the town toward it, a few at first and
then more, and try to climb its steps. One is nothing: a shove puts it on its back. Twenty is a siege.

The director, 23 Oct (line 27): "anytime we stop, monsters should be coming out of the woods, coming out of town, coming
out of a yard ... trying to put pressure on us ... in the early game ... moderate ... the later game, it really needs to be
like pushing against us."

## 2. Roster entry (GDD §21, CORRUPTED HUMANS)

**THE STRAGGLERS** · *out of the woods and the town, at every stop*
Grey workmen walking slowly toward the train with their tools.
> **RULE: hold the steps, or go.**

**Sense:** vibration (the train standing; its pump and blower). **Zone:** outside, at a stand. **Want:** Board. **Cost:**
1 a wave.

## 3. Silhouette and look

Human, stooped, slow, a little too tall; caps and coats; tools held low. Their faces are the drab townsfolk figure's
gone wrong (the lookbook's style targets, #426), eyes white. They come in a loose line, never running. Clips: `walk`,
`climb`, `swing`, `shoved` (falling back), `getup`, `death`.

## 4. The tell (spec A.3/A.4)

- **The approach** (tier 1): spike mauls tapping the rail as they walk, many and out of time, a metallic ping 2.8–4 kHz,
  growing as they come. Told from the doll's giggle (3–6 kHz, in a car) by its rhythm: ticks, not a laugh.
- **Seen:** lamplight on a line of figures at the treeline.

## 5. The rule (App. A.4 format)

```
COME      the train stands at a stop (a halt, a yard, a village) → after 25 s (Local 45 s) a wave of 3 walks out of the
          woods or the town, 40 m off; another wave every 30 s while it stands, each a little bigger (to 6)
WALK      0.9 m/s toward the train's nearest open door (a walker can leave them; it's the train they want)
          └ TELEGRAPH: the rail ticking
CLIMB     at a door's steps: 3 s to climb
          ├ Shove (the crew's verb), or a blow → it falls back off the steps and lies 4 s; 2 blows kill one
          └ a shut door → it moves to the next open one
ABOARD    one in a car swings at whoever's nearest (a blow of 15, 2 s apart), more than one GRAB a lone crewmate together
          and carry them to the door (10 s; a friend's blow frees)
GO        the train moves off over 4 m/s → those on the steps fall; those aboard fight on until killed
```

## 6. Aboard or afoot; what it counters

**At every stop, then aboard.** It counters **lingering at a stop** (the loot is good, the clock and the crowd say go),
**leaving the doors open**, and **everyone off the train at once**. It's the pressure the director asked for at every stop,
built to be numerous and individually weak, so it scales by count, not lethality.

## 7. Compositions

- **Stragglers + Hotbox / Knotter:** both are only answered at a stand; the gang comes for every second you stand.
- **Stragglers + Whistler:** the Whistler strikes only at a stand, from a gap, while everyone's at the doors.
- **Stragglers + Pickers / Mouse Moose:** the crates go down the drains while you hold the steps.
- **Stragglers + the Choir:** shoving them off is loud.

## 8. Early versus late

- **Early:** one wave of 2–3 after 45 s, no more. A reminder to shut the doors.
- **Late:** a wave every 30 s from 25 s, up to six at a time ("maybe six or seven at a time", line 29), from the town and
  the woods both.

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The dead section gang trying to get on." |
| 2 | Answered better by two | ✔ One at each door; and alone, two of them take you. |
| 3 | Consequence now, death later | ✔ They're on the steps now; the carry is 10 s. |
| 4 | A verb, not just a "don't" | ✔ Shove them off, shut doors, drive. |
| 5 | Someone gets blamed | ✔ "Who left the van door open?" |
| 6 | Fun to scream | ✔ "THEY'RE ON THE STEPS! GO, GO, GO!" |

## 10. Build cost

**Medium.** Reuses Climbers' gap-mounting for the steps, Shove (the crew's push, built) for the answer, the Passenger's carry, and the
townsfolk figure for the model (a coat and a tool from the props kit). The wave clock sits on the stop's existing
dread timer (`n1-C2-dread-at-the-stops`, note 944, if the director turns it on). About 2 agent-days of sim and bots, 1 of
art, one sound.

## 11. Open calls for the director

1. Do they board, or only hold the train (a crowd on the steps that has to be shoved off before it can leave)? Moth's
   call: they board, so the fight comes aboard.
2. At the yards too, or only halts and villages (the yards have the Pickers and the Mouse Moose)? Moth's call: everywhere,
   fewer at a yard.
