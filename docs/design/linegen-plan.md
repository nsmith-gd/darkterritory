# DARK TERRITORY — PROCEDURAL LINE & TERRAIN GENERATION PLAN
## Companion to GDD v1.0 and Systems Spec v0.1 · Implementation handoff for Claude Code · September 2026

This document specifies the generator that builds every run's railway: the departure fortress and threshold, the wilderness line, junctions and branches, facility stations, abandoned halts, the terminus, and the terrain, structures, dressing and paperwork around all of it.

**How to use this document (Claude Code):**

- Read `dark-territory-gdd.md` and `dark-territory-systems-spec.md` first. Authority order for *design* questions is GDD → Systems Spec → this plan. This plan is authoritative for generator *architecture*.
- Every number here is first pass. Put all of them in data files (see §19), never in code.
- §22 lists conflicts found between the source documents. Do not resolve them silently: implement the config default given there, log a warning at generation time, and leave the value tunable.
- Build in milestone order (§21). Each milestone has acceptance tests; do not start the next until they pass.

Units throughout: metres, m/s, grade in percent, chainage `s` in metres along a track edge.

---

# 1. Goals and non-goals

**Goals**

1. Every run gets a unique line that is **harder the further the crew travels from civilization**, both across the campaign and within a single run.
2. Difficulty comes from the GDD's hazard list only: grades, curves, fog, wet rail, cold, wind, washouts, weak bridges, tunnels, brass growth, dawn. **Terrain removes tools; it never changes enemy rules.**
3. The line is **fair**: a crew that obeys the boards and the paperwork cannot be killed by track geometry. What kills them is breaking that authority, or an enemy that telegraphed.
4. The line **serves the pressure director**: it provides the terrain contexts the spawn rules require (marsh for The Weight, long straights for The Ferryman, junctions for The Switchman, and so on).
5. The line is **callable on voice**: every place a crew might need to name has a number or a name.
6. Deterministic, streamable, network-safe, and cheap to render at the late-PS2/early-PS3 target.

**Non-goals**

- No free-roaming open world. The world exists only as a corridor around track.
- The internal layout of facilities belongs to the POI generator (Systems Spec Part D). This generator reserves the land, the junction and the approach, and hands off.
- Fortress interiors are template-assembled, not procedurally designed.

---

# 2. Core principles

**2.1 Track first, terrain second.** The generator produces the railway's alignment and profile first, then shapes the land around it (cuttings, embankments, ledges, ravines). Terrain is never generated first and routed through. This is how real railways are built, and it is the only way to control grades, curve radii and therefore difficulty.

**2.2 Difficulty runs on three axes.**

| Axis | Scale | Driven by |
|---|---|---|
| **Campaign depth** | Across runs | Route tier and route severity, combined into one scalar `D` (§3.1) |
| **Run position** | Within one run | A budget curve over chainage: gentle past the gate, rising to a spike before the terminus (§3.3) |
| **Consist** | Per run | The departing train's car count. Physics does the gating: a grade that is trivial at 3 cars is impossible at 20 (§3.4) |

**2.3 Every demand has a tell.** This mirrors the enemy fairness contract (TELEGRAPH always precedes COMMIT). Any piece of track that demands a speed change or a stop must be communicated far enough ahead for the actual consist to comply (§9).

**2.4 Stacking, not novelty, makes it hard.** Deep territory uses mostly the same pieces as Local routes. What changes is how many demands overlap, how little margin is left, and how few redundant cues remain.

**2.5 The world is generated for this train.** The line is generated after the crew has chosen contracts and consist at the fortress. Boards, ruling grades, bridge limits and coaling stops are tailored to the train that is leaving.

**2.6 Everything nameable.** Kilometre posts, junction numbers, named tunnels, bridges, halts and facilities. If the crew can't say where something is, the art is failing the design (GDD §32).

---

# 3. Difficulty model

## 3.1 The difficulty scalar `D`

`D = tierIndex + severity`, where tierIndex is 0 Local, 1 Frontier, 2 Dead lines, 3 Deep territory, and `severity ∈ [0,1)` is a property of the specific route on the campaign map (shown to the crew as route length and "known grades" on the route card). `D` therefore runs from 0 to just under 4.

All generator limits below are given at the start of each tier and **linearly interpolated by severity toward the next column**. Deep territory interpolates toward the "Deep max" column.

## 3.2 Tier table

| Parameter | Local | Frontier | Dead lines | Deep | Deep max |
|---|---|---|---|---|---|
| Main line length (km) | 18 | 23 | 29 | 34 | 40 (see §22.1) |
| Facility slots | 3 | 3 | 4 | 4 | 5 |
| Main-line grade cap (%) ¹ | 1.5 | 2.0 | 2.5 | 3.0 | 3.5 |
| Momentum grade cap (%) | — | 3.0 | 4.0 | 5.0 | 6.0 |
| Branch grade cap (%) | 2.0 | 3.0 | 4.0 | 5.0 | 6.0 |
| Min horizontal radius (m) | 450 | 300 | 220 | 170 | 150 |
| Min vertical curve radius (m) | 4000 | 3000 | 2500 | 2000 | 1500 |
| Max tunnel length (m) | 300 | 700 | 1200 | 1800 | 2200 |
| Max grade inside tunnels (%) | 0.5 | 1.0 | 2.0 | ruling | ruling |
| Curved tunnels | no | no | yes | yes | yes |
| Stack depth cap | 1 | 2 | 2 (3 once) | 3 | 3 |
| Recovery straight min (m) | 1500 | 1000 | 600 | 400 | 300 |
| Tell margin (× warning distance) | 1.5 | 1.3 | 1.2 | 1.1 | 1.1 |
| Default line speed (m/s) | 20 | 18 | 16 | 14 | 14 |
| Required-tell redundancy | 3 | 2 | 2 | 1 | 1 |
| Non-required signage survival | 100% | 85% | 55% | 30% | 20% |
| Main-line junctions ² | 2–3 | 3–5 | 5–7 | 6–9 | 8–10 |
| Alternate routes (loops) | 0–1 | 1–2 | 2 | 2–3 | 3 |
| Dead-line branches | 0 | 0–1 | 1–2 | 2–3 | 3–4 |
| Washouts | 0 | 0–1 | 1 | 1–2 | 2 |
| Weak bridges | 0 | 0–1 | 1 | 1–2 | 2 |
| Momentum banks | 0 | 0–1 | 1 | 1–2 | 2 |
| Brass fields | 0 | 1 | 2–3 | 3–4 | 4–6 |
| Terrain budget (units/km) | 0.6 | 1.0 | 1.5 | 2.1 | 2.6 |
| Corruption level | 0.1 | 0.35 | 0.6 | 0.85 | 1.0 |
| Fog distance range (m) | 110–120 | 90–120 | 75–110 | 60–100 | 60–90 |

¹ The main-line grade cap is always further limited by the consist: `g_main = min(tierCap, 0.85 × climbMax(N_plan))` (§3.4).

² Facing junctions to alternates and dead lines. Facility junctions are counted separately.

**Demo configuration:** the December demo ships Local and Frontier only, with washouts and weak bridges forced to 0 (they are full-scope track kit in the art plan). This is a config switch, not a code branch.

## 3.3 Run-position budget curve

Each run has a terrain budget `B = budgetPerKm(D) × mainLineLengthKm`. It is spent over chainage against a curve that mirrors the director's pressure shape (GDD B.1), so terrain and enemies build together:

| Zone | Chainage | Share of `B` | Rules |
|---|---|---|---|
| Threshold and grace | 0 → 2 km | 0% | Straight or R ≥ 1000 m, grade ≤ 0.5%. No Sleeper or Grease candidates. Covers the GDD 90 s grace period. |
| Opening | 2 km → first facility | 15% | Single features only, no stacks |
| Middle | first → last facility | 45% | Stacks allowed up to the tier cap |
| Final approach | last facility → terminus home straight | 40% | Contains the run's terminal spike (§11.4) |
| Home straight | last 1.5 km | 0% | Level, straight or R ≥ 1000 m. Director spawn ban applies to the final 500 m. |
| Facility lulls | 1 km before each facility junction, 0.5 km after rejoin | 0% | Level approach, holding track (§11.1) |

The final approach carries a disproportionate share on purpose: it is the stretch where the crew is carrying the most cargo and the most damage.

## 3.4 Consist coupling

The generator receives the departing consist and plans for its **worst case**: every car loaded (`N_plan` cars at 40 t, plus the 90 t engine). Losing cars only makes the train lighter, so the worst case is the departure consist fully loaded.

From Systems Spec B.5, `climbMax(N)` is interpolated from: 3 cars 9.2%, 10 cars 4.3%, 15 cars 2.8%, 20 cars 1.8%. Braking deceleration, acceleration and brake fade come from the shared train simulation module, never from copies of the table. The validator (§16) drives the real sim.

Consequences, all automatic:

- A 20-car train gets a main-line ruling grade of at most 1.53%. Steeper ground must be tunnelled, taken on a momentum bank, or put on an optional branch.
- Weak bridge limits and speed boards are computed for this train.
- Coaling stops are guaranteed when the expected run time approaches tender endurance (§11.1).

---

# 4. Pipeline overview

```
 RUN PARAMETERS  (seed, D, route, consist, contracts, crew size, weather profile)
        │
 1  ROUTE GRAPH          main line, facility slots, alternates, dead lines, junctions
        │
 2  LEG SCRIPTING        set pieces + connectors per edge, spent against the budget curve
        │
 3  ALIGNMENT & PROFILE  horizontal clothoid/arc/tangent + vertical grades/curves; closure
        │
 4  SPEED AUTHORITY      curve limits, restricted zones, tells, signage, Form 19 route card
        │
 5  STATIONS             fortress/threshold template, facility pads, halts, terminus template
        │
 6  TERRAIN FIELD        relief intents → deterministic height function → tiles
        │
 7  DRESSING             biomes, corruption, lineside kit, landmarks, names, light bake
        │
 8  DIRECTOR CONTEXT     spawn tags, candidate zones, affordance quotas, pressure curve
        │
 9  VALIDATION           ideal driver + sloppy driver on the real sim → accept / retry
        │
 LINE PLAN  (compact data, replicated to clients)  →  runtime streaming builds meshes
```

Stages 1–5, 8 and 9 produce the **Line Plan**, a compact data object (target < 500 KB compressed for Deep max). Stages 6–7 are pure functions of the Line Plan that run at build time on every machine, streamed around the train.

Each stage draws from its own RNG stream, sub-seeded as `hash(runSeed, stageName, edgeId, index)`. Changing one stage's logic must not reshuffle the output of any other.

---

# 5. Stage 0 — Run parameters

| Input | Source |
|---|---|
| `runSeed` (64-bit) | `hash(campaignSeed, routeId, runIndex)` |
| `D` | Route tier + route severity |
| Route biome bias | Route's region on the campaign map (§13.1) |
| `N_plan`, consist composition | Chosen at the fortress |
| Contracts | Chosen at the fortress; determine which facility types must appear |
| Crew size | Lobby; used only by the director and harness, never to change geometry |
| Weather profile | Rolled per run from tier and biome (§14) |
| Generator version | Stored in the Line Plan and the save |

Generation runs asynchronously on the host while the crew is still inside the fortress shopping and loading. Budget: **under 5 s** on target hardware including validation retries. If ten attempts fail validation, fall back to a curated seed from a per-tier list that is known to pass for that consist size (§16.4).

---

# 6. Stage 1 — Route graph

## 6.1 Graph model

Nodes: `fortress_depart`, `terminus`, `junction_facing` (diverging, needs a decision), `junction_trailing` (merging, spring switch, no action), `facility_junction`, `dead_end` (buffer stop).

Edges: `main`, `alternate` (leaves and rejoins the main line), `spur` (into a facility pad), `dead_line` (leaves and ends).

The main line is a single open-ended path from `fortress_depart` to `terminus`. It does not need to hit a fixed target, which keeps the alignment solver simple: the terminus is wherever the main line ends.

## 6.2 Construction order

1. **Main line length** from the tier table, lerped by severity.
2. **Reserved zones** by chainage: threshold (0–2 km), home straight (last 1.5 km), and the terminal spike window (§11.4).
3. **Facility slots.** First slot at `s ≥ max(3 km, 15% of length)`. Slots at least 3 km apart. Last slot at least 5 km before the terminus. Each slot reserves 1 km before and 0.5 km after for its approach and departure lulls.
4. **Alternates.** Choose main-line windows of 3–8 km that avoid reserved zones and facility lulls. The branch length is 0.7–1.4 × the window. Every alternate must **differ in its trade-off** from the main line it bypasses, drawn from:
   - *High line:* shorter, steeper, a trestle or a weak bridge
   - *Low line:* longer, flatter, marsh or river crossings
   - *Tunnel cut-off:* shorter, one long tunnel, radio blackout
   - *Settlement line:* through a dead settlement (Soot Children context), level
5. **Washouts** are placed on one side of a fork, never both. The other side must pass the validator for this consist.
6. **Weak bridges** go on alternates, or on the main line only with a limit the departing consist satisfies. **At least one route through every fork must be passable by the fully loaded departing consist.** The generator must never produce a run that is impossible for the train that left.
7. **Dead-line branches** (2–6 km, ending at a buffer stop) hang off facing junctions. The Switchman misroutes crews onto these. Some end at a wreck-yard facility instead of a buffer stop.
8. **Junction count check.** The count of facing junctions on the main line must meet the tier minimum. The Switchman needs at least 3 (GDD B.7), which Frontier's minimum of 3 guarantees.

## 6.3 Junction rules

- A turnout sits on **tangent, level-ish track**: no horizontal or vertical curve within ±40 m, grade ≤ 0.5%.
- Every facing junction has a default route, shown by a switch-stand lamp (straight / diverging aspect) visible from 250 m. Taking the non-default route means **stopping and throwing the switch by hand** (unless the crew owns the powered switch thrower upgrade).
- Because a facing junction may require a stop, its approach must satisfy the stopping-distance tell rule (§9.3) for the consist.
- Trailing junctions (merges) are spring switches. They need no crew action and no stop.
- Diverging branches curve away until corridor separation reaches at least 300 m within 2 km, so the two corridors' terrain intents do not fight (§12.4).

---

# 7. Stage 2 — Leg scripting

Each edge is filled with a **script**: an ordered sequence of set pieces separated by connectors, spent against the local share of the terrain budget.

## 7.1 Script shape: the sawtooth

Within each budgeted stretch, arrange pieces as:

```
recovery connector → build-up piece(s) → crunch piece or stack → recovery connector
```

Recovery connectors must be at least the tier's recovery length, with a sightline at fog distance and a communicated speed of at least cruise (14 m/s). This is where a crew can outrun what is chasing them. Pressure with troughs reads as rhythm; pressure without troughs reads as noise (GDD B.1).

## 7.2 Set piece library

All pieces live in `setpieces.json` with tier gates, parameter ranges, cost and emitted tags. Costs are in terrain budget units.

| Piece | Min D | Composition | Key parameters | Cost | Tags emitted |
|---|---|---|---|---|---|
| **Straight** (connector) | 0 | Tangent | 200–2500 m | 0 | `straight`, `straight_long` if ≥ 800 m and sightline ≥ fog |
| **Sweep** (connector) | 0 | Easement, arc, easement | R ≥ 800 m, 5–40° | 0 | `curve_gentle` |
| **Climb** | 0 | Grade up, crest vertical curve | g ≤ g_main, 0.6–4 km | 1–4, scales with g/g_main and length | `climb`, `climb_long` if ≥ 1.2 km |
| **Descent** | 0 | Grade down | g ≤ g_main, 0.6–4 km | 1–3 | `descent` |
| **Summit** | 0.5 | Climb, short level, Descent | | Sum of parts | `climb`, `crest`, `descent` |
| **Roller** | 0.5 | 3–6 alternating grades of ±0.5–1.5% | 300–600 m wavelength | 1 | `roller` (drives slack-action audio) |
| **The Drop** | 0.3 mild, 1.0 full | Descent ≥ 1.5 km ending in a limited curve | Limit 9–15 m/s (mild: ≥ 0.85 × line speed) | 3 | `descent`, `pre_curve` |
| **Blind Throat** | 1.0 | Reverse curves inside a deep cutting (8–20 m walls) | Sightline below fog | 3 | `cutting`, `blind_curve_exit`, `wet_bias` |
| **Ledge** | 1.0 | Shelf cut into a slope: +15–40 m up side, 20–60 m drop side | Often curved | 2 | `ledge`, `exposed` |
| **Trestle** | 0.5 | Bridge over a ravine, 60–400 m, 15–50 m high | Weak variant at D ≥ 1 | 2, +2 if weak | `bridge`, `exposed` |
| **River Crossing** | 0 | Flood-plain approach, girder or truss bridge | 40–200 m span | 2 | `bridge`, `water_crossing`, `low_ground` |
| **Causeway** | 1.0 | Level low embankment through marsh | 0.6–2.5 km, grade 0 | 2 | `marsh`, `low_ground`, `contaminated` at D ≥ 2 |
| **Tunnel** | 0.5 | Approach cutting, portal, bore, portal | Length and grade to tier caps; curved at D ≥ 2 | 1 per 300 m, +1 on grade, +1 curved | `tunnel`; `tunnel_exit` for 200 m after the exit portal |
| **Dark Forest** | 0 | Lineside region, biases toward curves | 1–4 km | 1 | `dark_forest` |
| **Open Plain** | 0 | Lineside region, biases toward long straights | 1–5 km | 0 | `open`, `exposed` |
| **Brass Field** | 1.0 | Growth across the rail for 50–200 m | Lineside growth ramps up over the approach | 1 | `brass` |
| **Momentum Bank** | 1.3 | Level run-up, then a short grade above g_main | Length ≤ 0.7 × the length the consist can carry momentum over | 4 | `climb`, `momentum` |
| **Dead Settlement** | 0 | Abandoned halt or dead town (§11.3) | 300–900 m | 0 | `dead_settlement`, `landmark` |
| **Hard Bend** | 0 | Straight, one curve of 40–90°, straight | Radius from the tier's derailing speeds (`bendDerail`), never under its minimum; laid only by the tier's count (`bends`), never by the budget. Where its stretch is full, cut into a connector or laid on a climb or descent (ARCHITECTURE §8 note 278; level-design.md Part B) | 2 | `curve_tight`, `pre_curve` |

**Terrain-gated enemy requirements are geometry rules, not spawn rules.** The Weight cannot spawn on grades, so Causeways and River Crossing approaches are level. The Ferryman needs a long clear straight, so Open Plains produce `straight_long`. The Gaunt watches tunnel exits, so every tunnel emits `tunnel_exit`.

**Momentum banks need a safe failure.** If the train stalls, it rolls back. The foot of every momentum bank must be level for at least the consist's length plus 200 m, with no junction, weak bridge or curve limit within that rollback zone. Stalling costs time (reverse, run at it again, or split the train), never lives.

## 7.3 Stacks

A piece's **demand window** runs from the start of its tell zone (§9) to its end. **Stack depth** at any chainage is the number of overlapping demand windows. Depth is capped by the tier table. A stacked piece costs `cost × (1 + 0.5 × (depth − 1))`.

**No two lethal checks within one reaction window.** Lethal checks are curve overspeed, weak bridge overload, and running onto a washout or buffer stop. Two lethal checks must be separated by at least the consist's full braking distance from line speed, so a correct driver can satisfy them one after another.

## 7.4 Signature stacks

The generator should place these deliberately, at least once per run where the tier allows, because they are the combinations real railways fear most.

| Stack | Min D | Chain |
|---|---|---|
| **Hounds' Hill** | 1.0 | Facility departure → `climb_long` while the heavy train is still accelerating (Cinder Hounds gain on grades) |
| **Blind Grease** | 1.5 | Blind Throat on a descent (a preferred Grease candidate) |
| **Junction at the Bottom** | 2.0 | Descent ending near a facility or alternate junction that requires a stop |
| **The Drop into High Iron** | 2.0 | The Drop, curve exit within 300 m of a trestle (weak at D ≥ 2.5) |
| **The Long Dark** | 2.5 | Tunnel ≥ 1 km on a climb, exiting onto a Ledge or Trestle |
| **The Gauntlet** | 3.0 | Momentum Bank → Blind Throat → Tunnel |

## 7.5 Biome bias

The route's region (§13.1) sets weights on piece selection. Mountain regions favour tunnels, ledges, climbs and trestles. Marsh favours causeways and river crossings. Forest favours curves and cuttings. Plains favour long straights. Industrial favours cuttings, dead lines and facility spurs.

---

# 8. Stage 3 — Alignment and profile

## 8.1 Horizontal alignment

Built from three primitives, all parameterised by arc length: **tangent**, **circular arc**, and **clothoid** (Euler spiral) transitions between them. Clothoid length is `L = max(40 m, v_line² / (0.5 × R))`, clamped to the kit's deformation limits. Transitions keep the ride smooth and prevent the lamp from snapping across the scene.

The script's curve intents (radius, deflection, direction) are realised in order. Between pieces, low-amplitude wander (deflections of 2–8° at R ≥ 1500 m) keeps connectors from looking ruled.

**World band.** The main line's heading stays within ±100° of the route's general bearing. When drift approaches the limit, the next connector curves back.

**Self-intersection.** Non-adjacent parts of any track (different edges, or the same edge more than 1 km apart in chainage) must stay at least 400 m apart, except within 2 km of a shared junction. Check against a 50 m occupancy grid. On failure, regenerate that edge from its last valid piece.

## 8.2 Branch closure

Alternates must rejoin the main line at a chosen trailing junction pose (position and heading). Realise the branch script up to its last piece, then close with a **G1-continuous connector** (clothoid–arc–clothoid plus tangent) that respects the tier's minimum radius. Distribute any residual error across the branch's connector lengths and wander deflections. If closure fails within tolerance (±0.5 m, ±0.2°) after five iterations, regenerate the branch with a new offset. Fallback shape: a simple symmetric bulge loop.

Spurs and dead lines are open-ended and need no closure.

## 8.3 Vertical profile

Rail height `z(s)` is piecewise linear grades joined by **parabolic vertical curves** with radius at least the tier minimum. Grade changes happen only through vertical curves.

The main line carries a slow regional elevation drift so the land rises and falls over the run, onto which set piece grades are applied. Alternates must meet the rejoin elevation: distribute the height difference across the branch's connector grades within the branch cap. If that is infeasible, regenerate.

## 8.4 Grade limits by consist

| Limit | Rule |
|---|---|
| Main-line sustained grade | ≤ `g_main = min(tierCap, 0.85 × climbMax(N_plan))` |
| Momentum grade | ≤ tier momentum cap, and length ≤ 0.7 × the distance the sim shows the loaded consist can climb from the communicated speed at its foot |
| Branch grade | ≤ branch cap. An alternate may be steeper than the consist can climb, as long as the other side of its fork is passable. The route card must show it. |
| Tunnel grade | ≤ tier tunnel cap |
| Facility spur grade | From the POI generator (0–4%). The spur must be climbable by the consist's loaded facility cut, or the pad must be downhill of the junction |

## 8.5 Curve limits

For a curve of radius `R`:

- **Derail threshold:** `v_derail = √(a_derail × R)`, with `a_derail = 1.0 m/s²`
- **Posted limit:** `v_posted = floor(√(a_post × R))`, with `a_post = 0.7 m/s²`

At these values a curve under ~480 m radius has a derail speed below the train's 22 m/s maximum. **Every night carries its tier's count of these** (the Hard Bend, §7.2; ARCHITECTURE §8 note 278): Local 1–2, Frontier 3–4, Dead Lines 4–5, Deep 5–6, and the validator holds it to the least (§16.3). A curve gets a speed board when its posted limit is below the communicated speed approaching it. For example, R = 150 m posts 10 m/s (derails at 12.2), and R = 300 m posts 14 m/s (derails at 17.3). Both constants are tunable in `tiers.json`.

## 8.6 Output

Every edge exposes, by chainage: position, heading, curvature, grade, rail height, cant (0 for now), and the containing piece. The train simulation reads **only this analytic data**, never mesh geometry. That keeps the sim identical on every machine (§17.3).

---

# 9. Stage 4 — Speed authority and tells

This stage is the fairness backbone. In dark territory, trains move on written and verbal authority. The generator's job is to make sure that authority is always sufficient.

## 9.1 Communicated speed

At every chainage, the **communicated speed** is the lowest of:

1. The **default line speed** for the tier, printed on the route card
2. Any **speed board** in effect (a limit board until its matching resume board)
3. Any **Form 19 slow order** covering that chainage
4. **Restricted speed** zones (§9.4)

> **The fairness guarantee: a train driven at or below the communicated speed, by a driver who reacts within `t_react`, can meet every downstream track demand.**

The validator's ideal driver drives exactly the communicated speed (§16).

## 9.2 Demands

A **demand** is any point where the track requires the train to be at or below a speed `v_req` by a chainage `s_req`:

| Demand | `v_req` |
|---|---|
| Limited curve | `v_posted` |
| Weak bridge | Its posted crossing speed |
| Brass field (to cut through) | Cutting speed, default 6 m/s |
| Facing junction requiring a stop, facility junction, washout, buffer stop | 0 |
| Restricted zone entry | Restricted speed |
| Yard limit at the terminus | 2.5 m/s |

## 9.3 Warning distance

For each demand, compute the distance needed from the communicated speed upstream:

```
d_warn = v_in × t_react + (v_in² − v_req²) / (2 × (a_brake × f_fade − g_down))
```

- `v_in` is the communicated speed approaching the demand
- `t_react` is 4 s (sighting, callout, application)
- `a_brake` is the consist's deceleration from the sim
- `f_fade` is the worst brake effectiveness the validator measures on that approach (brakes fade 8% per 10 s of application, Systems Spec B.5)
- `g_down` is `9.81 × sin θ` on descents, 0 otherwise

The **tell zone** for the demand starts at `s_req − d_warn × tellMargin(D)`. At least one required tell must sit at or before that point.

Worked example: a 20-car train at 14 m/s approaching a 10 m/s curve on level track with no fade needs `56 + (196 − 100)/0.7 ≈ 193 m`. At Frontier's 1.3 margin, the board sits at least 251 m before the curve, well beyond the fog. That is why boards exist.

## 9.4 Restricted speed and sightline

Some demands arrive with no board: an enemy telegraph seen in the lamp. Sleepers derail the train above a threshold speed (GDD A.2), and they are only visible within the lamp. For those, the generator computes a **sightline** at every chainage:

`sight(s) = min(fogMin(run), lampReveal, geometricSightline(s))`

The geometric sightline raymarches from the lamp position along the track over the terrain height field, so cuttings on curves shorten it. For a curve of radius `R` with lateral clearance `m` to the cutting wall, sightline is roughly `√(8Rm)`: a 300 m curve with 5 m clearance gives about 110 m.

**Restricted speed** is the highest `v_r` such that a train at `v_r` can react and brake to the Sleeper-safe speed within `sight(s) / tellMargin`. For a 20-car train with 100 m of sight at Deep margins, braking to just under the derail threshold, `v_r` works out to about 12 m/s. Short trains get much higher values.

**Sleeper candidate zones (§15.2) may only be placed inside restricted zones**, and every restricted zone has a required tell: a "RESTRICTED" board, a Form 19 entry, or both. That is the real railway rule ("prepared to stop within half the range of vision"), and it makes Sleeper country cost time rather than lives.

Restricted zones are placed both where geometry demands them (blind curves, deep cuttings) and where the generator wants Sleeper candidates. **Not every restricted zone holds Sleepers**, so crews can't learn to ignore the board. For the lethal speed threshold's units, see §22.2.

## 9.5 Tells

| Tell | Form | Notes |
|---|---|---|
| **Kilometre posts** | Numbered post every 1 km, minor post every 0.5 km | Reflective, readable in the lamp. The reference for all paperwork. Always required. |
| **Speed board** | Limit number on a reflective board, plus a "resume" board | Set for this consist's `v_posted` |
| **Restricted board** | "R" board at the entry, "END R" at the exit | |
| **Gradient post** | Two-armed post showing up/down and the grade number | Required at the foot of any climb that approaches `g_main` and at the top of any descent where fade matters |
| **Whistle board** | "W" board | Before tunnels and dead settlements |
| **Junction board** | "J4 — 2 km", "J4 — 1 km", with junction number | Required before any facing junction |
| **Facility board** | Facility name and distance | 2 km and 1 km before the facility junction |
| **Bridge board** | Bridge name; weak bridges add "MAX N CARS" and a crossing speed | Count in cars, not tonnes: cars are what the crew can count and say |
| **Line closed** | Red flag and "LINE CLOSED" board at the junction | For washouts. The washout itself is never a surprise. |
| **Form 19 entry** | Line on the route card paperwork (§9.7) | |
| **Switch-stand lamp** | Aspect lamp at every facing junction, visible from 250 m | The Switchman's telegraph ("a lamp signal reading wrong") uses this |
| **Dressing ramp** | Lineside growth or damage that intensifies over the tell zone | Brass fields and dead-line ends. Counts as a tell only in addition to one other. |

**Required tells are never removed.** Tier affects how many redundant tells exist: at redundancy 3 there is a board, a repeat board and a Form 19 entry; at redundancy 1 there is exactly one of those. The signage survival rate in the tier table applies only to non-required signage (extra km posts, dead signals, repeats), which may be intact, fallen or missing. Deep territory feels dark because nothing is repeated and the crew has to read the paper and count the posts, not because anything is hidden.

## 9.6 Weak bridges

A weak bridge has a car limit and a crossing speed. The limit is set from the departing consist: on a main-line weak bridge, `limit ≥ N_plan`; on an alternate, the limit may be below `N_plan` (that is the choice the GDD wants: "your longer, richer train may not clear it"). Crossing over the limit fails the bridge; the failure model is a sim decision (§22.5).

## 9.7 The route card (Form 19 and timetable)

The generator outputs structured data that the UI renders as paperwork (handwritten pages per the art pipeline; no HUD). Contents:

- **Timetable:** departure km 0, every named place with its km, junction numbers and default routes, facilities by name, type and km, alternates with their trade-offs ("High line via J3: shorter, 3.4% ruling, Harrow trestle"), terminus km, and the dawn time.
- **Form 19 slow orders:** restricted zones, washouts ("Line closed km 17.2, take the low line at J4"), weak bridge limits, momentum banks, and any demand whose required tell is paper-only at this tier.
- **Known grades:** the ruling grade of each route option, so the crew can plan around the consist before leaving.

The route card is how the crew chooses between alternates before they leave the walls, and it is the primary tell at Deep tiers.

---

# 10. Stage 5a — The starting area: fortress and threshold

The fortress is template-assembled; the threshold is a fixed template with seeded variation. Together they give the GDD's major tonal moment its room.

## 10.1 Fortress yard (chainage negative, ending at the outer gate)

- Assembled from a **fortress kit** (walls, gate, gun towers, floodlights, warehouses, coaling stage, workshop, yard crane, worker props) with a per-town identity preset (coal town, farm town, foundry town) that swaps signature buildings and dressing.
- **Departure road** at least the longest possible consist (20 cars, 330 m) plus 100 m, level and straight, where the consist is assembled and the crew shops and loads.
- **Yard throat** with 2–3 preset switches, then the **inner gate** at s = −600 m.
- Whole yard is at yard speed (2.5 m/s, below player run speed), so anyone can still catch the train.
- Lighting is authored: floodlights and workshop lights baked into the yard's vertices, plus a small number of dynamic lights around the consist.

## 10.2 The threshold (s = −600 m → +2 km)

| Chainage | Content |
|---|---|
| −600 m | Inner gate. Workers, voices, whistles, machinery audio at full. |
| −400 → 0 m | Defensive towers on both sides, searchlights sweeping the line. Towers thin out toward the gate. |
| **0 m** | **Outer gate. Km post 0.** The yard speed limit ends here. |
| 0 → 500 m | **Kill zone:** cleared, flat, open land. Wire, stumps, craters, burnt vehicles. The fortress's last lights fall off with distance. |
| ~300 m | **Last light:** a final lamp post or tower lamp. After it, the forward lamp is the only light. |
| 500 → 2000 m | **Dressing transition:** human traces fade, vegetation and corruption ramp in to the tier's level. |

Geometry through the threshold: straight or R ≥ 1000 m, grade ≤ 0.5%, no set pieces, no Sleeper or Grease candidates (GDD B.2 bans Sleepers within 2 km). This zone is where the crew settles into stations during the 90 s grace period.

Audio handoff: the generator emits chainage markers for the audio system (`gate_inner`, `gate_outer`, `last_light`), so the industrial bed fades out on geometry rather than on a timer.

---

# 11. Stage 5b — Stations

## 11.1 Facilities (POIs)

The line generator places and prepares each facility; the POI generator (Systems Spec D.1) builds its interior.

**Facility type selection**, in priority order:

1. Contracts chosen at the fortress guarantee their facility types appear.
2. A **coaling tower** is guaranteed when the validator's expected run time (transit plus planned facility time) exceeds 80% of tender endurance for `N_plan` (Systems Spec B.6: 53 min at 20 cars).
3. Remaining slots draw from biome-compatible types:

| Facility | Biome and terrain intent |
|---|---|
| Coaling tower | Any. Elevated trestle over the spur, gravity chute. |
| Grain elevator | Plains, rural. Flat pad, tall silhouette visible from far. |
| Foundry | Industrial basin, slag heaps. |
| Switchyard | Wide flat pad up to 400 m, many parallel sidings. |
| Wreck yard | **At the foot of a Drop or on a dead line**: where the previous train died. Environmental storytelling from geometry. |
| Slaughterhouse | Rural, low ground. |
| Chemical works | Low ground with drainage ponds, contaminated. |
| Mine head | Mountain. **Spur descends into a portal** (radio blackout in and out, GDD §18). |
| Military depot | Fortified cutting, embankment walls. |

**Every facility slot provides, in order along the main line:**

| Element | Rule |
|---|---|
| Facility boards | At 2 km and 1 km, with name and type |
| Approach | 1 km, grade ≤ 0.3%, straight or R ≥ 800 m, budget cost 0 (the facility lull) |
| **Holding track** | Level (±0.2%) for the consist's length + 50 m immediately before the junction. **Loaded cars parked here must not roll away.** |
| Facility junction | Facing, tangent, requires a stop. Satisfies §9.3 for the consist. |
| Spur | 100–600 m to the pad at the POI generator's spur grade |
| **Pad** | Reserved area sized by POI scale (compact 60 m to sprawling 400 m from the consist). Terrain flattened to the pad elevation. |
| Departure | Rejoins the main line (single-ended POIs reverse out through the same junction; looped POIs rejoin via a trailing junction). 0.5 km lull after. |

The line generator passes the POI generator: pad pose and bounds, pad elevation, spur entry pose, facility type, power state bias by tier, and a sub-seed.

## 11.2 Drop-in pickup points

Drop-in joins happen only at facilities (Systems Spec Part E). Every facility pad includes a **pickup point**: a lit shelter or platform where a joining player spawns, visible from the consist.

## 11.3 Abandoned halts and dead settlements

Every 4–8 km of main line (and on settlement-line alternates), place a **Dead Settlement** piece. These are landmarks and spawn contexts, not stops.

- **Halt:** a single platform, name board, shelter, dead signal, water tower. 300 m.
- **Dead town:** platforms, station building, goods shed, sidings with derelict stock (reusing the consist kit in damaged states), road crossings, house shells. 600–900 m.
- Each gets a **name** on its board and on the route card, and a whistle board before it.
- Emits `dead_settlement` (Soot Children context, GDD B.6) and `landmark`.

Proposed, not in the GDD: halts as places to scavenge coal, ammunition or scrip. Leave a `lootTable` field empty in data so design can decide later.

## 11.4 The terminus and arrival

The arrival mirrors the threshold in reverse, with the run's biggest terrain moment just before it.

| Distance before the gate | Content |
|---|---|
| 4 → 1.5 km | **Terminal spike:** the final approach's largest stack, placed deliberately. Pairs with the director's "one deliberate spike" (GDD B.1). |
| ~3 km | **Sky glow** above the fog: floodlight glow and sweeping searchlight beams, visible as light before anything is visible as shape. "Eventually the crew sees lights." |
| 1.5 km | **Home straight** begins: level, straight or R ≥ 1000 m, long sightline. Budget cost 0. |
| 1.0 km | "YARD LIMIT" board (demand: 2.5 m/s at the gate) |
| 500 m | Director spawn ban begins (`terminus_safe` tag) |
| ~300 m | Walls and gun towers resolve out of the fog |
| 0 | Outer gate. Arrival yard with a stop road long enough for the consist. |

**Terminus variants** by tier: *Live fortress* (Local, Frontier) and *Silent settlement* for Dead lines, which by definition run to towns that have stopped responding. The silent variant needs a design decision on whether its gate still counts as safe (§22.4). Build both from the fortress kit; the silent variant swaps lit assets for dark ones and damage states.

---

# 12. Stage 6 — Terrain field

The world is a **corridor**, not a map. Fog caps view distance at 60–120 m, so terrain only needs to exist within about 250 m of any track.

## 12.1 Relief intents

Every piece writes a **relief intent** for each side of the track over its chainage range. The intent says what the land should do relative to the rail, not where it is in absolute terms.

| Intent | Target height relative to rail | Slope treatment |
|---|---|---|
| `plain` | ±0–2 m | Gentle blend |
| `cutting` | +6 to +20 m | Soil cut 1:1.5; rock cut 2:1 with rock kit when H > 6 m |
| `embankment` | −2 to −8 m | Fill 1:2 |
| `ledge_up` / `ledge_drop` | +15 to +40 m / −20 to −60 m | Rock face up side, retaining wall kit if the slope won't fit; drop side steep but not sheer (§12.6) |
| `ravine` | −15 to −50 m under bridges | Channel under the span, abutment fill slopes to the ends |
| `river` | −3 to −8 m, water plane | Channel perpendicular to the track, extending to corridor edge |
| `marsh` | Water level −0.3 m with pools | Causeway formation +1.5 m above water, fill 1:2 |
| `mountain` | ≥ rail + 18 m over tunnels | Cover over the bore; steep faces at portals |
| `pad` | Flat at pad elevation | Flattened area for facilities, fortress, junctions, settlements |

Intents are blended over 60 m at piece boundaries so the land never steps.

## 12.2 Height function

`h(x, z)` is a pure, deterministic function evaluated on a 2 m grid:

1. **Find nearby track** via a spatial index of centreline samples (every 5 m) within 250 m.
2. For each nearby edge, get signed lateral offset `d`, chainage `s`, rail height `z_r(s)`, and the intent for that side.
3. **Corridor profile** for that edge:
   - `|d| ≤ 3.5 m`: formation, `z_r − 0.5` (the ballast and sleeper kit sits on it)
   - `3.5–5 m`: shoulder and ditch, down to `z_r − 1.1`
   - beyond 5 m: rise or fall toward the target `z_t = z_r + H_intent + noise`, limited by the intent's slope ratio, so cut and fill slopes appear naturally
   - Noise weight ramps in with `smoothstep(5, 30, |d|)`; noise is mid-frequency (30–120 m) and amplitude scales with biome
4. **Combine tracks:** within any edge's formation (`|d| < 5 m`) that edge wins outright, blended over 2 m. Elsewhere, weight each edge's height by distance falloff × role priority (main > alternate > spur > dead line). Around every junction, force a `pad` intent for ±150 m on all joining edges so they agree.
5. **Beyond 250 m**, the corridor edge dips below the fog line with a skirt; nothing out there is ever seen clearly.

Tiles are 256 m × 256 m at 2 m resolution (129 × 129 vertices), generated only where the occupancy grid says track is within 250 m.

## 12.3 Structures

| Structure | Terrain interaction | Kit |
|---|---|---|
| **Tunnel** | Formation carve is disabled inside the bore; the height field sits above it (`mountain` intent). At each portal, the approach cutting deepens to meet a steep face; the portal facade and cliff meshes cover the step. Fallback if a gap shows: a per-tile hole mask at the portal. | Portal facades (stone, timber, concrete), bore liner cells, refuges, name plate |
| **Bridge / trestle** | Formation carve disabled over the span; `ravine` or `river` intent below; abutment fill slopes at each end | Girder spans 10–30 m, trestle bents at 5 m pitch, truss spans 40–60 m, masonry arch viaduct; piers extend from deck to terrain |
| **Causeway** | `marsh` intent both sides; formation raised | Embankment edge stones, culverts every 100 m |
| **Retaining wall** | Placed where a cut or fill slope would exceed the available width (ledges, cuttings next to structures) | Masonry and timber wall kits |

Rule for turnouts and structures: **kit pieces with fixed geometry** (turnouts, bridge spans, portals) sit on tangent or constant-curvature track only, which §6.3 and the piece rules guarantee.

## 12.4 Water

Each water body (river, marsh) has a flat water plane at a fixed height, stored in the Line Plan. Rivers exist only as a short channel through the corridor, fading into fog at both ends. Marsh water fills below `water level` wherever the height field dips under it.

## 12.5 Surface materials

Splat weights are computed per vertex from: slope, distance from track (cinder path within 5 m, ballast spill), intent (rock on steep cuts, mud on marsh), biome, corruption level, and wetness. Keep the material vocabulary restrained per GDD §27: soil, dead grass, rock, mud, cinder, slag, snow crust for deep cold, corruption crust.

## 12.6 Walkability and body recovery

Bodies persist where players die, and recovery is a core system (Systems Spec Part C). So:

- The drop side of any ledge or ravine must stay walkable (≤ 45°) within 40 m of the track, or have a retaining wall.
- If a body comes to rest outside the walkable corridor, relocate it to the nearest walkable point on the formation edge. Never generate an unrecoverable body.

---

# 13. Stage 7 — Dressing, biomes, landmarks and light

## 13.1 Biomes

Each route belongs to a campaign-map region with biome weights by tier. Along the line, biomes change in regions of 2–6 km with 300 m transitions.

| Tier | Biome weights |
|---|---|
| Local | Rural/farmland 50 · forest edge 30 · plains 20 |
| Frontier | Black forest 40 · hills 25 · marsh 20 · rural 15 |
| Dead lines | Black forest 30 · mountain 30 · marsh 20 · dead-town belts 20 |
| Deep | Industrial ruin 35 · mountain 25 · slag and mining 25 · contaminated marsh 15 |

"Travelling farther from civilization" is literal: the landscape moves from worked land to wilderness to ruined industry as `D` rises.

## 13.2 Scatter rules

Deterministic jittered-grid scatter per tile, sub-seeded by tile coordinates.

| Distance from track | Contents |
|---|---|
| 0–3.5 m | Track kit only (rail, sleepers, ballast) |
| 3.5–8 m | Lineside kit: signage, km posts, cable troughs, drainage |
| 8 m | **Telegraph poles** every 50 m on one side (switching sides at structures), sagging wires, some broken by tier. They are a speed cue and a guide line in fog. |
| 8–15 m | Low vegetation, fences, debris |
| 15 m + | Trees and biome props at biome density |

Exclusions: structures, pads, water, slopes above each prop's limit.

**Corruption** scales with the tier's corruption level and rises further near brass fields, contaminated marsh and dead settlements: fungal crust and mineralized growth on trees and poles, pale sacs, fouled water. Per GDD §26, corruption reads as biology, not glowing magic.

**Brass fields** ramp their lineside growth over the tell zone, so the growth thickens visibly as the crew approaches.

## 13.3 Landmarks and names

The crew must be able to say where they are. The generator names every callable place from `names.json` word lists using period railway conventions:

| Thing | Pattern | Example |
|---|---|---|
| Junctions | Number, plus a name at Frontier+ | "J4 — Carrow Junction" |
| Tunnels | Number + name, plate on both portals | "Tunnel 2 — Blackwell" |
| Bridges | Name + type, plate at each end | "Harrow Trestle" |
| Halts and towns | Surname or feature + Halt, Siding, Crossing | "Mile 14 Halt", "Grieve Siding" |
| Facilities | Owner + type | "Voss Foundry" |

Names must be distinct in their first word within a run, so callouts can't be confused on a bad voice line. Every named place goes on the route card.

## 13.4 Light bake

Track cells and terrain tiles cannot be lightmapped ahead of time, so the art pipeline bakes per vertex at build time:

- **Ambient:** sky visibility from horizon occlusion (8 directions over the height field within 60 m). Cuttings and ravine floors go darker.
- **Moonlight:** `N·L` toward the run's moon direction, with a single occlusion ray.
- **Zero** inside tunnels and under bridge decks.
- **Fortress zones:** add baked floodlight contributions from the fortress template's light positions, falling off through the threshold.

---

# 14. Weather and exposure

The run rolls one weather profile (fog distance, rain probability, temperature, wind) from its tier and biome. Terrain then modifies it locally, and the generator writes per-segment modifiers the sim, audio and director read.

| Modifier | Terrain rule |
|---|---|
| **Fog density** | ×1.3 in `low_ground` and `marsh`, and beside water (a shore, or within 40 m of a lake), ×0.8 on crests. The renderer's fog follows it along the line, blended over 150 m either side (ARCHITECTURE §8 note 313) |
| **Wet rail (adhesion)** | Rain lowers adhesion everywhere; `wet_bias` segments (cuttings, forest, near portals) are worse |
| **Wind exposure** | ×1.5 on `exposed` segments (bridges, ledges, crests, open plains). Gunfire carries further there (GDD §22). |
| **Cold** | Deeper with altitude above the departure fortress (one step per 150 m) and on `exposed` segments |

**Fairness uses worst-case weather.** Sightlines in §9.4 use the run's minimum fog distance, and adhesion in §9.3 uses its worst wet-rail value, so a change of weather mid-run can never make obeying authority unsafe.

---

# 15. Stage 8 — Director interface

The director (GDD Appendix B) spends enemy budget; the generator supplies the ground it spends it on.

## 15.1 Spawn context tags

Every edge carries tag intervals by chainage. The director queries tags ahead of the train.

| Tag | Used by |
|---|---|
| `straight_long` | The Ferryman, Sleepers |
| `blind_curve_exit` | Sleepers |
| `pre_grade`, `pre_curve` (400–900 m before either) | The Long Whistle |
| `curve_tight` (every bend that derails the train under its top speed; ARCHITECTURE §8 note 278) | The places a train slows, where things board (GDD App. F: "slowing opens the doors"); nothing reads it yet |
| `climb`, `climb_long` | Cinder Hounds (gain on grades), The Drift + grade pair, Clingers + grade pair |
| `descent` | Grease preference |
| `marsh`, `low_ground`, `water_crossing` | The Weight, The Drift |
| `contaminated` | The Drift |
| `dark_forest`, `open` | Lamplighters |
| `dead_settlement`, `near_facility` | Soot Children, Followers |
| `junction` | The Switchman |
| `tunnel`, `tunnel_exit` | Radio blackout; The Gaunt |
| `mine_spur` | Radio blackout |
| `terminus_safe`, `grace` | Spawn bans |

## 15.2 Candidate zones for placed enemies

Sleepers and Grease are level content, placed at generation (GDD B.2). The generator outputs candidate zones; the director's line-time pass fills them.

- **Sleeper zones:** straights and blind curve exits, **only inside restricted zones** (§9.4), never within the first 2 km, density ×1 Local, ×2 Frontier, ×3.5 Dead lines and Deep (see §22.3).
- **Grease zones:** preferentially on grades and curve approaches, density ×2 in wet or cold weather.

## 15.3 Affordance quotas

The director's conflict table (GDD B.1) needs certain terrain to exist. The generator must guarantee, per run:

| Tier | Must contain |
|---|---|
| Local | ≥ 1 `straight_long` · ≥ 1 `climb` · ≥ 1 `dark_forest` or `open` |
| Frontier | + ≥ 1 `climb_long` · ≥ 1 `marsh` or `water_crossing` · ≥ 3 facing junctions · ≥ 1 `dead_settlement` within 2 km of a facility |
| Dead lines | + ≥ 1 dead-line branch · ≥ 2 `climb_long` · ≥ 1 tunnel |
| Deep | + ≥ 2 tunnels · ≥ 1 `contaminated` region |

Quota failures trigger regeneration of the relevant legs, not the whole run.

## 15.4 Terrain pressure curve

The generator exports `terrainPressure(s)`, the stacked cost of active demand windows at each chainage. The director uses it to avoid piling its own spikes onto terrain crunches beyond fairness, or to seed conflict pairs there deliberately (for example, Clingers during a `climb_long`). The agent harness verifies the combined pressure.

---

# 16. Stage 9 — Validation

Every generated Line Plan is driven headlessly on the **shared train simulation module** before it is accepted. This is separate from, and feeds, the GDD §34 agent harness.

## 16.1 Ideal driver

Computes the speed profile with the classic forward–backward method: the maximum speed at each point is the communicated speed; braking curves run backward from each demand using the consist's real deceleration, grade and fade; acceleration curves run forward with real traction and grade. Brake fade is path-dependent, so iterate the pass until the fade trace converges (usually two passes). It stops at every planned facility and assumes the main line at every fork unless the main side is closed.

## 16.2 Sloppy driver (Local and Frontier only)

Drives at communicated speed +10% with `t_react = 6 s`. It must still survive every lethal check. This is what the 1.5× and 1.3× margins buy, and it's what keeps new crews from bouncing off.

## 16.3 Acceptance checks

| Check | Pass condition |
|---|---|
| Determinism | Same inputs → byte-identical Line Plan |
| Geometry limits | Radii, grades, vertical curves and tunnel lengths within tier caps |
| Ruling grade | Main line sustained grades ≤ `g_main` for `N_plan` loaded |
| Momentum banks | Ideal driver crests; stall rollback zone clean |
| Descents | Ideal driver holds every limit with fade; brake effectiveness never below 60% |
| Tells | Every demand has a required tell at or before `s_req − d_warn × margin` |
| Lethal spacing | No two lethal checks within one full braking distance |
| Stacks | Stack depth ≤ tier cap everywhere |
| Recovery | Every crunch followed by a recovery connector ≥ tier minimum |
| Forks | At least one side of every fork passable by the loaded consist |
| Holding track | Level and long enough at every facility |
| Threshold and home straight | Zones clean; no Sleeper candidates in the first 2 km |
| Dawn | Ideal transit + 4 min per facility slot ≤ dawn timer (see §22.1). Taking every facility is allowed to run late; that is the slack decision the GDD wants. |
| Coaling | Coaling tower present when required |
| Quotas | Affordance quotas met |
| Hard bends | At least the tier's least count of main-line bends that derail the train under its top speed (`bends`; ARCHITECTURE §8 note 278) |
| Separation | No corridor overlap except at shared junctions |
| Walkability | Ledge and ravine drop sides meet §12.6 |

## 16.4 Retry and fallback

Failures regenerate the smallest scope that fixes them (a piece, a leg, an edge) before regenerating the run. After ten failed attempts, use a **curated fallback seed** from `fallback_seeds.json`: a per-tier, per-consist-band list of seeds proven to pass. Log every fallback; they are generator bugs.

## 16.5 Metrics report

Every plan writes a stats record: length, ideal transit time, average speed, ruling grade per route option, minimum radius, total tunnel and bridge length, junction counts, stack depth histogram, total terrain cost, tag coverage lengths, required-tell counts and slack against the dawn timer. The harness sweeps thousands of seeds per tier overnight and checks that Deep is meaningfully harder than Local at matched crew and consist (GDD B.9).

---

# 17. Runtime

## 17.1 Streaming

The whole Line Plan exists up front (it is small). Meshes are built on demand:

- **Train window:** track cells and terrain tiles from 400 m behind the rearmost car to 1.2 km ahead of the engine, following the current switch settings. When a facing junction is inside the window, build both branches.
- **Anchor bubbles:** a 250 m radius around every **anchor**: each player, each body, each detached car, each active enemy with a world position. Players walk back for bodies and cars; the world must still be there.
- Build jobs run off the main thread with a per-frame budget; unload with 150 m of hysteresis. Because generation is deterministic, dropping and rebuilding a tile is always safe.

## 17.2 Track cells

Track is built in **20 m cells** by chainage (matching the art pipeline's track kit). Each cell deforms the straight kit mesh along the alignment (rail, sleepers, ballast), picks a biome ballast variant, and places any lineside kit whose chainage falls in it. Fixed-geometry pieces (turnouts, bridge spans, portals) are placed whole.

## 17.3 Networking

The GDD's model is host-authoritative with a shared simulation module (§33).

- **The host generates the Line Plan** during the fortress phase and replicates it to clients compressed. Late joiners receive it at facility drop-in along with the current state.
- **The train simulation runs on Line Plan data only** (chainage, grade, curvature), so every machine's train physics is identical without depending on mesh generation.
- **Terrain heights must match** for player movement prediction on the ground. Implement the height function with integer-hash noise and strict floating-point settings (no fast-math, no platform-dependent intrinsics). As a safety net, the host broadcasts a checksum for each tile it builds; a client whose tile checksum differs requests that tile's heights from the host (about 30 KB compressed) and uses those.

## 17.4 Saves

Autosave happens per facility on departure (Systems Spec Part E). The save stores the **Line Plan itself** plus the generator version, not just the seed, so a generator update never changes a run in progress. Crash recovery restores the Line Plan and the train at the last facility's departure chainage.

## 17.5 Performance targets

| Item | Target |
|---|---|
| Line Plan generation with validation | < 5 s on host, async during the fortress phase |
| Line Plan size | < 500 KB compressed at Deep max |
| Terrain tile build | < 4 ms per tile on a worker thread |
| Resident tiles | ≤ 40 in normal transit |
| Track cell build | < 0.5 ms per cell |

---

# 18. Art build

Claude Code builds all art. These are the generator's asset requirements; follow the art pipeline plan and GDD §25–31 for style, triangle budgets and texture density.

| Kit | Pieces |
|---|---|
| **Track** | 20 m deformable cell (rail, sleepers, ballast) in 3–4 biome ballast variants; turnout (left and right); buffer stop; derailer; switch stand with aspect lamp |
| **Signage** | Km post (major, minor), speed board and resume, restricted boards, gradient post, whistle board, junction board, facility board, bridge plate and limit board, line-closed flag and board, station name board. Each in intact, fallen and missing (absent) states. All reflective in the lamp. |
| **Lineside** | Telegraph pole and wire span (intact, leaning, broken), fence runs, cable trough, dead signal, water tower, platelayer's hut |
| **Bridges** | Girder spans (10, 20, 30 m), trestle bent, truss span (40, 60 m), masonry arch, abutments, piers, weak-bridge damage variant |
| **Tunnels** | Portals in stone, timber and concrete with name plates; bore liner cell (straight, curved); refuge niche |
| **Earthworks** | Rock face panels, retaining walls (masonry, timber), culverts, causeway edge stones |
| **Stations** | Halt platform, shelter, station building (3 variants), goods shed, signal box, derelict stock (consist kit in damage states) |
| **Fortress** | Walls, inner and outer gates, gun towers, floodlights, searchlights, warehouses, workshop, coaling stage, yard crane, worker props; per-town identity sets; silent-settlement damage and dark variants |
| **Terrain** | Splat materials per biome; water plane material; corruption crust |
| **Vegetation** | Low-poly card and mesh trees (living, dead, corrupted), shrubs, reeds, brass growth in three densities |
| **Sky** | Fortress sky-glow billboard and searchlight beam cards visible above fog |

**Demo scope** (per the art plan): straight, curve, grades, junction, bridge and tunnel kits; foundry and switchyard facilities; one fortress identity; one halt. Washout and weak bridge variants follow after the demo.

---

# 19. Data

## 19.1 Line Plan schema (sketch)

```jsonc
{
  "version": "linegen-1.0.0",
  "seed": "0x9F3A...",
  "route": { "id": "frontier-07", "tier": 1, "severity": 0.4, "D": 1.4 },
  "consist": { "nPlan": 8, "maxMassT": 410 },
  "weather": { "fogMinM": 92, "rain": true, "tempStep": 1, "wind": 0.6 },
  "graph": {
    "nodes": [{ "id": "n0", "type": "fortress_depart" }, { "id": "j3", "type": "junction_facing", "name": "J3 — Carrow Junction", "defaultEdge": "e_main_2" }],
    "edges": [{ "id": "e_main_1", "role": "main", "from": "n0", "to": "j3", "lengthM": 6120 }]
  },
  "alignment": {
    "e_main_1": {
      "startPose": { "x": 0, "z": 0, "headingDeg": 12 },
      "horizontal": [{ "t": "tangent", "len": 2100 }, { "t": "clothoid", "len": 60, "r1": 0, "r2": 450 }, { "t": "arc", "len": 310, "r": 450, "dir": "L" }],
      "vertical": [{ "t": "grade", "len": 1800, "g": 0.3 }, { "t": "vcurve", "len": 120, "g0": 0.3, "g1": 1.6 }]
    }
  },
  "pieces": [{ "id": "p12", "type": "the_drop", "edge": "e_main_1", "s0": 3800, "s1": 5600, "params": { "limitMs": 12 }, "tags": ["descent", "pre_curve"], "cost": 3 }],
  "intents": { "e_main_1": [{ "s0": 3800, "s1": 4400, "left": { "t": "cutting", "h": 11 }, "right": { "t": "cutting", "h": 9 }, "biome": "black_forest" }] },
  "structures": [{ "type": "trestle", "edge": "e_alt_1", "s0": 900, "s1": 1140, "heightM": 28, "weak": { "maxCars": 6, "speedMs": 6 }, "name": "Harrow Trestle" }],
  "water": [{ "id": "w1", "type": "river", "levelM": 41.2, "edge": "e_main_2", "s": 2210 }],
  "authority": {
    "lineSpeedMs": 18,
    "limits": [{ "edge": "e_main_1", "s0": 5350, "s1": 5700, "vMs": 12, "source": "board" }],
    "restricted": [{ "edge": "e_main_2", "s0": 800, "s1": 2300, "vMs": 13 }]
  },
  "signage": [{ "type": "speed_board", "edge": "e_main_1", "s": 5020, "side": "R", "value": 12, "state": "intact", "required": true }],
  "pois": [{ "id": "poi1", "type": "foundry", "name": "Voss Foundry", "junction": "fj1", "holding": { "edge": "e_main_2", "s0": 3100, "s1": 3280 }, "pad": { "x": 0, "z": 0, "elevM": 55, "radiusM": 180 }, "spurGrade": 1.5, "subSeed": "0x..." }],
  "landmarks": [{ "type": "halt", "name": "Grieve Siding", "edge": "e_main_1", "s": 4200 }],
  "director": {
    "tags": [{ "tag": "marsh", "edge": "e_main_3", "s0": 0, "s1": 1400 }],
    "sleeperZones": [{ "edge": "e_main_2", "s0": 900, "s1": 2100 }],
    "greaseZones": [{ "edge": "e_main_1", "s0": 4100, "s1": 4300 }],
    "pressure": { "stepM": 50, "values": [0, 0, 0, 1.2] }
  },
  "routeCard": { "timetable": [], "form19": [], "knownGrades": [], "dawnS": 2560 },
  "markers": [{ "type": "gate_outer", "edge": "e_main_1", "s": 0 }, { "type": "last_light", "edge": "e_main_1", "s": 300 }],
  "validation": { "idealTransitS": 1480, "dawnSlackS": 690, "attempts": 2, "fallback": false }
}
```

## 19.2 Config files

| File | Contents |
|---|---|
| `linegen/tiers.json` | §3.2 table, §3.3 budget curve, curve-limit constants, `t_react`, demo switches |
| `linegen/setpieces.json` | §7.2 library and §7.4 signature stacks |
| `linegen/biomes.json` | §13.1 weights, scatter rules, noise amplitudes, materials |
| `linegen/facilities.json` | §11.1 type-to-biome and intent mapping |
| `linegen/signage.json` | Board types, visibility, redundancy rules |
| `linegen/names.json` | Word lists and patterns |
| `linegen/fallback_seeds.json` | Curated seeds per tier and consist band |

---

# 20. Code layout and tooling

## 20.1 Modules

| Module | Responsibility | Depends on |
|---|---|---|
| `LineGen.Core` | RNG streams, graph, scripting, alignment, profile, authority, stations, director context, route card. **Pure and engine-free.** | Shared sim (for validation) |
| `LineGen.Validate` | Ideal and sloppy drivers, checks, metrics | Core, shared sim |
| `LineGen.Terrain` | Deterministic height function, intents, tile heights, checksums | Core |
| `LineGen.Build` | Engine side: track cell deformation, tile meshes, structures, scatter, splat, light bake, streaming manager | Core, Terrain, engine |
| `LineGen.Tools` | CLI and debug views | All |

RNG: SplitMix64 for seeding, PCG32 or xoshiro128** for streams. Never use the engine's or the platform's random functions anywhere in Core or Terrain.

## 20.2 Tools

- `linegen generate --tier frontier --severity 0.4 --cars 8 --seed 1234 --out plan.json --map map.png --profile profile.png`: writes the plan, a top-down map (edges, junctions, pieces coloured by type, tags) and a profile plot (rail height, grade, communicated speed, ideal speed, tell zones).
- `linegen sweep --tier all --cars 3,10,20 --seeds 1000 --report sweep.csv`: batch generation, validation and metrics.
- **In-engine overlay:** chainage, piece IDs, tags, communicated speed, tell zones, sightline, stack depth, anchors and resident tiles.
- **Ride mode:** drives the ideal speed profile automatically with a free camera, for reviewing a seed visually in minutes.

---

# 21. Milestones and acceptance

The GDD schedules the procedural line for weeks 9–13, with a public demo in December 2026. Order:

| # | Milestone | Acceptance |
|---|---|---|
| **M0** | Schema, RNG streams, CLI skeleton | Same seed → byte-identical JSON across two runs and two machines |
| **M1** | Main line alignment and profile; train sim reads Line Plan data; flat placeholder ground | Train drives a generated 20 km main line end to end; grades and curvature affect the sim |
| **M2** | Terrain field, tiles, streaming with anchors, tile checksums | Walk 250 m from the track anywhere and find ground; host and client checksums match on 1000 tiles |
| **M3** | Set piece grammar, tier tables, authority and tells, validator | Sweep of 1000 seeds per tier at 3, 10 and 20 cars: ≥ 98% pass first or second attempt; zero tell violations |
| **M4** | Graph: junctions, alternates, spurs, facility pads and holding track; fortress, threshold and terminus templates; route card | A crew can depart, choose an alternate from the route card, stop at a facility junction, park loaded cars on level track, and arrive |
| **M5** | Structures, biomes, dressing, names, light bake, weather modifiers | Screenshot test (GDD §32) passes on 20 random seeds per tier |
| **M6** | Director tags, candidate zones, quotas, pressure curve; harness integration | Harness sweep confirms spawn gates and conflict pairs are satisfiable on every sampled seed |

**Demo cut:** M0–M6 at Local and Frontier with demo config (§3.2). Dead lines, Deep, washouts, weak bridges, momentum banks and silent settlements follow after the demo.

---

# 22. Conflicts and open issues found in the source documents

Implement the config default, log a warning, and flag for the director.

**22.1 Run length versus route length.** The Systems Spec gives routes of 18–40 km and total runs of 28–45 min, but also a dawn timer of `length ÷ 11 m/s × 1.18`. At 40 km that timer is about 71 minutes, and transit alone at cruise is about 48 minutes, so Deep runs cannot fit 45 minutes. **Default:** keep the 40 km cap in config and compute dawn with the spec's formula, but the validator also reports slack against ideal transit. Recommendation for design: either cap Deep near 30 km or accept 55–70 min Deep runs, and consider computing dawn from validated ideal transit so steep, slow Deep routes don't lose slack by accident.

**22.2 Sleeper lethal speed units.** GDD A.2 gives Sleeper outcomes at ">40" and "20–40" with no units. The Systems Spec uses m/s and a 22 m/s maximum, so these are presumably km/h (11.1 and 5.6 m/s). **Default:** 11.1 m/s derail, 5.6 m/s damage threshold.

**22.3 Sleeper density at Deep territory.** GDD B.2 lists ×1 / ×2 / ×3.5 for Local, Frontier and Dead lines, with nothing for Deep. **Default:** ×3.5.

**22.4 Silent settlement arrival.** Dead lines run to towns that have stopped responding, but the GDD's arrival is lights, walls and a safe gate. **Default:** generate the silent variant with dark walls but keep the gate as a safe zone; design decides.

**22.5 Weak bridge failure.** The GDD says a long train "may not clear it" without defining what happens. **Default:** crossing over the car limit collapses the span under the first car past the limit (derailment, run ends), telegraphed by the board and Form 19.

**22.6 Fog versus lamp reveal.** The art pipeline caps fog at 60–120 m, and the GDD has Sleepers telegraphing in the lamp beyond 120 m. **Default:** `lampReveal = 120 m` on the track axis only (the lamp cone cuts fog), and restricted-speed zones (§9.4) carry fairness for heavy consists either way.

**22.7 Engine.** The Systems Spec is titled for Unity; earlier art planning assumed a custom engine. Core and Terrain are engine-free by design, so this only affects `LineGen.Build`. Confirm before M1.

**22.8 Not in the GDD (proposed, off by default):** water stops for the boiler, halt scavenging, rockfalls on ledges, and coupler breaks on rollers. Each is a config flag so design can switch it on without code changes.

---

*Dark Territory · Line & Terrain Generation Plan v0.1 · September 2026*
