using System.Globalization;
using System.Text.RegularExpressions;
using Ballast;
using DarkTerritory.Dev.Encounters;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// `dt director report` (#613, note 930): tools/director/bands.json holds the director's brief's §7 targets as written, and a
/// sweep's groups are scored against them, each session by its own band.
/// </summary>
public class BalanceBandsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly BalanceBands Bands = BalanceBands.Load(BalanceBands.DefaultPath(Content));
    static readonly string Brief = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Content)!, "docs", "design", "spawn-director-overhaul.md"));

    [Fact]
    public void TheBandsAreTheBriefsSection7()
    {
        // §7's table, read from the brief itself: "| Solo, 2–3 cars | 5–8 |" and so on, in order.
        var rows = Regex.Matches(Brief, @"^\| (Solo|\d players), (\d+)(?:–(\d+)|\+)? cars \| (\d+)–(\d+) \|$", RegexOptions.Multiline);
        Assert.Equal(4, rows.Count);
        Assert.Equal(4, Bands.Encounters.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var m = rows[i].Groups;
            int crew = m[1].Value == "Solo" ? 1 : int.Parse(m[1].Value.Split(' ')[0], CultureInfo.InvariantCulture);
            var band = Bands.Encounters[i];
            Assert.Equal(crew, band.Crew);
            Assert.Equal(double.Parse(m[2].Value, CultureInfo.InvariantCulture), band.Cars[0]);
            // "8 cars" is exactly eight; "12+ cars" has no top.
            if (m[3].Success)
                Assert.Equal(double.Parse(m[3].Value, CultureInfo.InvariantCulture), band.Cars[1]);
            else if (rows[i].Value.Contains('+'))
                Assert.True(band.Cars[1] >= 18, "12+ cars takes the matrix's 18");
            else
                Assert.Equal(band.Cars[0], band.Cars[1]);
            Assert.Equal([double.Parse(m[4].Value, CultureInfo.InvariantCulture), double.Parse(m[5].Value, CultureInfo.InvariantCulture)], band.Per20Min);
        }
        // "First noticeable threat generally occurs within 90–150 seconds after leaving the safe departure area."
        Assert.Contains("within 90–150 seconds", Brief);
        Assert.Equal([90.0, 150.0], Bands.FirstThreatSeconds);
        Assert.Contains("more frequent toward the latter half", Brief);
        Assert.True(Bands.LateAtLeastEarly);
    }

    [Fact]
    public void EachSessionIsJudgedByItsOwnBandAndAGroupNoneHoldsIsNot()
    {
        var solo = Group(crew: 1, cars: 3, per20: 6, first: 120, early: 2, late: 4);
        var score = SweepScoring.Score(Bands, solo);
        Assert.Equal(score.Judged, score.Passed);
        Assert.Equal("5–8", score.Scores[0].Band);
        // Too few for a crew of four, though six would do for one.
        var four = SweepScoring.Score(Bands, Group(crew: 4, cars: 8, per20: 6, first: 120, early: 2, late: 4));
        Assert.False(four.Scores[0].Pass);
        // Solo on 8 cars: no §7 session holds it, so its encounters aren't judged; the rest still are.
        var long_ = SweepScoring.Score(Bands, Group(crew: 1, cars: 8, per20: 1, first: 300, early: 4, late: 1));
        Assert.Null(long_.Scores[0].Pass);
        Assert.False(long_.Scores[1].Pass);
        Assert.False(long_.Scores[2].Pass);
        // A group with no first threat at all is outside its band, not unjudged.
        Assert.False(SweepScoring.Score(Bands, Group(1, 3, 6, null, 2, 4)).Scores[1].Pass);
    }

    [Fact]
    public void OldAgainstNewSideBySide()
    {
        var old = Group(1, 3, 2, 200, 3, 1);
        var now = Group(1, 3, 6, 120, 2, 4);
        string table = SweepScoring.Table(Bands, [now], [old]);
        Assert.Contains("| measure | band | old | new |", table);
        Assert.Contains("| encounters / 20 min | 5–8 | 2 🔴 | 6 🟢 |", table);
        Assert.Contains("In band: new 5 of 5; old 2 of 5", table);
    }

    /// <summary>
    /// Note 930 (n1-C2): the late half judged per minute open to spawning: the terminus approach's held seconds come out of
    /// the late half, the grace's out of the early. A night whose raw late half is short of its early can be busier per open
    /// minute, and is judged so; the raw count is shown, unbanded.
    /// </summary>
    [Fact]
    public void TheLateHalfIsJudgedPerMinuteOpenToSpawning()
    {
        Assert.True(Bands.LatePerOpenMinute);
        // A 20-minute night: 600 s a half. The early half had 60 s of grace (540 open) and 6 encounters; the late half had
        // 300 s held in the final stretch and terminus_safe (300 open) and 4: fewer raw, but 0.8 a minute against 0.67.
        var cell = new SweepCell("frontier:7", 2, 5, 1);
        var night = new SweepRow(cell, "Delivered", true, 20, 8, 0, 1, 5, 0, 10, new Dictionary<string, int>(), 10,
            new Dictionary<string, int>(), 4, 6, 4, 100, 80, 80, 0, 2, 0, 0, 0,
            new Dictionary<string, int> { ["grace"] = 60, ["final stretch"] = 200, ["banned (terminus_safe)"] = 100, ["cooldown"] = 50 }, 1);
        var rates = SweepScoring.OpenRates([night]);
        Assert.Equal((0.67, 0.8), rates[cell.Key]);
        var group = DirectorSweep.Groups([night]).Single();
        var score = SweepScoring.Score(Bands, group, rates[cell.Key]);
        Assert.Contains(score.Scores, s => s.Measure == "late − early / open min" && s.Pass == true);
        Assert.Contains(score.Scores, s => s.Measure == "late half − early (raw)" && s.Value == -2 && s.Pass is null);
        // With no nights to measure it from, the raw count is judged, as before.
        Assert.Contains(SweepScoring.Score(Bands, group).Scores, s => s.Measure == "late half − early" && s.Pass == false);
    }

    static SweepGroup Group(int crew, int cars, double per20, double? first, double early, double late) =>
        new($"frontier:7 crew {crew} cars {cars}", "frontier:7", crew, cars, 4, per20, (int)per20, (int)per20, per20, 3, early, late, first,
            60, 80, 1, 2, 0.1, 0, 1, 1, 20, per20, 0);
}
