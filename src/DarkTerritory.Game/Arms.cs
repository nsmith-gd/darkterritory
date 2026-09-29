using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// A crewmate's arms (T47, roadmap M4 "VR body IK"): a headset player's hands are where their hands are, so the rest of
/// the crew see them reach for the lever, grab the crank, hold up the lamp. Two bones from the shoulder, the elbow bent
/// down and out like a person's; a keyboard player's arms hang. All in the frame the player faces, from their feet
/// (x right, y up, z behind), like the hands the sim takes.
/// </summary>
public static class Arms
{
    public const double Upper = 0.30, Lower = 0.32;
    /// <summary>Where the shoulders are, from the feet, in the facing frame (the greybox figure's).</summary>
    public const double ShoulderHeight = 1.45, ShoulderWidth = 0.27;

    public static Double3 Shoulder(int side) => new(side * ShoulderWidth, ShoulderHeight, 0);

    /// <summary>A hand hanging at the side.</summary>
    public static Double3 Hanging(int side) => new(side * (ShoulderWidth + 0.05), ShoulderHeight - (Upper + Lower) * 0.97, 0.02);

    /// <summary>
    /// The two hands for a crewmate's reported hands: each goes to the side it's on (the sim doesn't say which is which),
    /// the other one hangs. Returns (left, right).
    /// </summary>
    public static (Double3 Left, Double3 Right) Hands(Double3 hand, Double3 other)
    {
        var reported = new[] { hand, other }.Where(h => h != default).OrderBy(h => h.X).ToList();
        return reported.Count switch
        {
            2 => (reported[0], reported[1]),
            1 when reported[0].X < 0 => (reported[0], Hanging(1)),
            1 => (Hanging(-1), reported[0]),
            _ => (Hanging(-1), Hanging(1)),
        };
    }

    /// <summary>
    /// Two-bone IK: the elbow for a shoulder reaching to a hand, bent towards <paramref name="pole"/> (down and outward). A
    /// hand past the arm's length is reached for as far as it goes; returns the hand where it ends up.
    /// </summary>
    public static (Double3 Elbow, Double3 Hand) Solve(Double3 shoulder, Double3 target, Double3 pole)
    {
        var to = target - shoulder;
        double d = to.Length;
        if (d < 1e-6)
            return (shoulder + new Double3(0, -Upper, 0), target);
        var dir = to * (1 / d);
        double reach = Math.Clamp(d, Math.Abs(Upper - Lower) + 1e-3, (Upper + Lower) * 0.999);
        var hand = shoulder + dir * reach;
        // Along the reach, a from the shoulder; then out towards the pole by h (law of cosines).
        double a = (Upper * Upper - Lower * Lower + reach * reach) / (2 * reach);
        double h = Math.Sqrt(Math.Max(0, Upper * Upper - a * a));
        var bend = pole - dir * Double3.Dot(pole, dir);
        if (bend.Length < 1e-6)
            bend = Math.Abs(dir.Y) < 0.9 ? new Double3(0, -1, 0) - dir * -dir.Y : new Double3(1, 0, 0);
        return (shoulder + dir * a + bend.Normalized * h, hand);
    }

    /// <summary>The bend for an arm on a side: the elbow goes down, out, and a little back.</summary>
    public static Double3 Pole(int side) => new(side * 0.6, -1, 0.3);
}
