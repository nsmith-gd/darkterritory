using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The towns' houses with floors (queue #725, ARCHITECTURE §8 note 1022), drawn from the Sim's own shell
/// (<see cref="TownFloors.Shell"/>), so what's drawn is what's walked: the flight up the parlour's end wall, the upper floor
/// round its well and its rails, the bedrooms (plastered, their windows from inside, the beds made, the washstand's lamp),
/// the window that opens over the porch roof out the back, the boards with the hatch let into them and its lid thrown back,
/// the ladder down, and the cellar under: stone, its shelves and barrels, dark. A burnt house's charred boards, burnt
/// through over its cellar, a ladder left down the hole.
/// </summary>
public static partial class MaritimeKit
{
    /// <summary>A box of the shell (u, v in the house's frame, heights over its boards) in the kit's frame.</summary>
    static void ShellBox(Kit k, TownHouse h, double u, double v, double hu, double hv, double bottom, double top, Kit.Faces faces = Kit.Faces.All)
    {
        float xa = X(h, u - hu), xb = X(h, u + hu);
        k.Box(new Vector3(MathF.Min(xa, xb), (float)bottom, Z(h, v - hv)), new Vector3(MathF.Max(xa, xb), (float)top, Z(h, v + hv)), faces);
    }

    /// <summary>The upper rooms' ceiling (m over the boards): a hand under the eaves, never lower than a room needs.</summary>
    static float UpperCeiling(TownHouse h) => (float)Math.Min(h.Design.Eaves - 0.15, TownFloors.Storey + 2.45);

    /// <summary>
    /// A house's floors (note 1022): everything of <see cref="TownFloors.Shell"/> the house's own walls and the ground floor
    /// (<see cref="Inside"/>) don't draw, the upstairs' plaster and windows, its furniture, the ladders and the cellar.
    /// </summary>
    static void Floors(Kit k, TownHouse h, List<Pane> panes, bool lit)
    {
        bool burnt = h.Kind == HouseKind.Burnt, lived = h.Layout is not null;
        float f1 = (float)TownFloors.Storey;
        foreach (var p in TownFloors.Shell(h))
        {
            switch (p.Part)
            {
                case ShellPart.Floor:
                    // The ground floor's boards (charred black in a burnt house), the hatch let in.
                    k.Use("wood_floor", burnt ? Palette.SootBlack : Palette.DeepBrown, 0.8f, 0.1f, tile: 1.5f);
                    if (burnt)
                        k.Tint = new Vector3(0.14f, 0.12f, 0.1f);
                    ShellBox(k, h, p.X, p.Y, p.HalfX, p.HalfY, p.Bottom, burnt ? 0.03 : 0.02, Kit.Faces.PosY | Kit.Faces.NegY | Kit.Faces.Sides);
                    break;
                case ShellPart.Cellar:
                    // The cellar's floor: beaten earth and flags.
                    k.Use("stone_block", Palette.Charcoal, 0.95f, 0.02f, tile: 1.2f);
                    k.Tint = new Vector3(0.4f, 0.38f, 0.35f);
                    ShellBox(k, h, p.X, p.Y, p.HalfX, p.HalfY, p.Bottom, p.Top, Kit.Faces.PosY);
                    break;
                case ShellPart.Tread:
                    k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.12f, tile: 1);
                    k.Tint = new Vector3(0.7f, 0.55f, 0.42f);
                    ShellBox(k, h, p.X, p.Y, p.HalfX, p.HalfY, 0, p.Top, Kit.Faces.PosY | Kit.Faces.Sides);
                    break;
                case ShellPart.Upper:
                    // The upper floor's boards over, the ground floor's ceiling under.
                    k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.1f, tile: 1.5f);
                    ShellBox(k, h, p.X, p.Y, p.HalfX, p.HalfY, p.Bottom, p.Top, Kit.Faces.PosY | Kit.Faces.Sides);
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
                    k.Tint = new Vector3(0.55f, 0.5f, 0.42f);
                    ShellBox(k, h, p.X, p.Y, p.HalfX, p.HalfY, p.Bottom, p.Bottom + 0.01, Kit.Faces.NegY);
                    break;
                case ShellPart.Rail:
                    {
                        // A handrail on turned balusters.
                        k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.2f, tile: 1);
                        k.Tint = new Vector3(0.6f, 0.42f, 0.3f);
                        ShellBox(k, h, p.X, p.Y, p.HalfX + 0.01, p.HalfY + 0.01, p.Top - 0.07, p.Top);
                        bool alongV = p.HalfY > p.HalfX;
                        double run = alongV ? p.HalfY : p.HalfX;
                        int n = Math.Max(2, (int)(run * 2 / 0.32));
                        for (int i = 0; i <= n; i++)
                        {
                            double a = -run + 2 * run * i / n;
                            double bu = alongV ? p.X : p.X + a, bv = alongV ? p.Y + a : p.Y;
                            ShellBox(k, h, bu, bv, 0.025, 0.025, f1, p.Top - 0.07);
                        }
                        break;
                    }
                case ShellPart.Roof:
                    PorchRoof(k, h, p);
                    break;
                case ShellPart.Wall or ShellPart.Lintel:
                    {
                        bool partition = Math.Abs(p.X) < 0.01 && p.HalfX < 0.1;
                        if (partition && p.Bottom >= f1 - 0.01)
                        {
                            // Upstairs, the partition between the bedrooms, plastered both sides.
                            k.Use("concrete_stain", Palette.MuddyOlive, 0.9f, 0.02f, tile: 1.5f);
                            k.Tint = Plaster;
                            ShellBox(k, h, p.X, p.Y, p.HalfX, p.HalfY, p.Bottom, Math.Min(p.Top, UpperCeiling(h)));
                        }
                        else if (!partition && p.Bottom < -0.5)
                        {
                            // The cellar's walls, stone (under the ground floor's boards).
                            k.Use("stone_block", Palette.BlueGrey * 0.7f, 0.95f, 0.02f, tile: 1.3f);
                            ShellBox(k, h, p.X, p.Y, p.HalfX, p.HalfY, -TownFloors.Cellar - 0.05, -0.05);
                        }
                        break;
                    }
            }
            k.Tint = Vector3.One;
        }
        if (h.Upstairs)
            Upstairs(k, h, panes, lit && lived);
        if (h.Cellar)
            Cellar(k, h);
    }

    /// <summary>The bedrooms: plaster to the ceiling, the windows from inside (the one over the porch roof standing open), the beds and the rest.</summary>
    static void Upstairs(Kit k, TownHouse h, List<Pane> panes, bool lamp)
    {
        const float t = (float)HouseLayout.Wall, skin = 0.03f;
        float w = (float)h.Width / 2, d = (float)h.Depth, z0 = -d / 2, z1 = d / 2, f1 = (float)TownFloors.Storey, ceil = UpperCeiling(h);
        bool left = h.Left is not null;
        void Plaster4(Vector3 min, Vector3 max)
        {
            k.Use("concrete_stain", Palette.MuddyOlive, 0.9f, 0.02f, tile: 1.5f);
            k.Tint = Plaster * (left ? 0.7f : 1);
            k.Box(min, max);
        }
        // The walls' plaster (the back's round the window that opens), and the ceiling.
        var open = panes.FirstOrDefault(p => p.Opens);
        Plaster4(new Vector3(-w + t, f1, z0 + t), new Vector3(w - t, ceil, z0 + t + skin));
        Plaster4(new Vector3(-w + t, f1, z0 + t), new Vector3(-w + t + skin, ceil, z1 - t));
        Plaster4(new Vector3(w - t - skin, f1, z0 + t), new Vector3(w - t, ceil, z1 - t));
        if (open.W > 0)
        {
            float ox = open.At.X, hw = open.W / 2, sill = open.At.Y - open.H / 2, head = open.At.Y + open.H / 2;
            Plaster4(new Vector3(-w + t, f1, z1 - t - skin), new Vector3(ox - hw, ceil, z1 - t));
            Plaster4(new Vector3(ox + hw, f1, z1 - t - skin), new Vector3(w - t, ceil, z1 - t));
            Plaster4(new Vector3(ox - hw, f1, z1 - t - skin), new Vector3(ox + hw, sill, z1 - t));
            Plaster4(new Vector3(ox - hw, head, z1 - t - skin), new Vector3(ox + hw, ceil, z1 - t));
            // Its reveal through the wall, and a blanket box under it to step up on.
            k.Use("paint_black", Trim, 0.6f, 0.1f);
            k.Tint = Trim * 0.8f;
            k.Box(new Vector3(ox - hw - 0.05f, sill - 0.05f, z1 - t - skin - 0.08f), new Vector3(ox + hw + 0.05f, sill, z1));
            k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
            k.Tint = new Vector3(0.55f, 0.38f, 0.26f);
            k.Box(new Vector3(ox - 0.45f, f1, z1 - t - 0.45f), new Vector3(ox + 0.45f, f1 + 0.48f, z1 - t - skin));
        }
        else
            Plaster4(new Vector3(-w + t, f1, z1 - t - skin), new Vector3(w - t, ceil, z1 - t));
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
        k.Tint = new Vector3(0.55f, 0.5f, 0.42f) * (left ? 0.7f : 1);
        k.Box(new Vector3(-w + t, ceil, z0 + t), new Vector3(w - t, ceil + 0.05f, z1 - t), Kit.Faces.NegY);
        // The upstairs windows from inside: the night through the glass.
        foreach (var p in panes.Where(p => !p.Ground && !p.Opens && p.At.Y + p.H / 2 < ceil && p.At.Y - p.H / 2 > f1))
        {
            var inner = p.At - p.Out * (t + skin);
            k.Use("paint_black", Trim, 0.6f, 0.1f);
            k.Tint = Trim * 0.8f;
            Slab(k, inner - Vector3.UnitY * (p.H / 2 + 0.05f), -p.Out, p.W / 2 + 0.08f, 0.05f, 0, 0.1f);
            Slab(k, inner + Vector3.UnitY * (p.H / 2 + 0.05f), -p.Out, p.W / 2 + 0.08f, 0.05f, 0, 0.04f);
            k.Use("glass_dirty", Palette.BlueGrey, 0.4f, 0.4f, tile: 1);
            k.Shade(0.12f);
            k.Panel(inner - p.Out * 0.005f, -p.Out, Vector3.UnitY, p.W, p.H, Vector2.Zero, Vector2.One);
        }
        k.Tint = Vector3.One;
        foreach (var x in TownFloors.Furniture(h))
            Bedroom(k, x, X(h, x.U), Z(h, x.V), f1, left, lamp);
    }

    /// <summary>A piece of the bedrooms' furniture at its place (the kit's frame), on the upper floor; under a dust sheet in a house left boarded.</summary>
    static void Bedroom(Kit k, HouseThing x, float cx, float cz, float f, bool sheeted, bool lamp)
    {
        float hx = (float)x.HalfU, hz = (float)x.HalfV, ht = (float)x.Height;
        if (sheeted && x.Kind != "washstand")
        {
            // Under a dust sheet, gone grey, its hem on the boards.
            k.Use("wool", Palette.BoardEnamel, 0.95f, 0, tile: 1);
            k.Tint = new Vector3(0.42f, 0.41f, 0.38f);
            k.Box(new Vector3(cx - hx - 0.03f, f, cz - hz - 0.03f), new Vector3(cx + hx + 0.03f, f + ht + 0.04f, cz + hz + 0.03f), Kit.Faces.Sides | Kit.Faces.PosY);
            k.Tint = Vector3.One;
            return;
        }
        switch (x.Kind)
        {
            case "bed" or "cot":
                {
                    // An iron bedstead (the cot a wooden one), the mattress, a patchwork quilt, the pillow at the head (toward the back).
                    bool cot = x.Kind == "cot";
                    k.Use(cot ? "wood_floor" : "iron_smokebox", cot ? Palette.DeepBrown : Palette.SootBlack, 0.7f, 0.3f, tile: 1);
                    foreach (float lx in new[] { cx - hx + 0.03f, cx + hx - 0.03f })
                        foreach (float lz in new[] { cz - hz + 0.03f, cz + hz - 0.03f })
                            k.Box(new Vector3(lx - 0.025f, f, lz - 0.025f), new Vector3(lx + 0.025f, f + (lz > cz ? ht + 0.45f : ht + 0.15f), lz + 0.025f));
                    k.Box(new Vector3(cx - hx, f + ht + 0.2f, cz + hz - 0.04f), new Vector3(cx + hx, f + ht + 0.42f, cz + hz - 0.01f));
                    k.Use("sac", Palette.BoardEnamel, 0.9f, 0, tile: 1);
                    k.Tint = new Vector3(0.75f, 0.72f, 0.64f);
                    k.Box(new Vector3(cx - hx + 0.04f, f + 0.3f, cz - hz + 0.04f), new Vector3(cx + hx - 0.04f, f + ht - 0.05f, cz + hz - 0.05f));
                    k.Use("wool", Palette.RustRed, 0.9f, 0, tile: 0.6f);
                    k.Tint = cot ? new Vector3(0.45f, 0.5f, 0.6f) : new Vector3(0.55f, 0.3f, 0.25f);
                    k.Box(new Vector3(cx - hx + 0.02f, f + ht - 0.08f, cz - hz + 0.02f), new Vector3(cx + hx - 0.02f, f + ht, cz + hz - 0.35f), Kit.Faces.Sides | Kit.Faces.PosY);
                    k.Use("sac", Palette.BoardEnamel, 0.9f, 0, tile: 1);
                    k.Tint = new Vector3(0.85f, 0.82f, 0.75f);
                    k.Box(new Vector3(cx - hx + 0.1f, f + ht - 0.06f, cz + hz - 0.32f), new Vector3(cx + hx - 0.1f, f + ht + 0.08f, cz + hz - 0.08f));
                    break;
                }
            case "chest":
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
                k.Tint = new Vector3(0.5f, 0.32f, 0.22f);
                k.Box(new Vector3(cx - hx, f, cz - hz), new Vector3(cx + hx, f + ht, cz + hz));
                k.Use("iron_smokebox", Palette.SootBlack, 0.6f, 0.3f, tile: 1);
                foreach (float bx in new[] { cx - hx * 0.6f, cx + hx * 0.6f })
                    k.Box(new Vector3(bx - 0.03f, f, cz - hz - 0.01f), new Vector3(bx + 0.03f, f + ht + 0.01f, cz + hz + 0.01f));
                break;
            case "wardrobe":
                k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
                k.Tint = new Vector3(0.6f, 0.42f, 0.3f);
                k.Box(new Vector3(cx - hx, f, cz - hz), new Vector3(cx + hx, f + ht, cz + hz));
                k.Tint = new Vector3(0.45f, 0.3f, 0.22f);
                k.Box(new Vector3(cx - hx - 0.03f, f + ht, cz - hz - 0.03f), new Vector3(cx + hx + 0.03f, f + ht + 0.08f, cz + hz + 0.03f));
                break;
            case "washstand":
                {
                    // A marble-topped stand, the basin and the jug, and the bedroom's lamp (lit where somebody's home: MaritimeKit.Lights).
                    k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.15f, tile: 1);
                    k.Tint = new Vector3(0.6f, 0.42f, 0.3f);
                    k.Box(new Vector3(cx - hx, f, cz - hz), new Vector3(cx + hx, f + ht, cz + hz));
                    k.Use("paper_form", Palette.BoardEnamel, 0.4f, 0.3f, tile: 1);
                    k.Cylinder(new Vector3(cx, f + ht, cz), new Vector3(cx, f + ht + 0.08f, cz), 0.16f, 12, radiusB: 0.2f);
                    k.Lathe(new Vector3(cx + 0.12f, f + ht, cz + 0.08f), [new(0.06f, 0), new(0.08f, 0.12f), new(0.05f, 0.24f), new(0.06f, 0.28f)], 10);
                    k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.6f);
                    float lx = cx - hx * 0.55f;
                    k.Cylinder(new Vector3(lx, f + ht, cz), new Vector3(lx, f + ht + 0.1f, cz), 0.07f, 10, radiusB: 0.04f);
                    k.Use("lantern_pane", lamp ? Palette.LampAmber : Palette.BlueGrey, 0, 0, tile: 0.25f);
                    k.Emissive = lamp ? 1 : 0;
                    k.Cylinder(new Vector3(lx, f + ht + 0.1f, cz), new Vector3(lx, f + ht + 0.32f, cz), 0.05f, 10, radiusB: 0.035f);
                    k.Emissive = 0;
                    break;
                }
        }
        k.Tint = Vector3.One;
    }

    /// <summary>
    /// A house's cellar as a dark room (<see cref="Room.Dark"/>, note 1022; the villages' basements', note 1005), from under its
    /// floor (the shader fades a room over its last 15 cm) up to the boards' underside, with the house placed by <paramref name="m"/>.
    /// </summary>
    public static Room CellarRoom(TownHouse h, Matrix4x4 m)
    {
        const float under = 0.3f;
        float bottom = -(float)TownFloors.Cellar - under, top = -(float)TownFloors.Slab;
        var at = Turned(h) * m;
        var right = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, at));
        var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, at));
        var back = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, at));
        // The shader's frame has up = back x right.
        if (Vector3.Cross(back, right).Y < 0)
            back = -back;
        float t = (float)HouseLayout.Wall;
        return new Room(Vector3.Transform(new Vector3(0, (bottom + top) / 2, 0), at), right, up, back,
            new Vector3((float)h.Width / 2 - t, (top - bottom) / 2, (float)h.Depth / 2 - t)) { Dark = true };
    }

    /// <summary>Where the bedroom's lamp is (the kit's frame): on the washstand.</summary>
    static IEnumerable<(Vector3 At, Vector3 Colour, float Range)> UpstairsLights(TownHouse h)
    {
        if (!h.Upstairs || h.Layout is null)
            yield break;
        foreach (var x in TownFloors.Furniture(h).Where(x => x.Kind == "washstand"))
            yield return (new Vector3(X(h, x.U) - (float)x.HalfU * 0.55f, (float)(TownFloors.Storey + x.Height) + 0.4f, Z(h, x.V)), Palette.LampAmber * 0.8f, 4.5f);
    }

    /// <summary>
    /// The porch roof out the back under the window that opens: a lean-to of shingles down from the wall, on two posts, its
    /// fascia; where the Sim stands its slab (its slope taken flat at its middle), a hand either side.
    /// </summary>
    static void PorchRoof(Kit k, TownHouse h, ShellBox p)
    {
        float xa = X(h, p.X - p.HalfX), xb = X(h, p.X + p.HalfX), x0 = MathF.Min(xa, xb), x1 = MathF.Max(xa, xb);
        float zw = Z(h, h.Depth), ze = Z(h, p.Y + p.HalfY), mid = (float)p.Top, fall = 0.18f;
        float yw = mid + fall, ye = mid - fall;
        k.Use("shingle_cedar", Palette.DeepBrown, 0.85f, 0.05f, tile: 1);
        k.Tint = new Vector3(0.45f, 0.4f, 0.36f);
        k.Quad(new Vector3(x0, yw, zw), new Vector3(x1, yw, zw), new Vector3(x1, ye, ze), new Vector3(x0, ye, ze), twoSided: true);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Tint = new Vector3(0.6f, 0.55f, 0.48f);
        k.Box(new Vector3(x0 - 0.04f, ye - 0.2f, ze - 0.04f), new Vector3(x1 + 0.04f, ye, ze + 0.02f));
        foreach (float x in new[] { x0 + 0.08f, x1 - 0.08f })
            k.Box(new Vector3(x - 0.06f, 0, ze - 0.18f), new Vector3(x + 0.06f, ye - 0.2f, ze - 0.06f));
        k.Tint = Vector3.One;
    }

    /// <summary>The ladder down the hatch (or the burnt hole), the hatch's lid thrown back, and the cellar: stone, shelves, barrels, a crate.</summary>
    static void Cellar(Kit k, TownHouse h)
    {
        bool burnt = h.Kind == HouseKind.Burnt;
        float lo = -(float)TownFloors.Cellar;
        foreach (var (u, v, foot, top, inU, inV, _) in TownFloors.Ladders(h).Where(l => l.Foot < 0))
        {
            // Its rails up past the boards to hold, its rungs.
            var across = new Vector3((float)inV, 0, -h.Side * (float)inU);
            var at = new Vector3(X(h, u), 0, Z(h, v));
            k.Use(burnt ? "wood_grey" : "wood_floor", burnt ? Palette.SootBlack : Palette.DeepBrown, 0.85f, 0.1f, tile: 1);
            k.Tint = burnt ? new Vector3(0.3f, 0.26f, 0.22f) : new Vector3(0.65f, 0.5f, 0.36f);
            foreach (float s in new[] { -0.22f, 0.22f })
                k.Rod(at + across * s + Vector3.UnitY * lo, at + across * s + Vector3.UnitY * 0.9f, 0.03f);
            for (float y = lo + 0.28f; y < 0.75f; y += 0.28f)
                k.Rod(at - across * 0.22f + Vector3.UnitY * y, at + across * 0.22f + Vector3.UnitY * y, 0.018f);
        }
        var (hu, hv, _, iv) = TownFloors.Hatch(h);
        float hs = (float)TownFloors.HatchOf(h) / 2;
        if (!burnt)
        {
            // The hatch's lid, thrown back on its hinges against the back edge, standing.
            k.Use("wood_floor", Palette.DeepBrown, 0.8f, 0.1f, tile: 1);
            float bz = Z(h, hv - iv * hs);
            float xa = X(h, hu - hs), xb = X(h, hu + hs);
            k.Box(new Vector3(MathF.Min(xa, xb), 0.02f, bz), new Vector3(MathF.Max(xa, xb), 0.02f + hs * 2, bz + 0.05f));
            k.Use("iron_smokebox", Palette.SootBlack, 0.6f, 0.3f, tile: 1);
            k.BoxAt(new Vector3(X(h, hu), 0.55f, bz - 0.03f), new Vector3(0.06f, 0.02f, 0.03f));
        }
        else
        {
            // The burnt boards' ends round the hole, charred and broken short.
            k.Use("wood_grey", Palette.SootBlack, 0.95f, 0, tile: 1);
            k.Tint = new Vector3(0.1f, 0.09f, 0.08f);
            var rng = new Random(h.Id * 17 + 3);
            for (int i = 0; i < 6; i++)
            {
                double a = -hs + (i + 0.5) * 2 * hs / 6;
                float len = 0.1f + (float)rng.NextDouble() * 0.35f;
                ShellBox(k, h, hu + a, hv - hs - 0.01 + len / 2, 0.06, len / 2, -0.06, 0.02);
            }
        }
        // Down there: shelves of jars on the deep end's wall, barrels and a crate along a side, a coal heap in the corner.
        float w = (float)h.Width / 2 - (float)HouseLayout.Wall, z1 = (float)h.Depth / 2 - (float)HouseLayout.Wall, z0 = -z1;
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        k.Tint = new Vector3(0.5f, 0.45f, 0.38f);
        float sx = -X(h, TownFloors.StairSide(h) * 1.0);
        for (int i = 0; i < 3; i++)
            k.Box(new Vector3(sx - 1.0f, lo + 0.5f + i * 0.55f, z0 + 0.02f), new Vector3(sx + 1.0f, lo + 0.54f + i * 0.55f, z0 + 0.38f));
        k.Use("glass_dirty", new Vector3(0.3f, 0.35f, 0.2f), 0.4f, 0.6f, tile: 1);
        var jars = new Random(h.Id * 7 + 1);
        for (int i = 0; i < 3; i++)
            for (float x = sx - 0.9f; x < sx + 0.9f; x += 0.22f)
                if (jars.NextDouble() < 0.7)
                    k.Cylinder(new Vector3(x, lo + 0.54f + i * 0.55f, z0 + 0.2f), new Vector3(x, lo + 0.74f + i * 0.55f, z0 + 0.2f), 0.06f, 6);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        float bx = X(h, -TownFloors.StairSide(h) * (h.Width / 2 - 0.55));
        foreach (float z in new[] { -0.4f, 0.4f })
            k.Cylinder(new Vector3(bx, lo, z), new Vector3(bx, lo + 0.85f, z), 0.32f, 10, radiusB: 0.3f);
        k.Use("coal", Palette.SootBlack, 0.8f, 0.3f, tile: 1);
        k.Tint = Vector3.One;
        k.Cylinder(new Vector3(bx, lo, z1 - 0.6f), new Vector3(bx, lo + 0.5f, z1 - 0.5f), 0.6f, 8, radiusB: 0.15f);
    }
}
