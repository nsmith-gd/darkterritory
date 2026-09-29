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
| Networking | `ITransport` + **Steam Networking Sockets** (SDR relay, Steam lobbies) / **Epic Online Services P2P** for itch.io / **UDP** for LAN and direct IP (exists) / **loopback** for tests and bots (exists) | EOS gives NAT-punch + relay + lobbies free, no servers for us to run, and no Epic account needed (device-ID login). |
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
    - **What it is.** `UdpTransport` is the first real network backend: LAN and direct-IP play, for playtests and itch.io builds before Steam and EOS. It uses one non-blocking socket, polled once per tick with no threads.
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
      - drop-in at POIs;
      - autosave;
      - the Vigil (spec C.2);
      - an oncoming train as the dawn failure, rather than the run just ending.
