using System.Globalization;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// A night's crew memory on this machine (GDD §9, §11; the director, 14 Oct 2026; ARCHITECTURE §8 note 721): when the crew
/// photo's due and what it came to, the logbook on its shelf and the pages in it, and the night's page written into this
/// player's profile from the report every machine was sent. The app drives it; the rendering is the app's.
/// </summary>
/// <param name="crew">The campaign's crew name, key and night number (the setup's: <see cref="SessionSetup.Crew"/>), or
/// empty for a night that's no campaign's (no page is kept, and the book is empty).</param>
public sealed class CrewMemory(PlayerProfile profile, MemoryTuning tuning, string crew, string crewKey, int night)
{
    readonly Dictionary<string, Still?> _photos = [];
    double _photoDue = -1;
    LogPage? _page;
    IReadOnlyList<LogPage>? _pages;

    public MemoryTuning Tuning { get; } = tuning;
    /// <summary>The crew's name ("" on a quick night).</summary>
    public string Crew { get; } = crew;
    public string CrewKey { get; } = crewKey;
    public int Night { get; } = night;
    /// <summary>The logbook on its shelf, open or shut.</summary>
    public LogbookReader Reader { get; } = new();
    /// <summary>Tonight's crew photo, once taken on this machine.</summary>
    public Still? Photo { get; private set; }
    /// <summary>Where it's kept ("" until then, or when the disk wouldn't have it).</summary>
    public string PhotoFile { get; private set; } = "";
    /// <summary>When its flash went off (this machine's seconds), or −1.</summary>
    public double FlashAt { get; private set; } = -1;
    /// <summary>Tonight's page, once the report's in.</summary>
    public LogPage? Page => _page;
    /// <summary>The yard photographer's print of tonight's leaving (note 714), once taken on this machine.</summary>
    public Still? Yard { get; private set; }
    /// <summary>Where it's kept ("" until then, or when the disk wouldn't have it): tonight's page carries it (note 1030).</summary>
    public string YardFile { get; private set; } = "";
    /// <summary>The yard photographer's gallery (note 1030): the prints read back, their thumbnails.</summary>
    public YardGallery Gallery { get; } = new();
    (IReadOnlyList<LogPage>? Pages, Still? Yard, (IReadOnlyList<GalleryPrint>, int)? Shown) _endGallery;

    /// <summary>The setup's campaign, or a quick night's nothing.</summary>
    public static CrewMemory For(PlayerProfile profile, MemoryTuning tuning, SessionSetup? setup) =>
        new(profile, tuning, setup?.Crew ?? "", setup?.CrewKey ?? "", setup?.Night ?? 0);

    /// <summary>This crew's pages as this player's profile has them (tonight's among them once it's written); read once, kept as written.</summary>
    public IReadOnlyList<LogPage> Pages() => _pages ??= LogbookReader.Of(profile.Load(), CrewKey)?.Pages ?? [];

    /// <summary>
    /// The crew photo's due now (note 721): the night delivered, the report in, the photo not yet taken here, and
    /// <see cref="PhotoTuning.DelaySeconds"/> gone since this machine first saw the report.
    /// </summary>
    public bool PhotoDue(IPlaySession s, double now)
    {
        if (Photo is not null || s.World.Run?.Report is not { End: RunEnd.Delivered })
            return false;
        if (_photoDue < 0)
            _photoDue = now + Tuning.Photo.DelaySeconds;
        return now >= _photoDue;
    }

    /// <summary>Who's in it, and the slate's chalk.</summary>
    public (IReadOnlyList<Sitter> Crew, IReadOnlyList<string> Chalk) Sitting(IPlaySession s) =>
        (CrewPhoto.Sitters(s.World, s.CrewStates(1)), CrewPhoto.Chalk(CrewPhoto.RouteName(s.Route?.Name), Night, s.World.Run?.Report?.Net ?? 0));

    /// <summary>The photo staged beside the engine as it stands now, on the yard's open side (<see cref="CrewPhoto.YardSide"/>).</summary>
    public PhotoStaging Stage(IPlaySession s, IReadOnlyList<CarFrame> frames)
    {
        var (sitters, chalk) = Sitting(s);
        int side = CrewPhoto.YardSide((s.World.Arrival ?? s.World.Town)?.Plan.Square.Side);
        return CrewPhoto.Stage(frames[0], side, sitters, chalk, Tuning.Photo);
    }

    /// <summary>
    /// The photo's been taken (the frame drawn from the staging's lens): kept small, written to the night's album folder with
    /// its sidecar, the flash set off, and tonight's page written again with it.
    /// </summary>
    public void Took(IPlaySession s, byte[] rgba, int width, int height, BookmarkAlbum album, double now, DateTime date)
    {
        Photo = BookmarkStills.Shrink(rgba, width, height, Tuning.Photo.Width, Tuning.Photo.Height);
        FlashAt = now;
        var (sitters, chalk) = Sitting(s);
        PhotoFile = album.KeepPhoto(Photo, CrewPhoto.Sidecar(sitters, chalk, Crew, Date(date)));
        if (PhotoFile.Length > 0)
            _photos[PhotoFile] = Photo;
        if (_page is not null)
            Remember(s, date);
    }

    /// <summary>
    /// The yard photographer's print of tonight's leaving (note 714), taken and kept at <paramref name="file"/> (or not kept:
    /// ""): tonight's page carries it, and the gallery has it (note 1030).
    /// </summary>
    public void Snapped(Still print, string? file) => (Yard, YardFile) = (print, file ?? "");

    /// <summary>The gallery (note 1030): this crew's prints as this machine kept them, night by night, tonight's last.</summary>
    public IReadOnlyList<GalleryPrint> YardPrints() => Gallery.Prints(Pages(), Night, Yard);

    /// <summary>
    /// The gallery for the night's end card (note 1030): the latest <see cref="GalleryTuning.CardMost"/> as thumbnails cut
    /// at twice the card's size (as the crew photo's print is drawn), and how many there are in all; null with none.
    /// </summary>
    public (IReadOnlyList<GalleryPrint> Prints, int Total)? EndGallery()
    {
        var pages = Pages();
        if (_endGallery.Pages != pages || _endGallery.Yard != Yard)
        {
            var prints = YardPrints();
            var t = Tuning.Gallery;
            _endGallery = (pages, Yard, prints.Count == 0 ? null : (Gallery.Thumbs(prints, t.CardWidth * 2, t.CardHeight * 2, t.CardMost), prints.Count));
        }
        return _endGallery.Shown;
    }

    /// <summary>
    /// The photos pinned on the big cab's photo rail (note 1056), in its frames' order: this crew's nights with a crew photo
    /// as this machine kept them (read back, cached), tonight's as taken here, the frames' worth kept by
    /// <see cref="RailTuning.Keep"/>. A photo gone from the disk leaves its frame to the next. Empty on a quick night.
    /// </summary>
    public IReadOnlyList<Still> RailPhotos()
    {
        var pages = Pages();
        if (_rail.Pages == pages && _rail.Photo == Photo)
            return _rail.Shown;
        var kept = new Dictionary<int, Still>();
        foreach (var page in pages.Where(p => p.Photo.Length > 0))
            if (PhotoOf(page) is { } still)
                kept[page.Night] = still;
        if (Photo is not null && CrewKey.Length > 0)
            kept[Night] = Photo;
        var shown = PhotoRail.Pinned(kept.Keys, Art.TrainKit.PhotoFrames, Tuning.Rail.Keep).Select(n => kept[n]).ToList();
        _rail = (pages, Photo, shown);
        return shown;
    }

    (IReadOnlyList<LogPage>? Pages, Still? Photo, IReadOnlyList<Still> Shown) _rail = (null, null, []);

    /// <summary>The flash over the screen now: 1 as it goes off, fading to 0 over <see cref="PhotoTuning.FlashSeconds"/>.</summary>
    public float Flash(double now) =>
        FlashAt < 0 || now < FlashAt || Tuning.Photo.FlashSeconds <= 0 ? 0 : (float)Math.Clamp(1 - (now - FlashAt) / Tuning.Photo.FlashSeconds, 0, 1);

    /// <summary>
    /// Tonight's page in this crew's logbook in the player's profile (note 721), from the report and the commendations as this
    /// machine has them now: written when the report comes in, again with the photo, and once more as the night's left (the
    /// commendations given on the end card). A night that ended the campaign closes the crew onto the board of lost crews.
    /// Nothing on a quick night. Returns the page.
    /// </summary>
    public LogPage? Remember(IPlaySession s, DateTime date)
    {
        if (CrewKey.Length == 0 || s.World.Run?.Report is not { } report)
            return null;
        var world = s.World;
        var commended = world.Commendations.Where(c => c.Which < Commendations.StarterSet.Length)
            .Select(c => (IncidentLog.NameOf(world, c.To), Commendations.StarterSet[c.Which], IncidentLog.NameOf(world, c.From)));
        bool wakers = world.Enemies?.Wakers.Enabled ?? true;
        _page = Logbook.Page(report, Night, RouteTitle(s), Date(date), commended, wakers, PhotoFile, YardFile);
        bool over = report.End == RunEnd.DawnMissed && world.DawnEndsCampaign;
        _pages = LogbookReader.Of(profile.Log(CrewKey, Crew, _page, over), CrewKey)?.Pages ?? [];
        return _page;
    }

    /// <summary>A page's photo, read back from where this machine kept it (cached), or null.</summary>
    public Still? PhotoOf(LogPage page)
    {
        if (page.Photo.Length == 0)
            return null;
        if (!_photos.TryGetValue(page.Photo, out var still))
            _photos[page.Photo] = still = BookmarkAlbum.ReadPhoto(page.Photo);
        return still;
    }

    /// <summary>The page's line: "Frontier 7, Hollin Halt to Barrow Bay" (its tier and seed, the town it left and the terminus).</summary>
    public static string RouteTitle(IPlaySession s)
    {
        string name = CrewPhoto.RouteName(s.Route?.Name);
        string? from = s.World.Town?.Plan.Name, to = s.Route?.Plan?.Terminus.Name;
        return from is { Length: > 0 } && to is { Length: > 0 } ? $"{name}, {from} to {to}" : to is { Length: > 0 } ? $"{name}, to {to}" : name;
    }

    /// <summary>The page's date line, by the player's own clock: "14 October 2026".</summary>
    public static string Date(DateTime date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The board of lost crews' cards from this player's profile (note 721), the last ended first.</summary>
    public static IReadOnlyList<(string Title, string Text)> LostCrews(PlayerProfile.Data profile) =>
        [.. Logbook.Lost(profile.Crews).Select(Logbook.Card)];
}
