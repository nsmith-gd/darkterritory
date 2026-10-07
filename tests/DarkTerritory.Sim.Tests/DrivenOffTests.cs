using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Driven off by their rules, killed by the crew together (GDD App. F.1; the director's clarification of 7 Oct 2026: "They
/// should be driven off by rules but they should also be able to be killed like in Lethal Company if the team coordinates
/// effectively"; ARCHITECTURE §8 note 288). For each of the five: following its rule drives it off, a lone player's blows
/// never kill it, and two or more crewmates striking together (with what its rule asks for) do, for the night.
/// </summary>
public class DrivenOffTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A bare host world and the context its enemies step in; the crew stood where the test puts them.</summary>
    sealed class Rig
    {
        public readonly World World;
        public readonly EnemyContext Ctx;
        readonly Dictionary<int, (PlayerState State, byte Voice)> _crew = [];

        public Rig(EnemyTuning? enemies = null, int cars = 5)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(10_000)])), 2_000);
            World = new World(train, Tuning.Combat);
            Ctx = new EnemyContext { Tuning = enemies ?? E, World = World };
        }

        public TrainOnLine Train => World.Train;

        /// <summary>On the ground, <paramref name="x"/> m off the origin (loose in the world).</summary>
        public void Ground(int id, double x, byte voice = 0) =>
            Set(id, new PlayerState { Parent = PlayerState.World, Position = new Double3(x, 0, 0), Surface = Surface.Ground, Health = P.Health }, voice);

        /// <summary>On a car's floor, <paramref name="z"/> m along it.</summary>
        public void InCar(int id, int car, double z, byte voice = 0)
        {
            var room = Train.Frames[car].Shape.Interior!.Value;
            Set(id, new PlayerState { Parent = car, Position = room.Centre with { Y = room.Min.Y, Z = z }, Surface = Surface.Deck, Health = P.Health }, voice);
        }

        public void Set(int id, PlayerState s, byte voice = 0)
        {
            _crew[id] = (s, voice);
            Refresh();
        }

        public void Leave(int id)
        {
            _crew.Remove(id);
            Refresh();
        }

        void Refresh()
        {
            Ctx.Crew.Clear();
            foreach (var (id, (s, voice)) in _crew.OrderBy(c => c.Key))
                Ctx.Crew.Add((new PlayerSnapshot((byte)id, s), new PlayerIntent { Voice = voice }));
        }

        /// <summary>Steps it (and nothing else) for this long.</summary>
        public void Run(Enemy e, double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate && !e.Gone; i++)
            {
                World.Tick++;
                e.Step(Ctx);
            }
        }

        /// <summary>Blows by these crewmates in turn, a swing's recovery apart, stepping it between: how many until it's gone.</summary>
        public int Blows(Enemy e, int max, params int[] by)
        {
            int ticks = (int)Math.Ceiling(E.Melee.SwingSeconds * SimConstants.TickRate / by.Length);
            for (int n = 0; n < max; n++)
            {
                e.Struck(Ctx, by[n % by.Length], E.Melee.Damage);
                if (e.Gone)
                    return n + 1;
                for (int i = 0; i < ticks && !e.Gone; i++)
                {
                    World.Tick++;
                    e.Step(Ctx);
                }
            }
            return int.MaxValue;
        }
    }

    // ---- the Grumbler: "gang up or leave it alone".

    [Fact]
    public void ALonePlayerCanNeverWearTheGrumblerDown()
    {
        var r = new Rig();
        r.Ground(1, 1.2);
        var g = r.World.AddEnemy(id => Grumbler.OnCrates(id, new Double3(0, 0, 0), -1, E.Grumbler));
        Assert.Equal(int.MaxValue, r.Blows(g, 40, 1));
        Assert.False(g.Gone);
        Assert.Equal(E.Grumbler.Health, g.Health);
        Assert.True(g.Feral);
        // The old rule: the same blows hurt it (it regenerates against one, but they land).
        var old = new Rig(E with { Grumbler = E.Grumbler with { DrivenOff = false } });
        old.Ground(1, 1.2);
        var g2 = old.World.AddEnemy(id => Grumbler.OnCrates(id, new Double3(0, 0, 0), -1, E.Grumbler));
        g2.Struck(old.Ctx, 1, E.Melee.Damage);
        Assert.Equal(E.Grumbler.Health - E.Melee.Damage, g2.Health);
    }

    [Fact]
    public void GangedUpOnTheGrumblerBreaksOffAndComesBackCalmed()
    {
        var r = new Rig();
        r.Ground(1, 1.2);
        var g = r.World.AddEnemy(id => Grumbler.OnCrates(id, new Double3(0, 0, 0), -1, E.Grumbler));
        g.Struck(r.Ctx, 1, E.Melee.Damage);
        r.Run(g, 0.5);
        Assert.True(g.Feral);
        // A friend comes and stands with them: two of the crew on it and nobody's gang striking, it lets go and goes.
        var at = g.WorldPosition(r.Train);
        r.Ground(2, at.X + 1.0);
        r.Set(1, new PlayerState { Parent = PlayerState.World, Position = at + new Double3(-1.0, 0, 0), Surface = Surface.Ground, Health = P.Health });
        r.Run(g, E.Grumbler.OutnumberedSeconds + 0.1);
        Assert.Equal(SpinePhase.BreakOff, g.Phase);
        Assert.False(g.Feral);
        var from = g.WorldPosition(r.Train);
        r.Run(g, E.Grumbler.FleeSeconds * 0.5);
        Assert.True((g.WorldPosition(r.Train) - from).Length > 3, "it scuttles off");
        // A break, not the night: back to gnawing, calmed, and harmless till it's interrupted again.
        r.Run(g, E.Grumbler.FleeSeconds);
        Assert.False(g.Gone);
        Assert.Equal(SpinePhase.Telegraph, g.Phase);
        Assert.False(g.Feral);
        Assert.Empty(r.World.Slain);
    }

    [Fact]
    public void TwoStrikingTheGrumblerTogetherKillItForTheNight()
    {
        var r = new Rig();
        r.Ground(1, 1.2);
        r.Ground(2, -1.2);
        var g = r.World.AddEnemy(id => Grumbler.OnCrates(id, new Double3(0, 0, 0), -1, E.Grumbler));
        int blows = r.Blows(g, 40, 1, 2);
        Assert.True(g.Gone);
        // Every blow after the first counts (the first only starts the gang).
        Assert.InRange(blows, (int)Math.Ceiling(E.Grumbler.Health / E.Melee.Damage), (int)Math.Ceiling(E.Grumbler.Health / E.Melee.Damage) + 2);
        Assert.Contains(EnemyKind.Grumbler, r.World.Slain);
    }

    // ---- the Passenger: "make everyone speak".

    [Fact]
    public void ACrewmatesBlowFindsThePassengerOutAndItLetsGoAndRunsOffTheBack()
    {
        var r = new Rig();
        int car = Passenger.Car(r.Train)!.Value;
        var room = r.Train.Frames[car].Shape.Interior!.Value;
        r.InCar(1, car, room.Min.Z + 1);
        var p = r.World.AddEnemy(id => Passenger.Boards(id, r.Train, car, 2, E.Passenger));
        // Alone in its car: it goes to them and takes them.
        for (int i = 0; i < (E.Passenger.StalkSeconds + 30) * SimConstants.TickRate && p.Phase != SpinePhase.Grab; i++)
            r.Run(p, SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Grab, p.Phase);
        Assert.Equal(1, p.Holding);
        // The victim can't break free alone (A.8): their own blows are nothing to it.
        p.Struck(r.Ctx, 1, E.Melee.Damage);
        Assert.Equal(SpinePhase.Grab, p.Phase);
        // One blow from a friend and it's found out: it lets go and bolts for the back, and off.
        r.InCar(2, p.Attached, p.Local.Z + 1);
        p.Struck(r.Ctx, 2, E.Melee.Damage);
        Assert.Equal(SpinePhase.BreakOff, p.Phase);
        Assert.Equal(-1, p.Holding);
        Assert.Equal(E.Passenger.Health, p.Health);
        Assert.Contains(EnemyKind.Passenger, r.World.DrivenOff);
        r.Run(p, 30);
        Assert.True(p.Gone);
        Assert.Empty(r.World.Slain);
    }

    [Fact]
    public void RunDownTogetherThePassengerIsKilledForTheNight()
    {
        var r = new Rig();
        int car = Passenger.Car(r.Train)!.Value;
        var p = r.World.AddEnemy(id => Passenger.Boards(id, r.Train, car, 2, E.Passenger));
        // A lone friend chasing it lands blows, but they only keep it running.
        var lone = new Rig();
        var q = lone.World.AddEnemy(id => Passenger.Boards(id, lone.Train, car, 2, E.Passenger));
        for (int i = 0; i < 10; i++)
            q.Struck(lone.Ctx, 2, E.Melee.Damage);
        Assert.Equal(E.Passenger.Health, q.Health);
        Assert.False(q.Gone);
        // Two together: dead, for the night, and not counted as driven off.
        Assert.True(r.Blows(p, 20, 2, 3) <= (int)Math.Ceiling(E.Passenger.Health / E.Melee.Damage) + 1);
        Assert.True(p.Gone);
        Assert.Contains(EnemyKind.Passenger, r.World.Slain);
        Assert.DoesNotContain(EnemyKind.Passenger, r.World.DrivenOff);
    }

    // ---- the Gaunt: "keep talking to it".

    static Gaunt Angry(Rig r, int anger, SpinePhase phase)
    {
        var g = r.World.AddEnemy(id => Gaunt.Asleep(id, new Double3(0, 0, 1.2), E.Gaunt));
        g.Restore(phase, 5, E.Gaunt.Health, Enemy.Loose, new Double3(0, 0, 1.2), 0, 0, 0, extra: 1, extra2: anger);
        return g;
    }

    [Fact]
    public void TalkedToTheGauntCalmsAndInTheEndLeavesEmptyHanded()
    {
        var r = new Rig();
        r.Ground(1, 0, voice: 120);
        var g = Angry(r, E.Gaunt.AttackAt, SpinePhase.Commit);
        // Talk brings it down a step: under its threshold it stops attacking and follows again.
        r.Run(g, E.Gaunt.SilenceSeconds + 0.1);
        Assert.Equal(E.Gaunt.AttackAt - 1, g.Anger);
        Assert.Equal(SpinePhase.Telegraph, g.Phase);
        // Kept talking to, calm, it loses interest and goes (carrying nothing), and it may wake again somewhere tonight.
        r.Run(g, E.Gaunt.SilenceSeconds * E.Gaunt.AttackAt + E.Gaunt.TalkedDownSeconds + 1);
        Assert.Equal(SpinePhase.BreakOff, g.Phase);
        Assert.Equal(-1, g.Extra);
        Assert.Contains(EnemyKind.Gaunt, r.World.DrivenOff);
        // Silence instead and it climbs back, as ever.
        var s = new Rig();
        s.Ground(1, 0);
        var h = Angry(s, 0, SpinePhase.Telegraph);
        s.Run(h, E.Gaunt.SilenceSeconds * E.Gaunt.AttackAt + 1);
        Assert.Equal(SpinePhase.Commit, h.Phase);
    }

    [Fact]
    public void TheGauntDiesOnlyToTheGangWhileSomeoneKeepsTalkingToIt()
    {
        // Two swinging in silence: nothing, and it only gets angrier.
        var r = new Rig();
        r.Ground(1, 0.8);
        r.Ground(2, -0.8);
        var g = Angry(r, 1, SpinePhase.Telegraph);
        Assert.Equal(int.MaxValue, r.Blows(g, 6, 1, 2));
        Assert.Equal(E.Gaunt.Health, g.Health);
        Assert.True(g.Anger > 1);
        // One swinging while talking: nothing either.
        var lone = new Rig();
        lone.Ground(1, 0.8, voice: 120);
        var h = Angry(lone, 0, SpinePhase.Telegraph);
        Assert.Equal(int.MaxValue, lone.Blows(h, 20, 1));
        Assert.Equal(E.Gaunt.Health, h.Health);
        // Two together, one of them talking to it: dead, for the night.
        var t = new Rig();
        t.Ground(1, 0.8, voice: 120);
        t.Ground(2, -0.8);
        var k = Angry(t, 0, SpinePhase.Telegraph);
        Assert.True(t.Blows(k, 30, 1, 2) <= (int)Math.Ceiling(E.Gaunt.Health / E.Melee.Damage) + 1);
        Assert.Contains(EnemyKind.Gaunt, t.World.Slain);
    }

    // ---- Climbers: "outnumber them at the gaps".

    static Climber Inside(Rig r, int car)
    {
        var room = r.Train.Frames[car].Shape.Interior!.Value;
        var c = r.World.AddEnemy(id => Climber.Pacing(id, r.Train, car, 1, E.Climbers));
        c.Restore(SpinePhase.Commit, 5, E.Climbers.Health, car, room.Centre with { Y = room.Min.Y }, 0, 0, 0, extra: -1, extra2: 1);
        return c;
    }

    [Fact]
    public void OutnumberedInItsCarAClimberDropsBackOffForAnotherGap()
    {
        var r = new Rig();
        var c = Inside(r, 2);
        var room = r.Train.Frames[2].Shape.Interior!.Value;
        r.InCar(1, 2, room.Centre.Z - 4);
        r.InCar(2, 2, room.Centre.Z + 4);
        r.Run(c, E.Climbers.OutnumberedSeconds + 0.2);
        // Off the train, pacing for a gap it hasn't tried: a break, not killed.
        Assert.False(c.Gone);
        Assert.Equal(SpinePhase.Dormant, c.Phase);
        Assert.Equal(-1, c.Attached);
        Assert.Equal(E.Climbers.Health, c.Health);
        // Alone with one of the crew it isn't outnumbered: it stays (and it'll take them).
        var lone = new Rig();
        var d = Inside(lone, 2);
        lone.InCar(1, 2, room.Centre.Z - 4);
        lone.Run(d, E.Climbers.OutnumberedSeconds + 1);
        Assert.Equal(2, d.Attached);
    }

    [Fact]
    public void AClimberIsClubbedToDeathOnlyByTheCrewTogetherAndTheRestStillCome()
    {
        var r = new Rig();
        var c = Inside(r, 2);
        var room = r.Train.Frames[2].Shape.Interior!.Value;
        r.InCar(1, 2, room.Centre.Z - 1);
        // One alone: its blows never hurt it.
        for (int i = 0; i < 10; i++)
            c.Struck(r.Ctx, 1, E.Melee.Damage);
        Assert.Equal(E.Climbers.Health, c.Health);
        // Two together: dead. Only that one: the director still sends Climbers.
        r.InCar(2, 2, room.Centre.Z + 1);
        Assert.True(r.Blows(c, 10, 1, 2) <= (int)Math.Ceiling(E.Climbers.Health / E.Melee.Damage) + 1);
        Assert.True(c.Gone);
        Assert.DoesNotContain(EnemyKind.Climber, r.World.Slain);
    }

    // ---- the Choir: "hush, and shut every door".

    static ChoirGhost Seizing(Rig r, int victim)
    {
        var g = r.World.AddEnemy(id => ChoirGhost.Around(id, default, E.Choir));
        g.Restore(SpinePhase.Grab, 0, E.Choir.Health, Enemy.Loose, default, 0, 0, 0, extra: victim, extra2: 0, holding: victim, grabWindow: E.Choir.SeizeSeconds);
        return g;
    }

    [Fact]
    public void TheCrewHushingBreaksAChoirGhostsSeize()
    {
        var r = new Rig();
        r.Set(1, PlayerMotor.SpawnOnRoof(r.Train, 2, 0, P));
        var g = Seizing(r, 1);
        r.World.Choir.Loudness = 0;
        r.Run(g, E.Choir.HushBreakSeconds + 0.1);
        Assert.True(g.Gone);
        Assert.DoesNotContain(r.Ctx.Damage, d => d.Lethal);
        // Kept loud, it holds on to the end of its window and takes them.
        var loud = new Rig();
        loud.Set(1, PlayerMotor.SpawnOnRoof(loud.Train, 2, 0, P));
        var h = Seizing(loud, 1);
        loud.World.Choir.Loudness = Tuning.Combat.Choir.Threshold * 2;
        loud.Run(h, E.Choir.SeizeSeconds + 0.5);
        Assert.Contains(loud.Ctx.Damage, d => d.Lethal && d.PlayerId == 1);
    }

    [Fact]
    public void ADoorShutOnAChoirGhostsCatchBreaksItsSeize()
    {
        var r = new Rig();
        // Seized standing in a car with a door open; a friend shuts it.
        r.InCar(1, 2, 0);
        var v = r.Train.Vehicles[2];
        var door = r.Train.Frames[2].Shape.DoorList.First(d => d.Index != CarShape.HatchBit);
        v.ToggleDoor(door.Index);
        var g = Seizing(r, 1);
        r.World.Choir.Loudness = Tuning.Combat.Choir.Threshold * 2;
        r.Run(g, 1);
        Assert.Equal(SpinePhase.Grab, g.Phase);
        v.ToggleDoor(door.Index);
        r.Run(g, 0.1);
        Assert.Equal(SpinePhase.Telegraph, g.Phase);
        Assert.Equal(-1, g.Holding);
    }

    [Fact]
    public void AChoirGhostDiesOnlyToTheGangHoldingQuietAndBlowsDontBreakItsSeize()
    {
        var loud = new Rig();
        loud.Set(1, PlayerMotor.SpawnOnRoof(loud.Train, 2, 0, P));
        var g = Seizing(loud, 1);
        loud.World.Choir.Loudness = Tuning.Combat.Choir.Threshold * 2;
        // Loud, two swinging do it no harm, and it keeps its hold (the old rule's way out is gone).
        for (int i = 0; i < 6; i++)
            g.Struck(loud.Ctx, 2 + i % 2, E.Melee.Damage);
        Assert.Equal(E.Choir.Health, g.Health);
        Assert.Equal(SpinePhase.Grab, g.Phase);
        // Still it hits back.
        Assert.Contains(loud.Ctx.Damage, d => d.Amount == E.Choir.HitBackDamage);
        // Quiet, the gang kills it.
        var quiet = new Rig();
        var h = quiet.World.AddEnemy(id => ChoirGhost.Around(id, default, E.Choir));
        quiet.World.Choir.Loudness = 0;
        for (int i = 0; i < 20 && !h.Gone; i++)
        {
            h.Struck(quiet.Ctx, 2 + i % 2, E.Melee.Damage);
            quiet.World.Tick += 12;
        }
        Assert.True(h.Gone);
    }
}
