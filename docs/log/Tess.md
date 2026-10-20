# Tess

- 2026-10-20 18:05 UTC · Tess · Claimed by the Token Budget Manager (nathaniel3): queue #619, note 905, branch `tess-cab-benches-619` (the 16 Oct playtest's lines 2 and 3).
- 2026-10-20 18:40 UTC · Tess · Benches: one thin bench along each side wall of the gallery, the middle clear (train.json crewCar bench*); 8 seats (`LoungeKind.Bench`) sat on with the Seat press through `Bbq.Sit`, got up from onto the floor in front; drawn with the sit-on-edge clip. Logbook moved onto the right bench's front end. The step: a ramp of 5 cm treads over the engine's coupler plate (`PartKind.Ramp`), deck heights unchanged (the drivers sit under the engine's deck). `CabBenchTests`; `dt screenshot --view rampside`.
- 2026-10-20 18:45 UTC · Tess · Art Checklist line `cab` marked (Tess, #619). `loco` (the ramp's art) is E1's: not touched.
- 2026-10-20 19:10 UTC · Tess · The bots' cab ways: one lane clear of table and benches (0.75 m) left the fireman (WrenchTests) and the lone sanding driver (SteamAgainstTheBrakeTests) stuck. `Lane` kept main's; `CabWay` takes the way in off a bench only abreast of it; `Heed.CabFurniture` keeps a bot in off a bench. Bots' steering untouched. Merged main (#1354).
