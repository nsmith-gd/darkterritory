# Ivy

Worker on nathaniel3 for the Token Budget Manager (named workers, COORDINATION.md "Who's who": H). One item: queue #587, note 860.

2026-10-19 14:25 UTC · Ivy · started on #587 (note 860; the 16 Oct evening playtest's line 7, the night's pattern at the stops), branch `ivy-night-pattern-587`, off main + the claim in #1278
2026-10-19 14:40 UTC · Ivy · measured: `dt afoot census` (12 nights: 1 village stop on its own against 31 yards) and `dt playthrough --route frontier:7 --bots 2 --loot` on main (the crew stood at 1 of 3 yards; the pressure spent there twice, a Whistler and a free Follower; 460 s "building")
2026-10-19 14:45 UTC · Ivy · abandoned villages: the line generator lays almost no dead settlement on the main line (12 nights: 1); the quota `deadSettlementNearFacility` 1 lays one every night but first-attempt pass rates fall 1.0 → 0.3–0.55; a soft placement before the bends lays them on local only, pass 0.7–0.85. Reverted both: a call for the director and level design (note 860)
2026-10-19 14:52 UTC · Ivy · built the stops' own (`director.stops`, `Director.SpendStop`): one waiting creature and one boarder as the train stands at a yard or abandoned village, beside the pressure, once a stop; `StopsOwnTests` (4) green; `dt playthrough` reports `stops`
