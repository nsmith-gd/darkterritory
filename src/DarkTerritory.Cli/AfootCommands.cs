using System.Globalization;
using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
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
        var stop = route.Features.FirstOrDefault(f => f.Stop?.Buildings.Count(b => b.Kind == BuildingKind.House && b.Zone == StopZone.Village) >= 3)
            ?? throw new InvalidOperationException($"{spec} has no stop with a village");
        object Walk(bool on)
        {
            var session = new PrototypeSession(content, route, cars, enemies: true, crew: 1, at: stop.Start + stop.Stop!.StopPoint.S);
            var world = session.World;
            if (!on)
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
            double died = -1;
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
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
                if (!session.Player.Alive && died < 0)
                    died = i * SimConstants.TickSeconds;
            }
            var d = world.Director!;
            var engaged = events.Where(e => e.To == SpinePhase.Telegraph).DistinctBy(e => e.EnemyId).ToList();
            return new
            {
                afoot = on,
                crewSeconds = d.AfootSeconds,
                signs = d.Signs.Count,
                fromSites = d.Signs.Count(s => s.FromSite),
                signKinds = d.Signs.GroupBy(s => s.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                firstSign = d.Signs.Count > 0 ? At(d.Signs[0].Tick) : (double?)null,
                spawns = d.Log.Select(l => $"{l.Kind} at {At(l.Tick).ToString("0", CultureInfo.InvariantCulture)} s").ToArray(),
                engaged = engaged.Select(e => $"{e.Kind} at {At(e.Tick).ToString("0", CultureInfo.InvariantCulture)} s").ToArray(),
                grabs = events.Count(e => e.To == SpinePhase.Grab),
                died = died < 0 ? (double?)null : Math.Round(died),
                death = died < 0 ? null : session.Player.Death.ToString(),
            };
        }
        return new
        {
            route = spec,
            km = Math.Round(stop.Start / 1000, 2),
            houses = stop.Stop!.Buildings.Count(b => b.Kind == BuildingKind.House && b.Zone == StopZone.Village),
            lairs = stop.Stop.Lairs.GroupBy(l => l.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            seconds,
            after = Walk(true),
            before = Walk(false),
        };
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
