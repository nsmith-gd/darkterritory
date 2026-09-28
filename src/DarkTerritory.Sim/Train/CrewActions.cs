using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// What a player does with their hands at the engine's interactables: shovel coal (hold Use at the
/// firebox, one unit per 1.2 s, spec B.6) and hold the vent valve open. Runs on the host for everyone
/// and on a client for its own player, before the train steps, so the boiler sees this tick's crew.
/// </summary>
public static class CrewActions
{
    public static void Apply(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, double dt)
    {
        if (train.BoilerTuning is not { } bt || !s.Alive || !intent.Has(PlayerButtons.Use) || !PlayerMotor.InCab(s, train))
        {
            s.ActionProgress = 0;
            return;
        }
        var near = Nearest(s, train);
        switch (near)
        {
            case InteractableKind.Firebox:
                s.ActionProgress += dt;
                if (s.ActionProgress >= bt.ShovelSeconds)
                {
                    train.Boiler.Shovel(bt);
                    s.ActionProgress -= bt.ShovelSeconds;
                }
                break;
            case InteractableKind.Vent:
                train.Boiler.Venting = true;
                s.ActionProgress = 0;
                break;
            default:
                s.ActionProgress = 0;
                break;
        }
    }

    /// <summary>The interactable within reach of the player, if any.</summary>
    public static InteractableKind? Nearest(in PlayerState s, TrainOnLine train)
    {
        InteractableKind? best = null;
        double bestD = double.MaxValue;
        foreach (var i in train.Frames[0].Shape.Interactables)
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
