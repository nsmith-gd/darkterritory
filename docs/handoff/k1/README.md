# K1's handoff: the friendslop work, queue #450–#467

Saved 14 Oct 2026 as the session neared its token limit (the director: "Save your work now so nothing is lost"). The
plan is docs/design/friendslop.md; the director's calls are GDD App. F.6. **Anyone may carry on from here** (COORDINATION
rule 9), saying so in the item's row and their log.

## Merged
- #467 the audit and the calls (#991, note 733) · #461 the mail catch (#992, note 727)
- #451 horseplay with the shove's mark, #452 the night's prizes, #454 voices, #459's coupling test (#999; notes 717, 718,
  720, 725). Protocol 59.

## Work in progress, saved as patches here
Each `*.wip.patch` is a builder's uncommitted work, taken with `git diff --cached --binary HEAD` in its worktree. Apply it
on a branch made from the base commit named below, then merge main and finish (build, tests, views, the note):

| Item | Patch | Base commit | State when saved |
|---|---|---|---|
| #450 the big cab (note 716) | `450-big-cab.wip.patch` | `0f47f040` | geometry, chart table, gallery and back wall built; sim tests being written; the cold is cosmetic only (14 Oct playtest line 14) |
| #455 crew memory (note 721) | `455-crew-memory.wip.patch` | `c428d52c` | photo, logbook, lost crews board in progress |
| #462 the sense table (note 728) | `462-sense-table.wip.patch` | `c428d52c` | senses.json and the emitter query in progress |
| #458 sessions (note 724) | `458-sessions.wip.patch` | `c428d52c` | built; its final test runs were going |

`git checkout -b k1-450 0f47f040 && git apply --index docs/handoff/k1/450-big-cab.wip.patch` (the base commits are on
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
  table, which has a lamp per crewmate aboard: share it.
- AU1's sounds asked for in #999's body and the notes.
- U1's #967 (upgrades) shares ground: asked on #967 (the gramophone and cat on the cab's back wall, the emotes' slots,
  the photographer, the speaking tubes).
