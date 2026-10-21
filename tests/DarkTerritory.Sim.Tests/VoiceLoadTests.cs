using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 956 (M3's eight-person voice test, rehearsed headless): every voice a listener hears is about 22 kbit/s more of the
/// host's upload. On a home upload that can't carry them all, the host sends each listener only link.strainedVoices at once,
/// so the crew keep their snapshots; on one that can, nothing is held back.
/// </summary>
public class VoiceLoadTests
{
    static RailLine Line() => RailLine.Load(Path.Combine(Ballast.DataFile.FindContentRoot(), "lines/test-loop.json"));

    static HarnessReport Night(double hostUpKbps, int strainedVoices) => Harness.Run(Line(), Tuning.Train,
        Tuning.Player with { Link = Tuning.Player.Link with { StrainedVoices = strainedVoices } }, new HarnessOptions
        {
            Bots = 8,
            Seconds = 60,
            Talkers = 8,
            TalkRadio = true,
            Link = new LinkConditions(0.06, 0.02, 0.03) { HostUpKbps = hostUpKbps },
        }, Tuning.Boiler);

    [Fact]
    public void EightOnTheRadioOverAHomeUploadKeepTheSnapshotsComing()
    {
        var capped = Night(1000, strainedVoices: 2);
        var uncapped = Night(1000, strainedVoices: 0);
        Assert.True(capped.VoiceFramesHeldBack > 0);
        Assert.Equal(0, uncapped.VoiceFramesHeldBack);
        int Worst(HarnessReport r) => r.Clients.Min(c => c.Snapshots);
        Assert.True(Worst(capped) > Worst(uncapped) * 2, $"the worst client got {Worst(capped)} capped, {Worst(uncapped)} not, of {capped.Ticks}");
        Assert.True(capped.MaxCorrectionM < 1, $"corrected by {capped.MaxCorrectionM} m");
        Assert.True(capped.HostUpKbps < 1000, $"the host sent {capped.HostUpKbps} kbit/s");
    }

    [Fact]
    public void AnUploadThatCarriesThemAllHoldsNothingBack()
    {
        var r = Night(0, strainedVoices: 2);
        Assert.Equal(0, r.VoiceFramesHeldBack);
        Assert.True(r.HostUpKbps > 1000, $"8 on the radio to 8: {r.HostUpKbps} kbit/s");
        Assert.True(r.VoiceFramesHeard > r.VoiceFramesForwarded * 0.9);
    }
}
