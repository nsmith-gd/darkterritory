using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
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
}
