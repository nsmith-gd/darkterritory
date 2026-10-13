using System.Numerics;
using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;
using Xunit;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The towns' flags (queue #405, note 674; the director, 13 Oct: "it needs to be waving if it's going to be there. And we
/// need to do a series of flag designs for the towns").
/// </summary>
public class FlagTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly FlagTuning Tuning = new();

    [Fact]
    public void EveryCustomInEveryTradeHasItsOwnFlag()
    {
        var book = FlagBook.Load(Content);
        var towns = TownContent.Load(Content)!;
        Assert.NotEmpty(book.Flags);
        Assert.Equal(book.Flags.Count, book.Flags.Select(f => (f.X, f.Y)).Distinct().Count());
        foreach (var culture in towns.Writing.Cultures)
            foreach (var industry in towns.Writing.Industries.Values)
            {
                var flag = book.For(culture.Id, industry.Name, "Fort Anywhere");
                Assert.NotNull(flag);
                Assert.Equal(culture.Id, flag.Culture);
                // The trade the town lives by, not one picked by its name.
                Assert.Contains(flag.Field, industry.Name, StringComparison.OrdinalIgnoreCase);
            }
        // Every cell lies inside the atlas the texture index says it has.
        Assert.All(book.Flags, f => Assert.True(f.X >= 0 && f.Y >= 0 && f.X + f.W <= book.Size && f.Y + f.H <= book.Size, $"{f.Culture}-{f.Trade}"));
    }

    [Fact]
    public void TheSameTownAlwaysFliesTheSameFlag()
    {
        var book = FlagBook.Load(Content);
        // A custom the series hasn't got yet: one of its trade's, chosen by the town's name, the same every time.
        var a = book.For("unheard-of", "a pit town", "Fort Grieve");
        Assert.Equal(a, book.For("unheard-of", "a pit town", "Fort Grieve"));
        Assert.Equal("coal", a!.Trade);
        var picks = new[] { "Fort Grieve", "Fort Hale", "Fort Mabou", "Fort Tusket", "Fort Wedge", "Fort Ingonish" }
            .Select(n => book.For("unheard-of", "a pit town", n)!.Culture).Distinct().Count();
        Assert.True(picks > 1, "towns' names choose among the trade's flags");
    }

    [Fact]
    public void TheClothIsSkinnedAlongItsChain()
    {
        var book = FlagBook.Load(Content);
        var cloth = FlagKit.Cloth(null, book.Flags[0], book, Tuning);
        Assert.NotNull(cloth.Skin);
        Assert.All(cloth.Skin!, w =>
        {
            Assert.Equal(1f, w.Weights.X + w.Weights.Y + w.Weights.Z + w.Weights.W, 3);
            Assert.InRange(w.Joints.X, 0, FlagKit.Bones - 1);
            Assert.InRange(w.Joints.Y, 0, FlagKit.Bones - 1);
        });
    }

    // Where the fly's far corners are (object space, the hoist at the origin) posed in a wind at a time.
    static (Vector3 Top, Vector3 Bottom) Fly(Vector3 wind, double time)
    {
        var skin = new Matrix4x4[FlagKit.Bones];
        FlagKit.Pose(Tuning, wind, 0.7f, time, 1.3f, skin);
        var last = skin[FlagKit.Bones - 1];
        return (Vector3.Transform(new Vector3(Tuning.Length, 0, 0), last), Vector3.Transform(new Vector3(Tuning.Length, -Tuning.Height, 0), last));
    }

    [Fact]
    public void InACalmItHangsAndInAGaleItFliesDownwind()
    {
        var wind = new WindTuning();
        var (calmTop, _) = Fly(wind.Of(0) * 0.3f, 0.4);
        var (galeTop, _) = Fly(wind.Of(1), 0.4);
        var downwind = Vector3.Normalize(wind.Of(1) with { Y = 0 });
        // Slack: the fly's top corner well down the pole, not out in the air.
        Assert.True(calmTop.Y < -0.6f * Tuning.Length, $"calm fly at {calmTop}");
        // Flying: out downwind most of its length, about level.
        Assert.True(Vector3.Dot(galeTop, downwind) > 0.75f * Tuning.Length, $"gale fly at {galeTop}");
        Assert.True(MathF.Abs(galeTop.Y) < 0.35f * Tuning.Length, $"gale fly at {galeTop}");
    }

    [Fact]
    public void ItsNeverStill()
    {
        var wind = new WindTuning();
        foreach (float w in new[] { 0f, 0.5f, 1f })
        {
            var a = Fly(wind.Of(w), 1.0);
            var b = Fly(wind.Of(w), 1.25);
            Assert.True((a.Top - b.Top).Length() + (a.Bottom - b.Bottom).Length() > 0.05f, $"wind {w}: the fly didn't move in a quarter second");
        }
    }
}
