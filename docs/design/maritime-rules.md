# Maritime rules: the line and the land it runs through

The generator's rules for making a night's line feel like Maritime Canada (Nova Scotia, New Brunswick, PEI, gone dark). It condenses the research pass in `docs/design/research/maritime-landscape-research.md`, which has the sources and the numbers. That pass's figures come from search-result extracts; where no source gave a number the research marks its own estimate **[est.]**, and those are starting tuning values here, not facts.

All the numbers live in `content/linegen/tiers.json` (`terrain.lakes`, `terrain.shore`, `terrain.rivers`, `terrain.dykes`) and `content/linegen/biomes.json` (`lakesPerKm`, `shore`, `shoreKind`, `sweepChance`, `tidalRivers`, `flora`, `props`). The sim's side is `LineBuilder.Waterside.cs` (placement) and `TerrainField` (the land). The art's side is `PlanArt` (water, dressing, the shore's own props) and `WorldArt` (shore materials). `dt linegen water --route <spec>` lists what a night got.

The research ranks what matters most from a train window at night; the numbers below are that ranking.

## 1. The line (railway generation)

What the Maritime railways did, and what the generator now does with it.

- **The lines kept to the valleys and the shore.** The Intercolonial climbed the Wentworth Valley, the Dominion Atlantic ran the Annapolis and Gaspereau, and the Halifax & South Western headed every cove on the South Shore.
  - A biome can put the line along a shore. `shore` is the chance per biome region and `shoreKind` says which kind:
    - **sea**: the Atlantic's drowned coast.
    - **fundy**: red mudflats at low water.
    - **dyke**: a dyked marsh.
    - **river**: a river alongside the line, up its valley.
  - The shore goes on the side no branch or spur leaves by. It stops short of tunnels and pads.
  - Coast and fishing-town regions keep to the sea. Forest, hills and highland sometimes run up a river valley. The dykeland always runs its dyke.
- **Constant, wandering curvature (§9).** The South Shore line ("Hellish Slow & Wobbly") and the PEI Railway (a third of it on curves) curved almost all the time.
  - `sweepChance` makes a biome's connectors sweeps: coast 0.6, fishing town 0.4, barrens 0.35, dykeland 0.05 (dead straight across the marsh).
  - The sweeps keep the tier's radii and grades, so the validator's passes stand.
- **Lakes crossed on a fill (§2).** The South Shore line threaded its lakes and crossed their necks on fills.
  - `lakes.crossChance` (0.3) of lakes lie across the line: small ones (45-110 m) on open level ground only, never at a structure or a cutting.
  - The formation stays at rail height through the water. The land falls from its shoulder at `fillSlope` into the lake.
- **Tidal rivers on long trusses (§4).** On the Fundy side (farmland, drumlin country, dykeland: `tidalRivers`) a river crossing becomes a tidal one:
  - the span is `tidalSpanFactor` × longer, within the piece;
  - the channel is `tidalDepthFactor` × deeper;
  - the water is red-brown, with red mud banks up and down its reach.
- **Stations 3-10 km apart in settled country.** The settlements' `everyKm` [4, 8] already matched, so it's unchanged.
- **Grades and radii are unchanged.** Maritime ruling grades (0.6-1.4%) are gentler than the game's tiers. The tiers are gameplay (the plan's §3.2), so the Maritime feel comes from where the line goes, not from flattening it.

## 2. The land around it

- **The spruce wall and the alder fringe (§1).**
  - Speckled alder clumps (`NovaKit.Alder`) stand 5.5-12 m out along every grass-verged biome with trees, thicker where the ground is wet.
  - White pine (`flora.pine`) stands 20-29 m over the spruce. Tamarack (`flora.tamarack`) goes gold in the bogs.
- **Black lakes (§2).**
  - `lakesPerKm` per biome: barrens 1.4, black forest 1.0, forest edge 0.8, highland 0.8, bog 0.8, coast 0.5.
  - Each lake is an ellipse lying along the ice's flow (the drumlins' `flowDeg`), stretched 1.3-2.6× and wobbled.
  - Its water is 2.2 m under the lowest rail near it, 3.5 m deep, with a shingle shore rising at `shoreSlope`.
  - Its surface is tannin-dark `water_dark`: near black, glossy, wind ripples in the normal map.
  - A lake is kept inside the corridor the terrain models, never over another track, a pad, or another lake.
- **Granite (§3).** This was already there: the knobs landform, `granite_lichen` on the steep, erratics and outcrops.
- **Red mud and the tide out (§4).**
  - A Fundy shore has mudflats `flatM` (50-160 m) wide at low water, with channels in them.
  - A shore whose rail stands over `cliffAboveM` (11 m) above the water gets a cliff at `cliffSlope` instead of a beach.
- **The ria coast with islands and a light (§6).**
  - The sea's edge wanders `nearM` + up to `coveM` out, in coves every `coveWavelengthM`.
  - Drowned drumlins come up offshore as islands (`islandShare`).
  - At a cove's head there are two or three fish sheds on stilts at the waterline and a crib wharf (`NovaKit.Wharf`) run out from them.
  - Out on a headland there is sometimes a square tapered wooden lighthouse (`NovaKit.Lighthouse`), dark. Boulders line the tide line.
  - The sea's surface runs out 1.5 km into the fog.
- **Dykeland (§7).**
  - The hay fields lie `fieldsBelowRailM` (1.5 m) under the rail and follow it, dead flat for `landwardM` inland.
  - The dyke is `heightM` (2.2 m) high with a `crestM` (3 m) crest, `outM` (40-90 m) out. Past it is salt marsh, then red mud, then the water.
  - The sea's low water is below the fields, and its marsh stands higher than them, as the research's "sea higher than the fields" has it.
  - Where the rail rises or falls more than `maxRailRangeM` along the region, it's a mudflat shore instead.
- **Heath barrens (§8).** This was already there (heath, lichen, krummholz); jack pine now stands in them (`flora.pine`, stunted on barrens).
- **A river up the valley.**
  - The near bank is `bankM` (16-40 m) out, meandering `meanderM` more. The river is `widthM` (18-45 m) wide.
  - Its water is `belowRailM` (3.5 m) under the rail and falls with it.
  - The bed has boulders (rapids), and the far bank climbs `farSlope` into the valley side.
- **Old-field spruce (§12).** A farm field given up, grown in solid with white spruce all of an age: the `oldField` prop in farmland, drumlin country and the fishing towns.
- **White church on the rise (§10), coal country (§13), plateau and gorge (§14).** These were already there, from the earlier passes.

## 2b. The second pass: forest mass, roads and the shore at the water

After the first renders read more like moor than Nova Scotia, three changes:

- **The spruce wall.** The forest now comes in stands, world-space patches with hard edges (a cut, an old field's line, a bog's shore) covering as much of the land as the biome's tree density says (`PlanArt.Stand`, `Cover`), planted solid, with only the odd tree outside them.
  - Spruce wear a new spire card (`spruce_card`): narrow, ragged whorls, trunk gaps and a clubbed top, in place of the broad pine.
  - Long `treeline_card` walls of packed spires (`NovaKit.Treeline`) stand at the stand's near edge and deep in it, so the forest has a body and a serrated top. They are stunted on the barrens and the coast.
  - The ground under a stand is darkened to the forest's duff.
  - The far skyline was already black spruce (`PlanSky`).
- **The country road** (plan data: `LinePlan.Roads`, `LinePlan.Crossings`; `tiers.json` `terrain.roads`, biomes.json `roads`).
  - A gravel road runs `offsetM` (20-38 m) beside the main line through settled country, wandering, and crossing at grade every 1.2-3 km: it swings across over `rampM` either side.
  - Its bed is in the sim's terrain: flat across the road at the height of the land under its centre, banked back to it (`TerrainField.Roads`). It keeps clear of structures, cuttings, junctions and pads, and stays inland along the Atlantic.
  - The art draws the gravel, laid on whichever is higher of the bed and the land mesh's own surface.
  - Crossings get a plank deck and crossbucks.
  - Along the road: its own leaning poles; homesteads on the far side (saltbox, woodpile, a barn now and then, a fence along the front); and now and then a car left where it stopped. None of it lit.
- **The coast at the water.**
  - The Atlantic's water now stands `seaBelowRailM` (2 m) under the rail, `seaNearM` (12-30 m) out, with coves `seaCoveM` (15-60 m) deep, so the sea is beside the train, not below the horizon. Fundy and the dykes keep their deep low water.
  - A barachois: on an Atlantic shore, lakes become long narrow ponds lying along the line just behind its bank on the landward side, so there's water both sides.
  - At the edge: broad pale granite ledges running down into the water, weed-black rocks at the tide line, a broken surf line, and lobster traps stacked on the wharves.

## 3. How it holds together

- **Deterministic, and the same on every machine.**
  - Lakes and shores are plan data (`LinePlan.Lakes`, `LinePlan.Shores`), placed by the builder on its own RNG stream (`waterside`).
  - The terrain reads them with no trigonometry: a lake's heading is stored as a unit vector, and the drumlins' flow cosine is a series. So the tile checksums a joiner is held to still match.
  - The sightlines authority reads the terrain after the water is placed.
- **The formation is never flooded.** Wherever water lowers the land, it's held at or above a fill slope down from every track it's near, except under a bridge span. The ballast either side stays at rail height (`WatersideTests`).
- **Walkable.** The land beyond the formation is only lowered toward water, never raised, except for a dyke's fields and bank, which are set outright.

## 4. Not yet

- **Fog filling the coves first (§5).** The per-segment exposure has a fog factor, but nothing reads it: the renderer's fog is one density for the night. Varying it along the line (thicker on a shore, in the low ground) is the next step.
- **A trestle across a lake's neck.** A crossed lake is always a fill for now.
- **Boats on the mud at low tide, lobster traps along the wharf, aboiteaux (the dykes' sluices), red maple.**
- **The Tantramar railway grade doubling as the dyke.** The line runs a low bank beside its dyke rather than on it.
