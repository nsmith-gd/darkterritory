using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Voices as tools, the presentation's half (the director's calls of 14 Oct 2026, friendslop.md queue #454; ARCHITECTURE §8
/// note 720): the speaking tube heard end to end, tinny out of the far horn; the lamp signals' wheel; and a signal's lamp
/// drawing its shape.
/// </summary>
public class VoicesGameTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheTubeCarriesTheCabToTheGuardVanTinnyAndNothingThroughTheAir()
    {
        var r = VoiceBench.Run(Content, speakerCar: 0, speakerZ: 0, radio: false, tube: true);
        // A ten-car train's length away: nothing in the air, nothing on the radio; all of it out of the horn.
        Assert.True(r.DistanceM > 100, $"{r.DistanceM} m");
        Assert.Equal(-180, r.NearDb);
        Assert.Equal(-180, r.RadioDb);
        Assert.Equal(r.FramesSent, r.FramesHeard);
        Assert.True(r.TubeBandDb > -25, $"the tube's band {r.TubeBandDb} dB");
        // Band-limited harder than the radio (300 Hz-3 kHz): its lows and highs gone, a pipe's narrow voice.
        Assert.True(r.TubeLowDb < r.TubeBandDb - 25, $"low {r.TubeLowDb} vs band {r.TubeBandDb}");
        Assert.True(r.TubeHighDb < r.TubeBandDb - 25, $"high {r.TubeHighDb} vs band {r.TubeBandDb}");
    }

    [Fact]
    public void TheSignalWheelPicksByTheMouseLikeTheEmoteWheel()
    {
        var wheel = new SignalWheel();
        Assert.Equal(LampSignal.None, wheel.Update(false, 0, 0));
        // A tap gives STOP the first time.
        wheel.Update(true, 0, 0);
        Assert.Equal(LampSignal.Stop, wheel.Update(false, 0, 0));
        wheel.Update(true, 0, -80);
        Assert.Equal(LampSignal.Go, wheel.Update(false, 0, 0));
        wheel.Update(true, 90, 0);
        Assert.Equal(LampSignal.Back, wheel.Update(false, 0, 0));
        wheel.Update(true, -90, 0);
        Assert.Equal(LampSignal.Stop, wheel.Update(false, 0, 0));
        // And a tap again gives the last.
        wheel.Update(true, 3, 0);
        Assert.Equal(LampSignal.Stop, wheel.Update(false, 0, 0));
        Assert.Equal("U", Controls.Defaults[Control.Signal]);
    }

    [Fact]
    public void EachSignalsLampDrawsItsOwnShape()
    {
        (float W, float H) Spread(LampSignal s)
        {
            var at = Enumerable.Range(0, 48).Select(i => SceneArt.LampSignalHand(s, i * SceneArt.SignalBeat / 48)).ToList();
            return (at.Max(p => p.X) - at.Min(p => p.X), at.Max(p => p.Y) - at.Min(p => p.Y));
        }
        // STOP across (wide), GO up and down (tall, not wide), BACK UP round (both), all out in front of the body.
        var stop = Spread(LampSignal.Stop);
        var go = Spread(LampSignal.Go);
        var back = Spread(LampSignal.Back);
        Assert.True(stop.W > 0.8f && stop.W > 2 * stop.H, $"stop {stop}");
        Assert.True(go.H > 0.8f && go.W < 0.05f, $"go {go}");
        Assert.True(back.W > 0.6f && back.H > 0.6f, $"back {back}");
        foreach (var s in new[] { LampSignal.Stop, LampSignal.Go, LampSignal.Back })
            Assert.True(SceneArt.LampSignalHand(s, 0.4).Z < -0.3f, $"{s} out in front");
    }
}
