# THE BULL — a corrupted moose

*Creature proposal, G1 (enemy design), 7 Oct 2026. **Status: proposed, not yet the director's decision.** Nothing here
is in the GDD, the systems spec or the tuning yet. Once the director approves it, it goes into GDD §21 (Outside), App.
A.6, App. B.1 (costs, wants, conflict table), B.6 and level-design H.2, and the numbers go into `content/tuning/enemies.json`
`bull`. The numbers below are a first pass in the same units and scale as the roster's (player health 100, run 5.5 m/s,
a tool swing every 0.8 s, a gun round = 4 blows).*

> **The moose that thinks your engine is a rival.**

---

## 1. Why a moose

- **It's the Maritimes' animal.** The line runs through Nova Scotia gone dark (`docs/design/maritime-rules.md`): bog,
  granite barrens, black spruce, lakes. Moose live in exactly that country, and October, when the game is set, is the rut.
- **Real moose fight trains.** In deep snow they walk the track because it's the easy path, and they won't give way.
  Bulls stand their ground and charge locomotives. Railways in moose country kill hundreds a winter.
- **It's the GDD's own premise.** "*Prey became territorial and violent*" (§2). The Corruption "exaggerates whatever
  allows something to survive". A bull survives on mass, on its rack, and on the rut. All three are grown past sense.
- **It's a Split creature.** Split is the director's thinnest want, at 3 of 17 creatures and a 25% target, and B.1 names
  it "the best target for a post-launch addition". The Bull splits the crew. Someone has to lead it off alone while the
  others work.
- **It has a railway name.** A *railroad bull* was railway police, the men who ran tramps off railway property. This one
  runs you off its ground too.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE BULL** · *bogs and barrens beside the stops*
A moose in a rut that never ended, its rack grown wider than a doorway. Your whistle sounds like a rival to it.
> **RULE: get somewhere narrower than its antlers.**

Whoever calls it, it chases. That covers the whistle, a vent of steam, or a tool raked on a rail. It charges in straight
lines and can't turn. Dodge it, or bait it into the trees until its rack jams, then everyone clubs it. It never comes
aboard: nothing that wide fits through a door.

**Sense trigger:** sound (a rival's call). **Zone:** outside. **Want:** Split. **Cost:** 4.

## 3. What it looks like (art brief, GDD §26.5, §29)

*The strongest monsters are the ones where you can still tell what they used to be.* Anyone who has seen a moose knows
this one at once. Then they notice what's wrong with it.

- **The silhouette is the rack.** It has never shed. The rack has grown for years, mineralized grey-black like slag
  (comet contamination, not glow), and spans **3.2 m**: the widest silhouette in the roster. Its weight drags the head
  low, and the neck has swollen into a hump to carry it. Seen once, the shape says the rule: *that won't fit through
  anything.*
- **What's caught in the rack** is environmental story: fence wire, a telegraph insulator, a smashed lamp, a crew cap, and
  a **W whistle board** still on its post, torn out of the ballast. Bloody strips of velvet hang from every tine.
- **A ghost moose's hide.** In the Maritimes, real "ghost moose" are moose rubbed bald by tens of thousands of winter
  ticks. Corrupted, the ticks are engorged into **pale sacs** from grape to plum size, clustered on grey, hairless skin
  (§26.5: "*pale sacs, swollen flesh, stretched skin*"). A ridge of them runs along its spine. The pale hide catches the lamp
  across a bog, so it reads at distance in fog by its pallor (Part Eleven Q6).
- **The bell** (the dewlap) is swollen into a long sac that swings when it walks and fills before it bellows.
- **Too long in the leg**, knees knobbed, hooves split and black. It stands **2.4 m at the shoulder**, about 3.4 m to the
  top of the rack.
- **Unnerving in stillness** (§29). It stands motionless in its wallow with steam coming off it in the cold. Its head is
  cocked a little, like a moose with brainworm, a real Maritime disease that leaves moose fearless, circling and tame
  to people. Only the sacs move, pulsing.
- **Budget:** a large monster, 8,000–16,000 triangles (§27), in the Car Hugger's class. Material families: hide, sac,
  mineral rack, velvet.

**Clips** (for `tools/blender`; `dt art clearance` must fit the rack's capsules): `rut` (thrashing a sapling), `walk`,
`trot`, `squareUp`, `charge`, `overrun` (the skid and wheel round), `snag` (rack jammed, hauling), `ram`, `pin`, `bellow`,
`strut` (the winner's walk home), `driven` (stagger off), `death`.

## 4. How it sounds (a request to the audio chat)

Everything the Bull does is heard before it's seen, and none of it may sound like another creature's tell (A.1).

| Moment | Sound | Distinct from |
|---|---|---|
| Rutting (presence) | Wood cracking, antler rattle on a trunk, a short grunt every few seconds | The Gaunt's slow breathing, Ribbits' hops |
| Called (its answer) | A long, wet **bellow** out of the swollen bell, low enough to feel | The Hounds' rising howl; the Choir's chorus |
| Coming | Brush breaking in a line toward you | — |
| Squaring up | Two hoof stamps, a snort, the sacs creaking | Ribbits' throat-swell (silent) |
| Charge | Hooves on ballast, a wheeze | — |
| Snagged | Wood groaning, the rack grinding, a bellow of rage | — |

## 5. Behaviour tree (App. A.6 format)

### THE BULL · sound (a rival's call)
```
RUTTING   in its wallow at the stop's wet edge, thrashing a sapling; pale in the lamp across the bog
          └ TELEGRAPH (presence): cracking wood and a grunt from the dark, from the moment the train stops
CALLED    a rival's call in earshot: the whistle, the safety valve venting, a metal tool raked on a rail
          OR anyone walks onto its ground (within 20 m of the wallow)
          → its RIVAL is whoever made the call, or the engine itself if no hand made it
          └ TELEGRAPH: its answer, a bellow from the swollen bell, then the brush breaking as it comes
APPROACH  walks at its rival, head swinging; stops 18–35 m off
SQUARE UP ears flat, the sac ridge on its spine stands up, head drops the rack level, two stamps
          └ TELEGRAPH: 2.5 s; its heading locks at the end
CHARGE    a straight line at 11 m/s (twice a player's run); can't turn; overruns 8 m and wheels round (2.5 s)
          ├ sidestepped → missed → square up again
          ├ rival somewhere narrower than its rack (a doorway, a coupling slot, close-grown trees, aboard)
          │   → RAM what's in front of it: a car jolts (anyone on its roof is thrown flat, anyone on its
          │     ladder shaken off to the ground) and its shell is dented; a ram every 3 s, up to 4
          ├ ran its rack into anything narrower than itself (two trees, a ladder, a fence, a switch stand)
          │   → SNAG: rack jammed 4 s, and the only time a blow lands
          └ hit → KNOCKED FLAT: a heavy hit (60)
GRAB      charges a rival already hurt (≤ 40) → pins them under the rack and grinds them into the peat
          victim can still talk; 12 s
          └ interrupt: anyone rakes metal or hits it → they're the rival now, and it drops the victim
PUNISH    trampled
NEW RIVAL the last caller is always the rival: a second rake takes it off the first
BREAK OFF its rival out of reach 20 s → rams, bellows, walks home; still rutting, so the next call starts it again
          its rival more than 80 m from the wallow → it's won; struts home
          the train leaves → the rival engine is driven off; a victory bellow, and it's done for the stop
DRIVEN    one gun round → back to its wallow, quiet for 30 s
KILL      8 blows, landed only while SNAGGED (a crew of three does it in one snag; one player needs two)
          OR two gun rounds
```

**It never boards.** Its rack won't go through a door, so every door is shut to it, open or not. It keeps to the
director's boarding-first rule (App. F.1) by never needing an exception. It acts only on what's outside: people on the
ground, and the outside of a stopped car.

**Only where the train slows.** It lives at stops and is called at stops. "*Slowing opens the doors*" (App. F.1). The
hauling train is safe from it.

**The verb is the bullfight.** One player calls it, takes the charge, and sidesteps. A second player calls from the other
side and takes it over. They pass it back and forth while the rest of the crew loads, or lead it into the spruce until it
jams and the crew piles in. The rule teaches itself: the first death is a player caught in the open, and the corpse lies
two metres from a doorway or a stand of trees it couldn't have followed them into.

**No chip damage** (note 272). Two hits, then the pin, as with the Gaunt. The charge is the only thing that hurts, and
every charge comes after a 2.5 s square-up that says exactly which way it will go.

## 6. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The moose that thinks your engine is a rival." |
| 2 | Answered better by two | ✔ Two players pass it between them and neither is ever charged twice running. A third loads while they do. Alone, you hide and lose the time. |
| 3 | Consequence now, death later | ✔ The first charge knocks you flat, the second pins you, and the pin is 12 s long. |
| 4 | A verb, not just a "don't" | ✔ Call it, dodge it, pass it, jam it, club it. |
| 5 | Someone gets blamed | ✔ The last caller is its rival: whoever pulled the whistle at the stop, vented, or raked a rail. |
| 6 | Fun to scream | ✔ "BULL! BULL! GET IN THE TREES!" |

All six.

**Benchmarks** (App. F.1, "benchmark every creature against Lethal Company and R.E.P.O."):
- Lethal Company's **Thumper**: terrifying in a straight line, beaten by corners. The Bull adds a reason to *want* it in a
  corner, the snag.
- The **Eyeless Dog**: called by sound, and the whole crew learns who made it. The Bull is the same lesson with a name
  attached.
- R.E.P.O.'s carry-under-threat: the Bull is worst for whoever is **carrying** (2.8 m/s and slow to sidestep), so a carry
  needs an escort. That's the facility job made social.

## 7. Spawn rules (App. B.6 row)

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Bull** | Its wallow at a stop: wet ground at the stop's edge with trees near | Frontier+ · crew ≥2 · one per stop (two bulls never share ground) | ×2 bog, ×1.5 barrens and lakeshore stops, ×1 forest; ×0 where there's no wet ground. Up per player on the ground |

- **It spends when it's placed.** The director places it in its wallow when the train arrives, as a resident (like the
  slaughterhouse's Gaunt, `residents`). It's dormant until called, and the crew can see and hear it from the train. A
  crew that keeps quiet and keeps off its ground pays nothing but nerves.
- **The facility lull holds.** It's placed after the 20 s lull, and its presence telegraph begins with it.
- **Pressure cost: 4. Want: Split.** It brings Split to four creatures and toward its 25% share.

**Level design (H.2 addition):** *The Bull's wallow (B.6): a rut pit on the stop's wettest ground (bog, lakeshore, a
flooded ditch), 50–90 m from where the consist stops, never on the walk to the loading, with a stand of close-grown trees
(gaps under 3 m) between it and the train.* The trees are deliberate. The site always offers the counter.

## 8. Contradictions (App. B.1 conflict table)

| Pair | The bind |
|---|---|
| **Whistler + The Bull** | The whistle nobody pulled calls the bull, and the narrow coupling gaps you'd shelter in are where the Whistler waits |
| **Ribbits + The Bull** | Never be outnumbered vs. somebody has to lead the bull away alone |
| **Stoker + The Bull** | Vent to starve the Stoker vs. the vent's roar is a rival's call |
| **Choir + The Bull** | Raking a rail to pass the bull, and the gun that drives it off, both feed the meter |

The Whistler pair is the cruellest in the roster. The Whistler blows the whistle 4 s into a stop
(`whistler.whistleAfterStop`), the engine becomes the rival, and the Bull plants itself between the ground crew and the
train, ramming the cab. To get home, someone has to call it, which makes that player the rival, and then dodge it into
the coupling gaps, where the Whistler is waiting.

## 9. First-pass numbers (proposed `enemies.json` `bull`)

```jsonc
// THE BULL · sound · outside (proposal: docs/design/creatures/bull.md). Rutting in its wallow; a call within hearWhistle
// (the whistle), hearVent (the safety valve, a vent) or hearRake m (a metal tool raked on a rail), or anyone within
// groundRadius of the wallow, makes the caller its rival (the engine if no hand made it). It walks at approachSpeed to
// squareUpAt m, squares up squareUpSeconds, and charges at chargeSpeed, overrunning overrun m and wheeling wheelSeconds.
// Nothing narrower than rackSpan lets it through: it rams instead (every ramEvery s, maxRams), jolting the car. Run into
// something narrower, it's snagged snagSeconds, the only time a blow lands. A charge is a hit of chargeDamage; at or under
// grabBelowHealth it pins for pinSeconds. Out of reach giveUpSeconds, or its rival past homeRadius from the wallow, and it
// goes home. One gun round sends it home for drivenSeconds; health in blows.
"bull": {
  "hearWhistle": 250, "hearVent": 150, "hearRake": 60, "groundRadius": 20,
  "approachSpeed": 2.5, "squareUpAt": [18, 35], "squareUpSeconds": 2.5,
  "chargeSpeed": 11, "overrun": 8, "wheelSeconds": 2.5, "rackSpan": 3.2,
  "ramEvery": 3, "maxRams": 4, "ramShell": 0.02,
  "snagSeconds": 4, "health": 8,
  "chargeDamage": 60, "grabBelowHealth": 40, "pinSeconds": 12,
  "giveUpSeconds": 20, "homeRadius": 80, "drivenSeconds": 30,
  "minCrew": 2, "perGroundWeight": 0.5,
  "biomeWeights": { "marsh": 2, "plains": 1.5, "contaminatedMarsh": 1.5, "blackForest": 1, "forestEdge": 1, "mountain": 1 }
}
```

Body for the guns (`bodies`): `"bull": [[0.9, 1.6], [0.9, 2.1], [0.6, 2.6]]`, the barrel and the hump. The rack stops
nothing; a round in the rack is a miss.

**Why these numbers:**
- **11 m/s charge** is twice a player's run (5.5), so you can't outrun it in the open. That forces the sidestep or the
  trees.
- **2.5 s square-up** sits inside A.1's 1.5–4 s window, at the generous end, because a miss costs a hit.
- **8 blows:** a crowbar swings once every 0.8 s, so one player lands 5 in a 4 s snag and needs two snags. Three players
  land about 15 and finish it in one. Two gun rounds (4 blows each) also kill it, per note 290.
- **60 a charge, pin at 40:** the Gaunt's model. Two hits, then a 12 s pin, inside A.1's 8–20 s rescue window.
- **Whistle heard at 250 m:** a halt's whistle board stands 400 m out (`holdouts.json` `assignHalt`), so with the
  wallow 50–90 m from the consist, a whistle at the board is 310 m or more away and stays safe. A whistle in the yard
  does not.

## 10. What the harness verifies

- **Telegraph before commit:** every CHARGE follows a full SQUARE UP, and its heading locks at the square-up's end
  (`dt audit`).
- **Rescue:** every pin can be broken by the crew present at crew 2 (`dt audit grabs --crews 2 --only bull`). One rake
  or one blow frees the victim.
- **Narrow is safe:** a scripted rival standing in a doorway, a coupling slot, or between trees 3 m apart is never hit
  (a test).
- **Never derails, never boards:** rams jolt and dent; no ram moves a car on its brakes, and the Bull is never inside.
- **Sweeps:** `dt balance --pairs --only bull,whistler` and `bull,ribbits` at crews 2 and 4. The Whistler pair must be
  hard, not impossible (B.10).
- **Seen and heard:** a `dt screenshot --view bull` (squaring up in the lamp) and `bullcharge`; `dt art clip bull charge`;
  `dt audio render` for the bellow against the Hounds' howl.

## 11. What building it needs

- **The solid world (queue #14, B1)** is a dependency. "Narrower than its rack" means trees, fences and buildings need
  collision the Bull's rack can be tested against: a 3.2 m-wide sweep against the world.
- **Raking** is a new player action: Use with a metal tool held against a rail or the train's iron. It's an intent, loud
  (it feeds the meter), and shown to the crew.
- **The wallow** is a stop site (level-design H.2), placed by the stop generator like the Ribbits' warrens.
- **A1's #25** owns whether creatures die. The snag kill above is offered to it, not taken.

## 12. Open questions for the director

1. **The name.** *The Bull*: short to scream, and the railway police pun. Or *the Moose*, which is what crews will
   shout anyway?
2. **The rack as loot.** A killed bull's rack is a two-person carry, too wide for a door, so it rides on a roof and sells
   in town. Worth it, or does it turn the Bull into a farm?
3. **On the line.** A second, forward appearance: a bull standing on the rail in deep snow that won't give way. Whistle
   at it and it charges the engine. This overlaps the Track Doll's forward slot, so it's left out for now.
4. **Rams and roofs.** Does a ram only throw a roof player flat, as proposed, or can it put them over the edge, against
   T128's "hard to fall off"?
5. **Tier.** Frontier+ as proposed, or Local too, as a gentle first lesson?
