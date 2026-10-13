using System.Numerics;
using Ballast.Assets;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The survivors' figure's surface in its bind pose, as how far out it is from an upright axis at each height and bearing:
/// what the townsfolk's clothes are made to (note 694), so a hat sits on the skull it's on, a mask on the face, an apron on
/// the belly, not on an ellipse near them. The bearing is <see cref="LookbookKit"/>'s: 0 behind (+Z), π in front (−Z),
/// +π/2 the figure's left (+X)... its x = sin(a)·r, z = axis + cos(a)·r.
/// </summary>
public sealed class FigureSurface
{
    /// <summary>The height bins (m) and the bearings round.</summary>
    const float Step = 0.01f;
    const int Bearings = 48;

    readonly float[,] _r;
    readonly float _y0;
    readonly int _rows;

    /// <summary>The axis's Z (m): the surface's bearings are taken round (0, y, this).</summary>
    public float AxisZ { get; }

    FigureSurface(float[,] r, float y0, float axisZ)
    {
        _r = r;
        _y0 = y0;
        _rows = r.GetLength(0);
        AxisZ = axisZ;
    }

    /// <summary>
    /// The trunk and legs (the arms, out in the T-pose, left out): its frame, and its coat when <paramref name="coat"/>
    /// (the coat wearers' clothes go over that), reshaped by <paramref name="shape"/> as the character's figure is.
    /// </summary>
    public static FigureSurface Body(Model figure, bool coat, Func<Vector3, string, Vector3>? shape)
    {
        var bones = figure.Skeleton.Names;
        var points = new List<Vector3>();
        foreach (var part in figure.Parts)
        {
            bool isCoat = part.Name.Contains("coat", StringComparison.Ordinal);
            if (!(part.Name.Contains("frame", StringComparison.Ordinal) || coat && isCoat) || part.Clip is not null)
                continue;
            Sample(part, points, (p, bone) =>
            {
                if (bone.StartsWith("upperarm", StringComparison.Ordinal) || bone.StartsWith("lowerarm", StringComparison.Ordinal)
                    || bone.StartsWith("hand", StringComparison.Ordinal) || bone.StartsWith("finger", StringComparison.Ordinal)
                    || bone.StartsWith("thumb", StringComparison.Ordinal) || bone.StartsWith("head", StringComparison.Ordinal))
                    return null;
                var q = shape is null ? p : shape(p, bone);
                // The shoulders' tops, not the arms going out from them.
                return q.Y > 1.2f && MathF.Abs(q.X) > 0.24f ? null : q;
            }, bones);
        }
        return Of(points, -0.015f, 0, 1.6f, 2);
    }

    /// <summary>The head (the face's scan, its skull), round the head's own middle.</summary>
    public static FigureSurface Head(Model figure)
    {
        var bones = figure.Skeleton.Names;
        var points = new List<Vector3>();
        foreach (var part in figure.Parts)
        {
            if (!(part.Name == "body" || part.Name.EndsWith("_body", StringComparison.Ordinal)) || part.Clip is not null)
                continue;
            Sample(part, points, (p, bone) => bone.StartsWith("head", StringComparison.Ordinal) || bone == "neck" ? p : null, bones);
        }
        return Of(points, -0.015f, 1.45f, 1.81f, 1);
    }

    /// <summary>Points over each of the part's triangles about <see cref="Grain"/> apart (its vertices alone are too few: the
    /// face's scan is a few hundred), each kept as <paramref name="keep"/> moves it (null: left out), by its first corner's bone.</summary>
    static void Sample(MeshPart part, List<Vector3> points, Func<Vector3, string, Vector3?> keep, string[] bones)
    {
        var idx = part.Indices;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            Vector3 a = part.Positions[idx[t]], b = part.Positions[idx[t + 1]], c = part.Positions[idx[t + 2]];
            string bone = bones[Dominant(part, idx[t])];
            float longest = MathF.Max((b - a).Length(), MathF.Max((c - b).Length(), (a - c).Length()));
            int n = Math.Clamp((int)MathF.Ceiling(longest / Grain), 1, 24);
            for (int i = 0; i <= n; i++)
                for (int j = 0; i + j <= n; j++)
                    if (keep(a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n), bone) is { } q)
                        points.Add(q);
        }
    }

    const float Grain = 0.006f;

    static FigureSurface Of(List<Vector3> points, float axisZ, float y0, float y1, int blur)
    {
        int rows = (int)MathF.Ceiling((y1 - y0) / Step) + 1;
        var r = new float[rows, Bearings];
        foreach (var p in points)
        {
            int row = (int)MathF.Round((p.Y - y0) / Step);
            if (row < 0 || row >= rows)
                continue;
            float d = MathF.Sqrt(p.X * p.X + (p.Z - axisZ) * (p.Z - axisZ));
            int b = Bin(MathF.Atan2(p.X, p.Z - axisZ));
            r[row, b] = MathF.Max(r[row, b], d);
        }
        // Bins no vertex fell in: from their row's neighbours round (a coarse scan leaves gaps), then from the rows by.
        for (int row = 0; row < rows; row++)
            for (int pass = 0; pass < Bearings; pass++)
            {
                bool any = false;
                for (int b = 0; b < Bearings; b++)
                    if (r[row, b] == 0)
                    {
                        float l = r[row, (b + Bearings - 1) % Bearings], h = r[row, (b + 1) % Bearings];
                        r[row, b] = l > 0 && h > 0 ? (l + h) / 2 : MathF.Max(l, h);
                        any |= r[row, b] == 0;
                    }
                if (!any)
                    break;
            }
        for (int row = 0; row < rows; row++)
            if (Empty(r, row))
                for (int b = 0; b < Bearings; b++)
                    r[row, b] = row > 0 ? r[row - 1, b] : 0;
        // A vertex's furthest is noisy (a fold, a button): smoothed round and up.
        for (int pass = 0; pass < blur; pass++)
        {
            var s = new float[rows, Bearings];
            for (int row = 0; row < rows; row++)
                for (int b = 0; b < Bearings; b++)
                {
                    float sum = 0, w = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int db = -1; db <= 1; db++)
                        {
                            int rr = Math.Clamp(row + dr, 0, rows - 1);
                            float k = (dr == 0 ? 2 : 1) * (db == 0 ? 2 : 1);
                            sum += r[rr, (b + db + Bearings) % Bearings] * k;
                            w += k;
                        }
                    s[row, b] = MathF.Max(r[row, b] * 0.85f, sum / w);
                }
            r = s;
        }
        return new FigureSurface(r, y0, axisZ);
    }

    static bool Empty(float[,] r, int row)
    {
        for (int b = 0; b < Bearings; b++)
            if (r[row, b] > 0)
                return false;
        return true;
    }

    static int Bin(float bearing) => ((int)MathF.Round(bearing / MathF.Tau * Bearings) % Bearings + Bearings) % Bearings;

    /// <summary>How far out the surface is (m) at height <paramref name="y"/> and <paramref name="bearing"/>, between the bins.</summary>
    public float Radius(float y, float bearing)
    {
        float fy = Math.Clamp((y - _y0) / Step, 0, _rows - 1);
        int r0 = (int)fy, r1 = Math.Min(r0 + 1, _rows - 1);
        float ty = fy - r0;
        float fb = bearing / MathF.Tau * Bearings;
        fb = (fb % Bearings + Bearings) % Bearings;
        int b0 = (int)fb % Bearings, b1 = (b0 + 1) % Bearings;
        float tb = fb - (int)fb;
        float lo = _r[r0, b0] + (_r[r0, b1] - _r[r0, b0]) * tb, hi = _r[r1, b0] + (_r[r1, b1] - _r[r1, b0]) * tb;
        return lo + (hi - lo) * ty;
    }

    /// <summary>The point on the surface at <paramref name="y"/> and <paramref name="bearing"/>, <paramref name="out"/> m out from it.</summary>
    public Vector3 At(float y, float bearing, float @out = 0)
    {
        float r = Radius(y, bearing) + @out;
        return new Vector3(MathF.Sin(bearing) * r, y, AxisZ + MathF.Cos(bearing) * r);
    }

    /// <summary>A ring round the surface at <paramref name="y"/>, <paramref name="out"/> m out, from bearing <paramref name="from"/>
    /// to <paramref name="to"/> (all the way round, open: <paramref name="closed"/>).</summary>
    public Vector3[] Ring(float y, float @out, int n, float from = -MathF.PI, float to = MathF.PI, bool closed = true)
    {
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
            ring[i] = At(y, from + (to - from) * i / (closed ? n : n - 1), @out);
        return ring;
    }

    static int Dominant(MeshPart part, int v)
    {
        int k = v * 4, best = 0;
        for (int j = 1; j < 4; j++)
            if (part.Weights[k + j] > part.Weights[k + best])
                best = j;
        return part.Joints[k + best];
    }
}
