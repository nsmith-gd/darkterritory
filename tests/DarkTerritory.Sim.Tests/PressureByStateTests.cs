using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Pressure by state and by player (queue #703, note 999; the director's playtest of 23 Oct, line 35): the train read as
/// running or stopped and how, λ by that state (a stand ramping with its seconds), each crewmate's doing, the one the director
/// aims at and why, and the kinds that answer their doing weighed up. Pure where it can be; deterministic on a night.
/// </summary>
public class PressureByStateTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly StanceTuning S = E.Director.Encounters.Stance;
    static readonly TargetTuning T = E.Director.Encounters.Target;
    static readonly StanceTuning SOn = S with { On = true };
    static readonly TargetTuning TOn = T with { On = true };

    [Fact]
    public void TargetingShipsOnAndTheStanceOffAsMeasured()
    {
        Assert.True(T.On, "note 999's sweep: 14 of 20 bands both ways, every green one kept");
        Assert.False(S.On, "with the stance on four crew left their band (note 999); the director's call, nathaniel2-rook-wilderness-stop");
    }

    [Fact]
    public void TheTablesNameRealKindsEveryDoingAndEveryStance()
    {
        var kinds = Enum.GetValues<EnemyKind>().Select(Director.Key).ToHashSet();
        foreach (var d in Enum.GetValues<Doing>())
        {
            string name = Targeting.Name(d);
            Assert.True(T.Answers.ContainsKey(name), $"answers has no {name}");
            Assert.True(T.Pull.ContainsKey(name), $"pull has no {name}");
            foreach (var k in T.Answers[name])
                Assert.True(kinds.Contains(k), $"answers.{name}: {k} isn't a kind");
        }
        foreach (var k in Enum.GetValues<StanceKind>())
            Assert.True(S.Rate.ContainsKey(Stance.Name(k)), $"stance.rate has no {Stance.Name(k)}");
    }

    [Fact]
    public void TheStanceIsReadFromTheTrainAndTheStop()
    {
        Assert.Equal(StanceKind.Wilderness, Stance.Of(0, 0, atFacility: false, stopKey: -1, S));
        Assert.Equal(StanceKind.AtStop, Stance.Of(0, 0, atFacility: false, stopKey: 2, S));
        Assert.Equal(StanceKind.AtStop, Stance.Of(0, 0, atFacility: true, stopKey: -1, S));
        Assert.Equal(StanceKind.Halt, Stance.Of(0, 0, atFacility: false, stopKey: 1004, S));
        Assert.Equal(StanceKind.ThroughFacility, Stance.Of(3, 3, atFacility: true, stopKey: -1, S));
        Assert.Equal(StanceKind.Slowing, Stance.Of(5, 6, atFacility: false, stopKey: -1, S));
        Assert.Equal(StanceKind.AtSpeed, Stance.Of(5, 5, atFacility: false, stopKey: -1, S));
        Assert.Equal(StanceKind.AtSpeed, Stance.Of(20, 25, atFacility: false, stopKey: -1, S));
        Assert.True(Stance.Stopped(StanceKind.Wilderness) && Stance.Stopped(StanceKind.Halt) && !Stance.Stopped(StanceKind.Slowing));
    }

    [Fact]
    public void AStandsRateClimbsWithItsSecondsAndOffItsOne()
    {
        foreach (var k in Enum.GetValues<StanceKind>())
            Assert.Equal(1, Stance.Rate(k, 600, S with { On = false }));
        double full = SOn.Rate["wilderness"];
        Assert.Equal(1, Stance.Rate(StanceKind.Wilderness, 0, SOn));
        Assert.Equal(1 + (full - 1) / 2, Stance.Rate(StanceKind.Wilderness, SOn.RampSeconds / 2, SOn), 9);
        Assert.Equal(full, Stance.Rate(StanceKind.Wilderness, SOn.RampSeconds * 10, SOn), 9);
        // Running, its own at once.
        Assert.Equal(SOn.Rate["slowing"], Stance.Rate(StanceKind.Slowing, 0, SOn), 9);
        for (double s = 0; s < 120; s += 1)
            Assert.True(Stance.Rate(StanceKind.AtStop, s + 1, SOn) >= Stance.Rate(StanceKind.AtStop, s, SOn));
    }

    [Fact]
    public void EachDoingIsReadFromWhatTheSimAlreadyHas()
    {
        Doing of(Post post, PlayerFlags flags = 0, double action = 0, double ground = 0, bool moving = true, bool house = false, bool shut = false, bool op = false) =>
            Targeting.Of(post, flags, action, ground, moving, house, shut, op, T);
        Assert.Equal(Doing.Firing, of(Post.Cab, PlayerFlags.Shovelful));
        Assert.Equal(Doing.Driving, of(Post.Cab));
        Assert.Equal(Doing.Riding, of(Post.Cab, moving: false));
        Assert.Equal(Doing.Gunning, of(Post.Gunner));
        Assert.Equal(Doing.Repairing, of(Post.Walker, action: 0.4));
        Assert.Equal(Doing.Operating, of(Post.Ground, op: true));
        Assert.Equal(Doing.Carrying, of(Post.Ground, PlayerFlags.Heavy));
        Assert.Equal(Doing.Looting, of(Post.Ground, house: true));
        Assert.Equal(Doing.Sprinting, of(Post.Ground, ground: 5.5));
        Assert.Equal(Doing.Afield, of(Post.Ground, ground: 2.4));
        Assert.Equal(Doing.Hiding, of(Post.Rider, shut: true));
        Assert.Equal(Doing.Riding, of(Post.Rider));
        Assert.Equal(Doing.Walking, of(Post.Walker));
    }

    [Fact]
    public void TheAimIsWhoDrawsItMostAndNeverSomeoneAlreadyTakenOrLeftBehind()
    {
        var crew = new List<(int, Post, Doing, double, int)>
        {
            (1, Post.Cab, Doing.Driving, 0, 0),
            (2, Post.Ground, Doing.Carrying, 0, 0),
            (3, Post.Ground, Doing.Carrying, 0, 0),
            (4, Post.LeftBehind, Doing.Looting, 500, 0),
        };
        // Stood: the carrier afoot, the lowest id of the two.
        var aim = Targeting.Pick(crew, stopped: true, perPlayer: 1, TOn);
        Assert.Equal(2, aim?.Player);
        Assert.Equal(Doing.Carrying, aim?.Doing);
        // One on 2 already: 3.
        crew[1] = (2, Post.Ground, Doing.Carrying, 0, 1);
        Assert.Equal(3, Targeting.Pick(crew, stopped: true, perPlayer: 1, TOn)?.Player);
        // The idle draw it: the driver with minutes of slack outranks the carrier.
        crew[0] = (1, Post.Cab, Doing.Driving, 600, 0);
        Assert.Equal(1, Targeting.Pick(crew, stopped: false, perPlayer: 1, TOn)?.Player);
        // Nobody to aim at.
        Assert.Null(Targeting.Pick([(4, Post.LeftBehind, Doing.Afield, 0, 0)], stopped: true, perPlayer: 1, TOn));
    }

    [Fact]
    public void TheAimWeighsItsAnswersUpOnlyWhichKindAndOffItsTheChoiceAsItWas()
    {
        var options = new List<(EnemyKind Kind, double Weight)> { (EnemyKind.Gannet, 1), (EnemyKind.Ribbit, 2), (EnemyKind.TippyToesie, 3) };
        var aim = new Aim(2, Doing.Carrying, 1, 0);
        var w = Targeting.Weigh(options, aim, TOn);
        Assert.Equal(1 * TOn.OffWeight, w[0].Weight, 9);
        Assert.Equal(2 * TOn.AnswerWeight, w[1].Weight, 9);
        Assert.Equal(3 * TOn.OffWeight, w[2].Weight, 9);
        Assert.Equal(options, Targeting.Weigh(options, aim, T with { On = false }));
        Assert.Equal(options, Targeting.Weigh(options, null, TOn));
        // Nothing in the list answers firing: left as it is, not all weighed down.
        Assert.Equal(options, Targeting.Weigh(options, aim with { Doing = Doing.Firing }, TOn));
    }

    static EnemyTuning With(bool stance, bool target) => E with
    {
        Director = E.Director with
        {
            Encounters = E.Director.Encounters with { Stance = S with { On = stance }, Target = T with { On = target } },
        },
    };

    [Fact]
    public void StoodInTheWildernessTheRateClimbsToItsFactorOverTheOneOff()
    {
        GuardNight night(bool on)
        {
            var n = new GuardNight(With(on, false), null, 6_000, speed: 0, cars: 4);
            n.Aboard(2);
            n.Run(SOn.RampSeconds + 10);
            return n;
        }
        var off = night(false);
        var on = night(true);
        Assert.Equal(StanceKind.Wilderness, on.Director.Stance);
        Assert.True(on.Director.StoodSeconds >= SOn.RampSeconds);
        Assert.Equal(off.Director.EncounterRate * SOn.Rate["wilderness"], on.Director.EncounterRate, 9);
    }

    [Fact]
    public void StoodWithCrewAfootTheDirectorAimsAtOneOfThemAndOffAtNobody()
    {
        GuardNight night(bool on)
        {
            var n = new GuardNight(With(false, on), null, 6_000, speed: 0, cars: 4);
            n.Aboard(1);
            n.Crew[2] = n.Ground(6_000 - 30, lateral: 5);
            n.Crew[3] = n.Ground(6_000 - 40, lateral: 5);
            n.Run(5);
            return n;
        }
        var aimed = night(true).Director.Aimed;
        Assert.NotNull(aimed);
        Assert.Equal(2, aimed.Value.Player);
        Assert.Equal(Doing.Afield, aimed.Value.Doing);
        Assert.Null(night(false).Director.Aimed);
    }

    [Fact]
    public void WithBothOnTheSameSeedGivesTheSameNight()
    {
        List<(uint, EnemyKind)> night()
        {
            var n = new GuardNight(With(true, true), null, 3_000, speed: 12, cars: 6, seed: 7);
            n.Aboard(4);
            n.Run(400);
            return [.. n.Director.Log.Select(l => (l.Tick, l.Kind))];
        }
        var a = night();
        Assert.NotEmpty(a);
        Assert.Equal(a, night());
    }
}
