using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The fortress towns' houses (the director, 7 Oct 2026: "these are maritime Canada towns, the buildings must look like
/// Maritimes buildings ... fully interior modeled and explorable for some of them", and with his photographs: "lots of
/// variations so it doesn't feel like the same 10 assets recycled across towns over and over again"; note 281). Each
/// house is built from its <see cref="HouseDesign"/> (the Sim's, so what's solid is what's drawn), in the colours of
/// content/world/houses.json:
/// <list type="bullet">
/// <item>gable to the street or eaves to it; one, one and a half, two or two and a half storeys; a plain gable, a saltbox,
/// a gambrel or a hip;</item>
/// <item>cedar shingle (left silver, stained or painted) or painted clapboard, white trim and corner boards (or a
/// contrasting trim), a fieldstone foundation;</item>
/// <item>gable dormers, a shed dormer, a Lunenburg bump or the Island's centre gable; a lower side wing; a hood over the
/// step or an enclosed porch;</item>
/// <item>windows laid out floor by floor (two-over-two, six-over-six or one-over-one), shutters in the door's colour or
/// another, Victorian brackets and window heads in an accent colour; a brick chimney in the middle, at one end or both,
/// or a stovepipe.</item>
/// </list>
/// Fronts −Z, the main block's middle at the origin, X along its front (the house's u times its side). An open house's
/// ground floor is built inside it from the Sim's <see cref="HouseLayout"/>.
/// </summary>
public static class MaritimeKit
{
    /// <summary>The casings inside an open house (painted white, gone cream).</summary>
    static readonly Vector3 Trim = new(0.80f, 0.78f, 0.72f);

    /// <summary>The plaster inside, painted a faded green.</summary>
    static readonly Vector3 Plaster = new(0.95f, 1.0f, 0.82f);

    /// <summary>The fieldstone foundation's height out of the ground (m): the siding starts on it.</summary>
    const float Found = 0.35f;

    /// <summary>A window's middle off the ground on the ground floor, and a floor's height (m).</summary>
    const float GroundWindow = Found + 1.25f, Storey = 2.7f;

    // ---------------------------------------------------------------------------------------------------------------
    // Colour

    /// <summary>An sRGB hex colour (houses.json's) as linear.</summary>
    public static Vector3 Lin(string hex)
    {
        hex = hex.TrimStart('#');
        static float C(string h, int i)
        {
            float v = Convert.ToInt32(h.Substring(i, 2), 16) / 255f;
            return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        }
        return hex.Length >= 6 ? new Vector3(C(hex, 0), C(hex, 2), C(hex, 4)) : Vector3.One * 0.5f;
    }

    static Vector3 Of(HouseColour[] palette, int i, string fallback) =>
        Lin(palette.Length > 0 ? palette[Math.Clamp(i, 0, palette.Length - 1)].Rgb : fallback);

    /// <summary>A house's colours, resolved: its siding's (and which), trim, door, shutters, roof and accent.</summary>
    readonly record struct Coat(bool Shingle, Vector3 Siding, Vector3 Trim, Vector3 Door, Vector3? Shutter, Vector3 Roof, Vector3 Accent);

    static Coat CoatOf(HouseLooks looks, HouseDesign d, HouseKind kind)
    {
        var body = d.Shingle && !d.Painted ? Of(looks.Stains, d.Paint, "#B4B0A6") : Of(looks.Paints, d.Paint, "#D8D6CE");
        // Nobody's painted a house nobody lives in since: it's gone grey and pale under the salt.
        float fade = kind switch { HouseKind.Lived or HouseKind.Open => 0, HouseKind.Empty => 0.55f, _ => 0.4f };
        Vector3 Faded(Vector3 c) => Vector3.Lerp(c, Vector3.One * (0.3f * c.X + 0.55f * c.Y + 0.15f * c.Z), fade) * (1 - 0.25f * fade);
        return new Coat(d.Shingle, Faded(body), Faded(Of(looks.Trims, d.Trim, "#ECEAE2")), Faded(Of(looks.Doors, d.Door, "#A82A22")),
            d.Shutters >= 0 ? Faded(Of(looks.Doors, d.Shutters, "#1F3A6A")) : null, Of(looks.Roofs, d.RoofColour, "#3C3C40"),
            Faded(Of(looks.Accents, d.Accent, "#D0705E")));
    }

    /// <summary>
    /// Wears a texture in a colour: the texture's own albedo (tools/art grade.py: <paramref name="albedo"/>) lifted to the
    /// colour, a little under it (the paint chip's colour, at night, in the salt). Without the texture, the flat colour.
    /// </summary>
    static void Paint(Kit k, string texture, Vector3 colour, float albedo, float tile, float wear = 0.8f, float shine = 0.06f)
    {
        k.Use(texture, colour, wear, shine, tile);
        if (k.Tint == Vector3.One)
            k.Tint = colour * (0.75f / albedo);
    }

    static void Siding(Kit k, Coat c) => Paint(k, c.Shingle ? "shingle_cedar" : "clapboard", c.Siding, c.Shingle ? 0.13f : 0.26f, 1.0f);
    static void TrimPaint(Kit k, Vector3 colour) => Paint(k, "clapboard", colour, 0.26f, 3.0f, 0.5f, 0.1f);
    static void RoofPaint(Kit k, Coat c) => Paint(k, "roof_slate", c.Roof, 0.06f, 1.4f, 0.85f, 0.12f);

    // ---------------------------------------------------------------------------------------------------------------
    // The house

    /// <summary>The main block's numbers, in the kit's frame.</summary>
    sealed class Block
    {
        public float W, D, X0, X1, Z0, Z1, Eave, BackEave, Ridge, Base;
        public bool GableFront;
        public HouseRoof Roof;
        /// <summary>The roof's line across the ridge (s, y), from s0 to s1: z for an eave-front house, x for a gable-front.</summary>
        public readonly List<Vector2> Profile = [];
        /// <summary>A point by (along the ridge, up, across it) in the kit's frame.</summary>
        public Vector3 P(float a, float y, float s) => GableFront ? new Vector3(s, y, a) : new Vector3(a, y, s);
        public float A0 => GableFront ? Z0 : X0;
        public float A1 => GableFront ? Z1 : X1;
        public float S0 => GableFront ? X0 : Z0;
        public float S1 => GableFront ? X1 : Z1;

        /// <summary>The roof's top over a point across it.</summary>
        public float RoofY(float s)
        {
            for (int i = 0; i + 1 < Profile.Count; i++)
                if (s <= Profile[i + 1].X || i + 2 == Profile.Count)
                {
                    var a = Profile[i];
                    var b = Profile[i + 1];
                    float t = Math.Clamp((s - a.X) / MathF.Max(1e-4f, b.X - a.X), 0, 1);
                    return a.Y + (b.Y - a.Y) * t;
                }
            return Eave;
        }
    }

    static Block Shape(TownHouse h)
    {
        var d = h.Design;
        var b = new Block { W = (float)h.Width, D = (float)h.Depth, GableFront = d.GableFront, Roof = d.Roof };
        b.X0 = -b.W / 2;
        b.X1 = b.W / 2;
        b.Z0 = -b.D / 2;
        b.Z1 = b.D / 2;
        b.Eave = (float)d.Eaves;
        bool salt = d.Roof == HouseRoof.Saltbox && !d.GableFront;
        b.BackEave = salt ? MathF.Max(2.6f, b.Eave - 2.6f) : b.Eave;
        float s0 = b.S0, s1 = b.S1, span = s1 - s0, half = span / 2, pitch = (float)d.Pitch;
        switch (d.Roof)
        {
            case HouseRoof.Saltbox when salt:
                {
                    float sr = s0 + span * 0.4f;
                    b.Ridge = MathF.Max(b.Eave + 1.4f, b.Eave + pitch * span * 0.4f);
                    b.Profile.AddRange([new(s0, b.Eave), new(sr, b.Ridge), new(s1, b.BackEave)]);
                    break;
                }
            case HouseRoof.Gambrel:
                {
                    float rise = pitch * half * 1.1f;
                    b.Ridge = b.Eave + rise;
                    b.Profile.AddRange([new(s0, b.Eave), new(s0 + span * 0.2f, b.Eave + rise * 0.72f), new(0, b.Ridge),
                        new(s1 - span * 0.2f, b.Eave + rise * 0.72f), new(s1, b.Eave)]);
                    break;
                }
            default:
                b.Ridge = b.Eave + pitch * half;
                b.Profile.AddRange([new(s0, b.Eave), new(0, b.Ridge), new(s1, b.Eave)]);
                break;
        }
        b.Base = MathF.Min(b.Eave, b.BackEave);
        return b;
    }

    /// <summary>
    /// A house of the town (note 281): its outside built to its design in its colours, and an open one's ground floor
    /// inside, its household's own <paramref name="thing"/> (by kind) where the layout puts it. <paramref name="lit"/>:
    /// lamplight behind some of its windows (a custom that keeps windows dark has none).
    /// </summary>
    public static MeshAsset House(Look? look, TownHouse h, HouseLooks looks, string? thing, bool lit)
    {
        var k = new Kit(look, 3000 + h.Id * 7 + (int)h.Design.Paint);
        var coat = CoatOf(looks, h.Design, h.Kind);
        var b = Shape(h);
        Yard(k, h, coat);
        if (h.Kind == HouseKind.Burnt)
        {
            Burnt(k, h, b);
            return k.Build($"maritime-burnt-{h.Id}");
        }
        float doorX = X(h, h.Design.DoorU);
        Shell(k, h, b, coat, doorX);
        var panes = Windows(h, b, doorX);
        int n = 0;
        foreach (var p in panes)
        {
            bool glow = lit && h.Kind switch
            {
                HouseKind.Lived => (n++ * 7 + h.Id) % 3 != 1,
                // An open house's lamps are downstairs: upstairs, nobody's gone up to bed.
                HouseKind.Open => p.Ground,
                _ => false,
            };
            Window(k, p, coat, h, glow);
        }
        Door(k, h, b, coat, doorX, lit);
        Dormers(k, h, b, coat, lit && h.Kind == HouseKind.Lived);
        Wing(k, h, b, coat, lit && h.Kind == HouseKind.Lived);
        Chimneys(k, h, b);
        if (h.Design.Fancy)
            Fancy(k, h, b, coat);
        if (h.Layout is { } l)
            Inside(k, h, l, thing, panes);
        return k.Build($"maritime-{h.Kind}-{h.Id}-{h.S:0}-{h.D:0}");
    }

    /// <summary>
    /// A house as it's seen from down the street (queue #74's big towns, note 335): its block and roof in its colours,
    /// its gables, its wing, a chimney, its lit windows and its door as flat panels: a few dozen triangles, where the
    /// near one (<see cref="House"/>) is a few thousand. WorldArt draws these past the near ones, many to a mesh.
    /// </summary>
    public static MeshAsset Far(Look? look, TownHouse h, HouseLooks looks, bool lit)
    {
        var k = new Kit(look, 3000 + h.Id * 7);
        var coat = CoatOf(looks, h.Design, h.Kind);
        var b = Shape(h);
        FarInto(k, h, b, coat, lit);
        return k.Build($"maritime-far-{h.Id}");
    }

    static void FarInto(Kit k, TownHouse h, Block b, Coat c, bool lit)
    {
        FarYard(k, h);
        if (h.Kind == HouseKind.Burnt)
        {
            k.Use("wood_grey", Palette.SootBlack, 0.95f, 0, tile: 1);
            k.Tint = new Vector3(0.12f, 0.1f, 0.09f);
            k.Box(new Vector3(b.X0, 0, b.Z0), new Vector3(b.X1, Found + 1.0f, b.Z1), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1.2f);
            k.Box(new Vector3(-0.38f, 0, -0.32f), new Vector3(0.38f, b.Ridge + 0.9f, 0.32f), Kit.Faces.Sides | Kit.Faces.PosY);
            return;
        }
        Siding(k, c);
        k.Box(new Vector3(b.X0, -0.3f, b.Z0), new Vector3(b.X1, b.Eave, b.Z1), Kit.Faces.NegZ | (b.GableFront ? Kit.Faces.PosZ : 0) | (b.GableFront ? Kit.Faces.Sides : 0));
        if (!b.GableFront)
        {
            k.Box(new Vector3(b.X0, -0.3f, b.Z0), new Vector3(b.X1, b.BackEave, b.Z1), Kit.Faces.PosZ);
            k.Box(new Vector3(b.X0, -0.3f, b.Z0), new Vector3(b.X1, b.Base, b.Z1), Kit.Faces.PosX | Kit.Faces.NegX);
        }
        if (b.Roof != HouseRoof.Hip || b.GableFront)
            foreach (float a in new[] { b.A0, b.A1 })
            {
                var outward = b.GableFront ? new Vector3(0, 0, MathF.Sign(a)) : new Vector3(MathF.Sign(a), 0, 0);
                var poly = new List<Vector3> { b.P(a, b.Base, b.S0) };
                poly.AddRange(b.Profile.Select(p => b.P(a, MathF.Max(p.Y, b.Base), p.X)));
                poly.Add(b.P(a, b.Base, b.S1));
                var centre = poly.Aggregate(Vector3.Zero, (s, p) => s + p) / poly.Count;
                for (int i = 0; i < poly.Count; i++)
                    Face(k, poly[i], poly[(i + 1) % poly.Count], centre, outward, false);
            }
        Roof(k, b, c);
        var d = h.Design;
        if (d.Ell != 0)
        {
            float sx = MathF.Sign(X(h, d.Ell)), inner = sx * b.W / 2, outer = sx * (b.W / 2 + (float)d.EllWidth);
            float ez0 = b.Z0 + (float)d.EllSetback, ez1 = ez0 + (float)d.EllDepth, eave = d.EllTall ? 3.5f : 2.7f;
            Siding(k, c);
            k.Box(new Vector3(MathF.Min(inner, outer), -0.3f, ez0), new Vector3(MathF.Max(inner, outer), eave, ez1), Kit.Faces.Sides);
            RoofPaint(k, c);
            float mz = (ez0 + ez1) / 2, ridge = eave + 0.8f * (ez1 - ez0) / 2;
            k.Quad(new Vector3(MathF.Min(inner, outer), eave, ez0 - 0.3f), new Vector3(MathF.Max(inner, outer), eave, ez0 - 0.3f), new Vector3(MathF.Max(inner, outer), ridge, mz), new Vector3(MathF.Min(inner, outer), ridge, mz), twoSided: true);
            k.Quad(new Vector3(MathF.Min(inner, outer), ridge, mz), new Vector3(MathF.Max(inner, outer), ridge, mz), new Vector3(MathF.Max(inner, outer), eave, ez1 + 0.3f), new Vector3(MathF.Min(inner, outer), eave, ez1 + 0.3f), twoSided: true);
        }
        k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1.2f);
        float sr = b.Profile.MaxBy(p => p.Y).X;
        var top = b.P(0, b.Ridge + 0.9f, sr);
        k.Box(top + new Vector3(-0.38f, -1.6f, -0.32f), top + new Vector3(0.38f, 0, 0.32f), Kit.Faces.Sides | Kit.Faces.PosY);
        // The street side's windows and door: lit glass or dark, a dark door.
        float doorX = X(h, d.DoorU);
        int n = 0;
        foreach (var p in Windows(h, b, doorX).Where(p => p.Out.Z < -0.5f))
        {
            bool glow = lit && h.Kind == HouseKind.Lived && (n++ * 7 + h.Id) % 3 != 1;
            if (glow)
            {
                k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
                k.Emissive = 0.85f;
            }
            else
            {
                k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
                k.Shade(0.35f);
            }
            k.Panel(p.At + p.Out * 0.02f, p.Out, Vector3.UnitY, p.W, p.H, Vector2.Zero, Vector2.One);
            k.Emissive = 0;
        }
        k.Use("paint_black", Palette.SootBlack, 0.9f, 0);
        k.Shade(0.4f);
        k.Panel(new Vector3(doorX, k.DoorHeight() / 2, (float)(b.Z0 + h.DoorV) - 0.02f), -Vector3.UnitZ, Vector3.UnitY, (float)HouseLayout.DoorWidth, k.DoorHeight(), Vector2.Zero, Vector2.One);
    }

    /// <summary>
    /// Many houses as one mesh (queue #74's big towns): each one's far form (<see cref="Far"/>) placed by
    /// its <c>Local</c> in the block's own frame, so a block of a street is one draw.
    /// </summary>
    public static MeshAsset Street(Look? look, IEnumerable<(TownHouse House, Matrix4x4 Local)> houses, HouseLooks looks, bool lit, string name)
    {
        var k = new Kit(look, 4200);
        foreach (var (h, local) in houses)
        {
            k.Push(local);
            FarInto(k, h, Shape(h), CoatOf(looks, h.Design, h.Kind), lit);
            k.Pop();
        }
        return k.Build(name);
    }

    /// <summary>The lamp by a lived-in house's door (the kit's frame): on the wall beside it, a little over the door's head.</summary>
    public static Vector3 Porch(TownHouse h, float doorHeight) =>
        new(X(h, h.Design.DoorU) + (float)HouseLayout.DoorWidth / 2 + 0.4f, doorHeight + 0.1f, (float)(-h.Depth / 2 + h.DoorV) - 0.16f);

    /// <summary>The foundation, the walls, the gable ends, the corner boards and fascia, and the roof.</summary>
    static void Shell(Kit k, TownHouse h, Block b, Coat c, float doorX)
    {
        bool open = h.Layout is not null;
        const float t = (float)HouseLayout.Wall, proud = 0.06f;
        float dw = (float)HouseLayout.DoorWidth, doorH = k.DoorHeight();
        float x0 = b.X0, x1 = b.X1, z0 = b.Z0, z1 = b.Z1;
        // The fieldstone foundation, a little proud of the walls: a ring no deeper than the wall (an open house's floor
        // runs to the wall's inner face), broken for an open door.
        k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1.2f);
        var lo = new Vector3(x0 - proud, -0.4f, z0 - proud);
        var hi = new Vector3(x1 + proud, Found, z1 + proud);
        if (open)
        {
            k.Box(lo, new Vector3(doorX - dw / 2, Found, z0 + t), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(doorX + dw / 2, -0.4f, z0 - proud), new Vector3(hi.X, Found, z0 + t), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(lo.X, -0.4f, z1 - t), hi, Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(lo.X, -0.4f, z0 + t), new Vector3(x0 + t, Found, z1 - t), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Box(new Vector3(x1 - t, -0.4f, z0 + t), new Vector3(hi.X, Found, z1 - t), Kit.Faces.Sides | Kit.Faces.PosY);
        }
        else
            k.Box(lo, hi, Kit.Faces.Sides | Kit.Faces.PosY);
        // The walls: each up to its own eave (a gable-front's front and back up to the eaves, the gable over them).
        Siding(k, c);
        float front = b.GableFront ? b.Eave : b.Eave, back = b.GableFront ? b.Eave : b.BackEave, sides = b.Base;
        if (!open)
        {
            k.Box(new Vector3(x0, Found, z0), new Vector3(x1, front, z1), Kit.Faces.NegZ);
            k.Box(new Vector3(x0, Found, z0), new Vector3(x1, back, z1), Kit.Faces.PosZ);
            k.Box(new Vector3(x0, Found, z0), new Vector3(x1, sides, z1), Kit.Faces.PosX | Kit.Faces.NegX);
        }
        else
        {
            // Thin walls, the outer layer (Inside lines them): the door's opening down to the ground, its lintel over it.
            k.Box(new Vector3(x0, Found, z0), new Vector3(doorX - dw / 2, front, z0 + t), Kit.Faces.All);
            k.Box(new Vector3(doorX + dw / 2, Found, z0), new Vector3(x1, front, z0 + t), Kit.Faces.All);
            k.Box(new Vector3(doorX - dw / 2, doorH, z0), new Vector3(doorX + dw / 2, front, z0 + t), Kit.Faces.All);
            k.Box(new Vector3(x0, Found, z1 - t), new Vector3(x1, back, z1), Kit.Faces.All);
            k.Box(new Vector3(x0, Found, z0), new Vector3(x0 + t, sides, z1), Kit.Faces.All);
            k.Box(new Vector3(x1 - t, Found, z0), new Vector3(x1, sides, z1), Kit.Faces.All);
        }
        // The gable ends (a hip has none): the wall's top up to the roof's line, as a fan from the middle of its foot.
        if (b.Roof != HouseRoof.Hip || b.GableFront)
            foreach (float a in new[] { b.A0, b.A1 })
            {
                var outward = b.GableFront ? new Vector3(0, 0, MathF.Sign(a)) : new Vector3(MathF.Sign(a), 0, 0);
                var poly = new List<Vector3> { b.P(a, b.Base, b.S0) };
                poly.AddRange(b.Profile.Select(p => b.P(a, MathF.Max(p.Y, b.Base), p.X)));
                poly.Add(b.P(a, b.Base, b.S1));
                var centre = poly.Aggregate(Vector3.Zero, (s, p) => s + p) / poly.Count;
                // Round every edge, the foot's too (the polygon is convex, so the fan from its middle covers it).
                for (int i = 0; i < poly.Count; i++)
                    Face(k, poly[i], poly[(i + 1) % poly.Count], centre, outward, open);
            }
        // White corner boards (or the trim's colour), and the fascia under the eaves.
        TrimPaint(k, c.Trim);
        foreach (float x in new[] { x0, x1 })
            foreach (float z in new[] { z0, z1 })
                k.Box(new Vector3(x - 0.07f, Found, z - 0.07f), new Vector3(x + 0.07f, z < 0 ? front : back, z + 0.07f));
        if (b.GableFront)
        {
            foreach (float x in new[] { x0, x1 })
                k.Box(new Vector3(x - 0.09f * MathF.Sign(-x), b.Eave - 0.22f, z0 - 0.1f), new Vector3(x + 0.09f * MathF.Sign(x), b.Eave, z1 + 0.1f));
        }
        else
        {
            k.Box(new Vector3(x0 - 0.1f, front - 0.22f, z0 - 0.09f), new Vector3(x1 + 0.1f, front, z0 + 0.02f));
            k.Box(new Vector3(x0 - 0.1f, back - 0.22f, z1 - 0.02f), new Vector3(x1 + 0.1f, back, z1 + 0.09f));
        }
        // The rake boards up the gables' edges.
        if (b.Roof != HouseRoof.Hip || b.GableFront)
            foreach (float a in new[] { b.A0, b.A1 })
                for (int i = 0; i + 1 < b.Profile.Count; i++)
                {
                    float off = MathF.Sign(a) * 0.04f;
                    k.Rod(b.P(a + off, b.Profile[i].Y + 0.02f, b.Profile[i].X), b.P(a + off, b.Profile[i + 1].Y + 0.02f, b.Profile[i + 1].X), 0.07f);
                }
        Roof(k, b, c);
    }

    /// <summary>A triangle of a wall facing <paramref name="outward"/> (both ways for an open house's), its siding's courses level.</summary>
    static void Face(Kit k, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, bool twoSided)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0)
            (a, b) = (b, a);
        // Texture across the face (whichever axis it lies along) and down it.
        Vector2 Uv(Vector3 p) => MathF.Abs(outward.X) > 0.5f ? new(-outward.X * p.Z, -p.Y) : new(outward.Z * p.X, -p.Y);
        k.Tri(a, b, c, Uv(a), Uv(b), Uv(c));
        if (twoSided)
            k.Tri(b, a, c, Uv(b), Uv(a), Uv(c));
    }

    /// <summary>The roof: each of its profile's slopes out past the walls at the eaves and the gables, or a hip's four.</summary>
    static void Roof(Kit k, Block b, Coat c)
    {
        RoofPaint(k, c);
        const float over = 0.35f, rake = 0.28f;
        float a0 = b.A0 - rake, a1 = b.A1 + rake;
        if (b.Roof == HouseRoof.Hip && !b.GableFront)
        {
            // Hipped: the ridge shortened by the half-span at each end, the ends sloping up to it.
            float half = (b.S1 - b.S0) / 2, ra = MathF.Max(0, (b.A1 - b.A0) / 2 - half);
            float e = b.Eave - over * 0.6f;
            var f0 = b.P(b.A0 - over, e, b.S0 - over);
            var f1 = b.P(b.A1 + over, e, b.S0 - over);
            var k1 = b.P(b.A1 + over, e, b.S1 + over);
            var k0 = b.P(b.A0 - over, e, b.S1 + over);
            var r0 = b.P(-ra, b.Ridge, 0);
            var r1 = b.P(ra, b.Ridge, 0);
            k.Quad(f0, f1, r1, r0, twoSided: true);
            k.Quad(k1, k0, r0, r1, twoSided: true);
            k.Tri(f0, r0, k0);
            k.Tri(k0, r0, f0);
            k.Tri(k1, r1, f1);
            k.Tri(f1, r1, k1);
            return;
        }
        for (int i = 0; i + 1 < b.Profile.Count; i++)
        {
            var p = b.Profile[i];
            var q = b.Profile[i + 1];
            var dir = Vector2.Normalize(q - p);
            if (i == 0)
                p -= dir * over;
            if (i + 2 == b.Profile.Count)
                q += dir * over;
            k.Quad(b.P(a0, p.Y, p.X), b.P(a1, p.Y, p.X), b.P(a1, q.Y, q.X), b.P(a0, q.Y, q.X), twoSided: true);
        }
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
    // ---------------------------------------------------------------------------------------------------------------
    // Windows

    /// <summary>Where a house's windows go, floor by floor and face by face, clear of its corners, its door, its wing, and
    /// (an open house) the partition and what stands against the walls inside.</summary>
    static List<Pane> Windows(TownHouse h, Block b, float doorX)
    {
        var d = h.Design;
        var layout = h.Layout;
        float x0 = b.X0, x1 = b.X1, z0 = b.Z0, z1 = b.Z1, w = b.W, depth = b.D;
        float dh = (float)HouseLayout.DoorWidth / 2;
        // A window's room along its wall: its casing, and its shutters' when it has them.
        float item = d.Shutters >= 0 ? 2.0f : 1.15f;
        var panes = new List<Pane>();
        var against = new List<(int Wall, float A, float B)>();
        if (layout is not null)
        {
            const float near = 0.5f;
            foreach (var x in layout.Things.Where(x => x.Height > 0.8))
            {
                float cx = X(h, x.U), cz = Z(h, x.V), hx = (float)x.HalfU, hz = (float)x.HalfV;
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
        // The wing covers part of a side wall (its inner end), and an enclosed porch's back the front by the door.
        if (d.Ell != 0)
        {
            float ez0 = (float)(z0 + d.EllSetback), ez1 = (float)(ez0 + d.EllDepth);
            against.Add((X(h, d.Ell) < 0 ? 2 : 3, ez0 - 0.3f, ez1 + 0.3f));
        }
        // Wall 0 the front, 1 the back, 2 the −x end, 3 the +x end; -1 the upper floors' (nothing inside to keep clear of).
        IEnumerable<float> Fit(int wall, float a, float b2, int most, params (float A, float B)[] also)
        {
            var blocks = against.Where(x => x.Wall == wall).Select(x => (x.A, x.B)).Concat(also).OrderBy(x => x.A).ToList();
            float from = a + 0.45f;
            foreach (var (ba, bb) in blocks.Append((b2 - 0.45f, b2)))
            {
                float len = ba - from;
                int count = Math.Clamp((int)((len + item - 1.15f + 0.5f) / (item + 0.35f)), 0, most);
                if (len < item - 0.1f)
                    count = 0;
                if (count == 1)
                    yield return from + len / 2;
                else if (count >= 2)
                    for (int i = 0; i < count; i++)
                        yield return from + len * (i + 0.5f) / count;
                from = MathF.Max(from, bb);
            }
        }
        var noDoor = (doorX - dh - 0.35f - (d.Shutters >= 0 ? 0.4f : 0), doorX + dh + 0.35f + (d.Shutters >= 0 ? 0.4f : 0));
        if (d.Porch == HousePorch.Vestibule)
            noDoor = (noDoor.Item1 - 0.4f, noDoor.Item2 + 0.4f);
        // The bump stands out over the door, up from the first floor's top: its own windows (Dormers).
        var ground = Fit(0, x0, x1, w > 8 ? 3 : 2, noDoor).ToList();
        foreach (float x in ground)
            panes.Add(new(new Vector3(x, GroundWindow, z0), -Vector3.UnitZ, 0.8f, 1.2f, true));
        int floors = b.Eave >= 5 ? 2 : 1;
        bool bump = d.Dormer == HouseDormer.Bump;
        for (int f = 1; f < floors; f++)
        {
            // Upstairs at the front, over the windows below, and over the door when it's in the middle.
            var ups = new List<float>(ground);
            if (MathF.Abs(doorX) < 0.4f && !bump)
                ups.Add(doorX);
            if (ups.Count == 0)
                ups.AddRange(Fit(-1, x0, x1, 2));
            foreach (float x in ups.Where(x => !bump || MathF.Abs(x) > 1.6f))
                panes.Add(new(new Vector3(x, GroundWindow + f * Storey, z0), -Vector3.UnitZ, 0.8f, 1.2f, false));
        }
        // A gable-front's attic window, up in the gable over the street (the Cape Breton studio's).
        if (b.GableFront && b.Ridge - b.Eave > 1.7f)
            panes.Add(new(new Vector3(0, b.Eave + (b.Ridge - b.Eave) * 0.36f, z0), -Vector3.UnitZ, 0.65f, 0.95f, false));
        // The back.
        foreach (float x in Fit(1, x0, x1, 2, layout is null ? [(-0.4f, 0.4f)] : []))
        {
            panes.Add(new(new Vector3(x, GroundWindow, z1), Vector3.UnitZ, 0.8f, 1.2f, true));
            if ((b.GableFront ? b.Eave : b.BackEave) >= 5)
                panes.Add(new(new Vector3(x, GroundWindow + Storey, z1), Vector3.UnitZ, 0.8f, 1.2f, false));
        }
        // The ends: a window or two a floor, and one up in a gable end.
        foreach (int wall in new[] { 2, 3 })
        {
            float x = wall == 2 ? x0 : x1;
            var outward = wall == 2 ? -Vector3.UnitX : Vector3.UnitX;
            var zs = Fit(wall, z0, z1, depth > 6.5f ? 2 : 1).ToList();
            foreach (float z in zs)
            {
                panes.Add(new(new Vector3(x, GroundWindow, z), outward, 0.8f, 1.2f, true));
                if (b.Base >= 5)
                    panes.Add(new(new Vector3(x, GroundWindow + Storey, z), outward, 0.8f, 1.2f, false));
            }
            if (!b.GableFront && b.Roof != HouseRoof.Hip && b.Ridge - b.Base > 1.7f)
            {
                float gz = b.Roof == HouseRoof.Saltbox ? z0 + depth * 0.3f : 0;
                panes.Add(new(new Vector3(x, b.Base + MathF.Min(0.95f, (b.Ridge - b.Base) * 0.32f), gz), outward, 0.65f, 0.95f, false));
            }
        }
        return panes;
    }

    /// <summary>A box on a wall facing <paramref name="n"/>: <paramref name="ha"/> either way along it, <paramref name="hu"/> up and down, from <paramref name="d0"/> to <paramref name="d1"/> out.</summary>
    static void Slab(Kit k, Vector3 c, Vector3 n, float ha, float hu, float d0, float d1)
    {
        var along = Vector3.Cross(Vector3.UnitY, n);
        var p = c - along * ha - Vector3.UnitY * hu + n * d0;
        var q = c + along * ha + Vector3.UnitY * hu + n * d1;
        k.Box(Vector3.Min(p, q), Vector3.Max(p, q));
    }

    /// <summary>
    /// A window on a wall's face: its casing in the trim's colour, lamplit glass (<paramref name="lit"/>) or dark, its sash
    /// (two-over-two, six-over-six or one-over-one), its shutters, a Victorian head over it, boards over a boarded house's.
    /// </summary>
    static void Window(Kit k, Pane p, Coat c, TownHouse h, bool lit)
    {
        var (at, n, ww, wh) = (p.At, p.Out, p.W, p.H);
        var along = Vector3.Cross(Vector3.UnitY, n);
        TrimPaint(k, c.Trim);
        Slab(k, at - Vector3.UnitY * (wh / 2 + 0.07f), n, ww / 2 + 0.13f, 0.07f, 0, 0.07f);
        Slab(k, at + Vector3.UnitY * (wh / 2 + 0.08f), n, ww / 2 + 0.12f, 0.08f, 0, 0.06f);
        Slab(k, at - along * (ww / 2 + 0.06f), n, 0.06f, wh / 2, 0, 0.05f);
        Slab(k, at + along * (ww / 2 + 0.06f), n, 0.06f, wh / 2, 0, 0.05f);
        if (lit)
        {
            k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
            k.Emissive = 0.85f;
        }
        else
        {
            k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
            k.Shade(h.Kind == HouseKind.Empty ? 0.12f : 0.35f);
        }
        k.Panel(at + n * 0.01f, n, Vector3.UnitY, ww, wh, Vector2.Zero, Vector2.One);
        k.Emissive = 0;
        // The sash: the meeting rail across the middle, and the muntins of its panes.
        TrimPaint(k, c.Trim * 0.92f);
        Slab(k, at, n, ww / 2, 0.025f, 0, 0.045f);
        int across = h.Design.Windows switch { 1 => 3, 2 => 1, _ => 2 };
        for (int i = 1; i < across; i++)
            Slab(k, at + along * (-ww / 2 + ww * i / across), n, 0.015f, wh / 2, 0, 0.035f);
        if (h.Design.Windows == 1)
            foreach (float y in new[] { -wh / 4, wh / 4 })
                Slab(k, at + Vector3.UnitY * y, n, ww / 2, 0.012f, 0, 0.035f);
        // Shutters either side, board-and-batten with the Z brace (the Cape Breton studio's).
        if (c.Shutter is { } shutter && p.W >= 0.75f)
        {
            Paint(k, "clapboard", shutter, 0.26f, 2.0f, 0.6f, 0.08f);
            foreach (int s in new[] { -1, 1 })
            {
                var sc = at + along * s * (ww / 2 + 0.16f + ww / 4);
                Slab(k, sc, n, ww / 4, wh / 2 + 0.06f, 0.01f, 0.05f);
                k.Rod(sc + along * (-ww / 4 + 0.05f) - Vector3.UnitY * (wh / 2 - 0.1f) + n * 0.06f,
                    sc + along * (ww / 4 - 0.05f) + Vector3.UnitY * (wh / 2 - 0.1f) + n * 0.06f, 0.025f);
            }
        }
        // A Victorian head over it, in the accent (the Lunenburg house's).
        if (h.Design.Fancy)
        {
            Paint(k, "clapboard", c.Accent, 0.26f, 3.0f, 0.5f, 0.1f);
            Slab(k, at + Vector3.UnitY * (wh / 2 + 0.27f), n, ww / 2 + 0.2f, 0.09f, 0, 0.1f);
            Slab(k, at + Vector3.UnitY * (wh / 2 + 0.4f), n, ww / 2 + 0.26f, 0.04f, 0, 0.14f);
        }
        if (h.Kind == HouseKind.Boarded)
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.95f, 0, tile: 1);
            for (int i = 0; i < 3; i++)
                Slab(k, at + Vector3.UnitY * (-wh / 2 + 0.2f + i * (wh - 0.4f) / 2), n, ww / 2 + 0.15f, 0.09f, 0.05f, 0.1f);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The door, the porch, the dormers, the wing, the chimneys, the trim

    /// <summary>
    /// The front door in its colour, in its casing (an open house's swung in, Inside), boarded or gone; the step; a hood
    /// on brackets over it, or an enclosed porch round it with its own door. A lived-in house's lamp beside it.
    /// </summary>
    static void Door(Kit k, TownHouse h, Block b, Coat c, float doorX, bool lit)
    {
        var d = h.Design;
        bool open = h.Layout is not null;
        float dw = (float)HouseLayout.DoorWidth, doorH = k.DoorHeight(), z0 = b.Z0;
        float dz = z0;
        if (d.Porch == HousePorch.Vestibule)
        {
            // The enclosed porch: its walls in the house's siding, a gable to the street, a window each side.
            float hw = (float)HouseDesign.VestibuleHalf, pd = (float)HouseDesign.VestibuleDepth, pe = 2.45f;
            dz = z0 - pd;
            Siding(k, c);
            k.Box(new Vector3(doorX - hw, 0.15f, dz), new Vector3(doorX + hw, pe, z0), Kit.Faces.NegZ | Kit.Faces.PosX | Kit.Faces.NegX);
            var a = new Vector3(doorX - hw, pe, dz);
            var bb = new Vector3(doorX + hw, pe, dz);
            var top = new Vector3(doorX, pe + hw * 0.8f, dz);
            Face(k, a, bb, top, -Vector3.UnitZ, false);
            RoofPaint(k, c);
            k.Quad(new Vector3(doorX - hw - 0.18f, pe - 0.1f, dz - 0.2f), top + new Vector3(0, 0.1f, -0.2f), top + new Vector3(0, 0.1f, pd), new Vector3(doorX - hw - 0.18f, pe - 0.1f, z0), twoSided: true);
            k.Quad(top + new Vector3(0, 0.1f, -0.2f), new Vector3(doorX + hw + 0.18f, pe - 0.1f, dz - 0.2f), new Vector3(doorX + hw + 0.18f, pe - 0.1f, z0), top + new Vector3(0, 0.1f, pd), twoSided: true);
            foreach (int s in new[] { -1, 1 })
                Window(k, new Pane(new Vector3(doorX + s * hw, 1.55f, dz + pd / 2), new Vector3(s, 0, 0), 0.5f, 0.75f, false), c with { Shutter = null }, h,
                    lit && h.Kind == HouseKind.Lived);
            TrimPaint(k, c.Trim);
            foreach (int s in new[] { -1, 1 })
                k.Box(new Vector3(doorX + s * hw - 0.06f, 0.15f, dz - 0.06f), new Vector3(doorX + s * hw + 0.06f, pe, dz + 0.06f));
        }
        // The casing, the step (one granite slab, or wood).
        TrimPaint(k, c.Trim);
        k.Box(new Vector3(doorX - dw / 2 - 0.14f, open ? 0 : Found, dz - 0.06f), new Vector3(doorX - dw / 2, doorH + 0.14f, dz + 0.01f));
        k.Box(new Vector3(doorX + dw / 2, open ? 0 : Found, dz - 0.06f), new Vector3(doorX + dw / 2 + 0.14f, doorH + 0.14f, dz + 0.01f));
        k.Box(new Vector3(doorX - dw / 2 - 0.16f, doorH, dz - 0.07f), new Vector3(doorX + dw / 2 + 0.16f, doorH + 0.18f, dz + 0.01f));
        k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1);
        k.Box(new Vector3(doorX - 0.75f, 0, dz - 0.6f), new Vector3(doorX + 0.75f, open ? 0.06f : 0.18f, dz));
        if (d.Porch == HousePorch.Stoop)
        {
            // A hood over the step on two brackets: a little gable roof in the house's roofing.
            float y = doorH + 0.35f, hw = 0.95f;
            TrimPaint(k, c.Trim);
            foreach (int s in new[] { -1, 1 })
                k.Rod(new Vector3(doorX + s * hw, y - 0.55f, dz - 0.02f), new Vector3(doorX + s * hw, y, dz - 0.75f), 0.045f);
            RoofPaint(k, c);
            var peak = new Vector3(doorX, y + 0.45f, dz - 0.85f);
            k.Quad(new Vector3(doorX - hw - 0.1f, y, dz - 0.85f), peak, peak with { Z = dz }, new Vector3(doorX - hw - 0.1f, y, dz), twoSided: true);
            k.Quad(peak, new Vector3(doorX + hw + 0.1f, y, dz - 0.85f), new Vector3(doorX + hw + 0.1f, y, dz), peak with { Z = dz }, twoSided: true);
            Siding(k, c);
            Face(k, new Vector3(doorX - hw, y, dz - 0.85f), new Vector3(doorX + hw, y, dz - 0.85f), peak, -Vector3.UnitZ, true);
        }
        if (open)
            return;
        if (h.Kind == HouseKind.Empty)
        {
            // Gone: the dark inside.
            k.Use("paint_black", Palette.SootBlack, 0.9f, 0);
            k.Shade(0.15f);
            k.Panel(new Vector3(doorX, doorH / 2, dz - 0.02f), -Vector3.UnitZ, Vector3.UnitY, dw, doorH, Vector2.Zero, Vector2.One);
            return;
        }
        // The door: its colour, four panels picked out, a glass light in its top on some.
        Paint(k, "clapboard", c.Door, 0.26f, 2.0f, 0.6f, 0.15f);
        k.Box(new Vector3(doorX - dw / 2, 0.05f, dz - 0.04f), new Vector3(doorX + dw / 2, doorH, dz));
        k.Shade(0.8f);
        foreach (float px in new[] { -dw / 4, dw / 4 })
            foreach (float py in new[] { doorH * 0.3f, doorH * 0.7f })
                k.BoxAt(new Vector3(doorX + px, py, dz - 0.05f), new Vector3(dw / 4 - 0.07f, doorH * 0.17f, 0.012f));
        if (h.Kind == HouseKind.Boarded)
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.95f, 0, tile: 1);
            k.Box(new Vector3(doorX - dw / 2 - 0.2f, doorH * 0.35f, dz - 0.1f), new Vector3(doorX + dw / 2 + 0.2f, doorH * 0.35f + 0.18f, dz - 0.04f));
            k.Box(new Vector3(doorX - dw / 2 - 0.2f, doorH * 0.7f, dz - 0.1f), new Vector3(doorX + dw / 2 + 0.2f, doorH * 0.7f + 0.18f, dz - 0.04f));
        }
        else if (h.Kind == HouseKind.Lived)
        {
            // The lamp by the door (WorldArt lights it, Porch).
            var lamp = Porch(h, doorH);
            k.Use("iron_smokebox", Palette.SootBlack, 0.6f, 0.3f, tile: 1);
            k.Box(new Vector3(lamp.X - 0.03f, lamp.Y + 0.12f, dz - 0.02f), new Vector3(lamp.X + 0.03f, lamp.Y + 0.17f, lamp.Z));
            k.Box(new Vector3(lamp.X - 0.09f, lamp.Y + 0.12f, lamp.Z - 0.09f), new Vector3(lamp.X + 0.09f, lamp.Y + 0.15f, lamp.Z + 0.09f));
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
            k.Emissive = lit ? 1 : 0.35f;
            k.BoxAt(lamp, new Vector3(0.07f, 0.11f, 0.07f));
            k.Emissive = 0;
        }
    }

    /// <summary>What breaks an eave-front roof's front slope: gable dormers, a shed dormer, a Lunenburg bump, the Island's centre gable.</summary>
    static void Dormers(Kit k, TownHouse h, Block b, Coat c, bool lit)
    {
        var d = h.Design;
        if (b.GableFront || d.Dormer == HouseDormer.None)
            return;
        float z0 = b.Z0;
        switch (d.Dormer)
        {
            case HouseDormer.Gables:
                for (int i = 0; i < Math.Max(1, d.Dormers); i++)
                {
                    float x = d.Dormers <= 1 ? 0 : b.X0 + b.W * (i + 0.5f) / d.Dormers;
                    Dormer(k, b, c, x, 0.65f, lit && i % 2 == 0, h);
                }
                break;
            case HouseDormer.Shed:
                {
                    // One long dormer, its roof a shallower slope back into the main one.
                    float zf = z0 + 0.7f, yb = b.RoofY(zf), yt = yb + 1.55f, hw = MathF.Max(1.2f, b.W / 2 - 1.1f);
                    float zb = ZAt(b, yt + 0.55f);
                    Siding(k, c);
                    k.Box(new Vector3(-hw, yb - 0.3f, zf), new Vector3(hw, yt, zb), Kit.Faces.NegZ | Kit.Faces.PosX | Kit.Faces.NegX);
                    RoofPaint(k, c);
                    k.Quad(new Vector3(-hw - 0.25f, yt + 0.05f, zf - 0.3f), new Vector3(hw + 0.25f, yt + 0.05f, zf - 0.3f), new Vector3(hw + 0.25f, yt + 0.6f, zb), new Vector3(-hw - 0.25f, yt + 0.6f, zb), twoSided: true);
                    int count = hw > 2.4f ? 3 : 2;
                    for (int i = 0; i < count; i++)
                        Window(k, new Pane(new Vector3(-hw + 2 * hw * (i + 0.5f) / count, yb + 0.75f, zf), -Vector3.UnitZ, 0.7f, 0.9f, false), c with { Shutter = null }, h, lit && i == 1);
                    break;
                }
            case HouseDormer.Bump:
                Bump(k, b, c, h, lit);
                break;
            case HouseDormer.CentreGable:
                IslandGable(k, b, c, h, lit);
                break;
        }
    }

    /// <summary>Where across an eave-front's front slope the roof reaches a height (the front slope only).</summary>
    static float ZAt(Block b, float y)
    {
        for (int i = 0; i + 1 < b.Profile.Count; i++)
        {
            var p = b.Profile[i];
            var q = b.Profile[i + 1];
            if (y <= q.Y && q.Y > p.Y)
                return p.X + (q.X - p.X) * Math.Clamp((y - p.Y) / (q.Y - p.Y), 0, 1);
        }
        return 0;
    }

    /// <summary>A gable dormer on the front slope at <paramref name="x"/>: its cheeks and face in the siding, a little gable roof, its window.</summary>
    static void Dormer(Kit k, Block b, Coat c, float x, float hw, bool lit, TownHouse h)
    {
        float zf = b.Z0 + 0.65f, yb = b.RoofY(zf), yt = yb + 1.35f, zb = ZAt(b, yt + 0.45f);
        if (zb <= zf + 0.2f)
            zb = zf + 0.8f;
        Siding(k, c);
        k.Box(new Vector3(x - hw, yb - 0.35f, zf), new Vector3(x + hw, yt, zb), Kit.Faces.NegZ | Kit.Faces.PosX | Kit.Faces.NegX);
        var peak = new Vector3(x, yt + hw * 0.85f, zf);
        Face(k, new Vector3(x - hw, yt, zf), new Vector3(x + hw, yt, zf), peak, -Vector3.UnitZ, false);
        RoofPaint(k, c);
        k.Quad(new Vector3(x - hw - 0.15f, yt - 0.08f, zf - 0.15f), peak + new Vector3(0, 0.08f, -0.15f), peak with { Y = peak.Y + 0.08f, Z = zb }, new Vector3(x - hw - 0.15f, yt - 0.08f, zb), twoSided: true);
        k.Quad(peak + new Vector3(0, 0.08f, -0.15f), new Vector3(x + hw + 0.15f, yt - 0.08f, zf - 0.15f), new Vector3(x + hw + 0.15f, yt - 0.08f, zb), peak with { Y = peak.Y + 0.08f, Z = zb }, twoSided: true);
        Window(k, new Pane(new Vector3(x, yb + 0.7f, zf), -Vector3.UnitZ, 0.6f, 0.85f, false), c with { Shutter = null }, h, lit);
    }

    /// <summary>The Island's centre gable over the door: a steep little gable standing up out of the front slope, its window under the point.</summary>
    static void IslandGable(Kit k, Block b, Coat c, TownHouse h, bool lit)
    {
        float hw = 1.05f, z0 = b.Z0, top = MathF.Min(b.Ridge - 0.2f, b.Eave + 2.0f);
        Siding(k, c);
        Face(k, new Vector3(-hw, b.Eave, z0), new Vector3(hw, b.Eave, z0), new Vector3(0, top, z0), -Vector3.UnitZ, false);
        RoofPaint(k, c);
        float back = ZAt(b, top) + 0.2f;
        var peak = new Vector3(0, top + 0.12f, z0 - 0.2f);
        k.Quad(new Vector3(-hw - 0.2f, b.Eave - 0.15f, z0 - 0.2f), peak, peak with { Z = back }, new Vector3(-hw - 0.2f, b.Eave - 0.15f, back), twoSided: true);
        k.Quad(peak, new Vector3(hw + 0.2f, b.Eave - 0.15f, z0 - 0.2f), new Vector3(hw + 0.2f, b.Eave - 0.15f, back), peak with { Z = back }, twoSided: true);
        TrimPaint(k, h.Design.Fancy ? c.Accent : c.Trim);
        k.Rod(new Vector3(-hw - 0.05f, b.Eave, z0 - 0.06f), new Vector3(0, top, z0 - 0.06f), 0.06f);
        k.Rod(new Vector3(0, top, z0 - 0.06f), new Vector3(hw + 0.05f, b.Eave, z0 - 0.06f), 0.06f);
        Window(k, new Pane(new Vector3(0, b.Eave + 0.55f, z0), -Vector3.UnitZ, 0.55f, 0.8f, false), c with { Shutter = null }, h, lit);
    }

    /// <summary>The Lunenburg bump: a bay standing out over the door from the first floor's top up into the roof, its windows, its own gable.</summary>
    static void Bump(Kit k, Block b, Coat c, TownHouse h, bool lit)
    {
        float hw = 1.2f, bot = MathF.Min(b.Eave - 0.4f, 2.7f), top = b.Eave + 1.1f, out_ = 0.75f, z0 = b.Z0;
        Siding(k, c);
        k.Box(new Vector3(-hw, bot, z0 - out_), new Vector3(hw, top, z0 + 0.6f), Kit.Faces.NegZ | Kit.Faces.PosX | Kit.Faces.NegX | Kit.Faces.NegY);
        // Its windows: the middle one tall, a narrow one in each cheek.
        var cc = c with { Shutter = null };
        if (b.Eave >= 5)
            Window(k, new Pane(new Vector3(0, b.Eave - 1.25f, z0 - out_), -Vector3.UnitZ, 0.85f, 1.25f, false), cc, h, lit);
        Window(k, new Pane(new Vector3(0, top - 0.6f, z0 - out_), -Vector3.UnitZ, 0.6f, 0.8f, false), cc, h, lit);
        foreach (int s in new[] { -1, 1 })
            Window(k, new Pane(new Vector3(s * hw, top - 0.6f, z0 - out_ / 2), new Vector3(s, 0, 0), 0.35f, 0.75f, false), cc, h, false);
        RoofPaint(k, c);
        var peak = new Vector3(0, top + 1.0f, z0 - out_ - 0.15f);
        float back = ZAt(b, top + 1.0f) + 0.2f;
        k.Quad(new Vector3(-hw - 0.15f, top, z0 - out_ - 0.15f), peak, peak with { Z = back }, new Vector3(-hw - 0.15f, top, back), twoSided: true);
        k.Quad(peak, new Vector3(hw + 0.15f, top, z0 - out_ - 0.15f), new Vector3(hw + 0.15f, top, back), peak with { Z = back }, twoSided: true);
        Siding(k, c);
        Face(k, new Vector3(-hw, top, z0 - out_ - 0.01f), new Vector3(hw, top, z0 - out_ - 0.01f), new Vector3(0, top + 0.95f, z0 - out_ - 0.01f), -Vector3.UnitZ, false);
        TrimPaint(k, h.Design.Fancy ? c.Accent : c.Trim);
        k.Box(new Vector3(-hw - 0.08f, bot - 0.18f, z0 - out_ - 0.08f), new Vector3(hw + 0.08f, bot, z0));
        k.Rod(new Vector3(-hw - 0.05f, top, z0 - out_ - 0.08f), new Vector3(0, top + 0.95f, z0 - out_ - 0.08f), 0.06f);
        k.Rod(new Vector3(0, top + 0.95f, z0 - out_ - 0.08f), new Vector3(hw + 0.05f, top, z0 - out_ - 0.08f), 0.06f);
    }

    /// <summary>The side wing: a lower block set back beside the house, its own roof, a window or two, sometimes its own door.</summary>
    static void Wing(Kit k, TownHouse h, Block b, Coat c, bool lit)
    {
        var d = h.Design;
        if (d.Ell == 0)
            return;
        float s = MathF.Sign(X(h, d.Ell));
        float inner = s * b.W / 2, outer = s * (b.W / 2 + (float)d.EllWidth);
        float ex0 = MathF.Min(inner, outer), ex1 = MathF.Max(inner, outer);
        float ez0 = b.Z0 + (float)d.EllSetback, ez1 = ez0 + (float)d.EllDepth, eave = d.EllTall ? 3.5f : 2.7f;
        float ridge = eave + 0.8f * (ez1 - ez0) / 2, mz = (ez0 + ez1) / 2;
        k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1.2f);
        k.Box(new Vector3(ex0, -0.4f, ez0 - 0.05f), new Vector3(ex1, Found, ez1 + 0.05f), Kit.Faces.Sides | Kit.Faces.PosY);
        Siding(k, c);
        k.Box(new Vector3(ex0, Found, ez0), new Vector3(ex1, eave, ez1), Kit.Faces.NegZ | Kit.Faces.PosZ | (s > 0 ? Kit.Faces.PosX : Kit.Faces.NegX));
        var outward = new Vector3(s, 0, 0);
        Face(k, new Vector3(outer, eave, ez0), new Vector3(outer, eave, ez1), new Vector3(outer, ridge, mz), outward, false);
        RoofPaint(k, c);
        float o = 0.3f;
        k.Quad(new Vector3(ex0 - 0.05f, eave - 0.1f, ez0 - o), new Vector3(ex1 + 0.25f, eave - 0.1f, ez0 - o), new Vector3(ex1 + 0.25f, ridge + 0.05f, mz), new Vector3(ex0 - 0.05f, ridge + 0.05f, mz), twoSided: true);
        k.Quad(new Vector3(ex0 - 0.05f, ridge + 0.05f, mz), new Vector3(ex1 + 0.25f, ridge + 0.05f, mz), new Vector3(ex1 + 0.25f, eave - 0.1f, ez1 + o), new Vector3(ex0 - 0.05f, eave - 0.1f, ez1 + o), twoSided: true);
        TrimPaint(k, c.Trim);
        k.Box(new Vector3(outer - 0.07f, Found, ez0 - 0.07f), new Vector3(outer + 0.07f, eave, ez0 + 0.07f));
        // A door into the wing on some (the woodshed's, the summer kitchen's), else a window; one in its gable end.
        float mid = (ex0 + ex1) / 2;
        bool door = (h.Id * 5 + d.Paint) % 3 == 0 && d.EllWidth >= 3.2;
        if (door)
        {
            Paint(k, "clapboard", c.Door, 0.26f, 2.0f, 0.6f, 0.15f);
            k.Box(new Vector3(mid - 0.45f, 0.05f, ez0 - 0.04f), new Vector3(mid + 0.45f, MathF.Min(eave - 0.25f, k.DoorHeight()), ez0));
        }
        else
            Window(k, new Pane(new Vector3(mid, GroundWindow, ez0), -Vector3.UnitZ, 0.8f, 1.1f, true), c, h, lit);
        if (ridge - eave > 1.1f)
            Window(k, new Pane(new Vector3(outer, eave + 0.45f, mz), outward, 0.5f, 0.6f, false), c with { Shutter = null }, h, false);
    }

    /// <summary>The chimneys: brick through the ridge in the middle, at one end or both, or a stovepipe out of the back slope.</summary>
    /// <summary>
    /// Where a house's chimneys (or its stovepipe) let out, in its own kit frame: where a lived-in house's wood smoke rises
    /// from (<see cref="Effects.Chimney"/>). As <see cref="Chimneys"/> builds them.
    /// </summary>
    public static IEnumerable<Vector3> ChimneyTops(TownHouse h)
    {
        var b = Shape(h);
        float sr = b.Profile.MaxBy(p => p.Y).X, a0 = b.A0 + 0.55f, a1 = b.A1 - 0.55f;
        switch (h.Design.Chimney)
        {
            case HouseChimney.Centre:
                yield return b.P(0, b.Ridge + 1.1f, sr);
                break;
            case HouseChimney.End:
                yield return b.P(h.Id % 2 == 0 ? a1 : a0, b.Ridge + 1.1f, sr);
                break;
            case HouseChimney.Ends:
                yield return b.P(a0, b.Ridge + 1.1f, sr);
                yield return b.P(a1, b.Ridge + 1.1f, sr);
                break;
            default:
                {
                    float s = b.S1 - (b.S1 - b.S0) * 0.25f;
                    yield return b.P((b.A1 - b.A0) * 0.2f, b.RoofY(s) + 1.55f, s);
                    break;
                }
        }
    }

    static void Chimneys(Kit k, TownHouse h, Block b)
    {
        var d = h.Design;
        float sr = b.Profile.MaxBy(p => p.Y).X;
        void Brick(float a)
        {
            k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1.2f);
            var lo = b.P(a, b.Ridge - 1.4f, sr);
            k.Box(lo + new Vector3(-0.38f, 0, -0.32f), b.P(a, b.Ridge + 0.95f, sr) + new Vector3(0.38f, 0, 0.32f), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1);
            var top = b.P(a, b.Ridge + 0.95f, sr);
            k.Box(top + new Vector3(-0.45f, 0, -0.38f), top + new Vector3(0.45f, 0.12f, 0.38f));
        }
        float a0 = b.A0 + 0.55f, a1 = b.A1 - 0.55f;
        switch (d.Chimney)
        {
            case HouseChimney.Centre:
                Brick(0);
                break;
            case HouseChimney.End:
                Brick(h.Id % 2 == 0 ? a1 : a0);
                break;
            case HouseChimney.Ends:
                Brick(a0);
                Brick(a1);
                break;
            default:
                {
                    // A stovepipe out of the back slope, its cap.
                    float s = b.S1 - (b.S1 - b.S0) * 0.25f, a = (b.A1 - b.A0) * 0.2f;
                    var foot = b.P(a, b.RoofY(s) - 0.3f, s);
                    k.Use("iron_smokebox", Palette.IronGrey, 0.5f, 0.5f, tile: 1);
                    k.Cylinder(foot, foot + Vector3.UnitY * 1.6f, 0.09f, 8);
                    k.Cylinder(foot + Vector3.UnitY * 1.6f, foot + Vector3.UnitY * 1.75f, 0.16f, 8, radiusB: 0.1f);
                    break;
                }
        }
    }

    /// <summary>Victorian trim in the accent: brackets under the eaves, a bargeboard up a street-facing gable.</summary>
    static void Fancy(Kit k, TownHouse h, Block b, Coat c)
    {
        Paint(k, "clapboard", c.Accent, 0.26f, 3.0f, 0.5f, 0.1f);
        if (!b.GableFront)
        {
            for (float x = b.X0 + 0.4f; x <= b.X1 - 0.3f; x += 0.9f)
                k.Box(new Vector3(x - 0.06f, b.Eave - 0.55f, b.Z0 - 0.22f), new Vector3(x + 0.06f, b.Eave - 0.22f, b.Z0));
            k.Box(new Vector3(b.X0 - 0.05f, b.Eave - 0.32f, b.Z0 - 0.1f), new Vector3(b.X1 + 0.05f, b.Eave - 0.24f, b.Z0 - 0.02f));
        }
        else
        {
            for (int i = 0; i + 1 < b.Profile.Count; i++)
                k.Rod(new Vector3(b.Profile[i].X, b.Profile[i].Y - 0.15f, b.Z0 - 0.12f), new Vector3(b.Profile[i + 1].X, b.Profile[i + 1].Y - 0.15f, b.Z0 - 0.12f), 0.09f);
            foreach (float x in new[] { b.X0 + 0.1f, b.X1 - 0.1f })
                k.Box(new Vector3(x - 0.07f, b.Eave - 0.6f, b.Z0 - 0.25f), new Vector3(x + 0.07f, b.Eave - 0.2f, b.Z0));
        }
    }

    /// <summary>A house burnt out: its walls to the sills and ragged, charred black, the chimney standing on its own.</summary>
    static void Burnt(Kit k, TownHouse h, Block b)
    {
        float w = b.W, d = b.D;
        k.Use("stone_block", Palette.Charcoal, 0.85f, 0.05f, tile: 1.2f);
        k.Box(new Vector3(-w / 2 - 0.06f, -0.4f, -d / 2 - 0.06f), new Vector3(w / 2 + 0.06f, Found, d / 2 + 0.06f), Kit.Faces.Sides | Kit.Faces.PosY);
        k.Use("wood_grey", Palette.SootBlack, 0.95f, 0, tile: 1);
        k.Tint = new Vector3(0.12f, 0.1f, 0.09f);
        var rng = new Random(77 + h.Id);
        for (float x = -w / 2; x < w / 2 - 0.1f; x += 0.9f)
            foreach (float z in new[] { -d / 2, d / 2 - 0.15f })
                k.Box(new Vector3(x, Found, z), new Vector3(Math.Min(x + 0.9f, w / 2), Found + 0.3f + (float)rng.NextDouble() * 1.4f, z + 0.15f));
        for (float z = -d / 2; z < d / 2 - 0.1f; z += 0.9f)
            foreach (float x in new[] { -w / 2, w / 2 - 0.15f })
                k.Box(new Vector3(x, Found, z), new Vector3(x + 0.15f, Found + 0.3f + (float)rng.NextDouble() * 1.2f, Math.Min(z + 0.9f, d / 2)));
        // A charred beam or two fallen across.
        k.Rod(new Vector3(-w / 2 + 0.3f, Found + 0.2f, -d / 2 + 0.5f), new Vector3(w / 2 - 0.6f, Found + 0.9f, d / 2 - 0.4f), 0.1f);
        k.Rod(new Vector3(w / 2 - 0.5f, Found + 0.1f, -d / 2 + 0.8f), new Vector3(-w / 2 + 1.2f, Found + 0.5f, d / 2 - 1.0f), 0.08f);
        if (h.Design.Chimney != HouseChimney.Pipe)
        {
            k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1.2f);
            k.Tint = new Vector3(0.35f, 0.25f, 0.2f);
            float cx = h.Design.Chimney == HouseChimney.Centre ? 0 : w / 2 - 0.6f;
            k.Box(new Vector3(cx - 0.38f, 0, -0.32f), new Vector3(cx + 0.38f, b.Ridge + 0.9f, 0.32f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
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
            case "wine":
                // Nicki's (note 551): a bottle of red half gone, and four odd glasses poured and waiting.
                k.Use("glass_dirty", new Vector3(0.18f, 0.28f, 0.16f), 0.2f, 0.8f, tile: 1);
                k.Tint = new Vector3(0.35f, 0.5f, 0.3f);
                k.Lathe(new Vector3(x, y, z), [new(0.04f, 0), new(0.04f, 0.2f), new(0.015f, 0.26f), new(0.014f, 0.31f)], 10);
                for (int i = 0; i < 4; i++)
                {
                    float gx = x + 0.12f + i * 0.09f, gz = z + (i % 2 == 0 ? 0.06f : -0.05f);
                    k.Use("glass_dirty", Palette.BoardEnamel, 0.1f, 0.9f, tile: 1);
                    k.Tint = new Vector3(0.9f);
                    k.Cylinder(new Vector3(gx, y, gz), new Vector3(gx, y + 0.07f, gz), 0.006f, 5);
                    k.Lathe(new Vector3(gx, y + 0.07f, gz), [new(0.012f, 0), new(0.03f, 0.04f), new(0.032f, 0.08f)], 8, capTop: false);
                    k.Use("cream", new Vector3(0.45f, 0.06f, 0.1f), 0.2f, 0.5f, tile: 1);
                    k.Tint = new Vector3(0.45f, 0.06f, 0.1f);
                    k.Disc(new Vector3(gx, y + 0.11f, gz), Vector3.UnitY, 0.026f, 8);
                }
                k.Tint = Vector3.One;
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

    // ---------------------------------------------------------------------------------------------------------------
    // The yard (ARCHITECTURE §8 note 335)

    /// <summary>
    /// What stands in a house's yard (<see cref="TownHouse.Yard"/>), where the Sim stands it: the picket fence out front in
    /// the trim's paint (grey, gapped and leaning where nobody lives), the board fence on the back line, and the yard's
    /// things: the woodpile under its sheet of roofing, the shed, the privy, the lobster traps, the dory turned over on its
    /// blocks, the washing, the rain barrel. In the house's own frame, so they're part of its mesh (no draws of their own).
    /// </summary>
    static void Yard(Kit k, TownHouse h, Coat c)
    {
        foreach (var y in h.Yard)
        {
            float xa = X(h, y.U0), xb = X(h, y.U1);
            float x0 = MathF.Min(xa, xb), x1 = MathF.Max(xa, xb), z0 = Z(h, y.V0), z1 = Z(h, y.V1), ht = (float)y.Height;
            var rng = new Random(h.Id * 131 + (int)(y.U0 * 17) + (int)y.Kind);
            switch (y.Kind)
            {
                case YardKind.Picket:
                    Picket(k, c, x0, x1, (z0 + z1) / 2, ht, y.Variant == 1, rng);
                    break;
                case YardKind.Boards:
                    k.Use("wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1);
                    if (z1 - z0 > x1 - x0)
                    {
                        // Down the lot's side, beside a lane (note 353).
                        float xm = (x0 + x1) / 2;
                        k.Quad(new Vector3(xm, ht, z0), new Vector3(xm, ht, z1), new Vector3(xm, 0, z1), new Vector3(xm, 0, z0), twoSided: true);
                        for (float z = z0 + 0.05f; z < z1; z += 2.4f)
                            k.Box(new Vector3(x0 - 0.05f, 0, z - 0.05f), new Vector3(x1 + 0.05f, ht + 0.05f, z + 0.05f), Kit.Faces.Sides | Kit.Faces.PosY);
                        break;
                    }
                    k.Quad(new Vector3(x0, ht, (z0 + z1) / 2), new Vector3(x1, ht, (z0 + z1) / 2), new Vector3(x1, 0, (z0 + z1) / 2), new Vector3(x0, 0, (z0 + z1) / 2), twoSided: true);
                    for (float x = x0 + 0.05f; x < x1; x += 2.4f)
                        k.Box(new Vector3(x - 0.05f, 0, z0 - 0.05f), new Vector3(x + 0.05f, ht + 0.05f, z1 + 0.05f), Kit.Faces.Sides | Kit.Faces.PosY);
                    break;
                case YardKind.Woodpile:
                    {
                        // Split wood laid across the pile: bark on top and at the ends, the cut ends either side (the
                        // cobbles' round stones, in the pale of fresh-cut spruce, read as the ends of the stacked sticks).
                        float top = ht - 0.12f;
                        k.Use("pine_bark", Palette.DeepBrown, 0.9f, 0.05f, tile: 0.5f);
                        k.Box(new Vector3(x0, 0, z0), new Vector3(x1, top, z1), Kit.Faces.PosX | Kit.Faces.NegX | Kit.Faces.PosY);
                        k.Use("cobbles", Palette.DeepBrown, 0.9f, 0.05f, tile: 0.35f);
                        k.Tint = new Vector3(0.95f, 0.72f, 0.5f);
                        k.Panel(new Vector3((x0 + x1) / 2, top / 2, z0), -Vector3.UnitZ, Vector3.UnitY, x1 - x0, top);
                        k.Panel(new Vector3((x0 + x1) / 2, top / 2, z1), Vector3.UnitZ, Vector3.UnitY, x1 - x0, top);
                    }
                    k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f, tile: 1);
                    k.Quad(new Vector3(x0 - 0.15f, ht, z0 - 0.2f), new Vector3(x1 + 0.15f, ht, z0 - 0.2f), new Vector3(x1 + 0.15f, ht - 0.08f, z1 + 0.15f), new Vector3(x0 - 0.15f, ht - 0.08f, z1 + 0.15f), twoSided: true);
                    break;
                case YardKind.Shed:
                    Shed(k, c, x0, x1, z0, z1, ht, gable: true);
                    break;
                case YardKind.Privy:
                    Shed(k, c, x0, x1, z0, z1, ht, gable: false);
                    break;
                case YardKind.Traps:
                    k.Use("wood_crate", Palette.DeepBrown, 0.9f, 0.05f, tile: 0.8f);
                    int stacks = Math.Max(1, (int)MathF.Round(x1 - x0)), layers = Math.Max(1, (int)MathF.Round(ht / 0.5f));
                    for (int i = 0; i < stacks; i++)
                        for (int j = 0; j < layers; j++)
                        {
                            float sx = x0 + i * (x1 - x0) / stacks, jitter = (float)(rng.NextDouble() - 0.5) * 0.1f;
                            k.Box(new Vector3(sx + 0.03f + jitter, j * 0.5f, z0), new Vector3(sx + (x1 - x0) / stacks - 0.03f + jitter, j * 0.5f + 0.47f, z1), Kit.Faces.All & ~Kit.Faces.NegY);
                        }
                    break;
                case YardKind.Dory:
                    Dory(k, x0, x1, z0, z1, ht, y.Variant);
                    break;
                case YardKind.Clothesline:
                    Washing(k, x0, x1, (z0 + z1) / 2, ht, rng);
                    break;
                case YardKind.Barrel:
                    k.Use("wood_crate", Palette.DeepBrown, 0.9f, 0.05f, tile: 0.6f);
                    k.Shade(0.6f);
                    k.Cylinder(new Vector3((x0 + x1) / 2, 0, (z0 + z1) / 2), new Vector3((x0 + x1) / 2, ht, (z0 + z1) / 2), (x1 - x0) / 2, 10);
                    break;
            }
        }
    }

    /// <summary>A picket fence along x at <paramref name="z"/>: two rails, the pickets on them, a post at each end.</summary>
    static void Picket(Kit k, Coat c, float x0, float x1, float z, float ht, bool worn, Random rng)
    {
        TrimPaint(k, worn ? Vector3.Lerp(c.Trim, new Vector3(0.42f, 0.42f, 0.4f), 0.65f) : c.Trim);
        foreach (float y in new[] { 0.25f, ht - 0.3f })
            k.Box(new Vector3(x0, y, z + 0.015f), new Vector3(x1, y + 0.07f, z + 0.045f), Kit.Faces.Sides | Kit.Faces.PosY);
        foreach (float x in new[] { x0, x1 })
            k.Box(new Vector3(x - 0.05f, 0, z - 0.03f), new Vector3(x + 0.05f, ht + 0.1f, z + 0.05f), Kit.Faces.Sides | Kit.Faces.PosY);
        for (float x = x0 + 0.1f; x < x1 - 0.06f; x += 0.14f)
        {
            // Nobody's: pickets gone, and others short where they've split.
            if (worn && rng.NextDouble() < 0.25)
                continue;
            float top = worn && rng.NextDouble() < 0.2 ? ht - 0.3f : ht;
            k.Box(new Vector3(x - 0.035f, 0, z - 0.012f), new Vector3(x + 0.035f, top, z + 0.012f), Kit.Faces.PosZ | Kit.Faces.NegZ | Kit.Faces.PosY);
        }
    }

    /// <summary>A shed (a gable roof along it, its door toward the house) or a privy (a lean-to roof, a narrow door).</summary>
    static void Shed(Kit k, Coat c, float x0, float x1, float z0, float z1, float ht, bool gable)
    {
        float wall = gable ? ht - 0.9f : ht - 0.35f, xm = (x0 + x1) / 2;
        k.Use(c.Shingle ? "shingle_cedar" : "wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(x0, 0, z0), new Vector3(x1, gable ? wall : ht, z1), Kit.Faces.Sides);
        RoofPaint(k, c);
        if (gable)
        {
            float zm = (z0 + z1) / 2;
            k.Quad(new Vector3(x0 - 0.2f, ht, zm), new Vector3(x1 + 0.2f, ht, zm), new Vector3(x1 + 0.2f, wall - 0.1f, z0 - 0.25f), new Vector3(x0 - 0.2f, wall - 0.1f, z0 - 0.25f), twoSided: true);
            k.Quad(new Vector3(x0 - 0.2f, wall - 0.1f, z1 + 0.25f), new Vector3(x1 + 0.2f, wall - 0.1f, z1 + 0.25f), new Vector3(x1 + 0.2f, ht, zm), new Vector3(x0 - 0.2f, ht, zm), twoSided: true);
            k.Use(c.Shingle ? "shingle_cedar" : "wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1);
            foreach (float x in new[] { x0, x1 })
                k.Tri(new Vector3(x, wall, z0), new Vector3(x, ht, zm), new Vector3(x, wall, z1), new(0, 0), new(0.5f, 1), new(1, 0));
        }
        else
            k.Quad(new Vector3(x0 - 0.1f, ht + 0.05f, z0 - 0.2f), new Vector3(x1 + 0.1f, ht + 0.05f, z0 - 0.2f), new Vector3(x1 + 0.1f, ht - 0.35f, z1 + 0.15f), new Vector3(x0 - 0.1f, ht - 0.35f, z1 + 0.15f), twoSided: true);
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.25f);
        float doorW = gable ? 0.9f : 0.6f;
        k.Panel(new Vector3(xm, 0.95f, z0 - 0.02f), -Vector3.UnitZ, Vector3.UnitY, doorW, 1.9f);
    }

    /// <summary>A dory turned over on its blocks: the hull's flat bottom up, its sides drawn in to the bow and stern.</summary>
    static void Dory(Kit k, float x0, float x1, float z0, float z1, float ht, int variant)
    {
        // Dory buff, a dark green or an old red (the yard's, not the house's).
        var paint = variant switch { 0 => new Vector3(0.62f, 0.52f, 0.32f), 1 => new Vector3(0.12f, 0.24f, 0.17f), _ => new Vector3(0.42f, 0.1f, 0.08f) };
        k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.1f, tile: 0.8f);
        k.Tint = paint * 2.2f;
        float zm = (z0 + z1) / 2, lift = 0.22f, body0 = x0 + 0.7f, body1 = x1 - 0.7f, half = (z1 - z0) / 2;
        k.Box(new Vector3(body0, lift, z0), new Vector3(body1, ht, z1), Kit.Faces.PosY | Kit.Faces.PosZ | Kit.Faces.NegZ);
        foreach (var (tip, root) in new[] { (x0, body0), (x1, body1) })
        {
            var t0 = new Vector3(tip, lift + 0.1f, zm);
            var t1 = new Vector3(tip, ht + 0.05f, zm);
            var a0 = new Vector3(root, lift, zm - half);
            var a1 = new Vector3(root, ht, zm - half);
            var b0 = new Vector3(root, lift, zm + half);
            var b1 = new Vector3(root, ht, zm + half);
            k.Quad(a1, t1, t0, a0, twoSided: true);
            k.Quad(t1, b1, b0, t0, twoSided: true);
            k.Tri(a1, b1, t1, new(0, 0), new(1, 0), new(0.5f, 1));
        }
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        foreach (float x in new[] { body0 + 0.3f, body1 - 0.3f })
            k.Box(new Vector3(x - 0.15f, 0, z0 + 0.1f), new Vector3(x + 0.15f, lift, z1 - 0.1f), Kit.Faces.Sides | Kit.Faces.PosY);
    }

    /// <summary>
    /// The washing: two posts and the line between, and what's pegged out on it, sheets and shirts hanging dead still in the
    /// night air. Nobody brings the washing in after dark.
    /// </summary>
    static void Washing(Kit k, float x0, float x1, float z, float ht, Random rng)
    {
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        foreach (float x in new[] { x0, x1 })
            k.Box(new Vector3(x - 0.04f, 0, z - 0.04f), new Vector3(x + 0.04f, ht, z + 0.04f), Kit.Faces.Sides | Kit.Faces.PosY);
        k.Use("wool", Palette.BoardEnamel, 0.9f, 0);
        k.Rod(new Vector3(x0, ht - 0.12f, z), new Vector3(x1, ht - 0.12f, z), 0.008f);
        for (float x = x0 + 0.4f + (float)rng.NextDouble() * 0.5f; x < x1 - 0.5f; x += 0.4f + (float)rng.NextDouble() * 0.8f)
        {
            float w = 0.45f + (float)rng.NextDouble() * 0.7f, hgt = 0.5f + (float)rng.NextDouble() * 0.6f;
            if (x + w > x1 - 0.2f)
                break;
            k.Tint = Vector3.One * (0.75f + 0.35f * (float)rng.NextDouble());
            k.Panel(new Vector3(x + w / 2, ht - 0.12f - hgt / 2, z), Vector3.UnitZ, Vector3.UnitY, w, hgt, twoSided: true);
            x += w;
        }
    }

    /// <summary>The yard from down the street: its shed, privy, dory and woodpile as plain blocks (the fences are too fine to see).</summary>
    static void FarYard(Kit k, TownHouse h)
    {
        k.Use("wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1);
        foreach (var y in h.Yard.Where(y => y.Kind is YardKind.Shed or YardKind.Privy or YardKind.Dory or YardKind.Woodpile))
        {
            float xa = X(h, y.U0), xb = X(h, y.U1);
            k.Box(new Vector3(MathF.Min(xa, xb), 0, Z(h, y.V0)), new Vector3(MathF.Max(xa, xb), (float)y.Height, Z(h, y.V1)), Kit.Faces.Sides | Kit.Faces.PosY);
        }
    }
}
