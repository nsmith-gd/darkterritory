using System.Numerics;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The stop buildings you walk into (note 387, after note 279 made them walls with doors in the sim): a yard's sheds, its
/// hero and the Holdouts' rooms, drawn as the sim stands them (<see cref="StopWalls.Shell"/>), each wall a solid box so
/// it's as thick from inside as out, its doorway open, and inside a floor and what's over it.
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>The top of a shell's stone sill and of its floor, over the kit's origin (the frame stands 0.15 m under the ground).</summary>
    const float ShellSill = 0.8f, ShellFloor = 0.15f;

    /// <summary>A point in a building's own frame (x along its axis, y across, up from the kit's origin) in the kit's: its axis on −Z, across on +X.</summary>
    static Vector3 InKit(double x, double y, float up) => new((float)y, up, (float)-x);

    /// <summary>A box in a building's own frame: (x, y) its middle, (hx, hy) half its size, from <paramref name="y0"/> to <paramref name="y1"/> up.</summary>
    static void BoxIn(Kit k, double x, double y, double hx, double hy, float y0, float y1, Kit.Faces faces = Kit.Faces.All)
    {
        var a = InKit(x - hx, y - hy, y0);
        var c = InKit(x + hx, y + hy, y1);
        k.Box(Vector3.Min(a, c), Vector3.Max(a, c), faces);
    }

    /// <summary>
    /// A yard's shed or its hero (level-design P5, P7) as the sim stands it: each of its walls on its stone sill, from the
    /// ground to the eaves; its bay doors open to the floor under a lintel and a steel header, their two leaves run back
    /// along the rail over them; a concrete floor at the ground's height; and over each roofed length its gables, its
    /// trusses and its pitched iron roof, seen from under it too. A gantry's cut between two lengths is left open.
    /// </summary>
    void ShedShell(Kit k, StopLayout stop, int index, float height, string wall, bool hoists = true)
    {
        var b = stop.Buildings[index];
        var t = _look.Walls;
        double w2 = b.Width / 2, th = t.WallM;
        float pitch = (float)b.Width * 0.3f, bay = k.DoorHeight(bay: true);
        var rooms = StopWalls.Roofed(stop, index).ToList();
        var walls = StopWalls.Shell(stop, index, t).Select(w => w.Part).ToList();
        var doors = StopWalls.Doors(stop, index, t).ToList();
        bool brick = wall == "brick_soot";

        // The floor, at the ground's height, through the doorways.
        k.Use("concrete_stain", Palette.BlueGrey, 0.9f, 0.1f, tile: 2.5f);
        foreach (var (lo, hi) in rooms)
            BoxIn(k, (lo + hi) / 2, 0, (hi - lo) / 2, w2, -0.4f, ShellFloor, Kit.Faces.All & ~Kit.Faces.NegY);
        // The walls, the sim's: stone to the sill, then the shed's boards, iron or brick to the eaves.
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        foreach (var p in walls)
            BoxIn(k, p.X, p.Y, p.Length / 2, p.Width / 2, -0.5f, ShellSill, Kit.Faces.Sides);
        k.Use(wall, brick ? Palette.RustRed : Palette.DeepBrown, 0.9f, 0.1f, tile: brick ? 1.2f : 1.5f);
        foreach (var p in walls)
            BoxIn(k, p.X, p.Y, p.Length / 2, p.Width / 2, ShellSill, height, Kit.Faces.Sides | Kit.Faces.PosY);
        // Over each doorway the wall again from the bay's height (note 110's), its soffit seen from under it.
        foreach (var d in doors)
        {
            double y = d.Side * (w2 - th / 2);
            BoxIn(k, d.At, y, d.Width / 2, th / 2, ShellFloor + bay, height, Kit.Faces.All);
        }
        // The gables, outside and in, at each roofed length's ends.
        foreach (var (lo, hi) in rooms)
            foreach (var (x, outward) in new[] { (lo, -1), (hi, 1) })
            {
                Gable(k, x, outward, w2, height, pitch);
                Gable(k, x - outward * th, -outward, w2 - th, height, pitch - (float)th * 0.6f);
            }

        // A row of high windows down each side, clear of the doors: dirty glass, a frame round each, seen from in and out.
        foreach (var (lo, hi) in rooms)
            foreach (int side in new[] { -1, 1 })
                for (double x = lo + 2.4; x <= hi - 2.4; x += 4)
                {
                    if (doors.Any(d => d.Side == side && Math.Abs(d.At - x) < d.Width / 2 + 1.4))
                        continue;
                    float mid = height - 1.5f;
                    // (The glass is see-through: the dark of the shed behind it, outside; and in, the grey of the night.)
                    var outside = new Vector3(side, 0, 0);
                    k.Use("paint_black", Palette.SootBlack, 0.95f, 0);
                    k.Shade(0.25f);
                    k.Panel(InKit(x, side * (w2 + 0.01), mid), outside, Vector3.UnitY, 1.6f, 1.2f);
                    k.Use("paint_black", Palette.BlueGrey, 0.95f, 0);
                    k.Shade(0.6f);
                    k.Panel(InKit(x, side * (w2 - th - 0.01), mid), -outside, Vector3.UnitY, 1.6f, 1.2f);
                    k.Use("glass_dirty", Palette.SootBlack, 0.3f, 0.4f, tile: 1);
                    k.Panel(InKit(x, side * (w2 + 0.02), mid), outside, Vector3.UnitY, 1.6f, 1.2f);
                    k.Panel(InKit(x, side * (w2 - th - 0.02), mid), -outside, Vector3.UnitY, 1.6f, 1.2f);
                    k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1);
                    BoxIn(k, x, side * (w2 + 0.03), 0.88, 0.04, mid - 0.68f, mid - 0.6f);
                    BoxIn(k, x, side * (w2 + 0.03), 0.88, 0.04, mid + 0.6f, mid + 0.68f);
                    BoxIn(k, x, side * (w2 + 0.04), 0.03, 0.03, mid - 0.6f, mid + 0.6f);
                }
        // The roof: two slopes of corrugated iron over each length, out over the eaves and the gables, both faces drawn.
        k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
        foreach (var (lo, hi) in rooms)
            foreach (int side in new[] { -1, 1 })
                k.Quad(InKit(lo - 0.4, side * (w2 + 0.4), height - 0.1f), InKit(hi + 0.4, side * (w2 + 0.4), height - 0.1f),
                    InKit(hi + 0.4, 0, height + pitch), InKit(lo - 0.4, 0, height + pitch), twoSided: true);
        // Its trusses, every few metres: a tie beam across at the eaves, the two rafters up under the iron, a king post.
        k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
        foreach (var (lo, hi) in rooms)
        {
            int n = Math.Max(1, (int)Math.Round((hi - lo) / 3.6));
            for (int i = 1; i < n; i++)
            {
                double x = lo + (hi - lo) * i / n;
                BoxIn(k, x, 0, 0.08, w2 - th, height - 0.35f, height - 0.15f);
                foreach (int side in new[] { -1, 1 })
                    k.Rod(InKit(x, side * (w2 - th), height - 0.15f), InKit(x, 0, height + pitch - 0.22f), 0.07f);
                k.Rod(InKit(x, 0, height - 0.15f), InKit(x, 0, height + pitch - 0.22f), 0.06f);
                // A chain hoist off every other tie beam, its hook well over a head.
                if (hoists && i % 2 == 1)
                {
                    k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.4f);
                    BoxIn(k, x, 0.6, 0.12, 0.1, height - 0.75f, height - 0.35f);
                    k.Rod(InKit(x, 0.55, height - 0.75f), InKit(x, 0.55, 3.2f), 0.012f);
                    k.Rod(InKit(x, 0.68, height - 0.75f), InKit(x, 0.68, height - 1.9f - (i % 3) * 0.4f), 0.012f);
                    k.Rod(InKit(x, 0.55, 3.2f), InKit(x, 0.55, 2.95f), 0.03f);
                    k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
                }
            }
        }

        // Each bay door's iron header, and its two leaves run back on their rail beside the opening (one gone where the
        // wall beside it is too short to take it).
        foreach (var d in doors)
        {
            var (lo, hi) = rooms.FirstOrDefault(r => d.At > r.Lo && d.At < r.Hi);
            double y = d.Side * (w2 + 0.02), leaf = d.Width / 2 + 0.15;
            k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
            BoxIn(k, d.At, d.Side * (w2 - th / 2), d.Width / 2 + 0.25, th / 2 + 0.05, ShellFloor + bay - 0.04f, ShellFloor + bay + 0.22f);
            double rail0 = d.At, rail1 = d.At;
            k.Use(brick ? "rust_heavy" : wall, brick ? Palette.IronGrey : Palette.DeepBrown, 0.9f, brick ? 0.3f : 0.1f, tile: 1.5f);
            foreach (int way in new[] { -1, 1 })
            {
                double c = d.At + way * (d.Width / 2 + leaf / 2 + 0.05);
                if (c - leaf / 2 < lo + 0.2 || c + leaf / 2 > hi - 0.2)
                    continue;
                BoxIn(k, c, y + d.Side * 0.07, leaf / 2, 0.035, ShellFloor + 0.05f, ShellFloor + bay + 0.12f);
                (rail0, rail1) = (Math.Min(rail0, c - leaf / 2), Math.Max(rail1, c + leaf / 2));
            }
            k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
            if (rail1 > rail0)
                BoxIn(k, (rail0 + rail1) / 2, y + d.Side * 0.12, (rail1 - rail0) / 2 + 0.1, 0.04, ShellFloor + bay + 0.14f, ShellFloor + bay + 0.24f);
        }
    }

    /// <summary>How high an open barn's, outbuilding's or goods shed's walls stand (m, to the eaves): what it's drawn at, and its Room (note 462).</summary>
    public static float OpenShedHeight(BuildingKind kind) => kind switch { BuildingKind.Barn => 6.5f, BuildingKind.GoodsShed => 7f, _ => 4.6f };

    /// <summary>
    /// An open barn, outbuilding or goods shed (note 417): the yard sheds' walk-in shell (no chain hoists: it's a farm's or
    /// a goods agent's, not a works'), and what its finds are kept in, where the sim keeps them (<see cref="StopWalls.ShedKept"/>):
    /// a barn's hayloft over the back of it with its ladder leant on the edge, or a workbench against the back wall with
    /// its vice, its tools and a rack over it. The bench is the sim's solid box (<see cref="StopWalls.Benches"/>).
    /// </summary>
    /// <param name="rise">How far the ground under its footprint climbs over the ground at its middle (m): its boards are laid over it.</param>
    void OpenShed(Kit k, StopLayout stop, int index, float height, string wall, float rise)
    {
        var b = stop.Buildings[index];
        ShedShell(k, stop, index, height, wall, hoists: false);
        double w2 = b.Width / 2, th = _look.Walls.WallM, l2 = b.Length / 2;
        // Boards over the shell's concrete, a step up (as an open house's are): clear of the ground, which the terrain's mesh
        // carries a little over the levelled height across a wide footprint, and where it climbs away from the line.
        float floor = ShellFloor + MathF.Max(rise, 0) + BoardsUp, up = floor - ShellFloor;
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
        BoxIn(k, 0, 0, l2 - th, w2 - th, ShellFloor, floor, Kit.Faces.All & ~Kit.Faces.NegY);
        float loftAt = up + LoftFloor;
        int door = StopWalls.ShedDoorSide(b);
        // Across the shed toward its back wall, from the inner face out: y = -door * (w2 - th - d).
        double Back(double d) => -door * (w2 - th - d);
        bool loft = false;
        foreach (var c in stop.Containers.Where(c => c.Building == index))
        {
            var (x, _, _, _) = StopWalls.ShedKept(b, c.Index);
            if (c.Kind == ContainerKind.Hayloft)
            {
                if (!loft)
                {
                    // The loft: boards over the back of the barn its whole length, on a beam along its edge and posts.
                    loft = true;
                    double mid = Back(StopWalls.LoftDepth / 2), half = StopWalls.LoftDepth / 2;
                    k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
                    BoxIn(k, 0, mid, l2 - th, half, loftAt - 0.1f, loftAt);
                    k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
                    BoxIn(k, 0, Back(StopWalls.LoftDepth - 0.08), l2 - th, 0.08, loftAt - 0.3f, loftAt - 0.1f);
                    // (Not where a ladder leans.)
                    var ladders = stop.Containers.Where(h => h.Building == index && h.Kind == ContainerKind.Hayloft).Select(h => StopWalls.ShedKept(b, h.Index).X).ToList();
                    for (double px = -l2 + th + 0.6; px <= l2 - th - 0.5; px += 3.2)
                        if (ladders.All(lx => Math.Abs(lx - px) > 0.6))
                            BoxIn(k, px, Back(StopWalls.LoftDepth - 0.08), 0.07, 0.07, floor, loftAt - 0.3f);
                    // Hay up there, heaped against the wall and spilling to the edge, and a little fallen below.
                    k.Use("ground_heath", Palette.HazardYellow * 0.55f, 1, 0, tile: 0.8f);
                    for (double hx = -l2 + th + 0.9; hx < l2 - th - 0.6; hx += 1.7)
                    {
                        float tall = 0.5f + (float)((hx * 7.3 % 1 + 1) % 1) * 0.6f;
                        BoxIn(k, hx, Back(0.55), 0.75, 0.5, loftAt, loftAt + tall);
                    }
                    BoxIn(k, x * 0.4, Back(StopWalls.LoftDepth + 0.5), 0.6, 0.35, floor, floor + 0.08f);
                }
                // The ladder, leant on the loft's edge, its foot out on the floor.
                k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
                double top = StopWalls.LoftDepth - 0.05, foot = StopWalls.LoftDepth + StopWalls.LadderLean;
                foreach (double side in new[] { -0.24, 0.24 })
                    k.Rod(InKit(x + side, Back(foot), floor), InKit(x + side, Back(top), loftAt + 0.9f), 0.035f);
                float span = loftAt + 0.9f - floor;
                for (float r = 0.3f; r < span - 0.1f; r += 0.32f)
                {
                    double d = foot + (top - foot) * (r / span);
                    k.Rod(InKit(x - 0.24, Back(d), floor + r), InKit(x + 0.24, Back(d), floor + r), 0.022f);
                }
            }
            else if (c.Kind == ContainerKind.Bench)
            {
                // The workbench (the sim's box), its top and legs, a vice at one end, tools left on it, a rack on the wall over it.
                double y = Back(StopWalls.BenchDepth), hw = StopWalls.BenchWidth, hd = StopWalls.BenchDepth;
                k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.1f);
                BoxIn(k, x, y, hw, hd, up + 0.84f, up + 0.92f);
                foreach (double lx in new[] { -hw + 0.08, hw - 0.08 })
                    foreach (double ly in new[] { -hd + 0.08, hd - 0.08 })
                        BoxIn(k, x + lx, y + ly, 0.05, 0.05, floor, up + 0.84f);
                BoxIn(k, x, y, hw - 0.1, hd - 0.1, up + 0.25f, up + 0.29f);
                k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.5f);
                BoxIn(k, x + hw - 0.18, y + door * (hd - 0.12), 0.1, 0.09, up + 0.92f, up + 1.06f);
                BoxIn(k, x - 0.3, y + door * 0.05, 0.14, 0.04, up + 0.92f, up + 0.95f);
                k.Rod(InKit(x + 0.15, y - door * 0.05, up + 0.94f), InKit(x + 0.48, y + door * 0.1, up + 0.94f), 0.012f);
                k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
                BoxIn(k, x, Back(0.03), hw, 0.03, up + 1.45f, up + 2.05f);
                k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.5f);
                for (double tx = -hw + 0.25; tx < hw - 0.1; tx += 0.32)
                    k.Rod(InKit(x + tx, Back(0.07), up + 1.95f), InKit(x + tx, Back(0.07), up + 1.55f), 0.015f);
            }
        }
    }

    /// <summary>The top of a barn's hayloft floor (m over the shed's floor): over a head, under the eaves.</summary>
    const float LoftFloor = 3.0f;

    /// <summary>How far an open barn's or shed's boards stand over its concrete where the ground's level (m): just clear of the terrain, and of a find lying on the ground.</summary>
    const float BoardsUp = 0.06f;

    /// <summary>
    /// A Holdout's room as the sim stands it (one room, the whole footprint, its door where it's broken into: note 279):
    /// walls of <paramref name="wall"/> to <paramref name="height"/>, solid either side; a lintel over the door at the
    /// standard door's height (note 110); a board floor a step up and a boarded ceiling. <paramref name="back"/>
    /// is the middle of the foot of the wall that faces the door, inside, in the kit's frame, and the way into the room from it.
    /// </summary>
    void ShelterRoom(Kit k, StopLayout stop, int index, float height, string wall, Vector3 colour, float tile, out (Vector3 At, Vector3 Into) back)
    {
        var b = stop.Buildings[index];
        var t = _look.Walls;
        double l2 = b.Length / 2, w2 = b.Width / 2, th = t.WallM;
        var door = StopWalls.Doors(stop, index, t).First();
        // Boards a step up off the ground: a signal box or water tower stands by a track, and its ballast's shoulder would
        // come up through a floor laid at the ground's height.
        const float Boards = ShellFloor + 0.25f;
        float lintel = Boards + k.DoorHeight();
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0, tile: 1.2f);
        BoxIn(k, 0, 0, l2, w2, -0.4f, Boards, Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use(wall, colour, 0.9f, 0.1f, tile: tile);
        foreach (var (p, _) in StopWalls.Shell(stop, index, t))
            BoxIn(k, p.X, p.Y, p.Length / 2, p.Width / 2, -0.3f, height, Kit.Faces.Sides | Kit.Faces.PosY);
        if (door.Side != 0)
            BoxIn(k, door.At, door.Side * (w2 - th / 2), door.Width / 2, th / 2, lintel, height, Kit.Faces.All);
        else
            BoxIn(k, door.End * (l2 - th / 2), door.At, th / 2, door.Width / 2, lintel, height, Kit.Faces.All);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
        BoxIn(k, 0, 0, l2 - th, w2 - th, height - 0.14f, height - 0.04f, Kit.Faces.NegY);
        // The wall across from the door, where whoever's in there keeps their back to.
        back = door.Side != 0
            ? (InKit(0, -door.Side * (w2 - th), Boards), new Vector3(door.Side, 0, 0))
            : (InKit(-door.End * (l2 - th), 0, Boards), new Vector3(0, 0, -door.End));
    }

    /// <summary>A lamp room's shelf on its back wall: two boards on iron brackets, hand lamps and oil cans along them.</summary>
    static void LampShelf(Kit k, (Vector3 At, Vector3 Into) back)
    {
        var along = Vector3.Cross(Vector3.UnitY, back.Into);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        foreach (float h in new[] { 0.95f, 1.6f })
        {
            var mid = back.At + back.Into * 0.17f + Vector3.UnitY * h;
            var half = along * 0.8f + back.Into * 0.15f;
            k.Box(Vector3.Min(mid - half, mid + half), Vector3.Max(mid - half, mid + half) + Vector3.UnitY * 0.04f);
        }
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        for (int i = 0; i < 5; i++)
        {
            float u = -0.65f + i * 0.32f, h = i % 2 == 0 ? 0.99f : 1.64f;
            var foot = back.At + back.Into * 0.17f + along * u + Vector3.UnitY * h;
            if (i % 3 == 1)
                // An oil can.
                k.Box(foot - along * 0.07f - back.Into * 0.05f, foot + along * 0.07f + back.Into * 0.05f + Vector3.UnitY * 0.22f);
            else
            {
                k.Cylinder(foot, foot + Vector3.UnitY * 0.08f, 0.07f, 8);
                k.Rod(foot + Vector3.UnitY * 0.2f, foot + Vector3.UnitY * 0.3f, 0.012f);
                k.Use("glass_dirty", Palette.SootBlack, 0.3f, 0.6f, tile: 0.25f);
                k.Cylinder(foot + Vector3.UnitY * 0.08f, foot + Vector3.UnitY * 0.2f, 0.05f, 8);
                k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
            }
        }
    }

    /// <summary>A water tower's pump in its pump house: the iron barrel, its flywheel and the main up through the roof to the tank.</summary>
    static void Pump(Kit k)
    {
        k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.4f);
        var at = new Vector3(0.5f, ShellFloor + 0.25f, 0.4f);
        k.Box(at + new Vector3(-0.35f, 0, -0.35f), at + new Vector3(0.35f, 0.25f, 0.35f));
        k.Cylinder(at + new Vector3(0, 0.25f, 0), at + new Vector3(0, 1.1f, 0), 0.24f, 10);
        k.Cylinder(at + new Vector3(-0.42f, 0.75f, 0), at + new Vector3(-0.36f, 0.75f, 0), 0.5f, 14);
        k.Rod(at + new Vector3(-0.42f, 0.75f, 0), at + new Vector3(0, 0.75f, 0), 0.04f);
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.8f, 0.4f);
        k.Cylinder(at + new Vector3(0, 1.1f, 0), at + new Vector3(0, 6.1f, 0), 0.09f, 8);
    }

    /// <summary>
    /// A signal box's operating floor over its locking room (the shelter, <see cref="ShelterRoom"/>): weatherboarded, its
    /// windows all round in a dirty band, a hipped slate roof and a stove pipe; the stair up its side away from the door.
    /// </summary>
    static void SignalCabin(Kit k, StopBuilding b, Vector3 facing)
    {
        float l2 = (float)b.Length / 2, w2 = (float)b.Width / 2;
        const float Floor = 2.6f, Sill = 3.35f, Head = 4.5f, Eaves = 4.85f;
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1.4f);
        k.Box(new Vector3(-w2 - 0.08f, Floor - 0.01f, -l2 - 0.08f), new Vector3(w2 + 0.08f, Eaves, l2 + 0.08f), Kit.Faces.Sides | Kit.Faces.NegY);
        // The windows, all round: dark panes with a glint on them (nobody's up there), its bars, the box's weatherboard
        // over and under them. (Not the library's dirty glass: see-through, its film catches a dawn sky white.)
        k.Use("paint_black", Palette.SootBlack, 0.2f, 0.55f);
        k.Shade(0.45f);
        foreach (int s in new[] { -1, 1 })
        {
            k.Panel(new Vector3(s * (w2 + 0.1f), (Sill + Head) / 2, 0), new Vector3(s, 0, 0), Vector3.UnitY, 2 * l2 - 0.4f, Head - Sill);
            k.Panel(new Vector3(0, (Sill + Head) / 2, s * (l2 + 0.1f)), new Vector3(0, 0, s), Vector3.UnitY, 2 * w2 - 0.4f, Head - Sill);
        }
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1.4f);
        foreach (int s in new[] { -1, 1 })
        {
            for (float z = -l2 + 0.2f; z <= l2 - 0.19f; z += (2 * l2 - 0.4f) / 6)
                k.Box(new Vector3(s * (w2 + 0.1f) - 0.03f, Sill, z - 0.03f), new Vector3(s * (w2 + 0.1f) + 0.03f, Head, z + 0.03f));
            for (float bx = -w2 + 0.2f; bx <= w2 - 0.19f; bx += (2 * w2 - 0.4f) / 4)
                k.Box(new Vector3(bx - 0.03f, Sill, s * (l2 + 0.1f) - 0.03f), new Vector3(bx + 0.03f, Head, s * (l2 + 0.1f) + 0.03f));
        }
        // The hipped roof: its ridge along the box's length, slate both faces.
        float ex = w2 + 0.35f, ez = l2 + 0.35f, ridge = Math.Max(0.1f, ez - ex), rise = ex * 0.6f;
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        foreach (int s in new[] { -1, 1 })
        {
            k.Quad(new Vector3(0, Eaves + rise, -ridge), new Vector3(0, Eaves + rise, ridge), new Vector3(s * ex, Eaves, ez), new Vector3(s * ex, Eaves, -ez), twoSided: true);
            var (a, c, apex) = (new Vector3(-ex, Eaves, s * ez), new Vector3(ex, Eaves, s * ez), new Vector3(0, Eaves + rise, s * ridge));
            k.Tri(a, c, apex);
            k.Tri(c, a, apex);
        }
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        k.Cylinder(new Vector3(w2 * 0.5f, Eaves, l2 * 0.4f), new Vector3(w2 * 0.5f, Eaves + rise + 0.9f, l2 * 0.4f), 0.08f, 8);
        // The stair: up the long side away from the door (or either, when the door's on an end), to a landing at its door.
        int side = MathF.Abs(facing.X) > 0.5f ? -(int)MathF.Sign(facing.X) : 1;
        float x = side * (w2 + 0.55f);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        const int Steps = 10;
        for (int i = 0; i < Steps; i++)
        {
            float z = -l2 + 0.6f + i * (2 * l2 - 2.0f) / Steps, y = (i + 1) * Floor / (Steps + 1);
            k.Box(new Vector3(x - 0.42f, y - 0.04f, z - 0.15f), new Vector3(x + 0.42f, y, z + 0.15f));
        }
        k.Box(new Vector3(x - 0.45f, Floor - 0.06f, l2 - 1.3f), new Vector3(x + 0.45f, Floor, l2 + 0.1f));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        float out_ = x + side * 0.42f;
        k.Rod(new Vector3(out_, 1.0f, -l2 + 0.6f), new Vector3(out_, Floor + 1.0f, l2 - 1.3f), 0.025f);
        k.Rod(new Vector3(out_, Floor, l2 - 1.3f), new Vector3(out_, Floor + 1.0f, l2 - 1.3f), 0.025f);
        k.Rod(new Vector3(out_, 0, -l2 + 0.6f), new Vector3(out_, 1.0f, -l2 + 0.6f), 0.025f);
        // Its door off the landing, shut.
        k.Use("paint_oxide", Palette.BlueGrey * 0.7f, 0.95f, 0.2f, tile: 1.5f);
        k.Panel(new Vector3(side * (w2 + 0.11f), Floor + k.DoorHeight() / 2, l2 - 0.75f), new Vector3(side, 0, 0), Vector3.UnitY, 0.8f, k.DoorHeight());
    }

    /// <summary>
    /// A gable: the triangle of wall over a roofed length's end at <paramref name="x"/>, from the eaves to the ridge, facing
    /// along its axis the way <paramref name="outward"/> says (−1 back past its start, +1 on past its end).
    /// </summary>
    static void Gable(Kit k, double x, int outward, double halfWidth, float eaves, float pitch)
    {
        var a = InKit(x, -halfWidth, eaves);
        var c = InKit(x, halfWidth, eaves);
        var apex = InKit(x, 0, eaves + pitch);
        // (a, c, apex) faces the kit's +Z, the building's −x.
        if (outward < 0)
            k.Tri(a, c, apex);
        else
            k.Tri(c, a, apex);
    }
}
