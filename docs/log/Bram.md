# Bram

Token Budget Manager's worker on nathaniel3 (Opus 5.5): queue #592, note 865, Otis's call 5 in note 861 ("something for two on the run").

2026-10-19 17:40 UTC · Bram · started #592 (note 865), branch `bram-two-on-the-run-592` off main 604d280. Read COORDINATION, note 861 and #1292 (Otis's `SharedWatch`), notes 854 and 732 (the loose pin's causes and how they're told), the social collision audit, GDD §12 "One can, two do it faster".
2026-10-19 18:10 UTC · Bram · built the jumped pin (`LooseCause.Jumped`, upkeep.json `coupling.jump`): one on the pin and one on a brake wheel or the throttle, 4 s home, either letting go parts it and the line names who; minCrew 2; bots (walker on the pin, the driver eases). Vehicle record field 28 bit 10: a protocol change, the Token Budget Manager bumps it at merge. `TwoOnTheRunTests` 10/10.
2026-10-19 18:42 UTC · Bram · found the jumps never came: `Pcg32` is a struct and `readonly Pcg32 _jumps` drew the same first number forever. Fixed (not readonly) with a rate test; the same bug stands on main in HotBoxes, Gutters, Couplings._rng and Wreck (note 865, for an item of its own). perMinute 0.1 → 0.25 (a night has 260-375 s a jump can come in).
2026-10-19 20:00 UTC · Bram · measured (dt harness --as-app, frontier 1,2,3,5,6,7, 2 and 4 bots): 7 jumps in 12 nights, 4 home, 3 parted (the parted ones with a fire or a pack about: the walkers keep a loose pin's priorities). above 13 → 12 m/s, partAfter 40 → 60 s. Sim.Tests 2293/2293.
