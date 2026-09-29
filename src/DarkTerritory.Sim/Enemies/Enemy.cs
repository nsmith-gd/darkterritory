using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

public enum EnemyKind : byte { Sleepers = 1, CinderHound = 2, Clinger = 3, Hollow = 4 }

/// <summary>Where on the train a threat comes from (GDD §21), and so which answer applies.</summary>
public enum PressureZone : byte { Forward, Rear, Flank, Interior, Structural }

/// <summary>What a creature senses (App. A.1): each enemy has exactly one primary trigger.</summary>
public enum Sense : byte { Sound, Light, Heat, Movement, Scent, Vibration, Sight, Absence }

/// <summary>The shared five-state spine every enemy runs (GDD App. A.1).</summary>
public enum SpinePhase : byte { Dormant, Alert, Telegraph, Commit, Punish, BreakOff, Gone }

/// <summary>A spine transition, logged for audio/visual cues on clients and for the fairness audit.</summary>
public readonly record struct EnemyEvent(uint Tick, int EnemyId, EnemyKind Kind, SpinePhase From, SpinePhase To, double SecondsInFrom);

/// <summary>
/// Base for every enemy. Owns the spine and enforces the fairness contract structurally: <see cref="Enter"/>
/// refuses to go to <see cref="SpinePhase.Commit"/> unless the enemy has been telegraphing for at least the
/// minimum reaction window. "No enemy in this game may punish a player who was given no window." (App. A.1)
/// </summary>
public abstract class Enemy
{
    protected Enemy(int id) => Id = id;

    public int Id { get; }
    public abstract EnemyKind Kind { get; }
    public abstract PressureZone Zone { get; }
    public abstract Sense Sense { get; }

    public SpinePhase Phase { get; private set; } = SpinePhase.Dormant;
    public double PhaseSeconds { get; private set; }
    public double Health { get; set; } = 1;
    public bool Gone => Phase == SpinePhase.Gone;

    /// <summary>Vehicle the enemy is on (car-local <see cref="Local"/>), or −1 if it's free on the line.</summary>
    public int Attached { get; set; } = -1;
    public Double3 Local { get; set; }
    /// <summary>Along-line position, lateral offset and height when free on the line.</summary>
    public double LineDistance { get; set; }
    public double Lateral { get; set; }
    public double Height { get; set; }
    /// <summary>Hit volume radius; 0 means it can't be shot (Clingers, Sleepers, the Hollow in the stack).</summary>
    public virtual double HitRadius => 0;
    /// <summary>
    /// Lies on the main line wherever the train is (Sleepers across the rail). Everything else off the train is
    /// placed along the engine's path: it's after the train, down a branch too.
    /// </summary>
    public virtual bool OnMainLine => false;

    /// <summary>Free per-kind values that are replicated (drill progress, pry progress, pack id).</summary>
    public double Extra { get; set; }
    public double Extra2 { get; set; }

    public Double3 WorldPosition(TrainOnLine train)
    {
        if (Attached >= 0)
            return train.Frames[Attached].ToWorld(Local);
        var t = OnMainLine ? train.Line.Sample(LineDistance) : train.Line.Sample(train.Dynamics.Path, LineDistance);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return t.Position + right * Lateral + Double3.Up * Height;
    }

    /// <summary>Advances the enemy one tick.</summary>
    public void Step(EnemyContext ctx)
    {
        PhaseSeconds += SimConstants.TickSeconds;
        Tick(ctx);
    }

    protected abstract void Tick(EnemyContext ctx);

    /// <summary>Moves the spine. Commit is only reachable from a telegraph at least the reaction window long.</summary>
    protected bool Enter(EnemyContext ctx, SpinePhase next)
    {
        if (next == Phase)
            return true;
        if (next == SpinePhase.Commit && (Phase != SpinePhase.Telegraph || PhaseSeconds + 1e-9 < ctx.Tuning.MinReactionSeconds))
            return false;
        if (next == SpinePhase.Punish && Phase != SpinePhase.Commit && Phase != SpinePhase.Punish)
            return false;
        ctx.Events.Add(new EnemyEvent(ctx.Tick, Id, Kind, Phase, next, PhaseSeconds));
        Phase = next;
        PhaseSeconds = 0;
        return true;
    }

    /// <summary>Takes damage from a round; returns true if that killed it.</summary>
    public virtual bool Hit(EnemyContext ctx, double damage)
    {
        if (HitRadius <= 0 || Gone)
            return false;
        Health -= damage;
        if (Health > 0)
            return false;
        Enter(ctx, SpinePhase.Gone);
        return true;
    }

    /// <summary>Copies replicated state onto a client-side stand-in.</summary>
    public void Restore(SpinePhase phase, double phaseSeconds, double health, int attached, Double3 local, double s, double lateral, double height, double extra, double extra2)
    {
        Extra2 = extra2;
        Phase = phase;
        PhaseSeconds = phaseSeconds;
        Health = health;
        Attached = attached;
        Local = local;
        LineDistance = s;
        Lateral = lateral;
        Height = height;
        Extra = extra;
    }
}
