# THE MOOSE — a corrupted bull moose

*Creature design, G1 (enemy design). **Status: approved by the director, 7 Oct 2026, and built (queue #50, ARCHITECTURE
§8 note 311).** It's in the GDD (§21, App. A.6, A.9, B.1, B.6, Part Eleven Q14), level-design H.2, the systems spec (B.12,
pinned by `SpecTableTests.TheMooseMatchesB12`) and `content/tuning/enemies.json` `moose`. This page keeps the reasons; the
numbers in §9 are the tuning's. Where building it changed the design, it says so (**As built**).*

> **A hyper-aggressive, territorial moose, too big to get on the train.**

**The director's decisions (7 Oct 2026):**
- **The bullfight is the heart of it.** Keep the charge and the narrow places.
- **It's docile until bothered.** Too close, or hit, and it charges. Otherwise it stays where it is, something you're
  casually aware of around you.
- **There are three ways to be rid of it:** get far enough away, play hide-and-seek round the buildings until it gives
  up, or drive the train away.
- **It's too big to board.** It rams the car it knows you're in for a while.
- **Its name is the Moose.**
- **A ram is just a ram.** It never throws anyone off the train or knocks them down.
- **It's in every tier,** more often the harder the tier.
- **Talking near it gives you away,** and it raises its aggro.
- **It's never on the rail.** It can stand beside the line, and a train passing close riles it a little, but nothing of
  it can ever be in the train's way.

---

## 1. Why a moose

- **It's the Maritimes' animal.** The line runs through Nova Scotia gone dark (`docs/design/maritime-rules.md`): bog,
  granite barrens, black spruce. October, when the game is set, is the rut.
- **Real bull moose are territorial.** In the rut they charge whatever crowds them: people, cars, trains.
- **It's the GDD's premise.** "*Prey became territorial and violent*" (§2). The Corruption "exaggerates whatever allows
  something to survive": here the mass, the rack, and the rut's temper, all grown past sense.
- **It's a Split creature.** Split is the director's thinnest want, at 3 of 17 creatures against a 25% target (B.1). The
  Moose splits the crew: whoever riled it is chased off alone while the rest keep working.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE MOOSE** · *grazing beside the line and at the stops*
A moose in a rut that never ended, its rack grown wider than a doorway. Leave it be and it leaves you be.
> **RULE: give it room, keep it quiet.**

Crowd it, talk near it, or hit it and it charges, in straight lines it can't turn out of. Dodge it, lead it off, or lose it
among the buildings, quietly, until it gives up. Hit it to take its attention off a friend. It never comes aboard. Nothing
that wide fits through a door, so it rams the car you're in for a while, and only the train pulling away ends that for
good.

**Sense trigger:** movement (trespass) and sound. **Zone:** outside. **Want:** Split. **Cost:** 3.

**As built:** its one primary sense (App. A.1) is movement: what it sees coming onto its ground. That keeps it apart from
the Ribbits (sight) and the Gaunt (sound) under B.1's one-tell-type-at-once cap, so it can be paired with both.

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
  swollen flesh, stretched skin*"). A ridge of them runs along its spine. The pale hide shows up beside the line in the
  headlamp, so it reads at distance in fog by its pallor (Part Eleven Q6).
- **The ears are its meter.** They're oversized, torn and always turning toward voices. Forward and swivelling means it's
  listening; pinned flat means it's coming. Its aggro is readable from 30 m without a HUD.
- **The bell** (the dewlap) is swollen into a long sac that swings when it walks.
- **Too long in the leg**, knees knobbed, hooves split and black. It stands **2.4 m at the shoulder**, about 3.4 m to the
  top of the rack.
- **Unnerving in stillness** (§29). Calm, it grazes with its head down and steam coming off it in the cold, the sacs
  pulsing, the ears never still.
- **Budget:** a large monster, 8,000–16,000 triangles (§27), in the Car Hugger's class. Material families: hide, sac,
  mineral rack, velvet.

**Clips** (for `tools/blender`; `dt art clearance` must fit the rack's capsules): `graze`, `listen` (head up, ears
turning), `warn` (ears back, ridge up, head low), `walk`, `trot`, `squareUp`, `charge`, `overrun` (the skid and wheel
round), `snag` (rack jammed, hauling), `search` (head sweeping, ears swivelling), `ram`, `pin`, `strut` (the walk home),
`trainPass` (head up and tossing as a train goes by).

## 4. How it sounds (a request to the audio chat)

Everything the Moose does is heard before it's seen, and none of it may sound like another creature's tell (A.1).

| Moment | Sound | Distinct from |
|---|---|---|
| Grazing (presence) | Tearing browse, slow chewing, a sac's wet creak, the occasional low grunt | The Gaunt's slow breathing |
| Listening | The chewing stops: the silence is the tell | — |
| Warning | A cough-like grunt, teeth clacking, a hoof dragged through the ground | Ribbits' throat-swell (silent) |
| Squaring up | Two hoof stamps and a snort | — |
| Charge | Hooves on ballast, a wheeze, brush breaking | The Hounds' approach (howling) |
| Snagged | Wood groaning, the rack grinding, a bellow of rage | — |
| Searching | Heavy snorting breath, the rack knocking on walls | — |
| Ramming a car | A deep iron boom through the whole car, the rack scraping the plates | A Climber's scrabble |
| A train passing | A bellow after it, and hooves thrashing the verge | — |

## 5. Behaviour tree (App. A.6 format)

### THE MOOSE · sound, sight (trespass)

Its temper is an **aggro meter**, 0–100, as with the Gaunt (A.6). The meter is the Moose's own; nothing on the HUD shows
it, and its ears and posture do.

```
GRAZING   beside the line or at its ground by a stop; docile; aggro falls when it's given room and quiet
          └ TELEGRAPH (presence): seen and heard from the train, pale in the lamp
AGGRO     rises from:
          · anyone on the ground it can see within 20 m (faster within 12 m)
          · anyone talking within 15 m (faster the louder; their position heard, not just their noise)
          · the train passing within 25 m (a little, once)
          · a hit (a blow, a thrown thing, a gun round): straight to full
          falls when nobody's within 20 m and nobody's talking within 15 m
LISTENING aggro 20+ → head up, chewing stops, ears turning to whoever's loudest
          └ TELEGRAPH: back off and hush and it settles
WARNING   aggro 50+ → ears flat, the sac ridge on its spine stands up, a grunt, a hoof dragged
          └ TELEGRAPH: back off and hush now and it settles
ENRAGED   aggro 100 → its TARGET is whoever put the most into the meter (the one who hit it, if anyone did);
          whoever hits it last is always the target from then on
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
SEARCH    target out of its sight → goes to where it last saw or heard them, sweeping its head, ears turning;
          sees them again, or hears them talk within 15 m → goes straight for them → SQUARE UP
          └ 25 s without seeing or hearing them → GIVES UP
RAM       target seen going aboard, or lost within 5 m of a car → rams that car every 3 s for 15 s, then GIVES UP
          (a ram is just a ram: the car booms and shudders; nobody falls, is knocked down or is hurt;
          nothing in the car is lost)
GIVES UP  also: its target more than 80 m from its ground → walks home and grazes, aggro back to 0;
          it can be riled again the same way
TRAIN     the train pulls away → done for the stop
```

**Never on the rail.** It grazes, stands, searches and rams from beside the line, never on it. Its ground and its
lineside spots are always outside the train's clearance. It is never in the train's way: the train can't hit it, and the
Moose can't stop or derail the train.

**As built:** the track's clearance is a line it never crosses at all (3.2 m from any track's centre, the rack's half-span
off a car's side; 6 m while the train moves and where it's put down). The first draft let it cross a track at a run during
a chase; the simplest rule that can never put it in the train's way is that it doesn't, so the far side of the line is
somewhere it can't follow. Crowding also counts only what it can see: someone round a corner or behind the train isn't
crowding it (its voice-hearing still works through walls), which is what makes hiding work.

**The passing train.** A moose beside the line takes a train going by as a rival. It tosses its head, bellows after it,
and thrashes the verge, for a little aggro. It never acts on the moving train. The aggro only matters if the train stops
nearby: a moose by a stop that the train has just rolled past is already listening when the crew climbs down.

**Nothing kills it.** Blows and gun rounds only make you its target. It's a force of nature you manage, not a fight you
win. **Hitting it is a tool, not an attack:** the way to take it off a friend who's pinned or about to be charged, at the
price of being the one it chases.

**It never boards.** Its rack won't go through a door, so every door is shut to it, open or not. It keeps to the
director's boarding-first rule (App. F.1) without an exception. It acts only on people on the ground and, harmlessly,
on the outside of a stopped car.

**The bullfight.** Someone chats too close and it comes for them. They dodge the charge. A friend hits it from behind,
takes it over and leads it off. They pass it between them and duck down the alleys between sheds and cars. The hard
part is going quiet once they're hidden, in a game where everyone's shouting, while it searches a few metres away. The
first death teaches the rule: the body lies two metres from a doorway, and the crew heard the victim say "I think it's
gone" just before.

**No chip damage** (note 272). Two hits, then the pin, as with the Gaunt. The charge is the only thing that hurts. Every
charge comes after the listening and the warning, either of which you could have backed off from, and a 2.5 s
square-up that says which way it will go.

## 6. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The moose that's too big to get on the train." |
| 2 | Answered better by two | ✔ A friend's hit takes it off you, so a pair passes it back and forth and neither is charged twice running. Alone, you can only run and hide. |
| 3 | Consequence now, death later | ✔ The first charge knocks you flat, the second pins you, and the pin is 12 s long. |
| 4 | A verb, not just a "don't" | ✔ Dodge it, pass it, lead it off, hide from it, hush around it. |
| 5 | Someone gets blamed | ✔ Whoever put the most into its meter, the one who wouldn't stop talking, or whoever hit it. "Why did you HIT it?" |
| 6 | Fun to scream | ✔ "MOOSE! MOOSE! GET BETWEEN THE SHEDS!" Then everyone has to stop screaming. |

All six.

**The voice layer (A.9).** It joins the Gaunt, the Choir, Tippy Toesie, the Soot Children and the Passenger. It reads
*position*, not just loudness. The Choir hears a loud crew. The Moose hears where the loud one is standing.

**Benchmarks** (App. F.1, "benchmark every creature against Lethal Company and R.E.P.O."):
- Lethal Company's **Eyeless Dog**: talk near it and it comes straight for you. The Moose adds a visible warning and a
  dodge.
- The **Thumper**: lethal in a straight line, beaten by corners. Hide-and-seek round the buildings is the same lesson.
- The **Forest Keeper**: huge, survivable if you keep your distance and use the cover.
- R.E.P.O.'s carry-under-threat: the Moose is worst for whoever is **carrying** (2.8 m/s and slow to sidestep). A carry
  past its ground needs someone ready to take its attention.

## 7. Spawn rules (App. B.6 row)

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Moose** | Grazing at its ground beside a stop, where the crew will pass; or beside the line on the run | Every tier · one about at a time (two bulls never share ground) | By tier ×1 Local, ×1.5 Frontier, ×2 Dead Lines, ×2.5 Deep Territory. By biome ×2 bog, ×1.5 barrens and lakeshore, ×1 forest. Up per player on the ground |

- **Every tier, more the harder the tier.** It's a fair first lesson on Local (give it room, keep it quiet). On Deep
  Territory it's at most stops, beside the other outside creatures.
- **Any crew.** **As built:** the first draft gated it at crew 2 because a lone player can't be rescued. The build already
  has a self-escape for every grab at a crew of one (the solo rule, `grab.soloStruggle`, T89): a pinned lone player
  struggles free. So the Moose comes to solo nights too, as the director's "all difficulties" wants.
- **It's a resident at stops.** The director places it at its ground when the train arrives, like the slaughterhouse's
  Gaunt (`residents`), after the 20 s facility lull. A crew that gives it room pays nothing but attention.
- **On the run** it's dressing that matters: a moose beside the line, `lineside` of them per 10 km by tier, never within
  the clearance, riled a little by the train passing. These cost nothing, and only one standing near a stop the train
  stops at can come to anything.
- **Pressure cost: 3.** It's often harmless and costs a crew only when they bother it. **Want: Split.**

**Level design (H.2 addition):** *The Moose's ground (B.6): open grazing on the stop's wettest ground (bog, lakeshore, a
flooded ditch), 30–60 m from where the consist stops, within 25–40 m of a walk the crew has to make (to the loading,
the village, a switch), but never on it, and never on or within clearance of any track.* Close enough that you're always
aware of it, but never in the way. There's always cover between it and the train: a shed row, a rake of standing cars, or
trees with gaps under 3 m.

## 8. Contradictions (App. B.1 conflict table)

| Pair | The bind |
|---|---|
| **Gaunt + The Moose** | Keep talking to the Gaunt vs. the moose hears every word, and where it came from |
| **Ribbits + The Moose** | Never be outnumbered vs. whoever the moose chases ends up alone |
| **Whistler + The Moose** | The coupling gaps are the moose's blind spot and the Whistler's hiding place |
| **Moose + facility loading** | The loading needs everyone, calling to each other vs. someone has to keep the moose busy, and quiet |

The Gaunt pair is now the cruellest in the roster. Whoever woke the Gaunt has to keep talking to it and walk it home past
the moose's ground, and every word they say fills the moose's meter. The Gaunt punishes silence and the Moose punishes
voices, so the answer is to talk quietly, wide of its ground.

## 9. First-pass numbers (proposed `enemies.json` `moose`)

The tuning as built (`content/tuning/enemies.json` `moose`; its comment documents every field):

```jsonc
"moose": {
  "crowdAt": 20, "crowdPerSecond": 25, "closeAt": 12, "closePerSecond": 70,
  "hearVoice": 15, "talkingAbove": 40, "voicePerSecond": 40,
  "trainPassAt": 25, "trainPass": 25, "calmPerSecond": 15,
  "listenAt": 20, "warnAt": 50,
  "squareUpAt": [12, 30], "squareUpSeconds": 2.5, "huntSpeed": 4.5,
  "chargeSpeed": 11, "overrun": 8, "wheelSeconds": 2.5, "rackSpan": 3.2, "hitReach": 0.5, "snagSeconds": 4, "blockedCharges": 3,
  "chargeDamage": 60, "grabBelowHealth": 40, "pinSeconds": 12,
  "sightRange": 60, "searchSpeed": 2.5, "searchSeconds": 25, "leashRadius": 80,
  "lostAtCar": 5, "ramEvery": 3, "ramSeconds": 15,
  "trackClearance": 3.2, "movingClearance": 6, "goneBeyond": 600,
  "groundAt": [30, 60], "minCrew": 1, "perGroundWeight": 0.5,
  "tierWeights": { "local": 1, "frontier": 1.5, "deadLines": 2, "deepTerritory": 2.5 },
  "lineside": { "local": 2, "frontier": 3, "deadLines": 4, "deepTerritory": 5 },
  "linesideOut": [8, 22], "linesideAhead": 300,
  "biomeWeights": { "marsh": 2, "plains": 1.5, "contaminatedMarsh": 1.5, "coast": 0.5, "blackForest": 1, "forestEdge": 1,
    "mountain": 1, "deadTown": 0.5, "industrialRuin": 0.3, "slag": 0.3 }
}
```

Body for the guns (`bodies`): `"moose": [[0.9, 1.6], [0.9, 2.1], [0.6, 2.6]]`, the barrel and the hump. A round in the
body is a hit, so it makes the gunner the target, and the gunner is aboard: it rams the gun's car. The rack stops nothing.

**Why these numbers:**
- **The meter's pace.** Walk past at 20 m and it listens. Stop at 12 m and it's full in about 1.5 s. One crewmate chatting
  at a normal voice (about half of full, so 20 a second) 15 m away fills it in about 5 s: long enough that a sharp
  "shh, moose" saves you, and short enough that it bites. All of it falls away at 15 a second once you back off and hush.
- **The passing train** adds 25 once: enough to make it listen, never enough to warn on its own.
- **11 m/s charge** is twice a player's run (5.5), so you can't outrun it in the open. That forces the sidestep or the
  narrow places.
- **2.5 s square-up** sits inside A.1's 1.5–4 s window, at the generous end, because a miss costs a hit.
- **60 a charge, pin at 40:** the Gaunt's model. Two hits, then a 12 s pin, inside A.1's 8–20 s rescue window.
- **25 s search, 80 m leash:** long enough that a single corner won't shake it, short enough that hide-and-seek wins
  inside a minute, if you're quiet. It always gives up eventually.
- **15 s of ramming:** "for a little bit". It holds you in the car, and costs you only time.
- **3.2 m track clearance** (6 m while the train moves): its rack's half-span off a car's side, so it can stand at a
  stopped car to ram it and never be over the rail.
- **3 blocked charges:** a target safe in a narrow place isn't besieged all stop; it gives up and goes home.

## 10. What the harness verifies

- **Warning before charge:** no CHARGE without LISTENING, WARNING and a full SQUARE UP before it, unless the target hit
  it; its heading locks at the square-up's end (`dt audit`).
- **Docile when left be:** a crew that keeps beyond 20 m and doesn't talk within 15 m is never charged through a whole
  stop (a test).
- **Hushing works:** a crew that backs off and goes quiet at WARNING is never charged (a test).
- **Rescue:** every pin can be broken by the crew present at crew 2 (`dt audit grabs --crews 2 --only moose`).
- **Narrow is safe:** a target standing in a doorway, a coupling slot, or between trees 3 m apart is never hit (a test).
- **Hiding works:** a target out of sight and silent is never found once the search ends.
- **It always gives up:** out of sight, beyond the leash, or after its rams, it goes home every time; the train leaving
  ends it.
- **Never on the rail:** across a night's sweep, no moose ever stops within `trackClearance` of a track, and none is ever
  within the clearance of a moving train (`dt linegen sweep` and a harness check).
- **A ram is just a ram:** no ram moves, damages or hurts anything, or anyone, in or on the car.
- **Never boards.**
- **Sweeps:** `dt balance --pairs --only moose,gaunt`, `moose,ribbits` and `moose,whistler` at crews 2 and 4 (B.10:
  hard, not impossible). Tier spread: more moose at each tier up, at matched crew and length.
- **Seen and heard:** `dt screenshot --view moose` (warning in the lamp), `moosecharge` and `mooseline` (one beside the
  line in the headlamp); `dt art clip moose charge`; `dt audio render` for its tells against the others'.

## 11. What building it needs

- **The solid world (queue #14, B1)** is a dependency. Hide-and-seek and "narrower than its rack" need buildings, cars
  and trees that block its sight and its body: a 3.2 m-wide sweep against the world, and sight lines against it.
- **Proximity voice in the sim.** It needs each player's Voice byte and position, which the Gaunt (silence) and the
  Choir (loudness) already read.
- **Its ground** is a stop site (level-design H.2), placed by the stop generator like the Ribbits' warrens, and checked
  clear of the tracks. The lineside moose are linegen dressing with a sim body, also checked clear.
- **A1's #25** owns whether creatures die. The Moose never does, by the director's decision, as an exception to #25's
  "killable by a coordinated team".
- **Bots** need to give it room, hush near it, and break off and hide when it's on them.

## 12. Open questions

1. **Does shushing count?** (GDD Part Eleven Q14.) On the Choir it does (Part Eleven Q13). For the Moose, a whisper of "shh" under the
   `talkingAbove` floor is free, so whispering is the skill.
