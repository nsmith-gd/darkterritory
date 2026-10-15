using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Sound cues in the world (queue #466, ARCHITECTURE §8 note 732; the director, 14 Oct 2026: "I do like the idea of using
/// sound-only cues in the design of the environment"), each before what it tells of (App. A.1): boards creaking where a house
/// creature waits a few seconds before it's on you, the Householder shifting in its chair before it rises, a coupling's pin
/// jumped by something the crew can name and on the car's record, a far bell for what's been lost, a dog at the lamps' edge.
/// </summary>
[Collection(nameof(LineGenTests))]
public class SoundCueTests
{
    static readonly Route.Route Frontier = Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 6);
    static readonly EnemyTuning Quiet = Tuning.Enemies with
    {
        Director = Tuning.Enemies.Director with { Roster = ["lodger", "householder", "hollowHouse", "hanger"] },
        Dwellings = Tuning.Enemies.Dwellings with { Enabled = false },
    };
    static readonly CreakTuning Creak = Tuning.Enemies.SoundCues.Creak;
    /// <summary>A walk (m/s): a crewmate going into a house carefully.</summary>
    const double Walking = 1.5;

    static (Night Night, StopWalls.OpenHouse House) AtAHouse()
    {
        var f = Frontier.Features.First(x => x.Stop is { } s && s.Buildings.Any(b => b.Open));
        var n = new Night(4, speed: 0, route: Frontier, enemies: Quiet, front: f.Start + 60);
        var walls = StopWalls.Of(Frontier, n.Train.Line, tuning: Tuning.Run.Walls);
        n.Train.Walls = walls;
        int feature = Frontier.Features.ToList().IndexOf(f);
        var near = n.Train.Line.Sample(f.Start).Position;
        var house = walls.OpenHouses.Where(h => h.Feature == feature && walls.HouseDoors.Count(d => d.House == h.Index) == 1)
            .OrderBy(h => HouseWays.Flat(h.Middle - near)).First();
        return (n, house);
    }

    static HouseDoor DoorOf(Night n, StopWalls.OpenHouse h) => n.Train.Walls!.HouseDoors.Single(d => d.House == h.Index);
    static Double3 InsideDoor(Night n, StopWalls.OpenHouse h, double inBy = 1.2) => DoorOf(n, h).At - DoorOf(n, h).Out * inBy;
    static Double3 OutsideDoor(Night n, StopWalls.OpenHouse h, double outBy = 3) => DoorOf(n, h).At + DoorOf(n, h).Out * outBy;

    static PlayerState At(Night n, Double3 at)
    {
        double hint = n.Train.Dynamics.Distance;
        return PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) }, n.Train.Line, hint, Tuning.Player);
    }

    /// <summary>The cues made so far tonight, each once, with the tick it was made on.</summary>
    sealed class Heard
    {
        readonly HashSet<int> _ids = [];
        public readonly List<WorldCue> All = [];
        public void Take(World w)
        {
            foreach (var c in w.Cues)
                if (_ids.Add(c.Id))
                    All.Add(c);
        }
        public List<WorldCue> Of(CueKind kind, int subject) => [.. All.Where(c => c.Kind == kind && c.Subject == subject)];
    }

    static void Tick(Night n, Heard heard)
    {
        n.Run(SimConstants.TickSeconds);
        n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        heard.Take(n.World);
    }

    /// <summary>
    /// Crewmate 1 walked through <paramref name="way"/> at <paramref name="speed"/>, a tick at a time, till
    /// <paramref name="until"/> holds (then the tick it held on) or the way's done (null).
    /// </summary>
    static uint? Walk(Night n, Heard heard, Double3[] way, Func<bool> until, double speed = Walking)
    {
        for (int leg = 0; leg + 1 < way.Length; leg++)
        {
            var (from, to) = (way[leg], way[leg + 1]);
            double length = HouseWays.Flat(to - from);
            for (double gone = 0; gone < length; gone += speed * SimConstants.TickSeconds)
            {
                n.Crew[1] = At(n, from + (to - from) * (gone / Math.Max(1e-6, length)));
                Tick(n, heard);
                if (until())
                    return n.World.Tick;
            }
        }
        return null;
    }

    /// <summary>Crewmate 1 stood at <paramref name="at"/> up to <paramref name="seconds"/>, till <paramref name="until"/> holds.</summary>
    static uint? Stand(Night n, Heard heard, Double3 at, double seconds, Func<bool> until)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.Crew[1] = At(n, at);
            Tick(n, heard);
            if (until())
                return n.World.Tick;
        }
        return null;
    }

    // ---- What waits in a house ----

    [Fact]
    public void TheLodgersBoardsCreakAFewSecondsBeforeItShrieksQuickerAsYouComeAndNotForNobody()
    {
        var (n, house) = AtAHouse();
        var heard = new Heard();
        var lodger = n.World.AddEnemy(id => Lodger.In(id, house, n.Train.Walls!, Tuning.Enemies.Lodger));
        // Nobody near: nothing creaks.
        n.Crew[1] = At(n, OutsideDoor(n, house, 30));
        for (int i = 0; i < 8 * SimConstants.TickRate; i++)
            Tick(n, heard);
        Assert.Empty(heard.Of(CueKind.Creak, lodger.Id));
        // Walked in to its corner: it creaks well before it shrieks, and the creaks come quicker as they near it.
        var shriek = Walk(n, heard, [OutsideDoor(n, house, 10), InsideDoor(n, house), lodger.Hide], () => lodger.Mode == LodgerMode.Shriek);
        Assert.NotNull(shriek);
        var creaks = heard.Of(CueKind.Creak, lodger.Id);
        Assert.True(creaks.Count >= 2, $"{creaks.Count} creaks");
        double before = (shriek!.Value - creaks[0].Tick) * SimConstants.TickSeconds;
        Assert.True(before >= 1.5, $"first creak {before:0.00} s before the shriek");
        Assert.All(creaks, c => Assert.Equal((byte)EnemyKind.Lodger, c.Of));
        Assert.True(HouseWays.Flat(creaks[0].At - lodger.Hide) < 0.1);
        var gaps = creaks.Zip(creaks.Skip(1), (a, b) => b.Tick - a.Tick).ToList();
        Assert.True(gaps[^1] < gaps[0] || gaps.Count == 1, $"gaps {string.Join(", ", gaps)}");
        Assert.True(creaks[^1].Strength > creaks[0].Strength);
    }

    [Fact]
    public void TheHangersRoofCreaksBeforeAStrandsTouchedAndTheCreaksAreOverhead()
    {
        var (n, house) = AtAHouse();
        var heard = new Heard();
        var hanger = n.World.AddEnemy(id => Hanger.In(id, house, Tuning.Enemies.Hanger));
        var grab = Walk(n, heard, [OutsideDoor(n, house, 10), InsideDoor(n, house), hanger.StrandsAt[0]], () => hanger.Mode == HangerMode.Grab)
            ?? Stand(n, heard, hanger.StrandsAt[0], 3, () => hanger.Mode == HangerMode.Grab);
        Assert.NotNull(grab);
        var creaks = heard.Of(CueKind.Creak, hanger.Id);
        Assert.NotEmpty(creaks);
        Assert.True((grab!.Value - creaks[0].Tick) * SimConstants.TickSeconds >= 1.5);
        Assert.All(creaks, c => Assert.True(c.At.Y > hanger.StrandsAt[0].Y + 1.5, "up in the roof"));
    }

    static (Night Night, StopWalls.OpenHouse House, Householder It, Body Thing, Heard Heard) AtTheTable()
    {
        var (n, house) = AtAHouse();
        var heard = new Heard();
        n.World.EnableBodies();
        var it = n.World.AddEnemy(id => Householder.In(id, house, n.Train.Walls!, Tuning.Enemies.Householder));
        var thing = n.World.Bodies.SpawnItem(house.Middle - (DoorOf(n, house).At - house.Middle).Normalized * 1.0, n.Train.Dynamics.Distance, BodyKind.RepairKit);
        n.Crew[1] = At(n, InsideDoor(n, house));
        for (int i = 0; i < SimConstants.TickRate; i++)
            Tick(n, heard);
        Assert.Contains(thing.Id, it.Mine);
        return (n, house, it, thing, heard);
    }

    [Fact]
    public void OneOfItsThingsLiftedUnpaidTheHouseholderShiftsInItsChairThenCreaksAsItNearsTheDoorBeforeItRises()
    {
        var (n, house, it, thing, heard) = AtTheTable();
        // In the room, between its table and the door, its thing in hand.
        var by = (house.Middle + InsideDoor(n, house)) * 0.5;
        n.Crew[1] = At(n, by);
        thing.Carrier = 1;
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            Tick(n, heard);
        var shift = Assert.Single(heard.Of(CueKind.Shift, it.Id));
        Assert.Contains(it.Mode, new[] { HouseholderMode.Sit, HouseholderMode.Watch });
        // Carried to the door and out: its chair creaks on the way, and only out of the door does it rise.
        var rise = Walk(n, heard, [by, InsideDoor(n, house), OutsideDoor(n, house, 4)], () => it.Mode == HouseholderMode.Rise);
        Assert.NotNull(rise);
        Assert.True((rise!.Value - shift.Tick) * SimConstants.TickSeconds >= 1.5);
        Assert.Contains(heard.Of(CueKind.Creak, it.Id), c => c.Tick > shift.Tick && c.Tick <= rise.Value);
        Assert.Single(heard.Of(CueKind.Shift, it.Id));
    }

    [Fact]
    public void PaidForTheHouseholderSitsStillAndAGuestEmptyHandedHearsNothing()
    {
        var (n, house, it, thing, heard) = AtTheTable();
        for (int i = 0; i < 6 * SimConstants.TickRate; i++)
            Tick(n, heard);
        Assert.Empty(heard.Of(CueKind.Shift, it.Id));
        Assert.Empty(heard.Of(CueKind.Creak, it.Id));
        n.World.Bodies.SpawnItem(it.Table, n.Train.Dynamics.Distance, BodyKind.Toy);
        for (int i = 0; i < SimConstants.TickRate; i++)
            Tick(n, heard);
        Assert.Equal(1, it.Credit);
        var by = (house.Middle + InsideDoor(n, house)) * 0.5;
        n.Crew[1] = At(n, by);
        thing.Carrier = 1;
        Walk(n, heard, [by, InsideDoor(n, house), OutsideDoor(n, house, 4)], () => false);
        Assert.Empty(heard.Of(CueKind.Shift, it.Id));
        Assert.Empty(heard.Of(CueKind.Creak, it.Id));
    }

    // ---- What's about to go ----

    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(DataFile.FindContentRoot(), SightTuning.File));
    static readonly LooseTuning L = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;

    /// <summary>A train held at a speed round a 300 m curve and on, the couplings' upkeep on (as LinesideTests' ride).</summary>
    static World Round(double speed)
    {
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(800), new TrackSegment(300, 300), new TrackSegment(3000)]),
            [], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), 600);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableLineside(S, route);
        world.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = L };
        while (train.Dynamics.RearDistance < 1300 && !world.Derailed)
        {
            train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
        }
        return world;
    }

    [Fact]
    public void ABendTakenTooFastJumpsOnePinAndWhatDidItIsOnTheCarsRecordForEveryClient()
    {
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(800), new TrackSegment(300, 300), new TrackSegment(3000)]),
            [], new RouteWeather(0.01, false, 0, 0), 3600);
        double limit = Lineside.Boards(S, route).Single(s => s.Kind == SignKind.SpeedLimit).Limit;
        // At its board: the cars ride it, nothing jumps.
        Assert.DoesNotContain(Round(limit - 0.5).Train.Vehicles, v => v.Loose > 0);
        // Over it, lurching: one pin jumps, once for the bend, and the car says what jumped it.
        var w = Round(limit + S.LurchOver + 0.5);
        var loose = Assert.Single(w.Train.Vehicles, v => v.Loose > 0);
        Assert.Equal(LooseCause.Bend, loose.LooseCause);
        Assert.Equal(LooseCause.Bend, Assert.Single(w.Loosenings).Cause);
        // On the wire: a client's car has the cause too, so its moment is heard as the bend's.
        var controls = default(TrainControls);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), w.Train.Line, 600));
        WorldRecords.Apply(WorldRecords.Capture(w, controls, []), client, ref controls, []);
        Assert.Equal(LooseCause.Bend, client.Train.Vehicles[loose.Id].LooseCause);
        Assert.True(client.Train.Vehicles[loose.Id].Loose > 0);
    }

    [Fact]
    public void APinsKnockRunsFromTheMomentItJumpedTillItPartsAndTheCauseGoesWhenItsTight()
    {
        // A hard brake from 15 m/s: the pin jumps, the car's record says the run-in, and it knocks for partAfter of running
        // before it parts; tightened, the cause is gone with the knock.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(60_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 5_000);
        train.Dynamics.Velocity = 15;
        var w = new World(train, Tuning.Combat);
        w.EnableBodies();
        w.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = L };
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            w.Step(new TrainControls { Brake = 1, Reverser = 1 });
        var loose = Assert.Single(train.Vehicles, v => v.Loose > 0);
        Assert.Equal(LooseCause.RunIn, loose.LooseCause);
        double knocking = loose.Loose;
        while (loose.Loose > 0 && knocking < L.PartAfter + 5)
        {
            train.Dynamics.Velocity = 10;
            w.Step(new TrainControls { Reverser = 1 });
            knocking += SimConstants.TickSeconds;
        }
        Assert.InRange(knocking, L.PartAfter - 0.1, L.PartAfter + 0.1);
        w.Step(new TrainControls { Reverser = 1 });
        Assert.Equal(LooseCause.None, loose.LooseCause);
    }

    // ---- What's been hit ----

    [Fact]
    public void ACutLeftPastWhereTheNightHasItRingsAFarBellOnce()
    {
        var n = new Night(6, speed: 0, enemies: Quiet);
        var heard = new Heard();
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        n.Run(0.2);
        Assert.True(n.Train.Uncouple(3));
        var cut = n.Train.RakeOf(4);
        var middle = n.Train.Frames[cut.Consist.Vehicles[cut.Consist.Vehicles.Count / 2].Id].Origin;
        n.Train.Dynamics.Velocity = 20;
        for (int i = 0; i < 40 * SimConstants.TickRate; i++)
        {
            n.Run(SimConstants.TickSeconds);
            heard.Take(n.World);
        }
        double away = (PlayerMotor.WorldPosition(n.Crew[1], n.Train) - middle).Length;
        Assert.True(away > 400, $"{away:0} m away");
        var bell = Assert.Single(heard.All, c => c.Kind == CueKind.Bell);
        Assert.Equal((byte)BellFor.CutLost, bell.Of);
        Assert.True(HouseWays.Flat(bell.At - middle) < 25, $"{HouseWays.Flat(bell.At - middle):0} m off");
    }

    [Fact]
    public void ACarTakenWithNobodyByItRingsAFarBellAndOneTakenUnderTheCrewsNoseDoesnt()
    {
        var n = new Night(10, speed: 0, enemies: Quiet);
        var heard = new Heard();
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        n.Run(0.2);
        int last = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        Assert.True((PlayerMotor.WorldPosition(n.Crew[1], n.Train) - n.Train.Frames[last].Origin).Length > Tuning.Enemies.SoundCues.Bell.UnseenBeyond);
        n.Train.Vehicles[last].Taken = true;
        n.Run(0.2);
        heard.Take(n.World);
        var bell = Assert.Single(heard.All, c => c.Kind == CueKind.Bell);
        Assert.Equal((byte)BellFor.CarsTaken, bell.Of);
        Assert.Equal(last, bell.Subject);
        // The car behind the cab, taken with the crew right by it: they know.
        n.Train.Vehicles[1].Taken = true;
        n.Run(0.2);
        heard.Take(n.World);
        Assert.Single(heard.All, c => c.Kind == CueKind.Bell);
    }

    // ---- Something at the lamps' edge ----

    [Fact]
    public void ADogBarksFarOffPastSomethingMovingAtTheLampsEdgeAndNotAtWhatStandsStillOrIsFarOff()
    {
        var n = new Night(4, speed: 0, enemies: Quiet);
        var heard = new Heard();
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        var mid = n.Train.Frames[2];
        var side = mid.Right;
        // Three things abeam the train: one moving at the edge, one stood still there, one moving far out.
        Lodger Thing(double out_) => n.World.AddEnemy(id => new Lodger(id) { Attached = Enemy.Loose, Local = mid.Origin + side * out_, Extra = -1 });
        var moving = Thing(40);
        var still = Thing(-40);
        var far = Thing(150);
        var b = Tuning.Enemies.SoundCues.Bark;
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
        {
            moving.Local += mid.Back * (2.0 * SimConstants.TickSeconds);
            far.Local += mid.Back * (2.0 * SimConstants.TickSeconds);
            n.Run(SimConstants.TickSeconds);
            heard.Take(n.World);
        }
        var bark = Assert.Single(heard.All, c => c.Kind == CueKind.Bark);
        Assert.Equal(moving.Id, bark.Subject);
        // Out past it, further from the train.
        var p = moving.Local;
        Assert.InRange(HouseWays.Flat(bark.At - p), b.Beyond - 1, b.Beyond + 1);
        Assert.True(HouseWays.Flat(bark.At - mid.Origin) > HouseWays.Flat(p - mid.Origin) + b.Beyond * 0.8);
        Assert.Empty(heard.Of(CueKind.Bark, still.Id));
        Assert.Empty(heard.Of(CueKind.Bark, far.Id));
    }

    [Fact]
    public void ACueIsOnTheWireForEveryClientForAWhile()
    {
        var n = new Night(10, speed: 0, enemies: Quiet);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        n.Run(0.2);
        n.Train.Vehicles[n.Train.Dynamics.Consist.Vehicles[^1].Id].Taken = true;
        n.Run(SimConstants.TickSeconds);
        var bell = Assert.Single(n.World.Cues);
        var controls = default(TrainControls);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 10, 1)), n.Train.Line, 2_000));
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        var seen = Assert.Single(client.Cues);
        Assert.Equal(bell.Id, seen.Id);
        Assert.Equal(bell.Kind, seen.Kind);
        Assert.Equal(bell.Of, seen.Of);
        Assert.Equal(bell.Subject, seen.Subject);
        Assert.True((seen.At - bell.At).Length < 0.01);
        // Kept keepSeconds, then gone.
        n.Run(Tuning.Enemies.SoundCues.KeepSeconds + 0.2);
        Assert.Empty(n.World.Cues);
    }
}
