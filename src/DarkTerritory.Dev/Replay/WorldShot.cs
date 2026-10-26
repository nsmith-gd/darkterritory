using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Dev.Replay;

/// <summary>A shot's camera; for a subject view (note 524), what it's on and what's wrong with the frame, if anything.</summary>
public sealed record ShotCamera(Camera Camera, string? Subject = null, string? Note = null);

/// <summary>
/// A host's world drawn from a camera (note 515: a replayed night looked at, at any tick), as the app draws a frame: the
/// scene dressed from the night's state the way <c>dt playthrough</c> does (its <c>Shoot</c>), the crew doing what they're
/// doing. Needs a GPU (lavapipe headless).
/// </summary>
public sealed class WorldShot : IDisposable
{
    readonly GpuContext _gpu;
    readonly GreyboxRenderer _renderer;
    readonly Look _look;
    readonly MeshBuilder _mesh = new();
    readonly Overlay _overlay = new();
    GreyboxScene _scene;

    /// <param name="limits">The GPU held to these (a smaller texture budget: the visual tell audit's, note 1043); null for the device's own.</param>
    public WorldShot(string content, Sim.Route.Route? route, int width = 1280, int height = 720, GpuLimits? limits = null)
    {
        Width = width;
        Height = height;
        _gpu = new GpuContext("dt replay", limits: limits);
        _renderer = new GreyboxRenderer(_gpu, width, height);
        _look = Look.Load(content);
        _look.Dress(_renderer);
        if (route is not null)
            _look.Sky = PlanSky.For(route);
        _scene = new GreyboxScene { Look = _look, Route = route };
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Each frame drawn by a scene that's seen no frame before it (the visual tell audit's, note 1043). A scene remembers what it
    /// drew: a creature missing from the next frame is seen going (the Track Doll vanishing, the Choir's ghosts swept off, a
    /// death), so the audit's frame without a creature, drawn after the frame with it, still had it in it.
    /// </summary>
    public bool Forgetful { get; init; }

    /// <summary>A lighting of its own over the night's (the creature gym's, note 676), applied last.</summary>
    public Func<FrameLighting, FrameLighting>? Grade { get; init; }

    /// <summary>
    /// A camera by name: <c>eye:&lt;id&gt;</c> through that crewmate's eyes (where they look); <c>subject:&lt;who&gt;</c> framed on
    /// someone or something in the world (note 524: an enemy kind, <c>crew:N</c>, <c>car:N</c>, <c>engine</c>; see
    /// <see cref="Live.SubjectCamera"/>), for a frame <paramref name="aspect"/> wide; or any of <see cref="Views"/>' (chase,
    /// roof, cab, trackside…), on car <paramref name="car"/>. A view that isn't one, or a subject that isn't there, is an
    /// <see cref="ArgumentException"/>.
    /// </summary>
    /// <param name="player">The crew's tuning (a crewmate's height, to frame them), when it's at hand.</param>
    public static Camera Camera(string view, World world, IReadOnlyList<PlayerSnapshot> crew, int car = 2, double aspect = 16.0 / 9, PlayerTuning? player = null) =>
        Aim(view, world, crew, car, aspect, player).Camera;

    /// <summary>
    /// <see cref="Camera"/>, and for a subject view what it framed (its name) and what's wrong with the frame if anything is
    /// (it's under the ground; nowhere round it had a clear view).
    /// </summary>
    public static ShotCamera Aim(string view, World world, IReadOnlyList<PlayerSnapshot> crew, int car = 2, double aspect = 16.0 / 9, PlayerTuning? player = null)
    {
        if (view.StartsWith(Live.SubjectCamera.Prefix, StringComparison.Ordinal))
        {
            var subject = Live.SubjectCamera.Find(view[Live.SubjectCamera.Prefix.Length..], world, crew, player);
            var framing = Live.SubjectCamera.Frame(subject, world, aspect);
            return new ShotCamera(framing.Camera, subject.Name, framing.Note);
        }
        if (view.StartsWith("eye:", StringComparison.Ordinal) && byte.TryParse(view[4..], out byte id) && crew.FirstOrDefault(c => c.Id == id) is { Id: > 0 } who)
            return new ShotCamera(Eyes.Operator(who.State, world) ?? Eyes.From(who.State, who.State, world.Train.Frames, 1, 0, 0, world.Movement?.Crouch.EyeDrop ?? 0));
        return new ShotCamera(Views.Get(view, world.Train, car));
    }

    /// <summary>The crewmate whose eyes a view is (<c>eye:N</c>), or null: their own body isn't drawn, as the app draws you.</summary>
    public static byte? EyeOf(string view) => view.StartsWith("eye:", StringComparison.Ordinal) && byte.TryParse(view[4..], out byte id) ? id : null;

    /// <summary>
    /// The world as the scene draws it from <paramref name="camera"/>: RGBA, <see cref="Width"/> by <see cref="Height"/>.
    /// <paramref name="hide"/>: the crewmate whose eyes these are, left out (the app never draws your own body in your view).
    /// <paramref name="drawn"/>: only the creatures it passes are drawn (the visual tell audit's frame without its tell, note
    /// 1037); <paramref name="grade"/>: a lighting over <see cref="Grade"/>'s for this frame alone (the audit's weather).
    /// </summary>
    public byte[] Render(World world, IReadOnlyList<PlayerSnapshot> crew, Camera camera, byte? hide = null, Func<Enemy, bool>? drawn = null,
        Func<FrameLighting, FrameLighting>? grade = null)
    {
        var train = world.Train;
        double seconds = world.Tick * SimConstants.TickSeconds;
        var lighting = Views.Lighting(train, _look);
        if (_scene.Route is { } route)
        {
            lighting.FogDensity = Views.FogDensity(route, train);
            lighting.Frost = _look.Tuning.Atmosphere.Cold.Frost(route.Weather.Cold);
        }
        lighting = _look.Chill(lighting, GreyboxScene.ChoirCold(world.Choir.Present ? 1 : (float)world.Choir.Build));
        if (Grade is { } own)
            lighting = own(lighting);
        if (grade is not null)
            lighting = grade(lighting);
        if (Forgetful)
            _scene = new GreyboxScene { Look = _look, Route = _scene.Route };
        var s = _scene;
        s.Signs = world.Lineside?.Signs;
        s.SignRange = world.Lineside?.Tuning.LampSignRange ?? 350;
        s.Enemies = drawn is null ? world.ActiveEnemies : [.. world.ActiveEnemies.Where(drawn)];
        s.Hits = world.Hits;
        s.Impacts = world.Impacts;
        s.Run = world.Run;
        s.Holdouts = world.Holdouts;
        s.Town = world.Town;
        s.Arrival = world.Arrival;
        s.Vehicles = train.Vehicles;
        s.Bodies = world.Bodies.All;
        s.Diverging = train.Diverging;
        s.Stands = world.Switches;
        s.Time = seconds;
        s.Tick = world.Tick;
        s.Controls = world.Controls;
        s.Wreck = train.Wreck;
        s.Derailed = world.Derailed;
        s.FireDoorOpen = train.Boiler.FireDoorOpen;
        s.Ruptured = train.Boiler.Ruptured;
        s.Patched = train.Boiler.Patched;
        var breaks = RepairCallouts.Of(train);
        Couplings.Callouts(train, breaks);
        s.Breaks = breaks;
        s.BendStrain = world.TrackPlan is { } bent ? BendStrain.PerCar(train, bent.Rules) : null;
        s.SinceShovel = train.Boiler.SinceShovel;
        s.Flare = train.BoilerTuning is { } flt ? (float)flt.FlareLook(train.Boiler) : 0;
        s.ChoirGathering = world.Choir.Present ? 1 : (float)world.Choir.Build;
        s.Answer = world.Answer;
        s.AnswerShowSeconds = world.Director?.Tuning.Draw.ShowSeconds ?? 7;
        s.Watcher = world.Watcher;
        s.WatcherShowSeconds = world.Director?.Tuning.Afoot.SignSeconds ?? 3.5;
        s.FireGlow = train.BoilerTuning is { } bt ? GreyboxScene.FireLook(train.Boiler.Firebox, bt.FireboxCapacity) : 0.7f;
        s.LampLit = world.LampShining;
        s.Cut = SceneArt.Cuts(train);
        var states = crew.Select(c => c.State).ToList();
        s.Crew = [.. crew.Where(c => c.Id != hide).Select(c => CrewActs.Crewmate(c.Id, c.State, world, train.Frames, states, [.. crew.Select(x => ((int)x.Id, x.State))]))];
        // The car in a Waker's hands up off the rails, as the sessions draw it (note 902).
        _frames.Clear();
        _frames.AddRange(train.Frames);
        WakerLift.Apply(_frames, train, world);
        s.Build(_mesh, train, camera.Position, _frames);
        _overlay.Clear();
        return _renderer.Render(_mesh, camera, lighting, lighting.FogColor, _overlay);
    }

    readonly List<CarFrame> _frames = [];

    public void Save(string path, byte[] pixels)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        PngWriter.Write(path, pixels, Width, Height, 1);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _gpu.Dispose();
    }
}
