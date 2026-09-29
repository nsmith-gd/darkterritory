using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Things that take two (T43, spec D.2): the winch's cranks turned by hand and stalled out of rhythm, and heavy crates
/// with a carrier at each end, a headset taking its end with both hands.
/// </summary>
public class TwoHandedTests
{
    static readonly FacilityTuning F = FacilityTests.F;
    static readonly CrankTuning C = F.Winch.Crank;
    static readonly PlayerIntent Hold = new() { Buttons = PlayerButtons.Use };

    /// <summary>A headset's intent with its hands at world points (a player on the ground, not turning).</summary>
    static PlayerIntent Hands(in PlayerState s, Double3 hand, Double3? other = null, PlayerButtons buttons = PlayerButtons.Use)
    {
        var at = s.Position;
        double c = Math.Cos(s.Yaw), n = Math.Sin(s.Yaw);
        Double3 Local(Double3 w)
        {
            var d = w - at;
            return new Double3(d.X * c - d.Z * n, d.Y, d.X * n + d.Z * c);
        }
        var intent = new PlayerIntent { Buttons = buttons };
        intent.Reach(Local(hand), other is { } o ? Local(o) : null);
        return intent;
    }

    /// <summary>A winch stop with a headset player at crank 0 and a keyboard at crank 1.</summary>
    static FacilityTests.Stop AtTheCranks()
    {
        var stop = new FacilityTests.Stop(ModuleKind.Winch);
        stop.World.Hand = Tuning.Player.Hand;
        foreach (var handle in stop.Site.Handles)
            stop.Crew.Add(stop.OnTheGround(handle - Double3.Up * 0.9));
        return stop;
    }

    /// <summary>The headset's hand goes round crank 0 at <paramref name="revs"/> a second, the keyboard holding crank 1.</summary>
    static void Crank(FacilityTests.Stop stop, double revs, double seconds, bool partner = true)
    {
        var site = stop.Site;
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            double a = 2 * Math.PI * revs * t * SimConstants.TickSeconds;
            var hand = site.Handles[0] + (site.Outward * Math.Cos(a) + Double3.Up * Math.Sin(a)) * site.CrankRadius;
            stop.Step(SimConstants.TickSeconds, [Hands(stop.Crew[0], hand), partner ? Hold : default]);
        }
    }

    [Fact]
    public void AHandTurnsTheCrankByGoingRoundWithIt()
    {
        var stop = AtTheCranks();
        var site = stop.Site;
        // At the crank's own pace with a keyboard on the other: it hauls as two keyboards do, bar the hand's first moment.
        Crank(stop, C.RevsPerSecond, 10);
        Assert.True(site.Turning);
        Assert.InRange(site.Progress, 0.93 * 10 * F.Winch.Speed / F.Winch.HaulMetres, 10 * F.Winch.Speed / F.Winch.HaulMetres);
        // The drum's turned with it.
        Assert.NotEqual(0, site.Crank);

        // Faster than the crank's pace hauls no faster: a headset is no stronger than a keyboard.
        double before = site.Progress;
        Crank(stop, C.RevsPerSecond * 1.8, 4);
        Assert.InRange(site.Progress - before, 0, 4 * F.Winch.Speed / F.Winch.HaulMetres + 1e-6);
    }

    [Fact]
    public void OutOfRhythmTheDrumStalls()
    {
        var stop = AtTheCranks();
        var site = stop.Site;
        // Somewhat slower than the other crank: it goes at the slower one's pace.
        Crank(stop, C.RevsPerSecond * 0.8, 6);
        Assert.True(site.Turning);
        double slower = site.Progress;
        Assert.InRange(slower, 0.7 * 6 * F.Winch.Speed / F.Winch.HaulMetres, 0.8 * 6 * F.Winch.Speed / F.Winch.HaulMetres);

        // Much slower, or backwards, or holding still: out of rhythm, and it doesn't move (spec D.2's desync stall).
        foreach (double revs in new[] { C.RevsPerSecond * C.InRhythm * 0.6, -C.RevsPerSecond, 0 })
        {
            Crank(stop, revs, 1);
            double at = site.Progress;
            Crank(stop, revs, 3);
            Assert.Equal(at, site.Progress);
            Assert.True(site.OutOfRhythm);
            Assert.False(site.Turning);
        }

        // Alone, a hand going round turns nothing.
        Crank(stop, C.RevsPerSecond, 3, partner: false);
        Assert.False(site.Turning);
        Assert.False(site.OutOfRhythm);
    }

    [Fact]
    public void AHandOffTheCrankIsntOnIt()
    {
        var stop = AtTheCranks();
        var site = stop.Site;
        var hub = site.Handles[0];
        // Well off the crank's circle, and on the axle in the middle of it: neither is a hand on the crank.
        int? Holding(Double3 hand)
        {
            var s = stop.Crew[0];
            PlayerMotor.TakeHand(ref s, Hands(s, hand), stop.World.Hand);
            return stop.World.Run!.HandleInReach(s, stop.Train, stop.World.Hand);
        }
        Assert.Null(Holding(hub + site.Outward * 0.8));
        Assert.Null(Holding(hub + site.Axis * 0.1));
        // On it, anywhere round.
        Assert.Equal(0, Holding(hub - Double3.Up * site.CrankRadius));
        Assert.Equal(0, Holding(hub + (site.Outward + Double3.Up).Normalized * site.CrankRadius));
    }

    /// <summary>A crate stop, settled, and one of its heavy crates.</summary>
    static (FacilityTests.Stop Stop, Body Heavy) Crates()
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crates);
        stop.World.Hand = Tuning.Player.Hand;
        stop.Step(2, []);
        var heavy = stop.World.Bodies.All.Where(b => b.Kind == BodyKind.Heavy).ToList();
        Assert.InRange(heavy.Count, F.Crates.Heavy.Count[0], F.Crates.Heavy.Count[1]);
        Assert.Equal(stop.Site.HeavyStack.Length, heavy.Count);
        return (stop, heavy[0]);
    }

    /// <summary>Two players at either end of it, each facing it.</summary>
    static void EitherEnd(FacilityTests.Stop stop, Body heavy)
    {
        var at = heavy.Centre;
        var one = stop.OnTheGround(at + new Double3(0, 0, 1.0));
        one.Yaw = 0; // facing −Z, towards it
        var two = stop.OnTheGround(at - new Double3(0, 0, 1.0));
        two.Yaw = Math.PI;
        stop.Crew.Add(one);
        stop.Crew.Add(two);
        stop.Step(0.1, [default, default]);
    }

    static void Press(FacilityTests.Stop stop, int who, PlayerIntent? press = null)
    {
        var intents = stop.Crew.Select((_, i) => i == who ? press ?? Hold : default).ToArray();
        stop.Step(SimConstants.TickSeconds, intents);
        stop.Step(SimConstants.TickSeconds, new PlayerIntent[stop.Crew.Count]);
    }

    [Fact]
    public void AHeavyCrateTakesTwo()
    {
        var (stop, heavy) = Crates();
        EitherEnd(stop, heavy);
        var lay = heavy.Centre;

        // One takes an end: held, not lifted. Walking off with it leaves it lying there, and lets go of it.
        Press(stop, 0);
        Assert.Equal(1, heavy.Carrier);
        Assert.False(heavy.Lifted);
        Assert.True(stop.Crew[0].Has(PlayerFlags.Heavy));
        stop.Step(0.3, [new PlayerIntent { MoveZ = -1 }, default]);
        Assert.True((heavy.Centre - lay).Length < 0.05);

        // The other takes the other end: it comes up between them, and goes where they go, at the heavy pace.
        Press(stop, 1);
        Assert.Equal(2, heavy.Second);
        Assert.True(heavy.Lifted);
        stop.Step(0.2, [default, default]);
        Assert.True(heavy.Centre.Y > lay.Y + 0.3);
        var start = stop.Crew[0].Position;
        stop.Step(1, [new PlayerIntent { MoveX = 1, Buttons = PlayerButtons.Run }, new PlayerIntent { MoveX = -1, Buttons = PlayerButtons.Run }]);
        Assert.InRange((stop.Crew[0].Position - start).Length, Tuning.Player.CarryHeavy * 0.8, Tuning.Player.CarryHeavy * 1.05);
        Assert.All(stop.Crew, c => Assert.True(c.Has(PlayerFlags.Heavy)));
        var between = (stop.Crew[0].Position + stop.Crew[1].Position) * 0.5;
        Assert.True(((heavy.Centre - between) with { Y = 0 }).Length < 0.3);

        // Either lets go: it's down, for both.
        Press(stop, 1);
        Assert.Equal((-1, -1), (heavy.Carrier, heavy.Second));
        stop.Step(1, [default, default]);
        Assert.False(stop.Crew[0].Has(PlayerFlags.Heavy));

        // Walking off one from the other with it: past its span, it's down.
        Press(stop, 0);
        Press(stop, 1);
        Assert.True(heavy.Lifted);
        stop.Crew[1] = stop.Crew[1] with { Position = stop.Crew[1].Position - new Double3(0, 0, F.Crates.Heavy.Span + 1) };
        stop.Step(0.1, [default, default]);
        Assert.False(heavy.Lifted);
        Assert.Equal(-1, heavy.Carrier);
    }

    [Fact]
    public void CarriedInTogetherItsLoadedAsTwoCrates()
    {
        var (stop, heavy) = Crates();
        EitherEnd(stop, heavy);
        Press(stop, 0);
        Press(stop, 1);
        Assert.True(heavy.Lifted);
        // Both up in a cargo car with it between them (how they got there is CratesTests' business), and put down.
        var car = stop.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = stop.Train.Frames[car.Id].Shape.Interior!.Value;
        double floor = room.Min.Y + 0.05;
        stop.Crew[0] = stop.Crew[0] with { Parent = car.Id, Position = new Double3(0, floor, room.Centre.Z + 1.0), Surface = Surface.Deck };
        stop.Crew[1] = stop.Crew[1] with { Parent = car.Id, Position = new Double3(0, floor, room.Centre.Z - 1.0), Surface = Surface.Deck };
        stop.Step(0.2, [default, default]);
        Assert.Equal(car.Id, heavy.Parent);
        double before = car.Load;
        Press(stop, 0);
        stop.Step(F.Crates.SettleSeconds + 1.5, [default, default]);
        Assert.Equal(before + F.Crates.Heavy.LoadPerCrate, car.Load, 6);
        Assert.DoesNotContain(heavy, stop.World.Bodies.All);
    }

    [Fact]
    public void AHeadsetTakesItsEndWithBothHands()
    {
        var (stop, heavy) = Crates();
        EitherEnd(stop, heavy);
        var s = stop.Crew[0];
        var at = heavy.Centre;
        var right = at + new Double3(0.3, 0, 0.55);
        var left = at + new Double3(-0.3, 0, 0.55);
        // One hand on it and the other hanging at the side: that's not a grip on a heavy crate.
        Press(stop, 0, Hands(s, right, s.Position + new Double3(-0.7, 0.8, 0)));
        Assert.Equal(-1, heavy.Carrier);
        // Both hands on it.
        Press(stop, 0, Hands(s, right, left));
        Assert.Equal(1, heavy.Carrier);
    }

    [Fact]
    public void AClientSeesTheCrankAndBothCarriers()
    {
        var stop = AtTheCranks();
        // Turned a way round, then out of rhythm.
        Crank(stop, C.RevsPerSecond, 1.3);
        Crank(stop, C.RevsPerSecond * 0.2, 1);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var heavy = stop.World.Bodies.SpawnCargo(stop.Site.Capstan + Double3.Up, 0, F.Crates.Heavy.Radius);
        heavy.Carrier = 1;
        heavy.Second = 2;
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var mirrored = client.Run!.Sites[stop.Site.Index]!;
        Assert.Equal(stop.Site.Crank, mirrored.Crank, 4);
        Assert.True(mirrored.OutOfRhythm);
        var seen = client.Bodies.All.Single(b => b.Id == heavy.Id);
        Assert.Equal((BodyKind.Heavy, 1, 2), (seen.Kind, seen.Carrier, seen.Second));
    }
}
