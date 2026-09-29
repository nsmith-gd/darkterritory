using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The consist kit (pipeline plan, "Dark Territory asset inventory"; GDD §26 "the train is the protagonist"): the engine,
/// the cargo cars and the guard van, built over the sim's own collision (<see cref="CarShape"/>) so what you see is what
/// you stand on, bump into and climb. Each reads by shape first: the engine boiler-heavy and armoured, a lamp for an eye;
/// the cargo cars boxy and exposed; the guard van cramped, lamp-lit, the gun on its back.
/// </summary>
/// <remarks>
/// Everything here is a cooked <see cref="MeshAsset"/> in the car's frame. What moves (doors, levers, the gun's facing,
/// the firebox's glow) is drawn per frame by <see cref="SceneArt"/>.
/// </remarks>
public static class TrainKit
{
    /// <summary>Half the track gauge: where the wheels' treads run (GreyboxScene draws the rails there).</summary>
    public const float HalfGauge = 0.72f;

    static Vector3 F(Ballast.Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    // ---------------------------------------------------------------- running gear

    /// <summary>
    /// A spoked wheel on the X axis at <paramref name="centre"/>, its outer face towards <paramref name="outward"/> (±1):
    /// tyre, a recessed disc between the spokes, a hub, and for a driver the crank boss and counterweight.
    /// </summary>
    static void Wheel(Kit k, Vector3 centre, float radius, float outward, int spokes, bool driver)
    {
        float width = 0.13f;
        var o = new Vector3(outward, 0, 0);
        var inner = centre - o * (width / 2);
        var outer = centre + o * (width / 2);
        k.Use("wheel_iron", Palette.SootBlack, 0.6f, 0.45f);
        // The tyre, with its flange on the inside, proud of the tread.
        k.Cylinder(inner, outer, radius, 16, caps: false);
        k.Cylinder(inner - o * 0.03f, inner, radius + 0.035f, 16, caps: true);
        // The dark wheel centre, set back from the tyre's face.
        k.Shade(0.45f);
        k.Disc(outer - o * 0.03f, o, radius * 0.9f, 16);
        k.Disc(inner + o * 0.01f, -o, radius * 0.9f, 12);
        k.Use("wheel_iron", Palette.SootBlack, 0.6f, 0.45f);
        // Spokes, radiating from the hub.
        for (int i = 0; i < spokes; i++)
        {
            float a = i * MathF.Tau / spokes + 0.2f;
            var dir = new Vector3(0, MathF.Sin(a), MathF.Cos(a));
            k.Rod(outer - o * 0.03f + dir * radius * 0.2f, outer - o * 0.03f + dir * radius * 0.88f, driver ? 0.032f : 0.025f);
        }
        k.Cylinder(outer - o * 0.05f, outer + o * 0.02f, radius * 0.22f, 10);
        if (driver)
        {
            // The counterweight: a heavy crescent opposite the crank pin, blocked in.
            k.With(Kit.At(outer - o * 0.02f), () =>
            {
                var up = new Vector3(0, 1, 0);
                k.Box(new Vector3(-0.025f, 0, 0) - up * radius * 0.85f + new Vector3(0, 0, -radius * 0.35f),
                    new Vector3(0.025f, 0, 0) - up * radius * 0.45f + new Vector3(0, 0, radius * 0.35f));
            });
            k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.6f);
            k.Cylinder(outer, outer + o * 0.06f, 0.06f, 8);
        }
    }

    /// <summary>A pair of wheels on an axle at <paramref name="z"/>, their centres at <paramref name="radius"/> above the rail.</summary>
    static void Axle(Kit k, float z, float radius, int spokes, bool driver = false)
    {
        foreach (int side in new[] { -1, 1 })
            Wheel(k, new Vector3(side * HalfGauge, radius, z), radius, side, spokes, driver);
        k.Use("wheel_iron", Palette.SootBlack, 0.6f, 0.4f);
        k.Cylinder(new Vector3(-HalfGauge + 0.05f, radius, z), new Vector3(HalfGauge - 0.05f, radius, z), 0.07f, 8);
    }

    /// <summary>A freight bogie (arch-bar truck): side frames, bolster, springs, two axles.</summary>
    static void Truck(Kit k, float z)
    {
        const float r = 0.42f, pitch = 0.8f;
        Axle(k, z - pitch, r, 0);
        Axle(k, z + pitch, r, 0);
        k.Use("wheel_iron", Palette.SootBlack, 0.8f, 0.3f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (HalfGauge + 0.14f);
            // The arch bars: a top bar bowed over the springs, a straight tie bar under, journal boxes at each end.
            k.Box(new Vector3(x - 0.05f, 0.62f, z - pitch - 0.25f), new Vector3(x + 0.05f, 0.7f, z + pitch + 0.25f));
            k.Box(new Vector3(x - 0.05f, 0.3f, z - pitch + 0.1f), new Vector3(x + 0.05f, 0.36f, z + pitch - 0.1f));
            foreach (float dz in new[] { -pitch, pitch })
                k.Box(new Vector3(x - 0.09f, 0.3f, z + dz - 0.16f), new Vector3(x + 0.09f, 0.62f, z + dz + 0.16f));
            // Springs under the bolster.
            k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.1f);
            for (int s = -1; s <= 1; s++)
                k.Cylinder(new Vector3(x, 0.36f, z + s * 0.14f), new Vector3(x, 0.62f, z + s * 0.14f), 0.055f, 6);
            k.Use("wheel_iron", Palette.SootBlack, 0.8f, 0.3f);
        }
        k.Box(new Vector3(-HalfGauge - 0.2f, 0.62f, z - 0.2f), new Vector3(HalfGauge + 0.2f, 0.78f, z + 0.2f));
    }

    /// <summary>A knuckle coupler and its draft gear, out from the end beam at <paramref name="z"/> towards <paramref name="dir"/> (±1).</summary>
    static void Coupler(Kit k, float z, float dir, float height)
    {
        k.Use("wheel_iron", Palette.SootBlack, 0.7f, 0.35f);
        float z0 = z, z1 = z + dir * 0.35f;
        k.Box(new Vector3(-0.09f, height - 0.08f, MathF.Min(z0, z1)), new Vector3(0.09f, height + 0.08f, MathF.Max(z0, z1)));
        k.Box(new Vector3(-0.16f, height - 0.14f, MathF.Min(z1, z1 + dir * 0.18f)), new Vector3(0.16f, height + 0.14f, MathF.Max(z1, z1 + dir * 0.18f)));
        // The air hose, hanging.
        k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.1f);
        k.Rod(new Vector3(0.35f, height - 0.05f, z + dir * 0.05f), new Vector3(0.38f, height - 0.45f, z + dir * 0.25f), 0.025f, 6);
    }

    /// <summary>An iron ladder up a face: two stiles and rungs every 0.3 m, standing off it by a hand's depth.</summary>
    public static void RungLadder(Kit k, Vector3 foot, float top, Vector3 inward, float from = 0.2f)
    {
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, inward));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        float half = 0.2f;
        foreach (int s in new[] { -1, 1 })
            k.Box(foot + across * (s * half) - new Vector3(0.022f, -from, 0.022f), foot + across * (s * half) + new Vector3(0.022f, top, 0.022f));
        for (float y = from + 0.25f; y < top - 0.05f; y += 0.3f)
            k.Rod(foot + across * -half + new Vector3(0, y, 0), foot + across * half + new Vector3(0, y, 0), 0.016f);
        // Brackets back to the face.
        foreach (float y in new[] { from + 0.3f, top - 0.1f })
            foreach (int s in new[] { -1, 1 })
                k.Rod(foot + across * (s * half) + new Vector3(0, y, 0), foot + across * (s * half) + inward * 0.14f + new Vector3(0, y, 0), 0.015f);
    }

    /// <summary>A grab iron: a bent rod standing off a face.</summary>
    static void Grab(Kit k, Vector3 a, Vector3 b, Vector3 outward)
    {
        k.Rod(a + outward * 0.08f, b + outward * 0.08f, 0.014f);
        k.Rod(a, a + outward * 0.08f, 0.014f);
        k.Rod(b, b + outward * 0.08f, 0.014f);
    }

    // ---------------------------------------------------------------- the engine

    /// <summary>
    /// The engine and tender as one unit (spec B.4): an armoured 2-8-0. The boiler is cased in riveted plate (an octagon,
    /// so it reads as heavy and hand-made), the lamp sits in an armoured box at the front like an eye, the stack is a
    /// tapered funnel, and behind the cab the tender's coal is heaped under a flared coal board.
    /// </summary>
    public static MeshAsset Engine(Look? look, CarShape shape, int variant)
    {
        var k = new Kit(look, 101 + variant);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength;
        var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
        var stack = shape.Solids.First(s => s.Part == PartKind.Stack).Box;
        var tender = shape.Solids.First(s => s.Part == PartKind.Tender).Box;
        var roof = shape.Solids.First(s => s.Part == PartKind.CabRoof).Box;
        var cab = shape.Cab!.Value;
        float deck = (float)boiler.Min.Y, top = (float)boiler.Max.Y, bw = (float)boiler.Max.X;
        float cabFront = (float)cab.Min.Z, cabBack = (float)cab.Max.Z;
        float boilerFront = (float)boiler.Min.Z;
        float roofLow = (float)roof.Min.Y, roofTop = (float)roof.Max.Y;

        // Running gear: pilot truck, four drivers, the steam cylinders ahead of them, and the tender's two axles.
        const float driverR = 0.7f;
        float[] drivers = [-l + 3.8f, -l + 5.4f, -l + 7.0f, -l + 8.6f];
        foreach (float z in drivers)
            Axle(k, z, driverR, 12, driver: true);
        Axle(k, -l + 1.55f, 0.4f, 8);
        foreach (float z in new[] { tender.Min.Z + 1.0f, tender.Max.Z - 1.0f })
            Axle(k, (float)z, 0.45f, 0);
        // Frames: deep plates inside the wheels.
        k.Use("wheel_iron", Palette.SootBlack, 0.8f, 0.3f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side * 0.62f - 0.04f, 0.4f, -l + 0.8f), new Vector3(side * 0.62f + 0.04f, deck - 0.06f, l - 0.6f));
        // Rods: the coupling rod across all four crank pins, and the main rod from the crosshead to the third driver.
        k.Use("wheel_iron", Palette.IronGrey, 0.5f, 0.7f);
        foreach (int side in new[] { -1, 1 })
        {
            // The pins are a quarter turn apart side to side, so the train never stops on a dead centre.
            float phase = side < 0 ? 0.4f : 0.4f + MathF.PI / 2;
            var pin = new Vector3(0, MathF.Sin(phase), MathF.Cos(phase)) * 0.3f;
            float x = side * (HalfGauge + 0.2f);
            k.Box(new Vector3(x - 0.03f, driverR + pin.Y - 0.06f, drivers[0] + pin.Z - 0.1f), new Vector3(x + 0.03f, driverR + pin.Y + 0.06f, drivers[^1] + pin.Z + 0.1f));
            var crosshead = new Vector3(x + side * 0.06f, 0.95f, -l + 3.0f);
            k.Rod(crosshead, new Vector3(x + side * 0.06f, driverR + pin.Y, drivers[2] + pin.Z), 0.045f);
            // Crosshead guides and the cylinder, with its drain cocks.
            k.Box(new Vector3(x + side * 0.06f - 0.03f, 0.88f, -l + 2.2f), new Vector3(x + side * 0.06f + 0.03f, 1.02f, -l + 3.4f));
            k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
            var cylA = new Vector3(side * 1.12f, 0.98f, -l + 1.1f);
            var cylB = new Vector3(side * 1.12f, 0.98f, -l + 2.2f);
            k.Cylinder(cylA, cylB, 0.34f, 12);
            k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
            k.Cylinder(cylA - new Vector3(0, 0, 0.04f), cylA + new Vector3(0, 0, 0.02f), 0.36f, 12);
            k.Use("wheel_iron", Palette.IronGrey, 0.5f, 0.7f);
        }

        // Buffer beam and plough: the front is a wedge of armour down to the rail, for Sleepers and worse.
        k.Use("paint_oxide", Palette.RustRed, 0.9f, 0.1f);
        k.Box(new Vector3(-w, 0.75f, -l), new Vector3(w, deck, -l + 0.25f));
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        {
            float y0 = 0.1f, y1 = 0.78f, zf = -l - 0.45f, zb = -l + 0.05f;
            // Two raked planes meeting at the centre line, and a top shelf.
            var tl = new Vector3(-w + 0.1f, y1, zb);
            var tr = new Vector3(w - 0.1f, y1, zb);
            var tc = new Vector3(0, y1, zb - 0.2f);
            var bl = new Vector3(-w + 0.25f, y0, zb - 0.1f);
            var br = new Vector3(w - 0.25f, y0, zb - 0.1f);
            var bc = new Vector3(0, y0, zf);
            k.Quad(tc, tl, bl, bc);
            k.Quad(tr, tc, bc, br);
            k.Tri(tl, bl, new Vector3(-w + 0.1f, y0, zb), new(0, 0), new(0.1f, 0.7f), new(0, 0.7f));
            k.Tri(tr, new Vector3(w - 0.1f, y0, zb), br, new(0, 0), new(0, 0.7f), new(0.1f, 0.7f));
        }
        Coupler(k, -l - 0.05f, -1, 0.9f);

        // The running boards either side of the boiler, with a valance down over the wheels (armour skirt).
        k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
        k.Box(new Vector3(-w, deck - 0.06f, -l + 0.2f), new Vector3(w, deck, cabFront), Kit.Faces.All);
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side < 0 ? -w - 0.02f : w - 0.03f, x1 = side < 0 ? -w + 0.03f : w + 0.02f;
            k.Box(new Vector3(x0, 1.02f, -l + 0.25f), new Vector3(x1, deck - 0.02f, cabFront - 0.1f));
            // Straps where the skirt's plates join.
            for (float z = -l + 1.8f; z < cabFront - 0.5f; z += 1.9f)
                k.Box(new Vector3(x0 - side * 0.015f, 1.0f, z - 0.05f), new Vector3(x1 + side * 0.015f, deck, z + 0.05f));
        }

        // The armoured boiler casing: an octagon over the boiler, riveted plate, strapped where the plates meet.
        Vector2[] casing = Octagon(-bw, deck + 0.02f, bw, top, 0.45f, 0.18f);
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f, tile: 1.5f);
        k.Prism(casing, boilerFront, cabFront, caps: false, smooth: false);
        k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f);
        Vector2[] strap = Octagon(-bw - 0.025f, deck + 0.02f, bw + 0.025f, top + 0.025f, 0.46f, 0.18f);
        for (float z = boilerFront + 1.6f; z < cabFront - 0.4f; z += 2.2f)
            k.Prism(strap, z, z + 0.14f, caps: false, smooth: false);
        // The front: a flat armour face, the smokebox door's ring on it, and the lamp in its armoured box above.
        k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.3f);
        k.Prism(casing, boilerFront - 0.2f, boilerFront, caps: true, smooth: false);
        k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.4f);
        float doorY = deck + (top - deck) * 0.35f;
        k.Cylinder(new Vector3(0, doorY, boilerFront - 0.2f), new Vector3(0, doorY, boilerFront - 0.3f), 0.5f, 14);
        k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
        k.Cylinder(new Vector3(0, doorY, boilerFront - 0.3f), new Vector3(0, doorY, boilerFront - 0.42f), 0.07f, 8);
        k.Rod(new Vector3(-0.3f, doorY, boilerFront - 0.36f), new Vector3(0.3f, doorY, boilerFront - 0.36f), 0.018f);
        // The headlamp box: where Views puts the lamp's light, on a shelf, hooded, caged.
        var lamp = new Vector3(0, 2.8f, -l - 0.05f);
        k.Use("paint_black", Palette.SootBlack, 0.7f, 0.3f);
        k.Box(new Vector3(-0.42f, lamp.Y - 0.38f, lamp.Z + 0.02f), new Vector3(0.42f, lamp.Y + 0.38f, boilerFront - 0.18f));
        k.Use("iron_plate", Palette.IronGrey, 0.8f, 0.35f);
        k.Box(new Vector3(-0.5f, lamp.Y + 0.38f, lamp.Z - 0.2f), new Vector3(0.5f, lamp.Y + 0.44f, boilerFront - 0.1f));
        k.Box(new Vector3(-0.46f, lamp.Y - 0.44f, lamp.Z), new Vector3(0.46f, lamp.Y - 0.38f, boilerFront - 0.1f));
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.64f);
        k.Emissive = 1;
        k.Panel(lamp, -Vector3.UnitZ, Vector3.UnitY, 0.64f, 0.64f);
        k.Emissive = 0;
        k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
        for (int i = -1; i <= 1; i++)
            k.Rod(new Vector3(i * 0.18f, lamp.Y - 0.36f, lamp.Z - 0.06f), new Vector3(i * 0.18f, lamp.Y + 0.36f, lamp.Z - 0.06f), 0.014f);

        // The stack: a tapered funnel with a flared lip, soot-black inside.
        {
            var c = new Vector3(0, top - 0.05f, (float)stack.Centre.Z);
            float r = (float)stack.HalfSize.X + 0.05f;
            k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.3f);
            k.Lathe(c, [new(r + 0.12f, 0), new(r, 0.18f), new(r - 0.06f, (float)stack.Max.Y - c.Y - 0.2f), new(r + 0.06f, (float)stack.Max.Y - c.Y - 0.05f), new(r + 0.06f, (float)stack.Max.Y - c.Y), new(r - 0.1f, (float)stack.Max.Y - c.Y)], 10, smooth: false, capTop: false);
            k.Shade(0.15f);
            k.Disc(new Vector3(0, (float)stack.Max.Y - 0.12f, c.Z), Vector3.UnitY, r - 0.08f, 10);
        }
        // Steam dome and sand dome, low (the boiler top is walkable), and the whistle and safety valves by the cab.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        k.Lathe(new Vector3(0, top - 0.08f, boilerFront + (cabFront - boilerFront) * 0.45f), [new(0.5f, 0), new(0.42f, 0.2f), new(0.3f, 0.36f), new(0, 0.42f)], 10, smooth: true);
        k.Lathe(new Vector3(0, top - 0.08f, boilerFront + (cabFront - boilerFront) * 0.72f), [new(0.4f, 0), new(0.32f, 0.16f), new(0.2f, 0.27f), new(0, 0.3f)], 10, smooth: true);
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
        foreach (float dx in new[] { -0.12f, 0.12f })
            k.Lathe(new Vector3(dx, top - 0.05f, cabFront - 0.7f), [new(0.07f, 0), new(0.06f, 0.2f), new(0.09f, 0.22f), new(0.02f, 0.3f)], 8);
        k.Lathe(new Vector3(0.35f, top - 0.1f, cabFront - 0.3f), [new(0.04f, 0), new(0.04f, 0.25f), new(0.08f, 0.28f), new(0.08f, 0.55f), new(0.04f, 0.6f)], 8);
        // Handrails along the casing, on stanchions.
        k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (bw + 0.08f), y = deck + (top - deck) * 0.62f;
            k.Rod(new Vector3(x, y, boilerFront + 0.3f), new Vector3(x, y, cabFront - 0.2f), 0.018f, 6);
            for (float z = boilerFront + 0.4f; z < cabFront; z += 1.8f)
                k.Rod(new Vector3(x, y, z), new Vector3(side * (bw - 0.02f), y, z), 0.014f);
        }
        // Sand pipes running down to the rails ahead of the drivers.
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.7f, 0.5f);
        foreach (int side in new[] { -1, 1 })
            k.Rod(new Vector3(side * (bw * 0.6f), top - 0.25f, boilerFront + (cabFront - boilerFront) * 0.72f), new Vector3(side * (HalfGauge + 0.1f), 0.25f, drivers[0] - 0.8f), 0.02f, 5);

        Cab(k, shape, w, deck, cabFront, cabBack, bw, top, roofLow, roofTop);
        CouplerPlates(k, shape);
        Tender(k, shape, w, l, deck, (float)tender.Max.Y, cabBack);
        return k.Build($"engine-{variant}");
    }

    /// <summary>An octagon (counter-clockwise) for a box with its top corners cut by <paramref name="topBevel"/> and its bottom by <paramref name="bottomBevel"/>.</summary>
    static Vector2[] Octagon(float x0, float y0, float x1, float y1, float topBevel, float bottomBevel) =>
    [
        new(x0 + bottomBevel, y0), new(x1 - bottomBevel, y0), new(x1, y0 + bottomBevel), new(x1, y1 - topBevel),
        new(x1 - topBevel, y1), new(x0 + topBevel, y1), new(x0, y1 - topBevel), new(x0, y0 + bottomBevel),
    ];

    /// <summary>
    /// The cab: waist-high armoured sides with the doorways at the back, pillars, a visor over the side openings, the
    /// spectacle plate over the boiler with its two small windows, an arched roof, and inside, the backhead: the firebox
    /// door's frame, four gauges, the water glass and pipework (GDD §12, the Conductor's and the Boiler's place).
    /// </summary>
    static void Cab(Kit k, CarShape shape, float w, float deck, float cabFront, float cabBack, float bw, float top, float roofLow, float roofTop)
    {
        float waist = deck + 1.1f;
        var doorFront = cabBack - 0.9f;
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.CabWall))
        {
            var b = solid.Box;
            k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
            k.Box(F(b.Min), F(b.Max));
        }
        foreach (int side in new[] { -1, 1 })
        {
            // A capping rail on the waist, and the visor plate over the opening (above head height, no collision).
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
            float xo = side * w;
            k.Box(new Vector3(MathF.Min(xo, xo - side * 0.14f), waist, cabFront), new Vector3(MathF.Max(xo, xo - side * 0.14f), waist + 0.05f, doorFront));
            k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
            k.Box(new Vector3(MathF.Min(xo, xo - side * 0.08f), roofLow - 0.32f, cabFront + 0.1f), new Vector3(MathF.Max(xo, xo - side * 0.08f), roofLow, cabBack - 0.1f));
            // Grab irons at the doorway, and the step irons down to the ballast.
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
            Grab(k, new Vector3(xo, deck + 0.3f, cabBack - 0.12f), new Vector3(xo, waist + 0.6f, cabBack - 0.12f), new Vector3(side, 0, 0));
            foreach (float y in new[] { deck - 0.5f, deck - 1.0f })
                k.Box(new Vector3(MathF.Min(xo, xo + side * 0.25f), y - 0.03f, doorFront + 0.1f), new Vector3(MathF.Max(xo, xo + side * 0.25f), y, cabBack - 0.1f));
            k.Rod(new Vector3(xo + side * 0.24f, deck - 1.03f, doorFront + 0.12f), new Vector3(xo + side * 0.02f, deck, doorFront + 0.12f), 0.015f);
            k.Rod(new Vector3(xo + side * 0.24f, deck - 1.03f, cabBack - 0.12f), new Vector3(xo + side * 0.02f, deck, cabBack - 0.12f), 0.015f);
        }
        // The spectacle plate: either side of the boiler, and over it, with a window in each side part.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        float z0 = cabFront, z1 = cabFront + 0.08f;
        float winY0 = top - 0.45f, winY1 = top + 0.12f;
        foreach (int side in new[] { -1, 1 })
        {
            float xa = side * bw, xb = side * (w - 0.1f);
            var (x0, x1) = (MathF.Min(xa, xb), MathF.Max(xa, xb));
            k.Box(new Vector3(x0, deck, z0), new Vector3(x1, winY0, z1));
            k.Box(new Vector3(x0, winY1, z0), new Vector3(x1, roofLow, z1));
            k.Box(new Vector3(x0, winY0, z0), new Vector3(x0 + 0.08f, winY1, z1));
            k.Box(new Vector3(x1 - 0.08f, winY0, z0), new Vector3(x1, winY1, z1));
            k.Use("glass_dirty", Palette.BlueGrey, 0.3f, 0.8f, tile: 0.5f);
            k.Panel(new Vector3((x0 + x1) / 2, (winY0 + winY1) / 2, z0 + 0.04f), Vector3.UnitZ, Vector3.UnitY, x1 - x0 - 0.16f, winY1 - winY0, twoSided: true);
            k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        }
        k.Box(new Vector3(-bw, top, z0), new Vector3(bw, roofLow, z1));

        // The roof: shallowly arched, overhanging, with a vent on top.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f, tile: 1.5f);
        var arch = new List<Vector2>();
        const int n = 8;
        float rw = w + 0.12f;
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n * 2 - 1;
            arch.Add(new Vector2(-t * rw, roofTop - (roofTop - roofLow - 0.08f) * t * t * 0.9f));
        }
        var roofProfile = new List<Vector2>(arch);
        roofProfile.Add(new Vector2(-rw, roofLow));
        roofProfile.Add(new Vector2(rw, roofLow));
        roofProfile.Reverse();
        k.Prism(roofProfile, cabFront - 0.12f, cabBack + 0.12f, caps: true, smooth: false);

        // Inside: the floor, and the backhead.
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0);
        k.Box(new Vector3(-w + 0.1f, deck - 0.02f, cabFront), new Vector3(w - 0.1f, deck + 0.005f, cabBack), Kit.Faces.PosY);
        float face = cabFront + 0.085f;
        k.Use("iron_smokebox", Palette.SootBlack, 0.8f, 0.3f);
        k.Box(new Vector3(-bw, deck, cabFront), new Vector3(bw, top, face), Kit.Faces.PosZ);
        // The firebox door's frame (the glow itself is drawn with the fire).
        var fire = shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        float fy = (float)fire.Y + 0.7f;
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        k.Box(new Vector3(-0.42f, fy - 0.3f, face), new Vector3(0.42f, fy - 0.22f, face + 0.08f));
        k.Box(new Vector3(-0.42f, fy + 0.22f, face), new Vector3(0.42f, fy + 0.3f, face + 0.08f));
        k.Box(new Vector3(-0.42f, fy - 0.22f, face), new Vector3(-0.32f, fy + 0.22f, face + 0.08f));
        k.Box(new Vector3(0.32f, fy - 0.22f, face), new Vector3(0.42f, fy + 0.22f, face + 0.08f));
        // Gauges: pressure, heat, water, speed (the gauge atlas's four quarters), in brass bezels at eye height.
        string[] order = ["pressure", "heat", "water", "speed"];
        for (int i = 0; i < 4; i++)
        {
            var c = GaugeCentre(shape, i) - new Vector3(0, 0, 0.012f);
            k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
            k.Cylinder(c - new Vector3(0, 0, 0.05f), c + new Vector3(0, 0, 0.01f), 0.13f, 12);
            k.Use("gauge_face", Palette.TarnishedBrass * 1.6f, 0.2f, 0.3f, tile: 1);
            var cell = GaugeCell(order[i]);
            k.Disc(c + new Vector3(0, 0, 0.012f), Vector3.UnitZ, 0.11f, 16, cell.Centre, cell.Radius);
        }
        // The water glass, right of the door, and pipes.
        k.Use("glass_dirty", Palette.BlueGrey, 0.2f, 0.9f, tile: 0.3f);
        k.Box(new Vector3(0.62f, fy + 0.05f, face), new Vector3(0.68f, fy + 0.6f, face + 0.06f));
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.6f, 0.6f);
        k.Rod(new Vector3(-0.8f, deck + 0.2f, face + 0.05f), new Vector3(-0.8f, top - 0.2f, face + 0.05f), 0.025f, 6);
        k.Rod(new Vector3(-0.8f, top - 0.2f, face + 0.05f), new Vector3(0.7f, top - 0.2f, face + 0.05f), 0.025f, 6);
        k.Rod(new Vector3(0.75f, deck + 0.3f, face + 0.05f), new Vector3(0.75f, top - 0.2f, face + 0.05f), 0.02f, 6);
    }

    /// <summary>The centre of dial <paramref name="index"/>'s face on the backhead (pressure, heat, water, speed), in the engine's frame.</summary>
    public static Vector3 GaugeCentre(CarShape shape, int index)
    {
        var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
        float face = (float)shape.Cab!.Value.Min.Z + 0.085f;
        return new Vector3(-0.54f + index * 0.36f, (float)boiler.Min.Y + 1.85f + index % 2 * 0.06f, face + 0.072f);
    }

    /// <summary>A gauge's needle: pointing up (+Y) from its pivot, dark with a red tip, on a brass boss.</summary>
    public static MeshAsset Needle(Look? look)
    {
        var k = new Kit(look, 60);
        k.Use("paint_black", Palette.SootBlack, 0.2f, 0.4f, tile: 0.1f);
        k.Box(new Vector3(-0.004f, -0.015f, 0), new Vector3(0.004f, 0.08f, 0.004f));
        k.Use("paint_oxide", Palette.SignalRed, 0.2f, 0.2f, tile: 0.1f);
        k.Box(new Vector3(-0.004f, 0.06f, 0.0005f), new Vector3(0.004f, 0.085f, 0.0045f));
        k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.7f);
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0, 0.008f), 0.012f, 6);
        return k.Build("needle");
    }

    /// <summary>Where on the gauge atlas a dial is, in texture coordinates: the four quarters, left to right, top to bottom.</summary>
    public static (Vector2 Centre, float Radius) GaugeCell(string gauge) => gauge switch
    {
        "pressure" => (new Vector2(0.25f, 0.25f), 0.23f),
        "heat" => (new Vector2(0.75f, 0.25f), 0.23f),
        "water" => (new Vector2(0.25f, 0.75f), 0.23f),
        _ => (new Vector2(0.75f, 0.75f), 0.23f),
    };

    /// <summary>
    /// The tender: tank sides, a flared coal board round the heap, the coal itself heaped where you walk, the coal gate at
    /// the front onto the shovelling plate, a ladder and a tail lamp at the back.
    /// </summary>
    static void Tender(Kit k, CarShape shape, float w, float l, float deck, float top, float front)
    {
        k.Use("paint_black", Palette.SootBlack, 0.9f, 0.3f, tile: 1.5f);
        float sides = top - 0.28f;
        k.Box(new Vector3(-w, deck, front + 0.05f), new Vector3(w, sides, l), Kit.Faces.Sides);
        // Straps and rivet lines down the tank.
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        for (float z = front + 0.9f; z < l - 0.2f; z += 1.2f)
            foreach (int side in new[] { -1, 1 })
                k.Box(new Vector3(side * w - 0.02f, deck + 0.1f, z - 0.05f), new Vector3(side * w + 0.02f, sides, z + 0.05f));
        // The flared coal board.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        foreach (int side in new[] { -1, 1 })
        {
            var a = new Vector3(side * w, sides, front + 0.05f);
            var b = new Vector3(side * w, sides, l);
            var c = new Vector3(side * (w + 0.1f), top + 0.06f, l);
            var d = new Vector3(side * (w + 0.1f), top + 0.06f, front + 0.05f);
            if (side > 0)
                k.Quad(d, c, b, a, twoSided: true);
            else
                k.Quad(c, d, a, b, twoSided: true);
        }
        k.Box(new Vector3(-w, sides, l - 0.08f), new Vector3(w, top + 0.06f, l));
        // The coal, heaped to the walking height, lumpy.
        k.Use("coal", Palette.SootBlack, 0.4f, 0.5f, tile: 1.2f);
        const int nx = 6, nz = 8;
        float zc0 = front + 0.05f, zc1 = l - 0.08f;
        Vector3 Coal(int i, int j)
        {
            float x = -w + 2 * w * i / nx, z = zc0 + (zc1 - zc0) * j / nz;
            float bump = MathF.Sin(i * 2.3f + j * 1.7f) * 0.05f + MathF.Sin(i * 5.1f - j * 3.3f) * 0.03f;
            float edge = (i == 0 || i == nx) ? -0.12f : 0;
            return new Vector3(x, top + bump + edge, z);
        }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                k.Quad(Coal(i + 1, j + 1), Coal(i, j + 1), Coal(i, j), Coal(i + 1, j));
        // The front: plate either side of the coal gate, the gate's coal face, and the shovelling plate.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        float gate = 0.55f, gateTop = deck + 0.95f;
        k.Box(new Vector3(-w, deck, front), new Vector3(-gate, top, front + 0.06f), Kit.Faces.NegZ | Kit.Faces.PosY);
        k.Box(new Vector3(gate, deck, front), new Vector3(w, top, front + 0.06f), Kit.Faces.NegZ | Kit.Faces.PosY);
        k.Box(new Vector3(-gate, gateTop, front), new Vector3(gate, top, front + 0.06f), Kit.Faces.NegZ | Kit.Faces.NegY);
        k.Use("coal", Palette.SootBlack, 0.4f, 0.5f, tile: 1.2f);
        k.Quad(new Vector3(gate, gateTop, front + 0.2f), new Vector3(-gate, gateTop, front + 0.2f), new Vector3(-gate, deck + 0.05f, front - 0.15f), new Vector3(gate, deck + 0.05f, front - 0.15f));
        k.Use("iron_plate", Palette.IronGrey, 0.8f, 0.4f);
        k.Box(new Vector3(-gate - 0.1f, deck, front - 0.7f), new Vector3(gate + 0.1f, deck + 0.02f, front), Kit.Faces.PosY);
        // The frame under it, the rear beam, the coupler, the rear ladder and the tail lamp.
        k.Use("paint_oxide", Palette.RustRed, 0.9f, 0.1f);
        k.Box(new Vector3(-w, 0.75f, l - 0.25f), new Vector3(w, deck, l));
        k.Use("paint_black", Palette.SootBlack, 0.9f, 0.2f);
        k.Box(new Vector3(-w, deck - 0.25f, front), new Vector3(w, deck, l - 0.25f));
        Coupler(k, l, 1, 0.9f);
        foreach (var ladder in shape.Ladders.Where(x => x.Foot.Z > front && Math.Abs(x.Inward.Z) > 0))
            RungLadder(k, F(ladder.Foot) + new Vector3(0, 0, -0.04f), (float)ladder.Top, F(ladder.Inward), from: 0.5f);
        k.Use("paint_black", Palette.SootBlack, 0.7f, 0.3f);
        var tail = new Vector3(-w + 0.25f, top + 0.25f, l + 0.05f);
        k.BoxAt(tail, new Vector3(0.12f, 0.14f, 0.1f));
        k.Use("lamp_lens", Palette.SignalRed, 0, 0, tile: 0.2f);
        k.Emissive = 1;
        k.Tint = new Vector3(1.0f, 0.25f, 0.18f);
        k.Panel(tail + new Vector3(0, 0, 0.101f), Vector3.UnitZ, Vector3.UnitY, 0.16f, 0.18f);
        k.Emissive = 0;
    }

    // ---------------------------------------------------------------- cars

    /// <summary>How a car is finished: planked boxcar, steel boxcar, or armoured (plate and olive paint).</summary>
    public enum Livery { Planked, Steel, Armoured }

    public static Livery LiveryOf(int carIndex) => (carIndex % 3) switch { 0 => Livery.Steel, 1 => Livery.Planked, _ => Livery.Armoured };

    /// <summary>
    /// A walk-in car (cargo or guard van): the body over the sim's walls and roof slab, its outside finished by livery,
    /// its inside lined in dark boards with carlines under the roof, the underframe and trucks beneath, couplers, the
    /// side and end ladders, the roof walk and brake wheel; the cargo stacked as crates, the side-door steps as timber.
    /// </summary>
    public static MeshAsset Car(Look? look, CarShape shape, Livery livery, int variant)
    {
        var k = new Kit(look, 7 + variant * 3 + (int)livery);
        var interior = shape.Interior!.Value;
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        float floor = (float)interior.Min.Y + 0.1f, ceiling = (float)interior.Max.Y, t = w - (float)interior.Max.X;
        bool guard = shape.Gun is not null;

        (string Texture, Vector3 Fallback, float Tile) outside = livery switch
        {
            Livery.Planked => ("wood_siding", Palette.DeepBrown, 1f),
            Livery.Steel => ("paint_oxide", Palette.RustRed, 1.5f),
            _ => ("paint_olive", Palette.MuddyOlive, 1.5f),
        };
        void Outside() => k.Use(outside.Texture, outside.Fallback, 0.9f, livery == Livery.Planked ? 0.02f : 0.2f, outside.Tile);
        void Lining() => k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0, 1f);

        // Walls: the outer face in the livery, every other face in the lining.
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.Wall))
        {
            var b = solid.Box;
            var (min, max) = (F(b.Min), F(b.Max));
            Kit.Faces outer = 0;
            if (max.X >= w - 1e-3f) outer |= Kit.Faces.PosX;
            if (min.X <= -w + 1e-3f) outer |= Kit.Faces.NegX;
            if (max.Z >= l - 1e-3f) outer |= Kit.Faces.PosZ;
            if (min.Z <= -l + 1e-3f) outer |= Kit.Faces.NegZ;
            Outside();
            k.Box(min, max, outer);
            Lining();
            k.Box(min, max, Kit.Faces.All & ~outer);
        }
        // Outside framing: posts and braces on a planked car, ribs on steel, plate straps on armour.
        Outside();
        k.Shade(livery == Livery.Planked ? 0.7f : 0.85f);
        float sd = shape.DoorList.Count > 2 ? (float)(shape.DoorList[2].Box.Max.Z - shape.DoorList[2].Box.Min.Z) / 2 : 0;
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w + 0.03f);
            var posts = new List<float>();
            for (float z = -l + 0.06f; z <= l - 0.05f; z += (2 * l - 0.12f) / 8)
                if (sd == 0 || MathF.Abs(z) > sd + 0.1f)
                    posts.Add(z);
            foreach (float z in posts)
                k.Box(new Vector3(x - 0.03f, floor - 0.05f, z - 0.06f), new Vector3(x + 0.03f, ceiling, z + 0.06f));
            if (livery == Livery.Planked)
                for (int i = 0; i + 1 < posts.Count; i++)
                {
                    if (MathF.Abs(posts[i + 1] - posts[i]) > 2.2f)
                        continue;
                    bool down = (posts[i] < 0) == (i % 2 == 0);
                    var a = new Vector3(x, down ? ceiling - 0.1f : floor + 0.1f, posts[i]);
                    var b = new Vector3(x, down ? floor + 0.1f : ceiling - 0.1f, posts[i + 1]);
                    k.Rod(a, b, 0.035f);
                }
            // Top chord and bottom sill, full length.
            k.Box(new Vector3(x - 0.035f, ceiling - 0.1f, -l), new Vector3(x + 0.035f, ceiling, l));
            k.Box(new Vector3(x - 0.04f, floor - 0.25f, -l), new Vector3(x + 0.04f, floor, l));
        }
        if (livery == Livery.Armoured)
        {
            // Firing slits along the sides, dark, with their hinged covers open above.
            k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
            foreach (int side in new[] { -1, 1 })
                foreach (float z in new[] { -l + 1.6f, -l + 3.4f, l - 3.4f, l - 1.6f })
                {
                    if (sd > 0 && MathF.Abs(z) < sd + 0.4f)
                        continue;
                    float x = side * (w + 0.005f);
                    k.Shade(0.08f);
                    k.Box(new Vector3(x - 0.004f, floor + 1.35f, z - 0.35f), new Vector3(x + 0.004f, floor + 1.5f, z + 0.35f), side > 0 ? Kit.Faces.PosX : Kit.Faces.NegX);
                    k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
                    k.Box(new Vector3(x - 0.04f, floor + 1.52f, z - 0.4f), new Vector3(x + 0.04f, floor + 1.58f, z + 0.4f));
                }
        }

        // The roof: the slab's underside lined, carlines under it, an arched corrugated top with eaves, and the roof walk.
        Lining();
        k.Box(new Vector3(-w, ceiling, -l), new Vector3(w, ceiling + 0.01f, l), Kit.Faces.NegY);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0);
        for (float z = -l + 1; z < l - 0.5f; z += 1.1f)
            k.Box(new Vector3(-w + t, ceiling - 0.08f, z - 0.05f), new Vector3(w - t, ceiling, z + 0.05f), Kit.Faces.All & ~Kit.Faces.PosY);
        k.Use(livery == Livery.Planked ? "corrugated_iron" : "iron_plate", Palette.IronGrey, 0.9f, 0.3f, tile: 1.2f);
        {
            var profile = new List<Vector2> { new(w + 0.07f, ceiling - 0.02f), new(w + 0.07f, ceiling + 0.04f) };
            const int n = 6;
            for (int i = 0; i <= n; i++)
            {
                float s = (float)i / n * 2 - 1;
                profile.Add(new Vector2(-s * (w + 0.02f), h - 0.035f * s * s - 0.005f));
            }
            profile.Add(new Vector2(-w - 0.07f, ceiling + 0.04f));
            profile.Add(new Vector2(-w - 0.07f, ceiling - 0.02f));
            k.Prism(profile, -l - 0.06f, l + 0.06f, caps: true, smooth: false);
        }
        // Roof walk: boards on saddles down the safe centreline, gapped.
        k.Use("wood_grey", Palette.TarnishedBrass, 0.9f, 0, 1f);
        for (int i = -2; i <= 2; i++)
            k.Box(new Vector3(i * 0.14f - 0.06f, h, -l + 0.2f), new Vector3(i * 0.14f + 0.06f, h + 0.04f, l - 0.2f), Kit.Faces.All & ~Kit.Faces.NegY);
        // The brake wheel on its staff at the rear of the walk.
        foreach (var brake in shape.Interactables.Where(i => i.Kind == InteractableKind.Handbrake))
            BrakeWheel(k, F(brake.Position));

        // Floor and underframe.
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0);
        k.Box(new Vector3(-w + t, floor - 0.01f, -l + t), new Vector3(w - t, floor, l - t), Kit.Faces.PosY);
        k.Use("paint_black", Palette.SootBlack, 0.9f, 0.2f);
        k.Box(new Vector3(-w, floor - 0.2f, -l), new Vector3(w, floor - 0.01f, l), Kit.Faces.All & ~Kit.Faces.PosY);
        k.Box(new Vector3(-0.22f, 0.72f, -l + 0.2f), new Vector3(0.22f, floor - 0.2f, l - 0.2f));
        foreach (float z in new[] { -l + 1.9f, l - 1.9f })
            k.Box(new Vector3(-w + 0.2f, 0.78f, z - 0.2f), new Vector3(w - 0.2f, floor - 0.2f, z + 0.2f));
        // The brake gear: reservoir and cylinder slung under the middle.
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.2f);
        k.Cylinder(new Vector3(0.45f, 0.8f, -1.2f), new Vector3(0.45f, 0.8f, 0.4f), 0.2f, 10);
        k.Cylinder(new Vector3(-0.5f, 0.82f, 0.2f), new Vector3(-0.5f, 0.82f, 0.9f), 0.15f, 10);
        foreach (float z in new[] { -l + 1.9f, l - 1.9f })
            Truck(k, z);
        // End beams, couplers.
        k.Use("paint_oxide", Palette.RustRed, 0.9f, 0.1f);
        foreach (int end in new[] { -1, 1 })
            k.Box(new Vector3(-w, 0.75f, end < 0 ? -l : l - 0.2f), new Vector3(w, floor - 0.2f, end < 0 ? -l + 0.2f : l));
        Coupler(k, -l, -1, 0.9f);
        Coupler(k, l, 1, 0.9f);
        CouplerPlates(k, shape);

        // Ladders: rungs, not slabs.
        foreach (var ladder in shape.Ladders)
            if (ladder.Foot.Y < 0.2)
                RungLadder(k, F(ladder.Foot), (float)ladder.Top, F(ladder.Inward), from: 0.35f);

        // Side-door steps: timber, stepped as the collision is.
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0);
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.Steps))
            k.Box(F(solid.Box.Min), F(solid.Box.Max), Kit.Faces.All & ~Kit.Faces.NegY);
        // The side doors' track over their openings, and the stops at the ends of their travel.
        if (sd > 0)
        {
            k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
            float lintel = (float)shape.DoorList[2].Box.Max.Y;
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (w + 0.06f);
                k.Box(new Vector3(x - 0.03f, lintel + 0.03f, -sd - 0.1f), new Vector3(x + 0.03f, lintel + 0.1f, sd * 3 + 0.1f));
                k.Box(new Vector3(x - 0.03f, floor - 0.08f, -sd - 0.1f), new Vector3(x + 0.03f, floor, sd * 3 + 0.1f));
            }
        }

        // The load: crates, stacked to the cargo's collision, a little irregular.
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.Cargo))
            CrateStack(k, F(solid.Box.Min), F(solid.Box.Max), variant);
        // The guard van's lockers: iron cabinets on the left wall.
        k.Use("paint_olive", Palette.MuddyOlive, 0.9f, 0.2f);
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.Locker))
        {
            k.Box(F(solid.Box.Min), F(solid.Box.Max));
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
            float xf = (float)solid.Box.Max.X + 0.005f;
            for (float z = (float)solid.Box.Min.Z + 0.5f; z < solid.Box.Max.Z; z += 0.5f)
                k.Box(new Vector3(xf - 0.01f, (float)solid.Box.Min.Y + 0.05f, z - 0.01f), new Vector3(xf + 0.01f, (float)solid.Box.Max.Y - 0.05f, z + 0.01f));
            k.Use("paint_olive", Palette.MuddyOlive, 0.9f, 0.2f);
        }
        if (guard)
        {
            // Lit windows along the sides (GDD §26: the crew car cramped and lamp-lit), and the stove's chimney.
            k.Use("window_lit", Palette.LampAmber, 0.2f, 0.4f, tile: 0.5f);
            foreach (int side in new[] { -1, 1 })
                foreach (float z in new[] { -l + 2.2f, 0f, l - 3.6f })
                {
                    float x = side * (w + 0.012f);
                    k.Panel(new Vector3(x, floor + 1.7f, z), new Vector3(side, 0, 0), Vector3.UnitY, 0.5f, 0.5f);
                    k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.3f);
                    k.Box(new Vector3(x - 0.03f, floor + 1.97f, z - 0.33f), new Vector3(x + 0.03f, floor + 2.05f, z + 0.33f));
                    k.Use("window_lit", Palette.LampAmber, 0.2f, 0.4f, tile: 0.5f);
                }
            k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.3f);
            k.Cylinder(new Vector3(w - 0.35f, h - 0.05f, -l + 2.6f), new Vector3(w - 0.35f, h + 0.7f, -l + 2.6f), 0.09f, 8);
            k.Lathe(new Vector3(w - 0.35f, h + 0.7f, -l + 2.6f), [new(0.16f, 0), new(0.16f, 0.08f), new(0.02f, 0.2f)], 8, smooth: false);
        }
        return k.Build($"car-{livery}-{(guard ? "guard" : "cargo")}-{variant}");
    }

    /// <summary>The plate over the coupling gap you cross on (spec B.4): an open grating, the ballast rushing under it.</summary>
    static void CouplerPlates(Kit k, CarShape shape)
    {
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.Coupler))
        {
            var (min, max) = (F(solid.Box.Min), F(solid.Box.Max));
            k.Use("steel_grate", Palette.IronGrey, 0.8f, 0.4f, tile: 0.8f);
            k.Box(min with { Y = max.Y - 0.03f }, max, Kit.Faces.PosY | Kit.Faces.NegY);
            k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
            foreach (float x in new[] { min.X, max.X - 0.04f })
                k.Box(new Vector3(x, min.Y, min.Z), new Vector3(x + 0.04f, max.Y - 0.03f, max.Z));
        }
    }

    static void BrakeWheel(Kit k, Vector3 at)
    {
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
        k.Rod(at, at + new Vector3(0, 0.45f, 0), 0.02f, 6);
        var c = at + new Vector3(0, 0.45f, 0);
        const int spokes = 5, n = 10;
        const float r = 0.26f;
        for (int i = 0; i < n; i++)
        {
            float a0 = i * MathF.Tau / n, a1 = (i + 1) * MathF.Tau / n;
            k.Rod(c + new Vector3(MathF.Cos(a0) * r, 0.02f, MathF.Sin(a0) * r), c + new Vector3(MathF.Cos(a1) * r, 0.02f, MathF.Sin(a1) * r), 0.016f);
        }
        for (int i = 0; i < spokes; i++)
        {
            float a = i * MathF.Tau / spokes;
            k.Rod(c, c + new Vector3(MathF.Cos(a) * r, 0.02f, MathF.Sin(a) * r), 0.012f);
        }
    }

    /// <summary>Crates filling a cargo volume, each a little off square, so the stack reads as stacked, not as one block.</summary>
    static void CrateStack(Kit k, Vector3 min, Vector3 max, int variant)
    {
        k.Use("wood_crate", Palette.TarnishedBrass * 0.8f, 0.8f, 0, tile: 0.6f);
        var size = max - min;
        int nx = Math.Max(1, (int)MathF.Round(size.X / 0.55f)), ny = Math.Max(1, (int)MathF.Round(size.Y / 0.65f)), nz = Math.Max(1, (int)MathF.Round(size.Z / 0.6f));
        var cell = new Vector3(size.X / nx, size.Y / ny, size.Z / nz);
        for (int x = 0; x < nx; x++)
            for (int y = 0; y < ny; y++)
                for (int z = 0; z < nz; z++)
                {
                    float jitter = MathF.Sin((x * 7 + y * 13 + z * 5 + variant) * 1.7f);
                    var c0 = min + cell * new Vector3(x, y, z) + new Vector3(0.015f, 0, 0.015f);
                    var c1 = min + cell * new Vector3(x + 1, y + 1, z + 1) - new Vector3(0.015f, 0.01f, 0.015f);
                    // Inset along the aisle side a touch, varied, so the face isn't one plane.
                    c0.X += MathF.Max(0, jitter) * 0.04f;
                    k.Tint = Vector3.One * (0.8f + 0.2f * MathF.Abs(jitter));
                    k.Box(c0, c1, Kit.Faces.All & ~Kit.Faces.NegY);
                }
    }

    /// <summary>A door leaf of <paramref name="size"/> (centred): planks with iron straps, an end door with a small window.</summary>
    public static MeshAsset Door(Look? look, Vector3 size, bool side)
    {
        var k = new Kit(look, side ? 31 : 37);
        var half = size / 2;
        k.Use("wood_siding", Palette.DeepBrown, 0.9f, 0, 1);
        k.Box(-half, half);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        // Straps across the face, both faces.
        bool alongZ = size.Z > size.X;
        foreach (float y in new[] { -half.Y + 0.3f, 0, half.Y - 0.3f })
        {
            var e = alongZ ? new Vector3(half.X + 0.012f, 0.04f, half.Z) : new Vector3(half.X, 0.04f, half.Z + 0.012f);
            k.BoxAt(new Vector3(0, y, 0), e);
        }
        if (side)
        {
            // The Z brace of a boxcar door.
            var a = new Vector3(0, half.Y - 0.3f, -half.Z + 0.1f);
            var b = new Vector3(0, -half.Y + 0.3f, half.Z - 0.1f);
            k.Rod(a + new Vector3(half.X + 0.01f, 0, 0), b + new Vector3(half.X + 0.01f, 0, 0), 0.035f);
            k.Rod(a - new Vector3(half.X + 0.01f, 0, 0), b - new Vector3(half.X + 0.01f, 0, 0), 0.035f);
        }
        else
        {
            k.Use("glass_dirty", Palette.BlueGrey, 0.3f, 0.8f, tile: 0.4f);
            var wy = half.Y - 0.35f;
            k.Panel(new Vector3(0, wy, -half.Z - 0.005f), -Vector3.UnitZ, Vector3.UnitY, 0.4f, 0.36f);
            k.Panel(new Vector3(0, wy, half.Z + 0.005f), Vector3.UnitZ, Vector3.UnitY, 0.4f, 0.36f);
        }
        return k.Build(side ? "door-side" : "door-end");
    }

    /// <summary>
    /// A mounted gun on its pedestal (GDD §26: "mounted weapon silhouette, firing arc implied"): water-jacketed barrel,
    /// a shield plate with a sighting slot, the ammunition box, the spade grips. It faces −Z; the scene turns it.
    /// </summary>
    public static MeshAsset Gun(Look? look)
    {
        var k = new Kit(look, 53);
        k.Use("paint_olive", Palette.MuddyOlive, 0.9f, 0.3f);
        k.Cylinder(new Vector3(0, -0.9f, 0), new Vector3(0, -0.35f, 0), 0.16f, 8);
        k.Lathe(new Vector3(0, -0.4f, 0), [new(0.26f, 0), new(0.26f, 0.08f), new(0.14f, 0.16f)], 10, smooth: false);
        k.Use("wheel_iron", Palette.SootBlack, 0.6f, 0.6f);
        k.Box(new Vector3(-0.12f, -0.2f, -0.35f), new Vector3(0.12f, 0.08f, 0.35f));
        k.Use("paint_olive", Palette.MuddyOlive, 0.8f, 0.3f);
        k.Cylinder(new Vector3(0, -0.02f, -0.3f), new Vector3(0, -0.02f, -1.0f), 0.1f, 10);
        k.Use("wheel_iron", Palette.SootBlack, 0.5f, 0.7f);
        k.Cylinder(new Vector3(0, -0.02f, -1.0f), new Vector3(0, -0.02f, -1.45f), 0.035f, 8);
        k.Lathe(new Vector3(0, -0.02f, -1.4f), [new(0.05f, 0), new(0.05f, 0.1f)], 8, smooth: true);
        // The shield: an angled plate, with a slot to aim through.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        k.With(Matrix4x4.CreateRotationX(-0.15f) * Kit.At(0, 0.02f, -0.45f), () =>
        {
            k.Box(new Vector3(-0.48f, -0.35f, -0.02f), new Vector3(-0.08f, 0.32f, 0.02f));
            k.Box(new Vector3(0.08f, -0.35f, -0.02f), new Vector3(0.48f, 0.32f, 0.02f));
            k.Box(new Vector3(-0.08f, 0.08f, -0.02f), new Vector3(0.08f, 0.32f, 0.02f));
            k.Box(new Vector3(-0.08f, -0.35f, -0.02f), new Vector3(0.08f, -0.08f, 0.02f));
        });
        k.Use("paint_olive", Palette.MuddyOlive, 0.9f, 0.3f);
        k.Box(new Vector3(0.14f, -0.22f, -0.2f), new Vector3(0.36f, 0.0f, 0.1f));
        k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
        foreach (int s in new[] { -1, 1 })
            k.Rod(new Vector3(s * 0.1f, -0.05f, 0.35f), new Vector3(s * 0.16f, -0.05f, 0.5f), 0.02f);
        return k.Build("gun");
    }
}
