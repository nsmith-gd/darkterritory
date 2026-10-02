namespace Ballast.Core.Tests;

/// <summary>
/// <see cref="DMath"/>: as accurate as the C library, and the same bits on every OS. CI runs these on Linux and Windows,
/// so the pinned bit patterns are the cross-play check at its root (the crossplay job compares whole line plans).
/// </summary>
public class DMathTests
{
    static long Ulps(double a, double b)
    {
        if (a == b) return 0;
        long ia = BitConverter.DoubleToInt64Bits(a), ib = BitConverter.DoubleToInt64Bits(b);
        if (ia < 0) ia = long.MinValue - ia;
        if (ib < 0) ib = long.MinValue - ib;
        return Math.Abs(ia - ib);
    }

    // Seeded inputs over a spread of magnitudes: tiny, the kernels' ranges, a line's distances as phases, far out.
    static IEnumerable<double> Inputs(double scale, int count = 200_000)
    {
        var rng = new Pcg32(7, 11);
        for (int i = 0; i < count; i++)
        {
            double u = rng.NextDouble() * 2 - 1;
            double mag = Math.Exp(rng.NextDouble() * Math.Log(scale * 1e9)) / 1e9; // log-uniform up to scale
            yield return u * mag;
        }
    }

    static void Close(Func<double, double> ours, Func<double, double> theirs, IEnumerable<double> xs, long tolerance = 2)
    {
        long worst = 0; double at = 0;
        foreach (double x in xs)
        {
            long u = Ulps(ours(x), theirs(x));
            if (u > worst) { worst = u; at = x; }
        }
        Assert.True(worst <= tolerance, $"{worst} ulp at {at:R}: ours {ours(at):R}, Math's {theirs(at):R}");
    }

    // Within 2 ulp of the platform's: glibc is all but correctly rounded, the UCRT within an ulp, and so are we.
    [Fact] public void SinMatchesTheLibrary() => Close(DMath.Sin, Math.Sin, Inputs(2e5));
    [Fact] public void CosMatchesTheLibrary() => Close(DMath.Cos, Math.Cos, Inputs(2e5));
    [Fact] public void AtanMatchesTheLibrary() => Close(DMath.Atan, Math.Atan, Inputs(1e6));
    [Fact] public void AsinMatchesTheLibrary() => Close(DMath.Asin, Math.Asin, Inputs(1));
    [Fact] public void AcosMatchesTheLibrary() => Close(DMath.Acos, Math.Acos, Inputs(1));
    [Fact] public void TanIsCloseToTheLibrary() => Close(DMath.Tan, Math.Tan, Inputs(1.5), tolerance: 4);

    [Fact]
    public void Atan2MatchesTheLibrary()
    {
        var ys = Inputs(1e5, 100_000).ToArray();
        var xs = Inputs(1e3, 100_000).Reverse().ToArray();
        long worst = 0;
        for (int i = 0; i < ys.Length; i++)
            worst = Math.Max(worst, Ulps(DMath.Atan2(ys[i], xs[i]), Math.Atan2(ys[i], xs[i])));
        Assert.True(worst <= 2, $"{worst} ulp");
    }

    [Fact]
    public void EdgesAreTheLibrarys()
    {
        double[] specials = [0.0, -0.0, 1.0, -1.0, double.PositiveInfinity, double.NegativeInfinity, 5e-324, 1e-300];
        foreach (double y in specials)
            foreach (double x in specials)
                Assert.Equal(Math.Atan2(y, x), DMath.Atan2(y, x), 15);
        Assert.True(double.IsNaN(DMath.Sin(double.PositiveInfinity)));
        Assert.True(double.IsNaN(DMath.Cos(double.NaN)));
        Assert.True(double.IsNaN(DMath.Asin(1.5)));
        Assert.True(double.IsNaN(DMath.Atan2(double.NaN, 1)));
        Assert.Equal(Math.PI / 2, DMath.Asin(1));
        Assert.Equal(Math.PI, DMath.Acos(-1));
        Assert.Equal(0.0, DMath.Acos(1));
        Assert.Equal(Math.PI / 2, DMath.Atan(double.PositiveInfinity));
    }

    // Past the medium reduction range nothing's promised but agreement with itself (and a sane value).
    [Fact]
    public void FarArgumentsStayInRange()
    {
        foreach (double x in new[] { 1e7, -3.3e9, 7.5e12 })
        {
            Assert.InRange(DMath.Sin(x), -1, 1);
            Assert.InRange(DMath.Cos(x), -1, 1);
            Assert.Equal(1, DMath.Sin(x) * DMath.Sin(x) + DMath.Cos(x) * DMath.Cos(x), 12);
        }
    }

    [Fact]
    public void WholePowersAreExact()
    {
        Assert.Equal(1024.0, DMath.Pow(2, 10));
        Assert.Equal(1.0 / 8, DMath.Pow(2, -3));
        Assert.Equal(1.0, DMath.Pow(1.24, 0));
        Assert.Equal(1.24 * 1.24 * 1.24, DMath.Pow(1.24, 3), 15);
        Assert.Equal(Math.Pow(1.24, 16), DMath.Pow(1.24, 16), 12);
    }

    /// <summary>
    /// The bits themselves, through every branch (the kernels' small arguments, one quadrant per case, the near-pi/2
    /// reduction, the second and third Cody-Waite rounds, atan's five intervals, atan2's quadrants, asin's and acos's
    /// three regions). A Windows run that differs from Linux in any of these fails here.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pinned))]
    public void TheSameBitsEverywhere(string fn, double x, double y, ulong bits)
    {
        double r = fn switch
        {
            "sin" => DMath.Sin(x),
            "cos" => DMath.Cos(x),
            "tan" => DMath.Tan(x),
            "atan" => DMath.Atan(x),
            "atan2" => DMath.Atan2(y, x),
            "asin" => DMath.Asin(x),
            "acos" => DMath.Acos(x),
            _ => throw new ArgumentException(fn),
        };
        Assert.Equal(bits.ToString("X16"), BitConverter.DoubleToUInt64Bits(r).ToString("X16"));
    }

    public static TheoryData<string, double, double, ulong> Pinned => new()
    {
        { "sin", 1E-09, 0, 0x3E112E0BE826D695UL },
        { "sin", 0.5, 0, 0x3FDEAEE8744B05F0UL },
        { "sin", 0.785, 0, 0x3FE69E4FD79AC743UL },
        { "sin", 1, 0, 0x3FEAED548F090CEEUL },
        { "sin", 1.5707963267948966, 0, 0x3FF0000000000000UL },
        { "sin", 2, 0, 0x3FED18F6EAD1B446UL },
        { "sin", -2.3, 0, 0xBFE7DCD12D582F24UL },
        { "sin", 3.14159, 0, 0x3EC6428A6AA44CD0UL },
        { "sin", 4.5, 0, 0xBFEF47ED3DC74080UL },
        { "sin", -5.9, 0, 0x3FD7ED98640BBD1BUL },
        { "sin", 100, 0, 0xBFE03425B78C4DB8UL },
        { "sin", 1234.5678, 0, 0x3FB3FA00084EE3DDUL },
        { "sin", 355, 0, 0xBEFF9BD0307D1DE3UL },
        { "sin", 103993, 0, 0xBEF40EFDF1EB8DE7UL },
        { "sin", -48213.25, 0, 0xBFE65528A30ADBB1UL },
        { "sin", 800000, 0, 0xBFD20F02E8178612UL },
        { "sin", 33000000, 0, 0x3FE6339D4B3B3C20UL },
        { "cos", 1E-09, 0, 0x3FF0000000000000UL },
        { "cos", 0.2, 0, 0x3FEF5CB49577627AUL },
        { "cos", 0.5, 0, 0x3FEC1528065B7D50UL },
        { "cos", 0.78, 0, 0x3FE6BFCDBF817BFAUL },
        { "cos", 1, 0, 0x3FE14A280FB5068CUL },
        { "cos", -2, 0, 0xBFDAA22657537205UL },
        { "cos", 3, 0, 0xBFEFAE04BE85E5D2UL },
        { "cos", 4.7, 0, 0xBF895F3A43506A34UL },
        { "cos", 6, 0, 0x3FEEB9B7097822F6UL },
        { "cos", 710.5, 0, 0x3FEC14EB67D03EAAUL },
        { "cos", -31415.9, 0, 0x3FEFFD1DAF394611UL },
        { "cos", 5500000, 0, 0x3FD8ACE4954D3A59UL },
        { "tan", 0.3, 0, 0x3FD3CC2A44E29997UL },
        { "tan", -1.2, 0, 0xC00493C43ACB164CUL },
        { "tan", 1.5, 0, 0x402C33ED50B88778UL },
        { "atan", 1E-10, 0, 0x3DDB7CDFD9D7BDBBUL },
        { "atan", 0.3, 0, 0x3FD2A73A661EAF06UL },
        { "atan", -0.5, 0, 0xBFDDAC670561BB4FUL },
        { "atan", 0.9, 0, 0x3FE77338A80603BEUL },
        { "atan", 1.7, 0, 0x3FF0A00A3BCE369FUL },
        { "atan", -3, 0, 0xBFF3FC176B7A8560UL },
        { "atan", 1E+20, 0, 0x3FF921FB54442D18UL },
        { "asin", 1E-09, 0, 0x3E112E0BE826D695UL },
        { "asin", 0.3, 0, 0x3FD380159E14F6FFUL },
        { "asin", -0.7, 0, 0xBFE8D00E692AFD95UL },
        { "asin", 0.99, 0, 0x3FF6DE3C6F33D51DUL },
        { "acos", 1E-20, 0, 0x3FF921FB54442D18UL },
        { "acos", 0.3, 0, 0x3FF441F5ECBEEF59UL },
        { "acos", -0.7, 0, 0x4002C501446CD5F2UL },
        { "acos", 0.8, 0, 0x3FE4978FA3269EE0UL },
        { "acos", -0.999, 0, 0x4008C66280AE928EUL },
        { "atan2", 2, 1, 0x3FDDAC670561BB4FUL },
        { "atan2", -0.5, 3, 0x3FFBC66E44CBC074UL },
        { "atan2", -7, -2, 0xC006E8062854DB5EUL },
        { "atan2", 4, -0.25, 0xBFAFF55BB72CFDEAUL },
        { "atan2", -1, 1E-200, 0x400921FB54442D18UL },
        { "atan2", 1E-70, 5, 0x3FF921FB54442D18UL },
        { "atan2", -3, 0, 0x400921FB54442D18UL },
        { "atan2", 1, 12.5, 0x3FF7DAFF85A63058UL },
    };
}
