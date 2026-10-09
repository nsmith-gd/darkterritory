using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Come back (note 529; the art checklist's crew-freed "next": "the camera cut in with it"; GDD App. D.8): freed inside a
/// Holdout, a held shot of you getting up from in by its door, then into your eyes as they come up with the clip.
/// </summary>
public class EyesCameBackTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CreatureArt Art = new(Look.Load(Content), Content);

    static readonly Double3 Inside = new(10, 0, 20), Door = new(12.5, 0, 20);

    static Camera Eyes(double height) => new() { Position = Inside + Double3.Up * height, Yaw = 0, Pitch = 0, FovYDegrees = 75, Near = 0.05f, Far = 2000 };

    [Fact]
    public void FirstAHeldShotOfYouGettingUpFromInByTheDoor()
    {
        var shot = Game.Eyes.CameBack(Inside, Door, 0.4, Eyes(Game.Eyes.Height))!.Value;
        // Inside, between you and the door, over you.
        var from = shot.Position - Inside;
        Assert.InRange(new Vector2((float)from.X, (float)from.Z).Length(), 1.0f, 2.3f);
        Assert.True(from.X > 0.8, "the shot isn't on the door's side of them");
        Assert.InRange(from.Y, 1.0, 2.0);
        // Looking down at you.
        var look = shot.Forward;
        var toYou = (Inside + Double3.Up * 0.45 - shot.Position).Normalized;
        Assert.True(Vector3.Dot(look, new Vector3((float)toYou.X, (float)toYou.Y, (float)toYou.Z)) > 0.95, "the shot isn't on them");
        Assert.Equal(0.05f, shot.Near);
    }

    [Fact]
    public void ThenYourEyesComingUpWithTheClipThenStood()
    {
        var eyes = Eyes(Game.Eyes.Height);
        var cut = Game.Eyes.CameBack(Inside, Door, Game.Eyes.CutIn + 0.1, eyes)!.Value;
        Assert.Equal(eyes.Yaw, cut.Yaw);
        Assert.InRange(cut.Position.Y - Inside.Y, 0.9, 1.6);
        Assert.Null(Game.Eyes.CameBack(Inside, Door, Game.Eyes.GetUpSeconds + 0.01, eyes));
        // Never down again once rising.
        double last = 0;
        for (double t = 0; t <= Game.Eyes.GetUpSeconds; t += 0.05)
        {
            double h = Game.Eyes.GettingUp(t);
            Assert.True(h >= last - 1e-9, $"the eye goes back down at {t:0.00} s");
            last = h;
        }
    }

    [Fact]
    public void TheEyeFollowsTheClipsHead()
    {
        var crew = Art.Get("crew")!;
        int head = crew.Skeleton.IndexOf("head");
        float stood = Art.Joints("crew", "getup", Game.Eyes.GetUpSeconds, false).ElementAt(head).Y;
        var off = new List<string>();
        for (double t = 0; t <= Game.Eyes.GetUpSeconds; t += 0.1)
        {
            // The eye as far under its stood height as the clip's head is under its own.
            float clip = Art.Joints("crew", "getup", t, false).ElementAt(head).Y;
            double expected = Game.Eyes.Height - (stood - clip);
            if (Math.Abs(Game.Eyes.GettingUp(t) - expected) >= 0.15)
                off.Add($"{t:0.0} s: {Game.Eyes.GettingUp(t):0.00} m, the clip's {expected:0.00}");
        }
        Assert.True(off.Count == 0, string.Join("; ", off));
    }
}
