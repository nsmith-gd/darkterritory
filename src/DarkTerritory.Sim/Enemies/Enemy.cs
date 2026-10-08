using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

public enum EnemyKind : byte
{
    // GDD v1.1 §21's roster, and two hazards that are still things in the world: Sleepers (v1.1's track debris) and the Drift
    // (the marsh). v1.0's retired enemies' numbers (Clingers, the Hollow, the Rattle, Lamplighters, the Deadman, the
    // Ferryman, the Long Whistle, the Weight, the loose load and the Gnawers) aren't reused.
    Sleepers = 1, CinderHound = 2, Switchman = 5, SootChildren = 6, Dragger = 7, Stoker = 11, Climber = 14, Gaunt = 16,
    CarFire = 17, Passenger = 20, Follower = 21, Drift = 22,
    TrackDoll = 23, CarHugger = 24, Whistler = 25, TippyToesie = 26, FireFlies = 27, Ribbit = 28, Grumbler = 29, Choir = 30,
    // The Moose (GDD §21, the director's decisions of 7 Oct 2026; note 339).
    Moose = 31,
    // The Gannet (GDD §21, the director's decisions of 7 Oct 2026; note 340).
    Gannet = 32,
    // The Mourners (GDD §21, the director's brief of 8 Oct 2026; note 362).
    Mourners = 33,
    // The Brakeman (GDD §21, the director's brief of 8 Oct 2026; note 364).
    Brakeman = 35,
    // The Freight Beetle (GDD §21, the director's brief of 8 Oct 2026; note 366).
    FreightBeetle = 37,
    // Hotbox (GDD §21, the director's brief of 8 Oct 2026; note 367).
    Hotbox = 38
}

/// <summary>
/// Where a threat comes from (GDD v1.1 §21), and so which answer applies. Outside is the ground at facilities, villages and
/// yards, where the crew is on foot; Corrupted is the corrupted humans (App. B.8, at most one about at a time).
/// </summary>
public enum PressureZone : byte { Forward, Rear, Flank, Interior, Structural, Outside, Corrupted }

/// <summary>The player want an enemy attacks (App. B.1 "want balance"): the director aims each at a share of the budget.</summary>
public enum Want : byte { Kill, Split, Trust, Cargo }

/// <summary>What a creature senses (App. A.1): each enemy has exactly one primary trigger.</summary>
public enum Sense : byte { Sound, Light, Heat, Movement, Scent, Vibration, Sight, Absence }

/// <summary>
/// The shared spine every enemy runs (GDD v1.1 App. A.1): DORMANT → ALERT → TELEGRAPH → COMMIT → GRAB → PUNISH, or BREAK
/// OFF. A kill always passes through <see cref="Grab"/>: a player held long enough for a friend to act.
/// </summary>
public enum SpinePhase : byte { Dormant, Alert, Telegraph, Commit, Grab, Punish, BreakOff, Gone }

/// <summary>A spine transition, logged for audio/visual cues on clients and for the fairness audit.</summary>
public readonly record struct EnemyEvent(uint Tick, int EnemyId, EnemyKind Kind, SpinePhase From, SpinePhase To, double SecondsInFrom);

/// <summary>
/// Base for every enemy. Owns the spine and enforces the fairness contract structurally: <see cref="Enter"/>
/// refuses to go to <see cref="SpinePhase.Commit"/> unless the enemy has been telegraphing for at least the
/// minimum reaction window. "No enemy in this game may punish a player who was given no window." (App. A.1)
/// </summary>
public abstract class Enemy
{
    protected Enemy(int id) => Id = id;

    public int Id { get; }
    public abstract EnemyKind Kind { get; }
    public abstract PressureZone Zone { get; }
    public abstract Sense Sense { get; }
    /// <summary>
    /// A hazard, not an enemy (GDD v1.1 §22: track debris and the marsh): level content the director doesn't spend on, cap or
    /// count, and which can kill on its own terms (hazards remove your tools; they never change enemy rules).
    /// </summary>
    public virtual bool Hazard => false;
    /// <summary>The want it attacks (App. B.1), for the director's budget shares.</summary>
    public virtual Want Want => Want.Kill;

    public SpinePhase Phase { get; private set; } = SpinePhase.Dormant;
    public double PhaseSeconds { get; private set; }
    public double Health { get; set; } = 1;
    public bool Gone => Phase == SpinePhase.Gone;

    /// <summary>
    /// Vehicle the enemy is on (car-local <see cref="Local"/>), −1 if it's free on the line, or <see cref="Loose"/>: stood
    /// in the world at <see cref="Local"/> (Followers on the ground, at someone's back).
    /// </summary>
    public int Attached { get; set; } = -1;
    public const int Loose = -2;
    public Double3 Local { get; set; }
    /// <summary>Along-line position, lateral offset and height when free on the line.</summary>
    public double LineDistance { get; set; }
    public double Lateral { get; set; }
    public double Height { get; set; }
    /// <summary>
    /// Whether its body stands in the open now, where a cannonball can find it (GDD App. F.1 "the guns do nothing"; note
    /// 290): by default wherever a tool's blow can land on it. Its body is enemies.json <c>bodies</c>; a ball that finds it
    /// lands as a blow (<see cref="Hit"/>). From replicated state, so a client's prediction finds the same bodies.
    /// </summary>
    public virtual bool Exposed => MeleeRadius > 0 && !Gone;

    /// <summary>How much of its body's height it stands at now (a sleeping Gaunt, curled up); 1 upright.</summary>
    public virtual double Stoop(EnemyTuning t) => 1;

    /// <summary>
    /// Its body as a ball finds it now (world): enemies.json <c>bodies</c>' spheres over where it stands, or none when it
    /// isn't <see cref="Exposed"/> (or its kind has no body).
    /// </summary>
    public IEnumerable<Combat.HitTarget> Body(TrainOnLine train, EnemyTuning t)
    {
        if (!Exposed)
            yield break;
        var at = WorldPosition(train);
        double stoop = Stoop(t);
        foreach (var (radius, height) in t.Body(Kind))
            yield return new Combat.HitTarget(Id, at + Double3.Up * (height * stoop), radius);
    }

    /// <summary>Where a gunner lays on it: the middle of its body (world); where it stands, with no body.</summary>
    public Double3 AimPoint(TrainOnLine train, EnemyTuning t)
    {
        var body = t.Body(Kind);
        double height = body.Length == 0 ? 0 : body.Average(s => s.Height) * Stoop(t);
        return WorldPosition(train) + Double3.Up * height;
    }

    /// <summary>
    /// The cannon is its answer (GDD §21, App. A): the gunner's own work, laid on whenever it's in range (a bot gunner's
    /// targets). Anything else a ball can still find, but the gun is no answer to it, save to free a crewmate it holds.
    /// </summary>
    public virtual bool GunAnswers => false;
    /// <summary>
    /// Lies on the main line wherever the train is (Sleepers across the rail). Everything else off the train is
    /// placed along the engine's path: it's after the train, down a branch too.
    /// </summary>
    public virtual bool OnMainLine => false;
    /// <summary>
    /// Seen or heard from past the interest radius (the Ferryman's lantern, the Long Whistle's horn): sent to every client
    /// wherever it is, as the Choir's voice is (ARCHITECTURE §6.2).
    /// </summary>
    public virtual bool Far => false;

    /// <summary>The player it holds in <see cref="SpinePhase.Grab"/> (and punishes after), or −1. Replicated: the HUD's "held".</summary>
    public int Holding { get; private set; } = -1;
    /// <summary>How long this grab lasts before the punish (App. A.1's rescue window, 8–20 s).</summary>
    public double GrabWindow { get; private set; }
    /// <summary>Who last struck it with a tool (the Grumbler hunts them), and when (the world's tick).</summary>
    public int LastHitBy { get; private set; } = -1;
    public uint LastHitTick { get; private set; }

    /// <summary>
    /// How far a tool swing reaches it from (App. C.2): 0 can't be struck at all. Most things on the train can be clubbed.
    /// </summary>
    public virtual double MeleeRadius => 0;

    /// <summary>
    /// Whether a swing by <paramref name="by"/> can land on it now (App. C.2): in reach of a tool at all, and by default by
    /// anyone. A blow that's picked lands, and every client is told it did (T121's hit confirm).
    /// </summary>
    public virtual bool Strikable(int by) => MeleeRadius > 0;

    /// <summary>
    /// Whether a swing can get at it where it is now (the Stoker: only through the open firebox door, App. A.5; note 263).
    /// From replicated state, so a client's prompt and whiff agree with the host.
    /// </summary>
    public virtual bool Reachable(World world) => true;
    /// <summary>A crewmate holding Use at the victim pulls them free of this grab (Draggers, the Car Hugger, Tippy Toesie).</summary>
    public virtual bool PullsFree => false;
    /// <summary>
    /// Host: it stays until the crew deals with it, so the director never dismisses it for want of company (Cinder Hounds
    /// aboard: GDD App. F, 6 Oct 2026, note 269).
    /// </summary>
    public virtual bool StaysAboard => false;

    /// <summary>
    /// A vehicle it drags on while the train's over <see cref="DragAbove"/> (the Car Hugger's speed cap), or −1. From
    /// replicated state, so a predicting client drags the same.
    /// </summary>
    public virtual int Drags => -1;
    public virtual double DragAbove => 0;
    /// <summary>The drag, as a multiple of the engine's full tractive force (over 1: more than it can pull).</summary>
    public virtual double DragFactor => 0;

    /// <summary>Plays with the cab's controls (the Track Doll in an empty cab). From replicated state, on clients too.</summary>
    public virtual void Tamper(World world, ref TrainControls controls) { }

    /// <summary>
    /// Whether its <see cref="Tamper"/> may let a standing train off the brake it's held on, where enemies.json
    /// <c>tamperReleasesStandingBrake</c> otherwise keeps it on (note 263): only the Track Doll at her last stage (note 268).
    /// From replicated state, so a predicting client lets it off as the host does.
    /// </summary>
    public virtual bool ReleasesStandingBrake(World world) => false;

    /// <summary>Free per-kind values that are replicated (drill progress, pry progress, pack id).</summary>
    public double Extra { get; set; }
    public double Extra2 { get; set; }

    public Double3 WorldPosition(TrainOnLine train)
    {
        if (Attached >= 0)
            return train.Frames[Attached].ToWorld(Local);
        if (Attached == Loose)
            return Local;
        var t = OnMainLine ? train.Line.Sample(LineDistance) : train.Line.Sample(train.Dynamics.Path, LineDistance);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        double lateral = Lateral, lift = 0;
        // The world is solid (note 279): beside the train in a tunnel it runs inside the bore, on a bridge on the deck (not in
        // the rock or the air), and out past the formation on the land (not through a cutting's wall).
        if (Lateral != 0 && train.Line.Conditions is { } land)
        {
            double room = land.LateralRoom(OnMainLine ? Rail.RailLine.MainPath : train.Dynamics.Path, LineDistance);
            lateral = Math.Clamp(Lateral, -room, room);
            if (Math.Abs(lateral) > land.FormationM)
                lift = land.Ground(t.Position + right * lateral) - t.Position.Y;
        }
        return t.Position + right * lateral + Double3.Up * (Height + lift);
    }

    /// <summary>Advances the enemy one tick. A grab's rescue and its end are the spine's, the same for every enemy (App. A.9).</summary>
    public void Step(EnemyContext ctx)
    {
        PhaseSeconds += SimConstants.TickSeconds;
        if (Phase == SpinePhase.Grab)
        {
            var victim = ctx.Crew.FirstOrDefault(c => c.Player.Id == Holding);
            if (victim.Player.State is not { Alive: true } held)
            {
                Release(ctx);
                return;
            }
            if (PullsFree && Rescuer(ctx, held) is { } freedBy)
            {
                Rescued(ctx, freedBy);
                return;
            }
            ctx.Hold(Holding);
            if (PhaseSeconds >= GrabWindow)
            {
                Enter(ctx, SpinePhase.Punish);
                Punish(ctx, Holding);
                return;
            }
        }
        Tick(ctx);
    }

    /// <summary>A crewmate (not the victim) holding Use within reach of the one held: pulling them free.</summary>
    int? Rescuer(EnemyContext ctx, in PlayerState held)
    {
        var at = PlayerMotor.WorldPosition(held, ctx.Train);
        foreach (var (p, intent) in ctx.Crew)
            if (p.Id != Holding && p.State.Alive && !p.State.Has(PlayerFlags.Held) && intent.Has(PlayerButtons.Use)
                && (PlayerMotor.WorldPosition(p.State, ctx.Train) - at).Length <= ctx.Tuning.Grab.PullReach)
                return p.Id;
        return null;
    }

    protected abstract void Tick(EnemyContext ctx);

    /// <summary>
    /// From <see cref="SpinePhase.Commit"/>, takes hold of a player (App. A.1 GRAB) for <paramref name="window"/> seconds;
    /// false if it can't (not committed, or they're already held). At a crew of one there's no friend to act: with the solo
    /// rule on (tuning <c>grab.soloStruggle</c>) the victim can struggle free themselves (T89).
    /// </summary>
    protected bool Grab(EnemyContext ctx, int victim, double window)
    {
        if (Phase != SpinePhase.Commit || ctx.Crew.All(c => c.Player.Id != victim)
            || ctx.World.ActiveEnemies.Any(e => e != this && e.Phase == SpinePhase.Grab && e.Holding == victim))
            return false;
        Holding = victim;
        GrabWindow = window;
        _struggle = 0;
        Enter(ctx, SpinePhase.Grab);
        ctx.Hold(victim);
        // App. A.9: every GRAB start writes an attribution record (and, with D.12's bookmarks, a still).
        if (ctx.World.Run is not null && ctx.Crew.FirstOrDefault(c => c.Player.Id == victim) is { Player.State: var held })
        {
            string what = $"Grabbed by the {Run.IncidentLog.Spoken(Kind.ToString())}";
            ctx.World.Attribution.Add(Run.IncidentLog.Grab(ctx.World, victim, held, what));
            ctx.World.Bookmarks.Grab(ctx.World, victim, what, CrewOf(ctx));
        }
        return true;
    }

    double _struggle;

    static IEnumerable<(int Id, PlayerState State)> CrewOf(EnemyContext ctx) => ctx.Crew.Select(c => ((int)c.Player.Id, c.Player.State));

    /// <summary>The held player's own struggle, counted by the world at a crew of one (the solo rule): Use presses.</summary>
    internal void Struggle(EnemyContext ctx, double amount)
    {
        if (Phase != SpinePhase.Grab)
            return;
        _struggle += amount;
        if (_struggle >= ctx.Tuning.Grab.SoloStruggle)
            Rescued(ctx, Holding);
    }

    /// <summary>The grab broken by a friend (or, alone, the victim): it lets go and breaks off (App. A.1). Enemies may do more.</summary>
    protected virtual void Rescued(EnemyContext ctx, int by)
    {
        Holding = -1;
        Enter(ctx, SpinePhase.BreakOff);
    }

    /// <summary>Sent away (the Choir dispersing): it breaks off and is gone.</summary>
    public void Dismiss()
    {
        Holding = -1;
        if (Phase is not (SpinePhase.Gone or SpinePhase.BreakOff))
            Phase = SpinePhase.BreakOff;
        Phase = SpinePhase.Gone;
    }

    /// <summary>The victim got away some other way (died of something else, left): it lets go.</summary>
    protected void Release(EnemyContext ctx)
    {
        Holding = -1;
        Enter(ctx, SpinePhase.BreakOff);
    }

    /// <summary>
    /// The attribution record (App. C.9) for a PUNISH that holds nobody, written the tick it begins; null for none (when
    /// another record already says it: a derailment's, a death's). The default is C.9's last row: the nearest living
    /// crewmate to it, and how far off they were.
    /// </summary>
    protected virtual Run.Incident? Punished(EnemyContext ctx)
    {
        var at = WorldPosition(ctx.Train);
        var (actor, action) = Run.IncidentLog.Nearest(ctx.World, at, CrewOf(ctx));
        return Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Punished, $"Punished by the {Run.IncidentLog.Spoken(Kind.ToString())}", actor, action, at);
    }

    /// <summary>The rescue window ran out (App. A.1 PUNISH): what it does to its victim. The default is death.</summary>
    protected virtual void Punish(EnemyContext ctx, int victim) => Kill(ctx, victim, DeathCause.Taken);

    /// <summary>
    /// Kills a player: only a victim this enemy is punishing after holding them (the fairness contract, App. A.1: "kills go
    /// through GRAB"). Anything else it does to a player is a hurt that can't take the last point of health.
    /// </summary>
    protected void Kill(EnemyContext ctx, int victim, DeathCause cause)
    {
        if (Phase != SpinePhase.Punish || victim != Holding)
            throw new InvalidOperationException($"{Kind} {Id} tried to kill {victim} without holding them first");
        ctx.Kill(victim, cause);
    }

    /// <summary>Who struck it, and when: what <see cref="Struck"/> notes first, for a creature that does the rest itself.</summary>
    protected void Marked(EnemyContext ctx, int by)
    {
        LastHitBy = by;
        LastHitTick = ctx.Tick;
        Noted(ctx, by);
    }

    /// <summary>Struck with a tool by a crew member (App. C.2). By default it's hurt, and a friend's blow breaks its grab.</summary>
    public virtual void Struck(EnemyContext ctx, int by, double damage)
    {
        LastHitBy = by;
        LastHitTick = ctx.Tick;
        if (Phase == SpinePhase.Grab && by != Holding)
        {
            Health -= damage;
            if (Health <= 0)
            {
                Holding = -1;
                Enter(ctx, SpinePhase.Gone);
                return;
            }
            Rescued(ctx, by);
            return;
        }
        Health -= damage;
        if (Health <= 0)
            Enter(ctx, SpinePhase.Gone);
    }

    // Blows by crewmates, and when (the world's tick): the coordinated kill's count (note 288). Host-only, as the spine is.
    readonly List<(int By, uint Tick)> _blows = [];

    /// <summary>Notes a crewmate's blow on it, for <see cref="Strikers"/>.</summary>
    protected void Noted(EnemyContext ctx, int by) => _blows.Add((by, ctx.Tick));

    /// <summary>
    /// How many different crewmates have struck it in the last <paramref name="window"/> seconds (not counting
    /// <paramref name="except"/>, its victim). The coordinated kill (the director's clarification of 7 Oct 2026, GDD App. F.1;
    /// note 288): it takes the crew's gang, several at once, never one player swinging.
    /// </summary>
    protected int Strikers(EnemyContext ctx, double window, int except = -1)
    {
        uint ticks = (uint)Math.Round(window * SimConstants.TickRate);
        _blows.RemoveAll(h => ctx.Tick - h.Tick > ticks);
        int n = 0;
        for (int i = 0; i < _blows.Count; i++)
        {
            int by = _blows[i].By;
            if (by == except)
                continue;
            bool seen = false;
            for (int j = 0; j < i && !seen; j++)
                seen = _blows[j].By == by;
            if (!seen)
                n++;
        }
        return n;
    }

    /// <summary>Whether the crew's gang is on it now: enemies.json <c>coordinatedKill</c>'s count of strikers in its window.</summary>
    protected bool Ganged(EnemyContext ctx, int except = -1) =>
        Strikers(ctx, ctx.Tuning.CoordinatedKill.WindowSeconds, except) >= ctx.Tuning.CoordinatedKill.Gang;

    /// <summary>
    /// Killed by the crew together (note 288): gone, and (<paramref name="forTheNight"/>) its kind for the rest of the night
    /// (<see cref="World.Slain"/>: the director doesn't send it again), with a line in the incident report naming the gang
    /// (App. C.9, D.12: a moment worth a commendation). Driven off instead, a creature can come back.
    /// </summary>
    /// <param name="forTheNight">False for one of many (a Climber of a pack, a ghost of the swarm): only this one is done.</param>
    protected void Slay(EnemyContext ctx, bool forTheNight = true)
    {
        Holding = -1;
        Enter(ctx, SpinePhase.Gone);
        if (forTheNight)
            ctx.World.Slain.Add(Kind);
        if (ctx.World.Run is null)
            return;
        uint ticks = (uint)Math.Round(ctx.Tuning.CoordinatedKill.WindowSeconds * SimConstants.TickRate);
        var gang = _blows.Where(h => ctx.Tick - h.Tick <= ticks).Select(h => h.By).Distinct().ToList();
        string names = string.Join(", ", gang.Select(id => Run.IncidentLog.NameOf(ctx.World, id)));
        ctx.World.Attribution.Add(Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Slain, $"Killed the {Run.IncidentLog.Spoken(Kind.ToString())} together",
            gang.Count > 0 ? gang[0] : -1, gang.Count > 0 ? $"By {names}." : "", WorldPosition(ctx.Train)));
    }

    /// <summary>Moves the spine. Commit is only reachable from a telegraph at least the reaction window long.</summary>
    protected bool Enter(EnemyContext ctx, SpinePhase next)
    {
        if (next == Phase)
            return true;
        if (next == SpinePhase.Commit && (Phase != SpinePhase.Telegraph || PhaseSeconds + 1e-9 < ctx.Tuning.MinReactionSeconds))
            return false;
        if (next == SpinePhase.Punish && Phase != SpinePhase.Commit && Phase != SpinePhase.Grab && Phase != SpinePhase.Punish)
            return false;
        if (next == SpinePhase.Grab && Phase != SpinePhase.Commit)
            return false;
        if (next is not (SpinePhase.Grab or SpinePhase.Punish))
            Holding = -1;
        // GDD v1.4 App. D.12: every PUNISH is an auto-bookmark, of whoever it holds (else of the thing itself).
        if (next == SpinePhase.Punish && ctx.World.Run is not null)
            ctx.World.Bookmarks.Punish(ctx.World, Id, Kind.ToString(), Holding, WorldPosition(ctx.Train), CrewOf(ctx));
        // App. A.9, C.9: every PUNISH writes an attribution record. One that holds someone is their death's (written as their
        // body goes down); one that holds nobody writes its own (note 190).
        if (next == SpinePhase.Punish && Holding < 0 && Punished(ctx) is { } record)
            ctx.World.Attribution.Add(record);
        ctx.Events.Add(new EnemyEvent(ctx.Tick, Id, Kind, Phase, next, PhaseSeconds));
        Phase = next;
        PhaseSeconds = 0;
        return true;
    }

    /// <summary>
    /// A cannonball fired by <paramref name="by"/> found its body (note 290): it lands as a heavy blow, answered by the
    /// creature's own rule for one (<see cref="Struck"/>: hurt, a friend's blow breaking its grab, a Grumbler gone feral on
    /// whoever struck it). Returns true if that finished it.
    /// </summary>
    public virtual bool Hit(EnemyContext ctx, int by, double damage)
    {
        if (!Exposed)
            return false;
        Struck(ctx, by, damage);
        return Gone;
    }

    /// <summary>Copies replicated state onto a client-side stand-in.</summary>
    public void Restore(SpinePhase phase, double phaseSeconds, double health, int attached, Double3 local, double s, double lateral, double height, double extra, double extra2,
        int holding = -1, double grabWindow = 0)
    {
        Holding = holding;
        GrabWindow = grabWindow;
        Extra2 = extra2;
        Phase = phase;
        PhaseSeconds = phaseSeconds;
        Health = health;
        Attached = attached;
        Local = local;
        LineDistance = s;
        Lateral = lateral;
        Height = height;
        Extra = extra;
    }
}
