using System.Numerics;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The big cab as the crew car (ARCHITECTURE §8 note 716; the director, 14 Oct 2026: "the cab itself becomes the crew car
/// ... the captain's quarters, shared by all"): behind the footplate, the chart table with the run map on it under its lamp;
/// the gallery, a step up, so the line's seen over the heads at the controls, a thin bench along each side wall under its
/// long windows (note 905), its middle clear; and the back wall, the boiler's end, warm, with the kettle on its shelf, the cuckoo clock,
/// the rail for the crew's photos, the trophy shelf, a hook for each crewmate's cap, and the shelf kept for the gramophone.
/// All of it over the sim's own solids (<see cref="PartKind.ChartTable"/>, <see cref="PartKind.Gallery"/>,
/// <see cref="PartKind.Bench"/>); what moves (the map's train, the lamps, the bird, the caps, the kettle's steam) is the
/// scene's, at the places given here.
/// </summary>
public static partial class TrainKit
{
    /// <summary>The chart table (note 716), its top the map's; null on an engine without one.</summary>
    public static Box? ChartTable(CarShape shape) =>
        shape.Solids.Where(s => s.Part == PartKind.ChartTable).Select(s => (Box?)s.Box).FirstOrDefault();

    /// <summary>The gallery's raised floor (note 716): the tallest of its solids; null without one.</summary>
    public static Box? GalleryFloor(CarShape shape) =>
        shape.Solids.Where(s => s.Part == PartKind.Gallery).OrderByDescending(s => s.Box.Max.Y).Select(s => (Box?)s.Box).FirstOrDefault();

    /// <summary>The photo rail's frames (note 1056): two rows of two over each way out, eight in all.</summary>
    public const int PhotoFrames = 8;

    /// <summary>A rail frame's outside size (m, across and up), the print's inside it (16:9, the crew photo's shape), and the gap between frames.</summary>
    public static readonly Vector2 PhotoFrameSize = new(0.28f, 0.18f), PhotoPrintSize = new(0.248f, 0.1395f);
    public const float PhotoFrameGap = 0.03f;

    /// <summary>The photo rail's top (frame Y): just under the roof's low edge, over the doorways' heads.</summary>
    static float PhotoRailTop(CarShape shape, float doorHeight) => (float)shape.Cab!.Value.Min.Y + 0.1f + doorHeight + 0.44f;

    /// <summary>
    /// Rail frame <paramref name="index"/>'s centre on its face (the engine's frame; its print faces −Z, into the cab), in the
    /// order a night's photo is pinned (note 1056): as you face the back wall, left to right along the top row, then along
    /// the bottom; each side's pair over its way out. Even indexes are the top row.
    /// </summary>
    public static Vector3 PhotoFrame(CarShape shape, int index, float? doorHeight = null)
    {
        float top = PhotoRailTop(shape, doorHeight ?? (float)new Sim.Train.DoorwayTuning().Height);
        // Facing the back wall (+Z), your left is +X: the frames run from +X to −X, two over each doorway, either side of
        // the plate over the boiler's end (the cuckoo clock's under the rail between them).
        int row = index / 4, col = index % 4;
        float half = (float)shape.Solids.First(s => s.Part == PartKind.Boiler).Box.Max.X;
        float step = PhotoFrameSize.X + 0.04f, inner = half + 0.03f + PhotoFrameSize.X / 2;
        float x = col switch { 0 => inner + step, 1 => inner, 2 => -inner, _ => -inner - step };
        float y = top - 0.02f - PhotoFrameSize.Y / 2 - row * (PhotoFrameSize.Y + PhotoFrameGap);
        return new Vector3(x, y, BackFace(shape) - 0.025f);
    }

    /// <summary>The back wall's face into the cab (frame Z): the plate over the boiler's end.</summary>
    static float BackFace(CarShape shape) => (float)shape.Cab!.Value.Max.Z - 0.15f;

    /// <summary>How far in from the table's edge the map's sheet lies (m), and the crew lamps' rail along its front end.</summary>
    const float MapInset = 0.05f, LampRail = 0.1f;

    /// <summary>
    /// The plate's frame to the chart table's top (note 716): the map's sheet is laid out as it was on the plate over the
    /// windows (x along the night, y up the sheet, z out of it), and laid flat on the table, the night running from the
    /// back of the table to the front, the way the train goes, read from the driver's side of it (+X): the sheet's top
    /// edge away across the table. Identity on an engine with no table (the plate stays where it was).
    /// </summary>
    public static Matrix4x4 MapToCab(CarShape shape)
    {
        if (ChartTable(shape) is not { } t)
            return Matrix4x4.Identity;
        var (corner, _, _) = MapPlate(shape);
        // (1,0,0) along the sheet to −Z; (0,1,0) up the sheet to −X, away across the table; (0,0,1) out of it, up.
        var turn = new Matrix4x4(0, 0, -1, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1);
        return Matrix4x4.CreateTranslation(-corner) * turn
            * Matrix4x4.CreateTranslation((float)t.Max.X - MapInset, (float)t.Max.Y + 0.004f, (float)t.Max.Z - MapInset);
    }

    /// <summary>
    /// The chart table's lamp for crewmate <paramref name="index"/> of <paramref name="count"/> (note 716: lit, aboard; dark,
    /// off the train or dead), in the engine's frame: in a row across the rail at the table's front end, the footplate's.
    /// </summary>
    public static Vector3 CrewLamp(CarShape shape, int index, int count)
    {
        var t = ChartTable(shape) ?? default;
        float x0 = (float)t.Min.X + 0.08f, x1 = (float)t.Max.X - 0.08f;
        float x = count <= 1 ? (x0 + x1) / 2 : x0 + (x1 - x0) * index / (count - 1);
        return new Vector3(x, (float)t.Max.Y + 0.05f, (float)t.Min.Z + LampRail / 2);
    }

    /// <summary>The kettle on the back wall's shelf (note 716): its spout's tip, where it steams as the gauge climbs.</summary>
    public static Vector3 KettleSpout(CarShape shape) => KettleAt(shape) + new Vector3(0.13f, 0.17f, -0.05f);

    /// <summary>The kettle's foot on the warming shelf, left of the backhead's middle.</summary>
    static Vector3 KettleAt(CarShape shape) => new(-0.34f, (float)shape.Cab!.Value.Min.Y + 0.1f + ShelfHeight + 0.03f, BackFace(shape) - 0.16f);

    /// <summary>How high over the cab floor the back wall's warming shelf is (m): waist height, the kettle at hand.</summary>
    const float ShelfHeight = 1.05f;

    /// <summary>
    /// The shelf kept clear for U1's gramophone (PR #967's catalogue; the director: "the back wall should definitely have
    /// some fun, cute gimmicks"): the right half of the warming shelf, its middle, in the engine's frame. Nothing stands on
    /// it here; the gramophone's piece goes at this point, facing forward (−Z) into the cab.
    /// </summary>
    public static Vector3 GramophoneShelf(CarShape shape) => new(0.34f, (float)shape.Cab!.Value.Min.Y + 0.1f + ShelfHeight + 0.03f, BackFace(shape) - 0.16f);

    /// <summary>The cuckoo clock's little door (note 716), on the backhead high in the middle, facing forward into the cab.</summary>
    public static Vector3 CuckooDoor(CarShape shape) => new(0, (float)shape.Cab!.Value.Min.Y + 0.1f + 2.27f, BackFace(shape) - 0.13f);

    /// <summary>
    /// The peg crewmate <paramref name="index"/>'s cap hangs from (note 716), on the back of the cab's right side by the
    /// doorway, facing in; up to eight in a row, more doubled up under them.
    /// </summary>
    public static Vector3 CapHook(CarShape shape, int index)
    {
        var cab = shape.Cab!.Value;
        float deck = (float)cab.Min.Y + 0.1f, z0 = HooksFrom(shape), along = (float)Math.Min(0.14, (DoorFront(shape, deck, (float)cab.Max.Z) - 0.1f - z0) / 7);
        int row = index / 8, i = index % 8;
        return new Vector3((float)cab.Max.X - 0.04f, deck + 1.72f - row * 0.32f, z0 + i * along);
    }

    /// <summary>Where the hooks' row starts along the right side (frame Z): just behind the gallery's back tread.</summary>
    static float HooksFrom(CarShape shape) =>
        (float)shape.Solids.Where(s => s.Part == PartKind.Gallery).Select(s => s.Box.Max.Z).DefaultIfEmpty(shape.Cab!.Value.Max.Z - 2.2).Max() + 0.08f;

    /// <summary>
    /// The crew car inside (note 716): the table and its lamp, the gallery and its benches, the back wall and what's on it.
    /// Nothing on an engine without the crew car's solids.
    /// </summary>
    static void CrewCarInside(Kit k, CarShape shape, float w, float deck, float bw, float roofLow)
    {
        if (ChartTable(shape) is { } table)
            ChartTableArt(k, shape, F(table.Min), F(table.Max), roofLow);
        if (GalleryFloor(shape) is not null)
            GalleryArt(k, shape, w, deck);
        if (shape.Cab is { } cab && cab.Max.Z - cab.Min.Z > 8)
            BackWall(k, shape, w, deck, bw, roofLow);
    }

    /// <summary>
    /// The chart table (note 716): a heavy board on iron legs, its edge bound in brass so a chart doesn't slide off it on a
    /// bend, rolled charts on the shelf under it; at its front end the rail the crew's lamps stand on; and over it a lamp
    /// on a chain under a green shade, so the map's read and the cab round it stays dark.
    /// </summary>
    static void ChartTableArt(Kit k, CarShape shape, Vector3 min, Vector3 max, float roofLow)
    {
        float deck = min.Y, top = max.Y;
        k.Use("wood_floor", Palette.DeepBrown * 1.1f, 0.7f, 0.15f, tile: 1.2f);
        k.Box(new Vector3(min.X - 0.03f, top - 0.06f, min.Z - 0.03f), new Vector3(max.X + 0.03f, top, max.Z + 0.03f));
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side < 0 ? min.X - 0.03f : max.X + 0.03f;
            k.Rod(new Vector3(x, top + 0.012f, min.Z - 0.03f), new Vector3(x, top + 0.012f, max.Z + 0.03f), 0.012f);
            float z = side < 0 ? min.Z - 0.03f : max.Z + 0.03f;
            k.Rod(new Vector3(min.X - 0.03f, top + 0.012f, z), new Vector3(max.X + 0.03f, top + 0.012f, z), 0.012f);
        }
        // The lamps' rail across its front end: a brass strip the lamps' sockets are in.
        k.Box(new Vector3(min.X + 0.02f, top, min.Z + 0.01f), new Vector3(max.X - 0.02f, top + 0.02f, min.Z + LampRail - 0.01f));
        k.Use("iron_plate", Palette.IronGrey * 0.7f, 0.8f, 0.4f);
        foreach (float x in new[] { min.X + 0.06f, max.X - 0.06f })
            foreach (float z in new[] { min.Z + 0.08f, max.Z - 0.08f })
                k.Cylinder(new Vector3(x, deck, z), new Vector3(x, top - 0.06f, z), 0.035f, 6);
        // The shelf under it, and the charts rolled on it.
        k.Use("wood_floor", Palette.DeepBrown * 0.8f, 0.8f, 0.1f);
        k.Box(new Vector3(min.X + 0.04f, deck + 0.22f, min.Z + 0.1f), new Vector3(max.X - 0.04f, deck + 0.25f, max.Z - 0.1f));
        k.Use("paper_form", new Vector3(0.62f, 0.56f, 0.44f), 0.6f, 0.05f, tile: 0.5f);
        for (int i = 0; i < 5; i++)
        {
            float x = min.X + 0.12f + i * 0.15f, r = 0.035f + 0.008f * (i % 2);
            k.Cylinder(new Vector3(x, deck + 0.25f + r, min.Z + 0.25f + 0.1f * (i % 3)), new Vector3(x, deck + 0.25f + r, max.Z - 0.3f - 0.1f * (i % 2)), r, 8);
        }
        // The lamp over it, hung from the roof on a chain: a green enamel shade, its bulb lit (SceneArt puts its light there).
        var lamp = ChartLamp(shape);
        k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
        k.Rod(new Vector3(lamp.X, roofLow - 0.02f, lamp.Z), lamp + new Vector3(0, 0.16f, 0), 0.008f);
        k.Use("paint_olive", Palette.SignalGreen * 0.45f, 0.4f, 0.5f);
        k.Lathe(lamp + new Vector3(0, -0.02f, 0), [new(0.19f, 0), new(0.15f, 0.07f), new(0.06f, 0.14f), new(0.025f, 0.16f)], 12, smooth: true, capTop: true);
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.2f);
        k.Emissive = 1;
        k.Disc(lamp + new Vector3(0, 0.0f, 0), -Vector3.UnitY, 0.07f, 8, new Vector2(0.1f, 0.1f), 0.1f);
        k.Emissive = 0;
    }

    /// <summary>
    /// The gallery's lamp (note 716), in the engine's frame: a caged bulkhead lamp under the roof over the gallery's middle,
    /// behind the eye of whoever's up there, so it lights the benches and leaves the windows dark to see out of.
    /// </summary>
    public static Vector3? GalleryLamp(CarShape shape)
    {
        if (GalleryFloor(shape) is not { } g || shape.Solids.Where(s => s.Part == PartKind.CabRoof).Select(s => (Box?)s.Box).FirstOrDefault() is not { } roof)
            return null;
        return new Vector3(0, (float)roof.Min.Y - 0.14f, (float)(g.Min.Z + g.Max.Z) / 2 + 0.6f);
    }

    /// <summary>Where the chart table's lamp hangs (note 716), in the engine's frame: over the table's middle, a reach above it.</summary>
    public static Vector3 ChartLamp(CarShape shape)
    {
        var t = ChartTable(shape) ?? default;
        // (Up near the roof: hung lower, its shade was across the view from the gallery to the front windows.)
        return new Vector3(0, (float)t.Max.Y + 1.42f, (float)t.Centre.Z);
    }

    /// <summary>
    /// The gallery (note 716): its floor boarded like the cab's, plated at its front face and its treads' with a brass
    /// nosing on each edge (found by a boot in the dark); along each side wall a thin bench (note 905; the director, 16 Oct
    /// 2026: "side benches so that we keep the middle clear ... really thin"): one plank seat on slim iron brackets off the
    /// wall, and a single rail along the wall to sit back against, the space under the seat open.
    /// </summary>
    static void GalleryArt(Kit k, CarShape shape, float w, float deck)
    {
        if (GalleryLamp(shape) is { } lamp)
        {
            k.Use("paint_black", Palette.SootBlack, 0.7f, 0.3f);
            k.Box(lamp + new Vector3(-0.1f, 0.0f, -0.1f), lamp + new Vector3(0.1f, 0.14f, 0.1f));
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.2f);
            k.Emissive = 1;
            k.Disc(lamp - new Vector3(0, 0.005f, 0), -Vector3.UnitY, 0.08f, 8, new Vector2(0.1f, 0.1f), 0.1f);
            k.Emissive = 0;
            k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
            foreach (float a in new[] { -0.06f, 0.06f })
            {
                k.Rod(lamp + new Vector3(a, 0, -0.09f), lamp + new Vector3(a, -0.08f, 0), 0.006f);
                k.Rod(lamp + new Vector3(a, -0.08f, 0), lamp + new Vector3(a, 0, 0.09f), 0.006f);
            }
        }
        foreach (var s in shape.Solids.Where(s => s.Part == PartKind.Gallery))
        {
            var (min, max) = (F(s.Box.Min), F(s.Box.Max));
            k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0);
            k.Box(new Vector3(min.X, max.Y - 0.005f, min.Z), new Vector3(max.X, max.Y + 0.005f, max.Z), Kit.Faces.PosY);
            k.Use("iron_plate", Palette.IronGrey * 0.8f, 0.9f, 0.35f);
            k.Box(new Vector3(min.X, deck - 0.02f, min.Z), new Vector3(max.X, max.Y - 0.005f, max.Z), Kit.Faces.NegZ | Kit.Faces.PosZ);
            k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
            foreach (float z in new[] { min.Z, max.Z })
                k.Box(new Vector3(min.X, max.Y - 0.02f, z - 0.02f), new Vector3(max.X, max.Y + 0.008f, z + 0.02f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        foreach (var s in shape.Solids.Where(s => s.Part == PartKind.Bench))
        {
            var (min, max) = (F(s.Box.Min), F(s.Box.Max));
            float seat = max.Y, side = MathF.Sign(min.X + max.X);
            float wall = side > 0 ? max.X : min.X, edge = side > 0 ? min.X : max.X;
            // The seat: one plank, a thumb thick, its front edge rounded off by a thinner lip under it.
            k.Use("wood_floor", Palette.DeepBrown * 1.25f, 0.6f, 0.2f, tile: 0.8f);
            k.Box(new Vector3(MathF.Min(wall, edge), seat - 0.035f, min.Z), new Vector3(MathF.Max(wall, edge), seat, max.Z));
            k.Box(new Vector3(MathF.Min(edge, edge + side * 0.04f), seat - 0.05f, min.Z), new Vector3(MathF.Max(edge, edge + side * 0.04f), seat - 0.035f, max.Z));
            // Slim iron brackets off the wall under it, a seat's width apart: a flat bar out under the plank and a strut down
            // from its front to the wall, an open triangle; and a foot under the front at each end, on the gallery's floor.
            k.Use("iron_plate", Palette.SootBlack, 0.8f, 0.4f);
            int bays = Math.Max(1, (int)MathF.Round((max.Z - min.Z) / 0.7f));
            for (int i = 0; i <= bays; i++)
            {
                float z = min.Z + 0.05f + (max.Z - min.Z - 0.1f) * i / bays;
                k.Box(new Vector3(MathF.Min(wall, edge + side * 0.03f), seat - 0.06f, z - 0.012f), new Vector3(MathF.Max(wall, edge + side * 0.03f), seat - 0.035f, z + 0.012f));
                k.Rod(new Vector3(edge + side * 0.05f, seat - 0.06f, z), new Vector3(wall - side * 0.01f, seat - 0.3f, z), 0.012f, 5);
            }
            foreach (float z in new[] { min.Z + 0.05f, max.Z - 0.05f })
                k.Rod(new Vector3(edge + side * 0.05f, seat - 0.06f, z), new Vector3(edge + side * 0.05f, min.Y, z), 0.014f, 5);
            // The back: one rail along the wall to sit back against, on a standoff at each bracket.
            k.Use("wood_floor", Palette.DeepBrown * 1.1f, 0.6f, 0.2f, tile: 0.8f);
            float back = seat + 0.3f;
            k.Box(new Vector3(MathF.Min(wall, wall - side * 0.05f), back, min.Z), new Vector3(MathF.Max(wall, wall - side * 0.05f), back + 0.09f, max.Z));
            k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
            for (int i = 0; i <= bays; i++)
            {
                float z = min.Z + 0.05f + (max.Z - min.Z - 0.1f) * i / bays;
                k.Rod(new Vector3(wall, back + 0.045f, z), new Vector3(wall - side * 0.05f, back + 0.045f, z), 0.01f, 5);
            }
        }
    }

    /// <summary>
    /// The back wall (note 716): the plate over the boiler's end warm with it, its rivets and the feed's pipe across it, a
    /// warming shelf at waist height with the kettle on its left half (its right half kept for the gramophone,
    /// <see cref="GramophoneShelf"/>); over them the cuckoo clock, a carved house with its weights on chains; along the top
    /// of the wall, over both ways out down the boiler, the photo rail's frames (note 1056: each delivered night's crew photo
    /// pinned in the next); and on the side walls at the back, the trophy shelf on the left and the crew's hooks on the right.
    /// </summary>
    static void BackWall(Kit k, CarShape shape, float w, float deck, float bw, float roofLow)
    {
        float face = BackFace(shape);
        // The plate over the boiler's end, warmed: rust gone to a dull bronze with the heat, riveted in rows.
        k.Use("iron_smokebox", new Vector3(0.36f, 0.22f, 0.14f), 0.7f, 0.4f, tile: 0.9f);
        k.Box(new Vector3(-bw, deck, face - 0.02f), new Vector3(bw, roofLow, face), Kit.Faces.NegZ);
        k.Use("rust_heavy", Palette.RustRed * 0.8f, 0.8f, 0.3f);
        for (int row = 0; row < 5; row++)
            for (int i = 0; i < 7; i++)
            {
                float x = -bw + 0.08f + (2 * bw - 0.16f) * i / 6, y = deck + 0.2f + row * 0.6f;
                if (y < roofLow - 0.05f)
                    k.BoxAt(new Vector3(x, y, face - 0.025f), new Vector3(0.018f, 0.018f, 0.008f));
            }
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.7f, 0.5f);
        k.Cylinder(new Vector3(-bw + 0.05f, deck + 0.55f, face - 0.06f), new Vector3(bw - 0.05f, deck + 0.55f, face - 0.06f), 0.03f, 8);
        foreach (float x in new[] { -bw + 0.12f, bw - 0.12f })
            k.Cylinder(new Vector3(x, deck + 0.55f, face - 0.06f), new Vector3(x, deck + 0.05f, face - 0.06f), 0.03f, 8);

        // The warming shelf across the plate, on two brackets.
        float shelf = deck + ShelfHeight;
        k.Use("wood_floor", Palette.DeepBrown * 1.1f, 0.7f, 0.15f);
        k.Box(new Vector3(-bw + 0.04f, shelf - 0.04f, face - 0.3f), new Vector3(bw - 0.04f, shelf, face - 0.02f));
        k.Use("iron_plate", Palette.SootBlack, 0.8f, 0.4f);
        foreach (float x in new[] { -bw + 0.15f, bw - 0.15f })
            k.Quad(new Vector3(x, shelf - 0.04f, face - 0.02f), new Vector3(x, shelf - 0.04f, face - 0.26f), new Vector3(x, shelf - 0.3f, face - 0.02f), new Vector3(x, shelf - 0.3f, face - 0.02f), twoSided: true);
        // The gramophone's half: a brass plate on the shelf's edge, its place (U1's catalogue fills it).
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
        var gram = GramophoneShelf(shape);
        k.Box(new Vector3(gram.X - 0.1f, shelf - 0.035f, face - 0.305f), new Vector3(gram.X + 0.1f, shelf - 0.005f, face - 0.3f), Kit.Faces.NegZ);

        // The kettle: a squat tin kettle, sooted on its bottom, its spout to the right and its bail handle over the lid.
        var kettle = KettleAt(shape);
        k.Use("iron_plate", new Vector3(0.42f, 0.4f, 0.37f), 0.6f, 0.6f);
        k.Lathe(kettle, [new(0.1f, 0), new(0.13f, 0.04f), new(0.13f, 0.11f), new(0.09f, 0.17f), new(0.04f, 0.19f), new(0.025f, 0.21f), new(0, 0.215f)], 12, smooth: true);
        k.Cylinder(kettle + new Vector3(0.09f, 0.06f, 0), KettleSpout(shape), 0.018f, 6, radiusB: 0.01f);
        k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
        k.Rod(kettle + new Vector3(-0.08f, 0.17f, 0), kettle + new Vector3(0, 0.29f, 0), 0.008f);
        k.Rod(kettle + new Vector3(0, 0.29f, 0), kettle + new Vector3(0.08f, 0.17f, 0), 0.008f);
        k.Cylinder(kettle + new Vector3(0, 0.001f, 0), kettle + new Vector3(0, 0.02f, 0), 0.105f, 12);

        // The cuckoo clock: a little carved house, its roof pitched and its eaves out, the dial under the bird's door, and its
        // two weights hung on chains down the plate.
        var door = CuckooDoor(shape);
        float cz = face - 0.1f;
        k.Use("wood_crate", new Vector3(0.3f, 0.18f, 0.1f), 0.7f, 0.15f, tile: 0.5f);
        k.Box(new Vector3(-0.17f, door.Y - 0.36f, cz - 0.04f), new Vector3(0.17f, door.Y + 0.1f, face - 0.01f));
        k.Use("wood_crate", new Vector3(0.22f, 0.12f, 0.07f), 0.7f, 0.15f, tile: 0.5f);
        foreach (int side in new[] { -1, 1 })
            k.Quad(new Vector3(0, door.Y + 0.27f, cz - 0.07f), new Vector3(0, door.Y + 0.27f, face - 0.01f),
                new Vector3(side * 0.23f, door.Y + 0.05f, face - 0.01f), new Vector3(side * 0.23f, door.Y + 0.05f, cz - 0.07f), twoSided: true);
        k.Use("none", new Vector3(0.03f, 0.02f, 0.015f), 0, 0);
        k.Box(new Vector3(-0.045f, door.Y - 0.05f, cz - 0.045f), new Vector3(0.045f, door.Y + 0.05f, cz - 0.04f), Kit.Faces.NegZ);
        k.Use("gauge_face", Palette.BoardEnamel, 0.2f, 0.3f, tile: 1);
        k.Disc(new Vector3(0, door.Y - 0.2f, cz - 0.045f), -Vector3.UnitZ, 0.1f, 14);
        k.Use("rust_heavy", Palette.SootBlack, 0.6f, 0.4f);
        k.Rod(new Vector3(0, door.Y - 0.2f, cz - 0.05f), new Vector3(0, door.Y - 0.13f, cz - 0.05f), 0.006f);
        k.Rod(new Vector3(0, door.Y - 0.2f, cz - 0.05f), new Vector3(0.05f, door.Y - 0.2f, cz - 0.05f), 0.005f);
        k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.8f);
        foreach (float x in new[] { -0.07f, 0.07f })
        {
            float bottom = door.Y - 0.36f - 0.45f - (x > 0 ? 0.12f : 0);
            k.Rod(new Vector3(x, door.Y - 0.36f, cz + 0.01f), new Vector3(x, bottom + 0.14f, cz + 0.01f), 0.004f);
            k.Lathe(new Vector3(x, bottom, cz + 0.01f), [new(0.025f, 0), new(0.03f, 0.03f), new(0.03f, 0.11f), new(0.012f, 0.14f)], 8);
        }

        // The photo rail (note 1056): a moulded rail along the top of the back wall, and over both ways out its frames, two
        // rows of two a side, landscape for the crew photo's shape; their mounts blank till a night's photo's pinned in one
        // (the scene draws it, GreyboxScene.Memory).
        float railTop = PhotoRailTop(shape, k.DoorHeight());
        k.Use("wood_floor", Palette.DeepBrown * 1.2f, 0.7f, 0.15f);
        k.Box(new Vector3(-w + 0.1f, railTop, face - 0.04f), new Vector3(w - 0.1f, railTop + 0.04f, face));
        for (int i = 0; i < PhotoFrames; i++)
        {
            var c = PhotoFrame(shape, i, k.DoorHeight());
            float hw = PhotoFrameSize.X / 2, hh = PhotoFrameSize.Y / 2;
            k.Use("wood_crate", new Vector3(0.18f, 0.11f, 0.06f), 0.6f, 0.2f, tile: 0.4f);
            k.Box(c - new Vector3(hw, hh, 0.012f), c + new Vector3(hw, hh, 0.012f), Kit.Faces.All & ~Kit.Faces.PosZ);
            // The mount: a plain pale card, matte (the print's laid on it just in front; a printed form's texture read as one).
            k.Use("none", new Vector3(0.62f, 0.58f, 0.5f), 0.3f, 0);
            k.Box(c - new Vector3(hw - 0.012f, hh - 0.012f, 0.016f), c + new Vector3(hw - 0.012f, hh - 0.012f, -0.012f), Kit.Faces.NegZ);
            // Hung on two brass wires: the top row from the rail, the bottom row from the frame over it.
            k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.6f);
            float from = i % 2 == 0 ? railTop : c.Y + hh + PhotoFrameGap;
            k.Rod(c + new Vector3(-hw * 0.6f, hh, 0), new Vector3(c.X, from, face - 0.03f), 0.003f);
            k.Rod(c + new Vector3(hw * 0.6f, hh, 0), new Vector3(c.X, from, face - 0.03f), 0.003f);
        }

        var cab = shape.Cab!.Value;
        float doorFront = DoorFront(shape, deck, (float)cab.Max.Z), z0 = HooksFrom(shape);
        // The trophy shelf on the left wall at the back: a stout plank on iron knees, a brass plate on its edge, bare.
        float sx = (float)cab.Min.X;
        k.Use("wood_floor", Palette.DeepBrown * 1.15f, 0.7f, 0.15f);
        k.Box(new Vector3(sx, deck + 1.58f, z0), new Vector3(sx + 0.3f, deck + 1.63f, doorFront - 0.1f));
        k.Use("iron_plate", Palette.SootBlack, 0.8f, 0.4f);
        foreach (float z in new[] { z0 + 0.15f, doorFront - 0.25f })
            k.Quad(new Vector3(sx, deck + 1.58f, z), new Vector3(sx + 0.26f, deck + 1.58f, z), new Vector3(sx, deck + 1.32f, z), new Vector3(sx, deck + 1.32f, z), twoSided: true);
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
        k.Box(new Vector3(sx + 0.3f, deck + 1.585f, (z0 + doorFront) / 2 - 0.12f), new Vector3(sx + 0.305f, deck + 1.625f, (z0 + doorFront) / 2 + 0.12f), Kit.Faces.PosX);

        // The hooks on the right wall at the back: a board, and a peg for each crewmate's cap (the scene hangs the caps).
        float hx = (float)cab.Max.X;
        k.Use("wood_floor", Palette.DeepBrown * 1.2f, 0.7f, 0.15f);
        var first = CapHook(shape, 0);
        var last = CapHook(shape, 7);
        k.Box(new Vector3(hx - 0.02f, first.Y - 0.06f, first.Z - 0.08f), new Vector3(hx, first.Y + 0.06f, last.Z + 0.08f), Kit.Faces.NegX);
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
        for (int i = 0; i < 8; i++)
        {
            var peg = CapHook(shape, i);
            k.Rod(new Vector3(hx - 0.02f, peg.Y, peg.Z), new Vector3(hx - 0.09f, peg.Y + 0.03f, peg.Z), 0.008f);
        }
    }
}
