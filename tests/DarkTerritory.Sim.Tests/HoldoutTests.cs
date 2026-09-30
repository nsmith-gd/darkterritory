using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D.5 transition by transition, on a generated night's own Holdouts, stepped the way the host steps them:
/// assign, eligibility, reassign, breach (and its lock), interrupted, freed, release, and assigning again on the crew's
/// return. And D.4: the interior is a safe volume, and the lamp is a world light.
/// </summary>
[Collection(nameof(LineGenTests))]
public class HoldoutTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Route.Route Line = Routes.Generate(Content, "frontier:7", 6);
    static readonly HoldoutTuning H = Tuning.Holdouts;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A night on the host, players by id, stepped in HostSession's order.</summary>
    internal sealed class Night
    {
        public readonly World World;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly Dictionary<int, PlayerIntent> Intent = new();
        public TrainControls Controls = new() { Reverser = 1 };
        public int SessionCrew = 2;

        /// <param name="enemies">With the director and its enemies, seeded (the dead-silence checks compare two nights).</param>
        public Night(double front, Route.Route? route = null, int cars = 6, bool enemies = false)
        {
            route ??= Line;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), route.Build(), front, Tuning.Boiler);
            World = new World(train, Tuning.Combat);
            if (enemies)
                World.EnableEnemies(Tuning.Enemies, route, 11, 3, authority: true);
            World.EnableRun(Tuning.Run, route, route.GateOr(600), authority: true);
            World.EnableBodies();
            World.EnableHoldouts(H);
            // Under way: past the gate.
            World.Run!.Resume(10, -1, train.Boiler.Tender, 0);
        }

        public TrainOnLine Train => World.Train;
        public Holdouts Holdouts => World.Holdouts!;

        /// <param name="afterWorld">Called each tick after the world steps, before its damage lands (to add some).</param>
        public void Step(double seconds = SimConstants.TickSeconds, double? holdSpeed = null, Action? afterWorld = null)
        {
            for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * SimConstants.TickRate)); i++)
            {
                if (holdSpeed is { } v)
                    Train.Dynamics.Velocity = v;
                World.BeginTick();
                foreach (int id in Crew.Keys.Order().ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, Intent.GetValueOrDefault(id), id);
                    Crew[id] = s;
                }
                World.Step(Controls);
                afterWorld?.Invoke();
                World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys.ToList());
                foreach (int id in Crew.Keys.Order().ToList())
                {
                    var s = Crew[id];
                    PlayerMotor.Step(ref s, Intent.GetValueOrDefault(id), Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[id] = s;
                }
                World.StepBodies([.. Crew.Select(c => (c.Key, c.Value))]);
                World.StepHoldouts(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys.ToList(), SessionCrew, P);
                World.StepRun([.. Crew.Values]);
            }
        }

        /// <summary>A player dead at the fortress end of the line (outside every site's zone), in the queue.</summary>
        public void DeadAtTheFortress(int id)
        {
            var s = PlayerMotor.SpawnOnGround(Train.Line.Sample(50).Position + new Double3(4, 0, 0), Train.Line, 50, P);
            s.Health = 0;
            s.Death = DeathCause.Mauled;
            Crew[id] = s;
            World.StepBodies([.. Crew.Select(c => (c.Key, c.Value))]);
        }

        /// <summary>A living crew member on the ground at a Holdout's door, a tool in hand.</summary>
        public void AtTheDoor(int id, Holdout h, BodyKind? tool)
        {
            var outward = (h.Door - h.Centre).Normalized;
            var at = h.Door + outward * 0.6;
            Crew[id] = PlayerMotor.SpawnOnGround(at, Train.Line, Train.Dynamics.Distance, P);
            if (tool is { } kind)
                World.Bodies.SpawnCrate(Train, 1, Double3.Zero, kind).Carrier = id;
        }

        public void Hold(int id, bool use) => Intent[id] = use ? new PlayerIntent { Buttons = PlayerButtons.Use } : default;
    }

    static PlanHoldout FirstFacility(bool second = false) => Line.Plan!.Holdouts.First(h => h.SiteKind == HoldoutSiteKind.Facility && h.Second == second);

    /// <summary>The engine stopped inside a Holdout's zone, on the main line.</summary>
    static Night InZone(PlanHoldout h) => new(front: h.Zone.S0 + 150);

    [Fact]
    public void AssignWhenTheConsistEntersTheZoneWithSomeoneEligible()
    {
        var site = FirstFacility();
        // Out of the zone: nothing, though someone's waiting.
        var n = new Night(front: site.Zone.S0 - 400);
        n.DeadAtTheFortress(2);
        n.Step(0.5);
        var h = n.Holdouts.Of(site.Id)!;
        Assert.Equal(HoldoutPhase.Dormant, h.Phase);
        Assert.False(h.LampLit);
        // Into it at yard speed.
        n.Step(30, holdSpeed: 20);
        Assert.True(h.InZone(n.Train.Dynamics.Path, n.Train.Dynamics.Distance) || h.Phase != HoldoutPhase.Dormant);
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        Assert.Equal(2, h.Assigned);
        Assert.True(h.LampLit);
        Assert.Equal(site.Id, n.Holdouts.Queue.Of(2)!.Holdout);
        Assert.Contains(n.Holdouts.History, e => e.Event.Outcome == HoldoutOutcome.Assigned && e.Event.Player == 2);
    }

    [Fact]
    public void NobodyWhoLastDiedInTheSitesZoneIsAssignedThereAndTheyKeepTheirPlace()
    {
        var site = FirstFacility();
        var n = InZone(site);
        // Player 2 dies here, in the zone.
        var here = PlayerMotor.SpawnOnGround(n.Train.Line.Sample(site.Zone.S0 + 100).Position + new Double3(5, 0, 0), n.Train.Line, site.Zone.S0 + 100, P);
        here.Death = DeathCause.Mauled;
        here.Health = 0;
        n.Crew[2] = here;
        n.Step(1);
        var h = n.Holdouts.Of(site.Id)!;
        Assert.Contains(site.Site, n.Holdouts.Queue.Of(2)!.DiedIn);
        Assert.Equal(HoldoutPhase.Dormant, h.Phase);
        // Player 3, dead elsewhere, is behind them and eligible: they get it; 2 keeps the front.
        n.DeadAtTheFortress(3);
        n.Step(0.2);
        Assert.Equal(3, h.Assigned);
        Assert.Equal(0, n.Holdouts.Queue.PositionOf(2));
        Assert.Equal(1, n.Holdouts.Queue.PositionOf(3));
    }

    [Fact]
    public void TheSecondFacilityHoldoutWaitsForACrewOfFive()
    {
        var second = Line.Plan!.Holdouts.FirstOrDefault(x => x.Second);
        Assert.NotNull(second);
        var n = InZone(second!);
        n.DeadAtTheFortress(2);
        n.DeadAtTheFortress(3);
        n.SessionCrew = H.SecondCrew - 1;
        n.Step(0.5);
        var first = n.Holdouts.All.First(x => x.Site.Site == second!.Site && !x.Site.Second);
        var h = n.Holdouts.Of(second!.Id)!;
        Assert.Equal(2, first.Assigned);
        Assert.Equal(HoldoutPhase.Dormant, h.Phase);
        n.SessionCrew = H.SecondCrew;
        n.Step(0.2);
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        Assert.Equal(3, h.Assigned);
    }

    [Fact]
    public void ReassignWhenTheAssignedDefersOrDisconnectsAndTheLampStaysLit()
    {
        var site = FirstFacility();
        var n = InZone(site);
        n.DeadAtTheFortress(2);
        n.DeadAtTheFortress(3);
        n.DeadAtTheFortress(4);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        Assert.Equal(2, h.Assigned);
        Assert.True(n.Holdouts.Queue.Defer(2, 2));
        n.Step();
        Assert.Equal(3, h.Assigned);
        Assert.True(h.LampLit);
        Assert.Contains(n.Holdouts.Events, e => e.Outcome == HoldoutOutcome.Reassigned && e.Player == 3);
        // 3 leaves the session.
        n.World.DroppedOut(3, n.Crew[3]);
        n.Crew.Remove(3);
        n.Step();
        Assert.Equal(4, h.Assigned);
        Assert.True(h.LampLit);
    }

    [Fact]
    public void TheBreachLocksTheAssignmentAndIsInterruptedByLettingGoLeavingOrBeingHit()
    {
        var site = FirstFacility();
        var n = InZone(site);
        n.DeadAtTheFortress(2);
        n.DeadAtTheFortress(3);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        n.AtTheDoor(1, h, BodyKind.Crowbar);
        n.Hold(1, true);
        n.Step(1);
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        Assert.Equal(1, h.Breacher);
        Assert.InRange(h.Progress, 0.9, 1.1);
        // Locked: the one inside can't defer out of it now.
        Assert.True(n.Holdouts.Queue.Of(2)!.Locked);
        Assert.False(n.Holdouts.Queue.Defer(2, 1));
        // Lets go: back to Occupied, from nothing.
        n.Hold(1, false);
        n.Step();
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        Assert.Equal(0, h.Progress);
        Assert.False(n.Holdouts.Queue.Of(2)!.Locked);
        // Again, and walks off.
        n.Hold(1, true);
        n.Step(0.5);
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        var s = n.Crew[1];
        s.Position += (h.Door - h.Centre).Normalized * 6;
        n.Crew[1] = s;
        n.Step();
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        Assert.Equal(0, h.Progress);
        // Again, and takes a hit.
        s = n.Crew[1];
        s.Position -= (h.Door - h.Centre).Normalized * 6;
        n.Crew[1] = s;
        n.Step(0.5);
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        n.Step(afterWorld: () => n.World.Damage.Add(new Enemies.DamageEvent(1, 5, DeathCause.Mauled)));
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        Assert.Equal(P.Health - 5, n.Crew[1].Health);
        Assert.Contains(n.Holdouts.Events, e => e.Outcome == HoldoutOutcome.Interrupted && e.By == 1);
        Assert.Equal(2, h.Assigned);
    }

    [Fact]
    public void NoBreachWithoutTheRightThingInYourHands()
    {
        var site = FirstFacility();
        var n = InZone(site);
        n.DeadAtTheFortress(2);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        n.AtTheDoor(1, h, null);
        n.Hold(1, true);
        n.Step(1);
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        // The repair kit opens a prison car's lock, but won't pry a barricade.
        Assert.Equal(h.Type == HoldoutType.PrisonCar ? BreachMethod.Open : null, n.Holdouts.MethodFor(h, BodyKind.RepairKit));
        Assert.NotNull(n.Holdouts.MethodFor(h, BodyKind.Shovel));
        Assert.Null(n.Holdouts.MethodFor(h, BodyKind.Lamp));
    }

    [Theory]
    [InlineData(BodyKind.Crowbar)]
    [InlineData(BodyKind.RepairKit)]
    public void FreedWhenTheBreachCompletesInsideTheHoldoutAtEightyAndItsSpent(BodyKind tool)
    {
        var site = Line.Plan!.Holdouts.FirstOrDefault(x => x.Type == HoldoutType.PrisonCar) ?? FirstFacility();
        var n = InZone(site);
        n.DeadAtTheFortress(2);
        n.DeadAtTheFortress(3);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        if (n.Holdouts.MethodFor(h, tool) is not { } method)
            return; // this line's Holdout isn't one the kit opens
        n.AtTheDoor(1, h, tool);
        n.Hold(1, true);
        double seconds = H.Step(method).Seconds;
        n.Step(seconds - 0.2);
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        Assert.False(n.Crew[2].Alive);
        n.Step(0.3);
        Assert.Equal(HoldoutPhase.Freed, h.Phase);
        var freed = n.Crew[2];
        Assert.True(freed.Alive);
        Assert.Equal(H.FreedHealth, freed.Health);
        Assert.True(h.Inside(PlayerMotor.WorldPosition(freed, n.Train)));
        Assert.Contains(n.Holdouts.History, e => e.Event.Outcome == HoldoutOutcome.Freed && e.Event.Player == 2 && e.Event.By == 1);
        Assert.Equal(-1, n.Holdouts.Queue.PositionOf(2));
        Assert.Equal(new Character(h.Appearance, h.VoiceSet), n.World.Characters[2]);
        // Spent for the rest of the run: 3 is still waiting, and it never takes them.
        n.Hold(1, false);
        n.Step(2);
        Assert.Equal(HoldoutPhase.Freed, h.Phase);
        Assert.NotEqual(site.Id, n.Holdouts.Queue.Of(3)!.Holdout);
    }

    [Fact]
    public void ReleaseWhenTheTrainLeavesAndNobodysNearThenAssignAgainWhenItComesBack()
    {
        var site = FirstFacility();
        var n = InZone(site);
        n.DeadAtTheFortress(2);
        n.DeadAtTheFortress(3);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        Assert.Equal(2, h.Assigned);
        // The train goes on out of the zone, with its crew (the one living player aboard).
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        double past = site.Zone.S1 + 600 - n.Train.Dynamics.Distance;
        n.Step(past / 20, holdSpeed: 20);
        Assert.False(h.InZone(n.Train.Dynamics.Path, n.Train.Dynamics.Distance));
        Assert.Equal(HoldoutPhase.Dormant, h.Phase);
        Assert.False(h.LampLit);
        Assert.Contains(n.Holdouts.History, e => e.Event.Outcome == HoldoutOutcome.Released && e.Event.Player == 2);
        // Back where they were in the queue: the front.
        Assert.Equal(0, n.Holdouts.Queue.PositionOf(2));
        Assert.Null(n.Holdouts.Queue.Of(2)!.Holdout);
        // The crew come back for them: it assigns again, to them.
        n.Step((n.Train.Dynamics.Distance - site.Zone.S1 + 100) / 20, holdSpeed: -20);
        n.Step(0.2, holdSpeed: 0);
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        Assert.Equal(2, h.Assigned);
    }

    [Fact]
    public void NotReleasedWhileAnyLivingCrewIsNear()
    {
        var site = FirstFacility();
        var n = InZone(site);
        n.DeadAtTheFortress(2);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        // One stays at the door as the train goes on without them.
        n.AtTheDoor(1, h, null);
        n.Crew[4] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Step((site.Zone.S1 + 600 - n.Train.Dynamics.Distance) / 20, holdSpeed: 20);
        Assert.Equal(HoldoutPhase.Occupied, h.Phase);
        // They walk off out of the release radius.
        var off = h.Centre + ((h.Door - h.Centre) with { Y = 0 }).Normalized * (H.ReleaseM + 20);
        n.Crew[1] = PlayerMotor.SpawnOnGround(off, n.Train.Line, n.Train.Dynamics.Distance, P);
        n.Step(0.2, holdSpeed: 20);
        Assert.Equal(HoldoutPhase.Dormant, h.Phase);
    }

    [Fact]
    public void NothingDealsDamageInsideASealedHoldout()
    {
        var site = FirstFacility();
        var n = InZone(site);
        var h = n.Holdouts.Of(site.Id)!;
        n.Crew[1] = PlayerMotor.SpawnOnGround(h.Centre, n.Train.Line, n.Train.Dynamics.Distance, P);
        Assert.True(n.Holdouts.InSealed(PlayerMotor.WorldPosition(n.Crew[1], n.Train)));
        n.World.BeginTick();
        n.World.Damage.Add(new Enemies.DamageEvent(1, 500, DeathCause.Mauled));
        n.World.ApplyDamage(id => n.Crew[id], (id, s) => n.Crew[id] = s, [1]);
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(P.Health, n.Crew[1].Health);
        // Outside it, the same bite lands (GDD v1.1 App. A.1: a bite floors you; only a grab's end, or the train's own
        // dangers, kill).
        n.Crew[1] = PlayerMotor.SpawnOnGround(h.Door + (h.Door - h.Centre).Normalized * 3, n.Train.Line, n.Train.Dynamics.Distance, P);
        n.World.ApplyDamage(id => n.Crew[id], (id, s) => n.Crew[id] = s, [1]);
        Assert.True(n.Crew[1].Health < P.Health);
    }

    [Fact]
    public void TheHoldoutLampIsAWorldLightTheCabSwitchDoesntTouch()
    {
        var site = FirstFacility();
        var n = InZone(site);
        n.DeadAtTheFortress(2);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        Assert.True(h.LampLit);
        // Lamps down, and a smashed headlamp: the Holdout's lamp burns on.
        n.World.LampLit = false;
        n.World.SmashLamp(60);
        n.Step(1);
        Assert.True(h.LampLit);
        Assert.False(n.World.LampShining);
        // And it's no car lamp: nothing aboard is lit by it, and the train's lamp position isn't it.
        Assert.NotEqual(h.Lamp, World.LampPosition(n.Train.Frames[0]));
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Lamp && (Bodies.WorldCentre(b, n.Train) - h.Lamp).Length < 1);
    }
}
