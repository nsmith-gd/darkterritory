using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim;

/// <summary>
/// Hoisting a friend on the crane's hook, the host's side (GDD §18; ARCHITECTURE §8 note 726; the friendslop audit's griefers,
/// approved by the director, 14 Oct 2026: "The griefer ideas are funny"). A crewmate takes hold of an empty hook in reach
/// (Use held, crane.hoist.grabSeconds) and stands on it; the operator lifts, runs and lowers them as a load; Jump lets go. The
/// drop is a gag (note 802; the director, 15 Oct 2026: "It's a silly gag not a big grief"): the landing knocks by its height,
/// at most player.json landing.offTheHook.maxKnock, and never kills (<see cref="PlayerFlags.OffTheHook"/>, in the motor, so a
/// predicting client lands it alike). Being hoisted is the rider's own choice, so the grab isn't gated; what is mischief is the
/// operator shaking them off (Fire with nothing hooked), only with the CREW MISCHIEF switch on. A let-go after a lift that
/// hurts is the operator's in the report ("Dropped from the crane by Dave, 6 m up."). Who takes hold is
/// decided in each crewmate's act (<see cref="HoistAct"/>), and put on them once everyone's moved (<see cref="StepHoist"/>).
/// </summary>
public sealed partial class World
{
    enum HoistKind : byte { Grab, LetGo }

    readonly record struct HoistDone(HoistKind Kind, int Player, Crane? Crane);

    /// <summary>A crewmate on a hook: which crane's, who last lifted them (−1 for nobody), and where the hook was last tick.</summary>
    sealed class Ride(Crane crane)
    {
        public readonly Crane Crane = crane;
        public int LiftedBy = -1;
        public double LastHook = crane.Hook;
        public Double3 LastAt = crane.HookAt;
        public Double3 Velocity;
    }

    /// <summary>
    /// A let-go after a lift, till they've landed: who lifted them (or shook them off), how high their feet were over what's
    /// under the hook, their health then, and whether it was a shake.
    /// </summary>
    readonly record struct Fall(int By, double Height, int Health, bool Shaken);

    // Who rides which hook, by rider (sorted, so every pass goes in the same order).
    readonly SortedDictionary<int, Ride> _riding = new();
    // Use held at a hook: on which crane, and for how many ticks.
    readonly Dictionary<int, (Crane Crane, int Ticks)> _grabbing = new();
    readonly HashSet<int> _jumpWasHoist = new();
    readonly List<HoistDone> _hoists = new();
    readonly SortedDictionary<int, Fall> _falling = new();

    /// <summary>Note 726: the crane whose hook <paramref name="player"/> rides, or null.</summary>
    public Crane? HoistedOn(int player) => _riding.TryGetValue(player, out var r) ? r.Crane : null;

    /// <summary>Note 726: who lifted <paramref name="player"/> before they let go of the hook (or shook them off), till they land; null for nobody.</summary>
    public int? DroppedBy(int player) => _falling.TryGetValue(player, out var f) && f.By >= 0 ? f.By : null;

    /// <summary>Note 726: whether <paramref name="player"/> came off the hook shaken off by the operator, not by letting go.</summary>
    public bool WasShaken(int player) => _falling.TryGetValue(player, out var f) && f.Shaken;

    /// <summary>Note 726: how high <paramref name="player"/>'s feet were when they came off the hook (m over what was under it).</summary>
    public double DropHeight(int player) => _falling.TryGetValue(player, out var f) ? f.Height : 0;

    /// <summary>
    /// Note 726: whether <paramref name="s"/> can take hold of a hook: alive and steady on their feet, both hands empty, and not
    /// on a ladder, in a gun's seat, at a crane's controls or the handcar's handle, held by something, carried or carrying.
    /// </summary>
    public static bool CanTakeTheHook(in PlayerState s) =>
        s.Alive && s.Stagger == 0 && s.Grounded && !s.Has(PlayerFlags.Held) && !s.Has(PlayerFlags.Seated) && !s.Has(PlayerFlags.Operating)
        && !s.Has(PlayerFlags.Pumping) && !s.Has(PlayerFlags.Carried) && !s.Has(PlayerFlags.Carrying) && !s.Has(PlayerFlags.Hoisted)
        && !s.Has(PlayerFlags.Heavy) && !s.Has(PlayerFlags.Shovelful);

    /// <summary>
    /// Note 726: the crane whose hook <paramref name="s"/> could take hold of now (the HUD's HOLD ON prompt asks it too), or
    /// null: at a stop's crane, the hook empty and in reach (<see cref="Crane.Grabbable"/>), with no casting there to rig (that
    /// comes first) and not at its stand. Never a bot's; never with a find, a crate or a tool in both hands.
    /// </summary>
    public Crane? HookInReach(in PlayerState s, int playerId)
    {
        if (Run is not { Over: false, CurrentSite: { } site } || !CanTakeTheHook(s) || BotIds.Contains(playerId) || Bodies.CarriedBy(playerId) is not null)
            return null;
        var feet = PlayerMotor.WorldPosition(s, Train);
        foreach (var crane in site.Cranes)
            if (crane.Grabbable(feet) && crane.Riggable(feet) is null && !crane.AtStand(s, Train))
                return crane;
        return null;
    }

    /// <summary>
    /// Note 726, host, in a crewmate's act (before the rescues and the hands see Use): on a hook, Jump lets go (the rest of the
    /// keys are stripped already, <see cref="Rescue.CarriedIntent"/>); at an empty hook in reach, Use held for
    /// crane.hoist.grabSeconds takes hold of it, and while it's held nothing else in reach gets it.
    /// </summary>
    void HoistAct(in PlayerState s, ref PlayerIntent intent, int playerId)
    {
        bool jump = intent.Has(PlayerButtons.Jump);
        bool jumpPressed = jump && !_jumpWasHoist.Contains(playerId);
        Track(_jumpWasHoist, playerId, jump);
        if (_riding.ContainsKey(playerId))
        {
            _grabbing.Remove(playerId);
            if (jumpPressed)
                _hoists.Add(new HoistDone(HoistKind.LetGo, playerId, null));
            return;
        }
        if (!intent.Has(PlayerButtons.Use) || HookInReach(s, playerId) is not { } crane)
        {
            _grabbing.Remove(playerId);
            return;
        }
        intent.Buttons &= ~PlayerButtons.Use;
        int ticks = _grabbing.TryGetValue(playerId, out var g) && g.Crane == crane ? g.Ticks + 1 : 1;
        if (ticks * SimConstants.TickSeconds >= crane.Tuning.Hoist.GrabSeconds - SimConstants.TickSeconds / 2)
        {
            _grabbing.Remove(playerId);
            _hoists.Add(new HoistDone(HoistKind.Grab, playerId, crane));
        }
        else
            _grabbing[playerId] = (crane, ticks);
    }

    /// <summary>Note 726: how far <paramref name="player"/> is through taking hold of a hook (0 to 1), the HUD's on the host.</summary>
    public double HoistGrab(int player) =>
        _grabbing.TryGetValue(player, out var g) ? Math.Min(1, g.Ticks * SimConstants.TickSeconds / Math.Max(1e-6, g.Crane.Tuning.Hoist.GrabSeconds)) : 0;

    /// <summary>
    /// Note 726, host, once everyone has moved this tick (after the rescues, before the bodies): this tick's grabs and let-goes,
    /// the operator's shake (mischief on), then every rider stood on their hook where it is, moving as it moves, in the world's
    /// frame (the gantry stands in it; a rider who took hold from a car's roof leaves the car's). Anyone who's let go and since
    /// landed is done with, and one the landing hurt after a lift has a line in the report. The landing never kills (note 802),
    /// so <see cref="DeathCause.Dropped"/> isn't set any more.
    /// </summary>
    public void StepHoist(Func<int, PlayerState?> get, Action<int, PlayerState> set)
    {
        var site = Run?.CurrentSite;
        foreach (var h in _hoists)
        {
            if (get(h.Player) is not { } s)
                continue;
            switch (h.Kind)
            {
                case HoistKind.Grab:
                    if (h.Crane is not { } crane || site is null || !site.Cranes.Contains(crane) || _riding.ContainsKey(h.Player)
                        || _carriedBy.ContainsKey(h.Player) || CarriedBy(h.Player) is not null || !CanTakeTheHook(s)
                        || !crane.Grabbable(PlayerMotor.WorldPosition(s, Train)))
                        break;
                    _riding[h.Player] = new Ride(crane);
                    crane.Riders = Riders(crane);
                    _falling.Remove(h.Player);
                    if (s.Parent != PlayerState.World)
                        PlayerMotor.Reparent(ref s, Train, PlayerState.World);
                    s.Placed++;
                    set(h.Player, s);
                    break;
                case HoistKind.LetGo:
                    if (_riding.Remove(h.Player, out var ride))
                    {
                        LetGo(ref s, h.Player, ride, ride.LiftedBy, shaken: false);
                        set(h.Player, s);
                    }
                    break;
            }
        }
        _hoists.Clear();
        // The operator's Fire with nothing hooked (Run.Operate): with the crew mischief switch on, everyone on the hook is shaken off.
        foreach (var crane in site?.Cranes ?? [])
        {
            if (crane.ShakeOffBy is >= 0 and var by && crane.Tuning.Hoist.ShakeOff && Mischief(by))
                foreach (var (id, ride) in _riding.Where(r => r.Value.Crane == crane).ToList())
                    if (get(id) is { } s)
                    {
                        _riding.Remove(id);
                        LetGo(ref s, id, ride, by, shaken: true);
                        set(id, s);
                    }
            crane.ShakeOffBy = -1;
        }
        foreach (var (id, ride) in _riding.ToList())
        {
            var crane = ride.Crane;
            if (get(id) is not { } s)
            {
                _riding.Remove(id);
                continue;
            }
            // Dead, grabbed by something, or the crane gone with the stop or brought down: they come off it as they are.
            if (!s.Alive || s.Has(PlayerFlags.Held) || site is null || !site.Cranes.Contains(crane) || crane.Wrecked || Run?.Over != false)
            {
                _riding.Remove(id);
                LetGo(ref s, id, ride, ride.LiftedBy, shaken: false);
                set(id, s);
                continue;
            }
            // Lifted since last tick (the crane moves in the run's step, after this): whoever drove it then has them.
            if (crane.Hook > ride.LastHook + 1e-6 && crane.LastDriver >= 0)
                ride.LiftedBy = crane.LastDriver;
            var hook = crane.HookAt;
            ride.Velocity = (hook - ride.LastAt) * (1 / SimConstants.TickSeconds);
            ride.LastHook = crane.Hook;
            ride.LastAt = hook;
            var on = _riding.Where(r => r.Value.Crane == crane).Select(r => r.Key).ToList();
            if (s.Parent != PlayerState.World)
                PlayerMotor.Reparent(ref s, Train, PlayerState.World);
            s.Position = crane.RiderFeet(on.IndexOf(id), on.Count);
            s.Velocity = ride.Velocity;
            s.Surface = Surface.Air;
            s.Flags = (s.Flags | PlayerFlags.Hoisted) & ~(PlayerFlags.Crouched | PlayerFlags.OffTheGround | PlayerFlags.Shoved | PlayerFlags.OffTheHook);
            set(id, s);
        }
        foreach (var crane in site?.Cranes ?? [])
            crane.Riders = Riders(crane);
        // Off the hook and landed: a knock is the lifter's line in the report. The landing never kills (note 802: the motor's
        // gag, PlayerFlags.OffTheHook); one dead of something else on the way down is done with.
        foreach (var (id, fall) in _falling.ToList())
        {
            if (get(id) is not { } s || !s.Alive)
            {
                _falling.Remove(id);
                continue;
            }
            if (s.Surface != Surface.Air || s.Has(PlayerFlags.Hoisted))
            {
                if (s.Health < fall.Health && fall.By >= 0)
                    Attribution.Add(new Incident(IncidentKind.Bonked, Run?.Seconds ?? Tick * SimConstants.TickSeconds, id, "Dropped from the crane",
                        IncidentLog.Where(this, s), fall.By, DropLine(fall.Shaken, fall.Height)));
                _falling.Remove(id);
            }
        }
    }

    /// <summary>The report's contributing action for a drop off the hook (note 726): who, and from how high.</summary>
    internal static string DropLine(bool shaken, double height) =>
        $"{(shaken ? "Shaken off the hook" : "Dropped from the crane")} by {{actor}}, {height:0} m up.";

    int Riders(Crane crane)
    {
        int n = 0;
        foreach (var r in _riding.Values)
            if (r.Crane == crane)
                n++;
        return n;
    }

    /// <summary>
    /// A rider comes off the hook (note 726): in the air where they stood, on the hook's own motion, to fall. The landing is the
    /// gag's (note 802, <see cref="PlayerFlags.OffTheHook"/>: by its height, at most landing.offTheHook.maxKnock, never a death),
    /// lift or no lift. After a lift (or a shake), whose it was, and how high, till they land. A host move
    /// (<see cref="PlayerState.Placed"/>), so a client takes the flag with the rest.
    /// </summary>
    void LetGo(ref PlayerState s, int id, Ride ride, int by, bool shaken)
    {
        bool hoisted = s.Has(PlayerFlags.Hoisted);
        s.Flags &= ~PlayerFlags.Hoisted;
        ride.Crane.Riders = Riders(ride.Crane);
        if (!s.Alive || s.Has(PlayerFlags.Held) || !hoisted)
            return;
        s.Flags |= PlayerFlags.OffTheHook;
        s.Surface = Surface.Air;
        s.Velocity = ride.Velocity;
        s.Placed++;
        double height = PlayerMotor.WorldPosition(s, Train).Y - ride.Crane.Under(Train).Y;
        _falling[id] = new Fall(by, height, s.Health, shaken);
    }
}
