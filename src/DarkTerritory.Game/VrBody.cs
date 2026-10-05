using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// Mirror of content/tuning/vr.json <c>body</c> (T82, roadmap M4 "VR body IK"; ARCHITECTURE §8 note 208). Not in the GDD or
/// the spec (note 40: there are no VR numbers in either): our reading of how a headset player's body should look to the
/// rest of the crew.
/// </summary>
public sealed record VrBodyTuning
{
    /// <summary>A head this far under standing height (m) is a lean from the hips; past it the knees bend as well.</summary>
    public double LeanDrop { get; init; } = 0.25;
    /// <summary>How far the torso leans over (degrees) at <see cref="LeanDrop"/>.</summary>
    public double LeanDegrees { get; init; } = 35;
    /// <summary>The most the hips go down in a crouch (m).</summary>
    public double CrouchMost { get; init; } = 0.5;
    /// <summary>The head turns this far (degrees) from the hips before the hips come round after it.</summary>
    public double YawDeadzoneDegrees { get; init; } = 35;
    /// <summary>Of the head's turn from the hips, what the torso takes (the neck the rest).</summary>
    public double TwistShare { get; init; } = 0.6;
    /// <summary>The most the neck tips the head up or down to the headset's pitch (degrees).</summary>
    public double NodDegrees { get; init; } = 50;
    /// <summary>Each foot's place either side of the hips when they're square over them (m).</summary>
    public double StanceHalfWidth { get; init; } = 0.12;
    /// <summary>A foot steps when the hips have moved its place this far from it (m) ...</summary>
    public double StepDistance { get; init; } = 0.2;
    /// <summary>... or turned this far from it (degrees).</summary>
    public double StepTurnDegrees { get; init; } = 30;
    /// <summary>How long a step takes (s), and how high the foot lifts at its middle (m).</summary>
    public double StepSeconds { get; init; } = 0.28;
    public double StepHeight { get; init; } = 0.09;
    /// <summary>A step lands this far past the foot's place, along the way it went (of the step), so walking hips aren't chased.</summary>
    public double StepOvershoot { get; init; } = 0.25;
}

/// <summary>A foot on the floor: where (the parent frame's x and z; y unused) and which way it points (radians).</summary>
public readonly record struct Foot(Double3 At, double Yaw);

/// <summary>
/// A headset crewmate's legs between frames (T82): the hips' yaw, both feet where they're planted, and the step under way
/// (<see cref="Stepping"/> −1 the left, +1 the right, 0 none; from <see cref="From"/>, <see cref="Progress"/> 0..1 of the
/// way). All in the frame of the car they stand in, so the train moving under them moves nothing.
/// </summary>
public readonly record struct VrStride(double HipsYaw, Foot Left, Foot Right, int Stepping = 0, Foot From = default, double Progress = 0, int Last = 1);

/// <summary>
/// What the crew model does under a headset this frame (T82), in the model's space (from the feet, x right, y up, z behind,
/// the model faced the head's way): the torso's lean (radians, forward positive) and the hips' drop (m), the hips' yaw from
/// the head's (radians, positive left), the torso's share of the twist back towards the head, the neck's nod (positive
/// up), and each foot's ankle point over the floor (y: its lift in a step) and its yaw from the head's.
/// </summary>
public readonly record struct VrBodyPose(double Lean, double Crouch, double Hips, double Twist, double Nod,
    Double3 LeftFoot, double LeftYaw, Double3 RightFoot, double RightYaw);

/// <summary>
/// A headset crewmate's body under the head the snapshot carries (T82, roadmap M4; T47's arms reach from it): the spine
/// leans and crouches by how far the head is below standing height, and twists towards where it looks; the hips follow the
/// head's yaw once it's past a deadzone; the feet stay planted until the hips have moved or turned off them, then step in a
/// short arc. Presentation only and pure: the same inputs give the same body on every machine (no clock, no randomness).
/// </summary>
public static class VrBody
{
    /// <summary>A fresh stride: square under hips at <paramref name="hips"/>, facing <paramref name="yaw"/>.</summary>
    public static VrStride Stand(Double3 hips, double yaw, VrBodyTuning t) =>
        new(yaw, new Foot(Place(hips, yaw, -1, t), yaw), new Foot(Place(hips, yaw, 1, t), yaw));

    /// <summary>Where a foot belongs under hips at <paramref name="hips"/> facing <paramref name="yaw"/> (side −1 left, +1 right).</summary>
    public static Double3 Place(Double3 hips, double yaw, int side, VrBodyTuning t) =>
        new(hips.X + side * t.StanceHalfWidth * Math.Cos(yaw), hips.Y, hips.Z - side * t.StanceHalfWidth * Math.Sin(yaw));

    /// <summary>
    /// The hips' yaw after the head's: still while the head is within the deadzone of it, then dragged round to the
    /// deadzone's edge (radians, either way round).
    /// </summary>
    public static double Follow(double hips, double head, VrBodyTuning t)
    {
        double dead = t.YawDeadzoneDegrees * Math.PI / 180;
        double d = Wrap(head - hips);
        return Math.Abs(d) <= dead ? hips : Wrap(head - Math.Sign(d) * dead);
    }

    /// <summary>
    /// The spine for a head <paramref name="head"/> m over the feet, against standing at <paramref name="standing"/>: the
    /// first <see cref="VrBodyTuning.LeanDrop"/> of a lower head is a lean from the hips, the rest a crouch (to
    /// <see cref="VrBodyTuning.CrouchMost"/>). A head at or over standing height, or none (0), is stood straight.
    /// </summary>
    public static (double Lean, double Crouch) Spine(double head, double standing, VrBodyTuning t)
    {
        if (head <= 0)
            return (0, 0);
        double drop = Math.Max(0, standing - head);
        double lean = Math.Min(1, drop / Math.Max(1e-6, t.LeanDrop)) * t.LeanDegrees * Math.PI / 180;
        return (lean, Math.Clamp(drop - t.LeanDrop, 0, t.CrouchMost));
    }

    /// <summary>
    /// The stride a step of <paramref name="dt"/> later, under hips now at <paramref name="hips"/> with the head turned to
    /// <paramref name="head"/> (parent frame). A step under way goes on to where that foot belongs now (and lands there);
    /// with none, the foot furthest out of place steps, if it's past <see cref="VrBodyTuning.StepDistance"/> or
    /// <see cref="VrBodyTuning.StepTurnDegrees"/>; level pegging, the one that didn't step last. One foot at a time.
    /// </summary>
    public static VrStride Step(VrStride s, Double3 hips, double head, double dt, VrBodyTuning t)
    {
        s = s with { HipsYaw = Follow(s.HipsYaw, head, t) };
        if (s.Stepping != 0)
        {
            double p = s.Progress + dt / Math.Max(1e-3, t.StepSeconds);
            if (p < 1)
                return s with { Progress = p };
            var landed = new Foot(Target(s, hips, s.Stepping, t), s.HipsYaw);
            return (s.Stepping < 0 ? s with { Left = landed } : s with { Right = landed }) with { Stepping = 0, Progress = 0, Last = s.Stepping };
        }
        double left = OutOfPlace(s.Left, hips, s.HipsYaw, -1, t), right = OutOfPlace(s.Right, hips, s.HipsYaw, 1, t);
        if (Math.Max(left, right) < 1)
            return s;
        int side = left > right ? -1 : right > left ? 1 : -s.Last;
        return s with { Stepping = side, From = side < 0 ? s.Left : s.Right, Progress = 0 };
    }

    /// <summary>How far out of place a foot is, as a fraction of the step thresholds (1 or more: it steps).</summary>
    static double OutOfPlace(Foot f, Double3 hips, double yaw, int side, VrBodyTuning t)
    {
        var d = Place(hips, yaw, side, t) - f.At;
        double moved = Math.Sqrt(d.X * d.X + d.Z * d.Z) / Math.Max(1e-6, t.StepDistance);
        double turned = Math.Abs(Wrap(yaw - f.Yaw)) / Math.Max(1e-6, t.StepTurnDegrees * Math.PI / 180);
        return Math.Max(moved, turned);
    }

    /// <summary>Where a step lands: the foot's place now, a little past it along the way the step goes.</summary>
    static Double3 Target(VrStride s, Double3 hips, int side, VrBodyTuning t)
    {
        var place = Place(hips, s.HipsYaw, side, t);
        var go = (place - s.From.At) with { Y = 0 };
        return place + go * t.StepOvershoot;
    }

    /// <summary>
    /// A foot as it is this frame (parent frame): planted, or along its step, lifted in an arc to
    /// <see cref="VrBodyTuning.StepHeight"/> at the middle (y is the lift over the floor).
    /// </summary>
    public static (Double3 At, double Yaw) FootNow(VrStride s, int side, Double3 hips, VrBodyTuning t)
    {
        var planted = side < 0 ? s.Left : s.Right;
        if (s.Stepping != side)
            return (planted.At with { Y = 0 }, planted.Yaw);
        double p = Math.Clamp(s.Progress, 0, 1), e = p * p * (3 - 2 * p);
        var to = Target(s, hips, side, t);
        var at = s.From.At + (to - s.From.At) * e;
        return (at with { Y = t.StepHeight * Math.Sin(Math.PI * p) }, s.From.Yaw + Wrap(s.HipsYaw - s.From.Yaw) * e);
    }

    /// <summary>
    /// The model's body this frame for a stride under hips at <paramref name="hips"/> facing <paramref name="yaw"/> (the
    /// head's, which the model faces), the head <paramref name="head"/> m over the feet against <paramref name="standing"/>,
    /// looking <paramref name="pitch"/> up (radians).
    /// </summary>
    public static VrBodyPose Pose(VrStride s, Double3 hips, double yaw, double head, double pitch, double standing, VrBodyTuning t)
    {
        var (lean, crouch) = Spine(head, standing, t);
        double nod = Math.Clamp(pitch + lean, -t.NodDegrees * Math.PI / 180, t.NodDegrees * Math.PI / 180);
        double hipsYaw = Wrap(s.HipsYaw - yaw);
        var (l, ly) = FootNow(s, -1, hips, t);
        var (r, ry) = FootNow(s, 1, hips, t);
        return new VrBodyPose(lean, crouch, hipsYaw, -hipsYaw * t.TwistShare, nod, Model(l, hips, yaw), Wrap(ly - yaw), Model(r, hips, yaw), Wrap(ry - yaw));
    }

    /// <summary>A parent-frame point (y: its own height over the floor) into the model's space, from the hips' feet facing <paramref name="yaw"/>.</summary>
    static Double3 Model(Double3 p, Double3 hips, double yaw)
    {
        double dx = p.X - hips.X, dz = p.Z - hips.Z, c = Math.Cos(yaw), n = Math.Sin(yaw);
        return new Double3(dx * c - dz * n, p.Y, dx * n + dz * c);
    }

    static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);
}
