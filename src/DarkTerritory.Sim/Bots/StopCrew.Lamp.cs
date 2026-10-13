using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// A hand lamp carried out to the wreck yard's dark heaps (queue #229, ARCHITECTURE §8 note 492; note 187's "not yet"; GDD
/// §18 wreck yard "unstable, unlit"). A heap's salvage is found only once a hand lamp is within <c>wreck.lampReach</c> of it, or
/// it's in the engine's headlamp (<see cref="Run.Run.Lit"/>), and the bots carried no lamps, so a bot crew loaded only what the
/// headlamp found. Now one crate hand (the first, or the shunter where no bot's a crate hand) takes a hand lamp and carries
/// it out toward the nearest dark heap, to the side its pieces come out (<see cref="Run.Run.PieceAt"/>), clear of its shift:
/// the heap's found as the lamp comes within reach, and the hand goes on to the next. The lamp it takes is the nearest lying
/// loose: on the ground at the stop, else in a car of the train behind the engine (the guard van's on its floor, or the top
/// one on a shelf of car 1's crew lockers), fetched in by that car's side door. It's out while it does
/// (<see cref="CrewCalls.Out"/>), so the driver waits for it. With every heap found, the lamp goes down where the hand stands
/// and the hands go to the salvage; and as the train leaves, it's taken up again and comes aboard, not left in the yard. All
/// through intent.
/// </summary>
public sealed partial class StopHand
{
    /// <summary>The lamp this hand is on its way for, or carrying out to a dark heap (−1: none).</summary>
    int _lamp = -1;
    /// <summary>The lamp this hand set down at the stop with nothing more for it to light, to take aboard as the train leaves (−1: none).</summary>
    int _lampDown = -1;
    /// <summary>This hand's own count of its ticks at a lamp in a car, for the taps (<see cref="KitRun.TapEvery"/>).</summary>
    uint _lampTick;

    /// <summary>m: how far out from a heap's middle, on the side its pieces come out, the lamp's set down (inside its reach, outside a shift).</summary>
    const double LampOut = 4.2;

    /// <summary>Whether this hand is out with a lamp for the wreck's dark heaps (for tests and traces).</summary>
    public bool Lighting => _lamp >= 0;

    /// <summary>
    /// This tick's intent for the lamp, or null when there's nothing of the lamp's for this hand to do (none of the heaps
    /// dark, it's not this hand's to bring, or there's no lamp to bring).
    /// </summary>
    PlayerIntent? WreckLamp(in PlayerState self, World world, StopPlan p, Body? held)
    {
        var train = world.Train;
        if (PlayerId is null || held is not null && held.Kind != BodyKind.Lamp)
            return EndLampRun();
        var dark = calls.Leaving || !LampHand() ? null : DarkHeap(world, p, PlayerMotor.WorldPosition(self, train));
        if (dark is null)
            return LampBack(self, world, p, held);
        // A lamp in hand: out to the dark heap (found as the lamp comes within its reach; down beside it, if it isn't yet).
        if (held is { Kind: BodyKind.Lamp })
        {
            _lamp = held.Id;
            calls.Out(member, true);
            if (self.Parent != PlayerState.World)
                return OutOfTheCar(self, world, p) ?? GetDown(self, train, p.Site.Side);
            var put = LampSpot(world, p, dark);
            var here = PlayerMotor.WorldPosition(self, train);
            if ((Flat(here) - Flat(put)).Length > 0.6)
            {
                Doing = "taking a lamp to the wreck";
                return WalkTo(self, train.Line, p.Spur.Index, put with { Y = here.Y }, null).Step;
            }
            Doing = "setting the lamp down";
            _lampDown = held.Id;
            return Press();
        }
        // Empty-handed: the nearest lamp lying loose, and up with it.
        var lamp = LooseLamp(world, p, PlayerMotor.WorldPosition(self, train));
        if (lamp is null)
            return EndLampRun();
        _lamp = lamp.Id;
        calls.Out(member, true);
        return lamp.Parent == PlayerState.World ? PickUp(self, world, p, lamp) : FromTheCar(self, world, p, lamp);
    }

    /// <summary>
    /// Nothing dark for a lamp to light: one in hand goes back aboard, into a car whose side door's open on the stop's side
    /// and down on its floor, where the next wreck's lamp is fetched from (<see cref="LooseLamp"/>). Not up a ladder in hand:
    /// the walker's grab is a Use press, which puts down what's carried. With no door open, it goes down where we stand, and
    /// comes up again for a car before the doors are shut (<see cref="LampAboard"/>). Null when there's no lamp in hand.
    /// </summary>
    PlayerIntent? LampBack(in PlayerState self, World world, StopPlan p, Body? held)
    {
        EndLampRun();
        if (held is not { Kind: BodyKind.Lamp })
            return null;
        if (OpenCar(world, p, self) is int car)
            return IntoTheCar(self, world, p, car);
        if (self.Parent != PlayerState.World)
            return OutOfTheCar(self, world, p) ?? GetDown(self, world.Train, p.Site.Side);
        _lampDown = held.Id;
        Doing = "setting the lamp down";
        return Press();
    }

    /// <summary>
    /// The crates done, before the doors are shut behind us: the lamp this hand set down out at the stop, up again for a car
    /// (<see cref="LampBack"/>), not left in the yard. Null when it set none down, it's gone (someone else has it), or there's
    /// no door open to take it in by.
    /// </summary>
    PlayerIntent? LampAboard(in PlayerState self, World world, StopPlan p)
    {
        if (_lampDown >= 0 && world.Bodies.All.FirstOrDefault(b => b.Id == _lampDown) is { Carrier: < 0, Stowed: false } down
            && down.Parent == PlayerState.World && PlayerId is { } me && world.Bodies.CarriedBy(me) is null && OpenCar(world, p, self) is not null)
            return PickUp(self, world, p, down);
        _lampDown = -1;
        return null;
    }

    /// <summary>The nearest car with its side door open on the stop's side, for a lamp to go back into.</summary>
    static int? OpenCar(World world, StopPlan p, in PlayerState self)
    {
        var train = world.Train;
        var here = PlayerMotor.WorldPosition(self, train);
        return p.OpenSideDoors(train).Where(c => train.Frames[c].Shape.Interior is not null)
            .OrderBy(c => (Flat(train.Frames[c].Origin) - Flat(here)).Length).ThenBy(c => c).Cast<int?>().FirstOrDefault();
    }

    /// <summary>
    /// A lamp back into <paramref name="car"/> (its side door open): to the foot of its steps, up them, in to the middle, and
    /// down on its floor, as a crate goes in. Out again the crates' way.
    /// </summary>
    PlayerIntent? IntoTheCar(in PlayerState self, World world, StopPlan p, int car)
    {
        var train = world.Train;
        int side = p.Site.Side;
        var frame = train.Frames[car];
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        double w = frame.Shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        if (self.Parent == car && self.Surface == Surface.Deck)
        {
            bool inside = Math.Abs(self.Position.X) < w - layout.WallThickness;
            if (!inside && Math.Abs(self.Position.Z) > sd - 0.3)
                return Walk(self, landing, facingIn, "up to the door with the lamp");
            var drop = new Double3(0, layout.FloorHeight, 0);
            if (!Near(self, drop, 0.25) || !Aligned(self, facingIn))
                return Walk(self, drop, facingIn, "taking the lamp in");
            Doing = "putting the lamp down inside";
            return Press();
        }
        if (self.Parent != PlayerState.World)
            return OutOfTheCar(self, world, p) ?? GetDown(self, train, side);
        var foot = frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 });
        var local = frame.ToLocal(PlayerMotor.WorldPosition(self, train));
        double outward = side * local.X - w;
        bool inLane = outward > 0.1 && outward < layout.StepWidth - 0.1 && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3;
        if (inLane)
            return Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps with the lamp");
        var (walk, atFoot) = WalkTo(self, train.Line, p.Spur.Index, foot, null);
        Doing = "bringing the lamp back";
        return atFoot ? Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps with the lamp") : walk;
    }

    /// <summary>Nothing more of the lamp's: back from being out, if it was.</summary>
    PlayerIntent? EndLampRun()
    {
        if (_lamp >= 0)
        {
            _lamp = -1;
            calls.Out(member, false);
        }
        return null;
    }

    /// <summary>
    /// Whether the lamp's this hand's to bring: the first crate hand alive, or with no bot on the crates, the shunter (who
    /// carries where too few do: <see cref="CrewCalls.CrateHands"/>).
    /// </summary>
    bool LampHand() =>
        calls.AmongFirst(member, StopJob.Crates, 1)
        || job == StopJob.Shunter && !calls.AnyOn(StopJob.Crates) && calls.AmongFirst(member, StopJob.Shunter, 1);

    /// <summary>
    /// The heap nearest <paramref name="from"/> with salvage still unfound and nothing lighting it: no lamp within its reach,
    /// carried or set down, nor the headlamp on it. Null at a stop that isn't a wreck, or when every heap's found or lit.
    /// </summary>
    static WreckHeap? DarkHeap(World world, StopPlan p, Double3 from)
    {
        if (!p.Site.Has(ModuleKind.Wreck) || world.Run is not { } run)
            return null;
        return p.Site.Heaps.Where(h => !h.Found && h.Salvage > 0 && !run.Lit(world, h.Centre))
            .OrderBy(h => (Flat(h.Centre) - Flat(from)).Length).ThenBy(h => h.Index).FirstOrDefault();
    }

    /// <summary>
    /// Where the lamp goes down by <paramref name="heap"/>: out from its middle toward the track, where its pieces come out and
    /// the crew stand to take them (<see cref="Run.Run.PieceAt"/>), clear of its shift and within the lamp's reach.
    /// </summary>
    static Double3 LampSpot(World world, StopPlan p, WreckHeap heap)
    {
        var w = world.Run!.FacilityTuning!.Wreck;
        var near = Run.Run.PieceAt(p.Site, heap, 0, 1, w);
        var out_ = (near - heap.Centre) with { Y = 0 };
        var dir = out_.Length > 1e-6 ? out_.Normalized : new Double3(1, 0, 0);
        return heap.Centre + dir * Math.Min(LampOut, w.LampReach - 1);
    }

    /// <summary>
    /// The lamp to take: one this hand has already, or the nearest lying loose that's lighting nothing still to find: on the
    /// ground at the stop, or on the floor of a car of the train (not in a locker, not someone's). Null when there's none.
    /// </summary>
    Body? LooseLamp(World world, StopPlan p, Double3 here)
    {
        var train = world.Train;
        var run = world.Run!;
        var reach = run.FacilityTuning!.Wreck.LampReach;
        // In a locker, only the top thing on its shelves: a tap takes that (Bodies.Handle), and car 1's carry extinguishers too.
        bool Free(Body b) => b.Kind == BodyKind.Lamp && b.Carrier < 0
            && (!b.Stowed || b.Parent >= 0 && Lockers.Contents(world.Bodies, b.Parent, b.Locker).LastOrDefault()?.Id == b.Id)
            && !p.Site.Heaps.Any(h => !h.Found && h.Salvage > 0 && (Flat(Bodies.WorldCentre(b, train)) - Flat(h.Centre)).Length <= reach)
            && (b.Parent == PlayerState.World ? p.Site.Heaps.Any(h => (Flat(b.Centre) - Flat(h.Centre)).Length < 60)
                : train.Dynamics.Consist.IndexOf(b.Parent) > 0 && train.Frames[b.Parent].Shape.Interior is { } room && (b.Stowed || room.Contains(b.Centre)));
        if (_lamp >= 0 && world.Bodies.All.FirstOrDefault(b => b.Id == _lamp) is { } ours && Free(ours))
            return ours;
        return world.Bodies.All.Where(Free).OrderBy(b => b.Parent == PlayerState.World ? 0 : 1)
            .ThenBy(b => (Flat(Bodies.WorldCentre(b, train)) - Flat(here)).Length).ThenBy(b => b.Id).FirstOrDefault();
    }

    /// <summary>A lamp lying on the ground at the stop: to it, facing it, and up with it.</summary>
    PlayerIntent? PickUp(in PlayerState self, World world, StopPlan p, Body lamp)
    {
        var train = world.Train;
        if (self.Parent != PlayerState.World)
            return OutOfTheCar(self, world, p) ?? GetDown(self, train, p.Site.Side);
        var at = lamp.Centre;
        var from = new Double3(self.Position.X - at.X, 0, self.Position.Z - at.Z);
        var stand = at + (from.Length > 0.01 ? from.Normalized : new Double3(1, 0, 0)) * 0.7;
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand with { Y = at.Y }, null);
        if (!there && (Flat(self.Position) - Flat(stand)).Length > 0.25)
        {
            Doing = "to the lamp";
            return step;
        }
        double yaw = DMath.Atan2(-(at.X - self.Position.X), -(at.Z - self.Position.Z));
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        Doing = "taking the lamp up";
        return Press();
    }

    /// <summary>
    /// A lamp on a car's floor (the guard van's): to the foot of that car's side door steps on the stop's side, up them, the
    /// door opened, in to it, and up with it. Out again with it by <see cref="OutOfTheCar"/>.
    /// </summary>
    PlayerIntent? FromTheCar(in PlayerState self, World world, StopPlan p, Body lamp)
    {
        var train = world.Train;
        int car = lamp.Parent, side = p.Site.Side;
        var frame = train.Frames[car];
        var shape = frame.Shape;
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        int door = SideDoor(shape, side) ?? 0;
        bool open = train.Vehicles[car].DoorOpen(door);
        double w = shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        if (self.Parent == car && self.Surface == Surface.Deck)
        {
            bool inside = Math.Abs(self.Position.X) < w - layout.WallThickness;
            if (!inside && !open)
            {
                if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                    return Walk(self, landing, facingIn, "up to the door for a lamp");
                Doing = "opening up for a lamp";
                return new PlayerIntent { Buttons = PlayerButtons.Use };
            }
            // Along the door's line to the middle of the car first (a wall either side of the doorway), then as the cab's kit
            // run takes the kit: at its locker, opened and tapped, or beside it on the floor, looking down at it.
            if (!inside || Math.Abs(self.Position.Z) > sd && Math.Abs(self.Position.X) > w - layout.WallThickness - 0.6)
                return Walk(self, new Double3(0, layout.FloorHeight, 0), facingIn, "in for a lamp");
            Doing = lamp.Stowed ? "taking a lamp from a locker" : "taking a lamp";
            return KitRun.Take(self, train, lamp, _lampTick++);
        }
        if (self.Parent != PlayerState.World)
            return OutOfTheCar(self, world, p) ?? GetDown(self, train, side);
        // On the ground: to the foot of its steps, and up them along the treads.
        var foot = frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 });
        var local = frame.ToLocal(PlayerMotor.WorldPosition(self, train));
        double outward = side * local.X - w;
        bool inLane = outward > 0.1 && outward < layout.StepWidth - 0.1 && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3;
        if (inLane)
            return Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps for a lamp");
        var (walk, atFoot) = WalkTo(self, train.Line, p.Spur.Index, foot, null);
        Doing = "to the guard van for a lamp";
        return atFoot ? Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps for a lamp") : walk;
    }

    /// <summary>
    /// On a car's floor, landing or steps with the lamp (or for one that's gone): out by the side door on the stop's side and down
    /// its steps. Null off a car, or on one with no side door that way (the caller gets down another way).
    /// </summary>
    PlayerIntent? OutOfTheCar(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        if (self.Parent < 0 || self.Surface != Surface.Deck)
            return null;
        int side = p.Site.Side;
        var shape = train.Frames[self.Parent].Shape;
        if (SideDoor(shape, side) is not { } door || !train.Vehicles[self.Parent].DoorOpen(door) || shape.Interior is null)
            return null;
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        double w = shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        if (Math.Abs(self.Position.X) < w - layout.WallThickness)
            return Walk(self, landing, facingIn + Math.PI, "out of the car with the lamp");
        return Walk(self, landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.5 }, Math.PI, "down the steps with the lamp");
    }
}
