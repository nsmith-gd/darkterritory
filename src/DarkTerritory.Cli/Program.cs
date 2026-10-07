using System.Diagnostics;
using System.Text.Json;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

// `dt` — the headless command-line entry point. Everything an agent needs to inspect or verify
// the game without a window goes through here. Output is JSON unless stated otherwise.

var baseContent = DataFile.FindContentRoot(Environment.CurrentDirectory);
// Mods (T49) laid over the content like the game does (--no-mods for the base game). The editor edits the base content.
args = Mods.TakeArgs(args);
bool noMods = args.Contains("--no-mods");
args = [.. args.Where(a => a != "--no-mods")];
var content = args is ["edit", ..] or ["mods", ..] or ["edition", "bake", ..] ? baseContent : Mods.Mount(baseContent, enabled: !noMods);
var train = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
var player = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
var boiler = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));
var routeTuning = RouteTuning.Load(content);
var sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File));

return args switch
{
    // dt mods pack <package folder> [--out dir]: a Thunderstore-ready zip, or what the site would refuse (T78). Exit 1 if refused.
    ["mods", "pack", var package, ..] => PrintPack(package, Str(args, "--out", "out/mods")),
    // dt mods: the mods found, in load order, what can't be loaded and why, and what each does to which file (T49, T78).
    ["mods", ..] => Print(ModsReport(baseContent)),
    // dt edition bake <name> --into <dir>: the base content with an edition (editions/<name>) baked in, as the demo build
    // ships it (T79). dt [--edition demo] edition: what the content in use is.
    ["edition", "bake", var name, ..] => Print(new { edition = name, content = Path.GetFullPath(Mods.Bake(baseContent, name, Str(args, "--into", $"out/editions/{name}"))) }),
    ["edition", ..] => Print(EditionTuning.Load(content)),
    ["train", "table"] => Print(TrainTable(train, player)),
    ["boiler", "table"] => Print(new[] { 3, 6, 10, 15, 20 }.Select(n => new
    {
        cars = n,
        holdSecondsPerUnit = Math.Round(BoilerScenarios.HoldingSecondsPerUnit(boiler, n), 1),
        tenderEnduranceMin = Math.Round(BoilerScenarios.TenderEnduranceMinutes(boiler, n)),
        rebuildFromZeroS = Math.Round(BoilerScenarios.RebuildSeconds(train, boiler, player, n)),
        tenMinutesFullThrottle = BoilerScenarios.Run(train, boiler, player, n, 600, 1),
    })),
    ["boiler", "run", var cars, ..] => Print(BoilerScenarios.Run(train, boiler, player, int.Parse(cars), Opt(args, "--seconds", 300), Opt(args, "--throttle", 1),
        args.Contains("--no-fireman") ? null : Opt(args, "--fire-at", 88), Opt(args, "--pressure", 75), Opt(args, "--firebox", 4), Opt(args, "--speed", 0), args.Contains("--vent"))),
    ["train", "stop", var cars, ..] => Print(TrainScenarios.StopFrom(train, int.Parse(cars), Opt(args, "--from", train.MaxSpeed), Opt(args, "--load", 1), Opt(args, "--grade", 0))),
    ["train", "climb", var cars, var grade, ..] => Print(TrainScenarios.Climb(train, int.Parse(cars), double.Parse(grade), Opt(args, "--from", 10), Opt(args, "--load", 1))),
    ["line", "info", var name, ..] => Print(LineInfo(LoadLine(name), Opt(args, "--every", 100))),
    ["line", "drive", var name, ..] => Print(Drive(train, LoadLine(name), (int)Opt(args, "--cars", 3), Opt(args, "--start", -1), Opt(args, "--from", 0), Opt(args, "--throttle", 1), (int)Opt(args, "--seconds", 120))),
    ["art", "check", ..] => ArtCheck(train, content, args),
    ["art", "show", var piece, ..] => Print(ArtShow(train, content, piece, args)),
    ["art", "clip", var creature, var clip, ..] => Print(ArtClip(content, creature, clip, args)),
    ["art", "reel", ..] => Print(ArtReel(content, args)),
    ["art", "clearance", ..] => Print(ArtClearance(content, args)),
    // dt perf: a frame's cost against the frame-rate targets (tuning/perf.json), flat and in a headset.
    ["perf", ..] => Print(PerfCommands.Run(train, content, args)),
    ["screenshot", ..] when args.Contains("--film") => Print(FilmStill(content, args)),
    ["screenshot", ..] when args.Contains("--hud") || args.Contains("--hurt") => Print(HudShot(content, args)),
    ["screenshot", ..] when args.Contains("--menu") => Print(MenuShot(train, content, args)),
    ["screenshot", ..] => Print(Screenshot(train, content, args)),
    ["route", "gen", ..] => Print(GenerateRoute(routeTuning, content, args)),
    ["route", "sweep", ..] => Print(SweepRoutes(routeTuning, (int)Opt(args, "--seeds", 200))),
    ["site", "sweep", ..] => Print(SweepStops(routeTuning, LoadStops(content), (int)Opt(args, "--seeds", 60))),
    ["site", ..] => Print(ShowStop(content, routeTuning, LoadStops(content), args)),
    ["linegen", var verb, ..] => Print(LineGenCommands.Run(content, verb, args)),
    ["harness", ..] => Print(RunHarness(args)),
    ["wreck", ..] => Print(WreckCommands.Run(content, args)),
    ["trailer", ..] => Print(TrailerCommands.Run(content, args)),
    // dt film: the whole derailment (first person, replay, the film's cut, the cause card) as the app plays it, every frame
    // and the mixer's sound, encoded to an MP4 (GDD v1.4 App. E; note 251).
    ["film", ..] => Print(FilmCommands.Run(content, args)),
    ["playthrough", ..] => Print(PlaythroughCommands.Run(content, args)),
    // dt balance --pairs|--triples: GDD §34's combination fairness (note 186). dt audit cascades|grabs: §34's cascade audit,
    // App. A.9 / B.10's per-tree GRAB check. Each exits 1 on a finding.
    ["balance", ..] when args.Contains("--pairs") || args.Contains("--triples") => AuditCommands.Combinations(content, args),
    ["audit", var verb, ..] => AuditCommands.Audit(content, verb, args),
    ["balance", ..] => PrintBalance(RunBalance(args)),
    ["online", "check"] => Print(OnlineCheck()),
    ["campaign", var verb, ..] => Print(CampaignCommand(content, verb, args)),
    ["vr", "check", ..] => Print(VrCheck(train, content, args)),
    ["facility", "drill", var kind, ..] when Enum.TryParse<FacilityKind>(kind, ignoreCase: true, out var fk) => Print(FacilityWorkDrill(fk, args)),
    ["facility", "drill", ..] => Print(FacilityDrill(train, content, routeTuning, args)),
    ["audio", "render", ..] => Print(RenderAudio(content, args)),
    ["audio", "opera", ..] => Print(OperaCommands.Run(content, args)),
    ["audio", "music", ..] => MusicCommands.Run(content, args),
    ["audio", "clerk", ..] => Print(RenderClerk(content, args)),
    ["edit", ..] => Edit(content, args),
    ["voice", "bench", ..] => Print(DarkTerritory.Game.Sound.VoiceBench.Run(content, (int)Opt(args, "--car", 3), Opt(args, "--z", 4), args.Contains("--radio"),
        Opt(args, "--seconds", 2), new Ballast.Net.LinkConditions(Opt(args, "--latency", 0), Opt(args, "--jitter", 0), Opt(args, "--loss", 0)),
        // --space tunnel: heard as if in that space (content/audio/spaces.json), compressor, reverb and all.
        Str(args, "--space", "") is { Length: > 0 } space ? space : null,
        args.Contains("--die-at") ? Opt(args, "--die-at", 1) : null)),

    _ => Usage(),
};

static object TrainTable(TrainTuning t, PlayerTuning p) => t.Performance.Select(r =>
{
    var consist = Consist.Uniform(t, r.Cars, 1);
    var stop = TrainScenarios.StopFrom(t, r.Cars, t.MaxSpeed);
    var dyn = new TrainDynamics(consist);
    return new
    {
        cars = r.Cars,
        lengthM = consist.LengthMetres,
        roofTraverseS = Math.Round(consist.LengthMetres / p.RoofRun, 1),
        massT = consist.MassTonnes,
        stopS = Math.Round(stop.Seconds, 1),
        stopM = Math.Round(stop.Metres),
        maxGradePct = Math.Round(dyn.MaxClimbableGradePercent(), 2),
    };
}).ToList();

object RunHarness(string[] args)
{
    Route? route = Str(args, "--route", "") is { Length: > 0 } spec ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, (int)Opt(args, "--cars", 10)) : null;
    var line = route?.Build() ?? LoadLine(Str(args, "--line", "test-loop"));
    var combat = DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File));
    var enemies = DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(content, DarkTerritory.Sim.Enemies.EnemyTuning.File));
    using var online = args.Contains("--online") ? new DarkTerritory.Game.FakeLobbyNetwork() : null;
    // --trace file: a line each time anyone changes what they're doing (where they are, their part at a stop, warming up).
    using var trace = Str(args, "--trace", "") is { Length: > 0 } tracePath ? new StreamWriter(tracePath) : null;
    string lastTrace = "";
    var runTuning = route is null ? null : DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
    // A route's night starts where the game's does (run.json departShortOfGateM: just short of the fortress's gate), not
    // 400 m along: the yard is all at yard speed, and the crawl to the gate was five minutes of the harness's time (note 188).
    double start = route is null ? 600 : runTuning!.DepartFrom(route.GateOr(routeTuning.YardLength), Consist.Uniform(train, (int)Opt(args, "--cars", 10), 1).LengthMetres);
    return Harness.Run(line, train, player, new HarnessOptions
    {
        Observe = trace is null ? null : (tick, crew, world) =>
        {
            // And what's out there: each enemy, what it's doing, and where (its car, or along the line).
            string enemies = string.Join(" ", world.ActiveEnemies.Where(e => e.Kind != DarkTerritory.Sim.Enemies.EnemyKind.Sleepers)
                .Select(e => $"{e.Kind}:{e.Phase}@{(e.Attached >= 0 ? $"car{e.Attached}" : $"{e.LineDistance:0}")}"));
            string now = string.Join(" | ", crew.Select(c => Harness.Describe(c.Bot, c.State))) + (enemies.Length > 0 ? $"  || {enemies}" : "");
            if (now != lastTrace)
            {
                // And the fire (the Hollow's, the Stoker's), with the steam it makes.
                var b = world.Train.Boiler;
                string fire = world.Train.BoilerTuning is { } bt ? $" fire {b.FireFraction(bt):0.00} P{b.Pressure:0} tender {b.Tender:0}{(b.FireDoorOpen ? " DOOR" : "")}" : "";
                trace.WriteLine($"{tick / 30.0,7:0.0}s  @{world.Train.Dynamics.Distance:0} {world.Train.Dynamics.Velocity:0.0}m/s p{world.Train.Dynamics.Path}{fire}  {now}");
            }
            lastTrace = now;
        },
        Bots = (int)Opt(args, "--bots", 8),
        Cars = (int)Opt(args, "--cars", 10),
        Seconds = Opt(args, "--seconds", 120),
        Seed = (int)Opt(args, "--seed", 1),
        Link = new Ballast.Net.LinkConditions(Opt(args, "--latency", 0.09), Opt(args, "--jitter", 0.02), Opt(args, "--loss", 0.03)),
        StartDistance = Opt(args, "--start", start),
        // Started out on the line (--start past the gate, to look at one stretch of it), the crew are put at their posts: the
        // walk aboard is the yard's (T102), and the fireman stood on the ballast all night.
        WalkAboard = route is null || Opt(args, "--start", start) < route.GateOr(routeTuning.YardLength),
        Combat = args.Contains("--no-combat") ? null : combat,
        Enemies = args.Contains("--enemies") ? enemies : null,
        Route = route,
        Udp = args.Contains("--udp"),
        Network = online,
        Holdouts = DataFile.Load<DarkTerritory.Sim.Run.HoldoutTuning>(Path.Combine(content, DarkTerritory.Sim.Run.HoldoutTuning.File)),
        Sight = sight,
        Run = runTuning,
        Facilities = route is null ? null : DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)),
        YardLength = route?.GateOr(routeTuning.YardLength) ?? routeTuning.YardLength,
        Voice = Voice(args),
        // --drop-rejoin bot:at:seconds (note 253): one bot's link drops at seconds in and it connects again that long after,
        // asking for its slot back. bot: its index in the crew (0, the driver) or its name (roof-walker: the first).
        DropRejoin = Str(args, "--drop-rejoin", "") is { Length: > 0 } dropRejoin
            ? DarkTerritory.Sim.Net.DropRejoin.Parse(dropRejoin, name => Enumerable.Range(0, (int)Opt(args, "--bots", 8))
                .First(i => DarkTerritory.Sim.Bots.BotCrew.Make(i, (int)Opt(args, "--bots", 8), null, combat, player, 1).Name == name))
            : null,
        // The combination audit's night by hand (note 186): --insist kind,kind sends only those; --hazards a set from balance.json.
        Insist = Str(args, "--insist", "") is { Length: > 0 } insist ? [.. insist.Split(',').Select(k => Enum.Parse<DarkTerritory.Sim.Enemies.EnemyKind>(k, ignoreCase: true))] : null,
        Hazards = Str(args, "--hazards", "") is { Length: > 0 } hz
            ? DataFile.Load<BalanceTuning>(Path.Combine(content, BalanceTuning.File)).Combinations.HazardSets.First(h => h.Name == hz) : null,
        // And its look-out (note 212), as the sweep's; --no-look leaves it out (note 222: the before of a before/after).
        Look = args.Contains("--insist") && !args.Contains("--no-look") ? DataFile.Load<BalanceTuning>(Path.Combine(content, BalanceTuning.File)).Combinations.Look : null,
    }, args.Contains("--no-boiler") ? null : boiler);
}

// GDD §34's degraded comms (note 186): --comms poor|awful (balance.json comms), or --voice-loss/-latency/-jitter/-talkover.
DarkTerritory.Sim.Bots.VoiceConditions? Voice(string[] args)
{
    if (Str(args, "--comms", "") is { Length: > 0 } name)
        return DataFile.Load<BalanceTuning>(Path.Combine(content, BalanceTuning.File)).Comms.TryGetValue(name, out var v) ? v
            : throw new ArgumentException($"no comms called {name} in {BalanceTuning.File}");
    if (!args.Any(a => a.StartsWith("--voice-")))
        return null;
    return new DarkTerritory.Sim.Bots.VoiceConditions(Opt(args, "--voice-loss", 0), Opt(args, "--voice-latency", 0), Opt(args, "--voice-jitter", 0),
        Opt(args, "--voice-talkover", 0));
}

// GDD §34's balance sweep (T55): harness nights over tiers, seeds, crew sizes and train lengths, run side by side, and
// judged against tuning/balance.json. Exit code 1 if a check fails.
BalanceReport RunBalance(string[] args)
{
    var tiers = Str(args, "--tiers", "frontier").Split(',').Select(t => Enum.Parse<RouteTier>(t, ignoreCase: true));
    var seeds = Enumerable.Range(1, (int)Opt(args, "--seeds", 2)).Select(s => (ulong)s);
    var crews = Str(args, "--crews", "2,8").Split(',').Select(int.Parse);
    var lengths = Str(args, "--cars", "10").Split(',').Select(int.Parse);
    var grid = Balance.Grid(tiers, seeds, crews, lengths);
    // The targets first: a sweep's an hour of nights, not to be lost to a bad file at the end.
    var targets = DataFile.Load<BalanceTuning>(Path.Combine(content, BalanceTuning.File));
    var combat = DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File));
    var enemies = DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(content, DarkTerritory.Sim.Enemies.EnemyTuning.File));
    var holdouts = DataFile.Load<DarkTerritory.Sim.Run.HoldoutTuning>(Path.Combine(content, DarkTerritory.Sim.Run.HoldoutTuning.File));
    var run = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
    var facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File));
    // Long enough for a Dead Lines night and its yard (T76: they run past 3600 s); a night stops when its run's over.
    double seconds = Opt(args, "--seconds", 5400);
    var rows = new BalanceRow[grid.Count];
    // Each night is its own host and bots over their own loopback; nothing's shared, so they run side by side.
    Parallel.For(0, grid.Count, new ParallelOptions { MaxDegreeOfParallelism = (int)Opt(args, "--parallel", Environment.ProcessorCount) }, i =>
    {
        var n = grid[i];
        // The procedural line players get (T71): planned for this train's length, as a night in the game is.
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, n.Tier, n.Seed, n.Cars);
        var report = Harness.Run(route.Build(), train, player, new HarnessOptions
        {
            Bots = n.Crew,
            Cars = n.Cars,
            Seconds = seconds,
            Seed = (int)n.Seed,
            Link = new Ballast.Net.LinkConditions(0.09, 0.02, 0.03),
            StartDistance = 400,
            Combat = combat,
            Enemies = enemies,
            Route = route,
            Holdouts = holdouts,
            Sight = sight,
            Run = run,
            Facilities = facilities,
            YardLength = route.GateOr(routeTuning.YardLength),
        }, boiler);
        rows[i] = Balance.Row(n, report);
        Console.Error.WriteLine($"{n.Tier}:{n.Seed} crew {n.Crew} cars {n.Cars}: {rows[i].End}, net {rows[i].Net}, lost {rows[i].CrewLost}");
    });
    return Balance.Judge(rows, targets);
}

static object ModsReport(string baseContent)
{
    var scan = ContentMods.Scan(Mods.Folders(baseContent));
    return new
    {
        folders = Mods.Folders(baseContent),
        mods = scan.Mods.Select(m => new { m.Id, m.Version, m.Thunderstore, m.Dependencies, m.Order, m.Description, m.Directory }),
        problems = scan.Problems,
        files = ContentMods.Plan(baseContent, scan.Mods),
    };
}

static int PrintPack(string package, string outDir)
{
    var zip = ContentMods.Pack(package, outDir, out var problems);
    Print(new { zip, problems });
    return zip is null ? 1 : 0;
}

static int PrintBalance(BalanceReport report)
{
    Print(report);
    return report.Pass ? 0 : 1;
}

// A headset session end to end: runtime, stereo swapchains, frames at the runtime's pace, a clean exit, and both eyes
// as a PNG. Headless with Monado's simulated HMD (docs/ARCHITECTURE.md §8 note 25).
static object VrCheck(TrainTuning t, string content, string[] args)
{
    int frames = (int)Opt(args, "--frames", 30);
    string output = Str(args, "--out", "out/shots/vr.png");
    DarkTerritory.Game.VrView vr;
    try
    {
        // How the eyes are drawn: tuning/vr.json's way (multiview where the GPU has it), or --stereo multiview|per-eye.
        var stereo = Str(args, "--stereo", "") switch
        {
            "per-eye" => StereoPath.PerEye,
            "multiview" => StereoPath.Multiview,
            _ => DataFile.Load<DarkTerritory.Game.VrTuning>(Path.Combine(content, DarkTerritory.Game.VrTuning.File)).Stereo,
        };
        vr = DarkTerritory.Game.VrView.Start("dt vr check", Opt(args, "--scale", 0.5), stereo: stereo);
    }
    catch (Ballast.Xr.XrUnavailableException e)
    {
        return new { headset = false, error = e.Message };
    }
    using (vr)
    {
        var line = RailLine.Load(Path.Combine(content, "lines", Str(args, "--line", "test-loop") + ".json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, (int)Opt(args, "--cars", 6), 1)), line, Opt(args, "--at", 1200));
        var body = Views.Get(Str(args, "--view", "roof"), train, (int)Opt(args, "--car", 2));
        var mesh = new MeshBuilder();
        var look = Looked(content, args);
        new GreyboxScene { Time = 0.37, Look = look }.Build(mesh, train, body.Position);
        var lighting = Views.Lighting(train, look);
        if (look is not null)
        {
            vr.Dress(look);
            // Your own gloves on the simulated controllers, as the game draws them (CreatureArt.HeadsetHands); --box-hands: the
            // fallback box fists.
            if (!args.Contains("--box-hands"))
                vr.Hands = (into, b, c) => look.Art.HeadsetHands(into, (float)b.Yaw, c, 1, args.Contains("--tool") ? Tool.Crowbar : Tool.None);
        }
        var outcomes = new Dictionary<string, int>();
        var clock = Stopwatch.StartNew();
        var comfort = new DarkTerritory.Game.VrLocomotion(DataFile.Load<DarkTerritory.Game.VrTuning>(Path.Combine(content, DarkTerritory.Game.VrTuning.File)));
        // --hud: the HUD on its panel (T36), from a solo night stepped a few seconds, as the game draws it; --menu screen:
        // the front end on the menus' panel instead.
        DarkTerritory.Game.VrPanelContent? panel = null;
        if (args.Contains("--menu"))
        {
            var drawn = new Overlay();
            var menu = DemoMenu(content, args).Menu;
            menu.Headset = true;
            menu.Draw(drawn, 480, 270);
            panel = new DarkTerritory.Game.VrPanelContent(new DarkTerritory.Game.VrPanel(comfort.Tuning.Menu), drawn, 480, 270);
        }
        else if (args.Contains("--hud"))
        {
            var night = new PrototypeSession(content, Str(args, "--line", "test-loop"), (int)Opt(args, "--cars", 6));
            night.Controls.Throttle = 0.6;
            for (int i = 0; i < 3 * SimConstants.TickRate; i++)
                night.Step(default);
            var hud = new Overlay();
            DarkTerritory.Game.Hud.Build(hud, 480, 270, night, crosshair: false);
            panel = new DarkTerritory.Game.VrPanelContent(new DarkTerritory.Game.VrPanel(comfort.Tuning.Hud), hud, 480, 270);
        }
        void Count(Ballast.Xr.XrFrameResult r) => outcomes[r.ToString()] = outcomes.GetValueOrDefault(r.ToString()) + 1;
        // The frames asked for, with room for a software renderer: a simulated headset on lavapipe runs at about a frame a
        // second, and the runtime skips one now and then (shouldRender false). A 30 s cap came up one short in CI.
        while (vr.Session.FramesRendered < frames && clock.Elapsed.TotalSeconds < 30 + 3 * frames)
        {
            var r = vr.Frame(mesh, body, lighting, lighting.FogColor, comfort, panel);
            Count(r);
            if (r == Ballast.Xr.XrFrameResult.Exiting)
                break;
            if (r == Ballast.Xr.XrFrameResult.Idle)
                Thread.Sleep(5);
        }
        double seconds = clock.Elapsed.TotalSeconds;
        var (pixels, w, h) = vr.SideBySide(mesh, lighting, lighting.FogColor);
        PngWriter.Write(output, pixels, w, h, (int)Opt(args, "--png-scale", 1));
        // And leave properly: the runtime walks the session down to Exiting.
        vr.Session.RequestExit();
        for (var stop = Stopwatch.StartNew(); stop.Elapsed.TotalSeconds < 5;)
        {
            var r = vr.Frame(mesh, body, lighting, lighting.FogColor);
            if (r == Ballast.Xr.XrFrameResult.Exiting)
                break;
            if (r == Ballast.Xr.XrFrameResult.Idle)
                Thread.Sleep(5);
        }
        object Eye(int i)
        {
            var c = vr.LastEye(i);
            var f = c.Fov!.Value;
            static double Deg(float r) => Math.Round(r * 180 / Math.PI, 1);
            return new { offsetM = new[] { Math.Round(c.EyeOffset.X, 3), Math.Round(c.EyeOffset.Y, 3), Math.Round(c.EyeOffset.Z, 3) }, fovDeg = new[] { Deg(f.Left), Deg(f.Right), Deg(f.Up), Deg(f.Down) } };
        }
        static object Hand(Ballast.Xr.XrHand h) => new { h.Tracked, positionM = new[] { Math.Round(h.Position.X, 3), Math.Round(h.Position.Y, 3), Math.Round(h.Position.Z, 3) } };
        var pads = vr.Session.Controllers;
        return new
        {
            headset = true,
            runtime = vr.Headset.Runtime,
            system = vr.Headset.System,
            device = vr.Gpu.DeviceName,
            recommended = new[] { vr.Headset.EyeWidth, vr.Headset.EyeHeight },
            eyeRender = new[] { vr.Session.EyeWidth, vr.Session.EyeHeight },
            swapchainFormat = vr.Session.SwapchainFormat.ToString(),
            // Multiview: both eyes in one pass and one submit a frame; per-eye: a renderer, a pass and a submit an eye.
            stereo = vr.Stereo.ToString(),
            multiviewDevice = vr.Gpu.Multiview,
            eyeSubmitsPerFrame = vr.Session.FramesRendered > 0 ? Math.Round((double)vr.Session.EyeSubmits / vr.Session.FramesRendered, 2) : 0,
            framesRendered = vr.Session.FramesRendered,
            fps = Math.Round(vr.Session.FramesRendered / seconds, 1),
            outcomes,
            states = vr.Session.States.Select(s => s.ToString()).ToList(),
            eyes = new[] { Eye(0), Eye(1) },
            // The panel's triangles in each eye (and the vignette's, when it's closing in), for CI to check both got the HUD.
            overlayVertices = new[] { vr.OverlayVertices(0), vr.OverlayVertices(1) },
            // What the game would send from them, standing still with the controllers at rest.
            controllers = new
            {
                profile = pads.Profile,
                left = Hand(pads.Left),
                right = Hand(pads.Right),
                buttons = comfort.Intent(default, pads).Buttons.ToString(),
                turn = comfort.Tuning.Turn.ToString(),
                vignette = Math.Round(comfort.Vignette, 3),
            },
            path = Path.GetFullPath(output),
        };
    }
}

// GDD §17's facility set piece, scripted end to end on a generated night (T28): stop short of the spur's points, cut
// what won't fit, run the empties in, load (instantly, or --load-seconds), back out onto the waiting cars and couple,
// set the switch back, and go. Prints when each step began and how the train came out of it.
// GDD §18's facility set pieces (note 185): a bot crew works a facility of this kind, through intent, from a standing start short
// of its spur until the train's left it. --route tier:seed (a generated night with one), else the first hand-tuned route
// that has one; --cars n, --hands n crate hands, --seconds s.
object FacilityWorkDrill(FacilityKind kind, string[] args)
{
    var run = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
    var facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File));
    int cars = (int)Opt(args, "--cars", 8);
    (Route Route, int Facility)? found;
    if (Str(args, "--route", "") is { Length: > 0 } spec)
    {
        var generated = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars);
        var features = generated.Of(FeatureKind.Facility).ToList();
        int i = features.FindIndex(f => f.Facility == kind && generated.Branches.Any(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe)));
        found = i >= 0 ? (generated, i) : null;
    }
    else
        found = DarkTerritory.Sim.Bots.FacilityWork.Find(routeTuning, kind);
    if (found is not { } at)
        return new { error = $"no route with a {kind} down a spur" };
    var r = DarkTerritory.Sim.Bots.FacilityWork.Run(at.Route, at.Facility, train, player, boiler, run, facilities, routeTuning.Junctions, cars,
        (int)Opt(args, "--hands", 2), Opt(args, "--seconds", 1500), at.Route.GateOr(routeTuning.YardLength));
    return new
    {
        route = at.Route.Name,
        facility = r.Facility,
        modules = facilities.ModulesOf(kind).Select(m => m.ToString()),
        departed = r.Departed,
        seconds = r.Seconds,
        legs = r.Legs,
        doing = r.Doing,
        loadedBefore = r.LoadedBefore,
        loadedAfter = r.LoadedAfter,
        cars = r.Loads.Select(l => new { load = l.Load, cargo = l.Cargo.ToString(), cargoIntegrity = l.CargoIntegrity, integrity = l.Integrity }),
        bin = r.Bin,
        head = r.Head,
        hoseOn = r.HoseCar >= 0,
        leaking = r.Leaking,
        rakes = r.Rakes,
        switchBack = r.SwitchBack,
        alive = $"{r.Alive}/{r.Crew}",
        deaths = r.Deaths,
        stop = r.Record,
        // The switchyard's cars (note 187): brought away, and still standing; the engine's rake as it left, front to back.
        pickedUp = r.PickedUp.Select(c => new { id = c.Id, load = c.Load, cargo = c.Cargo.ToString() }),
        stillStanding = r.StillStanding,
        order = r.Order,
        // The wreck yard's heaps (note 187).
        heaps = r.Heaps.Select(h => new { found = h.Found, unfound = h.Unfound, shifts = h.Shifts, stability = h.Stability }),
        stops = r.Stops.Select(x => new { x.Kind, x.Seconds }),
    };
}

static object FacilityDrill(TrainTuning t, string content, RouteTuning rt, string[] args)
{
    int cars = (int)Opt(args, "--cars", 7);
    var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, Str(args, "--route", "frontier:7"), cars);
    var facilities = route.Of(FeatureKind.Facility).ToList();
    var run = new DarkTerritory.Sim.Run.Run(DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)), route);
    int facility = (int)Opt(args, "--facility", Enumerable.Range(0, facilities.Count).FirstOrDefault(i => run.SpurOf(i) >= 0, -1));
    if (facility < 0 || run.SpurOf(facility) < 0)
        return new { error = $"{route.Name} has no facility with a spur{(facility >= 0 ? $" at {facility}" : "")}" };
    var line = route.Build();
    var spur = line.Branches[run.SpurOf(facility)];
    var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, cars, 0.5)), line, spur.Toe - 120);
    var world = new World(train);
    world.EnableSwitches(rt.Junctions);
    world.EnableRun(run.Tuning, route, route.GateOr(rt.YardLength), authority: true);
    var drill = new DarkTerritory.Sim.Run.SpurDrill(world, facility);
    double loadFor = Opt(args, "--load-seconds", 0), loading = 0;
    var order = train.Dynamics.Consist.Vehicles.Select(v => v.Id).ToArray();
    int ticks = 0;
    for (; ticks < SimConstants.TickRate * 3600 && drill.Step != DarkTerritory.Sim.Run.DrillStep.Done; ticks++)
    {
        if (drill.Step == DarkTerritory.Sim.Run.DrillStep.Loading && (loading += SimConstants.TickSeconds) >= loadFor)
            drill.Loaded = true;
        world.BeginTick();
        world.Step(drill.Tick(SimConstants.TickSeconds));
        world.StepRun([]);
    }
    return new
    {
        route = route.Name,
        facility = $"{facilities[facility].Facility} ({facility})",
        spur = new { toe = spur.Toe, length = spur.Local.Length, side = spur.Side < 0 ? "left" : "right" },
        cars,
        tookIn = drill.TookIn,
        leftWaiting = drill.LeftWaiting,
        timeline = drill.Timeline.Select(x => new { step = x.Step.ToString(), atS = x.Seconds }),
        done = drill.Step == DarkTerritory.Sim.Run.DrillStep.Done,
        seconds = Math.Round(ticks * SimConstants.TickSeconds, 1),
        rakes = train.TrainRakes,
        inOrder = train.TrainRakes == 1 && train.Dynamics.Consist.Vehicles.Select(v => v.Id).SequenceEqual(order),
        onMain = train.OnMain,
        switchBack = !train.Diverging(spur.Index),
        departures = world.Run!.Departures,
        worstIntegrity = train.Vehicles.Min(v => v.Integrity),
    };
}

// The campaign between nights (spec E, F): save slots, the board, purchases, the progression check, and a night
// played by bots to settle.
object CampaignCommand(string content, string verb, string[] args)
{
    var t = DataFile.Load<DarkTerritory.Sim.Campaign.CampaignTuning>(Path.Combine(content, DarkTerritory.Sim.Campaign.CampaignTuning.File));
    var runTuning = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
    var saves = new DarkTerritory.Game.SaveSlots(Str(args, "--saves", DarkTerritory.Game.SaveSlots.DefaultDirectory), t.SaveSlots);
    int slot = (int)Opt(args, "--slot", 1);
    object Board(DarkTerritory.Sim.Campaign.CampaignState s) => new
    {
        slot = s.Slot,
        s.Name,
        cars = s.Cars,
        scrip = Math.Round(s.Scrip),
        runs = s.Runs,
        spareKits = s.SpareKits,
        tier = DarkTerritory.Sim.Campaign.Campaign.TierFor(t, s.Cars).ToString(),
        nextCar = DarkTerritory.Sim.Campaign.Campaign.NextCarCost(t, s),
        upgrades = s.Upgrades,
        contracts = DarkTerritory.Sim.Campaign.Campaign.Offers(t, runTuning, s).Select((c, i) => new { index = i, route = c.Route, cargo = DarkTerritory.Sim.Train.Cargoes.Name(c.Cargo), perCar = c.PerCar }),
        stores = s.Stores,
        sellBack = DarkTerritory.Sim.Campaign.Campaign.SellBack(t, s),
        shop = t.Upgrades.Where(u => !s.Upgrades.Contains(u.Id)).Select(u => new { u.Id, u.Name, size = u.Size.ToString(), cost = DarkTerritory.Sim.Campaign.Campaign.UpgradeCost(t, s, u), modelled = u.Effect.Count > 0 }),
        underway = s.Current?.Route,
        autosave = s.Checkpoint is { } c ? $"left facility {c.Facility} at {c.Seconds / 60:0.0} min" : null,
        s.History,
    };
    DarkTerritory.Sim.Campaign.CampaignState Load() => saves.Load(slot) ?? throw new InvalidOperationException($"slot {slot} is empty: dt campaign new --slot {slot}");
    switch (verb)
    {
        case "new":
            {
                var s = DarkTerritory.Sim.Campaign.Campaign.New(t, slot, Str(args, "--name", $"Crew {slot}"), (ulong)Opt(args, "--seed", Random.Shared.Next(1, 100000)));
                saves.Save(s);
                return new { path = saves.PathOf(slot), board = Board(s) };
            }
        case "show":
            return Board(Load());
        case "slots":
            return saves.List().Select(x => new { x.Slot, name = x.State?.Name, cars = x.State?.Cars, scrip = x.State is { } st ? Math.Round(st.Scrip) : (double?)null, runs = x.State?.Runs });
        case "buy":
            {
                var s = Load();
                string what = args.SkipWhile(a => a != "buy").Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "car";
                var p = what switch
                {
                    "car" => DarkTerritory.Sim.Campaign.Campaign.BuyCar(t, s),
                    "kit" => DarkTerritory.Sim.Campaign.Campaign.BuySpareKit(t, s),
                    // GDD §9's departure (note 182): powder and shot, lamps, extinguishers; and a car taken off.
                    "powder" => DarkTerritory.Sim.Campaign.Campaign.BuyStores(t, s, DarkTerritory.Sim.Campaign.StoreKind.Powder),
                    "lamp" => DarkTerritory.Sim.Campaign.Campaign.BuyStores(t, s, DarkTerritory.Sim.Campaign.StoreKind.Lamp),
                    "extinguisher" => DarkTerritory.Sim.Campaign.Campaign.BuyStores(t, s, DarkTerritory.Sim.Campaign.StoreKind.Extinguisher),
                    "sell" => DarkTerritory.Sim.Campaign.Campaign.SellCar(t, s),
                    _ => DarkTerritory.Sim.Campaign.Campaign.BuyUpgrade(t, s, what),
                };
                if (p.Ok)
                    saves.Save(p.State);
                return new { bought = p.Ok ? what : null, refused = p.Refused, board = Board(p.State) };
            }
        case "sim":
            {
                var reached = DarkTerritory.Sim.Campaign.Campaign.Simulate(t, runTuning);
                return new
                {
                    crew = t.StandardCrew,
                    runsTo = reached.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value),
                    specF4 = "6 cars by ~8 runs, 10 by ~20, 15 by ~38, 20 by 55-60",
                    carCosts = Enumerable.Range(4, t.MaxCars - 3).ToDictionary(n => n.ToString(), n => DarkTerritory.Sim.Campaign.Campaign.CarCost(t, n)),
                };
            }
        case "play":
            {
                // A night on a contract from the board, crewed by bots (they drive, shoot, and load at winch stops), settled.
                var s = Load();
                var contract = DarkTerritory.Sim.Campaign.Campaign.Offers(t, runTuning, s)[(int)Opt(args, "--contract", 0)];
                s = DarkTerritory.Sim.Campaign.Campaign.Begin(s, contract);
                var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, contract.Tier, contract.Seed, s.Cars);
                var loadout = DarkTerritory.Sim.Campaign.Campaign.Apply(t, s.Upgrades, DarkTerritory.Sim.Campaign.Campaign.WithStores(t, DarkTerritory.Sim.Campaign.Campaign.WithSpareKits(new DarkTerritory.Sim.Campaign.Loadout(train, boiler,
                    DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File)),
                    DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(content, DarkTerritory.Sim.Enemies.EnemyTuning.File))), s.SpareKits), s.Stores));
                var report = Harness.Run(route.Build(), loadout.Train, player, new HarnessOptions
                {
                    Bots = (int)Opt(args, "--bots", 4),
                    Cars = s.Cars,
                    Seconds = Opt(args, "--seconds", 3600),
                    Link = Ballast.Net.LinkConditions.Perfect,
                    StartDistance = 400,
                    Combat = loadout.Combat,
                    Enemies = args.Contains("--no-enemies") ? null : loadout.Enemies,
                    Route = route,
                    Run = runTuning,
                    YardLength = route.GateOr(routeTuning.YardLength),
                    Facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)),
                    Holdouts = DataFile.Load<DarkTerritory.Sim.Run.HoldoutTuning>(Path.Combine(content, DarkTerritory.Sim.Run.HoldoutTuning.File)),
                    Sight = sight,
                    Cargo = contract.Cargo,
                }, loadout.Boiler);
                if (report.Run is not { } night)
                    return new { error = "the night didn't run" };
                s = DarkTerritory.Sim.Campaign.Campaign.Settle(s, night);
                saves.Save(s);
                return new { contract = contract.Route, cargo = DarkTerritory.Sim.Train.Cargoes.Name(contract.Cargo), night, board = Board(s) };
            }
        default:
            return new { error = $"unknown campaign command '{verb}': new, show, slots, buy car|kit|powder|lamp|extinguisher|sell|<upgrade>, sim, play" };
    }
}

// Can this machine reach Steam? Says who it's signed in as, or exactly what's missing.
static object OnlineCheck()
{
    using var steam = Ballast.Online.Steam.SteamBackend.TryStart(Ballast.Online.Steam.SteamBackend.DevAppId, out var error);
    return steam is null
        ? new { steam = false, error }
        : new { steam = true, error = (string?)null, user = steam.NameOf(steam.Me), id = steam.Me.ToString() };
}

RailLine LoadLine(string name) => RailLine.Load(Path.Combine(content, "lines", name + ".json"));

// Generates a route; writes its line to content/lines/<name>.json (so screenshot/app can use it),
// its features next to it, and a map PNG. Prints a summary.
static object GenerateRoute(RouteTuning t, string content, string[] args)
{
    var tier = Enum.Parse<RouteTier>(Str(args, "--tier", "frontier"), ignoreCase: true);
    ulong seed = (ulong)Opt(args, "--seed", 1);
    var route = RouteGenerator.Generate(t, tier, seed);
    string name = Str(args, "--name", "generated");
    DataFile.Save(Path.Combine(content, "lines", name + ".json"), route.Line with { Name = name });
    DataFile.Save(Path.Combine(content, "lines", name + ".route.json"), route);
    string map = Str(args, "--map", $"out/routes/{route.Name}.png");
    PngWriter.Write(map, RouteMap.Render(route, 900, 700), 900, 700);
    return Summarise(route, map);
}

static object Summarise(Route r, string? map = null) => new
{
    r.Name,
    lengthKm = Math.Round(r.Length / 1000, 2),
    dawnMinutes = Math.Round(r.DawnSeconds / 60, 1),
    maxGradePct = r.Line.Segments.Max(s => Math.Abs(s.GradePercent)),
    minRadiusM = r.Line.Segments.Where(s => s.Radius != 0).Select(s => Math.Abs(s.Radius)).DefaultIfEmpty(0).Min(),
    facilities = r.Of(FeatureKind.Facility).Select(f => $"{f.Facility} @ {f.Start / 1000:0.0} km"),
    tunnels = r.Of(FeatureKind.Tunnel).Count(),
    bridges = r.Of(FeatureKind.Bridge).Select(b => b.MaxCars > 0 ? $"weak ({b.MaxCars} cars)" : "sound"),
    // Each junction's branch: where its points are, which side, and how far to its buffer stop.
    junctions = r.Branches.Select(b => $"{b.Kind} @ {b.Toe / 1000:0.0} km {(b.Side < 0 ? "left" : "right")}, {b.Length:0} m"),
    sleepers = r.Of(FeatureKind.Sleepers).Count(),
    grease = r.Of(FeatureKind.Grease).Count(),
    r.Weather,
    map = map is null ? null : Path.GetFullPath(map),
};

// Generates many routes per tier and reports ranges: the tier-progression check (App. B.9).
static object SweepRoutes(RouteTuning t, int seeds) => Enum.GetValues<RouteTier>().Select(tier =>
{
    var routes = Enumerable.Range(1, seeds).Select(s => RouteGenerator.Generate(t, tier, (ulong)s)).ToList();
    return new
    {
        tier = tier.ToString(),
        lengthKm = new[] { routes.Min(r => r.Length), routes.Max(r => r.Length) }.Select(v => Math.Round(v / 1000, 1)),
        dawnMinutes = new[] { routes.Min(r => r.DawnSeconds), routes.Max(r => r.DawnSeconds) }.Select(v => Math.Round(v / 60)),
        meanSleepers = Math.Round(routes.Average(r => r.Of(FeatureKind.Sleepers).Count()), 1),
        meanGrease = Math.Round(routes.Average(r => r.Of(FeatureKind.Grease).Count()), 1),
        meanFacilities = Math.Round(routes.Average(r => r.Of(FeatureKind.Facility).Count()), 1),
        junctions = new[] { routes.Min(r => r.Branches.Count), routes.Max(r => r.Branches.Count) },
        meanJunctions = Math.Round(routes.Average(r => r.Branches.Count), 1),
        weakBridgeShare = Math.Round(routes.SelectMany(r => r.Of(FeatureKind.Bridge)).DefaultIfEmpty().Average(b => b is { MaxCars: > 0 } ? 1.0 : 0), 2),
        meanClimbM = Math.Round(routes.Average(r => r.Line.Segments.Where(s => s.GradePercent > 0).Sum(s => s.Length * s.GradePercent / 100))),
    };
}).ToList();

static StopTuning LoadStops(string content) => DataFile.Load<StopTuning>(Path.Combine(content, StopTuning.File));

static StopContext StopContextOf(RouteTuning t) => t.StopContext;

// One stop's layout (level-design Parts D and Z): the summary, every check, and a top-down plan PNG.
static object ShowStop(string content, RouteTuning rt, StopTuning st, string[] args)
{
    var tier = Enum.Parse<RouteTier>(Str(args, "--tier", "frontier"), ignoreCase: true);
    ulong seed = (ulong)Opt(args, "--seed", 1);
    var kind = Enum.Parse<StopKind>(Str(args, "--kind", "yardAndVillage"), ignoreCase: true);
    StopLayout layout;
    double? start = null;
    if (Str(args, "--route", "") is { Length: > 0 } spec)
    {
        // A stop as a generated night has it (the night the game plays, as `dt screenshot --route` does): the route's i-th
        // stop (facilities' yards and village halts, in order).
        var stops = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, (int)Opt(args, "--cars", 6)).Features.Where(f => f.Stop is not null).ToList();
        int i = (int)Opt(args, "--stop", 0);
        if (i < 0 || i >= stops.Count)
            throw new ArgumentException($"{spec} has {stops.Count} stops (--stop 0..{stops.Count - 1})");
        layout = stops[i].Stop!;
        start = stops[i].Start;
        (tier, seed, kind) = (layout.Tier, layout.Seed, layout.Kind);
    }
    else
        // --town: a village halt as a dead town has it, with its station and goods yard (note 302).
        layout = StopGenerator.Generate(st, tier, seed, kind, StopContextOf(rt) with { DeadTown = args.Contains("--town") });
    string plan = Str(args, "--out", $"out/stops/{tier}-{seed}-{kind}.png");
    int size = (int)Opt(args, "--size", 900);
    PngWriter.Write(plan, StopMap.Render(layout, size), size, size);
    return new
    {
        tier = tier.ToString(),
        seed,
        kind = layout.Kind.ToString(),
        form = layout.Form?.ToString(),
        village = layout.VillageForm?.ToString(),
        arrangement = layout.Arrangement.ToString(),
        // Where it is on the night's line (for `dt screenshot --route ... --cam`), and which side its yard and village are.
        start,
        yardSide = layout.YardSide,
        villageSide = layout.VillageSide,
        villageOffset = layout.VillageOffset,
        halt = layout.Halt,
        power = layout.Power.ToString(),
        powerhouse = layout.Powerhouse >= 0 ? layout.Buildings[layout.Powerhouse].Centre : (Pt?)null,
        exitGrade = layout.ExitGrade,
        attempt = layout.Attempt + 1,
        layout.InBand,
        band = StopGenerator.Band(st, layout),
        layout.Moves,
        tracks = layout.Tracks.Select(t => new { t.Index, side = t.Side, toe = Math.Round(t.Toe, 1), offset = t.Offset, length = Math.Round(t.Length, 1), t.Capacity, t.FaceCars, crane = t.Crane, derelicts = t.Derelicts }),
        buildings = layout.Buildings.GroupBy(b => b.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        // A dead town's railway side (note 302): where its station, goods shed and derelicts stand, and its goods siding.
        railwaySide = layout.Buildings.Where(b => b.Kind is BuildingKind.Station or BuildingKind.GoodsShed or BuildingKind.Derelict)
            .Select(b => new { kind = b.Kind.ToString(), s = Math.Round(b.S, 1), d = Math.Round(b.D, 1) }),
        sidings = layout.Sidings.Select(x => x.Select(p => new { s = Math.Round(p.S, 1), d = Math.Round(p.D, 1) })),
        containers = layout.Containers.GroupBy(c => c.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        // Where a dead player waits to be freed (App. D.4), and where the outside creatures live (B.6, B.8).
        holdouts = layout.Holdouts.Select(h => new
        {
            kind = h.Kind.ToString(),
            site = h.Site.ToString(),
            building = layout.Buildings[h.Building].Kind.ToString(),
            at = layout.Buildings[h.Building].Centre,
            h.Second,
            walk = h.Walk
        }),
        lairs = layout.Lairs.GroupBy(x => x.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        checks = layout.Checks.Where(c => c.Applies).Select(c => new { c.Name, c.Pass, c.Detail }),
        plan = Path.GetFullPath(plan),
    };
}

// Many stops per tier: how hard they come out, how often they land in their band, which checks they fail (P15).
static object SweepStops(RouteTuning rt, StopTuning st, int seeds)
{
    var cx = StopContextOf(rt);
    return Enum.GetValues<RouteTier>().Select(tier =>
    {
        var result = new Dictionary<string, object>();
        // Each kind, and a dead town (a village halt with its railway side, note 302).
        foreach (var (kind, name, town) in Enum.GetValues<StopKind>().Select(k => (k, k.ToString(), false)).Append((StopKind.Village, "DeadTown", true)))
        {
            var first = new List<double>();
            var kept = new List<StopLayout>();
            var kcx = cx with { DeadTown = town };
            for (int s = 1; s <= seeds; s++)
            {
                first.Add(StopGenerator.Attempt(st, tier, (ulong)s, kind, kcx, 0).Moves.Score);
                kept.Add(StopGenerator.Generate(st, tier, (ulong)s, kind, kcx));
            }
            double Q(IEnumerable<double> v, double q) { var a = v.OrderBy(x => x).ToList(); return a[(int)Math.Round(q * (a.Count - 1))]; }
            result[name] = new
            {
                firstAttempt = new[] { 0.05, 0.25, 0.5, 0.75, 0.95 }.Select(q => Q(first, q)),
                kept = new[] { 0.05, 0.5, 0.95 }.Select(q => Q(kept.Select(l => l.Moves.Score), q)),
                band = StopGenerator.Band(st, kept[0]),
                inBand = Math.Round(kept.Average(l => l.InBand ? 1.0 : 0), 3),
                valid = Math.Round(kept.Average(l => l.Valid ? 1.0 : 0), 3),
                meanAttempts = Math.Round(kept.Average(l => l.Attempt + 1.0), 2),
                forms = kept.Where(l => l.Form is not null).GroupBy(l => l.Form!.Value).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                // Blocked sidings (D.2; note 294): how many a kept stop has, and how many its measure cleared.
                blocked = kept.Where(l => l.HasYard).GroupBy(l => l.Tracks.Count(t => t.Blocked)).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                clearances = Math.Round(kept.Where(l => l.HasYard).Select(l => (double)l.Moves.Clearances).DefaultIfEmpty(0).Average(), 2),
                villages = kept.Where(l => l.VillageForm is not null).GroupBy(l => l.VillageForm!.Value).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                failing = kept.SelectMany(l => l.Checks.Where(c => c.Applies && !c.Pass)).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.Count()),
                examples = kept.Where(l => !l.Valid).Take(4).Select(l => new { l.Seed, failed = l.Checks.Where(c => c.Applies && !c.Pass).Select(c => $"{c.Name}: {c.Detail}") }),
            };
        }
        return new { tier = tier.ToString(), stops = result };
    }).ToList();
}

static object LineInfo(RailLine line, double every) => new
{
    name = line.Name,
    lengthM = line.Length,
    segments = line.Segments.Count,
    profile = Enumerable.Range(0, (int)(line.Length / every) + 1).Select(i => line.Sample(i * every)).Select(p => new
    {
        s = p.Distance,
        x = Math.Round(p.Position.X, 1),
        y = Math.Round(p.Position.Y, 2),
        z = Math.Round(p.Position.Z, 1),
        gradePct = p.GradePercent,
        radiusM = p.Curvature == 0 ? 0 : Math.Round(1 / p.Curvature),
    }),
};

// Runs the train along a line with fixed controls and reports every 10 s: the quickest way
// to answer "can a train this long make this climb from here?" without a window.
static object Drive(TrainTuning t, RailLine line, int cars, double start, double from, double throttle, int seconds)
{
    var consist = Consist.Uniform(t, cars, 1);
    var run = new TrainOnLine(new TrainDynamics(consist), line, start < 0 ? consist.LengthMetres : start);
    run.Dynamics.Velocity = from;
    var controls = new TrainControls { Throttle = throttle, Reverser = 1 };
    var samples = new List<object>();
    for (int tick = 0; tick <= seconds * SimConstants.TickRate; tick++)
    {
        if (tick % (10 * SimConstants.TickRate) == 0)
            samples.Add(new
            {
                t = tick / SimConstants.TickRate,
                s = Math.Round(run.Dynamics.Distance, 1),
                v = Math.Round(run.Dynamics.Velocity, 2),
                band = SpeedBands.Classify(t, run.Dynamics.Velocity).ToString(),
                gradePct = Math.Round(run.AverageGrade(), 2),
            });
        if (run.AtEndOfLine && tick > 0)
            break;
        run.Step(SimConstants.TickSeconds, controls);
    }
    return new { line = line.Name, cars, massT = consist.MassTonnes, samples };
}

// Renders a greybox frame to PNG with no window. On machines without a GPU this uses Mesa lavapipe.
static object Screenshot(TrainTuning t, string content, string[] args)
{
    string view = Str(args, "--view", "trackside");
    string lineName = Str(args, "--line", "test-loop");
    int cars = (int)Opt(args, "--cars", 6);
    int width = (int)Opt(args, "--width", 1280), height = (int)Opt(args, "--height", 720), scale = (int)Opt(args, "--scale", 1);
    string output = Str(args, "--out", $"out/shots/{view}.png");

    // --route tier:seed generates the night in memory; --coaling stops the train at its coaling tower, chute pouring.
    Route? generated = Str(args, "--route", "") is { Length: > 0 } spec
        ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars)
        : null;
    var line = generated?.Build() ?? RailLine.Load(Path.Combine(content, "lines", lineName + ".json"));
    // --upgrades id[,id]: the train as the campaign's upgrades make it (note 184: crewCar, secondGuardCar, armouredCar,
    // roofHandrails...).
    if (Str(args, "--upgrades", "") is { Length: > 0 } upgrades)
        t = DarkTerritory.Sim.Campaign.Campaign.Apply(DataFile.Load<DarkTerritory.Sim.Campaign.CampaignTuning>(Path.Combine(content, DarkTerritory.Sim.Campaign.CampaignTuning.File)),
            upgrades.Split(','), new DarkTerritory.Sim.Campaign.Loadout(t, DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File)),
                DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File)), null)).Train;
    var consist = Consist.Uniform(t, cars, 1);
    DarkTerritory.Sim.Run.Run? run = null;
    double at = Opt(args, "--at", 1200);
    RouteFeature? tower = generated?.Of(FeatureKind.Facility).FirstOrDefault(f => f.Facility == FacilityKind.CoalingTower);
    if (args.Contains("--coaling") && generated is not null && tower is not null)
    {
        run = new DarkTerritory.Sim.Run.Run(DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)), generated);
        at = run.ChuteAt(tower, line).SpoutAlong + EnginePlan.Of(t.Geometry).CoalFromFront;
        int index = generated.Of(FeatureKind.Facility).ToList().IndexOf(tower);
        run.Mirror(DarkTerritory.Sim.Run.RunPhase.AtFacility, DarkTerritory.Sim.Run.RunEnd.None, 900, index, true, [.. Enumerable.Repeat(200.0, run.FacilityCount)]);
    }
    // --site: stop at the first facility with loading modules (spec D), crates out and the winch sled part-hauled.
    DarkTerritory.Sim.Run.Site? site = null;
    if (args.Contains("--site") && generated is not null)
    {
        run = new DarkTerritory.Sim.Run.Run(DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)), generated);
        var facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File));
        run.EnableSites(facilities, line);
        // The yard's own gantries and loot (level-design P14, P18), out as if the train had just stopped.
        run.EnableLoot(DataFile.Load<LootTuning>(Path.Combine(content, LootTuning.File)), line, facilities);
        // --crane: the first facility with a gantry crane (T48) instead, its first casting on the hook.
        // --facility i: that facility's site, whatever it has (to look at a kind's buildings).
        // --facility kind: the route's first facility of that kind (GDD §18's set pieces, note 185: grainElevator, slaughterhouse,
        // chemicalWorks, militaryDepot...), working: the spout pouring into a car under it, the herd going up, the hose on.
        string facilityArg = Str(args, "--facility", "");
        FacilityKind? kindPick = facilityArg.Length > 0 && !char.IsDigit(facilityArg[0]) ? Enum.Parse<FacilityKind>(facilityArg, ignoreCase: true) : null;
        int pick = kindPick is null ? (int)Opt(args, "--facility", -1) : -1;
        if (kindPick is { } wantKind && run.Sites.FirstOrDefault(x => x?.Feature.Facility == wantKind) is null)
            return Print(new { error = $"{Str(args, "--route", "")} has no {wantKind}", has = run.Sites.Where(x => x is not null).Select(x => x!.Feature.Facility.ToString()) });
        site = kindPick is { } k ? run.Sites.First(x => x?.Feature.Facility == k)
            : pick >= 0 && pick < run.Sites.Count ? run.Sites[pick]
            : args.Contains("--crane") ? run.Sites.FirstOrDefault(x => x?.Crane is not null)
            : run.Sites.FirstOrDefault(x => x is not null && x.Has(DarkTerritory.Sim.Run.ModuleKind.Winch)) ?? run.Sites.FirstOrDefault(x => x is not null);
        if (site?.Crane is { } shownCrane)
        {
            shownCrane.Castings[0].State = DarkTerritory.Sim.Run.CastingState.Hooked;
            shownCrane.Hook = 4;
            shownCrane.Trolley = 3;
        }
        if (site is not null)
        {
            // Down its spur, the engine up at the buffer stop (T28); on the main line for one without.
            at = site.Spur >= 0 ? line.Branches[site.Spur].End - 0.5 : (site.Feature.Start + site.Feature.End) / 2 + 45;
            // At the grain elevator, its first car under the spout (the engine short of the buffer stop).
            if (site.Has(DarkTerritory.Sim.Run.ModuleKind.Spout) && site.Spur >= 0)
                at = line.Branches[site.Spur].Toe + site.SpoutAlong + consist.OffsetOf(1) + t.Geometry.CarLength / 2;
            bool leak = args.Contains("--leak");
            run.Mirror(DarkTerritory.Sim.Run.RunPhase.AtFacility, DarkTerritory.Sim.Run.RunEnd.None, 900, site.Index, false,
                [.. Enumerable.Repeat(0.0, run.FacilityCount)], [.. run.Sites.Select(x => new DarkTerritory.Sim.Run.SiteState(true, x == site ? 0.45 : 0, x?.SledsLeft ?? 0, x == site, false, x == site ? 0.7 : 0)
                {
                    Bin = x?.Bin ?? 0, Head = x?.Head ?? 0, Pouring = x == site && x.Has(DarkTerritory.Sim.Run.ModuleKind.Spout),
                    Herding = x == site && x.Has(DarkTerritory.Sim.Run.ModuleKind.Ramp), Herd = x == site ? 0.5 : 0,
                    Pressure = x == site ? leak ? 1 : 0.6 : 0, Leak = x == site && leak ? 10 : 0,
                })]);
        }
    }
    // --junction i: at a branch's points (T27), [--diverge] set for the branch, [--through] and the train run in onto it.
    int junction = (int)Opt(args, "--junction", -1);
    if (junction >= 0 && junction < line.Branches.Count)
        at = line.Branches[junction].Toe - (args.Contains("--through") ? 10 : 25);
    // --mail [s]: at the night's first mail crane, car 2's side door by it; s > 0, the bag caught s seconds ago (its snatch
    // and the arms falling).
    Drop? mail = args.Contains("--mail") && generated is not null ? DarkTerritory.Sim.Route.Lineside.Drops(DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File)), generated).FirstOrDefault() : null;
    if (mail is not null)
        at = mail.At + t.Geometry.EngineLength + t.Geometry.CarLength * 1.5 + t.Geometry.CouplingGap * 2;
    // --structure type: the night's first of the plan's structures of that type on the main line (a girder, truss, trestle
    // or viaduct bridge, a causeway, a retaining wall...), the train on it, seen from off its side.
    var structure = Str(args, "--structure", "") is { Length: > 0 } kind && generated?.Plan is { } structurePlan
        // (weak: the first bridge with a car limit, whatever it's built as.)
        ? structurePlan.Structures.FirstOrDefault(x => x.Edge == "main" && (kind == "weak" ? x.Weak is not null : x.Type == Enum.Parse<DarkTerritory.Sim.LineGen.StructureType>(kind, true)))
        : null;
    if (Str(args, "--structure", "") is { Length: > 0 } && structure is null)
        return Print(new { error = $"no {Str(args, "--structure", "")} on {Str(args, "--route", "")}'s main line", has = generated?.Plan?.Structures.Where(x => x.Edge == "main").Select(x => x.Type.ToString()).Distinct() });
    if (structure is not null)
        at = (structure.S0 + structure.S1) / 2 + t.Geometry.EngineLength + t.Geometry.CarLength;
    var train = new TrainOnLine(new TrainDynamics(consist), line, at);
    if (site is { Spur: >= 0 })
    {
        var state = train.Capture();
        train.Restore(state with { Rakes = [state.Rakes[0] with { Path = site.Spur }] });
    }
    // GDD §18's switchyard (note 187): its cars standing on the sidings, and the train run up a siding to couple up to some, a
    // few metres short of them.
    if (site is not null && run is not null)
    {
        run.StandCars(train);
        if (site.Has(DarkTerritory.Sim.Run.ModuleKind.Rakes)
            && run.YardTracks(site.Index).Select(b => train.Rakes.FirstOrDefault(r => r.Path == b && train.Standing(r))).FirstOrDefault(r => r is not null) is { } standing)
        {
            var state = train.Capture();
            int engine = Array.FindIndex(state.Rakes, r => r.Vehicles.Contains(0));
            state.Rakes[engine] = state.Rakes[engine] with { Path = standing.Path, Distance = standing.RearDistance - t.Geometry.CouplingGap - 3 };
            train.Restore(state);
        }
    }
    // The works' hose on the car by its stand (note 185).
    if (site is not null && site.Has(DarkTerritory.Sim.Run.ModuleKind.Hose)
        && train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).MinBy(v => (train.Frames[v.Id].Origin - site.HoseStand).Length) is { } hosed)
        site.Mirror(site.State with { HoseCar = hosed.Id });
    if (junction >= 0 && junction < line.Branches.Count)
    {
        var branch = line.Branches[junction];
        train.ThrowSwitch(junction, args.Contains("--diverge"), 0);
        for (int i = 0; args.Contains("--through") && i < SimConstants.TickRate * 60 && train.Dynamics.Distance < branch.Toe + 160; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = train.Dynamics.Speed < 4 ? 0.6 : 0, Reverser = 1 });
        for (int i = 0; i < SimConstants.TickRate * 20 && train.Dynamics.Speed > 0.05; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
        at = train.Dynamics.Distance;
    }
    // --cut N: cut behind car N and pull the engine forward, to see a split train.
    if (Opt(args, "--cut", -1) is var cutAt and >= 0)
    {
        train.Uncouple((int)cutAt);
        for (int i = 0; i < SimConstants.TickRate * 12; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = i < SimConstants.TickRate * 6 ? 1 : 0, Brake = i < SimConstants.TickRate * 6 ? 0 : 1, Reverser = 1 });
    }
    // --ruptured s: the train as the rupture leaves it s seconds on: dragged down from --speed (20) at ruptureDecel to
    // coasting speed (boiler.json; the drivers sliding till then), so the burst's steam and the sparks are laid back right.
    if (args.Contains("--ruptured"))
    {
        var rbt = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));
        train.Dynamics.Velocity = Math.Max(Math.Min(rbt.RuptureCoastBelow, Opt(args, "--speed", 20)), Opt(args, "--speed", 20) - rbt.RuptureDecel * Opt(args, "--ruptured", 0.8));
        train.RefreshFrames();
    }
    // --wreck s: off the rails at --speed (22) and that many seconds into the wreck (T117), seen by the cinematic camera.
    if (Opt(args, "--wreck", -1) is var wreckAt and >= 0)
    {
        train.Dynamics.Velocity = Opt(args, "--speed", 22);
        train.RefreshFrames();
        var wrecking = new World(train) { WreckTuning = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File)) };
        wrecking.Derail("dt screenshot --wreck");
        for (int i = 0; i < wreckAt * SimConstants.TickRate; i++)
            wrecking.Step(default);
    }
    // --stranded s: s seconds into the Stranded outro (GDD v1.4 App. E.9): the empty rack, then the pull-back as the lamps go out.
    double strandedAt = Opt(args, "--stranded", -1);
    var outro = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File)).Stranded;
    var camera = strandedAt >= 0 && !args.Contains("--view") ? Views.Stranded(train, outro, strandedAt)
        : train.Wreck is { } shown && !args.Contains("--view") ? Views.Wreck(shown, shown.RealSeconds) : Views.Get(view, train, (int)Opt(args, "--car", 2));
    // --cam s,lateral,height --target s,lateral,height: place the camera anywhere by line coordinates.
    if (Str(args, "--cam", "") is { Length: > 0 } cam)
    {
        Double3 At(string spec)
        {
            var p = spec.Split(',').Select(double.Parse).ToArray();
            var t = line.Sample(p[0]);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            return t.Position + right * p[1] + Double3.Up * p[2];
        }
        camera = Camera.LookAt(At(cam), At(Str(args, "--target", cam)), (float)Opt(args, "--fov", 65));
    }
    if (structure is not null && Str(args, "--cam", "") is not { Length: > 0 } && !args.Contains("--view"))
    {
        // A bridge from down in its valley, a third of the way along, up at the span and the train on it; anything else
        // from a little above, off the side it's built on (a wall's, or the left).
        var c = line.Sample((structure.S0 + structure.S1) / 2);
        var near = line.Sample(structure.S0 + (structure.S1 - structure.S0) * 0.35);
        var right = Double3.Cross(c.Tangent, Double3.Up).Normalized;
        bool span = structure.Type is DarkTerritory.Sim.LineGen.StructureType.Girder or DarkTerritory.Sim.LineGen.StructureType.Truss
            or DarkTerritory.Sim.LineGen.StructureType.Trestle or DarkTerritory.Sim.LineGen.StructureType.Viaduct;
        if (span)
        {
            double drop = Math.Max(4, structure.HeightM * 0.55);
            camera = Camera.LookAt(near.Position + right * (18 + structure.HeightM * 0.8) - Double3.Up * drop, c.Position - Double3.Up * (drop * 0.4), 62);
        }
        else
        {
            int side = structure.Side == 0 ? -1 : -structure.Side;
            var ahead = line.Sample(structure.S0 + (structure.S1 - structure.S0) * 0.35 - 30);
            camera = Camera.LookAt(near.Position + right * (side * 11) + Double3.Up * 2.5, ahead.Position + right * (-side * 3) - Double3.Up * 0.5, 62);
        }
    }
    if (mail is not null && Str(args, "--cam", "") is not { Length: > 0 } && !args.Contains("--view"))
    {
        // Out beyond the crane on its side, a little up the line, looking back across it at the train's side door.
        var c = line.Sample(mail.At);
        var right = Double3.Cross(c.Tangent, Double3.Up).Normalized * mail.Side;
        camera = Camera.LookAt(c.Position + right * 6.2 + c.Tangent * 3.2 + Double3.Up * 2.2, c.Position + right * 2.35 + Double3.Up * 2.2, 55);
    }
    if (junction >= 0 && junction < line.Branches.Count && Str(args, "--cam", "") is not { Length: > 0 } && !args.Contains("--view"))
    {
        // Behind and beside the stand, looking up the line: both routes, the lamp, and the train in the points.
        var b = line.Branches[junction];
        var toe = line.Sample(b.Toe);
        var right = Double3.Cross(toe.Tangent, Double3.Up).Normalized;
        var ahead = line.Sample(b.Toe + 70);
        camera = Camera.LookAt(toe.Position - toe.Tangent * 18 + right * (b.Side * 7) + Double3.Up * 3.5, ahead.Position + right * (b.Side * 4), 70);
    }
    List<DarkTerritory.Sim.Physics.Body>? cargo = null;
    if (site is not null)
    {
        var shelf = new DarkTerritory.Sim.Physics.Bodies();
        // In the facility's own cases (the depot's are its powder kegs, note 185).
        var freight = site.Feature.Facility is { } fk ? DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)).CargoOf(fk) : CargoKind.None;
        foreach (var crate in site.CrateStack)
            shelf.SpawnCargo(crate - Double3.Up * 0.15, site.CrateLineHint, cargo: freight);
        foreach (var crate in site.HeavyStack)
            shelf.SpawnCargo(crate, site.CrateLineHint, site.HeavyRadius, freight);
        run!.Stock(shelf, run.Stops.ToList().IndexOf(site.Feature));
        // The wreck yard's heaps (note 187), as a crew a while into it would have them: all but the last found by a lamp, their
        // salvage out on the ground beside them, the second shifted once already and the first groaning (--settled: quiet).
        var wreck = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)).Wreck;
        foreach (var heap in site.Heaps)
        {
            bool found = heap.Index < site.Heaps.Count - 1;
            if (found)
                for (int k = 0; k < heap.SalvageStart; k++)
                    shelf.SpawnCargo(DarkTerritory.Sim.Run.Run.PieceAt(site, heap, k, heap.SalvageStart, wreck), site.MainDistance, cargo: CargoKind.Salvage);
            heap.Mirror(new DarkTerritory.Sim.Run.HeapState(found ? 0 : heap.SalvageStart, found, heap.Index == 0 ? 0 : 1, heap.Index == 0 && !args.Contains("--settled") ? 2 : 0, heap.Index == 1 ? 1 : 0));
        }
        cargo = [.. shelf.All];
        if (Str(args, "--cam", "") is not { Length: > 0 })
        {
            // (--crank closes on the winch's cranks even at a facility that also has a crane.)
            // GDD §18's set pieces (note 185), each from out beyond it on its side, along the line a way, looking back at it.
            Double3 Out(Double3 from) => ((from - line.Sample(site.Spur, site.Spur >= 0 ? line.Branches[site.Spur].Toe + site.Mid : site.Mid).Position) with { Y = 0 }).Normalized;
            Double3 Along() => site.Track.Sample(site.Mid).Tangent;
            // The switchyard (note 187): across the gap between the engine and the cars it's coupling up to, from the open side.
            var waiting = site.Has(DarkTerritory.Sim.Run.ModuleKind.Rakes) ? train.Rakes.FirstOrDefault(r => r.Path == train.Dynamics.Path && train.Standing(r)) : null;
            if (waiting is not null)
            {
                var gap = line.Sample(waiting.Path, waiting.RearDistance - 1.5);
                var right = Double3.Cross(gap.Tangent, Double3.Up).Normalized * line.Branches[waiting.Path].Side;
                camera = Camera.LookAt(gap.Position + right * 10 - gap.Tangent * 8 + Double3.Up * 3.2, gap.Position + gap.Tangent * 5 + Double3.Up * 1.2, 66);
            }
            // The wreck yard (note 187): from beside the engine at the buffer stop, out at the heaps in its headlamp.
            else if (site.Heaps.Count > 0)
            {
                var end = site.Track.Sample(site.Track.Length);
                var right = Double3.Cross(end.Tangent, Double3.Up).Normalized * site.Side;
                camera = Camera.LookAt(end.Position - end.Tangent * 12 + right * 7 + Double3.Up * 4.5,
                    (site.Heaps[0].Centre + site.Heaps[Math.Min(1, site.Heaps.Count - 1)].Centre) * 0.5 + Double3.Up, 70);
            }
            else if (site.Has(DarkTerritory.Sim.Run.ModuleKind.Spout))
            {
                var side = ((site.SpoutLever - site.Spout) with { Y = 0 }).Normalized;
                camera = Camera.LookAt(site.Spout + side * 10 + Along() * 8 + Double3.Up * 3.5, site.Spout - Double3.Up * 1.5, 62);
            }
            else if (site.Has(DarkTerritory.Sim.Run.ModuleKind.Ramp))
                camera = Camera.LookAt(site.Pen + Out(site.Pen) * 6 - Along() * 14 + Double3.Up * 6, (site.Pen + site.RampTop) * 0.5, 65);
            else if (site.Has(DarkTerritory.Sim.Run.ModuleKind.Hose))
            {
                var side = (site.HoseCar >= 0 && site.HoseCar < train.Frames.Count ? (site.HoseStand - train.Frames[site.HoseCar].Origin) with { Y = 0 } : Out(site.HoseStand)).Normalized;
                camera = Camera.LookAt(site.HoseStand + side * 8 + Along() * 7 + Double3.Up * 5, site.HoseStand + Double3.Up * 2.5 - side * 2.5, 62);
            }
            else if (site.Crane is { } crane && !args.Contains("--crank"))
            {
                // High on the near side of the track, past the gantry's end, looking down across the train at the hook and castings.
                var outward = (crane.Corner(0, 1) - crane.Corner(0, 0)) with { Y = 0 };
                var along = (crane.Corner(1, 0) - crane.Corner(0, 0)).Normalized;
                camera = Camera.LookAt(crane.Corner(1, 0) - outward.Normalized * 5 + along * 6 + Double3.Up * 11, crane.HookAt - Double3.Up * 2.5, 65);
            }
            // (The depot's powder kegs are the thing to see there, note 185: the crate stack's view.)
            else if (site.Has(DarkTerritory.Sim.Run.ModuleKind.Winch) && (site.Feature.Facility != FacilityKind.MilitaryDepot || args.Contains("--crank")))
            {
                // Out beyond the sled, a little along the line, looking back at the capstan and the train.
                var outward = (site.SledFrom - site.SledTo).Normalized;
                var along = (site.Handles[1] - site.Handles[0]).Normalized;
                camera = args.Contains("--crank")
                    // Close on the drum and its two cranks (T43), from the far side of it from the track.
                    ? Camera.LookAt(site.Capstan + outward * 3.2 + along * 2.2 + Double3.Up * 1.9, site.Capstan + Double3.Up * 0.8, 70)
                    : Camera.LookAt(site.Sled + outward * 9 + along * 7 + Double3.Up * 3.2, site.Capstan + Double3.Up * 1.2, 70);
            }
            else
            {
                // Beyond the crate stack, looking back over it at the train.
                var stack = site.CrateStack.Aggregate(Double3.Zero, (a, b) => a + b) * (1.0 / site.CrateStack.Length);
                var sample = line.Sample(site.CrateLineHint);
                var outward = (stack - sample.Position) with { Y = 0 };
                // Along the track rather than straight out: out there are the facility's buildings.
                camera = Camera.LookAt(stack + outward.Normalized * 3 + sample.Tangent * 10 + Double3.Up * 3, stack, 70);
            }
        }
    }
    string routeFile = Path.Combine(content, "lines", lineName + ".route.json");
    var route = generated ?? (File.Exists(routeFile) ? DataFile.Load<Route>(routeFile) : null);

    var clock = Stopwatch.StartNew();
    using var gpu = new GpuContext("dt screenshot");
    using var renderer = new GreyboxRenderer(gpu, width, height);
    var mesh = new MeshBuilder();
    var look = Looked(content, args);
    if (look is not null)
        look.Sky = DarkTerritory.Game.Art.PlanSky.For(route);
    look?.Dress(renderer);
    // --ps2: the pipeline's debug era mode, for art direction to compare against (no spec maps, harder banding, no bloom).
    if (args.Contains("--ps2"))
        renderer.Post = renderer.Post with { Ps2 = true };
    // --muzzle: the guns fired a tick ago (their flash, and its light).
    if (args.Contains("--muzzle"))
        foreach (var v in train.Vehicles.Where(v => v.HasGun))
            v.Gun.LastShotTick = 100;
    // The guns loaded as a night arms them (Guns.Arm): the powder and shot locker full, as aboard.
    DarkTerritory.Sim.Combat.Guns.Arm(train, DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File)).Guns);
    // --lamps-out i[,j,...]: those cars' lamps put out (Vehicle.LampLit: dark inside, their lanterns unlit).
    if (Str(args, "--lamps-out", "") is { Length: > 0 } outs)
        foreach (int i in outs.Split(',').Select(int.Parse))
            if (i < train.Vehicles.Count)
                train.Vehicles[i].LampLit = false;
    // --integrity a[,b,...]: each car's condition, front to back, the last repeating (look.json "damage": scars, states).
    if (Str(args, "--integrity", "") is { Length: > 0 } integrity)
    {
        var each = integrity.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        for (int i = 0; i < train.Vehicles.Count; i++)
            train.Vehicles[i].Integrity = Math.Clamp(each[Math.Min(i, each.Length - 1)], 0, 1);
    }
    // --cargo a[,b,...]: each cargo car's cargo, front to back, the last repeating (CargoKind: its load's cases, TrainKit.Load).
    if (Str(args, "--cargo", "") is { Length: > 0 } cargoes)
    {
        var each = cargoes.Split(',').Select(x => Enum.Parse<CargoKind>(x, ignoreCase: true)).ToArray();
        int k = 0;
        foreach (var v in train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
            v.Cargo = each[Math.Min(k++, each.Length - 1)];
    }
    // --stand n: close by branch n's switch stand, looking at its lever and lamp (--diverge: set for the branch).
    if (Opt(args, "--stand", -1) is var standIx and >= 0 && standIx < line.Branches.Count)
    {
        var stands = new SwitchStands(new JunctionTuning());
        var lever = stands.LeverAt(line, (int)standIx);
        var toe = line.Sample(line.Branches[(int)standIx].Toe);
        var across = Double3.Cross(toe.Tangent, Double3.Up).Normalized * line.Branches[(int)standIx].Side;
        camera = Camera.LookAt(lever + across * 2.6 - toe.Tangent * 2.2 + Double3.Up * 0.9, lever + Double3.Up * 0.3, 55);
    }
    // --lit: every Holdout on the route occupied, its lamp burning (GDD App. D.7), as if the dead were waiting at each.
    DarkTerritory.Sim.Run.Holdouts? holdouts = null;
    // --freed: every Holdout broken open and its occupant out (D.7, D.8): the door swung wide, the lock smashed off (or, every
    // other one, picked with the repair kit), the barricade pried down. --holdout n: the camera before the nth one's door.
    if ((args.Contains("--lit") || args.Contains("--freed")) && generated is not null)
    {
        holdouts = new DarkTerritory.Sim.Run.Holdouts(DataFile.Load<DarkTerritory.Sim.Run.HoldoutTuning>(Path.Combine(content, DarkTerritory.Sim.Run.HoldoutTuning.File)), generated, line);
        bool freed = args.Contains("--freed");
        foreach (var h in holdouts.All)
            holdouts.Mirror(h.Index, freed ? DarkTerritory.Sim.Run.HoldoutState.Freed : DarkTerritory.Sim.Run.HoldoutState.Occupied, 1, 0, quiet: freed && h.Index % 2 == 1);
        if (Opt(args, "--holdout", -1) is var hi and >= 0 && hi < holdouts.All.Count)
        {
            var h = holdouts.All[(int)hi];
            var outward = (h.Door - h.Inside) with { Y = 0 };
            outward = outward.Length > 0.1 ? outward.Normalized : Double3.Cross(Double3.Up, line.Sample(h.LineHint).Tangent);
            var across = Double3.Cross(Double3.Up, outward);
            camera = Camera.LookAt(h.Door + outward * 4.2 + across * 3.6 + Double3.Up * 2.0, h.Door + Double3.Up * 1.2, 60);
            // --approach m: instead from the cab's height on the line that far short of it (App. D.7: seen from the 1 km board).
            if (args.Contains("--approach"))
            {
                double back = Opt(args, "--approach", 1000);
                var from = line.Sample(Math.Max(0, h.LineHint - back)).Position + Double3.Up * 3.2;
                camera = Camera.LookAt(from, h.Door + Double3.Up * 3, 60);
            }
        }
    }
    // --gun-laid yaw,pitch (degrees): every gun turned and elevated so, as a seated gunner lays it (T112).
    if (Str(args, "--gun-laid", "") is { Length: > 0 } laid)
    {
        var yp = laid.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture) * Math.PI / 180).ToArray();
        foreach (var v in train.Dynamics.Consist.Vehicles.Where(v => v.HasGun))
            (v.Gun.Traverse, v.Gun.Elevation) = (yp[0], yp.Length > 1 ? yp[1] : 0);
    }
    // --doors-open: every door on the train open (looking through an end door onto its coupling, or out of the guard
    // van's rear door into the Car Hugger's mouth).
    if (args.Contains("--doors-open"))
        foreach (var v in train.Dynamics.Consist.Vehicles)
            v.DoorsOpen = 0xFF;
    // --lockers-open NAME[,NAME]: those crew lockers' doors open (note 173; "all" for the row); the lockers view opens the
    // repair kit's (the fitter's) by itself.
    if (Str(args, "--lockers-open", view == "lockers" ? "kit" : "") is { Length: > 0 } lockersOpen && DarkTerritory.Sim.World.KitLocker(train) is { } kitLocker)
        foreach (var bay in train.Frames[kitLocker.Car].Shape.Lockers)
            if (lockersOpen == "all" || lockersOpen == "kit" && bay.Index == kitLocker.Bay.Index
                || lockersOpen.Split(',').Contains(bay.Name, StringComparer.OrdinalIgnoreCase))
                train.Vehicles[kitLocker.Car].LockersOpen |= 1u << bay.Index;
    // --eaten f: the rear car that much eaten by a Car Hugger (App. A.3 FEED; 1 is eaten through), as if from sound.
    if (args.Contains("--eaten"))
    {
        var rear = train.Dynamics.Consist.Vehicles[^1];
        double eaten = Math.Clamp(Opt(args, "--eaten", 0.5), 0, 1);
        (rear.Eaten, rear.Integrity) = (eaten, 1 - eaten);
    }
    // --shouldered [walk] | --cradled [walk]: a crewmate carrying a body over the shoulder, or the child in their arms (App. C.4).
    var shouldered = args.Contains("--shouldered") ? Staging.Shouldered(train, content, Str(args, "--shouldered", "") == "walk")
        : args.Contains("--cradled") ? Staging.Shouldered(train, content, Str(args, "--cradled", "") == "walk", child: true)
        : ((DarkTerritory.Sim.Physics.Bodies Bodies, Crewmate Carrier)?)null;
    var scene = new GreyboxScene
    {
        // --draw m: how far along the line to build it (an aerial view of a stretch wants more than the cab's 400).
        DrawDistance = (float)Opt(args, "--draw", 400),
        // (--shot-age s: that long after the guns fired, for the powder smoke rolling off, Effects.CannonShot.)
        Tick = args.Contains("--muzzle") ? 100 + (long)Math.Round(Opt(args, "--shot-age", 1.0 / 30) * 30) : -1,
        Look = look,
        // --greybox: the box figure's headset bodies from vr.json too (note 223; with the art pass, the look has it).
        VrBody = look is null ? DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File)).Body : null,
        Route = route,
        Run = run,
        Holdouts = holdouts,
        DropCaught = mail is not null ? id => id == mail.Id && Opt(args, "--mail", 0) > 0 : null,
        StagedCatch = Opt(args, "--mail", 0),
        StagedCold = args.Contains("--cold") ? Opt(args, "--cold", 0) : null,
        // --utility i[,j]: those cars drawn as utility cars, fitted out for the crew (the sim has no utility kind yet).
        Utility = Str(args, "--utility", "") is { Length: > 0 } utilities && utilities.Split(',').Select(int.Parse).ToHashSet() is var utilitySet
            ? i => utilitySet.Contains(i) : null,
        // --burnt car,s: that car gutted by a fire that went out s seconds ago (its char, its smoulder).
        StagedBurnt = Str(args, "--burnt", "") is { Length: > 0 } burnt && burnt.Split(',') is var bp
            ? (int.Parse(bp[0]), bp.Length > 1 ? double.Parse(bp[1]) : 30) : null,
        Time = 0.37,
        // --spread f: the staged fire f of the way to jumping the coupling (Staging.Spread).
        Enemies = args.Contains("--threats") ? Later(Staging.Spread(args.Contains("--smoulder") ? Staging.Smoulder(Staging.Switchman(Staging.Soot(Staging.Passenger(Staging.Climber(Staging.Follower(Staging.Stoker(Staging.Grumbler(Staging.Gaunt(Staging.Ribbits(Staging.Whistler(Staging.Tippy(Staging.Hugger(Staging.Debris(Staging.Threats(train, Opt(args, "--doll-at", 22), args.Contains("--lurk-at") ? Opt(args, "--lurk-at", 30) : null), Str(args, "--debris", "")), Str(args, "--hugger", "")), train, Str(args, "--tippy", "")), Str(args, "--whistler", ""), train), Str(args, "--ribbits", ""), train), train, Str(args, "--gaunt", "")), train, Str(args, "--grumbler", "")), Str(args, "--stoker", "")), train, Str(args, "--follower", "")), train, Str(args, "--climber", "")), train, Str(args, "--passenger", "")), train, Str(args, "--soot", "")), Str(args, "--switchman", ""))) : Staging.Switchman(Staging.Soot(Staging.Passenger(Staging.Climber(Staging.Follower(Staging.Stoker(Staging.Grumbler(Staging.Gaunt(Staging.Ribbits(Staging.Whistler(Staging.Tippy(Staging.Hugger(Staging.Debris(Staging.Threats(train, Opt(args, "--doll-at", 22), args.Contains("--lurk-at") ? Opt(args, "--lurk-at", 30) : null), Str(args, "--debris", "")), Str(args, "--hugger", "")), train, Str(args, "--tippy", "")), Str(args, "--whistler", ""), train), Str(args, "--ribbits", ""), train), train, Str(args, "--gaunt", "")), train, Str(args, "--grumbler", "")), Str(args, "--stoker", "")), train, Str(args, "--follower", "")), train, Str(args, "--climber", "")), train, Str(args, "--passenger", "")), train, Str(args, "--soot", "")), Str(args, "--switchman", "")), Opt(args, "--spread", 0), DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(content, DarkTerritory.Sim.Enemies.EnemyTuning.File)).CarFire.SpreadSeconds), Opt(args, "--later", 0)) : null,
        StagedPaces = args.Contains("--passenger") ? new Dictionary<int, float> { [48] = Staging.PassengerPace(Str(args, "--passenger", "")) } : null,
        // --stocked: the train as it leaves, its stores and every car's extinguisher aboard (--charge 0..1: theirs).
        Bodies = shouldered is { } carried ? carried.Bodies.All
            : args.Contains("--bodies") ? Staging.Bodies(train, content).All
            : args.Contains("--stocked") ? Staging.Stocked(train, content, Opt(args, "--charge", 1)).All : cargo,
        // --crew: three on car 2's roof, one reaching up, one holding out both hands, one with a keyboard (T47's arms).
        // --working: the crew at work (X1): carrying, at a hatch and a brake wheel on car 2's roof, sat at the last gun.
        // --act smash,pry,pick,...: a row of the crew down car 2's roof, each at one of those acts (CrewPose names).
        // (--survivor prisoner|wildlander: all of them freed survivors' figures, App. D.8.)
        Crew = args.Contains("--act") ? Staging.Acts(train, content, Str(args, "--act", "").Split(','),
                Enum.Parse<DarkTerritory.Game.Art.Survivor>(Str(args, "--survivor", "none"), ignoreCase: true))
            // --vr-body: three headset crewmates on car 2's roof, leaning, crouched and mid-step (T82; views crew, crewside).
            : args.Contains("--vr-body") ? Staging.Headsets(train, content)
            : args.Contains("--working") ? Staging.Working(train, content)
            : args.Contains("--crew") ? [.. Staging.Crew(train, content), .. args.Contains("--ribbits") || args.Contains("--gaunt") || args.Contains("--grumbler") || args.Contains("--follower") || args.Contains("--soot") ? [Staging.Lone(train)] : Array.Empty<Crewmate>()]
            : Str(args, "--passenger", "") == "drag" ? [Staging.Dragged(train)]
            // --bodies --burned: the staged body (crewmate 9's) is one the fire took: drawn charred, smouldering (spec C.1).
            : args.Contains("--burned") ? [new Crewmate(9, default, 0, false, Death: DarkTerritory.Sim.Player.DeathCause.Burned)]
            // --shouldered [walk]: a crewmate on car 2 with a body over the shoulder (App. C.4; Staging.Shouldered).
            : shouldered is { } sh ? [sh.Carrier] : null,
        Emergency = args.Contains("--emergency"),
        LampsOut = strandedAt >= 0 ? Views.StrandedLampsOut(train.Frames.Count, outro, strandedAt) : 0,
        KitLockerOpen = strandedAt >= 0,
        LampRange = strandedAt >= 0 ? 400 : 60,
        // --headlamp-out: the engine's lamp switched off or smashed (World.LampShining false): its lens dark, no beam.
        LampLit = !args.Contains("--headlamp-out"),
        RoofGlow = strandedAt >= 0,
        FireDoorOpen = args.Contains("--firedoor") || args.Contains("--stoker") || args.Contains("--flare"),
        // --flare s: s seconds after a shovelful landed (default 0.15), the firebox flaring (§31).
        SinceShovel = args.Contains("--flare") ? Opt(args, "--flare", 0.15) : double.PositiveInfinity,
        // --gathering g: the Choir that far through its gathering (0-1), its frost in the air and the crew's breath (A.7).
        ChoirGathering = (float)Opt(args, "--gathering", 0),
        // --perched [s]: the fire burned low s seconds (default 10), the Stoker waiting on the smokestack (World.StokerWaiting);
        // past 42 it's climbing down into it.
        StokerLowFor = args.Contains("--perched") ? Opt(args, "--perched", 10) : -1,
        // --whistle: a crewmate on the cord (the cord hauled down, the whistle's steam).
        CordPulled = args.Contains("--whistle"),
        // --coal u: that much on the fire, as the HUD's FIRE reads it (T121: the firebox's look follows it, out only at 0).
        FireGlow = args.Contains("--ruptured") ? 0 : args.Contains("--coal") ? GreyboxScene.FireLook(Opt(args, "--coal", 4), DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File)).FireboxCapacity) : 0.7f,
        // --spray: an extinguisher on every car fire, from the aisle (with --threats, the staged one: --view fire).
        StagedSpray = args.Contains("--spray"),
        // --derailed s: off the rails s seconds ago (its sparks, dust and boiler burst).
        Derailed = args.Contains("--derailed"),
        // --air ash|spores: a corrupted stretch's air, whatever the biome.
        StagedAir = Str(args, "--air", "") is { Length: > 0 } air ? Enum.Parse<DarkTerritory.Game.Art.Effects.Air>(air, true) : null,
        StagedDerailSeconds = Opt(args, "--derailed", 0.6),
        // --venting: the blow-off held open and the safety valve lifting (T101), their steam.
        Venting = args.Contains("--venting"),
        SafetyValve = args.Contains("--venting"),
        // --ruptured s: the boiler burst s seconds ago (default 0.8: the blast at its height; 30 for the hiss after), spec
        // B.6, GDD §23; the drivers seized and sliding while she's still dragging down to coasting speed.
        // --strain x: every car straining round a bend that hard (0..1, BendStrain: 1 is coming off), its outer rail on the
        // right; the flange sparks of the overspeed telegraph.
        BendStrain = args.Contains("--strain") ? [.. train.Frames.Select(_ => ((float)Opt(args, "--strain", 0.8), 1))] : null,
        Ruptured = args.Contains("--ruptured"),
        StagedRuptureSeconds = Opt(args, "--ruptured", 0.8),
        DriversLocked = args.Contains("--ruptured") && Opt(args, "--speed", 20) - DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File)).RuptureDecel * Opt(args, "--ruptured", 0.8)
            > DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File)).RuptureCoastBelow,
        Diverging = train.Diverging,
        // --throttle x: the regulator's handle drawn that far open (T29's cab levers).
        Controls = new TrainControls { Throttle = Math.Clamp(Opt(args, "--throttle", 0), 0, 1), Reverser = 1 },
        // --own crowbar|shovel|wrench|none: your own arms in view, from the view's eye (X3); --own-swing s: that far into a
        // blow; --own-act shovel|carry|...: at that work instead.
        Own = Str(args, "--own", "") is { Length: > 0 } own
            ? new OwnView((float)camera.Yaw, (float)camera.Pitch,
                Str(args, "--own-act", "") is { Length: > 0 } a ? Enum.Parse<DarkTerritory.Game.Art.CrewPose>(a.Replace("_", ""), true) : null,
                args.Contains("--own-walk"), args.Contains("--own-swing") ? Opt(args, "--own-swing", 0.3) : -1, 1,
                own == "none" ? Tool.None : Enum.Parse<Tool>(own, true))
            : null,
    };
    // --phase s: how far through a timed act the staged crew are (the cannon's reload: 1.5 s a beat; Crewmate.Phase).
    if (args.Contains("--phase") && scene.Crew is { } phased)
        scene.Crew = [.. phased.Select(c => c with { Phase = Opt(args, "--phase", 0) })];
    // --tippy grab: crewmate 1, the one it has, as the game draws them (CrewActs: held_cover, its hand over their mouth).
    if (Str(args, "--tippy", "") == "grab" && scene.Crew is { } held)
        scene.Crew = [.. held.Select(c => c.Id == 1 ? c with { Act = DarkTerritory.Game.Art.CrewPose.HeldCover } : c)];
    // --ribbits tongue|devour: crewmate 4, the one the tongue has, frozen where they stand as the game draws them (held_frozen).
    if (Str(args, "--ribbits", "") is "tongue" or "devour" && scene.Crew is { } frozen)
        scene.Crew = [.. frozen.Select(c => c.Id == Staging.LoneId ? c with { Act = DarkTerritory.Game.Art.CrewPose.HeldFrozen } : c)];
    // --hugger swallow: the one it has in its mouth at the rear car's end door (App. A.3; Staging.Swallowed).
    if (Str(args, "--hugger", "") == "swallow" && scene.Enemies?.OfType<DarkTerritory.Sim.Enemies.CarHugger>().FirstOrDefault() is { Holding: >= 0 })
        scene.Crew = [.. scene.Crew ?? [], Staging.Swallowed(train)];
    // --whistler carry|nest: the one it's carrying off, or has at its nest, as well as anyone else staged (App. A.4; Staging.Carried).
    if (Str(args, "--whistler", "") is "carry" or "nest" && scene.Enemies?.OfType<DarkTerritory.Sim.Enemies.Whistler>().FirstOrDefault() is { Holding: >= 0 } carrying)
        scene.Crew = [.. scene.Crew ?? [], Staging.Carried(carrying)];
    // --gaunt leave|leavein: the body it's carrying off, under it (App. A.6; Staging.GauntLoad).
    if (Str(args, "--gaunt", "") is "leave" or "leavein" && scene.Enemies?.OfType<DarkTerritory.Sim.Enemies.Gaunt>().FirstOrDefault() is { } leaving)
        scene.Bodies = Staging.GauntLoad(train, content, leaving).All;
    // --wreck-poses: the derailment film's crew as each goes into the wreck from their work (App. F.2 take 4; WreckFilm.TaskPose):
    // on the throttle, at the shovel, in the gun's seat, carrying; stood in a row on the ballast off car --car's right (--view flanges).
    if (args.Contains("--wreck-poses"))
    {
        var by = train.Frames[Math.Min((int)Opt(args, "--car", 2), train.Frames.Count - 1)];
        var tasks = new[] { FilmTask.Driving, FilmTask.Firing, FilmTask.Gunning, FilmTask.Carrying };
        var posed = tasks.Select((task, k) =>
        {
            var at = new Double3(2.6, 0, -by.Shape.HalfLength * 0.6 + k * 1.3);
            var joints = WreckFilm.TaskPose(task).Select(j => new Ballast.Physics.Particle(by.ToWorld(at + j), 1, 0.1)).ToArray();
            return new DarkTerritory.Sim.Physics.Body(-1 - k, DarkTerritory.Sim.Physics.BodyKind.Ragdoll, DarkTerritory.Sim.Player.PlayerState.World,
                new Ballast.Physics.PbdBody(joints))
            { Owner = k + 1 };
        });
        scene.Bodies = [.. scene.Bodies ?? [], .. posed];
    }
    scene.Wreck = train.Wreck;
    // --impact ground|water|structure|train|creature|doll [--impact-at ahead,lateral] [--impact-age s] (T121): a cannonball
    // come down there that long ago (its burst, debris, smoke, scorch or splash, and the light of it); "doll" on the staged
    // Track Doll (with --threats), shattered, and her gone from the rail.
    if (Str(args, "--impact", "") is { Length: > 0 } surface)
    {
        var where = Str(args, "--impact-at", "60,0").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var impact = Staging.Impact(train, surface, where[0], where.Length > 1 ? where[1] : 0, scene.Enemies);
        scene.Impacts = [impact];
        if (impact.Struck == DarkTerritory.Sim.Enemies.EnemyKind.TrackDoll && scene.Enemies is List<DarkTerritory.Sim.Enemies.Enemy> staged)
        {
            var doll = staged.First(e => e is DarkTerritory.Sim.Enemies.TrackDoll { Attached: < 0 });
            staged.Remove(doll);
            scene.Hits = [new DarkTerritory.Sim.Combat.HitConfirm(1, Staging.StrikeTick, doll.Id, doll.Kind, 1, DarkTerritory.Sim.Combat.HitSource.Cannon, impact.At, impact.Direction, true)];
        }
        scene.Tick = Staging.StrikeTick + (long)Math.Round(Opt(args, "--impact-age", 0.07) * SimConstants.TickRate);
    }
    // --hit-flash [--hit-age s] (T121): a blow just landed on every staged creature (with --threats), from the camera's side:
    // each one's flinch and flash.
    if (args.Contains("--hit-flash") && scene.Enemies is { } struck)
    {
        scene.Hits = Staging.HitsOn(struck, train, camera.Position);
        scene.Tick = Staging.StrikeTick + (long)Math.Round(Opt(args, "--hit-age", 0.07) * SimConstants.TickRate);
    }
    // --board s (with --threats): the staged hound on the rear car s seconds into its board (up the car's end, over the lip).
    if (args.Contains("--board") && scene.Enemies is List<DarkTerritory.Sim.Enemies.Enemy> pack
        && pack.FirstOrDefault(e => e.Kind == DarkTerritory.Sim.Enemies.EnemyKind.CinderHound && e.Attached >= 0) is { } boarding)
    {
        // (The staged Car Hugger is on the same end: out of the way.)
        pack.RemoveAll(e => e.Kind == DarkTerritory.Sim.Enemies.EnemyKind.CarHugger);
        boarding.Restore(DarkTerritory.Sim.Enemies.SpinePhase.Commit, Opt(args, "--board", 0.5), boarding.Health, boarding.Attached, boarding.Local, 0, 0, 0,
            boarding.Extra, boarding.Extra2);
    }
    // --vanish s[:toy] (with --threats): the staged haunting Track Doll moved into car 2, over its cargo, and gone from there
    // s seconds ago (come at, or with ":toy", given one and taking it: GreyboxScene.Vanishing); s < 0, still there. The
    // inside view looks down that car's aisle at it.
    if (Str(args, "--vanish", "") is { Length: > 0 } vanish && scene.Enemies is List<DarkTerritory.Sim.Enemies.Enemy> dolls
        && dolls.FirstOrDefault(e => e is DarkTerritory.Sim.Enemies.TrackDoll { Attached: 0 }) is { } gone
        && train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.Interior is { } room)
    {
        var parts = vanish.Split(':');
        double ago = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        gone.Restore(DarkTerritory.Sim.Enemies.SpinePhase.Punish, 3, gone.Health, Math.Min(2, train.Frames.Count - 1), room.Centre with { Y = room.Min.Y, Z = room.Centre.Z + 1.5 },
            0, 0, 0, 0, 0);
        if (ago >= 0)
        {
            dolls.Remove(gone);
            scene.Vanished(gone, Staging.StrikeTick, parts.Length > 1 && parts[1] == "toy" ? 1 : -1);
            scene.Tick = Staging.StrikeTick + (long)Math.Round(ago * SimConstants.TickRate);
        }
    }
    // --dispersing s (with --threats): the staged Choir driven off s seconds ago, its ghosts going (GreyboxScene.Leaving).
    if (args.Contains("--dispersing") && scene.Enemies is List<DarkTerritory.Sim.Enemies.Enemy> swarm)
    {
        foreach (var ghost in swarm.Where(e => e.Kind == DarkTerritory.Sim.Enemies.EnemyKind.Choir).ToList())
        {
            swarm.Remove(ghost);
            scene.Dispersed(ghost, Staging.StrikeTick);
        }
        scene.Tick = Staging.StrikeTick + (long)Math.Round(Opt(args, "--dispersing", 1) * SimConstants.TickRate);
    }
    // --hugger ride (with --threats and --cut n): the staged Car Hugger on the last car, cut loose with it as the train was
    // (the sim's done with it then), riding it off into the dark, feeding (GreyboxScene.Riding).
    if (Str(args, "--hugger", "") == "ride" && scene.Enemies is List<DarkTerritory.Sim.Enemies.Enemy> cutLoose
        && cutLoose.OfType<DarkTerritory.Sim.Enemies.CarHugger>().FirstOrDefault(h => h.Attached >= 0) is { } rider)
    {
        int last = train.Vehicles.Count - 1;
        rider.Restore(rider.Phase, rider.PhaseSeconds, rider.Health, last, new Double3(0, 1.0, train.Frames[last].Shape.HalfLength + 0.4), 0, 0, 0, rider.Extra, rider.Extra2);
        cutLoose.Remove(rider);
        scene.Rode(rider, Staging.StrikeTick);
        scene.Tick = Staging.StrikeTick + SimConstants.TickRate;
    }
    // --killed kind:s (with --threats): that staged creature killed s seconds ago by a blow from the camera's side, going over
    // and crumbling (GreyboxScene.Deaths).
    if (Str(args, "--killed", "") is { Length: > 0 } killed && scene.Enemies is List<DarkTerritory.Sim.Enemies.Enemy> living)
    {
        var parts = killed.Split(':');
        var deadKind = Enum.Parse<DarkTerritory.Sim.Enemies.EnemyKind>(parts[0], ignoreCase: true);
        double ago = parts.Length > 1 ? double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0.4;
        var dead = living.First(e => e.Kind == deadKind);
        living.Remove(dead);
        var toward = dead.WorldPosition(train) - camera.Position;
        scene.Killed(dead, Staging.StrikeTick, new System.Numerics.Vector3((float)toward.X, 0, (float)toward.Z));
        scene.Tick = Staging.StrikeTick + (long)Math.Round(ago * SimConstants.TickRate);
    }
    // --rolled m: the engine's wheels turned as if it had rolled that far (its drivers and rods, SceneArt.Gear).
    scene.Rolled = Opt(args, "--rolled", 0);
    scene.Build(mesh, train, camera.Position);
    // How long a frame's scene takes to build on the CPU, warm (the first build cooks the kit's pieces).
    var buildClock = Stopwatch.StartNew();
    int builds = (int)Opt(args, "--builds", 5);
    for (int b = 0; b < builds; b++)
        scene.Build(mesh, train, camera.Position);
    double buildMs = buildClock.Elapsed.TotalMilliseconds / builds;
    // --lantern: a hand lamp held just under the eye (the scene is eye-relative), the light you'd have in a dark car.
    if (args.Contains("--lantern"))
        mesh.PointLights.Add(new PointLight(new System.Numerics.Vector3(0.15f, -0.35f, 0), DarkTerritory.Game.Palette.LampAmber * 1.6f, 6));
    // --dawn t: the dawn that far up (0 night .. 1 dawn; look.json atmosphere.dawn).
    var lighting = Views.Lighting(train, look, (float)Opt(args, "--dawn", 0));
    if (args.Contains("--emergency") || args.Contains("--headlamp-out"))
        lighting.LampRange = 0.01f; // emergency lighting (or the lamp out): no light from the headlamp
    if (route is not null)
    {
        lighting.FogDensity = Views.FogDensity(route, train);
        lighting.Wetness = route.Weather.Wet ? 1 : 0;
        if (look?.Tuning.Atmosphere.Wind is { } wind)
            (lighting.Wind, lighting.Gusts) = (wind.Of(route.Weather.Wind), wind.Gusts);
    }
    // --wind w: a night that windy (0..1, the route weather's), --time t: at that second of it (the foliage's sway).
    if (args.Contains("--wind") && look?.Tuning.Atmosphere.Wind is { } windTuning)
        (lighting.Wind, lighting.Gusts) = (windTuning.Of(Opt(args, "--wind", 0)), windTuning.Gusts);
    if (args.Contains("--time"))
        lighting.Time = Opt(args, "--time", 0);
    // --wet: a wet night whatever the route's (its rain sheen on what faces the sky), to look the rain over.
    if (args.Contains("--wet"))
        lighting.Wetness = 1;
    // --cold c: a night that cold (0..1, the route weather's): its frost here, its breath in the scene (GreyboxScene.Cold).
    lighting.Frost = look?.Tuning.Atmosphere.Cold.Frost(scene.Cold) ?? 0;
    // --gathering g: the Choir's cold on the frame too, as the app has it (Look.Chill).
    if (look is not null)
        lighting = look.Chill(lighting, GreyboxScene.ChoirCold(scene.ChoirGathering));
    // --fog d: a thinner (or thicker) night than the route's, to look the lie of the land over.
    if (args.Contains("--fog"))
        lighting.FogDensity = (float)Opt(args, "--fog", lighting.FogDensity);
    if (strandedAt >= 0)
        Views.CinematicFog(ref lighting, Views.StrandedDistance(train, outro, strandedAt));
    // --survey: a flat, bright, clear light for reading the land's shape (the curves, the grades, the cuttings): a
    // designer's view of a generated line, not the game's night.
    if (args.Contains("--survey"))
    {
        lighting.MoonDirection = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.4f, 0.8f, 0.3f));
        lighting.MoonColour = new System.Numerics.Vector3(1, 0.97f, 0.9f);
        lighting.MoonStrength = 2.2f;
        lighting.Ambient = 0.55f;
        lighting.FogColor = new System.Numerics.Vector3(0.62f, 0.64f, 0.66f);
        lighting.FogDensity = args.Contains("--fog") ? lighting.FogDensity : 0.0012f;
        lighting.Wetness = 0;
    }
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor);
    PngWriter.Write(output, pixels, width, height, scale);
    return new
    {
        path = Path.GetFullPath(output),
        view,
        trainAt = Math.Round(at, 1),
        // --site: where its cranes' hooks hang (the facility's own and the yard's), by line distance and offset.
        cranes = site?.Cranes.Select(c => new { line = Math.Round(GreyboxScene.NearestDistance(line, c.HookAt, site.Feature.Start + 300), 1), castings = c.Castings.Length }),
        device = gpu.DeviceName,
        triangles = renderer.Stats.Triangles,
        draws = renderer.Stats.Draws,
        lights = renderer.Stats.Lights,
        buildMs = Math.Round(buildMs, 2),
        width = width * scale,
        height = height * scale,
        ms = clock.ElapsedMilliseconds,
        paths = junction >= 0 ? train.Rakes.Select(r => r.Path).ToArray() : null,
    };
}

// The art pipeline's validator (pipeline plan, "Automation and QA"): every kit piece against its class's triangle
// budget. Exit code 1 when anything is over, so CI can hold the line.
static int ArtCheck(TrainTuning t, string content, string[] args)
{
    var look = DarkTerritory.Game.Look.Load(content);
    var rows = DarkTerritory.Game.Art.ArtCatalog.Entries(look, t).Select(e =>
    {
        var piece = e.Make();
        return new { piece = e.Name, triangles = piece.Triangles, budget = e.Class.MaxTriangles, @class = e.Class.Name, over = piece.Triangles > e.Class.MaxTriangles };
    }).ToList();
    var textures = look.Textures.Select(x => x.Name).ToHashSet();
    Print(new { pieces = rows, textures = textures.Count, over = rows.Where(r => r.over).Select(r => r.piece) });
    return rows.Any(r => r.over) ? 1 : 0;
}

// A kit piece on a turntable (pipeline plan: "an in-engine turntable viewer with era-mode toggle"): four quarters,
// lit by a lantern beside the camera and the moon, in thin fog. --ps2 for the era comparison, --greybox for flat.
static object ArtShow(TrainTuning t, string content, string name, string[] args)
{
    var look = Looked(content, args);
    var entry = DarkTerritory.Game.Art.ArtCatalog.Entries(look, t).FirstOrDefault(e => e.Name == name)
        ?? throw new ArgumentException($"no piece '{name}' (known: {string.Join(", ", DarkTerritory.Game.Art.ArtCatalog.Entries(look, t).Select(e => e.Name))})");
    var piece = entry.Make();
    var (min, max) = DarkTerritory.Game.Art.ArtCatalog.Bounds(piece);
    var centre = (min + max) / 2;
    float radius = Math.Max(0.5f, (max - min).Length() / 2);
    int w = (int)Opt(args, "--width", 480), h = (int)Opt(args, "--height", 270);
    float fov = (float)Opt(args, "--fov", 50);
    double dist = radius / Math.Sin(fov * Math.PI / 360) * Opt(args, "--zoom", 0.65);
    using var gpu = new GpuContext("dt art show");
    using var renderer = new GreyboxRenderer(gpu, w, h);
    look?.Dress(renderer);
    if (args.Contains("--ps2"))
        renderer.Post = renderer.Post with { Ps2 = true };
    var sheet = new byte[w * 2 * h * 2 * 4];
    double start = Opt(args, "--yaw", 35) * Math.PI / 180;
    for (int q = 0; q < 4; q++)
    {
        double yaw = start + q * Math.PI / 2, pitch = Opt(args, "--pitch", 15) * Math.PI / 180;
        var target = new Double3(centre.X, centre.Y, centre.Z);
        var eye = target + new Double3(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch)) * dist;
        var camera = Camera.LookAt(eye, target, fov);
        var mesh = new MeshBuilder();
        mesh.Instances.Add(new MeshInstance(piece, System.Numerics.Matrix4x4.CreateTranslation(-(float)eye.X, -(float)eye.Y, -(float)eye.Z)));
        // A dark floor under it, and a lantern over the viewer's shoulder.
        var floor = new System.Numerics.Vector3(0, min.Y - 0.01f, 0) - new System.Numerics.Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
        float f = radius * 4;
        mesh.Quad(floor + new System.Numerics.Vector3(-f, 0, f), floor + new System.Numerics.Vector3(f, 0, f), floor + new System.Numerics.Vector3(f, 0, -f), floor + new System.Numerics.Vector3(-f, 0, -f), DarkTerritory.Game.Palette.Charcoal * 0.5f);
        var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(camera.Forward, System.Numerics.Vector3.UnitY));
        mesh.PointLights.Add(new PointLight(right * (float)(dist * 0.35) + new System.Numerics.Vector3(0, radius * 0.6f, 0), DarkTerritory.Game.Palette.LampAmber * 2.4f, (float)dist * 2.2f));
        mesh.PointLights.Add(new PointLight(-right * (float)(dist * 0.6) + new System.Numerics.Vector3(0, radius, 0), new System.Numerics.Vector3(0.25f, 0.3f, 0.4f), (float)dist * 2.5f));
        var light = look?.Apply(FrameLighting.Night) ?? FrameLighting.Night;
        light.FogDensity = (float)Opt(args, "--fog", 0.004);
        light.LampRange = 0.01f;
        light.Time = 0.37;
        var px = renderer.Render(mesh, camera, light, light.FogColor);
        int ox = q % 2 * w, oy = q / 2 * h;
        for (int y = 0; y < h; y++)
            px.AsSpan(y * w * 4, w * 4).CopyTo(sheet.AsSpan(((oy + y) * w * 2 + ox) * 4));
    }
    string output = Str(args, "--out", $"out/shots/art/{name}.png");
    PngWriter.Write(output, sheet, w * 2, h * 2, (int)Opt(args, "--scale", 1));
    return new
    {
        path = Path.GetFullPath(output),
        piece = name,
        triangles = piece.Triangles,
        budget = entry.Class.MaxTriangles,
        @class = entry.Class.Name,
        size = new[] { Math.Round(max.X - min.X, 2), Math.Round(max.Y - min.Y, 2), Math.Round(max.Z - min.Z, 2) }
    };
}

// A creature's clip as a contact sheet: --frames stills evenly through it (the last one short of the loop's end, which
// is its first again), lantern-lit from one side, to look at its motion headless (GDD §31: the pops, the eases).
static object ArtClip(string content, string name, string clip, string[] args)
{
    var look = DarkTerritory.Game.Look.Load(content);
    var art = look.Art.Creatures;
    var model = art.Get(name) ?? throw new ArgumentException($"no creature '{name}' (known: {string.Join(", ", DarkTerritory.Game.Art.CreatureArt.Names)})");
    var c = model.Clip(clip) ?? throw new ArgumentException($"{name} has no clip '{clip}' (it has: {string.Join(", ", model.Clips.Keys)})");
    int frames = (int)Opt(args, "--frames", 8), cols = Math.Min(frames, 4), rows = (frames + cols - 1) / cols;
    int w = (int)Opt(args, "--width", 480), h = (int)Opt(args, "--height", 270);
    // Where to look (model space; the model faces -Z), from how far and which way round.
    var at = Str(args, "--at", "0,0.9,0").Split(',').Select(double.Parse).ToArray();
    var target = new Double3(at[0], at[1], at[2]);
    double dist = Opt(args, "--dist", 4), yaw = Opt(args, "--yaw", 60) * Math.PI / 180, pitch = Opt(args, "--pitch", 15) * Math.PI / 180;
    var eye = target + new Double3(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), -Math.Cos(yaw) * Math.Cos(pitch)) * dist;
    var camera = Camera.LookAt(eye, target, (float)Opt(args, "--fov", 50));
    var e = new System.Numerics.Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
    using var gpu = new GpuContext("dt art clip");
    using var renderer = new GreyboxRenderer(gpu, w, h);
    look.Dress(renderer);
    var sheet = new byte[w * cols * h * rows * 4];
    double duration = c.Duration;
    bool loop = !args.Contains("--once");
    for (int i = 0; i < frames; i++)
    {
        double time = loop ? duration * i / frames : duration * i / Math.Max(1, frames - 1);
        var mesh = new MeshBuilder { Style = look.Style };
        mesh.SurfaceOrigin = e;
        float f = 40;
        mesh.Quad(new System.Numerics.Vector3(-f, 0, f) - e, new System.Numerics.Vector3(f, 0, f) - e, new System.Numerics.Vector3(f, 0, -f) - e, new System.Numerics.Vector3(-f, 0, -f) - e, DarkTerritory.Game.Palette.Charcoal * 0.5f);
        var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(camera.Forward, System.Numerics.Vector3.UnitY));
        // (Close in on something small, a moth, the lights come in with the camera: scaled to the distance, round the target.)
        float near = (float)Math.Min(1, dist / 2);
        var anchor = near < 1 ? new System.Numerics.Vector3((float)target.X, (float)target.Y, (float)target.Z) - e : default;
        mesh.PointLights.Add(new PointLight(anchor + (right * (float)(dist * 0.4) + new System.Numerics.Vector3(0, 1.2f, 0)) * near, DarkTerritory.Game.Palette.LampAmber * 2.2f, (float)dist * 2.5f));
        mesh.PointLights.Add(new PointLight(anchor + (-right * (float)(dist * 0.6) + new System.Numerics.Vector3(0, 2f, 0)) * near, new System.Numerics.Vector3(0.25f, 0.3f, 0.4f), (float)dist * 2.5f));
        // (--lift: the model raised off the floor, for one whose origin isn't at its feet: the Stoker's is the firebox door.)
        // (--variant n: one of the model's variants, a Soot Child's black eyes (1) or a real child's (0).)
        var placed = System.Numerics.Matrix4x4.CreateTranslation(new System.Numerics.Vector3(0, (float)Opt(args, "--lift", 0), 0) - e);
        // (--tool tool_crowbar: the crew with a hand tool in their fist, as SceneArt hangs it, for the clip's pose by its name.)
        if (Str(args, "--tool", "") is { Length: > 0 } tool && name == "crew"
            && Enum.TryParse<DarkTerritory.Game.Art.CrewPose>(clip.Replace("_idle", "").Replace("_", ""), true, out var pose))
            art.Crewmate(mesh, placed, pose, pose is DarkTerritory.Game.Art.CrewPose.Swing or DarkTerritory.Game.Art.CrewPose.GetUp
                    or DarkTerritory.Game.Art.CrewPose.TakeDown ? time : time - ((int)Opt(args, "--variant", 0) & 7) * 0.41, (int)Opt(args, "--variant", 0),
                inHand: DarkTerritory.Game.Art.PropArt.Of(look).Get(tool));
        else
            art.Draw(mesh, name, clip, time, loop, placed, (int)Opt(args, "--variant", 0));
        var light = look.Apply(FrameLighting.Night);
        light.FogDensity = 0.004f;
        light.LampRange = 0.01f;
        light.Time = 0.37;
        var px = renderer.Render(mesh, camera, light, light.FogColor);
        int ox = i % cols * w, oy = i / cols * h;
        for (int y = 0; y < h; y++)
            px.AsSpan(y * w * 4, w * 4).CopyTo(sheet.AsSpan(((oy + y) * w * cols + ox) * 4));
    }
    string output = Str(args, "--out", $"out/shots/clips/{name}-{clip}.png");
    PngWriter.Write(output, sheet, w * cols, h * rows, 1);
    return new { path = Path.GetFullPath(output), creature = name, clip, seconds = Math.Round(duration, 2), frames };
}

// Every animated model's every clip, frame by frame (the Look Review's animations tab): each clip as a strip of frames
// at --fps (12) across its length, a loop round to its start, a one-shot from first frame to last, from a three-quarter
// view at the model's own framing (CreatureArt.Framing), on a dark floor under a lamp. Strips to out/reel/<model>/<clip>.png
// and a manifest, reel.json. --only crew,gaunt picks models. The cut ones (clinger, weight, sleeper, hollow) are left out,
// and the survivors show only their own clips, not the crew's they share.
static object ArtReel(string content, string[] args)
{
    var look = DarkTerritory.Game.Look.Load(content);
    var art = look.Art.Creatures;
    string[] cut = ["clinger", "weight", "sleeper", "hollow"];
    var only = Str(args, "--only", "") is { Length: > 0 } o ? o.Split(',') : null;
    var clipsOnly = Str(args, "--clips", "") is { Length: > 0 } co ? co.Split(',') : null;
    int w = (int)Opt(args, "--width", 360), h = (int)Opt(args, "--height", 240), fps = (int)Opt(args, "--fps", 12);
    var props = DarkTerritory.Game.Art.PropArt.Of(look);
    string dir = Str(args, "--out", "out/reel");
    using var gpu = new GpuContext("dt art reel");
    using var renderer = new GreyboxRenderer(gpu, w, h);
    look.Dress(renderer);
    var manifest = new List<object>();
    foreach (var name in DarkTerritory.Game.Art.CreatureArt.Names)
    {
        if (cut.Contains(name) || only is not null && !only.Contains(name) || art.Get(name) is not { } model)
            continue;
        bool survivor = name.StartsWith("survivor_", StringComparison.Ordinal);
        foreach (var clipName in model.Clips.Keys.Order())
        {
            if (survivor && clipName is not ("idle" or "walk") || clipsOnly is not null && !clipsOnly.Contains(clipName))
                continue;
            // The crew's acts are drawn as the game draws them, with what's in their hands: the reload's powder, rammer and
            // pick, the hand lamp hung from the fist, the bar or the wrench (CreatureArt.Crewmate; a Look Review note).
            var act = name == "crew" && clipName != "idle"
                ? Enum.GetValues<DarkTerritory.Game.Art.CrewPose>().Cast<DarkTerritory.Game.Art.CrewPose?>().FirstOrDefault(p => DarkTerritory.Game.Art.CreatureArt.ClipOf(p!.Value) == clipName)
                : null;
            var held = clipName switch
            {
                "swing" or "smash" or "pry" => props.Get("tool_crowbar"),
                "mend" => props.Get("tool_wrench"),
                _ => null,
            };
            var lamp = clipName is "lantern" or "lantern_walk" ? props.Get("hand_lantern") : null;
            // The extinguisher hangs from the left hand by its handle (the sim carries it there, Bodies.Carry): without it
            // the extinguisher clips are someone gesturing at nothing (a Look Review note on spray).
            var extinguisher = name == "crew" && clipName is "extinguish" or "spray" or "take_down" or "hang_up" ? props.Get("extinguisher") : null;
            float extTop = extinguisher?.Vertices.Max(v => v.Position.Y) ?? 0;
            int handL = model.Skeleton.IndexOf("hand_l"), handR = model.Skeleton.IndexOf("hand_r");
            // The crate held in front (Bodies.Carry), between the hands.
            var crate = name == "crew" && clipName is "carry" or "carry_walk" ? props.Get("stores_crate") : null;
            float crateTop = crate?.Vertices.Max(v => v.Position.Y) ?? 0;
            // A ladder for the climbs, its rungs (TrainKit.RungPitch) running down past them at the climb's pace, so the
            // hands and feet are seen on them (crew_clips.py: two rungs a 40-frame cycle, the hand on its rung at the start).
            float? rungAt = name == "crew" ? clipName is "climb" or "climb_carry" ? 1.94f : null : null;
            var c = model.Clip(clipName)!;
            bool loop = c.Loops;
            int frames = Math.Clamp((int)Math.Round(c.Duration * fps) + (loop ? 0 : 1), 4, 72);
            double Time(int i) => loop ? c.Duration * i / frames : c.Duration * i / Math.Max(1, frames - 1);
            // Framed on the clip itself: everywhere the creature reaches over it (a gaunt standing to its height or
            // folded down to smash, a hugger sprawled), three-quarters on to its face (the model faces -Z), the whole
            // of it held in frame.
            // What rises out of something (a Dragger's limb up over an eave, a Stoker up out of the firebox) stands on
            // a floor under its lowest reach; the rest stand on the ground, a leg's tip through it not counted.
            bool rises = name is "dragger" or "stoker";
            var joints = new List<System.Numerics.Vector3>();
            for (int i = 0; i < frames; i += Math.Max(1, frames / 12))
                foreach (var j in art.Joints(name, clipName, Time(i), loop))
                    joints.Add(rises ? j : j with { Y = MathF.Max(0, j.Y) });
            float floor = rises ? joints.Min(j => j.Y) - 0.05f : 0;
            var lo = joints.Aggregate(System.Numerics.Vector3.Min);
            var hi = joints.Aggregate(System.Numerics.Vector3.Max) + new System.Numerics.Vector3(0, 0.15f, 0);
            var centre = (lo + hi) / 2;
            // Back off along the view until every joint, padded out to the flesh on its bones, is inside the frustum.
            double yaw = 35 * Math.PI / 180, pitch = 10 * Math.PI / 180;
            var back = new System.Numerics.Vector3((float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(-Math.Cos(yaw) * Math.Cos(pitch)));
            var side = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.UnitY, back));
            var upward = System.Numerics.Vector3.Cross(back, side);
            double tanV = Math.Tan(25 * Math.PI / 180), tanH = tanV * w / h, dist = 0.3;
            float extent = (hi - lo).Length();
            foreach (var j in joints)
            {
                var d = j - centre;
                double near = System.Numerics.Vector3.Dot(d, back);
                dist = Math.Max(dist, near + (Math.Abs(System.Numerics.Vector3.Dot(d, side)) * 1.15 + 0.06 + extent * 0.08) / tanH);
                dist = Math.Max(dist, near + (Math.Abs(System.Numerics.Vector3.Dot(d, upward)) * 1.15 + 0.08 + extent * 0.1) / tanV);
            }
            var target = new Double3(centre.X, centre.Y, centre.Z);
            var eye = target + new Double3(back.X * dist, back.Y * dist, back.Z * dist);
            var camera = Camera.LookAt(eye, target, 50);
            var e = new System.Numerics.Vector3((float)eye.X, (float)eye.Y, (float)eye.Z);
            float size = hi.Y - lo.Y;
            var strip = new byte[w * frames * h * 4];
            for (int i = 0; i < frames; i++)
            {
                double time = Time(i);
                var mesh = new MeshBuilder { Style = look.Style };
                mesh.SurfaceOrigin = e;
                float f = 40;
                mesh.Quad(new System.Numerics.Vector3(-f, floor, f) - e, new System.Numerics.Vector3(f, floor, f) - e, new System.Numerics.Vector3(f, floor, -f) - e, new System.Numerics.Vector3(-f, floor, -f) - e, DarkTerritory.Game.Palette.Charcoal * 0.5f);
                var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(camera.Forward, System.Numerics.Vector3.UnitY));
                var anchor = new System.Numerics.Vector3((float)target.X, (float)target.Y, (float)target.Z) - e;
                // A lantern's key light from beside the camera, off to its right and above; a cold fill from its left; a
                // rim from behind to put an edge on dark clothes and hides against the dark. All of them as far off as the
                // camera, so a limb reached toward one isn't blown out against the rest.
                var toEye = -anchor;
                float reach = (float)dist * 3f;
                mesh.PointLights.Add(new PointLight(anchor + toEye + right * (float)(dist * 0.5) + new System.Numerics.Vector3(0, (float)dist * 0.4f, 0), DarkTerritory.Game.Palette.LampAmber * 3.4f, reach));
                mesh.PointLights.Add(new PointLight(anchor + toEye * 0.8f - right * (float)(dist * 0.8) + new System.Numerics.Vector3(0, (float)dist * 0.2f, 0), new System.Numerics.Vector3(0.35f, 0.42f, 0.56f), reach));
                mesh.PointLights.Add(new PointLight(anchor - toEye * 0.8f + new System.Numerics.Vector3(0, (float)dist * 0.6f, 0), new System.Numerics.Vector3(0.5f, 0.56f, 0.7f), reach));
                if (act is { } pose)
                    art.Crewmate(mesh, System.Numerics.Matrix4x4.CreateTranslation(-e), pose, time, 0, inHand: held, hanging: lamp);
                if (crate is not null && handL >= 0 && handR >= 0)
                {
                    var js = art.Joints(name, clipName, time, loop).ToArray();
                    var mid = (js[handL] + js[handR]) * 0.5f;
                    mesh.Append(crate, System.Numerics.Matrix4x4.CreateTranslation(mid - new System.Numerics.Vector3(0, crateTop * 0.6f, 0.08f) - e));
                }
                if (rungAt is { } top)
                {
                    var k = new DarkTerritory.Game.Art.Kit(look, mesh);
                    k.Use("rust_heavy", DarkTerritory.Game.Palette.IronGrey, 0.8f, 0.3f);
                    float shift = (float)(time / c.Duration * 2 * DarkTerritory.Game.Art.TrainKit.RungPitch);
                    float z = -0.34f;
                    foreach (int sx in new[] { -1, 1 })
                        k.Box(new System.Numerics.Vector3(sx * 0.27f - 0.02f, 0, z - 0.02f) - e, new System.Numerics.Vector3(sx * 0.27f + 0.02f, 2.6f, z + 0.02f) - e);
                    for (int r = -8; r <= 3; r++)
                    {
                        float y = top - shift + r * DarkTerritory.Game.Art.TrainKit.RungPitch;
                        if (y is > 0.05f and < 2.55f)
                            k.Rod(new System.Numerics.Vector3(-0.27f, y, z) - e, new System.Numerics.Vector3(0.27f, y, z) - e, 0.016f);
                    }
                }
                if (extinguisher is not null && handL >= 0)
                {
                    var hand = art.Joints(name, clipName, time, loop).ElementAt(handL);
                    mesh.Append(extinguisher, System.Numerics.Matrix4x4.CreateTranslation(hand - new System.Numerics.Vector3(0, extTop - 0.02f, 0) - e));
                }
                else
                    art.Draw(mesh, name, clipName, time, loop, System.Numerics.Matrix4x4.CreateTranslation(-e));
                var light = look.Apply(FrameLighting.Night);
                light.FogDensity = 0.004f;
                light.LampRange = 0.01f;
                light.Time = 0.37;
                var px = renderer.Render(mesh, camera, light, light.FogColor);
                for (int y = 0; y < h; y++)
                    px.AsSpan(y * w * 4, w * 4).CopyTo(strip.AsSpan((y * w * frames + i * w) * 4));
            }
            Directory.CreateDirectory(Path.Combine(dir, name));
            PngWriter.Write(Path.Combine(dir, name, $"{clipName}.png"), strip, w * frames, h, 1);
            manifest.Add(new { model = name, clip = clipName, seconds = Math.Round(c.Duration, 2), frames, fps = loop ? frames / Math.Max(0.1, c.Duration) : (frames - 1) / Math.Max(0.1, c.Duration), loop });
        }
    }
    File.WriteAllText(Path.Combine(dir, "reel.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    return new { dir = Path.GetFullPath(dir), clips = manifest.Count, width = w, height = h };
}

// A figure's clips checked for limbs through its body (Art.Clearance; a Look Review note: "arms through body, legs
// through coat"): each clip's worst overlap per pair and when, deepest first; a pair over --limit (m) is a failure. A hand
// meant to be on a knee is let through with --allow clip:pair.
static object ArtClearance(string content, string[] args)
{
    var look = DarkTerritory.Game.Look.Load(content);
    string name = Str(args, "--only", "crew");
    var clipsOnly = Str(args, "--clips", "") is { Length: > 0 } co ? co.Split(',') : null;
    float limit = (float)Opt(args, "--limit", DarkTerritory.Game.Art.Clearance.Touching);
    var allow = Str(args, "--allow", "") is { Length: > 0 } al ? al.Split(',').ToHashSet() : [];
    // The crew's figures by their measured sizes; anything else (or --mesh) by capsules fitted to its own mesh.
    var rows = name is "crew" or "husk" && !args.Contains("--mesh")
        ? DarkTerritory.Game.Art.Clearance.Check(look.Art.Creatures, name, clipsOnly)
        : DarkTerritory.Game.Art.Clearance.Mesh(look.Art.Creatures.Get(name) ?? throw new ArgumentException($"no model {name}"), clipsOnly);
    var failing = rows.Where(r => r.Depth > limit && !allow.Contains($"{r.Clip}:{r.Pair}")).ToList();
    return new
    {
        model = name,
        limit,
        failing = failing.Count,
        worst = failing.Take(60).Select(r => new { clip = r.Clip, pair = r.Pair, depth = Math.Round(r.Depth, 3), at = Math.Round(r.At, 2) }),
        allowed = rows.Where(r => r.Depth > limit && allow.Contains($"{r.Clip}:{r.Pair}")).Select(r => $"{r.Clip}:{r.Pair} {r.Depth:0.000}"),
    };
}

// The art pass's surfaces (T39, look.json), unless --greybox asks for flat colour to compare against.
static DarkTerritory.Game.Look? Looked(string content, string[] args) => args.Contains("--greybox") ? null : DarkTerritory.Game.Look.Load(content);

// The front end as the game draws it (T30): a screen of the menus over the yard, at the game's 480x270. The campaign
// screens use a demo slot (a few nights in, some scrip) in a scratch directory unless --saves names real ones.
static object MenuShot(TrainTuning t, string content, string[] args)
{
    var (menu, screen) = DemoMenu(content, args);
    var line = RailLine.Load(Path.Combine(content, "lines", "test-loop.json"));
    var standing = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 6, 1)), line, 1200);
    var view = Views.Get("trackside", standing);
    var mesh = new MeshBuilder();
    var look = Looked(content, args);
    new GreyboxScene { Time = 0.37, Look = look }.Build(mesh, standing, view.Position);
    var light = Views.Lighting(standing, look);
    using var gpu = new GpuContext("dt screenshot");
    using var renderer = new GreyboxRenderer(gpu, 480, 270);
    look?.Dress(renderer);
    var overlay = new Overlay();
    menu.Draw(overlay, renderer.Width, renderer.Height);
    string output = Str(args, "--out", $"out/shots/menu-{screen.ToString().ToLowerInvariant()}.png");
    PngWriter.Write(output, renderer.Render(mesh, view, light, light.FogColor, overlay), renderer.Width, renderer.Height, (int)Opt(args, "--scale", 2));
    return new { path = Path.GetFullPath(output), screen = screen.ToString(), items = menu.Items.Select(i => i.Label) };
}

/// <summary>The front end on <c>--menu</c>'s screen, with a demo campaign slot unless <c>--saves</c> names real ones.</summary>
static (DarkTerritory.Game.FrontEnd Menu, DarkTerritory.Game.Screen Screen) DemoMenu(string content, string[] args)
{
    var screen = Enum.Parse<DarkTerritory.Game.Screen>(Str(args, "--menu", "title"), ignoreCase: true);
    var ct = DataFile.Load<DarkTerritory.Sim.Campaign.CampaignTuning>(Path.Combine(content, DarkTerritory.Sim.Campaign.CampaignTuning.File));
    var rt = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
    string dir = Str(args, "--saves", Path.Combine(Path.GetTempPath(), "dt-menu-shot"));
    var saves = new DarkTerritory.Game.SaveSlots(dir, ct.SaveSlots);
    if (!args.Contains("--saves"))
    {
        var demo = DarkTerritory.Sim.Campaign.Campaign.New(ct, 1, "The Night Shift", 7) with
        {
            Cars = 5,
            Scrip = 2350,
            Runs = 6,
            History = [new DarkTerritory.Sim.Campaign.RunLog(6, "frontier:3", DarkTerritory.Sim.Run.RunEnd.Delivered, 812, 0, 2350)],
            Upgrades = ["lampBrightness"],
        };
        saves.Save(demo);
        saves.Delete(2);
        saves.Delete(3);
    }
    var menu = new DarkTerritory.Game.FrontEnd(ct, rt, saves, Path.Combine(dir, "settings.json"), () => 7, EditionTuning.Load(content));
    menu.DefaultPlayerName = "Nick";
    // --menu profile (note 293): a tally as a few nights' crews would leave it, one badge not given yet.
    menu.Profile = new DarkTerritory.Game.PlayerProfile.Data
    {
        Commendations = new() { ["Came Back For Me"] = 3, ["Held the Switch"] = 1, ["Kept the Fire"] = 5, ["Brought Them Home"] = 2 },
    };
    menu.StillsFolder = "C:/Users/Nick/AppData/Local/DarkTerritory/bookmarks";
    menu.Music = DarkTerritory.Sim.Music.MusicManifest.Load(content).Tracks;
    // The join screen's list, as a crowded evening has it: games on the network (pings as measured) and public lobbies off
    // a platform search (the fake's, its pings estimated from where each host is).
    if (screen == DarkTerritory.Game.Screen.Join)
        menu.Games = DemoLobbies(menu.Protocol, DarkTerritory.Game.NetPlaySession.CrewCap(content));
    if (screen is DarkTerritory.Game.Screen.Fortress or DarkTerritory.Game.Screen.Upgrades or DarkTerritory.Game.Screen.Stores)
        menu.ShowFortress((int)Opt(args, "--slot", 1));
    // --menu night|leave (note 292): the in-night menu over a night hosted on the network for --others n (3), or with
    // --joined, someone else's.
    if (screen is DarkTerritory.Game.Screen.Night or DarkTerritory.Game.Screen.Leave)
        menu.OpenNight(new(Hosting: !args.Contains("--joined"), Others: (int)Opt(args, "--others", 3), JoinAt: "192.168.1.20:27960"));
    menu.Show(screen);
    for (int i = 0; i < (int)Opt(args, "--down", 0); i++)
        menu.Down();
    return (menu, screen);
}

/// <summary>
/// A join screen's worth of public games: three on the network (one at the crew cap, shown FULL), and off a (fake) Steam
/// search two open lobbies; a third at the cap is shut, so the search doesn't find it (note 254), and a private one isn't listed.
/// </summary>
static IReadOnlyList<DarkTerritory.Game.ListedGame> DemoLobbies(int protocol, int cap)
{
    Ballast.Net.LanGame Lan(string ip, string host, string name, int aboard, string tier, double ping, int protocol) =>
        new(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ip), DarkTerritory.Game.NetPlaySession.DefaultPort), host, $"{tier.ToUpperInvariant()}:7, 6 CARS, IN THE YARD", aboard, protocol,
            DarkTerritory.Game.NetPlaySession.Game)
        { Name = name, Max = cap, Tier = tier, PingMs = ping };
    var cloud = new Ballast.Online.FakeOnline();
    var me = cloud.SignIn("me");
    var hosts = new (string Name, string Run, string Tier, (double, double) Where, Ballast.Online.LobbyVisibility Visibility, int Crew)[]
    {
        ("PRIYA", "PRIYA'S RUN", "DeadLines", (30, 34), Ballast.Online.LobbyVisibility.Public, 3),
        ("hollowman", "NO SLEEP TILL HOLLIN", "DeepTerritory", (90, 110), Ballast.Online.LobbyVisibility.Public, cap),
        ("ash", "LOCALS ONLY", "Local", (60, 20), Ballast.Online.LobbyVisibility.Public, 5),
        ("secret", "SECRET RUN", "Frontier", (5, 5), Ballast.Online.LobbyVisibility.FriendsOnly, 2),
    };
    var lobbies = new List<Ballast.Online.Lobby>();
    foreach (var h in hosts)
    {
        var lobby = Ballast.Online.Lobby.Host(cloud.SignIn(h.Name, h.Where), DarkTerritory.Game.NetPlaySession.Game, protocol, cap, h.Visibility,
            new Dictionary<string, string>
            {
                [DarkTerritory.Game.NetPlaySession.NameKey] = h.Run,
                [DarkTerritory.Game.NetPlaySession.TierKey] = h.Tier,
                [DarkTerritory.Game.NetPlaySession.RunKey] = $"{h.Tier.ToUpperInvariant()}:12, 6 CARS, IN THE YARD",
            });
        lobby.Poll();
        DarkTerritory.Game.NetPlaySession.Advertise(lobby, h.Crew, cap);
        lobbies.Add(lobby);
    }
    using var browser = new DarkTerritory.Game.LobbyBrowser(lan: null, me, protocol);
    var events = new List<Ballast.Online.OnlineEvent>();
    browser.Poll(0, events, search: true);
    me.Poll(events);
    browser.Poll(0, events, search: false);
    Ballast.Net.LanGame[] lan = [Lan("192.168.1.20", "nick-pc", "THE NIGHT SHIFT", 2, "Frontier", 1.8, protocol),
        Lan("192.168.1.31", "sam", "SAM'S RUN", 1, "Frontier", 3.2, protocol + 1), Lan("192.168.1.44", "jo", "JO'S RUN", cap, "DeadLines", 2.4, protocol)];
    var games = lan.Select(DarkTerritory.Game.ListedGame.From).Concat(browser.Games);
    foreach (var l in lobbies)
        l.Dispose();
    return [.. games.OrderBy(g => g.PingMs ?? double.MaxValue)];
}

// A frame as the game draws it: a solo session stepped for a while, seen first person, with the HUD (T23).
static object HudShot(string content, string[] args)
{
    Hud.Tuning = DataFile.Load<HudTuning>(Path.Combine(content, HudTuning.File));
    int cars = (int)Opt(args, "--cars", 6);
    Route? generated = Str(args, "--route", "") is { Length: > 0 } spec
        ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars)
        : null;
    // --spectating (GDD App. D.10): a hosted night with a joiner who's died, seen as the joiner sees it: through the
    // host's eyes in the cab, whom they watch, with their HUD.
    // --vote (GDD v1.4 App. D.11): the night has its director, so the dead watcher is offered a ballot (--ballot implies it).
    // --lost (note 253): a joiner whose link has just gone, seen as it sees it: lost, and on its first try at getting back.
    // --lost --refused (note 254): back too late to a full crew, turned away: CREW FULL (2/2). --crew-full: the host at its cap.
    using var spectated = args.Contains("--spectating") ? Spectating(content, Str(args, "--route", "frontier:7"), cars, args.Contains("--vote") || args.Contains("--ballot"))
        : args.Contains("--lost") ? LostLink(content, Str(args, "--route", "frontier:7"), cars, refused: args.Contains("--refused"))
        : args.Contains("--crew-full") ? CrewFull(content, Str(args, "--route", "frontier:7"), cars) : null;
    IPlaySession session;
    if (spectated is { } pair)
    {
        session = pair.Watcher;
        // --ballot pick|cast (note 202): the ballot's screen with its second creature picked, or cast and locked by the host;
        // --headset: as a headset's panel says it (the stick, not the keys).
        Hud.Headset = args.Contains("--headset");
        if (Str(args, "--ballot", "") is { Length: > 0 } stage && pair.Watcher.Ballot is { Options.Count: > 1 } offered)
        {
            pair.Watcher.Picker.Key(2, offered.Options.Count);
            if (stage == "cast")
            {
                pair.Watcher.Picker.Cast();
                for (int t = 0; t < SimConstants.TickRate / 2; t++)
                {
                    pair.Host.Step(default);
                    pair.Watcher.Step(default);
                    Thread.Sleep(1);
                }
            }
        }
    }
    else
    {
        // --roof-warning tunnel|bend (note 260): up on a roof with a tunnel's mouth, or a bend taken too fast, coming
        // (--route; deepTerritory:2 if none: frontier:7 has no tunnel on its main line), warned.
        string roofWarning = Str(args, "--roof-warning", "");
        // --bend-warning [s] (note 265): in the cab, s seconds short of a bend the speed would derail the train on (0: on it),
        // warned. deepTerritory:2 if no --route: frontier:7 has no such bend.
        if (args.Contains("--bend-warning"))
            roofWarning = "bend-cab";
        var solo = roofWarning == "bend-cab"
            ? Staging.BendWarning(content, generated ?? DarkTerritory.Sim.LineGen.Routes.Generate(content, "deepTerritory:2", cars), cars, Array.IndexOf(args, "--bend-warning") is var bw && bw + 1 < args.Length && double.TryParse(args[bw + 1], System.Globalization.CultureInfo.InvariantCulture, out double bws) ? bws : 4)
            : roofWarning.Length > 0
            ? Staging.RoofWarning(content, generated ?? DarkTerritory.Sim.LineGen.Routes.Generate(content, "deepTerritory:2", cars), cars, roofWarning)
            : generated is null ? new PrototypeSession(content, Str(args, "--line", "test-loop"), cars) : new PrototypeSession(content, generated, cars, enemies: false);
        if (roofWarning.Length == 0)
            solo.Controls.Throttle = Opt(args, "--throttle", 0.6);
        // --hazards name: one of balance.json's hazard sets laid over the line (note 186), e.g. cold: the HUD's cold step (note 201).
        if (Str(args, "--hazards", "") is { Length: > 0 } hz)
            DarkTerritory.Sim.Net.HazardConditions.Apply(solo.Train.Line,
                DataFile.Load<BalanceTuning>(Path.Combine(content, BalanceTuning.File)).Combinations.HazardSets.First(h => h.Name == hz));
        for (int i = 0; i < (roofWarning.Length > 0 ? 1 : Opt(args, "--seconds", 6) * SimConstants.TickRate); i++)
            solo.Step(new PlayerIntent { LookPitch = i == 0 ? (float)Opt(args, "--pitch", 0) : 0, LookYaw = i == 0 ? (float)Opt(args, "--yaw", 0) : 0 });
        // --mend [t]: the repair kit in hand and a broken radio on the belt (GDD §23; note 201), t seconds into mending it.
        if (args.Contains("--mend"))
        {
            var bodies = solo.World.Bodies;
            var radio = bodies.All.First(b => b.Kind == DarkTerritory.Sim.Physics.BodyKind.Radio);
            (radio.Carrier, radio.Broken, radio.MendTicks) = (((IPlaySession)solo).PlayerId, true, (int)(Opt(args, "--mend", 0) * SimConstants.TickRate));
            if (bodies.All.FirstOrDefault(b => b.Kind == DarkTerritory.Sim.Physics.BodyKind.RepairKit) is { } kit)
                (kit.Carrier, kit.Locker) = (((IPlaySession)solo).PlayerId, -1);
        }
        // --lockers [NAME] (note 264): stood at a crew locker (the fitter's if none named), facing it, with what it holds on
        // its door's tag; --carrying kit: the repair kit in hands (from the fitter's), as the hotbar shows it.
        if (args.Contains("--lockers") && DarkTerritory.Sim.World.KitLocker(solo.Train) is { } kitLocker)
        {
            string name = Str(args, "--lockers", "");
            var shape = solo.Train.Frames[kitLocker.Car].Shape;
            var bay = shape.Lockers.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase), kitLocker.Bay);
            var front = bay.Front;
            solo.Player = solo.Player with
            {
                Parent = kitLocker.Car,
                Surface = DarkTerritory.Sim.Player.Surface.Deck,
                Position = new Double3(front.X + bay.Facing * 0.5, front.Y, front.Z + 0.3),
                // Facing the door (it faces +X on the left wall): a quarter turn, down at its handle and tag, along the row.
                Yaw = bay.Facing * Math.PI / 2 - 0.35,
                Pitch = -0.45,
                Velocity = default,
            };
            if (Str(args, "--carrying", "") == "kit" && solo.World.Bodies.All.FirstOrDefault(b => b.Kind == DarkTerritory.Sim.Physics.BodyKind.RepairKit) is { } kit)
                (kit.Carrier, kit.Locker) = (((IPlaySession)solo).PlayerId, -1);
        }
        // --cord (note 264): in the cab looking up at the whistle cord's handle, as the driver reaching for it does.
        if (args.Contains("--cord") && solo.Train.Frames[0].Shape.Interactables.FirstOrDefault(i => i.Kind == DarkTerritory.Sim.Train.InteractableKind.Whistle) is { Aim: > 0 } cord)
        {
            var eye = cord.Position + new Double3(-0.4, 0, 0.45);
            var to = cord.Position + Double3.Up * cord.Aim - (eye + Double3.Up * solo.Train.Dynamics.Tuning.Pick.EyeHeight);
            solo.Player = solo.Player with
            {
                Position = eye,
                Yaw = Math.Atan2(-to.X, -to.Z),
                Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z)),
                Velocity = default,
            };
        }
        session = solo;
    }
    int width = (int)Opt(args, "--width", 480), height = (int)Opt(args, "--height", 270), scale = (int)Opt(args, "--scale", 2);
    string output = Str(args, "--out", "out/shots/hud.png");
    // --report [derailed]: the night over, and its incident report as the run-end screen shows it (GDD v1.4 App. D.12).
    if (args.Contains("--report") && session.World.Run is { } over)
        over.MirrorReport(Staging.Report(session.World, Str(args, "--report", "") == "derailed" ? DarkTerritory.Sim.Run.RunEnd.Derailed : DarkTerritory.Sim.Run.RunEnd.CrewLost));
    var frames = session.InterpolatedFrames(1);
    var camera = session.EyeCamera(frames, 1, 0, 0);
    using var gpu = new GpuContext("dt screenshot --hud");
    // Drawn at the scaled size with the HUD's canvas laid over it, as the app draws it at a window's size: the prompt's fine
    // print (Hud.PromptScaleAt) only shows as it will with real pixels under it.
    using var renderer = new GreyboxRenderer(gpu, width * scale, height * scale) { OverlaySize = new System.Numerics.Vector2(width, height) };
    var mesh = new MeshBuilder();
    var look = Looked(content, args);
    look?.Dress(renderer);
    GreyboxScene Scene(IReadOnlyList<Crewmate> crew) => new()
    {
        Route = session.Route,
        Run = session.World.Run,
        Holdouts = session.World.Holdouts,
        Vehicles = session.Train.Vehicles,
        Bodies = session.World.Bodies.All,
        Time = 0.37,
        Look = look,
        Crew = crew,
    };
    // The rest of the crew, but not the one whose eyes these are (as the app draws it).
    Scene([.. session.Crew(frames, 1).Where(c => c.Id != session.Watching)]).Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
    var lighting = Views.Lighting(frames[0], look);
    if (session.Route is { } r)
    {
        lighting.FogDensity = Views.FogDensity(r, session.Train);
        lighting.Wetness = r.Weather.Wet ? 1 : 0;
        lighting.Frost = look?.Tuning.Atmosphere.Cold.Frost(r.Weather.Cold) ?? 0;
    }
    // --hit-marker [kill]: a blow of yours just landed (T121), the crosshair's marker for it (red for the kill).
    if (args.Contains("--hit-marker"))
        session.World.Hits.Add(new DarkTerritory.Sim.Combat.HitConfirm(1, (uint)session.HostTick, 1, DarkTerritory.Sim.Enemies.EnemyKind.Ribbit, session.PlayerId,
            DarkTerritory.Sim.Combat.HitSource.Melee, default, new Double3(0, 0, -1), Str(args, "--hit-marker", "") == "kill"));
    // The report's bookmarks (GDD v1.4 App. D.12): each still drawn from its camera as the app takes it, the staged crew in
    // view but for whoever's eyes it is, before the report that shows them.
    var stills = new BookmarkStills();
    List<string>? kept = null;
    if (args.Contains("--report") && session.World.Run?.Report is { Bookmarks.Count: > 0 } staged)
    {
        var figures = Staging.ReportCrew(session.Train);
        foreach (var b in staged.Bookmarks)
        {
            var shot = b.Kind == DarkTerritory.Sim.Run.BookmarkKind.Stranded
                ? Views.Stranded(session.Train, session.World.WreckTuning.Stranded, session.World.WreckTuning.Stranded.Seconds) : BookmarkStills.Of(b, frames);
            var still = new MeshBuilder();
            Scene([.. figures.Where(f => f.Id != b.Viewer).Select(f => DarkTerritory.Game.Art.CrewActs.Crewmate((byte)f.Id, f.State, session.World, frames))])
                .Build(still, session.Train.Line, frames, session.Train.Dynamics.Distance, shot.Position);
            stills.Keep(b, renderer.Render(still, shot, lighting, lighting.FogColor), width * scale, height * scale);
        }
        // --stills dir: the night's stills kept as the app keeps them past the run end (note 203), a folder for the night in dir.
        if (Str(args, "--stills", "") is { Length: > 0 } dir)
            kept = new BookmarkAlbum(dir, DateTime.Now, session.Route?.Name ?? "night").Save(staged, stills.Stills, session.World);
    }
    var hud = new Overlay();
    // --hurt [s]: just hit (GDD App. F.1's damage model; note 272), the red edge flash at strength s (a heavy hit is 1).
    if (args.Contains("--hurt"))
        Hud.StagedHurt = Array.IndexOf(args, "--hurt") is var hu && hu + 1 < args.Length
            && double.TryParse(args[hu + 1], System.Globalization.CultureInfo.InvariantCulture, out double hurt) ? hurt : 0.6;
    // --commend: the night's commendations shown under its report (App. D.12; awarding them isn't in the game yet).
    Hud.Build(hud, width, height, session, pixels: scale, commendations: args.Contains("--commend")
        ? [("Dave", UiStyle.Commendation.CameBackForMe, "Okafor"), ("Priya", UiStyle.Commendation.KeptTheFire, "Dave"),
            ("Okafor", UiStyle.Commendation.HeldTheSwitch, "Priya"), ("Dunmore", UiStyle.Commendation.LastOneStanding, "Dave")]
        : null, stills: stills.Stills);
    // --emote-wheel: the emote wheel held, the mouse leant toward the wave (note 298).
    if (args.Contains("--emote-wheel"))
    {
        var wheel = new EmoteWheel();
        wheel.Update(true, -60, 0);
        wheel.Draw(hud, width, height, Hud.PromptScaleAt(scale));
    }
    // --radio manifest|tally [s]: the fortress on the radio (GDD §9; note 178), staged from this night and a delivered report,
    // --radio-at s into the reading.
    if (Str(args, "--radio", "") is { Length: > 0 } reading)
    {
        var lines = reading == "tally"
            ? DarkTerritory.Sim.Run.Radio.Tally(Staging.Report(session.World, DarkTerritory.Sim.Run.RunEnd.Delivered))
            : DarkTerritory.Sim.Run.Radio.Manifest(session.World, [0, 1, 2, 3]);
        Hud.RadioCard(hud, width, height, lines, Opt(args, "--radio-at", 6), session.World.Run?.Tuning.Radio ?? new());
    }
    // --supplies: the supplies aboard (the director's decision of 2026-10-06; note 264), as I toggles it on.
    if (args.Contains("--supplies"))
        Hud.Supplies(hud, width, height, session);
    // --roster: the crew roster (T69) as Q shows it, with a staged crew: two heard, one not yet, and a Passenger among them.
    if (args.Contains("--roster"))
    {
        var (lines, heard) = Staging.Roster(session.Train, content);
        Hud.Roster(hud, width, height, lines, heard);
    }
    // --card: the generated line's route card over it; --overlay: the designer's overlay (linegen plan §9.7, §20.2).
    if (session.Route?.Plan is { } plan)
    {
        if (args.Contains("--card"))
            DarkTerritory.Game.LineGen.PlanHud.RouteCard(hud, width, height, plan, (int)Opt(args, "--page", 0), session.Train.Line,
                session.Train.Line.MainDistance(session.Train.Dynamics.Path, session.Train.Dynamics.Distance));
        if (args.Contains("--overlay"))
            DarkTerritory.Game.LineGen.PlanHud.Overlay(hud, width, height, session, plan);
    }
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, hud);
    PngWriter.Write(output, pixels, width * scale, height * scale, 1);
    return new
    {
        path = Path.GetFullPath(output),
        prompt = Hud.Prompt(session),
        // Note 285: the corner, what's in your hands or the cab lets you do.
        corner = Hud.Hints(session) is var (cornerHead, cornerLines) ? new { head = cornerHead, lines = cornerLines } : null,
        quads = hud.Count / 6,
        status = session.Status(),
        watching = session.Watching,
        bookmarks = session.World.Run?.Report?.Bookmarks.Select(b => new { b.Id, kind = b.Kind.ToString(), b.Viewer, b.Victim, b.Frame, still = stills.Stills.ContainsKey(b.Id) }),
        kept,
    };
}

// GDD v1.4 App. E.5 (note 177): a frame of the derailment film as the app plays it, cards and all. A hosted night with
// --crew (4) aboard (the rest bots), run --seconds (12) and derailed at --speed (20 m/s); then --film s seconds into the
// film's cut (after the first person and the replay). --plan prints the shot list instead of nothing extra. --skip-hold h:
// the skip held for the last h seconds before the frame (under wreck.json's skip.holdSeconds, so the prompt's fill shows; note 315).
static object FilmStill(string content, string[] args)
{
    int crew = (int)Opt(args, "--crew", 4), cars = (int)Opt(args, "--cars", 6);
    double filmAt = Opt(args, "--film", 0), skipHold = Opt(args, "--skip-hold", 0);
    using var session = NetPlaySession.HostGame(content, new SessionSetup(Route: Str(args, "--route", "frontier:7"), Cars: cars, Enemies: false),
        port: 0, bots: Math.Max(0, crew - 1));
    for (int i = 0; i < Opt(args, "--seconds", 12) * SimConstants.TickRate; i++)
    {
        session.Step(default);
        Thread.Sleep(1);
    }
    var hostWorld = session.Host!.World;
    hostWorld.Train.Dynamics.Velocity = Opt(args, "--speed", 20);
    hostWorld.Train.RefreshFrames();
    hostWorld.Derail("took the 45 km/h bend at 72 km/h, 27 km/h too fast");
    // The cut starts after this player's own first person, which is as long as the film says (App. E.2 step 1).
    double Want() => session.SequenceTuning.FirstPersonSeconds + session.SequenceTuning.ReplaySeconds + filmAt;
    var clock = Stopwatch.StartNew();
    while ((session.WreckSeconds < Want() || session.Film is null) && clock.Elapsed.TotalSeconds < 120)
    {
        if (session.WreckSeconds < Want())
            session.Step(skipHold > 0 && session.WreckSeconds >= Want() - skipHold && session.Film is not null
                ? new PlayerIntent { Actions = PlayerActions.Skip } : default);
        Thread.Sleep(1);
    }
    var t = session.SequenceTuning;
    var film = session.Film;
    if (film is null || film.CutAt(DerailSequence.FilmSeconds(t, session.WreckSeconds)) is not { } at)
        return new { error = "no film to show there", wreckSeconds = session.WreckSeconds, film = film?.CutLength };
    double recorded = at.Shot.At(at.Into);
    var frames = DerailSequence.FilmFrames(film, recorded, session.InterpolatedFrames(1));
    var camera = DerailSequence.FilmCamera(at.Shot, at.Into, film);
    int width = (int)Opt(args, "--width", 640), height = (int)Opt(args, "--height", 360), scale = (int)Opt(args, "--scale", 2);
    string output = Str(args, "--out", "out/shots/film.png");
    using var gpu = new GpuContext("dt screenshot --film");
    using var renderer = new GreyboxRenderer(gpu, width, height);
    var mesh = new MeshBuilder();
    var look = Looked(content, args);
    look?.Dress(renderer);
    new GreyboxScene
    {
        Route = session.Route,
        Vehicles = session.Train.Vehicles,
        Bodies = DerailSequence.FilmBodies(film, recorded),
        CutAway = DerailSequence.FilmCutAway(film, at.Shot, recorded, frames, camera.Position),
        Lights = DerailSequence.FilmLights(film, at.Shot, recorded, camera.Position),
        Time = 0.37,
        Look = look,
        Derailed = true,
    }.Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
    var lighting = Views.Lighting(frames[0], look);
    var hud = new Overlay();
    Hud.Build(hud, width, height, session);
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, hud);
    PngWriter.Write(output, pixels, width, height, scale);
    // --stills dir: each crewmate's derailment bookmark, their peak in the film (E.5, D.12), drawn as the app takes it, and
    // kept as the app keeps the night's stills (note 203).
    List<string>? kept = null;
    var peaks = new List<object>();
    if (Str(args, "--stills", "") is { Length: > 0 } dir && session.World.Run?.Report is { } report)
    {
        var stills = new BookmarkStills();
        var live = session.InterpolatedFrames(1);
        foreach (var (mark, from, peak) in stills.Due(session, live, 0, film))
        {
            if (peak is null)
                continue;
            var scene = new GreyboxScene { Route = session.Route, Vehicles = session.Train.Vehicles, Time = 0.37, Look = look };
            var shotFrames = DerailSequence.Stage(scene, peak, live);
            var still = new MeshBuilder();
            scene.Build(still, session.Train.Line, shotFrames, session.Train.Dynamics.Distance, from.Position);
            stills.Keep(mark, renderer.Render(still, from, lighting, lighting.FogColor), width, height, peak: true);
            peaks.Add(new { mark.Id, mark.Victim, recorded = Math.Round(peak.Recorded, 2), into = Math.Round(peak.Into, 2), peakAt = Math.Round(film.Peaks[mark.Victim].At, 2) });
        }
        kept = new BookmarkAlbum(dir, DateTime.Now, session.Route?.Name ?? "night").Save(report, stills.Stills, session.World);
    }
    return new
    {
        path = Path.GetFullPath(output),
        peaks,
        kept,
        shot = new { kind = at.Shot.Kind.ToString(), at.Shot.Subject, card = at.Shot.Card, into = Math.Round(at.Into, 2), recorded = Math.Round(recorded, 2) },
        cut = film.Cut.Select(s => new { kind = s.Kind.ToString(), s.Subject, real = Math.Round(s.Real, 2), from = Math.Round(s.From, 2), to = Math.Round(s.To, 2), s.Card }),
        cutLength = Math.Round(film.CutLength, 2),
        recordedSeconds = Math.Round(film.Recorded, 2),
        crew = film.Start.Players.Select(p => new { p.Id, p.Name, p.Role, speed = Math.Round(p.Velocity.Length, 1), peak = Math.Round(film.Peaks[p.Id].Score, 2) }),
    };
}

// A hosted night over loopback with one joiner, who dies once aboard and watches the host (App. D.10).
// A joiner whose link the host has just lost (note 253): the host stops answering for a moment, so it's still trying.
static SpectatedNight LostLink(string content, string route, int cars, bool refused = false)
{
    var host = NetPlaySession.HostGame(content, new SessionSetup(Route: route, Cars: cars, Enemies: false), port: 0);
    // --refused (note 254): a crew cap of two and a place held a second, so the joiner's back too late and turned away.
    if (refused)
        host.Host!.PlayerTuning = host.Host.PlayerTuning with
        {
            Crew = host.Host.PlayerTuning.Crew with { Cap = 2 },
            Rejoin = host.Host.PlayerTuning.Rejoin with { ReserveSeconds = 1 },
        };
    var at = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port);
    var joiner = NetPlaySession.Join(content, at, () => host.Step(default));
    for (int t = 0; t < SimConstants.TickRate; t++)
    {
        host.Step(default);
        joiner.Step(default);
        Thread.Sleep(1);
    }
    host.Host!.Drop((byte)joiner.PlayerId);
    if (refused)
    {
        // The place runs out, a newcomer has it, and the joiner dials back to a full crew.
        for (int t = 0; t < SimConstants.TickRate * 3 / 2; t++)
            host.Step(default);
        using var newcomer = NetPlaySession.Join(content, at, () => host.Step(default));
        for (int t = 0; t < SimConstants.TickRate * 2 && joiner.Link?.Refused is null; t++)
        {
            host.Step(default);
            newcomer.Step(default);
            joiner.Step(default);
            Thread.Sleep(1);
        }
        return new SpectatedNight(host, joiner);
    }
    for (int t = 0; t < 10; t++)
    {
        joiner.Step(default);
        Thread.Sleep(1);
    }
    return new SpectatedNight(host, joiner);
}

/// <summary>
/// <c>--hud --crew-full</c> (note 254): a hosted night in the yard at a crew cap of two, the host and a joiner aboard, seen
/// as the host sees it: the lobby panel's CREW FULL and the link's crew 2/2.
/// </summary>
static SpectatedNight CrewFull(string content, string route, int cars)
{
    var host = NetPlaySession.HostGame(content, new SessionSetup(Route: route, Cars: cars, Enemies: false), port: 0);
    host.Host!.PlayerTuning = host.Host.PlayerTuning with { Crew = host.Host.PlayerTuning.Crew with { Cap = 2 } };
    var joiner = NetPlaySession.Join(content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
    for (int t = 0; t < SimConstants.TickRate; t++)
    {
        host.Step(default);
        joiner.Step(default);
        Thread.Sleep(1);
    }
    // Shown as the host: the "watcher" is the host's own session.
    return new SpectatedNight(joiner, host);
}

static SpectatedNight Spectating(string content, string route, int cars, bool enemies = false)
{
    var host = NetPlaySession.HostGame(content, new SessionSetup(Route: route, Cars: cars, Enemies: enemies), port: 0);
    var watcher = NetPlaySession.Join(content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
    void Step(int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            host.Step(default);
            watcher.Step(default);
            Thread.Sleep(1);
        }
    }
    Step(SimConstants.TickRate);
    host.Host!.SetPlayerState((byte)watcher.PlayerId, watcher.Player with { Health = 0, Death = DeathCause.Mauled });
    Step(SimConstants.TickRate);
    return new SpectatedNight(host, watcher);
}

// The designer's editor: tuning and routes in a local web page (T18). --screenshot captures both pages headless.
static int Edit(string content, string[] args)
{
    using var server = new DarkTerritory.Editor.EditorServer(content, (int)Opt(args, "--port", 0));
    server.Start();
    if (Str(args, "--screenshot", "") is { Length: > 0 } shot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shot))!);
        var script = Path.Combine(Path.GetDirectoryName(content)!, "tools", "editor-shot.cjs");
        var psi = new ProcessStartInfo("node", [script, server.Url, Path.GetFullPath(shot)]) { RedirectStandardError = true };
        psi.Environment["NODE_PATH"] = Environment.GetEnvironmentVariable("NODE_PATH") ?? "/opt/node22/lib/node_modules";
        using var node = Process.Start(psi)!;
        string errors = node.StandardError.ReadToEnd();
        node.WaitForExit();
        return Print(new { tuning = Path.GetFullPath(shot.Replace(".png", "-tuning.png")), routes = Path.GetFullPath(shot.Replace(".png", "-routes.png")), exit = node.ExitCode, errors });
    }
    Console.WriteLine($"Dark Territory editor: {server.Url}  (Ctrl+C to stop)");
    Console.WriteLine("Edits save into content/ in place; a running game picks them up.");
    var done = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
    done.Wait();
    return 0;
}

// Renders a staged moment through the real mixer to a WAV, and measures every tell against the bed.
static object RenderAudio(string content, string[] args)
{
    if (Str(args, "--sound", "") is { Length: > 0 } sound)
        return RenderSound(content, sound, args);
    string scenario = Str(args, "--scenario", "chaos");
    if (Str(args, "--listener", "") == "all")
        return DarkTerritory.Game.Sound.AudioBench.Sweep(content, scenario, (int)Opt(args, "--cars", 20), Opt(args, "--speed", 22), Opt(args, "--seconds", 6),
            Str(args, "--space", "") is { Length: > 0 } everywhere ? everywhere : null);
    string output = Str(args, "--out", $"out/audio/{scenario}.wav");
    var clock = Stopwatch.StartNew();
    // A wreck renders the whole derailment sequence by default (note 170), the opera and the dead channel's laughing with it (E.6).
    double seconds = scenario == "wreck" ? 1 + DataFile.Load<DarkTerritory.Sim.Train.WreckTuning>(Path.Combine(content, DarkTerritory.Sim.Train.WreckTuning.File)).SequenceSeconds : 6;
    var (report, mix) = DarkTerritory.Game.Sound.AudioBench.Render(content, scenario, (int)Opt(args, "--cars", 20), Opt(args, "--speed", 22),
        (int)Opt(args, "--listener", 5), Opt(args, "--seconds", seconds), Str(args, "--space", "") is { Length: > 0 } space ? space : null,
        Str(args, "--track", "") is { Length: > 0 } track ? track : null);
    Ballast.Audio.Wav.Write(output, mix);
    // The picture of it: a spectrogram beside the WAV, for looking at bands without listening.
    string picture = Path.ChangeExtension(output, ".png");
    PngWriter.Write(picture, DarkTerritory.Game.Sound.Spectrogram.Render(mix, 800, 300), 800, 300, 1);
    return new { path = Path.GetFullPath(output), spectrogram = Path.GetFullPath(picture), report, ms = clock.ElapsedMilliseconds };
}

// dt audio render --sound <name>: one sound alone (a one-shot to its end, a loop for 4 s), with --param name=value held.
static object RenderSound(string content, string sound, string[] args)
{
    var parameters = new Dictionary<string, double>();
    for (int i = 0; i + 1 < args.Length; i++)
        if (args[i] == "--param" && args[i + 1].Split('=') is [var name, var value])
            parameters[name] = double.Parse(value);
    // --space tunnel: heard in that space (content/audio/spaces.json), its reverb and all.
    var (report, mix) = DarkTerritory.Game.Sound.AudioBench.RenderSound(content, sound, args.Contains("--seconds") ? Opt(args, "--seconds", 0) : null, parameters,
        Str(args, "--space", "") is { Length: > 0 } space ? space : null);
    string output = Str(args, "--out", $"out/audio/sound-{sound}.wav");
    Ballast.Audio.Wav.Write(output, mix);
    string picture = Path.ChangeExtension(output, ".png");
    PngWriter.Write(picture, DarkTerritory.Game.Sound.Spectrogram.Render(mix, 800, 300), 800, 300, 1);
    return new { path = Path.GetFullPath(output), spectrogram = Path.GetFullPath(picture), report };
}

// dt audio clerk [--line "Crew: Priya."]: the yard on the radio (note 240), a manifest and a tally said through the set at the
// voice's own pace, to a WAV and its spectrogram, with each line's turn and what the set broke up over.
static object RenderClerk(string content, string[] args)
{
    var audio = new DarkTerritory.Game.Sound.GameAudio(content) { Mixer = { Listener = Ballast.Audio.Listener.At(Double3.Zero, 0) } };
    audio.Bank.Samples.InlineBytes = long.MaxValue;
    var t = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)).Radio;
    string[] lines = Str(args, "--line", "") is { Length: > 0 } one ? [one] :
    [
        "Yard to consist. Manifest follows.", "Crew: Okafor.", "Crew: Priya.", "Crew: Halloran.", "Coal: 412.", "Powder and shot: 40.",
        "Cars: 6.", "Freight: grain, medicine.", "Gates open. Yard out.",
        "Yard clerk. Consist received. Tally follows.", "Cars delivered: 5. Cargo: 2450.", "Cars lost: 1.",
        "Reyes. Body recovered. Fee 350. Refund 263.", "Priya. Body not recovered. Fee 350.", "Mail: 90.",
        "Coal: 40. Powder and shot: 12. Repairs: 30.", "Net: 1158. Next.",
    ];
    var times = DarkTerritory.Sim.Run.Radio.Times(lines, t, audio.Clerk.Seconds);
    double length = DarkTerritory.Sim.Run.Radio.Length(lines, t, times), blockSeconds = (double)Ballast.Audio.Audio.Block / Ballast.Audio.Audio.SampleRate;
    var mix = new List<float>();
    var block = new float[Ballast.Audio.Audio.Block * 2];
    for (double at = 0; at < length; at += blockSeconds)
    {
        audio.Radio(lines, DarkTerritory.Sim.Run.Radio.Reading(lines, at, t, times).Lines);
        audio.Mixer.Render(block);
        mix.AddRange(block);
    }
    string output = Str(args, "--out", "out/audio/clerk.wav");
    var all = mix.ToArray();
    Ballast.Audio.Wav.Write(output, all);
    string picture = Path.ChangeExtension(output, ".png");
    PngWriter.Write(picture, DarkTerritory.Game.Sound.Spectrogram.Render(all, 800, 300), 800, 300, 1);
    return new
    {
        path = Path.GetFullPath(output),
        spectrogram = Path.GetFullPath(picture),
        seconds = Math.Round(length, 2),
        lines = lines.Select((l, i) => new
        {
            line = l,
            at = Math.Round(times.Take(i).Sum(), 2),
            said = Math.Round(audio.Clerk.Seconds(l), 2),
            brokeUp = audio.Clerk.Pieces(l).Where(p => p.Kind == DarkTerritory.Game.Sound.ClerkVoice.PieceKind.Breakup).Select(p => p.Text),
        }),
    };
}

static string Str(string[] args, string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

// Every enemy that much further into its phase (dt screenshot --later: a frame of its animation further on).
// --later s: every staged threat that much further on in its phase.
static List<DarkTerritory.Sim.Enemies.Enemy> Later(List<DarkTerritory.Sim.Enemies.Enemy> enemies, double seconds)
{
    if (seconds != 0)
        foreach (var e in enemies)
            e.Restore(e.Phase, e.PhaseSeconds + seconds, e.Health, e.Attached, e.Local, e.LineDistance, e.Lateral, e.Height, e.Extra, e.Extra2,
                e.Holding, e.GrabWindow);
    return enemies;
}

static double Opt(string[] args, string name, double fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1]) : fallback;
}

static int Print(object value)
{
    Console.WriteLine(JsonSerializer.Serialize(value, DataFile.Options));
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage: dt <command>        (mods in ./mods and the user's app data are laid over content/; --no-mods for the base game)
          mods                                     the mods found, their load order, and what each does to which file
          train table                              spec table (B.4–B.6) as produced by current tuning
          train stop <cars> [--from v] [--load l] [--grade g]
          train climb <cars> <grade%> [--from v] [--load l]
          boiler table                             spec B.6 burn, endurance, rebuild as produced by boiler tuning
          boiler run <cars> [--seconds t] [--throttle 0..1] [--fire-at p | --no-fireman] [--pressure p] [--firebox u] [--vent]
          line info <name> [--every m]             position/grade profile of content/lines/<name>.json
          line drive <name> [--cars n] [--start s] [--from v] [--throttle 0..1] [--seconds t]
          trailer [--route tier:seed] [--fps n] [--width w --height h] [--short]   the trailer cut from the game itself: frames to out/trailer, ffmpeg to trailer.mp4
          art clearance [--only crew] [--clips a,b] [--limit m] [--allow clip:pair,..]   a figure's clips checked for limbs through its body
          art reel [--only a,b] [--clips c,d] [--fps n] [--width w --height h]   every animated model's every clip as frame strips + reel.json in out/reel (the Look Review's animations)
          art clip <creature> <clip> [--frames n] [--at x,y,z --dist m --yaw deg --pitch deg] [--lift m] [--variant n] [--once]   a clip as a lit contact sheet
          screenshot [--view trackside|roof|cab|chase|ahead] [--line name] [--cars n] [--at s] [--car i] [--cut n]
                     [--cam s,lateral,height --target s,lateral,height --fov deg]   camera by line coordinates
                     [--width w] [--height h] [--scale k] [--out file.png] [--threats]   --threats stages one of each enemy
                     [--doll-at m]   with --threats: the Track Doll this far up the line (App. A.2: the lamp shows it at 200)
                     [--lurk-at m]   with --threats: a Car Hugger lurking beside the line this far ahead (App. A.3 LURK)
                     [--doors-open]   every door on the train open   [--coal u] fire on the grate   [--venting] the blow-off and safety valve blowing
                     [--eaten f]   the rear car this much eaten by a Car Hugger (0..1; 1 eaten through)
                     [--later s]   with --threats: every staged enemy s seconds further into what it's doing (frames of its animation)
                     [--route tier:seed [--coaling]]   a generated night; --coaling stops at its coaling tower, chute pouring
                     [--bodies]   crates, a lamp and a crewmate's body on the roofs, settled by the physics
                     [--emergency]  emergency lighting: the cars' lamps a dim red, no headlamp
                     [--lit]      every Holdout occupied, its lamp burning (GDD App. D)
                     [--ps2]      the era comparison mode   [--muzzle] the guns just fired   [--builds n] time n warm builds
                     [--integrity a,b,..] each car's condition, front to back (scars and damage states)
                     [--utility i,j]   those cars as utility cars, fitted out for the crew (bunks, stove, table)
                     [--cold c]   a night that cold (0..1): frost on what's outdoors, breath from every mouth
                     [--burnt car,s]   that car gutted by a fire out s seconds ago: charred, smouldering
                     [--route tier:seed --structure girder|truss|trestle|viaduct|causeway|retainingwall]   the night's first of the plan's structures of that type, the train on it, from off its side
                     [--route tier:seed --mail s]   at the night's first mail crane, car 2's door by it; s > 0: the bag caught s seconds ago (its snatch, the arms falling)
                     [--route tier:seed --site [--crank | --crane | --facility i|kind [--leak] [--settled]]]   stopped at a facility: crates out, the winch sled part-hauled (spec D); --crank: close on the cranks; --crane: a gantry crane's facility, a casting on the hook; --facility: the route's i-th
             [--route tier:seed --junction i [--diverge] [--through]]   at a switch, set for the branch, run in onto it
          art check                                every kit piece against its triangle budget (exit 1 if any is over)
          art show <piece> [--yaw deg] [--pitch deg] [--zoom k] [--ps2] [--greybox]   a piece on a turntable, to out/shots/art/
          screenshot --menu title|slots|fortress|upgrades|stores|quickNight|host|join|settings|credits|night|leave|profile [--down n] [--saves dir] [--others n] [--joined]
                     a screen of the front end over the yard, as the game draws it
          screenshot --hud [--emote-wheel] [--lost] [--route tier:seed] [--seconds t] [--throttle 0..1] [--pitch r] [--yaw r]
                     a solo session played for a few seconds, first person, with the HUD, at the game's 480x270
                     --report [derailed]: the run-end screen's incident report, its bookmark stills beside their lines
                     (GDD v1.4 App. D.12); --stills dir keeps them as the app does past the run end, a folder for the
                     night with night.txt (note 203)
          screenshot --film s [--crew n --speed v --route r] [--stills dir]   a frame of the derailment film s seconds into
                     its cut (note 177); --stills dir: each crewmate's bookmark, their peak in the film (E.5), kept as the app keeps them
                     --hazards clear|wet|cold|dark: a balance.json hazard set over the line; --mend t: the repair kit in hand,
                     a broken radio worn, t s into mending it (note 201)
          route gen [--tier local|frontier|deadLines|deepTerritory] [--seed n] [--name generated] [--map file.png]
                     writes content/lines/<name>.json (+ .route.json) and a map; try `screenshot --line generated`
          route sweep [--seeds n]                  generate n routes per tier and report ranges
          site [--tier t] [--seed n] [--kind yard|yardAndVillage|village [--town]] [--route tier:seed --stop i] [--out file.png] [--size px]
                     one stop's layout (docs/design/level-design.md): its tracks, buildings, loot containers, how hard it
                     is to work, every invariant, and a top-down plan PNG (default out/stops/)
          site sweep [--seeds n]                   n stops per tier and kind: difficulty, band hits, attempts, failing checks
          linegen generate [--tier t] [--severity 0..1] [--cars n] [--seed n] [--out plan.json] [--map map.png] [--profile p.png]
                     the procedural line generator (docs/design/linegen-plan.md): a Line Plan, its map and profile
          linegen sweep [--tier all|t] [--cars 3,10,20] [--seeds n] [--report sweep.csv] [--fallbacks]
                     generation, validation and metrics over many seeds (plan §16.5, §20.2)
          harness [--bots n] [--cars n] [--seconds t] [--seed s] [--latency s] [--jitter s] [--loss 0..1] [--line name | --route tier:seed]
                     [--enemies] [--no-combat] [--no-boiler] [--udp | --online] [--trace file]   --udp: real sockets on localhost instead of the simulated link;
                     --online: every bot joins a lobby on the fake Steam and plays over relayed P2P
                     host + bot clients over a simulated network; reports prediction error, bandwidth, deaths,
                     and with --enemies the director's spawns, punishes, deaths by cause and fairness audit; on a route, the
                     facility stops the crew worked (five bots make a crew for a winch); --trace writes who's doing what;
                     --comms poor|awful (or --voice-loss/-latency/-jitter/-talkover): the crew's calls over a degraded voice;
                     --insist kind,kind --hazards wet --start m: one of dt balance --pairs's nights by hand (--no-look: without its look-out)
                     --drop-rejoin bot:at:seconds: that bot's link drops at seconds in and it rejoins the given seconds later (note 253)
          balance --pairs|--triples [--wide] [--routes r,r] [--crews 2,4,8] [--seeds n] [--every-hazard] [--at-stops] [--sample n] [--seconds s] [--hazards clear,wet,cold,dark] [--only kind,kind]
                     GDD §34 combination fairness: each combination insisted on for a short bot night under each hazard set,
                     on every route with every crew size (tuning/balance.json combinations; --wide: its nightly grid); flags
                     unwinnable (lost every night in a route and crew) and trivial meetings, exit 1 if any
          audit cascades [--only rupture,car-fire,…] | audit grabs [--only Dragger,…] [--crews 2,8]
                     §34's cascade audit (every §23 chain recovered in time) and App. A.9/B.10's per-tree check (every GRAB
                     broken by the crew present, at every crew size); exit 1 on a finding
          balance [--tiers frontier,deadLines] [--seeds n] [--crews 2,8] [--cars 6,10,20] [--seconds t] [--parallel p]
                     GDD §34's sweep: harness nights at each crew size and train length, side by side, judged against
                     tuning/balance.json (survivable at 2, non-trivial at 8, fair throughout); exit 1 if a check fails
          facility drill [--route tier:seed] [--facility i] [--cars n] [--load-seconds s]
          facility drill <kind> [--route tier:seed] [--cars n] [--hands n] [--seconds s]   a bot crew works a facility of that kind (GDD §18 set pieces)
                     GDD §17's set piece scripted: cut, spur in, load, back out, recouple, switch back, go; the timeline
          vr check [--frames n] [--view roof|cab|…] [--scale 0.5] [--stereo multiview|per-eye] [--out out/shots/vr.png]
                     an OpenXR session end to end (Monado's simulated headset works headless) and both eyes as a PNG
          campaign new|show|slots|buy car|kit|powder|lamp|extinguisher|sell|<upgrade>|sim|play [--slot 1..3] [--saves dir] [--contract i] [--seed n]
                     the campaign between nights (spec E, F): the board, purchases, F.4's progression check, a bot night settled
          online check                             is Steam reachable from here (signed-in user, or what's missing)
          audio opera [--check]
                     the derailment's music (GDD v1.4 App. E.6): our own CC0 recordings into content/audio/music, the manifest, CREDITS.md
          audio render [--scenario bed|tells|chaos|wreck|toys] [--cars n] [--speed v] [--listener car (0 = cab) | all] [--seconds t] [--out file.wav] [--track id] [--space name]
                     renders through the mixer to a WAV and a spectrogram PNG, and reports each tell's margin over the bed (spec A.3);
                     wreck: the whole derailment sequence with its opera (E.6: the hit, the duck under the dead channel, the fade);
                     --space: heard as if in that space (content/audio/spaces.json)
          audio render --sound <name> [--param name=value ...] [--seconds t] [--out file.wav]
                     one sound alone (a one-shot to its end, a loop for 4 s): WAV, spectrogram, its length, takes and level
          edit [--port p] [--screenshot file.png]   the designer's editor (tuning + routes) at http://127.0.0.1:<port>/
          voice bench [--car n (0 = cab)] [--z m] [--radio] [--latency s --jitter s --loss 0..1]
                     one speaker to a listener on car 3 through host routing, Opus and the mixer (spec A.5)
        """);
    return 2;
}

/// <summary>A hosted night and the dead joiner watching it (<c>dt screenshot --hud --spectating</c>).</summary>
sealed record SpectatedNight(NetPlaySession Host, NetPlaySession Watcher) : IDisposable
{
    public void Dispose()
    {
        Watcher.Dispose();
        Host.Dispose();
    }
}
