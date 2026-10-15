using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The handcar heard (queue #514, note 784; K1's #1026, note 729: "the sim's state says when"). Off its replicated state
/// (<see cref="Sim.Train.Handcar"/>) and its rake, as every machine runs it: the thud of it set on the rails and the clank of
/// it hung back on the van (<see cref="Sim.Train.Handcar.Carried"/>); on the rails, its little wheels rolling and clicking
/// over each joint by its speed, the brake shoe's squeal while the lever's on (<see cref="Sim.Train.Handcar.Brake"/>), and
/// the handle's creak at the top and bottom of its swing (<see cref="Sim.Train.Handcar.Phase"/>, geared to the wheels, so
/// it creaks as fast as it runs). The train's own wheels and bridges leave it out.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The handcar's hold owner, clear of the cars' and the creatures'.</summary>
    const int HandcarOwner = 131_000;
    // Its axles from its middle (a 3 m trolley's wheels 1.5 m apart), and how fast it must run to be heard rolling.
    const double HandcarAxle = 0.75, HandcarRolls = 0.3;
    /// <summary>The handcar as last heard: hung or on the rails, the handle's swing, and where each axle was along the line.</summary>
    (bool Carried, double Phase, double Front, double Back)? _handcarWas;

    void HandcarSounds(World world)
    {
        var train = world.Train;
        if (train.Handcar is not { } h || h.Id >= train.Frames.Count || train.HandcarRake is not { } rake)
        {
            _handcarWas = null;
            return;
        }
        var frame = train.Frames[h.Id];
        var pose = train.Cars[h.Id];
        var t = train.Dynamics.Tuning.Handcar;
        float outside = Occlusion(PlayerMotor.Outside);
        double middle = pose.FrontDistance - pose.Length / 2;
        double front = middle + HandcarAxle, back = middle - HandcarAxle;
        var was = _handcarWas;
        _handcarWas = (h.Carried, h.Phase, front, back);
        // The first sight of it isn't news (a client joining, a night begun).
        if (was is not { } w)
            return;
        // Set down on the rails at the lift's end, or put back on the van's hooks.
        if (w.Carried && !h.Carried)
            Cue("crew-handcar.set", frame.ToWorld(new Double3(0, 0.3, 0)), outside);
        else if (!w.Carried && h.Carried)
            Cue("crew-handcar.hang", frame.ToWorld(new Double3(0, t.DeckHeight, 0)), outside);
        if (h.Carried)
            return;
        double speed = rake.Speed;
        var under = frame.ToWorld(new Double3(0, 0.3, 0));
        if (speed > HandcarRolls)
        {
            HoldLevel("crew-handcar.roll", HandcarOwner, under, outside, Math.Clamp(speed / 8, 0.2, 1));
            // Up in pitch with its speed (install.py's rate on "speed"), as the train's own roll is.
            if (_held.TryGetValue(("crew-handcar.roll", HandcarOwner), out var rolling))
                rolling.Params.Set("speed", speed);
            if (h.Brake > 0)
                HoldLevel("crew-handcar.brake", HandcarOwner, under, outside, Math.Clamp(h.Brake * speed / 6, 0.25, 1));
            // A click at every rail joint each axle runs over (the train's rail length, GameAudio.Train's RailM).
            float click = (float)Math.Clamp(0.35 + speed / 12, 0.35, 1);
            foreach (var (now, then, z) in new[] { (front, w.Front, -HandcarAxle), (back, w.Back, HandcarAxle) })
                if (Math.Floor(now / RailM) != Math.Floor(then / RailM) && Math.Abs(now - then) < RailM)
                    Cue("crew-handcar.joint", frame.ToWorld(new Double3(0, 0.2, z)), outside, click);
        }
        // The handle at the top or bottom of its swing (an end's height, Handcar.EndHeight, at 0 and π): it takes up its lash.
        double turn = Math.IEEERemainder(h.Phase - w.Phase, DMath.Tau);
        if (turn != 0 && Math.Abs(turn) < Math.PI && Math.Floor(w.Phase / Math.PI) != Math.Floor((w.Phase + turn) / Math.PI))
            Cue("crew-handcar.creak", frame.ToWorld(new Double3(0, t.DeckHeight + 1.0, 0)), outside);
    }
}
