using DarkTerritory.Sim.Campaign;

namespace DarkTerritory.Game;

/// <summary>
/// The big cab's photo rail (GDD §10 "The back wall"; ARCHITECTURE §8 note 1056): each delivered night's crew photo at the
/// gate (note 721) pinned in the next of its frames (<see cref="Art.TrainKit.PhotoFrames"/>). Which photos, and in which
/// order, comes from the crew's logbook (<see cref="LogPage.Photo"/>), which the campaign save and every player's profile
/// keep: so the rail's kept in the save, and every machine pins the photos it took itself. Presentation only.
/// </summary>
public static class PhotoRail
{
    /// <summary>
    /// The nights pinned on a rail of <paramref name="frames"/>, in its frames' order: the logbook's nights with a photo, by
    /// night. When there are more than its frames, <paramref name="keep"/> (memory.json <c>rail.keep</c>) says which stay up:
    /// "latest" (the oldest taken down as each new one goes up; it's still in the logbook and the gallery) or "first" (the
    /// rail full, the first nights stay, later ones only in the book).
    /// </summary>
    public static IReadOnlyList<int> Pinned(IEnumerable<int> nights, int frames, string keep = RailTuning.Latest)
    {
        var all = nights.Distinct().Order().ToList();
        if (all.Count <= frames)
            return all;
        return keep == RailTuning.First ? all[..frames] : all[^frames..];
    }

    /// <summary>The nights in a logbook with a crew photo kept, by night.</summary>
    public static IEnumerable<int> Photographed(IReadOnlyList<LogPage> pages) => pages.Where(p => p.Photo.Length > 0).Select(p => p.Night);
}
