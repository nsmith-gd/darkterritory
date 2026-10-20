# Dark Territory: Monster Spawn Director Overhaul (the director's brief, 20 Oct 2026)

The director's brief, kept as written. The plan that splits it into queue items #600–#613 is
[note 874](../notes/874.md); each item's row in [docs/COORDINATION.md](../COORDINATION.md) says what it owns and what it
waits on.

## Mission

Audit, improve, implement, and validate the monster spawning and encounter pacing systems in Dark Territory.

**Primary objective:** Make Dark Territory feel like a Lethal Company-style cooperative horror game in which monster
encounters become progressively more frequent, dangerous, and socially disruptive as a run develops.

The game's current spawning system is sophisticated but consistently produces too few meaningful monster encounters,
especially during solo and two-player sessions.

Do not solve this by simply increasing global spawn weights. Fix the underlying systems that prevent monsters from
appearing, and introduce reliable encounter pacing that scales with the crew, train, and progression of the night.

Implement the changes directly in the repository, following its existing architecture, contribution conventions, and
coordination requirements.

## 1. Non-negotiable design goals

### A. Time escalation

**The longer a run continues, the more hostile it becomes.**

- Monster encounter frequency increases throughout the run.
- Late-run encounters become more frequent than early-run encounters.
- Quiet periods become progressively less forgiving.
- The director increasingly creates overlapping threats.
- Players who are behind schedule should experience mounting danger.
- Slowing down, stopping, or spending too long loading cargo must not become an effective method of avoiding the
  difficulty curve.

Difficulty progression must be calculated from both elapsed night time and route progress, whichever places the crew
further into the run.

Preserve safe zones, departure grace periods, post-crisis recovery, and the terminus's established no-spawn approach.

### B. Player-count scaling

**More players means the director escalates faster and can deploy more monsters.**

- Solo play must have consistent, interesting monster encounters.
- Two-player play must regularly produce situations requiring cooperation.
- Four-player play should involve overlapping threats across different train positions.
- Eight-player play should create multiple fronts demanding coordination and delegation.
- Adding players must increase the encounter arrival rate, not merely the total spawn budget.
- The time-based difficulty ramp should become steeper as player count increases.

Use the number of active living crewmates, handling human players and bots consistently. Do not count disconnected or
absent players as active capacity. Existing relief systems should still prevent a depleted crew from being overwhelmed
indiscriminately.

### C. Train-length scaling

**The longer the train, the greater the density of monsters and the difficulty of defending it.**

- Additional cars increase encounter arrival frequency.
- Longer trains support more simultaneously active threats.
- Threats should distribute themselves across different train areas.
- A larger train creates more opportunities for boarding, sabotage, cargo attacks, and system failures.
- Scaling must be gradual, not dependent on arbitrary breakpoints such as eight cars.

A long train should be more dangerous even with the same number of crewmates.

These three scaling dimensions must reinforce one another:

**Time × Crew Size × Train Length = Escalating Encounter Pressure**

### D. Preserve monster identity

Every monster must retain its distinctive behaviour, telegraph, counterplay, spawn restrictions that are essential to its
design, and role in the game.

Do not turn every monster into a generic enemy that can appear anywhere.

Instead, improve encounter selection and placement so the director can consistently find appropriate threats without
violating their fundamental rules.

## 2. Audit and correct existing defects

Inspect the latest repository state before implementation. The following issues were identified in a previous code
audit and must be independently verified.

Primary files include:

- `src/DarkTerritory.Sim/Enemies/Director.cs`
- `src/DarkTerritory.Sim/Enemies/Spawns.cs`
- `src/DarkTerritory.Sim/Enemies/EnemyTuning.cs`
- `src/DarkTerritory.Sim/Enemies/Census.cs`
- `src/DarkTerritory.Sim/World.cs`
- `src/DarkTerritory.Sim/Net/Harness.cs`
- `content/tuning/enemies.json`
- Relevant CLI playthrough, trends, and simulation tests

### Issue 1: Excessive solo pressure suppression

The current pressure calculation scales relative to a four-player, eight-car baseline. The existing configuration
includes `crewScale = 0.18`, `lengthScale = 0.06`, `scaleCrew = 4`, `scaleCars = 8`, `scaleFloor = 0.5`. A solo player
with a two-car train consequently receives approximately 0.32× the nominal pressure accumulation rate before other
modifiers.

Replace this with a model that provides a credible solo baseline and scales upward as crew and train size increase.

### Issue 2: Free-spawn crew fade

The current free-spawn system deliberately reduces arrival rates as crew size increases. Existing parameters include
`atGate = 3`, `atEnd = 18`, `crewFade = 0.25`, `crewFadeFrom = 2`, `crewFloor = 0.35`.

Eliminate the negative player-count scaling. Replace it with positive, tunable crew scaling consistent with the design
goals. Do not blindly add the new rate to all existing pressure spawns. Avoid unintended double scaling and maintain one
coherent encounter pacing model.

### Issue 3: Failed spawn accounting

Investigate the lifecycle of `Director.Decide`, `Director.Charge`, `Director.Placed`, `SpawnRule.Spawn`,
`Director.SpendFree`, `Director.SpendStop`. The existing implementation can account for an encounter before the selected
monster has been successfully instantiated. Some placement methods return `false`.

Fix this so failed placements cannot consume pressure, count as successful encounters, trigger a completed-spawn
cooldown, or permanently consume encounter opportunities. Prefer a clear transaction:

1. Choose a candidate.
2. Validate eligibility and placement.
3. Attempt placement.
4. Commit spawn accounting only if placement succeeded.
5. If placement fails, record the failure reason and retry appropriately.

Preserve deterministic random number generation. Also inspect whether multi-member packs are counted correctly: one
encounter may instantiate several creatures.

### Issue 4: Off-screen monsters occupying the threat cap

The existing engaged-cap system sometimes counts monsters that are technically active but not meaningfully interacting
with players. Review the distinction between spawned, alive, dormant, threatening, engaging, player-aware, and resolved
or dismissed.

An unseen monster several cars away should not indefinitely suppress interesting encounters for a solo player. Preserve
fairness limits on genuinely simultaneous active threats. Avoid solving this by indiscriminately despawning persistent
monsters. Cinder Hounds, train parasites, and similar enemies must retain their established behaviour.

### Issue 5: Monster spawn eligibility

Review all director-spawned monsters and their eligibility conditions. Pay particular attention to movement speed
thresholds, minimum crew requirements, minimum train length, facility conditions, ground-player requirements,
coupling-gap requirements, limited terrain or biome conditions, once-per-night restrictions, nightly availability rolls,
and monster-specific concurrent caps.

Identify monsters whose conditions almost never occur during ordinary play. For example, the special Cinder Hound run
currently requires sustained high-speed travel that ordinary Frontier bot play may not achieve.

Make reasonable tuning changes that increase practical encounter opportunities without violating creature identities.
Document any creatures intentionally remaining rare or context-specific.

## 3. Implement an encounter-accountable director

Retain and reuse the existing pressure director and orchestrator wherever practical. Do not introduce a completely
disconnected spawn manager alongside the existing pressure, free-spawn, stop-spawn, and special-event systems. Instead,
establish **one coherent encounter pacing model**.

### Encounter opportunity rate

Implement a configurable encounter-arrival function based on run progression, active crew size, number of train cars,
and route difficulty tier. Use the following as a starting prototype, not an immutable final formula:

λ = (0.12 + (0.33 + 0.035(P−1)) · p^1.2) · (1 + 0.07(P−1)) · (1 + 0.05 · max(0, C−3))

where λ = target encounter opportunities per minute, p = progression from 0 to 1, P = number of active players or
crewmates, C = number of cars.

All coefficients must be data-driven and tunable through `enemies.json` or the established tuning architecture.
Integrate route tier as a separate severity/rate modifier, preserving meaningful progression between Local, Frontier,
Dead Lines, and Deep Territory.

The fundamental mathematical requirements are:

1. Encounter opportunity rate never decreases as run progression increases, given otherwise identical conditions.
2. Adding players increases encounter rate.
3. Adding players increases the strength of time escalation.
4. Adding cars increases encounter rate.
5. Solo play has a meaningful minimum encounter rate.
6. Difficulty scaling remains bounded and respects encounter concurrency protections.

Avoid increasing frequency merely through repeated random rolls with no guarantee of eventually producing an encounter.

### Encounter debt

Introduce a bounded encounter-debt or equivalent pacing mechanism. When the director wants to create an encounter but
cannot find a valid placement, it should remember some portion of that unmet pressure. When a suitable opportunity
becomes available, the director may gradually recover the missed encounter frequency. Requirements:

- Never spawn inside established safe zones.
- Never violate monster-specific structural restrictions.
- Never exceed meaningful simultaneous engagement caps.
- Never unleash a large accumulated backlog immediately after a safe zone.
- Allow debt to decay, be capped, or expire where appropriate.
- Preserve quiet intervals as intentional pacing tools.
- Keep outcomes deterministic for replay and multiplayer.

A temporarily ineligible spawn must not silently erase the game's intended threat progression.

### Encounter selection

Give priority to monsters according to: (1) legal placement and eligibility; (2) current encounter pacing needs;
(3) whether they can meaningfully threaten the crew; (4) monster variety; (5) player locations and tasks; (6) existing
threats and potential combinations; (7) route tier and contextual weighting. Retain existing monster-specific rules,
contradiction pairs, and novelty weighting where appropriate.

## 4. Encounter effectiveness

**Do not optimize solely for total monster spawn count.** The director should evaluate whether spawned monsters actually
create gameplay. Classify encounters into three categories:

- **Level 1, ambient presence:** a Moose grazing near a facility; movement in the trees; an identifiable monster visible
  or audible near the train; a creature occupying a dangerous location. These create anticipation but may not require
  direct intervention.
- **Level 2, active harassment:** Climbers attempting to board; Ribbits approaching someone operating equipment; a
  Hotbox compromising train movement; a creature damaging cargo; a Whistler obstructing a coupling; a monster directly
  pursuing a player. These require decisions, resource use, evasive action, or repair.
- **Level 3, compound crisis:** Hounds attacking while a Hotbox requires maintenance; a Gannet threatening the engineer
  on the roof; a Knotter separating cars while another threat attacks the rear; a facility monster disrupting a
  two-person loading operation; two spatially separated problems requiring the crew to communicate and reprioritize.
  These are the intended high-value co-op horror encounters.

The director should favor increasingly interesting combinations later in the night, subject to fairness and real
monster eligibility. **Important:** Do not introduce fabricated UI notifications or forced artificial encounters merely
to satisfy numerical targets. Measure actual world-state interactions where feasible.

## 5. Spawn placement must be player-relevant

Ensure monsters can threaten relevant parts of the train or facility. For moving trains, consider the locomotive/cab,
forward carriages, middle cargo cars, rear cars, roofs, coupling gaps, and approaching ground and trackside threats. For
stopped trains, consider loading machinery, cargo stacks, switch controls, facility buildings, rail sidings, footpaths
between train and objective, and players separated from the main crew.

Spawn placement should not always target the player's exact position. Threats need believable approaches and
opportunities for perception, communication, and counterplay. For longer trains, encourage a spatial distribution of
threats rather than repeatedly selecting one car or one player. For larger crews, target different operational
responsibilities when valid. Do not teleport monsters into occupied spaces or bypass existing monster telegraphs.

## 6. Fairness and pacing

The goal is increased danger, not unfair instantaneous failure. Maintain monster-specific telegraphs, established escape
or counterplay rules, solo-specific recovery mechanisms, relevant simultaneous-engagement caps, post-crisis recovery
periods, safe departure and arrival areas, and difficulty relief when significant crew members are lost.

However, do not allow safety systems to produce enormous stretches where nothing dangerous or interesting happens.
Distinguish intentional recovery, a monster currently causing a sustained problem, a quiet but appropriately threatening
situation, and a true encounter drought. The director should respond to the last case, not blindly escalate during all
four. An active monster damaging a train system should count as ongoing danger even if nobody is currently fighting it.
Conversely, a dormant unseen creature should not indefinitely satisfy the game's encounter pacing requirements.

## 7. Initial balance targets

Experimental targets for a typical 20-minute full-game night: distinct meaningful encounters, not individual enemy
bodies.

| Session | Desired meaningful encounters |
|---|---:|
| Solo, 2–3 cars | 5–8 |
| 2 players, 4–6 cars | 7–11 |
| 4 players, 8 cars | 10–15 |
| 8 players, 12+ cars | 14–20 |

Do not hardcode these counts as spawn quotas. They are benchmarks for simulation and playtesting. Additional targets:

- First noticeable threat generally occurs within 90–150 seconds after leaving the safe departure area.
- Investigate ordinary mid-run stretches exceeding 90 seconds without a meaningful threat or sustained danger.
- Encounters become measurably more frequent toward the latter half of the night.
- Larger crews encounter more distinct threats and overlapping responsibilities.
- Longer trains encounter more threats than shorter trains under equivalent conditions.
- Monster diversity improves instead of concentrating repeatedly on the same two or three creatures.

Preserve the possibility of occasional unusual nights. Avoid designing perfectly predictable fixed spawn intervals. Do
not force these targets at the expense of basic playability, travel time, or the ability to finish runs.

## 8. Instrumentation and testing

Extend the existing headless simulation and reporting tools rather than building a separate testing framework.

**Encounter telemetry.** Record and report spawn attempts; successful placements; failed placements and their reasons;
spawn channel; monster type; monster count within a spawned pack; encounter location; run time and progress; player
count; train length; active versus dormant monster counts; player proximity or credible perception proxies; engagement
start and end; threat resolution; time since last meaningful encounter; encounter severity; overlapping threat count;
encounter debt; spawn suppression reasons. Distinguish measured facts from inferred awareness. Do not claim a player saw
a creature merely because it was instantiated within a certain distance.

**Simulation matrix.** Crew sizes 1, 2, 4, 8; train lengths 2, 4, 8, 12, 18 cars where supported; routes Local,
Frontier, Dead Lines, Deep Territory; multiple deterministic seeds, including historically sparse seeds and previous
regression cases. Compare the existing director baseline and the new director on encounter frequency, encounter
diversity, spawn placement success, drought length, simultaneous threats, player deaths, cargo survival, completion rate,
and time to complete the run. Aim for at least 30 seeds per primary configuration in the final balance validation, with
representative smaller sweeps during implementation. Do not run the entire expensive matrix after every small change.

**Efficient validation workflow.** (1) Focused tests: fast unit tests for scaling, eligibility, spawn transactions, caps,
and determinism. (2) Representative harness tests: a small deterministic selection of solo, two-, four-, and
eight-player nights. (3) Full regression and balance sweep after the system is stable. Reuse existing test fixtures and
CLI tooling. Avoid unnecessary graphical rendering, screenshot generation, or repeated full-suite runs when
deterministic headless results are sufficient.

**Required assertions.** Automated tests demonstrating:

- Encounter rate is monotonic with run progress.
- Encounter rate increases with player count.
- Late-run escalation becomes stronger with additional players.
- Encounter rate increases with train length.
- Solo sessions generate meaningful monster opportunities.
- Failed spawn placements do not consume encounter pressure.
- Encounters remain within legal safety and fairness constraints.
- Safe zones and terminus protections remain intact.
- Fixed seeds produce deterministic results.
- Host-authoritative multiplayer spawning remains consistent.
- Creature-specific spawn restrictions remain valid.
- A dormant or irrelevant threat cannot permanently suppress encounter pacing.
- Stop encounters and free encounters are accounted for coherently.

Do not weaken existing regression assertions merely to make the new system pass.

## 9. Implementation boundaries

Preserve the existing architecture wherever practical. Do not rewrite every monster's AI or behaviour. Do not change the
networking architecture. Do not break deterministic replay or host-authoritative spawning. Do not create a second
independent threat system that competes with the existing director. Do not remove established monster behaviours,
counters, or telegraphs. Do not increase Cinder Hound frequency indiscriminately: prior simulations show that additional
boarding packs can cause excessive car fires and prevent otherwise viable runs from finishing. Do not solve missing
variety by repeatedly spawning enemies that cannot meaningfully interact with players. Do not make the entire system
reliant on a single magic multiplier. Keep all significant balance parameters editable through existing tuning files.
Where design constraints conflict, prefer player-perceived encounter quality and legitimate counterplay over raw spawn
volume. Follow repository coordination rules, avoid overwriting concurrent work, and use an appropriately isolated branch
or worktree.

## 10. Implementation sequence

- **Stage 1, correctness:** verify the reported defects against current main; fix failed-spawn accounting; fix
  active-threat accounting and inappropriate cap suppression; add focused regression tests.
- **Stage 2, scaling:** establish a proper solo encounter baseline; implement time, crew, train-length and tier scaling;
  remove contradictory negative crew scaling; add deterministic mathematical tests.
- **Stage 3, encounter director:** integrate encounter opportunity pacing into the existing director; implement bounded
  encounter debt; account coherently for pressure, free and stop arrivals; improve placement and selection reliability.
- **Stage 4, tuning:** revisit overly restrictive monster eligibility; improve monster diversity; adjust concurrency and
  overlap for larger crews; protect short and solo crews from impossible combinations.
- **Stage 5, validation:** run focused tests and representative playthroughs; fix observed regressions; run the final
  balance matrix; compare against the existing baseline; document the results.

Keep the work in reviewable incremental commits. If separate PRs are required by the repository's coordination system,
use them.

## 11. Deliverables

1. Implemented code changes, integrated with the existing simulation and game systems.
2. Updated tuning configuration with clearly named, documented parameters.
3. Regression tests covering spawn transactions, scaling, caps, and determinism.
4. Simulation evidence comparing the old and new director.
5. Monster eligibility report identifying monsters that remain rare, gated, or underrepresented.
6. Balance report with encounter counts, diversity, droughts, concurrent threats, survival, and completion outcomes.
7. Documentation updates to the relevant GDD and system specifications.
8. Change summary explaining the design decisions and any remaining limitations.

Do not report a successful result based solely on compilation or unit tests. The implementation must demonstrate
measurable improvement in meaningful monster encounters across player counts and train lengths, without making normal
runs unreasonably punishing.

## 12. Definition of done

- **Solo:** a player operating a short train regularly encounters recognizable monsters throughout a run. The main
  difficulty is no longer simply the clock.
- **Two players:** encounters regularly create reasons to communicate, assist each other, or abandon one job to handle
  another.
- **Four players:** threats naturally overlap across different train areas, creating decisions about who responds to
  what.
- **Eight players:** the director generates more frequent, spatially distributed danger without becoming an
  indiscriminate swarm generator.
- **Long trains:** additional carriages produce measurable increases in threat density and coordination demands.
- **Late runs:** the last half of the night consistently feels more dangerous than the first, without eliminating
  intentional recovery windows.
- **All configurations:** failed spawns, dormant creatures, restrictive eligibility, or disconnected pacing mechanisms no
  longer routinely cause unexplained encounter droughts.

**Final design principle:** the director's responsibility is not to spawn monsters. Its responsibility is to create
compelling, escalating situations that require players to react, communicate, improvise, and make difficult decisions.
Monsters are the means by which it accomplishes that.
