using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.5 §21's three, met at stops: the Shy Thing (look away while you still can), the Huddle (pet them, never hit
/// them) and the Mimic (not on the count? leave it), each against its rule and App. A.1's fairness contract.
/// </summary>
public class AtStopsTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A stopped train, the director held off (these tests put their own threats out).</summary>
    static Night Stopped(bool boiler = false)
    {
        var n = new Night(4, speed: 0, boiler: boiler);
        n.World.EnableEnemies(E with { Director = E.Director with { GraceSeconds = 1e9 } }, null, 1, crew: 4, authority: true);
        return n;
    }

    static PlayerState OnGround(Night n, double outward, double along = 0, double yaw = 0)
    {
        var frame = n.Train.Frames[2];
        var at = frame.ToWorld(new Double3(frame.Shape.HalfWidth + outward, 0, along));
        var s = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance - frame.Shape.HalfLength, P);
        s.Yaw = yaw;
        return s;
    }

    static Double3 World(Night n, int id) => PlayerMotor.WorldPosition(n.Crew[id], n.Train);

    /// <summary>Straight out along a ground player's view.</summary>
    static Double3 Ahead(Night n, int id, double metres)
    {
        double yaw = n.Crew[id].Yaw;
        return World(n, id) + new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw)) * metres;
    }

    /// <summary>Out on the ground, a few metres off car 2's side, looking away from the train (+X in the car's frame).</summary>
    static (Night N, ShyThing Shy) Watched(double distance = 14)
    {
        var n = Stopped();
        var frame = n.Train.Frames[2];
        double outward = Math.Atan2(-frame.Right.X, -frame.Right.Z);
        n.Crew[1] = OnGround(n, 4, yaw: outward);
        var shy = n.World.AddEnemy(id => ShyThing.Waiting(id, Ahead(n, 1, distance), E.ShyThing));
        return (n, shy);
    }

    static PlayerIntent Turn(double radians) => new() { LookYaw = (float)radians };

    // ---- The Shy Thing (App. A.6): look away while you still can.

    [Fact]
    public void WatchItAndItHasYouOnlyTurningLeftToYou()
    {
        var (n, shy) = Watched();
        n.Run(E.ShyThing.WatchSeconds + 0.2);
        Assert.Equal(SpinePhase.Telegraph, shy.Phase);
        Assert.Equal(1, shy.Victim);
        Assert.True(n.Crew[1].Has(PlayerFlags.Held));
        // Their feet are its: walking gets them nowhere, though they can still turn.
        var before = World(n, 1);
        double yaw = n.Crew[1].Yaw;
        n.Run(0.5, _ => new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run });
        Assert.True((World(n, 1) - before).Length < 0.05);
        n.World.BeginTick();
        var s = n.Crew[1];
        n.World.CrewAct(ref s, Turn(0.2), 1);
        Assert.Equal(yaw + 0.2, s.Yaw, 4);
    }

    [Fact]
    public void LookAwayEarlyAndItsGone()
    {
        var (n, shy) = Watched();
        n.Run(E.ShyThing.WatchSeconds + 0.2);
        Assert.Equal(1, shy.Victim);
        n.Crew[1] = n.Crew[1] with { Yaw = n.Crew[1].Yaw + Math.PI };
        n.Run(ShyThing.LookAwayNeeded(E.ShyThing, shy.Under) + 0.2);
        Assert.True(shy.Gone);
        Assert.True(n.Crew[1].Alive);
        Assert.False(n.Crew[1].Has(PlayerFlags.Held));
        n.AssertFair();
    }

    [Fact]
    public void TheLongerYoureUnderTheLongerItTakesToLookAway()
    {
        var (n, shy) = Watched(distance: 18);
        n.Run(10);
        Assert.Equal(SpinePhase.Commit, shy.Phase);
        double needed = ShyThing.LookAwayNeeded(E.ShyThing, shy.Under);
        Assert.True(needed > E.ShyThing.LookAwaySeconds + 2, $"{needed:0.0} s");
        // A glance away as long as the first one took isn't enough now: it's still there, and still coming.
        n.Crew[1] = n.Crew[1] with { Yaw = n.Crew[1].Yaw + Math.PI };
        n.Run(E.ShyThing.LookAwaySeconds + 0.2);
        Assert.False(shy.Gone);
        n.Run(needed - E.ShyThing.LookAwaySeconds);
        Assert.True(shy.Gone);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void WatchedTooLongItWalksInUnhingesAndSwallowsYouWhole()
    {
        var (n, shy) = Watched();
        var from = shy.Local;
        for (double t = 0; t < 40 && shy.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, shy.Phase);
        Assert.True((shy.Local - from).Length > 10, "it walked in");
        Assert.True(shy.SeenBy(2), "unhinged, everyone sees it");
        n.Run(E.ShyThing.UnhingeSeconds + 0.5);
        Assert.Equal(DeathCause.ShyThing, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void UnderItsTheirsAloneToSee()
    {
        var (n, shy) = Watched();
        Assert.True(shy.SeenBy(2), "waiting in the dark, anyone can see it");
        n.Run(E.ShyThing.WatchSeconds + 0.2);
        Assert.True(shy.SeenBy(1));
        Assert.False(shy.SeenBy(2));
    }

    [Fact]
    public void AFriendSteppingIntoYourLineOfSightBreaksIt()
    {
        var (n, shy) = Watched();
        n.Run(E.ShyThing.WatchSeconds + 0.2);
        Assert.Equal(1, shy.Victim);
        // Halfway between them, told where to stand.
        var mid = (World(n, 1) + shy.Local) * 0.5;
        n.Crew[2] = PlayerMotor.SpawnOnGround(mid with { Y = World(n, 1).Y }, n.Train.Line, n.Train.Dynamics.Distance - 30, P);
        n.Run(ShyThing.LookAwayNeeded(E.ShyThing, shy.Under) + 0.3);
        Assert.True(shy.Gone);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void UnhingedAFriendsBlowSendsItOff()
    {
        var (n, shy) = Watched(distance: 4);
        for (double t = 0; t < 20 && shy.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, shy.Phase);
        // A friend at the victim's shoulder, facing it, swinging.
        var me = World(n, 1);
        var to = (shy.Local - me) with { Y = 0 };
        var friend = PlayerMotor.SpawnOnGround(me + new Double3(to.Z, 0, -to.X).Normalized * 0.8, n.Train.Line, n.Train.Dynamics.Distance - 30, P);
        friend.Yaw = Math.Atan2(-to.X, -to.Z);
        n.Crew[2] = friend;
        n.Run(1, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.True(shy.Gone);
        Assert.True(n.Crew[1].Alive);
        n.AssertFair();
    }

    [Fact]
    public void CarriedOffOutOfItsSightItLosesThem()
    {
        // Under on a car's deck at a stop, and the train pulls out: it can't follow, and they're free.
        var n = Stopped();
        var frame = n.Train.Frames[2];
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Yaw = Math.PI / 2 };
        var shy = n.World.AddEnemy(id => ShyThing.Waiting(id, Ahead(n, 1, 14) with { Y = 0 }, E.ShyThing));
        n.Run(E.ShyThing.WatchSeconds + 0.2);
        Assert.Equal(1, shy.Victim);
        n.Train.Dynamics.Velocity = 10;
        n.Run(8);
        Assert.True(shy.Gone);
        Assert.True(n.Crew[1].Alive);
        Assert.False(n.Crew[1].Has(PlayerFlags.Held));
    }

    [Fact]
    public void ItStandsWhereSomeoneOnTheGroundIsLookingClearOfTheTrain()
    {
        var n = Stopped();
        var frame = n.Train.Frames[2];
        // Facing the train: it won't stand in it; it's turned off their view as little as it takes.
        double atTrain = Math.Atan2(frame.Right.X, frame.Right.Z);
        n.Crew[1] = OnGround(n, 4, yaw: atTrain);
        Assert.Null(ShyThing.Spot(n.Train, n.Crew[1], 6));
        double along = Math.Atan2(frame.Back.X, frame.Back.Z);
        n.Crew[1] = n.Crew[1] with { Yaw = along };
        var spot = ShyThing.Spot(n.Train, n.Crew[1], 15);
        Assert.NotNull(spot);
        foreach (var f in n.Train.Frames)
            Assert.True(Math.Abs(f.ToLocal(spot!.Value).X) > f.Shape.HalfWidth + 2 || Math.Abs(f.ToLocal(spot.Value).Z) > f.Shape.HalfLength + 2);
    }

    // ---- The Huddle (App. A.6): pet them, never hit them.

    static (Night N, Huddle Flock) Flock(int count = 5, bool boiler = false)
    {
        var n = Stopped(boiler);
        n.Crew[1] = OnGround(n, 4);
        var at = World(n, 1) + new Double3(2, 0, 0);
        var h = n.World.AddEnemy(id => Huddle.Flock(id, at, count));
        return (n, h);
    }

    /// <summary>Facing a point on the ground (yaw only).</summary>
    static double Facing(Double3 from, Double3 to) => Math.Atan2(-(to.X - from.X), -(to.Z - from.Z));

    [Fact]
    public void TheyFollowYouAboutAndChirpIntoTheCrewsLoudness()
    {
        var (n, flock) = Flock();
        Assert.Equal(E.Huddle.ChirpLoudness * 5, flock.Noise(E.Huddle), 9);
        // Walk off: they come after you.
        n.Crew[1] = n.Crew[1] with { Yaw = Facing(World(n, 1), flock.Local) + Math.PI };
        n.Run(3, _ => new PlayerIntent { MoveZ = 1 });
        Assert.InRange(((flock.Local - World(n, 1)) with { Y = 0 }).Length, 0, E.Huddle.FollowAt + 1.5);
        Assert.True(n.World.Choir.Loudness > 0, "the meter hears them");
    }

    [Fact]
    public void PettingThemHushesThem()
    {
        var (n, flock) = Flock();
        n.Crew[1] = n.Crew[1] with { Yaw = Facing(World(n, 1), flock.Local), Pitch = -0.8 };
        n.Run(1.5);
        double before = flock.Noise(E.Huddle);
        n.Run(2, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(flock.Calm > 0.9, $"{flock.Calm:0.00}");
        Assert.True(flock.Noise(E.Huddle) < before * 0.2);
        // Unpetted, the hush wears off.
        n.Run(E.Huddle.CalmSeconds * 0.5);
        Assert.InRange(flock.Calm, 0.3, 0.7);
    }

    [Fact]
    public void HitOneAndTheRestBristleThenBuryYou()
    {
        var (n, flock) = Flock();
        n.Crew[1] = n.Crew[1] with { Yaw = Facing(World(n, 1), flock.Local) };
        n.Run(0.1, _ => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Equal(4, flock.Count);
        Assert.Equal(SpinePhase.Telegraph, flock.Phase);
        Assert.Equal(1, flock.Target);
        for (double t = 0; t < 6 && flock.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, flock.Phase);
        Assert.Equal(1, flock.Holding);
        n.Run(E.Huddle.BuriedSeconds + 0.5);
        Assert.Equal(DeathCause.Huddle, n.Crew[1].Death);
        // And they're as sweet as ever.
        Assert.Equal(SpinePhase.Dormant, flock.Phase);
        n.AssertFair();
    }

    [Fact]
    public void ClearOfThemWhileTheyBristleAndTheySettle()
    {
        var (n, flock) = Flock();
        n.Crew[1] = n.Crew[1] with { Yaw = Facing(World(n, 1), flock.Local) };
        n.Run(0.1, _ => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Equal(SpinePhase.Telegraph, flock.Phase);
        // Run, along the train.
        var back = n.Train.Frames[2].Back;
        n.Crew[1] = n.Crew[1] with { Yaw = Math.Atan2(back.X, back.Z) };
        n.Run(1.5, _ => new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run });
        Assert.Equal(SpinePhase.Dormant, flock.Phase);
        Assert.True(n.Crew[1].Alive);
        n.AssertFair();
    }

    [Fact]
    public void BuriedAFriendPetsThemOffButAnotherBlowOnlyMakesItWorse()
    {
        var (n, flock) = Flock();
        n.Crew[1] = n.Crew[1] with { Yaw = Facing(World(n, 1), flock.Local) };
        n.Run(0.1, _ => new PlayerIntent { Actions = PlayerActions.Swing });
        for (double t = 0; t < 6 && flock.Phase != SpinePhase.Grab; t += 0.1)
            n.Run(0.1);
        Assert.Equal(SpinePhase.Grab, flock.Phase);
        // A friend swings: one more dies, and the buried one's time is shorter. They're not let go.
        var me = World(n, 1);
        var friend = PlayerMotor.SpawnOnGround(me + new Double3(0, 0, 1.0), n.Train.Line, n.Train.Dynamics.Distance - 30, P);
        friend.Yaw = 0;
        n.Crew[2] = friend;
        double window = flock.GrabWindow;
        n.Run(0.1, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.Equal(SpinePhase.Grab, flock.Phase);
        Assert.Equal(3, flock.Count);
        Assert.Equal(window - E.Huddle.HitShortens, flock.GrabWindow, 6);
        // Use at them: petted off.
        n.Run(0.2, id => id == 2 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.Equal(SpinePhase.Dormant, flock.Phase);
        Assert.True(n.Crew[1].Alive);
        Assert.True(flock.Calm > 0.9);
    }

    [Fact]
    public void WithNobodyOnTheGroundTheyGoAboardAndPileRoundTheFirebox()
    {
        var (n, flock) = Flock(boiler: true);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        var cabSpot = n.Train.Frames[0].ToWorld(Huddle.CabSpot(n.Train));
        flock.Local = new Double3(cabSpot.X + 14, 0, cabSpot.Z);
        n.Run(8);
        Assert.True(flock.Aboard);
        Assert.Equal(Huddle.CabSpot(n.Train), flock.Local);
        // And they ride along.
        n.Train.Dynamics.Velocity = 12;
        n.Run(5);
        Assert.True(flock.Aboard);
    }

    [Fact]
    public void LiveCoalsFlungOutOfTheFireboxDrawThemOffAndTheTrainLeavesThemBehind()
    {
        var n = Stopped(boiler: true);
        var cab = n.Train.Frames[0];
        var firebox = cab.Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox);
        // The fireman at the firebox, shovel in hand, looking out of the cab's side and up a little.
        var s = PlayerMotor.SpawnInCab(n.Train, P);
        s.Position = new Double3(firebox.Position.X + 0.2, s.Position.Y, firebox.Position.Z + 0.4);
        s.Kit = Kit.With(s.Kit, 1, Tool.Shovel);
        s.HeldSlot = 1;
        s.Yaw = -Math.PI / 2;
        s.Pitch = 0.5;
        n.Crew[1] = s;
        var flock = n.World.AddEnemy(id => Huddle.Flock(id, default, 5));
        flock.Attached = 0;
        flock.Local = Huddle.CabSpot(n.Train);
        Assert.Equal(CrewActions.Nearest(s, n.Train), InteractableKind.Firebox);
        double fire = n.Train.Boiler.Firebox;
        n.Run(0.1, _ => new PlayerIntent { Buttons = PlayerButtons.Throw });
        var embers = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Embers);
        Assert.InRange(n.Train.Boiler.Firebox, fire - 1.2, fire - 0.9);
        Assert.True(n.Train.Boiler.FireDoorOpen);
        // Lying out on the ground (bodies aren't stepped in this harness: put where it'd land).
        embers.Parent = PlayerState.World;
        embers.Pbd.Particles[0].Position = cab.ToWorld(new Double3(cab.Shape.HalfWidth + 8, 0, 0)) with { Y = 0.15 };
        n.Run(6);
        Assert.False(flock.Aboard);
        Assert.True(((Bodies.WorldCentre(embers, n.Train) - flock.WorldPosition(n.Train)) with { Y = 0 }).Length < 2);
        // And away the train goes, the fireman aboard.
        n.Train.Dynamics.Velocity = 15;
        n.Run(15);
        Assert.True(flock.Gone);
    }

    [Fact]
    public void CoalsThatComeToRestOnACarsFloorSetItAlight()
    {
        var n = Stopped(boiler: true);
        int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var embers = n.World.Bodies.SpawnCrate(n.Train, car, new Double3(0, room.Min.Y, 0), BodyKind.Embers);
        embers.Pbd.Sleep();
        n.Run(0.2);
        Assert.Contains(n.World.ActiveEnemies, e => e is CarFire f && f.Attached == car);
        Assert.DoesNotContain(embers, n.World.Bodies.All);
    }

    // ---- The Mimic (App. A.6): not on the count? Leave it.

    /// <summary>A Mimic stowed in car 2 (a crate on its floor), and the crew in the cab.</summary>
    static (Night N, Mimic Mimic, Body Crate) Stowed()
    {
        var n = Stopped();
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        var room = n.Train.Frames[2].Shape.Interior!.Value;
        var crate = n.World.Bodies.SpawnCrate(n.Train, 2, new Double3(0, room.Min.Y, 0), BodyKind.Cargo);
        var mimic = n.World.AddEnemy(id => Mimic.As(id, crate, E.Mimic));
        return (n, mimic, crate);
    }

    static PlayerState InCar(Night n, int car, Double3 at) =>
        new() { Parent = car, Position = at, Surface = Surface.Deck, Health = P.Health, LineHint = n.Train.Cars[car].FrontDistance };

    [Fact]
    public void StowedItWakesAndBitesWhoeverComesNear()
    {
        var (n, mimic, crate) = Stowed();
        n.Run(E.Mimic.WakeSeconds + 1);
        Assert.Equal(SpinePhase.Dormant, mimic.Phase);
        Assert.Equal(2, mimic.Attached);
        n.Crew[2] = InCar(n, 2, crate.Centre with { Y = n.Train.Frames[2].Shape.Interior!.Value.Min.Y, Z = crate.Centre.Z + 0.9 });
        n.Run(0.2);
        Assert.Equal(SpinePhase.Telegraph, mimic.Phase);
        Assert.True(mimic.Open);
        n.Run(E.Mimic.LidSeconds + 0.2);
        Assert.Equal(SpinePhase.Grab, mimic.Phase);
        Assert.Equal(2, mimic.Holding);
        n.Run(E.Mimic.BiteSeconds + 0.5);
        Assert.Equal(DeathCause.Mimic, n.Crew[2].Death);
        // Shut again, and waiting.
        Assert.Equal(SpinePhase.Dormant, mimic.Phase);
        n.AssertFair();
    }

    [Fact]
    public void BeforeItWakesItsJustACrate()
    {
        var (n, mimic, crate) = Stowed();
        n.Crew[2] = InCar(n, 2, crate.Centre with { Y = n.Train.Frames[2].Shape.Interior!.Value.Min.Y, Z = crate.Centre.Z + 0.9 });
        n.Run(E.Mimic.WakeSeconds - 5);
        Assert.Equal(SpinePhase.Dormant, mimic.Phase);
        Assert.True(n.Crew[2].Alive);
    }

    [Fact]
    public void StepBackWhileTheLidLiftsAndItShutsAgain()
    {
        var (n, mimic, crate) = Stowed();
        n.Run(E.Mimic.WakeSeconds + 1);
        var floor = n.Train.Frames[2].Shape.Interior!.Value.Min.Y;
        n.Crew[2] = InCar(n, 2, crate.Centre with { Y = floor, Z = crate.Centre.Z + 0.9 });
        n.Run(0.3);
        Assert.Equal(SpinePhase.Telegraph, mimic.Phase);
        n.Crew[2] = n.Crew[2] with { Position = n.Crew[2].Position with { Z = crate.Centre.Z + 4 } };
        n.Run(0.2);
        Assert.Equal(SpinePhase.Dormant, mimic.Phase);
        Assert.True(n.Crew[2].Alive);
        n.AssertFair();
    }

    [Fact]
    public void AFriendPullsThemOut()
    {
        var (n, mimic, crate) = Stowed();
        n.Run(E.Mimic.WakeSeconds + 1);
        var floor = n.Train.Frames[2].Shape.Interior!.Value.Min.Y;
        n.Crew[2] = InCar(n, 2, crate.Centre with { Y = floor, Z = crate.Centre.Z + 0.9 });
        n.Run(E.Mimic.LidSeconds + 0.5);
        Assert.Equal(SpinePhase.Grab, mimic.Phase);
        n.Crew[3] = InCar(n, 2, crate.Centre with { Y = floor, Z = crate.Centre.Z + 1.8 });
        n.Run(0.2, id => id == 3 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.NotEqual(SpinePhase.Grab, mimic.Phase);
        Assert.True(n.Crew[2].Alive);
    }

    [Fact]
    public void ItBreathesWhenYouStandStillBesideIt()
    {
        var (n, mimic, crate) = Stowed();
        var floor = n.Train.Frames[2].Shape.Interior!.Value.Min.Y;
        n.Run(1);
        Assert.False(mimic.Breathing);
        n.Crew[2] = InCar(n, 2, crate.Centre with { Y = floor, Z = crate.Centre.Z + 1.5 });
        n.Run(E.Mimic.BreatheStill + 0.5);
        Assert.True(mimic.Breathing);
        Assert.Equal(0.5, mimic.Extra2);
        // Moving about, nobody's still enough to hear it.
        n.Run(1, id => id == 2 ? new PlayerIntent { MoveX = 0.4f } : default);
        Assert.False(mimic.Breathing);
    }

    [Fact]
    public void StruckItLungesAtWhoeverStruckIt()
    {
        var (n, mimic, crate) = Stowed();
        var floor = n.Train.Frames[2].Shape.Interior!.Value.Min.Y;
        // Two metres off, out of its reach, swinging at it: it doesn't stay a crate.
        n.Crew[2] = InCar(n, 2, crate.Centre with { Y = floor, Z = crate.Centre.Z + 2.0 });
        n.Run(0.1, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.Equal(E.Mimic.Health - E.Melee.Barehanded, mimic.Health, 6);
        Assert.Equal(SpinePhase.Telegraph, mimic.Phase);
        n.Run(E.Mimic.LidSeconds + 0.3);
        Assert.Equal(SpinePhase.Grab, mimic.Phase);
        Assert.Equal(2, mimic.Holding);
    }

    [Fact]
    public void KilledItsCrateGoesWithIt()
    {
        var (n, mimic, crate) = Stowed();
        mimic.Health = 0.1;
        var floor = n.Train.Frames[2].Shape.Interior!.Value.Min.Y;
        n.Crew[2] = InCar(n, 2, crate.Centre with { Y = floor, Z = crate.Centre.Z + 2.0 });
        n.Run(0.1, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.True(mimic.Gone);
        Assert.DoesNotContain(crate, n.World.Bodies.All);
    }

    [Fact]
    public void ThrownOffTheTrainItsLeftBehind()
    {
        var (n, mimic, crate) = Stowed();
        crate.Parent = PlayerState.World;
        crate.Pbd.Particles[0].Position = n.Train.Frames[2].ToWorld(new Double3(6, 0.3, 0));
        n.Train.Dynamics.Velocity = 15;
        n.Run(25);
        Assert.True(mimic.Gone);
        Assert.DoesNotContain(crate, n.World.Bodies.All);
    }

    [Fact]
    public void OnAStopItsOneMoreThanTheCountCarriedItsHeavierAndItNeverGoesIntoTheLoad()
    {
        // The spawn rule's own stop, from FacilityTests: crates out at a facility, the train stopped.
        var stop = new FacilityTests.Stop(ModuleKind.Crates);
        stop.World.EnableEnemies(E with { Director = E.Director with { GraceSeconds = 1e9 } }, null, 1, crew: 2, authority: true);
        stop.Step(1, []);
        var ctx = new SpawnContext(stop.World, E, stop.World.Director!);
        Assert.True(ctx.AtStop);
        var rule = Spawns.For(EnemyKind.Mimic)!;
        Assert.Equal(1.0, rule.Weight(ctx)!.Value);
        int crates = stop.World.Bodies.All.Count(b => b.Kind == BodyKind.Cargo);
        Assert.True(rule.Spawn(ctx));
        var mimic = Assert.Single(stop.World.ActiveEnemies.OfType<Mimic>());
        var body = stop.World.Bodies.All.Single(b => b.Id == mimic.BodyId);
        Assert.Equal(crates + 1, stop.World.Bodies.All.Count(b => b.Kind == BodyKind.Cargo));
        // Like the rest: freight, holding what they hold.
        var other = stop.World.Bodies.All.First(b => b.Kind == BodyKind.Cargo && b != body);
        Assert.Equal(other.Cargo, body.Cargo);

        // Carried: heavier than a crate should be.
        stop.Step(2, []);
        var s = stop.OnTheGround(body.Pbd.Particles[0].Position + new Double3(0, 0, 0.7));
        s.Yaw = 0;
        stop.Crew.Add(s);
        stop.Step(0.1, [default]);
        stop.Step(0.1, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        Assert.Equal(1, body.Carrier);
        stop.Step(0.1, [default]);
        Assert.True(stop.Crew[0].Has(PlayerFlags.Laden));
        var start = stop.Crew[0].Position;
        stop.Step(1, [new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }]);
        Assert.InRange((stop.Crew[0].Position - start).Length, P.CarryLaden * 0.8, P.CarryLaden * 1.05);
        Assert.True(P.CarryLaden < P.CarryHeavy);

        // Put down in a cargo car: it lies there, never taken into the load.
        stop.Step(0.1, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        var car = stop.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = stop.Train.Frames[car.Id].Shape.Interior!.Value;
        body.Parent = car.Id;
        body.Pbd.Particles[0].Position = new Double3(0, room.Min.Y + 0.5, 0);
        body.Pbd.Particles[0].Previous = body.Pbd.Particles[0].Position;
        stop.Crew.Clear();
        double load = car.Load;
        stop.Step(FacilityTests.F.Crates.SettleSeconds + 2, []);
        Assert.Equal(load, car.Load, 6);
        Assert.Contains(body, stop.World.Bodies.All);
    }

    [Fact]
    public void NoneOfThemComeOffAStop()
    {
        // Out on the line at speed, the crew on the roofs: the yard's threats stay in the yard.
        var n = new Night(4, speed: 12);
        n.World.EnableEnemies(E with { Director = E.Director with { GraceSeconds = 0, CooldownSeconds = [1, 1] } }, null, 3, crew: 3, authority: true);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(300);
        Assert.DoesNotContain(n.World.Director!.Log, l => l.Kind is EnemyKind.ShyThing or EnemyKind.Huddle or EnemyKind.Mimic);
    }
}
