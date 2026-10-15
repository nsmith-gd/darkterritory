using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// ARCHITECTURE §8 note 504: a Follower aboard drops off its host's back to the floor under them (App. A.6 "drops off
/// aboard") before it crawls to its car, falling in the host's car (so a moving train carries it), and still nests.
/// </summary>
public class FollowerDropTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void ItDropsOffItsHostsBackToTheirFeetThenCrawlsAndNests(double speed)
    {
        var t = Tuning.Enemies.Followers;
        var n = new Night(4, speed);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, Tuning.Player);
        var f = n.World.AddEnemy(id => Follower.On(id, n.Train, n.Crew[2], 2, t));
        for (int i = 0; i < 6000 && f.Phase != SpinePhase.Commit; i++)
            n.Run(Sim.SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Commit, f.Phase);
        // It lets go in its host's car, at their back.
        int car = n.Crew[2].Parent;
        Assert.Equal(car, f.Attached);
        var from = f.Local;
        double feet = n.Crew[2].Position.Y;
        Assert.InRange(from.Y - feet, Follower.BackUp - 0.05, Follower.BackUp + 0.05);
        // Half way through its drop it's in the air, straight down from where it let go, still in the car.
        n.Run(Follower.DropSeconds(t) * 0.5);
        Assert.Equal(car, f.Attached);
        Assert.InRange(f.Local.Y, feet + 0.05, from.Y - 0.05);
        Assert.Equal(from.X, f.Local.X, 6);
        Assert.Equal(from.Z, f.Local.Z, 6);
        // Down by the end of it, on the floor under them.
        n.Run(Follower.DropSeconds(t) * 0.5 + 0.1);
        double y = f.Attached == car ? f.Local.Y : n.Train.Frames[car].ToLocal(f.WorldPosition(n.Train)).Y;
        Assert.InRange(y, feet - 0.02, feet + 0.02);
        for (int s = 0; s < 400 && !f.Nested && !f.Gone; s++)
            n.Run(1);
        Assert.True(f.Nested, $"{f.Phase}");
    }

    [Fact]
    public void ItsDropTakesAboutHalfASecond() => Assert.InRange(Follower.DropSeconds(Tuning.Enemies.Followers), 0.45, 0.6);
}
