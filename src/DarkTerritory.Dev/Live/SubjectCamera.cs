using System.Globalization;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Dev.Live;

/// <summary>
/// What a shot is framed on (note 524): what it's called, the middle of it (world), its box's half extents along its own
/// axes (<paramref name="Right"/>, <paramref name="Up"/>, <paramref name="Back"/>: a car's frame, or the line's where it
/// stands), and the car it is, if it's one of the train's (-1 for anyone or anything else).
/// </summary>
public sealed record Subject(string Name, Double3 Centre, Double3 Right, Double3 Up, Double3 Back, Double3 Half, int Car = -1);

/// <summary>A subject's camera, what's wrong with it if anything is (it's buried; nowhere had a clear view), and whether it's down a room.</summary>
public sealed record Framing(Camera Camera, string? Note, bool Inside = false);

/// <summary>
/// Shots framed on a named subject instead of hand-placed coordinates (ARCHITECTURE §8 note 524): <c>subject:gaunt</c> (the
/// nearest active one of an enemy kind), <c>subject:enemy:12</c> (that one), <c>subject:crew:2</c>, <c>subject:car:3</c>,
/// <c>subject:engine</c>. The camera stands off it by what it takes to get the whole of its box in the frame, three-quarters
/// on from ahead and a little above, on the side of the line it's on. Of the places that would do, it takes the first that's
/// over the ground, out of the train's bodywork, and has the train and the land out of the way (a cutting's bank); inside a
/// car or the cab, it's down the room at it. A subject that isn't there is an error that says what is, never a frame of
/// something else.
/// </summary>
public static class SubjectCamera
{
    public const string Prefix = "subject:";

    /// <summary>The camera's vertical field of view outside, and in a room (wider: a car's aisle is short).</summary>
    const float Fov = 50, RoomFov = 70;
    /// <summary>How much of the frame the subject's box fills, in whichever of its width and height it fills first.</summary>
    const double Fill = 0.8;
    /// <summary>The nearest the camera comes, and how far it keeps over the ground (an eye stood on it, crouching).</summary>
    const double Nearest = 1.6, OverGround = 1.0;

    /// <summary>
    /// Who or what <paramref name="who"/> (what follows <see cref="Prefix"/>) names, in this world; an
    /// <see cref="ArgumentException"/> naming what's there if it isn't. <see cref="Frame"/> puts the camera on it.
    /// </summary>
    public static Subject Find(string who, World world, IReadOnlyList<PlayerSnapshot> crew, PlayerTuning? player = null)
    {
        var train = world.Train;
        string w = who.Trim();
        if (w.Equals("engine", StringComparison.OrdinalIgnoreCase))
            return Car(train, 0);
        if (Numbered(w, "car") is { } car)
            return car >= 0 && car < train.Frames.Count ? Car(train, car) : throw Absent($"no car {car}", world, crew);
        if (Numbered(w, "crew") is { } id)
            return crew.FirstOrDefault(c => c.Id == id) is { Id: > 0 } mate ? Crewmate(world, mate, player) : throw Absent($"no crewmate {id}", world, crew);
        if (Numbered(w, "enemy") is { } enemy)
            return world.ActiveEnemies.FirstOrDefault(e => e.Id == enemy && !e.Gone) is { } one ? Threat(world, one) : throw Absent($"no enemy {enemy} about", world, crew);
        if (EnemyNames.Parse(w) is not { } kind)
            throw Absent($"'{who}' isn't a subject (name an enemy kind, e.g. gaunt or cinderHound; enemy:N; crew:N; car:N; or engine)", world, crew);
        var nearest = world.ActiveEnemies.Where(e => !e.Gone && e.Kind == kind).MinBy(e => FromTrain(e.WorldPosition(train), train));
        return nearest is not null ? Threat(world, nearest) : throw Absent($"no {EnemyNames.Name(kind)} in the world", world, crew);
    }

    static int? Numbered(string who, string what) =>
        who.StartsWith(what + ":", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(who[(what.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n
                : throw new ArgumentException($"{what}:N takes a number, not '{who[(what.Length + 1)..]}'")
            : null;

    static ArgumentException Absent(string what, World world, IReadOnlyList<PlayerSnapshot> crew) => new($"{what}; there's {WhatsThere(world, crew)}");

    /// <summary>Everything a subject can name, as it stands: the train, the crew, and what's about by kind with each one's id.</summary>
    public static string WhatsThere(World world, IReadOnlyList<PlayerSnapshot> crew)
    {
        // The train's own first; a switchyard's standing cars (note 187) come after them in the frames.
        int own = Math.Min(world.Train.OwnVehicles, world.Train.Frames.Count), frames = world.Train.Frames.Count;
        string train = (own > 1 ? $"engine, car:1..car:{own - 1}" : "engine") + (frames > own ? $", standing cars car:{own}..car:{frames - 1}" : "");
        string mates = crew.Count > 0 ? $"crew:{string.Join(", crew:", crew.Select(c => c.Id.ToString(CultureInfo.InvariantCulture)))}" : "no crew";
        var about = world.ActiveEnemies.Where(e => !e.Gone).GroupBy(e => e.Kind).OrderBy(g => g.Key)
            .Select(g => $"{EnemyNames.Name(g.Key)} (enemy:{string.Join(", enemy:", g.Select(e => e.Id.ToString(CultureInfo.InvariantCulture)))})").ToList();
        return $"{train}; {mates}; {(about.Count > 0 ? string.Join("; ", about) : "no threats about")}";
    }

    static Subject Car(TrainOnLine train, int index)
    {
        var frame = train.Frames[index];
        var b = frame.Shape.Bounds;
        string name = index == 0 ? "the engine" : $"car {index}" + (index < train.Vehicles.Count ? $" ({train.Vehicles[index].Kind.ToString().ToLowerInvariant()})" : "");
        return new Subject(name, frame.ToWorld(b.Centre), frame.Right, frame.Up, frame.Back, b.HalfSize, index);
    }

    static Subject Crewmate(World world, PlayerSnapshot mate, PlayerTuning? player)
    {
        var train = world.Train;
        var feet = PlayerMotor.WorldPosition(mate.State, train);
        // Stood, their height; dead, lying where they fell: about as long as they're tall, and low.
        double height = player?.Height ?? 1.8, girth = player?.Radius ?? 0.3;
        var (right, back) = Axes(train, feet);
        var half = mate.State.Alive ? new Double3(girth + 0.1, height / 2, girth + 0.1) : new Double3(height / 2, girth, height / 2);
        string name = world.Names.TryGetValue(mate.Id, out var n) && n.Length > 0 ? $"crewmate {mate.Id} ({n})" : $"crewmate {mate.Id}";
        return new Subject(name, feet + Double3.Up * half.Y, right, Double3.Up, back, half);
    }

    static Subject Threat(World world, Enemy e)
    {
        var train = world.Train;
        var at = e.WorldPosition(train);
        // Its body as the guns find it (enemies.json bodies, stooped as it stoops), or about a man's size with none.
        var body = world.Enemies?.Body(e.Kind) ?? [];
        double stoop = world.Enemies is { } t ? e.Stoop(t) : 1;
        double top = body.Length > 0 ? body.Max(s => s.Height * stoop + s.Radius) : 1.6;
        double girth = body.Length > 0 ? body.Max(s => s.Radius) : 0.5;
        var (right, back) = Axes(train, at);
        return new Subject($"{EnemyNames.Name(e.Kind)} (enemy:{e.Id}, {e.Phase.ToString().ToLowerInvariant()})", at + Double3.Up * (top / 2), right, Double3.Up, back,
            new Double3(girth, top / 2, girth));
    }

    /// <summary>The line's right and back where a point is (the train's forward is the line's way on).</summary>
    static (Double3 Right, Double3 Back) Axes(TrainOnLine train, Double3 at)
    {
        double hint = train.Dynamics.Distance;
        var (path, along) = train.Line.Nearest(at, ref hint);
        var tangent = train.Line.Sample(path, along).Tangent;
        return (Double3.Cross(tangent, Double3.Up).Normalized, tangent * -1);
    }

    /// <summary>How far a point is from the train's nearest bodywork (0 on or in it).</summary>
    public static double FromTrain(Double3 at, TrainOnLine train)
    {
        double best = double.PositiveInfinity;
        foreach (var f in Views.Train(train.Frames, train.StandingCar))
        {
            var l = f.ToLocal(at);
            var b = f.Shape.Bounds;
            best = Math.Min(best, new Double3(Beyond(l.X, b.Min.X, b.Max.X), Beyond(l.Y, b.Min.Y, b.Max.Y), Beyond(l.Z, b.Min.Z, b.Max.Z)).Length);
        }
        return best;
    }

    static double Beyond(double v, double min, double max) => v < min ? min - v : v > max ? v - max : 0;

    /// <summary>
    /// The camera on a subject: down the room at it if it's inside a car or the cab, else off it outside; and a note when the
    /// frame can't show it as asked (it's under the ground where it stands, or nowhere round it has a clear view).
    /// </summary>
    public static Framing Frame(Subject s, World world, double aspect = 16.0 / 9)
    {
        if (s.Car < 0 && RoomAround(s.Centre, world.Train) is (var frame, var room))
            return new Framing(InRoom(s, frame, room), null, Inside: true);
        return Outside(s, world, aspect);
    }

    /// <summary>The car (or the engine's cab) whose room a point's in.</summary>
    static (CarFrame Frame, Box Room)? RoomAround(Double3 at, TrainOnLine train)
    {
        foreach (var f in train.Frames)
        {
            var l = f.ToLocal(at);
            foreach (var room in new[] { f.Shape.Interior, f.Shape.Cab })
                if (room is { } r && r.Contains(l))
                    return (f, r);
        }
        return null;
    }

    static Camera InRoom(Subject s, CarFrame frame, Box room)
    {
        // Down the room's length at it, from the end with more room, at a standing eye (under the ceiling).
        var at = frame.ToLocal(s.Centre);
        double away = Math.Max(Nearest, s.Half.Y * 2 / Math.Tan(RoomFov * Math.PI / 360) / Fill);
        double z = at.Z < room.Centre.Z ? Math.Min(room.Max.Z - 0.3, at.Z + away) : Math.Max(room.Min.Z + 0.3, at.Z - away);
        double x = Math.Clamp(at.X * -0.5, room.Min.X + 0.3, room.Max.X - 0.3);
        var eye = new Double3(x, Math.Min(room.Min.Y + 1.65, room.Max.Y - 0.15), z);
        return Camera.LookAt(frame.ToWorld(eye), s.Centre, RoomFov);
    }

    static Framing Outside(Subject s, World world, double aspect)
    {
        var train = world.Train;
        var forward = s.Back * -1;
        double hint = train.Dynamics.Distance;
        var (path, along) = train.Line.Nearest(s.Centre, ref hint);
        var line = train.Line.Sample(path, along);
        // On the side of the line it's on, so the train isn't between; on the line (or the train), its right.
        double lateral = Double3.Dot(s.Centre - line.Position, s.Right);
        int side = s.Car < 0 && lateral < -0.5 ? -1 : 1;
        bool tunnel = world.Route?.InTunnel(along) == true;
        var conditions = train.Line.Conditions;
        // Something put in the ground (the sim has it there; the scene draws it there) is out of sight from anywhere: said.
        var feet = s.Centre - s.Up * s.Half.Y;
        double under = PlayerMotor.GroundAt(feet, train.Line, ref hint) - feet.Y;
        string? buried = s.Car < 0 && !tunnel && under > 0.5 && FromTrain(s.Centre, train) > 0.5
            ? $"it's {under:0.0} m under the ground where it stands (its feet at {feet.Y:0.0} m, the ground at {feet.Y + under:0.0} m): drawn where the sim has it, out of sight"
            : null;
        (Camera Camera, int Faults, List<string> Why)? best = null;
        // Three-quarters on from ahead, then from behind, then square on; on its side, then the other; low, then higher.
        foreach (double elevation in new[] { 15.0, 35, 60 })
            foreach (int sideways in new[] { side, -side })
                foreach (double azimuth in new[] { 45.0, 135, 90 })
                {
                    double a = azimuth * Math.PI / 180, e = elevation * Math.PI / 180;
                    var dir = ((forward * Math.Cos(a) + s.Right * (sideways * Math.Sin(a))) * Math.Cos(e) + Double3.Up * Math.Sin(e)).Normalized;
                    var eye = s.Centre + dir * Distance(s, dir, aspect);
                    int faults = 0;
                    var why = new List<string>();
                    double ground = PlayerMotor.GroundAt(eye, train.Line, ref hint) + OverGround;
                    if (eye.Y < ground)
                    {
                        // Up out of a bank to stand on it, as dt playthrough's shots do; a long way up is a worse place.
                        if (ground - eye.Y > 1.5)
                        {
                            faults += 1;
                            why.Add($"stood {ground - eye.Y:0.0} m up a bank");
                        }
                        eye = eye with { Y = ground };
                    }
                    if (tunnel && conditions is not null)
                        eye = conditions.Confine(eye, 0.3);
                    if (InBodywork(eye, train))
                    {
                        faults += 4;
                        why.Add("in the train's bodywork");
                    }
                    if (TrainBetween(eye, s.Centre, train))
                    {
                        faults += 2;
                        why.Add("the train in the way");
                    }
                    if (!tunnel && LandBetween(eye, s.Centre, train))
                    {
                        faults += 2;
                        why.Add("the land in the way");
                    }
                    if (best is null || faults < best.Value.Faults)
                        best = (Camera.LookAt(eye, s.Centre, Fov), faults, why);
                    if (faults == 0)
                        return new Framing(best.Value.Camera, buried);
                }
        return new Framing(best!.Value.Camera, buried ?? $"no clear view of it from round it: the best had {string.Join(" and ", best.Value.Why)}");
    }

    /// <summary>
    /// How far off along <paramref name="dir"/> the whole of the subject's box is in the frame: each corner's offset across and
    /// up the view against the field of view, at its own depth (a near corner needs more room than the middle).
    /// </summary>
    public static double Distance(Subject s, Double3 dir, double aspect, double fov = Fov)
    {
        double tanUp = Math.Tan(fov * Math.PI / 360) * Fill, tanAcross = tanUp * aspect;
        var look = dir * -1;
        var across = Double3.Cross(look, Double3.Up).Normalized;
        var up = Double3.Cross(across, look);
        double d = Nearest;
        foreach (int i in new[] { -1, 1 })
            foreach (int j in new[] { -1, 1 })
                foreach (int k in new[] { -1, 1 })
                {
                    var corner = s.Right * (i * s.Half.X) + s.Up * (j * s.Half.Y) + s.Back * (k * s.Half.Z);
                    double toward = Double3.Dot(corner, dir);
                    d = Math.Max(d, toward + Math.Abs(Double3.Dot(corner, across)) / tanAcross);
                    d = Math.Max(d, toward + Math.Abs(Double3.Dot(corner, up)) / tanUp);
                }
        return d;
    }

    /// <summary>Inside one of the cars' boxes (a little grown: the near plane's to be clear of the paint).</summary>
    public static bool InBodywork(Double3 eye, TrainOnLine train)
    {
        foreach (var f in train.Frames)
        {
            var l = f.ToLocal(eye);
            var b = f.Shape.Bounds;
            if (l.X > b.Min.X - 0.25 && l.X < b.Max.X + 0.25 && l.Y > b.Min.Y - 0.25 && l.Y < b.Max.Y + 0.25 && l.Z > b.Min.Z - 0.25 && l.Z < b.Max.Z + 0.25)
                return true;
        }
        return false;
    }

    /// <summary>A car's box across the sight line (not the one the subject's in or is: that one's the subject).</summary>
    static bool TrainBetween(Double3 eye, Double3 target, TrainOnLine train)
    {
        foreach (var f in train.Frames)
        {
            var b = f.Shape.Bounds;
            var to = f.ToLocal(target);
            if (new Box(b.Min - new Double3(0.3, 0.3, 0.3), b.Max + new Double3(0.3, 0.3, 0.3)).Contains(to))
                continue;
            if (Crosses(f.ToLocal(eye), to, b))
                return true;
        }
        return false;
    }

    /// <summary>The segment from a to b passes through the box (slabs).</summary>
    static bool Crosses(Double3 a, Double3 b, Box box)
    {
        double t0 = 0, t1 = 1;
        var d = b - a;
        foreach (var (o, v, min, max) in new[] { (a.X, d.X, box.Min.X, box.Max.X), (a.Y, d.Y, box.Min.Y, box.Max.Y), (a.Z, d.Z, box.Min.Z, box.Max.Z) })
        {
            if (Math.Abs(v) < 1e-9)
            {
                if (o < min || o > max)
                    return false;
                continue;
            }
            double ta = (min - o) / v, tb = (max - o) / v;
            t0 = Math.Max(t0, Math.Min(ta, tb));
            t1 = Math.Min(t1, Math.Max(ta, tb));
            if (t0 > t1)
                return false;
        }
        return true;
    }

    /// <summary>The ground over the sight line somewhere along it (a cutting's bank, a rise between).</summary>
    static bool LandBetween(Double3 eye, Double3 target, TrainOnLine train)
    {
        double hint = train.Dynamics.Distance;
        for (int i = 1; i < 10; i++)
        {
            var p = Double3.Lerp(eye, target, i / 10.0);
            if (PlayerMotor.GroundAt(p, train.Line, ref hint) > p.Y + 0.1)
                return true;
        }
        return false;
    }
}

/// <summary>Enemy kinds by name, as an agent writes them: the kind (<c>cinderHound</c>), the tuning's key (<c>cinderHounds</c>), any case, spaces, dashes.</summary>
public static class EnemyNames
{
    /// <summary>The kind's name: its own, camel-cased, as enemies.json's bodies name it.</summary>
    public static string Name(EnemyKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    public static EnemyKind? Parse(string name)
    {
        string key = Squash(name);
        if (key.Length == 0)
            return null;
        foreach (var kind in Enum.GetValues<EnemyKind>())
            if (Squash(kind.ToString()) == key || Squash(Director.Key(kind)) == key)
                return kind;
        // A plural the tuning doesn't use ("gaunts", "moose" stays "moose").
        return key.EndsWith('s') ? Parse(key[..^1]) : null;
    }

    /// <summary>Kinds from their names; an <see cref="ArgumentException"/> naming the ones it doesn't know.</summary>
    public static IReadOnlyList<EnemyKind> ParseAll(IEnumerable<string> names)
    {
        var list = names.ToList();
        var unknown = list.Where(n => Parse(n) is null).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"no enemy kind {string.Join(", ", unknown.Select(u => $"'{u}'"))} (kinds: {string.Join(", ", Enum.GetValues<EnemyKind>().Select(Name))})");
        return [.. list.Select(n => Parse(n)!.Value).Distinct()];
    }

    static string Squash(string name) => new([.. name.Where(char.IsLetter).Select(char.ToLowerInvariant)]);
}
