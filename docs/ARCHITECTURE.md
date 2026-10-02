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

**Status (art pass v2, §8 note 48).** The renderer draws the art pipeline plan's look: a sky pass (gradient, hazy moon, clouds, a 360° backdrop band of far silhouettes), the scene in a float target (point-sampled texture arrays with box-filtered mips and a positive bias, two-layer terrain blend, alpha test, per-pixel practical lights, Blinn-Phong speculars from spec maps, exponential height fog), a blended effects pass (flipbook smoke and steam, additive sparks and glows), then half-res bloom, a 16³ LUT grade, vignette, grain and the ordered dither into reduced colour depth. The kits (`DarkTerritory.Game/Art`) build the train over the sim's own collision, and the track, lineside and structures along the line; textures come from `tools/art/textures.py` (CC0 photo sources through a PBR-to-legacy converter, plus procedural ones). `dt art check` holds every piece to its budget; `dt art show <piece>` turns one on a turntable. Not yet: the lamp's shadow map, multiview, normal maps on the cab.

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

### 6.10 The procedural line (docs/design/linegen-plan.md)
- **Where:** `DarkTerritory.Sim/LineGen` (engine-free: the plan's `LineGen.Core`, `.Validate` and `.Terrain` modules), `content/linegen/*.json` (every number; the plan's §19.2 files), `DarkTerritory.Game/Art/PlanArt.cs` and `SignKit.cs` (the plan's `LineGen.Build`), `dt linegen` (its tools).
- **Pipeline:** run parameters → route graph → leg scripting (set pieces, budget curve, quotas) → alignment (clothoids, closed alternates) and profile (grade runs, vertical curves) → structures and terrain intents → stations and names → weather, tags, exposure → authority (limits, restricted zones from sightlines) → signage → director context → route card → validation on the real `TrainOnLine` (ideal and sloppy drivers) → retries → fallback seed. `LineGenerator.Generate` never throws if any attempt built.
- **What the rest of the game sees:** a `Route` (`PlanRoutes.ToRoute`: its features, branches and weather, as the prototype generator made them), with the `LinePlan` riding along (`Route.Plan`). Everything that read a route still does; what reads the plan is new: the track's lethal rules (`TrackRules`), wet rail and brass (`PlanConditions`, the rail model's `ITrackConditions`), the terrain under players (`TerrainField`, via `PlayerMotor.GroundAt`), the bots' speed (`LineAuthority`), the director's context, the art.
- **One call:** `Routes.Generate(content, spec, cars)` is every night's line, cached per process. `tiers.json` `"routes": "legacy"` hands nights back to `RouteGenerator`, which stays for its tests and tools (`dt route gen`).

### 6.9 Coordinates and units
Metres, seconds, kilograms (tonnes and kN in the train sim, so the spec's numbers read directly), right-handed, **+Y up**, **−Z forward** (glTF convention). `double` for distance along the line and for world anchors, and `float` for everything local.

## 7. What is deliberately *not* in scope
Terrain sculpting tools, a node-graph material editor, a general-purpose visual scripting system, console platforms, host migration, dedicated server hosting (we support a headless host binary for testing, but the ship model is player-hosted).

## 8. Open technical questions
1. **Standalone Quest** (Android) at launch, or PCVR only? PCVR is assumed. Standalone doubles the performance and platform work.
2. **Crossplay between Steam and itch.io players?** It is possible via EOS for both, at the cost of Steam-native lobbies and invites. The current assumption is two separate pools.
3. **Brake fade on the flat**: spec B.5 is ambiguous. The sim applies fade only on descents, because otherwise the spec's own 20-car stop (63 s / 690 m) can't happen. See `content/tuning/train.json` → `brakeFade.onlyOnDescent`.
4. **"8+ players"**: the design says ideal 3–4, max 8. The net budget above targets 8 with headroom to 12. Beyond that needs a decision about train length and voice zones.
5. **Jump-off speed is measured over the ground**, not as train speed alone (`PlayerMotor.Land`). Running off sideways adds to the train's speed (and to the knock you take). Since T90 the lethal landing is 16.5 m/s, three times run speed, from the playtest; before it, at 4 m/s, sprinting off a yard-speed train was lethal. Conversely, running rearward along the roof before jumping could let a player survive a slightly faster train. That's emergent and arguably fair ("your speed over the ground"). The alternative is to test train speed only; it's a one-line change if the director prefers the simpler rule.
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
    - Holding Use while pushing toward a ladder grabs it (since T90, so does walking straight into its foot). Holding Use standing still works whatever you're at: firebox, valve, brake wheel. The coupler plate is the exception since T91: its coupling is cut with Uncouple, held, looking down.
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
      - the coupler plate across each gap bridges end door to end door: it lies on the doors' line (`interior.doorX`) and is a hand wider than the doorway (1.0 m to the door's 0.9 m), so stepping off it is stepping through a door. The end ladders stand just clear of its right edge (`GeometryTuning.EndLadderX`), and the roof's brake wheel keeps its place inboard of the ladder's top (a hand reaches it from the ladder; the feet don't). Until this the plate was centred on the car and the doorway overhung its left edge;
      - cargo stacked down the right-hand side of cargo cars.
    - **The guard car** (GDD §10: "rear gun, tool storage, the back door") has a tool locker and a hatch ladder up to the rear gun from inside.
    - **Doors are state.** They're a replicated bitmask on each vehicle and start shut. You open or shut one by holding Use while facing it, from inside or from the coupler plate outside.
      - Facing matters because the plate is in reach of two doors. (Until T91, Use on the plate also cut the coupling.)
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
      - It climbs outside and kills at 1200 s (`DeathCause.Cold`); inside a car with a door open it climbs at a quarter of that rate.
      - Near heat it falls at 1200/20 per second, so even the nearly frozen are recovered within spec B.2's "20 s near heat". That's our reading of "resets in 20s".
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
      - **The revived** come back in the cab with `PlayerFlags.Revived`: cold onset halved (spec's 300 s; death stays at 1200), light things only (lamps), and no guns until the run reaches a stop other than the one they came back at, or the terminus.
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
      - **The plate is a metre wide.** The doorway is in line with it and the end ladder just off its right edge. Any sideways step on the plate at speed is off it and a death. So the bot crosses between door and plate on the doors' line, steps straight in and out, and stands in reach of the ladder rather than at it.
      - **Use on the plate cut the coupling unless you were facing a door** (before T91). So the bot only presses it when `CrewActions.Nearest`, the sim's own rule, says a door is in reach.
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
      - The frontier:7 harness night delivers (net 2614: the stop's timeline moved, and a hound mauled one crewmate, who was revived at the gate). Its Switchyard stop still ended on the driver's 420 s give-up, as it had before; T50 (note 49) fixed that.
48. **Art pass v2: the look from the art & animation pipeline plan (GDD §25-32).**
    - **Renderer (Ballast.Render).**
      - Passes: sky → scene (float target) → effects → bloom (half res, threshold, two blurs) → composite (LUT grade, vignette, grain, dither and colour levels) → overlay. `ColorImage` is still the frame, so the window, the headset and readback didn't change.
      - The frame's constants moved from push constants to a uniform buffer (the push constants' 128 bytes were full); push constants now carry a draw's model matrix and tint.
      - Vertices grew to 80 bytes: texture coordinates, a layer in the material array, and a second layer with a blend weight (the terrain). A layer past what's loaded draws as flat colour, so a renderer without the look's textures still draws the greybox.
      - Materials are one 256² texture array for diffuse (sRGB, alpha = cutout) and one for spec (R strength, G gloss → Phong 4-128, B emissive mask). Nearest sampling with box-filtered mips and a +0.4 bias, no anisotropy: the pipeline's "intended pixel crawl".
      - Practical lights are per pixel (up to 32, nearest kept), no longer baked into vertices as geometry is added.
      - Cooked `MeshAsset`s (the train, structures) are uploaded once and drawn by transform (`MeshBuilder.Instances`); small pieces (trees, tufts, poles) are baked into the frame's soup (`MeshBuilder.Append`), cheaper than hundreds of draws.
      - Effects are billboards facing the eye *point*, not the view plane, so a headset's two eyes see the same sprite, and they're all computed from the time and the train's state (puff `i` left the stack at a known time), so screenshots repeat and clients agree without replication.
    - **The kits (DarkTerritory.Game/Art).** A small modelling kit (boxes with chosen faces, bevelled prisms, faceted cylinders, lathes, panels; texture coordinates in metres) and the pieces built with it:
      - `TrainKit`: the engine (an armoured 2-8-0: octagonal plate casing over the boiler, a lamp box like an eye, spoked drivers and rods, cab with backhead, gauges and firebox frame, tender with heaped coal), cargo cars in three liveries, the guard van (lit windows, stovepipe), doors and the gun. **Built over the sim's `CarShape`**, so they follow the train tuning, and what you see is what you collide with (the one exception is above head height: the cab's visor).
      - `WorldKit`/`WorldArt`: the ground (bed, shoulders, ditch, verge, then hills), sleepers and rails, poles with sagging wires, pines and dead trees on crossed alpha cards, tufts, rocks, fences, dead signals.
      - `StructureKit`: masonry viaducts over a gorge and timber trestles for weak bridges, tunnel portals and brick bores under a hill, fortress walls, towers and gatehouse, the home station's platform and canopy with a lantern to each bay, facility buildings by kind, buffer stops and switch stands.
      - `Effects`: stack smoke and cinders, cylinder-cock steam, brake sparks, the headlamp's beam and halo, fog banks.
      - The greybox path is untouched: `--greybox` (and any scene without a `Look`) draws it as before.
    - **Ground where the sim stands you.** Players off the train stand at rail height (`PlayerMotor.GroundAt`), so the ground stays within 0.3 m of it for the first 12 m either side (the bed, a shallow ditch, the verge). Hills rise only beyond 16 m, the gorge under a bridge and the hill over a tunnel are drawn, not simulated. When the sim gets terrain, `WorldArt.Ground` is the function to share.
    - **Ambiguity, the fog's shape:** the pipeline asks for "exponential height fog"; the route's weather sets a density whose 1/e distance is the visibility the spec plays with. Plain exponential fog made the near ground murky and the middle distance soup. `look.json` `atmosphere.fogCurve` (1.45) raises the optical depth to a power, which keeps the fog exactly as it was at the 1/e distance, clearer nearer, thicker further. A curve of 1 is the old fog.
    - **Aerial perspective anchored to the fog:** geometry converges to the fog colour, so the sky's horizon haze is `horizonGlow` brighter and the backdrop's darkest layer *is* the fog colour. Otherwise far things read darker than near ones.
    - **Assets as text and scripts:** textures are PNGs generated by `tools/art/textures.py` from CC0 sources fetched by `tools/art/fetch_sources.sh` (never committed; provenance per texture in `content/art/textures/index.json`) and procedural generators. Models are C# (the kits) or Blender scripts (`tools/blender`, the creatures); the cooked files are committed so a build needs neither Python nor Blender.
    - **Verified:** `dt art check` (every piece within its class's triangle budget: the engine is 7.7k of 45k); `LookTests` (materials map, lights stay clean, worn surfaces break up without burying the room, wear rides with the car); every `dt screenshot` view looked at, and `dt art show` for the pieces.
    - **The crew and the creatures (tools/blender, Ballast.Assets, Art/CreatureArt).** Eight models are built by Blender scripts run headless (`tools/blender/build.sh`, byte-identical rebuilds, about 10 s). They are the crew in four variants, the cinder hound, the Sleeper, the Clinger, the Hollow, the Switchman, the Soot child and the Dragger's limb (note 50: one up over the eave while it reaches, two gripping; nothing while it's under the lip, as the greybox has it). They sit on the pipeline's template skeletons (SK_Human, a quad rig, chain rigs), with hand-keyed clips at 30 fps played stepped, as the era did.
      - `Ballast.Assets` reads the .glb (a small GLB reader, no dependency), resamples clips to 30 fps and skins on the CPU into the frame's soup.
      - Each creature stands where its greybox stand-in did, so gameplay reads the same. The scene asks `CreatureArt` first and falls back to the boxes.
      - Crewmates walk or run by how fast they've moved since last drawn. The snapshot doesn't carry a gait, and a frame's lag in one is presentation only.
      - A headset player's reported hands (T47, note 51) are reached for by two-bone IK on the model's own shoulder and arm lengths, bent toward `Arms.Pole` as the greybox figure is. An arm with no hand reported keeps the clip's swing.
      - The dead are the crew model fitted to the ragdoll's 11 joints, wearing the body's owner's variant. The torso turns onto the pelvis→chest line and the line across shoulders and hips; then each limb bone swings onto its joint, parents first (`Skinner.Place`, `Skinner.Aim`). The ragdoll stays the truth: the sim knows nothing of the model. The chest lamp is down to an ember, so a body reads as dead from a distance but can still be found in the dark to carry back.
      - `CreatureArtTests` pins budgets, bones, clip rates, clean loops, deterministic skinning, every phase drawing, the dead lying inside their ragdoll with head, hands and feet on its joints, and the turntables (`out/shots/creatures/`).
    - **Wear and tear (Art/DamageKit, the scene shader's scar mask; `look.json` "damage").** The pipeline plan asks for "3 damage states per car; scars persist between runs as decal and mask layers" and a "damage-mask blend" shader.
      - Each car's state comes off its integrity: sound, damaged below 0.66, wrecked below 0.33.
      - The scar mask grows from 0.95 down to 0.1. `MeshInstance.Scar` carries the amount and a per-car seed. The mask lies in the piece's own texel space, so a car scars in the same places every frame and every run. It shows scorch with a blistered-rust edge, scraped streaks, and a few punctures.
      - Past the first state `DamageKit` adds what a mask can't: plate torn back, claw gouges, and, once wrecked, breaches with the frame showing. A wrecked car keeps its damaged marks and adds to them, so getting worse never moves a scar. The marks stay clear of the side doors, which slide.
      - The engine takes the mask only. It is the hero asset, and its casing isn't the plain walls the kit's marks sit on.
      - `dt screenshot --integrity a,b,…` sets each car's condition. `DamageTests` pins the states, the seeds, the monotony, the placement and a render that shows without burying.
    - **Ambiguity, scars that persist:** the plan wants scars kept between runs, but the campaign stores each car's integrity (`CarState`), not a history of hits. The scars are therefore a pure function of the car and the integrity it has lost. They come back where they were, and a repair takes the newest first. A per-hit record would need the sim to keep one, which is the gameplay lane's call.
    - **Not yet:** normal maps on the cab; the engine's own damage geometry and leaks; LODs (the fog caps view distance at 60-120 m, and the budgets hold at LOD0).
49. **Why the harness's crate stop never finished (T50).** frontier:7's Switchyard stop ended on the driver's 420 s loading give-up on every night. `StopCrewTests`' crate stops never did, because their bots see the host's world directly; the harness bots are clients. Three faults, found by logging what each hand was doing when the driver gave up:
    - **Clients never saw a body at rest.** The body record carries "asleep", but the client's stand-in dropped it. A crate put down on a car's steps (to open the door) is only picked up again once it lies still, so on a bot's client it never was. `PbdBody.Sleep()` lets the mirror adopt the flag.
    - **Room was counted as taken by crates that weren't in the car.** A car's room had pending crates subtracted, meaning every crate lying on the car, including on its steps. So those stranded crates held the room of two cars forever.
    - **A hand with no room went aboard,** which marks the stop done for it. With every hand aboard and two heavy crates still wanting carrying, the driver waited out the give-up. It now waits where it is. (T45 had it wait only on the ground; on a car's landing it still went aboard.)
    - While there, the door dealing was fixed too. Doors to shut were dealt out by position to the crate hands, including ones that had gone aboard or away to warm, so a door could be dealt to nobody who'd come. Now each hand claims the nearest open door no one else still at it has claimed (`CrewCalls.ClaimDoor`).
    - **Result:** the stop loads in 272 s instead of giving up at 420, and the night is 143 s shorter. It still delivers: 0 deaths, worst correction 0.35 m, fairness 0.
    - **Verified:** `TwoHandedTests.AClientSeesACrateAtRest`, `StopCrewTests.EachOpenDoorIsShutByOneHandThatsStillAtIt`, and the harness night.
50. **The Draggers (T46, App. A.4, B.4, spec B.3).** "Reach up from beneath the car edges. RULE: stay off the edges." A flank threat on the shared spine (`enemies.json` `draggers`).
    - **Where.** Under one edge of one car, and it never leaves that car. App. B.4 has them "pre-attached … dormant until a player is on the roofs"; the director makes that literal. It offers them only while someone's on a roof, under a car being walked, weighted by the roof walkers, at most two at a time, cost 2.
    - **Dormant:** under the lip, it follows the nearest walker on its side along the car. Nothing to see or hear.
    - **Telegraph:** a walker within `grabRange` (1 m) of its edge and near it along the car. A limb comes up over the lip with a single scrape (`dragger-scrape.json`, 2–4 kHz, spec A.4).
      - Stepping back to the centreline in time sinks it back under, and it won't reach again for `rearmSeconds`.
      - The grab waits for App. A.1's reaction window (1.5 s), which is longer than the GDD's "~1 s".
    - **Grab:**
      - **Alone:** after a beat, you're pulled off over the side (`DamageEvent.Pull`, `PlayerMotor.PullOff`). Spec B.3's one threshold decides it: faster than a survivable jump, that's death (`DeathCause.Dragged`, and the body goes over the side); slower, you land on the ballast and the train goes on.
      - **With someone within 4 m:** they have two seconds to pull you free: Use, within 1.5 m of you. After that, you're pulled off anyway.
    - **Speed.** Spec B.3's "Max 22 m/s: Draggers +50% grab range" is a ramp from cruise (14) to max (22), so there's no cliff at 21.9 m/s.
    - **The pull is the host's move.** It puts the player in the world frame outside the car, falling, and bumps `Placed`, so a predicting client adopts it rather than correcting it.
    - **Seen:** a pale limb just outside the eave, hooking in over the roof. Two limbs when it's grabbed. The HUD tells the grabbed and whoever's near them. `dt screenshot --threats` stages one; CI keeps `threats-dragger.png`.
    - **Heard:** the scrape passes `AudioTests`' "tier 1 is inviolable": +23 dB over the bed on that car's roof in maximum chaos, and the Clinger's drill, which shares 3–4 kHz, stays as audible as it was.
    - **Bots** walk the centreline, so they're never taken. On frontier:7, two Draggers woke and punished nobody. They did take the Flank slots the Clingers used to get, so that night's mix moved (more hounds): 4 were mauled, none lost for good, delivered.
    - **Verified:** `DraggerTests` (10): the centreline is safe; near the edge, it telegraphs, then pulls you off, and at 14 m/s that kills; at 2 m/s you're left on the ballast; stepping back in time sinks it and it rearms; with a mate, the window and the rescue, or no rescue and taken; farther reach at max speed; the other edge isn't its edge; the director wakes them only for roof walkers; a client sees the limb and who it's got.
51. **The crew see a headset's arms (T47, roadmap M4 "VR body IK").**
    - **The hands go out with the rest of the player.** The player record now carries the reaching hand and the other hand, on the centimetre grid the intent brought them in on. They're zero for a keyboard or a bot, so delta encoding makes them free: 8 bots' snapshots are exactly the size they were (160 bytes, 37.6 kbit/s down). Remote players' hands are interpolated with their feet.
    - **Prediction is unchanged.** A client's own hands still come from its own intent each tick, not from the host's echo, and a correction measures position only.
    - **Two bones from the shoulder** (`Game/Arms.cs`, 0.30 m and 0.32 m): the elbow bends down, out and a little back, like a person's. A hand past the arm's length is reached for as far as the arm goes; the sim lets a hand go 0.8 m across from the body because a real player leans, and the figure doesn't lean yet.
    - The sim doesn't say which hand is which, so each reported hand goes to the arm on its side. A single hand leaves the other arm hanging, and a keyboard player's arms hang.
    - **Not yet:** a spine or neck that follows the headset, legs that step, and hands that hold what they're carrying. That's the rest of M4's body IK.
    - **Verified:**
      - `ArmsTests`: bone lengths hold, the hand arrives, the bend is outward, an out-of-reach hand reaches as far as it can, and hands are assigned to sides.
      - `HandTests.TheRestOfTheCrewSeeAHeadsetsHands`: over loopback, another client sees both hands, and sees them drop when the controllers go down.
      - `dt screenshot --view roof --crew` stages three crewmates on a roof: arms hanging, one reaching up, one holding out both hands. CI keeps `crew-arms.png`.
52. **The gantry crane (T48, spec D.2 and D.3; GDD's foundry: "overhead crane run from a gantry. The operator can't see the ground crew").** The third loading module (`facilities.json` `crane`). The foundry has it, along with the winch and crates.
    - **Layout.** A gantry astride the track over the first cars behind the engine at the buffer stop, its castings stacked on the far side. Like the rest of a site, it's laid out from the route, so every machine agrees where it stands.
    - **The operator.** Holds Use at the control stand at the near leg, and is up in the cab.
      - The stick runs the bridge along the track and the trolley across; Space and B move the hook up and down; left mouse lets go.
      - At the controls, the stick drives the crane, not their feet (`PlayerFlags.Operating`). That's worked out from where they stand and what they hold, the same on every machine, so a client predicts standing still.
      - **Ambiguity, the elevated cab:** the world has no climbable structures, so the stand is on the ground and holding it puts you in the cab. Your view moves up there (`Eyes.Operator`) and looks along the gantry at the bridge, not down at the hook. That's spec D.3's "blind instruction": the ground crew have to call the position. In a headset, the view stays with the head (a cab you can stand in comes with climbable structures).
    - **The ground crew.** Hold Use on the ground at the hook, with the hook low enough to reach, beside a casting, for 2 s: it's rigged.
    - **Setting down.** A casting set down (its base within 0.6 m of what's under it) on a cargo car's roof is lashed there and loaded, half a car each. It rides the car from then on. Set down on the ground, it can be rigged again.
    - **"Dropped loads kill."** Let go of higher, it falls: the casting is lost, and anyone within 1.4 m of where it lands is crushed (`DeathCause.Crushed`). The world applies it on the next tick, through each player's crew step, since the run doesn't hold the crew with their ids.
    - **Replication:** a Crane record per crane: bridge, trolley, hook, the rig's progress, and each casting (state, car, place).
    - **Bots** don't run cranes. Castings don't keep the train, and the driver stops for the winch and crates as before.
    - **Verified:** `CraneTests` (5):
      - the operator drives it from the stand and stays put, and letting go stops it;
      - rigged on the ground, lifted, driven by the stick over a car, and set down on its roof, it's loaded;
      - let go of high, it's lost and kills the one under it but not the one beside;
      - nobody at the controls, nothing moves, and a hook up in the air can't be rigged;
      - a client sees the crane.
      - `dt screenshot --site --crane`; CI keeps `site-crane.png`.
53. **Mods v1 (T49, roadmap M7 "mod loader v1"; CLAUDE.md: "content/ is also the base mod").**
    - **Where they live.** A mod is a folder with a `mod.json` (name, version, description, order, enabled), in `mods/` beside `content/` or in the user's app data (`DarkTerritory/mods`).
    - **What a mod file does.** It's laid over the base content at the same path:
      - a new path adds a file, and the same path replaces it;
      - a JSON file marked `"$patch": true` is merged into the one below it, key by key, so a mod can change one number without copying the file. Arrays are replaced whole.
      - Mods go in `order`, then by name; a later one wins.
    - **One content root, as ever.** `ContentMods.Mount` writes the merged copy to app data (`content-with-mods`), rewriting only what changed, and everything downstream (hot reload, the content hash, the sim) reads it. With no mods, the base content is used untouched. The copy lists its mods in `mounted-mods.json`.
    - **Multiplayer.** The content hash already covers every file, so different mods can't join. A refused joiner is now told which mods the host has and which they have, instead of just "content differs".
    - **Switches.** `--no-mods` gives the base game, in the app and in `dt`. `dt edit` edits the base content, not the mounted copy. `dt mods` lists the folders, the mods in load order, and what each does to which file.
    - **Not yet:** mods can't add code, the Workshop, and an in-game mods screen.
    - **Verified:**
      - `ContentModsTests` (4): with no mods, the base content is used as it is; mods replace, add and patch in order; taking a mod out takes its files out; a patch with nothing under it is refused.
      - `NetPlayTests.AJoinerWithDifferentModsIsToldWhichMods`.
54. **The Rattle (T51, App. A.5 and B.5; GDD: "lives in the couplings. You hear it before you cross. RULE: don't cross between cars rattling").** `EnemyKind.Rattle`, costing 1 (App. B.2's table), tuned in `enemies.json` `rattle`.
    - **Spawn.** During a facility stop (the run `AtFacility`), on an engine's rake of two cars or more, one at a time. It takes a gap in that rake, mid-train by preference, and never one someone's standing in. It gets more likely by 0.25 per extra car.
    - **The gap.** The space between two coupled cars' ends, as wide as the cars, from the ground up to half a metre under the roofs.
      - **Ambiguity, what "the gap" is:** the coupling plate is how you cross on a moving train, and at a stop you'd walk between the cars on the ground. Both count.
      - The roofs are over it: that's App. A.5's "route over the roof", and someone up there doesn't wake it.
    - **The spine.**
      - Silent until someone comes within 6 m of it below the roofs. Then it rattles, and that's the whole tell (a dry bone rattle, 2–5 kHz, spec A.3).
      - Step into the gap while it rattles, once it has rattled for the reaction window (App. A.1), and you're pulled under: `DeathCause.PulledUnder`.
      - With nobody near for 10 s it goes quiet again.
      - It can't be shot. Cut the cars apart at its gap and it goes with the coupling.
    - **Ambiguity, when it leaves:** App. B.5 spawns it at a stop but doesn't say when it goes. It stays 300 s (`lingerSeconds`), so it's often still there as you pull out, then goes, rattling or not.
      - At first it went only once it was quiet after that. On frontier:2 the bots' warm-ups kept it rattling, so it rode the night out in a gap they used, and it took four of them (T54).
    - **No visual on purpose:** "Pure audio tell." Neither the greybox nor the art pass's creatures draw it (`CreatureArtTests` knows it's never seen); the rattle plays from the gap, muffled by a car in between like any other sound.
    - **Bots heed it** (`Heed.Rattles`, applied to every bot's intent in the harness). A bot whose next step would take it into a rattling gap stands still instead. One already in the gap when it starts (on the coupler plate, say) walks out the quickest way, never off the train at a speed that kills (`RattleTests.AtSpeedABotOnThePlateNeverStepsOffToGetOut`). Worked out on its client by stepping a copy of itself; the rest of its intent goes through.
    - **Found on the way: a dead shunter stalled the night.** Adding the Rattle to the director's options changed its draws, and on frontier:7 the hounds mauled three bots, the shunter among them. Parts were fixed at the start, so nobody set the Switchman's switch back, and the train stood at it for the rest of the night.
      - Now the first living hand with a part of its own takes over a part whose holder has died (`CrewCalls.StandIn`), between stops, as a crew would sort it out on the radio.
      - With that, the night delivers as before: net 3890, nobody lost, the Rattle in a gap at the Switchyard.
      - `dt harness --trace` now writes the enemies out there too (kind, phase, car or place), which is how this was found.
    - **Verified:**
      - `StopCrewTests.WithTheShunterDeadTheFirstHandLeftTakesItOver`.
      - `RattleTests` (11): silent until someone comes near on the ground, not from the roof; a bot already in the gap gets out; step in while it rattles and you're pulled under; someone already in the gap when it wakes has the reaction window to get out; walk off and wait and it goes quiet; cut at its gap and it's gone; it nests mid-train and never on someone; a bot waits rather than cross; the director puts one in only at a facility stop; a client knows which gap is rattling.
      - `dt audio render`: its tell clears the bed by 8.5 dB for a listener on the middle car in the chaos scenario (`AudioTests` needs 6).
55. **The Lamplighters and the lamp switch (T52, App. A.6 and B.6; GDD: "light-reactive. Work the lineside. RULE: lamps down. Contradicts everything that needs forward visibility").** `EnemyKind.Lamplighter`, cost 2, tuned in `enemies.json` `lamplighters`.
    - **The lamp switch.** Until now nothing could put the forward lamp out. It's a cab control now, through intent: `PlayerIntent.Lamp` (on or off: a setting, not a toggle, so a resent intent or a held key is harmless).
      - On the wire it rides in the notch byte's top bits, so no intent grows.
      - The host and a predicting client both apply it, from the cab only (the same rule as the regulator).
      - Keyboard **L** in the app. The HUD in the cab shows it.
      - With the lamp off (or smashed, or in a Vigil) the renderer draws no beam.
    - **Spawn (B.6).** Any tier, only while the lamp is lit (so x0 with every light out), not in a tunnel or at a facility, at most two (one each side). Weight x2 in the route's back half ("night depth").
      - **Ambiguity, "dark forest and open sections":** the routes don't mark forest, so anywhere that isn't a tunnel or a facility counts.
    - **The spine.**
      - **Dormant:** it paces the engine 14 m out, beside the cab, as fast as 16 m/s. A faster train leaves it behind and it's lost.
      - **Telegraph:** a lit lamp brings it in, its eyes catching the light. That's the tell ("near-silent; eyeshine is visual", spec A.3): about 5 s of it coming in from 14 m.
      - **Strike:** within 2.6 m of the lamp, across the ground, it smashes the lamp and bites the nearest player within 20 m for 40. That's usually the cab: the engine's length is in reach.
      - **Break off:** put the lamp out while it's coming and it loses track: it stands where it lost the light, and a moving train leaves it behind. (Having smashed the lamp, it goes back out to pace the train instead.)
      - **Ambiguity, "returns to the lineside":** read as standing there, not pacing the train again. A pacing one re-acquires the moment the lamp's relit, and on frontier:7 that kept the lamp down for most of the night and missed the dawn.
      - It goes when it's dormant after 240 s.
      - **Not shootable:** the counter is the lamp.
    - **Ambiguity, "destroys the lamp":** smashed, it can't be lit again for 45 s (the spare glass), replicated as `LampOutSeconds`. Destroying it for the rest of the night would leave the Sleepers unanswerable for the rest of the night.
    - **Bots.** The driver bot puts the lamp down when it sees eyeshine and keeps it down 30 s after the last it saw.
      - With the lamp out, the Sleepers only show at bracing distance (60 m), too late to brake from cruise to under their derailing speed (`derailAbove` 11.1 m/s: App. A.2's 40 km/h). So in the dark it runs at 10.5 m/s, and a Sleeper found late does heavy damage instead of derailing. That's the contradiction, played.
      - Faster was tried: at 14 m/s in the dark the frontier:7 night derailed on a Sleeper.
      - Stops still plan by the usual cruise. Planned by the dark one, the dawn looked nearer, and the crew ran past every facility.
    - **Not yet:** carried lamps as light sources (they have no lit state), and a lamp switch for VR hands (a headset player can't press L).
    - **Verified:** `LamplighterTests` (9):
      - with the lamp down it just paces the train;
      - a lit lamp draws it in, and it smashes the lamp and bites the driver;
      - put the lamp out as it comes and it loses track, stands, and is left behind, and nobody's hurt;
      - only the cab works the switch, and a smashed lamp stays out 45 s;
      - a train faster than it leaves it behind;
      - the director sends them only to a lit lamp;
      - the switch rides the notch byte;
      - a client sees the eyes and the lamp out;
      - the driver bot puts the lamp down for the eyes and up again after.
      - `dt screenshot --threats` stages one coming in for the lamp; CI keeps `threats-lamplighter.png`.
56. **The Deadman and the Stoker (T53, App. A.5 and B.5).** Both punish a crew that all piles out at a stop: the cab and the firebox are what's left open. Tuned in `enemies.json` `deadman` and `stoker`.
    - **The Deadman** ("never leave the cab empty"): condition-triggered, and charged its 4 only when it takes the cab (App. B.5's "cost budget only when they actually fire").
      - Not on Local routes, and not without a route: a route-less world (tests, the prototype) is left alone.
      - The host keeps `World.CabEmptySeconds`: how long nobody alive has been in the cab.
      - **Watch (the telegraph):** it starts its approach 10 s before the spec's "cab empty 30 s (20 s on Deep territory)", so it takes the cab at the spec's time. The tell is the controls clicking on their own over the lamp's dimming hum (1–2 kHz, `deadman-click`). Someone back in the cab in that time, and it's gone, free.
      - **Take:** the world holds the regulator open and the brake off while it's at the controls, whatever the cab controls say.
      - **Evict:** someone in the cab contests it. That takes 4 s, and it hurts them (25) as they start.
      - It's only seen at the controls: a crewman, or what was one, drawn from the crew model and darkened.
      - **Ambiguity, "cab lamp dims":** the tell is the sound for now. The cab has no light of its own to dim yet.
    - **The Stoker** ("vent, or the boiler goes"): cost 3.
      - **Spawn:** the director's option during a stop (the train under 0.5 m/s) with the firebox unattended (nobody in the cab for 10 s), any tier.
      - **Feed (the telegraph):** it feeds the boiler through `Boiler.ExternalHeat` (1.5 a second) and holds the safety valve shut (`SafetyValveJammed`, already replicated). So the gauge climbs past where the valve would lift, with no fuel going in.
      - **The tells:** the fire's light turns a sick green (the scene gives the firebox that colour whenever a Stoker's in it), and a hiss (1–3 kHz, `stoker-hiss`).
      - **Critical:** at the maximum it's committed, and the boiler's own rupture hold (20 s at 100, spec B.6) does the rest.
      - **Counters:**
        - venting, 6 a second against its 1.5, holds the pressure down but spends it: "the counter has a clock cost";
        - Use held at the firebox for 3 s drives it out, and it burns whoever does it (30): "exposing the boiler player".
      - It's never seen.
      - **Ambiguity, "the box open":** the firebox has no door state, so the spawn is "unattended" alone, and App. B.5's x3 weight for a box left open waits on one.
    - **Bots** keep the driver in the cab at every stop, so neither fires in the harness. The rules are the crew's to keep, and the tests keep them.
    - **Verified:** `DeadmanStokerTests` (8):
      - leave the cab empty and the Deadman takes it at the spec's time, and the train runs on with the brake held;
      - back in the cab in time, it's gone and cost nothing;
      - taking the cab back takes 4 s and hurts;
      - not on Local routes, and sooner on Deep territory;
      - the Stoker feeds the boiler past the valve until it goes;
      - venting holds it, and driving it out ends it but burns you;
      - the director puts a Stoker in only at a stop with the cab empty;
      - a client sees who holds the cab.
      - `dt audio render`: the click and the hiss clear the bed by 24 dB in the cab.
      - `dt screenshot --threats --view cab` shows the green fire.
57. **The fidelity target moves up to 2008-2012 (art direction, after the art pass).** Art direction's call: the art pass read as early PS2, and the benchmarks are BioShock 2, Silent Hill 4, Dead Space and Resident Evil Revelations. The pipeline plan's "late PS2 / early PS3" now means its PS3 end; the PS2 end stays as the comparison mode (`--ps2`, `post.ps2`). The work goes in phases, each looked at before the next:
    - **Image (this note).**
      - Materials are sampled trilinear and 16× anisotropic with no mip bias, instead of point-sampled with a positive one.
      - The scene goes through exposure and a filmic tonemap (ACES fit) instead of a hard shoulder. The fog's long gradients get a one-step triangular dither instead of Bayer banding into 48 levels.
      - Bloom has two scales: the tight half-res glow and a wide quarter-res halo.
      - Lens fringing toward the corners, finer grain.
      - FXAA over the tonemapped frame (luma in alpha). The overlay draws after it, so the HUD stays crisp.
      - `look.json` `post`: `exposure` 1.0, `wideBloom`, `lensFringe`, `mipBias` 0.
      - `LookTests`' room-brightness ratio is widened to 2.2×, because the filmic toe darkens flat colour more than a lamp-lit texture. It gains an absolute washed-out ceiling.
    - **Next:** normal and spec maps on every surface at higher resolution; ambient occlusion, more shadowed lights and light shafts in the fog; then a geometry and material detail pass.
    - **Resolution, occlusion and reflection (art direction: "graphic quality seems closer to Half Life 1 than Bioshock 2").** An audit found the biggest single cause: the game rendered at 480×270, and screenshots at 640×360, both scaled up by nearest neighbour. That's the "low-res by design" of the old PS2 target (GDD §32). The benchmarks ran native 720p.
      - The game renders at 1280×720 (`--internal`), blitted to the window bilinear. `dt screenshot` renders at 1280×720, `--scale 1`, and the app's `--capture` writes at 1×.
      - The HUD and menus keep their 480×270 canvas (`GreyboxRenderer.OverlaySize`), so their pixel font scales up with the frame instead of shrinking to a third.
      - **Screen-space ambient occlusion** (`ssao.frag`), at half resolution, from the scene's depth, which is now stored and sampled. Each pixel's position and normal are rebuilt from the depth, and 12 points in the hemisphere are tested. The composite blurs it (four bilinear taps) and darkens the HDR scene by it, but not a light's core. The corners of the cab, round the gauges, under the eaves and where a crate meets the floor all go dark. `look.json` `post.occlusion*`: strength 1.0, radius 0.6 m (shrinking toward the camera), intensity 2.0, faded out by 60 m (the far field is fog, and noise there reads as dirt).
      - **Reflection by Fresnel** (`scene.frag` `envAt`). There's no cubemap: the environment is the sky's own gradient (horizon haze, zenith, the fogged ground below) and a tight moon highlight. Along and under the horizon it's darkened to a third, because a reflection there mostly sees the world. Schlick's term with F0 from the spec map, weighted by gloss², and none on rough surfaces. So glass, brass, wet steel and puddles pick up the sky, and a grass field doesn't frost over. Indoors it's a dim warm room. A face seen from behind reflects off the side facing the eye.
      - **The moon's shadow.** Only the headlamp cast shadows before. The moon now has an orthographic 2048² depth map (`shadow_moon.vert`), 110 m square, pushed ahead of the camera and snapped to its own texels in world space so its edges don't crawl as the train moves. Everything the scene draws casts into it, the cut-out cards by their shapes. It shadows the moon's diffuse and its highlight, over eight taps (moonlight through cloud has a soft edge), and fades out toward the map's edge. It's off when the moon is down, and in the PS2 comparison mode. A headset's eyes each draw their own shadow maps, so theirs is 1024 (`GreyboxRenderer(moonShadowSize:)`): filling two 2048 maps a frame put the simulated headset in CI under its 30-frames-in-30-seconds check.
      - **The pines are modelled.** They were three crossed pictures of a whole tree, 24 triangles, the look of the late 1990s. Now `WorldKit.Pine` builds a spruce as the benchmarks built theirs, about 970 triangles:
        - a tapered trunk carrying 14 whorls of boughs, nine to a whorl, from low on it to the leader;
        - each bough a card of one spruce frond (`pine_bough`, `tools/art/texgen/mat_foliage.py`: dozens of needle-furred branchlets raked along a twig, gaps between them), bent in two, rising off the trunk and drooping to its tip, longest at the bottom;
        - alternate boughs rolled either way about their length, so none is seen edge-on.
      - The lineside uses them within 40 m of the line (`WorldArt.NearTrees`); further out, where a tree is a silhouette in the fog, the crossed cards stay (`WorldKit.PineCard`). The pine moves to the large-prop budget.
      - **Hero layers.** Every material layer is 512 in the GPU's arrays, so the characters' and creatures' atlases, baked at 1024, were halved. Layers authored larger than `layerSize` now also go in three arrays of their own at up to `heroLayerSize` (1024: `look.json`, `RenderAssets.HeroSize`). A per-layer slot table in the frame's uniforms (`heroOf`) sends a textured surface to them; everything else, and the PS2 mode, draws from the 512s as before. They're only the baked model atlases (crew, husk, hound, weight), about 70 MB with their mips. `LookTests.TheBakedAtlasesKeepTheirResolution`.
      - **Still to come:** terrain.
58. **Sourced models, and model bashing (art direction: "a texture and model fidelity problem").** Procedural kits can't reach the benchmarks' prop density and detail on their own. So the art now also takes free CC0 and CC-BY models and bashes them into the game's own things.
    - **Where they come from.** Only GitHub is reachable from the build machines (the asset sites are blocked). The sources are public GitHub collections (the Khronos glTF sample assets, three.js's examples, gkjohnson's demo data), pinned to a commit in `tools/models/sources.json`. Licences are read from each model's own files, and the intake rule is `intake/README.md`'s.
    - **The cook** (`tools/models/cook.py`, Blender, headless, deterministic).
      - A recipe imports the sources, deforms and combines them, and calls `finish`.
      - `finish` decimates to a triangle budget and scales to metres with the pivot at the foot.
      - It bakes each material's PBR maps into the library's format at 512: diffuse (base colour × occlusion, metals darkened, lit glass taking the light's colour), spec (strength, gloss, emissive) and a normal map (glTF's +Y green flipped to our y-down).
      - It exports a one-bone skinned `.glb`, so `Ballast.Assets` reads a prop the way it reads a creature. Socket bones mark where a prop's light is or what it hangs by.
    - **In the engine.** `PropArt` cooks each prop's bind pose once into a `MeshAsset`, and `Look` loads `index.models.json` beside the library.
      - The kit's piece stays as the fallback, so a checkout without the props still draws.
      - `dt art check` budgets the props like kit pieces. The hand lantern and the skull are medium props: the lantern doubles as the held lamp, and the skull is seen close.
    - **Creatures from scans.** A scan can be rigged on its own pose. `recipes/hollow.py` bakes Le Transi down and places a 21-bone skeleton on the statue's joints, read off its silhouette. It weights each vertex to its nearest bones and keys the clips on `tools/blender/rig`'s `Clip`. The Hollow is that cadaver now: it replaces the procedural one, with the same clips and the same place.
      - A figure that has to fold a long way (standing to crouched) is posed at full resolution first, with a linear-blend skin of its own in numpy, then baked down and rigged again on the new pose. The game rig only bends what the clips move, never the big fold, so the low mesh never has to survive it. `cook.rig_creature` holds the weighting and export both recipes share.
      - The Soot children are made this way (`recipes/soot_child.py`): the Boy Room's boy, his toy sword cut out of his hand, sat down in the ash, graded waxy-pale where the skin shows and rag-grey elsewhere, with soot run down him and his eyes painted into dark pits. It's the same child the line passes standing in the villages' ruined bedrooms. He replaces the procedural one, with the same clips (huddle, turn).
      - The Switchman is a three-scan bash (`recipes/switchman.py`), built from:
        - the Three D Scans Zenobia's gown and mantle, sooted to an oiled black coat;
        - Lee Perry-Smith's head (CC BY 3.0) set on her shoulders;
        - the Khronos Flight Helmet (CC0) over it: leather cap, goggles, and a rubber mask with its hose down the chest;
        - the cooked hand lantern, hung from her chain on its own bone, with a flame of pure light in it.
      - The Switchman is baked down as one figure and rigged on the statue's pose. It replaces the procedural one, with the same clips (wait, flee) and the "lantern" bone the engine lights.
      - The Sleepers (`recipes/sleeper.py`) are Le Transi again:
        - laid on its back and drawn out to a tie's 2.8 m;
        - pressed flat and sunk into a rotten tie of the library's sleeper timber (cut along its length so the chain bends it);
        - graded creosote-brown with the grain running along it.
      - At a glance a Sleeper is one more tie across the rails. Close to, the ribs, the face and the raised arm are the scan's. It keeps the same chain rig and clips (`rig_creature` takes the skeleton's name).
      - The Clinger (`recipes/clinger.py`) is a spiny crab (Three D Scans):
        - turned with its back out of the hull and pressed flat to the plate, its legs splayed on the steel;
        - three of Lee Perry-Smith's faces pushed up through its shell where the procedural one's sacs were, so they swell and ebb with the cling pulse;
        - a tar lip and a mineral-crust drill at its lower seam, with the hot plate.
      - It's rigged as before. The legs ride radial limb bones off the root, so when the body drives into the car they stay gripping it.
      - The crew, the hound and the Dragger stay procedural for now.
    - **Ruined interiors.** A whole sourced room can be ruined in its recipe and set where the line can see into it. `recipes/boy_room.py` does this with "Boy Room" (CC BY 4.0), a child's bedroom with a hulking imaginary friend.
      - It's split by material: the wardrobe knocked askew, a picture hung crooked.
      - It's graded per part: dust over everything, the boy ash-pale, the thing he drew soot-black but for its eyes.
      - It's walled by a house shell in the library's plaster, broken off raggedly above it, with fallen rafters and rubble.
      - It stands in each village with its front wall gone, facing the line, and its bedside lamp is still lit (a socket).
      - The cook reads spec-gloss materials (KHR_materials_pbrSpecularGlossiness) too. `dt art check` gives a whole room its own class (30k).
      - `recipes/wake_room.py` is a second, bashed from two sources: "Interior Scene" (CC BY 4.0), a modern living room, and the Three D Scans "Zenobia in Chains". Most villages have one further along from the boy's room (hashed on the village's place, so nothing after it moves).
        - The room is cut open on its long side and its far half taken away. Its glass is knocked out, its plants gone, and its coffee table cleared for a bier of library timber.
        - The pictures are hung with black crepe. The soft furnishings are baked down (`bake_down`) and yellowed with dust.
        - Its walls and floor were lit by a baked atlas that only works from inside the closed box, so they're replaced by the library's plaster, sooted brick and floorboards, broken off raggedly, with the window's hole left.
        - Zenobia is taken off her plinth and laid out on her back on the bier: the pale body the room is for.
        - The pendant lamp's globes are gone. Its bare bulbs are lit emissive spheres (`cook.eyes_at`), with a light at its "lamp" socket. `PropArtTests` lets pure light be drawn flat; everything else wears its own layers.
      - `recipes/portrait_room.py` is a third: a photographer's studio across the line, set up for a Victorian memorial portrait.
        - The Boy Room's boy is posed seated at full resolution, propped in the Khronos damask chair (CC BY 4.0) with an iron posing stand's clamp behind his head.
        - The Khronos Antique Camera (CC0) stands on its tripod pointed at him.
        - A candle burns on a crate: the Khronos hurricane holder (CC BY 4.0) with its glass (and the logos on it) gone.
        - Behind him hangs a torn painted backdrop.
        - Its light is at the flame's "lamp" socket. Most villages have one, hashed on the village's place.
      - The rooms share `cook.ruined_shell` (the broken plaster-and-brick walls and floorboards) and `cook.box_uv`. The posed figures share `tools/models/figures.py`: the boy's joint table and skeleton, and the full-resolution linear-blend pose the Soot children use too.
    - **Modelled here, for the game's own things** (`tools/models/make.py`). No free model exists for the crew's stores, the freight or the cab's controls, so each is modelled at high resolution in its recipe, then baked down (`bake_down(low=make.LOW)`) onto a plain game mesh built alongside it: the crate's shell, its battens, the pipes at a few sides.
      - The high-poly model is bevelled boards with gaps, nail heads, strap iron, rope grips and stencils (Blender text).
      - It wears the library's own maps, box-projected at the library's scale. Only layers without printed-in structure (`wood_sleeper`'s plain grain, `rust_heavy`, `paint_olive`, `brass`) suit it: a layer with boards or rivets printed in doubles them.
      - `bake_down` takes a smaller cage for these (a modelled low mesh lies almost on the high one). The low mesh is invisible to the bake's rays, or it would shadow the high one's flat faces black.
      - Provenance: a modelled layer's `sources` name its recipe (Dark Territory's own, CC0) and the pinned sources of every library layer it wore. `PropArtTests` accepts a recipe that exists in the repo as a source.
      - The first set is the physics bodies' models, drawn by `SceneArt.Body` with the kit's pieces as the fallback:
        - `stores_crate` (BodyKind.Crate);
        - four kinds of facility freight (BodyKind.Cargo, chosen by the body's id): `freight_parts`, `freight_ammo`, `freight_sacks`, `freight_medical`;
        - the two-man `heavy_crate`;
        - the `field_radio`, with its lamp pure light.
      - They're budgeted as medium props.
      - The cab is the second set. `cab_backhead` is appended into the engine's kit mesh at the firebox door: the doors standing ajar, the steam turret and its valves, siphons to each gauge, the injectors' valves and copper pipework, the lubricator, the whistle, the damper and the regulator's rack. It's baked in four groups so it holds up close.
      - The moving controls are drawn by `SceneArt.CabControls` where the sim puts them (T29), with the greybox's boxes as the fallback:
        - the regulator's handle slides along its rack, exactly the sim's travel;
        - the brake valve's handle swings about its pedestal pivot, and the reverser about its floor pivot, so each handle stays within a few cm of the sim's straight-line travel;
        - the blow-off valve sits at the vent.
      - Blender's diffuse bake scales colour by (1 − metallic), so the library materials bake as non-metals: brass would bake black. The engine's shine comes from the layer's spec.
      - The facilities' modules are the third set (`recipes/depot_modules.py`), parts that `SceneArt.Depots` places and moves where the sim has them. The greybox is the fallback.
        - The capstan winch: its frame, the drum turned by the crank, a crank arm per handle at the sim's grip (T43), the rope, and the freight sled as far as it's hauled.
        - The gantry crane (T48): legs, rail girders in 5 m lengths, the bridge where it is (scaled to the span), the trolley, the hook on its cable, the cab and the control stand.
        - The castings: stacked, hooked, or on a car's roof.
        - The coaling tower's lever stand, its handle down when pouring.
        - `dt screenshot --site --crank` now closes on the cranks even at a facility with a crane.
      - The facility buildings are the fourth set (`recipes/facility_pieces.py`). The six kinds that shared the generic sheds now read by shape, as the coaling tower, the elevator and the foundry did (GDD §30). `StructureKit.Facility` sets the pieces among kit buildings, sunk 0.3 m as the kit's sills are; without the props the kit parts still stand.
        - Mine head: the headframe over the shaft (at 1.5×, the tallest thing there), its winding house and chimney, and the spoil heap.
        - Chemical works: three storage tanks, a pipe rack on trestles, and the works with two thin stacks.
        - Military depot: a watchtower at the gate end, sandbag walls, Nissen huts, and a wire fence along the line.
        - Slaughterhouse: the long windowless hall, cattle pens in front, and the ramp down from the cars.
        - Switchyard: the signal box with one window lit, the water tower's spout swung over the track, and a goods shed.
        - Wreck yard: heaps of stripped carbodies and wheelsets, with the sheds set back behind them.
        - `dt screenshot --route tier:seed --site --facility i` stops at the route's i-th facility, to look at a kind's buildings.
      - The crew is the fifth (`recipes/crew.py`), baked over its own game mesh rather than replacing it. `tools/blender/crew.py` stays the source of the mesh, the rig, the weights, the variants and the clips; the recipe runs it with the export held back, then:
        - swaps the egg of a head for Lee Perry-Smith's scan (CC BY 3.0). The scan's mouth cavity is cut out and capped first: collapsed to a game mesh, the lips caved into it. The whole face rides the head bone, since a chin on the neck bone shears off when the clips tip the two apart;
        - models a high copy of each part, subdivided and displaced: the coat's folds below the belt, seams, buttons, bunched sleeves and a turned cuff, knee creases, laced boots with a welt, parted fingers, cap panels, the helmet's rolled rim. It's dressed in the library's oilskin, wool and leather at several times their repeat, so the weave reads as texture, not pattern;
        - joins the parts for one 1024 atlas, unwraps them (the head in one cylindrical piece, and it and the hands given more texels), bakes each group from its own high copy, and splits them back into the parts the variants draw;
        - soots the result: creases, mud climbing the boots and hem, smoke settled on the shoulders, and the face sallow and smudged, its occlusion softened.
      - A Cycles bake clears the whole image, and only the colour pass leaves alpha where it didn't write. So each group bakes into images of its own, and its colour coverage masks all four maps into the atlas.
      - The chest lamp's glass keeps crew_atlas's lit cell, and the shovel keeps its library layers. `tools/blender/build.sh` no longer builds the crew.
      - `tools/models/overbake.py` holds the machinery the crew's recipe grew, for any tools/blender character. It runs the script with its export held, makes the dressed and sculpted high copy, and joins the parts into one atlas. It bakes each group from its own copy, grades with soot, splits the parts back out, and exports with the script's own rig and clips.
      - The Cinder Hound (`recipes/cinder_hound.py`) is the second through it:
        - the hide gone to matted, oily soot, clumped back along the body;
        - the skin shrunk onto the frame: ribs as bars down the barrel, the spine's knuckles, the hips and shoulder blades up, tendons down the legs;
        - old scars across the flanks, and the muzzle's skin wrinkled back off the teeth;
        - the slag blistered and pitted like clinker.
      - The ember cracks, their cores and the eyes keep their own layers, so the tell's glow is untouched.
      - The cars are the sixth. Pieces TrainKit set as boxes are modelled once, baked, and set by the kit where the boxes were, with the boxes as the fallback:
        - `recipes/car_gear.py` has three pieces:
          - the arch-bar truck, with plate wheels, journal boxes with their lids, top, arch and tie bars through the columns, coil springs, the bolster, and brake beams with their shoes on the treads;
          - the knuckle coupler, with its striker, knuckle and guard arm, the cut lever out to the car side, and the air hose with its angle cock and glad hand. It's turned for a car's rear and raised to the kit's height;
          - the brake gear under the middle: the reservoir on straps, the cylinder, the triple valve, the levers and the push rods.
        - `recipes/car_body.py` has modules laid to the car's tuned geometry:
          - roof-walk bays of gapped, nailed boards, stretched to fit the walk end to end;
          - the roof sheets' riveted seam caps between the bays, on a steel roof, scaled to the roof's width;
          - side posts: riveted pressed ribs on the steel cars, and bolted timber posts with iron plates on the planked ones.
        - A car goes from about 5k triangles to about 10k, inside its class's 15k.
      - The engine is the seventh (`recipes/engine_parts.py`), with the kit's pieces as the fallback:
        - the spoked drivers with their counterweights and crank bosses, each turned to its side's crank phase (the pins a quarter turn apart, as the rods are laid);
        - the pilot wheels;
        - the fluted coupling rod with its bushes and oil cups;
        - each side's cylinder, with its steam chest, cover studs, drain cocks, guides and crosshead;
        - the smokebox door, with its hinges, dart and clamps;
        - the armoured headlamp box and its cage, stretched back to the boiler front. The kit's lens stays the light;
        - the bolted domes;
        - the riveted straps round the casing.
      - The engine unit comes to about 15.7k triangles of its 45k.
      - The gun car's gun is the eighth (`recipes/gun_mount.py`), to TrainKit.Gun's frame, so the muzzle flash still sits at its muzzle. It's a water-cooled heavy machine gun on a bolted pedestal and cradle, and replaces the kit's boxes (282 triangles). It has:
        - a riveted receiver with its top cover and crank, the spade grips and the trigger;
        - the corrugated jacket with its filler, drain and steam union, and the muzzle booster;
        - the feed block, with the belt curling down into the ammunition box;
        - the raked, rimmed, riveted and dented shield.
      - It comes to 944 triangles of the mount's 4k.
      - The crew wear a gas mask (art direction: masked, "so we don't need to worry about lip sync or eyes", then "more steampunk post apocalyptic... original looking... friendslop horror so we need to be masked but still have some fun to us", "different players have colour variations").
        - Two passes were turned down: a leather respirator and goggles over the scan's face, then a smokebox helm ("Yikes I really don't like the helmet. Give me other options with more detailing").
        - Four concepts followed, modelled at full detail on the bare figure (`tools/models/concepts/crew_headgear.py`, `render.sh`): a plague-beak hood, a boiler diver, a welder-gasman and a plate sallet. The director picked the welder-gasman.
        - The hood: a black rubber gas hood over the whole head, down into the collar. Its two brass-ringed eyepieces have lenses lit dimly amber (`helm.glass`, a pure light: the one thing you see of a face).
        - Hanging off it: a knurled filter drum at the chin with a brass grille, two small filters at the cheeks, and a corrugated hose down to a coupling on the chest.
        - The cap: a quilted leather flying cap in the player's colour, with a fleece rim and earflaps buckled under the chin.
        - The variants: a welder's visor on brass pivots at the temples, flipped up over the cap (variants 0 and 2) or down over the face (1 and 3; then there are no eyes, only a slot of dark glass). 2 and 3 add the scarf.
        - The high copy adds what the game mesh is too coarse to carry, baked: the eyepieces' threads, the drum's knurling and grille bars, the hose's corrugations, the visor's rivets, the hood's moulding seam and chin wrinkles, the cap's quilting (`crewfigure.gas_mask_detail`). The coat is patched, and stitched round the patches.
      - Each player has their own colour. The cap's leather and the scarf are baked pale and neutral, scuffed. They're drawn as their own material on the same atlas (`crew_0.paint`: `overbake.Atlas.finish(split=...)`, `cook.bake_layers`' `dt_alias`), which `CreatureArt` tints by player id from `look.json` `crewColours`: eight colours, strong enough to hold under the amber lamps.
      - From behind, a crewmate is a coloured cap; from in front, two dim eyes, or none.
      - The body under it was overhauled ("Player model is a bit low quality... a complete overhaul"). The kit's lofts had made a 22-sided coat, 12-sided sleeves and gloves of three boxes. `tools/blender/crewbody.py` now builds it as a character artist would:
        1. Blocking: the clothed figure in overlapping solids in SK_Human's T-pose.
           - The coat's body with its lapel and yoke.
           - Sleeves with turned-back cuffs.
           - Gloves: a gauntlet, a palm, four fingers and a thumb.
           - Trousers, and boots with a shaft, a foot, a sole on the foot's outline and a heel.
           - The belt with its buckle and three pouches, the bandolier and the satchel.
        2. Union: voxel-remeshed at 3.5 mm into one watertight surface, relaxed. This is the high copy (about 290k faces), and the bake sculpts its folds.
        3. Retopology: QuadriFlow to about 2300 quads, smooth-shaded (the `frame` part).
        4. Weights by position: the rig's own region functions (the torso up the spine, the arms along them, the legs down them, the fingers and thumb), so the joins between solids bend as one.
        - Each face takes the material of the solid it's nearest.
        - The skirt and the stood-up collar are shells too thin to retopologise. They're clean two-sided lofts (the `coat` part), subdivided for their high copy.
        - `crewfigure` bakes the body, frame and coat as one group, so the skirt shades the legs.
        - The coat's shells sculpt both sides the outer side's way. Along each side's own normal, where a fold sank the outside, the lining came out through it and baked dark.
        - The staged crew's raised arm (T47's headset reach) now points down the line: raised high it read as a salute.
        - The mask's clipping is fixed.
          - The scarf is thick wool wound round the new collar, lowered clear of the cheek filters, its end hanging on the left clear of the lamp.
          - The hose runs out in front of the scarf.
          - The collar is open at the throat past the cheek filters.
          - The visor is raised 40° rather than flipped 62°: past that, its edges met over the crown in a crest.
      - The crew are 8452 triangles at most (variant 3: visor and scarf), inside the brief's 9k, and 1.86 m to the top of the cap.
      - The husk shares the body. Its scanned head is decimated to 2000 to fit: 8936 at most.
      - `tools/models/crewfigure.py` builds both figures. `Style(figure="helm")` is the crew; `figure="bare"` is the bare-headed figure in a cap or a steel helmet, with Lee Perry-Smith's scan for its face (`DT_CREW=bare` to `tools/blender/crew.py`), which the husk is built from.
      - The husk is the ninth (`recipes/husk.py`): the crew figure gone wrong, for the Climbers (App. A.4: "drawn out thin, soot-black") and the Deadman (A.5: "a crewman, or was"), with the crew's rig and clips. Its mask is torn off and hangs from the collar by a strap, the face bare: that's how you tell it was crew. The face is shrunk onto the skull (the cheeks and temples sunk, the eyes back in black sockets weeping tar, the nose rotted back, the jaw long). The clothes are burned through in ragged holes, meat in them and char at the edges, everything soot-black, and the chest lamp is dead. The masks it bakes (`overbake.bake(masks=...)`: vertex colours on the high, baked to maps) steer the grade. `CreatureArt` draws the Climber and the Deadman with it, and falls back to the darkened crew when it's missing.
      - The Weight is the tenth. It had been three of the Dragger's arms scaled up; it's now its own figure (`tools/blender/weight.py`, baked by `recipes/weight.py`). What comes up out of the marsh is a bog body, several gone into one: four torsos fused in a sodden heap on the ballast, their faces sunk in it looking up with black holes for eyes and mouths. Four arms hook over the coupler and the end beam's corners, and two more and a pair of legs trail behind, clawing at the stones. The high copy is tanned leather folded over itself, with the spines and ribs down each back and the shoulder blades standing. Peat is caked on everything low down. It's kept a shade lighter than the hound: nothing on it glows, and under the car's end the moon is all it gets. It has its own clips on a chain rig of 30 bones: grab (it has hold by 0.5 s), drag (uneven heaves) and release, which `CreatureArt` plays through the telegraph and the break-off. 3970 triangles; the Dragger's arms stay as the fallback.
    - **First set.**
      - The Khronos Lantern, split into a lamp post and a hand lantern. The hand lantern replaces the kit's cage in the cars, on the platforms and as the dropped lamp.
      - The photoscanned skull.
      - The first bash: the skull lantern at the fortress gate, a cage lantern with a human skull where the flame should be, lit from beneath. `PropArtTests` holds the loading, the sockets and the provenance.
59. **Crew bots run the gantry crane (T54, spec D.2 and D.3).** The winch pair take the crane first, while the cars under the gantry still have room (the crates and the sleds would fill them), then the winch.
    - **The operator (Winch0):** down on the stand's side of the train (across the track from the castings), to the stand, and holding Use there.
      - Hook up while traversing, down over the next stacked casting to where it can be rigged, held there.
      - Hooked: up clear of the roofs, over a cargo car with room, down onto its roof, and let go only once it's sitting on it (never above `dropAbove`: a load let go of high kills).
      - It steers by bridge and trolley settings found by `Crane.Over`, a search over the gantry's reach that's remembered per point.
      - A car counts if the hook reaches anywhere along its roof clear of the ends: the 20 m gantry spans only part of the first cars.
    - **The rigger (Winch1):** down on the castings' side, beside the next one, and holding Use once the hook's down over it (`Crane.Riggable`); then clear, beyond the stack, while it's lifted away.
    - **Spec D.3's "blind instruction":** the operator and the rigger don't see each other. They agree by working the same plan: the next casting still stacked, and the nearest car in reach with room.
    - **The driver** waits in the Loading leg for the crane too, until no casting's left or no car in reach has room. `CrewCalls.CanWork` counts a crane site the pair can work.
    - **Found on the way:**
      - A hand going to the cab to warm up walked to its door on the site's side of the train. The operator, across the track, walked into the train and froze. Hands now go to the cab door on the side they're on.
      - The first night on frontier:2 (with a foundry, though the crew passed it by) lost four bots to the Rattle. Three fixes:
        - its lifetime is now a hard limit (note 54);
        - the warm-up routine (T31) keeps to car ends with no Rattle in the gap (`WarmUp.Rattled`, set by the bot from what its client sees);
        - `Heed.Rattles` looks a third of a second ahead rather than one tick: with the lag, the client's prediction isn't quite where the host has a bot.
      - A margin round the gap was tried too, and made it worse: bots near a car's end stuck in it.
      - After the fixes both nights deliver: frontier:7 net 3943 as before, frontier:2 net 3163, nobody lost on either.
    - **Verified:**
      - `StopCrewTests.AtTheFoundryThePairRunTheCraneAndTheCastingsGoOnTheRoofs`: both castings loaded, everyone alive, and the train back together and away.
      - The harness's stop records count `castings`.
60. **The balance sweep (T55, roadmap M7 "balance sweeps", GDD §34).** `dt balance` runs harness nights across a grid (tiers, seeds, crew sizes, train lengths) side by side, and judges them (`Net/Balance.cs`) against `tuning/balance.json`.
    - **Each night** is a whole host with its bots over its own loopback, as `dt harness --route --enemies` runs it. Nothing is shared between nights, so they run in parallel (`--parallel`, the machine's cores by default).
    - **The checks:**
      - the crew-size sweep: "survivable at 2" (at least half a two-crew's nights get home) and "non-trivial at 8" (an eight-crew's night sees at least 5 punishes);
      - App. A.1's fairness contract on every night.
    - **The train-length sweep** ("where is the real progression cap?") is reported by length, not judged: that's a design call. So is anything about fun (§34: "agents cannot tell us whether it is funny").
    - **Bots aren't people.** They keep to a plan and don't talk. So the targets are floors that find nights the bots can't play, or nights where nothing happens; they aren't the design's numbers for players.
    - The targets are loaded before any night runs: a sweep is an hour of nights, and a bad file shouldn't lose them at the end.
    - **First sweep** (frontier seeds 1 and 2, 10 cars, crews of 2 and 8), 10 min on 4 cores:
      - every night delivered, nobody lost, 0 fairness violations;
      - a crew of two: net 2100, 28.5 punishes a night; a crew of eight: net 3791, 24 punishes.
      - Two bots don't work facilities: the stops need a shunter, and the second bot is the gunner. That's why their net is lower.
    - The nightly soak runs that sweep and fails on a failed check.
    - **Verified:** `BalanceTests` (4): the grid is every combination; a survivable, busy sweep passes; each target fails on its own; train length is reported, not judged.
61. **The Ferryman (T56, App. A.2 and B.2; GDD: "stands on the track ahead holding a lantern, waving you down. RULE: do not slow down").** `EnemyKind.Ferryman`, cost 4 (App. B.1's table), tuned in `enemies.json` `ferryman`.
    - **The lantern is the telegraph.** It's placed 600 m up the line at the lineside, and telegraphing from the first tick. Past the draw distance the scene still shows the lantern's light, out to 1.2 km ("visible from very far out").
    - **Ambiguity: "train DECELERATES".** Read as the train going slower than the fastest it has come at the Ferryman, by at least `slowTolerance` (2.5 m/s). Cruise notching wobbles by about 1 m/s, so it doesn't count; a brake application, or easing off to the dark cruise, does. Any such slowing commits (ADVANCE). That still only happens after App. A.1's reaction window, which the spine enforces.
    - **Holding speed:** within 30 m of the engine, it steps aside and breaks off, and can't re-engage ("cannot re-engage after breaking off").
    - **Slowing:** it comes down the line at 5 m/s and boards the engine. It strikes whoever is in the cab (100: a life; if the cab is empty, the nearest crew within 20 m) and is gone. It can't be shot ("entirely defeated by doing nothing").
    - **Ambiguity: "long straight with clear sightline".** Read as its gate, `Ferryman.ClearAhead`: from the engine to 100 m past where it stands, no curve tighter than 1500 m and no grade over 1 %. Nothing else on that stretch may give the crew a reason to slow: Sleepers, Grease, a tunnel, a facility, or the end of the line within 600 m past it. Otherwise the rule contradicts "watch the road" with no way to satisfy both, and that isn't the kind of contradiction the conflict table seeds.
    - The other B.2 gates: Frontier and beyond, the back 60 % of the route ("mid-to-late"), once per run (the director's own log), a lamp that isn't smashed ("functioning forward lamp"), and a train coming on at 8 m/s or more.
    - B.2's "weight up if crew has braked for a false positive earlier": ×2 once a crew has braked hard for the Long Whistle's horn (T57, note 62).
    - **The driver bot** holds the fastest speed it has come at a waving lantern, with the lamp down or not. That is the Lamplighters + Ferryman bind: with the lamps down, the lantern is its own light.
    - **Art:** the Switchman's railwayman, drawn taller, swinging the lantern hard while it waves; the lantern is a point light. The greybox has its own figure. `dt screenshot --threats` shows one on the line ahead.
    - **Verified:** `FerrymanTests` (10) cover:
      - the lantern telegraphs from the start;
      - held speed: it steps aside and is gone;
      - slowing: it boards and kills the conductor;
      - slowing at once still gets the reaction window;
      - a notching wobble isn't slowing;
      - once it has stepped aside, slowing does nothing;
      - the director sends one mid-run, only once;
      - none with Sleepers ahead, on a Local line, with the lamp smashed, or with the train crawling;
      - a client sees it;
      - the driver bot holds its speed past it with the lamp down.
62. **The Long Whistle (T57, App. A.2 and B.2; GDD: "sounds a horn on the line ahead. There is no train ahead. RULE: don't trust the horn").** `EnemyKind.LongWhistle`, cost 3, tuned in `enemies.json` `longWhistle`.
    - **Never seen.** It sits at a point up the line: 60 m short of the first curve tighter than 1200 m, or grade of 1 % or more, 400-900 m ahead (B.2's "immediately before a grade or curve so braking is worst"). With no such point in that window, there's no Long Whistle.
    - **The horn is the telegraph** (spec A.4: 200-800 Hz, wrong pitch, no doppler), `content/audio/sounds/long-whistle.json`:
      - a three-chime locomotive horn whose middle chime is a quarter-tone flat, so it beats, with a sag at the end of each blast;
      - the mixer has no doppler, and the horn doesn't fake one: a real horn ahead would bend as you closed on it;
      - 25 dB over the bed at the cab in the chaos bench. An early 35 dB would have masked the other tells while it sounded, so it came down.
    - **"If ignored → escalates twice, then abandons":** a blast every 9 s, each louder, three in all, then gone. It's also gone once the train reaches its point.
    - **Ambiguity: "if crew brakes hard".** Read as the train 4 m/s slower than the fastest it came at the horn. That commits (after App. A.1's window, as always). A stop below 0.5 m/s is its punish: "the stop itself is the punishment". It deals no damage; whatever else is about does the killing. Picked up again and not stopped within 45 s, it's gone.
    - **Never alone.** App. A.8: "the harness must enforce a co-spawn requirement". The director only offers it with another active threat (not Sleepers, which are level content) within 1500 m of the engine, B.2's "≥1 other active lineside threat in region". `LongWhistleTests` pins it.
    - Frontier and beyond, one at a time, ×2 weight in fog (a route's fog density of 0.02 or more).
    - **Braking for it** sets `World.BrakedForFalseAlarm`, which weights the Ferryman ×2 (B.2).
    - **Seen and heard past the interest radius (§6.2):** `Enemy.Far`. The Long Whistle's horn carries 1.5 km, and the Ferryman's lantern is seen from 1.2 km, but interest management drops enemies past 520 m. So these two go to every client wherever they are, as the Choir's voice does. `AudioTests` exempts the horn from the "sent as far as it's heard" check for that reason.
    - **Bots** ignore it, which is the rule. The driver bot doesn't brake for horns.
    - **Verified:** `LongWhistleTests` (8):
      - it sounds from just short of the bend, and nowhere on a straight;
      - ignored, it escalates twice and abandons;
      - braking hard for it makes the stop the punishment;
      - braking at the first blast still gets the window;
      - it's gone once the train reaches its point;
      - the director never sends it alone;
      - it's sent to every client;
      - braking for it weights the Ferryman up.
    - `AudioTests` holds the horn over the bed with every other tell still clear, and `CreatureArtTests` has it drawn as nothing.
63. **Climbers (T58, App. A.4 and B.4; GDD: "scales directly with train length. More cars means more gaps means more mount points, defended by the same crew").** `EnemyKind.Climber`, cost 3, tuned in `enemies.json` `climbers`.
    - **PACE:** out beside the train at track level, level with its gap, for at least 6 s.
    - **MOUNT:** only at a coupling gap. The gaps are the couplings behind each car in the engine's rake; the engine's own is left out, since that's the cab's and the fireman's. It spends 2.5 s scrabbling at the gap, in at the couplers from the side (the telegraph), then climbs onto the roof of the car ahead.
    - **COUNTER, "a player physically occupying a gap blocks that mount point":** read as someone in the gap below the roofs (the Rattle's own gap volume), or stood on a roof end within 2 m of it. Blocked, as it comes or while it scrabbles, it drops back and makes for the nearest gap it hasn't tried. Three tries, then it gives up.
    - **TRAVERSE:** along the roofs toward the engine at 2.2 m/s.
    - **Ambiguity: "ENTER first unlit or unoccupied car".** Read as: over the middle of a car with a room in it and nobody standing inside, it goes in. During the Vigil every lamp is out, so every car counts. At the engine it goes down into the cab.
    - **Inside** (an interior threat): whoever comes within 1.8 m takes 25 every 1.5 s (`DeathCause.Climbed`). Left alone for 120 s, it leaves.
    - It can be shot while it scrabbles and while it's on the roofs (40 health). It can't be shot pacing (it's down beside the car, in the dark) or once it's inside.
    - **Director gates** (B.4): at least 2 gaps, and the train at 5 m/s or more. Weight is 0.5 a gap: "weight scales directly with gap count".
    - **Bots:** the warm-up's `Rattled` became `Barred`, so a bot won't go in by a Rattle's gap or into a car a Climber is in. The gunner shoots Climbers on the roofs like anything else with a hit volume. Holding gaps is left to people for now.
    - **Art:** a crewman gone wrong: the crew model drawn thin and soot-black, hunched running and walking, climbing fast at the gap, crouched inside. There's a greybox figure too, and a CI shot (`threats-climbers`).
    - **Verified:** `ClimberTests` (10):
      - it paces to its gap and scrabbles before it's up (with the reaction window);
      - it goes along the roofs, past an occupied car, into the first empty one;
      - an empty car it's on, it gets into;
      - inside, it goes for whoever comes in, and left alone it leaves;
      - someone in the gap holds it, and it tries another;
      - getting into the gap while it scrabbles stops it;
      - with every gap held it gives up;
      - the guns can take it on the roofs;
      - the gaps and the director's gates and weight;
      - a client sees it at the gap.
64. **The Weight (T59, App. A.3 and B.3; GDD: "deliberately below the rear gun's arc. Cannot be shot. Forces either a sacrifice or a trip outside").** `EnemyKind.Weight`, cost 3, tuned in `enemies.json` `weight`.
    - **BURIED:** it lies beside the track and is never drawn there.
    - **Ambiguity: "marsh, water crossings, low ground only".** Our routes have water crossings as bridges and no marsh terrain yet. So it's placed 8 m into the next bridge 200-1200 m ahead, and only where the line is level there (1 % or less: "cannot spawn on grades"). No bridge ahead, no Weight.
    - **GRAB:** as the rear car passes over it, it takes the rear coupling. The telegraph is the loss of speed and `weight-scrape.json` (60-300 Hz per spec A.4): a groaning rasp in slow, uneven heaves, with a sub-bass tearing under it. At the rear car it's 17 dB over the bed in the chaos bench.
    - **DRAG:** `TrackConditions.Drag`, new in the train sim, opposes motion like the brakes do and can't push the train backwards. `TrainOnLine.DraggedVehicle` puts it on that vehicle's rake at 1.25 times the engine's full tractive force, so full throttle can't hold speed ("speed decays continuously"). The world sets it from the enemies every tick, on the clients too, from their mirror, so prediction drags as the host does.
    - **Dragged to a stand** (below 0.3 m/s; the reaction window as always), it pulls the car off the rails: the car is cut away and wrecked (integrity and cargo 0), and anyone on it takes 60 (`DeathCause.TornOff`).
    - **Counters:**
      - cut the rear car: it takes the car (cargo 0) and goes;
      - Use within 2.2 m of it, one blow a press, five blows ("~5 hits"), and it lets go.
    - It can't be shot.
    - **Director gates** (B.3): at least 2 cars, one at a time, ×2 weight below 10 m/s ("weight up at low speed").
    - **Director:** only out on the main line with the whole train together, never at a facility stop (where the engine's rake is cut down to the spur's four cars).
    - **Bots take the sacrifice.** Roof walkers get off the held car and the one ahead of it, toward the engine. The gunner leaves the rear gun for the front. The warm-up won't go into the held car (`WarmUp.Barred`). Then it takes the car. Nobody goes down to beat it off yet.
      - Without this, a first frontier:2 night lost seven bots to the cold, stranded on the torn-off guard van.
      - Tallies count lost *cargo* cars (`RunReport.CarsLost`), so a torn-off guard van shows as none lost. That's the existing accounting, left as it is.
    - **Fixed on the way:** backing off a dead line (App. A.7), the driver stops anywhere short of the points, but the hand setting the switch back only counted "standing" within 3 m of the hold. Backed further than that, nobody ever set it, and the train stood until dawn: a frontier:2 night where the Switchman got the train down a dead line. `SwitchPlan.StandingAt` now takes anywhere short of the points (`SwitchmanTests`).
    - **Art:** its own model, a heap of bog bodies hooked over the rear coupling (note 58). Nothing is drawn while it's buried. The greybox has a dark mass. CI shot `threats-weight`.
    - **Verified:** `WeightTests` (8):
      - buried, it waits for the rear car, then takes hold;
      - it drags harder than the engine pulls;
      - dragged to a stand, it pulls the car off the rails;
      - cut the car and it goes;
      - five blows and it lets go;
      - the guns can't take it;
      - the director lays it at a water crossing ahead and nowhere else;
      - a client drags as the host does.
65. **The Gaunt (T60, App. A.4 and B.4; GDD: "the cost is a person. Whoever watches it can do nothing else, and the train still needs running").** `EnemyKind.Gaunt`, cost 5 (App. B.1), tuned in `enemies.json` `gaunt`.
    - **Ambiguity: "inside ANY player's view cone".** Read as: a living player, not shut in a car (walls), within 60 m, with the Gaunt's chest inside 35° of where they're looking, from 1.6 m eye height. No occlusion beyond the walls; the roofs are open. It's worked out on the host each tick (`Gaunt.Seen`). Clients just see it not moving.
    - **FROZEN** while seen, not so much as a pose change: the art holds one frame of the Hollow's idle, never played.
    - **ADVANCE** when unseen: along the roofs at 5 m/s toward the nearest living crew member on a roof, crossing the gaps car by car. There's no audio at all (spec A.4: silent by design), and no HUD text cue for it either.
    - **REACH:** within 1.3 m of them, unseen, and past App. A.1's window from its arrival, it takes them (100, `DeathCause.Gaunt`) and is gone: "the cost is a person".
    - **RETREAT:** seen without a break for 60 s. Any gap in the watching starts the minute again.
    - **Director gates** (B.4):
      - Frontier and beyond, once per run, a crew of three or more;
      - "during a stop" (the train below 0.5 m/s) or "on tunnel exit" (the engine within 150 m past a tunnel's mouth);
      - ×2 weight once the whole living crew has been shut in somewhere for three minutes;
      - it's put on the roof of the car furthest from the crew.
    - **Bots:** a roof walker within range stops and keeps its eyes on it (look deltas, as a player turns). A warm-up in progress goes on: the cold's a life too.
    - **Art:** the Hollow's figure drawn out taller still (0.78 × 1.32), dark, facing whoever it's after (its facing replicates). The greybox has a figure. CI shot `threats-gaunt`.
    - **Verified:** `GauntTests` (9):
      - watched, it doesn't move;
      - unwatched, it comes and takes them (after the window);
      - watched for a minute, it withdraws;
      - a glance away restarts the minute;
      - someone shut in a car can't watch it;
      - the director sends it once, at a stop or a tunnel's mouth, to a crew of three, and not on a Local line;
      - a roof walker bot keeps its eyes on it until it goes;
      - a client sees where it stands and which way it faces.
66. **The procedural line (docs/design/linegen-plan.md, roadmap M5 "procedural line v1").** The readings where the plan or the spec was ambiguous, and what's not done:
    - **Junction count** (§3.2's "junctions" beside its own alternate and dead-line counts): both ends of an alternate count, so junctions = 2 × alternates + dead lines. It's the reading that keeps the table's three columns consistent. The quotas' "facing junctions" (§15.3) and the Switchman's network size (App. B.7) count the same way.
    - **Dawn (§22.1):** the timer is the spec's formula over the gate-to-terminus distance. The validator holds the ideal transit to it and reports transit plus four minutes a stop as a warning (`validation.dawnWithStopsHard: false`): by the spec's own numbers the deeper tiers can't take every stop in time. Note 13 is the same conflict.
    - **Descent grades** come from brake fade's equilibrium: a train braking on a descent a third of the time recovers as fast as it fades (`profile`), so the ruling descent is the steepest where that duty holds at the consist's brake. Approaches to a stop never descend.
    - **Turnouts:** alternates and dead lines leave on 650 m, 70 m turnouts (`junctions.turnoutRadius`), so the points take line speed and the curve limit doesn't post a board at every junction. Spurs keep route.json's 150 m kit.
    - **Speeds on the boards and the paper are km/h**, as the cab's speedometer reads, rounded down to the five below. The sim's limits stay m/s.
    - **The coaling tower** is guaranteed when the tender won't last (spec B.6, §11.1), and can also be drawn like any facility, at most one of those.
    - **A washed-out main line** starts the night with its junction set for the alternate (`BranchDefinition.StartsDiverging`); a crew that throws it back runs onto the washout.
    - **Weak bridges (§22.5):** over its car limit, the span goes under the first car past the limit (`TrackRules`), telegraphed by its limit board and Form 19.
    - **Ground:** beside the track, players still stand at rail height (the formation); beyond it the land is the terrain field. The art's cells take their heights from the same field (`WorldArt.Relief`), so what you see is what you walk on. The old ±100 m strip's own hills are gone on generated lines.
    - **Tile checksums (§17.3):** host and joiner compare a terrain fingerprint (`TerrainField.Print`, heights at 256 points to the millimetre) and the plan's own fingerprint in the session setup, and a joiner that differs is refused by name. The plan's per-tile repair (a client fetching the host's heights) isn't built: the Welcome has to fit one packet, and on .NET's IEEE doubles a mismatch is a bug to fix, not to patch at runtime.
    - **Saves (§17.4)** keep the plan compressed (`RunCheckpoint.Plan`, about 12-20 KB); a resumed night plays it rather than generating afresh.
    - **Nearest track:** an alternate can loop out of sight of the main line, so `RailLine.Nearest` finds track near a point with a coarse grid index. Spurs and dead lines keep the old test (their points within their own length of here); `StopCrewTests` is sensitive to which track a player on the ground is placed on.
    - **The land's own shape:** past the formation, the terrain adds hills and ridges at landform scale (`terrain.reliefM` 32 m, 120-420 m wavelengths, half ridged noise, biased up so the line runs along valleys), none within 10 m of the track and full by 80 m, times the biome's `noiseScale` (mountain 2.2, marsh 0.3). Intents shape it: a cutting's walls go on up into the hill, a marsh or river stays low, a ledge's drop side only falls. It's in the sim's terrain, so it's what players walk on and what sightlines see; pass rates didn't move.
    - **Dressing by biome** (`WorldArt.PlanDressing`, `SettingKit`): trees at the biome's density and dead share (the black forest crowds the line from 7 m), boulders and crags where it's rough, reeds in the marsh, fences and farmhouses in the fields, ruined houses and walls in the dead-town belt, chimneys, tanks and walls in the ruin belt, headframes over the slag. The land takes its biome's ground texture and goes to bare rock where it's steep. `dt screenshot --survey` lights a scene flat and clear to look the shape over.
    - **The country is Nova Scotia gone dark** (biomes.json names, flora, props). `NovaKit`: black spruce and balsam fir, bare white birch, grey ghost spruce, granite erratics and ledge, dry stone walls, clapboard saltbox houses in faded paints, gambrel barns, the white wooden church with its needle steeple, a burying ground of leaning slate, fish sheds on stilts with their lobster traps. Each biome's trees, rocks, verge and props (their chance, distance and count per 150 m) are in `biomes.json`; `WorldArt.PlanDressing` places them.
    - **A far horizon for each night** (`Art.PlanSky`, `dt linegen sky`): the route's own 360° backdrop band, from its seed and tier, in place of the look's. The highland plateau with its scarps, drumlins nearer with a church steeple on one, a headland with its lighthouse over a gap of open sea and the fishing village under it, a colliery's headframe and smoking slag heap in the coal country, and the black spruce line with dead snags. Lights go out with depth: the lighthouse is dark past the Frontier, and the steeple has fallen in deep territory. The sky shader only lifts a band's value from the fog colour toward the horizon's haze, so this band keeps every layer low: the land stands as a dark mass against the paler sky. At night in the route's fog it's faint by design; the survey light shows it plainly.
    - **Maritime ground at the 2008-2012 bar (note 57):** each biome mixes landforms (`biomes.json` `landform`, shapes in `tiers.json` `terrain.drumlins/knobs/plateau`): drumlin fields, long whalebacks stretched along the ice's flow; the barrens' granite knobs; the highland plateau, flat-topped with gorges cut along a noise's zero line. Rolling relief is the old shape. It's all sim terrain, so it's deterministic and walkable. The ground textures come from `tools/art/texgen/mat_maritime.py`: barrens heath and reindeer lichen, spruce needle duff, lichened granite, Fundy red clay, sphagnum bog and shore shingle. Each is built from the CC0 scans with normal maps like the rest of the library, and detiled, i.e. the tile's own half-tile light and dark is taken out. Biomes name these textures directly as their `ground` and `materials`. Repetition is broken three ways. The terrain projects per triangle (triplanar-lite: a steep face takes its UVs from the side, not the top) at a 5 m tile. The terrain shader bombs both layers (after Quilez's texture-repetition technique 3): slow noise picks one of eight offsets of the tile, and neighbouring offsets cross-fade where the two samples differ. Offsets only translate, so the normal maps' tangent frames hold, and the second layer samples at 0.63 of the first's scale. On a generated line the cess spills its ballast straight into the biome's ground, tinted the same way; the old strip of `ground_mud` lined its puddles up into a chain. The terrain shader (`scene.frag`, terrain layer blend) modulates both layers by their own brightness at 1/6-1/9 scale, over each layer's mean from the last mip, and more so with distance. The second layer shows on slopes past 0.65 (full by 1.3) and in field-sized world-space patches.
    - **The Maritimes' water and line (docs/design/maritime-rules.md, from the research pass in docs/design/research/):** lakes, shores (Atlantic, Fundy mudflats, dyked marsh, a river up the valley), tidal-river trusses, and cove-hugging curvature (biomes.json `sweepChance`). Lakes and shores are plan data (`LinePlan.Lakes`, `LinePlan.Shores`), placed after the stations and before the sightlines authority, so authority sees the land they make. The readings made where the research left it open:
      - A shore's sea stands under the lowest rail along it, so where the line climbs along a coast the shore becomes a cliff.
      - A dykeland's fields follow the rail at a fixed depth under it, rather than lying at one level. Where the rail varies too much (`maxRailRangeM`) the shore is a mudflat shore instead.
      - A crossed lake is always crossed on a fill: the formation holds at rail height and the land falls at `fillSlope` into the water.
      - Shores stop short of tunnels and pads.
      - The terrain uses no trigonometry for any of it (a lake's heading is a unit vector in the plan, and the drumlins' flow cosine is a series), so tile checksums still match across machines.
      - Not yet: fog gathering on the shore (nothing reads the per-segment exposure's fog), and a trestle across a lake's neck.
      - Second pass (maritime-rules.md §2b): the forest in hard-edged stands with spruce spires and treeline walls; the country road as plan data, whose bed is in the terrain (the land under its centre), with level crossings and homesteads; and the Atlantic at the water, 2 m under the rail with barachois ponds behind the bank. The road is laid where the ground is open, so a line through cuttings gets short pieces or none, and not along the Atlantic, where the barachois has the land.
    - **Pass rates (§21 M3, `dt linegen sweep`, 50 seeds per tier at 3 and 20 cars):** within two attempts 96-100% on every tier (local 100%; 98-100% before the Maritime water and curvature pass), first attempt 80-100%; no fallbacks used, none left unpassed; 1-2 s a plan in Release (up to 16 s at the worst). An alternate that can't be laid leaves a dead line at its junction when the quota's junctions would otherwise be short; a short grade run shares its room between the vertical curves at its two ends as they need it.
    - **Not yet:** tile builds are 50 ms, not §17.5's 4 ms (tiles are only checksummed; the art builds 100 m cells, not tiles); the industrial bed that should fade out on the gate markers (§10.2) doesn't exist yet, so the markers are emitted and unused; grease has no traction effect in the sim (it didn't before either); the per-biome ballast, the corrupted and brass vegetation variants and the searchlights (§18) are the kits' existing pieces or nothing (a silent terminus is the fortress kit with its lamps out).
    - **Verified:** `LineGenTests` (M0 byte-identical, M1 a drive end to end, M2 ground to 250 m and a client's checksums, M3 six specs within two attempts, the derail, collapse and washout rules, prediction exact on a generated line), `LineGenConfigTests` (every config field is in the files), `AlternateTests` (clothoids, vertical curves, a loop run through, backed onto and coupled across), `dt linegen sweep` for pass rates, and the `lg-*` screenshots.
67. **Contradiction seeding and saving up (T64, App. B.1).**
    - **"The director draws pairs from a conflict table rather than spawning independently."** The table is in `enemies.json` `director.conflicts`, in the tuning's names plus three conditions:
      - `choir`: coming, its aggro past the approach (App. A.6);
      - `grade`: a climb or fall of 1.5 % or more within 600 m;
      - `facilityLoading`: at a facility.
      Sleepers count as about when they lie within 1500 m ahead.
    - **Ambiguity: "draws pairs".** Read as seeding, not flooding: while the run is short of its pairs ("at least one pair per run on Frontier and above. Two on Deep Territory": `pairsPerRun`), a spawn that would complete one with what's there now weighs ×3, and past halfway another ×3. After that, spawns are independent again.
      - The first cut weighted pairs all night, with the Choir counted as always present. Hounds and Lamplighters then took nearly every spawn, and the Climbers vanished from a frontier:7 night.
      - The director logs each pair it makes (`Director.Pairs`), and the harness reports them.
      - Pairs whose other half isn't in the game yet (the Drift, Followers) wait for it.
    - **Saving up.** The director spends as soon as it can afford anything, so a cost-5 threat only came when nothing cheaper could. In four frontier nights the Gaunt came once.
      - Now, from 20 % of the route in (`saveFrom`), cheaper threats leave enough budget for the rare, expensive ones the run can still have (`saveFor`: the Gaunt, on Frontier+ with a crew of three).
      - Once it's been, the saving stops.
    - **The flank was full all night.** A Dragger under a car nobody walked on stayed there for the rest of the run. Two of them held the flank's two places (App. B.1's cap), so neither Climbers, Clingers nor the Gaunt could come. Now a Dragger with nobody on its car's roof for 180 s lets go (`draggers.lingerSeconds`).
    - **Freeing the flank brought Climbers (ten on a frontier:7 night), and two stop-crew faults with them.**
      - At the Switchyard, the driver held for everyone with a part to ride the engine's rake. Four bots and the gunner were warming up in car 9, in the cut left on the main line, so the stop sat at `Held` until its give-up and the night missed the dawn.
      - Now a hand that's gone in out of the cold says so (`CrewCalls.Warming`). `Riding` leaves it out: it stays in its cut, and the train comes back for that.
      - `Held` also goes on without the missing after the loading leg's give-up, as that leg already did.
    - **Verified:** `ConflictSeedingTests` (7):
      - it saves up for the Gaunt over something cheaper;
      - it doesn't save on a run that can't have one;
      - a Lamplighter with Sleepers ahead is a pair and weighted up;
      - hounds pair with the Choir only when it's coming;
      - once the run has its pair, nothing's weighted;
      - the table only names things there are;
      - a Dragger nobody walks over lets go.
68. **Ground far overhead doesn't lift anyone off the train (T66).** `PlayerMotor.UpdateSupport` has always treated the ground as a candidate surface whenever you're at or below it ("nobody falls through the earth").
    - **The failure:** on the procedural line (note 66), the terrain field doesn't know about every cutting. On frontier:3's alternate line it stands 10 m over the rail. Everyone on the engine's deck was lifted onto the hillside at 12 m/s and left there. The train ran on driverless into the Deadman and the Hollow, and the crew froze.
    - **The fix:** ground more than a step (or this tick's fall) plus 0.5 m above you now loses to a train surface underfoot. It still catches whoever has nothing else under them.
    - The terrain should still carve that cutting. That's the line generator's to fix; this makes the player motor robust to it.
    - Harness traces now print the train's distance, speed and path on every line, which is how this was found.
    - **Verified:** `GroundOverheadTests` (3): the engine and roof cases fail without the fix. frontier:3 went from the whole crew lost to delivered (net 2645, one death, to the Gaunt).
69. **A winch with nowhere for its sleds to go is done (T66).** At a frontier:7 Foundry on the procedural line, the crane's castings filled the cars within reach of the winch. The pair then cranked for nothing until the loading leg's give-up (540 s), and the night missed the dawn.
    - `Run.SledHasRoom` asks whether a hauled sled could go anywhere: a cargo car with room in the rake standing at the winch.
    - When it can't, the driver counts the winch as done, and the winch hands leave their handles and come aboard (or carry crates, where there are crates).
    - **Verified:** `StopCrewTests.WithTheCarsAtTheWinchFullThePairDontCrankForNothing` (540 s of loading without the fix). frontier:7 went from the dawn missed (net -429) to delivered (net 4087), with loading down to 296 s.
70. **The 100-night playtest's fixes, and cold's bite cut (spec B.2).** A hundred bot nights (crews of 2, 4, 5 and 8, campaigns played contract to contract) found softlocks and deaths that weren't the design's:
    - **Two hands on one door undid each other.** Two walkers warming up in one car both pulled its open door the same tick; each toggle undid the other, every time. They froze indoors and the driver waited on them at the stop all night. A door now moves once a tick however many pull it (`Vehicle.ToggleDoor`, reset by the train's step).
    - **The stop driver's holds had no give-up once the switch was set** (`Held`), or once it waited on a switch to be set back with the shunter dead (`SetBack`, `Clear`). `Held` now goes in after its give-ups; with no shunter alive the driver gets down and sets the switch itself (`StopDriver.SetBackAlone`, `StopHand.SetBackAlone`).
    - **Walkers died jumping gaps** off-centre from the top of an end ladder, or chilled (a flat jump carries a fifth less). They now square up on the centreline first, wait out a curve, and turn back if chilled (`WarmUp.CanJumpGap`).
    - **Crate hands stood at the foot of the steps**, arrived but a hand's width outside the lane, holding crates. The old 200 s cold onset had been breaking the deadlock by sending them in to warm up.
    - **Cold was 73% of deaths, and it was attrition, not a decision.** The spec's numbers moved: 600 s to onset, 1200 s to death, 20 s to recover near heat, and a quarter the rate inside a car with a door open (`indoorsRate`). The onset slowdown is 0.9. `VigilTests` and `WarmUpTests` pin them.
71. **Running dark costs sign sight as well as obstacle sight (sight.json; the user's call after the playtest).** The line has boards, worked out from the route the same everywhere (`Route/Lineside.cs`), so none are sent:
    - a speed board before each curve too sharp for 15 m/s at 0.4 m/s² lateral (v = √(aR)), and before each weak bridge (7 m/s);
    - a low-clearance board before each tunnel: its mouth takes anyone standing on a roof (`DeathCause.Struck`), except a gun's crew, down behind the shield.
    - **Read in the lamp, lost in the dark.** A board is read once the headlamp is within 350 m of it (its reflective paint shines back up the line in the scene then too). Lamps down, the paint is nothing; what it warned of is made out only 10 m short. Once read, it stays known.
    - **Over a posted speed:** by 1.5 m/s the cars strain and the cargo lurches (integrity and cargo integrity by the second); by 3.5 m/s whoever's on the roofs goes over the side (`DeathCause.Thrown`); at 1.55× the board, the train derails.
    - **Ambiguity: Grease was level content that did nothing.** It now takes the rail's grip to 0.25 while the engine's on it (App. A.2 "can't climb, can't stop"), seen at 150 m in the lamp and 15 m without. Both machines set it before the train steps, so prediction holds.
    - **Bots:** the driver brakes in time to a read board and holds it until the last car's through. Walkers get off the roofs and inside (the warm-up's way in, `WarmUp.Shelter`) for a posted tunnel within 40 s, and stay in until the train's through. The gunner stays at the gun.
    - **Verified:** `LinesideTests` (7): the boards stand where they should on every tier; the lamp reads a board at 350 m and the dark only the mouth, close; a curve at, over and far over its board; a tunnel mouth takes the roof rider but not the gunner or someone inside; Grease; the driver takes a curve at its board lit and pays for it dark; walkers shelter for a posted tunnel.
72. **Trouble inside the cars (after the 100-night playtest: "more problems players need to face in cars, and reasons not to roof walk the whole time; defeatable, but able to hurt or kill").** Three incidents, not in the GDD's roster, on the shared spine (`Enemies/Incidents.cs`), interior zone, tuned in `enemies.json`: `CarFire`, `LooseLoad` and `Gnawers`, cost 2 each.
    - **Each takes a cargo car in the engine's rake** (one of a kind a car, `maxActive` each) and is answered from its floor: Use held at the cargo stack's face, within reach of the aisle. Cut the car loose and it goes with it. None can be shot.
    - **Car fire:** it grows from smoke (the telegraph) to alight at 0.35. Then it burns cargo, car and whoever's within 4 m of it (10 × how far gone, every 2 s; beaters take a 4-point scorch above 0.6). Each beater takes 0.08/s off. At full blaze for 30 s it takes the next cargo car.
    - **Loose load:** it telegraphs until the train's speed changes by 0.6 m/s² (a hard brake, the slack running in) or 90 s pass, then comes down across the aisle: 60 to anyone within 2.5 m, and 0.05 off the car's cargo. 5 s of lashing ends it. It binds with "watch the road": the brake that saves you from Sleepers drops the load.
    - **Gnawers:** they eat cargo and breed. 6 s after they're heard they're out of the crates, biting anyone in the car (7 × their number every 2 s), stamper included. Each stamper takes 0.1/s off. At full numbers for 30 s they take the next car.
    - **Tells:** 6–9 kHz crackle, a 1.4–2.2 kHz strap groan rhythmic with the joints, 9–12 kHz chittering. They're in spec A.4's collision table, and `AudioTests` holds them all ≥ +6 dB over the bed in chaos (they're at +24 to +36).
    - **Ambiguity: the Draggers "never leave their car" (App. A.4), but the user wants every problem beatable.** Stamped on as it reaches (the one it's reaching for holds Use), or a grabbed crewmate pulled free, it's hurt by `stampDamage` (0.5). At none it lets go for good.
    - **Bots:** walkers go into the troubled car by the warm-up's way in (`WarmUp.Into`), work it from the aisle (`WarmUp.Indoors`), and get out if hurt below 35. The gunner goes too, only while no hounds are out. Walkers stamp a Dragger reaching for them.
    - **Verified:** `IncidentTests` (9): a fire left alone burns and spreads; a blazing car kills and a beater puts one out; a hard brake drops a loose load on whoever's beside it; lashing it; Gnawers eaten away at a cost; trouble goes with a car cut loose; a Dragger stamped twice lets go; the director sends all three into cargo cars; a walker goes in and puts out a fire.
73. **The pacing rule, and the mail cranes (after the playtest: "a reward or a problem every 30 seconds at most, ideally 20").** Nights had stretches of 2½ to 6 minutes with nothing going on.
    - **The measure:** `World.Beats` logs each tick's moments: a threat telegraphing or hitting home, a board read, a bag caught or gone by, a stop made or left. `World.QuietSeconds` counts time out on the line with no beat and nothing telegraphing, committing or punishing (not in the yard, not after the night's over). `dt harness` reports `pacing`: beats a minute, and the longest, 95th-percentile and mean quiet stretch, with where the longest ended.
    - **The director:** at 18 s quiet (`paceSeconds`) it sends something, cooldown or not, overdrawing its curve by up to `pacedCost` (3). Such spawns are marked `Paced` in its log. Grace is 20 s, and budgets are about twice App. B.1's (GDD updated).
    - **Ambiguity: "total concurrent active" (App. B.1).** Read as engaged: alert, telegraphing, committing or punishing. Dormant Draggers under a car all night, and Lamplighters lingering after losing the light, had been filling the caps and silencing the director.
    - **Variety:** a kind among the last four spawns has its weight halved for each (Lamplighters were half of all spawns before). The in-car incidents weigh 0.5, and 0.4 of that at a stop.
    - **Mail cranes** (`sight.json` drops; `Route/Lineside.cs`): every 350–600 m from the route's seed, clear of structures, each with a green board 400 m short saying which side. A bag's caught by someone in a cargo car's open side door on that side holding the hook out (Fire, off a gun) as the car passes. In it: pay (30–90 scrip, paid at the terminus with the cargo, `RunReport.Mail`), 8 coal, 40 rounds a gun, or spares (+0.25 on the worst car). Lamps down, the board goes unread and you don't know which side. An open door is exposure: the cold (at the indoors rate) and the Choir.
    - **The terminus:** three boards (1400, 600 and 100 m out). Nothing spawns in the final 500 m, so these are the run-in's moments.
    - **Ambiguity: the in-car trouble at a stop.** Crate hands leave the crates for it; the winch pair go only for a fire that's alight or a load that's loose. Otherwise a stop's fire burned the whole train while everyone loaded. A fire now spreads at most once, and burns out once its car's cargo is gone; Gnawers leave then too.
    - **Not replicated:** which bags were caught (host only). A client's crane keeps its bag in view until the train's by.
    - **Measured** (`dt harness`, one seed each, bots): local crews of 2 and 4, frontier 4 and 5, frontier 8 and dead lines 6. The longest quiet stretch was 21–26 s, none over 30; the mean stretch 12–16 s; 3.3–4.7 beats a minute.
    - **Verified:** `DropAndPacingTests` (3): cranes stand clear of structures, each with a board, the same on every machine; a bag's caught from an open side door with the hook out, and not with the door shut or the hook in; a harnessed night is never quiet more than 30 s.
74. **The 100-night rerun (after the balance passes in notes 70–73).** The same four campaigns (crews of 2, 4, 5 and 8, 25 nights each, the same seeds), played contract to contract by the bots, each night allowed to its route's dawn:
    - **Endings:** 94 delivered (82 before), 3 crew lost, 2 dawn missed, 1 derailed. Seats lost 10% (16%); crew of 8 lost 4%, 4 and 5 lost 15–17%, 2 lost 2%.
    - **Deaths:** 113 (119), and no longer mostly one thing: mauled 28, Gnawers 21, jumped at speed 14, crushed 13, burned 11, cold 8 (87 before), struck in a tunnel 6, dragged 4, derailed 4.
    - **Pace:** the mean quiet stretch 12 s, 95th percentile 19 s, the longest 30.1 s (10 stretches in 100 nights, by a tick); 4.4–5.5 beats a minute.
    - **The in-car trouble:** 1784 incidents; fires beaten out mostly while still smoke (490 of 715), loads lashed about half the time before they came down (288 of 570), Gnawers stamped out before they were out 42% of the time. Walkers spent 53% of the night inside cars and 17% on roofs (23% and 31% before).
    - **Score:** mean net a night 803 (crew of 2), 497 (4), 482 (5), 783 (8); mail about a third of the gross. Cargo delivered 0.85–1.3 carloads a night (1.6–2 before): the trouble eats cargo.
    - **Bugs it found, fixed:** a gunner left on the ballast after a stop never got back aboard (it only borrowed the walker's legs on a car); a dead gunner stopped reporting to the crew calls, so the driver held at the next switch all night for it; a walker on the last car with the car ahead troubled had no way in for a tunnel. `StopCrewTests.AGunnerLeftOnTheBallast…`, `LinesideTests.AWalkerOnTheLastCar…`.
    - **Open:** a facility stop takes a bot crew 12–15 minutes (not counted in the pace, which is out on the line), and contract pay may want raising now the trouble eats cargo.
75. **The Passenger (T61, App. A.7 and B.7; GDD: "the tell is silence on a voice channel, in a game entirely about talking").** `EnemyKind.Passenger`, cost 5 (tier 5, as the Gaunt), tuned in `enemies.json` `passenger`.
    - **BOARD / BLEND:** into a car's room at a facility stop: the rearmost room of the engine's rake with nobody in it. It wears a living crewmate's face (the director's draw), replicated as `Extra`. Clients draw it through the crew's own path (`GreyboxScene.AsCrewmate`, `Crewmate.Looks`), the same cap, coat, mask and gait's beat as the one it copies.
    - **IDLE (the telegraph):**
      - It walks its car end to end at 1.3 m/s and stands 6 s at each end over a job it never finishes. After two rounds it goes on to the next room, bouncing at the rake's ends.
      - It has no voice: nothing routes one for it, and `VoiceMemory` has nothing of it to replay. The crew figures have masks now (no lips to read), so voice is the only way to tell.
      - **Ambiguity: "appears on the roster · crew count reads one too many".** There's no roster UI yet. The HUD's `N ABOARD` counts the figures (the connected crew plus any Passenger), so it reads one too many. A roster screen should list it under the face it wears.
    - **ISOLATE / STRIKE:**
      - **Ambiguity: "waits for a player alone in a car".** Read as: a living player in a car's room (not the cab), with nobody else living in there. After 30 s aboard (and 30 s after each strike), it walks car to car to the nearest such player.
      - In their car, with them still alone for 4 s, and within 1.2 m, it takes them (100, `DeathCause.Replaced`) and wears their face from then on.
      - Anyone else coming in resets the 4 s: two in a car is the counter's cheap half.
      - It's gone when its car leaves the train, or after 1500 s about.
    - **Ambiguity: the COUNTER's last step.** "Head count, and make everyone speak" is the crew's: it's how they find out which one it is. Once they know, someone in its car faces it (inside 25° of their look, within 2.5 m) and presses Use, and, called out, it runs (BreakOff, gone). Use on a real crewmate does nothing.
    - **Director gates** (B.7):
      - Dead lines and beyond, a crew of three or more, once per run;
      - at a facility stop, with a room to get into;
      - never with another corrupted human about: the Switchman's gate now also waits on the Passenger (B.7's "maximum one active at a time");
      - ×2 when the living crew is spread over three or more places (the ground, the cab, each car's room): "separated across multiple facility tasks".
      - `saveFor` keeps its price back from 20% in while a facility is still to come.
      - The comet cargo's "gates relaxed by one tier" waits for cargo types in the director (as for the Gaunt).
    - **Bots:** `Heed.Passengers` (applied to every harness bot, as `Heed.Rattles` is). A bot in a room with it, within reach, turns to face it and calls it out. Its world says what the Passenger is; that stands in for the head count.
    - **Found on the way: clients had no enemy tuning.** Neither a joiner's world nor a harness client's ever called `EnableEnemies`: they mirrored the enemies without their tuning. So `world.Enemies` was null on every client. The bots' counters that read it (the roof walker watching the Gaunt, `Heed.Passengers`) never ran outside the unit tests. The Weight's drag factor was 0 in a client's prediction (T59's "on the clients too" held only in `WeightTests`, which enable it by hand). `SessionSetup.Build` and the harness now give client worlds the tuning, with authority off, so there's no director and nothing spawns.
    - **Art:** the crew figure in the copied look. It's staged on car 2's roof among the three crewmates for the `--threats --crew` roof shot (in play it's only ever in the rooms, which no view looks into).
    - **Verified:** `PassengerTests` (8):
      - with nobody alone, it loops car to car and hurts nobody;
      - someone alone is taken after the rest and the stalk, and it wears their face after;
      - someone coming in first saves them;
      - facing it and pressing Use calls it out (and looking away doesn't);
      - a bot alone with it calls it out before it strikes;
      - it has no voice;
      - the director puts it aboard at a stop on the Dead lines to a crew of three, and not on the Frontier, to two, or between stops;
      - a client sees whose face it wears and where it is.
76. **Followers (T62, App. A.3 and B.3; GDD: "the asymmetry is the entire mechanic. The person in danger cannot see the danger").** `EnemyKind.Follower`, cost 3 (tier 3), tuned in `enemies.json` `followers`.
    - **Visible only to the others:** `HostSession.Interest` never sends a Follower's record to the player it's on, whatever the interest radius. So their machine has nothing to draw, play or put on the HUD, and a modified client couldn't show it either. Once it's nested in a car it's off their back, and it goes to everyone.
    - **Where it stands: `Enemy.Loose`.** On the ground a Follower is neither on a car nor at a distance along the line. `Attached = Loose` (−2) means `Local` is its world position. `Enemy.WorldPosition` and `GreyboxScene` read it as such; the record carries it as any other, and `Extra2` is its yaw.
    - **STALK:** put down 14 m behind someone on the ground (`PlayerState.World`) at a facility stop, closing at 1.5 m/s to 1.3 m at their back.
    - **ATTACH (the telegraph):** at their back, facing the way they face, every tick; it's placed from where they were as the tick began.
    - **BOARD:** once they're back on the train, if it's past App. A.1's window, it's in with them (Commit). Still coming up when they get aboard, it's lost them.
    - **NEST:** 4 s later, it's in the rearmost cargo car of their rake with a room and no lamp in it (Punish). With no dark car, it goes.
      - Nested, anyone in its room takes 20 every 3 s they stay (`DeathCause.Nested`).
      - A lamp body in the room, or held within 4 m of it, drives it out: "flees light".
    - **Ambiguity: "an observer calls it out → carrier halts → it detaches".** The call is voice and is the crew's. In the sim, the carrier standing still (below 0.3 m/s) for 2 s while someone else has it in view (35°, 30 m, not shut in) makes it let go and run. "Refuse boarding until the carrier is visually checked" is the same thing at the train's side. Speed is measured from how far they moved since the last tick, because the motor zeroes a grounded player's velocity.
    - **Director gates** (B.3):
      - at a facility stop, onto someone on the ground with nothing on them yet ("requires an excursion"), any tier, two at most;
      - weight 1 plus 1 for each more of the crew on the ground at once.
      - The Food cargo ×1.5 waits for cargo types in the director, as the comet cargo does. The conflict table's `sootChildren+followers` pair now has both sides.
    - **Bots:** `Heed.Followers`. A bot that sees one on someone else stops, keeps its eyes on it, and calls who it's on through `CrewCalls.Followed` (the radio's stand-in). The one it's on, called, stands still. The carrier's own bot never sees it; its world has no record of it.
    - **Art:** the husk (the Climbers' figure) at 0.9 × 0.8, dark, walking in their step; nested, crouched in the corner. For the `--threats --crew` roof shot it's staged at crewmate 1's back.
    - **Verified:** `FollowerTests` (11) and `NetcodeTests.AFollowerIsNeverSentToTheOneItsFollowing`:
      - it comes up behind and keeps to the blind spot at their pace;
      - stood still where someone can see it, it lets go (and not while they walk on, or with nobody looking);
      - aboard, it goes with them and nests in a dark cargo car (after the window), and it's lost them if they board before it's up;
      - nested, it bites whoever stays in its car, to death;
      - a lamp drives it out, and it won't nest where there's one;
      - bots call it and the carrier stands still;
      - the director sends one onto someone on the ground at a stop, and none with everyone aboard;
      - a client sees it where it stands.
77. **frontier:7 delivers again: the crane's operator stood in for the shunter, and nobody stood in for them (T67).** On main after the playtest passes (notes 70–74), the nightly soak's night (`dt harness --route frontier:7 --enemies --bots 8`) missed the dawn at 2727 s (net −559). Its one stop, the Foundry, ran its loading to the 420 s give-up and loaded nothing.
    - **Stand-ins for the winch pair.** The shunter died jumping at speed on the way. The winch pair's first hand, the crane's operator, stood in as shunter (`CrewCalls.StandIn`); the shunter rides the stop in the cab, so nobody went to the crane. `StandIn` now covers any part someone has had and nobody alive has now. The shunter's goes, as before, to the first of the rest with a part; a winch part goes to the first crate hand, between stops (never mid-part).
    - **Into trouble at a stop by the side door.** The in-car trouble drew the crate hands (note 73) to Gnawers in the last car of the cut rake. The walker's way in is a car's rear door, down from its roof or the one behind, and there's none of that at a rake's end. The three stood about on its roof and the ballast for the whole loading. `StopHand.IntoTrouble` takes them in as a crate goes in: from the ground, up the car's steps, open up, and in, where the walker works it from the aisle (`Tend`).
    - **Measured:** frontier:7 delivered (net 2638, the Foundry loaded 2 castings in 201 s, 2 deaths). frontier:3 (1885) and frontier:2 (2661) delivered too. deadLines:3 was still under way at 3600 s, with Gnawers the night's killer (5).
    - **Verified:** `StopCrewTests.WithTheShunterDeadTheFirstHandLeftTakesItOver` now also covers the winch part left behind.
    - **CI:** `vr check`'s wall-clock cap was 30 s for 30 frames. The simulated headset on lavapipe runs at about a frame a second since the renderer's native 720p (#71), and one runtime-skipped frame left a run a frame short. The cap is now 30 s + 3 s a frame.
78. **The Drift (T63, App. A.4 and B.4; GDD: "compounds brutally with anything that demands movement — which is most of the roster").** `EnemyKind.Drift`, cost 4 (tier 4), tuned in `enemies.json` `drift`.
    - **Where: `FeatureKind.Marsh`.** B.4 has it as "a terrain region, not an entity. Marsh and contaminated ground". A generated line's plan already has its bogs and tar ponds (`marsh` and `contaminatedMarsh` water, from the biomes), and `PlanRoute` now marks those on the main line as the route's Marsh.
    - **Ambiguity: "terrain-gated" spawning.** Read as the Hollow is (App. B.5, "condition-triggered, not placed"): over a marsh it comes up, once a marsh, whatever the director would draw, and it's charged when it does. It's gone once the train's rear is 60 m past. Left in the director's draw, it never came in ~150 s over deadLines:3's tar ponds against a dozen other options: a region that's sometimes not there isn't terrain.
    - **SPREAD:** it's over a car of the train, 4 m across at first, spreading to 9 at 0.15 m/s. It moves with the train, since that's where the movement is.
    - **Ambiguity: "movement".** Read as faster than 0.3 m/s in your own frame, since standing on the moving train isn't moving; a change of frame counts too. Anyone shut in a car isn't felt. Turning on the spot isn't movement: it feels feet, not eyes.
    - **DETECT (the telegraph):** the nearest mover within its radius. It surges at them at 2.2 m/s, so a walk outpaces it and standing doesn't. The tell is the rustle: dry reeds at 12–15 kHz, above everything in spec A.4's table (row added), swelling and ebbing as it creeps. It's gain −4 dB: at +8 it was the loudest stem in the chaos bench and drowned the Gnawers' band. It's now 31 dB over the bed from car 1.
    - **CONSUME:** within 1.2 m of them (past App. A.1's window) it's on them: 6 a second for as long as it stays (`DeathCause.Drift`). It moves with them, so walking on keeps it on.
    - **COUNTER:** "complete stillness ~4s". Stock still for 4 s, whether it's still coming or already on them, and it loses them and goes back to spreading. Twice its radius away and it's lost them too.
    - **Bots:** `Heed.Drift`. A bot it's after (its target replicates) stands still, look free, until it's lost.
    - **Not yet:** B.4's "×2 with chemical cargo aboard" waits for cargo types in the director, as the Followers' Food and the comet cargo do.
    - **Art:** the Dragger's limbs, dark, in a disc as wide as it's spread over the car's roof. Past the car's sides they lie on the ground beside the train. Staged over car 1 for `--threats`.
    - **Verified:** `DriftTests` (9):
      - standing still in it, nobody draws it (and it spreads);
      - moving in it, it surges and keeps eating while they move, to death (after the window);
      - stock still for 4 s, it loses them and stops;
      - shut in a car, nobody's felt;
      - off the marsh, it's gone;
      - a bot it's after stands still, and one it isn't walks on;
      - over a marsh it comes up once, charged, and not off one or again on the way out;
      - a generated line marks its bogs and tar ponds as Marsh;
      - a client sees how far it's spread and who it's after.
    - Also verified by `AudioTests`: the rustle ≥ 6 dB over the bed in chaos for whoever it's after.
    - **Measured:** deadLines:3 (tar ponds at 7.3–9.1 km): it came up once, surged three times, and got onto one bot once, which stood still till it lost them; no deaths to it. frontier:7 (no marsh on its main line) is unchanged: delivered, net 2638.
79. **Cargo types (T68, GDD §18–19, App. B.8: "cargo changes the run rather than just scoring it").** Until now a car's cargo was only how full it was; B.8's modifiers (and the Choir's `livestockFloor`, and `houndsLivestockWeight`) were read by nothing.
    - **`Vehicle.Cargo` (`CargoKind`):** a night leaves with the fortress's goods in its loaded cars. What a facility loads (a sled, a casting, a crate) is its cargo (facilities.json `cargo`):
      - grain elevator: food;
      - slaughterhouse: livestock;
      - chemical works: chemicals;
      - military depot: ammunition;
      - foundry: heavy;
      - wreck yard: salvage;
      - mine head: ore;
      - switchyard: goods.
      A car goes by what last went into it: `Run` marks every cargo car whose load went up that tick. Cargo replicates with the vehicle record and is kept in a night's checkpoint; an older save reads as none, and its cars keep their goods.
    - **Aboard** is what's in the loaded cargo cars of the engine's rake (`Director.Aboard`); a car cut off on a spur isn't aboard.
    - **B.8 in the director:**
      - livestock: Hounds ×`houndsLivestockWeight` (2.5), and the Choir's floor is `livestockFloor` (3), since they're never quiet;
      - food: Hounds ×2, Followers ×1.5;
      - comet material: every weight ×1.4, and the Gaunt's and the Passenger's tier gates one tier lower (`cometRelaxesGates`), in the draw and in what's saved for.
      The table is `cargoWeights` in enemies.json.
    - **Ambiguity: "The Drift ×2 with chemicals".** The Drift isn't drawn (note 78: it's terrain), so read as spreading twice as fast (`drift.chemicalSpread`).
    - **Not yet:**
      - "Chemicals: gunfire indoors becomes lethal to the crew": there's no gun indoors (they're mounted on the roofs and the tender);
      - ammunition's, coal's and timber's "every consequence is worse";
      - where comet material comes from: no facility loads it; it wants a contract type;
      - GDD open question 7, whether a car shows its cargo from outside.
    - **Verified:** `CargoTests` (6): each facility's cargo; a crate stowed at a stop is that facility's cargo, and a client and a restored train see it; with livestock aboard the Hounds come first 11 nights in 16 (7 with goods); livestock holds the Choir at its floor; chemicals spread the Drift twice as fast; comet material brings the Gaunt to a Local line.
    - **Measured:** frontier:7 (2638), frontier:3 (1885) and frontier:2 (2661) all delivered, unchanged. Their stops load heavy cargo and salvage, which B.8 doesn't weight.
80. **A stop's trouble takes two hands, not all of them, and the loading watches the clock (T70).** frontier:11 (26 km, a 46.7 min dawn; its validator already puts its slack with stops at −0.9 min) missed the dawn. Its one stop, a Slaughterhouse, had Gnawers and loose loads one after another. Every crate hand left the crates for each, nothing was loaded in 420 s, and four died to the Gnawers.
    - **Two crate hands to trouble** (`StopHand.TakesTrouble`, `TroubleHands`): of the crate hands, the first two alive go to a car's trouble at a stop, and the rest keep loading. The winch pair go as before, for a fire alight or a load loose. Gnawers deaths there went from four to one.
    - **Aboard, counted from done.** The wait for everyone aboard (`AboardGiveUp`) was timed from the start of the loading, so a stop that finished early still waited out the whole give-up again. It's now timed from when the loading was done.
    - **Late: the loading ends when the dawn won't wait.** The loading leg is done once what's left of the night is less than the run home plus 180 s of leaving.
      - The run home is at the pace the night has kept: the main-line distance covered since it got under way, over the time spent moving, every stop off the clock.
      - Measuring it turned up two slips. Down a spur the engine's distance is the spur's, so it's measured from the stop's hold on the main line. The driver's cruise setting is the spur's crawl while stopped, so the pace is capped by the fastest cruise the night's had.
    - **frontier:11 still misses, by under a kilometre:** the line after its stop is slower than the line before it. At the pace kept so far it isn't late; at the pace it goes on to keep, it is. A driver cautious enough to skip that stop would skip frontier:7's Foundry as well (its spare at the decision is under 600 s by the same measure), and that's the soak night's only stop. It's left as a tight line, as its validator says.
    - **Verified:** `StopCrewTests.AtAStopOnlyTheFirstTwoCrateHandsLeaveTheCratesForTrouble`. frontier:7 (2638) and frontier:3 (1885) delivered, unchanged.
81. **The crew roster lists the Passenger under the face it wears, and never as heard (T69).** App. A.7's Passenger "appears on the roster". The roster (hold Q) lists everyone aboard by the figures: you, each crewmate, and each Passenger. A Passenger's line carries the id of the crewmate it copies, so it sits beside the real one.
    - Each line says when that crewmate's voice last came in (`VoiceChat.SinceHeard`). Voices are keyed by player id, so the line would otherwise borrow the real crewmate's voice.
    - A `RosterLine.Voiced` flag stops that: the Passenger's line always reads NOT HEARD. The tells are "one too many" and "never speaks", and the roster puts both on screen together.
    - Its where reads as a crewmate's would inside a shut car ("inside car N, shut in"). It's a mimic, and the roster doesn't give it away by wording.
    - **Verified:** `HudTests.TheRosterCountsThePassengerAsOneTooManyAndNeverHearsIt`; `dt screenshot --hud --roster`.
82. **The guard van has a rear platform, and the bots beat the Weight off from it and hold the gaps against Climbers (T65).** GDD §24's Weight counter is "melee from the rear platform". The rear car had no platform: its back end was a wall at roof height, 3 m above the coupling. So the bots' only answer had been to let the Weight take the car.
    - **The platform:** a guard van last in the train gets a grating across its back end at plate height, `train.json` `platformDepth` deep. The rear door opens onto it, and a short ladder runs up to the roof. It's a coupler-kind surface, so standing on it works as a plate does: the rear car has no coupling behind it, so a press there cuts nothing. Its ladder foot is at platform height, so climbing down steps off onto the grating rather than the ballast.
    - **Beating the Weight off:** any bot on the held car (the gunner at the rear gun, a walker on the roof) climbs down the ladder, crosses to over the coupling (clear of the door's reach), and swings. Each swing is Use pressed for 0.35 s then released, and each counts as one blow. After the Weight lets go, the bot climbs back up as out of any gap. Bots on the other cars keep clear, as before. With no platform (a cargo car last), everyone still keeps clear.
    - **Holding the gaps against Climbers:** App. A.4 says someone on a roof end within `holdReach` of a gap holds it. A walker on a car at either end of a gap stands over it when a Climber is scrabbling there, or running alongside within 20 m of it. When a held Climber tries another gap, that's often this car's other end, and the walker follows.
      - Only the cars either side hold a gap; nobody runs the train's length. So one walker holds two of a Climber's three tries, and two adjacent walkers hold all three.
    - **Verified:**
      - `WeightTests.TheGuardVanLastHasARearPlatformOverTheCouplingItTakes` and `ABotOnTheGuardVanGoesDownToThePlatformAndBeatsItOff`, a walker and a gunner: the car survives, and the Weight never punishes.
      - `ClimberTests.ARoofWalkerHoldsTheGapAtItsCarsEndAndItTriesAnother` and `TwoRoofWalkersEitherSideHoldEveryGapItTriesAndItGivesUp`.
      - frontier:7 delivered, 2638 unchanged. `dt art show guard` shows the grating.
83. **`dt balance` sweeps the procedural line (T71).** The sweep ran its nights on the old `RouteGenerator` routes; it now runs them on `LineGen.Routes`.
    - Each line is planned for the night's train length, as a night in the game is.
    - The yard length comes from the line's own gate (`GateOr`), as `dt harness --route` does.
    - The first sweep: frontier, seeds 1–2, crews 2 and 8, 10 cars.
      - Survivable at 2: both delivered (nets 2195 and 1912).
      - Non-trivial at 8: 41 punishes a night.
      - Fair: no commits without the reaction window.
      - Mean quiet: 13.9 s.
      - It fails "never quiet over 30 s", with a 32.8 s stretch on frontier:1 at crew 2.
      - frontier:2 at crew 8 missed the dawn, though crew 2 delivered it.
    - Both are T74's to chase: they're findings about the lines players get, which is what moving the sweep was for.
84. **Grease's counter: sanding from the engine's running boards (T72).** Grease was already level content: the line generator lays it, and the lineside drops the grip to `greaseTraction` while the engine's on it. What was missing was its counter, App. A.2's "sanding from the running boards restores traction over ~8s".
    - **The running boards.** The engine has a walkway each side, `runningBoardWidth` out past the cab side at deck height. It runs from the boiler's front to partway across the cab doorway, so you step out of the doorway and forward onto it.
      - Each has a sandbox `sandboxAhead` ahead of the cab.
      - The boards are only ever footing. Collision skips them, so crews on the ballast pass under them to the cab steps; six stop-crew tests caught the first version pushing them off the steps.
    - **Ambiguity: "restores traction over ~8s".** It's read as a rate, not a one-off:
      - Held at a sandbox, the grip climbs from `greaseTraction` back to dry over `sandSeconds`.
      - Let go, and it falls back over `sandFadeSeconds` (sand is only under the wheels while it's going down).
      - So whoever's out there stays out there to the end of the grease: the "sends someone onto the running boards at speed" cost.
      - Both numbers are in `sight.json` beside `greaseTraction`.
    - **Replication and prediction.** The sand level is train state. It's on the World record, and the lineside steps it on both machines, as it does the grip.
    - **The driver bot.** On grease the controls do nothing ("cannot climb grade, cannot brake, cannot accelerate"). Once the grease is costing it way, more than 2 m/s under cruise (a climb), the driver leaves them. It goes out of the doorway, along the board to the sandbox, holds Use until the engine's off the grease, then comes back the same way.
      - Steam stays on, so the sanded drivers pull.
      - On the level it doesn't go. The first version sanded every stretch and shut steam off to do it. On frontier:7 that slowed the train from 13.9 to 11.3 m/s with Cinder Hounds behind; they gained, and five punishes against one cost most of the cargo (net 2638 fell to 679). The unsanded train had simply coasted through.
    - **The tell.** A HUD cue when the grease ahead comes into sight in the lamp: the chemical smell and the shine on the rail. The prompt at a sandbox shows the grip.
    - The guard van's rear-platform ladder (note 82) is now drawn too; the kit drew only ladders from the ground.
    - **Verified:**
      - `SandTests`: the boards and boxes, the grip coming back over 8 s and going over 3 s, Use in the cab doing nothing, the driver sanding a stretch through and back into the cab, and a client having the host's sand.
      - `dt art show engine`.
85. **Balance on the procedural line: the grace stretch, the terminus approach, and a stop's lateness (T74).** Note 83's first sweep failed "never quiet over 30 s" (32.8 s), and frontier:2 at a crew of 8 missed the dawn.
    - **Quiet stretches.** The pacing report now lists every quiet over 30 s: how long, where it ended, what ended it, and why the director had sent nothing (`Director.HeldBecause`: grace, cooldown, at the cap, banned, nothing fits).
      - All three on frontier:1 were in the line's opening grace stretch: "banned (grace)", ended by a mail board.
      - Ambiguity: the plan's grace stretch is gate to 2 km, "covers the GDD 90 s grace period". Its spawn ban held for as long as the crew took over those 2 km, minutes at the bots' pace. The playtest's later rule is "out of the gate in 20 s", the director's `graceSeconds`.
      - The later rule wins. `director.lineGraceSeconds` (20) lifts the line's grace ban that far into the run; -1 keeps the plan's reading.
      - The stretch keeps its easy geometry, and still has no Sleepers or Grease on it.
    - **The terminus approach.** Once frontier:2 delivered, it showed a 132 s quiet into the terminus, where nothing's sent by design (`terminus_safe`, the no-spawn final approach). That's the night letting go, not a lull, so it isn't counted as out on the line.
    - **Lateness.** frontier:2's Wreck Yard loading gave up at 300 s, not counted late: 1791 s of night left against 1465 s home plus 180 s of leaving.
      - Then everyone-aboard took its whole 120 s (hands kept climbing down to the Gnawers in car 4).
      - The line after the stop ran at 12.2 m/s, not the 13.7 kept before it.
      - So a stop's loading is now late with the aboard wait in hand too, the run home reckoned at 0.9 of the pace kept (`LatePace`).
      - A stop is only planned if it wouldn't be late on arrival, reckoned the same way. frontier:7's Foundry stop had been made and then given up at once, loading nothing.
    - **Sweep after** (frontier, seeds 1–2, crews 2 and 8): every check passes.
      - Longest quiet 22.7 s, mean 10.5 s, 28.5 punishes a night at crew 8, fair.
      - frontier:2 at crew 8 delivers (2114).
      - frontier:1 at crew 8 now misses the dawn. A Climber got into the cab with every walker warming inside, killed the driver, and no bot takes over the cab (T75).
      - frontier:7, the soak's night, delivers (2895).
    - **The Ferryman's straight.** With the night paced differently, frontier:7 got a Ferryman short of a stretch the line's authority slowed. The driver braked for it, as bound to; it boarded and took the conductor, and the Deadman ran the train back down the line.
      - "Do not slow down" has to be the crew's choice. `Ferryman.ClearAhead` also wants nothing posted, and no authority, under the train's speed over its approach (`LineAuthority.Lowest`).
      - `FerrymanTests` has a weak bridge's board, and the same line without it.
86. **The frame-rate targets (the director: "the game must run 72fps in VR minimum and 90fps on PC").** Not in the GDD or the spec. They and the budgets that keep them are in `tuning/perf.json`:
    - **The targets.** A flat screen draws 720p at 90 fps, which is 11.1 ms a frame. A headset draws two eyes at half the runtime's recommended size (a Quest 3's 2064x2208 halved) at 72 fps, 13.9 ms. The desktop window mirrors the flat view as well.
    - **The budgets.** The main thread's drawing work may take `cpuShare` (0.6) of a frame: the scene built, uploaded and recorded. At most `maxFrameTriangles` (1.5 M) are drawn over every pass, and at most `maxPassDraws` (1500) draws go in any one pass.
    - **`dt perf` measures it.** It runs each standard view, with the crew on the roof and the threats about, for a run of frames, flat and as a headset draws it. It reports:
      - the build, upload and record times, on the machine's CPU;
      - each pass's GPU time, from timestamps (`GreyboxRenderer.PassTimes`);
      - the triangles and draws in each pass (`FrameStats`);
      - the build broken down by part (`GreyboxScene.Timings`).
    - **What lavapipe can say.** Lavapipe's pass times are CPU rasterisation: they rank the passes, not a GPU's milliseconds. The counts and the CPU times hold anywhere, and the cloud's 2.1 GHz Xeon is slower than any desktop the game ships to.
    - **Measured (30 Sep, before any fixes).** Neither target is met on the CPU side:
      - **CPU.** Building a view takes about 23 ms against a 6.7 ms budget. 21 ms of it is skinning the enemies on the CPU (116k triangles into the frame's soup, remade every frame). Uploading the soup takes 5 ms per renderer, three times in a headset (two eyes and the mirror).
      - **Triangles.** A flat frame draws about 1 M triangles: everything goes into both shadow maps as well as the scene, with no culling. A headset frame draws 3 M, twice the budget: each eye draws its own shadow maps.
      - **Serial frames.** Every submit waits for the GPU to finish (`GpuContext.Submit`), so a frame costs its CPU time *plus* its GPU time, not the larger of the two.
    - **The order of the fixes.**
      1. Skinning on the GPU: bind poses uploaded once, bone palettes per frame.
      2. Kit meshes in device-local memory.
      3. The eyes sharing one set of shadow maps, and frustum culling for every pass.
      4. Frames in flight, so the CPU builds the next frame while the GPU draws this one.
    - **1 is done: skinning on the GPU.** Each model's bind pose, for a variant and a look, is cooked once into a skinned asset (`Skinner.Bind`; `SkinWeights` as a second vertex stream). A draw then adds only its pose's bone palette (`MeshBuilder.Skinned`) to the frame's bone buffer (scene set binding 10). The scene and both shadow passes have skinning twins of their pipelines (`skin.glsl`, `SKINNED`).
      - A glow that pulses goes to the instance (the shader's `tint.a`) rather than into the look. Looks are keyed to 1/128th, so a hull heating as it's drilled doesn't make an asset a frame.
      - The frames match CPU skinning to within rounding (mean pixel difference 0.0002 on the roof and chase views with the threats).
      - Building a view went from about 23 ms to 1.5 ms. The CPU share is 2.2 ms flat and 3.1 ms in a headset, inside both budgets.
      - `MeshBuilder.Flattened` is skin.glsl's blend on the CPU, so the tests still ask where a posed hand or a ragdoll's limbs are.
    - **3 is done: culling, shared shadows, and a mirror that's a blit.**
      - Every kit instance has a bounding sphere (`MeshAsset.Bounds`). Each pass draws only those inside its own view: the camera's for the scene, the lamp's frustum and the moon's box for the shadows.
      - What culling leaves out: a caster outside the lamp's frustum or the moon's box can't shadow anything inside it, so the maps are unchanged. The soup and skinned pieces are always drawn, since a pose can reach past its bind pose's sphere.
      - A headset's right eye samples the left eye's shadow maps (`ShadowsFrom`): both eyes stand at the body's eye point, so the lamp's and the moon's views are the same.
      - The desktop window in a headset session shows the left eye's middle (`VrView.Mirror`). It had drawn the whole flat view a third time.
      - The mirror is verified only by the app running a night on the simulated headset (xvfb + Monado) without errors. The window's contents can't be read back here.
      - Frames per view: a flat frame draws 0.66–0.78 M triangles (was about 1 M), and a headset frame 0.87–1.09 M (was 3.1 M).
      - Lavapipe's headset frame took about 45% less GPU time.
      - Pixels are the same as before on the roof view; the others differ in a few dozen pixels at most, from the skinning's rounding.
      - `PerfBudgetTests` holds every view inside `maxFrameTriangles` and `maxPassDraws`, flat and in a headset. It also checks that the shared shadows draw what an eye's own would.
    - **2 is done: kit meshes in the GPU's own memory.** Each piece is written to a staging buffer and copied into a device-local one. A frame's new pieces go across in one submit (`GreyboxRenderer.Resident`, `CopyStaged`).
      - The per-frame buffers stay host-visible: the soup, the bone palettes, the effects and the overlay.
      - Pixels are identical. It can't be timed on lavapipe, where all memory is the same.
    - **Left: 4.** Every submit still waits for the GPU. Frames in flight need per-frame copies of the frame's buffers and descriptor sets.
87. **A fireman in the cab, and nobody under a Climber that's got into it (T75).** On frontier:1 at a crew of 8, a Climber got into the cab while every walker was warming inside a car. It went through occupied car 1 and came down where the driver stood. It killed the driver, the Deadman took the empty cab, and the train ran back down the line all night.
    - **Nobody can reach the cab at speed.** The tender is full width and the cab roof is over its front edge, so a walker can't get over it. The only other driver a crew has at speed is someone already in the cab.
    - **The fireman.** A harness crew of `FiremanFrom` (6) or more keeps its last hand in the cab as fireman: a `ConductorBot { Fireman = true }` posted beside the driver.
      - While the driver lives, the fireman stands by, keeping the fire and keeping the cab from ever being empty. It says no stop job, so the stops don't wait on it.
      - Once the driver's been heard driving and then isn't, the fireman takes the controls and says it's the driver.
    - **A Climber in the cab.** It takes whoever comes within reach, and stays while anyone's in the cab; leaving it empty is the Deadman's. So nobody leaves.
      - The driver and fireman keep to the cab's front corners, out of its reach (it comes down at the cab's middle, where the driver used to stand).
      - They work the controls and the firebox from there; only where they stand changes.
    - **Firing from its own side.** Firing, each keeps to its own side of the firebox door: the driver right, the fireman left, clear of the vent's valve on the left wall.
      - Before, a bot walked straight fore and aft at the firebox from wherever it stood. The fireman fetched up by the vent, which was then the nearest thing to hand, and never shovelled.
      - On deadLines:3 the fire went out with 301 units still in the tender. The Hollow came down the cold stack and took the four in the cab.
    - **Verified:** `FiremanTests`. The fireman stands by, then takes the controls when the driver dies, and keeps a low fire up by itself. With a Climber in the cab, both stay in the cab, unhurt and out of its reach, for 20 s.
      - The sweep (frontier, seeds 1–2, crews 2 and 8) delivers all four nights and passes every check. frontier:1 at crew 8 comes home (2376), frontier:2 at crew 8 makes 3515, 43 punishes a night.
      - frontier:7 delivers (3791).
      - deadLines:3, "still under way at 3600 s" since T66 (T73), now delivers (2612, all eight home) in a 5400 s window. The night was the dead fire and the window: a Dead Lines night with its yard runs past 3600 s of harness time.
88. **The Dead Lines sweep: the cut-off rake counts as aboard, the driver calls all aboard, and the terrain at its ceiling isn't a quiet (T76).** The first sweep on the Dead Lines tier ran with a 5400 s window per night; a Dead Lines night with its yard runs past 3600 s. Crews of 2 delivered both nights. Crews of 8 missed the dawn on both, and one quiet ran 49.4 s.
    - **Held for the cut-off rake.** On deadLines:2 the Held leg waited 360 s: its two give-ups.
      - It wanted everyone with a part aboard the engine's rake. The gunner and two crate hands were on the car behind the cut, and nothing sent them across.
      - That car stays on the main, and the engine comes back and couples up to it, as for anyone warming in it (T64). So a stop's waits count anyone on any car of the train as aboard.
    - **All aboard.** Both nights then spent the aboard wait's full 120 s after the loading.
      - The crate hands kept fetching crates after the driver had called the loading done or late.
      - The driver now says so (`CrewCalls.Leaving`). Crate hands put nothing more in, shut up behind them and come aboard; the winch pair stop cranking and come aboard.
    - **Terrain at its ceiling.** The 49.4 s quiet was "director: terrain at its ceiling": linegen plan §15.4's "none of its own while the terrain is already at its hardest there". The terrain's the problem there, so it isn't counted as a quiet.
    - **After** (deadLines, seeds 1–2, crews 2 and 8, 5400 s): every check passes.
      - Longest quiet 22.7 s, mean 10.0 s, 66.5 punishes a night at crew 8, fair.
      - Crews of 2 deliver both nights.
      - Crews of 8 still miss the dawn, losing 5–6 (T77).
      - frontier:7 delivers (2984).
89. **Out of a car the Gnawers get out in, and the run home reckoned by the line (T77).** After T76, crews of 8 still missed the Dead Lines dawn, losing 5–6 each.
    - **Gnawers.** Five of deadLines:1's six were gnawed while warming in a car. Out of the crates, Gnawers bite everyone within reach in the car.
      - A walker too hurt to stamp them out (`TooHurt`) sat through it; nothing sent it out.
      - A warm-up now leaves a car whose trouble breaks out while it's in there and isn't its own to work, and warms somewhere else.
    - **The run home.** Both nights made their facility stop and missed the dawn by under a kilometre. Before the stop the train had kept about 13 m/s; after it, 10.9 m/s: a Dead Lines route's back half is harder than its front.
      - The pace kept so far flattered the rest of the line. `StopDriver.Home` now also reckons the run home by the line's own authority from here (`LineAuthority.SecondsTo`, capped at the driver's cruise), and takes the longer of the two, each with `LatePace`'s margin.
      - It's used by both the stop decision and the loading's lateness. On the Dead Lines the crew of 8 now skip the facility stops they haven't the night for, and still coal.
    - **Verified:** `IncidentTests.AWalkerTooHurtToStampThemOutLeavesTheCarItsWarmingInWhenTheGnawersGetOut`, which fails without the change.
      - The sweep (frontier and deadLines, seeds 1–2, crews 2 and 8) delivers all eight nights and passes every check: longest quiet 22.7 s, mean 9.9 s, 42 punishes a night at crew 8.
      - frontier:7 delivers (2447; it now skips its Foundry stop).
90. **Mods ship through Thunderstore (T78, the director's call).** A Dark Territory mod is a Thunderstore package, and the loader (note 53's v1, `Ballast.ContentMods`) reads it as it is.
    - **The package.** `manifest.json` (`name`, `version_number`, `website_url`, `description`, `dependencies`), `README.md`, a 256×256 `icon.png`, and a `content/` folder.
      - `content/` is laid over the game's `content/` as v1 mods are: added, replaced, or `"$patch": true` merged key by key.
      - A package without `content/` has its root laid over, bar its own files.
      - v1 `mod.json` folders still load.
    - **Identity and order.**
      - A mod's id is the `Namespace-Name` folder the mod managers install it under, else its name. It's what the mounted copy records, so it's what the content hash and a joiner's mod check compare.
      - Dependencies (`Namespace-Name-1.2.3`, read as "at least") load first. Otherwise mods load by `order`, then id.
      - A mod with a dependency missing or too old, or in a circle, isn't loaded. `ContentMods.Scan` says why, and the game prints it; the rest still load.
      - The same package twice (a profile and the mods folder): the newest.
    - **Where they come from.**
      - The `mods` folders as before.
      - A mod manager's profile: r2modman and the Thunderstore Mod Manager launch the game with `--mods-dir <profile folder>`, or set `DARKTERRITORY_MODS`.
      - Zips dropped straight into any of them, unpacked into the app data's `mods-unpacked` as the managers lay them out. They're unpacked again when the zip changes, and removed when it's gone.
    - **Making one.** `dt mods pack <folder>` checks a package against the site's upload rules and writes `Name-1.0.0.zip`, or lists what it would refuse and exits 1:
      - name characters, a `major.minor.patch` version, a description of 250 characters at most, dependency format;
      - README present, icon size, and some content.
      - `tools/mods/example` (LateDispatch) is a package the tests hold to those rules. Its README is the modder's guide.
    - **Not yet.** The game's own Thunderstore community page and its install rules are set up on the site, not in the repo: packages install as `Namespace-Name` folders into the profile the manager hands the game.
    - **Verified:** `ContentModsTests`: a package laid over from `content/`, dependencies ordering the load, missing, too-old and circular dependencies left out with the reason, a pack and a refused pack, a dropped zip loaded and then uninstalled, and the example package.
91. **The demo is an edition: an overlay on the content, baked into its build (T79).** GDD §21 "demo ships with five: one per pressure zone", §35 "demo roster of five, gun arcs, two facilities". The demo is the game with less in it, so it's data, laid over the base content as a mod is (note 53), and never a second code path.
    - **What's in it** (`editions/demo`, a v1 mod folder that loads before every mod):
      - `enemies.json`'s new `director.roster`: the five are the Sleepers (forward), Cinder Hounds (rear), Clingers (flank), the Hollow (interior) and the Choir (structural, always about with the guns). The trouble in the cars (fire, loose load, Gnawers) isn't on the GDD's roster; it's in, because it's what the crew does between threats, and without it the pace rule has too little to send.
      - The line generator's Frontier with two facilities, and no Grease (an enemy, not one of the five). A Frontier route's severity lerps toward the Dead Lines column, so that column says two as well; nothing else reads it in the demo.
      - `tuning/edition.json`: the tiers a quick night can be on (the Frontier), no campaign, at most 8 cars, "DEMO" under the title, and a wishlist line after a night.
    - **Ambiguity:** what "five" leaves out. Read as the director sending nothing off the roster and nothing condition-triggered off it coming up (the Deadman, the Drift), with no budget saved up for what the edition hasn't got (the Gaunt), which would otherwise starve it. `roster: []` is everything, the full game.
    - **Builds.** `tools/package.sh --demo` copies each build to `DarkTerritory-Demo-<rid>` and bakes the edition into its content (`dt edition bake demo`). `tools/upload.sh --demo` sends only those, checking that each is the demo, and the game's upload refuses a demo build. From the repo, `--edition demo` plays it (app or `dt`), mounted into the app data. A demo asked for a tier it hasn't got (`--route deadLines:3`) plays the Frontier. The content hash keeps demo and full crews apart.
    - **Verified:** `RosterTests` (nothing off the roster sent or saved for, nor the Deadman or Hollow on their conditions), `EditionTests` (the baked demo's roster, two facilities and no Grease on three seeds, and its front end), `linegen sweep` on the demo's Frontier (60 seeds, all passed, no fallbacks), and two harness nights on it: frontier:7 at 8 bots delivered, net 2643, longest quiet 20.2 s; frontier:3 at 2 delivered, net 46, longest quiet 18.6 s. CI packages the demo and starts it asking for a Dead Lines night.
92. **The GDD v1.1 roster (T84-T88): sixteen enemies and the Choir on one spine with a GRAB, the director on want tags.** The v1.1 GDD (now v1.2 in the repo: v1.2 adds only Appendix D, death and return, which is separate work) replaced most of the v1.0 roster. The Ferryman, Clingers, Rattle, Long Whistle, the Weight, the Deadman, the Lamplighters and the Hollow are gone from the code, with their tests. The Stoker absorbs the Hollow and the Track Doll replaces the Deadman's job. The Sleepers and the Drift are now §22 hazards (track debris, marsh): level content with `Hazard = true`, never drawn or charged by the director. Loose loads and Gnawers are gone. A car fire is App. C.5's supporting system, started by Fire Flies.
    - **The spine** (`Enemy.cs`) gains GRAB between COMMIT and PUNISH (App. A.1). `Grab()` is only possible from Commit. A kill (`Kill`) is only possible in Punish, and only on the one held. Anything else that hurts a player (`ctx.Bite`) floors them at 1 hp: only the train's own dangers (`ctx.Harm`: fire, derailment, the lineside) and a grab's end can kill. A grab is broken by a friend holding Use within `grab.pullReach` of the held player (where the enemy allows it, `PullsFree`), or by hitting what holds them (`Struck`). The held player's movement input is zeroed (`PlayerFlags.Held`, replicated). `AssertFair` checks both new edges: grabs only from Commit, punishes only from Commit or Grab.
    - **Appendix C systems:** melee with whatever you carry (`PlayerActions.Swing`, `enemies.json` `melee`; enemy health is in blows, a cannon round is four); the crude cannons' manual reload (`combat.json` `reloadSteps`, `reloadStepSeconds`; Use held at the gun); group counting (`CrewSense.Group`, 8 m); the loudness meter (`ChoirState`: voices from each player's intent `Voice` byte, the whistle, machinery, and a burst per shot, smoothed over `windowSeconds`); server-side voice effects (`VoicePath.Muffled` under Tippy Toesie's hand, `Fading` with a gain as a Soot Child drains); hand-carried toys (`BodyKind.Toy`, in the guard van); wall-mounted extinguishers in every car with a room, with charge and a slow recharge on the mount; per-car lamps (`PlayerActions.CarLamp`); the whistle cord (`PlayerActions.Whistle`).
    - **The director** (`Director.Decide`, `Spawns.Rules`): one rule per enemy with its B.2-B.8 gates and weights, the v1.1 cost table, want balance (a want under its target share weighs up, one over it down, by the square root of the ratio, clamped to 0.5-2), the zone cap (2), one corrupted human at a time, and the v1.1 conflict table. Saving up now works for any kind in `saveFor` whose rule would have it now (the Passenger by its own rule). The Choir isn't spawned: the meter held above `threshold` gathers it over `buildSeconds`, and quiet drains it.
    - **Solo (T89): a night alone comes with a bot crew.** The quick night's `CREW` option (default three; `--bots n` from the command line) hosts privately and connects that many bots as clients over localhost (`BotCrew`, the harness's own crew and think chain). The first drives, being first aboard; the player spawns on a roof, free to go where the trouble is. `NetPlayTests.ANightAloneComesWithABotCrewThatDrives`. `CREW: JUST YOU` is the old prototype night, all yours to do. Hosting for friends takes bots too. Campaign nights don't yet.
    - **Solo: no friend to break a grab.** The GDD has no rule for a crew of one. A held player alone (`grab.soloStruggleOn`) breaks any grab by holding Use for `grab.soloStruggle` (6 s): long enough to cost them, shorter than every grab window. A tuning flag, per §8's rule for spec gaps.
    - **Ambiguities and DESIGN-TODOs** (each is tuning, cited in `enemies.json`/`combat.json`):
      - The loudness meter has no numbers in the GDD. These were chosen so three players talking normally don't trip it, and three shouting, or a cannon every few seconds, do.
      - The Track Doll's "admires the cargo" cost is unstated (`cargoPerSecond`).
      - The Stoker's "firebox door left open at a stop" needs a door. It opens on every shovelful and swings shut `fireDoorShutSeconds` later with someone in the cab. Open `doorOpenSeconds` at a stop with the cab empty, the Stoker comes (a fireman shovelling at a stand isn't leaving it open).
      - The Gaunt's "talking holds it off" leaves open whose talking counts (Part Eleven Q9): `anyVoiceCounts`.
      - The Choir's ghosts fly `flySpeed` faster than the train: a swarm that couldn't catch a moving train would be no threat to one.
      - The demo roster (§21) swaps the Ribbits for the Grumbler "if the Foundry is one of the two demo facilities". The demo line has no Foundry, so the Ribbits stay.
      - The Soot Children's call is now a child's voice (`child-call`), not a mimic of a crewmate's. The v1.0 voice-mimic path in `VoiceChat` is unused and kept for now.
    - **Tells:** a new allocation in systems-spec A.4, reusing the retired enemies' bands. `AudioTests` still holds every tier-1 tell 6 dB over the bed for whoever has to hear it, on `Staging.Threats`' new layout: one of every enemy around the train, including a haunting doll in the cab, a Car Hugger on the rear, Ribbits, a Gaunt and a Grumbler on the ground, and two of the Choir's ghosts.
    - **Presentation:** the new creatures are greybox stand-ins built from the existing model kit (the husk for the Whistler, Tippy Toesie and the Grumbler, the hound's body squat and olive for a Ribbit, the Hollow's figure for the Gaunt and the Choir). Dedicated models are art-pass work. The Track Doll has its own since note 97, the Car Hugger since note 102 (it was the weight's heap).
    - **Bots** (`Heed.*`, the harness and solo crewmates): rescue anyone held (Use for what lets go, a swing for what doesn't); glance round and face Tippy Toesie; talk (a `Voice` byte), quietly to the Gaunt, and hush for the Choir; keep clear of a Whistler's gap after the whistle; put a swarmed car's lamp out; the gunner reloads, and fires until the meter's half gathered. The driver brakes for the Track Doll, and for a derailing Switchman it stops short, waits for the firebox door to shut, gets down and clubs it. At a stand the fireman only fires to keep the fire and gauge up (every shovelful opens the door). The driver (and a fireman) club a Stoker out of the firebox while they've health to spare. Walkers treat a swarmed lamp as trouble, like a fire: in, and the lamp out. **Car fires, retuned (T88):** the v1.0 numbers had a fire outgrow one extinguisher within 20 s, faster than anyone, bot or player, could get to it, so the fire grows slower and the spray is stronger and longer (DESIGN-TODO in `enemies.json`). **The Stoker's door, reread:** "left open" is open at a stop with the cab empty; a fireman shovelling at a stand had it open more than not, and a Stoker came through every stop. Harness frontier:7 at 4 bots now delivers with the crew home (net −775: the fires still take the cargo). **Not yet:** bots still lose cargo to fires they reach late, and don't pair up at stops.
    - **Verified:** `DemoRosterTests` (each of the demo five against its rule, grab rescue, the solo struggle), `EnemyTests` (hounds and the cannon, the Choir seizing only the exposed and dispersing when hushed), `DraggerTests`, `ClimberTests`, `CarFireTests`, `GunTests` (the reload, the meter), `ConflictSeedingTests`, `RosterTests`, `CargoTests`, `AudioTests`, `CreatureArtTests`; 595 tests green.

93. **Generated stops: yards, villages and their loot (level-design Part D, Part Z).** Every facility's zone, and a few village halts between them, holds a stop (on a generated line, its facilities' and settlements', note 96) generated from `tuning/stops.json` (`Sim/Stops/`). Its loot comes from the run's economy (`tuning/loot.json`).
    - **Seeded by hash (Z.1):** a stop's seed is `hash(route seed, stop index, tier)`, and each attempt's is `hash(stop seed, attempt)` (`StopSeed`, SplitMix64). One stop never shifts another; the same night is the same stops on every machine.
    - **Generate, measure, validate, reroll:** up to `maxAttempts` (8). Each attempt is scored for manoeuvre difficulty (P15, `StopGenerator.Measure`) and checked against Z.5's invariants (`StopChecks`). The first attempt that passes everything and lands in the tier's band is kept, or else the closest one. A 60-seed sweep (`dt site sweep`) lands 98–100% in band, all valid.
    - **Yards are nested spurs off the main line.** `RailLine` branches only come off the main line and face up-line, so a ladder, fan or split is several spurs, each at its own switch. The first toe leads to the outermost track, so none cross. Each track is an S-curve turnout (radius `turnoutRadius`, 60 m) out to its offset, a straight loading face, and for a fan a curve away. `BranchDefinition` carries them as segments; `StopGeneratorTests` pins the branch's world path to the layout's to 0.3 m.
    - **Deferred, as the engine stands:**
      - trailing points and loops (Z.6 answer 1: a north lead isn't a rule, and the engine can't build one yet);
      - power, derelict cars and grade as difficulty levers (Part D lists them; the sim has none of them at stops yet).
    - **Ambiguity: what a siding "holds" (P16).** A spur's capacity (`SpurDrill.Capacity`) is now its *standing length*: the shared loading face from the buffer stop back, engine included. It no longer counts the S-curve or an outer track's straight before the face. Counted over the whole spur, a long outer siding took the whole train and there was nothing to drill.
    - **Loot (P2, P12, P14):**
      - The layout says where: crane bays, crate stacks, the hero's strongroom, and village cupboards, cellars, haylofts and the like.
      - The economy says what: `StopLoot.Village` shares a budget (`villageBudget` × the tier's `perCar`) across a village's finds, by kind weight and an outlier bonus. `CratesIn` sets each stack's crates.
      - When the train first stops at a stop, its stacks come out as cargo, its strongroom as a heavy crate, and its finds as `BodyKind.Loot`.
      - A yard's extra gantries carry a casting per bay they reach (`Site.YardCranes`). The Crane record is keyed site × 16 + crane.
    - **Ambiguity: when a find pays.** A find put down inside any car and left still is *stowed*. It adds to `Run.Scavenged`, which `RunReport.Scavenged` reports and `Gross` includes on delivery, like the cargo it rode with. The Run record grew a field for it.
    - **The harness** doesn't pass loot tuning, so its nights are unchanged: bots don't scavenge yet.
    - **Art:**
      - `WorldArt.Stops` draws each stop in the lineside cells from its layout.
      - The terrain now runs out to 220 m, and its hills are levelled over a stop's zone, because the sim walks people out to the village at rail height.
      - A craned shed gets a roofless bay cut through it under the runway, so the gantry's legs and rails stand clear.
      - The HUD and the operator's view take the crane nearest you.
    - **Verified:**
      - `StopGeneratorTests` (29): invariants over 30 seeds a tier, deeper tiers harder, determinism, the route's branches matching the layout, halts on level straight track, finds within their kind and budget, a stowed find paying, and gantries over their faces.
      - `StopArtTests`: the ground under a stop is level, and every building is drawn in its cell.
      - Look at them with `dt site --route frontier:7 --stop 4` (the plan) and `dt screenshot --route frontier:7 --site --facility 2` (in game).

94. **Death, Holdouts and return (GDD v1.2 Appendix D). The Vigil is cut.** Systems spec C.2 is superseded: `Vigil.cs`, `vigil.json`, its record, its HUD, the Revived flag and the revive-at-the-gate rule are gone. Once the gate has opened, a Holdout is the only way back into a run (`Run/Holdouts.cs`, `tuning/holdouts.json` with D.13's numbers).
    - **Where they are** is level content, part of each stop's layout (level-design Part H, `stops.json` "holdouts").
    - **The queue (D.6).** The dead join it at the back as they die, and a mid-run joiner as *lobbied*: boarded with `DeathCause.Waiting`, so they spectate with no body. Defer only ever moves you down.
    - **States (D.5):**
      - **Assign.** From the approach board, the first eligible entry takes each Holdout at the site. You're never eligible where you last died, but you keep your place. A facility's second Holdout needs a session of five or more.
      - **Breach.** A living crew member holds Use at the door. Any interruption resets it to zero.
      - **Freed.** The player comes back inside the Holdout (at its middle: D.14 "no open-world spawns") on 80 health.
      - **Release.** The consist has left the zone moving away, with nobody living within 400 m. The player goes back to their place, and the Holdout can assign again if the train comes back.
    - **Ambiguity: the approach boards.** The Line Plan isn't in this repo. The 2 km and 1 km boards are distances before the zone; the whistle board is taken as 400 m (`assignHalt`, and `approach.halt` in stops.json).
    - **Ambiguity: a village-only stop is both "a halt" and "a dead town".** It gets one Holdout: the halt's lockup at `villageLockup` chance, else a shelter within the village's 80 m.
    - **Ambiguity: breach noise.** Smash and pry feed the loudness meter (note 92, App. C.7) as a fraction of a cannon round a second (`rounds`, `ChoirState.Loud`). Call Out never touches it.
    - **The repair kit's silent breach** is note 150's: a lock opens to the kit in hand, quietly, in 6 s.
    - **Controls.** A dead player's Use is Call Out (a shared 7 s cooldown per Holdout, only with someone living within 200 m) and their Throw is Defer. Live Mic, the spectator camera, the creature vote and commendations (D.7, D.10–D.12) aren't built.
    - **Bodies as loot (D.9).** `Bodies` makes one body per death (die twice, leave two), and counts deaths. A drop-out's body is marked, and carries no fee and no refund. `RunReport` gains Deaths, BodiesHome, CrewLossFees and BodyRefunds, and Net is after both. Fees are charged whenever Holdouts are on. A checkpoint keeps the spent Holdouts where it used to keep revivals.
    - **On the wire:** one Holdout record per Holdout (state, occupant, breach). A frontier night's snapshot grew from about 110 to 142 bytes, about 31 kbit/s down per client.
    - **In the world:** a prison car on its derelict siding, a signal box or water tower model with its door barricaded, a brick lamp room, or an iron cage behind a platform. Each lamp is on the corner the train sees first, and burns while the Holdout is occupied (`dt screenshot --lit` lights them all).
    - **Verified:**
      - `HoldoutTests` (12): assign, breach and free inside; a stopped breach starts over; eligibility; release and come back; the second Holdout's crew; defer; Call Out's cooldown and silence; smash noise; a lobbied joiner; two deaths, two bodies; no farming over random deaths and recoveries; drop-outs.
      - `HoldoutSiteTests` (6), and the stop checks in every `StopGeneratorTests` sweep.

95. **A yard's power and the grade out of it (level-design D.2, spec D.1).** Two of Part D's deferred levers.
    - **Power:**
      - Each yard rolls its power by tier (`stops.json` tiers "power": live at local, mostly dead in deep territory). A powerhouse is placed at its throat.
      - At low power the yard's cranes run at `lowSpeed` (0.5); dead, not at all. Holding Use at the powerhouse door for `restartSeconds` restarts it (`facilities.json` "power"), and it's machinery-loud.
      - The power replicates with the site (the Run record's per-facility fields grew from 5 to 7). The crane itself is only driven on the host.
    - **Grade:**
      - The route lays each facility's exit grade (by tier, up to 4%, from its own seed) on the first stretch past its zone, keeping at least 400 m of climb. Every other draw is left as it was, so only that stretch changes.
      - The stop is told the grade (`StopContext.ExitGrade`), and its score counts a hard pull from 2%.
    - **Verified:** `PowerTests` (4): the cranes run on the yard's power, the restart and its noise, and deeper tiers having worse power and steeper pulls, as the route lays them.
    - **Not done:** trailing points and derelict cars. Both need the train sim to change: a switch that faces down the line, and rakes standing on branches at the start of a night (level-design I.4).

96. **Stops on generated lines (level-design Part Z; linegen plan §11.1, §11.3).** The line generator is the default night, so its facilities and settlements get stops too (`LineGen/PlanStops.cs`, called from `PlanRoutes.ToRoute`).
    - **A facility on a spur gets its yard at its own junction.** The zone is placed so the yard's first switch is the plan's junction. That track becomes the plan's spur branch, so every edge the plan names keeps its branch index; the yard's other tracks are added after the plan's branches.
    - **Its ground is kept straight and level.** `LineBuilder` stretches a spur facility's departure lull (straight, level) to cover a stop's zone past the junction. The zone must then lie on straight, level main line, and the cut waiting before it on straight track, which may climb a little.
    - **Ambiguity: a village along the line before the yard.** That arrangement shifts the yard 150 m down its zone, back past the level holding track (up to 0.2%). Such a stop is drawn again, up to 4 times, from `hash(stop seed, n)`. A facility that still doesn't fit goes without a yard, keeping the plan's plain spur. No night in the test set does.
    - **Coaling towers and mine heads get no yard.** A coaling tower stands on the main line; a mine head's spur runs into its portal.
    - **Halts and dead towns get villages.** Each settlement on the main line that's clear of a yard's zone (by 100 m) and is straight and level gets a village stop over its stretch. Its halt is at the plan's platform, on the platform's side, and it's seeded from `hash(route seed, halt, S)`. The plan's own platform bays and random houses aren't drawn there; the village is.
    - **Flattened by pads.** Each stop adds a long pad (`stop:<S>`) to the route's copy of the plan, so the terrain on every machine is flat under it.
      - It's a box (`PlanPad.Box`), not a capsule. It ends square at the zone's ends, where the line may start to climb. It reaches each side only as far as the stop builds there, so a yard never flattens the sea on the other side (`WatersideTests`). It also leaves the rail's formation its own.
      - The departure lull this stretches changed the alignment of some nights. `frontier:7` lost the 418 m curve `LineGenTests.ACurveTakenAboveItsDerailSpeedDerails` used, so the test now takes the first frontier night with a curve the train can overspeed. A plan that already carries them (sent to a joiner, or saved) has them stripped and added again, so building its route twice gives the same stops.
    - **Art:** `WorldArt.Stops` draws a generated line's stops too; it had returned early for plan routes. The plan's woods keep off each stop's ground (`PlanScene.Clearings`), and its platform bays and town houses give way where a village took the settlement. `StopArtTests` runs on both generators' nights. `dt site --route` now reads the line generator's night, the one the game plays.
    - **Verified:** `PlanStopTests` (22) over four tiers checks:
      - every spur facility's yard at its own junction, its tracks as branches;
      - every stop on straight, level track;
      - villages at their settlements with the halt at the platform;
      - Holdouts on every tier;
      - one pad a stop;
      - a joiner's route from the plan matching the host's;
      - determinism.

97. **The Track Doll's own model (GDD v1.2 §21, App. A.2).** The Track Doll was the crew figure scaled up and whitened, with a lit box for a face. It is now a jointed porcelain doll (`tools/blender/track_doll.py`, baked by `tools/models/recipes/track_doll.py`, 6.1k triangles, SK_Human in a doll's proportions, 2.15 m).
    - **The look.** A Victorian bisque doll: the head a quarter of its height, glass eyes under painted lashes, a ringlet wig with a fringe, a lace collar, puffed sleeves, a bell skirt with a ragged hem, porcelain ball joints and moulded boots. What's wrong with it is kept small: a crack down through the left eye, a chip out of the right temple showing the hollow inside, soot run from the eyes, the left eye turned down and out.
    - **Jointed, not skinned.** Every piece is rigid on one bone (the skirt alone follows the thighs a little), so it moves at the joints and holds its poses. Its clips are keyed as holds that pop (GDD §31): stand (on the rail, nothing moves), admire (bent over the cargo to its right), giggle (hands over its mouth), tamper (fists on the controls), cower (cornered), hit.
    - **The tell.** App. A.2's telegraph is "white porcelain catches the forward lamp at ~200m", and the sim enters Telegraph only with the lamp on and the doll within that range. So the face and glass eyes draw under a material of their own (`track_doll_0.face`, the atlas's split), and `CreatureArt` gives it a light of its own in Telegraph: strong far off, which also thins the fog over it, easing to a sheen by 15 m. Unrevealed or aboard it's only porcelain. At 200 m it's a white pinprick on the rails (a 2 m figure is 7 px tall at 720p there), at 80 m a pale face with two dark eyes. `dt screenshot --threats --doll-at 200` stages it anywhere up the line.
    - **Facing.** A thing on the line is drawn facing down it, the way the train goes. The doll turns round to face the train; aboard, it keeps its car's frame.
    - **Aboard.** A haunting doll takes turns: 12 s admiring, then 5 s giggling. At the cab's controls (`Extra2`) it tampers; cornered (`Extra`) it cowers.
    - **The bake kit gained two things.** A mask may take the high copy's material (paint that goes on the face and not the hair) and return three masks in one bake. `Atlas.finish` takes a roughness map, so the glaze stays glossy (0.2) while the cloth and the chip's bisque go matte. Every other model's default is unchanged (0.75 all over).

98. **The first playtest's movement round (T90-T92).** Solo playtest feedback, all taken as asked:
    - **Jump.** `player.json` `jumpHeight` 0.8 m sets the take-off speed (`PlayerTuning.JumpVelocity`); the old derivation (a flat jump at roof run spanning exactly the 2.2 m `jumpGap`, 0.48 m high) is its floor. A roof-run jump now carries about 2.8 m.
    - **Landing.** Hitting the ground kills only above `landing.lethalAbove`, 16.5 m/s, three times run speed. Below that it's a knock rising from `rollDamage` (10) to `damageAtLethal` (80) with the speed over the ground. At the old 4 m/s every step off a moving train was a death. Spec B.3 is updated. Being pulled off by the Draggers (`PlayerMotor.PullOff`) still goes by `speedBands.jumpOffLethal` (4 m/s): being dragged off isn't a landing you chose.
    - **Into the cab from the train.** There wasn't a way: the tender was solid and full width. It now has a gangway down its left side at deck height (`train.json` `engine.tenderGangway`, 0.9 m), with a grated footplate off the coupler plate (a `RunningBoard` solid, footing only), leading into the back of the cab. A ladder up the tender's front beside the coal reaches the cab roof and its gun. `MovingFrame`-style tests walk both at 14 m/s (`PlayerMotorTests`), and `dt screenshot --view gangway` shows them.
    - **Ladders.** Walking straight into a ladder's foot (within 0.45 m, facing up it) takes hold without Use, and the climb carries you over the top as before. Hatch ladders inside the cab and cars don't, since people walk past them all the time working (the fireman bot walked up the cab's hatch).
    - **Uncoupling (T91).** Its own key (`Control.Uncouple`, Z; `PlayerActions.Uncouple` on the wire), held for `uncoupleSeconds` (3 s; 6 s under load), standing still and looking at least `uncoupleLookDownDegrees` (55°) below level (`CrewActions.Uncoupling`). A headset player grips with a hand reached down low. Use on the plate now only ever works a door. The stop crew's cutter looks down and holds it.
    - **Carrying (T92).** A carried crate, lamp or loot used to be drawn where the last snapshot put it, from the host's carry: it trailed the view and stepped at 30 Hz. The app now draws what you hold at your hands as this frame's eye sees them (`GreyboxScene.HeldHere`). It's presentation only; the host still decides where it is.
    - **Why the bots never needed a way into the cab.** The first aboard is spawned in the cab, and that's the driver bot. The harness also posts the gunner and the fireman to their stations at departure (a host-side placement, `Harness.PostGunner`/`PostFireman`). The walkers only ever use the roofs, end ladders, plates and end doors. So nothing a bot did went through the way into the cab, which is how its absence went unnoticed.

99. **The roof guns slide on rails (T93, playtest).** Every roof has a gun rail down its centreline (`CarShape.RoofRail`: the car's length less 0.4 m each end; on the engine, from over the cab's front back over the tender). A gun is state on the vehicle whose rail it's on: `GunState.Mounted`, `Z` along the rail and `Facing` (−1 forward, +1 back), replicated in the vehicle record with its ammunition. `CarShape.Gun` is now only where it stands at departure. `Guns.Mount` says where it is now.
    - **Pushing.** Stand at the gun, hold Use and walk: `Guns.Pushing` sets `PlayerFlags.Pushing`, which holds you to `player.json` `pushGun` (1.2 m/s). The motor moves the gun along its rail exactly as far as you moved along the car (`Guns.Slide`), on the host and in the pusher's own prediction alike (`NetcodeTests.AClientPushingAGunAlongTheRoofIsPredictedExactly`). A gun waiting on its reload isn't pushed: Use there reloads it, and walking stops a reload step.
    - **Over the coupling.** Past its rail's end the gun goes onto the next car's rail if the two are coupled and that car has no gun of its own; otherwise it stops at the end. So a gun on a car about to be cut can be saved by pushing it forward first, and one on a cut car stays with it.
    - **No mount solid.** The gun used to stand on a fixed pedestal solid. A gun that moves can't be part of its car's static collision, so it has none: you walk past it on the roof.
    - **Drawn** from the vehicle's state (`SceneArt.Car`, the greybox too), with the rail itself as a pair of iron flats on tie plates (`TrainKit.RoofRail`); `dt screenshot --view gun` looks at the guard van's.
    - **Bots** find the gun through `Guns.Mount` and don't push it yet.

100. **A night starts at the gate (the director's call, after playtesting).** The fortress yard is all at yard speed (2.5 m/s, plan §10, spec B.3). With the engine a train's length into it, the crawl to the gate took minutes before the run began. Now `PrototypeSession` and `SessionSetup.Build` stand the engine's front `departShortOfGateM` (8 m, `run.json`) short of the outer gate, the whole train still in the yard.
    - Everyone joining before it moves boards at the fortress (App. D.3). The run begins as it moves off.
    - The threshold is nearly level by construction (`fortress.maxThresholdGrade`), and a standing train doesn't creep through the gate: `DepartureTests` holds three seeds there for 90 s.
    - A resumed night still starts where it was saved. The harness keeps its own start (`StartDistance`), so its baselines don't move.
101. **A snapshot bigger than a datagram goes over a few ticks.** The transport never fragments, and it throws on anything over 1,200 bytes. With #96's stops and Holdouts, a joiner's first, full snapshot of a 6-car night outgrew that. So every hosted night, a solo one with a bot crew included, crashed as the crew joined. The tests' 4-car nights, on the loopback network (which doesn't enforce the limit), never saw it.
    - `HostSession.Budget` sends such a snapshot on a budget:
      - What the client already has, unchanged, costs nothing in a delta, and stays.
      - Changed and new records go in by priority (the players, the client's own first, then the train, then the rest) while they fit.
      - A changed record that doesn't fit goes as the client's old copy, so it isn't a removal. A new one waits for the next snapshot.
      - What's recorded as sent is exactly what the client holds, so the next delta is against that.
    - No protocol change. A frontier:7 6-car night with 3 bots budgets its first snapshot (1,192 bytes) and nothing after.
    - `NetcodeTests.AWorldBiggerThanADatagramReachesAJoinerOverAFewSnapshots` checks it: 150 crates aboard, every snapshot within a datagram, and the joiner ends with every one.
    - **Watch:** bots are UDP clients of their own host, stepped by the game's loop. A stall of more than the transport's 8 s timeout drops them (as a software renderer's first frame does; `--greybox` doesn't).
102. **The Car Hugger's own model (GDD v1.2 §21, App. A.3).** The Car Hugger was the Weight's heap standing in. It is now a creature of its own (`tools/blender/car_hugger.py`, baked by `tools/models/recipes/car_hugger.py`, 7.8k triangles, a chain rig of 25 bones). (It was 8.4k when it landed; restoring the crew's overhauled body put the headset's trackside view 1.2k over its 1.5M a frame, and 28 sides round the body and four round each tooth brought it back under.)
    - **The look.** A bloated, eyeless leech-maggot four metres long, hugging the guard van's rear end. Its head is down on the rear platform, its bulk sags off the back of it and its tail drags on the track (it's what drags the train). Four long, thin arms wrap round the car's rear corners, the upper hands' fingers hooked over the roof's edge and the lower ones dug into the sill. The mouth is a lamprey's: a ring of lips, a funnel of red-black gum, and three rings of teeth that turn against each other as it eats (the grinding from the rear that's its tell). Rusted plate it has eaten shows through its hide, and a wheel's flange stands out of its flank.
    - **The hide.** Waxy grey-violet, marbled like a drowned body's, bruised and veined, darker on the back and paler under the belly, caked with ballast dirt low down. It's wet, at roughness 0.3. Its colour is all painted from noise rather than worn from the library's flesh: that texture repeats every half metre, and over a body this size its blotches tiled into a camouflage pattern at any scale. Behind the chest it's ringed, with a crease every 0.3–0.42 m, some deep and some barely there; its outline needs that, because a smooth four-metre body read as a torpedo. The hands, lips and gums are small enough to wear the library's flesh.
    - **The mouth is on the door.** The sim's latch point is the car's centre line (`Local` (0, 1.0, HalfLength + 0.4)), but the end door is on the doors' line (`interior.doorX`, −0.45). The head swings over so the mouth fills the doorway, and the body eases back to the middle by the belly. So opening the guard van's rear door is opening onto its mouth, which is where App. A.3's SWALLOW takes whoever steps out; the sim's reach (1.6 m) covers the doorway either way. `Staging` now latches the hugger at the sim's own point, where it used to be half a metre off.
    - **Clips.** lurk (lying flat, breathing, a hand opening), latch (up off the stones with the arms flung wide and slapped on, holding by half a second, per App. A.1's window), feed (three uneven chews heaving into the door, the teeth grinding, loop), swallow (the mouth dilated wide, gulping, loop), release (the fingers out of the steel, sliding off), hit (not yet played, like the doll's). `CreatureArt` plays latch through the start of the telegraph and feed after it. Grab and Punish play swallow, and the break-off plays release.
    - **Lurking is seen (ambiguity, §8).** The stand-in drew nothing while dormant ("under the bank until it stirs"), and `CreatureArtTests` pinned that. App. A.3's LURK is "beside the track on low ground", so the model draws there, sunk to its shoulders and muddied (colour × 0.3). From the cab it's a dark mound by the rail that could be anything, and the grinding stays the tell. `dt screenshot --threats --lurk-at m` stages one; `--doors-open` opens every door on the train (to look out of the rear door into its mouth).
    - `weight.glb` is no longer drawn by anything: the Weight was cut in GDD v1.2 and the Car Hugger replaced it. It is left in the model list and its tests until the retired enemies' assets are cleared out together.
103. **A brake left on stays on (frontier:7's derailment).** The brake is a held control: the host clears it each tick and whoever's holding it re-applies it. With nobody at the controls it used to be cleared too, so a driver who got down with it on (to club a derailing Switchman, to set a switch back alone) left the train free to roll, and on the generated lines' grades out of yards it did: frontier:7 rolled 77 m down into the Switchman's points and derailed with the whole crew. Now the host clears it only when someone can drive (`HostSession.Step`), so it stays where it was left, as the throttle does and as a predicting client already assumed. The driver bot holds the brake as it gets down and while it's out. `NetcodeTests.ABrakeLeftOnStaysOnWithNobodyAtTheControls`.

104. **The dead watch the living (GDD App. D.10), on #96's Holdouts.** Main's Holdouts had no spectator: dead, the camera stayed at your own eyes. Now, dead or waiting to board, you see through a living crewmate's eyes.
    - **Whom.** The first of the living (lowest id) when you die; on to the next when they die; your own eyes when nobody's left, and the HUD says so. Fire or a step right goes to the next, a step left back, one step per press. Living crew only: no free camera. Right mouse stays "let someone else go first" and E stays Call Out, as #96 has them.
    - **Presentation is the client's.** `NetPlaySession` picks whom and draws their eyes (`Eyes.From` on the interpolated remote), hides their figure round the camera, and puts the listener's space and shelter at theirs (`IPlaySession.Viewpoint`), so you hear what they hear. In a headset the room follows them.
    - **The host is told, as intent.** `PlayerIntent.Watch` (the crewmate's id, 0 for nobody) rides the notch byte's spare top bit and one more byte, so only the dead pay for it. It moves no player or train state. `HostSession.EarsOf` centres interest (enemies, loose bodies) and living voices on the watched player: 300 m from your own body, you still get what's round them. A Follower's hidden from whoever watches its carrier too.
    - **Checked by** `NetcodeTests.ADeadPlayerIsSentWhatsAroundTheCrewmateTheyWatch`, `SpectatorTests`, and `dt screenshot --hud --spectating` (a hosted night, a joiner dead in the yard, watching the host in the cab).
    - Still to port from the first App. D branch: the Live Mic, the creature vote, the incident report with commendations and bookmarks.
105. **The second playtest round (T94–T98).**
    - **Steam drives the train (T97).** No regulator: `boiler.json` `steamDrive`. The pressure sets the speed the engine can make (`Boiler.SteamSpeed`: none at the power floor, top speed at the safety valve), and it pulls at full effort until within `driveSpeedBand` of it (`TrainOnLine.Step`). The cylinders, and the exhaust's draw on the fire, go with the effort of pulling away or with speed, whichever is more, full from `fullDemandAt` of top speed, so running flat out is the spec's old "full throttle" and B.6's tables hold (`BoilerTests`). Held on its brake, a standing engine isn't working. Coal is more speed; the vent (12/s, moved out to the left running board by the smokebox) is less; the brakes are doubled (spec B.5, `SpecTableTests`). With the steam up the engine pulls against the brake, so the quickest stop is brake and vent, which is two people. Without a boiler (the tests' bare trains) the old regulator path is untouched.
    - **Standing on the brake.** A standing engine with steam would pull away, so a train that stops on its brake stands on it until the driver lets it off (`CabControls.Clears`: a notch up, the old "regulator open" key, now RELEASE BRAKE). Nights start at the gate on it. The HUD reads "STEAM n KM/H" where the regulator was. The driver bot fires to what makes a little over its cruise (`Boiler.PressureFor`), only keeps the fire just off low when the pressure's well over that, and gets down (to sand, club, set a switch) with the brake held.
    - **A ladder key (T94).** `PlayerActions.Ladder` (F): the nearest ladder in reach, whichever way you face; the HUD offers it. The old regulator-close key moves to Y. `Hud.Bound` rewrites prompt keys in one pass (it had replaced a rebound key twice).
    - **The route strip (T95)** across the top of the HUD: how far is left, where the facilities (amber) and villages are, the next stop's distance.
    - **Bots don't leave anyone (T96).** The driver bot stops for a crewmate left on the ground behind the train, sets back to them at yard speed (`StopDriver.Toward`), waits while they climb on, and goes on (`LeftBehindTests`). A lit Holdout (GDD App. D.5) ahead brings it to a stand alongside; the nearest walker (`CrewCalls.ClaimBreach`) gets down and breaches the door, and the driver waits while anyone's on their way to one.
    - **The run map (T98)**, pinned to the cab's front wall left of the firebox: the line as a dotted inked track, the stops, the train in red (`GreyboxScene.CabMap`); `dt screenshot --view cab`.

106. **Roof hatches for the crane (T99).** A cargo car's roof has a hatch between the walls behind its side doors (`train.json` `interior.hatchZ`/`hatchLength`): its lid is a `PartKind.Hatch` solid, open on bit 4 of `DoorsOpen` (`CarShape.HatchBit`), after the four doors, so it replicates with them. Open, the lid isn't there for the motor, the bodies or a shot (`Solid.Present`), and it's drawn as two leaves swung up on their hinges at the sides. It's held open or shut from the roof at its handle, like a door. With it open the crane's hook goes down inside (`Crane.Under`, `Crane.Hatch`): a casting sets down on the floor, the load or a casting already in, and loads the car as on the roof. One that won't clear the opening, or would stand up through the roof line once set down, doesn't go in: the hook stops over the roof and won't let go there. The lid won't shut on a casting hanging in the opening (`TrainOnLine.HatchBlocked`, which the world answers from its cranes). Shut, the crane lashes castings on the roof as before, so the crane bots' routine is unchanged. The warming bot ignores an open hatch: it's worked from the roof. `HatchTests`; `dt screenshot --view hatch [--doors-open]`.

107. **The fortress is a town (T100).** The playtest: "fort villages should look like protected villages btw with people around (not a ton because its night)". The walls kept nothing: the yard ran between them to the gate, and the forest grew inside. Now a lit fortress has houses down both sides between the line and its walls, fronts to the line, one every 15 m with gaps, none on the home platform (`WorldArt.FortVillage`, `TownKit.LivedHouse`: whole, a lamp behind a window or two, and now and then its light on the street). The lineside keeps its trees and rocks out from inside the walls (`WorldArt.Walls`). A watchman stands either side inside the gate, and in about one house in four someone is out front, facing the line (`WorldArt.FortFolk`, drawn as the crew's model in drab, idling). They're presentation only: no sim state, so nothing to replicate. A silent terminus (linegen plan §22.4) stays as it was: empty walls. `dt screenshot --route frontier:7 --at 590 --cam 560,-3.5,1.8 --target 500,-6,1.2`.

108. **The driver sees the line (T101).** The playtest: "there also needs to be a way for the driver to see the track ahead and the front of the train on their own ... players should clearly see the firebox, map, gauges, speed, brake, and vent".
    - **What was wrong.** The spectacle plate's two windows started above a standing eye, so from the footplate the driver looked at iron. The cab view stood at the back of the cab.
    - **The windows.** Now a tall open window either side of the boiler runs from the waist almost to the roof (`TrainKit.SpectacleWindow`).
    - **The boiler.** It's narrowed from 1.9 m to 1.6 m (`train.json` `boilerHalfWidth` 0.8). From the farthest out a player stands at the cab side, the near rail is in sight from 30 m, the middle of the track from 40 m, and the running board all the way to the buffer beam. At 1.9 m the boiler's flank hid the track to 100 m. The boiler's top, the running boards and the cab are the same size, so the roof walk and the traverses keep their lengths.
    - **The map** moves from the left of the backhead to the plate over the boiler, between the windows, turned so the night runs left to right along it (`TrainKit.MapPlate`).
    - **The cab's fittings.**
        - The dials are bigger (`TrainKit.GaugeRadius`), under a gauge lamp in the cab roof.
        - The brake has a red grip.
        - The blow-off stands on a standpipe on the left running board with a red handwheel and a marker lamp, so the fireman's side sees it.
    - **Steam you can see.** Venting now shows: the boiler's `Vented` (the vent open on the last step) replicates in the boiler record's flags. It and the safety valve lifting each blow steam (`Effects.Train`).
    - **Checks.** `CabSightTests` casts the sightlines against the engine's solids and the window openings. `dt screenshot --view cab|fireman [--venting]`.
109. **The car eaten away, and the Car Hugger's and the Track Doll's motion (GDD v1.2 App. A.2, A.3; §31).**
    - **What's eaten is remembered.** App. A.3's FEED took the car's `Integrity` and forgot it: a half-eaten car looked like a dented one, and once the thing was killed nothing showed it had been there. Now the same loss is also counted in `Vehicle.Eaten`, replicated with the car (its vehicle record's 16th field) and restored with the train state. So a car's eaten fraction is `Eaten / (Eaten + Integrity)`: 1 exactly when the sim drops it. Its scars and torn plate (`DamageKit`, `look.json` "damage") read the rest of the loss only (`Integrity + Eaten`).
    - **The bite** (`look.json` "bite", `Art/BiteKit`, `Shaders/bite.glsl`). A ragged frontier across the car's own space moves forward from its rear end as it eats. Behind it the car isn't drawn and casts no shadow: a per-instance `MeshInstance.Bite`, cut in the fragment shaders (the push constants grow to 128 bytes, the size every device has). Along it the car is gnawed: raw and dark, wet, scored with tooth grooves.
      - **Its shape.** Deepest down the middle, where the mouth works; least at the side walls its hands hold (at most `sideLead` ahead of its head); never below `floor`, so the underframe and trucks stay and the car rolls until it's dropped. Eaten through, the rear `depth` (45%) of the body is gone, and the rear platform goes over the first `platform` of the eating.
      - **It moves in bites of 12 cm** (`Bite.Step`). Each bite is a mesh of torn edge laid along the same frontier: the frontier is sines, not noise, so C# and GLSL agree. The edge is boards snapped off in jags (plate torn back and curling, on a steel car), roof sheets bent up or hanging into the hole, broken floorboard ends, posts left standing and gnawed down, strings of slaver from the roof's edge. At the sim's rate it's a bite every five seconds or so.
      - **What goes with the wall.** An end door it's eaten past is gone, a lamp whose ceiling it's eaten goes out, and so does the tail lamp. The gun whose roof it's eaten falls in: it's drawn on the floor, nose up, and the sim still has it.
      - **Collision isn't eaten (a known gap).** The car's walls and floor are its `CarShape`, static per kind, so the crew can't walk out through the hole; its head fills most of it.
    - **It goes in as it eats.** Its head pushes in through the end as far as it's eaten, up to `advance` (1.4 m): its model moves forward in its car's frame. Its hands follow the side walls' torn edge: each arm reaches on (two-bone IK on the posed clip, `CreatureArt`) to where it held, that much further along the car.
    - **The Hugger's rings move.** It has a ring bone for each of its eight segments, leaf bones off the spine carrying the hide's weights (33 bones now). So swells run along it:
      - **feed** (3.2 s): six uneven chews that pop and ease, each sending a gulp down it ring by ring, with slow breathing the other way, and two hands in turn letting go and slapping back on;
      - **swallow**: the lump of whoever it has, running from head to tail;
      - **latch**: the rings clench as it hauls itself up;
      - **hit**: a flinch that pinches down it;
      - **lurk**: breathing.
      The motion was doubled after the first look, because at game distance it didn't read.
    - **The doll's head watches you.**
      - On its socket it turns to whoever's looking, in 30° clicks: it holds, then jumps round as you move, as far as it takes. From behind it you meet its face.
      - This is the viewer's own eye, drawn per client, not sim state. Each player sees it looking at them.
      - It watches standing and giggling. Bent over the cargo, it looks round only when you're within 8 m. At the controls it glances back now and then. Cowering, never.
      - On the rail with the train within 40 m it beckons: a hand out in front, the fingers curling in, a pop at a time.
      - Standing, it cocks its head once in eight seconds and a finger lifts. Admiring, it strokes the load.
    - **Verifiable headless.**
      - `dt screenshot --eaten f` (the rear car that much eaten) and `--later s` (every staged enemy further into its clip).
      - `dt art clip <creature> <clip>`: a lit contact sheet of a clip's frames.
      - `BiteTests` pins the frontier. `DemoRosterTests` pins that what's eaten is counted and reaches a client.
110. **Tippy Toesie, and the one doorway (GDD v1.2 §21, App. A.5; §26.5).** It was the husk shrunk to child height. Now it's a thing of its own (`tools/blender/tippy_toesie.py`, baked by `tools/models/recipes/tippy_toesie.py`, 4,404 triangles, 27 bones), and the doorway it has to duck through is now one standard across the game.
    - **The design.** A starved thing over two metres tall, on the points of its toes, as a dancer stands en pointe. Its feet have grown into bony spikes with black tips, and its heels never come down, so all it makes is App. A.5's faint tiptoeing. It was a child once (§26.5): a child's slip hangs on it, off one shoulder, filthy at the hem.
    - **The face.** It has no mouth. The skin has grown over where the mouth was, a seam puckered in and drawn up in creases all round like a purse string pulled tight. Its eyes are small, black and wet, deep in bruised sockets. Its hands are the worst of it: broad palms and fingers 0.3 m long, made for covering a mouth from behind.
    - **The skin.** Grey-white as old plaster and thin as paper, with veins showing through and the ribs, collarbones and knuckles standing under it. The colour is all painted in the bake, over a flat ground: the library's flesh is a living thing's pink. Its engine wear is 0.1, since the shader's rust grime warmed the plaster to tan.
    - **One skin per limb.** Each arm and leg is one tube over both of its bones, blended across the joint. The first cut had separate pieces with a blob at each joint, and it read as a jointed wooden mannequin.
    - **Clips.** In each, it moves, then it's still: too still (§31).
      - **stalk:** a high step placed with the point, then a long hold. One finger is to where its mouth should be; the other hand drifts out towards the victim.
      - **wait:** dead still, but the head tips over all at once, and once the fingers ripple.
      - **flee:** low and fast on its points.
      - **smother:** bent double over the victim, its head laid beside theirs, rocking them slowly as you'd rock a child to sleep.
      - **hit.**
    - **In the engine** (`CreatureArt`, its target passed in from `GreyboxScene` as `CreatureArt.Prey`):
      - **Facing.** It faces whoever it's after: the crewmate whose id is its `Extra`.
      - **On them (GRAB).** It's drawn 0.42 m behind their heels, facing the way they face. Its right hand reaches their mouth by two-bone IK, and its left reaches their shoulder.
      - **Hidden.** The sim's DORMANT draws nothing.
    - **Seen (ambiguity, §8).** The sim's `Flee` goes through BREAK OFF to DORMANT in the same tick, so it's simply gone, and the flee clip never had a phase to play in. App. A.5 says "runs if you see it coming", so the client shows it scuttling off from where it stood for 0.5 s at 4 m/s, then nothing. This is presentation only; the sim is unchanged. A Tippy that's never been placed (`Local` default) shows nothing.
    - **It's taller than the train was built for.** It stands 2.45 m on its points and 2.34 m stalking. The doorway is 2.1 m (below), and a car is 2.75 m from floor to roof.
      - **Indoors** (a car's interior or the cab, `CreatureArt.Room`) it stoops. `stoop` (it waits bowed, head laid over on its side as if listening to the roof) and `stalk_stoop` (the stalk bowed lower, knees bent through every step) are 2.26 m and 2.19 m.
      - **Under a lintel** it ducks. `duck` folds it double, 1.83 m: knees deep, back flat, head turned on its side to go under first, hands spread on the jambs either side, drawing it through.
      - **Lintels are found from the car's own solids**: something thin with its underside 1.5–2.6 m over the feet, within a stride. So every door on the train counts, the cab's included, without a list. With nobody to face, it faces out through the door.
      - `TippyToesieTests` holds each clip's skinned height to the room it's drawn in.
    - **One doorway (the standard).** `train.json` `geometry.doorway`: 2.1 m tall (0.3 m over a 1.8 m crewmate), 0.9 m wide. Big doors (sheds, barns, churches) are all `bayHeight` 3.5 m.
      - **The train is built to it.** The cars' end doors and side doors (the side doors stay 1.8 m wide, for crates), the guard van's rear door, and now the cab's two doorways. Those were open to the cab roof (2.8 m) and drawn 2.48 m under the visor; they now have the lintel, in collision and in the art.
      - **Every door the art draws reads it** (`Look.Doorway`, `Kit.Doorway`, `Kit.DoorHeight`). Buildings were 1.9 to 2.2 m (people) and 3.0 to 5.0 m (big doors): the stop sheds and works houses, the facility shed and its leaf, the holdouts' barricades and the prison car's door, the towns' house, church and windmill, and the Nova Scotia saltbox, barn, church, fish shed and lighthouse.
      - A works house too low for a big door gets a person's door. The outbuilding's walls were 2.8 m with a 4.25 m door through the eaves; they now stand the doorway and a header over the sill.
      - Building doors are still drawn only. Only the train's doors have collision.
      - `InteriorTests.EveryDoorwayIsTheStandardDoorway` pins the train. Widths are unchanged: a cab doorway's clear width is still 0.75 m, the standard's 0.9 less its rear pillar.
    - **Headless.**
      - `dt screenshot --view crew --threats --crew --tippy behind` puts it behind crewmate 1 on car 2's roof (the plain `--threats` spot is AudioTests' and stays). `--tippy grab` stages the smother; `--tippy in --view inside` has it stalking down car 2's aisle; `--tippy door --view door --doors-open` has it ducking out of car 2's rear door, seen from the car behind.
      - `crew`, `crewside`, `inside` and `door` are views left out of `Views.Names`, so the perf budgets don't walk them.
      - `dt art clip tippy_toesie <clip>` renders each clip as a contact sheet.
      - `CreatureArtTests` pins its budget and clips. `TippyToesieTests` pins its fit, its room reading, that it's hidden while dormant, and where it stands on its victim.
111. **The Whistler's model (GDD v1.2 §21, App. A.4).** It was the husk crouched small. Now it's its own (`tools/blender/whistler.py`, baked by `tools/models/recipes/whistler.py`, 4,240 triangles, 27 bones).
    - **The design.** A long thing folded small. Stood up it's over two metres, all limb, but it lives crammed under the bridge plate in a coupling gap, soot-black and slick with the couplers' grease.
      - **Its face** is the one pale thing on it: a mask of grey-white skin over the whole long skull. The eyes are grown over, healed in puckered rings like stitching, and the mouth is drawn out into a creased tube, pursed for ever to whistle.
      - **Its right forearm** is a third as long again, for the cord.
      - **A loop of rusted coupling chain** has grown into its waist, the skin sore round it.
    - **Clips.**
      - **fold:** squatting as low as it goes, arms round its shins, its chin on its knees and the face looking out over them; it breathes.
      - **whistle:** up out of the gap on its toes, all its length, the long arm hooked over the cord 3 m up, yanking twice.
      - **watch:** a crouch at the gap's mouth on toes and knuckles, the head turning in jerks.
      - **run:** on all fours, too fast.
      - **hit**.
    - **In the engine.**
      - It stands on the rail: the sim's gap point is 0.6 m over it, so the draw drops it by its `Local.Y`.
      - **In its gap it faces whoever's looking** (the eye, as the Track Doll's head does): the one who checks the gap finds the pale face turned up to them. The brief asks that it be spottable by a player who checks. The stand-in faced down the gap and was a dark lump.
      - Carrying someone (GRAB), it runs away from the train.
    - **The foot planter moves the body after the IK.** `rig.bake`'s planter drops the whole body to the floor once a pose is made, so a hand IK'd to an absolute height (on a shin, on the ground) ended up 0.7 m under the floor. The Whistler's targets are taken from the pose instead: `knee(pose)`, `ground(pose)`. That applies to any crouching creature.
    - **Headless.**
      - `dt screenshot --threats --whistler fold|whistle|watch --view gapside --car 3`. `gapside` is a view from the ballast beside a gap, looking in under the plate; like `crew`, it's left off `Views.Names`.
      - `WhistlerTests`: folded it fits under the plate, whistling it rises over the head of anyone on it, its feet are on the rail, and it faces the eye.

112. **The crew walk aboard (T102).** The first playtest asked "how are bots doing test runs if they cannot traverse into all the car positions they need". The harness used to put the gunner on the guard gun and the fireman in the cab half a second in.
    - **Boarding on foot.** On a night, everyone but the first to join now boards standing on the ballast beside the train at the gate (`HarnessOptions.WalkAboard`, on by default; `HostSession.BoardAt`), and walks and climbs to their post:
        - roof walkers up a car's ladder;
        - the gunner along the roofs to the guard gun;
        - the driver and fireman up the cab steps (`ConductorBot`, using the stop crew's `SetBackAlone`, and on up the steps once on them).
    - **All aboard.** The driver waits on the brake at the gate while anyone's still on the ballast or a ladder, up to 120 s.
    - **The report.** The harness gives each bot's seconds to its post (`HarnessReport.Posts`, −1 for never). On frontier:7 with eight bots: walkers 5 s, fireman 26 s, gunner 55 s.
    - **What it caught.** The fireman fell off the cab steps: once it was on the ladder nothing kept it climbing. `WalkAboardTests`.

113. **Saving the gun (T103).** T93's playtest asked for guns "moveable along top rails ... so a cannon can be saved before decoupling a car".
    - **The gunner saves it.** A car the Car Hugger holds is a car lost (App. A.3: cut loose or eaten through, it goes with it). So the gunner, on that car's roof with the gun still on it, pushes the gun up the rail onto the car ahead (`GunnerBot.SaveGun`). Then it goes on as before.
    - **What it caught.** Pushing a gun is Use held while walking, which is also how you grab a ladder. The guard van's hatch ladder comes up through the roof on the gun's rail, so pushing past it took the pusher down inside the van, player or bot. While pushing, the motor no longer takes ladders by Use or by walking into them; the ladder key still does. `GunSaveTests`.

114. **Graphics settings (T83).** Settings now has a display section: fullscreen, resolution (720p to 1440p), render scale (½, ¾, full) and vsync. Like the key bindings (T80), they're saved with the rest of the settings.
    - **What the game draws at.** The scene is drawn at the resolution times the render scale (rounded to even), then scaled up to the window. The HUD keeps its 480x270 canvas. Windowed, the resolution is also the window's size. Fullscreen, the window fills the monitor and the scene is scaled to it. So render scale is the one knob for a slow GPU, and resolution picks how sharp the picture is.
    - **Changes take straight away.** The app checks before each frame. The window goes fullscreen or resizes. The swapchain is made again with vsync on or off. When the size it draws at changes, the renderer is made again and dressed again: render targets are sized at creation, and remaking costs a moment, not a restart.
    - **What's left alone.** `--internal` still pins the size, for captures and `dt perf`. In a headset the headset sets the size and the pace, and these settings don't apply. `FrontEndTests.TheDisplaySettingsAreSavedAndSayWhatTheGameDrawsAt`; headless, the app run with a saved 1600x900 at ½ draws at 800x450.

115. **Nobody at the controls (T105).** frontier:11 derailed at 23.8 km with the whole crew aboard. The driver was out on the running board sanding greased rail (T72).
    - **Why that left the train unheld.** Sanding is "out with the brake held". But a moving train's brake is cleared every tick and holds only while someone in the cab holds it (`CabControls.Clears`, T97). So once the driver stepped out, the brake was off, sanded wheels gripped, and steam pulled the train up to whatever speed it made. Nobody was watching the road: it went onto the Sleepers at 11.2 m/s, just over their 11.1.
    - **The fix.** The fireman minds the controls while the driver's out (`CrewCalls.DriverAway`). It holds the train no faster than the lamp's cruise, the line's authority and the boards ahead (`MindingCruise`). It brakes for the Sleepers in the lamp as the driver does (`WatchTheRoad`). Stopping the train before stepping out doesn't work: stood on grease, it never leaves the grease. `SandTests.WhileTheDriversOutSandingTheFiremanHoldsTheTrainToTheBoardsAndWatchesTheRoad`: without the fix, 15.8 m/s over a 7 m/s board, and onto the Sleepers at 20.4 m/s.
    - **Derails say why.** Every derail now records its cause in `World.DerailCause`: a curve or bridge (track rules), a board run too fast, the Sleepers, or the Switchman. The harness reports it as `threats.derailCause`. Before, only the track rules set it, and nothing read it.

116. **The Ribbits' model (GDD v1.2 §21, App. A.6).** They were the Cinder Hound squashed and coloured olive. Now they're their own (`tools/blender/ribbit.py`, baked by `tools/models/recipes/ribbit.py`, 3,662 triangles, 28 bones), on a rig of their own (SK_Ribbit: a throat-sac bone to scale, a jaw, two tongue bones, two-bone ears, rabbit hind legs).
    - **The design.** A toad sat up on a rabbit's haunches, the size of a big dog in the model, drawn at 1.4× ("giant": its head at a crewmate's waist). It's hairless, its skin a pallid pinkish grey and wrinkled like a hairless rat's, with small warts down a bony back and a slack belly.
      - **Ears:** long rabbit's ears with no fur, veined, hanging limp down its sides.
      - **Eyes:** milky, with no pupils, under heavy lids.
      - **Mouth:** right across its head, with a crooked, gapped row of flat human teeth always showing.
      - **Throat:** the sac under the jaw is the tell.
    - **Clips.**
      - **sit:** dead still but for the throat's slow pulse; an ear twitches.
      - **hop:** half a second of leap and half a second sat, matching the sim. `Ribbit.Hop` leaps on the half seconds where `PhaseSeconds * 2 + Id` is even, so the draw offsets the clip by half a second for odd ids.
      - **swell** (the telegraph): up tall, the head raised, the sac blown up pale and pulsing under it.
      - **tongue:** the mouth gaping, braced, reeling.
      - **hit**.
      - Scaling the sac grows it from its bone's head, so that sits at the top of the sac, under the jaw. First placed at its middle, it swelled up over the whole head.
    - **The tongue is the engine's.** On its catch (COMMIT, GRAB, PUNISH) `CreatureArt.Tongue` draws it from the mouth (the `tongue_02` bone, posed) to the catch's chest, 1.15 m up. It's dark red and wet, thins to its tip, sags and twitches taut as the pack reels. Each Ribbit faces who its pack is after: `GreyboxScene` passes the crewmate whose id is its `Extra`, as for Tippy Toesie.
    - **First cut.** It read as a cartoon toad: cream with spots, googly eyes, red lips. The pallid wrinkled skin, the milky lidded eyes and the always-bared teeth are what made it unpleasant.
    - **Headless.**
      - `dt screenshot --threats --crew --ribbits hop|swell|tongue --view pack`. The mode puts the staged pack on crewmate 4 (`Staging.Lone`, alone on the ground). `pack` looks over that crewmate's shoulder; it's left off `Views.Names`.
      - `RibbitTests`: the hop leaps when the sim moves it and sits when it doesn't, nothing goes under the ground, and the tongue reaches its catch's chest.

117. **The Choir's model (GDD v1.2 §21, App. A.7).** Its ghosts were the retired Hollow shrunk pale. Now they're choir children gone wrong (`tools/blender/choir.py`, baked by `tools/models/recipes/choir.py`, 1,588 triangles, 27 bones: light, because several fly at once).
    - **The design.** Child-sized, in what's left of a chorister's surplice: grey-black with filth, a ruff gone limp at the throat, and below the waist no legs, only the surplice hanging in long ragged strips. The legs are inside them and swing them, so the strips stream behind as it flies.
      - **Skin:** a dead blue-grey, veined dark.
      - **Face:** the eyes black hollows weeping tar, and the jaw dropped far wider than a jaw can go, in a child's singing O, held.
      - **Hands:** long thin fingers.
      - **No light of its own** but a faint cold point light the colour of its skin (`ChoirCold` 0.25), so it's seen at night without glowing (§26: corruption palette, not neon). The stand-in carried a bright glow.
    - **Clips.**
      - **drift:** circling slow and bobbing, the head tipped back singing.
      - **swoop:** leant into the flight, reaching, the strips streaming.
      - **seize:** over its catch's head, arms wrapped round it, the mouth at their ear, shuddering.
      - **besiege:** at a shut door with nobody to take, fists hammering in uneven bursts.
      - **hit**.
      - The draw picks swoop or besiege from its target (COMMIT with `Extra` ≥ 0, or not). Swooping or seizing, it faces its target; `GreyboxScene` passes them, as for the Ribbits.
    - **First cut.** A tan surplice with stiff boxy sleeves, and the strips clean white candles. The surplice is now dark and filthy, the sleeves hang, and the strips are stained black from the hem down.
    - **Headless.**
      - `dt screenshot --threats --view choir`: the two staged ghosts over the guard van, from off its side, against the sky. Like the other close views, `choir` is left off `Views.Names`.
      - `CreatureArtTests` pins its budget and clips. It flies, so it's exempt from the feet-on-the-floor pivot check: its strips hang below its origin.

118. **The Gaunt's model (GDD v1.2 §21, App. A.6).** It was the retired Hollow stretched and darkened. Now it's its own (`tools/blender/gaunt.py`, baked by `tools/models/recipes/gaunt.py`, 5,494 triangles, 27 bones, SK_Human stretched).
    - **The design.** The bones of a starved giant, near three metres stood up (it never stands up), in a skin far too big for them.
      - **Body:** legs and arms like poles, the knees and elbows knots on them, the ribs a cage, the waist gone to nothing.
      - **Skin:** ash-grey, dry and cracked, with mould in the cracks. It hangs off it: an empty sack of belly over its hips, webs under its arms, creases slumped across its back, folds round its knees and ankles like fallen stockings.
      - **Face:** long, mournful and too human, its skin slid down. Heavy upper lids droop to the outer corners over big wet black eyes. The lower lids are dragged down, so a red crescent shows under each eye. The jowls hang either side of a small round open mouth, pursed as if to say "oh".
      - **Hands:** huge, the fingers as long as a forearm, the nails horn.
    - **Clips.**
      - **sleep** (Dormant): the heap. Squatted on its heels, its face in its knees, its arms wrapped over its head, its back rising and falling; once, the fingers of a hand open on its skull.
      - **stir** (Alert): the head comes up out of the heap, slowly, to look over its knees, and tips over on its side. It plays once and holds.
      - **follow:** a stalking lope on bent knees, the head carried low out ahead, the hands swinging past its knees, the head turning to you every other step.
      - **listen:** stooped over you, the neck out and bent down, the head on one side; now and then the fingers of a hand ripple.
      - **attack:** the fists up, too slowly, then down, too fast; it stays bent over what it hit.
      - **hit.**
    - **The engine chooses.** `CreatureArt` picks the clip by phase. Woken (Telegraph), it lopes while it's going and listens while it's stood, so the pace matters. The sim has no velocity for it, so `GreyboxScene.Pace` eases one from where each Gaunt was last drawn (in its car's frame aboard, the world off it); above `GauntGoing` (0.4 m/s) it's going. It faces its waker (`Extra`, passed as `Prey` like Tippy's).
    - **Anger is the telegraph.** App. A.6 LISTEN: "it leans in closer and tilts its head as aggro climbs". At each point of anger (`Extra2`) `LeanIn` bends the posed spine forward from the chest and tips the head over on its side, a quarter of the way each time. That's all the way at the default threshold (`enemies.json` gaunt.attackAt 4), and it stops there if the threshold's raised. The quarter is the art's (`GauntLeanPerAnger`), not a design number. `Bend` is the bone turn behind it, which the Track Doll's head now uses too.
    - **Aboard (note 110's standard doorway).** It boards with its waker, but stood it's taller than a door (2.1 m) and nearly a car's roof (2.75 m). Under a roof or a lintel (`Room`, as Tippy's) it gets down:
      - **crawl** (going): its legs folded under it, it drags itself along on its arms; 1.11 m high.
      - **squat** (stood): folded up small on its heels, the head out over its knees towards you; 1.22 m high.
      - **smash** (attacking): from the squat, fists no higher than the roof lets them; 2.03 m high.
      - Each clip's height is printed by the build, and `GauntTests` checks it against the car.
    - **First cut.** A full mantle of skin from the shoulders to the knees made a "spindly thing" a cardboard tube. The skull was a small ball. Bones in a skin too big for them is what reads, and the sagging face is what's unpleasant up close. The rims first painted the under-eye faces red, which read as a clown's cheeks; now each is a thin crescent of its own.
    - **Headless.**
      - `dt screenshot --threats --crew --gaunt sleep|stir|listen|angry|attack --view gaunt|gauntface`. Off the train's left it's stood in front of crewmate 4 (`Staging.Lone`; the Ribbits are put away), and the views look over their shoulder or up at it from the side.
      - `--gaunt in --view inside`: squatted in car 2's aisle.
      - `GauntTests`:
        - the clips fit a car;
        - it's drawn low indoors and stood up outside, going or not;
        - its anger leans it in and saturates;
        - woken, it faces its waker.

119. **The Grumbler's model (GDD v1.2 §21, App. A.8).** It was the husk tipped over on all fours. Now it's its own (`tools/blender/grumbler.py`, baked by `tools/models/recipes/grumbler.py`, 5,482 triangles, 34 bones: SK_Human plus a jaw and a second pair of arms).
    - **The design.** A dock labourer, still in what he worked in, gone wrong all the way through.
      - **Body:** face-down like a spider, the long arms and legs folded up over the back so the elbows and knees stand higher than it does, the hands and bare feet splayed flat. A second, thinner pair of arms has come out through its ribs, through holes torn in the waistcoat. It holds what it eats with them.
      - **Clothes:** a shirt with the sleeves rolled to the elbow, an open waistcoat and trousers torn off at the shins. Grease, coal dust and sweat stain all of it. The spine has pushed up through the back of the shirt, and the flat cap is still on.
      - **Face:** the eyes rolled up white under half-shut lids, the nose broken flat. The face is split from ear to ear, the edges raw and red, so the jaw drops a long way, and two crowded rows of yellow teeth fill the split.
    - **Clips.**
      - **gnaw** (on the crates, and aboard eating the cargo): head down in one, the jaw going hard, the rib arms tearing at it; once, a leg twitches out.
      - **scuttle:** very low and fast, the limbs in diagonal pairs, the head up and level, the jaw hanging.
      - **bite:** reared up on its back legs, all four arms out and grabbing, the head lunging in with the jaw dropped.
      - **maul:** over its victim, every hand down on them, the head going down into them.
      - **hit.**
    - **The crouch is IK.** `crouch()` sets it with `rig.reach` from where the hands and feet go, and its `avoid` pushes the elbows and knees up. From the T-pose the descent stuck with the feet up behind the tipped-over pelvis, so it starts from a rough crouch. The first targets couldn't be met: with the hips at 0.62 m, a knee kept over 0.9 m can't put a 0.62 m calf's foot on the ground. The hips came down to 0.4 m and the arms got longer.
    - **The engine chooses.**
      - **Gnawing:** on the crates, and when it's not feral.
      - **Rearing:** hit and feral, in TELEGRAPH, before it commits; it's the bite clip, its warning.
      - **Hunting:** in COMMIT it scuttles or bites by its pace (`GreyboxScene.Pace`, as the Gaunt's; note 118) and faces who it's after.
      - **Mauling:** GRAB and PUNISH.
      - **Who it's after is a guess.** It's whoever hit it last, but `LastHitBy` isn't sent to clients, so the draw faces the nearest crewmate, the same on every machine. (Note 118's `GauntGoing` is now `Going`, for both.)
    - **First cut.** The face was a potato's: two bulging eyes and a zip for a mouth. Three fixes made it unpleasant:
      - the sockets sunk deep, and the lids half down over a sliver of white;
      - bigger teeth in a wider split;
      - the jaw opening at all: −X drops it, so every opening had been closing it into the skull.
    - **Headless.** `dt screenshot --threats --crew --grumbler gnaw|rear|bite|maul --view grumbler` stages it in front of crewmate 4 (as the Gaunt is; the Ribbits and the Gaunt are put away), over their shoulder. `GrumblerTests`: low gnawing, rears up when hit, bites stood and scuttles going, faces who it's after.

120. **The Stoker's model (GDD v1.2 §21, App. A.5).** It was never drawn: only its work showed (the gauge, the wrong-coloured glow, the hiss). Now it's seen at the firebox door when it's open (`tools/blender/stoker.py`, baked by `tools/models/recipes/stoker.py`, 2,308 triangles, 28 bones: SK_Human shrunk, with a jaw).
    - **The design.** A fireman burnt right through to charcoal and still alive in it.
      - **Skin:** black, blistered and crazed like burnt wood. Where it's split, the fire shows through: the splits, its eyes and the fire in its mouth keep their own glowing layer, as the Cinder Hound's cracks do.
      - **Face:** no hair, no ears, no lips; the teeth are bare in a grin the heat drew back.
      - **Fingers:** long and black as burnt twigs.
      - **Its own light:** its fire lights its face a flickering sick green (`StokerFire`, the colour `GreyboxScene.FireColour` gives a fire with a Stoker in it).
    - **Where it's drawn.** `CreatureArt.FireDoorOpen` holds the door's centre while the door's open, and null while it's shut. `GreyboxScene.FireDoorOpen` sets it each frame, and the app sets that from `Boiler.FireDoorOpen`. Its origin is the door's centre on the backhead's face (`TrainKit.FireDoor`, which the door's frame uses too). In TELEGRAPH or COMMIT with the door open:
      - **peer** (TELEGRAPH): its head is out of the door into the cab, tipped back to look up at whoever's stood over it, and its arms are out over the lip, the hands flat on the cab's floor. Now and then the head tips over, and the jaw comes open on the fire.
      - **reach** (COMMIT, feeding): an arm up off the floor, groping out for whoever's opened the door.
      - **hit.**
    - **The firebox got a hole.** The backhead was drawn as one solid plate with the door's frame on it, and the backhead prop's leaves stood 30° ajar over it, so there was nothing to see into.
      - The plate is now drawn round the opening (`TrainKit.FireDoorHalfWidth`/`FireDoorHalfHeight`), with the firehole's sides going back to the fire.
      - The scene's fire is a dull back wall with a bright bed of coals along the bottom, set at the back of the firehole and toned down. It was a flat emissive panel.
      - The prop's leaves swing 70° open (`tools/models/recipes/cab_backhead.py`).
      - So the cab now shows the fire through the door, orange as it was and green with a Stoker in it.
    - **The crouch.** The hands on the floor are `rig.reach` targets. From one start the descent stuck with the arms thrown back, so `arm_to` tries a spread of starts with the elbow kept down by the shoulder and keeps the best. Each hand is laid flat with `rig.hang`; until then the fingers ran on under the floor.
    - **Headless.**
      - `dt screenshot --threats --stoker peer|reach --view firebox`: the door's opened by `--stoker` (or `--firedoor` alone), and the view is crouched at the door.
      - `dt art clip stoker peer --lift 0.75`: lifts a model whose origin isn't at its feet off the clip rig's floor.
      - `StokerTests`: drawn only with the door open, its head out of the door into the cab, its hands on the floor and not under it.

121. **The Followers' model (GDD v1.2 §21, App. A.6).** They were the husk shrunk to a lump. Now each is its own (`tools/blender/follower.py`, baked by `tools/models/recipes/follower.py`, 1,551 triangles), on a rig of its own: SK_Follower, a palm, the wrist's stump, a thumb and four fingers of three bones each, and a mouth (19 bones).
    - **The design.** A hand: an infant's grown to a man's and gone wrong.
      - **Skin:** pale and soft, faintly translucent, the veins blue-grey under it, wrinkled and grimed at too many knuckles.
      - **The wrist:** no arm, only a raw stump trailing threads of nerve.
      - **The mouth:** on the back of the hand, a puckered lamprey ring with three rings of teeth.
    - **Clips.**
      - **cling** (riding): flat, the fingertips dug in, the mouth working; then the twitch, every finger clenching at once and the threads drawing in.
      - **crawl:** up on its fingertips like a spider, the fingers in two sets thrown forward in turn.
      - **nest:** spread flat over the loot, the fingers kneading, the mouth wide.
      - **hit.**
    - **On a back.** Riding (DORMANT, TELEGRAPH), `Enemy.Extra` is its carrier, so `GreyboxScene` passes them as `Prey`. `CreatureArt` lays it flat between their shoulder blades, 1.35 m up and 0.15 m behind their middle (`FollowerUp`, `FollowerBack`). Its palm is to them and its fingers point up, so whoever's behind them sees the hand and its mouth, and they can't.
    - **Off it.** In COMMIT it crawls. Once it's in its car with a nest building (`Extra2` > 0), and in PUNISH, it nests, swelling as the nest builds to 2.5× its size (`FollowerSwell`).
    - **First cut.** Up close it was a cartoon glove: a fat smooth disc of a palm, the fingers floating off its edge, a target for a mouth. A ridge of knuckles now joins the fingers on, the palm is flatter, and the mouth is bigger and puckered. At its real size, on a back, it read from the first.
    - **Headless.**
      - `dt screenshot --threats --crew --follower back --view follower`: the new `follower` view is close behind crewmate 4 (`Staging.Lone`).
      - `--follower crawl`, and `--follower nest --view inside` (car 2's aisle).
      - `FollowerTests`:
        - riding, it's on its carrier's back, flat to it;
        - off them, it's low;
        - its nest swells it.

122. **The Climbers' model (GDD v1.2 §21, App. A.4).** They were the husk drawn out thin and tipped forward. Now each is its own (`tools/blender/climber.py`, baked by `tools/models/recipes/climber.py`, 2,902 triangles, 27 bones, SK_Human drawn out).
    - **The design.** One of the crew gone wrong. It still wears the same coveralls and the same gas mask the crew do (`tools/blender/crew.py`).
      - **Body:** drawn out long and thin, the coveralls burnt into the skin and torn open down the back and the limbs, soot-black where it shows.
      - **Face:** the mask has grown into it. The perished rubber is the skin now, the two round glass eyes in rusted rims are cracked and black, and the filter's canister is a snout with the hose torn off and hanging.
      - **Hands and feet:** the hands long and hooked; the boots split open, the long toes out of them.
    - **Clips.**
      - **run** (pacing): bent double on all fours, the hands thrown out ahead, the head level and still.
      - **scrabble** (TELEGRAPH, in the gap): the hands going one over the other up the buffers, the feet kicking, too fast.
      - **walk** (COMMIT, on the roofs): crouched low, the hands touching the roof, the masked head snapping round.
      - **crouch** (COMMIT inside an unlit car, `Extra` −1): folded in a corner, the head cocked, a finger drumming once.
      - **grab:** on its victim, both hands on their head, the snout in their face.
      - **hit.**
    - **Running along, not at, the train.** Pacing, it's loose, and the loose basis faces the nearest car. So `Enemy(e)` turns it a quarter round to run along the line, by the side it's on (`Extra2`).
    - **The limbs that land go by IK** (`reach_from`): the hands on the ground or the buffers, from a spread of starts, as the Stoker's are (note 120). The first gallop stood half upright on straightened legs, the arms propping it like stilts. Folding the legs deeper and laying the body level made it the crouch-run of the description.
    - **Headless.**
      - `dt screenshot --threats --climber run|scrabble|walk|crouch` stages it at car 2: `scrabble` with `--view gapside`, `walk` with `--view roof`, `crouch` with `--view inside`.
      - `ClimberTests`:
        - pacing, it runs low and along the line;
        - in a dark car it waits low, and on a roof it's crouched.

123. **The Fire Flies' model (GDD v1.2 §21, App. A.5).** They were 24 orange boxes circling a point. Now they're moths (`tools/blender/fire_fly.py`, baked by `tools/models/recipes/fire_fly.py` into a 512 atlas, 1,218 triangles), on a rig of their own: SK_FireFly, the thorax, the head, the abdomen's two bones, four wings and six legs (15 bones).
    - **The design.** Big moths, a hand across, come in out of the dark to a lamp.
      - **Body:** furred the grey of ash; the abdomen a fat grub of black plates, alight between them, with a coal at its tail.
      - **Wings:** charred paper, mottled and tattered, scorched to the tips, their edges still smouldering.
      - **The eyes:** on each forewing, painted in the bake, a human eye, the white gone yellow and the upper lid towards the head. Folded, the wings put the two side by side, so each moth settled on the glass is a pair of eyes looking back, and a lamp they've found is a cluster of them.
    - **Clips.**
      - **flutter:** the slow heavy beat of a big moth, the hindwings a frame behind, the abdomen pumping.
      - **settle:** on the glass, the wings folded back flat in a roof over it. It throbs, crawls a leg at a time and lurches, shivers its wings, then snaps them half open on the eyes, holds, and shuts.
    - **The swarm** (`CreatureArt.FireFlies`). They grow from 5 moths to 22 over the linger, and the share settled on the glass grows faster still. They fill by 20 s (`FireFlySwarmFills`), the sim's ignite time.
      - **On the glass:** settled ones sit on a cylinder round the flame, 0.085 m out (the lantern's 0.074 m and a body), at their own angle and height, heads up.
      - **In the air:** the rest fly loops of their own, now and then dashing in at the glass.
      - **The light:** the lamp's light through them grows redder and stronger as they fill.
    - **Which lamp.** The sim has one lamp per car (`Vehicle.LampLit`) and puts the swarm at the room's middle. The art pass draws two lanterns (`SceneArt.LampPositions`), so `GreyboxScene` draws the swarm on the nearer one. The sim is untouched.
    - **Headless.**
      - `dt screenshot --threats --view flies`: the new `flies` view is under the middle car's forward lantern. Add `--later 15` for the full swarm.
      - `dt art clip fire_fly settle --dist 0.15 --at 0,0.03,0 --lift 0.024`: the clip-sheet lights now close in with the camera when it's under 2 m out, for a model this small.
      - `FireFlyTests`:
        - the swarm thickens round the lamp as it lingers;
        - lingering, most of it is on the glass.

124. **The Passenger's model (GDD v1.2 §21, App. A.8).** It was the crew figure in the look of the crewmate it copies (note 75). Now it's its own (`tools/blender/passenger.py`, baked by `tools/models/recipes/passenger.py`, 4,014 triangles, 27 bones, SK_Human at a man's height).
    - **The reading.** The v1.2 brief is "old coat and cap; passes for crew in the dark", and the checklist's need adds "wrong up close". The crew-copy draw was v1.1's. The new model keeps what made the copy work, the colour of the one it copies, and gives up the rest.
      - **Its scarf** is dyed their colour (`look.json` `crewColours`, the crew's own `.paint` tint), so across a car it's theirs.
      - **Its silhouette** is a man in a heavy coat and a cap.
      - **Up close** it's wrong.
      - The roster (note 81) and the silence tell are unchanged. Without the model, the crew-copy draw (`GreyboxScene.AsCrewmate`) still stands in.
    - **The design.** A conductor off a train that went into the Territory a lifetime ago.
      - **Coat:** a double-breasted greatcoat to below the knee, felted and mould-bloomed, salt-white at the shoulders, its brass gone green and its hem rotted ragged.
      - **Cap:** a peaked conductor's cap gone soft, its badge black.
      - **Face:** grey, the cheeks fallen in, the eyes open and filmed white in dark sockets. The lips are wired shut with seven rusted stitches: that's why it never speaks.
      - **The watch:** in its left hand, always, a pocket watch that stopped when its train did.
    - **Clips.**
      - **stand:** hanging about like crew, but dead still with no breath in it. The head turns slowly and snaps back; the watch comes up to the face, is looked at a long time, and goes down.
      - **walk:** the crew's own gait, but the head never moves and the arms never swing.
      - **drag** (GRAB): leant into the haul, a hand back in someone's collar.
      - **pin** (UNCOUPLE): bent at the coupling, heaving at the lever in jerks.
      - **hit.**
    - **Drawn** (`CreatureArt`).
      - **Facing:** it faces as it goes (`Extra2`, the crew-copy's reading).
      - **Walking or still:** it walks at the pace it's been going (`GreyboxScene.Pace`), or stands.
      - **With its victim:** in GRAB, the sim carries the victim at its feet, so it's drawn a stride (`PassengerStride`, 0.8 m) ahead of them, dragging when it moves and heaving at the pin when it's stopped at the caboose.
    - **Headless.**
      - `dt screenshot --threats --passenger stand|walk|drag|pin --view passenger`: the new `passenger` view is in car 2's aisle. `drag` lays the one it's dragging on the floor behind it (`Staging.Dragged`).
      - A still frame can't measure a pace, so `GreyboxScene.StagedPaces` gives the staged one its pace (`Staging.PassengerPace`).
      - `PassengerArtTests`:
        - it's a man's height in its cap;
        - the scarf, and only the scarf, takes the copied crewmate's colour;
        - dragging, it's a stride ahead.

125. **The brake and the vent as a pair (T106).** With steam driving (T97) pressure is speed, so a board ahead wants the gauge down, not just the brake on. A held brake fades, and the steam fights it.
    - **The fireman fired for the wrong speed.** It fired to the pressure for its own cruise, which only changes when it drives, so it stayed at the open line's 14 m/s. Under a board the driver braked away steam the fireman had just made. Now it fires for what the line allows here (`MindingCruise`: lamp, authority, boards).
    - **And vents when the steam runs the train away.** When the train is running over the allowed speed with the gauge ten over its mark, the fireman walks out of the left doorway, along the left running board to the blow-off, and holds it open until the gauge is down to the mark. Then it comes back in. The driver holds the train on the brake meanwhile. Only one of them leaves the cab at a time (`CrewCalls.Venting`, `CrewCalls.DriverAway`).
    - **Not for a board still ahead.** Venting whenever the gauge read high, frontier:11's fireman spent 1,412 s of the night at the blow-off, and the train ran out of steam and missed the dawn. The driver brakes down for a board in good time; venting throws away coal. So it vents only once the train is actually running over what's allowed: on a descent, where the steam and the grade together beat the brake.
    - **What its nights found: a split train stood at a switch till dawn.** On frontier:7 a Switchman set a dead line's switch, the train took it, and it backed off onto the main line. A car had been cut loose miles back and ran on buffered up behind, so the train was two rakes. The shunter only sets a switch back once the train stands short of the points, and that check (`SwitchPlan.StandingAt`) wanted one rake. Now it wants every rake standing on the main line short of the points. `SwitchTests.AStandingTrainWithACarCutLooseBehindIsStillStandingAtTheSwitch`.
    - **The driver let go of the cab's steps.** Down to set a switch back itself, the driver took hold of the cab's steps to climb back, then let go, because nothing said climb (T102 did only at the gate). It was back on the ballast to try again, all night. Now walking into the cab climbs whatever steps it's on (`StopHand.IntoCab`). And a driver waiting on a live shunter who never comes (a deepTerritory:1 crew of two, the shunter on the guard van's roof from 1914 s till dawn) gives up after 150 s and does it itself. `StopCrewTests.WithAShunterWhoNeverComesTheDriverSetsTheSwitchBackItself`. That test's night also now clears the brake as the host does: a standing train stays on it.
    - `VentTests`: 6 cars down a 4 km descent at 2.5 %, with the gauge high. The fireman vents and comes back in, the driver stays at the controls, and the gauge comes down. Without the vent it climbs from 83 to 95, and the train runs to 20 m/s.

126. **The Soot Children's model (GDD v1.2 §21, App. A.6).** They were a sourced scan: the Boy Room's boy, posed into the huddle at full resolution and rigged on it (`tools/models/figures.py`). That rig could rock and lift its head but never get up, and its eyes, the whole tell, were two boxes drawn over it. Now the child is its own (`tools/blender/soot_child.py`, baked by `tools/models/recipes/soot_child.py`, 4,540 triangles, 28 bones: SK_Human at a child's height and proportions, with a jaw), so it can do what the brief asks of it.
    - **One child, two ways** (the lure): the model's variants.
      - **Variant 0, the real child:** a child's eyes, the whites and brown irises; the hands and feet only dirty.
      - **Variant 1, a Soot Child:** the eyes black, wet and filling the sockets, and the hands and feet black as tar to the wrists and the ankles. That's the tell from five metres; `Extra2` picks it. Only this variant has teeth: rings of them, set back in the mouth.
      - **Both:** a waxy-pale face with soot run down it from the eyes and the hairline, matted hair hanging at the sides (the eyes left clear), and an adult's shirt gone to rag-grey, tied with string.
    - **Clips.**
      - **huddle:** squatted on its heels, its arms round its knees, rocking.
      - **call** (`Extra` 1): the head up to the train, the mouth open, a hand out.
      - **pin** (once, the first 0.6 s of GRAB, `SootPinSeconds`): up off the ground and onto them, the jaw dropping.
      - **drink:** locked round them, arms round the neck and legs round the waist, its face in their throat, pumping. Now and then the head comes up and round to look over its shoulder at whoever's watching, and goes back down.
    - **On them, standing.** The brief says "pins you". The crew have no pinned-down pose yet (the checklist's `crew-grabbed`), and the sim leaves its victim standing. So it clings to them: in front of them and facing them, 0.3 m off their feet (`SootCling`), the clip lifting it to their chest. The victim is the nearest living crewmate (`GreyboxScene`, as the Grumbler's), since who it holds isn't sent to clients. LUNGE never shows (COMMIT goes straight to GRAB in the same tick), so the pin is the leap.
    - **What's lost:** the link to the bedroom boy. The villages' Boy Room scene keeps the scan.
    - **Headless.**
      - `dt screenshot --threats --crew --soot huddle|call|real|drink`, staged in front of crewmate 4 (`Staging.Lone`). The new `soot` view is over their shoulder; `sootside` is from the side, at them and what's on them.
      - `dt art clip ... --variant n` draws one of a model's variants.
      - `SootChildTests`:
        - the variants are the same child with their own eyes, hands and feet;
        - calling, the head comes up;
        - on someone, it's up at their chest, its feet off the ground.

127. **The Switchman at its lever (GDD v1.2 §21, App. A.7).** It had only `wait` and `flee`. It now has the clips for what the brief has it do with the lever (`tools/models/recipes/switchman.py`, on the scan's own rig).
    - **grip** (COMMIT, the derailer's tell): its right hand down on the lever at its side, leant to it, the hand trembling on it in pops, the head once snapped round to the coming train.
    - **throw** (PUNISH, once): the lever heaved over and across in front of it, the body twisting into the pull. Then it's dead still, its hand still on the lever, the head cocked, watching what it's done.
    - **The lever.** No switch stand sits where the Switchman is staged, so `CreatureArt` draws one with it while it grips and throws: an iron lever from a stub post's pivot by its right foot (`SwitchLeverPivot`) up into its hand. It turns as the hand does.
    - **Headless.**
      - `dt screenshot --threats --switchman wait|grip|throw --view switchman`: the new `switchman` view is on the line short of it.
      - `SwitchmanArtTests`: gripping and throwing, the lever is drawn; thrown, it's gone over.

128. **The Cinder Hounds' bite (GDD v1.2 §21, App. A.3).** On someone (GRAB), a hound used to go round the roof fight's crouch-and-lunge as if it had nobody. Now, after the leap, it bites (`tools/blender/cinder_hound.py` `bite`): braced low, the forelegs planted wide and the hind legs dug in hauling back, the jaws clamped, the head wrenching side to side in hard pops, held, and wrenched again; the tail lashing. Aboard with nobody in its jaws it keeps the crouch-and-lunge.

129. **The Draggers drag them under (GDD v1.2 §21, App. A.4).** The grab ended with the limbs still gripping the roof, and then nothing: the catch was gone. Now, over the last 0.8 s of the grab's window (`DraggerDragSeconds`; `Enemy.GrabWindow`, replicated), both limbs play `drag` (`tools/blender/dragger.py`, once). It's a yank, the hands dragged back across the sheet to the lip with the fingers scoring it, then the limbs whipped down out of sight. Holding a hanging player is still to come: the crew have no hanging pose to hold (the checklist's `crew-grabbed`). `DraggerArtTests`.

130. **A train that's lost the hill (T107).** The Deep sweep's deepTerritory:1 with a crew of 8 ended 0.18 km along the line. On the alternate at Grieve Junction (3.2 % up), the train stopped near the top with Climbers coming over car 2. Then it rolled back: 9.5 km down the climb it had just come up, to the gate at 22 m/s. The driver had been left on the ballast, and froze.
    - **Why nobody braked.** The driver's cruising brakes for the end of the line, and to hold a train that's over its speed. Its speed is the size of its velocity, so one rolling back read as under its cruise, and steam was given. Now a train going backwards when it's meant to be going on gets the brake (`ConductorBot.Drive`). The fireman minding the controls drives through the same code. `RollBackTests`: without it, a 10-car train let go on a 3.2 % climb with no steam runs back at 14 m/s.
    - **And the fireman watched it go.** Climbers had pulled the driver off onto the ballast as the train stalled; the fireman, in the cab, minded the controls only for a driver out sanding (T105). Now it minds them whenever the driver's away from them (`CrewCalls.DriverAway`). Moving, it holds the train to what the line allows and brakes it rolling back. Standing, it only holds the brake: it never drives off without the driver. `RollBackTests.WithTheDriverOffTheEngineTheFiremanBrakesItRollingBack`: without it, 14 m/s back down the hill.
    - **Why the driver and fireman were off the engine at all.** They hadn't been pulled off. At 9.72 km on that alternate the land beside the climb reads a hand's breadth above the cab floor. The motor takes the highest surface underfoot, and the ground is always a candidate, so it set both of them down on the ground at 13 m/s, in two runs at the same spot. Now a player riding the train keeps the train underfoot while any of it is there; the ground only catches someone with nothing of the train under them (`PlayerMotor.UpdateSupport`). `CabRideTests` stands a player in the cab up that alternate: without the fix, set down at 9720 m.
    - **And the crew on the roofs, on frontier:3.** With the cab crew kept on the engine, frontier:3 stood at 21.28 km till the dawn. Its alternate climbs up beside main there to rejoin it, within a formation's width of main's rail. The terrain gave the first edge's formation where two covered a point, so main's bed, 2.8 m higher, lay over the alternate's rail. The roof crew jumping the gaps between cars came down on it, beside the train. The driver stopped for a crewmate left behind (T96), they climbed back up and hopped on, and it went round again. Now the nearer rail's formation wins (`TerrainField.Height`, `CabRideTests`). Before the motor fix the cab crew were set down there too and nobody braked; the train ran on.
    - **A driver alone out sanding.** With the fireman left on the ballast after a switch set-back, the driver went out to sand a greased climb with nobody in the cab. The sanded wheels and the steam took the train onto the Sleepers at 11.8 m/s. Now, with steam driving and no fireman minding the controls (`CrewCalls.FiremanMinding`), the driver goes out to sand only while the train is slow (6 m/s), and comes back in once it isn't. `SandTests.AloneWithSteamDrivingTheDriverIsOutOnTheBoardOnlyWhileTheTrainIsSlow`: without it, out on the board at 16.9 m/s.
    - **And the mine head before it.** The stop had cut the cars off short of the spur. Nobody threw the switch in 240 s, so the driver backed down to couple up again. The cars, left on a slight downgrade, rolled away down it at a metre a second. Within 15 m of them the engine creeps at 0.8, so it followed them for 1,010 s and a kilometre, a hand left on the ballast behind. Now it closes at their speed plus the crawl (`StopDriver`, `Leg.BackOut`).
131. **Standing for the crew, in Deep territory (T81).** The Deep sweep missed every dawn, and the traces showed the train standing, not losing a fight.
    - **A hair short of a Holdout.** The driver slows for a lit Holdout to stand 5 m short of it, and only alongside starts the wait it gives up after (T96). deepTerritory:1's crew of two stood a hair over 5 m short, the allowed speed there all but nothing, till the dawn. A stand within 15 m now counts as alongside (`ConductorBot.AlongsideTheHoldout`).
    - **Measured on the wrong line.** A crewmate's `LineHint` is along the main line. On an alternate that's another count, and the driver set back again and again for "left-behind" crew who were on his own roofs. The left-behind check now projects each crewmate onto the train's own path (`AlongTheTrain`), counts only someone stood on the ground (a hop between roofs is in the air), and takes anyone beside the train up to the engine: two walkers pacing the rear car were in and out of the old window (10 m past the rear) and walked 2.5 km beside a 2 m/s train till one died of the cold.
    - **Out on the board with the door open.** A driver alone (no fireman) went out to sand within seconds of a shovelful. Only someone in the cab shuts the fire door, so it stayed open at a stand, and the Stoker came in and put the fire out (App. A.5) while the driver sanded on at a standing train till the cold took him. Now the driver waits in the cab for the door, comes back in to a Stoker (it's fought from the cab), and doesn't go out, or stay out, with no steam to pull on the sand.
    - **Back up into the tunnel's mouth.** Three walkers on deepTerritory:2, turned away from a car a Climber had got into, climbed straight back up the ladder from the plate under a posted tunnel's mouth and were struck together (four of the crew of eight gone by 1,500 s). Off the roofs already, with a tunnel near, a walker now stays off them: on the plate, inside, or back down the ladder (`LinesideTests.AWalkerDownOnAPlateForAPostedTunnelStaysOffTheRoofs`).
    - **Frontier, crew of 8, seeds 1–4:** 3 delivered against main's 2. The Deep sweep passes its checks; deepTerritory:1 delivers with a crew of 2, and deepTerritory:2 still stands at 24.5 km (next).

132. **The Gaunt is no longer a man (GDD v1.2 §21, App. A.6).** Note 118's Gaunt was SK_Human stretched: stood up, it had the same silhouette as Tippy Toesie, a tall thin person. It's been redone as a thing on stilts (`tools/blender/gaunt.py`, baked by `tools/models/recipes/gaunt.py`, 4,278 triangles, 24 bones, SK_Gaunt).
    - **The body.** Four legs, each jointed three times and longer than a man is tall, the knees standing higher than its back like a harvestman's. Between them a starved, ribbed body is slung two metres up. A long neck runs up and out from it to an eyeless horse's skull with a puckered, needle-toothed mouth at its tip. On the skull sit two bat's ears, each bigger than the head, ribbed and veined. The legs and body are grey bark, so asleep, with the legs laid out round it, it's a heap of dead branches.
    - **The clips keep their names and their jobs.**
      - `listen` stands over you, the head hung above yours and the ears cupped at you.
      - `follow` stalks in slow diagonal pairs.
      - `attack` (now a loop) rears up on the hind legs and stabs the forelegs down.
      - Aboard, `crawl`, `squat` and `smash` fold the legs. The knees go up under the roof and the cannons slant out along the floor fore and aft, so it's packed into the car like a spider in a box.
    - **The ears are held level.** They're set in armature space (`rig.hang`), whatever the skull does. A listening thing holds its ears on the sound, not on its head.
    - **Anger lets the neck down** (`CreatureArt.LeanIn`), no longer bending the back. The forelegs hang from the shoulders, so bending the back lifted them off the ground. The neck bends down from the shoulders and is straightened again at the top, so the head stays out over you. The skull rolls about its own length. `GauntLean` is now 0.32 rad, so at full anger the head comes down to the helmet, not into it.
    - **The `gaunt` and `gauntface` views** look higher, at where its body and head now are.
    - **`GauntTests`** measure the head as the front of it above chest height, no longer as its top: stood up, its top is its knees. Facing is now checked by where the mesh's middle goes: its head and ears are half its vertices.

133. **The Whistler is no longer a man (GDD v1.2 §21, App. A.4).** It was SK_Human stretched, with one long arm. Now it's a pale thing four metres long, segmented like a centipede and thick as a thigh (`tools/blender/whistler.py`, baked by `tools/models/recipes/whistler.py`, 5,216 triangles, 57 bones, SK_Whistler).
    - **The body.** Ten segments, twenty thin legs and a pair of long hooked forelegs. Ivory plates down its back, grub-white flesh where they part, all of it streaked with the couplers' grease.
    - **The head.** No eyes and no face, only a blunt wedge with two feelers. Out of the wedge comes a siphon with a puckered lip and stops along its top like a flute's, and the siphon is what whistles.
    - **The clips keep their names and their jobs.**
      - `fold` coils it round the drawgear under the plate, the front laid up over the coil looking out. A ripple runs down its legs once.
      - `whistle` rears it straight up out of the gap, over three metres. The forelegs hook the cord and yank it. The siphon swells, and the legs down its upright length stir out of step.
      - `watch` lifts its front at the gap's mouth and turns it in jerks.
      - `run` snakes it away with its legs in waves.
    - **No leg IK.** The legs are posed relative to their segment, so a coiled or reared body carries them with it.
    - **`centred` and `grounded`.** Each pose is set over the gap's middle and onto the rail (`root@loc`), so the old origin contract still holds. `WhistlerTests` keep their measures unchanged: folded under the 1.1 m plate, whistling over a crewmate's head on it, its front turned to the eye.

134. **The Choir are no longer children (GDD v1.2 §21, App. A.7).** Each ghost was a choir child in a surplice: SK_Human at a child's size. Now each is a bell of membrane the size of a child's chest, drifting the way a jellyfish drifts (`tools/blender/choir.py`, baked by `tools/models/recipes/choir.py`, 2,570 triangles, 54 bones, SK_Choir).
    - **The bell.** A dead blue-grey hood, the veins dark in it, with a ruffled skirt round its rim. Three frilled curtains hang under it, and six tendrils over a metre long. It has no eyes and one mouth, a child's, on its front. The grey lips are held open in a singing O round small milk teeth and a black throat. That is the voice.
    - **The clips keep their names and their jobs.**
      - `drift` pulses slowly, two beats; it rises on each squeeze, the tendrils trailing in waves. Once it snaps round to face you.
      - `swoop` tips mouth-first into the flight, pulsing fast, its tendrils streamed back.
      - `seize` settles on its catch's head like a cap, the tendrils wound round the skull and down the neck, its mouth turned to their ear. The sim has it 1.4 m over their feet, so the bell drops 0.5 m to sit on their head.
      - `besiege` presses it to a shut door, its tendrils thrown at it and slapping down it in bursts.
    - **The pulse.** It's the bell's scale (contracted: narrower and taller) and the rim flaps curling in about their tangents.
    - **The tendrils and curtains are aimed bone by bone along a curve** (`aim_along`): each bone points at the curve where it's a bone's length on, never back up it. So they can trail, stream, lash and wind round a head without IK.
    - **The bell rides higher** (its middle 1.15 m over the origin), so the tendrils hang clear of whatever it's over.

135. **The Followers are no longer hands (GDD v1.2 §21, App. A.6).** Note 121's Follower was a hand gone wrong. Now it's a tick grown to a hand's size and bloated (`tools/blender/follower.py`, baked by `tools/models/recipes/follower.py`, 2,708 triangles, 24 bones, SK_Follower, inside the 25-bone budget).
    - **The body.** A swollen sac of grey-white leather, blue where it's stretched thinnest, ridged across with the folds it hasn't filled. At its front is a hard shield the red-brown of dried blood, with twelve small wet black eyes bunched on it like roe, and a barbed beak between two palps under it. It has ten jointed legs, hooked at the tips.
    - **The clips keep their names and their jobs.**
      - `cling` lies flat on a back with every leg splayed and dug in, and the sac pulses. The twitch is every leg clenching at once and letting go, the sac jerking.
      - `crawl` scuttles on alternating sets of legs.
      - `nest` spreads it flat, the sac swelling and easing, the legs kneading pair after pair, the beak working.
    - **The engine side is unchanged:** the riding matrix, `FollowerSwell`. Only the comments say tick where they said hand. `FollowerTests` pass as they were.

136. **The Climbers are no longer men (GDD v1.2 §21, App. A.4).** Note 122's Climber was a crewman gone wrong, its gas mask grown into its face: SK_Human drawn out. Now it's a long, low, soot-black thing with six limbs (`tools/blender/climber.py`, baked by `tools/models/recipes/climber.py`, 3,652 triangles, 36 bones, SK_Climber).
    - **The body.** The limbs are splayed out from its sides like a gecko's: the elbows out and up at the body's height, the forearms coming down from them, each hand three hooked fingers made for steel. Its skin is wet-looking rubber, paler grey under the belly, with a row of short spines down its back to a whip of a tail.
    - **The head.** An eyeless blunt wedge. Under its front is a lamprey's sucker, ringed with three rows of hooked yellow teeth.
    - **The clips keep their names and their jobs.**
      - `run` snakes along low with the limbs in alternating threes.
      - `scrabble` stands it up the gap like a lizard on a wall, the hooks snatching up the car ends, the buffers and the coupler.
      - `walk` creeps flattened, one limb at a time, its head swinging as if it could smell its way.
      - `crouch` folds it in a corner, its head cocked; once it snaps to the other side.
      - `grab` rears it over its victim, the forehooks over their head and the sucker opened on their face.
    - **Limb IK.** The limbs go by `rig.reach` from a spread of starts. The elbow is kept out at its own side and, on the ground, up at the body's height. That's what keeps the sprawl from collapsing into a stick insect's legs.
    - **`ClimberTests` keep their measures.** It runs under 1.2 m, crouches under 1.3 m and walks the roofs under 1.7 m.

137. **The gun is a cannon, in pieces the game can work (GDD v1.2 §19 "crude cannons with a manual reload", App. C.3 powder, ball, ram).** The gun car carried a water-cooled machine gun; the HUD still called it a turret. Now it's a crude breech-loading swivel cannon (`tools/models/recipes/cannon.py`).
    - **Five props, all in the gun's frame** (the pivot 0.9 m over the roof, the barrel along −Z):
      - `cannon_mount`: the trolley on the roof rail and the pedestal. It stays on the roof.
      - `cannon_carriage`: the turntable, the yoke, the gunner's iron seat behind the breech, the traverse tiller under the left hand and the elevating handwheel under the right. Also the boiler-plate shield with its aiming slot at a seated eye's height, the rack of three spare powder chambers and the shot box. It turns about Y.
      - `cannon_barrel`: the cast barrel and its rings, the trunnions, the breech's open trough, the locking wedge, the sights and the friction-tube lanyard. It elevates about X. The muzzle is 1.5 m out.
      - `cannon_chamber`: one powder chamber.
      - `cannon_ball`: a round of shot.
    - **The reload maps onto it.** The ball goes into the bore's open back, a powder chamber is dropped in behind it, and the wedge is driven down to lock it (the ram).
    - **Scene and view constants.**
      - `SceneArt` draws the pieces chained: the mount, then the carriage turned, then the barrel elevated on it, with a chamber in the breech while it's loaded (`ReloadNeeded` 0).
      - `TrainKit.CannonSeat`, `CannonChamber` and `CannonMuzzle` give the seat (for sitting the gunner), the breech and the muzzle (for the flash).
      - The traverse and elevation are zero until the gunner's controls give the gun an aim to keep. Sitting, aiming, firing and reloading are another change's.
    - **Views.** `cannon` is the seated gunner's view through the slot; `cannonside` is the whole gun from beside it.

138. **Far land, and water with a far side (playtest: "the water falls below the mountain in the distance, no coastline").** Two things made it.
    - **The causes.**
      - A generated line's land stops 300 m out (`WorldArt.PlanLateral`), and the plan's terrain dropped `skirtDropM` 30 m past its 250 m corridor, "into the fog".
      - Shore water (`PlanArt.Water`) runs 1,500 m out with nothing across it.
      - From a deck, the water's fogged edge then sat under the sky's backdrop band (`PlanSky`) with a strip of haze between them. A lake near the corridor's edge lost its rim to the drop.
    - **`PlanArt.FarLand` carries the land on to the horizon.** Past the corridor's ground, low hills come and go along the line, from the plan's own land at 292 m up to about 50 m over it at 1.7 km. On a Fundy or dyke shore there's a far shore across the water instead: a beach at 1.1 km for the bay, 520 m for the tidal river, and hills behind. The open sea keeps its open horizon.
    - **Far land never stands over another corridor's ground.** Inside one it tucks under that corridor's land, so a branch out there keeps its track.
    - **`skirtDropM` is now 0** (`content/linegen/tiers.json`). The far land covers the corridor's edge, so the drop isn't needed to hide it, and it was what took the lakes' rims.
    - **Rivers.** A river's water under its bridge runs on 1.2 km down its valley either way, not stopping at 160 m.
    - **Headless.** `dt screenshot --route frontier:7 --at 8170 --cam 8170,0,12 --target 8170,-400,6` (the tidal crossing), `--at 7835 --cam 7835,-30,6 --target 7835,-250,2` (a lake).

139. **The dead towns' houses, modelled (playtest: "meshes missing textures, the house doesn't look good").** The houses were kit boxes, plaster or brick with flat black window panels. In the fog at night they read as untextured silhouettes. Now there are four modelled frontier houses (`tools/models/recipes/town_houses.py`, `house_0` to `house_3`), weatherboarded so the board courses, the white trim round the windows and the raw boards nailed across them read through the fog.
    - **The four houses.**
      - `house_0`: two storeys, faded white, with a sagging porch.
      - `house_1`: a red cottage with a lean-to and a stove pipe.
      - `house_2`: two storeys of brick, a slate roof and a porch.
      - `house_3`: fallen in, half its roof down inside it.
    - **Where they're used.** `TownKit.House` returns one by variant. A stop's village (`WorldArt.Stops`) fits one to each footprint part (`TownKit.HouseProp`: scaled to the part, its height in proportion). Both fall back to the kit's boxes without the look's props.

140. **Hard blocks in the sky (playtest), and the headless app capture.**
    - **The sky.** The sky's cloud noise, the SSAO's rotation and the film grain hashed with `fract(sin(dot(p, k)) * 43758.5)`.
      - The cloud's drift grows with the night's clock (`FrameLighting.Time`, wrapped at 10,000 s), so late in a night the sine's argument runs to around ten thousand. There a GPU's `sin` has lost its precision, and the cloud banks broke into hard dark blocks.
      - Screenshots pin the time at 0.37 s, and lavapipe's `sin` is exact, so no headless view showed it.
      - All three now use Dave Hoskins' hash without sine, which is exact at any input. The cloud's drift also wraps after 256 of the noise's cells.
    - **The app capture.** The app's `--quit-after` counted from launch. A load that outlasted it never ran the play loop, so `--capture` drew a default camera with a zero field of view and threw. The play loop now always runs one frame first.

141. **The hotbar (T108).** Playtest: "I don't have a bludgeoning weapon in my inventory". There was no inventory: a swing hit with whatever was (not) in hand. Now `PlayerState.Kit` packs eight slots, a byte each (`Player.Kit`: crowbar, shovel, wrench, GDD §1322's tools), and `HeldSlot` is the one in hand. Number keys 1–8 pick a slot (an empty one leaves the hands free), the wheel steps through the tools; the choice rides the intent (`PlayerIntent.Select`, `Cycle`), applied in `PlayerMotor.Step`, so a predicting client picks what the host does. Every spawn and respawn starts with `player.json`'s `kit` (a crowbar), and a Holdout gives back what you went in with.
    - **Blows.** With any tool a swing is the spec's blow (`melee.damage`, 1: enemy health is in blows); with nothing in hand, `melee.barehanded` (0.25) of one. The left button swings too (the host ignores a swing from someone at a gun, whose left button fires). The shovel stays implicit at the firebox for now. The wrench is a tool on the cab's rack; what mends the boiler is the repair kit (note 150).
    - **Wire protocol 2.** A byte on the tick a slot is chosen (flagged in the actions byte), and the kit and slot in the player record.
142. **Quick fixes from the solo playtest (T110).**
    - **Just you by default.** The menu's quick night starts with no bots (they got in the way of a human), and with none it's the real hosted night alone, not the command line's bare prototype.
    - **Shunting at 6 m/s.** "The reverse is agonizingly slow": the bots backed to a switch, onto a cut or for a crewmate at 3 m/s (2.5 for a crewmate), a minute and more for a few hundred metres. `StopDriver.SetBackTop` is now 6; every move still brakes to its mark on `Toward`'s own curve. Driving by hand, reverse was never slower than forward (the steam drive's the same either way).
    - **Past the boiler both sides.** The spectacle plate's openings go to the floor, and the boiler is 1.4 m wide (`train.json`), so the way out of the cab's front each side is 0.7 m, not a player's width exactly (`CabWalkTests`).
143. **The boiler as the driver's game (T109).** Playtest: coal for speed, a pressure that falls slowly, the vent if you overdo it; too long in the red and it ruptures, and the engineering kit mends it; and "in theory a single player should be able to do it all".
    - **The red has teeth.** The safety valve (not in the spec) shed 2.5 a second over 95, more than any fire makes, so nothing but the Stoker ever reached the spec's rupture (100 held 20 s). At 0.6 (`boiler.json`) a fire kept full beats it: up into the red, the valve lifting and heard, then to 100 and the 20 s. `OverfiringLiftsTheSafetyValveButDoesNotRupture` is now `OverfiredPastTheSafetyValveTheBoilerRuptures`, and venting in the red saves it (`VentedInTheRedTheBoilerHolds`). Bots fire for the line's speed (T106), well under the red.
    - **Told every way.** In the red, a HUD line; pinned at 100, a flashing "VENT! RUPTURE IN n S" counting the spec's 20 s; a straining shriek that climbs with it (`boiler-strain`, over the valve's hiss); the cab shaking under you (`BoilerShake`, presentation only); and the burst, once, when it goes (`boiler-burst`). The vent's roar while it's held (`vent-hiss`).
    - **The vent's in the cab,** on its left side (`CarFrame.Engine`), clear of where the firebox is worked from; its blow-off goes up through the cab roof. The bots' vent leg is a step across the cab.
    - **A rupture seizes it.** The engine's own rake drags at `ruptureDecel` (1.5 m/s²) down to `ruptureCoastBelow` (4 m/s), then coasts, to a stop unless braked (`TrainOnLine.Conditions`).
    - **The engineering kit.** One wrench, on a rack on the cab's right side (`InteractableKind.ToolRack`): a press takes it into the hotbar (T108) and into hand, a press with it in hand puts it back (`Boiler.WrenchOut` replicates). Held at the firebox with the wrench in hand for `repairSeconds` (25), a ruptured boiler was mended, cold and empty: fire it back up from nothing. Note 150 moves the mend to the repair kit, which rides in car 1; the wrench is a tool to swing.
144. **A board for every curve that can derail you (T111).** Plan §8.5 boarded a curve only where its posted speed (floor √(a_post R)) was under the line speed. With steam driving (T97) the line speed is a rule, not a governor: the engine makes 22 m/s on a full gauge, and a curve posting 18 (frontier's line speed) derails at 21.5, under it, unboarded (a Deep curve of R 225 m derailed at 15 m/s with no board before it). Now every curve on a routable edge that derails under `curves.boardDerailBelow` (22 m/s) gets a speed board at its posted speed, `boardBeforeM` ahead, where no lower curve limit already has one; required, so it stands whatever the tier's signage survival (`CurveBoardTests`).
145. **The crew at work (X1, X2).** The crew moved three ways: standing, walking, running, plus a ladder and a shovel. Everything else a crewmate does (carry, haul a door, wind a brake wheel, heave a hatch, pull the pin, vent, work levers, push the gun, struggle when held, sit at the cannon, fall, mend the boiler) looked like standing about.
    - **The clips live apart from the mesh.** `tools/blender/crew_clips.py` authors them on crew.py's skeleton (the same `rig.human(1.8)`, a 12-triangle stand-in so the file has a skin) into `content/art/models/crew_clips.glb`, and `CreatureArt` merges `<name>_clips.glb` into `<name>.glb` on load (`ModelLoader.WithClips`: bones matched by name, a bone it lacks held at rest, the model's own clip winning a clash). A new clip is a 5 s build, not a re-bake of the crew's high copy and atlas (tools/models/recipes/crew.py). Hands go where the work is by FK reach (`rig.reach`), at the sim's own heights: a crate at `Bodies` carry height, the vent's wheel over its 1.1 m stand, the cannon's tiller and handwheel measured from its seat.
    - **The act is read, not sent.** `CrewActs.Of` reads it off what the snapshot already has: the flags (held, pushing, operating, a shovelful), the surface (a ladder, falling through the air), what `Bodies.CarriedBy` says they're carrying (a body is dragged, anything else carried), the gun they're at (`Guns.MannedGun`), and a timed action's progress with what's nearest it (the firebox, with the repair kit in hand mending, T109, note 150). Presentation only: nothing here is replicated or fed back, so `Crewmate.Act` costs the wire nothing and needn't be deterministic.
    - **At the gun they're sat on its seat.** A manned gun's crewmate is drawn on the roof under the cannon's seat (note 137), facing its way, until the gunner's own controls give the sim a seat to put them in. Standing at the gun to reload sits them too; when the sim seats the gunner, `CrewActs.Seated` is where to take it from.
    - **The tool in hand.** T108's hotbar replicates `Kit` and `HeldSlot`, so a crewmate's held tool is drawn in their right fist (`Crewmate.Holding`; tools/models/recipes/hand_tools.py's crowbar, shovel and wrench, each with its origin at the fist), on the crew rig's `hand_r_weapon` socket (`CreatureArt.ToolGrip`: the haft along the socket's +Y, drooped 0.6 rad the way a wrist lets a bar hang; the shovel held up by its D-grip, the blade trailing). It's put away for the two-handed work (carrying, wheels, levers, the gun), where it would be through what they hold. `dt art clip crew <clip> --tool tool_crowbar` shows it.
    - **Not yet.** The swing has a clip but no reader: a remote crewmate's swing isn't in the snapshot (the host decides blows from intent, App. C.2), so it waits on the melee replicating something to draw from.
    - `dt screenshot --working` stages them (a carrier, a hatch, a brake wheel and a crowbar on car 2's roof; a gunner at the last gun) for the `crew`, `crewside` and `cannonside` views; `CrewActsTests` pin the reading and the seat, and `CreatureArtTests` check every clip loops clean.
146. **Your own arms (X3).** On a keyboard you saw nothing of yourself, a tool in hand or not; the brief asks for "the crew's weapons, held in first and third person".
    - **The crew's own arms, cut down.** `CreatureArt.OwnArms` draws the crew mesh's triangles whose corners all hang mostly from a forearm, hand, finger or thumb bone (`ArmsOf`, cached like any bound asset): the coat's cuffs and the gloves, the same textures the others see you in, and nothing of the body round the eye to clip the camera. It's posed and placed so the pose's eye (the head bone's root plus `EyeOverHead`, crew_clips.py's `EYE`) is the camera.
    - **What they do.** With a tool in hand they hold it up in view, the right fist low right, the wrist turned so the haft stands up and leans in (crew_clips.py `fp_hold`, `fp_walk` walking), following your look; Swing (or Fire, off the gun) plays `fp_swing`, one blow per melee recovery (`enemies.json` melee `swingSeconds`, 0.8 s, the clip's length), back to back while it's held, as the host's swings are. At work (any act `CrewActs.Of` reads off your own state: the shovel, a wheel, a crate) they do what that act's clip has them do, in the world, the same clip the others see. Empty hands at your sides: nothing's drawn.
    - **Not in a headset, yet.** A headset still draws its box fists (`VrHands`): putting the glove on the controller's grip pose wants a check on a real headset's grip axes, and the simulated runtime only gives positions. That's the X3 follow-up.
    - `dt screenshot --view roof --own crowbar [--own-swing 0.37] [--own-walk] [--own-act shovel]`; `CrewActsTests` pin that empty hands draw nothing and a held tool's in front of the eye, turning with you.
147. **The effects pass (X4).** The checklist's effects were boxes or nothing: a car fire of orange boxes, the furnace an emissive box, a muzzle flash and no smoke, no spray, no derailment, no coal falling (greybox boxes), no cold about the Choir, no corruption in the air. All of them are flipbook billboards now, worked out from the time and the sim's state like the train's smoke (no particle state, so a screenshot repeats and a client draws what the host would), in `Effects` (partial: `Effects.Fire.cs`, `Effects.Guns.cs`, `Effects.World.cs`).
    - **Two new flipbooks** (tools/art/texgen/mat_fx.py, cooked by `tools/art/textures.py --only fx_flame,fx_flash`): `fx_flame`, 16 looping frames of noise-eaten flame tongues (the noise is periodic and scrolls once round it, so the loop has no seam), and `fx_flash`, four ragged jet stars of a black-powder muzzle flash.
    - **A car fire** (`CarFire`, App. C.5): smouldering, grey smoke seeps out of the load to the roof and pools there, rolling along under it (a closed car fills from the top down); alight, two ranks of flame tongues on and in the load's face, taller the further it's gone, cinders spiralling up, its two lights flickering, and the smoke thickening black, lit orange from under near the flames. With the art pass's flipbooks only; the greybox keeps its boxes.
    - **The furnace** (`Furnace`): with the firedoor open, flames off the coal bed, a cinder now and then out of the hole, the light thrown into the cab.
    - **A cannon's shot** (`CannonShot`): the flash star and a jet of flame along the barrel for 0.12 s with a street-wide light, burning wad and sparks flung ahead and falling, and the powder smoke, a grey-white bank thrown out along the barrel, braked, rolling and lifting for `ShotSmokeSeconds` (6), left behind by the moving car (the air's still). Timed from the gun's replicated `LastShotTick`.
    - **An extinguisher** (`Spray`): a white jet onto the fire's foot and a cloud billowing there. The charge isn't replicated, so it's read off what is: a fire whose intensity is falling, with an extinguisher carried within reach of it (`GreyboxScene.Extinguishing`, held 0.4 s so it doesn't blink between ticks).
    - **The coal chute** (`CoalPour`): lumps falling the spout's drop under gravity, a dust curtain, a cloud where it lands.
    - **A derailment** (`Derailment`): the sim just stops the train, so the scene times it from when it first sees `World.Derailed`: sparks off every truck for a second and a half, ballast dust rolling out along the train and settling, the boiler bursting steam out of both sides and going over to a long hiss. The cars stay on the rails (no derailed pose yet).
    - **The Choir's cold** (`ChoirCold`): a faint mist sinking off each bell and frost glittering down from it; faint by design (§26 "not neon"), seen in the lamp, lost against a bright sky.
    - **Corrupted air** (`Corruption`): motes in world-anchored 6 m cells round the eye, so the train runs through them: ash drifting down over the colliery towns (`industrialRuin`), sick spores rising over the tar ponds (`contaminatedMarsh`), by the line plan's biome at the train (`WorldArt.BiomeAt`).
    - **Staging:** `dt screenshot --threats --view fire [--spray]`, `--view firebox --firedoor`, `--view cannonside|gun --muzzle --shot-age s`, `--route local:3 --coaling --view coaling`, `--view trackside --derailed s`, `--threats --view choir`, `--view roof --air ash|spores`. `EffectsTests` pin each: what draws when, the shot's smoke left behind the car, the air anchored to the world.
148. **The train's stores (X5, first half).** The props the crew live with (GDD §12 "where powder and shot are stored, where the repair kits sit, where tools and fire extinguishers hang") were a hand lantern for everything small, or nothing. They're modelled and baked in one recipe, `tools/models/recipes/train_stores.py`, and placed from the sim's own state:
    - **Toys** (`toy_bear`, `toy_horse`, `toy_doll`; App. C.4): a rag bear with one button eye, its mouth sewn shut and its belly split; a pull-along horse with the paint flaked off and an ear snapped; a little porcelain-headed doll, the Track Doll's own kind. A toy body picks one by its id. Each stands with its foot 0.15 m below its body's middle, on the floor the body rests on.
    - **The extinguisher** (App. C.5): a copper soda-acid extinguisher with a sight glass up its front. Its charge (`Body.Charge`, to the percent, so recharging doesn't stream every tick) and a crate's cargo now ride a ninth header field on the body record. The game draws the charge as lit water standing in the glass with a red float (`SceneArt.Charge`), readable across a car. The water is a hair wider than the glass because the glass is opaque.
    - **Every car's mount** (`extinguisher_mount`): a red board reading FIRE / PUT IT BACK, with a floor cradle and a shoulder hoop, at `World.ExtinguisherMount`. That spot is now a function of the car's shape: in the guard van it steps past the tool lockers, where the extinguisher used to spawn inside them. An extinguisher back on its mount is drawn standing in the cradle, its glass to the room.
    - **Powder and shot** (`shot_locker`, `powder_bag`, `cannon_ball`; App. C.3 "stocked at departure; carried to the cannon"): an iron-bound chest in the gun car's front corner, lid propped back against the wall, with twelve wells of shot and six powder bags. They empty as the gun's `Ammo` falls against combat.json's guns `ammo` (`SceneArt.Fittings`).
    - **Car numbers** (§32 "car four"): the vehicle's id, stencilled in white (tools/art `stencil_numerals`, an alpha-tested 5 x 2 atlas of stencil-cut digits). It's painted on both sides at the front end and on the roof at each end, beside the walk, the right way up for someone walking on (`TrainKit.CarNumber`). A car keeps its number when the cars ahead of it are cut away.
    - **The repair kit** (`repair_kit`) is placed by note 150, as the sim's item it is.
    - **Crates carry their cargo** (`Body.Cargo`, set from the facility's when a site is stocked), so a facility's crate comes in its cargo's case (`SceneArt.Freight`: food and grain in sacks, ammunition in ammunition boxes, heavy, salvage and ore in parts crates); a stop's loot crates, with no cargo, keep one by their id.
    - **Staging:** `dt screenshot --stocked [--charge c] [--lantern]` stocks the train as it leaves (World.Stock, Guns.Arm). Use it with the views `stores` (the guard van's stores and toys), `locker` (the powder and shot) and `mount` (an extinguisher on its board). `--lantern` is a hand lamp under the eye, for a dark interior. `TrainStoresTests` pin the props' sizes, the charge in the glass, every room's mount and the locker emptying with the stock.
149. **The L0 sweep (everything on the checklist at least a prototype).** The demo's missing rows each get a first version, rated L0 where it's only a prototype:
    - **Crew clips** (crew_clips.py): `gap` (balancing on the coupling plate, feet wide, arms out low), `extinguish` (the extinguisher on the hip, aimed), `lantern`/`lantern_walk` (the hand lamp held out low and swinging with the step), `haul` (down on a knee hauling at a friend's collar). They're read off the state like the rest (`CrewActs`). Carrying a lamp gives `lantern` and carrying an extinguisher gives `extinguish`. Standing on a coupler gives `gap`. A held crewmate within 1.6 m gives `haul`, read from the crew list `NetPlaySession.Crew` now passes in. A carried extinguisher is drawn where the hands have it, unlifted.
    - **Loads by cargo** (`TrainKit.Load`, a piece apart from the car's body whenever the scene has the vehicle): the cargo solids stacked with the cargo's cases. Food and grain come in sacks, ammunition in ammunition boxes, chemicals in carboys (new `freight_carboys`), ore in tubs (new `freight_ore`), heavy freight in heavy crates, and salvage in parts crates. Goods and livestock keep the crate stack. The load still fills the solids whatever `Vehicle.Load` says, because the collision does.
    - **The line's boards and cranes**:
      - The route's sight boards (speed, LOW for a tunnel, MAIL with the crane's side, END) are SignKit's boards, the plan's own, with a new `mailDrop` face. While the lamp reads them they glow back (instance glow 7).
      - The mail crane is modelled (`mail_crane`: post, footing, two arms and their clamps, step irons, flag), with the bag (`mail_bag`) hung between its clamp sockets and tinted by what's in it.
    - **The dawn** (look.json `atmosphere.dawn`): over the run's last `leadSeconds` of dawn clock, the fog pales, the moon's light goes over to a low sun from the east, and the dark pockets fill (`Look.DawnOf`, `Look.Dawn`; `dt screenshot --dawn t`).
    - **The Whistler's nest** (`whistler_nest`): a scraped hollow with sleepers leant over it, a coat and boot, a smashed lamp, picked bones and pale strands. Its position isn't replicated, but the Whistler stands on it from the moment its run is done (enemies.json `nestDistance` at `runSpeed`), so it's drawn under the Whistler from then (`dt screenshot --threats --whistler nest`).
    - **Store art at L0** (tools/art/store; out/store, not shipped): `icon.py` makes the headlamp's ringed glow and a stencilled DT, in content/art/ui/icon.png for the window (`Window.SetIcon`) and in the sizes Steam asks for. `capsule.py` sets the title over a game frame at Steam's capsule sizes. `screens.sh` renders eight 1920x1080 shots of moments the game has and runs both.
    - **Staging:** `dt screenshot --cargo food,chemicals,... --view inside`, `--route frontier:7 --at 1620 --cam 1631,0.2,2.4 --target 1637,3.0,2.4` (a crane), `--dawn 1`. `LinesideArtTests` and `CrewActsTests` pin them.
150. **The repair kit, an item (GDD §12, App. D.7).** "The repair kit is an item, not a station. The engineer is whoever picked it up ... when they die on the roofs it's lying in car four and someone has to go and get it." It was modelled (note 148) and unplaced, every Holdout breach was loud, and the cab's wrench mended the boiler, so the engineer was nobody in particular.
    - **What it is.** A loose body, `BodyKind.RepairKit`, carried in the hands like the extinguisher, not a hotbar tool. Being a body is what makes it a thing in the world: it's dropped where its carrier dies (`Bodies.Handle`, as everything carried is), it lies in whichever car it fell in until someone fetches it, and it replicates as every body does. A tool in the hotbar would come back out of a Holdout with its owner (T108), which is exactly what the GDD says it doesn't do.
    - **Where it is.** One (train.json `kit.repairKits`) on the floor of car 1 (`kit.repairKitCar`, counted back from the engine), just inside its front door on the right of the aisle, ahead of the load (`World.RepairKitStowage`; `dt screenshot --stocked --view kit`). A walk from the footplate, not in it: someone leaves the cab for it.
    - **It mends the boiler (T109).** Playtest direction: "leave the boiler to the kit to elevate the role". A ruptured boiler is mended only by holding Use at the firebox with the kit in hand, for `repairSeconds` (25); the wrench is a tool to swing and nothing more. What a player carries is the host's (`Bodies`), so the host sets `PlayerFlags.RepairKit` each tick from it, and `CrewActions` mends off the flag: a predicting client mends exactly as the host does. Use there works the boiler, not the hands (`Bodies.Handle`'s `keep`, which doesn't take the press, so the mend starts the same tick on host and client).
    - **The fireman fetches it** (`KitRun`): out of the cab's back on the left, down the tender's gangway, over the footplate and the plate, the door opened, in, the kit taken, and the same way back to the firing spot and held there. The way is worked out afresh each tick from where the bot stands and whether it has the kit (it never waits on a step it's left behind). The driver goes only with nobody else in the cab. Only a kit in the engine or car 1 is fetched: further back it's a crewmate's to bring (the HUD says which car it's in).
    - **What it does at a Holdout.** Carried to a lit prison car or halt lockup and Use held at the door, it opens the lock: `holdouts.json` `open`, 6 s and no noise, against the smash's 3 s at cannon level (D.7). Use there works the lock, not the hands, so it stays in hand (`Bodies.Handle`'s `keep`). At a shelter's barricade it's no help: that's pried, loud, and the kit stays in hand. Put down part way through, the breach starts over as a smash: half picking a lock is no start on smashing it.
    - **On the wire:** the Holdout record gains a fourth field, `Quiet` (the breach is the kit's), so a client's HUD counts the kit's 6 s, not the smash's 3. Old snapshots without it read as loud; the protocol number stands, since a record's field count isn't part of the layout.
    - **Also:** a player who drops out (D.2) now lets go of what they were carrying. Until now it stayed held by nobody, hanging where they stood, and the train has the one kit.
    - **Not built:** the campaign's "repair kit charges" upgrade has no effect yet (campaign.json `repairKit`).
    - **Verified:** `HoldoutTests` (19, 7 new): the kit opens a lock in 6 s silently, still in hand; it's no help at a barricade; put down part way it's a smash from the start; it's lying where its carrier died, in reach; a drop-out lets go; a client sees the quiet breach; the train leaves with it in car 1, guard van and all. `WrenchTests`: the wrench doesn't mend the boiler and the kit does; the kit rides just inside car 1's front door, clear of the load and the doorway; the fireman bot fetches it from car 1 and mends it.

151. **The Choir, rarer and answerable (T113).** Playtest: "annoying … they show up WAY TOO OFTEN and I have no counterplay". Playing alone, the likely cause was the mic: an open mic's level fed the loudness meter, and with nobody to talk to it heard the game's own train through the speakers (it went quiet when the train slowed).
    - **Alone, only Talk is loud.** With no other human in the night (`NetPlaySession.Alone`: bots don't listen), an open mic counts only while Talk is held. With others there, your voice counts as before.
    - **Slower, then a rest.** `buildSeconds` 40 (was 25) of the meter held loud to gather; driven off by quiet, it rests `restSeconds` (300) before it can begin again (`ChoirState.Rest`, replicated). It still takes at most one a run.
    - **Seen and said.** A noise meter above the hotbar: the crew's loudness against the threshold (the tick), and how far it's gathered under it in red. "THE CHOIR IS GATHERING: GO QUIET" once it's a quarter gathered; "THE CHOIR IS HERE: SILENCE" while it's here.
    - **Heard as music.** Its voices come in only past a sixth gathered (a moment's noise isn't a telegraph), and each one takes the next tone of a minor chord with its ninth (`GameAudio.ChoirChord`), not a random detune; slower breath and vibrato (`choir-voice.json`).
152. **A Holdout's call goes to a bot that can get down (T115).** Playtest: "none of the bots are coming to save me when I call out from a Halt Lockup. They're just standing at a door". The nearest bot claimed the breach (`Heed.Holdouts`), and the nearest was often in a car warming up; `StopHand.GetDown` has no way out of a car, so it stood at the car's door, and the claim kept everyone else away. Now only a bot on the ground, the engine's deck, a roof or a coupler takes it; one inside drops it to the next (`ABotWarmingInACarLeavesTheBreachToOneThatCanGetDown`).
153. **The gun's seat (T112).** Playtest: "a gun seat with its own controls". The gun fired wherever the view was, from anyone standing at it. Now only a gunner in its seat (`PlayerFlags.Seated`) fires it, and fires where the barrel points.
    - **In and out.** Use pressed standing still at a loaded gun sits you in it (the client sends `PlayerActions.Seat` on the press). Use held at a gun waiting on its reload still loads it, seated or not, and Use with walking still pushes it (not while seated). Jump in the seat gets you up without a leap. Seat only ever sits (a host repeats a lost intent; a toggle would stand you straight back up). The gun going (pushed away, its car cut) or being seized gets you up too.
    - **Its own aim.** Seated, the view is held inside the gun's arc (its traverse either side of its facing, its pitch limits), and the gun is laid after it at `traverseDegreesPerSecond` (70) and `elevateDegreesPerSecond` (35): the mouse leads, the carriage follows (`Guns.Sit`). `GunState.Traverse` and `Elevation` replicate, so everyone sees the gun turn, and the cannon's carriage and barrel are drawn by them (note 137).
    - **The seat is on the carriage.** Your feet stay behind the breech as it turns (`Guns.SeatAt`, `seatBehind` 0.75 m), and the eyes drop to the shield's slot (`Eyes.Seated`, 1.26 m).
    - **Bots** sit before they shoot, fire only once the barrel is within `laidDegrees` of the mark (`Guns.Laid`), and get up before going anywhere else.
    - **Tests.** `GunTests`: the traverse stop holds the view and the gun, the gun follows at its pace and fires where it points, only seated fires, Jump gets up. `TheFlankCannotBeCoveredFromEitherGun` now lays the barrel on every trial aim and reloads between; before, every trial after the first shot was refused for want of a reload, so it tested one aim.
    - **Headless.** `dt screenshot --view cannonside --gun-laid 35,15`.
154. **Every named halt has its village (T114).** Playtest: "a stop with nothing there". A halt or dead town on the main line gets its platform, its station and whistle boards and its name on the HUD from the plan, and its village (finds, a Holdout) from `PlanStops`. That took the village only where the main line was straight to the yard's exact test (curvature under 1e-6). The generator's straights carry a residual bow of 160 to 700 km radius, a few cm over a halt, so about one halt in ten failed it and was left a signed stop with nothing at it. A village now takes a stretch under 1e-5 (radius 100 km, under 12 cm over 300 m); yards keep the exact test. Across 36 nights on three tiers, every main-line halt and town now has its village (`SettlementStopTests`). Towns on alternates still have none: features are along the main line only.
155. **A village's houses are walls (T114).** Playtest: "collisions". Nothing at a stop was solid: `PlayerMotor.Collide` only knew the train. Now `StopWalls`, built from the route alike on every machine (set on the train with the run), stands each part of a village house, barn and outbuilding as an upright box in its own frame, found by a coarse grid, and the motor pushes a walker out of it like a car's solid. Not the yards' sheds (crates are loaded at them), not a Holdout's building (a freed crewmate comes back out of it), not a well. The house models have no interiors to search, so a find in a house is put out on its step: a little out from the wall facing the line (`StopWalls.Doorstep`). `StopWallsTests`: a walk at a house's middle never enters it (it walks clean through without the walls), and every step is clear of every wall. Enterable interiors with searching (level-design P12) want modelled interiors first.
156. **Solo nights were planned for four (T115).** Playtest: "in the cab piloting the train and suddenly I can't move and then a few seconds later I die", and a death screen with no cause. A local host with no bots told the director its crew was `expectedCrew`, 4, so the threats that need a crew (Tippy Toesie, Soot Children, the Gaunt: `minCrew` 2 or 3) and the budget for four came for one. Tippy Toesie took the driver from behind in the cab. Now:
    - **The crew it's planned for.** A local night is planned for you and your bots; hosted online, for the friends expected too. And the director's gates go by who's actually in the night once it's under way (`Director.Present`), so a 4-player estimate never lets a crew-only threat at one player.
    - **Driving is working.** A driver in the cab of a moving train isn't idle, so isn't Tippy Toesie's mark.
    - **Held, the HUD says so.** "SOMETHING HAS YOU", and alone, "HOLD [E] TO STRUGGLE FREE" (the solo rule, note 92); with a crew, shout for a friend.
    - **Every death says what did it.** The v1.1 causes (`Eaten`, `Suffocated`, `Devoured`, `Drained`, `Carried`, `Seized`, `Uncoupled`) had no line, so the screen said only DEAD. `Hud.DeathLine` covers every cause (`EveryDeathSaysWhatKilledYou`).
157. **The Track Doll, quieter (T115).** Playtest: "never use that chirping noise, it's so loud and crazy". Its two warbling 3.9 and 4.7 kHz tones gated at 7.5 Hz are now two soft glassy tones (3.1 and 4.0 kHz) swelling and fading on slow breaths of their own, 7 dB quieter, still in its 3-6 kHz band and over everything else there (`dt audio render`: 8.4 dB over all at its worst listener).

158. **What nobody answers goes (T114).** Playtest: "where are all the monsters". A solo night's director held back 673 of its 1,200 s out on the line "at the cap": the threats nobody answered (a Climber settled in a car nobody went into, hounds trailing a train nobody shot from) stayed engaged all night and filled the four places, so nothing new came. Now a threat that counts against the caps (`Director.Engaged`) and goes `lingerSeconds` (120) with nobody alive within `lingerRadius` (12 m, a car's length) of it, holding nobody, gives up and goes (`World.Unmet`, host, once a second). A car fire burning in a car nobody's in goes out the same way: only what has someone in its grip is spared. So does anything stuck telegraphing or committing for 1.5 × `lingerSeconds` (a Climber scrabbling at the cab's gap all night, within reach of the driver but never getting in). What lies in wait (a Dragger under a car's edge) doesn't count against the caps, so it may still wait all night. The harness's threat report has a `director` tally: each second out on the line by what the director did, sent or why it held back.
159. **Where friends join (T114).** Playtest: "how is she supposed to join if we're on the same wifi?". Hosting for friends, the HUD's link panel says "FRIENDS JOIN AT 192.168.x.x:27450": the first IPv4 address of a network interface that's up and has a gateway (`NetPlaySession.LanAddress`). A night hosted only for its bots (bound to the loopback, `UdpTransport.LocalLoopbackOnly`) says nothing.
160. **Host a night, join a night (T116).** Playtest: "we need to host a lobby and wait for players to join with a P2P set up where the host machine is hosting the session", like Lethal Company and PEAK. Those games have one player's machine host (listen server, no dedicated servers), friends find the game through a lobby list or an invite, and everyone waits in a safe space (Lethal Company's ship in orbit, PEAK's airport) until the host starts. Here:
    - **The title** has QUICK NIGHT (solo), HOST A NIGHT and JOIN A NIGHT.
    - **Hosting** picks the night (the same options) and OPEN THE LOBBY. The host's machine runs the session on UDP 27450, as it did, and now also says so on the local network: a `LanBeacon` (Ballast.Net) broadcasts "BALLAST-LAN/1", the game, protocol, port, host's name, night and how many are aboard to UDP 27451 every second.
    - **Joining** lists those games (`LanBrowser`, gone after 4 s unheard; a different protocol version shows and can't be picked) above the old address entry, for a game over the internet or a network that drops broadcasts.
    - **The yard is the lobby.** The host's HUD shows the crew aboard, how friends join, and "EVERYONE IN? DRIVE OUT OF THE YARD": the night starts when the host leaves (the yard was already the safe start, `RunPhase.Yard`).
    - **Joining doesn't freeze the window.** Building the night (or connecting to it) ran on the main thread with nothing drawn, so a slow join read as "not responding", and a failed one crashed the app. Now it runs off the main thread behind a loading screen, and a failure goes back to the menu with why.
    - Steam lobbies and invites (T20) stay behind `--steam` until the builds ship the Steam libraries.
    - **Cross-play** between Linux and Windows needs both to generate the same line from the same seed (a joiner regenerates it and is refused on a fingerprint mismatch). CI now runs `dt linegen prints` on both and the `crossplay` job diffs them; its first run caught the platforms' trigonometry disagreeing (note 163).
161. **The Track Doll giggles (T118).** Playtest: "replace the track doll sounds with occasional giggling of a little demon boy". While it punishes it plays a one-shot giggle (`doll-giggle`, 1.25 s) every 7-16 s, pitched 0.92-1.10, in place of the loop. The giggle is five falling syllables of a child's voice (a saw at 520 Hz through formants at 420 Hz, 2.9 kHz and a strong 3.9 kHz), breath in the 4.4 kHz band, and a low growl an octave and more under it (210 Hz, trembling at 31 Hz). It keeps its 3-6 kHz tell over the chaos (`AudioTests`).
162. **The derailment is physics (T117).** Playtest: "a derailment needs to be a full on dramatic cinematic derailment ... the full physics carnage of a derailment simulation". From the tick the train comes off (`World.Derail`), each car is a rigid box (`Train/Wreck.cs`, tuned in `tuning/wreck.json`).
    - **The physics:** gravity, impulse contacts with the terrain under it (friction, and the bank's slope), couplings as slack chains that pull and snap past `couplingBreak`, and cars shouldering into each other as capsules. Six substeps a tick.
    - **The first car off** (the engine) is thrown off the outside of its curve: sideways, slewing and rolling.
    - **The rest come off one after another** down the train, `dropInterval` apart. Until its wheels drop, a car rolls on, held to the rails, and shoves into the cars ploughing ahead. That's what folds the train on a straight line, where nothing else throws it.
    - **A car still upright, dragged sideways, digs its bogies in** (`digIn`) and trips over them.
    - **The cinematic beat:** the first 2.5 s play at 0.35 speed.
    - **Host and clients:** the host steps the wreck, and clients get each car's pose (`RecordKind.Wreck`). It isn't predicted, so it needn't be bit-exact; it's seeded anyway. The cars' frames are the wreck's poses, so whoever's aboard goes with them.
    - **Effects:** sparks where steel drags on the ground, dust, and the engine's steam and smoke.
    - **The cinematic:** for `cinematicSeconds` (12) the flat camera leaves your eyes and circles the wreck, closing on the engine end (`Views.Wreck`; in the headset your own eyes stay yours). The death screen and RUN LOST wait for it to end (`IPlaySession.WreckCinematic`, timed per client from when it first saw the wreck). Until then the game had never loaded `wreck.json` (only `dt` did); `NetPlaySession` now loads it.
    - **Sound:** a crash (`wreck-crash`: a ground thud, torn steel, a clang) wherever a car's velocity jumps (it hit the ground or another car), and the grind of steel through earth (`wreck-grind`) at the fastest car still sliding, louder the faster it goes. Both are read off the poses, so a client hears what the host simulates. `dt audio render --scenario wreck` renders it from beside the line (`AudioTests.AWreckIsHeardCrashingAndGrinding`).
    - **Checking it:** `dt wreck` reports it car by car; `dt screenshot --wreck s` shows it at s seconds; `WreckSessionTests` runs one through a session.
163. **The same trigonometry on every OS (T116 cross-play).** The `crossplay` job's first real run failed: Windows and Linux generated different line plans from the same seed for three of its four nights (the terrain prints agreed). `Math.Sin`, `Atan2` and the rest call the platform's C library, the UCRT on Windows and glibc on Linux, and those disagree in the last bit for about one argument in a hundred. The plan's fingerprint hashes every double it holds, so one ulp anywhere splits it, and a Windows player couldn't have joined a Linux host. The terrain print survived only because it rounds to the millimetre.
    - **`DMath` (Ballast.Core)** is fdlibm's sin, cos, atan, atan2, asin and acos (what Java's StrictMath uses), written with nothing but IEEE-754 + - * / and square root, which every x64 and arm64 machine rounds the same; the JIT never fuses a multiply-add on its own. Under 1 ulp from the true value, like the C libraries; `Tan` is sine over cosine (2 ulp), and `Pow(x, int)` squares. The argument reduction is fdlibm's for |x| up to 2^19 pi/2; past that it takes off whole turns first, inexactly but identically everywhere.
    - **The whole sim uses it,** not just the generator: the validator drives the real train, and clients predict players and the train with the same code, so a Windows client of a Linux host would drift too. `DeterministicMathTests` fails on any C-library call in DarkTerritory.Sim (Sqrt, Abs, Floor, Round and the like are exact by IEEE-754 and stay). Presentation keeps `Math`.
    - **Checked both ways:** `DMathTests` holds it within 2 ulp of the platform's over 200,000 arguments each and pins 56 results to the bit, through every branch, on both CI runners; the `crossplay` job compares whole plans. `dt linegen prints` now also gives a checksum per part of the plan, so a mismatch says where.
    - **Protocol 4:** the same seed now makes a slightly different line than a 3 did, so mixed builds are refused as versions, not as a fingerprint mismatch.
164. **Stranded, and bodies keep their tools (GDD v1.4 §23.2, App. D.2, E.9; T119).** GDD v1.4 added a fifth way a night ends: **Stranded**, a ruptured boiler with the engineering kit lost. The build's engineering kit is the repair kit, the carried item that rides in car 1 and mends the boiler (note 150, a playtest call after the GDD's "wrench in the cab"); §12 and E.9 in `docs/design/gdd.md` now say so.
    - **Where the kit is** (`EngineeringKit.Where`, each tick, in `Run.Kit`): carried by a living crewmate, lying somewhere, or lost. It's lost only when the Territory has it, per §23.2's table: its car is `Vehicle.Taken` (set when the Car Hugger finishes a car, and when the Passenger rolls the caboose away; `CarTaken`); its car was cut loose on the main line and no living crewmate is within `stranded.kitLostBeyond` (400 m, D.13's release distance; `LeftBehind`); or it's gone from the world (`Taken`). A car on a branch, a facility's pad or siding, is parked, not lost. A kit lying on the line, however far back, is never lost: somebody walks. With more than one kit aboard, every one has to be lost. A night never stocked with a kit (`World.KitStocked`) can't be stranded.
    - **The end.** If the boiler is ruptured and the kit has been lost for `lostForSeconds` (1 s, not in the GDD, so a hand-off between ticks can't strand a night), the run ends as `RunEnd.Stranded` once the engine is below `stopBelowSpeed`. That gives the crew the whole coast. Losing the kit without a rupture ends nothing. Once the boiler has ruptured with the kit lost, the HUD says so: "THE REPAIR KIT IS GONE: STRANDED WHEN SHE STOPS".
    - **Settlement:** like a failed night (no cargo, no body refunds, a crew-loss fee for each death), plus the dawn freight's tow: `stranded.recoveryFee` (0.5) of the tier's per-car value (`RunReport.Recovery`). The living come home with the train wherever they're standing, and so do the cars still coupled. The end screen shows STRANDED, how the kit was lost, and the bill.
    - **A body keeps its tools** (D.2: "it keeps everything the player was carrying"). A ragdoll keeps its player's hotbar (`Body.Tools`), which replicates in the body record's ninth field, unused by ragdolls (protocol 5). Lifting a body moves its tools into the lifter's empty slots; a duplicate stays on the body (`Bodies.TakeTools`). A freed player comes out with the night's starting kit (player.json `kit`, set on the Holdouts by the host), not their old hotbar.
    - **The Gaunt takes corpses** (D.9: "a body is valued at its refund when enemies rank loot"). `Bodies.Value` gives a ragdoll 5, above the medicine and below a living child (§23.1 "the Gaunt choosing a corpse over the medicine"), and Followers nest where bodies are.
    - **E.9's outro.** Nobody died, so there's no opera. For `wreck.json` `stranded.rackSeconds` (1.5) the camera looks at the bare boards in car 1 where the kit is kept. Then, for `pullBackSeconds` (6), it pulls up and back over the stopped train to a high wide while the lamps go out one by one, the last car first and the engine and its headlamp last (`Views.Stranded`, `StrandedLampsOut`). The clerk's line runs as a subtitle over the pull-back, and the end screen waits for it (`IPlaySession.StrandedOutro`). Seen from outside, a car's lamp lights only its inside, so for the outro each lit car spills a glow out of its roof (`GreyboxScene.RoofGlow`). The fog is pushed out to twice the shot's distance (`Views.CinematicFog`, E.4 O12's light rig). `dt screenshot --stranded s`.
    - **Not yet:** E.9's skip vote and bookmark.
    - `StrandedTests` covers each way of losing the kit, each way of not losing it, the settlement, and the tools on a body. `WrenchTests` checks that tools replicate, and `HoldoutTests` that a freed player has the starting kit.
165. **Failure attribution, the incident report, and names (GDD v1.4 App. C.9, D.12; T120).** The run's end said DELIVERED or RUN LOST and one line. Now it's the incident report: every death with who, where, and a cause line in the settlement clerk's voice beside its fee and refund; rescues; the boiler; cars lost; how it ended; and the night's money. "Swallowed by the Car Hugger on the roof of car 3 at Hollin Halt. Nearest crew: Priya, 14 m. Fee 350. Body not recovered."
    - **The log** (`World.Attribution`, host-side, `Run/Attribution.cs`) is written at every GRAB start (`Enemy.Grab`), every death (the tick its body goes down, `Bodies.OnDeaths` → `IncidentLog.Death`), every rescue (`World.StepHoldouts`: the Freed event now carries who breached), the boiler's rupture and the run's end (`Run`). It never touches money, progression or the director.
    - **Contributing actions** come from the crew inputs C.9's table names, as the host sees them: the driver is whoever last worked the cab's controls (`HostSession`), the fireman whoever last added coal or opened the vent (a firebox that grew, or a vent newly open, across `CrewActions.Apply`), each car's lamp-lighter and each vehicle's coupler-puller (a car that left the engine's rake during someone's crew action), the crane operator, and the last living holder of the engineering kit. Each death cause maps to its row: the throttle and speed for the train's own dangers (left behind adds how far back), the crane for a dropped load, the lamp for a fire, who last fired for the Stoker, and for any other GRAB death the nearest living crewmate and their distance. `AttributionTests.EveryDeathWritesExactlyOneRecord` covers every `DeathCause`.
    - **Cars lost** are read at the end, not as they're cut, so a car cut at a facility and coupled back isn't counted: each rake still apart from the engine is a line, with who pulled its coupler (or what took it) and what's inside, freight and bodies.
    - **Names.** A joiner sends a Hello with its name (settings `playerName`, else the online name, else the system's); the host keeps them (`World.Names`) and sends everyone the table whenever it changes. Bots are named from player.json `botNames`. Protocol 6.
    - **The report reaches clients.** It was the host's alone (`Run.Report`), so a joiner's run-end screen had nothing. The host now sends it once, as Brotli-compressed JSON in reliable numbered chunks under the 1200 B datagram (`Messages.ReportMessages`), and clients mirror it.
    - **Roll call is verbal** (v1.4 open question 2: "there is no aboard indicator for the conductor or anyone else"). The held-Q roster now lists the session's crew by name with who's speaking, and nothing else: no locations, no dead marking, no "N ABOARD", and no Passenger line. Reading: the Passenger's roster line and the NOT HEARD marks were tells the GDD no longer has ("the only tell is silence"), heard by ear. The yard lobby still lists who's joined (that's before departure), and the corner panel says CREW OF n.
    - **Not yet:** bookmarks beside the lines (D.12), the vote, commendations, and the clerk reading it aloud.
    - `dt screenshot --hud --report [derailed]` stages a night's report through the real log and formatter; `--roster` the roster.
166. **The crew at work, the rest (the art checklist's crew rows to L1).** Note 145's clips covered the crew getting about and most of the train's jobs; the breach, the freed, the driver, the rescue over an edge and the step across the gap were standing about. All new clips are crew_clips.py's (Blender 4.0 from apt in a cloud session: `apt-get install blender python3-numpy`), read off the state by `CrewActs` like the rest.
    - **Breaching a Holdout (App. D.7):** `smash` (wound up overhead, brought down two-handed on the hasp, 1.25 a second), `pry` (the bar bitten in at the chest, the weight hung back off it) and `pick` (down on a knee at the padlock with the repair kit's picks, small twists). Who's breaching isn't replicated, so whoever stands at the door of a Holdout being breached is at it, by its kind and `Holdout.Quiet`.
    - **Freed (D.8):** `getup`, played once from its start: off the floor, a knee, up. It plays while the freed player stands where the Holdout put them. Acts played once from their start get the time since the act began (`SceneArt._crewActSince`).
    - **At the controls (GDD §12):** `drive`, and `whistle`, the left hand hauling down the whistle cord. The cord is now modelled in the cab (`TrainKit.WhistleCord`, over the driver's head behind the regulator) and drawn hauled down while a crewmate's whistle blows (`CrewActs.CrewWhistling`): the Whistler's blast leaves it hanging, which is its tell. The hands go onto the levers where they are, the regulator at its notch and the brake valve (`Crewmate.Reach`, IK'd like a headset's).
    - **The rescue over an edge (A.1, A.4):** `haul_up`, crouched over the edge for a collar and heaving back and up, when the held friend is half a metre or more below; `haul` otherwise.
    - **The gap (§32):** `gap_step`, short wide careful steps with the arms out, while moving on a coupling plate; `gap` stood on it.
    - **The extinguisher's bracket (C.5):** `take_down`, lifted off it into the hands, while it's in hand by its mount.
    - **The hand lamp (§31):** hung from the fist by its ring (`CreatureArt.Crewmate`'s `hanging`), upright whatever the wrist does, so it swings with the walk; its glow and its light go where it hangs (`SceneArt.LampInHand`, a frame behind: the practical lights are laid before the crew are drawn). The body isn't drawn where the sim holds it.
    - **The cut coupler (T91):** tools/models car_gear's `coupler_open`, the knuckle swung 80 degrees open on its pin, the lock lifted, the cut lever thrown and the hose parted, hanging. The couplers aren't baked into the car bodies any more where the models are built: `SceneArt.Car` draws each end's, shut, or open at an end that's been cut (`SceneArt.Cuts`: nothing coupled there, and the car it was cut from still about in another rake; the train's own ends stay shut).
    - **Staging:** `dt screenshot --act smash,pry,pick,getup,haul_up,lantern` (a row of the crew down car 2's roof at those acts; `--view crewside` or `roof`), `--cut 2 --lantern --view cut` (the open coupler), `dt art clip crew <clip>`.
    - **Verified:** `CrewActsTests`: every pose has a clip of its own (through `CreatureArt.ClipOf`); at the controls the hands are on the levers and the whistle's pose is only the crew's; a friend below is hauled up; a carried lamp is drawn in the fist.

167. **Survivors, breach states, loads, nests, the mail catch, iron bridges, debris, causeways, engine damage, the cold and burnt cars (the art checklist's rows to L1).** What was in the sim and wasn't seen, or was seen wrongly.
    - **The freed survivor (App. D.8):** a freed player plays the rest of the run as the Holdout's occupant: `survivor_prisoner` (penal greys, banded, the number patch, bare-headed and bare-handed) or `survivor_wildlander` (patched hides and furs, sooted), both the crew figure redressed (tools/models/recipes/survivor_*, `crewfigure.Style(hats: false)`: the bare scan with no cap or helmet) on the crew's rig and clips. Who they are is read off the sim, not sent: `CrewActs.SurvivorOf`, the last Freed Holdout whose occupant is that player, typed by the Holdout's kind (a shelter's wildlander; a prison car's or a lockup's prisoner). The child rescued from a lockup is drawn apart, as note 166's.
    - **Breach states (D.4):** the Holdouts' entrances aren't baked any more but drawn as each stands (`WorldArt.Entrances`): the prison door shut and padlocked, the padlock smashed off or picked and hanging, the door swung in; the lockup's gate; the shelter's barricade boarded or pried off, its doorway dark. `dt screenshot --route r --holdout n [--freed]`.
    - **Loads (GDD §19):** the comet in its cradle, lit its own sick green (`SceneArt.CometGlow`: point light and glow, nothing like a lamp's); goods a mix of crates, timber, medicine chests and parts; livestock a car of sheep (tools/blender/sheep.py, clips idle/shuffle/bleat/startle), a dozen penned down the load side, each on its own beat from its index (a bleat a round, one in five startled): the noise is seen. Coal has no cargo kind (it's the tender's), so there's no coal load.
    - **The Follower's nest (A.5):** `follower_nest`, built up under it as it swells over the loot, the Follower riding on top.
    - **The Whistler's nest (A.4):** the sim already carries its catch off perpendicular to the line (`NestDistance`); the scene sets the carry on the land (`PlayerMotor.GroundAt`) and draws the drag trail from the ballast's shoulder to it (`GreyboxScene.DragTrail`: two heel furrows, breaking and wandering, scuffs where they fought it, a scrap of coat or a glove; from the creature's id, so the same everywhere). There it rears over its catch in its `watch`. `--whistler nest --view trail|nest`.
    - **The mail catch:** the crane's arms are their own piece (`mail_crane_arm`), so once a bag's caught (`GreyboxScene.DropCaught`, the host's `Lineside.Caught`, timed from the first frame that sees it) the bag's snatched in through the door with the car and the arms drop to the post with a bounce, as a real crane's do: a worked crane reads empty from up the line. The crane itself now stays drawn after the train's by (it was culled with the bag). Only a solo night has word of the catch (`Caught` is the host's: a client's world never sets it), so there the bag hangs until it's taken and a missed one's still there; with no word, it goes once the engine's by, as before. `--route r --mail s` (`--dawn 1` to see it by).
    - **Iron bridges (§30, linegen plan §12.3):** the plan's bridge types are drawn as built (`WorldArt.SpanType`): a girder viaduct (`StructureKit.GirderBay`: riveted plate girders on braced steel towers, an open deck with railed walkways) and a through truss (`TrussSpan`: a Pratt span per bay on stone piers, the train running inside its cage, the overhead bracing clearing the roof crew); the plan's trestle is the timber trestle whether or not it's weak; the viaduct stays stone. `--route r --structure girder|truss|trestle|viaduct`, `dt art show girder-bay|truss-span`.
    - **Track debris (v1.1 §22):** the old Sleepers' writhing ties are gone: debris is inert, one of three heaps by the hazard's id (`DebrisKit`: a pine down across the rails, WorldKit's spruce laid on its side from its torn-up root plate; a rockfall; a heap of old ties and a bent rail). `--threats --debris 0|1|2`, `dt art show debris-tree|debris-rocks|debris-tangle`.
    - **Causeways and retaining walls (§12.3):** the plan's causeway is stone pitching laid on the bank as the land's mesh has it (`WorldArt.Ground` at its own laterals, so on it and not under it) from the bed down to the marsh's waterline, old piles along the waterline, rubble slumped off; a retaining wall is a battered masonry face on its side, coped and buttressed, with weep holes. `--route r --structure causeway|retainingwall`.
    - **Engine damage:** the engine has its own (`DamageKit.Engine`): its plate is the cab's and the tender's side sheets, so they take the gouges and flaps when damaged and a breach when wrecked; and the boiler leaks (`DamageKit.Leaks`: one split seam damaged, three wrecked), each a jet of steam as hard as the pressure's up, laid back by the wind (`Effects.SteamLeaks`). `--integrity 0.2,1`.
    - **The cold (GDD §26):** look.json `atmosphere.cold` reads the route weather's cold: frost (`FrameLighting.Frost`, scene.frag: a pale glinting rime on what's outdoors, thickest on what faces the sky) from `frostFrom`, and breath from `breathFrom` (`Effects.Breath`, from each crewmate's mouth off the head bone, quicker when they're working). `--cold c`.
    - **The dawn's sky (§21):** the sky itself now comes up with it (frame `dawn`: look.json `dawn.horizonGlow`): the zenith pales toward the haze and a warm band glows low on the sun's side under the grey, the cloud still over it. `--dawn t`.
    - **The utility car (GDD §10, §4 "cramped, lamp-lit, human-scale"):** `TrainKit.UtilityFit`, a cargo car's body fitted out for the crew where its load would be: bunks two high, a stove with its pipe through the roof (smoking, `Effects.StoveSmoke`, as the guard van's chimney now does too), a table and benches, lockers and a coat rail, its windows lit. The sim has no utility kind yet (engine, cargo, guard), so it's staged only: `GreyboxScene.Utility`, `--utility i`; a `VehicleKind.Utility` and what it's for (rest, warmth against the cold) is the sim's to add.
    - **Burnt cars (C.5):** the sim keeps no record of a fire that's out, so the scene remembers the ones it's seen (`GreyboxScene._burns`: peak and when). A car alight forces smoke out of its eaves and doors, laid back along the roofs by the train's wind, its door cracks glowing (`Effects.CarSmoke`); gutted, it's charred (the body's tint toward charcoal and the scar mask as bad as the burn) and smoulders for minutes. Presentation only: a client that joins later sees the scars and not the smoke. `--burnt car,s`.
168. **The UI's own look, and the store's art (the art checklist's UI and store rows to L1).** The HUD, the menus and the run-end screens were solid rectangles and the 5×7 font; the icon and capsules were placeholders.
    - **The look (GDD §28; `UiStyle`):** drawn with the overlay's solid quads alone, so it costs nothing and stays sharp on a headset's panel (the overlay has no textures, by design). A panel is a plate of blackened iron, bevelled, brass-trimmed, riveted (`UiStyle.Plate`); a key is an enamel keycap (`Keycap`, and `Keyed` draws any "[KEY]" in a line as one, so every prompt and hint gets them as written); the title is a station's nameboard (`Nameboard`). The 5×7 stencil font stays: a finer one rasterised from a system face didn't hold its shapes at this size.
    - **Prompts:** keycaps, and a hold under way shows as a bar along the plate's foot (read off the prompt's own "(NN%)").
    - **The dead (App. D.6-D.10):** a card of their own in the lower middle (`Hud.DeadCard`), clear of what they're watching: how they died, who they're watching and the keys, the way back split over two lines.
    - **The run's end (D.12):** the report has the screen (no prompt or noise meter over it). Commendations are five badges (`UiStyle.Badge`: an enamel roundel with its pictogram) on a strip under the report. Awarding them (the run-end screen's input, one per player to anyone else, the profile's tally) isn't in the game yet, so only a still passes them: `dt screenshot --hud --report derailed --commend`.
    - **The store (§36):** the icon is the engine itself, head on in the fog with its lamp lit, a frame of the real model (`tools/art/store/icon.py` over `screens.sh`'s render); the capsules' key art is the engine off its front quarter with the train behind it into the haze, the title on a nameboard as the title screen sets it (`capsule.py`). (A cloud session's python needs a venv for numpy: `python3 -m venv v && v/bin/pip install numpy pillow`.)
    - **The trailer (§36, Next Fest):** `dt trailer`, cut from the game itself and repeatable: a solo night on a generated line warmed up out of the yard, run on in real time while the cameras cut through the train coming out of the fog, its roofs with the crew at work, something stalking a car's aisle, the cannon, and the dawn, with title cards between on the nameboard. Frames go to out/trailer/frames and ffmpeg makes trailer.mp4. It's silent: the score and the sound are `dt audio render`'s to add.
169. **HOST a run, JOIN from a lobby list (T116 follow-up).** Playtest: "You should be able to host a run, not a night. The button should just say HOST", and "Join should work like Lethal Company, I see active lobbies I can join and then what my ping is in relation to that. I can join it if its public. If it's a private lobby its not listed." The reading:
    - **The title** says HOST and JOIN (the screens' headings too). Hosting talks of a run: the loading screen says BUILDING THE RUN when hosting. QUICK NIGHT and the campaign's nights keep their word: a night is still the unit the campaign counts.
    - **VISIBILITY: PUBLIC or PRIVATE** on the host screen (left/right, default public, kept in the settings as `publicLobby`), and a **NAME** the host types (`lobbyName`; empty, `<PLAYER NAME>'S RUN` from the settings' name, else the Steam or system name). Public: the LAN beacon runs and the Steam lobby is `k_ELobbyTypePublic`. Private: no beacon at all, and the Steam lobby is friends-only, so it's joined by invite, "Join Game" on a friend, or the address. `--private` does the same from the command line. The HUD's lobby panel says which.
    - **JOIN is a lobby browser** (`LobbyBrowser`, `ListedGame`): the public games on the local network and Steam's public lobbies for this game and protocol, in one list with the lobby's name, crew aboard/max, tier and your ping, nearest first (unmeasured last). Another protocol (LAN only: the Steam search filters it out) or a full crew shows greyed with the reason on the row, since a greyed row can't be selected to show a detail line. REFRESH searches again and pings afresh (otherwise Steam is searched every 10 s while the screen's up). The address entry stays at the bottom for private games and ones far off. A game on both lists shows once, as the network's: the beacon carries its Steam lobby id.
    - **LAN ping is measured.** The browser pings each host's beacon socket about once a second ("BALLAST-PING/1" and a token, from a socket of its own so pongs reach the right copy of the game), the beacon sends it straight back, and the round trip, timed on `Stopwatch` (not the caller's clock), is smoothed lightly (0.7/0.3). It's the path to the host's machine, a little under the game's own ping, which adds the host's tick. The beacon's newer fields (name, max, tier, lobby) follow the original seven, so an older browser still reads it.
    - **Steam ping is Steam's estimate.** The host publishes its ping location (`ISteamNetworkingUtils.GetLocalPingLocation` then `ConvertPingLocationToString`) in lobby data as soon as Steam has one; the browser parses it (`ParsePingLocationString`) and asks `EstimatePingTimeFromLocalHost`. The search (`IOnlineBackend.RequestLobbyList`, `OnlineEventKind.LobbyList`) uses `AddRequestLobbyListStringFilter` on game, protocol and `public`=1, the worldwide distance filter and 50 results. Crew aboard comes from the host's own `aboard` value (LAN joiners aren't Steam lobby members). `FakeOnline` keeps the same rules (only public, joinable lobbies of the filter's game and protocol; each account somewhere on a plane, ping the distance) and the tests run against it (`LobbyListTests`, `LobbyBrowserTests`); the Steam half needs a Steam client to exercise.
    - `dt screenshot --menu join` fills the list from fake LAN games and a `FakeOnline` search; `--menu host` shows the new options.
170. **Playtest 3 and 4: speeds on the map, derailments you can read, the fire you can see (T121).**
    - **The run map's figures.** "On bends on the map put a number there that shows the top speed the bend can be taken." The cab map inks each posted stretch red and prints its board's figure in km/h beside it, on the outside of the bend (`GreyboxScene.PostedBends`, `MapFigure`). A generated line's are its plan's speed boards (`LineBuilder.Signage`: every bend that would derail the engine at full steam, plus each demand's), drawn at the bend each stands before; a prototype route's are its Lineside boards. The board's painted figure is printed, rounded down to 5 km/h, so the map and the board agree. Not every bend gets a figure: a bend the engine can take flat out has no board. On a gentle night (frontier:7) the only posted spots are two brass fields at 20 km/h. Where two figures would overprint, the slower one wins.
    - **Derailment causes say by how much.** "If derailment happens people should know they took the corner too hard and by how much." `DerailCause` is in the boards' km/h: "took the 45 km/h bend at 68 km/h, 23 km/h too fast" (`TrackRules.BendCause`, against the board before the bend, or what the bend holds where none stands); the Sleepers', the Lineside boards' and the Switchman's the same way. The replay shows it on screen and the incident report reads it.
    - **First-hand, then the replay.** "If we derail let people experience it first hand, then replay the moment from the third person train view." `DerailSequence`: for `firstPersonSeconds` the eye is kept where you were in your car, and the car carries it as the wreck throws it about. The camera takes the car's whole orientation, so a roll takes the horizon with it. Then `replaySeconds` of the chase view play back what was drawn from `replayLeadSeconds` before it came off. The camera rides with the train until the moment it derails, then holds and turns to keep the train in frame. Then the T117 orbit, `cinematicSeconds`. All of it is presentation: it records the frames the client drew, and the beats run off the client's own count of the wreck's seconds. The run's end waits for the whole sequence (`WreckTuning.SequenceSeconds`). Off the train, the first beat holds the eye where you stood. In a headset it stays the headset's (no camera taken from a VR player). `--derail-at s` in the app derails a host at s seconds, for `--capture` checks.
    - **A Switchman at a crawl.** "We appear to have derailed at a very low speed." A derailing Switchman threw the points under a train at any speed. Under `derailAbove` (25 km/h) the points now split under it instead: the engine takes `runThroughDamage` and stops short. Over that speed it's off the rails, as before. The playtest's 18 km/h derail near Talbot Foundry fits this, though nothing else on that stretch could do it at that speed. If it happens again, the cause on screen will say what it was.
    - **The fire you can see is the fire on the grate.** "Firebox seems to go out even when there's fire in the UI ... Firebox should only go out once the number is at 0." The firebox's look was the fire's fraction of capacity, so one unit of six read as out. `GreyboxScene.FireLook` maps the HUD's FIRE (the coal on the grate) to the look: dark only at 0.0; any coal is a bed with tongues of flame; the square root means the first coal on a dying fire shows at once; more coal adds tongues until the whole grate is alight. The light, the closed door's glow and the heat dial all follow it. `dt screenshot --view fireman --firedoor --coal u`.
    - **Brakes 0.7 of T97's** ("lets reduce their efficiency by 30%"): spec B.5's table and `SpecTableTests` restated; a 3-car stop from 22 m/s is 108 m, a 20-car one 494 m. The Track Doll's "only trains of 6 cars or fewer can stop within 200 m" still holds (164 m at 6).
    - **GDD D.15 answered.** Question 1: no drop-out fee, since nothing is tied to wins and dropping out only punishes friends. Question 2: big crews get a second Holdout at every facility (`stops.json` `secondPadScale` 0), and it takes someone only at `secondCrew` (5) aboard.
171. **The cannon answers the Track Doll, every creature confirms a hit, and every ball has an impact (T121 playtest).** "I shot the track doll and it did nothing while she was on the track. It should have saved me from hitting her." "All creatures need hit confirm feedback." "All cannonballs should have an impact explosion and VFX to show where impact was."
    - **The doll and the cannon.** App. A.2 gives the brakes as the answer and two outcomes (stop short: VANISH; strike her: HAUNT); it doesn't say a ball does nothing. Reading: a forward cannon's ball that hits her on the rail is the first outcome. She's shattered (`TrackDoll.Hit`): BREAK OFF then gone, the same way out as stopping short, so nothing's left to strike or to haunt. She stays a cost-only enemy (§21's category): the cost is the shot's loudness on the meter and the reload. Standing on the rail (Dormant or Telegraph, not aboard) she has a hit volume, `railHitRadius` 0.55 round a point `railHitHeight` 0.7 over the rail (enemies.json `trackDoll`, with `cannonShatters` to turn it off). Aboard, haunting, only cornering and clubbing answer her, as before. `HitConfirmTests.TheForwardCannonShattersTheDollOnTheRailAndTheTrainPassesHerSpotClean` fires at her from the engine's gun as the train runs up and runs on past her spot: no commit, no haunt.
    - **A geometry fact for the playtest:** the engine's gun stands behind the stack, so it can't lay on anything low on the rail dead ahead nearer than about 60 m (the ball hits the stack: `ABallIntoTheTrainsOwnBodyLandsOnTheTrain`, and the impact now shows it there). With the 80 m range, a doll dead ahead can be shot from about 62 to 80 m from the gun, a second or two at cruise. Not changed here; if that window's too narrow in play, the gun's pivot height or the range are the knobs.
    - **What every creature does when a ball finds it** (§21 by zone): Cinder Hounds on the line (hit, 4 blows to their 3, and a running pack is driven off by the round), the Switchman at his lever (killed, "one cannon shot"), Climbers alongside before they mount (hit), the Track Doll on the rail (shattered). Everything else has no hit volume by design, and a ball goes past it to land beyond: the flank's Draggers and Whistler (under the cars and in the gaps, "no cannon answers"), the interior's Stoker, Tippy Toesie and Fire Flies, the Car Hugger on the rear car (below the arc, as the bots already know), the outside's Ribbits, Gaunt, Followers, Soot Children and Grumbler (on foot at the stops, answered by the crew), the Passenger and the Choir's ghosts. The hazards and a car fire aren't creatures.
    - **Hit confirm** (`Combat/Hits.cs`). A blow (`World.Swing`) or a ball (`World.StepEnemies`) that lands on a creature writes a `HitConfirm` (who, what, where, which way, melee or cannon, whether it was the kill) to `World.Hits`; a ball's landing writes a `CannonImpact` (where, which way, on what surface, what creature) to `World.Impacts`. Host-authored, kept `hits.keepSeconds` 1 and `hits.impactKeepSeconds` 8 (combat.json), and replicated as records (`RecordKind.Hit`, `Impact`), so a dropped snapshot doesn't lose one; nothing in the sim reads them back, so prediction is untouched. A Follower's hits are held back from its carrier with the Follower itself (App. A.6: the thud at your back would give it away). A swing only picks what it can land on (`Enemy.Strikable`: the Follower's carrier can't), so every confirm is a blow that landed. Fire Flies can now be swatted (a hit confirm, nothing more: the swarm parts and closes, the answer's still the lamp). `HitConfirmTests` strikes every creature kind in its strikable phase, and fires on every one a ball can find, and checks the record reaches a client.
    - **Where a ball lands** (`Guns.Solid`): its path is marched in `guns.impactStep` (0.5 m) steps and narrowed for the ground (`PlayerMotor.GroundAt`, the terrain on a generated line), water (`TerrainField.WaterAt`) and the stops' walls (`StopWalls`), before the creature it was laid on, the train's own body or its range; one that hits nothing comes down under where its range runs out. Deterministic, alike on host and client. A creature behind a rise is now safe from a ball that would hit the rise first.
    - **Presentation.** `Effects.CannonImpact`: a white-hot core and a star of flash with a glare that grows with distance (what reads through fog at 100-150 m), a fireball rolling up and reddening over a second, a 55 m light, sparks in arcs, debris (earth, splinters, or the doll's white porcelain), a dirty smoke column for eight seconds, and on the ground a dust ring, a scorch decal and embers; into water a splash (spray column, mist, rings, a cold flash). `Effects.HitFlash`: a pop at the hit point, the creature flashed white for a blink, chips, a moment's light. The creature flinches (`GreyboxScene.Flinch`: knocked back along the blow and tipped away, a ball's twice a blow's). The crosshair gets four ticks for the one who landed it, red for the kill (`Hud.HitMarker`). Sounds (tier 4): `cannon-impact`, `cannon-splash`, `doll-shatter`, `hit-confirm`, each played once where it landed (`GameAudio.Strikes`); the chaos audio scenario now has the guard gun's balls booming down behind the train, and every tell still clears the bed by 6 dB.
    - `dt screenshot --impact ground|water|structure|train|creature|doll [--impact-at ahead,lateral] [--impact-age s]` stages a landing; `--threats --hit-flash [--hit-age s]` a blow on every staged creature; `--hud --hit-marker [kill]` the marker.
172. **The voice levers: the hard-cut and a GRAB on the radio (GDD v1.4 App. C.8, D.2, D.14; WP6).**
    - **The hard-cut.** "At PUNISH, or any instant death, the victim's proximity voice and radio stop on the tick, mid-word, with no fade." The host already stopped forwarding a dead speaker's frames to the living, but the last ~60 ms each listener had buffered (Opus prebuffer and jitter) played out, and the snapshots that tell a client someone's dead arrive a tenth of a second late (interpolation). So on the death tick the host sends every other client a cut in the voice stream itself (`VoicePath.Cut`, reliable, no sound), and the listener drops what it has of that speaker, near and on the radio, then and there (`VoiceChat.HardCut`). Frames from before the death that arrive late are stripped of air and radio. The voice stays cut until the snapshots have shown the speaker dead and then alive again, which is a freed player (D.8). A client whose cut went missing cuts when the snapshot says dead. `dt voice bench --die-at s` measures what's left: −180 dB with the cut, −90 without.
    - **A GRAB keys the radio.** "A grabbed player holding a radio keys it open for the whole GRAB." The host adds the radio path while the speaker is `Held` and alive (`HostSession.ForwardVoice`); the radio is still a thing (T41), and tunnels and mine spurs still kill it. On the client, the mic transmits throughout a GRAB, push to talk or not (`VoiceChat.Grabbed`). It closes at break-off and with the cut.
    - **Victims talk.** A held player is heard at full clarity unless the thing holding them changes it: Tippy Toesie's muffle and the Soot Child's fade are the only voice effects (`World.VoiceEffect`, unchanged).
173. **Crew lockers, spare kits, and things that stay in the car (GDD v1.4 §12, §23.2, E.9, E.12 question 4).** Playtest: things set down on the train were being lost through its walls, the train's stores all lie on floors, and E.12's open question 4 (should the fortress sell a spare kit?) was answered yes, with kits lootable too.
    - **The lockers** (`train.json kit.lockers`, `CarShape.WithLockers`, `Train/Lockers.cs`): the repair kit's car (`TrainOnLine.KitCar`, kit.repairKitCar: car 1) has a row of tall iron lockers along its left wall, from 0.7 m behind the front end wall back to just short of the left side door, 0.42 × 0.5 × 1.9 m each, so the row fits twelve and none crowds a door; the aisle beside them to the load is 1.2 m. Each is a `PartKind.CrewLocker` solid (you bump into it, things lie against it) and a `InteractableKind.Locker` that wants facing like a door. The car's extinguisher board moves into the gap ahead of the row. In the guard van (a two-vehicle train) the row takes its tool locker's place. Only that car's shape has them, so the shape cache keys on it.
    - **Names**: twelve grades as a Big Four depot would letter its lockers, in tuning: DRIVER, FIREMAN, GUARD, SHUNTER, SIGNALMAN, BRAKESMAN, LAMPMAN, FITTER, GANGER, WHEELTAPPER (the carriage-and-wagon examiner), PORTER, YARDMASTER. **The kit starts in the FITTER's** (`kitLocker`): the fitter is the shed's mechanic, who mends engines, and the kit is what mends the boiler. Spares go on his other shelf, then into the lockers after his.
    - **Use, held or tapped.** Held at a locker (`Lockers.DoorSeconds`), its door opens or shuts: `CrewActions`, so a predicting client opens it as the host does (`Vehicle.LockersOpen`, once a tick like a door). Tapped (let go before the door would move), the hands stow what they carry on the first free shelf, or take the top thing off the shelves (`Bodies.Handle` → `Stow`/`Take`, host-side like all hands). The hands wait for the release and never take the press, so host and client run `CrewActions` alike on every tick. Shut, a locker neither gives nor takes. Each has two shelves (`slots`) and takes what `holds` lists: lamp, radio, toy, loot, extinguisher, repair kit. Freight, the stores' crates, the heavy, the dead and the living don't go in.
    - **Held by the locker.** A stowed body (`Body.Locker`, `Body.Slot`) stays in its car's frame on its shelf (`Lockers.SlotAt`), asleep and skipped by the physics step, so a hard stop, a curve or a wreck can't move it, and it isn't in anyone's reach but through its open door. Everything that asks where things are (the run's kit, cars lost, loot stowed aboard) sees it in its car, as it is. A find stowed in a locker counts as stowed aboard (it pays, and goes, as on the floor). The Gaunt doesn't get into a shut locker; an open one is as good as the floor.
    - **Replication:** the vehicle record carries `LockersOpen`, the body record a tenth header field (locker × 256 + shelf, or −1). Protocol 7.
    - **Things stay in the car (the clipping).** Root cause: a carried thing rides at the hands, 0.6 m in front of the body, and a player faced up to a wall stands 0.3 m off it, so the hands are 0.3 m past a wall a tenth of a metre thick. Put down there, the sphere's push-out goes to the wall's nearest face, its *outside*, and the thing drops onto the ballast. A throw (9 m/s, 0.3 m a tick at 30 Hz) went through the wall between ticks the same way. Two fixes, both `Physics/Rooms.cs`: what's carried indoors is held inside the room (`Bodies.Carry`, brought in by its radius and a hair more), unless the way from your chest to your hands goes out through an open door or the open hatch; and every free body in a car's frame that was inside its room at the start of a step and ends it outside is stopped inside, its speed out through the wall gone, again unless it went out through an opening (`Bodies.KeepInside`). A hair's clearance from the wall matters: touching it, the contact's friction held a falling lamp up off the floor. Wrecks are left alone. `PutDownTests` reproduce it (they fail with `Rooms.Held` disabled): against a side wall, at the shut end door and in its corner, and thrown and set down at each wall on a moving train round a 200 m curve. Out through an open door still goes.
    - **Spare kits** (`campaign.json spareKit`, `Campaign.BuySpareKit`, the fortress screen; `dt campaign buy kit`): 325 scrip, at most 3 kept. `CampaignState.SpareKits` goes to the night as `SessionSetup.SpareKits` (`train.json kit.spareKits`, set by `Campaign.WithSpareKits`), so every machine builds the same train. **Reading:** the fortress always issues the train's own kit; the campaign's spares are the kits that came home beyond it (`RunReport.SpareKitsHome`: kits in a car still on the engine or in a living crewmate's hands, less train.json's `repairKits`). So a spare lost in the night is gone, and a kit found at a stop and brought home is kept as one.
    - **Found kits** (`loot.json repairKitChance`, 0.03; `StopLoot.Kits`): every container at a stop but a crane bay rolls on its own stream (`StopSeed.Kit`), so the finds and crates already dealt from a seed are unchanged; a kit lies beside what the container holds. **Reading:** a found kit is the crew's once someone has picked it up (`Body.Claimed`, host-side; stocked kits are claimed from the start). One lying unfound in a village the train has left would otherwise count as "lying on the line, never lost", and no train with one anywhere down its route could ever be Stranded.
    - **Stranded with several kits** (`EngineeringKit.Where`) already took losing them all; it now counts only claimed kits, and a kit in a locker in a coupled car is reachable. `StrandedTests` pins a spare: one carried off and the other in its locker is a mend; the lockers' car taken with both is Stranded; a spare in hand when the car goes saves the night.
    - **Presentation:** `LockerKit` draws the row (olive sheet steel, shelves, a plinth) as one piece in the car's frame and each door on its hinge at the cabinet's front edge, shut or swung 150° open back towards the row, its grade on a cream enamel plate in the HUD's pixel font (the same letter size on every door), and on a card inside the door, so an open locker reads by name too. The HUD prompts the locker in front of you and says which locker the kit's in. The Stranded outro (E.9's "empty place") opens on the fitter's locker standing open on its empty shelf (`GreyboxScene.KitLockerOpen`). The fetching bot (`KitRun`) holds Use at the fitter's until it opens, then taps for the kit.
    - `dt screenshot --stocked --view lockers` looks at the row with the fitter's open on the kit (`--lockers-open NAME,…|all` opens others); `--view kit` at the kit in its locker; `--stranded s` the outro. `LockerTests` covers the row, the verbs, the shelves, replication, the kit's place, spares bought and settled, and loot kits.
174. **The derailment's opera (GDD v1.4 App. E.6; WP5).** "Opera means the night is over" (E.1): it plays at a derailment and nowhere else.
    - **Playback** (`Ballast.Audio`): a recorded-clip source, `SourceKind.Sample`, plays an `AudioClip` (mono, any rate, Catmull-Rom up to 48 kHz) from `SoundInstance.ClipSeconds`, and the instance ends with the clip. `Wav.Read` takes PCM 8/16/24/32-bit and float WAV (no FLAC/OGG: nothing in the repo decodes them, and WAV is enough). The clips are loaded once, when `GameAudio` starts (`Game/Sound/Opera.cs`).
    - **The music bus** is tier 0 (`Mixer.MusicTier`, `content/audio/sounds/music.json`), outside the tell tiers: it ducks nothing, and no tier rule ducks it (the run is over; there's nothing left to hear a tell for). `mix.json music`: it ducks 6 dB under any voice on the dead channel (50 ms attack, 400 ms release; E.6 "Mix", E.10 "Music duck"), and while it sounds, everything on tiers 1 and 3-6 goes through two 1.2 kHz low-pass stages, crossfaded in over 0.25 s (E.6 "Game audio"). **Reading:** voices (tier 2) aren't "game audio" and E.6 wants the laughing heard, so they stay clear. E.6's "half speed" for the game audio isn't done (the mixer has no time-stretch), nor is "the train bed stops on the derail tick". The bus sits at `levelDb` −6 against the master's +9, 3 dB over E.6's −16 LUFS reference, so the opera reads over the wreck's grind.
    - **Loudness:** every track is measured in LUFS (`Ballast.Audio.Loudness`, BS.1770-4 K-weighting and gating, designed for the file's own rate) and normalised to −16 LUFS by its manifest `gainDb`.
    - **The tracks are our own recordings** (E.6's fallback). `tools/audio/fetch_music.py` looks for CC0 recordings on Wikimedia Commons, keeping only files whose own licence reads CC0, with the licence metadata and SHA-256 as evidence; from the cloud sessions Commons, Musopen, Freesound and the Internet Archive are all refused by the proxy (403). So `dt audio opera` (`src/DarkTerritory.Cli/OperaCommands.cs`, wrapped by `tools/audio/opera.sh`) renders arrangements of the public-domain scores on a deliberately cheap, slightly grand synth band (a church organ with a Leslie wobble, a blaring synth brass, a plucked banjo or mandolin, a tuba oom-pah, a buzzing sung "aah" with a sob, timpani, bass drum, snare and a crash, in a small Schroeder hall, look-ahead limited) into `content/audio/music/*.wav` (mono, 22.05 kHz, 16-bit, 21-24 s each, about 5 MB for the five), and writes the manifest and `CREDITS.md`. **Reading:** E.6's fallback is a commissioned singer and ensemble dedicating CC0 in writing; ours are synthesised by the project and dedicated CC0 by it, which the manifest's evidence says (`performers` "Dark Territory, synthesised (dt audio opera)", `source` the script, `evidence` the file's SHA-256 and the dedication; there's no licence page to archive). The melodies were arranged from memory of the scores, with no score at hand: they want an ear checking them against the real thing, the Lament's most of all. Five tracks, one per mood and two Gallops: Lament "Vesti la giubba" ("Ridi, Pagliaccio", the sung "aah" and its sob over the organ; the hit the high first note of "Ridi"), Gallop the "Infernal Galop" (the can-can on trumpet over banjo and tuba; the hit the tune's first downbeat) and the *William Tell* finale (the trumpet's "da-da-dum"; the gallop's first downbeat), Doom Verdi's "Dies irae" (a timpani roll into hammered G minor chords with the bass drum's off-beat blows and tumbling scales, then the choir; the hit the first chord), Swagger "La donna è mobile" (the trumpet strutting over a mandolin's oom-pah-pah; the first "La"). Every hit has the crash on it.
    - **The manifest** (`content/audio/music/manifest.json`, generated) has E.6's fields: id, file, work, composer and year, performers, source, licence (`CC0-1.0`), evidence, mood, in-point, hit, out-point, measured loudness, and the gain. `MusicManifestTests` fails the build on a music file without a CC0-1.0 entry whose SHA-256 matches it (anything in the folder but the manifest and the credits is a music file), an entry without its file, a loudness or gain that doesn't measure, a hit that isn't the file's sharpest onset where it says, or a track too short for the sequence; and on a pool without every mood (E.6: at least 4 for the demo). `dt audio opera --check` renders without writing and says whether the committed files match (they're byte-identical on a rerun).
    - **Rotation** (`Sim/Music/Music.cs`, `MusicRotation`): E.6's shuffle bag, kept with the host's campaign save (`CampaignState.Music`, written as the night settles) and, for a night without a slot, in the app's data (`MusicBagFile`, `music-bag.json` beside the settings, or beside the saves with `--saves`; written when a derail drew from it). Each derail draws one without replacement; an empty bag refills with every manifest track; the last track played is never the next while another is left, which covers the refilled bag's first draw. Within what those rules allow the draw is weighted by the speed it came off at (`wreck.json music`): under 8 m/s Lament weighs 3, over 16 m/s Gallop and Doom do, Swagger always weighs 2, anything else 1. **Reading:** between 8 and 16 m/s nothing is favoured but Swagger "fits any". Deterministic: ids in manifest order, a Pcg32 seeded from the wreck's own seed. A track added to the manifest joins at the next refill; one removed drops out of the bag. `MusicRotationTests`: 1,000 derails on one save carried as text, every bag a full pass, no track twice running (E.11 "Rotation").
    - **Every client plays the host's draw.** The host's world has the rotation (`World.Music`, from `SessionSetup.MusicBag`, host-only) and draws in `World.Derail`; the track goes out as a 31-bit FNV-1a key of its id in the world record (`World.DerailMusic`, protocol 9), and each client looks it up in its own manifest. **Reading:** a replicated draw, not a seed, because the bag is in the host's save and clients don't have it; a key, not an index, so manifest order doesn't matter. A client without the track plays nothing. The manifest isn't in the Welcome's content hashes (the packet's budget).
    - **On the derailment** (note 170): the music starts on the replay's first frame (`DerailBeat.Replay`) at `MusicTrack.StartFor(replayLeadSeconds)`, so the hit lands as the replay shows the train coming off; a client a frame late starts that much further in. It plays through the orbit and fades over the last `fadeOutSeconds` (1.5) to the sequence's end (`Opera.Cue`). **Reading:** E.5 put the hit on the final player's apex and the fade under the cause card; the sequence as built has neither, and the moment of derailment is its peak.
    - **Checking it:** `dt audio render --scenario wreck` renders the whole sequence by default (`--track id` for one track) with someone laughing on the dead channel over the replay, and reports the draw, when the hit was due and heard (the music stem's sharpest onset above 4 kHz), the duck and the low-pass (`report.music`); read the spectrogram beside the WAV. `AudioTests.TheOperaHitsOnTheReplayDucksUnderTheLaughingAndFadesOut` holds it: hit within ±0.1 s, a −6 dB duck, the low-pass on, silence by the end. `MusicBusTests` covers the clip source, the duck's times, the low-pass sparing voices, and the LUFS meter. `dt audio opera` drops a spectrogram of each track in `out/audio/opera/`.
    - **Credits:** `content/audio/music/CREDITS.md` (generated from the manifest) lists every work, composer and performer; CC0 asks for none, E.6 lists them anyway. The front end has no credits screen yet; it's the file one would show.
175. **The Choir takes the loudest voice, and noisy toys (GDD v1.4 App. A.7, C.7, C.9, App. C item 4; WP7).** A.7: "Among exposed players, the Choir takes whoever contributed most to the meter during BUILD."
    - **The shares** (`World.ChoirShares`, host-side, kept in player-id order so the tie-break is fixed) are in loudness-seconds. Each tick's meter input is credited to whoever made it: a voice (`intent.Voice`), the cord (`World.WhistleBy`, the last to pull it; the Whistler's whistle is nobody's), and a noisy toy to whoever's carrying it. Machinery, livestock (the floor) and a Holdout lock being smashed belong to nobody. A cannon round lifts the meter by `roundLoudness`, which the window then takes about `windowSeconds` to bleed off, so the gunner is credited `roundLoudness × windowSeconds` per shot. **Reading:** "during BUILD" means while the Choir could still gather, the build tick included (`!Present && !Spent`). The shares clear whenever it's back to Distant, and freeze while the swarm is here and once it's spent, so the incident report can read them after the seize.
    - **Seizure** (`ChoirGhost` Commit): the exposed living crew are ordered by share, then distance, then id. With nobody loud, it's the nearest, as before. **Fix found on the way:** two ghosts could each seize someone, and both died when the first finished, which breaks A.7's "one crew member per run". Now, while one ghost holds someone, the rest wheel overhead.
    - **The report line** (C.9's "Choir seizure" row: the victim's share and the top contributor if different): "Taken by the Choir on the roof of car 3 at km 4. 25% of the noise. Loudest on the line: Priya." When the victim was the loudest it reads "Loudest on the line: Priya, 75% of the noise." D.12's example uses the same words.
    - **Noisy toys** (`ToyNoise`: squeaker, music box, wind-up drummer, carried in `Body.Noise` and on the wire in the body record's ninth field, so no protocol change): the guard van now stocks four toys (`train.json kit.toys`), three of them noisy in `kit.toyNoises` order. They feed the meter only while carried (`combat.json choir.toys`: 0.2 / 0.15 / 0.3, against a normal voice's ~0.22). None alone trips the 0.6 threshold; one carried by someone talking does. **Reading:** "carried or jostled" is carried only for now; a toy rattling loose on a car floor has no owner to credit and no physics speed to read yet. A noisy toy is worth 1.5× a quiet one to whatever ranks loot (`Bodies.Value(Body)`). The Track Doll takes either. The HUD names them (THE SQUEAKER, THE MUSIC BOX, THE DRUMMER), and each plays its tier-4 loop while carried (`toy-squeaker`, `toy-musicbox`, `toy-drummer`). `dt audio render --scenario toys` renders all three on a roof, each about 10 dB over the wind and within 1 dB of each other.
    - `ChoirShareTests` pins the voice, cord, toy and cannon shares, the build-only window, loudest-first over nearest (with the quiet one surviving: one per run), the report's two wordings, and the stocked toys over the wire. `AudioTests` checks that the toys play only while carried and how they balance.
177. **The derailment film: everyone's own death, the cause card, the skip vote (GDD v1.4 App. E.2-E.5, E.8, E.9, E.11; WP4).** E.5's shot plan after T121's first person and replay. The playtest asked for "experience it first hand, then replay the moment from the third person train view", and E.5 asks for "everyone gets a shot".
    - **The sequence:** first person (4 s) → the chase-view replay (9 s) → the film's cut (`WreckFilm.Cut`): one shot per player, the settle, the cause card → the run's end. **Reading:** E.5's Freeze and Establishing beats are what the first person and the replay already show, so the cut starts at the first player's shot. With no film yet (a client still shooting it when the replay ends), the old orbit stands in.
    - **E.2/E.8, the stream:** the host doesn't stream keyframes. On the derail tick it takes the film's start (`World.Film`, a `FilmStart`): the wreck's bodies and couplings as `Wreck.Begin` made them, the seed, the cause card's words, where on the line it came off, and every living crewmate's world position, velocity, facing, the car they're inside and their name card ("on the throttle", "at the firebox", "on the gun", else `IncidentLog.Place`).
      - It is sent reliably as Brotli-compressed JSON chunks (`MessageType.Film`, like the report; doubles round-trip exactly).
      - Every client shoots the film from it (`World.ShootFilm`, off the frame loop in `NetPlaySession`). The pre-sim is deterministic (DMath, seeded) and looks the ground up from the start's own line hint, so every machine records the same frames and plans the same shots. `FilmTests` pins this frame for frame over the wire.
      - This replaces E.8's ≤250 KB keyframe budget with a ~2 KB start. It's the same trade the netcode makes everywhere: send intent and seeds, simulate alike.
    - **The physics and director:** `WreckFilm` (from the parked film-wip work).
      - The ragdolls are D.9's 11 joints, flung at E.3's ×1.3 with a 2–4 m/s kick and up to 4 rad/s of tumble, capped at 30 m/s. A slow tip-over still throws people sideways, and a bystander gets nothing and a beat to watch.
      - Colliders are the ground, the cars as boxes (hollow for whoever was inside), and each other.
      - Peaks are the highest apex, the longest airtime or the hardest landing.
      - Shots go in rising order of peak score. Each player's camera is the best of E.4 O9's 24 shell candidates that passes O1/O3/O4/O5 over the whole shot, with O10's overhead fallback. Shots ease from 0.25× to 0.1× at the peak.
      - All numbers are in `wreck.json` "film" (E.10's defaults).
    - **On screen:**
      - The film's car poses replace the frames (`DerailSequence.FilmFrames`) and the crew are drawn as ragdolls (`FilmBodies`).
      - O2's fadeable occluders are a cutaway: any car whose shell crosses the camera-to-subject line is not drawn for that shot (`FilmCutAway`, `GreyboxScene.CutAway`). That's how someone tumbling inside a car is seen. **Reading:** the whole car goes rather than a dither fade; the renderer has no per-object fade yet.
      - O12's light rig: a cool key from the camera's side and a warm rim behind the subject in the lamp's colour, or one light over the crew in a wide (`FilmLights`).
      - The name card is a lower third in the clerk's typewriter face: DAVE, then ON THE THROTTLE.
      - The cause card dims the picture and types out the clerk's line: "Consist derailed at km 14, 72 km/h. Took the 45 km/h bend at 72 km/h, 27 km/h too fast. Throttle: Dave. Recovery not scheduled." (`IncidentLog.CauseCard`, E.5's own example in our words).
      - The opera (note 174) fades out under the cause card rather than at the sequence's end. Its hit stays on the replay's moment of derailment, not the last apex: the music starts with the replay, and the tracks' hits come about 3.5 s in.
    - **Skipping (E.5, E.9):** holding Jump sends `PlayerActions.Skip` once the vote counts: after the first player's shot until the cause card, or from `stranded.skipAfterSeconds` (3) into the Stranded outro. The host counts heads each tick. A majority of the session, or the host's own player (`HostSession.HostPlayer`), sets `World.FilmSkipped`, replicated with the votes (`FilmVotes`) on the World record. Clients jump to the cause card (never past it) or the outro's end. The prompt shows the votes. Protocol 10.
    - **Not yet:**
      - E.3's water buoyancy, doorway gaps in the hollow cars, and the extras (stowed bodies and loose items) joining the wreck.
      - O7's other-body cover and O8's mid-shot cut. O9's scoring is simplified.
      - Radio static under the clerk.
      - E.5's per-player peak bookmark, which is WP8's (note 176) to take from `WreckFilm.Peaks`.
    - `dt screenshot --film s [--crew n --speed v --route r]` renders a frame of the cut, cards and all, as the app plays it, from a hosted night with bots, and prints the shot list. Tests: `FilmTests` (the start, determinism over the wire, everyone's shot in order inside the caps, the slow tip-over, the majority and host skips), `FilmPlaybackTests` (frames, bodies, cutaway, lights) and `WreckSessionTests` (the film beat and the host's skip landing on the cause card).
178. **The fortress on the radio: the dispatcher's manifest and the clerk's tally (GDD §9; WP9).** §9: at the gate "the yard dispatcher reads the crew out over the radio by name, as a manifest, with the same tone used for the coal". At the terminus "a yard clerk tallies the run over the radio as the cars come through: cargo by the car, bodies by the body, each fee read out flat".
    - **The words** (`Sim.Run.Radio`):
      - The manifest is "Yard to consist. Manifest follows.", then "Crew: Dave." for each player aboard, then the coal, the powder and shot, the cars, and "Gates open. Yard out.".
      - The tally is the cars delivered and the cargo's worth, then cars lost, then each death as "Sam. Body recovered. Fee 350. Refund 263." or "Ana. Body not recovered. Fee 350.", then the mail, salvage, costs, and "Net: 1158. Next.".
      - Both come from the night's own numbers (the report D.12 already sends), so nothing new goes on the wire.
      - **Reading:** "cargo by the car" is one line for the cars delivered and their worth. The report doesn't itemise cars, and §9's point is the flatness, which the deaths read in the same voice carry.
    - **When:**
      - Each client reads the manifest as its mirrored run first leaves the yard, naming the crew aboard then.
      - It reads the tally once a delivered night's report is in. The end screen waits for the tally (`ClerkTally`), as it waits for E.9's outro.
      - A line goes out every `run.json radio.lineSeconds` (1.6 s), typed out as it's read, with the last four on a card at the top of the screen ("RADIO: THE YARD").
    - **The voice:** there's no speech in the stack, so `radio-clerk` is a man's voice reading flat through a cheap set. It's a low buzz through a spoken vowel's formants, broken into syllables at an even, bored pace, over static, band-limited and crushed like `voice-radio`. It's tier 2, like any voice on the set, so the bed ducks under it.
    - **Not yet:** E.9's clerk line over the Stranded pull-back stays as it was (a caption). The dispatcher doesn't wait for the train to be underway past the towers; it reads as the run leaves the yard.
    - `dt screenshot --hud --radio manifest|tally [--radio-at s]` renders the card. Tests: `RadioReadingTests` (the words and the timing) and `RadioCardTests` (the tally holds the end screen, then gives way).
179. **The dead's tools: the queue they can see, a Call Out the crew can hear, the Live Mic (GDD v1.4 App. D.6, D.7, D.10, D.14; WP10).** The Call Out was counted on the host but never reached a client. The queue was the host's alone. Live Mic didn't exist.
    - **The queue (D.6):** the respawn queue rides its own Holdout record (id 0xFFFF): pairs of player id and lobbied, in order. Clients mirror it (`Holdouts.MirrorQueue`). The dead card shows it whole: "QUEUE: 1 SAM   2 YOU   3 ANA (JOINING)" (`Hud.QueueLine`). The living see nothing; roll call stays verbal.
    - **Call Out (D.7):**
      - Each Holdout counts its Call Outs (`Holdout.Calls`, on its record). A client plays each one once as the count goes up, a shout or a bout of banging, from the Holdout, at 60 m with normal falloff, through its walls (`holdout-shout`, `holdout-bang`).
      - Both sounds are adults: D.7 keeps them apart from the Soot Children's child voice. They're tier 3, never an enemy's telegraph, and can't mask one.
      - It never touches the meter. `HoldoutTests` already pins that, and the count is presentation.
    - **Live Mic (D.7):**
      - Only the player waiting in a Holdout can toggle it, with [SPACE] (Jump, which the dead don't otherwise use). It is off by default and off whenever the Holdout isn't lit (freed or released). It's on the record.
      - On the host, a dead speaker with it on is forwarded on the proximity path to every living listener within the proximity cutoff of the Holdout (`VoiceRouting.HearsLiveMic`), and on the dead channel as ever.
      - The client plays a dead speaker's proximity frames from their Holdout (`VoiceChat`: the hard-cut keeps their air voice only while the Live Mic is on).
      - It's never said aloud in the world: `World.Voices` (what the Soot Children mimic) only hears the living, and the meter only counts living voices, so the Gaunt and the Choir never hear it (D.14 "dead silence").
      - **Reading:** no new VoicePath bit. A dead speaker on the proximity path can only be a Live Mic, so the client positions it by the replicated Holdout.
    - **Not yet:** the lobbied (joining) players' own card doesn't show the queue (their wait panel is the link's). Each occupant has one shout and one bang rather than a voice set of several.
    - Protocol 11. Tests: `HoldoutTests` (the toggle is the occupant's alone and goes off when freed; a client mirrors the calls, the Live Mic and the queue), `VoiceRoutingTests` (heard only by the living near the Holdout) and `DeadPhaseTests` (the queue line; each Call Out heard once from its Holdout).
