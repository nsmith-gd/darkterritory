using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The sense table heard (queue #511, note 781; K1's #1026, note 728): what the crew's things now do to the creatures, heard as
/// the things themselves, from what every machine sees. Something heavy come down on the rails rings them (the knock the Car
/// Hugger and Tower Jaw answer, <see cref="World.KnockRail"/>, which is the host's alone: heard here off the body's own
/// landing, by the sim's rule); a thrown lamp's glass and oil (<see cref="CrewCarried"/>, off <c>Body.Broken</c> and the lamp
/// gone mid-flight into a car's fire); and a hound in the extinguisher's jet, a hiss off its hide and a yelp, by the spray's
/// own reach and cone (<see cref="World.Sprayed"/>'s emitter, <see cref="CinderHound.Reaches"/>).
/// </summary>
public sealed partial class GameAudio
{
    // A hound out of the jet this long is heard afresh when it's caught again; one held in it, again this often (the hiss off
    // its hide renewed, not one long yelp).
    const double HoundDousedAgain = 0.6, HoundDousedEvery = 1.4;
    /// <summary>Each hound in a jet lately (by its enemy id): when it was last heard doused, and when the jet was last on it.</summary>
    readonly Dictionary<int, (double Heard, double Last)> _houndSprayed = [];

    /// <summary>
    /// A knock on the rail (note 728, senses.json <c>knock</c>): something come down at <c>speedFrom</c> or faster on the ground
    /// within <c>railWithin</c> of a track's centre, as the host's <c>Thrown</c> finds it: the rail's ringing clank there.
    /// </summary>
    void RailKnock(World world, Double3 at, double speed, double hint, float occlusion)
    {
        if (speed >= world.Senses.Knock.SpeedFrom && world.OnRail(at, hint))
            Cue("world-rail-knock.knock", at, occlusion);
    }

    /// <summary>
    /// The extinguisher's jet on a Cinder Hound (note 728's <c>answers.cinderHounds</c>): each sprayer this tick (<see cref="_jets"/>)
    /// as the sim's spray emitter, from the eye along the look out to the item's reach in <c>spray.coneDeg</c>, on a hound aboard
    /// on the same side of a car's walls. Caught, the hiss off its hide and its yelp where it is.
    /// </summary>
    void HoundsDoused(World world)
    {
        var train = world.Train;
        if (_jets.Count > 0 && world.ActiveEnemies.OfType<CinderHound>().Any(h => !h.Gone && h.Attached >= 0))
        {
            double eye = train.Dynamics.Tuning.Pick.EyeHeight;
            double reach = world.Senses.Of("extinguisherSpray").Select(g => g.Radius).DefaultIfEmpty(0).Max();
            double cone = Math.Cos(world.Senses.Spray.ConeDeg * Math.PI / 180);
            foreach (var (carrier, body) in _jets)
            {
                if (CrewStates.Where(p => p.Id == carrier).Select(p => (PlayerState?)p.State).FirstOrDefault() is not { Alive: true } s)
                    continue;
                var look = Bookmarks.Forward(s.Yaw, s.Pitch);
                if (s.Parent >= 0 && s.Parent < train.Frames.Count)
                    look = train.Frames[s.Parent].DirToWorld(look);
                var jet = new Emitter(EmitterKind.Spray, Sense.Heat, PlayerMotor.WorldPosition(s, train) + Double3.Up * eye, reach, -1, carrier, body,
                    s.Parent, look, cone);
                foreach (var h in world.ActiveEnemies.OfType<CinderHound>())
                {
                    if (h.Gone || h.Attached < 0 || h.Attached >= train.Frames.Count || !h.Reaches(train, s))
                        continue;
                    var at = h.WorldPosition(train) + Double3.Up * 0.4;
                    if (!jet.Reaches(at))
                        continue;
                    var (heard, last) = _houndSprayed.GetValueOrDefault(h.Id, (double.NegativeInfinity, double.NegativeInfinity));
                    if (_time - last > HoundDousedAgain || _time - heard >= HoundDousedEvery)
                    {
                        Cue("cs-hounds-doused.doused", at, h.Inside(train) ? Occlusion(h.Attached) : Occlusion(PlayerMotor.Outside));
                        heard = _time;
                    }
                    _houndSprayed[h.Id] = (heard, _time);
                }
            }
        }
        foreach (var id in _houndSprayed.Where(kv => _time - kv.Value.Last > 5).Select(kv => kv.Key).ToList())
            _houndSprayed.Remove(id);
    }
}
