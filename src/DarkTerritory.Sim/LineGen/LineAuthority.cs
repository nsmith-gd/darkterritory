using System.Runtime.CompilerServices;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The authority as a crew reads it off the boards (plan §9): the speed to be at here, braking in time for every limit
/// ahead on the way the switches are set. The bots drive by it, as the validator's ideal driver did (§16.1), so a bot
/// crew keeps to what the line communicates instead of one cruise speed that a tighter curve would derail.
/// </summary>
public sealed class LineAuthority
{
    static readonly ConditionalWeakTable<RailLine, LineAuthority> ByLine = new();

    readonly LinePlan _plan;
    readonly RailLine _line;
    readonly PlanRouteWay _mainWay;
    readonly SpeedProfile _main;
    readonly Dictionary<string, (PlanRouteWay Way, SpeedProfile Profile)> _alternates = new();
    readonly PlanAlignment[] _forks;

    LineAuthority(LinePlan plan, RailLine line)
    {
        _plan = plan;
        _line = line;
        // Wet rail brakes worse (§14); the profile keeps the validator's margin on the consist's own deceleration.
        double adhesion = plan.Weather.Rain ? plan.Rules.WetAdhesion : 1;
        SpeedProfile Profile(PlanRouteWay way) => new(way, plan.Authority.BrakeMs2, [], adhesion: adhesion);
        _mainWay = new PlanRouteWay(plan, line, []);
        _main = Profile(_mainWay);
        _forks = [.. plan.Alignment.Where(a => a.Role == EdgeRole.Alternate).OrderBy(a => a.Toe)];
        foreach (var a in _forks)
        {
            var way = new PlanRouteWay(plan, line, [a.Edge]);
            _alternates[a.Edge] = (way, Profile(way));
        }
    }

    /// <summary>The authority for a plan's built line (one per line: every session on it shares it).</summary>
    public static LineAuthority For(LinePlan plan, RailLine line) => ByLine.GetValue(line, l => new LineAuthority(plan, l));

    /// <summary>The speed to hold now: the least the profile allows over the next second's run, for the engine's rake.</summary>
    public double Allowed(TrainOnLine train)
    {
        var rake = train.Dynamics;
        var (edge, s) = _mainWay.Locate(rake.Path, rake.Distance);
        double ahead = rake.Speed * 1.0;
        if (edge == "main")
        {
            // Along the main, the way ahead is the one the next fork's switch is set for.
            var fork = _forks.FirstOrDefault(a => a.Toe > s);
            if (fork is not null && train.Diverging(fork.Branch))
            {
                var (way, profile) = _alternates[fork.Edge];
                return profile.Target((way.Of("main", s) ?? s) + ahead);
            }
            return _main.Target(s + ahead);
        }
        if (_alternates.TryGetValue(edge, out var alt))
            return alt.Profile.Target((alt.Way.Of(edge, s) ?? 0) + ahead);
        // A spur or a dead line: short, walked in at its own limits.
        double v = _plan.Authority.LineSpeedMs;
        foreach (var l in _plan.Authority.Limits)
            if (l.Edge == edge && s >= l.S0 - ahead && s <= l.S1)
                v = Math.Min(v, l.VMs);
        return v;
    }
}
