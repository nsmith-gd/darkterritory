using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

public static partial class Staging
{
    /// <summary>
    /// The handcar views (note 729; dt screenshot --view handcar, handcarlift): its handle swung so far, and (handcar) the
    /// trolley set on the rails behind the train and run back down the line <paramref name="back"/> metres, or (handcarlift)
    /// still hung on the van with a lift <paramref name="lift"/> of the way through. The train needs one (handcar.always).
    /// </summary>
    public static void Handcar(TrainOnLine train, string view, double back, double lift, double phase)
    {
        if (train.Handcar is null)
            return;
        if (view == "handcar" && train.SetHandcarOnRails())
        {
            var rake = train.HandcarRake!;
            rake.Distance = rake.PreviousDistance = rake.Distance - back;
            train.RefreshFrames();
        }
        train.MirrorHandcar(train.HandcarNow!.Value with { Phase = phase, Lift = view == "handcarlift" ? lift : 0 });
    }

    /// <summary>
    /// The handcar's crew for its views (note 729): two at the handle, pumping, their hands on its grip bars where the swing
    /// has them (handcar); two on the ballast behind the van with their hands under its sides as it comes down (handcarlift).
    /// </summary>
    public static List<Crewmate> HandcarCrew(TrainOnLine train, string view)
    {
        if (train.Handcar is not { } h)
            return [];
        var t = train.Dynamics.Tuning.Handcar;
        var car = train.Frames[h.Id];
        var crew = new List<Crewmate>();
        if (view == "handcar")
        {
            foreach (int end in (ReadOnlySpan<int>)[-1, 1])
            {
                var local = Sim.Train.Handcar.Place(t, end);
                var feet = car.ToWorld(local);
                double localYaw = end < 0 ? Math.PI : 0;
                double yaw = localYaw + car.Heading;
                crew.Add(new Crewmate((byte)(30 + (end + 1) / 2), feet, yaw, true, Act: Art.CrewPose.Pump,
                    Reach: Art.CrewActs.AtTheHandle(t, car, h.Phase, end, feet, yaw), Car: h.Id, Local: local,
                    Phase: Art.CrewActs.PumpPhase(h.Phase, end)));
            }
            return crew;
        }
        // Lifting: the trolley as it's drawn part-way down (GreyboxScene.Lifted), a lifter each side of it on the ground.
        var to = train.HandcarLiftedTo() is { } target ? GreyboxScene.Lifted(car, target, h.Lift) : car;
        var van = train.Frames[train.HandcarHost];
        double backEnd = van.Shape.Platform is { } platform ? platform.Max.Z : van.Shape.HalfLength;
        double hint = train.Cars[train.HandcarHost].FrontDistance;
        foreach (int side in (ReadOnlySpan<int>)[-1, 1])
        {
            var stand = van.ToWorld(new Double3(side * (t.Width / 2 + 0.35), 0, backEnd + 1.1));
            var feet = stand with { Y = PlayerMotor.GroundAt(stand, train.Line, ref hint) };
            var grip = to.ToWorld(new Double3(side * t.Width / 2, t.DeckHeight * 0.5, 0));
            var d = grip - feet;
            double yaw = Math.Atan2(-d.X, -d.Z);
            Double3 Local(Double3 w)
            {
                var e = w - feet;
                return new Double3(e.X * Math.Cos(yaw) - e.Z * Math.Sin(yaw), e.Y, e.X * Math.Sin(yaw) + e.Z * Math.Cos(yaw));
            }
            var near = to.ToWorld(new Double3(side * t.Width / 2, t.DeckHeight * 0.5, -0.35));
            var far = to.ToWorld(new Double3(side * t.Width / 2, t.DeckHeight * 0.5, 0.35));
            crew.Add(new Crewmate((byte)(32 + (side + 1) / 2), feet, yaw, true, Act: Art.CrewPose.Lever, Reach: (Local(near), Local(far))));
        }
        return crew;
    }

    /// <summary>The handcar views' cameras (note 729): up the line off its lamp end at the pumpers; off the van's back at the lift.</summary>
    public static Camera HandcarCamera(TrainOnLine train, string view)
    {
        if (train.Handcar is not { } h)
            return Camera.LookAt(train.Frames[^1].ToWorld(new Double3(-4, 2, 12)), train.Frames[^1].ToWorld(new Double3(0, 1.5, 0)), 55);
        if (view == "handcar")
        {
            var car = train.Frames[h.Id];
            return Camera.LookAt(car.ToWorld(new Double3(-2.4, 1.9, -6.2)), car.ToWorld(new Double3(0, 1.1, 0.2)), 55);
        }
        var van = train.Frames[train.HandcarHost];
        double back = van.Shape.Platform is { } p ? p.Max.Z : van.Shape.HalfLength;
        return Camera.LookAt(van.ToWorld(new Double3(-5.2, 2.0, back + 6.0)), van.ToWorld(new Double3(0, 1.7, back + 0.6)), 55);
    }
}
