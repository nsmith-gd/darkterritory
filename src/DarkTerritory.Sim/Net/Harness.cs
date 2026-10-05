using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

public sealed record HarnessOptions
{
    public int Bots { get; init; } = 8;
    public int Cars { get; init; } = 10;
    public double Seconds { get; init; } = 120;
    public int Seed { get; init; } = 1;
    public LinkConditions Link { get; init; } = LinkConditions.Rough;
    public double StartDistance { get; init; } = 600;
    public CombatTuning? Combat { get; init; }
    /// <summary>With enemy tuning the host runs the director and the route's threats.</summary>
    public EnemyTuning? Enemies { get; init; }
    public Route.Route? Route { get; init; }
    /// <summary>Real UDP sockets on localhost instead of the simulated network (runs in real time's order, not faster).</summary>
    public bool Udp { get; init; }
    /// <summary>With a route: run the night as a game (departure, facilities, terminus, dawn) and report the result.</summary>
    public Run.RunTuning? Run { get; init; }
    public double YardLength { get; init; } = 600;
    /// <summary>The contract's freight the cars leave with (GDD §9, §19; note 182). Goods without one.</summary>
    public CargoKind Cargo { get; init; } = CargoKind.Goods;
    /// <summary>
    /// The facilities' loading modules. With a crew for it (a driver, a shunter and two for the winch: five bots, or four
    /// with the gunner lending a hand), the bots stop at the winch facilities and load (T32).
    /// </summary>
    public Run.FacilityTuning? Facilities { get; init; }
    /// <summary>With it (and a run), the dead come back through the route's Holdouts (GDD App. D); bots don't breach them yet.</summary>
    public Run.HoldoutTuning? Holdouts { get; init; }
    /// <summary>With a route, the line's boards and what they warn of (sight.json): posted curves, tunnel mouths, Grease.</summary>
    public Route.SightTuning? Sight { get; init; }
    /// <summary>
    /// On a night (a run), the crew board on foot (T102, playtest: "I dont understand how Bots are doing test runs if they
    /// cannot traverse into all the car positions they need"): all but the first join standing on the ballast beside the
    /// train at the gate, and walk and climb to their posts. False: they're put there, as before.
    /// </summary>
    public bool WalkAboard { get; init; } = true;
    /// <summary>Another network to run over (the CLI's fake Steam lobby), in place of the loopback or UDP.</summary>
    public IHarnessNetwork? Network { get; init; }
    /// <summary>
    /// Called every tick after everyone has stepped, with each bot and its player's state on the host, and the host's world
    /// (to trace a night).
    /// </summary>
    public Action<uint, IReadOnlyList<(IBot Bot, PlayerState State)>, World>? Observe { get; init; }
    /// <summary>
    /// Called every tick on the host's world before it steps: a scripted failure (the cascade audit's boiler rupture, a fire
    /// lit, a coupling cut; note 186). The world's own rules take it from there, and the bots answer it through intent.
    /// </summary>
    public Action<uint, World>? Script { get; init; }
    /// <summary>The night ends early once this holds on the host's world (a cascade recovered, or past saving).</summary>
    public Func<World, bool>? Until { get; init; }
    /// <summary>The combination audit (note 186): only these kinds, insisted on (<see cref="World.Insist"/>). Null: the director's night.</summary>
    public IReadOnlyList<EnemyKind>? Insist { get; init; }
    /// <summary>With <see cref="Insist"/>: seconds after one's gone before it's sent again.</summary>
    public double InsistEvery { get; init; } = 10;
    /// <summary>
    /// With <see cref="Insist"/>: the look-out's errand (note 212), the last walker's, to the Gaunt, Ribbits or a Dragger
    /// insisted on. Null: the crew keep to their posts.
    /// </summary>
    public LookTuning? Look { get; init; }
    /// <summary>What the night's line takes away (GDD §22; note 186), laid over the line for host and clients alike.</summary>
    public HazardSet? Hazards { get; init; }
    /// <summary>
    /// The crew's calls over a poor voice (GDD §34 "degraded comms"; note 186): lossy, laggy, talked over. Seeded from
    /// <see cref="Seed"/>. Null: perfect comms.
    /// </summary>
    public VoiceConditions? Voice { get; init; }
}

/// <summary>Transports for the harness from elsewhere: the Sim doesn't reference platform code, so the CLI brings it.</summary>
public interface IHarnessNetwork : IDisposable
{
    string Name { get; }
    ITransport Host();
    ITransport Client(int index);
    /// <summary>Once a tick, before anyone steps: pump the platform.</summary>
    void Pump();
}

public sealed record ClientReport(byte Id, string Bot, double MaxCorrectionM, int Corrections, int Snapshots, int HostMissedInputs,
    bool Alive, string Surface, string Where, long BytesUp, long BytesDown);

/// <param name="WarmUps">Times a bot went in out of the cold (down to a coupler plate, in through a door and shut it; T31).</param>
/// <param name="Stops">The facility stops the crew worked, as the driver saw them (T32).</param>
public sealed record HarnessReport(int Ticks, double Seconds, string Link, double TrainDistance, double TrainSpeed, double BoilerPressure, double Tender,
    int SnapshotBytes, double DownKbpsPerClient, double UpKbpsPerClient, double MaxCorrectionM, int Deaths,
    IReadOnlyList<ClientReport> Clients, ThreatReport? Threats = null, Run.RunReport? Run = null, int WarmUps = 0,
    IReadOnlyList<StopRecord>? Stops = null, PacingReport? Pacing = null)
{
    /// <summary>What got through on the crew's voice, with <see cref="HarnessOptions.Voice"/> (note 186).</summary>
    public VoiceReport? Voice { get; init; }
    /// <summary>
    /// Seconds from boarding until each bot first reached its post (T102): the driver and fireman in the cab, the gunner on
    /// the guard gun, a walker up on the train; −1 for never. Keyed "bot#id".
    /// </summary>
    public IReadOnlyDictionary<string, double>? Posts { get; init; }
}

/// <summary>
/// How often something happened (after the playtest: "a reward or a problem every 30 s at most, ideally 20"): the moments
/// the world logged (<see cref="World.Beats"/>), and the stretches out on the line with nothing going on
/// (<see cref="World.QuietSeconds"/>). <paramref name="Kinds"/> counts the beats by what they were.
/// </summary>
public sealed record PacingReport(int Beats, double BeatsPerMinute, double LongestQuietSeconds, double P95QuietSeconds, double MeanQuietSeconds,
    int QuietOver30, double QuietShare, IReadOnlyDictionary<string, int> Kinds)
{
    /// <summary>When the longest quiet stretch ended (seconds into the night), and what the run was doing (for finding it).</summary>
    public string? LongestQuietEnded { get; init; }
    /// <summary>Every stretch quiet over 30 s: how long, when it ended and where, by what, and why the director had sent nothing.</summary>
    public IReadOnlyList<string> LongQuiets { get; init; } = [];
}

/// <summary>What the director and the enemies did (GDD §34 / App. B.9 audit).</summary>
public sealed record ThreatReport(double Budget, double Spent, IReadOnlyDictionary<string, int> Spawned, IReadOnlyDictionary<string, int> Punishes,
    IReadOnlyDictionary<string, int> DeathsByCause, int FairnessViolations, bool Derailed, double ChoirPeak, double MeanCargoIntegrity, int RoundsFired)
{
    /// <summary>What derailed the train, if it was (<see cref="World.DerailCause"/>).</summary>
    public string? DerailCause { get; init; }
    /// <summary>The conflict-table pairs the director put together (App. B.1).</summary>
    public IReadOnlyList<string> Pairs { get; init; } = [];
    /// <summary>Seconds out on the line by what the director did each second: sent something, or why it held back (T114).</summary>
    public IReadOnlyDictionary<string, int> Director { get; init; } = new Dictionary<string, int>();
    /// <summary>While it held back at the cap: the engaged threats that filled it, by kind and phase, in seconds.</summary>
    public IReadOnlyDictionary<string, int> AtTheCap { get; init; } = new Dictionary<string, int>();
    /// <summary>GRABs begun, by kind (App. A.1): the combination audit's "it landed something" (note 186).</summary>
    public IReadOnlyDictionary<string, int> Grabs { get; init; } = new Dictionary<string, int>();
    /// <summary>Enemies that came on (telegraphed, or further), by kind: what the night actually exercised.</summary>
    public IReadOnlyDictionary<string, int> Engaged { get; init; } = new Dictionary<string, int>();
    /// <summary>GRABs a crewmate broke (the grab ended in a break-off, not a punish), by kind.</summary>
    public IReadOnlyDictionary<string, int> Rescues { get; init; } = new Dictionary<string, int>();
    /// <summary>The dead's votes cast (GDD v1.4 App. D.11; the bots' too, note 202), by creature.</summary>
    public IReadOnlyDictionary<string, int> Votes { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Host plus N bot clients in one process over a <see cref="LoopbackNetwork"/> with simulated lag and loss,
/// faster than real time (GDD §34 "Stability: desync and crash detection across 2–8 clients").
/// </summary>
public static class Harness
{
    public static HarnessReport Run(RailLine line, TrainTuning trainTuning, PlayerTuning playerTuning, HarnessOptions o, BoilerTuning? boiler = null)
    {
        var net = new LoopbackNetwork(o.Seed, o.Link);
        var udpHost = o.Udp ? UdpTransport.Host(0, bind: System.Net.IPAddress.Loopback) : null;
        ITransport ClientTransport(int i) => o.Network?.Client(i)
            ?? (udpHost is null ? net.CreateClient() : UdpTransport.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, udpHost.Port)));
        var hostTransport = new CountingTransport(o.Network?.Host() ?? udpHost ?? net.CreateHost());
        var host = new HostSession(hostTransport, NewTrain(line, trainTuning, o, boiler), trainTuning, playerTuning, o.Combat);
        if (o.Hazards is { } hazards)
            HazardConditions.Apply(line, hazards);
        // Insisted on (note 186), the director's roster is those kinds alone: no Sleepers or marsh on the line, no Stoker but
        // one insisted on (an empty roster is every kind, so nothing's roster is a name that's none of them).
        var enemyTuning = o.Enemies is { } full && o.Insist is { } only
            ? full with { Director = full.Director with { Roster = only.Count == 0 ? ["none"] : [.. only.Select(Director.Key)] } }
            : o.Enemies;
        if (enemyTuning is { } et)
            host.EnableEnemies(et, o.Route, (ulong)o.Seed, o.Bots);
        if (o.Insist is { } insist)
        {
            host.World.Insist = insist;
            host.World.InsistEvery = o.InsistEvery;
        }
        if (o.Hazards is { LampsOut: true })
        {
            host.World.SmashLamp(o.Seconds + 60);
            foreach (var v in host.Train.Vehicles)
                v.LampLit = false;
        }
        host.World.EnableBodies();
        host.World.Stock();
        if (o.Run is { } rt && o.Route is { } route)
            host.World.EnableRun(rt, route, o.YardLength, authority: true, o.Facilities);
        if (o.Holdouts is { } ht && o.Run is not null && o.Route is { } hroute)
            host.World.EnableHoldouts(ht, hroute);
        if (o.Sight is { } sight && o.Route is { } sightRoute)
            host.World.EnableLineside(sight, sightRoute);
        bool walk = o.WalkAboard && o.Run is not null && o.Route is not null;
        if (walk)
            // On the ballast on the right, beside the cars in turn (the platform's side at the home fortress).
            host.BoardAt = n =>
            {
                var train = host.Train;
                // The train's own cars, not a switchyard's out on its sidings (note 187).
                int car = n % train.OwnVehicles;
                var frame = train.Frames[car];
                var at = frame.ToWorld(new Double3(frame.Shape.HalfWidth + 2.2, 0, (n / train.OwnVehicles % 3 - 1) * 3.0));
                double along = train.Cars[car].FrontDistance - frame.Shape.HalfLength;
                return PlayerMotor.SpawnOnGround(at, train.Line, along, host.PlayerTuning);
            };

        // On a night with facilities, the crew call to each other at the stops, and each has a part (BotCrew.Make).
        var calls = o.Run is not null && o.Route is not null && o.Facilities is not null ? new CrewCalls() : null;
        if (calls is not null && o.Voice is { } voice)
            calls.Voice = new CrewVoice(voice, (ulong)o.Seed);
        var clients = new List<(ClientSession Session, IBot Bot, CountingTransport Transport)>();
        for (int i = 0; i < o.Bots; i++)
        {
            var transport = new CountingTransport(ClientTransport(i));
            IBot bot = BotCrew.Make(i, o.Bots, calls, o.Combat, playerTuning, o.Seed);
            var session = new ClientSession(transport, NewTrain(line, trainTuning, o, boiler), trainTuning, playerTuning, o.Combat);
            // The enemies' tuning, as a joiner loads it: prediction drags with the Weight as the host does (T59), and the bots
            // read their counters from it (the Gaunt's view, the Passenger's reach).
            if (enemyTuning is { } cet)
                session.World.EnableEnemies(cet, o.Route, (ulong)o.Seed, o.Bots, authority: false);
            // Clients see the night as players do: the phase, and each site's winch (mirrored from the host).
            if (o.Run is { } crt && o.Route is { } croute)
                session.World.EnableRun(crt, croute, o.YardLength, authority: false, o.Facilities);
            if (o.Holdouts is { } h && o.Run is not null && o.Route is { } hr)
                session.World.EnableHoldouts(h, hr);
            if (o.Sight is { } csight && o.Route is { } lroute)
                session.World.EnableLineside(csight, lroute);
            clients.Add((session, bot, transport));
        }
        // An insisted night's look-out (note 212): the last walker goes and looks at what lies in wait for it.
        if (o.Insist is { } looked && o.Look is { } look && clients.Select(c => c.Bot).OfType<RoofWalkerBot>().LastOrDefault() is { } lookout)
            lookout.Errand = new LookErrand(looked, look);

        int ticks = (int)(o.Seconds * SimConstants.TickRate);
        var posted = new Dictionary<byte, double>();
        var events = new List<EnemyEvent>();
        var deaths = new Dictionary<string, int>();
        double choirPeak = 0;
        var held = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var capped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int rounds = 0;
        var quiet = new List<double>();
        var beatKinds = new Dictionary<string, int>();
        int beats = 0, outTicks = 0, quietTicks = 0;
        double lastQuiet = 0;
        string? longestEnded = null;
        var longQuiets = new List<string>();
        string? heldAt20 = null;
        for (uint t = 0; t < ticks; t++)
        {
            if (host.World.Run is { Over: true } || o.Until?.Invoke(host.World) == true)
            {
                ticks = (int)t;
                break;
            }
            net.Advance(SimConstants.TickSeconds);
            o.Network?.Pump();
            o.Script?.Invoke(t, host.World);
            host.Step();
            events.AddRange(host.World.EnemyEvents);
            rounds += host.World.Shots.Count;
            beats += host.World.Beats.Count;
            foreach (var b in host.World.Beats)
                beatKinds[b] = beatKinds.GetValueOrDefault(b) + 1;
            double q = host.World.QuietSeconds;
            if (q < lastQuiet && lastQuiet > 0)
            {
                if (lastQuiet > quiet.DefaultIfEmpty(0).Max())
                    longestEnded = $"{t * SimConstants.TickSeconds:0}s at {host.Train.Dynamics.Distance:0} m, {host.World.Run?.Phase}, by {string.Join(",", host.World.Beats)}";
                if (lastQuiet > 30)
                    longQuiets.Add($"{lastQuiet:0.0}s to {t * SimConstants.TickSeconds:0}s at {host.Train.Dynamics.Distance:0} m, {host.World.Run?.Phase}, by {string.Join(",", host.World.Beats)}; director: {heldAt20 ?? "sent something"}");
                quiet.Add(lastQuiet);
            }
            if (host.World.Run is not { Phase: Sim.Run.RunPhase.Yard or Sim.Run.RunPhase.Arrived or Sim.Run.RunPhase.Failed })
            {
                outTicks++;
                if (q > 0)
                    quietTicks++;
            }
            // What the director was holding back for, once it's been quiet long enough that it should have sent something.
            if (q >= 20 && lastQuiet < 20)
                heldAt20 = host.World.Director?.HeldBecause;
            lastQuiet = q;
            choirPeak = Math.Max(choirPeak, host.World.Choir.Build);
            // T114 ("where are all the monsters"): what the director did with each second out on the line.
            if (t % SimConstants.TickRate == 1 && host.World.Director is { } dir && host.World.Run is { Phase: not (Sim.Run.RunPhase.Yard or Sim.Run.RunPhase.Arrived or Sim.Run.RunPhase.Failed) })
            {
                string what = host.World.Derailed ? "derailed" : dir.HeldBecause ?? "sent";
                held[what] = held.GetValueOrDefault(what) + 1;
                if (what == "at the cap")
                    foreach (var e in host.World.ActiveEnemies.Where(DarkTerritory.Sim.Enemies.Director.Engaged))
                        capped[$"{e.Kind}:{e.Phase}"] = capped.GetValueOrDefault($"{e.Kind}:{e.Phase}") + 1;
            }
            // Once everyone's in, the gunner goes to the guard gun (a host-side respawn at their post), unless they walk to it.
            if (t == 30 && !walk)
            {
                PostGunner(host, clients.Select(c => (c.Session, c.Bot)).ToList());
                PostFireman(host, clients.Select(c => (c.Session, c.Bot)).ToList());
            }
            if (t == 60)
                foreach (var c in clients)
                    c.Session.ResetStats();
            calls?.Advance(t);
            foreach (var (session, bot, _) in clients)
            {
                var intent = session.Connected ? BotCrew.Think(session, bot, (uint)t, calls) : default;
                session.Step(intent);
            }
            // Who's got to their post, and when (T102).
            foreach (var (session, bot, _) in clients)
                if (session.PlayerId is { } pid && !posted.ContainsKey(pid) && host.Players.FirstOrDefault(p => p.Id == pid) is { } hp && hp.State.Alive
                    && AtPost(bot, hp.State, host.World))
                    posted[pid] = t * SimConstants.TickSeconds;
            if (o.Observe is { } observe)
            {
                var states = host.Players.ToDictionary(p => p.Id, p => p.State);
                observe(t, [.. clients.Select(c => (c.Bot, c.Session.PlayerId is { } pid ? states.GetValueOrDefault(pid) : default))], host.World);
            }
        }

        var hostPlayers = host.Players.ToDictionary(p => p.Id, p => p.State);
        var reports = clients.Select(c =>
        {
            byte id = c.Session.PlayerId ?? 0;
            var s = hostPlayers.GetValueOrDefault(id);
            string where = s.Parent == PlayerState.World ? "ground" : s.Parent == 0 ? "engine" : $"car {s.Parent}";
            return new ClientReport(id, c.Bot.Name, Math.Round(c.Session.MaxCorrection, 4), c.Session.Corrections, c.Session.SnapshotsReceived,
                host.MissedInputs(id), s.Alive, s.Surface.ToString(), where, c.Transport.BytesSent, c.Transport.BytesReceived);
        }).ToList();

        double seconds = ticks * SimConstants.TickSeconds;
        ThreatReport? threats = null;
        if (host.World.Director is { } d)
        {
            foreach (var p in host.Players.Where(p => !p.State.Alive))
                deaths[p.State.Death.ToString()] = deaths.GetValueOrDefault(p.State.Death.ToString()) + 1;
            int unfair = events.Count(e => e.To == SpinePhase.Commit && (e.From != SpinePhase.Telegraph || e.SecondsInFrom < o.Enemies!.MinReactionSeconds - 1e-9));
            threats = new ThreatReport(Math.Round(d.Budget, 1), Math.Round(d.Spent, 1),
                d.Log.GroupBy(l => l.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                events.Where(e => e.To == SpinePhase.Punish).GroupBy(e => e.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                deaths, unfair, host.World.Derailed, Math.Round(choirPeak, 1),
                Math.Round(host.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).DefaultIfEmpty().Average(v => v?.CargoIntegrity ?? 1), 3), rounds)
            {
                Pairs = [.. d.Pairs],
                DerailCause = host.World.DerailCause,
                Director = held,
                AtTheCap = capped,
                Grabs = Count(events.Where(e => e.To == SpinePhase.Grab)),
                Engaged = Count(events.Where(e => e.To == SpinePhase.Telegraph).DistinctBy(e => e.EnemyId)),
                Rescues = Count(events.Where(e => e.From == SpinePhase.Grab && e.To is SpinePhase.BreakOff or SpinePhase.Gone)),
                Votes = new SortedDictionary<string, int>(d.Votes.GroupBy(v => v.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal),
            };
        }
        if (o.Udp || o.Network is not null)
        {
            foreach (var c in clients)
                c.Transport.Dispose();
            hostTransport.Dispose();
        }
        string link = o.Network is { } n ? n.Name : o.Udp ? "udp localhost" : $"{o.Link.LatencySeconds * 1000:0}ms ±{o.Link.JitterSeconds * 1000:0} loss {o.Link.LossRate:P0}";
        var pacing = Pace(quiet, lastQuiet, beats, outTicks, quietTicks, beatKinds);
        pacing = pacing with { LongestQuietEnded = lastQuiet > 0 && lastQuiet >= quiet.DefaultIfEmpty(0).Max() ? $"{seconds:0}s, the night's end" : longestEnded, LongQuiets = longQuiets };
        return new HarnessReport(ticks, seconds, link,
            Math.Round(host.Train.Dynamics.Distance, 1), Math.Round(host.Train.Dynamics.Speed, 2),
            Math.Round(host.Train.Boiler.Pressure, 1), Math.Round(host.Train.Boiler.Tender), host.LastSnapshotBytes,
            Math.Round(reports.Average(r => r.BytesDown) * 8 / 1000 / seconds, 1), Math.Round(reports.Average(r => r.BytesUp) * 8 / 1000 / seconds, 1),
            reports.Max(r => r.MaxCorrectionM), reports.Count(r => !r.Alive), reports, threats,
            host.World.Run is { } run ? run.Report ?? run.Tally(host.World, [.. host.Players.Select(p => p.State)]) : null,
            clients.Sum(c => c.Bot switch { RoofWalkerBot r => r.WarmUps, GunnerBot g => g.WarmUps, _ => 0 }),
            clients.Select(c => c.Bot).OfType<ConductorBot>().FirstOrDefault()?.Stops?.Log,
            pacing)
        {
            Voice = calls?.Voice?.Report(),
            Posts = clients.Where(c => c.Session.PlayerId is not null).ToDictionary(c => $"{c.Bot.Name}#{c.Session.PlayerId}",
                c => Math.Round(posted.TryGetValue(c.Session.PlayerId!.Value, out var at) ? at : -1, 1)),
        };
    }

    static SortedDictionary<string, int> Count(IEnumerable<EnemyEvent> events) =>
        new(events.GroupBy(e => e.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);

    /// <summary>A bot at its post (T102): in the cab at the controls or the fire, at the guard gun, or up on the train.</summary>
    static bool AtPost(IBot bot, in PlayerState s, World world) => bot switch
    {
        ConductorBot => PlayerMotor.InCab(s, world.Train),
        GunnerBot => world.Combat is { } c && Guns.MannedGun(s, world.Train, c.Guns) is not null,
        _ => s.Parent != PlayerState.World && s.Surface != Surface.Ladder,
    };

    static PacingReport Pace(List<double> quiet, double last, int beats, int outTicks, int quietTicks, Dictionary<string, int> kinds)
    {
        if (last > 0)
            quiet.Add(last);
        var sorted = quiet.Order().ToList();
        double minutes = Math.Max(1e-9, outTicks * SimConstants.TickSeconds / 60);
        return new PacingReport(beats, Math.Round(beats / minutes, 1), Math.Round(sorted.DefaultIfEmpty(0).Max(), 1),
            Math.Round(sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(sorted.Count * 0.95))], 1),
            Math.Round(sorted.DefaultIfEmpty(0).Average(), 1), sorted.Count(s => s > 30),
            Math.Round(outTicks == 0 ? 0 : (double)quietTicks / outTicks, 3),
            kinds.GroupBy(k => k.Key.Split(':')[0] == "board" || k.Key.StartsWith("caught") || k.Key.StartsWith("missed") || k.Key.StartsWith("run") ? k.Key : k.Key.Split(':')[0] + (k.Key.EndsWith("Punish") ? ":punish" : ""))
                .OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Value)));
    }

    /// <summary>A bot and what it's doing, in a few words (the harness trace).</summary>
    public static string Describe(IBot bot, in PlayerState s)
    {
        string where = !s.Alive ? $"dead ({s.Death})" : s.Parent == PlayerState.World ? "ground" : $"{s.Surface} {s.Parent}";
        // Hurt or held: what's happening to them shows in the trace.
        if (s.Alive && s.Health < 100)
            where += $" hp{s.Health}";
        if (s.Has(PlayerFlags.Held))
            where += " HELD";
        string doing = bot switch
        {
            ConductorBot { Driving: false } f => f.Venting ? "venting" : "firing",
            ConductorBot c => c.Sanding ? "sanding" : c.Stops?.Doing.ToString() ?? "",
            RoofWalkerBot { KitStep: { } k } => $"kit:{k}",
            RoofWalkerBot { TendStep: { } t } => $"tend:{t}",
            RoofWalkerBot { Errand.Doing: { } l } => l,
            RoofWalkerBot r => r.WarmUpStep is { } w and not "Off" ? $"warm:{w}" : r.Job?.Doing ?? "",
            GunnerBot { KitStep: { } k } => $"kit:{k}",
            GunnerBot g => g.Saving ? "saving the gun" : g.TendStep is { } t ? $"tend:{t}" : g.WarmUpStep is { } w and not "Off" ? $"warm:{w}" : g.Job?.Doing ?? "",
            _ => "",
        };
        return doing.Length > 0 ? $"{bot.Name}[{doing}] {where}" : $"{bot.Name} {where}";
    }

    /// <summary>The crew size from which one of them rides in the cab as fireman (T75).</summary>
    public const int FiremanFrom = 6;

    /// <summary>The fireman into the cab beside the driver, on the left (a host-side respawn at their post, as the gunner's).</summary>
    static void PostFireman(HostSession host, List<(ClientSession Session, IBot Bot)> clients)
    {
        var fireman = clients.FirstOrDefault(c => c.Bot is ConductorBot { Fireman: true });
        if (fireman.Session?.PlayerId is not { } id)
            return;
        host.SetPlayerState(id, PlayerMotor.SpawnInCab(host.Train, host.PlayerTuning, -0.8));
    }

    static void PostGunner(HostSession host, List<(ClientSession Session, IBot Bot)> clients)
    {
        var gunner = clients.FirstOrDefault(c => c.Bot is GunnerBot);
        if (gunner.Session?.PlayerId is not { } id)
            return;
        var guard = host.Train.Dynamics.Consist.Vehicles.LastOrDefault(v => v.Kind == VehicleKind.Guard);
        if (guard is null || Guns.Mount(host.Train, guard.Id) is not { } mount)
            return;
        var post = PlayerMotor.SpawnOnRoof(host.Train, guard.Id, mount.Position.Z - mount.Facing.Z * 0.7, host.PlayerTuning);
        post.Yaw = Math.PI;
        host.SetPlayerState(id, post);
    }

    /// <summary>On a night the cars leave the fortress as they do in the game, part loaded (run.json departureLoad).</summary>
    static TrainOnLine NewTrain(RailLine line, TrainTuning t, HarnessOptions o, BoilerTuning? boiler) =>
        new(new TrainDynamics(Consist.Uniform(t, o.Cars, o.Run is { } r && o.Route is not null ? r.DepartureLoad : 1).Carrying(o.Cargo)), line, o.StartDistance, boiler);

    /// <summary>Counts payload bytes both ways for bandwidth reporting.</summary>
    sealed class CountingTransport(ITransport inner) : ITransport
    {
        public long BytesSent, BytesReceived;
        public PeerId LocalId => inner.LocalId;

        public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
        {
            BytesSent += payload.Length;
            inner.Send(to, payload, delivery);
        }

        public void Poll(List<TransportEvent> into)
        {
            int start = into.Count;
            inner.Poll(into);
            for (int i = start; i < into.Count; i++)
                BytesReceived += into[i].Payload?.Length ?? 0;
        }

        public void Disconnect(PeerId peer) => inner.Disconnect(peer);
        public void Dispose() => inner.Dispose();
    }
}
