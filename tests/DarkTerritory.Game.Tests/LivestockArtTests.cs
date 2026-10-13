using System.Numerics;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The sheep look round at whoever's come in (note 455; the art checklist's livestock-anim): the turn and tip that point a
/// sheep's head at someone, from its own facing, whichever way round it stands in the pen.
/// </summary>
public class LivestockArtTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(MathF.PI)]
    [InlineData(0.4f)]
    public void ASheepsHeadTurnsTowardWhoeverItsLookingAt(float yaw)
    {
        // Its own forward, left and behind, turned with it.
        Vector3 Own(Vector3 v) => Vector3.Transform(v, Matrix4x4.CreateRotationY(yaw));
        Assert.Equal(0, SceneArt.SheepAim(Own(-Vector3.UnitZ), yaw).Turn, 3);
        Assert.Equal(MathF.PI / 2, SceneArt.SheepAim(Own(-Vector3.UnitX), yaw).Turn, 3);
        Assert.Equal(-MathF.PI / 2, SceneArt.SheepAim(Own(Vector3.UnitX), yaw).Turn, 3);
        Assert.Equal(MathF.PI, MathF.Abs(SceneArt.SheepAim(Own(Vector3.UnitZ), yaw).Turn), 3);
        // Up to a face a metre over its head two metres off, but no further than a sheep tips its head.
        float tip = SceneArt.SheepAim(Own(new Vector3(0, 1, -2)), yaw).Tip;
        Assert.InRange(tip, 0.3f, 0.5f);
        Assert.InRange(SceneArt.SheepAim(Own(new Vector3(0, 5, -0.5f)), yaw).Tip, 0.49f, 0.51f);
        Assert.InRange(SceneArt.SheepAim(Own(new Vector3(0, -5, -0.5f)), yaw).Tip, -0.36f, -0.34f);
    }

    [Fact]
    public void TheHeadTurnedPointsTheModelsForwardThatWay()
    {
        // CreatureArt.DrawTurned turns the head about +Y by the turn: the model's −Z goes where it was asked to look.
        var to = Vector3.Normalize(new Vector3(-0.6f, 0, -0.3f));
        var (turn, _) = SceneArt.SheepAim(to, 0);
        var forward = Vector3.Transform(-Vector3.UnitZ, Matrix4x4.CreateRotationY(turn));
        Assert.True(Vector3.Distance(forward, to) < 1e-4f, $"{forward} vs {to}");
    }
}
