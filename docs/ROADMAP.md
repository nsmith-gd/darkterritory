# Roadmap

Anchored to the GDD's gates: **public demo December 2026**, Next Fest 22 Feb 2027, launch mid–late March 2027.

**Schedule risk, stated plainly:** building the engine and the game in the same window leaves about ten weeks to the December demo. The plan below front-loads the systems that answer the GDD's riskiest design questions (spec G.1: does the 4:1 speed ratio feel right?) and defers engine polish. If M2 slips past early November, the demo should drop VR and ship flat-screen first.

| Milestone | Target | Deliverable | Exit test |
|---|---|---|---|
| **M0 — Foundation** | done | Solution, CI, `dt` CLI, data + hot reload, train longitudinal sim pinned to spec B, loopback transport | `dotnet test` green. `dt train table` reproduces spec B.4–B.6. |
| **M1 — Feel prototype** | ready for play-test | SDL3 window, Vulkan greybox renderer, offscreen screenshots, FPS controller, rail spline, train you can drive and walk on (moving frames §6.1), jump-off death, Jolt crates | Director plays it and answers spec G.1 and G.2. Agent can screenshot any scene headless. |
| **M2 — Crew of eight** | in progress | Snapshot replication, prediction/reconciliation, interpolation, physics object sync, Steam transport + lobby, bots over loopback, `dt harness` skeleton | 8 clients on `LinkConditions.Rough` with no desync over 30 min, run by the harness overnight |
| **M3 — Voice** | +5.5 wk | Capture, Opus, host routing, proximity falloff, occlusion, radio item, dead channel | 8-player voice test. Radio dies in a tunnel. |
| **M4 — VR** | in progress | OpenXR, stereo, action mapping, hand interactions (shovel, levers, ladders), VR body IK, comfort options | Quest via Link and SteamVR both playable in the M1 scene |
| **M5 — Demo slice** | +9 wk | Procedural line v1, one fortress, two facilities, 5 demo enemies (one per pressure zone), director budget, audio mixer + tier ducking, art pass to style sheet, simple editor (module + rail + tuning) | Screenshot test (GDD §32) passes director review. Harness sweeps the demo roster. |
| **M6 — Demo ship** | Dec 2026 | Steam demo build, itch build (EOS transport), crash reporting, settings | Steam demo live |
| M7 — Next Fest | Feb 2027 | Mod loader v1, more facilities and enemies, balance sweeps | |
| M8 — Launch | Mar 2027 | Full roster pass, economy, save slots | |

## Status

**Since M1:**
- **Boiler, walkable cab and resistance:** spec B.6 is pinned.
- **Rakes:** cutting, coupling and collision damage.
- **Procedural routes:** tiers, facilities, tunnels, bridges, and hazards as level content.
- **Play a night:** `DarkTerritory -- --route frontier:7` runs one, enemies and all (`--no-enemies` for a quiet line). The HUD prints text cues for the telegraphs until there's audio.
- **Guns and the Choir:** two mounted guns with real arcs, and the Choir's global aggro.
- **Demo roster and director (M5's "5 demo enemies"):**
  - Sleepers, Cinder Hounds, Clingers and the Hollow run on the shared five-state spine, with the fairness rule enforced in code.
  - The pressure director follows App. B.1.
  - Greybox stand-ins for each: `dt screenshot --threats`.
  - `dt harness --route frontier:7 --enemies` plays a whole night with bots and reports pacing, punishes, deaths by cause and fairness violations.
- **Audio (M5's "audio mixer + tier ducking"):**
  - A synthesised train bed, slack action, and the five demo tells, all from data, through a tiered mixer.
  - `dt audio render` produces a WAV plus a spectrogram. `AudioTests` holds spec A.3's "tier 1 is inviolable" in maximum chaos.

**M1 (feel prototype):** playable. `dotnet run --project src/DarkTerritory.App`. Rail line model, train on the line with mass-weighted grade, moving car frames, first-person motor (roof and ground speeds, gap jumps, ladders, lethal jump-off), Vulkan greybox renderer with a pixelated low-res look, headless screenshots. Waiting on the director to answer spec G.1 and G.2. Not in M1 yet: Jolt crates.

**M2 (crew of eight):** the netcode core exists and is exercised by `dt harness` over a lossy loopback network:
- **Model:** host-authoritative 30 Hz ticks, intent-only input with 4× redundancy, snapshots with an input ack, and client prediction plus reconciliation of both the player and the train.
- **Remote players:** interpolated 100 ms behind, in car-local frames.
- **Results:** on a perfect link, prediction is exact except where another player's cab input reaches you a round trip late (under a millimetre). On a rough link (90 ms, ±20 ms, 3% loss) the worst correction is under 1 m, with a handful of corrections per client over 5 minutes.

- **Snapshots** are fixed-point records delta-encoded against the client's last acked snapshot. The host adopts the quantised state itself, so prediction stays exact. At 20 cars with 8 players they're about 110 bytes, around 28 kbit/s down per client (target ≤ 64).

- **UDP transport:** real sockets, direct IP or LAN. `--host` / `--join` in the app. `dt harness --udp` runs 8 bots over localhost sockets with exact prediction.

**M3 (voice), core done:**
- Proximity voice with spec A.5's falloff and cab-wall occlusion.
- The walkie-talkie (T), dead in tunnels.
- The dead channel.
- Opus, host-routed so a listener is sent only what reaches them.
- Measured end to end, headless.

Remaining for M3: Soot Children mimicry, the radio as an item, and interior occlusion (after car interiors).

**Designer tools (level editor v1):**
- `dt edit` opens a local editor page for every tuning and sound value. Edits keep the comments, are validated, and are hot-reloaded by the running game.
- A route editor: generate, see the plan and profile, edit the features, save, and play with `--route-file`.

**Physics:**
- Crates, lamps and crewmates' bodies, host-simulated in car frames.
- Pick up (E), put down (E) and throw (right mouse).
- Bodies persist, can be carried, and are revived at the gate if brought home.

**The run:**
- A night is a game: through the gates, a stop at the coaling tower (a working gravity chute), the terminus, and pay by spec F.1.
- Dawn, derailment or losing the crew ends it.
- The harness reports the outcome and the payout.

**The campaign (spec E, F):**
- Scrip, cars on F.2's curve, F.3 upgrades (five modelled), a board of contracts by F.4 tier, and three text save slots.
- Autosave on leaving each facility, and `--resume`.
- `dt campaign sim` holds F.4's 55–60 nights to 20 cars. `dt campaign play` settles a bot night.

**Switches and dead lines (GDD §17, App. A.7):**
- Junctions have real branches: a turnout, then a dead line alongside the main line to a buffer stop.
- Rakes take the branch the switch is set for, and back out without a choice.
- Switches are thrown by hand at a stand beside the points, never under a wheel, and their lamp reads the setting from the cab.
- `dt screenshot --route frontier:7 --junction 0 --diverge --through` shows one.

**Loading at facilities (spec D):**
- Manual crates you carry into the cars (heavy: slow, no climbing).
- A capstan winch that needs two on it.
- Cars leave the fortress half full, so the facilities are where the money is.
- `dt screenshot --site` shows a stop.

**HUD:**
- A pixel-font HUD in the low-res frame: engine gauges, ping to host (spec E), the prompt for what your hands can do, and the night.
- `dt screenshot --hud` captures it.

**Cold and the Vigil (spec B.2, C.2):**
- Cold exposure is predicted like movement: 200 s to onset, 320 s to death, recovered near heat.
- The Vigil revives a body laid in the engine at a dead stop, for 90/120/150 s of maximum exposure. Engine off, lights to emergency, guns dead (they need steam now), the Choir at maximum.
- The revived come back cold.

**Car interiors:**
- Walk-in cars and a guard van with the back door.
- Replicated doors.
- Shelter rules shared by the Choir, voice and sound.
- Warm practical light inside.

**Session rules (M2):**
- Interest management for enemies and bodies (520 m, past the farthest tell), with per-client baselines.
- Someone who drops out leaves an inert body.
- Joiners with different tuning are refused, with the files named.
- Mid-run joiners wait and board at the next stop (spec E drop-in at POIs).

**Steam (M2):**
- Friends-only lobbies, overlay invites, "Join Game" and `+connect_lobby`, over Steam's relayed P2P.
- The same protocol as UDP, so a host can take Steam friends and LAN players at once.
- `dt harness --online` runs 8 bots through a lobby on a fake Steam. `dt online check` checks a machine's Steam setup.
- It needs Valve's `steam_api64.dll` in `external/steam/`.

Remaining for M2:
- a first run on real Steam between two accounts (needs the SDK library and two machines)
- the 30-minute, 8-client rough-link soak as a nightly job
- EOS for itch.io moves to M6 with the itch build

**M4 (VR), foundation done:**
- An OpenXR session with stereo rendering: `--vr` in the app, mirrored to the window.
- The headset's pose on the player's body, in the car's frame.
- Verified headless on Monado's simulated headset: `tools/xr-sim.sh`, then `dt vr check`. CI runs it on every push.
- **Controllers and comfort (T26):**
  - OpenXR actions bound for Touch, Index and the simple controller.
  - Head-relative walking through the same intent path as the keyboard.
  - Snap or smooth turn, a locomotion vignette (`tuning/vr.json`), and the player's hands drawn.

Remaining for M4:
- hand interactions
- body IK
- the HUD in the headset
- multiview
- the exit test on a real Quest and SteamVR
