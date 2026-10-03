using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// App. C.2 melee (WP19, note 191): "the tools already on the train are the weapons: shovel, wrench, crowbar. The boiler
/// player's shovel doubling as the crew's best club is intended tension." Each tool lands its own blow, and every swing,
/// landed or not, reaches every client for the swinger's figure.
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
        var (n, e) = HitConfirmTests.Staged(EnemyKind.Climber);
        n.Crew[1] = n.Crew[1] with { Kit = Kit.Of([tool]), HeldSlot = 0 };
        Assert.Equal(tool, Kit.Held(n.Crew[1]));
        double before = e.Health;
        n.Run(1.0 / SimConstants.TickRate, _ => Swing);
        Assert.Single(n.World.Hits);
        Assert.InRange(before - e.Health, M.Blow(tool) - 1e-6, M.Blow(tool) + 1e-6);
    }

    [Fact]
    public void AClientSeesAFriendsSwingLandedOrNot()
    {
        // n145's "not yet": a remote crewmate's swing wasn't in the snapshot. Now each swing the host takes is a record while
        // it's recent, with who swung, when and with what, a swing at nothing as much as a blow that lands.
        var n = new Night(4, speed: 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, Tuning.Player) with { Kit = Kit.Of([Tool.Shovel]) };
        uint tick = n.World.Tick;
        n.Run(1.0 / SimConstants.TickRate, _ => Swing);
        Assert.Empty(n.World.Hits);
        var swing = Assert.Single(n.World.Swings);
        Assert.Equal((1, Tool.Shovel), (swing.By, swing.Tool));
        Assert.InRange(swing.Tick, tick, tick + 1);
        var client = new World(n.Train);
        var controls = new TrainControls();
        client.EnableEnemies(Tuning.Enemies, null, 1, 1, authority: false);
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        Assert.Equal(swing, Assert.Single(client.Swings));
        // Held on, one swing a recovery (swingSeconds), each one sent; and gone from the wire once it's past (hits.keepSeconds).
        n.Run(M.SwingSeconds * 2.5, _ => Swing);
        Assert.InRange(n.World.Swings.Count, 1, (int)Math.Ceiling(Tuning.Combat.Hits.KeepSeconds / M.SwingSeconds) + 1);
        n.Run(Tuning.Combat.Hits.KeepSeconds + 0.1);
        Assert.Empty(n.World.Swings);
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        Assert.Empty(client.Swings);
    }

    [Fact]
    public void ASwingIsOneRecordPerRecovery()
    {
        var n = new Night(4, speed: 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, Tuning.Player);
        n.Run(M.SwingSeconds * 0.9, _ => Swing);
        Assert.Single(n.World.Swings);
        // The dead swing nothing.
        var m = new Night(4, speed: 0);
        m.Crew[1] = PlayerMotor.SpawnOnRoof(m.Train, 2, 0, Tuning.Player) with { Death = DeathCause.Mauled };
        m.Run(0.2, _ => Swing);
        Assert.Empty(m.World.Swings);
    }
}
