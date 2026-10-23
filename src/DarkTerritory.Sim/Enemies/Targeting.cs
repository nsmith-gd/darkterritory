using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What a crewmate is doing, as the director reads it (note 999 §2): the thing its threats are a countermeasure to.</summary>
public enum Doing : byte
{
    Firing,
    Driving,
    Gunning,
    Repairing,
    Operating,
    Carrying,
    Looting,
    Sprinting,
    Hiding,
    Walking,
    Riding,
    Afield,
}

/// <summary>A crewmate the director aims at this second, what they're doing, and how strongly they draw it (note 999 §4).</summary>
public readonly record struct Aim(int Player, Doing Doing, double Score, double Slack)
{
    /// <summary>What they're showing that the director foils (note 999 §2b), with <c>target.behaviours.on</c>.</summary>
    public Behaviour Shows { get; init; }
}

/// <summary>
/// Targeting (queue #703, note 999; the director's playtest of 23 Oct, line 35: "we should be targeting players specifically ...
/// tracking things that they're doing ... to apply pressure to them as countermeasures to the things that they are doing"). Each
/// second the director reads each living crewmate's <see cref="Doing"/>, picks the one who draws it most, and weighs the kinds
/// that answer that doing (<see cref="TargetTuning.Answers"/>) up in its choice. Only which kind: never when, how many, or a
/// kind's own rule. Pure and the host's: players by id, ties to the lowest, no draws.
/// </summary>
public static class Targeting
{
    /// <summary>
    /// The doing, first match wins (note 999 §2's table): <paramref name="groundSpeed"/> is the crewmate's speed over the ground
    /// (m/s, afoot only), <paramref name="trainMoving"/> the train's above the census's driving speed.
    /// </summary>
    public static Doing Of(Post post, PlayerFlags flags, double actionProgress, double groundSpeed, bool trainMoving, bool inHouse,
        bool shutIn, bool operating, TargetTuning t)
    {
        bool has(PlayerFlags f) => (flags & f) != 0;
        if (post == Post.Cab && has(PlayerFlags.Shovelful))
            return Doing.Firing;
        if (post == Post.Cab && trainMoving)
            return Doing.Driving;
        if (post == Post.Gunner)
            return Doing.Gunning;
        if (actionProgress > 0 || has(PlayerFlags.Pumping))
            return Doing.Repairing;
        if (operating || has(PlayerFlags.Operating))
            return Doing.Operating;
        if (has(PlayerFlags.Heavy) || has(PlayerFlags.Carrying) || has(PlayerFlags.SoloCarry))
            return Doing.Carrying;
        bool afoot = post is Post.Ground or Post.LeftBehind;
        if (afoot && inHouse)
            return Doing.Looting;
        if (afoot && groundSpeed > t.SprintAbove)
            return Doing.Sprinting;
        if (shutIn)
            return Doing.Hiding;
        return post switch
        {
            Post.Walker => Doing.Walking,
            Post.Rider or Post.Cab => Doing.Riding,
            _ => Doing.Afield,
        };
    }

    /// <summary>
    /// The crewmate the director aims at (note 999 §4), or null: each one's <see cref="TargetTuning.Pull"/> for their doing ×
    /// (1 + slack / <see cref="TargetTuning.SlackScale"/>) × <see cref="TargetTuning.AfootStopped"/> afoot while the train stands
    /// or × <see cref="TargetTuning.AboardRunning"/> aboard while it runs; nobody with <paramref name="perPlayer"/> threats on
    /// them already, nobody left behind (their own hunt answers them). The most, ties to the lowest id.
    /// </summary>
    public static Aim? Pick(IReadOnlyList<(int Player, Post Post, Doing Doing, double Slack, int On)> crew, bool stopped, int perPlayer, TargetTuning t,
        Func<int, Behaviour>? shows = null)
    {
        Aim? best = null;
        foreach (var c in crew.OrderBy(c => c.Player))
        {
            if (c.Post == Post.LeftBehind || perPlayer > 0 && c.On >= perPlayer)
                continue;
            bool afoot = c.Post == Post.Ground;
            double score = Math.Max(0, t.Pull.GetValueOrDefault(Name(c.Doing), 1))
                * (1 + Math.Max(0, c.Slack) / Math.Max(1, t.SlackScale))
                * (stopped && afoot ? t.AfootStopped : !stopped && !afoot ? t.AboardRunning : 1);
            // What they're showing that the director foils (note 999 §2b): × each behaviour's pull.
            var b = t.Behaviours.On && shows is not null ? shows(c.Player) : Behaviour.None;
            if (b != Behaviour.None)
                foreach (var f in Enum.GetValues<Behaviour>())
                    if (f != Behaviour.None && (b & f) != 0)
                        score *= Math.Max(0, t.Behaviours.Pull.GetValueOrDefault(Behaviours.Name(f), 1));
            if (score > 0 && (best is null || score > best.Value.Score))
                best = new Aim(c.Player, c.Doing, score, c.Slack) { Shows = b };
        }
        return best;
    }

    /// <summary>Whether <paramref name="kind"/> is one of the countermeasures to <paramref name="doing"/> (<see cref="TargetTuning.Answers"/>).</summary>
    public static bool Answers(Doing doing, EnemyKind kind, TargetTuning t) =>
        t.Answers.TryGetValue(Name(doing), out var kinds) && kinds.Contains(Director.Key(kind));

    /// <summary>Whether <paramref name="kind"/> answers the aim: its doing (<see cref="Answers(Doing, EnemyKind, TargetTuning)"/>) or a behaviour it shows (<see cref="BehavioursTuning.Foils"/>).</summary>
    public static bool Answers(Aim aim, EnemyKind kind, TargetTuning t) => Answers(aim.Doing, kind, t) || Foils(aim.Shows, kind, t);

    /// <summary>Whether <paramref name="kind"/> foils one of <paramref name="shows"/> (<see cref="BehavioursTuning.Foils"/>).</summary>
    public static bool Foils(Behaviour shows, EnemyKind kind, TargetTuning t)
    {
        if (shows == Behaviour.None || !t.Behaviours.On)
            return false;
        string key = Director.Key(kind);
        foreach (var f in Enum.GetValues<Behaviour>())
            if (f != Behaviour.None && (shows & f) != 0 && t.Behaviours.Foils.TryGetValue(Behaviours.Name(f), out var kinds) && kinds.Contains(key))
                return true;
        return false;
    }

    /// <summary>
    /// The choice weighed toward the aim: a kind that answers its doing ×<see cref="TargetTuning.AnswerWeight"/>, every other
    /// ×<see cref="TargetTuning.OffWeight"/>. Left as it is when none of them answers (nothing to aim with) or with no aim.
    /// </summary>
    public static List<(EnemyKind Kind, double Weight)> Weigh(List<(EnemyKind Kind, double Weight)> options, Aim? aim, TargetTuning t)
    {
        if (!t.On || aim is not { } a || !options.Any(o => o.Weight > 0 && Answers(a, o.Kind, t)))
            return options;
        return [.. options.Select(o => (o.Kind, o.Weight * (Answers(a, o.Kind, t) ? t.AnswerWeight : t.OffWeight)))];
    }

    /// <summary>The doing's name in the tuning (camel case).</summary>
    public static string Name(Doing d) => char.ToLowerInvariant(d.ToString()[0]) + d.ToString()[1..];

    /// <summary>
    /// Every living crewmate's post, doing, slack and threats on them this second, by id (the census's slack and count where
    /// it has them, 0 where it doesn't: the orchestrator off).
    /// </summary>
    public static List<(int Player, Post Post, Doing Doing, double Slack, int On)> Read(World world, IReadOnlyList<CensusEntry> census,
        double behindM, double drivingAbove, TargetTuning t) => Read(world, census, behindM, drivingAbove, t, out _);

    /// <summary>
    /// <see cref="Read(World, IReadOnlyList{CensusEntry}, double, double, TargetTuning)"/>, and what the behaviour reader sees of each
    /// (note 999 §2b): the nearest living crewmate (one O(crew²) pass), a cellar searched, a deck chair or the band, the mark.
    /// </summary>
    public static List<(int Player, Post Post, Doing Doing, double Slack, int On)> Read(World world, IReadOnlyList<CensusEntry> census,
        double behindM, double drivingAbove, TargetTuning t, out List<CrewSeen> seen)
    {
        seen = [];
        var train = world.Train;
        bool moving = Math.Abs(train.Dynamics.Velocity) > drivingAbove;
        var site = world.Run is { Phase: Run.RunPhase.AtFacility, CurrentSite: { } here } ? here : null;
        var list = new List<(int, Post, Doing, double, int)>();
        var spots = new List<Double3>();
        foreach (var (id, s) in world.CrewThisTick.OrderBy(c => c.Id))
        {
            if (!s.Alive)
                continue;
            var post = Census.PostOf(s, train, behindM);
            bool afoot = s.Parent == PlayerState.World;
            var at = PlayerMotor.WorldPosition(s, train);
            bool inHouse = afoot && train.Walls is { } walls && walls.HouseAt(at + Double3.Up * 0.3) >= 0;
            double ground = afoot ? Math.Sqrt(s.Velocity.X * s.Velocity.X + s.Velocity.Z * s.Velocity.Z) : 0;
            bool shut = PlayerMotor.Space(s, train) > 0;
            var doing = Of(post, s.Flags, s.ActionProgress, ground, moving, inHouse, shut, site?.Works(id) == true, t);
            var entry = census.FirstOrDefault(c => c.Player == id);
            list.Add((id, post, doing, entry?.Slack ?? 0, entry?.On ?? 0));
            bool cellar = afoot && world.Run?.SearchingIn(id) == Stops.ContainerKind.Cellar;
            seen.Add(new CrewSeen(id, post, doing, double.PositiveInfinity, cellar, s.Has(PlayerFlags.Lounging) || s.Has(PlayerFlags.Playing), world.IsMarked(id)));
            spots.Add(at);
        }
        for (int i = 0; i < seen.Count; i++)
        {
            double near = double.PositiveInfinity;
            for (int j = 0; j < seen.Count; j++)
                if (i != j)
                    near = Math.Min(near, (spots[i] - spots[j]).Length);
            seen[i] = seen[i] with { Nearest = near };
        }
        return list;
    }
}

/// <summary>Targeting's numbers (note 999). Mirror of enemies.json <c>director.encounters.target</c>; field docs live there.</summary>
public sealed record TargetTuning
{
    public bool On { get; init; }
    public double SprintAbove { get; init; } = 4.5;
    public double SlackScale { get; init; } = 120;
    public double AfootStopped { get; init; } = 1.5;
    public double AboardRunning { get; init; } = 1.2;
    public double AnswerWeight { get; init; } = 2;
    public double OffWeight { get; init; } = 0.75;
    public Dictionary<string, double> Pull { get; init; } = [];
    public Dictionary<string, List<string>> Answers { get; init; } = [];
    /// <summary>The behaviours read and foiled (note 999 §2b).</summary>
    public BehavioursTuning Behaviours { get; init; } = new();
}
