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
using CrewActs = DarkTerritory.Game.Art.CrewActs;

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
// Options: --route tier:seed | --route-file name (saved from dt edit) [--no-enemies] | --line name, --cars n --internal WxH --throttle 0..1 --quit-after seconds --capture file.png --derail-at seconds --mute --greybox (flat colour, no art pass)
// Multiplayer (UDP, direct IP / LAN): --host [port] hosts the same options for others to join; --join address[:port] joins one.
//   A hosted run is public (on the LAN beacon, and a public Steam lobby) unless --private (invite and address only).
//   --password words makes it a private run, listed with a lock (note 450): joiners need the words (--join … --password
//   words), the host's Steam friends don't. --mood laughs|competitive says who it's for.
// Steam: --steam hosts a lobby as well (F2 opens the invite dialog; friends can also "Join Game" from the
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

// A crash leaves a report (the exception, and the last things the game said) in the user's app data (--crashes dir
// elsewhere), and the next launch opens on a notice that says where it is (note 411).
int crashesAt = Array.IndexOf(args, "--crashes");
var crashes = CrashReports.Install(crashesAt >= 0 && crashesAt + 1 < args.Length ? args[crashesAt + 1] : null);
#if DEVTOOLS
// A developer build's tools (note 514): reached only here and under the other DEVTOOLS hooks; a player's build has none of it.
var dev = DarkTerritory.Dev.DevTools.Start(args);
#endif

// The system's file browser on a folder (note 411): Explorer, Finder, or whatever xdg-open hands it to; or, given an address
// (note 434's store page), the browser.
void ShellOpen(string path)
{
    try
    {
        if (!path.StartsWith("https://", StringComparison.Ordinal) && !path.StartsWith("mailto:", StringComparison.Ordinal))
            System.IO.Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
    catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        Console.WriteLine($"couldn't open {path}: {e.Message}");
    }
}

string Arg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

// Mods (T49) laid over the base content, unless --no-mods plays the base game.
// A mod manager's profile (Thunderstore, T78) comes in as --mods-dir.
args = Mods.TakeArgs(args);
var content = Mods.Mount(DataFile.FindContentRoot(Environment.CurrentDirectory), enabled: !args.Contains("--no-mods"));
// The figures out in the Territory and their words (note 570): Dave.
FigureTalk.Words = FigureWords.Load(content);
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
// The player's profile (note 180): commendations, and whether a night they hosted has had a child's call (B.6; note 182).
var profile = new PlayerProfile(args.Contains("--saves") ? Path.Combine(saves.Directory, "profile.json") : PlayerProfile.DefaultPath);
// Note 350: the loading screen's tips and the first nights' card, and how many of this player's nights are over.
var onboarding = Onboarding.Load(content);
int nightsOver = profile.Load().Nights;
// Note 349: CAPTIONS, the sounds worth hearing written over the hotbar, fed from the mixer's voices each frame.
var captions = new Captions(Captions.Load(content));
// GDD v1.4 App. E.6: the derailment's shuffle bag for nights without a campaign slot (a slot keeps its own), in app data.
var musicBags = new MusicBagFile(args.Contains("--saves") ? Path.Combine(saves.Directory, "music-bag.json") : MusicBagFile.DefaultPath);
var frontEnd = new FrontEnd(campaignTuning, runTuning, saves, Arg("--settings", Settings.DefaultPath), edition: EditionTuning.Load(content))
{
    Protocol = DarkTerritory.Sim.Net.Protocol.Version,
    DefaultPlayerName = steam?.NameOf(steam.Me) ?? Environment.UserName,
    // GDD v1.4 App. E.6: the credits screen lists every track's performers (note 194).
    Music = DarkTerritory.Sim.Music.MusicManifest.Load(content).Tracks,
    // Note 390: everyone else whose work is in the game, from the content's own provenance.
    CreditSections = DarkTerritory.Game.Credits.Load(content),
    // MODS (note 323): what Mount found installed, and whether --no-mods left it all off.
    InstalledMods = [.. Mods.Installed.Mods.Select(m => new InstalledMod(m.Name, m.Version, m.Description))],
    ModProblems = Mods.Installed.Problems,
    ModsOff = Mods.Off,
    // The settings' MICROPHONE: what there is to choose from.
    MicDevices = args.Contains("--mute") || args.Contains("--no-mic") ? [] : AudioIn.Devices(),
    // OUTFIT (note 298): the crew's looks by name.
    OutfitNames = look?.Tuning.CrewColourNames is { Length: > 0 } outfits ? outfits : ["RED", "BLUE", "OCHRE", "TEAL", "GREEN", "VIOLET", "ORANGE", "WHITE"],
    // The PROFILE page (note 293): the commendations kept in the profile, and where the nights' stills go (note 203).
    Profile = profile.Load(),
    StillsFolder = BookmarkAlbum.DefaultDirectory,
    // Note 411: the game stopped last time, and the console that said where its report is was never seen.
    Crash = CrashReports.Unseen(crashes.Directory),
    // Note 452: where reports go, and the player's own report of a problem.
    Reports = ReportsTuning.Load(content),
    ProblemReport = () => crashes.WriteProblem(),
};
// Note 452: every report says what the game is: its edition and mods, whether Steam's up, and (asked as it's written) the
// settings, the screen or the night and where it had got to.
crashes.Context("edition", EditionTuning.Load(content).Name);
crashes.Context("mods", Mods.Off ? "off (--no-mods)" : Mods.Installed.Mods.Count == 0 ? "none"
    : string.Join(", ", Mods.Installed.Mods.Select(m => $"{m.Name} {m.Version}")) + (Mods.Installed.Problems.Count > 0 ? $"; {Mods.Installed.Problems.Count} couldn't load" : ""));
crashes.Context("steam", steam is null ? "off" : "on");
crashes.Context("args", args.Length == 0 ? "none" : string.Join(" ", args));
IPlaySession? playing = null;
string doing = "the menus";
crashes.Live = () =>
[
    new ReportField("settings", ReportFields.Settings(frontEnd.Settings)),
    new ReportField("doing", playing is null ? $"{doing}: {frontEnd.Screen}" : doing),
    .. playing is { } night ? [new ReportField("night", ReportFields.Night(night))] : (ReportField[])[],
];
if (frontEnd.Crash is not null)
    frontEnd.Show(Screen.Crashed);

// A night named on the command line starts straight away; otherwise it's the front end's choice.
Launch? LaunchFromArgs()
{
    int cars = int.Parse(Arg("--cars", "6"));
    int? port = !args.Contains("--host") ? null : int.TryParse(Arg("--host", ""), out var p) ? p : NetPlaySession.DefaultPort;
    string? password = Arg("--password", "") is { Length: > 0 } pw ? pw : null;
    if (connectLobby is { } lobby)
        return new Launch.JoinLobby(lobby) { Password = password };
    if (args.Contains("--join"))
        return new Launch.Join(Arg("--join", "127.0.0.1")) { Password = password };
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
        return new Launch.Night(route, cars, host)
        {
            Line = Arg("--line", "test-loop"),
            RouteFile = routeFile,
            Bots = int.TryParse(Arg("--bots", "0"), out var b) ? b : 0,
            Public = !args.Contains("--private") || password is not null,
            Password = password,
            Mood = Moods.Parse(Arg("--mood", "")),
        };
    return null;
}
var launch = LaunchFromArgs();
bool fromCommandLine = launch is not null;

// The frame renders at the window's 720p (the 2008-2012 target, ARCHITECTURE §8 note 57); the HUD and menus keep their
// 480x270 canvas (their pixel font's), scaled up over it.
// --internal pins it (captures, perf); otherwise it's the player's resolution at their render scale (T83, Settings).
bool pinnedInternal = args.Contains("--internal");
var internalSize = Arg("--internal", "1280x720").Split('x').Select(int.Parse).ToArray();
// Note 347: TEXT SIZE draws it smaller (Settings.Canvas), so the same print is bigger in the window.
int UiWidth = Settings.CanvasWidth, UiHeight = Settings.CanvasHeight;
double quitAfter = double.Parse(Arg("--quit-after", "0"));
double derailAt = double.Parse(Arg("--derail-at", "0"), System.Globalization.CultureInfo.InvariantCulture);
string? capture = Arg("--capture", "") is { Length: > 0 } c ? c : null;

var startSettings = frontEnd.Settings;
if (!pinnedInternal)
    internalSize = [startSettings.InternalSize.Width, startSettings.InternalSize.Height];
// --window WxH (note 459): the window at a size of its own, any shape (a Steam Deck's 1280x800, an ultrawide), windowed; the
// frame keeps its shape inside it. For seeing the real window headless (Xvfb at that size, the screen captured).
var windowSize = Arg("--window", "") is { Length: > 0 } ws && ws.Split('x') is [var wws, var whs] && int.TryParse(wws, out int ww) && int.TryParse(whs, out int wh)
    ? (Width: ww, Height: wh) : (Width: 0, Height: 0);
using var window = new Window("Dark Territory", windowSize.Width > 0 ? windowSize.Width : startSettings.WindowSize.Width,
    windowSize.Height > 0 ? windowSize.Height : startSettings.WindowSize.Height);
// The icon (tools/art/store/icon.py): the headlamp's glow and the stencilled DT.
if (Path.Combine(content, "art", "ui", "icon.png") is var iconPath && File.Exists(iconPath))
{
    var icon = Ballast.Render.ImageFile.Load(iconPath);
    window.SetIcon(icon.Width, icon.Height, icon.Rgba);
}
window.Fullscreen = startSettings.Fullscreen && windowSize.Width == 0;
// --vr: the headset makes the GPU (it has to pick the device and the extensions), and the window mirrors the flat view.
using var vr = args.Contains("--vr") ? StartVr() : null;
VrView? StartVr()
{
    try
    {
        // Both eyes in one pass where the GPU can (tuning/vr.json "stereo", ARCHITECTURE §8 note 221).
        var stereo = DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File)).Stereo;
        var view = VrView.Start("Dark Territory", double.Parse(Arg("--vr-scale", "0.5")), Window.VulkanInstanceExtensions(), window.CreateSurface, stereo);
        Console.WriteLine($"vr: {view.Headset.System} on {view.Headset.Runtime}, {view.Session.EyeWidth}x{view.Session.EyeHeight} per eye, {view.Stereo}");
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
crashes.Context("gpu", gpu.DeviceName);
crashes.Context("vr", vr is null ? "off" : "on");
// --crash-test (note 452): stops the game on purpose once it's up, so the whole path (the report, its twin, the notice on
// the next launch, the mail) can be tried on any machine.
if (args.Contains("--crash-test"))
    throw new InvalidOperationException("--crash-test: stopped on purpose to try the crash report");
var renderer = new GreyboxRenderer(gpu, internalSize[0], internalSize[1]) { OverlaySize = new Vector2(UiWidth, UiHeight) };
using var rendererOwner = new Owner(() => renderer.Dispose());
if (look is not null)
{
    look.Dress(renderer);
    vr?.Dress(look);
}
var (w, h) = window.PixelSize;
// In VR the headset sets the pace; the mirror shouldn't wait for the monitor as well.
using var swapchain = new Swapchain(gpu, w, h, vsync: vr is null && startSettings.VSync);
var shownSettings = startSettings;

// T83: a display setting changed in the menus takes now. The window (fullscreen, its size), the swapchain (vsync), and the
// renderer itself when the size it draws at changes (made again at the new size and dressed again). In a headset the
// headset sets all of this.
void ApplyDisplay()
{
    var now = frontEnd.Settings;
    // Note 347: the text size takes at once, as the canvas the overlay's stretched from. A headset's panel keeps its own.
    if (vr is null && now.Canvas != (UiWidth, UiHeight))
    {
        (UiWidth, UiHeight) = now.Canvas;
        renderer.OverlaySize = new Vector2(UiWidth, UiHeight);
    }
    if (vr is not null || now.Fullscreen == shownSettings.Fullscreen && now.Resolution == shownSettings.Resolution
        && now.RenderScale == shownSettings.RenderScale && now.VSync == shownSettings.VSync)
        return;
    window.Fullscreen = now.Fullscreen;
    if (!now.Fullscreen && (now.Resolution != shownSettings.Resolution || now.Fullscreen != shownSettings.Fullscreen))
        window.SetSize(now.WindowSize.Width, now.WindowSize.Height);
    if (now.VSync != swapchain.VSync)
    {
        swapchain.VSync = now.VSync;
        window.Resized = true;
    }
    if (!pinnedInternal && now.InternalSize != (renderer.Width, renderer.Height))
    {
        gpu.WaitIdle();
        renderer.Dispose();
        renderer = new GreyboxRenderer(gpu, now.InternalSize.Width, now.InternalSize.Height) { OverlaySize = new Vector2(UiWidth, UiHeight) };
        look?.Dress(renderer);
        Console.WriteLine($"display: drawing at {renderer.Width}x{renderer.Height}");
    }
    shownSettings = now;
}
Console.WriteLine($"GPU: {gpu.DeviceName}, window {w}x{h}, internal {renderer.Width}x{renderer.Height}");

var sound = new GameAudio(content);
// The menus' sounds (ui-menus): flat, through the same mixer the speaker's fed from in the menus too.
frontEnd.Cue = sound.Ui;
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
        sound.Mixer.Volumes = frontEnd.Settings.Volumes;
        sound.Mixer.Render(audioBlock);
        if (frontEnd.Settings.Mute)
            Array.Clear(audioBlock);
        speaker.Write(audioBlock);
    }
}

var input = window.Input;
// Note 459: the mouse in the overlay's pixels. The frame keeps its shape in the window, bars beside it where the window's is
// another, so the mouse is read inside the frame, not across the window (on a bar it's off the overlay's edge).
Vector2 OverlayMouse()
{
    var (pw, ph) = window.PixelSize;
    var (x, y) = Letterbox.Inside(input.MouseX, input.MouseY, Letterbox.Fit(renderer.Width, renderer.Height, pw, ph), pw, ph);
    return new Vector2(x * UiWidth, y * UiHeight);
}
var timer = Stopwatch.StartNew();
var mesh = new MeshBuilder();
var overlay = new Overlay();
Camera menuView = default;
FrameLighting menuLight = default;
long frameCount = 0;
var steamEvents = new List<OnlineEvent>();
LobbyId? relaunch = null;

void Present(in Camera camera, in FrameLighting lighting)
{
    ApplyDisplay();
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

bool menuOpened = false;
bool QuitNow() => quitAfter > 0 && timer.Elapsed.TotalSeconds >= quitAfter;

// The front end over a night scene: the fortress yard with a train standing in it, the camera slowly looking about.
Launch? Menu()
{
    window.MouseCaptured = false;
    // Note 267: in the menus a click is a click on what the pointer's over, not the window taking the mouse.
    window.CaptureOnClick = false;
    try
    {
        return MenuLoop();
    }
    finally
    {
        window.CaptureOnClick = true;
    }
}

Launch? MenuLoop()
{
    var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
    var line = DarkTerritory.Sim.Rail.RailLine.Load(Path.Combine(content, "lines", "test-loop.json"));
    var standing = new TrainOnLine(new TrainDynamics(Consist.Uniform(trainTuning, 6, 1)), line, 1200);
    var view = Views.Get("trackside", standing);
    var backdrop = new GreyboxScene { Time = 0.37, Look = look };
    backdrop.Build(mesh, standing, view.Position);
    var light = Views.Lighting(standing, look);
    (menuView, menuLight) = (view, light);
    double started = timer.Elapsed.TotalSeconds;
    // In a headset the menus float ahead, over the yard (T36), and the controllers work them.
    var vrMenu = vr is null ? null : new VrPanel(DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File)).Menu);
    var vrKeys = new VrMenuInput();
    frontEnd.Headset = vr is not null;
    // The title's sound under the front end, until a night starts (the main loop stops it).
    sound.UiLoop(UiCue.Title, on: true);
    // Whatever's held coming in (the A that ended the night) isn't a press here.
    if (vr is not null)
        vrKeys.Read(vr.Session.Controllers);
    // The public games (T116; the user's playtest: "Join should work like Lethal Company"), for the join screen: the local
    // network's, and Steam's lobby search when Steam's up.
    using var browser = new LobbyBrowser(new Ballast.Net.LanBrowser(), steam);
    // Note 351 (the polish pass, headless with --capture): --screen name opens a menu screen from the first frame (fortress
    // is slot 1's), and --select n moves n rows down it.
    if (Arg("--screen", "") is { Length: > 0 } opened && !menuOpened)
    {
        menuOpened = true;
        if (opened.Equals("fortress", StringComparison.OrdinalIgnoreCase))
            frontEnd.ShowFortress(1);
        else if (Enum.TryParse<Screen>(opened, ignoreCase: true, out var screen))
            frontEnd.Show(screen);
        for (int i = 0; i < int.Parse(Arg("--select", "0")); i++)
            frontEnd.Down();
    }
    while (!window.CloseRequested && !QuitNow())
    {
        window.PumpEvents();
        window.TextInput = frontEnd.WantsText;
        if (Invited(null) is { } lobby)
            return new Launch.JoinLobby(lobby);
        if (frontEnd.TakeRefresh())
            browser.Refresh();
        browser.Poll(timer.Elapsed.TotalSeconds, steamEvents, search: frontEnd.Screen == Screen.Join);
        frontEnd.Games = browser.Games;
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
            // The keys as ever, and the mouse (note 264): hover, click, the wheel, in the overlay's pixels (it's stretched
            // over the frame, which keeps its shape in the window: note 459). Typing only while a field's being edited (the window turns text input on for that).
            var keys = MenuInput.Keys(name => Enum.TryParse<Key>(name, out var k) && input.Pressed(k), frontEnd.WantsText);
            var mouse = vr is not null ? (MenuMouse?)null : new MenuMouse(OverlayMouse(), input.MouseMoved,
                input.Pressed(Key.MouseLeft), input.Pressed(Key.MouseRight), input.Wheel);
            chosen = MenuInput.Apply(frontEnd, keys, frontEnd.WantsText ? input.Text : "", mouse);
        }
        if (vr is not null)
            chosen ??= VrMenuInput.Apply(vrKeys.Read(vr.Session.Controllers), frontEnd);
        // Note 411: OPEN THE REPORTS shows their folder in the system's file browser, and the menu stays up under it.
        if (chosen is Launch.OpenFolder folder)
        {
            ShellOpen(folder.Path);
            chosen = null;
        }
        // Note 452: a report to the studio, the player's mail on it and its folder beside it to attach the file.
        if (chosen is Launch.Mail mail)
        {
            ShellOpen(mail.Folder);
            ShellOpen(mail.MailTo);
            chosen = null;
        }
        // Note 434: WISHLIST ON STEAM, the store page in the overlay over the menu, or the browser when the overlay's off.
        if (chosen is Launch.Wishlist wishlist)
        {
            if (steam?.ShowStorePage(wishlist.App) != true)
                ShellOpen(wishlist.Url);
            chosen = null;
        }
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
#if DEVTOOLS
        dev.Draw(overlay, UiWidth, UiHeight);
#endif
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
    // --capture with no night (note 351): the menu as it was last drawn, over its yard.
    if (capture is not null && vr is null && QuitNow())
    {
        var camera = view;
        camera.Yaw += Math.Sin((timer.Elapsed.TotalSeconds - started) * 0.07) * 0.25;
        frontEnd.Draw(overlay, UiWidth, UiHeight);
        var pixels = renderer.Render(mesh, camera, light, light.FogColor, overlay);
        PngWriter.Write(capture, pixels, renderer.Width, renderer.Height, scale: 1);
        Console.WriteLine($"captured {Path.GetFullPath(capture)}");
    }
    return null;
}

// What a night was, for a report (note 452): "a hosted night, frontier:7, 6 cars, 3 bots".
static string Doing(Launch chosen) => chosen switch
{
    Launch.CampaignNight c => $"a campaign night, slot {c.Slot}, contract {c.Contract}{(c.Resume ? ", resumed" : "")}{(c.Host ? ", hosted" : "")}",
    Launch.Night n => $"a {(n.Host ? "hosted" : "solo")} night, {n.Route ?? n.RouteFile ?? n.Line}, {n.Cars} cars{(n.Bots > 0 ? $", {n.Bots} bots" : "")}",
    Launch.Join j => $"joined {j.Address}",
    Launch.JoinLobby l => $"joined Steam lobby {l.Lobby}",
    _ => chosen.GetType().Name,
};

// Starts what was chosen. A campaign night begins (or resumes) its slot's contract and saves that first.
(IPlaySession Session, CampaignState? Campaign) Start(Launch chosen)
{
    bool enemies = !args.Contains("--no-enemies");
    switch (chosen)
    {
        case Launch.JoinLobby lobby when steam is not null:
            Console.WriteLine($"joining lobby {lobby.Lobby} on Steam…");
            return (NetPlaySession.JoinLobby(content, steam, lobby.Lobby, password: lobby.Password), null);
        case Launch.Join join:
            {
                string target = join.Address;
                var endpoint = IPEndPoint.TryParse(target, out var ep) ? ep
                    : new IPEndPoint(Dns.GetHostAddresses(target.Split(':')[0]).First(a => a.AddressFamily == AddressFamily.InterNetwork),
                        target.Contains(':') && int.TryParse(target.Split(':')[1], out var jp) ? jp : NetPlaySession.DefaultPort);
                if (endpoint.Port == 0)
                    endpoint.Port = NetPlaySession.DefaultPort;
                Console.WriteLine($"joining {endpoint}…");
                return (NetPlaySession.Join(content, endpoint, password: join.Password), null);
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
                var setup = new SessionSetup(Route: contract.Route, Cars: campaign.Cars, Enemies: enemies)
                {
                    Upgrades = campaign.Upgrades,
                    SpareKits = campaign.SpareKits,
                    MusicBag = campaign.Music,
                    Identities = campaign.Identities,
                    // GDD §9 (note 182): the contract's freight in every loaded car, and the stores bought for the night.
                    Cargo = contract.Cargo,
                    Powder = campaign.Stores.Powder,
                    SpareLamps = campaign.Stores.Lamps,
                    SpareExtinguishers = campaign.Stores.Extinguishers,
                    FirstChildReal = profile.FirstChildReal,
                    LastTown = campaign.LastTown,
                };
                Console.WriteLine($"campaign slot {night.Slot} ({campaign.Name}): {campaign.Cars} cars, {campaign.Scrip:0} scrip, tonight {contract.Route} carrying {Cargoes.Name(contract.Cargo)} at {contract.PerCar:0} a car{(resume is not null ? $", resuming after facility {resume.Facility}" : "")}");
                // Note 450: private, behind the host screen's password (listed with a lock); with none typed, unlisted as before.
                string? password = frontEnd.Settings.PublicLobby ? null : frontEnd.Settings.LobbyPassword.Trim() is { Length: > 0 } pw ? pw : null;
                return (NetPlaySession.HostGame(content, setup, port, online: night.Host ? steam : null, resume: resume,
                    listed: frontEnd.Settings.PublicLobby || password is not null, lobbyName: frontEnd.LobbyName, password: password, mood: frontEnd.Settings.Mood), campaign);
            }
        // From the menu always the real night (T110: with no bots too, it's the same game alone); the bare prototype is the
        // command line's, for a route or line on its own.
        case Launch.Night hosted when hosted.Host || hosted.Bots > 0 || !fromCommandLine:
            {
                // From the menu, friends join on the usual port; `--steam` alone takes no UDP port (the lobby's enough). A night
                // with bots and no friends (T89) is hosted privately: the bots are clients on localhost.
                int? port = !hosted.Host ? null : args.Contains("--host") ? int.TryParse(Arg("--host", ""), out var p) ? p : NetPlaySession.DefaultPort
                    : fromCommandLine ? null : NetPlaySession.DefaultPort;
                var setup = new SessionSetup(Route: hosted.Route, Line: hosted.Line, Cars: hosted.Cars, Enemies: enemies)
                {
                    MusicBag = musicBags.Load(),
                    FirstChildReal = profile.FirstChildReal,
                };
                var session = NetPlaySession.HostGame(content, setup, port, online: hosted.Host ? steam : null, bots: hosted.Bots,
                    listed: hosted.Public, lobbyName: hosted.LobbyName, password: hosted.Password, mood: hosted.Mood);
                if (hosted.Bots > 0)
                    Console.WriteLine($"a crew of {hosted.Bots} bot{(hosted.Bots == 1 ? "" : "s")} aboard");
                if (port is not null)
                    Console.WriteLine($"hosting on UDP port {session.Port}: others join with --join <this machine's address>:{session.Port}");
                if (steam is not null && hosted.Host)
                    Console.WriteLine($"hosting a {(session.Locked ? "private (password)" : hosted.Public ? "public" : "friends-only")} Steam lobby, \"{session.LobbyName}\", as {steam.NameOf(steam.Me)}: F2 to invite");
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
    sound.UiLoop(UiCue.Title, on: false);
    if (launch is null or Launch.Quit || launch is Launch.JoinLobby && steam is null)
        break;
    // T116 playtest ("the linux build crashed ... keeps getting a 'not responding' message"): connecting and building the
    // night's line take seconds, and ran on the window's thread with nothing pumping it, and a join nobody answered threw
    // out of the game. Now they run behind a loading screen, and a failure is said on the menu.
    // Who you are and what you wear go in your Hello as the night's joined or hosted (notes 267, 298), so they're the
    // settings' before it starts (the name was only set once the first night was under way).
    NetPlaySession.PlayerName = frontEnd.Settings.PlayerName;
    NetPlaySession.Outfit = frontEnd.Settings.OutfitByte(frontEnd.OutfitNames.Count);
    if (Starting(launch) is not { } begun)
    {
        if (fromCommandLine)
            break;
        launch = null;
        continue;
    }
    var (session, campaign) = begun;
    var leaving = launch;
    launch = null;
    // Note 452: a report written in the night says which, and where it had got to.
    (playing, doing) = (session, Doing(leaving));
    campaign = Play(session, campaign);
    (playing, doing) = (null, "the menus");
    // Left: nothing of the night follows into the menus (its bed, its loops, a hold's tick).
    sound.EndNight();
    // What the crew commended you for tonight, on the PROFILE page from now (note 293).
    frontEnd.Profile = profile.Load();
    // B.6 (note 182): a night this host ran had a child's call in it; from now on every call is the dice's.
    if (session is NetPlaySession { Host.World.ChildCalled: true })
        profile.MarkChildCalled();
    // E.6: a night without a slot keeps its shuffle bag in app data (a campaign's went into its save as it settled).
    if (campaign is null && session is NetPlaySession { MusicBag: { } bag } hosting && hosting.Host!.World.DerailMusic != 0)
        musicBags.Save(bag);
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

// Starts what was chosen off the window's thread, drawing what it's doing meanwhile; null (the menu says why) if it failed.
(IPlaySession Session, CampaignState? Campaign)? Starting(Launch chosen)
{
    var task = Task.Run(() => Start(chosen));
    // The user's playtest: "You should be able to host a run, not a night."
    string doing = chosen is Launch.Join or Launch.JoinLobby ? "JOINING"
        : chosen is Launch.Night { Host: true } or Launch.CampaignNight { Host: true } ? "BUILDING THE RUN" : "BUILDING THE NIGHT";
    double began = timer.Elapsed.TotalSeconds;
    while (!task.IsCompleted)
    {
        window.PumpEvents();
        if (window.CloseRequested)
            break;
        overlay.Clear();
        string dots = new('.', 1 + (int)((timer.Elapsed.TotalSeconds - began) * 2) % 3);
        // Note 350: with FIRST NIGHTS on, the night's tip under it (a new one each of the player's nights).
        Onboarding.DrawLoading(overlay, UiWidth, UiHeight, doing + dots, chosen is Launch.Join j ? j.Address.ToUpperInvariant() : "THE LINE, THE LAND, THE CREW",
            Onboarding.Tip(onboarding, frontEnd.Settings, nightsOver));
        // From the command line there's no menu yard behind it (no camera yet): the window's only kept answering.
        if (vr is null && menuView.FovYDegrees > 0)
        {
            renderer.Prepare(mesh, overlay);
            Present(menuView, menuLight);
        }
        input.EndFrame();
        Thread.Sleep(15);
    }
    if (!task.IsCompleted)
        return null;
    if (task.Exception?.GetBaseException() is { } failed)
    {
        Console.WriteLine($"couldn't start {chosen.Redacted()}: {failed}");
        // Note 450: a private run's host said the password wasn't its own: asked for again, over why.
        if (failed is JoinRefusedException { Refusal.Reason: DarkTerritory.Sim.Net.RefusalReason.Password })
            frontEnd.AskPassword(chosen, chosen is Launch.Join j ? j.Address : "the run", failed.Message);
        else
            frontEnd.Failed(chosen is Launch.Join or Launch.JoinLobby ? Screen.Join : Screen.Title,
                failed is IOException or System.Net.Sockets.SocketException ? failed.Message : $"couldn't start: {failed.Message}");
        return null;
    }
    return task.Result;
}

// One night, until it's left, the window closes, or an invite takes us elsewhere. Returns the campaign as it stands.
CampaignState? Play(IPlaySession session, CampaignState? campaign)
{
    var settings = frontEnd.Settings;
    var proto = session as PrototypeSession;
    var net = session as NetPlaySession;
    // The yard's readings go at its voice's pace (note 240): the card typed as it's said.
    if (net is not null && sound.Clerk.Speaks)
        net.RadioPace = sound.Clerk.Seconds;
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
    // The radio's own clicks, squelch and static (voice-radio-sfx) follow what it's doing.
    sound.Voice = voice;
    using var mic = voice is null || settings.Mute || args.Contains("--mute") || args.Contains("--no-mic") ? null
        : AudioIn.Open(Audio.SampleRate, out var micError, settings.MicDevice) is { } m ? m : NoMic(micError);
    if (voice is not null)
        voice.MicLevel = (float)settings.MicLevel;
    var clock = new FixedStepClock(SimConstants.TickRate);
    var locomotion = vr is null ? null : new VrLocomotion(settings.Apply(DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File))));
    var levers = vr is null ? null : new VrLevers();
    // The HUD in the headset (T36), drawn as for the window but without the aiming cross.
    var vrHud = locomotion is null ? null : new VrPanel(locomotion.Tuning.Hud);
    var vrOverlay = new Overlay();
    bool showHud = settings.Hud && !args.Contains("--no-hud");
    // The card's side showing (C turns it over, and puts it away after the last); -1 put away.
    int cardPage = args.Contains("--card") ? 0 : -1, cardPages = 1;
    // The supplies aboard (the director's decision of 2026-10-06; note 264): toggled on and off, never always there.
    bool showSupplies = args.Contains("--supplies");
    bool rosterOut = false;
    double stokerSince = -1;
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
        Hits = session.World.Hits,
        Swings = session.World.Swings,
        Outfits = session.World.Outfits,
        Impacts = session.World.Impacts,
        Run = session.World.Run,
        Holdouts = session.World.Holdouts,
        Town = session.World.Town,
        Vehicles = session.Train.Vehicles,
        HotBoxTuning = session.Train.HotBoxTuning,
        Gutter = session.Train.Gutter,
        Handrails = session.Train.Dynamics.Tuning.Composition.Handrails,
        Bodies = session.World.Bodies.All,
        Diverging = session.Train.Diverging,
        // Only where this machine runs the catch (solo, or hosting a menu night: the host's own world, not its client's
        // copy); a joining client's world has no word of it (Lineside.Caught is the host's).
        DropCaught = session switch
        {
            PrototypeSession { World.Lineside: { } solo } => solo.Caught,
            NetPlaySession { Host.World.Lineside: { } hosted } => hosted.Caught,
            _ => null,
        },
        Stands = session.World.Switches,
    };
    double last = timer.Elapsed.TotalSeconds, titleAt = 0;
    // Talking and reading in the fortress town (note 281): on this machine alone. A press the town took isn't sent to the
    // host while the key's still down (nothing in a town changes the night; a lamp at somebody's feet stays where it is).
    var townTalk = new TownTalk();
    // Dave's words and his card (note 570): on this machine, as a town's are.
    var figureTalk = new FigureTalk();
    bool useKept = false;
    double pendingYaw = 0, pendingPitch = 0;
    int pendingNotch = 0;
    bool pendingReverser = false;
    var pendingLamp = LampSwitch.None;
    bool pendingCarLamp = false;
    byte pendingSelect = 0;
    bool pendingSeat = false;
    bool pendingBookmark = false;
    float pendingCycle = 0;
    double voiceLevel = 0;
    bool chase = ride;
    double sensitivity = 0.0025 * settings.MouseSpeed;
    // The player's keys (T80): each control's key, from the settings (a name the platform doesn't know: its default).
    var keyOf = Enum.GetValues<Control>().ToDictionary(c => c, c => Enum.TryParse<Key>(settings.KeyFor(c), out var k) ? k : Enum.Parse<Key>(Controls.Defaults[c]));
    Hud.Keys = settings;
    Hud.Tuning = DataFile.Load<HudTuning>(Path.Combine(content, HudTuning.File));
    // Note 350: one of this player's first nights shows the core controls in the yard.
    bool firstNight = Onboarding.FirstNight(onboarding, settings, nightsOver);
    // F4 (note 527): the card closed for the night, and back with F4 again.
    bool cardHidden = false;
    captions.Clear();
    // The canvas TEXT SIZE asks for from the night's first frame (note 351: a night from the command line drew its first on
    // the default canvas, there being no menu frame before it to take the setting).
    ApplyDisplay();
    // In a headset the ballot and the commendations are the stick's (note 202), and say so.
    Hud.Headset = vr is not null;
    var nightKeys = new VrMenuInput();
    NetPlaySession.PlayerName = settings.PlayerName;
    // The in-night menu (note 292) open this frame: the night goes on, but nothing pressed reaches it.
    bool inMenu = false;
    // HOLD KEYS on TOGGLE (note 383): run, the brake, talk, the radio and the roster latched by a press (once a frame, below).
    var latch = new HoldLatch { Toggles = settings.ToggleHolds };
    bool Held(Control c) => !inMenu && latch.Held(c, input.Down(keyOf[c]));
    bool Hit(Control c) => !inMenu && input.Pressed(keyOf[c]);
    bool Pressed(Key k) => !inMenu && input.Pressed(k);
    // The settings as this night last took them up: changed in the menu, they're taken up at once (note 292).
    var takenUp = settings;
    void TakeUp(Settings now)
    {
        settings = takenUp = now;
        sensitivity = 0.0025 * now.MouseSpeed;
        foreach (var c in Enum.GetValues<Control>())
            keyOf[c] = Enum.TryParse<Key>(now.KeyFor(c), out var k) ? k : Enum.Parse<Key>(Controls.Defaults[c]);
        Hud.Keys = now;
        showHud = now.Hud && !args.Contains("--no-hud");
        if (voice is not null)
        {
            voice.PushToTalk = now.PushToTalk || args.Contains("--push-to-talk");
            voice.MicLevel = (float)now.MicLevel;
        }
        // An outfit tried on (note 298): asked of the host, which takes it in the yard (and keeps it for the next Hello).
        byte outfit = now.OutfitByte(frontEnd.OutfitNames.Count);
        if (net is not null && outfit != NetPlaySession.Outfit)
        {
            NetPlaySession.Outfit = outfit;
            net.Wear(outfit);
        }
        ApplyDisplay();
    }
    // What the menu's LEAVE has to say about this night (note 292): whose it is, and who else is in it.
    NightMenu NightNow() => new(
        Hosting: net is null || net.Host is not null,
        Others: net?.Host is { } h ? Math.Max(0, h.Players.Count() - (net.BotCrew?.Bots.Count ?? 0) - 1) : 0,
        Campaign: campaign is not null,
        Invites: net?.Lobby is { Status: Ballast.Online.Lobby.State.Open } && session.Link is not { Full: true },
        JoinAt: session.Link?.JoinAt,
        Over: session.World.Run?.Over == true,
        Yard: net is { CanWear: true });
    // The emote wheel (note 298): held on its key, the mouse picks; let go, it's sent on the next tick.
    var wheel = new EmoteWheel();
    var pendingEmote = Emote.None;
    Camera camera = default;
    // T121: the derailment first-hand, then replayed from the chase view, then the orbit (DerailSequence).
    var derailSequence = new DerailSequence();
    // GDD v1.4 App. D.12: the night's bookmark stills, taken here as the host's bookmarks arrive.
    var stills = new BookmarkStills();
    // D.12, D.13 (note 203): and kept past the run-end screen, in the user's app data, a folder for the night.
    var album = new BookmarkAlbum(BookmarkAlbum.DefaultDirectory, DateTime.Now, session.Route?.Name ?? "night");
    bool albumSaid = false;
    FrameLighting lighting = default;
    window.MouseCaptured = true;
    window.TextInput = false;
    int frames0 = 0;
    double swingFrom = -1; // when your swing in view began (X3), or -1

    // (At least one frame, whatever --quit-after says: a slow load can outlast it, and --capture draws the last frame.)
    while (!window.CloseRequested && (frames0++ == 0 || !QuitNow()))
    {
        window.PumpEvents();
        double now = timer.Elapsed.TotalSeconds;
        double dt = now - last;
        last = now;

        // Escape opens the in-night menu (note 292), and the menu's own keys and the mouse work it, as the front end's do:
        // before, a second Escape left the night at once, a host's for the whole crew. In a headset the window's mirror
        // keeps the old way (VR's on the backburner): Escape frees the mouse, and again leaves.
        inMenu = frontEnd.Night is not null;
        if (vr is not null)
        {
            if (input.Pressed(Key.Escape))
            {
                if (window.MouseCaptured) window.MouseCaptured = false;
                else break;
            }
        }
        // --night-menu (headless checks with --capture): the in-night menu open from the first frame.
        else if (!inMenu && (input.Pressed(Key.Escape) || frames0 == 1 && args.Contains("--night-menu")))
        {
            frontEnd.OpenNight(NightNow());
            window.MouseCaptured = false;
            window.CaptureOnClick = false;
            inMenu = true;
        }
        else if (inMenu)
        {
            // What leaving costs, and who's here, as of now (the report up, someone joined, the gate passed).
            frontEnd.RefreshNight(NightNow());
            Launch? chosen = null;
            window.TextInput = frontEnd.WantsText;
            if (frontEnd.Capturing is not null)
            {
                // Binding a control, as on the front end's CONTROLS: the next key or button, Escape keeps the old.
                window.MouseCaptured = true;
                if (input.Pressed(Key.Escape)) frontEnd.Back();
                else if (input.AnyPressed is { } bound) frontEnd.Bind(bound.ToString());
                if (frontEnd.Capturing is null)
                    window.MouseCaptured = false;
            }
            else
            {
                var keys = MenuInput.Keys(name => Enum.TryParse<Key>(name, out var k) && input.Pressed(k), frontEnd.WantsText);
                var mouse = new MenuMouse(OverlayMouse(), input.MouseMoved,
                    input.Pressed(Key.MouseLeft), input.Pressed(Key.MouseRight), input.Wheel);
                chosen = MenuInput.Apply(frontEnd, keys, frontEnd.WantsText ? input.Text : "", mouse);
            }
            if (!frontEnd.Settings.Equals(takenUp))
                TakeUp(frontEnd.Settings);
            if (chosen is Launch.Invite)
                net?.ShowInviteDialog();
            if (chosen is Launch.Leave)
            {
                frontEnd.CloseNight();
                window.TextInput = false;
                if (net is not null && session.World.Run?.Over == true && session.World.Commendations.Count > 0)
                    profile.Record(session.World.Commendations, net.PlayerId);
                break;
            }
            // RESUME, or Escape on the menu's first page: back in the night, the mouse its again.
            if (frontEnd.Night is null)
            {
                window.TextInput = false;
                window.MouseCaptured = true;
                window.CaptureOnClick = true;
            }
        }
        // The night's over: Enter goes back (to the fortress, for a campaign night).
        if (session.World.Run?.Over == true && Pressed(Key.Enter))
        {
            // D.12: what the crew commended you for goes in your profile, whatever becomes of the character.
            if (net is not null && session.World.Commendations.Count > 0)
                profile.Record(session.World.Commendations, net.PlayerId);
            break;
        }
        // In a headset (note 202): the left stick's pushes and its click, once each, for the ballot and the commendations.
        latch.Toggles = frontEnd.Settings.ToggleHolds;
        foreach (var c in Settings.Toggleable)
            if (Hit(c))
                latch.Press(c);
        var vrPress = vr is null ? VrMenuPress.None : nightKeys.Read(vr.Session.Controllers);
        // GDD v1.4 App. D.12: on the run-end screen, a commendation for a crewmate: the arrows pick who and which, Space gives
        // it; in a headset the stick picks and its click gives.
        if (session.World.Run?.Over == true && net is not null)
            net.Commend((Pressed(Key.Right) || vrPress.HasFlag(VrMenuPress.Right) ? 1 : 0) - (Pressed(Key.Left) || vrPress.HasFlag(VrMenuPress.Left) ? 1 : 0),
                (Pressed(Key.Down) || vrPress.HasFlag(VrMenuPress.Down) ? 1 : 0) - (Pressed(Key.Up) || vrPress.HasFlag(VrMenuPress.Up) ? 1 : 0),
                Pressed(Key.Space) || vrPress.HasFlag(VrMenuPress.Click));
        // D.11 (note 202): dead with a ballot to cast, a number key picks a creature and the same again (or Enter) casts it;
        // in a headset the stick's up and down pick (its left and right still change whom you watch) and its click casts.
        if (net is { Voting: true, Ballot: { } ballot })
        {
            int options = ballot.Options.Count;
            for (var k = Key.D1; k < Key.D1 + options; k++)
                if (Pressed(k))
                    net.Picker.Key(k - Key.D1 + 1, options);
            if (Pressed(Key.Enter))
                net.Picker.Cast();
            net.Picker.Headset(vrPress, options);
        }
        // D.11: the dead's chime as a creature they voted for comes.
        if (net?.TakeNewCue() == true)
            sound.Play("vote-cue");
        // --derail-at s (a host, headless checks of the derailment's beats with --capture): off the rails at s seconds.
        if (derailAt > 0 && now >= derailAt && session is NetPlaySession { Host.World: { Derailed: false } hostWorld })
            hostWorld.Derail("--derail-at");
        // The prototype drives from anywhere; networked, cab controls go through intent like everything else.
        sbyte notch = (sbyte)((Hit(Control.RegulatorOpen) ? 1 : 0) - (Hit(Control.RegulatorClose) ? 1 : 0));
        bool reverser = Hit(Control.Reverser);
        // The lamp switch (T52): a setting, the opposite of how the lamp is now, held until a tick sends it.
        if (Hit(Control.Lamp))
            pendingLamp = session.World.LampLit ? LampSwitch.Off : LampSwitch.On;
        pendingCarLamp |= Hit(Control.CarLamp);
        // Dead (App. D.10), a bookmark of whom you're watching (D.12): sent on the press, as intent.
        pendingBookmark |= Hit(Control.Bookmark) && !session.Player.Alive;
        // A word in town is this machine's alone, so the press is kept from the host; but by Nicki, held, it's a glass of her
        // wine (note 551), which is the host's to pour.
        if (Hit(Control.Use) && session.World.Town is { } town && Hud.TownTarget(session) is var spoken && townTalk.Use(town, spoken, now))
            useKept = spoken is not { Kind: DarkTerritory.Sim.Towns.TownTargetKind.Person } hosting || !town.Plan.People[hosting.Index].Hosting;
        else if (Hit(Control.Use) && Hud.Prompt(session) is { } atDave && FigureTalk.Target(session) is { } dave && atDave == FigureTalk.Prompt(dave)
            && figureTalk.Use(dave, now))
            useKept = true;
        if (!Held(Control.Use))
            useKept = false;
        // The gun's seat (T112): Use pressed standing still at a loaded gun sits you in it (Use held at one waiting on its
        // reload loads it, walking with it pushes it). Seated, Jump gets you up.
        if (Hit(Control.Use) && !session.Player.Has(PlayerFlags.Seated) && session.World.Combat is { } gc
            && DarkTerritory.Sim.Combat.Guns.MannedGun(session.Player, session.Train, gc.Guns) is { } atGun && session.Train.Vehicles[atGun].Gun.ReloadNeeded <= 0
            && !Held(Control.Forward) && !Held(Control.Back) && !Held(Control.Left) && !Held(Control.Right))
            pendingSeat = true;
        // The crane's controls (T48): Use pressed at its stand takes them, and pressed again lets them go (the director, 8 Oct:
        // "enter with E and exit with E, it shouldn't be a hold function"), the same press as the gun's seat (Crane.Operates).
        if (Hit(Control.Use) && (session.Player.Has(PlayerFlags.Operating)
            || session.World.Run?.CurrentSite?.Cranes.Any(c => c.AtStand(session.Player, session.Train)) == true))
            pendingSeat = true;
        // The hotbar (T108): a number key picks its slot, the wheel steps through the tools (not while they're the ballot's).
        for (var k = Key.D1; k < Key.D1 + Kit.Slots; k++)
            if (Pressed(k) && net is not { Voting: true })
                pendingSelect = (byte)(k - Key.D1 + 1);
        if (window.MouseCaptured && !inMenu)
            pendingCycle += input.Wheel;
        // Held until a tick sends them: at a high frame rate a key press can land on a frame with no tick.
        pendingNotch += notch;
        pendingReverser |= reverser;
        if (proto is not null)
        {
            if (notch != 0) proto.Notch(notch);
            if (reverser) proto.FlipReverser();
            if (Pressed(Key.Backspace)) proto.Respawn(0);
            proto.BrakeHeld(Held(Control.Brake));
            if (ride && session.Route?.Plan is { } ridden)
                DarkTerritory.Game.LineGen.Ride.Drive(proto.Train, ridden, ref proto.Controls);
        }
        if (Hit(Control.Chase)) chase = !chase;
        if (Pressed(Key.F1)) showHud = !showHud;
        if (Pressed(Key.F4)) cardHidden = !cardHidden;
        if (Pressed(Key.F2)) net?.ShowInviteDialog();
        // RECONNECT (note 253): a joiner whose link went, out of automatic tries, tries again.
        if (Pressed(Key.F5)) net?.Reconnect();
        // A generated line's route card (C: the paper the crew is handed) and the designer's overlay (F3).
        if (Hit(Control.RouteCard))
        {
            cardPage = cardPage + 1 >= cardPages ? -1 : cardPage + 1;
            // The panels heard (note 322): the card drawn out, paged, put away.
            sound.Ui(cardPage < 0 ? UiCue.PanelClose : cardPage == 0 ? UiCue.PanelOpen : UiCue.PanelPage);
        }
        if (Hit(Control.Supplies))
        {
            showSupplies = !showSupplies;
            sound.Ui(showSupplies ? UiCue.PanelOpen : UiCue.PanelClose);
        }
        // Q held is the roster: out as it's pressed, away as it's let go.
        if (Held(Control.Roster) != rosterOut)
        {
            rosterOut = !rosterOut;
            sound.Ui(rosterOut ? UiCue.PanelOpen : UiCue.PanelClose);
        }
        if (Pressed(Key.F3)) showPlan = !showPlan;
        // An invite accepted (or "Join Game" on a friend) while playing: leave this game for theirs.
        if (Invited(net) is { } invitedTo)
        {
            relaunch = invitedTo;
            break;
        }

        // Held, the emote wheel takes the mouse (note 298); let go, what it was on goes with the next tick.
        if (wheel.Update(Held(Control.Emote) && session.Player.Alive, input.MouseDX, input.MouseDY) is var let && let != Emote.None)
            pendingEmote = let;
        if (!inMenu && !wheel.Open)
        {
            pendingYaw -= input.MouseDX * sensitivity;
            pendingPitch -= input.MouseDY * sensitivity * (settings.InvertMouse ? -1 : 1);
        }
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
            if (Held(Control.Use) && !useKept) buttons |= PlayerButtons.Use;
            if (Held(Control.Fire)) buttons |= PlayerButtons.Fire;
            if (Held(Control.Throw)) buttons |= PlayerButtons.Throw;
            // The brake key is the crane's hook down as well (Crane.Drive), so it's in the intent alone too: there the
            // session's cab also takes it straight off the key (BrakeHeld), and the cab's own brake is the same either way.
            if (Held(Control.Brake)) buttons |= PlayerButtons.Brake;
            if (proto is null && pendingReverser) buttons |= PlayerButtons.Reverser;
            var intent = new PlayerIntent
            {
                MoveX = (Held(Control.Right) ? 1 : 0) - (Held(Control.Left) ? 1 : 0),
                MoveZ = (Held(Control.Forward) ? 1 : 0) - (Held(Control.Back) ? 1 : 0),
                LookYaw = (float)pendingYaw,
                LookPitch = (float)pendingPitch,
                Buttons = buttons,
                ThrottleNotch = proto is null ? (sbyte)Math.Clamp(pendingNotch, -4, 4) : (sbyte)0,
                Lamp = pendingLamp,
                // T108: the left button swings what's in hand too (the host ignores a swing from someone at a gun, whose
                // left button fires it).
                Actions = (Held(Control.Swing) || Held(Control.Fire) ? PlayerActions.Swing : 0) | (Held(Control.Whistle) ? PlayerActions.Whistle : 0)
                    // Note 267: the vent's key shares Uncouple's bit (a coupler plate is never in the cab).
                    | (Held(Control.Uncouple) || Held(Control.Vent) ? PlayerActions.Uncouple : 0) | (Held(Control.Ladder) ? PlayerActions.Ladder : 0)
                    | (pendingCarLamp ? PlayerActions.CarLamp : 0) | (pendingSeat ? PlayerActions.Seat : 0)
                    // D.12: the dead's bookmark, while the run's under way (the same bit is the film's skip vote once it's over).
                    | (pendingBookmark && session.World.Run is not { Over: true } ? PlayerActions.Bookmark : 0)
                    // E.5, E.9: holding Jump skips the film to its cause card (or the Stranded outro), once a skip counts: this
                    // player's own, held for wreck.json's skip.holdSeconds (note 315), or a vote.
                    | (session.Skippable && Held(Control.Jump) ? PlayerActions.Skip : 0),
                // How loud you are (GDD v1.1 App. C.7, C.8): the mic while it sends; with no mic, holding Talk counts as
                // speaking up, so a player without one can still talk the Gaunt down and answer a roll call.
                Voice = (byte)Math.Clamp(voiceLevel * 255, 0, 255),
                Select = pendingSelect,
                // Scrolled up is the previous slot, down the next, as in most games.
                Cycle = (sbyte)(pendingCycle >= 1 ? -1 : pendingCycle <= -1 ? 1 : 0),
                Emote = pendingEmote,
            };
            pendingSelect = 0;
            pendingEmote = Emote.None;
            if (Math.Abs(pendingCycle) >= 1)
                pendingCycle -= Math.Sign(pendingCycle);
            if (locomotion is not null)
            {
                locomotion.Follow(session.Viewpoint, Eyes.Heading(session.Viewpoint, session.Train.Frames));
                var headset = locomotion.Intent(session.Player, vr!.Session.Controllers, session.PlayerTuning.LadderClimb);
                intent.MoveX = Math.Clamp(intent.MoveX + headset.MoveX, -1, 1);
                intent.MoveZ = Math.Clamp(intent.MoveZ + headset.MoveZ, -1, 1);
                intent.LookYaw = headset.LookYaw;
                intent.LookPitch = headset.LookPitch;
                intent.Buttons |= headset.Buttons;
                (intent.HandX, intent.HandY, intent.HandZ) = (headset.HandX, headset.HandY, headset.HandZ);
                // The other hand (T43) and the head's height (T82) go with it: the host's two-handed grips and the body the
                // crew see under the head need them.
                (intent.Other, intent.OtherX, intent.OtherY, intent.OtherZ, intent.Head) = (headset.Other, headset.OtherX, headset.OtherY, headset.OtherZ, headset.Head);
                // The cab's levers by hand (T29): the same notches, brake and reverser a keyboard sends.
                levers!.Apply(ref intent, session.Player, session.Train, session.Controls, session.PlayerTuning.Hand);
            }
            pendingNotch = 0;
            pendingLamp = LampSwitch.None;
            pendingCarLamp = false;
            pendingSeat = false;
            pendingBookmark = false;
            pendingReverser = false;
            pendingYaw = pendingPitch = 0;
            session.Step(intent);
            if (campaign is not null && session is NetPlaySession played)
                campaign = Autosave(saves, campaign, played);
            // The ears are where the eyes were last frame; audio follows the sim tick so no shot is missed.
            // Watching a crewmate (App. D.10), you hear what they hear: their shelter, their space.
            var ears = session.Viewpoint;
            bool exposed = !PlayerMotor.Indoors(ears, session.Train);
            // Who's aboard to be heard (their feet, their hands, who was bitten), and your own intent (your swing, your
            // trigger: not replicated).
            sound.CrewStates = GameAudio.CrewOf(session);
            sound.OwnId = session.PlayerId;
            sound.OwnIntent = intent;
            // The crew's clips' clock, the scene's: the breach's blows land as the lock jumps (note 497).
            sound.SceneClock = now;
            sound.Update(session.World, session.Controls, Listener.At(camera.Position, camera.Yaw), exposed, SimConstants.TickSeconds,
                PlayerMotor.Space(ears, session.Train));
            // Your own interface sounds (your hold, the night's end, the queue while dead), whoever you're watching.
            sound.Interface(session.World, session.Player, session.PlayerId);
            sound.Choices(session);
            if (voice is not null && net is not null)
                voice.Update(net.Client, session.Crew(session.InterpolatedFrames(1), 1), SimConstants.TickSeconds);
        }
        if (voice is not null && net is not null)
        {
            voice.TalkHeld = Held(Control.Talk);
            // Only with a radio on you (T41); the host checks too.
            voice.RadioHeld = Held(Control.Radio) && session.World.Bodies.HasRadio(session.PlayerId);
            // Held by something (App. C.8): the mic keyed open for the whole GRAB, onto the radio if you have one.
            voice.Grabbed = session.Player.Alive && session.Player.Has(PlayerFlags.Held);
            double loud = 0;
            for (int n; mic is not null && (n = mic.Read(micSamples)) > 0;)
            {
                voice.Capture(micSamples.AsSpan(0, n), net.Client);
                double sum = 0;
                for (int k = 0; k < n; k++)
                    sum += micSamples[k] * micSamples[k];
                loud = Math.Max(loud, Math.Sqrt(sum / n));
            }
            // Speech RMS sits around 0.05-0.2; a shout nearer 0.3 and up.
            // T113 playtest (the Choir "WAY TOO OFTEN", gone when the train slowed): alone, an open mic hears the game's own
            // train through the speakers, so it's only Talk held that makes you loud; with someone to hear you, your voice.
            bool speaking = voice.Transmitting && (!(net?.Alone ?? false) || Held(Control.Talk));
            voiceLevel = mic is null ? (Held(Control.Talk) ? 0.5 : 0) : speaking ? Math.Clamp(loud * 3.5, 0, 1) : 0;
        }
        else
            voiceLevel = Held(Control.Talk) ? 0.5 : 0;
        FeedSpeaker();

        var frames = session.InterpolatedFrames(clock.Alpha);
        // Off the rails (T117, T121): first in your own eyes riding it, then the chase view replaying it from a few seconds
        // before, then the camera circling the wreck. What was drawn is kept for the replay (DerailSequence).
        bool outro = session.StrandedOutro;
        // The sequence's timing is this player's: their own first person, up to their own death (App. E.2 step 1).
        var wreckTuning = session.SequenceTuning;
        bool wrecking = session.WreckCinematic && session.Train.Wreck is not null;
        var film = session.Film;
        // GDD v1.4 App. E.6: the opera, from the replay's first frame, its hit on the moment the replay shows it coming off,
        // faded under the film's cause card.
        // Its hit on the final player's apex in the film (E.5-E.6; note 245), or the replay's derail moment with no film.
        sound.Music(session.World.DerailMusic, wrecking ? session.WreckSeconds : -1, wreckTuning,
            film is null ? -1 : wreckTuning.FirstPersonSeconds + wreckTuning.ReplaySeconds + film.CauseAt,
            film?.FinalApexAt is { } apex ? wreckTuning.FirstPersonSeconds + wreckTuning.ReplaySeconds + apex : -1);
        // GDD §9: the dispatcher's manifest leaving the yard and the clerk's tally home, on the radio, said a line at a time
        // as each comes on (note 240).
        var reading = session.RadioReading;
        sound.Radio(reading, reading is null ? 0
            : DarkTerritory.Sim.Run.Radio.Reading(reading, session.RadioSeconds, session.World.Run?.Tuning.Radio ?? new(), session.RadioTimes).Lines);
        // E.9: the Stranded outro's cooling boiler and its lamps going out, in time with the picture.
        sound.Stranded(session.Train, wreckTuning.Stranded, outro ? session.OutroSeconds : -1);
        // E.5, E.9: the clerk's one line, said as it comes up (note 242): the film's cause card, the Stranded report over the
        // pull-back. It runs on past the card into the end screen if it's longer.
        string? clerkLine = null;
        var beat = wrecking ? DerailSequence.Beat(wreckTuning, session.WreckSeconds, film) : DerailBeat.None;
        if (beat == DerailBeat.Film && film?.CutAt(DerailSequence.FilmSeconds(wreckTuning, session.WreckSeconds)) is { } cut
            && cut.Shot.Kind == DarkTerritory.Sim.Train.ShotKind.Cause)
            clerkLine = cut.Shot.Card;
        else if (outro && session.OutroSeconds > wreckTuning.Stranded.RackSeconds)
            clerkLine = DarkTerritory.Sim.Run.Radio.Stranded(session.World.Run?.Report?.DistanceKm ?? 0);
        sound.ClerkLine(clerkLine);
        derailSequence.Record((session.Tick + clock.Alpha) * DarkTerritory.Sim.SimConstants.TickSeconds, frames, scene.Crew, session.World.Derailed, camera,
            session.Player.Parent >= 0 ? session.Player.Parent : -1, wreckTuning);
        // The beat, the cars as drawn (the replay's, the film's) and its camera: the same pick `dt film` renders (note 251).
        var derailShot = derailSequence.Show(session, frames, ownEyes: vr is null);
        frames = derailShot.Frames;
        // The film's own wreck heard, not the live one (note 251).
        sound.Film(derailShot.Film, derailShot.Filming);
        bool cinematic = wrecking || outro;
        var outroTuning = wreckTuning.Stranded;
        camera = outro ? Views.Stranded(session.Train, outroTuning, session.OutroSeconds)
            : derailShot.Camera is { } sequenceCamera ? sequenceCamera
            : chase ? Views.Get("chase", session.Train) : session.EyeCamera(frames, clock.Alpha, pendingYaw, pendingPitch) with { FovYDegrees = settings.EyeFov };
        // E.9: the lamps go out down the train as the camera pulls back, and stay lit (or not) as far as it can see.
        // E.9: the outro opens on the repair kit's locker standing open and empty (note 173).
        scene.KitLockerOpen = outro;
        scene.LampsOut = outro || session.World.Run?.End == DarkTerritory.Sim.Run.RunEnd.Stranded ? Views.StrandedLampsOut(session.Train.Frames.Count, outroTuning, session.OutroSeconds) : 0;
        scene.LampRange = outro ? 400 : 60;
        scene.RoofGlow = outro;
        // On the engine with the boiler in the red, it shakes you (T109).
        if (!chase && !cinematic)
        {
            // The settings' CAMERA SHAKE (note 297) scales both, down to none.
            camera.Position += BoilerShake.Offset(session.World, session.Viewpoint, timer.Elapsed.TotalSeconds) * settings.CameraShake;
            // On a car straining round a bend too fast, it judders you (the overspeed telegraph, App. F.1).
            if (scene.BendStrain is { } judder && session.Viewpoint.Parent is var on and >= 0 && on < judder.Count)
                camera.Position += BendStrain.Offset(judder[on].Stress, timer.Elapsed.TotalSeconds) * settings.CameraShake;
        }
        // E.5's film draws the crew as ragdolls, its cutaway and light rig; the replay, the crew as they were (DerailSequence.Dress).
        DerailSequence.Dress(scene, derailShot, session, camera.Position, derailShot.Replay is null && derailShot.Filming is null ? session.Crew(frames, clock.Alpha) : []);
        // Behind a crewmate's eyes (App. D.10), their own figure isn't drawn round the camera.
        if (session.Watching >= 0 && !chase)
            scene.Crew = [.. (scene.Crew ?? []).Where(c => c.Id != session.Watching)];
        // Just come back inside a Holdout, you're seen getting up while the camera's on you (note 529).
        if (session.CameBackFigure(frames, clock.Alpha) is { } risen && !chase && !cinematic)
            scene.Crew = [.. scene.Crew ?? [], risen];
        // What you carry is drawn at your hands as you see them this frame, not where the last tick left it (T92).
        var carry = session.World.Bodies.Hands;
        var eyeForward = new Double3(-Math.Sin(camera.Yaw), 0, -Math.Cos(camera.Yaw));
        // Off the rails the living ride the wreck to their death (App. E.2 step 1): no hands, nothing carried, in the sequence.
        scene.HeldHere = chase || cinematic || !session.Player.Alive ? null
            : (session.PlayerId, camera.Position - Double3.Up * (Eyes.Height - carry.CarryHeight) + eyeForward * carry.CarryForward, camera.Yaw);
        // Your own arms in view (X3), and the swing you've started: a blow lasts the melee's recovery, and held, they follow
        // one another (World.Swing's cadence). A headset draws its own hands; behind a crewmate's eyes, theirs aren't yours.
        var me = session.Player;
        var act = CrewActs.Of(me, session.PlayerId, session.World);
        bool swinging = act is null && (Held(Control.Swing) || Held(Control.Fire));
        double swingSeconds = session.World.Enemies?.Melee.SwingSeconds ?? 0.8;
        if (swinging && (swingFrom < 0 || now - swingFrom >= swingSeconds))
            swingFrom = now;
        double swing = swingFrom >= 0 && now - swingFrom < swingSeconds ? now - swingFrom : -1;
        if (swing < 0)
            swingFrom = -1;
        scene.Own = chase || cinematic || vr is not null || !me.Alive || session.Watching >= 0 ? null
            : new OwnView((float)camera.Yaw, (float)camera.Pitch, act, me.Velocity.X * me.Velocity.X + me.Velocity.Z * me.Velocity.Z > 0.16,
                swing, session.World.OutfitOf(session.PlayerId), Kit.Held(me));
        scene.Time = now;
        // Dave's card (note 570): closes as you walk off, and opens on what he says to a blow near you.
        figureTalk.Step(session.World, PlayerMotor.WorldPosition(me, session.Train) + Double3.Up * session.Train.Dynamics.Tuning.Pick.EyeHeight, now);
        // The town's card closes once you've walked off; whoever you're talking to turns to you.
        if (session.World.Town is { } here)
        {
            var eye = PlayerMotor.WorldPosition(me, session.Train) + Double3.Up * session.Train.Dynamics.Tuning.Pick.EyeHeight;
            townTalk.Step(here, eye, now);
            scene.TownFacing = townTalk.Open is { Kind: DarkTerritory.Sim.Towns.TownTargetKind.Person } talking ? (talking.Index, eye) : null;
        }
        lighting = Views.Lighting(frames[0], look, session.World.Run is { } dawnRun && look is not null ? look.DawnOf(dawnRun.DawnIn) : 0);
        lighting.Time = now;
        // Lamps down (T52), or smashed: no beam.
        if (!session.World.LampShining)
            lighting.LampIntensity = 0;
        if (session.Route is { } r)
        {
            lighting.FogDensity = Views.FogDensity(r, session.Train);
            lighting.Wetness = r.Weather.Wet ? 1 : 0;
            lighting.Frost = look?.Tuning.Atmosphere.Cold.Frost(r.Weather.Cold) ?? 0;
            if (look?.Tuning.Atmosphere.Wind is { } wind)
                (lighting.Wind, lighting.Gusts) = (wind.Of(r.Weather.Wind), wind.Gusts);
        }
        // The Choir's cold, coming before it (App. A.7): the frame chills as it gathers (Look.Chill, over the weather's frost).
        if (look is not null)
            lighting = look.Chill(lighting, GreyboxScene.ChoirCold(session.World.Choir.Present ? 1 : (float)session.World.Choir.Build));
        if (session.StrandedOutro)
            Views.CinematicFog(ref lighting, Views.StrandedDistance(session.Train, session.World.WreckTuning.Stranded, session.OutroSeconds));
        DerailSequence.Fog(ref lighting, derailShot);
        scene.FireGlow = session.Train.BoilerTuning is { } bt ? GreyboxScene.FireLook(session.Train.Boiler.Firebox, bt.FireboxCapacity) : 0.7f;
        scene.WrenchRacked = !session.Train.Boiler.WrenchOut;
        scene.ShovelRacked = !session.Train.Boiler.ShovelOut;
        scene.CordPulled = DarkTerritory.Game.Art.CrewActs.CrewWhistling(session.World);
        scene.Cut = DarkTerritory.Game.Art.SceneArt.Cuts(session.Train);
        scene.FireDoorOpen = session.Train.Boiler.FireDoorOpen;
        scene.SinceShovel = session.Train.Boiler.SinceShovel;
        scene.ChoirGathering = session.World.Choir.Present ? 1 : (float)session.World.Choir.Build;
        // The dark answering a draw (note 287): eyes at the lamp's edge.
        scene.Answer = session.World.Answer;
        scene.AnswerShowSeconds = session.World.Director?.Tuning.Draw.ShowSeconds ?? 7;
        // Watched afoot (note 327): eyes toward what lives at the stop.
        scene.Watcher = session.World.Watcher;
        scene.WatcherShowSeconds = session.World.Director?.Tuning.Afoot.SignSeconds ?? 3.5;
        // How long the Stoker's been waiting on the stack, as seen here (presentation only: it's put in by the host's own clock).
        stokerSince = session.World.StokerWaiting ? stokerSince < 0 ? scene.Time : stokerSince : -1;
        scene.StokerLowFor = stokerSince < 0 ? -1 : scene.Time - stokerSince;
        scene.StokerDownAt = session.World.Enemies?.Stoker.HeatSeconds ?? 20;
        scene.Tick = session.HostTick;
        scene.Pressure = (float)(session.Train.BoilerTuning is { } pt ? session.Train.Boiler.Pressure / pt.PressureMax : 0.78);
        scene.Tender = (float)(session.Train.BoilerTuning is { TenderCapacity: > 0 } tt ? Math.Clamp(session.Train.Boiler.Tender / tt.TenderCapacity, 0, 1) : 0.72);
        scene.LampLit = session.World.LampShining && scene.LampsOut < session.Train.Frames.Count;
        scene.Venting = session.Train.Boiler.Vented;
        scene.SafetyValve = session.Train.Boiler.SafetyValveLifting;
        scene.Ruptured = session.Train.Boiler.Ruptured;
        // Every break the crew can mend, called out where it is, and the ones a wrench is at (note 301).
        // And the loose couplings, called out alike (note 356).
        var breaks = DarkTerritory.Sim.Train.RepairCallouts.Of(session.Train);
        var mending = breaks.Count > 0 ? GreyboxScene.MendingAt(breaks, session.CrewStates(1).Select(c => c.State), session.Train) : null;
        var tightening = new HashSet<int>();
        DarkTerritory.Sim.Train.Couplings.Callouts(session.Train, breaks, session.CrewStates(1).Select(c => c.State), tightening);
        scene.Breaks = breaks;
        scene.Mending = tightening.Count > 0 ? [.. mending ?? [], .. tightening] : mending;
        scene.BendStrain = session.Route?.Plan is { } strainPlan ? BendStrain.PerCar(session.Train, strainPlan.Rules) : null;
        scene.DriversLocked = scene.Ruptured && session.Train.BoilerTuning is { } rt && session.Train.Dynamics.Speed > rt.RuptureCoastBelow;
        scene.Controls = session.Controls;
        if (!session.World.LampShining)
            lighting.LampRange = 0.01f; // not 0: the shader divides by it
        scene.Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
        // GDD v1.4 App. D.12: a bookmark that's due is drawn from its camera now, off screen, with what this machine has of
        // the world (everyone but whoever's eyes it is), and kept small for the run-end screen. Rare, so a stall's fine.
        if (stills.Due(session, frames, now) is { Count: > 0 } due)
        {
            var (shownCrew, shownOwn, shownHeld) = (scene.Crew, scene.Own, scene.HeldHere);
            var (shownBodies, shownCut, shownLights, shownWreck, shownDerailed) = (scene.Bodies, scene.CutAway, scene.Lights, scene.Wreck, scene.Derailed);
            (scene.Own, scene.HeldHere) = (null, null);
            foreach (var (mark, from, peak) in due)
            {
                // E.5: a derailment's still is that crew member's peak in the film, drawn as the film draws it.
                var at = peak is not null ? DerailSequence.Stage(scene, peak, frames) : frames;
                if (peak is null)
                    scene.Crew = BookmarkStills.Figures(session, frames, clock.Alpha, mark.Viewer);
                scene.Build(mesh, session.Train.Line, at, session.Train.Dynamics.Distance, from.Position);
                stills.Keep(mark, renderer.Render(mesh, from, lighting, lighting.FogColor), renderer.Width, renderer.Height, peak is not null);
                (scene.Bodies, scene.CutAway, scene.Lights, scene.Wreck, scene.Derailed) = (shownBodies, shownCut, shownLights, shownWreck, shownDerailed);
            }
            (scene.Crew, scene.Own, scene.HeldHere) = (shownCrew, shownOwn, shownHeld);
            scene.Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
        }
        // D.12, D.13 (note 203): once the report's in, the stills on it are kept on disk, so a player has them after the screen.
        if (session.World.Run?.Report is { } kept && album.Save(kept, stills.Stills, session.World).Count > 0 && !albumSaid)
        {
            albumSaid = true;
            Console.WriteLine($"bookmarks: the night's stills are kept in {album.Night}");
        }
        // Note 349: what the ear heard in the last block, at the gain it was heard at.
        if (frontEnd.Settings.Captions)
            captions.Update(sound.Mixer.Voices.Where(v => !v.Finished && !v.Virtual).Select(v => new Heard(v.Name, v.Position, v.AudibleGain)),
                sound.Mixer.Listener, now);
        else
            captions.Clear();
        if (showHud)
        {
            Hud.Build(overlay, UiWidth, UiHeight, session, stills: stills.Stills, pixels: (float)renderer.Height / UiHeight, talk: townTalk, figures: figureTalk, now: now,
                // The first nights' card gives way to a panel opened over it (note 351: it showed through the supplies).
                firstNight: firstNight && !cardHidden && !Held(Control.Roster) && !showSupplies && cardPage < 0 && !showPlan,
                captions: frontEnd.Settings.Captions ? captions.Lines() : null);
            wheel.Draw(overlay, UiWidth, UiHeight, Hud.PromptScaleAt((float)renderer.Height / UiHeight));
            // Q held: the crew roster (T69), with who's been heard.
            if (Held(Control.Roster))
                Hud.Roster(overlay, UiWidth, UiHeight, session.Roster(), voice is null ? null : voice.SinceHeard);
            else if (showSupplies && session.World.Run?.Over != true)
                Hud.Supplies(overlay, UiWidth, UiHeight, session);
            if (session.Route?.Plan is { } shown)
            {
                if (cardPage >= 0)
                    cardPages = DarkTerritory.Game.LineGen.PlanHud.RouteCard(overlay, UiWidth, UiHeight, shown, cardPage, session.Train.Line,
                        session.Train.Line.MainDistance(session.Train.Dynamics.Path, session.Train.Dynamics.Distance));
                if (showPlan)
                    DarkTerritory.Game.LineGen.PlanHud.Overlay(overlay, UiWidth, UiHeight, session, shown);
            }
            if (session.World.Run?.Over == true)
            {
                overlay.TextCentred(UiWidth / 2f, UiHeight - 22, campaign is not null ? "ENTER: BACK TO THE FORTRESS" : "ENTER: BACK", new Vector4(1, 0.7f, 0.3f, 1));
                if (album.Kept > 0)
                    overlay.TextCentred(UiWidth / 2f, UiHeight - 12, $"{album.Kept} STILLS KEPT IN YOUR BOOKMARKS FOLDER", new Vector4(0.6f, 0.6f, 0.6f, 1));
            }
        }
        // The in-night menu (note 292) in place of the HUD: the night dimmed behind its list, nothing else over it.
        bool menuShown = frontEnd.Night is not null && vr is null;
        if (menuShown)
        {
            overlay.Clear();
            frontEnd.Draw(overlay, UiWidth, UiHeight);
        }
#if DEVTOOLS
        dev.Draw(overlay, UiWidth, UiHeight);
#endif
        if (vr is null)
        {
            renderer.Prepare(mesh, showHud || menuShown ? overlay : null);
            Present(camera, lighting);
        }
        // The body is the flat camera's eye point, turned to where the room faces; the head does the looking.
        VrPanelContent? onPanel = null;
        if (vr is not null && showHud)
        {
            Hud.Build(vrOverlay, 480, 270, session, crosshair: false, stills: stills.Stills, pixels: 2, talk: townTalk, figures: figureTalk, now: now);
            if (session.World.Run?.Over == true)
                vrOverlay.TextCentred(240, 248, campaign is not null ? "A: BACK TO THE FORTRESS" : "A: BACK", new Vector4(1, 0.7f, 0.3f, 1));
            onPanel = new VrPanelContent(vrHud!, vrOverlay, 480, 270);
        }
        // Your own gloves on your controllers, the crew's sleeves and gloves (X3, roadmap M4), the tool in hand in the right.
        if (vr is not null)
        {
            var heldTool = Kit.Held(me);
            int myId = session.PlayerId;
            bool alive = me.Alive && session.Watching < 0;
            vr.Hands = look is null || !alive ? null : (into, body, controllers) => look.Art.HeadsetHands(into, (float)body.Yaw, controllers, myId, heldTool);
        }
        if (vr is not null && vr.Frame(mesh, locomotion!.Body(camera, Eyes.Heading(session.Viewpoint, frames)), lighting, lighting.FogColor, locomotion, onPanel) == XrFrameResult.Exiting)
            break;
        if (vr is not null)
            Mirror();
        // The night's over: A goes back, as Enter does, keeping what you were commended for (D.12) as Enter does.
        if (vr is not null && session.World.Run?.Over == true && vr.Session.Controllers.Primary)
        {
            if (net is not null && session.World.Commendations.Count > 0)
                profile.Record(session.World.Commendations, net.PlayerId);
            break;
        }

        if (now >= titleAt)
        {
            string talking = voice is { Transmitting: true } ? voice.OnRadio ? " | ON THE RADIO" : " | talking" : "";
            window.Title = $"Dark Territory — {session.Status()}{talking}";
            titleAt = now + 0.25;
        }
        input.EndFrame();
        frameCount++;
    }

    // Note 350: a night seen to its end is one of the player's nights.
    if (session.World.Run?.Over == true)
        nightsOver = profile.CountNight().Nights;
    if (capture is not null)
    {
        var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, showHud || frontEnd.Night is not null ? overlay : null);
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
        // E.6: the shuffle bag goes into the save with the night (a derail drew from it).
        // Note 281: and the town it left, so the next night's isn't the same custom again.
        var settled = Campaign.Settle(campaign, report) with { Music = session.MusicBag ?? campaign.Music, LastTown = session.World.Town?.Plan.Culture ?? campaign.LastTown };
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

/// <summary>Disposes what it's given when it is (the renderer's remade when the display settings change, T83).</summary>
sealed class Owner(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
