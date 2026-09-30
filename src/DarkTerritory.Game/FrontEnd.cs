using System.Numerics;
using Ballast.Online;
using Ballast.Render;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

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
        public string? RouteFile { get; init; }
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
}

public enum Screen { Title, Slots, Fortress, Upgrades, QuickNight, Join, Settings }

/// <param name="Detail">A line about the selected item, under the list.</param>
public sealed record MenuItem(string Label, string? Detail = null, bool Enabled = true);

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
    /// <summary>The address typed so far on the join screen.</summary>
    public string Address { get; private set; } = "127.0.0.1";
    /// <summary>The join screen wants typed text (the app turns text input on).</summary>
    public bool WantsText => Screen == Screen.Join;
    /// <summary>Played in a headset (T36): the hints name the controllers' buttons, not the keys.</summary>
    public bool Headset { get; set; }

    public IReadOnlyList<MenuItem> Items => Entries().Select(e => e.Item).ToList();

    public void Up() => Move(-1);
    public void Down() => Move(+1);
    /// <summary>Changes the selected setting or value (tier, seed, cars, toggles).</summary>
    public void Left() => Adjust(-1);
    public void Right() => Adjust(+1);

    /// <summary>Back a screen (from the title: nothing).</summary>
    public void Back()
    {
        Show(Screen switch
        {
            Screen.Upgrades => Screen.Fortress,
            Screen.Fortress => Screen.Slots,
            Screen.Title => Screen.Title,
            _ => Screen.Title,
        });
    }

    /// <summary>Acts on the selected item: a new screen, a purchase, or something for the app to start.</summary>
    public Launch? Select()
    {
        var entries = Entries();
        if (entries.Count == 0)
            return null;
        var e = entries[Math.Clamp(Selected, 0, entries.Count - 1)];
        if (!e.Item.Enabled)
            return null;
        Message = null;
        return e.Select?.Invoke();
    }

    public void Type(string text)
    {
        if (!WantsText)
            return;
        foreach (char c in text)
            if ((char.IsLetterOrDigit(c) || c is '.' or ':' or '-') && Address.Length < 64)
                Address += c;
    }

    public void Erase()
    {
        if (WantsText && Address.Length > 0)
            Address = Address[..^1];
    }

    public void Show(Screen screen)
    {
        Screen = screen;
        Selected = 0;
        // Skip to the first item you can do something with.
        var entries = Entries();
        while (Selected < entries.Count - 1 && !entries[Selected].Item.Enabled)
            Selected++;
    }

    /// <summary>Back at the title after a quick night or a join, with the edition's word after a night (the demo's).</summary>
    public void NightOver()
    {
        Show(Screen.Title);
        Message = _edition.AfterNight is { Length: > 0 } after ? after : null;
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

    void Move(int by)
    {
        var entries = Entries();
        if (entries.Count == 0)
            return;
        // Over anything greyed out.
        for (int i = 0; i < entries.Count; i++)
        {
            Selected = (Selected + by + entries.Count) % entries.Count;
            if (entries[Selected].Item.Enabled)
                break;
        }
        Message = null;
    }

    void Adjust(int by)
    {
        var entries = Entries();
        if (Selected < entries.Count)
            entries[Selected].Adjust?.Invoke(by);
    }

    void Change(Settings settings)
    {
        Settings = settings;
        settings.Save(_settingsPath);
    }

    readonly record struct Entry(MenuItem Item, Func<Launch?>? Select = null, Action<int>? Adjust = null);

    List<Entry> Entries() => Screen switch
    {
        Screen.Title =>
        [
            .. _edition.Campaign ? [new Entry(new("CAMPAIGN", "Three slots. Take contracts, buy cars, go farther out."), () => { Show(Screen.Slots); return null; })] : (Entry[])[],
            new(new("QUICK NIGHT", _tiers.Length > 1 ? "Any tier, any seed. Alone, or hosted for friends." : "Any seed. Alone, or hosted for friends."), () => { Show(Screen.QuickNight); return null; }),
            new(new("JOIN A NIGHT", "By address. Steam invites join from the friends list."), () => { Show(Screen.Join); return null; }),
            new(new("SETTINGS"), () => { Show(Screen.Settings); return null; }),
            new(new("QUIT"), () => new Launch.Quit()),
        ],
        Screen.Slots => [.. _saves.List().Select(x => SlotEntry(x.Slot, x.State)), new(new("BACK"), Go(Screen.Title))],
        Screen.Fortress => FortressEntries(),
        Screen.Upgrades => UpgradeEntries(),
        Screen.QuickNight =>
        [
            new(new($"TIER: {Name(_tiers[_tier])}", _tiers.Length > 1 ? "Local, frontier, the dead lines, the deep territory: farther is darker." : "The full game goes farther out.", _tiers.Length > 1),
                null, by => _tier = (_tier + by + _tiers.Length) % _tiers.Length),
            new(new($"SEED: {_seed}", "The same seed is the same line for everyone."), null, by => _seed = by > 0 ? _seed + 1 : Math.Max(1UL, _seed - 1)),
            new(new($"CARS: {_cars}"), null, by => _cars = Math.Clamp(_cars + by, 3, MaxCars)),
            new(new("PLAY ALONE"), () => new Launch.Night(RouteSpec(_tiers[_tier], _seed), _cars, Host: false)),
            new(new("HOST FOR FRIENDS", "They join by your address, or from your Steam lobby."), () => new Launch.Night(RouteSpec(_tiers[_tier], _seed), _cars, Host: true)),
            new(new("BACK"), Go(Screen.Title)),
        ],
        Screen.Join =>
        [
            new(new($"ADDRESS: {Address}_", "Type it; the host's port if it isn't the usual one (host:port)."), () => Address.Length > 0 ? new Launch.Join(Address) : null),
            new(new("JOIN", null, Address.Length > 0), () => new Launch.Join(Address)),
            new(new("BACK"), Go(Screen.Title)),
        ],
        Screen.Settings =>
        [
            new(new($"SOUND: {(Settings.Mute ? "OFF" : "ON")}"), Toggle(s => s with { Mute = !s.Mute }), _ => Change(Settings with { Mute = !Settings.Mute })),
            new(new($"VOICE: {(Settings.PushToTalk ? "PUSH TO TALK (HOLD V)" : "OPEN MIC")}"), Toggle(s => s with { PushToTalk = !s.PushToTalk }), _ => Change(Settings with { PushToTalk = !Settings.PushToTalk })),
            new(new($"HUD: {(Settings.Hud ? "ON" : "OFF")}", "F1 in the game as well."), Toggle(s => s with { Hud = !s.Hud }), _ => Change(Settings with { Hud = !Settings.Hud })),
            new(new($"VR TURNING: {(Settings.VrTurn == VrTurn.Snap ? "SNAP" : "SMOOTH")}"), Toggle(s => s with { VrTurn = s.VrTurn == VrTurn.Snap ? VrTurn.Smooth : VrTurn.Snap }),
                _ => Change(Settings with { VrTurn = Settings.VrTurn == VrTurn.Snap ? VrTurn.Smooth : VrTurn.Snap })),
            new(new($"VR COMFORT VIGNETTE: {(Settings.VrVignette ? "ON" : "OFF")}"), Toggle(s => s with { VrVignette = !s.VrVignette }), _ => Change(Settings with { VrVignette = !Settings.VrVignette })),
            new(new($"MOUSE SPEED: {Settings.MouseSpeed:0.0}", "Left and right to change."), null, by => Change(Settings with { MouseSpeed = Math.Clamp(Math.Round(Settings.MouseSpeed + by * 0.1, 1), 0.2, 3) })),
            new(new("BACK"), Go(Screen.Title)),
        ],
        _ => [],
    };

    int MaxCars => _edition.MaxCars > 0 ? Math.Min(_edition.MaxCars, _campaign.MaxCars) : _campaign.MaxCars;

    Func<Launch?> Go(Screen screen) => () => { Show(screen); return null; };

    Func<Launch?> Toggle(Func<Settings, Settings> change) => () => { Change(change(Settings)); return null; };

    Entry SlotEntry(int slot, CampaignState? s)
    {
        if (s is null)
            return new(new($"SLOT {slot}: EMPTY", "Start a campaign: three cars on local work, and no scrip."), () =>
            {
                var fresh = Campaign.New(_campaign, slot, $"Crew {slot}", _newSeed());
                _saves.Save(fresh);
                ShowFortress(slot);
                return null;
            });
        string underway = s.Current is null ? "" : ", a night under way";
        return new(new($"SLOT {slot}: {s.Name.ToUpperInvariant()}", $"{s.Cars} cars, {s.Scrip:0} scrip, {s.Runs} nights{underway}"), () =>
        {
            ShowFortress(slot);
            return null;
        });
    }

    List<Entry> FortressEntries()
    {
        if (Open is not { } s)
            return [new(new("BACK"), Go(Screen.Slots))];
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
                list.Add(new(new($"TONIGHT: {Name(c.Tier)} {c.Seed}, {c.PerCar:0} A CAR", $"Pays on what arrives: {s.Cars} cars could bring in {c.PerCar * s.Cars:0}."),
                    () => new Launch.CampaignNight(s.Slot, contract, Resume: false, _host)));
            }
        }
        double carCost = Campaign.NextCarCost(_campaign, s);
        bool full = s.Cars >= _campaign.MaxCars;
        list.Add(new(new(full ? "BUY A CAR: THE CONSIST IS FULL" : $"BUY A CAR: {carCost:0} SCRIP", full ? null : $"Car {s.Cars + 1}. More cars carry more, and burn more, and need more hands.",
            !full && s.Current is null), () => Buy(Campaign.BuyCar(_campaign, s), $"A car bought: {s.Cars + 1} now.")));
        list.Add(new(new("UPGRADES", null, s.Current is null), () => { Show(Screen.Upgrades); return null; }));
        list.Add(new(new($"PLAY: {(_host ? "HOST FOR FRIENDS" : "ALONE")}", "Left and right to change."), () => { _host = !_host; return null; }, _ => _host = !_host));
        list.Add(new(new("BACK"), Go(Screen.Slots)));
        return list;
    }

    List<Entry> UpgradeEntries()
    {
        if (Open is not { } s)
            return [new(new("BACK"), Go(Screen.Fortress))];
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
        list.Add(new(new("BACK"), Go(Screen.Fortress)));
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
    static readonly Vector4 Panel = new(0.02f, 0.02f, 0.03f, 0.72f);

    /// <summary>Draws the screen in the frame's own pixels, over whatever the frame shows behind it.</summary>
    public void Draw(Overlay o, int width, int height)
    {
        o.Clear();
        float x = 20, y = 18;
        o.Text(x, y, "DARK TERRITORY", Amber, scale: 3);
        if (_edition.Tag is { Length: > 0 } tag)
            o.Text(x + o.Font.Measure("DARK TERRITORY", 3) + 8, y + 14, tag, Dim);
        y += 30;
        string? heading = Screen switch
        {
            Screen.Slots => "CAMPAIGN",
            Screen.Fortress or Screen.Upgrades when Open is { } s =>
                $"{s.Name.ToUpperInvariant()}: {s.Cars} CARS, {s.Scrip:0} SCRIP, NIGHT {s.Runs + 1}, {Name(Campaign.TierFor(_campaign, s.Cars))}",
            Screen.Upgrades => "UPGRADES",
            Screen.QuickNight => "QUICK NIGHT",
            Screen.Join => "JOIN A NIGHT",
            Screen.Settings => "SETTINGS",
            _ => "A CO-OP NIGHT ON THE LAST RAILWAY",
        };
        o.Text(x, y, heading!, Dim);
        y += 10;
        if (Screen == Screen.Fortress && Open?.History.LastOrDefault() is { } last)
        {
            o.Text(x, y, $"LAST NIGHT: {last.Route.ToUpperInvariant()}, {last.End.ToString().ToUpperInvariant()}, {(last.Net >= 0 ? "+" : "")}{last.Net:0} SCRIP", Faint);
            y += 10;
        }
        if (Screen == Screen.Upgrades)
        {
            o.Text(x, y, "UPGRADES COST A SHARE OF THE NEXT CAR", Faint);
            y += 10;
        }
        y += 4;
        var items = Items;
        float widest = items.Select(i => o.Font.Measure(i.Label)).DefaultIfEmpty(0).Max() + 20;
        o.Rect(x - 6, y - 4, Math.Min(width - x, widest + 8), items.Count * 10 + 6, Panel);
        for (int i = 0; i < items.Count; i++)
        {
            bool on = i == Selected;
            var colour = !items[i].Enabled ? Faint : on ? Amber : Ink;
            o.Text(x, y, (on ? "> " : "  ") + items[i].Label, colour);
            y += 10;
        }
        y += 6;
        if (Selected < items.Count && items[Selected].Detail is { } detail)
        {
            o.Text(x, y, detail.ToUpperInvariant(), Dim);
            y += 10;
        }
        if (Message is { } m)
            o.Text(x, y, m.ToUpperInvariant(), Amber);
        o.TextRight(width - 8, height - 12, WantsText ? "TYPE   ENTER JOIN   ESC BACK"
            : Headset ? "STICK UP/DOWN CHOOSE   TRIGGER   STICK LEFT/RIGHT CHANGE   B BACK"
            : "UP/DOWN CHOOSE   ENTER   LEFT/RIGHT CHANGE   ESC BACK", Faint);
    }
}
