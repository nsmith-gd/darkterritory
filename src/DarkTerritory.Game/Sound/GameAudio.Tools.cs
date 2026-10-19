using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The tools heard working (queue #505, note 775; the checklist's up-tells; docs/design/upgrades.md §8.1, the director, 13 Oct:
/// "all of these are going to need hard signs and feedback for the player to understand what the consequence is"): each
/// tool bought at the blacksmith has a sound at the moment it does its job, heard only where it's fitted (campaign.json's
/// <c>dress:&lt;id&gt;</c>, which every machine builds the train from) and installed. The recorder's bell, the pre-cut boards,
/// the rammer, the booster's beat, the composition shoes, the oilers and the horseshoe's glint, each beside the sound of
/// what it changes; none is a creature's tell or one of the train's alarms (spec A.4), and none replaces one.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>Under this (m/s) the train's standing, for the booster's cutting in as it pulls away.</summary>
    const double BoosterStanding = 0.2;
    /// <summary>The booster cuts in once the train's moving faster than this (m/s) from standing, regulator open.</summary>
    const double BoosterCutsIn = 0.6;
    /// <summary>The composition shoes' moan once the shoes are this hot (heat 0-1, where the drag goes hot), and again once they've cooled under the half of it.</summary>
    const double ShoesBite = 0.5;
    /// <summary>A second glint for the same shot comes no sooner than this (s): a client hears its own tick's shot, then the host's.</summary>
    const double GlintAgain = 1.0;

    bool _boosterStood = true, _shoesHot;
    readonly HashSet<int> _oiled = [];

    /// <summary>Whether a tool is fitted (its <c>dress:</c> mark) and its cue's installed.</summary>
    bool Fitted(TrainOnLine train, string id, string cue) => train.Dynamics.Tuning.Composition.Dressed(id) && HasCue(cue);

    /// <summary>
    /// The trailing-truck booster (<c>boosterEngine</c>): as the train pulls away from standing with steam on, its gear drops
    /// into mesh and a second, quicker exhaust beats under the chuff, at the engine's trailing truck.
    /// </summary>
    void Booster(TrainOnLine train, TrainDynamics rake, bool derailed)
    {
        double speed = rake.Speed;
        if (derailed || speed < BoosterStanding)
        {
            _boosterStood = true;
            return;
        }
        if (!_boosterStood || speed < BoosterCutsIn || train.LastControls.Throttle <= 0)
            return;
        _boosterStood = false;
        if (!Fitted(train, "boosterEngine", "up-tells.booster"))
            return;
        var engine = train.Frames[0];
        Cue("up-tells.booster", engine.ToWorld(new Double3(0, 0.6, engine.Shape.HalfLength * 0.7)), Occlusion(PlayerMotor.Outside));
    }

    /// <summary>
    /// The composition shoes (<c>compositionShoes</c>): on a long descent, as the shoes heat to where the iron ones go hot and
    /// glaze, their lesser, duller moan, once, at the car the listener's nearest; again only once they've cooled.
    /// </summary>
    void CompositionShoes(TrainOnLine train, double heat, Double3 at)
    {
        if (heat < ShoesBite * 0.5)
            _shoesHot = false;
        if (_shoesHot || heat < ShoesBite)
            return;
        _shoesHot = true;
        if (Fitted(train, "compositionShoes", "up-tells.composition"))
            Cue("up-tells.composition", at);
    }

    /// <summary>The axle oilers (<c>axleOilers</c>): a Hotbox's first knock eased, the oil's cushioned knock and its drip, once a Hotbox, beside the tell's own knock.</summary>
    void Oilers(TrainOnLine train, int hotbox, Double3 at, float occlusion)
    {
        if (_oiled.Add(hotbox) && Fitted(train, "axleOilers", "up-tells.oilers"))
            Cue("up-tells.oilers", at, occlusion);
    }

    /// <summary>
    /// The lucky horseshoe (<c>horseshoe</c>, <see cref="GunTuning.HorseshoeFoul"/>): a shot that would have fouled the bore
    /// without it, and didn't. The foul's the tick's and the gun's (<see cref="Guns.Fouls"/>), the same on every machine, so
    /// this one can tell from the shot's tick: the chance as fitted against the chance without the horseshoe.
    /// </summary>
    bool HorseshoeSaved(World world, uint shotTick, int gun)
    {
        if (world.Combat?.Guns is not { HorseshoeFoul: < 1 } guns)
            return false;
        var train = world.Train;
        bool wet = (train.Line.Conditions?.Adhesion(train.Dynamics.Path, train.Dynamics.Distance) ?? 1) < 1;
        double chance = guns.FoulChance * (wet ? guns.FoulWetFactor : 1);
        return !Guns.Fouls(shotTick, gun, chance) && Guns.Fouls(shotTick, gun, chance / guns.HorseshoeFoul);
    }
}
