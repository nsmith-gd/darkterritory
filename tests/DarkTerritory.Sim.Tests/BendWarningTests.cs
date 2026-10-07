using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// ARCHITECTURE §8 note 265 (build 1121: "if derailment happens ... we'll want to telegraph using sound design that the train
/// is going under stress"; the director, 6 Oct: "a derailment needs to be clearly a mistake by the driver"). A bend taken
/// over its derailing speed (plan §8.5's √(aDerail R), unchanged) commits only once the warning (TrackRules.Assess, what
/// the HUD and the cab's bell say) has been up train.json overspeed.leadSeconds; and a driver who brakes on it makes the bend.
/// </summary>
public class BendWarningTests
{
    static readonly OverspeedTuning O = Tuning.Train.Overspeed;

    /// <summary>
    /// The routes' sharp bends: each on the main line where a train would come off under full speed. A reverse curve's
    /// halves (a Blind Throat's), under one board, are one bend. Note 278 lays bends as close as the lethal spacing allows
    /// (half a braking distance from line speed), not the 1.2 km these runs used to take as given: so each also says what
    /// the bend before it derails at, and where a train is clear of that one (its whole length past its end).
    /// </summary>
    static IEnumerable<(string Spec, double S, double DerailMs, double SMax, double Clear, double PrevDerailMs)> Bends(int cars, params string[] specs)
    {
        const double OneBend = 100;
        foreach (var spec in specs)
        {
            var route = Routes.Generate(DataFile.FindContentRoot(), spec, cars);
            var line = route.Build();
            double a = route.Plan!.Rules.ADerail, length = Consist.Uniform(Tuning.Train, cars, 1).LengthMetres;
            double start = -1, kMax = 0, sMax = 0, clear = double.NegativeInfinity, end = double.NegativeInfinity, prev = double.PositiveInfinity;
            (double S, double K, double SMax, double Clear, double Prev)? held = null;
            // The main line between the gate and the terminus's yard (past it the train runs into the arrival roads, not the main line).
            for (double s = route.Gate + 1000; s < Math.Min(line.Length, route.Plan!.TerminusM) - 600; s += 5)
            {
                // A stretch the night's switches take the train round (a washed-out main line) isn't run.
                if (route.Branches.Any(b => b.StartsDiverging && s >= b.Toe - 400 && s <= (b.Rejoin ?? b.Toe) + 400))
                    continue;
                double k = Math.Abs(line.Sample(RailLine.MainPath, s).Curvature);
                if (k > 1e-9 && Math.Sqrt(a / k) < Tuning.Train.MaxSpeed)
                {
                    if (start < 0)
                        start = s;
                    if (k > kMax)
                        (kMax, sMax) = (k, s);
                }
                else if (start >= 0)
                {
                    // The other half of a reverse curve: the same bend as the one held.
                    if (held is { } h && start - end < OneBend)
                        held = kMax > h.K ? (h.S, kMax, sMax, h.Clear, h.Prev) : h;
                    else
                    {
                        if (held is { } done)
                        {
                            yield return (spec, done.S, Math.Sqrt(a / done.K), done.SMax, done.Clear, done.Prev);
                            prev = Math.Sqrt(a / done.K);
                        }
                        held = (start, kMax, sMax, clear, prev);
                    }
                    end = s;
                    clear = s + length + 20;
                    start = -1;
                    kMax = 0;
                }
            }
            if (held is { } last)
                yield return (spec, last.S, Math.Sqrt(a / last.K), last.SMax, last.Clear, last.Prev);
        }
    }

    /// <summary>Where a run at <paramref name="speed"/> into a bend starts: <paramref name="back"/> short of it, unless the bend before it would come off at that speed, then clear of that one.</summary>
    static double StartOf(double s, double back, double speed, double clear, double prevDerailMs) =>
        prevDerailMs > speed + 0.1 ? s - back : Math.Max(s - back, clear);

    sealed class Run
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public double WarnedAt = double.NaN;

        public Run(string spec, double from, double speed, int cars = 6)
        {
            var route = Routes.Generate(DataFile.FindContentRoot(), spec, cars);
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), route.Build(), from);
            Train.Dynamics.Velocity = speed;
            World = new World(Train, Tuning.Combat) { TrackPlan = route.Plan };
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 1, authority: true);
        }

        /// <summary>Steps, each tick at the speed <paramref name="hold"/> says (or the brake's, if null), until it's past or off.</summary>
        public void Until(double past, Func<double, double?> hold, TrainControls controls, Func<bool>? done = null)
        {
            for (int i = 0; i < 400 * SimConstants.TickRate && !World.Derailed && Train.Dynamics.RearDistance < past && Train.Dynamics.Speed > 0.01 && done?.Invoke() != true; i++)
            {
                if (hold(World.ElapsedSeconds) is { } v)
                    Train.Dynamics.Velocity = v;
                World.BeginTick();
                World.Step(controls);
                if (double.IsNaN(WarnedAt) && TrackRules.Assess(Train, World.TrackPlan!.Rules, O).Warning)
                    WarnedAt = World.ElapsedSeconds;
            }
        }
    }

    static readonly string[] Specs = ["deepTerritory:2", "deepTerritory:5", "deadLines:3", "frontier:2"];

    [Fact]
    public void EveryBendDerailOnAGeneratedLineIsWarnedAFullLeadAhead()
    {
        var bends = Bends(6, Specs).ToList();
        Assert.NotEmpty(bends);
        int derails = 0;
        foreach (var (spec, s, vd, _, clear, prev) in bends)
        {
            // Into it a fifth over its derailing speed, held; and surging over it once on the bend, from just under it.
            foreach (bool surge in new[] { false, true })
            {
                double from = StartOf(s, 700, vd * 1.2, clear, prev);
                var r = new Run(spec, from, surge ? vd * 0.95 : vd * 1.2);
                r.Until(s + 400, _ => surge && r.Train.Dynamics.Distance > s + 20 ? vd * 1.2 : surge ? vd * 0.95 : vd * 1.2, new TrainControls { Reverser = 1 });
                Assert.True(r.World.Derailed, $"{spec} km {s / 1000:0.0} ({vd * 3.6:0} km/h), surge {surge}: still on");
                derails++;
                Assert.Single(r.World.BendCommits);
                Assert.True(r.World.BendCommits[0] + 1e-9 >= O.LeadSeconds, $"{spec} km {s / 1000:0.0}: warned {r.World.BendCommits[0]:0.00} s");
                // Held over its speed from far enough out for the warning's reach, none spared; started clear of a bend just
                // before it, the train's on its way in before the warning's been up a full lead (and it's still not taken
                // until it has, above).
                if (!surge && s - from >= O.WarnDistance(vd * 1.2, vd, r.Train.Dynamics.RatedBrakeDecel, O.LeadSeconds))
                    Assert.Equal(0, r.World.BendsSpared);
            }
        }
        Assert.Equal(bends.Count * 2, derails);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(20)]
    public void ADriverWhoBrakesOnTheWarningMakesTheBend(int cars)
    {
        // The director's risk: run right up to the bend and brake hard. A driver who shuts off and brakes within the reaction
        // window (lead - 0.5 s) of the warning going up is under its derailing speed by the time the train's on it.
        foreach (var (spec, s, vd, _, clear, prev) in Bends(cars, Specs))
        {
            double speed = Math.Min(Tuning.Train.MaxSpeed, vd * 1.4);
            var r = new Run(spec, StartOf(s, 1200, speed, clear, prev), speed, cars);
            var coast = new TrainControls { Reverser = 1 };
            r.Until(s + 400, _ => speed, coast, () => !double.IsNaN(r.WarnedAt) && r.World.ElapsedSeconds >= r.WarnedAt + O.LeadSeconds - 0.5);
            Assert.False(double.IsNaN(r.WarnedAt), $"{spec} km {s / 1000:0.0}: never warned (at {r.Train.Dynamics.Distance:0} m, {r.Train.Dynamics.Speed:0.0} m/s, {r.World.ElapsedSeconds:0} s, derailed {r.World.DerailCause}, vd {vd:0.0}, line {r.Train.Line.Length:0})");
            // On the brake from here.
            r.Until(s + 400, _ => null, new TrainControls { Reverser = 1, Brake = 1 });
            Assert.False(r.World.Derailed, $"{cars} cars, {spec} km {s / 1000:0.0}: {r.World.DerailCause}");
        }
    }

    [Fact]
    public void ABendTakenAtItsBoardIsNoWarningAndItsStressRisesTowardItsDerailingSpeed()
    {
        var (spec, s, vd, sMax, _, _) = Bends(6, Specs).First();
        var rules = Routes.Generate(DataFile.FindContentRoot(), spec, 6).Plan!.Rules;
        double posted = vd * Math.Sqrt(rules.APost / rules.ADerail);
        var r = new Run(spec, s - 300, posted * 0.98);
        r.Until(s + 200, _ => posted * 0.98, new TrainControls { Reverser = 1 });
        Assert.True(double.IsNaN(r.WarnedAt));
        Assert.False(r.World.Derailed);
        // Halfway from the board to coming off: stressed, not warned.
        var half = new Run(spec, sMax + 10, (posted + vd) / 2);
        var b = TrackRules.Assess(half.Train, rules, O);
        Assert.InRange(b.Stress, 0.3, 0.9);
        Assert.False(b.Warning);
    }
}
