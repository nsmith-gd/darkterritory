using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The fortress towns (GDD §3.1, App. F.1 T133; ARCHITECTURE §8 note 281): each its own custom, people with lines, papers
/// to read, made alike on every machine, solid where they stand.
/// </summary>
public class TownTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly PlayerTuning P = Tuning.Player;

    static TownSite Site(int seed, IReadOnlyList<string>? roster = null, string? last = null)
    {
        string[] industries = [.. Towns.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        return new TownSite($"Fort {Towns.Surnames[seed % Towns.Surnames.Count]}", industries[seed % industries.Length], 1100, (ulong)seed, roster ?? [], last);
    }

    static (Route.Route Route, TrainOnLine Train, Town Town) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, gate - 8);
        var town = new Town(TownGenerator.Generate(Towns, TownSite.Of(route, gate, [], Towns)), Towns.Tuning, line);
        // As World.EnableTown stands them: the fortresses' solids (T124), the departure one stepping back round the square.
        train.Walls = StopWalls.Of(route, line, Forts(route, line, gate, town));
        train.Walls.Add(town.Walls);
        return (route, train, town);
    }

    static List<Fort> Forts(Route.Route route, RailLine line, double gate, Town town)
    {
        var forts = Fortresses.Of(route, line, gate, Tuning.Run.TerminusZone);
        return [forts[0] with { Square = town.Plan.Square }, .. forts.Skip(1)];
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("local:3")]
    public void TheFortressStandsBackRoundTheSquare(string spec)
    {
        // T124's solid fortress (note 274) and the town's square (note 281): no wall bay across the square's mouth, no gun
        // tower or village house in it, and the walls either side of it still there.
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var town = new Town(TownGenerator.Generate(Towns, TownSite.Of(route, gate, [], Towns)), Towns.Tuning, line);
        var sq = town.Plan.Square;
        var solids = Fortresses.Solids(Forts(route, line, gate, town)[0], line).ToList();
        (double S, double D) Rail(Double3 p)
        {
            double s = (sq.S0 + sq.S1) / 2;
            line.Nearest(p, ref s);
            var t = line.Sample(s);
            return (s, Double3.Dot(p - t.Position, Double3.Cross(t.Tangent, Double3.Up).Normalized));
        }
        foreach (var w in solids)
        {
            var (s, d) = Rail(w.Centre);
            bool across = Math.Sign(d) == sq.Side && Math.Abs(d) > 2.5 && Math.Abs(d) < Math.Abs(sq.WallD) - 1;
            Assert.False(across && s + w.HalfLength > sq.S0 + 0.2 && s - w.HalfLength < sq.S1 - 0.2,
                $"a fortress solid {w.HalfLength * 2:0.0} m long at ({s - sq.S0:0.0} m into the square, {d:0.0}) stands in it");
        }
        // The wall on the square's side runs up to it and on from it.
        bool Wall(double at) => solids.Any(w => w.HalfWidth == Fortresses.WallHalf && Rail(w.Centre) is var (s, d)
            && Math.Sign(d) == sq.Side && Math.Abs(s - at) < w.HalfLength + 0.01);
        Assert.True(Wall(sq.S0 - 1) && Wall(sq.S1 + 1), "the wall either side of the square");
        Assert.False(Wall((sq.S0 + sq.S1) / 2), "a wall across the square's mouth");
    }

    [Fact]
    public void TheSameRouteMakesTheSameTownOnEveryMachine()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(600);
        string Made() => JsonSerializer.Serialize(TownGenerator.Generate(Towns, TownSite.Of(route, gate, [], Towns)));
        Assert.Equal(Made(), Made());
        // And another night's is another town.
        var other = Routes.Generate(Content, "frontier:8", 6);
        Assert.NotEqual(Made(), JsonSerializer.Serialize(TownGenerator.Generate(Towns, TownSite.Of(other, other.GateOr(600), [], Towns))));
    }

    [Fact]
    public void EveryCustomIsWrittenInFullForACreatureThatExists()
    {
        var creatures = Tuning.Enemies.Director.Costs.Keys.Append("choir").ToHashSet();
        Assert.True(Towns.Writing.Cultures.Length >= 12, "a dozen customs at least");
        Assert.Equal(Towns.Writing.Cultures.Length, Towns.Writing.Cultures.Select(c => c.Id).Distinct().Count());
        foreach (var c in Towns.Writing.Cultures)
        {
            Assert.Contains(c.Creature, creatures);
            Assert.InRange(c.Law.Length, 10, 60);
            // The director (7 Oct): the lore is in what people say; the papers are the lesser part of it.
            Assert.True(c.Lines.Length >= 8, $"{c.Id}: {c.Lines.Length} lines");
            Assert.True(c.Notes.Length >= 2, $"{c.Id}: {c.Notes.Length} notes");
            Assert.NotEqual(TownFixtures.Size("unknown"), TownFixtures.Size(c.Centrepiece.Kind));
            Assert.NotEmpty(c.Centrepiece.Text);
        }
        // Each of the demo's creatures has a custom of its own (GDD §21: the demo's five, the Choir under them).
        foreach (string demo in new[] { "trackDoll", "carHugger", "whistler", "tippyToesie", "ribbits", "choir" })
            Assert.Contains(Towns.Writing.Cultures, c => c.Creature == demo);
    }

    [Fact]
    public void EverythingSaidFitsTheCardsAndNothingIsLeftUnfilled()
    {
        for (int seed = 1; seed <= 120; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            foreach (var p in plan.People)
            {
                Assert.True(p.Lines.Count is >= 1 and <= 3, $"seed {seed}, {p.Title}: {p.Lines.Count} lines");
                foreach (var line in p.Lines)
                {
                    Assert.True(line.Length <= 150, $"seed {seed}, {p.Title}: {line.Length} chars: {line}");
                    Assert.DoesNotContain('{', line);
                    // Plain print, and the Acadian names' accents (the card's font draws them as their plain letters).
                    Assert.All(line, ch => Assert.True(ch is >= ' ' and <= '}' || "àâçéèêëîïôùûü".Contains(ch), $"'{ch}' in {line}"));
                }
                Assert.DoesNotContain('{', p.Title);
            }
            foreach (var paper in plan.Papers)
            {
                Assert.True(paper.Text.Length <= 420, $"seed {seed}, {paper.Title}: {paper.Text.Length} chars");
                Assert.True(paper.Title.Length <= 40, $"seed {seed}: {paper.Title}");
                Assert.DoesNotContain('{', paper.Text + paper.Title);
            }
            foreach (var f in plan.Fixtures)
                Assert.True(f.Text.Length <= 420 && !f.Text.Contains('{'), $"seed {seed}, {f.Kind}: {f.Text}");
            // Nobody in a town says what somebody else there says.
            var said = plan.People.SelectMany(p => p.Lines).ToList();
            Assert.Equal(said.Count, said.Distinct().Count());
            Assert.Equal(plan.People.Count, plan.People.Select(p => p.Name).Distinct().Count());
        }
    }

    [Fact]
    public void ATownHasAGatekeeperWhoSaysItsLawAKeeperAndAFullBoard()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            var culture = Towns.Writing.Cultures.Single(c => c.Id == plan.Culture);
            var gatekeeper = Assert.Single(plan.People, p => p.Role == "gatekeeper");
            Assert.Contains(plan.Law, gatekeeper.Lines[0]);
            var keeper = Assert.Single(plan.People, p => p.Role == "keeper");
            Assert.Contains(plan.Hall, keeper.Title);
            // The keeper of the custom's hall talks about it first (its lines' placeholders filled in).
            Assert.Contains(culture.Lines, l => System.Text.RegularExpressions.Regex.IsMatch(keeper.Lines[0],
                "^" + System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Escape(l), @"\\\{[a-z0-9]+}", ".+") + "$"));
            var board = plan.Papers.Where(p => p.OnBoard).ToList();
            Assert.InRange(board.Count, Towns.Tuning.Notices[0], Towns.Tuning.Notices[1]);
            Assert.Equal(culture.Notes[0].Title, board[0].Title);
            Assert.Contains(plan.Papers, p => !p.OnBoard);
        }
    }

    [Fact]
    public void ATownKeepsOnlyTheCustomsOfTheCreaturesItsEditionFields()
    {
        // The demo's roster (editions/demo/tuning/enemies.json).
        string[] demo = ["trackDoll", "carHugger", "whistler", "tippyToesie", "ribbits", "sleepers", "drift", "carFire"];
        var seen = new HashSet<string>();
        for (int seed = 1; seed <= 200; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed, demo));
            Assert.True(plan.Creature == "choir" || demo.Contains(plan.Creature), $"seed {seed}: {plan.Culture} ({plan.Creature})");
            seen.Add(plan.Culture);
        }
        Assert.Equal(Towns.Writing.Cultures.Count(c => c.Creature == "choir" || demo.Contains(c.Creature)), seen.Count);
    }

    [Fact]
    public void NoTownSharesTheCustomOfTheLastOne()
    {
        string? last = null;
        for (int seed = 1; seed <= 200; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed, ["ribbits", "whistler"], last));
            Assert.NotEqual(last, plan.Culture);
            last = plan.Culture;
        }
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("local:3")]
    [InlineData("deadLines:3")]
    public void EverybodyStandsClearOfTheTrainTheWallsAndEachOther(string spec)
    {
        var (_, train, town) = Night(spec);
        var plan = town.Plan;
        double gate = plan.People.Single(p => p.Role == "gatekeeper").S + 9;
        foreach (var p in plan.People)
        {
            Assert.True(Math.Abs(p.D) >= 2.5, $"{p.Title} at {p.D:0.0}: in the train's way");
            Assert.True(p.S < gate, $"{p.Title} outside the gate");
            // Inside the yard's walls, or a walled town's (queue #74, note 335).
            Assert.True(plan.Bounds?.Holds(p.S, p.D, 1) ?? (Math.Abs(p.D) < 14.8 || plan.Square.Holds(p.S, Math.Sign((int)Math.Round(p.D)))),
                $"{p.Title} outside the walls at {p.D:0.0}");
            var feet = town.Feet(p);
            // In no wall (they go about their rounds, so they've none of their own: note 353; TownsfolkTests walk them).
            int inside = train.Walls!.Near(feet).Count(w =>
            {
                var l = w.ToLocal(feet);
                return Math.Abs(l.X) < w.HalfLength - 0.01 && Math.Abs(l.Z) < w.HalfWidth - 0.01 && feet.Y + 1 > w.Bottom && feet.Y < w.Top;
            });
            Assert.True(inside == 0, $"{p.Title} at ({p.S - gate:0.0}, {p.D:0.0}) is in {inside} walls");
        }
    }

    [Fact]
    public void WalkingAtTheHallStopsAtItsFront()
    {
        var (_, train, town) = Night("frontier:7");
        var hall = town.Plan.Buildings.Single(b => b.Kind == "hall");
        int side = Math.Sign(hall.D);
        // From the line's side of the square (clear of the folk at its door), straight at the hall's middle for ten seconds.
        var from = town.World(hall.S - 4, side * 4);
        var s = PlayerMotor.SpawnOnGround(from, train.Line, hall.S - 4, P);
        var to = town.World(hall.S, hall.D) - from;
        s.Yaw = Math.Atan2(-to.X, -to.Z);
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
        double reached = Math.Abs(Double3.Dot(s.Position - town.World(hall.S, 0), town.Direction(hall.S, 0, side)));
        Assert.True(reached < Math.Abs(hall.D) - hall.Depth / 2 + 0.05, $"walked {reached:0.00} m out, into the hall (its front is {Math.Abs(hall.D) - hall.Depth / 2:0.00})");
        Assert.True(reached > Math.Abs(hall.D) - hall.Depth / 2 - 2.5, $"stopped {reached:0.00} m out, short of the hall");
    }

    [Fact]
    public void WhatYouLookAtCloseUpIsWhatYouTalkToOrRead()
    {
        var (_, _, town) = Night("frontier:7");
        var person = town.Plan.People.Single(p => p.Role == "clerk");
        var chest = town.Feet(person) + Double3.Up * 1.45;
        var front = town.Direction(person.S, person.FaceS, person.FaceD);
        // A step and a half in front of them, looking at them.
        var eye = chest + front * 1.5 + Double3.Up * 0.15;
        Assert.Equal(new TownTarget(TownTargetKind.Person, person.Id), town.Target(eye, (chest - eye).Normalized));
        // Turned away, nothing; too far off, nothing.
        Assert.Null(town.Target(eye, (eye - chest).Normalized));
        var far = chest + front * 6;
        Assert.Null(town.Target(far, (chest - far).Normalized));
        // At the board: the board, its notices read there in turn.
        var board = town.Plan.Fixtures.Single(f => f.Kind == "board");
        var paper = town.World(board.S, board.D, 1.5);
        var before = paper + town.Direction(board.S, board.FaceS, board.FaceD) * 1.6 + Double3.Up * 0.1;
        Assert.Equal(TownTargetKind.Board, town.Target(before, (paper - before).Normalized)?.Kind);
        Assert.Equal(town.Plan.Papers.Count(p => p.OnBoard), town.Notices.Count);
    }

    [Fact]
    public void ATalkingPlayerOnTheGroundIsLookedFromTheirEyes()
    {
        var (_, train, town) = Night("frontier:7");
        var person = town.Plan.People.Single(p => p.Role == "clerk");
        var feet = town.Feet(person) + town.Direction(person.S, person.FaceS, person.FaceD) * 1.5;
        var eye = feet + Double3.Up * train.Dynamics.Tuning.Pick.EyeHeight;
        var to = town.Feet(person) + Double3.Up * 1.45 - eye;
        var player = new PlayerState
        {
            Parent = PlayerState.World,
            Position = feet,
            Surface = Surface.Ground,
            Health = P.Health,
            Yaw = Math.Atan2(-to.X, -to.Z),
            Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z)),
        };
        Assert.Equal(new TownTarget(TownTargetKind.Person, person.Id), town.Target(player, train.Dynamics.Tuning.Pick.EyeHeight));
        // Up on a car, nobody in the town is in reach: you get down to talk.
        Assert.Null(town.Target(player with { Parent = 1 }, train.Dynamics.Tuning.Pick.EyeHeight));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The houses and households (the director, 7 Oct 2026; note 281)

    [Fact]
    public void ATownIsTwentyToThreeThousandPeopleInHousesAndHasLostSome()
    {
        var t = Towns.Tuning;
        for (int seed = 1; seed <= 60; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            Assert.InRange(plan.Population, t.Population[0], t.Population[1]);
            Assert.True(plan.Former > plan.Population, $"seed {seed}: {plan.Former} once, {plan.Population} now");
            var open = plan.Houses.Where(h => h.Kind == HouseKind.Open).ToList();
            Assert.InRange(open.Count, Math.Min(t.Explorable.Min, plan.Houses.Count(h => h.Kind is HouseKind.Open or HouseKind.Lived)), t.Explorable.Max);
            foreach (var h in plan.Houses)
            {
                // A shut house says something when you knock or look; an open one has its rooms and somebody at home.
                Assert.Equal(h.Kind == HouseKind.Open, h.Layout is not null);
                if (h.Kind == HouseKind.Open)
                    Assert.Contains(plan.People, p => p.House == h.Id);
                else
                    Assert.False(string.IsNullOrWhiteSpace(h.Text), $"seed {seed}: the {h.Family}s' {h.Kind} house says nothing");
                Assert.Contains(h.Family, Towns.Surnames);
            }
            // Nobody lives in a house that isn't open to you (the rest are behind their doors).
            Assert.All(plan.People.Where(p => p.House >= 0), p => Assert.Equal(HouseKind.Open, plan.Houses[p.House].Kind));
            // The houses stand apart, between the line and the walls (a walled town's, or the yard's: WalledTownTests has
            // the walled town's streets and lanes).
            foreach (var a in plan.Houses)
            {
                if (plan.Bounds is { } b0)
                    Assert.True(b0.Holds(a.S, a.D + a.Side * a.Depth / 2), $"seed {seed}: house {a.Id} past the town's wall");
                else
                    Assert.InRange(Math.Abs(a.D) + a.Depth / 2, 0, Fortresses.WallOut - Fortresses.WallHalf);
                Assert.True(Math.Abs(a.D) - a.Depth / 2 > 3, $"seed {seed}: a house {Math.Abs(a.D) - a.Depth / 2:0.0} m off the line");
                // Its neighbours in its row.
                foreach (var b in plan.Houses.Where(b => b.Id > a.Id && Math.Abs(b.D - a.D) < 0.01))
                    Assert.True(Math.Abs(a.S - b.S) >= (a.Width + b.Width) / 2, $"seed {seed}: houses {a.Id} and {b.Id} overlap");
            }
        }
    }

    /// <summary>Where a point is in a house's own frame: along its front from its middle (u), in from its front (v).</summary>
    static (double U, double V) InHouse(Town town, RailLine line, TownHouse h, Double3 p)
    {
        double s = h.S;
        line.Nearest(p, ref s);
        var t = line.Sample(s);
        double d = Double3.Dot(p - t.Position, Double3.Cross(t.Tangent, Double3.Up).Normalized);
        return (s - h.S, (d - h.FrontD) * h.Side);
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("local:3")]
    public void YouWalkInAtAnOpenHousesDoorAndThroughToTheParlour(string spec)
    {
        var (_, train, town) = Night(spec);
        var h = town.Plan.Houses.First(x => x.Kind == HouseKind.Open);
        var l = h.Layout!;
        // From the step, straight in through the front door as far as the doorway in the partition.
        var (s0, d0) = h.Rail(l.DoorU, -2.5);
        var player = PlayerMotor.SpawnOnGround(town.World(s0, d0), train.Line, s0, P);
        void Walk(double fu, double fv, double seconds, Func<(double U, double V), bool> until)
        {
            var dir = town.Direction(h.S, fu, h.Side * fv);
            player.Yaw = Math.Atan2(-dir.X, -dir.Z);
            for (int i = 0; i < seconds * SimConstants.TickRate && !until(InHouse(town, train.Line, h, player.Position)); i++)
                PlayerMotor.Step(ref player, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
        }
        Walk(0, 1, 6, at => at.V >= l.PassV);
        var inside = InHouse(town, train.Line, h, player.Position);
        Assert.True(inside.V >= l.PassV - 0.1, $"stopped {inside.V:0.00} m in (the partition's doorway is {l.PassV:0.00} in)");
        Assert.InRange(inside.U, l.DoorU - 0.5, l.DoorU + 0.5);
        // Then across, through the partition's doorway, into the parlour.
        Walk(-l.Kitchen, 0, 6, at => at.U * l.Kitchen < -0.8);
        var parlour = InHouse(town, train.Line, h, player.Position);
        Assert.True(parlour.U * l.Kitchen < -0.8, $"stopped at u {parlour.U:0.00}: never through the partition");
        Assert.InRange(parlour.V, HouseLayout.Wall, h.Depth - HouseLayout.Wall);
    }

    [Fact]
    public void AShutHousesWallsKeepYouOutAndNobodyTalksThroughAWall()
    {
        var (_, train, town) = Night("frontier:7");
        // A lived-in house, shut: walking at its middle stops at its front.
        var shut = town.Plan.Houses.First(x => x.Kind == HouseKind.Lived);
        var (fs, fd) = shut.Rail(0, -3);
        var player = PlayerMotor.SpawnOnGround(town.World(fs, fd), train.Line, fs, P);
        var into = town.Direction(shut.S, 0, shut.Side);
        player.Yaw = Math.Atan2(-into.X, -into.Z);
        for (int i = 0; i < 5 * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref player, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
        Assert.True(InHouse(town, train.Line, shut, player.Position).V < 0.05, "walked into a shut house");
        // Somebody at home in an open house: seen from its doorway, but not from behind its back wall.
        var h = town.Plan.Houses.First(x => x.Kind == HouseKind.Open);
        var who = town.Plan.People.First(p => p.House == h.Id);
        var face = town.Feet(who) + Double3.Up * 1.45;
        var (ds, dd) = h.Rail(h.Layout!.DoorU, 0.3);
        var door = town.World(ds, dd, 1.65);
        Assert.True(town.Seen(door, face) || town.Seen(town.World(who.S + who.FaceS, who.D + who.FaceD, 1.65), face), "nobody at home to be seen");
        var (bs, bd) = h.Rail((who.S - h.S), h.Depth + 1.2);
        var behind = town.World(bs, bd, 1.65);
        Assert.False(town.Seen(behind, face), "seen through the back wall");
        Assert.NotEqual(new TownTarget(TownTargetKind.Person, who.Id), town.Target(behind, (face - behind).Normalized));
    }

    [Fact]
    public void HousesVaryWithinATownAndTownsDifferInCharacter()
    {
        // The director (7 Oct, with his photographs): "lots of variations so it doesn't feel like the same 10 assets
        // recycled across towns". Every character turns up, and within a town no two standing houses look alike (form,
        // storeys, roof, dormers, siding, paint, wing, porch), but a company town's, which are one house in its own paints.
        var characters = new HashSet<string>();
        int towns = 0;
        for (int seed = 1; seed <= 80; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            characters.Add(plan.Character);
            // The houses round the square, where a crew walks (a big town's streets go on past them: it can't be all one
            // of each, but no stretch of street should look like a row of copies).
            var standing = plan.Houses.Where(h => h.Kind != HouseKind.Burnt).Take(40).ToList();
            if (standing.Count < 6)
                continue;
            towns++;
            var looks = standing.Select(h => (h.Design.GableFront, h.Design.Storeys, h.Design.Roof, h.Design.Dormer, h.Design.Shingle,
                h.Design.Paint, h.Design.Ell != 0, h.Design.Porch)).ToList();
            if (plan.Character == "company")
                Assert.Single(looks.Select(l => (l.GableFront, l.Storeys, l.Roof, l.Dormer)).Distinct());
            else
                Assert.True(looks.Distinct().Count() >= looks.Count * 0.8, $"seed {seed} ({plan.Character}): {looks.Distinct().Count()} looks among {looks.Count} houses");
            // What stands of a house stays in its lot and off the line.
            foreach (var h in plan.Houses)
                foreach (var (u0, u1, v0, v1, _) in h.Parts())
                {
                    // A lot on the line's own street is st.Every long; a walled town's street's, up to walled.lot's most.
                    double lot = Math.Abs(Math.Abs(h.D) - Towns.Tuning.Houses.Out) < 0.01 ? Towns.Tuning.Houses.Every : Towns.Tuning.Walled.Lot[1];
                    Assert.True(Math.Max(Math.Abs(u0), Math.Abs(u1)) <= lot / 2 - 0.3, $"seed {seed}: house {h.Id} reaches {Math.Max(Math.Abs(u0), Math.Abs(u1)):0.0} m along");
                    Assert.True(Math.Abs(h.FrontD) - Math.Max(0, -v0) > 3, $"seed {seed}: house {h.Id}'s porch within 3 m of the line");
                }
        }
        Assert.True(towns > 30, $"{towns} towns with houses enough");
        foreach (var c in Towns.Looks.Characters.Where(c => c.Id != "mixed"))
            Assert.Contains(c.Id, characters);
    }
}
