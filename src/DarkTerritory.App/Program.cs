using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
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
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

// Dark Territory. Plain `DarkTerritory` opens the front end (T30): the campaign's three slots and the fortress between
// nights, a quick night on any tier, joining by address, and the settings (saved in the user's app data). The flags
// below start a night straight away instead, and quit when it's over.
// Controls:
//   mouse look · WASD move · Shift run · Space jump · E grab/let go of ladders
//   R/F throttle notch up/down · B brake (hold) · X reverser (stopped only)
//   E at the firebox: shovel (hold) · E at the valve: vent (hold) · E on a coupler plate: cut (hold) · E at a switch stand: throw it (hold)
//   Left mouse at a gun (engine cab roof, guard car roof): fire · E (press) near a crate, lamp or body: pick up / put down · Right mouse: throw it
//   1–9 respawn on that car's roof · Backspace respawn in the cab · Tab chase camera · Q (hold) the crew roster
//   Esc frees the mouse; Esc again leaves the night (to the menu, or quits one started from the command line). When the
//   night's over, Enter goes back.
// F1 toggles the HUD (--no-hud to start without it).
// Campaign: --campaign <slot> [--contract i] [--resume] [--saves dir] plays tonight's contract with the slot's cars and
//   upgrades, autosaves leaving each facility, and settles at the end (spec E, F). `dt campaign` runs the fortress headless.
// Options: --route tier:seed | --route-file name (saved from dt edit) [--no-enemies] | --line name, --cars n --internal WxH --throttle 0..1 --quit-after seconds --capture file.png --mute --greybox (flat colour, no art pass)
// Multiplayer (UDP, direct IP / LAN): --host [port] hosts the same options for others to join; --join address[:port] joins one.
// Steam: --steam hosts a friends-only lobby as well (F2 opens the invite dialog; friends can also "Join Game" from the
//   friends list). Accepting an invite starts the game with +connect_lobby <id>, or --join-lobby <id> by hand.
//   Needs steam_api64.dll next to the game (external/steam/README.md); --no-steam to not even try.
// Networked, the cab is the only place to drive from (GDD §12): R/F/B/X work when you're standing in it.
// VR: --vr plays in an OpenXR headset (Quest via Link, SteamVR, Monado) and mirrors to the window; --vr-scale 0.5 of the
//   runtime's per-eye size. You look with your head and walk where you look: left stick walks (click it to run), right
//   stick turns (snap by default, content/tuning/vr.json and the settings), grip uses/grabs, trigger fires, A jumps, B
//   throws. The keyboard and mouse still work alongside (mouse yaw turns the body). Grip a cab lever to work it; the
//   HUD and the menus float on a panel ahead (left stick moves through the menus, trigger or A chooses, B goes back).
// Voice (networked): open mic with voice activity, or push to talk (the settings, or --push-to-talk) and hold V. Hold T
//   to talk on the radio. --no-mic to only listen.

// A crash leaves a report (the exception, and the last things the game said) in the user's app data.
CrashReports.Install();

string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

// Mods (T49) laid over the base content, unless --no-mods plays the base game.
// A mod manager's profile (Thunderstore, T78) comes in as --mods-dir.
args = Mods.TakeArgs(args);
var content = Mods.Mount(DataFile.FindContentRoot(Environment.CurrentDirectory), enabled: !args.Contains("--no-mods"));
// The art pass's surfaces (T39); --greybox draws flat colour instead.
var look = args.Contains("--greybox") ? null : Look.Load(content);
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

var campaignTuning = DataFile.Load<CampaignTuning>(Path.Combine(content, CampaignTuning.File));
var runTuning = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
var saves = new SaveSlots(Arg("--saves", SaveSlots.DefaultDirectory), campaignTuning.SaveSlots);
var frontEnd = new FrontEnd(campaignTuning, runTuning, saves, Arg("--settings", Settings.DefaultPath), edition: EditionTuning.Load(content));

// A night named on the command line starts straight away; otherwise it's the front end's choice.
Launch? LaunchFromArgs()
{
    int cars = int.Parse(Arg("--cars", "6"));
    int? port = !args.Contains("--host") ? null : int.TryParse(Arg("--host", ""), out var p) ? p : NetPlaySession.DefaultPort;
    if (connectLobby is { } lobby)
        return new Launch.JoinLobby(lobby);
    if (args.Contains("--join"))
        return new Launch.Join(Arg("--join", "127.0.0.1"));
    var edition = EditionTuning.Load(content);
    if (Arg("--campaign", "") is { Length: > 0 } slot && edition.Campaign)
        return new Launch.CampaignNight(int.Parse(slot), int.Parse(Arg("--contract", "0")), args.Contains("--resume"), port is not null);
    string? route = Arg("--route", "") is { Length: > 0 } r ? r : null;
    // The demo's nights are on its own tiers (T79), whatever's asked for.
    if (route is not null && edition.Tiers.Length > 0 && !edition.HasTier(DarkTerritory.Sim.Route.Route.ParseSpec(route).Tier))
    {
        Console.WriteLine($"edition {edition.Name}: no {route.Split(':')[0]} nights in it; {edition.Tiers[0]} instead");
        route = edition.Tiers[0] + (route.Contains(':') ? route[route.IndexOf(':')..] : "");
    }
    string? routeFile = Arg("--route-file", "") is { Length: > 0 } f ? f : null;
    bool host = port is not null || args.Contains("--steam") && steam is not null;
    if (host || route is not null || routeFile is not null || args.Contains("--line"))
        return new Launch.Night(route, cars, host) { Line = Arg("--line", "test-loop"), RouteFile = routeFile };
    return null;
}
var launch = LaunchFromArgs();
bool fromCommandLine = launch is not null;

// The frame renders at the window's 720p (the 2008-2012 target, ARCHITECTURE §8 note 57); the HUD and menus keep their
// 480x270 canvas (their pixel font's), scaled up over it.
var internalSize = Arg("--internal", "1280x720").Split('x').Select(int.Parse).ToArray();
const int UiWidth = 480, UiHeight = 270;
double quitAfter = double.Parse(Arg("--quit-after", "0"));
string? capture = Arg("--capture", "") is { Length: > 0 } c ? c : null;

using var window = new Window("Dark Territory", 1280, 720);
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
using var renderer = new GreyboxRenderer(gpu, internalSize[0], internalSize[1]) { OverlaySize = new Vector2(UiWidth, UiHeight) };
if (look is not null)
{
    look.Dress(renderer);
    vr?.Dress(look);
}
var (w, h) = window.PixelSize;
// In VR the headset sets the pace; the mirror shouldn't wait for the monitor as well.
using var swapchain = new Swapchain(gpu, w, h, vsync: vr is null);
Console.WriteLine($"GPU: {gpu.DeviceName}, window {w}x{h}, internal {renderer.Width}x{renderer.Height}");

var sound = new GameAudio(content);
using var speaker = args.Contains("--mute") ? null : AudioOut.Open(Audio.SampleRate, out var audioError) is { } s ? s : Warn(audioError);
var audioBlock = new float[Audio.Block * 2];
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
// Keep ~60 ms queued at the device; muted in the settings, it gets silence.
void FeedSpeaker()
{
    while (speaker is not null && speaker.QueuedSeconds < 0.06)
    {
        sound.Mixer.Render(audioBlock);
        if (frontEnd.Settings.Mute)
            Array.Clear(audioBlock);
        speaker.Write(audioBlock);
    }
}

var input = window.Input;
var timer = Stopwatch.StartNew();
var mesh = new MeshBuilder();
var overlay = new Overlay();
long frameCount = 0;
var steamEvents = new List<OnlineEvent>();
LobbyId? relaunch = null;

void Present(in Camera camera, in FrameLighting lighting)
{
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
}

// In a headset the window shows the left eye (its middle), not the flat view drawn again (tuning/perf.json).
void Mirror()
{
    if (window.Resized)
    {
        (w, h) = window.PixelSize;
        swapchain.Recreate(w, h);
        window.Resized = false;
    }
    if (!vr!.Mirror(swapchain))
    {
        (w, h) = window.PixelSize;
        swapchain.Recreate(w, h);
    }
}

// An invite accepted (or "Join Game" on a friend) while in the game or the menus.
LobbyId? Invited(NetPlaySession? net)
{
    if (net?.Lobby is not null)
        return net.TakeJoinRequest();
    if (steam is null)
        return null;
    steamEvents.Clear();
    steam.Poll(steamEvents);
    return steamEvents.Where(e => e.Kind == OnlineEventKind.JoinRequested).Select(e => (LobbyId?)e.Lobby).LastOrDefault();
}

bool QuitNow() => quitAfter > 0 && timer.Elapsed.TotalSeconds >= quitAfter;

// The front end over a night scene: the fortress yard with a train standing in it, the camera slowly looking about.
Launch? Menu()
{
    window.MouseCaptured = false;
    var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
    var line = DarkTerritory.Sim.Rail.RailLine.Load(Path.Combine(content, "lines", "test-loop.json"));
    var standing = new TrainOnLine(new TrainDynamics(Consist.Uniform(trainTuning, 6, 1)), line, 1200);
    var view = Views.Get("trackside", standing);
    var backdrop = new GreyboxScene { Time = 0.37, Look = look };
    backdrop.Build(mesh, standing, view.Position);
    var light = Views.Lighting(standing, look);
    double started = timer.Elapsed.TotalSeconds;
    // In a headset the menus float ahead, over the yard (T36), and the controllers work them.
    var vrMenu = vr is null ? null : new VrPanel(DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File)).Menu);
    var vrKeys = new VrMenuInput();
    frontEnd.Headset = vr is not null;
    // Whatever's held coming in (the A that ended the night) isn't a press here.
    if (vr is not null)
        vrKeys.Read(vr.Session.Controllers);
    while (!window.CloseRequested && !QuitNow())
    {
        window.PumpEvents();
        window.TextInput = frontEnd.WantsText;
        if (Invited(null) is { } lobby)
            return new Launch.JoinLobby(lobby);
        Launch? chosen = null;
        // Binding a control (T80): the next key or button pressed is the one, Escape keeps the old. The mouse is held
        // meanwhile, so a click is a button pressed and not the window taking the mouse.
        if (frontEnd.Capturing is not null)
        {
            window.MouseCaptured = true;
            if (input.Pressed(Key.Escape)) frontEnd.Back();
            else if (input.AnyPressed is { } bound) frontEnd.Bind(bound.ToString());
            if (frontEnd.Capturing is null)
                window.MouseCaptured = false;
        }
        else
        {
            if (input.Pressed(Key.Up) || input.Pressed(Key.W) && !frontEnd.WantsText) frontEnd.Up();
            if (input.Pressed(Key.Down) || input.Pressed(Key.S) && !frontEnd.WantsText) frontEnd.Down();
            if (input.Pressed(Key.Left) || input.Pressed(Key.A) && !frontEnd.WantsText) frontEnd.Left();
            if (input.Pressed(Key.Right) || input.Pressed(Key.D) && !frontEnd.WantsText) frontEnd.Right();
            if (input.Pressed(Key.Enter) || input.Pressed(Key.Space) && !frontEnd.WantsText) chosen = frontEnd.Select();
            if (input.Pressed(Key.Escape)) frontEnd.Back();
            if (input.Pressed(Key.Backspace)) frontEnd.Erase();
            if (input.Text.Length > 0) frontEnd.Type(input.Text);
        }
        if (vr is not null)
            chosen ??= VrMenuInput.Apply(vrKeys.Read(vr.Session.Controllers), frontEnd);
        if (chosen is not null)
        {
            // The key that chose it isn't also the night's first press.
            input.EndFrame();
            window.TextInput = false;
            return chosen;
        }
        var camera = view;
        camera.Yaw += Math.Sin((timer.Elapsed.TotalSeconds - started) * 0.07) * 0.25;
        frontEnd.Draw(overlay, UiWidth, UiHeight);
        if (vr is null)
        {
            renderer.Prepare(mesh, overlay);
            Present(camera, light);
        }
        else if (vr.Frame(mesh, view, light, light.FogColor, panel: new VrPanelContent(vrMenu!, overlay, UiWidth, UiHeight)) == XrFrameResult.Exiting)
            return new Launch.Quit();
        else
            Mirror();
        FeedSpeaker();
        input.EndFrame();
        frameCount++;
    }
    return null;
}

// Starts what was chosen. A campaign night begins (or resumes) its slot's contract and saves that first.
(IPlaySession Session, CampaignState? Campaign) Start(Launch chosen)
{
    bool enemies = !args.Contains("--no-enemies");
    switch (chosen)
    {
        case Launch.JoinLobby lobby when steam is not null:
            Console.WriteLine($"joining lobby {lobby.Lobby} on Steam…");
            return (NetPlaySession.JoinLobby(content, steam, lobby.Lobby), null);
        case Launch.Join join:
            {
                string target = join.Address;
                var endpoint = IPEndPoint.TryParse(target, out var ep) ? ep
                    : new IPEndPoint(Dns.GetHostAddresses(target.Split(':')[0]).First(a => a.AddressFamily == AddressFamily.InterNetwork),
                        target.Contains(':') && int.TryParse(target.Split(':')[1], out var jp) ? jp : NetPlaySession.DefaultPort);
                if (endpoint.Port == 0)
                    endpoint.Port = NetPlaySession.DefaultPort;
                Console.WriteLine($"joining {endpoint}…");
                return (NetPlaySession.Join(content, endpoint), null);
            }
        case Launch.CampaignNight night:
            {
                // Spec E: the host owns the campaign, and picks a slot. The night is the contract picked from the board, with
                // the slot's cars and upgrades; hosting takes friends as usual.
                var campaign = saves.Load(night.Slot) ?? Campaign.New(campaignTuning, night.Slot, $"Crew {night.Slot}", (ulong)Random.Shared.Next(1, 100000));
                // Resuming: the night that was under way, from the last facility it left (spec E "crash: rolls back to last POI autosave").
                var resume = night.Resume && campaign.Current is not null ? campaign.Checkpoint : null;
                var contract = night.Resume && campaign.Current is not null ? campaign.Current : Campaign.Offers(campaignTuning, runTuning, campaign)[Math.Max(0, night.Contract)];
                campaign = Campaign.Begin(campaign, contract) with { Checkpoint = resume };
                saves.Save(campaign);
                int? port = night.Host ? NetPlaySession.DefaultPort : null;
                var setup = new SessionSetup(Route: contract.Route, Cars: campaign.Cars, Enemies: enemies) { Upgrades = campaign.Upgrades };
                Console.WriteLine($"campaign slot {night.Slot} ({campaign.Name}): {campaign.Cars} cars, {campaign.Scrip:0} scrip, tonight {contract.Route} at {contract.PerCar:0} a car{(resume is not null ? $", resuming after facility {resume.Facility}" : "")}");
                return (NetPlaySession.HostGame(content, setup, port, online: night.Host ? steam : null, resume: resume), campaign);
            }
        case Launch.Night { Host: true } hosted:
            {
                // From the menu, friends join on the usual port; `--steam` alone takes no UDP port (the lobby's enough).
                int? port = args.Contains("--host") ? int.TryParse(Arg("--host", ""), out var p) ? p : NetPlaySession.DefaultPort
                    : fromCommandLine ? null : NetPlaySession.DefaultPort;
                var setup = new SessionSetup(Route: hosted.Route, Line: hosted.Line, Cars: hosted.Cars, Enemies: enemies);
                var session = NetPlaySession.HostGame(content, setup, port, online: steam);
                if (port is not null)
                    Console.WriteLine($"hosting on UDP port {session.Port}: others join with --join <this machine's address>:{session.Port}");
                if (steam is not null)
                    Console.WriteLine($"hosting a friends-only Steam lobby as {steam.NameOf(steam.Me)}: F2 to invite");
                return (session, null);
            }
        case Launch.Night { RouteFile: { } file } alone:
            {
                // A route saved from the editor (dt edit): content/lines/<name>.route.json.
                var saved = DataFile.Load<Route>(Path.Combine(content, "lines", file + ".route.json"));
                return (new PrototypeSession(content, saved, alone.Cars, enemies), null);
            }
        case Launch.Night { Route: { } spec } alone:
            {
                return (new PrototypeSession(content, Routes.Generate(content, spec, alone.Cars), alone.Cars, enemies), null);
            }
        case Launch.Night alone:
            return (new PrototypeSession(content, alone.Line, alone.Cars), null);
        default:
            throw new InvalidOperationException($"can't start {chosen}");
    }
}

while (!window.CloseRequested && !QuitNow())
{
    launch ??= Menu();
    if (launch is null or Launch.Quit || launch is Launch.JoinLobby && steam is null)
        break;
    var (session, campaign) = Start(launch);
    var leaving = launch;
    launch = null;
    campaign = Play(session, campaign);
    (session as IDisposable)?.Dispose();
    if (campaign is { Current: not null } unfinished)
        Console.WriteLine($"campaign: the night on {unfinished.Current.Route} isn't settled; its slot carries on from the last facility it left");
    // Started from the command line: done when the night is. Otherwise, back to where it was chosen.
    if (fromCommandLine || relaunch is not null)
        break;
    if (leaving is Launch.CampaignNight night)
        frontEnd.ShowFortress(night.Slot, campaign?.History.LastOrDefault() is { } log && campaign.Current is null
            ? $"{log.End}: {(log.Net >= 0 ? "+" : "")}{log.Net:0} scrip" : null);
    else
        frontEnd.NightOver();
}

Console.WriteLine($"frames {frameCount} ({frameCount / timer.Elapsed.TotalSeconds:0} fps)");
if (relaunch is { } next)
{
    // The simplest way into another game is a fresh start, the same one Steam gives an invite accepted from outside.
    steam?.Dispose();
    Console.WriteLine($"leaving for lobby {next}");
    Process.Start(Environment.ProcessPath!, ["+connect_lobby", next.ToString()]);
}
return 0;

// One night, until it's left, the window closes, or an invite takes us elsewhere. Returns the campaign as it stands.
CampaignState? Play(IPlaySession session, CampaignState? campaign)
{
    var settings = frontEnd.Settings;
    var proto = session as PrototypeSession;
    var net = session as NetPlaySession;
    // A generated night has its own far horizon (Art.PlanSky); a hand-laid line keeps the look's.
    if (look is not null)
    {
        look.Sky = DarkTerritory.Game.Art.PlanSky.For(session.Route);
        look.Dress(renderer);
        vr?.Dress(look);
    }
    if (proto is not null)
        proto.Controls.Throttle = double.Parse(Arg("--throttle", "0"));
    var voice = net is null ? null : new VoiceChat(sound.Mixer) { PushToTalk = settings.PushToTalk || args.Contains("--push-to-talk") };
    using var mic = voice is null || settings.Mute || args.Contains("--mute") || args.Contains("--no-mic") ? null
        : AudioIn.Open(Audio.SampleRate, out var micError) is { } m ? m : NoMic(micError);
    var clock = new FixedStepClock(SimConstants.TickRate);
    var locomotion = vr is null ? null : new VrLocomotion(settings.Apply(DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File))));
    var levers = vr is null ? null : new VrLevers();
    // The HUD in the headset (T36), drawn as for the window but without the aiming cross.
    var vrHud = locomotion is null ? null : new VrPanel(locomotion.Tuning.Hud);
    var vrOverlay = new Overlay();
    bool showHud = settings.Hud && !args.Contains("--no-hud");
    // The card's side showing (C turns it over, and puts it away after the last); -1 put away.
    int cardPage = args.Contains("--card") ? 0 : -1, cardPages = 1;
    bool showPlan = args.Contains("--overlay");
    // --ride (linegen plan §20.2): the train drives itself by the line's authority, the camera outside, for looking a
    // generated line over in minutes.
    bool ride = args.Contains("--ride");
    var scene = new GreyboxScene
    {
        Look = look,
        Route = session.Route,
        Signs = session.World.Lineside?.Signs,
        SignRange = session.World.Lineside?.Tuning.LampSignRange ?? 350,
        Enemies = session.World.ActiveEnemies,
        Run = session.World.Run,
        Vehicles = session.Train.Vehicles,
        Bodies = session.World.Bodies.All,
        Diverging = session.Train.Diverging,
        Stands = session.World.Switches,
    };
    double last = timer.Elapsed.TotalSeconds, titleAt = 0;
    double pendingYaw = 0, pendingPitch = 0;
    int pendingNotch = 0;
    bool pendingReverser = false;
    var pendingLamp = LampSwitch.None;
    bool chase = ride;
    double sensitivity = 0.0025 * settings.MouseSpeed;
    // The player's keys (T80): each control's key, from the settings (a name the platform doesn't know: its default).
    var keyOf = Enum.GetValues<Control>().ToDictionary(c => c, c => Enum.TryParse<Key>(settings.KeyFor(c), out var k) ? k : Enum.Parse<Key>(Controls.Defaults[c]));
    Hud.Keys = settings;
    bool Held(Control c) => input.Down(keyOf[c]);
    bool Hit(Control c) => input.Pressed(keyOf[c]);
    Camera camera = default;
    FrameLighting lighting = default;
    window.MouseCaptured = true;
    window.TextInput = false;

    while (!window.CloseRequested && !QuitNow())
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
        // The night's over: Enter goes back (to the fortress, for a campaign night).
        if (session.World.Run?.Over == true && input.Pressed(Key.Enter))
            break;
        // The prototype drives from anywhere; networked, cab controls go through intent like everything else.
        sbyte notch = (sbyte)((Hit(Control.RegulatorOpen) ? 1 : 0) - (Hit(Control.RegulatorClose) ? 1 : 0));
        bool reverser = Hit(Control.Reverser);
        // The lamp switch (T52): a setting, the opposite of how the lamp is now, held until a tick sends it.
        if (Hit(Control.Lamp))
            pendingLamp = session.World.LampLit ? LampSwitch.Off : LampSwitch.On;
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
            proto.Controls.Brake = Held(Control.Brake) ? 1 : 0;
            if (ride && session.Route?.Plan is { } ridden)
                DarkTerritory.Game.LineGen.Ride.Drive(proto.Train, ridden, ref proto.Controls);
        }
        if (Hit(Control.Chase)) chase = !chase;
        if (input.Pressed(Key.F1)) showHud = !showHud;
        if (input.Pressed(Key.F2)) net?.ShowInviteDialog();
        // A generated line's route card (C: the paper the crew is handed) and the designer's overlay (F3).
        if (Hit(Control.RouteCard)) cardPage = cardPage + 1 >= cardPages ? -1 : cardPage + 1;
        if (input.Pressed(Key.F3)) showPlan = !showPlan;
        // An invite accepted (or "Join Game" on a friend) while playing: leave this game for theirs.
        if (Invited(net) is { } invitedTo)
        {
            relaunch = invitedTo;
            break;
        }

        pendingYaw -= input.MouseDX * sensitivity;
        pendingPitch -= input.MouseDY * sensitivity;
        if (locomotion is not null)
        {
            // In a headset the head looks; the mouse only turns the room.
            locomotion.Turn(pendingYaw);
            pendingYaw = pendingPitch = 0;
        }

        int ticks = clock.Advance(dt);
        for (int i = 0; i < ticks; i++)
        {
            var buttons = PlayerButtons.None;
            if (Held(Control.Run)) buttons |= PlayerButtons.Run;
            if (Held(Control.Jump)) buttons |= PlayerButtons.Jump;
            if (Held(Control.Use)) buttons |= PlayerButtons.Use;
            if (Held(Control.Fire)) buttons |= PlayerButtons.Fire;
            if (Held(Control.Throw)) buttons |= PlayerButtons.Throw;
            if (proto is null)
            {
                if (Held(Control.Brake)) buttons |= PlayerButtons.Brake;
                if (pendingReverser) buttons |= PlayerButtons.Reverser;
            }
            var intent = new PlayerIntent
            {
                MoveX = (Held(Control.Right) ? 1 : 0) - (Held(Control.Left) ? 1 : 0),
                MoveZ = (Held(Control.Forward) ? 1 : 0) - (Held(Control.Back) ? 1 : 0),
                LookYaw = (float)pendingYaw,
                LookPitch = (float)pendingPitch,
                Buttons = buttons,
                ThrottleNotch = proto is null ? (sbyte)Math.Clamp(pendingNotch, -4, 4) : (sbyte)0,
                Lamp = pendingLamp,
            };
            if (locomotion is not null)
            {
                locomotion.Follow(session.Player, Eyes.Heading(session.Player, session.Train.Frames));
                var headset = locomotion.Intent(session.Player, vr!.Session.Controllers, session.PlayerTuning.LadderClimb);
                intent.MoveX = Math.Clamp(intent.MoveX + headset.MoveX, -1, 1);
                intent.MoveZ = Math.Clamp(intent.MoveZ + headset.MoveZ, -1, 1);
                intent.LookYaw = headset.LookYaw;
                intent.LookPitch = headset.LookPitch;
                intent.Buttons |= headset.Buttons;
                (intent.HandX, intent.HandY, intent.HandZ) = (headset.HandX, headset.HandY, headset.HandZ);
                // The cab's levers by hand (T29): the same notches, brake and reverser a keyboard sends.
                levers!.Apply(ref intent, session.Player, session.Train, session.Controls, session.PlayerTuning.Hand);
            }
            pendingNotch = 0;
            pendingLamp = LampSwitch.None;
            pendingReverser = false;
            pendingYaw = pendingPitch = 0;
            session.Step(intent);
            if (campaign is not null && session is NetPlaySession played)
                campaign = Autosave(saves, campaign, played);
            // The ears are where the eyes were last frame; audio follows the sim tick so no shot is missed.
            bool exposed = !PlayerMotor.Indoors(session.Player, session.Train);
            sound.Update(session.World, session.Controls, Listener.At(camera.Position, camera.Yaw), exposed, SimConstants.TickSeconds,
                PlayerMotor.Space(session.Player, session.Train));
            if (voice is not null && net is not null)
                voice.Update(net.Client, session.Crew(session.InterpolatedFrames(1), 1), SimConstants.TickSeconds);
        }
        if (voice is not null && net is not null)
        {
            voice.TalkHeld = Held(Control.Talk);
            // Only with a radio on you (T41); the host checks too.
            voice.RadioHeld = Held(Control.Radio) && session.World.Bodies.HasRadio(session.PlayerId);
            for (int n; mic is not null && (n = mic.Read(micSamples)) > 0;)
                voice.Capture(micSamples.AsSpan(0, n), net.Client);
        }
        FeedSpeaker();

        var frames = session.InterpolatedFrames(clock.Alpha);
        camera = chase ? Views.Get("chase", session.Train) : session.EyeCamera(frames, clock.Alpha, pendingYaw, pendingPitch);
        scene.Crew = session.Crew(frames, clock.Alpha);
        scene.Time = now;
        lighting = Views.Lighting(frames[0], look);
        lighting.Time = now;
        // Lamps down (T52), smashed, or no power in a Vigil: no beam.
        if (!session.World.LampShining)
            lighting.LampIntensity = 0;
        if (session.Route is { } r)
        {
            lighting.FogDensity = (float)r.Weather.FogDensity;
            lighting.Wetness = r.Weather.Wet ? 1 : 0;
        }
        scene.FireGlow = (float)(session.Train.BoilerTuning is { } bt ? session.Train.Boiler.FireFraction(bt) : 0.7);
        scene.Tick = session.Tick;
        scene.Pressure = (float)(session.Train.BoilerTuning is { } pt ? session.Train.Boiler.Pressure / pt.PressureMax : 0.78);
        // A Vigil: emergency lighting, and no power to the headlamp.
        scene.Emergency = session.World.EmergencyLights;
        scene.LampLit = session.World.LampShining;
        scene.Controls = session.Controls;
        if (!session.World.LampShining)
            lighting.LampRange = 0.01f; // not 0: the shader divides by it
        scene.Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
        if (showHud)
        {
            Hud.Build(overlay, UiWidth, UiHeight, session);
            // Q held: the crew roster (T69), with who's been heard.
            if (Held(Control.Roster))
                Hud.Roster(overlay, UiWidth, UiHeight, session.Roster(), voice is null ? null : voice.SinceHeard);
            if (session.Route?.Plan is { } shown)
            {
                if (cardPage >= 0)
                    cardPages = DarkTerritory.Game.LineGen.PlanHud.RouteCard(overlay, UiWidth, UiHeight, shown, cardPage);
                if (showPlan)
                    DarkTerritory.Game.LineGen.PlanHud.Overlay(overlay, UiWidth, UiHeight, session, shown);
            }
            if (session.World.Run?.Over == true)
                overlay.TextCentred(UiWidth / 2f, UiHeight - 22, campaign is not null ? "ENTER: BACK TO THE FORTRESS" : "ENTER: BACK", new Vector4(1, 0.7f, 0.3f, 1));
        }
        if (vr is null)
        {
            renderer.Prepare(mesh, showHud ? overlay : null);
            Present(camera, lighting);
        }
        // The body is the flat camera's eye point, turned to where the room faces; the head does the looking.
        VrPanelContent? onPanel = null;
        if (vr is not null && showHud)
        {
            Hud.Build(vrOverlay, 480, 270, session, crosshair: false);
            if (session.World.Run?.Over == true)
                vrOverlay.TextCentred(240, 248, campaign is not null ? "A: BACK TO THE FORTRESS" : "A: BACK", new Vector4(1, 0.7f, 0.3f, 1));
            onPanel = new VrPanelContent(vrHud!, vrOverlay, 480, 270);
        }
        if (vr is not null && vr.Frame(mesh, locomotion!.Body(camera, Eyes.Heading(session.Player, frames)), lighting, lighting.FogColor, locomotion, onPanel) == XrFrameResult.Exiting)
            break;
        if (vr is not null)
            Mirror();
        // The night's over: A goes back, as Enter does.
        if (vr is not null && session.World.Run?.Over == true && vr.Session.Controllers.Primary)
            break;

        if (now >= titleAt)
        {
            string talking = voice is { Transmitting: true } ? voice.RadioHeld ? " | ON THE RADIO" : " | talking" : "";
            window.Title = $"Dark Territory — {session.Status()}{talking}";
            titleAt = now + 0.25;
        }
        input.EndFrame();
        frameCount++;
    }

    if (capture is not null)
    {
        var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, showHud ? overlay : null);
        PngWriter.Write(capture, pixels, renderer.Width, renderer.Height, scale: 1);
        Console.WriteLine($"captured {Path.GetFullPath(capture)}");
    }
    Console.WriteLine($"ticks {session.Tick}, {session.Status()}");
    window.Title = "Dark Territory";
    return campaign;
}

// Spec E: autosave on each departure from a facility, and settle the night into the slot when it's over.
static CampaignState Autosave(SaveSlots saves, CampaignState campaign, NetPlaySession session)
{
    if (campaign.Current is null)
        return campaign;
    if (session.World.Run?.Report is { } report)
    {
        var settled = Campaign.Settle(campaign, report);
        saves.Save(settled);
        Console.WriteLine($"campaign: {report.End}, net {report.Net:0} scrip; now {settled.Cars} cars and {settled.Scrip:0} scrip after {settled.Runs} nights");
        return settled;
    }
    if (session.Checkpoint is { } c && !ReferenceEquals(c, campaign.Checkpoint))
    {
        campaign = campaign with { Checkpoint = c };
        saves.Save(campaign);
    }
    return campaign;
}
