=== YOUR ITEM: queue #463, note 729 — The handcar ===

Why: the audit's Hackers and Friends goals: a two-person pump trolley is railway slapstick, a way back for a friend left
behind, a scout down a branch, and a race. Approved by the director on 14 Oct 2026 (GDD App. F.6). The audit sized it L.

Read first: how rakes/vehicles run on the line (`TrainOnLine`, `TrainDynamics`, `Consist`, `RailLine` paths and branches,
the points/switches), how players ride a car's frame, `PlayerMotor` moving frames, the guns' "seat" style interactions,
and how the dining/flat cars or the switchyard's standing rakes are represented. Also U1's open PR #967 for upgrade
patterns (an upgrade card if you make it purchasable).

Build:
1. **What it is.** A small hand-pumped trolley (a platform on four wheels with a see-saw pump handle), carried on a
   flatcar/the guard van's rear deck, or standing at some stops' sidings (pick one source that fits the game; a fortress
   upgrade "the handcar" in campaign.json is natural — then it rides on the guard van's rear platform; for testing, a
   tuning flag to always have one).
2. **Getting it on the rails.** At a stand, two crewmates (or one, slowly: tuning) lift it off and set it on the track
   behind or ahead of the train (a held Use, like the heavy two-lifter, note 46). It's its own rake on the line (a vehicle
   of its own kind), so it follows the line's paths and points like any car. It can't pass the train; it couples to
   nothing (or couples gently to the rear car to be hauled — decide; keep it simple).
3. **Pumping.** One or two crewmates stand on it at the handle; pumping (alternate W/S, or hold W — pick a rhythm-based
   input that's fun and works with keyboard: e.g. each pump stroke is a press timed to the handle's swing; two pumpers in
   rhythm go faster, out of rhythm slower, like the capstan winch's rhythm, notes on the winch) drives it up to ~8 m/s
   (tuning), a brake lever stops it, reverse by pumping the other way. Grades matter (uphill hard, downhill runaway).
   Deterministic, intent-driven.
4. **Uses:** fetch a friend left behind (the hunts still apply — it's the open line at night); scout a branch past points;
   race back to the train. Getting it back aboard: at a stand, lift it back onto its place (or leave it).
5. **Creatures:** it's on the open line, so lineside creatures treat its riders as on foot (they're exposed); the
   Gannet's fast-train rule doesn't apply. Wakers at dawn take it like anything else.
6. Views `handcar` (two crewmates pumping on the line at night, the lamp ahead) and `handcarlift`; look at them.
7. GDD: a short new section (e.g. under §10 or §24); note docs/notes/729.md.

Tests: it can be set on the rails at a stand and not at speed; two pumping in rhythm reach a higher speed than one or
two out of rhythm; it follows the line's path through points; riders ride its frame; it stops with the brake; NetcodeTests
green with a handcar running.
