using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 779 (the art checklist's <c>failing-stages</c>): each of note 576's stages drawn its own way. Failing, boards sprung
/// and a side door askew; breaking, the frame sagging under the floor and a plate peeled back off the load; come apart, the
/// car off its rails, over, slewed and down at one end; the engine broken down, slumped behind its cab. All of it presentation:
/// the sim's frames and what's walked on stay put.
/// </summary>
public class FailingStagesTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Cargo = CarShape.Build(Tuning.Geometry, VehicleKind.Cargo, hasCarBehind: true);

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 1200);

    [Fact]
    public void FailingAndBreakingEachHaveTheirOwnPiecesTheCarsOwnEveryTime()
    {
        var failing = DamageKit.Failing(Look, Cargo, TrainKit.Livery.Planked, FailStage.Failing, 3).Vertices;
        var breaking = DamageKit.Failing(Look, Cargo, TrainKit.Livery.Planked, FailStage.Breaking, 3).Vertices;
        Assert.Equal(failing.Select(v => v.Position), DamageKit.Failing(Look, Cargo, TrainKit.Livery.Planked, FailStage.Failing, 3).Vertices.Select(v => v.Position));
        Assert.NotEqual(failing.Select(v => v.Position), DamageKit.Failing(Look, Cargo, TrainKit.Livery.Planked, FailStage.Failing, 4).Vertices.Select(v => v.Position));
        // Breaking is failing's boards sprung further, and more besides.
        Assert.True(breaking.Length > failing.Length * 3 / 2, $"breaking {breaking.Length} vs failing {failing.Length}");
        Assert.InRange(breaking.Length / 3, 50, ArtCatalog.MediumProp.MaxTriangles);

        // On the car, stood out from it where it's sprung, not off in the air: its sides, its roof's edge, under its floor.
        float w = (float)Cargo.HalfWidth, l = (float)Cargo.HalfLength, h = (float)Cargo.RoofHeight;
        for (int seed = 0; seed < 12; seed++)
            foreach (var v in DamageKit.Failing(Look, Cargo, TrainKit.Livery.Steel, FailStage.Breaking, seed).Vertices)
            {
                Assert.InRange(MathF.Abs(v.Position.X), 0, w + 1.15f);
                Assert.InRange(v.Position.Y, 0.2f, h + 0.7f);
                Assert.InRange(MathF.Abs(v.Position.Z), 0, l + 0.2f);
            }
        // Something sprung stands well clear of the car's side: it breaks the car's line from along the train.
        Assert.Contains(failing, v => MathF.Abs(v.Position.X) > w + 0.25f);
        // And the roof's lifted against the sky.
        Assert.Contains(failing, v => v.Position.Y > h + 0.15f);
    }

    [Fact]
    public void TheEnginesCasingSpringsOnItsSidesAndNothingOnItsTop()
    {
        var shape = CarShape.Build(Tuning.Geometry, VehicleKind.Engine, hasCarBehind: true);
        var failing = DamageKit.Failing(Look, shape, TrainKit.Livery.Steel, FailStage.Failing, 0).Vertices;
        var breaking = DamageKit.Failing(Look, shape, TrainKit.Livery.Steel, FailStage.Breaking, 0).Vertices;
        Assert.NotEmpty(failing);
        Assert.True(breaking.Length > failing.Length);
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength, h = (float)shape.RoofHeight;
        for (int seed = 0; seed < 8; seed++)
            foreach (var v in DamageKit.Failing(Look, shape, TrainKit.Livery.Steel, FailStage.Breaking, seed).Vertices)
            {
                // Off its sides, never over its walked top or down among its drivers.
                Assert.InRange(MathF.Abs(v.Position.X), w - 0.4f, w + 1.1f);
                Assert.InRange(v.Position.Y, 1.2f, h);
                Assert.InRange(MathF.Abs(v.Position.Z), 0, l);
            }
    }

    [Fact]
    public void TheFrameSagsUnderTheFloorBetweenTheBogiesAndNothingWalkedOnMoves()
    {
        Assert.Equal(0, DamageKit.SagOf(FailStage.Holding));
        Assert.Equal(0, DamageKit.SagOf(FailStage.Failing));
        Assert.True(DamageKit.SagOf(FailStage.Breaking) > 0 && DamageKit.SagOf(FailStage.Broken) > DamageKit.SagOf(FailStage.Breaking));

        float floor = (float)Cargo.Interior!.Value.Min.Y, bogie = (float)Cargo.HalfLength * 0.6f, sag = DamageKit.BreakingSag;
        Assert.Equal(sag, DamageKit.Sag(new Vector3(0, floor - 1, 0), floor, bogie, sag), 5);
        Assert.Equal(0, DamageKit.Sag(new Vector3(0, floor + 0.01f, 0), floor, bogie, sag));
        Assert.Equal(0, DamageKit.Sag(new Vector3(0, floor - 1, bogie), floor, bogie, sag));
        Assert.Equal(0, DamageKit.Sag(new Vector3(0, floor - 1, -bogie - 1), floor, bogie, sag));

        var body = TrainKit.Car(Look, Cargo, TrainKit.Livery.Planked, 0, load: false, wheel: false);
        var sagged = DamageKit.Sagged(body, Cargo, sag);
        Assert.Equal(body.Vertices.Length, sagged.Vertices.Length);
        bool dropped = false;
        for (int i = 0; i < body.Vertices.Length; i++)
        {
            Vector3 a = body.Vertices[i].Position, b = sagged.Vertices[i].Position;
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Z, b.Z);
            if (a.Y >= floor)
                Assert.Equal(a.Y, b.Y);
            dropped |= a.Y - b.Y > sag * 0.9f;
        }
        Assert.True(dropped, "nothing under the floor sagged");
    }

    [Fact]
    public void ASideDoorHangsAskewFurtherTheFurtherGone()
    {
        int door = DamageKit.AskewDoor(Cargo, 5);
        Assert.Contains(door, Cargo.DoorList.Select(d => d.Index));
        Assert.Equal(door, DamageKit.AskewDoor(Cargo, 5));
        var box = Cargo.DoorList.First(d => d.Index == door).Box;
        Assert.True(box.Max.Z - box.Min.Z > box.Max.X - box.Min.X, "a side door, not an end door");

        var size = new Vector3((float)(box.Max.X - box.Min.X), (float)(box.Max.Y - box.Min.Y), (float)(box.Max.Z - box.Min.Z));
        var placed = Matrix4x4.CreateTranslation((float)box.Centre.X, (float)box.Centre.Y, (float)box.Centre.Z);
        int side = box.Min.X < 0 ? -1 : 1;
        double last = 0;
        foreach (var stage in new[] { FailStage.Failing, FailStage.Breaking, FailStage.Broken })
        {
            var m = DamageKit.Askew(placed, size, side, stage, 5);
            // Turned in the wall's plane (its top edge no longer level), and out of the opening, never into the car.
            var top0 = Vector3.Transform(new Vector3(0, size.Y / 2, -size.Z / 2), m);
            var top1 = Vector3.Transform(new Vector3(0, size.Y / 2, size.Z / 2), m);
            double tilt = Math.Abs(top1.Y - top0.Y) / size.Z;
            Assert.True(tilt > last + 0.02, $"{stage}: {tilt:0.000} after {last:0.000}");
            last = tilt;
            Assert.True(Vector3.Transform(Vector3.Zero, m).X * side > (float)box.Centre.X * side);
            // Hung off its lower corner, which hasn't left the runner it dropped onto.
            int end = (5 & 1) == 0 ? 1 : -1;
            var corner = new Vector3(0, -size.Y / 2, end * size.Z / 2);
            Assert.Equal(Vector3.Transform(corner, placed).Y, Vector3.Transform(corner, m).Y, 3);
        }
    }

    [Fact]
    public void TheSceneDrawsEachStageByIntegrity()
    {
        static string[] Drawn(double integrity, bool rule = true)
        {
            var train = Train();
            train.Vehicles[2].Integrity = integrity;
            var mesh = new MeshBuilder();
            new GreyboxScene { Look = Look, Time = 0.37, FailTuning = rule ? Tuning.Failing : null }.Build(mesh, train, train.Frames[2].Origin);
            return [.. mesh.Instances.Select(i => i.Asset.Name)];
        }
        Assert.DoesNotContain(Drawn(0.9), n => n.StartsWith("failing-") || n.EndsWith("-sagged"));
        var failing = Drawn((Tuning.Failing.Below + Tuning.Failing.BreakingBelow) / 2);
        Assert.Contains($"failing-{(int)FailStage.Failing}", failing);
        Assert.DoesNotContain(failing, n => n.EndsWith("-sagged"));
        var breaking = Drawn(Tuning.Failing.BreakingBelow / 2);
        Assert.Contains(breaking, n => n == $"failing-{(int)FailStage.Breaking}-sagged");
        Assert.Contains(breaking, n => n.EndsWith("-sagged") && !n.StartsWith("failing-"));
        // Without the rule (failing off), a battered car's only its scars.
        Assert.DoesNotContain(Drawn(0.1, rule: false), n => n.StartsWith("failing-") || n.EndsWith("-sagged"));
    }

    [Fact]
    public void ACarComeApartLiesWhereItStoppedAndTheRestStandOnTheirRails()
    {
        var train = Train();
        (train.Vehicles[2].Integrity, train.Vehicles[2].OffRails) = (0, true);
        var frames = new List<CarFrame>(train.Frames);
        ComeApart.Apply(frames, train, null);
        for (int i = 0; i < frames.Count; i++)
            if (i != 2)
                Assert.Equal(train.Frames[i], frames[i]);
        var was = train.Frames[2];
        var now = frames[2];
        // Over, slewed round and down at one end, and dropped onto the sleepers.
        Assert.True(Angle(was.Up, now.Up) > 8, $"over by {Angle(was.Up, now.Up):0.0}°");
        Assert.True(Angle(Flat(was.Back), Flat(now.Back)) > 4, $"slewed by {Angle(Flat(was.Back), Flat(now.Back)):0.0}°");
        Assert.True(Math.Abs(now.Back.Y - was.Back.Y) > 0.01, "level end to end");
        Assert.True(now.Origin.Y < was.Origin.Y - 0.1, $"down {was.Origin.Y - now.Origin.Y:0.000} m");
        // The same every time, on every machine.
        var again = new List<CarFrame>(train.Frames);
        ComeApart.Apply(again, train, null);
        Assert.Equal(frames[2], again[2]);
    }

    [Fact]
    public void TheEngineBrokenDownSlumpsBehindItsCabAndTheCabStaysPut()
    {
        var train = Train();
        var frames = new List<CarFrame>(train.Frames);
        ComeApart.Apply(frames, train, null);
        Assert.Equal(train.Frames[0], frames[0]);

        train.Vehicles[0].Integrity = 0;
        Assert.Equal(FailStage.Broken, Failing.Stage(train, 0));
        frames = new List<CarFrame>(train.Frames);
        ComeApart.Apply(frames, train, null);
        CarFrame was = train.Frames[0], now = frames[0];
        var cab = was.Shape.Cab!.Value;
        // Where the driver stands, at the cab's front windows, is where it was, near enough to walk in...
        var driver = new Double3(0, cab.Min.Y, cab.Min.Z + 0.6);
        Assert.True((now.ToWorld(driver) - was.ToWorld(driver)).Length < 0.06, $"the driver's floor moved {(now.ToWorld(driver) - was.ToWorld(driver)).Length:0.000} m");
        // ...and its back end's down on its broken springs.
        var rear = new Double3(0, 0, was.Shape.HalfLength);
        Assert.True(was.ToWorld(rear).Y - now.ToWorld(rear).Y > 0.15, $"its back end's down {was.ToWorld(rear).Y - now.ToWorld(rear).Y:0.000} m");
        Assert.True(Angle(was.Up, now.Up) > 1, "it lists");
    }

    static Double3 Flat(Double3 v) => new Double3(v.X, 0, v.Z).Normalized;

    static double Angle(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized, b.Normalized), -1, 1)) * 180 / Math.PI;
}
