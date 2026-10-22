using System.Globalization;
using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

/// <summary>
/// `dt afoot` (ARCHITECTURE §8 note 327; GDD App. F.3, the director: "I left the train, and I went to explore a village, and it
/// just felt like there wasn't enough dangers going on"): that walk, headless. The train stands at the line's first stop with a
/// village; one crewmate walks from doorstep to doorstep through it for --seconds, the rest of the night as the host runs it
/// (enemies and all). The same walk twice, with director.afoot on and off: the signs they were shown, the director's spawns,
/// and the threats that showed themselves while they were out. The bots never go far enough from the train to be afoot.
/// </summary>
static class AfootCommands
{
    public static object Run(string content, string[] args)
    {
        string spec = Str(args, "--route", "frontier:7");
        double seconds = Opt(args, "--seconds", 240), speed = Opt(args, "--walk", 1.4);
        int cars = (int)Opt(args, "--cars", 6);
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars);
        // --halt (#651, note 953): at the first abandoned village with a halt, the train stood at the halt on the main line, and
        // the two walks are the halt's own response (director.encounters.halt) on and off, the watch on in both.
        bool halt = args.Contains("--halt");
        var stop = (halt
            ? route.Features.FirstOrDefault(f => f.Kind == FeatureKind.Village && f.Stop?.Halt is not null
                && f.Stop.Buildings.Any(b => b.Kind == BuildingKind.House && b.Zone == StopZone.Village))
            : route.Features.FirstOrDefault(f => f.Stop?.Buildings.Count(b => b.Kind == BuildingKind.House && b.Zone == StopZone.Village) >= 3))
            ?? throw new InvalidOperationException($"{spec} has no stop with a village{(halt ? " and a halt" : "")}");
        double standAt = stop.Start + (halt ? stop.Stop!.Halt!.Value.S : stop.Stop!.StopPoint.S);
        object Walk(bool on)
        {
            var session = new PrototypeSession(content, route, cars, enemies: true, crew: 1, at: standAt);
            var world = session.World;
            if (halt)
            {
                var t = world.Enemies!;
                var enc = t.Director.Encounters;
                world.EnableEnemies(t with { Director = t.Director with { Encounters = enc with { Halt = enc.Halt with { On = on } } } }, route, route.Seed, crew: 1, authority: true);
            }
            else if (!on)
            {
                var t = world.Enemies!;
                world.EnableEnemies(t with { Director = t.Director with { Afoot = t.Director.Afoot with { On = false } } }, route, route.Seed, crew: 1, authority: true);
            }
            // Under way, stopped here: past the yard and the night's grace, as a crew arriving at a village would be.
            world.Run!.Resume(600, -1, 0, 0);
            session.Controls = session.Controls with { Throttle = 0, Brake = 1 };
            var line = session.Train.Line;
            double hint = 0;
            Double3 World(Pt p)
            {
                var at = DarkTerritory.Sim.Run.Run.StopWorld(line, stop, p);
                return at with { Y = PlayerMotor.GroundAt(at, line, ref hint) };
            }
            // Doorstep to doorstep, nearest first from the train, and back round.
            var houses = stop.Stop!.Buildings.Where(b => b.Kind == BuildingKind.House && b.Zone == StopZone.Village)
                .Select(b => World(DarkTerritory.Sim.Run.StopWalls.Doorstep(b, 0))).ToList();
            var path = new List<Double3>();
            var here = session.Train.Frames[0].Origin;
            while (houses.Count > 0)
            {
                var next = houses.MinBy(h => (h - here).Length);
                houses.Remove(next);
                path.Add(here = next);
            }
            var health = session.PlayerTuning.Health;
            session.Player = new PlayerState { Parent = PlayerState.World, Position = path[0], Health = health };
            int leg = 1;
            uint start = world.Tick;
            double At(uint tick) => Math.Round((tick - start) * SimConstants.TickSeconds, 1);
            var events = new List<EnemyEvent>();
            // Each sign as it was shown, in the line's coordinates (s, lateral, height over the rail) for `dt screenshot --cam`:
            // where the walker's eyes were, and where the sign's were.
            var shown = new List<object>();
            double[] Line(Double3 p)
            {
                double h = session.Train.Dynamics.Distance;
                line.Nearest(p, ref h);
                var r = line.Sample(DarkTerritory.Sim.Rail.RailLine.MainPath, h);
                var right = Double3.Cross(r.Tangent, Double3.Up).Normalized;
                return [Math.Round(h, 1), Math.Round(Double3.Dot(p - r.Position, right), 2), Math.Round(p.Y - r.Position.Y, 2)];
            }
            double died = -1;
            // Note 795: why the director sent nothing, a second at a time, what held its cap, and which kinds' rules would have
            // let them come then (tonight's, their own gates).
            var held = new Dictionary<string, int>();
            var capped = new Dictionary<string, int>();
            var could = new Dictionary<string, int>();
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                if (i % SimConstants.TickRate == 0 && world.Director is { } dd && world.Enemies is { } et)
                {
                    string why = dd.HeldBecause ?? "sent";
                    held[why] = held.GetValueOrDefault(why) + 1;
                    if (why == "at the cap")
                        foreach (var e in world.ActiveEnemies.Where(Director.Engaged))
                            capped[$"{e.Kind}:{e.Phase}"] = capped.GetValueOrDefault($"{e.Kind}:{e.Phase}") + 1;
                    var ctx = new SpawnContext(world, et, dd);
                    foreach (var rule in Spawns.Rules)
                        if (dd.Allows(rule.Kind) && dd.Tonight(rule.Kind, world) && rule.Weight(ctx) is > 0)
                            could[rule.Kind.ToString()] = could.GetValueOrDefault(rule.Kind.ToString()) + 1;
                }
                if (session.Player.Alive && session.Player.Parent == PlayerState.World
                    && !world.ActiveEnemies.Any(e => e.Phase is SpinePhase.Grab or SpinePhase.Punish))
                {
                    var to = path[leg % path.Count] - session.Player.Position;
                    to = to with { Y = 0 };
                    if (to.Length < 1)
                        leg++;
                    else
                    {
                        var p = session.Player.Position + to.Normalized * Math.Min(to.Length, speed * SimConstants.TickSeconds);
                        session.Player = session.Player with { Position = p with { Y = PlayerMotor.GroundAt(p, line, ref hint) } };
                    }
                }
                session.Step(default);
                events.AddRange(world.EnemyEvents);
                if (world.Director is { } wd && wd.Signs.Count > shown.Count)
                    shown.Add(new { at = At(world.Tick), kind = world.Watcher.Kind.ToString(), eye = Line(session.Player.Position + Double3.Up * 1.6), sign = Line(world.Watcher.At) });
                if (!session.Player.Alive && died < 0)
                    died = i * SimConstants.TickSeconds;
            }
            var d = world.Director!;
            var engaged = events.Where(e => e.To == SpinePhase.Telegraph).DistinctBy(e => e.EnemyId).ToList();
            return new
            {
                afoot = halt || on,
                haltOwn = halt ? on : (bool?)null,
                stopSends = d.Log.Count(l => l.Stop),
                crewSeconds = d.AfootSeconds,
                signs = d.Signs.Count,
                fromSites = d.Signs.Count(s => s.FromSite),
                signKinds = d.Signs.GroupBy(s => s.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                firstSign = d.Signs.Count > 0 ? At(d.Signs[0].Tick) : (double?)null,
                spawns = d.Log.Select(l => $"{l.Kind} at {At(l.Tick).ToString("0", CultureInfo.InvariantCulture)} s").ToArray(),
                engaged = engaged.Select(e => $"{e.Kind} at {At(e.Tick).ToString("0", CultureInfo.InvariantCulture)} s").ToArray(),
                grabs = events.Count(e => e.To == SpinePhase.Grab),
                held,
                capped,
                could,
                // What the stop is to the director's rules, and which kinds are about tonight (director.nightChance).
                atFacility = world.Run is { Phase: DarkTerritory.Sim.Run.RunPhase.AtFacility },
                inSettlement = world.InSettlement,
                tonight = Spawns.Rules.Where(r => d.Tonight(r.Kind, world)).Select(r => r.Kind.ToString()).ToArray(),
                shown,
                died = died < 0 ? (double?)null : Math.Round(died),
                death = died < 0 ? null : session.Player.Death.ToString(),
            };
        }
        return new
        {
            route = spec,
            km = Math.Round(stop.Start / 1000, 2),
            stop = $"{stop.Kind} {stop.Facility} {stop.Start:0}-{stop.End:0} at {standAt:0}{(halt ? " (its halt)" : "")}",
            tags = route.Plan?.Director.Tags.Where(t => t.Edge == "main" && t.S1 >= stop.Start - 500 && t.S0 <= stop.End + 500).Select(t => $"{t.Tag} {t.S0:0}-{t.S1:0}").ToArray(),
            houses = stop.Stop!.Buildings.Count(b => b.Kind == BuildingKind.House && b.Zone == StopZone.Village),
            lairs = stop.Stop.Lairs.GroupBy(l => l.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            seconds,
            after = Walk(true),
            before = Walk(false),
        };
    }

    /// <summary>
    /// `dt afoot census` (note 848; the director's playtest of 16 Oct evening, line 7: "Monsters are positioned and ready at
    /// yards and abandoned villages"): every stop on many nights, by its kind (a yard, a yard and its village, a village on its
    /// own), with the train stood at it and one crewmate afoot in its yard and in its village. For each: the creature sites it
    /// lays (level-design H.2), what lives in its houses, and which stop creatures' own rules would let them come to that crew
    /// there (their weight above nothing, tonight's dice aside). The rules aren't changed by it; it says where they can act.
    /// </summary>
    public static object Census(string content, string[] args)
    {
        string specs = Str(args, "--routes", "local:1-4,frontier:1-4,deadLines:1-4");
        int cars = (int)Opt(args, "--cars", 6), crew = (int)Opt(args, "--crew", 4);
        var stops = new List<(string Kind, string Spot, IReadOnlyCollection<string> Could, IReadOnlyDictionary<string, int> Lairs, IReadOnlyCollection<string> Houses)>();
        var rows = new List<object>();
        foreach (var spec in Specs(specs))
        {
            var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars);
            foreach (var f in route.Features.Where(f => f.Stop is not null))
            {
                var stop = f.Stop!;
                var lairs = stop.Lairs.GroupBy(l => l.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count());
                string kind = stop.Kind.ToString();
                var spots = new List<(string Name, StopZone Zone)>();
                if (stop.HasYard)
                    spots.Add(("yard", StopZone.Yard));
                if (stop.HasVillage && stop.Buildings.Any(b => b.Zone == StopZone.Village))
                    spots.Add(("village", StopZone.Village));
                var at = new Dictionary<string, object>();
                foreach (var (name, zone) in spots)
                {
                    var (could, houses, standing, open, lived) = Stood(content, route, f, zone, cars, crew);
                    stops.Add((kind, name, could, lairs, houses));
                    at[name] = new { standsAt = standing, could, houses, openHouses = open, livedIn = lived };
                }
                rows.Add(new
                {
                    route = spec,
                    km = Math.Round(f.Start / 1000, 2),
                    kind,
                    feature = f.Kind.ToString(),
                    facility = f.Facility.ToString(),
                    yard = stop.Form?.ToString(),
                    village = stop.VillageForm?.ToString(),
                    lairs,
                    at
                });
            }
        }
        var rules = Spawns.Rules.Select(r => r.Kind.ToString()).ToList();
        // By stop kind and where the crewmate stood: how many such stops lay each site, and let each creature come.
        var byKind = stops.GroupBy(s => $"{s.Kind} ({s.Spot})").OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (object)new
        {
            stops = g.Count(),
            lay = Enum.GetNames<LairKind>().ToDictionary(l => l, l => g.Count(s => s.Lairs.ContainsKey(l))),
            could = rules.Where(r => g.Any(s => s.Could.Contains(r))).ToDictionary(r => r, r => g.Count(s => s.Could.Contains(r))),
            never = rules.Where(r => Director.Outdoors(Enum.Parse<EnemyKind>(r)) && !g.Any(s => s.Could.Contains(r))).ToArray(),
            houses = g.SelectMany(s => s.Houses).GroupBy(h => h).OrderBy(h => h.Key, StringComparer.Ordinal).ToDictionary(h => h.Key, h => h.Count()),
        });
        return new { routes = specs, crew, byKind, stops = rows };
    }

    /// <summary>
    /// The train stood at <paramref name="f"/>'s stop (its stop point on the main line) and braked, one crewmate afoot amid the
    /// buildings of <paramref name="zone"/>, a couple of seconds on: the stop creatures whose rules let them come, the creatures
    /// put in its houses, and whether the run counts the train as standing at a stop.
    /// </summary>
    static (List<string> Could, List<string> Houses, bool StandsAt, int Open, List<string> Lived) Stood(string content, Route route, RouteFeature f, StopZone zone, int cars, int crew)
    {
        var stop = f.Stop!;
        var session = new PrototypeSession(content, route, cars, enemies: true, crew: crew, at: f.Start + stop.StopPoint.S);
        var world = session.World;
        world.Run!.Resume(600, -1, 0, 0);
        session.Controls = session.Controls with { Throttle = 0, Brake = 1 };
        var built = stop.Buildings.Where(b => b.Zone == zone).ToList();
        var mid = new Pt(built.Average(b => b.S), built.Average(b => b.D));
        var line = session.Train.Line;
        double hint = 0;
        var p = DarkTerritory.Sim.Run.Run.StopWorld(line, f, mid);
        session.Player = new PlayerState { Parent = PlayerState.World, Position = p with { Y = PlayerMotor.GroundAt(p, line, ref hint) }, Health = session.PlayerTuning.Health };
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            session.Step(default);
        var could = new List<string>();
        if (world.Director is { } d && world.Enemies is { } t)
        {
            var ctx = new SpawnContext(world, t, d);
            could.AddRange(Spawns.Rules.Where(r => d.Allows(r.Kind) && r.Weight(ctx) is > 0).Select(r => r.Kind.ToString()));
        }
        var houses = world.ActiveEnemies.Where(e => !e.Gone && e.Kind is EnemyKind.Lodger or EnemyKind.Householder or EnemyKind.HollowHouse or EnemyKind.Hanger)
            .Select(e => e.Kind.ToString()).ToList();
        // Its open houses, and what lives in them by their own dice (tonight's aside).
        int fi = route.Features.ToList().IndexOf(f);
        var open = session.Train.Walls?.OpenHouses.Where(h => h.Feature == fi).ToList() ?? [];
        var lived = world.Enemies is { } et ? open.Select(h => Dwelling.Lives(route, stop, h, et.Dwellings, null)).OfType<EnemyKind>().Select(k => k.ToString()).ToList() : [];
        return (could, houses, world.Run!.StandsAt(session.Train) >= 0, open.Count, lived);
    }

    /// <summary>"frontier:1-4,local:7" as route specs, one per seed.</summary>
    static IEnumerable<string> Specs(string specs)
    {
        foreach (var part in specs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (tier, seeds) = (part[..part.IndexOf(':')], part[(part.IndexOf(':') + 1)..]);
            var range = seeds.Split('-');
            int a = int.Parse(range[0], CultureInfo.InvariantCulture), b = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : a;
            for (int seed = a; seed <= b; seed++)
                yield return $"{tier}:{seed}";
        }
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
