using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The fortress towns' houses (the director, 7 Oct 2026: "these are maritime Canada towns, the buildings must look like
/// Maritimes buildings ... fully interior modeled and explorable for some of them"; note 281). First pass, before the
/// director's references: the vernacular of Nova Scotia and the Island in kit pieces. Painted clapboard (barn red,
/// ochre, slate blue, sage, white) with white corner boards and window trim on a fieldstone foundation, steep roofs, a
/// brick chimney, in four forms:
/// <list type="bullet">
/// <item>0, a storey and a half with a centre gable over the door (the "Island" farmhouse, Gothic Revival);</item>
/// <item>1, a two-storey saltbox, its roof running long and low to the back;</item>
/// <item>2, a storey and a half under a gambrel roof;</item>
/// <item>3, a two-storey house with a Lunenburg bump: a bay standing out over the door up into the roof.</item>
/// </list>
/// Fronts −Z, the house's middle at the origin, X along its front. An open house's (<see cref="HouseLayout"/>) ground
/// floor is built inside it from the same layout the walls are (the Sim's), its X the layout's u times the side.
/// </summary>
public static class MaritimeKit
{
    /// <summary>The paints (faded by the dark and the salt): barn red, ochre, slate blue, sage, white, grey shingle, bottle green.</summary>
    public static readonly Vector3[] Paints =
    [
        new(0.42f, 0.12f, 0.09f), new(0.56f, 0.42f, 0.17f), new(0.24f, 0.31f, 0.40f), new(0.35f, 0.41f, 0.32f),
        new(0.72f, 0.71f, 0.66f), new(0.36f, 0.34f, 0.31f), new(0.14f, 0.22f, 0.17f),
    ];

    static readonly Vector3 Trim = new(0.80f, 0.78f, 0.72f);

    /// <summary>The plaster inside, painted a faded green (the director's references will say what goes on a Maritime kitchen's walls).</summary>
    static readonly Vector3 Plaster = new(0.95f, 1.0f, 0.82f);

    /// <summary>
    /// The lamp by a lived-in house's door (the kit's frame): on the wall beside it, at the top of the door, so the
    /// clapboard round the door reads at night. <paramref name="doorX"/> is where the door is along the front.
    /// </summary>
    public static Vector3 Porch(float doorX, float depth, float doorHeight) =>
        new(doorX + (float)HouseLayout.DoorWidth / 2 + 0.4f, doorHeight + 0.1f, -depth / 2 - 0.16f);

    /// <summary>The fieldstone foundation's height out of the ground (m): the clapboard starts on it.</summary>
    const float Found = 0.35f;

    /// <summary>A window's sill height off the ground, and a ground-floor window's middle (m).</summary>
    const float GroundWindow = Found + 1.25f;

    /// <summary>How high a form's walls go at the front and back, and its ridge (m).</summary>
    static (float Front, float Back, float Ridge) Heights(int style, float depth) => style switch
    {
        1 => (5.6f, 3.0f, 6.9f),
        2 => (3.3f, 3.3f, 3.3f + depth * 0.45f),
        3 => (5.4f, 5.4f, 5.4f + depth * 0.32f),
        _ => (3.5f, 3.5f, 3.5f + depth * 0.5f),
    };

    /// <summary>
    /// A house from outside: <paramref name="kind"/> how it's left (lived in, boarded, burnt, empty); <paramref name="lit"/>
    /// lamplight behind some of its windows (a town whose custom keeps windows dark has none).
    /// </summary>
    public static MeshAsset House(Look? look, HouseKind kind, int style, int paint, float width, float depth, bool lit)
    {
        var k = new Kit(look, 3000 + style * 17 + paint * 5 + (int)kind);
        if (kind == HouseKind.Burnt)
        {
            Burnt(k, width, depth, style);
            return k.Build($"maritime-burnt-{style}-{width:0.0}x{depth:0.0}");
        }
        Outside(k, kind, style, paint, width, depth, lit, doorX: 0, layout: null, side: 1);
        return k.Build($"maritime-{kind}-{style}-{paint}-{width:0.0}x{depth:0.0}-{lit}");
    }

    /// <summary>
    /// An open house (note 281): its outside with the front door standing open, and its ground floor inside, built to the
    /// Sim's <see cref="HouseLayout"/> on its side of the line; <paramref name="thing"/> is its household's own thing (by
    /// kind), set where the layout puts it.
    /// </summary>
    public static MeshAsset Open(Look? look, TownHouse h, string? thing, bool lit)
    {
        var l = h.Layout!;
        float w = (float)h.Width, d = (float)h.Depth;
        var k = new Kit(look, 3500 + h.Id);
        var windows = Outside(k, HouseKind.Open, h.Style, h.Paint, w, d, lit, (float)(h.Side * l.DoorU), l, h.Side);
        Inside(k, h, l, thing, windows);
        return k.Build($"maritime-open-{h.Id}-{h.S:0}-{h.D:0}-{h.Style}-{h.Paint}");
    }

    /// <summary>
    /// Where an open house's lamps and fires are (the kit's frame, as <see cref="Open"/> builds it): the oil lamp on the
    /// kitchen table, the small one on the parlour cabinet, the range's firebox, the line of light under the stair door.
    /// WorldArt lights them while you're near.
    /// </summary>
    public static IEnumerable<(Vector3 At, Vector3 Colour, float Range)> Lights(TownHouse h)
    {
        if (h.Layout is not { } l)
            yield break;
        int kx = l.Kitchen * h.Side;
        foreach (var x in l.Things)
        {
            var (cx, cz) = (X(h, x.U), Z(h, x.V));
            switch (x.Kind)
            {
                case "table":
                    yield return (new Vector3(cx, (float)x.Height + 0.35f, cz), Palette.LampAmber * 0.95f, 5.5f);
                    break;
                case "cabinet":
                    yield return (new Vector3(cx + (float)x.HalfU * 0.6f * Math.Sign(cx), (float)x.Height + 0.4f, cz - 0.4f), Palette.LampAmber * 0.5f, 3.5f);
                    break;
                case "lampstand":
                    // The parlour's own lamp, on its stand by the chair.
                    yield return (new Vector3(cx, 1.3f, cz), Palette.LampAmber * 0.9f, 4.5f);
                    break;
                case "stove":
                    yield return (new Vector3(cx - kx * ((float)x.HalfU + 0.25f), 0.45f, cz), Palette.FurnaceOrange * 0.55f, 2.4f);
                    break;
                case "stairs":
                    yield return (new Vector3(cx + kx * ((float)x.HalfU + 0.2f), 0.08f, cz), Palette.LampAmber * 0.3f, 1.4f);
                    break;
            }
        }
    }

    static float X(TownHouse h, double u) => (float)(h.Side * u);
    static float Z(TownHouse h, double v) => (float)(v - h.Depth / 2);

    /// <summary>A window: its middle on the wall's outer face, the way it faces out, its size, and whether it's the ground floor's.</summary>
    readonly record struct Pane(Vector3 At, Vector3 Out, float W, float H, bool Ground);

    /// <summary>
    /// The outside: foundation, clapboard, gable ends, trim, windows, the door, the roof and its chimney. An open house's
    /// (<paramref name="layout"/> not null) walls are thin, the door's opening goes down to the ground (the Sim's floor is
    /// the ground), and its windows keep clear of the partition and the furniture against the walls. The windows made.
    /// </summary>
    static List<Pane> Outside(Kit k, HouseKind kind, int style, int paint, float w, float d, bool lit, float doorX, HouseLayout? layout, int side)
    {
        var (front, back, ridge) = Heights(style, d);
        float x0 = -w / 2, x1 = w / 2, z0 = -d / 2, z1 = d / 2;
        bool open = layout is not null;
        const float t = (float)HouseLayout.Wall;
        float dw = (float)HouseLayout.DoorWidth, doorH = k.DoorHeight();
        // The fieldstone foundation, a little proud of the walls: a ring, broken for an open door.
        k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1.2f);
        // (No deeper than the wall itself: inside an open house the floor runs to the wall's inner face.)
        const float ring = (float)HouseLayout.Wall, proud = 0.06f;
        var lo = new Vector3(x0 - proud, -0.4f, z0 - proud);
        var hi = new Vector3(x1 + proud, Found, z1 + proud);
        if (open)
        {
            k.Box(lo, new Vector3(doorX - dw / 2, Found, z0 + ring), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(doorX + dw / 2, -0.4f, z0 - proud), new Vector3(hi.X, Found, z0 + ring), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(lo.X, -0.4f, z1 - ring), hi, Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(lo.X, -0.4f, z0 + ring), new Vector3(x0 + ring, Found, z1 - ring), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(x1 - ring, -0.4f, z0 + ring), new Vector3(hi.X, Found, z1 - ring), Kit.Faces.Sides | Kit.Faces.PosY);
        }
        else
            k.Box(lo, hi, Kit.Faces.Sides | Kit.Faces.PosY);
        // The clapboard, in the house's paint.
        Siding(k, paint, kind);
        float low = Math.Min(front, back);
        if (!open)
        {
            Clap(k, new Vector3(x0, Found, z0), new Vector3(x1, front, z1), Kit.Faces.NegZ);
            Clap(k, new Vector3(x0, Found, z0), new Vector3(x1, back, z1), Kit.Faces.PosZ);
            Clap(k, new Vector3(x0, Found, z0), new Vector3(x1, low, z1), Kit.Faces.PosX | Kit.Faces.NegX);
        }
        else
        {
            // Thin walls, the outer layer (Inside lines them): the door's opening down to the ground, its lintel over it.
            Clap(k, new Vector3(x0, Found, z0), new Vector3(doorX - dw / 2, front, z0 + t), Kit.Faces.All);
            Clap(k, new Vector3(doorX + dw / 2, Found, z0), new Vector3(x1, front, z0 + t), Kit.Faces.All);
            Clap(k, new Vector3(doorX - dw / 2, doorH, z0), new Vector3(doorX + dw / 2, front, z0 + t), Kit.Faces.All);
            Clap(k, new Vector3(x0, Found, z1 - t), new Vector3(x1, back, z1), Kit.Faces.All);
            Clap(k, new Vector3(x0, Found, z0), new Vector3(x0 + t, low, z1), Kit.Faces.All);
            Clap(k, new Vector3(x1 - t, Found, z0), new Vector3(x1, low, z1), Kit.Faces.All);
        }
        // The gable ends over the side walls (and a saltbox's sloping side), clapboard to the roof.
        foreach (float x in new[] { x0, x1 })
            GableEnd(k, style, x, z0, z1, front, back, ridge, twoSided: open);
        // White corner boards and the fascia under the eaves.
        k.Use("paint_black", Trim, 0.6f, 0.1f);
        k.Tint = Trim;
        foreach (float x in new[] { x0, x1 })
            foreach (float z in new[] { z0, z1 })
                k.Box(new Vector3(x - 0.06f, Found, z - 0.06f), new Vector3(x + 0.06f, z < 0 ? front : back, z + 0.06f));
        k.Box(new Vector3(x0 - 0.1f, front - 0.22f, z0 - 0.08f), new Vector3(x1 + 0.1f, front, z0 + 0.02f));
        k.Box(new Vector3(x0 - 0.1f, back - 0.22f, z1 - 0.02f), new Vector3(x1 + 0.1f, back, z1 + 0.08f));
        // The windows: two-over-two in white trim, as many as fit either side of the door, upstairs on a two-storey house,
        // in the gable ends and at the back. Lamplight behind some (lived in), dark glass (nobody), or boards.
        var panes = Windows(style, w, d, front, back, ridge, doorX, layout, side);
        int n = 0;
        foreach (var p in panes)
        {
            bool glow = lit && kind switch
            {
                HouseKind.Lived => n++ % 3 != 1,
                // An open house's lamps are downstairs: upstairs, nobody's gone up to bed.
                HouseKind.Open => p.Ground,
                _ => false,
            };
            Window(k, p, kind, glow);
        }
        // The front door: painted panel in its trim (open: swung in, Inside), boarded, or gone (an empty house's is). A
        // house lived in keeps a lamp burning by it (WorldArt lights it, Porch).
        Door(k, kind, doorX, z0, doorH, open);
        if (kind is HouseKind.Lived or HouseKind.Open)
        {
            var lamp = Porch(doorX, d, doorH);
            k.Use("iron_smokebox", Palette.SootBlack, 0.6f, 0.3f, tile: 1);
            k.Box(new Vector3(lamp.X - 0.03f, lamp.Y + 0.12f, z0 - 0.02f), new Vector3(lamp.X + 0.03f, lamp.Y + 0.17f, lamp.Z));
            k.Box(new Vector3(lamp.X - 0.09f, lamp.Y + 0.12f, lamp.Z - 0.09f), new Vector3(lamp.X + 0.09f, lamp.Y + 0.15f, lamp.Z + 0.09f));
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
            k.Emissive = lit ? 1 : 0.35f;
            k.BoxAt(lamp, new Vector3(0.07f, 0.11f, 0.07f));
            k.Emissive = 0;
        }
        // The roof, its form's, and the chimney through it.
        k.Use("roof_slate", Palette.Charcoal, 0.85f, 0.15f, tile: 1.4f);
        Roof(k, style, x0, x1, z0, z1, front, back, ridge);
        if (style == 0)
            IslandGable(k, front, ridge, z0, kind, lit && kind == HouseKind.Lived);
        if (style == 3)
            Bump(k, front, z0, paint, kind, lit && kind == HouseKind.Lived);
        k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1.2f);
        float cx = style == 1 ? 0 : x1 * 0.45f, cz = style == 1 ? z0 + d * 0.4f : 0;
        k.Box(new Vector3(cx - 0.35f, ridge - 1.2f, cz - 0.3f), new Vector3(cx + 0.35f, ridge + 1.0f, cz + 0.3f), Kit.Faces.All & ~Kit.Faces.NegY);
        return panes;
    }

    /// <summary>
    /// Where a house's windows go: along each wall, in the stretches clear of its corners, its door, and (an open house)
    /// the partition and anything standing against that wall inside, as many as fit up to two a stretch.
    /// </summary>
    static List<Pane> Windows(int style, float w, float d, float front, float back, float ridge, float doorX, HouseLayout? layout, int side)
    {
        float x0 = -w / 2, x1 = w / 2, z0 = -d / 2, z1 = d / 2;
        float dh = (float)HouseLayout.DoorWidth / 2;
        var panes = new List<Pane>();
        // What stands against a wall inside (an open house), as a stretch along it: (wall, from, to).
        var against = new List<(int Wall, float A, float B)>();
        if (layout is not null)
        {
            const float near = 0.5f;
            foreach (var x in layout.Things.Where(x => x.Height > 0.8))
            {
                float cx = (float)(side * x.U), cz = (float)(x.V - d / 2), hx = (float)x.HalfU, hz = (float)x.HalfV;
                if (cz + hz > z1 - near)
                    against.Add((1, cx - hx, cx + hx));
                if (cx - hx < x0 + near)
                    against.Add((2, cz - hz, cz + hz));
                if (cx + hx > x1 - near)
                    against.Add((3, cz - hz, cz + hz));
            }
            // The partition meets the front and back walls in the middle.
            against.Add((0, -0.4f, 0.4f));
            against.Add((1, -0.4f, 0.4f));
        }
        // Wall 0 the front, 1 the back, 2 the −x end, 3 the +x end.
        IEnumerable<float> Fit(int wall, float a, float b, int most, params (float A, float B)[] also)
        {
            var blocks = against.Where(x => x.Wall == wall).Select(x => (x.A, x.B)).Concat(also).OrderBy(x => x.A).ToList();
            float from = a + 0.45f;
            foreach (var (ba, bb) in blocks.Append((b - 0.45f, b)))
            {
                float to = ba;
                float len = to - from;
                int count = Math.Clamp((int)((len + 0.5f) / 1.5f), 0, most);
                if (count == 1)
                    yield return from + len / 2;
                else if (count == 2)
                {
                    yield return from + len / 4;
                    yield return from + len * 3 / 4;
                }
                from = Math.Max(from, bb);
            }
        }
        var noDoor = (doorX - dh - 0.3f, doorX + dh + 0.3f);
        foreach (float x in Fit(0, x0, x1, 2, noDoor))
            panes.Add(new(new Vector3(x, GroundWindow, z0), -Vector3.UnitZ, 0.8f, 1.2f, true));
        // Upstairs at the front: a two-storey house's (not behind a Lunenburg bump).
        if (front > 5)
            foreach (float x in Fit(-1, x0, x1, 3, style == 3 ? [(-1.3f, 1.3f)] : []))
                panes.Add(new(new Vector3(x, front - 1.5f, z0), -Vector3.UnitZ, 0.8f, 1.2f, false));
        foreach (float x in Fit(1, x0, x1, 1, layout is null ? [(-0.4f, 0.4f)] : []))
            panes.Add(new(new Vector3(x, GroundWindow, z1), Vector3.UnitZ, 0.8f, 1.2f, true));
        if (back > 5)
            panes.Add(new(new Vector3(x1 - w * 0.3f, back - 1.5f, z1), Vector3.UnitZ, 0.8f, 1.2f, false));
        // The gable ends: one downstairs, one up in the gable.
        foreach (int wall in new[] { 2, 3 })
        {
            float x = wall == 2 ? x0 : x1;
            var outward = wall == 2 ? -Vector3.UnitX : Vector3.UnitX;
            foreach (float z in Fit(wall, z0, z1, 1).Take(1))
                panes.Add(new(new Vector3(x, GroundWindow, z), outward, 0.8f, 1.2f, true));
            float gz = style == 1 ? z0 + d * 0.3f : 0;
            float gy = style switch { 1 => 4.3f, 3 => front - 1.5f, _ => front + Math.Min(0.75f, (ridge - front) * 0.3f) };
            panes.Add(new(new Vector3(x, gy, gz), outward, 0.7f, 1.0f, false));
        }
        return panes;
    }

    /// <summary>
    /// A box of clapboard: <see cref="Kit.Box(Vector3, Vector3, Kit.Faces)"/>'s faces with the siding's boards turned to
    /// run across the wall (the texture's run up it), as a Maritime house's clapboard does.
    /// </summary>
    internal static void Clap(Kit k, Vector3 min, Vector3 max, Kit.Faces faces)
    {
        var (x0, y0, z0, x1, y1, z1) = (min.X, min.Y, min.Z, max.X, max.Y, max.Z);
        float lo = 1 - k.Baked;
        void Side(Vector3 a, Vector3 b, float ua, float ub)
        {
            var at = a with { Y = y1 };
            var bt = b with { Y = y1 };
            k.Tri(at, a, b, new(-y1, ua), new(-y0, ua), new(-y0, ub), 1, lo, lo);
            k.Tri(at, b, bt, new(-y1, ua), new(-y0, ub), new(-y1, ub), 1, lo, 1);
        }
        if (faces.HasFlag(Kit.Faces.PosZ))
            Side(new(x0, y0, z1), new(x1, y0, z1), x0, x1);
        if (faces.HasFlag(Kit.Faces.NegZ))
            Side(new(x1, y0, z0), new(x0, y0, z0), -x1, -x0);
        if (faces.HasFlag(Kit.Faces.PosX))
            Side(new(x1, y0, z1), new(x1, y0, z0), -z1, -z0);
        if (faces.HasFlag(Kit.Faces.NegX))
            Side(new(x0, y0, z0), new(x0, y0, z1), z0, z1);
        if ((faces & (Kit.Faces.PosY | Kit.Faces.NegY)) != 0)
            k.Box(min, max, faces & (Kit.Faces.PosY | Kit.Faces.NegY));
    }

    static void Siding(Kit k, int paint, HouseKind kind)
    {
        k.Use("wood_siding", Paints[paint % Paints.Length], 0.85f, 0.05f, tile: 0.9f);
        // Nobody's painted a house nobody lives in since: it's gone grey under the paint.
        k.Tint = Paints[paint % Paints.Length] * (kind is HouseKind.Lived or HouseKind.Open ? 2.3f : 1.4f);
    }

    /// <summary>A gable end at <paramref name="x"/>: the wall's top up to the roofline, as the form's roof runs.</summary>
    static void GableEnd(Kit k, int style, float x, float z0, float z1, float front, float back, float ridge, bool twoSided)
    {
        var pts = Profile(style, z0, z1, front, back, ridge);
        float baseY = Math.Min(front, back);
        // Fan from the middle of the wall's top.
        var c = new Vector3(x, baseY, (z0 + z1) / 2);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = new Vector3(x, pts[i].Y, pts[i].X);
            var b = new Vector3(x, pts[i + 1].Y, pts[i + 1].X);
            if (a.Y <= baseY + 1e-3f && b.Y <= baseY + 1e-3f)
                continue;
            var aa = a with { Y = Math.Max(a.Y, baseY) };
            var bb = b with { Y = Math.Max(b.Y, baseY) };
            Wedge(k, x, aa, bb, c, twoSided);
        }
        // A saltbox's high front wall runs above its low back: the side wall's sloped part under the roof.
        if (front > back + 0.1f)
            Wedge(k, x, new Vector3(x, back, z0), new Vector3(x, front, z0), new Vector3(x, back, z1), twoSided);
    }

    /// <summary>A triangle of an end wall at <paramref name="x"/>, facing out from the house (and in, for an open one).</summary>
    static void Wedge(Kit k, float x, Vector3 a, Vector3 b, Vector3 c, bool twoSided)
    {
        // (The clapboard's boards across, as Clap turns them.)
        if (x < 0 || twoSided)
            k.Tri(a, b, c, new(-a.Y, a.Z), new(-b.Y, b.Z), new(-c.Y, c.Z));
        if (x >= 0 || twoSided)
            k.Tri(b, a, c, new(-b.Y, -b.Z), new(-a.Y, -a.Z), new(-c.Y, -c.Z));
    }

    /// <summary>The roof's line across the house (z, y), front to back.</summary>
    static List<Vector2> Profile(int style, float z0, float z1, float front, float back, float ridge)
    {
        float d = z1 - z0;
        return style switch
        {
            // Saltbox: the ridge two-fifths back, the long slope down to the low back wall.
            1 => [new(z0, front), new(z0 + d * 0.4f, ridge), new(z1, back)],
            // Gambrel: steep, then shallow, to the ridge, and back down.
            2 => [new(z0, front), new(z0 + d * 0.18f, front + (ridge - front) * 0.7f), new(0, ridge), new(z1 - d * 0.18f, back + (ridge - back) * 0.7f), new(z1, back)],
            _ => [new(z0, front), new(0, ridge), new(z1, back)],
        };
    }

    static void Roof(Kit k, int style, float x0, float x1, float z0, float z1, float front, float back, float ridge)
    {
        var pts = Profile(style, z0, z1, front, back, ridge);
        const float over = 0.35f;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            // Carried out past the walls at the eaves and the gable ends.
            var dir = Vector2.Normalize(b - a);
            if (i == 0)
                a -= dir * over;
            if (i + 2 == pts.Count)
                b += dir * over;
            k.Quad(new Vector3(x0 - over, a.Y, a.X), new Vector3(x1 + over, a.Y, a.X), new Vector3(x1 + over, b.Y, b.X), new Vector3(x0 - over, b.Y, b.X), twoSided: true);
        }
    }

    /// <summary>Style 0's centre gable over the door: a little steep gable wall standing up out of the roof, its window under the point.</summary>
    static void IslandGable(Kit k, float front, float ridge, float z0, HouseKind kind, bool lit)
    {
        float hw = 1.0f, top = Math.Min(ridge - 0.2f, front + 1.9f);
        k.Use("wood_siding", Palette.BlueGrey, 0.85f, 0.05f, tile: 1.8f);
        k.Tint = Vector3.One * 0.9f;
        var a = new Vector3(-hw, front, z0);
        var b = new Vector3(hw, front, z0);
        var c = new Vector3(0, top, z0);
        k.Tri(a, c, b, new(-a.Y, a.X), new(-c.Y, c.X), new(-b.Y, b.X));
        // Its little roof back into the main one.
        k.Use("roof_slate", Palette.Charcoal, 0.85f, 0.15f, tile: 1.4f);
        float back = z0 + 1.6f;
        k.Quad(new Vector3(-hw - 0.2f, front - 0.15f, z0 - 0.2f), c + new Vector3(0, 0.12f, -0.2f), c + new Vector3(0, 0.12f, back - z0), new Vector3(-hw - 0.2f, front - 0.15f, back), twoSided: true);
        k.Quad(c + new Vector3(0, 0.12f, -0.2f), new Vector3(hw + 0.2f, front - 0.15f, z0 - 0.2f), new Vector3(hw + 0.2f, front - 0.15f, back), c + new Vector3(0, 0.12f, back - z0), twoSided: true);
        Window(k, new Pane(new Vector3(0, front + 0.45f, z0), -Vector3.UnitZ, 0.55f, 0.8f, false), kind, lit);
    }

    /// <summary>Style 3's Lunenburg bump: a bay out over the door, from the first floor's top up into the roof, with a gable.</summary>
    static void Bump(Kit k, float front, float z0, int paint, HouseKind kind, bool lit)
    {
        float hw = 1.15f, bot = 2.6f, top = front + 0.9f, out_ = 0.7f;
        Siding(k, paint, kind);
        Clap(k, new Vector3(-hw, bot, z0 - out_), new Vector3(hw, top, z0), Kit.Faces.NegZ | Kit.Faces.PosX | Kit.Faces.NegX | Kit.Faces.NegY);
        Window(k, new Pane(new Vector3(0, front - 1.5f, z0 - out_), -Vector3.UnitZ, 0.8f, 1.2f, false), kind, lit);
        Window(k, new Pane(new Vector3(0, top - 0.55f, z0 - out_), -Vector3.UnitZ, 0.5f, 0.7f, false), kind, lit);
        k.Use("roof_slate", Palette.Charcoal, 0.85f, 0.15f, tile: 1.4f);
        var c = new Vector3(0, top + 0.9f, z0 - out_ - 0.15f);
        k.Quad(new Vector3(-hw - 0.15f, top, z0 - out_ - 0.15f), c, c with { Z = z0 + 0.6f }, new Vector3(-hw - 0.15f, top, z0 + 0.6f), twoSided: true);
        k.Quad(c, new Vector3(hw + 0.15f, top, z0 - out_ - 0.15f), new Vector3(hw + 0.15f, top, z0 + 0.6f), c with { Z = z0 + 0.6f }, twoSided: true);
        k.Use("paint_black", Trim, 0.6f, 0.1f);
        k.Tint = Trim;
        var ga = new Vector3(-hw, top, z0 - out_ - 0.01f);
        var gb = new Vector3(hw, top, z0 - out_ - 0.01f);
        var gc = new Vector3(0, top + 0.85f, z0 - out_ - 0.01f);
        k.Tri(ga, gc, gb, new(ga.X, -ga.Y), new(gc.X, -gc.Y), new(gb.X, -gb.Y));
    }

    /// <summary>A box on a wall facing <paramref name="n"/>: <paramref name="ha"/> either way along it, <paramref name="hu"/> up and down, from <paramref name="d0"/> to <paramref name="d1"/> out.</summary>
    static void Slab(Kit k, Vector3 c, Vector3 n, float ha, float hu, float d0, float d1)
    {
        var along = Vector3.Cross(Vector3.UnitY, n);
        var p = c - along * ha - Vector3.UnitY * hu + n * d0;
        var q = c + along * ha + Vector3.UnitY * hu + n * d1;
        k.Box(Vector3.Min(p, q), Vector3.Max(p, q));
    }

    /// <summary>A window in its white trim on a wall's face: lamplit glass (<paramref name="lit"/>) or dark, its sash bars, boards over a boarded house's.</summary>
    static void Window(Kit k, Pane p, HouseKind kind, bool lit)
    {
        var (c, n, ww, wh) = (p.At, p.Out, p.W, p.H);
        var along = Vector3.Cross(Vector3.UnitY, n);
        k.Use("paint_black", Trim, 0.6f, 0.1f);
        k.Tint = Trim;
        Slab(k, c - Vector3.UnitY * (wh / 2 + 0.06f), n, ww / 2 + 0.1f, 0.06f, 0, 0.06f);
        Slab(k, c + Vector3.UnitY * (wh / 2 + 0.07f), n, ww / 2 + 0.1f, 0.07f, 0, 0.06f);
        Slab(k, c - along * (ww / 2 + 0.05f), n, 0.05f, wh / 2, 0, 0.05f);
        Slab(k, c + along * (ww / 2 + 0.05f), n, 0.05f, wh / 2, 0, 0.05f);
        if (lit)
        {
            k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
            k.Emissive = 0.85f;
        }
        else
        {
            k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
            k.Shade(0.35f);
        }
        k.Panel(c + n * 0.01f, n, Vector3.UnitY, ww, wh, Vector2.Zero, Vector2.One);
        k.Emissive = 0;
        // The sash bars: two over two.
        k.Use("paint_black", Trim, 0.6f, 0.1f);
        k.Tint = Trim * 0.9f;
        Slab(k, c, n, 0.02f, wh / 2, 0, 0.04f);
        Slab(k, c, n, ww / 2, 0.02f, 0, 0.04f);
        if (kind == HouseKind.Boarded)
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.95f, 0, tile: 1);
            for (int i = 0; i < 3; i++)
                Slab(k, c + Vector3.UnitY * (-wh / 2 + 0.2f + i * (wh - 0.4f) / 2), n, ww / 2 + 0.15f, 0.09f, 0.04f, 0.09f);
        }
    }

    static void Door(Kit k, HouseKind kind, float doorX, float z0, float doorH, bool open)
    {
        float dw = (float)HouseLayout.DoorWidth, sill = open ? 0 : Found;
        k.Use("paint_black", Trim, 0.6f, 0.1f);
        k.Tint = Trim;
        k.Box(new Vector3(doorX - dw / 2 - 0.12f, sill, z0 - 0.05f), new Vector3(doorX - dw / 2, doorH + 0.12f, z0 + 0.01f));
        k.Box(new Vector3(doorX + dw / 2, sill, z0 - 0.05f), new Vector3(doorX + dw / 2 + 0.12f, doorH + 0.12f, z0 + 0.01f));
        k.Box(new Vector3(doorX - dw / 2 - 0.12f, doorH, z0 - 0.05f), new Vector3(doorX + dw / 2 + 0.12f, doorH + 0.14f, z0 + 0.01f));
        // The step up to it: one granite slab.
        k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1);
        k.Box(new Vector3(doorX - 0.7f, 0, z0 - 0.55f), new Vector3(doorX + 0.7f, open ? 0.06f : 0.18f, z0));
        if (open)
            return;
        if (kind == HouseKind.Empty)
        {
            // Gone: the dark inside.
            k.Use("paint_black", Palette.SootBlack, 0.9f, 0);
            k.Shade(0.15f);
            k.Panel(new Vector3(doorX, doorH / 2, z0 - 0.02f), -Vector3.UnitZ, Vector3.UnitY, dw, doorH, Vector2.Zero, Vector2.One);
            return;
        }
        k.Use("paint_oxide", Palette.RustRed, 0.8f, 0.1f, tile: 1);
        k.Box(new Vector3(doorX - dw / 2, 0.05f, z0 - 0.04f), new Vector3(doorX + dw / 2, doorH, z0));
        if (kind == HouseKind.Boarded)
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.95f, 0, tile: 1);
            k.Box(new Vector3(doorX - dw / 2 - 0.2f, doorH * 0.35f, z0 - 0.1f), new Vector3(doorX + dw / 2 + 0.2f, doorH * 0.35f + 0.18f, z0 - 0.04f));
            k.Box(new Vector3(doorX - dw / 2 - 0.2f, doorH * 0.7f, z0 - 0.1f), new Vector3(doorX + dw / 2 + 0.2f, doorH * 0.7f + 0.18f, z0 - 0.04f));
        }
    }

    /// <summary>A house burnt out: its walls to the sills and ragged, charred black, the chimney standing on its own.</summary>
    static void Burnt(Kit k, float w, float d, int style)
    {
        k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1.2f);
        k.Box(new Vector3(-w / 2 - 0.06f, -0.4f, -d / 2 - 0.06f), new Vector3(w / 2 + 0.06f, Found, d / 2 + 0.06f), Kit.Faces.Sides | Kit.Faces.PosY);
        k.Use("wood_grey", Palette.SootBlack, 0.95f, 0, tile: 1);
        k.Tint = new Vector3(0.12f, 0.1f, 0.09f);
        var rng = new Random(77 + style);
        for (float x = -w / 2; x < w / 2 - 0.1f; x += 0.9f)
            foreach (float z in new[] { -d / 2, d / 2 - 0.15f })
                k.Box(new Vector3(x, Found, z), new Vector3(Math.Min(x + 0.9f, w / 2), Found + 0.3f + (float)rng.NextDouble() * 1.4f, z + 0.15f));
        for (float z = -d / 2; z < d / 2 - 0.1f; z += 0.9f)
            foreach (float x in new[] { -w / 2, w / 2 - 0.15f })
                k.Box(new Vector3(x, Found, z), new Vector3(x + 0.15f, Found + 0.3f + (float)rng.NextDouble() * 1.2f, Math.Min(z + 0.9f, d / 2)));
        // A charred beam or two fallen across.
        k.Rod(new Vector3(-w / 2 + 0.3f, Found + 0.2f, -d / 2 + 0.5f), new Vector3(w / 2 - 0.6f, Found + 0.9f, d / 2 - 0.4f), 0.1f);
        k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1.2f);
        k.Tint = new Vector3(0.35f, 0.25f, 0.2f);
        k.Box(new Vector3(w / 2 * 0.45f - 0.35f, 0, -0.3f), new Vector3(w / 2 * 0.45f + 0.35f, Heights(style, d).Ridge + 1.0f, 0.3f), Kit.Faces.All & ~Kit.Faces.NegY);
    }

    /// <summary>
    /// The ground floor inside (note 281), to the layout the Sim stands its walls by: the floor, the plaster inside the
    /// walls with wainscot below and the windows' insides, the partition and its doorway, the ceiling, the boxed stair with
    /// its door shut, the range, the table and chair, the dresser, the cabinet, the armchair, a photograph, the household's
    /// own thing, and the front door swung in.
    /// </summary>
    static void Inside(Kit k, TownHouse h, HouseLayout l, string? thing, List<Pane> windows)
    {
        float w = (float)h.Width, d = (float)h.Depth, x0 = -w / 2, x1 = w / 2, z0 = -d / 2, z1 = d / 2;
        const float t = (float)HouseLayout.Wall, ceil = (float)HouseLayout.Ceiling, floor = 0.02f, skin = 0.03f;
        float doorH = k.DoorHeight();
        int kx = l.Kitchen * h.Side;
        // The floor boards, and the ceiling over the downstairs.
        k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.1f, tile: 1.5f);
        k.Box(new Vector3(x0 + t, -0.05f, z0 + t), new Vector3(x1 - t, floor, z1 - t), Kit.Faces.PosY);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
        k.Tint = new Vector3(0.55f, 0.5f, 0.42f);
        k.Box(new Vector3(x0 + t, ceil, z0 + t), new Vector3(x1 - t, ceil + 0.05f, z1 - t), Kit.Faces.NegY);
        // Plaster inside the walls, papered a faded green, with tongue-and-groove wainscot below.
        void InnerWall(Vector3 min, Vector3 max)
        {
            k.Use("concrete_stain", Palette.MuddyOlive, 0.9f, 0.02f, tile: 1.5f);
            k.Tint = Plaster;
            k.Box(min with { Y = 1.0f }, max with { Y = Math.Min(max.Y, ceil) });
            k.Use("wood_siding", Palette.DeepBrown, 0.85f, 0.05f, tile: 1);
            k.Tint = new Vector3(0.5f, 0.4f, 0.3f);
            k.Box(min with { Y = floor }, max with { Y = Math.Min(1.0f, max.Y) });
        }
        float dx = X(h, l.DoorU), dh = (float)HouseLayout.DoorWidth / 2, ph = (float)HouseLayout.PassWidth / 2;
        InnerWall(new Vector3(x0 + t, 0, z0 + t), new Vector3(dx - dh, ceil, z0 + t + skin));
        InnerWall(new Vector3(dx + dh, 0, z0 + t), new Vector3(x1 - t, ceil, z0 + t + skin));
        k.Use("concrete_stain", Palette.MuddyOlive, 0.9f, 0.02f, tile: 1.5f);
        k.Tint = Plaster;
        k.Box(new Vector3(dx - dh, doorH, z0 + t), new Vector3(dx + dh, ceil, z0 + t + skin));
        InnerWall(new Vector3(x0 + t, 0, z1 - t - skin), new Vector3(x1 - t, ceil, z1 - t));
        InnerWall(new Vector3(x0 + t, 0, z0 + t), new Vector3(x0 + t + skin, ceil, z1 - t));
        InnerWall(new Vector3(x1 - t - skin, 0, z0 + t), new Vector3(x1 - t, ceil, z1 - t));
        // The ground floor's windows from inside: the night through the glass, in the same white trim.
        foreach (var p in windows.Where(p => p.Ground))
        {
            var inner = p.At - p.Out * (t + skin);
            k.Use("paint_black", Trim, 0.6f, 0.1f);
            k.Tint = Trim * 0.8f;
            Slab(k, inner - Vector3.UnitY * (p.H / 2 + 0.05f), -p.Out, p.W / 2 + 0.08f, 0.05f, 0, 0.1f);
            Slab(k, inner + Vector3.UnitY * (p.H / 2 + 0.05f), -p.Out, p.W / 2 + 0.08f, 0.05f, 0, 0.04f);
            var along = Vector3.Cross(Vector3.UnitY, p.Out);
            Slab(k, inner - along * (p.W / 2 + 0.04f), -p.Out, 0.04f, p.H / 2, 0, 0.04f);
            Slab(k, inner + along * (p.W / 2 + 0.04f), -p.Out, 0.04f, p.H / 2, 0, 0.04f);
            k.Use("glass_dirty", Palette.BlueGrey, 0.4f, 0.4f, tile: 1);
            k.Shade(0.12f);
            k.Panel(inner - p.Out * 0.005f, -p.Out, Vector3.UnitY, p.W, p.H, Vector2.Zero, Vector2.One);
            k.Use("paint_black", Trim, 0.6f, 0.1f);
            k.Tint = Trim * 0.7f;
            Slab(k, inner, -p.Out, 0.02f, p.H / 2, 0, 0.03f);
            Slab(k, inner, -p.Out, p.W / 2, 0.02f, 0, 0.03f);
        }
        // The partition at the middle, the doorway through it, its lintel.
        float pz = Z(h, l.PassV);
        InnerWall(new Vector3(-t / 2, 0, z0 + t), new Vector3(t / 2, ceil, pz - ph));
        InnerWall(new Vector3(-t / 2, 0, pz + ph), new Vector3(t / 2, ceil, z1 - t));
        k.Use("concrete_stain", Palette.MuddyOlive, 0.9f, 0.02f, tile: 1.5f);
        k.Tint = Plaster;
        k.Box(new Vector3(-t / 2, doorH, pz - ph), new Vector3(t / 2, ceil, pz + ph));
        // The front door, swung in against the wall on its hinges' side (toward the partition).
        k.Use("paint_oxide", Palette.RustRed, 0.8f, 0.1f, tile: 1);
        float hinge = dx - Math.Sign(dx) * dh;
        k.Box(new Vector3(Math.Min(hinge, hinge + Math.Sign(dx) * 0.05f), floor, z0 + t + skin), new Vector3(Math.Max(hinge, hinge + Math.Sign(dx) * 0.05f), doorH, z0 + t + skin + 2 * dh));
        foreach (var x in l.Things)
            Thing(k, x, X(h, x.U), Z(h, x.V), kx, doorH);
        if (thing is not null)
        {
            var (u, v, ht) = l.Place(thing, h.Width, h.Depth);
            Small(k, thing, X(h, u), Z(h, v), (float)ht);
        }
    }

    /// <summary>A piece of the layout's furniture, at its place (<paramref name="cx"/>, <paramref name="cz"/>) in the kit's frame; <paramref name="kx"/> the way to the kitchen along x.</summary>
    static void Thing(Kit k, HouseThing x, float cx, float cz, int kx, float doorH)
    {
        // A thing's half-extent along u is along x either way (the side flips the sign, not the axis).
        float hx = (float)x.HalfU, hz = (float)x.HalfV, ht = (float)x.Height;
        switch (x.Kind)
        {
            case "stove":
                // The kitchen range: black iron on legs, its warming oven, the pipe up through the ceiling, the firebox's
                // glow on the side toward the room.
                k.Use("iron_smokebox", Palette.SootBlack, 0.6f, 0.4f, tile: 1);
                k.Box(new Vector3(cx - hx, 0.18f, cz - hz), new Vector3(cx + hx, ht, cz + hz));
                foreach (float lx in new[] { cx - hx + 0.06f, cx + hx - 0.06f })
                    foreach (float lz in new[] { cz - hz + 0.06f, cz + hz - 0.06f })
                        k.Box(new Vector3(lx - 0.04f, 0, lz - 0.04f), new Vector3(lx + 0.04f, 0.18f, lz + 0.04f));
                k.Box(new Vector3(cx - hx * 0.8f, ht, cz + hz - 0.15f), new Vector3(cx + hx * 0.8f, ht + 0.45f, cz + hz));
                k.Cylinder(new Vector3(cx, ht, cz + hz * 0.4f), new Vector3(cx, (float)HouseLayout.Ceiling, cz + hz * 0.4f), 0.08f, 8);
                k.Use("ember_crack", Palette.FurnaceOrange, 0.5f, 0);
                k.Emissive = 0.8f;
                k.Panel(new Vector3(cx - kx * (hx + 0.005f), 0.5f, cz), new Vector3(-kx, 0, 0), Vector3.UnitY, 0.3f, 0.2f, Vector2.Zero, Vector2.One);
                k.Emissive = 0;
                break;
            case "table":
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.1f, tile: 1);
                k.Box(new Vector3(cx - hx, ht - 0.04f, cz - hz), new Vector3(cx + hx, ht, cz + hz));
                foreach (float lx in new[] { cx - hx + 0.06f, cx + hx - 0.06f })
                    foreach (float lz in new[] { cz - hz + 0.06f, cz + hz - 0.06f })
                        k.Box(new Vector3(lx - 0.03f, 0, lz - 0.03f), new Vector3(lx + 0.03f, ht - 0.04f, lz + 0.03f));
                // An oil lamp in the middle of it: the kitchen's light (WorldArt lights it, Lights).
                k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.6f);
                k.Cylinder(new Vector3(cx, ht, cz), new Vector3(cx, ht + 0.1f, cz), 0.07f, 10, radiusB: 0.04f);
                k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
                k.Emissive = 1;
                k.Cylinder(new Vector3(cx, ht + 0.1f, cz), new Vector3(cx, ht + 0.32f, cz), 0.05f, 10, radiusB: 0.035f);
                k.Emissive = 0;
                break;
            case "chair":
            case "armchair":
                bool arm = x.Kind == "armchair";
                k.Use(arm ? "wool" : "wood_grey", arm ? Palette.MuddyOlive : Palette.DeepBrown, 0.9f, 0, tile: 1);
                if (arm)
                    k.Tint = new Vector3(0.45f, 0.3f, 0.25f);
                k.Box(new Vector3(cx - hx, 0.42f, cz - hz), new Vector3(cx + hx, 0.48f, cz + hz));
                // The back's on the side away from where its sitter faces (the layout's spots face −v: the back's at +v).
                k.Box(new Vector3(cx - hx, 0.48f, cz + hz - 0.06f), new Vector3(cx + hx, arm ? 1.0f : 0.95f, cz + hz));
                foreach (float lx in new[] { cx - hx + 0.03f, cx + hx - 0.03f })
                    foreach (float lz in new[] { cz - hz + 0.03f, cz + hz - 0.03f })
                        k.Box(new Vector3(lx - 0.025f, 0, lz - 0.025f), new Vector3(lx + 0.025f, 0.42f, lz + 0.025f));
                if (arm)
                    foreach (float lx in new[] { cx - hx, cx + hx - 0.07f })
                        k.Box(new Vector3(lx, 0.48f, cz - hz), new Vector3(lx + 0.07f, 0.68f, cz + hz));
                break;
            case "lampstand":
                // A little stand by the armchair with the parlour's lamp on it (Lights).
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
                k.Tint = new Vector3(0.6f, 0.42f, 0.3f);
                k.Box(new Vector3(cx - hx, 0.66f, cz - hz), new Vector3(cx + hx, 0.7f, cz + hz));
                k.Box(new Vector3(cx - 0.03f, 0, cz - 0.03f), new Vector3(cx + 0.03f, 0.66f, cz + 0.03f));
                k.Box(new Vector3(cx - hx * 0.7f, 0, cz - hz * 0.7f), new Vector3(cx + hx * 0.7f, 0.04f, cz + hz * 0.7f));
                k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.6f);
                k.Cylinder(new Vector3(cx, 0.7f, cz), new Vector3(cx, 0.8f, cz), 0.07f, 10, radiusB: 0.04f);
                k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
                k.Emissive = 1;
                k.Cylinder(new Vector3(cx, 0.8f, cz), new Vector3(cx, 1.02f, cz), 0.05f, 10, radiusB: 0.035f);
                k.Emissive = 0;
                break;
            case "dresser":
                // The kitchen dresser: cupboard below, open shelves above with the plates stood on them.
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.1f, tile: 1);
                k.Tint = new Vector3(0.75f, 0.6f, 0.45f);
                k.Box(new Vector3(cx - hx, 0, cz - hz), new Vector3(cx + hx, 0.9f, cz + hz));
                k.Box(new Vector3(cx - hx, 0.9f, cz + hz - 0.12f), new Vector3(cx + hx, ht, cz + hz));
                k.Box(new Vector3(cx - hx, 1.35f, cz - 0.02f), new Vector3(cx + hx, 1.38f, cz + hz));
                k.Use("paper_form", Palette.BoardEnamel, 0.4f, 0.3f, tile: 1);
                for (float px = cx - hx + 0.15f; px < cx + hx - 0.1f; px += 0.26f)
                    k.Disc(new Vector3(px, 1.52f, cz + hz - 0.13f), -Vector3.UnitZ, 0.11f, 10);
                break;
            case "stairs":
                // The stair boxed in, its door (toward the partition) shut, a line of light under it.
                k.Use("wood_siding", Palette.DeepBrown, 0.85f, 0.05f, tile: 1);
                k.Tint = new Vector3(0.5f, 0.4f, 0.3f);
                k.Box(new Vector3(cx - hx, 0, cz - hz), new Vector3(cx + hx, ht, cz + hz));
                float face = cx + kx * hx;
                k.Use("paint_oxide", Palette.RustRed, 0.8f, 0.1f, tile: 1);
                k.Tint = new Vector3(0.45f, 0.35f, 0.3f);
                k.Box(new Vector3(Math.Min(face, face + kx * 0.04f), 0.03f, cz - 0.4f), new Vector3(Math.Max(face, face + kx * 0.04f), doorH - 0.05f, cz + 0.4f));
                k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
                k.Emissive = 1;
                k.Panel(new Vector3(face + kx * 0.045f, 0.02f, cz), Vector3.UnitY, new Vector3(kx, 0, 0), 0.8f, 0.05f, Vector2.Zero, Vector2.One);
                k.Emissive = 0;
                break;
            case "cabinet":
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
                k.Tint = new Vector3(0.6f, 0.42f, 0.3f);
                k.Box(new Vector3(cx - hx, 0, cz - hz), new Vector3(cx + hx, ht, cz + hz));
                k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
                k.Emissive = 1;
                k.BoxAt(new Vector3(cx + hx * 0.6f * Math.Sign(cx), ht + 0.14f, cz), new Vector3(0.045f, 0.07f, 0.045f));
                k.Emissive = 0;
                break;
            case "photo":
                // On the partition, the parlour's side.
                k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
                k.BoxAt(new Vector3(-kx * ((float)HouseLayout.Wall / 2 + 0.015f), ht, cz), new Vector3(0.015f, 0.17f, 0.13f));
                k.Use("paper_form", Palette.BoardEnamel, 0.6f, 0.1f, tile: 1);
                k.Shade(0.6f);
                k.Panel(new Vector3(-kx * ((float)HouseLayout.Wall / 2 + 0.032f), ht, cz), new Vector3(-kx, 0, 0), Vector3.UnitY, 0.2f, 0.26f, Vector2.Zero, Vector2.One);
                break;
        }
    }

    /// <summary>The household's own thing (towns.json households' "object"): small, where the layout sets it.</summary>
    static void Small(Kit k, string kind, float x, float z, float y)
    {
        switch (kind)
        {
            case "table":
                // A place laid: a plate and a cup.
                k.Use("paper_form", Palette.BoardEnamel, 0.4f, 0.3f, tile: 1);
                k.Disc(new Vector3(x, y + 0.005f, z), Vector3.UnitY, 0.12f, 12);
                k.Cylinder(new Vector3(x + 0.2f, y, z), new Vector3(x + 0.2f, y + 0.09f, z), 0.04f, 8);
                break;
            case "letters":
                k.Use("paper_form", Palette.BoardEnamel, 0.7f, 0, tile: 1);
                for (int i = 0; i < 4; i++)
                    k.Box(new Vector3(x - 0.12f + i * 0.01f, y + i * 0.012f, z - 0.08f), new Vector3(x + 0.12f + i * 0.01f, y + 0.01f + i * 0.012f, z + 0.08f));
                break;
            case "anklebell":
                k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.7f);
                k.Cylinder(new Vector3(x, y, z), new Vector3(x, y - 0.06f, z), 0.025f, 8, radiusB: 0.04f);
                k.Use("wool", Palette.BoardEnamel, 0.9f, 0);
                k.Rod(new Vector3(x, y, z), new Vector3(x, y + 0.5f, z), 0.005f);
                break;
            case "timetable":
                k.Use("paper_form", Palette.BoardEnamel, 0.9f, 0, tile: 1);
                k.BoxAt(new Vector3(x, y, z), new Vector3(0.22f, 0.3f, 0.015f));
                break;
            case "boards":
                k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
                for (int i = 0; i < 4; i++)
                    k.BoxAt(new Vector3(x, y - 0.45f + i * 0.3f, z), new Vector3(0.6f, 0.08f, 0.025f));
                break;
            case "boots":
                k.Use("leather", Palette.SootBlack, 0.5f, 0.3f, tile: 1);
                k.Box(new Vector3(x - 0.15f, 0.02f, z - 0.14f), new Vector3(x - 0.04f, 0.3f, z + 0.12f));
                k.Box(new Vector3(x + 0.04f, 0.02f, z - 0.14f), new Vector3(x + 0.15f, 0.3f, z + 0.12f));
                break;
            case "suitcase":
                k.Use("leather", Palette.DeepBrown, 0.6f, 0.2f, tile: 1);
                k.Box(new Vector3(x - 0.32f, 0.02f, z - 0.11f), new Vector3(x + 0.32f, 0.45f, z + 0.11f));
                break;
            case "cradle":
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.1f, tile: 1);
                k.Box(new Vector3(x - 0.42f, 0.18f, z - 0.25f), new Vector3(x + 0.42f, 0.62f, z + 0.25f), Kit.Faces.Sides | Kit.Faces.NegY);
                k.Box(new Vector3(x - 0.42f, 0, z - 0.3f), new Vector3(x - 0.36f, 0.18f, z + 0.3f));
                k.Box(new Vector3(x + 0.36f, 0, z - 0.3f), new Vector3(x + 0.42f, 0.18f, z + 0.3f));
                k.Use("wool", Palette.BoardEnamel, 0.9f, 0);
                k.Box(new Vector3(x - 0.38f, 0.4f, z - 0.21f), new Vector3(x + 0.38f, 0.52f, z + 0.21f));
                break;
            case "radio":
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
                k.Box(new Vector3(x - 0.22f, y, z - 0.14f), new Vector3(x + 0.22f, y + 0.4f, z + 0.14f));
                k.Use("sac", Palette.MuddyOlive, 0.9f, 0, tile: 1);
                k.Panel(new Vector3(x, y + 0.22f, z - 0.145f), -Vector3.UnitZ, Vector3.UnitY, 0.32f, 0.2f, Vector2.Zero, Vector2.One, twoSided: true);
                break;
            default:
                // A mantel clock.
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
                k.Box(new Vector3(x - 0.15f, y, z - 0.08f), new Vector3(x + 0.15f, y + 0.32f, z + 0.08f));
                k.Use("gauge_face", Palette.BoardEnamel, 0.4f, 0.2f, tile: 1);
                k.Disc(new Vector3(x, y + 0.2f, z - 0.085f), -Vector3.UnitZ, 0.09f, 12);
                break;
        }
    }
}
