using System.Numerics;
using Ballast.Online;
using Ballast.Render;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Music;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>What the front end asks the game to start.</summary>
public abstract record Launch
{
    /// <summary>As the console's log may say it: without a password (note 450).</summary>
    public virtual Launch Redacted() => this;

    /// <summary>
    /// A night on a generated route (null: a hand-made <see cref="Line"/>, or a <see cref="RouteFile"/> saved from the
    /// editor), alone or hosted for friends (UDP, and a Steam lobby if Steam's up).
    /// </summary>
    public sealed record Night(string? Route, int Cars, bool Host) : Launch
    {
        public string Line { get; init; } = "test-loop";
        /// <summary>Bot crewmates to take along (T89): the first drives, the rest crew the train with you.</summary>
        public int Bots { get; init; }
        public string? RouteFile { get; init; }
        /// <summary>Hosting: listed for anyone to find, or private (invite and address only).</summary>
        public bool Public { get; init; } = true;
        /// <summary>Hosting: what the lobby browser calls it (null: the host's name's run).</summary>
        public string? LobbyName { get; init; }
        /// <summary>Hosting a private run: its password (note 450). Null: an open run.</summary>
        public string? Password { get; init; }
        /// <summary>Hosting: who the run is for (note 450), said in its listing.</summary>
        public RunMood Mood { get; init; }
        /// <summary>Hosting: the CREW MISCHIEF switch as the host set it (note 724); null, where the mood and visibility have it.</summary>
        public bool? Mischief { get; init; }
        public override Launch Redacted() => Password is null ? this : this with { Password = "***" };
    }
    /// <summary>Someone else's night, at an address (host[:port]).</summary>
    public sealed record Join(string Address) : Launch
    {
        /// <summary>A private run's password (note 450), as typed; null for none.</summary>
        public string? Password { get; init; }
        public override Launch Redacted() => Password is null ? this : this with { Password = "***" };
    }
    /// <summary>A friend's Steam lobby (an invite accepted, or "Join Game").</summary>
    public sealed record JoinLobby(LobbyId Lobby) : Launch
    {
        /// <summary>A private run's password (note 450), as typed; null for none (a friend of the host's needs none).</summary>
        public string? Password { get; init; }
        public override Launch Redacted() => Password is null ? this : this with { Password = "***" };
    }
    /// <summary>
    /// A campaign night (spec E, F): the slot, and the contract off its board; or, with <paramref name="Resume"/>, the
    /// night already under way, from its last autosave.
    /// </summary>
    public sealed record CampaignNight(int Slot, int Contract, bool Resume, bool Host) : Launch
    {
        /// <summary>The same night again from its yard, the train as the blacksmith's just fitted it (note 697).</summary>
        public bool Refit { get; init; }
        /// <summary>
        /// The crew's bots (note 611; T89): the fortress board's CREW, or <c>--bots</c>. The host going on to the next night
        /// from the town (note 606) keeps them: the board's next contract carries the same.
        /// </summary>
        public int Bots { get; init; }
        /// <summary>
        /// A crew ridden with (note 724; GDD §11): its id among the crews kept on this machine, and <paramref name="Slot"/> 0.
        /// Null: the slot's own crew.
        /// </summary>
        public string? Crew { get; init; }
    }
    /// <summary>
    /// The crew goes on together (note 606): a joiner whose host left a delivered campaign night to go on to the next, waiting
    /// on the town's card and dialling the same host until that night's up.
    /// </summary>
    public sealed record Follow(NetPlaySession.Following Going) : Launch;
    public sealed record Quit : Launch;
    /// <summary>The in-night menu's INVITE (note 292): the platform's invite dialog, over the night.</summary>
    public sealed record Invite : Launch;
    /// <summary>The in-night menu's CREW MISCHIEF (note 724), the host's in the yard: the switch set so, for every machine. The menu stays up.</summary>
    public sealed record SetMischief(bool On) : Launch;
    /// <summary>The in-night menu's LEAVE, confirmed (note 292): out of the night and back to the menus.</summary>
    public sealed record Leave : Launch;
    /// <summary>A folder shown in the system's file browser (note 411: where the crash reports are). The menu stays up.</summary>
    public sealed record OpenFolder(string Path) : Launch;
    /// <summary>
    /// The demo's WISHLIST ON STEAM (note 434): the full game's store page, over the game in the Steam overlay or in the
    /// browser at <paramref name="Url"/>. The menu stays up.
    /// </summary>
    public sealed record Wishlist(uint App, string Url) : Launch;
    /// <summary>
    /// A developer tool's own night (a developer build's, note 514), by name: the creature gym (GDD §34.1, note 676). Only a
    /// developer build offers one (<see cref="FrontEnd.DevEntries"/>), and only its hooks start it.
    /// </summary>
    public sealed record DevTool(string Name) : Launch;
    /// <summary>
    /// A report to the studio (note 452): the player's mail program on <paramref name="MailTo"/> (a mailto: link with the
    /// subject and a summary filled in), and <paramref name="Folder"/> in the file browser beside it, to attach the report.
    /// The menu stays up.
    /// </summary>
    public sealed record Mail(string MailTo, string Folder) : Launch;
}

/// <summary>
/// The night the in-night menu was opened over (note 292): what LEAVE's confirmation has to say about leaving it, and
/// whether there's anyone to invite. The app reads it off the session as Escape opens the menu.
/// </summary>
/// <param name="Hosting">This machine runs the night (solo nights too): leaving it ends it for everyone on it.</param>
/// <param name="Others">The other people aboard, bots not counted: who a host's leaving ends the night for.</param>
/// <param name="Campaign">A campaign night: its slot carries on from the last stop the train left.</param>
/// <param name="Invites">A platform lobby with room, that takes invites (Steam's overlay).</param>
/// <param name="JoinAt">Hosting on the network: the address friends type on JOIN.</param>
/// <param name="Over">The night's over (the report's up): leaving costs nothing, so it isn't asked twice.</param>
/// <param name="Yard">In the yard before the gate (GDD §9), where outfits are tried on (note 298): OUTFIT is on the menu.</param>
/// <param name="Lobby">Hosting a listed run (note 535): who it's for and whether it's locked, which the host changes from here.
/// Null: a joiner, or a run nobody finds on a list.</param>
public sealed record NightMenu(bool Hosting = true, int Others = 0, bool Campaign = false, bool Invites = false, string? JoinAt = null, bool Over = false,
    bool Yard = false, HostLobby? Lobby = null)
{
    /// <summary>The host in the yard (note 724): the CREW MISCHIEF switch as it stands, to change; null for anyone else, or past the gate.</summary>
    public bool? Mischief { get; init; }
}

/// <summary>A listed run's lobby as its host has it now (note 535): its mood, and whether it's behind a password.</summary>
public sealed record HostLobby(RunMood Mood, bool Locked);

public enum Screen { Title, Slots, Fortress, Upgrades, QuickNight, Join, Settings, Controls, Host, Stores, Credits, Night, Leave, Profile, DeleteCrew, Mods, Crashed, Password, Paint, Smithy, Consent, PlaytestData }

/// <summary>
/// The blacksmith's catalogue, open over the yard (ARCHITECTURE §8 note 697): who's selling, the crew's campaign as it stands
/// (its scrip, its cars, what's fitted), and whether this player pays (the host owns the campaign and its wallet, spec E;
/// otherwise <paramref name="Payer"/> is who to ask).
/// </summary>
public sealed record SmithyShop(string Smith, CampaignState State, bool CanBuy, string? Payer = null);

/// <summary>A mod laid over the game (note 323), as the MODS screen lists it: the app's scan of what's installed.</summary>
public sealed record InstalledMod(string Name, string Version, string? Description);

/// <param name="Detail">A line about the selected item, under the list.</param>
/// <summary>A row of a menu. A <paramref name="Heading"/> (note 386) names the rows under it: never selected, never clicked.</summary>
public sealed record MenuItem(string Label, string? Detail = null, bool Enabled = true, bool Heading = false);

/// <summary>
/// The front end's text fields (note 264, the director's notes on build 1121: "when I go to name of lobby, it just starts
/// automatically typing"). None takes typing on being chosen: Enter or a click starts editing, Enter or Esc ends it.
/// </summary>
public enum TextField { LobbyName, Address, PlayerName, CrewName, LobbyPassword, JoinPassword, NightPassword, EngineName }

/// <summary>Where a pointer is over the front end (note 264): an item, and on a value's row, its arrows (−1, +1) or the row (0).</summary>
public readonly record struct MenuHit(int Item, int Step = 0);

/// <summary>
/// The game's front door and the fortress between nights (spec E, F; roadmap M6 "settings"): the title, the three
/// campaign slots, the fortress's board of contracts with cars and upgrades to buy, a quick night on any tier, joining
/// by address, and the settings. It's a plain state machine the app feeds keys to and draws with the HUD's overlay,
/// so tests drive it, and `dt screenshot --menu` shows it, without a window.
/// </summary>
public sealed class FrontEnd
{
    readonly CampaignTuning _campaign;
    readonly RunTuning _run;
    readonly SaveSlots _saves;
    readonly string _settingsPath;
    readonly Func<ulong> _newSeed;
    readonly EditionTuning _edition;
    readonly RouteTier[] _tiers;
    int _tier;
    ulong _seed = 7;
    int _cars = 6;
    /// <summary>
    /// Bot crewmates for a quick night (T89). None by default (T110 playtest: with a human playing, the bots get in the way
    /// more than they help); a crew is a press away.
    /// </summary>
    int _bots = 0;
    public const int MaxBots = 7;

    /// <summary>
    /// The crew's bots, as the CREW choice has them (note 611): a campaign night launched with some (<c>--campaign n --bots n</c>)
    /// sets it, so the board's next contract, after a delivered night, takes the same crew on.
    /// </summary>
    public int Bots
    {
        get => _bots;
        set => _bots = Math.Clamp(value, 0, MaxBots);
    }
    bool _host;
    /// <summary>The host screen's CREW MISCHIEF (note 724) as the host set it; null, where the run's mood and visibility have it.</summary>
    bool? _mischief;

    /// <summary>The CREW MISCHIEF switch as the host set it for the run they open (note 724); null: as its mood and visibility have it.</summary>
    public bool? Mischief => _mischief;

    /// <summary>Horseplay's tuning (player.json, note 724): where the CREW MISCHIEF switch starts by mood. The app sets the content's.</summary>
    public Sim.Player.HorseplayTuning Horseplay { get; set; } = new();

    /// <summary>The switch as the host screen shows it: the host's choice, or the run's mood and visibility's.</summary>
    bool MischiefShown => _mischief ?? Moods.MischiefByDefault(Settings.Mood, !Settings.PublicLobby, Horseplay);

    /// <summary>The host screen's and the fortress board's CREW MISCHIEF row (note 724).</summary>
    Entry MischiefEntry() =>
        new(new($"CREW MISCHIEF: {(MischiefShown ? "ON" : "OFF")}", MischiefShown
                ? "Shoves, bonks and coal at the crew land. Nobody's hurt by them, but a shove off a roof at speed is a fall."
                : "Shoves, bonks and coal at the crew land on nobody. You can change it in the yard."),
            () => { _mischief = !MischiefShown; return null; }, _ => _mischief = !MischiefShown);

    /// <param name="newSeed">Where a new campaign's seed comes from (tests pin it).</param>
    /// <param name="edition">Which game this is (T79): the demo has a quick night on its own tiers, and no campaign.</param>
    public FrontEnd(CampaignTuning campaign, RunTuning run, SaveSlots saves, string settingsPath, Func<ulong>? newSeed = null, EditionTuning? edition = null)
    {
        _edition = edition ?? new();
        _tiers = [.. Enum.GetValues<RouteTier>().Where(_edition.HasTier)];
        if (_tiers.Length == 0)
            _tiers = Enum.GetValues<RouteTier>();
        // The Frontier to start with, where there is one: the first tier past the local lines.
        _tier = Math.Max(0, Array.IndexOf(_tiers, RouteTier.Frontier));
        _campaign = campaign;
        _run = run;
        _saves = saves;
        _settingsPath = settingsPath;
        _newSeed = newSeed ?? (() => (ulong)Random.Shared.Next(1, 1_000_000));
        Settings = Settings.Load(settingsPath);
    }

    public Screen Screen { get; private set; }
    public int Selected { get; private set; }
    /// <summary>What just happened (a purchase, a refusal, a night's result), until the next move.</summary>
    public string? Message { get; private set; }
    /// <summary>The campaign open at the fortress.</summary>
    public CampaignState? Open { get; private set; }
    /// <summary>The blacksmith's catalogue open over the yard (note 697), or null.</summary>
    public SmithyShop? Smithy { get; private set; }
    /// <summary>
    /// The host's purchase at the forge (note 697): the app buys it on its own campaign, saves it, and fits it before the gate.
    /// </summary>
    public Func<string, Purchase>? SmithyBuy { get; set; }

    /// <summary>
    /// The blacksmith's shelves with no prices and no stock rules (a developer build's unlock, note 747, sets it): every upgrade
    /// is FREE, and one owned can be taken off again.
    /// </summary>
    public bool SmithyFree { get; set; }

    /// <summary>A row of its own at the top of the blacksmith's catalogue, and what choosing it does (a developer build's, note 747).</summary>
    public Func<(MenuItem Item, Action Choose)>? SmithyMode { get; set; }
    public Settings Settings { get; private set; }
    public EditionTuning Edition => _edition;
    /// <summary>
    /// The public games for the join screen (T116; the user's playtest, "I see active lobbies I can join and then what my
    /// ping is"): the app's <see cref="LobbyBrowser"/> sets them each frame, nearest first.
    /// </summary>
    public IReadOnlyList<ListedGame> Games { get; set; } = [];
    /// <summary>
    /// The derailment's music for the credits screen (GDD v1.4 App. E.6: "the credits screen lists every performer"; note
    /// 194): the manifest's tracks, which the app loads from content/audio/music. Empty, the screen says there's none.
    /// </summary>
    public IReadOnlyList<MusicTrack> Music { get; set; } = [];
    /// <summary>
    /// Everyone else whose work is in the game, after the opera (note 390): the models, the sounds and voices, the textures,
    /// the type and the code, as the app reads them from the content's provenance (<see cref="Credits.Load"/>).
    /// </summary>
    public IReadOnlyList<Credits.Section> CreditSections { get; set; } = [];

    /// <summary>
    /// The MODS screen's (note 323; note 53's "not yet": "an in-game mods screen"): the mods installed, in the order they're
    /// laid over the game, and what couldn't be loaded and why. With none of either, the title has no MODS.
    /// </summary>
    public IReadOnlyList<InstalledMod> InstalledMods { get; set; } = [];
    public IReadOnlyList<string> ModProblems { get; set; } = [];
    /// <summary>
    /// A developer build's own lines on the title screen (note 514), above QUIT: the creature gym's (GDD §34.1, note 676). A
    /// player's build sets none.
    /// </summary>
    public IReadOnlyList<(string Label, string Hint, Launch Launch)> DevEntries { get; set; } = [];
    /// <summary>Started with <c>--no-mods</c>: the mods are listed, and the base game is what's playing.</summary>
    public bool ModsOff { get; set; }
    /// <summary>
    /// The player's profile (GDD App. D.12, note 293): the commendations their crews have given them, for the PROFILE page.
    /// The app loads it at the start and again after each night.
    /// </summary>
    public PlayerProfile.Data Profile { get; set; } = new();
    /// <summary>Where the nights' bookmark stills are kept (note 203), said on the PROFILE page; null, nowhere to say.</summary>
    public string? StillsFolder { get; set; }

    /// <summary>
    /// Note 411: crash reports written since the player last put the notice away (<see cref="CrashReports.Unseen"/>). The app
    /// opens on <see cref="Screen.Crashed"/> while there are; OK or Escape puts it away for good.
    /// </summary>
    public CrashNotice? Crash { get; set; }

    /// <summary>
    /// Note 914: the player's answer about playtest data, and what the question and the "what we collect" page say
    /// (content/ui/playtest-data.json). The app opens on <see cref="Screen.Consent"/> while it's due; both null, and
    /// nothing asks or offers it.
    /// </summary>
    public Telemetry.PlaytestConsent? Playtest { get; set; }
    public Telemetry.PlaytestNotice? PlaytestNotice { get; set; }
    // Where WHAT WE COLLECT was opened from (the question, or the settings), for its BACK.
    Screen _playtestBack = Screen.Settings;

    /// <summary>Note 452: where reports go (content/ui/reports.json); without an address nothing offers to send one.</summary>
    public ReportsTuning? Reports { get; set; }

    /// <summary>
    /// Note 452: REPORT A PROBLEM writes the player's report (what's going on, the log so far) and returns its path; the app's,
    /// from its <see cref="CrashReports"/>. Null and the settings don't offer it.
    /// </summary>
    public Func<string?>? ProblemReport { get; set; }

    bool CanMail => Reports is { To.Length: > 0 };
    /// <summary>Who's playing, for the lobby's default name when the settings have none (the app sets it: the Steam name, or the system's).</summary>
    public string DefaultPlayerName { get; set; } = Environment.UserName;
    /// <summary>The lobby's name as the host screen has it: the one set, or "&lt;PLAYER NAME&gt;'S RUN".</summary>
    public string LobbyName => Settings.LobbyName is { Length: > 0 } set ? set : _blankName ? "" : DefaultLobbyName;
    string DefaultLobbyName => $"{(Settings.PlayerName is { Length: > 0 } me ? me : DefaultPlayerName).ToUpperInvariant()}'S RUN";
    /// <summary>The name erased to nothing while it's being typed: blank until typed or left, then the default again.</summary>
    bool _blankName;
    bool _refresh;

    /// <summary>REFRESH was chosen on the join screen since the last ask: the app looks again (a new search, fresh pings).</summary>
    public bool TakeRefresh()
    {
        bool r = _refresh;
        _refresh = false;
        return r;
    }
    /// <summary>The wire protocol this build speaks: a game on another can't be joined.</summary>
    public int Protocol { get; init; }
    /// <summary>The microphones there are, by name (the app asks the platform): the settings' MICROPHONE goes round them.</summary>
    public IReadOnlyList<string> MicDevices { get; init; } = [];
    /// <summary>The address typed so far on the join screen.</summary>
    public string Address { get; private set; } = "";

    /// <summary>
    /// Note 450: the private run being joined, while its password's asked for (<see cref="Screen.Password"/>): what to start
    /// once it's typed, and the run's name to say whose it is.
    /// </summary>
    (Launch Join, string Name)? _locked;
    /// <summary>The password typed so far for <see cref="_locked"/>.</summary>
    public string JoinPassword { get; private set; } = "";

    /// <summary>
    /// Asks for a private run's password (note 450): picking a locked row on JOIN, or a join the host turned away WRONG
    /// PASSWORD (<paramref name="why"/>, said over it). Typing starts at once: it's all the page is for.
    /// </summary>
    public void AskPassword(Launch join, string name, string? why = null)
    {
        _locked = (join, name);
        JoinPassword = join switch
        {
            Launch.Join { Password: { } p } => p,
            Launch.JoinLobby { Password: { } p } => p,
            _ => "",
        };
        Show(Screen.Password);
        Editing = TextField.JoinPassword;
        Message = why?.ToUpperInvariant();
    }

    /// <summary>The run asked for, with the password as typed.</summary>
    Launch? WithPassword()
    {
        if (_locked is not { } locked || JoinPassword.Trim().Length == 0)
            return null;
        return locked.Join switch
        {
            Launch.Join j => j with { Password = JoinPassword.Trim() },
            Launch.JoinLobby l => l with { Password = JoinPassword.Trim() },
            var other => other,
        };
    }

    /// <summary>A password's longest (note 450): a few words read out over voice.</summary>
    public const int MaxPassword = 24;
    /// <summary>Waiting for the key to bind this control to (T80): the app hands the next one pressed to <see cref="Bind"/>.</summary>
    public Control? Capturing { get; private set; }

    /// <summary>
    /// The menus' interface sounds as they're worked, by name (<see cref="UiCue"/>, the audio checklist's ui-menus): a move,
    /// a choice, backing out, the demo's end card. The app hands them to <c>GameAudio.Ui</c>; tests listen. Null: silent.
    /// </summary>
    public Action<string>? Cue { get; set; }

    /// <summary>The key pressed for the control being bound (a key's name); Escape (<see cref="Back"/>) keeps the old one.</summary>
    public void Bind(string key)
    {
        if (Capturing is not { } c)
            return;
        Capturing = null;
        if (Controls.Reserved.Contains(key))
        {
            Message = $"{Controls.KeyLabel(key)} is kept for the menus.";
            // Not taken: the old key's kept, as if backed out of.
            Cue?.Invoke(UiCue.Back);
            return;
        }
        var was = Enum.GetValues<Control>().FirstOrDefault(o => o != c && Settings.KeyFor(o) == key, c);
        Change(Settings.Bind(c, key));
        Message = was != c ? $"{Controls.Label(was)} moved to {Controls.KeyLabel(Settings.KeyFor(was))}." : null;
        Cue?.Invoke(UiCue.Select);
    }

    /// <summary>A text field's being edited, and wants typed text (the app turns text input on). Only then: never on being chosen.</summary>
    public bool WantsText => Editing is not null;

    /// <summary>The text field being edited (note 264): from Enter or a click on it, to Enter or Esc (or moving off it).</summary>
    public TextField? Editing { get; private set; }

    bool NamingLobby => Editing == TextField.LobbyName;
    const string NameLabel = "NAME: ";

    /// <summary>Stops editing (the text's kept as typed).</summary>
    void EndEdit()
    {
        if (Editing is null)
            return;
        // A crew's name erased to nothing is its slot's again (note 320).
        if (Editing == TextField.CrewName && Open is { } crew && crew.Name.Trim().Length == 0)
            SaveCrew(crew with { Name = DefaultCrewName(crew.Slot) });
        // An engine's name erased to nothing is unnamed again.
        if (Editing == TextField.EngineName && Open is { } engine && (engine.EngineName ?? "").Trim().Length == 0 && engine.EngineName is not null)
            SaveCrew(engine with { EngineName = null });
        Editing = null;
        _blankName = false;
        Cue?.Invoke(UiCue.Select);
    }
    /// <summary>Played in a headset (T36): the hints name the controllers' buttons, not the keys.</summary>
    public bool Headset { get; set; }

    public IReadOnlyList<MenuItem> Items => Entries().Select(e => e.Item).ToList();

    public void Up() => Move(-1);
    public void Down() => Move(+1);
    /// <summary>Changes the selected setting or value (tier, seed, cars, toggles). Not while a field's being typed in.</summary>
    public void Left()
    {
        if (Editing is null)
            Adjust(-1);
    }
    public void Right()
    {
        if (Editing is null)
            Adjust(+1);
    }

    /// <summary>Back a screen (from the title: nothing).</summary>
    public void Back()
    {
        // Esc ends typing in a field, and goes nowhere else (note 264).
        if (Editing is not null)
        {
            EndEdit();
            return;
        }
        if (Capturing is not null)
        {
            Capturing = null;
            Message = null;
            Cue?.Invoke(UiCue.Back);
            return;
        }
        // From the title there's nowhere to back out to.
        if (Screen != Screen.Title)
            Cue?.Invoke(UiCue.Back);
        // In a night (note 292): Escape on the menu's first page closes it, as Escape opened it; the rest back up to it. The
        // blacksmith's catalogue (note 697) is a first page of its own.
        if (Night is not null && Screen is Screen.Night or Screen.Smithy)
        {
            CloseNight();
            return;
        }
        // The crash notice (note 411): Escape is its OK.
        if (Screen == Screen.Crashed)
        {
            PutCrashAway();
            return;
        }
        // Note 914: Escape on the question answers nothing (asked again next time; nothing recorded meanwhile).
        if (Screen == Screen.Consent)
        {
            AfterConsent();
            return;
        }
        if (Screen == Screen.PlaytestData)
        {
            Show(_playtestBack);
            return;
        }
        Show(Screen switch
        {
            Screen.Upgrades or Screen.Stores or Screen.DeleteCrew => Screen.Fortress,
            Screen.Fortress => Screen.Slots,
            Screen.Controls => Screen.Settings,
            Screen.Password => Screen.Join,
            Screen.Title => Screen.Title,
            Screen.Leave or Screen.Settings when Night is not null => Screen.Night,
            // Note 731: back to where PAINT YOUR MASK was chosen (the settings, or the yard's menu).
            Screen.Paint => PaintBack,
            _ => Screen.Title,
        });
    }

    /// <summary>Acts on the selected item: a new screen, a purchase, or something for the app to start.</summary>
    public Launch? Select()
    {
        // Note 450: Enter on the password page, typed, is the join: there's nothing else to do there.
        if (Editing == TextField.JoinPassword && WithPassword() is { } joining)
        {
            EndEdit();
            return joining;
        }
        // Note 535: Enter on a new password typed in a night locks the run with it (blank, nothing changes).
        if (Editing == TextField.NightPassword && NightPassword.Trim().Length > 0)
        {
            Change(Settings with { LobbyPassword = NightPassword.Trim(), PublicLobby = false });
            Message = "THE RUN'S LOCKED WITH THE NEW PASSWORD. TELL YOUR CREW.";
        }
        // Enter ends typing in a field (note 264): it doesn't also choose what's selected.
        if (Editing is not null)
        {
            EndEdit();
            return null;
        }
        var entries = Entries();
        if (entries.Count == 0)
            return null;
        var e = entries[Math.Clamp(Selected, 0, entries.Count - 1)];
        if (!e.Item.Enabled)
            return null;
        Message = null;
        // A text field: Enter (or a click) starts typing into it.
        if (e.Field is { } field)
        {
            Editing = field;
            _blankName = false;
            NightPassword = "";
            Cue?.Invoke(UiCue.Select);
            return null;
        }
        if (e.Select is null)
            return null;
        // Choosing BACK is backing out.
        Cue?.Invoke(e.Back ? UiCue.Back : e.Sound ?? UiCue.Select);
        return e.Select();
    }

    public void Type(string text)
    {
        // A key heard for each typing that took (note 322): a field full, or a character it won't take, is silent.
        string before = TypedText();
        TypeInto(text);
        if (TypedText() != before)
            Cue?.Invoke(UiCue.Type);
    }

    /// <summary>What's in the field being typed into, to hear whether a key took.</summary>
    string TypedText() => Editing switch
    {
        TextField.PlayerName => Settings.PlayerName,
        TextField.CrewName => Open?.Name ?? "",
        TextField.EngineName => Open?.EngineName ?? "",
        TextField.Address => Address,
        TextField.LobbyPassword => Settings.LobbyPassword,
        TextField.JoinPassword => JoinPassword,
        TextField.NightPassword => NightPassword,
        _ => NamingLobby ? LobbyName : "",
    };

    void TypeInto(string text)
    {
        if (Editing == TextField.PlayerName)
        {
            string me = Settings.PlayerName;
            foreach (char c in text)
                if ((char.IsLetterOrDigit(c) || c is ' ' or '\'' or '.' or '-' or '_') && me.Length < Sim.Net.Messages.NameLength && c < 0x7f)
                    me += c;
            Change(Settings with { PlayerName = me.TrimStart() });
            return;
        }
        if (NamingLobby)
        {
            // From the name shown: typing onto the default carries on from it.
            string name = LobbyName;
            foreach (char c in text.ToUpperInvariant())
                if ((char.IsLetterOrDigit(c) || c is ' ' or '\'' or '.' or '-' or '!' or '?') && name.Length < MaxLobbyName)
                    name += c;
            _blankName = false;
            Change(Settings with { LobbyName = name });
            return;
        }
        // Note 450: a password, as the lobby's name is typed (upper case: it doesn't count, so it's shown as it'll be said).
        if (Editing is TextField.LobbyPassword or TextField.JoinPassword or TextField.NightPassword)
        {
            string typed = Editing switch { TextField.LobbyPassword => Settings.LobbyPassword, TextField.NightPassword => NightPassword, _ => JoinPassword };
            foreach (char c in text.ToUpperInvariant())
                if ((char.IsLetterOrDigit(c) || c is ' ' or '\'' or '.' or '-' or '!' or '?') && typed.Length < MaxPassword && c < 0x7f)
                    typed += c;
            typed = typed.TrimStart();
            if (Editing == TextField.LobbyPassword)
                Change(Settings with { LobbyPassword = typed });
            else if (Editing == TextField.NightPassword)
                NightPassword = typed;
            else
                JoinPassword = typed;
            return;
        }
        // The engine's name (the nameplate, note 714): the crew's name's letters and length.
        if (Editing == TextField.EngineName && Open is { } named)
        {
            string name = named.EngineName ?? "";
            foreach (char c in text)
                if ((char.IsLetterOrDigit(c) || c is ' ' or '\'' or '.' or '-' or '!' or '?') && name.Length < MaxCrewName && c < 0x7f)
                    name += c;
            SaveCrew(named with { EngineName = name.TrimStart() });
            return;
        }
        if (Editing == TextField.CrewName && Open is { } crew)
        {
            string name = crew.Name;
            foreach (char c in text)
                if ((char.IsLetterOrDigit(c) || c is ' ' or '\'' or '.' or '-' or '!' or '?') && name.Length < MaxCrewName && c < 0x7f)
                    name += c;
            SaveCrew(crew with { Name = name.TrimStart() });
            return;
        }
        if (Editing != TextField.Address)
            return;
        foreach (char c in text)
            if ((char.IsLetterOrDigit(c) || c is '.' or ':' or '-') && Address.Length < 64)
                Address += c;
    }

    /// <summary>A lobby name's longest: what fits the browser's name column.</summary>
    public const int MaxLobbyName = 24;

    /// <summary>A crew's name's longest (note 320): the lobby's, so the fortress's heading keeps to one line.</summary>
    public const int MaxCrewName = 24;

    public void Erase()
    {
        string before = TypedText();
        EraseOne();
        if (TypedText() != before)
            Cue?.Invoke(UiCue.Type);
    }

    void EraseOne()
    {
        if (Editing == TextField.PlayerName)
        {
            if (Settings.PlayerName.Length > 0)
                Change(Settings with { PlayerName = Settings.PlayerName[..^1] });
            return;
        }
        if (NamingLobby)
        {
            // Erased to nothing, it's the default again.
            if (LobbyName.Length == 0)
                return;
            string name = LobbyName[..^1];
            _blankName = name.Trim().Length == 0;
            Change(Settings with { LobbyName = _blankName ? "" : name });
            return;
        }
        if (Editing == TextField.LobbyPassword && Settings.LobbyPassword.Length > 0)
        {
            Change(Settings with { LobbyPassword = Settings.LobbyPassword[..^1] });
            return;
        }
        if (Editing == TextField.NightPassword && NightPassword.Length > 0)
        {
            NightPassword = NightPassword[..^1];
            return;
        }
        if (Editing == TextField.JoinPassword && JoinPassword.Length > 0)
        {
            JoinPassword = JoinPassword[..^1];
            return;
        }
        if (Editing == TextField.CrewName && Open is { Name.Length: > 0 } crew)
        {
            SaveCrew(crew with { Name = crew.Name[..^1] });
            return;
        }
        if (Editing == TextField.EngineName && Open is { EngineName.Length: > 0 } engine)
        {
            SaveCrew(engine with { EngineName = engine.EngineName[..^1] });
            return;
        }
        if (Editing == TextField.Address && Address.Length > 0)
            Address = Address[..^1];
    }

    public void Show(Screen screen)
    {
        // The in-night menu's pages are only there over a night (note 292).
        if (screen is Screen.Night or Screen.Leave && Night is null || screen is Screen.Smithy && (Night is null || Smithy is null))
            screen = Screen.Title;
        if (screen is Screen.DeleteCrew && Open is null)
            screen = Screen.Slots;
        if (screen is Screen.Crashed && Crash is null)
            screen = Screen.Title;
        if (screen is Screen.Consent or Screen.PlaytestData && (Playtest is null || PlaytestNotice is null))
            screen = Screen.Title;
        _blankName = false;
        Editing = null;
        Screen = screen;
        Selected = 0;
        _scroll = 0;
        _follow = true;
        // Skip to the first item you can do something with.
        var entries = Entries();
        while (Selected < entries.Count - 1 && !entries[Selected].Item.Enabled)
            Selected++;
    }

    /// <summary>A night that couldn't start (a join nobody answered, a host's line this build won't make): back where it was chosen, saying why.</summary>
    public void Failed(Screen from, string why)
    {
        Show(from);
        Message = why.ToUpperInvariant();
    }

    /// <summary>
    /// The night the in-night menu is open over (note 292), or null: the menu's shut (or this is the front end proper).
    /// </summary>
    public NightMenu? Night { get; private set; }

    /// <summary>
    /// Opens the in-night menu (note 292, Escape in a night): RESUME, SETTINGS, INVITE and LEAVE, over the night, which goes
    /// on (it's the crew's, not this player's to stop). Where the menus were before the night is put back as it closes.
    /// </summary>
    public void OpenNight(NightMenu night)
    {
        _before ??= (Screen, Selected, Message);
        Night = night;
        Message = null;
        Show(Screen.Night);
        Cue?.Invoke(UiCue.Select);
    }

    /// <summary>
    /// The night as it is now, while the menu's open (note 292): someone joins or leaves, the report comes up, the train goes
    /// through the gate. The page and the selection stay where they are.
    /// </summary>
    public void RefreshNight(NightMenu night)
    {
        if (Night is null || Night == night)
            return;
        Night = night;
        int count = Entries().Count;
        if (Selected >= count)
            Selected = Math.Max(0, count - 1);
    }

    /// <summary>
    /// Opens the blacksmith's catalogue over the yard (note 697): Use held on the town's blacksmith. It's the in-night menu's
    /// kind of page, the night going on behind it, and Escape or DONE shuts it.
    /// </summary>
    public void OpenSmithy(NightMenu night, SmithyShop shop)
    {
        _before ??= (Screen, Selected, Message);
        Night = night;
        Smithy = shop;
        Message = null;
        Show(Screen.Smithy);
        Cue?.Invoke(UiCue.Select);
    }

    /// <summary>Shuts the in-night menu (RESUME, or Escape on its first page): back to the night.</summary>
    public void CloseNight()
    {
        if (Night is null)
            return;
        Night = null;
        Smithy = null;
        Editing = null;
        Capturing = null;
        if (_before is var (screen, selected, message))
        {
            Show(screen);
            Selected = selected;
            Message = message;
        }
        _before = null;
    }

    (Screen Screen, int Selected, string? Message)? _before;

    /// <summary>The in-night menu's first page (note 292).</summary>
    List<Entry> NightEntries(NightMenu n)
    {
        var list = new List<Entry>
        {
            new(new("RESUME", "The night goes on while this is open."), () => { CloseNight(); return null; }),
            new(new("SETTINGS"), Go(Screen.Settings)),
        };
        // In the yard (GDD §9: the crew "try on outfits"; note 298): put one on, and the crew see it.
        if (n.Yard)
            list.Add(OutfitEntry("Left and right to try one on: the crew see it."));
        if (n.Yard && HatNames.Count > 0)
            list.Add(HatEntry("From the hat rack: left and right to try one on."));
        // Note 731: and paint the mask, on its own page.
        if (n.Yard)
            list.Add(PaintEntry("Its colour and a stencil, tried on at once: the crew see it."));
        // Note 724: the host's CREW MISCHIEF, in the yard: every machine has it as it's set.
        if (n.Mischief is { } on)
            list.Add(new(new($"CREW MISCHIEF: {(on ? "ON" : "OFF")}", on ? "Shoves, bonks and coal at the crew land." : "Shoves, bonks and coal at the crew land on nobody."),
                () => new Launch.SetMischief(!on)));
        // Note 535 (note 450's "not yet"): the host of a listed run changes who it's for and its password with the lobby open.
        if (n.Lobby is { } lobby)
            list.AddRange(LobbyEntries(lobby));
        // Someone to ask along: a platform lobby that takes invites (its overlay), or a host on the network (its address,
        // to type on JOIN). A night nobody can join (alone, on this machine only) has no INVITE.
        if (n.Invites)
            list.Add(new(new("INVITE", "Your friends, through Steam."), () => new Launch.Invite()));
        else if (n.Hosting && n.JoinAt is { } at)
            list.Add(new(new("INVITE", "Friends on your network join by its address."),
                () => { Message = $"Friends join at {at}: JOIN, then type it."; return null; }));
        // Over, there's nothing to lose by leaving: it goes at once, as Enter does.
        list.Add(new(new(n.Hosting && n.Others > 0 && !n.Over ? "END THE NIGHT" : "LEAVE"), n.Over ? () => new Launch.Leave() : Go(Screen.Leave)));
        return list;
    }

    /// <summary>
    /// A listed run's rows in its host's in-night menu (note 535). MOOD steps from the run's own. PASSWORD shows whether there
    /// is one, never the words (a host's screen may be on a stream): Enter types a new one from blank, Enter again puts it on
    /// the run, Esc keeps the old. OPEN THE RUN takes the lock off. Each goes into the settings, so the next night from the
    /// town is hosted the same; the app hands the run what changed.
    /// </summary>
    List<Entry> LobbyEntries(HostLobby lobby)
    {
        bool typing = Editing == TextField.NightPassword;
        var list = new List<Entry>
        {
            new(new($"MOOD: {Moods.Label(lobby.Mood)}", "Said beside your run on the list: players looking for the same find it first."),
                Toggle(s => s with { Mood = Moods.Step(lobby.Mood, 1) }), by => Change(Settings with { Mood = Moods.Step(lobby.Mood, by) })),
            new(new($"PASSWORD: {(typing ? NightPassword + "_" : lobby.Locked ? "SET" : "NONE")}",
                    typing ? "Type the new one, Enter to lock the run with it. Esc keeps the old."
                    : lobby.Locked ? "Listed with a lock. Enter to type a new one: who's aboard stays aboard."
                    : "Anyone who finds it joins. Enter to type one: the run's listed with a lock."),
                Field: TextField.NightPassword),
        };
        if (lobby.Locked)
            list.Add(new(new("OPEN THE RUN", "The lock comes off: anyone who finds it on the list joins."),
                () => { Change(Settings with { PublicLobby = true }); Message = "THE RUN'S OPEN."; return null; }));
        return list;
    }

    /// <summary>The new password being typed in a night (note 535), from blank; it's the run's on Enter, and nothing on Esc.</summary>
    public string NightPassword { get; private set; } = "";

    /// <summary>
    /// LEAVE's confirmation (note 292): STAY first, so the Enter that chose LEAVE and the next one don't take you out, and
    /// LEAVE saying what it costs the crew. A menu's plain word on the session, not a prompt foretelling the night (§32).
    /// </summary>
    static List<Entry> LeaveEntries(NightMenu n, Func<Launch?> stay)
    {
        // Said under both, so it's read before LEAVE is reached.
        string cost = n.Hosting && n.Others > 0 ? $"You're the host: the night ends for {(n.Others == 1 ? "the other one" : $"all {n.Others} others")} aboard."
            : !n.Hosting ? "The crew carries on without you. JOIN finds them again."
            : n.Campaign ? "The slot carries on from the last stop the train left."
            : "Nothing from tonight is kept.";
        return
        [
            new(new("STAY", cost), stay, Back: true),
            new(new(n.Hosting && n.Others > 0 ? "END THE NIGHT FOR EVERYONE" : "LEAVE", cost), () => new Launch.Leave()),
        ];
    }

    /// <summary>
    /// Deleting a crew (note 320), asked as leaving a night is (note 292): KEEP IT first, so Enter on arriving keeps it, and
    /// what goes said under both. The slot's file goes; the slot lists as empty.
    /// </summary>
    List<Entry> DeleteEntries(CampaignState s)
    {
        string under = s.Current is null ? "" : ", and the night under way";
        string cost = $"{s.Cars} cars, {s.Scrip:0} scrip and {s.Runs} {(s.Runs == 1 ? "night" : "nights")}{under}: gone for good.";
        return
        [
            new(new("KEEP IT", cost), Go(Screen.Fortress), Back: true),
            new(new($"DELETE {CrewName(s).ToUpperInvariant()}", cost), () =>
            {
                _saves.Delete(s.Slot);
                Open = null;
                Show(Screen.Slots);
                Message = $"Slot {s.Slot} is empty.";
                return null;
            }, Sound: UiCue.Delete),
        ];
    }

    /// <summary>
    /// The crash notice (note 411): OK first and lit, so the Enter that's pressed on arriving puts it away; where the report
    /// is, said under both, since the console that said it was never seen.
    /// </summary>
    List<Entry> CrashEntries(CrashNotice c)
    {
        string where = $"Its report is {c.Newest}{(c.Count > 1 ? $", the newest of {c.Count}" : "")}. Send it in with a few words on what you were doing.";
        return
        [
            new(new("OK", where), () => { PutCrashAway(); return null; }, Back: true),
            // Note 452: to the studio, from the player's own mail, the report's folder beside it to attach the file.
            .. CanMail && Report.Load(c.Newest) is { } report
                ? [new Entry(new("SEND THE REPORT", $"A mail to the studio, its subject and a summary filled in; attach {Path.GetFileName(c.Newest)} from the folder that opens beside it."),
                    () => new Launch.Mail(report.MailTo(Reports!, c.Newest), c.Directory))]
                : (Entry[])[],
            new(new("OPEN THE REPORTS", where), () => new Launch.OpenFolder(c.Directory)),
        ];
    }

    /// <summary>REPORT A PROBLEM (note 452): the report written, and the mail to send it opened; if it couldn't be written, said.</summary>
    Launch? ReportProblem()
    {
        if (ProblemReport?.Invoke() is not { } path || Report.Load(path) is not { } report)
        {
            Message = "The report couldn't be written.";
            return null;
        }
        Message = $"Report written: {Path.GetFileName(path)}. Attach it to the mail.";
        return new Launch.Mail(report.MailTo(Reports!, path), Path.GetDirectoryName(path)!);
    }

    /// <summary>
    /// Note 914: the first-run question. YES lit first only in a developer build, where it's on unless the player says no;
    /// a player's build lights neither answer above the other (NO is first: saying yes is a choice made, not a key pressed
    /// through).
    /// </summary>
    List<Entry> ConsentEntries(Telemetry.PlaytestConsent p, Telemetry.PlaytestNotice n)
    {
        // What it's for is said under the heading, read before either answer (ConsentLines); each answer says what it does.
        string dev = p.DefaultOn && n.DevBuild.Length > 0 ? " " + n.DevBuild : "";
        var yes = new Entry(new("YES, RECORD MY NIGHTS", "Your nights are recorded anonymously, to help tune the game. Off or deleted any time in Settings." + dev),
            () => Answer(p, n, yes: true));
        var no = new Entry(new("NO THANKS", "Nothing is recorded. You can change this any time in Settings, PLAYTEST DATA."), () => Answer(p, n, yes: false));
        var what = new Entry(new("WHAT WE COLLECT", "Every kind of thing it records, and for how long, before you answer."),
            () => { _playtestBack = Screen.Consent; Show(Screen.PlaytestData); return null; });
        return p.DefaultOn ? [yes, no, what] : [no, yes, what];
    }

    Launch? Answer(Telemetry.PlaytestConsent p, Telemetry.PlaytestNotice n, bool yes)
    {
        p.Choose(yes, n);
        AfterConsent();
        Message = yes ? "Thank you. Playtest data is on: Settings, PLAYTEST DATA to change it." : "Playtest data is off.";
        return null;
    }

    /// <summary>After the question: the crash notice if there's one waiting, else the title.</summary>
    void AfterConsent() => Show(Crash is not null ? Screen.Crashed : Screen.Title);

    Entry PlaytestSwitch(Telemetry.PlaytestConsent p) =>
        new(new($"PLAYTEST DATA: {(p.Recording ? "ON" : "OFF")}", p.Recording
                ? "Your nights are recorded, anonymously, to help tune the game. Left and right, or Enter, to turn it off."
                : "Nothing is recorded. On, your nights are recorded anonymously to help tune the game."),
            () => { p.Choose(!p.Recording, PlaytestNotice!); return null; }, _ => p.Choose(!p.Recording, PlaytestNotice!));

    /// <summary>
    /// Note 914, WHAT WE COLLECT: the switch, each kind of thing the recorder writes (its detail under it as it's chosen),
    /// what it never does, where it goes and for how long, and DELETE.
    /// </summary>
    List<Entry> PlaytestDataEntries(Telemetry.PlaytestConsent p, Telemetry.PlaytestNotice n) =>
    [
        PlaytestSwitch(p),
        Heading("WHAT WE COLLECT"),
        .. n.Collect.Select(f => new Entry(new(f.What, f.Detail))),
        new(new("WHAT WE NEVER COLLECT", n.Never)),
        new(new($"KEPT FOR {n.RetentionMonths} MONTHS", n.WhereLine)),
        new(new("DELETE MY PLAYTEST DATA", "Deletes every night recorded on this computer, and starts a new id, so nothing sent before can be tied to you after."),
            () =>
            {
                int gone = p.Delete();
                Message = $"Deleted {gone} recorded {(gone == 1 ? "night" : "nights")}{(p.Recording ? "; a new id from now on" : "")}.";
                return null;
            }),
        BackTo(_playtestBack),
    ];

    /// <summary>The crash notice put away: its reports aren't said again, and the title's up.</summary>
    void PutCrashAway()
    {
        if (Crash is { } c)
            CrashReports.MarkSeen(c);
        Crash = null;
        Show(Screen.Title);
    }

    static string DefaultCrewName(int slot) => $"Crew {slot}";


    /// <summary>The title's MODS line (note 323): how many are on tonight, and how many couldn't load.</summary>
    string ModsLine()
    {
        string notLoaded = ModProblems.Count == 0 ? "" : $", {ModProblems.Count} {(ModProblems.Count == 1 ? "problem" : "problems")}";
        return ModsOff ? $"{InstalledMods.Count} installed, all off tonight (--no-mods){notLoaded}."
            : $"{InstalledMods.Count} laid over the game{notLoaded}.";
    }

    /// <summary>A package's name as it reads (Thunderstore's have no spaces): "LateDispatch", "Late_Dispatch" → "LATE DISPATCH".</summary>
    public static string ModName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ").ToUpperInvariant();

    /// <summary>A scan's problem (<c>ContentMods.Scan</c>: "X isn't loaded: …", "X is installed 2 times: …") as a row's label.</summary>
    static string ProblemLabel(string problem)
    {
        string who = ModName(problem.Split(' ', 2)[0]);
        return problem.Contains("isn't loaded", StringComparison.Ordinal) ? $"{who}: NOT LOADED"
            : problem.Contains(" times", StringComparison.Ordinal) ? $"{who}: INSTALLED TWICE" : who;
    }

    /// <summary>What a crew is called: its name, or its slot's while it has none (erased, mid-typing).</summary>
    static string CrewName(CampaignState s) => s.Name.Trim() is { Length: > 0 } named ? named : DefaultCrewName(s.Slot);

    /// <summary>A change to the open crew, kept at once (the front end saves every change to the slot).</summary>
    void SaveCrew(CampaignState s)
    {
        _saves.Save(s);
        Open = s;
    }

    /// <summary>Back at the title after a quick night or a join, with the edition's word after a night (the demo's).</summary>
    /// <param name="say">What to say over the edition's word: the crew put this player off (note 724).</param>
    public void NightOver(string? say = null)
    {
        Show(Screen.Title);
        Message = say ?? (_edition.AfterNight is { Length: > 0 } after ? after : null);
        // The demo's end card (T79): its wishlist line coming up, and (note 434) WISHLIST ON STEAM lit under it.
        if (Message is not null)
            Cue?.Invoke(UiCue.EndCard);
        if (Items.ToList().FindIndex(i => i.Label == "WISHLIST ON STEAM") is >= 0 and int wishlist)
            Selected = wishlist;
    }

    /// <summary>
    /// Opens a crew ridden with at the fortress (note 724): its save as this machine keeps it, hosted from here. Gone (or one
    /// that won't read), the slots and crews.
    /// </summary>
    public void ShowCrew(string crew, string? message = null)
    {
        Open = _saves.LoadCrew(crew);
        if (Open is null)
        {
            Show(Screen.Slots);
            return;
        }
        Show(Screen.Fortress);
        Message = message;
    }

    /// <summary>Opens a slot at the fortress (after a night, with how it went).</summary>
    public void ShowFortress(int slot, string? message = null)
    {
        Open = _saves.Load(slot);
        if (Open is null)
        {
            Show(Screen.Slots);
            return;
        }
        Show(Screen.Fortress);
        Message = message;
    }

    // The mouse (note 264, the director's notes on build 1121: "we really should be able to click with a mouse"): what the
    // last Draw put where, in the overlay's pixels, for HitTest; and the list's scroll.
    readonly List<(MenuHit Hit, float X, float Y, float W, float H)> _hits = [];
    int _scroll;
    /// <summary>The scroll follows the selection (the keys moved it), or stays where the wheel put it.</summary>
    bool _follow = true;

    /// <summary>The first row shown of <paramref name="count"/> in <paramref name="rows"/>: the selection kept in view while the keys move it.</summary>
    int First(int rows, int count)
    {
        if (_follow)
        {
            if (Selected < _scroll)
                _scroll = Selected;
            else if (Selected >= _scroll + rows)
                _scroll = Selected - rows + 1;
        }
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, count - rows));
        return _scroll;
    }

    /// <summary>What's under the pointer at (<paramref name="x"/>, <paramref name="y"/>), in the overlay's pixels, as last drawn; null for nothing.</summary>
    public MenuHit? HitTest(float x, float y)
    {
        MenuHit? hit = null;
        foreach (var (h, hx, hy, w, ht) in _hits)
            if (x >= hx && x < hx + w && y >= hy && y < hy + ht)
                hit = h;
        return hit;
    }

    /// <summary>The pointer's moved onto an item: it's the selection (anything greyed out is passed over).</summary>
    public void Hover(int item)
    {
        if (Capturing is not null || item == Selected)
            return;
        var entries = Entries();
        if (item < 0 || item >= entries.Count || !entries[item].Item.Enabled)
            return;
        Selected = item;
        Cue?.Invoke(UiCue.Move);
    }

    /// <summary>
    /// A click (note 264): on an item, it's selected and chosen, as Enter does (a field starts typing); on a value's arrows
    /// it's stepped, as left and right do, and a value with nothing to choose steps on (back, with <paramref name="right"/>,
    /// the right button). A click anywhere else ends typing in a field.
    /// </summary>
    public Launch? Click(MenuHit? at, bool right = false)
    {
        if (Capturing is not null)
            return null;
        var entries = Entries();
        if (at is not { } hit || hit.Item < 0 || hit.Item >= entries.Count || !entries[hit.Item].Item.Enabled)
        {
            EndEdit();
            return null;
        }
        var e = entries[hit.Item];
        // Clicking the field being typed in carries on typing; clicking anything else ends it first.
        if (Editing is not null && e.Field == Editing)
            return null;
        if (Editing is not null)
            EndEdit();
        if (Selected != hit.Item)
            Hover(hit.Item);
        if (hit.Step != 0 || right)
        {
            Adjust(hit.Step != 0 ? hit.Step : -1);
            return null;
        }
        if (e.Select is null && e.Field is null && e.Adjust is not null)
        {
            Adjust(+1);
            return null;
        }
        return Select();
    }

    /// <summary>The wheel: <paramref name="notches"/> up (positive) scrolls a long list back towards its top.</summary>
    public void Scroll(int notches)
    {
        if (notches == 0)
            return;
        _scroll -= notches;
        _follow = false;
    }

    void Move(int by)
    {
        var entries = Entries();
        if (entries.Count == 0)
            return;
        int was = Selected;
        // Moving off a field ends typing in it (the text's kept).
        if (Editing is not null)
        {
            Editing = null;
            _blankName = false;
        }
        _follow = true;
        // Over anything greyed out.
        for (int i = 0; i < entries.Count; i++)
        {
            Selected = (Selected + by + entries.Count) % entries.Count;
            if (entries[Selected].Item.Enabled)
                break;
        }
        Message = null;
        if (Selected != was)
            Cue?.Invoke(UiCue.Move);
    }

    void Adjust(int by)
    {
        var entries = Entries();
        if (Selected >= entries.Count || entries[Selected].Adjust is not { } adjust)
            return;
        string was = entries[Selected].Item.Label;
        adjust(by);
        // A value stepped along (a tier, the seed, cars, a setting) sounds as a move, when it moved at all.
        if (Items.ElementAtOrDefault(Selected)?.Label != was)
            Cue?.Invoke(UiCue.Move);
    }

    /// <summary>Alt+Enter (note 672): a window to the whole screen or back, kept like any other setting.</summary>
    public void AltEnter() => Change(Settings.AltEnter());

    void Change(Settings settings)
    {
        Settings = settings;
        settings.Save(_settingsPath);
    }

    /// <summary>A night's tier, seed, cars and bots: the quick night's and the host's.</summary>
    List<Entry> NightOptions() =>
    [
            new(new($"TIER: {Name(_tiers[_tier])}", _tiers.Length > 1 ? "Local, frontier, the dead lines, the deep territory: farther is darker." : "The full game goes farther out.", _tiers.Length > 1),
                null, by => _tier = (_tier + by + _tiers.Length) % _tiers.Length),
            new(new($"SEED: {_seed}", "The same seed is the same line for everyone."), null, by => _seed = by > 0 ? _seed + 1 : Math.Max(1UL, _seed - 1)),
            new(new($"CARS: {_cars}"), null, by => _cars = Math.Clamp(_cars + by, 3, MaxCars)),
            CrewEntry(),
    ];

    /// <summary>Who goes out with you (T89): bots, or none. The quick night's and the campaign's (note 611).</summary>
    Entry CrewEntry() =>
        new(new(_bots == 0 ? "CREW: JUST YOU" : $"CREW: YOU AND {_bots} BOT{(_bots == 1 ? "" : "S")}",
            "Bots drive, stoke, man the rear gun and lend a hand. None, and it's all yours to do."), null, by => _bots = Math.Clamp(_bots + by, 0, MaxBots));

    /// <param name="Back">A BACK item: choosing it sounds as backing out.</param>
    /// <param name="Field">A text field: choosing it starts typing into it (note 264).</param>
    readonly record struct Entry(MenuItem Item, Func<Launch?>? Select = null, Action<int>? Adjust = null, bool Back = false, TextField? Field = null,
        string? Sound = null);

    List<Entry> Entries() => Screen switch
    {
        Screen.Title =>
        [
            .. _edition.Campaign ? [new Entry(new("CAMPAIGN", "Three slots. Take contracts, buy cars, go farther out."), () => { Show(Screen.Slots); return null; })] : (Entry[])[],
            new(new("QUICK NIGHT", _tiers.Length > 1 ? "Any tier, any seed, alone or with bots." : "Any seed, alone or with bots."), () => { Show(Screen.QuickNight); return null; }),
            // Note 434: the demo's end card has something to press; hidden until the edition names the store's app.
            .. _edition.StoreUrl is { } store ? [new Entry(new("WISHLIST ON STEAM", "The full game's page on Steam, where it can be wishlisted."),
                () => new Launch.Wishlist(_edition.StoreAppId, store))] : (Entry[])[],
            // T116 (the co-op games' way, Lethal Company's ship): the host opens a lobby, the yard, and waits there; friends
            // join it from the list, by invite or by address; the host drives out of the yard when everyone's in.
            // The user's playtest: "You should be able to host a run, not a night. The button should just say HOST."
            new(new("HOST", "Open a lobby in the yard for a run. Friends join; you drive out when everyone's in."), () => { Show(Screen.Host); return null; }),
            new(new("JOIN", "The public games, nearest first, a Steam invite, or an address."), () => { Show(Screen.Join); return null; }),
            new(new("SETTINGS"), () => { Show(Screen.Settings); return null; }),
            new(new("PROFILE", "What your crews have commended you for."), () => { Show(Screen.Profile); return null; }),
            .. InstalledMods.Count + ModProblems.Count > 0 ? [new Entry(new("MODS", ModsLine()), Go(Screen.Mods))] : (Entry[])[],
            new(new("CREDITS", "Whose work is in the game: the opera, the models, the sounds, the code."), () => { Show(Screen.Credits); return null; }),
            .. DevEntries.Select(d => new Entry(new(d.Label, d.Hint), () => d.Launch)),
            new(new("QUIT"), () => new Launch.Quit()),
        ],
        Screen.Slots => [.. _saves.List().Select(x => SlotEntry(x.Slot, x.State)), .. CrewEntries(), BackTo(Screen.Title)],
        Screen.Fortress => FortressEntries(),
        Screen.Paint => PaintEntries(),
        Screen.Upgrades => UpgradeEntries(),
        Screen.Smithy when Smithy is { } shop => SmithyEntries(shop),
        Screen.Stores => StoreEntries(),
        Screen.QuickNight =>
        [
            .. NightOptions(),
            new(new("PLAY"), () => new Launch.Night(RouteSpec(_tiers[_tier], _seed), _cars, Host: false) { Bots = _bots }),
            BackTo(Screen.Title),
        ],
        Screen.Host =>
        [
            .. NightOptions(),
            // Note 450 (the director, 8 Oct 2026: "Private matches should be password gated"): both are listed, a private run
            // with a lock; the director's answer, "Listed with a lock". Its password's typed below.
            new(new($"VISIBILITY: {(Settings.PublicLobby ? "PUBLIC" : "PRIVATE")}", Settings.PublicLobby
                    ? "Listed: anyone on your network, or on Steam, finds it on their join screen and joins."
                    : "Listed with a lock: a joiner needs the password. Your Steam friends don't."),
                Toggle(s => s with { PublicLobby = !s.PublicLobby }), _ => Change(Settings with { PublicLobby = !Settings.PublicLobby })),
            .. Settings.PublicLobby ? (Entry[])[] :
            [
                new Entry(new($"PASSWORD: {Settings.LobbyPassword}{(Editing == TextField.LobbyPassword ? "_" : "")}",
                    Editing == TextField.LobbyPassword ? "Type it; Enter or Esc when it's done. Upper or lower case, it's the same."
                        : Settings.LobbyPassword.Trim().Length == 0 ? "Enter to type one: a private run needs a password." : "Enter to change it: tell your crew."),
                    Field: TextField.LobbyPassword),
            ],
            MoodEntry("Said beside your run on the list, so players looking for the same find it first."),
            MischiefEntry(),
            new(new($"{NameLabel}{LobbyName}{(NamingLobby ? "_" : "")}", NamingLobby ? "Type the name; Enter or Esc when it's done." : "Enter to rename it: what the join screen calls it."),
                Field: TextField.LobbyName),
            new(new("OPEN THE LOBBY", PasswordMissing ? "Type a password first: a private run needs one." : "You wait in the yard with the train; you drive out when everyone's in.",
                    !PasswordMissing),
                () => new Launch.Night(RouteSpec(_tiers[_tier], _seed), _cars, Host: true)
                {
                    Bots = _bots,
                    Public = true,
                    LobbyName = LobbyName.Trim() is { Length: > 0 } named ? named : DefaultLobbyName,
                    Password = Settings.PublicLobby ? null : Settings.LobbyPassword.Trim(),
                    Mood = Settings.Mood,
                    Mischief = _mischief,
                }),
            BackTo(Screen.Title),
        ],
        Screen.Password =>
        [
            new(new($"PASSWORD: {new string('*', JoinPassword.Length)}{(Editing == TextField.JoinPassword ? "_" : "")}",
                Editing == TextField.JoinPassword ? "Type it, then Enter to join. Esc when it's done." : "Enter to type it: ask the host."),
                Field: TextField.JoinPassword),
            new(new("JOIN", null, JoinPassword.Trim().Length > 0), WithPassword),
            BackTo(Screen.Join),
        ],
        Screen.Join =>
        [
            new(new(Row("LOBBY", "CREW", "TIER", "MOOD", "RECORD", "PING"), null, false)),
            .. ListedGame.Sort(Games, Settings.Mood).Select(g => new Entry(new(Row((g.Locked ? $"{BitmapFont.Lock} " : "") + g.Name.ToUpperInvariant(), g.Max > 0 ? $"{g.Aboard}/{g.Max}" : $"{g.Aboard}",
                    // What can't be joined says why where its tier would be (the row's width is the frame's).
                    g.Protocol != Protocol ? "OTHER VERSION" : g.Full ? "FULL" : Enum.TryParse<RouteTier>(g.Tier, out var t) ? Name(t) : "-",
                    Moods.Tag(g.Mood), g.RecordKm is { } km ? $"{km} KM" : "-", g.PingMs is { } ms ? $"{ms:0} MS" : "-- MS"),
                    g.Locked ? $"Private: the host's password to join. {g.Where}" : g.Where, g.Protocol == Protocol && !g.Full),
                () =>
                {
                    if (!g.Locked)
                        return g.Join;
                    AskPassword(g.Join, g.Name);
                    return null;
                })),
            .. Games.Count == 0 ? [new Entry(new("  NO GAMES YET", "When someone opens a lobby, it shows here.", false))] : (Entry[])[],
            // Note 450: what you're looking for puts those runs first, each nearest first. Under the list, so the nearest
            // run is still what's chosen as the screen opens.
            MoodEntry("Runs of your mood first on the list, nearest first. Your runs are listed with it too."),
            new(new("REFRESH", "Look again, and ping everyone afresh."), () => { _refresh = true; Message = "Looking..."; return null; }),
            new(new($"ADDRESS: {Address}{(Editing == TextField.Address ? "_" : "")}", Editing == TextField.Address
                    ? "Type it, and the host's port if it isn't the usual one (host:port), or the host's join code; Enter or Esc when it's done."
                    : "A private game, or one far off: Enter to type its address, or its join code (note 896)."), Field: TextField.Address),
            new(new("JOIN", null, Address.Length > 0), () => new Launch.Join(Address)),
            BackTo(Screen.Title),
        ],
        Screen.Settings =>
        [
            // Note 386: in sections, a heading over each (never selected), so TEXT SIZE isn't found past the microphone.
            // Note 904 (the director, 16 Oct: "I don't see a developer setting to be able to go in and unlock everything"): a
            // developer build's unlock (note 747) is here, first, as well as at the forge. A player's build never sets SmithyMode.
            .. SmithyMode is not null ? [Heading("DEVELOPER"), DevUnlockEntry()] : (Entry[])[],
            Heading("YOU"),
            // Note 267: the name the crew and the report know you by, typed here (empty: your Steam or system name). Not in a
            // night (note 292): the crew have it already, from when you joined.
            .. Night is not null ? (Entry[])[] : [new Entry(new($"PLAYER NAME: {(Editing == TextField.PlayerName ? Settings.PlayerName + "_" : Settings.PlayerName is { Length: > 0 } me ? me.ToUpperInvariant() : DefaultPlayerName.ToUpperInvariant())}",
                Editing == TextField.PlayerName ? "Type your name; Enter or Esc when it's done. Erased, it's your Steam or system name."
                    : "Enter to type the name the crew and the report know you by."), Field: TextField.PlayerName)],
            OutfitEntry(Night is null ? "Left and right to change: what the crew see you in." : "Tried on in the yard; past the gate, from the next night."),
            PaintEntry(Night is null ? "Your gas mask's colour and a stencil: what the crew see on it." : "Tried on in the yard; past the gate, from the next night."),
            Heading("SOUND AND VOICE"),
            new(new($"SOUND: {(Settings.Mute ? "OFF" : "ON")}"), Toggle(s => s with { Mute = !s.Mute }), _ => Change(Settings with { Mute = !Settings.Mute })),
            new(new($"VOICE: {(Settings.PushToTalk ? $"PUSH TO TALK ({(Settings.ToggleHolds ? "TAP" : "HOLD")} {Controls.KeyLabel(Settings.KeyFor(Control.Talk))})" : "OPEN MIC")}"), Toggle(s => s with { PushToTalk = !s.PushToTalk }), _ => Change(Settings with { PushToTalk = !Settings.PushToTalk })),
            // The audio checklist's mix-settings: the volumes, the microphone and its level.
            Volume("MASTER VOLUME", "Everything you hear.", Settings.MasterVolume, (s, v) => s with { MasterVolume = v }),
            Volume("EFFECTS VOLUME", "The train, the world, the things in the dark, your own hands.", Settings.EffectsVolume, (s, v) => s with { EffectsVolume = v }),
            Volume("MUSIC VOLUME", "The drone under the night, and the opera when it's over.", Settings.MusicVolume, (s, v) => s with { MusicVolume = v }),
            Volume("VOICE VOLUME", "The crew, near and on the radio, the dead, and the yard.", Settings.VoiceVolume, (s, v) => s with { VoiceVolume = v }),
            new(new($"MICROPHONE: {(Settings.MicDevice is { Length: > 0 } mic ? mic.ToUpperInvariant() : "DEFAULT")}", "Left and right to change. From the next night."),
                Toggle(s => s with { MicDevice = NextMic(s.MicDevice, 1) }), by => Change(Settings with { MicDevice = NextMic(Settings.MicDevice, by) })),
            new(new($"MIC LEVEL: {Settings.MicLevel * 100:0}%", "Left and right to change: up if the crew can't hear you."), null,
                by => Change(Settings with { MicLevel = Math.Clamp(Math.Round(Settings.MicLevel + by * 0.1, 1), 0, 3) })),
            Heading("SCREEN"),
            new(new($"HUD: {(Settings.Hud ? "ON" : "OFF")}", "F1 in the game as well."), Toggle(s => s with { Hud = !s.Hud }), _ => Change(Settings with { Hud = !Settings.Hud })),
            new(new($"CONTROL HINTS: {(Settings.ControlHints ? "ON" : "OFF")}", "The keys in the corner for what you're holding or driving."),
                Toggle(s => s with { ControlHints = !s.ControlHints }), _ => Change(Settings with { ControlHints = !Settings.ControlHints })),
            // T83: the display.
            // Note 672: WINDOWED, BORDERLESS or FULLSCREEN (exclusive); Alt+Enter anywhere, a window or the whole screen.
            new(new($"DISPLAY: {Settings.Display.ToString().ToUpperInvariant()}", "Left and right to change. Alt+Enter in the game as well."),
                Toggle(s => s with { Display = Settings.Cycle(Enum.GetValues<DisplayMode>(), s.Display, 1) }),
                by => Change(Settings with { Display = Settings.Cycle(Enum.GetValues<DisplayMode>(), Settings.Display, by) })),
            new(new($"RESOLUTION: {Settings.Resolution}", "Left and right to change."), Toggle(s => s with { Resolution = Settings.Cycle(Settings.Resolutions, s.Resolution, 1) }),
                by => Change(Settings with { Resolution = Settings.Cycle(Settings.Resolutions, Settings.Resolution, by) })),
            new(new($"RENDER SCALE: {Settings.RenderScale * 100:0}%", "Draws the scene smaller and scales it up: faster, softer."), Toggle(s => s with { RenderScale = Settings.Cycle(Settings.RenderScales, s.RenderScale, 1) }),
                by => Change(Settings with { RenderScale = Settings.Cycle(Settings.RenderScales, Settings.RenderScale, by) })),
            new(new($"VSYNC: {(Settings.VSync ? "ON" : "OFF")}"), Toggle(s => s with { VSync = !s.VSync }), _ => Change(Settings with { VSync = !Settings.VSync })),
            Heading("CONTROLS AND COMFORT"),
            new(new($"MOUSE SPEED: {Settings.MouseSpeed:0.0}", "Left and right to change."), null, by => Change(Settings with { MouseSpeed = Math.Clamp(Math.Round(Settings.MouseSpeed + by * 0.1, 1), 0.2, 3) })),
            // Note 297: comfort.
            new(new($"INVERT MOUSE: {(Settings.InvertMouse ? "ON" : "OFF")}", "On, pushing the mouse away looks down."), Toggle(s => s with { InvertMouse = !s.InvertMouse }),
                _ => Change(Settings with { InvertMouse = !Settings.InvertMouse })),
            new(new($"FIELD OF VIEW: {Settings.EyeFov:0}", "Left and right to change: degrees, top to bottom. Wider sees more, and costs the GPU more."),
                Toggle(s => s with { FieldOfView = Settings.Cycle(Settings.FieldsOfView, s.EyeFov, 1) }),
                by => Change(Settings with { FieldOfView = Math.Clamp(Settings.EyeFov + by * 5, Settings.FieldsOfView[0], Settings.FieldsOfView[^1]) })),
            new(new($"CAMERA SHAKE: {(Settings.CameraShake <= 0 ? "OFF" : $"{Settings.CameraShake * 100:0}%")}", "The boiler's shake and a straining car's judder, in your eyes."),
                Toggle(s => s with { CameraShake = Settings.Cycle(Settings.CameraShakes, s.CameraShake, 1) }),
                by => Change(Settings with { CameraShake = Math.Clamp(Math.Round(Settings.CameraShake + by * 0.25, 2), 0, 1) })),
            // The headset's comfort is set as a night starts: not in a night's menu (note 292), which is the window's.
            .. Night is not null ? (Entry[])[] :
            [
                new Entry(new($"VR TURNING: {(Settings.VrTurn == VrTurn.Snap ? "SNAP" : "SMOOTH")}"), Toggle(s => s with { VrTurn = s.VrTurn == VrTurn.Snap ? VrTurn.Smooth : VrTurn.Snap }),
                    _ => Change(Settings with { VrTurn = Settings.VrTurn == VrTurn.Snap ? VrTurn.Smooth : VrTurn.Snap })),
                new Entry(new($"VR COMFORT VIGNETTE: {(Settings.VrVignette ? "ON" : "OFF")}"), Toggle(s => s with { VrVignette = !s.VrVignette }), _ => Change(Settings with { VrVignette = !Settings.VrVignette })),
            ],
            new(new("CONTROLS", "Rebind the keys."), Go(Screen.Controls)),
            Heading("ACCESSIBILITY"),
            // Note 347: the print, bigger; the HUD's and these menus' alike, at once.
            new(new($"TEXT SIZE: {Settings.TextScale * 100:0}%",
                "Left and right to change: the HUD's print and the menus', bigger."),
                Toggle(s => s with { TextSize = Settings.Cycle(Settings.TextSizes, s.TextSize, 1) }),
                by => Change(Settings with { TextSize = Settings.Cycle(Settings.TextSizes, Settings.TextSize, by) })),
            // Note 348: the HUD's colours that mean something, told apart without red against green.
            new(new($"COLOURS: {(Settings.Colours == HudColours.Colourblind ? "COLOURBLIND" : "STANDARD")}",
                Settings.Colours == HudColours.Colourblind ? "The HUD's good in blue, warnings in yellow, danger in red." : "The HUD's good in green, warnings in amber, danger in red."),
                Toggle(s => s with { Colours = s.Colours == HudColours.Colourblind ? HudColours.Standard : HudColours.Colourblind }),
                _ => Change(Settings with { Colours = Settings.Colours == HudColours.Colourblind ? HudColours.Standard : HudColours.Colourblind })),
            // Note 349: what's heard, named, and where.
            new(new($"CAPTIONS: {(Settings.Captions ? "ON" : "OFF")}", "The sounds worth hearing named as you hear them, and where they are."),
                Toggle(s => s with { Captions = !s.Captions }), _ => Change(Settings with { Captions = !Settings.Captions })),
            // Note 404: a band behind the print in play, as a subtitle's.
            new(new($"TEXT BACKING: {(Settings.TextBacking ? "ON" : "OFF")}", "A dark band behind the HUD's print and the captions, to read them over a bright night."),
                Toggle(s => s with { TextBacking = !s.TextBacking }), _ => Change(Settings with { TextBacking = !Settings.TextBacking })),
            // Note 383: holds as toggles.
            new(new($"HOLD KEYS: {(Settings.ToggleHolds ? "TOGGLE" : "HOLD")}", Settings.ToggleHolds
                    ? "Run, the brake, talk, the radio and the crew: press once for on, again for off."
                    : "Run, the brake, talk, the radio and the crew: held down. TOGGLE makes each a press for on and off."),
                Toggle(s => s with { ToggleHolds = !s.ToggleHolds }), _ => Change(Settings with { ToggleHolds = !Settings.ToggleHolds })),
            // Note 350: a new player's first nights.
            new(new($"FIRST NIGHTS: {(Settings.FirstNights ? "ON" : "OFF")}", "Tips while a night's built, and the controls in the yard for your first nights."),
                Toggle(s => s with { FirstNights = !s.FirstNights }), _ => Change(Settings with { FirstNights = !Settings.FirstNights })),
            // Note 914: playtest data, on or off, and what it is.
            .. Playtest is { } playtest && PlaytestNotice is not null
                ? [Heading("PLAYTEST DATA"), PlaytestSwitch(playtest),
                    new Entry(new("WHAT WE COLLECT", "Every kind of thing it records, for how long, and DELETE."), () => { _playtestBack = Screen.Settings; Show(Screen.PlaytestData); return null; })]
                : (Entry[])[],
            // Note 452: something wrong that didn't crash it, reported as a crash is, from the player's own mail.
            .. CanMail && ProblemReport is not null
                ? [Heading("HELP"), new Entry(new("REPORT A PROBLEM", "Writes what the game's doing and its log to a file, and opens a mail to the studio to send it with a few words."),
                    ReportProblem)]
                : (Entry[])[],
            BackTo(Night is null ? Screen.Title : Screen.Night),
        ],
        // A heading a section (greyed: the selection steps over it), then a row a credit with its licence and source under
        // it (DrawCredits): the opera's tracks first (E.6), then everyone else's (note 390).
        // The badges and their tally are drawn over the list (DrawProfile): BACK is all there is to choose.
        Screen.Profile => [BackTo(Screen.Title)],
        Screen.Night when Night is { } n => NightEntries(n),
        Screen.Leave when Night is { } n => LeaveEntries(n, Go(Screen.Night)),
        Screen.DeleteCrew when Open is { } s => DeleteEntries(s),
        Screen.Crashed when Crash is { } c => CrashEntries(c),
        Screen.Consent when Playtest is { } p && PlaytestNotice is { } n => ConsentEntries(p, n),
        Screen.PlaytestData when Playtest is { } p && PlaytestNotice is { } n => PlaytestDataEntries(p, n),
        // Note 323: a row a mod, its description under it; what couldn't load, why. Nothing here changes them: they're laid
        // over as the game starts, from its folders or a mod manager's profile (note 53).
        Screen.Mods =>
        [
            .. InstalledMods.Select(m => new Entry(new($"{ModName(m.Name)} {m.Version}", m.Description is { Length: > 0 } d ? d : "No description."))),
            .. ModProblems.Select(p => new Entry(new(ProblemLabel(p), p))),
            new(new("BACK", "Laid over as the game starts. A crew all run the same ones."),
                Go(Screen.Title), Back: true),
        ],
        Screen.Credits =>
        [
            new(new("THE OPERA", Music.Count == 0 ? "No music in this build." : Credits.OperaLine, Enabled: false)),
            .. Music.Select(t => new Entry(new($"{t.Work} - {t.Composer}, {t.Year}", Credits.TrackLine(t)))),
            .. CreditSections.Where(s => s.Lines.Count > 0).SelectMany(s => (Entry[])[
                new(new(s.Name, s.Blurb, Enabled: false)),
                .. s.Lines.Select(l => new Entry(new(l.Title, l.Detail)))]),
            new(new("BACK"), Go(Screen.Title)),
        ],
        Screen.Controls =>
        [
            .. Enum.GetValues<Control>().Select(c => new Entry(
                new($"{Controls.Label(c)}: {(Capturing == c ? "PRESS A KEY" : Controls.KeyLabel(Settings.KeyFor(c)))}", "Enter, then the key. Esc keeps it."),
                () => { Capturing = c; Message = null; return null; })),
            new(new("RESET TO DEFAULTS", null, Settings.Keys.Count > 0), () => { Change(Settings with { Keys = new() }); Message = "Keys reset."; return null; }),
            BackTo(Screen.Settings),
        ],
        _ => [],
    };

    /// <summary>
    /// The join screen's columns: the font's fixed-width, so spaces line them up under the header. A private run's name
    /// starts with the font's padlock (note 450); the ping always has a figure, "--" till it's measured. RECORD (note 535) is
    /// how far the host's crews have gone, beside who the run's for: what a player feeling competitive looks for. For its room
    /// the name keeps to a name's longest (a locked one's last two letters give way to the padlock).
    /// </summary>
    static string Row(string name, string crew, string tier, string mood, string record, string ping) =>
        $"{(name.Length > MaxLobbyName ? name[..MaxLobbyName] : name),-25}{crew,-6}{tier,-15}{mood,-8}{record,7}{ping,7}";

    /// <summary>A private run with no password yet: it can't be opened (note 450).</summary>
    bool PasswordMissing => !Settings.PublicLobby && Settings.LobbyPassword.Trim().Length == 0;

    /// <summary>MOOD (note 450): one setting, the host's run and what a joiner's looking for.</summary>
    Entry MoodEntry(string detail) =>
        new(new($"MOOD: {Moods.Label(Settings.Mood)}", detail), Toggle(s => s with { Mood = Moods.Step(s.Mood, 1) }),
            by => Change(Settings with { Mood = Moods.Step(Settings.Mood, by) }));

    int MaxCars => _edition.MaxCars > 0 ? Math.Min(_edition.MaxCars, _campaign.MaxCars) : _campaign.MaxCars;

    Func<Launch?> Go(Screen screen) => () => { Show(screen); return null; };

    /// <summary>A section's heading (note 386): greyed so the keys pass over it, drawn as a heading.</summary>
    /// <summary>The developer build's unlock as a row (note 747; in Settings, note 904): its toggle, and the menu redrawn.</summary>
    Entry DevUnlockEntry()
    {
        var mode = SmithyMode!();
        return new(mode.Item, () =>
        {
            mode.Choose();
            SmithyMode!();
            return null;
        }, _ =>
        {
            mode.Choose();
            SmithyMode!();
        });
    }

    static Entry Heading(string label) => new(new(label, null, Enabled: false, Heading: true));

    Entry BackTo(Screen screen) => new(new("BACK"), Go(screen), Back: true);

    Func<Launch?> Toggle(Func<Settings, Settings> change) => () => { Change(change(Settings)); return null; };

    /// <summary>
    /// The crew's looks by name, for OUTFIT (note 298): look.json's crew colours, in order (the app sets them). Each look
    /// is its colour and the cap or helmet and scarf that come with it.
    /// </summary>
    public IReadOnlyList<string> OutfitNames { get; set; } = ["RED", "BLUE", "OCHRE", "TEAL", "GREEN", "VIOLET", "ORANGE", "WHITE"];

    /// <summary>The hat rack's hats by name (note 714; look.json crewHats): the app sets them when the train has the rack, else none.</summary>
    public IReadOnlyList<string> HatNames { get; set; } = [];

    /// <summary>HAT (note 714): none, then each of the rack's in turn, round again; offered only with the rack aboard.</summary>
    Entry HatEntry(string detail)
    {
        int n = HatNames.Count, now = Math.Clamp(Settings.Hat, 0, n);
        return new(new($"HAT: {(now == 0 ? "NONE" : HatNames[now - 1].ToUpperInvariant())}", detail), Toggle(s => s with { Hat = (now + 1) % (n + 1) }),
            by => Change(Settings with { Hat = ((now + by) % (n + 1) + n + 1) % (n + 1) }));
    }

    /// <summary>The mask paints by name (note 731; look.json maskPaintNames), in order (the app sets them).</summary>
    public IReadOnlyList<string> MaskPaintNames { get; set; } = ["BONE", "SOOT", "SIGNAL RED", "OXBLOOD", "OCHRE", "LEAF GREEN", "SEA BLUE", "SLATE", "PLUM", "PITCH PINE"];
    /// <summary>The stencils by name (note 731; look.json maskStencils), in order (the app sets them).</summary>
    public IReadOnlyList<string> StencilNames { get; set; } = ["STRIPE", "CROSS", "TEETH", "STAR", "NUMBER", "MOUSTACHE", "HEART", "CROW", "BOLT", "SMILE", "X EYES", "ANCHOR"];
    /// <summary>A stencil's colour when none's picked (look.json maskInk, from 1).</summary>
    public int MaskInk { get; set; } = 1;

    /// <summary>Where PAINT YOUR MASK goes back to: the settings, or the yard's menu (note 731).</summary>
    Screen _paintBack = Screen.Settings;
    Screen PaintBack => _paintBack is Screen.Night && Night is null ? Screen.Settings : _paintBack;

    /// <summary>PAINT YOUR MASK (note 731): the settings' and the yard menu's way onto its page, saying what's on it now.</summary>
    Entry PaintEntry(string detail)
    {
        var p = Settings.MaskPaint(MaskPaintNames.Count, StencilNames.Count);
        string now = !p.Painted ? "UNPAINTED"
            : p.Stencil == 0 ? MaskPaintNames[p.Colour - 1]
            : $"{(p.Colour == 0 ? "BARE" : MaskPaintNames[p.Colour - 1])}, {StencilNames[p.Stencil - 1]}";
        return new(new($"PAINT YOUR MASK: {now}", detail), () =>
        {
            _paintBack = Screen;
            Show(Screen.Paint);
            return null;
        });
    }

    /// <summary>
    /// PAINT YOUR MASK's page (note 731, GDD §29): the coat, the stencil and the stencil's colour, each left and right round
    /// its list (the coat and the stencil through none). In the yard each change is tried on at once (the app sends it as the
    /// outfit's is); past the gate it's kept for the next night.
    /// </summary>
    List<Entry> PaintEntries()
    {
        int paints = MaskPaintNames.Count, stencils = StencilNames.Count;
        var p = Settings.MaskPaint(paints, stencils);
        int ink = p.Ink > 0 ? p.Ink : Math.Clamp(MaskInk, 1, Math.Max(1, paints));
        string tried = Night is { Yard: true } ? "The crew see it at once." : Night is not null ? "Past the gate: from the next night." : "What the crew see on your mask.";
        // Round a list from 1 with none (0) among it, or without.
        static int Round(int now, int by, int count, bool none) =>
            none ? ((now + by) % (count + 1) + count + 1) % (count + 1) : ((now - 1 + by) % count + count) % count + 1;
        var list = new List<Entry>
        {
            new(new($"COAT: {(p.Colour == 0 ? "UNPAINTED" : MaskPaintNames[p.Colour - 1])}", $"The mask's colour. {tried}"),
                Toggle(s => s with { MaskColour = Round(p.Colour, 1, paints, true) }), by => Change(Settings with { MaskColour = Round(p.Colour, by, paints, true) })),
            new(new($"STENCIL: {(p.Stencil == 0 ? "NONE" : StencilNames[p.Stencil - 1])}", $"Painted over the coat. {tried}"),
                Toggle(s => s with { MaskStencil = Round(p.Stencil, 1, stencils, true) }), by => Change(Settings with { MaskStencil = Round(p.Stencil, by, stencils, true) })),
        };
        if (p.Stencil > 0 && paints > 0)
            list.Add(new(new($"STENCIL COLOUR: {MaskPaintNames[ink - 1]}", $"The stencil's paint: pick one that shows on the coat. {tried}"),
                Toggle(s => s with { MaskInk = Round(ink, 1, paints, false) }), by => Change(Settings with { MaskInk = Round(ink, by, paints, false) })));
        list.Add(new(new("DONE"), () => { Show(PaintBack); return null; }, Back: true));
        return list;
    }

    /// <summary>
    /// The fortress board's CREW SIGIL (note 731): none, then each stencil in turn. Saved with the crew at once; whoever hosts
    /// from this board takes it into the night, on the engine's side and small on every mask.
    /// </summary>
    Entry SigilEntry(CampaignState s)
    {
        int n = StencilNames.Count, now = Math.Clamp(s.Sigil, 0, n);
        Launch? Set(int by)
        {
            SaveCrew(s with { Sigil = ((now + by) % (n + 1) + n + 1) % (n + 1) });
            return null;
        }
        return new(new($"CREW SIGIL: {(now == 0 ? "NONE" : StencilNames[now - 1])}", "Your crew's mark, painted on the engine's side and small on every mask."),
            () => Set(1), by => Set(by));
    }

    /// <summary>OUTFIT (note 298): the crew's pick (your place's look), then each look in turn, round again.</summary>
    Entry OutfitEntry(string detail)
    {
        int n = OutfitNames.Count;
        int now = Settings.Outfit >= 0 && Settings.Outfit < n ? Settings.Outfit : -1;
        string name = now < 0 ? "THE CREW'S PICK" : OutfitNames[now].ToUpperInvariant();
        return new(new($"OUTFIT: {name}", detail), Toggle(s => s with { Outfit = (now + 2) % (n + 1) - 1 }),
            by => Change(Settings with { Outfit = ((now + 1 + by) % (n + 1) + n + 1) % (n + 1) - 1 }));
    }

    /// <summary>A volume row: left and right a tenth at a time, from silent to full.</summary>
    Entry Volume(string label, string what, double now, Func<Settings, double, Settings> set) =>
        new(new($"{label}: {now * 100:0}%", what), null, by => Change(set(Settings, Math.Clamp(Math.Round(now + by * 0.1, 1), 0, 1))));

    /// <summary>The next microphone along (the default first, then each by name), wrapping round.</summary>
    string NextMic(string now, int by)
    {
        string[] all = ["", .. MicDevices];
        return Settings.Cycle(all, Array.IndexOf(all, now) < 0 ? "" : now, by);
    }

    /// <summary>A crew ridden with's id, for its night's launch (note 724); null for a slot's own crew.</summary>
    static string? RodeWith(CampaignState s) => s.Slot == 0 ? s.CrewId : null;

    /// <summary>
    /// The crews this player has ridden with (GDD §11; note 724), under the slots: each crew's save as its host last sent it,
    /// "CREW &lt;NAME&gt; · NIGHT 7 · LAST HOSTED BY DAVE", and any of them hosted from here. A crew in one of this player's own
    /// slots is listed only when the copy ridden with has gone further. One whose train was taken at dawn stays listed, ended.
    /// </summary>
    List<Entry> CrewEntries()
    {
        var mine = _saves.List().Where(x => x.State is not null).Select(x => x.State!).ToList();
        var crews = _saves.Crews().Where(c => !mine.Any(m => m.CrewId == c.CrewId && m.Runs >= c.Runs)).ToList();
        if (crews.Count == 0)
            return [];
        var list = new List<Entry> { new(new("CREWS YOU'VE RIDDEN WITH", null, false, Heading: true)) };
        foreach (var c in crews)
        {
            string host = c.LastHost.Length > 0 ? $" · LAST HOSTED BY {Sim.Net.Messages.CleanName(c.LastHost).ToUpperInvariant()}" : "";
            string id = c.CrewId;
            list.Add(c.Over
                ? new(new($"CREW {CrewName(c).ToUpperInvariant()} · TAKEN AT DAWN{host}", $"{c.Runs} nights. The company has no more trains for them."),
                    () => { ShowCrew(id); return null; })
                : new(new($"CREW {CrewName(c).ToUpperInvariant()} · NIGHT {c.Runs + 1}{host}", $"{c.Cars} cars, {c.Scrip:0} scrip. Host its next night from here."),
                    () => { ShowCrew(id); return null; }));
        }
        return list;
    }

    Entry SlotEntry(int slot, CampaignState? s)
    {
        if (s is null)
            return new(new($"SLOT {slot}: EMPTY", "Start a campaign: three cars on local work, and no scrip."), () =>
            {
                var fresh = Campaign.New(_campaign, slot, DefaultCrewName(slot), _newSeed());
                _saves.Save(fresh);
                ShowFortress(slot);
                return null;
            });
        string underway = s.Current is null ? "" : ", a night under way";
        // Note 632 (the director, the inbox, 12 Oct: "the campaign ends"): a crew whose train was taken at dawn is done.
        if (s.Over)
            return new(new($"SLOT {slot}: {CrewName(s).ToUpperInvariant()}, TAKEN AT DAWN", $"{s.Runs} nights. The company has no more trains for them."), () =>
            {
                ShowFortress(slot);
                return null;
            });
        return new(new($"SLOT {slot}: {CrewName(s).ToUpperInvariant()}", $"{s.Cars} cars, {s.Scrip:0} scrip, {s.Runs} nights{underway}"), () =>
        {
            ShowFortress(slot);
            return null;
        });
    }

    List<Entry> FortressEntries()
    {
        if (Open is not { } s)
            return [BackTo(Screen.Slots)];
        if (s.Over)
            return OverEntries(s);
        var list = new List<Entry>();
        if (s.Current is { } tonight)
        {
            // Spec E: a night that was under way picks up from its last autosave (or starts again, if it had none).
            string from = s.Checkpoint is null ? "from the yard" : "from the last facility it left";
            list.Add(new(new($"CARRY ON: {Name(tonight.Tier)} {tonight.Seed}", $"The night that was under way, {from}."),
                () => new Launch.CampaignNight(s.Slot, -1, Resume: true, _host) { Bots = _bots, Crew = RodeWith(s) }));
        }
        else
        {
            var offers = Campaign.Offers(_campaign, _run, s);
            for (int i = 0; i < offers.Count; i++)
            {
                var c = offers[i];
                int contract = i;
                // GDD §9 "choose a route" (note 637: a contract is just a route): the train leaves empty, and its cars are paid
                // by what its stops load, about the tier's car-load each.
                list.Add(new(new($"TONIGHT: {Name(c.Tier)} {c.Seed}, ABOUT {c.PerCar:0} A CAR-LOAD", $"Whatever its stops load. Up to about {c.PerCar * s.Cars:0} on {s.Cars} cars."),
                    () => new Launch.CampaignNight(s.Slot, contract, Resume: false, _host) { Bots = _bots, Crew = RodeWith(s) }));
            }
        }
        // The crew's bots (note 611): who goes out with you tonight, and on to the next night from the town after it.
        list.Add(CrewEntry());
        double carCost = Campaign.NextCarCost(_campaign, s);
        bool full = s.Cars >= _campaign.MaxCars;
        list.Add(new(new(full ? "BUY A CAR: THE CONSIST IS FULL" : $"BUY A CAR: {carCost:0} SCRIP", full ? null : $"Car {s.Cars + 1}. More cars carry more, and burn more, and need more hands.",
            !full && s.Current is null), () => Buy(Campaign.BuyCar(_campaign, s), $"A car bought: {s.Cars + 1} now.")));
        // GDD §9 "add or remove railcars" (note 182): a car off brings back a share of its price; a shorter train works a lower tier.
        if (_campaign.SellCar is { } sell)
        {
            bool fewest = s.Cars <= sell.Fewest;
            var tierAfter = Campaign.TierFor(_campaign, s.Cars - 1);
            string after = tierAfter != Campaign.TierFor(_campaign, s.Cars) ? $" The consist drops to {Name(tierAfter)} work." : "";
            list.Add(new(new(fewest ? $"TAKE A CAR OFF: {sell.Fewest} IS THE FEWEST" : $"TAKE A CAR OFF: +{Campaign.SellBack(_campaign, s):0} SCRIP",
                fewest ? null : $"Car {s.Cars} back to the yard for {sell.Share:P0} of its price.{after}", !fewest && s.Current is null),
                () => Buy(Campaign.SellCar(_campaign, s), $"A car taken off: {s.Cars - 1} now.")));
        }
        // GDD v1.4 App. E.12 question 4: the fortress sells spare repair kits; each rides in a crew locker (note 173).
        if (_campaign.SpareKit is { } spare)
        {
            bool most = s.SpareKits >= spare.Most;
            string have = s.SpareKits == 0 ? "None aboard yet" : $"{s.SpareKits} aboard";
            list.Add(new(new(most ? $"SPARE REPAIR KIT: THE LOCKERS HOLD {spare.Most}" : $"BUY A SPARE REPAIR KIT: {spare.Cost:0} SCRIP",
                $"{have}. A ruptured boiler with every kit lost strands the night; spares ride in the crew lockers.", !most && s.Current is null),
                () => Buy(Campaign.BuySpareKit(_campaign, s), $"A spare repair kit bought: {s.SpareKits + 1} now.")));
        }
        if (_campaign.Stores is not null)
            list.Add(new(new("STORES", StoresLine(s.Stores), s.Current is null), () => { Show(Screen.Stores); return null; }));
        // Bought from the town's blacksmith, in the yard (note 697): here, what's on their shelves, to plan the night's buying.
        list.Add(new(new("UPGRADES", "What the blacksmith sells. Buy it at their forge, in the yard.", s.Current is null), () => { Show(Screen.Upgrades); return null; }));
        // Note 731: the crew's sigil, picked by whoever hosts from here: on the engine's side and every mask, kept in the save.
        list.Add(SigilEntry(s));
        list.Add(new(new($"PLAY: {(_host ? "HOST FOR FRIENDS" : "ALONE")}", "Left and right to change."), () => { _host = !_host; return null; }, _ => _host = !_host));
        // Note 724: hosting, the run's CREW MISCHIEF, as on the host screen.
        if (_host)
            list.Add(MischiefEntry());
        // A crew ridden with (note 724) is the crew's, not this machine's: its name and its file are whoever hosts it last's.
        if (s.Slot == 0)
        {
            list.Add(BackTo(Screen.Slots));
            return list;
        }
        // Note 320 (note 33's "not yet": renaming a crew or deleting a slot was `dt campaign`'s alone).
        bool naming = Editing == TextField.CrewName;
        list.Add(new(new($"{NameLabel}{(naming ? s.Name : CrewName(s)).ToUpperInvariant()}{(naming ? "_" : "")}",
            naming ? "Type the name; Enter or Esc when it's done." : "Enter to rename the crew."), Field: TextField.CrewName));
        // The nameplate (note 714): the engine's name, once its plates are bought.
        if (s.Upgrades.Contains("nameplate"))
        {
            bool christening = Editing == TextField.EngineName;
            string engineName = s.EngineName is { Length: > 0 } en ? en : christening ? "" : "UNNAMED";
            list.Add(new(new($"ENGINE: {engineName.ToUpperInvariant()}{(christening ? "_" : "")}",
                christening ? "Type her name; Enter or Esc when it's done." : "Enter to name the engine: it's cast in her plates, and the clerk reads it."),
                Field: TextField.EngineName));
        }
        list.Add(new(new("DELETE THIS CREW", $"Slot {s.Slot} emptied for a new crew. It asks first."), Go(Screen.DeleteCrew)));
        list.Add(BackTo(Screen.Slots));
        return list;
    }

    /// <summary>
    /// A slot whose campaign is over (note 632; the director, the inbox, 12 Oct: a train taken at dawn ends it): what the crew
    /// came to, and in place of the board, a new crew from the fortress in the same slot.
    /// </summary>
    List<Entry> OverEntries(CampaignState s)
    {
        var last = s.History.LastOrDefault();
        return
        [
            new(new(Hud.CampaignOver, $"{CrewName(s)}'s train was taken at dawn{(last is null ? "" : $" on {last.Route}")}, after {s.Runs} nights.", Heading: true)),
            new(new("A NEW CREW", "Start again from a fortress: three cars on local work, and no scrip. This crew's save is gone."), () =>
            {
                var fresh = Campaign.New(_campaign, s.Slot, DefaultCrewName(s.Slot), _newSeed());
                _saves.Save(fresh);
                ShowFortress(s.Slot, "A new crew has signed on.");
                return null;
            }),
            BackTo(Screen.Slots),
        ];
    }

    List<Entry> UpgradeEntries()
    {
        if (Open is not { } s)
            return [BackTo(Screen.Fortress)];
        // Note 687: bought from the town's blacksmith, not the menu; here it's to look at.
        var list = CatalogueEntries(s, _ => () => { Message = "Buy it from the blacksmith, at the forge in the yard."; return null; });
        if (Purse(s, false) is { } purse)
            list.Insert(0, purse);
        list.Add(BackTo(Screen.Fortress));
        return list;
    }

    /// <summary>The blacksmith's catalogue (note 697): the shelves as the menu's, bought on the host's word.</summary>
    List<Entry> SmithyEntries(SmithyShop shop)
    {
        var list = CatalogueEntries(shop.State, u => () =>
        {
            if (!shop.CanBuy || SmithyBuy is not { } buy)
            {
                Message = $"The host keeps the crew's scrip: ask {shop.Payer ?? "them"} to buy it.";
                return null;
            }
            bool had = shop.State.Upgrades.Contains(u.Id);
            var p = buy(u.Id);
            if (p.Ok)
                Smithy = shop with { State = p.State };
            Message = !p.Ok ? p.Refused : had && !p.State.Upgrades.Contains(u.Id) ? $"{u.Name}: taken off before the gate."
                : had ? $"{u.Name}: painted before the gate." : $"{u.Name}: fitted before the gate.";
            return null;
        }, free: SmithyFree);
        if (Purse(shop.State, SmithyFree) is { } purse)
            list.Insert(0, purse);
        if (SmithyMode?.Invoke() is { } mode)
            list.Insert(0, new(mode.Item, () =>
            {
                mode.Choose();
                return null;
            }));
        list.Add(new(new("DONE", "Back to the yard."), () => { CloseNight(); return null; }, Back: true));
        return list;
    }

    /// <summary>
    /// The catalogue's first line when the crew can't buy anything on it yet (note 904; the director, 16 Oct: "It should tell me
    /// that nothing is available to purchase until you have scrip"): no scrip at all, or less than the cheapest thing they could
    /// otherwise have. Null when something's in reach (or the shelves are free, a developer build's unlock, note 747).
    /// </summary>
    Entry? Purse(CampaignState s, bool free)
    {
        if (free)
            return null;
        var costs = _campaign.Upgrades.Where(u => !s.Upgrades.Contains(u.Id) && Campaign.Unavailable(_campaign, s, u) is null)
            .Select(u => Campaign.UpgradeCost(_campaign, s, u)).ToList();
        if (costs.Count == 0 || s.Scrip >= costs.Min())
            return null;
        string earn = "Scrip's paid at the end of a night for what the train delivered. What each thing costs is on its line.";
        return new(new(s.Scrip <= 0 ? "NOTHING CAN BE BOUGHT UNTIL YOU HAVE SCRIP" : $"NOTHING HERE YOU CAN AFFORD YET: {s.Scrip:0} SCRIP",
            s.Scrip <= 0 ? $"The crew has none. {earn}" : $"The cheapest is {costs.Min():0} scrip. {earn}", Enabled: false));
    }

    /// <summary>
    /// The catalogue by what it's for (note 678), for <paramref name="s"/>: the tools first, then the rest of the shelves, each
    /// with its line, and why one isn't for sale (a tier the train doesn't work yet, or a fitting it won't go with).
    /// </summary>
    List<Entry> CatalogueEntries(CampaignState s, Func<UpgradeDef, Func<Launch?>> select, bool free = false)
    {
        var list = new List<Entry>();
        foreach (var kind in new[] { UpgradeKind.Tool, UpgradeKind.Whimsy, UpgradeKind.Fun, UpgradeKind.Silly })
        {
            var shelf = _campaign.Upgrades.Where(u => u.Kind == kind).ToList();
            if (shelf.Count == 0)
                continue;
            list.Add(new(new(KindHeading(kind), Heading: true)));
            foreach (var u in shelf)
            {
                bool owned = s.Upgrades.Contains(u.Id);
                double cost = Campaign.UpgradeCost(_campaign, s, u);
                string? why = owned || free ? null : Campaign.Unavailable(_campaign, s, u);
                // The shop says which upgrades the night doesn't model yet (ARCHITECTURE §8 note 29).
                string detail = u.Line ?? $"{(u.Size == UpgradeSize.Major ? "Major" : "Small")}{(u.Effect.Count == 0 && u.Tally is null ? ", not modelled yet" : "")}.";
                // A livery owned is picked by choosing it again (note 714): the train's in the last picked.
                bool livery = u.Effect.ContainsKey("livery");
                bool painted = livery && owned && Campaign.LiveryOf(_campaign, s) == u.Id;
                string label = free ? $"{u.Name.ToUpperInvariant()}: {(owned ? "OWNED, TAKE IT OFF" : "FREE")}"
                    : painted ? $"{u.Name.ToUpperInvariant()}: ON THE TRAIN"
                    : livery && owned ? $"{u.Name.ToUpperInvariant()}: OWNED, PAINT HER IN IT"
                    : owned ? $"{u.Name.ToUpperInvariant()}: OWNED"
                    // Note 904 (the director, 16 Oct: "it should tell me ... until you have scrip"): what it costs, on every line.
                    : why is not null && Campaign.TierFor(_campaign, s.Cars) < u.FromTier ? $"{u.Name.ToUpperInvariant()}: {Name(u.FromTier)} AND ON, {cost:0} SCRIP"
                    : $"{u.Name.ToUpperInvariant()}: {cost:0} SCRIP";
                list.Add(new(new(label, why is null ? detail : $"{why}.", (free || !owned || livery && !painted) && why is null), select(u)));
            }
        }
        return list;
    }

    static string KindHeading(UpgradeKind kind) => kind switch
    {
        UpgradeKind.Tool => "TOOLS",
        UpgradeKind.Whimsy => "WHIMSY",
        UpgradeKind.Fun => "FUN",
        _ => "SILLY",
    };

    static string StoresLine(Stores st) => st.Any
        ? $"Bought for tonight: {st.Powder} powder, {st.Lamps} lamps, {st.Extinguishers} extinguishers{(st.Flares > 0 ? $", {st.Flares} flare pistols" : "")}."
        : "Powder and shot, spare lamps and extinguishers for tonight.";

    /// <summary>The departure's stores (GDD §9 "stock ... powder and shot, lamps, repair supplies"; note 182): for the coming night.</summary>
    List<Entry> StoreEntries()
    {
        if (Open is not { } s || _campaign.Stores is not { } st)
            return [BackTo(Screen.Fortress)];
        var list = new List<Entry>();
        void Row(StoreKind kind, StoreItem item, string name, string detail)
        {
            int have = s.Stores.Of(kind);
            bool most = have >= item.Most;
            list.Add(new(new(most ? $"{name}: {have} OF {item.Most}, ALL A NIGHT TAKES" : $"{name}: {item.Cost:0} SCRIP ({have} OF {item.Most})", detail, !most && s.Current is null),
                () => Buy(Campaign.BuyStores(_campaign, s, kind), $"{name.ToLowerInvariant()} bought for tonight.")));
        }
        Row(StoreKind.Powder, st.Powder, "POWDER AND SHOT", $"A crate: {st.Powder.Each} more rounds for every gun tonight.");
        Row(StoreKind.Lamp, st.Lamps, "SPARE LAMP", "In the guard van beside its own, for when one goes out over the side.");
        Row(StoreKind.Extinguisher, st.Extinguishers, "SPARE EXTINGUISHER", "Loose in the guard van: a second hand on a fire. It doesn't recharge.");
        // Note 871: the flare pistol, stocked from the Frontier tier (upgrades.md §4).
        if (st.Flares is { } flares)
        {
            if (Campaign.TierFor(_campaign, s.Cars) < flares.FromTier)
                list.Add(new(new($"FLARE PISTOL: STOCKED FOR {Campaign.TierWords(flares.FromTier).ToUpperInvariant()} WORK",
                    "A longer train first.", false), () => null));
            else
                Row(StoreKind.Flare, flares, "FLARE PISTOL", $"Loaded with {flares.Each} flares. Each lights a village for a minute, and the Fire Flies follow it away from the train.");
        }
        list.Add(BackTo(Screen.Fortress));
        return list;
    }

    Launch? Buy(Purchase p, string done)
    {
        if (p.Ok)
        {
            _saves.Save(p.State);
            Open = p.State;
        }
        Message = p.Ok ? done : p.Refused;
        return null;
    }

    const string CreditHints = "[UP/DOWN] SCROLL   [ESC] BACK";

    /// <summary>
    /// The credits (E.6: CC0 asks for none, the screen lists every performer anyway; note 390, everyone else whose work is in
    /// the game): each credit two lines, whose and what, then its licence and source, under its section's heading; scrolled
    /// to keep the selection in view.
    /// </summary>
    void DrawCredits(Overlay o, float x, float y, int width, int height)
    {
        var items = Items;
        // Room under the list for the selected credit's line in full, over three rows at the least.
        int rows = Math.Max(1, (int)((height - y - 48) / 20));
        int first = First(rows, items.Count);
        // A section's first credit brings its heading into view with it.
        if (first > 0 && first == Selected && !items[first - 1].Enabled)
            _scroll = first = first - 1;
        int shown = Math.Min(rows, items.Count - first);
        float w = width - x - 8;
        int chars = (int)((w - 8) / o.Font.Advance);
        UiStyle.Plate(o, x - 8, y - 6, w + 4, shown * 20 + 6);
        for (int i = first; i < first + shown; i++)
        {
            bool on = i == Selected;
            if (!items[i].Enabled)
            {
                // A heading: its name, and its line in faint print.
                o.Text(x, y, Fit(items[i].Label, chars), Ink);
                if (items[i].Detail is { } blurb)
                    o.Text(x, y + 9, Fit(blurb, chars), Faint);
                y += 20;
                continue;
            }
            _hits.Add((new MenuHit(i), x - 4, y - 1, w - 4, 19));
            if (on)
                o.Rect(x - 4, y - 1, w - 4, items[i].Detail is null ? 9 : 19, UiStyle.Lit with { W = 0.14f });
            o.Text(x, y, Fit((on ? "> " : "  ") + items[i].Label, chars), on ? Amber : Ink);
            if (items[i].Detail is { } line)
                o.Text(x, y + 9, Fit("    " + line, chars), Dim);
            y += 20;
        }
        // The selected credit whole (a long attribution, Commons title or ensemble name is cut above), wrapped.
        if (Selected < items.Count && items[Selected] is { Enabled: true } sel && (sel.Label.Length + 2 > chars || sel.Detail is { } d && d.Length + 4 > chars))
        {
            y += 6;
            // A word longer than the row (a Commons address) is broken across rows rather than cut.
            var whole = UiStyle.Wrap(o, $"{sel.Label}. {sel.Detail}".ToUpperInvariant(), w - 8)
                .SelectMany(r => r.Chunk(Math.Max(1, chars)).Select(c => new string(c))).ToList();
            // As many rows as fit above the hints' keycaps; the last says so if there's more.
            int fit = Math.Min(whole.Count, Math.Max(1, (int)((height - 22 - y) / 9) + 1));
            for (int i = 0; i < fit; i++)
                o.Text(x, y + i * 9, Fit(i == fit - 1 && whole.Count > fit ? whole[i] + " ..." : whole[i], chars), Ink);
        }
    }

    /// <summary>The PROFILE page's rows (note 293): each of D.12's starter set, in its order, with the times it's been given.</summary>
    public IReadOnlyList<(UiStyle.Commendation Badge, int Given)> Tally =>
        [.. Enum.GetValues<UiStyle.Commendation>().Select(c => (c, Profile.Commendations.GetValueOrDefault(Sim.Run.Commendations.StarterSet[(int)c])))];

    /// <summary>The line over the PROFILE page's badges: how many in all, or how they're come by.</summary>
    /// <summary>
    /// The player's record (note 535), as the PROFILE page says it: the furthest any of their crews has gone, which a run they
    /// host is listed with beside its mood. Null till a crew's gone a kilometre.
    /// </summary>
    public string? RecordLine => Profile.RecordKm is >= 1 and var km ? $"RECORD: {(int)km} KM, THE FURTHEST A CREW OF YOURS HAS GONE" : null;

    public string TallyLine => Tally.Sum(t => t.Given) is var total && total == 0
        ? "NO COMMENDATIONS YET: A CREW GIVES THEM AT THE END OF A NIGHT." : $"GIVEN BY YOUR CREWS: {total} IN ALL";

    /// <summary>
    /// The PROFILE page (note 293; GDD App. D.12: "where it's kept: the player profile, not the character. Characters change
    /// on death; the tally survives"): the five badges, a row each, with how many times a crew has given it; those not yet
    /// given are drawn faint. Then where the nights' stills are kept. Returns where the list goes under it.
    /// </summary>
    float DrawProfile(Overlay o, float x, float y, int width, int height)
    {
        var tally = Tally;
        // Note 535: the record a run you host is listed with, over the tally.
        if (RecordLine is { } record)
            foreach (var line in UiStyle.Wrap(o, record, width - x - 8))
            {
                o.Text(x, y, line, Ink);
                y += 10;
            }
        // Wrapped at a bigger TEXT SIZE (note 347).
        foreach (var line in UiStyle.Wrap(o, TallyLine, width - x - 8))
        {
            o.Text(x, y, line, Faint);
            y += 10;
        }
        y += 7;
        // A badge a row; where the canvas is too short for them (TEXT SIZE, note 347), the names and counts as print alone.
        bool compact = y + tally.Count * 22 + 8 + 22 + 50 > height;
        float row = compact ? 10 : 22;
        UiStyle.Plate(o, x - 8, y - 6, Math.Min(width - x, 220), tally.Count * row + 8);
        foreach (var (c, n) in tally)
        {
            float w = 0;
            if (!compact)
            {
                // Not given yet: the badge greyed under a veil, its name faint.
                w = UiStyle.Badge(o, x, y, c, ribbon: n > 0 ? null : new Vector4(0.3f, 0.3f, 0.3f, 1)) + 8;
                if (n == 0)
                    o.Rect(x, y, w - 8, w - 8, new Vector4(0.08f, 0.08f, 0.09f, 0.6f));
            }
            float ty = compact ? y : y + 4;
            o.Text(x + w, ty, UiStyle.Name(c), n > 0 ? Ink : Faint);
            o.Text(x + 178, ty, n > 0 ? $"x{n}" : "-", n > 0 ? Amber : Faint);
            y += row;
        }
        y += 6;
        if (StillsFolder is { Length: > 0 } folder)
        {
            int chars = (int)((width - x - 8) / o.Font.Advance);
            o.Text(x, y, Fit("THE NIGHTS' STILLS ARE KEPT IN", chars), Faint);
            o.Text(x, y + 9, Fit(folder, chars), Dim);
            y += 22;
        }
        return y + 6;
    }

    static string Fit(string s, int chars) => s.Length <= chars ? s : s[..Math.Max(0, chars - 3)] + "...";

    static string Name(RouteTier tier) => tier switch
    {
        RouteTier.Local => "LOCAL",
        RouteTier.Frontier => "FRONTIER",
        RouteTier.DeadLines => "DEAD LINES",
        _ => "DEEP TERRITORY",
    };

    static string RouteSpec(RouteTier tier, ulong seed) => $"{char.ToLowerInvariant(tier.ToString()[0])}{tier.ToString()[1..]}:{seed}";

    static readonly Vector4 Ink = new(0.88f, 0.84f, 0.74f, 1);
    static readonly Vector4 Dim = new(0.60f, 0.58f, 0.53f, 1);
    static readonly Vector4 Faint = new(0.40f, 0.39f, 0.36f, 1);
    static readonly Vector4 Amber = new(1.00f, 0.70f, 0.30f, 1);
    /// <summary>The night behind the in-night menu (note 292): dimmed, not hidden.</summary>
    static readonly Vector4 Veil = new(0.02f, 0.02f, 0.03f, 0.62f);

    /// <summary>Draws the screen in the frame's own pixels, over whatever the frame shows behind it.</summary>
    public void Draw(Overlay o, int width, int height)
    {
        _hits.Clear();
        float x = 20, y = 14;
        if (Night is null)
        {
            o.Clear();
            // The title on a station's nameboard (UiStyle), the edition's tag hung under its end.
            float board = UiStyle.Nameboard(o, x - 6, y, "DARK TERRITORY", 3);
            if (_edition.Tag is { Length: > 0 } tag)
                o.Text(x + o.Font.Measure("DARK TERRITORY", 3) + 24, y + board - 9, tag, Amber);
            y += board + 8;
        }
        else
        {
            // In a night (note 292): over the HUD as it's drawn, the night dimmed behind the list, and no nameboard (it's
            // the game you're in). The night goes on in the dark behind it: Lethal Company's quick menu, not a pause.
            o.Rect(0, 0, width, height, Veil);
            y += 10;
        }
        string? heading = Screen switch
        {
            Screen.Night => "THE NIGHT GOES ON",
            Screen.Leave => Night is { Hosting: true, Others: > 0 } ? "END THE NIGHT?" : "LEAVE THE NIGHT?",
            Screen.Slots => "CAMPAIGN",
            Screen.DeleteCrew when Open is { } s => $"DELETE {CrewName(s).ToUpperInvariant()}?",
            Screen.Crashed => "DARK TERRITORY STOPPED LAST TIME",
            Screen.Consent => PlaytestNotice?.Question ?? "PLAYTEST DATA",
            Screen.PlaytestData => "PLAYTEST DATA",
            Screen.Fortress or Screen.Upgrades or Screen.Stores when Open is { } s =>
                $"{CrewName(s).ToUpperInvariant()}{(s.Town is { } town ? $" IN {town.Name.ToUpperInvariant()}" : "")}: {s.Cars} CARS, {s.Scrip:0} SCRIP, NIGHT {s.Runs + 1}, {Name(Campaign.TierFor(_campaign, s.Cars))}",
            Screen.Upgrades => "UPGRADES",
            // Note 687: who's selling, and what the crew has to spend.
            Screen.Smithy when Smithy is { } shop => $"{shop.Smith.ToUpperInvariant()}, BLACKSMITH: TRAIN UPGRADES. {shop.State.Scrip:0} SCRIP",
            Screen.QuickNight => "QUICK NIGHT",
            Screen.Join => "JOIN",
            Screen.Password when _locked is { } locked => $"{locked.Name.ToUpperInvariant()} IS PRIVATE",
            Screen.Host => "HOST",
            Screen.Settings => "SETTINGS",
            Screen.Paint => "PAINT YOUR MASK",
            Screen.Controls => "CONTROLS",
            Screen.Credits => "CREDITS",
            Screen.Mods => ModsOff ? "MODS: OFF, STARTED WITH --NO-MODS" : "MODS, IN THE ORDER THEY'RE LAID OVER THE GAME",
            Screen.Profile => $"PROFILE: {(Settings.PlayerName is { Length: > 0 } me ? me : DefaultPlayerName).ToUpperInvariant()}",
            _ => "A CO-OP NIGHT ON THE LAST RAILWAY",
        };
        // At a bigger TEXT SIZE (note 347) a long heading wraps rather than run off the frame.
        foreach (var row in UiStyle.Wrap(o, heading!, width - x - 8))
        {
            o.Text(x, y, row, Dim);
            y += 10;
        }
        if (Screen == Screen.Fortress && Open?.History.LastOrDefault() is { } last)
        {
            o.Text(x, y, Clip(o, $"LAST NIGHT: {last.Route.ToUpperInvariant()}, {last.End.ToString().ToUpperInvariant()}, {(last.Net >= 0 ? "+" : "")}{last.Net:0} SCRIP", width - x - 8), Faint);
            y += 10;
        }
        // (Wrapped, as the heading is, at a bigger TEXT SIZE: note 347.)
        string? under = Screen == Screen.Consent && PlaytestNotice is { } notice ? string.Join(" ", notice.Ask).ToUpperInvariant()
            : Screen == Screen.Upgrades ? "BOUGHT FROM THE BLACKSMITH, AT THE FORGE IN THE YARD"
            : Screen == Screen.Smithy && Smithy is { } selling
                ? !selling.CanBuy ? $"THE HOST PAYS: ASK {(selling.Payer ?? "THEM").ToUpperInvariant()}"
                    // Note 904: with nothing in reach, why, where every one of its rows can see it.
                    : Purse(selling.State, SmithyFree) is { } purse ? purse.Item.Detail!.ToUpperInvariant()
                    : "A SHARE OF THE NEXT CAR. FITTED BEFORE THE GATE"
            : null;
        if (under is not null)
            foreach (var row in UiStyle.Wrap(o, under, width - x - 8))
            {
                // The playtest question's reasons are read before answering (note 914): as bright as the heading.
                o.Text(x, y, row, Screen == Screen.Consent ? Dim : Faint);
                y += 10;
            }
        if (Screen == Screen.Stores)
        {
            o.Text(x, y, "STORES FOR TONIGHT: SPENT WITH THE NIGHT", Faint);
            y += 10;
        }
        y += 4;
        if (Screen == Screen.Credits)
        {
            DrawCredits(o, x, y, width, height);
            UiStyle.Keyed(o, width - 8 - UiStyle.MeasureKeyed(o, CreditHints), height - 13, CreditHints, Dim);
            return;
        }
        if (Screen == Screen.Profile)
            y = DrawProfile(o, x, y, width, height);
        var entries = Entries();
        var items = entries.Select(e => e.Item).ToList();
        float widest = items.Select(i => o.Font.Measure(i.Label)).DefaultIfEmpty(0).Max() + 20;
        // A value's row has its arrows at the plate's right, for the mouse (note 264): < and > step it, as left and right do.
        bool arrows = entries.Any(e => e.Adjust is not null);
        float plate = Math.Min(width - x, widest + 12 + (arrows ? 24 : 0));
        // A list longer than the screen (the controls) scrolls: the selection kept in view, or where the wheel put it.
        int rows = Math.Max(1, (int)((height - y - 44) / 10));
        int first = First(rows, items.Count);
        int shown = Math.Min(rows, items.Count - first);
        UiStyle.Plate(o, x - 8, y - 6, plate, shown * 10 + 10);
        // More above or below (note 351): a small arrow on the plate's edge, where "..." on a row read as part of its label.
        if (first > 0)
            Chevron(o, x - 8 + plate / 2, y - 7, down: false, Ink);
        if (first + shown < items.Count)
            Chevron(o, x - 8 + plate / 2, y + shown * 10 + 1, down: true, Ink);
        for (int i = first; i < first + shown; i++)
        {
            // A section's heading (note 386): dim, flush left, a rule out to the plate's edge; nothing to point at.
            if (items[i].Heading)
            {
                string title = Clip(o, items[i].Label, plate - 24);
                o.Text(x, y, title, Dim);
                float rule = x + o.Font.Measure(title) + 4;
                if (x - 8 + plate - 6 > rule)
                    o.Rect(rule, y + 3, x - 8 + plate - 6 - rule, 1, Faint);
                y += 10;
                continue;
            }
            bool on = i == Selected;
            _hits.Add((new MenuHit(i), x - 4, y - 1, plate - 8, 10));
            // The selection: a brass-lit bar under it, as a lamp on a lever frame's plate.
            if (on)
                o.Rect(x - 4, y - 1, plate - 8, 9, UiStyle.Lit with { W = 0.14f });
            var colour = !items[i].Enabled ? Faint : on ? Amber : Ink;
            // A label wider than the frame (a long contract at 150%, note 347) is cut short; the detail under the list says it whole.
            string label = Clip(o, (on ? "> " : "  ") + items[i].Label, plate - 8 - (arrows ? 28 : 0));
            o.Text(x, y, label, colour);
            if (entries[i].Adjust is not null && items[i].Enabled && Editing is null)
            {
                float ax = x - 8 + plate - 26;
                o.Text(ax, y, "<", on ? Amber : Dim);
                o.Text(ax + 12, y, ">", on ? Amber : Dim);
                // Each arrow's box a little bigger than its glyph; listed after the row, so the arrow wins over it.
                _hits.Add((new MenuHit(i, -1), ax - 3, y - 1, 10, 10));
                _hits.Add((new MenuHit(i, +1), ax + 9, y - 1, 10, 10));
            }
            y += 10;
        }
        y += 6;
        if (Selected < items.Count && items[Selected].Detail is { } detail)
            // Wrapped to the screen (note 323): a mod's description is its author's, up to Thunderstore's 250 characters.
            foreach (var line in UiStyle.Wrap(o, detail.ToUpperInvariant(), width - x - 8))
            {
                o.Text(x, y, line, Dim);
                y += 10;
            }
        if (Message is { } m)
            o.Text(x, y, m.ToUpperInvariant(), Amber);
        string hints = Capturing is not null ? "PRESS THE KEY   [ESC] KEEP IT"
            // Note 450: on the password page Enter joins.
            : Editing == TextField.JoinPassword ? "TYPE   [ENTER] JOIN   [ESC] DONE"
            : Editing == TextField.NightPassword ? "TYPE   [ENTER] LOCK   [ESC] KEEP THE OLD"
            : Editing is not null ? "TYPE   [ENTER] DONE   [ESC] DONE"
            // The in-night menu's own pages have nothing to change (note 292) but a listed run's MOOD (note 535); Escape on the
            // first is back to the night.
            : Screen == Screen.Night && Night?.Lobby is not null ? "[UP/DOWN] OR MOUSE   [ENTER] OR CLICK   [LEFT/RIGHT] CHANGE   [ESC] RESUME"
            : Screen == Screen.Night ? "[UP/DOWN] OR MOUSE   [ENTER] OR CLICK   [ESC] RESUME"
            // LEFT/RIGHT only where there's a value to change (note 293).
            : Headset ? arrows ? "[STICK UP/DOWN] CHOOSE   [TRIGGER]   [STICK LEFT/RIGHT] CHANGE   [B] BACK" : "[STICK UP/DOWN] CHOOSE   [TRIGGER]   [B] BACK"
            : arrows ? "[UP/DOWN] OR MOUSE   [ENTER] OR CLICK   [LEFT/RIGHT] CHANGE   [ESC] BACK" : "[UP/DOWN] OR MOUSE   [ENTER] OR CLICK   [ESC] BACK";
        // Too wide for a small canvas (TEXT SIZE, note 347): the mouse's words go first; the keys stay.
        if (UiStyle.MeasureKeyed(o, hints) > width - 16)
            hints = hints.Replace(" OR MOUSE", "").Replace(" OR CLICK", "");
        UiStyle.Keyed(o, width - 8 - UiStyle.MeasureKeyed(o, hints), height - 13, hints, Dim);
    }

    /// <summary>A four-pixel arrow centred on <paramref name="cx"/>, from <paramref name="y"/> down, pointing up or down.</summary>
    static void Chevron(Overlay o, float cx, float y, bool down, Vector4 colour)
    {
        for (int r = 0; r < 4; r++)
        {
            float half = down ? 3 - r : r;
            o.Rect(MathF.Round(cx - half), y + r, half * 2 + 1, 1, colour);
        }
    }

    /// <summary><paramref name="text"/> cut short with "..." to fit <paramref name="width"/>, or as it is if it fits.</summary>
    static string Clip(Overlay o, string text, float width)
    {
        if (o.Font.Measure(text) <= width)
            return text;
        int n = text.Length;
        while (n > 1 && o.Font.Measure(text[..n].TrimEnd(' ', ',') + "...") > width)
            n--;
        return text[..n].TrimEnd(' ', ',') + "...";
    }
}
