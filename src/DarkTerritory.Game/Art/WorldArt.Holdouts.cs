using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Holdouts (GDD App. D.4 "Fiction and art"): a derelict penal transport on its spare siding, its windows barred; a
/// signal box, lamp room or water tower a wildlander has barricaded; a halt's caged waiting room. Each has its lamp on
/// the corner the train sees first, dark until someone's in there (the scene lights it: <see cref="HoldoutLamp"/>).
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>How high a Holdout's lamp hangs, by its building.</summary>
    public static double LampHeight(BuildingKind kind) => kind switch
    {
        BuildingKind.SignalBox => 5.2,
        BuildingKind.WaterTower => 3.2,
        BuildingKind.PrisonCar => 3.4,
        _ => 2.6,
    };

    /// <summary>A point in a footprint's own frame (along its length, across), as the layout's.</summary>
    static (double X, double Y) Local(StopBuilding b, Pt p)
    {
        var u = new Pt(Math.Cos(b.Yaw), Math.Sin(b.Yaw));
        var r = p - b.Centre;
        return (r.S * u.S + r.D * u.D, r.S * u.Normal.S + r.D * u.Normal.D);
    }

    /// <param name="facing">Its door's side, in the kit's frame (the building's axis along −Z).</param>
    void Holdout(Kit k, StopLayout stop, int index, Vector3 facing)
    {
        var b = stop.Buildings[index];
        float length = (float)b.Length, width = (float)b.Width;
        switch (b.Kind)
        {
            case BuildingKind.PrisonCar:
                PrisonCar(k, length, facing.X >= 0 ? 1 : -1);
                break;
            case BuildingKind.Lockup:
                Lockup(k, length, width, facing, (float)_look.Walls.PersonDoorM);
                break;
            case BuildingKind.SignalBox:
                // Its locking room is the shelter (note 387): the sim's walls, its door where it's broken into, and over it
                // the box with its windows all round, a hipped roof and the stair up the side away from the door.
                ShelterRoom(k, stop, index, 2.6f, "brick_soot", Palette.RustRed, 1.2f, out _);
                SignalCabin(k, b, facing);
                break;
            case BuildingKind.WaterTower when _props.Get("water_tower") is { } tower:
                {
                    // Someone's boarded themselves into the pump house under the tank: the sim's walls, the tower's legs
                    // standing in it, the pump in the middle and its pipe up through the roof to the tank.
                    ShelterRoom(k, stop, index, 2.4f, "brick_soot", Palette.RustRed, 1.2f, out _);
                    k.Use("wood_sleeper", Palette.DeepBrown, 0.85f, 0, tile: 1.3f);
                    k.Box(new Vector3(-width / 2 - 0.15f, 2.4f, -length / 2 - 0.15f), new Vector3(width / 2 + 0.15f, 2.6f, length / 2 + 0.15f));
                    k.Append(tower, Matrix4x4.CreateRotationY(MathF.Atan2(facing.X, facing.Z)) * Kit.At(0, -0.2f, 0));
                    Pump(k);
                    break;
                }
            default:
                {
                    // A lamp room (or a water tower without its model): a squat brick hut, a slate roof, the door planked
                    // over from inside; within, the shelf of lamps and oil it was for.
                    ShelterRoom(k, stop, index, 2.8f, "brick_soot", Palette.RustRed, 1.2f, out var back);
                    k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
                    k.Box(new Vector3(-width / 2 - 0.3f, 2.8f, -length / 2 - 0.3f), new Vector3(width / 2 + 0.3f, 3.1f, length / 2 + 0.3f), Kit.Faces.All);
                    LampShelf(k, back);
                    break;
                }
        }
    }

    /// <summary>
    /// A Holdout's way in (App. D.7), drawn by its state each frame (<see cref="Entrances"/>): the prison car's door leaf and
    /// padlock, a shelter's barricade, a lockup's gate. Shut while anyone's in there or nobody is; freed, broken open: the
    /// car's door swung wide, its padlock smashed off into the ballast (or hanging open on the hasp, picked with the repair
    /// kit); the barricade's boards pried off, two hanging, the rest on the ground, the doorway dark; the cage's gate open.
    /// </summary>
    void Entrance(Kit k, StopBuilding b, Vector3 facing, bool open, bool picked)
    {
        float length = (float)b.Length, width = (float)b.Width;
        switch (b.Kind)
        {
            case BuildingKind.PrisonCar:
                {
                    int door = facing.X >= 0 ? 1 : -1;
                    const float w = 1.5f, floor = 1.1f;
                    float x = door * (w + 0.02f), h = k.DoorHeight();
                    k.Use("paint_oxide", Palette.BlueGrey * 0.7f, 0.95f, 0.2f, tile: 1.5f);
                    // The leaf hangs on its hinges at the van's −Z edge of the doorway: swung out wide, it stands off the side.
                    var hinge = Matrix4x4.CreateRotationY(open ? door * 1.75f : 0) * Matrix4x4.CreateTranslation(x, 0, -1.0f);
                    k.With(hinge, () => k.Box(new Vector3(-0.04f, floor + 0.1f, 0), new Vector3(0.04f, floor + 0.1f + h, 2.0f)));
                    // (Open, the doorway shows the van's inside: PrisonCar draws it hollow, note 387.)
                    k.Use("brass", Palette.TarnishedBrass, 0.7f, 0.5f);
                    if (!open)
                        k.BoxAt(new Vector3(door * (w + 0.1f), 2.0f, 0.9f), new Vector3(0.05f, 0.12f, 0.09f));
                    else if (picked)
                        // Opened with the kit: the padlock hanging open from the staple, its shackle up.
                        k.BoxAt(new Vector3(door * (w + 0.1f), 1.85f, -0.95f), new Vector3(0.05f, 0.08f, 0.09f));
                    else
                        // Smashed off: lying in the ballast under the door, the hasp twisted with it.
                        k.With(Matrix4x4.CreateRotationZ(1.2f) * Matrix4x4.CreateTranslation(door * (w + 0.7f), 0.08f, 0.6f),
                            () => k.BoxAt(Vector3.Zero, new Vector3(0.05f, 0.12f, 0.09f)));
                    break;
                }
            case BuildingKind.Lockup:
                {
                    // The gate: an iron frame of bars across the cage's end (or side) the crew comes to, padlocked shut.
                    bool end = MathF.Abs(facing.Z) > 0;
                    float halfSpan = (float)_look.Walls.PersonDoorM / 2, at = end ? length / 2 + 0.03f : width / 2 + 0.03f;
                    var place = end ? Matrix4x4.CreateRotationY(facing.Z > 0 ? 0 : MathF.PI) : Matrix4x4.CreateRotationY(facing.X > 0 ? MathF.PI / 2 : -MathF.PI / 2);
                    k.With(place, () =>
                    {
                        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
                        var swing = Matrix4x4.CreateRotationY(open ? 1.6f : 0) * Matrix4x4.CreateTranslation(-halfSpan, 0, at);
                        k.With(swing, () =>
                        {
                            for (float gx = 0; gx <= 2 * halfSpan + 0.01f; gx += 0.2f)
                                k.Rod(new Vector3(gx, 0.25f, 0.02f), new Vector3(gx, 2.4f, 0.02f), 0.014f);
                            foreach (float y in new[] { 0.35f, 1.3f, 2.35f })
                                k.Rod(new Vector3(0, y, 0.02f), new Vector3(2 * halfSpan, y, 0.02f), 0.025f);
                        });
                        k.Use("brass", Palette.TarnishedBrass, 0.7f, 0.5f);
                        if (!open)
                            k.BoxAt(new Vector3(halfSpan - 0.05f, 1.3f, at + 0.06f), new Vector3(0.05f, 0.08f, 0.04f));
                        else if (!picked)
                            k.BoxAt(new Vector3(halfSpan + 0.3f, 0.3f, at + 0.4f), new Vector3(0.05f, 0.04f, 0.08f));
                    });
                    break;
                }
            default:
                {
                    // A shelter: the barricade across its doorway, the sim's in the middle of the face it's broken into at
                    // (every shelter is the sim's walls now, note 387).
                    var centre = facing * ((MathF.Abs(facing.X) > 0 ? width / 2 : length / 2) + 0.05f);
                    float half = (float)_look.Walls.PersonDoorM / 2 + 0.25f;
                    // (Its sill's the room's boards, a step up: ShelterRoom.)
                    if (!open)
                        Barricade(k, centre + Doorway(k) + Vector3.UnitY * 0.25f, facing, half, k.DoorHeight());
                    else
                        PriedOff(k, centre, facing, half, k.DoorHeight());
                    break;
                }
        }
    }

    /// <summary>A barricade pried off (D.7): two boards hanging by a nail at one end, the rest down on the ground before the doorway, which stands open on the room (note 387).</summary>
    static void PriedOff(Kit k, Vector3 sill, Vector3 outward, float halfWidth, float height)
    {
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, outward));
        k.Shade(0.3f);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        foreach (int i in new[] { 0, 3 })
        {
            // Hanging off the left jamb's nail, swung down.
            var nail = sill + across * -halfWidth + Vector3.UnitY * (0.25f + i * (height - 0.4f) / 4 + 0.2f) + outward * 0.08f;
            k.Rod(nail, nail + across * 0.6f - Vector3.UnitY * (1.0f + i * 0.08f) + outward * 0.05f, 0.07f);
        }
        for (int i = 0; i < 3; i++)
        {
            var a = sill + outward * (0.5f + i * 0.35f) + across * (-halfWidth * 0.9f + i * 0.2f) + Vector3.UnitY * 0.07f;
            k.Rod(a, a + across * (2 * halfWidth * 0.85f) + outward * ((i - 1) * 0.3f), 0.07f);
        }
    }

    /// <summary>
    /// Each Holdout's way in near <paramref name="eye"/>, by its state (<see cref="Entrance"/>): placed as the stop's own
    /// building is (<see cref="Building"/>), so it sits where the baked building expects it.
    /// </summary>
    public void Entrances(MeshBuilder mesh, RailLine line, Route? route, Sim.Run.Holdouts holdouts, Double3 eye, float valleyDepth)
    {
        if (route is null)
            return;
        var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)) };
        foreach (var h in holdouts.All)
        {
            if (h.Site.Stop is not { } stop || (h.Door - eye).Length > 160)
                continue;
            var b = stop.Buildings[h.Layout.Building];
            var f = h.Site;
            double along = f.Start + b.S;
            var t = line.Sample(Math.Clamp(along, 0, line.Length));
            var at = Sim.Run.Run.StopWorld(line, f, b.Centre, Ground(route, along, (float)b.D, valleyDepth) - 0.15);
            var frame = Basis(t.Tangent, at, eye, (float)-b.Yaw);
            var (dx, dy) = Local(b, h.Layout.Door);
            var facing = Math.Abs(dx) / b.Length > Math.Abs(dy) / b.Width ? new Vector3(0, 0, (float)-Math.Sign(dx)) : new Vector3((float)Math.Sign(dy), 0, 0);
            bool open = h.State == Sim.Run.HoldoutState.Freed;
            k.Reseed(b.Variant * 7.1f + (float)(b.S * 0.13));
            k.With(frame, () => Entrance(k, b, facing, open, open && h.Quiet));
        }
    }

    /// <summary>A derelict penal transport (D.4): an iron van on its bogies, barred slits, the door padlocked, on its siding.</summary>
    static void PrisonCar(Kit k, float siding, int door)
    {
        // The spare siding: sleepers and two rails, the points long gone; on the ground, not under it (the frame stands
        // 0.15 m under the ground, as every stop building's does).
        const float rail = 0.36f;
        k.Use("wood_sleeper", Palette.DeepBrown, 0.8f, 0, tile: 1.3f);
        for (float z = -siding / 2 + 0.4f; z < siding / 2; z += 0.75f)
            k.Box(new Vector3(-1.3f, 0.05f, z - 0.12f), new Vector3(1.3f, rail - 0.12f, z + 0.12f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side * TrainKit.HalfGauge - 0.035f, rail - 0.12f, -siding / 2), new Vector3(side * TrainKit.HalfGauge + 0.035f, rail, siding / 2));
        // The van, 14 m, on two bogies: their wheels on the rails, the side frames over the axle boxes outside them, a
        // bolster across under the van (note 387: they were a block with the wheels inside it).
        const float half = 7, w = 1.5f, floor = 1.1f, top = 3.6f, wheel = 0.42f;
        k.Use("wheel_iron", Palette.IronGrey, 0.8f, 0.4f, tile: 0.5f);
        foreach (float z in new[] { -4.8f, 4.8f })
        {
            foreach (float dz in new[] { -0.8f, 0.8f })
            {
                foreach (int side in new[] { -1, 1 })
                    k.Cylinder(new Vector3(side * TrainKit.HalfGauge - 0.06f, rail + wheel, z + dz), new Vector3(side * TrainKit.HalfGauge + 0.06f, rail + wheel, z + dz), wheel, 12);
                k.Rod(new Vector3(-TrainKit.HalfGauge, rail + wheel, z + dz), new Vector3(TrainKit.HalfGauge, rail + wheel, z + dz), 0.06f, 8);
            }
            k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (TrainKit.HalfGauge + 0.14f);
                k.Box(new Vector3(x - 0.05f, rail + wheel - 0.12f, z - 1.25f), new Vector3(x + 0.05f, rail + wheel + 0.14f, z + 1.25f));
                foreach (float dz in new[] { -0.8f, 0.8f })
                    k.Box(new Vector3(x - 0.09f, rail + wheel - 0.14f, z + dz - 0.16f), new Vector3(x + 0.09f, rail + wheel + 0.16f, z + dz + 0.16f));
            }
            k.Box(new Vector3(-1.2f, rail + wheel + 0.14f, z - 0.3f), new Vector3(1.2f, floor, z + 0.3f));
            k.Use("wheel_iron", Palette.IronGrey, 0.8f, 0.4f, tile: 0.5f);
        }
        // Its body hollow (note 387): plate walls, floor and roof, so its door broken open shows the cell inside. The door's
        // opening is where Entrance hangs its leaf: 2 m along the middle of the crew's side, the standard door's height.
        const float plate = 0.08f, d0 = -1.0f, d1 = 1.0f;
        float lintel = floor + 0.1f + k.DoorHeight();
        k.Use("paint_oxide", Palette.BlueGrey * 0.7f, 0.95f, 0.2f, tile: 1.5f);
        k.Box(new Vector3(-w, floor, -half), new Vector3(w, floor + 0.1f, half), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Box(new Vector3(-w, top - plate, -half), new Vector3(w, top, half), Kit.Faces.All);
        foreach (float z in new[] { -half, half - plate })
            k.Box(new Vector3(-w, floor + 0.1f, z), new Vector3(w, top - plate, z + plate), Kit.Faces.Sides);
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side > 0 ? w - plate : -w, x1 = side > 0 ? w : -w + plate;
            if (side != door)
            {
                k.Box(new Vector3(x0, floor + 0.1f, -half + plate), new Vector3(x1, top - plate, half - plate), Kit.Faces.Sides);
                continue;
            }
            k.Box(new Vector3(x0, floor + 0.1f, -half + plate), new Vector3(x1, top - plate, d0), Kit.Faces.Sides);
            k.Box(new Vector3(x0, floor + 0.1f, d1), new Vector3(x1, top - plate, half - plate), Kit.Faces.Sides);
            k.Box(new Vector3(x0, lintel, d0), new Vector3(x1, top - plate, d1), Kit.Faces.Sides | Kit.Faces.NegY);
        }
        // Barred slits high along both sides, dark behind, outside and in.
        k.Shade(0.15f);
        for (float z = -5.5f; z <= 5.6f; z += 2.2f)
            foreach (int side in new[] { -1, 1 })
            {
                k.Panel(new Vector3(side * (w + 0.01f), 2.9f, z), new Vector3(side, 0, 0), Vector3.UnitY, 0.9f, 0.35f);
                k.Panel(new Vector3(side * (w - plate - 0.01f), 2.9f, z), new Vector3(-side, 0, 0), Vector3.UnitY, 0.9f, 0.35f);
            }
        // The cell: a plank bench down each side (the door's either side of it), and the slop bucket at the far end.
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side * (w - plate - 0.42f), x1 = side * (w - plate);
            (float, float)[] runs = side == door ? [(-half + 0.5f, d0 - 0.2f), (d1 + 0.2f, half - 0.5f)] : [(-half + 0.5f, half - 0.5f)];
            foreach (var (z0, z1) in runs)
            {
                k.Box(new Vector3(MathF.Min(x0, x1), floor + 0.52f, z0), new Vector3(MathF.Max(x0, x1), floor + 0.57f, z1));
                foreach (float z in new[] { z0 + 0.2f, z1 - 0.2f })
                    k.Box(new Vector3(MathF.Min(x0, x1) + 0.1f, floor + 0.1f, z - 0.04f), new Vector3(MathF.Max(x0, x1) - 0.1f, floor + 0.52f, z + 0.04f));
            }
        }
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        k.Cylinder(new Vector3(-door * 0.6f, floor + 0.1f, half - 0.6f), new Vector3(-door * 0.6f, floor + 0.42f, half - 0.6f), 0.16f, 10);
        k.Use("paint_oxide", Palette.BlueGrey * 0.7f, 0.95f, 0.2f, tile: 1.5f);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        for (float z = -5.5f; z <= 5.6f; z += 2.2f)
            foreach (int side in new[] { -1, 1 })
                for (float bz = -0.35f; bz <= 0.36f; bz += 0.175f)
                    k.Rod(new Vector3(side * (w + 0.03f), 2.72f, z + bz), new Vector3(side * (w + 0.03f), 3.08f, z + bz), 0.015f);
        // (The door on the crew's side, its leaf and padlock, are drawn by its state: Entrance.)
    }

    /// <summary>
    /// A halt's lockup (D.4): a parcel cage of iron bars on a plinth, a tin roof, a padlocked gate. No bars where its gate
    /// is, the sim's door in the middle of the face it's broken into at (<paramref name="facing"/>, the gate's
    /// <paramref name="gate"/> wide): Entrance hangs the gate there, and swung open you walk in (note 387).
    /// </summary>
    static void Lockup(Kit k, float length, float width, Vector3 facing, float gate)
    {
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        k.Box(new Vector3(-width / 2, -0.3f, -length / 2), new Vector3(width / 2, 0.25f, length / 2), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        const float h = 2.6f;
        bool Gate(Vector3 at) => Vector3.Dot(at, facing) > 0.01f && MathF.Abs(Vector3.Dot(at, new Vector3(facing.Z, 0, facing.X))) < gate / 2;
        for (float z = -length / 2; z <= length / 2 + 0.01f; z += 0.2f)
            foreach (float x in new[] { -width / 2, width / 2 })
                if (!Gate(new Vector3(x, 0, z)) || MathF.Abs(facing.X) < 0.5f)
                    k.Rod(new Vector3(x, 0.25f, z), new Vector3(x, h, z), 0.012f);
        for (float x = -width / 2; x <= width / 2 + 0.01f; x += 0.2f)
            foreach (float z in new[] { -length / 2, length / 2 })
                if (!Gate(new Vector3(x, 0, z)) || MathF.Abs(facing.Z) < 0.5f)
                    k.Rod(new Vector3(x, 0.25f, z), new Vector3(x, h, z), 0.012f);
        foreach (float y in new[] { 0.3f, 1.4f, h })
        {
            k.Rod(new Vector3(-width / 2, y, -length / 2), new Vector3(-width / 2, y, length / 2), 0.03f);
            k.Rod(new Vector3(width / 2, y, -length / 2), new Vector3(width / 2, y, length / 2), 0.03f);
            k.Rod(new Vector3(-width / 2, y, -length / 2), new Vector3(width / 2, y, -length / 2), 0.03f);
            k.Rod(new Vector3(-width / 2, y, length / 2), new Vector3(width / 2, y, length / 2), 0.03f);
        }
        k.Use("corrugated_iron", Palette.IronGrey, 0.9f, 0.3f, tile: 1.5f);
        k.Quad(new Vector3(-width / 2 - 0.3f, h + 0.1f, length / 2 + 0.3f), new Vector3(-width / 2 - 0.3f, h + 0.1f, -length / 2 - 0.3f),
            new Vector3(width / 2 + 0.3f, h + 0.5f, -length / 2 - 0.3f), new Vector3(width / 2 + 0.3f, h + 0.5f, length / 2 + 0.3f), twoSided: true);
    }

    /// <summary>Planks nailed across a doorway, from inside: whoever's in there did it.</summary>
    /// <summary>A ground-floor doorway's middle (its sill a hand over the ground), the standard door's height up (note 110).</summary>
    static Vector3 Doorway(Kit k) => Vector3.UnitY * (0.05f + k.DoorHeight() / 2);

    static void Barricade(Kit k, Vector3 centre, Vector3 outward, float halfWidth, float height)
    {
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, outward));
        k.Shade(1);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        for (int i = 0; i < 5; i++)
        {
            float y = -height / 2 + 0.2f + i * (height - 0.4f) / 4;
            float tilt = (i % 2 == 0 ? 1 : -1) * 0.12f;
            var a = centre + across * -halfWidth + Vector3.UnitY * (y - tilt) + outward * 0.08f;
            var c = centre + across * halfWidth + Vector3.UnitY * (y + tilt) + outward * 0.08f;
            k.Rod(a, c, 0.07f);
        }
    }

    /// <summary>A Holdout's lamp: a caged lantern on a short bracket (its glass lit by the scene while someone waits there).</summary>
    /// <param name="height">How high it hangs: an iron post carries it down to the ground.</param>
    static void LampFixture(Kit k, float height)
    {
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        k.Rod(new Vector3(0, -height, 0), new Vector3(0, 0.6f, 0), 0.05f);
        k.Rod(new Vector3(0, 0.6f, 0), new Vector3(0, 0.6f, 0.01f), 0.05f);
        k.Rod(new Vector3(0, 0.25f, 0), new Vector3(0, 0.6f, 0), 0.02f);
        k.BoxAt(new Vector3(0, 0.22f, 0), new Vector3(0.14f, 0.03f, 0.14f));
        k.BoxAt(new Vector3(0, -0.2f, 0), new Vector3(0.12f, 0.03f, 0.12f));
        k.Use("lamp_lens", Palette.SootBlack, 0.2f, 0.5f, tile: 0.25f);
        k.BoxAt(Vector3.Zero, new Vector3(0.1f, 0.18f, 0.1f));
    }
}
