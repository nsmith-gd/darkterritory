namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The behaviours the director foils (queue #703, note 999 §2b; the director's playtest of 23 Oct, line 36: "track some things
/// about player behavior during the game in a performant way ... and build foils for that, so that pushing players for
/// adaptability or towards teamwork"). Flags: a crewmate may show several at once.
/// </summary>
[Flags]
public enum Behaviour : ushort
{
    None = 0,
    /// <summary>Searching a house with nobody near ("I'm going looting by myself").</summary>
    LootingAlone = 1,
    /// <summary>Searching a cellar with nobody near ("I'm going into the basement by myself").</summary>
    CellarAlone = 2,
    /// <summary>Afoot, far from every crewmate ("running away from all of my friends ... and disperse").</summary>
    Straggling = 4,
    /// <summary>On a gun a long while with nobody near ("sitting on the back gun alone for too long").</summary>
    GunAlone = 8,
    /// <summary>In a deck chair or playing the band a long while ("hanging out on some of the fun cosmetic stuff").</summary>
    Lazing = 16,
    /// <summary>Hardly a word on the mic ("players who don't particularly communicate on mic").</summary>
    Silent = 32,
    /// <summary>On the mic nearly all the time ("some players who never shut up").</summary>
    Chatter = 64,
    /// <summary>Marked for shoving the crew about (the horseplay's mark, note 717: "players who are griefing too much").</summary>
    Griefing = 128,
    /// <summary>Most of the night at one job ("players who are min-maxing too much").</summary>
    OneTrick = 256,
}

/// <summary>What the director sees of one crewmate this second, for <see cref="Behaviours.Second"/>.</summary>
/// <param name="Nearest">Metres to the nearest other living crewmate (∞ alone in the night).</param>
/// <param name="Cellar">Searching a cellar now (<see cref="Run.Run.SearchingIn"/>).</param>
/// <param name="Lazing">In a deck chair or playing the band (<see cref="Player.PlayerFlags.Lounging"/>, <see cref="Player.PlayerFlags.Playing"/>).</param>
/// <param name="Marked">The horseplay's mark on them (<see cref="World.IsMarked"/>).</param>
public readonly record struct CrewSeen(int Player, Post Post, Doing Doing, double Nearest, bool Cellar, bool Lazing, bool Marked);

/// <summary>
/// The behaviour reader (note 999 §2b): host only, once a director second, O(crew) over what <see cref="Targeting.Read"/> already
/// read (the nearest-crewmate distances are its O(crew²), at most 64 at eight), plus a byte a tick per player for the voice
/// (<see cref="Hear"/>: the loudest of the second). No draws; players by id. It changes nothing in the world: the director reads
/// it back only to weigh which kind comes and at whom (<see cref="TargetTuning.Behaviours"/>).
/// </summary>
public sealed class Behaviours
{
    sealed class Track
    {
        public Doing Doing;
        public double InDoing;
        public double Talk;
        public double Heard;
        public double Seconds;
        public double Nearest = double.PositiveInfinity;
        /// <summary>Seconds lazing without a break (note 1008: the Head Waiter's come).</summary>
        public double Lazed;
        public readonly double[] ByDoing = new double[Enum.GetValues<Doing>().Length];
        public Behaviour Now;
    }

    readonly SortedDictionary<int, Track> _p = [];
    readonly SortedDictionary<int, byte> _voice = [];
    readonly SortedDictionary<string, double> _seconds = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, SortedSet<int>> _who = new(StringComparer.Ordinal);

    /// <summary>A tick's voice (the intent's <c>Voice</c>, App. C.7): the loudest of the second is kept.</summary>
    public void Hear(int player, byte voice)
    {
        if (voice > _voice.GetValueOrDefault(player))
            _voice[player] = voice;
    }

    /// <summary>
    /// What <paramref name="player"/>'s doing now, the seconds they've been at it, and the metres to their nearest crewmate (note
    /// 1011: the Tapper's own dwell on the gun); null for nobody read.
    /// </summary>
    public (Doing Doing, double Seconds, double Nearest)? At(int player) => _p.TryGetValue(player, out var t) ? (t.Doing, t.InDoing, t.Nearest) : null;

    /// <summary>What <paramref name="player"/> shows now.</summary>
    public Behaviour Of(int player) => _p.TryGetValue(player, out var t) ? t.Now : Behaviour.None;

    /// <summary>
    /// Seconds <paramref name="player"/> has been lazing (in a deck chair, the hot tub, the hammock, playing the band) without a
    /// break, as the reader saw it once a second (note 1008: the Head Waiter comes for a long one).
    /// </summary>
    public double LazedSeconds(int player) => _p.TryGetValue(player, out var t) ? t.Lazed : 0;

    /// <summary>Crew-seconds of each behaviour tonight, by its name.</summary>
    public IReadOnlyDictionary<string, double> Seconds => _seconds;

    /// <summary>The crewmates who showed each behaviour tonight, by its name.</summary>
    public IReadOnlyDictionary<string, SortedSet<int>> Who => _who;

    /// <summary>
    /// One second: each crewmate's behaviours from <paramref name="crew"/> (the living; anyone missing is forgotten).
    /// <paramref name="counting"/>: out on the run (past the grace, out of the forts), when the night's tallies and the voice's
    /// and the jobs' shares count; otherwise the reads hold.
    /// </summary>
    public void Second(IReadOnlyList<CrewSeen> crew, bool counting, BehavioursTuning t)
    {
        var seen = new HashSet<int>();
        bool company = crew.Count > 1;
        foreach (var c in crew.OrderBy(c => c.Player))
        {
            seen.Add(c.Player);
            if (!_p.TryGetValue(c.Player, out var p))
                _p[c.Player] = p = new Track { Doing = c.Doing };
            p.InDoing = p.Doing == c.Doing ? p.InDoing + 1 : 1;
            p.Doing = c.Doing;
            p.Nearest = c.Nearest;
            p.Lazed = c.Lazing ? p.Lazed + 1 : 0;
            bool alone = c.Nearest > t.AloneM;
            if (counting)
            {
                p.Seconds++;
                p.ByDoing[(int)c.Doing]++;
                // The share of the second on the mic, as a running mean over talkSeconds.
                bool talking = _voice.GetValueOrDefault(c.Player) / 255.0 >= t.TalkAbove;
                p.Talk += ((talking ? 1 : 0) - p.Talk) / Math.Max(1, t.TalkSeconds);
                p.Heard++;
            }
            var b = Behaviour.None;
            if (c.Doing == Doing.Looting && alone && p.InDoing >= t.LootAloneSeconds)
                b |= Behaviour.LootingAlone;
            if (c.Cellar && alone)
                b |= Behaviour.CellarAlone;
            if (c.Post == Post.Ground && c.Nearest > t.StraggleM)
                b |= Behaviour.Straggling;
            if (c.Doing == Doing.Gunning && alone && p.InDoing >= t.GunAloneSeconds)
                b |= Behaviour.GunAlone;
            if (c.Lazing && p.InDoing >= t.LazeSeconds)
                b |= Behaviour.Lazing;
            if (company && p.Heard >= t.TalkSeconds)
            {
                if (p.Talk <= t.SilentShare)
                    b |= Behaviour.Silent;
                else if (p.Talk >= t.ChatterShare)
                    b |= Behaviour.Chatter;
            }
            if (c.Marked)
                b |= Behaviour.Griefing;
            if (p.Seconds >= t.OneTrickAfter && Top(p, t) is { } top && top >= t.OneTrickShare)
                b |= Behaviour.OneTrick;
            p.Now = b;
            if (counting)
                foreach (var f in Enum.GetValues<Behaviour>())
                    if (f != Behaviour.None && (b & f) != 0)
                    {
                        string name = Name(f);
                        _seconds[name] = _seconds.GetValueOrDefault(name) + 1;
                        if (!_who.TryGetValue(name, out var who))
                            _who[name] = who = [];
                        who.Add(c.Player);
                    }
        }
        foreach (int gone in _p.Keys.Where(k => !seen.Contains(k)).ToList())
            _p.Remove(gone);
        _voice.Clear();
    }

    /// <summary>The share of their counted seconds in their most-held job, the exempt ones (riding, afield: no job) left out.</summary>
    static double? Top(Track p, BehavioursTuning t)
    {
        double best = 0;
        for (int d = 0; d < p.ByDoing.Length; d++)
            if (!t.OneTrickExempt.Contains(Targeting.Name((Doing)d)))
                best = Math.Max(best, p.ByDoing[d]);
        return p.Seconds > 0 ? best / p.Seconds : null;
    }

    /// <summary>The behaviour's name in the tuning (camel case).</summary>
    public static string Name(Behaviour b) => char.ToLowerInvariant(b.ToString()[0]) + b.ToString()[1..];
}

/// <summary>The behaviour reader's thresholds and their foils (note 999 §2b). Mirror of enemies.json <c>director.encounters.target.behaviours</c>; field docs live there.</summary>
public sealed record BehavioursTuning
{
    public bool On { get; init; }
    public double AloneM { get; init; } = 12;
    public double StraggleM { get; init; } = 40;
    public double LootAloneSeconds { get; init; } = 10;
    public double GunAloneSeconds { get; init; } = 120;
    public double LazeSeconds { get; init; } = 45;
    public double TalkAbove { get; init; } = 0.15;
    public double TalkSeconds { get; init; } = 120;
    public double SilentShare { get; init; } = 0.03;
    public double ChatterShare { get; init; } = 0.6;
    public double OneTrickAfter { get; init; } = 600;
    public double OneTrickShare { get; init; } = 0.8;
    public List<string> OneTrickExempt { get; init; } = ["riding", "afield"];
    /// <summary>× on the aim's score for each behaviour they show.</summary>
    public Dictionary<string, double> Pull { get; init; } = [];
    /// <summary>The kinds that foil each behaviour, weighed as <see cref="TargetTuning.Answers"/> are.</summary>
    public Dictionary<string, List<string>> Foils { get; init; } = [];
}
