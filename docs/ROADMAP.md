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
- **Procedural line v1 (M5, docs/design/linegen-plan.md):** every night's line comes from the line generator: a route graph with alternates, dead lines and facility spurs, set pieces scripted to a budget curve, clothoid alignment and a vertical profile, a terrain field the players stand on, authority and its tells (boards, Form 19, the route card), director context, and validation by driving it on the real train sim. `dt linegen generate|sweep|debug|bench`; `C` shows the route card, `F3` the overlay, `--ride` rides a line. ARCHITECTURE §6.10 and §8 note 66.
- **Generated stops (level-design Parts D and Z):**
  - Every facility has a yard of nested spurs, and many have a village nearby. Village halts sit between the facilities.
  - Each tier is harder than the last, by a measured difficulty band.
  - Loot comes from the run's economy: crates, strongrooms, castings under yard gantries, and village finds that pay once stowed aboard.
  - `dt site` draws a plan, and `dt site sweep` sweeps a tier.
- **Play a night:** `DarkTerritory -- --route frontier:7` runs one, enemies and all (`--no-enemies` for a quiet line). The HUD prints text cues for the telegraphs until there's audio.
- **Guns and the Choir:** two mounted guns with real arcs, and the Choir's global aggro.
- **Demo roster and director (M5's "5 demo enemies"):**
  - Sleepers, Cinder Hounds, Clingers and the Hollow run on the shared five-state spine, with the fairness rule enforced in code.
  - The pressure director follows App. B.1.
  - Greybox stand-ins for each: `dt screenshot --threats`.
  - `dt harness --route frontier:7 --enemies` plays a whole night with bots and reports pacing, punishes, deaths by cause and fairness violations.
- **GDD v1.3's three at the stops (post-demo roster):** the Shy Thing, the Huddle and the Mimic, on the spine with greybox stand-ins, their tells and screenshot staging (ARCHITECTURE §8 note 164). Not in the demo edition. Models to come.
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

**The radio as a thing (T41):**
- Worn on the belt, one each; the train leaves with two (`train.json` `kit`).
- Only those wearing one are on it, and it's dead down mine spurs as in tunnels.
- The dead drop theirs.
- Occlusion through a shut car is pinned by a test.

**The Soot Children (T40, App. A.4):**
- Outside a car with crew shut in, they call for help in the voice of someone who isn't there: the host replays what that crewmate last said.
- The call plays at one loudness however far away (spec A.5's tell).
- Open the door to it, or go out, and it takes you. Ignored for 30 s, it gives up.
- Never for a solo crew, and only after somebody's spoken.

M3 is done but for a test with eight people.

**Designer tools (level editor v1):**
- `dt edit` opens a local editor page for every tuning and sound value. Edits keep the comments, are validated, and are hot-reloaded by the running game.
- A route editor: generate, see the plan and profile, edit the features, save, and play with `--route-file`.
- **v2 (T44), M5's "module + rail + tuning":**
  - the track itself, piece by piece (length, curve, grade);
  - each facility's own loading modules (crates, the winch), or its kind's;
  - the facilities', the Vigil's and the campaign's tuning too.

**Physics:**
- Crates, lamps and crewmates' bodies, host-simulated in car frames.
- Pick up (E), put down (E) and throw (right mouse).
- Bodies persist, can be carried, and are revived at the gate if brought home.

**The run:**
- A night is a game: through the gates, a stop at the coaling tower (a working gravity chute), the terminus, and pay by spec F.1. It starts with the engine at the gate, ready to depart (ARCHITECTURE §8 note 100).
- Dawn, derailment or losing the crew ends it.
- The harness reports the outcome and the payout.

**Builds for players (M6):**
- `tools/package.sh` makes self-contained Windows and Linux folders with their content, and CI keeps both as artifacts on every push, after starting the Linux one from elsewhere and playing it.
- Crashes leave a report in the user's app data.
- **The demo edition (T79, GDD §21, §35):**
  - the demo roster of five (Sleepers, Cinder Hounds, Clingers, the Hollow, and the Choir), the trouble in the cars, and the Frontier with two facilities and no Grease;
  - it's an overlay, `editions/demo`, laid over the content like a mod: `--edition demo` plays it from the repo, and `tools/package.sh --demo` bakes it into `DarkTerritory-Demo-<rid>` beside the game;
  - its title says DEMO, its menu is a quick night on the Frontier (no campaign), and a night over ends on a wishlist line; asked for another tier, it plays the Frontier;
  - `tools/upload.sh --demo` sends only a demo build, and the game's upload refuses one.
- **Store uploads (T38):**
  - `tools/upload.sh steam|itch [--demo]` sends them through `steamcmd` or `butler`, and never sets Steam's default branch live;
  - CI dry-runs both on every push;
  - the hand-run Release workflow does it for real, once there are app IDs and an itch page (`tools/store/README.md`).

**Front end (T30):**
- `DarkTerritory` opens on a title screen with Campaign (three slots, and the fortress between nights: the board, cars, upgrades), Quick night, Join, Settings and Quit.
- Nights return to the menu when they're over.
- Settings are saved: sound, voice, HUD, VR comfort, mouse.
- `dt screenshot --menu fortress` shows it.

**The campaign (spec E, F):**
- Scrip, cars on F.2's curve, F.3 upgrades (five modelled), a board of contracts by F.4 tier, and three text save slots.
- Autosave on leaving each facility, and `--resume`.
- `dt campaign sim` holds F.4's 55–60 nights to 20 cars. `dt campaign play` settles a bot night.

**Switches and dead lines (GDD §17, App. A.7):**
- Junctions have real branches: a turnout, then a dead line alongside the main line to a buffer stop.
- Rakes take the branch the switch is set for, and back out without a choice.
- Switches are thrown by hand at a stand beside the points, never under a wheel, and their lamp reads the setting from the cab.
- `dt screenshot --route frontier:7 --junction 0 --diverge --through` shows one.
- **The Draggers (T46, App. A.4):**
  - under a car's edge, woken by someone on the roofs;
  - a limb and a scrape at the lip, then a grab: alone, you're pulled off (at speed, that's death); with a mate near, they have two seconds to pull you free;
  - the centreline is safe, and they reach half as far again at max speed (spec B.3);
  - `dt screenshot --threats` shows one reaching.
- **The Rattle (T51, App. A.5):**
  - in a coupling gap during a facility stop, mid-train by preference;
  - silent until someone comes near below the roofs, then a dry bone rattle;
  - step into the gap while it rattles and you're pulled under; wait it out, or go over the roof;
  - bots stop short of a rattling gap.
- **The Deadman and the Stoker (T53, App. A.5):**
  - leave the cab empty (not on Local routes) and the controls start clicking; still empty at 30 s (20 on Deep territory), it takes the cab, locks the regulator open and the brake off, and it takes 4 s and some blood to get it back;
  - leave the firebox unattended at a stop and a Stoker gets in: the pressure climbs with the valve held shut and the fire turns green; vent to hold it, or drive it out at the firebox and get burned.
- **The Lamplighters (T52, App. A.6):**
  - out in the dark beside the engine; a lit lamp brings them in, eyes catching the light;
  - reaching it, they smash the lamp (out for 45 s) and go for the cab;
  - lamps down (L in the cab) and they lose track, but then the Sleepers only show close: the driver bot slows down in the dark;
  - `dt screenshot --threats` shows one.
- **Contradiction seeding (T64, App. B.1):** the director weighs up whatever would make a pair from the conflict table with what's about (Lamplighters with Sleepers ahead, Clingers before a grade, the Deadman at a facility), more so past halfway on a run short of its pair. It saves up for the Gaunt so that it comes at all. The harness reports each night's pairs.
- **The Drift (T63, App. A.4):**
  - over the line's bogs and tar ponds, a dark mass rides over and alongside the train, spreading;
  - move in it and it comes for you, rustling like dry reeds; once it's on you it eats at you as long as you keep moving;
  - stand stock still for four seconds and it loses you. Bots it's after stand still.
- **Followers (T62, App. A.3):**
  - at a stop, out on the grounds, something comes up behind one of the crew and keeps to their back, in their step: the others see it, and they never can (the host doesn't send it to them);
  - back aboard with it, it goes to a dark cargo car and nests there, at anyone who comes in; take a lamp in and it runs;
  - the counter is a call: stand still where someone can see it, and it lets go. Bots call it on each other.
- **The Passenger (T61, App. A.7):**
  - boards at a facility stop on the Dead lines and beyond, wearing a crewmate's face, and goes about the train like one of them: the same job over and over, and never a word;
  - the HUD's ABOARD count reads one too many; whoever's alone in a car for long enough with it is taken, and it wears their face after;
  - two in a car keeps it off; face it and press Use to call it out, and it runs. Bots call it out when they're in a car with it.
- **The Gaunt (T60, App. A.4):**
  - on the roofs at a stop or out of a tunnel: dead still while anyone's looking at it, and silent;
  - unwatched it comes along the roofs and takes whoever's nearest; watched for a minute without a break, it goes;
  - the roof-walking bots stop and watch it. `dt screenshot --threats` shows it.
- **The Weight (T59, App. A.3):**
  - buried at a water crossing, it takes the rear coupling as the last car passes: the train lurches, and a deep scraping starts at the rear;
  - it drags harder than the engine can pull, and brought to a stand it pulls the car off the rails;
  - cut the rear car loose, or get down to it and beat it off (five blows); it can't be shot.
- **Climbers (T58, App. A.4):**
  - they run alongside and mount only at a coupling gap, scrabbling there first; stand in the gap and they try another;
  - up and along the roofs toward the engine, then into the first car nobody's in (the guns can take them on the roofs);
  - the more cars, the more gaps: the director weighs them by gap count. `dt screenshot --threats` shows two.
- **The Long Whistle (T57, App. A.2):**
  - a horn on the line ahead, just short of a bend or a grade, and no train: the horn is out of tune and doesn't bend as you close;
  - brake hard for it and the stop is the punishment; ignored, it sounds twice more and gives up;
  - never alone (the director only sends it with another threat about), and a crew that braked for it draws the Ferryman.
- **The Ferryman (T56, App. A.2):**
  - a lantern on a long straight ahead, waving the train down, seen from far out;
  - slow for it and it comes down the line, boards and kills whoever's driving; hold your speed and it steps aside for good;
  - the driver bot holds its speed past it, even with the lamps down;
  - `dt screenshot --threats` shows one.
- **The Switchman (T37, App. A.7):**
  - it throws a junction ahead for its dead line: the lamp reads wrong, and a figure with a lantern stands at the stand;
  - it flees anyone on the ground, but chasing it off doesn't set the switch back;
  - crew bots stop short, set it back on the ground, and go on; left alone, the train takes the dead line and backs out, costing the clock, never a life;
  - `dt screenshot --threats` shows it.

**Loading at facilities (spec D, GDD §17):**
- Manual crates you carry into the cars (heavy: slow, no climbing).
- A capstan winch that needs two on it.
- A gantry crane at the foundry (T48): one up in the cab drives it, one on the ground rigs the castings, a casting set down on a car's roof is loaded, and one let go of high kills whoever's under it. `dt screenshot --site --crane`.
- Crew bots run it (T54): the winch pair, one at the controls and one rigging, before the winch; `dt harness` counts the castings loaded at each stop.
- Cars leave the fortress half full, so the facilities are where the money is.
- Every facility but the coaling tower is down a spur that takes the engine and four cars. Cut the rest, run the empties in, load, back out and recouple, set the switch back, go. The night autosaves as you leave.
- `dt facility drill` plays the sequence headless; `dt screenshot --site` shows a stop.
- **Crew bots work it (T32):**
  - the driver on the regulator, a shunter on the coupler and switch, and two on the winch, all through intent;
  - `dt harness --route frontier:6` reports each stop's legs and the sleds loaded.
- **Coaling (T35):** the crew stops at a coaling tower when the tender has room, and works the chute from the ground.
- **Crates go in by the side door (T34):**
  - cargo cars have a sliding door on each side with steps up to it, so you walk freight in;
  - crate hands carry the stack in and shut the doors after.

**Mods v1 (T49, M7's "mod loader v1"):**
- Folders in `mods/` (or app data) laid over `content/`: they add files, replace them, or `$patch` a JSON file one key at a time.
- The game reads the merged copy. The content hash keeps a crew on the same mods, and a refused joiner is told which mods differ.
- `dt mods` shows what's loaded; `--no-mods` gives the base game.
- **Through Thunderstore (T78):** a mod is a Thunderstore package (`manifest.json`, README, icon, `content/`), dependencies load first, mod managers hand the game their profile with `--mods-dir`, and `dt mods pack` checks one against the site's rules and zips it. `tools/mods/example` is the modder's guide.

**Balance sweeps (T55, M7, GDD §34):**
- `dt balance` runs harness nights over tiers, seeds, crew sizes and train lengths, side by side.
- It checks "survivable at 2, non-trivial at 8" and fairness, and reports by train length for the progression-cap question.
- It runs nightly in the soak.

**Art pass v1 (T39, M5's "art pass to style sheet"):**
- Greybox surfaces are weathered to GDD §27, standing in for textures: blocky grain at 128 px/m, soot fields, rust patches and streaks, a baked shadow low down, and harsh speculars on iron and brass.
- The wear rides with each car. Materials come from the palette (`tuning/look.json`), and `--greybox` gives flat colour to compare.
- Screenshots are deterministic now: the pines stopped moving between runs.

**Art pass v2 (the art & animation pipeline plan):**
- A renderer with the plan's post stack: textures, per-pixel practical lights, a sky with a backdrop, height fog, effects, bloom, a LUT grade, grain and dither.
- The train, the track and the lineside, bridges, tunnels, fortresses and facilities, built from kits over the sim's own shapes.
- A texture library from CC0 photo sources and procedural generators, with provenance.
- `dt art check` holds every piece to its triangle budget; `dt art show <piece>` puts one on a turntable.

**HUD:**
- A pixel-font HUD in the low-res frame: engine gauges, ping to host (spec E), the prompt for what your hands can do, and the night.
- `dt screenshot --hud` captures it.

**Pacing (after the 100-night playtest: "a reward or a problem every 30 s at most, ideally 20"):**
- The world logs each moment (a threat showing itself, a board, a bag), and counts the quiet out on the line. `dt harness` reports it.
- At 18 s of quiet the director sends something, cooldown or not. It prefers kinds it hasn't sent lately, and its caps count only threats that are engaged.
- Mail cranes line the route with a board before each. Hook a bag from an open cargo side door as it passes: pay, coal, rounds or spares.
- Nights measured in the harness: longest quiet 21–26 s, typical 12–16 s.

**Trouble inside the cars (after the 100-night playtest):**
- A car fire: smoke, then flames. It burns the cargo, the car, and whoever's inside, and spreads at full blaze. You beat it out from inside.
- A loose load: straps groaning. On a hard brake, slack action, or after a minute and a half, it comes down across the aisle and crushes whoever's beside it. You lash it from inside.
- Gnawers: a nest in the cargo that eats it, breeds, and bites anyone in the car. You stamp them out, taking bites.
- Each has its own tell in a band nothing else uses. The director spends on them (interior zone) and they go with a car cut loose.
- Walkers go in and deal with them; the gunner goes too while there's nothing at the back.
- Draggers can be beaten now: stamp on the limb as it reaches, twice, and it lets go of the car.

**The line's boards (sight.json, after the 100-night playtest):**
- Speed boards before sharp curves and weak bridges, and low-clearance boards before tunnels, read in the headlamp at 350 m. Lamps down they're unread, and the curve or tunnel mouth is only made out 10 m short.
- Over a board, the cars strain and the cargo lurches, roof riders are thrown, and far over the train derails. A tunnel mouth takes anyone standing on a roof.
- Grease now takes the rail's grip.
- The driver bot slows for boards it has read; walkers get inside for a posted tunnel.

**Cold and the Vigil (spec B.2, C.2):**
- Cold exposure is predicted like movement: 600 s to onset, 1200 s to death, recovered in 20 s near heat, and a quarter as fast inside a car with a door open.
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

**The soak (T42), M2's exit test:**
- The nightly `Soak` workflow runs 8 clients on the rough link (90 ms ±20, 3% loss) for 30 minutes, then a whole frontier:7 night with the enemies.
- The first run locally passed: worst correction 0.32 m, at most 17 corrections per client, every snapshot but ~4%, and nobody died.

Remaining for M2:
- a first run on real Steam between two accounts (needs the SDK library and two machines)
- EOS for itch.io moves to M6 with the itch build

**M4 (VR), foundation done:**
- An OpenXR session with stereo rendering: `--vr` in the app, mirrored to the window.
- The headset's pose on the player's body, in the car's frame.
- Verified headless on Monado's simulated headset: `tools/xr-sim.sh`, then `dt vr check`. CI runs it on every push.
- **Controllers and comfort (T26):**
  - OpenXR actions bound for Touch, Index and the simple controller.
  - Head-relative walking through the same intent path as the keyboard.
  - Snap or smooth turn, a locomotion vignette (`tuning/vr.json`), and the player's hands drawn.
- **Hand interactions (T29):**
  - reach from the hand: levers, handles, switch stands, doors, crates and ladders take the hand that's on them;
  - the shovel is a stroke from the tender's coal face into the firebox, at the keyboard's rate at most;
  - the cab's regulator, brake and reverser move with the hand;
  - ladders climb hand over hand;
  - all of it through the same intent as a keyboard (`player.json` `hand`).
- **The HUD and menus in the headset (T36):**
  - the flat HUD and the front end on a panel ahead, projected into both eyes at a real depth;
  - it follows the head lazily, and there's no crosshair on it;
  - the menus work from the controllers;
  - `dt vr check --hud` / `--menu title` show them.

- **Two to lift, two to turn (T43):**
  - the winch's cranks go round with the hand, at most at the keyboard's pace;
  - the drum stalls when the two are out of rhythm (spec D.2);
  - heavy crates take one at each end, and a headset's end takes both hands;
  - bots carry them in pairs, and come to help a player holding one (T45).

- **The crew see a headset's arms (T47):** the hands go out in snapshots (free for keyboard players), and a two-bone arm reaches from the shoulder to each; `dt screenshot --view roof --crew` shows them.

Remaining for M4:
- the rest of body IK (a spine that follows the headset, stepping legs)
- multiview
- the exit test on a real Quest and SteamVR
