using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The buyer in the square (queue #657, ARCHITECTURE §8 note 1027; GDD §3.1's "turning loot into scrip in the town itself"):
/// every town has one, at a stall beside the forge; a find carried to them is sold with a press of Use, host side, for a little
/// under the tally's price (less in a town cold to the crew), and a find the tally has paid for is never bought.
/// </summary>
public class BuyerTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly double Dt = SimConstants.TickSeconds;

    static LootFind Of(Run.Run run, Body b) => run.FindOf(b)!.Value;

    static TownSite Site(int seed)
    {
        string[] industries = [.. Towns.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        return new TownSite($"Fort {Towns.Surnames[seed % Towns.Surnames.Count]}", industries[seed % industries.Length], 1100, (ulong)seed, []);
    }

    [Fact]
    public void EveryTownHasABuyerAtTheirStallBesideTheForge()
    {
        var b = Towns.Tuning.Buyer!;
        Assert.True(b.Enabled, "on by default (note 1027: the director's ask is open, so the flag's on)");
        Assert.InRange(b.Pays, 0.01, 0.99);
        for (int seed = 1; seed <= 40; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            var buyer = Assert.Single(plan.People, p => p.Role == "buyer");
            var stall = Assert.Single(plan.Fixtures, f => f.Kind == "buyerstall");
            Assert.Equal("buyer", buyer.Title);
            Assert.NotEmpty(buyer.Lines);
            Assert.Contains("BUYER", stall.Text, StringComparison.Ordinal);
            Assert.Contains("FINDS BOUGHT", stall.Text, StringComparison.Ordinal);
            int side = plan.Square.Side;
            Assert.Equal(plan.Gate + b.FromGate, stall.S, 6);
            Assert.Equal(side * b.Out, stall.D, 6);
            // In front of it, toward the line, facing the line; along the train as it stands in the yard.
            Assert.Equal(stall.S, buyer.S, 6);
            Assert.Equal(side * (b.Out - b.Stand), buyer.D, 6);
            Assert.Equal(-side, buyer.FaceD, 6);
            Assert.InRange(stall.S, plan.Gate - 40, plan.Gate - 8);
        }
    }

    [Fact]
    public void TheStallStandsClearOfTheTownsBuildingsThingsAndPeople()
    {
        static bool Overlap(double s0, double hs0, double d0, double hd0, double s1, double hs1, double d1, double hd1) =>
            Math.Abs(s0 - s1) < hs0 + hs1 && Math.Abs(d0 - d1) < hd0 + hd1;
        for (int seed = 1; seed <= 40; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            var stall = plan.Fixtures.Single(f => f.Kind == "buyerstall");
            foreach (var b in plan.Buildings)
                Assert.False(Overlap(stall.S, stall.SolidS, stall.D, stall.SolidD, b.S, b.Length / 2, b.D, b.Depth / 2), $"seed {seed}: the stall in {b.Name}");
            foreach (var h in plan.Houses)
                Assert.False(Overlap(stall.S, stall.SolidS, stall.D, stall.SolidD, h.S, h.Width / 2, h.D, h.Depth / 2), $"seed {seed}: the stall in the {h.Family} house");
            foreach (var f in plan.Fixtures.Where(f => f.House < 0 && f.Id != stall.Id && f.Kind is not "mural" and not "line"))
                Assert.False(Overlap(stall.S, stall.SolidS, stall.D, stall.SolidD, f.S, f.SolidS, f.D, f.SolidD), $"seed {seed}: the stall on {f.Name}");
            foreach (var p in plan.People.Where(p => p.House < 0))
                Assert.False(Overlap(stall.S, stall.SolidS, stall.D, stall.SolidD, p.S, 0.3, p.D, 0.3), $"seed {seed}: {p.Name} in the stall");
            // And the buyer stands where nobody else does.
            var buyer = plan.People.Single(p => p.Role == "buyer");
            foreach (var p in plan.People.Where(p => p.House < 0 && p.Id != buyer.Id))
                Assert.False(Overlap(buyer.S, 0.4, buyer.D, 0.4, p.S, 0.4, p.D, 0.4), $"seed {seed}: {p.Name} where the buyer stands");
        }
    }

    [Fact]
    public void EverybodyElseIsWhoTheyWereBeforeTheBuyerCameAndOffThereIsNone()
    {
        // The buyer and their stall come last (note 1027), so nobody else's name, place, number or lines changed with them.
        var with = TownGenerator.Generate(Towns, Site(7));
        var without = TownGenerator.Generate(Towns with { Tuning = Towns.Tuning with { Buyer = Towns.Tuning.Buyer! with { Enabled = false } } }, Site(7));
        Assert.Equal(without.People.Count + 1, with.People.Count);
        Assert.Equal(without.People.Select(p => (p.Id, p.Name, p.S, p.D, string.Join("|", p.Lines))),
            with.People.Take(without.People.Count).Select(p => (p.Id, p.Name, p.S, p.D, string.Join("|", p.Lines))));
        Assert.Equal(without.Fixtures.Select(f => (f.Id, f.Kind)), with.Fixtures.Take(without.Fixtures.Count).Select(f => (f.Id, f.Kind)));
        Assert.DoesNotContain(without.People, p => p.Role == "buyer");
        Assert.DoesNotContain(without.Fixtures, f => f.Kind == "buyerstall");
    }

    [Fact]
    public void TheBuyerNeverLeavesTheStall()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var town = new Town(TownGenerator.Generate(Towns, TownSite.Of(route, gate, [], Towns)), Towns.Tuning, route.Build());
        var buyer = town.Plan.People.Single(p => p.Role == "buyer");
        var post = town.World(buyer.S, buyer.D);
        for (double t = 0; t < 1800; t += 7)
        {
            town.Clock = t;
            Assert.True((town.Now(buyer).Feet - post).Length < 0.5, $"at {t} s the buyer's {(town.Now(buyer).Feet - post).Length:0.0} m from the stall");
        }
    }

    [Fact]
    public void ThePriceIsUnderTheTallysRoundedToFivesAndLessInAColdTown()
    {
        var b = Towns.Tuning.Buyer!;
        Assert.Equal(80, b.Price(100, 0));
        Assert.Equal(40, b.Price(100, 1));
        Assert.Equal(60, b.Price(100, 0.5));
        Assert.Equal(LootTuning.LeastWorth, b.Price(5, 1));
        for (double worth = 5; worth < 3000; worth += 35)
            Assert.True(b.Price(worth, 0) <= worth, $"a find worth {worth} sells for {b.Price(worth, 0)}: the tally must stay the better deal");
    }

    // ---- In a night ----

    /// <summary>
    /// Frontier:7's train rolling in at yard speed short of its town terminus's gate, both towns stood (the arrival as cold as
    /// <paramref name="cold"/>), the stops' loot on, and its driver in the cab.
    /// </summary>
    sealed class Night
    {
        public readonly World World;
        public readonly Route.Route Route;
        public readonly List<PlayerState> Crew = [];

        public Night(int cold = 0, int cars = 4)
        {
            Route = Routes.Generate(Content, "frontier:7", cars);
            double gate = Route.Plan!.Terminus.GateM;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), Route.Build(), gate - 60, Tuning.Boiler);
            train.Dynamics.Velocity = 2.5;
            World = new World(train, Tuning.Combat);
            World.EnableBodies();
            double yard = Route.GateOr(Tuning.Route.YardLength);
            World.EnableRun(Tuning.Run, Route, yard, authority: true, FacilityTests.F, L);
            World.Run!.Resume(1200, -1, train.Boiler.Tender, 0);
            World.EnableTown(Towns, Route, yard, [], arrival: TownAt.Terminus(Route, Identities)! with { Cold = cold });
            Crew.Add(PlayerMotor.SpawnInCab(train, P));
            Crew.Add(PlayerMotor.SpawnInCab(train, P));
        }

        public Townsperson Buyer => World.Arrival!.Plan.People.Single(p => p.Role == "buyer");

        /// <summary>The finds of the route's first stop with any, put out (as the host does coming up to it).</summary>
        public List<Body> Finds()
        {
            var run = World.Run!;
            for (int k = 0; k < run.Stops.Count; k++)
            {
                run.Stock(World.Bodies, k, searched: true);
                var finds = World.Bodies.All.Where(b => b.Kind == BodyKind.Loot && run.FindOf(b) is not null).ToList();
                if (finds.Count >= 2)
                    return finds;
            }
            throw new InvalidOperationException("no stop on frontier:7 has two finds");
        }

        /// <summary>In <paramref name="who"/>'s hands.</summary>
        public void Hand(Body b, int who)
        {
            b.Carrier = who;
            b.Claimed = true;
            b.Parent = PlayerState.World;
        }

        /// <summary>Crewmate <paramref name="who"/> stood on the ground in front of the arrival's buyer.</summary>
        public void ToTheBuyer(int who)
        {
            var town = World.Arrival!;
            var buyer = Buyer;
            var at = town.World(buyer.S + 0.6, buyer.D - Math.Sign(buyer.D) * 0.9);
            double hint = World.Train.Dynamics.Distance;
            Crew[who - 1] = PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, World.Train.Line, ref hint) }, World.Train.Line, hint, P);
        }

        /// <summary>A press of Use by <paramref name="who"/> (1-based), and let go.</summary>
        public void Press(int who)
        {
            foreach (var buttons in new[] { PlayerButtons.Use, PlayerButtons.None })
            {
                World.BeginTick();
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    World.CrewAct(ref s, i == who - 1 ? new PlayerIntent { Buttons = buttons } : default, i + 1);
                    Crew[i] = s;
                }
                World.StepBodies([.. Crew.Select((c, i) => (i + 1, c))]);
                World.StepRun(Crew);
            }
        }

        /// <summary>On in at yard speed until the whole train's home inside the gate, then stopped there, and the night over.</summary>
        public RunReport Bring()
        {
            var run = World.Run!;
            for (int t = 0; t < 240 * SimConstants.TickRate && !run.Over; t++)
            {
                World.BeginTick();
                bool home = World.Train.Dynamics.Distance >= run.HomeFront(World.Train) + 2;
                if (!home && World.Train.Dynamics.Velocity < 2.5)
                    World.Train.Dynamics.Velocity = 2.5;
                World.Step(new TrainControls { Reverser = 1, Brake = home ? 1 : 0 });
                World.StepBodies([.. Crew.Select((c, i) => (i + 1, c))]);
                World.StepRun(Crew);
            }
            return Assert.IsType<RunReport>(run.Report);
        }

        /// <summary>Let go of on the first cargo car's floor, and left till it's still and banked.</summary>
        public void Stow(Body b)
        {
            var car = World.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
            var room = World.Train.Frames[car.Id].Shape.Interior!.Value;
            b.Carrier = b.Second = -1;
            b.Parent = car.Id;
            b.Pbd.Particles[0].InverseMass = 1;
            b.Pbd.Particles[0].Position = b.Pbd.Particles[0].Previous = new Double3(0, room.Min.Y + b.Pbd.Particles[0].Radius + 0.05, 0);
            b.Pbd.Wake();
            for (double t = 0; t < L.SettleSeconds + 1; t += Dt)
            {
                World.BeginTick();
                World.Train.Dynamics.Velocity = 0;
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                World.StepBodies([.. Crew.Select((c, i) => (i + 1, c))]);
                World.StepRun(Crew);
            }
            Assert.True(b.Banked, "stowed in the car");
        }
    }

    [Fact]
    public void AFindInHandIsSoldToTheBuyerForAPressOfUseHostSide()
    {
        var night = new Night();
        var run = night.World.Run!;
        var find = night.Finds()[0];
        var what = Of(run, find);
        night.Hand(find, 1);
        night.ToTheBuyer(1);
        var offer = night.World.BuyerOffer(night.Crew[0], 1);
        Assert.NotNull(offer);
        Assert.Same(find, offer.Value.Find);
        double price = Towns.Tuning.Buyer!.Price(what.Value, 0);
        Assert.Equal(price, offer.Value.Price);
        Assert.True(price < what.Value);

        night.Press(1);
        Assert.DoesNotContain(find, night.World.Bodies.All);
        Assert.Null(night.World.Bodies.CarriedBy(1));
        var sale = Assert.Single(run.Sales);
        Assert.Equal((1, what.Item, what.Value, price, false), (sale.Player, sale.Item, sale.Worth, sale.Paid, sale.AfterTally));
        Assert.Equal(price, run.Sold);

        // Sold before the tally, it's in the tally: once, at the buyer's price, and not as a find stowed.
        var report = night.Bring();
        Assert.Equal(RunEnd.Delivered, report.End);
        Assert.Equal(price, report.Sold);
        Assert.Equal(0, report.Scavenged);
        Assert.DoesNotContain(report.Prizes, p => p.Item == what.Item && p.Worth == what.Value && p.Count > 0 && report.Scavenged > 0);
        Assert.Equal(0, run.TakeSoldAfter());

        // Away from the buyer, Use with a find in hand is what it always was (here: it's put down).
        var other = night.Finds().First(b => night.World.Bodies.All.Contains(b) && b.Carrier < 0);
        night.Hand(other, 2);
        Assert.Null(night.World.BuyerOffer(night.Crew[1], 2));
        night.Press(2);
        Assert.Contains(other, night.World.Bodies.All);
        Assert.Single(run.Sales);
    }

    [Fact]
    public void AFindTheTallyPaidForIsNeverBoughtAndOneItDidntIsPaidIntoTheWalletAfter()
    {
        var night = new Night();
        var run = night.World.Run!;
        var finds = night.Finds();
        var (stowed, held) = (finds[0], finds[1]);
        night.Stow(stowed);
        // Brought home in a hand, not stowed: the tally doesn't pay for it.
        night.Hand(held, 1);
        var report = night.Bring();
        Assert.Equal(RunEnd.Delivered, report.End);
        Assert.Equal(Of(run, stowed).Value, report.Scavenged);
        Assert.True(run.Tallied(stowed));
        Assert.False(run.Tallied(held));

        // The one in hand is sold after the tally: the campaign's to take into the wallet, once.
        night.ToTheBuyer(1);
        double price = Towns.Tuning.Buyer!.Price(Of(run, held).Value, 0);
        night.Press(1);
        Assert.DoesNotContain(held, night.World.Bodies.All);
        Assert.True(Assert.Single(run.Sales).AfterTally);
        Assert.Equal(price, run.SoldAfter);
        Assert.Equal(price, run.TakeSoldAfter());
        Assert.Equal(0, run.TakeSoldAfter());
        Assert.Equal(0, report.Sold);

        // The one the tally paid for, taken back out of the car and carried to the buyer, isn't bought: nothing paid twice.
        night.Hand(stowed, 1);
        Assert.NotNull(night.World.BuyerOffer(night.Crew[0], 1));
        night.Press(1);
        night.Press(1);
        Assert.Contains(stowed, night.World.Bodies.All);
        Assert.Equal(stowed, night.World.Bodies.CarriedBy(1));
        Assert.Single(run.Sales);
        Assert.Equal(price, run.SoldAfter);
    }

    [Fact]
    public void ATownColdToTheCrewPaysLess()
    {
        var warm = new Night();
        var cold = new Night(cold: 100);
        Assert.Equal(100, cold.World.Arrival!.Plan.Cold);
        var (wf, cf) = (warm.Finds()[0], cold.Finds()[0]);
        double worth = Of(warm.World.Run!, wf).Value;
        Assert.Equal(worth, Of(cold.World.Run!, cf).Value);
        foreach (var (night, find) in new[] { (warm, wf), (cold, cf) })
        {
            night.Hand(find, 1);
            night.ToTheBuyer(1);
            night.Press(1);
        }
        var b = Towns.Tuning.Buyer!;
        Assert.Equal(b.Price(worth, 0), warm.World.Run!.Sold);
        Assert.Equal(b.Price(worth, Towns.Tuning.Standing.Of(100)), cold.World.Run!.Sold);
        Assert.True(cold.World.Run!.Sold < warm.World.Run!.Sold);
        // And the buyer says so, in the cold town only.
        Assert.Contains(cold.Buyer.Lines, l => Towns.Writing.BroughtIn!.Buyer.Any(c => l == c.Replace("{town}", cold.World.Arrival.Plan.Name)));
        Assert.DoesNotContain(warm.Buyer.Lines, l => Towns.Writing.BroughtIn!.Buyer.Any(c => l == c.Replace("{town}", warm.World.Arrival!.Plan.Name)));
    }

    [Fact]
    public void AJoinersFindIsSoldTheSameWayThroughTheirIntent()
    {
        // The departure town's buyer, a joiner over the link: their Use crosses as intent, and the host sells their find.
        var route = Routes.Generate(Content, "frontier:7", 4);
        double gate = route.GateOr(Tuning.Route.YardLength);
        World Side(bool host)
        {
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), gate - 8, Tuning.Boiler), Tuning.Combat);
            world.EnableBodies();
            world.EnableRun(Tuning.Run, route, gate, authority: host, FacilityTests.F, L);
            world.EnableTown(Towns, route, gate, []);
            return world;
        }
        var net = new Ballast.Net.LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Side(true), Tuning.Train, P);
        var joiner = new ClientSession(net.CreateClient(), Side(false), Tuning.Train, P) { Name = "Priya" };
        void Run(double seconds, PlayerIntent hands = default)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                joiner.Step(hands);
            }
        }
        Run(1);
        byte me = joiner.PlayerId!.Value;
        var world = host.World;
        var town = world.Town!;
        var buyer = town.Plan.People.Single(p => p.Role == "buyer");
        var at = town.World(buyer.S + 0.6, buyer.D - Math.Sign(buyer.D) * 0.9);
        double hint = world.Train.Dynamics.Distance;
        host.SetPlayerState(me, PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, world.Train.Line, ref hint) }, world.Train.Line, hint, P));
        var run = world.Run!;
        run.Stock(world.Bodies, 0, searched: true);
        var find = world.Bodies.All.First(b => b.Kind == BodyKind.Loot && run.FindOf(b) is not null);
        var what = Of(run, find);
        find.Carrier = me;
        find.Claimed = true;
        find.Parent = PlayerState.World;
        Run(0.5);
        Assert.Contains(find, world.Bodies.All);
        Run(SimConstants.TickSeconds * 3, new PlayerIntent { Buttons = PlayerButtons.Use });
        Run(0.5);
        Assert.DoesNotContain(find, world.Bodies.All);
        var sale = Assert.Single(run.Sales);
        Assert.Equal(me, sale.Player);
        Assert.Equal(Towns.Tuning.Buyer!.Price(what.Value, 0), sale.Paid);
        Assert.Equal(sale.Paid, run.Sold);
        // On the joiner's machine, it's gone from their hands too.
        Assert.Null(joiner.World.Bodies.CarriedBy(me));
    }

    [Fact]
    public void ThereIsNoBuyerWithTheFlagOff()
    {
        var night = new Night();
        var off = Towns with { Tuning = Towns.Tuning with { Buyer = Towns.Tuning.Buyer! with { Enabled = false } } };
        night.World.EnableTown(off, night.Route, night.Route.GateOr(Tuning.Route.YardLength), [], arrival: TownAt.Terminus(night.Route, Identities));
        Assert.DoesNotContain(night.World.Arrival!.Plan.People, p => p.Role == "buyer");
        var find = night.Finds()[0];
        night.Hand(find, 1);
        Assert.Null(night.World.BuyerOffer(night.Crew[0], 1));
    }
}
