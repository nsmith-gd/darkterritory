using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// K1's crew car and crew memory heard (queue #506, note 776; K1's #1016, notes 716 and 721). The kettle on the big cab's back
/// wall singing as the gauge nears the redline (<see cref="Sim.Train.CrewCar.KettleSong"/>), the cab's own sign the pressure is climbing
/// for a crew on the gallery with their backs to the dials; the cuckoo clock's bird calling each time it comes out in the last
/// minutes before dawn (<see cref="Sim.Train.CrewCar.Cuckoo"/>). Both off what every machine already has, as the scene draws them. The
/// crew photo's flash and the logbook's pages are this machine's own: <see cref="PhotoFlash"/> and the logbook's UI cues.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The kettle's hold owner, clear of the creatures', the town's and the comets'.</summary>
    const int KettleOwner = 130_000;
    /// <summary>The cuckoo's bird out last tick (its call's progress), so each time it comes out is heard once.</summary>
    double? _cuckooOut;

    void CrewCarSounds(World world)
    {
        var train = world.Train;
        var shape = train.Frames[0].Shape;
        // The big cab only (the scene's own test): an engine without the crew car has no back wall.
        if (shape.Cab is not { } cab || cab.Max.Z - cab.Min.Z <= 8)
        {
            _cuckooOut = null;
            return;
        }
        var layout = train.Dynamics.Tuning.Geometry.Engine.CrewCar;
        var engine = train.Frames[0];
        if (train.BoilerTuning is { } boiler && Sim.Train.CrewCar.KettleSong(train.Boiler.Pressure, boiler, layout) is > 0 and var song)
        {
            var spout = TrainKit.KettleSpout(shape);
            var local = new Double3(spout.X, spout.Y, spout.Z);
            HoldLevel("crew-car.kettle", KettleOwner, engine.ToWorld(local), Occlusion(SpaceOf(train, 0, local)), (float)song);
        }
        double? bird = world.Run is { } run && world.Enemies?.Wakers is { } wakers ? Sim.Train.CrewCar.Cuckoo(run.DawnIn, wakers.StirSeconds, layout) : null;
        if (bird is not null && _cuckooOut is null)
        {
            var door = TrainKit.CuckooDoor(shape);
            var local = new Double3(door.X, door.Y, door.Z);
            Cue("crew-car.cuckoo", engine.ToWorld(local), Occlusion(SpaceOf(train, 0, local)));
        }
        _cuckooOut = bird;
    }

    /// <summary>The crew photo taken at the gate (note 721): the flash powder's whump and the shutter, where the flash went off.</summary>
    public void PhotoFlash(Double3 at) => Cue("crew-memory.flash", at, 0);
}
