using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ballast;
using Ballast.Audio;
using Ballast.Platform;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

// Feel prototype (roadmap M1). Controls:
//   mouse look · WASD move · Shift run · Space jump · E grab/let go of ladders
//   R/F throttle notch up/down · B brake (hold) · X reverser (stopped only)
//   E at the firebox: shovel (hold) · E at the valve: vent (hold) · E on a coupler plate: cut (hold)
//   Left mouse at a gun (engine cab roof, guard car roof): fire · E (press) near a crate, lamp or body: pick up / put down · Right mouse: throw it
//   1–9 respawn on that car's roof · Backspace respawn in the cab · Tab chase camera · Esc release mouse / quit
// Options: --route tier:seed [--no-enemies] | --line name, --cars n --internal WxH --throttle 0..1 --quit-after seconds --capture file.png --mute
// Multiplayer (UDP, direct IP / LAN): --host [port] hosts the same options for others to join; --join address[:port] joins one.
// Networked, the cab is the only place to drive from (GDD §12): R/F/B/X work when you're standing in it.
// Voice (networked): open mic with voice activity, or --push-to-talk and hold V. Hold T to talk on the radio. --no-mic to only listen.

string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

var content = DataFile.FindContentRoot(Environment.CurrentDirectory);
int cars = int.Parse(Arg("--cars", "6"));
IPlaySession session;
if (args.Contains("--join"))
{
    string target = Arg("--join", "127.0.0.1");
    var endpoint = IPEndPoint.TryParse(target, out var ep) ? ep : new IPEndPoint(Dns.GetHostAddresses(target.Split(':')[0]).First(a => a.AddressFamily == AddressFamily.InterNetwork), NetPlaySession.DefaultPort);
    if (endpoint.Port == 0)
        endpoint.Port = NetPlaySession.DefaultPort;
    Console.WriteLine($"joining {endpoint}…");
    session = NetPlaySession.Join(content, endpoint);
}
else if (args.Contains("--host"))
{
    int port = int.TryParse(Arg("--host", ""), out var p) ? p : NetPlaySession.DefaultPort;
    var setup = new SessionSetup(Route: Arg("--route", "") is { Length: > 0 } r ? r : null, Line: Arg("--line", "test-loop"), Cars: cars, Enemies: !args.Contains("--no-enemies"));
    var hosted = NetPlaySession.HostGame(content, setup, port);
    Console.WriteLine($"hosting on UDP port {hosted.Port}: others join with --join <this machine's address>:{hosted.Port}");
    session = hosted;
}
else if (Arg("--route", "") is { Length: > 0 } routeSpec)
{
    var (tier, seed) = Route.ParseSpec(routeSpec);
    var routeTuning = DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File));
    session = new PrototypeSession(content, RouteGenerator.Generate(routeTuning, tier, seed), cars, enemies: !args.Contains("--no-enemies"));
}
else
{
    session = new PrototypeSession(content, Arg("--line", "test-loop"), cars);
}
var proto = session as PrototypeSession;
var internalSize = Arg("--internal", "480x270").Split('x').Select(int.Parse).ToArray();
double quitAfter = double.Parse(Arg("--quit-after", "0"));
string? capture = Arg("--capture", "") is { Length: > 0 } c ? c : null;
if (proto is not null)
    proto.Controls.Throttle = double.Parse(Arg("--throttle", "0"));

using var window = new Window("Dark Territory — prototype", 1280, 720);
using var gpu = new GpuContext("Dark Territory", Window.VulkanInstanceExtensions(), window.CreateSurface);
using var renderer = new GreyboxRenderer(gpu, internalSize[0], internalSize[1]);
var (w, h) = window.PixelSize;
using var swapchain = new Swapchain(gpu, w, h);
Console.WriteLine($"GPU: {gpu.DeviceName}, window {w}x{h}, internal {renderer.Width}x{renderer.Height}");

var sound = new GameAudio(content);
using var speaker = args.Contains("--mute") ? null : AudioOut.Open(Audio.SampleRate, out var audioError) is { } s ? s : Warn(audioError);
var audioBlock = new float[Audio.Block * 2];
var net = session as NetPlaySession;
var voice = net is null ? null : new VoiceChat(sound.Mixer) { PushToTalk = args.Contains("--push-to-talk") };
using var mic = voice is null || args.Contains("--mute") || args.Contains("--no-mic") ? null
    : AudioIn.Open(Audio.SampleRate, out var micError) is { } m ? m : NoMic(micError);
var micSamples = new float[4800];
static AudioIn? NoMic(string? error)
{
    Console.WriteLine($"voice: no microphone ({error}); you can still hear the crew");
    return null;
}
static AudioOut? Warn(string? error)
{
    Console.WriteLine($"audio: no output device ({error}); running silent");
    return null;
}

var clock = new FixedStepClock(SimConstants.TickRate);
var scene = new GreyboxScene { Route = session.Route, Enemies = session.World.ActiveEnemies, Run = session.World.Run, Vehicles = session.Train.Vehicles, Bodies = session.World.Bodies.All };
var mesh = new MeshBuilder();
var timer = Stopwatch.StartNew();
double last = 0, titleAt = 0;
long frameCount = 0;
double pendingYaw = 0, pendingPitch = 0;
int pendingNotch = 0;
bool pendingReverser = false;
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
    // The prototype drives from anywhere; networked, cab controls go through intent like everything else.
    sbyte notch = (sbyte)((input.Pressed(Key.R) ? 1 : 0) - (input.Pressed(Key.F) ? 1 : 0));
    bool reverser = input.Pressed(Key.X);
    // Held until a tick sends them: at a high frame rate a key press can land on a frame with no tick.
    pendingNotch += notch;
    pendingReverser |= reverser;
    if (proto is not null)
    {
        if (notch != 0) proto.Notch(notch);
        if (reverser) proto.FlipReverser();
        if (input.Pressed(Key.Backspace)) proto.Respawn(0);
        for (var k = Key.D1; k <= Key.D9; k++)
            if (input.Pressed(k)) proto.Respawn(k - Key.D1 + 1);
        proto.Controls.Brake = input.Down(Key.B) ? 1 : 0;
    }
    if (input.Pressed(Key.Tab)) chase = !chase;

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
        if (input.Down(Key.MouseRight)) buttons |= PlayerButtons.Throw;
        if (proto is null)
        {
            if (input.Down(Key.B)) buttons |= PlayerButtons.Brake;
            if (pendingReverser) buttons |= PlayerButtons.Reverser;
        }
        var intent = new PlayerIntent
        {
            MoveX = (input.Down(Key.D) ? 1 : 0) - (input.Down(Key.A) ? 1 : 0),
            MoveZ = (input.Down(Key.W) ? 1 : 0) - (input.Down(Key.S) ? 1 : 0),
            LookYaw = (float)pendingYaw,
            LookPitch = (float)pendingPitch,
            Buttons = buttons,
            ThrottleNotch = proto is null ? (sbyte)Math.Clamp(pendingNotch, -4, 4) : (sbyte)0,
        };
        pendingNotch = 0;
        pendingReverser = false;
        pendingYaw = pendingPitch = 0;
        session.Step(intent);
        // The ears are where the eyes were last frame; audio follows the sim tick so no shot is missed.
        bool exposed = !PlayerMotor.Indoors(session.Player, session.Train);
        sound.Update(session.World, session.Controls, Listener.At(camera.Position, camera.Yaw), exposed, SimConstants.TickSeconds,
            PlayerMotor.Space(session.Player, session.Train));
        if (voice is not null && net is not null)
            voice.Update(net.Client, session.Crew(session.InterpolatedFrames(1), 1), SimConstants.TickSeconds);
    }
    if (voice is not null && net is not null)
    {
        voice.TalkHeld = input.Down(Key.V);
        voice.RadioHeld = input.Down(Key.T);
        for (int n; mic is not null && (n = mic.Read(micSamples)) > 0;)
            voice.Capture(micSamples.AsSpan(0, n), net.Client);
    }
    // Keep ~60 ms queued at the device.
    while (speaker is not null && speaker.QueuedSeconds < 0.06)
    {
        sound.Mixer.Render(audioBlock);
        speaker.Write(audioBlock);
    }

    var frames = session.InterpolatedFrames(clock.Alpha);
    camera = chase ? Views.Get("chase", session.Train) : session.EyeCamera(frames, clock.Alpha, pendingYaw, pendingPitch);
    scene.Crew = session.Crew(frames, clock.Alpha);
    scene.Time = now;
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
        string talking = voice is { Transmitting: true } ? voice.RadioHeld ? " | ON THE RADIO" : " | talking" : "";
        window.Title = $"Dark Territory — {session.Status()}{talking}";
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
(session as IDisposable)?.Dispose();
