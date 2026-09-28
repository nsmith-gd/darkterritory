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
| VR | **OpenXR** (`Silk.NET.OpenXR`), `XR_KHR_vulkan_enable2`, multiview stereo | PCVR first (Link / Air Link / Virtual Desktop / SteamVR). Standalone Quest (Android) is a post-launch option that this stack does not rule out. |
| Physics | **Jolt** (`JoltPhysicsSharp`) | Rigid bodies, ragdolls (from the animation skeleton), `CharacterVirtual`, constraints. Deterministic build flag available. |
| Train | **Custom 1D rail sim** (`DarkTerritory.Sim/Train`) | Cars live on the rail spline, driven by longitudinal dynamics, and appear to Jolt as kinematic bodies. Already implemented and pinned to the spec. |
| ECS | **Friflo.Engine.ECS** or **Arch**, decided in M1 | Needs: fast queries, struct components, and component change tracking for replication |
| Audio | **Custom mixer** in C# + **miniaudio** device I/O + **Steam Audio** (HRTF, occlusion) | See §6 |
| Voice codec | **Opus** (libopus via P/Invoke, `Concentus` as pure-C# fallback) | |
| Networking | `ITransport` + **Steam Networking Sockets** (SDR relay, Steam lobbies) / **Epic Online Services P2P** for itch.io / **loopback** for tests and bots | EOS gives NAT-punch + relay + lobbies free, no servers for us to run, and no Epic account needed (device-ID login). |
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
- Jolt runs in a **floating origin** centred on the train and rebased periodically. Cars are kinematic bodies with correct velocity, so friction carries bodies resting on them.
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

### 6.4 Audio (spec A) — why not FMOD
FMOD's power lives in FMOD Studio, a GUI authoring tool whose projects an agent cannot sensibly author or verify. The spec's needs are specific and small: six tiered buses with sidechain ducking, parameter-driven layered events, voice limiting, positional slack-action delay chains, and heavy procedural synthesis. We build this in C#:
- **Sound graphs as data** (`content/audio/*.json`): layers, parameters, DSP chains, synthesis nodes (filtered noise, resonators, granular, mass-spring, formant).
- **Tier ducking** exactly per spec A.3. Tier 1 is never ducked or occluded beyond −6 dB.
- **Offline render**: the mixer can render any sim state to a buffer. The harness then *measures* tell audibility against the bed, which makes spec A.3's "tier 1 is inviolable" an automated test.
- Steam Audio for HRTF (spec A.4: ~30° localisation, and essential in VR) and geometric occlusion.

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
