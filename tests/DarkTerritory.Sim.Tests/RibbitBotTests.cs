using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 658 (queue #388; found under note 652's roster): a Ribbit pack takes a crewmate alone (App. A.6), and a friend coming
/// up lets the tongue go; a bot that only came up to even the count went back to its work and the pack had the same hand
/// again three seconds later, sixty times at deepTerritory:7 seed 2's Foundry. Now the bots on the ground club the pack.
/// With every kind about (<see cref="EveryKind"/>), the director's grace never over: only the pack put there.
/// </summary>
public class RibbitBotTests
{
    static readonly EnemyTuning E = EveryKind.Enemies with
    {
        Director = EveryKind.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 },
    };
    static readonly PlayerTuning P = Tuning.Player;

    static Double3 Beside(Night n, int car, double lateral, double along = 0)
    {
        var at = n.Train.Frames[car].ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    [Fact]
    public void BotsOnTheGroundClubAPackAfterACrewmateAndItDoesntKeepTakingThem()
    {
        var n = new Night(4, speed: 0, enemies: E);
        // A hand alone beside car 2, a friend along by car 4 (out of the pack's count), and a pack of two coming from out past them.
        n.Crew[2] = PlayerMotor.SpawnOnGround(Beside(n, 2, 4), n.Train.Line, n.Train.Dynamics.Distance, P);
        n.Crew[3] = PlayerMotor.SpawnOnGround(Beside(n, 3, 4), n.Train.Line, n.Train.Dynamics.Distance, P);
        var pack = Enumerable.Range(0, 2).Select(i => n.World.AddEnemy(id => Ribbit.At(id, 7, Beside(n, 2, 14, i * 1.5), E.Ribbits))).ToList();
        int grabs = 0;
        bool held = false;
        for (int i = 0; i < 40 * SimConstants.TickRate && pack.Any(r => !r.Gone && r.Health > 0); i++)
        {
            n.Run(SimConstants.TickSeconds, id => id is 2 or 3 ? Heed.Ribbits(default, n.Crew[id], n.World, id) : default);
            bool now = pack.Any(r => r.Phase == SpinePhase.Grab);
            if (now && !held)
                grabs++;
            held = now;
        }
        Assert.All(pack, r => Assert.True(r.Gone || r.Health <= 0, $"a Ribbit's still at it: {r.Phase}, health {r.Health}; {grabs} grabs"));
        Assert.True(grabs <= 1, $"the pack had them {grabs} times");
        Assert.True(n.Crew[2].Alive && n.Crew[3].Alive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABotAloneOnItsWayWithAPackOfThreeAfterItRunsOnAndIsntTaken(bool run)
    {
        // Note 894: frontier:6 seed 4's shunter, alone at the pick-up's points and walking back to the train, turned on a pack
        // of three (note 658's clubbing) and was eaten. Outnumbered, it keeps going, at a run (App. A.6: "outrunnable").
        var e = E with { CrewBots = E.CrewBots with { RibbitsOutnumberedRun = run } };
        var n = new Night(4, speed: 0, enemies: e);
        n.Crew[2] = PlayerMotor.SpawnOnGround(Beside(n, 2, 4), n.Train.Line, n.Train.Dynamics.Distance, P);
        // Coming up behind it, 10 m back along the train (within the bots' ribbitWithin); it walks on, away from them.
        var pack = Enumerable.Range(0, 3).Select(i => n.World.AddEnemy(id => Ribbit.At(id, 7, Beside(n, 2, 3 + i * 1.5, 10), e.Ribbits))).ToList();
        // Its way on: straight away from them, turned to it as the bots' own walking turns (the yaw the look's laid on).
        var away = PlayerMotor.WorldPosition(n.Crew[2], n.Train) - pack[0].WorldPosition(n.Train);
        PlayerIntent Walk() => new() { MoveZ = 1, LookYaw = (float)Math.IEEERemainder(Math.Atan2(-away.X, -away.Z) - n.Crew[2].Yaw, 2 * Math.PI) };
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            n.Run(SimConstants.TickSeconds, id => id == 2 ? Heed.Ribbits(Walk(), n.Crew[2], n.World, 2, [(2, n.Crew[2])]) : default);
        bool taken = n.Events.Any(ev => ev.Kind == EnemyKind.Ribbit && ev.To == SpinePhase.Grab);
        Assert.True(taken != run, run ? "taken, though it ran" : "never taken, turning on a pack of three: the test's not the night's");
    }
}
