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

    public static VoicePath Route(in PlayerState speaker, in PlayerState listener, bool radio, TrainOnLine train, Func<double, bool>? inTunnel = null)
    {
        if (!speaker.Alive)
            return listener.Alive ? VoicePath.None : VoicePath.Dead;
        var path = VoicePath.None;
        var a = PlayerMotor.WorldPosition(speaker, train);
        var b = PlayerMotor.WorldPosition(listener, train);
        if ((a - b).Length <= ProximityCutoff + ForwardMargin)
        {
            path |= VoicePath.Proximity;
            // Cars have no interiors yet; the cab is the one enclosed space, so its walls are the occluder.
            if (PlayerMotor.InCab(speaker, train) != PlayerMotor.InCab(listener, train))
                path |= VoicePath.Occluded;
        }
        if (radio && !InTunnel(speaker, train, inTunnel) && !InTunnel(listener, train, inTunnel))
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
