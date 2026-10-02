using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Spec A.5 and C.1: who hears whom, decided on the host.</summary>
public class VoiceRoutingTests
{
    static readonly TrainOnLine Train = new(new TrainDynamics(Consist.Uniform(Tuning.Train, 20, 1)),
        RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json")), 1200);

    static PlayerState Roof(int car, double z = 0) => PlayerMotor.SpawnOnRoof(Train, car, z, Tuning.Player);

    [Fact]
    public void NeighboursHearEachOtherAndTheFarEndOfTheTrainDoesNot()
    {
        Assert.Equal(VoicePath.Proximity, VoiceRouting.Route(Roof(5), Roof(5, 4), radio: false, Train));
        Assert.Equal(VoicePath.Proximity, VoiceRouting.Route(Roof(5), Roof(6), radio: false, Train));
        // A 20-car train is ~330 m: the crew fragments into voice zones (spec A.5).
        Assert.Equal(VoicePath.None, VoiceRouting.Route(Roof(2), Roof(12), radio: false, Train));
    }

    [Fact]
    public void TheRadioReachesTheWholeTrainButNotIntoATunnel()
    {
        Assert.Equal(VoicePath.Radio, VoiceRouting.Route(Roof(2), Roof(19), radio: true, Train));
        double rear = Train.Cars[19].FrontDistance;
        bool Tunnel(double s) => Math.Abs(s - rear) < 30;
        Assert.Equal(VoicePath.None, VoiceRouting.Route(Roof(2), Roof(19), radio: true, Train, Tunnel));
        Assert.Equal(VoicePath.None, VoiceRouting.Route(Roof(19), Roof(2), radio: true, Train, Tunnel));
        // Said into the radio next to someone, it reaches them both ways.
        Assert.Equal(VoicePath.Proximity | VoicePath.Radio, VoiceRouting.Route(Roof(5), Roof(5, 3), radio: true, Train));
    }

    [Fact]
    public void TheCabWallsMuffle()
    {
        var cab = PlayerMotor.SpawnInCab(Train, Tuning.Player);
        Assert.Equal(VoicePath.Proximity | VoicePath.Occluded, VoiceRouting.Route(cab, Roof(1), radio: false, Train));
        Assert.Equal(VoicePath.Proximity, VoiceRouting.Route(cab, PlayerMotor.SpawnInCab(Train, Tuning.Player, 0.5), radio: false, Train));
    }

    [Fact]
    public void AShutCarMufflesAndAnOpenDoorLetsItOut()
    {
        // Spec A.5 "car walls -12 dB": inside a car shut up, to someone on its roof. Open a door and it carries.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Train.Line, 1200);
        int guard = train.Vehicles.Count - 1;
        var room = train.Frames[guard].Shape.Interior!.Value;
        var inside = new PlayerState { Parent = guard, Position = new Double3(0, room.Min.Y + 0.1, room.Centre.Z), Surface = Surface.Deck, Health = 100 };
        var roof = PlayerMotor.SpawnOnRoof(train, guard, 2, Tuning.Player);
        Assert.Equal(VoicePath.Proximity | VoicePath.Occluded, VoiceRouting.Route(inside, roof, radio: false, train));
        train.Vehicles[guard].ToggleDoor(0);
        Assert.Equal(VoicePath.Proximity, VoiceRouting.Route(inside, roof, radio: false, train));
    }

    [Fact]
    public void TheDeadTalkOnlyToTheDead()
    {
        // Spec C.1: "a separate dead channel which the living cannot hear".
        var dead = Roof(5) with { Health = 0, Death = DeathCause.Mauled };
        var otherDead = Roof(15) with { Health = 0, Death = DeathCause.Choir };
        Assert.Equal(VoicePath.None, VoiceRouting.Route(dead, Roof(5, 2), radio: true, Train));
        Assert.Equal(VoicePath.Dead, VoiceRouting.Route(dead, otherDead, radio: false, Train));
        // The dead still hear the living near them: spectators watch, and say nothing useful to anyone.
        Assert.Equal(VoicePath.Proximity, VoiceRouting.Route(Roof(5, 2), dead, radio: false, Train));
    }

    [Fact]
    public void ALiveMicIsHeardFromItsHoldoutsDoorByTheLivingNearIt()
    {
        // GDD App. D.7: "the player's mic plays from the Holdout on the normal proximity voice layer" (8 m clear, 26 m cutoff),
        // and they stay audible on the dead channel. The speaker's body is wherever it fell: what counts is the door.
        var dead = Roof(15) with { Health = 0, Death = DeathCause.Mauled };
        var rescuer = Roof(5);
        var door = PlayerMotor.WorldPosition(rescuer, Train) + new Double3(0, 0, 6);
        Assert.Equal(VoicePath.Proximity | VoicePath.LiveMic, VoiceRouting.Route(dead, rescuer, radio: false, Train, liveMic: door));
        // Off, they're the dead talking to the dead as ever.
        Assert.Equal(VoicePath.None, VoiceRouting.Route(dead, rescuer, radio: false, Train));
        Assert.Equal(VoicePath.Dead, VoiceRouting.Route(dead, Roof(6) with { Health = 0, Death = DeathCause.Choir }, radio: false, Train, liveMic: door));
        // Past the cutoff (and the host's margin), nobody hears it: only a rescuer at the door does.
        Assert.Equal(VoicePath.None, VoiceRouting.Route(dead, Roof(12), radio: false, Train, liveMic: door));
        // From outside, so through the cab's walls to someone in it.
        var cab = PlayerMotor.SpawnInCab(Train, Tuning.Player);
        var nearCab = PlayerMotor.WorldPosition(cab, Train) + new Double3(0, 0, 4);
        Assert.Equal(VoicePath.Proximity | VoicePath.LiveMic | VoicePath.Occluded, VoiceRouting.Route(dead, cab, radio: false, Train, liveMic: nearCab));
        // The radio doesn't carry it: the dead have none.
        Assert.Equal(VoicePath.Proximity | VoicePath.LiveMic, VoiceRouting.Route(dead, rescuer, radio: true, Train, liveMic: door));
    }
}
