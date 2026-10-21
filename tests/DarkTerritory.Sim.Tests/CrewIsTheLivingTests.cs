using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 957's frontier:4 4×8, the crew lost one by one: the director's crew (its gates' minCrew, the Ribbits' "never more
/// than the crew") counted the dead watching, so a pack of three Ribbits went for the last one alive. director.crewIsTheLiving.
/// </summary>
public class CrewIsTheLivingTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static int CrewAfterTwoDie(bool living)
    {
        var e = Tuning.Enemies with { Director = Tuning.Enemies.Director with { CrewIsTheLiving = living } };
        var n = new Night(6, speed: 14, enemies: e);
        for (int id = 0; id < 3; id++)
            n.Crew[id] = PlayerMotor.SpawnOnRoof(n.Train, 2 + id, 0, P);
        foreach (int id in new[] { 1, 2 })
            n.Crew[id] = n.Crew[id] with { Death = DeathCause.Eaten, Health = 0 };
        n.Run(2);
        Assert.True(n.Crew[0].Alive && !n.Crew[1].Alive && !n.Crew[2].Alive);
        return n.World.Director!.Crew;
    }

    [Fact]
    public void OnTheDirectorsCrewIsTheLivingOnly() => Assert.Equal(1, CrewAfterTwoDie(living: true));

    [Fact]
    public void OffTheDeadWatchingStillCountAsBefore() => Assert.Equal(3, CrewAfterTwoDie(living: false));

    [Fact]
    public void TheShippingTuningLeavesItOff() =>
        Assert.False(Tuning.Enemies.Director.CrewIsTheLiving, "note 957's crew count is a spawn rule: the director's call, off until then");
}
