using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>What the D.14 checks run on. The CLI loads the tuning (the Sim reads nothing outside content).</summary>
public sealed record HoldoutCheckOptions
{
    /// <summary>The night the networked scenario plays (a generated line: it has the Holdouts).</summary>
    public required Route.Route Route { get; init; }
    /// <summary>A night with the director's fuller roster for the vote (a Dead lines one); <see cref="Route"/> if null.</summary>
    public Route.Route? VoteRoute { get; init; }
    /// <summary>More lines to hold to "recoverability" (every Holdout and body reachable on foot).</summary>
    public IReadOnlyList<Route.Route> Lines { get; init; } = [];
    public required TrainTuning Train { get; init; }
    public required PlayerTuning Player { get; init; }
    public required BoilerTuning Boiler { get; init; }
    public required CombatTuning Combat { get; init; }
    public required EnemyTuning Enemies { get; init; }
    public required RunTuning Run { get; init; }
    public required HoldoutTuning Holdouts { get; init; }
    public FacilityTuning? Facilities { get; init; }
    public int Cars { get; init; } = 6;
    /// <summary>Crew at the start (a joiner comes in once the run has left the gate).</summary>
    public int Players { get; init; } = 4;
    public int Seed { get; init; } = 1;
    public LinkConditions Link { get; init; } = LinkConditions.Rough;
    /// <summary>How many Holdout sites the networked night works, in line order (every second one is left and come back to).</summary>
    public int Rescues { get; init; } = 3;
    /// <summary>Randomised nights for "no farming".</summary>
    public int Nights { get; init; } = 24;
    /// <summary>Randomised queue operations for "queue integrity".</summary>
    public int QueueOps { get; init; } = 4000;
}

/// <summary>One row of GDD App. D.14: what was checked, how, and what broke it (nothing, to pass).</summary>
public sealed record HoldoutCheck(string Name, string Method, IReadOnlyList<string> Faults)
{
    public bool Passed => Faults.Count == 0;
    /// <summary>What was measured on the way (how many spawns, frames, decisions...): a pass should have tested something.</summary>
    public IReadOnlyDictionary<string, double> Measured { get; init; } = new Dictionary<string, double>();
}

/// <summary>
/// GDD App. D.14, "what the harness verifies": every row as a headless scenario. The networked ones (spawns, the queue,
/// release and reassign, the channels) are one night over a lossy loopback: a crew from the yard, a joiner once the gates
/// are open, deaths before each Holdout site, rescues worked by intent at the door, one site left and come back to, the
/// dead talking and calling out throughout. The rest are nights on the host alone, run twice where the check is that
/// something made no difference.
/// </summary>
public static class HoldoutChecks
{
    static readonly byte[] Opus = [1, 2, 3, 4, 5, 6, 7, 8];

    public static IReadOnlyList<HoldoutCheck> Run(HoldoutCheckOptions o)
    {
        var night = Networked(o);
        return
        [
            NoOpenWorldSpawns(night),
            Recoverability(o, night),
            QueueIntegrity(o, night),
            ReleaseAndReassign(night),
            NoFarming(o),
            DeadSilence(o, night),
            VoteBounds(o),
            ChannelIsolation(night),
        ];
    }

    // ------------------------------------------------------------------ the networked night

    sealed class NetNight
    {
        public required HostSession Host;
        public required List<ClientSession> Clients;
        public List<string> Faults { get; } = new();
        public List<(string Site, bool Released, bool Reassigned, bool Freed)> Returns { get; } = new();
        public int Freed, CallOutsAllowed, DeadFramesToDead, LiveMicFramesAtDoor, DeadFramesToLiving, DeadVoicesHeardByTheRun;
        public List<Double3> BodiesLeft { get; } = new();
    }

    static NetNight Networked(HoldoutCheckOptions o)
    {
        var net = new LoopbackNetwork(o.Seed, o.Link);
        var route = o.Route;
        World Build(double front, bool authority)
        {
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(o.Train, o.Cars, 1)), route.Build(), front, o.Boiler), o.Combat);
            world.EnableRun(o.Run, route, route.GateOr(600), authority);
            world.EnableHoldouts(o.Holdouts);
            return world;
        }
        var host = new HostSession(net.CreateHost(), Build(300, authority: true), o.Train, o.Player);
        host.World.EnableBodies();
        var clients = new List<ClientSession>();
        var intents = new List<PlayerIntent>();
        var n = new NetNight { Host = host, Clients = clients };
        void Add()
        {
            clients.Add(new ClientSession(net.CreateClient(), Build(host.Train.Dynamics.Distance, authority: false), o.Train, o.Player));
            intents.Add(default);
        }
        var train = host.Train;
        var line = train.Line;
        var holdouts = host.World.Holdouts!;
        byte Id(int i) => clients[i].PlayerId ?? 0;
        PlayerState? StateOf(byte id)
        {
            foreach (var p in host.Players)
                if (p.Id == id)
                    return p.State;
            return null;
        }
        PlayerState State(int i) => StateOf(Id(i)) ?? default;
        var rng = new Random(o.Seed);
        ushort voiceSeq = 0;
        var heardAtDeath = new Dictionary<byte, uint?>();

        void Tick(double seconds, double? holdSpeed = null, Action? each = null)
        {
            for (int t = 0; t < Math.Max(1, seconds * SimConstants.TickRate); t++)
            {
                if (holdSpeed is { } v)
                    train.Dynamics.Velocity = v;
                each?.Invoke();
                // The dead talk (the dead channel, or their Holdout's Live Mic) every half second.
                if (host.Tick % 15 == 0)
                    for (int i = 0; i < clients.Count; i++)
                        if (clients[i].PlayerId is { } id && StateOf(id) is { Alive: false })
                            clients[i].SendVoice(++voiceSeq, false, Opus);
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                for (int i = 0; i < clients.Count; i++)
                    clients[i].Step(intents[i]);
                Listen();
            }
        }
        void Listen()
        {
            for (int i = 0; i < clients.Count; i++)
            {
                // Living as the client knows itself (the host's word on it can still be in flight).
                if (clients[i].PlayerId is null)
                    continue;
                var me = clients[i].Predicted;
                while (clients[i].VoiceFrames.TryDequeue(out var f))
                {
                    bool dead = f.Path.HasFlag(VoicePath.Dead);
                    if (dead && me.Alive)
                        n.DeadFramesToLiving++;
                    else if (dead)
                        n.DeadFramesToDead++;
                    if (f.Path.HasFlag(VoicePath.Holdout) && me.Alive)
                        n.LiveMicFramesAtDoor++;
                }
            }
            // The Gaunt's silence check listens to who's spoken (VoiceMemory): a waiting player never registers.
            foreach (var (id, was) in heardAtDeath)
                if (StateOf(id) is { Alive: false } && host.World.Voices.LastSpoke(id) != was)
                {
                    n.DeadVoicesHeardByTheRun++;
                    heardAtDeath[id] = host.World.Voices.LastSpoke(id);
                }
        }
        void Kill(int i, double at)
        {
            var sample = line.Sample(Math.Clamp(at, 0, line.Length));
            var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
            var s = PlayerMotor.SpawnOnGround(sample.Position + right * (rng.Next(2) == 0 ? -4 : 4), line, at, o.Player);
            host.SetPlayerState(Id(i), s with { Health = 0, Death = DeathCause.Mauled });
            heardAtDeath[Id(i)] = host.World.Voices.LastSpoke(Id(i));
        }
        void Board(int i, int car) => host.SetPlayerState(Id(i), PlayerMotor.SpawnOnRoof(train, car, 0, o.Player));
        void DriveTo(double front, double speed)
        {
            for (int guard = 0; guard < 3600 * SimConstants.TickRate && (speed > 0 ? train.Dynamics.Distance < front : train.Dynamics.Distance > front); guard++)
                Tick(SimConstants.TickSeconds, speed);
            Tick(1, 0);
        }

        // The crew, all in the session in the yard: aboard at the fortress. One dies in the yard (back at once).
        for (int i = 0; i < o.Players; i++)
            Add();
        Tick(0.5, 0);
        Kill(o.Players - 1, train.Dynamics.Distance - 20);
        Tick(0.5, 0);
        if (!State(o.Players - 1).Alive)
            n.Faults.Add("a death in the yard wasn't back aboard at the fortress");
        // Out through the gates; then a joiner, who's lobbied.
        DriveTo(route.GateOr(600) + 150, 20);
        Add();
        Tick(1, 0);
        int joiner = clients.Count - 1;
        if (State(joiner).Alive || !State(joiner).Has(PlayerFlags.Lobbied))
            n.Faults.Add("a joiner once the gates were open wasn't lobbied");

        // The sites to work, in line order, on the main line.
        var sites = holdouts.All.Where(h => h.Site.Zone.Edge == "main" && !h.Site.Second).OrderBy(h => h.Site.Zone.S0).Take(o.Rescues).ToList();
        int victim = 1;
        for (int k = 0; k < sites.Count; k++)
        {
            var site = sites[k];
            var zone = site.Site.Zone;
            // Someone dies on the way (outside the zone), so there's someone to free: a living one who isn't the rescuer.
            for (int tries = 0; tries < clients.Count && !State(victim).Alive; tries++)
                victim = victim % (clients.Count - 1) + 1;
            if (State(victim).Alive)
                Kill(victim, Math.Max(train.Dynamics.Distance - 30, zone.S0 - 400));
            DriveTo(zone.S0 + 150, 30);
            Tick(1, 0);
            Holdout? Taken() => holdouts.All.FirstOrDefault(h => h.Site.Site == site.Site.Site && h.Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching);
            if (Taken() is not { } h)
            {
                n.Faults.Add($"stopped at {site.Site.Name} with the queue [{string.Join(",", holdouts.Queue.Order)}], nobody was assigned");
                continue;
            }
            if (k % 2 == 1)
            {
                // D.14 "release and reassign": away, past release distance, then back.
                var events = holdouts.History.Count;
                DriveTo(zone.S1 + o.Holdouts.ReleaseM + 100, 30);
                bool released = holdouts.History.Skip(events).Any(e => e.Event.Holdout == h.Index && e.Event.Outcome == HoldoutOutcome.Released);
                events = holdouts.History.Count;
                DriveTo(zone.S0 + 150, -20);
                Tick(1, 0);
                bool again = Taken() is not null;
                h = Taken() ?? h;
                n.Returns.Add((site.Site.Name, released, again, false));
            }
            // The rescue, worked by intent: the rescuer at the door, a crowbar in hand, holding Use.
            var outward = (h.Door - h.Centre) with { Y = 0 };
            host.SetPlayerState(Id(0), PlayerMotor.SpawnOnGround(h.Door + outward.Normalized * 0.6, line, train.Dynamics.Distance, o.Player));
            host.World.Bodies.SpawnCrate(train, 1, Double3.Zero, BodyKind.Crowbar).Carrier = Id(0);
            int freedId = h.Assigned;
            // The waiting call out (the rescuer's in earshot now); the assigned opens the Live Mic, heard at the door.
            int asker = clients.FindIndex(c => c.PlayerId == freedId);
            if (asker >= 0)
                clients[asker].Send(new Request(RequestKind.LiveMic, 1));
            foreach (var c in clients.Where(c => c.PlayerId is { } id && StateOf(id) is { Alive: false }))
                c.Send(new Request(RequestKind.CallOut, h.Index));
            Tick(1.5, 0);
            intents[0] = new PlayerIntent { Buttons = PlayerButtons.Use };
            double needed = o.Holdouts.Step(holdouts.MethodFor(h, BodyKind.Crowbar) ?? BreachMethod.Smash).Seconds;
            for (int t = 0; t < (needed + 6) * SimConstants.TickRate && h.Phase != HoldoutPhase.Freed; t++)
                Tick(SimConstants.TickSeconds, 0);
            intents[0] = default;
            Tick(0.3, 0);
            if (h.Phase != HoldoutPhase.Freed)
                n.Faults.Add($"{h.Id} at {site.Site.Name} wasn't freed by a {needed:0} s breach");
            else
                n.Freed++;
            if (k % 2 == 1 && n.Returns.Count > 0)
                n.Returns[^1] = n.Returns[^1] with { Freed = h.Phase == HoldoutPhase.Freed };
            // Everyone back aboard; the tool goes back in the van (its carrier's gone).
            foreach (var b in host.World.Bodies.All.Where(b => b.Kind == BodyKind.Crowbar && b.Carrier == Id(0)).ToList())
                host.World.Bodies.Remove(b);
            Board(0, 1);
            int back = clients.FindIndex(c => c.PlayerId == freedId);
            if (back >= 0 && State(back).Alive)
                Board(back, 2);
            Tick(0.5, 0);
        }
        n.CallOutsAllowed = host.Requests.Count(q => q.Request.Kind == RequestKind.CallOut && q.Allowed);
        // The bodies left on the ground (not aboard, not carried).
        foreach (var b in host.World.Bodies.All.Where(b => b.Kind == BodyKind.Ragdoll && b.Parent == PlayerState.World && b.Carrier < 0))
            n.BodiesLeft.Add(b.Centre);
        return n;
    }

    static HoldoutCheck NoOpenWorldSpawns(NetNight n)
    {
        var faults = new List<string>(n.Faults);
        var spawns = n.Host.Spawns;
        foreach (var s in spawns.Where(s => s.MidRun && s.Holdout is null && !s.SessionStart))
            faults.Add($"player {s.Player} came to life mid-run at ({s.At.X:0},{s.At.Z:0}), in no Holdout (tick {s.Tick}, {s.Phase})");
        int freed = spawns.Count(s => s.MidRun && s.Holdout is not null);
        if (freed == 0)
            faults.Add("nobody was freed: nothing mid-run was tested");
        return new HoldoutCheck("No open-world spawns", "every player come to life on the host, watched by state over a networked night, is in the yard or inside a Holdout", faults)
        {
            Measured = new Dictionary<string, double>
            {
                ["spawns"] = spawns.Count,
                ["midRun"] = spawns.Count(s => s.MidRun),
                ["inHoldouts"] = freed,
                ["inTheYard"] = spawns.Count(s => !s.MidRun),
            },
        };
    }

    // ------------------------------------------------------------------ recoverability

    static HoldoutCheck Recoverability(HoldoutCheckOptions o, NetNight n)
    {
        var faults = new List<string>();
        int holdouts = 0, bodies = 0, water = 0;
        foreach (var route in new[] { o.Route }.Concat(o.Lines).DistinctBy(r => r.Name))
        {
            var line = route.Build();
            if (route.Plan is not { } plan || (line.Conditions as PlanConditions)?.Terrain is not { } terrain)
                continue;
            // Every Holdout, from the stopped consist to its door: the validator's own check (walk included).
            faults.AddRange(HoldoutSites.Check(plan, line, terrain, o.Holdouts, o.Facilities, o.Train).Select(f => $"{route.Name}: {f}"));
            holdouts += plan.Holdouts.Count;
            // Every body: one fallen off the train either side comes to rest off the ballast where the ground stops
            // falling away (a cutting's ditch, not up its face; an embankment's foot, within 8 m), and is walked to from
            // the track's edge.
            for (double s = 250; s < line.Length - 250; s += 250)
            {
                if (route.InTunnel(s) || route.BridgeAt(s) is not null)
                    continue;
                var t = line.Sample(s);
                var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                foreach (int side in new[] { -1, 1 })
                {
                    double H(double lateral)
                    {
                        var p = t.Position + right * (side * lateral);
                        return terrain.Height(p.X, p.Z);
                    }
                    double rest = 3.5;
                    while (rest < 8 && H(rest + 0.5) <= H(rest) + 0.02)
                        rest += 0.5;
                    var at = t.Position + right * (side * rest);
                    if (terrain.WaterAt(at.X, at.Z) is not null)
                    {
                        water++; // in the water it's lost (D.9): not a body anyone can bring home
                        continue;
                    }
                    var edge = t.Position + right * (side * 2.5);
                    bodies++;
                    if (HoldoutSites.Walk(terrain, edge with { Y = terrain.Height(edge.X, edge.Z) }, at with { Y = terrain.Height(at.X, at.Z) }, 1.5) is { } why)
                        faults.Add($"{route.Name}: a body at {s:0} m, {(side < 0 ? "left" : "right")}, can't be walked to: {why}");
                }
            }
        }
        // And the networked night's own bodies, where they lie.
        if ((n.Host.Train.Line.Conditions as PlanConditions)?.Terrain is { } own)
            foreach (var b in n.BodiesLeft)
            {
                double hint = n.Host.Train.Dynamics.Distance;
                var (_, d) = n.Host.Train.Line.Nearest(b, ref hint);
                var track = n.Host.Train.Line.Sample(Math.Clamp(d, 0, n.Host.Train.Line.Length)).Position;
                var edge = track + ((b - track) with { Y = 0 }).Normalized * 2.5;
                bodies++;
                if ((b - track).Length > 3 && HoldoutSites.Walk(own, edge with { Y = own.Height(edge.X, edge.Z) }, b with { Y = own.Height(b.X, b.Z) }, 1.5) is { } why)
                    faults.Add($"a body left on the night at ({b.X:0},{b.Z:0}) can't be walked to: {why}");
            }
        return new HoldoutCheck("Recoverability", "every generated Holdout walked to from the stopped consist (the validator's check); a body either side of the line every 250 m, and the night's own, walked to from the track's edge", faults)
        {
            Measured = new Dictionary<string, double> { ["lines"] = 1 + o.Lines.Count, ["holdouts"] = holdouts, ["bodies"] = bodies, ["inWater"] = water },
        };
    }

    // ------------------------------------------------------------------ queue integrity

    static HoldoutCheck QueueIntegrity(HoldoutCheckOptions o, NetNight n)
    {
        var faults = new List<string>();
        var queue = n.Host.World.Holdouts!.Queue;
        if (queue.Violations > 0)
            faults.Add($"the night's queue broke order {queue.Violations} times");
        // Random operations on a queue of its own: every change audited, deferral only ever down, skipped entries in place.
        var rng = new Random(o.Seed * 7919);
        var q = new RespawnQueue();
        int nextId = 1, deferredUp = 0, skippedMoved = 0, ops = 0, assigns = 0;
        var sites = new[] { "a", "b", "c" };
        for (int i = 0; i < o.QueueOps; i++)
        {
            ops++;
            var before = q.Order;
            switch (rng.Next(6))
            {
                case 0:
                    q.Died(nextId++, i, rng.Next(3) == 0 ? [sites[rng.Next(3)]] : []);
                    break;
                case 1:
                    q.Lobbied(nextId++, i);
                    break;
                case 2 when q.Count > 0:
                    {
                        var who = q.Entries[rng.Next(q.Count)].Player;
                        int at = q.PositionOf(who), to = rng.Next(q.Count);
                        if (q.Defer(who, to) && to <= at)
                            deferredUp++;
                        break;
                    }
                case 3 when q.Count > 0:
                    {
                        var site = sites[rng.Next(3)];
                        if (q.AssignedTo(site) is not null)
                            break;
                        int firstEligible = q.Entries.ToList().FindIndex(e => e.Holdout is null && !e.DiedIn.Contains(site));
                        var got = q.Assign(site, e => !e.DiedIn.Contains(site));
                        assigns++;
                        if (!q.Order.SequenceEqual(before))
                            skippedMoved++;
                        if (got is not null && q.PositionOf(got.Player) != firstEligible)
                            faults.Add($"a Holdout took the {q.PositionOf(got.Player)}th entry, not the first eligible ({firstEligible})");
                        break;
                    }
                case 4:
                    {
                        var site = sites[rng.Next(3)];
                        if (rng.Next(2) == 0)
                            q.Freed(site);
                        else
                            q.Release(site);
                        break;
                    }
                case 5 when q.Count > 0:
                    q.Remove(q.Entries[rng.Next(q.Count)].Player);
                    break;
            }
        }
        if (q.Violations > 0)
            faults.Add($"random operations broke the order {q.Violations} times");
        if (deferredUp > 0)
            faults.Add($"a deferral moved someone up {deferredUp} times");
        if (skippedMoved > 0)
            faults.Add($"an assignment moved entries {skippedMoved} times (skipped entries must keep their place)");
        return new HoldoutCheck("Queue integrity", "every change to the night's queue and to a randomly driven one audited: nobody moves up but by those ahead leaving, deferral only down, skipped entries keep their place", faults)
        {
            Measured = new Dictionary<string, double> { ["nightChanges"] = queue.History.Count, ["randomOps"] = ops, ["assigns"] = assigns },
        };
    }

    static HoldoutCheck ReleaseAndReassign(NetNight n)
    {
        var faults = new List<string>();
        if (n.Returns.Count == 0)
            faults.Add("no site was left and come back to");
        foreach (var r in n.Returns)
        {
            if (!r.Released)
                faults.Add($"{r.Site}: leaving past release distance didn't release its Holdout");
            if (!r.Reassigned)
                faults.Add($"{r.Site}: coming back didn't assign it again");
            if (!r.Freed)
                faults.Add($"{r.Site}: its occupant wasn't freed after the crew came back");
        }
        return new HoldoutCheck("Release and reassign", "on the networked night, every second site is left past release distance and come back to, then its occupant freed", faults)
        {
            Measured = new Dictionary<string, double> { ["returns"] = n.Returns.Count, ["freed"] = n.Freed },
        };
    }

    // ------------------------------------------------------------------ no farming

    /// <summary>A night on the host alone, stepped in HostSession's order (the checks that compare two nights).</summary>
    sealed class HostNight
    {
        public readonly World World;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly Dictionary<int, PlayerIntent> Intent = new();
        readonly HoldoutCheckOptions _o;

        public HostNight(HoldoutCheckOptions o, Route.Route route, double front, bool enemies)
        {
            _o = o;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(o.Train, o.Cars, 1)), route.Build(), front, o.Boiler);
            World = new World(train, o.Combat);
            if (enemies)
                World.EnableEnemies(o.Enemies, route, 11, 3, authority: true);
            World.EnableRun(o.Run, route, route.GateOr(600), authority: true);
            World.EnableBodies();
            World.EnableHoldouts(o.Holdouts);
            World.Run!.Resume(10, -1, train.Boiler.Tender, 0);
        }

        public TrainOnLine Train => World.Train;

        public void Step(double seconds = SimConstants.TickSeconds, double? holdSpeed = null)
        {
            for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * SimConstants.TickRate)); i++)
            {
                if (holdSpeed is { } v)
                    Train.Dynamics.Velocity = v;
                World.BeginTick();
                World.LivingCrew = Crew.Values.Count(c => c.Alive);
                foreach (int id in Crew.Keys.Order().ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, Intent.GetValueOrDefault(id), id);
                    Crew[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys.ToList());
                foreach (int id in Crew.Keys.Order().ToList())
                {
                    var s = Crew[id];
                    PlayerMotor.Step(ref s, Intent.GetValueOrDefault(id), Train, _o.Player, _o.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[id] = s;
                }
                World.StepBodies([.. Crew.Select(c => (c.Key, c.Value))]);
                World.StepHoldouts(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys.ToList(), Crew.Count, _o.Player);
                World.StepRun([.. Crew.Values]);
            }
        }

        public void DeadAtTheFortress(int id)
        {
            var s = PlayerMotor.SpawnOnGround(Train.Line.Sample(50).Position + new Double3(4, 0, 0), Train.Line, 50, _o.Player);
            Crew[id] = s with { Health = 0, Death = DeathCause.Mauled };
            World.StepBodies([.. Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    static HoldoutCheck NoFarming(HoldoutCheckOptions o)
    {
        var faults = new List<string>();
        var rng = new Random(o.Seed * 104729);
        double front = o.Route.Length - 150;
        double Settle(HostNight n)
        {
            n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, o.Player);
            n.Step(0.2, holdSpeed: 0);
            return n.World.Run!.Report!.Net;
        }
        int deathsTotal = 0, delivered = 0;
        for (int night = 0; night < o.Nights; night++)
        {
            double clean = Settle(new HostNight(o, o.Route, front, enemies: false));
            var n = new HostNight(o, o.Route, front, enemies: false);
            int deaths = rng.Next(1, 8);
            for (int i = 0; i < deaths; i++)
            {
                int id = 10 + i, car = rng.Next(1, n.Train.Frames.Count);
                var st = PlayerMotor.SpawnOnRoof(n.Train, car, 0, o.Player);
                if (n.Train.Frames[car].Shape.Interior is { } room)
                    st = st with { Position = new Double3(0, room.Min.Y + 0.1, room.Centre.Z), Surface = Surface.Deck };
                if (rng.Next(3) == 0)
                    n.World.Bodies.SpawnCrate(n.Train, car, Double3.Zero, BodyKind.Wrench).Carrier = id;
                if (rng.Next(4) == 0)
                    n.World.DroppedOut(id, st);
                else
                {
                    n.Crew[id] = st with { Health = 0, Death = DeathCause.Mauled };
                    n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
                    n.Crew.Remove(id);
                }
                // Some are left out on the line.
                if (rng.Next(3) == 0 && n.World.Bodies.All.LastOrDefault(b => b.Kind == BodyKind.Ragdoll) is { } left)
                    n.World.Bodies.Remove(left);
            }
            double net = Settle(n);
            var r = n.World.Run!.Report!;
            deathsTotal += deaths;
            delivered += r.BodiesDelivered;
            if (net > clean)
                faults.Add($"night {night}: {net:0} with {deaths} deaths against {clean:0} with none");
            if (r.BodyRefunds > r.CrewLossFees)
                faults.Add($"night {night}: refunds {r.BodyRefunds:0} over fees {r.CrewLossFees:0}");
        }
        if (o.Holdouts.RefundShare >= 1)
            faults.Add($"refundShare {o.Holdouts.RefundShare} isn't under 1.0");
        return new HoldoutCheck("No farming", "randomised deaths, drop-outs and recoveries on nights that arrive, each against the same night with nobody dying", faults)
        {
            Measured = new Dictionary<string, double> { ["nights"] = o.Nights, ["deaths"] = deathsTotal, ["bodiesDelivered"] = delivered },
        };
    }

    // ------------------------------------------------------------------ dead silence

    static string Fingerprint(World w, ChoirTuning t)
    {
        var d = w.Director!;
        return string.Join("|",
            $"choir {w.Choir.Loudness:R} {w.Choir.Build:R} {w.Choir.Present} {w.Choir.Spent}",
            $"loud {w.Loudness(t):R}",
            $"director {d.Spent:R} {d.HeldBecause} " + string.Join(",", d.Log.Select(l => $"{l.Tick}:{l.Kind}")),
            "enemies " + string.Join(",", w.ActiveEnemies.Select(e => $"{e.Id}:{e.Kind}:{e.Phase}")),
            $"voices {string.Join(",", Enumerable.Range(0, 5).Select(i => w.Voices.LastSpoke(i)?.ToString() ?? "-"))}");
    }

    static HoldoutCheck DeadSilence(HoldoutCheckOptions o, NetNight net)
    {
        var faults = new List<string>();
        var site = o.Route.Plan!.Holdouts.First(x => x.SiteKind == HoldoutSiteKind.Facility && !x.Second);
        HostNight Night(out Holdout h)
        {
            var n = new HostNight(o, o.Route, site.Zone.S0 + 150, enemies: true);
            n.DeadAtTheFortress(2);
            n.DeadAtTheFortress(3);
            n.Step(0.2);
            h = n.World.Holdouts!.Of(site.Id)!;
            // A living crewmate at its door: a Call Out has someone in earshot.
            var outward = (h.Door - h.Centre) with { Y = 0 };
            n.Crew[1] = PlayerMotor.SpawnOnGround(h.Door + outward.Normalized * 0.6, n.Train.Line, n.Train.Dynamics.Distance, o.Player);
            return n;
        }
        var quiet = Night(out _);
        var loud = Night(out var hl);
        int allowed = 0, seconds = 90;
        for (int tick = 0; tick < seconds * SimConstants.TickRate; tick++)
        {
            var everyone = loud.Crew.Select(c => (c.Key, c.Value)).ToList();
            foreach (int dead in new[] { 2, 3 })
            {
                if (loud.World.Request(dead, new Request(RequestKind.CallOut, hl.Index), everyone))
                    allowed++;
                loud.World.Request(dead, new Request(RequestKind.LiveMic, tick / 30 % 2), everyone);
            }
            quiet.Step();
            loud.Step();
            if (tick % (10 * SimConstants.TickRate) == 0 && Fingerprint(quiet.World, o.Combat.Choir) != Fingerprint(loud.World, o.Combat.Choir))
            {
                faults.Add($"at {tick / SimConstants.TickRate} s the nights differ: {Fingerprint(loud.World, o.Combat.Choir)} against {Fingerprint(quiet.World, o.Combat.Choir)}");
                break;
            }
        }
        if (faults.Count == 0 && Fingerprint(quiet.World, o.Combat.Choir) != Fingerprint(loud.World, o.Combat.Choir))
            faults.Add("after the call outs the nights differ");
        if (allowed == 0)
            faults.Add("no Call Out was allowed: nothing was tested");
        // The networked night's dead talked all night: the silence check (who's spoken) never heard them.
        if (net.DeadVoicesHeardByTheRun > 0)
            faults.Add($"the waiting were heard by the run {net.DeadVoicesHeardByTheRun} times");
        return new HoldoutCheck("Dead silence", "two seeded nights with the director, one with its dead calling out and working the Live Mic every tick, compared (meter, Choir, director, enemies, who's spoken); and the networked night's dead talking throughout", faults)
        {
            Measured = new Dictionary<string, double> { ["callOuts"] = allowed, ["networkCallOuts"] = net.CallOutsAllowed, ["seconds"] = seconds },
        };
    }

    // ------------------------------------------------------------------ vote bounds

    static HoldoutCheck VoteBounds(HoldoutCheckOptions o)
    {
        var faults = new List<string>();
        var route = o.VoteRoute ?? o.Route;
        var n = new HostNight(o, route, 6000, enemies: true);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, o.Player);
        n.Crew[5] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, o.Player);
        n.Step(0.1);
        var w = n.World;
        var d = w.Director!;
        // The Soot Children's 50/50 (App. B.6) is drawn from the director's dice when they come: the votes mustn't spend any,
        // nor touch the chance or the host's first-call rule.
        var dice = d.Rng;
        bool nextReal = w.NextChildReal;
        // A vote for every creature on offer, and more for one than the cap allows.
        int id = 20;
        foreach (var k in w.VoteOptions.ToList())
        {
            n.DeadAtTheFortress(id);
            if (!w.Vote(id, n.Crew[id], k))
                faults.Add($"a dead player's vote for {k}, on offer, was refused");
            if (w.Vote(id, n.Crew[id], k))
                faults.Add($"player {id} voted twice");
            id++;
        }
        var favourite = w.VoteOptions.FirstOrDefault();
        for (int i = 0; i < 6; i++)
        {
            n.DeadAtTheFortress(id);
            w.Vote(id, n.Crew[id], favourite);
            id++;
        }
        // Not the living, not the lobbied, not something that isn't on offer.
        if (w.Vote(1, n.Crew[1], favourite))
            faults.Add("the living voted");
        if (w.Vote(99, HostSession.Lobbied(n.Train), favourite))
            faults.Add("a lobbied player voted");
        n.DeadAtTheFortress(id);
        if (w.Vote(id, n.Crew[id], EnemyKind.Stoker))
            faults.Add("a vote for the Stoker (condition-triggered) was allowed");
        foreach (var (k, m) in d.VoteMultipliers)
            if (m > o.Holdouts.Vote.Cap + 1e-12)
                faults.Add($"{k} weighs x{m} from votes, over x{o.Holdouts.Vote.Cap}");
        var diceNow = d.Rng;
        if (diceNow.NextUInt() != dice.NextUInt() || w.NextChildReal != nextReal)
            faults.Add("the votes moved the Soot Children's real-child roll (the director's dice, or the first-call rule)");
        // Every decision over a stretch of night: the same options, each want tag's share held, no gain past the cap.
        int decisions = 0, seen = d.Log.Count;
        double budget = d.Budget;
        // Until the director's made enough decisions to say something (the v1.1 roster's comes every minute or so), or the
        // line runs out.
        for (int t = 0; t < 1500 * SimConstants.TickRate && decisions < 10 && n.Train.Dynamics.Distance < route.Length - 3000; t++)
        {
            n.Step(holdSpeed: 14);
            if (d.Log.Count == seen || d.LastOptions.Count == 0)
                continue;
            seen = d.Log.Count;
            decisions++;
            var before = d.LastOptionsBeforeVotes;
            var after = d.LastOptions;
            if (!before.Select(x => x.Kind).SequenceEqual(after.Select(x => x.Kind)))
                faults.Add($"decision {decisions}: the votes changed what was an option");
            foreach (var tag in before.Select(x => d.WantTag(x.Kind)).Distinct())
            {
                double b = before.Where(x => d.WantTag(x.Kind) == tag).Sum(x => x.Weight), a = after.Where(x => d.WantTag(x.Kind) == tag).Sum(x => x.Weight);
                if (Math.Abs(a - b) > 1e-9 * Math.Max(1, b))
                    faults.Add($"decision {decisions}: want tag {tag ?? "none"} weighs {a} against {b} without votes");
            }
            for (int i = 0; i < before.Count; i++)
                if (before[i].Weight > 0 && after[i].Weight / before[i].Weight > o.Holdouts.Vote.Cap + 1e-9)
                    faults.Add($"decision {decisions}: {before[i].Kind} gained x{after[i].Weight / before[i].Weight}");
        }
        if (d.Budget != budget)
            faults.Add("the votes changed the director's budget");
        // A once-a-run creature, sent, is no option again: voted for or not.
        d.Charge(w, EnemyKind.Gaunt, w.ActiveEnemies);
        n.DeadAtTheFortress(++id);
        if (w.VoteOptions.Contains(EnemyKind.Gaunt) || w.Vote(id, n.Crew[id], EnemyKind.Gaunt))
            faults.Add("the Gaunt, once a run and already sent, could be voted for");
        if (decisions < 5)
            faults.Add($"only {decisions} director decisions: too few to test the shares");
        return new HoldoutCheck("Vote bounds", "every creature on offer voted for, one past the cap; ten director decisions compared with and without the votes; the gates, the once-a-run limit and the Soot Children's draw held to", faults)
        {
            Measured = new Dictionary<string, double> { ["options"] = w.VoteOptions.Count, ["votes"] = w.VoteLog.Count, ["decisions"] = decisions },
        };
    }

    // ------------------------------------------------------------------ channel isolation

    static HoldoutCheck ChannelIsolation(NetNight n)
    {
        var faults = new List<string>();
        if (n.DeadFramesToLiving > 0)
            faults.Add($"living clients received {n.DeadFramesToLiving} dead-channel frames");
        if (n.Host.DeadFramesToTheLiving > 0)
            faults.Add($"the host forwarded {n.Host.DeadFramesToTheLiving} dead-channel frames to the living");
        if (n.DeadFramesToDead == 0)
            faults.Add("no dead-channel frame reached the dead: nothing was tested");
        return new HoldoutCheck("Channel isolation", "the networked night's dead talk every half second; every frame each living client receives is looked at", faults)
        {
            Measured = new Dictionary<string, double>
            {
                ["deadFramesToDead"] = n.DeadFramesToDead,
                ["liveMicFramesAtTheDoor"] = n.LiveMicFramesAtDoor,
                ["deadFramesForwarded"] = n.Host.DeadFramesForwarded,
            },
        };
    }
}
