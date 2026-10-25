using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// Note 1021 (the director's playtest of 23 Oct, line 5: "I've been captured ... the bots aren't coming to save me ... I got
/// captured by the guy that exists between cars ... A Whistler got me ... they didn't try to rescue me"): a crewmate taken
/// where the generic rescue (<see cref="Rescue"/>, a strike from where a bot stands) can't reach them, gone for by the one bot
/// <see cref="CrewCalls.Rescue"/> names. A Whistler carries its victim off the train at a run to its nest out from its gap
/// (<see cref="Whistler.NestSite"/>): down off the standing train on foot after it, and a blow (any blow drops them,
/// <see cref="Enemy.Struck"/>). A Climber takes its victim in a car, below a roof's swing (note 945): to its car, over it on
/// the roof or beside it on the ground, as its rule has it (App. A.4 "outnumber them": outnumbered, it lets go), and a blow
/// from in there.
/// </summary>
public static partial class Heed
{
    /// <summary>A Whistler in a grab is <c>meleeRadius</c> 0.7, running: this near on the flat, a swing (m).</summary>
    const double WhistlerReach = 1.4;

    /// <summary>
    /// The first (by id) crewmate held where the generic rescue can't reach them: carried off by a Whistler, or taken in a
    /// car by a Climber. Null with nobody.
    /// </summary>
    public static Enemy? TakenAway(World world) =>
        world.ActiveEnemies.Where(e => !e.Gone && e.Phase == SpinePhase.Grab && e.Holding >= 0 && (e is Whistler || e is Climber { Inside: true }))
            .OrderBy(e => e.Id).FirstOrDefault();

    /// <summary>Whether a Whistler is carrying a crewmate off (the train is stood for it: <see cref="ConductorBot"/>).</summary>
    public static bool CarriedOff(World world) => TakenAway(world) is Whistler;

    /// <summary>
    /// The bot <see cref="CrewCalls.Rescue"/> names goes for the one taken: a walker or a gunner (the driver's own way is
    /// <see cref="ConductorBot"/>'s, out of its cab). The intent as it was for anyone else, or with nothing it can do from
    /// where it is.
    /// </summary>
    public static PlayerIntent Taken(PlayerIntent intent, in PlayerState self, World world, int selfId, StopHand? hand,
        IReadOnlyList<(int Id, PlayerState State)> crew, CrewCalls? calls)
    {
        if (hand is null || calls is null || !Free(self) || calls.Rescue(world, crew, selfId, self) is not { } grab)
            return intent;
        return GoFor(self, world, hand, grab) is { } going ? Up(self, going) : intent;
    }

    /// <summary>
    /// This tick's step toward breaking <paramref name="grab"/> (<see cref="TakenAway"/>), or null with no way from here:
    /// the Whistler on foot (only off a standing train, <see cref="CanGoDown"/>), the Climber in its car.
    /// </summary>
    public static PlayerIntent? GoFor(in PlayerState self, World world, StopHand hand, Enemy grab)
    {
        var train = world.Train;
        var at = grab.WorldPosition(train);
        if (grab is Whistler)
            return CanGoDown(self, train) ? Club(self, world, hand, at, WhistlerReach) : null;
        if (grab is not Climber climber || climber.Attached < 0 || climber.Attached >= train.Frames.Count || world.Enemies is not { } et)
            return null;
        int car = climber.Attached;
        // In there with it: the blow (a lone one knocks it off them, note 288).
        if (self.Parent == car && PlayerMotor.Indoors(self, train))
            return Strike(self, train, at, WhistlerReach) ?? Facing(self, train, at);
        // On the roofs: along them to over it, within its count (climbers.countRadius) of it, and stand there.
        if (self.Parent > 0 && self.Surface == Surface.Roof && car > 0)
        {
            var pose = train.Cars[car];
            return ToAlong(self, train, pose.FrontDistance - pose.Length / 2 - climber.Local.Z, self.Yaw) ?? new PlayerIntent();
        }
        // On the ground beside the standing train: to beside its car (the cab's, for the engine), well within its count.
        if (self.Parent == PlayerState.World && CanGoDown(self, train))
            return hand.Afoot(self, world, at, et.Climbers.CountRadius * 0.5) ?? Facing(self, train, at);
        return null;
    }
}
