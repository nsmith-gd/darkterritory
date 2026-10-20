using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>
    /// Note 716: the big cab's chart table stands down its middle (the gallery's benches are along its sides, note 905), and
    /// the ways down the cab are either side of it (<see cref="EnginePlan.Lane"/>). A bot walking the cab, front to back or back to front, into one of
    /// them goes out into the way on the side it's on and on along it, whatever it was going there for (its own walks use
    /// <see cref="WarmUp.SteerCab"/>; this is for every other one, a rescue or a crewmate to heal). The treads up to the
    /// gallery are walked up, as anyone does.
    /// </summary>
    public static PlayerIntent CabFurniture(PlayerIntent intent, in PlayerState self, World world, PlayerTuning p)
    {
        var train = world.Train;
        if (intent.MoveX == 0 && intent.MoveZ == 0 || !PlayerMotor.InCab(self, train))
            return intent;
        double fx = -DMath.Sin(self.Yaw), fz = -DMath.Cos(self.Yaw), rx = DMath.Cos(self.Yaw), rz = -DMath.Sin(self.Yaw);
        double wx = intent.MoveZ * fx + intent.MoveX * rx, wz = intent.MoveZ * fz + intent.MoveX * rz;
        double len = Math.Sqrt(wx * wx + wz * wz);
        if (len < 1e-6 || Math.Abs(wz) < 0.3 * len)
            return intent;
        wz = Math.Sign(wz);
        var at = self.Position;
        var plan = EnginePlan.Of(train.Dynamics.Tuning.Geometry);
        double r = p.Radius + LyingMargin;
        Box? block = null;
        foreach (var f in plan.Furniture)
            if (f.Max.Y - at.Y > p.StepUp && f.Min.Y < at.Y + p.Height && Ahead(at, 0, wz, f, r) is { } along && along > 0 && along <= LyingLookAhead)
                block = f;
        if (block is not { } b)
            return intent;
        // Round the chart table by the way on its side; along a bench against a side wall (note 905), in off its edge.
        double lane = Math.Abs(b.Centre.X) < 0.3 ? at.X >= b.Centre.X ? plan.Lane : -plan.Lane
            : Math.Sign(b.Centre.X) * Math.Min(plan.Lane, Math.Min(Math.Abs(b.Min.X), Math.Abs(b.Max.X)) - r);
        double ax = Math.Clamp((lane - at.X) * 3, -1, 1), az = Math.Abs(lane - at.X) < 0.1 ? wz : 0;
        return intent with
        {
            MoveZ = (float)Math.Clamp(ax * fx + az * fz, -1, 1),
            MoveX = (float)Math.Clamp(ax * rx + az * rz, -1, 1),
        };
    }
}
