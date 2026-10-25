using System.Text.Json;
using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Dev.Moments;

/// <summary>
/// A clip-worthy moment of a night (queue #256, ARCHITECTURE §8 note 1034; GDD D.15 question 5): what kind, when (the host's
/// tick, and seconds into the night), how much it's worth (<see cref="MomentScores"/>), whom it's of (a player id, −1 none:
/// the train, a car), and the clerk's words for it.
/// </summary>
public sealed record Moment(string Kind, uint Tick, double Seconds, double Score, int Who, string What, string Where = "");

/// <summary>
/// How much each kind of moment is worth (tools/moments/scores.json): a developer's yardstick for cutting a reel, not a
/// design number: nothing in the game reads it.
/// </summary>
public sealed record MomentScores
{
    public const string File = "tools/moments/scores.json";

    /// <summary>The incident report's facts by kind (<see cref="IncidentKind"/>'s names, camelCase), each a moment worth this.</summary>
    public Dictionary<string, double> Incidents { get; init; } = new() { ["death"] = 5, ["derailed"] = 10, ["stranded"] = 7, ["carLost"] = 4, ["rescue"] = 6, ["punished"] = 4 };
    /// <summary>A grab broken (a friend prised them off, a blow, struggled free): worth this, and <see cref="LatePerSecond"/> more a second it held.</summary>
    public double GrabBroken { get; init; } = 4;
    public double LatePerSecond { get; init; } = 1;
    /// <summary>The most lateness adds.</summary>
    public double LateMost { get; init; } = 4;
    /// <summary>Someone left behind: on foot this far (m) from every car with the train going (over <see cref="LeftGoing"/> m/s), worth <see cref="LeftBehind"/>.</summary>
    public double LeftBehind { get; init; } = 6;
    public double LeftBehindM { get; init; } = 60;
    public double LeftGoing { get; init; } = 3;
    /// <summary>A reel's moments are at least this far apart (s), so one fight isn't the whole reel.</summary>
    public double Apart { get; init; } = 20;

    public static MomentScores Load(string? path) =>
        path is not null && System.IO.File.Exists(path) ? DataFile.Load<MomentScores>(path) : new MomentScores();
}

/// <summary>
/// Scores a night as the host plays it, a tick at a time (from a recording, <see cref="Replay.NightReplay"/>, or any hosted
/// world): every fact the incident report keeps that's worth a clip (a death, a rescue, a derailment), every grab broken
/// (worth more the later it was broken: "a grab broken late"), and everyone left behind on foot as the train goes. Reads
/// the world, never changes it. Not voice: a mimic answered is heard, and nothing in a recording says who answered.
/// </summary>
public sealed class MomentScorer(MomentScores scores)
{
    readonly List<Moment> _all = [];
    readonly HashSet<int> _behind = [];
    int _incidents;

    public IReadOnlyList<Moment> All => _all;

    /// <summary>After a host step: what this tick made of the night.</summary>
    public void Step(World world, IEnumerable<(int Id, PlayerState State)> crew)
    {
        uint tick = world.Tick;
        double seconds = world.Run?.Seconds ?? tick * SimConstants.TickSeconds;
        var log = world.Attribution.Log;
        for (; _incidents < log.Count; _incidents++)
        {
            var i = log[_incidents];
            string kind = JsonNamingPolicy.CamelCase.ConvertName(i.Kind.ToString());
            if (scores.Incidents.TryGetValue(kind, out double worth) && worth > 0)
                _all.Add(new Moment(kind, tick, i.Seconds, worth, i.Victim, i.What, i.Where));
        }
        foreach (var e in world.EnemyEvents)
            if (e.From == SpinePhase.Grab && e.To is SpinePhase.BreakOff or SpinePhase.Dormant)
            {
                double late = Math.Min(scores.LateMost, e.SecondsInFrom * scores.LatePerSecond);
                _all.Add(new Moment("grabBroken", tick, seconds, scores.GrabBroken + late, -1, $"A grab by the {e.Kind} broken after {e.SecondsInFrom:0.0} s"));
            }
        var train = world.Train;
        bool going = Math.Abs(train.Dynamics.Velocity) > scores.LeftGoing;
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
        {
            if (!s.Alive || s.Parent != PlayerState.World)
            {
                _behind.Remove(id);
                continue;
            }
            var at = PlayerMotor.WorldPosition(s, train);
            double nearest = train.Frames.Count == 0 ? double.MaxValue : train.Frames.Min(f => ((f.Origin - at) with { Y = 0 }).Length);
            if (going && nearest > scores.LeftBehindM && _behind.Add(id))
                _all.Add(new Moment("leftBehind", tick, seconds, scores.LeftBehind, id, $"Left behind, {nearest:0} m from the train", IncidentLog.Where(world, s)));
        }
    }

    /// <summary>
    /// The best <paramref name="most"/>: highest first (the earlier on a tie), none within <see cref="MomentScores.Apart"/>
    /// of a better one, then in the order they happened.
    /// </summary>
    public List<Moment> Top(int most)
    {
        var picked = new List<Moment>();
        foreach (var m in _all.OrderByDescending(m => m.Score).ThenBy(m => m.Tick))
            if (picked.Count < most && picked.All(p => Math.Abs(p.Seconds - m.Seconds) >= scores.Apart))
                picked.Add(m);
        return [.. picked.OrderBy(m => m.Tick)];
    }
}
