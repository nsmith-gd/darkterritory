using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;
using Incident = DarkTerritory.Sim.Run.Incident;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 App. C.9's rows that aren't a death (note 190), each written the tick it happens with the contributing action
/// its row names: the Track Doll and the Sleepers struck (the throttle and the speed), the Fire Flies' fire (who lit that
/// lamp), a Followers nest (who carried it aboard), a Grumbler craned aboard (who ran the crane), a Stoker in the firebox
/// (who last fuelled or tended it, and how long it went unattended), and the Stoker's runaway derailment, whose cause card
/// names the firebox rather than the throttle.
/// </summary>
public class IncidentEventTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    static Incident Only(World world, IncidentKind kind) => Assert.Single(world.Attribution.Of(kind));

    [Fact]
    public void HittingTheTrackDollNamesTheThrottleAndTheSpeed()
    {
        var n = new Night(4, speed: 10);
        n.World.Attribution.Drove(1);
        n.World.AddEnemy(id => TrackDoll.Ahead(id, n.Train, 60, E.TrackDoll));
        n.Run(8);
        var struck = Only(n.World, IncidentKind.Struck);
        Assert.Equal("Struck the Track Doll", struck.What);
        Assert.Equal(1, struck.Actor);
        Assert.Equal("Throttle: {actor}, 36 km/h.", struck.Action);
        // Its PUNISH is that record, not a second, generic one.
        Assert.Empty(n.World.Attribution.Of(IncidentKind.Punished));
    }

    [Fact]
    public void HittingTheSleepersHardNamesTheThrottleAndTheSpeed()
    {
        // Between heavy damage and the derail (enemies.json sleepers): the track debris row, short of a derailment.
        double speed = (E.Sleepers.HeavyDamageAbove + E.Sleepers.DerailAbove) / 2;
        var n = new Night(4, speed);
        n.World.Attribution.Drove(1);
        n.World.AddEnemy(id => new Sleepers(id) { LineDistance = n.Train.Dynamics.Distance + 55, Height = 0.2 });
        n.Run(12);
        Assert.False(n.World.Derailed);
        var struck = Only(n.World, IncidentKind.Struck);
        Assert.Equal("Ran onto the Sleepers", struck.What);
        Assert.Equal(1, struck.Actor);
        Assert.Equal($"Throttle: {{actor}}, {speed * 3.6:0} km/h.", struck.Action);
    }

    [Fact]
    public void TheFireFliesFireNamesWhoLitThatCarsLamp()
    {
        var n = new Night(5, speed: 0);
        n.Train.Vehicles[3].LampLit = true;
        n.World.Attribution.LitLamp(3, 2);
        n.World.AddEnemy(id => FireFlies.OnLamp(id, n.Train, 3));
        n.Run(E.FireFlies.IgniteSeconds + 1);
        var fire = Only(n.World, IncidentKind.Fire);
        Assert.Equal("Fire Flies set car 3 alight", fire.What);
        Assert.Equal(2, fire.Actor);
        Assert.Equal("Lamp lit by {actor}.", fire.Action);
        // The fire it started taking hold is the same fire: no second record for it.
        n.Run(E.CarFire.BurnFrom / Math.Max(1e-3, E.CarFire.GrowPerSecond) + 2);
        Assert.Single(n.World.Attribution.Log);
    }

    [Fact]
    public void ALampNobodyLitIsSaidSo()
    {
        var n = new Night(5, speed: 0);
        n.Train.Vehicles[2].LampLit = true;
        n.World.AddEnemy(id => FireFlies.OnLamp(id, n.Train, 2));
        n.Run(E.FireFlies.IgniteSeconds + 1);
        var fire = Only(n.World, IncidentKind.Fire);
        Assert.Equal(-1, fire.Actor);
        Assert.Equal("Nobody lit that lamp.", fire.Action);
    }

    [Fact]
    public void AFollowersNestNamesWhoCarriedItAboard()
    {
        var n = new Night(4, speed: 0);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        var f = n.World.AddEnemy(id => Follower.On(id, n.Train, n.Crew[2], 2, E.Followers));
        for (int s = 0; s < 400 && !f.Nested && !f.Gone; s++)
            n.Run(1);
        Assert.True(f.Nested, $"{f.Phase}");
        var nest = Only(n.World, IncidentKind.Nest);
        Assert.StartsWith("Followers nested in car ", nest.What);
        Assert.Equal(2, nest.Actor);
        Assert.Equal("Carried aboard by {actor}.", nest.Action);
    }

    [Fact]
    public void AGrumblerCranedAboardNamesWhoRanTheCrane()
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crane);
        var world = stop.World;
        var quiet = E with { Director = E.Director with { GraceSeconds = 1e9, PaceSeconds = 1e9 } };
        world.EnableEnemies(quiet, world.Route, 1, crew: 1, authority: true);
        var crane = stop.Site.Crane!;
        var grumbler = world.AddEnemy(id => Grumbler.OnCrates(id, crane.Castings[0].At, 0, quiet.Grumbler));
        stop.Step(0.2, []);
        // The lift lands its casting, and the Grumbler on it, in a cargo car: who was at the crane's controls.
        int car = stop.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo).Id;
        world.Attribution.Craned(0);
        crane.Castings[0].State = CastingState.Loaded;
        crane.Castings[0].Car = car;
        crane.Castings[0].At = new Double3(0, 1, 0);
        stop.Step(0.2, []);
        Assert.Equal(car, grumbler.Attached);
        var aboard = Only(world, IncidentKind.Aboard);
        Assert.Equal($"Grumbler craned aboard car {car}", aboard.What);
        Assert.Equal(0, aboard.Actor);
        Assert.Equal("Crane: {actor}.", aboard.Action);
    }

    [Fact]
    public void AStokerInTheFireboxNamesWhoLastTendedItAndHowLongAgo()
    {
        var n = new Night(4, speed: 0, boiler: true);
        n.World.Attribution.Fired(1, 0);
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, E.Stoker));
        n.Run(E.Stoker.SootSeconds + 1, holdSpeed: false);
        Assert.True(stoker.Feeding);
        var runaway = Only(n.World, IncidentKind.Runaway);
        Assert.Equal("Stoker got into the firebox down the stack", runaway.What);
        Assert.Equal(1, runaway.Actor);
        Assert.Matches(@"^Firebox last tended: \{actor\}, unattended \d+ s\.$", runaway.Action);
    }

    [Fact]
    public void ClubbingTheStokerIsTendingTheFirebox()
    {
        var n = new Night(4, speed: 0, boiler: true);
        n.World.Attribution.Fired(1, 0);
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, E.Stoker));
        n.Run(E.Stoker.SootSeconds + 1, holdSpeed: false);
        n.World.BeginTick();
        stoker.Struck(new EnemyContext { Tuning = E, World = n.World }, 3, 1);
        Assert.Equal(3, n.World.Attribution.Tender);
        Assert.Equal(1, n.World.Attribution.Fireman); // the boiler's row (rupture) is still who fired it
    }

    [Fact]
    public void ABendTakenTooFastWithAStokerFeedingIsTheStokersRunawayAndNamesTheFirebox()
    {
        var n = new Night(4, speed: 19, boiler: true);
        n.World.Attribution.Drove(2);
        n.World.Attribution.Fired(1, 0);
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, E.Stoker));
        stoker.Restore(SpinePhase.Commit, 0, E.Stoker.Health, 0, stoker.Local, 0, 0, 0, 0, 0);
        n.World.Overspeed("took the 45 km/h bend at 68 km/h, 23 km/h too fast");
        Assert.Equal("the Stoker ran away with it: took the 45 km/h bend at 68 km/h, 23 km/h too fast", n.World.DerailCause);
        Assert.Equal(1, n.World.DerailActor);
        Assert.Matches(@"^Consist derailed at km \d+, 68 km/h\. The Stoker ran away with it: took the 45 km/h bend at 68 km/h, 23 km/h too fast\. "
            + @"Firebox last tended: Crew 1, unattended \d+ s\. Recovery not scheduled\.$", n.World.Film!.Cause);
    }

    [Fact]
    public void ABendTakenTooFastWithoutAStokerNamesTheThrottle()
    {
        var n = new Night(4, speed: 19, boiler: true);
        n.World.Attribution.Drove(2);
        n.World.Overspeed("took the 45 km/h bend at 68 km/h, 23 km/h too fast");
        Assert.Equal("took the 45 km/h bend at 68 km/h, 23 km/h too fast", n.World.DerailCause);
        Assert.Equal(2, n.World.DerailActor);
        Assert.Equal("Throttle: {actor}.", n.World.DerailAction);
        Assert.Contains("too fast. Throttle: Crew 2. Recovery not scheduled.", n.World.Film!.Cause);
    }

    [Fact]
    public void AVictimlessPunishsStillSitsBesideItsOwnLine()
    {
        // D.12: the auto-bookmark of a PUNISH that held nobody goes beside the record it wrote, not on a line of its own.
        var n = new Night(4, speed: 0);
        var lines = new List<ReportLine> { new(IncidentKind.Struck, "", "Struck the Track Doll at km 4. Throttle: Dave, 38 km/h.") { Seconds = 950 } };
        n.World.Bookmarks.Mirror(new Bookmark(1, BookmarkKind.Punish, 950, -1, 1, 0, Double3.Zero, new Double3(0, 0, -1), "Punished by the Track Doll", "at km 4"));
        var (marked, shown) = Run.Run.MarkBookmarks(n.World, lines);
        var line = Assert.Single(marked);
        Assert.Equal([1], line.Marks);
        Assert.Single(shown);
    }
}
