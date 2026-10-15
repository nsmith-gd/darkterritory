using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// What a sound cue in the world is (queue #466, ARCHITECTURE §8 note 732; the director, 14 Oct 2026, cutting the dead's
/// haunt: "I do like the idea of using sound-only cues in the design of the environment"). Each is heard where it is and never
/// on the HUD, and each comes before what it tells of (App. A.1's fairness contract: a sound before a threat).
/// </summary>
public enum CueKind : byte
{
    /// <summary>Boards taking a weight where a house creature waits: the Lodger in its corner, the Hanger in the roof, the Householder in its chair.</summary>
    Creak = 1,
    /// <summary>The Householder shifting in its chair as one of its things is lifted and nothing's been left to pay for it.</summary>
    Shift = 2,
    /// <summary>A far bell from where something of the crew's has been lost: cars taken, a cut left to the night, a Holdout left.</summary>
    Bell = 3,
    /// <summary>A dog barking far off, out past something moving at the lamps' edge.</summary>
    Bark = 4,
}

/// <summary>Why a far bell rang (<see cref="WorldCue.Of"/> of a <see cref="CueKind.Bell"/>).</summary>
public enum BellFor : byte { None, CarsTaken, CutLost, HoldoutLeft }

/// <summary>
/// A sound cue in the world (note 732): host-made, kept on the wire <see cref="SoundCuesTuning.KeepSeconds"/> so a dropped
/// snapshot doesn't lose it, and heard once by every client (GameAudio, by its id).
/// </summary>
/// <param name="At">Where it's heard from (the world).</param>
/// <param name="Subject">What it's of: the creature's id (a creak, a shift, a bark), the car or the Holdout (a bell).</param>
/// <param name="Of">The creature's <see cref="EnemyKind"/> (a creak, a shift, a bark), or the <see cref="BellFor"/> (a bell).</param>
/// <param name="Strength">0-1, how near what it tells of is (a creak's: 1 at its nearest); 1 for the rest.</param>
public readonly record struct WorldCue(int Id, uint Tick, CueKind Kind, Double3 At, int Subject, byte Of, double Strength);

/// <summary>enemies.json <c>soundCues</c> (note 732). Field docs live in that file.</summary>
public sealed record SoundCuesTuning
{
    public bool Enabled { get; init; } = true;
    public double KeepSeconds { get; init; } = 1;
    public CreakTuning Creak { get; init; } = new();
    public BellTuning Bell { get; init; } = new();
    public BarkTuning Bark { get; init; } = new();
}

/// <summary>enemies.json <c>soundCues.creak</c>: the boards where a house creature waits.</summary>
public sealed record CreakTuning
{
    public bool Enabled { get; init; } = true;
    public double Within { get; init; } = 10;
    public double Near { get; init; } = 2;
    public double EveryFar { get; init; } = 2.4;
    public double EveryNear { get; init; } = 0.7;
}

/// <summary>enemies.json <c>soundCues.bell</c>: the far bell for what's been lost out of the crew's sight.</summary>
public sealed record BellTuning
{
    public bool Enabled { get; init; } = true;
    public bool CarsTaken { get; init; } = true;
    public bool CutsLost { get; init; } = true;
    public bool HoldoutsLeft { get; init; } = true;
    public double UnseenBeyond { get; init; } = 60;
    public double Height { get; init; } = 6;
}

/// <summary>enemies.json <c>soundCues.bark</c>: the dog far off, past what moves at the lamps' edge.</summary>
public sealed record BarkTuning
{
    public bool Enabled { get; init; } = true;
    public double EdgeFrom { get; init; } = 30;
    public double EdgeTo { get; init; } = 55;
    public double MovingAbove { get; init; } = 0.8;
    public double Beyond { get; init; } = 120;
    public double EveryCreature { get; init; } = 30;
    public double AtMost { get; init; } = 8;
}

/// <summary>
/// The sound cues in the world (queue #466, note 732), made on the host after the creatures have moved, from what the world
/// is: nothing here changes a player or the train, it only says what's there to be heard.
/// <list type="bullet">
/// <item><b>What waits in a house:</b> boards that creak where a house creature waits, only within <see cref="CreakTuning.Within"/>
/// and quicker the nearer anyone comes (the Lodger's corner before it shrieks, the Hanger's roof before a strand's touched), and
/// the Householder's chair shifting as one of its things is lifted unpaid, its chair creaking as it's carried to the door,
/// before it rises.</item>
/// <item><b>What's been hit:</b> a far bell where something of the crew's is lost out of their sight: cars taken with nobody by
/// them, a cut of theirs left past run.json <c>stranded.kitLostBeyond</c> (§23.2's 400 m: the night has it), a Holdout left
/// with a friend still in it (<see cref="World.StepHoldouts"/>).</item>
/// <item><b>Something at the edge:</b> a dog barking far off, out past a creature moving at the lamps' edge (between
/// <see cref="BarkTuning.EdgeFrom"/> and <see cref="BarkTuning.EdgeTo"/> of the train and the crew).</item>
/// </list>
/// What's about to go is the train's own state: a loose pin's knock and what jumped it (<see cref="Vehicle.LooseCause"/>), a
/// hot box's squeal, a lamp's gutter.
/// </summary>
public sealed class SoundCues
{
    /// <summary>The tick each waiting creature next creaks on (by its id): looked up, never walked.</summary>
    readonly Dictionary<int, uint> _nextCreak = new();
    /// <summary>The things each Householder's already shifted for (by its id): the shift is once a lift.</summary>
    readonly Dictionary<int, HashSet<int>> _shifted = new();
    /// <summary>Where each creature was last tick, and the tick a dog last barked at it.</summary>
    readonly Dictionary<int, Double3> _was = new();
    readonly Dictionary<int, uint> _barkedAt = new();
    uint? _lastBark;
    /// <summary>Every car that's run in the engine's rake tonight (the crew's), the cars seen taken, the cuts rung for.</summary>
    readonly HashSet<int> _ours = [], _taken = [], _lostCuts = [];

    /// <summary>The tuning this step: the world's, read afresh each tick (a dev build's hot reload, note 687).</summary>
    SoundCuesTuning tuning = new();

    /// <summary>A sound cue made: its kind, where, what it's of, the kind of that, how near.</summary>
    public delegate void Make(CueKind kind, Double3 at, int subject, byte of, double strength);

    /// <summary>Host, after the creatures' step. <paramref name="living"/> the living crew and where each stands, in id order.</summary>
    public void Step(World world, IReadOnlyList<(int Id, Double3 At)> living, Make cue)
    {
        tuning = world.SoundCuesTuning;
        if (!tuning.Enabled)
            return;
        var train = world.Train;
        foreach (var v in train.Dynamics.Consist.Vehicles)
            _ours.Add(v.Id);
        if (tuning.Creak.Enabled && train.Walls is { } walls)
            foreach (var e in world.ActiveEnemies)
                Waits(world, walls, e, living, cue);
        if (tuning.Bell.Enabled)
            Bells(world, living, cue);
        if (tuning.Bark.Enabled)
            Barks(world, living, cue);
        // Forget what's gone (each key on its own: the order they go in changes nothing).
        var here = world.ActiveEnemies.Where(e => !e.Gone).Select(e => e.Id).ToHashSet();
        Forget(_nextCreak, here);
        Forget(_was, here);
        Forget(_barkedAt, here);
        Forget(_shifted, here);
    }

    static void Forget<T>(Dictionary<int, T> of, HashSet<int> here)
    {
        foreach (int id in of.Keys.Where(id => !here.Contains(id)).ToList())
            of.Remove(id);
    }

    /// <summary>The house creatures waiting: where their boards creak, and how near the nearest of the living is to what it waits for.</summary>
    void Waits(World world, StopWalls walls, Enemy e, IReadOnlyList<(int Id, Double3 At)> living, Make cue)
    {
        if (e.Gone || living.Count == 0)
        {
            _nextCreak.Remove(e.Id);
            return;
        }
        switch (e)
        {
            // In its corner: how near anyone is to it (its shriek comes at lodger.shriekWithin, or a while in its house).
            case Lodger { Mode: LodgerMode.Hidden } l:
                Creak(world, e, l.Hide + Double3.Up * 0.1, living.Min(c => Flat(c.At - l.Hide)), cue);
                break;
            // Up in the roof: how near anyone is to one of its strands (touching one is the grab).
            case Hanger { Mode: HangerMode.Hidden } h when h.StrandsAt.Count > 0:
                Creak(world, e, h.Local, living.Min(c => h.StrandsAt.Min(s => Flat(c.At - s))), cue);
                break;
            case Householder { Mode: HouseholderMode.Sit or HouseholderMode.Watch } hh:
                Householder(world, walls, hh, living, cue);
                break;
            default:
                _nextCreak.Remove(e.Id);
                break;
        }
    }

    /// <summary>
    /// The Householder (note 584): one of its things lifted with nothing left on its table to pay for it, it shifts in its
    /// chair (once a lift); carried on toward its door, its chair creaks, quicker the nearer the door. Out of the door it rises.
    /// </summary>
    void Householder(World world, StopWalls walls, Householder hh, IReadOnlyList<(int Id, Double3 At)> living, Make cue)
    {
        // Its things in the living's hands, still in its house (out of the door it rises: that's its own tell).
        var carried = world.Bodies.All.Where(b => b.Carrier >= 0 && hh.Mine.Contains(b.Id)).OrderBy(b => b.Id)
            .SelectMany(b => living.Where(c => c.Id == b.Carrier).Select(c => (Thing: b.Id, c.At)))
            .Where(c => walls.InHouse(hh.House, c.At + Double3.Up * 0.5)).ToList();
        if (!_shifted.TryGetValue(hh.Id, out var shifted))
            _shifted[hh.Id] = shifted = [];
        var now = carried.Select(c => c.Thing).ToHashSet();
        shifted.RemoveWhere(id => !now.Contains(id));
        if (carried.Count <= hh.Credit)
        {
            _nextCreak.Remove(hh.Id);
            return;
        }
        bool shifts = false;
        foreach (var (thing, _) in carried)
            if (shifted.Add(thing))
            {
                cue(CueKind.Shift, hh.Local + Double3.Up * 0.4, hh.Id, (byte)hh.Kind, 1);
                shifts = true;
            }
        // Its chair creaks after the shift, not over it.
        if (shifts)
        {
            _nextCreak[hh.Id] = world.Tick + (uint)Math.Max(1, Math.Round(tuning.Creak.EveryNear * SimConstants.TickRate));
            return;
        }
        var doors = walls.HouseDoors.Where(d => d.House == hh.House).Select(d => d.At).ToList();
        if (doors.Count > 0)
            Creak(world, hh, hh.Local + Double3.Up * 0.4, carried.Min(c => doors.Min(d => Flat(c.At - d))), cue);
    }

    /// <summary>
    /// A creak, if it's time: the first as soon as anyone's within <see cref="CreakTuning.Within"/>, then every
    /// <see cref="CreakTuning.EveryFar"/> s there, quickening to <see cref="CreakTuning.EveryNear"/> by <see cref="CreakTuning.Near"/>
    /// (a little uneven, as a board is: a hash of the creature and the tick, the same on every run).
    /// </summary>
    void Creak(World world, Enemy e, Double3 at, double distance, Make cue)
    {
        var t = tuning.Creak;
        if (distance > t.Within)
        {
            _nextCreak.Remove(e.Id);
            return;
        }
        if (_nextCreak.TryGetValue(e.Id, out uint next) && world.Tick < next)
            return;
        double near = Math.Clamp((t.Within - distance) / Math.Max(1e-6, t.Within - t.Near), 0, 1);
        cue(CueKind.Creak, at, e.Id, (byte)e.Kind, near);
        double every = (t.EveryFar + (t.EveryNear - t.EveryFar) * near) * (0.85 + 0.3 * Unit(world.Tick, e.Id));
        _nextCreak[e.Id] = world.Tick + (uint)Math.Max(1, Math.Round(every * SimConstants.TickRate));
    }

    /// <summary>
    /// The far bell: a car of the crew's taken with nobody of the living within <see cref="BellTuning.UnseenBeyond"/> of it; a
    /// cut of theirs that's every living crewmate further than §23.2's distance from it (once, till someone's back by it).
    /// </summary>
    void Bells(World world, IReadOnlyList<(int Id, Double3 At)> living, Make cue)
    {
        var train = world.Train;
        var t = tuning.Bell;
        double up = t.Height;
        foreach (var v in train.Vehicles)
        {
            if (!v.Taken || !_taken.Add(v.Id) || !t.CarsTaken || living.Count == 0 || v.Id >= train.Frames.Count || !_ours.Contains(v.Id))
                continue;
            var at = train.Frames[v.Id].Origin;
            if (living.All(c => (c.At - at).Length > t.UnseenBeyond))
                cue(CueKind.Bell, at + Double3.Up * up, v.Id, (byte)BellFor.CarsTaken, 1);
        }
        if (!t.CutsLost || living.Count == 0)
            return;
        double beyond = world.Run?.Tuning.Stranded.KitLostBeyond ?? new StrandedTuning().KitLostBeyond;
        foreach (var rake in train.Rakes)
        {
            if (rake == train.Dynamics)
                continue;
            var cars = rake.Consist.Vehicles.Where(v => !v.Derelict && !v.Taken && _ours.Contains(v.Id) && v.Id < train.Frames.Count).Select(v => v.Id).ToList();
            if (cars.Count == 0)
                continue;
            int key = rake.Consist.Vehicles[0].Id;
            double nearest = living.Min(c => cars.Min(car => (c.At - train.Frames[car].Origin).Length));
            if (nearest <= beyond)
                _lostCuts.Remove(key);
            else if (_lostCuts.Add(key))
                cue(CueKind.Bell, train.Frames[cars[cars.Count / 2]].Origin + Double3.Up * up, key, (byte)BellFor.CutLost, 1);
        }
    }

    /// <summary>
    /// A dog far off: a creature (not a hazard, nor Dave or Jacob, nor aboard) moving faster than <see cref="BarkTuning.MovingAbove"/>
    /// between <see cref="BarkTuning.EdgeFrom"/> and <see cref="BarkTuning.EdgeTo"/> of the engine's rake and the living, barked
    /// at from <see cref="BarkTuning.Beyond"/> further out; not again at it for <see cref="BarkTuning.EveryCreature"/> s, nor at
    /// anything for <see cref="BarkTuning.AtMost"/>.
    /// </summary>
    void Barks(World world, IReadOnlyList<(int Id, Double3 At)> living, Make cue)
    {
        var t = tuning.Bark;
        var train = world.Train;
        foreach (var e in world.ActiveEnemies)
        {
            if (e.Gone || e.Hazard || e.Kind is EnemyKind.Dave or EnemyKind.Jacob || e.Attached >= 0)
                continue;
            var p = e.WorldPosition(train);
            bool seen = _was.TryGetValue(e.Id, out var was);
            _was[e.Id] = p;
            if (!seen || Flat(p - was) / SimConstants.TickSeconds <= t.MovingAbove)
                continue;
            var (d, from) = Edge(train, p, living);
            if (d < t.EdgeFrom || d > t.EdgeTo)
                continue;
            if (_barkedAt.TryGetValue(e.Id, out uint at) && world.Tick - at < t.EveryCreature * SimConstants.TickRate)
                continue;
            if (_lastBark is { } last && world.Tick - last < t.AtMost * SimConstants.TickRate)
                continue;
            var way = (p - from) with { Y = 0 };
            way = way.Length > 1e-6 ? way.Normalized : new Double3(1, 0, 0);
            cue(CueKind.Bark, p + way * t.Beyond, e.Id, (byte)e.Kind, 1);
            _barkedAt[e.Id] = world.Tick;
            _lastBark = world.Tick;
        }
    }

    /// <summary>How far <paramref name="p"/> is from the lamps (flat): the engine's rake's cars and the living afoot; and the nearest point of them.</summary>
    static (double Distance, Double3 From) Edge(TrainOnLine train, Double3 p, IReadOnlyList<(int Id, Double3 At)> living)
    {
        double best = double.MaxValue;
        var from = p;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            var f = train.Frames[v.Id];
            var local = f.ToLocal(p);
            double z = Math.Clamp(local.Z, -f.Shape.HalfLength, f.Shape.HalfLength);
            var near = f.ToWorld(new Double3(0, 0, z));
            double d = Flat(p - near);
            if (d < best)
                (best, from) = (d, near);
        }
        foreach (var (_, at) in living)
            if (Flat(p - at) < best)
                (best, from) = (Flat(p - at), at);
        return (best, from);
    }

    static double Flat(Double3 v) => Math.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>0-1 from a creature and a tick: the same on every run.</summary>
    static double Unit(uint tick, int id)
    {
        uint x = tick * 2654435761u ^ (uint)id * 40503u;
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return (x & 0xFFFFFF) / (double)0xFFFFFF;
    }
}
