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
    /// While it's being breached (note 464), the breach shows where it's worked: <see cref="Lock"/>, <see cref="Barricade"/>.
    /// </summary>
    /// <param name="breach">How far the breach under way is (0 to 1 of its seconds); −1 when nobody's at it.</param>
    /// <param name="quiet">The breach under way is the repair kit's picks (a lock opened, not smashed).</param>
    /// <param name="time">The scene's clock, the crew's clips' own: the blows and the heaves land on their beats.</param>
    void Entrance(Kit k, StopBuilding b, Vector3 facing, bool open, bool picked, float breach = -1, bool quiet = false, float time = 0)
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
                        Lock(k, new Vector3(door * (w + 0.1f), 2.0f, 0.9f), new Vector3(door, 0, 0), Vector3.UnitZ, new Vector3(0.09f, 0.12f, 0.05f),
                            breach, quiet, time, b.Variant);
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
                            Lock(k, new Vector3(halfSpan - 0.05f, 1.3f, at + 0.06f), Vector3.UnitZ, Vector3.UnitX, new Vector3(0.05f, 0.08f, 0.04f),
                                breach, quiet, time, b.Variant);
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
                        Barricade(k, centre + Doorway(k) + Vector3.UnitY * 0.25f, facing, half, k.DoorHeight(), breach, time, b.Variant);
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
    public void Entrances(MeshBuilder mesh, RailLine line, Route? route, Sim.Run.Holdouts holdouts, Double3 eye, float valleyDepth, double time = 0)
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
            // The breach under way (note 464), from the sim's replicated progress, so a client's is the host's.
            float breach = h.State == Sim.Run.HoldoutState.Breaching
                ? (float)Math.Clamp(h.Progress / Math.Max(1e-6, h.Breach(holdouts.Tuning).Seconds), 0, 1) : -1;
            k.Reseed(b.Variant * 7.1f + (float)(b.S * 0.13));
            k.With(frame, () => Entrance(k, b, facing, open, open && h.Quiet, breach, h.Quiet, (float)(time % 3600)));
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

    /// <summary>
    /// A shelter's barricade: five boards nailed across its doorway. Being pried (<paramref name="breach"/> 0 to 1, note 464),
    /// they come away one at a time in the order a bar gets at them (the one at the chest first, crew_clips' pry, then the ones
    /// above and below it): each pried board lies on the ground before the doorway, and the one being worked stands out from
    /// the jamb at its free end, further as its share of the breach goes on, sprung out on each heave of the clip and
    /// splintering at its nails as the heave comes on.
    /// </summary>
    static void Barricade(Kit k, Vector3 centre, Vector3 outward, float halfWidth, float height, float breach = -1, float time = 0, int seed = 0)
    {
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, outward));
        k.Shade(1);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        // Which board comes off when: the chest's, then above, below, the top, the bottom (5 boards, a fifth of the breach each).
        int[] order = [2, 3, 1, 4, 0];
        int worked = breach < 0 ? -1 : Math.Min(4, (int)(breach * 5));
        float share = breach < 0 ? 0 : breach * 5 - worked;
        float heave = breach < 0 ? 0 : Heave(time);
        for (int i = 0; i < 5; i++)
        {
            float y = -height / 2 + 0.2f + i * (height - 0.4f) / 4;
            float tilt = (i % 2 == 0 ? 1 : -1) * 0.12f;
            var a = centre + across * -halfWidth + Vector3.UnitY * (y - tilt) + outward * 0.08f;
            var c = centre + across * halfWidth + Vector3.UnitY * (y + tilt) + outward * 0.08f;
            int rank = Array.IndexOf(order, i);
            if (rank < worked)
            {
                // Off: down on the ground before the doorway, where PriedOff lays them (its sill is the ground's).
                var g = centre - Vector3.UnitY * (height / 2 + 0.25f + 0.05f - 0.07f) + outward * (0.5f + rank * 0.33f) + across * (-halfWidth * 0.9f + rank * 0.15f);
                k.Rod(g, g + across * (2 * halfWidth * 0.85f) + outward * ((rank % 3 - 1) * 0.3f), 0.07f);
                continue;
            }
            if (rank == worked)
            {
                // Worked: its free end (the right jamb's) pulled off the nails, sprung out further on each heave.
                float pull = 0.04f + 0.16f * share + 0.07f * heave;
                c += outward * pull + Vector3.UnitY * (0.03f * heave);
                // Its nails drawn with it: two bright points of iron at the end.
                k.Rod(c - across * 0.04f, c - across * 0.04f - outward * (pull * 0.8f), 0.008f);
            }
            k.Rod(a, c, 0.07f);
            if (rank == worked)
                Splinters(k, c - across * 0.06f, outward, across, PrySince(time), seed * 31 + (int)(time / PryCycle));
        }
    }

    // The breach clips' beats (tools/blender/crew_clips.py, 30 fps), so the lock and the boards answer the crew's blows: the
    // smash a 24-frame loop with the bar on the hasp at frame 9; the pry a 40-frame loop hauled back from 0 to 14, held to 22,
    // eased off by 32.
    const float SmashCycle = 24 / 30f, SmashBlow = 9 / 30f, PryCycle = 40 / 30f, PryOn = 14 / 30f, PryHeld = 22 / 30f, PryOff = 32 / 30f;

    /// <summary>How long since the last blow of the smash landed (s).</summary>
    static float SmashSince(float time) => ((time - SmashBlow) % SmashCycle + SmashCycle) % SmashCycle;

    /// <summary>How long since the pry's heave came on (s).</summary>
    static float PrySince(float time) => ((time - PryOn) % PryCycle + PryCycle) % PryCycle;

    /// <summary>How hard the pry's heaving at <paramref name="time"/>: 0 at rest, 1 hauled right back.</summary>
    static float Heave(float time)
    {
        float t = (time % PryCycle + PryCycle) % PryCycle;
        return t < PryOn ? t / PryOn : t < PryHeld ? 1 : t < PryOff ? 1 - (t - PryHeld) / (PryOff - PryHeld) : 0;
    }

    /// <summary>
    /// A lock on its hasp (a prison car's padlock, a lockup gate's), <paramref name="outward"/> toward whoever's at it. Struck
    /// (note 464): it jumps out on the hasp at each blow of the smash and swings back, throwing sparks, and hangs lower and
    /// more twisted the further the breach is; under the repair kit's picks it turns a little this way and that, a pick in it.
    /// </summary>
    /// <param name="half">Its half size along the door (<paramref name="along"/>), up, and out from it.</param>
    static void Lock(Kit k, Vector3 at, Vector3 outward, Vector3 along, Vector3 half, float breach, bool quiet, float time, int seed)
    {
        if (breach < 0)
        {
            k.BoxAt(at, Vector3.Abs(along * half.X) + new Vector3(0, half.Y, 0) + Vector3.Abs(outward * half.Z));
            return;
        }
        var up = Vector3.UnitY;
        // The hasp's staple it hangs from, just over it, bent out by the blows.
        var staple = at + up * (half.Y + 0.03f);
        float since = SmashSince(time), jolt = quiet || since > 0.3f ? 0 : MathF.Exp(-since / 0.06f);
        float swing = quiet ? 0.12f * MathF.Sin(time * 9) : 0.5f * breach + 0.6f * jolt;
        float twist = quiet ? 0.25f * MathF.Sin(time * 5.3f) : 0.3f * breach;
        var turn = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(along), -swing) * Matrix4x4.CreateFromAxisAngle(up, twist);
        var hang = Vector3.Transform(at - staple, turn);
        var centre = staple + hang - up * (0.04f * breach) + outward * (0.02f * jolt);
        var down = Vector3.Normalize(Vector3.Transform(-up, turn));
        var face = Vector3.Normalize(Vector3.Transform(outward, turn));
        var side = Vector3.Cross(-down, face);
        k.Rod(staple - outward * 0.02f, staple + outward * (0.03f + 0.03f * breach + 0.03f * jolt), 0.012f);
        k.Rod(staple, centre - down * half.Y, 0.008f);
        k.With(new Matrix4x4(side.X, side.Y, side.Z, 0, -down.X, -down.Y, -down.Z, 0, face.X, face.Y, face.Z, 0, centre.X, centre.Y, centre.Z, 1),
            () => k.BoxAt(Vector3.Zero, new Vector3(half.X, half.Y, half.Z)));
        if (quiet)
        {
            // The kit's pick and tension wrench in its keyhole, at its foot.
            k.Use("iron_plate", Palette.IronGrey, 0.4f, 0.6f);
            var hole = centre + down * (half.Y * 0.7f) + face * half.Z;
            k.Rod(hole, hole + face * 0.07f + down * 0.02f, 0.004f);
            k.Rod(hole + side * 0.01f, hole + side * 0.01f + face * 0.05f - down * 0.03f, 0.004f);
            return;
        }
        if (since < 0.35f)
            Sparks(k, centre + face * half.Z, face, side, since, seed * 17 + (int)((time - SmashBlow) / SmashCycle));
    }

    /// <summary>A blow's sparks off struck iron: a dozen streaks out of it, splayed, falling, gone in a third of a second.</summary>
    static void Sparks(Kit k, Vector3 at, Vector3 outward, Vector3 side, float since, int seed)
    {
        k.Use("lamp_lens", new Vector3(1.0f, 0.62f, 0.2f), 0, 0, tile: 0.25f);
        k.Tint = new Vector3(1.6f, 0.95f, 0.35f);
        k.Emissive = 1;
        if (since < 0.06f)
            k.BoxAt(at, new Vector3(0.05f * (1 - since / 0.06f) + 0.01f));
        for (int i = 0; i < 12; i++)
        {
            float h = Hash01(seed, i, 1), h2 = Hash01(seed, i, 2), h3 = Hash01(seed, i, 3);
            var dir = Vector3.Normalize(outward * 1.1f + side * ((h - 0.5f) * 1.8f) + Vector3.UnitY * ((h2 - 0.25f) * 1.4f));
            float speed = 2.6f * (0.6f + 0.7f * h3);
            float life = 0.2f + 0.15f * h;
            if (since > life)
                continue;
            var v = dir * speed - Vector3.UnitY * (9.8f * since);
            var p = at + dir * (speed * since) - Vector3.UnitY * (4.9f * since * since);
            k.Rod(p, p - v * 0.035f, 0.011f * (1 - since / life) + 0.004f);
        }
        k.Emissive = 0;
    }

    /// <summary>Splinters off a board pried from its nails as the heave comes on: slivers of it out and down, gone in half a second.</summary>
    static void Splinters(Kit k, Vector3 at, Vector3 outward, Vector3 across, float since, int seed)
    {
        if (since > 0.5f)
            return;
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        k.Shade(1.25f);
        for (int i = 0; i < 7; i++)
        {
            float h = Hash01(seed, i, 4), h2 = Hash01(seed, i, 5), h3 = Hash01(seed, i, 6);
            var dir = Vector3.Normalize(outward * 1.0f + across * ((h - 0.5f) * 1.2f) + Vector3.UnitY * ((h2 - 0.2f) * 1.1f));
            float speed = 1.6f * (0.6f + 0.7f * h3);
            var p = at + dir * (speed * since) - Vector3.UnitY * (4.9f * since * since);
            var spin = Vector3.Normalize(across * MathF.Cos(since * 20 + h * 6) + Vector3.UnitY * MathF.Sin(since * 20 + h * 6));
            k.Rod(p, p + spin * (0.05f + 0.06f * h2), 0.008f);
        }
    }

    /// <summary>A repeatable 0..1 from three integers (the effects' own scatter, so a still frame is the same each time).</summary>
    static float Hash01(int seed, int i, int salt)
    {
        uint x = (uint)(seed * 73856093) ^ (uint)(i * 19349663) ^ (uint)(salt * 83492791);
        x ^= x >> 13;
        x *= 0x5bd1e995;
        x ^= x >> 15;
        return (x & 0xffffff) / (float)0x1000000;
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
