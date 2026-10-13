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
        // The train's middle at the stop's middle, its front no further on than the stop's end.
        Front = Stop is { } s ? Math.Min(s.End, (s.Start + s.End) / 2 + length / 2) : route.Length * 0.25;
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
        Calls++;
        return called;
    }

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
