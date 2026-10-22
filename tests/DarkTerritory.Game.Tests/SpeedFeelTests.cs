using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>GDD §31 "Speed is felt" (note 709): aboard a moving train the view widens and the rail joints come through.</summary>
public class SpeedFeelTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly SpeedFeelTuning T = DataFile.Load<LookTuning>(Path.Combine(Content, LookTuning.File)).SpeedFeel;

    [Fact]
    public void AboardTheViewNeverWidensWithSpeed()
    {
        // Note 842 (the director, 16 Oct: "the FOV is adjusting a little bit ... I would get rid of that"): the widening (note 709)
        // is off, at a walking pace and at full speed, aboard and off the train.
        var s = new PrototypeSession(Content, "test-loop", 4);
        var train = s.Train;
        var aboard = PlayerMotor.SpawnOnRoof(train, 1, 0, s.PlayerTuning);
        foreach (double v in new[] { 0, 27 / 3.6, T.FullAt, T.FullAt * 2 })
        {
            train.Dynamics.Velocity = v;
            Assert.Equal(75f, SpeedFeel.Fov(75, train, aboard, T));
        }
        var ground = aboard with { Parent = PlayerState.World };
        Assert.Equal(75f, SpeedFeel.Fov(75, train, ground, T));
        Assert.Equal(default, SpeedFeel.Offset(train, ground, T));
    }

    [Fact]
    public void AtSpeedTheViewNeverShakes()
    {
        // Note 735 (the director, 14 Oct: "it seems like the cab is shaking. I don't like that ... the train feels like it's
        // sort of rocking"): the rail joints' knock and the sway on the eye (note 709) are off. Down a rail's length at full
        // speed, the eye doesn't move.
        var s = new PrototypeSession(Content, "test-loop", 4);
        var train = s.Train;
        var aboard = PlayerMotor.SpawnOnRoof(train, 1, 0, s.PlayerTuning);
        train.Dynamics.Velocity = T.FullAt;
        for (int i = 0; i < 120; i++)
        {
            train.Step(T.Joint / T.FullAt / 120, default);
            Assert.Equal(default, SpeedFeel.Offset(train, aboard, T));
        }
    }
}
