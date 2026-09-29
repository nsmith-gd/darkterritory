# Roadmap

Anchored to the GDD's gates: **public demo December 2026**, Next Fest 22 Feb 2027, launch mid–late March 2027.

**Schedule risk, stated plainly:** building the engine and the game in the same window leaves about ten weeks to the December demo. The plan below front-loads the systems that answer the GDD's riskiest design questions (spec G.1: does the 4:1 speed ratio feel right?) and defers engine polish. If M2 slips past early November, the demo should drop VR and ship flat-screen first.

| Milestone | Target | Deliverable | Exit test |
|---|---|---|---|
| **M0 — Foundation** | done | Solution, CI, `dt` CLI, data + hot reload, train longitudinal sim pinned to spec B, loopback transport | `dotnet test` green. `dt train table` reproduces spec B.4–B.6. |
| **M1 — Feel prototype** | ready for play-test | SDL3 window, Vulkan greybox renderer, offscreen screenshots, FPS controller, rail spline, train you can drive and walk on (moving frames §6.1), jump-off death, Jolt crates | Director plays it and answers spec G.1 and G.2. Agent can screenshot any scene headless. |
| **M2 — Crew of eight** | in progress | Snapshot replication, prediction/reconciliation, interpolation, physics object sync, Steam transport + lobby, bots over loopback, `dt harness` skeleton | 8 clients on `LinkConditions.Rough` with no desync over 30 min, run by the harness overnight |
| **M3 — Voice** | +5.5 wk | Capture, Opus, host routing, proximity falloff, occlusion, radio item, dead channel | 8-player voice test. Radio dies in a tunnel. |
| **M4 — VR** | +6.5 wk | OpenXR, stereo, action mapping, hand interactions (shovel, levers, ladders), VR body IK, comfort options | Quest via Link and SteamVR both playable in the M1 scene |
| **M5 — Demo slice** | +9 wk | Procedural line v1, one fortress, two facilities, 5 demo enemies (one per pressure zone), director budget, audio mixer + tier ducking, art pass to style sheet, simple editor (module + rail + tuning) | Screenshot test (GDD §32) passes director review. Harness sweeps the demo roster. |
| **M6 — Demo ship** | Dec 2026 | Steam demo build, itch build (EOS transport), crash reporting, settings | Steam demo live |
| M7 — Next Fest | Feb 2027 | Mod loader v1, more facilities and enemies, balance sweeps | |
| M8 — Launch | Mar 2027 | Full roster pass, economy, save slots | |

## Status

**Since M1:**
- **Boiler, walkable cab and resistance:** spec B.6 is pinned.
- **Rakes:** cutting, coupling and collision damage.
- **Procedural routes:** tiers, facilities, tunnels, bridges, and hazards as level content.
- **Play a night:** `DarkTerritory -- --route frontier:7` runs one.

**M1 (feel prototype):** playable. `dotnet run --project src/DarkTerritory.App`. Rail line model, train on the line with mass-weighted grade, moving car frames, first-person motor (roof and ground speeds, gap jumps, ladders, lethal jump-off), Vulkan greybox renderer with a pixelated low-res look, headless screenshots. Waiting on the director to answer spec G.1 and G.2. Not in M1 yet: Jolt crates.

**M2 (crew of eight):** the netcode core exists and is exercised by `dt harness` over a lossy loopback network:
- **Model:** host-authoritative 30 Hz ticks, intent-only input with 4× redundancy, snapshots with an input ack, and client prediction plus reconciliation of both the player and the train.
- **Remote players:** interpolated 100 ms behind, in car-local frames.
- **Results:** prediction is exact on a perfect link. On a rough link (90 ms, ±20 ms, 3% loss) the worst correction is under 1 m, with a handful of corrections per client over 5 minutes.

- **Snapshots** are fixed-point records delta-encoded against the client's last acked snapshot. The host adopts the quantised state itself, so prediction stays exact. At 20 cars with 8 players they're about 110 bytes, around 28 kbit/s down per client (target ≤ 64).

Remaining for M2:
- interest management (only needed once enemies and props multiply the record count)
- a Steam transport and lobby
- physics object sync
- inert bodies on disconnect
- hitscan lag compensation
