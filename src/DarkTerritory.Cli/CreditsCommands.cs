using System.Text.Json;
using Ballast;
using DarkTerritory.Game;

/// <summary>
/// `dt credits [--notices | --write]` (note 390): everyone whose work is in the game, as the credits screen lists them after
/// the opera, from the content's own provenance (<see cref="Credits"/>). <c>--notices</c> prints THIRD-PARTY-NOTICES.txt;
/// <c>--write</c> rewrites content/credits/THIRD-PARTY-NOTICES.txt (after a model, sound pack or library comes in or goes),
/// which the build carries and tools/package.sh puts beside the game. Exit 1 if any source's licence isn't described in
/// credits.json, so it can't be shipped uncredited.
/// </summary>
static class CreditsCommands
{
    public static int Run(string content, string[] args)
    {
        var undescribed = Credits.Undescribed(content);
        if (undescribed.Count > 0)
        {
            Console.Error.WriteLine($"licences named by the provenance but not in {Credits.File}: {string.Join(", ", undescribed)}");
            return 1;
        }
        if (args.Contains("--notices"))
        {
            Console.Write(Credits.Notices(content));
            return 0;
        }
        if (args.Contains("--write"))
        {
            string path = Path.Combine(content, Credits.NoticesFile);
            File.WriteAllText(path, Credits.Notices(content));
            Console.WriteLine(JsonSerializer.Serialize(new { written = Path.GetFullPath(path) }, DataFile.Options));
            return 0;
        }
        var sections = Credits.Load(content);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            owed = sections.Sum(s => s.Lines.Count(l => l.Owed)),
            credits = sections.Sum(s => s.Lines.Count),
            sections,
        }, DataFile.Options));
        return 0;
    }
}
