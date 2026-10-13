using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// App. C.2 melee (WP19, note 275): "the tools already on the train are the weapons: shovel, wrench, crowbar. The boiler
/// player's shovel doubling as the crew's best club is intended tension." Each tool lands its own blow. (A swing reaching every
/// client, landed or not, is note 197's: HitConfirmTests.)
/// </summary>
public class MeleeTests
{
    static readonly MeleeTuning M = Tuning.Enemies.Melee;
    static readonly PlayerIntent Swing = new() { Actions = PlayerActions.Swing };

    [Fact]
    public void TheShovelIsTheBestClubAndAFistTheWeakest()
    {
        Assert.Equal(M.Shovel, M.Blow(Tool.Shovel));
        Assert.Equal(M.Crowbar, M.Blow(Tool.Crowbar));
        Assert.Equal(M.Wrench, M.Blow(Tool.Wrench));
        Assert.Equal(M.Barehanded, M.Blow(Tool.None));
        // Enemy health is in the crowbar's blows (enemies.json), everyone's tool from the start (player.json kit).
        Assert.Equal(1, M.Crowbar);
        Assert.True(M.Shovel > M.Crowbar, "the shovel is the crew's best club");
        Assert.True(M.Shovel > M.Wrench);
        Assert.True(M.Barehanded < M.Wrench && M.Barehanded < M.Crowbar && M.Barehanded > 0);
    }

    [Theory]
    [InlineData(Tool.Shovel)]
    [InlineData(Tool.Crowbar)]
    [InlineData(Tool.Wrench)]
    [InlineData(Tool.None)]
    public void EachToolLandsItsOwnBlow(Tool tool)
    {
        // A Cinder Hound: a lone blow wears it down (a Climber's needs the crew together now, note 288).
        var (n, e) = HitConfirmTests.Staged(EnemyKind.CinderHound);
        n.Crew[1] = n.Crew[1] with { Kit = Kit.Of([tool]), HeldSlot = 0 };
        Assert.Equal(tool, Kit.Held(n.Crew[1]));
        double before = e.Health;
        n.Run(1.0 / SimConstants.TickRate, _ => Swing);
        Assert.Single(n.World.Hits);
        Assert.InRange(before - e.Health, M.Blow(tool) - 1e-6, M.Blow(tool) + 1e-6);
    }
}
