using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The figures heard (queue #503, note 773; P1's notes 570, 571, 489; asked of AU1 in #569): Dave painting at his easel
/// past a stop, a breath of a laugh at each blow, his brush set down at the last warning and the snap of his grab; Jacob's
/// line at the water's edge and his blessing running down the cars as he mends the train. Nicki's party is the town's
/// (<see cref="LookRoundTown"/>). People, not creatures: none of it is a tell.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>Jacob's blessing runs down the train from the engine: a car this many seconds after the one before.</summary>
    const double BlessingDown = 0.18;

    void DaveSounds(World world, Dave d, Creature was, Double3 at)
    {
        var t = world.Enemies?.Dave ?? new DaveTuning();
        bool began = d.Phase != was.Phase;
        if (d.Phase == SpinePhase.Dormant)
            Hold("figures.dave-paint", d.Id, at + Double3.Up * 1.2, 0);
        // Struck: a blow is the count going up (or a new striker's first). His turn at the last warning and his grab are
        // their own; the blows before them, a breath of a laugh.
        bool blow = d.StrikerBlows > 0 && (d.StrikerBlows != (int)Math.Round(was.Extra) || d.Striker != (int)Math.Round(was.Extra2));
        if (blow && d.StrikerBlows < t.Blows - 1)
            Cue("figures.dave-laugh", at + Double3.Up * 1.5, 0);
        switch (d.Phase)
        {
            case SpinePhase.Telegraph when began:
                Cue("figures.dave-turn", at + Double3.Up * 0.8, 0);
                break;
            case SpinePhase.Grab when began:
                Cue("figures.dave-grab", Victim(world.Train, d.Holding, 1.5) ?? at + Double3.Up * 1.5, 0);
                break;
        }
    }

    void JacobSounds(World world, Jacob j, Creature was, Double3 at)
    {
        Hold("figures.jacob-fish", j.Id, at + Double3.Up * 0.8, 0);
        if (j.Extra < 0.5 || was.Extra >= 0.5)
            return;
        // The train made new (World.Bless): from the engine back, car by car.
        var train = world.Train;
        var cars = train.Dynamics.Consist.Vehicles;
        for (int k = 0; k < cars.Count; k++)
            CueLater(k * BlessingDown, "figures.jacob-bless", train.Frames[cars[k].Id].Origin + Double3.Up * 2, 0, k == 0 ? 1 : 0.6f);
    }
}
