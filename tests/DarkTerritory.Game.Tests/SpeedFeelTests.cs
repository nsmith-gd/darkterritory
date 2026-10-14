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
    public void AboardTheViewWidensWithSpeedEasedSoTheFirstGainIsFelt()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        var train = s.Train;
        var aboard = PlayerMotor.SpawnOnRoof(train, 1, 0, s.PlayerTuning);
        train.Dynamics.Velocity = 0;
        Assert.Equal(75f, SpeedFeel.Fov(75, train, aboard, T));
        Assert.Equal(default, SpeedFeel.Offset(train, aboard, T));
        // The director's 27 km/h: already a good share of the widening, eased (under linear's 7.5 / 20 of it).
        train.Dynamics.Velocity = 27 / 3.6;
        float slow = SpeedFeel.Fov(75, train, aboard, T);
        Assert.True(slow - 75 > T.Widen * 7.5 / 20, $"{slow - 75:0.0} degrees at 27 km/h");
        train.Dynamics.Velocity = T.FullAt * 2;
        Assert.Equal(75 + (float)T.Widen, SpeedFeel.Fov(75, train, aboard, T), 3);
        // Off the train, nothing (a crewmate on the ground isn't riding).
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
