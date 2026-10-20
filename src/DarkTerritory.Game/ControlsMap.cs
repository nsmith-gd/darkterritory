namespace DarkTerritory.Game;

/// <summary>
/// The controls map (note 898; the director, 20 Oct: "update the control map in the github, it's out of date"): every
/// control on its default key, and the keys that aren't controls, written from <see cref="Controls"/> into README.md's table
/// and the players' tools/package/PLAYING.txt, so neither can say a key the game doesn't. <c>dt controls --write</c> rewrites
/// both; <c>ControlsMapTests</c> fails when either disagrees with the defaults.
/// </summary>
public static class ControlsMap
{
    /// <summary>README.md's generated table sits between these two lines.</summary>
    public const string ReadmeBegin = "<!-- controls map: written by `dt controls --write` from Controls.Defaults (note 898); don't edit by hand -->",
        ReadmeEnd = "<!-- end of the controls map -->";
    /// <summary>PLAYING.txt's generated keys run from this heading to the line before <see cref="TextEnd"/>.</summary>
    public const string TextBegin = "Keys (the defaults: SETTINGS > CONTROLS changes any of them)", TextEnd = "Working the train";

    public const string Readme = "README.md", Playing = "tools/package/PLAYING.txt";

    /// <summary>The map as a Markdown table: the controls in the menu's order, then the fixed keys.</summary>
    public static string Markdown()
    {
        var lines = new List<string> { "| Key | Action |", "|---|---|" };
        lines.AddRange(Enum.GetValues<Control>().Select(c => $"| {Key(Controls.Defaults[c])} | {Controls.Help(c)} |"));
        lines.AddRange(Controls.Fixed.Select(f => $"| {f.Key} | {f.Does} (not rebindable) |"));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The map as PLAYING.txt's plain text: its heading, a line a control, then the fixed keys.</summary>
    public static string Text()
    {
        var controls = Enum.GetValues<Control>().Select(c => (Key: Key(Controls.Defaults[c]), Does: Controls.Help(c))).ToList();
        int width = controls.Concat(Controls.Fixed).Max(r => r.Key.Length);
        var lines = new List<string> { TextBegin };
        lines.AddRange(controls.Select(r => $"  {r.Key.PadRight(width)}  {r.Does}"));
        lines.Add("These keys are fixed:");
        lines.AddRange(Controls.Fixed.Select(r => $"  {r.Key.PadRight(width)}  {r.Does}"));
        return string.Join('\n', lines) + "\n\n";
    }

    /// <summary>A key as the map writes it: the menu's name, in words a reader knows (LEFT ALT → Left Alt).</summary>
    public static string Key(string key)
    {
        string label = Controls.KeyLabel(key);
        return label.Length <= 1 ? label : string.Join(' ', label.Split(' ').Select(w => w.Length <= 1 ? w : w[0] + w[1..].ToLowerInvariant()));
    }

    /// <summary>README.md with its table replaced by <see cref="Markdown"/>; null if the markers aren't there.</summary>
    public static string? SpliceReadme(string readme) => Splice(readme, ReadmeBegin + "\n", "\n" + ReadmeEnd, Markdown().TrimEnd('\n'));

    /// <summary>PLAYING.txt with its keys replaced by <see cref="Text"/>; null if the headings aren't there.</summary>
    public static string? SplicePlaying(string playing)
    {
        int at = playing.IndexOf(TextBegin, StringComparison.Ordinal);
        int end = at < 0 ? -1 : playing.IndexOf("\n" + TextEnd, at, StringComparison.Ordinal);
        return end < 0 ? null : playing[..at] + Text() + playing[(end + 1)..];
    }

    static string? Splice(string text, string begin, string end, string with)
    {
        int at = text.IndexOf(begin, StringComparison.Ordinal);
        int stop = at < 0 ? -1 : text.IndexOf(end, at, StringComparison.Ordinal);
        return stop < 0 ? null : text[..(at + begin.Length)] + with + text[stop..];
    }
}
