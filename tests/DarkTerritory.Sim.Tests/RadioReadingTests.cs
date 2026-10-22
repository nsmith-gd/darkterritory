using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD §9 (ARCHITECTURE §8 note 178): the dispatcher reads the crew out by name as a manifest, with the coal in the same
/// breath; the clerk tallies the run car by car and body by body, each fee flat.
/// </summary>
public class RadioReadingTests
{
    static World World()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, 600, Tuning.Boiler), Tuning.Combat);
        world.Names[0] = "Dave";
        world.Names[1] = "Priya";
        return world;
    }

    [Fact]
    public void TheManifestIsTheCrewByNameThenTheCoal()
    {
        var w = World();
        var lines = Radio.Manifest(w, [0, 1, 2]);
        Assert.Equal("Yard to consist. Manifest follows.", lines[0]);
        Assert.Equal(["Crew: Dave.", "Crew: Priya.", "Crew: Crew 2."], lines.Skip(1).Take(3));
        Assert.Contains($"Coal: {w.Train.Boiler.Tender:0}.", lines);
        Assert.Contains("Cars: 4.", lines);
        Assert.Equal("Gates open. Yard out.", lines[^1]);
    }

    [Fact]
    public void TheTallyReadsEachBodyAsFlatAsTheCargo()
    {
        var report = new RunReport(RunEnd.Delivered, 1000, 12, 3, 1, 2, 1240, 40, 12, 30, 1158, 3, 1, Mail: 90)
        {
            Lines =
            [
                new ReportLine(IncidentKind.Death, "Sam", "Taken by the Choir ...", 350, 263),
                new ReportLine(IncidentKind.Death, "Ana", "Mauled by the hounds ...", 350, 0),
                new ReportLine(IncidentKind.Rescue, "Dave", "Freed ..."),
            ],
        };
        var lines = Radio.Tally(report);
        // Note 766: the cargo alone (the gross less the mail, read on its own line).
        Assert.Equal("Cars delivered: 3. Cargo: 1150.", lines[1]);
        Assert.Contains("Cars lost: 1.", lines);
        Assert.Contains("Sam. Body recovered. Fee 350. Refund 263.", lines);
        Assert.Contains("Ana. Body not recovered. Fee 350.", lines);
        Assert.Contains("Mail: 90.", lines);
        Assert.Equal("Net: 1158. Next.", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Contains("Dave"));
    }

    [Fact]
    public void ANightInTheRedSaysWhatCostMostAndItsFiguresAddUp()
    {
        // Note 766, a night like the director's (13 Oct: "Powder and shot zero. Repairs 91 ... net negative 1607"): nothing
        // paid, a crewmate's fee the most of it.
        var report = new RunReport(RunEnd.CrewLost, 1500, 9, 0, 0, 0, 0, 16, 0, 91, -1607, 0, 1, Deaths: 1, CrewLossFees: 1500)
        {
            Lines = [new ReportLine(IncidentKind.Death, "Nick", "Grabbed ...", 1500, 0)],
        };
        var lines = Radio.Tally(report);
        Assert.Contains("Nick. Body not recovered. Fee 1500.", lines);
        Assert.Contains("Coal: 16. Powder and shot: 0. Repairs: 91.", lines);
        Assert.Equal("Net: -1607. Most of it the crew lost, 1500. Next.", lines[^1]);
        var ledger = Radio.Ledger(report);
        Assert.Equal([("the crew lost", -1500.0), ("coal", -16.0), ("repairs", -91.0)], ledger);
        Assert.Equal(report.Net, ledger.Sum(i => i.Amount));
        // In the black, the net alone, as before.
        Assert.Equal("Net: 1158. Next.", Radio.Tally(report with { Net = 1158 })[^1]);
        // The stranded's tow and the town's brigade are read too: they were in the net without a word.
        var towed = Radio.Tally(report with { Recovery = 420 });
        Assert.Contains("Recovery: 420.", towed);
        var brought = new RunReport(RunEnd.Delivered, 1, 1, 1, 0, 1, 500, 0, 0, 0, 425, 1, 0) { BroughtInFees = 75 };
        Assert.Contains("Brigade fees: 75.", Radio.Tally(brought));
        Assert.Equal(brought.Net, Radio.Ledger(brought).Sum(i => i.Amount));
    }

    [Fact]
    public void ALineAtATimeEachTypedOut()
    {
        var t = new RadioTuning(LineSeconds: 2);
        string[] lines = ["one", "two", "three"];
        Assert.Equal((0, 0.0), Radio.Reading(lines, -1, t));
        Assert.Equal(1, Radio.Reading(lines, 0.5, t).Lines);
        Assert.InRange(Radio.Reading(lines, 0.5, t).Typed, 0.3, 0.5);
        Assert.Equal((2, 1.0), Radio.Reading(lines, 3.9, t));
        Assert.Equal((3, 1.0), Radio.Reading(lines, 7, t));
        Assert.Equal(8, Radio.Length(lines, t));
    }

    [Fact]
    public void SpokenEachLineTakesAsLongAsItsSaidThenThePause()
    {
        // Note 240: with the yard's voice, a line's turn is what saying it takes and the pause after, never under lineSeconds,
        // and the card's typed as it's said.
        var t = new RadioTuning(LineSeconds: 1.6, PauseSeconds: 0.5);
        string[] lines = ["short", "a much longer line", "end"];
        // Said at 0.2 s a character: 1 s, 3.6 s and 0.6 s.
        var times = Radio.Times(lines, t, l => l.Length * 0.2);
        Assert.Equal(new[] { 1.6, 4.1, 1.6 }, times.Select(x => Math.Round(x, 9)));
        Assert.Equal(1, Radio.Reading(lines, 1.5, t, times).Lines);
        Assert.Equal(2, Radio.Reading(lines, 1.7, t, times).Lines);
        // Typed in step with the voice: halfway through saying it, half typed; all of it once it's said, before the pause.
        Assert.InRange(Radio.Reading(lines, 1.6 + 1.8, t, times).Typed, 0.49, 0.51);
        Assert.Equal(1.0, Radio.Reading(lines, 1.6 + 3.7, t, times).Typed);
        Assert.Equal(2, Radio.Reading(lines, 1.6 + 4.0, t, times).Lines);
        Assert.Equal(3, Radio.Reading(lines, 1.6 + 4.2, t, times).Lines);
        Assert.Equal(1.6 + 4.1 + 1.6 + 1.6, Radio.Length(lines, t, times), 9);
    }
}
