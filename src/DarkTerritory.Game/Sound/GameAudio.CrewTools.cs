using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The crew's tools heard (queue #753, ARCHITECTURE §8 note 1052; docs/design/upgrades.md §8.1's second tell: "a sound and a
/// word where it happened"): the communication cord's gong in the cab, struck the car's number of times; the telltale's relay
/// dropping out on the backhead as the train parts; a fusee struck and burning where it lies. Each from what every machine
/// has (the world record's last pull, the rakes, the bodies), so every crewmate hears the same strokes. None is a creature's
/// tell or one of the train's alarms (spec A.4): the cord's gong is lower and slower than the overspeed bell.
/// </summary>
public sealed partial class GameAudio
{
    // The last pull rung, the strokes still to strike for it and when the next falls; the telltale as last heard.
    uint _cordRung;
    int _cordStrokes;
    double _cordNext;
    bool? _telltaleWas;
    readonly HashSet<int> _fuseesSeen = [];

    void CrewToolSounds(World world)
    {
        var train = world.Train;
        if (train.Frames.Count == 0 || train.Frames[0].Shape.Cab is null)
            return;
        var engine = train.Frames[0];
        var kit = train.Dynamics.Tuning.Kit;
        // A new pull: the car's number of strokes, from now. One rung already (joined after it) isn't rung again.
        if (world.Cord is { Any: true } cord && cord.Tick != _cordRung)
        {
            bool fresh = _cordRung != 0 || world.Tick <= cord.Tick || (world.Tick - cord.Tick) * SimConstants.TickSeconds < kit.CordBell.ShowSeconds;
            _cordRung = cord.Tick;
            if (fresh)
            {
                _cordStrokes = Math.Clamp(cord.Car, 1, Math.Max(1, kit.CordBell.Strokes));
                _cordNext = _time;
            }
        }
        if (_cordStrokes > 0 && _time >= _cordNext)
        {
            Cue("up-tells.cord-bell", engine.ToWorld(D(Art.TrainKit.CordGongAt(engine.Shape))), Occlusion(0));
            _cordStrokes--;
            _cordNext = _time + kit.CordBell.StrokeSeconds;
        }
        // The telltale gone out: its relay, once, where the lamp is.
        var lit = Telltale.Lit(train);
        if (_telltaleWas == true && lit == false)
            Cue("up-tells.telltale-out", engine.ToWorld(D(Art.TrainKit.TelltaleLampAt(engine.Shape))), Occlusion(0));
        _telltaleWas = lit;
        // Fusees: struck where one's first seen (not one burning already when this machine came to it), then burning.
        foreach (var b in world.Bodies.All)
        {
            if (b.Kind != BodyKind.Fusee || b.Parent != PlayerState.World && b.Parent >= train.Frames.Count)
                continue;
            var at = Bodies.WorldCentre(b, train);
            float occlusion = b.Parent == PlayerState.World ? Occlusion(PlayerMotor.Outside) : OccludedAt(train, b.Parent, b.Centre);
            if (_fuseesSeen.Add(b.Id) && b.Charge > 0.98)
                Cue("up-fusee.strike", at, occlusion);
            if (b.Charge > 0 && Hold("up-fusee.burn", b.Id, at, occlusion) is { } burn)
                burn.Volume = (float)Math.Clamp(b.Charge / 0.03, 0, 1);
        }
        if (_fuseesSeen.Count > 64)
            _fuseesSeen.RemoveWhere(id => !world.Bodies.All.Any(b => b.Id == id));
    }

    static Double3 D(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    void EndNightCrewTools()
    {
        _cordRung = 0;
        _cordStrokes = 0;
        _telltaleWas = null;
        _fuseesSeen.Clear();
    }
}
