using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The roof warning by ear (note 260; GDD App. A.1, "TELEGRAPH always precedes COMMIT"): a tunnel's mouth or a bend taken
/// too fast coming, and you up top. From the line and the train as this machine has them (<see cref="Lineside.Warning"/>),
/// so nothing's sent: the HUD says it in words (<see cref="Hud.RoofWarningLines"/>), this in sound.
/// </summary>
public sealed partial class GameAudio
{
    // The warning sounded last (its board), and when it sounds again while it's still up.
    int _roofWarned = -1;
    double _roofAgain;

    void RoofWarning(World world)
    {
        if (world.Lineside is not { } lineside || lineside.Warning(world.Train) is not { } w || !RoofWarningForMe(world, lineside))
        {
            _roofWarned = -1;
            return;
        }
        var t = lineside.Tuning.RoofWarning;
        if (w.Sign == _roofWarned && _time < _roofAgain)
            return;
        _roofWarned = w.Sign;
        _roofAgain = _time + t.RepeatSeconds;
        PlayRoofWarning(w.Kind, t, world.Train);
    }

    /// <summary>
    /// Whether the listener's player is someone it's for (<see cref="Lineside.For"/>: up top, unless it's for everyone). With
    /// nobody's record to go by (a staged render), whoever's outside.
    /// </summary>
    bool RoofWarningForMe(World world, Lineside lineside)
    {
        foreach (var (id, s) in CrewStates)
            if (id == OwnId || CrewStates.Count == 1)
                return lineside.For(s, world);
        return _exposed || !lineside.Tuning.RoofWarning.RoofOnly;
    }

    /// <summary>
    /// The cue itself, over the listener: the telltales' slaps just ahead and overhead for a tunnel, the roof irons'
    /// chatter at your feet for a bend (spec A.4 rule 2: from where it is). Public for the bench (AudioBench's tells).
    /// </summary>
    public void PlayRoofWarning(SignKind kind, RoofWarningTuning t, Sim.Train.TrainOnLine train)
    {
        var ear = Mixer.Listener.Position;
        var forward = train.Frames[0].Back * -1;
        bool tunnel = kind == SignKind.LowClearance;
        var at = tunnel ? ear + forward * 3 + Double3.Up * 0.8 : ear - Double3.Up * 1.4 + forward * 1.5;
        Mixer.Play(tunnel ? t.LowClearanceSound : t.CurveSound, at);
    }

    void EndNightRoofWarning() => _roofWarned = -1;
}
