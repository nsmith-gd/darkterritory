using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ballast;
using Ballast.Audio;
using Ballast.Online;
using Ballast.Online.Steam;
using Ballast.Platform;
using Ballast.Render;
using Ballast.Xr;
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
// F1 toggles the HUD (--no-hud to start without it).
// Options: --route tier:seed | --route-file name (saved from dt edit) [--no-enemies] | --line name, --cars n --internal WxH --throttle 0..1 --quit-after seconds --capture file.png --mute
// Multiplayer (UDP, direct IP / LAN): --host [port] hosts the same options for others to join; --join address[:port] joins one.
// Steam: --steam hosts a friends-only lobby as well (F2 opens the invite dialog; friends can also "Join Game" from the
//   friends list). Accepting an invite starts the game with +connect_lobby <id>, or --join-lobby <id> by hand.
//   Needs steam_api64.dll next to the game (external/steam/README.md); --no-steam to not even try.
// Networked, the cab is the only place to drive from (GDD §12): R/F/B/X work when you're standing in it.
// VR: --vr plays in an OpenXR headset (Quest via Link, SteamVR, Monado) and mirrors to the window; --vr-scale 0.5 of the
//   runtime's per-eye size. Mouse yaw still turns the body; the head does the rest.
// Voice (networked): open mic with voice activity, or --push-to-talk and hold V. Hold T to talk on the radio. --no-mic to only listen.

string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

var content = DataFile.FindContentRoot(Environment.CurrentDirectory);
int cars = int.Parse(Arg("--cars", "6"));
var connectLobby = LaunchArgs.ConnectLobby(args);
// Steam when asked for, when an invite brought us here, or when Steam launched us (so invites reach a solo game).
using var steam = args.Contains("--no-steam") || !(args.Contains("--steam") || connectLobby is not null || Environment.GetEnvironmentVariable("SteamAppId") is not null)
    ? null
    : SteamBackend.TryStart(uint.TryParse(Arg("--steam-appid", ""), out var appId) ? appId : SteamBackend.DevAppId, out var steamError) is { } started
        ? started
        : NoSteam(steamError);
static SteamBackend? NoSteam(string? error)
{
    Console.WriteLine($"steam: {error}; LAN and direct IP still work");
    return null;
}
IPlaySession session;
if (connectLobby is { } lobbyId)
{
    if (steam is null)
        return 1;
    Console.WriteLine($"joining lobby {lobbyId} on Steam…");
    session = NetPlaySession.JoinLobby(content, steam, lobbyId);
}
else if (args.Contains("--join"))
{
    string target = Arg("--join", "127.0.0.1");
    var endpoint = IPEndPoint.TryParse(target, out var ep) ? ep : new IPEndPoint(Dns.GetHostAddresses(target.Split(':')[0]).First(a => a.AddressFamily == AddressFamily.InterNetwork), NetPlaySession.DefaultPort);
    if (endpoint.Port == 0)
        endpoint.Port = NetPlaySession.DefaultPort;
    Console.WriteLine($"joining {endpoint}…");
    session = NetPlaySession.Join(content, endpoint);
}
else if (args.Contains("--host") || (args.Contains("--steam") && steam is not null))
{
    int? port = !args.Contains("--host") ? null : int.TryParse(Arg("--host", ""), out var p) ? p : NetPlaySession.DefaultPort;
    var setup = new SessionSetup(Route: Arg("--route", "") is { Length: > 0 } r ? r : null, Line: Arg("--line", "test-loop"), Cars: cars, Enemies: !args.Contains("--no-enemies"));
    var hosted = NetPlaySession.HostGame(content, setup, port, online: steam);
    if (port is not null)
        Console.WriteLine($"hosting on UDP port {hosted.Port}: others join with --join <this machine's address>:{hosted.Port}");
    if (steam is not null)
        Console.WriteLine($"hosting a friends-only Steam lobby as {steam.NameOf(steam.Me)}: F2 to invite");
    session = hosted;
}
else if (Arg("--route-file", "") is { Length: > 0 } routeFile)
{
    // A route saved from the editor (dt edit): content/lines/<name>.route.json.
    var saved = DataFile.Load<Route>(Path.Combine(content, "lines", routeFile + ".route.json"));
    session = new PrototypeSession(content, saved, cars, enemies: !args.Contains("--no-enemies"));
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
// --vr: the headset makes the GPU (it has to pick the device and the extensions), and the window mirrors the flat view.
using var vr = args.Contains("--vr") ? StartVr() : null;
VrView? StartVr()
{
    try
    {
        var view = VrView.Start("Dark Territory", double.Parse(Arg("--vr-scale", "0.5")), Window.VulkanInstanceExtensions(), window.CreateSurface);
        Console.WriteLine($"vr: {view.Headset.System} on {view.Headset.Runtime}, {view.Session.EyeWidth}x{view.Session.EyeHeight} per eye");
        return view;
    }
    catch (XrUnavailableException e)
    {
        Console.WriteLine($"vr: {e.Message}; playing flat");
        return null;
    }
}
using var ownGpu = vr is null ? new GpuContext("Dark Territory", Window.VulkanInstanceExtensions(), window.CreateSurface) : null;
var gpu = vr?.Gpu ?? ownGpu!;
using var renderer = new GreyboxRenderer(gpu, internalSize[0], internalSize[1]);
var (w, h) = window.PixelSize;
// In VR the headset sets the pace; the mirror shouldn't wait for the monitor as well.
using var swapchain = new Swapchain(gpu, w, h, vsync: vr is null);
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
var hud = new Overlay();
bool showHud = !args.Contains("--no-hud");
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

LobbyId? relaunch = null;
var steamEvents = new List<OnlineEvent>();
LobbyId? Invited()
{
    if (net?.Lobby is not null)
        return net.TakeJoinRequest();
    if (steam is null)
        return null;
    steamEvents.Clear();
    steam.Poll(steamEvents);
    return steamEvents.Where(e => e.Kind == OnlineEventKind.JoinRequested).Select(e => (LobbyId?)e.Lobby).LastOrDefault();
}

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
    if (input.Pressed(Key.F1)) showHud = !showHud;
    if (input.Pressed(Key.F2)) net?.ShowInviteDialog();
    // An invite accepted (or "Join Game" on a friend) while playing: leave this game for theirs.
    if (Invited() is { } invitedTo)
    {
        relaunch = invitedTo;
        break;
    }

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
    // A Vigil: emergency lighting, and no power to the headlamp.
    scene.Emergency = session.World.EmergencyLights;
    if (!session.World.LampShining)
        lighting.LampRange = 0.01f; // not 0: the shader divides by it
    scene.Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
    if (showHud)
        Hud.Build(hud, renderer.Width, renderer.Height, session);
    renderer.Prepare(mesh, showHud ? hud : null);

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
    // The body is the flat camera: its eye point and yaw. The head does the looking.
    if (vr is not null && vr.Frame(mesh, cam, light, light.FogColor) == XrFrameResult.Exiting)
        break;

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
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, showHud ? hud : null);
    PngWriter.Write(capture, pixels, renderer.Width, renderer.Height, scale: 2);
    Console.WriteLine($"captured {Path.GetFullPath(capture)}");
}
Console.WriteLine($"frames {frameCount} ({frameCount / timer.Elapsed.TotalSeconds:0} fps), ticks {session.Tick}, {session.Status()}");
(session as IDisposable)?.Dispose();
if (relaunch is { } next)
{
    // The simplest way into another game is a fresh start, the same one Steam gives an invite accepted from outside.
    steam?.Dispose();
    Console.WriteLine($"leaving for lobby {next}");
    Process.Start(Environment.ProcessPath!, ["+connect_lobby", next.ToString()]);
}
return 0;
