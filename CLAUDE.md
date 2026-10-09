# Dark Territory — agent guide

Co-op first-person survival horror (2–8+ players, PC + VR) on **Ballast**, a small custom C#/.NET 10 engine built for this game.
**Before starting any work, read `docs/COORDINATION.md`** (who owns what, claiming an item, reserved note numbers; several agents work on this repo at once).
**UI/UX notes and checklist assignments** (the director, 7 Oct; the steps are COORDINATION.md's "UI/UX notes and assignments"): the director's UI/UX notes live in the Art Checklist's UI/UX notes panel (claude.ai/artifact/7MnAAHwaVRtNosBH7hRLNu, its store's `uiux_notes`) and go to every UI/UX chat listed in `uiux_chats`.
- UI/UX agents list their own chat in `uiux_chats`, then take notes after reading COORDINATION.md and the logs: write `assignee` (your agent id), `assignedAt` and `queue` to the note, pinned to the version you read so no two chats take one; `status: "done"` with its `pr` when it merges.
- Every agent marks the checklist lines it works on: `assignee`, `queue` and `assignedAt` on the line's `items` row (nothing else on C1's row). Someone else's `assignee` means it's theirs: ask on their PR.
**World building notes** (the director, 8 Oct): the director reviews the towns and the world, and leaves notes, on the World Building board (claude.ai/artifact/JuwKtJb7gdT41PEtFaowdp, its store's `notes`; each piece's shots and status in `sections`). Agents whose work touches the world read the open notes and take them as COORDINATION.md's "World building notes" says. Its Cultures tab (store collection `lore`) is the director's writing on how the towns live, for the town agents to turn into content; its `canon` collection lists what's in the game.

Read before large changes: `docs/ARCHITECTURE.md` (decisions and why), `docs/ROADMAP.md` (what's next), `docs/design/gdd.md` and `docs/design/systems-spec.md` (the design, source of truth for numbers), `docs/design/level-design.md` (level-design principles from example sketches, and the rules for generating sites).

## Commands
```bash
dotnet build Ballast.slnx                          # warnings are errors
dotnet test --solution Ballast.slnx                # all tests (Microsoft.Testing.Platform runner)
dotnet test --project tests/DarkTerritory.Sim.Tests  # one project
dotnet run --project src/DarkTerritory.Cli -- train table   # `dt`: headless inspection tool, JSON out
tools/package.sh [--demo] [--dev] [win-x64] [linux-x64]    # builds for players: self-contained, zipped, in out/dist/ (--demo: the demo edition beside it; --dev: the director's test build, the developer tools in)
dotnet run --project src/DarkTerritory.Cli -- build check out/dist/DarkTerritory-linux-x64   # none of the developer tools in a built game (note 514; --dev: all of them)
dotnet run --project src/DarkTerritory.Cli -- replay night.dtrec [--to 12:30] [--shot out/shots/r.png --view eye:2|chase]   # a recorded night (a dev build records every night it hosts) played again tick for tick: where it parts, the crew and train there, a shot (note 515); `replay record --bots 4 --seconds 120` makes one headless
dotnet run --project src/DarkTerritory.Cli -- feedback pull && dotnet run --project src/DarkTerritory.Cli -- feedback show <id>   # the director's notes from inside the game (F8 types, F9 says, in a dev build), from the repo's feedback branch; show replays the note's moment and draws it through their eyes (note 516)
dotnet run --project src/DarkTerritory.Cli -- trends measure [--seeds 1-8] [--seconds 2700] [--perf]   # this commit's numbers as one trends line: 4-bot full nights' km, deaths, cars, pacing, net (and dt perf's counts); CI appends main's to trends/main.jsonl on the `trends` branch; `trends show <file> [--metric km]` the falls past tools/trends/metrics.json's bands; tools/trends/bisect.sh --metric km --good A --bad B finds the commit (note 523)
python3 tools/tonight/integrate.py --offline-test   # tonight's build (.github/workflows/tonight.yml, every night at 7:30 pm Montreal time): main + every open green PR that merges cleanly, a dev build zipped with TEST-PLAN.md from their "how to check" lines, the merge on `tonight` (note 523)
dotnet run --project src/DarkTerritory.Cli -- mcp   # live control for agents (note 524): an MCP server on stdio (night_start, night_step [until gaunt|enemy|incident], night_state, night_insist, replay_open, shot, dt); a view `subject:gaunt|crew:2|car:3|engine|enemy:12` frames that, in `shot` and `dt replay --shot` alike; only the sessions that need it add it (`claude mcp add dt -- dotnet run --project src/DarkTerritory.Cli -- mcp`)
dotnet run --project src/DarkTerritory.Cli -- --edition demo harness --route frontier:7 --bots 8 --enemies   # any dt command (or the app) on the demo edition (editions/demo)
tools/xr-sim.sh && XDG_RUNTIME_DIR=/tmp/xr dotnet run --project src/DarkTerritory.Cli -- vr check   # VR end to end on a simulated headset
python3 tools/audio/fetch_music.py --dry-run          # E.6's CC0 music intake: the candidate list (--offline-test the cut path); the real run is .github/workflows/music-intake.yml (Commons is blocked here)
dotnet run --project src/DarkTerritory.Cli -- audio render --listener all   # spec A.3 tell audit; one listener → WAV + spectrogram PNG
dotnet run --project src/DarkTerritory.Cli -- screenshot --view roof   # 1280x720 PNG to out/shots/; then Read it to look
tools/review/gallery.sh "--no-build" && dotnet run --project src/DarkTerritory.Cli -- review diff <main's shots> out/shots   # the whole gallery in one process (note 517; `screenshot --batch tools/review/gallery.txt`), and what a change did to it: strips + summary in out/review (CI does this on every PR: the review-packet artifact)
dotnet run --project src/DarkTerritory.Cli -- art show engine          # a kit piece on a turntable; `art check` = every piece vs its triangle budget
dotnet run --project src/DarkTerritory.Cli -- art clip car_hugger feed   # a creature's clip as a lit contact sheet (--frames n --at x,y,z --dist --yaw)
dotnet run --project src/DarkTerritory.Cli -- playthrough --route frontier:7 --minutes 20 [--bots 4] [--insist whistler,choir] [--loot]   # a real night with enemies (solo, or a bot crew working the stops; --loot: the stops' finds, which bots search the villages for, note 326), every encounter photographed as it happens -> out/playthrough (note 200)
dotnet run --project src/DarkTerritory.Cli -- afoot --route frontier:7 [--seconds 300]   # a crewmate walks a stop's village, the night on: the signs they're shown, spawns and encounters, director.afoot on vs off (note 327)
dotnet run --project src/DarkTerritory.Cli -- film [--crew 8] [--fps 24] [--speed 20] [--route frontier:7] [--cars 6]   # the whole derailment (first person, replay, the film's cut, the cause card) as the app plays it: frames, the mixer's WAV, an MP4 and a contact sheet -> out/film (ffmpeg: `pip install imageio-ffmpeg`; note 251)
dotnet run --project src/DarkTerritory.Cli -- art reel [--only gaunt,sheep] [--clips a,b]   # every clip of every model, framed on its own movement: strips + reel.json in out/reel/ (the Look Review's animations)
dotnet run --project src/DarkTerritory.Cli -- art clearance [--only crew|ribbit|...]   # every clip checked for limbs through the body, coat, head and other limbs; a creature by capsules fitted to its own mesh (CreatureArtTests pins the crew and the demo's creatures)
python3 tools/art/textures.py                                         # rebuild content/art/textures (CC0 sources: tools/art/fetch_sources.sh)
tools/art/store/screens.sh                                            # store screenshots, capsules and the icon -> out/store (icon also content/art/ui)
tools/blender/build.sh                                                # rebuild the procedural creatures in content/art/models (needs blender)
python3 tools/models/fetch.py && tools/models/build.sh                # sourced CC0/CC-BY models, and the modelled-and-baked ones (props, the crew) -> content/art/models (needs blender)
dotnet run --project src/DarkTerritory.Cli -- perf [--only pc|vr] [--views roof,cab]   # frame cost vs tuning/perf.json (90 fps PC, 72 fps VR): CPU phases, GPU passes, counts
dotnet run --project src/DarkTerritory.Cli -- harness --bots 8 --seconds 300 [--express 21]   # host + bots over lossy loopback; netcode report (--express: a driver that runs hot and takes no stops, note 376)
dotnet run --project src/DarkTerritory.Cli -- mods pack tools/mods/example      # mods are Thunderstore packages: check one and zip it; `dt mods` lists what's installed
dotnet run --project src/DarkTerritory.Cli -- report [--problem]   # a crash report (or a player's own) as the game writes it, its JSON twin and the mail to the studio -> out/reports (note 452); the app's --crash-test crashes it on purpose
dotnet run --project src/DarkTerritory.Cli -- credits --write   # after a model, sound pack or library comes in: the credits from the content's provenance, THIRD-PARTY-NOTICES.txt rewritten (note 390)
dotnet run --project src/DarkTerritory.Cli -- linegen generate --route frontier:7 --cars 6   # a night's line plan + map and profile PNGs; `linegen sweep` for pass rates; `linegen water` its lakes and shores
dotnet run --project src/DarkTerritory.Cli -- holes --route deadLines:3 [--every 300] [--untextured]   # see-through land: down a night's line, the sky painted magenta where it shows through (untextured surfaces cyan), frames with any -> out/holes (note 433)
XDG_RUNTIME_DIR=/tmp xvfb-run -a dotnet run --project src/DarkTerritory.App -- --route frontier:7 --throttle 1 --quit-after 30 --capture out/shots/app.png   # real window path, headless (--route skips the front end)
```
**Look at your visual changes.** After touching rendering or scene code, render the relevant `dt screenshot` views and read the PNGs before calling it done. Views: trackside, roof, cab, fireman, chase, ahead, gap, hatch (and, off the perf list, crew, crewside, inside, door, gapside, pack, choir, gaunt, gauntface, grumbler, firebox, follower, flies, passenger, soot, sootside, switchman, cannon, cannonside, fire, coaling, stores, locker, kit, cut, pen, trail, nest, mount, board, carry, swallow, packside, cutoff, rupture, flanges, poses, run, hotbox, moose, moosecharge, moosepin, gannet, gannetfold, gannetstuck, gannetpin, lane, laneside, patrol, glass, gauntcarry, mourners, beetle, towerjaw, towerjawfall, brakeman, brakemancorner, knotter, knotterslip, hotboxbug, hotboxout).
Cloud sessions: `.claude/hooks/session-start.sh` installs the .NET 10 SDK from Ubuntu apt (the Microsoft download host is blocked by the proxy) and Mesa lavapipe (software Vulkan) for rendering without a GPU.

## Layout
- `src/Ballast.*` — engine modules (`Ballast.Render`: Vulkan 1.3, GLSL shaders embedded as text and compiled at startup).
- `src/DarkTerritory.Sim` — shared host/client simulation. `src/DarkTerritory.Game` — presentation (greybox scene, views; `Art/` the art pass's kits: train, world, structures, effects). `src/DarkTerritory.Cli` — `dt`.
- `src/Ballast.Dev`, `src/DarkTerritory.Dev` — developer tools (note 514): in `dt` and the director's dev builds, never in a player's. The app reaches them only under `#if DEVTOOLS`.
- `content/` — all game data (JSON with comments, hot-reloaded). This is also the base mod. `content/art/` holds cooked textures and models (generated by `tools/art`, `tools/blender`; provenance in `index.json`).
- `tests/` — xunit v3. `SpecTableTests` pin the sim to the systems spec.

## Rules
- **Design numbers live in `content/tuning/*.json`, never in code.** Each tuning file cites its spec section. If you change a number that a spec test pins, update the test *and* `docs/design/systems-spec.md` in the same change, and say so.
- **`DarkTerritory.Sim` has no platform deps** (no rendering, audio devices, Steam, files outside `content/`). The host and every client run it identically; the headless harness depends on this.
- **Clients send intent, never state.** Anything a player does goes through input intent → host sim. Bots use the same path.
- **Anything that changes player or train state must be deterministic** (no wall clock, no unseeded RNG, no dictionary-order dependence): clients re-simulate it for prediction, and `NetcodeTests.OnAPerfectLinkPredictionMatchesTheHostExactly` fails on any divergence.
- **Moving frames:** a player standing on a car lives in that car's frame. Never convert between frames *before* integrating a tick; the car has already moved this tick (see `MovingFrameRegressionTests`).
- **Everything authored is text**; no binary scene/prefab formats. Assets are generated by scripts in `tools/` and cooked by the CLI.
- **Developer-only features never ship** (note 514): they go in `DarkTerritory.Dev`/`Ballast.Dev`, and the app reaches them only under `#if DEVTOOLS` (no `#else`). `PlayerBuildTests` and `dt build check` on every package hold it.
- **Make it verifiable headless.** New systems get a `dt` subcommand or a test that exercises them without a window. Visual work gets a screenshot path; audio work gets `dt audio render` (look at the spectrogram, and keep `AudioTests` green: a tell that can be drowned out is a bug).
- Units: metres, seconds; train sim uses tonnes and kN. +Y up, −Z forward. `double` for along-line distance/world anchors.
- When the spec is ambiguous, pick the reading that keeps the spec's own tables true, make it a tuning flag, and note it in `docs/ARCHITECTURE.md` §8.
- Match surrounding style: file-scoped namespaces, records for data, comments explain *why* and cite GDD/spec sections.
