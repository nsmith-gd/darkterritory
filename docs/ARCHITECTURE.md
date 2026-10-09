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
| VR | **OpenXR** (`Silk.NET.OpenXR`), `XR_KHR_vulkan_enable2`, stereo (multiview: both eyes in one pass, a pass an eye where the GPU lacks it; §8 note 221) | PCVR first (Link / Air Link / Virtual Desktop / SteamVR). Standalone Quest (Android) is a post-launch option that this stack does not rule out. Tested headless on Monado's simulated headset (§8 note 25). |
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
- **Route:** `VoiceRouting` on the host decides proximity (with a 4 m forwarding margin past the 26 m cutoff), cab-wall occlusion, radio (not into or out of a tunnel), the dead channel, and a dead player's Live Mic from their Holdout (note 179).
- **Play:** the receiver plays each path through the mixer as a stream voice on tier 2. Proximity uses spec A.5's log curve as a mixer rolloff mode. The radio is `voice-radio.json`: flat, 300 Hz–3 kHz, crushed, with static while keyed.
- **Measured:** 4 m against 16.5 m is 8.3 dB, matching the curve. Past 26 m nothing is sent. The radio at 93 m keeps its band, with the low end 48 dB down. Talking ducks the bed by exactly −6 dB. On a 90 ms ±20 ms link with 5% loss, level stays within 1 dB of a perfect link.
- **Since:** Soot Children mimicry, replayed from the host (T40) and formant-shifted (note 247); the listener's head (note 246); occlusion by the cars' own walls, an open door, the hatch or a breach letting it through (note 248).
- **Not yet:** a radio as an item (for now everyone carries one).

### 6.4 Audio (spec A) — why not FMOD
FMOD's power lives in FMOD Studio, a GUI authoring tool whose projects an agent cannot sensibly author or verify. The spec's needs are specific and small: six tiered buses with sidechain ducking, parameter-driven layered events, voice limiting, positional slack-action delay chains, and heavy procedural synthesis. We build this in C#:
- **Sound graphs as data** (`content/audio/*.json`): layers, parameters, DSP chains, synthesis nodes (filtered noise, resonators, granular, mass-spring, formant).
- **Tier ducking** exactly per spec A.3. Tier 1 is never ducked or occluded beyond −6 dB.
- **Offline render**: the mixer can render any sim state to a buffer. The harness then *measures* tell audibility against the bed, which makes spec A.3's "tier 1 is inviolable" an automated test.
- Steam Audio for HRTF (spec A.4: ~30° localisation, and essential in VR) and geometric occlusion.

**Status (T12).** `Ballast.Audio` exists. It has no device dependency, so the same code renders to the speakers and offline.
- **Sounds:**
  - Layered synthesis nodes: noise, sine, saw, square, impulse; and recorded takes, Ogg Opus in `content/audio/samples` (note 191), and a recorded clip from a file (the opera, note 174).
  - Biquad filter chains, tremolo and gates with jitter, vibrato, envelopes and bit-crush.
  - Every number can be a curve over a live parameter.
- **Buses:** seven tier buses (spec A.3's six, and the work's drone under them), with the ducking rules and each tier's fader in `content/audio/mix.json`; and the opera's music bus apart from them (note 174).
- **Spaces:** the listener's space (the cab, a car, a tunnel, a facility's yard, outside; `content/audio/spaces.json`) convolves everything positioned with a synthetic impulse response, shuts out what it shuts out, and compresses voice in a tunnel (note 192).
- **Spatialisation:** distance rolloff and a spherical head (note 246: the far ear's delay and shadow, the pinna's front-and-back and height cues), plus occlusion by the cars' own walls (note 248). Tells are floored at −6 dB and keep their band through a wall. The bed and the world go through a tape's wow, flutter and saturation; the tells and the crew's voices don't (note 250).
- **Voices:** per-sound instance limits with stealing, and a 64-voice budget. Past the budget, voices virtualise, but tells always render.
- **Game hookup (`DarkTerritory.Game.Sound.GameAudio`)** drives it all from world state, so clients hear what the host does:
  - the bed follows speed, pressure, fire, throttle and brake;
  - slack action runs down the consist one coupling at a time;
  - the tells follow enemy phases;
  - the Choir adds voices and closes in as aggro climbs.
- **Output:** device output is an SDL3 audio stream. miniaudio isn't needed yet. Measured HRTFs (Steam Audio) stay open for VR, behind the head's stage (note 246).

### 6.5 Rendering (art direction)
Forward+ renderer, deliberately limited:
- Point-sampled 256–512 px textures with mips; optional internal resolution scale with nearest upscale for pixel crawl (off or reduced in VR for comfort).
- Per-pixel lighting from a few practical lights (lanterns, headlamp, furnace), shadow maps only for key spot and point lights, baked vertex AO.
- **Height fog + low-res froxel volumetric fog**, the core of the look. Smoke and steam particles, bloom, film grain, ordered dither, colour grading LUT to the sheet's palette.
- Offscreen capture path for agents: `dt screenshot --scene … --camera … --out shot.png`.
- VR: multiview single-pass stereo, 90 Hz target. The PS2-level poly budget makes this easy.

**Status (art pass v2, §8 note 48).** The renderer draws the art pipeline plan's look: a sky pass (gradient, hazy moon, clouds, a 360° backdrop band of far silhouettes), the scene in a float target (point-sampled texture arrays with box-filtered mips and a positive bias, two-layer terrain blend, alpha test, per-pixel practical lights, Blinn-Phong speculars from spec maps, exponential height fog), a blended effects pass (flipbook smoke and steam, additive sparks and glows), then half-res bloom, a 16³ LUT grade, vignette, grain and the ordered dither into reduced colour depth. The kits (`DarkTerritory.Game/Art`) build the train over the sim's own collision, and the track, lineside and structures along the line; textures come from `tools/art/textures.py` (CC0 photo sources through a PBR-to-legacy converter, plus procedural ones). `dt art check` holds every piece to its budget; `dt art show <piece>` turns one on a turntable. Not yet: the lamp's shadow map, normal maps on the cab. (Multiview: note 221.)

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

Each note is a file of its own in [docs/notes/](notes/) (note 521): read the ones your work touches
(`tools/coord/notes.py show 515`, or open the file). To add one, write `docs/notes/<number>.md` starting
`<number>. **Title.**`, with the number you claimed (docs/COORDINATION.md), and run `tools/coord/notes.py index`. A note
written here instead is moved out by `tools/coord/notes.py split`; the Coordination check asks for it.

<!-- notes index: written by tools/coord/notes.py index; a line per file in docs/notes/ -->
- [1. Standalone Quest](notes/1.md)
- [2. Crossplay between Steam and itch.io players?](notes/2.md)
- [3. Brake fade on the flat](notes/3.md)
- [4. "8+ players"](notes/4.md)
- [5. Jump-off speed is measured over the ground](notes/5.md)
- [6. End ladders](notes/6.md)
- [7. Boiler model](notes/7.md)
- [8. Safety valve](notes/8.md)
- [9. Rolling and air resistance](notes/9.md)
- [10. Hollow trigger](notes/10.md)
- [11. Engine layout](notes/11.md)
- [12. Rakes and couplings](notes/12.md)
- [13. Run length vs. route length (spec B.8, needs a design call)](notes/13.md)
- [14. Generated routes](notes/14.md)
- [15. Guns and the Choir](notes/15.md)
- [16. Enemies (GDD Part Six, App. A, App. B)](notes/16.md)
- [17. Audio mix (spec A)](notes/17.md)
- [18. UDP transport and host/join (T13)](notes/18.md)
- [19. Car interiors (T15)](notes/19.md)
- [20. The run (T16)](notes/20.md)
- [21. Physics: position-based dynamics, not Jolt (T17)](notes/21.md)
- [22. The editor is a local web page, not ImGui (T18)](notes/22.md)
- [23. Session rules: interest, drop-out, content, drop-in (T19)](notes/23.md)
- [24. Steam: one protocol over every carrier, lobbies as meeting places (T20)](notes/24.md)
- [25. VR foundation: OpenXR sessions, stereo, and a headset in CI (T21)](notes/25.md)
- [26. Cold and the Vigil (T22, spec B.2 and C.2)](notes/26.md)
- [27. Text and the HUD (T23)](notes/27.md)
- [28. Loading modules v1 (T24, spec D)](notes/28.md)
- [29. The campaign (T25, spec E and F)](notes/29.md)
- [30. VR controllers and comfort (T26, roadmap M4)](notes/30.md)
- [31. Branches and switches (T27, GDD §17, App. A.7)](notes/31.md)
- [32. The facility set piece on spurs (T28, GDD §17)](notes/32.md)
- [33. The front end and the fortress screen (T30, spec E and F, roadmap M6 "settings")](notes/33.md)
- [34. Bots keep warm by themselves (T31, spec B.2)](notes/34.md)
- [35. Builds for players (T33, roadmap M6)](notes/35.md)
- [36. The crew works a stop (T32, GDD §17, spec D)](notes/36.md)
- [37. Crates you can load (T34, spec D.2)](notes/37.md)
- [38. The crew coals up (T35, spec B.6, D.2 gravity chute)](notes/38.md)
- [39. The Switchman (T37, App. A.7, B.7)](notes/39.md)
- [40. Hands (T29, roadmap M4)](notes/40.md)
- [41. The HUD and menus in the headset (T36, roadmap M4)](notes/41.md)
- [42. Art pass v1 (T39, GDD §25-28)](notes/42.md)
- [43. The radio is a thing (T41, spec A.5, GDD §24)](notes/43.md)
- [44. The Soot Children (T40, App. A.4, B.6, spec A.5)](notes/44.md)
- [45. Editor v2: the rail and the modules (T44, roadmap M5 "simple editor (module + rail + tuning)")](notes/45.md)
- [46. Two to lift, two to turn (T43, spec D.2, roadmap M4 "two-handed grips")](notes/46.md)
- [47. Bots carry heavy crates in pairs (T45, spec D.2)](notes/47.md)
- [48. Art pass v2: the look from the art & animation pipeline plan (GDD §25-32)](notes/48.md)
- [49. Why the harness's crate stop never finished (T50)](notes/49.md)
- [50. The Draggers (T46, App. A.4, B.4, spec B.3)](notes/50.md)
- [51. The crew see a headset's arms (T47, roadmap M4 "VR body IK")](notes/51.md)
- [52. The gantry crane (T48, spec D.2 and D.3; GDD's foundry: "overhead crane run from a gantry. The operator can't see the ground crew")](notes/52.md)
- [53. Mods v1 (T49, roadmap M7 "mod loader v1"; CLAUDE.md: "content/ is also the base mod")](notes/53.md)
- [54. The Rattle (T51, App. A.5 and B.5; GDD: "lives in the couplings. You hear it before you cross. RULE: don't cross between cars rattling")](notes/54.md)
- [55. The Lamplighters and the lamp switch (T52, App. A.6 and B.6; GDD: "light-reactive. Work the lineside. RULE: lamps down. Contradicts…](notes/55.md)
- [56. The Deadman and the Stoker (T53, App. A.5 and B.5)](notes/56.md)
- [57. The fidelity target moves up to 2008-2012 (art direction, after the art pass)](notes/57.md)
- [58. Sourced models, and model bashing (art direction: "a texture and model fidelity problem")](notes/58.md)
- [59. Crew bots run the gantry crane (T54, spec D.2 and D.3)](notes/59.md)
- [60. The balance sweep (T55, roadmap M7 "balance sweeps", GDD §34)](notes/60.md)
- [61. The Ferryman (T56, App. A.2 and B.2; GDD: "stands on the track ahead holding a lantern, waving you down. RULE: do not slow down")](notes/61.md)
- [62. The Long Whistle (T57, App. A.2 and B.2; GDD: "sounds a horn on the line ahead. There is no train ahead. RULE: don't trust the horn")](notes/62.md)
- [63. Climbers (T58, App. A.4 and B.4; GDD: "scales directly with train length. More cars means more gaps means more mount points, defended by…](notes/63.md)
- [64. The Weight (T59, App. A.3 and B.3; GDD: "deliberately below the rear gun's arc. Cannot be shot. Forces either a sacrifice or a trip…](notes/64.md)
- [65. The Gaunt (T60, App. A.4 and B.4; GDD: "the cost is a person. Whoever watches it can do nothing else, and the train still needs running")](notes/65.md)
- [66. The procedural line (docs/design/linegen-plan.md, roadmap M5 "procedural line v1")](notes/66.md)
- [67. Contradiction seeding and saving up (T64, App. B.1)](notes/67.md)
- [68. Ground far overhead doesn't lift anyone off the train (T66)](notes/68.md)
- [69. A winch with nowhere for its sleds to go is done (T66)](notes/69.md)
- [70. The 100-night playtest's fixes, and cold's bite cut (spec B.2)](notes/70.md)
- [71. Running dark costs sign sight as well as obstacle sight (sight.json; the user's call after the playtest)](notes/71.md)
- [72. Trouble inside the cars (after the 100-night playtest: "more problems players need to face in cars, and reasons not to roof walk the whole…](notes/72.md)
- [73. The pacing rule, and the mail cranes (after the playtest: "a reward or a problem every 30 seconds at most, ideally 20")](notes/73.md)
- [74. The 100-night rerun (after the balance passes in notes 70–73)](notes/74.md)
- [75. The Passenger (T61, App. A.7 and B.7; GDD: "the tell is silence on a voice channel, in a game entirely about talking")](notes/75.md)
- [76. Followers (T62, App. A.3 and B.3; GDD: "the asymmetry is the entire mechanic. The person in danger cannot see the danger")](notes/76.md)
- [77. frontier:7 delivers again: the crane's operator stood in for the shunter, and nobody stood in for them (T67)](notes/77.md)
- [78. The Drift (T63, App. A.4 and B.4; GDD: "compounds brutally with anything that demands movement — which is most of the roster")](notes/78.md)
- [79. Cargo types (T68, GDD §18–19, App. B.8: "cargo changes the run rather than just scoring it")](notes/79.md)
- [80. A stop's trouble takes two hands, not all of them, and the loading watches the clock (T70)](notes/80.md)
- [81. The crew roster lists the Passenger under the face it wears, and never as heard (T69)](notes/81.md)
- [82. The guard van has a rear platform, and the bots beat the Weight off from it and hold the gaps against Climbers (T65)](notes/82.md)
- [83. `dt balance` sweeps the procedural line (T71)](notes/83.md)
- [84. Grease's counter: sanding from the engine's running boards (T72)](notes/84.md)
- [85. Balance on the procedural line: the grace stretch, the terminus approach, and a stop's lateness (T74)](notes/85.md)
- [86. The frame-rate targets (the director: "the game must run 72fps in VR minimum and 90fps on PC")](notes/86.md)
- [87. A fireman in the cab, and nobody under a Climber that's got into it (T75)](notes/87.md)
- [88. The Dead Lines sweep: the cut-off rake counts as aboard, the driver calls all aboard, and the terrain at its ceiling isn't a quiet (T76)](notes/88.md)
- [89. Out of a car the Gnawers get out in, and the run home reckoned by the line (T77)](notes/89.md)
- [90. Mods ship through Thunderstore (T78, the director's call)](notes/90.md)
- [91. The demo is an edition: an overlay on the content, baked into its build (T79)](notes/91.md)
- [92. The GDD v1.1 roster (T84-T88): sixteen enemies and the Choir on one spine with a GRAB, the director on want tags](notes/92.md)
- [93. Generated stops: yards, villages and their loot (level-design Part D, Part Z)](notes/93.md)
- [94. Death, Holdouts and return (GDD v1.2 Appendix D). The Vigil is cut](notes/94.md)
- [95. A yard's power and the grade out of it (level-design D.2, spec D.1)](notes/95.md)
- [96. Stops on generated lines (level-design Part Z; linegen plan §11.1, §11.3)](notes/96.md)
- [97. The Track Doll's own model (GDD v1.2 §21, App. A.2)](notes/97.md)
- [98. The first playtest's movement round (T90-T92)](notes/98.md)
- [99. The roof guns slide on rails (T93, playtest)](notes/99.md)
- [100. A night starts at the gate (the director's call, after playtesting)](notes/100.md)
- [101. A snapshot bigger than a datagram goes over a few ticks](notes/101.md)
- [102. The Car Hugger's own model (GDD v1.2 §21, App. A.3)](notes/102.md)
- [103. A brake left on stays on (frontier:7's derailment)](notes/103.md)
- [104. The dead watch the living (GDD App. D.10), on #96's Holdouts](notes/104.md)
- [105. The second playtest round (T94–T98)](notes/105.md)
- [106. Roof hatches for the crane (T99)](notes/106.md)
- [107. The fortress is a town (T100)](notes/107.md)
- [108. The driver sees the line (T101)](notes/108.md)
- [109. The car eaten away, and the Car Hugger's and the Track Doll's motion (GDD v1.2 App. A.2, A.3; §31)](notes/109.md)
- [110. Tippy Toesie, and the one doorway (GDD v1.2 §21, App. A.5; §26.5)](notes/110.md)
- [111. The Whistler's model (GDD v1.2 §21, App. A.4)](notes/111.md)
- [112. The crew walk aboard (T102)](notes/112.md)
- [113. Saving the gun (T103)](notes/113.md)
- [114. Graphics settings (T83)](notes/114.md)
- [115. Nobody at the controls (T105)](notes/115.md)
- [116. The Ribbits' model (GDD v1.2 §21, App. A.6)](notes/116.md)
- [117. The Choir's model (GDD v1.2 §21, App. A.7)](notes/117.md)
- [118. The Gaunt's model (GDD v1.2 §21, App. A.6)](notes/118.md)
- [119. The Grumbler's model (GDD v1.2 §21, App. A.8)](notes/119.md)
- [120. The Stoker's model (GDD v1.2 §21, App. A.5)](notes/120.md)
- [121. The Followers' model (GDD v1.2 §21, App. A.6)](notes/121.md)
- [122. The Climbers' model (GDD v1.2 §21, App. A.4)](notes/122.md)
- [123. The Fire Flies' model (GDD v1.2 §21, App. A.5)](notes/123.md)
- [124. The Passenger's model (GDD v1.2 §21, App. A.8)](notes/124.md)
- [125. The brake and the vent as a pair (T106)](notes/125.md)
- [126. The Soot Children's model (GDD v1.2 §21, App. A.6)](notes/126.md)
- [127. The Switchman at its lever (GDD v1.2 §21, App. A.7)](notes/127.md)
- [128. The Cinder Hounds' bite (GDD v1.2 §21, App. A.3)](notes/128.md)
- [129. The Draggers drag them under (GDD v1.2 §21, App. A.4)](notes/129.md)
- [130. A train that's lost the hill (T107)](notes/130.md)
- [131. Standing for the crew, in Deep territory (T81)](notes/131.md)
- [132. The Gaunt is no longer a man (GDD v1.2 §21, App. A.6)](notes/132.md)
- [133. The Whistler is no longer a man (GDD v1.2 §21, App. A.4)](notes/133.md)
- [134. The Choir are no longer children (GDD v1.2 §21, App. A.7)](notes/134.md)
- [135. The Followers are no longer hands (GDD v1.2 §21, App. A.6)](notes/135.md)
- [136. The Climbers are no longer men (GDD v1.2 §21, App. A.4)](notes/136.md)
- [137. The gun is a cannon, in pieces the game can work (GDD v1.2 §19 "crude cannons with a manual reload", App. C.3 powder, ball, ram)](notes/137.md)
- [138. Far land, and water with a far side (playtest: "the water falls below the mountain in the distance, no coastline")](notes/138.md)
- [139. The dead towns' houses, modelled (playtest: "meshes missing textures, the house doesn't look good")](notes/139.md)
- [140. Hard blocks in the sky (playtest), and the headless app capture](notes/140.md)
- [141. The hotbar (T108)](notes/141.md)
- [142. Quick fixes from the solo playtest (T110)](notes/142.md)
- [143. The boiler as the driver's game (T109)](notes/143.md)
- [144. A board for every curve that can derail you (T111)](notes/144.md)
- [145. The crew at work (X1, X2)](notes/145.md)
- [146. Your own arms (X3)](notes/146.md)
- [147. The effects pass (X4)](notes/147.md)
- [148. The train's stores (X5, first half)](notes/148.md)
- [149. The L0 sweep (everything on the checklist at least a prototype)](notes/149.md)
- [150. The repair kit, an item (GDD §12, App. D.7)](notes/150.md)
- [151. The Choir, rarer and answerable (T113)](notes/151.md)
- [152. A Holdout's call goes to a bot that can get down (T115)](notes/152.md)
- [153. The gun's seat (T112)](notes/153.md)
- [154. Every named halt has its village (T114)](notes/154.md)
- [155. A village's houses are walls (T114)](notes/155.md)
- [156. Solo nights were planned for four (T115)](notes/156.md)
- [157. The Track Doll, quieter (T115)](notes/157.md)
- [158. What nobody answers goes (T114)](notes/158.md)
- [159. Where friends join (T114)](notes/159.md)
- [160. Host a night, join a night (T116)](notes/160.md)
- [161. The Track Doll giggles (T118)](notes/161.md)
- [162. The derailment is physics (T117)](notes/162.md)
- [163. The same trigonometry on every OS (T116 cross-play)](notes/163.md)
- [164. Stranded, and bodies keep their tools (GDD v1.4 §23.2, App. D.2, E.9; T119)](notes/164.md)
- [165. Failure attribution, the incident report, and names (GDD v1.4 App. C.9, D.12; T120)](notes/165.md)
- [166. The crew at work, the rest (the art checklist's crew rows to L1)](notes/166.md)
- [167. Survivors, breach states, loads, nests, the mail catch, iron bridges, debris, causeways, engine damage, the cold and burnt cars (the art…](notes/167.md)
- [168. The UI's own look, and the store's art (the art checklist's UI and store rows to L1)](notes/168.md)
- [169. HOST a run, JOIN from a lobby list (T116 follow-up)](notes/169.md)
- [170. Playtest 3 and 4: speeds on the map, derailments you can read, the fire you can see (T121)](notes/170.md)
- [171. The cannon answers the Track Doll, every creature confirms a hit, and every ball has an impact (T121 playtest)](notes/171.md)
- [172. The voice levers: the hard-cut and a GRAB on the radio (GDD v1.4 App. C.8, D.2, D.14; WP6)](notes/172.md)
- [173. Crew lockers, spare kits, and things that stay in the car (GDD v1.4 §12, §23.2, E.9, E.12 question 4)](notes/173.md)
- [174. The derailment's opera (GDD v1.4 App. E.6; WP5)](notes/174.md)
- [175. The Choir takes the loudest voice, and noisy toys (GDD v1.4 App. A.7, C.7, C.9, App. C item 4; WP7)](notes/175.md)
- [176. Bookmarks (GDD v1.4 App. D.10, D.12, D.13, E.5, E.9; WP8)](notes/176.md)
- [177. The derailment film: everyone's own death, the cause card, the skip vote (GDD v1.4 App. E.2-E.5, E.8, E.9, E.11; WP4)](notes/177.md)
- [178. The fortress on the radio: the dispatcher's manifest and the clerk's tally (GDD §9; WP9)](notes/178.md)
- [179. The dead's tools: the queue they can see, a Call Out the crew can hear, the Live Mic (GDD v1.4 App. D.6, D.7, D.10, D.14; WP10)](notes/179.md)
- [180. The dead's creature vote, and commendations (GDD v1.4 App. D.11, D.12, D.13; WP11)](notes/180.md)
- [181. Bodies as loot, completed, and the survivor's identity (GDD v1.4 App. C.4, D.8, D.9, Line Plan §12.6; WP12)](notes/181.md)
- [182. Cargo, contracts and the child (GDD v1.4 §9, §18, §19, App. A.6, B.6, B.9, C.4; WP13)](notes/182.md)
- [183. Hazards: deep cold, wind, tunnels, fouled guns, broken radios, lamps out (GDD §22, §23; WP14)](notes/183.md)
- [184. Consist variety: the crew car, armour, a second guard car, and the consist's upgrades (GDD §10, §26, spec F.3; WP16)](notes/184.md)
- [185. Facility set pieces: the grain elevator's spout, the slaughterhouse's herd, the chemical works' hose, the depot's powder (GDD §18, §19;…](notes/185.md)
- [186. The verification harness's gaps: combination fairness, the cascade audit, degraded comms, the per-tree GRAB check (GDD §34, App. A.9, B.10,…](notes/186.md)
- [187. The switchyard's standing cars and the wreck yard's derailed train (GDD §18; spec D.1, D.2; level-design I.4; WP15b)](notes/187.md)
- [188. Bot crews that get there with the cargo: the car fires, the Fire Flies, and the harness's own night (T81/T114 leftovers)](notes/188.md)
- [190. Attribution complete: C.9's rows that aren't deaths, and the right contributing action for the Switchman's and the Stoker's derailments…](notes/190.md)
- [191. Recorded sounds (the audio checklist's pass, merged with main in note 233)](notes/191.md)
- [192. The mix and voice lines at L1: long takes off the mixing thread, the work's drone on its tier, spaces, tunnel voice](notes/192.md)
- [193. The train's, the world's and the places' cues in the game (GameAudio.Train, GameAudio.Outside)](notes/193.md)
- [194. The opera, for real: CC0 recordings of the public-domain works, cut to their climaxes (GDD v1.4 App. E.6; WP25)](notes/194.md)
- [195. Protocol 21 for #149's body record (an audit fix)](notes/195.md)
- [196. The last of F.3's upgrades: lamp armour, gun cooling, repair kit charges, radio range, the powered switch thrower (spec F.3, A.5; GDD §17,…](notes/196.md)
- [197. A swing at nothing is seen too (note 146's "not yet"; audit order P1)](notes/197.md)
- [198. The set pieces heard (notes 185 and 187's "not yet"; audit order P1)](notes/198.md)
- [199. A dropped joiner is told, and doesn't see itself (audit order P2's rehearsal)](notes/199.md)
- [200. `dt playthrough`, and the train's own frames (the 5 October audit)](notes/200.md)
- [201. Hazards, the rest: wind on a roof's footing, the cold step on the HUD, mending a broken radio (GDD §22, §23; WP14, note 183's "not yet")](notes/201.md)
- [202. The dead's vote as a screen, from a headset, and the bots' votes (GDD v1.4 App. D.11, D.12; WP11, note 180's "not yet")](notes/202.md)
- [203. Bookmarks, the rest: each player's peak in the film, and the night's stills kept on disk (GDD v1.4 App. D.12, D.13, E.5; WP8, note 176's…](notes/203.md)
- [204. The combination sweep over a grid of routes and crew sizes (note 186's "not yet"; GDD §34, App. B.10; WP17)](notes/204.md)
- [205. The Climber in play: hands for steel, skin that isn't plastic (the 5 October audit, note 200; note 136's model)](notes/205.md)
- [206. A car fire's flames aren't a row of cards (the 5 October audit, note 200)](notes/206.md)
- [207. The effects' last "not yet"s: the furnace flare, brass dust, a fire you can see spreading (the checklist's demo VFX rows)](notes/207.md)
- [208. The killed go over and crumble (T121's hit confirm; the checklist's Switchman "shot or clubbed", Soot Children "killed")](notes/208.md)
- [209. The Cinder Hounds board the rear car (the checklist's "not yet": a distinct boarding leap, not the lunge)](notes/209.md)
- [210. The body under a headset (T82, roadmap M4 "VR body IK"; note 51's "not yet")](notes/210.md)
- [211. A crewmate's gait is paced over the car they stand on, not the ground (presentation; `SceneArt.Crewmate`)](notes/211.md)
- [212. The look-out: the sweep's dormant kinds brought on (notes 186 and 204's "not yet"; GDD §34, App. A.4, A.6; WP17)](notes/212.md)
- [213. The Whistler carries its victim (the checklist's "not yet": "the victim carried during the run")](notes/213.md)
- [214. The Choir disperses (the checklist's "not yet": "a disperse when the crew hushes (it just leaves)")](notes/214.md)
- [215. The Track Doll vanishes, and takes a toy and goes (the checklist's "not yet": "vanish with no walk-off (it just stops being drawn); takes a…](notes/215.md)
- [216. The firebox's bed is a heap of coals (the 5 October audit; the checklist's firebox)](notes/216.md)
- [217. The spruce near the line is modelled, and the pines fill out (the 5 October audit's "pines from above": narrow stacked columns)](notes/217.md)
- [218. The one the Car Hugger swallows is drawn in its mouth (the checklist's "nothing draws a player held in its mouth")](notes/218.md)
- [219. The Choir's arrival beat: the frost before it's seen (the checklist's choir-fx "still to do")](notes/219.md)
- [220. The Ribbits creep in and devour (the checklist's ribbits-anim "still to do")](notes/220.md)
- [221. Multiview: both eyes in one pass (roadmap M4; the "multiview later" of §3's VR row and note 25's "not yet")](notes/221.md)
- [222. The look-out in a crew of two (note 212's "not yet"; GDD §34; WP17)](notes/222.md)
- [223. The greybox figure leans too (note 210's "not yet"; T82)](notes/223.md)
- [224. The Car Hugger rides its cut car off (the checklist's "rides the cut car away")](notes/224.md)
- [225. The Tippy Toesie recoils when it's pulled off (the checklist's tippy-anim "recoil beat")](notes/225.md)
- [226. The extinguisher sprayed braced, and hung back on its bracket (the checklist's crew-extinguisher "recoil while spraying, and a distinct…](notes/226.md)
- [227. The backhead is boiler plate and the firehole firebrick (the checklist's firebox "the firebox texture")](notes/227.md)
- [228. The Choir's cold chills the whole frame (note 219's frost; the Look Review's "the frost reads only in the crop")](notes/228.md)
- [229. `dt playthrough` sees the scene's own beats (the audit's "deaths, vanish and disperse aren't carried in a playthrough")](notes/229.md)
- [230. Powder and shot: "carried to the cannon" is at departure (App. C.3; the checklist's powder-shot row, closed)](notes/230.md)
- [231. The Deep sweep: steam against the brake, Sleepers in the grease, a step off the roof beside the plate (T81; note 131's "next")](notes/231.md)
- [232. Breaches (the breach decided 1 Oct; spec B.9), and the fouled gun's sounds](notes/232.md)
- [233. The sound package merged with main (the audio checklist's branch after notes 150-190)](notes/233.md)
- [234. The endings' own sounds: a powder blast, and the Stranded outro (GDD v1.4 §18-19, App. E.9)](notes/234.md)
- [235. Laying the seated gun is heard (T112; spec C.2 "the turrets run on steam")](notes/235.md)
- [236. The game at half speed under the opera (GDD v1.4 App. E.6 "Game audio": "Everything else plays at half speed through a 1.2 kHz low-pass.…](notes/236.md)
- [237. A crewmate's swing is heard too (note 197's sound)](notes/237.md)
- [238. The radio's static starts where its voices stop (note 196's radio range, heard)](notes/238.md)
- [239. The powered thrower's lever is heard in the cab (note 196's lever, heard)](notes/239.md)
- [240. The yard on the radio has a voice (note 178's "no speech in the stack"; the audio checklist's voice-clerk, the last line at L0)](notes/240.md)
- [241. The wind on a roof and a mended radio, heard (note 201's hazards; mechanic telegraphs and confirmations)](notes/241.md)
- [242. The clerk reads the cause card and the Stranded line (GDD v1.4 App. E.5, E.9; note 240's "not yet")](notes/242.md)
- [243. The dead's vote, a bookmark and a commendation are heard (GDD v1.4 App. D.11, D.12; notes 180, 202, 203)](notes/243.md)
- [244. A kill is heard as the host says it, and confirmed as it's seen (note 208's crumble; the checklist's mechanic confirmations)](notes/244.md)
- [245. The checklist's last hooks: a crewmate's blow on the train, a powder keg handled as a keg, and the opera's hit on the final apex (the audio…](notes/245.md)
- [246. A head on the listener: side, front and back, and height, by ear (spec A.4 "directional to within ~30°. Players must be able to say 'car…](notes/246.md)
- [247. The mimic's voice through a smaller throat (spec A.6 "formant-shifted crew voice: Soot Children"; the audio checklist's voice-mimic)](notes/247.md)
- [248. The walls between the ear and a sound, from the cars themselves; and a one-shot rides its car (spec A.5 "car walls −12 dB and lowpass at…](notes/248.md)
- [249. The sound settings: the volumes, the microphone and its level (the audio checklist's mix-settings: "master, effects and voice volumes, mic…](notes/249.md)
- [250. A loop that wanders, the tape, the mine closing in, a toy jostled (spec A.4 rule 4, A.6; the audio checklist's mix-repeat, mix-degrade,…](notes/250.md)
- [251. `dt film`: the whole derailment as a video, and what watching it found (GDD v1.4 App. E.1, E.3-E.6; notes 170, 174, 177)](notes/251.md)
- [252. The lone driver lets the brake off to sand (note 231's first blocker)](notes/252.md)
- [253. Rejoining a night after a drop (note 24's "not yet"; after note 199's dropped joiner)](notes/253.md)
- [254. The crew cap, and a full lobby (notes 24 and 253's "not yet"; GDD §1 "Players: 2–8", §15)](notes/254.md)
- [255. The crew's clips, from the Look Review's notes (the notes boxes under each clip; twelve on the crew's)](notes/255.md)
- [256. Limbs through the body: a clearance check, and the second round of clip notes (Look Review; note 255's clips)](notes/256.md)
- [257. The derailment film, bigger and closer: cars hit bodies, the gunner thrown from the seat, landings thud (the director, 5 Oct 2026; GDD v1.4…](notes/257.md)
- [258. Each of the crew dies on their own hit, not on the derail tick (the director's decision of 5 Oct 2026; GDD v1.4 App. E.2 step 1, E.5's…](notes/258.md)
- [259. The lone driver breaks the dead out of a Holdout (T115's leftover; GDD App. D.5 "a living crew member begins the breach")](notes/259.md)
- [260. The line's own kills are telegraphed: a roof warning before a tunnel's mouth and a bend taken too fast (T115 playtest, "random death…](notes/260.md)
- [261. Cargo stops for the crew that's there (T114 "cargo stops"; GDD §17–18, spec D.2)](notes/261.md)
- [262. The demo's creatures, higher poly and textured denser, and their clips checked for parts through parts (the Look Review: "some things are…](notes/262.md)
- [263. Build 1121's playtest: the safe yard, the runaway nobody drove, the kit "the train has none", the Stoker redesigned, car fires (the…](notes/263.md)
- [264. The director's notes on build 1121 (6 Oct 2026): the menus take the mouse, fields don't type until entered, a lighter cab, the whistle…](notes/264.md)
- [265. A derailment is the driver's mistake, told in time: boards on the bends that derail, a train you can hear, the stress before it comes off,…](notes/265.md)
- [266. The director's pacing by pressure, and a grace picked per night (GDD App. B.1 "Pressure" and "Pacing rules", spec B.10; design decision,…](notes/266.md)
- [267. Fire is a grid (the director's decision of 6 Oct 2026, GDD App. F.1: "Each car's surfaces (floor, walls, roof; never mid-air) are cut into…](notes/267.md)
- [268. The Track Doll escalates if ignored (the director's decision of 6 Oct 2026, later the same day; GDD App. F.1)](notes/268.md)
- [269. Per-creature boarding rules: Cinder Hounds that board stay aboard; Fire Flies come only to a stopped train (GDD App. F, the director's…](notes/269.md)
- [270. One night length for every tier: 24 km, a 51 min dawn; the tiers differ in density, and quiet is counted in kilometres (the director's…](notes/270.md)
- [271. Stoker v3, the firebox half (the director's decision of 6 Oct 2026, GDD App. F.1)](notes/271.md)
- [272. The damage model: a few big hits, rare healing, an edge flash (the director's decision of 6 Oct 2026, GDD App. F.1)](notes/272.md)
- [273. T128, the director's notes on build 1121: falls, where a grab takes you, a player left behind, and the forts (GDD App. F; queue #7)](notes/273.md)
- [274. The fortresses are solid, and the guns hit them (T124; build 1121's notes, GDD App. F: "Fort buildings have no collision, and gun shots hit…](notes/274.md)
- [275. Tools matter: which tool, how hard, at what; the fireman's shovel; breach with a tool (GDD §12, §13, App. C.2, D.7; WP19, renumbered from…](notes/275.md)
- [276. The engine, cab forward (the director's sketch of 6 Oct 2026: "the current front of train design that I want to change to the desired…](notes/276.md)
- [277. The art checklist's open rows to L1 (C1, the art session; the director: "keep getting L1s in there")](notes/277.md)
- [278. Bends worth braking for: every night carries its tier's hard bends (queue #13, B1; note 265's open call; the director, 6 Oct 2026:…](notes/278.md)
- [279. The world is solid (queue #14, B1; the director's decision of 6 Oct 2026, GDD App. F.1: "The carry that clipped straight through the…](notes/279.md)
- [280. The cab facing forward, run by one (queue #15, C1; the director, 7 Oct 2026: "I don't like the new design, I feel like the controls and…](notes/280.md)
- [281. Fortress towns, first pass: a custom each, people who talk, papers to read, a square that's solid (the director's direction of 6 Oct 2026,…](notes/281.md)
- [284. The bend's stress heard building, and main's synth sounds on the audio checklist (AU1, queue #20; build 1121, the director: "we'll want to…](notes/284.md)
- [285. The HUD: your hands and the dark (B3; the director, 7 Oct 2026: "UI/UX needs a serious overhaul. There's too much UI on screen. I like the…](notes/285.md)
- [286. Boarding-first across the roster, the Switchman's lines that lead nowhere, and the Fire Flies' credit (the director's decisions of 6 and 7…](notes/286.md)
- [287. The night's first threat is drawn by something a crewmate did, the dark answers it, and the report names it (queue #23, A1.9; GDD App. F.1…](notes/287.md)
- [288. Driven off by their rules, killed by the crew together (queue #25, A1.10; GDD App. F.1's damage model, "creatures mostly don't take damage;…](notes/288.md)
- [289. The switch audit (queue #26, A1.11; the director, 7 Oct: "audit switches, in a recent playtest I felt like they weren't working")](notes/289.md)
- [290. The guns' effect on creatures: every body a ball can meet stops it, and a ball lands as a blow (GDD App. F.1, build 1121: "Bug: the guns do…](notes/290.md)
- [291. The Look Review's open creature notes (the director, 6 Oct 2026; queue #29, E1)](notes/291.md)
- [292. The in-night menu (F1, UI/UX 3; queue #30)](notes/292.md)
- [293. The profile screen (F1, UI/UX 3; queue #31)](notes/293.md)
- [294. Blocked sidings: derelict cars on a yard's sidings, by tier (B4, queue #32; level-design D.1, D.2, P18; I.4's "derelict cars on the…](notes/294.md)
- [295. Tunnel name plates on both portals (B4, queue #33; linegen plan §13.3 "Tunnels: Number + name, plate on both portals")](notes/295.md)
- [296. T128's caveats: the Choir stilled in the forts, and a Gaunt among the left-behind's hunters (queue #34, D1; note 273's "Caveat" lines, GDD…](notes/296.md)
- [297. Comfort settings: field of view, invert mouse, camera shake (F1, UI/UX 3; queue #35)](notes/297.md)
- [298. The yard's fun: emotes and outfits (F1, UI/UX 3; queue #36)](notes/298.md)
- [299. The bot gunner takes its gun (queue #37, D1)](notes/299.md)
- [300. Solo, a few runs then friends: tested (queue #38, D1; GDD App. F.1, the director, 6 Oct 2026: "a solo player can finish one to three runs…](notes/300.md)
- [301. Wrenches are the repair tool, Sea of Thieves style (queue #39, D1.3 for D1 at the director's call, the number C1's; GDD App. F.3, the…](notes/301.md)
- [302. A dead town's railway side: its station, and a goods yard of derelict stock (B4, queue #40; linegen plan §11.3 "Dead town: platforms,…](notes/302.md)
- [304. The voice booth (AU1, queue #42; the director, 7 Oct 2026: "Make an artifact for this where I can put my recordings for lines and then…](notes/304.md)
- [305. Crew 2 and the group creatures: GDD Part Eleven, open question 12 swept (queue #43, D1: "At crew 2, which group-based enemies are still…](notes/305.md)
- [306. The crew's emotes, the clips (queue #44, E1; F1's #217, note 298)](notes/306.md)
- [307. The trailer, re-cut and heard (queue #46, E1; the art checklist's "trailer", GDD §36: Next Fest pulls trailers on 18 Jan 2027)](notes/307.md)
- [308. The kit lost, on the radio (F1, UI/UX 3; queue #47; GDD App. E.12 question 5)](notes/308.md)
- [309. The outside creatures start where the stops say they live (B4, queue #48; level-design H.2; GDD B.6)](notes/309.md)
- [310. The crew pull a swallowed crewmate from the Car Hugger's mouth; harness nights started on the line keep their walkers (queue #49, D1; found…](notes/310.md)
- [311. The engine's front, a second pass (queue #50, E1; the director, 7 Oct 2026: "it just looks like we took the old classic boiler train and…](notes/311.md)
- [312. The Cinder Hound, remodelled and re-rigged to the director's concept sheet (queue #51, E1; the director, 7 Oct 2026: "I want to get a…](notes/312.md)
- [313. Fog that fills the low ground first (B4, queue #52; linegen plan §14 "Fog density ×1.3 in `low_ground` and `marsh`, ×0.8 on crests";…](notes/313.md)
- [314. The Whistler's nest at its site (B4, queue #53; level-design H.2 "out on the side of the stop with the least built on it"; note 309's "not…](notes/314.md)
- [315. Each player skips their own film (F1, UI/UX 3; queue #54; GDD App. E.12 question 2, E.5, E.9)](notes/315.md)
- [316. The panels you open, in note 285's form (F1, UI/UX 3; queue #55; GDD §32; note 285's "not yet")](notes/316.md)
- [317. A trestle across a lake's neck (B4, queue #56; maritime-rules §4's "not yet": "a crossed lake is always a fill")](notes/317.md)
- [318. The run map's and the route card's markers where the stops are (B4, queue #57; the director, 7 Oct 2026: "markers on the map in the train…](notes/318.md)
- [319. An engine short of steam holds the train back (queue #58, D1; the director's test build, 7 Oct 2026: "both heat and pressure are going…](notes/319.md)
- [320. A crew renamed, and deleted, from the fortress (F1, UI/UX 3; queue #59; GDD §9 "each host has three campaign slots"; note 33's "not yet")](notes/320.md)
- [321. Audio upkeep: the kit lost in the clerk's voice, and the bot crew's network audio test steady (AU1, queue #60)](notes/321.md)
- [322. Main's new features heard (AU1, queue #61)](notes/322.md)
- [323. The MODS screen (F1, UI/UX 3; queue #62; note 53's "not yet": "an in-game mods screen"; the wiki's "Playing modded")](notes/323.md)
- [324. A full car says so (F1, UI/UX 3; queue #63; note 37's "not yet": "a human-facing prompt for which car has room"; GDD §32 "a short state…](notes/324.md)
- [325. More set dressing, the first slice: what the railway left beside its line (queue #64, E1; the director's notes of 7 Oct, GDD App. F.3:…](notes/325.md)
- [326. Explorable village interiors, a first slice (B4, queue #65; GDD App. F.3, the director, 7 Oct 2026: "right now the villages don't have…](notes/326.md)
- [327. A presence of threat off the train (queue #66, D1 (D1.1); GDD App. F.3, the director, 7 Oct 2026: "ideally, we want people to be feeling…](notes/327.md)
- [328. The threat orchestrator, and the run between stops (D1.2 for D1, queue #67; GDD App. F.3, the director, 7 Oct 2026: "on the train, still…](notes/328.md)
- [329. The cannon's traverse sound, low and slow (queue #68, D1; GDD App. F.3, the director, 7 Oct 2026: "there's a weird sound that is happening…](notes/329.md)
- [330. A derailment that commits (queue #69, D1; GDD App. F.3, the director, 7 Oct 2026: "derailments are feeling quite underwhelming ... took a…](notes/330.md)
- [331. The hot box, the first upkeep job while the train runs (queue #71, D1; GDD App. F.3, the director, 7 Oct 2026: "on the train, still…](notes/331.md)
- [333. The gun's laying, recorded low and slow (AU1, queue #72; GDD App. F.3, the director, 7 Oct 2026: "there's a weird sound that is happening…](notes/333.md)
- [334. The Moose heard (AU1, queue #73; G1's note 339, docs/design/creatures/moose.md §4)](notes/334.md)
- [335. Walled towns: up to 3000 people, the fortress round the town, streets to walk (queue #74, B2; the director, 7 Oct 2026: "lets make it so…](notes/335.md)
- [336. The orchestrator's live crew: the budget from the crew alive, and the engaged cap by it (D1.2 for D1, queue #75;…](notes/336.md)
- [337. The Cinder Hounds' own light (queue #70, E1; first claimed as #62 and 323, then 331 and 335: F1's MODS screen, D1's hot box and B2's walled…](notes/337.md)
- [338. The armoured train (queue #76, E1; the director, 7 Oct 2026, with a marked-up shot of the way beside the boiler and a reference image: "I…](notes/338.md)
- [339. The Moose: a hyper-aggressive, territorial moose, too big to get on the train (queue #77, G1; the director's decisions of 7 Oct 2026;…](notes/339.md)
- [340. The Gannet: one enormous corrupted seabird that rides a fast train (queue #78, G1; the director's decisions of 7 Oct 2026; design…](notes/340.md)
- [342. Signs off the train heard (AU1, queue #79; D1.1's note 327 left the hook)](notes/342.md)
- [343. A lone crew answers a boarded hound pack (D1.2 for D1, queue #80; found in note 336)](notes/343.md)
- [344. Hold prompts in one form (F1, UI/UX 3; queue #81; GDD §32 "the action and its key ... and a hold's progress"; note 285)](notes/344.md)
- [345. The orchestrator's census of posts and slack (D1.2 for D1, queue #82; [orchestrator.md](design/orchestrator.md) §3.1, §3.2 items 2–4; GDD…](notes/345.md)
- [346. A lamp guttering, the second upkeep job while the train runs (queue #83, D1; GDD App. F.3, the director, 7 Oct 2026: "on the train, still…](notes/346.md)
- [347. TEXT SIZE (F1, UI/UX 3; queue #84; GDD §32 "Accessibility"; the director, 8 Oct 2026: "these are all quite important")](notes/347.md)
- [348. COLOURS: a colourblind-safe palette (F1, UI/UX 3; queue #85; GDD §32 "Accessibility"; the director, 8 Oct 2026)](notes/348.md)
- [349. CAPTIONS: the sounds worth hearing, written as they're heard (F1, UI/UX 3; queue #86; GDD §32 "Accessibility"; the director, 8 Oct 2026)](notes/349.md)
- [350. FIRST NIGHTS: a tip while the night's built, the controls in the yard (F1, UI/UX 3; queue #87; GDD §32 "Accessibility"; the director, 8 Oct…](notes/350.md)
- [351. The polish pass in the real app window (F1, UI/UX 3; queue #88; the director, 8 Oct 2026)](notes/351.md)
- [352. Loot on every yard line, and out before the train arrives (A1, queue #89; the director, 8 Oct 2026: "I really dont like that I spent all…](notes/352.md)
- [353. Towns that are lived in: townsfolk with their own breathing gear, a round for everyone, a green and the things a walled-in people put up…](notes/353.md)
- [354. The town's stones underfoot, and the ground's (AU1, queue #91; the director, 8 Oct 2026: "Theres a super weird squishy footstep sound when…](notes/354.md)
- [355. Turning on the spot is silent (queue #92, D1; GDD App. F.1, build 1121: "turning on the spot shouldn't make a sound; only walking should")](notes/355.md)
- [356. A coupling working loose, the third upkeep job while the train runs (queue #93, D1; GDD App. F.3, the director, 7 Oct 2026: "on the train,…](notes/356.md)
- [357. At a switch stand, Use is the lever's (A1, queue #94; found following up the switch audit, note 289)](notes/357.md)
- [358. The upkeep heard (AU1, queue #95; D1's notes 331 and 346, [orchestrator.md](design/orchestrator.md) §5.1)](notes/358.md)
- [359. S-bends (queue #96, B1; level-design B.4 and note 278's "Not yet"; the director, 6 Oct 2026: derailing on a bend is the core fear, and…](notes/359.md)
- [360. The armoured engine's damage, and the rest of #76's armour (queue #97, E1; note 338's "not yet")](notes/360.md)
- [361. The Ribbit rebuilt organically (G1, queue #98; the director, 8 Oct 2026: "Redo the Ribbit the way you did the Gannet")](notes/361.md)
- [362. The Mourners: the ones that come for your dead (G1, queue #99; the director's brief of 8 Oct 2026; design…](notes/362.md)
- [363. Tower Jaw: a corrupted beaver that fells the railway's timber (G1, queue #100; the director's brief of 8 Oct 2026; design…](notes/363.md)
- [364. The Brakeman: a dead railwayman winding the brakes on (G1, queue #101; the director's brief of 8 Oct 2026; design…](notes/364.md)
- [365. The Knotter: a living rope that holds two cars apart (G1, queue #102; the director's brief of 8 Oct 2026; design…](notes/365.md)
- [366. The Freight Beetle: it pushes the freight away from you (G1, queue #103; the director's brief of 8 Oct 2026; design…](notes/366.md)
- [367. Hotbox: an axle parasite that seizes a car (G1, queue #104; the director's brief of 8 Oct 2026; design `docs/design/creatures/hotbox.md`;…](notes/367.md)
- [368. The steam lift (A1, queue #105; spec D.2: "requires the locomotive coupled nearby and venting pressure to power it. 2 crew. Ties loading…](notes/368.md)
- [369. The HUD's last lines in note 285's form (B3, queue #106; GDD §32; note 285's rules, found re-reading `Hud.cs`)](notes/369.md)
- [370. The art checklist's "not yet" lines, a second round (queue #107, C1; first claimed as #91 and note 354, then #96 and 359, renumbered each…](notes/370.md)
- [371. Lineside props are solid (queue #108, B1; GDD App. F.1 "the world is solid", note 279's "Not yet": lineside props)](notes/371.md)
- [372. Toys a lamp finds (queue #109, C1; the director, 8 Oct, on what's found at a stop: "a bit of a brighter more unique look to them so that…](notes/372.md)
- [373. The film's extras and water (A1, queue #110; App. E.3: "Extras: bodies of the already-dead stowed in cars, crates, loot and extinguishers…](notes/373.md)
- [374. Powder to the guns, the fourth upkeep job (queue #111, D1; GDD App. F.3, the director, 7 Oct 2026: "on the train, still relatively boring…](notes/374.md)
- [375. The crew in the air and on a lurching car (queue #112, E1; the art checklist's `crew-gap` "next": "a stumble when the train sways, and a…](notes/375.md)
- [376. Bot nights that run hot (D1.2 for D1, queue #113; note 328's open end: "the harness has no top-speed driver yet")](notes/376.md)
- [377. Bots bring the powder (queue #114, D1.3 for D1; [orchestrator.md](design/orchestrator.md) §5.1 U4, §6.2 item 2: "it makes the guns a…](notes/377.md)
- [378. The rescue matched to the grab (queue #115, E1; the art checklist's `crew-rescue` "next": "matched to the victim's clip, and a pull free…](notes/378.md)
- [379. The orchestrator's pacing targets judged (D1.2 for D1, queue #116; [orchestrator.md](design/orchestrator.md) §4: "each is a `balance.json`…](notes/379.md)
- [380. Walkers who live through a hot run (D1.2 for D1, queue #117; found on note 376's first express night: 3 of 4 dead)](notes/380.md)
- [381. The grain elevator modelled (queue #118, C1; GDD §18's set pieces, the art checklist's `grain-elevator`)](notes/381.md)
- [382. Walkers out of the armoured engine (queue #119, D1; found in a 45-minute bot night with upkeep, 8 Oct)](notes/382.md)
- [383. HOLD KEYS: holds as toggles (F1, UI/UX 3; queue #120; GDD §32 "Accessibility")](notes/383.md)
- [384. The Gannet heard (AU1, queue #121; G1's note 340 and its four lines on the audio checklist; design `docs/design/creatures/gannet.md` §4)](notes/384.md)
- [385. The night's new jobs heard (AU1, queue #122; main's features since #95 that had no sound, found 8 Oct)](notes/385.md)
- [386. The settings in sections, and the loading screen looked at (F1, UI/UX 3; queue #123)](notes/386.md)
- [387. Doors you walk through: the yard's sheds, its hero and the Holdouts (queue #124, C1; the art checklist's `stop-building-doors`, note 279's…](notes/387.md)
- [388. G1's six new creatures heard, ahead of their sims (AU1, queue #125; G1's #99–#104, notes 362–367, and its nine lines on the audio checklist)](notes/388.md)
- [389. The rest of the lineside is solid (queue #126, B1; GDD App. F.1 "the world is solid": "the rest of the lineside (the roads' furniture, the…](notes/389.md)
- [390. Credits for what the game is built on (queue #127, B3; the checklist's `credits`; GDD App. E.6; CC BY's terms)](notes/390.md)
- [391. Captions for the sounds since CAPTIONS (AU1, queue #128; F1's note 349, GDD §32 "Accessibility")](notes/391.md)
- [392. Inside the stop's buildings heard as rooms (AU1, queue #129; C1's note 387 drew the sheds, the hero and the Holdouts walk-in on note 279's…](notes/392.md)
- [393. The slaughterhouse modelled (queue #130, C1; GDD §18's set pieces, the art checklist's `slaughterhouse`: "Animals are loud, and something…](notes/393.md)
- [394. The wreck yard's heaps as wrecked cars (queue #131, C1; GDD §18 "pull cargo off derailed trains. Unstable, unlit", note 187; the art…](notes/394.md)
- [395. More set dressing, the third slice: the trees (queue #132, E1; note 325's "not yet"; the director's notes of 7 Oct, GDD App. F.3: "the same…](notes/395.md)
- [396. The outside through a room's walls (AU1, queue #133; note 392's "not yet")](notes/396.md)
- [398. The set pieces modelled (queue #135, C1; ROADMAP M5, the demo slice: "set pieces still greybox"; GDD §18, notes 185 and 368; the art…](notes/398.md)
- [399. The relief driver, and a Climber in the cab clubbed out (queue #134, D1.3 for D1; D1's regression call, 8 Oct: "when the driver is dead,…](notes/399.md)
- [400. The conveyor line (A1, queue #136; spec D.2: "start machinery at a powerhouse, then clear jams as they occur. 1 + 1 roaming. Jams every…](notes/400.md)
- [401. Doors that shut, in the village houses (B4, queue #137; note 326's "not yet"; GDD §21, the Choir: "seizes anyone outside, on the roofs, or…](notes/401.md)
- [402. The knuckle swings open (queue #138, E1; the art checklist's `uncouple-anim` "next": "the knuckle's swing animated as it opens")](notes/402.md)
- [403. A bot crew works the whole yard on foot (B4, queue #139; GDD App. F.3: "work the yard together"; queue #89's loot on every siding, note 352)](notes/403.md)
- [404. TEXT BACKING (F1, UI/UX 3; queue #140; GDD §32 "Accessibility"; after note 349's captions)](notes/404.md)
- [405. The lane ahead, for the forward gun (queue #141, D1; [orchestrator.md](design/orchestrator.md) §5.3 6: "lanes ahead (runners crossing the…](notes/405.md)
- [406. The driver left on the ground: a far Holdout, and the walk back to the cab (queue #142, D1.3 for D1; found in D1.3's seed nights since…](notes/406.md)
- [407. The freed survivors' finish (queue #143, E1; the art checklist's `survivor-prisoner` and `survivor-wildlander` "next"s; GDD App. D.8)](notes/407.md)
- [408. The dead card says what there is to do, with the player's own keys (queue #144, B3; GDD App. D.6, D.7, D.10's UI row "Queue list and…](notes/408.md)
- [409. The village houses' doors heard (AU1, queue #145; B4's note 401: "a house shut up is behind a closed door")](notes/409.md)
- [410. The mine head's and the chemical works' buildings modelled (queue #146, C1; the art checklist's `mine-head` and `chemical-works`; GDD §18,…](notes/410.md)
- [411. The game stopped last time, said on the title (F1, UI/UX 3; queue #147; roadmap M6's crash reports)](notes/411.md)
- [412. Searching the open houses heard (AU1, queue #148; note 326's hiding spots)](notes/412.md)
- [413. The bots behind a house door when the Choir comes (B4, queue #149; GDD §21: the Choir takes "anyone ... not behind a closed door"; App.…](notes/413.md)
- [414. A bot on the forward gun (queue #150, D1; note 405's "not yet")](notes/414.md)
- [415. The walled town heard (AU1, queue #151; the towns since note 335, B2's notes 353 and 335; the director's notes of 8 Oct, towns that are…](notes/415.md)
- [416. The report's lines inked by what they cost (F1, UI/UX 3; queue #152; note 190's not-yet: "the HUD's report draws the new lines in the dim…](notes/416.md)
- [417. The barns, outbuildings and a dead town's goods shed open, their finds inside (B4, queue #153; level-design I.4's "barns and outbuildings…](notes/417.md)
- [418. The flank lanes (queue #154, D1; [orchestrator.md](design/orchestrator.md) §5.3 6: "lanes on the flanks from the open country's sides")](notes/418.md)
- [419. The car floors and roofs underfoot (AU1, queue #155; AU1's audit of the installed sets, after note 354's cobbles)](notes/419.md)
- [420. The foundry's buildings modelled (queue #156, C1; the art checklist's `foundry`; GDD §18 "overhead crane run from a gantry", §30…](notes/420.md)
- [421. The installed sounds' headroom (AU1, queue #157; note 419's "not yet")](notes/421.md)
- [422. The coaling tower modelled (queue #158, C1; the art checklist's `coaling-tower`; GDD §18 "gravity chute: fast, deafening, fills whether…](notes/422.md)
- [423. The tipple (A1, queue #159; spec D.2: "Clamp the car, rotate it to load. 1 crew. Bad clamp derails the car on the spur"; D.3: "tipple fails…](notes/423.md)
- [424. Water with life in it, and shores that meet it (queue #160, B1; the director, 8 Oct, GDD App. F.4: "lots of textures in the landscape…](notes/424.md)
- [425. The foundry's furnace heard (AU1, queue #161; C1's casting shed, note 420: "a furnace nobody tends"; GDD §18, §30 "oversized, partially…](notes/425.md)
- [426. Every key the HUD names is the player's own (queue #162, B3; T80's CONTROLS; note 285's ACTION : [KEY]; found on #144, note 408)](notes/426.md)
- [427. The wreck yard's dressing (queue #163, C1; the art checklist's `wreck-yard`; GDD §18 "derailed trains: unstable, unlit, already occupied";…](notes/427.md)
- [428. A stop building's walls both ways (AU1, queue #164; note 396's two not-yets, note 412's one)](notes/428.md)
- [429. The water heard (AU1, queue #165; the line plan's water, maritime-rules.md §2-5; B1's #160 makes it move on screen)](notes/429.md)
- [430. The conveyor line's art (queue #166, C1; A1's #136, note 400: "the conveyor's greybox only, its art left to C1"; spec D.2, D.3)](notes/430.md)
- [431. The rail joints' clack (AU1, queue #167; AU1's audit of the installed sets, after note 354's cobbles and note 419's floors)](notes/431.md)
- [432. The branches' pines are solid (queue #168, B1; GDD App. F.1 "the world is solid"; the last of notes 371's and 389's lineside the art dealt…](notes/432.md)
- [433. Holes in the land, found headless and closed (queue #169, B1; the director, 8 Oct, GDD App. F.4: "lots of textures in the landscape…](notes/433.md)
- [434. WISHLIST on the demo's title (F1, UI/UX 3; queue #170; T79's end card; GDD §35, whose checkpoint is wishlists a week)](notes/434.md)
- [435. Draggers off a truss (queue #171, D1; [orchestrator.md](design/orchestrator.md) §5.2 S4, "dropping onto the roofs from an overbridge or a…](notes/435.md)
- [436. The hand lamp's shadows (queue #172, E1; the art checklist's `crew-lantern` "next", "the light's flicker swinging its shadows"; GDD §31,…](notes/436.md)
- [437. Burns on a hot run (D1.2 for D1, queue #173; note 380's "left for next")](notes/437.md)
- [438. The top rung leaves a climber in the air at speed (queue #174, D1.3 for D1; found by D1.2 in #425's 8-bot express nights)](notes/438.md)
- [439. The bots in a car shut it up when the Choir comes (B4, queue #175; note 413's "not yet"; GDD §21, App. A.7)](notes/439.md)
- [440. Two bots fetch a heavy crate from the yard (B4, queue #176; note 403's "not yet"; level-design P8: the strongroom's crate at the yard's far…](notes/440.md)
- [441. The HUD's alarms and the radio inside the frame at every TEXT SIZE (B3, queue #177; GDD §32 "Alarms are rare and short", its…](notes/441.md)
- [442. Bots off the roofs for a truss Dragger (queue #178, D1.3 for D1; note 435's "not yet")](notes/442.md)
- [443. A flank pair abeam the engine, for the forward gun (queue #179, D1; note 418's "not yet"; [orchestrator.md](design/orchestrator.md) §5.3 6)](notes/443.md)
- [444. The truss Dragger's drop heard (AU1, queue #180; D1's #171, note 435)](notes/444.md)
- [445. The reverser thrown by hand, and the whistle's pull seen from the roofs (queue #181, E1; the art checklist's `crew-cab` "next", "the…](notes/445.md)
- [446. The townsfolk heard on their rounds (AU1, queue #182; B2's #389, note 353: towns that are lived in; AU1's note 415 heard the town standing…](notes/446.md)
- [447. The guns answer on a big crew's hot night (queue #183, D1.3 for D1; found by D1)](notes/447.md)
- [448. The seated gunner at a truss (queue #184, D1.3 for D1; note 442's "not yet")](notes/448.md)
- [449. Each stop's own modules (A1, queue #185; spec D intro: "POIs are procedurally assembled from a module grammar, so no two facilities operate…](notes/449.md)
- [450. Public and private runs, the password, and the run's mood (N1, queue #186; the director, 8 Oct 2026: "We should do public and private…](notes/450.md)
- [451. A scattered hound runs off, never blinks out (queue #187, D1; the guns' effect on creatures, note 290; note 328's scatter)](notes/451.md)
- [452. Reports to the studio (F1, UI/UX 3; queue #188; the director, 8 Oct: "You can point reports, crashes, logs, etc. into a nicely formatted…](notes/452.md)
- [453. A pair of cottages is two homes, each behind its own door (B4, queue #189; note 413's "not yet"; GDD §21: the Choir takes "anyone ... not…](notes/453.md)
- [454. The bots and the Gannet (queue #190, D1; note 340's "not yet": "the bots don't stop on a fold, or keep from hitting it")](notes/454.md)
- [455. The sheep turn their heads to whoever comes in (queue #191, E1; the art checklist's `livestock-anim` "next", "heads turning to whoever…](notes/455.md)
- [456. A dead or left-behind gunner's gun gets manned (queue #192, D1.3 for D1; note 447's "not yet", D1's question on #183)](notes/456.md)
- [457. The guard gun's powder while the guard van's held (queue #193, D1.3 for D1; note 447's "not yet", re-aimed with D1)](notes/457.md)
- [458. Nothing blinks out in sight of the crew (D1.2 for D1, queue #194; D1's #187, note 451: a scattered Cinder Hound was Gone the tick it broke…](notes/458.md)
- [459. The picture keeps its shape in any window (B3, queue #195; T83's display settings; the HUD's 480x270 canvas, note 347)](notes/459.md)
- [460. The Follower's nest built up over its 60 s (queue #196, E1; the art checklist's `follower-nest` "next", "the nest building up over its 60 s…](notes/460.md)
- [461. The switchyard's goods shed and the military depot's huts, wire and magazine modelled (queue #197, C1; the art checklist's `switchyard` and…](notes/461.md)
- [462. The open barns and sheds are rooms (B4, queue #198; note 417's "not yet"; GDD §28 "inside = warm, human, temporary safety", §31)](notes/462.md)
- [463. The express driver back to the fire (queue #199, D1; found by D1.3 on #183's 8-bot express sweep, seed 3)](notes/463.md)
- [464. A Holdout's breach seen at its lock and its barricade (queue #200, C1; the art checklist's `breach-states` "next", "the boards' splinters,…](notes/464.md)
- [465. A yard's walk-in sheds and its strongroom are rooms (B4, queue #201; note 462's "not yet"; note 387's shells; GDD §28, §31)](notes/465.md)
- [466. The conveyor line heard (AU1, queue #202; A1's #446, note 400: "its sound is AU1's: the belt, the drive and the jam, a line on the audio…](notes/466.md)
- [467. The extinguisher puts a cell out in a second (queue #203, D1; the director, 8 Oct 2026, on the test build: "Holding fire extinguisher on…](notes/467.md)
- [468. The capstan winch heard wherever it stands (AU1, queue #204; spec D.2 "Capstan winch: two players hand-crank in rhythm to drag cargo from…](notes/468.md)
- [469. The extinguisher heard on the fire (AU1, queue #205; the director, 8 Oct 2026: "Holding fire extinguisher on fire still doesnt feel like…](notes/469.md)
- [470. No derailment on a yard's track (queue #206, D1; the director, 8 Oct 2026, on the test build: "When turning into a yard, derailment is way…](notes/470.md)
- [471. Hounds aboard bite only who they can reach, and keep their own spots (queue #207, D1; the director, 8 Oct 2026, on the test build: "Cinder…](notes/471.md)
- [472. Hounds aboard patrol (queue #208, D1; the director, 8 Oct 2026, on the test build: "It matters that they dont just stand there and howl,…](notes/472.md)
- [473. The base jump higher (queue #209, D1; the director, 8 Oct 2026, on the test build: "I find our base jump is too weak as well.")](notes/473.md)
- [474. The townsfolk's personality matrix, and names to match (P1; the director, 8 Oct: "for townspeople I want to create a personality matrix ...…](notes/474.md)
- [475. Interior lighting that works (B4, queue #211; the director, 8 Oct: "I feel like interior lighting can be improved a lot, it feels almost…](notes/475.md)
- [476. The link's corner in note 285's form, and `--radio tally` without a route (B3, queue #212; found sweeping main's prompts after notes 441…](notes/476.md)
- [477. The hounds' patrol clips (queue #213, E1; for D1's #208, note 472; the director, 8 Oct: "It matters that they dont just stand there and…](notes/477.md)
- [478. The hounds heard on their patrol aboard (AU1, queue #214; D1's #208, note 472, the director, 8 Oct: hounds aboard "should either patrol…](notes/478.md)
- [480. The tipple heard (AU1, queue #216; A1's #458, note 423: "its sound (the clamp, the roll, the derail) AU1's"; spec D.2 "Clamp the car,…](notes/480.md)
- [481. A resumed night keeps the train as it left (A1, queue #218; spec E "Autosave per POI, on successful departure", "Crash: Session lost.…](notes/481.md)
- [482. The comet's green out of its car's seams (queue #219, C1; the art checklist's `comet` "next": "the green leaking out of the car's seams at…](notes/482.md)
- [483. A hound aboard seen as it patrols (queue #220, D1.3 for D1; note 472's "not yet")](notes/483.md)
- [484. Bots against a pack that moves (queue #221, D1.2 for D1; D1's #208, note 472: hounds aboard patrol the roofs, leap the gaps and drop in at…](notes/484.md)
- [485. Frost from the cab windows' edges, and breath on the glass (queue #222, E1; the art checklist's `cold` "next": "frost creeping in from a…](notes/485.md)
- [486. A stop hand goes round the train to its winch handle (queue #223, D1.3 for D1; found on #220's playthrough)](notes/486.md)
- [487. The Grumbler's healing seen (queue #224, E1; the art checklist's `grumbler-anim` "still to do": "a tell for its healing"; GDD App. A.8 "It…](notes/487.md)
- [488. The Gaunt's barn or shed is dark too (B4, queue #225; note 475's lanterns; level-design H.2 "the Gaunt's roost"; GDD §28)](notes/488.md)
- [489. The hounds' patrol heard by its moves (AU1, queue #226; D1's #208, note 472, the mode replicated as `CinderHound.Aboard`; E1's #213 clips,…](notes/489.md)
- [492. A bot crew takes a hand lamp out to the wreck yard's dark heaps (A1, queue #229; note 187's "not yet"; GDD §18 wreck yard "unstable, unlit")](notes/492.md)
- [493. A dead town's station stands open (B4, queue #230; note 417's "not yet": "the station, the derelicts and the powerhouse stay shut"; linegen…](notes/493.md)
- [494. The Grumbler's healing heard (AU1, queue #231; E1's #224, note 487, its healing seen; GDD App. A.8 "REGEN heals if only one player has hit…](notes/494.md)
- [495. Nobody goes into a burning guard van for powder (queue #232, D1.2; found on #221's hot runs, note 484)](notes/495.md)
- [496. The nearest hand puts out the Fire Flies' lamp at a stop, whatever its part (queue #233, D1.3 for D1; D1's data: 4-bot full nights losing…](notes/496.md)
- [497. The breach's blows and boards heard on their beats (AU1, queue #234; C1's #200, note 464: "AU1: the strikes' and the boards' sounds are…](notes/497.md)
- [499. The Track Doll's restlessness heard (AU1, queue #236; note 268's "not yet": "the doll has no recorded 'restless' sound of her own (the…](notes/499.md)
- [500. A resumed night keeps what it earned and what's aboard (A1, queue #237; note 481's "not yet"; spec E "Crash: Session lost. Campaign rolls…](notes/500.md)
- [501. The coupler's knuckle heard opening, a clank not a thud (AU1, queue #238; the weak-sounds audit)](notes/501.md)
- [503. Weak sounds: a body on the grating, and a crate set on concrete (AU1, queue #240; the weak-sounds audit, dull one-shots)](notes/503.md)
- [505. The Gaunt carries off what it took (queue #242, E1; the art checklist's `gaunt-anim`; GDD App. A.6 "it carries the body out at walking…](notes/505.md)
- [508. Bodies on the grating heard on steel (AU1, queue #245; the weak-sounds audit; note 503's fault in three more cues)](notes/508.md)
- [509. The yard's powerhouse stands open, its switchboard inside (B4, queue #246; note 417's "not yet": "the derelicts and the powerhouse stay…](notes/509.md)
- [510. Captions for the moments: what a creature does and how the train fails (AU1, queue #247; note 349's CAPTIONS, F1's; note 391; GDD §32; the…](notes/510.md)
- [511. A loose coupling calls the nearest walker (queue #248, D1.3 for D1; D1's data after note 496: the rakes "lost at Voss Grain Elevator")](notes/511.md)
- [512. The express driver backs off a dead line (queue #249, D1.2; found on #221's measurements, note 484; D1 asked why seed 2 stood)](notes/512.md)
- [513. The repair kit carried in one hand (queue #250, E1; the art checklist's `repair-kit` "next": "a one-hand toolbox carry clip, and a…](notes/513.md)
- [514. Developer builds and player builds (W1, queue #251; the director, 9 Oct 2026: "We need a way for these developer features to not ship in…](notes/514.md)
- [515. Record and replay a night (W1, queue #252; the director, 9 Oct 2026: "I love all this. Get on it and make sure we preserve performance")](notes/515.md)
- [516. Feedback from inside the game (W1, queue #253; the director is one person giving feedback on everything)](notes/516.md)
- [517. Review packets: what a pull request changed, in pictures and numbers (W1, queue #254; the director reviews everything, alone)](notes/517.md)
- [520. The director's inbox (W1, queue #257; the director, 9 Oct 2026: one person giving feedback on everything)](notes/520.md)
- [521. Coordination as data, first step: the shared docs merge on their own (W1, queue #258)](notes/521.md)
- [523. Tonight's build and the trends: main with the green pull requests on top, and the numbers per commit (W1, queue #260; D1 did both by hand:…](notes/523.md)
- [524. Live control and shots by subject (W1, queue #261)](notes/524.md)
- [526. Note 496's lamp claim, where it missed (queue #263, D1.3 for D1; found on #625's sweep, frontier:7 seed 6)](notes/526.md)
- [527. The first nights' controls card closes (queue #264, D1; the director, 9 Oct 2026, testing main's build: "we need a way to close the 'first…](notes/527.md)
- [528. A hound bites only who's on its ground (queue #265, D1.2; #221's follow-up, note 484; D1's ask, for the director's counter)](notes/528.md)
- [529. Come back: the camera cuts in on you getting up (queue #266, E1; the art checklist's `crew-freed` "next": "the camera cut in with it"; GDD…](notes/529.md)
- [530. The netcode audit against the P2P model of Lethal Company, REPO and PEAK (N2, queue #267; the director, 9 Oct 2026: "We don't want to be…](notes/530.md)
- [531. Bots gone at the start of a night (queue #268, D1; the director, 9 Oct 2026, testing main's build: "I tried to play with bots and they were…](notes/531.md)
- [532. The host's network off its frame loop (N2, queue #270; the netcode audit's gap 2, note 530)](notes/532.md)
- [533. Two hurt hands on the ballast at Renwick Yard and the driver waiting all night (queue #277, D1.3 for D1; D1.2's trace, frontier:7 seed 6…](notes/533.md)
- [534. The link's quality shown (F1, UI/UX 3; queue #271; netcode-audit.md gap 3; spec E "ping visibility is load-bearing. Without host migration,…](notes/534.md)
- [536. The Stoker's char close to (queue #278, E1; the art checklist's `stoker`, its audit of 5 Oct: "the arm it reaches out into the cab with is…](notes/536.md)
- [537. Warming up clear of the pack (queue #279, D1.2; #208's patrol meeting the warm-up; D1's frontier:7 seed 3 with #265)](notes/537.md)
- [538. The Passenger wrong up close (queue #280, E1; the art checklist's `passenger`, GDD App. A.8: "passes for crew in the dark; wrong up close")](notes/538.md)
- [539. Fire-fighters don't walk into a car that's well alight (queue #281, D1.2; note 495's not-yet, D1.3's frontier:7 4-bot seed 1 on main before…](notes/539.md)
- [540. A host told whose link is bad out on the line (F1, UI/UX 3; queue #282; note 534's "not yet"; spec E "ping visibility is load-bearing")](notes/540.md)
- [541. What cuts the couplings, and a VR hand that cuts one by mistake (queue #283, D1.3 for D1; from note 533's sweep)](notes/541.md)
- [542. Why couplings part with a bot crew aboard (queue #284, D1.3 for D1; from note 533's sweep: parted couplings 22 → 35 cars on frontier:7…](notes/542.md)
- [543. The Grumbler's body (queue #285, E1; the art checklist's `grumbler`, GDD App. A.8: "scuttles like a spider over the crane")](notes/543.md)
- [544. A fresh bot baseline for the pack and fire work (queue #286, D1.2 for D1; note 484's tables carried on)](notes/544.md)
- [545. Where a bot night's distance goes (queue #287, D1.3 for D1; D1.3's bot report, #671: frontier:7, 4 bots, `--enemies --upkeep`, 2,700 s,…](notes/545.md)
- [546. The Soot Child close to (queue #288, E1; the art checklist's `soot-children`, GDD App. A.6: "a child calling for help: black eyes,…](notes/546.md)
- [547. Crew freezing on the train's roofs (queue #289, D1.2 for D1; D1.3's nine-night report, #671: froze 3 → 10, nine of them "Froze, left behind…](notes/547.md)
- [548. The Gaunt's legs as branches (queue #290, E1; the art checklist's `gaunt`, GDD App. A.6: "lonely, spindly"; note 132: "grey bark: asleep,…](notes/548.md)
- [549. The slow frames written down (queue #291, D1; the director, 9 Oct 2026, of main's build: "an extreme borderline unplayable performance drop…](notes/549.md)
- [550. A train run down a facility's spur outside a stop (queue #292, D1; D1.2's find on #289: frontier:7, 4 bots, seed 8 on #289's build)](notes/550.md)
- [551. The mauled deaths: too hurt for the pack, into cover (queue #293, D1.2 for D1; note 544's baseline: mauled 5 → 10 dead at dawn on…](notes/551.md)
- [552. The lone driver off a bridge's deck (queue #294, D1; D1.2's find on #293: frontier:7, 4 bots, seed 4)](notes/552.md)
- [555. An invite accepted while playing joins in-process (N2, queue #272; the netcode audit's gap 4, note 530; note 24's relaunch)](notes/555.md)
- [556. The two-machine test in the nightly soak (N2, queue #276; the netcode audit's gap 8, note 530; note 450's run by hand)](notes/556.md)
- [557. A weak link adapts (N2, queue #273; the netcode audit's gap 5, note 530)](notes/557.md)
- [570. Dave, the wandering painter (P1, queue #300; the director, 8 Oct 2026: "a special NPC that shows up randomly in places. His name is Dave…](notes/570.md)
- [571. Nicki's party (P1, queue #301; the director, 8 Oct 2026: "an NPC you can find some times in one of the houses. Her name is Nicki and she's…](notes/571.md)
- [572. Jacob, the fisherman (P1, queue #302; the director, 8 Oct 2026: "an NPC named Jacob who can be found randomly in the world near water…](notes/572.md)
- [580. Held: the camera cuts to third person on what's holding you (D1.4, queue #312; the director, 9 Oct 2026, playtest: "When you are being held…](notes/580.md)
<!-- end of the notes index -->
