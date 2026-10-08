using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// Somebody's six traits (tuning/townsfolk.json; ARCHITECTURE §8 note 474), each from −1 to 1: how hard they keep the
/// custom, how much of it they'll tell, their nerve, how raw their grief is, how warm they are to those from away, and
/// how much they try to make the world tolerable.
/// </summary>
public readonly record struct TownTraits(double Keeping, double Telling, double Nerve, double Grief, double Welcome, double Hope)
{
    /// <summary>The traits' names in the content files, in this order (never a dictionary's).</summary>
    public static readonly string[] Axes = ["keeping", "telling", "nerve", "grief", "welcome", "hope"];

    public double this[int axis] => axis switch
    {
        0 => Keeping,
        1 => Telling,
        2 => Nerve,
        3 => Grief,
        4 => Welcome,
        _ => Hope,
    };

    static TownTraits Of(Func<int, double> at) => new(at(0), at(1), at(2), at(3), at(4), at(5));

    /// <summary>A lean from the content (its axes by name; one it doesn't name is nothing).</summary>
    public static TownTraits From(IReadOnlyDictionary<string, double>? lean) =>
        lean is null ? default : Of(i => lean.TryGetValue(Axes[i], out double v) ? v : 0);

    public static TownTraits operator +(TownTraits a, TownTraits b) => Of(i => a[i] + b[i]);

    public TownTraits Clamped()
    {
        var me = this;
        return Of(i => Math.Clamp(me[i], -1, 1));
    }

    /// <summary>How far these traits lie along a direction (the dot product over the direction's length).</summary>
    public double Along(TownTraits direction)
    {
        double dot = 0, length = 0;
        for (int i = 0; i < Axes.Length; i++)
            (dot, length) = (dot + this[i] * direction[i], length + direction[i] * direction[i]);
        return length > 0 ? dot / Math.Sqrt(length) : 0;
    }

    /// <summary>
    /// A draw about nothing, about <paramref name="spread"/> either way: three uniforms summed, so most people are near
    /// their town's middle and a few are far out, without a transcendental function (the Sim's are exact on every machine).
    /// </summary>
    public static TownTraits Draw(ref Pcg32 rng, double spread)
    {
        var r = rng;
        var t = Of(_ => (r.NextDouble() + r.NextDouble() + r.NextDouble() - 1.5) * 2 * spread);
        rng = r;
        return t;
    }
}

/// <summary>
/// Who a townsperson is (note 474): their traits, the temperament those make them (and how strongly: its score), the
/// people they come from, their given name and surname, the byname the town knows them by ("Holy Annie"; empty for
/// none), and whether they were born before the fall ("elder", "adult") or inside the walls ("after").
/// </summary>
public sealed record TownPersonality(TownTraits Traits, string Temperament, double Strength, string Heritage, string Given, string Surname,
    string Byname, string Generation)
{
    /// <summary>What they're called: the byname with the surname after it, the Cape Breton way, or the plain name.</summary>
    public string Name => (Byname.Length > 0 ? $"{Byname} {Surname}" : $"{Given} {Surname}").Trim();
}

/// <summary>Mirror of content/tuning/townsfolk.json (note 474). Field docs live in that file.</summary>
public sealed record TownFolkTuning
{
    public const string File = "tuning/townsfolk.json";

    public double Spread { get; init; } = 0.42;
    public double HouseholdSpread { get; init; } = 0.22;
    public double TownSpread { get; init; } = 0.18;
    public Dictionary<string, Dictionary<string, double>> Cultures { get; init; } = [];
    public Dictionary<string, Dictionary<string, double>> Roles { get; init; } = [];
    public Dictionary<string, Dictionary<string, double>> Parts { get; init; } = [];
    public Dictionary<string, double> Absent { get; init; } = [];
    public double TemperamentFloor { get; init; } = 0.35;
    public Dictionary<string, Dictionary<string, double>> Temperaments { get; init; } = [];
    public FolkTelling Telling { get; init; } = new();
    public FolkNaming Names { get; init; } = new();
}

public sealed record FolkTelling
{
    public double Close { get; init; } = -0.45;
    public double Open { get; init; } = 0.45;
}

public sealed record FolkNaming
{
    public double[] Dominant { get; init; } = [0.45, 0.7];
    public double[] Second { get; init; } = [0.15, 0.3];
    public double FortName { get; init; } = 0.7;
    public Dictionary<string, Dictionary<string, double>> ByTrade { get; init; } = [];
    public double MixedGiven { get; init; }
    public double AfterCulture { get; init; }
    public double AfterLean { get; init; } = 0.15;
    public double Byname { get; init; }
    public double BynameStrong { get; init; }
    public double Strong { get; init; } = 0.65;
    public double RoleByname { get; init; }
    public double Patronymic { get; init; }
}

/// <summary>Mirror of content/world/townsfolk.json (note 474): the heritages' names and the temperaments' words.</summary>
public sealed record TownFolkWriting
{
    public const string File = "world/townsfolk.json";

    public FolkHeritage[] Heritages { get; init; } = [];
    public FolkAfterNames After { get; init; } = new();
    public Dictionary<string, string[]> RoleBynames { get; init; } = [];
    public Dictionary<string, string[]> TradeBynames { get; init; } = [];
    public Dictionary<string, FolkTemperament> Temperaments { get; init; } = [];
    /// <summary>Nicki's party (note 487); null without it.</summary>
    public FolkParty? Party { get; init; }
}

/// <summary>
/// Nicki's party (world/townsfolk.json <c>party</c>; note 487): her name and her card's titles, what she says first (the
/// wine), the rest of what she says, her guests' lines, and the wine on the table.
/// </summary>
public sealed record FolkParty
{
    public string Name { get; init; } = "Nicki";
    public string Title { get; init; } = "";
    public string GuestTitle { get; init; } = "";
    public string[] Offer { get; init; } = [];
    public string[] Host { get; init; } = [];
    public string[] Guests { get; init; } = [];
    public required TownCentrepiece Wine { get; init; }
}

/// <summary>One of the province's peoples: its given names, its surnames, and (the Gaelic) its patronymics' fathers.</summary>
public sealed record FolkHeritage
{
    public required string Id { get; init; }
    public required string[] Given { get; init; }
    public required string[] Surnames { get; init; }
    public bool Patronymic { get; init; }
    public string[] Fathers { get; init; } = [];
}

/// <summary>What children born inside the walls are called: by what their parents kept, hoped or lost, or the custom's own.</summary>
public sealed record FolkAfterNames
{
    public string[] Virtue { get; init; } = [];
    public string[] Daylight { get; init; } = [];
    public string[] Plain { get; init; } = [];
    public Dictionary<string, string[]> Culture { get; init; } = [];
}

public sealed record FolkTemperament
{
    public string[] Bynames { get; init; } = [];
    public string[] Lines { get; init; } = [];
}

/// <summary>The townsfolk's matrix and names, as loaded; null on <see cref="TownContent"/> where the content has none.</summary>
public sealed record TownFolkContent(TownFolkTuning Tuning, TownFolkWriting Writing)
{
    public static TownFolkContent? Load(string content)
    {
        string tuning = Path.Combine(content, TownFolkTuning.File), writing = Path.Combine(content, TownFolkWriting.File);
        return System.IO.File.Exists(tuning) && System.IO.File.Exists(writing)
            ? new TownFolkContent(DataFile.Load<TownFolkTuning>(tuning), DataFile.Load<TownFolkWriting>(writing))
            : null;
    }

    /// <summary>Every heritage's surnames, in the file's order, each once.</summary>
    public IEnumerable<string> Surnames => Writing.Heritages.SelectMany(h => h.Surnames).Distinct(StringComparer.Ordinal);
}

/// <summary>
/// One town's people as persons (GDD §3.1; ARCHITECTURE §8 note 474): who each is, from the town's custom, its mood, their
/// household's, their job or part in the house and their own draw (the personality matrix, tuning/townsfolk.json); and
/// what they're called, from the town's mix of the province's peoples, when they were born, and who they are (a zealous
/// house's daughter is Patience; the widow who talks to her husband at the window is Black Flora). Every draw is on its
/// own stream, by the person's place in the town, so the same on every machine.
/// </summary>
public sealed class TownFolk
{
    readonly TownFolkTuning _t;
    readonly TownFolkWriting _w;

    public TownFolkTuning Tuning => _t;
    public TownFolkWriting Writing => _w;
    readonly TownWriting _words;
    readonly ulong _seed;
    readonly string _culture, _trade;
    readonly TownTraits _town;
    readonly Dictionary<string, FolkHeritage> _bySurname = new(StringComparer.Ordinal);
    readonly string[] _temperaments;

    /// <summary>The town's peoples and their shares, the dominant first.</summary>
    public IReadOnlyList<(string Heritage, double Share)> Mix { get; }

    public TownFolk(TownFolkContent folk, TownWriting words, TownSite site, string culture, ulong seed)
    {
        (_t, _w, _words, _seed, _culture, _trade) = (folk.Tuning, folk.Writing, words, seed, culture, site.Industry);
        foreach (var h in _w.Heritages)
            foreach (string s in h.Surnames)
                _bySurname.TryAdd(s, h);
        _temperaments = [.. _t.Temperaments.Keys.Order(StringComparer.Ordinal)];
        var mood = Streams.Rng(seed, "folk.town");
        _town = TownTraits.From(_t.Cultures.GetValueOrDefault(culture)) + TownTraits.Draw(ref mood, _t.TownSpread);
        Mix = DrawMix(site);
    }

    /// <summary>
    /// The town's mix: the dominant people the fort's name's (Fort Boudreau's Acadians) or by its trade's leans, a second
    /// by the same leans, the rest over the others.
    /// </summary>
    List<(string, double)> DrawMix(TownSite site)
    {
        var all = _w.Heritages;
        if (all.Length == 0)
            return [];
        var n = _t.Names;
        var rng = Streams.Rng(_seed, "folk.mix");
        var lean = n.ByTrade.GetValueOrDefault(_trade);
        double Weight(FolkHeritage h) => lean is not null && lean.TryGetValue(h.Id, out double w) ? w : 1;
        FolkHeritage? Draw(IEnumerable<FolkHeritage> from) => rng.Weighted([.. from.Select(h => (h, Weight(h)))]);
        string fort = site.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        bool byName = _bySurname.TryGetValue(fort, out var named) & rng.Chance(n.FortName);
        var first = byName ? named! : Draw(all) ?? all[0];
        double share = rng.Range(n.Dominant);
        var mix = new List<(string, double)> { (first.Id, share) };
        if (all.Length > 1 && Draw(all.Where(h => h != first)) is { } second)
        {
            double s2 = Math.Min(rng.Range(n.Second), 1 - share);
            mix.Add((second.Id, s2));
            var rest = all.Where(h => h != first && h != second).ToList();
            foreach (var h in rest)
                mix.Add((h.Id, (1 - share - s2) / rest.Count));
        }
        return mix;
    }

    FolkHeritage? Heritage(string id) => _w.Heritages.FirstOrDefault(h => h.Id == id);

    FolkHeritage? DrawHeritage(ref Pcg32 rng) => Heritage(rng.Weighted([.. Mix.Select(m => (m.Heritage, m.Share))]) ?? "");

    /// <summary>
    /// The town's families in the order their houses are given them: each the next of a people drawn by the town's mix,
    /// never one twice (a household shares one surname, and so its people).
    /// </summary>
    public List<string> Families(Pcg32 rng)
    {
        var left = _w.Heritages.Select(h => (h.Id, Names: h.Surnames.Where(s => _bySurname[s] == h).ToList())).ToList();
        var order = new List<string>();
        while (true)
        {
            var heritage = rng.Weighted([.. left.Select(l => (l, l.Names.Count > 0 ? Mix.FirstOrDefault(m => m.Heritage == l.Id).Share : 0))]);
            if (heritage.Names is not { Count: > 0 } names)
                return order;
            int k = (int)(rng.NextDouble() * names.Count);
            order.Add(names[k]);
            names.RemoveAt(k);
        }
    }

    /// <summary>A household's shared mood (families resemble), the same for each of its people.</summary>
    TownTraits Household(int house)
    {
        var rng = Streams.Rng(_seed, "folk.house", "", house);
        return TownTraits.Draw(ref rng, _t.HouseholdSpread);
    }

    /// <summary>
    /// Somebody's traits: the custom and the town's mood, their household's (and its loss), their job's or their part in
    /// the house, and their own draw (the <paramref name="index"/>'th person's stream).
    /// </summary>
    public TownTraits Traits(int index, string role, string part, int house, bool absent)
    {
        var rng = Streams.Rng(_seed, "folk.person", "", index);
        var t = _town + TownTraits.Draw(ref rng, _t.Spread);
        if (house >= 0)
        {
            t += Household(house) + TownTraits.From(_t.Parts.GetValueOrDefault(part.Length > 0 ? part : "parent"));
            if (absent)
                t += TownTraits.From(_t.Absent);
        }
        else
            t += TownTraits.From(_t.Roles.GetValueOrDefault(role));
        return t.Clamped();
    }

    /// <summary>The temperament the traits lie furthest along, and how far; "plain" short of the floor.</summary>
    public (string Id, double Strength) Temperament(TownTraits traits)
    {
        (string id, double best) = ("plain", _t.TemperamentFloor);
        foreach (string k in _temperaments)
            if (traits.Along(TownTraits.From(_t.Temperaments[k])) is var score && score > best)
                (id, best) = (k, score);
        return (id, id == "plain" ? 0 : best);
    }

    /// <summary>
    /// The <paramref name="index"/>'th person of the town: their traits and temperament, then their name. A household's
    /// people have its <paramref name="family"/>; a child is named after the walls went up by what the house kept, hoped
    /// or lost (<paramref name="absent"/>: the one it lost); the name is one nobody else in <paramref name="taken"/> has.
    /// </summary>
    public TownPersonality Person(int index, string role, string part, int house, string? family, string? absent, ISet<string> taken)
    {
        var traits = Traits(index, role, part, house, absent is not null);
        var (temperament, strength) = Temperament(traits);
        var rng = Streams.Rng(_seed, "folk.name", "", index);
        var n = _t.Names;
        var heritage = (family is not null ? _bySurname.GetValueOrDefault(family) : null) ?? DrawHeritage(ref rng);
        string surname = family ?? (heritage is { Surnames.Length: > 0 } ? rng.Pick(heritage.Surnames) : rng.Pick(_words.Surnames));
        string generation = part == "child" ? "after" : part == "elder" || role == "driver" ? "elder" : "adult";
        for (int tries = 0; ; tries++)
        {
            string given = generation == "after" ? AfterName(house, absent, ref rng) : Given(heritage, ref rng);
            string byname = Byname(role, part, given, heritage, temperament, strength, ref rng);
            var who = new TownPersonality(traits, temperament, strength, heritage?.Id ?? "", given, surname, byname, generation);
            if (taken.Add(who.Name) || tries > 40)
                return who;
        }
    }

    /// <summary>An old name, of their people's (or now and then another of the town's: Jean-Guy MacNeil).</summary>
    string Given(FolkHeritage? heritage, ref Pcg32 rng)
    {
        if (rng.Chance(_t.Names.MixedGiven) && DrawHeritage(ref rng) is { } other)
            heritage = other;
        return heritage is { Given.Length: > 0 } ? rng.Pick(heritage.Given) : rng.Pick(_words.FirstNames);
    }

    /// <summary>
    /// A child's after-name: the custom's own now and then; else by the house's strongest lean past the floor (zealous: a
    /// virtue; hopeful: a daylight name; raw with a loss: the lost one's); else a plain short name.
    /// </summary>
    string AfterName(int house, string? absent, ref Pcg32 rng)
    {
        var a = _w.After;
        if (a.Culture.TryGetValue(_culture, out var own) && own.Length > 0 && rng.Chance(_t.Names.AfterCulture))
            return rng.Pick(own);
        var mood = _town + (house >= 0 ? Household(house) : default) + (absent is not null ? TownTraits.From(_t.Absent) : default);
        (string[] stock, double lean) = (a.Plain, _t.Names.AfterLean);
        if (mood.Keeping > lean && a.Virtue.Length > 0)
            (stock, lean) = (a.Virtue, mood.Keeping);
        if (mood.Hope > lean && a.Daylight.Length > 0)
            (stock, lean) = (a.Daylight, mood.Hope);
        if (absent is not null && mood.Grief > lean)
            return absent;
        return stock.Length > 0 ? rng.Pick(stock) : rng.Pick(_words.FirstNames);
    }

    /// <summary>
    /// What the town calls them, or empty: a strong temperament's byname; else now and then their job's (a hand's by the
    /// trade), a Gaelic patronymic, or their temperament's.
    /// </summary>
    string Byname(string role, string part, string given, FolkHeritage? heritage, string temperament, double strength, ref Pcg32 rng)
    {
        var n = _t.Names;
        bool strong = strength >= n.Strong;
        if (!rng.Chance(strong ? n.BynameStrong : n.Byname))
            return "";
        string[] mine = _w.Temperaments.TryGetValue(temperament, out var tm) ? tm.Bynames : [];
        string Fill(string form) => form.Replace("{first}", given);
        if (strong && mine.Length > 0)
            return Fill(rng.Pick(mine));
        string[] job = part.Length > 0 ? []
            : role == "hand" && _w.TradeBynames.TryGetValue(_trade, out var trade) ? trade
            : _w.RoleBynames.GetValueOrDefault(role) ?? [];
        if (job.Length > 0 && rng.Chance(n.RoleByname))
            return Fill(rng.Pick(job));
        if (heritage is { Patronymic: true, Fathers.Length: > 0 } && part != "child" && rng.Chance(n.Patronymic))
        {
            string father = rng.Pick(heritage.Fathers), grandfather = rng.Pick(heritage.Fathers);
            return rng.Chance(0.5) && grandfather != father ? $"{given} {father} {grandfather}" : $"{given} {father}";
        }
        return mine.Length > 0 ? Fill(rng.Pick(mine)) : "";
    }
}
