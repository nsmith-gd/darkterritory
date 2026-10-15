using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim;

/// <summary>What the yard cat or the dog is doing (note 745), in its body's <see cref="Body.Tipped"/>: every client draws and hears it.</summary>
public enum PetMood : sbyte { Idle, Walk, Sleep, Warn, Fetch }

// The yard cat and the dog (ARCHITECTURE §8 note 745; docs/design/upgrades.md §4, §7.4): bodies the host walks about the cars
// in their frames (World.StepPets), their mood in the body record. The cat wanders the car it's in and sleeps on the warm
// backhead while the fire's lit, and hisses at whatever's aboard its car or the next; the dog wanders, barks at whatever's
// aboard anywhere or coming at the train, and fetches a toy lying in its car to whoever's nearest.
public sealed partial class World
{
    sealed class PetPlan
    {
        public Double3 Target;
        public double Until;
        public int Carrying = -1;
    }

    // Host: each pet's errand, by its body's id.
    readonly SortedDictionary<int, PetPlan> _pets = [];

    /// <summary>The cat in the cab, the dog in the guard van (note 745), where they're fitted.</summary>
    void StockPets(int guard)
    {
        _pets.Clear();
        _launchedAt = null;
        if (Fitted("yardCat") && Train.Frames[0].Shape.Cab is { } cab)
            Bodies.SpawnCrate(Train, 0, new Double3(cab.Min.X + 0.6, cab.Min.Y, cab.Max.Z - 0.6), BodyKind.Cat);
        if (Fitted("dog") && guard >= 0 && Train.Frames[guard].Shape.Interior is { } room)
            Bodies.SpawnCrate(Train, guard, new Double3(-0.4, room.Min.Y + 0.1, room.Max.Z - 2.0), BodyKind.Dog);
    }

    /// <summary>Where a pet can walk in a car: its floor's box and height (a car's inside, or the engine's cab).</summary>
    static (Box Room, double Floor)? PetSpace(CarShape shape) =>
        shape.Interior is { } room ? (room, room.Min.Y + 0.1) : shape.Cab is { } cab ? (cab, cab.Min.Y) : null;

    /// <summary>The cat's spot on the warm backhead (the firebox's door, in the cab), in the engine's frame.</summary>
    static Double3? Backhead(CarShape engine) =>
        engine.Cab is { } cab && engine.Solids.FirstOrDefault(s => s.Part == PartKind.Firebox) is { Box: var fire } && fire.Max.Z > fire.Min.Z
            ? new Double3((fire.Min.X + fire.Max.X) / 2, cab.Min.Y, fire.Max.Z + 0.3) : null;

    /// <summary>
    /// What's aboard for a pet to warn of (note 745): a creature on or in a car (not a hazard, not dormant), for the cat on its
    /// own car or within <see cref="WhimsyTuning.CatWarnCars"/>; for the dog anywhere on the train, or out in the open within
    /// <see cref="WhimsyTuning.DogHears"/> m and coming.
    /// </summary>
    Enemy? Boarder(Body pet)
    {
        var t = WhimsyTuning;
        var at = Physics.Bodies.WorldCentre(pet, Train);
        Enemy? best = null;
        double nearest = double.MaxValue;
        foreach (var e in _enemies)
        {
            if (e.Gone || e.Hazard || e.Phase == SpinePhase.Dormant)
                continue;
            bool aboard = e.Attached >= 0 && e.Attached < Train.Frames.Count;
            bool warns = pet.Kind == BodyKind.Cat
                ? aboard && Math.Abs(e.Attached - pet.Parent) <= t.CatWarnCars
                : aboard || e.Exposed && e.Phase is SpinePhase.Telegraph or SpinePhase.Commit && (e.WorldPosition(Train) - at).Length <= t.DogHears;
            if (!warns)
                continue;
            double d = (e.WorldPosition(Train) - at).Length;
            if (d < nearest)
                (best, nearest) = (e, d);
        }
        return best;
    }

    /// <summary>
    /// Host, each tick after the bodies: the cat and the dog. Carried (the cat can be picked up), they're the carrier's. A
    /// boarder and they stop and warn of it, facing it; otherwise the cat sleeps on the backhead while the fire's lit (going
    /// car to car forward to it) and the dog fetches a toy lying in its car to the nearest crewmate; otherwise they wander.
    /// </summary>
    void StepPets(IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        if (!Authority)
            return;
        var t = WhimsyTuning;
        double dt = SimConstants.TickSeconds, now = Tick * dt;
        // A ball in the jaws of a hound that's gone (shot, or given up): it drops where it was.
        foreach (var ball in Bodies.All.Where(b => b.Kind == BodyKind.TennisBall && b.TakenBy >= 0))
            if (_enemies.All(e => e.Id != ball.TakenBy || e.Gone))
            {
                ball.TakenBy = -1;
                ball.Pbd.Wake();
            }
        foreach (var pet in Bodies.All.Where(b => b.Kind is BodyKind.Cat or BodyKind.Dog).ToList())
        {
            if (!_pets.TryGetValue(pet.Id, out var plan))
                _pets[pet.Id] = plan = new PetPlan { Target = pet.Centre, Until = now };
            if (pet.Lifted || pet.Parent < 0 || pet.Parent >= Train.Frames.Count || PetSpace(Train.Frames[pet.Parent].Shape) is not { } space)
            {
                pet.Tipped = (sbyte)PetMood.Idle;
                plan.Until = now;
                continue;
            }
            var frame = Train.Frames[pet.Parent];
            var feet = pet.Centre with { Y = space.Floor };
            double speed = pet.Kind == BodyKind.Cat ? t.CatSpeed : t.DogSpeed;
            // A boarder: stop, face it, warn.
            if (Boarder(pet) is { } boarder)
            {
                var toward = frame.ToLocal(boarder.WorldPosition(Train)) - feet;
                if (toward.Length > 0.01)
                    pet.Yaw = DMath.Atan2(-toward.X, -toward.Z);
                Drop(pet, plan, frame, feet);
                pet.Tipped = (sbyte)PetMood.Warn;
                Physics.Bodies.PlaceIn(pet, pet.Parent, feet + Double3.Up * pet.Pbd.Particles[0].Radius);
                continue;
            }
            PetMood mood;
            Double3 goal;
            if (pet.Kind == BodyKind.Cat && Train.Boiler.Firebox > 0 && Backhead(Train.Frames[0].Shape) is { } warm)
            {
                // Forward car by car to the cab, then to the backhead, and asleep there.
                goal = pet.Parent == 0 ? warm : new Double3(0, space.Floor, space.Room.Min.Z + 0.4);
                mood = pet.Parent == 0 && (feet - warm).Length < 0.15 ? PetMood.Sleep : PetMood.Walk;
                if (pet.Parent > 0 && (feet - goal).Length < 0.15)
                {
                    HopForward(pet);
                    continue;
                }
            }
            else if (pet.Kind == BodyKind.Dog && Fetching(pet, plan, crew, frame, feet, out var fetchTo))
            {
                goal = fetchTo;
                mood = PetMood.Fetch;
            }
            else
            {
                if (now >= plan.Until && (feet - plan.Target).Length < 0.15)
                {
                    // Somewhere else on the floor, a while from now: the same on every run of the night.
                    uint h = (uint)(Tick * 2654435761u) ^ (uint)(pet.Id * 40503);
                    double u = h % 1000 / 1000.0, v = h / 1000 % 1000 / 1000.0, w = h / 1000000 % 1000 / 1000.0;
                    var room = space.Room;
                    plan.Target = new Double3(room.Min.X + 0.3 + (room.Max.X - room.Min.X - 0.6) * u, space.Floor,
                        room.Min.Z + 0.4 + (room.Max.Z - room.Min.Z - 0.8) * v);
                    plan.Until = now + t.Idle[0] + (t.Idle[^1] - t.Idle[0]) * w;
                }
                goal = plan.Target;
                mood = (feet - goal).Length < 0.15 ? PetMood.Idle : PetMood.Walk;
            }
            var step = (goal - feet) with { Y = 0 };
            if (step.Length > 0.02)
            {
                pet.Yaw = DMath.Atan2(-step.X, -step.Z);
                feet += step * Math.Min(1, speed * dt / step.Length);
            }
            pet.Tipped = (sbyte)mood;
            Physics.Bodies.PlaceIn(pet, pet.Parent, feet + Double3.Up * pet.Pbd.Particles[0].Radius);
            if (plan.Carrying >= 0 && Bodies.All.FirstOrDefault(b => b.Id == plan.Carrying) is { } toy)
                Physics.Bodies.PlaceIn(toy, pet.Parent, Mouth(pet, feet));
        }
    }

    /// <summary>Where the dog's jaws are, in its car's frame: a toy's carried there.</summary>
    static Double3 Mouth(Body dog, Double3 feet) => feet + new Double3(-DMath.Sin(dog.Yaw), 0, -DMath.Cos(dog.Yaw)) * 0.38 + Double3.Up * 0.42;

    /// <summary>The cat through the door into the car ahead (note 745), at its back end, facing on.</summary>
    void HopForward(Body pet)
    {
        int ahead = pet.Parent - 1;
        if (ahead < 0 || PetSpace(Train.Frames[ahead].Shape) is not { } next)
            return;
        var room = next.Room;
        Physics.Bodies.PlaceIn(pet, ahead, new Double3(0, next.Floor + pet.Pbd.Particles[0].Radius, room.Max.Z - 0.4));
        pet.Yaw = 0;
    }

    /// <summary>
    /// The dog's fetch (note 745): a toy lying loose in its car, it goes for it; carrying one, it takes it to the nearest
    /// crewmate in the car and drops it at their feet. False when there's nothing to fetch or nobody to bring it to.
    /// </summary>
    bool Fetching(Body dog, PetPlan plan, IReadOnlyCollection<(int Id, PlayerState State)> crew, in CarFrame frame, Double3 feet, out Double3 goal)
    {
        var t = WhimsyTuning;
        goal = feet;
        if (plan.Carrying >= 0)
        {
            int car = dog.Parent;
            var near = crew.Where(c => c.State.Alive && c.State.Parent == car).OrderBy(c => (c.State.Position - feet).Length).ThenBy(c => c.Id).ToList();
            if (near.Count == 0 || Bodies.All.All(b => b.Id != plan.Carrying))
            {
                Drop(dog, plan, frame, feet);
                return false;
            }
            goal = near[0].State.Position with { Y = feet.Y };
            if ((goal - feet).Length <= t.Drop)
            {
                Drop(dog, plan, frame, feet);
                return false;
            }
            return true;
        }
        var toy = Bodies.All.Where(b => b.Kind == BodyKind.Toy && b.Parent == dog.Parent && !b.Lifted && b.TakenBy < 0 && !b.Stowed)
            .OrderBy(b => (b.Centre - feet).Length).ThenBy(b => b.Id).FirstOrDefault();
        if (toy is null || crew.All(c => !c.State.Alive || c.State.Parent != dog.Parent))
            return false;
        goal = toy.Centre with { Y = feet.Y };
        if ((goal - feet).Length <= t.Fetch)
            plan.Carrying = toy.Id;
        return true;
    }

    /// <summary>The dog lets go of what it's carrying, at its feet.</summary>
    void Drop(Body dog, PetPlan plan, in CarFrame frame, Double3 feet)
    {
        if (plan.Carrying >= 0 && Bodies.All.FirstOrDefault(b => b.Id == plan.Carrying) is { } toy)
            Physics.Bodies.PlaceIn(toy, dog.Parent, Mouth(dog, feet) with { Y = feet.Y + toy.Pbd.Particles[0].Radius });
        plan.Carrying = -1;
    }

    // Who had Use down at the launcher last tick, and when it last fired.
    readonly HashSet<int> _launchWas = [];
    uint? _launchedAt;

    /// <summary>
    /// The tennis-ball launcher (note 745): a Use press at it on the guard van's rear platform fires a ball off the back, up the
    /// line behind, at most every <see cref="WhimsyTuning.LaunchEvery"/> s. The Cinder Hounds running after the train go for it
    /// (CinderHound.Fetch), and one that catches the train up again drops it on the platform.
    /// </summary>
    void Launch(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool use = intent.Has(PlayerButtons.Use) && s.Alive && Fitted("tennisBalls")
            && CrewActions.NearestInteractable(s, Train, Hand) is ({ Kind: InteractableKind.Launcher }, var van) && van == s.Parent;
        bool pressed = use && !_launchWas.Contains(playerId);
        if (use)
            _launchWas.Add(playerId);
        else
            _launchWas.Remove(playerId);
        var t = WhimsyTuning;
        if (!Authority || !pressed || _launchedAt is { } last && (Tick - last) * SimConstants.TickSeconds < t.LaunchEvery
            || Train.Frames[s.Parent].Shape.Platform is not { } platform)
            return;
        _launchedAt = Tick;
        var frame = Train.Frames[s.Parent];
        var mouth = frame.ToWorld(CarShape.Launcher(platform) + new Double3(0, 1.1, 0.35));
        var ball = Bodies.SpawnItem(mouth, Train.Dynamics.RearDistance, BodyKind.TennisBall);
        ball.Claimed = true;
        var away = frame.DirToWorld(new Double3(0, 0.35, 1).Normalized) * t.LaunchSpeed;
        ball.Pbd.Particles[0].Position = mouth;
        ball.Pbd.Particles[0].SetVelocity(frame.Velocity + away, SimConstants.TickSeconds);
        ball.Pbd.Wake();
    }
}
