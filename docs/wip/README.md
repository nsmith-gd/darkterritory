# Work in progress (U1, queue #447, note 745): the yard cat, the dog, the tennis-ball launcher

`whimsy-iii.patch` is unbuilt work, saved here so a session's end doesn't lose it. It isn't part of PR #967's change: delete this
folder before #967 merges, or in the PR that lands the work.

Resume: `git apply docs/wip/whimsy-iii.patch && git rm -r docs/wip`, then:
- Add `yardCat`, `dog` and `tennisBalls` (kind whimsy, `dress:<id>`, a line and a tally) to `content/tuning/campaign.json`,
  and the whimsy tuning's new fields to `content/tuning/train.json` (`catSpeed`, `dogSpeed`, `wander`, `catWarnCars`,
  `dogHears`, `fetch`, `drop`, `launchSpeed`, `launchEvery`, `ballSeen`, `ballTake`; `WhimsyTuning` has the defaults).
- Replace `Math.Sin`, `Math.Cos`, `Math.Atan2` and `Math.Sqrt` in `World.Pets.cs` and `Enemies/Rear.cs` with `DMath`
  (DeterministicMathTests).
- Build, run `PetTests`, look at `dt screenshot --upgrades yardCat,dog,tennisBalls --stocked --view cat|dog|launcher
  [--pet-mood warn]`, then write note 745, the upgrades.md "built" line, the log, and CLAUDE.md's views.

What it is: the cat and the dog are bodies (`BodyKind.Cat`, `.Dog`) the host walks about the cars in their frames
(`World.StepPets`), their mood (`PetMood`) in the body record's ninth field. The cat wanders, sleeps on the backhead while
the fire's lit and hisses at what's aboard its car or the next; the dog barks at what's aboard or coming within `dogHears`
and fetches a toy to the nearest crewmate. The launcher (`InteractableKind.Launcher`, the guard van's platform) fires a
`TennisBall` off the back; a Cinder Hound running the train down goes for one within `ballSeen` m (`CinderHound.Fetch`),
and one that catches up drops it on the platform. Then the human cannonball (note 746).
