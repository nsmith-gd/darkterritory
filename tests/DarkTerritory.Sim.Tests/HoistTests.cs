using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A friend on the crane's hook (GDD §18; ARCHITECTURE §8 note 726; the friendslop audit's griefers, the director, 14 Oct 2026:
/// "The griefer ideas are funny"): a crewmate takes hold of the empty hook and rides it, the operator lifts, runs and lowers
/// them as a load, Jump lets go and the world's fall rules take over, and the report names the operator for a drop that hurt.
/// </summary>
public class HoistTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CraneTuning C = FacilityTests.F.Crane;
    static readonly HoistTuning H = C.Hoist;
    const double Dt = SimConstants.TickSeconds;
    const int Operator = 1, Friend = 2;

    static readonly PlayerIntent Press = new() { Actions = PlayerActions.Seat };
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };
    static readonly PlayerIntent Jump = new() { Buttons = PlayerButtons.Jump };
    static readonly PlayerIntent Up = new() { Buttons = PlayerButtons.Jump };
    static readonly PlayerIntent Down = new() { Buttons = PlayerButtons.Brake };
    static readonly PlayerIntent Fire = new() { Buttons = PlayerButtons.Fire };

    /// <summary>
    /// A foundry's crane with Dave at its controls, stepped the way the host steps a night (HostSession.Step): the crew at the
    /// tick's start, the carried's and the hook's intents stripped, the acts, the world, everyone's feet, the rescues, the hooks,
    /// the bodies and the run (where the crane moves).
    /// </summary>
    sealed class Yard
    {
        public readonly FacilityTests.Stop Stop;
        public readonly Crane Crane;
        public readonly SortedDictionary<int, PlayerState> Players = [];
        public World World => Stop.World;
        public TrainOnLine Train => Stop.Train;

        public Yard(FacilityTuning? tuning = null)
        {
            Stop = tuning is null ? new FacilityTests.Stop(ModuleKind.Crane) : new FacilityTests.Stop(FacilityKind.Foundry, tuning);
            Crane = Assert.IsType<Crane>(Stop.Site.Crane);
            World.Movement = P;
            World.Names[Operator] = "Dave";
            World.Names[Friend] = "Priya";
            Players[Operator] = Stop.OnTheGround(Crane.Controls);
            Run(Dt, id => id == Operator ? Press : default);
            Assert.True(Players[Operator].Has(PlayerFlags.Operating));
        }

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null) => Ticks((int)Math.Round(seconds * SimConstants.TickRate), intent);

        public void Ticks(int ticks, Func<int, PlayerIntent>? intent = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                Train.Dynamics.Velocity = 0;
                World.Mates = [.. Players.Select(c => (c.Key, c.Value))];
                var crowd = Horseplay.Crowd(World.Mates);
                var ids = Players.Keys.ToList();
                var intents = ids.ToDictionary(id => id, id => Rescue.CarriedIntent(Players[id], Horseplay.Staggered(Players[id], intent?.Invoke(id) ?? default)));
                World.BeginTick();
                foreach (var id in ids)
                {
                    var s = Players[id];
                    World.CrewAct(ref s, intents[id], id);
                    Players[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                World.ApplyDamage(id => Players.TryGetValue(id, out var s) ? s : null, (id, s) => Players[id] = s, ids);
                foreach (var id in ids)
                {
                    var s = Players[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, T, Dt, applyLook: false, id, crowd);
                    Players[id] = s;
                }
                World.StepRescue(id => Players.TryGetValue(id, out var s) ? s : null, (id, s) => Players[id] = s);
                World.StepHoist(id => Players.TryGetValue(id, out var s) ? s : null, (id, s) => Players[id] = s);
                World.StepBodies([.. ids.Select(id => (id, Players[id]))]);
                World.StepRun([.. ids.Select(id => Players[id])]);
            }
        }

        /// <summary>The hook over open ground between the track and the stack, clear of the castings, at a height over the ground.</summary>
        public void HookOverGround(double height)
        {
            Crane.Bridge = C.Along + C.Length * 0.35;
            Crane.Trolley = C.Span[1] * 0.55;
            Crane.Hook = height;
            Assert.Equal(-1, Crane.Under(Train).Car);
        }

        /// <summary>Priya on the ground beside the hook, half a metre off it.</summary>
        public void FriendBesideTheHook()
        {
            var ground = Crane.HookAt with { Y = Crane.HookAt.Y - Crane.Hook };
            Players[Friend] = Stop.OnTheGround(ground + new Double3(0.5, 0, 0));
        }

        /// <summary>Priya holds Use at the hook long enough to take hold, Dave's hands still on the controls.</summary>
        public void TakeHold()
        {
            Run(H.GrabSeconds + 0.1, id => id == Friend ? Use : default);
            Assert.True(Players[Friend].Has(PlayerFlags.Hoisted));
        }

        public Double3 At(int id) => PlayerMotor.WorldPosition(Players[id], Train);
    }

    [Fact]
    public void AFriendTakesHoldOfTheEmptyHookAndRidesItWhereverTheOperatorTakesIt()
    {
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        Assert.Same(y.Crane, y.World.HookInReach(y.Players[Friend], Friend));
        // A held Use, not a press: short of grabSeconds they're still on their feet.
        y.Run(H.GrabSeconds - 3 * Dt, id => id == Friend ? Use : default);
        Assert.False(y.Players[Friend].Has(PlayerFlags.Hoisted));
        Assert.Equal(PlayerState.World, y.Players[Friend].Parent);
        y.Run(0.2, id => id == Friend ? Use : default);
        var on = y.Players[Friend];
        Assert.True(on.Has(PlayerFlags.Hoisted));
        Assert.Equal(1, y.Crane.Riders);
        Assert.True(y.Crane.Loaded);
        Assert.Same(y.Crane, y.World.HoistedOn(Friend));
        Assert.True((y.At(Friend) - y.Crane.RiderFeet(0, 1)).Length < 1e-6, $"stood at {y.At(Friend)}, the hook's {y.Crane.HookAt}");

        // Up, along the bridge and across: they go with it, stood on the hook, a tick behind the crane at most (it moves after them).
        double step = Math.Max(C.HoistSpeed, Math.Max(C.BridgeSpeed, C.TrolleySpeed)) * Dt * 1.5;
        y.Run(2, id => id == Operator ? Up : default);
        Assert.True(Math.Abs(y.At(Friend).Y - (y.Crane.HookAt.Y + H.FeetAbove)) <= step, $"{y.At(Friend).Y:0.00} on a hook at {y.Crane.HookAt.Y:0.00}");
        Assert.True(y.Crane.Hook > 1.2 + 2 * C.HoistSpeed - 0.1);
        y.Run(1.5, id => id == Operator ? new PlayerIntent { MoveZ = 1, MoveX = -1 } : default);
        Assert.True((y.At(Friend) - y.Crane.RiderFeet(0, 1)).Length <= step * 1.5, $"{(y.At(Friend) - y.Crane.RiderFeet(0, 1)).Length:0.000} m off the hook");
        Assert.Equal(PlayerState.World, y.Players[Friend].Parent);
        Assert.True(y.Players[Friend].Alive);
        // Their own keys are look, talk and let go: walking does nothing on the hook (stood still, a tick on to catch it up).
        y.Run(Dt);
        var before = y.At(Friend);
        y.Run(1, id => id == Friend ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run } : default);
        Assert.True((y.At(Friend) - before).Length < 1e-6);
        Assert.True(y.Players[Friend].Has(PlayerFlags.Hoisted));
    }

    [Fact]
    public void WithSomeoneOnItTheHookStopsShortOfTheBridgeAndRiggingWaits()
    {
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(C.Height / C.HoistSpeed + 1, id => id == Operator ? Up : default);
        Assert.Equal(C.Height - H.Headroom, y.Crane.Hook, 6);
        // Their head stays under the bridge's rails.
        Assert.True(y.At(Friend).Y + P.Height < y.Crane.BridgeEnd(0).Y);
        // A casting can't be rigged under their boots, and there's no second grab of a hook with a casting on it.
        Assert.Null(y.Crane.Riggable(y.Crane.HookAt));
        y.Crane.Castings[0].State = CastingState.Hooked;
        Assert.False(y.Crane.Grabbable(y.Crane.HookAt - Double3.Up));
        y.Crane.Castings[0].State = CastingState.Stacked;
    }

    [Fact]
    public void JumpLetsGoAndFromLowItsAStepDownNobodyIsBlamed()
    {
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(1, id => id == Operator ? Down : default);
        y.Run(Dt, id => id == Friend ? Jump : default);
        Assert.False(y.Players[Friend].Has(PlayerFlags.Hoisted));
        Assert.Equal(0, y.Crane.Riders);
        y.Run(1);
        Assert.Equal(Surface.Ground, y.Players[Friend].Surface);
        Assert.Equal(P.Health, y.Players[Friend].Health);
        Assert.DoesNotContain(IncidentLog.Lines(y.World, 0, 0, _ => false), l => l.Victim == Friend);
    }

    [Fact]
    public void LetGoFromTheTopItsAKnockAndTheReportNamesWhoLiftedThem()
    {
        // Note 573's fall by height: the foundry's gantry tops a rider's feet out at 6.35 m, between fallHurtFrom (5.5) and
        // fallLethalFrom (7.5): a hard knock, the operator's in the report.
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(C.Height / C.HoistSpeed + 1, id => id == Operator ? Up : default);
        double feet = y.At(Friend).Y - (y.Crane.HookAt.Y - y.Crane.Hook);
        Assert.InRange(feet, P.Landing.FallHurtFrom, P.Landing.FallLethalFrom);
        y.Run(Dt, id => id == Friend ? Jump : default);
        Assert.Equal(Operator, y.World.DroppedBy(Friend));
        y.Run(2);
        var her = y.Players[Friend];
        Assert.True(her.Alive);
        Assert.Equal(Surface.Ground, her.Surface);
        int knock = (int)Math.Round(P.Landing.RollDamage + (P.Health - P.Landing.RollDamage) * (feet - P.Landing.FallHurtFrom) / (P.Landing.FallLethalFrom - P.Landing.FallHurtFrom));
        Assert.InRange(P.Health - her.Health, knock - 6, knock + 6);
        var line = Assert.Single(IncidentLog.Lines(y.World, 0, 0, _ => false), l => l.Victim == Friend);
        Assert.Equal("Priya", line.Who);
        Assert.Matches(@"^Dropped from the crane .+\. Dropped from the crane by Dave, 6 m up\.$", line.Text);
        Assert.Null(y.World.DroppedBy(Friend));
    }

    [Fact]
    public void OffATallerGantryTheDropKillsAndTheDeathIsTheOperators()
    {
        var tall = FacilityTests.F with { Crane = C with { Height = 11 } };
        var y = new Yard(tall);
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(11 / C.HoistSpeed + 1, id => id == Operator ? Up : default);
        Assert.True(y.At(Friend).Y - (y.Crane.HookAt.Y - y.Crane.Hook) > P.Landing.FallLethalFrom);
        y.Run(Dt, id => id == Friend ? Jump : default);
        y.Run(2.5);
        var her = y.Players[Friend];
        Assert.False(her.Alive);
        Assert.Equal(DeathCause.Dropped, her.Death);
        var death = Assert.Single(y.World.Attribution.Of(IncidentKind.Death));
        Assert.Equal((Friend, Operator, "Dropped from the crane"), (death.Victim, death.Actor, death.What));
        Assert.Equal("Dropped from the crane by {actor}, 9 m up.", death.Action);
    }

    [Fact]
    public void ShakingThemOffIsMischiefOnlyWithTheSwitchOn()
    {
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(4, id => id == Operator ? Up : default);
        // CREW MISCHIEF off: Fire with nothing hooked does nothing to a friend on it.
        y.World.CrewMischief = false;
        y.Run(3 * Dt, id => id == Operator ? Fire : default);
        y.Run(3 * Dt);
        Assert.True(y.Players[Friend].Has(PlayerFlags.Hoisted));
        // On, it shakes them off, and it's the operator's.
        y.World.CrewMischief = true;
        y.Run(Dt, id => id == Operator ? Fire : default);
        y.Run(2 * Dt);
        Assert.False(y.Players[Friend].Has(PlayerFlags.Hoisted));
        Assert.True(y.World.WasShaken(Friend));
        Assert.Equal(Operator, y.World.DroppedBy(Friend));
    }

    [Fact]
    public void FromACarsRoofOntoTheHookAndSetDownOnTheRoofAgainBackInTheCarsFrame()
    {
        // Moving frames: stood on a car's roof, the rider leaves the car's frame for the world's (the gantry stands in it); set
        // down over the car and let go, they land on its roof and are in its frame again.
        var y = new Yard();
        var car = y.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo)
            .OrderBy(v => Math.Abs(y.Train.Frames[v.Id].ToLocal(y.Crane.HookAt).Z)).First();
        var frame = y.Train.Frames[car.Id];
        (y.Crane.Bridge, y.Crane.Trolley, _) = y.Crane.Over(frame.ToWorld(new Double3(0, 0, 0)));
        Assert.Equal(car.Id, y.Crane.Under(y.Train).Car);
        y.Crane.Hook = y.Crane.Lowest(y.Train) + 0.8;
        var roof = frame.ToLocal(y.Crane.HookAt) with { Y = frame.Shape.RoofHeight };
        y.Players[Friend] = new PlayerState
        {
            Parent = car.Id,
            Position = roof + new Double3(0, 0, 0.6),
            Surface = Surface.Roof,
            Health = P.Health,
            LineHint = y.Train.Cars[car.Id].FrontDistance
        };
        y.TakeHold();
        Assert.Equal(PlayerState.World, y.Players[Friend].Parent);
        y.Run(2, id => id == Operator ? Down : default);
        Assert.Equal(y.Crane.Lowest(y.Train), y.Crane.Hook, 6);
        y.Run(Dt, id => id == Friend ? Jump : default);
        y.Run(1);
        var her = y.Players[Friend];
        Assert.Equal(car.Id, her.Parent);
        Assert.Equal(Surface.Roof, her.Surface);
        Assert.Equal(P.Health, her.Health);
        Assert.True(Math.Abs(her.Position.Y - frame.Shape.RoofHeight) < 0.05, $"stood {her.Position.Y:0.00} m up in the car's frame");
    }

    [Fact]
    public void BotsTheCarriedAndFullHandsNeverTakeHold()
    {
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        var her = y.Players[Friend];
        Assert.NotNull(y.World.HookInReach(her, Friend));
        Assert.Null(y.World.HookInReach(her with { Flags = PlayerFlags.Carried }, Friend));
        Assert.Null(y.World.HookInReach(her with { Flags = PlayerFlags.Heavy }, Friend));
        // Out of reach: too high over them, or too far off across the ground.
        y.Crane.Hook = 1.2 + H.Height[1] + 0.3;
        Assert.Null(y.World.HookInReach(her, Friend));
        y.Crane.Hook = 1.2;
        Assert.Null(y.World.HookInReach(her with { Position = her.Position + new Double3(H.Reach + 0.6, 0, 0) }, Friend));
        // A bot holding Use at the hook stays on its feet.
        y.World.BotIds.Add(Friend);
        y.Run(H.GrabSeconds + 0.5, id => id == Friend ? Use : default);
        Assert.False(y.Players[Friend].Has(PlayerFlags.Hoisted));
        Assert.Equal(0, y.Crane.Riders);
    }

    [Fact]
    public void AClientSeesWhoRidesTheHook()
    {
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 0)), y.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, y.World.Run!.Route, 600, authority: false, FacilityTests.F);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(y.World, controls, []), client, ref controls, []);
        var seen = client.Run!.Sites[y.Stop.Site.Index]!.Crane!;
        Assert.Equal(1, seen.Riders);
        Assert.True(seen.Loaded);
        Assert.Equal(C.Height - H.Headroom, seen.Highest, 6);
    }
}
