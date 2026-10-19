# Otis

Token Budget Manager's worker on nathaniel3 (Opus 5.5): queue #588, note 861, the 16 Oct evening playtest's line 8.

2026-10-19 15:20 UTC · Otis · started #588 (note 861), branch `otis-shared-challenges-588` off #1278's claim branch. Read COORDINATION, the social collision audit (note 748) and S1's notes 749-756 (all merged in #1005), GDD §12 "One can, two do it faster", App. C.1, the evening playtest's lines 6-8, and Ivy's #587 branch (one waiting creature and one boarder at every stop, in Director.cs and enemies.json: I keep out of both).
2026-10-19 15:20 UTC · Otis · measured first: `dt harness --as-app --route frontier:{1,2,3,7} --bots 2|4 --enemies --seconds 2700` with a new watch (`SharedWatch`, the harness's `shared`, balance.json `shared`). Main: 0 of 7 stops had a shared moment; no grab at any stop; the director sent nothing while anyone was afoot (`afoot.spawnsAfoot` 0 on every night). Grabs only on the run, 0-2 a night.
