using Ballast;
using Ballast.Audio;
using Ballast.Render;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD v1.4 App. D.6 and D.7 on a client (note 179): the dead see the whole queue and where they are in it; each Call Out the
/// host counts is heard once, from its Holdout. The dead card offers what does something, in the player's keys (note 408).
/// </summary>
// Hud.Keys is the HUD's settings, static: the tests that set it don't run beside the one that compares two builds (note 390).
[Collection("Hud.Keys")]
public class DeadPhaseTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning Tuning = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));

    /// <summary>A seat at a networked night as the dead card reads it: who this player is, and every crewmate's state.</summary>
    sealed class Seat(World world, int id, PlayerState me, IReadOnlyList<(int Id, PlayerState State)> crew) : IPlaySession
    {
        public TrainOnLine Train => world.Train;
        public World World => world;
        public Sim.Route.Route? Route => null;
        public PlayerState Player => me;
        public TrainControls Controls => default;
        public long Tick => 0;
        public PlayerTuning PlayerTuning => Tuning;
        public int PlayerId => id;
        public int Watching => crew.FirstOrDefault(c => c.State.Alive, (-1, default)).Id;
        public IReadOnlyList<(int Id, PlayerState State)> CrewStates(double alpha) => [(id, me), .. crew];
        public string Status() => "";
        public void Step(in PlayerIntent intent) { }
        public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha) => [];
        public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) => default;
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => [];
    }

    static PlayerState Dead(DeathCause how) => new() { Parent = PlayerState.World, Health = 0, Death = how };

    static PlayerState StandingAt(Double3 at) => new() { Parent = PlayerState.World, Position = at, Health = 100 };

    [Fact]
    public void AJoinerWaitsUnderJoiningNotDead()
    {
        // D.10: lobbied players get the dead's experience, minus the vote; someone joining mid-run has never died.
        var world = Night();
        var far = StandingAt(world.Holdouts!.All[0].Inside + new Double3(5000, 0, 0));
        Assert.Equal("JOINING", Hud.DeadCardLines(new Seat(world, 4, Dead(DeathCause.Waiting), [(2, far)]))[0]);
        Assert.Equal("DEAD", Hud.DeadCardLines(new Seat(world, 4, Dead(DeathCause.Mauled), [(2, far)]))[0]);
    }

    [Fact]
    public void CallOutIsOfferedOnlyWhereItWouldBeHeard()
    {
        // D.7: a call counts from a lit Holdout while someone living is within callOutRadius of it (the host's own test,
        // Holdouts.Step), from any of the dead; D.10 offers it "when available".
        var world = Night();
        var holdouts = world.Holdouts!;
        var h = holdouts.All[0];
        double radius = holdouts.Tuning.CallOutRadius;
        var near = StandingAt(h.Inside + new Double3(radius * 0.5, 0, 0));
        var far = StandingAt(h.Inside + new Double3(radius * 3, 0, 0));
        bool Offered(int me, PlayerState living) =>
            Hud.DeadCardLines(new Seat(world, me, Dead(DeathCause.Mauled), [(2, living)])).Any(l => l.StartsWith("CALL OUT", StringComparison.Ordinal));
        // Nobody waiting in it: no lamp, nothing to call from.
        Assert.False(Offered(3, near));
        holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0);
        Assert.True(Offered(3, near));
        Assert.False(Offered(3, far));
        // The dead not in it can call from it too, as the host takes it.
        Assert.True(Offered(5, near));
        // Nobody living near it: nothing heard, however loud.
        Assert.False(Offered(3, Dead(DeathCause.Mauled)));
    }

    [Fact]
    public void LetSomeoneElseGoFirstIsOfferedOnlyWithSomeoneBehind()
    {
        // D.6: Defer moves you one place back; last in the queue (or not in it), it does nothing.
        var world = Night();
        var holdouts = world.Holdouts!;
        var far = StandingAt(holdouts.All[0].Inside + new Double3(5000, 0, 0));
        bool Offered(int me) =>
            Hud.DeadCardLines(new Seat(world, me, Dead(DeathCause.Mauled), [(2, far)])).Any(l => l.StartsWith("LET SOMEONE ELSE GO FIRST", StringComparison.Ordinal));
        Assert.False(Offered(3));
        holdouts.MirrorQueue([(3, false), (5, true)]);
        Assert.True(Offered(3));
        Assert.False(Offered(5));
        Assert.False(Offered(7));
    }

    [Fact]
    public void TheCardsKeysAreThePlayersOwn()
    {
        // The card's actions in the player's keys (Hud.Bound): Use, Throw and Jump rebound, it says so.
        var world = Night();
        var holdouts = world.Holdouts!;
        var h = holdouts.All[0];
        holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0);
        holdouts.MirrorQueue([(3, false), (5, true)]);
        var near = StandingAt(h.Inside + new Double3(10, 0, 0));
        var was = Hud.Keys;
        try
        {
            Hud.Keys = new Settings { Keys = new() { ["Use"] = "G", ["Throw"] = "Q", ["Jump"] = "C" } };
            var lines = Hud.DeadCardLines(new Seat(world, 3, Dead(DeathCause.Mauled), [(2, near)]));
            Assert.Contains("CALL OUT : [G]", lines);
            Assert.Contains("LET SOMEONE ELSE GO FIRST : [Q]", lines);
            Assert.Contains("LIVE MIC OFF : [C]", lines);
            Assert.DoesNotContain(lines, l => l.Contains("[E]") || l.Contains("[RMB]") || l.Contains("[SPACE]"));
        }
        finally
        {
            Hud.Keys = was;
        }
    }

    public static TheoryData<double, int> SizesAndScreens() =>
        [.. Settings.TextSizes.SelectMany(size => new[] { 540, 720, 1080 }.Select(screen => (size, screen)))];

    [Theory]
    [MemberData(nameof(SizesAndScreens))]
    public void TheCardFitsTheFrameAtEveryTextSize(double size, int screen)
    {
        // Everything on offer at once, in the default keys (Throw reads [RIGHT MOUSE]): each row inside the canvas, for every
        // way to have died (F1 on #417: MAULED and PECKED ran off a 540p window at 150%).
        var world = Night();
        world.Names[3] = "Bartholomew";
        world.Names[5] = "Anastasia";
        world.Names[6] = "Konstantin";
        var holdouts = world.Holdouts!;
        var h = holdouts.All[0];
        holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0);
        holdouts.MirrorQueue([(3, false), (5, true), (6, false)]);
        var was = Hud.Keys;
        try
        {
            Hud.Keys = new Settings { TextSize = size };
            var (w, hgt) = Hud.Keys.Canvas;
            float k = Hud.PromptScaleAt((float)screen / hgt);
            var o = new Overlay();
            foreach (var cause in Enum.GetValues<DeathCause>().Where(c => c != DeathCause.None))
            {
                var any = Hud.DeadCardDrawn(o, new Seat(world, 3, Dead(cause), [(2, StandingAt(h.Inside))]), w, k);
                Assert.All(any, line => Assert.True(UiStyle.MeasureKeyed(o, line, k) <= w - 12,
                    $"{cause}: '{line}' is {UiStyle.MeasureKeyed(o, line, k)} wide on a {w} canvas at {screen}p"));
            }
            var seat = new Seat(world, 3, Dead(DeathCause.Mauled), [(2, StandingAt(h.Inside))]);
            var drawn = Hud.DeadCardDrawn(o, seat, w, k);
            foreach (var line in drawn)
                Assert.True(UiStyle.MeasureKeyed(o, line, k) <= w - 12, $"'{line}' is {UiStyle.MeasureKeyed(o, line, k)} wide on a {w} canvas at {screen}p");
            // The rows with keys are whole, never wrapped; a long cause is wrapped, not lost.
            Assert.Contains("CALL OUT : [E]", drawn);
            Assert.Contains("LET SOMEONE ELSE GO FIRST : [RIGHT MOUSE]", drawn);
            // A row of actions breaks between them: no key parted from what it does.
            Assert.Contains(drawn, l => l.StartsWith("WATCHING CREW", StringComparison.Ordinal));
            Assert.DoesNotContain(drawn, l => l.TrimEnd().EndsWith(':') || l.StartsWith('['));
            Assert.Equal(string.Join(" ", Hud.DeadCardLines(seat)[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                string.Join(" ", drawn.TakeWhile(l => !l.StartsWith("WATCHING", StringComparison.Ordinal))));
        }
        finally
        {
            Hud.Keys = was;
        }
    }

    static World Night()
    {
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 1);
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 3, 0)), route.Build(), 600));
        world.EnableHoldouts(DataFile.Load<HoldoutTuning>(Path.Combine(Content, HoldoutTuning.File)), route);
        return world;
    }

    [Fact]
    public void TheDeadSeeTheWholeQueueAndTheirPlaceInIt()
    {
        var world = Night();
        world.Names[3] = "Sam";
        world.Names[5] = "Ana";
        Assert.Null(Hud.QueueLine(world, world.Holdouts!, 1));
        world.Holdouts!.MirrorQueue([(3, false), (1, false), (5, true)]);
        Assert.Equal("QUEUE: 1 SAM   2 YOU   3 ANA (JOINING)", Hud.QueueLine(world, world.Holdouts, 1));
    }

    [Fact]
    public void EachCallOutIsHeardOnceFromItsHoldout()
    {
        var world = Night();
        var h = world.Holdouts!.All[0];
        var audio = new GameAudio(Content);
        var ear = Listener.At(h.Inside + new Double3(10, 1.6, 0), 0);
        // A call in the prisoner's own voice set or a bout of banging (note 193), or note 179's synth shout and bang under them.
        static bool IsCall(Ballast.Audio.SoundInstance v) => v.Name.StartsWith("voice-prisoner-sets.", StringComparison.Ordinal)
            || v.Name is "voice-callout.bang" or "holdout-shout" or "holdout-bang";
        var heard = new Dictionary<int, Double3>();
        void Update(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
                foreach (var v in audio.Mixer.Voices.Where(IsCall))
                    heard.TryAdd(v.Id, v.Position);
            }
        }
        // Already called before this client looked: old news.
        world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0, calls: 2);
        Update(5);
        Assert.Empty(heard);
        world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0, calls: 3);
        Update(SimConstants.TickRate * 2);
        Assert.NotEmpty(heard);
        Assert.All(heard.Values, at => Assert.True((at - h.Inside).Length < 2, $"heard at {at}, the Holdout's inside is {h.Inside}"));
        // Once a call: nothing more until the count goes up again.
        int once = heard.Count;
        Update(SimConstants.TickRate * 2);
        Assert.Equal(once, heard.Count);
    }
}
