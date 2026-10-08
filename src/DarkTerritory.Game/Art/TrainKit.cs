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

    /// <summary>A modelled piece (tools/models car_gear) appended at <paramref name="at"/>, when the look has it.</summary>
    /// <summary>
    /// The engine's main rod, a unit of it: from its big end at the origin back along +Z to its little end
    /// <paramref name="length"/> away (SceneArt.Gear lays it from the crank pin to the crosshead each frame).
    /// </summary>
    public static MeshAsset MainRod(Look? look, float length)
    {
        var k = new Kit(look, 141);
        k.Use("wheel_iron", Palette.IronGrey, 0.5f, 0.7f);
        k.Rod(Vector3.Zero, new Vector3(0, 0, length), 0.045f);
        // The big end's strap round the pin, and the little end's eye at the crosshead.
        k.Cylinder(new Vector3(-0.05f, 0, 0), new Vector3(0.05f, 0, 0), 0.075f, 10);
        k.Cylinder(new Vector3(-0.04f, 0, length), new Vector3(0.04f, 0, length), 0.06f, 8);
        return k.Build("engine-main-rod");
    }

    static bool Prop(Kit k, string name, Matrix4x4 at)
    {
        if (k.Look is not { } look || PropArt.Of(look).Get(name) is not { } piece)
            return false;
        k.Append(piece, at);
        return true;
    }

    /// <summary>A freight bogie (arch-bar truck): side frames, bolster, springs, two axles.</summary>
    static void Truck(Kit k, float z)
    {
        if (Prop(k, "truck_archbar", Kit.At(0, 0, z)))
            return;
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

    /// <summary>
    /// A knuckle coupler and its draft gear, out from the end beam at <paramref name="z"/> towards <paramref name="dir"/> (±1).
    /// Where the modelled ones are built (tools/models car_gear: coupler_knuckle, coupler_open) they're not baked in: the
    /// scene draws each end's per frame, shut or cut (<see cref="CouplerEnds"/>, SceneArt.Car), so a cut shows.
    /// </summary>
    static void Coupler(Kit k, float z, float dir, float height)
    {
        if (k.Look is { } look && PropArt.Of(look).Get("coupler_knuckle") is not null)
            return;
        k.Use("wheel_iron", Palette.SootBlack, 0.7f, 0.35f);
        float z0 = z, z1 = z + dir * 0.35f;
        k.Box(new Vector3(-0.09f, height - 0.08f, MathF.Min(z0, z1)), new Vector3(0.09f, height + 0.08f, MathF.Max(z0, z1)));
        k.Box(new Vector3(-0.16f, height - 0.14f, MathF.Min(z1, z1 + dir * 0.18f)), new Vector3(0.16f, height + 0.14f, MathF.Max(z1, z1 + dir * 0.18f)));
        // The air hose, hanging.
        k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.1f);
        k.Rod(new Vector3(0.35f, height - 0.05f, z + dir * 0.05f), new Vector3(0.38f, height - 0.45f, z + dir * 0.25f), 0.025f, 6);
    }

    /// <summary>How far apart a ladder's rungs are (also a climber's hand-over-hand: GameAudio's rung cues).</summary>
    public const float RungPitch = 0.3f;

    /// <summary>
    /// Where a vehicle's two couplers stand (its frame, 0.9 m up): the front's and the rear's along it, and the way each
    /// points (±1, the modelled one pointing −Z, turned for the rear). The engine's front one stands a little proud of the
    /// pilot beam.
    /// </summary>
    public static (Matrix4x4 Front, Matrix4x4 Rear) CouplerEnds(CarShape shape)
    {
        float l = (float)shape.HalfLength;
        float front = shape.Cab is not null ? -l - 0.05f : -l;
        return (Matrix4x4.CreateTranslation(0, 0, front), Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(0, 0, l));
    }

    /// <summary>An iron ladder up a face: two stiles and rungs every <see cref="RungPitch"/>, standing off it by a hand's depth.</summary>
    public static void RungLadder(Kit k, Vector3 foot, float top, Vector3 inward, float from = 0.2f)
    {
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, inward));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        float half = 0.2f;
        foreach (int s in new[] { -1, 1 })
            k.Box(foot + across * (s * half) - new Vector3(0.022f, -from, 0.022f), foot + across * (s * half) + new Vector3(0.022f, top, 0.022f));
        for (float y = from + 0.25f; y < top - 0.05f; y += RungPitch)
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

    /// <summary>The drivers' radius (m), and their places along the engine (its frame's Z), front to back.</summary>
    public const float DriverRadius = 0.7f;

    /// <summary>
    /// The four pairs of drivers (note 276): under the boiler, from just behind the cab, so its weight's over them and the
    /// cab rides on the pilot truck ahead.
    /// </summary>
    public static float[] Drivers(CarShape shape)
    {
        float b = (float)shape.Cab!.Value.Max.Z;
        return [b + 1.4f, b + 3.0f, b + 4.6f, b + 6.2f];
    }

    /// <summary>A side's crank angle at rest (rad): the pins a quarter turn apart side to side, so it never stops on a dead centre.</summary>
    public static float CrankPhase(int side) => side < 0 ? 0.4f : 0.4f + MathF.PI / 2;

    /// <summary>
    /// A modelled driver (tools/models engine_parts, its pin towards +Z) turned to its side's crank at <paramref name="turn"/>
    /// (rad, the wheels' roll: distance over <see cref="DriverRadius"/>), at <paramref name="at"/> (and, on the left, round to face out).
    /// </summary>
    public static Matrix4x4 DriverAt(int side, Vector3 at, float turn) => side > 0
        ? Matrix4x4.CreateRotationX(-(CrankPhase(side) + turn)) * Kit.At(at)
        : Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateRotationX(MathF.PI - (CrankPhase(side) + turn)) * Kit.At(at);

    /// <summary>A side's crank pin off its driver's centre at <paramref name="turn"/> (rad).</summary>
    public static Vector3 CrankPin(int side, float turn) =>
        new Vector3(0, MathF.Sin(CrankPhase(side) + turn), MathF.Cos(CrankPhase(side) + turn)) * 0.3f;

    /// <summary>Where a side's rods run (x), and the crosshead's height and its place at the rest crank (frame Z): ahead of the drivers.</summary>
    public static float RodX(int side) => side * (HalfGauge + 0.2f) + side * 0.06f;
    public const float CrossheadY = 0.95f;
    public static float CrossheadRestZ(CarShape shape) => Drivers(shape)[0] - 0.8f;

    /// <summary>The steam cylinders' middle along the engine: ahead of the drivers, under the cab's back.</summary>
    public static float CylinderZ(CarShape shape) => Drivers(shape)[0] - 2.15f;

    /// <summary>How high over the rail the headlamp's lens is: on the cab's nose under the front windows (note 276).</summary>
    public static float HeadlampY => (float)Sim.World.LampHeight;

    /// <summary>
    /// The engine, cab forward (ARCHITECTURE §8 note 276, the director's sketch): an armoured 2-8-0 run cab first. The cab
    /// leads over the pilot and its plough, the lamp in an armoured box on its nose under the front windows like an eye;
    /// behind it the boiler, cased in riveted plate (an octagon, so it reads as heavy and hand-made), runs back to the
    /// smokebox and the tapered funnel at the rear, where car 1 couples on. The coal is in the cab, in a bunker by the fire.
    /// </summary>
    /// <param name="gear">With its drivers and rods in it (a still engine: the catalog's, a wreck's). Without, they're
    /// drawn apart each frame turning with the train's going (SceneArt.Gear).</param>
    public static MeshAsset Engine(Look? look, CarShape shape, int variant, bool gear = true)
    {
        var k = new Kit(look, 101 + variant);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength;
        var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
        var stack = shape.Solids.First(s => s.Part == PartKind.Stack).Box;
        var roof = shape.Solids.First(s => s.Part == PartKind.CabRoof).Box;
        var cab = shape.Cab!.Value;
        float deck = (float)boiler.Min.Y, top = (float)boiler.Max.Y, bw = (float)boiler.Max.X;
        float cabFront = (float)cab.Min.Z, cabBack = (float)cab.Max.Z;
        float boilerBack = (float)boiler.Max.Z;
        float roofLow = (float)roof.Min.Y, roofTop = (float)roof.Max.Y;

        // Running gear: the pilot truck under the cab, the steam cylinders behind it, four drivers under the boiler, and
        // the trailing truck under the smokebox.
        const float driverR = DriverRadius;
        float[] drivers = Drivers(shape);
        static float Phase(int side) => CrankPhase(side);
        foreach (float z in drivers)
        {
            bool modelled = true;
            foreach (int side in new[] { -1, 1 })
                modelled &= gear ? Prop(k, "driver_wheel", DriverAt(side, new Vector3(side * HalfGauge, driverR, z), 0))
                    : k.Look is { } lk && PropArt.Of(lk).Get("driver_wheel") is not null;
            if (!modelled)
                Axle(k, z, driverR, 12, driver: true);
            else
            {
                k.Use("wheel_iron", Palette.SootBlack, 0.6f, 0.4f);
                k.Cylinder(new Vector3(-HalfGauge + 0.05f, driverR, z), new Vector3(HalfGauge - 0.05f, driverR, z), 0.07f, 8);
            }
        }
        foreach (float z in new[] { -l + 1.55f, l - 1.4f })
        {
            bool modelled = true;
            foreach (int side in new[] { -1, 1 })
                modelled &= Prop(k, "pilot_wheel", (side > 0 ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI)) * Kit.At(side * HalfGauge, 0.4f, z));
            if (!modelled)
                Axle(k, z, 0.4f, 8);
            else
            {
                k.Use("wheel_iron", Palette.SootBlack, 0.6f, 0.4f);
                k.Cylinder(new Vector3(-HalfGauge + 0.05f, 0.4f, z), new Vector3(HalfGauge - 0.05f, 0.4f, z), 0.06f, 8);
            }
        }
        // The trailing truck's second axle, and the truck's frame over both.
        Axle(k, l - 2.5f, 0.45f, 0);
        k.Use("wheel_iron", Palette.SootBlack, 0.8f, 0.3f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side * (HalfGauge + 0.12f) - 0.04f, 0.3f, l - 2.9f), new Vector3(side * (HalfGauge + 0.12f) + 0.04f, 0.62f, l - 1.0f));
        // Frames: deep plates inside the wheels.
        k.Use("wheel_iron", Palette.SootBlack, 0.8f, 0.3f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side * 0.62f - 0.04f, 0.4f, -l + 0.8f), new Vector3(side * 0.62f + 0.04f, deck - 0.06f, l - 0.6f));
        // Rods: the coupling rod across all four crank pins, and the main rod from the crosshead to the third driver.
        k.Use("wheel_iron", Palette.IronGrey, 0.5f, 0.7f);
        float cyl = CylinderZ(shape);
        foreach (int side in new[] { -1, 1 })
        {
            float phase = Phase(side);
            var pin = new Vector3(0, MathF.Sin(phase), MathF.Cos(phase)) * 0.3f;
            float x = side * (HalfGauge + 0.2f);
            var rodAt = new Vector3(x, driverR + pin.Y, (drivers[0] + drivers[^1]) / 2 + pin.Z);
            if (gear)
            {
                if (!Prop(k, "coupling_rod", (side > 0 ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI)) * Kit.At(rodAt)))
                    k.Box(new Vector3(x - 0.03f, driverR + pin.Y - 0.06f, drivers[0] + pin.Z - 0.1f), new Vector3(x + 0.03f, driverR + pin.Y + 0.06f, drivers[^1] + pin.Z + 0.1f));
                k.Use("wheel_iron", Palette.IronGrey, 0.5f, 0.7f);
                var crosshead = new Vector3(x + side * 0.06f, CrossheadY, CrossheadRestZ(shape));
                k.Rod(crosshead, new Vector3(x + side * 0.06f, driverR + pin.Y, drivers[2] + pin.Z), 0.045f);
            }
            // Crosshead guides and the cylinder, with its drain cocks.
            if (Prop(k, side > 0 ? "cylinder_r" : "cylinder_l", Kit.At(side * 1.12f, 0.98f, cyl)))
                continue;
            k.Box(new Vector3(x + side * 0.06f - 0.03f, 0.88f, cyl + 0.55f), new Vector3(x + side * 0.06f + 0.03f, 1.02f, cyl + 1.75f));
            k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
            var cylA = new Vector3(side * 1.12f, 0.98f, cyl - 0.55f);
            var cylB = new Vector3(side * 1.12f, 0.98f, cyl + 0.55f);
            k.Cylinder(cylA, cylB, 0.34f, 12);
            k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
            k.Cylinder(cylA - new Vector3(0, 0, 0.04f), cylA + new Vector3(0, 0, 0.02f), 0.36f, 12);
            k.Use("wheel_iron", Palette.IronGrey, 0.5f, 0.7f);
        }

        // The prow (note 311): one raked, curved shell of armour from the plough at the rail up to the cab's window sills.
        Prow(k, w, l, cabFront);

        // The deck down either side of the boiler behind the cab (the cab's floor is its own; the prow covers the pilot's), and
        // a skirt of armour down over the wheels from the cab's doorways back, bellied, its foot swept up at the ends.
        k.Use("paint_black", Palette.SootBlack, 0.8f, 0.3f);
        k.Box(new Vector3(-w, deck - 0.06f, cabBack), new Vector3(w, deck, l - 0.05f), Kit.Faces.All);
        Skirt(k, w, l, deck, DoorFront(shape, deck, cabBack));

        // The armoured boiler casing, from the cab's back wall to the smokebox (note 338; the director, 7 Oct: "I dont like
        // the boiler being rounded, lets make it squared up a bit and feel more armoured"): a box of plate the boiler's
        // width, its top corners cut (an armoured car's chamfer, and the boiler top still walked on), banded at the plate
        // seams, each side's plates braced corner to corner as a war train's are, and a riveted belt along the chamfer.
        Vector2[] casing = Octagon(-bw, deck + 0.02f, bw, top, CasingBevel, 0);
        float smokebox = boilerBack - 0.2f;
        Plate(k, 1.5f);
        k.Prism(casing, cabBack, smokebox - 1.2f, caps: false, smooth: false);
        Rusted(k);
        Vector2[] band = Octagon(-bw - 0.025f, deck + 0.02f, bw + 0.025f, top + 0.025f, CasingBevel + 0.01f, 0);
        var seams = new List<float>();
        for (float z = cabBack + 0.25f; z < smokebox - 1.3f; z += CasingPanel)
            seams.Add(z);
        seams.Add(smokebox - 1.3f);
        foreach (float z in seams)
            k.Prism(band, z, z + 0.1f, caps: false, smooth: false);
        Rusted(k).Shade(0.6f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (bw + 0.012f), y0 = deck + 0.18f, y1 = top - CasingBevel - 0.08f;
            for (int i = 0; i + 1 < seams.Count; i++)
            {
                float z0 = seams[i] + 0.16f, z1 = seams[i + 1] - 0.06f;
                Strap(k, new Vector3(x, y0, z0), new Vector3(x, y1, z1), new Vector3(side, 0, 0), 0.07f);
                Strap(k, new Vector3(x, y1, z0), new Vector3(x, y0, z1), new Vector3(side, 0, 0), 0.07f);
            }
            // The belt along the chamfer's foot: the casing's hard line from the cab back.
            k.Box(new Vector3(MathF.Min(x, side * (bw - 0.02f)), top - CasingBevel - 0.05f, cabBack), new Vector3(MathF.Max(x, side * (bw - 0.02f)) + 0.01f, top - CasingBevel + 0.01f, smokebox - 1.2f),
                Kit.Faces.All & ~Kit.Faces.NegZ & ~Kit.Faces.PosZ);
        }
        // The smokebox at the rear, in its sooted iron, under the stack; its end a flat armour face with the door's ring on
        // it, looking back down the train.
        k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.3f);
        k.Prism(casing, smokebox - 1.2f, boilerBack, caps: true, smooth: false);
        float doorY = deck + (top - deck) * 0.45f;
        var turned = Matrix4x4.CreateRotationY(MathF.PI);
        if (!Prop(k, "smokebox_door", turned * Kit.At(0, doorY, boilerBack)))
        {
            k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.4f);
            k.Cylinder(new Vector3(0, doorY, boilerBack), new Vector3(0, doorY, boilerBack + 0.1f), 0.5f, 14);
            k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
            k.Cylinder(new Vector3(0, doorY, boilerBack + 0.1f), new Vector3(0, doorY, boilerBack + 0.2f), 0.07f, 8);
            k.Rod(new Vector3(-0.3f, doorY, boilerBack + 0.16f), new Vector3(0.3f, doorY, boilerBack + 0.16f), 0.018f);
        }

        // The hood's back over the smokebox (note 338): plate from the boiler's top up to the roof, between the corridors'
        // back ends.
        {
            float hoodBack = (float)shape.Solids.Where(x => x.Part == PartKind.CabRoof).Max(x => x.Box.Max.Z);
            Plate(k);
            k.Box(new Vector3(-bw, top, hoodBack - 0.15f), new Vector3(bw, roofLow, hoodBack));
        }

        // The headlamp (note 311): a round, hooded eye in the prow, caged, where World puts the lamp's light.
        Headlamp(k, l);

        // The stack: a tapered funnel with a flared lip, soot-black inside, over the smokebox.
        {
            var c = new Vector3(0, top - 0.05f, (float)stack.Centre.Z);
            float r = (float)stack.HalfSize.X + 0.05f;
            k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.3f);
            k.Lathe(c, [new(r + 0.12f, 0), new(r, 0.18f), new(r - 0.06f, (float)stack.Max.Y - c.Y - 0.2f), new(r + 0.06f, (float)stack.Max.Y - c.Y - 0.05f), new(r + 0.06f, (float)stack.Max.Y - c.Y), new(r - 0.1f, (float)stack.Max.Y - c.Y)], 10, smooth: false, capTop: false);
            k.Shade(0.15f);
            k.Disc(new Vector3(0, (float)stack.Max.Y - 0.12f, c.Z), Vector3.UnitY, r - 0.08f, 10);
        }
        // Steam dome and sand dome, low (the boiler top is walkable), over the drivers; the whistle and safety valves just
        // behind the cab.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        var sandDome = new Vector3(0, top - 0.08f, (drivers[0] + drivers[1]) / 2);
        var steamDome = new Vector3(0, top - 0.08f, (drivers[2] + drivers[3]) / 2);
        if (!Prop(k, "steam_dome", Kit.At(steamDome)))
            k.Lathe(steamDome, [new(0.5f, 0), new(0.42f, 0.2f), new(0.3f, 0.36f), new(0, 0.42f)], 10, smooth: true);
        if (!Prop(k, "sand_dome", Kit.At(sandDome)))
            k.Lathe(sandDome, [new(0.4f, 0), new(0.32f, 0.16f), new(0.2f, 0.27f), new(0, 0.3f)], 10, smooth: true);
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
        foreach (float dx in new[] { -0.12f, 0.12f })
            k.Lathe(new Vector3(dx, roofTop - 0.05f, SafetyValveZ(shape)), [new(0.07f, 0), new(0.06f, 0.2f), new(0.09f, 0.22f), new(0.02f, 0.3f)], 8);
        k.Lathe(new Vector3(0.35f, roofTop - 0.1f, WhistleZ(shape)), [new(0.04f, 0), new(0.04f, 0.25f), new(0.08f, 0.28f), new(0.08f, 0.55f), new(0.04f, 0.6f)], 8);
        // Handrails along the casing, on stanchions.
        k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (bw + 0.08f), y = deck + (top - deck) * 0.62f;
            k.Rod(new Vector3(x, y, cabBack + 0.2f), new Vector3(x, y, boilerBack - 0.3f), 0.018f, 6);
            for (float z = cabBack + 0.4f; z < boilerBack; z += 1.8f)
                k.Rod(new Vector3(x, y, z), new Vector3(side * (bw - 0.02f), y, z), 0.014f);
        }
        // Sand pipes from the sand dome down to the rails ahead of the drivers.
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.7f, 0.5f);
        foreach (int side in new[] { -1, 1 })
            k.Rod(new Vector3(side * (bw * 0.6f), top - 0.25f, sandDome.Z), new Vector3(side * (HalfGauge + 0.1f), 0.25f, drivers[0] - 0.8f), 0.02f, 5);

        Cab(k, shape, w, deck, cabFront, cabBack, bw, top, roofLow, roofTop);
        Bunker(k, shape, deck, roofLow, roofTop, w);
        RunningBoards(k, shape);
        CouplerPlates(k, shape);
        RearEnd(k, shape, w, l, deck, boilerBack);
        return k.Build($"engine-{variant}");
    }

    /// <summary>
    /// The engine's dressing (note 338; the director's reference, a war train's engine: "the train isnt looking even close to
    /// this"): drawn over <see cref="Engine"/> in its frame and casting no shadow (MeshInstance.Shadowless), so it costs the
    /// frame once, not three times. The plough out ahead in a V of heavy slats, raked from the rail up over the buffer
    /// beam; armoured housings over the domes, the cylinders and the stack's foot; pipes down the boiler's flanks; air
    /// tanks and a tool chest under the running boards; bars over the front windows; ammunition boxes on the cab roof's
    /// back corners. Nothing here is walked on or stood in: the sim's engine is <see cref="Engine"/>'s.
    /// </summary>
    public static MeshAsset EngineDress(Look? look, CarShape shape)
    {
        var k = new Kit(look, 141);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength;
        var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
        var stack = shape.Solids.First(s => s.Part == PartKind.Stack).Box;
        var roof = shape.Solids.First(s => s.Part == PartKind.CabRoof).Box;
        var cab = shape.Cab!.Value;
        float deck = (float)boiler.Min.Y, top = (float)boiler.Max.Y, bw = (float)boiler.Max.X;
        float cabFront = (float)cab.Min.Z, cabBack = (float)cab.Max.Z, boilerBack = (float)boiler.Max.Z;
        float roofTop = (float)roof.Max.Y;
        float[] drivers = Drivers(shape);

        // The plough: tall and steep, from the rail up over the buffer beam to the nose's foot. A broad face across the
        // front and a cheek either side angled back to the engine's corners, all of it in heavy upright slats with dark
        // gaps between, each slat's edges standing proud; two bands across and a rail-iron stem at each fold.
        float reach = PloughAhead, foot = 0.06f, crest = PloughCrest, half = PloughFront;
        // A point on the plough's face: across from −1 (the left corner) to 1, up from the foot to the crest.
        Vector3 Plough(float across, float y)
        {
            float f = (y - foot) / (crest - foot);
            float zFront = -l - reach + (reach - 0.08f) * f;
            float ax = MathF.Abs(across);
            // Across the front face to ±half, then the cheek back to the engine's side.
            float x = ax <= 0.5f ? across * 2 * half : MathF.Sign(across) * (half + (w + 0.08f - half) * (ax - 0.5f) * 2);
            float back = ax <= 0.5f ? 0 : (ax - 0.5f) * 2 * (-l + 0.5f - zFront);
            return new Vector3(x, y, zFront + back);
        }
        // The dark backing behind the slats.
        Plate(k).Shade(0.3f);
        for (int i = 0; i < 4; i++)
        {
            float a0 = -1 + i * 0.5f, a1 = a0 + 0.5f;
            var off = new Vector3(0, 0, 0.1f);
            k.Quad(Plough(a0, foot) + off, Plough(a1, foot) + off, Plough(a1, crest) + off, Plough(a0, crest) + off, twoSided: true);
        }
        const int slats = 14;
        for (int i = 0; i < slats; i++)
        {
            float a0 = -1 + 2f * (i + 0.07f) / slats, a1 = -1 + 2f * (i + 0.86f) / slats;
            // (A slat lies on one face: never across a fold.)
            if (MathF.Abs(a0) < 0.5f != MathF.Abs(a1) < 0.5f)
                (a0, a1) = MathF.Abs(a0) < 0.5f ? (a0, MathF.Sign(a1) * 0.499f) : (MathF.Sign(a0) * 0.501f, a1);
            var p0 = Plough(a0, foot);
            var p1 = Plough(a1, foot);
            var p2 = Plough(a1, crest);
            var p3 = Plough(a0, crest);
            var n = Vector3.Normalize(Vector3.Cross(p1 - p0, p3 - p0));
            if (n.Z > 0)
                n = -n;
            var t = n * 0.08f;
            (i % 4 == 1 ? Iron(k, 0.9f) : Plate(k, 0.9f)).Shade(0.85f + 0.15f * (i % 2));
            k.Quad(p0 + t, p1 + t, p2 + t, p3 + t, twoSided: true);
            k.Quad(p0, p1, p1 + t, p0 + t, twoSided: true);
            k.Quad(p1, p2, p2 + t, p1 + t, twoSided: true);
            k.Quad(p3, p0, p0 + t, p3 + t, twoSided: true);
            k.Quad(p2, p3, p3 + t, p2 + t, twoSided: true);
        }
        // Bands across, over the slats; a stem at each fold; a heavy lip along the foot.
        Rusted(k).Shade(0.55f);
        foreach (float y in new[] { foot + 0.35f, crest - 0.35f })
            for (int i = 0; i < 4; i++)
            {
                float a0 = -1 + i * 0.5f, a1 = a0 + 0.5f;
                var p = Plough(a0, y);
                var q = Plough(a1, y);
                var n = Vector3.Normalize(Vector3.Cross(q - p, Vector3.UnitY));
                if (n.Z > 0)
                    n = -n;
                var lift = n * 0.1f;
                k.Quad(p + lift + new Vector3(0, 0.06f, 0), q + lift + new Vector3(0, 0.06f, 0), q + lift - new Vector3(0, 0.06f, 0), p + lift - new Vector3(0, 0.06f, 0), twoSided: true);
            }
        foreach (float a0 in new[] { -0.5f, 0.5f })
            k.Rod(Plough(a0, foot) + new Vector3(0, 0, -0.1f), Plough(a0, crest) + new Vector3(0, 0, -0.1f), 0.06f);
        for (int i = 0; i < 4; i++)
        {
            float a0 = -1 + i * 0.5f, a1 = a0 + 0.5f;
            k.Rod(Plough(a0, foot + 0.04f) + new Vector3(0, 0, -0.06f), Plough(a1, foot + 0.04f) + new Vector3(0, 0, -0.06f), 0.06f);
        }
        // Its back corners closed to the engine's sides.
        Plate(k);
        foreach (int side in new[] { -1, 1 })
            k.Quad(Plough(side, foot), new Vector3(side * (w + 0.08f), foot, -l + 1.0f), new Vector3(side * (w + 0.08f), crest, -l + 1.0f), Plough(side, crest), twoSided: true);

        // Bars over the front windows, an armoured cab's grille: three uprights and a rail across each.
        Rusted(k).Shade(0.5f);
        foreach (int side in new[] { -1, 1 })
        {
            var win = FrontWindow(shape, side);
            float z = cabFront - 0.04f;
            for (int i = 1; i <= 3; i++)
            {
                float x = win.X0 + (win.X1 - win.X0) * i / 4;
                k.Box(new Vector3(x - 0.02f, win.Y0, z - 0.03f), new Vector3(x + 0.02f, win.Y1, z), Kit.Faces.All & ~Kit.Faces.PosZ);
            }
            float ym = win.Y0 + (win.Y1 - win.Y0) * 0.55f;
            k.Box(new Vector3(win.X0, ym - 0.02f, z - 0.05f), new Vector3(win.X1, ym + 0.02f, z - 0.03f), Kit.Faces.All & ~Kit.Faces.PosZ);
            // An armoured visor over the window's top third, propped out (the driver's eye is under it).
            float yv = win.Y1 - (win.Y1 - win.Y0) / 3;
            Plate(k, 1.0f);
            k.Quad(new Vector3(win.X0 - 0.04f, win.Y1 + 0.02f, z), new Vector3(win.X1 + 0.04f, win.Y1 + 0.02f, z),
                new Vector3(win.X1 + 0.04f, yv, z - 0.16f), new Vector3(win.X0 - 0.04f, yv, z - 0.16f), twoSided: true);
            Rusted(k).Shade(0.5f);
        }

        // The hood (note 338): its plate braced corner to corner in panels between seam straps, both sides; on its roof,
        // armoured lockers along the chamfers either side of the roof walk, and the stack's foot cased square.
        float hoodBack = (float)shape.Solids.Where(x => x.Part == PartKind.CabRoof).Max(x => x.Box.Max.Z);
        float roofLow = (float)roof.Min.Y;
        {
            var seams = new List<float>();
            for (float z = cabBack + 0.05f; z < hoodBack - 0.4f; z += 1.6f)
                seams.Add(z);
            seams.Add(hoodBack - 0.05f);
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * w;
                Rusted(k).Shade(0.55f);
                foreach (float z in seams)
                    k.Box(new Vector3(MathF.Min(x, x + side * 0.03f), deck, z - 0.05f), new Vector3(MathF.Max(x, x + side * 0.03f), roofLow, z + 0.05f));
                foreach (float y in new[] { deck + 0.06f, roofLow - 0.08f })
                    k.Box(new Vector3(MathF.Min(x, x + side * 0.03f), y - 0.04f, cabBack), new Vector3(MathF.Max(x, x + side * 0.03f), y + 0.04f, hoodBack));
                Rusted(k).Shade(0.45f);
                for (int i = 0; i + 1 < seams.Count; i++)
                {
                    float z0 = seams[i] + 0.12f, z1 = seams[i + 1] - 0.12f;
                    Strap(k, new Vector3(x, deck + 0.15f, z0), new Vector3(x, roofLow - 0.15f, z1), new Vector3(side, 0, 0), 0.09f);
                    Strap(k, new Vector3(x, roofLow - 0.15f, z0), new Vector3(x, deck + 0.15f, z1), new Vector3(side, 0, 0), 0.09f);
                }
            }
        }
        foreach (var (z, len, side) in new[] { (cabBack + 1.4f, 1.1f, 1), (cabBack + 3.3f, 1.5f, -1), (cabBack + 5.6f, 1.2f, 1), (hoodBack - 2.6f, 1.4f, -1) })
        {
            float x = side * (w - 0.38f), y = RoofProfile((w - 0.38f) / (w + 0.12f), roofTop, roofLow);
            Plate(k, 1.0f).Shade(0.9f);
            k.BevelBox(new Vector3(x - 0.3f, y - 0.06f, z - len / 2), new Vector3(x + 0.3f, y + 0.42f, z + len / 2), 0.06f);
            Rusted(k).Shade(0.5f);
            k.Box(new Vector3(x - 0.32f, y + 0.18f, z - len / 2 - 0.02f), new Vector3(x + 0.32f, y + 0.24f, z + len / 2 + 0.02f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        {
            var c = (float)stack.Centre.Z;
            float r = (float)stack.HalfSize.X + 0.22f;
            Plate(k, 1.1f);
            k.BevelBox(new Vector3(-r, roofTop - 0.05f, c - r), new Vector3(r, roofTop + 0.45f, c + r), 0.08f);
            Rusted(k).Shade(0.6f);
            k.Box(new Vector3(-r - 0.02f, roofTop + 0.3f, c - r - 0.02f), new Vector3(r + 0.02f, roofTop + 0.38f, c + r + 0.02f), Kit.Faces.All & ~Kit.Faces.NegY & ~Kit.Faces.PosY);
        }

        // Pipes along each side of the hood: a big feed pipe high, turning down at the back to the gear, and a lesser one
        // low, on brackets off the plate.
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.9f, 0.4f);
        k.Tint *= new Vector3(0.5f, 0.38f, 0.32f);
        float smokebox = boilerBack - 0.2f;
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w + 0.12f), y = deck + 1.55f;
            k.Cylinder(new Vector3(x, y, cabBack + 0.2f), new Vector3(x, y, hoodBack - 0.9f), 0.08f, 8);
            k.Cylinder(new Vector3(x, y, hoodBack - 0.9f), new Vector3(x, deck + 0.05f, hoodBack - 0.4f), 0.08f, 8);
            foreach (float z in new[] { cabBack + 0.2f, cabBack + 3.4f, hoodBack - 0.9f })
                k.Cylinder(new Vector3(x, y, z - 0.07f), new Vector3(x, y, z + 0.07f), 0.11f, 8);
            k.Cylinder(new Vector3(side * (w + 0.08f), deck + 0.45f, cabBack + 0.3f), new Vector3(side * (w + 0.08f), deck + 0.45f, smokebox - 1.0f), 0.045f, 6);
        }

        // The cylinders cased in armour: a riveted box over each, its front cut back at a slant.
        float cyl = CylinderZ(shape);
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side * 0.82f, x1 = side * (w + 0.12f);
            Plate(k, 1.1f);
            k.BevelBox(new Vector3(MathF.Min(x0, x1), 0.5f, cyl - 0.8f), new Vector3(MathF.Max(x0, x1), 1.42f, cyl + 0.8f), 0.12f);
            Rusted(k).Shade(0.6f);
            k.Box(new Vector3(MathF.Min(x1, x1 + side * 0.03f), 0.55f, cyl - 0.02f), new Vector3(MathF.Max(x1, x1 + side * 0.03f), 1.38f, cyl + 0.06f));
        }

        // Under the running boards: an air tank each side, strapped, and a tool chest.
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w + 0.32f);
            float z0 = drivers[1] - 0.3f, z1 = drivers[3] + 0.5f;
            Iron(k, 1.0f);
            k.Cylinder(new Vector3(x, 1.0f, z0), new Vector3(x, 1.0f, z1), 0.24f, 10);
            Rusted(k).Shade(0.55f);
            foreach (float z in new[] { z0 + 0.4f, z1 - 0.4f })
                k.Cylinder(new Vector3(x, 1.0f, z - 0.04f), new Vector3(x, 1.0f, z + 0.04f), 0.26f, 10, caps: false);
            Plate(k, 1.0f);
            float tz = drivers[3] + 1.0f;
            k.Box(new Vector3(MathF.Min(side * (w + 0.1f), side * (w + 0.55f)), 0.85f, tz), new Vector3(MathF.Max(side * (w + 0.1f), side * (w + 0.55f)), 1.28f, tz + 0.9f));
        }

        // Ammunition boxes on the cab roof's back corners, strapped down.
        foreach (int side in new[] { -1, 1 })
        {
            var at = new Vector3(side * (w - 0.45f), RoofProfile((w - 0.45f) / (w + 0.12f), roofTop, (float)roof.Min.Y), cabBack - 0.55f);
            Plate(k, 0.8f).Shade(0.85f);
            k.Box(at + new Vector3(-0.28f, -0.05f, -0.4f), at + new Vector3(0.28f, 0.34f, 0.4f));
            Rusted(k).Shade(0.5f);
            k.Box(at + new Vector3(-0.3f, 0.12f, -0.42f), at + new Vector3(0.3f, 0.16f, 0.42f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        // The corridors' lamps, caged, on brackets off the hood's side.
        foreach (var lamp in CorridorLamps(shape))
        {
            k.Use("paint_black", Palette.SootBlack, 0.7f, 0.3f);
            k.Box(lamp - new Vector3(0.08f, 0.12f, 0.08f), lamp + new Vector3(0.08f, 0.12f, 0.08f));
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.2f);
            k.Emissive = 1;
            float face = -MathF.Sign(lamp.X);
            k.Panel(lamp + new Vector3(face * 0.085f, 0, 0), new Vector3(face, 0, 0), Vector3.UnitY, 0.12f, 0.17f);
            k.Emissive = 0;
        }
        return k.Build("engine-dress");
    }

    /// <summary>
    /// The lamps in the corridors under the hood (note 338), in the engine's frame: one in each, halfway along it, high on
    /// the hood's side over the walkway (SceneArt lights them; EngineDress cages them).
    /// </summary>
    public static Vector3[] CorridorLamps(CarShape shape)
    {
        var roof = shape.Solids.Where(x => x.Part == PartKind.CabRoof).OrderBy(x => x.Box.Max.Z).Last().Box;
        float deck = (float)shape.Cab!.Value.Min.Y + 0.1f, w = (float)shape.HalfWidth;
        float z = (float)(roof.Min.Z + roof.Max.Z) / 2;
        return [new(-(w - 0.18f), deck + 2.35f, z), new(w - 0.18f, deck + 2.35f, z)];
    }

    // The plough (note 338): how far ahead of the engine's front its foot reaches, how high its crest stands (m, up to the
    // nose's foot, under the headlamp's housing), and how far across its front face is either side of the middle.
    const float PloughAhead = 1.7f, PloughCrest = 1.55f, PloughFront = 0.8f;

    /// <summary>Where the safety valves stand on the boiler top (frame Z): just behind the cab (Effects lifts there).</summary>
    public static float SafetyValveZ(CarShape shape) => (float)shape.Cab!.Value.Max.Z + 0.7f;

    /// <summary>Where the whistle stands on the boiler top (frame Z), beside the safety valves (Effects plumes there).</summary>
    public static float WhistleZ(CarShape shape) => (float)shape.Cab!.Value.Max.Z + 0.35f;

    /// <summary>
    /// Where the boiler tears when it ruptures (spec B.6, GDD §23): a seam on its left flank, high, between the first two
    /// straps behind the cab, so the burst's seen from the cab's left doorway and from the line; in the engine's frame, on
    /// the casing's face (its outward normal the engine's −X).
    /// </summary>
    public static Vector3 RuptureSeam(CarShape shape)
    {
        var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
        return new(-(float)boiler.Max.X, (float)boiler.Max.Y - 0.85f, (float)shape.Cab!.Value.Max.Z + 2.7f);
    }

    /// <summary>
    /// The tear itself, drawn over the casing at <see cref="RuptureSeam"/> while she's ruptured: a ragged black hole
    /// in a scorch of soot, its plate peeled back in petals bent out from the edge, and burst tubes curling out of it.
    /// Its own frame: the casing's face is x = 0, outward −X; Y up, Z along the engine.
    /// </summary>
    public static MeshAsset RuptureTear(Look? look)
    {
        var k = new Kit(look, 77);
        const int n = 9;
        // The hole's ragged edge: an ellipse, longer along the seam than across it, each point in or out a little.
        var edge = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float ang = i * MathF.Tau / n;
            float r = 0.8f + 0.35f * Frac(MathF.Sin(i * 91.7f) * 437.5f);
            edge[i] = new Vector3(0, MathF.Sin(ang) * 0.3f * r, MathF.Cos(ang) * 0.55f * r);
        }
        // The scorch: soot blown over the plate round it, wider and ragged.
        k.Use("iron_smokebox", Palette.SootBlack, 0.95f, 0.1f, tile: 0.8f);
        for (int i = 0; i < n; i++)
        {
            var e0 = edge[i] * 1.6f + new Vector3(-0.004f, 0.05f, 0);
            var e1 = edge[(i + 1) % n] * 1.6f + new Vector3(-0.004f, 0.05f, 0);
            // (Both windings: it's seen from out along −X, and cheap.)
            k.Tri(new Vector3(-0.004f, 0, 0), e1, e0);
            k.Tri(new Vector3(-0.004f, 0, 0), e0, e1);
        }
        // The hole: black, the dark of the boiler's inside.
        k.Use("paint_black", new Vector3(0.01f, 0.01f, 0.01f), 1, 0);
        for (int i = 0; i < n; i++)
        {
            k.Tri(new Vector3(-0.008f, 0, 0), edge[(i + 1) % n] with { X = -0.008f }, edge[i] with { X = -0.008f });
            k.Tri(new Vector3(-0.008f, 0, 0), edge[i] with { X = -0.008f }, edge[(i + 1) % n] with { X = -0.008f });
        }
        // The torn edge: raw bright iron where the plate parted, a thin lip round the hole.
        k.Use("iron_plate", new Vector3(0.62f, 0.6f, 0.56f), 0.2f, 0.7f, tile: 0.3f);
        k.Tint = new Vector3(1.6f, 1.55f, 1.45f);
        for (int i = 0; i < n; i++)
        {
            var e0 = edge[i];
            var e1 = edge[(i + 1) % n];
            k.Quad(e0 with { X = -0.03f }, e1 with { X = -0.03f }, (e1 * 0.84f) with { X = 0.01f }, (e0 * 0.84f) with { X = 0.01f }, twoSided: true);
        }
        // The petals: each length of edge's plate torn up from the hole and bent out about it, ragged at the tip.
        // (Rusted on the inside, which is what's turned out: they read orange-brown against the grey casing.)
        k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.3f, tile: 0.5f);
        k.Tint *= 1.4f;
        for (int i = 0; i < n; i++)
        {
            var b0 = edge[i];
            var b1 = edge[(i + 1) % n];
            var mid = (b0 + b1) / 2;
            var outIn = Vector3.Normalize(mid with { X = 0 });
            float h = Frac(MathF.Sin(i * 37.3f) * 911.1f);
            float len = 0.22f + 0.22f * h;
            // In the casing's plane the petal would lie over the hole (pointing in); torn, it swings out about its base by
            // 100 to 150 degrees, so it stands proud of the plate and leans back away from the hole.
            var tipA = b0 * 0.85f - outIn * len * 0.6f;
            var tipB = b1 * 0.85f - outIn * len;
            var axis = Vector3.Normalize(b1 - b0);
            var turn = Quaternion.CreateFromAxisAngle(axis, -(1.75f + 0.85f * h));
            Vector3 Bend(Vector3 p) => b0 + Vector3.Transform(p - b0, turn);
            var ta = Bend(tipA);
            var tb = Bend(tipB);
            // Make sure it's bent outward (−X), whichever way the edge runs.
            if (ta.X + tb.X > 0)
            {
                turn = Quaternion.CreateFromAxisAngle(axis, 1.75f + 0.85f * h);
                ta = Bend(tipA);
                tb = Bend(tipB);
            }
            var jag = Vector3.Lerp(ta, tb, 0.5f) + (ta - b0) * 0.25f;
            k.Quad(b0, b1, tb, jag, twoSided: true);
            k.Quad(b0, jag, ta, ta, twoSided: true);
        }
        // Burst tubes: copper, curling out of the hole and back along the engine.
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
        foreach (var (y, z, reach) in new[] { (0.04f, -0.08f, 0.38f), (-0.06f, 0.06f, 0.3f), (0.1f, 0.12f, 0.24f) })
        {
            var p0 = new Vector3(0.05f, y, z);
            var p1 = new Vector3(-reach * 0.6f, y + 0.04f, z + reach * 0.2f);
            var p2 = new Vector3(-reach, y - 0.02f, z + reach * 0.7f);
            k.Rod(p0, p1, 0.022f, 6);
            k.Rod(p1, p2, 0.022f, 6);
        }
        return k.Build("rupture-tear");
    }

    static float Frac(float x) => x - MathF.Floor(x);

    // The war train's iron (note 338, the director's reference: dark, rusted, riveted): the riveted oxide plate darkened
    // to a burnt brown, the riveted iron warmed toward it, and the rust's orange for straps and edges.
    static readonly Vector3 PlateTint = new(0.58f, 0.46f, 0.4f), IronTint = new(0.66f, 0.52f, 0.43f), RustTint = new(0.72f, 0.52f, 0.4f);

    /// <summary>Riveted plate, rusted dark (the train's armour, note 338).</summary>
    static Kit Plate(Kit k, float tile = 1.3f)
    {
        k.Use("paint_oxide", Palette.RustRed * 0.6f, 0.95f, 0.3f, tile);
        k.Tint *= PlateTint;
        return k;
    }

    /// <summary>Riveted iron, warmed with rust (the armour's second plate, note 338).</summary>
    static Kit Iron(Kit k, float tile = 1.3f)
    {
        k.Use("iron_plate", Palette.IronGrey * 0.7f, 0.95f, 0.3f, tile);
        k.Tint *= IronTint;
        return k;
    }

    /// <summary>Rust: straps, edges, slats (note 338).</summary>
    static Kit Rusted(Kit k, float tile = 1f)
    {
        k.Use("rust_heavy", Palette.RustRed * 0.6f, 0.95f, 0.25f, tile);
        k.Tint *= RustTint;
        return k;
    }

    /// <summary>
    /// A flat strap of iron <paramref name="width"/> wide from <paramref name="a"/> to <paramref name="b"/> on a face whose
    /// outward normal is <paramref name="normal"/> (note 338's braces): one quad, the face's only, so a car's dozens of
    /// braces cost the frame almost nothing.
    /// </summary>
    static void Strap(Kit k, Vector3 a, Vector3 b, Vector3 normal, float width)
    {
        var across = Vector3.Normalize(Vector3.Cross(normal, b - a)) * (width / 2);
        var lift = Vector3.Normalize(normal) * 0.01f;
        Vector3 p0 = a - across + lift, p1 = b - across + lift, p2 = b + across + lift, p3 = a + across + lift;
        // (Wound to face along the normal, whichever way the strap runs: Quad's first triangle is p0, p3, p2.)
        if (Vector3.Dot(Vector3.Cross(p3 - p0, p2 - p0), normal) >= 0)
            k.Quad(p0, p1, p2, p3);
        else
            k.Quad(p0, p3, p2, p1);
    }

    // The boiler casing's top corners cut off (m, note 338), and the run of each of its plates between the seam bands.
    const float CasingBevel = 0.28f, CasingPanel = 1.6f;

    /// <summary>An octagon (counter-clockwise) for a box with its top corners cut by <paramref name="topBevel"/> and its bottom by <paramref name="bottomBevel"/>.</summary>
    static Vector2[] Octagon(float x0, float y0, float x1, float y1, float topBevel, float bottomBevel) =>
    [
        new(x0 + bottomBevel, y0), new(x1 - bottomBevel, y0), new(x1, y0 + bottomBevel), new(x1, y1 - topBevel),
        new(x1 - topBevel, y1), new(x0 + topBevel, y1), new(x0, y1 - topBevel), new(x0, y0 + bottomBevel),
    ];

    // ---------------------------------------------------------------- the engine's shell (note 311)

    /// <summary>
    /// A rounded section for the boiler's casing (counter-clockwise): a squared-off barrel, its sides near straight and its
    /// top near flat (the boiler top is walked on), the corners well rounded. A superellipse of exponent 3.2.
    /// </summary>
    static Vector2[] Rounded(float halfWidth, float y0, float y1, int points = 16)
    {
        float yc = (y0 + y1) / 2, hh = (y1 - y0) / 2;
        var p = new Vector2[points];
        for (int i = 0; i < points; i++)
        {
            float a = (i + 0.5f) * MathF.Tau / points, c = MathF.Cos(a), s = MathF.Sin(a);
            p[i] = new(halfWidth * MathF.Sign(c) * MathF.Pow(MathF.Abs(c), 2 / RoundedPower), yc + hh * MathF.Sign(s) * MathF.Pow(MathF.Abs(s), 2 / RoundedPower));
        }
        return p;
    }

    const float RoundedPower = 3.2f;

    /// <summary>How far out from the middle <see cref="Rounded"/>'s side is at height <paramref name="y"/>.</summary>
    static float RoundedX(float halfWidth, float y0, float y1, float y)
    {
        float hh = (y1 - y0) / 2, t = MathF.Min(1, MathF.Abs(y - (y0 + y1) / 2) / hh);
        return halfWidth * MathF.Pow(1 - MathF.Pow(t, RoundedPower), 1 / RoundedPower);
    }

    /// <summary>Where the cab's doorways start along the engine (the standard doorway's front, under its lintel).</summary>
    static float DoorFront(CarShape shape, float deck, float cabBack) =>
        shape.Solids.Where(s => s.Part == PartKind.CabWall && s.Box.Min.Y > deck + 1.5).Select(s => (float)s.Box.Min.Z).DefaultIfEmpty(cabBack - 1.05f).Min();

    // Nothing of the prow stands more than ProwReach ahead of the engine's front (note 311): the headlamp's shadow map
    // starts 0.3 m out from the lens (GreyboxRenderer.LampViewProjection), and a piece that reaches past that is drawn into
    // it whole. The engine casts no shadow in its own lamp, and its triangles stay out of that pass (the frame's budget,
    // tuning/perf.json, has no room for them: PerfBudgetTests).
    const float ProwReach = 0.28f;

    // The prow's heights (note 338): the plough's foot and top, the buffer beam's top, and the nose up to the window sills.
    const float PloughFoot = 0.12f, PloughTop = 1.05f, BeamTop = 1.4f;

    // The nose's flat front, how far across (m); its cheeks run back from there to the cab's sides.
    const float NoseHalf = 0.85f;

    /// <summary>
    /// The prow (note 338; the director, 7 Oct, with a reference: "a boxier cooler looking train front"): armour in flat
    /// faces, not curves. At the rail a plough in a V of heavy slats, raked back, riveted to the beam over it; the buffer
    /// beam across the front, deep and riveted; over it the nose, a flat plate across the middle with the headlamp in it
    /// and cheeks cut back to the cab's sides, up to the window sills.
    /// </summary>
    static void Prow(Kit k, float w, float l, float cabFront)
    {
        float front = -l - 0.02f;
        // (The plough is the engine's dressing's, out ahead of all this: EngineDress.)
        // The buffer beam: deep, square, the whole width, riveted along its face.
        Iron(k, 1.2f);
        k.Box(new Vector3(-w, PloughTop, front - 0.04f), new Vector3(w, BeamTop, cabFront + 0.1f), Kit.Faces.All & ~Kit.Faces.PosZ);
        k.Use("rust_heavy", Palette.SootBlack, 0.85f, 0.3f);
        for (int i = 0; i <= 10; i++)
        {
            float x = -w + 0.12f + (2 * w - 0.24f) * i / 10;
            foreach (float y in new[] { PloughTop + 0.07f, BeamTop - 0.07f })
                k.Box(new Vector3(x - 0.025f, y - 0.025f, front - 0.07f), new Vector3(x + 0.025f, y + 0.025f, front - 0.04f), Kit.Faces.All & ~Kit.Faces.PosZ);
        }
        // The nose: a flat plate across the middle, raked back a little, its cheeks cut back to the cab's sides.
        float sill = Sill;
        Vector3[] Nose(float y, float back) =>
        [
            new(-w - 0.02f, y, cabFront + 0.02f), new(-NoseHalf, y, front + back), new(NoseHalf, y, front + back), new(w + 0.02f, y, cabFront + 0.02f),
        ];
        Plate(k, 1.4f);
        var nose = new List<Vector3[]> { Nose(BeamTop, 0), Nose(sill, 0.14f) };
        k.Loft(nose, smooth: false, facing: -Vector3.UnitZ);
        // Its top, the sill under the windows.
        Rusted(k).Shade(0.7f);
        var top = nose[1];
        for (int i = 0; i + 1 < top.Length; i++)
            k.Rod(top[i] + new Vector3(0, 0.03f, -0.02f), top[i + 1] + new Vector3(0, 0.03f, -0.02f), 0.05f);
        k.Quad(top[0], top[1], top[2], top[3], twoSided: true);
        // The plate's seams: straps down the corners where the front meets the cheeks, and across under the lamp.
        Rusted(k);
        foreach (int i in new[] { 1, 2 })
            k.Rod(nose[0][i] + new Vector3(0, 0, -0.03f), nose[1][i] + new Vector3(0, 0, -0.03f), 0.035f);
        foreach (int side in new[] { -1, 1 })
        {
            // A plate bolted over each cheek, braced across.
            var lo0 = nose[0][side < 0 ? 0 : 3];
            var lo1 = nose[0][side < 0 ? 1 : 2];
            var hi0 = nose[1][side < 0 ? 0 : 3];
            var hi1 = nose[1][side < 0 ? 1 : 2];
            var outward = Vector3.Normalize(Vector3.Cross(hi1 - lo1, lo0 - lo1)) * 0.03f;
            if (outward.Z > 0)
                outward = -outward;
            k.Rod(Vector3.Lerp(lo0, lo1, 0.1f) + outward, Vector3.Lerp(hi0, hi1, 0.9f) + outward, 0.022f);
            k.Rod(Vector3.Lerp(hi0, hi1, 0.1f) + outward, Vector3.Lerp(lo0, lo1, 0.9f) + outward, 0.022f);
        }
        k.Use("rust_heavy", Palette.SootBlack, 0.85f, 0.3f);
        foreach (float y in new[] { BeamTop + 0.08f, sill - 0.08f })
            for (int i = 0; i <= 6; i++)
            {
                float x = -NoseHalf + 0.08f + (2 * NoseHalf - 0.16f) * i / 6, z = front + 0.14f * (y - BeamTop) / (sill - BeamTop);
                if (MathF.Abs(x) > 0.5f || y < HeadlampY - 0.5f || y > HeadlampY + 0.5f)
                    k.Box(new Vector3(x - 0.025f, y - 0.025f, z - 0.03f), new Vector3(x + 0.025f, y + 0.025f, z));
            }
    }

    // The cab's window sills over the nose (m over the rail): its waist, which the nose comes up to.
    const float Sill = 2.52f;

    /// <summary>
    /// The headlamp (note 338): a round lamp in a square armoured housing let into the nose's plate, a visor over it, the
    /// lens caged with bars; its lens is where World puts the lamp's light (<see cref="HeadlampY"/>, the engine's front).
    /// </summary>
    static void Headlamp(Kit k, float l)
    {
        var lens = new Vector3(0, HeadlampY, -l - 0.06f);
        var root = lens + new Vector3(0, 0, 0.45f);
        const float half = 0.42f;
        k.Use("iron_plate", Palette.IronGrey * 0.8f, 0.9f, 0.35f);
        k.Box(new Vector3(-half, lens.Y - half, lens.Z - 0.04f), new Vector3(half, lens.Y + half, root.Z), Kit.Faces.All & ~Kit.Faces.PosZ & ~Kit.Faces.NegZ);
        // The housing's face: a frame round the lamp, the plate cut square to its rim.
        foreach (var (x0, y0, x1, y1) in new[] { (-half, -half, half, -0.36f), (-half, 0.36f, half, half), (-half, -0.36f, -0.36f, 0.36f), (0.36f, -0.36f, half, 0.36f) })
            k.Box(new Vector3(x0, lens.Y + y0, lens.Z - 0.04f), new Vector3(x1, lens.Y + y1, lens.Z), Kit.Faces.All & ~Kit.Faces.PosZ);
        // The visor: a plate out over it, angled down.
        k.Quad(new Vector3(-half - 0.03f, lens.Y + half, root.Z - 0.2f), new Vector3(half + 0.03f, lens.Y + half, root.Z - 0.2f),
            new Vector3(half + 0.03f, lens.Y + half - 0.1f, lens.Z - 0.2f), new Vector3(-half - 0.03f, lens.Y + half - 0.1f, lens.Z - 0.2f), twoSided: true);
        k.Use("iron_smokebox", Palette.SootBlack * 1.4f, 0.8f, 0.35f);
        k.Cylinder(root, lens + new Vector3(0, 0, 0.02f), 0.34f, 12, capA: false);
        k.Use("brass", Palette.TarnishedBrass, 0.6f, 0.6f);
        k.Cylinder(lens + new Vector3(0, 0, 0.04f), lens - new Vector3(0, 0, 0.01f), 0.37f, 12, caps: false);
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.64f);
        k.Emissive = 1;
        // The lens image once across the disc (tile 0.64 m: its middle at 0.32), not four quarters of it mirrored from a
        // corner: those read as a pattern behind the saltire (the director, 8 Oct).
        k.Disc(lens, -Vector3.UnitZ, 0.34f, 16, new Vector2(0.32f, 0.32f), 0.32f);
        k.Emissive = 0;
        // The cage is a saltire (the director, 8 Oct 2026: "a cool symbol for the maritimes in this world, a world dominated
        // by monsters, in the headlamp of the train ... something more anglo maritimes"): St Andrew's cross, Nova Scotia's
        // flag (New Scotland's blue saltire on white), in broad bars of plate painted blue across the lens on a ring, so the
        // lamp coming out of the fog carries the flag in its own white light.
        Saltire(k, lens - new Vector3(0, 0, 0.05f), 0.32f);
    }

    /// <summary>
    /// St Andrew's saltire over a lamp's lens (note 338): two broad bars of plate crossed corner to corner, a fifth of the
    /// lens across as the flag's are, painted Nova Scotia's blue and bolted at the crossing, on an iron ring the size of
    /// the lens; facing −Z from <paramref name="at"/>.
    /// </summary>
    static void Saltire(Kit k, Vector3 at, float radius)
    {
        k.Use("paint_black", Palette.SootBlack, 0.6f, 0.35f);
        k.Tint = new Vector3(0.3f, 0.55f, 1.25f);
        float half = radius * 1.02f, bar = radius * 0.2f;
        foreach (float turn in new[] { MathF.PI / 4, -MathF.PI / 4 })
            k.With(Matrix4x4.CreateRotationZ(turn) * Matrix4x4.CreateTranslation(at), () =>
                k.Box(new Vector3(-half, -bar, -0.012f), new Vector3(half, bar, 0.012f)));
        // The boss at the crossing, and the ring.
        k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
        k.Tint *= 0.6f;
        k.Cylinder(at - new Vector3(0, 0, 0.02f), at + new Vector3(0, 0, 0.012f), 0.04f, 8);
        for (int i = 0; i < 16; i++)
        {
            float a0 = i * MathF.Tau / 16, a1 = (i + 1) * MathF.Tau / 16;
            k.Rod(at + new Vector3(MathF.Cos(a0), MathF.Sin(a0), 0) * radius * 1.06f, at + new Vector3(MathF.Cos(a1), MathF.Sin(a1), 0) * radius * 1.06f, 0.016f);
        }
    }

    /// <summary>
    /// The skirt down over the wheels (note 338), each side from the cab's doorway back to the rear: flat armour plate in
    /// panels, strapped where they meet and braced corner to corner, its foot cut up at the ends.
    /// </summary>
    static void Skirt(Kit k, float w, float l, float deck, float from)
    {
        foreach (int side in new[] { -1, 1 })
        {
            float xo = side * (w + 0.05f), xi = side * (w - 0.01f);
            var (x0, x1) = (MathF.Min(xo, xi), MathF.Max(xo, xi));
            Plate(k);
            k.Box(new Vector3(x0, 1.0f, from + 0.3f), new Vector3(x1, deck, l - 0.6f), Kit.Faces.All & ~Kit.Faces.PosY);
            // The ends cut up at a slant, over the wheels' first and last.
            k.Quad(new Vector3(xo, 1.0f, from + 0.3f), new Vector3(xo, deck, from), new Vector3(xo, deck, from + 0.3f), new Vector3(xo, 1.0f, from + 0.3f), twoSided: true);
            k.Quad(new Vector3(xo, 1.0f, l - 0.6f), new Vector3(xo, deck, l - 0.6f), new Vector3(xo, deck, l - 0.3f), new Vector3(xo, 1.0f, l - 0.6f), twoSided: true);
            // Its panels: a strap at each seam, braced corner to corner between.
            var seams = new List<float>();
            for (float z = from + 0.3f; z < l - 0.7f; z += 1.9f)
                seams.Add(z);
            seams.Add(l - 0.6f);
            float xs = side * (w + 0.07f);
            Rusted(k);
            foreach (float z in seams)
                k.Box(new Vector3(MathF.Min(xs, xo), 0.98f, z - 0.05f), new Vector3(MathF.Max(xs, xo), deck, z + 0.05f));
            Rusted(k).Shade(0.6f);
            for (int i = 0; i + 1 < seams.Count; i++)
            {
                float z0 = seams[i] + 0.1f, z1 = seams[i + 1] - 0.1f;
                Strap(k, new Vector3(xo, 1.05f, z0), new Vector3(xo, deck - 0.05f, z1), new Vector3(side, 0, 0), 0.06f);
                Strap(k, new Vector3(xo, deck - 0.05f, z0), new Vector3(xo, 1.05f, z1), new Vector3(side, 0, 0), 0.06f);
            }
        }
    }

    /// <summary>
    /// The cab's shell outside the sim's walls (note 311): its sides under the windows bellied out like a hull's and
    /// capped with a rolled rail (the prow's sill rail, carried down both sides), round armoured pillars at the front
    /// corners leaning in with the cab's tumblehome, and over each side's windows a shutter of plate propped out on its
    /// hinge.
    /// </summary>
    static void CabShell(Kit k, float w, float deck, float cabFront, float cabBack, float doorFront, float roofLow, float bw)
    {
        float waist = deck + 1.1f;
        foreach (int side in new[] { -1, 1 })
        {
            // The side under the windows, from the nose back to the doorway (note 338): flat plate, braced corner to corner.
            float sideOut = side * (w + 0.05f), sideIn = side * w;
            Plate(k, 1.4f);
            k.Box(new Vector3(MathF.Min(sideOut, sideIn), 1.0f, cabFront + 0.02f), new Vector3(MathF.Max(sideOut, sideIn), waist, doorFront), Kit.Faces.All & ~Kit.Faces.PosY);
            Rusted(k).Shade(0.6f);
            Strap(k, new Vector3(sideOut, 1.08f, cabFront + 0.2f), new Vector3(sideOut, waist - 0.08f, doorFront - 0.12f), new Vector3(side, 0, 0), 0.07f);
            Strap(k, new Vector3(sideOut, waist - 0.08f, cabFront + 0.2f), new Vector3(sideOut, 1.08f, doorFront - 0.12f), new Vector3(side, 0, 0), 0.07f);
            // Welded patches over old damage, proud of the plate.
            k.Use("rust_heavy", Palette.RustRed, 0.95f, 0.15f);
            float mid = (cabFront + doorFront) / 2;
            k.Box(new Vector3(side * (w + 0.08f) - 0.02f, 1.55f, mid - 0.55f), new Vector3(side * (w + 0.08f) + 0.02f, 1.95f, mid + 0.1f));
            // The rolled rail along the waist.
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
            k.Box(new Vector3(MathF.Min(side * (w - 0.02f), side * (w + 0.09f)), waist - 0.02f, cabFront), new Vector3(MathF.Max(side * (w - 0.02f), side * (w + 0.09f)), waist + 0.06f, doorFront));
            // The shutter over the side windows, propped out and down off its hinge under the eave.
            Iron(k);
            float h0 = roofLow - EaveAt(cabFront + 0.25f, cabFront, cabBack) - 0.02f;
            var hinge0 = new Vector3(side * (w + 0.06f), h0, cabFront + 0.25f);
            var hinge1 = new Vector3(side * (w + 0.06f), roofLow - EaveAt(doorFront - 0.05f, cabFront, cabBack) - 0.02f, doorFront - 0.05f);
            var lip = new Vector3(side * 0.2f, -0.16f, 0);
            k.Quad(hinge0, hinge1, hinge1 + lip, hinge0 + lip, twoSided: true);
            k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
            foreach (float z in new[] { hinge0.Z + 0.2f, hinge1.Z - 0.2f })
                k.Rod(new Vector3(side * (w + 0.02f), h0 - 0.3f, z), new Vector3(hinge0.X + lip.X * 0.8f, h0 + lip.Y * 0.8f, z), 0.012f);
            // Over the windows, armour: plate under and over each, so the side reads as an armoured cab with slits, not an
            // open frame (the sim's side is open over its waist wall; nothing walks through a slit's height anyway).
            Plate(k, 1.2f);
            float x0 = MathF.Min(side * (w - 0.06f), side * (w + 0.02f)), x1 = MathF.Max(side * (w - 0.06f), side * (w + 0.02f));
            k.Box(new Vector3(x0, waist + 0.05f, cabFront + 0.15f), new Vector3(x1, SlitBottom + deck, doorFront - 0.02f));
            {
                // Up to the eave, which falls towards the back.
                float xs = side * (w + 0.02f), xi = side * (w - 0.06f), y0 = SlitTop + deck;
                Vector3 P(float x, float y, float z) => new(x, y, z);
                float ef = roofLow - EaveAt(cabFront + 0.15f, cabFront, cabBack) + 0.02f, eb = roofLow - EaveAt(doorFront, cabFront, cabBack) + 0.02f;
                k.Quad(P(xs, ef, cabFront + 0.15f), P(xs, eb, doorFront - 0.02f), P(xs, y0, doorFront - 0.02f), P(xs, y0, cabFront + 0.15f), twoSided: true);
                k.Quad(P(xi, ef, cabFront + 0.15f), P(xi, eb, doorFront - 0.02f), P(xi, y0, doorFront - 0.02f), P(xi, y0, cabFront + 0.15f), twoSided: true);
                k.Quad(P(xs, y0, cabFront + 0.15f), P(xs, y0, doorFront - 0.02f), P(xi, y0, doorFront - 0.02f), P(xi, y0, cabFront + 0.15f), twoSided: true);
            }
            // Rivet straps up the plate at each post.
            k.Use("rust_heavy", Palette.RustRed, 0.9f, 0.2f);
            float span = doorFront - (cabFront + 0.15f);
            for (int i = 1; i <= 2; i++)
            {
                float z = cabFront + 0.15f + span * i / 3;
                k.Box(new Vector3(x0 - 0.015f, waist + 0.05f, z - 0.06f), new Vector3(x1 + 0.015f, roofLow - EaveAt(z, cabFront, cabBack), z + 0.06f));
            }
        }
        // The windscreen's frame, raked forward from the sill up into the brow, the way a ship's bridge leans out over its
        // bow: the middle post over the windows' divide and one at each corner outside them (the driver's view is between).
        Iron(k).Shade(0.85f);
        k.Rod(new Vector3(0, waist + 0.05f, cabFront - 0.03f), new Vector3(0, roofLow - CabChamfer, cabFront + 0.02f), 0.05f);
        // The way in (the director, 7 Oct: "there actually has to be a way to get into the cab from the train"): each side's
        // doorway at the back of the cab, which the running board along the boiler comes up to from car 1, framed in rolled
        // iron, with a caged lamp over it, so it reads as the door it is from along the boards and from the roofs.
        float lintel = deck + k.DoorHeight();
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w + 0.04f), z0 = doorFront, z1 = cabBack - 0.15f;
            k.Use("rust_heavy", Palette.RustRed, 0.85f, 0.3f);
            k.Rod(new Vector3(x, deck, z0), new Vector3(x, lintel + 0.06f, z0), 0.045f);
            k.Rod(new Vector3(x, deck, z1), new Vector3(x, lintel + 0.06f, z1), 0.045f);
            k.Rod(new Vector3(x, lintel + 0.06f, z0 - 0.05f), new Vector3(x, lintel + 0.06f, z1 + 0.05f), 0.045f);
            // The lamp on the cab's back corner over the doorway, out on a bracket, lit back along the board and out to the
            // side: what a crewmate coming forward from car 1 walks towards.
            var lamp = new Vector3(side * (w + 0.16f), lintel - 0.05f, cabBack + 0.06f);
            k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
            k.Rod(new Vector3(side * (w - 0.02f), lamp.Y, cabBack - 0.05f), lamp, 0.02f);
            k.Use("paint_black", Palette.SootBlack, 0.7f, 0.3f);
            k.Box(lamp - new Vector3(0.09f, 0.13f, 0.09f), lamp + new Vector3(0.09f, 0.13f, 0.09f));
            k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.2f);
            k.Emissive = 1;
            k.Panel(lamp + new Vector3(0, 0, 0.095f), Vector3.UnitZ, Vector3.UnitY, 0.13f, 0.18f);
            k.Panel(lamp + new Vector3(side * 0.095f, 0, 0), new Vector3(side, 0, 0), Vector3.UnitY, 0.13f, 0.18f);
            k.Emissive = 0;
            k.Use("rust_heavy", Palette.SootBlack, 0.8f, 0.3f);
            k.Rod(lamp + new Vector3(-0.05f, -0.13f, 0.1f), lamp + new Vector3(-0.05f, 0.13f, 0.1f), 0.008f);
            k.Rod(lamp + new Vector3(0.05f, -0.13f, 0.1f), lamp + new Vector3(0.05f, 0.13f, 0.1f), 0.008f);
        }
        // The back wall's openings (note 338; the director: "cut open on both sides so players can walk straight to the
        // cab"): the walkway either side of the boiler runs on into the cab under a lintel; each opening framed in rolled
        // iron, so it reads as the way in from along the boiler.
        foreach (int side in new[] { -1, 1 })
        {
            float xa = side * (bw + 0.04f), xb = side * (w - 0.14f), z = cabBack + 0.03f;
            k.Use("rust_heavy", Palette.RustRed, 0.85f, 0.3f);
            k.Rod(new Vector3(xa, deck, z), new Vector3(xa, lintel + 0.06f, z), 0.04f);
            k.Rod(new Vector3(xb, deck, z), new Vector3(xb, lintel + 0.06f, z), 0.04f);
            k.Rod(new Vector3(xa, lintel + 0.06f, z), new Vector3(xb, lintel + 0.06f, z), 0.04f);
        }
        Iron(k).Shade(0.85f);
        // And at each corner, raked the same way from the sill rail, outside the windows.
        foreach (int side in new[] { -1, 1 })
            k.Rod(new Vector3(side * (w - 0.04f), waist + 0.05f, cabFront - 0.02f), new Vector3(side * (w - 0.04f), roofLow - CabChamfer, cabFront + 0.02f), 0.07f);
    }

    // The side windows are slits between these heights over the cab floor (m).
    const float SlitBottom = 1.62f, SlitTop = 1.9f;

    /// <summary>
    /// How far the roof's eaves come down over the cab's sides at <paramref name="z"/> (m, note 311): a little at the
    /// front, more towards the back, so the roof's line falls back along the cab into the shoulders and on down onto the
    /// boiler (over the doorways it's still clear of the lintels).
    /// </summary>
    static float EaveAt(float z, float cabFront, float cabBack) => float.Lerp(0.2f, 0.48f, Math.Clamp((z - cabFront) / (cabBack - cabFront), 0, 1));

    // How far the cab roof's front corner is cut back, over the windows (m, note 338).
    const float CabChamfer = 0.34f;

    // How far across the cab's roof is flat (of its half-width), before it chamfers down to the eaves (note 338).
    const float RoofFlat = 0.55f;

    /// <summary>The cab roof's height at <paramref name="t"/> across it (−1 to 1): flat over the middle, a straight chamfer to the eaves.</summary>
    static float RoofProfile(float t, float top, float low) =>
        MathF.Abs(t) <= RoofFlat ? top : top - (top - low - 0.08f) * 0.9f * (MathF.Abs(t) - RoofFlat) / (1 - RoofFlat);

    /// <summary>
    /// The cab's roof (note 311): a helmet. Arched across and overhanging the sides, it rides the cab's length; at the
    /// front it narrows and droops into a brow out over the windows, and at the back it sweeps down in two shoulders onto
    /// the boiler's flanks (clear over the way beside the boiler: they start head-high there), so the cab and the boiler
    /// read as one machine.
    /// </summary>
    static void Roof(Kit k, float w, float cabFront, float cabBack, float hoodBack, float roofLow, float roofTop)
    {
        float rw = w + 0.12f;
        // A section of it: arched across from eave to eave, the eaves coming down over the cab's sides, and under it the
        // ceiling at the roof slab's underside (the run map and the gauges hang under that).
        // (Over the top it's the arch the roof has always had, which the coaling hatch sits on: Bunker's RoofY. Past nine
        // tenths of the way out it curls down to the eave.)
        Vector3[] Arch(float z, float widen, float drop, float flat)
        {
            var ring = new List<Vector3>();
            float half = rw * widen, lo = roofLow - drop - EaveAt(z, cabFront, cabBack), hi = roofTop - drop * flat;
            // (Note 338: flat over the middle, chamfered down to the eaves, an armoured car's roof, not a helmet's arch.)
            foreach (float t in (float[])[-1, -0.975f, -0.94f, -0.9f, -0.7f, -RoofFlat, 0, RoofFlat, 0.7f, 0.9f, 0.94f, 0.975f, 1])
            {
                float y = RoofProfile(t, hi, roofLow - drop);
                float drip = MathF.Abs(t) <= 0.94f ? 0 : (MathF.Abs(t) - 0.94f) / 0.06f;
                ring.Add(new Vector3(-t * half, y - (y - lo) * drip, z));
            }
            ring.Add(new Vector3(-(w - 0.12f) * widen, roofLow - drop - 0.02f, z));
            ring.Add(new Vector3((w - 0.12f) * widen, roofLow - drop - 0.02f, z));
            return [.. ring];
        }
        Plate(k, 1.5f);
        var rings = new List<Vector3[]>
        {
            // (Its front corner cut back on a chamfer over the windows, and on back over the hood to the engine's tail, one
            // roof the whole length: note 338, the director's silhouette.)
            Arch(cabFront - 0.02f, 0.97f, CabChamfer, 1),
            Arch(cabFront + CabChamfer, 1, 0, 0),
            Arch(hoodBack - 0.02f, 1, 0, 0),
            Arch(hoodBack + 0.1f, 0.96f, 0.1f, 0.6f),
        };
        k.Loft(rings, closed: true, smooth: false, facing: Vector3.Zero);
        k.Cap(rings[0], -Vector3.UnitZ);
        k.Cap(rings[^1], Vector3.UnitZ);
        // A rib over the roof where the brow meets it, and the brow's riveted edge.
        Rusted(k);
        var rib = Arch(cabFront + CabChamfer, 1.01f, -0.025f, 1);
        // (The rib runs over the arch only, eave to eave.)
        for (int i = 0; i < 12; i++)
            k.Rod(rib[i], rib[i + 1], 0.035f);
    }

    /// <summary>
    /// The cab, at the front (note 276): waist-high armoured sides with the doorways at the back, pillars, a visor over the
    /// side openings; across its front, the windows the driver looks down the line through, the run map's plate over them;
    /// an arched roof; and in its back wall, where the boiler comes through, the backhead: the firebox door's frame, four
    /// gauges, the water glass and pipework (GDD §12, the Conductor's and the Boiler's place), facing forward into the cab.
    /// </summary>
    static void Cab(Kit k, CarShape shape, float w, float deck, float cabFront, float cabBack, float bw, float top, float roofLow, float roofTop)
    {
        float waist = deck + 1.1f;
        // Where the doorway starts: under its lintel (the sim's, the standard doorway: note 110).
        var doorFront = shape.Solids.Where(s => s.Part == PartKind.CabWall && s.Box.Min.Y > deck + 1.5).Select(s => (float)s.Box.Min.Z).DefaultIfEmpty(cabBack - 1.05f).Min();
        // The walls as the sim has them. Outside, the front and the sides under the windows are the shell's (note 311: the
        // prow, the flared sides, the round pillars); of those, only the faces into the cab are drawn here.
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.CabWall))
        {
            var b = solid.Box;
            bool front = b.Max.Z <= cabFront + 0.2, waistWall = b.Max.Y <= waist + 0.05 && b.Max.X - b.Min.X < 0.2;
            var faces = front ? Kit.Faces.PosZ | Kit.Faces.PosY | Kit.Faces.PosX | Kit.Faces.NegX
                : waistWall ? (b.Min.X < 0 ? Kit.Faces.PosX : Kit.Faces.NegX) | Kit.Faces.PosY : Kit.Faces.All;
            Plate(k);
            k.Box(F(b.Min), F(b.Max), faces);
        }
        CabShell(k, w, deck, cabFront, cabBack, doorFront, roofLow, bw);
        foreach (int side in new[] { -1, 1 })
        {
            float xo = side * w;
            // The side windows (note 276): two posts between the front pillar and the doorway, so the side reads as a cab's
            // glazed side and not an open shed; nothing to walk into (the waist under them is the wall).
            float span = doorFront - (cabFront + 0.15f);
            for (int i = 1; i <= 2; i++)
            {
                float z = cabFront + 0.15f + span * i / 3;
                k.Box(new Vector3(MathF.Min(xo, xo - side * 0.09f), waist + 0.05f, z - 0.045f), new Vector3(MathF.Max(xo, xo - side * 0.09f), roofLow - EaveAt(z, cabFront, cabBack), z + 0.045f));
            }
            // Grab irons at the doorway, and the step irons down to the ballast.
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
            Grab(k, new Vector3(xo, deck + 0.3f, cabBack - 0.12f), new Vector3(xo, waist + 0.6f, cabBack - 0.12f), new Vector3(side, 0, 0));
            foreach (float y in new[] { deck - 0.5f, deck - 1.0f })
                k.Box(new Vector3(MathF.Min(xo, xo + side * 0.25f), y - 0.03f, doorFront + 0.1f), new Vector3(MathF.Max(xo, xo + side * 0.25f), y, cabBack - 0.1f));
            k.Rod(new Vector3(xo + side * 0.24f, deck - 1.03f, doorFront + 0.12f), new Vector3(xo + side * 0.02f, deck, doorFront + 0.12f), 0.015f);
            k.Rod(new Vector3(xo + side * 0.24f, deck - 1.03f, cabBack - 0.12f), new Vector3(xo + side * 0.02f, deck, cabBack - 0.12f), 0.015f);
        }
        // The front windows (note 276: "controls at the front with full vis of the rail"): two tall openings either side of a
        // narrow middle post, from the waist to the run map's plate, their frames thin. Over them, the plate the map hangs on.
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        float z0 = cabFront, z1 = cabFront + 0.08f;
        foreach (int side in new[] { -1, 1 })
        {
            var win = FrontWindow(shape, side);
            // Half the post between the windows, the pillar's side of the opening, and the plate over it up to the roof.
            float inner = side > 0 ? win.X0 : win.X1;
            k.Box(new Vector3(MathF.Min(0, inner), win.Y0, z0), new Vector3(MathF.Max(0, inner), win.Y1, z1));
            float pillar = side > 0 ? win.X1 : win.X0, edge = side * (w - 0.1f);
            k.Box(new Vector3(MathF.Min(pillar, edge), win.Y0, z0), new Vector3(MathF.Max(pillar, edge), win.Y1, z1));
            k.Box(new Vector3(MathF.Min(0, edge), win.Y1, z0), new Vector3(MathF.Max(0, edge), roofLow, z1));
            // A brass-rimmed frame round the opening, its cab-facing faces only (seen from the footplate; the headset's
            // frame has no triangles to spare).
            k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
            var faces = Kit.Faces.PosZ;
            k.Box(new Vector3(win.X0, win.Y0 - 0.03f, z1), new Vector3(win.X1, win.Y0, z1 + 0.03f), faces | Kit.Faces.PosY);
            k.Box(new Vector3(win.X0, win.Y1, z1), new Vector3(win.X1, win.Y1 + 0.03f, z1 + 0.03f), faces | Kit.Faces.NegY);
            k.Box(new Vector3(win.X0 - 0.02f, win.Y0, z1), new Vector3(win.X0, win.Y1, z1 + 0.03f), faces | Kit.Faces.PosX);
            k.Box(new Vector3(win.X1, win.Y0, z1), new Vector3(win.X1 + 0.02f, win.Y1, z1 + 0.03f), faces | Kit.Faces.NegX);
            k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        }
        // The driver's console under the right-hand window: a shelf the regulator and the brake valve stand out of.
        k.Use("iron_plate", Palette.IronGrey * 0.8f, 0.9f, 0.35f);
        k.Box(new Vector3(0.1f, deck + 0.95f, cabFront + 0.15f), new Vector3(w - 0.12f, waist + 0.04f, cabFront + 0.55f), Kit.Faces.All & ~Kit.Faces.NegZ);

        // The driver's gauges over the right-hand window (DriverGauge): the backhead's four again, in brass bezels.
        string[] dials = ["pressure", "heat", "water", "speed"];
        for (int i = 0; i < 4; i++)
        {
            var c = DriverGauge(shape, i) - new Vector3(0, 0, 0.012f);
            k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
            k.Cylinder(c - new Vector3(0, 0, 0.03f), c + new Vector3(0, 0, 0.01f), DriverGaugeRadius + 0.02f, 12);
            k.Use("gauge_face", Palette.TarnishedBrass * 1.6f, 0.2f, 0.3f, tile: 1);
            var cell = GaugeCell(dials[i]);
            k.Disc(c + new Vector3(0, 0, 0.012f), Vector3.UnitZ, DriverGaugeRadius, 16, cell.Centre, cell.Radius);
        }
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);

        // The back wall over the boiler, up to the roof.
        Plate(k);
        k.Box(new Vector3(-bw, top, cabBack - 0.15f), new Vector3(bw, roofLow, cabBack));

        // The roof: a helmet over the cab, its front drooping into a brow over the windows, its back sweeping down onto
        // the boiler (note 311).
        Roof(k, w, cabFront, cabBack, (float)shape.Solids.Where(x => x.Part == PartKind.CabRoof).Max(x => x.Box.Max.Z), roofLow, roofTop);

        // Inside: the floor, and the backhead, turned to face forward from the back wall.
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0);
        k.Box(new Vector3(-w + 0.1f, deck - 0.02f, cabFront), new Vector3(w - 0.1f, deck + 0.005f, cabBack), Kit.Faces.PosY);
        k.With(BackheadFrame(shape), () => Backhead(k, shape, deck, top, bw));
    }

    /// <summary>
    /// The backhead, in its own frame (<see cref="BackheadFrame"/>: x across as you face it, +Z out of the back wall into the
    /// cab, 0 at the wall): the plate round the firebox door's opening, the firehole's brick, the door's frame, the four
    /// gauges, the water glass and pipework, and the modelled fittings.
    /// </summary>
    static void Backhead(Kit k, CarShape shape, float deck, float top, float bw)
    {
        float face = BackheadDepth;
        float fy = FireDoorUp + deck;
        // The backhead's plate, round the firebox door's opening (it's a hole: through it the fire, drawn with the fire's
        // glow by the scene, and a Stoker if one's in there): riveted boiler plate, sooted, its seams and rivet rows on it.
        k.Use("iron_plate", Palette.SootBlack * 1.6f, 0.8f, 0.35f, tile: 0.9f);
        float ox = FireDoorHalfWidth, oy = FireDoorHalfHeight;
        k.Box(new Vector3(-bw, deck, 0), new Vector3(-ox, top, face), Kit.Faces.PosZ);
        k.Box(new Vector3(ox, deck, 0), new Vector3(bw, top, face), Kit.Faces.PosZ);
        k.Box(new Vector3(-ox, deck, 0), new Vector3(ox, fy - oy, face), Kit.Faces.PosZ);
        k.Box(new Vector3(-ox, fy + oy, 0), new Vector3(ox, top, face), Kit.Faces.PosZ);
        // The firehole's sides, back to the fire: the firebox's lining of firebrick, black with soot, lit by the fire.
        k.Use("brick_soot", Palette.SootBlack * 2.2f, 0.9f, 0.1f, tile: 2.2f);
        k.Box(new Vector3(-ox - 0.02f, fy - oy, -0.1f), new Vector3(-ox, fy + oy, face), Kit.Faces.PosX);
        k.Box(new Vector3(ox, fy - oy, -0.1f), new Vector3(ox + 0.02f, fy + oy, face), Kit.Faces.NegX);
        k.Box(new Vector3(-ox, fy - oy - 0.02f, -0.1f), new Vector3(ox, fy - oy, face), Kit.Faces.PosY);
        k.Box(new Vector3(-ox, fy + oy, -0.1f), new Vector3(ox, fy + oy + 0.02f, face), Kit.Faces.NegY);
        // The firebox door's frame (the glow itself is drawn with the fire).
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        k.Box(new Vector3(-0.42f, fy - 0.3f, face), new Vector3(0.42f, fy - 0.22f, face + 0.08f));
        k.Box(new Vector3(-0.42f, fy + 0.22f, face), new Vector3(0.42f, fy + 0.3f, face + 0.08f));
        k.Box(new Vector3(-0.42f, fy - 0.22f, face), new Vector3(-0.32f, fy + 0.22f, face + 0.08f));
        k.Box(new Vector3(0.32f, fy - 0.22f, face), new Vector3(0.42f, fy + 0.22f, face + 0.08f));
        // Gauges: pressure, heat, water, speed (the gauge atlas's four quarters), in brass bezels at eye height.
        string[] order = ["pressure", "heat", "water", "speed"];
        for (int i = 0; i < 4; i++)
        {
            var c = GaugeLocal(shape, i) - new Vector3(0, 0, 0.012f);
            k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.7f);
            k.Cylinder(c - new Vector3(0, 0, 0.05f), c + new Vector3(0, 0, 0.01f), GaugeRadius + 0.025f, 12);
            k.Use("gauge_face", Palette.TarnishedBrass * 1.6f, 0.2f, 0.3f, tile: 1);
            var cell = GaugeCell(order[i]);
            k.Disc(c + new Vector3(0, 0, 0.012f), Vector3.UnitZ, GaugeRadius, 16, cell.Centre, cell.Radius);
        }
        // The water glass, right of the door, and pipes.
        k.Use("glass_dirty", Palette.BlueGrey, 0.2f, 0.9f, tile: 0.3f);
        k.Box(new Vector3(0.62f, fy + 0.05f, face), new Vector3(0.68f, fy + 0.6f, face + 0.06f));
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.6f, 0.6f);
        k.Rod(new Vector3(-0.8f, deck + 0.2f, face + 0.05f), new Vector3(-0.8f, top - 0.2f, face + 0.05f), 0.025f, 6);
        k.Rod(new Vector3(-0.8f, top - 0.2f, face + 0.05f), new Vector3(0.7f, top - 0.2f, face + 0.05f), 0.025f, 6);
        k.Rod(new Vector3(0.75f, deck + 0.3f, face + 0.05f), new Vector3(0.75f, top - 0.2f, face + 0.05f), 0.02f, 6);
        // The backhead's fittings, modelled (tools/models cab_backhead: the firebox doors ajar, the steam turret and its
        // valves, the injectors, the whistle, the damper), set on the face at the firebox door's centre.
        if (k.Look is { } look && PropArt.Of(look).Get("cab_backhead") is { } fittings)
            k.Append(fittings, Matrix4x4.CreateTranslation(0, fy, face));
    }

    // The backhead's face stands this far into the cab from the back wall (m); the firebox door's centre is this far
    // over the cab floor.
    const float BackheadDepth = 0.085f, FireDoorUp = 0.7f;

    /// <summary>How thick the cab's back wall is (the sim's: CarShape.Engine), the backhead's plate standing off its face.</summary>
    const float BackWall = 0.15f;

    /// <summary>The firebox door's opening, half its width and half its height (m), inside its frame.</summary>
    public const float FireDoorHalfWidth = 0.32f, FireDoorHalfHeight = 0.22f;

    /// <summary>
    /// The backhead's frame in the engine's (note 276): its local x across as you face it, y up from the rail, and +Z out of
    /// the cab's back wall into the cab, 0 at the wall's face. Cab forward, the fireman faces the back of the engine to it,
    /// so it's the engine's frame turned round. Everything on the backhead (its gauges' needles, the shut fire door, the
    /// firebox camera) is placed in it.
    /// </summary>
    public static Matrix4x4 BackheadFrame(CarShape shape) =>
        Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(0, 0, (float)shape.Cab!.Value.Max.Z - BackWall);

    /// <summary>The firebox door's centre on the backhead's face, in the backhead's frame.</summary>
    public static Vector3 FireDoorLocal(CarShape shape)
    {
        var fire = shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        return new Vector3(-(float)fire.X, (float)fire.Y + FireDoorUp, BackheadDepth);
    }

    /// <summary>
    /// The firebox door's centre on the backhead's face, in the engine's frame: where its frame's drawn, and where a
    /// Stoker shows in it when it's open (CreatureArt).
    /// </summary>
    public static Vector3 FireDoor(CarShape shape) => Vector3.Transform(FireDoorLocal(shape), BackheadFrame(shape));

    /// <summary>
    /// A point in the fire, in the engine's frame: <paramref name="up"/> over the firebox door's centre and
    /// <paramref name="behind"/> behind the back wall's face (negative: out in front of it, in the firehole's mouth).
    /// </summary>
    public static Ballast.Double3 InFirebox(CarShape shape, double up, double behind)
    {
        var p = Vector3.Transform(FireDoorLocal(shape) with { Z = -(float)behind } + new Vector3(0, (float)up, 0), BackheadFrame(shape));
        return new(p.X, p.Y, p.Z);
    }

    /// <summary>Out of the backhead's face into the cab, in the engine's frame (a unit direction).</summary>
    public static Ballast.Double3 OutOfBackhead(CarShape shape)
    {
        var d = Vector3.TransformNormal(Vector3.UnitZ, BackheadFrame(shape));
        return new(d.X, d.Y, d.Z);
    }

    /// <summary>
    /// The opening in the cab's front either side of the middle post (note 276), in the engine's frame: from the post to the
    /// cab side's pillar, from the waist to the run map's plate.
    /// </summary>
    public static (float X0, float X1, float Y0, float Y1) FrontWindow(CarShape shape, int side)
    {
        var roof = shape.Solids.First(s => s.Part == PartKind.CabRoof).Box;
        float inner = (float)shape.HalfWidth - 0.1f, deck = (float)shape.Cab!.Value.Min.Y + 0.1f;
        float a = side * 0.07f, b = side * (inner - 0.05f);
        return (MathF.Min(a, b), MathF.Max(a, b), deck + 1.15f, (float)roof.Min.Y - BandHeight - 0.02f);
    }

    /// <summary>How tall the run map's plate over the front windows is (m).</summary>
    const float MapHeight = 0.42f;

    /// <summary>
    /// The band over the front windows (m), from the window tops to the roof: the run map in the middle and the driver's
    /// gauges over the right-hand window.
    /// </summary>
    const float BandHeight = 0.6f;

    /// <summary>The driver's dials' face radius (m): smaller than the backhead's, and nearer the eye.</summary>
    public const float DriverGaugeRadius = 0.11f;

    /// <summary>
    /// The centre of the driver's dial <paramref name="index"/> (pressure, heat, water, speed), in the engine's frame, facing
    /// back into the cab (+Z). Cab forward (note 276), the backhead's behind the driver: as on the real cab-forwards, a
    /// second set stands in front of them, over the right-hand window in two rows of two, where a look up from the line
    /// reads them.
    /// </summary>
    public static Vector3 DriverGauge(CarShape shape, int index)
    {
        var roof = shape.Solids.First(s => s.Part == PartKind.CabRoof).Box;
        float pitch = 2 * (DriverGaugeRadius + 0.035f);
        float x = (float)shape.HalfWidth - 0.32f - (1 - index % 2) * pitch, top = (float)roof.Min.Y - 0.03f - DriverGaugeRadius - 0.025f;
        return new Vector3(x, top - index / 2 * pitch, (float)shape.Cab!.Value.Min.Z + 0.1f);
    }

    /// <summary>The run map's chart on the plate over the front windows (T101), in the engine's frame: its lower left corner and size.</summary>
    public static (Vector3 Corner, float Width, float Height) MapPlate(CarShape shape)
    {
        var roof = shape.Solids.First(s => s.Part == PartKind.CabRoof).Box;
        const float half = 0.58f;
        return (new Vector3(-half - 0.1f, (float)roof.Min.Y - MapHeight - 0.03f, (float)shape.Cab!.Value.Min.Z + 0.1f), 2 * half, MapHeight - 0.02f);
    }

    /// <summary>A dial's face radius on the backhead (T101: big enough to read from anywhere on the footplate).</summary>
    public const float GaugeRadius = 0.15f;

    /// <summary>The centre of dial <paramref name="index"/>'s face (pressure, heat, water, speed), in the backhead's frame.</summary>
    public static Vector3 GaugeLocal(CarShape shape, int index) =>
        new(-0.6f + index * 0.4f, (float)shape.Cab!.Value.Min.Y + 0.1f + 1.85f + index % 2 * 0.06f, BackheadDepth + 0.072f);

    /// <summary>The centre of dial <paramref name="index"/>'s face on the backhead, in the engine's frame.</summary>
    public static Vector3 GaugeCentre(CarShape shape, int index) => Vector3.Transform(GaugeLocal(shape, index), BackheadFrame(shape));

    /// <summary>A painted grip over a lever's handle, centred on it (T101: the brake's red, found at a glance).</summary>
    /// <summary>
    /// Where the whistle cord's handle hangs in the cab (car frame): the sim's whistle interactable (note 264), in the
    /// driver's front corner over the brake valve, at its height (GDD §12: the real whistle has a hand on it; the Whistler's
    /// has none, App. A.4). Hauled down 0.2 m while it blows.
    /// </summary>
    public static Ballast.Double3 WhistleCordHandle(CarShape engine, bool pulled)
    {
        foreach (var i in engine.Interactables)
            if (i.Kind == InteractableKind.Whistle)
                return i.Position + Ballast.Double3.Up * (i.Aim - (pulled ? 0.2 : 0));
        var reg = engine.Levers?.Regulator ?? default;
        return new Ballast.Double3(reg.X - 0.12, reg.Y + (pulled ? 0.22 : 0.4), reg.Z + 0.4);
    }

    /// <summary>
    /// The whistle cord: a waxed cord <paramref name="length"/> down from the cab roof to a T-handle painted signal red (note
    /// 267: "I don't see a switch for a whistle"), so it reads at a glance as the brake's grip does; its origin at the handle.
    /// </summary>
    public static MeshAsset WhistleCord(Look? look, float length)
    {
        var k = new Kit(look, 63);
        k.Use("wood_grey", new Vector3(0.62f, 0.56f, 0.42f), 0.85f, 0.05f, tile: 0.3f);
        k.Cylinder(new Vector3(0, 0.02f, 0), new Vector3(0, length, 0), 0.009f, 5);
        k.Use("paint_oxide", Palette.SignalRed, 0.5f, 0.2f, tile: 0.2f);
        k.Cylinder(new Vector3(-0.1f, 0, 0), new Vector3(0.1f, 0, 0), 0.022f, 6);
        k.Cylinder(new Vector3(0, -0.01f, 0), new Vector3(0, 0.06f, 0), 0.012f, 5);
        return k.Build("whistle-cord");
    }

    public static MeshAsset Grip(Look? look, Vector3 colour)
    {
        var k = new Kit(look, 62);
        k.Use("paint_oxide", colour, 0.5f, 0.2f, tile: 0.2f);
        k.Box(new Vector3(-0.09f, -0.035f, -0.035f), new Vector3(0.09f, 0.035f, 0.035f));
        return k.Build("grip");
    }

    /// <summary>
    /// The blow-off's standpipe (T101), up from the running board to the valve <paramref name="height"/> over it: the pipe
    /// off the boiler, a red handwheel on its side, and the lamp bracket over it.
    /// </summary>
    /// <param name="lamp">The marker lamp over it, to find it in the dark from the cab: not for a valve in the cab (T109).</param>
    public static MeshAsset VentStand(Look? look, float height, bool lamp = true)
    {
        var k = new Kit(look, 63);
        k.Use("copper_pipe", Palette.TarnishedBrass, 0.6f, 0.6f);
        k.Rod(Vector3.Zero, new Vector3(0, height, 0), 0.05f, 6);
        k.Rod(new Vector3(0, height * 0.75f, 0), new Vector3(0.75f, height * 0.75f, 0), 0.04f, 6);
        // The handwheel: a red rim of six, and its cross.
        k.Use("paint_oxide", Palette.SignalRed, 0.5f, 0.2f, tile: 0.2f);
        var wheel = new Vector3(0, height - 0.15f, 0.12f);
        for (int i = 0; i < 6; i++)
        {
            float a0 = i * MathF.PI / 3, a1 = (i + 1) * MathF.PI / 3;
            k.Rod(wheel + new Vector3(MathF.Cos(a0), MathF.Sin(a0), 0) * 0.16f, wheel + new Vector3(MathF.Cos(a1), MathF.Sin(a1), 0) * 0.16f, 0.02f, 4);
        }
        k.Rod(wheel - new Vector3(0.16f, 0, 0), wheel + new Vector3(0.16f, 0, 0), 0.012f, 3);
        if (!lamp)
            return k.Build("vent-stand-cab");
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        k.Rod(new Vector3(0, height, 0), new Vector3(0, height + 0.5f, 0), 0.015f, 4);
        k.Use("lamp_lens", new Vector3(1.0f, 0.35f, 0.15f), 0, 0, tile: 0.25f);
        k.Emissive = 1;
        k.BoxAt(new Vector3(0, height + 0.45f, 0), new Vector3(0.05f, 0.07f, 0.05f));
        k.Emissive = 0;
        return k.Build("vent-stand");
    }

    /// <summary>
    /// The engineering kit's rack (T109) on the cab side: a board on the wall, two pegs, and an amber band painted on so
    /// it's found in the glow of the firebox. Drawn at the rack's interactable, the wall at +X.
    /// </summary>
    public static MeshAsset ToolRack(Look? look)
    {
        var k = new Kit(look, 64);
        k.Use("wood_floor", Palette.DeepBrown, 0.9f, 0.1f, tile: 0.5f);
        k.Box(new Vector3(0.16f, 0.45f, -0.4f), new Vector3(0.2f, 1.02f, 0.4f));
        k.Use("paint_oxide", Palette.LampAmber, 0.6f, 0.1f, tile: 0.2f);
        k.Box(new Vector3(0.15f, 0.93f, -0.38f), new Vector3(0.16f, 0.99f, 0.38f), Kit.Faces.NegX);
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        k.Rod(new Vector3(0.16f, 0.7f, -0.25f), new Vector3(0.06f, 0.7f, -0.25f), 0.018f, 4);
        k.Rod(new Vector3(0.16f, 0.7f, 0.2f), new Vector3(0.06f, 0.7f, 0.2f), 0.018f, 4);
        return k.Build("tool-rack");
    }

    /// <summary>The wrench (T109) hung on its rack's pegs: a long bright iron handle and an open jaw at one end.</summary>
    public static MeshAsset Wrench(Look? look)
    {
        var k = new Kit(look, 65);
        k.Use("steel_grate", new Vector3(0.62f, 0.62f, 0.6f), 0.4f, 0.9f);
        k.Box(new Vector3(0.07f, 0.71f, -0.34f), new Vector3(0.13f, 0.77f, 0.24f));
        k.Box(new Vector3(0.06f, 0.64f, 0.24f), new Vector3(0.14f, 0.86f, 0.32f));
        k.Box(new Vector3(0.06f, 0.81f, 0.32f), new Vector3(0.14f, 0.86f, 0.44f));
        k.Box(new Vector3(0.06f, 0.64f, 0.32f), new Vector3(0.14f, 0.69f, 0.44f));
        return k.Build("wrench");
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

    /// <summary>
    /// A car's number (GDD §32 "something players can say out loud": "car four"), stencilled on in the shops' white
    /// (tools/art stencil_numerals): big on both sides at its front end, where a crewman on the ballast or the next roof
    /// reads it, and on the roof at each end beside the walk, the right way up for someone walking onto it.
    /// </summary>
    public static MeshAsset CarNumber(Look? look, CarShape shape, int number)
    {
        var k = new Kit(look, 63);
        k.Use("stencil_numerals", new Vector3(0.75f, 0.72f, 0.66f), 0.6f, 0, tile: 1);
        string digits = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        void Paint(Vector3 centre, Vector3 normal, Vector3 up, float size)
        {
            var right = Vector3.Normalize(Vector3.Cross(up, normal));
            float cw = size * 0.8f;
            for (int i = 0; i < digits.Length; i++)
            {
                int d = digits[i] - '0';
                var uv0 = new Vector2(d % 5 / 5f, d / 5 / 2f);
                k.Panel(centre + right * ((i - (digits.Length - 1) / 2f) * cw * 0.9f), normal, up, cw, size, uv0, uv0 + new Vector2(0.2f, 0.5f));
            }
        }
        foreach (int side in new[] { -1, 1 })
            Paint(new Vector3(side * (w + 0.02f), h * 0.58f, -l + 1.3f), new Vector3(side, 0, 0), Vector3.UnitY, 0.75f);
        // On the roof, off the walk, to read walking in from either end: up the car at the front, down it at the back.
        Paint(new Vector3(0.68f, h + 0.012f, -l + 0.9f), Vector3.UnitY, -Vector3.UnitZ, 0.6f);
        Paint(new Vector3(-0.68f, h + 0.012f, l - 0.9f), Vector3.UnitY, Vector3.UnitZ, 0.6f);
        return k.Build($"car-number-{number}");
    }

    /// <summary>
    /// The water in an extinguisher's sight glass (SceneArt.Charge): a column a metre high from its foot, scaled to the
    /// charge, a little lit so it reads in a dark car (the glass catches the lamp). A hair wider than the glass (train_stores'
    /// 14 mm), which is opaque: what you see of the glass is the water in it, and the dark glass above.
    /// </summary>
    public static MeshAsset SightWater(Look? look)
    {
        var k = new Kit(look, 61);
        k.Use("glass_dirty", new Vector3(0.45f, 0.72f, 0.68f), 0.1f, 0, tile: 0.1f);
        k.Emissive = 0.6f;
        k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 1, 0), 0.0148f, 8);
        return k.Build("sight-water");
    }

    /// <summary>The red float riding on the water in the sight glass.</summary>
    public static MeshAsset SightFloat(Look? look)
    {
        var k = new Kit(look, 62);
        k.Use("paint_oxide", Palette.SignalRed, 0.3f, 0, tile: 0.1f);
        k.Emissive = 0.5f;
        k.Cylinder(new Vector3(0, -0.01f, 0), new Vector3(0, 0.01f, 0), 0.016f, 8);
        return k.Build("sight-float");
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
    /// The coal bunker in the cab (note 276, the director: "put a coal bunker in the cab"): an iron box against the left wall
    /// ahead of the left doorway, the coal heaped in it, its gate low in the inner side by the fire door where the coal runs
    /// out onto the shovelling plate; over it in the cab roof, the coaling hatch a tower's spout pours through.
    /// </summary>
    static void Bunker(Kit k, CarShape shape, float deck, float roofLow, float roofTop, float w)
    {
        var b = shape.Solids.First(s => s.Part == PartKind.Tender).Box;
        var (min, max) = (F(b.Min), F(b.Max));
        // The box: iron plate, strapped, open at the top; the inner side stops short of the back end for the gate.
        float gate0 = max.Z - 0.75f, gateTop = deck + 0.55f;
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.35f);
        k.Box(new Vector3(min.X, deck, min.Z), new Vector3(max.X, max.Y, min.Z + 0.05f));
        k.Box(new Vector3(min.X, deck, max.Z - 0.05f), new Vector3(max.X, max.Y, max.Z));
        k.Box(new Vector3(max.X - 0.05f, deck, min.Z), new Vector3(max.X, max.Y, gate0));
        k.Box(new Vector3(max.X - 0.05f, gateTop, gate0), new Vector3(max.X, max.Y, max.Z));
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        k.Box(new Vector3(min.X, max.Y - 0.04f, min.Z), new Vector3(max.X + 0.02f, max.Y + 0.02f, max.Z));
        for (float z = min.Z + 0.45f; z < gate0; z += 0.6f)
            k.Box(new Vector3(max.X, deck + 0.05f, z - 0.03f), new Vector3(max.X + 0.02f, max.Y - 0.04f, z + 0.03f));
        // The coal, heaped up over the rim, lumpy.
        k.Use("coal", Palette.SootBlack, 0.4f, 0.5f, tile: 1.2f);
        const int nx = 3, nz = 6;
        Vector3 Coal(int i, int j)
        {
            float x = min.X + (max.X - min.X) * i / nx, z = min.Z + (max.Z - min.Z) * j / nz;
            float bump = MathF.Sin(i * 2.3f + j * 1.7f) * 0.05f + MathF.Sin(i * 5.1f - j * 3.3f) * 0.03f;
            float edge = (i == 0 || i == nx || j == 0 || j == nz) ? -0.1f : 0.06f;
            return new Vector3(x, max.Y + bump + edge, z);
        }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                k.Quad(Coal(i + 1, j + 1), Coal(i, j + 1), Coal(i, j), Coal(i + 1, j));
        // The coal run out of the gate, and the shovelling plate under it.
        k.Quad(new Vector3(max.X - 0.05f, gateTop - 0.05f, max.Z - 0.08f), new Vector3(max.X - 0.05f, gateTop - 0.05f, gate0),
            new Vector3(max.X + 0.3f, deck + 0.04f, gate0 - 0.05f), new Vector3(max.X + 0.3f, deck + 0.04f, max.Z - 0.05f));
        k.Use("iron_plate", Palette.IronGrey, 0.8f, 0.4f);
        k.Box(new Vector3(max.X, deck, gate0 - 0.15f), new Vector3(max.X + 0.7f, deck + 0.02f, max.Z + 0.1f), Kit.Faces.PosY);
        // The coaling hatch over it in the roof: a raised coaming round a lid, hinged on its outer edge.
        float rw = w + 0.12f;
        float RoofY(float x) => RoofProfile(x / rw, roofTop, roofLow);
        float hx0 = min.X + 0.08f, hx1 = max.X - 0.08f, y = RoofY((hx0 + hx1) / 2);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        k.Box(new Vector3(hx0, y - 0.02f, min.Z + 0.15f), new Vector3(hx1, y + 0.1f, max.Z - 0.15f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("iron_plate", Palette.IronGrey * 0.85f, 0.9f, 0.35f);
        k.Box(new Vector3(hx0 - 0.03f, y + 0.1f, min.Z + 0.12f), new Vector3(hx1 + 0.03f, y + 0.13f, max.Z - 0.12f));
        foreach (float z in new[] { min.Z + 0.4f, max.Z - 0.4f })
            k.Cylinder(new Vector3(hx0 - 0.05f, y + 0.11f, z - 0.06f), new Vector3(hx0 - 0.05f, y + 0.11f, z + 0.06f), 0.025f, 6);
    }

    /// <summary>
    /// The engine's rear, where car 1 couples on (note 276): the rear beam, the coupler, the ladders up the back (onto the
    /// boiler, and on up the cab's back wall to its roof and gun), and the tail lamp on the smokebox's corner.
    /// </summary>
    static void RearEnd(Kit k, CarShape shape, float w, float l, float deck, float boilerBack)
    {
        k.Use("paint_oxide", Palette.RustRed, 0.9f, 0.1f);
        k.Box(new Vector3(-w, 0.75f, l - 0.25f), new Vector3(w, deck, l));
        Coupler(k, l, 1, 0.9f);
        // Every ladder up a face along the engine (the cab's side steps are its step irons): the rear end's, the cab's back
        // wall's, and the hatch ladder up from the cab floor to the gun.
        foreach (var ladder in shape.Ladders.Where(x => Math.Abs(x.Inward.Z) > 0))
            RungLadder(k, F(ladder.Foot) - F(ladder.Inward) * 0.04f, (float)(ladder.Top - ladder.Foot.Y), F(ladder.Inward), from: ladder.Foot.Y > 0 ? 0 : 0.5f);
        k.Use("paint_black", Palette.SootBlack, 0.7f, 0.3f);
        var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
        var tail = new Vector3(-(float)boiler.Max.X + 0.15f, (float)boiler.Max.Y - 0.35f, boilerBack + 0.1f);
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

    /// <summary>
    /// A car's livery by its place in the train. (Note 338, "make all the train feel armoured": every one is plate now; the
    /// planked car, a wooden boxcar once, is sheeted over its boards in patched iron.)
    /// </summary>
    public static Livery LiveryOf(int carIndex) => (carIndex % 3) switch { 0 => Livery.Steel, 1 => Livery.Planked, _ => Livery.Armoured };

    /// <summary>
    /// A walk-in car (cargo or guard van): the body over the sim's walls and roof slab, its outside finished by livery,
    /// its inside lined in dark boards with carlines under the roof, the underframe and trucks beneath, couplers, the
    /// side and end ladders, the roof walk and brake wheel; the cargo stacked as crates, the side-door steps as timber.
    /// </summary>
    public static MeshAsset Car(Look? look, CarShape shape, Livery livery, int variant, bool load = true)
    {
        var k = new Kit(look, 7 + variant * 3 + (int)livery);
        var interior = shape.Interior!.Value;
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        float floor = (float)interior.Min.Y + 0.1f, ceiling = (float)interior.Max.Y, t = w - (float)interior.Max.X;
        bool guard = shape.Gun is not null;

        (string Texture, Vector3 Fallback, float Tile) outside = livery switch
        {
            // (Its boards sheeted over in patched plate: note 338.)
            Livery.Planked => ("iron_plate", Palette.IronGrey * 0.75f, 1.3f),
            Livery.Steel => ("paint_oxide", Palette.RustRed, 1.5f),
            _ => ("paint_olive", Palette.MuddyOlive, 1.5f),
        };
        // Every livery rusted dark, the engine's iron (note 338, the director's reference).
        var weather = livery switch { Livery.Planked => IronTint, Livery.Steel => PlateTint, _ => new Vector3(0.62f, 0.55f, 0.45f) };
        void Outside()
        {
            k.Use(outside.Texture, outside.Fallback, 0.9f, 0.2f, outside.Tile);
            k.Tint *= weather;
        }
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
        k.Shade(0.85f);
        float sd = shape.DoorList.Count > 2 ? (float)(shape.DoorList[2].Box.Max.Z - shape.DoorList[2].Box.Min.Z) / 2 : 0;
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w + 0.03f);
            var posts = new List<float>();
            for (float z = -l + 0.06f; z <= l - 0.05f; z += (2 * l - 0.12f) / 8)
                if (sd == 0 || MathF.Abs(z) > sd + 0.1f)
                    posts.Add(z);
            // (A modelled post, tools/models car_body, faces +X from the car side at its foot: turned for the left.)
            string post = "post_steel";
            var turn = side > 0 ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI);
            foreach (float z in posts)
                if (!Prop(k, post, turn * Matrix4x4.CreateScale(1, (ceiling - floor + 0.05f) / 2.7f, 1) * Kit.At(side * w, floor - 0.05f, z)))
                    k.Box(new Vector3(x - 0.03f, floor - 0.05f, z - 0.06f), new Vector3(x + 0.03f, ceiling, z + 0.06f));
            // Every car's plate between its posts braced corner to corner, a war train's (note 338; the director: "make all the
            // train feel armoured"); flat straps on the plate's face.
            {
                k.Use("rust_heavy", Palette.IronGrey * 0.7f, 0.9f, 0.3f);
                for (int i = 0; i + 1 < posts.Count; i++)
                {
                    if (MathF.Abs(posts[i + 1] - posts[i]) > 2.2f)
                        continue;
                    float z0 = posts[i] + 0.1f, z1 = posts[i + 1] - 0.1f, xf = side * w;
                    Strap(k, new Vector3(xf, floor + 0.05f, z0), new Vector3(xf, ceiling - 0.14f, z1), new Vector3(side, 0, 0), 0.08f);
                    Strap(k, new Vector3(xf, ceiling - 0.14f, z0), new Vector3(xf, floor + 0.05f, z1), new Vector3(side, 0, 0), 0.08f);
                }
                Outside();
                k.Shade(0.85f);
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
        // A cargo car's roof hatch (T99) leaves an opening between the walls, framed by a coaming; its lid is drawn by the
        // scene, where the car's state says it is (HatchLid).
        float hz0 = shape.Hatch is { } hb ? (float)hb.Min.Z : l + 1, hz1 = shape.Hatch is { } hb1 ? (float)hb1.Max.Z : l + 1;
        bool InHatch(float z0, float z1) => z1 > hz0 && z0 < hz1;
        var spans = shape.Hatch is null ? new[] { (-l, l) } : new[] { (-l, hz0), (hz1, l) };
        Lining();
        foreach (var (z0, z1) in spans)
            k.Box(new Vector3(-w, ceiling, z0), new Vector3(w, ceiling + 0.01f, z1), Kit.Faces.NegY);
        k.Use("wood_sleeper", Palette.DeepBrown, 0.9f, 0);
        for (float z = -l + 1; z < l - 0.5f; z += 1.1f)
            if (!InHatch(z - 0.05f, z + 0.05f))
                k.Box(new Vector3(-w + t, ceiling - 0.08f, z - 0.05f), new Vector3(w - t, ceiling, z + 0.05f), Kit.Faces.All & ~Kit.Faces.PosY);
        k.Use(livery == Livery.Planked ? "corrugated_iron" : "iron_plate", Palette.IronGrey, 0.9f, 0.3f, tile: 1.2f);
        k.Tint *= IronTint;
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
            foreach (var (z0, z1) in spans)
                k.Prism(profile, z0 == -l ? -l - 0.06f : z0, z1 == l ? l + 0.06f : z1, caps: true, smooth: false);
        }
        if (shape.Hatch is { } hatch)
        {
            // The eaves along the opening, over the wall tops, and the coaming round it.
            float hx = (float)hatch.Max.X;
            foreach (int side in new[] { -1, 1 })
                k.Box(new Vector3(side < 0 ? -w - 0.07f : hx, ceiling - 0.02f, hz0), new Vector3(side < 0 ? -hx : w + 0.07f, h - 0.03f, hz1));
            k.Use("paint_black", Palette.SootBlack, 0.9f, 0.3f);
            foreach (int side in new[] { -1, 1 })
                k.Box(new Vector3(side < 0 ? -hx - 0.06f : hx, h - 0.03f, hz0 - 0.06f), new Vector3(side < 0 ? -hx : hx + 0.06f, h + 0.05f, hz1 + 0.06f));
            k.Box(new Vector3(-hx, h - 0.03f, hz0 - 0.06f), new Vector3(hx, h + 0.05f, hz0));
            k.Box(new Vector3(-hx, h - 0.03f, hz1), new Vector3(hx, h + 0.05f, hz1 + 0.06f));
        }
        // Roof walk: boards on saddles down the safe centreline, gapped: modelled bays laid end to end (tools/models
        // car_body, 1.1 m each, stretched to fit), with the roof sheets' seam caps between them on a steel roof.
        float run = 2 * l - 0.4f;
        int bays = Math.Max(1, (int)MathF.Round(run / 1.1f));
        float bay = run / bays;
        bool modelled = true;
        for (int i = 0; i < bays && modelled; i++)
            if (!InHatch(-l + 0.2f + bay * i, -l + 0.2f + bay * (i + 1)))
                modelled = Prop(k, "roof_walk_bay", Matrix4x4.CreateScale(1, 1, bay / 1.1f) * Kit.At(0, h, -l + 0.2f + bay * (i + 0.5f)));
        if (!modelled)
        {
            k.Use("wood_grey", Palette.TarnishedBrass, 0.9f, 0, 1f);
            foreach (var (z0, z1) in spans)
                for (int i = -2; i <= 2; i++)
                    k.Box(new Vector3(i * 0.14f - 0.06f, h, Math.Max(z0, -l + 0.2f)), new Vector3(i * 0.14f + 0.06f, h + 0.04f, Math.Min(z1, l - 0.2f)), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        else if (livery != Livery.Planked)
            for (int i = 1; i < bays; i++)
                Prop(k, "roof_seam", Matrix4x4.CreateScale(w / 1.5f, 1, 1) * Kit.At(0, h, -l + 0.2f + bay * i));
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
        if (!Prop(k, "brake_gear", Matrix4x4.Identity))
        {
            k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.2f);
            k.Cylinder(new Vector3(0.45f, 0.8f, -1.2f), new Vector3(0.45f, 0.8f, 0.4f), 0.2f, 10);
            k.Cylinder(new Vector3(-0.5f, 0.82f, 0.2f), new Vector3(-0.5f, 0.82f, 0.9f), 0.15f, 10);
        }
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
            // The guard van's short one up from its rear platform (T65).
            else if (ladder.Foot.Z > shape.HalfLength)
                RungLadder(k, F(ladder.Foot), (float)(ladder.Top - ladder.Foot.Y), F(ladder.Inward), from: 0.05f);

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
        if (load)
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

    /// <summary>
    /// The engine's running boards out past the cab sides (App. A.2 GREASE: "sanding from the running boards"): a chequer-plate
    /// walk on brackets, a grab rail along the boiler, and a lidded sandbox on the outer lip of each where the sand's let down.
    /// </summary>
    static void RunningBoards(Kit k, CarShape shape)
    {
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.RunningBoard))
        {
            var (min, max) = (F(solid.Box.Min), F(solid.Box.Max));
            float side = MathF.Sign(min.X + max.X);
            k.Use("steel_grate", Palette.IronGrey, 0.8f, 0.4f, tile: 0.8f);
            k.Box(min, max, Kit.Faces.All);
            // Brackets under it back to the frame.
            k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
            float inner = side > 0 ? min.X : max.X, outer = side > 0 ? max.X : min.X;
            for (float z = min.Z + 0.4f; z < max.Z; z += 2.2f)
                k.Rod(new Vector3(outer - side * 0.05f, min.Y, z), new Vector3(inner, min.Y - 0.45f, z), 0.025f, 5);
        }
        foreach (var box in shape.Interactables.Where(i => i.Kind == InteractableKind.Sandbox))
        {
            var at = F(box.Position);
            float side = MathF.Sign(at.X), lip = at.X + side * 0.22f;
            k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.3f);
            var lo = new Vector3(Math.Min(lip, lip + side * 0.16f), at.Y, at.Z - 0.3f);
            var hi = new Vector3(Math.Max(lip, lip + side * 0.16f), at.Y + 0.4f, at.Z + 0.3f);
            k.Box(lo, hi, Kit.Faces.All & ~Kit.Faces.NegY);
            k.Use("copper_pipe", Palette.TarnishedBrass, 0.7f, 0.5f);
            k.Rod(new Vector3((lo.X + hi.X) / 2, hi.Y, at.Z), new Vector3((lo.X + hi.X) / 2, hi.Y + 0.35f, at.Z), 0.02f, 5); // the lever
            k.Rod(new Vector3((lo.X + hi.X) / 2, at.Y, at.Z), new Vector3((lo.X + hi.X) / 2, 0.25f, at.Z - 0.6f), 0.02f, 5); // the pipe down
        }
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
    /// <summary>
    /// The case a car's load comes in (GDD §19 "physically aboard and readable"), by its cargo: the facility freight's
    /// own models (tools/models freight_*, heavy_crate), or null for the plain crate stack (goods, livestock, a car not
    /// loaded at a facility yet).
    /// </summary>
    public static string? LoadProp(CargoKind cargo) => cargo switch
    {
        CargoKind.Food or CargoKind.Grain => "freight_sacks",
        CargoKind.Ammunition => "freight_ammo",
        CargoKind.Chemicals => "freight_carboys",
        CargoKind.Ore => "freight_ore",
        CargoKind.Heavy => "heavy_crate",
        CargoKind.Salvage => "freight_parts",
        CargoKind.Comet => "freight_comet",
        // The contracts' freight (note 182): the goods mix's own cases, and coal in the ore's lumps.
        CargoKind.Medicine => "freight_medicine",
        CargoKind.Timber => "freight_timber",
        CargoKind.Coal => "freight_ore",
        _ => null,
    };

    /// <summary>
    /// Goods (the cars' own freight, GDD §19 "timber, medicine, machine parts"): a mix by the case, crates, bundles of
    /// timber and chests of medical stores, so a car of goods reads as freight, not one block.
    /// </summary>
    static readonly string[] Goods = ["stores_crate", "freight_timber", "freight_medicine", "freight_parts"];

    /// <summary>
    /// A car's load (its cargo solids, as the sim has them) as its cargo's cases stacked to fill them: each case the size of
    /// a crate's body, scaled a little to the cell, turned a touch either way so the face isn't one plane. The plain crate
    /// stack when the cargo has no case of its own (<see cref="LoadProp"/>) or it isn't built.
    /// </summary>
    /// <summary>
    /// A utility car's fit-out (GDD §10: "experienced crews run engine, armour, cannons, utility cars"; §4's read: "crew /
    /// utility car: cramped, lamp-lit, human-scale"), in the cargo car's body where its load would be: bunks two high down
    /// one side, a pot-bellied stove at the rear end with its pipe up through the roof, a table and benches, lockers and
    /// a coat rail, the floor worn; and outside, its lit windows and the stovepipe smoking, so it reads from the roofs as
    /// where the crew lives. <see cref="StovePipe"/> is where the pipe comes out.
    /// </summary>
    public static MeshAsset UtilityFit(Look? look, CarShape shape)
    {
        var k = new Kit(look, 990);
        var room = shape.Interior!.Value;
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        float floor = (float)room.Min.Y + 0.1f, xIn = (float)room.Max.X, zMin = (float)room.Min.Z + 0.3f, zMax = (float)room.Max.Z - 0.3f;
        // The bunks: down the right side, a lower and an upper berth each with its blanket, end boards between.
        foreach (float y in new[] { floor + 0.45f, floor + 1.35f })
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0, tile: 1);
            k.Box(new Vector3(xIn - 0.75f, y - 0.06f, zMin + 1.2f), new Vector3(xIn, y, zMax - 0.4f));
            k.Use("wool", Palette.BlueGrey, 0.9f, 0, tile: 1.4f);
            k.Box(new Vector3(xIn - 0.72f, y, zMin + 1.25f), new Vector3(xIn - 0.04f, y + 0.12f, zMax - 0.45f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0, tile: 1);
        for (float z = zMin + 1.2f; z <= zMax - 0.35f; z += (zMax - zMin - 1.6f) / 3)
            k.Box(new Vector3(xIn - 0.78f, floor, z - 0.03f), new Vector3(xIn, floor + 1.75f, z + 0.03f));
        // The stove at the rear end on the left, on its iron plate, its door to the car and its pipe up through the roof.
        var stove = StoveAt(shape);
        k.Use("iron_plate", Palette.IronGrey, 0.8f, 0.3f);
        k.Box(new Vector3(stove.X - 0.45f, floor - 0.02f, stove.Z - 0.45f), new Vector3(stove.X + 0.45f, floor + 0.02f, stove.Z + 0.45f));
        k.Use("iron_smokebox", Palette.SootBlack, 0.9f, 0.3f);
        k.Cylinder(new Vector3(stove.X, floor, stove.Z), new Vector3(stove.X, floor + 0.75f, stove.Z), 0.26f, 10, radiusB: 0.22f);
        k.Cylinder(new Vector3(stove.X, floor + 0.75f, stove.Z), new Vector3(stove.X, h + 0.7f, stove.Z), 0.07f, 8);
        k.Lathe(new Vector3(stove.X, h + 0.7f, stove.Z), [new(0.15f, 0), new(0.15f, 0.08f), new(0.02f, 0.2f)], 8, smooth: false);
        // Its door's glow.
        k.Use("ember_crack", Palette.FurnaceOrange, 0.2f, 0);
        k.Panel(new Vector3(stove.X, floor + 0.32f, stove.Z - 0.25f), -Vector3.UnitZ, Vector3.UnitY, 0.18f, 0.14f);
        // A table and its two benches mid-car on the left, a coat rail on the end wall, lockers by the door.
        k.Use("wood_crate", Palette.DeepBrown, 0.85f, 0, tile: 1);
        float tx = -xIn + 0.55f, tz = 0.4f;
        k.Box(new Vector3(tx - 0.4f, floor + 0.72f, tz - 0.6f), new Vector3(tx + 0.4f, floor + 0.78f, tz + 0.6f));
        k.Box(new Vector3(tx - 0.05f, floor, tz - 0.05f), new Vector3(tx + 0.05f, floor + 0.72f, tz + 0.05f));
        foreach (float bz in new[] { tz - 0.95f, tz + 0.95f })
            k.Box(new Vector3(tx - 0.38f, floor + 0.4f, bz - 0.17f), new Vector3(tx + 0.38f, floor + 0.46f, bz + 0.17f));
        // (The kit's car has the crew's own row of lockers there, note 173: those are drawn as the car's.)
        if (shape.Lockers.Count == 0)
        {
            k.Use("paint_olive", Palette.MuddyOlive, 0.9f, 0.2f);
            k.Box(new Vector3(-xIn, floor, zMin + 0.2f), new Vector3(-xIn + 0.45f, floor + 1.8f, zMin + 1.4f));
        }
        k.Use("coat_oilskin", Palette.MuddyOlive, 0.9f, 0.2f, tile: 1);
        for (int i = 0; i < 3; i++)
            k.Box(new Vector3(-xIn + 0.05f, floor + 0.9f, zMax - 2.6f + i * 0.45f), new Vector3(-xIn + 0.2f, floor + 1.6f, zMax - 2.25f + i * 0.45f));
        // Outside: its windows lit, along both sides, and a stencilled band to say what it is.
        k.Use("window_lit", Palette.LampAmber, 0.2f, 0.4f, tile: 0.5f);
        foreach (int side in new[] { -1, 1 })
            for (float z = -l + 2.0f; z < l - 1.6f; z += (2 * l - 3.6f) / 3)
            {
                float x = side * (w + 0.012f);
                k.Panel(new Vector3(x, floor + 1.65f, z), new Vector3(side, 0, 0), Vector3.UnitY, 0.55f, 0.42f);
                k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.3f);
                k.Box(new Vector3(x - 0.03f, floor + 1.9f, z - 0.36f), new Vector3(x + 0.03f, floor + 1.97f, z + 0.36f));
                k.Use("window_lit", Palette.LampAmber, 0.2f, 0.4f, tile: 0.5f);
            }
        return k.Build("utility-fit");
    }

    /// <summary>
    /// Where a utility car's stove stands (its rear end, on the left), so its pipe's smoke comes out over it: the crew car's
    /// own (<see cref="CarShape.Stove"/>, note 184), or where it would go in a cargo car's shell (a still frame's).
    /// </summary>
    public static Vector3 StoveAt(CarShape shape)
    {
        if (shape.Stove is { } stove)
            return new Vector3((float)stove.Centre.X, (float)stove.Min.Y, (float)stove.Centre.Z);
        var room = shape.Interior!.Value;
        return new Vector3((float)room.Min.X + 0.55f, (float)room.Min.Y + 0.1f, (float)room.Max.Z - 0.75f);
    }

    /// <summary>
    /// An armoured car's plate (spec F.3 armoured car conversion, GDD §26 "reinforced plating, heavier mass"; note 184), over
    /// whatever livery it wears: riveted plates hung proud of each side and end wall (clear of its doorways), a deep skirt
    /// down over the trucks, and angle iron along the eaves. Heavy, flat, bolted on: it reads from the roofs and from the
    /// lineside as a different car.
    /// </summary>
    public static MeshAsset ArmourPlate(Look? look, CarShape shape)
    {
        var k = new Kit(look, 1840);
        var room = shape.Interior!.Value;
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength;
        float floor = (float)room.Min.Y + 0.1f, ceiling = (float)room.Max.Y;
        const float Proud = 0.07f, Thick = 0.04f;
        // The side door's opening, if it has one: the plate stops either side of it.
        float sd = shape.DoorList.Where(d => d.Box.Max.Z - d.Box.Min.Z > d.Box.Max.X - d.Box.Min.X).Select(d => (float)(d.Box.Max.Z - d.Box.Min.Z) / 2).DefaultIfEmpty(0).Max();
        var runs = sd > 0 ? new[] { (-l, -sd - 0.12f), (sd + 0.12f, l) } : new[] { (-l, l) };
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side * (w + Proud - Thick), x1 = side * (w + Proud);
            var (lo, hi) = (MathF.Min(x0, x1), MathF.Max(x0, x1));
            foreach (var (z0, z1) in runs)
            {
                // Plates a metre and a half long, seamed, from the skirt's foot to the eaves.
                int plates = Math.Max(1, (int)MathF.Round((z1 - z0) / 1.6f));
                float each = (z1 - z0) / plates;
                for (int i = 0; i < plates; i++)
                {
                    float a = z0 + i * each + 0.012f, b = z0 + (i + 1) * each - 0.012f;
                    k.Use("iron_plate", Palette.IronGrey, 0.95f, 0.35f, tile: 1.1f);
                    k.Box(new Vector3(lo, 0.55f, a), new Vector3(hi, ceiling - 0.05f, b));
                    // Its rivet lines: a strap down each edge and across the top and the floor line.
                    k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
                    float face = side * (w + Proud + 0.012f);
                    var (f0, f1) = (MathF.Min(face, face - side * 0.02f), MathF.Max(face, face - side * 0.02f));
                    foreach (float z in new[] { a + 0.05f, b - 0.05f })
                        k.Box(new Vector3(f0, 0.6f, z - 0.025f), new Vector3(f1, ceiling - 0.1f, z + 0.025f), side > 0 ? Kit.Faces.PosX | Kit.Faces.PosY : Kit.Faces.NegX | Kit.Faces.PosY);
                    foreach (float y in new[] { floor - 0.05f, ceiling - 0.15f })
                        k.Box(new Vector3(f0, y - 0.025f, a + 0.05f), new Vector3(f1, y + 0.025f, b - 0.05f), side > 0 ? Kit.Faces.PosX | Kit.Faces.PosY : Kit.Faces.NegX | Kit.Faces.PosY);
                }
            }
            // Angle iron along the eaves, the whole length.
            k.Use("paint_black", Palette.SootBlack, 0.9f, 0.3f);
            k.Box(new Vector3(MathF.Min(side * w, side * (w + Proud + 0.03f)), ceiling - 0.05f, -l - 0.04f), new Vector3(MathF.Max(side * w, side * (w + Proud + 0.03f)), ceiling + 0.03f, l + 0.04f));
        }
        // The end walls: a plate either side of the end doorway, proud of the wall, down to the end beam.
        var doors = shape.DoorList.Where(d => d.Box.Max.X - d.Box.Min.X > d.Box.Max.Z - d.Box.Min.Z).ToList();
        foreach (int end in new[] { -1, 1 })
        {
            var door = doors.FirstOrDefault(d => MathF.Sign((float)d.Box.Centre.Z) == end);
            float d0 = door.Box.Max.X > door.Box.Min.X ? (float)door.Box.Min.X - 0.06f : 0, d1 = door.Box.Max.X > door.Box.Min.X ? (float)door.Box.Max.X + 0.06f : 0;
            float z0 = end * (l + 0.01f), z1 = end * (l + 0.01f + Thick);
            var (lo, hi) = (MathF.Min(z0, z1), MathF.Max(z0, z1));
            k.Use("iron_plate", Palette.IronGrey, 0.95f, 0.35f, tile: 1.1f);
            k.Box(new Vector3(-w - Proud, floor - 0.2f, lo), new Vector3(d0, ceiling - 0.05f, hi));
            k.Box(new Vector3(d1, floor - 0.2f, lo), new Vector3(w + Proud, ceiling - 0.05f, hi));
        }
        return k.Build("armour-plate");
    }

    /// <summary>
    /// Roof handrails (spec F.3 "Dragger resistance"; note 184): an iron rail on stanchions down each edge of the roof, knee
    /// high, a hand's reach from the walk, with gaps at the ladder heads so the ladders still come up onto the roof.
    /// </summary>
    public static MeshAsset RoofHandrails(Look? look, CarShape shape)
    {
        var k = new Kit(look, 1841);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        const float Height = 0.5f, In = 0.08f;
        k.Use("rust_heavy", Palette.IronGrey, 0.85f, 0.35f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w - In);
            // Broken where a side ladder comes up at this edge.
            var heads = shape.Ladders.Where(d => d.Foot.Y < 0.2 && MathF.Sign((float)d.Foot.X) == side && Math.Abs(d.Foot.X) > w).Select(d => (float)d.Foot.Z).ToList();
            var runs = new List<(float, float)>();
            float from = -l + 0.35f;
            foreach (float z in heads.Order())
            {
                if (z - 0.45f > from)
                    runs.Add((from, z - 0.45f));
                from = z + 0.45f;
            }
            if (l - 0.35f > from)
                runs.Add((from, l - 0.35f));
            foreach (var (z0, z1) in runs)
            {
                k.Rod(new Vector3(x, h + Height, z0), new Vector3(x, h + Height, z1), 0.022f, 6);
                int posts = Math.Max(1, (int)MathF.Ceiling((z1 - z0) / 2.2f));
                for (int i = 0; i <= posts; i++)
                {
                    float z = z0 + (z1 - z0) * i / posts;
                    k.Rod(new Vector3(x, h - 0.02f, z), new Vector3(x, h + Height, z), 0.018f, 4);
                }
            }
        }
        return k.Build("roof-handrails");
    }

    public static MeshAsset Load(Look? look, CarShape shape, CargoKind cargo, int variant)
    {
        var k = new Kit(look, 64 + (int)cargo);
        var name = LoadProp(cargo);
        var prop = name is not null && look is not null ? PropArt.Of(look).Get(name) : null;
        var mix = cargo == CargoKind.Goods && look is not null ? Goods.Select(g => PropArt.Of(look).Get(g)).OfType<MeshAsset>().ToArray() : [];
        foreach (var solid in shape.Solids.Where(s => s.Part == PartKind.Cargo))
        {
            var (min, max) = (F(solid.Box.Min), F(solid.Box.Max));
            // Livestock: no cases, a pen: straw down and a rail along its open side (the animals: SceneArt.Livestock).
            if (cargo == CargoKind.Livestock)
            {
                Pen(k, min, max);
                continue;
            }
            if (mix.Length > 1)
            {
                GoodsStack(k, mix, min, max, variant);
                continue;
            }
            if (prop is null)
            {
                CrateStack(k, min, max, variant);
                continue;
            }
            var (pmin, pmax) = Extent(prop);
            var psize = pmax - pmin;
            var size = max - min;
            int nx = Math.Max(1, (int)MathF.Round(size.X / psize.X)), ny = Math.Max(1, (int)MathF.Round(size.Y / psize.Y)),
                nz = Math.Max(1, (int)MathF.Round(size.Z / psize.Z));
            var cell = new Vector3(size.X / nx, size.Y / ny, size.Z / nz);
            var scale = new Vector3(cell.X / psize.X, cell.Y / psize.Y, cell.Z / psize.Z) * 0.97f;
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        float jitter = MathF.Sin((x * 7 + y * 13 + z * 5 + variant) * 1.7f);
                        var centre = min + cell * new Vector3(x + 0.5f, y, z + 0.5f) - new Vector3(0, pmin.Y * scale.Y, 0);
                        k.Append(prop, Matrix4x4.CreateTranslation(-(pmin + pmax) * new Vector3(0.5f, 0, 0.5f)) * Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateRotationY(jitter * 0.05f + ((x + y + z) % 2) * MathF.PI) * Matrix4x4.CreateTranslation(centre));
                    }
        }
        return k.Build($"load-{cargo}-{variant}");
    }

    /// <summary>A livestock pen in a load's volume: straw bedding over its floor, posts and two rails along the aisle side.</summary>
    static void Pen(Kit k, Vector3 min, Vector3 max)
    {
        k.Use("grass_card", new Vector3(0.75f, 0.62f, 0.38f), 0.95f, 0, tile: 0.6f);
        k.Box(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y + 0.05f, max.Z));
        k.Use("wood_grey", Palette.DeepBrown, 0.85f, 0, tile: 1.2f);
        for (float z = min.Z; z <= max.Z + 0.01f; z += (max.Z - min.Z) / MathF.Max(1, MathF.Round((max.Z - min.Z) / 1.2f)))
            k.Box(new Vector3(min.X - 0.04f, min.Y, z - 0.04f), new Vector3(min.X + 0.04f, min.Y + 1.15f, z + 0.04f));
        foreach (float y in new[] { 0.55f, 1.05f })
            k.Box(new Vector3(min.X - 0.03f, min.Y + y, min.Z), new Vector3(min.X + 0.03f, min.Y + y + 0.1f, max.Z));
    }

    /// <summary>A volume of goods: crate-sized cells, each one of <paramref name="mix"/> by a hash of the cell, scaled into it.</summary>
    static void GoodsStack(Kit k, MeshAsset[] mix, Vector3 min, Vector3 max, int variant)
    {
        var size = max - min;
        const float cellSize = 0.88f;
        int nx = Math.Max(1, (int)MathF.Round(size.X / cellSize)), ny = Math.Max(1, (int)MathF.Round(size.Y / cellSize)),
            nz = Math.Max(1, (int)MathF.Round(size.Z / cellSize));
        var cell = new Vector3(size.X / nx, size.Y / ny, size.Z / nz);
        for (int x = 0; x < nx; x++)
            for (int y = 0; y < ny; y++)
                for (int z = 0; z < nz; z++)
                {
                    uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ variant * 2654435761);
                    var prop = mix[h % (uint)mix.Length];
                    var (pmin, pmax) = Extent(prop);
                    var psize = pmax - pmin;
                    var scale = new Vector3(cell.X / psize.X, cell.Y / psize.Y, cell.Z / psize.Z) * 0.97f;
                    // Each case keeps its own proportions, fitted to the cell by its tightest side.
                    float s = MathF.Min(scale.X, MathF.Min(scale.Y, scale.Z));
                    var centre = min + cell * new Vector3(x + 0.5f, y, z + 0.5f) - new Vector3(0, pmin.Y * s, 0);
                    float jitter = MathF.Sin((x * 7 + y * 13 + z * 5 + variant) * 1.7f);
                    k.Append(prop, Matrix4x4.CreateTranslation(-(pmin + pmax) * new Vector3(0.5f, 0, 0.5f)) * Matrix4x4.CreateScale(s)
                        * Matrix4x4.CreateRotationY(jitter * 0.08f + (h >> 8) % 2 * MathF.PI) * Matrix4x4.CreateTranslation(centre));
                }
    }

    static (Vector3 Min, Vector3 Max) Extent(MeshAsset m)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in m.Vertices)
        {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        return (lo, hi);
    }

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

    /// <summary>
    /// One leaf of a cargo car's roof hatch (T99), <paramref name="size"/> (centred, its top face up): an iron plate, its
    /// hinges along its +X edge, and along its −X edge (the roof's centreline, where the two leaves meet) its half of the
    /// roof walk and a grab handle at its front.
    /// </summary>
    public static MeshAsset HatchLid(Look? look, Vector3 size)
    {
        var k = new Kit(look, 41);
        var half = size / 2;
        k.Use("iron_plate", Palette.IronGrey, 0.9f, 0.3f, tile: 1.2f);
        k.Box(-half, half);
        k.Use("wood_grey", Palette.TarnishedBrass, 0.9f, 0, 1f);
        for (int i = 0; i < 3; i++)
        {
            float x = -half.X + 0.01f + i * 0.14f;
            k.Box(new Vector3(x, half.Y, -half.Z + 0.05f), new Vector3(x + 0.12f, half.Y + 0.04f, half.Z - 0.05f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        foreach (float z in new[] { -half.Z + 0.35f, half.Z - 0.35f })
            k.Box(new Vector3(half.X - 0.45f, half.Y, z - 0.05f), new Vector3(half.X + 0.03f, half.Y + 0.02f, z + 0.05f));
        k.Use("brass", Palette.TarnishedBrass, 0.5f, 0.8f);
        k.Rod(new Vector3(-half.X + 0.5f, half.Y + 0.03f, -half.Z + 0.12f), new Vector3(-half.X + 0.85f, half.Y + 0.03f, -half.Z + 0.12f), 0.025f);
        return k.Build("hatch-lid");
    }

    /// <summary>A door leaf of <paramref name="size"/> (centred): planks with iron straps, an end door with a small window.</summary>
    public static MeshAsset Door(Look? look, Vector3 size, bool side)
    {
        var k = new Kit(look, side ? 31 : 37);
        var half = size / 2;
        // Plate, not boards (note 338: the whole train armoured): a door slab of riveted iron, strapped and braced.
        k.Use("iron_plate", Palette.IronGrey * 0.9f, 0.95f, 0.35f, tile: 1.1f);
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
    /// <summary>
    /// The gun rail along a roof's centreline (T93): a pair of iron flats on tie plates, over whatever roof is there (the
    /// engine's steps down from the cab roof to the tender), for the gun to be pushed along.
    /// </summary>
    public static MeshAsset RoofRail(Look? look, CarShape shape, (double Front, double Back) rail)
    {
        var k = new Kit(look, 61);
        const float gauge = 0.16f, flat = 0.025f, height = 0.05f;
        // Runs of level roof: a new run wherever the roof under the centreline steps.
        var runs = new List<(float From, float To, float Top)>();
        const double step = 0.1;
        for (double z = rail.Front; z < rail.Back - 1e-6; z += step)
        {
            float top = (float)(shape.TopAt(0, z + step / 2)?.Top ?? shape.RoofHeight), to = (float)Math.Min(z + step, rail.Back);
            if (runs.Count > 0 && Math.Abs(runs[^1].Top - top) < 1e-3)
                runs[^1] = (runs[^1].From, to, top);
            else
                runs.Add(((float)z, to, top));
        }
        foreach (var (from, to, top) in runs)
        {
            k.Use("rust_heavy", Palette.IronGrey, 0.6f, 0.6f);
            foreach (int s in new[] { -1, 1 })
                k.Box(new Vector3(s * gauge - flat, top, from), new Vector3(s * gauge + flat, top + height, to));
            k.Use("iron_plate", Palette.SootBlack, 0.9f, 0.3f);
            for (float z = from + 0.3f; z < to; z += 1.2f)
                k.Box(new Vector3(-gauge - 0.08f, top, z - 0.05f), new Vector3(gauge + 0.08f, top + 0.015f, z + 0.05f));
        }
        return k.Build("roof-rail");
    }

    /// <summary>
    /// The cannon's pieces (tools/models/recipes/cannon.py, note 137), each in the gun's frame: the pivot (traverse and
    /// trunnions) at the origin, 0.9 m over the roof, the barrel out along −Z. The mount stays on the roof; the carriage
    /// (the seat, the tiller and handwheel, the shield) turns about Y; the barrel elevates about X on the carriage; a
    /// powder chamber sits in the breech at <see cref="CannonChamber"/> when it's loaded. Null without the look's props.
    /// </summary>
    public static (MeshAsset Mount, MeshAsset Carriage, MeshAsset Barrel, MeshAsset Chamber)? Cannon(Look? look)
    {
        if (look is null)
            return null;
        var props = PropArt.Of(look);
        return props.Get("cannon_mount") is { } m && props.Get("cannon_carriage") is { } c && props.Get("cannon_barrel") is { } b
            && props.Get("cannon_chamber") is { } ch ? (m, c, b, ch) : null;
    }

    /// <summary>
    /// Where the gunner sits on the cannon's carriage (its seat pan's top, the hips; the carriage's frame): behind the
    /// breech, the tiller under the left hand and the elevating handwheel under the right.
    /// </summary>
    public static readonly Vector3 CannonSeat = new(0, -0.42f, 0.75f);

    /// <summary>A loaded powder chamber's base in the breech (the barrel's frame; its mouth 0.2 m on, at the bore).</summary>
    public static readonly Vector3 CannonChamber = new(0, 0, 0.44f);

    /// <summary>The muzzle (the barrel's frame).</summary>
    public static readonly Vector3 CannonMuzzle = new(0, 0, -1.5f);

    /// <summary>
    /// The cannon's shield (note 338; the director's reference, the turret on the engine's cab): riveted plate round the
    /// carriage, in its frame so it turns with the gun. A front plate cut back at its corners with a notch the barrel
    /// rides in, cheeks either side back past the breech, a brow over the front; low enough that the seated gunner aims
    /// over it, open at the back where they sit.
    /// </summary>
    public static MeshAsset GunShield(Look? look)
    {
        var k = new Kit(look, 61);
        const float z0 = -0.66f, y0 = -0.62f, y1 = 0.12f, half = 0.72f;
        Plate(k, 1.0f);
        // The front plate, either side of the barrel's notch and under it.
        k.Box(new Vector3(-half + 0.18f, y0, z0 - 0.04f), new Vector3(-0.24f, y1, z0));
        k.Box(new Vector3(0.24f, y0, z0 - 0.04f), new Vector3(half - 0.18f, y1, z0));
        k.Box(new Vector3(-0.24f, y0, z0 - 0.04f), new Vector3(0.24f, -0.3f, z0));
        // Its corners cut back, and the cheeks along the carriage's sides.
        foreach (int side in new[] { -1, 1 })
        {
            var a = new Vector3(side * (half - 0.18f), 0, z0 - 0.02f);
            var b = new Vector3(side * half, 0, z0 + 0.22f);
            k.Quad(a with { Y = y1 }, b with { Y = y1 }, b with { Y = y0 }, a with { Y = y0 }, twoSided: true);
            float x = side * half;
            k.Box(new Vector3(MathF.Min(x, x - side * 0.04f), y0, z0 + 0.22f), new Vector3(MathF.Max(x, x - side * 0.04f), y1 - 0.05f, 0.45f));
            // A riveted strap down the corner.
            Rusted(k).Shade(0.5f);
            k.Rod(a with { Y = y0 } + new Vector3(0, 0, -0.03f), a with { Y = y1 } + new Vector3(0, 0, -0.03f), 0.025f);
            Plate(k, 1.0f);
        }
        // The brow over the front, raked forward, the notch under it.
        k.Quad(new Vector3(-half + 0.18f, y1, z0 - 0.04f), new Vector3(half - 0.18f, y1, z0 - 0.04f), new Vector3(half - 0.1f, y1 + 0.08f, z0 + 0.3f),
            new Vector3(-half + 0.1f, y1 + 0.08f, z0 + 0.3f), twoSided: true);
        Rusted(k).Shade(0.5f);
        k.Box(new Vector3(-half + 0.15f, y0 + 0.08f, z0 - 0.07f), new Vector3(half - 0.15f, y0 + 0.16f, z0 - 0.04f), Kit.Faces.All & ~Kit.Faces.PosZ);
        return k.Build("gun-shield");
    }

    public static MeshAsset Gun(Look? look)
    {
        // The cannon assembled at rest, loaded (for the catalog and `dt art show gun`); the scene draws its pieces.
        if (Cannon(look) is { } cannon)
        {
            var a = new Kit(look, 53);
            a.Append(cannon.Mount, Matrix4x4.Identity);
            a.Append(cannon.Carriage, Matrix4x4.Identity);
            a.Append(cannon.Barrel, Matrix4x4.Identity);
            a.Append(cannon.Chamber, Matrix4x4.CreateTranslation(CannonChamber));
            return a.Build("gun");
        }
        // The modelled machine gun it replaced (tools/models gun_mount) to this frame: pivot at the origin, muzzle 1.5 m
        // out along -Z.
        if (look is not null && PropArt.Of(look).Get("gun_mount") is { } modelled)
            return modelled;
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
