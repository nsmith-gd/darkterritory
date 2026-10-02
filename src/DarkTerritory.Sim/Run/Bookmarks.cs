using System.Text.Json;
using System.Text.Json.Serialization;
using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Run;

/// <summary>What made a bookmark (GDD v1.4 App. D.12): the four automatic captures, and a dead player's button.</summary>
public enum BookmarkKind : byte { Grab, Punish, Derail, Stranded, Manual }

/// <summary>
/// A bookmark (GDD v1.4 App. D.12) as the host records it: not pixels, but where the still is to be taken from, so every
/// machine can take it from its own copy of the world. The camera is in the frame of whoever's eyes it is (a car's, or the
/// world's for <see cref="Frame"/> −1), so a client that draws it a few ticks later still has the eye on the same car.
/// </summary>
/// <param name="Id">Numbered from 1 in the order the host made them.</param>
/// <param name="Seconds">The run's clock when it was made: the report's timestamp.</param>
/// <param name="Victim">Whom it's of (the grabbed, the punished, each crew member in a derailment), or −1.</param>
/// <param name="Viewer">Whose eyes: the nearest crewmate who could see it, the victim's own, or (manual) whoever the dead
/// player was following. −1 for a still the client frames itself (the Stranded outro's last frame).</param>
/// <param name="Frame">The car the camera rides (−1: the world).</param>
/// <param name="Eye">The camera's position in <paramref name="Frame"/>.</param>
/// <param name="Look">Its direction in <paramref name="Frame"/>, unit length.</param>
/// <param name="What">For the report: what happened ("Grabbed by the Dragger") and <paramref name="Where"/>, so a GRAB
/// nobody died of still has a line to sit beside.</param>
/// <param name="Taker">A manual bookmark: the dead player who pressed the button.</param>
/// <param name="Name">A manual bookmark: the followed player's name, as the session had it then.</param>
public sealed record Bookmark(int Id, BookmarkKind Kind, double Seconds, int Victim, int Viewer, int Frame,
    [property: JsonConverter(typeof(Double3Json))] Double3 Eye, [property: JsonConverter(typeof(Double3Json))] Double3 Look,
    string What = "", string Where = "", int Taker = -1, string Name = "");

/// <summary>A <see cref="Double3"/> on the wire (the report's and a bookmark's JSON) as [x, y, z]: its derived properties aren't data.</summary>
public sealed class Double3Json : JsonConverter<Double3>
{
    public override Double3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var v = JsonSerializer.Deserialize<double[]>(ref reader, options) ?? [];
        return v.Length == 3 ? new Double3(v[0], v[1], v[2]) : throw new JsonException("a Double3 is [x, y, z]");
    }

    public override void Write(Utf8JsonWriter writer, Double3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Run.json <c>bookmarks</c> (GDD v1.4 App. D.12, D.13). <see cref="AutoCap"/> and <see cref="Priority"/> are D.13's; the rest
/// aren't in the GDD (ARCHITECTURE §8 note 176).
/// </summary>
public sealed record BookmarkTuning
{
    /// <summary>D.13: automatic bookmarks kept per run (12, range 8-20).</summary>
    public int AutoCap { get; init; } = 12;
    /// <summary>D.13: which survive the cap first: derailment cinematic > PUNISH > GRAB start. The Stranded outro's frame
    /// isn't in D.13's list; it's the end of the night, so it ranks with the derailment.</summary>
    public IReadOnlyList<BookmarkKind> Priority { get; init; } = [BookmarkKind.Derail, BookmarkKind.Stranded, BookmarkKind.Punish, BookmarkKind.Grab];
    /// <summary>A crewmate farther than this from the victim didn't see it, line of sight or not: it's night.</summary>
    public double WitnessRange { get; init; } = 60;
    /// <summary>The eye over the feet (the game's camera, <c>Eyes.Height</c>); a dead or seated eye is lower, not tracked here.</summary>
    public double EyeHeight { get; init; } = 1.65;
    /// <summary>Where on the victim a witness looks, over their feet: the chest.</summary>
    public double ChestHeight { get; init; } = 1.1;
    /// <summary>A GRAB or PUNISH belongs to the victim's death line if they died within this many seconds of it.</summary>
    public double LineWindowSeconds { get; init; } = 30;
    /// <summary>GRABs and PUNISHes recorded per run before the host stops taking them (each is a still on every machine).</summary>
    public int RecordLimit { get; init; } = 48;
    /// <summary>Manual bookmarks: per dead player per run, and on the report in all.</summary>
    public int ManualPerPlayer { get; init; } = 3;
    public int ManualPerRun { get; init; } = 8;
}

/// <summary>
/// The night's bookmarks (GDD v1.4 App. D.12). The host writes them: a still at every GRAB start and every PUNISH, from the
/// nearest living crewmate who can see the victim, else from the victim's own eyes; one per crew member at a derailment
/// (taken at the peak of the cinematic's first-person beat, E.5); the Stranded outro's last frame (E.9); and a dead player's
/// own, from the followed view. Clients are sent each as it's made and take the still then, from their own world; the
/// report keeps the ones that survive D.13's cap, beside the line each belongs to.
/// </summary>
public sealed class Bookmarks
{
    readonly List<Bookmark> _all = [];
    readonly HashSet<(int Enemy, int Victim)> _punished = [];
    readonly Dictionary<int, int> _manualBy = [];
    bool _ended;

    public BookmarkTuning Tuning { get; set; } = new();
    public IReadOnlyList<Bookmark> All => _all;

    int NextId => _all.Count == 0 ? 1 : _all[^1].Id + 1;

    bool Room => _all.Count(b => b.Kind is BookmarkKind.Grab or BookmarkKind.Punish) < Tuning.RecordLimit;

    static double Seconds(World world) => world.Run?.Seconds ?? world.Tick * SimConstants.TickSeconds;

    /// <summary>Host: a GRAB began (App. A.9's hook, beside its attribution record).</summary>
    public Bookmark? Grab(World world, int victim, string what, IEnumerable<(int Id, PlayerState State)> crew)
    {
        var list = crew.ToList();
        if (!Room || list.FindIndex(c => c.Id == victim) is var at && at < 0)
            return null;
        var s = list[at].State;
        var (viewer, frame, eye, look) = Witness(world, victim, s, list);
        return Add(new Bookmark(NextId, BookmarkKind.Grab, Seconds(world), victim, viewer, frame, eye, look, what, IncidentLog.Where(world, s)));
    }

    /// <summary>
    /// Host: an enemy enters PUNISH. Of whoever it holds, else of the enemy itself (a car alight, the Sleepers under the
    /// engine). Once per enemy per victim: a marsh that loses its man and finds him again is one punish, not a reel.
    /// </summary>
    public Bookmark? Punish(World world, int enemy, string kind, int victim, Double3 at, IEnumerable<(int Id, PlayerState State)> crew)
    {
        var list = crew.ToList();
        if (!Room || !_punished.Add((enemy, victim)))
            return null;
        string what = $"Punished by the {IncidentLog.Spoken(kind)}";
        if (victim >= 0 && list.FindIndex(c => c.Id == victim) is var i && i >= 0)
        {
            var s = list[i].State;
            var (viewer, frame, eye, look) = Witness(world, victim, s, list);
            return Add(new Bookmark(NextId, BookmarkKind.Punish, Seconds(world), victim, viewer, frame, eye, look, what, IncidentLog.Where(world, s)));
        }
        // Nobody held: whoever's nearest and can see the thing itself; nobody, and there's no picture.
        if (See(world, at, -1, list) is not { } seen)
            return null;
        return Add(new Bookmark(NextId, BookmarkKind.Punish, Seconds(world), -1, seen.Viewer, seen.Frame, seen.Eye, seen.Look, what,
            IncidentLog.At(world, at, world.Train.Dynamics.Distance)));
    }

    /// <summary>
    /// Host, the tick the night ends: a derailment's one still per crew member (each from their own eyes in the car they
    /// rode, taken by every client at the peak of the first-person beat), or the Stranded outro's last frame.
    /// </summary>
    public void End(World world, RunEnd end, IEnumerable<(int Id, PlayerState State)> crew)
    {
        if (_ended || end is not (RunEnd.Derailed or RunEnd.Stranded))
            return;
        _ended = true;
        double now = Seconds(world);
        if (end == RunEnd.Stranded)
        {
            Add(new Bookmark(NextId, BookmarkKind.Stranded, now, -1, -1, -1, default, default, "Consist stranded"));
            return;
        }
        // Everyone the derailment took or threw about; the already dead were watching, not riding.
        foreach (var (id, s) in crew.Where(c => c.State.Alive || c.State.Death is DeathCause.Derailed or DeathCause.TornOff or DeathCause.Thrown).OrderBy(c => c.Id))
        {
            var (frame, eye, look) = OwnView(s);
            Add(new Bookmark(NextId, BookmarkKind.Derail, now, id, id, frame, eye, look, "Consist derailed", IncidentLog.Where(world, s)));
        }
    }

    /// <summary>
    /// Host: a dead player pressed Bookmark (their intent) while following <paramref name="followed"/> (App. D.10). It
    /// stores the time, the followed player's name and their view. Within the per-player and per-run caps; null otherwise.
    /// </summary>
    public Bookmark? Manual(World world, int taker, int followed, in PlayerState followedState)
    {
        if (!followedState.Alive || followed == taker || _manualBy.GetValueOrDefault(taker) >= Tuning.ManualPerPlayer
            || _all.Count(b => b.Kind == BookmarkKind.Manual) >= Tuning.ManualPerRun)
            return null;
        _manualBy[taker] = _manualBy.GetValueOrDefault(taker) + 1;
        var (frame, eye, look) = OwnView(followedState);
        return Add(new Bookmark(NextId, BookmarkKind.Manual, Seconds(world), -1, followed, frame, eye, look, "", IncidentLog.Where(world, followedState),
            taker, IncidentLog.NameOf(world, followed)));
    }

    /// <summary>Client: one the host sent. In order; one already held (resent) is ignored.</summary>
    public void Mirror(Bookmark b)
    {
        if (_all.All(x => x.Id != b.Id))
            _all.Add(b);
    }

    Bookmark Add(Bookmark b)
    {
        _all.Add(b);
        return b;
    }

    /// <summary>A player's own eyes: feet plus the eye's height, looking where they look, in the frame they're in.</summary>
    public (int Frame, Double3 Eye, Double3 Look) OwnView(in PlayerState s) =>
        (s.Parent, s.Position + Double3.Up * Tuning.EyeHeight, Forward(s.Yaw, s.Pitch));

    /// <summary>Radians as the camera has them: yaw 0 looks along −Z, positive turns left; pitch positive looks up.</summary>
    public static Double3 Forward(double yaw, double pitch) =>
        new(-DMath.Sin(yaw) * DMath.Cos(pitch), DMath.Sin(pitch), -DMath.Cos(yaw) * DMath.Cos(pitch));

    /// <summary>D.12: from the nearest living crewmate with line of sight to the victim; from the victim's own eyes if nobody.</summary>
    (int Viewer, int Frame, Double3 Eye, Double3 Look) Witness(World world, int victim, in PlayerState s, List<(int Id, PlayerState State)> crew)
    {
        var chest = PlayerMotor.WorldPosition(s, world.Train) + Double3.Up * Tuning.ChestHeight;
        if (See(world, chest, victim, crew) is { } seen)
            return seen;
        var (frame, eye, look) = OwnView(s);
        return (victim, frame, eye, look);
    }

    /// <summary>
    /// The nearest living crewmate (other than <paramref name="victim"/>) within <see cref="BookmarkTuning.WitnessRange"/>
    /// whose eye has a clear line to <paramref name="target"/> past the train's solids, looking at it; nearest first, then by
    /// id, so host and harness agree. Null if nobody can see it.
    /// </summary>
    public (int Viewer, int Frame, Double3 Eye, Double3 Look)? See(World world, Double3 target, int victim, IEnumerable<(int Id, PlayerState State)> crew)
    {
        var train = world.Train;
        foreach (var (id, s) in crew.Where(c => c.Id != victim && c.State.Alive)
            .Select(c => (c.Id, c.State, D: (PlayerMotor.WorldPosition(c.State, train) + Double3.Up * Tuning.EyeHeight - target).Length))
            .Where(c => c.D <= Tuning.WitnessRange).OrderBy(c => c.D).ThenBy(c => c.Id).Select(c => (c.Id, c.State)))
        {
            var local = s.Position + Double3.Up * Tuning.EyeHeight;
            var eye = s.Parent == PlayerState.World ? local : train.Frames[s.Parent].ToWorld(local);
            var to = target - eye;
            double d = to.Length;
            if (d < 1e-6)
                continue;
            var dir = to * (1 / d);
            if (!Clear(train, eye, dir, d))
                continue;
            var look = s.Parent == PlayerState.World ? dir : train.Frames[s.Parent].DirToLocal(dir);
            return (id, s.Parent, local, look);
        }
        return null;
    }

    /// <summary>
    /// Nothing of the train's between the eye and the target. The ray starts a hand's breadth out (an eye against a wall
    /// isn't blinded by it) and stops short of the target (a victim on a roof stands on it).
    /// </summary>
    static bool Clear(Train.TrainOnLine train, Double3 eye, Double3 dir, double distance)
    {
        const double Skin = 0.15, Short = 0.4;
        if (distance <= Skin + Short)
            return true;
        return Guns.TrainRaycast(train, eye + dir * Skin, dir, distance) >= distance - Skin - Short;
    }

    /// <summary>
    /// The automatic bookmarks the report keeps (D.13): at most <see cref="BookmarkTuning.AutoCap"/>, by
    /// <see cref="BookmarkTuning.Priority"/>, then those beside a death line before those that aren't, then the earliest.
    /// </summary>
    /// <param name="fatal">Whether a bookmark belongs to a death line.</param>
    public static List<Bookmark> Kept(IEnumerable<Bookmark> all, BookmarkTuning t, Func<Bookmark, bool> fatal)
    {
        int Rank(BookmarkKind k) => t.Priority.ToList().IndexOf(k) is var i && i >= 0 ? i : int.MaxValue;
        return [.. all.Where(b => b.Kind != BookmarkKind.Manual)
            .OrderBy(b => Rank(b.Kind)).ThenBy(b => fatal(b) ? 0 : 1).ThenBy(b => b.Seconds).ThenBy(b => b.Id)
            .Take(Math.Max(0, t.AutoCap)).OrderBy(b => b.Id)];
    }
}
