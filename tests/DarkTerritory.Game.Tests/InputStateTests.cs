using Ballast.Platform;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The window's key state as the tick loop reads it (note 850: the 16 Oct evening playtest's joiner, on Linux, could not jump
/// while running; her machine's frames were long, and a jump tapped within one frame was never seen held).
/// </summary>
public class InputStateTests
{
    [Fact]
    public void ATapWithinOneFrameReadsAsHeldForThatFrameOnly()
    {
        var input = new InputState();
        input.Set(Key.LeftShift, true);
        input.Set(Key.W, true);
        input.EndFrame();
        // Down and up between two pumps' EndFrames: a 40 ms tap on a 100 ms frame.
        input.Set(Key.Space, true);
        input.Set(Key.Space, false);
        Assert.True(input.Down(Key.Space), "the tapped key is held this frame");
        Assert.True(input.Pressed(Key.Space));
        Assert.True(input.Down(Key.LeftShift) && input.Down(Key.W), "the run keys stay held around it");
        input.EndFrame();
        Assert.False(input.Down(Key.Space), "and let go the frame after, as a key held one frame would be");
        Assert.False(input.Pressed(Key.Space));
    }

    [Fact]
    public void AKeyHeldAcrossFramesIsDownWithoutBeingPressedAgain()
    {
        var input = new InputState();
        input.Set(Key.Space, true);
        Assert.True(input.Pressed(Key.Space));
        input.EndFrame();
        // The system's key repeat sends the press again while it's held: not a new press.
        input.Set(Key.Space, true);
        Assert.True(input.Down(Key.Space));
        Assert.False(input.Pressed(Key.Space));
        input.Set(Key.Space, false);
        Assert.False(input.Down(Key.Space), "let go of after frames held, it's up: only a press this frame makes a tap held");
    }

    [Fact]
    public void SwallowedPressesAreNotHeldEither()
    {
        var input = new InputState();
        input.Set(Key.Space, true);
        input.Set(Key.Space, false);
        input.Set(Key.W, true);
        input.Swallow();
        Assert.False(input.Down(Key.Space), "a tap a text box took is gone");
        Assert.True(input.Down(Key.W), "a key still held stays held");
    }
}
