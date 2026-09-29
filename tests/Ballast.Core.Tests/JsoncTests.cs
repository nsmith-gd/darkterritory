namespace Ballast.Core.Tests;

public class JsoncTests
{
    const string Doc = """
        // Header: cites spec B.8.
        {
          // How fast it pours.
          "pourPerSecond": 12,
          "chute": {
            /* reach, metres */ "leverReach": 2.2,
            "name": "coal \"tower\"",
            "on": true
          },
          "rows": [ { "cars": 3, "accel": 0.9 }, { "cars": 6, "accel": 0.62 } ],

          "none": null
        }
        """;

    [Fact]
    public void FindsEveryScalarWithItsPathAndComment()
    {
        var s = Jsonc.Scalars(Doc).ToDictionary(x => x.Path);
        Assert.Equal("12", s["pourPerSecond"].Text);
        Assert.Equal("How fast it pours.", s["pourPerSecond"].Comment);
        Assert.Equal("reach, metres", s["chute.leverReach"].Comment);
        Assert.Equal(JsoncKind.String, s["chute.name"].Kind);
        Assert.Equal(JsoncKind.Bool, s["chute.on"].Kind);
        Assert.Equal("0.62", s["rows[1].accel"].Text);
        Assert.Equal(JsoncKind.Null, s["none"].Kind);
    }

    [Fact]
    public void SettingAValueChangesOnlyThatValue()
    {
        string edited = Jsonc.Set(Doc, "rows[1].accel", 0.65);
        Assert.Equal(Doc.Replace("\"accel\": 0.62", "\"accel\": 0.65"), edited);
        edited = Jsonc.Set(edited, "chute.name", "the tipple");
        Assert.Contains("\"name\": \"the tipple\"", edited);
        Assert.Contains("// Header: cites spec B.8.", edited);
        Assert.Contains("/* reach, metres */", edited);
        Assert.Contains("// How fast it pours.", edited);
    }

    [Fact]
    public void EveryShippedContentFileParses()
    {
        foreach (var file in Directory.EnumerateFiles(DataFile.FindContentRoot(), "*.json", SearchOption.AllDirectories))
        {
            var scalars = Jsonc.Scalars(File.ReadAllText(file));
            Assert.NotEmpty(scalars);
        }
    }

    [Fact]
    public void AMissingPathSaysSo() =>
        Assert.Throws<KeyNotFoundException>(() => Jsonc.Set(Doc, "chute.nope", 1));
}
