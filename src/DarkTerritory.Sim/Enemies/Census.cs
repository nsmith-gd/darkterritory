using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>Where a crewmate is working, as the orchestrator counts it (docs/design/orchestrator.md §3.1 2; note 345).</summary>
public enum Post : byte
{
    /// <summary>In the cab: the controls, the fire, the line ahead.</summary>
    Cab,
    /// <summary>Seated at a gun.</summary>
    Gunner,
    /// <summary>Out on the train: its roofs, a ladder, a coupling gap.</summary>
    Walker,
    /// <summary>Inside a car.</summary>
    Rider,
    /// <summary>Off the train, near it.</summary>
    Ground,
    /// <summary>Off the train and left behind (note 273): their own hunt answers them, not the census.</summary>
    LeftBehind,
}

/// <summary>A crewmate as the census last saw them: their post, the seconds since they had anything to answer, and the threats on them.</summary>
public sealed record CensusEntry(int Player, Post Post, double Slack, int On);

/// <summary>
/// The orchestrator's census (ARCHITECTURE §8 note 345; docs/design/orchestrator.md §3.1, §3.2 items 2–4; GDD App. F.3: threats
/// orchestrated to "the number of active players"). Once a second, host only, with the director's think: each crewmate alive
/// in the night, their <see cref="Post"/>, which engaged threats are on whom, and each one's slack: seconds on the run between
/// stops since they last had something to answer. Something to answer is:
/// - a threat on them: holding them; else the nearest crewmate to it within <see cref="OrchestratorTuning.OnRadius"/>; else
///   the nearest at a post that answers it (a pack running behind is the gunner's);
/// - an upkeep job on their car (an open hot box, note 331; a guttering lamp, note 346), a hot box anywhere for a walker, or
///   a guttering lamp anywhere for a rider;
/// - in the cab, a train on the move (the line is the driver's to answer).
/// Deterministic: players by id, threats by id.
/// </summary>
public sealed class Census
{
    readonly SortedDictionary<int, (Post Post, double Slack, int On)> _now = [];
    readonly SortedDictionary<int, (double Max, double Over)> _stats = [];

    /// <summary>The crewmates counted this second, by player id.</summary>
    public IReadOnlyList<CensusEntry> Entries => [.. _now.Select(kv => new CensusEntry(kv.Key, kv.Value.Post, kv.Value.Slack, kv.Value.On))];

    /// <summary>Each crewmate's most slack tonight, and the seconds they've spent at or past <see cref="OrchestratorTuning.SlackPress"/>.</summary>
    public IReadOnlyDictionary<int, (double Max, double Over)> Stats => _stats;

    /// <summary>The post that answers each kind, as the tuning names them (<see cref="OrchestratorTuning.Answers"/>).</summary>
    public static IEnumerable<Post> AnswerPosts(OrchestratorTuning t, EnemyKind kind)
    {
        string key = Director.Key(kind);
        foreach (var (post, kinds) in t.Answers.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (kinds.Contains(key) && Enum.TryParse<Post>(post, ignoreCase: true, out var p))
                yield return p;
    }

    /// <summary>A crewmate's post now.</summary>
    public static Post PostOf(in PlayerState s, TrainOnLine train, double behindM)
    {
        if (s.Parent == PlayerState.World)
        {
            var at = PlayerMotor.WorldPosition(s, train);
            double hint = s.LineHint;
            train.Line.Nearest(at, ref hint);
            double behind = Math.Max(train.Dynamics.RearDistance - hint, hint - train.Dynamics.Distance);
            return behind > behindM ? Post.LeftBehind : Post.Ground;
        }
        if (s.Has(PlayerFlags.Seated))
            return Post.Gunner;
        if (PlayerMotor.InCab(s, train) || s.Parent == 0)
            return Post.Cab;
        return s.Surface == Surface.Deck ? Rider(s, train) : Post.Walker;
    }

    /// <summary>On a car's floor inside its walls (a rider), or out on its landing (a walker).</summary>
    static Post Rider(in PlayerState s, TrainOnLine train) =>
        s.Parent >= 0 && s.Parent < train.Frames.Count && Math.Abs(s.Position.X) < train.Frames[s.Parent].Shape.HalfWidth ? Post.Rider : Post.Walker;

    /// <summary>
    /// One second: posts, who each engaged threat is on, and slack. <paramref name="counting"/>: out on the line (past the
    /// grace, out of the forts, short of the final approach); otherwise slack holds where it is.
    /// </summary>
    public void Count(World world, IReadOnlyList<Enemy> active, OrchestratorTuning t, double behindM, bool counting)
    {
        var train = world.Train;
        var crew = world.CrewThisTick.Where(c => c.State.Alive).OrderBy(c => c.Id).ToList();
        var posts = crew.Select(c => (c.Id, Post: PostOf(c.State, train, behindM), At: PlayerMotor.WorldPosition(c.State, train), c.State.Parent)).ToList();
        var on = new SortedDictionary<int, int>();
        foreach (var e in active.Where(Director.Engaged).OrderBy(e => e.Id))
            if (On(e, posts, train, t) is { } who)
                on[who] = on.GetValueOrDefault(who) + 1;
        bool anyHotBox = train.Vehicles.Any(v => v.HotBox > 0), anyGutter = train.Vehicles.Any(v => v.Gutter > 0);
        var seen = new HashSet<int>();
        foreach (var (id, post, _, parent) in posts)
        {
            seen.Add(id);
            int n = on.GetValueOrDefault(id);
            // The upkeep jobs (orchestrator.md §5.1): an open hot box (note 331) is greased from the landing above it, the walkers'
            // job; a guttering lamp (note 346) is trimmed inside the car, the riders'. Either on their own car is theirs.
            bool onCar = parent >= 0 && parent < train.Vehicles.Count;
            bool hotBox = onCar && (train.Vehicles[parent].HotBox > 0 || train.Vehicles[parent].Gutter > 0)
                || post == Post.Walker && anyHotBox || post == Post.Rider && anyGutter;
            // The cab with the train moving has the line to answer: the boards and bends ahead, the fire and the gauge (§12's
            // conductor and boiler). Standing, it's as idle as anyone.
            bool driving = post == Post.Cab && Math.Abs(train.Dynamics.Velocity) > t.DrivingAbove;
            double slack = _now.TryGetValue(id, out var was) ? was.Slack : 0;
            slack = n > 0 || hotBox || driving || post == Post.LeftBehind ? 0 : counting ? slack + 1 : slack;
            _now[id] = (post, slack, n);
            var st = _stats.GetValueOrDefault(id);
            _stats[id] = (Math.Max(st.Max, slack), st.Over + (counting && slack >= t.SlackPress ? 1 : 0));
        }
        foreach (int id in _now.Keys.Where(k => !seen.Contains(k)).ToList())
            _now.Remove(id);
    }

    /// <summary>The crewmate an engaged threat is on, or null (nobody near and nobody at a post that answers it).</summary>
    static int? On(Enemy e, List<(int Id, Post Post, Double3 At, int Parent)> crew, TrainOnLine train, OrchestratorTuning t)
    {
        if (e.Holding >= 0 && crew.Any(c => c.Id == e.Holding))
            return e.Holding;
        var at = e.WorldPosition(train);
        int? best = null;
        double bestD = double.MaxValue;
        foreach (var c in crew)
        {
            double d = (c.At - at).Length;
            if (d <= t.OnRadius && d < bestD)
                (best, bestD) = (c.Id, d);
        }
        if (best is not null)
            return best;
        var posts = AnswerPosts(t, e.Kind).ToHashSet();
        foreach (var c in crew)
        {
            double d = (c.At - at).Length;
            if (posts.Contains(c.Post) && d < bestD)
                (best, bestD) = (c.Id, d);
        }
        return best;
    }
}
