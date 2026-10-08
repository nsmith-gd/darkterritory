using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Talking and reading in a fortress town (GDD §3.1, App. F.1 T133; ARCHITECTURE §8 note 281): the prompt for what's in
/// front of you, a card that opens on Use, turns on Use again, and closes when you walk off.
/// </summary>
public class TownTalkTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static PrototypeSession Night() => new(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 6), 6, enemies: false);

    /// <summary>Stands the player on the ground before a town's <paramref name="what"/>, looking at it (as dt screenshot --talk does).</summary>
    static Town StandAt(PrototypeSession s, string what, int which = 0)
    {
        var town = s.World.Town!;
        var (feet, look) = Staging.TownStand(town, what, which);
        var to = look - (feet + Double3.Up * s.Train.Dynamics.Tuning.Pick.EyeHeight);
        s.Player = s.Player with
        {
            Parent = PlayerState.World,
            Surface = Surface.Ground,
            Position = feet,
            Yaw = Math.Atan2(-to.X, -to.Z),
            Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z)),
            Velocity = default,
        };
        return town;
    }

    [Fact]
    public void ANightStartsInATownWithPeopleInIt()
    {
        var s = Night();
        var town = Assert.IsType<Town>(s.World.Town);
        Assert.Equal(s.Route!.Plan!.Fortress.Name, town.Plan.Name);
        Assert.True(town.Plan.People.Count >= 10);
        // Its walls stand with the stops' (the world is solid: App. F.1).
        Assert.Contains(town.Walls[0], s.Train.Walls!.All);
    }

    [Fact]
    public void TalkingToSomebodyHearsTheirLinesInTurn()
    {
        var s = Night();
        var town = StandAt(s, "person", 3);
        var person = town.Plan.People[3];
        Assert.Equal($"{person.Name.ToUpperInvariant()}   TALK : [E]", Hud.Prompt(s));
        var target = Hud.TownTarget(s);
        Assert.Equal(new TownTarget(TownTargetKind.Person, person.Id), target);
        var talk = new TownTalk();
        Assert.True(talk.Use(town, target, 10));
        // It types out: part of the line at first, all of it soon after.
        var first = talk.Card(town, 10.2)!;
        Assert.Equal(TownCardKind.Speech, first.Kind);
        Assert.StartsWith(person.Name, first.Heading);
        Assert.True(first.Text.Length < person.Lines[0].Length);
        Assert.Equal(person.Lines[0], talk.Card(town, 30)!.Text);
        // Use again: the next line; and round again after the last.
        talk.Use(town, target, 31);
        Assert.Equal(person.Lines[1], talk.Card(town, 60)!.Text);
        for (int i = 2; i <= person.Lines.Count; i++)
            talk.Use(town, target, 61);
        Assert.Equal(person.Lines[0], talk.Card(town, 90)!.Text);
        // Said and left long enough, the card goes.
        var eye = s.Player.Position + Double3.Up * s.Train.Dynamics.Tuning.Pick.EyeHeight;
        talk.Step(town, eye, 62);
        Assert.NotNull(talk.Open);
        talk.Step(town, eye, 61 + person.Lines[0].Length / town.Tuning.TypePerSecond + town.Tuning.LingerSeconds + 1);
        Assert.Null(talk.Open);
    }

    [Fact]
    public void TheBoardIsReadNoticeByNoticeAndWalkingOffPutsItDown()
    {
        var s = Night();
        var town = StandAt(s, "board");
        Assert.StartsWith("THE BOARD (", Hud.Prompt(s));
        var talk = new TownTalk();
        var target = Hud.TownTarget(s);
        for (int i = 0; i < town.Notices.Count; i++)
        {
            Assert.True(talk.Use(town, target, i));
            var card = talk.Card(town, i)!;
            Assert.Equal(TownCardKind.Paper, card.Kind);
            Assert.Equal(town.Notices[i].Title, card.Heading);
            Assert.Equal(town.Notices[i].Text, card.Text);
        }
        // Past the last notice, Use puts the board down.
        Assert.True(talk.Use(town, target, 9));
        Assert.Null(talk.Open);
        // Open again, then walked away from: closed.
        talk.Use(town, target, 10);
        var far = town.Where(target!.Value) + Double3.Up * 0 + town.Direction(town.Plan.Fixtures.First(f => f.Kind == "board").S, 1, 0) * (town.Tuning.Reach.CloseBeyond + 1);
        talk.Step(town, far, 11);
        Assert.Null(talk.Open);
    }

    [Fact]
    public void ALampAtSomebodysFeetDoesNotTakeThePressMeantForThem()
    {
        var s = Night();
        var town = StandAt(s, "person", 2);
        var person = town.Plan.People[2];
        // A lamp lying in front of them, in the hands' reach.
        var at = s.Player.Position + (town.Feet(person) - s.Player.Position) * 0.4 + Double3.Up * 0.2;
        var lamp = s.World.Bodies.SpawnItem(at, person.S, Sim.Physics.BodyKind.Lamp);
        Assert.Equal(PlayerState.World, lamp.Parent);
        Assert.Equal($"{person.Name.ToUpperInvariant()}   TALK : [E]", Hud.Prompt(s));
        Assert.NotNull(Hud.TownTarget(s));
    }

    [Fact]
    public void LookingAwayAUsePressIsNotTheTownsAndClosesItsCard()
    {
        var s = Night();
        var town = StandAt(s, "person", 2);
        var talk = new TownTalk();
        talk.Use(town, Hud.TownTarget(s), 0);
        Assert.NotNull(talk.Open);
        s.Player = s.Player with { Yaw = s.Player.Yaw + Math.PI };
        Assert.Null(Hud.TownTarget(s));
        Assert.False(talk.Use(town, Hud.TownTarget(s), 1));
        Assert.Null(talk.Open);
    }

    [Fact]
    public void EveryWordInATownIsInTheFont()
    {
        var font = BitmapFont.Default;
        var unknown = font.Glyph('?');
        var towns = TownContent.Load(Content)!;
        for (int seed = 1; seed <= 60; seed++)
        {
            var plan = TownGenerator.Generate(towns, new TownSite($"Fort {towns.Surnames[seed % towns.Surnames.Count]}", "coal", 1100, (ulong)seed, []));
            var words = plan.People.SelectMany(p => p.Lines.Append(p.Name).Append(p.Title))
                .Concat(plan.Papers.SelectMany(p => new[] { p.Title, p.Text }))
                .Concat(plan.Fixtures.SelectMany(f => new[] { f.Name, f.Text }))
                .Concat(plan.Buildings.SelectMany(b => new[] { b.Name, b.Knock }))
                .Concat(plan.Houses.SelectMany(h => new[] { TownTalk.HouseName(h), h.Text }))
                .Append(plan.Name).Append(plan.Law);
            foreach (string text in words)
                foreach (char c in text)
                    Assert.True(c == '?' || !ReferenceEquals(font.Glyph(c), unknown), $"no glyph for '{c}' in \"{text}\"");
        }
    }
}
