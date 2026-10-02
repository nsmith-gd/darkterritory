using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D, death, Holdouts and return: the queue (D.6), a Holdout's states (D.5), breaching (D.7), the freed player
/// (D.8), bodies as loot (D.9), and D.14's checks: no open-world spawns, queue integrity, release and reassign, dead
/// silence, no farming.
/// </summary>
public class HoldoutTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly HoldoutTuning H = Tuning.Holdouts;

    /// <summary>A night under way near a Holdout site, with a crew whose states the test holds.</summary>
    sealed class Night
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public readonly RouteFeature Site;
        public readonly List<(int Id, PlayerState State)> Crew = [];
        public readonly List<HoldoutEvent> Events = [];
        public PlayerIntent[] Intents = [];

        public Night(Func<RouteFeature, bool> pick, double engineFrom, RouteTier tier = RouteTier.Frontier)
        {
            for (ulong seed = 1; ; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                if (route.Features.FirstOrDefault(f => f.Stop is not null && pick(f)) is not { } site)
                    continue;
                Site = site;
                Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), route.Build(), site.Start + engineFrom, Tuning.Boiler);
                World = new World(Train, Tuning.Combat);
                World.EnableBodies();
                World.EnableRun(Tuning.Run, route, 600, authority: true);
                World.Run!.Resume(900, -1, Train.Boiler.Tender, 0);
                World.EnableHoldouts(H, route);
                return;
            }
        }

        public IEnumerable<Holdout> Here => World.Holdouts!.All.Where(h => h.Site == Site);

        /// <summary>A crew member: alive in the cab, or dead (died at <paramref name="diedAt"/> along the line).</summary>
        public int Add(bool alive, double diedAt = 100)
        {
            int id = Crew.Count;
            var s = alive ? PlayerMotor.SpawnInCab(Train, P) : new PlayerState
            {
                Parent = PlayerState.World,
                Death = DeathCause.Mauled,
                LineHint = diedAt,
                Position = Train.Line.Sample(diedAt).Position
            };
            Crew.Add((id, s));
            return id;
        }

        public PlayerState this[int id]
        {
            get => Crew[id].State;
            set => Crew[id] = (id, value);
        }

        public void Stand(int id, Double3 at) => this[id] = PlayerMotor.SpawnOnGround(at, Train.Line, Site.Start, P);

        public void Step(double seconds, double speed = 0)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                foreach (var rake in Train.Rakes)
                    rake.Velocity = speed;
                World.BeginTick();
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i].State;
                    World.CrewAct(ref s, i < Intents.Length ? Intents[i] : default, i);
                    Crew[i] = (i, s);
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.StepBodies(Crew);
                Events.AddRange(World.StepHoldouts(Crew, (id, s) => Crew[id] = (id, s)));
            }
        }

        public void Hold(int id, PlayerButtons b)
        {
            if (Intents.Length < Crew.Count)
                Array.Resize(ref Intents, Crew.Count);
            Intents[id] = new PlayerIntent { Buttons = b };
        }
    }

    static bool AHalt(RouteFeature f) => f.Kind == FeatureKind.Village && f.Stop!.Holdouts.Count == 1;

    [Fact]
    public void TheDeadWaitAtTheNextSiteAndComeBackInsideItWhenTheCrewBreaksThemOut()
    {
        var n = new Night(AHalt, engineFrom: -300);
        int living = n.Add(alive: true), dead = n.Add(alive: false);
        n.Step(0.2);
        var h = Assert.Single(n.Here);
        // D.5 assign: the consist inside the approach, someone in the queue; the lamp lights.
        Assert.Equal(HoldoutState.Occupied, h.State);
        Assert.Equal(dead, h.Occupant);
        Assert.True(h.Lit);
        Assert.Contains(n.Events, e => e.Kind == HoldoutEventKind.Assigned && e.PlayerId == dead);

        n.Stand(living, h.Door);
        n.Hold(living, PlayerButtons.Use);
        n.Step(h.Breach(H).Seconds + 0.2);
        Assert.Equal(HoldoutState.Freed, h.State);
        var back = n[dead];
        Assert.True(back.Alive);
        Assert.Equal(H.FreedHealth, back.Health);
        // D.14 "no open-world spawns": inside the Holdout, never out in the open.
        Assert.True((back.Position - h.Inside).Length < 0.01);
        Assert.DoesNotContain(n.World.Holdouts!.Queue, e => e.PlayerId == dead);
    }

    [Fact]
    public void ABreachThatStopsStartsOver()
    {
        var n = new Night(AHalt, engineFrom: -300);
        int living = n.Add(alive: true);
        n.Add(alive: false);
        n.Step(0.2);
        var h = n.Here.Single();
        n.Stand(living, h.Door);
        n.Hold(living, PlayerButtons.Use);
        n.Step(h.Breach(H).Seconds * 0.6);
        Assert.Equal(HoldoutState.Breaching, h.State);
        n.Hold(living, PlayerButtons.None);
        n.Step(0.1);
        Assert.Equal(HoldoutState.Occupied, h.State);
        Assert.Equal(0, h.Progress);
        n.Hold(living, PlayerButtons.Use);
        n.Step(h.Breach(H).Seconds * 0.6);
        Assert.NotEqual(HoldoutState.Freed, h.State);
    }

    [Fact]
    public void NobodyWaitsAtTheSiteWhereTheyDied()
    {
        // D.5 eligibility: skipped, keeping their place; the next in line takes it.
        var n = new Night(AHalt, engineFrom: -300);
        n.Add(alive: true);
        int diedHere = n.Add(alive: false, diedAt: n.Site.Start + 50);
        int diedEarlier = n.Add(alive: false, diedAt: 100);
        n.Step(0.2);
        Assert.Equal(diedEarlier, n.Here.Single().Occupant);
        Assert.Equal(diedHere, n.World.Holdouts!.Queue[0].PlayerId);
    }

    [Fact]
    public void ACrewThatLeavesReleasesThemAndCanComeBackForThem()
    {
        var n = new Night(AHalt, engineFrom: -300);
        n.Add(alive: true);
        int dead = n.Add(alive: false);
        n.Step(0.2);
        var h = n.Here.Single();
        Assert.Equal(dead, h.Occupant);
        // Away past the zone, moving, nobody near: released, back to their place at the front.
        var engine = n.Train.Rakes.First(r => r.Consist.HasEngine);
        while (engine.Distance < n.Site.End + H.Release + 50)
            n.Step(1, speed: 30);
        Assert.Equal(HoldoutState.Dormant, h.State);
        Assert.Contains(n.Events, e => e.Kind == HoldoutEventKind.Released && e.PlayerId == dead);
        Assert.Equal(dead, n.World.Holdouts!.Queue[0].PlayerId);
        // Back into the zone: it takes them again (D.5 "if the crew comes back for them").
        while (engine.Distance > n.Site.End - 20)
            n.Step(1, speed: -30);
        n.Step(0.2);
        Assert.Equal(dead, h.Occupant);
    }

    [Fact]
    public void AFacilitysSecondHoldoutWaitsForACrewOfFive()
    {
        foreach (int crew in new[] { 4, 5 })
        {
            var n = new Night(f => f.Kind == FeatureKind.Facility && f.Stop!.Holdouts.Count == 2, engineFrom: -1000);
            n.Add(alive: true);
            for (int i = 1; i < crew; i++)
                n.Add(alive: false);
            n.Step(0.2);
            int lit = n.Here.Count(h => h.Lit);
            Assert.Equal(crew >= H.SecondCrew ? 2 : 1, lit);
        }
    }

    [Fact]
    public void DeferringOnlyEverMovesYouDown()
    {
        // D.6: no jumping; positions only improve as the people ahead are freed.
        var n = new Night(AHalt, engineFrom: -5000);
        n.Add(alive: true);
        int a = n.Add(alive: false), b = n.Add(alive: false), c = n.Add(alive: false);
        n.Step(0.1);
        var q = n.World.Holdouts!;
        Assert.Equal([a, b, c], q.Queue.Select(e => e.PlayerId));
        q.Defer(a);
        Assert.Equal([b, a, c], q.Queue.Select(e => e.PlayerId));
        q.Defer(c);
        Assert.Equal([b, a, c], q.Queue.Select(e => e.PlayerId));
        // Throw from the dead is defer: a presses it, and goes behind c.
        n.Hold(a, PlayerButtons.Throw);
        n.Step(1.0 / SimConstants.TickRate);
        Assert.Equal([b, c, a], q.Queue.Select(e => e.PlayerId));
    }

    [Fact]
    public void CallingOutIsOneShoutACooldownAndNeverNoise()
    {
        // D.7: the shared cooldown, and dead silence (D.14): it never touches the Choir.
        var n = new Night(AHalt, engineFrom: -300);
        int living = n.Add(alive: true);
        int a = n.Add(alive: false), b = n.Add(alive: false);
        n.Step(0.2);
        // Somebody living within reach of it (D.7), or there's nobody to shout to.
        n.Stand(living, n.Here.Single().Door);
        double loud = n.World.Choir.Loudness;
        n.Hold(a, PlayerButtons.Use);
        n.Hold(b, PlayerButtons.Use);
        n.Step(1.0 / SimConstants.TickRate);
        Assert.Single(n.Events, e => e.Kind == HoldoutEventKind.CalledOut);
        n.Hold(a, PlayerButtons.None);
        n.Hold(b, PlayerButtons.None);
        n.Step(1.0 / SimConstants.TickRate);
        n.Hold(a, PlayerButtons.Use);
        n.Step(1.0 / SimConstants.TickRate);
        Assert.Single(n.Events, e => e.Kind == HoldoutEventKind.CalledOut);
        Assert.Equal(loud, n.World.Choir.Loudness);
    }

    [Fact]
    public void TheLiveMicIsTheOccupantsToggleAndNeverNoise()
    {
        // D.7: offered only to the player assigned to that Holdout; off by default; off again when they're freed. And D.14's
        // dead silence: talking on it never feeds the loudness meter, however loud.
        var n = new Night(AHalt, engineFrom: -300);
        // The host's enemies on, so the meter is listening to everyone's voice (App. C.7).
        n.World.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 3, authority: true);
        int living = n.Add(alive: true);
        int inside = n.Add(alive: false), other = n.Add(alive: false);
        n.Step(0.2);
        var h = n.Here.Single();
        Assert.Equal(inside, h.Occupant);
        Assert.False(h.LiveMic);
        n.Stand(living, h.Door + new Double3(3, 0, 0));
        n.Step(0.5);
        double quiet = n.World.Choir.Loudness;

        void Press(int id, bool down, byte voice = 0)
        {
            if (n.Intents.Length < n.Crew.Count)
                Array.Resize(ref n.Intents, n.Crew.Count);
            n.Intents[id] = new PlayerIntent { Actions = down ? PlayerActions.LiveMic : 0, Voice = voice };
            n.Step(1.0 / SimConstants.TickRate);
        }
        // Someone else dead pressing it does nothing: it isn't theirs.
        Press(other, true);
        Press(other, false);
        Assert.False(h.LiveMic);
        Assert.Null(n.World.Holdouts!.LiveMicOf(other));
        // The occupant's press turns it on; held, it stays on (a toggle on the press).
        Press(inside, true, voice: 255);
        Press(inside, true, voice: 255);
        Assert.True(h.LiveMic);
        Assert.Same(h, n.World.Holdouts.LiveMicOf(inside));
        // Shouting into it for a few seconds: the meter never hears it.
        for (int t = 0; t < 3 * SimConstants.TickRate; t++)
            Press(inside, false, voice: 255);
        Assert.Equal(quiet, n.World.Choir.Loudness);
        // ... where the living crewmate at the door saying the same would have been heard.
        n.Intents[living] = new PlayerIntent { Voice = 255 };
        n.Step(1);
        Assert.True(n.World.Choir.Loudness > quiet, $"loudness {quiet} -> {n.World.Choir.Loudness}");
        n.Intents[living] = default;

        // A client sees it (the HUD's LIVE MIC ON).
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), n.World.Run!.Route.Build(), n.Site.Start, Tuning.Boiler), Tuning.Combat);
        client.EnableHoldouts(H, n.World.Run.Route);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        Assert.True(client.Holdouts!.All[h.Index].LiveMic);
        Assert.Equal(inside, client.Holdouts.All[h.Index].Occupant);

        // Pressed again, off.
        Press(inside, true);
        Assert.False(h.LiveMic);
        Press(inside, false);
        Press(inside, true);
        Assert.True(h.LiveMic);
        // Freed, it's off, and they're heard as anyone living is.
        n.Stand(living, h.Door);
        n.Hold(living, PlayerButtons.Use);
        n.Step(h.Breach(H).Seconds + 0.2);
        Assert.Equal(HoldoutState.Freed, h.State);
        Assert.False(h.LiveMic);
        Assert.Null(n.World.Holdouts.LiveMicOf(inside));
    }

    [Fact]
    public void SmashingALockIsLoud()
    {
        var n = new Night(f => AHalt(f) && f.Stop!.Holdouts[0].Kind != HoldoutKind.Shelter, engineFrom: -300);
        int living = n.Add(alive: true);
        n.Add(alive: false);
        n.Step(0.2);
        var h = n.Here.Single();
        double loud = n.World.Choir.Loudness;
        n.Stand(living, h.Door);
        n.Hold(living, PlayerButtons.Use);
        n.Step(h.Breach(H).Seconds * 0.5);
        Assert.True(n.World.Choir.Loudness > loud, $"loudness {loud} → {n.World.Choir.Loudness}");
    }

    [Fact]
    public void SomeoneWhoJoinsMidRunWaitsInTheQueue()
    {
        // D.3: a mid-run joiner is lobbied, at the back, and can be freed anywhere (they never died here).
        var n = new Night(AHalt, engineFrom: -300);
        n.Add(alive: true);
        int joiner = n.Crew.Count;
        n.Crew.Add((joiner, PlayerMotor.SpawnInCab(n.Train, P) with { Health = 0, Death = DeathCause.Waiting }));
        n.Step(0.2);
        Assert.True(Assert.Single(n.World.Holdouts!.Queue).Lobbied);
        Assert.Equal(joiner, n.Here.Single().Occupant);
        // A lobbied player has no body.
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Ragdoll);
        Assert.Equal(0, n.World.Bodies.Deaths);
    }

    [Fact]
    public void DyingTwiceLeavesTwoBodies()
    {
        var n = new Night(AHalt, engineFrom: -300);
        int living = n.Add(alive: true), dead = n.Add(alive: false);
        n.Step(0.2);
        var h = n.Here.Single();
        n.Stand(living, h.Door);
        n.Hold(living, PlayerButtons.Use);
        n.Step(h.Breach(H).Seconds + 0.2);
        n[dead] = n[dead] with { Health = 0, Death = DeathCause.Mauled };
        n.Step(0.2);
        Assert.Equal(2, n.World.Bodies.All.Count(b => b.Kind == BodyKind.Ragdoll && b.Owner == dead));
        Assert.Equal(2, n.World.Bodies.Deaths);
    }

    [Fact]
    public void DeathAlwaysCostsTheCrewAndABodyHomeGetsMostOfItBack()
    {
        // D.9 and D.14 "no farming": whatever dies and whatever's recovered, the night never pays more than with no deaths.
        var rng = new Random(7);
        double? clean = null;
        for (int trial = 0; trial < 12; trial++)
        {
            int deaths = trial == 0 ? 0 : rng.Next(1, 5), recovered = trial == 0 ? 0 : rng.Next(0, deaths + 1);
            var n = new Night(AHalt, engineFrom: -300);
            n.Add(alive: true);
            for (int i = 0; i < deaths; i++)
                n.Add(alive: false);
            n.Step(0.1);
            // Bring `recovered` of the bodies aboard the first cargo car, and deliver.
            var car = n.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo).Id;
            foreach (var body in n.World.Bodies.All.Where(b => b.Kind == BodyKind.Ragdoll).Take(recovered))
                body.Parent = car;
            var report = Deliver(n);
            Assert.Equal(deaths, report.Deaths);
            Assert.Equal(recovered, report.BodiesHome);
            double fee = H.CrewLossFee * Tuning.Run.Economy.PerCar["frontier"];
            Assert.Equal(Math.Round(deaths * fee), report.CrewLossFees);
            Assert.Equal(Math.Round(recovered * H.BodyRefund * fee), report.BodyRefunds);
            clean ??= report.Net;
            if (deaths > 0)
                Assert.True(report.Net < clean, $"{deaths} deaths, {recovered} home: {report.Net} against {clean} with none");
        }
    }

    [Fact]
    public void ADropOutsBodyCostsNothingAndRefundsNothing()
    {
        var n = new Night(AHalt, engineFrom: -300);
        n.Add(alive: true);
        var left = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        var body = n.World.Bodies.DropOut(n.Train, 9, left);
        body.Parent = n.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo).Id;
        var report = Deliver(n);
        Assert.Equal(0, report.Deaths);
        Assert.Equal(0, report.BodiesHome);
        Assert.Equal(0, report.CrewLossFees);
    }

    static RunReport Deliver(Night n)
    {
        var run = n.World.Run!;
        run.Mirror(RunPhase.Arrived, RunEnd.Delivered, run.Seconds, -1, false, [.. Enumerable.Repeat(0.0, run.FacilityCount)]);
        return run.Tally(n.World, [.. n.Crew.Select(c => c.State)]);
    }
}
