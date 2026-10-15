using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The human cannonball (note 746; docs/design/upgrades.md §8.3; the director, 15 Oct 2026: "the reload flow to be powder,
/// pack, then either a ball or a player jumps in"): on the guard van's gun, packed, a crewmate climbs in at the muzzle instead
/// of the ball, and climbs out with Jump.
/// </summary>
public class CannonballTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static readonly PlayerTuning P = Tuning.Player;
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    static World World(bool dressed)
    {
        var t = dressed ? Campaign.Campaign.Apply(C, ["humanCannonball"], Base).Train : Tuning.Train;
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 2_000, Tuning.Boiler), Tuning.Combat);
    }

    static int Van(World w) => Assert.Single(w.Train.Vehicles, v => v.Kind == VehicleKind.Guard && v.HasGun).Id;

    /// <summary>On the van's roof at its gun's muzzle, the gun fired and packed (one step, the ball's, left).</summary>
    static PlayerState AtTheMuzzle(World w)
    {
        int van = Van(w);
        w.Train.Vehicles[van].Gun.ReloadNeeded = 1;
        var s = PlayerMotor.SpawnOnRoof(w.Train, van, 0, P);
        var muzzle = HumanCannonball.Muzzle(w.Train, van, Tuning.Combat.Cannonball);
        return s with { Position = new Double3(muzzle.X, s.Position.Y, muzzle.Z) };
    }

    static void Act(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
    }

    [Fact]
    public void ItsOnTheCatalogueAndTheReloadEndsWithTheBall()
    {
        var u = Assert.Single(C.Upgrades, u => u.Id == "humanCannonball");
        Assert.Equal(UpgradeKind.Whimsy, u.Kind);
        Assert.NotNull(u.Line);
        Assert.NotNull(u.Tally);
        Assert.True(HumanCannonball.Takes(World(dressed: true).Train, Van(World(dressed: true))));
        Assert.False(HumanCannonball.Takes(World(dressed: false).Train, Van(World(dressed: false))));
    }

    [Fact]
    public void ACrewmateHoldingUseAtThePackedGunsMuzzleClimbsInAndJumpClimbsOut()
    {
        var w = World(dressed: true);
        int van = Van(w);
        var s = AtTheMuzzle(w);
        Assert.Equal(van, HumanCannonball.AtTheMuzzle(s, w.Train, Tuning.Combat.Guns, Tuning.Combat.Cannonball));
        // Not long enough: let go, and the climb starts again.
        Act(w, ref s, Use, Tuning.Combat.Cannonball.ClimbSeconds * 0.5);
        Act(w, ref s, default, 0.1);
        Assert.False(s.Has(PlayerFlags.InCannon));
        Assert.Equal(1, w.Train.Vehicles[van].Gun.ReloadNeeded);
        // Held: in, the gun loaded with them (no ball).
        Act(w, ref s, Use, Tuning.Combat.Cannonball.ClimbSeconds + 0.1);
        Assert.True(s.Has(PlayerFlags.InCannon));
        Assert.Equal(0, w.Train.Vehicles[van].Gun.ReloadNeeded);
        // Held there: walking does nothing.
        var at = s.Position;
        Act(w, ref s, new PlayerIntent { MoveZ = 1 }, 1);
        Assert.True((s.Position - at).Length < 0.05, $"moved {(s.Position - at).Length:F2} m in the barrel");
        // Jump: out, at the muzzle, and the ball's wanted again.
        Act(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Jump }, SimConstants.TickSeconds);
        Assert.False(s.Has(PlayerFlags.InCannon));
        Assert.Equal(1, w.Train.Vehicles[van].Gun.ReloadNeeded);
    }

    [Fact]
    public void WithoutTheUpgradeOrBeforeItsPackedTheMuzzleTakesNobody()
    {
        var plain = World(dressed: false);
        var s = AtTheMuzzle(plain);
        Act(plain, ref s, Use, Tuning.Combat.Cannonball.ClimbSeconds * 2);
        Assert.False(s.Has(PlayerFlags.InCannon));
        var w = World(dressed: true);
        s = AtTheMuzzle(w);
        w.Train.Vehicles[Van(w)].Gun.ReloadNeeded = 3;
        Assert.Null(HumanCannonball.AtTheMuzzle(s, w.Train, Tuning.Combat.Guns, Tuning.Combat.Cannonball));
    }
}
