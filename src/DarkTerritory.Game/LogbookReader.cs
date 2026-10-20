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
    /// Where the book lies, in its car's frame (note 721), which way its front faces (yaw: 0 faces −Z) and whether it's on
    /// its own little shelf. "cab": shut on the seat of the big cab's right-hand bench (notes 716, 905), at its front end, its
    /// spine along it, just behind the chart table (the table's top is the run map's, kept clear; the bench's seats are
    /// behind it); it falls back to the lockers on an engine
    /// without the benches. "lockers": on a small
    /// shelf on the wall across the aisle from the kit locker in car one (the lockers' tops are up under the roof), at chest
    /// height, its front to the aisle. Null on a train with neither.
    /// </summary>
    public static (int Car, Double3 Local, double Yaw, bool OnShelf)? Shelf(TrainOnLine train, LogbookTuning t)
    {
        if (t.Place == "cab" && train.Frames.Count > 0 && CrewCar.FrontBench(train.Frames[0].Shape) is { } bench)
            return (0, new Double3((bench.Min.X + bench.Max.X) / 2, bench.Max.Y + 0.006, bench.Min.Z + SeatInset + BookHalf.X), Math.PI / 2, false);
        if (World.KitLocker(train) is not { } locker || train.Frames[locker.Car].Shape.Interior is not { } room)
            return null;
        var box = locker.Bay.Box;
        // The lockers face the aisle across it (+X on the left wall): the shelf's on the far wall, its front back to them.
        int toAisle = -locker.Bay.Facing;
        double wall = toAisle < 0 ? room.Max.X : room.Min.X;
        var at = new Double3(wall + toAisle * ShelfDepth, box.Min.Y + t.Height, (box.Min.Z + box.Max.Z) / 2);
        return (locker.Car, at, -toAisle * Math.PI / 2, true);
    }

    /// <summary>
    /// Where a reader stands to read it (feet, in its car's frame): on the gallery in front of the bench, by the
    /// book; or in car one's aisle at the kit locker, across from its shelf. Null without a book.
    /// </summary>
    public static Double3? ReadFrom(TrainOnLine train, (int Car, Double3 Local, double Yaw, bool OnShelf) book)
    {
        if (!book.OnShelf)
            return CrewCar.FrontBench(train.Frames[book.Car].Shape) is { } bench
                ? new Double3(bench.Centre.X > 0 ? bench.Min.X - ReaderOut : bench.Max.X + ReaderOut, bench.Min.Y, book.Local.Z)
                : null;
        if (World.KitLocker(train) is not { } locker)
            return null;
        var bay = locker.Bay;
        return new Double3(bay.Front.X + bay.Facing * 0.75, bay.Front.Y, bay.Front.Z);
    }

    /// <summary>How far out from the bench's edge a reader stands (m): on the gallery's floor in front of it.</summary>
    public const double ReaderOut = 0.35;

    /// <summary>The shut book's half size across and along (m), for laying it inside the table's edge.</summary>
    public static readonly Double3 BookHalf = new(0.17, 0.035, 0.12);
    /// <summary>How far in from the bench's front end the book lies (m).</summary>
    public const double SeatInset = 0.06;

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
