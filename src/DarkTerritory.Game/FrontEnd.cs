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
    }
    /// <summary>Someone else's night, at an address (host[:port]).</summary>
    public sealed record Join(string Address) : Launch;
    /// <summary>A friend's Steam lobby (an invite accepted, or "Join Game").</summary>
    public sealed record JoinLobby(LobbyId Lobby) : Launch;
    /// <summary>
    /// A campaign night (spec E, F): the slot, and the contract off its board; or, with <paramref name="Resume"/>, the
    /// night already under way, from its last autosave.
    /// </summary>
    public sealed record CampaignNight(int Slot, int Contract, bool Resume, bool Host) : Launch;
    public sealed record Quit : Launch;
    /// <summary>The in-night menu's INVITE (note 292): the platform's invite dialog, over the night.</summary>
    public sealed record Invite : Launch;
    /// <summary>The in-night menu's LEAVE, confirmed (note 292): out of the night and back to the menus.</summary>
    public sealed record Leave : Launch;
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
public sealed record NightMenu(bool Hosting = true, int Others = 0, bool Campaign = false, bool Invites = false, string? JoinAt = null, bool Over = false,
    bool Yard = false);

public enum Screen { Title, Slots, Fortress, Upgrades, QuickNight, Join, Settings, Controls, Host, Stores, Credits, Night, Leave, Profile, DeleteCrew, Mods }

/// <summary>A mod laid over the game (note 323), as the MODS screen lists it: the app's scan of what's installed.</summary>
public sealed record InstalledMod(string Name, string Version, string? Description);

/// <param name="Detail">A line about the selected item, under the list.</param>
public sealed record MenuItem(string Label, string? Detail = null, bool Enabled = true);

/// <summary>
/// The front end's text fields (note 264, the director's notes on build 1121: "when I go to name of lobby, it just starts
/// automatically typing"). None takes typing on being chosen: Enter or a click starts editing, Enter or Esc ends it.
/// </summary>
public enum TextField { LobbyName, Address, PlayerName, CrewName }

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
    bool _host;

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
    /// The MODS screen's (note 323; note 53's "not yet": "an in-game mods screen"): the mods installed, in the order they're
    /// laid over the game, and what couldn't be loaded and why. With none of either, the title has no MODS.
    /// </summary>
    public IReadOnlyList<InstalledMod> InstalledMods { get; set; } = [];
    public IReadOnlyList<string> ModProblems { get; set; } = [];
    /// <summary>Started with <c>--no-mods</c>: the mods are listed, and the base game is what's playing.</summary>
    public bool ModsOff { get; set; }
    /// <summary>
    /// The player's profile (GDD App. D.12, note 293): the commendations their crews have given them, for the PROFILE page.
    /// The app loads it at the start and again after each night.
    /// </summary>
    public PlayerProfile.Data Profile { get; set; } = new();
    /// <summary>Where the nights' bookmark stills are kept (note 203), said on the PROFILE page; null, nowhere to say.</summary>
    public string? StillsFolder { get; set; }
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
        // In a night (note 292): Escape on the menu's first page closes it, as Escape opened it; the rest back up to it.
        if (Night is not null && Screen == Screen.Night)
        {
            CloseNight();
            return;
        }
        Show(Screen switch
        {
            Screen.Upgrades or Screen.Stores or Screen.DeleteCrew => Screen.Fortress,
            Screen.Fortress => Screen.Slots,
            Screen.Controls => Screen.Settings,
            Screen.Title => Screen.Title,
            Screen.Leave or Screen.Settings when Night is not null => Screen.Night,
            _ => Screen.Title,
        });
    }

    /// <summary>Acts on the selected item: a new screen, a purchase, or something for the app to start.</summary>
    public Launch? Select()
    {
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
        TextField.Address => Address,
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
        if (Editing == TextField.CrewName && Open is { Name.Length: > 0 } crew)
        {
            SaveCrew(crew with { Name = crew.Name[..^1] });
            return;
        }
        if (Editing == TextField.Address && Address.Length > 0)
            Address = Address[..^1];
    }

    public void Show(Screen screen)
    {
        // The in-night menu's pages are only there over a night (note 292).
        if (screen is Screen.Night or Screen.Leave && Night is null)
            screen = Screen.Title;
        if (screen is Screen.DeleteCrew && Open is null)
            screen = Screen.Slots;
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

    /// <summary>Shuts the in-night menu (RESUME, or Escape on its first page): back to the night.</summary>
    public void CloseNight()
    {
        if (Night is null)
            return;
        Night = null;
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
    public void NightOver()
    {
        Show(Screen.Title);
        Message = _edition.AfterNight is { Length: > 0 } after ? after : null;
        // The demo's end card (T79): its wishlist line coming up.
        if (Message is not null)
            Cue?.Invoke(UiCue.EndCard);
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
            new(new(_bots == 0 ? "CREW: JUST YOU" : $"CREW: YOU AND {_bots} BOT{(_bots == 1 ? "" : "S")}",
                "Bots drive, stoke, man the rear gun and lend a hand. None, and it's all yours to do."), null, by => _bots = Math.Clamp(_bots + by, 0, MaxBots)),
    ];

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
            // T116 (the co-op games' way, Lethal Company's ship): the host opens a lobby, the yard, and waits there; friends
            // join it from the list, by invite or by address; the host drives out of the yard when everyone's in.
            // The user's playtest: "You should be able to host a run, not a night. The button should just say HOST."
            new(new("HOST", "Open a lobby in the yard for a run. Friends join; you drive out when everyone's in."), () => { Show(Screen.Host); return null; }),
            new(new("JOIN", "The public games, nearest first, a Steam invite, or an address."), () => { Show(Screen.Join); return null; }),
            new(new("SETTINGS"), () => { Show(Screen.Settings); return null; }),
            new(new("PROFILE", "What your crews have commended you for."), () => { Show(Screen.Profile); return null; }),
            .. InstalledMods.Count + ModProblems.Count > 0 ? [new Entry(new("MODS", ModsLine()), Go(Screen.Mods))] : (Entry[])[],
            new(new("CREDITS", "The music, and who played it."), () => { Show(Screen.Credits); return null; }),
            new(new("QUIT"), () => new Launch.Quit()),
        ],
        Screen.Slots => [.. _saves.List().Select(x => SlotEntry(x.Slot, x.State)), BackTo(Screen.Title)],
        Screen.Fortress => FortressEntries(),
        Screen.Upgrades => UpgradeEntries(),
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
            // The user's playtest: "I can join it if its public. If it's a private lobby its not listed."
            new(new($"VISIBILITY: {(Settings.PublicLobby ? "PUBLIC" : "PRIVATE")}", Settings.PublicLobby
                    ? "Listed: anyone on your network, or on Steam, finds it on their join screen."
                    : "Not listed: friends join by Steam invite, or type your address."),
                Toggle(s => s with { PublicLobby = !s.PublicLobby }), _ => Change(Settings with { PublicLobby = !Settings.PublicLobby })),
            new(new($"{NameLabel}{LobbyName}{(NamingLobby ? "_" : "")}", NamingLobby ? "Type the name; Enter or Esc when it's done." : "Enter to rename it: what the join screen calls it."),
                Field: TextField.LobbyName),
            new(new("OPEN THE LOBBY", "You wait in the yard with the train; you drive out when everyone's in."),
                () => new Launch.Night(RouteSpec(_tiers[_tier], _seed), _cars, Host: true) { Bots = _bots, Public = Settings.PublicLobby, LobbyName = LobbyName.Trim() is { Length: > 0 } named ? named : DefaultLobbyName }),
            BackTo(Screen.Title),
        ],
        Screen.Join =>
        [
            new(new(Row("LOBBY", "CREW", "TIER", "PING"), null, false)),
            .. Games.Select(g => new Entry(new(Row(g.Name.ToUpperInvariant(), g.Max > 0 ? $"{g.Aboard}/{g.Max}" : $"{g.Aboard}",
                    Enum.TryParse<RouteTier>(g.Tier, out var t) ? Name(t) : "-", g.PingMs is { } ms ? $"{ms:0} MS" : "--")
                    + (g.Protocol != Protocol ? "  OTHER VERSION" : g.Full ? "  FULL" : ""),
                    g.Where, g.Protocol == Protocol && !g.Full),
                () => g.Join)),
            .. Games.Count == 0 ? [new Entry(new("  NO PUBLIC GAMES YET", "When someone opens a public lobby, it shows here.", false))] : (Entry[])[],
            new(new("REFRESH", "Look again, and ping everyone afresh."), () => { _refresh = true; Message = "Looking..."; return null; }),
            new(new($"ADDRESS: {Address}{(Editing == TextField.Address ? "_" : "")}", Editing == TextField.Address
                    ? "Type it, and the host's port if it isn't the usual one (host:port); Enter or Esc when it's done."
                    : "A private game, or one far off: Enter to type its address."), Field: TextField.Address),
            new(new("JOIN", null, Address.Length > 0), () => new Launch.Join(Address)),
            BackTo(Screen.Title),
        ],
        Screen.Settings =>
        [
            // Note 267: the name the crew and the report know you by, typed here (empty: your Steam or system name). Not in a
            // night (note 292): the crew have it already, from when you joined.
            .. Night is not null ? (Entry[])[] : [new Entry(new($"PLAYER NAME: {(Editing == TextField.PlayerName ? Settings.PlayerName + "_" : Settings.PlayerName is { Length: > 0 } me ? me.ToUpperInvariant() : DefaultPlayerName.ToUpperInvariant())}",
                Editing == TextField.PlayerName ? "Type your name; Enter or Esc when it's done. Erased, it's your Steam or system name."
                    : "Enter to type the name the crew and the report know you by."), Field: TextField.PlayerName)],
            new(new($"SOUND: {(Settings.Mute ? "OFF" : "ON")}"), Toggle(s => s with { Mute = !s.Mute }), _ => Change(Settings with { Mute = !Settings.Mute })),
            new(new($"VOICE: {(Settings.PushToTalk ? $"PUSH TO TALK (HOLD {Controls.KeyLabel(Settings.KeyFor(Control.Talk))})" : "OPEN MIC")}"), Toggle(s => s with { PushToTalk = !s.PushToTalk }), _ => Change(Settings with { PushToTalk = !Settings.PushToTalk })),
            // The audio checklist's mix-settings: the volumes, the microphone and its level.
            Volume("MASTER VOLUME", "Everything you hear.", Settings.MasterVolume, (s, v) => s with { MasterVolume = v }),
            Volume("EFFECTS VOLUME", "The train, the world, the things in the dark, your own hands.", Settings.EffectsVolume, (s, v) => s with { EffectsVolume = v }),
            Volume("MUSIC VOLUME", "The drone under the night, and the opera when it's over.", Settings.MusicVolume, (s, v) => s with { MusicVolume = v }),
            Volume("VOICE VOLUME", "The crew, near and on the radio, the dead, and the yard.", Settings.VoiceVolume, (s, v) => s with { VoiceVolume = v }),
            new(new($"MICROPHONE: {(Settings.MicDevice is { Length: > 0 } mic ? mic.ToUpperInvariant() : "DEFAULT")}", "Left and right to change. From the next night."),
                Toggle(s => s with { MicDevice = NextMic(s.MicDevice, 1) }), by => Change(Settings with { MicDevice = NextMic(Settings.MicDevice, by) })),
            new(new($"MIC LEVEL: {Settings.MicLevel * 100:0}%", "Left and right to change: up if the crew can't hear you."), null,
                by => Change(Settings with { MicLevel = Math.Clamp(Math.Round(Settings.MicLevel + by * 0.1, 1), 0, 3) })),
            new(new($"HUD: {(Settings.Hud ? "ON" : "OFF")}", "F1 in the game as well."), Toggle(s => s with { Hud = !s.Hud }), _ => Change(Settings with { Hud = !Settings.Hud })),
            new(new($"CONTROL HINTS: {(Settings.ControlHints ? "ON" : "OFF")}", "The keys in the corner for what you're holding or driving."),
                Toggle(s => s with { ControlHints = !s.ControlHints }), _ => Change(Settings with { ControlHints = !Settings.ControlHints })),
            // Note 348: the HUD's colours that mean something, told apart without red against green.
            new(new($"COLOURS: {(Settings.Colours == HudColours.Colourblind ? "COLOURBLIND" : "STANDARD")}",
                Settings.Colours == HudColours.Colourblind ? "The HUD's good in blue, warnings in yellow, danger in red." : "The HUD's good in green, warnings in amber, danger in red."),
                Toggle(s => s with { Colours = s.Colours == HudColours.Colourblind ? HudColours.Standard : HudColours.Colourblind }),
                _ => Change(Settings with { Colours = Settings.Colours == HudColours.Colourblind ? HudColours.Standard : HudColours.Colourblind })),
            // Note 349: what's heard, named, and where.
            new(new($"CAPTIONS: {(Settings.Captions ? "ON" : "OFF")}", "The sounds worth hearing named as you hear them, and where they are."),
                Toggle(s => s with { Captions = !s.Captions }), _ => Change(Settings with { Captions = !Settings.Captions })),
            // Note 350: a new player's first nights.
            new(new($"FIRST NIGHTS: {(Settings.FirstNights ? "ON" : "OFF")}", "Tips while a night's built, and the controls in the yard for your first nights."),
                Toggle(s => s with { FirstNights = !s.FirstNights }), _ => Change(Settings with { FirstNights = !Settings.FirstNights })),
            // Note 347: the print, bigger; the HUD's and these menus' alike, at once.
            new(new($"TEXT SIZE: {Settings.TextScale * 100:0}%",
                "Left and right to change: the HUD's print and the menus', bigger."),
                Toggle(s => s with { TextSize = Settings.Cycle(Settings.TextSizes, s.TextSize, 1) }),
                by => Change(Settings with { TextSize = Settings.Cycle(Settings.TextSizes, Settings.TextSize, by) })),
            // The headset's comfort is set as a night starts: not in a night's menu (note 292), which is the window's.
            .. Night is not null ? (Entry[])[] :
            [
                new Entry(new($"VR TURNING: {(Settings.VrTurn == VrTurn.Snap ? "SNAP" : "SMOOTH")}"), Toggle(s => s with { VrTurn = s.VrTurn == VrTurn.Snap ? VrTurn.Smooth : VrTurn.Snap }),
                    _ => Change(Settings with { VrTurn = Settings.VrTurn == VrTurn.Snap ? VrTurn.Smooth : VrTurn.Snap })),
                new Entry(new($"VR COMFORT VIGNETTE: {(Settings.VrVignette ? "ON" : "OFF")}"), Toggle(s => s with { VrVignette = !s.VrVignette }), _ => Change(Settings with { VrVignette = !Settings.VrVignette })),
            ],
            new(new($"MOUSE SPEED: {Settings.MouseSpeed:0.0}", "Left and right to change."), null, by => Change(Settings with { MouseSpeed = Math.Clamp(Math.Round(Settings.MouseSpeed + by * 0.1, 1), 0.2, 3) })),
            OutfitEntry(Night is null ? "Left and right to change: what the crew see you in." : "Tried on in the yard; past the gate, from the next night."),
            // Note 297: comfort.
            new(new($"INVERT MOUSE: {(Settings.InvertMouse ? "ON" : "OFF")}", "On, pushing the mouse away looks down."), Toggle(s => s with { InvertMouse = !s.InvertMouse }),
                _ => Change(Settings with { InvertMouse = !Settings.InvertMouse })),
            new(new($"FIELD OF VIEW: {Settings.EyeFov:0}", "Left and right to change: degrees, top to bottom. Wider sees more, and costs the GPU more."),
                Toggle(s => s with { FieldOfView = Settings.Cycle(Settings.FieldsOfView, s.EyeFov, 1) }),
                by => Change(Settings with { FieldOfView = Math.Clamp(Settings.EyeFov + by * 5, Settings.FieldsOfView[0], Settings.FieldsOfView[^1]) })),
            new(new($"CAMERA SHAKE: {(Settings.CameraShake <= 0 ? "OFF" : $"{Settings.CameraShake * 100:0}%")}", "The boiler's shake and a straining car's judder, in your eyes."),
                Toggle(s => s with { CameraShake = Settings.Cycle(Settings.CameraShakes, s.CameraShake, 1) }),
                by => Change(Settings with { CameraShake = Math.Clamp(Math.Round(Settings.CameraShake + by * 0.25, 2), 0, 1) })),
            // T83: the display.
            new(new($"DISPLAY: {(Settings.Fullscreen ? "FULLSCREEN" : "WINDOWED")}"), Toggle(s => s with { Fullscreen = !s.Fullscreen }), _ => Change(Settings with { Fullscreen = !Settings.Fullscreen })),
            new(new($"RESOLUTION: {Settings.Resolution}", "Left and right to change."), Toggle(s => s with { Resolution = Settings.Cycle(Settings.Resolutions, s.Resolution, 1) }),
                by => Change(Settings with { Resolution = Settings.Cycle(Settings.Resolutions, Settings.Resolution, by) })),
            new(new($"RENDER SCALE: {Settings.RenderScale * 100:0}%", "Draws the scene smaller and scales it up: faster, softer."), Toggle(s => s with { RenderScale = Settings.Cycle(Settings.RenderScales, s.RenderScale, 1) }),
                by => Change(Settings with { RenderScale = Settings.Cycle(Settings.RenderScales, Settings.RenderScale, by) })),
            new(new($"VSYNC: {(Settings.VSync ? "ON" : "OFF")}"), Toggle(s => s with { VSync = !s.VSync }), _ => Change(Settings with { VSync = !Settings.VSync })),
            new(new("CONTROLS", "Rebind the keys."), Go(Screen.Controls)),
            BackTo(Night is null ? Screen.Title : Screen.Night),
        ],
        // A row a track (its work and composer), its performers, licence and source drawn under it (DrawCredits).
        // The badges and their tally are drawn over the list (DrawProfile): BACK is all there is to choose.
        Screen.Profile => [BackTo(Screen.Title)],
        Screen.Night when Night is { } n => NightEntries(n),
        Screen.Leave when Night is { } n => LeaveEntries(n, Go(Screen.Night)),
        Screen.DeleteCrew when Open is { } s => DeleteEntries(s),
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
            .. Music.Select(t => new Entry(new($"{t.Work} - {t.Composer}, {t.Year}", CreditLine(t)))),
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

    /// <summary>The join screen's columns: the font's fixed-width, so spaces line them up under the header.</summary>
    static string Row(string name, string crew, string tier, string ping) =>
        $"{(name.Length > MaxLobbyName ? name[..MaxLobbyName] : name),-25}{crew,-7}{tier,-16}{ping,6}";

    int MaxCars => _edition.MaxCars > 0 ? Math.Min(_edition.MaxCars, _campaign.MaxCars) : _campaign.MaxCars;

    Func<Launch?> Go(Screen screen) => () => { Show(screen); return null; };

    Entry BackTo(Screen screen) => new(new("BACK"), Go(screen), Back: true);

    Func<Launch?> Toggle(Func<Settings, Settings> change) => () => { Change(change(Settings)); return null; };

    /// <summary>
    /// The crew's looks by name, for OUTFIT (note 298): look.json's crew colours, in order (the app sets them). Each look
    /// is its colour and the cap or helmet and scarf that come with it.
    /// </summary>
    public IReadOnlyList<string> OutfitNames { get; set; } = ["RED", "BLUE", "OCHRE", "TEAL", "GREEN", "VIOLET", "ORANGE", "WHITE"];

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
        var list = new List<Entry>();
        if (s.Current is { } tonight)
        {
            // Spec E: a night that was under way picks up from its last autosave (or starts again, if it had none).
            string from = s.Checkpoint is null ? "from the yard" : "from the last facility it left";
            list.Add(new(new($"CARRY ON: {Name(tonight.Tier)} {tonight.Seed}", $"The night that was under way, {from}."),
                () => new Launch.CampaignNight(s.Slot, -1, Resume: true, _host)));
        }
        else
        {
            var offers = Campaign.Offers(_campaign, _run, s);
            for (int i = 0; i < offers.Count; i++)
            {
                var c = offers[i];
                int contract = i;
                // GDD §9 "choose freight contracts" (note 182): each names the freight the train leaves with, and pays a car by it.
                string cargo = Cargoes.Name(c.Cargo).ToUpperInvariant();
                list.Add(new(new($"TONIGHT: {Name(c.Tier)} {c.Seed}, {cargo}, {c.PerCar:0} A CAR", $"{Carrying(c.Cargo)} Up to {c.PerCar * s.Cars:0} on {s.Cars} cars."),
                    () => new Launch.CampaignNight(s.Slot, contract, Resume: false, _host)));
            }
        }
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
        list.Add(new(new("UPGRADES", null, s.Current is null), () => { Show(Screen.Upgrades); return null; }));
        list.Add(new(new($"PLAY: {(_host ? "HOST FOR FRIENDS" : "ALONE")}", "Left and right to change."), () => { _host = !_host; return null; }, _ => _host = !_host));
        // Note 320 (note 33's "not yet": renaming a crew or deleting a slot was `dt campaign`'s alone).
        bool naming = Editing == TextField.CrewName;
        list.Add(new(new($"{NameLabel}{(naming ? s.Name : CrewName(s)).ToUpperInvariant()}{(naming ? "_" : "")}",
            naming ? "Type the name; Enter or Esc when it's done." : "Enter to rename the crew."), Field: TextField.CrewName));
        list.Add(new(new("DELETE THIS CREW", $"Slot {s.Slot} emptied for a new crew. It asks first."), Go(Screen.DeleteCrew)));
        list.Add(BackTo(Screen.Slots));
        return list;
    }

    List<Entry> UpgradeEntries()
    {
        if (Open is not { } s)
            return [BackTo(Screen.Fortress)];
        var list = new List<Entry>();
        foreach (var u in _campaign.Upgrades)
        {
            bool owned = s.Upgrades.Contains(u.Id);
            double cost = Campaign.UpgradeCost(_campaign, s, u);
            // The shop says which upgrades the night doesn't model yet (ARCHITECTURE §8 note 29).
            string detail = $"{(u.Size == UpgradeSize.Major ? "Major" : "Small")}{(u.Effect.Count == 0 ? ", not modelled yet" : "")}.";
            list.Add(new(new(owned ? $"{u.Name.ToUpperInvariant()}: OWNED" : $"{u.Name.ToUpperInvariant()}: {cost:0}", detail, !owned),
                () => Buy(Campaign.BuyUpgrade(_campaign, s, u.Id), $"{u.Name} bought.")));
        }
        list.Add(BackTo(Screen.Fortress));
        return list;
    }

    /// <summary>What a contract's freight does to the night (GDD §19, App. B.9), said on the board.</summary>
    static string Carrying(CargoKind cargo) => cargo switch
    {
        CargoKind.Comet => "Comet material: best pay; everything wants it.",
        CargoKind.Ammunition => "Powder and shot: a fire in its car can blow.",
        CargoKind.Chemicals => "Chemicals: fires spread; no cannon beside it.",
        CargoKind.Medicine => "Medicine: rough couplings and brakes spoil it.",
        CargoKind.Coal => "Coal: it burns, and fire runs down the train.",
        CargoKind.Timber => "Timber: it burns, and fire runs down the train.",
        CargoKind.Livestock => "Livestock: loud all night; hounds smell it.",
        CargoKind.Food => "Food: the scavengers come for it.",
        CargoKind.Heavy => "Machine parts: heavy, inert, safe.",
        _ => "Goods.",
    };

    static string StoresLine(Stores st) => st.Any
        ? $"Bought for tonight: {st.Powder} powder, {st.Lamps} lamps, {st.Extinguishers} extinguishers."
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

    /// <summary>A track's performers, licence and where it came from: a recording's Commons page, or the script that made it.</summary>
    static string CreditLine(MusicTrack t) =>
        $"{t.Performers}. {t.Licence}. {(t.Recorded ? t.Source.Replace("https://", "") : "Made by " + Path.GetFileName(t.Source))}";

    /// <summary>
    /// The credits (E.6: CC0 asks for none, the screen lists every performer anyway): each track two lines, its work,
    /// composer and year, then its performers, licence and source, scrolled to keep the selection in view.
    /// </summary>
    void DrawCredits(Overlay o, float x, float y, int width, int height)
    {
        // Wrapped at a bigger TEXT SIZE (note 347).
        foreach (var row in UiStyle.Wrap(o, "COMPOSITIONS IN THE PUBLIC DOMAIN. RECORDINGS DEDICATED CC0 1.0.", width - x - 8))
        {
            o.Text(x, y, row, Faint);
            y += 10;
        }
        y += 6;
        var items = Items;
        // Room under the list for the selected track's line in full, over two rows.
        int rows = Math.Max(1, (int)((height - y - 44) / 20));
        int first = First(rows, items.Count);
        int shown = Math.Min(rows, items.Count - first);
        float w = width - x - 8;
        int chars = (int)((w - 8) / o.Font.Advance);
        UiStyle.Plate(o, x - 8, y - 6, w + 4, shown * 20 + 6);
        if (Music.Count == 0)
            o.Text(x, y + shown * 20 - 4, "NO MUSIC IN THIS BUILD.", Faint);
        for (int i = first; i < first + shown; i++)
        {
            bool on = i == Selected;
            _hits.Add((new MenuHit(i), x - 4, y - 1, w - 4, 19));
            if (on)
                o.Rect(x - 4, y - 1, w - 4, items[i].Detail is null ? 9 : 19, UiStyle.Lit with { W = 0.14f });
            o.Text(x, y, Fit((on ? "> " : "  ") + items[i].Label, chars), on ? Amber : Ink);
            if (items[i].Detail is { } line)
                o.Text(x, y + 9, Fit("    " + line, chars), Dim);
            y += 20;
        }
        // The selected track's performers, licence and source, whole (a long Commons title or ensemble name is cut above).
        if (Selected < items.Count && items[Selected].Detail is { } full && full.Length + 4 > chars)
        {
            y += 6;
            string text = full.ToUpperInvariant();
            o.Text(x, y, text[..Math.Min(chars, text.Length)], Ink);
            if (text.Length > chars)
                o.Text(x, y + 9, Fit(text[chars..], chars), Ink);
        }
    }

    /// <summary>The PROFILE page's rows (note 293): each of D.12's starter set, in its order, with the times it's been given.</summary>
    public IReadOnlyList<(UiStyle.Commendation Badge, int Given)> Tally =>
        [.. Enum.GetValues<UiStyle.Commendation>().Select(c => (c, Profile.Commendations.GetValueOrDefault(Sim.Run.Commendations.StarterSet[(int)c])))];

    /// <summary>The line over the PROFILE page's badges: how many in all, or how they're come by.</summary>
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
            Screen.Fortress or Screen.Upgrades or Screen.Stores when Open is { } s =>
                $"{CrewName(s).ToUpperInvariant()}: {s.Cars} CARS, {s.Scrip:0} SCRIP, NIGHT {s.Runs + 1}, {Name(Campaign.TierFor(_campaign, s.Cars))}",
            Screen.Upgrades => "UPGRADES",
            Screen.QuickNight => "QUICK NIGHT",
            Screen.Join => "JOIN",
            Screen.Host => "HOST",
            Screen.Settings => "SETTINGS",
            Screen.Controls => "CONTROLS",
            Screen.Credits => "CREDITS: THE OPERA AT A DERAILMENT (GDD E.6)",
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
        if (Screen == Screen.Upgrades)
        {
            o.Text(x, y, "UPGRADES COST A SHARE OF THE NEXT CAR", Faint);
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
            : Editing is not null ? "TYPE   [ENTER] DONE   [ESC] DONE"
            // The in-night menu's own pages have nothing to change (note 292); Escape on the first is back to the night.
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
