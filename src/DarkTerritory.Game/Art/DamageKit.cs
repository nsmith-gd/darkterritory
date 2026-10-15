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
        var doors = SideDoors(shape);

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

    /// <summary>A car's side doors by side (−1, +1) and their span along the car, with a margin: marks keep off them.</summary>
    static (int Side, float Z0, float Z1)[] SideDoors(CarShape shape) => shape.DoorList
        .Select(d => d.Box)
        .Where(b => b.Max.Z - b.Min.Z > b.Max.X - b.Min.X)
        .Select(b => (Side: b.Min.X < 0 ? -1 : 1, Z0: (float)b.Min.Z - 0.15f, Z1: (float)b.Max.Z + 0.15f))
        .ToArray();

    /// <summary>
    /// A car working itself apart (note 576's stages; note 779, the art checklist's <c>failing-stages</c>: "each stage needs a
    /// look of its own that reads from the roof, the cab and the lineside at night"), over what its integrity's torn
    /// (<see cref="Car"/>). <see cref="FailStage.Failing"/>: boards and plates sprung off its sides, nailed at one end and
    /// bowed out at the other, the dark of the gap each left behind it and their split backs pale, and a roof sheet lifted at
    /// its eave against the sky. <see cref="FailStage.Breaking"/> and on: twice as many, sprung further, a plate peeled back
    /// off a great hole with the load inside it showing, and under the car a truss rod snapped and hanging toward the sleepers
    /// (the frame's sag is <see cref="Sagged"/>'s). In the car's <paramref name="livery"/>, so what's come off it is its own
    /// plate; <paramref name="seed"/> (the car) chooses where, the same way every time, and breaking keeps failing's boards
    /// where they were. The engine (a shape with a boiler) failing and breaking down: its casing's plates sprung off the boiler
    /// behind the cab, scraped bright behind, and breaking down one torn open on the lagging; nothing on its walked top or
    /// among its drivers.
    /// </summary>
    public static MeshAsset Failing(Look? look, CarShape shape, TrainKit.Livery livery, FailStage stage, int seed)
    {
        var k = new Kit(look, 950 + seed);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        float floor = shape.Interior is { } room ? (float)room.Min.Y + 0.1f : h * 0.35f;
        var doors = SideDoors(shape);
        // The engine's side sheets, as Engine's marks find them: its cab's walls and its armoured hood's.
        bool engine = shape.Solids.Any(x => x.Part == PartKind.Boiler);
        var plates = engine ? EngineSheets(shape) : [new Box(new Ballast.Double3(-w, floor, -l + 0.7), new Ballast.Double3(w, h, l - 0.7))];
        if (plates.Length == 0)
            return k.Build("failing-none");
        bool breaking = stage >= FailStage.Breaking;
        // One generator per stage, as Car's per state: failing's boards are the first ones breaking springs.
        for (int s = 0; s <= (breaking ? 1 : 0); s++)
        {
            var rng = new Random(5300 + seed * 17 + s * 11);
            (float W, (int Side, float Z, float Y) At) Spot(float halfWidth, float halfHeight)
            {
                for (int tries = 0; ; tries++)
                {
                    var b = plates[rng.Next(plates.Length)];
                    int side = b.Max.X > 0.3 && b.Min.X < -0.3 ? rng.Next(2) * 2 - 1 : b.Centre.X >= 0 ? 1 : -1;
                    float z = Lerp((float)b.Min.Z + MathF.Min(halfWidth, (float)(b.Max.Z - b.Min.Z) / 2), (float)b.Max.Z - MathF.Min(halfWidth, (float)(b.Max.Z - b.Min.Z) / 2), (float)rng.NextDouble());
                    float y = Lerp((float)b.Min.Y + halfHeight + 0.2f, (float)b.Max.Y - halfHeight - 0.25f, (float)rng.NextDouble());
                    if (tries > 20 || !doors.Any(d => d.Side == side && z + halfWidth > d.Z0 && z - halfWidth < d.Z1))
                        return ((float)(side > 0 ? b.Max.X : -b.Min.X), (side, z, y));
                }
            }
            for (int i = 0; i < (engine ? 3 : 4); i++)
            {
                var (sw, at) = Spot(engine ? 0.9f : 1.2f, 0.12f);
                Sprung(k, livery, sw, at, rng, breaking ? 1.5f : 1, iron: engine || livery == TrainKit.Livery.Steel);
            }
            if (!engine)
                RoofLifted(k, livery, shape, rng, breaking ? 1.5f : 1);
            if (s == 1)
            {
                var (pw, pat) = Spot(engine ? 0.45f : 0.6f, 0.5f);
                Peeled(k, livery, pw, pat, rng, engine);
                if (!engine)
                    TrussRod(k, shape, floor, rng);
            }
        }
        return k.Build($"failing-{(int)stage}");
    }

    /// <summary>The engine's side sheets tall and long enough to mark: its cab's walls and its hood's (or tender's) sides.</summary>
    static Box[] EngineSheets(CarShape shape) => shape.Solids
        .Where(x => x.Part is PartKind.CabWall or PartKind.Tender && x.Box.Max.Y - x.Box.Min.Y > 1)
        .Select(x => x.Box)
        .Where(b => b.Max.Z - b.Min.Z > 1.2)
        .ToArray();

    /// <summary>
    /// A board or a strip of plate sprung off the car's side: still nailed at one end, the rest bowed out from the wall and
    /// sagging at its free end, the wall's dark showing where it lay. <paramref name="how"/> scales how far it's gone.
    /// </summary>
    static void Sprung(Kit k, TrainKit.Livery livery, float w, (int Side, float Z, float Y) at, Random rng, float how, bool iron)
    {
        float len = 1.8f + 0.8f * (float)rng.NextDouble(), wide = 0.18f + 0.08f * (float)rng.NextDouble();
        int dir = rng.Next(2) * 2 - 1;
        float z0 = at.Z - dir * len / 2;
        // Out far enough to break the car's line from along the train (the roof, the cab), not only from the lineside.
        float lift = (0.26f + 0.14f * (float)rng.NextDouble()) * how, droop = (0.06f + 0.08f * (float)rng.NextDouble()) * how;
        float wall = at.Side * (w + 0.05f);
        // Where it came off: the black of the wall behind.
        k.Use("rust_heavy", Palette.SootBlack, 0.3f, 0).Shade(0.1f);
        k.Panel(new Vector3(wall, at.Y, at.Z), new Vector3(at.Side, 0, 0), Vector3.UnitY, len, wide);
        // The board itself, bowed out off its nailed end: its face in the car's livery, its sawn edge and back in the lining.
        const int Steps = 5;
        Vector3 Along(float t, float dy) => new(wall + at.Side * (0.012f + lift * MathF.Pow(t, 1.6f)), at.Y + dy - droop * t * t, z0 + dir * len * t);
        TrainKit.Outside(k, livery);
        for (int j = 0; j < Steps; j++)
        {
            float t0 = j / (float)Steps, t1 = (j + 1) / (float)Steps;
            k.Quad(Along(t0, -wide / 2), Along(t1, -wide / 2), Along(t1, wide / 2), Along(t0, wide / 2), twoSided: true);
        }
        // Its back and edge fresh: split wood pale, or plate scraped bright, where the paint and the weather never reached; what
        // catches a lamp at night.
        if (iron)
            k.Use("iron_plate", Palette.IronGrey, 0.3f, 0.5f).Shade(1.6f);
        else
            k.Use("wood_grey", Palette.BoardEnamel, 0.8f, 0, 1f).Shade(1.7f);
        for (int j = 0; j < Steps; j++)
        {
            float t0 = j / (float)Steps, t1 = (j + 1) / (float)Steps;
            var back = new Vector3(-at.Side * 0.03f, 0, 0);
            k.Quad(Along(t0, wide / 2), Along(t1, wide / 2), Along(t1, wide / 2) + back, Along(t0, wide / 2) + back, twoSided: true);
        }
        // Its nails, torn out, bright where they bent.
        k.Use("iron_plate", Palette.IronGrey, 0.1f, 0.5f).Shade(1.2f);
        for (int j = 1; j <= 3; j++)
            k.Rod(Along(j / 4f, 0) - new Vector3(at.Side * 0.012f, 0, 0), Along(j / 4f, 0) - new Vector3(at.Side * 0.06f, 0.01f, 0), 0.004f, 3);
    }

    /// <summary>
    /// A great sheet of plate peeled back off the side on its edge, curled outward, the hole it left open on the load: the
    /// freight's boards and sacking in the car's dark, the rim's torn lips bright. On the engine (<paramref name="lagging"/>),
    /// the boiler's grey lagging in the hole.
    /// </summary>
    static void Peeled(Kit k, TrainKit.Livery livery, float w, (int Side, float Z, float Y) at, Random rng, bool lagging)
    {
        float hw = 0.75f + 0.2f * (float)rng.NextDouble(), hh = 0.5f + 0.12f * (float)rng.NextDouble();
        float wall = at.Side * (w + 0.05f);
        var n = new Vector3(at.Side, 0, 0);
        // The load inside, dark: crates and sacks behind the hole.
        if (lagging)
            k.Use("plaster_ruin", Palette.BoardEnamel * 0.6f, 0.9f, 0).Shade(0.3f);
        else
            k.Use("wood_crate", Palette.DeepBrown, 0.7f, 0, 0.9f).Shade(0.6f);
        k.Panel(new Vector3(wall - at.Side * 0.03f, at.Y, at.Z), n, Vector3.UnitY, hw * 2, hh * 2);
        k.Use("rust_heavy", Palette.SootBlack, 0.3f, 0).Shade(0.08f);
        foreach (int e in (ReadOnlySpan<int>)[-1, 1])
        {
            k.Panel(new Vector3(wall, at.Y + e * (hh - 0.04f), at.Z), n, Vector3.UnitY, hw * 2, 0.08f);
            k.Panel(new Vector3(wall, at.Y, at.Z + e * (hw - 0.04f)), n, Vector3.UnitY, 0.08f, hh * 2);
        }
        // The sheet: hinged on its upright edge at one end, peeled out past square and curling round as it goes, narrowing
        // a little toward its torn edge.
        int dir = rng.Next(2) * 2 - 1;
        float hinge = at.Z - dir * hw, curl = (95 + 35 * (float)rng.NextDouble()) * MathF.PI / 180;
        Vector3 Sheet(float t, float y)
        {
            float a = curl * t, r = hw * 2 * t;
            return new Vector3(wall + at.Side * r * MathF.Sin(a) * 0.55f, at.Y + y * (1 - 0.12f * t) - 0.08f * t, hinge + dir * r * MathF.Cos(a) * 0.9f);
        }
        TrainKit.Outside(k, livery);
        const int Steps = 6;
        for (int j = 0; j < Steps; j++)
        {
            float t0 = j / (float)Steps, t1 = (j + 1) / (float)Steps;
            k.Quad(Sheet(t0, -hh), Sheet(t1, -hh), Sheet(t1, hh), Sheet(t0, hh));
        }
        // Its back, curled round to face out: bright scraped iron, never painted.
        k.Use("iron_plate", Palette.IronGrey, 0.4f, 0.4f).Shade(1.5f);
        for (int j = 0; j < Steps; j++)
        {
            float t0 = j / (float)Steps, t1 = (j + 1) / (float)Steps;
            k.Quad(Sheet(t0, hh), Sheet(t1, hh), Sheet(t1, -hh), Sheet(t0, -hh));
        }
        k.Use("iron_plate", Palette.IronGrey, 0.1f, 0.5f).Shade(1.2f);
        k.Rod(new Vector3(wall + at.Side * 0.004f, at.Y - hh, at.Z - hw), new Vector3(wall + at.Side * 0.004f, at.Y - hh, at.Z + hw), 0.012f, 3);
        k.Rod(new Vector3(wall + at.Side * 0.004f, at.Y + hh, at.Z - hw), new Vector3(wall + at.Side * 0.004f, at.Y + hh, at.Z + hw), 0.012f, 3);
    }

    /// <summary>
    /// A sheet of the roof lifted at its eave: nailed along its inner edge by the roof walk, its outer edge bent up off the
    /// carlines, a dark slot under it. Against the sky from the lineside, and in the way of anyone walking the roof.
    /// </summary>
    static void RoofLifted(Kit k, TrainKit.Livery livery, CarShape shape, Random rng, float how)
    {
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        int side = rng.Next(2) * 2 - 1;
        float len = 1.1f + 0.5f * (float)rng.NextDouble();
        float z = Lerp(-l + 1 + len / 2, l - 1 - len / 2, (float)rng.NextDouble());
        if (shape.Hatch is { } hatch && z + len / 2 > hatch.Min.Z - 0.2f && z - len / 2 < hatch.Max.Z + 0.2f)
            z = hatch.Max.Z + 0.3f + len / 2 < l - 0.5f ? (float)hatch.Max.Z + 0.3f + len / 2 : (float)hatch.Min.Z - 0.3f - len / 2;
        float inner = 0.42f, lift = (0.22f + 0.12f * (float)rng.NextDouble()) * how;
        // Up off the arched roof, from the walk's edge out past the eave.
        Vector3 At(float t, float dz)
        {
            float x = inner + (w + 0.07f - inner) * t, roof = h - 0.035f * (x / w) * (x / w) + 0.012f;
            return new Vector3(side * x, roof + lift * t * t, z + dz * (1 + 0.04f * t));
        }
        k.Use("rust_heavy", Palette.SootBlack, 0.3f, 0).Shade(0.1f);
        k.Quad(At(0, -len / 2) - Vector3.UnitY * 0.008f, At(0, len / 2) - Vector3.UnitY * 0.008f,
            new Vector3(side * (w + 0.07f), h - 0.03f, z + len / 2), new Vector3(side * (w + 0.07f), h - 0.03f, z - len / 2), twoSided: true);
        const int Steps = 4;
        k.Use(livery == TrainKit.Livery.Planked ? "corrugated_iron" : "iron_plate", Palette.IronGrey, 0.9f, 0.3f, tile: 1.2f);
        for (int j = 0; j < Steps; j++)
        {
            float t0 = j / (float)Steps, t1 = (j + 1) / (float)Steps;
            k.Quad(At(t0, -len / 2), At(t0, len / 2), At(t1, len / 2), At(t1, -len / 2), twoSided: true);
        }
        // Its torn edge bright where it bent.
        k.Use("iron_plate", Palette.IronGrey, 0.1f, 0.5f).Shade(1.4f);
        k.Rod(At(1, -len / 2), At(1, len / 2), 0.01f, 3);
    }

    /// <summary>
    /// Under a breaking car, one of the frame's truss rods snapped: from its anchor at the bolster, down to the queen post,
    /// then its broken half hanging toward the sleepers, swinging clear of the rails.
    /// </summary>
    static void TrussRod(Kit k, CarShape shape, float floor, Random rng)
    {
        int side = rng.Next(2) * 2 - 1;
        float l = (float)shape.HalfLength, x = side * (float)shape.HalfWidth * 0.55f;
        float under = floor - 0.32f;
        int end = rng.Next(2) * 2 - 1;
        var anchor = new Vector3(x, under + 0.18f, end * l * 0.55f);
        var post = new Vector3(x, under - 0.32f, end * l * 0.18f);
        var hang = post + new Vector3(side * 0.08f, -(0.35f + 0.15f * (float)rng.NextDouble()), -end * (0.55f + 0.25f * (float)rng.NextDouble()));
        // Down toward the sleepers, but never on them: it'd drag, and it's not dragging yet.
        hang.Y = MathF.Max(hang.Y, 0.3f);
        k.Use("rust_heavy", Palette.IronGrey * 0.7f, 0.9f, 0.3f);
        k.Rod(anchor, post, 0.018f, 4);
        k.Rod(post, hang, 0.018f, 4);
        k.Rod(post + new Vector3(0, -0.02f, 0), post + new Vector3(0, 0.3f, 0), 0.03f, 4);
    }

    /// <summary>How far a car's frame has sagged at its middle (m): breaking, and come apart on the line.</summary>
    public const float BreakingSag = 0.16f, ApartSag = 0.24f;

    public static float SagOf(FailStage stage) => stage switch { FailStage.Breaking => BreakingSag, FailStage.Broken => ApartSag, _ => 0 };

    /// <summary>Which of a car's side doors hangs askew once it's failing (the car chooses), or −1 if it has none.</summary>
    public static int AskewDoor(CarShape shape, int seed)
    {
        var sides = shape.DoorList.Where(d => d.Box.Max.Z - d.Box.Min.Z > d.Box.Max.X - d.Box.Min.X).Select(d => d.Index).ToArray();
        return sides.Length == 0 ? -1 : sides[(int)((uint)(seed * 7 + 3) % (uint)sides.Length)];
    }

    /// <summary>
    /// A side door (<paramref name="placed"/>, at its middle in the car's frame) dropped off its top runner at one end: hanging
    /// on its lower corner at the other, out of its opening, further the further gone the car is.
    /// </summary>
    public static Matrix4x4 Askew(Matrix4x4 placed, Vector3 size, int side, FailStage stage, int seed)
    {
        float degrees = stage switch { FailStage.Failing => 6, FailStage.Breaking => 11, _ => 16 };
        float outward = stage switch { FailStage.Failing => 0.05f, FailStage.Breaking => 0.09f, _ => 0.12f };
        int end = (seed & 1) == 0 ? 1 : -1;
        var pivot = new Vector3(0, -size.Y / 2, end * size.Z / 2);
        return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationX(-end * degrees * MathF.PI / 180) * Matrix4x4.CreateTranslation(pivot)
            * Matrix4x4.CreateTranslation(side * outward, 0, 0) * placed;
    }

    /// <summary>
    /// A car's body (or what's drawn over it) with its frame sagging: under the floor, between the bogies (a fifth of the
    /// way in from each end), bowed down by as much as <paramref name="sag"/> at its middle, and nothing above the floor
    /// moved, so whoever's stood in it or walking its roof is still on what's drawn. Cooked once per body and sag.
    /// </summary>
    public static MeshAsset Sagged(MeshAsset body, CarShape shape, float sag)
    {
        float floor = shape.Interior is { } room ? (float)room.Min.Y : (float)shape.RoofHeight * 0.35f;
        float bogie = (float)shape.HalfLength * 0.6f;
        var v = (Vertex[])body.Vertices.Clone();
        for (int i = 0; i < v.Length; i++)
            v[i].Position.Y -= Sag(v[i].Position, floor, bogie, sag);
        return new MeshAsset(body.Name + "-sagged", v);
    }

    /// <summary>How far down the sag has a point in the car's frame: the frame's bow between the bogies, faded in under the floor.</summary>
    public static float Sag(Vector3 p, float floor, float bogie, float sag)
    {
        float along = Math.Clamp(1 - p.Z / bogie * (p.Z / bogie), 0, 1);
        float below = Math.Clamp((floor - p.Y) / 0.45f, 0, 1);
        return sag * along * below;
    }

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
