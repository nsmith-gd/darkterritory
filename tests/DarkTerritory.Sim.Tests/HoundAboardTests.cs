using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 891, the director's answer of 9 Oct 2026 on the Cinder Hounds aboard (inbox <c>D1.2-car-fires-from-packs</c>): "Cinder
/// Hounds should reignite every minute and a half they are left aboard ... Attacking a cinder hound should reset its ignite
/// burst ... Attacking should cause it to retreat. Cinder Hounds attack the player when the player is first spotted with a
/// lunge attack by jumping at the player to bite, then retreating to do it again. Cinder Hounds stop attacking and leave the
/// train if they are pushed to the back of the train and there is a valid exit (door open or roof of car)."
/// </summary>
public class HoundAboardTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static HoundTuning H => E.CinderHounds;

    /// <summary>The director's own sends off; the hound's patrol off too, so it's where it's put (the fight's not the patrol's).</summary>
    static EnemyTuning Still(bool on = true) => HoundRunTests.Quiet with
    {
        CinderHounds = HoundRunTests.Quiet.CinderHounds with
        {
            Patrol = HoundRunTests.Quiet.CinderHounds.Patrol with { On = false },
            Aboard = HoundRunTests.Quiet.CinderHounds.Aboard with { On = on },
        },
    };

    static CinderHound OnRoof(Night n, int car, double z)
    {
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, H.Health, car, new Double3(0.6, n.Train.Frames[car].Shape.RoofHeight, z), 0, 0, 0.6, hound.Id, 0);
        return hound;
    }

    /// <summary>Crewmate <paramref name="id"/> looks at the hound (as a player would, to swing at it).</summary>
    static void Face(Night n, int id, CinderHound hound)
    {
        var s = n.Crew[id];
        var d = n.Train.Frames[s.Parent].ToLocal(hound.WorldPosition(n.Train)) - s.Position;
        n.Crew[id] = s with { Yaw = Math.Atan2(-d.X, -d.Z), Pitch = 0 };
    }

    /// <summary>Swings at it until a blow lands (or 3 s go by). True if one did.</summary>
    static bool Strike(Night n, int id, CinderHound hound)
    {
        double was = hound.Health;
        for (double t = 0; t < 3 && hound.Health == was && !hound.Gone; t += SimConstants.TickSeconds)
        {
            Face(n, id, hound);
            n.Run(SimConstants.TickSeconds, who => who == id ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        }
        return hound.Health < was;
    }

    [Fact]
    public void ItLungesAtWhoeverItSpotsBitesAndRetreatsToComeAgain()
    {
        var n = new Night(4, speed: 10, enemies: Still());
        int car = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var hound = OnRoof(n, car, 2.5);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, -0.5, P);
        var modes = new List<HoundMode>();
        var bitten = new List<double>();
        double health = n.Crew[1].Health, far = 0;
        for (double t = 0; t < 12 && n.Crew[1].Alive && !n.Crew[1].Has(PlayerFlags.Held); t += SimConstants.TickSeconds)
        {
            n.Run(SimConstants.TickSeconds);
            if (modes.Count == 0 || modes[^1] != hound.Aboard)
                modes.Add(hound.Aboard);
            if (n.Crew[1].Health < health)
                bitten.Add(t);
            health = n.Crew[1].Health;
            if (bitten.Count > 0)
                far = Math.Max(far, Math.Abs(hound.Local.Z - n.Crew[1].Position.Z));
        }
        // At once, the first: no standing about first (it was the bite timer's 4 s).
        Assert.NotEmpty(bitten);
        Assert.True(bitten[0] <= H.Aboard.LungeSeconds + 0.1, $"first bite at {bitten[0]:0.00} s");
        // Then back off, a crouch, and again: lunge, retreat, still, lunge.
        int lunge = modes.IndexOf(HoundMode.Lunge);
        Assert.True(lunge >= 0, string.Join(",", modes));
        Assert.Equal([HoundMode.Lunge, HoundMode.Retreat, HoundMode.Still, HoundMode.Lunge], modes.Skip(lunge).Take(4));
        Assert.True(far >= H.Aboard.RetreatMetres, $"backed off {far:0.0} m");
        // Bitten twice down to the last, the second lunge pins them (App. A.1): a friend's still the answer.
        Assert.True(bitten.Count >= 2 || n.Crew[1].Has(PlayerFlags.Held), $"bites at {string.Join(", ", bitten.Select(b => b.ToString("0.0")))}");
        Assert.True(bitten.Count < 2 || bitten[1] - bitten[0] >= H.Aboard.CrouchSeconds, "it came again too soon");
    }

    [Fact]
    public void ABlowSetsItsIgniteClockBack()
    {
        // "Attacking a cinder hound should reset its ignite burst from its body that sets the fire."
        var n = new Night(4, speed: 10, enemies: Still());
        int car = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var hound = OnRoof(n, car, 0);
        n.Run(H.IgniteEverySeconds - 10);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire { Gone: false });
        // A crewmate up the ladder, a blow, and away again: nobody near it.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, hound.Local.Z - 1.6, P);
        Assert.True(Strike(n, 1, hound));
        Assert.False(hound.Gone);
        n.Crew.Clear();
        n.Run(H.IgniteEverySeconds - 5);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire { Gone: false });
        n.Run(8);
        Assert.Contains(n.World.ActiveEnemies, e => e is CarFire { Gone: false } f && f.Attached == car);
    }

    [Fact]
    public void ABlowDrivesItBackFromWhoeverStruckIt()
    {
        // "Attacking should cause it to retreat": struck from ahead, it goes back down the roof, away from them.
        var n = new Night(4, speed: 10, enemies: Still());
        int car = n.Train.VehicleAhead(n.Train.Dynamics.Consist.Vehicles[^1].Id);
        var hound = OnRoof(n, car, -2);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, -3.6, P);
        double z0 = hound.Local.Z;
        Assert.True(Strike(n, 1, hound));
        n.Run(H.Aboard.StruckSeconds * 0.5);
        Assert.Equal(HoundMode.Retreat, hound.Aboard);
        Assert.True(hound.Local.Z > z0 + 1.5, $"z {z0:0.0} → {hound.Local.Z:0.0}");
    }

    [Fact]
    public void DrivenToTheBackOfTheRearCarsRoofItLeavesTheTrain()
    {
        // "Cinder Hounds stop attacking and leave the train if they are pushed to the back of the train and there is a valid
        // exit (door open or roof of car)": the roof's end is always a way off.
        var n = new Night(4, speed: 10, enemies: Still());
        int car = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        double end = n.Train.Frames[car].Shape.HalfLength;
        var hound = OnRoof(n, car, end - 3);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, end - 4.6, P);
        Assert.True(Strike(n, 1, hound));
        n.Run(H.Aboard.StruckSeconds + 0.5);
        Assert.True(hound.Gone, $"{hound.Phase} at z {hound.Local.Z:0.0} of {end:0.0}");
        Assert.Contains(EnemyKind.CinderHound, n.World.DrivenOff);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void DrivenBackOverACarAheadItLeapsTheGapToTheCarBehind()
    {
        // Pushed toward the back of the train from a car ahead of the rear one: it goes on over the gap, still driven.
        var n = new Night(4, speed: 10, enemies: Still());
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id, car = n.Train.VehicleAhead(rear);
        double end = n.Train.Frames[car].Shape.HalfLength;
        var hound = OnRoof(n, car, end - 2);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, end - 3.6, P);
        Assert.True(Strike(n, 1, hound));
        n.Run(H.Aboard.StruckSeconds);
        Assert.False(hound.Gone);
        Assert.Equal(rear, hound.Attached);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsideTheRearCarItLeavesOnlyByAnOpenDoor(bool open)
    {
        var n = new Night(4, speed: 10, enemies: Still());
        int car = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var shape = n.Train.Frames[car].Shape;
        var room = shape.Interior!.Value;
        n.Train.Vehicles[car].DoorsOpen = 0;
        foreach (var d in shape.DoorList)
            if (open)
                n.Train.Vehicles[car].DoorsOpen |= (byte)(1 << d.Index);
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, H.Health, car, new Double3(room.Centre.X, room.Min.Y, room.Max.Z - 2.5), 0, 0, 0.6, hound.Id, 0);
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(room.Centre.X, room.Min.Y, room.Max.Z - 4), Surface = Surface.Deck, Health = P.Health };
        Assert.True(Strike(n, 1, hound));
        n.Run(H.Aboard.StruckSeconds + 0.5);
        Assert.Equal(open, hound.Gone);
    }

    [Fact]
    public void WithTheFlagOffItBitesWhereItStandsAsBefore()
    {
        var n = new Night(4, speed: 10, enemies: Still(on: false));
        int car = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var hound = OnRoof(n, car, 1);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, -0.5, P);
        double health = n.Crew[1].Health;
        n.Run(H.BiteEverySeconds - 0.5);
        Assert.Equal(health, n.Crew[1].Health);
        n.Run(1);
        Assert.True(n.Crew[1].Health < health);
        Assert.NotEqual(HoundMode.Lunge, hound.Aboard);
    }
}
