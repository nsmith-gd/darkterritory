# K1's handoff: the friendslop work, queue #450–#467

Saved 14 Oct 2026 as the session neared its token limit (the director: "Save your work now so nothing is lost"). The
plan is docs/design/friendslop.md; the director's calls are GDD App. F.6. **Anyone may carry on from here** (COORDINATION
rule 9), saying so in the item's row and their log.

## Merged
- #467 the audit and the calls (#991, note 733) · #461 the mail catch (#992, note 727)
- #451 horseplay with the shove's mark, #452 the night's prizes, #454 voices, #459's coupling test (#999; notes 717, 718,
  720, 725). Protocol 59.
- From `ccr-f72c055b-195xp3`: #450 the big cab (note 716; with #459's lamp per crewmate aboard), #455 crew
  memory (note 721; the logbook on the cab's front bench), #458 sessions (note 724; protocol 62) (#1016, merged). Then #462 the sense
  table (note 728; protocol 63), #457 bringing them home (note 723; 64), #453 big finds (note 719; 65) and #463 the handcar
  (note 729; 66) (#1026, merged). Then #465 painted masks
  (note 731; 67) and #466 sound cues (note 732; 68) (#1034, merged). Then #464 odd outcomes
  (note 730; 69) and #456 emotes for two (note 722; 70) (#1038, merged). Then #460 a friend on the crane's hook
  (note 726; 71), the last of K1's items (#1046, merged).

## Building or not started (specs in `specs/`, each with `specs/preamble.md`'s rules)
Every item of queue #450–#467 is merged. Two of the director's calls of 15 Oct were built but not finished when K1 hit
its weekly limit (2026-10-15 ~15:45 UTC). Anyone may pick them up: put your id on the queue row first (COORDINATION
rule 1), then:

- **Finished by A1** (15 Oct): #529 in [#1066](https://github.com/nsmith-gd/darkterritory/pull/1066) (protocol 74), #530 in [#1083](https://github.com/nsmith-gd/darkterritory/pull/1083) (note 803 with the pay measured). The two entries below are kept as they were handed off.
- **#529, note 802: a drop off the crane's hook is a gag** (the director: "the player can only lose 10 health at most
  from a drop and then scale that based on height. It's a silly gag not a big grief."). `patches/529-hoist-gag.patch`,
  made on main a7dde418 (`git checkout -b <yours> a7dde418 && git apply docs/handoff/k1/patches/529-hoist-gag.patch`,
  then merge main). What it does: `PlayerFlags` widened to `uint` with `OffTheHook = 65536`, set in `World.LetGo`,
  cleared on footing; `PlayerMotor.Land` with the flag knocks `player.json landing.offTheHook` (maxKnock 10, from, fullAt)
  by the height fallen, never a death; HoistTests rewritten; note 802 written; the GDD and friendslop.md updated.
  `dotnet build` is clean. **Not yet done:** the test run (HoistTests, NetcodeTests, the protocol pins, the whole Sim
  project), and the protocol: the patch takes 72, which main has since given to someone else, so take main's
  `Messages.Version` + 1 at merge and fix the pins (RejoinTests, CrewCapTests, SessionsTests, MaskPaintTests,
  EmotesForTwoTests); `tools/coord/notes.py index` after the merge.
- **#530, note 803: the mail cut for now** (the director: "Im soft on the mail. I dont like it so far. Lets cut for
  now."). `patches/530-mail-cut.patch` applies on today's main (0845f54). What it does: one switch, `sight.json`
  `mailCranes` false, so no mail cranes or boards are placed (`Lineside`), no HUD line, nothing to catch; the mail's
  tests turn the switch on in their own setup; notes 673 and 727, the GDD, ROADMAP and friendslop.md updated.
  `dotnet build` is clean (K1 fixed one test's `Box?`). **Not yet done:** `docs/notes/803.md` (format of note 709;
  then `tools/coord/notes.py index`), the test run (LinesideTests, BoardSightTests, MailHudTests, MailAndCometSoundTests,
  BotGunnerTests, CrewOfTwoTests, NetcodeTests, SpecTableTests, the economy and campaign tests, then both projects
  whole), the screenshots (`trackside`, `ahead`: no mail crane), and what the cut does to the economy (the mail paid
  `sight.json mailScrip` 20-60 a bag at the terminus): measure it, report it to the director, don't retune to
  compensate.
- **The prompts for AU1 and E1** the director asked for (the last sounds; the two-person clips): `prompts-au1-e1.md`.
- **The GDD artifact's republish** (the director gave it to K1 from A1, 15 Oct): claude.ai/artifact/PrfaZjyZb1rWcWWuiPm1Dx
  holds gdd.md raw in its `gdd-md` block; read the whole page, swap the block for main's gdd.md (after #529 and #530
  land, so it goes once), and publish. Not started.

## Open threads
- The director questioned the mail (14 Oct playtest line 18): #461's speed post stands until they decide.
- A1's #474 (second 14 Oct playtest line 4, "signal to the driver that everybody is on the train") meets #450's chart
  table, which has a lamp per crewmate aboard (`CrewCar.Aboard`): told on #1004.
- Protocol: #458 is 62 (A1's #1017 and #1024 took 60, 61); B3's #1007 re-takes at its merge (told on #1007).
- The art checklist's `cab` line is C1's (#492) and `loco` E1's (#298): asked on #1010, not written. `crew-variants`
  marked K1 · #465.
- AU1's sounds: horseplay heard in AU1's #1012; the rest (tube, comet, torn bag, kettle, cuckoo, shutter, page) asked on #1012.
- U1's #967 (upgrades) shares ground: asked on #967 (the gramophone and cat on the cab's back wall, the emotes' slots,
  the photographer, the speaking tubes).
