using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A town's people as they're drawn (queue #90, ARCHITECTURE §8 note 353): down on their feet when they crouch, on their
/// chair when they sit (the director's 8 Oct shots: "some of the animation positions are off"), and wearing what they
/// breathe through, never the crew's mask.
/// </summary>
public class TownsfolkArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    static float LowestFoot(string figure, string clip)
    {
        var model = Art.Get(figure)!;
        var joints = Art.Joints(figure, clip, 0, true).ToArray();
        return ((string[])["foot_l", "foot_r", "ball_l", "ball_r"]).Min(b => joints[model.Skeleton.IndexOf(b)].Y);
    }

    [Theory]
    [InlineData("survivor_prisoner")]
    [InlineData("survivor_wildlander")]
    public void ACrouchedTownspersonIsSetDownOnTheirFeet(string figure)
    {
        // crew_clips.py doesn't plant crouch_idle's feet: drawn as authored they're half a metre off the floor.
        float up = Art.FeetOver(figure, "crouch_idle");
        Assert.True(up > 0.3f, $"crouch_idle's feet {up:F2} m up: planted now? then FeetOver has nothing to do");
        Assert.InRange(LowestFoot(figure, "crouch_idle") - up, 0.0f, 0.06f);
        // Standing and walking with a lamp, nothing moves.
        Assert.InRange(Art.FeetOver(figure, "idle"), 0, 0.02f);
        Assert.InRange(Art.FeetOver(figure, "lantern"), 0, 0.03f);
    }

    [Theory]
    [InlineData(7.6, 7.0, 1)]
    [InlineData(6.4, 6.2, -1)]
    public void ASeatedResidentSitsOnTheirChair(double width, double depth, int kitchen)
    {
        var layout = HouseLayout.For(width, depth, kitchen);
        var model = Art.Get("survivor_prisoner")!;
        var pelvis = Art.Joints("survivor_prisoner", "gunner", 0, true).ToArray()[model.Skeleton.IndexOf("pelvis")];
        foreach (var spot in layout.Spots.Where(s => s.Pose == "seated"))
        {
            // A chair where they sit (the kitchen's at the table, the parlour's armchair) ...
            var chair = Assert.Single(layout.Things, t => t.Kind is "chair" or "armchair" && Math.Abs(t.U - spot.U) < 0.05 && Math.Abs(t.V - spot.V) < 0.05);
            // ... its back behind them (MaritimeKit puts it at +v: they face −v) ...
            Assert.Equal((0.0, -1.0), (spot.FaceU, spot.FaceV));
            // ... and the clip's seat over its pan: the pelvis within the seat (model −Z the way they face, so +Z is +v) and
            // its joint a hand over the pan's 0.48 m.
            Assert.InRange(Math.Abs(pelvis.X), 0, chair.HalfU - 0.05);
            Assert.InRange(pelvis.Z, -chair.HalfV + 0.05, chair.HalfV - 0.05);
            Assert.InRange(pelvis.Y - 0.48f, 0.03f, 0.14f);
        }
    }

    [Fact]
    public void EveryKindOfGearAndHatIsBuiltAndCheap()
    {
        var kit = new TownsfolkKit(Look);
        foreach (string gear in TownGear.Kinds)
            foreach (bool down in (bool[])[false, true])
            {
                var face = kit.Face(gear, down);
                Assert.InRange(face.Triangles, 40, 1200);
                // Never the crew's mask: worn round the face, in front of it, not a helmet over the head.
                var (min, max) = ArtCatalog.Bounds(face);
                Assert.True(max.Y < 1.86f && min.Z < -0.15f, $"{gear} {(down ? "down" : "on")}: {min} {max}");
                Assert.Equal(gear is "oxygen" or "rebreather", kit.Body(gear) is not null);
                Assert.Equal(gear switch { "oxygen" => 1, "rebreather" => 2, _ => 0 }, TownsfolkKit.Hoses(gear, down).Count);
            }
        Assert.Null(kit.Hat(0));
        for (int hat = 1; hat < TownsfolkKit.Hats; hat++)
            Assert.InRange(kit.Hat(hat)!.Triangles, 40, 800);
    }
}
