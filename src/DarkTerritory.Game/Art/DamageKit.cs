using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The consist's wear (pipeline plan, consist kit: "3 damage states per car; scars persist between runs as decal and
/// mask layers"). The scar mask (<see cref="MeshInstance.Scar"/>) does the scorch, rust and holes over the whole body;
/// this is what a mask can't do, the geometry: plate torn back on its rivets, a hound's claw gouges, and once a car is
/// wrecked, a breach with the ribs showing. Cooked per car shape, state and seed, and drawn with the car's own
/// transform, over its body. It keeps clear of the side doors, which slide, and of the ends, where the gangways are.
/// </summary>
public static class DamageKit
{
    /// <summary>
    /// A car's damage at <paramref name="state"/> (1 damaged, 2 wrecked: <see cref="DamageTuning.StateOf"/>);
    /// <paramref name="seed"/> (the car) chooses where, the same way every time. A wrecked car keeps its damaged
    /// state's marks and adds to them, so getting worse never moves a scar.
    /// </summary>
    public static MeshAsset Car(Look? look, CarShape shape, int state, int seed)
    {
        var k = new Kit(look, 900 + seed);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        float floor = shape.Interior is { } room ? (float)room.Min.Y + 0.1f : h * 0.35f;
        // Side doors by side (−1, +1) and their span along the car.
        var doors = shape.DoorList
            .Select(d => d.Box)
            .Where(b => b.Max.Z - b.Min.Z > b.Max.X - b.Min.X)
            .Select(b => (Side: b.Min.X < 0 ? -1 : 1, Z0: (float)b.Min.Z - 0.15f, Z1: (float)b.Max.Z + 0.15f))
            .ToArray();

        // One generator per state, so the damaged state's marks are the same ones under the wrecked state's.
        for (int s = 1; s <= Math.Min(state, 2); s++)
        {
            var rng = new Random(4200 + seed * 31 + s * 7);
            (int Side, float Z, float Y) Spot(float halfWidth, float halfHeight)
            {
                for (int tries = 0; ; tries++)
                {
                    int side = rng.Next(2) * 2 - 1;
                    float z = Lerp(-l + 0.7f + halfWidth, l - 0.7f - halfWidth, (float)rng.NextDouble());
                    float y = Lerp(floor + halfHeight + 0.25f, h - halfHeight - 0.3f, (float)rng.NextDouble());
                    if (tries > 20 || !doors.Any(d => d.Side == side && z + halfWidth > d.Z0 && z - halfWidth < d.Z1))
                        return (side, z, y);
                }
            }
            if (s == 1)
            {
                for (int i = 0; i < 3; i++)
                    Gouge(k, w, Spot(0.35f, 0.4f), rng);
                for (int i = 0; i < 2; i++)
                    Flap(k, w, Spot(0.3f, 0.25f), rng);
            }
            else
            {
                Breach(k, w, Spot(0.65f, 0.6f), rng, 0.55f);
                Breach(k, w, Spot(0.45f, 0.4f), rng, 0.35f);
                for (int i = 0; i < 2; i++)
                    Flap(k, w, Spot(0.3f, 0.25f), rng);
                Gouge(k, w, Spot(0.35f, 0.4f), rng);
            }
        }
        return k.Build($"damage-{state}");
    }

    /// <summary>
    /// The engine's damage (the row the cars' kit left out: "damage geometry and steam leaks on the engine itself"): its
    /// plate is the cab's side sheets and the tender's, so that's where it's torn: gouges and a flap at
    /// <paramref name="state"/> 1, holed through at 2 (the boiler's leaks are <see cref="Effects.SteamLeaks"/>'s). The
    /// same marks under the worse state's, as the cars'.
    /// </summary>
    public static MeshAsset Engine(Look? look, CarShape shape, int state, int seed)
    {
        var k = new Kit(look, 980 + seed);
        // The side sheets: the cab's walls and the tender's sides, each box's outer face on whichever side it's on.
        var plates = shape.Solids
            .Where(x => x.Part is PartKind.CabWall or PartKind.Tender && x.Box.Max.Y - x.Box.Min.Y > 1)
            .Select(x => x.Box)
            .Where(b => b.Max.Z - b.Min.Z > 1.2)
            .ToArray();
        if (plates.Length == 0)
            return k.Build("engine-damage-none");
        for (int s = 1; s <= Math.Min(state, 2); s++)
        {
            var rng = new Random(5200 + seed * 31 + s * 7);
            (float W, (int Side, float Z, float Y) At) Spot(float half)
            {
                var b = plates[rng.Next(plates.Length)];
                int side = b.Max.X > 0.3 && b.Min.X < -0.3 ? rng.Next(2) * 2 - 1 : b.Centre.X >= 0 ? 1 : -1;
                float w = (float)(side > 0 ? b.Max.X : -b.Min.X);
                float z = Lerp((float)b.Min.Z + half + 0.2f, (float)b.Max.Z - half - 0.2f, (float)rng.NextDouble());
                float y = Lerp((float)b.Min.Y + half + 0.3f, (float)b.Max.Y - half - 0.25f, (float)rng.NextDouble());
                return (w, (side, z, y));
            }
            if (s == 1)
            {
                for (int i = 0; i < 2; i++)
                {
                    var (w, at) = Spot(0.4f);
                    Gouge(k, w, at, rng);
                }
                var (fw, fat) = Spot(0.3f);
                Flap(k, fw, fat, rng);
            }
            else
            {
                var (bw, bat) = Spot(0.5f);
                Breach(k, bw, bat, rng, 0.4f);
                for (int i = 0; i < 2; i++)
                {
                    var (w, at) = Spot(0.3f);
                    Flap(k, w, at, rng);
                }
                CrackedLamp(k, shape, rng);
                BentBoard(k, shape, rng);
            }
        }
        return k.Build($"engine-damage-{state}");
    }

    /// <summary>
    /// Wrecked, the headlamp's glass is cracked (note 360; the checklist's "next" for the engine's damage): a star of dark
    /// cracks from a strike off its centre and a shard gone, over the lens while it still burns (the lamp out is
    /// SceneArt.HeadlampOut's), so the train's eye reads hurt, not shut.
    /// </summary>
    static void CrackedLamp(Kit k, CarShape shape, Random rng)
    {
        // Struck in one of the open wedges between the saltire's bars (TrainKit.Saltire), where it shows.
        float wedge = rng.Next(4) * MathF.PI / 2;
        var strike = new Vector3(MathF.Cos(wedge) * 0.17f, TrainKit.HeadlampY + MathF.Sin(wedge) * 0.17f, (float)-shape.HalfLength - 0.075f);
        k.Use("paint_black", Palette.SootBlack, 0.3f, 0.6f).Shade(0.1f);
        for (int i = 0; i < 7; i++)
        {
            float a = i * MathF.Tau / 7 + (float)rng.NextDouble() * 0.5f;
            float len = 0.16f + 0.14f * (float)rng.NextDouble();
            var bend = strike + new Vector3(MathF.Cos(a), MathF.Sin(a), 0) * len * 0.55f + new Vector3(MathF.Sin(a), -MathF.Cos(a), 0) * 0.025f;
            var tip = strike + new Vector3(MathF.Cos(a + 0.12f), MathF.Sin(a + 0.12f), 0) * len;
            k.Rod(strike, bend, 0.009f, 4);
            k.Rod(bend, tip, 0.006f, 4);
        }
        // A ring of crazing round the strike, and a shard out of it: black where the glass is gone.
        for (int i = 0; i < 7; i++)
        {
            float a0 = i * MathF.Tau / 7, a1 = (i + 1) * MathF.Tau / 7;
            k.Rod(strike + new Vector3(MathF.Cos(a0), MathF.Sin(a0), 0) * 0.06f, strike + new Vector3(MathF.Cos(a1), MathF.Sin(a1), 0) * 0.07f, 0.007f, 4);
        }
        var n = -Vector3.UnitZ;
        Face(k, strike + new Vector3(0, 0, -0.002f), strike + new Vector3(0.1f, 0.04f, -0.002f), strike + new Vector3(0.04f, 0.11f, -0.002f), n);
    }

    /// <summary>
    /// Wrecked, a length of one running board is bent down (note 360): its outer half torn off its brackets and hanging
    /// at a slant over the wheels, the gap it left black.
    /// </summary>
    static void BentBoard(Kit k, CarShape shape, Random rng)
    {
        var boards = shape.Solids.Where(x => x.Part == PartKind.RunningBoard && x.Box.Max.Z - x.Box.Min.Z > 4).Select(x => x.Box).ToArray();
        if (boards.Length == 0)
            return;
        var b = boards[rng.Next(boards.Length)];
        int side = b.Centre.X >= 0 ? 1 : -1;
        float len = 1.1f + 0.5f * (float)rng.NextDouble();
        // Behind the cab: the board runs on ahead of its doorway (note 559), where the steps hang, and that stays whole.
        float from = MathF.Max((float)b.Min.Z + 1, shape.Cab is { } cab ? (float)cab.Max.Z + 0.3f : float.MinValue);
        float z0 = Lerp(from, (float)b.Max.Z - 1 - len, (float)rng.NextDouble()), z1 = z0 + len;
        float top = (float)b.Max.Y, inner = (float)(side > 0 ? b.Min.X : b.Max.X), outer = (float)(side > 0 ? b.Max.X : b.Min.X);
        float mid = (inner + outer) / 2;
        // The gap: the board's outer half gone, black over where it lay.
        k.Use("paint_black", Palette.SootBlack, 0.3f, 0).Shade(0.08f);
        k.Box(new Vector3(MathF.Min(mid, outer) - 0.01f, top - 0.005f, z0), new Vector3(MathF.Max(mid, outer) + 0.01f, top + 0.012f, z1), Kit.Faces.PosY);
        // The torn half, hinged at the middle and bent down 35–60°, twisted along its length; its torn edge rusted.
        float drop = (35 + 25 * (float)rng.NextDouble()) * MathF.PI / 180, twist = 0.08f * ((float)rng.NextDouble() - 0.5f);
        float reach = MathF.Abs(outer - mid);
        var down0 = new Vector3(side * MathF.Cos(drop), -MathF.Sin(drop), 0) * reach;
        var down1 = new Vector3(side * MathF.Cos(drop + twist * 4), -MathF.Sin(drop + twist * 4), 0) * reach;
        var a = new Vector3(mid, top, z0 + 0.04f);
        var c = new Vector3(mid, top, z1 - 0.04f);
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.3f).Shade(0.6f);
        k.Quad(a, c, c + down1, a + down0, twoSided: true);
        k.Use("rust_heavy", Palette.RustRed, 0.7f, 0.2f);
        k.Rod(a + down0, c + down1, 0.02f, 4);
    }

    /// <summary>
    /// Where a damaged boiler leaks (in the engine's frame), and which way the steam comes out: seams along its upper
    /// flanks, one at <paramref name="state"/> 1, three at 2, chosen by the engine's seed.
    /// </summary>
    public static IEnumerable<(Vector3 At, Vector3 Out)> Leaks(CarShape shape, int state, int seed)
    {
        var boilers = shape.Solids.Where(x => x.Part == PartKind.Boiler).ToArray();
        if (state <= 0 || boilers.Length == 0)
            yield break;
        var b = boilers[0].Box;
        var rng = new Random(6100 + seed * 13);
        for (int i = 0; i < (state >= 2 ? 3 : 1); i++)
        {
            int side = rng.Next(2) * 2 - 1;
            float z = Lerp((float)b.Min.Z + 0.6f, (float)b.Max.Z - 0.6f, (float)rng.NextDouble());
            float x = (float)(side > 0 ? b.Max.X : b.Min.X) * 0.82f, y = (float)(b.Max.Y - (b.Max.Y - b.Min.Y) * 0.2);
            // Under the armoured hood (note 338) the seam's steam finds the hood's plate seams (note 360): it comes out of
            // the hood's side, above the feed pipe.
            if (TrainKit.HoodFace(shape, side, z) is { } hood)
                (x, y) = (side * (hood.X + 0.06f), hood.Deck + 1.9f);
            yield return (new Vector3(x, y, z), Vector3.Normalize(new Vector3(side, 0.55f, 0)));
        }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Three parallel slashes raked down the plate (a cinder hound going up the side): black, with bright torn lips.</summary>
    static void Gouge(Kit k, float w, (int Side, float Z, float Y) at, Random rng)
    {
        var n = new Vector3(at.Side, 0, 0);
        float slant = (float)(rng.NextDouble() - 0.5) * 0.9f;
        var up = Vector3.Normalize(new Vector3(0, 1, slant));
        var across = Vector3.Normalize(Vector3.Cross(up, n));
        float length = 0.45f + (float)rng.NextDouble() * 0.3f;
        for (int j = -1; j <= 1; j++)
        {
            var c = new Vector3(at.Side * (w + 0.05f), at.Y, at.Z) + across * (j * 0.09f) + up * (MathF.Abs(j) * -0.04f);
            k.Use("rust_heavy", Palette.SootBlack, 0.3f, 0).Shade(0.18f);
            k.Panel(c, n, up, 0.03f, length * (1 - MathF.Abs(j) * 0.15f));
            k.Use("iron_plate", Palette.IronGrey, 0.1f, 0.5f).Shade(1.25f);
            k.Panel(c + across * 0.022f + n * 0.002f, n, up, 0.008f, length * (1 - MathF.Abs(j) * 0.15f) * 0.9f);
        }
    }

    /// <summary>A plate torn off its lower rivets and bent out from the car, the hole it left black behind it.</summary>
    static void Flap(Kit k, float w, (int Side, float Z, float Y) at, Random rng)
    {
        float hw = 0.22f + (float)rng.NextDouble() * 0.12f, hh = 0.18f + (float)rng.NextDouble() * 0.1f;
        float x = at.Side * (w + 0.05f);
        k.Use("rust_heavy", Palette.SootBlack, 0.3f, 0).Shade(0.12f);
        k.Panel(new Vector3(x, at.Y, at.Z), new Vector3(at.Side, 0, 0), Vector3.UnitY, hw * 2, hh * 2);
        // Hinged along its top edge, swung out 25-55°, a little twisted.
        float swing = (25 + (float)rng.NextDouble() * 30) * MathF.PI / 180;
        float twist = ((float)rng.NextDouble() - 0.5f) * 0.12f;
        var hingeA = new Vector3(x + at.Side * 0.005f, at.Y + hh, at.Z - hw);
        var hingeB = new Vector3(x + at.Side * 0.005f, at.Y + hh, at.Z + hw);
        var drop = new Vector3(at.Side * MathF.Sin(swing), -MathF.Cos(swing), 0) * (hh * 2);
        k.Use("rust_heavy", Palette.RustRed, 0.7f, 0.2f);
        k.Quad(hingeA, hingeB, hingeB + drop + new Vector3(0, twist, 0), hingeA + drop - new Vector3(0, twist, 0), twoSided: true);
    }

    /// <summary>
    /// Holed through: a ragged black opening, its plate petalled outward round the edge, the car's frame across it.
    /// </summary>
    static void Breach(Kit k, float w, (int Side, float Z, float Y) at, Random rng, float radius)
    {
        var n = new Vector3(at.Side, 0, 0);
        var centre = new Vector3(at.Side * (w + 0.05f), at.Y, at.Z);
        const int Sides = 9;
        var rim = new Vector3[Sides];
        for (int i = 0; i < Sides; i++)
        {
            float a = i * MathF.Tau / Sides + (float)rng.NextDouble() * 0.3f;
            float r = radius * (0.7f + (float)rng.NextDouble() * 0.45f);
            rim[i] = centre + new Vector3(0, MathF.Sin(a) * r, MathF.Cos(a) * r);
        }
        k.Use("rust_heavy", Palette.SootBlack, 0.2f, 0).Shade(0.08f);
        for (int i = 0; i < Sides; i++)
            Face(k, centre, rim[i], rim[(i + 1) % Sides], n);
        // The frame showing through: an upright and a rail, bent.
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f).Shade(0.55f);
        float lean = ((float)rng.NextDouble() - 0.5f) * radius * 0.5f;
        k.Rod(centre + new Vector3(0.02f * at.Side, -radius * 0.95f, lean * 0.2f), centre + new Vector3(0.05f * at.Side, radius * 0.95f, lean), 0.035f);
        k.Rod(centre + new Vector3(0.02f * at.Side, lean * 0.3f, -radius * 0.9f), centre + new Vector3(0.04f * at.Side, -lean * 0.4f, radius * 0.9f), 0.025f);
        // Petals: each stretch of the rim peeled outward and back, torn lip bright.
        for (int i = 0; i < Sides; i++)
        {
            if (rng.NextDouble() < 0.25)
                continue;
            var a = rim[i];
            var b = rim[(i + 1) % Sides];
            var mid = (a + b) / 2;
            var outward = Vector3.Normalize(mid - centre);
            var tip = mid + outward * (radius * (0.25f + (float)rng.NextDouble() * 0.3f)) + n * (0.1f + (float)rng.NextDouble() * 0.18f);
            k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.2f);
            k.Tri(a, b, tip, new(0, 0), new(0.3f, 0), new(0.15f, 0.3f));
            k.Tri(b, a, tip, new(0.3f, 0), new(0, 0), new(0.15f, 0.3f));
            k.Use("iron_plate", Palette.IronGrey, 0.1f, 0.5f).Shade(1.2f);
            k.Rod(a + n * 0.004f, b + n * 0.004f, 0.012f, 3);
        }
    }

    /// <summary>A triangle facing <paramref name="n"/> whichever way its corners were given.</summary>
    static void Face(Kit k, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0)
            (b, c) = (c, b);
        k.Tri(a, b, c, new(a.Z, -a.Y), new(b.Z, -b.Y), new(c.Z, -c.Y));
    }
}
