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

Strict ducking priority. Higher tiers duck everything below, except voice: nothing ducks tier 2 (*decided 2 Oct*).

| Tier | Content | Behaviour |
|---|---|---|
| **1** | Enemy telegraphs | Ducks tiers 3–6 −9dB (*changed 2 Oct*: not voice, so a crewmate calling out the tell is heard over it; as the implementation table below has it, "tier 1 bus ducks buses 3–6"). Never masked. Never occluded beyond −6dB. |
| **2** | Proximity voice | Ducks bed −6dB while active, and the ambient world with it (*changed 18 Oct, note 855*: on a roof at speed the wind is the loudest thing in the voice's band, and a crewmate talking from the cab's door was lost under it). Every microphone is levelled to one speech level before it's sent (note 855), so a quiet mic is heard as a loud one |
| **3** | Critical train state | Pressure alarm, brake fade, breach |
| **4** | Player actions | Footsteps, tools, shovel, gun |
| **5** | Train bed | The floor everything sits on |
| **6** | Ambient world | Wind, distant, weather |
| **7** | Music | *Added 1 Oct.* A low drone under the night. Every other tier ducks it; it ducks nothing, never voice |

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
| Track debris *(hazard, formerly the Sleepers; retired 6 Oct 2026, the director's decision, GDD §22: off unless a mod brings it back)* | Wet writhe, brief | 400Hz–2k | sleepers-writhe |
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
| ~~The Switchman~~ | *Cut: the director, 16 Oct 2026 ("I think we cut them"; note 852). Its tell was visual: the figure at the lever, the lamp wrong* | — | — |
| Grumbler | Gnawing on the crates | 1.4–2.2k | grumbler-gnaw |
| The Moose *(added 7 Oct, note 339; the sounds are the audio chat's to make)* | Warning: a cough-like grunt and teeth clacking; the charge: hooves on ballast; a ram: an iron boom through the car | grunt 150Hz–1.5k, clack 2–4k | tell-moose-* (note 334) |
| The Gannet *(added 7 Oct, note 340; the sounds are the audio chat's to make)* | The fold: a crack of wings, then a rising whistle of air for 1.6 s; overhead, harsh "arrah" calls that stop when it picks a walker; the bank: a long rising scream | whistle 2–5 kHz rising; calls 1–3 kHz | tell-gannet-* (note 384) |
| The Choir | Layered voices, multiplying as it gathers | 300Hz–4k, wide | choir-voice |
| Car fire *(App. C.5)* | Crackle and pop through the boards | 6–9k | car-fire |
| Marsh *(hazard, formerly the Drift)* | Dry reeds rustling, in slow creeping swells | 12–15k | drift-rustle |
| Low clearance *(the line's roof warning, a tunnel's mouth ahead; added 5 Oct, note 260)* | The telltales: a run of cord slaps over the roof | 4–7k | warn-low-clearance |
| Bend too fast *(the line's roof warning, riders up over a posted speed; added 5 Oct, note 260)* | The roof irons chattering, rocking in and out | 2.5–4.5k | warn-curve |
| Bend too fast, the cab *(tier 3, not a tell: a bend ahead or under the train that the speed now would derail it on; added 6 Oct, note 265)* | The communication bell over the driver, struck twice, every 1.5 s while it's up | 1.2k strike, partials 2.6k and 4.1k | warn-overspeed |

Overlapping bands (Draggers and Climbers, Tippy Toesie and the Stoker) are either never staged together or are told apart by
rhythm: the Dragger's scrape is one rasp, the Climbers' scrabble a clatter; the tiptoe ticks slowly, the hiss is continuous.
The two roof warnings sit in shared bands the same way: the telltales are a quick run of slaps, not the doll's giggle or the
fire's crackle; the roof irons' chatter rocks in and out, not one rasp or a clatter at a gap. They are heard by whoever is up
top (`sight.json` `roofWarning.roofOnly`), as the warning goes up and every 4 s while it's up.
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
| Jump height | 1.2m | Playtest (T90): the 0.48 m a flat 2.2 m gap needs felt like no jump at all (0.8 m then); the director, 8 Oct 2026: "our base jump is too weak" (note 473). A roof-run jump now carries ~3.4 m |
| Health | 100 | Most attacks 35–60. No regeneration. Damage model (GDD App. F.1, note 272): every creature hit is a big one, at least 30 (`enemies.json` `damage.minHit`; a hit 35, a heavy hit 60) and at least 2.5 s apart (`minGapSeconds`), so a creature on you is two or three hits from death. The train's own continuous dangers (fire, gas) and landings are not chip-limited |
| Healing | Bandages 30, medicine 50, morphine 80 | Rare finds (about one a frontier night), used from the hands: Use held 2 s standing, up to full health, the find gone (`loot.json` `healing`; note 272) |
| ~~Cold exposure~~ | ~~600s to onset, 1200s to death~~ | *Struck: the director's decision, 16 Oct 2026 (GDD §6 "The cold is the look, not a system"; ARCHITECTURE §8 note 852). The cold is no longer anything the player has to worry about: no exposure, no onset, no death by it, no deep-cold step on the line (GDD §22's row retired), nothing it takes off the boiler. The frost and the breath stay. (Was 600 / 1200 / 20, and before that 200 / 320 / 45: cold was 73% of deaths in the 100-night playtest.)* |

## B.3 Speed bands

The bands fall out of the 4:1 ratio, and they define what's possible at each speed.

| Band | Speed | Consequence |
|---|---|---|
| **Yard** | 2.5 m/s | **Below player run speed.** You can chase the train down and board it. This is what makes the decoupled-engine loading sequence work. |
| **Jump-off survivable** | <16.5 m/s | A knock on landing: 10 just above 1.5 m/s, rising to 80 at the lethal edge (T90 playtest) |
| **Jump-off lethal** | >16.5 m/s (3× run) | Death. Was 4.0 m/s, which made every step off a moving train fatal. Being *pulled* off (Draggers) still kills above 4.0 m/s |
| **Fall** | ≥5.5 m down | A knock by the height fallen: 10 at 5.5 m, rising to death at **7.5 m** (off a bridge's deck, 8 m to the river bank). Under 5.5 m (a car's roof and a jump's apex over the ballast) it's the speed rows alone; the worse of the two knocks counts (note 573) |
| **Off the ground** | — | Off open ground's own footing (over a dip, down a bank), or off a train standing still (under 0.5 m/s, `landing.standBelow`; the director's 14 Oct playtest, note 717), a crewmate's own run is no knock: only what's over the run speed counts (note 573) |
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
| 3 cars | 0.90 m/s² | 2.24 m/s² | 10s / 108m |
| 6 cars | 0.62 m/s² | 1.47 m/s² | 15s / 164m |
| 10 cars | 0.42 m/s² | 1.01 m/s² | 22s / 239m |
| 15 cars | 0.27 m/s² | 0.67 m/s² | 33s / 361m |
| **20 cars** | **0.18 m/s²** | **0.49 m/s²** | **45s / 494m** |

**T97 (playtest): the brakes were doubled from the first pass** ("braking needs to reduce speed significantly faster"); **T121 (playtest): then cut to 0.7 of that** ("brakes are maybe a bit too strong, lets reduce their efficiency by 30%"). The stop column is a stop with the steam off. With steam driving (B.6) the engine pulls against the brake until its pressure's down, so a stop on the brake alone takes longer, and the quickest stop is brake *and* vent: both in the cab since T109, a few steps apart (cab forward, note 276: the brake at the front windows, the vent on the left wall). At 20 cars you begin braking 500–600 m before a stop.

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

**The director's playtest of 16 Oct (line 18, note 843): the gauge moves 17% faster** ("build pressure a bit faster ... 15-20% faster"). The steam a unit of coal makes and the steam the train draws went up together, so the burn table below is unchanged, and a rebuild at a standstill (C.2) is about 15% quicker: 37 s to the working band at three cars, three and a half minutes at twenty. A boiler left unfed also falls 17% faster.

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
| Effective range | 220m (T121: the forward gun covers the Track Doll from the 200 m it shows in the lamp; cab forward since note 276, nothing of the engine masks the rail: the driver sees it from 8 m past the plough) |
| Traverse | 200°; **the engine's forward gun 360°** (the director's in-game notes, 9 Oct 2026; note 596): it turns the full circle, the short way round past its back, and the dead zone and the train's own body still keep it from firing back down the train |
| **Dead zone** | **20° each side along the train's own body** |
| Ammunition | 200 rounds/gun, resupply at POI |
| Choir aggro | +1.5 per round fired, decay 45s |
| **Foul** | **1 shot in 25 fouls the bore (4%), ×2.5 on wet rail.** The shot goes; the gun's out until it's cleared, and a pull on it is a dead click (GDD §23 "Cannon fouls") |
| Clearing a foul | 4s of Use held at the gun, standing still, by hand; let go and it starts over |

**The dead zone is what makes the flank uncoverable regardless of train length.** Guns face outward from the engine and guard car; the consist's own body is definitionally out of arc. This is a geometry fact, not a balance number, which means it can't be accidentally tuned away.

**A foul is a cascade, not a chore.** At 4% a shot, a gun that fires all 24 of its night's shot fouls at least once about three nights in five (1 − 0.96²⁴ = 62%), and a night's usual dozen shots or so foul one time in three; in the wet, far more often. It lands mid-fight, because the gun's only fired when something is there to shoot. Clearing it (4 s) is about a reload's time (3 × 1.5 s), under fire, and far less than mending the boiler (25 s). Which shot fouls is a hash of the tick and the gun, the same on every machine (combat.json, ARCHITECTURE §8 note 183).

## B.8 Run length

| Parameter | Value |
|---|---|
| Route length | **7 km, every tier** (the director's decision of 9 Oct 2026, note 554: shorter nights, an average run of about 17 min; was 24 km from 6 Oct, and 18–40 km by tier before that) |
| Transit at cruise | 8.3 min (was 28.6) |
| POIs per run | 3 (a stop is time: more of them would make a deeper night longer) |
| Time per POI | about 2 min for a crew that works it well |
| **Total run** | **about 17 min on average** (the director's marker, not a limit: three stops made in good time) |
| Dawn timer | Route length ÷ 11 m/s average, +40% slack (14.9 min), + 3 min for each facility on the night (`dawnPerFacilitySeconds`, 180): **23.9 min, every night** ("Dawn in 24 minutes"; the slack was +18%: after the 100-night playtest a stop, the posted boards and the in-car trouble didn't fit, and missing dawn was the commonest failure) |

The dawn budget assumes an 11 m/s average, below the 14 m/s cruise. **The slack is what you spend on stopping** — every POI, every repair, every revival eats it — and each facility adds its own three minutes on top (note 554: at 7 km the transit's slack alone, 4.2 min, left no time for a stop). A crew that makes all three stops in good time is home with minutes to spare; a slow one isn't.

**At dawn the Wakers get up** (GDD §8; docs/design/creatures/wakers.md; note 588; enemies.json `wakers`), in place of v1's two minutes' grace and a failed night. A train still out has them behind it: 1 on the Local and Frontier tiers, 2 on the Dead Lines, 3 in the Deep Territory. The first rises 1.2 km behind the last car. After 8 s it runs 6 m/s, building to 26 m/s over 45 s (the engine's flat out is 22). A train standing at dawn is caught in about 75 s; one at cruise is caught from about 2.5 km out, so a crew that close races it in. They stop 300 m short of the terminus's gate. The one that reaches the last car drags the train to a stand (3 × the engine's pull) and lifts a car every 2.5 s from the rear. From 6 s in it eats one every 4 s, whoever's in it with it, and anyone off the train goes 8 s in. The engine eaten, the night is lost: "taken at dawn" (`RunEnd.DawnMissed`).

**One night length** (the director, 6 Oct 2026): "The time a run takes should always be the same. Difficulty scales not by time but by monsters and density of challenges." On 9 Oct 2026 the one length became 7 km ("Sessions are feeling very long ... the average run has 3 full stops it can make", then "let me increase the time of a run up to 17 minutes ... that time is just an average run marker"; and after playing it, "yes make the shorter night final"). The total run's floor is the transit at cruise with no stop made (8.3 min); its ceiling is the dawn. A tier no longer adds kilometres; it packs more into the same 7:

| Tier | Facilities | Junctions | Washouts, weak bridges, brass | Terrain budget per km | Terrain laid per night (measured) | Director's base budget per km |
|---|---|---|---|---|---|---|
| Local | 3 | 0–1 | 0 | 0.6 | 3.3 | 12.9 |
| Frontier | 3 | 1–2 | 0–1 each | 1.0 | 4.4 | 21.4 |
| Dead lines | 3 | 1–2 | 0–1 each | 1.5 | 5.9 | 32.9 |
| Deep territory | 3 | 1–2 | 1 washout, 0–1 weak bridge and brass | 2.1–2.6 | 6.3 | 47.1 |

Counts are each tier's start, rising with a route's severity toward the next tier's (`content/linegen/tiers.json`); terrain laid is `dt linegen sweep`'s mean terrain cost at 10 cars; the director's budget is App. B.1's per night (`enemies.json` `baseBudget`: 90 / 150 / 230 / 330) over the one length, × the train and crew multipliers. The counts were set for 24 km lines (and the deeper tiers' for 29–40 km before that); on 7 km they are a 24 km night's per kilometre where that's a whole number (note 554: the facility slots from 1.25 km past the gate, 1.1 km apart and 1.7 km before the end; one or two hard bends; at most one alternate, dead line, washout, weak bridge, momentum bank and brass field; the tag quotas, the tunnel quota and the dead settlement by a facility dropped). The three stops take about half of a 7 km line, so the set pieces, their recoveries and the threshold, home straight and spike window are cut to fit what's left (content/linegen, note 554): the land laid per kilometre is near a 24 km night's again, rising with the tier (above, `dt linegen sweep --cars 10 --seeds 20`, 11 Oct 2026; it was 2.2 / 3.2 / 2.8 / 2.8 with the 24 km lengths, which left the 7 km lines flat). Quiet stretches are counted in kilometres of line, with time as the backstop for a stopped train (GDD App. F.1): the director's quiet pressure ramps over 1.26 km run since a threat was engaged (the old 90 s at cruise), or over 120 s if that's sooner (`enemies.json` `quietRampMetres`, `quietBackstopSeconds`; ARCHITECTURE §8 note 270).

## B.9 Breaches

A breach is a car's shell giving way to the outside (decided 1 Oct): a door forced, a hatch torn off, the Car Hugger chewing through the end wall, Climbers getting into an unlit car. Until it is boarded up, the car shuts nobody in, whatever its doors. The cold, the night's sound, voices and the Choir come in as through an open door, so it is no longer "behind a closed door" for the Choir. The change in the train's sound is the alarm: there is no klaxon.

| Parameter | Value |
|---|---|
| Car Hugger through the end wall | Every 0.1 of the shell eaten (25 s of feeding at 0.004/s), boarded up or not |
| Climbers forcing their way in | Into a car with every door and its hatch shut, and unlit, through the roof (the hatch, on a cargo car) |
| Boarding up | 8 s of Use held inside the car within 1.5 m of the hole; let go and that board starts over |
| Two boarding up | Each works it 1.8× as fast, 4.4 s; a third adds nothing (train.json `repair.pairRate`; GDD §12 Fluidity, note 752) |
| Needs the repair kit | No: anyone's hands (train.json `breach.needsKit`, true to need the kit carried) |
| Inside a breached car | No muffling, no shelter from the Choir (and no shelter from the cold, while there was one: B.2) |

## B.10 Director pacing

*Design decision, 2026-10* (GDD App. B.1 "Pressure", "Pacing rules"; `director` in `content/tuning/enemies.json`). The budget and its curve, the caps and the gates are GDD App. B.1's; these say when the director spends. The night's curve and the free spawns on it (the director, 16 Oct 2026; note 849) are the rows marked so; `NightCurveTests` pins them. The stops' own (note 860) is its row; `StopsOwnTests` pins it.

| Parameter | Value |
|---|---|
| **Grace period** | **20–90 s**, per night from its seed; the draw above 20 s × 1 Local, 0.85 Frontier, 0.7 Dead Lines, 0.55 Deep Territory |
| Pressure at the end of the grace | 4 |
| **Threshold** (spends) | **10** |
| Pressed (cooldown gives way, curve overdrawn by up to 3) | 16 |
| Most banked | 18 |
| **Relief per spawn** | **3 × its cost** |
| Base | 0.03 /s |
| **Escalation** (on everything) | × (1 + 3 × the night's curve): the curve is progress along the line or toward dawn, whichever is further, to the power **1** (`director.curve.power`; note 849) |
| Quiet | + 0.1 /s × (seconds since a threat was engaged or sent ÷ 90, to 1) |
| Loudness | + 0.1 /s × (Choir meter ÷ its threshold, to 1.5) |
| Cargo | + 0.01 /s per car-load (× comet 2, livestock 1.5, food and medicine 1.3, ammunition 1.2) |
| Tier | × 0.8 Local, 1 Frontier, 1.2 Dead Lines, 1.4 Deep Territory |
| Conditions | × (1 + 0.15 lamp out + 0.05 rain + 0.05 × wind). The cold's terms (0.1 × cold + 0.05 per deep-cold step) went with the cold (B.2, note 852) |
| **Relief valve** | × (alive ÷ crew)² × (1 − 0.25 × share of the living under 35 health) |
| Busy | × 1 ÷ (1 + 0.5 × threats engaged × (1 − progress)): engaged is telegraphing, committing, grabbing or punishing; late in the night it stops waiting for the crew |
| **Free spawns** (GDD App. B.1 "The night's curve"; note 849) | Beside the pressure, outside its budget, cooldown and holds: **3 an hour at the gate rising to 18 at the end** along the curve, rolled a second at a time on their own dice, the whole of it for a crew of **2** or fewer and **0.25** less for each crewmate alive over that, never under **0.35** (`crewFade`, `crewFadeFrom`, `crewFloor`); never within **20 s** of anything the director sent, never before the night's first threat (drawn); nothing off the pressure. Under the hard caps, each kind's `nightChance` and `perNight`, the variety rules, who's next and the votes |
| Free spawn: the fill (`curve.fill`) | The pressure **pressed** (at 16, `fillPressed`) with **no rule met** spends on the rule-less (the list below) instead of nothing: charged, paced and relieving as any spend |
| Free spawn: what comes | One draw among the rule-less, sent without their situational rule, by weight: Cinder Hounds 0.5 (the train moving), Climbers 1, Ribbits 1.5, Followers 1, Soot Children 1, the Moose 0.7 (`curve.kinds`); and every kind whose rule is met now at its rule's weight × **1** × the curve (`curve.bound`); a Dragger or Whistler (silent until met) only with nothing else to send |
| **The stops' own** (GDD App. B.1 "The stops' own"; note 860) | As the train stands at a yard (a facility with a stop laid) or an abandoned village (a village stop, stood on the main line by it): **1** of the kinds whose rule the stop meets put in place (`waiting`) and **1** of the boarders (`boarding`: the Whistler, Draggers, Tippy Toesie, the Passenger, Climbers) whose rule it meets, one a second, for the first **240 s** stood there (`trySeconds`); by rule weight × `weights` × the night's variety, under the roster, `nightChance`, `perNight`, slain and the engaged cap. Once a stop a night. Beside the pressure: outside its budget, threshold, cooldown and the facility lull; the grace, the forts, the final stretch and the spawn bans hold; nothing off the pressure; its own dice |
| Post-spawn cooldown | 25–45 s (gives way when pressed) |
| Facility lull (note 751) | 20 s from the first second stood at a facility: nothing of the director's own |
| Aftermath (note 751) | 12 s after a death, cars lost (told), a grab broken with the one held alive, a Holdout freed: nothing of the director's own, pressed or not |
| **The first threat's draw** (note 287) | Each crewmate's draws on a ledger halving every 30 s: whistle 1.5 /s, raised voice 0.5 /s at full (above 40% of the mic), noisy toy 0.15 /s, firebox 0.25 /s at full (above 4.5 of 6), engine 0.03 /s at 20 m/s; a cannon round 3, a car lamp lit 2, cargo aboard 4 a car-load × its value |
| Answered | One crewmate's draw of one kind at **3**, after the grace: a call from 60 m ahead, 12 m off the line, eyes there for 7 s; the first threat **5 s** later, pressed for |
| Listening | No draw that big: past the threshold the first threat waits up to pressure 16, then the biggest draw standing takes it |

Early in a night the pressure takes about a minute after a spawn to reach the threshold again; near the end about twenty seconds, and the cooldown sets the pace. In the harness (8 bots, 1,800 s, ARCHITECTURE §8 note 266) that is 4–5 of the director's spawns in the first five minutes against 11–12 in the last, and no quiet over about 30 s.

## B.11 Fire grid

A car fire burns on cells (decided 6 Oct, GDD App. F.1; ARCHITECTURE note 267). Each car's floor, side walls and roof are cut into cells of about 1.5 m, never mid-air; the end walls aren't cells, and a fire's way out of a car is through its ends. Each cell has its own heat (0 to 1) and its own fuel, and a cell that burns chars for the rest of the night. The extinguisher puts out the cell you aim at. The numbers live in enemies.json `carFire`.

| Parameter | Value |
|---|---|
| Cell size | 3.5 m (`cellSize`): 16 cells in a cargo car, 4 along, the floor, the roof and each wall one across (the director, the inbox, 12 Oct: "make it 15"; it was 1.5 m, 72 cells; note 634) |
| A cell's growth | `growPerSecond` 0.006 + `growWithSize` 0.02 × its heat; the floor's cells × the cargo's growth (powder 1.6, chemicals 1.3, coal and timber 1.4) |
| Catching | A cell at 0.5 or more heats each cell sharing an edge at 0.04 × its heat a second, ×2 upward (fire climbs) |
| Burning out | A cell burns 0.01 × its heat of its fuel a second, charring as it goes; burnt out, it dies down at 0.05/s. Embers below 0.05 go out unless a neighbour is heating them |
| Extinguisher | 3.5 m from the eye along the look; the stretch of the car where it's aimed, its floor, walls and roof there, cools 1/s a cell (`sprayWorksAcross`, `sprayPerSecond`; the director, 13 Oct: "one grid ahead of me ... hold. And it should dissipate within five seconds"; 12 Oct: "extinguish far more of the fire"), the cells round it 40% of that, and it stays wet (won't catch) for 3 s; 15 s of charge |
| Alight | When any cell reaches 0.35 (the commit, after the smoke) |
| The car's fire | The mean of its cells: the explosion (powder car at 0.6), the cargo and car damage, the sound |
| Jumping the coupling | A cell against an end wall at 0.8 for 15 s (chemicals ×2, coal and timber ×2 as fast), into the next car's near end |
| Burns | Within 1.6 m of a burning cell's patch (feet to 1.8 m up), its heat × the falloff; the roof at half |
| Left alone (goods) | At 1.5 m cells: alight at 25 s, the roof caught by about 55 s, half the car by 70 s, the next car by 85 s; burnt out by about 6 min. Not yet measured again at 3.5 m (note 634) |
| One extinguisher | Puts out a car at full blaze in about 8.6 s, nearly three-quarters of its charge left, the one spraying it unburned (the director, 12 Oct: "from full blaze within 20 seconds"; `FireCellsTests`) |
| Two extinguishers | Two in the car, each spraying the nearest fire in reach, put the same car out in about 6.4 s (three in 5.5 s): each cools the stretch it's aimed at by the whole rate, so they stack (note 752, GDD §12; note 851, the director's playtest of 16 Oct evening: "didn't go out ever despite both of us fighting it"); both unburned, most of their charge left (`FireCellsTests.TwoOnTheExtinguishersPutARearCarFireOutWellWithinTheOnesTime`) |
| Every fire has a cause | Told to the crew as it starts, where it starts, and in the report: the hot box, the Fire Flies at the car's lamp, Cinder Hounds feeding on it, a thrown lamp (and who threw it), the fire jumping the coupling from the next car, a powder car going up (`FireCause`, `World.FireStarted`; the news line, note 663; note 851) |
| Home | In through the arrival's gates (note 847) or delivered, nothing on the train burns: a fire goes out at once, no hound lights one, no hot box catches, no powder car goes up (`World.HomeSafe`; note 851). Never in the gym (note 676), and not the departure's yard |
| A hound over the crew | A Cinder Hound on the roof of a car with a crewmate inside it (out of its reach, note 471) is not alone: it doesn't feed and relight the car over their heads; it prowls and waits for them to come out (note 851) |

---

## B.12 The Moose

*The director's decisions, 2026-10-07* (GDD §21, App. A.6, B.6; `moose` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 339). `SpecTableTests.TheMooseMatchesB12` pins this table.

| Parameter | Value |
|---|---|
| **Aggro** | 0–100; listening at **20**, warning at **50**, charging at **100** |
| Crowding (on the ground, in its sight) | + 25 /s within **20 m**; + 70 /s within **12 m** |
| Voices | + 40 /s × (voice ÷ 255) within **15 m**, over a floor of 40 (a whisper is free) |
| A train going by | + 25, once a pass, within 25 m |
| A hit | fills it; whoever hit it is its target from then on |
| Calming | − 15 /s with nobody crowding or talking |
| **Square-up** | **2.5 s** within 30 m, its heading locked at the end |
| **Charge** | **11 m/s** (twice a player's run), overruns 8 m, wheels round in 2.5 s |
| Rack | **3.2 m** across: nothing narrower lets it in |
| Snagged (charged into a building) | 4 s |
| **A charge** | a hit of **60**; on someone at **40** or less, a pin of **12 s**, broken by a friend's blow |
| Search | 25 s at 2.5 m/s where it lost them; a voice within 15 m gives anyone away |
| Leash | 80 m from its ground |
| Ram (lost at a car) | every 3 s for 15 s; nothing in or on the car is touched |
| **Track clearance** | never within **3.2 m** of a track's centre; 6 m while the train moves and where it's put down |
| Spawn weight by tier | 1 · 1.5 · 2 · 2.5 (Local to Deep Territory) |
| Beside the line | 2 · 3 · 4 · 5 per 10 km by tier, × the biome's weight |
| Health | none: nothing kills it |

## B.13 The Gannet

*The director's decisions, 2026-10-07* (GDD §21, App. A.4, B.4; `gannet` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 340). `SpecTableTests.TheGannetMatchesB13` pins this table.

| Parameter | Value |
|---|---|
| **Comes** | the train at **18 m/s** or more for **30 s**, out of a tunnel |
| Peels off | under **12 m/s** for 6 s, in a tunnel, or 60 s with nobody walking the roofs; back after 180 s |
| Soars | 20–35 m over a car |
| Prey | a walker on a roof, over 0.8 m/s on it, not seated |
| **Hang, fold** | hangs **2 s**, then folds: **1.6 s** to the strike, its line locked on where they'll be |
| Strike | within **0.9 m** of its line's end: a stab of **35**; nobody there, on the roof: stuck **4 s** |
| A pass | every 8–12 s |
| **Mark** | whoever hit it; its next pass is a **2.5 s** bank, then down on them (a roof, or the ground within 200 m) |
| **Pin** | **4 pecks, 3 s apart** (12 s); **3 blows** from friends drive it off |
| Health | **12** blows; under 4 it leaves for the run |
| Trophy | its head: **1.5** of the tier's car-loads (loot.json `trophies`) |
| Spawn weight by tier | 1 · 1.5 · 2 · 2.5 (Local to Deep Territory) |

## B.14 The Mourners

*The director's brief, 2026-10-08* (GDD §21, App. A.6, B.6; `mourners` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 362). `SpecTableTests.TheMournersMatchB14` pins this table.

| Parameter | Value |
|---|---|
| **Come** | **15 s** after a body's left lying off the train; **3 · 4 · 5 · 6** of them by tier, 40 m off it, away from the line |
| Shy | a crewmate within **6 m** starts one back to 12 m; back in 4 s |
| **Take** | nobody within 6 m of the body: two take it and drag it straight off the line at **1.6 m/s** |
| Drop | a crewmate within **3 m** of the body, or a blow on any of them; the rest scatter **6 s** |
| Health | **1** blow each |
| **Gone** | hauled **120 m** from the track: the body, its refund and its kit |

## B.15 Tower Jaw

*The director's brief, 2026-10-08* (GDD §21, App. A.6, B.6; `towerJaw` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 363). `SpecTableTests.TowerJawMatchesB15` pins this table.

| Parameter | Value |
|---|---|
| **Gnaws through** | **150 · 120 · 100 · 90 s** by tier; already 20% through when the train comes |
| Threat, bite | a crewmate within **10 m**: it rears (1.5 s); within **3 m** a bite of **35** (never a kill) |
| **Driven off** | **4 blows in 15 s**: away 120 s, its gnawing kept; **12** blows kill it |
| **Down** | crushes within 3.5 m (**45**); the coaling tower's chute empty; its wreck closes the line ±3 m of the spout |
| **Cleared** | **30** crew-seconds of Use held within 4 m |

## B.16 The Brakeman

*The director's brief, 2026-10-08* (GDD §21, App. A.8, B.8; `brakeman` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 364). `SpecTableTests.TheBrakemanMatchesB16` pins this table.

| Parameter | Value |
|---|---|
| **Comes** | never with fewer than **2** alive; a train of 4 cars or more over 3 m/s |
| Works | **2.5 m/s** along the roofs, **4 s** a wheel; a wound car's handbrake drags (train.json `handbrakeDecel` of its mass) |
| **Runs** | from a crewmate on the roofs within **12 m**, at **5 m/s** (a roof run is 3.5); lost **20 m** clear: hides **30–60 s** |
| **Cornered** | crewmates within **10 m** on both sides: blows land (**4**); he lashes the nearer every 2.5 s for **30** |
| Spawn weight by tier | 1 · 1.5 · 2 · 2.5; × cars ÷ 6; ×2 with a 2% climb within 1 km |

## B.17 The Knotter

*The director's brief, 2026-10-08* (GDD §21, App. A.7, B.7; `knotter` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 365). `SpecTableTests.TheKnotterMatchesB17` pins this table.

| Parameter | Value |
|---|---|
| **Comes** | a train of 3 or more at **8 m/s** or more; **4 s** of creaking before it clamps |
| **The gap** | forced to **5 m** over 6 s (a roof jump carries about 2.8) |
| **Slip** | **45%** a second on its back over 2 m/s; its coil **1.5 s** for a friend to pull them up |
| Health | **8** blows, only at a stand |

## B.18 The Freight Beetle

*The director's brief, 2026-10-08* (GDD §21, App. A.6, B.6; `freightBeetle` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 366). `SpecTableTests.TheFreightBeetleMatchesB18` pins this table.

| Parameter | Value |
|---|---|
| **Notices** | a player within **25 m** |
| **Pushes** | directly away from the nearest player at **1.2 m/s** (a heavy crate 0.8), turning **90°/s** |
| Startled | a player within 1.5 m of its head: 2 s |
| Health | **6** blows; **3 in 10 s** drive it off its load for 30 s |

## B.19 Hotbox

*The director's brief, 2026-10-08* (GDD §21, App. A.7, B.7; `hotbox` in `content/tuning/enemies.json`; ARCHITECTURE §8 note 367). `SpecTableTests.HotboxMatchesB19` pins this table.

| Parameter | Value |
|---|---|
| **Boards** | the train at **10 m/s** or more |
| Heat | **90 s** knocking, then **90 s** glowing, at 18 m/s (in proportion to the speed) |
| **Seized** | the train held to **7 m/s** while the car's in it; repaired with a wrench, **10 s** |
| **Exposed** | at a stand for **2 s**: **6** blows kill it, or a crowbar or wrench held **4 s** prises it out; it bites **30** every 3 s |

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
- ~~Turret traverse dead (no boiler pressure).~~ The guns are their own, firing and reloading whatever the boiler's pressure, with endless shot (the director, 13 Oct 2026, note 689; combat.json `minPressure` 0, `infiniteAmmo`). The vent's noise is still theirs to answer.
- **The vent is deafening.** Choir aggro spikes to maximum instantly, and every noise-triggered spawn weight doubles for the duration.
- Dawn clock keeps running.

**After the Vigil:** boiler pressure at zero. Rebuilding to working band takes 40s at three cars and **over three minutes at twenty.** Then you still have to accelerate a 890-tonne train from a standing start.

### The revived

They come back weak: carry capacity reduced to light only, and they cannot operate the guns until the next POI. ~~Cold exposure onset halved (300s)~~ *struck with the cold (B.2, note 852)*.

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

Contracts pay on **delivered cargo that survives**. Nothing else. A contract is the route and its tier (note 637: "Just a route", the director, the inbox, 12 Oct); the cargo is whatever the night's stops load, each car paid at this table's value by its freight's rate (run.json `economy.cargoRates`).

| Tier | Value per delivered car |
|---|---|
| Local | 450 |
| Frontier | 900 |
| Dead lines | 5,500 |
| Deep territory | 23,000 |

*Note 636 (the director, the inbox, 12 Oct: "each successful run should help players towards earning and spending to upgrade so they can go further the next run when the difficulty goes up").* The train leaves empty (note 575) and a night earns what its one stop loads, and the harder yards load less: 2.7 car-loads on Local, 3.0 on Frontier, 1.5 on Dead lines, 0.9 in Deep territory (64 nights of four bots). So each tier's car-load is priced for a delivered night to net about **the tier's next car ÷ 1.5** (F.2): about 1,100 on Local, 2,650 on Frontier, 7,800 on Dead lines and 20,000 in Deep territory. (Was 450 / 700 / 1,100 / 1,700 a car, priced for full trains.)

**Running costs** — coal, ammunition, repairs — are fixed sums, not a share: they came to 15% of gross at the old pay, and with the deaths' fees and less the mail they come to about **5% of gross** now.
**Fees don't follow the pay.** A death's crew-loss fee and a creature's monster brigade fee are halves of the tier's **fee value** (450 / 700 / 1,100 / 1,700, run.json `economy.feeValue`), so a raise in the pay isn't a raise in what a death costs (GDD App. D).
**Expected loss** is already out of the car-loads above: they're what came home.

### Net income by consist

| Consist | Tier | Car-loads home | Gross | Net |
|---|---|---|---|---|
| 3 | Local | 2 | 900 | **855** |
| 6 | Frontier | 3.0 | 2,690 | **2,556** |
| 10 | Frontier | 3.0 | 2,690 | **2,556** |
| 15 | Dead lines | 1.5 | 8,305 | **7,890** |
| 20 | Deep | 0.9 | 20,470 | **19,447** |

A longer train doesn't earn more on the same tier: it carries home what the stop loads. It earns more by going further (F.4).

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

Lamp brightness · lamp armour · gun cooling · ammunition capacity · repair kit charges · tender capacity · coupling reinforcement · roof handrails (Dragger resistance) · radio range · ~~car insulation (cold)~~ *(retired with the cold, note 852)*

**Major — 100–180% of current car cost.** A run's income, deliberately competing with buying a car.

Armoured car conversion · second guard car (a **third gun**, but it costs a cargo slot) · improved brakes (+20% deceleration) · boiler upgrade (−15% burn) · powered switch thrower (removes the ground excursion at junctions) · reinforced couplings (uncouple under load)

**The core tension:** every upgrade slot competes with a car, and every car makes you richer and more vulnerable. A crew that buys guns and brakes instead of cars stays small, safe and poor.

**The catalogue** *(the director, 13 Oct 2026; note 678, docs/design/upgrades.md)*. Upgrades come in four kinds: **tools**
(the above, and more priced in the same bands), **whimsy** (works and is funny: 1–15%), **fun** (looks and sounds, no
gameplay: 2–8%) and **silly** (bought because it's silly; any price, the gold whistle's 250% being its joke, and the yard
clerk reads it into every tally). A tool is stocked from the tier whose trouble it answers (campaign.json `fromTier`), and
takes a share of that trouble, never all of it:

Frontier: speed recorder (+50% bend warning) · composition brake shoes (−50% fade) · pre-cut boards (−50% boarding a
breach) · big extinguishers (+50% spray, −50% recharge). Dead lines: axle oilers (Hotbox +50% before seizing, −50% to
mend) · ~~feedwater heater (−50% cold off the boiler)~~ *(retired with the cold, note 852)* · gun rammers (−20% reload step) · trailing-truck booster (major, +20%
pull). From Local: sprung cradles (−60% fragile spoilage) · spare radios (+2).

## F.4 Progression shape

| Phase | Consist | Tier | Runs |
|---|---|---|---|
| Early | 3–6 | Local | 1–9 |
| Establishing | 6–10 | Frontier | 9–21 |
| Committed | 10–15 | Dead lines | 21–40 |
| Behemoth | 15–20 | Deep territory | 40–56 |

*Note 636: `dt campaign sim`'s standard crew on F.1's nets (campaign.json `standardCrew`) reaches 6 cars on run 9, 10 on 21, 15 on 40 and 20 on 56.*

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
