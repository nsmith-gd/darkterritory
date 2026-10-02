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
    /// <summary>Where the engine's front starts, along the line; null for the fortress yard. A resumed night starts where it was saved.</summary>
    public double? Start { get; init; }
    /// <summary>The host's only: a resumed night's own line, from its save (linegen plan §17.4), rather than generated afresh.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Sim.LineGen.LinePlan? Plan { get; init; }
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
        return hashes;
    }

    /// <summary>Files that differ between the host's content and this machine's.</summary>
    public IReadOnlyList<string> ContentDifferences(string content)
    {
        if (Content is null)
            return [];
        var mine = HashContent(content);
        return [.. Content.Keys.Union(mine.Keys).Where(k => Content.GetValueOrDefault(k) != mine.GetValueOrDefault(k)).Order()
            .Select(k => k == LineGenKey ? "linegen/*.json" : $"tuning/{k}.json")];
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
        var consist = Consist.Uniform(trainTuning, Cars, Route is { Length: > 0 } ? runTuning.DepartureLoad : 1);
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
    /// <summary>Lobby size: GDD §3, "2–8+" players.</summary>
    public const int MaxCrew = 12;
    public const string Game = "darkterritory";
    /// <summary>A hosted game's values in its platform lobby, for the browser (the engine's own are on <see cref="Lobby"/>).</summary>
    public const string NameKey = "name", TierKey = "tier", RunKey = "run", AboardKey = "aboard";
    readonly ITransport? _hostTransport;
    readonly UdpTransport? _udp;
    readonly ITransport _clientTransport;
    readonly IConnectionInfo _link;
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
    public bool WreckCinematic => Train.Wreck is not null && WreckSeconds < World.WreckTuning.SequenceSeconds;
    public double OutroSeconds { get; private set; }
    public bool StrandedOutro => World.Run?.End == Sim.Run.RunEnd.Stranded && OutroSeconds < World.WreckTuning.Stranded.Seconds;
    public World World => Client.World;
    public PlayerState Player => Client.Predicted;
    public TrainControls Controls => Client.Controls;
    public long Tick { get; private set; }
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
        setup = setup with { PlanPrint = route?.Plan?.Fingerprint(), TerrainPrint = TerrainOf(hostWorld)?.Print() };
        if (resume is not null)
            Restore(hostWorld, resume);
        var udp = port is { } p ? UdpTransport.Host(p) : UdpTransport.Host(0, bind: IPAddress.Loopback);
        ITransport hostTransport = online is null ? udp : new HostGroup(udp, OnlineTransport.Host(online));
        string tier = setup.Route is { } spec ? Sim.Route.Route.ParseSpec(spec).Tier.ToString() : "";
        string name = lobbyName is { Length: > 0 } n ? n : $"{(Messages.CleanName(PlayerName) is { Length: > 0 } set ? set : online?.NameOf(online.Me) ?? LocalName(null))}'s run";
        var lobby = online is null ? null : Lobby.Host(online, Game, Protocol.Version, MaxCrew, listed ? LobbyVisibility.Public : LobbyVisibility.FriendsOnly,
            new Dictionary<string, string> { [NameKey] = name, [TierKey] = tier, [RunKey] = Describe(setup, inYard: true), [AboardKey] = "1" });
        var host = new HostSession(hostTransport, hostWorld, trainTuning, playerTuning) { SessionInfo = setup.Encode() };
        hostWorld.EnableBodies();
        hostWorld.Stock();
        // Spec E: drop-in at POIs only: in the yard, stopped at a facility, or home. Mid-run joiners wait by
        // the train at the facility, "like a pickup".
        if (hostWorld.Run is { } run)
        {
            host.CanBoard = () => run.Phase is Sim.Run.RunPhase.Yard or Sim.Run.RunPhase.AtFacility or Sim.Run.RunPhase.Arrived;
            host.BoardAt = n => run.Phase == Sim.Run.RunPhase.Yard ? PlayerMotor.SpawnOnRoof(hostWorld.Train, 1 + (n - 1) % Math.Max(1, hostWorld.Train.Frames.Count - 1), 0, playerTuning)
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
    int _advertisedAboard = 1;

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
        Connect(content, UdpTransport.Connect(address, options), address.ToString(), null, whileWaiting);

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
            throw new IOException($"couldn't join: {lobby.Error}");
        try
        {
            return Connect(content, OnlineTransport.Connect(online, lobby.Owner, options), $"{online.NameOf(lobby.Owner)}'s game", lobby, whileWaiting);
        }
        catch
        {
            lobby.Dispose();
            throw;
        }
    }

    static NetPlaySession Connect(string content, ITransport transport, string describe, Lobby? lobby, Action? whileWaiting)
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
                if (e.Kind == TransportEventKind.Disconnected)
                {
                    transport.Dispose();
                    throw new IOException($"no answer from {describe}");
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
        return new NetPlaySession(null, null, null, client, transport, setup, route, lobby);
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
        int humans = Aboard - (BotCrew?.Bots.Count ?? 0);
        // Hosting a public game: it says where it is on the local network (T116), so the join screen lists it, and answers
        // the browsers' pings. A private one stays quiet (the user's playtest: "If it's a private lobby its not listed").
        if (Host is not null && Listed && _udp is { LocalLoopbackOnly: false } udp)
        {
            _beacon ??= new LanBeacon();
            _beacon.Tick(_clock.Elapsed.TotalSeconds, new LanAdvert(Game, Protocol.Version, udp.Port, HostName, Describe(Setup, World.Run is { Phase: Sim.Run.RunPhase.Yard }), humans)
            {
                Name = LobbyName,
                Max = MaxCrew,
                Tier = Tier,
                Lobby = Lobby is { IsHost: true, Status: Lobby.State.Open, Visibility: LobbyVisibility.Public } l ? l.Id.ToString() : "",
            });
        }
        if (Host is not null && humans != _advertisedAboard)
        {
            _advertisedAboard = humans;
            Lobby?.SetData(AboardKey, humans.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
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
        Client.Step(Spectate(intent));
        WreckSeconds = Train.Wreck is null ? 0 : WreckSeconds + SimConstants.TickSeconds;
        OutroSeconds = World.Run?.End == Sim.Run.RunEnd.Stranded ? OutroSeconds + SimConstants.TickSeconds : 0;
        Tick++;
        if (!_link.IsConnected && Client.Connected)
            Lost = true;
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
        string link = Client.Waiting ? $"WAITING: {Client.WaitingReason}" : !Client.Connected ? "connecting…" : Lost ? "CONNECTION LOST"
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
        if (Lobby is not { } lobby)
            return Host is not null ? $"hosting {udp}" : "joined";
        string platform = lobby.Online.Platform;
        return lobby.Status switch
        {
            Lobby.State.Creating => $"hosting {udp} · {platform} lobby…",
            Lobby.State.Failed => $"hosting {udp} · no {platform} lobby ({lobby.Error})",
            _ when Host is not null => $"hosting {udp} · {platform} lobby {lobby.Members.Count}/{MaxCrew}, F2 invites",
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
        Aboard, Client.Waiting ? Client.WaitingReason : null, Lost, JoinAt, Listed && Host is not null);

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
