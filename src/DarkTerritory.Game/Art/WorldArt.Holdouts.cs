using System.Numerics;
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
    void Holdout(Kit k, StopBuilding b, Vector3 facing)
    {
        float length = (float)b.Length, width = (float)b.Width;
        // The modelled pieces face +Z: turned so their front is the door's side.
        float yaw = MathF.Atan2(facing.X, facing.Z);
        switch (b.Kind)
        {
            case BuildingKind.PrisonCar:
                PrisonCar(k, length, facing.X >= 0 ? 1 : -1);
                break;
            case BuildingKind.SignalBox when _props.Get("signal_box") is { } box:
                k.Append(box, Matrix4x4.CreateRotationY(yaw) * Kit.At(0, -0.2f, 0));
                Barricade(k, facing * (width / 2 + 0.05f) + Doorway(k), facing, 1.1f, k.DoorHeight());
                break;
            case BuildingKind.WaterTower when _props.Get("water_tower") is { } tower:
                k.Append(tower, Matrix4x4.CreateRotationY(yaw) * Kit.At(0, -0.2f, 0));
                // Someone's boarded themselves into the pump house under the tank.
                k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                k.Box(new Vector3(-1.4f, -0.3f, -1.4f), new Vector3(1.4f, 2.4f, 1.4f), Kit.Faces.All & ~Kit.Faces.NegY);
                Barricade(k, facing * 1.45f + Doorway(k), facing, 1.0f, k.DoorHeight());
                break;
            case BuildingKind.Lockup:
                Lockup(k, length, width);
                break;
            default:
                // A lamp room (or a signal box or water tower without its model): a squat brick hut, a slate roof, the
                // door planked over from inside.
                k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                k.Box(new Vector3(-width / 2, -0.3f, -length / 2), new Vector3(width / 2, 2.8f, length / 2), Kit.Faces.All & ~Kit.Faces.NegY);
                k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
                k.Box(new Vector3(-width / 2 - 0.3f, 2.8f, -length / 2 - 0.3f), new Vector3(width / 2 + 0.3f, 3.1f, length / 2 + 0.3f), Kit.Faces.All);
                float half = MathF.Abs(facing.X) > 0 ? width / 2 : length / 2;
                Barricade(k, facing * (half + 0.05f) + Doorway(k), facing, 1.0f, k.DoorHeight());
                break;
        }
    }

    /// <summary>A derelict penal transport (D.4): an iron van on its bogies, barred slits, the door padlocked, on its siding.</summary>
    static void PrisonCar(Kit k, float siding, int door)
    {
        // The spare siding: sleepers and two rails, the points long gone.
        k.Use("wood_sleeper", Palette.DeepBrown, 0.8f, 0, tile: 1.3f);
        for (float z = -siding / 2 + 0.4f; z < siding / 2; z += 0.75f)
            k.Box(new Vector3(-1.3f, -0.05f, z - 0.12f), new Vector3(1.3f, 0.07f, z + 0.12f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        foreach (int side in new[] { -1, 1 })
            k.Box(new Vector3(side * TrainKit.HalfGauge - 0.035f, 0.07f, -siding / 2), new Vector3(side * TrainKit.HalfGauge + 0.035f, 0.19f, siding / 2));
        // The van, 14 m, on two bogies.
        const float half = 7, w = 1.5f, floor = 1.1f, top = 3.6f;
        k.Use("wheel_iron", Palette.IronGrey, 0.8f, 0.4f, tile: 0.5f);
        foreach (float z in new[] { -4.8f, 4.8f })
        {
            k.Box(new Vector3(-1.1f, 0.3f, z - 1.2f), new Vector3(1.1f, 0.9f, z + 1.2f));
            foreach (float dz in new[] { -0.8f, 0.8f })
                foreach (int side in new[] { -1, 1 })
                    k.Cylinder(new Vector3(side * TrainKit.HalfGauge - 0.06f, 0.5f, z + dz), new Vector3(side * TrainKit.HalfGauge + 0.06f, 0.5f, z + dz), 0.45f, 10);
        }
        k.Use("paint_oxide", Palette.BlueGrey * 0.7f, 0.95f, 0.2f, tile: 1.5f);
        k.Box(new Vector3(-w, floor, -half), new Vector3(w, top, half), Kit.Faces.All & ~Kit.Faces.NegY);
        // Barred slits high along both sides, dark behind.
        k.Shade(0.15f);
        for (float z = -5.5f; z <= 5.6f; z += 2.2f)
            foreach (int side in new[] { -1, 1 })
                k.Panel(new Vector3(side * (w + 0.01f), 2.9f, z), new Vector3(side, 0, 0), Vector3.UnitY, 0.9f, 0.35f);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        for (float z = -5.5f; z <= 5.6f; z += 2.2f)
            foreach (int side in new[] { -1, 1 })
                for (float bz = -0.35f; bz <= 0.36f; bz += 0.175f)
                    k.Rod(new Vector3(side * (w + 0.03f), 2.72f, z + bz), new Vector3(side * (w + 0.03f), 3.08f, z + bz), 0.015f);
        // The door on the crew's side: a heavy leaf with a hasp and padlock.
        k.Box(new Vector3(door * (w + 0.02f) - 0.04f, floor + 0.1f, -1.0f), new Vector3(door * (w + 0.02f) + 0.04f, floor + 0.1f + k.DoorHeight(), 1.0f));
        k.Use("brass", Palette.TarnishedBrass, 0.7f, 0.5f);
        k.BoxAt(new Vector3(door * (w + 0.1f), 2.0f, 0.9f), new Vector3(0.05f, 0.12f, 0.09f));
    }

    /// <summary>A halt's lockup (D.4): a parcel cage of iron bars on a plinth, a tin roof, a padlocked gate.</summary>
    static void Lockup(Kit k, float length, float width)
    {
        k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 2.5f);
        k.Box(new Vector3(-width / 2, -0.3f, -length / 2), new Vector3(width / 2, 0.25f, length / 2), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.4f);
        const float h = 2.6f;
        for (float z = -length / 2; z <= length / 2 + 0.01f; z += 0.2f)
            foreach (float x in new[] { -width / 2, width / 2 })
                k.Rod(new Vector3(x, 0.25f, z), new Vector3(x, h, z), 0.012f);
        for (float x = -width / 2; x <= width / 2 + 0.01f; x += 0.2f)
            foreach (float z in new[] { -length / 2, length / 2 })
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
