using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Audit;

/// <summary>The content the audits run on (note 186): the Sim reads no files, so the CLI or a test hands it over.</summary>
public sealed record AuditContent(TrainTuning Train, PlayerTuning Player, BoilerTuning Boiler, CombatTuning Combat, EnemyTuning Enemies,
    Run.RunTuning Run, Net.BalanceTuning Balance);

/// <summary>
/// A host-side night on a long straight line for the audits (note 186): the train, the crew as player states, and each
/// tick everyone acts through intent, the world steps, damage lands and everyone moves, as the host does it. Nothing comes
/// on its own (<see cref="World.Insist"/> empty): what's under test is put there by the audit.
/// </summary>
public sealed class AuditNight
{
    public readonly World World;
    public readonly SortedDictionary<int, PlayerState> Crew = [];
    public readonly List<EnemyEvent> Events = [];
    public TrainControls Controls = new() { Reverser = 1 };
    /// <summary>Held at this speed every tick (the train itself isn't under test), or null to let it run.</summary>
    public double? Speed;
    readonly AuditContent _c;

    public AuditNight(AuditContent c, int cars, double speed, int crew, bool boiler = false)
    {
        _c = c;
        Speed = speed;
        var line = new RailLine(new LineDefinition("audit", [new TrackSegment(60_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(c.Train, cars, 1)), line, 2_000, boiler ? c.Boiler : null);
        train.Dynamics.Velocity = speed;
        World = new World(train, c.Combat);
        World.EnableEnemies(c.Enemies, route: null, 1, crew, authority: true);
        World.Insist = [];
        World.InsistAfter = 0;
    }

    public TrainOnLine Train => World.Train;
    public PlayerTuning P => _c.Player;
    public EnemyTuning E => _c.Enemies;

    public int Add(PlayerState s)
    {
        int id = Crew.Count == 0 ? 1 : Crew.Keys.Max() + 1;
        Crew[id] = s;
        return id;
    }

    public PlayerState Roof(int car, double z, double x = 0) =>
        PlayerMotor.SpawnOnRoof(Train, car, Math.Clamp(z, -Train.Frames[car].Shape.HalfLength + 0.6, Train.Frames[car].Shape.HalfLength - 0.6), P, x);

    /// <summary>On a car's floor, this far along its room (clamped inside it).</summary>
    public PlayerState Inside(int car, double z, double x = -0.45)
    {
        var room = Train.Frames[car].Shape.Interior!.Value;
        var s = PlayerMotor.SpawnOnRoof(Train, car, 0, P);
        s.Position = new Double3(x, room.Min.Y, Math.Clamp(z, room.Min.Z + 0.6, room.Max.Z - 0.6));
        s.Surface = Surface.Deck;
        return s;
    }

    /// <summary>On the ballast beside a car, this far out from its side and along it.</summary>
    public PlayerState Ground(int car, double outward, double along = 0)
    {
        var frame = Train.Frames[car];
        var at = frame.ToWorld(new Double3(frame.Shape.HalfWidth + outward, 0, along));
        return PlayerMotor.SpawnOnGround(at, Train.Line, Train.Dynamics.Distance - frame.Shape.HalfLength, P);
    }

    /// <summary>On the coupler plate in the gap behind a car.</summary>
    public PlayerState Gap(int car, double x = 0)
    {
        var s = PlayerMotor.SpawnOnRoof(Train, car, 0, P);
        s.Position = CrewSense.GapLocal(Train, car) with { X = x, Y = 0.9 };
        s.Surface = Surface.Coupler;
        return s;
    }

    public Double3 Where(int id) => PlayerMotor.WorldPosition(Crew[id], Train);

    /// <summary>Steps <paramref name="seconds"/>; <paramref name="intent"/> gives each crewmate's intent from their state.</summary>
    public void Run(double seconds, Func<int, PlayerState, PlayerIntent> intent, Func<bool>? until = null)
    {
        for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
        {
            if (until?.Invoke() == true)
                return;
            if (Speed is { } v && !World.Derailed)
                Train.Dynamics.Velocity = v;
            World.BeginTick();
            var intents = Crew.ToDictionary(c => c.Key, c => intent(c.Key, c.Value));
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                World.CrewAct(ref s, intents[id], id);
                Crew[id] = s;
            }
            World.Step(Controls);
            World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys.ToList());
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                PlayerMotor.Step(ref s, intents[id], Train, P, _c.Train, SimConstants.TickSeconds, applyLook: false);
                Crew[id] = s;
            }
            World.StepBodies([.. Crew.Select(c => (c.Key, c.Value))]);
            Events.AddRange(World.EnemyEvents);
        }
    }
}
