using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The mail cranes on the HUD (note 673; the director, 13 Oct: "I still don't know what the mail is suggesting"): its board
/// read, the crane at the top of the screen with its side and how far; at the open door on its side, the hook at the
/// crosshair; held as the car goes by, the bag's caught and the top of the screen says what was in it.
/// </summary>
public class MailHudTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Route Frontier = Routes.Generate(Content, "frontier:7", 6);

    [Fact]
    public void ACraneComingUpSaysWhichSideAndTheOpenDoorSaysTheHook()
    {
        var s = Staging.MailCrane(Content, Frontier, 6, seconds: 6, atDoor: true);
        var ahead = Hud.MailAhead(s);
        Assert.NotNull(ahead);
        var (drop, metres, seconds) = ahead.Value;
        Assert.InRange(seconds, 3, Hud.Tuning.MailLeadSeconds);
        var (head, distance) = Hud.MailLines(drop, metres);
        Assert.Equal(drop.Side > 0 ? "MAIL CRANE >" : "< MAIL CRANE", head);
        Assert.EndsWith(" M", distance);
        Assert.Equal("HOOK THE BAG : HOLD [LMB]", Hud.Prompt(s));

        // Held as the car goes by: caught, and paid (or the tender, the guns, a car).
        // Held under the crane's posted speed (note 727): over it, the bag tears.
        for (int i = 0; i < 20 * Sim.SimConstants.TickRate && !s.World.Lineside!.Passed(drop.Id); i++)
        {
            s.Train.Dynamics.Velocity = Math.Min(s.Train.Dynamics.Velocity, 12);
            s.Step(new PlayerIntent { Buttons = PlayerButtons.Fire });
        }
        Assert.True(s.World.Lineside!.Caught(drop.Id), "the bag wasn't caught from the open door");
        Assert.True(Hud.MailAhead(s) is not { } next || next.Drop.Id != drop.Id, "the crane's still said once it's gone by");
        Assert.False(string.IsNullOrEmpty(Hud.CaughtLine(drop)));
    }

    [Fact]
    public void AwayFromTheDoorTheCraneIsSaidButNotTheHook()
    {
        var s = Staging.MailCrane(Content, Frontier, 6, seconds: 6, atDoor: false);
        Assert.NotNull(Hud.MailAhead(s));
        Assert.NotEqual("HOOK THE BAG : HOLD [LMB]", Hud.Prompt(s));
        // Nothing caught going by with nobody at the door.
        var drop = Hud.MailAhead(s)!.Value.Drop;
        for (int i = 0; i < 20 * Sim.SimConstants.TickRate && !s.World.Lineside!.Passed(drop.Id); i++)
            s.Step(new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.False(s.World.Lineside!.Caught(drop.Id));
    }

    [Theory]
    [InlineData(DropKind.Mail, "MAIL: 40 SCRIP")]
    [InlineData(DropKind.Coal, "COAL: THE TENDER")]
    [InlineData(DropKind.Ammo, "ROUNDS: THE GUNS")]
    [InlineData(DropKind.Spares, "SPARES: A CAR MENDED")]
    public void ABagCaughtSaysWhatWasInIt(DropKind kind, string said) =>
        Assert.Equal(said, Hud.CaughtLine(new Drop(0, kind, 1000, 1, kind == DropKind.Mail ? 40 : 0.2)));
}
