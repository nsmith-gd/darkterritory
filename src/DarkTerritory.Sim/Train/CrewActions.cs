using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// What a player does with their hands, all by holding Use (without pushing forward, which is
/// how you grab a ladder instead):
/// <list type="bullet">
/// <item>at the firebox, shovel coal: one unit per 1.2 s (spec B.6);</item>
/// <item>at the vent valve, hold it open;</item>
/// <item>on a coupler plate, cut the coupling behind that car (GDD §17, §24);</item>
/// <item>at a car's brake wheel, wind its rake's handbrakes on or off.</item>
/// </list>
/// Runs on the host for everyone and on a client for its own player, before the train steps.
/// </summary>
public static class CrewActions
{
    public static void Apply(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, double dt)
    {
        if (!s.Alive || !intent.Has(PlayerButtons.Use) || intent.MoveZ > 0.5 || s.Parent == PlayerState.World)
        {
            s.ActionProgress = 0;
            return;
        }
        double before = s.ActionProgress;
        var couplings = train.Dynamics.Tuning.Couplings;
        var near = NearestInteractable(s, train);

        // A door you're facing comes first, even from the coupler plate; otherwise Use there cuts the coupling.
        if (s.Surface == Surface.Coupler && near?.Thing.Kind != InteractableKind.Door)
        {
            double needed = train.CouplingUnderLoad(s.Parent) ? couplings.UncoupleUnderLoadSeconds : couplings.UncoupleSeconds;
            s.ActionProgress += dt;
            if (before < needed && s.ActionProgress >= needed)
                train.Uncouple(s.Parent);
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
            case InteractableKind.Vent when PlayerMotor.InCab(s, train):
                train.Boiler.Venting = true;
                s.ActionProgress = 0;
                break;
            case InteractableKind.Handbrake when s.Surface == Surface.Roof:
                s.ActionProgress += dt;
                if (before < couplings.HandbrakeSeconds && s.ActionProgress >= couplings.HandbrakeSeconds)
                    train.SetHandbrake(s.Parent, !train.RakeOf(s.Parent).Handbrake);
                break;
            default:
                s.ActionProgress = 0;
                break;
        }
    }

    /// <summary>The interactable on the player's own vehicle within reach, if any.</summary>
    public static InteractableKind? Nearest(in PlayerState s, TrainOnLine train) => NearestInteractable(s, train)?.Thing.Kind;

    /// <summary>
    /// Within reach across and about the same height: the roof's brake wheel isn't in reach from the floor
    /// under it. A door is also in reach from the coupler plate or next car's end, so it can be opened from
    /// outside (Soot Children: "the failure is opening a door").
    /// </summary>
    public static (Interactable Thing, int Vehicle)? NearestInteractable(in PlayerState s, TrainOnLine train)
    {
        if (s.Parent == PlayerState.World)
            return null;
        (Interactable, int)? best = null;
        double bestD = double.MaxValue;
        // Doors want facing: the coupler plate is in reach of two of them, and Use there also cuts the coupling.
        double fx = -Math.Sin(s.Yaw), fz = -Math.Cos(s.Yaw);
        void Search(int vehicle, Double3 at, bool doorsOnly)
        {
            foreach (var i in train.Frames[vehicle].Shape.Interactables)
            {
                if (doorsOnly && i.Kind != InteractableKind.Door)
                    continue;
                double dx = at.X - i.Position.X, dz = at.Z - i.Position.Z;
                double d = dx * dx + dz * dz;
                if (i.Kind == InteractableKind.Door && -(dx * fx + dz * fz) < 0.6 * Math.Sqrt(d))
                    continue;
                if (d <= i.Radius * i.Radius && Math.Abs(at.Y - i.Position.Y) < 1.2 && d < bestD)
                {
                    bestD = d;
                    best = (i, vehicle);
                }
            }
        }
        Search(s.Parent, s.Position, doorsOnly: false);
        // On a coupler plate, the next car's front door is in reach too.
        if (s.Surface == Surface.Coupler && train.VehicleBehind(s.Parent) is var behind and >= 0)
        {
            var world = train.Frames[s.Parent].ToWorld(s.Position);
            Search(behind, train.Frames[behind].ToLocal(world), doorsOnly: true);
        }
        return best;
    }
}
