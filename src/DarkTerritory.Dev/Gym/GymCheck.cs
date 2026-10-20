using Ballast;
using Ballast.Render;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Dev.Gym;

/// <summary>
/// <c>dt gym</c> (GDD §34.1, ARCHITECTURE §8 note 676): the creature gym hosted headless as the app hosts it, every creature
/// called in turn from where the director stands at the start (the cab), and what came of it: whether it came, where, how far
/// off at the call and a few seconds on, and whether the caller lived; each framed in a photograph under the gym's light.
/// Then a death in the gym, and where the dead stood up again; and a creature put down in the safe ring, and whether it went.
/// </summary>
public static class GymCheck
{
    /// <param name="Aboard">It came on the train (a car, a gap, the tender): its rule picked the place, and its reach is the car's.</param>
    /// <param name="About">How many of what came are still about at the end (a Gannet that left, a pack that went, isn't).</param>
    /// <param name="AboardAfter">Any of them on the train at the end (the Cinder Hounds having come up and boarded).</param>
    public sealed record KindResult(string Kind, bool Came, string Where, int Count, double? NearestAtCall, double? NearestAfter,
        string? Phase, bool YouAlive, bool Aboard, string? Shot, int About = 0, bool AboardAfter = false);

    public sealed record RespawnResult(bool Alive, double Seconds, int Health, double FromRing, bool RunOver);
    public sealed record RingResult(int RibbitPutInRing, bool SentOff);
    public sealed record GymReport(string Route, object? Stop, double Front, double FrontNow, double Speed, string? Phase, double[] SafeRing,
        double SafeRadius, int Lamps, string? Overview, IReadOnlyList<KindResult> Kinds, int Came, int Of, IReadOnlyList<string> TooNear, IReadOnlyList<string> TooFar,
        RespawnResult Respawn, RingResult Ring);

    public static GymReport Run(string content, IReadOnlyList<EnemyKind>? only, double seconds, string? shots, int width = 960, int height = 540)
    {
        var gym = CreatureGym.Load(content);
        var setup = gym.Setup();
        using var night = NetPlaySession.HostGame(content, setup, port: null, bots: 0, listed: false);
        var host = night.Host!;
        gym.Begin(host);
        byte you = (byte)night.PlayerId;
        Step(night, gym, host, 2);
        WorldShot? shooter = null;
        if (shots is not null)
        {
            Directory.CreateDirectory(shots);
            shooter = new WorldShot(content, host.World.Route, width, height) { Grade = l => { gym.Light(ref l); return l; } };
        }
        string? overview = Shoot(shooter, host, $"subject:crew:{you}", shots, "00-gym");
        var results = new List<KindResult>();
        int n = 0;
        foreach (var kind in only ?? CreatureGym.Roster)
        {
            n++;
            host.World.Clear();
            Step(night, gym, host, 0.5);
            // Back in the cab each time, whole: what the last one did isn't the next one's start.
            host.Stand(you, Fresh(host, you));
            Step(night, gym, host, 0.2);
            var me = Me(host, you);
            var called = gym.Call(host, you, kind);
            double? at = Nearest(host, called.Ids, me);
            // On the train, or on the line beside it (pacing a coupling gap): the train's, where its rule put it.
            var train = host.World.Train;
            bool aboard = host.World.ActiveEnemies.FirstOrDefault(e => called.Ids.Contains(e.Id)) is { } first
                && (first.Attached >= 0 || first.Attached == -1 && first.LineDistance >= train.Dynamics.RearDistance - 10 && first.LineDistance <= train.Dynamics.Distance + 10);
            Step(night, gym, host, 1);
            string? shot = called.Ids.Count > 0 && host.World.ActiveEnemies.Any(e => e.Id == called.Ids[0] && !e.Gone)
                ? Shoot(shooter, host, $"subject:enemy:{called.Ids[0]}", shots, $"{n:00}-{Live.EnemyNames.Name(kind)}") : null;
            if (kind == EnemyKind.Waker && called.Ids.Count > 0)
                WakerHeld(night, gym, host, shooter, called.Ids[0], Math.Max(0, seconds - 1), shots, n);
            else
                Step(night, gym, host, Math.Max(0, seconds - 1));
            // The Choir comes a few seconds after it's called (it gathers first): its ghosts are what came.
            var ids = kind == EnemyKind.Choir ? [.. host.World.ActiveEnemies.Where(e => e.Kind == EnemyKind.Choir && !e.Gone).Select(e => e.Id)] : called.Ids;
            var still = host.World.ActiveEnemies.Where(e => ids.Contains(e.Id) && !e.Gone).ToList();
            results.Add(new KindResult(Live.EnemyNames.Name(kind), called.Came, called.Where, Math.Max(called.Ids.Count, still.Count), Round(at),
                Round(Nearest(host, [.. still.Select(e => e.Id)], Me(host, you))), still.FirstOrDefault()?.Phase.ToString(),
                host.Players.First(p => p.Id == you).State.Alive, aboard, shot, still.Count, still.Any(e => e.Attached >= 0)));
        }
        host.World.Clear();
        Step(night, gym, host, 0.5);
        return new GymReport(gym.Tuning.Route,
            gym.Stop is { } s ? new { s.Facility, kind = s.Stop?.Kind.ToString(), start = Math.Round(s.Start), end = Math.Round(s.End), points = gym.FirstPoints is { } fp ? Math.Round(fp) : (double?)null, village = Math.Round(gym.Village) } : null,
            Math.Round(gym.Front), Math.Round(host.World.Train.Dynamics.Distance, 1), Math.Round(host.World.Train.Dynamics.Speed, 2),
            host.World.Run?.Phase.ToString(), V(gym.SafeSpot), gym.Tuning.SafeRing.Radius,
            host.World.Bodies.All.Count(b => b.Kind == Sim.Physics.BodyKind.Lamp && b.Parent == PlayerState.World),
            overview, results, results.Count(r => r.Came), results.Count,
            // Off the train, nothing should come nearer than the gym's distance (what's aboard is where its rule put it).
            [.. results.Where(r => r.Came && !r.Aboard && r.NearestAtCall < gym.Tuning.MinDistance).Select(r => r.Kind)],
            // And nothing farther than its reach (the director, 13 Oct: "nothing should spawn more than 200m off"); the Tippy
            // Toesie has nowhere to be till it's behind someone.
            [.. results.Where(r => r.Came && r.Kind != "tippyToesie" && r.NearestAtCall > gym.Tuning.MaxDistance).Select(r => r.Kind)],
            Respawn(night, gym, host, you), Ring(night, gym, host));
    }

    /// <summary>Dead in the gym: how long till standing again, and where (it should be the safe ring, whole).</summary>
    static RespawnResult Respawn(NetPlaySession night, CreatureGym gym, HostSession host, byte you)
    {
        var was = host.Players.First(p => p.Id == you).State;
        host.Stand(you, was with { Health = 0, Death = DeathCause.Mauled });
        double waited = 0;
        while (waited < gym.Tuning.RespawnSeconds + 3 && !host.Players.First(p => p.Id == you).State.Alive)
        {
            Step(night, gym, host, 0.25);
            waited += 0.25;
        }
        var back = host.Players.First(p => p.Id == you).State;
        var at = PlayerMotor.WorldPosition(back, host.World.Train);
        return new RespawnResult(back.Alive, waited, back.Health, Math.Round(((at - gym.SafeSpot) with { Y = 0 }).Length, 2), host.World.Run?.Over == true);
    }

    /// <summary>A Ribbit put down in the safe ring: it should be sent off within a tick or two.</summary>
    static RingResult Ring(NetPlaySession night, CreatureGym gym, HostSession host)
    {
        var world = host.World;
        var r = world.AddEnemy(i => Ribbit.At(i, i, gym.SafeSpot, world.Enemies!.Ribbits));
        Step(night, gym, host, 0.2);
        return new RingResult(r.Id, !world.ActiveEnemies.Any(e => e.Id == r.Id && !e.Gone));
    }

    static PlayerState Fresh(HostSession host, byte you)
    {
        var was = host.Players.First(p => p.Id == you).State;
        return PlayerMotor.SpawnInCab(host.World.Train, host.PlayerTuning) with { Placed = (byte)(was.Placed + 1) };
    }

    static Double3 Me(HostSession host, byte you) => PlayerMotor.WorldPosition(host.Players.First(p => p.Id == you).State, host.World.Train);

    static double? Nearest(HostSession host, IReadOnlyList<int> ids, Double3 me)
    {
        // A hidden Tippy Toesie is nowhere yet (its place is the line's start), so it isn't counted.
        var at = host.World.ActiveEnemies.Where(e => ids.Contains(e.Id) && !e.Gone && e is not TippyToesie { Hidden: true }).Select(e => ((e.WorldPosition(host.World.Train) - me) with { Y = 0 }).Length).ToList();
        return at.Count == 0 ? null : at.Min();
    }

    static void Step(NetPlaySession night, CreatureGym gym, HostSession host, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * SimConstants.TickRate); i++)
        {
            night.Step(default);
            gym.Step(host, host.Tick * SimConstants.TickSeconds);
        }
    }

    /// <summary>
    /// A Waker called (note 902): its <paramref name="seconds"/> stepped, and once it has the train, shot through its hold (its
    /// hands down to the last car, the car up at its mouth, the first bite, its hands down for the next, that one up).
    /// </summary>
    static void WakerHeld(NetPlaySession night, CreatureGym gym, HostSession host, WorldShot? shooter, int id, double seconds, string? shots, int n)
    {
        double[] marks = [1.5, 5, 8.6, 10.4, 12];
        int next = 0;
        for (double s = 0; s < seconds; s += 0.25)
        {
            Step(night, gym, host, 0.25);
            if (next < marks.Length && host.World.ActiveEnemies.FirstOrDefault(e => e.Id == id) is Sim.Enemies.Waker { Holds: true } w && w.Extra >= marks[next]
                && host.World.Enemies?.Wakers is { } t && WakerLift.Of(host.World.Train.Frames, host.World.Train, w, t) is { } hold)
            {
                // From out beside the line, level with his hands, at him and the car he has (a subject view's too close for a titan).
                var rail = host.World.Train.Frames[hold.Car];
                var side = (rail.Right with { Y = 0 }).Normalized;
                var at = hold.Stand - (rail.Back with { Y = 0 }).Normalized * 10 + Double3.Up * 16;
                if (shooter is not null && shots is not null)
                {
                    string path = Path.Combine(shots, $"{n:00}-waker-held-{marks[next]:0.0}.png");
                    shooter.Save(path, shooter.Render(host.World, [.. host.Players], Camera.LookAt(at + side * 90 + Double3.Up * 4, at, 60)));
                }
                next++;
            }
        }
    }

    static string? Shoot(WorldShot? shooter, HostSession host, string view, string? dir, string name)
    {
        if (shooter is null || dir is null)
            return null;
        try
        {
            var crew = host.Players.ToList();
            var aim = WorldShot.Aim(view, host.World, crew, aspect: (double)shooter.Width / shooter.Height, player: host.PlayerTuning);
            var pixels = shooter.Render(host.World, crew, aim.Camera);
            string path = Path.Combine(dir, name + ".png");
            shooter.Save(path, pixels);
            return Path.GetFullPath(path);
        }
        catch (ArgumentException e)
        {
            return "no shot: " + e.Message;
        }
    }

    static double? Round(double? v) => v is { } x ? Math.Round(x, 1) : null;
    static double[] V(Double3 v) => [Math.Round(v.X, 1), Math.Round(v.Y, 1), Math.Round(v.Z, 1)];
}
