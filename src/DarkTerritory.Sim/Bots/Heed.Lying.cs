using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>How far ahead (m) a crewmate walking a car looks for something lying in the way.</summary>
    const double LyingLookAhead = 1.2;

    /// <summary>A hand's breadth (m) kept between a crewmate going by and what's lying there.</summary>
    const double LyingMargin = 0.1;

    /// <summary>
    /// Note 659 (the director, 13 Oct 2026: "poor stacking becomes an obstacle for players passing through"): what's lying still
    /// in a car is as solid for the bots as for anyone (<see cref="TrainOnLine.Lying"/>). A bot walking a car into a thing too
    /// tall to step onto goes round it, on whichever side of it has the room. Where neither has (the aisle stacked shut), it
    /// stands: it doesn't walk through, and it doesn't push at it for ever.
    /// </summary>
    public static PlayerIntent Lying(PlayerIntent intent, in PlayerState self, World world, PlayerTuning p)
    {
        var train = world.Train;
        if (self.Parent <= 0 || train.Lying.Count == 0 || intent.MoveX == 0 && intent.MoveZ == 0
            || train.Frames[self.Parent].Shape.Interior is not { } room)
            return intent;
        // Where it means to go, in the car's frame (PlayerMotor's move: forward −sin, −cos of the yaw; right cos, −sin).
        double fx = -DMath.Sin(self.Yaw), fz = -DMath.Cos(self.Yaw), rx = DMath.Cos(self.Yaw), rz = -DMath.Sin(self.Yaw);
        double wx = intent.MoveZ * fx + intent.MoveX * rx, wz = intent.MoveZ * fz + intent.MoveX * rz;
        double len = Math.Sqrt(wx * wx + wz * wz);
        // Walking the car is walking along it: what's in the way is what's ahead down the aisle, whatever sideways pull its
        // steering has back onto its line (judged by that, a bot sidestepping a crate is steered straight back into it).
        if (len < 1e-6 || Math.Abs(wz) < 0.3 * len)
            return intent;
        wx = 0;
        wz = Math.Sign(wz);
        var at = self.Position;
        double r = p.Radius + LyingMargin;
        (Box Box, double Along)? first = null;
        foreach (var (car, box) in train.Lying)
        {
            if (car != self.Parent || box.Max.Y - at.Y <= p.StepUp || box.Min.Y >= at.Y + p.Height)
                continue;
            // Along the way it's going, how far to the thing (its footprint grown by a body's breadth), if it's in the way at all.
            // Already up against it (come to take it, or put something by it), it's where it means to be.
            if (Ahead(at, wx, wz, box, r) is { } along && along > 0 && along <= LyingLookAhead && (first is not { } f || along < f.Along))
                first = (box, along);
        }
        if (first is not { } block)
            return intent;
        // Round it: across the car, to whichever side has room for a body and more of it. The car's fittings beside it (a crew
        // car's lockers, the heap down the load side, note 659's Solid.BoxIn) take their share of the width.
        var vehicle = train.Vehicles[self.Parent];
        double mid = (block.Box.Min.X + block.Box.Max.X) / 2;
        // From where it stands to past the thing: the way round is all of that.
        double z0 = Math.Min(at.Z, block.Box.Min.Z - r), z1 = Math.Max(at.Z, block.Box.Max.Z + r);
        foreach (var solid in train.Frames[self.Parent].Shape.Solids)
        {
            var fit = solid.BoxIn(vehicle);
            if (!solid.Present(vehicle) || fit.Max.Y - at.Y <= p.StepUp || fit.Min.Y >= at.Y + p.Height
                || fit.Max.Z <= z0 || fit.Min.Z >= z1 || fit.Max.X <= room.Min.X || fit.Min.X >= room.Max.X)
                continue;
            if ((fit.Min.X + fit.Max.X) / 2 < mid)
                room = room with { Min = room.Min with { X = Math.Max(room.Min.X, fit.Max.X) } };
            else
                room = room with { Max = room.Max with { X = Math.Min(room.Max.X, fit.Min.X) } };
        }
        double left = block.Box.Min.X - room.Min.X, right = room.Max.X - block.Box.Max.X;
        double need = 2 * r;
        bool leftOpen = left >= need && !BlockedBeside(train, self.Parent, block.Box, -1, room, need);
        bool rightOpen = right >= need && !BlockedBeside(train, self.Parent, block.Box, 1, room, need);
        if (!leftOpen && !rightOpen)
            return intent with { MoveX = 0, MoveZ = 0 };
        // The side it's already gone to while that's open (what's beside it changes as it goes along, and a choice made again
        // each step sends it back and forth); from square behind it, the roomier.
        bool goRight = at.X > mid + 0.05 ? rightOpen : at.X < mid - 0.05 ? !leftOpen : rightOpen && (!leftOpen || right > left);
        double side = goRight ? block.Box.Max.X + r : block.Box.Min.X - r;
        // Across to that side first, on along the car once there (on sooner, it's up against the thing's corner).
        double ax = Math.Clamp((side - at.X) * 3, -1, 1), az = Math.Abs(side - at.X) < 0.1 ? wz : 0;
        return intent with
        {
            MoveZ = (float)Math.Clamp(ax * fx + az * fz, -1, 1),
            MoveX = (float)Math.Clamp(ax * rx + az * rz, -1, 1),
        };
    }

    /// <summary>How far along (<paramref name="wx"/>, <paramref name="wz"/>) from <paramref name="at"/> its path meets the box grown by <paramref name="r"/>; null if it doesn't.</summary>
    static double? Ahead(Double3 at, double wx, double wz, Box box, double r)
    {
        double x0 = box.Min.X - r, x1 = box.Max.X + r, z0 = box.Min.Z - r, z1 = box.Max.Z + r;
        double tMin = 0, tMax = double.PositiveInfinity;
        if (!Slab(at.X, wx, x0, x1, ref tMin, ref tMax) || !Slab(at.Z, wz, z0, z1, ref tMin, ref tMax))
            return null;
        return tMin;
    }

    static bool Slab(double o, double d, double lo, double hi, ref double tMin, ref double tMax)
    {
        if (Math.Abs(d) < 1e-9)
            return o >= lo && o <= hi;
        double t0 = (lo - o) / d, t1 = (hi - o) / d;
        if (t0 > t1)
            (t0, t1) = (t1, t0);
        tMin = Math.Max(tMin, t0);
        tMax = Math.Min(tMax, t1);
        return tMin <= tMax;
    }

    /// <summary>Whether the gap beside <paramref name="box"/> on <paramref name="side"/> (−1 left, +1 right) is taken by something else lying there.</summary>
    static bool BlockedBeside(TrainOnLine train, int car, Box box, int side, Box room, double need)
    {
        double g0 = side < 0 ? room.Min.X : box.Max.X, g1 = side < 0 ? box.Min.X : room.Max.X;
        foreach (var (c, other) in train.Lying)
            if (c == car && other != box && other.Max.Z > box.Min.Z && other.Min.Z < box.Max.Z && other.Max.X > g0 && other.Min.X < g1
                && Math.Max(other.Min.X - g0, g1 - other.Max.X) < need)
                return true;
        return false;
    }
}
