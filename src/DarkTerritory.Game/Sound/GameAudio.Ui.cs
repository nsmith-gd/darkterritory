using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The interface sounds' names: the audio checklist's ui-* lines (tools/audio/cues.py), each a flat tier-4 sound definition
/// (tools/audio/install.py). One place for them, so the front end, the HUD's sounds and whatever draws the dead phase next
/// name the same ones.
/// </summary>
public static class UiCue
{
    // ui-menus: the front end (FrontEnd.Cue) and the title's loop under it (App/Program.cs).
    public const string Move = "ui-menus.move";
    public const string Select = "ui-menus.select";
    public const string Back = "ui-menus.back";
    public const string Title = "ui-menus.title";
    public const string EndCard = "ui-menus.end-card";
    // ui-prompts: a hold-to-interact action (GameAudio.Interface).
    public const string Hold = "ui-prompts.hold";
    public const string Complete = "ui-prompts.complete";
    public const string Cancel = "ui-prompts.cancel";
    // ui-run-end: the incident report (GDD App. D.12).
    public const string Report = "ui-run-end.report";
    public const string Tally = "ui-run-end.tally";
    /// <summary>A commendation awarded (D.12). Nothing awards one yet: there's no commendation in the game to hook.</summary>
    public const string Commendation = "ui-run-end.commendation";
    // ui-dead-phase (GDD App. D.10).
    public const string Queue = "ui-dead-phase.queue";
    /// <summary>A creature vote locked in (D.11). There's no vote in the game yet to hook.</summary>
    public const string Vote = "ui-dead-phase.vote";
    /// <summary>A bookmark taken (D.12). There's no bookmark in the game yet to hook.</summary>
    public const string Bookmark = "ui-dead-phase.bookmark";
}

/// <summary>
/// The interface sounds that follow the night, flat (not placed in the world), for this machine's player only: the hold
/// they're working and how it ends, the night's report coming up, and the respawn queue moving while they're dead. Read off
/// replicated state once per sim tick (<see cref="Interface"/>), like everything else here. The menus' own sounds come in
/// through <see cref="FrontEnd.Cue"/> to <see cref="Ui"/>, and the title's loop through <see cref="UiLoop"/>.
/// </summary>
public sealed partial class GameAudio
{
    readonly Dictionary<string, SoundInstance> _uiLoops = new();
    readonly Dictionary<int, int> _occupants = new();
    readonly Queue<double> _tallies = new();
    World? _uiWorld;
    double _uiTime;
    Held? _hold;
    double _holdMark;
    bool _holdDone, _over;

    // The report's lines tally in one at a time after it comes up (GDD App. D.12): when the first does, and the gap between.
    const double TallyFirst = 0.6, TallyEvery = 0.35;

    /// <summary>
    /// Keeps an interface loop (flat) on or off by name: started if <paramref name="on"/> and it isn't playing, stopped if
    /// not. The title's sound under the front end (App/Program.cs), a hold's while it's held. Returns it while it plays, so
    /// its params can be driven; null off, or not installed.
    /// </summary>
    public SoundInstance? UiLoop(string name, bool on)
    {
        _uiLoops.TryGetValue(name, out var v);
        if (!on)
        {
            v?.Stop();
            _uiLoops.Remove(name);
            return null;
        }
        if (v is { Finished: false })
            return v;
        if (!HasCue(name) || Mixer.Play(name) is not { } started)
            return null;
        return _uiLoops[name] = started;
    }

    /// <summary>
    /// This machine's player's interface sounds, once per sim tick after <see cref="Update"/>: ui-prompts (the hold they're
    /// working: its loop, quicker as it gets there, then done or let go), ui-run-end (the report coming up, its lines
    /// tallying in) and ui-dead-phase (the respawn queue moving, while they're dead).
    /// </summary>
    /// <param name="me">Their own state (<c>IPlaySession.Player</c>, not whoever they're watching); <paramref name="self"/>, their id.</param>
    public void Interface(World world, in PlayerState me, int self)
    {
        _uiTime += SimConstants.TickSeconds;
        if (!ReferenceEquals(world, _uiWorld))
        {
            // A new night (or a first look at this one): what's already so when it starts isn't news.
            InterfaceEnd();
            _uiWorld = world;
            _over = world.Run?.Over == true;
        }
        Prompts(world, me);
        NightOver(world);
        DeadPhase(world, me);
    }

    /// <summary>The night's been left: a hold's loop stops, and the report's tallies still to come won't.</summary>
    void EndNightUi()
    {
        InterfaceEnd();
        _uiLoops.Clear();
    }

    public void InterfaceEnd()
    {
        UiLoop(UiCue.Hold, false);
        _hold = null;
        _holdDone = false;
        _tallies.Clear();
        _occupants.Clear();
        _uiWorld = null;
    }

    enum HoldKind : byte { Reload, Repair, Handbrake, Hatch, Uncouple, Breach, Restart, Rig }

    /// <param name="Id">What it's at: the gun's or the car's vehicle, the Holdout, the facility, the crane.</param>
    /// <param name="Fraction">How far along: 1 is done.</param>
    readonly record struct Held(HoldKind Kind, int Id, double Fraction);

    void Prompts(World world, in PlayerState me)
    {
        var now = Holding(world, me);
        if (_hold is { } was && (now is not { } n || n.Kind != was.Kind || n.Id != was.Id))
        {
            // It's over: done if what it does got done (its mark moved), let go too soon if not.
            if (!_holdDone)
                Ui(Mark(world, was) > _holdMark ? UiCue.Complete : UiCue.Cancel);
            UiLoop(UiCue.Hold, false);
            _hold = null;
            _holdDone = false;
        }
        if (now is not { } h)
            return;
        if (_hold is null)
            _holdMark = Mark(world, h);
        _hold = h;
        if (_holdDone)
            return;
        if (h.Fraction >= 1)
        {
            // The holds whose count goes on past done while it's held (a handbrake wound, a hatch, the coupling cut): done
            // the tick it gets there, and letting go after is nothing.
            Ui(UiCue.Complete);
            UiLoop(UiCue.Hold, false);
            _holdDone = true;
            return;
        }
        // The definition's playback rate follows progress (tools/audio/install.py): it hurries as it gets there.
        UiLoop(UiCue.Hold, true)?.Params.Set("progress", h.Fraction);
    }

    /// <summary>
    /// The hold the player's working, as the HUD prompts it ("[E] HOLD: ..." with an end to it), or null: a step of the
    /// cannon's reload (App. C.3), mending the boiler (T109), a handbrake, a roof hatch (T99), cutting the coupling (T91),
    /// breaking a Holdout open (App. D.7), restarting a yard's generator (level-design D.2), rigging a casting (T48). Not the
    /// holds that only go on while held (the vent, sand, a crank, the chute lever), nor the shovel (every shovelful is the
    /// crew-shovel sounds'), nor a switch lever (its progress is the host's alone, SwitchStands.Progress).
    /// </summary>
    static Held? Holding(World world, in PlayerState me)
    {
        if (!me.Alive)
            return null;
        var train = world.Train;
        if (world.Combat is { } combat && Guns.MannedGun(me, train, combat.Guns) is { } g
            && train.Vehicles[g].Gun is { ReloadNeeded: > 0, ReloadProgress: > 0 } gun)
            return new(HoldKind.Reload, g, gun.ReloadProgress / combat.Guns.ReloadStepSeconds);
        if (me.ActionProgress > 0 && me.Parent != PlayerState.World)
        {
            // What CrewActions.Apply counts ActionProgress for, by what's in reach (the HUD's Prompt reads it the same way).
            var near = CrewActions.NearestInteractable(me, train, world.Hand);
            var couplings = train.Dynamics.Tuning.Couplings;
            if (me.Surface == Surface.Coupler && near?.Thing.Kind != InteractableKind.Door)
                return new(HoldKind.Uncouple, me.Parent,
                    me.ActionProgress / (train.CouplingUnderLoad(me.Parent) ? couplings.UncoupleUnderLoadSeconds : couplings.UncoupleSeconds));
            // A headset's hand at the coal or the firebox is shovelling (CrewActions.ShovelByHand).
            if (world.Hand is not null && me.Hand != default && PlayerMotor.InCab(me, train)
                && near?.Thing.Kind is InteractableKind.Coal or InteractableKind.Firebox or null)
                return null;
            switch (near?.Thing.Kind)
            {
                case InteractableKind.Firebox when train.Boiler.Ruptured && Kit.Held(me) == Tool.Wrench && train.BoilerTuning is { } bt:
                    return new(HoldKind.Repair, 0, me.ActionProgress / bt.RepairSeconds);
                case InteractableKind.Handbrake:
                    return new(HoldKind.Handbrake, near.Value.Vehicle, me.ActionProgress / couplings.HandbrakeSeconds);
                case InteractableKind.Hatch:
                    return new(HoldKind.Hatch, near.Value.Vehicle, me.ActionProgress / (train.Dynamics.Tuning.Geometry.Interior?.DoorSeconds ?? 0.4));
            }
        }
        if (me.Parent != PlayerState.World)
            return null;
        var at = PlayerMotor.WorldPosition(me, train);
        if (world.Holdouts is { } holdouts)
            foreach (var h in holdouts.All)
                if (h.State == HoldoutState.Breaching && ((h.Door - at) with { Y = 0 }).Length <= holdouts.Tuning.BreachReach)
                    return new(HoldKind.Breach, h.Index, h.Progress / h.Breach(holdouts.Tuning).Seconds);
        if (world.Run is { CurrentSite: { } site } run)
        {
            if (site.Restart > 0 && run.PowerhouseInReach(me, train))
                return new(HoldKind.Restart, run.Facility, site.Restart / run.PowerTuning.RestartSeconds);
            var cranes = site.Cranes;
            for (int i = 0; i < cranes.Count; i++)
                if (cranes[i].Rigging > 0 && cranes[i].Riggable(at) is not null)
                    return new(HoldKind.Rig, i, cranes[i].Rigging);
        }
        return null;
    }

    /// <summary>
    /// What finishing a hold changes, for the holds whose progress starts over when they're done (so a drop to nothing is
    /// either): read as it starts and as it stops, a rise is done.
    /// </summary>
    static double Mark(World world, Held h) => h.Kind switch
    {
        HoldKind.Reload => h.Id < world.Train.Vehicles.Count ? -world.Train.Vehicles[h.Id].Gun.ReloadNeeded : 0,
        HoldKind.Repair => world.Train.Boiler.Ruptured ? 0 : 1,
        HoldKind.Breach => world.Holdouts?.All.ElementAtOrDefault(h.Id)?.State == HoldoutState.Freed ? 1 : 0,
        HoldKind.Restart => world.Run?.CurrentSite?.Power == PowerState.Live ? 1 : 0,
        HoldKind.Rig => world.Run?.CurrentSite?.Cranes.ElementAtOrDefault(h.Id)?.Castings.Count(c => c.State != CastingState.Stacked) ?? 0,
        _ => 0,
    };

    /// <summary>The night's end (replicated: the run's phase): the report comes up, then each line under its headline tallies in.</summary>
    void NightOver(World world)
    {
        bool over = world.Run?.Over == true;
        if (over && !_over)
        {
            Ui(UiCue.Report);
            // The lines as the HUD shows them (Hud.ReportLines). Only the host's world has the report itself.
            int lines = world.Run!.Report is { } r ? Hud.ReportLines(r).Count - 1 : 0;
            for (int i = 0; i < lines; i++)
                _tallies.Enqueue(_uiTime + TallyFirst + i * TallyEvery);
        }
        if (!over)
            _tallies.Clear();
        _over = over;
        while (_tallies.TryPeek(out double due) && due <= _uiTime)
        {
            _tallies.Dequeue();
            Ui(UiCue.Tally);
        }
    }

    /// <summary>
    /// The respawn queue (GDD App. D.6) is the host's, but its front is seen: a lit Holdout taking someone in is the queue
    /// moving (whoever was at its front is out of it, or you deferred and it's gone to the next). Heard while you're dead
    /// or waiting to be picked up.
    /// </summary>
    void DeadPhase(World world, in PlayerState me)
    {
        if (world.Holdouts is not { } holdouts)
            return;
        bool moved = false;
        foreach (var h in holdouts.All)
        {
            int now = h.Lit ? h.Occupant : -1;
            // First seen: as it is, not news.
            int was = _occupants.TryGetValue(h.Index, out int seen) ? seen : now;
            _occupants[h.Index] = now;
            moved |= now >= 0 && now != was;
        }
        if (moved && !me.Alive)
            Ui(UiCue.Queue);
    }
}
