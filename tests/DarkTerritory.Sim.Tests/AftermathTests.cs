using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Breathing room after a crisis, and on arriving at a facility (GDD App. B.1 Pacing rules; note 751; the director, 14 Oct
/// 2026: "The breathing room is good. Don't necessarily want a lot of it though"): after a death, a car lost, a rescue or a
/// Holdout freed, the director sends nothing of its own for a few seconds; and nothing for the first twenty seconds stood at
/// a facility.
/// </summary>
public class AftermathTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly DirectorTuning D = E.Director;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>
    /// A director always pressed to send something, so only a hold keeps it from it, with the aftermath as given; its roster only
    /// the Passenger, which none of these stops can have, so what it would send is "nothing fits", not a night of its own.
    /// </summary>
    static EnemyTuning Due(AftermathTuning? aftermath = null, double? lull = null) => HoundRunTests.Quiet with
    {
        Director = HoundRunTests.Quiet.Director with
        {
            Roster = ["passenger"],
            Pressure = D.Pressure with { Threshold = 1, PressAt = 1, Start = 18, Max = 18, ReliefPerCost = 0 },
            Aftermath = aftermath ?? D.Aftermath,
            FacilityLullSeconds = lull ?? D.FacilityLullSeconds,
        },
    };

    /// <summary>A stand-in that holds whoever it's given until a friend pulls them free, and does nothing else.</summary>
    sealed class Grip(int id) : Enemy(id)
    {
        public override EnemyKind Kind => EnemyKind.Dragger;
        public override PressureZone Zone => PressureZone.Flank;
        public override Sense Sense => Sense.Movement;
        public override bool PullsFree => true;
        protected override void Tick(EnemyContext ctx)
        {
        }
    }

    /// <summary>Two on the roof of car 3 side by side, a third in the cab; past the night's grace.</summary>
    static Night Crew(EnemyTuning enemies)
    {
        var n = new Night(6, 14, enemies: enemies);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 3, 1, P);
        n.Run(n.World.Director!.Grace + 2);
        return n;
    }

    /// <summary>Crewmate 2 held, then pulled free by crewmate 3 holding Use beside them.</summary>
    static void Rescue(Night n)
    {
        var grip = n.World.AddEnemy(id => new Grip(id));
        grip.Restore(SpinePhase.Grab, 0, 3, n.Crew[2].Parent, n.Crew[2].Position, 0, 0, 0, 0, 0, holding: 2, grabWindow: 60);
        n.Run(1.1);
        Assert.Equal(2, grip.Holding);
        Assert.Equal(0, n.World.Director!.AftermathLeft);
        n.Run(1.1, id => id == 3 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.Equal(-1, grip.Holding);
    }

    [Fact]
    public void AfterARescueNothingNewComesForTheAftermath()
    {
        Assert.True(D.Aftermath.On);
        Assert.InRange(D.Aftermath.Seconds, 10, 15);
        var n = Crew(Due());
        var d = n.World.Director!;
        Rescue(n);
        Assert.Equal("rescue", d.AftermathCause);
        int sent = d.Log.Count;
        // Pressed as it is (the pressure at its most), it holds the whole aftermath...
        for (int i = 0; i < D.Aftermath.Seconds - 2; i++)
        {
            n.Run(1);
            Assert.Equal("aftermath", d.HeldBecause);
            Assert.Equal(sent, d.Log.Count);
        }
        // ...and then it's itself again.
        n.Run(2);
        Assert.Equal(0, d.AftermathLeft);
        Assert.NotEqual("aftermath", d.HeldBecause);
    }

    [Fact]
    public void WithTheAftermathOffARescueHoldsNothing()
    {
        var n = Crew(Due(D.Aftermath with { On = false }));
        var d = n.World.Director!;
        Rescue(n);
        Assert.Null(d.AftermathCause);
        for (int i = 0; i < 4; i++)
        {
            n.Run(1);
            Assert.NotEqual("aftermath", d.HeldBecause);
        }
        // An event the tuning doesn't name holds nothing either.
        var only = Crew(Due(D.Aftermath with { Events = ["death"] }));
        Rescue(only);
        Assert.Null(only.World.Director!.AftermathCause);
    }

    [Fact]
    public void ADeathAndACarLostBeginItToo()
    {
        var n = Crew(Due());
        var d = n.World.Director!;
        n.Crew[2] = n.Crew[2] with { Health = 0, Death = DeathCause.Mauled };
        n.Run(1.1);
        Assert.Equal("death", d.AftermathCause);
        Assert.True(d.AftermathLeft > 0);
        n.Run(D.Aftermath.Seconds + 1);
        Assert.Equal(0, d.AftermathLeft);
        // The rear car parted at speed (a pin dropped): the crew are told, and that's a car lost.
        var rake = n.Train.Dynamics.Consist.Vehicles;
        n.Train.Uncouple(rake[^2].Id, Parting.Pin);
        n.Run(1.1);
        Assert.Equal("carLost", d.AftermathCause);
        Assert.True(d.AftermathLeft > 0);
    }

    [Fact]
    public void TheFacilityLullHoldsTheDirectorOnArrival()
    {
        Assert.Equal(20, D.FacilityLullSeconds);
        var stop = new FacilityTests.Stop(FacilityKind.ChemicalWorks);
        stop.World.EnableEnemies(Due(), null, 1, crew: 4, authority: true);
        var d = stop.World.Director!;
        Assert.Equal(RunPhase.AtFacility, stop.World.Run!.Phase);
        for (int i = 0; i < D.FacilityLullSeconds; i++)
        {
            Assert.Null(d.Decide(stop.World, 1_000 + i, [], 500));
            Assert.Equal("facility lull", d.HeldBecause);
        }
        d.Decide(stop.World, 1_000 + D.FacilityLullSeconds, [], 500);
        Assert.NotEqual("facility lull", d.HeldBecause);
        // At 0 there's none.
        var none = new FacilityTests.Stop(FacilityKind.ChemicalWorks);
        none.World.EnableEnemies(Due(lull: 0), null, 1, crew: 4, authority: true);
        none.World.Director!.Decide(none.World, 1_000, [], 500);
        Assert.NotEqual("facility lull", none.World.Director.HeldBecause);
    }
}
