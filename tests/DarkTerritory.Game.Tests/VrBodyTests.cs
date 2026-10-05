using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A headset crewmate's body (T82, <see cref="VrBody"/>; ARCHITECTURE §8 note 209): the spine leans and crouches under the
/// head and twists to it, the hips follow the head's yaw past a deadzone, and the feet plant and step. Pure functions:
/// the same inputs give the same body.
/// </summary>
public class VrBodyTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly VrBodyTuning T = DataFile.Load<VrTuning>(Path.Combine(Content, VrTuning.File)).Body;
    const double Dt = 1 / 90.0;
    static double Deg(double d) => d * Math.PI / 180;

    [Fact]
    public void EveryNumberIsInTheFile()
    {
        // vr.json's body section names every one of the record's numbers (none left at a code default by a misspelt key).
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, VrTuning.File)),
            new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var keys = doc.RootElement.GetProperty("body").EnumerateObject().Select(p => p.Name).ToHashSet();
        var wanted = typeof(VrBodyTuning).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToHashSet();
        Assert.Equal(wanted.Order(), keys.Order());
        Assert.True(T.StepSeconds > 0 && T.StepDistance > 0 && T.YawDeadzoneDegrees > 0 && T.LeanDrop > 0);
    }

    [Fact]
    public void ALowerHeadLeansFirstThenCrouches()
    {
        Assert.Equal((0, 0), VrBody.Spine(0, Eyes.Height, T));
        Assert.Equal((0, 0), VrBody.Spine(Eyes.Height + 0.1, Eyes.Height, T));
        // Half the lean's drop: half the lean, no crouch.
        var (lean, crouch) = VrBody.Spine(Eyes.Height - T.LeanDrop / 2, Eyes.Height, T);
        Assert.Equal(Deg(T.LeanDegrees) / 2, lean, 9);
        Assert.Equal(0, crouch);
        // Lower: the full lean, and the knees take the rest, up to the most a crouch goes.
        (lean, crouch) = VrBody.Spine(Eyes.Height - T.LeanDrop - 0.2, Eyes.Height, T);
        Assert.Equal(Deg(T.LeanDegrees), lean, 9);
        Assert.Equal(0.2, crouch, 9);
        Assert.Equal(T.CrouchMost, VrBody.Spine(0.5, Eyes.Height, T).Crouch, 9);
    }

    [Fact]
    public void TheHipsWaitInsideTheDeadzoneThenFollow()
    {
        double dead = Deg(T.YawDeadzoneDegrees);
        Assert.Equal(0, VrBody.Follow(0, dead * 0.9, T));
        Assert.Equal(0, VrBody.Follow(0, -dead * 0.9, T));
        Assert.Equal(Deg(20), VrBody.Follow(0, dead + Deg(20), T), 9);
        Assert.Equal(-Deg(20), VrBody.Follow(0, -dead - Deg(20), T), 9);
        // Across the half-turn: the short way round (170° to −170° is 20°, inside; to −120°, 70° on round).
        Assert.Equal(Deg(170), VrBody.Follow(Deg(170), -Deg(170), T), 9);
        Assert.Equal(-Deg(120) - dead, VrBody.Follow(Deg(170), -Deg(120), T), 9);
        // A look round inside the deadzone: no step, the feet stay where they are.
        var hips = new Double3(1, 2, 3);
        var s = VrBody.Stand(hips, 0, T);
        for (int i = 0; i < 90; i++)
            s = VrBody.Step(s, hips, dead * 0.95 * Math.Sin(i * 0.2), Dt, T);
        Assert.Equal(VrBody.Stand(hips, 0, T), s);
    }

    [Fact]
    public void TurningPastTheDeadzoneSteps()
    {
        var hips = new Double3(0, 0, 0);
        var s = VrBody.Stand(hips, 0, T);
        // The head round 90°: the hips come round to the deadzone's edge, past the feet's turn threshold, and they step.
        int steps = 0, was = 0;
        for (int i = 0; i < 300; i++)
        {
            s = VrBody.Step(s, hips, Deg(90), Dt, T);
            if (s.Stepping != 0 && was == 0)
                steps++;
            was = s.Stepping;
        }
        Assert.Equal(Deg(90 - T.YawDeadzoneDegrees), s.HipsYaw, 9);
        Assert.Equal(0, s.Stepping);
        Assert.InRange(steps, 2, 4);
        // Both feet end up turned with the hips.
        Assert.Equal(s.HipsYaw, s.Left.Yaw, 9);
        Assert.Equal(s.HipsYaw, s.Right.Yaw, 9);
    }

    [Fact]
    public void FeetPlantThenStepInAnArcAndAlternate()
    {
        // The hips creep sideways (a stick nudged, a car's floor shuffled across) at 0.4 m/s for two seconds.
        var s = VrBody.Stand(default, 0, T);
        var sides = new List<int>();
        double highest = 0;
        Foot plantedLeft = s.Left, plantedRight = s.Right;
        for (int i = 0; i < 180; i++)
        {
            var hips = new Double3(Math.Min(0.8, 0.4 * i * Dt), 0, 0);
            var before = s;
            s = VrBody.Step(s, hips, 0, Dt, T);
            if (s.Stepping != 0 && before.Stepping == 0)
                sides.Add(s.Stepping);
            // A planted foot doesn't move; only the one stepping does, and it's off the floor while it does.
            if (s.Stepping != -1 && before.Stepping != -1)
                Assert.Equal(plantedLeft.At, VrBody.FootNow(s, -1, hips, T).At with { Y = plantedLeft.At.Y });
            if (s.Stepping != 1 && before.Stepping != 1)
                Assert.Equal(plantedRight.At, VrBody.FootNow(s, 1, hips, T).At with { Y = plantedRight.At.Y });
            if (s.Stepping != 0)
                highest = Math.Max(highest, VrBody.FootNow(s, s.Stepping, hips, T).At.Y);
            (plantedLeft, plantedRight) = (s.Left, s.Right);
        }
        Assert.True(sides.Count >= 4, $"{sides.Count} steps");
        // Left, right, left...: the way it's going, then the other.
        for (int i = 1; i < sides.Count; i++)
            Assert.NotEqual(sides[i - 1], sides[i]);
        Assert.InRange(highest, T.StepHeight * 0.95, T.StepHeight);
        // Stopped, they settle square under the hips, either side.
        for (int i = 0; i < 120; i++)
            s = VrBody.Step(s, new Double3(0.8, 0, 0), 0, Dt, T);
        Assert.Equal(0, s.Stepping);
        Assert.InRange(s.Left.At.X, 0.8 - T.StanceHalfWidth - T.StepDistance, 0.8 - T.StanceHalfWidth + T.StepDistance);
        Assert.InRange(s.Right.At.X, 0.8 + T.StanceHalfWidth - T.StepDistance, 0.8 + T.StanceHalfWidth + T.StepDistance);
    }

    [Fact]
    public void TheSameHeadGivesTheSameBody()
    {
        VrBodyPose Run()
        {
            var s = VrBody.Stand(new Double3(3, 4, -20), 1, T);
            VrBodyPose pose = default;
            for (int i = 0; i < 400; i++)
            {
                var hips = new Double3(3 + 0.3 * Math.Sin(i * 0.02), 4, -20 + 0.2 * i * Dt);
                double head = 1 + 1.5 * Math.Sin(i * 0.013);
                s = VrBody.Step(s, hips, head, Dt, T);
                pose = VrBody.Pose(s, hips, head, 1.65 - 0.5 * Math.Abs(Math.Sin(i * 0.01)), -0.3, Eyes.Height, T);
            }
            return pose;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void InModelSpaceTheFeetAreUnderTheHipsAndTheHipsLagTheHead()
    {
        // Facing the head's way (the model's −Z), a stood stride's feet are either side of the feet's origin.
        var s = VrBody.Stand(new Double3(5, 1, 5), 0.7, T);
        var pose = VrBody.Pose(s, new Double3(5, 1, 5), 0.7, Eyes.Height, 0, Eyes.Height, T);
        Assert.Equal(-T.StanceHalfWidth, pose.LeftFoot.X, 9);
        Assert.Equal(T.StanceHalfWidth, pose.RightFoot.X, 9);
        Assert.Equal(0, pose.LeftFoot.Z, 9);
        Assert.Equal(0, pose.Hips, 9);
        // The head turned inside the deadzone: the hips stay, so from the head's facing they're turned back, and the torso
        // takes its share of the twist back towards the head.
        var turned = VrBody.Pose(s, new Double3(5, 1, 5), 0.7 + Deg(20), Eyes.Height, 0, Eyes.Height, T);
        Assert.Equal(-Deg(20), turned.Hips, 9);
        Assert.Equal(Deg(20) * T.TwistShare, turned.Twist, 9);
    }

    [Fact]
    public void TheCrewModelCrouchesAndLiftsAFoot()
    {
        // On the crew model: a crouched head brings the figure's top down; a foot mid-step is off the roof.
        var look = Look.Load(Content);
        var art = new CreatureArt(look, Content);
        var stood = new MeshBuilder();
        Assert.True(art.Crewmate(stood, Matrix4x4.Identity, CrewPose.Idle, 0.4, 1));
        float top = stood.Flattened().Max(v => v.Position.Y);
        var still = VrBody.Stand(default, 0, T);
        var upright = new MeshBuilder();
        Assert.True(art.Crewmate(upright, Matrix4x4.Identity, CrewPose.Idle, 0.4, 1, body: VrBody.Pose(still, default, 0, Eyes.Height, 0, Eyes.Height, T)));
        Assert.InRange(upright.Flattened().Max(v => v.Position.Y), top - 0.05f, top + 0.05f);
        var crouched = new MeshBuilder();
        Assert.True(art.Crewmate(crouched, Matrix4x4.Identity, CrewPose.Idle, 0.4, 1, body: VrBody.Pose(still, default, 0, 1.0, -0.3, Eyes.Height, T)));
        Assert.True(crouched.Flattened().Max(v => v.Position.Y) < top - 0.3f);
        // The feet stay on the floor in the crouch: the knees bent, not the figure sunk.
        Assert.InRange(crouched.Flattened().Min(v => v.Position.Y), stood.Flattened().Min(v => v.Position.Y) - 0.03f, 0.05f);

        var s = VrBody.Stand(default, 0, T);
        while (!(s.Stepping != 0 && s.Progress >= 0.5))
            s = VrBody.Step(s, default, Deg(100), Dt, T);
        var stepping = new MeshBuilder();
        var pose = VrBody.Pose(s, default, Deg(100), Eyes.Height, 0, Eyes.Height, T);
        Assert.True(art.Crewmate(stepping, Matrix4x4.Identity, CrewPose.Idle, 0.4, 1, body: pose));
        var lifted = s.Stepping < 0 ? pose.LeftFoot : pose.RightFoot;
        var under = new Vector3((float)lifted.X, 0, (float)lifted.Z);
        float lowestThere = stepping.Flattened().Where(v => new Vector2(v.Position.X - under.X, v.Position.Z - under.Z).Length() < 0.12f).Min(v => v.Position.Y);
        Assert.True(lowestThere > T.StepHeight * 0.5, $"the stepping foot's sole is {lowestThere} m up");
    }
}
