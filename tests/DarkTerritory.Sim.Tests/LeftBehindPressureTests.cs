using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T128 (build 1121; ARCHITECTURE §8 note 273): a crewmate the train leaves behind feels the world close in (their own
/// pressure, and hunts sent at them, bigger and closer each time), and the forts are safe from creatures all night (GDD §9).
/// </summary>
public class LeftBehindPressureTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly AbandonedTuning A = Tuning.Enemies.Director.Abandoned;
    const double Yard = 600;

    sealed class Night
    {
        public readonly World World;
        public readonly Route.Route Route;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public double Speed;

        public Night(double at, double speed)
        {
            Route = new Route.Route("t", RouteTier.Frontier, 1, new LineDefinition("t", [new TrackSegment(20_000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Route.Build(), at);
            Speed = speed;
            train.Dynamics.Velocity = speed;
            World = new World(train, Tuning.Combat);
            World.EnableEnemies(Tuning.Enemies, Route, 1, crew: 2, authority: true);
            World.EnableRun(Tuning.Run, Route, Yard, authority: true);
            // Under way, well past the night's grace.
            World.Run!.Resume(900, -1, 0, 0);
        }

        public TrainOnLine Train => World.Train;
        public Director Director => World.Director!;

        public PlayerState Ground(double along, double lateral = 4)
        {
            var rail = Train.Line.Sample(RailLine.MainPath, along);
            var right = Double3.Cross(rail.Tangent, Double3.Up).Normalized;
            return PlayerMotor.SpawnOnGround(rail.Position + right * lateral, Train.Line, along, P);
        }

        public void Run(double seconds, Action? each = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, default, id);
                    Crew[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys);
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    PlayerMotor.Step(ref s, default, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[id] = s;
                }
                each?.Invoke();
            }
        }

        /// <summary>Whatever's hunting them, gone (they got away from it), so the next one can come.</summary>
        public void Shake() { foreach (var e in World.ActiveEnemies.Where(e => e is Ribbit or Gaunt)) e.Dismiss(); }
    }

    [Fact]
    public void LeftBehindTheWorldClosesInHuntAfterHuntEachBiggerAndCloser()
    {
        var n = new Night(5_000, 12);
        n.Crew[1] = n.Ground(4_600);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        var pressure = new List<double>();
        n.Run(240, () =>
        {
            if (n.World.Tick % SimConstants.TickRate == 0)
                pressure.Add(n.Director.AbandonedPressure(1));
            // They get away from each pack a few seconds after it finds them.
            if (n.Director.Hunts.Count > 0 && (n.World.Tick - n.Director.Hunts[^1].Tick) == 8 * SimConstants.TickRate)
                n.Shake();
        });
        var hunts = n.Director.Hunts;
        Assert.True(hunts.Count >= 4, $"{hunts.Count} hunts in 4 minutes left behind");
        Assert.All(hunts, h => Assert.Equal(1, h.Player));
        Assert.InRange(hunts[0].Seconds, 20, 90);
        Assert.Equal(A.Pack[0], hunts[0].Size);
        for (int i = 1; i < hunts.Count; i++)
        {
            Assert.True(hunts[i].Size >= hunts[i - 1].Size, "never smaller");
            Assert.True(hunts[i].Seconds - hunts[i - 1].Seconds <= hunts[1].Seconds - hunts[0].Seconds + 1, "never slower to come than the second");
        }
        Assert.Equal(Math.Min(A.Pack[1], A.Pack[0] + hunts.Count - 1), hunts[^1].Size);
        // The driver aboard is nobody's hunt; nor did the train's own director spend on them.
        Assert.True(n.Crew[2].Alive);
        Assert.True(n.Crew[1].Alive, "they needn't die at once");
    }

    [Fact]
    public void FromTheThirdHuntAGauntWokenOnThemComesWithThePack()
    {
        // Note 296 (note 273's caveat: "only the Ribbits hunt so far"): from hunt gauntFrom on, a Gaunt comes too, already
        // awake and after them (App. A.6: it follows its waker; talking holds it off). One at a time.
        var n = new Night(5_000, 12);
        n.Crew[1] = n.Ground(4_600);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(240, () =>
        {
            // They get away from each pack a few seconds after it finds them; the Gaunt keeps after them.
            if (n.Director.Hunts.Count > 0 && (n.World.Tick - n.Director.Hunts[^1].Tick) == 8 * SimConstants.TickRate)
                foreach (var e in n.World.ActiveEnemies.OfType<Ribbit>())
                    e.Dismiss();
        });
        var hunts = n.Director.Hunts;
        Assert.True(hunts.Count > A.GauntFrom, $"{hunts.Count} hunts");
        Assert.All(hunts.Take(A.GauntFrom), h => Assert.Equal(-1, h.Gaunt));
        Assert.NotEqual(-1, hunts[A.GauntFrom].Gaunt);
        // Only the one while it's still after them.
        Assert.Single(hunts, h => h.Gaunt >= 0);
        var gaunt = Assert.Single(n.World.ActiveEnemies.OfType<Gaunt>());
        Assert.Equal(1, gaunt.Waker);
        Assert.Equal(Enemy.Loose, gaunt.Attached);
        // At their back by now, from where it was put down.
        var them = PlayerMotor.WorldPosition(n.Crew[1], n.Train);
        Assert.InRange((gaunt.WorldPosition(n.Train) - them).Length, 0, Tuning.Enemies.Gaunt.FollowAt * 3);
    }

    [Fact]
    public void AGauntStillWalkingUpDoesntJumpAboardWithThem()
    {
        // Woken from far off, it follows on foot: their getting aboard doesn't put it at their back in the car (note 296).
        var n = new Night(5_000, 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        var far = n.Ground(n.Train.Dynamics.Distance - 30, 20).Position;
        var g = (Gaunt)n.World.AddEnemy(id => Gaunt.WokenBy(id, far, 1, Tuning.Enemies.Gaunt));
        n.Run(1);
        Assert.Equal(Enemy.Loose, g.Attached);
        Assert.True((g.WorldPosition(n.Train) - far).Length > 1, "it walks after them");
    }

    [Fact]
    public void TheChoirDoesntGatherWithTheTrainInAFortAndASwarmThatFollowedItInIsGone()
    {
        // GDD §9: the forts are safe (note 273's caveat: "the Choir's meter isn't stilled in the terminus"; note 296).
        var inFort = new Night(Yard - 50, 0);
        var outside = new Night(5_000, 0);
        foreach (var n in new[] { inFort, outside })
        {
            n.World.Choir.Build = 1;
            n.World.Choir.Present = true;
            n.Run(2);
        }
        Assert.Equal("in a fort", inFort.Director.HeldBecause);
        Assert.False(inFort.World.Choir.Present);
        Assert.Equal(0, inFort.World.Choir.Build);
        // Out on the line, a swarm only goes after its quiet (enemies.json choir.disperseQuietSeconds).
        Assert.True(outside.World.Choir.Present);
    }

    [Fact]
    public void TheRibbitsSentGoForThemAlone()
    {
        var n = new Night(5_000, 12);
        n.Crew[1] = n.Ground(4_600);
        for (int i = 0; i < 120 && n.Director.Hunts.Count == 0; i++)
            n.Run(1);
        Assert.NotEmpty(n.Director.Hunts);
        n.Run(2);
        var pack = n.World.ActiveEnemies.OfType<Ribbit>().Where(r => r.Pack == n.Director.Hunts[0].Pack).ToList();
        Assert.NotEmpty(pack);
        Assert.All(pack, r => Assert.Equal(1, r.Target));
    }

    [Fact]
    public void BesideTheTrainOrAboardThereIsNoHunt()
    {
        var n = new Night(5_000, 0);
        n.Crew[1] = n.Ground(n.Train.Dynamics.RearDistance - A.BehindM * 0.5);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Run(150);
        Assert.Empty(n.Director.Hunts);
        Assert.Equal(0, n.Director.AbandonedPressure(1));
    }

    [Fact]
    public void BackInsideTheFortsGateTheyreSafe()
    {
        // Left behind at the gate: they went back into the fortress.
        var n = new Night(2_000, 12);
        n.Crew[1] = n.Ground(Yard - 100);
        Assert.True(n.World.InFort(PlayerMotor.WorldPosition(n.Crew[1], n.Train)));
        n.Run(150);
        Assert.Empty(n.Director.Hunts);
    }

    [Fact]
    public void TheFortsReachTheGateAndFromTheTerminusOnAndNoFurtherOut()
    {
        var n = new Night(2_000, 0);
        Double3 At(double along, double lateral) => n.Ground(along, lateral).Position;
        Assert.True(n.World.InFort(At(100, 10)));
        Assert.True(n.World.InFort(At(Yard - 1, -60)));
        Assert.False(n.World.InFort(At(Yard + 50, 0)));
        Assert.False(n.World.InFort(At(300, Tuning.Run.Forts.HalfWidthM + 10)));
        Assert.False(n.World.InFort(At(10_000, 0)));
        Assert.True(n.World.InFort(At(n.Route.Length - Tuning.Run.TerminusZone + 10, 5)));
    }

    [Fact]
    public void ACreatureThatComesIntoAFortIsDrivenOffAndNothingIsSentWhileTheTrainIsInOne()
    {
        var n = new Night(Yard + 300, 0);
        n.Crew[1] = n.Ground(Yard - 10);
        var r = Tuning.Enemies.Ribbits;
        // A pack outside the gate, hopping in after them.
        var outside = Enumerable.Range(0, 3).Select(i => n.World.AddEnemy(id => Ribbit.At(id, 1000, n.Ground(Yard + 15 + i).Position, r))).ToList();
        bool inside = false;
        n.Run(20, () => inside |= n.World.ActiveEnemies.Any(e => !e.Gone && n.World.InFort(e.WorldPosition(n.Train))));
        Assert.False(inside, "a creature in the fort");
        Assert.Contains(outside, e => e.Gone);
        Assert.False(n.Crew[1].Has(PlayerFlags.Held));
        Assert.True(n.Crew[1].Alive);
        // A train inside a fort: the director holds.
        var m = new Night(Yard - 50, 0);
        m.Run(3);
        Assert.Equal("in a fort", m.Director.HeldBecause);
    }
}
