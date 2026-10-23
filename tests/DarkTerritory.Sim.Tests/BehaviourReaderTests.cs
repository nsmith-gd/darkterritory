using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The behaviour reader (queue #703, note 999 §2b; the director's playtest of 23 Oct, line 36): each behaviour the director
/// foils read from what the Sim already has, once a director second, on its own threshold; the aim drawn to it and its foils
/// weighed up only with <c>target.behaviours.on</c>; read the same on a staged night.
/// </summary>
public class BehaviourReaderTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly TargetTuning T = E.Director.Encounters.Target;
    static readonly BehavioursTuning B = T.Behaviours;

    static CrewSeen At(int id, Doing doing, double nearest = 2, Post post = Post.Ground, bool cellar = false, bool lazing = false, bool marked = false) =>
        new(id, post, doing, nearest, cellar, lazing, marked);

    /// <summary><paramref name="seconds"/> director seconds of <paramref name="crew"/>, each crewmate's voice <paramref name="voice"/> (by id) every tick of them.</summary>
    static Behaviours Run(CrewSeen[] crew, double seconds, Func<int, byte>? voice = null)
    {
        var b = new Behaviours();
        for (int s = 0; s < seconds; s++)
        {
            foreach (var c in crew)
                b.Hear(c.Player, voice?.Invoke(c.Player) ?? 0);
            b.Second(crew, counting: true, B);
        }
        return b;
    }

    [Fact]
    public void TheTablesNameRealKindsAndEveryBehaviour()
    {
        var kinds = Enum.GetValues<EnemyKind>().Select(Director.Key).ToHashSet();
        foreach (var f in Enum.GetValues<Behaviour>().Where(f => f != Behaviour.None))
        {
            string name = Behaviours.Name(f);
            Assert.True(B.Pull.ContainsKey(name), $"pull has no {name}");
            Assert.True(B.Foils.ContainsKey(name), $"foils has no {name}");
            foreach (var k in B.Foils[name])
                Assert.True(kinds.Contains(k), $"foils.{name}: {k} isn't a kind");
        }
        Assert.True(B.On, "measured in note 999 §2b: 14 of 20 bands, as with targeting alone");
    }

    [Fact]
    public void LootingAloneIsASearchWithNobodyNearHeldItsSeconds()
    {
        var alone = Run([At(1, Doing.Looting, nearest: B.AloneM + 1), At(2, Doing.Afield, nearest: B.AloneM + 1)], B.LootAloneSeconds);
        Assert.True(alone.Of(1).HasFlag(Behaviour.LootingAlone));
        Assert.False(Run([At(1, Doing.Looting, nearest: B.AloneM + 1)], B.LootAloneSeconds - 1).Of(1).HasFlag(Behaviour.LootingAlone));
        Assert.False(Run([At(1, Doing.Looting, nearest: B.AloneM - 1)], B.LootAloneSeconds * 3).Of(1).HasFlag(Behaviour.LootingAlone));
    }

    [Fact]
    public void ACellarAloneIsReadAtOnceAndNotWithAFriendAtHand()
    {
        Assert.True(Run([At(1, Doing.Looting, nearest: B.AloneM + 1, cellar: true)], 1).Of(1).HasFlag(Behaviour.CellarAlone));
        Assert.False(Run([At(1, Doing.Looting, nearest: 3, cellar: true)], 1).Of(1).HasFlag(Behaviour.CellarAlone));
    }

    [Fact]
    public void StragglingIsAfootFarFromEveryoneAndNeverAboard()
    {
        Assert.True(Run([At(1, Doing.Afield, nearest: B.StraggleM + 1)], 1).Of(1).HasFlag(Behaviour.Straggling));
        Assert.False(Run([At(1, Doing.Riding, nearest: B.StraggleM + 1, post: Post.Rider)], 1).Of(1).HasFlag(Behaviour.Straggling));
        Assert.False(Run([At(1, Doing.Afield, nearest: B.StraggleM - 1)], 1).Of(1).HasFlag(Behaviour.Straggling));
    }

    [Fact]
    public void TheGunAloneTakesItsSecondsOnTheGun()
    {
        CrewSeen gunner = At(1, Doing.Gunning, nearest: B.AloneM + 1, post: Post.Gunner);
        Assert.False(Run([gunner], B.GunAloneSeconds - 1).Of(1).HasFlag(Behaviour.GunAlone));
        Assert.True(Run([gunner], B.GunAloneSeconds).Of(1).HasFlag(Behaviour.GunAlone));
        Assert.False(Run([gunner with { Nearest = 2 }], B.GunAloneSeconds * 2).Of(1).HasFlag(Behaviour.GunAlone));
    }

    [Fact]
    public void LazingIsTheDeckChairOrTheBandHeldItsSeconds()
    {
        CrewSeen lounger = At(1, Doing.Walking, post: Post.Walker, lazing: true);
        Assert.False(Run([lounger], B.LazeSeconds - 1).Of(1).HasFlag(Behaviour.Lazing));
        Assert.True(Run([lounger], B.LazeSeconds).Of(1).HasFlag(Behaviour.Lazing));
    }

    [Fact]
    public void SilentAndChatterAreTheMicsShareWithCompanyAfterItsWindow()
    {
        CrewSeen[] two = [At(1, Doing.Riding, post: Post.Rider), At(2, Doing.Riding, post: Post.Rider)];
        // 1 never speaks, 2 always does.
        var b = Run(two, B.TalkSeconds * 3, id => id == 1 ? (byte)0 : (byte)255);
        Assert.True(b.Of(1).HasFlag(Behaviour.Silent));
        Assert.True(b.Of(2).HasFlag(Behaviour.Chatter));
        // Before the window's done, neither.
        var early = Run(two, B.TalkSeconds - 1, id => id == 1 ? (byte)0 : (byte)255);
        Assert.Equal(Behaviour.None, early.Of(1) & Behaviour.Silent);
        Assert.Equal(Behaviour.None, early.Of(2) & Behaviour.Chatter);
        // Alone in the night, there's nobody to talk to: neither.
        Assert.False(Run([two[0]], B.TalkSeconds * 3).Of(1).HasFlag(Behaviour.Silent));
        // Talking now and then (a quarter of the time) is neither.
        int tick = 0;
        var some = Run(two, B.TalkSeconds * 3, id => id == 1 && tick++ % 4 == 0 ? (byte)255 : (byte)0);
        Assert.Equal(Behaviour.None, some.Of(1) & (Behaviour.Silent | Behaviour.Chatter));
    }

    [Fact]
    public void GriefingIsTheMarkAndOneTrickIsMostOfTheNightAtOneJob()
    {
        Assert.True(Run([At(1, Doing.Afield, marked: true)], 1).Of(1).HasFlag(Behaviour.Griefing));
        CrewSeen gunner = At(1, Doing.Gunning, post: Post.Gunner);
        Assert.False(Run([gunner], B.OneTrickAfter - 1).Of(1).HasFlag(Behaviour.OneTrick));
        Assert.True(Run([gunner], B.OneTrickAfter).Of(1).HasFlag(Behaviour.OneTrick));
        // Riding all night is no job: not one trick.
        Assert.False(Run([At(1, Doing.Riding, post: Post.Rider)], B.OneTrickAfter * 2).Of(1).HasFlag(Behaviour.OneTrick));
    }

    [Fact]
    public void TheNightsTalliesCountOnlyOutOnTheRunAndForgetTheGone()
    {
        var b = new Behaviours();
        CrewSeen s = At(1, Doing.Afield, nearest: B.StraggleM + 1);
        b.Second([s], counting: false, B);
        Assert.Empty(b.Seconds);
        b.Second([s], counting: true, B);
        Assert.Equal(1, b.Seconds["straggling"]);
        Assert.Equal([1], b.Who["straggling"]);
        b.Second([], counting: true, B);
        Assert.Equal(Behaviour.None, b.Of(1));
    }

    [Fact]
    public void WithTheFoilsOnTheAimIsDrawnToWhatTheyShowAndItsFoilsAnswerIt()
    {
        var on = T with { On = true, Behaviours = B with { On = true } };
        var crew = new List<(int, Post, Doing, double, int)> { (1, Post.Ground, Doing.Afield, 0, 0), (2, Post.Ground, Doing.Afield, 0, 0) };
        Behaviour shows(int id) => id == 2 ? Behaviour.Straggling : Behaviour.None;
        // Alike but for 2's straggling: 2 is aimed at, with it.
        var aim = Targeting.Pick(crew, stopped: true, perPlayer: 1, on, shows);
        Assert.Equal(2, aim?.Player);
        Assert.Equal(Behaviour.Straggling, aim?.Shows);
        // Off, the lowest id as before, and nothing shown.
        var off = Targeting.Pick(crew, stopped: true, perPlayer: 1, T with { On = true, Behaviours = B with { On = false } }, shows);
        Assert.Equal(1, off?.Player);
        Assert.Equal(Behaviour.None, off?.Shows);
        // A foil that isn't in the doing's answers still answers the aim.
        var a = new Aim(2, Doing.Firing, 1, 0) { Shows = Behaviour.Straggling };
        string foil = B.Foils["straggling"][0];
        var kind = Enum.GetValues<EnemyKind>().First(k => Director.Key(k) == foil);
        Assert.False(Targeting.Answers(Doing.Firing, kind, on));
        Assert.True(Targeting.Answers(a, kind, on));
        Assert.False(Targeting.Answers(a, kind, T with { On = true, Behaviours = B with { On = false } }));
    }

    [Fact]
    public void OnAStagedNightTheReaderSeesTheStragglerAndNotTheCrewByTheTrain()
    {
        // (A deck chair's flag is the motor's, set only in a chair: lazing is read in the pure tests above.)
        var n = new GuardNight(E, null, 6_000, speed: 0, cars: 4);
        n.Aboard(2);
        n.Crew[3] = n.Ground(6_000 - 30, lateral: 50);
        n.Crew[4] = n.Ground(6_000 - 20, lateral: 4);
        n.Run(5);
        var b = n.Director.Behaviours;
        Assert.True(b.Of(3).HasFlag(Behaviour.Straggling), $"3 shows {b.Of(3)}");
        Assert.False(b.Of(4).HasFlag(Behaviour.Straggling), $"4 shows {b.Of(4)}");
        Assert.False(b.Of(1).HasFlag(Behaviour.Straggling));
    }
}
