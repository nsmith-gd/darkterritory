using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Dev.Replay;

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
    readonly GreyboxScene _scene;

    public WorldShot(string content, Sim.Route.Route? route, int width = 1280, int height = 720)
    {
        Width = width;
        Height = height;
        _gpu = new GpuContext("dt replay");
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
    /// A camera by name: <c>eye:&lt;id&gt;</c> through that crewmate's eyes (where they look), or any of <see cref="Views"/>'
    /// (chase, roof, cab, trackside…), on car <paramref name="car"/>.
    /// </summary>
    public static Camera Camera(string view, World world, IReadOnlyList<PlayerSnapshot> crew, int car = 2)
    {
        if (view.StartsWith("eye:", StringComparison.Ordinal) && byte.TryParse(view[4..], out byte id) && crew.FirstOrDefault(c => c.Id == id) is { Id: > 0 } who)
            return Eyes.Operator(who.State, world) ?? Eyes.From(who.State, who.State, world.Train.Frames, 1, 0, 0);
        return Views.Get(view, world.Train, car);
    }

    /// <summary>The crewmate whose eyes a view is (<c>eye:N</c>), or null: their own body isn't drawn, as the app draws you.</summary>
    public static byte? EyeOf(string view) => view.StartsWith("eye:", StringComparison.Ordinal) && byte.TryParse(view[4..], out byte id) ? id : null;

    /// <summary>
    /// The world as the scene draws it from <paramref name="camera"/>: RGBA, <see cref="Width"/> by <see cref="Height"/>.
    /// <paramref name="hide"/>: the crewmate whose eyes these are, left out (the app never draws your own body in your view).
    /// </summary>
    public byte[] Render(World world, IReadOnlyList<PlayerSnapshot> crew, Camera camera, byte? hide = null)
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
        var s = _scene;
        s.Signs = world.Lineside?.Signs;
        s.SignRange = world.Lineside?.Tuning.LampSignRange ?? 350;
        s.Enemies = world.ActiveEnemies;
        s.Hits = world.Hits;
        s.Impacts = world.Impacts;
        s.Run = world.Run;
        s.Holdouts = world.Holdouts;
        s.Town = world.Town;
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
        var breaks = RepairCallouts.Of(train);
        Couplings.Callouts(train, breaks);
        s.Breaks = breaks;
        s.BendStrain = world.TrackPlan is { } bent ? BendStrain.PerCar(train, bent.Rules) : null;
        s.SinceShovel = train.Boiler.SinceShovel;
        s.ChoirGathering = world.Choir.Present ? 1 : (float)world.Choir.Build;
        s.Answer = world.Answer;
        s.AnswerShowSeconds = world.Director?.Tuning.Draw.ShowSeconds ?? 7;
        s.Watcher = world.Watcher;
        s.WatcherShowSeconds = world.Director?.Tuning.Afoot.SignSeconds ?? 3.5;
        s.FireGlow = train.BoilerTuning is { } bt ? GreyboxScene.FireLook(train.Boiler.Firebox, bt.FireboxCapacity) : 0.7f;
        s.LampLit = world.LampShining;
        s.Cut = SceneArt.Cuts(train);
        var states = crew.Select(c => c.State).ToList();
        s.Crew = [.. crew.Where(c => c.Id != hide).Select(c => CrewActs.Crewmate(c.Id, c.State, world, train.Frames, states))];
        s.Build(_mesh, train, camera.Position);
        _overlay.Clear();
        return _renderer.Render(_mesh, camera, lighting, lighting.FogColor, _overlay);
    }

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
