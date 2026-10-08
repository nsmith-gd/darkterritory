using Ballast;

namespace DarkTerritory.Sim.Towns;

/// <summary>A house's roof: a plain gable, a saltbox's long back slope, a gambrel's two pitches, or a hip.</summary>
public enum HouseRoof : byte { Gable, Saltbox, Gambrel, Hip }

/// <summary>What breaks a roof's front slope: nothing, gable dormers, one long shed dormer, a Lunenburg bump, the Island's centre gable.</summary>
public enum HouseDormer : byte { None, Gables, Shed, Bump, CentreGable }

/// <summary>What's at the front door: nothing, a little roof on posts over the step, or an enclosed porch you go through.</summary>
public enum HousePorch : byte { None, Stoop, Vestibule }

/// <summary>Where the chimney is: through the ridge in the middle, at one end, at both ends, or a stovepipe.</summary>
public enum HouseChimney : byte { Centre, End, Ends, Pipe }

/// <summary>
/// One house's design (the director, 7 Oct 2026, with photographs of Maritime houses: "lots of variations so it doesn't
/// feel like the same 10 assets recycled across towns"; ARCHITECTURE §8 note 281). Drawn in the Sim, alike on every
/// machine, because some of it stands (a side wing, an enclosed porch: <see cref="TownHouse.Parts"/>); the rest is how
/// the art builds it. Colours are indices into content/world/houses.json's palettes.
/// </summary>
/// <param name="GableFront">The gable end faces the street (the ridge runs back from it), else the eaves do.</param>
/// <param name="Storeys">1, 1.5 (the upper floor in the roof), 2 or 2.5.</param>
/// <param name="Pitch">The roof's rise over its run (0.55 low to 1.15 steep).</param>
/// <param name="Dormers">How many gable dormers (<see cref="HouseDormer.Gables"/>).</param>
/// <param name="Ell">A lower side wing: −1 or +1 along the front (the house's u), 0 none; its width along the front,
/// its depth back from <see cref="EllSetback"/>, and whether it's a storey and a half (else one).</param>
/// <param name="Shingle">Cedar shingle (stained by <see cref="Paint"/> from the stains, unless <see cref="Painted"/>), else clapboard.</param>
/// <param name="Shutters">The shutters' colour (doors palette), or −1 for none.</param>
/// <param name="Windows">Sash: 0 two-over-two, 1 six-over-six, 2 one-over-one.</param>
/// <param name="Fancy">Victorian trim (brackets, window heads, bargeboard) in <see cref="Accent"/>.</param>
/// <param name="DoorU">Where the front door is along the front from the middle (an open house's is its kitchen's: <see cref="HouseLayout.DoorU"/>).</param>
public sealed record HouseDesign(bool GableFront, double Storeys, HouseRoof Roof, double Pitch, HouseDormer Dormer, int Dormers,
    int Ell, double EllWidth, double EllDepth, double EllSetback, bool EllTall, HousePorch Porch, bool Shingle, bool Painted,
    int Paint, int Trim, int Door, int Shutters, int RoofColour, int Windows, HouseChimney Chimney, bool Fancy, int Accent, double DoorU)
{
    /// <summary>The enclosed porch's half-width along the front and its depth out from it (m).</summary>
    public const double VestibuleHalf = 1.1, VestibuleDepth = 1.7;

    /// <summary>How far the eaves stand off the ground, by storeys (m): the wall's height under the roof.</summary>
    public double Eaves => Storeys switch { <= 1 => 2.9, <= 1.5 => 3.6, <= 2 => 5.6, _ => 6.2 };
}

/// <summary>Mirror of content/world/houses.json: the palettes (their weights; the colours are the art's) and the characters.</summary>
public sealed record HouseLooks
{
    public const string File = "world/houses.json";

    public HouseColour[] Paints { get; init; } = [];
    public HouseColour[] Stains { get; init; } = [];
    public HouseColour[] Trims { get; init; } = [];
    public HouseColour[] Doors { get; init; } = [];
    public HouseColour[] Roofs { get; init; } = [];
    public HouseColour[] Accents { get; init; } = [];
    public HouseCharacter[] Characters { get; init; } = [];
    public Dictionary<string, string[]> ByTrade { get; init; } = [];

    /// <summary>The content's house looks, or the plain defaults where there's no houses.json (an old content folder, a mod).</summary>
    public static HouseLooks Load(string content)
    {
        string path = Path.Combine(content, File);
        return System.IO.File.Exists(path) ? DataFile.Load<HouseLooks>(path) : new HouseLooks();
    }
}

/// <summary>A palette entry: its name, its colour (sRGB hex, the art's), and how often it's picked against the rest.</summary>
public sealed record HouseColour(string Id, string Rgb, double Weight = 1);

/// <summary>
/// A town's character (houses.json <c>characters</c>): the odds its houses are drawn with, so a fishing cove of
/// shingled gable-fronts isn't a Lunenburg street of painted bumps.
/// </summary>
public sealed record HouseCharacter
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public double Shingle { get; init; } = 0.35;
    public double GableFront { get; init; } = 0.35;
    public double Dormers { get; init; } = 0.35;
    public double Ell { get; init; } = 0.4;
    public double Porch { get; init; } = 0.3;
    public double Shutters { get; init; } = 0.3;
    public double Fancy { get; init; } = 0.1;
    public double Bump { get; init; }
    public double CentreGable { get; init; }
    /// <summary>Weights of 1, 1.5, 2 and 2.5 storeys.</summary>
    public double[] Storeys { get; init; } = [2, 4, 4, 1];
    /// <summary>Weights of a gable, a saltbox, a gambrel and a hip.</summary>
    public double[] Roofs { get; init; } = [7, 2, 2, 1];
    /// <summary>The town's own paints (ids), and the share of its houses painted from them.</summary>
    public string[] Paints { get; init; } = [];
    public double Own { get; init; }
    /// <summary>A company town's: one design, its paint and wear each house's own.</summary>
    public bool Uniform { get; init; }
}

/// <summary>Draws a town's character and its houses' designs, each from the stream it's given.</summary>
public static class HouseDesigner
{
    /// <summary>The town's character: from those its trade leans to (houses.json <c>byTrade</c>), else any.</summary>
    public static HouseCharacter Character(HouseLooks looks, string trade, ref Pcg32 rng)
    {
        if (looks.Characters.Length == 0)
            return new HouseCharacter { Id = "mixed" };
        var ids = looks.ByTrade.TryGetValue(trade, out var t) && t.Length > 0 ? t
            : looks.ByTrade.TryGetValue("", out var any) && any.Length > 0 ? any : [.. looks.Characters.Select(c => c.Id)];
        string id = ids[(int)(rng.NextDouble() * ids.Length)];
        return looks.Characters.FirstOrDefault(c => c.Id == id) ?? looks.Characters[0];
    }

    /// <summary>
    /// One house's design for a house <paramref name="width"/> along its front by <paramref name="depth"/>, its gable to the
    /// street or not (<paramref name="gable"/>, drawn first: it sets the width), in a town of <paramref name="character"/>. <paramref name="room"/> is how much wider the lot lets it be with a side wing;
    /// <paramref name="open"/>, an open house (no enclosed porch: you go straight in, at <paramref name="doorU"/>).
    /// </summary>
    public static HouseDesign Draw(HouseLooks looks, HouseCharacter character, bool gable, double width, double depth, double room, bool open,
        double? doorU, ref Pcg32 rng)
    {
        var c = character;
        double storeys = Weighted(c.Storeys, ref rng) switch { 0 => 1, 1 => 1.5, 2 => 2, _ => 2.5 };
        var roof = (HouseRoof)Weighted(c.Roofs, ref rng);
        // A saltbox runs its long slope to the back: an eave-front house's. A hip on a narrow gable-front reads as a box.
        if (gable && roof is HouseRoof.Saltbox or HouseRoof.Hip)
            roof = HouseRoof.Gable;
        double pitch = roof == HouseRoof.Gambrel ? rng.Range(0.75, 1.0) : rng.Range(0.6, 1.15);
        // The roof's front slope: a storey and a half needs its dormers (or the Island's gable) for the rooms up there.
        var dormer = HouseDormer.None;
        int dormers = 0;
        if (!gable && roof is HouseRoof.Gable or HouseRoof.Gambrel)
        {
            if (rng.Chance(c.Bump) && storeys >= 1.5)
                dormer = HouseDormer.Bump;
            else if (rng.Chance(c.CentreGable) && storeys <= 1.5)
                dormer = HouseDormer.CentreGable;
            else if (rng.Chance(storeys == 1.5 || storeys == 2.5 ? Math.Max(c.Dormers, 0.5) : c.Dormers * 0.5))
            {
                dormer = rng.Chance(0.75) ? HouseDormer.Gables : HouseDormer.Shed;
                dormers = width > 7.5 ? rng.RangeInclusive(2, 3) : rng.RangeInclusive(1, 2);
            }
        }
        int ell = 0;
        double ellWidth = 0, ellDepth = 0, ellSetback = 0;
        bool ellTall = false;
        if (room >= 3 && rng.Chance(c.Ell))
        {
            ell = rng.Chance(0.5) ? 1 : -1;
            ellWidth = Math.Min(room, rng.Range(3.0, 4.6));
            ellDepth = Math.Min(depth - 0.4, rng.Range(3.4, depth));
            ellSetback = rng.Range(0.3, Math.Max(0.35, depth - ellDepth));
            ellTall = rng.Chance(0.3);
        }
        var porch = HousePorch.None;
        if (rng.Chance(c.Porch))
            porch = !open && rng.Chance(0.55) ? HousePorch.Vestibule : HousePorch.Stoop;
        bool shingle = rng.Chance(c.Shingle);
        bool painted = shingle && rng.Chance(0.3);
        int paint = shingle && !painted ? Pick(looks.Stains, ref rng) : PaintFor(looks, c, ref rng);
        int trim = Pick(looks.Trims, ref rng);
        int door = Pick(looks.Doors, ref rng);
        int shutters = rng.Chance(c.Shutters) ? (rng.Chance(0.5) ? door : Pick(looks.Doors, ref rng)) : -1;
        int roofColour = Pick(looks.Roofs, ref rng);
        int windows = (int)Weighted([5, 3, 2], ref rng);
        var chimney = (HouseChimney)Weighted([5, 3, 1.5, 1.5], ref rng);
        bool fancy = rng.Chance(c.Fancy) || dormer == HouseDormer.Bump && rng.Chance(0.5);
        int accent = Pick(looks.Accents, ref rng);
        // The door: in the middle of an eave-front (or in a side bay), to one side of a gable-front.
        double du = doorU ?? (gable ? (rng.Chance(0.5) ? 1 : -1) * (width / 2 - 1.3)
            : rng.Chance(0.6) ? 0 : (rng.Chance(0.5) ? 1 : -1) * width / 4);
        if (porch == HousePorch.Vestibule)
            du = Math.Clamp(du, -width / 2 + HouseDesign.VestibuleHalf + 0.2, width / 2 - HouseDesign.VestibuleHalf - 0.2);
        return new HouseDesign(gable, storeys, roof, pitch, dormer, dormers, ell, ellWidth, ellDepth, ellSetback, ellTall, porch,
            shingle, painted, paint, trim, door, shutters, roofColour, windows, chimney, fancy, accent, du);
    }

    /// <summary>
    /// A company town's house: the town's one design (<paramref name="model"/>), its own paint, door and roof, the side
    /// the door's on mirrored now and then (the double houses' two halves).
    /// </summary>
    public static HouseDesign Copy(HouseLooks looks, HouseCharacter c, HouseDesign model, double? doorU, ref Pcg32 rng) =>
        model with
        {
            Paint = model.Shingle && !model.Painted ? Pick(looks.Stains, ref rng) : PaintFor(looks, c, ref rng),
            Door = Pick(looks.Doors, ref rng),
            RoofColour = rng.Chance(0.7) ? model.RoofColour : Pick(looks.Roofs, ref rng),
            DoorU = doorU ?? (rng.Chance(0.5) ? model.DoorU : -model.DoorU),
            Ell = rng.Chance(0.5) ? model.Ell : -model.Ell,
        };

    static int PaintFor(HouseLooks looks, HouseCharacter c, ref Pcg32 rng)
    {
        if (c.Paints.Length > 0 && rng.Chance(c.Own))
        {
            string id = c.Paints[(int)(rng.NextDouble() * c.Paints.Length)];
            int i = Array.FindIndex(looks.Paints, p => p.Id == id);
            if (i >= 0)
                return i;
        }
        return Pick(looks.Paints, ref rng);
    }

    /// <summary>A palette entry by its weight; 0 for an empty palette.</summary>
    static int Pick(HouseColour[] palette, ref Pcg32 rng)
    {
        if (palette.Length == 0)
            return 0;
        return (int)Weighted([.. palette.Select(p => p.Weight)], ref rng);
    }

    static long Weighted(IReadOnlyList<double> weights, ref Pcg32 rng)
    {
        double total = weights.Sum();
        if (total <= 0)
            return 0;
        double r = rng.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            r -= weights[i];
            if (r < 0)
                return i;
        }
        return weights.Count - 1;
    }
}
