using System.Diagnostics;
using Ballast;
using Ballast.Platform;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

// Feel prototype (roadmap M1). Controls:
//   mouse look · WASD move · Shift run · Space jump · E grab/let go of ladders
//   R/F throttle notch up/down · B brake (hold) · X reverser (stopped only)
//   E at the firebox: shovel (hold) · E at the valve: vent (hold) · E on a coupler plate: cut (hold)
//   Left mouse at a gun (engine cab roof, guard car roof): fire
//   1–9 respawn on that car's roof · Backspace respawn in the cab · Tab chase camera · Esc release mouse / quit
// Options: --route tier:seed | --line name, --cars n --internal WxH --throttle 0..1 --quit-after seconds --capture file.png

string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

var content = DataFile.FindContentRoot(Environment.CurrentDirectory);
int cars = int.Parse(Arg("--cars", "6"));
PrototypeSession session;
if (Arg("--route", "") is { Length: > 0 } routeSpec)
{
    var (tier, seed) = Route.ParseSpec(routeSpec);
    var routeTuning = DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File));
    session = new PrototypeSession(content, RouteGenerator.Generate(routeTuning, tier, seed), cars);
}
else
{
    session = new PrototypeSession(content, Arg("--line", "test-loop"), cars);
}
var internalSize = Arg("--internal", "480x270").Split('x').Select(int.Parse).ToArray();
double quitAfter = double.Parse(Arg("--quit-after", "0"));
string? capture = Arg("--capture", "") is { Length: > 0 } c ? c : null;
session.Controls.Throttle = double.Parse(Arg("--throttle", "0"));

using var window = new Window("Dark Territory — prototype", 1280, 720);
using var gpu = new GpuContext("Dark Territory", Window.VulkanInstanceExtensions(), window.CreateSurface);
using var renderer = new GreyboxRenderer(gpu, internalSize[0], internalSize[1]);
var (w, h) = window.PixelSize;
using var swapchain = new Swapchain(gpu, w, h);
Console.WriteLine($"GPU: {gpu.DeviceName}, window {w}x{h}, internal {renderer.Width}x{renderer.Height}");

var clock = new FixedStepClock(SimConstants.TickRate);
var scene = new GreyboxScene { Route = session.Route };
var mesh = new MeshBuilder();
var timer = Stopwatch.StartNew();
double last = 0, titleAt = 0;
long frameCount = 0;
double pendingYaw = 0, pendingPitch = 0;
bool chase = false;
const double Sensitivity = 0.0025;
var input = window.Input;
Camera camera = default;
FrameLighting lighting = default;

while (!window.CloseRequested)
{
    window.PumpEvents();
    double now = timer.Elapsed.TotalSeconds;
    double dt = now - last;
    last = now;

    if (input.Pressed(Key.Escape))
    {
        if (window.MouseCaptured) window.MouseCaptured = false;
        else break;
    }
    if (input.Pressed(Key.R)) session.Notch(+1);
    if (input.Pressed(Key.F)) session.Notch(-1);
    if (input.Pressed(Key.X)) session.FlipReverser();
    if (input.Pressed(Key.Tab)) chase = !chase;
    if (input.Pressed(Key.Backspace)) session.Respawn(0);
    for (var k = Key.D1; k <= Key.D9; k++)
        if (input.Pressed(k)) session.Respawn(k - Key.D1 + 1);
    session.Controls.Brake = input.Down(Key.B) ? 1 : 0;

    pendingYaw -= input.MouseDX * Sensitivity;
    pendingPitch -= input.MouseDY * Sensitivity;

    int ticks = clock.Advance(dt);
    for (int i = 0; i < ticks; i++)
    {
        var buttons = PlayerButtons.None;
        if (input.Down(Key.LeftShift)) buttons |= PlayerButtons.Run;
        if (input.Down(Key.Space)) buttons |= PlayerButtons.Jump;
        if (input.Down(Key.E)) buttons |= PlayerButtons.Use;
        if (input.Down(Key.MouseLeft)) buttons |= PlayerButtons.Fire;
        var intent = new PlayerIntent
        {
            MoveX = (input.Down(Key.D) ? 1 : 0) - (input.Down(Key.A) ? 1 : 0),
            MoveZ = (input.Down(Key.W) ? 1 : 0) - (input.Down(Key.S) ? 1 : 0),
            LookYaw = (float)pendingYaw,
            LookPitch = (float)pendingPitch,
            Buttons = buttons,
        };
        pendingYaw = pendingPitch = 0;
        session.Step(intent);
    }

    var frames = session.InterpolatedFrames(clock.Alpha);
    camera = chase ? Views.Get("chase", session.Train) : session.EyeCamera(frames, clock.Alpha, pendingYaw, pendingPitch);
    lighting = Views.Lighting(frames[0]);
    if (session.Route is { } r)
        lighting.FogDensity = (float)r.Weather.FogDensity;
    scene.FireGlow = (float)(session.Train.BoilerTuning is { } bt ? session.Train.Boiler.FireFraction(bt) : 0.7);
    scene.Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
    renderer.Prepare(mesh);

    if (window.Resized)
    {
        (w, h) = window.PixelSize;
        swapchain.Recreate(w, h);
        window.Resized = false;
    }
    var cam = camera;
    var light = lighting;
    if (!swapchain.Present(renderer, cmd => renderer.Record(cmd, cam, light, light.FogColor)))
    {
        (w, h) = window.PixelSize;
        swapchain.Recreate(w, h);
    }

    if (now >= titleAt)
    {
        window.Title = $"Dark Territory — {session.Status()}";
        titleAt = now + 0.25;
    }
    input.EndFrame();
    frameCount++;

    if (quitAfter > 0 && now >= quitAfter)
        break;
}

if (capture is not null)
{
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor);
    PngWriter.Write(capture, pixels, renderer.Width, renderer.Height, scale: 2);
    Console.WriteLine($"captured {Path.GetFullPath(capture)}");
}
Console.WriteLine($"frames {frameCount} ({frameCount / timer.Elapsed.TotalSeconds:0} fps), ticks {session.Tick}, {session.Status()}");
