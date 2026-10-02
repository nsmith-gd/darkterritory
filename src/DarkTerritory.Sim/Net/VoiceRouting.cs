using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>How one listener hears one speaker's frame. A frame can arrive both ways at once (said into a radio, next to you).</summary>
[Flags]
public enum VoicePath : byte
{
    None = 0,
    /// <summary>In the air: positional, falling off to nothing at 26 m (spec A.5).</summary>
    Proximity = 1,
    /// <summary>Through the walkie-talkie: flat, band-limited, whole run, dead in tunnels.</summary>
    Radio = 2,
    /// <summary>Through a car wall: −12 dB and a 900 Hz lowpass on the proximity path.</summary>
    Occluded = 4,
    /// <summary>The dead channel (spec C.1): spectators to spectators only.</summary>
    Dead = 8,
    /// <summary>
    /// A Soot Child calling in a crewmate's voice (T40): played from where it is, at one loudness however far (spec
    /// A.5's tell), from the frames the host kept. <see cref="VoiceFrame.Source"/> says which.
    /// </summary>
    Mimic = 16,
    /// <summary>
    /// The speaker's mouth covered (GDD v1.1 App. A.5, Tippy Toesie; App. C.8 "voice effects on the server"): the host says
    /// so, the listener muffles it.
    /// </summary>
    Muffled = 32,
    /// <summary>
    /// The speaker's being drained (App. A.6, a Soot Child): their cries grow weaker and quieter. A gain byte follows
    /// (<see cref="VoiceFrame.Gain"/>), the host's.
    /// </summary>
    Fading = 64,
    /// <summary>
    /// A dead player's Live Mic (GDD App. D.7), with <see cref="Proximity"/>: their voice from their Holdout's door, not from
    /// where they are. <see cref="VoiceFrame.Source"/> says which Holdout.
    /// </summary>
    LiveMic = 128,
}

/// <summary>
/// Who hears whom (spec A.5, C.1), decided on the host from authoritative positions. The host forwards a
/// frame only to listeners it reaches, so a 20-car train fragments into voice zones in bandwidth as well as
/// in sound. The receiver does the DSP; this only decides the path.
/// </summary>
public static class VoiceRouting
{
    /// <summary>Spec A.5 cutoff. The host forwards a little past it so a speaker walking into range isn't clipped.</summary>
    public const double ProximityCutoff = 26;
    const double ForwardMargin = 4;

    /// <param name="radio">The speaker is talking on the radio, and has one (T41: the host checks).</param>
    /// <param name="listenerRadio">The listener has a radio to hear it on.</param>
    /// <param name="underground">Where else the radio's dead: down a mine head's spur (<see cref="Run.Run.Underground"/>).</param>
    /// <param name="liveMic">
    /// A dead speaker's Live Mic is on (GDD App. D.7): the door of the Holdout they wait in. The living within proximity range
    /// of it hear them from there (8 m clear, 26 m cutoff, as any voice), through the walls if they're shut in a car or the
    /// cab; the dead still hear them on the dead channel.
    /// </param>
    public static VoicePath Route(in PlayerState speaker, in PlayerState listener, bool radio, TrainOnLine train, Func<double, bool>? inTunnel = null,
        bool listenerRadio = true, Func<PlayerState, bool>? underground = null, Double3? liveMic = null)
    {
        if (!speaker.Alive)
        {
            if (!listener.Alive)
                return VoicePath.Dead;
            if (liveMic is not { } door || (door - PlayerMotor.WorldPosition(listener, train)).Length > ProximityCutoff + ForwardMargin)
                return VoicePath.None;
            return VoicePath.Proximity | VoicePath.LiveMic | (PlayerMotor.Space(listener, train) != PlayerMotor.Outside ? VoicePath.Occluded : 0);
        }
        var path = VoicePath.None;
        var a = PlayerMotor.WorldPosition(speaker, train);
        var b = PlayerMotor.WorldPosition(listener, train);
        if ((a - b).Length <= ProximityCutoff + ForwardMargin)
        {
            path |= VoicePath.Proximity;
            // Walls between you: the cab's, or a car shut up with its doors closed (an open door lets it through).
            if (PlayerMotor.Space(speaker, train) != PlayerMotor.Space(listener, train))
                path |= VoicePath.Occluded;
        }
        if (radio && listenerRadio && !InTunnel(speaker, train, inTunnel) && !InTunnel(listener, train, inTunnel)
            && underground?.Invoke(speaker) != true && underground?.Invoke(listener) != true)
            path |= VoicePath.Radio;
        return path;
    }

    /// <summary>Radio dies in tunnels (spec A.5): anyone whose nearest point on the line is under one.</summary>
    static bool InTunnel(in PlayerState s, TrainOnLine train, Func<double, bool>? inTunnel)
    {
        if (inTunnel is null)
            return false;
        double along = s.Parent != PlayerState.World && s.Parent < train.Vehicles.Count
            ? train.Cars[s.Parent].FrontDistance
            : s.LineHint;
        return inTunnel(along);
    }
}
