using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>The Stoker about: crossing the footplate from the tender, or feeding in the firebox (App. A.5; note 271). Null otherwise.</summary>
    public static Stoker? StokerAbout(World world) =>
        world.Train.BoilerTuning is null ? null
        : world.ActiveEnemies.OfType<Stoker>().Where(st => !st.Gone && st.Phase is SpinePhase.Telegraph or SpinePhase.Commit).OrderBy(st => st.Id).FirstOrDefault();

    /// <summary>
    /// The Stoker fought from the firebox door (note 1051; GDD §20's "better by two": one drives, one fights it), in the cab on
    /// <paramref name="side"/> of the door (the fireman's, −1), facing it: clubbed as it crosses the footplate (note 263), and
    /// once it's in the fire, never the door (it burns), the cab's hose on it (note 648), or with none the blow-off held to
    /// starve it (note 271). The way there first, within the cab.
    /// </summary>
    public static PlayerIntent AtTheFirebox(in PlayerState self, World world, Stoker stoker, int side)
    {
        var train = world.Train;
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        var (step, there) = WarmUp.SteerCab(self, train, ConductorBot.FiringSpot(firebox, side), ConductorBot.FacingFire);
        if (!there)
            return step;
        if (stoker.Boarding)
            return new PlayerIntent { Actions = PlayerActions.Swing };
        return world.Enemies is { Stoker.CabHose: true } ? new PlayerIntent { Buttons = PlayerButtons.Fire } : new PlayerIntent { Actions = PlayerActions.Vent };
    }

    /// <summary>Where a fireman stands to the firebox door, on the cab's floor (its left of the door, as the driver's is its right).</summary>
    public static Double3 FiremansSpot(TrainOnLine train) =>
        ConductorBot.FiringSpot(train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position, -1);

    /// <summary>m: how near the fireman's spot at the door a crewmate counts as there (note 1051).</summary>
    const double AtTheDoorWithin = 0.6;

    /// <summary>
    /// Note 1051: a living crewmate at the fireman's side of the firebox door (where <see cref="AtTheFirebox"/> and a fireman
    /// stand), so the driver can leave the Stoker to them and keep the controls.
    /// </summary>
    public static bool HandAtTheFirebox(TrainOnLine train, IEnumerable<PlayerState>? crewmates)
    {
        if (crewmates is null)
            return false;
        var spot = FiremansSpot(train);
        return crewmates.Any(c => c.Alive && PlayerMotor.InCab(c, train)
            && Math.Sqrt((c.Position.X - spot.X) * (c.Position.X - spot.X) + (c.Position.Z - spot.Z) * (c.Position.Z - spot.Z)) <= AtTheDoorWithin);
    }
}
