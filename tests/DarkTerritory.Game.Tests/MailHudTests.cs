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
        Assert.NotNull(Hud.MailPrompt(s)); // so the top's "catch it from" line gives way to the crosshair's hook (note 901)

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
        // Note 901 (the director, 13 Oct evening: "I went outside looking for it and I did not see it"): away from the door,
        // the top of the screen says where the bag's caught from, on the crane's side.
        Assert.Null(Hud.MailPrompt(s));
        var coming = Hud.MailAhead(s)!.Value.Drop;
        Assert.Equal(coming.Side > 0 ? "CATCH IT FROM AN OPEN RIGHT SIDE DOOR" : "CATCH IT FROM AN OPEN LEFT SIDE DOOR", Hud.MailHow(coming));
        // Nothing caught going by with nobody at the door.
        var drop = Hud.MailAhead(s)!.Value.Drop;
        for (int i = 0; i < 20 * Sim.SimConstants.TickRate && !s.World.Lineside!.Passed(drop.Id); i++)
            s.Step(new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.False(s.World.Lineside!.Caught(drop.Id));
    }

    [Fact]
    public void WithTheMailCutANightHasNoCraneAndTheHudNeverSaysOne()
    {
        // Queue #530, note 803 (the director, 15 Oct 2026: "Im soft on the mail. I dont like it so far. Lets cut for now."):
        // a night on the shipped tuning, by where the first crane would stand, in the open side door on its side with the
        // hook held out the whole way past: no crane, no board, no MAIL CRANE line, no hook at the crosshair, no pay.
        var sight = DataFile.Load<SightTuning>(Path.Combine(Content, SightTuning.File));
        Assert.False(sight.MailCranes);
        var would = Lineside.Drops(sight with { MailCranes = true }, Frontier).First(d => d.At > Frontier.Gate + 1500);
        var s = new PrototypeSession(Content, Frontier, 6, enemies: false, at: would.At - 12 * 8);
        Assert.Empty(s.World.Lineside!.AllDrops);
        Assert.DoesNotContain(s.World.Lineside.Signs, b => b.Kind == SignKind.Drop);
        s.Controls = new Sim.Train.TrainControls { Reverser = 1 };
        int car = Math.Min(2, s.Train.Frames.Count - 1);
        var shape = s.Train.Frames[car].Shape;
        s.Respawn(car);
        if (Sim.Bots.StopHand.SideDoor(shape, would.Side) is { } door && !s.Train.Vehicles[car].DoorOpen(door))
            s.Train.Vehicles[car].ToggleDoor(door);
        var room = shape.Interior!.Value;
        s.Player = s.Player with
        {
            Parent = car,
            Position = new Ballast.Double3(would.Side * (room.HalfSize.X - 0.35), room.Min.Y, 0),
            Surface = Surface.Deck,
            Yaw = -would.Side * Math.PI / 2,
        };
        for (int i = 0; i < 20 * Sim.SimConstants.TickRate && s.Train.Dynamics.RearDistance < would.At + 5; i++)
        {
            s.Train.Dynamics.Velocity = 12;
            s.Step(new PlayerIntent { Buttons = PlayerButtons.Fire });
            Assert.Null(Hud.MailAhead(s));
            Assert.Null(Hud.MailPrompt(s));
        }
        Assert.True(s.Train.Dynamics.RearDistance >= would.At + 5, "the train never got past where the crane would be");
        Assert.Equal(0, s.World.Run!.Mail);
    }

    [Theory]
    [InlineData(DropKind.Mail, "MAIL: 40 SCRIP AT THE TERMINUS")]
    [InlineData(DropKind.Coal, "COAL: THE TENDER")]
    [InlineData(DropKind.Ammo, "ROUNDS: THE GUNS")]
    [InlineData(DropKind.Spares, "SPARES: A CAR MENDED")]
    public void ABagCaughtSaysWhatWasInIt(DropKind kind, string said) =>
        Assert.Equal(said, Hud.CaughtLine(new Drop(0, kind, 1000, 1, kind == DropKind.Mail ? 40 : 0.2)));
}
