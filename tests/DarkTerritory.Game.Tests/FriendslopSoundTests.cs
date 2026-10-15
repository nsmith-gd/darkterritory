using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// K1's #1026 heard (queue #511-#514, notes 781-784): the sense table's things (a knock on the rails, a thrown lamp's glass
/// and oil, a hound in the extinguisher's jet), bringing a friend home (hauled aboard, lifted, put down, thrown), the big
/// finds (a jackpot hefted and set down, a fragile find knocked, the jackpot house's lock picked) and the handcar. Each off
/// what every machine sees, once, where it happened. Each cue is installed here as a short tone (the real takes come from
/// tools/audio/install.py), so what plays can be counted off the mixer.
/// </summary>
public class FriendslopSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    static readonly FacilityTuning F = DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File));
    const double Dt = SimConstants.TickSeconds;

    static readonly string[] Cues =
    [
        "world-rail-knock.knock", "crew-lamp-broken.smash", "crew-lamp-broken.burn~", "cs-hounds-doused.doused",
        "crew-rescue.haul", "crew-rescue.lift", "crew-rescue.set", "crew-rescue.throw",
        "loot-jackpot.lift-iron", "loot-jackpot.lift-bell", "loot-jackpot.lift-wood", "loot-jackpot.set-iron", "loot-jackpot.set-wood", "loot-jackpot.bell",
        "loot-fragile.glass", "loot-fragile.decanter", "loot-fragile.clock", "place-breach.pick~", "place-breach.pick-give",
        "crew-house-door.open", "crew-handcar.creak", "crew-handcar.joint", "crew-handcar.roll~", "crew-handcar.brake~",
        "crew-handcar.set", "crew-handcar.hang", "crew-carry.lamp-set", "crew-carry.crate-lift", "crew-carry.crate-set",
    ];

    /// <summary>A world, the audio with these cues installed (a trailing ~ a loop), and what's been heard.</summary>
    sealed class Bench
    {
        public readonly World World;
        public readonly GameAudio Audio = new(Content);
        public readonly List<(string Name, Double3 At, int Tick)> Heard = [];
        readonly HashSet<int> _seen = [];
        public int Tick;
        public double? Speed;
        public Action? Mirror;

        public Bench(World world)
        {
            World = world;
            foreach (var cue in Cues)
                Audio.Bank.Add(cue.TrimEnd('~'), new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Loop: cue.EndsWith('~'),
                    Duration: 0.1, MaxInstances: 64));
            Audio.PlayerTuning = P;
        }

        public Bench(TrainTuning? train = null)
            : this(new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(train ?? T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000), C))
        {
        }

        public TrainOnLine Train => World.Train;

        /// <summary>One tick: the world, then what's heard with this crew aboard, from car 1's roof.</summary>
        public void Step(params (int Id, PlayerState State)[] crew)
        {
            if (Speed is { } speed)
                Train.Dynamics.Velocity = speed;
            World.BeginTick();
            World.Step(World.Controls);
            World.StepBodies(crew);
            // The night's own step (the host's: its finds marked fragile, banked, the stops stocked).
            World.StepRun([.. crew.Select(c => c.State)]);
            Mirror?.Invoke();
            Audio.CrewStates = crew;
            Audio.Update(World, World.Controls, Listener.At(Train.Frames[1].Origin + Double3.Up * 2, 0), exposed: true, Dt);
            foreach (var v in Audio.Mixer.Voices)
                if (_seen.Add(v.Id) && (Cues.Contains(v.Name) || Cues.Contains(v.Name + "~")))
                    Heard.Add((v.Name, v.Position, Tick));
            Tick++;
        }

        public void Run(double seconds, params (int Id, PlayerState State)[] crew)
        {
            for (int i = 0; i < Math.Round(seconds / Dt); i++)
                Step(crew);
        }

        public int Count(string name) => Heard.Count(h => h.Name == name);

        public bool Playing(string name) => Audio.Mixer.Voices.Any(v => v.Name == name && !v.Stopped && !v.Finished);

        /// <summary>A point on the ground on the line's centre this far ahead of the train, and its line distance.</summary>
        public (Double3 At, double Hint) Ahead(double metres)
        {
            double hint = Train.Dynamics.Distance + metres;
            var at = Train.Line.Sample(hint).Position;
            return (at with { Y = PlayerMotor.GroundAt(at, Train.Line, ref hint) }, hint);
        }
    }

    /// <summary>Held a few ticks and let go: what the hands do to it, as far as anything heard is concerned.</summary>
    static void LetGo(Bench b, params Body[] things)
    {
        foreach (var t in things)
            t.Carrier = 1;
        b.Run(0.1);
        foreach (var t in things)
            t.Carrier = -1;
    }

    // ---- The sense table (note 781).

    [Fact]
    public void SomethingDroppedOnTheRailsKnocksThemAndBesideTheLineItDoesNot()
    {
        var b = new Bench();
        b.World.EnableBodies();
        var (on, hint) = b.Ahead(40);
        var crate = b.World.Bodies.SpawnItem(on + Double3.Up * 2.5, hint, BodyKind.Crate);
        var beside = b.World.Bodies.SpawnItem(on + new Double3(4, 2.5, 0), hint, BodyKind.Crate);
        b.Run(0.1);
        LetGo(b, crate, beside);
        b.Run(3);
        // One knock (senses.json knock: within railWithin of the track's centre), where the crate came down; none for the other.
        var knock = Assert.Single(b.Heard, h => h.Name == "world-rail-knock.knock");
        Assert.True((knock.At - Bodies.WorldCentre(crate, b.Train)).Length < 1, $"the knock is where the crate lies ({knock.At})");
    }

    [Fact]
    public void AThrownLampBreaksWithItsOilAlightAndBurnsWhereItLiesTillItsOut()
    {
        var b = new Bench();
        b.World.EnableBodies();
        var (on, hint) = b.Ahead(40);
        // Off a car's roof onto the ground: brought up short faster than senses.json thrown.lampBreakSpeed.
        var lamp = b.World.Bodies.SpawnItem(on + new Double3(3, 4, 0), hint, BodyKind.Lamp);
        b.Run(0.1);
        LetGo(b, lamp);
        b.Run(2);
        Assert.True(lamp.Broken, "the lamp broke");
        Assert.Equal(1, b.Count("crew-lamp-broken.smash"));
        Assert.Equal(0, b.Count("crew-carry.lamp-set"));
        Assert.True(b.Playing("crew-lamp-broken.burn"), "its oil burning where it lies");
        b.Run(b.World.Senses.Thrown.GroundBurnSeconds);
        Assert.DoesNotContain(b.World.Bodies.All, x => x.Id == lamp.Id);
        Assert.False(b.Playing("crew-lamp-broken.burn"), "burnt out and gone, quiet");
        Assert.Equal(1, b.Count("crew-lamp-broken.smash"));

        // Into a car's fire grid, the lamp's gone the tick it breaks (World.BreakLamp): heard breaking there all the same.
        var room = b.Train.Frames[2].Shape.Interior!.Value;
        var thrown = b.World.Bodies.SpawnCrate(b.Train, 2, new Double3(0, room.Min.Y + 1.5, 0), BodyKind.Lamp);
        b.Run(0.1);
        LetGo(b, thrown);
        b.Run(0.05);
        b.World.Bodies.Remove(thrown);
        b.Run(0.5);
        Assert.Equal(2, b.Count("crew-lamp-broken.smash"));
    }

    [Fact]
    public void AHoundInTheExtinguishersJetIsHeardDousedAndOneBehindTheSprayerIsNot()
    {
        var b = new Bench();
        b.World.EnableBodies();
        const int car = 2;
        var shape = b.Train.Frames[car].Shape;
        var me = PlayerMotor.StandUp(b.Train, car, new Double3(0, shape.RoofHeight, 0), b.Train.Cars[car].FrontDistance, P);
        // Aimed down at the roof ahead, as at a hound there (the spray's cone, senses.json spray.coneDeg, from the eye).
        me.Yaw = 0;
        me.Pitch = -0.55;
        var ext = b.World.Bodies.SpawnCrate(b.Train, car, new Double3(0, shape.RoofHeight + 1, 0), BodyKind.Extinguisher);
        ext.Charge = 1;
        ext.Carrier = 1;
        CinderHound Hound(int id, double z) => Record(new CinderHound(id, 10), car, new Double3(0.2, shape.RoofHeight, z));
        b.Mirror = () => b.World.MirrorEnemies([Hound(31, -2), Hound(32, 2)]);
        b.Run(0.2, (1, me));
        Assert.Equal(0, b.Count("cs-hounds-doused.doused"));
        // The spray at work: its charge going (CrewCarried's own sign someone's spraying).
        for (int i = 0; i < Math.Round(2.0 / Dt); i++)
        {
            ext.Charge -= 0.002;
            b.Step((1, me));
        }
        var doused = b.Heard.Where(h => h.Name == "cs-hounds-doused.doused").ToList();
        // Caught, and held in it two seconds, heard again once (GameAudio.Senses: every HoundDousedEvery).
        Assert.Equal(2, doused.Count);
        var ahead = b.Train.Frames[car].ToWorld(new Double3(0.2, shape.RoofHeight, -2));
        Assert.All(doused, h => Assert.True((h.At - ahead).Length < 1, $"heard at the hound in the jet ({h.At}), not the one behind"));
    }

    static T Record<T>(T e, int attached, Double3 local) where T : Enemy
    {
        e.Restore(SpinePhase.Commit, 2, 3, attached, local, 0, 0, 0, 10, 0);
        return e;
    }

    // ---- Bringing them home (note 782).

    [Fact]
    public void AFriendHauledAboardLiftedPutDownAndThrownIsHeardEachOnce()
    {
        var b = new Bench { Speed = 5 };
        const int car = 1;
        var room = b.Train.Frames[car].Shape.Interior!.Value;
        var helper = PlayerMotor.StandUp(b.Train, car, new Double3(room.Max.X - 0.4, room.Min.Y, 0), b.Train.Cars[car].FrontDistance, P);
        var beside = b.Train.Frames[car].ToWorld(new Double3(room.Max.X + 1.2, 0, 0));
        double hint = b.Train.Cars[car].FrontDistance;
        var friend = PlayerMotor.StandUp(b.Train, PlayerState.World, beside with { Y = PlayerMotor.GroundAt(beside, b.Train.Line, ref hint) }, hint, P);
        b.Run(0.3, (1, helper), (2, friend));
        Assert.Empty(b.Heard);

        // Hauled up (Rescue.HaulAboard): put in the car inboard of the helper, the host's move.
        friend.Parent = car;
        friend.Position = helper.Position + new Double3(-0.45, 0, 0);
        friend.Placed++;
        b.Run(0.3, (1, helper), (2, friend));
        Assert.Equal(1, b.Count("crew-rescue.haul"));

        // Over the shoulder, and put down.
        helper.Flags |= PlayerFlags.Carrying;
        friend.Flags |= PlayerFlags.Carried;
        b.Run(0.3, (1, helper), (2, friend));
        Assert.Equal(1, b.Count("crew-rescue.lift"));
        helper.Flags &= ~PlayerFlags.Carrying;
        friend.Flags &= ~PlayerFlags.Carried;
        b.Run(0.3, (1, helper), (2, friend));
        Assert.Equal(1, b.Count("crew-rescue.set"));

        // Up again and thrown (Rescue.Throw: off their feet as a shove puts them, and the thrower's arm out, KnockKind.Throw).
        helper.Flags |= PlayerFlags.Carrying;
        friend.Flags |= PlayerFlags.Carried;
        b.Run(0.3, (1, helper), (2, friend));
        helper.Flags &= ~PlayerFlags.Carrying;
        friend.Flags = (friend.Flags & ~PlayerFlags.Carried) | PlayerFlags.Shoved;
        b.World.Horseplays.Add(new HorseplayEvent(9_001, b.World.Tick, 1, KnockKind.Throw, 2));
        b.Run(0.3, (1, helper), (2, friend));
        Assert.Equal(2, b.Count("crew-rescue.lift"));
        Assert.Equal(1, b.Count("crew-rescue.throw"));
        Assert.Equal(1, b.Count("crew-rescue.set"));
        Assert.Equal(1, b.Count("crew-rescue.haul"));
    }

    // ---- Big finds (note 783).

    /// <summary>A frontier night stood at a stop with a locked jackpot house, its jackpot only <paramref name="jackpot"/>.</summary>
    static (Bench Bench, int Stop) AtAJackpotHouse(string jackpot)
    {
        var loot = L with
        {
            JackpotHouse = L.JackpotHouse! with { Chance = new() { ["frontier"] = 1 }, Items = ["mantelClock"] },
            Jackpots = L.Jackpots! with
            {
                House = 1,
                Chance = new() { ["frontier"] = 1 },
                Items = new() { [jackpot] = L.Jackpots!.Items[jackpot] },
            },
        };
        World Night(Route route, double at)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 0)), route.Build(), Math.Max(at, 700));
            var world = new World(train, C);
            world.EnableBodies();
            world.EnableRun(R, route, 600, authority: true, F, loot);
            return world;
        }
        for (ulong seed = 1; seed < 100; seed++)
        {
            var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, seed);
            var run = Night(route, 0).Run!;
            for (int k = 0; k < run.Stops.Count; k++)
                if (run.LockedHouse(k) >= 0)
                    return (new Bench(Night(route, (run.Stops[k].Start + run.Stops[k].End) / 2)) { Speed = 0 }, k);
        }
        throw new InvalidOperationException("no frontier night has a village house to lock");
    }

    [Theory]
    [InlineData("safe", "loot-jackpot.lift-iron", "loot-jackpot.set-iron", false)]
    [InlineData("churchBell", "loot-jackpot.lift-bell", "loot-jackpot.set-wood", true)]
    public void AJackpotIsHeftedBetweenTwoAndSetDownAsWhatItIs(string item, string lift, string set, bool rings)
    {
        var (b, k) = AtAJackpotHouse(item);
        b.World.Run!.Stock(b.World.Bodies, k);
        var jackpot = b.World.Bodies.All.First(Sim.Run.Run.IsJackpot);
        Assert.Equal(item, b.World.Run.FindOf(jackpot)!.Value.Item);
        // Two crewmates stood either side of it in the house, an end each (spec D.2, T43): lifted between their hands, let go.
        var at = Bodies.WorldCentre(jackpot, b.Train);
        double hint = b.Train.Dynamics.Distance;
        (int, PlayerState) End(int id, double x)
        {
            var feet = at + new Double3(x, 0, 0);
            return (id, PlayerMotor.StandUp(b.Train, PlayerState.World, feet with { Y = PlayerMotor.GroundAt(feet, b.Train.Line, ref hint) }, hint, P));
        }
        var crew = new[] { End(1, -0.6), End(2, 0.6) };
        b.Run(0.5, crew);
        jackpot.Carrier = 1;
        jackpot.Second = 2;
        b.Run(0.5, crew);
        Assert.True(jackpot.Lifted, "up between the two");
        jackpot.Carrier = jackpot.Second = -1;
        b.Run(2, crew);
        Assert.Equal(1, b.Count(lift));
        Assert.Equal(1, b.Heard.Count(h => h.Name.StartsWith("loot-jackpot.lift", StringComparison.Ordinal)));
        Assert.Equal(0, b.Count("crew-carry.crate-lift"));
        Assert.Equal(1, b.Count(set));
        Assert.Equal(0, b.Count("crew-carry.crate-set"));
        Assert.Equal(rings ? 1 : 0, b.Count("loot-jackpot.bell"));
    }

    [Fact]
    public void AFragileFindBreaksAtEachKnockAndNotSetDownGently()
    {
        var (b, k) = AtAJackpotHouse("safe");
        var (on, hint) = b.Ahead(30);
        int owner = Sim.Run.Run.LootOwner(k, Sim.Run.Run.ExtraContainer);
        var gentle = b.World.Bodies.SpawnLoot(on + new Double3(0, 0.95, 0), hint, owner, L.Radius);
        var dropped = b.World.Bodies.SpawnLoot(on + new Double3(2, 4, 0), hint, owner, L.Radius);
        b.Run(2);
        Assert.Equal((0, 1), (gentle.Knocks, dropped.Knocks));
        var clock = Assert.Single(b.Heard, h => h.Name == "loot-fragile.clock");
        Assert.True((clock.At - Bodies.WorldCentre(dropped, b.Train)).Length < 1, "heard where it was knocked");
        dropped.Pbd.Particles[0].SetVelocity(new Double3(0, -9, 0), Dt);
        dropped.Pbd.Wake();
        b.Run(1);
        Assert.Equal(2, dropped.Knocks);
        Assert.Equal(2, b.Count("loot-fragile.clock"));
        Assert.Equal(0, b.Count("loot-fragile.glass"));
    }

    [Fact]
    public void TheJackpotHousesLockIsHeardPickedWhileTheWrenchWorksAndGivingAsItOpens()
    {
        var (b, k) = AtAJackpotHouse("safe");
        var walls = b.Train.Walls!;
        int key = b.World.Run!.LockedDoors(k).First();
        Assert.True(walls.Locked(key));
        b.Run(0.5);
        for (int i = 1; i <= 60; i++)
        {
            walls.SetPicking(key, i / 120.0);
            b.Step();
        }
        Assert.True(b.Playing("place-breach.pick"), "the wrench at the lock");
        // Left half done: it goes quiet.
        b.Run(0.5);
        Assert.False(b.Playing("place-breach.pick"));
        for (int i = 60; i <= 120; i++)
        {
            walls.SetPicking(key, i / 120.0);
            b.Step();
        }
        walls.SetShut(key, false);
        walls.SetPicking(key, 0);
        b.Run(0.3);
        Assert.Equal(1, b.Count("place-breach.pick-give"));
        Assert.Equal(1, b.Count("crew-house-door.open"));
        Assert.False(b.Playing("place-breach.pick"));
    }

    // ---- The handcar (note 784).

    [Fact]
    public void TheHandcarIsHeardSetDownHungBackRolledOverTheJointsPumpedAndBraked()
    {
        // Not parked with nobody aboard, so it coasts as it's let go.
        var t = T with { Handcar = T.Handcar with { Always = true, Parked = false } };
        var b = new Bench(t) { Speed = 0 };
        var h = b.Train.Handcar!;
        b.Run(0.5);
        Assert.Empty(b.Heard);
        Assert.True(b.Train.SetHandcarOnRails());
        b.Run(0.3);
        Assert.Equal(1, b.Count("crew-handcar.set"));
        Assert.True(b.Train.StowHandcar());
        b.Run(0.3);
        Assert.Equal(1, b.Count("crew-handcar.hang"));
        Assert.Equal(0, b.Count("crew-handcar.creak") + b.Count("crew-handcar.joint"));

        // On the rails and running back down the line at 6 m/s for 5 s: about 28 m.
        Assert.True(b.Train.SetHandcarOnRails());
        b.Train.HandcarRake!.Velocity = -6;
        double from = b.Train.Cars[h.Id].FrontDistance;
        b.Run(5);
        double run = Math.Abs(b.Train.Cars[h.Id].FrontDistance - from);
        Assert.InRange(run, 20, 31);
        Assert.True(b.Playing("crew-handcar.roll"), "rolling");
        // The handle at the top and bottom of its swing: twice a stroke (handcar.strokeMetres).
        Assert.InRange(b.Count("crew-handcar.creak"), (int)(2 * run / t.Handcar.StrokeMetres) - 1, (int)(2 * run / t.Handcar.StrokeMetres) + 1);
        // Each of its two axles over every rail joint it passed (12 m rails).
        Assert.InRange(b.Count("crew-handcar.joint"), 2 * (int)Math.Floor(run / 12), 2 * (int)Math.Ceiling(run / 12));
        Assert.False(b.Playing("crew-handcar.brake"));
        // The brake on (the lever as the host's record has it, a client's copy): its shoe squeals while it runs.
        b.Mirror = () => h.Brake = 1;
        b.Run(0.5);
        Assert.True(b.Playing("crew-handcar.brake"), "the brake shoe on the wheels");
        // Stopped, nothing rolls or squeals.
        b.Mirror = () => b.Train.HandcarRake!.Velocity = 0;
        b.Run(0.5);
        Assert.False(b.Playing("crew-handcar.roll"));
        Assert.False(b.Playing("crew-handcar.brake"));
    }
}
