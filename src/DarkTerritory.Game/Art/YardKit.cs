using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A walled town's rail yard behind the home platform (queue #239, note 502; GDD §3: "Fortified towns survive behind stone and
/// steel walls ... rail yards and warehouses"). The goods shed is the facility's modelled piece (tools/models facility_pieces
/// goods_shed, C1's #521: its dock, canopy, doors, hand crane and office), stood as built on its fixture's footprint
/// (<see cref="Shed"/>, held by TownYardArtTests); the rest are this kit's: the yard office, the weighbridge's house and its
/// plate, freight stacked waiting, a handcart, old sleepers stacked to dry, the stock left standing in the siding (a van, a
/// flat wagon with a dory or with rails), the buffer stops, and the siding itself. Each in its fixture's frame, its front
/// toward −Z: the pieces' to the siding, the stock's and the buffer stops' along it.
/// </summary>
public static class YardKit
{
    /// <summary>The yard's kinds this kit draws (the lamps are the sourced lamp post, WorldArt.Yard).</summary>
    public static bool Draws(string kind) => kind is "goodsshed" or "yardoffice" or "weighbridge" or "freight" or "cart" or "sleepers"
        or "van" or "flatdory" or "flatrails" or "buffers" or "yardlamp";

    /// <summary>The goods shed's model: built with its dock to +Z (StructureKit's switchyard turns it to the line), so turned a half.</summary>
    public const string Shed = "goods_shed";

    /// <summary>
    /// A modelled piece's ground footprint in its own space: the middle and extent of what stands below
    /// <paramref name="below"/> m (a goods shed's walls, office and dock; not its crane's jib or its canopy out over the siding).
    /// </summary>
    public static (Vector3 Middle, Vector3 Size) GroundFootprint(MeshAsset mesh, float below = 1.3f)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
            if (v.Position.Y < below)
            {
                lo = Vector3.Min(lo, v.Position);
                hi = Vector3.Max(hi, v.Position);
            }
        return (new Vector3((lo.X + hi.X) / 2, 0, (lo.Z + hi.Z) / 2), hi - lo);
    }

    /// <summary>
    /// Where a piece's lights burn, in its own frame: the goods shed's office window and a lamp left burning inside its open
    /// door; the yard office's window and the lamp over its door; the weigh house's window toward its plate.
    /// </summary>
    public static IEnumerable<(Vector3 At, Vector3 Colour, float Range)> Lights(string kind, int side)
    {
        var (hs, hd, _) = Sim.Towns.TownFixtures.Size(kind);
        float x = (float)hs, z = (float)hd;
        switch (kind)
        {
            case "goodsshed":
                yield return (new Vector3(x - 1.8f, 2.8f, -z + 2.6f), Palette.LampAmber * 1.1f, 8);
                yield return (new Vector3(7.2f, 2.4f, -z + 3.4f), Palette.LampAmber * 0.6f, 5);
                break;
            case "yardoffice":
                yield return (new Vector3(-x * 0.4f, 1.8f, -z - 0.8f), Palette.LampAmber * 0.9f, 6);
                yield return (new Vector3(x * 0.45f, 2.7f, -z - 0.4f), Palette.LampAmber * 1.4f, 9);
                break;
            case "weighbridge":
                yield return (new Vector3(-side * (x + 0.8f), 1.7f, 0), Palette.LampAmber * 0.8f, 6);
                break;
        }
    }

    /// <summary>A piece the kit makes, the size its fixture is (<paramref name="side"/>: the yard's, which way is the yard's rear end).</summary>
    public static MeshAsset Piece(Look? look, string kind, int variant, int side)
    {
        var k = new Kit(look, 5300 + kind.Sum(c => c) % 97 + variant);
        var (hs, hd, ht) = Sim.Towns.TownFixtures.Size(kind);
        float x = (float)hs, z = (float)hd, h = (float)ht;
        switch (kind)
        {
            case "goodsshed":
                // Where the goods shed isn't built (an edition without the props): a timber shed its size, its dock to the siding.
                k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
                k.Box(new Vector3(-x, -0.3f, -z + 2.6f), new Vector3(x, h - 2.5f, z), Kit.Faces.All & ~Kit.Faces.NegY);
                k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                k.Box(new Vector3(-x, -0.3f, -z), new Vector3(x, 1.1f, -z + 2.6f), Kit.Faces.All & ~Kit.Faces.NegY);
                break;
            case "yardoffice":
                Office(k, x, z, h);
                break;
            case "weighbridge":
                Weighbridge(k, x, z, h, side);
                break;
            case "freight":
                Freight(k, look, x, z, variant);
                break;
            case "cart":
                Cart(k, look, x, z);
                break;
            case "sleepers":
                Sleepers(k, x, z, h);
                break;
            case "van":
                Van(k, x, z, h, variant);
                break;
            case "flatdory":
            case "flatrails":
                Flat(k, x, z, kind == "flatdory", variant);
                break;
            case "buffers":
                Buffers(k, x, z, h);
                break;
        }
        return k.Build($"yard-{kind}-{variant}-{side}");
    }

    /// <summary>
    /// A piece of the siding, <paramref name="length"/> long down −Z: its ballast, sleepers and two rails rusted the colour of
    /// the ground (nothing has run on them since its points were lifted).
    /// </summary>
    public static MeshAsset Siding(Look? look, float length)
    {
        var k = new Kit(look, 5391);
        float half = length / 2;
        k.Use("ballast", Palette.Ballast, 0.9f, 0.05f, tile: 1.5f);
        k.Shade(0.8f);
        k.Box(new Vector3(-1.7f, -0.2f, -half), new Vector3(1.7f, 0.04f, half), Kit.Faces.PosY | Kit.Faces.PosX | Kit.Faces.NegX);
        k.Shade(1);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
        // Evenly down it, so piece meets piece at the same spacing.
        int sleepers = Math.Max(1, (int)MathF.Round(length / 0.76f));
        for (int i = 0; i < sleepers; i++)
        {
            float zz = -half + (i + 0.5f) * length / sleepers;
            k.Box(new Vector3(-1.3f, 0, zz - 0.12f), new Vector3(1.3f, 0.12f, zz + 0.12f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.3f);
        foreach (int s in new[] { -1, 1 })
        {
            k.Box(new Vector3(s * TrainKit.HalfGauge - 0.035f, 0.12f, -half), new Vector3(s * TrainKit.HalfGauge + 0.035f, 0.26f, half), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Box(new Vector3(s * TrainKit.HalfGauge - 0.07f, 0.12f, -half), new Vector3(s * TrainKit.HalfGauge + 0.07f, 0.15f, half), Kit.Faces.PosY | Kit.Faces.PosX | Kit.Faces.NegX);
        }
        return k.Build($"yard-siding-{length:0.0}");
    }

    /// <summary>
    /// The yard office: a weatherboarded hut, its tin roof pitched along it, the clerk's window lit to the siding and the
    /// lamp over his door; a stovepipe up its back corner (<see cref="OfficePipe"/>), the timetable pinned by the door.
    /// </summary>
    static void Office(Kit k, float x, float z, float h)
    {
        float eave = h - 1.1f;
        k.Use("wood_siding", Palette.BlueGrey, 0.85f, 0.05f, tile: 1);
        k.Box(new Vector3(-x, -0.3f, -z), new Vector3(x, eave, z), Kit.Faces.All & ~Kit.Faces.NegY);
        foreach (float sx in new[] { -1f, 1f })
        {
            k.Tri(new Vector3(sx * x, eave, -z), new Vector3(sx * x, eave, z), new Vector3(sx * x, h, 0));
            k.Tri(new Vector3(sx * x, eave, z), new Vector3(sx * x, eave, -z), new Vector3(sx * x, h, 0));
        }
        k.Use("corrugated_iron", Palette.IronGrey, 0.8f, 0.35f, tile: 1.5f);
        foreach (float sz in new[] { -1f, 1f })
            k.Quad(new Vector3(-x - 0.3f, eave - 0.15f, sz * (z + 0.35f)), new Vector3(x + 0.3f, eave - 0.15f, sz * (z + 0.35f)),
                new Vector3(x + 0.3f, h + 0.08f, 0), new Vector3(-x - 0.3f, h + 0.08f, 0), twoSided: true);
        // The door, the window lit, its sill; the timetable by the door.
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(x * 0.45f - 0.5f, 0, -z - 0.06f), new Vector3(x * 0.45f + 0.5f, 2.05f, -z + 0.02f));
        k.Use("window_lit", Palette.LampAmber, 0.3f, 0.2f, tile: 1);
        k.Emissive = 1.1f;
        k.Panel(new Vector3(-x * 0.4f, 1.6f, -z - 0.02f), -Vector3.UnitZ, Vector3.UnitY, 1.3f, 0.9f);
        k.Emissive = 0;
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-x * 0.4f - 0.75f, 1.08f, -z - 0.14f), new Vector3(-x * 0.4f + 0.75f, 1.14f, -z));
        k.Use("paper_form", Palette.BoardEnamel, 0.7f, 0, tile: 1);
        k.Box(new Vector3(x * 0.45f + 0.65f, 1.2f, -z - 0.03f), new Vector3(x * 0.45f + 1.15f, 1.85f, -z - 0.01f));
        k.Use("lantern_pane", Palette.LampAmber, 0.2f, 0.3f, tile: 1);
        k.Emissive = 1.4f;
        k.BoxAt(new Vector3(x * 0.45f, 2.45f, -z - 0.2f), new Vector3(0.1f, 0.13f, 0.09f));
        k.Emissive = 0;
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f, tile: 1);
        var pipe = OfficePipe((float)Sim.Towns.TownFixtures.Size("yardoffice").HalfS, z, h);
        k.Cylinder(new Vector3(pipe.X, eave - 0.6f, pipe.Z), pipe, 0.09f, 8);
    }

    /// <summary>The top of the yard office's stovepipe in its own frame (its back corner), where its smoke rises.</summary>
    public static Vector3 OfficePipe(float x, float z, float h) => new(-x + 0.5f, h + 0.25f, z - 0.1f);

    /// <summary>
    /// The weighbridge: a brick weigh house with a slate roof and its window to the plate, and the plate itself beside it
    /// toward the yard's rear end, flush with the ground in a stone kerb, the cart road from the street over it.
    /// </summary>
    static void Weighbridge(Kit k, float x, float z, float h, int side)
    {
        float eave = h - 0.8f;
        k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
        k.Box(new Vector3(-x, -0.3f, -z), new Vector3(x, eave, z), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("roof_slate", Palette.Charcoal, 0.8f, 0.1f, tile: 1);
        var top = new Vector3(0, h, 0);
        var c = new[] { new Vector3(-x - 0.25f, eave, -z - 0.25f), new Vector3(x + 0.25f, eave, -z - 0.25f), new Vector3(x + 0.25f, eave, z + 0.25f), new Vector3(-x - 0.25f, eave, z + 0.25f) };
        for (int i = 0; i < 4; i++)
            k.Tri(c[i], c[(i + 1) % 4], top);
        // The window toward the plate, lit, the ledger's lamp behind it; the door to the siding.
        float wx = -side * (x + 0.01f);
        k.Use("window_lit", Palette.LampAmber, 0.3f, 0.2f, tile: 1);
        k.Emissive = 1.0f;
        k.Panel(new Vector3(wx, 1.5f, 0), new Vector3(-side, 0, 0), Vector3.UnitY, 1.0f, 0.8f);
        k.Emissive = 0;
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-0.45f, 0, -z - 0.05f), new Vector3(0.45f, 2.0f, -z + 0.02f));
        // The plate: 3 m by 7 across the yard, an iron chequer in a stone kerb, a hair proud of the ground.
        float p0 = -side * (x + 0.9f), p1 = -side * (x + 3.9f);
        var (a, b) = (MathF.Min(p0, p1), MathF.Max(p0, p1));
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 1.2f);
        k.Box(new Vector3(a - 0.25f, -0.2f, -3.75f), new Vector3(b + 0.25f, 0.06f, 3.75f), Kit.Faces.PosY | Kit.Faces.Sides);
        k.Use("iron_plate", Palette.IronGrey, 0.7f, 0.45f, tile: 1);
        k.Box(new Vector3(a, -0.2f, -3.5f), new Vector3(b, 0.08f, 3.5f), Kit.Faces.PosY);
    }

    /// <summary>
    /// Freight stacked waiting for the train: crates two wide and two deep, sacks and a box of parts on top, the clerk's tags
    /// on them (the train's own freight where it's built, else plain crates).
    /// </summary>
    static void Freight(Kit k, Look? look, float x, float z, int variant)
    {
        var props = look is null ? null : PropArt.Of(look);
        void Prop(string name, Vector3 at, float yaw = 0)
        {
            if (props?.Get(name) is { } p)
                k.Append(p, Matrix4x4.CreateRotationY(yaw) * Kit.At(at));
            else
            {
                k.Use("wood_crate", Palette.DeepBrown, 0.8f, 0.05f, tile: 1);
                k.BoxAt(at, new Vector3(0.44f, 0.44f, 0.44f));
            }
        }
        foreach (float zz in new[] { -z * 0.5f, z * 0.5f })
            foreach (float xx in new[] { -x * 0.5f, x * 0.5f })
                Prop(variant % 2 == 0 && zz > 0 ? "freight_timber" : "heavy_crate", new Vector3(xx, 0.45f, zz), xx * 0.02f);
        Prop("freight_sacks", new Vector3(-x * 0.55f, 1.33f, -z * 0.4f), 0.3f);
        Prop(variant % 3 == 0 ? "freight_carboys" : "freight_sacks", new Vector3(x * 0.1f, 1.33f, -z * 0.3f), -0.2f);
        Prop("freight_parts", new Vector3(x * 0.6f, 1.34f, z * 0.3f));
        // The tags: a card tied on each, the clerk's hand.
        k.Use("paper_form", Palette.BoardEnamel, 0.7f, 0, tile: 1);
        foreach (float xx in new[] { -x * 0.5f, x * 0.5f })
            k.Panel(new Vector3(xx + 0.3f, 0.6f, -z - 0.01f), -Vector3.UnitZ, Vector3.UnitY, 0.18f, 0.12f);
    }

    /// <summary>A handcart, its shafts down to the ground toward the siding, sacks on its bed: pulled by hand, six to a cart.</summary>
    static void Cart(Kit k, Look? look, float x, float z)
    {
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        float bed = 0.75f;
        k.Box(new Vector3(-0.75f, bed, -z * 0.35f), new Vector3(0.75f, bed + 0.08f, z), Kit.Faces.All);
        foreach (float sx in new[] { -1f, 1f })
        {
            k.Box(new Vector3(sx * 0.75f - 0.04f, bed, -z * 0.35f), new Vector3(sx * 0.75f + 0.04f, bed + 0.35f, z));
            // The shafts, from the bed's front down to the ground.
            k.Rod(new Vector3(sx * 0.45f, bed, -z * 0.35f), new Vector3(sx * 0.45f, 0.05f, -z + 0.05f), 0.04f);
        }
        k.Use("wheel_iron", Palette.IronGrey, 0.8f, 0.4f, tile: 0.5f);
        foreach (float sx in new[] { -1f, 1f })
            k.Cylinder(new Vector3(sx * x * 0.92f, 0.62f, z * 0.3f), new Vector3(sx * (x * 0.92f - 0.08f), 0.62f, z * 0.3f), 0.62f, 12);
        k.Rod(new Vector3(-x * 0.92f, 0.62f, z * 0.3f), new Vector3(x * 0.92f, 0.62f, z * 0.3f), 0.04f);
        if (look is not null && PropArt.Of(look).Get("freight_sacks") is { } sacks)
        {
            k.Append(sacks, Matrix4x4.CreateRotationY(0.2f) * Kit.At(-0.3f, bed + 0.52f, z * 0.45f));
            k.Append(sacks, Matrix4x4.CreateRotationY(-0.5f) * Kit.At(0.35f, bed + 0.52f, z * 0.1f));
        }
    }

    /// <summary>Old sleepers stacked crossways to dry, for the fires: each with its number cut out of its end.</summary>
    static void Sleepers(Kit k, float x, float z, float h)
    {
        k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
        const float t = 0.15f, w = 0.25f;
        int courses = (int)(h / t);
        for (int c = 0; c < courses; c++)
        {
            float y = c * t, jitter = (c * 37 % 7 - 3) * 0.02f;
            for (float u = -z + 0.18f; u < z - 0.1f; u += 0.42f)
                if (c % 2 == 0)
                    k.Box(new Vector3(-x + jitter, y, u - w / 2), new Vector3(x + jitter, y + t, u + w / 2), Kit.Faces.All & ~Kit.Faces.NegY);
                else
                    k.Box(new Vector3(u - w / 2, y, -z + jitter), new Vector3(u + w / 2, y + t, z + jitter), Kit.Faces.All & ~Kit.Faces.NegY);
        }
    }

    /// <summary>The running gear under a wagon <paramref name="z"/> m half-long: two four-wheeled trucks, its headstocks and buffers.</summary>
    static void Gear(Kit k, float z, float floor)
    {
        k.Use("wheel_iron", Palette.IronGrey, 0.8f, 0.4f, tile: 0.5f);
        foreach (float zz in new[] { -z + 1.8f, z - 1.8f })
        {
            k.Box(new Vector3(-1.05f, 0.3f, zz - 1.1f), new Vector3(1.05f, 0.85f, zz + 1.1f), Kit.Faces.All & ~Kit.Faces.NegY);
            foreach (float dz in new[] { -0.75f, 0.75f })
                foreach (int s in new[] { -1, 1 })
                    k.Cylinder(new Vector3(s * TrainKit.HalfGauge - 0.06f, 0.6f, zz + dz), new Vector3(s * TrainKit.HalfGauge + 0.06f, 0.6f, zz + dz), 0.45f, 10);
        }
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.35f);
        foreach (int e in new[] { -1, 1 })
        {
            k.Box(new Vector3(-1.4f, floor - 0.3f, e * z - 0.12f), new Vector3(1.4f, floor, e * z + 0.12f));
            foreach (int s in new[] { -1, 1 })
                k.Cylinder(new Vector3(s * 0.85f, floor - 0.15f, e * z), new Vector3(s * 0.85f, floor - 0.15f, e * (z + 0.4f)), 0.17f, 8);
        }
    }

    /// <summary>
    /// A goods van standing in the siding: oxide-red planking, its roof curved, its sliding doors shut, what's chalked on
    /// them (<paramref name="variant"/>: its seals still on, a line through where it was going, wired shut and lettered).
    /// </summary>
    static void Van(Kit k, float x, float z, float h, int variant)
    {
        const float floor = 1.15f;
        Gear(k, z, floor);
        var paint = (variant % 3) switch { 0 => Palette.RustRed, 1 => new Vector3(0.36f, 0.3f, 0.24f), _ => new Vector3(0.3f, 0.12f, 0.08f) };
        k.Use("paint_oxide", paint, 0.95f, 0.1f, tile: 1.4f);
        float top = h - 0.35f;
        k.Box(new Vector3(-x, floor, -z), new Vector3(x, top, z), Kit.Faces.All & ~Kit.Faces.NegY);
        // The roof, a shallow curve: two slopes to a crown, overhanging a little.
        k.Use("paint_black", Palette.SootBlack, 0.9f, 0.1f, tile: 1);
        foreach (float sx in new[] { -1f, 1f })
            k.Quad(new Vector3(sx * (x + 0.08f), top, -z - 0.08f), new Vector3(sx * (x + 0.08f), top, z + 0.08f),
                new Vector3(0, h, z + 0.08f), new Vector3(0, h, -z - 0.08f), twoSided: true);
        // A sliding door each side, shut, on its rail; the chalk on it.
        foreach (int s in new[] { -1, 1 })
        {
            k.Use("wood_grey", paint * 0.8f, 0.9f, 0.05f, tile: 1);
            k.Box(new Vector3(s * x - 0.03f, floor + 0.1f, -1.0f), new Vector3(s * x + 0.03f + s * 0.02f, top - 0.15f, 1.0f));
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f, tile: 1);
            k.Box(new Vector3(s * (x + 0.05f) - 0.03f, top - 0.12f, -2.2f), new Vector3(s * (x + 0.05f) + 0.03f, top - 0.05f, 2.2f));
            k.Use("paper_form", Palette.BoardEnamel, 0.7f, 0, tile: 1);
            k.Tint = new Vector3(0.9f);
            float cx = s * (x + 0.07f);
            int lines = variant % 3 == 2 ? 4 : 2;
            for (int i = 0; i < lines; i++)
                k.Box(new Vector3(cx - 0.005f, floor + 1.7f - i * 0.25f, -0.6f), new Vector3(cx + 0.005f, floor + 1.75f - i * 0.25f, -0.6f + 0.6f + (i * 41 % 5) * 0.12f));
            if (variant % 3 == 1)
                // A line through where it was going.
                k.Box(new Vector3(cx - 0.005f, floor + 1.72f, -0.75f), new Vector3(cx + 0.005f, floor + 1.75f, 0.4f));
            k.Tint = Vector3.One;
            if (variant % 3 != 1)
            {
                // Its seal on the door's handle, or the wire round it.
                k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.5f, tile: 1);
                k.BoxAt(new Vector3(s * (x + 0.08f), floor + 1.1f, 1.05f), new Vector3(0.03f, variant % 3 == 2 ? 0.2f : 0.05f, 0.05f));
            }
        }
    }

    /// <summary>A flat wagon: its timber deck on the trucks, and its load lashed down: a dory, or new rails chained in a stack.</summary>
    static void Flat(Kit k, float x, float z, bool dory, int variant)
    {
        const float floor = 1.1f;
        Gear(k, z, floor);
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-x, floor - 0.15f, -z), new Vector3(x, floor, z), Kit.Faces.All);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.35f);
        foreach (int s in new[] { -1, 1 })
            k.Box(new Vector3(s * x - 0.06f, floor - 0.3f, -z), new Vector3(s * x + 0.06f, floor, z));
        if (dory)
        {
            k.With(Matrix4x4.CreateRotationY(MathF.PI / 2) * Kit.At(0, floor, 0), () => MaritimeKit.Dory(k, -2.7f, 2.7f, -0.8f, 0.8f, 0.85f, variant));
            // Its lashings, over the hull to the deck's edges.
            k.Use("wool", Palette.TrailCloth, 0.9f, 0, tile: 1);
            foreach (float zz in new[] { -1.2f, 1.2f })
            {
                k.Rod(new Vector3(-x + 0.05f, floor, zz), new Vector3(-0.5f, floor + 0.85f, zz), 0.02f);
                k.Rod(new Vector3(-0.5f, floor + 0.85f, zz), new Vector3(0.5f, floor + 0.85f, zz), 0.02f);
                k.Rod(new Vector3(0.5f, floor + 0.85f, zz), new Vector3(x - 0.05f, floor, zz), 0.02f);
            }
        }
        else
        {
            // New rails, never laid: two courses on bearers, chained down at three places.
            k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
            foreach (float zz in new[] { -z + 1.0f, 0f, z - 1.0f })
                k.Box(new Vector3(-x + 0.1f, floor, zz - 0.08f), new Vector3(x - 0.1f, floor + 0.1f, zz + 0.08f));
            k.Use("rail_steel", Palette.IronGrey, 0.6f, 0.6f, tile: 1);
            for (int course = 0; course < 2; course++)
                for (int i = 0; i < 6 - course; i++)
                {
                    float rx = -x + 0.3f + (i + course * 0.5f) * 0.42f, y = floor + 0.1f + course * 0.16f;
                    k.Box(new Vector3(rx - 0.07f, y, -z + 0.15f), new Vector3(rx + 0.07f, y + 0.15f, z - 0.15f), Kit.Faces.All & ~Kit.Faces.NegY);
                }
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.5f, tile: 1);
            foreach (float zz in new[] { -z + 1.0f, 0f, z - 1.0f })
            {
                k.Rod(new Vector3(-x + 0.05f, floor, zz), new Vector3(-x + 0.2f, floor + 0.45f, zz), 0.02f);
                k.Rod(new Vector3(-x + 0.2f, floor + 0.45f, zz), new Vector3(x - 0.2f, floor + 0.45f, zz), 0.02f);
                k.Rod(new Vector3(x - 0.2f, floor + 0.45f, zz), new Vector3(x - 0.05f, floor, zz), 0.02f);
            }
        }
    }

    /// <summary>A buffer stop: a beam of old sleepers on two posts, faced white once, braced back into the ground.</summary>
    static void Buffers(Kit k, float x, float z, float h)
    {
        k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
        k.Box(new Vector3(-x, h - 0.5f, -z), new Vector3(x, h, -z + 0.3f), Kit.Faces.All);
        foreach (int s in new[] { -1, 1 })
        {
            k.Box(new Vector3(s * 0.9f - 0.13f, -0.2f, -z + 0.3f), new Vector3(s * 0.9f + 0.13f, h, -z + 0.56f), Kit.Faces.All & ~Kit.Faces.NegY);
            k.Rod(new Vector3(s * 0.9f, h - 0.2f, -z + 0.5f), new Vector3(s * 0.9f, 0, z), 0.08f);
        }
        k.Use("plaster_ruin", Palette.BoardEnamel, 0.95f, 0, tile: 1);
        k.Tint = new Vector3(0.75f);
        k.Panel(new Vector3(0, h - 0.25f, -z - 0.01f), -Vector3.UnitZ, Vector3.UnitY, 2 * x - 0.1f, 0.42f);
        k.Tint = Vector3.One;
    }
}
