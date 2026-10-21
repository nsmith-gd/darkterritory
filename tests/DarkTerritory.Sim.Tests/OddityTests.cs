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
/// Odd outcomes to find (queue #464, GDD §3 and App. F.6; ARCHITECTURE §8 note 730; the director, 14 Oct 2026: things that
/// answer when poked, never in the Wiki). Each is triggered as a crewmate would, the host's, and the same on every machine.
/// </summary>
public class OddityTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = Tuning.Player;
    static readonly OddityTuning O = Tuning.Run.Oddities;
    static readonly Stops.LootTuning Loot = DataFile.Load<Stops.LootTuning>(Path.Combine(Content, Stops.LootTuning.File));
    /// <summary>Every stop with a village has its can, its postbox at a halt and a farmstead its scarecrow.</summary>
    static readonly OddityTuning Always = O with
    {
        Can = O.Can with { Chance = 1 },
        Scarecrow = O.Scarecrow with { Chance = 1 },
        Postbox = O.Postbox with { Chance = 1 },
        Note = O.Note with { Chance = 1 },
    };
    const double Dt = SimConstants.TickSeconds;

    /// <summary>A host's night on a generated route, no creatures: a crew stepped as the host steps them.</summary>
    sealed class Odd
    {
        public readonly World World;
        public readonly Route.Route Route;
        public readonly Dictionary<int, PlayerState> Crew = new();

        public Odd(Route.Route route, OddityTuning? t = null, TownContent? towns = null, double? front = null)
        {
            Route = route;
            double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), front ?? gate - 8), Tuning.Combat);
            World.EnableRun(Tuning.Run with { Oddities = t ?? Always }, route, gate, authority: true, loot: Loot);
            if (towns is not null)
                World.EnableTown(towns, route, gate, []);
            World.Movement = P;
        }

        public TrainOnLine Train => World.Train;

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null)
        {
            for (int i = 0; i < Math.Round(seconds * SimConstants.TickRate); i++)
            {
                World.BeginTick();
                var intents = Crew.Keys.Order().ToDictionary(id => id, id => intent?.Invoke(id) ?? default);
                foreach (var id in intents.Keys)
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, intents[id], id);
                    Crew[id] = s;
                }
                World.Step(default);
                World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys);
                foreach (var id in intents.Keys)
                {
                    var s = Crew[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, Tuning.Train, Dt, applyLook: false);
                    Crew[id] = s;
                }
                World.StepBodies([.. Crew.Select(c => (c.Key, c.Value))]);
            }
        }

        /// <summary>Crewmate stood on the ground at <paramref name="at"/>, facing <paramref name="toward"/>.</summary>
        public PlayerState Stood(Double3 at, Double3 toward)
        {
            var s = PlayerMotor.SpawnOnGround(at, Train.Line, Train.Dynamics.Distance, P);
            var d = toward - at;
            s.Yaw = DMath.Atan2(-d.X, -d.Z);
            return s;
        }
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    /// <summary>The first frontier night (by seed) with an odd thing of <paramref name="kind"/>, and that thing.</summary>
    static (Odd Night, OddThing Thing) With(OddThingKind kind)
    {
        for (int seed = 1; seed < 80; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 4);
            var odd = Oddities.Of(Always, route, route.Build());
            if (odd?.Things.FirstOrDefault(t => t.Kind == kind) is { } thing)
                return (new Odd(route), thing);
        }
        throw new InvalidOperationException($"no frontier night of 80 had a {kind}");
    }

    [Fact]
    public void TheOddThingsStandAlikeOnEveryMachineAndTheirStateMirrors()
    {
        var route = Routes.Generate(Content, "frontier:7", 4);
        var a = Oddities.Of(Always, route, route.Build())!;
        var b = Oddities.Of(Always, route, route.Build())!;
        Assert.NotEmpty(a.Things);
        Assert.Equal(a.Things.Select(t => (t.Kind, t.Feature, t.At, t.Facing)), b.Things.Select(t => (t.Kind, t.Feature, t.At, t.Facing)));
        // Off, there are none.
        Assert.Null(Oddities.Of(O with { Enabled = false }, route, route.Build()));
        // Whatever the host has done to them, a client has from the one record.
        var can = a.Things.FirstOrDefault(t => t.Kind == OddThingKind.Can);
        if (can is not null)
            a.Move(can.Index, can.At + new Double3(4, 0, 1));
        a.Finish(a.Things[0].Index);
        a.PaintingGiven = true;
        b.Mirror(a.Capture(v => (long)Math.Round(v * 1e4)), q => q / 1e4);
        Assert.True(b.PaintingGiven);
        Assert.False(b.CoinGiven);
        Assert.True(b.Done(a.Things[0].Index));
        if (can is not null)
            Assert.Equal(a.At(can.Index).X, b.At(can.Index).X, 3);
    }

    [Fact]
    public void AFindDroppedDownAWellSplashesAfterALongBeatAndTheFirstGetsACoinBack()
    {
        var (n, well) = With(OddThingKind.Well);
        var w = n.World;
        var mouth = w.Oddities!.At(well.Index);
        var by = mouth + new Double3(1.4, 0, 0);
        n.Crew[1] = n.Stood(by, mouth);
        Assert.Null(w.WellToDropIn(n.Crew[1], 1));
        // A toy in hand at its mouth: a press of Use and it's gone down.
        var toy = w.Bodies.SpawnItem(by + Double3.Up, n.Train.Dynamics.Distance, Physics.BodyKind.Toy);
        toy.Carrier = 1;
        Assert.NotNull(w.WellToDropIn(n.Crew[1], 1));
        n.Run(Dt, _ => Use);
        Assert.DoesNotContain(toy, w.Bodies.All);
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Drop && e.By == 1);
        // Heard hitting the water a long beat later, and not before.
        n.Run(O.Well.SplashSeconds - 0.3);
        Assert.DoesNotContain(w.OddEvents, e => e.Kind == OddKind.Splash);
        n.Run(0.5);
        Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Splash);
        // The night's first: something throws a coin back up, onto the ground beside the well.
        n.Run(O.Well.CoinSeconds + 0.2);
        Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Coin);
        var coin = Assert.Single(w.Bodies.All, b => b.Kind == Physics.BodyKind.Loot && b.Owner == Run.Run.OddOwner(coin: true));
        var find = w.Run!.FindOf(coin);
        Assert.Equal("oldCoin", find!.Value.Item);
        Assert.True(find.Value.Value > 0);
        Assert.Equal("An old coin", w.Run.FindName(coin));
        // The second gets nothing back: once a night.
        var another = w.Bodies.SpawnItem(by + Double3.Up, n.Train.Dynamics.Distance, Physics.BodyKind.Toy);
        another.Carrier = 1;
        n.Run(0.1);
        n.Run(Dt, _ => Use);
        n.Run(O.Well.SplashSeconds + O.Well.CoinSeconds + 1);
        Assert.Single(w.Bodies.All, b => b.Kind == Physics.BodyKind.Loot && b.Owner == Run.Run.OddOwner(coin: true));
    }

    [Fact]
    public void UnderTheCoalingChuteYouComeOutSootedAndTheWellWashesItOff()
    {
        for (int seed = 1; seed < 40; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 4);
            var facilities = route.Of(FeatureKind.Facility).ToList();
            int tower = facilities.FindIndex(f => f.Facility == FacilityKind.CoalingTower);
            if (tower < 0 || Oddities.Of(Always, route, route.Build())?.Things.FirstOrDefault(t => t.Kind == OddThingKind.Well) is not { } well)
                continue;
            var n = new Odd(route);
            var w = n.World;
            var spout = n.Train.Line.Sample(w.Run!.ChuteAt(facilities[tower], n.Train.Line).SpoutAlong).Position;
            n.Crew[1] = n.Stood(spout + new Double3(0.4, 0, 0), spout + new Double3(5, 0, 0));
            n.Crew[2] = n.Stood(spout + new Double3(8, 0, 0), spout);
            n.Run(0.2);
            Assert.False(n.Crew[1].Has(PlayerFlags.Sooted), "the chute's shut: nothing falls");
            // The chute open (as the lever leaves it): whoever's under the pour is black with it, nobody beside it.
            w.Run.Mirror(RunPhase.AtFacility, RunEnd.None, 900, tower, true, [.. Enumerable.Repeat(200.0, w.Run.FacilityCount)]);
            n.Run(0.2);
            Assert.True(n.Crew[1].Has(PlayerFlags.Sooted));
            Assert.False(n.Crew[2].Has(PlayerFlags.Sooted));
            Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Sooted && e.By == 1);
            Assert.Equal(P.Health, n.Crew[1].Health);
            // At a village's well, Use held a while washes it off.
            var mouth = w.Oddities!.At(well.Index);
            n.Crew[1] = n.Stood(mouth + new Double3(1.4, 0, 0), mouth) with { Flags = n.Crew[1].Flags };
            n.Run(O.Well.WashSeconds / 2, id => id == 1 ? Use : default);
            Assert.True(n.Crew[1].Has(PlayerFlags.Sooted), "a splash isn't a wash");
            n.Run(O.Well.WashSeconds + 0.1, id => id == 1 ? Use : default);
            Assert.False(n.Crew[1].Has(PlayerFlags.Sooted));
            Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Wash && e.By == 1);
            return;
        }
        Assert.Fail("no frontier night of 40 had a coaling tower and a well");
    }

    [Fact]
    public void AKickedCanSkittersOnAndSomethingAnswersFromTheDark()
    {
        var (n, can) = With(OddThingKind.Can);
        var w = n.World;
        var at = w.Oddities!.At(can.Index);
        // Walking up to it from 3 m off (along the line, whichever way that is here).
        var along = n.Train.Line.Sample(n.Train.Dynamics.Distance).Tangent with { Y = 0 };
        var from = at - along.Normalized * 3;
        n.Crew[1] = n.Stood(from, at + along.Normalized * 10);
        // Stood beside it, nothing happens.
        n.Crew[2] = n.Stood(at + Double3.Cross(along.Normalized, Double3.Up) * 0.3, at);
        n.Run(0.5);
        Assert.Equal(at, w.Oddities.At(can.Index));
        n.Run(1.5, id => id == 1 ? new PlayerIntent { MoveZ = 1 } : default);
        var kicked = Assert.Single(w.OddEvents, e => e.Kind == OddKind.Kick);
        Assert.Equal(1, kicked.By);
        double moved = ((w.Oddities.At(can.Index) - at) with { Y = 0 }).Length;
        Assert.InRange(moved, O.Can.Kick[0] - 0.01, O.Can.Kick[1] + 0.01);
        // A beat later, from out in the dark: answered, far off.
        n.Run(O.Can.Answer[1] + 0.2);
        var answer = Assert.Single(w.OddEvents, e => e.Kind == OddKind.Answer);
        Assert.True((answer.At - kicked.At).Length >= O.Can.AnswerM - 1);
        Assert.True(answer.Tick - kicked.Tick >= O.Can.Answer[0] * SimConstants.TickRate - 1);
        Assert.All(n.Crew.Values, s => Assert.Equal(P.Health, s.Health));
    }

    [Fact]
    public void AScarecrowIsFoundTurnedToTheLineWhenNobodysLooking()
    {
        var (n, crow) = With(OddThingKind.Scarecrow);
        var w = n.World;
        var odd = w.Oddities!;
        var line = n.Train.Line;
        Assert.Equal(crow.Facing, odd.Facing(crow.Index, line));
        // Somebody walks up to it, then back toward the line, looking back at it: it stays as it is.
        n.Crew[1] = n.Stood(crow.At + crow.Facing * -3, crow.At);
        n.Run(0.2);
        Assert.False(odd.Done(crow.Index));
        var away = crow.At + crow.Facing * -(O.Scarecrow.TurnBeyondM + 20);
        n.Crew[1] = n.Stood(away, crow.At);
        n.Run(1);
        Assert.False(odd.Done(crow.Index), "watched, it doesn't move");
        // Turned away from it: it's turned to face the line.
        n.Crew[1] = n.Stood(away, away + crow.Facing * -10);
        n.Run(0.2);
        Assert.True(odd.Done(crow.Index));
        Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Turned && e.Thing == crow.Index);
        double hint = 0;
        var (_, s) = line.Nearest(crow.At, ref hint);
        var toLine = (line.Sample(s).Position - crow.At) with { Y = 0 };
        Assert.True(Double3.Dot(odd.Facing(crow.Index, line), toLine.Normalized) > 0.99);
        Assert.True(Double3.Dot(crow.Facing, toLine.Normalized) < 0, "it stood with its back to the line");
    }

    [Fact]
    public void APostboxHoldsALetterThatGoesInTheNightsReportOnce()
    {
        var (n, box) = With(OddThingKind.Postbox);
        var w = n.World;
        w.Names[1] = "Dave";
        var at = w.Oddities!.At(box.Index);
        n.Crew[1] = n.Stood(at + box.Facing * 0.9, at);
        n.Run(O.Postbox.HoldSeconds / 2, _ => Use);
        n.Run(0.1);
        Assert.False(w.Oddities.Done(box.Index), "a tap isn't the letter");
        n.Run(O.Postbox.HoldSeconds + 0.1, _ => Use);
        Assert.True(w.Oddities.Done(box.Index));
        Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Letter && e.By == 1 && e.Thing == box.Index);
        var letter = w.Oddities.LetterIn(box.Index)!;
        Assert.Contains(letter, O.Postbox.Letters);
        var line = Assert.Single(IncidentLog.Lines(w, 0, 0, _ => true), l => l.Kind == IncidentKind.Letter);
        Assert.Contains(letter.Text, line.Text);
        Assert.Contains("Read by Dave.", line.Text);
        // Empty now: another hold takes nothing.
        n.Run(0.1);
        n.Run(O.Postbox.HoldSeconds + 0.1, _ => Use);
        Assert.Single(w.Attribution.Log, i => i.Kind == IncidentKind.Letter);
    }

    [Fact]
    public void ANoteOnADoorstepIsPickedUpReadIntoTheNightsReportOnceAndGone()
    {
        // Note 961 (GDD §3.1 "Not yet": the world's story between the towns): a note weighted by a stone at a village house's
        // door, picked up with a held Use, read out to whoever did, in the night's report, and gone from the step.
        var (n, note) = With(OddThingKind.Note);
        var w = n.World;
        w.Names[1] = "Dave";
        var at = w.Oddities!.At(note.Index);
        n.Crew[1] = n.Stood(at - note.Facing * 0.9, at);
        n.Run(O.Note.HoldSeconds / 2, _ => Use);
        n.Run(0.1);
        Assert.False(w.Oddities.Done(note.Index), "a tap doesn't pick it up");
        n.Run(O.Note.HoldSeconds + 0.1, _ => Use);
        Assert.True(w.Oddities.Done(note.Index));
        Assert.Contains(w.OddEvents, e => e.Kind == OddKind.Letter && e.By == 1 && e.Thing == note.Index);
        var read = w.Oddities.NoteIn(note.Index)!;
        Assert.Contains(read, O.Note.Notes);
        Assert.Null(w.Oddities.LetterIn(note.Index));
        var line = Assert.Single(IncidentLog.Lines(w, 0, 0, _ => true), l => l.Kind == IncidentKind.Letter);
        Assert.Contains(read.Text, line.Text);
        Assert.Contains("Picked up by Dave.", line.Text);
        // Gone: another hold there finds nothing.
        n.Run(0.1);
        n.Run(O.Note.HoldSeconds + 0.1, _ => Use);
        Assert.Single(w.Attribution.Log, i => i.Kind == IncidentKind.Letter);
    }

    [Fact]
    public void TheNightsNotesStandOffTheLineAtHousesAndNoneIsReadTwiceTillAllHaveBeenOut()
    {
        // Note 961: dealt round the night's stops by its seed, a note isn't found twice in a night till every one has been
        // out; each lies off the main line's ballast, at a village house.
        int nights = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 4);
            var line = route.Build();
            var odd = Oddities.Of(Always, route, line)!;
            var notes = odd.Things.Where(t => t.Kind == OddThingKind.Note).ToList();
            if (notes.Count == 0)
                continue;
            nights++;
            var read = notes.Select(t => odd.NoteIn(t.Index)!).ToList();
            Assert.Equal(Math.Min(notes.Count, O.Note.Notes.Count), read.Distinct().Count());
            foreach (var t in notes)
            {
                double hint = 0;
                var (_, along) = line.Nearest(t.At, ref hint);
                Assert.True(((line.Sample(along).Position - t.At) with { Y = 0 }).Length > 4, $"note {t.Index} on frontier:{seed} lies on the line");
            }
        }
        Assert.True(nights >= 6, $"only {nights} of 12 frontier nights had a note");
    }

    [Fact]
    public void BetweenTheStopsNotesLieAtTheHutsCampsGravesAndWrecksTheLineLeft()
    {
        // Note 965: out along the line, a note is left at a lineman's hut, a camp gone cold, a grave or a wreck (the railway's
        // leavings, LinesideProps.Laid), by its stretch's own draw: every one by such a leaving and off the line, the same on
        // every machine, and a few to a night.
        int total = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 4);
            var line = route.Build();
            var a = Oddities.Of(O, route, line)!;
            var b = Oddities.Of(O, route, line)!;
            Assert.Equal(a.Things.Select(t => (t.Kind, t.At)), b.Things.Select(t => (t.Kind, t.At)));
            var laid = LinesideProps.Of(route, line)!.Laid(0, line.Length + Leavings.Every)
                .Where(x => x.Leaving.Kind is "hut" or "camp" or "grave" or "wheelset" or "trolley")
                .Select(x => line.Sample(x.Leaving.Along) is var t ? t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * x.Leaving.Lateral : default)
                .ToList();
            var notes = a.Things.Where(t => t.Kind == OddThingKind.Note && t.Feature < 0).ToList();
            total += notes.Count;
            foreach (var n in notes)
            {
                Assert.Contains(laid, at => ((at - n.At) with { Y = 0 }).Length < 3.5);
                double hint = 0;
                var (_, along) = line.Nearest(n.At, ref hint);
                Assert.True(((line.Sample(along).Position - n.At) with { Y = 0 }).Length > LinesideProps.LeavingClearM, $"a note on frontier:{seed} lies on the line");
            }
        }
        Assert.InRange(total / 6.0, 1.5, 12);
    }

    [Fact]
    public void SitAWhileWithDaveAndHeGivesYouAPaintingOnceANight()
    {
        var route = Routes.Generate(Content, "frontier:7", 4);
        var n = new Night(4, speed: 0);
        n.World.EnableRun(Tuning.Run, route, 600, authority: true, loot: Loot);
        var at = n.Train.Frames[2].ToWorld(new Double3(30, 0, 0));
        double hint = n.Train.Dynamics.Distance;
        at = at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
        var dave = n.World.AddEnemy(id => Dave.At(id, at, n.Train.Dynamics.Distance, 0, Tuning.Enemies.Dave));
        PlayerState By(Double3 where)
        {
            var s = PlayerMotor.SpawnOnGround(where, n.Train.Line, n.Train.Dynamics.Distance, P);
            var d = at - where;
            s.Yaw = DMath.Atan2(-d.X, -d.Z);
            return s;
        }
        n.Crew[1] = By(at + new Double3(1.5, 0, 0.5));
        n.Crew[2] = By(at + new Double3(-1.5, 0, 0.5));
        n.Crew[3] = By(at + new Double3(0, 0, 40));
        Assert.NotNull(n.World.DaveToSitWith(n.Crew[1]));
        Assert.Null(n.World.DaveToSitWith(n.Crew[3]));
        // Crewmate 2 strikes him once (a warning, nothing more): he gives them nothing, however long they sit.
        for (double t = 0; t < 5 && dave.BlowsBy(2) < 1; t += 0.1)
            n.Run(0.1, who => who == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.Equal(1, dave.BlowsBy(2));
        n.Run(1);
        // Half the while is nothing; the whole of it is a painting at his feet, signed D.
        n.Run(O.Painting.HoldSeconds / 2, who => who is 1 or 2 ? Use : default);
        n.Run(0.1);
        Assert.False(n.World.Oddities!.PaintingGiven);
        n.Run(O.Painting.HoldSeconds + 0.2, who => who == 1 ? Use : who == 2 ? Use : default);
        Assert.True(n.World.Oddities.PaintingGiven);
        Assert.Contains(n.World.OddEvents, e => e.Kind == OddKind.Painting && e.By == 1);
        var painting = Assert.Single(n.World.Bodies.All, b => b.Kind == Physics.BodyKind.Loot && b.Owner == Run.Run.OddOwner(coin: false));
        Assert.Equal("A painting signed D", n.World.Run!.FindName(painting));
        Assert.True(n.World.Run.FindOf(painting)!.Value.Value > 0);
        Assert.True((painting.Pbd.Particles[0].Position - at).Length < 2);
        // Once a night: sat with again, nothing more.
        n.Run(O.Painting.HoldSeconds + 1, who => who == 1 ? Use : default);
        Assert.Single(n.World.Bodies.All, b => b.Kind == Physics.BodyKind.Loot && b.Owner == Run.Run.OddOwner(coin: false));
        Assert.Null(n.World.DaveToSitWith(n.Crew[1]));
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive));
    }

    /// <summary>A walled town on a frontier night, with its council house's bell rope.</summary>
    static Odd Walled()
    {
        var towns = TownContent.Load(Content)!;
        for (int seed = 1; seed < 40; seed++)
        {
            var n = new Odd(Routes.Generate(Content, $"frontier:{seed}", 4), towns: towns);
            if (n.World.Town is { BellRope: not null, Plan.People.Count: > 4 })
                return n;
        }
        throw new InvalidOperationException("no walled town in 40 nights");
    }

    [Fact]
    public void TheBellRungTurnsEveryTownspersonInEarshotToLookUpAtIt()
    {
        var n = Walled();
        var w = n.World;
        var town = w.Town!;
        var rope = town.BellRope!.Value;
        var council = town.Plan.Buildings.First(b => b.Style == "council");
        var outward = ((rope - town.World(council.S, council.D)) with { Y = 0 }).Normalized;
        n.Crew[1] = n.Stood(rope + outward * 0.6, rope);
        Assert.NotNull(w.BellRopeInReach(n.Crew[1]));
        var near = town.Plan.People.Where(p => ((town.Feet(p) - rope) with { Y = 0 }).Length <= O.Bell.EarshotM - 2).ToList();
        var far = town.Plan.People.Where(p => ((town.Feet(p) - rope) with { Y = 0 }).Length > O.Bell.EarshotM + 2).ToList();
        Assert.NotEmpty(near);
        Assert.All(town.Plan.People, p => Assert.Null(town.Reacting(p.Id)));
        // A tug isn't a ring.
        n.Run(O.Bell.HoldSeconds / 2, _ => Use);
        n.Run(0.1);
        Assert.DoesNotContain(w.OddEvents, e => e.Kind == OddKind.Bell);
        n.Run(O.Bell.HoldSeconds + 0.1, _ => Use);
        var bell = Assert.Single(w.OddEvents, e => e.Kind == OddKind.Bell);
        Assert.Equal(1, bell.By);
        n.Run(0.5);
        // Everyone within earshot has stopped where they were and is looking toward it; those beyond carry on.
        foreach (var p in near)
        {
            Assert.Equal("idle", town.Reacting(p.Id));
            var pose = town.Now(p);
            var to = (bell.At - pose.Feet) with { Y = 0 };
            Assert.True(Double3.Dot(pose.Facing, to.Normalized) > 0.99, $"{p.Name} isn't looking at the bell");
        }
        Assert.All(far, p => Assert.Null(town.Reacting(p.Id)));
        // They stay stopped while they look, then go back to what they were doing.
        var someone = near[0];
        var stood = town.Now(someone).Feet;
        n.Run(O.Bell.LookSeconds / 2);
        Assert.Equal(stood, town.Now(someone).Feet);
        n.Run(O.Bell.LookSeconds);
        Assert.Null(town.Reacting(someone.Id));
        // Rung again too soon after, it doesn't ring.
        n.Run(O.Bell.HoldSeconds + 0.1, _ => Use);
        Assert.DoesNotContain(w.OddEvents, e => e.Kind == OddKind.Bell && e.Id != bell.Id);
    }

    [Fact]
    public void ATownspersonWavedAtWavesBack()
    {
        var n = Walled();
        var w = n.World;
        var town = w.Town!;
        // Somebody out of doors, and a crewmate 6 m off them looking their way.
        var who = town.Plan.People.First(p => p.House < 0 && !p.Hosting && Town.Guards(p) is false);
        var feet = town.Feet(who);
        var off = town.Direction(who.S, 1, 0) * 6;
        n.Crew[1] = n.Stood(feet + off, feet);
        n.Run(0.2);
        Assert.Null(town.Reacting(who.Id));
        n.Run(Dt, _ => new PlayerIntent { Emote = Emote.Wave });
        n.Run(0.3);
        Assert.Equal("wave", town.Reacting(who.Id));
        var pose = town.Now(who);
        var to = (PlayerMotor.WorldPosition(n.Crew[1], n.Train) - pose.Feet) with { Y = 0 };
        Assert.True(Double3.Dot(pose.Facing, to.Normalized) > 0.99);
        n.Run(O.Wave.Seconds + 0.5);
        Assert.Null(town.Reacting(who.Id));
    }

    [Fact]
    public void ASwitchThrownUnderSomebodyOnItsBladesLurchesThemHarmlessly()
    {
        for (int seed = 1; seed < 40; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 4);
            var line = route.Build();
            if (line.Branches.Count == 0)
                continue;
            var n = new Odd(route);
            var w = n.World;
            var b = line.Branches[0];
            if (Math.Abs(b.Toe - n.Train.Dynamics.Distance) < 300)
                continue;
            var stands = w.Switches!;
            var toe = line.Sample(b.Toe + 1.5);
            var lever = stands.LeverAt(line, b.Index);
            n.Crew[1] = n.Stood(lever + (toe.Position - lever) with { Y = 0 } * 0.3, toe.Position);
            n.Crew[2] = n.Stood(toe.Position, toe.Position + toe.Tangent * 5);
            n.Crew[3] = n.Stood(toe.Position + Double3.Cross(toe.Tangent, Double3.Up).Normalized * -6, toe.Position);
            n.Run(0.3);
            var before = n.Crew[2];
            bool diverging = !n.Train.Diverging(b.Index);
            n.Run(stands.Tuning.ThrowSeconds + 0.2, id => id == 1 ? Use : default);
            Assert.Equal(diverging, n.Train.Diverging(b.Index));
            var lurch = Assert.Single(w.OddEvents, e => e.Kind == OddKind.Lurch);
            Assert.Equal(1, lurch.By);
            n.Run(1.5);
            var after = n.Crew[2];
            var right = Double3.Cross(toe.Tangent, Double3.Up).Normalized;
            double moved = Double3.Dot(PlayerMotor.WorldPosition(after, n.Train) - PlayerMotor.WorldPosition(before, n.Train), right) * b.Side;
            Assert.True(moved > 0.2, $"thrown for the branch, they go over toward it ({moved:0.00} m)");
            Assert.Equal(P.Health, after.Health);
            Assert.True(after.Alive);
            // Nobody else is touched: not the one at the lever, not the one off the track.
            Assert.DoesNotContain(w.OddEvents, e => e.Kind == OddKind.Lurch && e.Id != lurch.Id);
            return;
        }
        Assert.Fail("no frontier night of 40 had a junction away from the yard");
    }
}
