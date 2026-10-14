namespace Ballast.Core.Tests;

/// <summary>The fixed-step accumulator: whole ticks, the spiral guard, and note 532's stretch and dropped time.</summary>
public class FixedStepClockTests
{
    [Fact]
    public void WholeTicksAndTheRemainderAsAlpha()
    {
        var clock = new FixedStepClock(30);
        Assert.Equal(0, clock.Advance(0.02));
        Assert.Equal(1, clock.Advance(0.02));
        Assert.InRange(clock.Alpha, 0.19, 0.21);
        Assert.Equal(1, clock.Tick);
    }

    [Fact]
    public void TheGuardDropsTimeItCouldntSimulateAndCountsIt()
    {
        var clock = new FixedStepClock(30, maxTicksPerFrame: 8);
        Assert.Equal(0, clock.DroppedSeconds);
        // A second-long frame: eight ticks run, the rest is thrown away bar one tick's worth, and counted.
        Assert.Equal(8, clock.Advance(1.0));
        Assert.InRange(clock.DroppedSeconds, 1.0 - 9.0 / 30 - 1e-9, 1.0 - 9.0 / 30 + 1e-9);
        // One tick's worth is kept, so the next frame runs it and then stands even.
        Assert.Equal(1, clock.Advance(0));
        Assert.Equal(0, clock.Advance(0));
        // A frame that only just overruns drops nothing.
        double dropped = clock.DroppedSeconds;
        Assert.Equal(8, clock.Advance(8.0 / 30 + 0.001));
        Assert.Equal(dropped, clock.DroppedSeconds);
    }

    [Fact]
    public void AStretchedClockTicksSlowerAgainstTheWall()
    {
        var clock = new FixedStepClock(30) { Stretch = 1.05 };
        // A second of real time is 28 ticks at 5 % longer each (30 / 1.05 = 28.57), never 30.
        int ticks = 0;
        for (int i = 0; i < 100; i++)
            ticks += clock.Advance(0.01);
        Assert.Equal(28, ticks);
        clock.Stretch = 0.5; // never under one: a client can't run ahead of the host's clock
        Assert.Equal(1, clock.Stretch);
    }
}
