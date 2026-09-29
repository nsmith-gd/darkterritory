using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim;

/// <summary>
/// Everything one night simulates beyond the players themselves: the train and its pieces, the Choir,
/// what can be shot, and what was fired this tick. The host steps it for everyone; a client steps the
/// same code for its own predicted player (GDD §33: shared simulation module).
/// </summary>
public sealed class World
{
    public World(TrainOnLine train, CombatTuning? combat = null)
    {
        Train = train;
        Combat = combat;
        Choir = ChoirState.Quiet;
        if (combat is not null)
            Guns.Arm(train, combat.Guns);
    }

    public TrainOnLine Train { get; }
    public CombatTuning? Combat { get; set; }
    public ChoirState Choir;
    /// <summary>Hit volumes for this tick (supplied by the enemy system).</summary>
    public List<HitTarget> Targets { get; } = new();
    /// <summary>Rounds fired this tick.</summary>
    public List<GunShot> Shots { get; } = new();
    public uint Tick { get; set; }

    /// <summary>One player's hands this tick: crew actions at interactables, and firing a gun they're manning.</summary>
    public void CrewAct(ref PlayerState s, in PlayerIntent intent, int playerId)
    {
        CrewActions.Apply(ref s, intent, Train, SimConstants.TickSeconds);
        if (Combat is { } c && Guns.TryFire(s, intent, Train, c.Guns, ref Choir, c.Choir, Targets, Tick, playerId) is { } shot)
            Shots.Add(shot);
    }

    /// <summary>Starts a tick: clears last tick's shots.</summary>
    public void BeginTick() => Shots.Clear();

    /// <summary>Advances the train and the world systems after everyone's crew actions.</summary>
    public void Step(in TrainControls controls)
    {
        Train.Step(SimConstants.TickSeconds, controls);
        if (Combat is { } c)
        {
            Guns.Step(Train);
            Choir.Step(c.Choir, SimConstants.TickSeconds);
        }
        Tick++;
    }
}
