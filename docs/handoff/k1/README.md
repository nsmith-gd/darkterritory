# K1's handoff: the friendslop work, queue #450–#467

Saved 14 Oct 2026 as the session neared its token limit (the director: "Save your work now so nothing is lost"). The
plan is docs/design/friendslop.md; the director's calls are GDD App. F.6. **Anyone may carry on from here** (COORDINATION
rule 9), saying so in the item's row and their log.

## Merged
- #467 the audit and the calls (#991, note 733) · #461 the mail catch (#992, note 727)
- #451 horseplay with the shove's mark, #452 the night's prizes, #454 voices, #459's coupling test (#999; notes 717, 718,
  720, 725). Protocol 59.
- In review from `ccr-f72c055b-195xp3`: #450 the big cab (note 716; with #459's lamp per crewmate aboard), #455 crew
  memory (note 721; the logbook on the cab's front bench), #458 sessions (note 724; protocol 62) (#1016).

## Work in progress, saved as patches here
Each `*.wip.patch` is a builder's uncommitted work, taken with `git diff --cached --binary HEAD` in its worktree. Apply it
on a branch made from the base commit named below, then merge main and finish (build, tests, views, the note):

| Item | Patch | Base commit | State when saved |
|---|---|---|---|
| #462 the sense table (note 728) | `462-sense-table.wip.patch` | `c428d52c` | senses.json and the emitter query in progress |

`git checkout -b k1-462 c428d52c && git apply --index docs/handoff/k1/462-sense-table.wip.patch` (the base commits are on
main's history or on this branch's: `c428d52c` was this branch before #999's squash; `git fetch origin` and look it up
with `git log --all`, or apply with `git apply -3` onto main).

## Not started (specs in `specs/`, each with `specs/preamble.md`'s rules)
#453 big finds (B4's #472 already put small finds on the floors: drop that part) · #456 emotes for two (needs #454's
lamp-signal wheel, merged) · #457 bringing them home (needs #451, merged) · #460 hoist a friend (D1.5 holds the crane's
controls, #449) · #463 the handcar · #464 odd outcomes · #465 painted masks · #466 sound cues in the world (and the
loose coupling's cause, 14 Oct playtest line 12).

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
