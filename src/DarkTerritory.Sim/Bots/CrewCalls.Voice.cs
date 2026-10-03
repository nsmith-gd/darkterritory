namespace DarkTerritory.Sim.Bots;

/// <summary>
/// The crew's calls over a voice (GDD §34 "degraded comms"; note 186). Without one (<see cref="Voice"/> null, every night
/// but a harness run asked for poor comms) a call is heard at once, as before. With one, what a bot says reaches the
/// board late or not at all. Claims (a door to shut, a Holdout to breach) stay instant: they're who goes where, settled
/// by sight as much as by word, and they answer the claimant on the spot.
/// </summary>
public sealed partial class CrewCalls
{
    /// <summary>The voice the calls go over, or null for perfect comms.</summary>
    public CrewVoice? Voice { get; set; }

    uint _now;

    /// <summary>Once a tick, before anyone thinks: the clock for what's said, and what's arrived is heard.</summary>
    public void Advance(uint tick)
    {
        _now = tick;
        Voice?.Deliver(tick);
    }

    /// <summary>A call: heard now, or sent over the voice.</summary>
    void Heard(int speaker, string what, int about, object value, Action apply)
    {
        if (Voice is { } voice)
            voice.Say(_now, speaker, about < 0 ? what : $"{what} {about}", value, apply);
        else
            apply();
    }
}
