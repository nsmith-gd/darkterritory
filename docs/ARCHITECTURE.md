# Ballast — engine architecture for Dark Territory

Status: **v0.1, proposed**. Decisions below are made to be reversed cheaply until each one is exercised by a milestone in [ROADMAP.md](ROADMAP.md).

Ballast is a small, purpose-built engine for one game. It is not a general engine. Every system exists because the GDD needs it. It is designed so that **an AI agent (Claude Code) does most of the building**, from code and networking to assets, animation and balance, and humans spend their time on design, parameters, level layout and visual judgement.

Design inputs: [design/gdd.md](design/gdd.md), [design/systems-spec.md](design/systems-spec.md), [design/art-direction.webp](design/art-direction.webp).
The systems spec was written against Unity; where it names Unity or FMOD, this document supersedes it (see §6).

---

## 1. Hard requirements (from GDD + director)

| Requirement | Source | Consequence for the engine |
|---|---|---|
| First-person 3D, late-PS2/early-PS3 look | GDD §25–31, art sheet | Small forward renderer; fog, practical lights and post-processing matter more than fidelity |
| **VR** (Quest via PC, SteamVR, other OpenXR headsets) | Director | OpenXR from day one; input is *actions*, not keys; stereo rendering path |
| **8+ players**, host-authoritative P2P, 30 Hz | GDD §33, spec E | Snapshot replication with delta compression and interest management; host is also a player |
| Sync ragdolls, thrown objects, movement, *as much as possible* | Director | Host-simulated physics replicated to clients; bodies are gameplay objects (the Vigil) |
| Proximity voice + radio/walkie-talkie, dead channel, Soot Children mimicry | GDD §21, spec A.5 | Voice is an engine system with DSP; host routes and can sample voice |
| Audio tells as a fairness system | Spec A.1–A.4 | Custom mixer with tiered ducking, measurable offline |
| Steam **and** itch.io | Director | Transport and platform services behind interfaces |
| Windows (+ VR) | Director | Windows is the ship target; Linux must still run headless and render offscreen for agents/CI |
| Mod support | Director | Game content is itself a mod; C# + data mods |
| Simple level editor for a designer | Director | In-engine ImGui editor over plain-text scenes |
| Procedural line + POI module grammar | GDD §22, spec D | Designers author *pieces and rules*; the editor previews generation |
| Headless agent verification harness | GDD §34, App. B.9 | Sim runs without window/GPU, faster than real time, many instances |
| AI-first, autonomous | Director | Everything text, everything scriptable, everything verifiable from a CLI |

## 2. The AI-first rules

These are the rules that make the engine usable by an agent without a human in the loop. They are the reason the engine exists instead of Unity.

1. **All authored data is text.** Scenes, prefabs, tuning, audio graphs, animation graphs and mod manifests are JSON (with comments) in `content/`. No binary scene files, no GUID-soup meta files, no editor-only state. An agent can read, diff, write and review everything.
2. **Assets are built from source by scripts.** Models come from Blender Python scripts run headless (`blender -b --python`) or procedural C# generators. Textures come from procedural generators or AI-generated sources that a script downscales and quantizes. Everything is cooked to runtime formats by a CLI step. A human-made `.blend` or `.png` is just another source file.
3. **Every system is runnable headless.** Simulation, networking, audio mixing (offline render to buffer) and rendering (offscreen, to PNG) run without a window. In cloud containers rendering uses Mesa lavapipe (software Vulkan).
4. **The engine can be driven and inspected from outside.** A `dt` CLI covers batch work (`dt train table`, `dt sim …`, `dt screenshot …`, `dt harness …`). A local JSON-RPC control port on the running game lets an agent query entities, set tuning, teleport, spawn, step, and capture frames.
5. **Fast verification loop.** `dotnet build` + `dotnet test` in seconds. Scenario tests pin the design numbers (see `tests/DarkTerritory.Sim.Tests/SpecTableTests.cs`). Visual changes are verified by golden screenshots the agent looks at.
6. **Humans tune while it runs.** All numbers live in hot-reloaded data files (`HotData<T>`). The editor's inspector writes back to the same files, so designer tweaks show up as diffs Claude can see.
7. **One code path.** Bots, the agent harness, and real players all send the same *intent* input to the same host simulation over the same transport interface.

## 3. Language: C# on .NET 10

Evaluated: C++20, Rust, C#/.NET, Zig/Odin.

**Chosen: C# 14 / .NET 10 (LTS)**, with native libraries via P/Invoke where performance or maturity demands it.

| Criterion | Why C# wins here |
|---|---|
| Agent iteration speed | Incremental builds in seconds; hot reload; excellent Claude proficiency. C++ and Rust compile far slower, and every build is a turn the agent waits for. |
| Agent debugging | Memory-safe by default. Exceptions come with stack traces. An autonomous agent can diagnose a `NullReferenceException` from a headless log. It cannot reliably diagnose heap corruption. |
| Reflection + source generators | Serialization, the editor inspector, network replication and the control port all come from attributes instead of hand-written glue. |
| Mod support | Genre expectation. Lethal Company's mod scene is C# (BepInEx/Thunderstore). Loading mod assemblies is trivial on the JIT runtime, and HarmonyX patching works. |
| Libraries | Mature bindings exist for everything on the list: Jolt, Vulkan, OpenXR, SDL3, Steamworks, Dear ImGui, Opus. |
| Performance | This is a PS2-fidelity game. The costs are physics (native Jolt), rendering (GPU) and audio DSP (SIMD-able C#). GC is managed by pooling and struct-heavy hot paths; .NET's gen0 pauses are sub-millisecond, within a 90 Hz VR budget. |

Trade-offs accepted: we ship the JIT runtime (not NativeAOT) so mods can load; some native interop boilerplate; GC discipline in hot loops.

## 4. Stack

| Concern | Choice | Notes |
|---|---|---|
| Windowing, input, gamepad | **SDL3** (`SDL3-CS`) | Also used for audio device I/O fallback |
| Graphics API | **Vulkan 1.3** (`Vortice.Vulkan`) | Dynamic rendering, no render-pass boilerplate. It is the API with universal OpenXR support (Meta PC runtime, SteamVR, standalone Quest if ever wanted) and it runs headless on Linux via lavapipe for agent screenshots. D3D11 would not run in the Linux containers. OpenGL's OpenXR support is uneven. |
| VR | **OpenXR** (`Silk.NET.OpenXR`), `XR_KHR_vulkan_enable2`, stereo (a pass per eye now, multiview later) | PCVR first (Link / Air Link / Virtual Desktop / SteamVR). Standalone Quest (Android) is a post-launch option that this stack does not rule out. Tested headless on Monado's simulated headset (§8 note 25). |
| Physics | **Custom PBD** (`Ballast.Physics`), Jolt held in reserve | Loose bodies and ragdolls as position-based particles and constraints, living in car frames like players do (§8 note 21). Jolt (`JoltPhysicsSharp`, verified to restore and run on Linux) stays the option if we need true rigid-body stacking. |
| Train | **Custom 1D rail sim** (`DarkTerritory.Sim/Train`) | Cars live on the rail spline, driven by longitudinal dynamics, and appear to Jolt as kinematic bodies. Already implemented and pinned to the spec. |
| ECS | **Friflo.Engine.ECS** or **Arch**, decided in M1 | Needs: fast queries, struct components, and component change tracking for replication |
| Audio | **Custom mixer** in C# + **miniaudio** device I/O + **Steam Audio** (HRTF, occlusion) | See §6 |
| Voice codec | **Opus** (libopus via P/Invoke, `Concentus` as pure-C# fallback) | |
| Networking | `ITransport`. One protocol (`DatagramTransport`) over **UDP** for LAN and direct IP, and over **Steam's relayed P2P** (`ISteamNetworkingMessages`, Steam Datagram Relay) with Steam lobbies and invites (`Ballast.Online`, Steamworks.NET). **Epic Online Services P2P** will follow for itch.io, and **loopback** serves tests and bots. | EOS gives NAT-punch + relay + lobbies free, no servers for us to run, and no Epic account needed (device-ID login). §8 note 24. |
| UI / editor | **Dear ImGui** (`Hexa.NET.ImGui` + ImGuizmo) for editor and debug. A small custom retained UI for diegetic and in-game UI. | |
| Models | **glTF 2.0** is the only interchange format | Blender scripts export glTF; the cooker converts to runtime meshes |
| Animation | Skeletal, GPU skinning, text-defined blend graphs, two-bone + look-at IK, VR body IK | Clips come from free mocap (CMU, etc.) retargeted in Blender, from procedural motion, and later from AI text-to-motion |

## 5. Module layout

```
src/
  Ballast.Core        math, clock, logging, data files + hot reload, jobs        (exists)
  Ballast.Net         ITransport, loopback, conditioner, snapshot/replication    (exists: transport)
  Ballast.Platform    SDL3 window/input, action mapping (flat + XR share actions)
  Ballast.Render      Vulkan forward renderer, offscreen capture
  Ballast.XR          OpenXR session, swapchains, action sets, hand poses
  Ballast.Physics     Jolt wrapper, ragdoll builder, character controller
  Ballast.Audio       mixer, buses, ducking, DSP, synthesis, Steam Audio
  Ballast.Voice       capture, Opus, VAD, routing graph (proximity/radio/dead)
  Ballast.Online      Steam + EOS: lobbies, identity, invites, achievements
  Ballast.Editor      ImGui editor, inspector, rail and module tools
  Ballast.Assets      asset cooker (glTF → runtime, texture quantize)
  Ballast.Modding     mod discovery, manifest, load order, host mod-list check
  DarkTerritory.Sim   shared simulation, host + client identical, no platform deps (exists)
  DarkTerritory.Game  presentation: rendering the sim, UI, audio hookup
  DarkTerritory.App   executable (game + editor + dedicated/headless host)
  DarkTerritory.Cli   `dt` tool                                                   (exists)
content/              all game data (the "base mod")
tools/blender/        Blender Python asset scripts
tests/
```

**Dependency rule:** `DarkTerritory.Sim` may depend only on `Ballast.Core`, `Ballast.Physics` and `Ballast.Net`'s message types. It never touches rendering, audio devices or platform services. That keeps the headless harness fast and host/client divergence impossible.

## 6. Key technical decisions

### 6.1 Moving reference frames (the hardest problem in this game)
Players walk, fight and throw crates on cars moving at 22 m/s around curves, 40 km from the origin. Approach:
- **Entities have a parent frame**: world, or a specific car. Positions are stored and replicated *car-local*.
- ~~Jolt runs in a floating origin centred on the train, with cars as kinematic bodies.~~ Superseded: loose bodies use the same car-frame treatment as players (§8 note 21).
- Character controllers resolve in car-local space while grounded on a car. On leaving it (jumping, falling, being dragged off) they inherit the car's world velocity, which is how the lethal jump-off happens with no special-case code.
- Voice, audio and AI all query positions through the same frame system.
This gets prototyped in M1 alongside the 4:1 speed-ratio feel test (spec G.1).

### 6.2 Netcode (GDD §33)
- Host-authoritative, **30 Hz** tick, host is a player. No host migration (spec E).
- Clients send **intent** (actions + aim + VR head/hand poses), sequence-numbered. The host clamps `dt` and validates VR poses against reach limits.
- Client-side **prediction + reconciliation** for the local character. Remote entities are **interpolated ~100 ms** behind.
- **Physics objects** (thrown items, cargo, ragdolls, bodies) are host-simulated. A client that throws predicts its own throw locally, then blends to host state. Ragdoll poses replicate as quantized bone rotations at a reduced rate, with priority by distance and visibility.
- **Snapshot delta compression** against the last acked snapshot, with **interest management** by distance *along the train* and by facility zone. At 20 cars and 8+ players, not everyone needs everything every tick.
- **Lag compensation** for hitscan turret fire (host rewinds remote entities).
- Budget target: ≤ 64 kbit/s down per client typical, 128 kbit/s peak, excluding voice.

### 6.3 Voice (spec A.5)
- Capture → VAD → Opus (20 ms frames) → **to host** → host forwards only to listeners in range or on a shared channel (star topology, which saves bandwidth at 8+ players).
- Receiver-side DSP: distance falloff (full 0–8 m, log 8–26 m, cutoff 26 m), occlusion (−12 dB + 900 Hz lowpass through car walls), radio channel (300–3 kHz band-limit, static floor, dies in tunnels), dead channel.
- Because the host sees all voice, it can keep a rolling per-player buffer for **Soot Children** mimicry (formant-shifted, no distance falloff) and detect "never speaks" for **The Passenger**. Both come directly from the architecture, as the GDD intends.
- Voice data rides the same transport on its own unreliable channel.

**Status (T14).** The whole path exists and is measured headless (`dt voice bench`, `VoiceTests`).
- **Encode:** `Ballast.Voice` does Opus through Concentus (pure C#; ~60 bytes per 20 ms at 24 kbit/s), with packet-loss concealment for gaps, voice activity with a hangover, and a 60 ms reorder buffer. Without the reorder buffer, a jitter of ±20 ms on 20 ms frames cost 3.6 dB of speech to conceal-and-drop.
- **Route:** `VoiceRouting` on the host decides proximity (with a 4 m forwarding margin past the 26 m cutoff), cab-wall occlusion, radio (not into or out of a tunnel) and the dead channel.
- **Play:** the receiver plays each path through the mixer as a stream voice on tier 2. Proximity uses spec A.5's log curve as a mixer rolloff mode. The radio is `voice-radio.json`: flat, 300 Hz–3 kHz, crushed, with static while keyed.
- **Measured:** 4 m against 16.5 m is 8.3 dB, matching the curve. Past 26 m nothing is sent. The radio at 93 m keeps its band, with the low end 48 dB down. Talking ducks the bed by exactly −6 dB. On a 90 ms ±20 ms link with 5% loss, level stays within 1 dB of a perfect link.
- **Not yet:** Soot Children mimicry, a per-player rolling buffer on the host, occlusion by car walls once interiors exist, a radio as an item (for now everyone carries one), and Steam Audio HRTF.

### 6.4 Audio (spec A) — why not FMOD
FMOD's power lives in FMOD Studio, a GUI authoring tool whose projects an agent cannot sensibly author or verify. The spec's needs are specific and small: six tiered buses with sidechain ducking, parameter-driven layered events, voice limiting, positional slack-action delay chains, and heavy procedural synthesis. We build this in C#:
- **Sound graphs as data** (`content/audio/*.json`): layers, parameters, DSP chains, synthesis nodes (filtered noise, resonators, granular, mass-spring, formant).
- **Tier ducking** exactly per spec A.3. Tier 1 is never ducked or occluded beyond −6 dB.
- **Offline render**: the mixer can render any sim state to a buffer. The harness then *measures* tell audibility against the bed, which makes spec A.3's "tier 1 is inviolable" an automated test.
- Steam Audio for HRTF (spec A.4: ~30° localisation, and essential in VR) and geometric occlusion.

**Status (T12).** `Ballast.Audio` exists. It has no device dependency, so the same code renders to the speakers and offline.
- **Sounds:**
  - Layered synthesis nodes: noise, sine, saw, square, impulse.
  - Biquad filter chains, tremolo and gates with jitter, vibrato, envelopes and bit-crush.
  - Every number can be a curve over a live parameter.
- **Buses:** six tier buses, with the ducking rules in `content/audio/mix.json`.
- **Spatialisation:** distance rolloff and equal-power pan with a small rear cut, plus occlusion. Tells are floored at −6 dB.
- **Voices:** per-sound instance limits with stealing, and a 64-voice budget. Past the budget, voices virtualise, but tells always render.
- **Game hookup (`DarkTerritory.Game.Sound.GameAudio`)** drives it all from world state, so clients hear what the host does:
  - the bed follows speed, pressure, fire, throttle and brake;
  - slack action runs down the consist one coupling at a time;
  - the tells follow enemy phases;
  - the Choir adds voices and closes in as aggro climbs.
- **Output:** device output is an SDL3 audio stream. miniaudio isn't needed yet, and Steam Audio's HRTF comes with VR.

### 6.5 Rendering (art direction)
Forward+ renderer, deliberately limited:
- Point-sampled 256–512 px textures with mips; optional internal resolution scale with nearest upscale for pixel crawl (off or reduced in VR for comfort).
- Per-pixel lighting from a few practical lights (lanterns, headlamp, furnace), shadow maps only for key spot and point lights, baked vertex AO.
- **Height fog + low-res froxel volumetric fog**, the core of the look. Smoke and steam particles, bloom, film grain, ordered dither, colour grading LUT to the sheet's palette.
- Offscreen capture path for agents: `dt screenshot --scene … --camera … --out shot.png`.
- VR: multiview single-pass stereo, 90 Hz target. The PS2-level poly budget makes this easy.

### 6.6 Level editor
The world is mostly **generated** (GDD §22: the line is generated per run; spec D: POIs are assembled from modules). So the designer mainly authors **pieces and rules**:
- **Module editor**: place prefabs, snap to grid and rail, set properties via a reflection inspector, mark interaction points (switches, ladders, chutes), spawn points and light and fog volumes.
- **Rail tool**: draw spline track with grade and curve read-outs, switches, sidings.
- **Generator preview**: pick seed, tier and consist length, then *generate the line / POI and fly through it*.
- **Tuning panels**: live sliders over `content/tuning/*.json`, written back to disk.
- **Play from here**: drop into the scene as a player (flat or VR), with bots.
Scenes save as stable-ID, sorted, one-entity-per-block JSON so diffs stay readable to humans and agents.

**Status (T18).** `dt edit` serves the first two pieces as a **local web page**, rather than ImGui inside the game (§8 note 22):
- **Tuning panels:** every value in `content/tuning/` and `content/audio/`, with the comment above it as the help text.
- **Generator preview:** generate any tier and seed, see the plan and the elevation profile, edit the features, and save a named route. The game plays it with `--route-file name`.
The module editor, the rail tool and "play from here" are still to come.

### 6.7 Modding
- `content/` is the base mod. Mods are folders (`mods/<id>/mod.json`) that add or override data and assets, with an optional C# assembly that gets a documented `IModEntry` API.
- The host broadcasts its mod list and hashes. Clients must match (or auto-download from Steam Workshop, post-launch).
- Code mods are unsandboxed, the same as the genre norm. We say so clearly in the UI.

### 6.8 Agent harness (GDD §34, App. B.9)
- `dt harness` runs host + N bot clients in one process over `LoopbackNetwork` with `LinkConditions` (lag, jitter, loss), faster than real time, headless.
- Bots are **in-engine agents that emit player intent**, never privileged sim access. So they exercise the same code paths as humans.
- Sweeps (enemy pairs/triples × hazards × crew 2–8 × consist length) are parallel processes and produce JSON reports. Fairness invariants (telegraph precedes commit, caps, dependency spawns, pacing shape) are asserted per run.

### 6.9 Coordinates and units
Metres, seconds, kilograms (tonnes and kN in the train sim, so the spec's numbers read directly), right-handed, **+Y up**, **−Z forward** (glTF convention). `double` for distance along the line and for world anchors, and `float` for everything local.

## 7. What is deliberately *not* in scope
Terrain sculpting tools, a node-graph material editor, a general-purpose visual scripting system, console platforms, host migration, dedicated server hosting (we support a headless host binary for testing, but the ship model is player-hosted).

## 8. Open technical questions
1. **Standalone Quest** (Android) at launch, or PCVR only? PCVR is assumed. Standalone doubles the performance and platform work.
2. **Crossplay between Steam and itch.io players?** It is possible via EOS for both, at the cost of Steam-native lobbies and invites. The current assumption is two separate pools.
3. **Brake fade on the flat**: spec B.5 is ambiguous. The sim applies fade only on descents, because otherwise the spec's own 20-car stop (63 s / 690 m) can't happen. See `content/tuning/train.json` → `brakeFade.onlyOnDescent`.
4. **"8+ players"**: the design says ideal 3–4, max 8. The net budget above targets 8 with headroom to 12. Beyond that needs a decision about train length and voice zones.
5. **Jump-off speed is measured over the ground**, not as train speed alone (`PlayerMotor.Land`). Running off sideways adds to the train's speed, so sprinting off a yard-speed train is lethal while walking off is a roll. Conversely, running rearward along the roof before jumping could let a player survive a slightly faster train. That's emergent and arguably fair ("your speed over the ground"). The alternative is to test train speed only; it's a one-line change if the director prefers the simpler rule.
6. **End ladders**: not in the GDD. Cars have ladders on both end faces beside the coupler, so a player who drops into a coupling gap can climb out. Without them the gap is a one-way trap. That may be desirable later (Rattle), but it should be a design decision, not a geometry accident.
7. **Boiler model** (`content/tuning/boiler.json`). The spec gives burn rates by consist length, but one engine burns coal the same way however long its train is. The model explains the difference with **steam heating per car**: longer trains draw more steam to keep the cars warm. The fire's **draft comes from the exhaust**, so a standing engine burns slower, which produces the spec's slow Vigil rebuilds (40 s at 3 cars, over 3 min at 20). Fitted constants reproduce B.6's 20/12/8 s per unit and 133/80/53 min endurance; `BoilerTests` pin them.
8. **Safety valve** (not in the spec). It lifts at the 95 redline and sheds 2.5 pressure/s. Without it an ordinary overfired short train ruptures. With it, a rupture needs something the valve can't keep up with (the Stoker, a jammed valve). The lifting valve is loud: a free tell.
9. **Rolling and air resistance** (not in the spec). The spec's accel and brake tables are treated as net performance, so full throttle and full brake reproduce them exactly. Resistance shows only when coasting: a dead boiler coasts for kilometres rather than forever.
10. **Hollow trigger.** The spec says both "pressure below 40" (B.6) and "firebox temperature below threshold for 45 s" (App. B.5). The sim times a low *fire* (`Boiler.LowFireSeconds`, firebox under 20%), because that's what the crew controls directly. Pressure below 40 is also exposed.
11. **Engine layout.** Boiler forward, a walkable cab (the firebox, vent and controls are in it, and only players in the cab can drive), and a coal tender behind at 3.4 m. The tender is lower than a car roof (4.0 m) and higher than a jump reaches, so getting from the engine to the cars means dropping into the gap and taking an end ladder.
12. **Rakes and couplings.** The train is a set of rakes on the line, each with its own speed. Vehicles keep stable ids, so players standing on cut cars stay on them. The rules:
    - Rakes that touch closing at 1.5 m/s or less couple automatically.
    - Above 1 m/s, contact damages the two end vehicles, and some of that damage spreads to all the cargo in both rakes.
    - Cars cut at a standstill get their handbrakes wound on; cars cut at speed roll free.
    - Cutting takes 1.5 s, or 4 s while the engine is working (spec F.3's "uncouple under load" upgrade implies it's harder).
    - Holding Use while pushing toward a ladder grabs it. Holding Use standing still works whatever you're at: firebox, valve, coupler plate, brake wheel.
13. **Run length vs. route length (spec B.8, needs a design call).** The spec's dawn timer (route ÷ 11 m/s + 18%) on its own 18–40 km routes gives 32–72 minute nights. The same table's "total run 28–45 min" can't hold: 40 km at cruise alone is 48 min, before 3–5 facility stops. The generator implements the dawn formula as written (`content/tuning/route.json`). Either the deep-tier routes shorten, or the total-run target moves to about 35–75 min.
14. **Generated routes** are pure functions of (tier, seed) through PCG32, so the host only needs to send the seed. Facilities, the yard and the terminus approach sit on level straight track. Tunnels, bridges and junctions never overlap. Sleepers and Grease are level content, placed at generation (App. B.2). A long night always gets a coaling tower.
15. **Guns and the Choir.**
    - **Guard car:** with two or more cars, the last car is the guard car. It counts as one of the cars, matching spec F.3's "costs a cargo slot".
    - **Gun positions:** the forward gun is on the cab roof, reached by a hatch ladder from the cab; the rear gun is on the guard car roof. Both are exposed positions.
    - **Aiming:** standing at a gun makes your view its aim.
    - **Arcs:** 200° traverse, a 20° dead zone along the body, and the train's own cars stop rounds. A test tries every aim from both guns at targets beside every middle car of a 10-car train and hits none: the flank really is uncoverable.
    - **The Choir** is a global value: +1.5 per round, decaying only after 45 s of silence.
16. **Enemies (GDD Part Six, App. A, App. B).**
    - **One spine for every enemy:** Dormant → Alert → Telegraph → Commit → Punish or BreakOff → Gone. `Enemy.Enter` refuses Commit unless the enemy has telegraphed for at least `minReactionSeconds` (1.5 s). App. A.1's fairness rule is therefore structural rather than a convention, and the harness counts any violation.
    - **Host-only:** enemies run on the host. Clients mirror them from Enemy records and don't predict them.
    - **Demo roster, one per pressure zone:**
      - Sleepers (forward), Cinder Hounds (rear), Clingers (flank), the Hollow (interior).
      - The Choir (structural) is the global aggro value from note 15. At Swarm it hurts everyone exposed.
    - **Sleepers are level content.** They come from the route's Sleepers features and aren't spent from the director's budget. A train over 40 km/h (11.1 m/s) derails. Over 20 km/h it takes heavy damage.
    - **Sleeper-safe cruise.** This makes spec B.3's 14 m/s cruise the real speed limit on any line with Sleepers:
      - From 14 m/s, even 20 cars brake below 40 km/h inside the lamp's 120 m (104 m of braking).
      - From 22 m/s, only a 3-car train can.
      - The hounds' sustainable speed is 19 m/s, so a train driving the safe speed can't outrun them. Only the rear gun answers them. That's the GDD's intended pairing (§21), and the numbers deliver it.
    - **Hound break-off.** App. A.3 says hounds break off under "sustained rear gun fire". Read as: 9 rounds in 4 s from a gun within range of the pack turns running hounds away, dead or not (`cinderHounds.suppressRounds` / `suppressWindowSeconds`).
      - So suppressing costs about as much Choir aggro as killing the pack (13.5 against about 18), and is more reliable.
      - Rounds fired from out of range don't count, so spraying from 180 m still brings the swarm.
    - **Boarded hounds** hold the rear roof. They can't be shot from the gun beside them. They bite anyone within 4 m and drop off after 6 s with nobody within 12 m. They become a moving interior threat once cars have interiors.
    - **Cover from the Choir** is anywhere that isn't exposed. Today that's only the cab floor (`Surface.Deck`), because cars have no interiors yet. A guard-van interior is the obvious next piece of cover.
    - **The director** follows App. B.1:
      - Budget = tier base × (1 + 0.15 per car beyond three) × a crew multiplier (0.7 + 0.12 per player, capped at 1.6).
      - The budget is spent against the 15/45/40 curve, split at the first and last facility.
      - Hard caps: 2 per zone, 4 overall (6 with six or more crew).
      - 90 s of grace at the gate, a 30–60 s trough after each spawn, and silence in the final 500 m.
      - The Hollow is condition-triggered and is charged only when it actually comes.
    - **A whole night in the harness** (`dt harness --route frontier:7 --enemies --bots 8 --cars 10 --seconds 2400`): 26.9 km yard to terminus with the director spending 105 of its 230 budget (19 hound packs, 24 Clingers), zero fairness violations, no derailment, no deaths, cargo intact. Bandwidth is 40 kbit/s down per client with enemies. What it took from the bots is what it will take from players:
      - The conductor holds cruise with the brake on descents. Coasting downhill to 22 m/s put the train into Sleepers it couldn't stop for.
      - The gunner fires only in range, and holds fire once another burst would bring the swarm, until the Choir has dropped below its approach. Firing again sooner resets the quiet clock and the aggro never falls.
      - Everyone walks away from boarded hounds, and roof walkers go and prise Clingers off.
    - **Still open from that run:**
      - The gunner fired all 200 rounds. Ammunition needs a resupply (facilities).
      - Five Sleeper hits at braking speed each took 0.4 of the engine's integrity, and an engine at zero integrity does nothing yet.
17. **Audio mix (spec A).**
    - **What's measured.** `dt audio render --listener all` renders a staged moment from the cab, the Clinger's car, mid-train and the guard car, and measures each tell's level in its own spec A.4 band against everything else. It counts only the moments the tell is sounding.
    - **Who has to hear what.** Only the players who need a tell are held to it:
      - the cab, for the Sleepers and the Hollow;
      - the guard car, for the hounds;
      - the Clinger's own car, for the Clinger;
      - everyone, for the Choir.
    - **The test.** `AudioTests` requires +6 dB over the bed in maximum chaos: 20 cars at 22 m/s, surging regulator and brake, the valve lifting, the gun firing, the Choir in full swarm, and every tell at once. The tuned sounds clear it by 8–18 dB.
    - **The spec's own band table collides.** The Choir (300 Hz–4 kHz) spans the hounds (500 Hz–3 kHz) and the Sleepers (400 Hz–2 kHz).
      - Resolution: the Choir ducks 8 dB whenever a howl or a writhe is sounding (`soundDucking` in `mix.json`).
      - Loops (the drill, the Hollow) don't duck it.
      - Even so, with everything at once, a full swarm still roughly equals a howl at the guard car. Tell against tell is reported, not required.
    - **Ducking for tiers 3 and 4** isn't in the spec's table, which gives numbers only for tiers 1 and 2. They duck the tiers below them gently (−3 dB and −1.5 dB).
    - **Distance behaviour.** Tells carry further than 1/d: the howl rolls off at 0.7 and the Choir at 0.6, because "distant howl, closing" and "audible singing far off" have to be heard at 150–250 m.
18. **UDP transport and host/join (T13).**
    - **What it is.** `UdpTransport` is the first real network backend: LAN and direct-IP play, for playtests and itch.io builds before Steam and EOS. It uses one non-blocking socket, polled once per tick with no threads. (Since T20 the protocol below is `DatagramTransport`, shared with Steam: note 24.)
    - **Handshake.** The client repeats Connect with a magic number, version and nonce. The host Accepts with a peer id and a random session token, and every later datagram carries that token.
    - **Channels.**
      - Unreliable is one datagram per message.
      - Reliable-ordered uses sequence numbers, with a cumulative ack piggybacked on every datagram, resends after 1.5 round trips (80 ms minimum), and a reorder buffer.
      - Pings every second keep the link alive and measure the round trip. 8 s of silence is a timeout.
      - There's no fragmentation: a message must fit 1200 bytes, and the transport refuses larger ones loudly. Snapshots are about 50–110 bytes.
      - IPv4 only for now.
    - **Found by the tests:** an empty datagram reads as zero bytes available and wedged the receive queue, because the socket was checked with `Available`. It's now checked with `Poll`, and a fuzzing test sends junk at the host.
    - **The host is a client too.** `NetPlaySession.HostGame` runs `HostSession` and joins it over localhost through an ordinary `ClientSession`. There's no host-only code path, and the host gains no latency advantage beyond the loopback. Its player boards first, so it takes the cab.
    - **Joining.** The Welcome now carries a `SessionSetup` (route spec or line, car count, enemies on or off) as JSON. A joiner waits for it and then builds the identical world, and route generation is deterministic from the seed.
    - **Content must match.** Tuning is loaded locally on every machine. Different content between host and joiner shows up as prediction corrections, not a desync, because the host is authoritative, but it should be refused. **Next:** the host sends a content hash in the Welcome, which the mod loader needs anyway (spec: host mod-list check).
    - **Networked cab controls.** The throttle, brake and reverser go through intent (`CabControls`) and work only in the cab, as the host enforces. The single-player prototype still drives from anywhere for the feel test.
19. **Car interiors (T15).**
    - **Walk-in cars.** Every car behind the engine is a shell:
      - a floor level with the coupler plate (1.1 m), walls, and a roof slab whose top is the same 4.0 m walkway, so spec B.4's roof traverse is unchanged;
      - a door in each end wall, left of centre, with the end ladders on the right;
      - cargo stacked down the right-hand side of cargo cars.
    - **The guard car** (GDD §10: "rear gun, tool storage, the back door") has a tool locker and a hatch ladder up to the rear gun from inside.
    - **Doors are state.** They're a replicated bitmask on each vehicle and start shut. You open or shut one by holding Use while facing it, from inside or from the coupler plate outside.
      - Facing matters because the plate is in reach of two doors, and Use on the plate also cuts the coupling.
      - Door reach (0.75 m) covers only the plate's ends, so the middle of the plate is where you cut.
    - **Ceilings stop heads.** A jump indoors used to put your head into the roof slab, which pushed you sideways out through the wall. Now it stops you at the ceiling.
    - **Protected versus exposed (GDD §26) is one rule, `PlayerMotor.Space`.** You're in a space if you're in the cab (always) or inside a car with every door shut. A car with a door open counts as outside.
      - **The Choir swarm** hurts anyone outside.
      - **Voice** between different spaces is occluded.
      - **Sound** from outside your space is occluded, except a Clinger on your own car's hull and the Hollow in your cab.
    - **Lighting.** Each car has two lamps and the firebox lights the cab, as per-vertex practical lights in `MeshBuilder`, PS2 style, with long interior surfaces sliced at 2 m so the light has vertices to land on. There are no shadows, and outward-facing surfaces take no light from inside.
    - **Not yet:**
      - bots that take cover when the Choir approaches (the gunner's fire discipline keeps it off them for now);
      - boarded hounds hunting inside;
      - the side doors of freight cars;
      - Soot Children at the door.
20. **The run (T16).**
    - **One night, host-authoritative.** `Run` lives in the sim and clients mirror it through a Run record.
      - **Departure:** the run and the dawn clock start when the engine passes the yard gate, the end of the 600 m fortress yard.
      - **Arrival:** stopped with the front within 400 m of the end of line, the train is home.
      - **Failure:** a derailment, the whole crew dead, or still out 120 s after dawn. GDD §8 says the main line reopens at dawn; the grace before "the railway itself becomes a threat" is `run.json` → `dawnGraceSeconds`. Dawn itself is a warning.
    - **Pay (spec F.1):** tier value × load × cargo integrity, for cargo cars still in the engine's rake ("everything still attached to the locomotive counts").
      - **Running costs** are what the night actually burned and broke: coal, rounds and repairs, priced so a competent night lands near the spec's 15% of gross.
      - **The whole-night harness** delivered 9 of 9 cars on a Frontier 10-car night for exactly spec F.1's table figure of 6,300 gross, at 8.7% costs, because the bots lost nothing.
      - **Crew home** means alive and aboard, or within 40 m of the train. Someone mid-jump between roofs at the gates still counts.
    - **The coaling tower (GDD §18, spec D.2 gravity chute).**
      - Stopped at the facility, someone on the ground holds Use at the lever by the tower, and the chute opens.
      - It pours 12 units/s from a 260-unit hopper whether you're ready or not. Coal lands in the tender if it's within 2.5 m of the spout, and on the ballast if not.
      - Overfilling damages the engine. Pulling the lever again shuts the chute.
      - The ground isn't part of the train, so the lever keeps its own hold timer rather than using `ActionProgress`.
    - **Not yet:**
      - the other facilities' modules (spec D) and loading cargo at them;
      - ammunition resupply;
      - the fortress departure phase (contracts, purchases);
      - autosave;
      - the Vigil (spec C.2);
      - an oncoming train as the dawn failure, rather than the run just ending.
21. **Physics: position-based dynamics, not Jolt (T17).**
    - **Why.** Bodies on a train at 22 m/s, 40 km out, round curves, have exactly the player's problem: resting on a car they must be still in its frame, and in flight they must carry its velocity. A kinematic-car rigid-body engine gets there through friction against fast-moving colliders and a rebased floating origin. PBD in the car's frame gets there directly.
      - A crate on a roof at 22 m/s doesn't move (tested to 5 cm over 10 s).
      - One thrown off the side flies on with the train's speed, lands, and is left behind (tested).
      - It's pure C#, deterministic, and its state is positions, which quantise and delta-compress like everything else.
    - **Bodies (`Sim/Physics/Bodies`)** are particles and distance constraints.
      - **Crates and lamps** are one particle each, with a drawn yaw that tumbles in flight.
      - **The dead** are an 11-particle ragdoll (head, chest, pelvis, elbows, hands, knees, feet, plus soft braces). It spawns with the player's velocity and a backwards tip, so bodies fall over rather than fold into a pile.
    - **Frames.** A body lives in the frame of the car it touched, or the world's after three steps touching no car.
    - **Physics.** Gravity plus the car's own braking or pulling, felt as a pseudo-force. Sleep after half a second still. A hard stop wakes everything aboard.
    - **Hands.** Press E near a body (nothing else in reach) to pick it up. A crate rides at your hands; a body hangs from its chest and drags. E puts it down; right mouse throws it along your view (9 m/s, a body 4 m/s) on top of your own motion. The host resolves all of it; clients mirror the Body records.
    - **Spec C.** A body persists where its player died, can be carried, and one brought home aboard is "revived free at the gate" (RunReport.RevivedAtGate).
    - **Not yet:**
      - body-to-body collision: crates pass through each other, so the guard van stocks them side by side;
      - true rigid-box contact: crates are spheres to the world and cubes to the eye;
      - the thrower predicting their own throw (it shows on the host's timeline, 100 ms interpolated);
      - bodies on the Choir's list, and the Vigil itself.
22. **The editor is a local web page, not ImGui (T18).** `dt edit` runs a small HTTP server on 127.0.0.1 over `content/`, and the designer uses a browser next to the running game.
    - **Why.** A designer-first tool needs forms, tables, maps and help text. A browser has all of that today, while ImGui needs a Vulkan UI path (textures, fonts, input) the renderer doesn't have yet. The page is plain HTML and JS embedded in `DarkTerritory.Editor`, and an agent can drive it and screenshot it headless (`dt edit --screenshot`, via Playwright).
    - **Comments survive.** Tuning files carry their rationale and spec citations in comments, so an edit replaces only the characters of the value (`Ballast.Jsonc`, tested on every shipped content file). A serialise-and-write round trip would have thrown the comments away.
    - **Safety.** An edit is validated against the game's own record type before it's written, so the running game never hot-reloads a file it can't load. Only known content files are writable, and routes save only under a plain name.
    - **In-game UI** (the HUD beyond the window title, menus, VR panels) needed text rendering in the engine, which came separately (note 27); the editor didn't wait for it.
23. **Session rules: interest, drop-out, content, drop-in (T19).**
    - **Interest.** Each client is sent the train, the players, the run and the world in full, but enemies and bodies only within the interest radius of their own player, plus anything they're carrying. Each client has its own delta baselines, so a record that leaves someone's radius is simply absent from their next snapshot and removed on their side.
      - **The radius is 520 m** (`enemies.json` → `interestRadius`). An enemy that isn't on your machine can't play its tell there, so the radius must cover the farthest one, the hound howl's 500 m. `AudioTests` holds every enemy tell's range to it. The Choir's voice comes from the world record, which always goes.
      - **Measured:** at 20 cars, 8 bots, 10 minutes of Frontier with enemies, down per client falls from 52 to 39 kbit/s (budget 64). Most of that is the route's dormant threats up the line.
    - **Drop-out (spec E).** A player who disconnects alive leaves an inert body where they stood: the same ragdoll a death makes, carryable and revivable at the gate.
    - **Content hash.** The host puts a hash of every `content/tuning/*.json` (line endings normalised) in the session setup. A joiner whose files differ is refused before building a world, with the files named ("your content differs from the host's: tuning/train.json"). Mods will need the same check over their own files; routes are already sent as a spec the joiner rebuilds.
    - **Drop-in at POIs (spec E).** Joiners are welcomed straight away but boarded only in the yard, stopped at a facility, or at the terminus. Between stops they get a Wait message (the HUD shows "WAITING: …") and board at the next stop on the ballast beside the engine, "like a pickup". A session with no run (the free-play line) boards anyone at once.
24. **Steam: one protocol over every carrier, lobbies as meeting places (T20).**
    - **One protocol.** The handshake, reliable channel, pings and timeouts from T13 are now `DatagramTransport<TAddress>`, over any `IDatagramCarrier`. `UdpTransport` is that protocol over a socket. `OnlineTransport` is the same protocol over a platform's relayed datagrams, addressed by user id.
    - **Why not Steam's own connections and reliability?** A Steam game and a direct-IP game are then the same game on the wire, tested once. The harness, the netcode tests and the prediction numbers all carry over, and EOS P2P (itch.io) will be one more carrier.
      - The cost is a few bytes of header that Steam's own channel wouldn't need.
      - Messages go out unreliable with no Nagle delay, and Steam reopens a broken session quietly.
    - **Lobbies (`Ballast.Online.Lobby`).** The host opens a friends-only lobby and writes the game name, protocol version and host name into it.
      - A friend arrives by an invite (F2 opens Steam's picker), by "Join Game" on the friends list, or by launch with `+connect_lobby <id>`.
      - The joiner refuses a lobby on another protocol before connecting, then connects to the lobby's owner. The content hash (note 23) is checked in the Welcome, as over UDP.
      - Everyone stays in the lobby while playing. That's what makes "Join Game" work, and it's the rule the host uses to accept P2P sessions: only lobby-mates, or people it has sent to first. A stranger who knows your SteamID gets nothing.
    - **The host is on several transports at once (`HostGroup`).** Steam for friends; UDP for its own player on localhost, and for LAN joiners if `--host` is also given. `HostSession` sees one crew.
    - **Invites mid-game.** An invite accepted while playing ends the game and relaunches with `+connect_lobby`, the same path as an invite accepted from outside. It's simple, and it means one code path.
    - **Tested without Steam.** `FakeOnline` is an in-process platform with accounts, lobbies (limits, closing, hand-off when the owner leaves), invites and datagrams that follow the lobby-mates rule.
      - `LobbyTests` cover invite → join → connect, the stranger refused, full/closed/gone lobbies and version refusal.
      - `NetPlayTests` cover a friend joining a hosted game through a lobby.
      - `dt harness --online` puts 8 bots in the host's lobby as separate accounts: prediction exact to 0.1 mm, 28 kbit/s down.
    - **Steam itself.** `SteamBackend` is a thin mapping onto Steamworks.NET 2024.8 (SDK 1.60), and `dt online check` says whether Steam is reachable and who's signed in.
      - It needs Valve's `steam_api64.dll` from the SDK in `external/steam/` (README there), and a running Steam client. Without them the game says so and carries on over UDP.
      - It uses app id 480 (Valve's test app) until the game has its own.
      - **Not yet run against real Steam:** the CI containers have no Steam client. The first two-account test on real machines is the next step for it.
    - **Not yet:** EOS for itch.io; rich presence; closing the lobby when the crew is full; a lobby browser; reconnecting to a host after a drop.
25. **VR foundation: OpenXR sessions, stereo, and a headset in CI (T21).**
    - **`Ballast.Xr`.** `XrHeadset` finds the runtime and the headset, and makes the game's Vulkan instance and device through `XR_KHR_vulkan_enable2`. `GpuContext` takes an `IVulkanFactory` for this, so the runtime adds what its compositor needs and picks the GPU the headset is on.
      - `XrStereoSession` runs the session's lifecycle (ready → begin, stopping → end, exiting), the stereo swapchains, and the frame loop: wait, begin, locate the views, draw both eyes, end.
      - When tracking is lost it submits no layer rather than a wrong view.
    - **Colour.** The renderer's frame is UNORM holding display-ready values. Each eye renders in the UNORM twin of the swapchain's sRGB format and is copied across bit for bit, so there's no conversion and no washed-out image. (A runtime treats a UNORM swapchain as linear, which would wash it out.)
    - **Resolution.** Eyes render at `--vr-scale` (default 0.5) of the runtime's recommended size into the top-left of the swapchain image, and the compositor scales it up. That keeps the low-res look of GDD §32 without the shimmer a nearest-filter upscale makes in a headset.
    - **The body and the head.** The flat camera is the player's body: its eye point and yaw. LOCAL space's origin (the head at session start) sits there, so on a moving car the view is in the car's frame, not the room's.
      - The head supplies pitch, roll and every movement. Mouse yaw still turns the body.
      - Both eyes draw the one mesh built around the body's eye point, each from its offset (`Camera.EyeOffset`, `Orientation`, and an asymmetric `EyeFov`).
    - **In the app.** `--vr` plays in the headset and mirrors the flat view to the window, paced by the headset. Without a runtime it says why and plays flat.
    - **Verified headless.** Monado (Ubuntu's `monado-service`) runs a simulated HMD on lavapipe under Xvfb (`tools/xr-sim.sh`; CI does the same).
      - `dt vr check` runs a full session: Idle → Ready → Synchronized → Visible → Focused, 30 stereo frames (about 31 fps in software), then a requested exit through Stopping → Exiting. It writes both eyes side by side to a PNG.
      - `VrTests` check that a centred eye matches the flat projection, that the eyes turn with the body, and that an off-centre frustum puts straight-ahead towards the nose. They also run a real session (eyes 50–80 mm apart, images that differ), skipped where there's no runtime.
    - **Found on the way:** Monado's IPC socket path must fit a `sockaddr_un` (108 bytes), or its client aborts the whole process. So the runtime dir is `/tmp/xr`. A runtime that's installed but not running reports `ErrorRuntimeFailure`, which is now "not running" rather than a crash.
    - **Loader.** Windows builds ship Khronos' `openxr_loader.dll` (NuGet `OpenXR.Loader`, Apache-2.0). Linux uses the system `libopenxr-loader1`.
    - **Not yet (M4):**
      - action mapping and controllers;
      - hand interactions (shovel, levers, ladders);
      - body IK;
      - comfort options (snap turn, vignette);
      - multiview (one pass for both eyes);
      - a real headset run: Quest over Link and SteamVR, which needs a person with one.
26. **Cold and the Vigil (T22, spec B.2 and C.2).**
    - **Cold** is `PlayerState.Cold`, stepped inside the motor, so a client predicts it exactly.
      - It climbs outside and kills at 320 s (`DeathCause.Cold`).
      - Near heat it falls at 320/45 per second, so even the nearly frozen are recovered within spec B.2's "45 s near heat". That's our reading of "resets in 45s".
      - Heat is the cab while the fire burns, or a shut car while the boiler has steam to heat it. A Vigil's vent leaves the cars cold.
      - The spec gives onset no effect. Past onset you move at 0.8 of your speed (`player.json` → `cold.onsetSpeedScale`, ours), and the HUD says how long you have.
    - **The Vigil** (`Sim/Run/Vigil`, `tuning/vigil.json`), host-authoritative and mirrored by clients in a Vigil record.
      - **Starting it.** A dead crewmate's body laid (not carried) aboard the engine, the train stationary (under 0.05 m/s), and someone holding the vent in the cab for 1.5 s. The hold is ours, so a moment's venting at a stop can't start one by accident.
      - **The clock.** 90, 120 and 150 s, and no fourth.
      - **During it,** identically on every machine:
        - the regulator does nothing and the brake is on;
        - the vent is held open, dumping pressure to zero at the boiler's `ventRate`;
        - the Choir sits at its maximum;
        - the car lamps go to emergency red and the headlamp is dark (so it reveals no Sleepers);
        - the guns are dead;
        - the valve roars in the mix.
      - **Spawns.** "Every noise-triggered spawn weight doubles" is read as the director coming twice as often (its cooldown halved). Doubling every weight alike wouldn't change which enemy it picks.
      - **The guns** now need steam generally: they don't traverse below 20 pressure (`combat.json` → `guns.minPressure`, the same floor where the engine loses its pull). That's our reading of "turret traverse dead (no boiler pressure)". After a Vigil they come back as the pressure does.
      - **Breaking it.** Taking the body out of the engine breaks the Vigil; the pressure is gone either way.
      - **The revived** come back in the cab with `PlayerFlags.Revived`: cold onset halved (spec's 100 s; death stays at 320), light things only (lamps), and no guns until the run reaches a stop other than the one they came back at, or the terminus.
    - **Placements.** `PlayerState.Placed` counts the host's authoritative moves: respawns, revivals, the gunner's posting. A client adopts a changed one as a placement, not a misprediction, so the prediction statistics stay honest.
    - **Bots and cold.** At first the bots couldn't climb down and shut a door behind them, so the harness moved a chilled bot into the cab on the host and back once warm. Since T31 they do it themselves by intent (note 34), and the host-side move is gone. Bots don't hold Vigils yet.
    - **Tested** over the real netcode (`VigilTests`):
      - a full 90 s Vigil: pressure to zero, the Choir at maximum, the regulator ignored, lamps out, then the dead back in the cab, revived, agreed by their own machine;
      - the body on a roof, or a moving train, starts nothing;
      - moving the body breaks it;
      - three and no fourth;
      - the revived carry lamps but not crates.
      - `ColdTests` pin spec B.2's numbers and the heat rules. `dt screenshot --vigil` shows the emergency lighting.
    - **Not yet:** a spectator camera and UI for the dead; bots that warm themselves or hold a Vigil; the Vigil's own sound design (it borrows the safety valve's roar).
27. **Text and the HUD (T23).**
    - **A pixel font authored as text.** `Ballast.Render/Fonts/ballast-5x7.txt` draws each glyph in rows of `#` and `.`: 66 glyphs covering capitals, digits and punctuation.
      - Lower case draws as capitals (stencilled rail signage, and a 5×7 lower case is mush), typographic dashes and dots fall back to the plain ones, and anything missing draws as `?`.
      - A designer can redraw a letter in a text editor.
    - **The overlay is solid quads.** `Overlay` builds 2D quads in the frame's own pixels (top-left origin): panels, bars, outlines and text. Text is one quad per horizontal run of ink, with a one-pixel shadow.
      - The renderer draws it with a second pipeline at the end of the scene's pass (alpha-blended, no depth). So there are no textures, samplers or descriptor sets, and no binary atlas to regenerate.
      - It's drawn into the low-res frame (480×270), so the HUD is as chunky as the world and the window blit scales both alike.
    - **The HUD** (`Game/Hud`) is sparse, per GDD §32:
      - the engine, top left: speed and band, regulator, brake, reverser, the pressure gauge with its working band and redline, fire and coal;
      - the link, top right: ping to host at double size, coloured by quality (spec E "shown prominently", non-optional), then the crew count and role;
      - the middle: dead (and why), waiting, a Vigil's countdown, cold, the revived's limits, the night's result;
      - the prompt, bottom centre: what your hands can do right there, with the key. It's worked out from the same interactable, reach and chute checks the sim uses, so it can't promise something the sim won't do;
      - the night, bottom left: the route line.
    - **Controls.** F1 toggles it, `--no-hud` starts without it, and the window title keeps the full debug line.
    - **Verified:** `dt screenshot --hud` plays a solo session for a few seconds and captures the frame at 480×270 (CI keeps it). `HudTests` check font coverage, the run-length quads, the prompts, and that the overlay really blends over the frame on the GPU.
    - **Not yet:** the HUD in VR (it wants a world-space panel, not screen-locked text), menus, and a lower-case font.
28. **Loading modules v1 (T24, spec D).**
    - **Layout from the route.** Each facility's modules come from its kind (`facilities.json` → `kinds`) and are laid out beside the line from the route itself. So host and clients agree without sending positions. `Run.Sites` holds them, and the Run record carries their state per facility.
    - **Manual crates** (D.2): freight crates (`BodyKind.Cargo`) are put out on the platform the first time the train stops there.
      - Carrying one holds you to spec B.2's 2.8 m/s, and stops you jumping or climbing (`PlayerFlags.Heavy`, set by the host, read by the motor).
      - A crate lying still inside a cargo car's walls for a second is stowed: +0.25 of a load, and the crate is gone.
    - **Capstan winch** (D.2, "2 mandatory"): two handles by the track, and a sled of freight 40 m out on a rope.
      - It hauls only while both handles are held by different people. One alone stalls it; that's our first cut of "desync", and a real rhythm comes later.
      - At the track it loads the nearest cargo car with room within 10 m (9 until T32): +0.5, two sleds a site.
      - Both players stand in the open for 80 s a sled.
    - **Departure load.** Cars leave the fortress half full (`run.json` → `departureLoad`, ours). GDD §18 says "every facility is optional; skipping is safe and poor", and with full cars there'd be nothing to gain. Spec F.1's table is the fully loaded train, the ceiling. Existing pay tests build their consists explicitly, so they still pin F.1.
    - **Presentation.** Freight crates are stencilled and strapped. The capstan, its handles, the rope and the sled are drawn where the sim has them. The HUD prompts are "[E] HOLD: CRANK (IT NEEDS TWO)" and "INTO A CAR TO LOAD IT", and the status line says what the stop offers.
    - **Verified:** `FacilityTests` cover:
      - each kind's modules;
      - crates stocked on arrival and stowed as load;
      - carrying freight slow, with no ladders;
      - the winch still with one on the capstan, hauling at speed with two, then loading the car by the track;
      - a client mirroring the site.
      - `dt screenshot --route … --site` shows a stop (CI keeps a winch and a crate stack).
    - **Not yet:** power states (D.1); the other seven modules; 2–4 modules per POI; heavy items needing two; crates bots can load (T34; bots work the winch since T32, note 36). Spurs and the "break the consist apart" set piece came with T27 and T28 (notes 31, 32).
29. **The campaign (T25, spec E and F).**
    - **Rules are pure** (`Sim/Campaign`, `tuning/campaign.json`): states in, states out, no files.
      - Car costs follow spec F.2's curve.
      - The tier follows the consist's size (F.4).
      - The board offers `contractsOffered` routes: the consist's tier, plus one from the tier below for a safer, poorer night. They're deterministic from the campaign seed and night number.
      - Purchases are refused with the reason.
      - `Settle` adds the night's net (spec F.1: the RunReport already charges coal, ammunition and repairs) and drops the cars left behind.
    - **Tier steps.** F.4's ranges overlap at 10 and 15 (Frontier 6–10, Dead lines 10–15). F.1's table works a 10-car train on Frontier and a 15-car one on Dead lines, so the tiers start at 6, 11 and 16.
    - **Upgrades** (F.3) cost a share of the next car's price.
      - Five change the night's tuning (`Campaign.Apply`): tender capacity, ammunition, lamp reach for Sleepers, brakes ×1.2, and the boiler (the same steam from 15% less coal).
      - The rest are bought and saved but not modelled yet. The shop listing says which.
      - The list travels in `SessionSetup.Upgrades`, so every joiner builds the same tuned night from the same content. The content hash still checks the files.
    - **F.2 against F.4.** F.2 prices a car at 1.5–2 nights, but F.4 takes 55–60 nights to reach 20 cars. They agree only if a crew spends about as much again on upgrades and loses some cargo.
      - The standard crew (`dt campaign sim`) buys a car as soon as it can also cover upgrades worth 0.3 of it, a major worth 1.8 every second car, and loses a quarter of its cargo. It reaches 20 cars on night 56.
      - Its phases (6 cars by 12, 10 by 23, 15 by 39) run later than F.4's early phase (6 by 8). That would take a crew that buys almost nothing but cars at first.
      - `CampaignTests` pin F.1's net table, F.2's costs, and the 55–60.
    - **Saves** (spec E, "3 per host") are one JSON text file a slot in the user's app data (`SaveSlots`), written to a temp file and moved into place.
      - **Autosave per POI:** `NetPlaySession` takes a `RunCheckpoint` as the train pulls away from a facility: route, the stop left, the dawn clock, the front's position, every car's load, integrity and ammunition, the coal, and the Vigils used.
      - `--campaign <slot> --resume` restarts that night there. Stops up to it are spent, running costs count on from the save, and joiners build the train where it stands (`SessionSetup.Start`).
    - **Playing it.**
      - `DarkTerritory --campaign <slot> [--contract i]` hosts tonight's contract with the slot's cars and upgrades (with `--host`/`--steam` as usual), autosaves at each departure, and settles when the night ends.
      - `dt campaign new|show|slots|buy|sim|play` is the fortress, headless. `play` crews a night with bots and settles it: a local night delivered 2 cars for 900 gross and 848 net.
    - **Not yet:** a fortress screen in the game (it's the CLI for now); contracts with their own cargo and destinations (GDD §19); failure costs beyond running costs and lost cars; repairs bought at the fortress.
30. **VR controllers and comfort (T26, roadmap M4).**
    - **Actions are the engine's, meanings the game's.** `Ballast.Xr.XrControls` is one OpenXR action set, `gameplay`: move and turn sticks, grip, trigger, two face buttons, stick click, menu, and a grip pose per hand.
      - It suggests bindings for Quest Touch, Valve Index and the Khronos simple controller, so every runtime binds something. A runtime that doesn't know a profile says so and the others still take.
      - The controllers are read once a frame, straight after `xrBeginFrame`, at the frame's predicted display time. The bound profile is cached until the runtime says it changed.
    - **Two yaws.** A headset player sends the same `PlayerIntent` a keyboard does (`VrLocomotion`), so neither the host nor the sim knows about VR.
      - The sim's yaw and pitch follow the head: guns, reach and what the crew see agree with the headset. Look is still a delta: the one that turns the predicted player to where the head is. It converges in a tick, with no drift, because the client predicts its own player.
      - The tracking space's yaw (`BodyYaw`) is this machine's alone: only the stick (or the mouse) turns it, at the display's rate. The eyes and hands hang off it, never the sim's yaw, which moves at 30 Hz and would make the world swim.
      - **Walking is head-relative for free:** the stick moves the sim player along its look, which is the head's.
      - **Changing frame keeps the room still:** stepping off a car, or across a curve's couplers, keeps the world facing. A placement (respawn, drop-in: `PlayerState.Placed`) takes the sim's facing instead.
    - **Comfort** (`tuning/vr.json`):
      - Turning is snap (45°, re-armed when the stick comes back to the middle) by default, or smooth.
      - A vignette darkens the eye's edge while the stick moves or turns you, and blinks on a snap. It's centred on each eye's straight-ahead, not mid-image.
      - The train carrying you doesn't count: you're riding it, so it isn't artificial motion.
      - These numbers aren't in the GDD or the spec. They're defaults a settings screen will expose.
    - **Mapping:** grip Use, trigger Fire, A Jump, B Throw. A stick click runs until the stick is let go (holding a click down while steering is a cramp).
      - On the simple controller, select is Use on the left hand and Fire on the right.
      - Driving stays on the keyboard until hand interactions give the cab its levers.
    - **Hands** are a gloved block and a cuff at each grip pose, drawn on this machine only. They're added to the shared mesh for the eyes and taken off again. `VrView.SideBySide` redraws them for screenshots.
    - **Headless:** `tools/xr-sim.sh` starts Monado with its simulated simple controllers.
      - `dt vr check` reports the bound profile, both hands, and what they'd send.
      - `VrTests` asserts the binding and that both hands are in view.
      - `VrLocomotionTests` covers the rest without a runtime, ending in four snaps and a walk the other way down a car through `PrototypeSession`.
    - **Not yet:** room-scale walking (the tracked head offsets the eyes, not the sim player); hand interactions with levers, ladders and the shovel; body IK; the HUD as a world panel in the headset; a seated/standing height option.
31. **Branches and switches (T27, GDD §17, App. A.7).**
    - **Paths, not a graph.** `RailLine` has branches, each off the main line at a switch's points (the toe) and built as its own line from there. A rake runs on a path: the main line, or the main line up to a branch's points and the branch beyond them.
      - Distances agree up to the points, so a rake on the shared track is where it is whichever path it's on. The 1D rake physics, cutting, coupling and replication carry on unchanged; a rake just carries a `Path` (one more field on its wire record).
      - **The switch decides once:** as a rake's front runs forward through the points, it takes the branch if the switch is set for it. Backing out needs no choice, and a rake whose front is back behind the points is on the main line again.
      - The switch settings are train state, like the rakes: the host owns them, and a `Switch` record per branch replicates them.
    - **Rakes meet along each path in turn** (`TrainOnLine.ResolveOn`). A rake on another path takes part only by what's on the track the two share.
      - Its tail over the points can be run into from behind, and coupled to.
      - A rake whose front is off down the other route can't couple on this one. It only sideswipes (collides, shares momentum, is pushed back) whatever is fouling the points.
    - **A dead line's end is a buffer stop.** Running into it at speed damages the front vehicle and the cargo, on the coupling tuning's curve. The main line's ends still just stop the train, as before.
    - **Thrown by hand** (`SwitchStands`, `tuning/route.json` "junctions"):
      - Stand at a branch's stand (beside the points, on its side) and hold Use for `throwSeconds`. It goes over once per hold, and says which way on the HUD.
      - The points won't move with a wheel within `pointsLength` of the toe. Throwing them under a train would split its bogies, and the model only reads the switch as a front passes.
      - The stand's target lamp is green set for the main line, red for the branch: from the cab, that's how you read a switch before you're on it (the Switchman's telegraph, App. A.7). `World.SetSwitch` is the hook for him.
    - **Generated dead lines** (`RouteGenerator.AddJunctions`): every junction feature gets one.
      - It goes out through a turnout on straight, evenly graded main line, back to parallel at about 8.4 m, then alongside the main line to a buffer stop 450–700 m on.
      - Alongside is the main line's own segments offset: the same angle, the radius plus or minus the offset, and the same rise. So it lies on the main line's ground, never crosses it, and needs no terrain of its own.
      - Nothing else is within 150 m of its whole length: no tunnel or bridge to run into.
      - Over 80 routes, 273 dead lines sit within 7 mm of the main line's height and 3 cm of their offset.
    - **Exact segment boundaries.** Laying the turnout exposed a flaw in `RailLine`: a step that crossed a segment boundary used the old segment's curve for the whole step, so a curve could run up to a metre long. Each step now integrates piecewise through the segments it crosses. Every line moves by less than that, and all the existing tests held.
    - **What else follows the path.**
      - The ground under a player is the nearest track's (`RailLine.Nearest`).
      - Off-train enemies are placed along the engine's path. Sleepers lie on the main line and can't be reached from a branch past them.
      - The driver bot brakes for the end of the track it's on.
      - A train down a dead line is at no facility and not at the terminus.
    - **Headless:**
      - `dt route gen` lists each junction's branch.
      - `dt screenshot --route tier:seed --junction i [--diverge] [--through]` shows the switch, or the train run in onto it.
      - `SwitchTests` covers the geometry, taking and backing out of a branch, locked points, the buffer stop, rakes that only meet at the points, a sideswipe, the hand throw, replication, the ground, and the generator. A `HudTests` case plays a stand through a `PrototypeSession`.
    - **Not yet:** the Switchman; a derailment for taking a turnout too fast. Facility spurs came with T28 (note 32).
32. **The facility set piece on spurs (T28, GDD §17).**
    - **Every facility but the coaling tower is down a spur.**
      - The spur is laid off the facility's level zone (`route.json` "junctions": `spurToe`, a tighter turnout, 6 m between track centres).
      - It's 100 m to the buffer stop, which takes the engine and four cars clear of the points (`SpurDrill.Capacity`). A longer train has to break the consist apart: "most cannot accommodate a full armoured freight train".
      - The coaling tower stays over the main line: the tender goes under its chute where it is.
    - **The machinery is down there.** A site is laid out along its spur, from where the first cars stand with the engine at the buffer stop (`facilities.json` `spurLayout`), on the side away from the main line. The buildings stand back beyond it, clear of the crate stack and the winch's haul, and lineside trees keep out of the yard.
    - **The run follows the track.**
      - At a spur facility the train is only *at* it with the engine stopped down its spur. Pulling up on the main line beside it gets nothing, and the HUD says how much of the train the spur takes and to cut the rest.
      - **Leaving is a departure:** the engine out on the main line past the end of the zone, after having stopped there (`Run.Departed`, `Departures`). That's where the night autosaves now (spec E's "leaving a POI"), not the first time the train moves at a stop, which the shunting does again and again.
    - **`SpurDrill` plays it by script**, with only what a crew has: the regulator, the brake, a coupler and the switch.
      1. Stop short of the points.
      2. Cut what won't fit.
      3. Set the switch and run the empties in to the buffer stop.
      4. Wait while the crew loads.
      5. Back out onto the waiting cars at a crawl, so the buckeyes couple rather than collide, and on until the whole train's front is behind the points.
      6. Set the switch back and go.
    - **Verified on generated nights.** `SpurDrillTests` plays a seven-car train through a winch facility with two crew on the capstan:
      - four cars go in, three wait;
      - both sleds load the first car;
      - the train comes out as one rake in its original order, undamaged, on the main line, with the switch back;
      - the night sees it leave.
      - A three-car train goes in whole.
      - `dt facility drill` prints the timeline: about five minutes for 3, 7 or 12 cars.
    - **Two things the drill found.** Left alone through the loading, the fire dies and the regulator does nothing: someone has to fire the boiler. And a crew left standing out at the capstan freezes (spec B.2) before the train can leave, which ends the night. Both are the GDD working as written; the tests take unlimited steam and bring the crew aboard.
    - **Not yet:** crew bots working a stop through intent (T31); more than one trip for a train too long to load in one; the loaded cars' load shown on the HUD.
33. **The front end and the fortress screen (T30, spec E and F, roadmap M6 "settings").**
    - **Plain `DarkTerritory` opens it.** The title leads to:
      - **Campaign:** the three slots. An empty one starts a crew.
      - **The fortress:** the board of contracts, a car to buy at F.2's price, the upgrades (saying which the night doesn't model yet), and alone or hosted.
      - **Quick night:** any tier, seed and length of train, alone or hosted.
      - **Join:** a typed address.
      - **Settings.**
    - A night under way in a slot shows as "carry on" instead of the board. It resumes from its last autosave, and the shop waits until it's settled.
    - After a night the game comes back to where it was chosen, and the fortress says how it went. Leaving is Esc twice, or Enter once the night's over.
    - **`FrontEnd` is a plain state machine** in `DarkTerritory.Game`: keys in, a `Launch` out, drawn with the HUD's overlay. The rules are `Campaign`'s; the front end only lists them and saves each change to the slot at once. `FrontEndTests` drives it, and `dt screenshot --menu <screen>` draws it, without a window.
    - **The app** creates the window, the GPU, the audio and the headset once, and loops: menu, start, play, back.
      - A night named on the command line (`--route`, `--campaign`, `--host`, `--join`, a Steam invite) skips the menu and quits when it's over, as before.
      - A Steam invite accepted in the menus joins at once.
    - **Settings** (`Settings`, `settings.json` beside the save slots in the user's app data): sound, open mic or push to talk, the HUD, VR snap or smooth turning and the comfort vignette (over `tuning/vr.json`), and mouse speed. A file that can't be read is the defaults.
    - **Input:** the window gained arrow keys, Enter and typed text (SDL text input, on only while the join screen wants it).
    - **Not yet:** the menus in the headset (they're on the window); a lobby screen that shows who's aboard before the night starts; renaming a crew or deleting a slot from the menu (`dt campaign` does both); rebinding keys.
34. **Bots keep warm by themselves (T31, spec B.2).**
    - **`WarmUp` does it the way a person would.** A walker or the gunner:
      1. Walks off the roof's end onto a coupler plate. It's always the plate of the car it'll enter, and never the engine's: the cab is the fireman's.
      2. Opens that car's end door and goes in.
      3. Shuts the door behind it, and the far one if someone left it open: a car only warms you shut.
      4. Waits by the door until warm.
      5. Leaves by the nearer door and climbs the end ladder.
    - All of it is intent through the same path as a player's.
    - **What made it hard:**
      - **The plate is 0.8 m wide.** The doorway is left of centre and the end ladder just off the plate's right edge. Any sideways step on the plate at speed is off it and a death. So the bot crosses between door and plate on one line that's in both, steps straight in and out, and stands in reach of the ladder rather than at it.
      - **Use on the plate cuts the coupling unless you're facing a door.** So the bot only presses it when `CrewActions.Nearest`, the sim's own rule, says a door is in reach.
      - **Two hands on one door toggle it twice.** The bots go in at their own point in the onset (seeded, 45–70% of it), so a crew that started together doesn't queue at one door. A door shut in its face sends it back to opening.
      - **Harness bots decide from their client's predicted state.** Corrections up to a third of a metre are why the margins above matter, and why the isolated test passed long before the harness did.
    - **Walkers now recover from where they end up.**
      - In a gap, they steer to the end ladder before climbing, not only when they happen to land by it.
      - On a car's floor, they leave by the nearer door.
      - On the ballast, they walk to the nearest car's side ladder and climb. That catches a train at a stand or a crawl, not one at speed.
    - **Verified:**
      - `WarmUpTests` runs three walkers on a standing train for ten minutes, twice the time it takes to freeze. They all live, each goes in before the onset, they come out again, and nobody cuts the train.
      - The harness (`HarnessOptions.Observe` traces a night tick by tick) ran 10-minute Frontier nights with enemies at seeds 1–3: no deaths, 14–18 warm-ups each.
    - **Not yet:** the fireman's own trips out. Bots working a facility stop came with T32 (note 36).
35. **Builds for players (T33, roadmap M6).**
    - **`tools/package.sh [win-x64] [linux-x64]`** publishes the app self-contained (the .NET runtime inside, nothing to install) into `out/dist/DarkTerritory-<rid>/`, and zips it.
      - Each folder has `content/` and `PLAYING.txt` (`tools/package/PLAYING.txt`: the menus, the controls, hosting).
      - It also has the native libraries from the NuGet runtimes: SDL3, shaderc and the OpenXR loader. Vulkan comes with the GPU driver. Steam's own library still has to be dropped in (`external/steam/README.md`); without it the game runs, and says so.
      - Windows builds cross-publish from Linux.
    - **Content beside the executable.** `DataFile.FindContentRoot` still prefers a repository's `content/` (found by `Ballast.slnx` in a parent) and falls back to the one beside the executable. A player can start the game from anywhere.
    - **CI packages every push.**
      - The `package` job runs after the tests and builds both zips.
      - It starts the Linux build from a different directory under Xvfb and lavapipe, and plays six seconds with a capture. A build that can't find its content or its native libraries fails there.
      - Both folders are kept as artifacts.
    - **Crash reports** (`CrashReports`): the console is teed through a ring of its last 200 lines.
      - An unhandled exception writes `crash-<time>.txt` to the user's app data (`DarkTerritory/crashes`): the version, the OS, the exception with its stack, and those lines.
      - It says where the report is on the way out.
    - **Store uploads (T38).** `tools/upload.sh steam|itch` sends those folders on (`tools/store/README.md`).
      - **Steam:** it writes one SteamPipe app build with a depot per platform, then runs `steamcmd +run_app_build`. The game and the demo are separate apps (`--demo`).
      - **itch.io:** `butler push` to a channel per platform. The demo goes on `-demo` channels on the same page.
      - **IDs and credentials.** The IDs are in `tools/store/store.conf`. The credentials come from the environment.
      - **Checks.** Each folder has to be a whole game: its executable, content, and how to play. A real Steam upload also refuses a Windows build without Valve's library, since lobbies need it.
      - **Never the default branch.** A Steam build goes live on a beta branch at most. Steamworks doesn't let a script set the default branch live anyway, so a release to players is a person's click.
      - **CI dry-runs both on every push.** It checks the builds, writes the app build and the butler commands to `out/store/`, and checks them with `jq`, with no tool and no login.
      - **The real upload** is the hand-run `Release` workflow, with the credentials as secrets. Steam Guard is passed with a saved `config.vdf`.
      - The demo is the same build as the game for now. When the demo's content is cut down, `package.sh` gets a demo variant.
    - **Not yet:** a real upload (it needs the app ids and an itch page); a Windows smoke test (the CI's Windows runners have no Vulkan); code signing; a crash reporter that sends reports.
36. **The crew works a stop (T32, GDD §17, spec D).**
    - **Parts at a stop.** The crew is a driver (`ConductorBot` with a `StopDriver`), a shunter, and two on the winch (`StopHand`s on walkers, or on the gunner when a crew of four needs it). Everyone else keeps walking the roofs.
      - The driver stops only where the crew can do the work: a spur with a winch, and a living shunter and winch pair. Crate stops wait for T34: a 0.9 m crate won't go through a 0.9 m end door from a plate 1.1 m up, and carrying one stops you climbing.
      - The facility is worked out the same way by everyone from the route (`StopPlan`): stop short of the points, cut behind the cars the spur takes.
    - **All of it is intent**, through the same path as a player's.
      - The driver runs the whole sequence from the cab with notches, the brake and the reverser: run up and stop two metres short of the points; wait for the cut, the switch and the riders; run the empties in to the buffer stop; wait for the winch; back out onto the waiting cars at 0.8 m/s so they couple; stop clear of the points; wait for the switch and everyone aboard; go.
      - It reads the cab's levers from `World.Controls`, what a person in the cab sees.
      - The shunter drops into the gap behind the cut car and holds Use facing away from both doors. Then it gets down, walks beside the train to the stand, and throws the switch. It rides in and out in the cab, which is warm and where the driver can see it, and then sets the switch back and climbs aboard.
      - The winch pair ride in in the cab, walk to their handles, crank until both sleds are in, and climb onto the nearest car.
      - Walking on the ground keeps 2.4 m from the track until level with the target, so nobody walks into the train.
    - **What they tell each other** (`CrewCalls`): who has which part and where each of them is. A crew of people says that on the radio.
      - It carries nothing a player couldn't say.
      - What each bot sees is its own client's world. Clients now mirror the run in the harness, as they do in the game.
    - **Cold still applies.**
      - At 85% of the onset, a hand stops work and gets warm until it's nearly back to nothing (5 s), then returns.
      - It warms in the cab when the engine is within 60 m, and the walker's way in a car otherwise. The cab has no doors, so two hands can't undo each other: the winch pair both went cold at once and froze in one car, shutting and reopening its door (the T31 door race).
      - The cab is warm, so riding in and out doubles as a warm-up. Both 80 s sleds fit inside the onset from a warm start.
    - **Found on the way, and fixed:**
      - **The cab steps put you on the cab roof.** Over the top of a ladder you now land on the highest footing at the top rung, not whatever's overhead. That fixes it for players too (`PlayerMotorTests`).
      - **A half-full train could only take one sled.** The sled came to rest by the first car only, which takes 0.5 when half full, and the engine can't pull forward from the buffer stop to bring the next car up. The capstan now stands where the sled rests between the first two cars, in reach of both (`facilities.json` `winch.along` 20 → 8, and `carReach` 9 → 10 for a margin either side; our numbers, not the spec's).
      - **A second winch stop loaded nothing.** The cars by the winch were filled at the first, and a train can't reorder its cars at a spur. A sled's load now goes into the nearest cargo car with room in the train standing by it, handed along. Spec D.2 doesn't say which car a sled fills.
      - **The harness's trains left full** (`Consist.Uniform`'s third argument is the load). On a night they now leave at `run.json` `departureLoad`, as in the game, so night pay in the harness drops to what a real night pays for what it loads.
      - **Walkers dropped into a gap on curves at speed and missed the plate.** In the air you go straight on while the train turns under you: 0.5 m across a 0.8 m plate at 15 m/s on a 333 m curve. `WarmUp` now waits at the roof's end until the pull (v²/R) is under 0.3 m/s².
      - **At a long stand the fire burned low** while pressure held up, and the Hollow came down the stack. The conductor now keeps the firebox above twice the low-fire mark too. The safety valve sheds what a standing fire makes (at most 1.8 a second against its 2.5).
    - **Verified:**
      - `StopCrewTests`, direct:
        - a crew of five runs every leg;
        - both sleds are loaded;
        - the train leaves in one rake, with the switch back and everyone alive and aboard;
        - without the winch pair the driver runs straight past.
      - Over the harness (`dt harness --route frontier:6`, 90 ms ±20 with 3% loss), the foundry stop took 437 s with five bots, 622 s with four, and 500 s with eight and enemies. Both sleds went in each time, with no deaths.
      - The report lists each stop's legs, and `--trace file` writes who's doing what whenever it changes.
    - **Not yet:** stops at the coaling tower (crates came with T34, note 37); a second spur visit if the winch was left unfinished; the driver taking over the switch when the shunter is dead (a stop with no shunter left strands the train at the points, as it would a real crew).
37. **Crates you can load (T34, spec D.2).**
    - **Why.** Before this, nobody could load a crate. A crate is 0.9 m across and the end doors are 0.9 m wide, 1.1 m up off a plate. Carrying one stops you climbing.
    - **Side doors.** Cargo cars now have a sliding door in the middle of each side, 1.8 m wide.
      - The doors are bits 2 and 3 of the door state, after the end doors. They're worked from inside or from outside (`InteractableKind.Door`, radius 0.9).
      - The load stacked down the right-hand side is split either side of its door.
    - **Steps.** A flight of steps runs up the outside of the car to each side door.
      - The treads rise under 0.3 m each, so you walk up them (player.json `stepUp`), arms full or not.
      - They end in a landing level with the floor, and stand 0.6 m out from the car (`train.json` `interior`: `sideDoorWidth`, `stepWidth`, `stepDepth`).
      - They're car solids (`PartKind.Steps`), so the motor, crates and renderer treat them like any other part. No world geometry was needed.
      - A 0.6 m landing fits a 0.3 m player clear of the wall. A narrower one didn't.
    - **Shelter.** An open side door lets the cold in like any other door. `WarmUp` shuts any open door, not just the end ones, walking down the aisle to a side door first. It also never leaves a car toward the engine: that plate's ladder goes up onto the tender, and a walker jumped off it at speed.
    - **The renderer** slides a side door back along the outside of the car when it's open, the way a boxcar's does.
    - **Crate hands** (`StopJob.Crates`, and the winch pair wherever there's no winch or it's done):
      - They open the car's door on the site side first. Use with your arms full puts the crate down.
      - Then they pick up a crate and walk it along the lane in front of the steps: the landing's side is as high as the floor. Up the treads, in, and down in the middle of the car, where it's stowed once still.
      - They choose the car with the most room, counting crates already in it and the ones others have called for it ("this one's for car two"). Without that call, three hands filled one car at once.
      - A crate left on the steps or landing can be picked up from the ground beside them.
      - When the crates are in, the hands share out the side doors to shut. Two at one door undo each other.
    - **The driver** now stops at crate-only facilities when there's anyone to carry, and at no stop where the cars that would go in are already full. It waits until the crates are in and the side doors are shut: a loose crate in an overfilled car slid out of an open door when the train moved.
    - **Tuning moved out of the bot:** a crate's share of a load comes from the site (`facilities.json` `loadPerCrate`).
    - **Verified:**
      - `InteriorTests`: up the steps to the landing, arms full or not, open the side door, walk in; an open side door leaves you outside.
      - `WarmUpTests`: a walker warming up shuts a side door left open.
      - `StopCrewTests`: at a crates-only stop a crew of five loads every crate the cars have room for, shuts the doors, and leaves with nobody left behind; a full train runs straight past.
      - `dt screenshot --route frontier:7 --site` shows the doors and steps by the crate stack.
    - **Not yet:** heavy items that need two (D.2); crates at the coaling tower; a human-facing prompt for which car has room (the HUD still says only "INTO A CAR TO LOAD IT").
38. **The crew coals up (T35, spec B.6, D.2 gravity chute).**
    - **Why.** Spec B.6's tender endurance is finite, and the bots never stopped at the coaling tower. A long night's tender could run dry, with the Hollow close behind.
    - **When it stops.** The driver stops at a coaling tower that has coal left, when the tender has a quarter of its capacity to fill and there's a shunter (`CoalPlan`). Of that and a spur stop, it takes whichever comes first.
    - **The driver** stops with the tender's middle under the spout, well inside `run.json`'s spout tolerance. If it rolls past, it creeps back.
      - It holds until the chute has been opened and shut again and everyone's aboard.
      - It leaves through the same `Depart` leg as a spur stop, so a reverser left in reverse after backing up is flipped first.
    - **The shunter** gets down on the lever's side and stands just beyond the lever from the track.
      - It holds Use to open the chute, and to shut it again once the tender is a second and a half's pour from full (`pourPerSecond`) or the tower's empty. Overflow damages the engine.
      - Then it climbs aboard the walker's way.
    - **Reported:** the stop's `StopRecord` has the coal taken.
    - **Verified:** in `StopCrewTests`, a train with its tender at 40% stops, fills it by more than 100 units, shuts the chute with no overflow damage, and leaves with everyone aboard.
    - **Stops the dawn has time for.** Every stop is optional (GDD §18). The driver takes a facility stop only with 600 s to spare before the dawn after the run to the end of the line, and a coaling stop with 120 s. It always coals when the tender is under a fifth: without coal the train goes nowhere. On frontier:7 two long crate stops had made the train miss the dawn.
    - **Crate stops, faster** (from T34's first full night, 527 s and 689 s for two stops):
      - A hand with no loose crate to hand waits by the stack for the next, rather than taking itself off the stop while crates are still in other hands' arms.
      - A hand on the far side of the train from the steps goes over it: it puts the crate down, climbs a car, and gets down on the working side. There's no way round on foot.
      - Only crates on the working side count as loose, so nobody waits on one over the train.
    - **Correction reports.** A prediction on a different frame from the host's truth (the ballast against a step, one roof against the next) used to count as a 100 m correction. It's now measured in the world through the current frames (`ClientSession`).
    - **Not yet:** topping up the tender by shovelling from a coal car; a coaling tower on a spur.
39. **The Switchman (T37, App. A.7, B.7).**
    - **What it does.** A corrupted railway worker at a junction ahead throws the switch for the dead line (`World.SetSwitch`), through the enemy spine like the rest of the roster.
      - **Telegraph:** the switch itself. The stand's lamp reads wrong from the cab, and the figure stands at the stand with a lantern.
      - **Commit:** the train taking the points.
      - **Punish:** the dead line and its buffer stop. After a while it's gone.
      - **Flee:** anyone on the ground within 25 m makes it go, and it leaves the switch as it set it. Someone still has to throw it back.
      - If the switch is set back first, it has lost that one.
      - "Never directly lethal": the train isn't hurt by taking the points. It costs the clock.
    - **Silent.** The spec's tell table (A.4) gives it no sound, and the GDD's telegraph is visual ("a lamp signal reading wrong; a distant figure"). So it's silent, like the Lamplighters. That's our reading (§8).
    - **Fairness by where it goes** (`Switchman.Junction`). It only takes a dead line's points 500–1200 m ahead (`enemies.json` `switchman.ahead`) whose switch still reads right. At cruise that's more than half a minute of telegraph, where the reaction window needs 1.5 s. The spine's own rule refuses a commit without the window anyway.
    - **The director** (App. B.7) considers it on the Frontier and beyond:
      - only on a route with at least 3 dead lines (`minJunctions`; facility spurs don't count);
      - never with another one about (one corrupted human at a time);
      - only with a junction in its window;
      - it costs 2.
    - **Replication.** Its switch replicates like any other, so the lamp reads wrong from any distance. The figure only reaches a client inside the 520 m interest radius, so it's seen when it's near.
    - **The crew's answer** (`SwitchPlan`, "verify every switch on the ground"):
      - A dead line's switch set for it within 1200 m of the cab: the driver stops two metres short of the points' reach, the shunter walks to the stand and sets it back, and once everyone's aboard it goes on.
      - Already down the dead line: the driver backs out past the points, the switch is set back, and then it goes on.
      - Each is a `SwitchSetBack` in the harness's stops.
    - **Presentation.** A greybox railwayman, stooped, with a swinging lantern (the only light on it) at the stand. The HUD cues are "a figure at the points ahead; the switch lamp reads wrong", "the train takes a dead line" and "the figure at the points slips away". `dt screenshot --threats` stages one beside the line.
    - **Verified:**
      - `SwitchmanTests`:
        - it throws the junction and stands at it;
        - left alone, the train takes the dead line after a full telegraph, and nobody's hurt;
        - approached on the ground, it goes and the switch stays wrong;
        - set back, it's done;
        - it won't take a junction nearer than its window.
      - `StopCrewTests`: a switch set wrong ahead is set back on the ground and the train never takes it; a train down a dead line backs out and goes on.
    - **Not yet:** its patrol along the network (it appears at the junction); the Passenger (A.7's other corrupted human); a powered switch thrower (spec F.3) that sets it back from the cab.
40. **Hands (T29, roadmap M4).** There are no VR numbers in the GDD or the spec. So these are our readings, and the numbers are tuning (`player.json` `hand`).
    - **The hand is intent.**
      - A headset player's reaching hand goes in `PlayerIntent` (`PlayerButtons.Hand`, then `HandX/Y/Z`). It's measured from the feet, in the frame the player faces, on a centimetre grid.
      - The wire only carries it when it's there (six bytes). Keyboards and bots send what they always did.
    - **The sim holds it to an arm's length.**
      - `PlayerMotor.TakeHand` takes it each tick after the look: no farther than `arm` across from the body, and from the feet to `overhead`.
      - It's as low as the feet because a headset player crouches for real; the sim's body doesn't.
      - It isn't replicated. The host and the predicting client each take it from the same intent, so there's nothing to correct: a client shovelling by hand over loopback has zero corrections.
      - `World.Hand` carries the tuning; the sessions set it from their player tuning. Without it, hands are ignored.
    - **Reach from the hand.** With a hand reported, every reach test uses the hand instead of the body:
      - a switch stand's lever, the winch handles and the chute lever: the hand within `grab` of the grip;
      - a loose body: within `grab` of any part of it (`Bodies.InReach`);
      - an interactable: within its reach across, and between knee and head height over its footing. It picks by where the hand is, so doors want no facing. The brake wheel is in reach of a hand from the top of the end ladder.
      - a ladder: a hand on it takes hold without pushing towards it.
      - The HUD's prompts follow the hand too.
    - **The shovel is a stroke.**
      - Coal goes on the shovel at the tender's coal face (`InteractableKind.Coal`, for hands only) and into the firebox (`PlayerFlags.Shovelful`). Letting go spills it.
      - Spec B.6's 1.2 s a shovelful still caps it. The time counts from the grip or the last shovelful, so a steady swing matches the keyboard's rate and a frantic one doesn't beat it.
      - The coal face is placed so a fireman turning between it and the firebox reaches both.
    - **The cab's levers are the game's, not the sim's** (`VrLevers`).
      - The engine's shape has the regulator, brake and reverser handles (`CabLevers`), with their travel.
      - A hand gripping one moves it, and that becomes the notches, the held brake or the one reverser press a keyboard sends. The host can't tell the difference.
      - The greybox cab draws the handles where the controls have them (`dt screenshot --view cab --throttle 0.5`).
    - **Ladders by hand.** On a ladder, a gripping hand pulled down climbs, up to the ladder's climbing speed (`VrLocomotion`). Pushing it up climbs down slowly, never as far as letting go.
    - **Which hand.** The last hand to grip is the one reaching; until then it's the right.
    - **Verified:**
      - `HandTests`: the wire, the arm's length, shovel strokes, a frantic swing, spilling, the keyboard never finding the coal face, a ladder by hand, the brake wheel from the ladder, a lamp by hand, a switch lever, and a hand-shovelling client predicting exactly.
      - `VrHandsTests`: the hand's frame, which hand reaches, climbing by hand, and each lever.
    - **Not yet:**
      - two-handed grips (the winch, heavy crates);
      - throwing by the hand's motion (B still throws along the look);
      - carrying at the hand;
      - remote players' hands (body IK);
      - the exit test on a real headset.
41. **The HUD and menus in the headset (T36, roadmap M4).**
    - **The flat screen's own drawing, on a panel** (`VrPanel`).
      - The HUD and the front end are drawn exactly as for the window: an `Overlay` at 480×270.
      - That overlay is carried onto a flat panel floating ahead of the head. Each vertex is projected through each eye's camera into that eye's overlay, under the comfort vignette.
      - So it has real depth in both eyes, and nothing about the HUD or the menus knows about headsets. No new renderer path: it's the 2D pass the window already has.
      - It's drawn over the scene rather than into it, as a HUD should be. Triangles behind an eye are dropped.
    - **This frame's eyes.**
      - `XrStereoSession.Frame` now locates the views before it reads the controllers.
      - So the synced callback, where the panel is projected and each eye's overlay prepared, has this frame's eyes.
      - A panel projected with last frame's eyes would drag behind every head movement.
    - **Lazy follow.** A panel pinned to the face is the classic way to make people sick. So:
      - its place moves with the head at once (leaning doesn't slide it away);
      - it only turns once the head has looked `followDegrees` off it, then eases round over about `followSeconds`, and stops within 2° of the head;
      - a glance at a corner doesn't move it.
      - The HUD's panel is nearer and lower (1.6 m ahead, 0.2 m down, 1.8 m across); the menus' is straight ahead at 2 m (`vr.json` `hud`, `menu`).
    - **No crosshair on it.** The head aims, and a lazily following panel is never quite where the head points, so the cross is left off (`Hud.Build(..., crosshair: false)`).
    - **Menus by controller** (`VrMenuInput`).
      - The left stick moves through them: one step per push, and it has to come back near the middle for another, so there's no chatter at the threshold.
      - The trigger or A chooses; B goes back.
      - The front end's hints say so in a headset (`FrontEnd.Headset`).
      - Coming in from a night, whatever's held (the A that ended it) isn't a press.
      - Typing an address (Join) is still the keyboard's.
    - **Verified:**
      - `VrPanelTests`: straight ahead and below by the drop, at the panel's distance; the two eyes' disparity is what that distance gives; a glance doesn't move it but a turn brings it round, and then it stays; it turns with the body and isn't drawn from behind; the menu sticks and buttons press once.
      - `dt vr check --hud` and `--menu title` project the real HUD (a solo night stepped three seconds) and the title screen into both eyes on Monado's simulated headset. CI checks both eyes got the panel, and keeps `vr-hud.png` and `vr-menu.png`.
    - **Not yet:**
      - pointing at menu items with a hand;
      - a panel you can grab and move;
      - the text's size is the flat HUD's, legible at a real headset's pixel density but small at the simulated one's;
      - the exit test on a real headset.
42. **Art pass v1 (T39, GDD §25-28).** Surfaces stand in for textures until there are any, and they're all from the style sheet's short list.
    - **In the vertex, not in the frame's constants.**
      - The push constants are already at the 128 bytes every GPU guarantees. So each vertex carries its surface: texel coordinates, wear and shine (`Vertex` is 60 bytes).
      - `MeshBuilder.Style` fills them in. With no style, the greybox is flat colour as before (`--greybox`, and every test that doesn't ask).
    - **Texels that stay put.**
      - A box's texels are in its own frame, at `look.json` `texelsPerMetre` (§27's environment density, 128 px/m). So the grime rides with a moving car, and nothing swims as the camera moves.
      - Bare triangles (the ground, the trees) take world texels, wrapped every 4 km so they fit a float. The pattern jumps at a wrap, rarely and far off in the fog.
      - `MeshBuilder.Seed` (a vehicle's index, a prop's place along the line) keeps identical parts from wearing identically.
    - **What the shader does with them.** In texel space:
      - blocky per-texel grain: the low-res texture look, and §27's "subtle pixel crawl" as the camera moves;
      - broad soot fields about half a metre across;
      - warm rust and chipped patches;
      - streaks stretched down the surface.
      - The existing dither and 48-level colour then give it the "compression feel".
      - Metal throws a harsh Blinn specular from the headlamp and a cold one from the moon (§25), dulled in patches by the grime.
      - The bottoms of boxes are darker by `baked`: the baked shadow, and the soot that collects low.
    - **Materials by colour** (`Look`).
      - Every `Palette` colour named in `look.json` is one of §27's material families: iron dark and oily with a specular, brass tarnished, paint and wood the most worn, lights clean.
      - Any other colour (a tint, a lamp dimmed in a Vigil) takes the nearest named one's by hue first, then brightness. So the whole scene is dressed without the scene saying what anything is.
      - A name that isn't a palette colour is an error.
    - **Found on the way:** the lineside pines were placed with `HashCode.Combine`, which .NET seeds afresh every process. So they moved between runs and no two screenshots matched. They use a fixed hash now.
    - **Verified:**
      - `LookTests`: every material maps, lights stay clean, and a dimmed or tinted colour keeps its family;
      - worn walls break up (4×4 blocks, which cancel the dither, differ more than flat colour's) without burying the room: its mean brightness stays within 60-110% of flat;
      - the guard van's wear is the same seen from inside the car with the train 300 m further on.
      - CI's screenshots all carry the look.
    - **Not yet:**
      - the look hot-reloaded;
      - real textures (the vertex layout leaves room: the texel coordinates are UVs in waiting);
      - normal breakup;
      - VFX (§31: steam, sparks, cinders).
43. **The radio is a thing (T41, spec A.5, GDD §24).**
    - **Worn, not held.**
      - A radio is a body (`BodyKind.Radio`). Picked up with Use, it goes on the belt, one each, so the hands stay free for crates and lamps.
      - Right mouse with empty hands sets it down to pass on.
      - The dead drop theirs where they fall. Its red lamp stays lit, so a dropped one can be found in the dark.
    - **Only wearers are on the radio.** The host checks both ends of every radio frame: a speaker without one isn't on the radio, and a listener without one hears nobody on it. The client doesn't send it either, and the HUD says NO RADIO.
      - GDD §24's "radio breaks: shouting down the length of a moving train" is now something a crew can do to itself: lose them.
      - The spec's "dies in tunnels and mine spurs" now covers the mine spurs too (`Run.Underground`): aboard a rake down a mine head's spur, or on the ground beside it.
    - **The kit** (`train.json` `kit.radios`): the train leaves with 2. One sits at the back of the cab floor, clear of the firebox so a stoker's Use doesn't pick it up. The rest are in the guard van.
    - **Legacy.** Until the train has been stocked, `Bodies.RadiosCarried` is false and the radio is the button everyone has, as before, so the voice tests and a bare world still work. A client that has seen a radio in a snapshot knows they're things tonight.
    - **Interior occlusion was already done** (`PlayerMotor.Space`: a car shut up is its own space, and an open door makes it the outside's). `VoiceRoutingTests` pins it now.
    - **Verified:**
      - `RadioTests`: the train leaves with its radios; a radio goes on the belt and the hands stay free; one each, passed on by setting it down; the dead drop theirs; routing without one.
      - Over loopback, the host puts only wearers on the radio: nobody's heard until both the talker and the listener wear one.
    - **Not yet:**
      - breaking a radio (damage);
      - F.3's radio range upgrade;
      - a radio on the floor heard in the room (a Soot Child's way in).
44. **The Soot Children (T40, App. A.4, B.6, spec A.5).** "Outside in the dark, calling for help in your crewmates' voices." They are the one enemy made of the voice system itself.
    - **Listening** (`VoiceMemory`, host only). Every frame the host forwards from a living speaker is kept: the last 250 frames (5 s) each, and when they last spoke.
      - That's the "samples crew proximity voice" of A.4. It's also B.6's gate "requires recent proximity voice activity".
    - **Who, and where** (`SootChildren.Choose`).
      - The car with the most of the living crew shut inside, so there's someone to open a door. The cab doesn't count: it has no door to open.
      - The voice is someone who has spoken lately and *isn't* in that car. It's the friend the listeners think is outside: GDD §23's "the failure is opening a door for a friend who is standing right next to you".
      - None for a crew under 2, nobody talking, or nobody shut in.
      - It crouches 6 m out from the car's side, the side with a door if it has one.
    - **The spine.**
      - **Telegraph:** the call. Every 7 s it replays the last thing that voice said (up to 2.5 s of it). The fairness rule holds: nothing can answer it inside the reaction window.
      - **Commit:** somebody answered: a door of its car opened (the one at that door is taken), or someone on the ground within 7 m.
      - **Punish:** 200 damage, `DeathCause.Taken`.
      - **Decay:** ignored for 30 s, or left behind by the train, it gives up.
    - **The call is the tell.**
      - The host plays it as `VoicePath.Mimic` frames (with the enemy's id), at the voice's own 50 frames a second, with a sequence of their own.
      - Everyone living within 40 m of it hears it; walls still muffle it.
      - The client plays each Soot Child as its own stream, keyed by the thing, not the crewmate. The frames are replayed, so the crewmate's own decoder would drop them as stale.
      - It plays from where the thing is, at one loudness however far (`voice-mimic.json`: rolloff 0). That's spec A.5's missing falloff, the thing a crew learns to hear.
    - **The director** (App. B.6): near facilities (within 250 m of one), never with another about, cost 4. It's weighted up per crew member outside.
    - **The harness never meets them.** Bots don't talk, so there's no voice to steal, which is App. B.6's own rule.
    - **Verified:**
      - `SootChildrenTests`: it calls in the voice of someone who isn't there, from off the car's side; it needs a voice to steal and someone to fool; opening the door takes the opener, but not inside the reaction window; going out to it takes you; ignored, it gives up, having called the whole time.
      - Over loopback, the host plays the stolen voice: every frame of it, in order, at the voice's pace, labelled as the Soot Child's, to those near, and not beyond its radius.
      - `dt screenshot --threats` stages a huddle off car 2 (CI's `threats-soot.png`).
    - **Not yet:**
      - trying a different voice after a decay (it's a new spawn instead);
      - a radio lying on the floor as a way in (GDD §32 open question 4: the corrupted on the radio);
      - its own sound for the moment it takes someone.
45. **Editor v2: the rail and the modules (T44, roadmap M5 "simple editor (module + rail + tuning)").**
    - **Rail.** The routes page has a track table: the line from the fortress, piece by piece (length, curve radius, grade), with pieces added after any other or taken out. Preview redraws the plan and the profile.
      - Save refuses track that can't be laid: a piece with no length, or a curve tighter than the generator lays on any tier (the least `minRadius` in `route.json`, 250 m today; the page shows it).
      - Branches (dead lines, spurs) still come with the features. Moving a junction feature moves where the generator put its branch the next time it's generated, not the saved branch: that's the next step.
    - **Modules.** A facility can have its own loading modules (`RouteFeature.Modules`: "crates", "winch"). Unset, it has its kind's (`facilities.json` `kinds`), which is all any facility had before.
      - The page shows each facility's kind's set, faint, until one's ticked. Then that facility has its own set, which can be reset to its kind's.
      - `FacilityTuning.ModulesOf(feature)` is what the run lays out from.
      - The route model keeps them as names, so it doesn't depend on the run's types. Preview and save both refuse a name that isn't a module, and modules on anything but a facility.
    - **Tuning.** `facilities.json`, `vigil.json` and `campaign.json` are editable now, each checked against its record on every edit like the rest. `vr.json` and `look.json` are the game's, which the editor doesn't reference.
    - **Verified:** `EditorTests`:
      - the new files are listed;
      - a facility given crates only, with the third piece of track bent the other way and steeper, previews differently, saves, and lays out exactly crates when played;
      - unknown modules and a curve a metre tighter than any tier lays are refused, and nothing is written.
      - `dt edit --screenshot` shows both tables.
46. **Two to lift, two to turn (T43, spec D.2, roadmap M4 "two-handed grips").**
    - **The winch's cranks.** The drum lies along the track between two cranks, half a turn apart (`facilities.json` `winch.crank`). Each manned crank has a pace:
      - a keyboard holding E, the crank's own (0.5 turns a second, which hauls at the winch's `speed` as before);
      - a reaching hand (VR), how fast it goes round the crank's circle: forward only, smoothed over 0.25 s, and no faster than the crank's pace. A headset is no stronger than a keyboard, as with the shovel (T29).
      - The drum goes at the slower crank's pace, and stalls when one is below `inRhythm` (0.6) of the other. That's D.2's "desync stalls it", and D.3's "two people out of rhythm".
      - A hand is on a crank anywhere within grab of the circle its grip goes round, not only at the grip. The drum's angle reaches a client a snapshot late, so a hand following the drawn grip lags it; the circle doesn't care.
      - **Ambiguity:** on the keyboard, holding E is always in rhythm, so two keyboards never desync; the stall comes from letting go, or from a headset out of step. A keyboard rhythm (strokes on a beat) is the obvious next step if play-tests want the failure on flat screens too.
    - **Heavy crates (D.2 "heavy items need two").** A few per crate stack (`crates.heavy`: 1–2, each half a car's load), set apart from the stack so a hand reaching for a light one doesn't get one.
      - One on it holds an end: it stays where it lies, you're slowed, and you let go by walking off. The other end taken, it rides between the two carriers' hands in the first one's frame, both at the heavy pace. Either lets go, or they get more than `span` (2.4 m) apart, and it's down. Nobody throws one.
      - Put down inside a cargo car, it's stowed like a crate.
      - `Body.Second` is the other carrier, replicated in the body record.
    - **Both hands (VR).** The intent carries the other hand as well as the reaching one when it's tracked (`PlayerIntent.Other*`, a flag byte and three more centimetre shorts on the wire). A headset takes its end of a heavy crate only with both hands on it. Nothing else reads the other hand yet.
    - **Bots** take them in pairs (T45, note 47).
    - **Verified:** `TwoHandedTests`:
      - a hand going round at the crank's pace hauls as a keyboard does, and faster hauls no faster;
      - 0.8 of the pace goes at 0.8; much slower, backwards or still stalls it, out of rhythm;
      - a hand off the circle or on the axle isn't on the crank;
      - a heavy crate held by one stays put; taken by two, it goes between them at the heavy pace; dropped by either, or pulled apart, it's down;
      - carried into a car together, it's loaded as half a car;
      - a headset with one hand on it can't take it, with both it can;
      - a client sees the drum's angle, the stall and both carriers.
      - `dt screenshot --site --crank` shows the cranks; CI keeps `site-crank.png`.
47. **Bots carry heavy crates in pairs (T45, spec D.2).**
    - A hand needs its own player id to know which end is whose (`StopHand.PlayerId`). The harness sets it; a hand without one leaves heavy crates to others, as before.
    - The light crates go first. Once none is loose and there are two crate hands that know themselves (`CrewCalls.HeavyHands`), one takes an end of a heavy crate and waits.
    - Anyone holding one alone, a bot or a player, gets a hand: the nearest free crate hand comes to the other end. It stands across the crate from the holder, if the holder has said where they are (`CrewCalls.Standing`), and takes hold.
    - **The front end** walks it in exactly as a light crate: to the car with room, up the side steps, in by the door, down in the middle.
    - **The back end** follows the crate's own trail a metre behind it. It needs nobody's position, only where the crate has been. It takes the steps and the door in the order the front end did, and it follows a player's lead the same way.
      - This is stable: the crate rides at the midpoint, so the back end settles 0.8 m behind the front end's hands, well inside the 2.4 m span.
    - **What keeps the train.** A heavy crate keeps it while it's up, put down in a car, held by one with a hand to lend, or loose with two to take it. Otherwise it doesn't, so a crew without ids isn't held at a stop by freight it won't touch.
    - **Found on the way:** a hand with no car to go to (every car's room spoken for by crates on their way in) used to go aboard. That marked the stop done for it, so if the room then ran out, nobody came back to shut the doors. It waits on the ground now. Heavy crates made "more freight than room" common enough to show it.
    - **Verified:**
      - `StopCrewTests.TwoHandsCarryTheHeavyCratesInTogether`: a crates-only stop loads every crate, light and heavy, up to the room there is; the back end is walked; everyone's aboard; the doors are shut.
      - `AHandComesToHelpAPlayerHoldingAHeavyCrate`: a player holds an end, and a bot takes the other.
      - The frontier:7 harness night delivers (net 2614: the stop's timeline moved, and a hound mauled one crewmate, who was revived at the gate). Its Switchyard stop still ended on the driver's 420 s give-up, as it had before; T50 (note 48) fixed that.
48. **Why the harness's crate stop never finished (T50).** frontier:7's Switchyard stop ended on the driver's 420 s loading give-up on every night. `StopCrewTests`' crate stops never did, because their bots see the host's world directly; the harness bots are clients. Three faults, found by logging what each hand was doing when the driver gave up:
    - **Clients never saw a body at rest.** The body record carries "asleep", but the client's stand-in dropped it. A crate put down on a car's steps (to open the door) is only picked up again once it lies still, so on a bot's client it never was. `PbdBody.Sleep()` lets the mirror adopt the flag.
    - **Room was counted as taken by crates that weren't in the car.** A car's room had pending crates subtracted, meaning every crate lying on the car, including on its steps. So those stranded crates held the room of two cars forever.
    - **A hand with no room went aboard,** which marks the stop done for it. With every hand aboard and two heavy crates still wanting carrying, the driver waited out the give-up. It now waits where it is. (T45 had it wait only on the ground; on a car's landing it still went aboard.)
    - While there, the door dealing was fixed too. Doors to shut were dealt out by position to the crate hands, including ones that had gone aboard or away to warm, so a door could be dealt to nobody who'd come. Now each hand claims the nearest open door no one else still at it has claimed (`CrewCalls.ClaimDoor`).
    - **Result:** the stop loads in 272 s instead of giving up at 420, and the night is 143 s shorter. It still delivers: 0 deaths, worst correction 0.35 m, fairness 0.
    - **Verified:** `TwoHandedTests.AClientSeesACrateAtRest`, `StopCrewTests.EachOpenDoorIsShutByOneHandThatsStillAtIt`, and the harness night.
