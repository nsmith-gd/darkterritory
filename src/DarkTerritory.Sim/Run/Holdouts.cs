using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/holdouts.json. Field docs live in that file.</summary>
public sealed record HoldoutTuning
{
    public const string File = "tuning/holdouts.json";

    public required double AssignFacility { get; init; }
    public required double AssignHalt { get; init; }
    public required int SecondCrew { get; init; }
    public required double Release { get; init; }
    public required double CallOutRadius { get; init; }
    public required double CallOutRange { get; init; }
    public required double CallOutCooldown { get; init; }
    public required BreachTuning Smash { get; init; }
    public required BreachTuning Pry { get; init; }
    public required double BreachReach { get; init; }
    public required int FreedHealth { get; init; }
    public required double CrewLossFee { get; init; }
    public required double BodyRefund { get; init; }
}

public sealed record BreachTuning(double Seconds, double Rounds);

/// <summary>A Holdout's state (GDD App. D.5).</summary>
public enum HoldoutState : byte { Dormant, Occupied, Breaching, Freed }

public enum HoldoutEventKind : byte { Assigned, Released, Freed, CalledOut }

/// <summary>What a Holdout did this tick, for the host to act on (a shout to play) and for the harness to count.</summary>
public readonly record struct HoldoutEvent(HoldoutEventKind Kind, int Holdout, int PlayerId);

/// <summary>One Holdout in the world: where it stands, and (host-authoritative, mirrored) who's in it.</summary>
public sealed class Holdout
{
    public required int Index { get; init; }
    public required RouteFeature Site { get; init; }
    public required StopHoldout Layout { get; init; }
    /// <summary>Inside it (where its occupant comes back), its door (where the crew breaches it), its lamp.</summary>
    public required Double3 Inside { get; init; }
    public required Double3 Door { get; init; }
    public required Double3 Lamp { get; init; }
    public required double LineHint { get; init; }

    public HoldoutState State { get; internal set; }
    /// <summary>Who's waiting in it (−1 for nobody).</summary>
    public int Occupant { get; internal set; } = -1;
    /// <summary>Seconds of breach done, of <see cref="BreachSeconds"/>.</summary>
    public double Progress { get; internal set; }
    /// <summary>
    /// Its occupant's Live Mic is on (D.7): their voice plays from the door to the living near it, on proximity voice. It
    /// defaults off, and goes off when they're freed or released. It never counts toward loudness: only the living are heard.
    /// </summary>
    public bool LiveMic { get; internal set; }
    internal int Breacher = -1;
    internal double CallCooldown;

    public bool Lit => State is HoldoutState.Occupied or HoldoutState.Breaching;
    /// <summary>A lock is smashed (a prison car, a halt's lockup); a shelter's barricade is pried (D.7).</summary>
    public BreachTuning Breach(HoldoutTuning t) => Layout.Kind == HoldoutKind.Shelter ? t.Pry : t.Smash;
}

/// <summary>
/// GDD App. D: death, Holdouts and return. Once the gate has opened a Holdout is the only way back: the dead (and anyone
/// who joined mid-run) wait in one shared queue; as the consist comes in to a site its Holdouts light and take the
/// first eligible players; a crew member breaches the door and they come back inside it; a site the train leaves
/// behind releases them to their place in the queue. Host-authoritative; clients mirror the Holdouts for the lamps and
/// the HUD. Deterministic: lists in order, lookups only.
/// </summary>
public sealed class Holdouts
{
    readonly List<Holdout> _holdouts = [];
    readonly List<QueueEntry> _queue = [];
    // Each player's buttons last tick (a dead player's presses are edges), and who's holding a breach where.
    readonly Dictionary<int, PlayerButtons> _last = new();
    readonly Dictionary<int, PlayerActions> _lastActions = new();
    readonly Dictionary<int, int> _holding = new();
    readonly Dictionary<int, int> _health = new();
    readonly List<(int Player, int Holdout)> _callOuts = [];

    /// <summary>A place in the queue (D.6): who, whether they joined mid-run (lobbied), and where they last died.</summary>
    public readonly record struct QueueEntry(int PlayerId, bool Lobbied, double DiedAt);

    public HoldoutTuning Tuning { get; set; }
    public IReadOnlyList<Holdout> All => _holdouts;
    /// <summary>The respawn queue, front first (D.6). Host only.</summary>
    public IReadOnlyList<QueueEntry> Queue => _queue;
    /// <summary>Players freed this run, in order (host only).</summary>
    public List<int> Freed { get; } = [];

    public Holdouts(HoldoutTuning t, Route.Route route, RailLine line)
    {
        Tuning = t;
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop)
                continue;
            foreach (var h in stop.Holdouts)
            {
                var b = stop.Buildings[h.Building];
                _holdouts.Add(new Holdout
                {
                    Index = _holdouts.Count,
                    Site = f,
                    Layout = h,
                    Inside = Run.StopWorld(line, f, b.Centre),
                    Door = Run.StopWorld(line, f, h.Door),
                    Lamp = Run.StopWorld(line, f, h.Lamp, 3),
                    LineHint = f.Start + b.S,
                });
            }
        }
    }

    /// <summary>A player's hands this tick (host): holding Use at an occupied Holdout's door is breaching it; a dead player's Use calls out, Throw defers.</summary>
    public void CrewAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train)
    {
        var last = _last.GetValueOrDefault(playerId);
        var now = intent.Buttons;
        _last[playerId] = now;
        bool pressed(PlayerButtons b) => (now & b) != 0 && (last & b) == 0;
        var lastActions = _lastActions.GetValueOrDefault(playerId);
        _lastActions[playerId] = intent.Actions;
        if (!s.Alive)
        {
            _holding.Remove(playerId);
            // D.7 Live Mic: a toggle offered only to the one waiting in it.
            if ((intent.Actions & PlayerActions.LiveMic) != 0 && (lastActions & PlayerActions.LiveMic) == 0)
                foreach (var h in _holdouts)
                    if (h.Lit && h.Occupant == playerId)
                        h.LiveMic = !h.LiveMic;
            // D.7 Call Out, from whichever Holdout they could be answered at (the one they're in, or any lit one).
            if (pressed(PlayerButtons.Use))
                foreach (var h in _holdouts)
                    if (h.Lit)
                        _callOuts.Add((playerId, h.Index));
            if (pressed(PlayerButtons.Throw))
                Defer(playerId);
            return;
        }
        var at = PlayerMotor.WorldPosition(s, train);
        _holding.Remove(playerId);
        if (!intent.Has(PlayerButtons.Use))
            return;
        foreach (var h in _holdouts)
            if (h.Lit && ((h.Door - at) with { Y = 0 }).Length <= Tuning.BreachReach)
            {
                _holding[playerId] = h.Index;
                return;
            }
    }

    /// <summary>D.6 defer: one place further back, never forward.</summary>
    public void Defer(int playerId)
    {
        int i = _queue.FindIndex(e => e.PlayerId == playerId);
        if (i < 0 || i == _queue.Count - 1)
            return;
        (_queue[i], _queue[i + 1]) = (_queue[i + 1], _queue[i]);
        // An occupant who defers before the breach starts gives up the Holdout (D.5 "reassign while occupied").
        foreach (var h in _holdouts)
            if (h.State == HoldoutState.Occupied && h.Occupant == playerId)
                Release(h);
    }

    /// <summary>
    /// Host, after the world and bodies have stepped: keeps the queue (the newly dead at the back, the gone taken off),
    /// then each Holdout: assign as the consist comes in, breach, free, release; and the dead's shouts.
    /// </summary>
    /// <param name="crew">Everyone in the session, living, dead and lobbied, in order.</param>
    public List<HoldoutEvent> Step(World world, IReadOnlyList<(int Id, PlayerState State)> crew, Action<int, PlayerState> set, double dt)
    {
        var events = new List<HoldoutEvent>();
        var train = world.Train;
        var ids = crew.Select(c => c.Id).ToHashSet();
        // Disconnected: off the queue, and out of any Holdout they were in (D.6).
        foreach (var gone in _queue.Where(e => !ids.Contains(e.PlayerId)).ToList())
        {
            _queue.Remove(gone);
            foreach (var h in _holdouts.Where(h => h.Occupant == gone.PlayerId && h.State != HoldoutState.Freed))
                Release(h);
        }
        foreach (var (id, s) in crew)
        {
            bool queued = _queue.Any(e => e.PlayerId == id);
            if (!s.Alive && !queued && !_holdouts.Any(h => h.Occupant == id && h.State != HoldoutState.Freed))
            {
                double hint = s.LineHint;
                train.Line.Nearest(PlayerMotor.WorldPosition(s, train), ref hint);
                _queue.Add(new QueueEntry(id, s.Death == DeathCause.Waiting, s.Death == DeathCause.Waiting ? double.NaN : hint));
            }
            // A breacher who takes damage is interrupted (D.5).
            int health = s.Health;
            if (_health.TryGetValue(id, out int was) && health < was)
                _holding.Remove(id);
            _health[id] = health;
        }

        var engine = train.Rakes.First(r => r.Consist.HasEngine);
        int session = crew.Count;
        foreach (var h in _holdouts)
        {
            if (h.State == HoldoutState.Freed)
                continue;
            var f = h.Site;
            double approach = f.Kind == FeatureKind.Facility ? Tuning.AssignFacility : Tuning.AssignHalt;
            bool coming = engine.Distance >= f.Start - approach && engine.Distance <= f.End;
            h.CallCooldown = Math.Max(0, h.CallCooldown - dt);

            if (h.State == HoldoutState.Dormant)
            {
                // D.4: a second Holdout sleeps below the crew threshold.
                if (!coming || h.Layout.Second && session < Tuning.SecondCrew)
                    continue;
                if (FirstEligible(h) is { } entry)
                {
                    h.State = HoldoutState.Occupied;
                    h.Occupant = entry.PlayerId;
                    events.Add(new HoldoutEvent(HoldoutEventKind.Assigned, h.Index, entry.PlayerId));
                }
                continue;
            }

            // Breaching: someone holding Use at the door. Anything else interrupts it, back to nothing (D.5).
            int breacher = _holding.Where(x => x.Value == h.Index).Select(x => x.Key).DefaultIfEmpty(-1).Min();
            if (breacher >= 0 && (h.Breacher < 0 || h.Breacher == breacher))
            {
                var b = h.Breach(Tuning);
                h.State = HoldoutState.Breaching;
                h.Breacher = breacher;
                h.Progress += dt;
                // Its noise is the living's: it counts toward loudness (D.7). Call Out never does.
                if (world.Combat is { } c)
                    world.Choir.Loud(c.Choir, b.Rounds, dt);
                if (h.Progress >= b.Seconds)
                {
                    Free(h, crew, set);
                    events.Add(new HoldoutEvent(HoldoutEventKind.Freed, h.Index, h.Occupant));
                }
                continue;
            }
            if (h.State == HoldoutState.Breaching)
            {
                h.State = HoldoutState.Occupied;
                h.Progress = 0;
                h.Breacher = -1;
            }

            // D.5 release: the consist has left the zone moving away, and nobody living is near.
            bool leaving = engine.Distance > f.End && engine.Speed > 0;
            bool near = crew.Any(c => c.State.Alive && (PlayerMotor.WorldPosition(c.State, train) - h.Inside).Length <= Tuning.Release);
            if (leaving && !near)
            {
                int who = h.Occupant;
                Release(h);
                events.Add(new HoldoutEvent(HoldoutEventKind.Released, h.Index, who));
            }
        }

        // D.7 Call Out: one shout per Holdout per cooldown, while someone living is within reach of it.
        foreach (var (player, index) in _callOuts)
        {
            var h = _holdouts[index];
            if (!h.Lit || h.CallCooldown > 0
                || !crew.Any(c => c.State.Alive && (PlayerMotor.WorldPosition(c.State, train) - h.Inside).Length <= Tuning.CallOutRadius))
                continue;
            h.CallCooldown = Tuning.CallOutCooldown;
            events.Add(new HoldoutEvent(HoldoutEventKind.CalledOut, h.Index, player));
        }
        _callOuts.Clear();
        return events;
    }

    /// <summary>
    /// The first eligible entry (D.5, D.6): nobody already waiting in another Holdout, and nobody whose last death was in
    /// this site's zone (they keep their place, skipped).
    /// </summary>
    QueueEntry? FirstEligible(Holdout h)
    {
        foreach (var e in _queue)
        {
            if (_holdouts.Any(o => o.Occupant == e.PlayerId && o.State is HoldoutState.Occupied or HoldoutState.Breaching))
                continue;
            if (!double.IsNaN(e.DiedAt) && h.Site.Contains(e.DiedAt))
                continue;
            return e;
        }
        return null;
    }

    /// <summary>The lit Holdout whose Live Mic is <paramref name="playerId"/>'s, on (D.7); null if they have none.</summary>
    public Holdout? LiveMicOf(int playerId)
    {
        foreach (var h in _holdouts)
            if (h.Lit && h.LiveMic && h.Occupant == playerId)
                return h;
        return null;
    }

    void Release(Holdout h)
    {
        h.State = HoldoutState.Dormant;
        h.Occupant = -1;
        h.LiveMic = false;
        h.Progress = 0;
        h.Breacher = -1;
    }

    /// <summary>D.8: they come back inside the Holdout, on 80 health with the standard kit; it's spent for the run.</summary>
    void Free(Holdout h, IReadOnlyList<(int Id, PlayerState State)> crew, Action<int, PlayerState> set)
    {
        int id = h.Occupant;
        h.State = HoldoutState.Freed;
        h.LiveMic = false;
        h.Progress = h.Breach(Tuning).Seconds;
        h.Breacher = -1;
        _queue.RemoveAll(e => e.PlayerId == id);
        Freed.Add(id);
        var was = crew.First(c => c.Id == id).State;
        var back = new PlayerState
        {
            Parent = PlayerState.World,
            Position = h.Inside,
            Surface = Surface.Ground,
            Health = Tuning.FreedHealth,
            LineHint = h.LineHint,
            Yaw = was.Yaw,
            Placed = (byte)(was.Placed + 1),
            // What they carried, back with them (T108: nobody comes out of a Holdout empty-handed).
            Kit = was.Kit,
            HeldSlot = was.HeldSlot,
        };
        _health[id] = back.Health;
        set(id, back);
    }

    /// <summary>The Holdouts freed so far (spent for the run), for an autosave.</summary>
    public int[] Spent => [.. _holdouts.Where(h => h.State == HoldoutState.Freed).Select(h => h.Index)];

    /// <summary>A resumed night (spec E autosave): these Holdouts were freed before the save.</summary>
    public void Spend(IEnumerable<int> indices)
    {
        foreach (int i in indices)
            if (i >= 0 && i < _holdouts.Count)
                _holdouts[i].State = HoldoutState.Freed;
    }

    /// <summary>Client side: adopts the host's Holdouts.</summary>
    public void Mirror(int index, HoldoutState state, int occupant, double progress, bool liveMic = false)
    {
        if (index < 0 || index >= _holdouts.Count)
            return;
        var h = _holdouts[index];
        h.State = state;
        h.Occupant = occupant;
        h.Progress = progress;
        h.LiveMic = liveMic;
    }
}
