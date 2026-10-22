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
/// <param name="Committed">
/// Note 753: their hands are on a job that holds them there (<see cref="Census.IsCommitted"/>); read only with
/// <see cref="OrchestratorTuning.Commitment"/> on.
/// </param>
/// <param name="At">Where they stand (world), for the collision's reach.</param>
public sealed record CensusEntry(int Player, Post Post, double Slack, int On, bool Committed = false, Double3 At = default)
{
    /// <summary>Note 867: the one on the same pair job with them (<see cref="Census.PartnerOf"/>), or −1.</summary>
    public int Partner { get; init; } = -1;
}

/// <summary>
/// The orchestrator's census (ARCHITECTURE §8 note 345; docs/design/orchestrator.md §3.1, §3.2 items 2–4; GDD App. F.3: threats
/// orchestrated to "the number of active players"). Once a second, host only, with the director's think: each crewmate alive
/// in the night, their <see cref="Post"/>, which engaged threats are on whom, and each one's slack: seconds on the run between
/// stops since they last had something to answer. Something to answer is:
/// - a threat on them: holding them; else the nearest crewmate to it within <see cref="OrchestratorTuning.OnRadius"/>; else
///   the nearest at a post that answers it (a pack running behind is the gunner's);
/// - an upkeep job on their car (an open hot box, note 331; a guttering lamp, note 346; a coupling working loose at either
///   end, note 356), a hot box or loose coupling anywhere for a walker, or a guttering lamp anywhere for a rider;
/// - in the cab, a train on the move (the line is the driver's to answer).
/// Deterministic: players by id, threats by id.
/// </summary>
public sealed class Census
{
    readonly SortedDictionary<int, (Post Post, double Slack, int On, bool Committed, Double3 At, int Partner)> _now = [];
    readonly SortedDictionary<int, (double Max, double Over)> _stats = [];

    /// <summary>The crewmates counted this second, by player id.</summary>
    public IReadOnlyList<CensusEntry> Entries =>
        [.. _now.Select(kv => new CensusEntry(kv.Key, kv.Value.Post, kv.Value.Slack, kv.Value.On, kv.Value.Committed, kv.Value.At) { Partner = kv.Value.Partner })];

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
    /// One second: posts, who each threat in <paramref name="threats"/> is on (note 881: the director's threats now, see
    /// <see cref="Director.Threatening"/>; everything <see cref="Director.Engaged"/> with <c>director.threats</c> off), and slack. <paramref name="counting"/>: out on the line (past the
    /// grace, out of the forts, short of the final approach); otherwise slack holds where it is.
    /// </summary>
    public void Count(World world, IReadOnlyList<Enemy> threats, OrchestratorTuning t, double behindM, bool counting)
    {
        var train = world.Train;
        var crew = world.CrewThisTick.Where(c => c.State.Alive).OrderBy(c => c.Id).ToList();
        var posts = crew.Select(c => (c.Id, Post: PostOf(c.State, train, behindM), At: PlayerMotor.WorldPosition(c.State, train), c.State.Parent)).ToList();
        var on = new SortedDictionary<int, int>();
        foreach (var e in threats.OrderBy(e => e.Id))
            if (On(e, posts, train, t) is { } who)
                on[who] = on.GetValueOrDefault(who) + 1;
        bool anyHotBox = train.Vehicles.Any(v => v.HotBox > 0 || v.Loose > 0), anyGutter = train.Vehicles.Any(v => v.Gutter > 0);
        var seen = new HashSet<int>();
        foreach (var (id, post, at, parent) in posts)
        {
            seen.Add(id);
            int n = on.GetValueOrDefault(id);
            // The upkeep jobs (orchestrator.md §5.1): an open hot box (note 331) is greased from the landing above it and a
            // coupling working loose (note 356) is made fast in its gap, the walkers' jobs; a guttering lamp (note 346) is
            // trimmed inside the car, the riders'. Any of them on their own car is theirs (a loose coupling: either car of its
            // gap, the one behind it or the one ahead).
            bool onCar = parent >= 0 && parent < train.Vehicles.Count;
            // (Loose is on the car ahead of its gap: this car's own is the gap behind it, the car ahead's the gap in front.)
            int ahead = onCar ? train.VehicleAhead(parent) : -1;
            bool hotBox = onCar && (train.Vehicles[parent].HotBox > 0 || train.Vehicles[parent].Gutter > 0 || train.Vehicles[parent].Loose > 0
                    || ahead >= 0 && train.Vehicles[ahead].Loose > 0)
                || post == Post.Walker && anyHotBox || post == Post.Rider && anyGutter;
            // The cab with the train moving has the line to answer: the boards and bends ahead, the fire and the gauge (§12's
            // conductor and boiler). Standing, it's as idle as anyone.
            bool driving = post == Post.Cab && Math.Abs(train.Dynamics.Velocity) > t.DrivingAbove;
            double slack = _now.TryGetValue(id, out var was) ? was.Slack : 0;
            slack = n > 0 || hotBox || driving || post == Post.LeftBehind ? 0 : counting ? slack + 1 : slack;
            // Committed (note 753) is read wherever the census counts, at a stop as on the run.
            bool committed = t.Commitment && IsCommitted(world, id, crew.First(c => c.Id == id).State);
            _now[id] = (post, slack, n, committed, at, committed ? PartnerOf(world, id) : -1);
            var st = _stats.GetValueOrDefault(id);
            _stats[id] = (Math.Max(st.Max, slack), st.Over + (counting && slack >= t.SlackPress ? 1 : 0));
        }
        foreach (int id in _now.Keys.Where(k => !seen.Contains(k)).ToList())
            _now.Remove(id);
    }

    /// <summary>
    /// Committed (note 753; the audit's R5, §7): their hands on a job that holds them where they are, read from the state that's
    /// already there. At a crane's controls (<see cref="PlayerFlags.Operating"/>); carrying something that needs both arms
    /// (<see cref="PlayerFlags.Heavy"/>); a timed Use job under way (<see cref="PlayerState.ActionProgress"/>: grease at a hot
    /// box, a coupling tightened, a breach boarded up, a mend with the wrench, the shovel at the fire); seated at a gun with
    /// its reload under way or its bore being cleared; on one of the stop's modules (a winch handle, a lever, the herd, a
    /// crane's rigging); breaking into a Holdout.
    /// </summary>
    public static bool IsCommitted(World world, int id, in PlayerState s)
    {
        if (s.Has(PlayerFlags.Operating) || s.Has(PlayerFlags.Heavy) || s.ActionProgress > 0)
            return true;
        var train = world.Train;
        if (s.Has(PlayerFlags.Seated) && s.Parent >= 0 && s.Parent < train.Vehicles.Count
            && train.Vehicles[s.Parent].Gun is var gun && (gun.ReloadProgress > 0 || gun.Jammed))
            return true;
        if (world.Run is { Phase: Run.RunPhase.AtFacility, CurrentSite: { } site } && site.Works(id))
            return true;
        return world.Holdouts?.All.Any(h => h.Breacher == id) == true;
    }

    /// <summary>
    /// Note 867: the crewmate on the same pair job as <paramref name="id"/>, or −1: the other end of a heavy crate or a jackpot
    /// up between them, the winch's other handle, the crane's operator for its rigger (and the rigger for its operator).
    /// </summary>
    public static int PartnerOf(World world, int id)
    {
        foreach (var b in world.Bodies.All)
            if (b.Kind == Physics.BodyKind.Heavy && b.Carrier >= 0 && b.Second >= 0 && b.HeldBy(id))
                return b.Carrier == id ? b.Second : b.Carrier;
        if (world.Run is { Phase: Run.RunPhase.AtFacility, CurrentSite: { } site })
        {
            if (site.Cranking[0] >= 0 && site.Cranking[1] >= 0 && site.Cranking[0] != site.Cranking[1])
            {
                if (site.Cranking[0] == id)
                    return site.Cranking[1];
                if (site.Cranking[1] == id)
                    return site.Cranking[0];
            }
            foreach (var crane in site.Cranes)
                if (crane.Operator >= 0 && crane.Rigger >= 0 && crane.Operator != crane.Rigger)
                {
                    if (crane.Operator == id)
                        return crane.Rigger;
                    if (crane.Rigger == id)
                        return crane.Operator;
                }
        }
        return -1;
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
