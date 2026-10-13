using Ballast;
using Ballast.Audio;

namespace DarkTerritory.Game;

/// <summary>A sound playing, as the captions hear it: its name, where it is, and how loud it is at the ear.</summary>
public readonly record struct Heard(string Name, Double3 Position, float Gain);

/// <summary>
/// CAPTIONS (note 349; GDD §32 "Accessibility"): the sounds worth hearing that are heard now, what each is and where, in
/// fine print over the hotbar: "[TIPTOEING, BEHIND]". What a sound is, never which creature or what to do (content/ui/
/// captions.json). Fed each frame from the mixer's voices, at the gain the mixer last heard each at.
/// </summary>
public sealed class Captions
{
    public const string File = "ui/captions.json";

    public sealed record Data
    {
        public double Threshold { get; init; } = 0.03;
        public double HoldSeconds { get; init; } = 2.5;
        public int Lines { get; init; } = 3;
        public Dictionary<string, string> Sounds { get; init; } = [];
    }

    public Data Tuning { get; }
    // What's been heard, by caption: when last, from where, and how loud then.
    readonly Dictionary<string, (double At, string Where, float Gain)> _heard = [];

    public Captions(Data tuning) => Tuning = tuning;

    public static Data Load(string content) =>
        System.IO.File.Exists(Path.Combine(content, File)) ? DataFile.Load<Data>(Path.Combine(content, File)) : new();

    /// <summary>
    /// The caption for a sound's name: its own, or the nearest name it's a variant of, a '.' at a time
    /// ("cs-ribbits.hop-land.ground" is "cs-ribbits.hop-land"'s); null for none.
    /// </summary>
    public string? CaptionOf(string name)
    {
        for (string n = name; ; n = n[..n.LastIndexOf('.')])
        {
            if (Tuning.Sounds.TryGetValue(n, out var caption))
                return caption;
            if (n.LastIndexOf('.') <= 0)
                return null;
        }
    }

    /// <summary>Where a sound is from the listener's head: AHEAD, BEHIND, LEFT, RIGHT, ABOVE or BELOW.</summary>
    public static string Where(Listener ears, Double3 at)
    {
        var d = at - ears.Position;
        double length = d.Length;
        if (length < 1e-6)
            return "HERE";
        d *= 1 / length;
        double ahead = Double3.Dot(d, ears.Forward), side = Double3.Dot(d, ears.Right);
        if (Math.Abs(d.Y) > 0.75)
            return d.Y > 0 ? "ABOVE" : "BELOW";
        if (ahead < -0.5)
            return "BEHIND";
        if (side > 0.45)
            return "RIGHT";
        if (side < -0.45)
            return "LEFT";
        return "AHEAD";
    }

    /// <summary>This frame's sounds: each heard over the threshold, captioned, keeps its caption up (from the loudest of its kind).</summary>
    public void Update(IEnumerable<Heard> sounds, Listener ears, double now)
    {
        var loudest = new Dictionary<string, Heard>();
        foreach (var s in sounds)
            if (s.Gain >= Tuning.Threshold && CaptionOf(s.Name) is { } caption && (!loudest.TryGetValue(caption, out var was) || s.Gain > was.Gain))
                loudest[caption] = s;
        foreach (var (caption, s) in loudest)
            _heard[caption] = (now, Where(ears, s.Position), s.Gain);
        foreach (var gone in _heard.Where(h => now - h.Value.At > Tuning.HoldSeconds).Select(h => h.Key).ToList())
            _heard.Remove(gone);
    }

    /// <summary>The captions up now, newest last, at most <see cref="Data.Lines"/>: "[TIPTOEING, BEHIND]".</summary>
    public IReadOnlyList<string> Lines() =>
        [.. _heard.OrderByDescending(h => h.Value.At).ThenByDescending(h => h.Value.Gain).ThenBy(h => h.Key, StringComparer.Ordinal)
            .Take(Math.Max(0, Tuning.Lines)).Reverse().Select(h => $"[{h.Key}, {h.Value.Where}]")];

    /// <summary>Nothing's up (CAPTIONS turned off, a new night).</summary>
    public void Clear() => _heard.Clear();
}
