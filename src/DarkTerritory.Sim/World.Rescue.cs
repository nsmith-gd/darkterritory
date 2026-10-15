using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim;

/// <summary>
/// Bringing them home, the host's side (GDD §17, §23.5; ARCHITECTURE §8 note 723; the friendslop audit's "nobody gets left",
/// approved by the director, 14 Oct 2026): the hand-up onto a moving car, carrying a living friend, and a bandage on someone
/// else. The shared rules are <see cref="Rescue"/>'s. Who's lending whom a hand is decided in each crewmate's act
/// (<see cref="RescueAct"/>, from where everyone stood at the tick's start, <see cref="Mates"/>), and put on them once
/// everyone's moved (<see cref="StepRescue"/>), so a carried friend goes where their carrier went this tick.
/// </summary>
public sealed partial class World
{
    enum Lend : byte { HandUp, Lift }

    enum RescueKind : byte { HandUp, Lift, SetDown, Throw, Wriggle }

    readonly record struct RescueDone(RescueKind Kind, int By, int Target);

    // Who's on whose shoulder: the carried, by their carrier (note 723). Sorted, so every pass goes in the same order.
    readonly SortedDictionary<int, int> _carriedBy = new();
    // A hand-up or a lift under way, by the one lending the hand: whom, which, and how many ticks of Use held on it.
    readonly Dictionary<int, (int Target, Lend Kind, int Ticks)> _lending = new();
    // Use held on through a rescue (its press spent on it): kept from everything else till it's let go, so a friend put down
    // isn't lifted again, nor a crate picked up, by the same hold.
    readonly HashSet<int> _useSpent = new();
    readonly HashSet<int> _useWasRescue = new(), _throwWasRescue = new(), _jumpWasRescue = new();
    readonly List<RescueDone> _rescues = new();
    // The crew thrown off a shoulder, till they're shoved by someone (the report's "Thrown off by").
    readonly HashSet<int> _thrown = new();

    static readonly RescueTuning NoRescue = new();

    /// <summary>This night's rescue numbers (player.json <c>rescue</c>, the sessions' <see cref="Movement"/>).</summary>
    public RescueTuning RescueTuning => Movement?.Rescue ?? NoRescue;

    /// <summary>Note 723: who carries <paramref name="player"/> over their shoulder, or null.</summary>
    public int? CarrierOf(int player) => _carriedBy.TryGetValue(player, out int by) ? by : null;

    /// <summary>Note 723: whom <paramref name="carrier"/> carries, or null.</summary>
    public int? CarriedBy(int carrier)
    {
        foreach (var (carried, by) in _carriedBy)
            if (by == carrier)
                return carried;
        return null;
    }

    /// <summary>Note 723: whether <paramref name="player"/>'s last time off their feet was a throw off a friend's shoulder.</summary>
    public bool WasThrown(int player) => _thrown.Contains(player);

    /// <summary>
    /// Note 723, host, in a crewmate's act: what Use, Throw and Jump do for the rescues before anything else sees them. Carried,
    /// Jump wriggles free. Carrying, Use puts the friend down and Throw throws them (only with the crew mischief switch on;
    /// otherwise it puts them down too); neither reaches anything else. Lending a hand, Use held looking at a runner beside the
    /// moving car hauls them up (rescue.handUp.seconds), and a press on a friend in front lifts them (carry.liftSeconds): while
    /// it does, Use is theirs alone (a door the helper stands in isn't worked). Returns the hurt friend a healing find in the
    /// hands goes on, for the hands (−1 for nobody).
    /// </summary>
    int RescueAct(in PlayerState s, ref PlayerIntent intent, int playerId)
    {
        bool use = intent.Has(PlayerButtons.Use), thrown = intent.Has(PlayerButtons.Throw), jump = intent.Has(PlayerButtons.Jump);
        bool usePressed = use && !_useWasRescue.Contains(playerId);
        bool throwPressed = thrown && !_throwWasRescue.Contains(playerId);
        bool jumpPressed = jump && !_jumpWasRescue.Contains(playerId);
        Track(_useWasRescue, playerId, use);
        Track(_throwWasRescue, playerId, thrown);
        Track(_jumpWasRescue, playerId, jump);
        if (!use)
            _useSpent.Remove(playerId);
        var t = RescueTuning;
        int patient = -1;
        if (!s.Alive || Movement is not { } p)
        {
            _lending.Remove(playerId);
            return patient;
        }
        // Over a friend's shoulder: Jump wriggles free; the rest of the keys are stripped already (Rescue.CarriedIntent).
        if (_carriedBy.ContainsKey(playerId))
        {
            _lending.Remove(playerId);
            if (jumpPressed)
                _rescues.Add(new RescueDone(RescueKind.Wriggle, playerId, playerId));
            return patient;
        }
        // Carrying one: Use puts them down, Throw throws them (mischief on), and nothing else in reach gets either.
        if (CarriedBy(playerId) is { } friend)
        {
            _lending.Remove(playerId);
            if (usePressed)
            {
                _rescues.Add(new RescueDone(RescueKind.SetDown, playerId, friend));
                _useSpent.Add(playerId);
            }
            else if (throwPressed)
                _rescues.Add(new RescueDone(Mischief(playerId) ? RescueKind.Throw : RescueKind.SetDown, playerId, friend));
            intent.Buttons &= ~(PlayerButtons.Use | PlayerButtons.Throw);
            intent.Actions &= ~(PlayerActions.Swing | PlayerActions.Seat | PlayerActions.Ladder);
            return patient;
        }
        if (_useSpent.Contains(playerId))
        {
            intent.Buttons &= ~PlayerButtons.Use;
            return patient;
        }
        var carried = Bodies.CarriedBy(playerId);
        // A healing find in the hands goes on a hurt friend looked at (the hands use it: Bodies.Dose). Never a bot's.
        if (carried is { Kind: Physics.BodyKind.Loot } && !BotIds.Contains(playerId) && Bodies.Heals?.Invoke(carried) > 0)
        {
            _lending.Remove(playerId);
            return Rescue.Patient(s, playerId, Mates, Train, t, p, Bodies.FullHealth) is { } hurt ? hurt.Id : -1;
        }
        // Lending a hand takes both empty hands (a tool in the hotbar is no matter).
        (int Id, PlayerState State)? target = null;
        Lend kind = Lend.HandUp;
        if (use && carried is null)
        {
            // The hand-up first: a runner beside the moving car, whatever else is in reach (the helper stands in a door).
            if (Rescue.HandUpTarget(s, playerId, Mates, Train, t) is { } runner)
                target = runner;
            // A friend in front, lifted: begun on a press, with nothing Use works in reach (a door, a lever, the fire). Never by a bot.
            else if (!BotIds.Contains(playerId) && (usePressed || _lending.TryGetValue(playerId, out var was) && was.Kind == Lend.Lift)
                && Sim.Train.CrewActions.NearestInteractable(s, Train, Hand) is null && Switches?.InReach(s, Train, Hand) is null
                && Rescue.LiftTarget(s, playerId, Mates, Train, t, p) is { } lifted)
                (target, kind) = (lifted, Lend.Lift);
        }
        if (target is not { } on)
        {
            // A lift broken off with Use still held (the friend gone out of reach): the hold stays spent, so it doesn't fall
            // through to a crate or a door as a fresh press. (A hand-up's runner can come back into reach and be hauled on.)
            if (_lending.Remove(playerId, out var broken) && use && broken.Kind == Lend.Lift)
            {
                _useSpent.Add(playerId);
                intent.Buttons &= ~PlayerButtons.Use;
            }
            return patient;
        }
        int ticks = _lending.TryGetValue(playerId, out var lending) && lending.Target == on.Id && lending.Kind == kind ? lending.Ticks + 1 : 1;
        intent.Buttons &= ~PlayerButtons.Use;
        double seconds = kind == Lend.HandUp ? t.HandUp.Seconds : t.Carry.LiftSeconds;
        if (ticks * SimConstants.TickSeconds >= seconds - SimConstants.TickSeconds / 2)
        {
            _lending.Remove(playerId);
            _useSpent.Add(playerId);
            _rescues.Add(new RescueDone(kind == Lend.HandUp ? RescueKind.HandUp : RescueKind.Lift, playerId, on.Id));
        }
        else
            _lending[playerId] = (on.Id, kind, ticks);
        return patient;
    }

    static void Track(HashSet<int> set, int id, bool held)
    {
        if (held)
            set.Add(id);
        else
            set.Remove(id);
    }

    /// <summary>Note 723: whether <paramref name="playerId"/> is hauling someone up this moment (a hand-up under way), the HUD's and the art's.</summary>
    public bool HaulingUp(int playerId) => _lending.TryGetValue(playerId, out var l) && l.Kind == Lend.HandUp;

    /// <summary>
    /// Note 723, host, once everyone has moved this tick (after the motor, before the bodies): this tick's hand-ups, lifts,
    /// set-downs, throws and wriggles are put on whom they're for, then every carried friend goes where their carrier went (or
    /// comes down at their feet, if the carrier's dead, held, knocked off their feet or at something that takes their hands:
    /// "a carried player is safe from nothing").
    /// </summary>
    public void StepRescue(Func<int, PlayerState?> get, Action<int, PlayerState> set)
    {
        var t = RescueTuning;
        foreach (var r in _rescues)
        {
            if (get(r.By) is not { } by || get(r.Target) is not { } them)
                continue;
            switch (r.Kind)
            {
                case RescueKind.HandUp:
                    if (!them.Alive || them.Parent != PlayerState.World || them.Has(PlayerFlags.Held) || _carriedBy.ContainsKey(r.Target)
                        || !by.Alive || by.Parent < 0 || by.Parent >= Train.Frames.Count || Movement is not { } p)
                        break;
                    double speed = Math.Abs(Train.RakeOf(by.Parent).Speed);
                    Rescue.HaulAboard(ref them, by, Train, p, t);
                    set(r.Target, them);
                    if (speed >= t.HandUp.ReportFrom)
                        Attribution.Add(new Sim.Run.Incident(Sim.Run.IncidentKind.Rescue, Run?.Seconds ?? Tick * SimConstants.TickSeconds, r.Target, "Hauled aboard",
                            Sim.Run.IncidentLog.Where(this, them), r.By, $"Hauled up by {{actor}} at {speed * 3.6:0} km/h."));
                    break;
                case RescueKind.Lift:
                    if (!them.Alive || !Horseplay.Solid(them) || them.Has(PlayerFlags.Carrying) || _carriedBy.ContainsKey(r.Target) || _carriedBy.ContainsValue(r.Target)
                        || !Rescue.CanHelp(by) || _carriedBy.ContainsKey(r.By) || _carriedBy.ContainsValue(r.By))
                        break;
                    _carriedBy[r.Target] = r.By;
                    // Note 722: a friend sat on an edge (or leant out, or holding a hand up) is lifted off it: the emote ends.
                    Emotes.RemoveAll(e => e.By == r.Target);
                    set(r.By, by with { Flags = by.Flags | PlayerFlags.Carrying | PlayerFlags.Heavy });
                    Rescue.Follow(ref them, by, Train, t);
                    them.Placed++;
                    set(r.Target, them);
                    break;
                case RescueKind.SetDown:
                case RescueKind.Wriggle:
                    if (_carriedBy.TryGetValue(r.Target, out int carrier) && get(carrier) is { } holder)
                        Drop(r.Target, carrier, them, holder, set);
                    break;
                case RescueKind.Throw:
                    if (!_carriedBy.ContainsKey(r.Target))
                        break;
                    _carriedBy.Remove(r.Target);
                    set(r.By, by with { Flags = by.Flags & ~(PlayerFlags.Carrying | PlayerFlags.Heavy) });
                    Rescue.Throw(ref them, by, Train, t);
                    set(r.Target, them);
                    _shovedBy[r.Target] = (r.By, SpeedUnder(by));
                    _thrown.Add(r.Target);
                    Horseplays.Add(new HorseplayEvent(_nextFx, Tick, r.By, KnockKind.Throw, r.Target));
                    _nextFx = _nextFx % 0xFFFFFF + 1;
                    break;
            }
        }
        _rescues.Clear();
        foreach (var (carried, carrier) in _carriedBy.ToList())
        {
            if (get(carried) is not { } them)
            {
                _carriedBy.Remove(carried);
                if (get(carrier) is { } left)
                    set(carrier, left with { Flags = left.Flags & ~(PlayerFlags.Carrying | PlayerFlags.Heavy) });
                continue;
            }
            if (get(carrier) is not { } by || !by.Alive || by.Has(PlayerFlags.Held) || by.Stagger > 0 || by.Surface == Surface.Ladder
                || by.Has(PlayerFlags.Seated) || by.Has(PlayerFlags.Operating) || !them.Alive || them.Has(PlayerFlags.Held))
            {
                Drop(carried, carrier, them, get(carrier), set);
                continue;
            }
            Rescue.Follow(ref them, by, Train, t);
            set(carried, them);
            if (!by.Has(PlayerFlags.Carrying) || !by.Has(PlayerFlags.Heavy))
                set(carrier, by with { Flags = by.Flags | PlayerFlags.Carrying | PlayerFlags.Heavy });
        }
    }

    /// <summary>
    /// A carried friend comes down (note 723): set at the carrier's feet (or, held by something, left where it holds them, or
    /// dead, left lying where they were), and the carrier's hands are free.
    /// </summary>
    void Drop(int carried, int carrier, PlayerState them, PlayerState? holder, Action<int, PlayerState> set)
    {
        _carriedBy.Remove(carried);
        if (holder is { } by)
        {
            var free = by with { Flags = by.Flags & ~(PlayerFlags.Carrying | PlayerFlags.Heavy) };
            set(carrier, free);
            if (them.Alive && !them.Has(PlayerFlags.Held))
            {
                Rescue.SetDown(ref them, free, Train);
                set(carried, them);
                return;
            }
        }
        if (them.Has(PlayerFlags.Carried))
            set(carried, them with { Flags = them.Flags & ~PlayerFlags.Carried, Placed = (byte)(them.Placed + 1) });
    }
}
