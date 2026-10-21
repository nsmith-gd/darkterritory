using Ballast;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// A house's roof: a plain gable, a saltbox's long back slope, a gambrel's two pitches, a hip; or flat, a garden on it (the
/// director, 14 Oct, of Glass Gardeners: "we should also do things like gardening on roofs for some of the buildings").
/// </summary>
public enum HouseRoof : byte { Gable, Saltbox, Gambrel, Hip, Flat }

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

    /// <summary>
    /// How sooted and salt-bleached it's gone (0..1; its town's character's, note 692: a fortress town's houses haven't
    /// seen paint in years). The art greys and darkens its colours by it.
    /// </summary>
    public double Grime { get; init; }

    /// <summary>
    /// A houseboat (note 822, the Flooders): a scow's hull under it, up on stilts against the flood, a ladder to its door.
    /// The art lifts the house onto the hull's deck (<c>MaritimeKit.HullLift</c>); never an open house, whose rooms are on the
    /// Sim's floor.
    /// </summary>
    public bool Hull { get; init; }

    /// <summary>
    /// A houseboat's hull as the Sim holds it (note 841): how high its deck stands, and how far its sides stand out past
    /// the house's walls and its ends past its front and back (m). <c>MaritimeKit</c> draws it to these, so what's solid is
    /// what's drawn (the director, 16 Oct: "I can sort of clip through the corners of some of them and just see inside").
    /// </summary>
    public const double HullLift = 2.2, HullBeam = 0.3, HullEnds = 0.4;

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
    /// <summary>Which characters a town type's towns are drawn from (note 692), before its trade's.</summary>
    public Dictionary<string, string[]> ByForm { get; init; } = [];

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
    /// <summary>Weights of a gable, a saltbox, a gambrel, a hip and (where there's a fifth) a flat roof with its garden.</summary>
    public double[] Roofs { get; init; } = [7, 2, 2, 1];
    /// <summary>The town's own paints (ids), and the share of its houses painted from them.</summary>
    public string[] Paints { get; init; } = [];
    public double Own { get; init; }
    /// <summary>A company town's: the company's plans (<see cref="Plans"/>), its paint and wear each house's own.</summary>
    public bool Uniform { get; init; }
    /// <summary>
    /// How many plans a company built to (note 815; the director, 13 Oct: "A lot of the house models are too same same"):
    /// each street's houses in runs of one plan, the next run another's.
    /// </summary>
    public int Plans { get; init; } = 1;
    /// <summary>
    /// The share of its houses the families have changed over the years (note 815: a porch added or closed in, a lean-to
    /// on the side, a dormer cut in, shingled over, shutters hung, a stovepipe for the chimney, a storey built up), and how
    /// many changes such a house has had.
    /// </summary>
    public double Altered { get; init; }
    public int[] Alterations { get; init; } = [1, 2];
    /// <summary>How sooted and bleached its houses have gone (0..1, <see cref="HouseDesign.Grime"/>), and how far either way one house's differs.</summary>
    public double Grime { get; init; }
    public double GrimeSpread { get; init; }
    /// <summary>The share of its shut houses that are houseboats (<see cref="HouseDesign.Hull"/>; note 822, the Flooders).</summary>
    public double Hull { get; init; }
    /// <summary>
    /// The creed its people keep (world/towns.json <c>creeds</c>; note 822, the Flooders'): heard and posted as a habit's
    /// lines and notes are, its first notice on the board after the custom's order.
    /// </summary>
    public string? Creed { get; init; }
    /// <summary>
    /// Its people out among its houses at their own work all night (note 841; the director, 16 Oct, in a Flooder town:
    /// "There's also nobody walking around. There's no kind of like culture going on"): one for every <c>Out[0]</c> of its
    /// houses, at most <c>Out[1]</c>, spread through the town, each at a house's front doing <see cref="OutActs"/>' next
    /// (a townsperson's act: "mend" caulking a hull, "crouch" reading the water's mark on a stilt, "lantern" out walking
    /// with a lamp, "work" hauling). None where it's [0, 0].
    /// </summary>
    public int[] Out { get; init; } = [0, 0];
    public string[] OutActs { get; init; } = ["lantern"];
    /// <summary>What its yards hold (note 335).</summary>
    public YardOdds Yard { get; init; } = new();
    /// <summary>What its people breathe through (note 353): each of <see cref="TownGear.Kinds"/>' weight against the rest.</summary>
    public Dictionary<string, double> Gear { get; init; } = new() { ["respirator"] = 1 };
}

/// <summary>
/// What a town's people breathe through out of doors (the director, 8 Oct 2026: "townsfolk models who wear some sort of
/// respirator mask or oxygen mask or other breathing apparatuses to indicate the air is foul"; note 353). Never the crew's
/// mask: a rubber half-mask with its two cans ("respirator"), a cup over the nose and mouth on a hose from a bottle on the
/// back ("oxygen"), a mine-rescue set's mouthpiece and its bag on the chest ("rebreather"), or a wool wrap with a can sewn
/// into it ("wrap"). Drawn per person from their name, so who they are decides it and nobody else's draw moves it.
/// </summary>
public static class TownGear
{
    /// <summary>The kinds, in the order a weight's drawn (not the file's).</summary>
    public static readonly string[] Kinds = ["respirator", "oxygen", "rebreather", "wrap"];

    /// <summary>The kind <paramref name="hash"/> lands on among <paramref name="weights"/> (in <see cref="Kinds"/>' order).</summary>
    public static string Pick(IReadOnlyDictionary<string, double> weights, ulong hash)
    {
        double total = 0;
        foreach (string k in Kinds)
            total += weights.TryGetValue(k, out double w) ? Math.Max(0, w) : 0;
        if (total <= 0)
            return Kinds[0];
        double at = (hash >> 11) * (1.0 / (1UL << 53)) * total;
        foreach (string k in Kinds)
        {
            at -= weights.TryGetValue(k, out double w) ? Math.Max(0, w) : 0;
            if (at < 0)
                return k;
        }
        return Kinds[^1];
    }
}

/// <summary>
/// What a character's yards hold (houses.json <c>yard</c>; note 335): the share of houses on a street with a picket fence
/// out front, the share of back lines with a board fence, how many things in a yard, and how often each thing is picked
/// (by <see cref="YardKind"/> name, camel case) against the rest.
/// </summary>
public sealed record YardOdds
{
    public double Picket { get; init; } = 0.5;
    public double Boards { get; init; } = 0.8;
    public int[] Count { get; init; } = [1, 3];
    public Dictionary<string, double> Things { get; init; } = new() { ["woodpile"] = 4, ["shed"] = 2, ["privy"] = 1, ["clothesline"] = 2, ["barrel"] = 2 };

    /// <summary>A thing's weight, in <see cref="YardKind"/>'s order whatever order the file lists them in.</summary>
    public double Weight(YardKind kind) => Things.TryGetValue(char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], out double w) ? w : 0;
}

/// <summary>Draws a town's character and its houses' designs, each from the stream it's given.</summary>
public static class HouseDesigner
{
    /// <summary>The town's character: from those its trade leans to (houses.json <c>byTrade</c>), else any.</summary>
    public static HouseCharacter Character(HouseLooks looks, string trade, ref Pcg32 rng, string form = "")
    {
        if (looks.Characters.Length == 0)
            return new HouseCharacter { Id = "mixed" };
        var ids = form.Length > 0 && looks.ByForm.TryGetValue(form, out var f) && f.Length > 0 ? f
            : looks.ByTrade.TryGetValue(trade, out var t) && t.Length > 0 ? t
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

    /// <summary>What a family can do to its house over the years (note 815), each as often as its weight.</summary>
    public enum Change : byte { Porch, LeanTo, Dormer, Shingled, Shutters, Stovepipe, BuiltUp }

    static readonly double[] ChangeWeights = [4, 4, 2, 3, 2, 2, 1];

    /// <summary>
    /// A house as its family has changed it over the years (note 815; the director, 13 Oct: "A lot of the house models are too
    /// same same"): <paramref name="changes"/> of <see cref="Change"/>, each that fits the house as it stands, drawn from
    /// <paramref name="rng"/>. A lean-to needs <paramref name="room"/> beside it; an open house's porch stays a stoop (you go
    /// straight in). What doesn't fit is drawn again, up to a few times.
    /// </summary>
    public static HouseDesign Alter(HouseLooks looks, HouseDesign d, int changes, double width, double depth, double room, bool open, ref Pcg32 rng)
    {
        for (int n = 0, tries = 0; n < changes && tries < 4 * changes; tries++)
        {
            var change = (Change)Weighted(ChangeWeights, ref rng);
            HouseDesign? to = change switch
            {
                Change.Porch when d.Porch == HousePorch.None => d with { Porch = HousePorch.Stoop },
                Change.Porch when d.Porch == HousePorch.Stoop && !open => d with
                {
                    Porch = HousePorch.Vestibule,
                    DoorU = Math.Clamp(d.DoorU, -width / 2 + HouseDesign.VestibuleHalf + 0.2, width / 2 - HouseDesign.VestibuleHalf - 0.2),
                },
                Change.LeanTo when d.Ell == 0 && room >= 2.2 => LeanTo(d, depth, room, ref rng),
                Change.Dormer when !d.GableFront && d.Dormer == HouseDormer.None && d.Storeys >= 1.5 && d.Roof is HouseRoof.Gable or HouseRoof.Gambrel
                    => d with { Dormer = HouseDormer.Shed, Dormers = 1 },
                Change.Shingled when !d.Shingle => d with { Shingle = true, Painted = false, Paint = Pick(looks.Stains, ref rng) },
                Change.Shutters when d.Shutters < 0 => d with { Shutters = Pick(looks.Doors, ref rng) },
                Change.Stovepipe when d.Chimney != HouseChimney.Pipe => d with { Chimney = HouseChimney.Pipe },
                Change.BuiltUp when d.Storeys <= 1.5 && d.Roof is not HouseRoof.Flat => d with { Storeys = d.Storeys + 0.5 },
                _ => null,
            };
            if (to is null)
                continue;
            d = to;
            n++;
        }
        return d;
    }

    /// <summary>A lean-to: a one-storey wing built on the side, narrower than an ell, toward the back of the house.</summary>
    static HouseDesign LeanTo(HouseDesign d, double depth, double room, ref Pcg32 rng)
    {
        double w = Math.Min(room, rng.Range(2.2, 3.2)), deep = Math.Min(depth - 0.4, rng.Range(2.6, Math.Max(2.7, depth - 0.6)));
        return d with { Ell = rng.Chance(0.5) ? 1 : -1, EllWidth = w, EllDepth = deep, EllSetback = Math.Max(0.3, depth - deep - 0.1), EllTall = false };
    }

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
