using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public sealed partial class ConductorBot
{
    StopHand? _rescueHand;
    bool _outToRescue;

    /// <summary>Out of the cab after a crewmate a Whistler's carrying off (note 1021), or on the way back up after (for tests and traces).</summary>
    public bool OutToRescue => _outToRescue;

    /// <summary>
    /// Note 1021 (the director's playtest of 23 Oct, line 5: "A Whistler got me ... they didn't try to rescue me"; the night on
    /// local:509 was the director and one bot, the driver): the driver, if it's the bot nearest a crewmate a Whistler's carrying
    /// off (<see cref="CrewCalls.Rescue"/>), with the train standing (a Whistler grabs from nothing moving): the engine got
    /// ready to be left, down out of the cab after them on foot, and a blow (<see cref="Heed.GoFor"/>). Then back up into the
    /// cab. A Climber in a car is the walkers' (the driver keeps the controls). Null while it isn't, and isn't out on one.
    /// </summary>
    PlayerIntent? OnARescue(in PlayerState self, World world)
    {
        if (calls is null || Me < 0 || !self.Alive || Express)
        {
            _outToRescue = false;
            return null;
        }
        _rescueHand ??= new StopHand(StopJob.Driver, calls, member);
        _rescueHand.PlayerId = Me;
        if (calls.Rescue(world, Crew, Me, self) is not Whistler grab || Heed.GoFor(self, world, _rescueHand, grab) is not { } going)
        {
            if (!_outToRescue)
                return null;
            if (_rescueHand.SetBackAlone(self, world, null) is { } back)
                return back with { Buttons = back.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
            _outToRescue = _firedToLeave = false;
            return null;
        }
        if (!_outToRescue)
        {
            if (!PlayerMotor.InCab(self, world.Train))
                return null;
            if (ReadyToLeave(self, world, calls.FiremanMinding, WorkingFire) is { } readying)
                return readying;
            _outToRescue = true;
        }
        return going with { Buttons = going.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
    }

    /// <summary>
    /// Note 1021: the train stood while a Whistler's carrying a crewmate off (it grabs only from a standing train): never
    /// driven away from under them and whoever's gone after them, as <see cref="ForTheLeftBehind"/> stands for someone on
    /// the ground. Once it's dropped them, the stop's wait for its hands and anyone playing (or the left-behind's, on the
    /// open line) brings them back aboard. Null otherwise.
    /// </summary>
    PlayerIntent? ForTheTaken(World world) =>
        Express || !Heed.CarriedOff(world) || world.Train.Dynamics.Speed > CabControls.StandingBelow ? null
            : new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
}
