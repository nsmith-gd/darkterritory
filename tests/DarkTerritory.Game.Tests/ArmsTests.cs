using Ballast;
using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>A crewmate's arms (T47, M4 "VR body IK"): two bones from the shoulder to where the headset's hands are.</summary>
public class ArmsTests
{
    [Theory]
    [InlineData(0.30, 1.95, -0.25)]  // up and out
    [InlineData(0.22, 1.15, -0.45)]  // held out in front
    [InlineData(0.30, 0.95, 0.10)]   // down by the hip, a touch behind
    public void TheArmKeepsItsLengthsAndBendsDownAndOut(double x, double y, double z)
    {
        var shoulder = Arms.Shoulder(1);
        var target = new Double3(x, y, z);
        var (elbow, hand) = Arms.Solve(shoulder, target, Arms.Pole(1));
        Assert.Equal(Arms.Upper, (elbow - shoulder).Length, 6);
        Assert.Equal(Arms.Lower, (hand - elbow).Length, 6);
        // Reachable, so the hand's where it was asked to be.
        Assert.True((hand - target).Length < 1e-6);
        // The elbow bends out towards its pole, down and out (like an arm, not up like a wing).
        var mid = (shoulder + hand) * 0.5;
        Assert.True(Double3.Dot(elbow - mid, Arms.Pole(1)) > 0);
    }

    [Fact]
    public void AHandOutOfReachIsReachedForAsFarAsTheArmGoes()
    {
        var shoulder = Arms.Shoulder(-1);
        var (elbow, hand) = Arms.Solve(shoulder, shoulder + new Double3(-2, 0, 0), Arms.Pole(-1));
        Assert.InRange((hand - shoulder).Length, (Arms.Upper + Arms.Lower) * 0.99, Arms.Upper + Arms.Lower);
        Assert.True(hand.X < shoulder.X);
        Assert.Equal(Arms.Upper, (elbow - shoulder).Length, 6);
    }

    [Fact]
    public void EachHandGoesToTheArmOnItsSideAndTheOtherHangs()
    {
        var up = new Double3(0.35, 2.05, -0.45);
        var (left, right) = Arms.Hands(up, default);
        Assert.Equal(up, right);
        Assert.Equal(Arms.Hanging(-1), left);
        // The sim doesn't say which is which: the one further left is the left.
        var a = new Double3(0.2, 1.1, -0.4);
        var b = new Double3(-0.2, 1.2, -0.4);
        Assert.Equal((b, a), Arms.Hands(a, b));
        Assert.Equal((b, a), Arms.Hands(b, a));
        // A keyboard player's arms hang.
        Assert.Equal((Arms.Hanging(-1), Arms.Hanging(1)), Arms.Hands(default, default));
    }
}
