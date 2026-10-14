using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// GDD §31 "Speed is felt" (the director's evening playtest, 13 Oct: "it just feels so agonizingly slow"; note 709): aboard
/// a moving train the view widens with speed. The rail joints' knock and the car's sway on the eye are here too, off as
/// shipped (note 735: "the cab is shaking. I don't like that"). Presentation only: it widens (and could move) the eye, never
/// the player, and the train's speeds are unchanged.
/// </summary>
public static class SpeedFeel
{
    /// <summary>How fast the car the viewer's on is going, as a share of <see cref="SpeedFeelTuning.FullAt"/> (0 to 1); 0 off the train.</summary>
    public static double Share(TrainOnLine train, in PlayerState viewer, SpeedFeelTuning t) =>
        viewer.Parent < 0 || viewer.Parent >= train.Frames.Count || t.FullAt <= 0 ? 0
            : Math.Clamp(Math.Abs(train.Dynamics.Velocity) / t.FullAt, 0, 1);

    /// <summary>The field of view (degrees) for <paramref name="baseFov"/> at the train's speed.</summary>
    public static float Fov(float baseFov, TrainOnLine train, in PlayerState viewer, SpeedFeelTuning t)
    {
        double share = Share(train, viewer, t);
        return share <= 0 ? baseFov : baseFov + (float)(t.Widen * Math.Pow(share, t.Ease));
    }

    /// <summary>The eye's offset (m, world) from the rail joints' knocks and the car's sway, where the viewer's car is now.</summary>
    public static Double3 Offset(TrainOnLine train, in PlayerState viewer, SpeedFeelTuning t)
    {
        double share = Share(train, viewer, t);
        // Note 735: off as shipped (the director: "the cab is shaking. I don't like that").
        if (share <= 0 || t.Jolt <= 0 && t.Sway <= 0)
            return default;
        var frame = train.Frames[viewer.Parent];
        double along = train.Cars[viewer.Parent].FrontDistance;
        // A knock under each bogie as it crosses a joint: a short bump of travel either side of it.
        double knock = 0;
        if (t.Joint > 0 && t.JoltWidth > 0)
            foreach (double bogie in new[] { 0.0, t.Bogies })
            {
                double past = (along - bogie) % t.Joint;
                if (past < 0)
                    past += t.Joint;
                double d = Math.Min(past, t.Joint - past) / t.JoltWidth;
                knock = Math.Max(knock, Math.Exp(-d * d));
            }
        double sway = t.SwayWave > 0 ? Math.Sin(along * 2 * Math.PI / t.SwayWave) : 0;
        return frame.Up * (-t.Jolt * share * knock) + frame.Right * (t.Sway * share * sway);
    }
}
