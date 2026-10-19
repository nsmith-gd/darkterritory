using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 865 (queue #592; Otis's call 5 in note 861, the director: "Yes but not on solo nights"): a pin jumped clear at speed
/// takes two, one in the gap holding it down and one taking the slack off it (a brake wheel over the gap, or the throttle
/// shut); both on it, it goes home; either letting go, it parts, and the line says who; never on a night of one.
/// </summary>
public class TwoOnTheRunTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly LooseTuning L = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;
    const double Dt = SimConstants.TickSeconds;
    const int Gap = 2;
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    static World Night(LooseTuning? loose = null)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(200_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 5_000);
        train.Dynamics.Velocity = 16;
        var w = new World(train, Tuning.Combat) { NewsTuning = Tuning.Player.News };
        w.EnableBodies();
        w.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = loose ?? L };
        w.Names[1] = "Priya";
        w.Names[2] = "Dave";
        return w;
    }

    static void Jump(World w)
    {
        w.Train.Vehicles[Gap].Loose = Dt;
        w.Train.Vehicles[Gap].LooseCause = LooseCause.Jumped;
        // As the host tells it (World.PinLoosened): the report keeps what jumped it for when it parts.
        w.PinLoosened(Gap, LooseCause.Jumped, 16);
    }

    static PlayerState InTheGap() => new()
    {
        Parent = Gap,
        Surface = Surface.Coupler,
        Position = new Double3(T.Geometry.PlateX, T.Geometry.CouplerHeight, T.Geometry.CarLength / 2 + 0.7),
        Health = 100,
        Kit = Kit.Of([Tool.Shovel]),
    };

    /// <summary>On the roof at a car of the gap's brake wheel.</summary>
    static PlayerState AtTheWheel(TrainOnLine train)
    {
        foreach (int car in (ReadOnlySpan<int>)[Gap, train.VehicleBehind(Gap)])
            if (train.Frames[car].Shape.Interactables.FirstOrDefault(i => i.Kind == InteractableKind.Handbrake) is { } wheel)
                return PlayerMotor.SpawnOnRoof(train, car, wheel.Position.Z, Tuning.Player, wheel.Position.X) with { Health = 100 };
        throw new InvalidOperationException("no brake wheel at the gap");
    }

    /// <summary>A tick: each crewmate's hands as given (null, standing idle), the train held at speed under the controls.</summary>
    static void Tick(World w, ref PlayerState a, PlayerIntent? aDoes, ref PlayerState b, PlayerIntent? bDoes, TrainControls controls, double speed = 16)
    {
        w.BeginTick();
        w.CrewAct(ref a, aDoes ?? default, 1);
        w.CrewAct(ref b, bDoes ?? default, 2);
        w.Train.Dynamics.Velocity = speed;
        w.Step(controls);
    }

    static readonly TrainControls Coasting = new() { Reverser = 1 };
    static readonly TrainControls Steaming = new() { Throttle = 0.75, Reverser = 1 };

    [Fact]
    public void OneOnThePinAndOneOnTheBrakeWheelAndItGoesHome()
    {
        var w = Night();
        Jump(w);
        var pinner = InTheGap();
        var wheel = AtTheWheel(w.Train);
        Assert.Equal(Gap, Couplings.Within(pinner, w.Train, L));
        Assert.Equal(InteractableKind.Handbrake, CrewActions.Nearest(wheel, w.Train));
        for (int i = 0; i < (L.Jump.DriveSeconds + 0.5) * SimConstants.TickRate && w.Train.Vehicles[Gap].Loose > 0; i++)
            Tick(w, ref pinner, Use, ref wheel, Use, Steaming);
        Tick(w, ref pinner, null, ref wheel, null, Steaming);
        Assert.Equal(0, w.Train.Vehicles[Gap].Loose);
        Assert.Equal(1, w.Train.TrainRakes);
        Assert.Equal((0, 1, 0), w.JumpCount);
    }

    [Fact]
    public void OneOnThePinAndTheDriverWithTheThrottleShutAndItGoesHome()
    {
        var w = Night();
        Jump(w);
        var pinner = InTheGap();
        var driver = PlayerMotor.SpawnInCab(w.Train, Tuning.Player) with { Health = 100 };
        Assert.True(PlayerMotor.InCab(driver, w.Train));
        // The throttle shut a tick first (the slack's read off the controls as the train last stepped).
        Tick(w, ref pinner, null, ref driver, null, Coasting);
        for (int i = 0; i < (L.Jump.DriveSeconds + 0.5) * SimConstants.TickRate && w.Train.Vehicles[Gap].Loose > 0; i++)
            Tick(w, ref pinner, Use, ref driver, null, Coasting);
        Assert.Equal(0, w.Train.Vehicles[Gap].Loose);
        Assert.Equal(1, w.Train.TrainRakes);
    }

    [Fact]
    public void AloneOnThePinItNeverGoesHomeAndLeftItParts()
    {
        var w = Night();
        Jump(w);
        var pinner = InTheGap();
        var away = PlayerMotor.SpawnOnRoof(w.Train, 5, 0, Tuning.Player) with { Health = 100 };
        // The throttle open and nobody at a wheel: the hands are on it, and it doesn't move.
        for (int i = 0; i < 2 * L.Jump.DriveSeconds * SimConstants.TickRate; i++)
            Tick(w, ref pinner, Use, ref away, null, Steaming);
        Assert.True(w.Train.Vehicles[Gap].Loose > 0);
        Assert.False(w.Train.Vehicles[Gap].Pinned);
        Assert.Equal(0, pinner.ActionProgress);
        // Its clock ran meanwhile: it parts at partAfter, a jump's sooner than a pin worked loose.
        for (int i = 0; i < L.Jump.PartAfter * SimConstants.TickRate && w.Train.TrainRakes == 1; i++)
            Tick(w, ref pinner, null, ref away, null, Steaming);
        Assert.Equal(2, w.Train.TrainRakes);
        Assert.True(L.Jump.PartAfter < L.PartAfter);
        Tick(w, ref pinner, null, ref away, null, Steaming);
        var lost = Assert.Single(w.NewsLog, e => e.Kind == NewsKind.CarsLost);
        Assert.Equal($"CARS {Gap + 1}-6 LOST · THE PIN THE RAIL JOINT JUMPED", News.Line(w, lost));
    }

    [Fact]
    public void TheOneOnTheBrakeWheelLetsGoAndItPartsAndTheLineSaysWho()
    {
        var w = Night();
        Jump(w);
        var pinner = InTheGap();
        var wheel = AtTheWheel(w.Train);
        for (int i = 0; i < SimConstants.TickRate; i++)
            Tick(w, ref pinner, Use, ref wheel, Use, Steaming);
        Assert.True(w.Train.Vehicles[Gap].Pinned);
        Assert.True(pinner.ActionProgress > 0);
        // Dave lets go of the wheel, Priya still holding the pin: the slack snatches it out, at once.
        for (int i = 0; i < (L.Jump.LetGo + 0.2) * SimConstants.TickRate && w.Train.TrainRakes == 1; i++)
            Tick(w, ref pinner, Use, ref wheel, null, Steaming);
        Assert.Equal(2, w.Train.TrainRakes);
        Assert.Equal((0, 0, 1), w.JumpCount);
        Tick(w, ref pinner, null, ref wheel, null, Steaming);
        var lost = Assert.Single(w.NewsLog, e => e.Kind == NewsKind.CarsLost);
        Assert.Equal($"CARS {Gap + 1}-6 LOST · DAVE LET GO OF THE BRAKE WHEEL", News.Line(w, lost));
    }

    [Fact]
    public void ThePinnerLetsGoOrTheDriverOpensTheThrottleAndItParts()
    {
        foreach (bool driverOpens in (ReadOnlySpan<bool>)[false, true])
        {
            var w = Night();
            Jump(w);
            var pinner = InTheGap();
            var driver = PlayerMotor.SpawnInCab(w.Train, Tuning.Player) with { Health = 100 };
            Tick(w, ref pinner, null, ref driver, null, Coasting);
            for (int i = 0; i < SimConstants.TickRate; i++)
                Tick(w, ref pinner, Use, ref driver, null, Coasting);
            Assert.True(w.Train.Vehicles[Gap].Pinned);
            for (int i = 0; i < (L.Jump.LetGo + 0.2) * SimConstants.TickRate && w.Train.TrainRakes == 1; i++)
                Tick(w, ref pinner, driverOpens ? Use : null, ref driver, null, driverOpens ? Steaming : Coasting);
            Assert.Equal(2, w.Train.TrainRakes);
            Tick(w, ref pinner, null, ref driver, null, Coasting);
            var lost = Assert.Single(w.NewsLog, e => e.Kind == NewsKind.CarsLost);
            Assert.Equal(driverOpens ? $"CARS {Gap + 1}-6 LOST · DAVE OPENED THE THROTTLE" : $"CARS {Gap + 1}-6 LOST · PRIYA LET GO OF THE PIN",
                News.Line(w, lost));
        }
    }

    [Fact]
    public void ItNeverJumpsOnANightOfOneAndDoesWithTwo()
    {
        // Every running second a jump, from the first: alone, never; with a second crewmate alive, at once.
        var sure = L with { Jump = L.Jump with { PerMinute = 60 / Dt, After = 0, Spacing = 0 } };
        foreach (int crew in (ReadOnlySpan<int>)[1, 2])
        {
            var w = Night(sure);
            var a = PlayerMotor.SpawnOnRoof(w.Train, 1, 0, Tuning.Player) with { Health = 100 };
            var b = PlayerMotor.SpawnOnRoof(w.Train, 4, 0, Tuning.Player) with { Health = crew == 2 ? 100 : 0, Death = crew == 2 ? DeathCause.None : DeathCause.Eaten };
            for (int i = 0; i < 30 * SimConstants.TickRate; i++)
                Tick(w, ref a, null, ref b, null, Steaming);
            if (crew == 1)
                Assert.Equal(0, w.JumpCount.Jumped);
            else
                Assert.True(w.JumpCount.Jumped > 0);
            Assert.Equal(crew == 2, w.NewsLog.Any(e => e is { Kind: NewsKind.Loose, Cause: (byte)LooseCause.Jumped }));
        }
    }

    [Theory]
    [InlineData(2, 6, 3)]
    [InlineData(4, 6, 3)]
    [InlineData(2, 10, 8)]
    public void ABotCrewAnswersItAWalkerOnThePinAndTheDriverEasingOff(int bots, int cars, int gap)
    {
        // A pin jumps behind car 3 at 40 s on the open line: a hand goes down into the gap and holds it, the driver shuts the
        // throttle for them, and it goes home before it parts. (The night's own jumps off: this one's the test's.)
        double? cameAt = null, homeAt = null;
        var quiet = L with { Jump = L.Jump with { Enabled = false } };
        var report = CrewOfTwoTests.Night(CrewOfTwoTests.OpenLine.Spec, cars, 40 + L.Jump.PartAfter + 5, null, start: CrewOfTwoTests.OpenLine.Start, bots: bots,
            upkeep: new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = quiet },
            each: w =>
            {
                if (cameAt is null && w.ElapsedSeconds >= 40 && w.Train.Dynamics.Speed > 5)
                {
                    w.Train.Vehicles[gap].Loose = Dt;
                    w.Train.Vehicles[gap].LooseCause = LooseCause.Jumped;
                    cameAt = w.ElapsedSeconds;
                }
                else if (cameAt is not null && homeAt is null && w.Train.Vehicles[gap].Loose == 0 && w.JumpCount.Home > 0)
                    homeAt = w.ElapsedSeconds;
            });
        Assert.NotNull(cameAt);
        Assert.True(homeAt is not null, $"jumped at {cameAt:0} s behind car {gap}, never home ({report.Upkeep?.Parted} parted, {report.Deaths} died)");
        Assert.Equal(0, report.Upkeep?.Parted ?? 0);
    }

    /// <summary>
    /// An hour at speed with two aboard: the jumps come about as often as perMinute and spacing say, at more than one gap.
    /// (Its draws once came from a readonly struct field, a fresh copy each tick: the first number forever, and none jumped.)
    /// </summary>
    [Fact]
    public void ItComesAsOftenAsItsRateSaysAtGapsOfItsOwnPicking()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(400_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 10, 1)), line, 5_000) { Loose = L };
        var pins = new Couplings(L, 786582);
        var crew = new List<(int, PlayerState, PlayerIntent)> { (1, new PlayerState { Health = 100 }, default), (2, new PlayerState { Health = 100 }, default) };
        var gaps = new HashSet<int>();
        for (int t = 0; t < 3600 * SimConstants.TickRate; t++)
        {
            train.Dynamics.Velocity = 16;
            train.Step(Dt, new TrainControls { Reverser = 1 });
            pins.Step(train, 2, false, false, crew);
            if (pins.Loosened is { Cause: LooseCause.Jumped } j)
                gaps.Add(j.Car);
            // Tightened as it comes, so every gap's always there to jump (and none parts the train).
            foreach (var v in train.Vehicles)
                v.Loose = 0;
        }
        // The mean wait is spacing plus a perMinute draw's: (3600 - after) over that, give or take.
        double expected = (3600 - L.Jump.After) / (L.Jump.Spacing + 60 / L.Jump.PerMinute);
        Assert.InRange(pins.Jumped, expected * 0.5, expected * 1.6);
        Assert.True(gaps.Count > 1, $"every jump at car {string.Join(",", gaps)}");
    }

    /// <summary>Systems spec B.20's table.</summary>
    [Fact]
    public void MatchesB20()
    {
        var j = L.Jump;
        Assert.True(j.Enabled);
        Assert.Equal(2, j.MinCrew);
        Assert.Equal(12, j.Above);
        Assert.Equal(0.25, j.PerMinute);
        Assert.Equal(180, j.After);
        Assert.Equal(300, j.Spacing);
        Assert.Equal(4, j.DriveSeconds);
        Assert.Equal(0.3, j.LetGo);
        Assert.Equal(60, j.PartAfter);
        Assert.Equal(90, L.PartAfter);
    }

    [Fact]
    public void TheJumpIsToldWithTheGapTheJointAndThatItTakesTwo()
    {
        var w = Night();
        var told = new NewsEvent(1, 0, NewsKind.Loose, [3], (byte)LooseCause.Jumped, 0, -1, 64 / 3.6);
        Assert.Equal("PIN JUMPED BEHIND CAR 3 · A RAIL JOINT AT 64 KM/H · TWO TO HOLD IT", News.Line(w, told));
    }
}
