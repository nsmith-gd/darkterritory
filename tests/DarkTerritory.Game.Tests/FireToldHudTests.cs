using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 939 (the director's playtest of 20 Oct evening, line 9: "We've got car one. I open it. Oh my god, it's on fire. Why is
/// it on fire?"): whoever comes to a burning car after the news has said what lit it is told again, there, for as long as it
/// burns; a car alight again tonight says so.
/// </summary>
public class FireToldHudTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning Tuning = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly EnemyTuning Enemies = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File));

    sealed class Seat(World world, PlayerState me) : IPlaySession
    {
        public PlayerState Me { get; set; } = me;
        public TrainOnLine Train => world.Train;
        public World World => world;
        public Sim.Route.Route? Route => null;
        public PlayerState Player => Me;
        public TrainControls Controls => default;
        public long Tick => world.Tick;
        public PlayerTuning PlayerTuning => Tuning;
        public int PlayerId => 1;
        public IReadOnlyList<(int Id, PlayerState State)> CrewStates(double alpha) => [(1, Me)];
        public string Status() => "";
        public void Step(in PlayerIntent intent) { }
        public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha) => [];
        public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) => default;
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => [];
    }

    static void Run(World w, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.Step(new TrainControls { Reverser = 1 });
        }
    }

    [Fact]
    public void AtABurningCarItsCauseIsToldForAsLongAsItBurnsAndARelightSaysAgain()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 6, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 2_000);
        var w = new World(train);
        w.EnableEnemies(Enemies with { Director = Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, route: null, 1, crew: 1, authority: true);
        int car = 3;
        var room = train.Frames[car].Shape.Interior!.Value;
        // On car 1's roof, far from it: nothing told here.
        var s = new Seat(w, PlayerMotor.SpawnOnRoof(train, 1, 0, Tuning));
        var fire = (CarFire)w.AddEnemy(id => CarFire.In(id, train, car, 2, Enemies.CarFire, FireCause.Hounds).LitInside(false).Ablaze(0.6));
        Run(w, 1);
        Assert.Null(Hud.FireLine(s));
        // In it: while the news is saying it, once is enough; after, the line.
        s.Me = new PlayerState { Parent = car, Position = new Double3(0, room.Min.Y, room.Max.Z - 1), Surface = Surface.Deck, Health = Tuning.Health };
        Assert.Null(Hud.FireLine(s));
        Run(w, w.NewsTuning.Seconds + 1);
        Assert.Equal($"CAR {car} ALIGHT · A CINDER HOUND ON ITS ROOF", Hud.FireLine(s));
        // Drawn: more at the top centre with it than without.
        var told = new Overlay();
        Hud.Build(told, 480, 270, s, crosshair: false);
        // Beside the car on the ground, by its side: still told.
        var beside = train.Frames[car].ToWorld(new Double3(train.Frames[car].Shape.HalfWidth + 1.5, 0, 0));
        s.Me = PlayerMotor.SpawnOnGround(beside, train.Line, train.Dynamics.Distance, Tuning);
        Assert.Equal($"CAR {car} ALIGHT · A CINDER HOUND ON ITS ROOF", Hud.FireLine(s));
        // A client's copy reads the same, from what's replicated alone.
        var copy = (CarFire)Enemy.Blank(EnemyKind.CarFire, fire.Id, fire.Extra);
        copy.Restore(fire.Phase, fire.PhaseSeconds, fire.Health, fire.Attached, fire.Local, fire.LineDistance, fire.Lateral, fire.Height, fire.Extra, fire.Extra2);
        Assert.Equal((FireCause.Hounds, 0, false, -1), (copy.Cause, copy.Again, copy.HoundInside, copy.From));
        // Out, and the line goes with it.
        foreach (var e in w.ActiveEnemies.OfType<CarFire>())
            e.Ablaze(0);
        for (int k = 0; k < 20 * SimConstants.TickRate && w.ActiveEnemies.OfType<CarFire>().Any(f => !f.Gone); k++)
            Run(w, SimConstants.TickSeconds);
        Assert.Null(Hud.FireLine(s));
        // Lit again by its hot box: "AGAIN", and the new cause.
        var relit = (CarFire)w.AddEnemy(id => CarFire.In(id, train, car, 2, Enemies.CarFire, FireCause.HotBox).Ablaze(0.6));
        Run(w, w.NewsTuning.Seconds + 1);
        Assert.Equal(1, relit.Again);
        Assert.Equal($"CAR {car} ALIGHT AGAIN · ITS HOT BOX RAN HOT", Hud.FireLine(s));
        var quiet = new Overlay();
        s.Me = PlayerMotor.SpawnOnRoof(train, 1, 0, Tuning);
        Hud.Build(quiet, 480, 270, s, crosshair: false);
        Assert.True(told.Count > quiet.Count, $"{told.Count} vertices with the line, {quiet.Count} without");
    }
}
