using System.Diagnostics;
using System.Globalization;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt playthrough`: a real night, photographed. The art checklist rates each piece in a staged view; this checks what a
/// crew actually sees. A solo night on a generated route runs with the director's enemies, the train driven by the line's
/// own authority (LineGen.Ride, the app's --ride), and every encounter is rendered as it happens, the game's own scene as
/// the app builds it: when each enemy is first there, and as it telegraphs, grabs and punishes, from beside it (or down
/// the aisle, if it's in a car). A chase shot every --every seconds shows the line between. Frames go to
/// out/playthrough with index.json (what, when, where), for the Look Review and the audit.
/// </summary>
static class PlaythroughCommands
{
    public static object Run(string content, string[] args)
    {
        string spec = Str(args, "--route", "frontier:7");
        int width = (int)Opt(args, "--width", 960), height = (int)Opt(args, "--height", 540), cars = (int)Opt(args, "--cars", 6);
        double minutes = Opt(args, "--minutes", 20), every = Opt(args, "--every", 90), gap = Opt(args, "--gap", 2);
        int bots = (int)Opt(args, "--bots", 0);
        // The ride drives the line's authority. --cap km/h holds it under a figure besides (it was 38, for the Sleepers' 40,
        // until they went: the director's decision of 2026-10-06, note 265); 0, none.
        double cap = Opt(args, "--cap", 0) is > 0 and var capKmh ? capKmh / 3.6 : double.MaxValue;
        string dir = Str(args, "--out", "out/playthrough");
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars);
        using var gpu = new GpuContext("dt playthrough");
        using var renderer = new GreyboxRenderer(gpu, width, height);
        var look = Look.Load(content);
        look.Dress(renderer);
        var mesh = new MeshBuilder();
        var overlay = new Overlay();
        var shots = new List<object>();
        var seen = new HashSet<(int, SpinePhase)>();
        var beats = new HashSet<(int, string)>();
        var pending = new List<(double Due, Enemy Body, string Name, string What)>();
        // How long after a beat begins it's photographed (s): mid-way through the going (the death's 2 s crumble, the
        // dispersal's 3 s, the doll's 0.3 s flicker), and a cut Car Hugger once its car's fallen a little way back.
        static double BeatDelay(string beat) => beat switch { "killed" => 0.6, "dispersed" => 1.0, "vanished" => 0.12, "cut-loose" => 2.5, _ => 0.5 };
        double lastShot = double.NegativeInfinity, lastChase = 0, stokerSince = -1;
        var watch = Stopwatch.StartNew();

        // One scene the whole night, as the app has: what the sim's done with in a tick (the killed, the Choir driven off, a
        // Car Hugger cut loose, a Track Doll gone) is the scene's own memory (GreyboxScene.Remember), fed every tick below,
        // so a shot shows it going the way a crew watching all along would see it.
        var scene = new GreyboxScene { Look = look, Route = route };
        void Shoot(World world, IReadOnlyList<PlayerState> crew, string name, Camera camera, string what)
        {
            var train = world.Train;
            double seconds = world.Tick * SimConstants.TickSeconds;
            var lighting = Views.Lighting(train, look);
            lighting.FogDensity = (float)route.Weather.FogDensity;
            lighting.Frost = look.Tuning.Atmosphere.Cold.Frost(route.Weather.Cold);
            lighting = look.Chill(lighting, GreyboxScene.ChoirCold(world.Choir.Present ? 1 : (float)world.Choir.Build));
            scene.Signs = world.Lineside?.Signs;
            scene.SignRange = world.Lineside?.Tuning.LampSignRange ?? 350;
            scene.Enemies = world.ActiveEnemies;
            scene.Hits = world.Hits;
            scene.Impacts = world.Impacts;
            scene.Run = world.Run;
            scene.Holdouts = world.Holdouts;
            scene.Vehicles = train.Vehicles;
            scene.Bodies = world.Bodies.All;
            scene.Diverging = train.Diverging;
            scene.Stands = world.Switches;
            scene.Time = seconds;
            scene.Tick = world.Tick;
            scene.Controls = world.Controls;
            // What the app sets each frame from the night's state (DarkTerritory.App's render loop).
            scene.Wreck = train.Wreck;
            scene.Derailed = world.Derailed;
            scene.FireDoorOpen = train.Boiler.FireDoorOpen;
            scene.SinceShovel = train.Boiler.SinceShovel;
            scene.ChoirGathering = world.Choir.Present ? 1 : (float)world.Choir.Build;
            scene.FireGlow = train.BoilerTuning is { } bt ? GreyboxScene.FireLook(train.Boiler.Firebox, bt.FireboxCapacity) : 0.7f;
            scene.StokerLowFor = stokerSince < 0 ? -1 : seconds - stokerSince;
            scene.StokerDownAt = world.Enemies?.Stoker.HeatSeconds ?? 20;
            scene.LampLit = world.LampShining;
            scene.Cut = DarkTerritory.Game.Art.SceneArt.Cuts(train);
            // Each as the app draws them, doing what they're doing (CrewActs): ids in join order, as the host gave them.
            scene.Crew = [.. crew.Select((s, i) => CrewActs.Crewmate((byte)(i + 1), s, world, train.Frames, crew))];
            scene.Build(mesh, train, camera.Position);
            overlay.Clear();
            var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, overlay);
            string file = $"{shots.Count:000}-{name}.png";
            PngWriter.Write(Path.Combine(dir, file), pixels, width, height, 1);
            var engine = train.Frames[0].Origin;
            shots.Add(new
            {
                file,
                seconds = Math.Round(seconds, 1),
                km = Math.Round(train.Dynamics.Distance / 1000, 2),
                what,
                engine = new[] { Math.Round(engine.X), Math.Round(engine.Y), Math.Round(engine.Z) },
                camera = new[] { Math.Round(camera.Position.X), Math.Round(camera.Position.Y), Math.Round(camera.Position.Z) }
            });
            lastShot = seconds;
        }

        // Note 260: the roof warning against the line's own kills. Each board's warning as a player would see it go up (after
        // the tick), every time the host's hazards took someone up top, and every roof death: none may come without its
        // warning up a full lead (GDD App. A.1).
        var roofUp = new Dictionary<int, uint>();
        int roofWarnings = 0, roofWarningsWithRidersUp = 0, commitsSeen = 0, unwarned = 0;
        var commitLeads = new List<double>();
        var roofDeaths = new Dictionary<string, int>();
        var wasAlive = new Dictionary<int, bool>();
        void RoofAudit(World world, IReadOnlyList<PlayerState> crew)
        {
            if (world.Lineside is not { } lineside)
                return;
            var train = world.Train;
            foreach (var sign in lineside.Signs)
            {
                if (sign.Kind is not (SignKind.LowClearance or SignKind.SpeedLimit))
                    continue;
                if (!lineside.Warns(sign, train))
                    roofUp.Remove(sign.Id);
                else if (roofUp.TryAdd(sign.Id, world.Tick))
                {
                    roofWarnings++;
                    if (crew.Any(c => c.Alive && c.Surface == Surface.Roof))
                        roofWarningsWithRidersUp++;
                }
            }
            for (; commitsSeen < lineside.Commits.Count; commitsSeen++)
            {
                var c = lineside.Commits[commitsSeen];
                double lead = roofUp.TryGetValue(c.Sign, out uint since) ? (c.Tick - since) * SimConstants.TickSeconds : -1;
                commitLeads.Add(lead);
                if (lead < lineside.Tuning.RoofWarning.LeadSeconds - 1e-6)
                    unwarned++;
            }
            for (int i = 0; i < crew.Count; i++)
            {
                bool alive = crew[i].Alive;
                if (wasAlive.TryGetValue(i, out bool was) && was && !alive && crew[i].Death is DeathCause.Struck or DeathCause.Thrown)
                    roofDeaths[crew[i].Death.ToString()] = roofDeaths.GetValueOrDefault(crew[i].Death.ToString()) + 1;
                wasAlive[i] = alive;
            }
        }

        // Note 266: the bend warning as the cab has it (TrackRules.Assess), each time it goes up, and the night's lamps: when
        // the forward lamp went out and came back, and each car's (who put it out, by the enemies under way then).
        int bendWarnings = 0;
        bool bendUp = false, headWas = true;
        double headOutSeconds = 0;
        var headOuts = new List<object>();
        var carOuts = new List<object>();
        var carWas = new Dictionary<int, bool>();
        void LampAndBendAudit(World world)
        {
            var train = world.Train;
            double now = world.Tick * SimConstants.TickSeconds;
            if (world.TrackPlan is { } plan)
            {
                bool up = DarkTerritory.Sim.LineGen.TrackRules.Assess(train, plan.Rules, train.Dynamics.Tuning.Overspeed).Warning;
                if (up && !bendUp)
                    bendWarnings++;
                bendUp = up;
            }
            string Who() => string.Join(",", world.ActiveEnemies.Where(e => !e.Gone && e.Phase != SpinePhase.Dormant).Select(e => e.Kind.ToString()).Distinct());
            if (!world.Derailed)
            {
                if (!world.LampLit)
                    headOutSeconds += SimConstants.TickSeconds;
                if (world.LampLit != headWas)
                    headOuts.Add(new { seconds = Math.Round(now, 1), lit = world.LampLit, smashed = Math.Round(world.LampOutSeconds), around = Who() });
                headWas = world.LampLit;
                foreach (var v in train.Dynamics.Consist.Vehicles)
                {
                    if (carWas.TryGetValue(v.Id, out bool was) && was && !v.LampLit)
                        carOuts.Add(new { seconds = Math.Round(now, 1), car = v.Id, around = Who() });
                    carWas[v.Id] = v.LampLit;
                }
            }
        }

        // After each tick: each enemy as it arrives and at each beat of its spine that a crew would be watching for, and the
        // line between.
        void Watch(World world, IReadOnlyList<PlayerState> crew)
        {
            var train = world.Train;
            double now = world.Tick * SimConstants.TickSeconds;
            RoofAudit(world, crew);
            LampAndBendAudit(world);
            stokerSince = world.StokerWaiting ? stokerSince < 0 ? now : stokerSince : -1;
            // The scene watching every tick, as the app's does each frame: what it takes in now it draws going later.
            scene.Enemies = world.ActiveEnemies;
            scene.Hits = world.Hits;
            scene.Bodies = world.Bodies.All;
            scene.Tick = world.Tick;
            scene.Cut = DarkTerritory.Game.Art.SceneArt.Cuts(train);
            scene.Remember(train.Frames.Count);
            // And what it saw go is photographed a moment on, in the middle of its going: the killed going over, a Choir
            // ghost swept up, the doll's flicker, a Car Hugger fallen back with its car; a Tippy Toesie's recoil (its BreakOff,
            // its sim's flee) the same way. One a creature, each, whatever the gap since the last shot.
            foreach (var (beat, body) in scene.NewBeats)
                if (beats.Add((body.Id, beat)))
                    pending.Add((now + BeatDelay(beat), body, $"{body.Kind}-{beat}".ToLowerInvariant(), $"{body.Kind} {beat}"));
            foreach (var e in world.EnemyEvents)
                if (e.Kind == EnemyKind.TippyToesie && e.To == SpinePhase.BreakOff && world.ActiveEnemies.FirstOrDefault(x => x.Id == e.EnemyId) is { } tippy
                    && beats.Add((e.EnemyId, $"recoil{(int)now}")))
                    pending.Add((now + 0.15, tippy, "tippytoesie-recoil", "TippyToesie recoil"));
            for (int i = pending.Count - 1; i >= 0; i--)
                if (now >= pending[i].Due)
                {
                    var (_, body, name, what) = pending[i];
                    pending.RemoveAt(i);
                    Shoot(world, crew, name, Beside(train, body), what);
                }
            foreach (var e in world.EnemyEvents)
            {
                if (e.To is not (SpinePhase.Alert or SpinePhase.Telegraph or SpinePhase.Grab or SpinePhase.Punish) || now - lastShot < gap)
                    continue;
                if (!seen.Add((e.EnemyId, e.To)))
                    continue;
                var enemy = world.ActiveEnemies.FirstOrDefault(x => x.Id == e.EnemyId);
                if (enemy is null || enemy.Gone)
                    continue;
                Shoot(world, crew, $"{e.Kind}-{e.To}".ToLowerInvariant(), Beside(train, enemy), $"{e.Kind} {e.From} -> {e.To}");
            }
            if (now - lastChase >= every)
            {
                lastChase = now;
                Shoot(world, crew, "line", Views.Get("chase", train, 2), "the line");
            }
        }

        World last;
        if (bots > 0)
            last = Crewed(content, route, cars, bots, minutes, args, Watch);
        else
        {
            // --crew n: the night the director plans for a crew of n (a solo night meets only part of the roster), the one
            // player standing for them all.
            var session = new PrototypeSession(content, route, cars, enemies: true, crew: (int)Opt(args, "--crew", 1));
            var train = session.Train;
            int ticks = (int)(minutes * 60 * SimConstants.TickRate);
            for (int t = 0; t < ticks && session.World.Run?.Over != true; t++)
            {
                if (route.Plan is { } plan)
                    DarkTerritory.Game.LineGen.Ride.Drive(train, plan, ref session.Controls);
                if (train.Dynamics.Speed > cap)
                    session.Controls = session.Controls with { Throttle = 0, Brake = 1 };
                // The fireman's job, done for them (there's only the driver): the fire kept up, so the night isn't lost to the
                // boiler running down on the first grade and the train rolling back.
                if (train.BoilerTuning is { } boiler && train.Boiler.FireFraction(boiler) < 0.6 && t % SimConstants.TickRate == 0)
                    train.Boiler.Shovel(boiler);
                session.Step(default);
                Watch(session.World, [session.Player]);
            }
            last = session.World;
        }
        string index = Path.Combine(dir, "index.json");
        File.WriteAllText(index, System.Text.Json.JsonSerializer.Serialize(shots, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return new
        {
            route = spec,
            seconds = Math.Round(last.Tick * SimConstants.TickSeconds),
            km = Math.Round(last.Train.Dynamics.Distance / 1000, 2),
            over = last.Run?.Over ?? false,
            // How the night ended (a solo night's one player: what killed them, if anything).
            end = last.Run?.End.ToString(),
            derailed = last.Derailed ? last.DerailCause : null,
            shots = shots.Count,
            // Note 266: a bend too fast (train.json overspeed): its warning each time it went up, every bend derailment by how
            // long that warning had been up (none may be under leadSeconds), and the ticks spared for want of it.
            bends = new
            {
                warnings = bendWarnings,
                derails = last.BendCommits.Count,
                warnedSeconds = last.BendCommits.Select(x => Math.Round(x, 2)).ToArray(),
                underLead = last.BendCommits.Count(x => x + 1e-9 < last.Train.Dynamics.Tuning.Overspeed.LeadSeconds),
                spared = last.BendsSpared,
            },
            lamps = new
            {
                headlampOutSeconds = Math.Round(headOutSeconds),
                headlamp = headOuts,
                carsPutOut = carOuts,
                carsLitAtEnd = last.Train.Dynamics.Consist.Vehicles.Count(v => v.LampLit && v.Id > 0),
                cars = last.Train.Dynamics.Consist.Vehicles.Count - 1,
            },
            // Note 260: the line's own kills against their warning (sight.json roofWarning.leadSeconds).
            roofWarning = new
            {
                warnings = roofWarnings,
                withRidersUp = roofWarningsWithRidersUp,
                taken = commitLeads.Count,
                shortestLeadSeconds = commitLeads.Count == 0 ? (double?)null : Math.Round(commitLeads.Min(), 2),
                withoutAFullWarning = unwarned,
                spared = last.Lineside?.Spared ?? 0,
                deaths = roofDeaths,
            },
            index = Path.GetFullPath(index),
            renderSeconds = Math.Round(watch.Elapsed.TotalSeconds),
        };
    }

    /// <summary>
    /// --bots n: a crew of bots works the night instead (the harness's, over a clean loopback), stops and all, so it goes on
    /// past the first facility a lone driver can't work; --insist kind,kind sends those (the combination audit's, note 186).
    /// The host's world is photographed.
    /// </summary>
    static World Crewed(string content, DarkTerritory.Sim.Route.Route route, int cars, int bots, double minutes, string[] args,
        Action<World, IReadOnlyList<PlayerState>> watch)
    {
        var c = AuditCommands.Load(content);
        var routeTuning = RouteTuning.Load(content);
        double yard = route.GateOr(routeTuning.YardLength);
        World? last = null;
        Harness.Run(route.Build(), c.Train, c.Player, new HarnessOptions
        {
            Bots = bots,
            Cars = cars,
            Seconds = minutes * 60,
            Seed = (int)Opt(args, "--seed", 1),
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = c.Run.DepartFrom(yard, Consist.Uniform(c.Train, cars, 1).LengthMetres),
            Combat = c.Combat,
            Enemies = c.Enemies,
            Route = route,
            Run = c.Run,
            Facilities = DataFile.Load<DarkTerritory.Sim.Run.FacilityTuning>(Path.Combine(content, DarkTerritory.Sim.Run.FacilityTuning.File)),
            Holdouts = DataFile.Load<DarkTerritory.Sim.Run.HoldoutTuning>(Path.Combine(content, DarkTerritory.Sim.Run.HoldoutTuning.File)),
            Sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File)),
            YardLength = yard,
            Insist = Str(args, "--insist", "") is { Length: > 0 } insist ? [.. insist.Split(',').Select(k => Enum.Parse<EnemyKind>(k, ignoreCase: true))] : null,
            InsistEvery = Opt(args, "--insist-every", 20),
            Observe = (_, crew, world) =>
            {
                last = world;
                watch(world, [.. crew.Select(x => x.State)]);
            },
            Until = world => world.Run?.Over == true,
        }, c.Boiler);
        return last ?? throw new InvalidOperationException("the night never started");
    }

    /// <summary>
    /// Where a crewmate would see it from: down the aisle at it if it's inside a car (the car's room), else from beside it
    /// off the line, a little behind and above, the train in the shot.
    /// </summary>
    static Camera Beside(TrainOnLine train, Enemy e)
    {
        // Drawn where the scene draws them (GreyboxScene): the Fire Flies on the nearer of the car's two lanterns, the
        // Stoker waiting on the stack's rim.
        if (e.Kind == EnemyKind.FireFlies && e.Attached > 0 && e.Attached < train.Frames.Count && train.Frames[e.Attached].Shape.Interior is { } lamps)
        {
            var near = DarkTerritory.Game.Art.SceneArt.LampPositions(lamps).MinBy(p => Math.Abs(p.Z - e.Local.Z));
            var frame = train.Frames[e.Attached];
            var lamp = new Double3(near.X, near.Y, near.Z);
            return Camera.LookAt(frame.ToWorld(lamp + new Double3(0.8, -0.6, 1.4)), frame.ToWorld(lamp), 55);
        }
        if (e.Kind == EnemyKind.Stoker && train.Frames[0].Shape.Solids.FirstOrDefault(s => s.Part == PartKind.Stack) is { Box: var stack } && stack.Max.Y > 0)
        {
            var engine = train.Frames[0];
            var rim = engine.ToWorld(new Double3(0, stack.Max.Y, stack.Centre.Z));
            return Camera.LookAt(rim + engine.Right * 6 + engine.Up * 1.5 + engine.Back * 4, rim, 50);
        }
        var at = e.WorldPosition(train);
        if (e.Attached >= 0 && e.Attached < train.Frames.Count && train.Frames[e.Attached].Shape.Interior is { } room
            && e.Local.X >= room.Min.X && e.Local.X <= room.Max.X && e.Local.Z >= room.Min.Z && e.Local.Z <= room.Max.Z
            && e.Local.Y >= room.Min.Y - 0.2 && e.Local.Y <= room.Max.Y)
        {
            var frame = train.Frames[e.Attached];
            double z = e.Local.Z < room.Centre.Z ? Math.Min(room.Max.Z - 0.4, e.Local.Z + 3.5) : Math.Max(room.Min.Z + 0.4, e.Local.Z - 3.5);
            var eye = frame.ToWorld(new Double3(0.2, room.Min.Y + 1.6, z));
            return Camera.LookAt(eye, frame.ToWorld(e.Local + Double3.Up * 0.8), 70);
        }
        double hint = train.Dynamics.Distance;
        train.Line.Nearest(at, ref hint);
        var line = train.Line.Sample(hint);
        var side = Double3.Cross(line.Tangent, Double3.Up).Normalized;
        // Off the side it's on (or the right, if it's on the line), so the train isn't between.
        double lateral = Double3.Dot(at - line.Position, side);
        var away = lateral < -0.5 ? side * -1 : side;
        var from = at + away * 7 + Double3.Up * 2.8 - line.Tangent * 5;
        // Never inside the ground: in a cutting, 7 m off the line is in its bank (seen from inside the hill, the rocks and
        // bushes on it float and its face is a blank wall), so up out of it to stand on the slope.
        double ground = PlayerMotor.GroundAt(from, train.Line, ref hint) + 1.7;
        if (from.Y < ground)
            from = new Double3(from.X, ground, from.Z);
        return Camera.LookAt(from, at + Double3.Up * 1.0, 62);
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
    }
}
