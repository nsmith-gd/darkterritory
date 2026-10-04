using Ballast;
using Ballast.Net;
using DarkTerritory.Game.Sound;

namespace DarkTerritory.Game.Tests;

/// <summary>Spec A.5 end to end: microphone samples through host routing, Opus and the listener's mixer.</summary>
public class VoiceTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void NearVoicesAreClearAndFallOffToNothingAt26Metres()
    {
        var near = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false);
        var mid = VoiceBench.Run(Content, speakerCar: 4, speakerZ: 1, radio: false);
        var far = VoiceBench.Run(Content, speakerCar: 5, speakerZ: 0, radio: false);
        Assert.Equal(near.FramesSent, near.FramesHeard);
        Assert.True(near.NearDb > -20, $"4 m is {near.NearDb} dB");
        // Logarithmic 8–26 m: 16.5 m keeps 39% of full level, about −8 dB.
        Assert.InRange(near.NearDb - mid.NearDb, 6.5, 10);
        // Past the cutoff the host doesn't even send it.
        Assert.True(far.DistanceM > 26);
        Assert.Equal(0, far.FramesHeard);
    }

    [Fact]
    public void TheRadioCarriesTheLengthOfTheTrainBandLimited()
    {
        var r = VoiceBench.Run(Content, speakerCar: 9, speakerZ: 0, radio: true);
        Assert.True(r.DistanceM > 80);
        Assert.Equal(-180, r.NearDb);
        Assert.True(r.RadioBandDb > -15, $"radio band {r.RadioBandDb} dB");
        // 300 Hz–3 kHz: the low end of the voice is gone.
        Assert.True(r.RadioLowDb < r.RadioBandDb - 30, $"low {r.RadioLowDb} vs band {r.RadioBandDb}");
    }

    [Fact]
    public void TalkingDucksTheBedSixDecibels()
    {
        // Spec A.3 tier 2: "ducks bed −6 dB while active".
        Assert.InRange(VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false).BedDuckDb, -6.3, -5.7);
    }

    [Fact]
    public void VoiceSurvivesARoughLink()
    {
        var clean = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false);
        var rough = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false, link: new LinkConditions(0.09, 0.02, 0.05));
        Assert.True(rough.FramesHeard < rough.FramesSent);
        Assert.InRange(clean.NearDb - rough.NearDb, -1.5, 1.5);
    }

    /// <summary>
    /// GDD v1.4 App. D.2 and C.8, the hard-cut: on the tick of death the victim's voice stops mid-word, near and on the
    /// radio, with no fade and nothing buffered played out. The living hear the cut.
    /// </summary>
    [Fact]
    public void DeathCutsTheVoiceOffMidWord()
    {
        foreach (bool radio in new[] { false, true })
        {
            var r = VoiceBench.Run(Content, speakerCar: radio ? 9 : 3, speakerZ: radio ? 0 : 4, radio: radio, seconds: 2, dieAt: 1.0);
            Assert.True(r.FramesHeard > 0, "heard before the death");
            Assert.True(r.AfterCutDb < -150, $"{(radio ? "radio" : "near")}: {r.AfterCutDb} dB after the death");
        }
    }
}
