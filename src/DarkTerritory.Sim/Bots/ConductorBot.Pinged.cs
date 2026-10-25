using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public sealed partial class ConductorBot
{
    StopHand? _pingHand;
    bool _outOnPing;

    /// <summary>Out of the cab on a ping's job (note 936), or on the way back up after it (for tests and the harness's trace).</summary>
    public bool OutOnPing => _outOnPing;
    /// <summary>What it's doing out there (for tests and traces).</summary>
    public string? PingStep => _outOnPing ? _pingHand?.Doing : null;

    /// <summary>
    /// Note 936 (the director's playtest of 20 Oct, one bot crewmate driving: "I wish there was a way I could just send this guy
    /// out to go and throw the spur for me"; "Oh, an abandoned house? Sure, I'll go search it"): a walker's job pinged with no
    /// walker alive is the driver's (<see cref="CrewCalls.Pings"/>). It stands the train (on the brake, from cruise; a stop's
    /// moves it finishes first), gets the engine ready to be left (<see cref="ReadyToLeave"/>, as for a Holdout's breach), and
    /// goes: the points' lever, the crane, the town, a find, the cargo through a <see cref="StopHand"/>; a fire on a walker's
    /// legs. The train stands on its brake meanwhile, so it's held for the job. Then back up into the cab, and on. Null while
    /// it has no such order and isn't out on one.
    /// </summary>
    PlayerIntent? OnAPing(in PlayerState self, World world, uint tick)
    {
        var train = world.Train;
        var hold = new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        if (calls is null || Me < 0 || !self.Alive || Express)
        {
            _outOnPing = false;
            return null;
        }
        _pingHand ??= new StopHand(StopJob.Driver, calls, member);
        _pingHand.PlayerId = Me;
        if (calls.OrderOf(Me) is not { Kind: not PingKind.Cab } order)
            return _outOnPing ? BackFromAPing(self, world) is { } back ? back with { Buttons = back.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 } : null : null;
        if (!_outOnPing)
        {
            // Down at a stop's loading already (note 261): straight on to the job from there.
            if (_workingOut)
                _workingOut = false;
            else if (!PlayerMotor.InCab(self, train))
                return null;
            // Moving: stood first, from cruise; a stop's own moves (in, out, a set-back) are finished first.
            if (train.Dynamics.Speed > CabControls.StandingBelow)
                return Stops is { Doing: not StopDriver.Leg.Cruise } ? null : hold;
            if (PlayerMotor.InCab(self, train) && ReadyToLeave(self, world, calls.FiremanMinding, WorkingFire) is { } readying)
                return readying;
            _outOnPing = true;
        }
        PlayerIntent? doing;
        if (order.Kind == PingKind.Fire)
        {
            // A fire on a walker's legs, as the driver handed over works one (the pinged car's first in its trouble).
            _legs ??= new RoofWalkerBot(member * 1000 + 977);
            _legs.Me = Me;
            _legs.Calls = calls;
            _legs.Crew = Crew;
            doing = _legs.Decide(self, world, tick, out _);
        }
        else
            doing = _pingHand.Ordered(self, world, order.Kind, order.Target);
        Stops?.Decide(self, world); // its clock runs on while it's out of the cab
        var step = doing ?? new PlayerIntent();
        return step with { Buttons = step.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
    }

    /// <summary>
    /// Done with a ping's job: back up into the cab, anything in its arms put down first (a find it fetched, down aboard the
    /// cab). Null once it's there (and no longer out on it).
    /// </summary>
    PlayerIntent? BackFromAPing(in PlayerState self, World world)
    {
        if (self.Has(PlayerFlags.Heavy) || world.Bodies.CarriedBy(Me) is not null && PlayerMotor.InCab(self, world.Train))
            return _pingHand!.LetGo() with { Buttons = PlayerButtons.Use };
        if (_pingHand!.SetBackAlone(self, world, null) is { } back)
            return back;
        _outOnPing = _firedToLeave = false;
        return null;
    }

    /// <summary>
    /// Note 1021 (the director's playtest of 23 Oct, line 8: "I pinged one of the towns and Dunmore said, I'll go fetch it ...
    /// and then he starts manning the cab"): handed over (someone playing holds the controls, note 574), the driver is still
    /// the crew's only pair of hands to the pings (<see cref="CrewCalls.Pings"/>), so it takes a find, the town, the crane,
    /// the cargo or the points, and says so. But its walker's legs (<see cref="Handed"/>) have no stop hand to work them with:
    /// it never went, and the order ran out its time. Now it works them as <see cref="OnAPing"/> does, through its
    /// <see cref="StopHand"/>, while the train stands (whoever has the controls drives; a fire stays the legs'), and comes
    /// back aboard after. Null with no such order and nothing to come back from.
    /// </summary>
    PlayerIntent? HandedPing(in PlayerState self, World world)
    {
        if (calls is null || Me < 0 || !self.Alive)
        {
            _outOnPing = false;
            return null;
        }
        _pingHand ??= new StopHand(StopJob.Driver, calls, member);
        _pingHand.PlayerId = Me;
        if (calls.OrderOf(Me) is not { Kind: not (PingKind.Cab or PingKind.Fire) } order || world.Train.Dynamics.Speed > CabControls.StandingBelow)
            return _outOnPing ? BackFromAPing(self, world) : null;
        _outOnPing = true;
        return _pingHand.Ordered(self, world, order.Kind, order.Target) ?? new PlayerIntent();
    }

    /// <summary>
    /// Note 936 (the director, 20 Oct: "trying to find a way to tell them to go search the house with me. And of course, we're
    /// just moving right away"): standing, with a crewmate bot sent off to a job by a ping, the train's held on its brake till
    /// it's done (or its order's time is up, <c>pings.orderSeconds</c>). At a stop <see cref="StopDriver"/>'s departure waits
    /// for it (note 935's one rule, agreed with #638); this is the rest: standing on the open line. Null otherwise.
    /// </summary>
    PlayerIntent? ForAPing(World world)
    {
        if (calls is not { JobsOrdered: true } || Express || world.Train.Dynamics.Speed > CabControls.StandingBelow
            || Stops is not { Doing: StopDriver.Leg.Cruise })
            return null;
        return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
    }
}
