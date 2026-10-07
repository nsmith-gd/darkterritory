# The threat orchestrator, and the run between stops

*Design outline, 7 Oct 2026 (D1.2, queue #67, ARCHITECTURE §8 note 328). Answers the director's notes of 7 Oct (GDD App.
F.3, "Encounters" and "Pacing on the train"). A proposal for the director to read: the numbers are first-pass tuning, and
every section names the file they'd live in. §6.1, the hound run, is the first piece, in progress (note 328); the rest is not built.*

> "On the train, still relatively boring from point A to point B ... A couple playtests ago, I had two car huggers because
> I just kept the train hot the whole time and I didn't stop for anything ... we're going to need more threats that can
> board the train at speed to give players things to do ... activities on the train, things that players can do to maintain
> the train while it's going ... things that are trying to attack the train sort of like tower defense style that gives our
> gunners things to do ... a threat orchestrator that takes into account how many players are on and then orchestrates
> threats based on the number of active players in the game currently." (the director, 7 Oct 2026)

## 1. What's wrong now

1. **A fast train is safe from almost everything, by the numbers.** The train's top speed is 22 m/s (train.json
   `maxSpeed`). The Cinder Hounds can't board above 19 m/s (enemies.json `cinderHounds.maxSpeed`), the Climbers above 18
   (`climbers.maxSpeed`). The Whistler, the Passenger and everything outside come at stops. What's left at top speed is the
   Car Hugger (it lies in wait on low ground and clamps on as the train passes) and the trouble in the cars. That's exactly
   the director's playtest: kept hot, never stopped, two Car Huggers. App. F.1's boarding-first decision made the hauling
   train safe on purpose ("a hauling train is safe from most monsters, not all. A fast, flying class may come later to
   answer the top-speed strategy"). Nothing has answered it yet.
2. **One pressure for the whole train.** The director (App. B.1; note 266) builds one pressure from quiet, loudness,
   cargo and escalation, and spends it on whatever its weights pick. It doesn't know who is where or doing what. A crew of
   eight can have one player fighting hounds at the rear while seven ride in the cars with nothing to answer. Its crew
   multiplier is fixed when the night starts (`0.7 + 0.12 × crew`, to 1.6), whoever has died or dropped since.
3. **The guns have little to shoot between stops.** A gun answers four creatures (note 171: the Hounds, the Climbers at a
   gap, the Track Doll, the Switchman). A hound pack leaves at one round fired anywhere within 220 m
   (`cinderHounds.suppressRounds` 1), so the gunner's whole fight is one shot, aimed or not.
4. **Nothing on the train needs keeping up while it runs** but the fire. The driver has the line and the boiler, and the
   fireman has the firebox. Everyone else rides.
5. **Solo isn't dangerous, and crews of two are fair only when played right.** Note 300: a bot alone delivers every night
   swept, and income is the only wall. Note 305: every crew-counting creature is fair at crew 2 when the crew plays it the
   small-crew way. The orchestrator must not undo either: a crew of one gets one thing at a time, and a crew of two
   is never asked to be in three places.

## 2. Rules it keeps

Everything below keeps the GDD's rules. Where a proposal could bend one, the section says how it doesn't.

- **Telegraph before commit** (App. A.1). Every new threat has a tell: a sound, a sight or both, at least
  `minReactionSeconds` before it can hurt anyone.
- **Kills go through GRAB** (App. A.1). Nothing new kills outright: a bite leaves you on 1 (note 272), and a death is a
  grab nobody broke.
- **Boarding-first** (App. F.1, Decided). Nothing acts inside the train unless it boarded, by its own known rule at a
  known point, with a tell and a counter. Boarding speeds are per creature.
- **Hazards never change enemy rules** (§22). The cold, the dark, rain and wind make the crew's tools worse; they never
  change what a creature does or when it boards.
- **Every shot is a decision** (§14). Waves give the guns targets. They don't give the gunner free rounds: a ball is loud
  and feeds the Choir, the reload is three steps by hand, and the night's powder is 24 rounds a gun (combat.json `guns.ammo`).
- **Scaling is by train length** (§15). The orchestrator scales threats by who is *active*, which §15 rules out ("not
  enemy count multipliers"). The director asked for it (App. F.3), so this outline proposes it, and §7 asks the director
  to confirm the §15 change.

## 3. The orchestrator

The orchestrator is a layer over the director. The director keeps deciding *how much* (the budget) and *when* (the
pressure). The orchestrator decides *at whom*: which post gets the next threat, and how big it is for the people there.

### 3.1 The census (once a second, host only, deterministic)

1. **Active players.** Alive, in the night (not spectating or in a Holdout), and not in a fort. A player who has dropped,
   died or is held in a grab isn't active. `active` is their count.
2. **Each one's post**, from where they stand and what their hands are doing (the bots' `posts` already compute most of
   it):

   | Post | Where | Answers |
   |---|---|---|
   | **Driver** | In the cab, at the controls | The line ahead: boards, bends, the Track Doll, the Switchman |
   | **Fireman** | In the cab, at the firebox or the coal | The fire, the Stoker |
   | **Gunner** | Seated at a gun | Waves (§5.3), anything in the gun's arc |
   | **Walker** | On the roofs, the landings or in a coupling gap | Climbers, Draggers, Fire Flies, upkeep jobs (§5.1) |
   | **Rider** | Inside a car, not working | Upkeep jobs, the trouble in the cars, boarders |
   | **Ground** | Off the train, near it | The yard's and village's creatures (B.6) |
   | **Left behind** | Off the train, past 150 m | Their own hunt (note 273), unchanged |

3. **Each one's slack**: seconds since that player last had something to answer. They answer something when they're
   engaged with a threat, a threat targets their post, or an upkeep job at their post is open. Slack grows only while the
   train is out on the line (not in the forts, the grace or the final approach).

### 3.2 What it does with the census

1. **The crew multiplier is live.** The budget's crew multiplier uses `active`, not the crew the night started with,
   recomputed each second (`0.7 + 0.12 × active`, cap 1.6 as now). A crew that loses two players gets a director that
   spends like a smaller crew's. The relief valve (alive share squared) stays: the two compound, which is the point.
2. **Who's next.** When the director's pressure passes its threshold, it weighs its options as now, then multiplies
   each by how well it answers the post with the most slack: ×`slackWeight` (2) for a threat whose answer is at that
   post, ×1 otherwise. A threat whose answer is at a post nobody holds is weighed ×`emptyPostWeight` (0.5) unless its rule is
   to punish an empty post (the Track Doll's empty cab, B.2).
3. **Per-player caps.** At most `perPlayer` (1) threat engaging any one player at once, and at most
   `ceil(active × 0.75)` engaged in all (1 at crew 1, 2 at crew 2, 3 at crew 4, 6 at crew 8). That's under App. B.1's
   flat 4/6, which stay as the ceiling.
4. **Slack presses.** A player at `slackPress` (150 s) of slack adds `slackPerSecond` (0.05) a second to the director's
   pressure, for each such player, so a crew with several people idle fills faster than one with one idle.
5. **Solo and crew 2.** `active` 1: one threat at a time, never two. `active` 2: two, never both on one player. The
   per-player cap means note 305's crew-2 answers still hold.

### 3.3 Tuning (enemies.json `director.orchestrator`, first pass)

```
"orchestrator": { "on": true, "liveCrew": true, "slackWeight": 2, "emptyPostWeight": 0.5, "perPlayer": 1,
  "engagedPerActive": 0.75, "slackPress": 150, "slackPerSecond": 0.05 }
```

## 4. Pacing targets

What a night on the line should feel like, measured by the harness's pacing trace (`dt harness`, `dt balance`; the
director's spawn log and note 270's beats). First pass; each is a `balance.json` check once it's built.

| # | Target | First pass |
|---|---|---|
| P1 | Every active player has something to answer at least once in every | 150 s on the line (slack never past 150 s for more than one player at once) |
| P2 | The guns have a target while the train runs fast, at least once every | 2.5 km above 15 m/s (about every 2 min at top speed) |
| P3 | Something is engaged for this share of the night on the line | 45–60 % |
| P4 | Troughs: nothing new for at least | 25 s after a spawn (App. B.1's cooldown, unchanged) |
| P5 | A night kept hot and never stopped gets | at least 3 hound runs (§6.1) on a 24 km frontier line |
| P6 | Stops stay the peaks: share of grabs at stops and slowings | ≥ 50 % (App. F.1: "slowing opens the doors") |
| P7 | The terminus approach | silent, 500 m out (App. B.1, unchanged) |
| P8 | Solo: threats engaged at once | never more than 1 |

## 5. Things to do between stops

### 5.1 Upkeep while the train runs

Small jobs the train makes as it runs, each at a known place with its own sound, each fixed with a tool the crew has.
Left alone, each costs the train something (speed, a car, the dark), never a life on its own. They give riders and
walkers something to do, and make walking the train a reason to be out on it. The orchestrator counts an open job as
something to answer for whoever's post it's at. One job open per `active` player at most, and none in the grace or
the final approach. All in a new `content/tuning/upkeep.json`.

| # | Job | Comes | Tell | Fix | Left alone |
|---|---|---|---|---|---|
| U1 | **Hot box** (an axle box running dry) | Every 4 km of running per car, ±50 %; 1.5× above 18 m/s | A squeal, then smoke from the bogie, at the car's end | Grease from the landing above it: hold Use 3 s with the oil can (the lampman's locker) | After 60 s the car drags: −2 m/s top speed. After 120 s the box catches: a car fire (C.5) |
| U2 | **A coupling working loose** | Every 6 km of rough track (bends, points) per gap | A knocking at the gap, getting faster | The wrench in the gap: hold Use 4 s | After 90 s the pin drops and the rake parts behind it: the cut-car rules (§24). The worst job, the rarest |
| U3 | **A lamp guttering** | Each car lamp every 5 km | The light flickers and dims | Trim it: Use 1 s at the lamp | It goes out: the conditions' dark (×1.15 on the pressure), and Fire Flies' pull while it's out |
| U4 | **Powder to the guns** | When a gun's ready rack is empty | The gun's rack shows empty; the gunner calls | Carry a charge from the stores car (the guard van's magazine) to the gun: a 6-round rack (combat.json `guns.rack`), filled from the night's 24 | The gun can't fire. It turns a gunner's reload into a walker's errand under fire |
| U5 | **Sanding** (exists: App. A.2 GREASE) | Grades, wet rail | Wheelslip, the beat stutters | Sand from the running boards | The train stalls on the grade (exists) |
| U6 | **Water** (the boiler's water, if the boiler gets a glass) | Every 8 km | The gauge glass falls | The injector in the cab, Use 2 s | The fire must be dropped or the crown sheet goes: §23's rupture |

U4 is the one that ties to the guns: a wave with one gunner and an empty rack is a crew problem, not a gunner's.

### 5.2 Threats that board at speed

Each with its own boarding rule, at a known point, with a tell and a counter (App. F.1, Decided). These answer the
top-speed strategy; none comes to a train under `minSpeed` (they're the run's, not the stop's).

| # | Threat | Boards | Tell | Counter | Status |
|---|---|---|---|---|---|
| S1 | **The hound run**: Cinder Hounds coming in a stream, faster than the train | The rear car's end, from the line behind, at any speed | Howls behind, the pack's eyes in the dark | The guns, one ball per hound (a ball landing near scatters it) | **First piece (§6.1)** |
| S2 | **Climbers at speed**: raise `climbers.maxSpeed` to 24 m/s on Dead Lines+ | A coupling gap (B.4) | Pacing alongside, scrabbling | A walker in the gap, or a gun on the gap | Tuning only |
| S3 | **The kites** (new, App. F.1's "fast, flying class") | The roofs, only above 18 m/s, in open country | A shriek overhead, a shadow across the lamp | Lamps lit on the roofs (they won't land in light), or a walker with a tool. The gun can't elevate to them (`maxPitchDegrees` 45) unless one's on a roof | New creature: art, clips, a rule |
| S4 | **Draggers off a bridge** (B.4 variant) | Dropping onto the roofs from an overbridge or a tunnel mouth | A scraping above as the bridge comes up | Nobody on the roofs under a bridge, or knocked off with a tool | Rule change |
| S5 | **The Car Hugger** (exists) | The rear car as it passes low ground | Grinding | Cut the car, or club it | Unchanged |

### 5.3 Waves at the guns (tower defence)

A wave is a stream of runners the guns answer one at a time, in lanes the guns cover: behind (the guard gun), the two
flanks (both guns, at the edges of their arcs) and ahead (the forward gun, past the stack's mask). Each runner is a target
for one ball. Each one that gets through boards and is the rest of the crew's problem: the gunner's misses land on the
walkers and riders, which is what gives everyone something to answer at once.

1. **When.** The run has gone `afterMetres` above `fromSpeed` since the last stop, the last wave, or the start of the
   night past its grace. A hot firebox shortens it (×`hotShorter`): the heat draws them (App. B.3's ×1.5 for a hot boiler).
2. **How many.** `round(base + perActive × active)`, in pairs, between `size[0]` and `size[1]`: 2 at crew 1, 3 at crew 2,
   4 at crew 4, 6 at crew 8. A crew of eight gets six runners; a lone driver gets two.
3. **How they come.** In pairs, `spacing` seconds apart, from `spawnBehind` m behind, alternating flanks, closing at
   `closing` m/s faster than the train. That's faster than the train can run, so a fast train can't outrun them. About
   25 s from first howl to the leap for each pair, so one gunner reloading by hand (about 5.5 s a round) gets four shots
   at each pair.
4. **Answered.** A ball that lands within `scatter` m of a runner scatters it (it breaks off, App. A.3) and one on it
   kills it (note 290). A pair running close is one good shot. Rounds fired near but not at them no longer drive the
   whole wave off: that was the old pack's rule and stays with the director's packs.
5. **Missed.** A runner that reaches the train boards the rear car and is the pack fight (App. A.3), staying aboard
   (note 269). The wave's tell is the howls; the boarding's is the leap.
6. **Later waves** (not built): lanes ahead (runners crossing the line in the lamp, for the forward gun), lanes on the
   flanks from the open country's sides, and other runners than hounds, each by its own rule.

## 6. The first piece, and what's next

### 6.1 First: the hound run (note 328)

§5.3 with the Cinder Hounds as the runners, behind and on the flanks. enemies.json `director.run`:

```
"run": { "on": true, "fromSpeed": 15, "afterMetres": 2400, "hotShorter": 0.75, "base": 1.25, "perActive": 0.6,
  "size": [2, 6], "spacing": 6, "spawnBehind": 200, "lateral": [4, 8], "closing": 5, "scatter": 5 }
```

Outside the director's budget and caps, like the left-behind's hunts (note 273): it's the run's, and only a train
running fast gets it. Nothing in the grace, the forts, the final approach, or while the train's at a stop.

### 6.2 Next, in order

1. **The live crew multiplier and per-player caps** (§3.2 1, 3, 5): small, testable in the existing harness.
2. **Powder to the guns** (U4): the rack, the magazine and the carry. It makes the guns a two-person job in a wave.
3. **Hot boxes and loose couplings** (U1, U2): the upkeep that gets walkers onto the train.
4. **Slack and posts** (§3.1, §3.2 2, 4): the census and who's next; the bots' `posts` already compute most of it.
5. **Climbers at speed** (S2): tuning and a sweep.
6. **The kites** (S3): a new creature, after the director's yes.

## 7. What changes in the existing director

| # | Today (App. B.1, note 266) | Proposed |
|---|---|---|
| D1 | Crew multiplier from the crew at the night's start | From `active`, live (§3.2 1) |
| D2 | One pressure; weights pick a threat for the train | The same pressure; the weights also favour the post with the most slack (§3.2 2) |
| D3 | Caps: flat 4 (crew ≤ 4) or 6, 2 a zone | Also `ceil(active × 0.75)` engaged and 1 per player (§3.2 3) |
| D4 | Quiet is the train's (seconds or metres since a threat) | Plus slack: idle players press it (§3.2 4) |
| D5 | Hounds: a pack of 3–5 behind, any round fired near drives all off, can't board above 19 m/s | The director's packs as before; the run (§6.1) is the fast train's: faster than the train, one ball per hound |
| D6 | Nothing between stops but the director's spends | Upkeep jobs (§5.1) and the run (§5.3), outside the budget |
| D7 | §15: "scaling by train length, not enemy count multipliers" | Train length still sets the work; who's active sets how many come at once. **For the director: confirm the §15 change** |

## 8. Questions for the director

1. §15's change (D7): scaling threats by active players, as App. F.3 asks, against §15's "not enemy count multipliers".
2. The kites (S3): a new flying creature, or should the top-speed answer stay with the hounds and the Climbers?
3. Upkeep (§5.1): which of U1–U6 first? U4 (powder to the guns) is proposed, because it makes a wave a crew job.
4. Should a missed runner board, as built (a pack fight in the rear car), or should it do something to the train itself
   (a coupling bitten through, a car's brake wrenched on)? Boarding keeps every existing rule; the second is new.
