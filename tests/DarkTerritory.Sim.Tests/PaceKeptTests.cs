using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 1059 (queue #763): the pace's quiet is judged from the grace's end (the grace is the night's quiet spell by design,
/// note 266), and the director reads it: quiet out on the line <c>pressure.pressQuietSeconds</c>, the night is pressed.
/// </summary>
public class PaceKeptTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    static EnemyTuning PressingAt(double seconds) => Tuning.Enemies with { Director = D with { Pressure = D.Pressure with { PressQuietSeconds = seconds } } };

    [Fact]
    public void TheGraceIsNotCountedAsQuiet()
    {
        var n = new Night(6, speed: 14);
        var d = n.World.Director!;
        n.Run(d.Grace - 1);
        Assert.Equal("grace", d.HeldBecause);
        Assert.Equal(0, n.World.QuietSeconds);
        // Past it, the quiet counts.
        n.Run(10);
        Assert.InRange(n.World.QuietSeconds, 8, 11);
    }

    [Fact]
    public void AQuietPastItsLimitPressesTheNight()
    {
        Assert.True(D.Pressure.PressQuietSeconds > 0);
        double press = D.Pressure.PressQuietSeconds;
        var on = new Night(6, speed: 14, enemies: PressingAt(press));
        var off = new Night(6, speed: 14, enemies: PressingAt(0));
        // Both nights draw the same grace (the seed's); a crew that draws nothing is listened for, the pressure building.
        Assert.Equal(on.World.Director!.Grace, off.World.Director!.Grace);
        on.Run(on.World.Director.Grace + press - 2);
        off.Run(off.World.Director.Grace + press - 2);
        Assert.Equal(on.World.Director.Pressure, off.World.Director.Pressure, 9);
        Assert.True(on.World.Director.Pressure < D.Pressure.PressAt, $"pressure {on.World.Director.Pressure:0.00} before the limit");
        // At the limit the one keeping the pace is pressed and sends; the other is still listening for the crew's draw.
        on.Run(5);
        off.Run(5);
        static int Sent(Night n) => n.World.Director!.Log.Count(l => l.Kind is not (EnemyKind.Stoker or EnemyKind.Drift));
        Assert.Equal(0, Sent(off));
        Assert.Equal("listening", off.World.Director.HeldBecause);
        Assert.True(Sent(on) > 0, $"held: {on.World.Director.HeldBecause}, pressure {on.World.Director.Pressure:0.00}");
    }
}
