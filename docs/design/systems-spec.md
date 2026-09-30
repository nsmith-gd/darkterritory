# DARK TERRITORY — SYSTEMS SPEC
## Companion to GDD v1.0 · Unity · September 2026

Implementation-facing. Every number here is **first pass, built for feel-testing and correction** — not balance. The two anchors are player run speed and train max speed; almost everything else is derived from their ratio.

---

# PART A — AUDIO DIRECTION

## A.1 The core principle

**Audio is not atmosphere in this game. It is the primary tell channel.**

Fourteen of twenty-one enemies telegraph through sound. The Choir *is* a sound mechanic. Rattle is nothing but audio. Soot Children is a trick played on the voice system itself. Proximity voice is a hard dependency.

Which produces the governing constraint:

> **If a tell doesn't cut through the mix, the enemy is unfair. Mix priority is a fairness system, not a polish pass.**

## A.2 The train bed, and masking as a mechanic

The locomotive is a continuous, loud, layered bed: boiler roar, rod clank, wheel-on-rail, brake squeal, coupling slack, wind.

**This bed masks things, and that masking is deliberate.** Being deep in the consist at speed means hearing less. Cutting the engine to hear something is a real tactical choice with a real cost — you lose lights, traverse and speed.

The bed is fully parameterised and drives from sim state:

| Layer | Driven by |
|---|---|
| Boiler roar | Pressure, firebox temperature |
| Rod / drive clank | Speed, tempo-locked |
| Wheel-rail | Speed, track condition, curve radius |
| Coupling slack | Acceleration delta — the "slack action" clunk running down the train |
| Brake | Brake application, heat |
| Wind | Speed, exterior only |
| Structural groan | Mass, grade |

**Slack action is the highest-value single sound in the game.** A 20-car consist changing speed produces a clunk that travels the length of the train. It communicates train state to everyone, everywhere, without UI.

## A.3 Mix hierarchy

Strict ducking priority. Higher tiers duck everything below.

| Tier | Content | Behaviour |
|---|---|---|
| **1** | Enemy telegraphs | Ducks all else −9dB. Never masked. Never occluded beyond −6dB. |
| **2** | Proximity voice | Ducks bed −6dB while active |
| **3** | Critical train state | Pressure alarm, brake fade, breach |
| **4** | Player actions | Footsteps, tools, shovel, gun |
| **5** | Train bed | The floor everything sits on |
| **6** | Ambient world | Wind, distant, weather |

**Tier 1 is inviolable.** A telegraph that can be drowned out is a bug, and the agent harness should test it by generating maximum-chaos states and verifying tell audibility.

## A.4 Tell design rules

Every enemy telegraph must be:

1. **Spectrally distinct from the bed.** The train occupies low-mid heavily. Tells live at 2–6kHz or in rhythmic patterns the bed can't produce.
2. **Spatially precise.** Directional to within ~30°. Players must be able to say "car four, left side."
3. **Nameable in three words.** If a player can't describe it, they can't warn anyone.
4. **Non-repeating at short intervals.** Repetition trains players to ignore it.

### Tell allocation — collision check

*GDD v1.1 roster (T84-T88). The v1.0 table is superseded: the bands the retired enemies held (Clingers, Rattle, the Weight,
the Long Whistle, the Hollow, the Deadman, the in-car incidents) are reused where they fit.* Each tier-1 tell keeps a band of
its own among the tells that can sound together; `dt audio render` and `AudioTests` hold every one of them over the bed for
whoever has to hear it.

| Enemy | Signature | Band | Sound |
|---|---|---|---|
| Track debris *(hazard, formerly the Sleepers)* | Wet writhe, brief | 400Hz–2k | sleepers-writhe |
| Track Doll | A glassy giggle, in the car it haunts or the cab it's taken | 3–6k | doll-giggle |
| Cinder Hounds | Distant howl, closing | 500Hz–3k | hound-howl |
| Car Hugger | Heavy grinding at the rear, in heaves | 60–300Hz | hugger-grind |
| Draggers | Scrape at the edge lip as the limb comes up | 2–4k | dragger-scrape |
| Whistler | The train's own whistle with no hand on the cord | 200–800Hz | train-whistle |
| Climbers | Scrabbling at the gap they're mounting | 2–5k | climber-scrabble |
| Stoker | Soot fall, the fire hissing wrong | 1–3k | stoker-hiss |
| Tippy Toesie | Faint tiptoeing, short range | 1–2k | tippy-tiptoe |
| Fire Flies | Buzzing whine round a lamp | 9–12k | fireflies-buzz |
| Ribbits | Throats swelling: a bubbling croak | 100Hz–1k | ribbit-swell |
| The Gaunt | **Silent by design** (it listens for your silence) | — | — |
| Followers | Silent: a lump on a back, seen by friends | — | — |
| Soot Children | A child calling for help (tier 2; the eyes are the tell) | voice band | child-call |
| The Passenger | **Silent by design** (it never speaks) | — | — |
| The Switchman | Visual: the figure at the lever, the lamp wrong | — | — |
| Grumbler | Gnawing on the crates | 1.4–2.2k | grumbler-gnaw |
| The Choir | Layered voices, multiplying as it gathers | 300Hz–4k, wide | choir-voice |
| Car fire *(App. C.5)* | Crackle and pop through the boards | 6–9k | car-fire |
| Marsh *(hazard, formerly the Drift)* | Dry reeds rustling, in slow creeping swells | 12–15k | drift-rustle |

Overlapping bands (Draggers and Climbers, Tippy Toesie and the Stoker) are either never staged together or are told apart by
rhythm: the Dragger's scrape is one rasp, the Climbers' scrabble a clatter; the tiptoe ticks slowly, the hiss is continuous.
Three enemies are silent on purpose, and the Gaunt and the Passenger make silence itself the thing to listen for.

## A.5 Proximity voice

**Spec, because it is a mechanic:**

| Parameter | Value |
|---|---|
| Full clarity | 0–8m |
| Falloff curve | Logarithmic, 8–26m |
| Cutoff | 26m |
| Occlusion | Car walls −12dB and lowpass at 900Hz |
| Radio channel | Flat, no falloff, band-limited 300–3kHz, static floor |
| Radio range | Whole run, dies in tunnels and mine spurs |

**Soot Children exploit the falloff exactly.** Their mimicked voice plays at constant volume with no distance attenuation — which is why it sounds like it's right there when it isn't. The tell is a property of the voice system, so it costs nothing extra to build.

A 20-car train is ~330m. At 26m cutoff, **the crew fragments into roughly twelve voice zones.** That's the 5–8 player difficulty, and it emerges from the falloff curve rather than being designed separately.

## A.6 Production approach — cheap and generated

The degraded-fidelity art direction extends to audio, and it works in our favour.

**Layered synthesis over recorded libraries.** Most of this game's sound is machinery and wrongness, both of which synthesise well:

| Source technique | Used for |
|---|---|
| **Granular resynthesis** of cheap library metal | Rod clank, coupling, structural groan |
| **Filtered noise + resonant peaks** | Steam, boiler roar, venting, wind |
| **Pitched-down / time-stretched animal and human sources** | Creature vocals |
| **Physical modelling (simple mass-spring)** | Rattle, drag, scrape |
| **Convolution with cheap impulse responses** | Car interiors, tunnels, facility spaces |
| **Formant-shifted crew voice** | Soot Children |

**Deliberate degradation as aesthetic.** Bit-crush, sample-rate reduction to 22kHz on non-tell layers, tape saturation, light wow and flutter. It matches the art direction, it hides cheap sources, and it costs nothing.

**Budget guidance:** a single cheap industrial/metal library plus a creature library plus a synth is enough raw material for the whole game if processed hard. Total source spend under $300 is realistic.

## A.7 Unity implementation

**Recommendation: FMOD Studio.** Free under the indie revenue threshold, and it gives you parameter-driven adaptive mixing, sidechain ducking, and event-level voice limiting — all of which the tell-priority system needs and none of which Unity's native mixer does well.

The alternative is Unity's built-in AudioMixer with hand-rolled ducking. It's viable and saves an integration week, but you will rebuild half of FMOD badly.

| Requirement | Implementation |
|---|---|
| Tell priority | FMOD sidechain, tier 1 bus ducks buses 3–6 |
| Train bed | Single multi-parameter event driven by speed, pressure, mass, grade |
| Spatialisation | FMOD 3D with custom rolloff; occlusion via raycast against car geometry |
| Voice | Separate transport, routed through the mixer for ducking and occlusion |
| Voice limiting | Hard cap per enemy type — a Choir swarm must not eat the voice budget |
| Slack action | Positional delay chain down the consist, one impulse per coupling |

**Performance note:** a 20-car train with 8 players, roof wind, bed layers and multiple enemies is a real voice-count problem. Budget 64 concurrent voices, aggressive virtualisation, and test at max consist early.

---

# PART B — FIRST-PASS NUMBERS

## B.1 The two anchors

| Anchor | Value |
|---|---|
| **Player run speed** | **5.5 m/s** |
| **Train max speed** | **22 m/s** (~80 km/h) |

**Ratio 4:1.** Everything below derives from it.

## B.2 Player

| Parameter | Value | Note |
|---|---|---|
| Run | 5.5 m/s | |
| Walk | 2.4 m/s | |
| Roof run | 3.5 m/s | Wind and balance penalty |
| Roof walk (safe) | 1.8 m/s | Off the centreline is Dragger range |
| Ladder climb | 1.6 m/s | |
| Carrying heavy cargo | 2.8 m/s | No climbing |
| Jump gap | 2.2m max | Coupling gaps are 1.5m — jumpable, but not while it's rattling |
| Jump height | 0.8m | Playtest (T90): the 0.48 m a flat 2.2 m gap needs felt like no jump at all. A roof-run jump now carries ~2.8 m |
| Health | 100 | Most attacks 35–60 |
| Cold exposure | 600s to onset, 1200s to death | Resets in 20s near heat. Inside a car with a door open it builds at ¼ rate. (Was 200 / 320 / 45: cold was 73% of deaths in the 100-night playtest.) |

## B.3 Speed bands

The bands fall out of the 4:1 ratio, and they define what's possible at each speed.

| Band | Speed | Consequence |
|---|---|---|
| **Yard** | 2.5 m/s | **Below player run speed.** You can chase the train down and board it. This is what makes the decoupled-engine loading sequence work. |
| **Jump-off survivable** | <16.5 m/s | A knock on landing: 10 just above 1.5 m/s, rising to 80 at the lethal edge (T90 playtest) |
| **Jump-off lethal** | >16.5 m/s (3× run) | Death. Was 4.0 m/s, which made every step off a moving train fatal. Being *pulled* off (Draggers) still kills above 4.0 m/s |
| **Slow** | 6 m/s | Roof work comfortable |
| **Working** | 10–14 m/s | Standard transit |
| **Cruise** | 14 m/s | Target efficiency |
| **Max** | 22 m/s | Roof traversal dangerous, Draggers +50% grab range |

> **If you could catch it, you can leave it.** Boarding still needs the train under 3.5 m/s past you (a ladder you can catch). Since T90 leaving is more forgiving than boarding: you can survive a jump off at working speed, but the train goes on without you.

## B.4 Consist geometry

| Parameter | Value |
|---|---|
| Car length | 14m |
| Coupling gap | 1.5m |
| Car pitch | 15.5m |
| Engine + tender | 20m |
| Roof width | 3.0m (safe centreline 1.4m) |
| **3-car train** | **66m** — 19s to traverse at roof run |
| **10-car train** | **175m** — 50s |
| **20-car train** | **330m** — **94s end to end** |

At 20 cars, crossing the train takes a minute and a half in each direction. That is the behemoth feel, and it is also why the flank becomes indefensible.

## B.5 Mass and handling

| Element | Mass |
|---|---|
| Locomotive + tender | 90t |
| Empty car | 12t |
| Loaded car | 40t |
| **20 loaded cars + engine** | **890t** |

### Acceleration and braking

| Consist | Accel | Brake | 22→0 stop |
|---|---|---|---|
| 3 cars | 0.90 m/s² | 3.20 m/s² | 7s / 76m |
| 6 cars | 0.62 m/s² | 2.10 m/s² | 10.5s / 115m |
| 10 cars | 0.42 m/s² | 1.44 m/s² | 15s / 168m |
| 15 cars | 0.27 m/s² | 0.96 m/s² | 23s / 252m |
| **20 cars** | **0.18 m/s²** | **0.70 m/s²** | **31s / 346m** |

**T97 (playtest): the brakes are twice the first pass's** ("braking needs to reduce speed significantly faster"). The stop column is a stop with the steam off. With steam driving (B.6) the engine pulls against the brake until its pressure's down, so a stop on the brake alone takes longer, and the quickest stop is brake *and* vent: two people, the brake in the cab and the vent out by the smokebox. At 20 cars you begin braking 350–450 m before a stop.

### Grade

Gravity contributes `9.81 × sin(θ)`, so a 3% grade costs 0.29 m/s².

**Maximum climbable grade by consist:**

| Consist | Max grade |
|---|---|
| 3 cars | 9.2% |
| 10 cars | 4.3% |
| 15 cars | 2.8% |
| **20 cars** | **1.8%** |

A 3% grade is trivially climbable early and **physically impossible at 20 cars.** Route planning becomes a real constraint as you grow, with no artificial gating — the physics does it.

Descending, the same figure adds to your speed and your brakes fade at 8% per 10s of continuous application, recovering at 4% per 10s released.

## B.6 Boiler

| Parameter | Value |
|---|---|
| Pressure range | 0–100 |
| Working band | 60–90 |
| Redline | 95+ (rupture at 100 held 20s) |
| Below 40 | Hollow spawn condition |
| Tender capacity | 400 coal units |
| Shovel action | 1.2s per unit |

### Steam drives the train (T97, playtest)

There's no regulator. **The pressure sets the speed the engine can make:** none at the power floor (20), the line's top speed (22 m/s) at the safety valve (95), in proportion between; the engine pulls at full effort until it's within 3 m/s of that. Coal raises the pressure (faster), the vent drops it (slower, 12 per second held), and too much pressure just runs you at top speed. The cylinders draw steam with the effort of pulling away or with speed, whichever is more, drawing full demand from 85% of top speed: running flat out is the "full throttle" of the tables below. A standing train with steam stands on its brake, and it stays on it until the driver lets it off. Cruise (14 m/s) is about 68 on the gauge; the working band 60–90 is 12–20 m/s.

### Burn rate by consist

| Consist | Burn | Shovel interval | Tender endurance |
|---|---|---|---|
| 3 cars | 1 unit / 20s | Every ~20s | 133 min |
| 10 cars | 1 unit / 12s | Every ~12s | 80 min |
| **20 cars** | **1 unit / 8s** | **Every ~8s** | **53 min** |

At three cars, the boiler is a periodic chore someone fits around other work. **At twenty cars, with a 1.2s shovel every 8s, it is a full-time post** — and the crew has lost a body without any rule saying so. Tender endurance also drops below comfortable run length, forcing coaling stops on long routes.

## B.7 Guns

| Parameter | Value |
|---|---|
| Fire rate | 3/s |
| Effective range | 80m |
| Traverse | 200° |
| **Dead zone** | **20° each side along the train's own body** |
| Ammunition | 200 rounds/gun, resupply at POI |
| Choir aggro | +1.5 per round fired, decay 45s |

**The dead zone is what makes the flank uncoverable regardless of train length.** Guns face outward from the engine and guard car; the consist's own body is definitionally out of arc. This is a geometry fact, not a balance number, which means it can't be accidentally tuned away.

## B.8 Run length

| Parameter | Value |
|---|---|
| Route length | 18–40 km by tier |
| Transit at cruise | 21–48 min |
| POIs per run | 3–5 |
| Time per POI | 4–8 min |
| **Total run** | **28–45 min** |
| Dawn timer | Route length ÷ 11 m/s average, +40% slack (was +18%: after the 100-night playtest a stop, the posted boards and the in-car trouble didn't fit, and missing dawn was the commonest failure) |

The dawn budget assumes an 11 m/s average, below the 14 m/s cruise. **The slack is what you spend on stopping** — every POI, every repair, every revival eats it.

---

# PART C — DEATH AND REVIVAL

> **Superseded by GDD v1.2 Appendix D (Death, Holdouts and Return).** The Vigil is cut. A dead player returns only through a Holdout at a halt, village or yard; bodies are loot (D.9). This part is kept for the record.

## C.1 Death

A dead player becomes a spectator and **their body persists at the death location.** Bodies do not despawn.

Spectators follow living crew freely. They retain voice on a separate **dead channel** which the living cannot hear — they can talk to each other, and watch, and say nothing useful to anyone. That's the punishment.

## C.2 The Vigil

Revival is possible, expensive, and genuinely capable of ending a run.

### Requirements

1. **Recover the body.** Someone goes and gets it. This is a full excursion, at the death site, which is usually where something killed them.
2. **Body must be aboard**, in the engine car.
3. **Full stop.** The train must be completely stationary.
4. **Vent the boiler.** Pressure dumps from working band to **zero** and cycles heat through the car.

### The cycle

| Revival (this run) | Duration |
|---|---|
| First | 90s |
| Second | 120s |
| Third | 150s |
| Fourth+ | Not permitted |

**During the Vigil:**

- Engine off. No movement.
- Lights drop to emergency only.
- Turret traverse dead (no boiler pressure).
- **The vent is deafening.** Choir aggro spikes to maximum instantly, and every noise-triggered spawn weight doubles for the duration.
- Dawn clock keeps running.

**After the Vigil:** boiler pressure at zero. Rebuilding to working band takes 40s at three cars and **over three minutes at twenty.** Then you still have to accelerate a 890-tonne train from a standing start.

### The revived

They come back **cold**: cold exposure onset halved (300s), carry capacity reduced to light only, and they cannot operate the guns until the next POI.

### The alternative

**Do nothing.** A body carried to the terminus is revived free at the gate. You just play short-handed for the rest of the run, with fewer hands on a train that needs more of them.

That choice — 90 seconds of maximum exposure now, or a man down for thirty minutes — is the argument this system exists to generate.

---

# PART D — LOADING MODULES

POIs are **procedurally assembled** from a module grammar, so no two facilities operate identically.

## D.1 POI generation

```
POI = SPUR TOPOLOGY + 2–4 LOADING MODULES + POWER STATE + SCALE
```

| Dimension | Range |
|---|---|
| **Sidings** | 1–6 |
| **Manual switches** | 2–9 |
| **Dead ends** | 0–3 (mis-routing costs a reverse) |
| **Spur grade** | 0–4% (affects whether you can pull loaded cars back out) |
| **Power state** | Dead (must restart) · Partial · Live |
| **Scale** | Compact (60m) → Sprawling (400m from consist to furthest module) |

Power state matters most: a **dead** facility means someone finds and restarts a generator or donkey boiler before any powered module works — a separate excursion before the real work starts.

## D.2 Module roster

| Module | Interaction | Crew | Failure mode |
|---|---|---|---|
| **Gravity chute** | Position car precisely, pull release, watch the fill gauge, close at the right moment | 1–2 | Overfill damages car and spills; deafening throughout |
| **Gantry crane** | One player in an elevated cab drives X/Y/Z; ground crew rigs the load and calls position | **2 mandatory** | Crane operator cannot see the ground crew. Dropped loads kill. |
| **Capstan winch** | Two players hand-crank in rhythm to drag cargo from distance | **2 mandatory** | Desync stalls it; sustained, slow, both players fully exposed |
| **Conveyor line** | Start machinery at a powerhouse, then clear jams as they occur | 1 + 1 roaming | Jams every 30–60s; unattended jam stops the line |
| **Tipple** | Clamp the car, rotate it to load | 1 | Bad clamp derails the car on the spur |
| **Steam lift** | **Requires the locomotive coupled nearby and venting pressure to power it** | 2 | Ties loading directly to the boiler; you're at zero pressure while it runs |
| **Manual crates** | Carry by hand; heavy items need two | 1–4 | Slow, but works when everything else is dead |
| **Livestock ramp** | Herd animals up a ramp into a stock car | 2–3 | Constant noise. Raises Choir floor while active. |
| **Fluid gantry** | Connect hoses, monitor pressure, disconnect cleanly | 1–2 | Leaks if unattended. Chemical spill contaminates the car. |

## D.3 Design intent

Each module produces a different **shape of coordination failure**:

- Crane fails through **blind instruction** — the operator can't see what they're moving
- Capstan fails through **desync** — two people out of rhythm
- Chute fails through **timing** — a moment missed
- Conveyor fails through **attention split** — someone has to roam
- Steam lift fails through **resource conflict** — the boiler can't do two jobs
- Livestock fails through **noise** — it makes the whole region worse

The generator should favour combinations that split the crew across incompatible demands. **Two mandatory-2-player modules at a 5-player POI means someone is alone somewhere.**

---

# PART E — SESSION MODEL

| Parameter | Decision |
|---|---|
| **Architecture** | Host-authoritative. Host is a player. |
| **Host migration** | **Not supported.** Host disconnect ends the session. |
| **Lobby visibility** | Host's choice: public or friends-only |
| **Ping display** | **Ping to host shown prominently** in browser and lobby — non-optional UI |
| **Drop-in** | **At POIs only.** Joining player arrives at the facility, like a pickup. *Superseded by GDD v1.2 App. D.3: a mid-run joiner joins the respawn queue and returns through a Holdout.* |
| **Drop-out** | Any time. Character remains as an inert body until recovered or the run ends. *GDD v1.2 App. D.2: its kit is recoverable, with no crew-loss fee and no refund.* |
| **Campaign ownership** | Host owns it entirely |
| **Save slots** | **3 per host.** Slot must be selected when hosting. |
| **Autosave** | **Per POI**, on successful departure |
| **Crash** | Session lost. Campaign rolls back to last POI autosave. |

**The POI-only drop-in is a design win, not a limitation.** It gives joining a diegetic moment — a figure waiting at the facility — and it prevents mid-transit spawning, which would break the "getting left behind is fatal" logic that everything else depends on.

**Ping visibility is load-bearing.** Without host migration, a bad host connection loses everyone's run. Players must be able to see that before committing 40 minutes.

---

# PART F — ECONOMY

Currency: **scrip**.

## F.1 Income

Contracts pay on **delivered cargo that survives**. Nothing else.

| Tier | Value per delivered car |
|---|---|
| Local | 450 |
| Frontier | 700 |
| Dead lines | 1,100 |
| Deep territory | 1,700 |

**Running costs** — coal, ammunition, repairs — average **15% of gross.**
**Expected loss** — roughly one car's cargo per run at competent play.

### Net income by consist

| Consist | Tier | Loaded | Gross | Net |
|---|---|---|---|---|
| 3 | Local | 2 | 900 | **765** |
| 6 | Frontier | 5 | 3,500 | **2,975** |
| 10 | Frontier | 9 | 6,300 | **5,355** |
| 15 | Dead lines | 14 | 15,400 | **13,090** |
| 20 | Deep | 19 | 32,300 | **27,455** |

## F.2 Car costs

Tuned so each car is **1.5–2 runs**, with enough left for one or two small upgrades.

Curve: `cost(N) ≈ 1,300 × 1.24^(N−4)`

| Car # | Cost | Runs to afford | Leftover |
|---|---|---|---|
| 4 | 1,300 | 1.7 | ~300 |
| 5 | 1,610 | 1.6 | ~350 |
| 6 | 2,000 | 1.5 | ~450 |
| 8 | 3,070 | 1.6 | ~700 |
| 10 | 4,720 | 1.7 | ~1,100 |
| 12 | 7,260 | 1.8 | ~1,800 |
| 15 | 13,830 | 1.7 | ~3,400 |
| 18 | 26,340 | 1.8 | ~5,600 |
| 20 | 40,470 | 1.9 | ~8,300 |

Leftover scrip grows in absolute terms but stays roughly proportional, so **the upgrade tempo stays constant** while the numbers inflate. That's what keeps late progression feeling like early progression.

## F.3 Upgrades

**Small — 15–25% of current car cost.** One or two per car purchase.

Lamp brightness · lamp armour · gun cooling · ammunition capacity · repair kit charges · tender capacity · coupling reinforcement · roof handrails (Dragger resistance) · radio range · car insulation (cold)

**Major — 100–180% of current car cost.** A run's income, deliberately competing with buying a car.

Armoured car conversion · second guard car (a **third gun**, but it costs a cargo slot) · improved brakes (+20% deceleration) · boiler upgrade (−15% burn) · powered switch thrower (removes the ground excursion at junctions) · reinforced couplings (uncouple under load)

**The core tension:** every upgrade slot competes with a car, and every car makes you richer and more vulnerable. A crew that buys guns and brakes instead of cars stays small, safe and poor.

## F.4 Progression shape

| Phase | Consist | Tier | Runs |
|---|---|---|---|
| Early | 3–6 | Local | 1–8 |
| Establishing | 6–10 | Frontier | 9–20 |
| Committed | 10–15 | Dead lines | 21–38 |
| Behemoth | 15–20 | Deep territory | 39–60 |

**Roughly 55–60 successful runs to reach 20 cars**, which at 35 minutes average is around 33 hours of successful play — considerably more with failures. That's a healthy campaign length for the genre and leaves room for post-launch route and enemy additions without inflating the ceiling.

---

# PART G — WHAT'S STILL OPEN

1. **Does the 4:1 speed ratio feel right?** Everything derives from it. If the train feels too fast or the player too slow, every table above shifts. **Test this first, before anything else is built.**
2. **Is 94s to traverse a 20-car train fun or tedious?** It's the single riskiest number here. If tedious, the answer is probably a roof-running mechanic rather than a shorter train.
3. **Is one Vigil per run enough?** Three feels generous given the cost. Might want a hard cap of two.
4. **Should the third gun exist at all?** It relieves the flank pressure that the whole difficulty curve depends on.
5. **Do POIs need a guaranteed manual fallback** when a facility is dead and the crew can't restart it?

---

*Dark Territory · Systems Spec v0.1 · September 2026*
