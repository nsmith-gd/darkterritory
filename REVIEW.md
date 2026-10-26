# Reviewing a pull request

The rubric a reviewer checks a Dark Territory PR against: a person, Claude Code Review (which reads this file beside
CLAUDE.md), or the cross-model review workflow (`.github/workflows/cross-review.yml`, note 1044). Every rule is one line:
what it says, then **look for** what breaks it in a diff. CLAUDE.md's Rules are the source; this file says how to see them
broken. Cite the rule's id (D1, N2, …) in a finding.

## First, the PR's risk tier

Tier by the riskiest path the diff touches. Each tier needs everything the tiers below it need.

| Tier | Paths | Before it merges |
|---|---|---|
| **High** | `src/DarkTerritory.Sim/**` (and `Net/`), `src/Ballast.Net/**`, `src/Ballast.Online/**`, `src/Ballast.Voice/**`; anything that changes player or train state; `Protocol.Version`; save and record formats (`PlayerProfile`, settings, `NightRecorder`, `TelemetrySchema`); `.github/workflows/**`, `tools/package.sh`, `tools/tonight/**`; player-build gating (`#if DEVTOOLS`, `*.csproj` conditions, `PlayerBuildTests`, `dt build check`) | A test that fails without the change. Netcode and replay tests green on the merged head (`NetcodeTests`, `RecordReplayTests`, the crossplay job). For the wire, the protocol rule (N3). For a workflow, `actionlint` clean and its permissions read. A named human or owning session looked at it |
| **Medium** | `src/DarkTerritory.Game/**`, `src/Ballast.Render/**`, `src/Ballast.Audio/**`, `src/DarkTerritory.App/**`, `content/**` (tuning, art, audio, towns) | A screenshot (`dt screenshot`, or the review packet's strips) for anything seen; `dt audio render` and `AudioTests` for anything heard; for a tuning number, its spec section and test (T1) |
| **Low** | `docs/**`, `tools/**` (but the ones above), `src/DarkTerritory.Cli/**`, `src/*.Dev/**`, tests only | Build clean, the coord check green for docs (`python3 tools/coord/check.py`) |

## Determinism (D)

Clients re-simulate the host's state for prediction; `NetcodeTests.OnAPerfectLinkPredictionMatchesTheHostExactly` and
the replay fail on any divergence, sometimes only on a long night.

- **D1 No wall clock** in anything that changes player or train state. Look for `DateTime.Now/UtcNow`, `Stopwatch`,
  `Environment.TickCount`, frame `dt` from the renderer reaching Sim code.
- **D2 No unseeded randomness.** Look for `new Random()`, `Random.Shared`, `Guid.NewGuid()` in Sim; a seeded stream must
  come from the night's seed by a named stream (as `folk.town`, `folk.house`), not a shared one whose draw order moves.
- **D3 No container-order dependence.** Look for iterating a `Dictionary`, `HashSet` or LINQ `GroupBy`/`Distinct` result
  where order changes the outcome (spawns, targets, first hit). Iterate by id, ordinal or a sorted list.
- **D4 Same on Windows and Linux.** The crossplay job compares nights built on both. Look for culture-sensitive parsing or
  formatting (`ToString()`/`Parse` without `InvariantCulture`), path separators in content keys, `float` math in the
  line or land generator where `double` is the rule (along-line distance and world anchors are `double`).
- **D5 Moving frames.** A player on a car lives in that car's frame. Look for a conversion between car and world frames
  *before* a tick integrates (the car has already moved this tick; `MovingFrameRegressionTests`).

## Networking (N)

- **N1 Clients send intent, never state.** Anything a player does goes input intent → host sim; bots use the same path.
  Look for a client writing positions, health, cargo or car state into a message, or a bot calling Sim setters directly.
- **N2 The host decides.** Look for presentation (`DarkTerritory.Game`) deciding an outcome a client could disagree on.
- **N3 Wire change, version bump.** Any change to a message's fields, order or meaning takes `Protocol.Version`
  (`src/DarkTerritory.Sim/Net/Messages.cs`) to main's + 1 *at merge time*, with the tests that pin it (COORDINATION rule
  10). Look for a changed `Write`/`Read` pair, a new message kind or enum value on the wire with the version unchanged, or
  a version that's stale against main.

## Numbers and the spec (T)

- **T1 Design numbers live in `content/tuning/*.json`,** never in code, each file citing its spec section. Look for a
  new literal speed, damage, distance, chance or time in `src/` that a designer would tune.
- **T2 A pinned number moves with its pins.** A number `SpecTableTests` (or another spec test) pins changes in the same
  PR as the test and `docs/design/systems-spec.md`, and the PR says so.
- **T3 Creature numbers go through the Creature Tweaker** (COORDINATION "The Creature Tweaker"): look for a hand edit to
  `enemies.json`'s `director.*` values with no Tweaker `want` behind it.
- **T4 New behaviour behind a flag** when it waits on the director's verdict (`<thing>.on`, off by default). Look for a
  new creature or system on by default that the PR or its note says the director hasn't approved.
- **T5 Spec ambiguity is written down:** the reading that keeps the spec's tables true, a tuning flag, and a note.

## Platform and build (P)

- **P1 `DarkTerritory.Sim` has no platform deps:** no rendering, audio devices, Steam, sockets of its own, or files outside
  `content/`. Look for a new `using` of `Ballast.Render`/`Audio`/`Platform`, `System.IO.File` outside the content loader,
  or a package reference in its `.csproj`.
- **P2 Developer tools never ship.** They live in `DarkTerritory.Dev` / `Ballast.Dev`; the app reaches them only inside
  `#if DEVTOOLS` with **no `#else`**. Look for a Dev reference outside the guard, an `#else` branch, a new dev feature in
  `Game` or `App`, or a `.csproj` change to the DEVTOOLS condition (`PlayerBuildTests`, `dt build check`).
- **P3 Everything authored is text.** Look for a binary scene, prefab or hand-made asset without the `tools/` script that
  makes it and its provenance (an `index.json` beside it, as in `content/art/textures/`; credits for a third-party one: `dt credits --write`).
- **P4 Warnings are errors and `dotnet format` is clean.** Look for `#pragma warning disable` or `NoWarn` added to get past one.

## Verifiable headless (V)

- **V1 A new system has a test or a `dt` path** that exercises it without a window. Look for a feature with neither.
- **V2 Visual work was looked at.** Rendering or scene changes come with `dt screenshot` views (a new view for a new
  thing) or the review packet, and the PR says what was seen.
- **V3 A tell is never drowned out** (spec A.3). A new or louder sound keeps `AudioTests` and `dt audio render --listener
  all` green; look for a mix or bus change with no render.
- **V4 Tests test.** Look for a test skipped, deleted, loosened (wider tolerance, fewer seeds) or quarantined to get green;
  that is a finding whatever the reason given, unless the PR names the director's or the owner's agreement.
- **V5 CI memory.** The Linux runner has 16 GB and runs two test projects at once (`--max-parallel-test-modules 2`).
  Look for a test that hosts a renderer or a full night (lavapipe, a dressed `Look`, a GPU context) without disposing it,
  a class-static `Look` or scene, or a new GPU-heavy test class beside `TellAuditTests` (notes 804, 1043).
- **V6 Performance budgets hold** (`content/tuning/perf.json`; 90 fps PC, 72 fps VR): a change to what's drawn in a town
  or down the line names its `dt perf` numbers.

## Workflows (W)

- **W1 Least privilege.** `permissions:` set at the top, no `write-all`; secrets never reach a fork's code
  (`pull_request_target` with a checkout of the PR head is a blocker).
- **W2 Don't spend the queue.** CI runs once per change: docs-only paths stay ignored, a PR's newer push cancels its older
  run; look for a new trigger or matrix leg that doubles runner time.

## Coordination (C)

- **C1 Claimed:** the PR's queue row and note number are its own (`tools/coord/check.py` green), the row names its
  owner and branch.
- **C2 Done means merged:** the PR sets its row to done with the PR link and note, or a follow-up docs PR does.
- **C3 Its note says why:** `docs/notes/<n>.md` with what, why, how to check, and a current Handoff block.
- **C4 Logged:** a line in `docs/log/<agent id>.md` with its UTC time.
- **C5 Other owners' work:** an edit to another session's lines (towns, art, audio, the HUD, a creature) is asked about on
  their PR first; audio belongs to the audio chat (AU1).
- **C6 A "how to check" line** in the PR body; tonight's build turns it into the director's test plan.

## Writing the review

- One finding per broken rule, with the rule's id, the file and line, and the concrete path to the failure (which input,
  which tick, which machine disagrees). A rule the diff can't break isn't a finding.
- Blocking: any D, N, P2, V4 or W1 finding, and a missing tier requirement. Everything else is a suggestion.
- Say what you didn't check (a generated file, a binary, a diff too large to read) rather than passing it.
