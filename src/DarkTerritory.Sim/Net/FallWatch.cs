using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// A fall on a harness night (note 553): a crewmate come down out of the air onto the ground well below the rails, or hurt (or
/// killed) by the landing. <paramref name="Below"/>: how far under the rail they landed (m); <paramref name="Hurt"/>: the health
/// the landing took; <paramref name="Speed"/>: the train's (m/s). <paramref name="Took"/>: the bot as it left its footing (the
/// harness trace's words); <paramref name="Left"/>: as it last stepped off the train.
/// </summary>
public sealed record FallReport(double Seconds, string Bot, int Id, double Km, double Below, int Hurt, bool Killed, double Speed, string Took, string Left);

/// <summary>
/// Note 553 (D1's audit after #294: the lone driver walked off Stroud Bridge's deck beside the train, 8 m down, and never got
/// back): watches the crew a tick at a time and keeps every fall, so the sweeps count them.
/// </summary>
public sealed class FallWatch
{
    /// <summary>Landed further than this under the rail (m) is a fall, not a step down off the train (#294's walkable ground is within 1).</summary>
    public const double Drop = 1.5;

    readonly Dictionary<int, (PlayerState Was, string Took, string Left)> _seen = [];

    /// <summary>The night's falls, in the order they happened.</summary>
    public List<FallReport> Falls { get; } = [];

    public void Watch(uint tick, TrainOnLine train, int id, in PlayerState s, IBot? bot)
    {
        if (!_seen.TryGetValue(id, out var w))
        {
            _seen[id] = (s, "", "");
            return;
        }
        var was = w.Was;
        string took = w.Took, left = w.Left;
        if (bot is not null && was.Alive && was.Parent != PlayerState.World && s.Parent == PlayerState.World)
            left = Harness.Describe(bot, was);
        if (bot is not null && was.Alive && was.Surface != Surface.Air && s.Surface == Surface.Air)
            took = Harness.Describe(bot, was);
        if (was.Alive && was.Surface == Surface.Air && s.Parent == PlayerState.World && (s.Surface == Surface.Ground || !s.Alive))
        {
            double below = Rail(train, s) - s.Position.Y;
            int hurt = was.Health - Math.Max(0, s.Health);
            if (below > Drop || hurt > 0)
                Falls.Add(new FallReport(Math.Round(tick * SimConstants.TickSeconds, 1), bot?.Name ?? $"player {id}", id,
                    Math.Round(train.Dynamics.Distance / 1000, 2), Math.Round(below, 1), hurt, !s.Alive, Math.Round(train.Dynamics.Speed, 1), took, left));
        }
        _seen[id] = (s, took, left);
    }

    /// <summary>The rail's height where they are: the train's line at the nearest point of it.</summary>
    static double Rail(TrainOnLine train, in PlayerState s)
    {
        double hint = s.LineHint > 0 ? s.LineHint : train.Dynamics.Distance;
        var (path, along) = train.Line.Nearest(s.Position, ref hint);
        return train.Line.Sample(path, along).Position.Y;
    }
}
