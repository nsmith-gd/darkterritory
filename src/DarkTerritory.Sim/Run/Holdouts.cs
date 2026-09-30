using System.Globalization;
using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>GDD App. D.5: a Holdout's life. Dormant until the crew come by with someone waiting; spent once they're out.</summary>
public enum HoldoutPhase : byte { Dormant, Occupied, Breaching, Freed }

/// <summary>A D.5 transition, for the host to act on (a freed player) and the harness and the report to count.</summary>
public enum HoldoutOutcome : byte { Assigned, Reassigned, Emptied, BreachStarted, Interrupted, Freed, Released }

/// <param name="Player">Who it's about: the occupant (assigned, reassigned, freed, released), or the breacher.</param>
/// <param name="By">For a breach or a rescue, who worked it.</param>
public readonly record struct HoldoutEvent(HoldoutOutcome Outcome, int Holdout, int Player, int By = -1);

/// <summary>
/// One Holdout at run time (D.4, D.5): its site from the Line Plan, and its state. The host runs it; clients mirror it.
/// </summary>
public sealed class Holdout
{
    internal Holdout(int index, PlanHoldout site, LinePlan plan, RailLine line)
    {
        Index = index;
        Site = site;
        Centre = new Double3(site.X, site.Y, site.Z);
        Door = new Double3(site.Door[0], site.Door[1], site.Door[2]);
        Lamp = new Double3(site.Lamp[0], site.Lamp[1], site.Lamp[2]);
        Yaw = site.HeadingDeg * Math.PI / 180;
        (ZonePath, ZoneS0, ZoneS1) = PathRange(plan, line, site.Zone.Edge, site.Zone.S0, site.Zone.S1);
        SpurPath = site.Spur is { } spur ? plan.Edge(spur).Branch : int.MinValue;
        SubSeed = ulong.TryParse(site.SubSeed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seed) ? seed : (ulong)index;
    }

    static (int Path, double S0, double S1) PathRange(LinePlan plan, RailLine line, string edge, double s0, double s1)
    {
        if (edge == "main")
            return (RailLine.MainPath, s0, s1);
        int b = plan.Edge(edge).Branch;
        double toe = b >= 0 && b < line.Branches.Count ? line.Branches[b].Toe : 0;
        return (b, toe + s0, toe + s1);
    }

    public int Index { get; }
    public PlanHoldout Site { get; }
    public string Id => Site.Id;
    public HoldoutType Type => Site.Type;
    public Double3 Centre { get; }
    public Double3 Door { get; }
    /// <summary>
    /// The Holdout lamp (D.4): a world light, lit while it's Occupied or Breaching. It isn't a car lamp (nothing that
    /// hunts a train's lights comes for it) and the cab's lamp switch doesn't touch it (D.7 "Fire Flies").
    /// </summary>
    public Double3 Lamp { get; }
    public double Yaw { get; }
    internal readonly int ZonePath;
    internal readonly double ZoneS0, ZoneS1;
    internal readonly int SpurPath;
    internal readonly ulong SubSeed;

    public HoldoutPhase Phase { get; internal set; }
    /// <summary>The player waiting inside (Occupied, Breaching), or −1.</summary>
    public int Assigned { get; internal set; } = -1;
    /// <summary>Who's working the breach (Breaching), or −1.</summary>
    public int Breacher { get; internal set; } = -1;
    public BreachMethod Method { get; internal set; }
    /// <summary>Seconds into the breach, and how many it takes (D.7's table).</summary>
    public double Progress { get; internal set; }
    public double Needed { get; internal set; }
    /// <summary>Call Out's cooldown, shared by everyone who calls from here (D.7), seconds left.</summary>
    public double CallOutCooldown { get; internal set; }
    /// <summary>How many Call Outs it has made (a client plays one each time it goes up), and which sound the last was.</summary>
    public int CallOuts { get; internal set; }
    public int CallOutSound { get; internal set; }
    /// <summary>D.7 Live Mic: the assigned player's voice plays from here on the proximity layer.</summary>
    public bool LiveMic { get; internal set; }
    /// <summary>The occupant's look and voice (indices into holdouts.json's survivor pools): D.8's survivor, D.7's Call Out.</summary>
    public int Appearance { get; internal set; }
    public int VoiceSet { get; internal set; }

    public bool LampLit => Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching;
    /// <summary>Sealed until it's breached (D.4 "the interior is a safe volume").</summary>
    public bool Sealed => Phase != HoldoutPhase.Freed;

    /// <summary>Whether a world point is inside its walls (its footprint, floor to roof).</summary>
    public bool Inside(Double3 world)
    {
        var d = world - Centre;
        double c = Math.Cos(Yaw), s = Math.Sin(Yaw);
        // Along is (−sin, −cos), across is (cos, −sin): see HoldoutSites.Footprint.
        double along = -d.X * s - d.Z * c, across = d.X * c - d.Z * s;
        return Math.Abs(across) <= Site.Size[0] && Math.Abs(along) <= Site.Size[1] && d.Y >= -0.5 && d.Y <= Site.Size[2];
    }

    /// <summary>Whether the consist's engine is in the site's zone (D.5): on its stretch of line, or down its spur.</summary>
    public bool InZone(int path, double distance) =>
        path == SpurPath || path == ZonePath && distance >= ZoneS0 && distance <= ZoneS1;

    /// <summary>Outside the zone and not coming back into it: past its far end going on, short of it backing away, or elsewhere.</summary>
    public bool LeftMovingAway(int path, double distance, double velocity)
    {
        if (InZone(path, distance))
            return false;
        if (path != ZonePath)
            return true;
        return distance > ZoneS1 ? velocity >= 0 : velocity <= 0;
    }

    /// <summary>Client side: adopts the host's state (who's inside comes with the queue, to the dead and lobbied only).</summary>
    public void Mirror(HoldoutPhase phase, int breacher, BreachMethod method, double progress, double needed, double cooldown, int callOuts,
        int sound, bool liveMic, int appearance, int voiceSet)
    {
        Phase = phase;
        Breacher = breacher;
        Method = method;
        Progress = progress;
        Needed = needed;
        CallOutCooldown = cooldown;
        CallOuts = callOuts;
        CallOutSound = sound;
        LiveMic = liveMic;
        Appearance = appearance;
        VoiceSet = voiceSet;
    }
}

/// <summary>
/// The night's Holdouts and the respawn queue they take from (GDD App. D.5, D.6): once a run has left the gate, a
/// Holdout is the only way back into it. Host-authoritative; clients mirror the Holdouts (and the dead and lobbied, the
/// queue) from records.
/// <list type="table">
/// <item><b>Assign:</b> the engine enters the site's zone and someone in the queue is eligible (their last death wasn't in
/// this site's zone): the first eligible entry. A second facility Holdout only with a session crew of five or more.</item>
/// <item><b>Reassign:</b> the assigned player defers or leaves before the breach starts: the next eligible.</item>
/// <item><b>Breach:</b> a living crew member holds Use at the door with the right thing in their hands (D.7); it locks the
/// assignment. Taking damage, leaving the door or letting go interrupts it: back to Occupied, progress to zero.</item>
/// <item><b>Freed:</b> the breach completes; the player spawns inside (D.8) and the Holdout is spent.</item>
/// <item><b>Release:</b> the engine has left the zone moving away and no living crew member is within 400 m: the player's
/// back in the place they held in the queue, and it can assign again if the crew come back.</item>
/// </list>
/// </summary>
public sealed class Holdouts
{
    readonly List<Holdout> _all;
    // Who held Use at which door this tick (host): a breach goes on only while its breacher does.
    readonly Dictionary<int, int> _holding = new();

    public Holdouts(HoldoutTuning tuning, LinePlan plan, RailLine line)
    {
        Tuning = tuning;
        _all = [.. plan.Holdouts.Select((h, i) => new Holdout(i, h, plan, line))];
    }

    public HoldoutTuning Tuning { get; set; }
    public IReadOnlyList<Holdout> All => _all;
    public RespawnQueue Queue { get; } = new();
    /// <summary>What happened this tick (host).</summary>
    public List<HoldoutEvent> Events { get; } = new();
    /// <summary>Everything that's happened this run, oldest first (host): the report's rescues come from it.</summary>
    public List<(double Seconds, HoldoutEvent Event)> History { get; } = new();

    public Holdout? Of(string id) => _all.Find(h => h.Id == id);

    /// <summary>
    /// Client side: the queue as the host sent it (player, kind, Holdout index or −1, locked), and who each Holdout holds.
    /// Only the dead and lobbied are sent it; for the living it's empty.
    /// </summary>
    public void MirrorQueue(IReadOnlyList<(int Player, QueueKind Kind, int Holdout, bool Locked)> entries)
    {
        Queue.Mirror([.. entries.Select(e => new QueueEntry(e.Player, e.Kind, 0)
        {
            Holdout = e.Holdout >= 0 && e.Holdout < _all.Count ? _all[e.Holdout].Id : null,
            Locked = e.Locked,
        })]);
        foreach (var h in _all)
            h.Assigned = -1;
        foreach (var e in entries)
            if (e.Holdout >= 0 && e.Holdout < _all.Count)
                _all[e.Holdout].Assigned = e.Player;
    }

    /// <summary>The sites whose zones take in this stretch of track (D.5 eligibility: where a death was).</summary>
    public IReadOnlyList<string> ZonesAt(int path, double distance) =>
        [.. _all.Where(h => h.InZone(path, distance)).Select(h => h.Site.Site).Distinct()];

    /// <summary>Whether a world point is inside a sealed Holdout (D.4 "no enemy spawns, paths or deals damage" there).</summary>
    public bool InSealed(Double3 world) => _all.Any(h => h.Sealed && h.Inside(world));

    /// <summary>The Holdout whose door a player's hands are at (within breach reach), if any is waiting to be broken into.</summary>
    public Holdout? DoorInReach(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (!s.Alive)
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        foreach (var h in _all)
            if (h.Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching
                && PlayerMotor.Grips(s, train, hand, h.Door + Double3.Up * 1.1, HoldoutSites.Horizontal(at, h.Door) <= Tuning.Breach.ReachM && Math.Abs(at.Y - h.Door.Y) < 2))
                return h;
        return null;
    }

    /// <summary>The breach a player could work at this Holdout with what's in their hands (D.7), if any.</summary>
    public BreachMethod? MethodFor(Holdout h, BodyKind? inHands)
    {
        var tool = inHands switch
        {
            BodyKind.Shovel or BodyKind.Wrench or BodyKind.Crowbar => BreachTool.Melee,
            BodyKind.RepairKit => BreachTool.RepairKit,
            _ => (BreachTool?)null,
        };
        if (tool is null || !Tuning.Methods.TryGetValue(h.Type, out var methods))
            return null;
        foreach (var m in methods)
            if (Tuning.Step(m).Tool == tool)
                return m;
        return null;
    }

    /// <summary>
    /// A player's hands this tick (host, from <see cref="World.CrewAct"/>): holding Use at a Holdout's door with a tool that
    /// breaks it in starts the breach, or keeps it going. Returns true if it took the Use, so it isn't also a pick-up or a
    /// put-down of what's in their hands.
    /// </summary>
    public bool CrewAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train, Bodies bodies, HandTuning? hand = null)
    {
        if (!intent.Has(PlayerButtons.Use) || intent.MoveZ > 0.5 || DoorInReach(s, train, hand) is not { } h)
            return false;
        var inHands = bodies.CarriedBy(playerId)?.Kind;
        if (MethodFor(h, inHands) is not { } method)
            // A tool that won't open this one is still a tool in the hands at a door: it isn't dropped by trying.
            return inHands is BodyKind.Shovel or BodyKind.Wrench or BodyKind.Crowbar or BodyKind.RepairKit;
        if (h.Phase == HoldoutPhase.Occupied)
        {
            h.Phase = HoldoutPhase.Breaching;
            h.Breacher = playerId;
            h.Method = method;
            h.Progress = 0;
            h.Needed = Tuning.Step(method).Seconds;
            Queue.Lock(h.Id, true);
            Raise(new HoldoutEvent(HoldoutOutcome.BreachStarted, h.Index, h.Assigned, playerId));
        }
        if (h.Breacher == playerId)
            _holding[playerId] = h.Index;
        return true;
    }

    /// <summary>The loudness a breach under way makes (D.7): its method's level ("cannon", "machinery"), or none.</summary>
    public IEnumerable<string> Noise() =>
        _all.Where(h => h.Phase == HoldoutPhase.Breaching).Select(h => Tuning.Step(h.Method).Loudness).Where(l => l != "none");

    /// <summary>
    /// Host, after the tick's damage: breaches go on or are interrupted, the freed come out, and Holdouts assign, reassign
    /// and release against where the train is and who's about.
    /// </summary>
    /// <param name="crew">Everyone's state (the living, the dead and the lobbied).</param>
    /// <param name="damaged">Who took damage this tick (it interrupts a breach).</param>
    /// <param name="sessionCrew">Everyone in the session, for the second facility Holdout's gate (D.4).</param>
    public void Step(TrainOnLine train, IReadOnlyCollection<(int Id, PlayerState State)> crew, IReadOnlySet<int> damaged, int sessionCrew, double seconds, double dt)
    {
        Events.Clear();
        _seconds = seconds;
        var engine = train.Rakes.First(r => r.Consist.HasEngine);
        foreach (var h in _all)
        {
            h.CallOutCooldown = Math.Max(0, h.CallOutCooldown - dt);
            if (h.Phase == HoldoutPhase.Breaching)
                Breach(h, crew, damaged, dt);
            if (h.Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching)
            {
                // The one waiting here left (deferred, or disconnected): the next eligible, or it's empty again.
                if (Queue.AssignedTo(h.Id) is not { } entry || entry.Player != h.Assigned)
                {
                    if (h.Phase == HoldoutPhase.Breaching)
                        Interrupt(h);
                    Reassign(h);
                }
                else if (engine.Consist.Vehicles.Count > 0 && h.LeftMovingAway(engine.Path, engine.Distance, engine.Velocity)
                    && !crew.Any(c => c.State.Alive && (PlayerMotor.WorldPosition(c.State, train) - h.Centre).Length <= Tuning.ReleaseM))
                    Release(h);
            }
            if (h.Phase == HoldoutPhase.Dormant && h.InZone(engine.Path, engine.Distance) && (!h.Site.Second || sessionCrew >= Tuning.SecondCrew)
                && Queue.Assign(h.Id, e => !e.DiedIn.Contains(h.Site.Site)) is { } taken)
            {
                Occupy(h, taken.Player);
                Raise(new HoldoutEvent(HoldoutOutcome.Assigned, h.Index, taken.Player));
            }
        }
        _holding.Clear();
    }

    double _seconds;

    void Raise(HoldoutEvent e)
    {
        Events.Add(e);
        History.Add((_seconds, e));
    }

    void Occupy(Holdout h, int player)
    {
        h.Phase = HoldoutPhase.Occupied;
        h.Assigned = player;
        h.LiveMic = false;
        // The occupant: a survivor from the pools (D.4), the same for the same player at the same Holdout.
        ulong pick = Streams.Mix(h.SubSeed, "occupant", "", player);
        h.Appearance = (int)(pick % (ulong)Math.Max(1, Tuning.Survivors.Appearances.Length));
        h.VoiceSet = (int)((pick >> 20) % (ulong)Math.Max(1, Tuning.Survivors.VoiceSets.Length));
    }

    void Reassign(Holdout h)
    {
        h.LiveMic = false;
        if (Queue.Assign(h.Id, e => !e.DiedIn.Contains(h.Site.Site)) is { } next)
        {
            // D.5 "reassign while Occupied": the lamp stays lit.
            Occupy(h, next.Player);
            Raise(new HoldoutEvent(HoldoutOutcome.Reassigned, h.Index, next.Player));
            return;
        }
        int was = h.Assigned;
        h.Phase = HoldoutPhase.Dormant;
        h.Assigned = -1;
        Raise(new HoldoutEvent(HoldoutOutcome.Emptied, h.Index, was));
    }

    void Breach(Holdout h, IReadOnlyCollection<(int Id, PlayerState State)> crew, IReadOnlySet<int> damaged, double dt)
    {
        // D.5 "interrupted": the breacher takes damage, moves out of range or lets go (they didn't hold Use at this door).
        bool held = _holding.TryGetValue(h.Breacher, out int at) && at == h.Index;
        bool alive = crew.Any(c => c.Id == h.Breacher && c.State.Alive);
        if (!held || !alive || damaged.Contains(h.Breacher))
        {
            Interrupt(h);
            return;
        }
        h.Progress += dt;
        if (h.Progress + 1e-9 < h.Needed)
            return;
        int freed = h.Assigned, by = h.Breacher;
        Queue.Freed(h.Id);
        h.Phase = HoldoutPhase.Freed;
        h.Breacher = -1;
        h.LiveMic = false;
        Raise(new HoldoutEvent(HoldoutOutcome.Freed, h.Index, freed, by));
    }

    void Interrupt(Holdout h)
    {
        int by = h.Breacher;
        h.Phase = HoldoutPhase.Occupied;
        h.Breacher = -1;
        h.Progress = 0;
        Queue.Lock(h.Id, false);
        Raise(new HoldoutEvent(HoldoutOutcome.Interrupted, h.Index, h.Assigned, by));
    }

    void Release(Holdout h)
    {
        int player = h.Assigned;
        if (h.Phase == HoldoutPhase.Breaching)
            Interrupt(h);
        Queue.Release(h.Id);
        h.Phase = HoldoutPhase.Dormant;
        h.Assigned = -1;
        h.LiveMic = false;
        Raise(new HoldoutEvent(HoldoutOutcome.Released, h.Index, player));
    }

    /// <summary>
    /// D.8: where a freed player stands up: inside the Holdout, on its floor, facing its door. At 80 of 100 (holdouts.json
    /// freedHealth), and with the standard kit (the host gives it).
    /// </summary>
    public PlayerState FreedState(Holdout h, TrainOnLine train, PlayerTuning p)
    {
        var s = PlayerMotor.SpawnOnGround(h.Centre, train.Line, NearestMain(train, h.Centre), p);
        var outward = h.Door - h.Centre;
        s.Yaw = Math.Atan2(-outward.X, -outward.Z);
        s.Health = Tuning.FreedHealth;
        return s;
    }

    static double NearestMain(TrainOnLine train, Double3 at)
    {
        double hint = train.Dynamics.Distance;
        train.Line.Nearest(at, ref hint);
        return hint;
    }
}
