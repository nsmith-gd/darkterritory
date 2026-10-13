using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// A stop hand aboard a car when the Choir comes (queue #175, ARCHITECTURE §8 note 439; note 413's "not yet"). A crate hand
/// loading a cargo car stands in it with the side door open, and an open door is the outside (<see cref="PlayerMotor.Space"/>:
/// the Choir comes in through it). With the Choir gathering (facilities.json <c>crew.shelterAt</c>) or here, a hand on a
/// car's floor goes in (from the landing, by the side door, opening it if it's shut), puts down what it carries (a crate put
/// down in there is loaded), shuts every door of the car that's open, from inside at each (<see cref="WarmUp.Inside"/>), and
/// waits. Quiet again (<c>crew.shelterOutAt</c>), it opens the side door the loading uses and goes back to it. All through
/// intent: it walks, presses Use, holds Use.
/// </summary>
public sealed partial class StopHand
{
    enum CarHiding : byte { Off, In, Shut, Reopen }

    CarHiding _carHiding;
    int _hidingCar = -1, _carTicks;

    /// <summary>Where it is in shutting itself into a car from the Choir (for tests and traces): "" when it isn't.</summary>
    public string ShutInCar => _carHiding == CarHiding.Off ? "" : _carHiding.ToString();

    /// <summary>Given up on the car's doors (something in the way of one): back to what it was doing.</summary>
    const int CarGiveUpTicks = SimConstants.TickRate * 30;

    /// <summary>
    /// Shut into the car it's in from the Choir (from <see cref="Decide"/>, at a stop): this tick's intent, or null when
    /// there's nothing in it for us (no Choir, or not on a car with a side door).
    /// </summary>
    PlayerIntent? ShutIn(in PlayerState self, World world, StopPlan p)
    {
        var t = Crew(world);
        var choir = world.Choir;
        bool gathering = t.ShelterAt > 0 && !world.SafeYard && !world.TrainInFort && (choir.Present || choir.Build >= t.ShelterAt);
        bool quiet = !choir.Present && choir.Build < t.ShelterOutAt;
        var train = world.Train;
        int side = p.Site.Side;
        if (_carHiding == CarHiding.Off)
        {
            if (!gathering || PlayerId is null || self.Surface != Surface.Deck || self.Parent <= 0 || self.Parent >= train.Frames.Count
                || train.Frames[self.Parent].Shape is not { Interior: not null } entered || SideDoor(entered, side) is null)
                return null;
            _hidingCar = self.Parent;
            _carHiding = CarHiding.In;
            _carTicks = 0;
        }
        // Off the car (knocked off, or it's moved under us), or stuck at a door: back to the work.
        if (self.Parent != _hidingCar || ++_carTicks > CarGiveUpTicks)
            return CarDone();
        var shape = train.Frames[_hidingCar].Shape;
        var car = train.Vehicles[_hidingCar];
        int sideDoor = SideDoor(shape, side)!.Value;
        bool inside = PlayerMotor.Indoors(self, train);
        // Looking across the car from its side door (as the crate hands carry in).
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        switch (_carHiding)
        {
            case CarHiding.In:
                {
                    if (quiet)
                        return CarDone();
                    if (inside)
                    {
                        // What it carries, down in here facing the car's middle, clear of the door (a crate in a car is loaded);
                        // then the doors.
                        if (world.Bodies.CarriedBy(PlayerId!.Value) is not null)
                        {
                            if (!Aligned(self, facingIn))
                                return new PlayerIntent { LookYaw = Turn(self, facingIn) };
                            Doing = "putting it down for the Choir";
                            return Press();
                        }
                        _carHiding = CarHiding.Shut;
                        return new PlayerIntent();
                    }
                    // On the landing: in by the side door, opened first if it's shut.
                    var layout = train.Dynamics.Tuning.Geometry.Interior!;
                    double w = shape.Bounds.Max.X;
                    if (!car.DoorOpen(sideDoor))
                    {
                        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
                        if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                            return Walk(self, landing, facingIn, "in from the Choir");
                        Doing = "in from the Choir";
                        return new PlayerIntent { Buttons = PlayerButtons.Use };
                    }
                    return Walk(self, new Double3(0, layout.FloorHeight, 0), facingIn, "in from the Choir");
                }
            case CarHiding.Shut:
                {
                    // Every door of the car shut (not the roof hatch: the crane's, note 99), each from inside at it.
                    var open = shape.DoorList.Where(d => d.Index != CarShape.HatchBit && car.DoorOpen(d.Index)).Select(d => (int?)d.Index).FirstOrDefault();
                    if (open is not { } door)
                    {
                        _carTicks = 0; // waiting's not being stuck
                        if (quiet)
                        {
                            _carHiding = CarHiding.Reopen;
                            return new PlayerIntent();
                        }
                        Doing = "shut in a car from the Choir";
                        return new PlayerIntent();
                    }
                    var (at, yaw) = WarmUp.Inside(shape, door);
                    if (!Near(self, at, 0.2) || !Aligned(self, yaw))
                        return Walk(self, at, yaw, "shutting the car on the Choir");
                    Doing = "shutting the car on the Choir";
                    _pressed = false;
                    return new PlayerIntent { Buttons = PlayerButtons.Use };
                }
            case CarHiding.Reopen:
                {
                    // The Choir back before we're out: the doors again.
                    if (gathering)
                    {
                        _carHiding = CarHiding.Shut;
                        return new PlayerIntent();
                    }
                    // Quiet: the side door the loading's by open again, and back to it.
                    if (car.DoorOpen(sideDoor))
                        return CarDone();
                    var (at, yaw) = WarmUp.Inside(shape, sideDoor);
                    if (!Near(self, at, 0.2) || !Aligned(self, yaw))
                        return Walk(self, at, yaw, "opening up");
                    Doing = "opening up";
                    _pressed = false;
                    return new PlayerIntent { Buttons = PlayerButtons.Use };
                }
        }
        return null;
    }

    PlayerIntent? CarDone()
    {
        _carHiding = CarHiding.Off;
        _hidingCar = -1;
        _carTicks = 0;
        return null;
    }
}
