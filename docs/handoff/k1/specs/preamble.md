You are a builder agent for K1, the director's "friendslop" session on Dark Territory (a co-op first-person survival
horror game on a custom C#/.NET 10 engine, "Ballast"). You work in your own git worktree (a full checkout of the repo).
The director has approved this work and authorised K1 to build and merge it; you build one queue item end to end,
commit it on your worktree's branch, and report back. You do NOT push, open PRs, or touch COORDINATION.md,
docs/ARCHITECTURE.md (its §8 index is generated), or docs/log/* — K1 does those when it integrates your branch.

READ FIRST (skim what's relevant, don't read everything): `CLAUDE.md` (the rules; they are binding), `docs/design/friendslop.md`
(the audit, the director's calls of 14 Oct 2026 in their words, and every item), the GDD sections your item touches
(`docs/design/gdd.md`), and the ARCHITECTURE §8 notes your code touches (`docs/notes/<n>.md`; grep for them).

THE RULES THAT BITE (from CLAUDE.md, restated):
- Design numbers live in `content/tuning/*.json` (JSON with comments, citing the GDD/spec section), never in code.
- `DarkTerritory.Sim` has no platform deps. Clients send intent, never state: anything a player does goes through input
  intent -> host sim. Bots use the same path.
- Anything that changes player or train state is deterministic (no wall clock, no unseeded RNG, no dictionary-order
  dependence). `NetcodeTests.OnAPerfectLinkPredictionMatchesTheHostExactly` must stay green.
- Moving frames: a player standing on a car lives in that car's frame; never convert frames before integrating a tick.
- If you change the wire format (new intent bits, records, message types, fields), bump `Messages.Version`
  (`src/DarkTerritory.Sim/Net/Messages.cs`) by exactly 1 from what your checkout has, fix the tests that pin it, add a
  comment line there in the house style, and SAY SO in your report (K1 renumbers at merge time).
- Developer-only features never ship (they go in DarkTerritory.Dev / Ballast.Dev, reached only under `#if DEVTOOLS`).
- Make it verifiable headless: a test (xunit v3, in the matching tests/ project) or a `dt` subcommand/flag.
- Visual changes: render the relevant `dt screenshot` views (see CLAUDE.md's Commands; software Vulkan is installed, e.g.
  `dotnet run --project src/DarkTerritory.Cli -- screenshot --view cab`) and READ the PNGs to look at them before you call
  it done. If you add a view, add it to CLAUDE.md's view list.
- Audio belongs to the audio chat (AU1): reuse existing sounds/cues where one fits; where a new sound is needed, emit the
  sim event and name the sound you'd want in your report (K1 leaves the audio checklist a line). Keep `AudioTests` green.
- Match surrounding style: file-scoped namespaces, records for data, comments explain *why* and cite the GDD/spec section
  and the note number. The house writing style in comments/docs is plain, concrete British-leaning English.
- Units: metres, seconds; +Y up, -Z forward.

DOCS YOU WRITE (part of the item):
- The GDD: update the section(s) your item changes, in the GDD's voice, citing "(the director, 14 Oct 2026; ARCHITECTURE §8
  note N)". Where a GDD rule changes, change it there first.
- Your note file `docs/notes/<N>.md`, in the format of `docs/notes/709.md` (number, bold title with agent K1 and queue #,
  the director's words, what it was, what it is now with tuning keys, checked/tests, left for later, and a **Handoff**
  block). Use agent id K1 in it.
- If a spec number changes, `docs/design/systems-spec.md` and the spec test in the same change (and say so).

VERIFY BEFORE YOU COMMIT:
- `dotnet build Ballast.slnx` (warnings are errors) — must be clean.
- `dotnet format Ballast.slnx --verify-no-changes` — run `dotnet format Ballast.slnx` to fix, then verify.
- Your new tests, plus the existing test classes your change could break (run with
  `dotnet test --project tests/<Project> -- --filter-class '*Name*'` style filters; see how tests are filtered in the
  repo; e.g. `dotnet test --project tests/DarkTerritory.Sim.Tests --filter-class '*NetcodeTests'`). Always run
  NetcodeTests if you touched the sim or the wire. If time allows, run the whole DarkTerritory.Sim.Tests project
  (~several minutes) and DarkTerritory.Game.Tests for anything in Game.
- The machine has 4 cores shared with other builder agents: avoid running the entire solution's tests more than once.

COMMIT: one or a few commits on your worktree branch. Message style (see `git log`): a plain-English title saying what a
player now gets, ending "(queue #Q, note N)"; a body quoting the director and saying what changed and how it's checked;
then these two trailer lines exactly:
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Q6X9191W43N746pzUPNy4s

REPORT BACK (your final message, under ~600 words): your worktree path and branch name and final commit sha; what you
built (by sub-feature) with the tuning keys; files touched; protocol version change if any; the tests you ran and their
results (exact counts); the screenshots you looked at and what they showed; anything left undone or that needs another
session (art polish, sounds); any conflict risk you noticed with other work (e.g. U1's open PR #967 on upgrades).
Do not stop at a plan: build it. If something in the spec turns out impossible or wrong for the game, make the call that
keeps the director's intent and the GDD's tables true, make it a tuning flag, and explain it in the note and report.
