using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's decision of 2026-10-06 (GDD App. F.1 "abandoned player"; note 266): a player the train leaves behind feels
/// the world close in, theirs alone. The ramp runs over the tuned window, a creature commits through its telegraph by the
/// deadline, getting back aboard lets them go, and the crew's own night (the director's budget) is untouched.
/// </summary>
public class AbandonedTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly AbandonedTuning A = Tuning.Enemies.Abandoned;

    sealed class Night
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public readonly List<(int Id, PlayerState State)> Crew = [];
        public readonly List<EnemyEvent> Events = [];
        /// <summary>The train held at this speed (m/s; negative, setting back).</summary>
        public double Speed;

        /// <param name="director">The director at work as on any night; off, nothing but the abandonment sends anything.</param>
        public Night(bool director, double speed = 12, bool leftAlive = true)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
            double gate = route.GateOr(Tuning.Route.YardLength);
            // Km 1: a kilometre past the gate.
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), gate + 1_000, Tuning.Boiler);
            Train.Dynamics.Velocity = Speed = speed;
            World = new World(Train, Tuning.Combat);
            World.EnableBodies();
            World.EnableEnemies(Tuning.Enemies, route, 7, 2, authority: true);
            if (!director)
                World.Insist = [];
            Crew.Add((0, PlayerMotor.SpawnInCab(Train, P)));
            // Stepped off the back as it went: on the ballast just behind the rear car.
            double at = Train.Dynamics.RearDistance - 4;
            var t = Train.Line.Sample(at);
            var left = PlayerMotor.SpawnOnGround(t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 3, Train.Line, at, P);
            if (!leftAlive)
                left = left with { Health = 0, Death = DeathCause.Cold };
            Crew.Add((1, left));
        }

        public PlayerState Left => Crew[1].State;

        public void Step(double seconds, Action<double>? each = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c].State;
                    World.CrewAct(ref s, default, c);
                    Crew[c] = (c, s);
                }
                World.Step(new TrainControls { Reverser = Speed < 0 ? -1 : 1 });
                Train.Dynamics.Velocity = Speed;
                World.ApplyDamage(id => Crew[id].State, (id, s) => Crew[id] = (id, s), [0, 1]);
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c].State;
                    PlayerMotor.Step(ref s, default, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[c] = (c, s);
                }
                Events.AddRange(World.EnemyEvents);
                each?.Invoke(World.Abandonment!.SecondsOf(1));
            }
        }
    }

    [Fact]
    public void APlayerLeftAtKm1IsHuntedTheDreadRampsAndACreatureCommitsByTheDeadline()
    {
        var night = new Night(director: false);
        var left = night.World.Abandonment!;
        double firstSecond = -1, clock = 0;
        var dread = new List<Dread>();
        bool lampOutEarly = false;
        night.Step(A.HuntAt + 40, s =>
        {
            clock += SimConstants.TickSeconds;
            if (s > 0 && firstSecond < 0)
                firstSecond = clock;
            var d = Abandonment.DreadAt(s, A);
            if (dread.Count == 0 || dread[^1] != d)
                dread.Add(d);
            if (s > 0 && s < A.LampOutAt - 0.1 && left.LampOut(1))
                lampOutEarly = true;
        });
        // Targeted within seconds of the train pulling the gap open past strandBeyond (at 12 m/s, five or so).
        Assert.InRange(firstSecond, 0.1, 10);
        Assert.Equal([Dread.None, Dread.Closing, Dread.Hunted], dread.Take(3));
        Assert.False(lampOutEarly);
        Assert.True(left.LampOut(1) || !night.Left.Alive, "the lamp's out by now");
        // A pack sent after them, nobody else's, and one of them telegraphed and then committed, by the deadline.
        var hunters = night.Events.Where(e => e.Kind == EnemyKind.Ribbit).ToList();
        var telegraph = hunters.FirstOrDefault(e => e.To == SpinePhase.Telegraph);
        var commit = hunters.FirstOrDefault(e => e.To == SpinePhase.Commit);
        Assert.NotEqual(default, telegraph);
        Assert.NotEqual(default, commit);
        Assert.True(commit.Tick >= telegraph.Tick + (uint)(Tuning.Enemies.MinReactionSeconds * SimConstants.TickRate) - 1, "the telegraph came first, a full window");
        double committedAt = commit.Tick * SimConstants.TickSeconds - firstSecond;
        Assert.InRange(committedAt, A.HuntAt, A.HuntAt + 25);
        // And the cold's coming on faster than the night's own.
        Assert.True(night.Left.Cold > clock * 1.5 || !night.Left.Alive, $"cold {night.Left.Cold:0} after {clock:0} s");
    }

    [Fact]
    public void BackAboardInTheWindowTheyreLetGoAndTheHuntersLoseThem()
    {
        var night = new Night(director: false);
        var left = night.World.Abandonment!;
        night.Step(A.HuntAt + 10);
        Assert.Equal(Dread.Hunted, left.DreadOf(1));
        Assert.Contains(night.World.ActiveEnemies, e => e.Quarry == 1);
        // Fetched: up on a roof.
        night.Crew[1] = (1, PlayerMotor.SpawnOnRoof(night.Train, 2, 0, P));
        night.Step(1);
        Assert.Equal(0, left.SecondsOf(1));
        Assert.Equal(Dread.None, left.DreadOf(1));
        Assert.DoesNotContain(night.World.ActiveEnemies, e => e.Quarry == 1);
        Assert.True(night.Left.Alive);
    }

    [Fact]
    public void ATrainComingBackForThemHoldsTheClock()
    {
        var night = new Night(director: false);
        var left = night.World.Abandonment!;
        night.Step(20);
        Assert.True(left.SecondsOf(1) > 0);
        // Setting back for them (T96): the clock holds while it closes, and they're let go once it's alongside.
        night.Speed = -6;
        night.Step(3);
        double held = left.SecondsOf(1);
        Assert.True(left.All.Single().Held, "held while the train comes back");
        night.Step(5);
        Assert.Equal(held, left.SecondsOf(1), 6);
        night.Step(30);
        Assert.Equal(0, left.SecondsOf(1));
    }

    [Fact]
    public void TheCrewsDirectorBudgetIsTheSameWhoeverIsLeftBehind()
    {
        // The same night twice: once with the second crewmate left alive on the ballast at km 1 (hunted), once with them lying
        // dead there (no one to hunt). The director's spending is the crew's, and the same in both.
        var hunted = new Night(director: true);
        var dead = new Night(director: true, leftAlive: false);
        hunted.Step(150);
        dead.Step(150);
        Assert.True(hunted.Events.Any(e => e.Kind == EnemyKind.Ribbit), "the hunt went on");
        Assert.Equal(dead.World.Director!.Budget, hunted.World.Director!.Budget);
        Assert.Equal(dead.World.Director.Spent, hunted.World.Director.Spent);
        Assert.Equal(dead.World.Director.Log.Select(l => (l.Tick, l.Kind)), hunted.World.Director.Log.Select(l => (l.Tick, l.Kind)));
    }

    [Fact]
    public void TheFiguresAreTheSameOnEveryMachineAndCloseIn()
    {
        var line = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1).Build();
        var at = line.Sample(3_000).Position;
        var early = Abandonment.FiguresOf(new Abandoned(1, A.FiguresFrom + 1, at, false), A, line);
        var late = Abandonment.FiguresOf(new Abandoned(1, A.DeadlyAt, at, false), A, line);
        Assert.Empty(Abandonment.FiguresOf(new Abandoned(1, A.FiguresFrom - 1, at, false), A, line));
        Assert.True(late.Count > early.Count);
        Assert.Equal(A.Figures, late.Count);
        double Mean(List<(Double3 Feet, Double3 Facing)> f) => f.Average(x => ((x.Feet - at) with { Y = 0 }).Length);
        Assert.True(Mean(late) < Mean(early));
        Assert.Equal(late, Abandonment.FiguresOf(new Abandoned(1, A.DeadlyAt, at, false), A, line));
    }
}
