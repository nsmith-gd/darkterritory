using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim;

/// <summary>
/// The flare pistol (note 871; docs/design/upgrades.md §4, the director, 13 Oct: "fantastic"): bought at the fortress's stores
/// from the Frontier tier, it's in the guard van loaded (its flares left in <see cref="Physics.Body.Charge"/>). Fire with it in
/// hand sends a flare out along the look, arcing; where it comes down it burns (combat.json <c>flarePistol.burnSeconds</c>),
/// lighting a village (senses.json <c>flare</c>), and the Fire Flies follow it away from the train (Interior.FireFlies). The
/// host's: a flare is a body like any, so every machine sees it fly and burn from its record.
/// </summary>
public sealed partial class World
{
    // Who had Fire down last tick, and when each pistol last fired (its body's id, the tick).
    readonly HashSet<int> _flareFireWas = [];
    readonly Dictionary<int, uint> _flareFiredAt = [];

    /// <summary>Fire pressed with a loaded flare pistol in hand: a flare out along the look (host).</summary>
    void FireFlare(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool fire = intent.Has(PlayerButtons.Fire) && s.Alive;
        bool pressed = fire && !_flareFireWas.Contains(playerId);
        if (fire)
            _flareFireWas.Add(playerId);
        else
            _flareFireWas.Remove(playerId);
        if (!Authority || !pressed || s.Has(PlayerFlags.Seated) || Bodies.CarriedBy(playerId) is not { Kind: Physics.BodyKind.FlarePistol, Charge: >= 1 } pistol)
            return;
        var t = Combat?.FlarePistol ?? new Combat.FlarePistolTuning();
        if (_flareFiredAt.TryGetValue(pistol.Id, out uint last) && (Tick - last) * SimConstants.TickSeconds < t.Every)
            return;
        _flareFiredAt[pistol.Id] = Tick;
        pistol.Charge -= 1;
        var look = Sim.Run.Bookmarks.Forward(s.Yaw, s.Pitch);
        var velocity = s.Velocity;
        if (s.Parent >= 0 && s.Parent < Train.Frames.Count)
        {
            var frame = Train.Frames[s.Parent];
            look = frame.DirToWorld(look);
            velocity = frame.Velocity + frame.DirToWorld(s.Velocity);
        }
        var muzzle = PlayerMotor.WorldPosition(s, Train) + Double3.Up * Train.Dynamics.Tuning.Pick.EyeHeight + look * 0.5;
        var flare = Bodies.SpawnItem(muzzle, s.LineHint, Physics.BodyKind.Flare);
        flare.Claimed = true;
        flare.LastHeld = playerId;
        flare.Burns = t.BurnSeconds;
        var p = flare.Pbd.Particles;
        p[0].Position = muzzle;
        p[0].SetVelocity(velocity + (look + Double3.Up * t.Lift).Normalized * t.Speed, SimConstants.TickSeconds);
        flare.Pbd.Wake();
    }

    /// <summary>A flare burning down (host, with the senses' step): its light left in Charge for every machine (0..1), gone when it's out.</summary>
    void StepFlares(double dt)
    {
        double burn = Combat?.FlarePistol.BurnSeconds ?? new Combat.FlarePistolTuning().BurnSeconds;
        foreach (var b in Bodies.All)
            if (b.Kind == Physics.BodyKind.Flare)
            {
                b.Burns = Math.Max(0, b.Burns - dt);
                b.Charge = burn > 0 ? b.Burns / burn : 0;
            }
        Bodies.All.Where(b => b.Kind == Physics.BodyKind.Flare && b.Burns <= 0).ToList().ForEach(Bodies.Remove);
    }
}
