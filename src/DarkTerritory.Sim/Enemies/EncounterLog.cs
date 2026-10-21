using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The way an encounter came (the director's brief §8, note 880): the director's pressure spend, its free spawn on the night's
/// curve or the fill (note 849), a stop's own (note 860), a special of the director's (the Stoker, a hound run, a truss drop,
/// a hunt for someone left behind, the insisted), or the world's own (the house creatures, the Pickers, the Mourners, the
/// lineside Moose, Dave), which the director doesn't pay for.
/// </summary>
public enum SpawnChannel : byte { Pressure, Free, Fill, Stop, Special, World }

/// <summary>What became of an encounter: still about at the night's end, killed (every member down), or gone some other way.</summary>
public enum EncounterEnd : byte { About, Killed, Gone }

/// <summary>
/// One spawn the director tried (note 880). <paramref name="Charged"/>: the director accounted it (its log, its pressure) before
/// placement; a charged attempt that placed nothing is the brief's issue 3 (#603 makes it impossible; until then it's counted).
/// <paramref name="Failure"/>: why nothing came, null when it did; #603's <c>SpawnFailure</c> reasons as they land.
/// <paramref name="Encounter"/>: the encounter it made, or −1.
/// </summary>
public readonly record struct SpawnAttemptRecord(uint Tick, double Seconds, double Progress, SpawnChannel Channel, EnemyKind Kind,
    bool Placed, string? Failure, bool Charged, int Members, int Crew, int Cars, double Debt, int Encounter);

/// <summary>
/// One encounter (note 880): what one spawn put in, its members counted once however many bodies a pack has. The measured
/// facts (when it came, when any member first went on the alert, first confronted someone, its grabs, how it ended) are kept
/// apart from the inferred (<see cref="NearAt"/>, <see cref="NearestMetres"/>: a crewmate came within the telemetry's
/// <c>nearMetres</c> of a member; that is "near", never "seen").
/// </summary>
public sealed class Encounter
{
    public int Index { get; init; }
    public SpawnChannel Channel { get; init; }
    public EnemyKind Kind { get; init; }
    public IReadOnlyList<int> Members { get; init; } = [];
    public PressureZone Zone { get; init; }
    /// <summary>Where it came, said plainly: "engine", "car", "roof", "trackside", "afield" (a yard's or a village's ground included).</summary>
    public string Where { get; init; } = "";
    /// <summary>The train's vehicle it came on or beside (0 the engine), or −1 off the line's reach.</summary>
    public int Vehicle { get; init; } = -1;
    /// <summary>"front", "middle" or "rear": the third of the train it came at, for the brief §5's spread over a long train.</summary>
    public string Part { get; init; } = "";
    /// <summary>Every living crewmate was off the train when it came (a yard, a village; note 795).</summary>
    public bool Afoot { get; init; }
    public uint Tick { get; init; }
    public double Seconds { get; init; }
    public double Progress { get; init; }
    public int Crew { get; init; }
    public int Cars { get; init; }
    /// <summary>Other encounters active (some member engaged, <see cref="Director.Engaged"/>) when it came.</summary>
    public int Overlap { get; init; }

    /// <summary>Measured: the first second any member was engaged (threatening or engaging, #604's <see cref="ThreatState"/>), −1 for never.</summary>
    public double EngagedAt { get; internal set; } = -1;
    /// <summary>Measured: the first second any member confronted someone (telegraph, commit, grab, punish), −1 for never.</summary>
    public double ConfrontedAt { get; internal set; } = -1;
    /// <summary>Measured: the last second any member was engaged, −1 for never.</summary>
    public double LastEngagedAt { get; internal set; } = -1;
    /// <summary>Measured: seconds some member was engaged; seconds none was (dormant, ambient: alive, not a threat).</summary>
    public double EngagedSeconds { get; internal set; }
    public double DormantSeconds { get; internal set; }
    /// <summary>Measured: grabs and punishes its members made.</summary>
    public int Grabs { get; internal set; }
    public int Punishes { get; internal set; }
    /// <summary>Measured: when the last member went, −1 while any is about.</summary>
    public double EndedAt { get; internal set; } = -1;
    public EncounterEnd End { get; internal set; }

    /// <summary>Inferred (a perception proxy): the first second a living crewmate was within <c>nearMetres</c> of a member, −1 for never.</summary>
    public double NearAt { get; internal set; } = -1;
    /// <summary>Inferred: the closest any living crewmate came to a member, metres (∞ for nobody alive to measure).</summary>
    public double NearestMetres { get; internal set; } = double.PositiveInfinity;

    /// <summary>
    /// The brief §4's level it reached (<see cref="EncounterSeverity"/>; note 893): 0 never noticed, 1 ambient presence, 2 active
    /// harassment, 3 compound crisis.
    /// </summary>
    public int Severity { get; internal set; }
    /// <summary>The first second it reached level 2 (harassing the crew or the train), and level 3 (compound); −1 for never.</summary>
    public double HarassedAt { get; internal set; } = -1;
    public double CompoundAt { get; internal set; } = -1;
    /// <summary>Seconds it spent at level 1, 2 and 3 (index 1–3; index 0 the seconds about but unnoticed).</summary>
    public int[] SecondsAt { get; } = new int[4];

    /// <summary>Meaningful (note 893): it harassed the crew or the train, level 2 or more; level 1 is presence, not an encounter met.</summary>
    public bool Meaningful => Severity >= 2;
}

/// <summary>A stretch of the run with no meaningful encounter going (note 880): from, to, and what held the director meanwhile.</summary>
public readonly record struct Drought(double From, double To, double Progress, string Held)
{
    public double Seconds => To - From;
}

/// <summary>
/// The encounter telemetry (the director's brief §8; queue #600, note 880). Host-side, it only watches: the world tells it of
/// each spawn attempt and of what each spawn call put in, and once a second it sweeps the enemies and the crew. Nothing in
/// the Sim reads it back, so it can't move a night (no draws, no wire state); <c>dt harness</c>, <c>dt playthrough</c> and
/// <c>dt director sweep</c> (#601) report it as their <c>encounters</c> block.
/// </summary>
public sealed class EncounterLog(TelemetryTuning t, ThreatTuning threats)
{
    readonly TelemetryTuning _t = t;
    readonly ThreatTuning _threats = threats;
    double _quietFrom = -1;
    readonly Dictionary<int, Encounter> _byMember = [];
    /// <summary>The open encounters and their members' bodies, kept past their removal from the world so a kill can be told.</summary>
    readonly List<(Encounter Encounter, List<Enemy> Bodies)> _open = [];
    int _seen;
    double _progress, _lastMeaningful = -1, _droughtFrom = -1;
    readonly Dictionary<string, int> _droughtHeld = [];

    public List<SpawnAttemptRecord> Attempts { get; } = [];
    public List<Encounter> Encounters { get; } = [];
    /// <summary>Stretches of at least droughtSeconds with nothing harassing the crew (level 2+), rests and presence included.</summary>
    public List<Drought> Droughts { get; } = [];
    /// <summary>
    /// True droughts (the brief §6; note 893): unbroken runs of at least droughtSeconds of <see cref="QuietKind.Drought"/>, nothing
    /// about and the director not resting. What the director is to answer; the rest is pacing.
    /// </summary>
    public List<Drought> TrueDroughts { get; } = [];
    /// <summary>Seconds out on the run by what kind of second each was (<see cref="QuietKind"/>).</summary>
    public int[] QuietSeconds { get; } = new int[4];
    /// <summary>What the last second out was, and how long the true drought running now has gone (0 when none is).</summary>
    public QuietKind Now { get; private set; } = QuietKind.Recovery;
    public double DroughtNow { get; private set; }
    /// <summary>Seconds out on the run the director held, by its reason (<see cref="Director.HeldBecause"/>; "sent" when it didn't).</summary>
    public SortedDictionary<string, int> Suppressed { get; } = new(StringComparer.Ordinal);
    /// <summary>Seconds out on the run with this many encounters active at once (index 0, 1, 2, … the last bucket and over).</summary>
    public int[] Concurrent { get; } = new int[5];
    /// <summary>The most encounters active at once, and the most bodies alive (not hazards) at once.</summary>
    public int PeakActive { get; private set; }
    public int PeakBodies { get; private set; }
    /// <summary>The run's progress, 0–1: the larger of the night's time fraction and the route's distance fraction, never falling.</summary>
    public double Progress => _progress;
    /// <summary>The director's encounter debt as last told (#607/#608 set it each second; 0 until then).</summary>
    public double Debt { get; set; }
    /// <summary>The most debt told at a sweep.</summary>
    public double DebtPeak { get; private set; }
    /// <summary>The run's seconds out (the yard and the arrival not counted), the drought clock's base.</summary>
    public double OutSeconds { get; private set; }
    public TelemetryTuning Tuning => _t;

    /// <summary>Which channel the director's last decision went by, from the log entries it added since <paramref name="logged"/>.</summary>
    public static SpawnChannel ChannelOf(IReadOnlyList<DirectorSpawn> log, int logged) =>
        log.Count <= logged ? SpawnChannel.Pressure
        : log[^1] switch
        {
            { Stop: true } => SpawnChannel.Stop,
            { Free: true, Paced: true } => SpawnChannel.Fill,
            { Free: true } => SpawnChannel.Free,
            _ => SpawnChannel.Pressure,
        };

    /// <summary>
    /// A spawn the director tried: <paramref name="kind"/> by <paramref name="channel"/>, and whether it placed. Every enemy
    /// put in since id <paramref name="firstId"/> is its encounter's members.
    /// </summary>
    public void Attempt(World world, SpawnChannel channel, EnemyKind kind, bool placed, string? failure, bool charged, int firstId)
    {
        // Everything the call put in is its doing (a kind's companions included); the attempt's own kind is its encounter.
        var came = placed ? Came(world, channel, firstId) : [];
        var made = came.FirstOrDefault(e => e.Kind == kind) ?? came.FirstOrDefault();
        Attempts.Add(new SpawnAttemptRecord(world.Tick, Seconds(world), Advance(world), channel, kind, placed,
            placed ? null : failure ?? "no placement", charged, made?.Members.Count ?? 0, Living(world), Cars(world), Debt, made?.Index ?? -1));
    }

    /// <summary>
    /// What a spawn call put in (ids from <paramref name="firstId"/> on, not yet counted): one encounter per kind among them
    /// (a pack is one), hazards (a car fire, the marsh) left out, in the order their kinds first came.
    /// </summary>
    public List<Encounter> Came(World world, SpawnChannel channel, int firstId) =>
        [.. world.ActiveEnemies.Where(e => e.Id >= firstId && e.Id >= _seen && Counts(e) && !_byMember.ContainsKey(e.Id))
            .GroupBy(e => e.Kind).Select(g => Open(world, channel, g.Key, [.. g]))];

    /// <summary>
    /// What makes an encounter: not a hazard (the marsh, Dave), nor a car fire, which is what a pack or a spark did to a car,
    /// not something come for the crew (it isn't engaged either, <see cref="Director.Engaged"/>'s callers leave it out).
    /// </summary>
    public static bool Counts(Enemy e) => !e.Hazard && e.Kind != EnemyKind.CarFire;

    Encounter Open(World world, SpawnChannel channel, EnemyKind kind, List<Enemy> members)
    {
        var train = world.Train;
        var lead = members[0];
        var (where, vehicle) = Place(lead, train);
        int n = train.Vehicles.Count;
        var living = world.CrewThisTick.Where(c => c.State.Alive).ToList();
        var e = new Encounter
        {
            Index = Encounters.Count,
            Channel = channel,
            Kind = kind,
            Members = [.. members.Select(m => m.Id)],
            Zone = lead.Zone,
            Where = where,
            Vehicle = vehicle,
            Part = vehicle < 0 ? "" : vehicle * 3 < n ? "front" : vehicle * 3 < 2 * n ? "middle" : "rear",
            Afoot = living.Count > 0 && living.All(c => CrewSense.Afoot(c.State, train)),
            Tick = world.Tick,
            Seconds = Seconds(world),
            Progress = Advance(world),
            Crew = living.Count,
            Cars = Cars(world),
            Overlap = _open.Count(o => o.Encounter.LastEngagedAt >= Seconds(world) - 1),
        };
        Encounters.Add(e);
        _open.Add((e, members));
        foreach (var m in members)
            _byMember[m.Id] = e;
        return e;
    }

    /// <summary>
    /// Once a second, after the enemies have stepped: anything put in that no spawn call claimed is the world's own; then each
    /// open encounter's members are read (engaged, confronting, dormant, how near the crew), the concurrency, the director's
    /// held reason and the drought clock.
    /// </summary>
    public void Sweep(World world)
    {
        Came(world, SpawnChannel.World, _seen);
        _seen = world.NextEnemyId;
        double now = Seconds(world);
        bool counting = world.Run is not { Phase: Run.RunPhase.Yard or Run.RunPhase.Arrived or Run.RunPhase.Failed } && !world.SafeYard;
        Advance(world);
        var train = world.Train;
        var crew = Threats.LivingAt(world);
        // Presence (level 1) is near the crew or the train: the train's vehicles stand for it.
        var near = crew.Concat(train.Frames.Select(f => f.Origin)).ToList();
        int active = 0, highest = 0;
        var harassing = new List<(Encounter Encounter, Double3 At)>();
        foreach (var (enc, bodies) in _open.ToList())
        {
            var members = bodies.Where(b => !b.Gone).ToList();
            if (members.Count == 0)
            {
                enc.EndedAt = now;
                enc.End = bodies.All(b => b.Health <= 0) ? EncounterEnd.Killed : EncounterEnd.Gone;
                _open.Remove((enc, bodies));
                continue;
            }
            var states = members.Select(m => Threats.Of(m, world, crew, _threats)).ToList();
            bool engaged = states.Any(Threats.Counts), confronting = members.Any(Director.Confronting);
            if (engaged)
            {
                active++;
                enc.EngagedSeconds++;
                enc.LastEngagedAt = now;
                if (enc.EngagedAt < 0)
                    enc.EngagedAt = now;
            }
            else
                enc.DormantSeconds++;
            if (confronting && enc.ConfrontedAt < 0)
                enc.ConfrontedAt = now;
            bool present = members.Any(m => m.WorldPosition(train) is var at && near.Any(p => (p - at).Length <= _t.PresenceMetres));
            int level = EncounterSeverity.Level(states, present);
            if (level >= 2)
            {
                harassing.Add((enc, members[states.FindIndex(Threats.Counts)].WorldPosition(train)));
                if (enc.HarassedAt < 0)
                    enc.HarassedAt = now;
                _lastMeaningful = now;
            }
            else
                enc.SecondsAt[level]++;
            enc.Severity = Math.Max(enc.Severity, level);
            highest = Math.Max(highest, level);
            foreach (var m in members)
            {
                var p = m.WorldPosition(train);
                foreach (var c in crew)
                {
                    double d = (c - p).Length;
                    enc.NearestMetres = Math.Min(enc.NearestMetres, d);
                    if (d <= _t.NearMetres && enc.NearAt < 0)
                        enc.NearAt = now;
                }
            }
        }
        // Level 3: harassing while another problem harasses elsewhere (the crew has to split up, or choose).
        var compound = EncounterSeverity.Compound([.. harassing.Select(h => h.At)], _t.CompoundApartMetres);
        for (int i = 0; i < harassing.Count; i++)
        {
            var enc = harassing[i].Encounter;
            enc.SecondsAt[compound[i] ? 3 : 2]++;
            if (!compound[i])
                continue;
            enc.Severity = 3;
            if (enc.CompoundAt < 0)
                enc.CompoundAt = now;
        }
        PeakActive = Math.Max(PeakActive, active);
        DebtPeak = Math.Max(DebtPeak, Debt);
        PeakBodies = Math.Max(PeakBodies, world.ActiveEnemies.Count(e => !e.Gone && Counts(e)));
        if (!counting)
            return;
        OutSeconds++;
        Concurrent[Math.Min(active, Concurrent.Length - 1)]++;
        string held = world.Derailed ? "derailed" : world.Director?.HeldBecause ?? "sent";
        Suppressed[held] = Suppressed.GetValueOrDefault(held) + 1;
        Now = EncounterSeverity.Classify(highest, EncounterSeverity.Resting(held, _t.RecoveryHolds));
        QuietSeconds[(int)Now]++;
        if (Now == QuietKind.Drought)
        {
            if (_quietFrom < 0)
                _quietFrom = now - 1;
            DroughtNow = now - _quietFrom;
        }
        else
            CloseTrueDrought(now);
        // The drought clock: from the last second something confronted the crew (or the run's start), closed when something does.
        if (_lastMeaningful == now)
            CloseDrought(now);
        else
        {
            if (_droughtFrom < 0)
                _droughtFrom = Math.Max(_lastMeaningful, now - 1);
            _droughtHeld[held] = _droughtHeld.GetValueOrDefault(held) + 1;
        }
    }

    /// <summary>Each tick's spine events (the world's, after its enemies step): the grabs and punishes, to their encounters.</summary>
    public void Heard(IReadOnlyList<EnemyEvent> events)
    {
        foreach (var ev in events)
            if (_byMember.TryGetValue(ev.EnemyId, out var at))
            {
                if (ev.To == SpinePhase.Grab)
                    at.Grabs++;
                else if (ev.To == SpinePhase.Punish)
                    at.Punishes++;
            }
    }

    /// <summary>The night's over (the harness's last tick): the drought still running is closed now.</summary>
    public void Finish(World world)
    {
        CloseDrought(Seconds(world));
        CloseTrueDrought(Seconds(world));
    }

    void CloseTrueDrought(double now)
    {
        if (_quietFrom >= 0 && now - _quietFrom >= _t.DroughtSeconds)
            TrueDroughts.Add(new Drought(_quietFrom, now, _progress, "drought"));
        _quietFrom = -1;
        DroughtNow = 0;
    }

    void CloseDrought(double now)
    {
        if (_droughtFrom >= 0 && now - _droughtFrom >= _t.DroughtSeconds)
            Droughts.Add(new Drought(_droughtFrom, now, _progress,
                _droughtHeld.OrderByDescending(h => h.Value).ThenBy(h => h.Key, StringComparer.Ordinal).Select(h => h.Key).FirstOrDefault() ?? ""));
        _droughtFrom = -1;
        _droughtHeld.Clear();
    }

    /// <summary>Seconds since the last meaningful encounter (or since the log began counting), for the director to read.</summary>
    public double SinceMeaningful(World world) => Seconds(world) - Math.Max(0, _lastMeaningful);

    static double Seconds(World world) => world.Run is { Tuning.YardIsSafe: true } r ? r.Seconds : world.ElapsedSeconds;

    static int Cars(World world) => world.Train.Dynamics.Consist.CarCount;

    static int Living(World world) => world.CrewThisTick.Count(c => c.State.Alive);

    double Advance(World world)
    {
        if (world.Route is { } route)
        {
            double time = route.DawnSeconds > 0 && world.Run is { } run ? run.Seconds / route.DawnSeconds : 0;
            double along = route.RunLength > 0 ? world.Train.Dynamics.Distance / route.RunLength : 0;
            _progress = Math.Max(_progress, Math.Clamp(Math.Max(time, along), 0, 1));
        }
        return _progress;
    }

    (string Where, int Vehicle) Place(Enemy e, TrainOnLine train)
    {
        if (e.Attached >= 0 && e.Attached < train.Frames.Count)
        {
            var frame = train.Frames[e.Attached];
            return (e.Attached == 0 ? "engine" : e.Local.Y >= frame.Shape.RoofHeight - 0.2 ? "roof" : "car", e.Attached);
        }
        // Off the train: the nearest vehicle along the line, and trackside within the line's reach of it, afield past it.
        var at = e.WorldPosition(train);
        int nearest = -1;
        double best = double.PositiveInfinity;
        for (int i = 0; i < train.Frames.Count; i++)
        {
            double d = (train.Frames[i].Origin - at).Length;
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }
        return best <= _t.TracksideMetres ? ("trackside", nearest) : ("afield", -1);
    }
}

/// <summary>
/// The encounter telemetry's measures (enemies.json <c>director.telemetry</c>; the director's brief §7, §8; note 880). They
/// only shape the report, never a night.
/// </summary>
/// <param name="NearMetres">A crewmate this close to a member is "near" it: the perception proxy, never "seen" (§8).</param>
/// <param name="DroughtSeconds">A stretch out on the run this long with nothing confronting the crew is a drought (§7: 90 s).</param>
/// <param name="TracksideMetres">Off the train, this close to a vehicle is trackside; further, afield.</param>
/// <param name="PresenceMetres">
/// Level 1 (note 893): a creature about within this of the train or a living crewmate is present, near enough to be noticed
/// (a proxy, never "noticed").
/// </param>
/// <param name="CompoundApartMetres">Level 3 (note 893): two harassing encounters at least this far apart are a compound crisis (0: any two).</param>
public sealed record TelemetryTuning(double NearMetres = 25, double DroughtSeconds = 90, double TracksideMetres = 30,
    double PresenceMetres = 60, double CompoundApartMetres = 15)
{
    /// <summary>
    /// The director's held reasons (<see cref="Director.HeldBecause"/>, each a prefix) that are its rests, not a drought (§6;
    /// note 893): the grace, an aftermath, the facility lull, a fort, a ban, the final stretch, the terrain at its ceiling.
    /// </summary>
    public string[] RecoveryHolds { get; init; } = ["grace", "aftermath", "facility lull", "in a fort", "banned", "final stretch", "terrain at its ceiling", "derailed"];
}
