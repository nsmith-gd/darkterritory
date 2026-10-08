using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A cut coupler's knuckle swings open on its pin (note 402; the art checklist's uncouple-anim): car_gear's coupler_jaw,
/// turned through <see cref="SceneArt.KnuckleSwing"/> about <see cref="SceneArt.KnucklePin"/>, lands where the whole cut
/// coupler (coupler_open) has its knuckle, so the swing ends on the model that was drawn before it.
/// </summary>
public class CouplerArtTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());

    static (Vector3 Min, Vector3 Max) Box(IEnumerable<Vector3> ps) =>
        (ps.Aggregate(new Vector3(float.MaxValue), Vector3.Min), ps.Aggregate(new Vector3(float.MinValue), Vector3.Max));

    [Fact]
    public void TheKnuckleSwungOpenIsWhereTheCutCouplersKnuckleIs()
    {
        var props = PropArt.Of(Look);
        var jaw = props.Get("coupler_jaw");
        var open = props.Get("coupler_open");
        var head = props.Get("coupler_head_open");
        Assert.NotNull(jaw);
        Assert.NotNull(open);
        Assert.NotNull(head);
        var turn = Matrix4x4.CreateTranslation(-SceneArt.KnucklePin) * Matrix4x4.CreateRotationY(SceneArt.KnuckleSwing) * Matrix4x4.CreateTranslation(SceneArt.KnucklePin);
        var swung = jaw!.Vertices.Select(v => Vector3.Transform(v.Position, turn)).ToList();
        // What the whole cut coupler has that its head alone doesn't: its knuckle, swung open.
        var headBox = Box(head!.Vertices.Select(v => v.Position));
        var knuckle = open!.Vertices.Select(v => v.Position)
            .Where(p => p.X > SceneArt.KnucklePin.X - 0.3f && p.X < SceneArt.KnucklePin.X + 0.3f && p.Z < -0.4f && p.Y > 0.7f && p.Y < 1.1f).ToList();
        var (sMin, sMax) = Box(swung);
        var (kMin, kMax) = Box(knuckle);
        // The swung knuckle sits inside the open coupler's head region, a couple of centimetres of bake slop either way...
        Assert.InRange(sMin.X, kMin.X - 0.05f, kMax.X);
        Assert.InRange(sMax.X, kMin.X, kMax.X + 0.05f);
        Assert.InRange(sMin.Z, kMin.Z - 0.05f, kMax.Z);
        // ...and it has moved: shut, it's somewhere else.
        var (jMin, jMax) = Box(jaw.Vertices.Select(v => v.Position));
        Assert.True(Vector3.Distance((jMin + jMax) / 2, (sMin + sMax) / 2) > 0.05f, "the knuckle didn't swing");
        Assert.True(headBox.Max.X > 0);
    }
}
