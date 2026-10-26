using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The houses with floors (queue #710, ARCHITECTURE §8 note 1005), drawn from the sim's own shell (<see cref="StopWalls.Shell"/>),
/// so what's drawn is what's walked into: a raised house's stone plinth and the steps up to its doors, its walls (stone below
/// its ground floor's boards, plaster over), the boards with the hatch let into them and its lid thrown back, the ladder
/// down it, the basement's flags, its two cellar windows just over the ground outside with crates stacked under each, the
/// flight up along the side wall, the upper floor and its rails, its boarded windows on each floor, and the roof over all.
/// </summary>
public static partial class TownKit
{
    /// <summary>How far under the ground at its middle a house's frame stands (WorldArt.Building), so heights over the ground are this over the frame.</summary>
    public const float FrameUnder = 0.15f;

    /// <summary>The boards' height over a one-floor house's frame (<see cref="OpenHouse"/>'s floor).</summary>
    const float Boards = 0.17f;

    /// <summary>
    /// How far up from a one-floor house's boards a floor of a house with floors is (note 1005): what's kept on it, the ransack
    /// and the light are drawn this much higher (0 for the ground floor of a house that isn't raised).
    /// </summary>
    public static float LevelLift(StopBuilding b, int level) =>
        StopWalls.Storeyed(b) && (level != 0 || b.Basement) ? (float)StopWalls.FloorAt(b, level) + FrameUnder - Boards
        // (A house of parts' other floors, note 1022: its ground floor's a one-floor house's.)
        : StopWalls.PartFloored(b) && level != 0 ? (float)StopWalls.PartFloorAt(level) + FrameUnder - Boards : 0;

    /// <summary>A house of parts' floored part's eaves over its frame (note 1022): over its upstairs, as a plain house's.</summary>
    public static float PartEaves => (float)StopWalls.Storey + FrameUnder + 2.85f;

    /// <summary>A house with floors' eaves over its frame: a one-floor house's wall's height over its top floor.</summary>
    public static float Eaves(StopBuilding b) => (float)StopWalls.FloorAt(b, b.Upstairs ? 1 : 0) + FrameUnder + 2.85f;

    /// <param name="box">A box in the house's frame: middle, half sizes, bottom and top over the frame.</param>
    static void StoreyedShell(Kit k, StopBuilding b, Action<double, double, double, double, float, float> box)
    {
        static Vector3 K(double x, double y, float h) => new((float)y, h, (float)-x);
        float Up(double over) => (float)over + FrameUnder;
        double t = StopWalls.WallThickness, f0 = StopWalls.FloorAt(b, 0), under = f0 - StopWalls.Slab;
        float eaves = Eaves(b);
        // Not raised, the boards are a one-floor house's (the ground's the sim's floor): a box down into the ground.
        if (!b.Basement)
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
            box(0, 0, b.Length / 2 - t, b.Width / 2 - t, -0.4f, Boards);
        }
        foreach (var p in StopWalls.Shell(b, b.Basement ? StopWalls.FloorAt(b, -1) - 0.3 : -0.4 - FrameUnder, eaves - FrameUnder))
        {
            switch (p.Part)
            {
                case ShellPart.Wall or ShellPart.Plinth or ShellPart.Lintel:
                    // Stone up to the boards (the plinth outside, the basement's walls in), plaster over.
                    if (p.Bottom < under)
                    {
                        k.Use("stone_block", Palette.BlueGrey * 0.8f, 0.95f, 0.02f, tile: 1.5f);
                        box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(Math.Min(p.Top, under)));
                    }
                    if (p.Top > under)
                    {
                        k.Use("plaster_ruin", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
                        box(p.X, p.Y, p.HalfX, p.HalfY, Up(Math.Max(p.Bottom, under)), Up(p.Top));
                    }
                    break;
                case ShellPart.Cheek:
                    k.Use("stone_block", Palette.BlueGrey * 0.7f, 0.95f, 0.02f, tile: 1);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(-0.3), Up(p.Top));
                    break;
                case ShellPart.Step:
                    k.Use("stone_block", Palette.BlueGrey * 0.75f, 0.95f, 0.02f, tile: 1);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(Math.Max(p.Bottom, p.Top - 0.6)), Up(p.Top));
                    break;
                case ShellPart.Cellar:
                    // The basement's floor: old flags, damp.
                    k.Use("concrete_stain", Palette.MuddyOlive * 0.6f, 0.95f, 0.05f, tile: 2);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(p.Top));
                    break;
                case ShellPart.Floor or ShellPart.Upper:
                    k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0.02f, tile: 1.5f);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(p.Top));
                    break;
                case ShellPart.Tread:
                    // A stair's tread and its riser, the space under the flight boxed in with boards.
                    k.Use("wood_grey", Stained, 0.85f, 0.04f, tile: 1);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Top - 0.05), Up(p.Top));
                    k.Use("wood_grey", Stained * 0.7f, 0.9f, 0.02f, tile: 1);
                    box(p.X, p.Y, p.HalfX - 0.01, p.HalfY - 0.01, Up(p.Bottom), Up(p.Top - 0.05));
                    break;
                case ShellPart.Rail:
                    k.Use("wood_grey", Stained, 0.8f, 0.05f, tile: 1);
                    // A handrail on balusters.
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Top - 0.06), Up(p.Top));
                    bool alongX = p.HalfX > p.HalfY;
                    double run = alongX ? p.HalfX : p.HalfY;
                    for (double a = -run + 0.05; a <= run - 0.04; a += 0.3)
                        box(alongX ? p.X + a : p.X, alongX ? p.Y : p.Y + a, 0.02, 0.02, Up(p.Top - StopWalls.RailHeight - StopWalls.Slab), Up(p.Top - 0.06));
                    break;
            }
        }

        // The hatch: an oak frame round the hole, its lid thrown back against the boards, the ladder down.
        if (b.Basement)
        {
            var (hx, hy, inX, inY) = StopWalls.Hatch(b);
            double h = StopWalls.HatchSize / 2;
            k.Use("wood_grey", Oak, 0.85f, 0.05f, tile: 1);
            foreach (int s in new[] { -1, 1 })
            {
                box(hx + s * (h + 0.04), hy, 0.04, h + 0.08, Up(f0 - 0.01), Up(f0 + 0.02));
                box(hx, hy + s * (h + 0.04), h, 0.04, Up(f0 - 0.01), Up(f0 + 0.02));
            }
            // The lid, flat on the boards on the hole's far side from the room.
            k.Use("wood_grey", Deal, 0.9f, 0.03f, tile: 1);
            box(hx - inX * (2 * h + 0.1), hy - inY * (2 * h + 0.1), inX != 0 ? h : h, h, Up(f0), Up(f0 + 0.05));
            foreach (var (x, y, foot, top, lx, ly, _) in StopWalls.StoreyedLadders(b))
            {
                // Each ladder: the hatch's two stiles and rungs; under a cellar window, a stack of crates.
                bool hatch = top > f0 - 0.1;
                if (hatch)
                {
                    k.Use("wood_grey", Deal * 0.85f, 0.9f, 0.03f, tile: 1);
                    var (ax, ay) = (ly, -lx);
                    foreach (int s in new[] { -1, 1 })
                        box(x + ax * s * 0.22, y + ay * s * 0.22, 0.03, 0.03, Up(foot), Up(top + 0.5));
                    for (double r = foot + 0.3; r < top + 0.3; r += 0.3)
                        k.Rod(K(x - ax * 0.22, y - ay * 0.22, Up(r)), K(x + ax * 0.22, y + ay * 0.22, Up(r)), 0.018f);
                }
                else
                {
                    k.Use("wood_crate", Deal, 0.9f, 0.03f, tile: 0.6f);
                    double height = top - foot, each = height / 3;
                    for (int c = 0; c < 3; c++)
                    {
                        double w = 0.32 - c * 0.04, out_ = 0.05 + c * 0.04;
                        // Inward from the wall (lx, ly is out through the window): stacked a little back each one up.
                        box(x - lx * (out_ + 0.1), y - ly * (out_ + 0.1), lx != 0 ? 0.28 : w, ly != 0 ? 0.28 : w, Up(foot + c * each), Up(foot + (c + 1) * each - 0.02));
                    }
                }
            }
            // The cellar windows: a frame round each, sill to head, and nothing in it: you see the ground outside.
            k.Use("wood_grey", Palette.DeepBrown * 0.8f, 0.9f, 0.03f, tile: 1);
            foreach (var (x, y, ox, oy) in StopWalls.CellarWindows(b))
            {
                double half = StopWalls.CellarWindow / 2, across = y - oy * 0.02 + oy * t / 2;
                box(x, across, half + 0.05, t / 2 + 0.03, Up(StopWalls.WindowSill - 0.05), Up(StopWalls.WindowSill));
                box(x, across, half + 0.05, t / 2 + 0.03, Up(StopWalls.WindowHead), Up(StopWalls.WindowHead + 0.05));
                foreach (int s in new[] { -1, 1 })
                    box(x + s * (half + 0.025), across, 0.025, t / 2 + 0.03, Up(StopWalls.WindowSill), Up(StopWalls.WindowHead));
            }
        }

        // Windows on each floor (none in the back door's end on the ground floor), and the roof over all: the ground floor's
        // working ones (note 1053) round their openings in the shell, as high as the sim has them over its boards.
        int back = StopWalls.BackEnd(b);
        foreach (int level in b.Upstairs ? new[] { 0, 1 } : [0])
        {
            float lift = level == 0 ? (float)StopWalls.LevelAt(b, 0) : LevelLift(b, level);
            BoardedWindows(k, b, (x, y, wx, wy, y0, y1) =>
            {
                if (level == 0 && Math.Abs(x - back * b.Length / 2) < 0.4)
                    return;
                box(x, y, wx, wy, y0 + lift, y1 + lift);
            }, working: level == 0);
        }
        Gabled(k, 0, 0, (float)b.Width, (float)b.Length, true, eaves);
    }

    /// <summary>
    /// A house of parts' floors (note 1022), from the sim's own boxes (<see cref="StopWalls.PartShell"/>): the boards with the
    /// hatch let in and its frame and lid, the cellar's floor and stone walls, the flight, the upper floor and its ceiling, the
    /// rails, the walls upstairs along its seams, and the ladder down each hatch.
    /// </summary>
    /// <param name="box">A box in the house's frame: middle, half sizes, bottom and top over the frame.</param>
    static void PartShell(Kit k, StopBuilding b, Action<double, double, double, double, float, float> box)
    {
        static Vector3 K(double x, double y, float h) => new((float)y, h, (float)-x);
        float Up(double over) => (float)over + FrameUnder;
        foreach (var p in StopWalls.PartShell(b, PartEaves - FrameUnder))
        {
            switch (p.Part)
            {
                case ShellPart.Wall or ShellPart.Plinth:
                    if (p.Top < 0)
                    {
                        k.Use("stone_block", Palette.BlueGrey * 0.7f, 0.95f, 0.02f, tile: 1.5f);
                        box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(p.Top));
                    }
                    else
                    {
                        k.Use("plaster_ruin", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
                        box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(p.Top));
                    }
                    break;
                case ShellPart.Lintel:
                    k.Use("plaster_ruin", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(p.Top));
                    break;
                case ShellPart.Cellar:
                    k.Use("concrete_stain", Palette.MuddyOlive * 0.6f, 0.95f, 0.05f, tile: 2);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(p.Top));
                    break;
                case ShellPart.Floor:
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Boards);
                    break;
                case ShellPart.Upper:
                    k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0.02f, tile: 1.5f);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Bottom), Up(p.Top));
                    break;
                case ShellPart.Tread:
                    k.Use("wood_grey", Stained, 0.85f, 0.04f, tile: 1);
                    box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Top - 0.05), Up(p.Top));
                    k.Use("wood_grey", Stained * 0.7f, 0.9f, 0.02f, tile: 1);
                    box(p.X, p.Y, p.HalfX - 0.01, p.HalfY - 0.01, Up(p.Bottom), Up(p.Top - 0.05));
                    break;
                case ShellPart.Rail:
                    {
                        k.Use("wood_grey", Stained, 0.8f, 0.05f, tile: 1);
                        box(p.X, p.Y, p.HalfX, p.HalfY, Up(p.Top - 0.06), Up(p.Top));
                        bool alongX = p.HalfX > p.HalfY;
                        double run = alongX ? p.HalfX : p.HalfY;
                        for (double a = -run + 0.05; a <= run - 0.04; a += 0.3)
                            box(alongX ? p.X + a : p.X, alongX ? p.Y : p.Y + a, 0.02, 0.02, Up(p.Top - StopWalls.RailHeight - StopWalls.Slab), Up(p.Top - 0.06));
                        break;
                    }
            }
        }
        // Each hatch: its frame, its lid on the boards past it, and the ladder down.
        foreach (int part in StopWalls.FlooredParts(b))
        {
            if (!b.Basement || StopWalls.PartHatch(b, part) is not { } h)
                continue;
            double hs = StopWalls.HatchSize / 2;
            k.Use("wood_grey", Oak, 0.85f, 0.05f, tile: 1);
            foreach (int s in new[] { -1, 1 })
            {
                box(h.X + s * (hs + 0.04), h.Y, 0.04, hs + 0.08, Boards - 0.01f, Boards + 0.02f);
                box(h.X, h.Y + s * (hs + 0.04), hs, 0.04, Boards - 0.01f, Boards + 0.02f);
            }
            k.Use("wood_grey", Deal, 0.9f, 0.03f, tile: 1);
            box(h.X - h.InX * (2 * hs + 0.1), h.Y - h.InY * (2 * hs + 0.1), hs, hs, Boards, Boards + 0.05f);
        }
        foreach (var (x, y, foot, top, lx, ly, _) in StopWalls.PartLadders(b))
        {
            k.Use("wood_grey", Deal * 0.85f, 0.9f, 0.03f, tile: 1);
            var (ax, ay) = (ly, -lx);
            foreach (int s in new[] { -1, 1 })
                box(x + ax * s * 0.22, y + ay * s * 0.22, 0.03, 0.03, Up(foot), Up(top + 0.5));
            for (double r = foot + 0.3; r < top + 0.3; r += 0.3)
                k.Rod(K(x - ax * 0.22, y - ay * 0.22, Up(r)), K(x + ax * 0.22, y + ay * 0.22, Up(r)), 0.018f);
        }
    }

    /// <summary>
    /// A basement's find's chest (note 1005): an iron-bound deal box on the flags, its lid shut; its face toward the room. In the
    /// kit's frame: x across its face, y up, z out the way it faces.
    /// </summary>
    public static MeshAsset CellarChest(Look? look, bool open)
    {
        var k = new Kit(look, 67);
        const float W = 0.4f, D = 0.28f, H = 0.42f;
        k.Use("wood_crate", Deal * 0.8f, 0.9f, 0.03f, tile: 0.6f);
        k.Box(new Vector3(-W, 0, -D), new Vector3(W, H, D), top: open);
        if (open)
        {
            // Its lid up and back, its inside dark.
            k.Use("wood_grey", Inside, 0.95f, 0, tile: 1);
            k.Box(new Vector3(-W + 0.02f, 0.05f, -D + 0.02f), new Vector3(W - 0.02f, H - 0.01f, D - 0.02f));
            k.Use("wood_crate", Deal * 0.8f, 0.9f, 0.03f, tile: 0.6f);
            k.Box(new Vector3(-W, H, -D - 0.04f), new Vector3(W, H + 2 * D, -D));
        }
        else
            k.Box(new Vector3(-W - 0.01f, H, -D - 0.01f), new Vector3(W + 0.01f, H + 0.05f, D + 0.01f));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f, tile: 1);
        foreach (float x in new[] { -W + 0.08f, W - 0.08f })
            k.Box(new Vector3(x - 0.02f, 0, -D - 0.008f), new Vector3(x + 0.02f, H, D + 0.008f), top: false);
        return k.Build(open ? "cellar-chest-open" : "cellar-chest");
    }
}
