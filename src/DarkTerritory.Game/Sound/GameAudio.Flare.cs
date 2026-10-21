using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The flare pistol heard (queue #675, note 975; n1-w3's note 871, docs/design/upgrades.md §4: "A flare lights a village for
/// 60 s, and the Fire Flies follow it away from the train"). The shot is the shooter's own the moment they press Fire with a
/// loaded pistol in hand (the host spawns the flare, World.FireFlare, so a client would hear its own shot a round trip
/// late); everyone else hears it where the flare first flies, the shooter's own flare passed over. The flare burning is
/// held on the flare's body while its burn lasts (its <see cref="Body.Charge"/>, 0..1, which every machine has), coming in
/// as the shot's hiss dies and dying with the flare. Fire on an empty pistol is a dry click, the shooter's own. None of it
/// is a creature's tell (spec A.4: the burn is held under the car-fire tell's band, recipes/flare.py).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The burn comes in this long (s) after the flare's first seen, as the shot's own hiss of it flying off dies…</summary>
    const double FlareBurnIn = 0.9;
    /// <summary>…over this long (s).</summary>
    const double FlareBurnRamp = 0.9;
    /// <summary>The last of a flare's burn (its Charge, 0..1) it dies away over: the last 3 s of its minute.</summary>
    const double FlareDying = 0.05;
    /// <summary>A flare seen this soon (s) after this machine's own shot, and this near (m) where it was fired, is that shot's.</summary>
    const double OwnFlareWithin = 1.5, OwnFlareNear = 40;

    readonly Dictionary<int, double> _flaresSeen = [];
    readonly List<(double At, Double3 Where)> _ownFlares = [];
    double _ownFlareShot = double.NegativeInfinity;

    /// <summary>
    /// Fire pressed with the flare pistol in hand (the one thing the sim fires it from, Bodies.CarriedBy): the shot, as the
    /// sim fires it (alive, not seated, a flare left as of last tick, not again within <c>flarePistol.every</c>), or the dry
    /// click with none left. Last tick's flares, because the host has taken this one off already.
    /// </summary>
    void FlarePistol(World world, bool firePressed)
    {
        if (!firePressed || world.Bodies.CarriedBy(OwnId) is not { Kind: BodyKind.FlarePistol } pistol)
            return;
        PlayerState? own = null;
        foreach (var (id, s) in CrewStates)
            if (id == OwnId)
                own = s;
        if (own is not { Alive: true } me || me.Has(PlayerFlags.Seated))
            return;
        var train = world.Train;
        if (me.Parent != PlayerState.World && me.Parent >= train.Frames.Count)
            return;
        double yaw = PlayerMotor.WorldYaw(me, train);
        var hands = world.Bodies.Hands;
        var hand = PlayerMotor.WorldPosition(me, train) + Double3.Up * hands.CarryHeight
            + new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw)) * hands.CarryForward;
        double flares = _crewBodies.TryGetValue(pistol.Id, out var m) ? m.Charge : pistol.Charge;
        if (flares < 1)
        {
            Cue("up-flare.click", hand);
            return;
        }
        double every = world.Combat?.FlarePistol.Every ?? new Sim.Combat.FlarePistolTuning().Every;
        if (_time - _ownFlareShot < every - 1e-6)
            return;
        _ownFlareShot = _time;
        _ownFlares.Add((_time, hand));
        Cue("up-flare.shot", hand);
    }

    /// <summary>A flare first seen: the shot, where it flies, unless it's this machine's own (heard already).</summary>
    void FlareSeen(Body b, Double3 centre, float occlusion)
    {
        // Seen already burning (joined, or come into range): its burn full at once, no shot.
        _flaresSeen[b.Id] = _crewBodiesPrimed ? _time : _time - FlareBurnIn - FlareBurnRamp;
        if (!_crewBodiesPrimed)
            return;
        _ownFlares.RemoveAll(o => _time - o.At > OwnFlareWithin);
        int mine = _ownFlares.FindIndex(o => (o.Where - centre).Length < OwnFlareNear);
        if (mine >= 0)
            _ownFlares.RemoveAt(mine);
        else
            Cue("up-flare.shot", centre, occlusion);
    }

    /// <summary>A flare burning, held on it: in as the shot's hiss of it dies, out with the last of its burn.</summary>
    void FlareBurning(Body b, Double3 centre, float occlusion)
    {
        if (b.Charge <= 0)
            return;
        double age = _time - _flaresSeen.GetValueOrDefault(b.Id, _time);
        if (Hold("up-flare.burn", b.Id, centre, occlusion) is { } burn)
            burn.Volume = (float)(Math.Clamp((age - FlareBurnIn) / FlareBurnRamp, 0, 1) * Math.Clamp(b.Charge / FlareDying, 0, 1));
    }
}
