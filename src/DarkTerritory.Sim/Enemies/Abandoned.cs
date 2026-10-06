using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>A player the train's left: enemies.json <c>abandoned</c>. Field docs live in that file.</summary>
public sealed record AbandonedTuning
{
    public bool On { get; init; } = true;
    public double StrandBeyond { get; init; } = 60;
    public double GoneBeyond { get; init; } = 400;
    public double AwayAbove { get; init; } = 0.5;
    public double ReleaseWithin { get; init; } = 25;
    public double FiguresFrom { get; init; } = 10;
    public double LampOutAt { get; init; } = 30;
    public double HuntAt { get; init; } = 45;
    public double DeadlyAt { get; init; } = 90;
    public double ColdExtra { get; init; } = 5;
    public string Hunters { get; init; } = "ribbits";
    public int[] HuntPack { get; init; } = [2, 3];
    public double HuntOut { get; init; } = 30;
    public double HuntRange { get; init; } = 150;
    public double HuntEvery { get; init; } = 20;
    public double DeadlyHop { get; init; } = 1.5;
    public int Figures { get; init; } = 5;
    public double FiguresFar { get; init; } = 18;
    public double FiguresNear { get; init; } = 9;
    public double ShiftSeconds { get; init; } = 2.5;
}

/// <summary>How far the world has closed in on someone left behind (<see cref="Abandonment"/>).</summary>
public enum Dread : byte { None, Closing, Hunted, Deadly }

/// <summary>
/// One player left behind: how long the world's been closing in on them (the ramp's clock), where they were this tick, and
/// whether the clock's held (the train's coming back for them). Replicated, so every client hears and sees the same.
/// </summary>
public readonly record struct Abandoned(int Player, double Seconds, Double3 At, bool Held);

/// <summary>
/// The director's decision of 2026-10-06 (GDD App. F.1 "abandoned player", §7, §23 "getting left behind: cold and distance
/// do the rest"; ARCHITECTURE §8 note 266): "anytime someone is abandoned by the train, they don't necessarily need to die
/// right away, but the world should basically close in on them". A living player on the ground outside the forts, with
/// the train opening the gap past <see cref="AbandonedTuning.StrandBeyond"/> or gone past
/// <see cref="AbandonedTuning.GoneBeyond"/>, is the director's: from the first second the dark closes in (sounds, then
/// figures at the edge of the lamp), their hand lamp gutters out, the cold comes on faster and faster, and at
/// <see cref="AbandonedTuning.HuntAt"/> a pack hunts them, through its own TELEGRAPH and GRAB like any other (App. A.1);
/// past <see cref="AbandonedTuning.DeadlyAt"/> it's faster than they can run, and another comes whenever one's gone.
/// <para>
/// It's theirs alone: the hunters are never charged to the crew's budget, never count against the director's caps or its
/// pacing, and draw on dice of their own, so the train's night is the tier's. Aboard again, inside a fort, or with the
/// train back within <see cref="AbandonedTuning.ReleaseWithin"/>, they're let go and the clock's back to nothing; while the
/// train's closing on them (setting back for them, T96), it holds.
/// </para>
/// </summary>
public sealed class Abandonment(AbandonedTuning tuning)
{
    sealed class Track
    {
        public double Gap = double.NaN, Rate, Seconds, NextHunt;
        public bool Stranded, Held;
        public int Hunts;
        public Double3 At;
    }

    readonly SortedDictionary<int, Track> _tracks = [];
    readonly List<Abandoned> _mirror = [];

    public AbandonedTuning Tuning { get; } = tuning;

    /// <summary>Everyone the world's closing in on now, by player id.</summary>
    public IReadOnlyList<Abandoned> All => _mirror;

    /// <summary>How long the world's been closing in on <paramref name="player"/> (0: it isn't).</summary>
    public double SecondsOf(int player)
    {
        foreach (var a in _mirror)
            if (a.Player == player)
                return a.Seconds;
        return 0;
    }

    public Dread DreadOf(int player) => DreadAt(SecondsOf(player), Tuning);

    public static Dread DreadAt(double seconds, AbandonedTuning t) =>
        seconds <= 0 ? Dread.None : seconds < t.HuntAt ? Dread.Closing : seconds < t.DeadlyAt ? Dread.Hunted : Dread.Deadly;

    /// <summary>The ramp, 0 to 1 at <see cref="AbandonedTuning.DeadlyAt"/>.</summary>
    public static double Level(double seconds, AbandonedTuning t) => Math.Clamp(seconds / Math.Max(1e-6, t.DeadlyAt), 0, 1);

    /// <summary>Their hand lamp has gone out (presentation, from the replicated clock).</summary>
    public bool LampOut(int player) => SecondsOf(player) >= Tuning.LampOutAt;

    /// <summary>Extra seconds of cold a second on top of the night's (spec B.2's clock), rising to <see cref="AbandonedTuning.ColdExtra"/>.</summary>
    public double ExtraCold(int player) => Tuning.ColdExtra * Level(SecondsOf(player), Tuning);

    /// <summary>The hunters after <paramref name="player"/> (their <see cref="Enemy.Quarry"/>).</summary>
    public static IEnumerable<Enemy> HuntersOf(World world, int player) => world.ActiveEnemies.Where(e => !e.Gone && e.Quarry == player);

    /// <summary>Client side: the host's, as its records have it.</summary>
    public void Mirror(IEnumerable<Abandoned> host)
    {
        _mirror.Clear();
        _mirror.AddRange(host.OrderBy(a => a.Player));
    }

    /// <summary>
    /// How close the train is to someone on the ground: the nearest of the engine's rake's cars, edge to edge, flat.
    /// </summary>
    public static double GapTo(TrainOnLine train, Double3 at)
    {
        double best = double.MaxValue;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            var frame = train.Frames[v.Id];
            var local = frame.ToLocal(at);
            double dz = Math.Max(0, Math.Abs(local.Z) - frame.Shape.HalfLength), dx = Math.Max(0, Math.Abs(local.X) - frame.Shape.HalfWidth);
            best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
        }
        return best;
    }

    /// <summary>Out there, as far as the director's concerned: alive, on the ground, outside the forts, in a night that's on.</summary>
    static bool OutThere(World world, in PlayerState s, Double3 at) =>
        s.Alive && s.Parent == PlayerState.World && !world.Derailed && world.InFort(at) is false
        && (world.Run is null || world.Run is { Over: false, Phase: Run.RunPhase.Underway or Run.RunPhase.AtFacility });

    /// <summary>Host, once a tick, after the enemies: who's been left, the ramp's clock, and the hunt.</summary>
    public void Step(World world, IReadOnlyList<(int Id, PlayerState State)> crew, ulong seed)
    {
        double dt = SimConstants.TickSeconds;
        var t = Tuning;
        var train = world.Train;
        var present = new HashSet<int>();
        foreach (var (id, s) in crew)
        {
            present.Add(id);
            if (!_tracks.TryGetValue(id, out var k))
                _tracks[id] = k = new Track();
            var at = PlayerMotor.WorldPosition(s, train);
            k.At = at;
            if (!t.On || !OutThere(world, s, at))
            {
                LetGo(world, id, k);
                k.Gap = double.NaN;
                continue;
            }
            double gap = GapTo(train, at);
            // The gap opening (or closing), smoothed over a second: the train going away, or coming back.
            double rate = double.IsNaN(k.Gap) ? 0 : (gap - k.Gap) / dt;
            k.Rate += (rate - k.Rate) * Math.Min(1, dt / 1.0);
            k.Gap = gap;
            if (!k.Stranded)
            {
                k.Stranded = gap >= t.GoneBeyond || gap >= t.StrandBeyond && k.Rate > t.AwayAbove;
                if (!k.Stranded)
                    continue;
                k.Seconds = 0;
                k.NextHunt = t.HuntAt;
            }
            else if (gap <= t.ReleaseWithin)
            {
                LetGo(world, id, k);
                continue;
            }
            // Coming back for them: the clock holds (and nothing new is sent) while the gap closes.
            k.Held = k.Rate < -t.AwayAbove;
            if (k.Held)
                continue;
            k.Seconds += dt;
            if (k.Seconds >= k.NextHunt && !HuntersOf(world, id).Any())
            {
                Hunt(world, id, at, seed, k);
                k.NextHunt = k.Seconds + t.HuntEvery;
            }
            else if (HuntersOf(world, id).Any())
                k.NextHunt = Math.Max(k.NextHunt, k.Seconds + t.HuntEvery);
        }
        foreach (var id in _tracks.Keys.Where(id => !present.Contains(id)).ToList())
        {
            LetGo(world, id, _tracks[id]);
            _tracks.Remove(id);
        }
        _mirror.Clear();
        foreach (var (id, k) in _tracks)
            if (k.Stranded && k.Seconds > 0)
                _mirror.Add(new Abandoned(id, k.Seconds, k.At, k.Held));
    }

    /// <summary>Let go: back aboard, inside a fort, the train back for them, dead, or the night over. Their hunters lose them.</summary>
    static void LetGo(World world, int id, Track k)
    {
        if (!k.Stranded)
            return;
        k.Stranded = k.Held = false;
        k.Seconds = k.Rate = 0;
        k.Hunts = 0;
        foreach (var e in HuntersOf(world, id).ToList())
            e.Dismiss();
    }

    /// <summary>
    /// A pack out of the dark at them (<see cref="AbandonedTuning.Hunters"/>): from <see cref="AbandonedTuning.HuntOut"/> m
    /// off, on the side away from the train, on dice of its own (the director's are the crew's night).
    /// </summary>
    void Hunt(World world, int id, Double3 at, ulong seed, Track k)
    {
        var t = Tuning;
        var et = world.Enemies;
        if (et is null || t.Hunters != "ribbits" || world.Director is { } d && !d.Allows(EnemyKind.Ribbit))
            return;
        var rng = new Pcg32(seed ^ (ulong)(id + 1) * 0x9E3779B97F4A7C15UL, 0xAB4D0 + (ulong)k.Hunts);
        k.Hunts++;
        var train = world.Train;
        var engine = train.Frames[0].Origin;
        var away = (at - engine) with { Y = 0 };
        away = away.Length > 1e-6 ? away.Normalized : new Double3(1, 0, 0);
        double turn = rng.Range(-1.2, 1.2);
        double c = DMath.Cos(turn), s = DMath.Sin(turn);
        var dir = new Double3(away.X * c - away.Z * s, 0, away.X * s + away.Z * c);
        var spot = at + dir * t.HuntOut;
        // Not out of a fort's walls (they'd be turned back at once).
        if (world.InFort(spot))
            spot = at - dir * t.HuntOut;
        double hint = train.Dynamics.Distance;
        spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) };
        int size = Math.Max(2, (int)Math.Round(rng.Range(t.HuntPack[0], t.HuntPack[^1] + 0.49)));
        int pack = world.NextEnemyId;
        for (int i = 0; i < size; i++)
        {
            var here = spot + new Double3(i * 1.2, 0, i % 2 * 1.2);
            world.AddEnemy(n => Ribbit.Hunting(n, pack, here, id, et.Ribbits));
        }
    }

    /// <summary>
    /// The figures at the edge of their lamp (presentation, from the replicated clock and where they are, so every client
    /// draws the same ones): none before <see cref="AbandonedTuning.FiguresFrom"/>, then more of them and nearer, up to
    /// <see cref="AbandonedTuning.Figures"/> at <see cref="AbandonedTuning.FiguresNear"/> m by the time it's deadly. Each
    /// stands somewhere new every <see cref="AbandonedTuning.ShiftSeconds"/>: look back and it's moved. Feet on the ground,
    /// facing them.
    /// </summary>
    public static List<(Double3 Feet, Double3 Facing)> FiguresOf(in Abandoned a, AbandonedTuning t, RailLine line)
    {
        var figures = new List<(Double3, Double3)>();
        if (a.Seconds < t.FiguresFrom || t.Figures <= 0)
            return figures;
        double k = Math.Clamp((a.Seconds - t.FiguresFrom) / Math.Max(1e-6, t.DeadlyAt - t.FiguresFrom), 0, 1);
        int n = Math.Max(1, (int)Math.Ceiling(k * t.Figures));
        double reach = t.FiguresFar + (t.FiguresNear - t.FiguresFar) * k;
        uint beat = (uint)(a.Seconds / Math.Max(0.1, t.ShiftSeconds));
        double hint = 0;
        for (int i = 0; i < n; i++)
        {
            var rng = new Pcg32(((ulong)(uint)a.Player << 32) | beat, 0xF16 + (ulong)i);
            double angle = (i + rng.NextDouble() * 0.8) * 2 * Math.PI / n;
            double r = reach * (0.85 + 0.3 * rng.NextDouble());
            var feet = a.At + new Double3(DMath.Cos(angle) * r, 0, DMath.Sin(angle) * r);
            feet = feet with { Y = PlayerMotor.GroundAt(feet, line, ref hint) };
            var facing = (a.At - feet) with { Y = 0 };
            figures.Add((feet, facing.Length > 1e-6 ? facing.Normalized : new Double3(0, 0, 1)));
        }
        return figures;
    }
}
