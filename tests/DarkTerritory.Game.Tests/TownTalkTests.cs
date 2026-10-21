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
    public void TheBlacksmithIsClearlyTheBlacksmithAndSaysWhatTheySell()
    {
        // Note 687: whatever the town calls them, the prompt says blacksmith; in a campaign night's yard, the hold that opens
        // their catalogue too (a night that isn't a campaign's sells nothing: its upgrades aren't anyone's).
        var s = Night();
        int smith = s.World.Town!.Plan.People.Single(p => p.Role == "blacksmith").Id;
        var town = StandAt(s, "person", smith);
        string name = town.Plan.People[smith].Name.ToUpperInvariant();
        Assert.False(TownTalk.Selling(s));
        Assert.Equal($"{name}, BLACKSMITH   TALK : [E]", Hud.Prompt(s));
        var target = Hud.TownTarget(s);
        Assert.Equal(new TownTarget(TownTargetKind.Person, smith), target);
        // Note 904 (the director, 16 Oct: "I would have assumed talking to him, I get the upgrades"): selling, the press is the
        // catalogue's; not selling, it's a word, and their card says where and when the wares are.
        Assert.Equal($"{name}, BLACKSMITH   UPGRADES : [E]", TownTalk.Prompt(town, target!.Value, selling: true));
        Assert.True(TownTalk.OpensForge(town, target, selling: true));
        Assert.False(TownTalk.OpensForge(town, target, selling: false));
        var talk = new TownTalk();
        Assert.True(talk.Use(town, target, 10));
        var card = talk.Card(town, 30)!;
        Assert.Contains(card.Text, town.Plan.People[smith].Lines);
        Assert.Contains(TownTalk.ForgeLater, card.Footer, StringComparison.Ordinal);
        // Nobody else's press opens a forge.
        var other = new TownTarget(TownTargetKind.Person, town.Plan.People.First(p => p.Role != "blacksmith").Id);
        Assert.False(TownTalk.OpensForge(town, other, selling: true));
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
        // (Typed in their temperament's voice, note 963: as long as that takes, then the linger.)
        talk.Step(town, eye, 61 + town.Tuning.VoiceOf(person).Seconds(person.Lines[0], town.Tuning.TypePerSecond) + town.Tuning.LingerSeconds + 1);
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

    /// <summary>
    /// Note 971 (docs/design/townsfolk.md, "Not yet": "The crew could hear a different line from someone who's been told 'the
    /// custom' by another townsperson first"): the custom's told once in a town. Whoever tells it first says it; after that,
    /// anybody whose line it would be says what their temperament makes of its having been told, them too the next time round.
    /// </summary>
    [Fact]
    public void TheCustomsToldOnceAndAfterThatTheyKnowYouKnow()
    {
        var s = Night();
        var town = s.World.Town!;
        var tellers = town.Plan.People.Where(p => p.Custom.Count > 0).Take(2).ToList();
        Assert.Equal(2, tellers.Count);
        var talk = new TownTalk();
        string Hear(Townsperson p, double at)
        {
            // Pressed till their custom line comes up (each press the next line), and read whole.
            var target = new TownTarget(TownTargetKind.Person, p.Id);
            for (int k = 0; k < p.Lines.Count; k++)
            {
                talk.Close();
                talk.Use(town, target, at);
                if (talk.Page == p.Custom[0])
                    return talk.Card(town, at + 120)!.Text;
            }
            throw new InvalidOperationException("never came to it");
        }
        Assert.False(talk.Told(town));
        Assert.Equal(tellers[0].Lines[tellers[0].Custom[0]], Hear(tellers[0], 10));
        Assert.True(talk.Told(town));
        Assert.Equal(TownTalk.Told(town.Tuning, tellers[1]), Hear(tellers[1], 200));
        Assert.NotEqual(tellers[1].Lines[tellers[1].Custom[0]], Hear(tellers[1], 400));
        Assert.Equal(TownTalk.Told(town.Tuning, tellers[0]), Hear(tellers[0], 600));
    }

    /// <summary>
    /// Note 971 (townsfolk.md, "Not yet": "Households don't argue"): a house whose two at most odds are far apart quarrels on
    /// its own beat, overheard as a card near its front while it does, the two back and forth; nothing further off, or between.
    /// </summary>
    [Fact]
    public void AHouseholdQuarrelsWhereYouCanOverhearIt()
    {
        var s = Night();
        var town = s.World.Town!;
        var quarrel = Assert.IsType<TownQuarrel>(town.Plan.Quarrels.FirstOrDefault());
        var q = town.Tuning.Quarrels;
        var house = town.Plan.Houses[quarrel.House];
        var (fs, fd) = house.Rail(0, 0);
        var front = town.World(fs, fd);
        double on = Enumerable.Range(0, (int)q.Every).Select(k => (double)k).First(t => TownTalk.QuarrelLine(q, quarrel, t) == 0);
        var card = TownTalk.Overheard(town, front + Double3.Up * 1.6, on);
        Assert.NotNull(card);
        Assert.Contains(house.Family, card.Heading);
        Assert.Equal($"{town.Plan.People[quarrel.A].Name}: {quarrel.Lines[0].Line}", card.Text);
        // The other one answers.
        Assert.Contains(town.Plan.People[quarrel.B].Name, TownTalk.Overheard(town, front, on + q.LineSeconds)!.Text);
        // Out of earshot, nothing; and between their quarrels, nothing.
        var away = front + (front - town.World(house.S, house.D)).Normalized * (q.Hear + 5);
        Assert.Null(TownTalk.Overheard(town, away, on) is { } far && far.Text.Contains(town.Plan.People[quarrel.A].Name) ? far : null);
        Assert.Equal(-1, TownTalk.QuarrelLine(q, quarrel, on + q.Lasts + 1));
    }
}
