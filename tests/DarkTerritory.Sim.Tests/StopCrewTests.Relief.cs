using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

public partial class StopCrewTests
{
    [Fact]
    public void AtTheCraneTheOperatorLetsGoOfTheControlsToRelieveADeadDriver()
    {
        // Note 955 (frontier:7, 8 crew, 12 cars, seed 6, note 933's missed dawn with no Car Hugger): Climbers killed the driver
        // at the Foundry's loading, and the relief (note 399) fell to the crane's operator. Its legs walked it to the train from
        // the controls, but the stick drives the crane while it has them: it stood at the crane till dawn, the fire out, the
        // train where the driver fell. It lets go of them first, as it does for trouble (note 609).
        var night = new Night(cars: 8, walkers: 0, modules: [ModuleKind.Crane, ModuleKind.Winch]);
        for (int i = 0; i < night.Bots.Count; i++)
            if (night.Bots[i] is RoofWalkerBot w)
                (w.Me, w.Calls) = (i + 1, night.Calls);
        var run = night.World.Run!;
        int at = -1;
        night.Until(() => at >= 0 || run.Departures > 0, 1500, () =>
            at = night.Crew.FindIndex(c => c.Alive && c.Has(PlayerFlags.Operating)));
        Assert.True(at > 0, "nobody took the crane's controls");
        night.Crew[0] = night.Crew[0] with { Health = 0, Death = DeathCause.Climbed };
        // The others go on with the loading: only the operator's free for the controls (the relief is whoever claims it).
        for (int i = 1; i < night.Bots.Count; i++)
            if (i != at && night.Bots[i] is RoofWalkerBot other)
                other.Calls = null;
        night.Until(() => PlayerMotor.InCab(night.Crew[at], night.Train), 120);
        var hand = Assert.IsType<RoofWalkerBot>(night.Bots[at]);
        Assert.True(PlayerMotor.InCab(night.Crew[at], night.Train),
            $"the operator ({hand.Job?.Doing}, relieving {hand.Relieving}, operating {night.Crew[at].Has(PlayerFlags.Operating)}) never reached the cab");
    }
}
