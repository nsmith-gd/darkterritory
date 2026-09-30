using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// What a player does with their hands, all by holding Use (without pushing forward, which is
/// how you grab a ladder instead):
/// <list type="bullet">
/// <item>at the firebox, shovel coal: one unit per 1.2 s (spec B.6);</item>
/// <item>at the vent valve, hold it open;</item>
/// <item>on a coupler plate, nothing but the doors: the coupling is cut by <see cref="Uncoupling"/> (T91);</item>
/// <item>at a car's brake wheel, wind its rake's handbrakes on or off;</item>
/// <item>at a sandbox on the engine's running boards, sand the rail (Grease's counter, App. A.2).</item>
/// </list>
/// A VR player's reaching hand (T29) picks what's worked by where it is, not where they stand, and shovels by the
/// stroke: coal onto the shovel at the tender, then into the firebox (<see cref="ShovelByHand"/>).
/// Runs on the host for everyone and on a client for its own player, before the train steps.
/// </summary>
public static class CrewActions
{
    /// <param name="hand">The hand tuning, when hands are reported at all (<see cref="World.Hand"/>).</param>
    public static void Apply(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, double dt, HandTuning? hand = null)
    {
        if (Uncoupling(s, intent, train, hand))
        {
            var c = train.Dynamics.Tuning.Couplings;
            double needed = train.CouplingUnderLoad(s.Parent) ? c.UncoupleUnderLoadSeconds : c.UncoupleSeconds;
            double was = s.ActionProgress;
            s.ActionProgress += dt;
            if (was < needed && s.ActionProgress >= needed)
                train.Uncouple(s.Parent);
            return;
        }
        if (!s.Alive || !intent.Has(PlayerButtons.Use) || intent.MoveZ > 0.5 || s.Parent == PlayerState.World)
        {
            // Let go of the shovel and what's on it is spilled.
            s.ActionProgress = 0;
            s.Flags &= ~PlayerFlags.Shovelful;
            return;
        }
        double before = s.ActionProgress;
        var couplings = train.Dynamics.Tuning.Couplings;
        var near = NearestInteractable(s, train, hand);
        if (Hand(s, hand) && train.BoilerTuning is { } boiler && PlayerMotor.InCab(s, train)
            && near?.Thing.Kind is InteractableKind.Coal or InteractableKind.Firebox or null)
        {
            ShovelByHand(ref s, near?.Thing.Kind, train, boiler, dt);
            return;
        }
        s.Flags &= ~PlayerFlags.Shovelful;

        // Use on the coupler plate works a door, never the coupling (T91: that's Uncouple, looking down).
        if (s.Surface == Surface.Coupler && near?.Thing.Kind != InteractableKind.Door)
        {
            s.ActionProgress = 0;
            return;
        }

        switch (near?.Thing.Kind)
        {
            case InteractableKind.Door:
                double doorSeconds = train.Dynamics.Tuning.Geometry.Interior?.DoorSeconds ?? 0.4;
                s.ActionProgress += dt;
                if (before < doorSeconds && s.ActionProgress >= doorSeconds)
                    train.Vehicles[near.Value.Vehicle].ToggleDoor(near.Value.Thing.Index);
                break;
            case InteractableKind.Firebox when train.BoilerTuning is { } bt && PlayerMotor.InCab(s, train):
                s.ActionProgress += dt;
                if (s.ActionProgress >= bt.ShovelSeconds)
                {
                    train.Boiler.Shovel(bt);
                    s.ActionProgress -= bt.ShovelSeconds;
                }
                break;
            // Out on the running boards at a sandbox: sand going down under the drivers while it's held (App. A.2).
            case InteractableKind.Sandbox when s.Parent == 0 && s.Surface == Surface.Deck:
                train.Sanding = true;
                s.ActionProgress = 0;
                break;
            // Out on the running board by the smokebox (T97).
            case InteractableKind.Vent when s.Parent == 0 && s.Surface == Surface.Deck:
                train.Boiler.Venting = true;
                s.ActionProgress = 0;
                break;
            // A hand reaches the wheel from the top of the end ladder, too.
            case InteractableKind.Handbrake when s.Surface == Surface.Roof || Hand(s, hand):
                s.ActionProgress += dt;
                if (before < couplings.HandbrakeSeconds && s.ActionProgress >= couplings.HandbrakeSeconds)
                    train.SetHandbrake(s.Parent, !train.RakeOf(s.Parent).Handbrake);
                break;
            default:
                s.ActionProgress = 0;
                break;
        }
    }

    /// <summary>
    /// A shovelful by hand (T29): a hand gripping at the tender's coal face puts coal on the shovel; carried to the
    /// firebox, it goes in. Never faster than spec B.6's one unit per <see cref="BoilerTuning.ShovelSeconds"/>: the
    /// time counts from the grip, or from the last shovelful, so a steady swing shovels at the keyboard's rate and a
    /// frantic one doesn't beat it.
    /// </summary>
    static void ShovelByHand(ref PlayerState s, InteractableKind? at, TrainOnLine train, BoilerTuning boiler, double dt)
    {
        s.ActionProgress += dt;
        if (at == InteractableKind.Coal)
            s.Flags |= PlayerFlags.Shovelful;
        else if (at == InteractableKind.Firebox && s.Has(PlayerFlags.Shovelful) && s.ActionProgress >= boiler.ShovelSeconds)
        {
            train.Boiler.Shovel(boiler);
            s.Flags &= ~PlayerFlags.Shovelful;
            s.ActionProgress = 0;
        }
    }

    static bool Hand(in PlayerState s, HandTuning? hand) => hand is not null && s.Hand != default;

    /// <summary>
    /// Working the coupling you stand on (T91 playtest): Uncouple held, standing still, looking down at the coupler (well
    /// below level); or, in a headset, gripping with a hand reached down to it. Deliberate, and nothing else's button.
    /// </summary>
    public static bool Uncoupling(in PlayerState s, in PlayerIntent intent, TrainOnLine train, HandTuning? hand = null)
    {
        if (!s.Alive || s.Surface != Surface.Coupler || s.Parent == PlayerState.World || s.Has(PlayerFlags.Held)
            || Math.Abs(intent.MoveX) > 0.1 || Math.Abs(intent.MoveZ) > 0.1)
            return false;
        if (Hand(s, hand))
            return intent.Has(PlayerButtons.Use) && s.Hand.Y < ReachedDown;
        double down = train.Dynamics.Tuning.Couplings.UncoupleLookDownDegrees * Math.PI / 180;
        return intent.Has(PlayerActions.Uncouple) && s.Pitch <= -down;
    }

    /// <summary>A headset hand this low (from the feet) is down at the coupler, not at a door handle.</summary>
    const double ReachedDown = 0.7;

    /// <summary>The interactable on the player's own vehicle within reach, if any.</summary>
    public static InteractableKind? Nearest(in PlayerState s, TrainOnLine train, HandTuning? hand = null) => NearestInteractable(s, train, hand)?.Thing.Kind;

    /// <summary>
    /// Within reach across and about the same height: the roof's brake wheel isn't in reach from the floor
    /// under it. A door is also in reach from the coupler plate or next car's end, so it can be opened from
    /// outside (Soot Children: "the failure is opening a door").
    /// <para>
    /// A reaching hand (T29) is tested instead of the feet: within the thing's reach across and between knee and head
    /// height over its footing. It picks by where it is, so it wants no facing, and the brake wheel is in reach of a
    /// hand from the top of the end ladder. The tender's coal face is only for hands (the keyboard shovels at the
    /// firebox, as it always has).
    /// </para>
    /// </summary>
    public static (Interactable Thing, int Vehicle)? NearestInteractable(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (s.Parent == PlayerState.World)
            return null;
        var reaching = Hand(s, hand) ? PlayerMotor.HandAt(s) : null;
        (Interactable, int)? best = null;
        double bestD = double.MaxValue;
        // Doors want facing: the coupler plate is in reach of two of them, and Use there also cuts the coupling.
        double fx = -Math.Sin(s.Yaw), fz = -Math.Cos(s.Yaw);
        void Search(int vehicle, Double3 at, bool doorsOnly)
        {
            foreach (var i in train.Frames[vehicle].Shape.Interactables)
            {
                if (doorsOnly && i.Kind != InteractableKind.Door || reaching is null && i.Kind == InteractableKind.Coal)
                    continue;
                double dx = at.X - i.Position.X, dz = at.Z - i.Position.Z;
                double d = dx * dx + dz * dz;
                if (reaching is null && i.Kind == InteractableKind.Door && -(dx * fx + dz * fz) < 0.6 * Math.Sqrt(d))
                    continue;
                bool height = reaching is null ? Math.Abs(at.Y - i.Position.Y) < 1.2 : at.Y - i.Position.Y is >= 0.2 and <= 1.8;
                if (d <= i.Radius * i.Radius && height && d < bestD)
                {
                    bestD = d;
                    best = (i, vehicle);
                }
            }
        }
        var from = reaching ?? s.Position;
        Search(s.Parent, from, doorsOnly: false);
        // On a coupler plate, the next car's front door is in reach too.
        if (s.Surface == Surface.Coupler && train.VehicleBehind(s.Parent) is var behind and >= 0)
        {
            var world = train.Frames[s.Parent].ToWorld(from);
            Search(behind, train.Frames[behind].ToLocal(world), doorsOnly: true);
        }
        return best;
    }
}
