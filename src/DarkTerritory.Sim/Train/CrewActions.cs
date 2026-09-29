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

        if (s.Surface == Surface.Coupler)
        {
            double needed = train.CouplingUnderLoad(s.Parent) ? couplings.UncoupleUnderLoadSeconds : couplings.UncoupleSeconds;
            s.ActionProgress += dt;
            if (before < needed && s.ActionProgress >= needed)
                train.Uncouple(s.Parent);
            return;
        }

        switch (Nearest(s, train))
        {
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
    public static InteractableKind? Nearest(in PlayerState s, TrainOnLine train)
    {
        if (s.Parent == PlayerState.World)
            return null;
        InteractableKind? best = null;
        double bestD = double.MaxValue;
        foreach (var i in train.Frames[s.Parent].Shape.Interactables)
        {
            double dx = s.Position.X - i.Position.X, dz = s.Position.Z - i.Position.Z;
            double d = dx * dx + dz * dz;
            if (d <= i.Radius * i.Radius && d < bestD)
            {
                bestD = d;
                best = i.Kind;
            }
        }
        return best;
    }
}
