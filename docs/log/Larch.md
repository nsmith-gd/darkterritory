# Larch

The Token Budget Manager's worker on nathaniel2 (Opus 5.5): queue #710, note 1005.

2026-10-23 17:58 UTC · Larch · started #710 (houses with floors and dark basements; the director's 23 Oct review, line 36), branch `larch-houses-710` off `tbm-claim-709` (the claim, PR #1690)
2026-10-23 18:20 UTC · Larch · the sim's slice: `village.forms` and `village.keep` (stops.json), `StopWalls.Storeys` (the shell, the basement under the boards, the hatch's and cellar windows' world ladders), finds by floor and loot.json `levelDraws`; StoreyedHouseTests (up the flight and down, down the hatch and out a cellar window); 0.28 m treads stuck everyone at the first, so 0.4 m
2026-10-23 18:35 UTC · Larch · the house creatures, the Gaunt's roost and the jackpot house keep to one-floor houses; bots to the ground floor (FootPath walks under and over the floors); the tests whose layouts moved with fewer houses re-aimed (HouseCreatureTests' house, SolidWorldTests' seed, the finds-a-house band)
2026-10-23 18:51 UTC · Larch · the art (TownKit.Storeys, drawn from the sim's shell), the basement a dark room (look.json `interiors.cellarFill`, Room.Dark in the renderer), `dt screenshot --storey`; note 1005; row #710 in review
2026-10-23 22:02 UTC · Larch · done and in review: full Sim suite (2663 + the 3 re-aimed) and Game suite (1610, by class) green, Release 0 warnings; village.keepFirst 5, the basement's better draws never to a healing find, bots' paths off the hatches; perf as main's (both over on the town view); head pushed for the manager's PR
