using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Tower Jaw (GDD §21, App. A.6, B.6; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 363). Rule: hear the chewing,
/// find the tower, get it off before it falls. It gnaws a coaling tower's leg through; down, the tower's chute is empty and
/// its wreck closes the line (a train run into it is stopped there) until the crew clears it by hand; it bites but never
/// kills; blows drive it off (it comes back to finish) or kill it.
/// </summary>
public class TowerJawTests
{
    static readonly TowerJawTuning J = Tuning.Enemies.TowerJaw;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
    static readonly RouteFeature Tower = Frontier.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);

    sealed class Stop
    {
        public readonly World World;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly List<EnemyEvent> Events = new();
        public TrainControls Controls = new() { Reverser = 1, Brake = 1 };

        public Stop(double front)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Frontier.Build(), front, Tuning.Boiler);
            World = new World(train, Tuning.Combat);
            World.EnableRun(Tuning.Run, Frontier, 600, authority: true);
            World.EnableEnemies(Tuning.Enemies, Frontier, 1, crew: 4, authority: true);
        }

        public TrainOnLine Train => World.Train;
        public int Facility => World.Run!.Facilities.ToList().IndexOf(Tower);

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null, double? hold = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                if (hold is { } v)
                    Train.Dynamics.Velocity = v;
                World.BeginTick();
                var intents = Crew.Keys.ToDictionary(id => id, id => intent?.Invoke(id) ?? default);
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, intents[id], id);
                    Crew[id] = s;
                }
                World.Step(Controls);
                World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys);
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[id] = s;
                }
                Events.AddRange(World.EnemyEvents);
            }
        }

        public PlayerState Ground(Double3 at, Double3? toward = null)
        {
            var s = PlayerMotor.SpawnOnGround(at, Train.Line, Train.Dynamics.Distance, P);
            if (toward is { } to)
            {
                var d = to - at;
                s.Yaw = DMath.Atan2(-d.X, -d.Z);
            }
            return s;
        }
    }

    /// <summary>Far back down the line from the tower (nobody about), the beaver at its leg <paramref name="gnawed"/> through.</summary>
    static (Stop Stop, TowerJaw Jaw) AtTheTower(double gnawed = 0, double back = 2000)
    {
        var s = new Stop(Tower.Start - back);
        var jaw = s.World.AddEnemy(id => TowerJaw.AtCoalingTower(id, s.World, s.Facility, J, gnawed));
        return (s, jaw);
    }

    [Fact]
    public void LeftAloneItGnawsTheTowerDownAndItsChuteIsGone()
    {
        var (s, jaw) = AtTheTower();
        Assert.True(s.World.Run!.ChuteLeft(s.Facility) > 0);
        double through = J.GnawFor(RouteTier.Frontier);
        s.Run(through - 5);
        Assert.Equal(TowerJawMode.Gnaw, jaw.Mode);
        Assert.InRange(jaw.Gnawed, 0.9, 1);
        s.Run(6);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        Assert.Equal(0, s.World.Run!.ChuteLeft(s.Facility));
        Assert.Equal(1, s.World.TowersDown);
        Assert.NotNull(jaw.Blocks);
    }

    [Fact]
    public void ItsFallIsAPunishOutOfTheGroanAndItsRecordFollows()
    {
        // Note 1045 (note 1036's finding): it gnaws in Alert, and its fall asked for Punish from there, which the spine refuses
        // (App. A.1): the tower came down with no punish event, no bookmark, no attribution record. Its telegraph is the groan
        // (tower-jaw.md §5: the last groanSeconds of gnawing), then the fall is its commit and punish.
        var (s, jaw) = AtTheTower();
        double through = J.GnawFor(RouteTier.Frontier);
        // Someone watching it from out past its notice and the fall's reach, for the bookmark's picture.
        s.Crew[1] = s.Ground(jaw.Local + (jaw.Local - jaw.FallsAt).Normalized * 25, jaw.Local);
        var groanAt = new List<(TowerJawMode Mode, double Gnawed)>();
        for (int i = 0; i < (through + 2) * SimConstants.TickRate && jaw.Mode != TowerJawMode.Wreck; i++)
        {
            int before = s.Events.Count;
            s.Run(SimConstants.TickSeconds);
            if (s.Events.Skip(before).Any(e => e.To == SpinePhase.Telegraph))
                groanAt.Add((jaw.Mode, jaw.Gnawed));
        }
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        var mine = s.Events.Where(e => e.EnemyId == jaw.Id).ToList();
        // Its telegraph, still at its gnawing, the groan's length before it's through.
        var (mode, gnawed) = Assert.Single(groanAt);
        Assert.Equal(TowerJawMode.Gnaw, mode);
        Assert.InRange(gnawed, 1 - (J.GroanSeconds + 0.1) / through, 1 - (J.GroanSeconds - 0.1) / through);
        var commit = Assert.Single(mine, e => e.To == SpinePhase.Commit);
        Assert.Equal(SpinePhase.Telegraph, commit.From);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds, $"committed {commit.SecondsInFrom:0.00} s into the groan");
        var punish = Assert.Single(mine, e => e.To == SpinePhase.Punish);
        Assert.Equal(SpinePhase.Commit, punish.From);
        Assert.Equal(SpinePhase.Punish, jaw.Phase);
        // The night's record: an attribution line and an auto-bookmark (App. A.9, D.12).
        Assert.Contains(s.World.Attribution.Of(IncidentKind.Punished), i => i.What.Contains("tower jaw", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(s.World.Bookmarks.All);
    }

    [Fact]
    public void RearedAtSomeoneDuringTheGroanItStillLungesOnlyAfterItsThreat()
    {
        // Note 1045: come near in the groan, its rear joins the telegraph already running; the lunge still waits its own threatSeconds.
        var (s, jaw) = AtTheTower(gnawed: 0.95); // as far as it's put (TowerJaw.Make), the groan about a second off
        s.Run(1.5);
        Assert.Equal(SpinePhase.Telegraph, jaw.Phase);
        s.Crew[1] = s.Ground(jaw.Local + new Double3(J.LungeWithin - 0.5, 0, 0), jaw.Local);
        int before = s.Events.Count;
        s.Run(J.ThreatSeconds - 0.1);
        Assert.DoesNotContain(s.Events.Skip(before), e => e.EnemyId == jaw.Id && e.To == SpinePhase.Commit);
        s.Run(0.3);
        Assert.Contains(s.Events.Skip(before), e => e.EnemyId == jaw.Id && e.To == SpinePhase.Commit);
        Assert.True(s.Crew[1].Alive);
        Assert.Equal(0, s.World.TowersDown); // rearing, it isn't gnawing
    }

    [Fact]
    public void ATrainRunIntoTheWreckIsStoppedThereAndHurtByItsSpeed()
    {
        var (s, jaw) = AtTheTower(gnawed: 0.95, back: 400);
        s.Run(0.05 * J.GnawFor(RouteTier.Frontier) + 0.5);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        double along = jaw.Blocks!.Value;
        s.Controls = new TrainControls { Reverser = 1, Throttle = 1 };
        s.Train.Dynamics.Velocity = 12;
        s.Run(60);
        Assert.InRange(s.Train.Dynamics.Distance, along - J.WreckHalf - 0.5, along - J.WreckHalf + 0.01);
        Assert.True(s.Train.Vehicles[0].Integrity < 1);
        Assert.False(s.World.Derailed);
        // Note 996: dented, never broken down (12 m/s took (11)² × 0.06 of it, the whole engine, before).
        Assert.Equal(J.WreckLeaves, s.Train.Vehicles[0].Integrity, 9);
        Assert.NotEqual(FailStage.Broken, Failing.Stage(s.Train, 0));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(6)]
    public void NoFallLeavesTheLineImpassable(double speed)
    {
        // Note 996 (the director's playtest of 23 Oct, line 1: "we ran right into a coaling tower ... Engine just breaks down
        // immediately, and now we're perma-stuck"): run into the wreck at up to the fastest tier's line speed (20 m/s), the
        // engine still pulls, one crewmate clears it by hand, and the train runs on past where it lay.
        var (s, jaw) = AtTheTower(gnawed: 0.95, back: 400);
        s.Run(0.05 * J.GnawFor(RouteTier.Frontier) + 0.5);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        double along = jaw.Blocks!.Value;
        s.Controls = new TrainControls { Reverser = 1, Throttle = 1 };
        s.Train.Dynamics.Velocity = speed;
        s.Run(120);
        Assert.InRange(s.Train.Dynamics.Distance, along - J.WreckHalf - 0.5, along - J.WreckHalf + 0.01);
        Assert.True(s.Train.Vehicles[0].Integrity >= J.WreckLeaves - 1e-9, $"{s.Train.Vehicles[0].Integrity}");
        Assert.True(Failing.Power(s.Train.Dynamics.Tuning.Failing, s.Train.Vehicles[0]) > 0);
        // One crewmate at it, Use held: clearCrewSeconds and it's gone.
        s.Crew[1] = s.Ground(jaw.FallsAt + new Double3(1.5, 0, 0));
        s.Run(J.ClearCrewSeconds + 1, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(jaw.Gone);
        s.Crew.Clear();
        s.Run(60);
        Assert.True(s.Train.Dynamics.Distance > along + J.WreckHalf + 20, $"the train at {s.Train.Dynamics.Distance:0}, the wreck was at {along:0}");
    }

    [Fact]
    public void ItsFallDentsACarStoodUnderItNeverBreaksIt()
    {
        // Note 996 (line 26: "it didn't do anything to the car ... an unrealized threat"): the train stood at the tower, its
        // cars under where it comes down are dented crushCar, never below wreckLeaves; the cars clear of it untouched.
        var probe = AtTheTower(gnawed: 0.95).Jaw;
        double spout = probe.Blocks!.Value;
        var s = new Stop(spout + 20);
        var jaw = s.World.AddEnemy(id => TowerJaw.AtCoalingTower(id, s.World, s.Facility, J, 0.95));
        s.Train.Dynamics.Velocity = 0;
        s.Run(0.05 * J.GnawFor(RouteTier.Frontier) + 0.5, hold: 0);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        var hit = s.Train.Vehicles.Where(v => v.Integrity < 1).ToList();
        Assert.NotEmpty(hit);
        Assert.All(hit, v => Assert.Equal(1 - J.CrushCar, v.Integrity, 9));
        Assert.Contains(s.Train.Vehicles, v => v.Integrity == 1);
    }

    [Fact]
    public void TheCrewClearsTheWreckByHandFasterTogether()
    {
        var (s, jaw) = AtTheTower(gnawed: 0.95);
        s.Run(0.05 * J.GnawFor(RouteTier.Frontier) + 0.5);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        s.Crew[1] = s.Ground(jaw.FallsAt + new Double3(1.5, 0, 0));
        s.Crew[2] = s.Ground(jaw.FallsAt + new Double3(-1.5, 0, 0));
        s.Run(J.ClearCrewSeconds / 2 - 1, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.False(jaw.Gone);
        s.Run(2, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(jaw.Gone);
        Assert.Equal(1, s.World.TowersCleared);
    }

    [Fact]
    public void ComeCloseAndItRearsAndBitesButNeverKills()
    {
        var (s, jaw) = AtTheTower();
        s.Crew[1] = s.Ground(jaw.Local + new Double3(2, 0, 0)) with { Health = 30 };
        s.Run(20);
        Assert.True(s.Crew[1].Alive);
        Assert.True(s.Crew[1].Health < 30);
        Assert.Contains(s.Events, e => e.Kind == EnemyKind.TowerJaw && e.To == SpinePhase.Commit);
        Assert.All(s.Events.Where(e => e.To == SpinePhase.Commit), e => Assert.True(e.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds - 1e-9));
    }

    [Fact]
    public void BlowsDriveItOffAndItComesBackToFinishTheJob()
    {
        var (s, jaw) = AtTheTower(gnawed: 0.5);
        double gnawed = jaw.Gnawed;
        void Blow()
        {
            var at = jaw.Local;
            s.Crew[1] = s.Ground(at + new Double3(1.4, 0, 0), toward: at) with { Health = P.Health };
            s.Run(Tuning.Enemies.Melee.SwingSeconds + 0.05, id => new PlayerIntent { Actions = PlayerActions.Swing });
        }
        for (int i = 0; i < J.DriveOffBlows && jaw.Mode != TowerJawMode.Away; i++)
            Blow();
        Assert.Equal(TowerJawMode.Away, jaw.Mode);
        s.Crew.Remove(1);
        s.Run(J.AwaySeconds + 30);
        Assert.Equal(TowerJawMode.Gnaw, jaw.Mode);
        Assert.True(jaw.Gnawed > gnawed);
    }

    [Fact]
    public void TwelveBlowsKillItAndTheTowerStands()
    {
        var (s, jaw) = AtTheTower(gnawed: 0.3);
        for (int i = 0; i < 40 && !jaw.Gone; i++)
        {
            var at = jaw.Local;
            // Two crewmates, by turns, so the drive-off window never fills.
            int id = i % 2 + 1;
            s.Crew.Clear();
            s.Crew[id] = s.Ground(at + new Double3(id == 1 ? 1.4 : -1.4, 0, 0), toward: at);
            s.Run(Tuning.Enemies.Melee.SwingSeconds + 0.05, x => new PlayerIntent { Actions = PlayerActions.Swing });
            if (jaw.Mode == TowerJawMode.Away)
                s.Run(J.AwaySeconds + 30);
        }
        Assert.True(jaw.Gone);
        Assert.True(s.World.Run!.ChuteLeft(s.Facility) > 0);
        Assert.Equal(0, s.World.TowersDown);
    }
    /// <summary>A client that joins now: a fresh world, given the host's records once (note 1029).</summary>
    static World Joiner(World host, FacilityTuning? facilities = null)
    {
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), host.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, host.Run!.Route, 600, authority: false, facilities);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        return client;
    }

    [Fact]
    public void AJoinerAfterTheFallSeesTheTowerDownAndAfterTheClearingCleared()
    {
        // Note 1029 (tower-jaw.md "Not yet": "a client joining after the clearing sees the tower standing"): the tower's state
        // is the run's, on the wire, not the scene's memory of a creature it saw.
        var (s, jaw) = AtTheTower(gnawed: 0.95);
        Assert.Equal(StructureDown.Standing, Joiner(s.World).Run!.Tower(s.Facility));
        s.Run(0.05 * J.GnawFor(RouteTier.Frontier) + 0.5);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        Assert.Equal(StructureDown.Fallen, s.World.Run!.Tower(s.Facility));
        var late = Joiner(s.World);
        Assert.Equal(StructureDown.Fallen, late.Run!.Tower(s.Facility));
        Assert.Equal(0, late.Run.ChuteLeft(s.Facility));
        s.Crew[1] = s.Ground(jaw.FallsAt + new Double3(1.5, 0, 0));
        s.Run(J.ClearCrewSeconds + 1, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(jaw.Gone);
        Assert.Equal(StructureDown.Cleared, s.World.Run.Tower(s.Facility));
        s.Run(5);
        Assert.DoesNotContain(s.World.ActiveEnemies, e => e is TowerJaw);
        Assert.Equal(StructureDown.Cleared, Joiner(s.World).Run!.Tower(s.Facility));
    }

    /// <summary>A foundry's crane, the train stood down its spur, Tower Jaw at the gantry's leg <paramref name="gnawed"/> through.</summary>
    static (FacilityTests.Stop Stop, Crane Crane, TowerJaw Jaw) AtTheGantry(double gnawed)
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crane);
        stop.World.EnableEnemies(Tuning.Enemies, stop.World.Run!.Route, 1, crew: 4, authority: true);
        var crane = stop.Site.Crane!;
        var jaw = stop.World.AddEnemy(id => TowerJaw.AtGantry(id, stop.World, stop.Site.Index, crane, J, gnawed));
        return (stop, crane, jaw);
    }

    [Fact]
    public void GnawedThroughTheGantryFallsAcrossItsTrackAndClosesIt()
    {
        // Note 1029 (tower-jaw.md "Not yet": "the loading crane's gantry doesn't lean or fall; the crane's Wrecked only stops
        // it"): it comes down across its loading track at its middle, the crane dead, and a train run at it down the spur is
        // stopped short of it, as at the coaling tower's.
        var (stop, crane, jaw) = AtTheGantry(0.95);
        Assert.Equal(stop.Site.Spur, jaw.BlocksPath);
        stop.Step(0.05 * J.GnawFor(RouteTier.Frontier) + 0.5, []);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        Assert.True(crane.Wrecked);
        Assert.Equal(StructureDown.Fallen, stop.World.Run!.Gantry(stop.Site.Index));
        Assert.True(((jaw.FallsAt - crane.OverTrack) with { Y = 0 }).Length < 1e-6);
        double along = jaw.Blocks!.Value;
        Assert.InRange(along, stop.Train.Line.Branches[stop.Site.Spur].Toe, stop.Train.Line.Branches[stop.Site.Spur].End);
        stop.StandAt(along - J.WreckHalf - 30);
        for (int i = 0; i < 20 * SimConstants.TickRate; i++)
        {
            stop.Train.Dynamics.Velocity = 3;
            stop.World.BeginTick();
            stop.World.Step(new TrainControls { Reverser = 1 });
        }
        Assert.Equal(stop.Site.Spur, stop.Train.Dynamics.Path);
        Assert.InRange(stop.Train.Dynamics.Distance, along - J.WreckHalf - 0.5, along - J.WreckHalf + 0.2);
    }

    [Fact]
    public void AJoinerAfterTheGantryFallsSeesItDownAndTheCraneDeadThenCleared()
    {
        var (stop, crane, jaw) = AtTheGantry(0.95);
        stop.Step(0.05 * J.GnawFor(RouteTier.Frontier) + 0.5, []);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        var late = Joiner(stop.World, FacilityTests.F);
        Assert.Equal(StructureDown.Fallen, late.Run!.Gantry(stop.Site.Index));
        Assert.True(late.Run.Sites[stop.Site.Index]!.Crane!.Wrecked);
        stop.Crew.Add(stop.OnTheGround(jaw.FallsAt + new Double3(1.5, 0, 0)));
        stop.Step(J.ClearCrewSeconds + 1, [new PlayerIntent { Buttons = PlayerButtons.Use }]);
        Assert.True(jaw.Gone);
        Assert.Equal(StructureDown.Cleared, stop.World.Run!.Gantry(stop.Site.Index));
        Assert.True(crane.Wrecked);
        late = Joiner(stop.World, FacilityTests.F);
        Assert.Equal(StructureDown.Cleared, late.Run!.Gantry(stop.Site.Index));
        Assert.Equal(StructureDown.Standing, late.Run.Tower(stop.Site.Index));
        Assert.True(late.Run.Sites[stop.Site.Index]!.Crane!.Wrecked);
    }
}
