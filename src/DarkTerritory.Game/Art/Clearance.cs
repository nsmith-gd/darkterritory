using System.Numerics;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A figure's clips checked for limbs through its body (Look Review notes: "arms through body", "arm through knee",
/// "arm through head"; ARCHITECTURE §8 note 256). Every clip is sampled at 30 fps; its limbs are capsules round their
/// bones, sized to the crew's coat, sleeves and boots (measured off crew.glb: about 0.2 m round the chest and the hips,
/// the skirt 0.22 m to the knee), measured against the trunk, the coat's skirt, the head and the other limbs.
/// </summary>
public static class Clearance
{
    /// <summary>How deep a pair can overlap and still read as touching (an arm against the coat's side, a forearm on a
    /// knee), not through: round capsules round a coat are that rough (m).</summary>
    public const float Touching = 0.06f;

    /// <summary>One clip's deepest overlap of one pair of parts, and when (s).</summary>
    public readonly record struct Overlap(string Clip, string Pair, float Depth, double At);

    readonly record struct Part(string Name, int A, int Z, float R, float From);

    /// <summary>Every clip's (or <paramref name="clips"/>') worst overlap per pair of parts, deepest first.</summary>
    public static List<Overlap> Check(CreatureArt art, string name, IReadOnlyCollection<string>? clips = null)
    {
        var model = art.Get(name) ?? throw new ArgumentException($"no model {name}");
        var names = model.Skeleton.Names;
        int B(string bone) => Array.IndexOf(names, bone) is var i and >= 0 ? i : throw new ArgumentException($"{name} has no {bone}");
        Part[] Side(string s) =>
        [
            new($"upperarm_{s}", B($"upperarm_{s}"), B($"lowerarm_{s}"), 0.055f, 0.45f),
            new($"forearm_{s}", B($"lowerarm_{s}"), B($"hand_{s}"), 0.05f, 0),
            new($"hand_{s}", B($"hand_{s}"), B($"fingers_{s}"), 0.045f, 0),
            new($"thigh_{s}", B($"thigh_{s}"), B($"calf_{s}"), 0.08f, 0.3f),
            new($"calf_{s}", B($"calf_{s}"), B($"foot_{s}"), 0.06f, 0),
        ];
        var limbs = Side("r").Concat(Side("l")).ToArray();
        Part[] trunk =
        [
            new("hips", B("pelvis"), B("spine_02"), 0.18f, 0.2f), new("chest", B("spine_02"), B("neck"), 0.17f, 0),
            new("head", B("head"), B("head"), 0.12f, 0), new("skirt", B("pelvis"), B("pelvis"), 0.2f, 0),
        ];
        int neck = B("neck"), spine = B("spine_02");
        static bool Leg(Part p) => p.Name.StartsWith("thigh", StringComparison.Ordinal) || p.Name.StartsWith("calf", StringComparison.Ordinal);
        var found = new List<Overlap>();
        foreach (var clipName in model.Clips.Keys.Order(StringComparer.Ordinal))
        {
            if (clips is not null && !clips.Contains(clipName))
                continue;
            var c = model.Clip(clipName)!;
            var worst = new Dictionary<string, (float Depth, double At)>();
            for (int f = 0; f <= (int)Math.Round(c.Duration * 30); f++)
            {
                double t = f / 30.0;
                var j = art.Joints(name, clipName, t, c.Loops).ToArray();
                (Vector3, Vector3) Seg(Part k)
                {
                    if (k.Name == "skirt")
                    {
                        // Hung from the hips down the line of the body, to the knee.
                        var down = Vector3.Normalize(j[k.A] - j[spine]);
                        return (j[k.A], j[k.A] + down * 0.4f);
                    }
                    if (k.Name == "head")
                    {
                        // The head's ball over its joint, up the line of the neck.
                        var centre = j[k.A] + Vector3.Normalize(j[k.A] - j[neck]) * 0.1f;
                        return (centre, centre);
                    }
                    var a = j[k.A];
                    var z = j[k.Z];
                    if (k.Name.StartsWith("hand", StringComparison.Ordinal))
                        z = a + Vector3.Normalize(z - a) * 0.1f;
                    return (a + (z - a) * k.From, z);
                }
                void Test(Part p, Part q)
                {
                    var (p1, q1) = Seg(p);
                    var (p2, q2) = Seg(q);
                    float depth = p.R + q.R - SegmentDistance(p1, q1, p2, q2);
                    string pair = $"{p.Name}~{q.Name}";
                    if (depth > 0 && (!worst.TryGetValue(pair, out var w) || depth > w.Depth))
                        worst[pair] = (depth, t);
                }
                foreach (var limb in limbs)
                {
                    bool arm = !Leg(limb);
                    string side = limb.Name[^1..];
                    foreach (var tr in trunk)
                        if (arm || tr.Name is not ("hips" or "skirt"))
                            Test(limb, tr);
                    // Arms against the legs (both), the legs against the other leg, the arms against the other arm.
                    foreach (var other in limbs)
                        if (arm && Leg(other) || other.Name[^1..] != side && Leg(limb) == Leg(other) && string.CompareOrdinal(limb.Name, other.Name) < 0)
                            Test(limb, other);
                }
            }
            foreach (var (pair, w) in worst)
                found.Add(new(clipName, pair, w.Depth, w.At));
        }
        return [.. found.OrderByDescending(r => r.Depth)];
    }

    /// <summary>The closest two segments come (m).</summary>
    public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a < 1e-8f && e < 1e-8f)
            return r.Length();
        if (a < 1e-8f)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e < 1e-8f)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                s = den != 0 ? Math.Clamp((b * f - c * e) / den, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        return (p1 + d1 * s - (p2 + d2 * t)).Length();
    }
}
