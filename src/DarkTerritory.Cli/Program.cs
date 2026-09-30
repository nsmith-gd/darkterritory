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
using DarkTerritory.Sim.Train;

// `dt` — the headless command-line entry point. Everything an agent needs to inspect or verify
// the game without a window goes through here. Output is JSON unless stated otherwise.

var baseContent = DataFile.FindContentRoot(Environment.CurrentDirectory);
// Mods (T49) laid over the content like the game does (--no-mods for the base game). The editor edits the base content.
bool noMods = args.Contains("--no-mods");
args = [.. args.Where(a => a != "--no-mods")];
var content = args is ["edit", ..] or ["mods", ..] ? baseContent : Mods.Mount(baseContent, enabled: !noMods);
var train = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
var player = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
var boiler = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));
var routeTuning = DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File));
var sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File));

return args switch
{
    // dt mods: the mods found, in load order, and what each does to which file (T49).
    ["mods", ..] => Print(new
    {
        folders = Mods.Folders(baseContent),
        mods = ContentMods.Find(Mods.Folders(baseContent)).Select(m => new { m.Name, m.Version, m.Order, m.Description, m.Directory }),
        files = ContentMods.Plan(baseContent, ContentMods.Find(Mods.Folders(baseContent))),
    }),
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
    ["screenshot", ..] when args.Contains("--hud") => Print(HudShot(content, args)),
    ["screenshot", ..] when args.Contains("--menu") => Print(MenuShot(train, content, args)),
    ["screenshot", ..] => Print(Screenshot(train, content, args)),
    ["route", "gen", ..] => Print(GenerateRoute(routeTuning, content, args)),
    ["route", "sweep", ..] => Print(SweepRoutes(routeTuning, (int)Opt(args, "--seeds", 200))),
    ["linegen", var verb, ..] => Print(LineGenCommands.Run(content, verb, args)),
    ["harness", ..] => Print(RunHarness(args)),
    ["balance", ..] => PrintBalance(RunBalance(args)),
    ["online", "check"] => Print(OnlineCheck()),
    ["campaign", var verb, ..] => Print(CampaignCommand(content, verb, args)),
    ["vr", "check", ..] => Print(VrCheck(train, content, args)),
    ["facility", "drill", ..] => Print(FacilityDrill(train, content, routeTuning, args)),
    ["audio", "render", ..] => Print(RenderAudio(content, args)),
    ["edit", ..] => Edit(content, args),
    ["voice", "bench", ..] => Print(DarkTerritory.Game.Sound.VoiceBench.Run(content, (int)Opt(args, "--car", 3), Opt(args, "--z", 4), args.Contains("--radio"),
        Opt(args, "--seconds", 2), new Ballast.Net.LinkConditions(Opt(args, "--latency", 0), Opt(args, "--jitter", 0), Opt(args, "--loss", 0)))),

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
    return Harness.Run(line, train, player, new HarnessOptions
    {
        Observe = trace is null ? null : (tick, crew, world) =>
        {
            // And what's out there: each enemy, what it's doing, and where (its car, or along the line).
            string enemies = string.Join(" ", world.ActiveEnemies.Where(e => e.Kind != DarkTerritory.Sim.Enemies.EnemyKind.Sleepers)
                .Select(e => $"{e.Kind}:{e.Phase}@{(e.Attached >= 0 ? $"car{e.Attached}" : $"{e.LineDistance:0}")}"));
            string now = string.Join(" | ", crew.Select(c => Harness.Describe(c.Bot, c.State))) + (enemies.Length > 0 ? $"  || {enemies}" : "");
            if (now != lastTrace)
                trace.WriteLine($"{tick / 30.0,7:0.0}s  @{world.Train.Dynamics.Distance:0} {world.Train.Dynamics.Velocity:0.0}m/s p{world.Train.Dynamics.Path}  {now}");
            lastTrace = now;
        },
        Bots = (int)Opt(args, "--bots", 8),
        Cars = (int)Opt(args, "--cars", 10),
        Seconds = Opt(args, "--seconds", 120),
        Seed = (int)Opt(args, "--seed", 1),
        Link = new Ballast.Net.LinkConditions(Opt(args, "--latency", 0.09), Opt(args, "--jitter", 0.02), Opt(args, "--loss", 0.03)),
        StartDistance = route is null ? 600 : 400,
        Combat = args.Contains("--no-combat") ? null : combat,
        Enemies = args.Contains("--enemies") ? enemies : null,
        Route = route,
        Udp = args.Contains("--udp"),
        Network = online,
        Vigil = DataFile.Load<DarkTerritory.Sim.Run.VigilTuning>(Path.Combine(content, DarkTerritory.Sim.Run.VigilTuning.File)),
        Sight = sight,
        Run = route is null ? null : DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)),
        Facilities = route is null ? null : DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)),
        YardLength = route?.GateOr(routeTuning.YardLength) ?? routeTuning.YardLength,
    }, args.Contains("--no-boiler") ? null : boiler);
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
    var vigil = DataFile.Load<DarkTerritory.Sim.Run.VigilTuning>(Path.Combine(content, DarkTerritory.Sim.Run.VigilTuning.File));
    var run = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
    var facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File));
    double seconds = Opt(args, "--seconds", 3600);
    var rows = new BalanceRow[grid.Count];
    // Each night is its own host and bots over their own loopback; nothing's shared, so they run side by side.
    Parallel.For(0, grid.Count, new ParallelOptions { MaxDegreeOfParallelism = (int)Opt(args, "--parallel", Environment.ProcessorCount) }, i =>
    {
        var n = grid[i];
        var route = RouteGenerator.Generate(routeTuning, n.Tier, n.Seed);
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
            Vigil = vigil,
            Sight = sight,
            Run = run,
            Facilities = facilities,
            YardLength = routeTuning.YardLength,
        }, boiler);
        rows[i] = Balance.Row(n, report);
        Console.Error.WriteLine($"{n.Tier}:{n.Seed} crew {n.Crew} cars {n.Cars}: {rows[i].End}, net {rows[i].Net}, lost {rows[i].CrewLost}");
    });
    return Balance.Judge(rows, targets);
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
        vr = DarkTerritory.Game.VrView.Start("dt vr check", Opt(args, "--scale", 0.5));
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
            vr.Dress(look);
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
        while (vr.Session.FramesRendered < frames && clock.Elapsed.TotalSeconds < 30)
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
        rakes = train.Rakes.Count,
        inOrder = train.Rakes.Count == 1 && train.Dynamics.Consist.Vehicles.Select(v => v.Id).SequenceEqual(order),
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
        tier = DarkTerritory.Sim.Campaign.Campaign.TierFor(t, s.Cars).ToString(),
        nextCar = DarkTerritory.Sim.Campaign.Campaign.NextCarCost(t, s),
        upgrades = s.Upgrades,
        contracts = DarkTerritory.Sim.Campaign.Campaign.Offers(t, runTuning, s).Select((c, i) => new { index = i, route = c.Route, perCar = c.PerCar }),
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
                var p = what == "car" ? DarkTerritory.Sim.Campaign.Campaign.BuyCar(t, s) : DarkTerritory.Sim.Campaign.Campaign.BuyUpgrade(t, s, what);
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
                var loadout = DarkTerritory.Sim.Campaign.Campaign.Apply(t, s.Upgrades, new DarkTerritory.Sim.Campaign.Loadout(train, boiler,
                    DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File)),
                    DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(content, DarkTerritory.Sim.Enemies.EnemyTuning.File))));
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
                    Vigil = DataFile.Load<DarkTerritory.Sim.Run.VigilTuning>(Path.Combine(content, DarkTerritory.Sim.Run.VigilTuning.File)),
                    Sight = sight,
                }, loadout.Boiler);
                if (report.Run is not { } night)
                    return new { error = "the night didn't run" };
                s = DarkTerritory.Sim.Campaign.Campaign.Settle(s, night);
                saves.Save(s);
                return new { contract = contract.Route, night, board = Board(s) };
            }
        default:
            return new { error = $"unknown campaign command '{verb}': new, show, slots, buy car|<upgrade>, sim, play" };
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
    int width = (int)Opt(args, "--width", 640), height = (int)Opt(args, "--height", 360), scale = (int)Opt(args, "--scale", 2);
    string output = Str(args, "--out", $"out/shots/{view}.png");

    // --route tier:seed generates the night in memory; --coaling stops the train at its coaling tower, chute pouring.
    Route? generated = Str(args, "--route", "") is { Length: > 0 } spec
        ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars)
        : null;
    var line = generated?.Build() ?? RailLine.Load(Path.Combine(content, "lines", lineName + ".json"));
    var consist = Consist.Uniform(t, cars, 1);
    DarkTerritory.Sim.Run.Run? run = null;
    double at = Opt(args, "--at", 1200);
    RouteFeature? tower = generated?.Of(FeatureKind.Facility).FirstOrDefault(f => f.Facility == FacilityKind.CoalingTower);
    if (args.Contains("--coaling") && generated is not null && tower is not null)
    {
        run = new DarkTerritory.Sim.Run.Run(DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)), generated);
        at = run.ChuteAt(tower, line).SpoutAlong + t.Geometry.EngineLength - t.Geometry.Engine.TenderLength / 2;
        int index = generated.Of(FeatureKind.Facility).ToList().IndexOf(tower);
        run.Mirror(DarkTerritory.Sim.Run.RunPhase.AtFacility, DarkTerritory.Sim.Run.RunEnd.None, 900, index, true, [.. Enumerable.Repeat(200.0, run.FacilityCount)]);
    }
    // --site: stop at the first facility with loading modules (spec D), crates out and the winch sled part-hauled.
    DarkTerritory.Sim.Run.Site? site = null;
    if (args.Contains("--site") && generated is not null)
    {
        run = new DarkTerritory.Sim.Run.Run(DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File)), generated);
        run.EnableSites(DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)), line);
        // --crane: the first facility with a gantry crane (T48) instead, its first casting on the hook.
        // --facility i: that facility's site, whatever it has (to look at a kind's buildings).
        int pick = (int)Opt(args, "--facility", -1);
        site = pick >= 0 && pick < run.Sites.Count ? run.Sites[pick]
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
            run.Mirror(DarkTerritory.Sim.Run.RunPhase.AtFacility, DarkTerritory.Sim.Run.RunEnd.None, 900, site.Index, false,
                [.. Enumerable.Repeat(0.0, run.FacilityCount)], [.. run.Sites.Select(x => new DarkTerritory.Sim.Run.SiteState(true, x == site ? 0.45 : 0, x?.SledsLeft ?? 0, x == site, false, x == site ? 0.7 : 0))]);
        }
    }
    // --junction i: at a branch's points (T27), [--diverge] set for the branch, [--through] and the train run in onto it.
    int junction = (int)Opt(args, "--junction", -1);
    if (junction >= 0 && junction < line.Branches.Count)
        at = line.Branches[junction].Toe - (args.Contains("--through") ? 10 : 25);
    var train = new TrainOnLine(new TrainDynamics(consist), line, at);
    if (site is { Spur: >= 0 })
    {
        var state = train.Capture();
        train.Restore(state with { Rakes = [state.Rakes[0] with { Path = site.Spur }] });
    }
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
    var camera = Views.Get(view, train, (int)Opt(args, "--car", 2));
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
        foreach (var crate in site.CrateStack)
            shelf.SpawnCargo(crate - Double3.Up * 0.15, site.CrateLineHint);
        foreach (var crate in site.HeavyStack)
            shelf.SpawnCargo(crate, site.CrateLineHint, site.HeavyRadius);
        cargo = [.. shelf.All];
        if (Str(args, "--cam", "") is not { Length: > 0 })
        {
            // (--crank closes on the winch's cranks even at a facility that also has a crane.)
            if (site.Crane is { } crane && !args.Contains("--crank"))
            {
                // High on the near side of the track, past the gantry's end, looking down across the train at the hook and castings.
                var outward = (crane.Corner(0, 1) - crane.Corner(0, 0)) with { Y = 0 };
                var along = (crane.Corner(1, 0) - crane.Corner(0, 0)).Normalized;
                camera = Camera.LookAt(crane.Corner(1, 0) - outward.Normalized * 5 + along * 6 + Double3.Up * 11, crane.HookAt - Double3.Up * 2.5, 65);
            }
            else if (site.Has(DarkTerritory.Sim.Run.ModuleKind.Winch))
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
    // --integrity a[,b,...]: each car's condition, front to back, the last repeating (look.json "damage": scars, states).
    if (Str(args, "--integrity", "") is { Length: > 0 } integrity)
    {
        var each = integrity.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        for (int i = 0; i < train.Vehicles.Count; i++)
            train.Vehicles[i].Integrity = Math.Clamp(each[Math.Min(i, each.Length - 1)], 0, 1);
    }
    var scene = new GreyboxScene
    {
        // --draw m: how far along the line to build it (an aerial view of a stretch wants more than the cab's 400).
        DrawDistance = (float)Opt(args, "--draw", 400),
        Tick = args.Contains("--muzzle") ? 101 : -1,
        Look = look,
        Route = route,
        Run = run,
        Time = 0.37,
        Enemies = args.Contains("--threats") ? Staging.Threats(train) : null,
        Bodies = args.Contains("--bodies") ? Staging.Bodies(train, content).All : cargo,
        // --crew: three on car 2's roof, one reaching up, one holding out both hands, one with a keyboard (T47's arms).
        Crew = args.Contains("--crew") ? Staging.Crew(train, content) : null,
        Emergency = args.Contains("--vigil"),
        Diverging = train.Diverging,
        // --throttle x: the regulator's handle drawn that far open (T29's cab levers).
        Controls = new TrainControls { Throttle = Math.Clamp(Opt(args, "--throttle", 0), 0, 1), Reverser = 1 },
    };
    scene.Build(mesh, train, camera.Position);
    // How long a frame's scene takes to build on the CPU, warm (the first build cooks the kit's pieces).
    var buildClock = Stopwatch.StartNew();
    int builds = (int)Opt(args, "--builds", 5);
    for (int b = 0; b < builds; b++)
        scene.Build(mesh, train, camera.Position);
    double buildMs = buildClock.Elapsed.TotalMilliseconds / builds;
    var lighting = Views.Lighting(train, look);
    if (args.Contains("--vigil"))
        lighting.LampRange = 0.01f; // a Vigil: no power to the headlamp
    if (route is not null)
    {
        lighting.FogDensity = (float)route.Weather.FogDensity;
        lighting.Wetness = route.Weather.Wet ? 1 : 0;
    }
    // --fog d: a thinner (or thicker) night than the route's, to look the lie of the land over.
    if (args.Contains("--fog"))
        lighting.FogDensity = (float)Opt(args, "--fog", lighting.FogDensity);
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
    var menu = new DarkTerritory.Game.FrontEnd(ct, rt, saves, Path.Combine(dir, "settings.json"), () => 7);
    if (screen is DarkTerritory.Game.Screen.Fortress or DarkTerritory.Game.Screen.Upgrades)
        menu.ShowFortress((int)Opt(args, "--slot", 1));
    menu.Show(screen);
    for (int i = 0; i < (int)Opt(args, "--down", 0); i++)
        menu.Down();
    return (menu, screen);
}

// A frame as the game draws it: a solo session stepped for a while, seen first person, with the HUD (T23).
static object HudShot(string content, string[] args)
{
    int cars = (int)Opt(args, "--cars", 6);
    Route? generated = Str(args, "--route", "") is { Length: > 0 } spec
        ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars)
        : null;
    var session = generated is null ? new PrototypeSession(content, Str(args, "--line", "test-loop"), cars) : new PrototypeSession(content, generated, cars, enemies: false);
    session.Controls.Throttle = Opt(args, "--throttle", 0.6);
    for (int i = 0; i < Opt(args, "--seconds", 6) * SimConstants.TickRate; i++)
        session.Step(new PlayerIntent { LookPitch = i == 0 ? (float)Opt(args, "--pitch", 0) : 0, LookYaw = i == 0 ? (float)Opt(args, "--yaw", 0) : 0 });
    int width = (int)Opt(args, "--width", 480), height = (int)Opt(args, "--height", 270), scale = (int)Opt(args, "--scale", 2);
    string output = Str(args, "--out", "out/shots/hud.png");
    var frames = session.InterpolatedFrames(1);
    var camera = session.EyeCamera(frames, 1, 0, 0);
    using var gpu = new GpuContext("dt screenshot --hud");
    using var renderer = new GreyboxRenderer(gpu, width, height);
    var mesh = new MeshBuilder();
    var look = Looked(content, args);
    look?.Dress(renderer);
    new GreyboxScene { Route = session.Route, Run = session.World.Run, Vehicles = session.Train.Vehicles, Bodies = session.World.Bodies.All, Time = 0.37, Look = look }
        .Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
    var lighting = Views.Lighting(frames[0], look);
    if (session.Route is { } r)
    {
        lighting.FogDensity = (float)r.Weather.FogDensity;
        lighting.Wetness = r.Weather.Wet ? 1 : 0;
    }
    var hud = new Overlay();
    Hud.Build(hud, width, height, session);
    // --card: the generated line's route card over it; --overlay: the designer's overlay (linegen plan §9.7, §20.2).
    if (session.Route?.Plan is { } plan)
    {
        if (args.Contains("--card"))
            DarkTerritory.Game.LineGen.PlanHud.RouteCard(hud, width, height, plan, (int)Opt(args, "--page", 0));
        if (args.Contains("--overlay"))
            DarkTerritory.Game.LineGen.PlanHud.Overlay(hud, width, height, session, plan);
    }
    var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, hud);
    PngWriter.Write(output, pixels, width, height, scale);
    return new { path = Path.GetFullPath(output), prompt = Hud.Prompt(session), quads = hud.Count / 6, status = session.Status() };
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
    string scenario = Str(args, "--scenario", "chaos");
    if (Str(args, "--listener", "") == "all")
        return DarkTerritory.Game.Sound.AudioBench.Sweep(content, scenario, (int)Opt(args, "--cars", 20), Opt(args, "--speed", 22), Opt(args, "--seconds", 6));
    string output = Str(args, "--out", $"out/audio/{scenario}.wav");
    var clock = Stopwatch.StartNew();
    var (report, mix) = DarkTerritory.Game.Sound.AudioBench.Render(content, scenario, (int)Opt(args, "--cars", 20), Opt(args, "--speed", 22),
        (int)Opt(args, "--listener", 5), Opt(args, "--seconds", 6));
    Ballast.Audio.Wav.Write(output, mix);
    // The picture of it: a spectrogram beside the WAV, for looking at bands without listening.
    string picture = Path.ChangeExtension(output, ".png");
    PngWriter.Write(picture, DarkTerritory.Game.Sound.Spectrogram.Render(mix, 800, 300), 800, 300, 1);
    return new { path = Path.GetFullPath(output), spectrogram = Path.GetFullPath(picture), report, ms = clock.ElapsedMilliseconds };
}

static string Str(string[] args, string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
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
          screenshot [--view trackside|roof|cab|chase|ahead] [--line name] [--cars n] [--at s] [--car i] [--cut n]
                     [--cam s,lateral,height --target s,lateral,height --fov deg]   camera by line coordinates
                     [--width w] [--height h] [--scale k] [--out file.png] [--threats]   --threats stages one of each enemy
                     [--route tier:seed [--coaling]]   a generated night; --coaling stops at its coaling tower, chute pouring
                     [--bodies]   crates, a lamp and a crewmate's body on the roofs, settled by the physics
                     [--vigil]    emergency lighting, as during a Vigil (spec C.2)
                     [--ps2]      the era comparison mode   [--muzzle] the guns just fired   [--builds n] time n warm builds
                     [--integrity a,b,..] each car's condition, front to back (scars and damage states)
                     [--route tier:seed --site [--crank | --crane | --facility i]]   stopped at a facility: crates out, the winch sled part-hauled (spec D); --crank: close on the cranks; --crane: a gantry crane's facility, a casting on the hook; --facility: the route's i-th
             [--route tier:seed --junction i [--diverge] [--through]]   at a switch, set for the branch, run in onto it
          art check                                every kit piece against its triangle budget (exit 1 if any is over)
          art show <piece> [--yaw deg] [--pitch deg] [--zoom k] [--ps2] [--greybox]   a piece on a turntable, to out/shots/art/
          screenshot --menu title|slots|fortress|upgrades|quickNight|join|settings [--down n] [--saves dir]
                     a screen of the front end over the yard, as the game draws it
          screenshot --hud [--route tier:seed] [--seconds t] [--throttle 0..1] [--pitch r] [--yaw r]
                     a solo session played for a few seconds, first person, with the HUD, at the game's 480x270
          route gen [--tier local|frontier|deadLines|deepTerritory] [--seed n] [--name generated] [--map file.png]
                     writes content/lines/<name>.json (+ .route.json) and a map; try `screenshot --line generated`
          route sweep [--seeds n]                  generate n routes per tier and report ranges
          linegen generate [--tier t] [--severity 0..1] [--cars n] [--seed n] [--out plan.json] [--map map.png] [--profile p.png]
                     the procedural line generator (docs/design/linegen-plan.md): a Line Plan, its map and profile
          linegen sweep [--tier all|t] [--cars 3,10,20] [--seeds n] [--report sweep.csv] [--fallbacks]
                     generation, validation and metrics over many seeds (plan §16.5, §20.2)
          harness [--bots n] [--cars n] [--seconds t] [--seed s] [--latency s] [--jitter s] [--loss 0..1] [--line name | --route tier:seed]
                     [--enemies] [--no-combat] [--no-boiler] [--udp | --online] [--trace file]   --udp: real sockets on localhost instead of the simulated link;
                     --online: every bot joins a lobby on the fake Steam and plays over relayed P2P
                     host + bot clients over a simulated network; reports prediction error, bandwidth, deaths,
                     and with --enemies the director's spawns, punishes, deaths by cause and fairness audit; on a route, the
                     facility stops the crew worked (five bots make a crew for a winch); --trace writes who's doing what
          balance [--tiers frontier,deadLines] [--seeds n] [--crews 2,8] [--cars 6,10,20] [--seconds t] [--parallel p]
                     GDD §34's sweep: harness nights at each crew size and train length, side by side, judged against
                     tuning/balance.json (survivable at 2, non-trivial at 8, fair throughout); exit 1 if a check fails
          facility drill [--route tier:seed] [--facility i] [--cars n] [--load-seconds s]
                     GDD §17's set piece scripted: cut, spur in, load, back out, recouple, switch back, go; the timeline
          vr check [--frames n] [--view roof|cab|…] [--scale 0.5] [--out out/shots/vr.png]
                     an OpenXR session end to end (Monado's simulated headset works headless) and both eyes as a PNG
          campaign new|show|slots|buy car|buy <upgrade>|sim|play [--slot 1..3] [--saves dir] [--contract i] [--seed n]
                     the campaign between nights (spec E, F): the board, purchases, F.4's progression check, a bot night settled
          online check                             is Steam reachable from here (signed-in user, or what's missing)
          audio render [--scenario bed|tells|chaos] [--cars n] [--speed v] [--listener car (0 = cab) | all] [--seconds t] [--out file.wav]
                     renders through the mixer to a WAV and a spectrogram PNG, and reports each tell's margin over the bed (spec A.3)
          edit [--port p] [--screenshot file.png]   the designer's editor (tuning + routes) at http://127.0.0.1:<port>/
          voice bench [--car n (0 = cab)] [--z m] [--radio] [--latency s --jitter s --loss 0..1]
                     one speaker to a listener on car 3 through host routing, Opus and the mixer (spec A.5)
        """);
    return 2;
}
