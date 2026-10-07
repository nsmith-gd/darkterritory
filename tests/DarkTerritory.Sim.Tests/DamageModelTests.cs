using System.Reflection;
using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The damage model (the director's decision of 6 Oct 2026, GDD App. F.1; ARCHITECTURE §8 note 272): "Lethal Company style.
/// Health exists, but damage comes in a few big hits, never chip damage. Healing items are rare loot."
/// </summary>
public class DamageModelTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));

    /// <summary>Every creature's hit on a player in enemies.json: by convention an int field named ...Damage (note 272).</summary>
    static IEnumerable<(string Name, int Amount)> Hits()
    {
        foreach (var section in typeof(EnemyTuning).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (section.GetValue(E) is not { } tuning || section.PropertyType.Namespace != typeof(EnemyTuning).Namespace)
                continue;
            foreach (var field in section.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (field.PropertyType == typeof(int) && field.Name.EndsWith("Damage", StringComparison.Ordinal))
                    yield return ($"{section.Name}.{field.Name}", (int)field.GetValue(tuning)!);
        }
        // The Stoker's door (Stoker v3, note 271): a heavy burn, then death. Named for what it is, so listed here.
        yield return ("Stoker.DoorBurn", E.Stoker.DoorBurn);
        yield return ("Stoker.DoorKill", E.Stoker.DoorKill);
    }

    [Fact]
    public void NoCreatureHitIsChip()
    {
        // A future chip number (a 15-point nip every second and a half, as the Grumbler had) fails here. Zero is no hit at all
        // (the Choir's legacy swarm).
        var hits = Hits().ToList();
        foreach (string known in new[] { "CinderHounds.BiteDamage", "Grumbler.BiteDamage", "Climbers.BiteDamage", "Gaunt.HitDamage",
            "Choir.HitBackDamage", "Drift.Damage" })
            Assert.Contains(hits, h => h.Name == known);
        foreach (var (name, amount) in hits)
            Assert.True(amount == 0 || amount >= E.Damage.MinHit, $"{name} is {amount}: chip damage (under minHit {E.Damage.MinHit})");
        // Spec B.2: "Most attacks 35–60" of 100.
        Assert.Equal(100, P.Health);
        Assert.InRange(E.Damage.MinHit, 30, 35);
    }

    [Fact]
    public void ACreaturesHitsComeWithLongGaps()
    {
        // A few big hits, not a stream: time to get clear after the first.
        foreach (var (name, gap) in new[] { ("cinderHounds.biteEverySeconds", E.CinderHounds.BiteEverySeconds), ("grumbler.biteEvery", E.Grumbler.BiteEvery),
            ("climbers.biteEvery", E.Climbers.BiteEvery), ("gaunt.hitEvery", E.Gaunt.HitEvery), ("choir.hitBackEvery", E.Choir.HitBackEvery),
            ("drift.damageSeconds", E.Drift.DamageSeconds) })
            Assert.True(gap >= E.Damage.MinGapSeconds, $"{name} is {gap} s: under minGapSeconds {E.Damage.MinGapSeconds}");
    }

    [Fact]
    public void ACreatureOnYouIsTwoOrThreeHitsFromDeath()
    {
        // Bites can't kill (App. A.1: kills go through GRAB); each creature that grabs does so once it's beaten you down. Its
        // grab is the last hit: so two or three in all, and a clean escape after the first costs one.
        static int HitsToGrab(int damage, Func<int, bool> grabs)
        {
            int health = P.Health, hits = 0;
            while (!grabs(health) && hits < 10)
            {
                health = Math.Max(1, health - damage);
                hits++;
            }
            return hits;
        }
        Assert.InRange(HitsToGrab(E.CinderHounds.BiteDamage, h => h <= E.CinderHounds.BiteDamage), 1, 2);
        Assert.InRange(HitsToGrab(E.Grumbler.BiteDamage, h => h <= E.Grumbler.GrabBelowHealth), 1, 2);
        Assert.InRange(HitsToGrab(E.Gaunt.HitDamage, h => h <= E.Gaunt.GrabBelowHealth), 1, 2);
        // Nothing takes a whole crewmate in one hit outside its grab.
        // (The Stoker's second burn at its door kills outright: the mistake you learn from, note 271.)
        Assert.All(Hits().Where(h => h.Name is not ("CarFire.ExplodeDamage" or "Stoker.DoorKill")), h => Assert.True(h.Amount < P.Health, h.Name));
    }

    [Fact]
    public void AChoirGhostHitsBackOnceAGapHoweverFastItsStruck()
    {
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(10_000)])), 2_000));
        var ctx = new EnemyContext { Tuning = E, World = world };
        var ghost = ChoirGhost.Around(1, default, E.Choir with { Health = 100 });
        // Four blows in a second: one hit back.
        for (int i = 0; i < 4; i++)
        {
            ghost.Struck(ctx, 1, 1);
            world.Tick += (uint)(SimConstants.TickRate / 4);
        }
        Assert.Single(ctx.Damage);
        Assert.Equal(E.Choir.HitBackDamage, ctx.Damage[0].Amount);
        // The gap over, the next blow costs another.
        world.Tick += (uint)Math.Ceiling(E.Choir.HitBackEvery * SimConstants.TickRate);
        ghost.Struck(ctx, 1, 1);
        Assert.Equal(2, ctx.Damage.Count);
    }

    // ---- healing: rare loot, used from the hands.

    [Fact]
    public void HealingFindsAreRare()
    {
        // App. F.1: "Healing items are rare loot." About one a frontier night (they were six, a quarter of every find).
        foreach (var item in L.Healing!.Heals.Keys)
            Assert.True(L.Items.ContainsKey(item), $"{item} isn't a find");
        Assert.True(L.Healing.Heals["bandages"] < L.Healing.Heals["medicine"] && L.Healing.Heals["medicine"] < L.Healing.Heals["morphine"]);
        foreach (var (tier, most) in new[] { (RouteTier.Local, 2.5), (RouteTier.Frontier, 2.0), (RouteTier.DeepTerritory, 1.5) })
        {
            int heal = 0, all = 0;
            const int nights = 20;
            for (ulong seed = 1; seed <= nights; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                for (int i = 0; i < route.Features.Count; i++)
                    if (route.Features[i].Stop is { } stop)
                        foreach (var f in StopLoot.Village(L, stop, route.Seed, i, 700))
                        {
                            all++;
                            heal += L.Healing.Of(f.Item) > 0 ? 1 : 0;
                        }
            }
            Assert.True(heal > 0, $"{tier}: no healing at all in {nights} nights");
            Assert.True(heal / (double)nights <= most, $"{tier}: {heal / (double)nights:0.0} healing finds a night (of {all / (double)nights:0.0})");
            Assert.True(heal <= all / 8.0, $"{tier}: {heal} of {all} finds heal");
        }
    }

    /// <summary>A host night on a generated line with its stops' loot out, and a crewmate holding a find that heals.</summary>
    sealed class Night
    {
        public readonly World World;
        public readonly Run.Run Run;
        public PlayerState Me;
        public readonly Body Find;
        public readonly int Heals;

        public Night(int health = 40, bool heals = true)
        {
            for (ulong seed = 1; ; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
                var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), route.Build(), 3_000, Tuning.Boiler);
                var world = new World(train, Tuning.Combat);
                world.EnableBodies();
                world.EnableRun(Tuning.Run, route, route.GateOr(Tuning.Route.YardLength), authority: true, loot: L);
                for (int k = 0; k < world.Run!.Stops.Count; k++)
                    world.Run.Stock(world.Bodies, k);
                var find = world.Bodies.All.FirstOrDefault(b => b.Kind == BodyKind.Loot && (world.Run.HealOf(b) > 0) == heals);
                if (find is null)
                    continue;
                (World, Run, Find, Heals) = (world, world.Run, find, world.Run.HealOf(find));
                break;
            }
            // Out on the ballast beside the train, at nothing Use works (the find's in hand wherever it was found).
            var line = World.Train.Line.Sample(2_950);
            var at = line.Position + Double3.Cross(line.Tangent, Double3.Up).Normalized * 8;
            double hint = 2_950;
            Me = PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, World.Train.Line, ref hint) }, World.Train.Line, 2_950, P) with { Health = health };
            Find.Carrier = 1;
            Find.Claimed = true;
        }

        public void Act(PlayerIntent intent, double seconds)
        {
            for (int i = 0; i < Math.Round(seconds * SimConstants.TickRate); i++)
            {
                World.BeginTick();
                World.CrewAct(ref Me, intent, 1);
            }
        }

        public bool Holding => World.Bodies.All.Contains(Find) && Find.Carrier == 1;
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void AFindThatHealsIsUsedWithUseHeldAndIsGone()
    {
        var n = new Night();
        Assert.Contains(n.Heals, L.Healing!.Heals.Values);
        n.Act(Use, L.Healing.UseSeconds - 0.5);
        Assert.Equal(40, n.Me.Health);
        Assert.True(n.Find.MendTicks > 0, "how far it's got is on the body, for the HUD");
        n.Act(Use, 0.6);
        Assert.Equal(Math.Min(P.Health, 40 + n.Heals), n.Me.Health);
        Assert.DoesNotContain(n.Find, n.World.Bodies.All);
        // Still holding Use with empty hands does nothing more.
        n.Act(Use, 1);
        Assert.Equal(Math.Min(P.Health, 40 + n.Heals), n.Me.Health);
    }

    [Fact]
    public void HealingStopsAtFullHealth()
    {
        var n = new Night(health: 95);
        n.Act(Use, L.Healing!.UseSeconds + 0.1);
        Assert.Equal(P.Health, n.Me.Health);
    }

    [Fact]
    public void ATapStillPutsItDownAndWalkingDoesntUseIt()
    {
        var n = new Night();
        n.Act(new PlayerIntent { Buttons = PlayerButtons.Use, MoveZ = 1 }, L.Healing!.UseSeconds + 0.5);
        Assert.Equal(40, n.Me.Health);
        Assert.True(n.Holding);
        n.Act(default, 0.1);
        n.Act(Use, 0.1);
        n.Act(default, 0.1);
        Assert.False(n.Holding);
        Assert.Contains(n.Find, n.World.Bodies.All);
        Assert.Equal(40, n.Me.Health);
    }

    [Fact]
    public void WholeItsJustAFindAndOtherFindsDontHeal()
    {
        // At full health Use puts it down at once, as any find.
        var whole = new Night(health: P.Health);
        whole.Act(Use, 0.1);
        Assert.False(whole.Holding);
        Assert.Equal(P.Health, whole.Me.Health);
        // A pocket watch is no medicine.
        var watch = new Night(heals: false);
        watch.Act(Use, 0.1);
        Assert.False(watch.Holding);
        watch.Act(Use, L.Healing!.UseSeconds + 0.1);
        Assert.Equal(40, watch.Me.Health);
    }

    [Fact]
    public void AHurtBotUsesTheFindItCarries()
    {
        // Through the same intent a person sends: it stands still and holds Use.
        var n = new Night();
        var intent = Heed.Heal(new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, n.Me, n.World, 1);
        Assert.True(intent.Has(PlayerButtons.Use));
        Assert.Equal(0, intent.MoveZ);
        n.Act(intent, L.Healing!.UseSeconds + 0.1);
        Assert.Equal(Math.Min(P.Health, 40 + n.Heals), n.Me.Health);
        // Above botBelow, it keeps it.
        var fine = new Night(health: L.Healing.BotBelow + 10);
        var walking = new PlayerIntent { MoveZ = 1 };
        Assert.Equal(walking, Heed.Heal(walking, fine.Me, fine.World, 1));
    }
}
