using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The BBQ deck on the HUD (ARCHITECTURE §8 note 698): each thing on it says what Use does there, a chair's sitter sees the
/// line low over their knees, and sat, getting up is all there is.
/// </summary>
public class BbqDeckHudTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static PrototypeSession Night()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        var c = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
        var t = s.Train.Dynamics.Tuning;
        s.Train.Dynamics.Consist.Tuning = Campaign.Apply(c, ["bbqDeck"], new Loadout(t, s.Train.BoilerTuning!, s.World.Combat!, null)).Train;
        s.Train.Step(0, default);
        s.Train.Vehicles[0].Beers = t.Composition.Bbq.Beers;
        return s;
    }

    static void StandAt(PrototypeSession s, InteractableKind kind)
    {
        var thing = s.Train.Frames[0].Shape.Interactables.First(i => i.Kind == kind);
        s.Player = s.Player with { Parent = 0, Surface = Surface.Deck, Position = thing.Position, Yaw = 0, Pitch = -0.4, Velocity = default };
    }

    [Fact]
    public void EachThingOnTheDeckSaysWhatUseDoesThere()
    {
        var s = Night();
        Assert.NotNull(s.Train.Frames[0].Shape.Bbq);
        StandAt(s, InteractableKind.Chair);
        Assert.Equal("SIT : [E]", Hud.Prompt(s));
        StandAt(s, InteractableKind.Cooler);
        Assert.Equal($"A BEER : [E]   {s.Train.Vehicles[0].Beers} LEFT", Hud.Prompt(s));
        s.Train.Vehicles[0].Beers = 0;
        Assert.Equal("THE COOLER'S EMPTY", Hud.Prompt(s));
        StandAt(s, InteractableKind.Grill);
        Assert.Equal("LIGHT THE GRILL : HOLD [E]", Hud.Prompt(s));
        s.Train.Vehicles[0].Grill = 60;
        Assert.Equal("GRILL A SAUSAGE : HOLD [E]", Hud.Prompt(s));
        // Sat: getting up, and the eyes low over the knees.
        StandAt(s, InteractableKind.Chair);
        s.Player = s.Player with { Flags = s.Player.Flags | PlayerFlags.Lounging };
        Assert.Equal("GET UP : [E]", Hud.Prompt(s));
        var cam = Eyes.From(s.Player, s.Player, s.Train.Frames, 1, 0, 0);
        var feet = s.Train.Frames[0].ToWorld(s.Player.Position);
        Assert.Equal(Eyes.Lounging, cam.Position.Y - feet.Y, 2);
    }

    [Fact]
    public void SatWithACanItsTheLoungeAPullAtItAndACheersOnAnEmote()
    {
        // C1's clips (note 791): sat back in the chair with a can on its arm; a pull at it while it's being drunk; and an
        // emote from the chair with it in hand is a cheers, then sat back again. The can's drawn in the fist for these.
        var s = Night();
        StandAt(s, InteractableKind.Chair);
        s.Player = s.Player with { Flags = s.Player.Flags | PlayerFlags.Lounging };
        Assert.Equal(Art.CrewPose.Lounge, Art.CrewActs.Of(s.Player, 1, s.World));
        var can = s.World.Bodies.SpawnCrate(s.Train, 0, s.Player.Position + Double3.Up, Sim.Physics.BodyKind.Beer);
        can.Carrier = 1;
        Assert.Equal(Art.CrewPose.Lounge, Art.CrewActs.Of(s.Player, 1, s.World));
        can.MendTicks = 10;
        Assert.Equal(Art.CrewPose.LoungeDrink, Art.CrewActs.Of(s.Player, 1, s.World));
        can.MendTicks = 0;
        s.World.Emotes.Add(new EmoteEvent(1, s.World.Tick, 1, Emote.Wave));
        Assert.Equal(Art.CrewPose.Cheers, Art.CrewActs.Of(s.Player, 1, s.World));
        Assert.True(Art.SceneArt.Fisted(Art.CrewPose.Cheers) && Art.SceneArt.Fisted(Art.CrewPose.Drink) && !Art.SceneArt.Fisted(Art.CrewPose.Carry));
        // Stood with it, being drunk: the stood pull.
        s.World.Emotes.Clear();
        s.Player = s.Player with { Flags = s.Player.Flags & ~PlayerFlags.Lounging };
        can.MendTicks = 10;
        Assert.Equal(Art.CrewPose.Drink, Art.CrewActs.Of(s.Player, 1, s.World));
    }
}
