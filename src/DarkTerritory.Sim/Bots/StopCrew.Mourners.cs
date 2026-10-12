using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public sealed partial class StopHand
{
    /// <summary>The car a body in our arms is going into (note 623), while it still takes one.</summary>
    int _bodyCar = -1, _bodySide = 1;

    /// <summary>
    /// Note 623: a crewmate's body from the Mourners, picked up (GDD §21: "unless someone stands over it, scatters them or
    /// carries it home"): turned to it and a press of Use, as a crate's taken up. Stood over and clubbed, they never left:
    /// they shy back from anyone near faster than a bot closes (B.14's <c>shy</c>, <c>scatter</c>), and they go only once
    /// it's home or lost. deadLines:3 seed 4's walker stood over the gunner's 270 s so, the mine head's lift unwound.
    /// </summary>
    public PlayerIntent TakeUp(in PlayerState self, TrainOnLine train, Double3 at)
    {
        var here = PlayerMotor.WorldPosition(self, train);
        double yaw = DMath.Atan2(-(at.X - here.X), -(at.Z - here.Z));
        Doing = "picking the body up";
        return Aligned(self, yaw) ? Press() : new PlayerIntent { LookYaw = Turn(self, yaw) };
    }

    /// <summary>The driver's called the loading done (<see cref="CrewCalls.Leaving"/>): no carrying a body home now (note 623).</summary>
    public bool Leaving => calls.Leaving;

    /// <summary>Put down what's in our arms, where we stand (note 623: a body there's no taking home).</summary>
    public PlayerIntent SetDown()
    {
        Doing = "leaving the body";
        return Press();
    }

    /// <summary>Whether a car of the train that takes a body (<see cref="TakesABody"/>) has the foot of its steps on the
    /// body's side within <paramref name="within"/> of it (m, flat).</summary>
    public bool CanTakeHome(TrainOnLine train, Double3 body, double within) => HomeFor(train, body, within) >= 0;

    /// <summary>The nearest car that takes a body, with the foot of its side steps on that side within reach of a point.</summary>
    static int HomeFor(TrainOnLine train, Double3 at, double within)
    {
        if (train.Dynamics.Tuning.Geometry.Interior is not { } layout)
            return -1;
        int best = -1;
        double nearest = within;
        foreach (var f in train.Frames)
        {
            int side = SideOfCar(train, f.Index, at);
            if (!TakesABody(train, f.Index, side))
                continue;
            var foot = f.ToWorld(new Double3(side * (f.Shape.Bounds.Max.X + layout.StepWidth / 2), 0, -layout.SideDoorWidth / 2 - 4 * layout.StepDepth - 0.4));
            double d = (Flat(foot) - Flat(at)).Length;
            if (d <= nearest)
                (best, nearest) = (f.Index, d);
        }
        return best;
    }

    /// <summary>
    /// Note 623: a body in our arms, home with it (B.14: in a car the engine pulls, and the Mourners go): the nearest of the
    /// engine's cars with a side door on our side, up its steps as the crate hands go (<see cref="Carry"/>), the door opened
    /// with it in our arms (a hold of Use, note 581), in, and down in the middle of the floor. Nobody climbs a ladder with a
    /// body (spec B.2), so not the cab. Null if no car of the train takes it with its steps within <paramref name="within"/>.
    /// </summary>
    public PlayerIntent? BodyHome(in PlayerState self, World world, double within)
    {
        var train = world.Train;
        if (train.Dynamics.Tuning.Geometry.Interior is not { } layout)
            return null;
        var here = PlayerMotor.WorldPosition(self, train);
        // Chosen on the ground, by the side we're on (aboard it, that's the side we came up).
        if (self.Parent == PlayerState.World)
        {
            if (_bodyCar < 0 || !TakesABody(train, _bodyCar, SideOfCar(train, _bodyCar, here)))
                _bodyCar = HomeFor(train, here, within);
            if (_bodyCar >= 0)
                _bodySide = SideOfCar(train, _bodyCar, here);
        }
        if (_bodyCar < 0 || !TakesABody(train, _bodyCar, _bodySide))
            return null;
        var frame = train.Frames[_bodyCar];
        int side = _bodySide;
        int door = SideDoor(frame.Shape, side)!.Value;
        bool open = train.Vehicles[_bodyCar].DoorOpen(door);
        double w = frame.Shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        if (self.Parent == _bodyCar && self.Surface == Surface.Deck)
        {
            // Inside: down in the middle of the floor, facing away from the door, where it's in the car for good.
            if (Math.Abs(self.Position.X) < w - layout.WallThickness)
            {
                var drop = new Double3(0, layout.FloorHeight, 0);
                if (!Near(self, drop, 0.25) || !Aligned(self, facingIn))
                    return Walk(self, drop, facingIn, "carrying the body in");
                Doing = "putting the body down";
                return Press();
            }
            // On the landing: the door held open with it in our arms (a hold is the door's, a tap would put it down), then in.
            if (!open)
            {
                if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                    return Walk(self, landing, facingIn, "up to the door with the body");
                Doing = "opening up with the body";
                return new PlayerIntent { Buttons = PlayerButtons.Use };
            }
            return Math.Abs(self.Position.Z) > sd - 0.3
                ? Walk(self, landing, facingIn, "up to the door with the body")
                : Walk(self, new Double3(0, layout.FloorHeight, 0), facingIn, "carrying the body in");
        }
        if (self.Parent != PlayerState.World)
            return null;
        // On the ground: in the lane in front of the steps, on up them; anywhere else, to the foot of them first (by the way
        // round the walls and the train from further off).
        var local = frame.ToLocal(here);
        double outward = side * local.X - w;
        if (outward > 0.1 && outward < layout.StepWidth - 0.1 && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3)
            return Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps with the body");
        var foot = frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 });
        var (step, there) = OnFoot(self, train, train.Dynamics.Path, foot, null);
        Doing = "carrying the body home";
        return there ? Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps with the body") : step;
    }

    /// <summary>A car of the engine's rake (B.14's "a car the engine pulls") with a room and a side door on that side.</summary>
    static bool TakesABody(TrainOnLine train, int car, int side) =>
        car > 0 && car < train.Frames.Count && train.Frames[car].Shape.Interior is not null
        && train.Dynamics.Consist.IndexOf(car) >= 0 && SideDoor(train.Frames[car].Shape, side) is not null;

    /// <summary>Which side of a car a point is (+1 its right).</summary>
    static int SideOfCar(TrainOnLine train, int car, Double3 at) =>
        car >= 0 && car < train.Frames.Count && train.Frames[car].ToLocal(at).X < 0 ? -1 : 1;
}
