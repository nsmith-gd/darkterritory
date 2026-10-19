# Ivy

Worker on nathaniel3 for the Token Budget Manager (named workers, COORDINATION.md "Who's who": H). One item: queue #587, note 860.

2026-10-19 14:25 UTC · Ivy · started on #587 (note 860; the 16 Oct evening playtest's line 7, the night's pattern at the stops), branch `ivy-night-pattern-587`, off main + the claim in #1278
2026-10-19 14:40 UTC · Ivy · measured: `dt afoot census` (12 nights: 1 village stop on its own against 31 yards) and `dt playthrough --route frontier:7 --bots 2 --loot` on main (the crew stood at 1 of 3 yards; the pressure spent there twice, a Whistler and a free Follower; 460 s "building")
2026-10-19 14:45 UTC · Ivy · abandoned villages: the line generator lays almost no dead settlement on the main line (12 nights: 1); the quota `deadSettlementNearFacility` 1 lays one every night but first-attempt pass rates fall 1.0 → 0.3–0.55; a soft placement before the bends lays them on local only, pass 0.7–0.85. Reverted both: a call for the director and level design (note 860)
2026-10-19 14:52 UTC · Ivy · built the stops' own (`director.stops`, `Director.SpendStop`): one waiting creature and one boarder as the train stands at a yard or abandoned village, beside the pressure, once a stop; `StopsOwnTests` (4) green; `dt playthrough` reports `stops`
2026-10-19 15:30 UTC · Ivy · the whole Sim project's first run: `LampKeyTests.FrontierTwelvesFirstStopIsWorkedNotGivenUp` failed (a Whistler, the stop's boarder, in the gap the shunter cuts at the points); the boarder now waits until the crew is busy with the stop (`boardingAfterSeconds`)
2026-10-19 16:20 UTC · Ivy · whole Sim project green, 2,282 (90 s build); boarder from 40 s stood (later stops are short); stop, lamp-key, Grumbler-perch and night-curve tests green on it; merged main
2026-10-19 17:00 UTC · Ivy · measured: 2-bot playthroughs seeds 1-4 (something in place within 30 s at 6 of 8 stops against 3 of 8; a boarder at 5 of 8; delivered 2 of 4 against 3 of 4) and `dt trends measure --seeds 1-4` (off = main's line; on: deaths 0.25 → 1.25, net 1,484 → 961, all delivered, nothing past the bands); note 860, row and playtest line set to in review
