using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Where a Car Hugger has eaten into a car (GDD v1.2 App. A.3 FEED: "eats the car's shell and loot steadily"), in the
/// car's own space (+Z its rear end): behind a ragged frontier the car is gone, deepest down the middle where its mouth
/// works, least at the side walls its hands hold, never below <see cref="Floor"/> (the underframe and the trucks, so the
/// car rolls until it's dropped). The shader cuts it (Shaders/bite.glsl, <see cref="MeshInstance.Bite"/>); the edge is
/// torn geometry laid along the same frontier (<see cref="Edge"/>).
/// </summary>
/// <param name="RearZ">The rear end the eating starts from (the platform's end, on a guard van).</param>
/// <param name="Centre">How deep it's eaten down the middle (m from <paramref name="RearZ"/>).</param>
/// <param name="Side">How deep at the side walls.</param>
/// <param name="Advance">How far the thing's head has pushed in through the car's end (m).</param>
/// <param name="Grip">How far forward of where they took hold its hands have moved, following the side walls' edge (m).</param>
public readonly record struct Bite(float RearZ, float Centre, float Side, float HalfWidth, float Floor, float Advance, float Grip, float Seed)
{
    /// <summary>The frontier moves in bites of this much (m): each a mesh of its own, and the bitten chunk goes at once.</summary>
    public const float Step = 0.12f;

    public bool Any => Centre > 0;
    public Vector4 Shader => new(RearZ, Centre, Side, HalfWidth);

    /// <summary>
    /// A car's bite, off its eaten and integrity (<see cref="BiteTuning.Fraction"/>), stepped to <see cref="Step"/>;
    /// <paramref name="seed"/> is the car's scar seed (<see cref="MeshInstance.Scar"/>.y), which rags its edge.
    /// </summary>
    public static Bite Of(BiteTuning t, CarShape shape, double eaten, double integrity, float seed)
    {
        float f = BiteTuning.Fraction(eaten, integrity);
        float l = (float)shape.HalfLength, w = (float)shape.HalfWidth;
        float rear = shape.Platform is { } p ? (float)p.Max.Z : l;
        if (f <= 0)
            return new Bite(rear, 0, 0, w, t.Floor, 0, 0, seed);
        float platform = (rear - l) * Math.Clamp(f / Math.Max(1e-3f, t.Platform), 0, 1);
        float body = Stepped(f * t.Depth * 2 * l);
        float advance = MathF.Min(body, t.Advance);
        float side = MathF.Min(body, advance + t.SideLead);
        // (At least one step in, so the first bite shows.)
        float centre = MathF.Max(Step, platform + body);
        // The hands took hold 0.75 m inside the end; they keep a hand's breadth of wall ahead of its torn edge.
        float grip = MathF.Max(0, platform + side + 0.3f - (rear - l) - 0.75f);
        return new Bite(rear, centre, platform + side, w, t.Floor, advance, grip, seed);
    }

    /// <summary>A car's bite as it's drawn: off its vehicle (none on the engine), seeded as its scars are.</summary>
    public static Bite For(BiteTuning t, CarShape shape, Vehicle? vehicle, int fallbackId) =>
        Of(t, shape, shape.Cab is null ? vehicle?.Eaten ?? 0 : 0, vehicle?.Integrity ?? 1, ScarSeed(vehicle?.Id ?? fallbackId));

    /// <summary>The car's scar seed (<see cref="MeshInstance.Scar"/>.y): its scars, and its bite's ragged edge.</summary>
    public static float ScarSeed(int id) => id * 0.618f % 1 * 97;

    static float Stepped(float d) => MathF.Floor(d / Step) * Step;

    /// <summary>bite.glsl's biteRagged: sines, so both sides lay the same edge.</summary>
    public float Ragged(float x, float y) =>
        0.5f * MathF.Sin(3.1f * y + Seed) + 0.3f * MathF.Sin(2.3f * x + 1.7f * Seed) + 0.2f * MathF.Sin(7.3f * (y + 0.5f * x) + 2.9f * Seed)
        + 0.12f * MathF.Sin(13.1f * y - 5f * x + Seed);

    /// <summary>The frontier's z at (x, y): everything behind it (greater) is eaten. bite.glsl's biteInto, solved for z.</summary>
    public float FrontierZ(float x, float y)
    {
        float centre = 1 - SmoothStep(HalfWidth - 0.9f, HalfWidth - 0.1f, MathF.Abs(x));
        float depth = Side + (Centre - Side) * centre;
        float amp = MathF.Min(0.32f, 0.12f + 0.08f * Centre) * Math.Clamp(depth / 0.35f, 0, 1);
        depth += amp * (Ragged(x, y) * 0.5f + 0.5f);
        return RearZ - depth;
    }

    public bool Eats(Vector3 p) => Any && p.Y >= Floor && p.Z > FrontierZ(p.X, p.Y);

    static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// The torn edge along the frontier: plank ends snapped off ragged down the side walls (or plate torn back and curled,
    /// on a steel car), the roof's sheets bent up and out over the hole, the floorboards' broken ends, posts left
    /// standing, gnawed, where the boards between them have gone, and strings of slaver hanging from the roof's edge.
    /// Seeded by the car, so each bite adds to what was torn, not a new tearing.
    /// </summary>
    public MeshAsset Edge(Look? look, CarShape shape, TrainKit.Livery livery, int carSeed)
    {
        var k = new Kit(look, 1700 + carSeed);
        float w = HalfWidth, h = (float)shape.RoofHeight;
        float floor = shape.Interior is { } room ? (float)room.Min.Y + 0.1f : Floor + 0.1f;
        float ceiling = shape.Interior is { } r2 ? (float)r2.Max.Y : h - 0.2f;
        bool planked = livery == TrainKit.Livery.Planked;
        void Wood() => k.Use("wood_siding", Palette.DeepBrown, 0.9f, 0.02f);
        void Plate() => k.Use(livery == TrainKit.Livery.Steel ? "paint_oxide" : "paint_olive", livery == TrainKit.Livery.Steel ? Palette.RustRed : Palette.MuddyOlive, 0.9f, 0.2f);
        void Raw() => k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0);

        // Hashed per piece of the car (its place, not the bite), so a splinter keeps its shape as the edge moves past it.
        float Hash(float a, float b) => Fract(MathF.Sin(a * 127.1f + b * 311.7f + carSeed * 74.7f) * 43758.55f);

        // Down each side wall: a board at a time, snapped at the frontier, splintered back past it.
        foreach (int s in new[] { -1, 1 })
        {
            float x = s * w;
            for (float y = floor + 0.05f; y < ceiling - 0.04f; y += 0.16f)
            {
                float z = FrontierZ(x, y);
                if (z > RearZ - 0.02f)
                    continue;
                float n = Hash(y * 7, s);
                float len = 0.08f + 0.3f * n * n;
                var outward = new Vector3(s, 0, 0);
                if (planked || n < 0.35f)
                {
                    // The board's end, a jag sticking back into the hole, and a split sliver off it.
                    Wood();
                    float tilt = (Hash(y * 3, s * 5) - 0.5f) * 0.5f;
                    k.With(Matrix4x4.CreateRotationX(tilt) * Kit.At(x - s * 0.03f, y, z), () =>
                        k.Box(new Vector3(-0.03f, -0.06f, -0.02f), new Vector3(0.03f, 0.06f, len)));
                    Raw();
                    k.Rod(new Vector3(x - s * 0.02f, y + 0.04f, z + len * 0.6f), new Vector3(x + s * 0.02f, y + 0.07f, z + len + 0.12f * n), 0.012f);
                }
                else
                {
                    // Plate torn back: a tongue of it peeled out from the wall, curling.
                    Plate();
                    float curl = 0.3f + 0.8f * n;
                    k.With(Matrix4x4.CreateRotationY(-s * curl) * Kit.At(x, y, z), () =>
                        k.Box(new Vector3(-0.006f, -0.07f, 0), new Vector3(0.006f, 0.07f, len + 0.1f)));
                }
            }
            // The posts: the frame outlasts the boards, gnawed thin, standing up out of the hole where the wall's gone.
            k.Use(planked ? "wood_sleeper" : "rust_heavy", Palette.DeepBrown, 0.9f, 0.1f);
            float l = (float)shape.HalfLength;
            for (float pz = -l + 0.06f; pz <= l; pz += (2 * l - 0.12f) / 8)
            {
                float zf = FrontierZ(x, (floor + ceiling) / 2);
                if (pz < zf || pz > zf + 1.4f)
                    continue;
                // Eaten down from the top the further past the edge it stands.
                float top = floor + (ceiling - floor) * Math.Clamp(1 - (pz - zf) / 1.4f, 0.15f, 0.95f) * (0.8f + 0.2f * Hash(pz, s));
                k.Box(new Vector3(x - s * 0.02f - 0.025f, floor - 0.05f, pz - 0.045f), new Vector3(x - s * 0.02f + 0.025f, top, pz + 0.045f));
                k.Rod(new Vector3(x - s * 0.02f, top, pz), new Vector3(x - s * 0.02f + s * 0.03f, top + 0.14f, pz + 0.02f), 0.012f);
            }
        }

        // Across the roof: sheets bent up and back over the hole, a few hanging down into it.
        k.Use(planked ? "corrugated_iron" : "iron_plate", Palette.IronGrey, 0.9f, 0.3f, tile: 1.2f);
        for (float x = -w + 0.1f; x < w - 0.05f; x += 0.22f)
        {
            float z = FrontierZ(x, h);
            if (z > RearZ - 0.02f)
                continue;
            float n = Hash(x * 5, 9);
            float lift = n < 0.3f ? -(0.6f + 0.6f * n) : 0.25f + 0.9f * n;   // down into the hole, or up and out
            float len = 0.15f + 0.35f * Hash(x * 11, 3);
            k.With(Matrix4x4.CreateRotationX(lift) * Kit.At(x, h - 0.02f, z), () =>
                k.Box(new Vector3(-0.1f, -0.005f, 0), new Vector3(0.1f, 0.005f, len)));
        }

        // The floor's broken board ends, and what's showing of the joists under them.
        Raw();
        for (float x = -w + 0.12f; x < w - 0.1f; x += 0.14f)
        {
            float z = FrontierZ(x, floor);
            if (z > RearZ - 0.02f || floor < Floor)
                continue;
            float len = 0.05f + 0.25f * Hash(x * 13, 1);
            k.Box(new Vector3(x - 0.06f, floor - 0.04f, z - 0.01f), new Vector3(x + 0.06f, floor, z + len));
        }

        // Slaver: strings of it hanging from the roof's torn edge, glossy and dark.
        k.Use("tar", Palette.SootBlack, 0.2f, 0.9f);
        for (float x = -w + 0.3f; x < w - 0.2f; x += 0.37f)
        {
            float z = FrontierZ(x, h) + 0.05f;
            if (z > RearZ)
                continue;
            float drop = 0.2f + 0.9f * Hash(x * 17, 4);
            k.Rod(new Vector3(x, ceiling, z), new Vector3(x + 0.03f, ceiling - drop, z + 0.02f), 0.008f + 0.01f * Hash(x, 8));
        }
        return k.Build($"bite-{Centre:0.00}-{Side:0.00}");
    }

    static float Fract(float v) => v - MathF.Floor(v);
}
