using System.Numerics;
using Ballast.Render;
using DarkTerritory.Game;

namespace DarkTerritory.Dev;

/// <summary>
/// The developer tools a developer build of the app carries (ARCHITECTURE §8 note 514), started once at launch. A player's
/// build has none of this: the app reaches it only under <c>#if DEVTOOLS</c>, which only a DevTools build defines, and
/// tools/package.sh builds players' builds without it (<c>dt build check</c> proves it of the package).
/// </summary>
public sealed class DevTools
{
    /// <param name="args">The app's command line: <c>--no-dev-mark</c> hides the corner's mark (for footage).</param>
    public static DevTools Start(string[] args) => new(args);

    DevTools(string[] args)
    {
        Marked = !args.Contains("--no-dev-mark");
        var (_, commit) = Report.Build();
        Label = $"DEV {(commit.Length > 7 ? commit[..7] : commit).ToUpperInvariant()}";
    }

    /// <summary>The corner's mark is drawn: whoever's looking (the director's test builds, a screenshot) knows it's a dev build.</summary>
    public bool Marked { get; }

    /// <summary>"DEV" and the build's commit.</summary>
    public string Label { get; }

    static readonly Vector4 MarkColour = new(0.75f, 0.75f, 0.75f, 0.45f);

    /// <summary>The mark, small and dim in the top right, over whatever the frame shows (the HUD or a menu).</summary>
    public void Draw(Overlay overlay, int width, int height)
    {
        if (Marked)
            overlay.TextRight(width - 3, 3, Label, MarkColour);
    }
}
