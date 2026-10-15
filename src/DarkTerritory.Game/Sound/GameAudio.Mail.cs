using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// K1's merged asks heard (queue #504, note 774): the mail catch at a lineside crane (note 727), the bag snatched into the
/// catcher arm or, hooked over the crane's posted speed, torn off and gone; and a comet fragment (note 718) humming low and
/// wrong wherever it lies or rides. Both off what every machine has: a drop's state (<see cref="Lineside.Caught"/>,
/// <see cref="Lineside.Torn"/>) and a find's body (<see cref="Run.IsComet"/>). The speaking tube's call is
/// <see cref="VoiceChat"/>'s.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>How near a comet fragment is heard (m), and the hold owners for them, clear of the creatures' and the town's.</summary>
    const double CometReach = 30;
    const int CometOwner = 120_000;
    /// <summary>The drops already heard caught or torn, so each is heard once.</summary>
    readonly HashSet<int> _dropsHeard = [];
    /// <summary>Joined mid-run, the bags already caught were caught before this ear: taken as heard, not played all at once.</summary>
    bool _dropsPrimed;

    void MailAndCometSounds(World world, Double3 ear)
    {
        var train = world.Train;
        if (world.Lineside is { } lineside)
            foreach (var d in lineside.AllDrops)
            {
                bool caught = lineside.Caught(d.Id), torn = lineside.Torn(d.Id);
                if (!caught && !torn || !_dropsHeard.Add(d.Id) || !_dropsPrimed)
                    continue;
                // At the crane's arm by the line, a car's door high, where the catcher met it.
                var at = train.Line.Sample(d.At);
                var side = Double3.Cross(at.Tangent, Double3.Up).Normalized * d.Side;
                Cue(torn ? "crew-mail.tear" : "crew-mail.catch", at.Position + side * 2.2 + Double3.Up * 2.6, 0);
            }
        _dropsPrimed = world.Lineside is not null;
        foreach (var b in world.Bodies.All)
        {
            if (!Run.IsComet(b))
                continue;
            var at = Bodies.WorldCentre(b, train);
            if ((at - ear).Length <= CometReach)
                HoldLevel("world-comet.hum", CometOwner + b.Id, at, b.Parent >= 0 ? Occlusion(SpaceOf(train, b.Parent, b.Centre)) : 0, 1);
        }
    }
}
