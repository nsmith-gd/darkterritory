using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The crew's logbook on its shelf (GDD §11 "What a crew keeps"; the director, 14 Oct 2026; ARCHITECTURE §8 note 721): a
/// book on top of the kit locker in car one (memory.json <c>logbook.place</c>, the hook for the big cab's back wall). Use
/// opens it as a page a night in the paper's style; the movement keys turn the pages; Use again shuts it. On this machine
/// alone, from this player's own copy in their profile (<see cref="PlayerProfile.Data.Crews"/>), so nothing is sent: the
/// press is the app's to keep from the host, as a word in town is.
/// </summary>
public sealed class LogbookReader
{
    /// <summary>What's offered at the shelf.</summary>
    public const string Prompt = "THE LOGBOOK   READ : [E]";

    /// <summary>The book's open.</summary>
    public bool Open { get; private set; }
    /// <summary>The page open (0 the first night's).</summary>
    public int Page { get; private set; }

    /// <summary>
    /// Where the book lies, in its car's frame (note 721): for "lockers", on a small shelf on the wall across the aisle from
    /// the kit locker in car one (the lockers' tops are up under the roof), at chest height; and which way its front faces
    /// (yaw: 0 faces −Z), to the aisle. Null on a train without the lockers. Any other place is the lockers' until the cab
    /// has its back wall to take it (queue #450).
    /// </summary>
    public static (int Car, Double3 Local, double Yaw)? Shelf(TrainOnLine train, LogbookTuning t)
    {
        if (World.KitLocker(train) is not { } locker || train.Frames[locker.Car].Shape.Interior is not { } room)
            return null;
        var box = locker.Bay.Box;
        // The lockers face the aisle across it (+X on the left wall): the shelf's on the far wall, its front back to them.
        int toAisle = -locker.Bay.Facing;
        double wall = toAisle < 0 ? room.Max.X : room.Min.X;
        var at = new Double3(wall + toAisle * ShelfDepth, box.Min.Y + t.Height, (box.Min.Z + box.Max.Z) / 2);
        return (locker.Car, at, -toAisle * Math.PI / 2);
    }

    /// <summary>How far the book's middle stands off its wall (m): the shelf's half depth and a little.</summary>
    public const double ShelfDepth = 0.17;

    /// <summary>
    /// The book's in front of <paramref name="p"/>: in its car, within reach of the eye and near the view's centre. The
    /// prompt (<see cref="Prompt"/>) is offered then.
    /// </summary>
    public static bool Facing(in PlayerState p, TrainOnLine train, LogbookTuning t, double eyeHeight)
    {
        if (!p.Alive || Shelf(train, t) is not { } shelf || p.Parent != shelf.Car)
            return false;
        var eye = p.Position + Double3.Up * eyeHeight;
        double cp = Math.Cos(p.Pitch);
        var view = new Double3(-Math.Sin(p.Yaw) * cp, Math.Sin(p.Pitch), -Math.Cos(p.Yaw) * cp);
        var to = shelf.Local + Double3.Up * 0.03 - eye;
        double d = to.Length;
        return d <= t.Reach && d > 1e-6 && Double3.Dot(view, to * (1 / d)) >= Math.Cos(t.LookDegrees * Math.PI / 180);
    }

    /// <summary>Use at the shelf: opens the book at its last page (the night just run), or shuts it.</summary>
    public void Use(int pages)
    {
        Open = !Open;
        Page = Math.Max(0, pages - 1);
    }

    /// <summary>Shuts it (walked off, the night's over, the menu opened).</summary>
    public void Close() => Open = false;

    /// <summary>A page turned: −1 back a night, +1 on; held at the covers. True when it turned.</summary>
    public bool Turn(int by, int pages)
    {
        int to = Math.Clamp(Page + by, 0, Math.Max(0, pages - 1));
        if (to == Page)
            return false;
        Page = to;
        return true;
    }

    /// <summary>The crew whose book it is on this night: this campaign's, from the player's profile (empty pages on a quick night).</summary>
    public static CrewLog? Of(PlayerProfile.Data profile, string? crewKey) =>
        crewKey is { Length: > 0 } key ? profile.Crews.FirstOrDefault(c => c.Key == key) : null;
}
