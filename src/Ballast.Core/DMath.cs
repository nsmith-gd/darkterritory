namespace Ballast;

/// <summary>
/// Trigonometry that gives the same bits on every machine. <c>Math.Sin</c>, <c>Atan2</c> and the rest call the platform's
/// C library (the UCRT on Windows, glibc on Linux), and those differ in the last bit for some inputs. That's enough to
/// split a line plan (the joiner regenerates the host's night and is refused on a fingerprint mismatch, T116) and to
/// drift a predicted train. These are fdlibm's algorithms (what Java's StrictMath uses), written with nothing but
/// IEEE-754 + - * / and square root, which every x64 and arm64 machine rounds alike, and no fused multiply-add (the JIT
/// never contracts one on its own). Error is under 1 ulp, like the C libraries'; only the agreement is new.
/// <para>
/// Anything that changes player or train state, or goes into a line plan, uses these rather than <c>Math</c>'s
/// (<c>DeterministicMathTests</c> holds DarkTerritory.Sim to it). Presentation is free to use <c>Math</c>.
/// </para>
/// </summary>
/// <remarks>
/// Ported from fdlibm 5.3 (k_sin.c, k_cos.c, e_rem_pio2.c, s_sin.c, s_cos.c, s_atan.c, e_atan2.c, e_asin.c, e_acos.c):
/// Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved. Developed at SunSoft, a Sun Microsystems, Inc.
/// business. Permission to use, copy, modify, and distribute this software is freely granted, provided that this
/// notice is preserved.
/// </remarks>
public static class DMath
{
    public const double PI = Math.PI;
    public const double Tau = Math.Tau;

    static int Hi(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);
    static uint Lo(double x) => (uint)BitConverter.DoubleToInt64Bits(x);
    static double WithLoZero(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) & unchecked((long)0xFFFFFFFF00000000UL));

    public static double Sin(double x)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix <= 0x3fe921fb) return KernelSin(x, 0, false); // |x| <= pi/4
        if (ix >= 0x7ff00000) return x - x;                   // inf or NaN
        int n = RemPio2(x, out double y0, out double y1);
        return (n & 3) switch
        {
            0 => KernelSin(y0, y1, true),
            1 => KernelCos(y0, y1),
            2 => -KernelSin(y0, y1, true),
            _ => -KernelCos(y0, y1),
        };
    }

    public static double Cos(double x)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix <= 0x3fe921fb) return KernelCos(x, 0);
        if (ix >= 0x7ff00000) return x - x;
        int n = RemPio2(x, out double y0, out double y1);
        return (n & 3) switch
        {
            0 => KernelCos(y0, y1),
            1 => -KernelSin(y0, y1, true),
            2 => -KernelCos(y0, y1),
            _ => KernelSin(y0, y1, true),
        };
    }

    /// <summary>Sine over cosine: within a couple of ulp, which is all a slope or a field of view needs.</summary>
    public static double Tan(double x) => Sin(x) / Cos(x);

    public static double Atan(double x)
    {
        int hx = Hi(x), ix = hx & 0x7fffffff;
        int id;
        if (ix >= 0x44100000) // |x| >= 2^66
        {
            if (ix > 0x7ff00000 || (ix == 0x7ff00000 && Lo(x) != 0)) return x + x; // NaN
            return hx > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3];
        }
        if (ix < 0x3fdc0000) // |x| < 0.4375
        {
            if (ix < 0x3e200000) return x; // |x| < 2^-29
            id = -1;
        }
        else
        {
            x = Math.Abs(x);
            if (ix < 0x3ff30000) // |x| < 1.1875
            {
                if (ix < 0x3fe60000) { id = 0; x = (2.0 * x - 1.0) / (2.0 + x); } // 7/16 <= |x| < 11/16
                else { id = 1; x = (x - 1.0) / (x + 1.0); }                      // 11/16 <= |x| < 19/16
            }
            else if (ix < 0x40038000) { id = 2; x = (x - 1.5) / (1.0 + 1.5 * x); } // |x| < 2.4375
            else { id = 3; x = -1.0 / x; }                                         // 2.4375 <= |x| < 2^66
        }
        double z = x * x, w = z * z;
        double s1 = z * (AT0 + w * (AT2 + w * (AT4 + w * (AT6 + w * (AT8 + w * AT10)))));
        double s2 = w * (AT1 + w * (AT3 + w * (AT5 + w * (AT7 + w * AT9))));
        if (id < 0) return x - x * (s1 + s2);
        z = AtanHi[id] - ((x * (s1 + s2) - AtanLo[id]) - x);
        return hx < 0 ? -z : z;
    }

    public static double Atan2(double y, double x)
    {
        int hx = Hi(x), ix = hx & 0x7fffffff; uint lx = Lo(x);
        int hy = Hi(y), iy = hy & 0x7fffffff; uint ly = Lo(y);
        if (double.IsNaN(x) || double.IsNaN(y)) return x + y;
        if (hx == 0x3ff00000 && lx == 0) return Atan(y); // x = 1
        int m = ((hy >> 31) & 1) | ((hx >> 30) & 2);     // 2 * sign(x) + sign(y)

        if (iy == 0 && ly == 0) // y = 0
            return m switch { 0 or 1 => y, 2 => Pi, _ => -Pi };
        if (ix == 0 && lx == 0) return hy < 0 ? -PiO2 : PiO2; // x = 0
        if (ix == 0x7ff00000) // x is infinite
        {
            if (iy == 0x7ff00000)
                return m switch { 0 => PiO4, 1 => -PiO4, 2 => 3.0 * PiO4, _ => -3.0 * PiO4 };
            return m switch { 0 => 0.0, 1 => -0.0, 2 => Pi, _ => -Pi };
        }
        if (iy == 0x7ff00000) return hy < 0 ? -PiO2 : PiO2; // y is infinite

        int k = (iy - ix) >> 20;
        double z;
        if (k > 60) z = PiO2 + 0.5 * PiLo;      // |y/x| > 2^60
        else if (hx < 0 && k < -60) z = 0.0;    // |y|/x < -2^60
        else z = Atan(Math.Abs(y / x));
        return m switch
        {
            0 => z,
            1 => -z,
            2 => Pi - (z - PiLo),
            _ => (z - PiLo) - Pi,
        };
    }

    public static double Asin(double x)
    {
        int hx = Hi(x), ix = hx & 0x7fffffff;
        if (ix >= 0x3ff00000) // |x| >= 1
        {
            if (((ix - 0x3ff00000) | (int)Lo(x)) == 0) return x * Pio2Hi + x * Pio2Lo; // asin(+-1) = +-pi/2
            return double.NaN;
        }
        if (ix < 0x3fe00000) // |x| < 0.5
        {
            if (ix < 0x3e400000) return x; // |x| < 2^-27
            double t2 = x * x;
            return x + x * (R(t2) / Q(t2));
        }
        // 0.5 <= |x| < 1
        double w = 1.0 - Math.Abs(x), t = w * 0.5;
        double p = R(t), q = Q(t), s = Math.Sqrt(t);
        if (ix >= 0x3FEF3333) // |x| > 0.975
        {
            t = Pio2Hi - (2.0 * (s + s * (p / q)) - Pio2Lo);
        }
        else
        {
            w = WithLoZero(s);
            double c = (t - w * w) / (s + w);
            double r = p / q;
            p = 2.0 * s * r - (Pio2Lo - 2.0 * c);
            q = Pio4Hi - 2.0 * w;
            t = Pio4Hi - (p - q);
        }
        return hx > 0 ? t : -t;
    }

    public static double Acos(double x)
    {
        int hx = Hi(x), ix = hx & 0x7fffffff;
        if (ix >= 0x3ff00000) // |x| >= 1
        {
            if (((ix - 0x3ff00000) | (int)Lo(x)) == 0) return hx > 0 ? 0.0 : Pi + 2.0 * Pio2Lo;
            return double.NaN;
        }
        if (ix < 0x3fe00000) // |x| < 0.5
        {
            if (ix <= 0x3c600000) return Pio2Hi + Pio2Lo; // |x| < 2^-57
            double z2 = x * x;
            return Pio2Hi - (x - (Pio2Lo - x * (R(z2) / Q(z2))));
        }
        if (hx < 0) // x < -0.5
        {
            double z = (1.0 + x) * 0.5, s = Math.Sqrt(z);
            double w = (R(z) / Q(z)) * s - Pio2Lo;
            return Pi - 2.0 * (s + w);
        }
        else // x > 0.5
        {
            double z = (1.0 - x) * 0.5, s = Math.Sqrt(z);
            double df = WithLoZero(s);
            double c = (z - df * df) / (s + df);
            double w = (R(z) / Q(z)) * s + c;
            return 2.0 * (df + w);
        }
    }

    /// <summary><paramref name="x"/> to a whole power by squaring: exact wherever the true answer is representable.</summary>
    public static double Pow(double x, int n)
    {
        if (n < 0) return 1.0 / Pow(x, -n);
        double r = 1.0;
        while (n > 0)
        {
            if ((n & 1) != 0) r *= x;
            x *= x;
            n >>= 1;
        }
        return r;
    }

    // k_sin.c: sin on [-pi/4, pi/4]; y is the tail of x, and only counts when hasTail.
    static double KernelSin(double x, double y, bool hasTail)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix < 0x3e400000 && (int)x == 0) return x; // |x| < 2^-27
        double z = x * x, v = z * x;
        double r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
        if (!hasTail) return x + v * (S1 + z * r);
        return x - ((z * (0.5 * y - v * r) - y) - v * S1);
    }

    // k_cos.c: cos on [-pi/4, pi/4].
    static double KernelCos(double x, double y)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix < 0x3e400000 && (int)x == 0) return 1.0; // |x| < 2^-27
        double z = x * x;
        double r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
        if (ix < 0x3FD33333) return 1.0 - (0.5 * z - (z * r - x * y)); // |x| < 0.3
        double qx = ix > 0x3fe90000 ? 0.28125 : BitConverter.Int64BitsToDouble((long)(ix - 0x00200000) << 32); // x/4
        double hz = 0.5 * z - qx, a = 1.0 - qx;
        return a - (hz - (z * r - x * y));
    }

    // e_rem_pio2.c's paths for |x| up to 2^19 pi/2 (Cody-Waite against pi/2 in three 33-bit parts): x - n pi/2 as
    // y0 + y1, returning n. fdlibm's table-driven reduction for larger x isn't ported; nothing on a line reaches it, and
    // past it x is first brought down by whole turns, still with nothing but + - * (so still the same everywhere).
    static int RemPio2(double x, out double y0, out double y1)
    {
        int hx = Hi(x), ix = hx & 0x7fffffff;
        if (ix < 0x4002d97c) // |x| < 3pi/4: n = +-1
        {
            if (hx > 0)
            {
                double z = x - Pio2_1;
                if (ix != 0x3ff921fb) { y0 = z - Pio2_1t; y1 = (z - y0) - Pio2_1t; }
                else { z -= Pio2_2; y0 = z - Pio2_2t; y1 = (z - y0) - Pio2_2t; } // near pi/2: 33+33+53 bits
                return 1;
            }
            else
            {
                double z = x + Pio2_1;
                if (ix != 0x3ff921fb) { y0 = z + Pio2_1t; y1 = (z - y0) + Pio2_1t; }
                else { z += Pio2_2; y0 = z + Pio2_2t; y1 = (z - y0) + Pio2_2t; }
                return -1;
            }
        }
        if (ix > 0x413921fb) // beyond the medium range: take off whole turns first
        {
            double turns = Math.Floor(x / Tau);
            x = (x - turns * TauHi) - turns * TauLo;
            hx = Hi(x); ix = hx & 0x7fffffff;
            if (ix <= 0x3fe921fb) { y0 = x; y1 = 0; return 0; }
            if (ix < 0x4002d97c) return RemPio2(x, out y0, out y1);
        }
        double t = Math.Abs(x);
        int n = (int)(t * InvPio2 + 0.5);
        double fn = n;
        double r = t - fn * Pio2_1;
        double w = fn * Pio2_1t; // first round, good to 85 bits
        int j = ix >> 20;
        y0 = r - w;
        int i = j - ((Hi(y0) >> 20) & 0x7ff);
        if (i > 16) // second round, good to 118 bits
        {
            t = r;
            w = fn * Pio2_2;
            r = t - w;
            w = fn * Pio2_2t - ((t - r) - w);
            y0 = r - w;
            i = j - ((Hi(y0) >> 20) & 0x7ff);
            if (i > 49) // third round, 151 bits
            {
                t = r;
                w = fn * Pio2_3;
                r = t - w;
                w = fn * Pio2_3t - ((t - r) - w);
                y0 = r - w;
            }
        }
        y1 = (r - y0) - w;
        if (hx < 0) { y0 = -y0; y1 = -y1; return -n; }
        return n;
    }

    // asin/acos's rational approximation of (asin(x) - x) / x^3 in x^2: R/Q.
    static double R(double t) => t * (PS0 + t * (PS1 + t * (PS2 + t * (PS3 + t * (PS4 + t * PS5)))));
    static double Q(double t) => 1.0 + t * (QS1 + t * (QS2 + t * (QS3 + t * QS4)));

    // The constants, as fdlibm gives them (each decimal is the exact double of the hex pattern beside it there).
    const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
        S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
    const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
        C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;
    const double InvPio2 = 6.36619772367581382433e-01,
        Pio2_1 = 1.57079632673412561417e+00, Pio2_1t = 6.07710050650619224932e-11,
        Pio2_2 = 6.07710050630396597660e-11, Pio2_2t = 2.02226624879595063154e-21,
        Pio2_3 = 2.02226624871116645580e-21, Pio2_3t = 8.47842766036889956997e-32;
    // 2 pi as a 33-bit head (so turns * TauHi is exact for any turn count that fits 20 bits) and the rest.
    const double TauHi = 2 * Pio2_1 * 2, TauLo = 2 * Pio2_1t * 2;
    static readonly double[] AtanHi = [4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00];
    static readonly double[] AtanLo = [2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17];
    const double AT0 = 3.33333333333329318027e-01, AT1 = -1.99999999998764832476e-01, AT2 = 1.42857142725034663711e-01,
        AT3 = -1.11111104054623557880e-01, AT4 = 9.09088713343650656196e-02, AT5 = -7.69187620504482999495e-02,
        AT6 = 6.66107313738753120669e-02, AT7 = -5.83357013379057348645e-02, AT8 = 4.97687799461593236017e-02,
        AT9 = -3.65315727442169155270e-02, AT10 = 1.62858201153657823623e-02;
    const double Pi = 3.1415926535897931160E+00, PiO2 = 1.5707963267948965580E+00, PiO4 = 7.8539816339744827900E-01,
        PiLo = 1.2246467991473531772E-16;
    const double Pio2Hi = 1.57079632679489655800e+00, Pio2Lo = 6.12323399573676603587e-17, Pio4Hi = 7.85398163397448278999e-01;
    const double PS0 = 1.66666666666666657415e-01, PS1 = -3.25565818622400915405e-01, PS2 = 2.01212532134862925881e-01,
        PS3 = -4.00555345006794114027e-02, PS4 = 7.91534994289814532176e-04, PS5 = 3.47933107596021167570e-05,
        QS1 = -2.40339491173441421878e+00, QS2 = 2.02094576023350569471e+00, QS3 = -6.88283971605453293030e-01,
        QS4 = 7.70381505559019352791e-02;
}
