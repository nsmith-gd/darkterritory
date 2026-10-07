using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 App. A.7 and C.7 (ARCHITECTURE §8 note 175): the loudness meter keeps a per-player share during the Choir's
/// BUILD (voice, the rounds they fired, the whistle they pulled, a noisy toy in their hands; machinery and the Whistler's
/// whistle are nobody's), the swarm takes the loudest exposed first, and the incident report says who was loudest.
/// </summary>
public class ChoirShareTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly ChoirTuning T = Tuning.Combat.Choir;

    /// <summary>Gathering, well short of here: the shares count.</summary>
    static void Building(Night n) => n.World.Choir = new ChoirState { Build = 0.5, Loudness = T.MaxLoudness };

    static PlayerIntent Talking(byte voice) => new() { Voice = voice };

    [Fact]
    public void EachVoiceIsCreditedToWhoeverSpokeAndOnlyDuringTheBuild()
    {
        var n = new Night(6, speed: 10);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        // Not gathering: nobody's to blame for anything.
        n.Run(1, id => Talking(id == 2 ? (byte)255 : (byte)0));
        Assert.Equal(-1, n.World.ChoirLoudest);
        Building(n);
        n.Run(2, id => Talking(id == 2 ? (byte)255 : (byte)60));
        Assert.Equal(2, n.World.ChoirLoudest);
        // Two seconds at full voice: about 2 × voicePerPlayer loudness-seconds; the quieter about a quarter of that.
        Assert.InRange(n.World.ChoirShare(2), 1.9 * T.VoicePerPlayer, 2.1 * T.VoicePerPlayer);
        Assert.InRange(n.World.ChoirShare(1) / n.World.ChoirShare(2), 0.2, 0.3);
        // Quiet drains what's gathered; back to nothing, the build's shares go with it.
        n.World.Choir = new ChoirState { Build = 0.001 };
        n.Run(1);
        Assert.Equal(ChoirPhase.Distant, n.World.Choir.Phase(T));
        Assert.Empty(n.World.ChoirShares);
    }

    [Fact]
    public void TheCordIsWhoeverPulledItAndTheWhistlersIsNobodys()
    {
        var n = new Night(6, speed: 10);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        Building(n);
        n.Run(1, _ => new PlayerIntent { Actions = PlayerActions.Whistle });
        Assert.InRange(n.World.ChoirShare(1), 0.9 * T.WhistleLoudness, 1.1 * T.WhistleLoudness);
        double before = n.World.ChoirShare(1);
        n.World.Whistled(2.0);
        Assert.Equal(-1, n.World.WhistleBy);
        n.Run(1.5);
        Assert.Equal(before, n.World.ChoirShare(1));
        Assert.Single(n.World.ChoirShares);
    }

    [Fact]
    public void ANoisyToyIsLoudInTheHandsThatCarryIt()
    {
        var n = new Night(6, speed: 10);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        var room = n.Train.Frames[3].Shape.Interior!.Value;
        var drummer = n.World.Bodies.SpawnCrate(n.Train, 3, new Ballast.Double3(0, room.Min.Y + 0.1, 0), BodyKind.Toy);
        drummer.Noise = ToyNoise.Drummer;
        var quiet = n.World.Bodies.SpawnCrate(n.Train, 3, new Ballast.Double3(0, room.Min.Y + 0.1, 1), BodyKind.Toy);
        Building(n);
        // Lying in the car it's nobody's; picked up, it's the carrier's. A quiet toy carried is just a toy.
        n.Run(1);
        Assert.Empty(n.World.ChoirShares);
        drummer.Carrier = 2;
        quiet.Carrier = 1;
        n.Run(2);
        Assert.InRange(n.World.ChoirShare(2), 1.9 * T.Toys.Drummer, 2.1 * T.Toys.Drummer);
        Assert.Equal(0, n.World.ChoirShare(1));
        // Worth more than a quiet one to whatever ranks loot (App. C item 4).
        Assert.True(Bodies.Value(drummer) > Bodies.Value(quiet));
    }

    [Fact]
    public void TheGunnerOwnsTheRoundsTheyFire()
    {
        var n = new Night(6, speed: 10);
        int guard = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = n.Train.Frames[guard].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(n.Train, guard, mount.Position.Z - 0.7, P);
        n.Crew[1] = gunner with { Yaw = Math.PI, Pitch = 0.3, Flags = gunner.Flags | PlayerFlags.Seated };
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        Building(n);
        n.Run(1, id => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Fire } : default);
        Assert.Single(n.Shots);
        Assert.InRange(n.World.ChoirShare(1), T.RoundLoudness * T.WindowSeconds, T.RoundLoudness * T.WindowSeconds + 0.01);
        Assert.Equal(1, n.World.ChoirLoudest);
    }

    [Fact]
    public void TheSwarmTakesTheLoudestExposedFirstNotTheNearest()
    {
        // A.7: "whoever put the most into the meter during the build. The one shouting at everyone to shut up is usually the
        // one it takes." The quiet one stands right where the swarm wheels; the loud one is at the far end of the train.
        // (Who it takes, under the old rule: the two of them aren't over the meter's threshold, so note 288's hush would let go.)
        var n = new Night(6, speed: 10, enemies: E with { Choir = E.Choir with { DrivenOff = false } });
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 6, 4, P);
        Building(n);
        n.Run(3, id => Talking(id == 2 ? (byte)255 : (byte)20));
        n.World.Choir = n.World.Choir with { Build = 0.9999 };
        n.Run(E.Choir.SeizeSeconds + 20, id => Talking(id == 2 ? (byte)255 : (byte)20));
        Assert.Equal(DeathCause.Seized, n.Crew[2].Death);
        Assert.True(n.Crew[1].Alive, $"{n.Crew[1].Death} {string.Join(",", n.Events.Where(e => e.Kind == EnemyKind.Choir && e.To is SpinePhase.Grab or SpinePhase.Punish).Select(e => $"{e.EnemyId}:{e.To}@{e.Tick}"))}");
        Assert.True(n.World.Choir.Spent);
        n.AssertFair();
    }

    static readonly EnemyTuning E = Tuning.Enemies;

    [Fact]
    public void TheReportSaysWhoWasLoudest()
    {
        var n = new Night(6, speed: 10);
        n.Crew[0] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        n.World.Names[0] = "Dave";
        n.World.Names[1] = "Priya";
        Building(n);
        n.Run(2, id => Talking(id == 1 ? (byte)255 : (byte)85));
        string Line(int victim)
        {
            var s = n.Crew[victim] with { Death = DeathCause.Seized, Health = 0 };
            var i = IncidentLog.Death(n.World, victim, s, null, n.Crew.Select(c => (c.Key, c.Value)));
            Assert.Equal("Taken by the Choir", i.What);
            return i.Action.Replace("{actor}", IncidentLog.NameOf(n.World, i.Actor));
        }
        Assert.Equal("25% of the noise. Loudest on the line: Priya.", Line(0));
        Assert.Equal("Loudest on the line: Priya, 75% of the noise.", Line(1));
    }

    [Fact]
    public void ToysAreFoundAtTheStopsWithTheirNoisesAndTheWireCarriesThem()
    {
        // Note 267 (the director's notes on build 1121: the guard van's toys "are things that we should definitely find out in
        // the world"): none ride from the fortress; the stops' village containers have them (loot.json "toys").
        var n = new Night(6, speed: 0);
        n.World.EnableBodies();
        n.World.Stock();
        Assert.Equal(0, Tuning.Train.Kit.Toys);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Toy);

        var loot = Ballast.DataFile.Load<Stops.LootTuning>(System.IO.Path.Combine(Ballast.DataFile.FindContentRoot(), Stops.LootTuning.File));
        Assert.NotNull(loot.Toys);
        var noises = new List<ToyNoise>();
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var route = Route.RouteGenerator.Generate(Tuning.Route, Route.RouteTier.Frontier, seed);
            foreach (var (f, i) in route.Features.Select((f, i) => (f, i)).Where(x => x.f.Stop is not null))
            {
                var toys = Stops.StopLoot.Toys(loot, f.Stop!, route.Seed, i);
                // In the village's cupboards, cellars, haylofts and under floors only; the same from the seed every time.
                Assert.All(toys, t => Assert.Equal(Stops.StopZone.Village, f.Stop!.Containers.Single(c => c.Index == t.Container).Zone));
                Assert.Equal(toys, Stops.StopLoot.Toys(loot, f.Stop!, route.Seed, i));
                noises.AddRange(toys.Select(t => t.Noise));
            }
        }
        // Enough of them for the Track Doll, and App. C item 4's mix: quiet ones and noisy ones.
        Assert.True(noises.Count >= 6, $"{noises.Count} toys on six frontier nights");
        Assert.Contains(ToyNoise.None, noises);
        Assert.Contains(noises, x => x != ToyNoise.None);

        // A found toy's noise rides the wire.
        var drummer = n.World.Bodies.SpawnItem(n.Train.Frames[0].ToWorld(new Ballast.Double3(0, 0, -20)), n.Train.Dynamics.Distance, BodyKind.Toy);
        drummer.Noise = ToyNoise.Drummer;
        var mirrored = new World(n.Train);
        var controls = new TrainControls();
        Net.WorldRecords.Apply(Net.WorldRecords.Capture(n.World, controls, []), mirrored, ref controls, []);
        Assert.Equal(ToyNoise.Drummer, mirrored.Bodies.All.Single(b => b.Kind == BodyKind.Toy).Noise);
    }
}
