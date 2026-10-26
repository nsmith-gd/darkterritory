using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim;

/// <summary>
/// A pull of the communication cord (note 1052): the car it was pulled in (its vehicle id, the number the crew know it by), who
/// pulled it, and the tick. <see cref="Tick"/> 0 is none yet.
/// </summary>
public readonly record struct CordPull(int Car, int By, uint Tick)
{
    public bool Any => Tick > 0;
}

/// <summary>
/// The crew's tools (queue #753, ARCHITECTURE §8 note 1052; docs/design/upgrades.md §7.3, the director, 14 Oct 2026 §8.2: "all
/// of §7.3's"): the communication cord, the end-of-train telltale and the fusees. Each a share of a trouble, never all of it
/// (§2): the cord says which car, not why; the telltale says the train's parted, not where; a fusee marks the way back, and is
/// a light for whatever answers one.
/// </summary>
public sealed partial class World
{
    // Who had the whistle's key down last tick (the cord's a press), when each last pulled it; who had Fire down (a fusee is a
    // press) and when each box last gave one.
    readonly HashSet<int> _cordWas = [], _fuseeFireWas = [];
    readonly Dictionary<int, uint> _cordPulledAt = [], _fuseeStruckAt = [];
    bool _telltaleWas = true;

    /// <summary>
    /// The last pull of the communication cord (note 1052): the host's, on the world record, so every machine's cab bell
    /// strikes the car's number and its HUD names the car.
    /// </summary>
    public CordPull Cord { get; set; }
    /// <summary>Host (note 1052): the cord's pulls tonight, for the clerk.</summary>
    public int CordPulls { get; private set; }
    /// <summary>Host (note 1052): how many times the telltale went out tonight (the train parted behind the cab), for the clerk.</summary>
    public int TelltaleOuts { get; private set; }
    /// <summary>Host (note 1052): the fusees struck tonight, for the clerk.</summary>
    public int FuseesStruck { get; private set; }

    /// <summary>
    /// The whistle's key pressed aboard a car of the engine's rake, behind the engine, with the cord fitted (host): the cab's
    /// bell rings with that car's number. In the cab the key is the whistle's; on the engine outside the cab there's no cord
    /// to pull, and in a car cut off the train the cord's parted with it.
    /// </summary>
    void PullCord(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool pulling = intent.Has(PlayerActions.Whistle) && s.Alive;
        bool pressed = pulling && !_cordWas.Contains(playerId);
        if (pulling)
            _cordWas.Add(playerId);
        else
            _cordWas.Remove(playerId);
        var kit = Train.Dynamics.Tuning.Kit;
        if (!Authority || !pressed || !kit.Cord || s.Has(PlayerFlags.Held) || s.Parent < 1 || s.Parent >= Train.Vehicles.Count
            || Train.Vehicles[s.Parent].IsHandcar || Train.RakeOf(s.Parent) != Train.Dynamics)
            return;
        if (_cordPulledAt.TryGetValue(playerId, out uint last) && (Tick - last) * SimConstants.TickSeconds < kit.CordBell.Every)
            return;
        _cordPulledAt[playerId] = Tick;
        Cord = new CordPull(s.Parent, playerId, Tick);
        CordPulls++;
    }

    /// <summary>
    /// Fire pressed with a box of fusees in hand (host): one struck and dropped at the feet, burning red for train.json
    /// <c>kit.fusee.burnSeconds</c>. Off a moving car it goes down with the car's speed, as anything dropped does.
    /// </summary>
    void StrikeFusee(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool fire = intent.Has(PlayerButtons.Fire) && s.Alive;
        bool pressed = fire && !_fuseeFireWas.Contains(playerId);
        if (fire)
            _fuseeFireWas.Add(playerId);
        else
            _fuseeFireWas.Remove(playerId);
        if (!Authority || !pressed || s.Has(PlayerFlags.Seated) || Bodies.CarriedBy(playerId) is not { Kind: Physics.BodyKind.Fusees, Charge: >= 1 } box)
            return;
        var t = Train.Dynamics.Tuning.Kit.Fusee;
        if (_fuseeStruckAt.TryGetValue(box.Id, out uint last) && (Tick - last) * SimConstants.TickSeconds < t.Every)
            return;
        _fuseeStruckAt[box.Id] = Tick;
        box.Charge -= 1;
        // Out in front of the feet, a hand's height up, so it falls clear of them.
        var ahead = Sim.Run.Bookmarks.Forward(s.Yaw, 0);
        var velocity = s.Velocity;
        if (s.Parent >= 0 && s.Parent < Train.Frames.Count)
        {
            var frame = Train.Frames[s.Parent];
            ahead = frame.DirToWorld(ahead);
            velocity = frame.Velocity + frame.DirToWorld(s.Velocity);
        }
        var at = PlayerMotor.WorldPosition(s, Train) + ahead * 0.4 + Double3.Up * 0.5;
        var fusee = Bodies.SpawnItem(at, s.LineHint, Physics.BodyKind.Fusee);
        fusee.Claimed = true;
        fusee.LastHeld = playerId;
        fusee.Burns = t.BurnSeconds;
        fusee.Charge = 1;
        var p = fusee.Pbd.Particles;
        p[0].Position = at;
        p[0].SetVelocity(velocity, SimConstants.TickSeconds);
        fusee.Pbd.Wake();
        FuseesStruck++;
    }

    /// <summary>
    /// Host, with the senses' step: each fusee burning down, its burn left in Charge for every machine (0..1), gone when it's
    /// out; and the telltale, counted each time it goes out.
    /// </summary>
    void StepCrewTools(double dt)
    {
        double burn = Train.Dynamics.Tuning.Kit.Fusee.BurnSeconds;
        foreach (var b in Bodies.All)
            if (b.Kind == Physics.BodyKind.Fusee)
            {
                b.Burns = Math.Max(0, b.Burns - dt);
                b.Charge = burn > 0 ? b.Burns / burn : 0;
            }
        Bodies.All.Where(b => b.Kind == Physics.BodyKind.Fusee && b.Burns <= 0).ToList().ForEach(Bodies.Remove);
        if (Telltale.Lit(Train) is { } lit)
        {
            if (_telltaleWas && !lit)
                TelltaleOuts++;
            _telltaleWas = lit;
        }
    }
}
