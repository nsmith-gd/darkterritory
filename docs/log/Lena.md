# Lena's log

2026-10-19 18:25 UTC · Lena · started queue #597 (note 870), the starting town's look (the director's playtest of 19 Oct, lines 1, 3, 4, 5), on `lena-town-look-597`; told Wade (#596) which files I'd touch (SquareKit, WorksKit, TownGenerator.Market, towns.json square); marked the art checklist's `night-market` line (`fortress` is B2's)
2026-10-19 18:50 UTC · Lena · before shots: frontier:21 (the lastCar town, a foundry's works) square/centre/market/marketover/works/slag/watertower, local:3's market from main; `dt art show prop-water_tower` showed the static on the turntable too
2026-10-19 19:05 UTC · Lena · line 3's cause: `GreyboxRenderer.Motion` drew every `water_*` layer as water, and the tower's atlas is `water_tower_0`; a baked model atlas never moves by its name now (WaterArtTests)
2026-10-19 19:10 UTC · Lena · line 5's cause: SquareKit's `Paint()` wore `paper_form` (the FORM 19 sheet); white paint now, and the van rebuilt as a brake van, bitten
2026-10-19 19:35 UTC · Lena · line 1: the heaps ridged, gullied, lumped, textured by facing; the slag's glow runs down its slope. Line 4: six stall forms × three colourings dealt so neighbours differ; two facing rows across an aisle, staggered (towns.json `marketAisle` 3.4, `marketJitter` 0.5, new tuning); pushed ab66914
2026-10-19 19:50 UTC · Lena · note 870 and its Handoff written; the playtest's lines 1, 3, 4, 5 and the queue row set to in review
2026-10-19 19:45 UTC · Lena · TownsfolkTests caught a second-row stall on a small town's green: Market keeps off it; Town* 136/136. The heap kept inside its footprint; its new shape sent to Wade (#596 layers the slag's walls by its profile)
