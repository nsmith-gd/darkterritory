using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 750 on a client: the HUD's line for a crewmate held or killed out of voice reach (who and where, never what), from
/// the crew's states as this machine has them, and drawn under the news.
/// </summary>
public class CrewNewsHudTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning Tuning = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    /// <summary>This player and one crewmate, each as the snapshots have them, at a tick.</summary>
    sealed class Seat(World world, PlayerState me, PlayerState her) : IPlaySession
    {
        public PlayerState Me { get; set; } = me;
        public PlayerState Her { get; set; } = her;
        public long Now { get; set; }
        public TrainOnLine Train => world.Train;
        public World World => world;
        public Sim.Route.Route? Route => null;
        public PlayerState Player => Me;
        public TrainControls Controls => default;
        public long Tick => Now;
        public PlayerTuning PlayerTuning => Tuning;
        public int PlayerId => 1;
        public IReadOnlyList<(int Id, PlayerState State)> CrewStates(double alpha) => [(1, Me), (2, Her)];
        public string Status() => "";
        public void Step(in PlayerIntent intent) { }
        public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha) => [];
        public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) => default;
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => [];
    }

    static Seat AtTheFront()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 8, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 2_000);
        var world = new World(train);
        world.Names[2] = "Priya";
        return new Seat(world, PlayerMotor.SpawnOnRoof(train, 1, 0, Tuning), PlayerMotor.SpawnOnRoof(train, 6, 0, Tuning));
    }

    [Fact]
    public void TheLivingSeeAFarHoldAndAFarDeathAndTheDeadSeeNothing()
    {
        var s = AtTheFront();
        Assert.Null(Hud.CrewLine(s));
        s.Her = s.Her with { Flags = s.Her.Flags | PlayerFlags.Held };
        s.Now = 1;
        Assert.Equal("PRIYA - CAR 6 ROOF", Hud.CrewLine(s)?.Text);
        // Dead yourself: nothing, whatever's happening down the train.
        var alive = s.Me;
        s.Me = alive with { Health = 0, Death = DeathCause.Mauled };
        Assert.Null(Hud.CrewLine(s));
        s.Me = alive;
        // Killed: the death's line, red, for its seconds.
        s.Her = s.Her with { Health = 0, Death = DeathCause.Mauled };
        s.Now = 2;
        Assert.Equal("PRIYA IS GONE - CAR 6 ROOF", Hud.CrewLine(s)?.Text);
        s.Now = 2 + (long)((Tuning.CrewNews.DeathSeconds + 0.1) * SimConstants.TickRate);
        Assert.Null(Hud.CrewLine(s));
    }

    [Fact]
    public void TheLineIsDrawnUnderTheNews()
    {
        var s = AtTheFront();
        var quiet = new Overlay();
        Hud.CrewLine(s);
        Hud.Build(quiet, 480, 270, s, crosshair: false);
        s.Her = s.Her with { Flags = s.Her.Flags | PlayerFlags.Held };
        s.Now = 1;
        var told = new Overlay();
        Hud.Build(told, 480, 270, s, crosshair: false);
        // More drawn at the top centre with the line up than without.
        Assert.True(told.Count > quiet.Count, $"{told.Count} vertices with the line, {quiet.Count} without");
    }
}
