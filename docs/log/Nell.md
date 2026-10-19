# Nell

The Token Budget Manager's worker (nathaniel3; Opus 5.5): queue #590, note 863.

2026-10-19 15:01 UTC · Nell · claimed #590 (note 863, the director 19 Oct: "turn on the 3-a-night creature cap") on `nell-night-cap-590`; check.py --next said 589/862 on main, so the TBM's 590/863 are taken here
2026-10-19 15:01 UTC · Nell · main's baseline measuring from a worktree at 62ed23e (`dt trends measure --seeds 1-8`, the same 8 harness nights for by-kind counts, `dt playthrough --route frontier:7 --bots 2/4 --minutes 20`)
2026-10-19 15:01 UTC · Nell · perNightDefault null → 3; packCountsOnce (true) added, the spec silent on packs; GDD "Not every creature every night" and spec B.10's new row say what counts
2026-10-19 16:00 UTC · Nell · measured (note 863): creatures a night 22.6 → 18.0, most of a kind 12 → 3, hounds 5.0 → 2.1; the 10–15 min block 54 → 31 sends, 4-bot playthrough's last third 9 → 0 ("nothing fits" 106 → 448 s): put to the director as a call
2026-10-19 16:00 UTC · Nell · PacingTests.TheNightRisesTowardItsEnd now runs with the cap off (the pressure's shape; the cap's cost is the call); ThePressureSpendsOnTheRuleLessWhenNoRuleFits likewise, with the capped fill checked beside it. Sim.Tests 2,279/2,280 before that fix, the touched classes 35/35 after
2026-10-19 16:00 UTC · Nell · found: the Tweaker's settings/night want.perNightDefault is null, so apply.py would turn the cap off again; to be set to 3 after the merge. Row #590 in review
