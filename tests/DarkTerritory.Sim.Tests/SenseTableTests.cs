using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The sense table, and thrown things doing what they are (GDD App. A.1, App. C, §21; queue #462, ARCHITECTURE §8 note 728;
/// the director, 14 Oct 2026, App. F.6; the playtest of 13 Oct evening, line 24: "I'm pretty sure I can take a fire
/// extinguisher and use them on the cinder hounds, can't I? ... they still got me, even though I was using it on them. That
/// should have worked."): every item gives off or masks a sense, and each creature answers its own sense of it, and only
/// that. A music box draws the Moose, a lamp is nothing to it, the extinguisher drives the hounds back, a thrown lamp lights
/// the fire grid, a keg thrown hard goes up, a knock on the rail draws the Car Hugger off.
/// </summary>
public class SenseTableTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly MooseTuning M = Tuning.Enemies.Moose;
    static readonly SenseTuning S = SenseTuning.Load();

    static Double3 Beside(Night n, int car, double lateral, double along = 0)
    {
        var at = n.Train.Frames[car].ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    static double Flat(Double3 v) => Math.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>A wound toy let go of (its <c>playsSeconds</c> ahead of it), lying at <paramref name="at"/>, last held by <paramref name="by"/>.</summary>
    static Body WoundToy(Night n, Double3 at, ToyNoise noise, int by = 1)
    {
        var toy = n.World.Bodies.SpawnItem(at, n.Train.Dynamics.Distance, BodyKind.Toy);
        toy.Noise = noise;
        toy.Plays = S.Toys.Plays(noise);
        toy.LastHeld = by;
        toy.Pbd.Sleep();
        return toy;
    }

    [Fact]
    public void TheTableSaysWhatEachThingGivesOffInTheCreaturesOwnSenses()
    {
        Assert.Equal(Sense.Light, Assert.Single(S.Of("handLamp")).Sense);
        Assert.Equal(Sense.Light, Assert.Single(S.Of("carLamp")).Sense);
        foreach (var toy in new[] { "squeaker", "musicBox", "drummer" })
            Assert.Equal(Sense.Sound, Assert.Single(S.Of(toy)).Sense);
        // The extinguisher's cold masks heat.
        Assert.True(S.Of("extinguisherSpray", Sense.Heat)!.Strength < 0);
        Assert.Equal(Sense.Vibration, Assert.Single(S.Of("knock")).Sense);
        Assert.Equal(Sense.Scent, Assert.Single(S.Of("food")).Sense);
        Assert.Equal(Sense.Absence, Assert.Single(S.Of("body")).Sense);
        Assert.NotNull(S.Of("burningLamp", Sense.Heat));
        // Wound, the music box and the drummer play on set down; a squeaker only squeezed.
        Assert.True(S.Toys.Plays(ToyNoise.MusicBox) > 0 && S.Toys.Plays(ToyNoise.Drummer) > 0);
        Assert.Equal(0, S.Toys.Plays(ToyNoise.Squeaker));
        // The file and the code's mirror of it agree on the numbers the tests lean on.
        Assert.Equal(new SenseTuning().Answers, S.Answers);
    }

    [Fact]
    public void AMusicBoxLying20mOffDrawsTheMooseOverAQuietCrewmate()
    {
        var n = new Night(4, speed: 0);
        var home = Beside(n, 2, 30);
        var moose = n.World.AddEnemy(id => Moose.Grazing(id, home, n.Train.Dynamics.Distance, 0));
        // A quiet crewmate on the ground just past its crowding, the other way from the box.
        n.Crew[1] = PlayerMotor.SpawnOnGround(Beside(n, 2, 30, -(M.CrowdAt + 2)), n.Train.Line, n.Train.Dynamics.Distance, P);
        var box = WoundToy(n, Beside(n, 2, 30, 20), ToyNoise.MusicBox);
        n.Run(15);
        Assert.InRange(Flat(Physics.Bodies.WorldCentre(box, n.Train) - moose.Local), 0, S.Answers.Moose.StandOff + 1);
        Assert.Null(moose.Target);
        Assert.Equal(P.Health, n.Crew[1].Health);
        // Run down, it goes back to its ground.
        box.Plays = 0;
        n.Run(15);
        Assert.InRange(Flat(moose.Local - home), 0, 2);
        n.AssertFair();
    }

    [Fact]
    public void AMusicBoxIsWhereTheMooseSearchesForWhoeverItLost()
    {
        var n = new Night(4, speed: 0);
        var home = Beside(n, 2, 30);
        // Riled at crewmate 1, who's gone up on a car's roof out of its sight.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        var moose = n.World.AddEnemy(id => Moose.Enraged(id, home, n.Train.Dynamics.Distance, 1));
        var box = WoundToy(n, Beside(n, 2, 30, 18), ToyNoise.Drummer);
        n.Run(10);
        Assert.InRange(Flat(Physics.Bodies.WorldCentre(box, n.Train) - moose.Local), 0, 3);
    }

    [Fact]
    public void ASoundCreatureIgnoresALamp()
    {
        var n = new Night(4, speed: 0);
        var home = Beside(n, 2, 30);
        var moose = n.World.AddEnemy(id => Moose.Grazing(id, home, n.Train.Dynamics.Distance, 0));
        var lamp = n.World.Bodies.SpawnItem(Beside(n, 2, 30, 8), n.Train.Dynamics.Distance, BodyKind.Lamp);
        lamp.Pbd.Sleep();
        Assert.Contains(n.World.AllEmitters(), e => e.Kind == EmitterKind.HandLamp && e.Sense == Sense.Light && e.Reaches(home));
        n.Run(15);
        Assert.Equal(SpinePhase.Dormant, moose.Phase);
        Assert.InRange(Flat(moose.Local - home), 0, 0.01);
    }

    /// <summary>A hound on car 3's roof, at the fight, and crewmate 1 on the roof 2.5 m up the car from it with an extinguisher, aimed at it.</summary>
    static (Night N, CinderHound Hound, Body Extinguisher) HoundAndSprayer(int car = 3)
    {
        var n = new Night(4, 10, enemies: HoundRunTests.Quiet);
        double roof = n.Train.Frames[car].Shape.RoofHeight;
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, car, new Double3(0, roof, 1.5), 0, 0, 0.6, hound.Id, 0);
        var s = PlayerMotor.SpawnOnRoof(n.Train, car, -1.0, P);
        double eye = Tuning.Train.Pick.EyeHeight;
        // Facing +Z down the car at it, the look dipped onto its back.
        n.Crew[1] = s with { Yaw = Math.PI, Pitch = Math.Atan2(roof + 0.4 - (s.Position.Y + eye), 2.5) };
        var ext = n.World.Bodies.SpawnCrate(n.Train, car, s.Position + Double3.Up * 1.1, BodyKind.Extinguisher);
        ext.Carrier = 1;
        return (n, hound, ext);
    }

    static PlayerIntent Spray => new() { Buttons = PlayerButtons.Fire };

    [Fact]
    public void TheExtinguisherSprayedOnAHoundInRangeDrivesItBack()
    {
        // Not sprayed, it has them (the playtest's "they still got me").
        var (bare, _, _) = HoundAndSprayer();
        bare.Run(Tuning.Enemies.CinderHounds.BiteEverySeconds * 2 + 1);
        Assert.True(bare.Crew[1].Health < P.Health);
        // Sprayed: it backs off down the car and never bites; held at it long enough, it's off the train.
        var (n, hound, ext) = HoundAndSprayer();
        double from = hound.Local.Z;
        n.Run(1, _ => Spray);
        Assert.True(hound.Repelled > 0);
        Assert.True(hound.Local.Z > from + 1, $"backed off {hound.Local.Z - from:0.00} m");
        Assert.True(ext.Charge < 1);
        // Backed off out of the spray's reach: after it down the roof, spraying, and it's off the train.
        for (int i = 0; i < S.Answers.CinderHounds.DriveOffSeconds * 4 && !hound.Gone; i++)
        {
            var s = n.Crew[1];
            n.Crew[1] = s with { Position = s.Position with { Z = Math.Max(s.Position.Z, hound.Local.Z - 2.5) } };
            n.Run(0.25, _ => Spray);
        }
        Assert.True(hound.Gone);
        Assert.Contains(EnemyKind.CinderHound, n.World.DrivenOff);
        Assert.Equal(P.Health, n.Crew[1].Health);
        n.AssertFair();
    }

    [Fact]
    public void ASprayedHoundLetsGoOfWhoeverItHolds()
    {
        var (n, hound, _) = HoundAndSprayer();
        // Crewmate 2 pinned under it on the roof.
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 1.6, P) with { Health = 10 };
        hound.Restore(SpinePhase.Grab, 0, Tuning.Enemies.CinderHounds.Health, 3, hound.Local, 0, 0, 0.6, hound.Id, 0, holding: 2,
            grabWindow: Tuning.Enemies.CinderHounds.MaulSeconds);
        n.Run(0.5, id => id == 1 ? Spray : default);
        Assert.NotEqual(SpinePhase.Grab, hound.Phase);
        Assert.Equal(-1, hound.Holding);
        n.Run(Tuning.Enemies.CinderHounds.MaulSeconds, id => id == 1 ? Spray : default);
        Assert.True(n.Crew[2].Alive);
    }

    [Fact]
    public void AnExtinguisherAimedAwayDoesNothingToTheHound()
    {
        var (n, hound, ext) = HoundAndSprayer();
        n.Crew[1] = n.Crew[1] with { Yaw = 0 };
        n.Run(1, _ => Spray);
        Assert.Equal(0, hound.Repelled);
        Assert.Equal(1, ext.Charge);
    }

    /// <summary>Steps a night with its bodies (the host's order: the crew, the world, the knocks, the bodies).</summary>
    static void RunWithBodies(Night n, double seconds)
    {
        var controls = new TrainControls { Reverser = 1 };
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.World.BeginTick();
            n.World.Step(controls);
            n.World.ApplyDamage(id => n.Crew.TryGetValue(id, out var s) ? s : null, (id, s) => n.Crew[id] = s, n.Crew.Keys);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    [Fact]
    public void AThrownLampLandingOnACarFloorStartsAFireCell()
    {
        var n = new Night(4, speed: 0);
        int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        // Set down from the hands' height, it's a lamp still.
        var setDown = n.World.Bodies.SpawnCrate(n.Train, car, new Double3(room.Centre.X, room.Min.Y + 1.0, room.Centre.Z + 2), BodyKind.Lamp);
        // Thrown down at the boards.
        var thrown = n.World.Bodies.SpawnCrate(n.Train, car, new Double3(room.Centre.X, room.Min.Y + 1.0, room.Centre.Z - 2), BodyKind.Lamp);
        thrown.LastHeld = 1;
        thrown.Pbd.Particles[0].SetVelocity(new Double3(0, -9, 1), SimConstants.TickSeconds);
        thrown.Pbd.Wake();
        RunWithBodies(n, 1);
        Assert.Contains(setDown, n.World.Bodies.All);
        Assert.False(setDown.Broken);
        Assert.DoesNotContain(thrown, n.World.Bodies.All);
        var fire = Assert.Single(n.World.ActiveEnemies.OfType<CarFire>(), f => !f.Gone && f.Attached == car);
        var grid = FireGrid.Of(n.Train, car, Tuning.Enemies.CarFire.CellSize)!;
        int cell = grid.Nearest(new Double3(room.Centre.X, room.Min.Y, room.Centre.Z - 2));
        Assert.True(fire.Heat[cell] > 0, "the cell it broke on isn't alight");
    }

    [Fact]
    public void AThrownLampOnTheGroundBurnsAShortWhileWhereItLies()
    {
        var n = new Night(4, speed: 0);
        var at = Beside(n, 2, 8);
        var lamp = n.World.Bodies.SpawnItem(at + Double3.Up * 1.0, n.Train.Dynamics.Distance, BodyKind.Lamp);
        lamp.Pbd.Particles[0].SetVelocity(new Double3(0, -9, 0), SimConstants.TickSeconds);
        lamp.Pbd.Wake();
        RunWithBodies(n, 0.5);
        Assert.True(lamp.Broken);
        Assert.Contains(n.World.AllEmitters(), e => e.Kind == EmitterKind.BurningLamp && e.Sense == Sense.Heat);
        // Broken glass and burning oil: nothing to pick up.
        Assert.Null(n.World.Bodies.InReach(PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P), n.Train));
        RunWithBodies(n, S.Thrown.GroundBurnSeconds + 0.5);
        Assert.DoesNotContain(lamp, n.World.Bodies.All);
    }

    [Fact]
    public void AKegThrownHardGoesUp()
    {
        var stop = new FacilityTests.Stop(FacilityKind.MilitaryDepot);
        stop.Step(2, []);
        var keg = stop.World.Bodies.All.First(b => b.Kind == BodyKind.Cargo && b.Cargo == CargoKind.Ammunition);
        // Thrown at a car's side from 3 m off, at a throw's speed.
        var frame = stop.Train.Frames[2];
        var side = frame.ToWorld(new Double3(frame.Shape.HalfWidth, 1.4, 0));
        var from = frame.ToWorld(new Double3(frame.Shape.HalfWidth + 3, 1.6, 0));
        // In someone's hands a tick (so its let-go is the hands', as a throw's is), then away.
        keg.Carrier = 99;
        stop.Step(SimConstants.TickSeconds, []);
        keg.Carrier = -1;
        ref var p = ref keg.Pbd.Particles[0];
        p.Position = from;
        p.SetVelocity((side - from).Normalized * stop.World.Bodies.Hands.ThrowSpeed, SimConstants.TickSeconds);
        keg.Pbd.Wake();
        // Not on its way: on its landing.
        stop.Step(SimConstants.TickSeconds * 2, []);
        Assert.Contains(keg, stop.World.Bodies.All);
        stop.Step(1, []);
        Assert.DoesNotContain(keg, stop.World.Bodies.All);
        Assert.Contains(stop.World.Impacts, i => i.Shooter == -1);
    }

    [Fact]
    public void AKnockOnTheRailDrawsALurkingCarHuggerOff()
    {
        var n = new Night(4, speed: 0);
        double front = n.Train.Dynamics.Distance;
        var hugger = n.World.AddEnemy(id => CarHugger.Lurking(id, front + 200, 1, Tuning.Enemies.CarHugger));
        // A crate dropped hard on the rails 25 m short of it.
        var rail = n.Train.Line.Sample(front + 175).Position;
        var crate = n.World.Bodies.SpawnItem(rail + Double3.Up * 1.2, front + 175, BodyKind.Crate);
        crate.LastHeld = 1;
        crate.Pbd.Particles[0].SetVelocity(new Double3(0, -7, 0), SimConstants.TickSeconds);
        crate.Pbd.Wake();
        RunWithBodies(n, 0.5);
        Assert.Contains(n.World.AllEmitters(), e => e.Kind == EmitterKind.Knock && e.By == 1);
        RunWithBodies(n, 5);
        Assert.True(hugger.Decoyed);
        Assert.True(hugger.LineDistance < front + 195, $"still at {hugger.LineDistance - front:0.0} m");
        // Knotter, Hotbox: their sense is the train's running, never a knock (App. A.7); nothing here touches them.
    }

    [Fact]
    public void ALumpOfCoalOnTheRailKnocks()
    {
        var n = new Night(4, speed: 0);
        double front = n.Train.Dynamics.Distance;
        var rail = n.Train.Line.Sample(front + 30).Position;
        Assert.True(n.World.OnRail(rail, front + 30));
        Assert.False(n.World.OnRail(rail + Double3.Cross(n.Train.Line.Sample(front + 30).Tangent, Double3.Up).Normalized * 4, front + 30));
        n.World.KnockRail(rail, 1);
        var knock = Assert.Single(n.World.AllEmitters(), e => e.Kind == EmitterKind.Knock);
        Assert.Equal(Sense.Vibration, knock.Sense);
        // Heard for the knock's while, then gone.
        n.Run(S.Knock.Seconds + 0.2);
        Assert.DoesNotContain(n.World.AllEmitters(), e => e.Kind == EmitterKind.Knock);
    }

    [Fact]
    public void AMusicBoxSetDownPlaysOnAndCountsOnTheChoirsMeterInTheNameOfWhoeverLastHadIt()
    {
        var n = new Night(6, speed: 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        var room = n.Train.Frames[3].Shape.Interior!.Value;
        var box = n.World.Bodies.SpawnCrate(n.Train, 3, new Double3(0, room.Min.Y + 0.1, 0), BodyKind.Toy);
        box.Noise = ToyNoise.MusicBox;
        n.World.Choir = new ChoirState { Build = 0.5, Loudness = Tuning.Combat.Choir.MaxLoudness };
        // In crewmate 2's hands a moment, then set down: it plays on, in their name.
        box.Carrier = 2;
        n.Run(0.5);
        box.Carrier = -1;
        double held = n.World.ChoirShare(2);
        n.Run(2);
        Assert.True(box.Playing);
        Assert.InRange(n.World.ChoirShare(2) - held, 1.9 * Tuning.Combat.Choir.Toys.MusicBox, 2.1 * Tuning.Combat.Choir.Toys.MusicBox);
        // Run down, it's quiet.
        box.Plays = 0;
        n.Run(0.5);
        Assert.False(box.Playing);
    }

    [Fact]
    public void AMusicBoxNearTheGauntIsntSilenceButOnlyAVoiceTalksItDown()
    {
        var t = Tuning.Enemies.Gaunt;
        var n = new Night(4, speed: 0);
        n.Crew[1] = PlayerMotor.SpawnOnGround(Beside(n, 2, 10), n.Train.Line, n.Train.Dynamics.Distance, P);
        var gaunt = n.World.AddEnemy(id => Gaunt.WokenBy(id, Beside(n, 2, 11), 1, t));
        var box = WoundToy(n, Beside(n, 2, 10, 1), ToyNoise.MusicBox);
        // Silent but for the box: its anger holds where it was.
        n.Run(t.SilenceSeconds * 3);
        Assert.Equal(0, gaunt.Extra2);
        // The box run down: silence, and it climbs.
        box.Plays = 0;
        n.Run(t.SilenceSeconds * 2 + 0.5);
        Assert.True(gaunt.Extra2 >= 1);
    }
}
