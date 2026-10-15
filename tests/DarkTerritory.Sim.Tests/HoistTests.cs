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
/// them as a load, Jump lets go, the drop is a gag (note 802; the director, 15 Oct 2026: "It's a silly gag not a big grief": at
/// most 10 health by its height, never a death), and the report names the operator for a drop that hurt.
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

    /// <summary>Note 802: the gag's knock for a drop of <paramref name="height"/> m, worked from player.json's numbers here.</summary>
    static int Gag(double height)
    {
        var g = P.Landing.OffTheHook!;
        return height <= g.From ? 0 : (int)Math.Round(g.MaxKnock * Math.Min(1, (height - g.From) / (g.FullAt - g.From)));
    }

    [Fact]
    public void TheGagsNumbersAreTheDirectors()
    {
        // Note 802 (the director, 15 Oct 2026): "the player can only lose 10 health at most from a drop and then scale that based
        // on height".
        var g = Assert.IsType<OffTheHookTuning>(P.Landing.OffTheHook);
        Assert.Equal(10, g.MaxKnock);
        Assert.Equal(0, g.Knock(g.From));
        Assert.Equal(g.MaxKnock / 2, g.Knock((g.From + g.FullAt) / 2));
        Assert.Equal(g.MaxKnock, g.Knock(g.FullAt));
        Assert.Equal(g.MaxKnock, g.Knock(100));
    }

    [Fact]
    public void LetGoFromTheTopItsTheGagsFullKnockAndTheReportNamesWhoLiftedThem()
    {
        // Note 802: the foundry's gantry tops a rider's feet out at 6.35 m, past landing.offTheHook.fullAt: the full knock of 10,
        // no more (note 573's fall table would have taken about 48), and the operator's in the report.
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(C.Height / C.HoistSpeed + 1, id => id == Operator ? Up : default);
        double feet = y.At(Friend).Y - (y.Crane.HookAt.Y - y.Crane.Hook);
        Assert.True(feet > P.Landing.OffTheHook!.FullAt, $"feet {feet:0.00} m up");
        y.Run(Dt, id => id == Friend ? Jump : default);
        Assert.True(y.Players[Friend].Has(PlayerFlags.OffTheHook));
        Assert.Equal(Operator, y.World.DroppedBy(Friend));
        y.Run(2);
        var her = y.Players[Friend];
        Assert.True(her.Alive);
        Assert.Equal(Surface.Ground, her.Surface);
        Assert.False(her.Has(PlayerFlags.OffTheHook));
        Assert.Equal(P.Landing.OffTheHook.MaxKnock, P.Health - her.Health);
        var line = Assert.Single(IncidentLog.Lines(y.World, 0, 0, _ => false), l => l.Victim == Friend);
        Assert.Equal("Priya", line.Who);
        Assert.Matches(@"^Dropped from the crane .+\. Dropped from the crane by Dave, 6 m up\.$", line.Text);
        Assert.Null(y.World.DroppedBy(Friend));
    }

    [Fact]
    public void OffATallerGantryItsStillTheGagNeverADeath()
    {
        // Note 802: an 11 m gantry puts a rider's feet past note 573's fallLethalFrom; off the hook it's still the gag's 10.
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
        Assert.True(her.Alive);
        Assert.Equal(DeathCause.None, her.Death);
        Assert.Equal(Surface.Ground, her.Surface);
        Assert.Equal(P.Landing.OffTheHook!.MaxKnock, P.Health - her.Health);
        Assert.Empty(y.World.Attribution.Of(IncidentKind.Death));
        var line = Assert.Single(IncidentLog.Lines(y.World, 0, 0, _ => false), l => l.Victim == Friend);
        Assert.EndsWith("Dropped from the crane by Dave, 9 m up.", line.Text);
    }

    [Fact]
    public void ALowDropIsASmallKnockByItsHeight()
    {
        // Note 802: lifted to about 2 m over the ground and let go, the knock is the gag's for that height (a couple of health).
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(1, id => id == Operator ? Up : default);
        y.Run(Dt);
        double feet = y.At(Friend).Y - (y.Crane.HookAt.Y - y.Crane.Hook);
        Assert.InRange(feet, 1.7, 2.4);
        y.Run(Dt, id => id == Friend ? Jump : default);
        y.Run(1.5);
        var her = y.Players[Friend];
        Assert.Equal(Surface.Ground, her.Surface);
        int knock = P.Health - her.Health;
        Assert.InRange(knock, 1, P.Landing.OffTheHook!.MaxKnock / 2);
        Assert.InRange(knock, Gag(feet) - 1, Gag(feet) + 1);
        Assert.Contains(IncidentLog.Lines(y.World, 0, 0, _ => false), l => l.Victim == Friend);
    }

    [Fact]
    public void LetGoWithNoLiftItsStillTheGagAndNobodysFault()
    {
        // Taken hold of as high as a crewmate can reach and let go without anyone lifting them: still off the hook, so the gag's
        // knock by the height (a little, from about 1.9 m), and nobody's in the report.
        var y = new Yard();
        y.HookOverGround(H.Height[1] - 0.05);
        y.FriendBesideTheHook();
        y.TakeHold();
        double feet = y.At(Friend).Y - (y.Crane.HookAt.Y - y.Crane.Hook);
        Assert.True(feet > P.Landing.OffTheHook!.From + 0.5, $"feet {feet:0.00} m up");
        y.Run(Dt, id => id == Friend ? Jump : default);
        Assert.Null(y.World.DroppedBy(Friend));
        Assert.True(y.Players[Friend].Has(PlayerFlags.OffTheHook));
        y.Run(2);
        var her = y.Players[Friend];
        Assert.True(her.Alive);
        Assert.Equal(Surface.Ground, her.Surface);
        Assert.InRange(P.Health - her.Health, Math.Max(1, Gag(feet) - 1), Gag(feet) + 1);
        Assert.DoesNotContain(IncidentLog.Lines(y.World, 0, 0, _ => false), l => l.Victim == Friend);
    }

    [Fact]
    public void OffTheHookCrossesTheWirePastTheOldSixteenBits()
    {
        // Note 802: PlayerFlags is a uint, OffTheHook bit 18 (past Lounging and Playing); a snapshot carries it, with Hoisted's bit, whole.
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(2, id => id == Operator ? Up : default);
        y.Run(Dt, id => id == Friend ? Jump : default);
        var flags = y.Players[Friend].Flags | PlayerFlags.Hoisted | PlayerFlags.Sooted;
        Assert.True(flags.HasFlag(PlayerFlags.OffTheHook));
        Assert.True((uint)flags > ushort.MaxValue);
        var records = WorldRecords.Capture(y.World, new TrainControls(), [new PlayerSnapshot(Friend, y.Players[Friend] with { Flags = flags })]);
        var w = new Ballast.Net.NetWriter();
        WorldRecords.WriteDelta(w, records, null);
        var r = new Ballast.Net.NetReader(w.Written);
        var read = WorldRecords.ReadDelta(ref r, null);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 0)), y.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, y.World.Run!.Route, 600, authority: false, FacilityTests.F);
        var controls = new TrainControls();
        var players = new List<PlayerSnapshot>();
        WorldRecords.Apply(read, client, ref controls, players);
        Assert.Equal(flags, Assert.Single(players, p => p.Id == Friend).State.Flags);
    }

    [Fact]
    public void TheGagNeverTakesTheLastHealth()
    {
        // Note 802: on 5 health, the full knock off the top leaves them on 1, not dead.
        var y = new Yard();
        y.HookOverGround(1.2);
        y.FriendBesideTheHook();
        y.TakeHold();
        y.Run(C.Height / C.HoistSpeed + 1, id => id == Operator ? Up : default);
        y.Players[Friend] = y.Players[Friend] with { Health = 5 };
        y.Run(Dt, id => id == Friend ? Jump : default);
        y.Run(2);
        var her = y.Players[Friend];
        Assert.True(her.Alive);
        Assert.Equal(1, her.Health);
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
