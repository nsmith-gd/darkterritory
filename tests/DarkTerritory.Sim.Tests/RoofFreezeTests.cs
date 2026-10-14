using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 547 (queue #289; D1.3's nine-night report, #671: froze 3 → 10): crew freezing on the train's own roofs mid-night, read
/// as left behind. On frontier:7 seed 8 the gunner stood on car 3's roof in "warm:ToEnd" for 240 s and froze there.
/// </summary>
public class RoofFreezeTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly GunTuning G = Tuning.Combat.Guns;

    [Fact]
    public void AColdGunnerAtItsGunGoesInToWarmAndDoesntFreeze()
    {
        var n = new Night(4, 14);
        int van = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = Guns.Mount(n.Train, van)!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, van, mount.Position.Z - mount.Facing.Z * 0.8, P) with { Yaw = Math.PI };
        var gunner = new GunnerBot(G, cold: P.Cold) { Me = 1 };
        n.Run(1, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        Assert.Equal(van, Guns.MannedGun(n.Crew[1], n.Train, G));
        n.Crew[1] = n.Crew[1] with { Cold = P.Cold.OnsetSeconds * 0.95 };
        var steps = new List<string>();
        bool warmed = false;
        for (int i = 0; i < 90 * 4 && !warmed; i++)
        {
            n.Run(0.25, id => gunner.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            string w = $"{gunner.WarmUpStep ?? "-"}:{n.Crew[1].Surface}{n.Crew[1].Parent}{(n.Crew[1].Has(PlayerFlags.Seated) ? "S" : "")}";
            if (steps.Count == 0 || steps[^1] != w) steps.Add(w);
            warmed = PlayerMotor.Indoors(n.Crew[1], n.Train) && gunner.WarmUpStep is { } st && st.StartsWith("Warm");
        }
        Assert.True(warmed, $"never in to warm: {string.Join(" > ", steps.TakeLast(20))}");
        Assert.True(n.Crew[1].Alive, $"died of {n.Crew[1].Death}");
    }

    [Fact]
    public void WithTheBrakemanAboutAColdWalkerStillGoesInToWarm()
    {
        // frontier:7 seed 8: the Brakeman about and cars wound, the bot crew's heed (BotCrew.HeedBrakeman) drafted a cold
        // crewmate into his pincer or the unwind every tick, over its warm-up: it never turned for the door, and froze on the
        // roof. On its way in out of the cold, it isn't drafted (the warm crewmates are).
        var e = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
        var n = new Night(6, speed: 12, enemies: e);
        n.Train.Vehicles[1].Wound = true; // forward: the unwind's way, against the warm-up's (in by its own rear door)
        var last = n.Train.Cars[^1];
        n.World.AddEnemy(id => Enemies.Brakeman.Up(id, n.Train, last.FrontDistance - last.Length + 0.5, 1, e.Brakeman));
        // A crewmate on the roofs too: the two of them are the Brakeman's pincer (the lower id takes the front).
        var mate = PlayerMotor.SpawnOnRoof(n.Train, 5, 0, P);
        var bot = new RoofWalkerBot(5, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 3, 3, P) with { Cold = P.Cold.OnsetSeconds * 0.95 };
        var steps = new List<string>();
        bool warmed = false;
        for (int i = 0; i < 90 * SimConstants.TickRate && !warmed; i++)
        {
            n.Run(SimConstants.TickSeconds, id =>
            {
                var intent = bot.Decide(n.Crew[id], n.World, n.World.Tick, out _);
                return BotCrew.HeedBrakeman(bot, intent, n.Crew[id], n.World, id, [(2, mate)], null);
            });
            string w = $"{bot.WarmUpStep ?? "-"}:{n.Crew[1].Surface}{n.Crew[1].Parent}";
            if (steps.Count == 0 || steps[^1] != w) steps.Add(w);
            warmed = PlayerMotor.Indoors(n.Crew[1], n.Train) && bot.WarmUpStep is { } st && st.StartsWith("Warm");
        }
        Assert.True(warmed, $"never in to warm: {string.Join(" > ", steps.TakeLast(16))}");
        Assert.True(n.Crew[1].Alive, $"died of {n.Crew[1].Death}");
    }
}
