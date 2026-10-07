using System.Net;
using System.Text.Json;
using Ballast;
using Ballast.Net;
using Ballast.Online;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>What a joining machine needs to build the host's world. Travels in the Welcome as JSON.</summary>
/// <param name="Route">A route spec (<c>frontier:7</c>), or null to play <paramref name="Line"/>.</param>
public sealed record SessionSetup(string? Route = null, string Line = "test-loop", int Cars = 6, bool Enemies = true)
{
    /// <summary>The campaign's upgrades (spec F.3). Every machine applies the same list to the same content.</summary>
    public IReadOnlyList<string> Upgrades { get; init; } = [];
    /// <summary>
    /// The campaign's spare repair kits (GDD v1.4 App. E.12 question 4): each starts the night in a crew locker beside the
    /// train's own. Every machine builds the same train from it.
    /// </summary>
    public int SpareKits { get; init; }
    /// <summary>
    /// The contract's freight (GDD §9 "choose freight contracts", §19; note 182): what every loaded car leaves the fortress
    /// carrying. None is goods. Every machine builds the same consist from it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public CargoKind Cargo { get; init; }
    /// <summary>The departure's stores (GDD §9; campaign.json <c>stores</c>, note 182): crates of powder and shot, spare lamps, spare extinguishers.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Powder { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int SpareLamps { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int SpareExtinguishers { get; init; }
    /// <summary>
    /// The host's only: this host has never had a child's call, so tonight's first is a real child (GDD App. B.6, A.6 "the first
    /// one a host player ever meets is always real"). The app reads it from the host's profile; the Sim gets the bool.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool FirstChildReal { get; init; }
    /// <summary>Where the engine's front starts, along the line; null for the fortress yard. A resumed night starts where it was saved.</summary>
    public double? Start { get; init; }
    /// <summary>The host's only: a resumed night's own line, from its save (linegen plan §17.4), rather than generated afresh.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Sim.LineGen.LinePlan? Plan { get; init; }
    /// <summary>
    /// The host's only: the derailment's shuffle bag going into the night (GDD v1.4 App. E.6), from the campaign save or, for
    /// a quick night, the app's data. The host draws from it; clients are sent the track, not the bag.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Sim.Music.MusicBag? MusicBag { get; init; }
    /// <summary>The host's only: the campaign's looks by player name (GDD v1.4 App. D.8; note 181), going into the night.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, string>? Identities { get; init; }
    /// <summary>
    /// The custom of the town the crew left last night (note 304; App. F.1: "each town has its own odd culture, different
    /// from the last"): tonight's town won't share it. Every machine makes the town, so a joiner is sent it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LastTown { get; init; }
    /// <summary>The host's line's fingerprint: a joiner whose own generated line differs (another generator version) is refused.</summary>
    public string? PlanPrint { get; init; }
    /// <summary>The host's terrain's fingerprint (linegen plan §17.3): a joiner whose ground comes out differently is refused.</summary>
    public string? TerrainPrint { get; init; }

    /// <summary>The night's tunings, with the upgrades applied.</summary>
    public Sim.Campaign.Loadout Loadout(string content)
    {
        var loadout = new Sim.Campaign.Loadout(
            DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File)),
            DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File)),
            DataFile.Load<CombatTuning>(Path.Combine(content, CombatTuning.File)),
            DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)));
        loadout = Sim.Campaign.Campaign.WithSpareKits(loadout, SpareKits);
        if (Powder > 0 || SpareLamps > 0 || SpareExtinguishers > 0)
            loadout = Sim.Campaign.Campaign.WithStores(DataFile.Load<Sim.Campaign.CampaignTuning>(Path.Combine(content, Sim.Campaign.CampaignTuning.File)),
                loadout, new Sim.Campaign.Stores(Powder, SpareLamps, SpareExtinguishers));
        return Upgrades.Count == 0 ? loadout
            : Sim.Campaign.Campaign.Apply(DataFile.Load<Sim.Campaign.CampaignTuning>(Path.Combine(content, Sim.Campaign.CampaignTuning.File)), Upgrades, loadout);
    }

    /// <summary>
    /// The host's tuning, file by file (content/tuning/*.json), and the line generator's files together (content/linegen),
    /// line endings normalised. A joiner whose content differs would predict (and generate) a different game from the
    /// one the host runs, so it's refused by name.
    /// </summary>
    public Dictionary<string, string>? Content { get; init; }

    /// <summary>The mods the host plays with, in order ("name version", T49): named to a joiner whose content differs.</summary>
    public IReadOnlyList<string> Mods { get; init; } = [];

    // Keyed short (a tuning file by its name, the line generator's files as one) with a 4-byte hash, so the Welcome that
    // carries them fits one packet with room for more files: enough to tell content apart, it's not a security check.
    const string LineGenKey = "linegen";

    public static Dictionary<string, string> HashContent(string content)
    {
        static string Hash(string text) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))), 0, 4).ToLowerInvariant();
        var hashes = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(content, "tuning"), "*.json").Order(StringComparer.Ordinal))
            hashes[Path.GetFileNameWithoutExtension(file)] = Hash(File.ReadAllText(file));
        // The line generator's files, as one: every machine generates the night's line from them (linegen plan §17.3),
        // and the Welcome that carries these has to fit one packet.
        string linegen = Path.Combine(content, Sim.LineGen.LineGenConfig.Directory);
        if (Directory.Exists(linegen))
            hashes[LineGenKey] = Hash(string.Concat(Directory.EnumerateFiles(linegen, "*.json").Order(StringComparer.Ordinal)
                .Select(f => Path.GetFileName(f) + "\n" + File.ReadAllText(f))));
        // The world's words (note 304): the towns are made from them alike on every machine, and their walls are solid.
        string world = Path.Combine(content, "world");
        if (Directory.Exists(world))
            hashes[WorldKey] = Hash(string.Concat(Directory.EnumerateFiles(world, "*.json").Order(StringComparer.Ordinal)
                .Select(f => Path.GetFileName(f) + "\n" + File.ReadAllText(f))));
        return hashes;
    }

    const string WorldKey = "world";

    /// <summary>Files that differ between the host's content and this machine's.</summary>
    public IReadOnlyList<string> ContentDifferences(string content)
    {
        if (Content is null)
            return [];
        var mine = HashContent(content);
        return [.. Content.Keys.Union(mine.Keys).Where(k => Content.GetValueOrDefault(k) != mine.GetValueOrDefault(k)).Order()
            .Select(k => k == LineGenKey ? "linegen/*.json" : k == WorldKey ? "world/*.json" : $"tuning/{k}.json")];
    }

    /// <summary>Why a joiner's refused: the files that differ, and the mods on each side when they're not the same.</summary>
    public static string Refusal(IReadOnlyList<string> differ, IReadOnlyList<string> hostMods, IReadOnlyList<string> mine)
    {
        string text = $"your content differs from the host's: {string.Join(", ", differ)}";
        static string List(IReadOnlyList<string> m) => m.Count == 0 ? "none" : string.Join(", ", m);
        return hostMods.SequenceEqual(mine) ? text : $"{text} (the host's mods: {List(hostMods)}; yours: {List(mine)})";
    }

    /// <summary>Compact: the Welcome carrying it has to fit one datagram (1200 bytes), and indented, on Windows every line
    /// break is two bytes (a Windows host's Welcome went over once the tuning files numbered 19).</summary>
    public string Encode() => JsonSerializer.Serialize(this, Compact);
    static readonly JsonSerializerOptions Compact = new(DataFile.Options) { WriteIndented = false };
    public static SessionSetup Decode(string json) => JsonSerializer.Deserialize<SessionSetup>(json, DataFile.Options) ?? new SessionSetup();

    /// <summary>The world this setup describes, identically on every machine.</summary>
    /// <param name="authority">The host's world runs the night; a joiner's mirrors it.</param>
    public (World World, Route? Route) Build(string content, bool authority = false)
    {
        var loadout = Loadout(content);
        var (trainTuning, boiler, combat) = (loadout.Train, loadout.Boiler, loadout.Combat);
        Route? route = null;
        RailLine line;
        var runTuning = DataFile.Load<Sim.Run.RunTuning>(Path.Combine(content, Sim.Run.RunTuning.File));
        // A night leaves the fortress part loaded; the facilities fill the rest (GDD §17-18).
        var consist = Consist.Uniform(trainTuning, Cars, Route is { Length: > 0 } ? runTuning.DepartureLoad : 1).Carrying(Cargo);
        double start = 600;
        if (Route is { Length: > 0 } spec)
        {
            route = Plan is { } saved ? Sim.LineGen.Routes.FromPlan(content, saved) : Sim.LineGen.Routes.Generate(content, spec, Cars);
            line = route.Build();
            // At the fortress's gate, ready to depart (run.json departShortOfGateM), as in the prototype.
            start = runTuning.DepartFrom(route.GateOr(DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File)).YardLength), consist.LengthMetres);
        }
        else
        {
            line = RailLine.Load(Path.Combine(content, "lines", Line + ".json"));
        }
        start = Start ?? start;
        var train = new TrainOnLine(new TrainDynamics(consist), line, start, boiler);
        var world = new World(train, combat) { WreckTuning = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File)) };
        if (route is not null)
        {
            var routeTuning = RouteTuning.Load(content);
            world.EnableSwitches(routeTuning.Junctions);
            world.EnableRun(runTuning, route, route.GateOr(routeTuning.YardLength), authority,
                DataFile.Load<Sim.Run.FacilityTuning>(Path.Combine(content, Sim.Run.FacilityTuning.File)),
                DataFile.Load<Sim.Stops.LootTuning>(Path.Combine(content, Sim.Stops.LootTuning.File)));
            world.EnableLineside(DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File)), route);
            // GDD App. D: once the gate has opened, the dead come back only through the route's Holdouts.
            world.EnableHoldouts(DataFile.Load<Sim.Run.HoldoutTuning>(Path.Combine(content, Sim.Run.HoldoutTuning.File)), route);
            // The departure fortress's town (note 304), after the run: its walls go up beside the stops'.
            if (Sim.Towns.TownContent.Load(content) is { } towns)
                world.EnableTown(towns, route, route.GateOr(routeTuning.YardLength), loadout.Enemies?.Director.Roster ?? [], LastTown);
        }
        // A client mirrors the enemies, and needs their tuning for what it predicts from them (the Weight's drag, T59) and
        // for bots reading them; the host's world gets its director from HostSession.EnableEnemies.
        if (!authority && Enemies && route is not null && loadout.Enemies is { } enemies)
            world.EnableEnemies(enemies, route, route.Seed, crew: 1, authority: false);
        return (world, route);
    }
}

/// <summary>
/// A networked game (T13, T20): the host runs <see cref="HostSession"/> and plays through its own
/// <see cref="ClientSession"/> over localhost, exactly like everyone else, so the host has no advantage and
/// no separate code path. Friends reach the host over UDP (LAN, direct IP) or through a platform lobby (Steam),
/// often both at once. A joiner builds its world from the host's <see cref="SessionSetup"/>.
/// </summary>
public sealed class NetPlaySession : IPlaySession, IDisposable
{
    public const int DefaultPort = 27450;
    public const string Game = "darkterritory";
    /// <summary>
    /// A hosted game's values in its platform lobby, for the browser (the engine's own are on <see cref="Lobby"/>). Aboard
    /// is the places taken against the crew cap (note 254), max the cap, full "1" while there's no room.
    /// </summary>
    public const string NameKey = "name", TierKey = "tier", RunKey = "run", AboardKey = "aboard", MaxKey = "max", FullKey = "full";

    /// <summary>The crew cap the content sets (player.json crew.cap, GDD §1 "2–8"; a mod can raise it: note 254).</summary>
    public static int CrewCap(string content) => DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File)).Crew.Places;
    readonly ITransport? _hostTransport;
    readonly UdpTransport? _udp;
    ITransport _clientTransport;
    IConnectionInfo _link;
    readonly List<Crewmate> _crew = new();
    readonly List<CarFrame> _frames = new();
    PlayerState _previous;

    NetPlaySession(HostSession? host, ITransport? hostTransport, UdpTransport? udp, ClientSession client, ITransport clientTransport,
        SessionSetup setup, Route? route, Lobby? lobby)
    {
        Host = host;
        _hostTransport = hostTransport;
        _udp = udp;
        Client = client;
        _clientTransport = clientTransport;
        _link = (IConnectionInfo)clientTransport;
        Setup = setup;
        Route = route;
        Lobby = lobby;
    }

    /// <summary>The platform lobby this game is hosted in or was joined through, if any.</summary>
    public Lobby? Lobby { get; }
    public HostSession? Host { get; }
    public ClientSession Client { get; }
    public SessionSetup Setup { get; }
    public Route? Route { get; }
    public TrainOnLine Train => Client.Train;
    /// <summary>Seconds since this client first saw the train come off (T117), host or not: the wreck's own clock is the host's.</summary>
    public double WreckSeconds { get; private set; }
    /// <summary>The derailment's cinematic: the camera off the eye and on the wreck, the run's end held back till it's over.</summary>
    /// <summary>What derailed it (T121): hosting, the host's world says; joined, the incident report's line.</summary>
    public string? DerailCause => Host?.World.DerailCause is { Length: > 0 } c ? c
        : World.DerailCause is { Length: > 0 } mine ? mine
        : World.Run?.Report?.Lines.LastOrDefault(l => l.Kind == Sim.Run.IncidentKind.Derailed)?.Text;

    /// <summary>The host's shuffle bag as it stands (E.6), to keep with the campaign save or the app's data; null on a joiner.</summary>
    public Sim.Music.MusicBag? MusicBag => Host?.World.Music?.Bag;

    public bool WreckCinematic => Train.Wreck is not null && WreckSeconds < DerailSequence.Length(SequenceTuning, Film);

    /// <summary>The sequence's timing with this player's own first person (<see cref="IPlaySession.SequenceTuning"/>).</summary>
    public WreckTuning SequenceTuning => DerailSequence.TuningFor(World.WreckTuning, Film, PlayerId);

    Task<WreckFilm?>? _shooting;

    /// <summary>
    /// GDD v1.4 App. E.2 steps 3-4: the film, shot here from the host's start once it's in (off the frame loop: a few hundred
    /// steps of the wreck and the crew's ragdolls), the same as every other machine shoots it. Null till it's ready.
    /// </summary>
    public WreckFilm? Film => _shooting is { IsCompletedSuccessfully: true } done ? done.Result : null;

    public bool Skippable =>
        Film is { } film && DerailSequence.Beat(SequenceTuning, WreckSeconds, film) == DerailBeat.Film
            && DerailSequence.FilmSeconds(SequenceTuning, WreckSeconds) >= film.SkippableFrom
            && DerailSequence.FilmSeconds(SequenceTuning, WreckSeconds) < film.CauseAt
        || StrandedOutro && OutroSeconds >= World.WreckTuning.Stranded.SkipAfterSeconds;
    public double OutroSeconds { get; private set; }

    public (IReadOnlyList<Sim.Enemies.EnemyKind> Options, Sim.Enemies.EnemyKind? Cast)? Ballot => Client.Ballot;

    public BallotPicker Picker { get; } = new();

    /// <summary>Dead with a ballot still to cast (D.11): the number keys (or the headset's stick) are the ballot's, not the hotbar's.</summary>
    public bool Voting => !Player.Alive && Ballot is { Cast: null, Options.Count: > 0 } && World.Run is not { Over: true };

    string? _cue;
    double _cueSeconds;

    /// <summary>D.11: the newest cue to the dead, for a few seconds; a new one plays its sound (<see cref="TakeNewCue"/>).</summary>
    public string? VoteCue => _cueSeconds > 0 ? _cue : null;

    bool _newCue;

    /// <summary>Whether a cue arrived since last asked (the app plays the dead channel's chime for it).</summary>
    public bool TakeNewCue()
    {
        bool fresh = _newCue;
        _newCue = false;
        return fresh;
    }

    void StepCues()
    {
        _cueSeconds = Math.Max(0, _cueSeconds - SimConstants.TickSeconds);
        foreach (var (kind, voters) in Client.VoteCues)
        {
            _cue = $"THE DEAD CALLED THE {Sim.Run.IncidentLog.Spoken(kind.ToString()).ToUpperInvariant()}: " +
                string.Join(", ", voters.Select(v => v == PlayerId ? "YOU" : Sim.Run.IncidentLog.NameOf(World, v).ToUpperInvariant()));
            _cueSeconds = 6;
            _newCue = true;
        }
        Client.VoteCues.Clear();
    }

    int _commendTo, _commendWhich;
    bool _commended;

    /// <summary>Everyone else in the session at run end, by id: who can be commended (D.12: anyone but yourself).</summary>
    IReadOnlyList<int> Commendable => [.. Client.RemoteIds.Select(id => (int)id).Where(id => id != PlayerId).Order()];

    public (string To, string What, bool Given)? CommendPick
    {
        get
        {
            if (World.Run is not { Over: true } || WreckCinematic || StrandedOutro || ClerkTally)
                return null;
            var mine = World.Commendations.FirstOrDefault(c => c.From == PlayerId);
            if (World.Commendations.Any(c => c.From == PlayerId))
                return (Sim.Run.IncidentLog.NameOf(World, mine.To).ToUpperInvariant(), Sim.Run.Commendations.StarterSet[mine.Which].ToUpperInvariant(), true);
            var choices = Commendable;
            if (choices.Count == 0)
                return null;
            int to = choices[(_commendTo % choices.Count + choices.Count) % choices.Count];
            int which = (_commendWhich % 5 + 5) % 5;
            return (Sim.Run.IncidentLog.NameOf(World, to).ToUpperInvariant(), Sim.Run.Commendations.StarterSet[which].ToUpperInvariant(), _commended);
        }
    }

    /// <summary>The picker's keys (D.12): step through who and which; give it (once).</summary>
    public void Commend(int stepTo, int stepWhich, bool give)
    {
        if (CommendPick is not { Given: false })
            return;
        _commendTo += stepTo;
        _commendWhich += stepWhich;
        if (!give)
            return;
        var choices = Commendable;
        Client.Commend(choices[(_commendTo % choices.Count + choices.Count) % choices.Count], (byte)((_commendWhich % 5 + 5) % 5));
        _commended = true;
    }

    List<string>? _manifest, _tally;
    List<double>? _manifestTimes, _tallyTimes;
    double _manifestSeconds = -1, _tallySeconds = -1;

    /// <summary>
    /// How long the yard's voice takes to say a line (GameAudio.Clerk; note 240), so the reading goes at its pace: the card
    /// typed as it's said, the end screen waiting for the last word. Null (no voice), a line every lineSeconds.
    /// </summary>
    public Func<string, double>? RadioPace { get; set; }

    public IReadOnlyList<string>? RadioReading =>
        _tally is not null && _tallySeconds >= 0 ? _tally
        : _manifest is not null && _manifestSeconds >= 0 && _manifestSeconds < Sim.Run.Radio.Length(_manifest, RadioTuning, _manifestTimes) ? _manifest
        : null;

    public double RadioSeconds => _tally is not null && _tallySeconds >= 0 ? _tallySeconds : _manifestSeconds;

    public IReadOnlyList<double>? RadioTimes => _tally is not null && _tallySeconds >= 0 ? _tallyTimes : _manifestTimes;

    public bool ClerkTally => _tally is not null && _tallySeconds < Sim.Run.Radio.Length(_tally, RadioTuning, _tallyTimes);

    List<double>? RadioTimesOf(List<string> lines) => RadioPace is { } pace ? Sim.Run.Radio.Times(lines, RadioTuning, pace) : null;

    Sim.Run.RadioTuning RadioTuning => World.Run?.Tuning.Radio ?? new();

    /// <summary>
    /// GDD §9: the dispatcher reads the manifest as the train first leaves the yard (the crew aboard then, by name); the clerk
    /// reads the tally once a delivered night's report is in. Each client times its own reading.
    /// </summary>
    void StepRadio()
    {
        if (World.Run is not { } run)
            return;
        // Note 267: unless the run's tuning has dropped it (the director's call on build 1121).
        if (_manifest is null && RadioTuning.Manifest && run.Phase != Sim.Run.RunPhase.Yard && !run.Over)
        {
            var crew = Client.RemoteIds.Select(id => (int)id).Append(PlayerId).Distinct().Order();
            _manifest = Sim.Run.Radio.Manifest(World, crew);
            _manifestTimes = RadioTimesOf(_manifest);
            _manifestSeconds = 0;
        }
        else if (_manifestSeconds >= 0)
            _manifestSeconds += SimConstants.TickSeconds;
        if (_tally is null && run.Report is { End: Sim.Run.RunEnd.Delivered } report)
        {
            _tally = Sim.Run.Radio.Tally(report);
            _tallyTimes = RadioTimesOf(_tally);
            _tallySeconds = 0;
        }
        else if (_tallySeconds >= 0)
            _tallySeconds += SimConstants.TickSeconds;
    }
    public bool StrandedOutro => World.Run?.End == Sim.Run.RunEnd.Stranded && OutroSeconds < World.WreckTuning.Stranded.Seconds;
    public World World => Client.World;
    public PlayerState Player => Client.Predicted;
    public TrainControls Controls => Client.Controls;
    public long Tick { get; private set; }
    /// <summary>The host's tick of the newest snapshot (T121): a joiner's own count started later than the host's.</summary>
    public long HostTick => Client.NewestSnapshotTick;
    public bool Lost { get; private set; }

    /// <summary>Hosts and joins it from this machine.</summary>
    /// <param name="port">UDP port for LAN and direct-IP joiners (0 = any free one), or null to take none: the host's
    /// own player then connects on a private localhost port.</param>
    /// <param name="online">A platform to host a lobby on as well (Steam): public, or friends-only when not <paramref name="listed"/>.</param>
    /// <param name="listed">Public: listed for anyone to find, on the local network (the beacon) and in the platform's lobby
    /// search. Private: no beacon and a friends-only lobby, so it's joined by invite or address only.</param>
    /// <param name="lobbyName">What the browser calls it; null, "&lt;host&gt;'s run".</param>
    /// <param name="beacon">The beacon to advertise on (a test's, on loopback); by default the usual broadcast one, when listed.</param>
    /// <param name="resume">A night's autosave (spec E): start again from the facility it last left.</param>
    /// <param name="bots">Bot crewmates to play with (T89: a night alone, with a crew). Each is a client of this host over
    /// localhost like anyone else; the first drives (first aboard takes the cab), so the human can go where the trouble is.</param>
    public static NetPlaySession HostGame(string content, SessionSetup setup, int? port = DefaultPort, int expectedCrew = 4, IOnlineBackend? online = null,
        Sim.Campaign.RunCheckpoint? resume = null, int bots = 0, bool listed = true, string? lobbyName = null, LanBeacon? beacon = null)
    {
        var playerTuning = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
        // Bots hold crewmates, so they count against the cap (note 254); the host's own player always has a place.
        int cap = playerTuning.Crew.Places;
        bots = Math.Clamp(bots, 0, cap - 1);
        setup = setup with
        {
            Content = SessionSetup.HashContent(content),
            Mods = ContentMods.MountedIn(content),
            Start = resume?.Front ?? setup.Start,
            Plan = resume?.Plan is { } saved ? Sim.LineGen.LinePlan.Decompress(saved) : setup.Plan,
        };
        var loadout = setup.Loadout(content);
        var trainTuning = loadout.Train;
        var (hostWorld, route) = setup.Build(content, authority: true);
        // E.6: the host's world draws the derailment's track from the bag it brought (clients' worlds have no rotation).
        hostWorld.Music = Sim.Music.MusicRotation.Load(content, hostWorld.WreckTuning.Music, setup.MusicBag);
        // The host records the film on the derail tick, for the deaths (App. E.2 step 1; note 258): compiled now, off the
        // frame loop, so that tick isn't held up by the JIT as well (a second, the first time).
        var warmTuning = hostWorld.WreckTuning;
        _ = Task.Run(() => WreckFilm.Warm(warmTuning));
        // D.8: who each of the crew is, from the campaign, matched up as their names arrive.
        if (setup.Identities is { } identities)
            hostWorld.LooksByName = identities;
        // B.6: a host's first-ever child call is a real child (their profile says whether they've had one; note 182).
        hostWorld.NextChildReal = setup.FirstChildReal;
        setup = setup with { PlanPrint = route?.Plan?.Fingerprint(), TerrainPrint = TerrainOf(hostWorld)?.Print() };
        if (resume is not null)
            Restore(hostWorld, resume);
        var udp = port is { } p ? UdpTransport.Host(p) : UdpTransport.Host(0, bind: IPAddress.Loopback);
        ITransport hostTransport = online is null ? udp : new HostGroup(udp, OnlineTransport.Host(online));
        string tier = setup.Route is { } spec ? Sim.Route.Route.ParseSpec(spec).Tier.ToString() : "";
        string name = lobbyName is { Length: > 0 } n ? n : $"{(Messages.CleanName(PlayerName) is { Length: > 0 } set ? set : online?.NameOf(online.Me) ?? LocalName(null))}'s run";
        var lobby = online is null ? null : Lobby.Host(online, Game, Protocol.Version, cap, listed ? LobbyVisibility.Public : LobbyVisibility.FriendsOnly,
            new Dictionary<string, string>
            {
                [NameKey] = name,
                [TierKey] = tier,
                [RunKey] = Describe(setup, inYard: true),
                [AboardKey] = Invariant(1 + bots),
                [MaxKey] = Invariant(cap),
                [FullKey] = 1 + bots >= cap ? "1" : "0",
            });
        var host = new HostSession(hostTransport, hostWorld, trainTuning, playerTuning) { SessionInfo = setup.Encode() };
        hostWorld.EnableBodies();
        hostWorld.Stock();
        // Spec E: drop-in at POIs only: in the yard, stopped at a facility, or home. Mid-run joiners wait by
        // the train at the facility, "like a pickup".
        if (hostWorld.Run is { } run)
        {
            host.CanBoard = () => run.Phase is Sim.Run.RunPhase.Yard or Sim.Run.RunPhase.AtFacility or Sim.Run.RunPhase.Arrived;
            host.BoardAt = n => run.Phase == Sim.Run.RunPhase.Yard ? PlayerMotor.SpawnOnRoof(hostWorld.Train, 1 + (n - 1) % Math.Max(1, hostWorld.Train.OwnVehicles - 1), 0, playerTuning)
                : Beside(hostWorld.Train, n, playerTuning);
        }
        if (setup.Enemies && route is not null)
            // Planned for who'll be there: alone with bots, them and you; hosted online, the friends expected too (T115: a
            // local night alone was planned for four).
            host.EnableEnemies(loadout.Enemies!, route, route.Seed, online is null ? bots + 1 : Math.Max(bots + 1, expectedCrew));
        // The bot crew aboard first, so one of them has the cab. Their clients take the host's plan, not a fresh generation.
        BotCrew? crew = null;
        if (bots > 0)
        {
            crew = new BotCrew(hostWorld.Run is not null ? new CrewCalls() : null);
            var botSetup = setup with { Plan = route?.Plan ?? setup.Plan };
            for (int i = 0; i < bots; i++)
            {
                var (botWorld, _) = botSetup.Build(content);
                var botTransport = UdpTransport.Connect(new IPEndPoint(IPAddress.Loopback, udp.Port));
                var session = new ClientSession(botTransport, botWorld, trainTuning, playerTuning) { Name = playerTuning.BotName(i) };
                crew.Add(session, BotCrew.Make(i, bots, crew.Calls, loadout.Combat, playerTuning, (int)(route?.Seed ?? 1)), botTransport);
                var joining = System.Diagnostics.Stopwatch.StartNew();
                while (session.PlayerId is null && joining.Elapsed.TotalSeconds < 5)
                {
                    host.Step();
                    foreach (var (s, _) in crew.Bots)
                        s.Step(default);
                    Thread.Sleep(1);
                }
            }
        }
        var (clientWorld, _) = setup.Build(content);
        var clientTransport = UdpTransport.Connect(new IPEndPoint(IPAddress.Loopback, udp.Port));
        var client = new ClientSession(clientTransport, clientWorld, trainTuning, playerTuning) { Name = LocalName(lobby) };
        // The host's own player comes aboard before anyone else can: first aboard takes the cab.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (client.PlayerId is null && clock.Elapsed.TotalSeconds < 5)
        {
            host.Step();
            client.Step(default);
            Thread.Sleep(1);
        }
        if (!listed)
            beacon?.Dispose();
        return new NetPlaySession(host, hostTransport, port is null ? null : udp, client, clientTransport, setup, route, lobby)
        {
            BotCrew = crew,
            Listed = listed,
            LobbyName = name,
            Tier = tier,
            _beacon = listed ? beacon : null,
        };
    }

    /// <summary>The night as the browser's detail line says it.</summary>
    static string Describe(SessionSetup setup, bool inYard) =>
        $"{(setup.Route ?? setup.Line).ToUpperInvariant()}, {setup.Cars} CARS{(inYard ? ", IN THE YARD" : ", UNDER WAY")}";

    /// <summary>Hosting a public game: listed on the network and in the platform's lobby search.</summary>
    public bool Listed { get; private init; }
    /// <summary>What the browser calls this game.</summary>
    public string LobbyName { get; private init; } = "";
    string Tier { get; init; } = "";

    static string Invariant(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Note 254: the crew as the platform lobby says it, the places taken (<see cref="AboardKey"/>) of the cap
    /// (<see cref="MaxKey"/>), and at the cap closed: <see cref="FullKey"/> set, not joinable (so it drops out of the
    /// platform's search, and an invite or "Join Game" can't get in), its member limit the cap. A place freeing (a held one
    /// running out, a player leaving) opens it again. Each value is only written when it changes.
    /// </summary>
    public static void Advertise(Lobby lobby, int occupied, int cap)
    {
        bool full = occupied >= cap;
        lobby.SetData(AboardKey, Invariant(occupied));
        lobby.SetData(MaxKey, Invariant(cap));
        lobby.SetData(FullKey, full ? "1" : "0");
        lobby.SetMemberLimit(cap);
        lobby.SetJoinable(!full);
    }

    /// <summary>The bot crewmates this host is running, if any (T89).</summary>
    public BotCrew? BotCrew { get; private init; }

    /// <summary>
    /// The only human here (the host, and no one but bots, T113): nobody to talk to, so an open mic is only the room (the
    /// game's own sound through the speakers, most of all) and doesn't go into the loudness meter.
    /// </summary>
    public bool Alone => Host is { } h && h.Players.Count() - (BotCrew?.Bots.Count ?? 0) <= 1;

    static Sim.LineGen.TerrainField? TerrainOf(World world) => (world.Train.Line.Conditions as Sim.LineGen.PlanConditions)?.Terrain;

    static Sim.Campaign.RunCheckpoint Capture(World world, string route, int facility)
    {
        var train = world.Train;
        return new Sim.Campaign.RunCheckpoint(route, facility, world.Run!.Seconds, train.Dynamics.Distance, train.Boiler.Tender,
            [.. train.Vehicles.Select(v => new Sim.Campaign.CarState(v.Id, v.Load, v.Integrity, v.CargoIntegrity, v.Gun.Ammo, v.Cargo))],
            world.Holdouts?.Spent ?? [])
        { Plan = world.TrackPlan?.Compress() };
    }

    /// <summary>Puts a night back as it was saved: the cars, the coal, the clock, and the stops already made.</summary>
    static void Restore(World world, Sim.Campaign.RunCheckpoint c)
    {
        var train = world.Train;
        foreach (var car in c.Cars)
            if (car.Id < train.Vehicles.Count)
            {
                var v = train.Vehicles[car.Id];
                v.Load = car.Load;
                v.Integrity = car.Integrity;
                v.CargoIntegrity = car.CargoIntegrity;
                v.Gun = v.Gun with { Ammo = car.Ammo };
                // An older save has no cargo types: its loaded cars keep the goods they were built with.
                if (car.Cargo != CargoKind.None)
                    v.Cargo = car.Cargo;
            }
        train.Boiler.Tender = c.Tender;
        world.Run?.Resume(c.Seconds, c.Facility, c.Tender, c.Cars.Sum(x => x.Ammo));
        world.Holdouts?.Spend(c.SpentHoldouts ?? []);
    }

    /// <summary>The UDP port direct joiners use, or 0 when the host took none.</summary>
    public int Port => _udp?.Port ?? 0;

    /// <summary>On the ballast beside the engine, a few metres apart: where someone who was waiting at a stop is standing.</summary>
    static PlayerState Beside(TrainOnLine train, int n, PlayerTuning p)
    {
        var engine = train.Frames[0];
        double side = n % 2 == 0 ? 1 : -1;
        var world = engine.ToWorld(new Ballast.Double3(side * (engine.Shape.HalfWidth + 2.5), 0, engine.Shape.HalfLength - 4 - 2 * (n / 2)));
        return PlayerMotor.SpawnOnGround(world, train.Line, train.Dynamics.Distance - train.Dynamics.Tuning.Geometry.EngineLength / 2, p);
    }

    /// <summary>Connects to a host over UDP and waits (up to the transport's connect timeout) for its Welcome.</summary>
    /// <param name="whileWaiting">Called each time round the wait (a test steps its in-process host here).</param>
    public static NetPlaySession Join(string content, IPEndPoint address, Action? whileWaiting = null, DatagramOptions? options = null) =>
        Connect(content, UdpTransport.Connect(address, options), address.ToString(), null, whileWaiting, () => UdpTransport.Connect(address, options));

    /// <summary>Joins a friend's lobby (an invite, "Join Game", <c>+connect_lobby</c>) and connects to its owner.</summary>
    public static NetPlaySession JoinLobby(string content, IOnlineBackend online, LobbyId id, Action? whileWaiting = null, DatagramOptions? options = null)
    {
        var lobby = Lobby.Join(online, id, Game, Protocol.Version);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (lobby.Status == Lobby.State.Joining)
        {
            if (clock.Elapsed.TotalSeconds > (options ?? new DatagramOptions()).ConnectSeconds)
            {
                lobby.Dispose();
                throw new IOException("the lobby didn't answer");
            }
            lobby.Poll();
            whileWaiting?.Invoke();
            Thread.Sleep(5);
        }
        if (lobby.Status != Lobby.State.Open)
        {
            // Note 254: a full crew's lobby is shut; where the platform still shows us its values, say why as the host would.
            if (online.LobbyData(id, FullKey) == "1" && int.TryParse(online.LobbyData(id, AboardKey), System.Globalization.CultureInfo.InvariantCulture, out int aboard)
                && int.TryParse(online.LobbyData(id, MaxKey), System.Globalization.CultureInfo.InvariantCulture, out int max))
                throw new IOException(new Refusal(RefusalReason.CrewFull, aboard, max).ToString());
            throw new IOException($"couldn't join: {lobby.Error}");
        }
        try
        {
            return Connect(content, OnlineTransport.Connect(online, lobby.Owner, options), $"{online.NameOf(lobby.Owner)}'s game", lobby, whileWaiting,
                () => OnlineTransport.Connect(online, lobby.Owner, options));
        }
        catch
        {
            lobby.Dispose();
            throw;
        }
    }

    /// <param name="redial">A new link to the same host, for coming back after a drop (note 253).</param>
    static NetPlaySession Connect(string content, ITransport transport, string describe, Lobby? lobby, Action? whileWaiting, Func<ITransport>? redial = null)
    {
        var early = new List<TransportEvent>();
        string? session = null;
        var poll = new List<TransportEvent>();
        while (session is null)
        {
            poll.Clear();
            lobby?.Poll();
            transport.Poll(poll);
            foreach (var e in poll)
            {
                // The host welcomes nobody till they've said hello (note 253): who this is, a new joiner (no token).
                if (e.Kind == TransportEventKind.Connected)
                {
                    var hello = new NetWriter();
                    Messages.WriteHello(hello, LocalName(lobby));
                    transport.Send(PeerId.Host, hello.Written, Delivery.ReliableOrdered);
                }
                if (e.Kind == TransportEventKind.Disconnected)
                {
                    transport.Dispose();
                    throw new IOException($"no answer from {describe}");
                }
                // Note 254: turned away (a full crew). Said on the join screen, "CREW FULL (8/8)", rather than waiting on.
                if (e is { Kind: TransportEventKind.Data, Payload: { Length: > 0 } no } && no[0] == (byte)MessageType.Refused)
                {
                    var r = new NetReader(no);
                    r.U8();
                    var refusal = Messages.ReadRefused(ref r);
                    transport.Dispose();
                    throw new IOException(refusal.ToString());
                }
                if (e is { Kind: TransportEventKind.Data, Payload: { Length: > 0 } p } && p[0] == (byte)MessageType.Welcome)
                {
                    var r = new NetReader(p);
                    r.U8();
                    session = Messages.ReadWelcome(ref r).Session;
                }
            }
            early.AddRange(poll);
            whileWaiting?.Invoke();
            Thread.Sleep(5);
        }
        var setup = SessionSetup.Decode(session);
        if (setup.ContentDifferences(content) is { Count: > 0 } differ)
        {
            transport.Dispose();
            throw new IOException(SessionSetup.Refusal(differ, setup.Mods, ContentMods.MountedIn(content)));
        }
        var (world, route) = setup.Build(content);
        if (setup.PlanPrint is { } print && route?.Plan?.Fingerprint() != print)
        {
            transport.Dispose();
            throw new IOException("the host's night is on a line this build of the game doesn't generate (it was saved by another version): update to the host's version to join");
        }
        if (setup.TerrainPrint is { } ground && TerrainOf(world)?.Print() != ground)
        {
            transport.Dispose();
            throw new IOException("the host's land comes out differently on this machine (its terrain checksum differs): report it, it's a bug");
        }
        var client = new ClientSession(new Replay(transport, early), world,
            setup.Loadout(content).Train, DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File)))
        { Name = LocalName(lobby) };
        return new NetPlaySession(null, null, null, client, transport, setup, route, lobby) { Redial = redial };
    }

    /// <summary>A new link to the host this joiner reached (by address, or through the lobby), for coming back after a drop.</summary>
    Func<ITransport>? Redial { get; init; }

    /// <summary>
    /// Note 253: the tries at getting back to the host since the link went, this one included (0 while connected), out of
    /// player.json rejoin.retries; then it's <see cref="CanReconnect"/>'s, by hand.
    /// </summary>
    public int Attempt { get; private set; }
    public int Attempts => Client.PlayerTuning.Rejoin.Retries;
    bool _dialing;
    double _retryIn;

    /// <summary>The link's gone, and this joiner is trying to get back (or about to try again).</summary>
    public bool Reconnecting => Lost && Redialable && (_dialing || Attempt < Attempts);
    /// <summary>The tries ran out: RECONNECT (the F5 key) starts them again.</summary>
    public bool CanReconnect => Lost && Redialable && !_dialing && Attempt >= Attempts;
    /// <summary>A joiner, with a host to go back to: not hosting, and the host hasn't left its lobby (spec E: that ends it).</summary>
    bool Redialable => Redial is not null && Lobby is not { HostLeft: true };

    /// <summary>RECONNECT: another round of tries, the first at once.</summary>
    public void Reconnect()
    {
        if (!CanReconnect)
            return;
        Attempt = 0;
        _retryIn = 0;
    }

    /// <summary>
    /// Note 253: lost, a joiner dials the host again (a new link, the same way it came), and asks for its slot back with its
    /// token; up to rejoin.retries times, rejoin.retrySeconds apart. Each try lasts until it's back aboard or the link fails.
    /// </summary>
    void StepRedial()
    {
        if (!Redialable)
            return;
        if (_dialing)
        {
            if (Client.Connected)
            {
                _dialing = false;
                Lost = false;
                Attempt = 0;
                return;
            }
            // Note 254: turned away (the place ran out and the crew's full again). No use trying again at once: it's said,
            // and F5 tries when the player likes.
            if (Client.Refused is not null)
            {
                _dialing = false;
                Attempt = Attempts;
                return;
            }
            // That try's link failed (nobody answered, or it went again): the next one in a while.
            if (Client.Dropped)
            {
                _dialing = false;
                _retryIn = Client.PlayerTuning.Rejoin.RetrySeconds;
            }
            return;
        }
        if (Attempt >= Attempts)
            return;
        _retryIn -= SimConstants.TickSeconds;
        if (_retryIn > 0)
            return;
        Attempt++;
        _dialing = true;
        _clientTransport.Dispose();
        _clientTransport = Redial!();
        _link = (IConnectionInfo)_clientTransport;
        Client.Reconnect(_clientTransport);
    }

    /// <summary>
    /// Spec E "autosave per POI, on successful departure": the host takes one each time the train pulls away from a
    /// facility, and counts them so the app knows when to write the save.
    /// </summary>
    public Sim.Campaign.RunCheckpoint? Checkpoint { get; private set; }
    public int Checkpoints { get; private set; }
    int _departures;

    public void Step(in PlayerIntent intent)
    {
        Lobby?.Poll();
        Host?.Step();
        // Hosting a public game: it says where it is on the local network (T116), so the join screen lists it, and answers
        // the browsers' pings. A private one stays quiet (the user's playtest: "If it's a private lobby its not listed").
        // What it says of the crew is the places taken against the cap (note 254), bots and held places too: what decides
        // whether the next joiner gets in. (Not the HUD's head count, which counts a Passenger: that would give it away.)
        if (Host is { } hosting && Listed && _udp is { LocalLoopbackOnly: false } udp)
        {
            _beacon ??= new LanBeacon();
            _beacon.Tick(_clock.Elapsed.TotalSeconds, new LanAdvert(Game, Protocol.Version, udp.Port, HostName, Describe(Setup, World.Run is { Phase: Sim.Run.RunPhase.Yard }), hosting.Occupied)
            {
                Name = LobbyName,
                Max = hosting.Cap,
                Tier = Tier,
                Lobby = Lobby is { IsHost: true, Status: Lobby.State.Open, Visibility: LobbyVisibility.Public } l ? l.Id.ToString() : "",
            });
        }
        if (Host is { } served && Lobby is { } meeting)
            Advertise(meeting, served.Occupied, served.Cap);
        BotCrew?.Step();
        // Spec E: the night autosaves on leaving a POI: the engine out past the end of its zone, whatever shunting it
        // took there (GDD §17), so the save is the train going on.
        if (Host?.World.Run is { } run && run.Departures != _departures)
        {
            _departures = run.Departures;
            if (Setup.Route is { } spec)
            {
                Checkpoint = Capture(Host.World, spec, run.Departed);
                Checkpoints++;
            }
        }
        _previous = Client.Predicted;
        // Hosting, this machine's own player is the host whose vote alone skips the film (E.5).
        if (Host is { HostPlayer: < 0 } host && Client.PlayerId is { } me)
            host.HostPlayer = me;
        // D.11 (note 202): a cast vote goes as the option's number, the hotbar choice, till the host's ballot says it's locked.
        var sent = intent;
        if (Picker.Select(Ballot) is > 0 and var vote && !Player.Alive)
            sent.Select = vote;
        Client.Step(Spectate(sent));
        WreckSeconds = Train.Wreck is null ? 0 : WreckSeconds + SimConstants.TickSeconds;
        OutroSeconds = World.Run?.End == Sim.Run.RunEnd.Stranded ? OutroSeconds + SimConstants.TickSeconds : 0;
        StepRadio();
        StepCues();
        if (World.Film is { } start && _shooting is null)
        {
            var world = World;
            _shooting = Task.Run(() => (WreckFilm?)world.ShootFilm());
        }
        // E.5: voted off, the film cuts to the cause card (never past it); E.9: the outro to its end.
        if (World.FilmSkipped && Film is { } film && DerailSequence.Beat(SequenceTuning, WreckSeconds, film) == DerailBeat.Film
            && DerailSequence.FilmSeconds(SequenceTuning, WreckSeconds) < film.CauseAt)
            WreckSeconds = SequenceTuning.FirstPersonSeconds + SequenceTuning.ReplaySeconds + film.CauseAt;
        if (World.FilmSkipped && StrandedOutro)
            OutroSeconds = World.WreckTuning.Stranded.Seconds;
        Tick++;
        if (Client.Dropped || (!_link.IsConnected && Client.Connected))
            Lost = true;
        if (Lost)
            StepRedial();
    }

    public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha)
    {
        Train.FramesAt(alpha, _frames);
        return _frames;
    }

    public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) =>
        Watching >= 0 && Client.TryGetRemote((byte)Watching, alpha, out var s)
            ? Eyes.Operator(s, World) ?? Eyes.From(s, s, frames, alpha, 0, 0)
            : Eyes.Operator(Player, World) ?? Eyes.From(Player, _previous, frames, alpha, pendingYaw, pendingPitch);

    /// <summary>
    /// Dead, or waiting to board, you watch the living crew through their eyes (GDD App. D.10): whoever's first when you
    /// die, and on to the next when they die. Fire or a step right goes to the next, a step left back; living crew only, no
    /// free camera. The host's told whom (<see cref="PlayerIntent.Watch"/>), so it sends what's around them.
    /// </summary>
    PlayerIntent Spectate(PlayerIntent intent)
    {
        var last = _lastIntent;
        _lastIntent = intent;
        if (Player.Alive || World.Run is { Over: true })
        {
            _watching = -1;
            return intent;
        }
        var living = new List<int>();
        foreach (byte id in Client.RemoteIds)
            if (Client.TryGetRemote(id, 1, out var s) && s.Alive)
                living.Add(id);
        living.Sort();
        int step = (intent.Has(PlayerButtons.Fire) && !last.Has(PlayerButtons.Fire)) || (intent.MoveX > 0.5f && last.MoveX <= 0.5f) ? 1
            : intent.MoveX < -0.5f && last.MoveX >= -0.5f ? -1 : 0;
        int at = living.IndexOf(_watching);
        _watching = living.Count == 0 ? -1
            : at < 0 ? living[0]
            : living[((at + step) % living.Count + living.Count) % living.Count];
        intent.Watch = (byte)Math.Max(0, _watching);
        return intent;
    }

    int _watching = -1;
    PlayerIntent _lastIntent;

    public int Watching => _watching;

    public PlayerState Viewpoint => Watching >= 0 && Client.TryGetRemote((byte)Watching, 1, out var s) ? s : Player;

    readonly List<PlayerState> _states = [];
    readonly List<(int, PlayerState)> _crewStates = [];

    public IReadOnlyList<(int Id, PlayerState State)> CrewStates(double alpha)
    {
        _crewStates.Clear();
        _crewStates.Add((PlayerId, Player));
        foreach (byte id in Client.RemoteIds)
            if (Client.TryGetRemote(id, alpha, out var s))
                _crewStates.Add((id, s));
        return _crewStates;
    }

    public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha)
    {
        _crew.Clear();
        _states.Clear();
        _states.Add(Player);
        foreach (byte id in Client.RemoteIds)
            if (Client.TryGetRemote(id, alpha, out var s))
                _states.Add(s);
        foreach (byte id in Client.RemoteIds)
            if (Client.TryGetRemote(id, alpha, out var s))
                _crew.Add(Art.CrewActs.Crewmate(id, s, World, frames, _states));
        return _crew;
    }

    public string Status()
    {
        var d = Train.Dynamics;
        var p = Player;
        string link = Reconnecting ? $"RECONNECTING ({Attempt}/{Attempts})…" : CanReconnect ? "CONNECTION LOST: F5 to reconnect"
            : Client.Waiting ? $"WAITING: {Client.WaitingReason}" : Lost ? "CONNECTION LOST" : !Client.Connected ? "connecting…"
            : Host is not null ? $"{Aboard} aboard" // the host's own ping is to itself
            : $"{Aboard} aboard, ping {_link.RoundTrip(PeerId.Host) * 1000:0} ms";
        string where = PrototypeSession.Where(p, Train);
        string state = p.Alive ? $"{p.Surface} {where} hp {p.Health}{PrototypeSession.Condition(p, Client.PlayerTuning)}" : $"DEAD ({p.Death})";
        return $"{d.Speed,5:0.0} m/s | thr {Controls.Throttle:0.00} brk {Controls.Brake:0} | P {Train.Boiler.Pressure,3:0} fire {Train.Boiler.Firebox:0.0} tender {Train.Boiler.Tender:0} | " +
               $"noise {World.Choir.Loudness:0.0}{(World.Choir.Present ? " CHOIR HERE" : World.Choir.Build > 0 ? $" choir {World.Choir.Build:P0}" : "")} | {d.Distance / 1000:0.00}/{Train.Line.Length / 1000:0.0} km | {state} | {Role()} | {link}" +
               PrototypeSession.RouteStatus(Route, World, Train);
    }

    string Role()
    {
        string udp = Port > 0 ? $":{Port}" : "";
        // Hosting, the places taken of the crew cap (note 254).
        string crew = Host is { } h ? $", crew {h.Occupied}/{h.Cap}{(h.Full ? " FULL" : "")}" : "";
        if (Lobby is not { } lobby)
            return Host is not null ? $"hosting {udp}{crew}" : "joined";
        string platform = lobby.Online.Platform;
        return lobby.Status switch
        {
            Lobby.State.Creating => $"hosting {udp} · {platform} lobby…",
            Lobby.State.Failed => $"hosting {udp} · no {platform} lobby ({lobby.Error})",
            _ when Host is not null => $"hosting {udp} · {platform} lobby{crew}{(Host.Full ? "" : ", F2 invites")}",
            _ when lobby.HostLeft => $"{platform}: the host left",
            _ => $"joined {lobby.Online.NameOf(lobby.Owner)} on {platform}",
        };
    }

    public IReadOnlyList<RosterLine> Roster()
    {
        var remotes = new List<(byte, PlayerState)>();
        foreach (byte id in Client.RemoteIds)
            if (Client.TryGetRemote(id, 1, out var s))
                remotes.Add((id, s));
        return RosterOf((byte)(Client.PlayerId ?? 0), Player, remotes, World);
    }

    /// <summary>
    /// The roster's lines: you and the rest of the session's crew, by name, in player-id order. GDD v1.4 (open question 2)
    /// made roll call verbal: "there is no aboard indicator for the conductor or anyone else", so a line says nothing of
    /// where anyone is or whether they're alive, and the Passenger isn't on it: its only tell is silence, heard by ear.
    /// </summary>
    public static IReadOnlyList<RosterLine> RosterOf(byte me, in PlayerState mine, IEnumerable<(byte Id, PlayerState State)> crew, World world)
    {
        var lines = new List<RosterLine> { new(me, NameOr(world, me, "YOU").ToUpperInvariant(), "", true, You: true) };
        foreach (var (id, _) in crew)
            lines.Add(new RosterLine(id, NameOr(world, id, $"CREW {id}").ToUpperInvariant(), "", true));
        return [.. lines.OrderBy(l => l.Id)];
    }

    static string NameOr(World world, int id, string fallback) => world.Names.TryGetValue(id, out var n) && n.Length > 0 ? n : fallback;

    /// <summary>
    /// Everyone aboard, by the figures: the crew, and anything wearing one of their faces (App. A.7's tell, "crew count reads
    /// one too many").
    /// </summary>
    int Aboard => Client.RemoteIds.Count() + 1 + World.ActiveEnemies.Count(e => e is Sim.Enemies.Passenger);

    public PlayerTuning PlayerTuning => Client.PlayerTuning;
    public int PlayerId => Client.PlayerId ?? 0;
    public LinkInfo? Link => new(Role(), Host is null && Client.Connected ? _link.RoundTrip(PeerId.Host) * 1000 : null,
        Aboard, Client.Waiting ? Client.WaitingReason : null, Lost, JoinAt, Listed && Host is not null)
    {
        Attempt = Reconnecting ? Math.Max(1, Attempt) : 0,
        Attempts = Attempts,
        CanReconnect = CanReconnect,
        Cap = Host?.Cap ?? Client.PlayerTuning.Crew.Places,
        Places = Host?.Occupied ?? 0,
        Refused = Lost ? Client.Refused?.ToString() : null,
    };

    /// <summary>This machine's address on the local network and the port, for friends to type in; null unless hosting for them.</summary>
    string? JoinAt => _joinAt ??= Host is not null && _udp is { Port: > 0, LocalLoopbackOnly: false } u ? LanAddress() is { } ip ? $"{ip}:{u.Port}" : null : null;
    string? _joinAt;

    /// <summary>The first IPv4 address of an interface that's up and has a gateway (the wifi or the cable), not a loopback or a VPN's.</summary>
    static string? LanAddress()
    {
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                    || nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                    continue;
                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0)
                    continue;
                foreach (var a in props.UnicastAddresses)
                    if (a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                        return a.Address.ToString();
            }
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
        }
        return null;
    }

    /// <summary>Accepts a friend's invite that arrived while playing, if any, leaving it for the app to act on.</summary>
    public LobbyId? TakeJoinRequest() => Lobby?.TakeJoinRequest();

    public void ShowInviteDialog() => Lobby?.ShowInviteDialog();

    LanBeacon? _beacon;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Whose game it is, for the join list: the Steam name hosting through Steam, else the computer's user.</summary>
    string HostName => Lobby is { } l ? l.Online.NameOf(l.Online.Me) : Environment.UserName;

    /// <summary>The name set in the settings (the app sets it at start), for the crew and the report.</summary>
    public static string PlayerName { get; set; } = "";

    /// <summary>What this player is called: the settings' name, else the online name, else the system's.</summary>
    static string LocalName(Lobby? lobby) =>
        Messages.CleanName(PlayerName) is { Length: > 0 } set ? set
        : Messages.CleanName(lobby is { } l ? l.Online.NameOf(l.Online.Me) : Environment.UserName) is { Length: > 0 } n ? n : "You";

    public void Dispose()
    {
        _beacon?.Dispose();
        BotCrew?.Dispose();
        // A joiner quitting on purpose gives its place up (note 254), so a full crew has room for someone else at once.
        if (Host is null && !Lost)
            Client.Leave();
        _clientTransport.Dispose();
        _hostTransport?.Dispose();
        Lobby?.Dispose();
    }

    /// <summary>Hands the events read while waiting for the Welcome to the session, then gets out of the way.</summary>
    sealed class Replay(ITransport inner, List<TransportEvent> early) : ITransport
    {
        public PeerId LocalId => inner.LocalId;
        public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery) => inner.Send(to, payload, delivery);
        public void Disconnect(PeerId peer) => inner.Disconnect(peer);
        public void Dispose() => inner.Dispose();

        public void Poll(List<TransportEvent> into)
        {
            into.AddRange(early);
            early.Clear();
            inner.Poll(into);
        }
    }
}
