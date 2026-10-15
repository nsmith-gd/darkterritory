# A1's handoff (account nathaniel2)

Written at the Token Budget Manager's PREPARE HANDOFF (COORDINATION rule 11), 2026-10-15 17:44 UTC: nathaniel2's weekly limit at 60%,
on pace to run out about 16 Oct 08:00 UTC (resets Wed 21 Oct 20:00 UTC). A1 is still working these; this file is kept
current at each step, and the rows say `open for pick-up` only at EXECUTE HANDOFF. One README for several items
(*Accounts and usage limits*): the notes 802, 803 and 770 live on the branches until their PRs land, so landing them here
first would make the PRs conflict.

## Queue #529, note 802: a drop off the crane's hook is a gag

- **From:** nathaniel2, A1, 2026-10-15 17:44 UTC, prepare handoff (weekly 60%). K1 built it (K1.16) before its own limit; A1 carries it on.
- **Branch and last commit:** `a1-hoist-gag`, 6b63c46d (pushed). PR [#1066](https://github.com/nsmith-gd/darkterritory/pull/1066).
- **Goal:** the director, 15 Oct: "the player can only lose 10 health at most from a drop and then scale that based on height. It's a silly gag not a big grief."
- **Done:** K1's patch (`docs/handoff/k1/patches/529-hoist-gag.patch` on a7dde418) applied and main merged in; protocol 73 (main gave 72 to the Mouse Moose, note 790) with the five pins (`RejoinTests`, `CrewCapTests`, `SessionsTests`, `MaskPaintTests`, `EmotesForTwoTests`) and note 802; the listed tests 139/139, the Sim project 2123/2123, `dotnet format` clean, `dt screenshot --view hoist` looked at.
- **Next, in order:** (1) CI: Ubuntu running; Windows failed once on `LobbyBrowserTests.APublicHostOnTheNetworkIsListedWithAMeasuredPingAndAPrivateOneIsnt` (a loopback ping of 200.44 ms against 0–200: wall-clock timing, not this PR's; commented on the PR) — re-run the failed job once when the workflow's done. (2) Green → squash-merge, row #529 done with the PR. (3) If main bumps the protocol first, take main's + 1 again (rule 10) and fix the five pins.
- **Decisions:** 73 at merge (rule 10). `PlayerFlags` as a uint is safe on the wire: `WorldRecords` writes `(long)s.Flags`, `WriteDelta` a signed varint; `.dtrec` recordings are those datagrams; no hash reads flags.
- **Gotchas:** a union merge of COORDINATION.md doubles changed rows; take main's file and re-apply your row.
- **Files:** `PlayerMotor.cs` (the flag, the landing), `World.Hoist.cs`, `Rescue.cs`, `PlayerTuning.cs`, `player.json`, `facilities.json`, `Messages.cs`, `HoistTests` and the five pins, note 802, note 726, the GDD, friendslop.md.
- **Verify:** `dotnet test --project tests/DarkTerritory.Sim.Tests -- --filter-class DarkTerritory.Sim.Tests.HoistTests --filter-class DarkTerritory.Sim.Tests.NetcodeTests`; `dotnet run --project src/DarkTerritory.Cli -- screenshot --view hoist`.
- **Claimed by:** —

## Queue #530, note 803: the mail cut for now

- **From:** nathaniel2, A1, 2026-10-15 17:44 UTC, prepare handoff (weekly 60%). K1 built it (K1.17); A1 carries it on.
- **Branch and last commit:** `a1-mail-cut`, 231c8a97 (pushed). No PR yet.
- **Goal:** the director, 15 Oct: "Im soft on the mail. I dont like it so far. Lets cut for now." One switch, `sight.json` `mailCranes` false; the code stays.
- **Done:** K1's patch applied; note 803 written (format of 709) with the pay measured: 12 four-bot nights on main with the mail on (`dt harness --route <tier>:<seed> --bots 4 --enemies --as-app --seconds 2700`): the mail was 8.3% of a Local night's freight, 10.1% Frontier, 1.9% Dead lines, 0.7% Deep territory (about 100–200 scrip a delivered night on every tier); `dt campaign sim` reaches 20 cars in about 60–62 runs without it, against 56 (runningCosts 0.07 → 59, 0.14 → 63). Not retuned. Linegen frontier:7: no drop. Screenshots ahead and trackside: the MAIL board and crane gone. Targeted tests pass after two fixes the cut needed:
  - `BoardSightTests.NoBoardStandsInFrontOfAnotherWhileItsRead`: frontier:7's Maddox Bridge board, no longer hung with the mail board, took a spot with two boards in front of it; `LinesideProps.Place`'s fallback now takes the best read of the spots with none in front first (B4's area, #299 `pt13-signs`: say so on the PR).
  - `DropAndPacingTests.ANightOutOnTheLineIsNeverQuietForLongerThanTheLongestQuietSpell`: the mail's boards and catches were pacing beats; cut, local:3's longest quiet is 175 s against note 266's 90 s. The test measures with the mail on now (as the mail's other tests do) and says so; reported to the director in note 803, not retuned.
- **Next, in order:** (1) the whole Sim and Game projects (running locally on the merged branch). (2) Row #530 in review, A1 log entry, open the PR (body: the pay table, the two fixes, the screenshots, B4 overlap). (3) Green → squash-merge, row done.
- **Decisions:** the cut is a switch, not a deletion (the director's "for now"). The economy isn't made up for: the director's call.
- **Gotchas:** the mail's own tests turn the switch on (`S with { MailCranes = true }`); `SightTuning`'s default must mirror sight.json (`LinesideTests` holds it).
- **Files:** `Lineside.cs`, `LinesideProps.Boards.cs`, `Staging.cs`, the CLI's screenshot, `sight.json`, `hud.json`, the mail's tests, `LinesideTests`, notes 673, 727, 803, GDD §19 and App. F.6, ROADMAP, friendslop.md.
- **Verify:** `dotnet test --project tests/DarkTerritory.Sim.Tests -- --filter-class DarkTerritory.Sim.Tests.BoardSightTests --filter-class DarkTerritory.Sim.Tests.DropAndPacingTests`; `dotnet run --project src/DarkTerritory.Cli -- linegen generate --route frontier:7 --cars 6`; `dotnet run --project src/DarkTerritory.Cli -- screenshot --view ahead`.
- **Claimed by:** —

## Queue #500, note 770: alone, the child calls too

- **From:** nathaniel2, A1, 2026-10-15 17:44 UTC, prepare handoff (weekly 60%).
- **Branch and last commit:** `a1-child-found`, c1b2da5d (pushed, WIP). No PR yet.
- **Goal:** the 14 Oct playtest's line 16 ("I also haven't seen like a rescue child ever"). The child comes only from the Soot Children's call, which needed a crew of two; asked, the director chose (15 Oct): solo nights hear it too.
- **Done:** GDD B.6's table and App. A.6 first; `enemies.json` `sootChildren.minCrew` 2 → 1 and `SootChildrenTuning`'s default; `CargoTests.AloneAtAStopAChildStillCalls` (passes; the old rule's weight is null in it). Note 770 with the measurement (bots: a call on 2 nights of 6).
- **Next, in order:** (1) merge main; (2) the Sim project whole (spawn tests could see a solo Soot Child now); (3) row #500 in review, the playtest line, the log; PR; green → merge.
- **Decisions:** only the crew gate moved; the weight, the 50/50 and the first-call rule as they were. The Creature Tweaker has no crew-gate column.
- **Gotchas:** a solo harness bot seldom stands afoot, so `dt harness --bots 1` shows no call; `dt afoot` (a crewmate walking a village, crew 1) is the better headless look.
- **Files:** `enemies.json`, `EnemyTuning.cs`, `Spawns.cs` (comment), `CargoTests`, the GDD, note 770, the playtest table.
- **Verify:** `dotnet test --project tests/DarkTerritory.Sim.Tests -- --filter-method "*AloneAtAStopAChildStillCalls"`.
- **Claimed by:** —

## The GDD artifact's republish (given to A1 with #529 and #530)

- **Goal:** claude.ai/artifact/PrfaZjyZb1rWcWWuiPm1Dx holds docs/design/gdd.md raw in its `gdd-md` block: after #529 and #530 land, read the whole page (the Artifact tool's read), swap the block for main's gdd.md, check it parses, publish to the same URL, and note the version and main's commit in COORDINATION's line about it.
- **Done:** nothing yet (it waits on the two merges, so it goes once).
- **Claimed by:** —
