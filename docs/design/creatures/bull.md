# THE BULL — a corrupted moose

*Creature proposal, G1 (enemy design). **Status: proposed, revised with the director, 7 Oct 2026.** Nothing here is
in the GDD, the systems spec or the tuning yet. On approval it goes into GDD §21 (Outside), App. A.6, App. B.1 (costs,
wants, conflict table), B.6 and level-design H.2, and the numbers go into `content/tuning/enemies.json` `bull`. The
numbers below are a first pass in the same units and scale as the roster's (player health 100, run 5.5 m/s, a gun round
= 4 blows).*

> **A hyper-aggressive, territorial moose, too big to get on the train.**

**The director's revision (7 Oct 2026):**
- **The bullfight is the heart of it.** Keep the charge and the narrow places.
- **It's docile until bothered.** It stays where it is unless you get too close or hit it, so it's something you're
  casually aware of around you.
- **There are three ways to be rid of it:** get far enough away, play hide-and-seek round the buildings until it gives
  up, or drive the train away.
- **It's too big to board.** If it knows you're in a car, it goes at the car with its antlers for a while.

The first draft's calls (the whistle, venting, raking a rail) and its kill are gone.

---

## 1. Why a moose

- **It's the Maritimes' animal.** The line runs through Nova Scotia gone dark (`docs/design/maritime-rules.md`): bog,
  granite barrens, black spruce. October, when the game is set, is the rut.
- **Real bull moose are territorial and fight trains.** They stand their ground on the track and charge locomotives.
- **It's the GDD's premise.** "*Prey became territorial and violent*" (§2). The Corruption "exaggerates whatever allows
  something to survive": here the mass, the rack, and the rut's temper, all grown past sense.
- **It's a Split creature.** Split is the director's thinnest want, at 3 of 17 creatures against a 25% target (B.1). The
  Bull splits the crew. Whoever bothered it is chased off alone while the rest keep working.
- **It has a railway name.** A *railroad bull* was railway police, the men who ran tramps off railway property. This one
  runs you off its ground too.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE BULL** · *grazing at the edge of the stops*
A moose in a rut that never ended, its rack grown wider than a doorway. Leave it be and it leaves you be.
> **RULE: give it room, or go narrow.**

Crowd it or hit it and it charges, in straight lines it can't turn out of. Dodge it, lead it off, or lose it among the
buildings until it gives up. Hit it to take its attention off a friend. It never comes aboard. Nothing that wide fits
through a door, so it takes its rage out on the car you're in, and only the train pulling away ends that for good.

**Sense trigger:** sight (trespass). **Zone:** outside. **Want:** Split. **Cost:** 3.

## 3. What it looks like (art brief, GDD §26.5, §29)

*The strongest monsters are the ones where you can still tell what they used to be.* Anyone who has seen a moose knows
this one at once. Then they notice what's wrong with it.

- **The silhouette is the rack.** It has never been shed. It has grown for years, mineralized grey-black like slag
  (comet contamination, not glow), and spans **3.2 m**: the widest silhouette in the roster. Its weight drags the head
  low, and the neck has swollen into a hump to carry it. Seen once, the shape says the rule: *that won't fit through
  anything.*
- **What's caught in the rack** is environmental story: fence wire, a telegraph insulator, a smashed lamp, a crew cap, a
  splintered door frame. Bloody strips of velvet hang from every tine.
- **A ghost moose's hide.** In the Maritimes, real "ghost moose" are moose rubbed bald by winter ticks. Corrupted, the
  ticks are engorged into **pale sacs** from grape to plum size, clustered on grey, hairless skin (§26.5: "*pale sacs,
  swollen flesh, stretched skin*"). A ridge of them runs along its spine. The pale hide shows up across a bog in the
  lamp, so it reads at distance in fog by its pallor (Part Eleven Q6).
- **The bell** (the dewlap) is swollen into a long sac that swings when it walks.
- **Too long in the leg**, knees knobbed, hooves split and black. It stands **2.4 m at the shoulder**, about 3.4 m to the
  top of the rack.
- **Unnerving in stillness** (§29). Calm, it grazes with its head down and steam coming off it in the cold, the sacs
  pulsing. It is never fully still: it shifts, chews, and turns its head to keep whoever's nearest in view.
- **Budget:** a large monster, 8,000–16,000 triangles (§27), in the Car Hugger's class. Material families: hide, sac,
  mineral rack, velvet.

**Clips** (for `tools/blender`; `dt art clearance` must fit the rack's capsules): `graze`, `watch` (head up, turning to
follow), `warn` (ears back, ridge up, head low), `walk`, `trot`, `squareUp`, `charge`, `overrun` (the skid and wheel
round), `snag` (rack jammed, hauling), `search` (head sweeping, snorting), `ram`, `pin`, `strut` (the walk home).

## 4. How it sounds (a request to the audio chat)

Everything the Bull does is heard before it's seen, and none of it may sound like another creature's tell (A.1).

| Moment | Sound | Distinct from |
|---|---|---|
| Grazing (presence) | Tearing browse, slow chewing, a sac's wet creak, the occasional low grunt | The Gaunt's slow breathing |
| Warning | A cough-like grunt, teeth clacking, a hoof dragged through the ground | Ribbits' throat-swell (silent) |
| Squaring up | Two hoof stamps and a snort | — |
| Charge | Hooves on ballast, a wheeze, brush breaking | The Hounds' approach (howling) |
| Snagged | Wood groaning, the rack grinding, a bellow of rage | — |
| Searching | Heavy snorting breath, the rack knocking on walls | — |
| Ramming a car | A deep iron boom through the whole car, the rack scraping the plates | A Climber's scrabble |

## 5. Behaviour tree (App. A.6 format)

### THE BULL · sight (trespass)
```
GRAZING   at its ground beside the stop; docile; ignores anyone giving it room
          └ TELEGRAPH (presence): seen and heard from the train, pale in the lamp
WATCHING  anyone within 30 m → head up, turns to keep them in view
WARNING   anyone within 20 m → ears back, the sac ridge on its spine stands up, a grunt, a hoof dragged
          └ TELEGRAPH: back off now and it settles back to GRAZING
ENRAGED   anyone stays within 12 m for 1.5 s, OR anyone hits it (a blow, a thrown thing, a gun round)
          → its TARGET is whoever did it; whoever hits it last is always the target
SQUARE UP ears flat, ridge up, head drops the rack level, two stamps
          └ TELEGRAPH: 2.5 s; its heading locks at the end
CHARGE    a straight line at 11 m/s (twice a player's run); can't turn; overruns 8 m and wheels round (2.5 s)
          ├ sidestepped → missed → square up again
          ├ target somewhere narrower than its rack (a doorway, a coupling slot, between close trees or
          │   buildings) → it can't follow: it rakes the gap with its antlers and paces outside
          ├ ran its rack into anything narrower than itself → SNAG: jammed 4 s, the moment to get away
          └ hit → KNOCKED FLAT: a heavy hit (60)
GRAB      charges a target already hurt (≤ 40) → pins them under the rack and grinds them into the peat
          victim can still talk; 12 s
          └ interrupt: anyone else hits it → they're the target now, and it drops the victim
PUNISH    trampled
SEARCH    target out of its sight → goes to where it last saw them, sweeping its head and snorting,
          checking round corners; sees them again → SQUARE UP
          └ not found in 25 s → GIVES UP
RAM       target seen going aboard, or lost within 5 m of a car → goes at that car with its antlers:
          a ram every 3 s for 15 s (anyone on its roof is thrown flat, anyone on its ladder shaken off
          to the ground; the shell dented), then GIVES UP
GIVES UP  also: its target more than 80 m from its ground
          → walks home and grazes again; it can be set off again the same way
TRAIN     the train pulls away → done for the stop
```

**Nothing kills it.** Blows and gun rounds only make you its target. It's a force of nature you manage, not a fight you
win. **Hitting it is a tool, not an attack:** the way to take it off a friend who's pinned or about to be charged, at the
price of being the one it chases.

**It never boards.** Its rack won't go through a door, so every door is shut to it, open or not. It keeps to the
director's boarding-first rule (App. F.1) without an exception. It acts only on what's outside: people on the ground, and
the outside of a car.

**Only where the train stops.** It lives at stops and never bothers a moving train. "*Slowing opens the doors*" (App.
F.1).

**The bullfight.** Someone strays too close and it comes for them. They dodge the charge. A friend hits it from behind,
takes it over and leads it off. They pass it between them, use the alleys between sheds and cars, and break its line of
sight until it gives up, while the rest of the crew keeps loading. The first death teaches the rule: the body lies two
metres from a doorway it couldn't have followed them through.

**No chip damage** (note 272). Two hits, then the pin, as with the Gaunt. The charge is the only thing that hurts. Every
charge comes after a warning you could have backed off from and a 2.5 s square-up that says which way it will go.

## 6. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The moose that's too big to get on the train." |
| 2 | Answered better by two | ✔ A friend's hit takes it off you, so a pair passes it back and forth and neither is charged twice running. Alone, you can only run and hide. |
| 3 | Consequence now, death later | ✔ The first charge knocks you flat, the second pins you, and the pin is 12 s long. |
| 4 | A verb, not just a "don't" | ✔ Dodge it, pass it, lead it off, hide from it. |
| 5 | Someone gets blamed | ✔ Whoever walked up to it or hit it. "Why did you HIT it?" |
| 6 | Fun to scream | ✔ "BULL! BULL! GET BETWEEN THE SHEDS!" |

All six.

**Benchmarks** (App. F.1, "benchmark every creature against Lethal Company and R.E.P.O."):
- Lethal Company's **Thumper**: lethal in a straight line, beaten by corners. Hide-and-seek round the buildings is the
  same lesson.
- The **Forest Keeper**: a huge thing that's survivable if you keep your distance and use the cover. Being in its sight
  is the mistake.
- R.E.P.O.'s carry-under-threat: the Bull is worst for whoever is **carrying** (2.8 m/s and slow to sidestep). A carry
  past its ground needs someone ready to take its attention.

## 7. Spawn rules (App. B.6 row)

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Bull** | Grazing at its ground beside a stop, where the crew will pass | Frontier+ · crew ≥2 · one per stop (two bulls never share ground) | ×2 bog, ×1.5 barrens and lakeshore stops, ×1 forest. Up per player on the ground |

- **It's a resident.** The director places it at its ground when the train arrives, like the slaughterhouse's Gaunt
  (`residents`), after the 20 s facility lull. A crew that gives it room pays nothing but attention.
- **Pressure cost: 3.** It's often harmless and costs a crew only when they bother it. **Want: Split.**

**Level design (H.2 addition):** *The Bull's ground (B.6): open grazing on the stop's wettest ground (bog, lakeshore, a
flooded ditch), 30–60 m from where the consist stops, within 25–40 m of a walk the crew has to make (to the loading,
the village, a switch), but never on it.* Close enough that you're always aware of it, but never in the way. There's
always cover between it and the train: a shed row, a rake of standing cars, or trees with gaps under 3 m.

## 8. Contradictions (App. B.1 conflict table)

| Pair | The bind |
|---|---|
| **Ribbits + The Bull** | Never be outnumbered vs. whoever the bull chases ends up alone |
| **Whistler + The Bull** | The coupling gaps are the bull's blind spot and the Whistler's hiding place |
| **Gaunt + The Bull** | Whoever the Gaunt follows home walks at its pace past the bull's ground, and the Gaunt won't fit where they hide |
| **Bull + facility loading** | The loading needs everyone vs. someone has to keep the bull busy |

The Ribbits pair is the cruellest. The player the bull chases off is exactly the lone player a Ribbit pack is waiting
for.

## 9. First-pass numbers (proposed `enemies.json` `bull`)

```jsonc
// THE BULL · sight (trespass) · outside (proposal: docs/design/creatures/bull.md). Grazing at its ground; it watches
// anyone within watchAt m and warns anyone within warnAt. Anyone within chargeAt for crowdSeconds, or anyone who hits it,
// is its target (the last to hit it, always). It squares up squareUpSeconds at squareUpAt m and charges at chargeSpeed,
// overrunning overrun m and wheeling wheelSeconds. Nothing narrower than rackSpan lets it through; run into something
// narrower and it's snagged snagSeconds. A charge is a hit of chargeDamage; at or under grabBelowHealth it pins for
// pinSeconds (a hit from anyone else breaks it). Out of sight (sightRange) it searches searchSeconds, then gives up, as it
// does past leashRadius from its ground. Lost at a car (lostAtCar m) it rams it every ramEvery s for ramSeconds. It can't
// be killed: blows and rounds only make the hitter its target.
"bull": {
  "watchAt": 30, "warnAt": 20, "chargeAt": 12, "crowdSeconds": 1.5,
  "squareUpAt": [12, 30], "squareUpSeconds": 2.5,
  "chargeSpeed": 11, "overrun": 8, "wheelSeconds": 2.5, "rackSpan": 3.2, "snagSeconds": 4,
  "chargeDamage": 60, "grabBelowHealth": 40, "pinSeconds": 12,
  "sightRange": 60, "searchSpeed": 2.5, "searchSeconds": 25, "leashRadius": 80,
  "lostAtCar": 5, "ramEvery": 3, "ramSeconds": 15, "ramShell": 0.02,
  "minCrew": 2, "perGroundWeight": 0.5,
  "biomeWeights": { "marsh": 2, "plains": 1.5, "contaminatedMarsh": 1.5, "blackForest": 1, "forestEdge": 1, "mountain": 1 }
}
```

Body for the guns (`bodies`): `"bull": [[0.9, 1.6], [0.9, 2.1], [0.6, 2.6]]`, the barrel and the hump. A round in the
body is a hit, so it makes the gunner the target, and the gunner is aboard: it rams the gun's car. The rack stops nothing.

**Why these numbers:**
- **Watch at 30 m, warn at 20, charge at 12:** three steps, each one visible, so you always get a "back off" before
  the "too late". The 1.5 s at 12 m means brushing past it doesn't count, but stopping to look does.
- **11 m/s charge** is twice a player's run (5.5), so you can't outrun it in the open. That forces the sidestep or the
  narrow places.
- **2.5 s square-up** sits inside A.1's 1.5–4 s window, at the generous end, because a miss costs a hit.
- **60 a charge, pin at 40:** the Gaunt's model. Two hits, then a 12 s pin, inside A.1's 8–20 s rescue window.
- **25 s search, 80 m leash:** long enough that a single corner won't shake it, short enough that hide-and-seek wins
  inside a minute. It always gives up eventually.
- **15 s of ramming:** "for a little bit". Long enough to hurt (anyone on the roof thrown flat, the shell dented) but
  short enough not to besiege the train for the whole stop.

## 10. What the harness verifies

- **Warning before charge:** no CHARGE without a WARNING and a full SQUARE UP before it, unless the target hit it; its
  heading locks at the square-up's end (`dt audit`).
- **Docile when left be:** a crew that keeps beyond `warnAt` through a whole stop is never charged (a test).
- **Rescue:** every pin can be broken by the crew present at crew 2 (`dt audit grabs --crews 2 --only bull`).
- **Narrow is safe:** a target standing in a doorway, a coupling slot, or between trees 3 m apart is never hit (a test).
- **It always gives up:** out of sight, beyond the leash, or after its rams, it goes home every time; the train leaving
  ends it.
- **Never boards, never derails:** rams jolt and dent; no ram moves a car, and the Bull is never inside.
- **Sweeps:** `dt balance --pairs --only bull,ribbits` and `bull,whistler` at crews 2 and 4 (B.10: hard, not
  impossible).
- **Seen and heard:** `dt screenshot --view bull` (warning in the lamp) and `bullcharge`; `dt art clip bull charge`;
  `dt audio render` for the ram and the warning against the other tells.

## 11. What building it needs

- **The solid world (queue #14, B1)** is a dependency. Hide-and-seek and "narrower than its rack" need buildings, cars
  and trees that block its sight and its body: a 3.2 m-wide sweep against the world, and sight lines against it.
- **Its ground** is a stop site (level-design H.2), placed by the stop generator like the Ribbits' warrens.
- **A1's #25** owns whether creatures die. The Bull is proposed as one that never does. That's the director's revision,
  and an exception to #25's "killable by a coordinated team".
- **Bots** need to know to give it room, and to break off and hide when it's on them.

## 12. Open questions for the director

1. **The name.** *The Bull*: short to scream, and the railway police pun. Or *the Moose*, which is what crews will
   shout anyway?
2. **Rams and roofs.** Does a ram only throw a roof player flat, as proposed, or can it put them over the edge, against
   T128's "hard to fall off"?
3. **Tier.** Frontier+ as proposed, or Local too, as a gentle first lesson in giving things room?
4. **Does it hear you while it searches?** Talking within a few metres of it giving you away would sharpen the
   hide-and-seek. It would also put the Bull on the voice layer, which already holds the Gaunt, the Choir and others.
5. **On the line.** A forward appearance: a bull standing on the rail that won't give way. It overlaps the Track Doll's
   forward slot, so it's left out for now.
