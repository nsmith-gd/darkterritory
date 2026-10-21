using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 936 (the director's playtest of 20 Oct, one bot crewmate, the driver: "I wish I could just click middle mouse button
/// and like the bot would get what I want just based on what I'm pointing at ... oh, the spur? ... an abandoned house? Sure,
/// I'll go search it. You want me to load that cargo into the car that's open ... Sure, let me go operate the crane"): with
/// the driver the crew's only bot, a ping on each of those sends it out to do it, says it's on it, and holds the train.
/// </summary>
public partial class StopCrewTests
{
    /// <summary>Who's a bot and what it is, as the crew say each tick (BotCrew.Think); the driver knows its own id.</summary>
    static void Roles(Night night)
    {
        for (int i = 0; i < night.Bots.Count; i++)
        {
            night.Calls.Bot(i + 1);
            night.Calls.Role(i + 1, night.Bots[i] is ConductorBot ? BotRole.Driver : BotRole.Walker);
        }
        night.Driver.Me = 1;
    }

    /// <summary>Every bot takes what pings it can, as BotCrew.Think has them do each tick.</summary>
    static void Take(Night night)
    {
        var crew = night.Crew.Select((c, i) => (i + 1, c)).ToList();
        night.Driver.Crew = crew;
        for (int i = 0; i < night.Bots.Count; i++)
            night.Calls.Pings(night.World, i + 1, night.Crew[i], crew);
    }

    /// <summary>Someone playing, standing at <paramref name="from"/> on the ground, looking at <paramref name="at"/>.</summary>
    static PlayerState LookingFrom(TrainOnLine train, Double3 from, Double3 at)
    {
        var eye = PlayerMotor.SpawnOnGround(from, train.Line, train.Dynamics.Distance, P);
        var to = at - (eye.Position + Double3.Up * train.Dynamics.Tuning.Pick.EyeHeight);
        to *= 1 / to.Length;
        return eye with { Yaw = Math.Atan2(-to.X, -to.Z), Pitch = Math.Asin(to.Y) };
    }

    /// <summary>The lone driver at the end of the stop's spur, at its loading.</summary>
    static void ToTheLoading(Night night)
    {
        night.Until(() => night.Driver.Stops!.Doing == StopDriver.Leg.Loading && night.World.Run!.CurrentSite is not null, 900);
        Assert.Equal(StopDriver.Leg.Loading, night.Driver.Stops!.Doing);
    }

    [Fact]
    public void ASpursPointsPingedAreThrownByTheDriverAlone()
    {
        // "I wish there was a way I could just send this guy out to go and throw the spur for me": the train standing 60 m short
        // of a stop the driver alone wouldn't make (a winch wants two), its spur's lever pinged. The driver stands the train,
        // gets down, throws it over, and says so.
        var night = new Night(cars: 4, walkers: 0, hands: 0, from: 60);
        var train = night.Train;
        var world = night.World;
        var spur = night.Site.Spur;
        var lever = world.Switches!.LeverAt(train.Line, spur);
        Assert.False(train.Diverging(spur));
        // Looking at it from beside the engine: the points, the spur's.
        var beside = lever + (train.Frames[0].Origin - lever).Normalized * 12 - Double3.Up * 0.9;
        var ping = Pings.Resolve(world, LookingFrom(train, beside, lever), P.Pings);
        Assert.Equal((PingKind.Points, spur), (ping?.Kind, ping?.Target));
        world.Pings.Add(new PingEvent(7, world.Tick, 99, PingKind.Points, spur, lever));
        Roles(night);
        bool answered = false, wentOut = false;
        night.Until(() => train.Diverging(spur), 150, () =>
        {
            Take(night);
            answered |= night.Calls.Answering(1) == 7;
            wentOut |= night.Driver.OutOnPing && night.Crew[0].Parent == PlayerState.World;
        });
        Assert.True(answered, "the driver never took the ping");
        Assert.True(wentOut, "the driver never got down");
        Assert.True(train.Diverging(spur), $"the points weren't thrown; the driver was {night.Driver.PingStep}");
        // Done: the order's let go, and the driver's back up into the cab.
        night.Until(() => PlayerMotor.InCab(night.Crew[0], train), 120, () => Take(night));
        Assert.Null(night.Calls.OrderOf(1));
        Assert.True(PlayerMotor.InCab(night.Crew[0], train), $"the driver's still out: {night.Crew[0].Surface} on {night.Crew[0].Parent}");
    }

    [Fact]
    public void ATownPingedIsSearchedByTheDriverAloneAndTheTrainWaitsForIt()
    {
        // "Oh, an abandoned house? Sure, I'll go search it": the lone driver at a crates stop's loading, a house of its village
        // pinged. The driver goes out searching it, and the train's still there till it's back.
        var night = new Night(cars: 6, walkers: 0, hands: 0, ids: true, loot: true, modules: ModuleKind.Crates);
        var train = night.Train;
        var world = night.World;
        var run = world.Run!;
        ToTheLoading(night);
        // (The stop the lone driver makes: the first with crates and a village it can work, the train being short.)
        var site = run.CurrentSite!;
        int k = run.StopOf(site.Feature).Stop;
        var beside = train.Frames[2].ToWorld(new Double3(site.Side * (train.Frames[2].Shape.Bounds.Max.X + 3), 0, 0));
        var house = train.Walls!.OpenHouses.Where(h => h.Feature == run.StopOf(site.Feature).Feature).OrderBy(h => (h.Middle - beside).Length).First();
        var ping = Pings.Resolve(world, LookingFrom(train, beside, house.Middle + Double3.Up * 2), P.Pings);
        Assert.Equal((PingKind.Town, k), (ping?.Kind, ping?.Target));
        world.Pings.Add(new PingEvent(8, world.Tick, 99, PingKind.Town, k, ping!.Value.At));
        Roles(night);
        int searched = run.HidingSpots.Count(h => h.Stop == k && run.Searched(k, h.Container.Index));
        var doing = new HashSet<string>();
        bool taken = false;
        night.Until(() => run.HidingSpots.Count(h => h.Stop == k && run.Searched(k, h.Container.Index)) > searched, 180, () =>
        {
            Take(night);
            taken |= night.Calls.OrderOf(1) == (PingKind.Town, k);
            if (night.Driver.PingStep is { Length: > 0 } step)
                doing.Add(step);
        });
        var me0 = PlayerMotor.WorldPosition(night.Crew[0], train);
        string trace = $"the driver did {string.Join(", ", doing)}; nearest spot {run.HidingSpots.Where(h => h.Stop == k).Min(h => ((h.At - me0) with { Y = 0 }).Length):0} m, dawn in {run.DawnIn:0}";
        Assert.True(taken, "the driver never took the ping");
        Assert.True(run.HidingSpots.Count(h => h.Stop == k && run.Searched(k, h.Container.Index)) > searched, trace);
        Assert.Contains(doing, d => d.StartsWith("searching the", StringComparison.Ordinal));
        Assert.Equal(0, run.Departures);
        Assert.True(night.Crew[0].Alive, $"died of {night.Crew[0].Death}");
    }

    [Fact]
    public void AFindPingedIsFetchedAboardByTheDriverAlone()
    {
        // "If I point at loot": the lone driver at the loading, a find lying out beside the train pinged; it fetches it aboard.
        var night = new Night(cars: 6, walkers: 0, hands: 0, ids: true, loot: true, modules: ModuleKind.Crates);
        var train = night.Train;
        var world = night.World;
        ToTheLoading(night);
        var frame = train.Frames[2];
        var at = frame.ToWorld(new Double3(world.Run!.CurrentSite!.Side * (frame.Shape.Bounds.Max.X + 5), 0.3, 0));
        var find = world.Bodies.SpawnLoot(at, train.Dynamics.Distance, -1, 0.2);
        world.Pings.Add(new PingEvent(9, world.Tick, 99, PingKind.Loot, find.Id, at));
        Roles(night);
        var doing = new HashSet<string>();
        night.Until(() => find.Carrier < 0 && (find.Stowed || find.Parent != PlayerState.World), 240, () =>
        {
            Take(night);
            if (night.Driver.PingStep is { Length: > 0 } step)
                doing.Add(step);
        });
        Assert.True(find.Carrier < 0 && (find.Stowed || find.Parent != PlayerState.World),
            $"the find's still out (carrier {find.Carrier}, on {find.Parent}); the driver did {string.Join(", ", doing)}");
        Assert.Contains("to the find it was pointed at", doing);
        Assert.Equal(0, world.Run!.Departures);
    }

    [Fact]
    public void ACranePingedIsWorkedByTheDriverAlone()
    {
        // "Sure, let me go operate the crane": the lone driver at a foundry's loading (its crates in; the crane wants two and
        // there's one), the crane pinged. The driver takes the crane's controls (someone playing rigs).
        var night = new Night(cars: 3, walkers: 0, hands: 0, ids: true, modules: [ModuleKind.Crane, ModuleKind.Crates]);
        var world = night.World;
        var crane = Assert.IsType<Crane>(night.Site.Crane);
        ToTheLoading(night);
        int i = night.Site.Cranes.ToList().IndexOf(crane);
        world.Pings.Add(new PingEvent(10, world.Tick, 99, PingKind.Crane, i, crane.Controls + Double3.Up));
        Roles(night);
        var doing = new HashSet<string>();
        night.Until(() => night.Crew[0].Has(PlayerFlags.Operating), 240, () =>
        {
            Take(night);
            if (night.Driver.PingStep is { Length: > 0 } step)
                doing.Add(step);
        });
        Assert.Equal(StopJob.Winch0, night.Calls.CranePart(1));
        Assert.True(night.Crew[0].Has(PlayerFlags.Operating), $"the driver's not at the crane; it did {string.Join(", ", doing)}");
        Assert.Equal(0, world.Run!.Departures);
    }

    [Fact]
    public void ACarPingedForTheCargoIsLoadedByTheDriverAlone()
    {
        // "You want me to load that cargo into the car that's open ... Sure": the lone driver at a crates stop's loading, the
        // last cargo car pinged. The stop's crates go into it.
        var night = new Night(cars: 3, walkers: 0, hands: 0, ids: true, modules: ModuleKind.Crates);
        var train = night.Train;
        var world = night.World;
        ToTheLoading(night);
        var car = train.Dynamics.Consist.Vehicles.Last(v => v.Kind == VehicleKind.Cargo && v.Load < 1 - 1e-6 && StopHand.SideDoor(train.Frames[v.Id].Shape, world.Run!.CurrentSite!.Side) is not null);
        double was = car.Load;
        world.Pings.Add(new PingEvent(11, world.Tick, 99, PingKind.Cargo, car.Id, train.Frames[car.Id].Origin));
        Roles(night);
        night.Until(() => car.Load > was + 1e-6, 300, () => Take(night));
        Assert.True(car.Load > was + 1e-6, $"nothing went into car {car.Id}; the driver did {night.Driver.PingStep}");
    }

    [Fact]
    public void ACarFirePingedIsTakenByTheDriverAloneWhoGoesToIt()
    {
        // "If I point at ... a car on fire": the train standing, car 2 alight, pinged; the lone driver goes to it.
        var night = new Night(cars: 4, walkers: 0, hands: 0, creatures: true);
        var train = night.Train;
        var world = night.World;
        world.AddEnemy(id => CarFire.In(id, train, 2, 2, Tuning.Enemies.CarFire, FireCause.Set));
        world.Pings.Add(new PingEvent(12, world.Tick, 99, PingKind.Fire, 2, train.Frames[2].Origin));
        Roles(night);
        double fastest = 0;
        night.Until(() => night.Crew[0].Parent == 2, 90, () =>
        {
            Take(night);
            fastest = Math.Max(fastest, train.Dynamics.Speed);
        });
        Assert.Equal((PingKind.Fire, 2), night.Calls.OrderOf(1));
        Assert.Equal(2, night.Crew[0].Parent);
        Assert.True(fastest < 1, $"the train ran on at {fastest:0.0} m/s");
    }

    [Fact]
    public void ATownPingedHoldsTheTrainAtTheStopTillItsSearched()
    {
        // "trying to find a way to tell them to go search the house with me. And of course, we're just moving right away": a
        // crew with walkers, a house pinged at the loading. The driver doesn't leave while the walker's at it.
        var night = new Night(cars: 8, walkers: 3, crateHands: true, ids: true, loot: true, modules: ModuleKind.Crates);
        var world = night.World;
        var run = world.Run!;
        int k = run.Stops.ToList().IndexOf(night.Site.Feature);
        var hands = night.Bots.OfType<RoofWalkerBot>().Select(b => b.Job).OfType<StopHand>().ToList();
        night.Until(() => hands.Any(h => h.Doing.Contains("crate")), 900);
        var house = night.Train.Walls!.OpenHouses.First(h => h.Feature == run.StopOf(night.Site.Feature).Feature);
        world.Pings.Add(new PingEvent(13, world.Tick, 99, PingKind.Town, k, house.Middle + Double3.Up * 2));
        Roles(night);
        bool orderedAtDeparture = false, ordered = false;
        night.Until(() => run.Departures > 0, 1500, () =>
        {
            bool before = night.Calls.JobsOrdered;
            Take(night);
            ordered |= night.Calls.JobsOrdered;
            if (run.Departures > 0)
                orderedAtDeparture = before;
        });
        Assert.True(ordered, "nobody took the ping");
        Assert.True(run.Departures > 0, $"never left: driver {night.Driver.Stops!.Doing}");
        Assert.False(orderedAtDeparture, "the train left with a walker out at the town it was sent to");
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}"));
    }
}
