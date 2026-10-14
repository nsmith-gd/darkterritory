using System.Numerics;
using Ballast;
using Ballast.Platform;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Dev.Gym;

/// <summary>
/// The creature gym (GDD §34.1, ARCHITECTURE §8 note 676), a developer build's: a solo night standing at a stop with a yard and
/// a village, where nothing comes unless it's called. <b>F6</b> picks the next creature (<b>Shift+F6</b> the one before),
/// <b>F7</b> calls it, by its own spawn rule with that rule's gates set aside and never within its reach of you (the Sim's
/// <see cref="World.Call"/>), and <b>F10</b> clears everything about. A death stands you up again at the safe ring by the
/// engine (<see cref="World.SafeRing"/>, kept as a fort is) a few seconds after the blow. Lamps are set down lit about the
/// stop, and the frame's light and air are the gym's own (<see cref="Light"/>), for filming.
/// <para>It works on the host's world between its steps, as the developer tools do (note 524's insist): the creatures it
/// calls are the host's, and every client is sent them as any night's.</para>
/// </summary>
public sealed class CreatureGym
{
    public const string ToolName = "creature-gym";

    /// <summary>Every kind the gym can call, in the roster's order (GDD §21, then the figures and the hazards).</summary>
    public static readonly IReadOnlyList<EnemyKind> Roster = [.. Enum.GetValues<EnemyKind>()];

    CreatureGym(GymTuning tuning, string content)
    {
        Tuning = tuning;
        Content = content;
    }

    public GymTuning Tuning { get; }
    public string Content { get; }

    /// <summary>The kind F7 calls.</summary>
    public EnemyKind Picked => Roster[_pick];
    int _pick;

    /// <summary>What the last call or clear said (the panel's second line), and when.</summary>
    public string Said { get; private set; } = "";
    double _saidAt = double.NegativeInfinity;

    /// <summary>The engine's front where the gym stands it, and the stop it's at.</summary>
    public double Front { get; private set; }
    public RouteFeature? Stop { get; private set; }

    /// <summary>Where along the line the stop's first points are (its yard's throat), and its village's middle.</summary>
    public double? FirstPoints { get; private set; }
    public double Village { get; private set; }

    /// <summary>Loads gym.json; the gym's night is <see cref="Setup"/>.</summary>
    public static CreatureGym Load(string content) => new(GymTuning.Load(content), content);

    /// <summary>
    /// The gym's night: its line and train, standing at its stop (the first on the line of <see cref="GymTuning.Stop"/>'s
    /// kind; failing that the first with a village, then the first stop of any kind), the threats on.
    /// </summary>
    public SessionSetup Setup()
    {
        var route = Sim.LineGen.Routes.Generate(Content, Tuning.Route, Tuning.Cars);
        var stops = route.Of(FeatureKind.Facility).Where(f => f.Stop is not null).ToList();
        var want = Enum.TryParse<StopKind>(Tuning.Stop, ignoreCase: true, out var k) ? k : StopKind.YardAndVillage;
        Stop = stops.FirstOrDefault(f => f.Stop!.Kind == want) ?? stops.FirstOrDefault(f => f.Stop!.Kind != StopKind.Yard) ?? stops.FirstOrDefault()
            ?? route.Of(FeatureKind.Facility).FirstOrDefault();
        var train = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        double length = Sim.Train.Consist.Uniform(train, Tuning.Cars, 1).LengthMetres;
        // The train's middle beside the stop's village (its houses and their creatures a short walk off), else the stop's middle.
        Front = Stop is { } s ? s.Start + Middle(s) + length / 2 : route.Length * 0.25;
        Village = Stop is { } v ? v.Start + Middle(v) : Front;
        // Or the engine short of the stop's first points, so the Switchman has some ahead to work (the director, 13 Oct, App.
        // F.5: "Switchman cannot be tested that far away"); the village is round the yard's throat, so still a walk from the
        // engine, and if it isn't, the train stays beside it.
        FirstPoints = Stop is { Stop: { Tracks.Count: > 0 } layout } at ? at.Start + layout.Tracks.Min(x => x.Toe) : null;
        if (FirstPoints is { } toe && Tuning.PointsAhead > 0 && Math.Abs(Village - (toe - Tuning.PointsAhead)) <= Tuning.MaxDistance * 0.75)
            Front = toe - Tuning.PointsAhead;
        return new SessionSetup(Route: Tuning.Route, Cars: Tuning.Cars, Enemies: true) { Start = Front };
    }

    /// <summary>
    /// Makes the host's night the gym's: nothing comes by itself (<see cref="World.CallOnly"/>), the safe ring beside the
    /// engine, and the lamps set down lit along the train. Once, after the night's hosted.
    /// </summary>
    public void Begin(HostSession host)
    {
        var world = host.World;
        world.CallOnly = true;
        var train = world.Train;
        double front = train.Dynamics.Distance;
        // The side away from the stop's village (its houses are where the house creatures and the Soot Children are).
        int villageSide = Stop?.Stop is { } layout ? VillageSide(layout) : 1;
        var at = train.Line.Sample(train.Dynamics.Path, front - 4);
        var right = Double3.Cross(at.Tangent, Double3.Up).Normalized;
        var centre = at.Position + right * (-villageSide * Tuning.SafeRing.Out);
        double ringHint = front;
        centre = centre with { Y = PlayerMotor.GroundAt(centre, train.Line, ref ringHint) };
        world.SafeRing = (centre, Tuning.SafeRing.Radius);
        SafeSpot = centre;
        // The lamps: lit, set down along the train on alternate sides, out past its edge (each a hand lamp: it can be taken).
        var dice = new Pcg32(world.Route?.Seed ?? 7, 0x67_796DUL);
        double rear = train.Dynamics.RearDistance;
        for (int i = 0; i < Tuning.Lamps.Count; i++)
        {
            double along = rear + (front - rear) * (i + 0.5) / Math.Max(1, Tuning.Lamps.Count);
            var p = train.Line.Sample(train.Dynamics.Path, along);
            var side = Double3.Cross(p.Tangent, Double3.Up).Normalized * (i % 2 == 0 ? 1 : -1);
            var spot = p.Position + side * dice.Range(Tuning.Lamps.Out[0], Tuning.Lamps.Out[^1]);
            double hint = along;
            spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) + 0.2 };
            world.Bodies.SpawnItem(spot, along, Sim.Physics.BodyKind.Lamp);
        }
    }

    /// <summary>Where a death stands you up again: the safe ring's middle.</summary>
    public Double3 SafeSpot { get; private set; }

    /// <summary>Along the stop from its start: its village's middle (its houses'), or the stop's own middle with none.</summary>
    static double Middle(RouteFeature stop)
    {
        var houses = stop.Stop?.Buildings.Where(b => b.Zone == StopZone.Village).ToList() ?? [];
        return houses.Count > 0 ? houses.Average(b => b.S) : stop.Length / 2;
    }

    static int VillageSide(StopLayout layout) => layout.VillageSide is var s && s != 0 ? Math.Sign(s) : 1;

    /// <summary>
    /// The gym's keys (the night's other keys go on as ever): F6 / Shift+F6 pick, F7 calls the picked kind at you, F10 clears.
    /// </summary>
    public void Keys(InputState input, HostSession host, byte you, double now)
    {
        if (input.Pressed(Key.F6))
        {
            bool back = input.Down(Key.LeftShift) || input.Down(Key.RightShift);
            _pick = (_pick + (back ? Roster.Count - 1 : 1)) % Roster.Count;
            Say($"{Name(Picked)}: F7 TO CALL", now);
        }
        if (input.Pressed(Key.F7))
            Call(host, you, Picked, now);
        if (input.Pressed(Key.F10))
            Clear(host, now);
    }

    /// <summary>Calls <paramref name="kind"/> at crewmate <paramref name="you"/>: what came, and where, said on the panel.</summary>
    public World.Called Call(HostSession host, byte you, EnemyKind kind, double now = 0)
    {
        var world = host.World;
        var me = host.Players.FirstOrDefault(p => p.Id == you);
        var at = me.Id == you ? PlayerMotor.WorldPosition(me.State, world.Train) : SafeSpot;
        var called = world.Call(kind, at, Tuning.MinDistance, Tuning.MaxDistance);
        Say(called.Came ? $"{Name(kind)} CALLED: {called.Where}" : $"{Name(kind)} CAN'T COME: {called.Where}", now);
        if (called.Came)
            Pointed = kind;
        Calls++;
        return called;
    }

    /// <summary>Kinds called once the night's begun (<c>--gym-call</c>), the last of them picked.</summary>
    public IReadOnlyList<EnemyKind> CallAtStart { get; set; } = [];

    /// <summary>How many calls the gym's had (a test's count).</summary>
    public int Calls { get; private set; }

    /// <summary>Everything about sent off, as if it had been driven off.</summary>
    public void Clear(HostSession host, double now = 0)
    {
        int n = host.World.Clear();
        Say(n == 0 ? "NOTHING ABOUT" : $"CLEARED {n}", now);
    }

    readonly Dictionary<byte, double> _deadSince = [];

    /// <summary>
    /// Each frame, after the host's step: whoever's been dead <see cref="GymTuning.RespawnSeconds"/> stands up again at the
    /// safe ring, whole, with the night's starting kit. The cold is kept off (the gym's night isn't about it).
    /// </summary>
    public void Step(HostSession host, double now)
    {
        // --gym-call (headless checks, or straight to one creature): called once the night's two seconds in.
        if (CallAtStart.Count > 0 && now >= 2 && host.Players.FirstOrDefault() is { Id: > 0 } first)
        {
            var kinds = CallAtStart;
            CallAtStart = [];
            foreach (var kind in kinds)
            {
                _pick = Math.Max(0, Roster.ToList().IndexOf(kind));
                Call(host, first.Id, kind, now);
            }
        }
        foreach (var (id, state) in host.Players.ToList())
        {
            if (state.Alive)
            {
                _deadSince.Remove(id);
                if (state.Cold > 0)
                    host.Stand(id, state with { Cold = 0 });
                continue;
            }
            if (state.Death == DeathCause.Waiting)
                continue;
            if (!_deadSince.TryGetValue(id, out double since))
                _deadSince[id] = since = now;
            if (now - since < Tuning.RespawnSeconds)
                continue;
            _deadSince.Remove(id);
            host.Stand(id, Respawn(host, state));
        }
    }

    PlayerState Respawn(HostSession host, PlayerState was)
    {
        var train = host.World.Train;
        double hint = train.Dynamics.Distance;
        train.Line.Nearest(SafeSpot, ref hint);
        var up = PlayerMotor.SpawnOnGround(SafeSpot, train.Line, hint, host.PlayerTuning);
        return up with
        {
            Yaw = was.Yaw,
            Placed = (byte)(was.Placed + 1),
            Kit = host.PlayerTuning.StartingKit,
        };
    }

    void Say(string what, double now)
    {
        Said = what;
        _saidAt = now;
    }

    public static string Name(EnemyKind kind) => Sim.Run.IncidentLog.Spoken(kind.ToString()).ToUpperInvariant();

    static readonly Vector4 Ink = new(0.92f, 0.88f, 0.78f, 0.95f), Dim = new(0.75f, 0.72f, 0.65f, 0.7f), Back = new(0, 0, 0, 0.55f);

    /// <summary>The kind the pointer's on: the last that came when called (null before any has).</summary>
    public EnemyKind? Pointed { get; private set; }

    static readonly Vector4 Mark = new(1f, 0.78f, 0.36f, 0.95f);

    /// <summary>
    /// The pointer (the director, 13 Oct 2026: "the gym needs a UI pointer that points to where the thing spawned is"): to
    /// the nearest of the kind last called, in <paramref name="world"/> as this machine draws it. In view, a mark on it with
    /// its name and how far; out of view, an arrow at the screen's edge the way to turn, with the same. Nothing once
    /// they're gone, and nothing for what has nowhere to be seen (the Tippy Toesie, hidden till it's behind someone).
    /// </summary>
    public void Pointer(Overlay overlay, int width, int height, in Camera camera, World world)
    {
        if (Pointed is not { } kind)
            return;
        var eye = camera.Position;
        var all = world.ActiveEnemies.Where(e => !e.Gone && e.Kind == kind)
            .Select(e => e.WorldPosition(world.Train) + Double3.Up * 1.2)
            .Where(at => (at - eye).Length <= Tuning.MaxDistance * 3).ToList();
        if (all.Count == 0)
            return;
        var at = all.MinBy(a => (a - eye).Length);
        string label = $"{Name(kind)}{(all.Count > 1 ? $" x{all.Count}" : "")} {(at - eye).Length:0} M";
        var p = Point(camera, at, width, height, Edge);
        if (p.OnScreen)
        {
            overlay.Outline(p.X - 5, p.Y - 5, 10, 10, Mark);
            overlay.Rect(p.X - 1, p.Y - 1, 2, 2, Mark);
            overlay.TextCentred(p.X, p.Y + 8, label, Mark);
            return;
        }
        // At the edge, the way to it: an arrowhead, its point at the edge and widening back in, and the label inside it.
        for (int i = 0; i < 5; i++)
        {
            float back = 2.5f * i, half = 0.8f + i * 1.1f;
            float cx = p.X - p.DirX * back, cy = p.Y - p.DirY * back;
            overlay.Rect(cx - half, cy - half, half * 2, half * 2, Mark);
        }
        if (p.DirX > 0.6f)
            overlay.TextRight(p.X - 16, p.Y - 4, label, Mark);
        else if (p.DirX < -0.6f)
            overlay.Text(p.X + 16, p.Y - 4, label, Mark);
        else
            overlay.TextCentred(Math.Clamp(p.X, 60, width - 60), p.DirY > 0 ? p.Y - 22 : p.Y + 14, label, Mark);
    }

    /// <summary>How far in from the screen's edge the pointer's arrow stands (UI pixels).</summary>
    const float Edge = 14;

    /// <summary>
    /// Where <paramref name="at"/> falls on a <paramref name="width"/> by <paramref name="height"/> screen through
    /// <paramref name="camera"/>: on it, or (behind the eye, or past an edge) the point on a frame <paramref name="inset"/> in
    /// from the edge in its direction, with that direction (screen space, +X right, +Y down).
    /// </summary>
    public static (bool OnScreen, float X, float Y, float DirX, float DirY) Point(in Camera camera, Double3 at, float width, float height, float inset)
    {
        var rel = at - camera.Position;
        var clip = Vector4.Transform(new Vector4((float)rel.X, (float)rel.Y, (float)rel.Z, 1), camera.ViewProjection(width / height));
        float cx = width / 2, cy = height / 2;
        if (clip.W > 1e-3f)
        {
            float x = (clip.X / clip.W * 0.5f + 0.5f) * width, y = (clip.Y / clip.W * 0.5f + 0.5f) * height;
            if (x >= inset && x <= width - inset && y >= inset && y <= height - inset)
                return (true, x, y, 0, 0);
        }
        // In front, the way it lies on the screen. Behind the eye, the side it's on (the way to turn to it), at the edge's
        // middle; straight behind, the bottom.
        float dx = clip.X, dy = clip.Y;
        if (clip.W <= 1e-3f)
            (dx, dy) = Math.Abs(clip.X) < 1e-3f * Math.Abs(clip.W) + 1e-4f ? (0f, 1f) : (Math.Sign(clip.X), 0f);
        float len = MathF.Sqrt(dx * dx + dy * dy);
        dx /= len;
        dy /= len;
        // Out from the middle along it to the inset frame.
        float sx = dx == 0 ? float.MaxValue : (cx - inset) / Math.Abs(dx), sy = dy == 0 ? float.MaxValue : (cy - inset) / Math.Abs(dy);
        float s = Math.Min(sx, sy);
        return (false, cx + dx * s, cy + dy * s, dx, dy);
    }

    /// <summary>The panel, top left: the picked kind, where it comes from and what answers it, the keys, and what was last said.</summary>
    public void Draw(Overlay overlay, int width, int height, double now)
    {
        var note = Tuning.Notes.GetValueOrDefault(Live.EnemyNames.Name(Picked)) ?? new GymTuning.KindNote();
        float x = 8, y = 26, w = Math.Min(560, width - 16);
        int lines = 3 + (note.From.Length > 0 ? 1 : 0) + (note.Answer.Length > 0 ? 1 : 0);
        overlay.Rect(x, y, w, 6 + lines * 12, Back);
        y += 3;
        overlay.Text(x + 6, y, $"CREATURE GYM · {_pick + 1}/{Roster.Count} · {Name(Picked)}", Ink);
        y += 12;
        if (note.From.Length > 0)
        {
            overlay.Text(x + 6, y, "FROM: " + note.From.ToUpperInvariant(), Dim);
            y += 12;
        }
        if (note.Answer.Length > 0)
        {
            overlay.Text(x + 6, y, "ANSWER: " + note.Answer.ToUpperInvariant(), Dim);
            y += 12;
        }
        overlay.Text(x + 6, y, "F6 NEXT · SHIFT+F6 BACK · F7 CALL · F10 CLEAR", Dim);
        y += 12;
        if (now - _saidAt < 8)
            overlay.Text(x + 6, y, Said, Ink);
    }

    /// <summary>The gym's light and air over the frame's (gym.json "look", "fogScale").</summary>
    public void Light(ref FrameLighting light)
    {
        var l = Tuning.Look;
        light.FogDensity *= (float)Tuning.FogScale;
        if (l.FogHeightFalloff is { } fh) light.FogHeightFalloff = fh;
        if (l.FogFloor is { } ff) light.FogFloor = ff;
        if (l.FogCurve is { } fc) light.FogCurve = fc;
        if (V(l.FogColour) is { } fo) light.FogColor = fo;
        if (V(l.MoonDirection) is { } md) light.MoonDirection = Vector3.Normalize(md);
        if (V(l.MoonColour) is { } mc) light.MoonColour = mc;
        if (l.MoonStrength is { } ms) light.MoonStrength = ms;
        if (l.Ambient is { } am) light.Ambient = am;
        if (V(l.LampColour) is { } lc) light.LampColour = lc;
        if (l.LampIntensity is { } li) light.LampIntensity = li;

        static Vector3? V(float[]? a) => a is [var r, var g, var b] ? new Vector3(r, g, b) : null;
    }
}
