using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

/// <summary>
/// <c>dt screenshot --hud --crew-news held|gone</c> (note 750): a solo night with a crewmate, Priya, on the last car's roof,
/// out of voice reach, held or just killed, so the HUD draws the line it gives the living for her.
/// </summary>
static class CrewNewsShot
{
    public static IPlaySession With(IPlaySession solo, bool gone)
    {
        const int Priya = 2;
        solo.World.Names[Priya] = "Priya";
        int last = solo.Train.Dynamics.Consist.Vehicles[^1].Id;
        var her = PlayerMotor.SpawnOnRoof(solo.Train, last, 0, solo.PlayerTuning);
        var seat = new Seat(solo, Priya, her);
        // Seen alive first, as a machine sees a crewmate before anything happens to them.
        Hud.CrewLine(seat);
        seat.Her = gone ? her with { Health = 0, Death = DeathCause.Mauled } : her with { Flags = her.Flags | PlayerFlags.Held };
        return seat;
    }

    /// <summary>The solo night as it is, with one more crewmate in its crew's states.</summary>
    sealed class Seat(IPlaySession inner, int id, PlayerState her) : IPlaySession
    {
        public PlayerState Her { get; set; } = her;
        public TrainOnLine Train => inner.Train;
        public World World => inner.World;
        public DarkTerritory.Sim.Route.Route? Route => inner.Route;
        public PlayerState Player => inner.Player;
        public TrainControls Controls => inner.Controls;
        public long Tick => inner.Tick;
        public PlayerTuning PlayerTuning => inner.PlayerTuning;
        public int PlayerId => inner.PlayerId;
        public string Status() => inner.Status();
        public void Step(in PlayerIntent intent) => inner.Step(intent);
        public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha) => inner.InterpolatedFrames(alpha);
        public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) =>
            inner.EyeCamera(frames, alpha, pendingYaw, pendingPitch);
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => inner.Crew(frames, alpha);
        public IReadOnlyList<RosterLine> Roster() =>
            [.. inner.Roster(), new RosterLine((byte)id, "PRIYA", PrototypeSession.Where(Her, Train), Her.Alive)];
        public IReadOnlyList<(int Id, PlayerState State)> CrewStates(double alpha) => [.. inner.CrewStates(alpha), (id, Her)];
    }
}
