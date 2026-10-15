=== YOUR ITEM: queue #453, note 719 — Big finds ===

Why: the audit found Opportunists (grab what's in front of you, greed against risk) and Hackers (physics to poke)
underserved: hand loot is mostly an abstract stowed sum, nothing breaks when dropped, nothing big needs two. The director
(14 Oct 2026): "Very big value things should take two to carry. That is a good idea." And earlier (App. F, 9 Oct) asked
for loot to persist so "poor stacking becomes an obstacle ... lethal company style". Queue #452 (merged before you; read
docs/notes/718.md) made value learned (each kind always worth the same, the tally naming finds), comet fragments at
stops, the finder's line and trophies — build on its find kinds and tally lines.

Build:
1. **Two-person jackpots.** New finds too heavy for one: a safe, a church bell, a ship's figurehead (pick 3; content in
   loot.json), each worth about a car-load (tuning, against run.json economy.perCar). Carried by two using the existing
   heavy two-lifter mechanic (`Body.Second`, `BodyKind.Heavy`, note 46) — check how heavy crates work and reuse it. Placed
   rarely: at most one or two a stop (tuning), in a house or a yard. Readable art (procedural kit pieces fine).
2. **Fragile valuables.** Some finds (e.g. a crate of glass, a mantel clock, medicine) lose worth when knocked: dropped
   from height, thrown, or hit hard (bodies' impact speed over a threshold, tuning), each knock taking a share of worth
   (tuning), shown only by the object's look (cracked glass, a dented clock) and the clerk naming it in the tally ("a mantel
   clock, cracked, 40"). No price shown on pickup (the director cut that).
3. **The jackpot house.** One house a stop (the chapel or the biggest house; chance by tier, tuning) is locked: the wrench
   opens its door quietly (like a Holdout's lock, D.7 — reuse) after a held Use, and inside it holds more and better finds
   (a jackpot item likelier) and is likelier to hold a house creature (the Lodger, Householder, Hollow House, Hanger;
   their spawn rules in enemies.json — weight, don't force). Readable from outside: a better door, a lamp in a window.
4. **Loose loot rides the train.** Finds and crates put down in a car stay as physics bodies (check today's behaviour:
   note 581 "loot in cars" stows finds put down in an aisle; decide with care: the director wants stacking to matter). They
   slide on bends and under braking (the car's frame acceleration), topple when stacked badly, and can fall out of an open
   side door (then they're on the line, lost unless fetched). Fragile ones take knocks this way. Keep it deterministic and
   cheap (a cap on live bodies per car, sleep when still). Stowed value is paid only for what's still aboard at the gate.
5. Views: `jackpot` (two crewmates carrying the safe), `jackpothouse`, `looseloot` (finds slid in a car after a bend);
   look at them.
6. GDD §19 (cargo, hand loot), App. F.3 village houses; note docs/notes/719.md.

Tests: a jackpot can't be lifted by one and moves with two; a fragile find loses worth on a hard knock and the tally names
it as damaged; a jackpot house is locked, the wrench opens it, and its contents' worth beats an ordinary house's on
average (seeded); loose finds in a car slide on a bend and one by an open door can fall out; NetcodeTests green.

ALSO (the director's 14 Oct playtest, docs/playtest/2026-10-14.md lines 15 and 21; put K1/#453 on those lines' rows):
"I feel like I never find anything in some of these abandoned buildings. We're going to want to reward players a little
bit more for that type of exploration. Not too much more, but smaller bits of loot"; "I feel like I'm always looking for
cupboards. I don't actually ever find loot like on the floor or anything. It's not really rewarding me for being
attentive." Add small finds lying in the open (on floors, tables, shelves, under beds, in a dead hand), lit by the
existing low work light round loose finds (note 705), worth little each, so attentive players are rewarded; tune the
houses so few rooms are bare. (Line 21 also says the cupboards "definitely need a redesign": art, leave a line for C1.)
