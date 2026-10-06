using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>A bookmark's still (GDD v1.4 App. D.12): RGBA8 display values, <see cref="Width"/> × <see cref="Height"/>.</summary>
public sealed record Still(int Width, int Height, byte[] Rgba);

/// <summary>A still due this frame: the bookmark, the camera, and for a derailment's, the film's peak to draw (E.5).</summary>
public sealed record DueStill(Bookmark Mark, Camera Camera, FilmPeak? Peak);

/// <summary>
/// The stills of the night's bookmarks (GDD v1.4 App. D.12), taken on this machine. The host sends where each is to be
/// taken from (<see cref="Bookmark"/>); this says when each is due and from what camera, the app draws the frame from there
/// with what it has of the world now, and the still is kept, small, for the run-end screen. No replays: a still is all
/// there is, and a bookmark this machine never saw (it joined later) has none.
/// </summary>
public sealed class BookmarkStills
{
    /// <summary>A still's size: small, for a thumbnail on the report, but more than the report shows (D.12 "still capture").</summary>
    public const int Width = 320, Height = 180;

    readonly Dictionary<int, Still> _stills = [];
    readonly Dictionary<int, double> _seen = [];
    readonly HashSet<int> _peaks = [];

    public IReadOnlyDictionary<int, Still> Stills => _stills;

    /// <summary>A new night: nothing kept.</summary>
    public void Clear()
    {
        _stills.Clear();
        _seen.Clear();
        _peaks.Clear();
    }

    /// <summary>
    /// The bookmarks due a still this frame, with the camera to take each from. A GRAB's, a PUNISH's and a dead player's are
    /// due as they arrive; the Stranded outro's once the outro's over, from its last frame. A derailment's is E.5's: that
    /// crew member's peak in the film (<see cref="DerailSequence.PeakOf"/>), from their shot's camera, as soon as this
    /// machine has the film. Until it has (the film's shot off the frame loop, and a machine that never gets it has none),
    /// it's the first-person beat's at <see cref="WreckTuning.BookmarkSeconds"/> (by this machine's clock from when it heard
    /// of it), from their eye in the car they rode as the wreck throws it; the film's peak replaces that when it comes.
    /// </summary>
    /// <param name="now">This machine's seconds (smooth between ticks).</param>
    /// <param name="film">The derailment's film; the session's own if not given.</param>
    public List<DueStill> Due(IPlaySession s, IReadOnlyList<CarFrame> frames, double now, WreckFilm? film = null)
    {
        film ??= s.Film;
        var due = new List<DueStill>();
        var wreck = s.World.WreckTuning;
        foreach (var b in s.World.Bookmarks.All)
        {
            if (_peaks.Contains(b.Id))
                continue;
            if (b.Kind == BookmarkKind.Derail && film is not null && DerailSequence.PeakOf(film, b.Victim) is { } peak)
            {
                due.Add(new DueStill(b, peak.Camera, peak));
                continue;
            }
            if (_stills.ContainsKey(b.Id))
                continue;
            if (!_seen.TryGetValue(b.Id, out double seen))
                _seen[b.Id] = seen = now;
            Camera? camera = b.Kind switch
            {
                BookmarkKind.Derail => now - seen >= wreck.BookmarkSeconds ? Of(b, frames) : null,
                // A beat after it's heard of, so a client knows the run's stranded (the outro's playing) before it asks.
                BookmarkKind.Stranded => !s.StrandedOutro && now - seen >= 0.25 ? Views.Stranded(s.Train, wreck.Stranded, wreck.Stranded.Seconds) : null,
                _ => Of(b, frames),
            };
            if (camera is { } c)
                due.Add(new DueStill(b, c, null));
        }
        return due;
    }

    /// <summary>Whether a bookmark's still is its crew member's peak in the film (E.5), not the first-person stand-in.</summary>
    public bool IsPeak(int id) => _peaks.Contains(id);

    /// <summary>The camera a bookmark names: its eye and look in the car it rides (rolled with the car), or in the world.</summary>
    public static Camera Of(Bookmark b, IReadOnlyList<CarFrame> frames)
    {
        if (b.Frame >= 0 && b.Frame < frames.Count)
            return DerailSequence.Riding(frames[b.Frame], b.Eye, b.Look);
        var look = b.Look.Length > 1e-6 ? b.Look.Normalized : new Double3(0, 0, -1);
        return Camera.LookAt(b.Eye, b.Eye + look, 70);
    }

    /// <summary>
    /// Who's drawn in a still: everyone aboard but whoever's eyes it is (their own head would fill it), this machine's
    /// player included (the app never draws its own figure).
    /// </summary>
    public static IReadOnlyList<Crewmate> Figures(IPlaySession s, IReadOnlyList<CarFrame> frames, double alpha, int viewer)
    {
        var crew = s.Crew(frames, alpha).Where(c => c.Id != viewer).ToList();
        if (viewer != s.PlayerId && s.Player.Alive && crew.All(c => c.Id != s.PlayerId))
            crew.Add(Art.CrewActs.Crewmate((byte)s.PlayerId, s.Player, s.World, frames));
        return crew;
    }

    /// <summary>
    /// Keeps a frame drawn from a bookmark's camera as its still: cropped to 16:9 about its middle, averaged down.
    /// <paramref name="peak"/>: it's the film's peak (E.5), final.
    /// </summary>
    public Still Keep(Bookmark b, byte[] rgba, int width, int height, bool peak = false)
    {
        var still = Shrink(rgba, width, height, Width, Height);
        _stills[b.Id] = still;
        if (peak)
            _peaks.Add(b.Id);
        return still;
    }

    /// <summary>Puts a still in directly (the headless report, tests).</summary>
    public void Put(int id, Still still) => _stills[id] = still;

    /// <summary>An RGBA frame cropped to the target's shape about its middle and box-filtered down to it.</summary>
    public static Still Shrink(byte[] rgba, int width, int height, int w, int h)
    {
        int cropW = Math.Min(width, height * w / h), cropH = Math.Min(height, width * h / w);
        int ox = (width - cropW) / 2, oy = (height - cropH) / 2;
        var out_ = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int y0 = oy + y * cropH / h, y1 = Math.Max(y0 + 1, oy + (y + 1) * cropH / h);
            for (int x = 0; x < w; x++)
            {
                int x0 = ox + x * cropW / w, x1 = Math.Max(x0 + 1, ox + (x + 1) * cropW / w);
                int r = 0, g = 0, bl = 0, n = 0;
                for (int py = y0; py < y1; py++)
                    for (int px = x0; px < x1; px++)
                    {
                        int i = (py * width + px) * 4;
                        r += rgba[i];
                        g += rgba[i + 1];
                        bl += rgba[i + 2];
                        n++;
                    }
                int o = (y * w + x) * 4;
                out_[o] = (byte)(r / n);
                out_[o + 1] = (byte)(g / n);
                out_[o + 2] = (byte)(bl / n);
                out_[o + 3] = 255;
            }
        }
        return new Still(w, h, out_);
    }
}
