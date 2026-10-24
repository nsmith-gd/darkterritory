using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Named camera setups for screenshots, so an agent or a designer can ask for "the roof of car 2"
/// instead of coordinates. Each view also places the engine's headlamp.
/// </summary>
public static class Views
{
    public static readonly string[] Names = ["trackside", "roof", "cab", "fireman", "chase", "ahead", "gap", "gangway", "gun"];

    public static Camera Get(string name, TrainOnLine train, int car = 2)
    {
        var engine = train.Frames[0];
        var target = train.Frames[Math.Min(car, train.Frames.Count - 1)];
        double roof = target.Shape.RoofHeight;
        double engineHalf = engine.Shape.HalfLength;
        return name switch
        {
            "trackside" => Camera.LookAt(engine.ToWorld(new Double3(9, 1.7, -engineHalf - 25)), target.ToWorld(new Double3(0, 2.5, 0)), 60),
            "roof" => Camera.LookAt(target.ToWorld(new Double3(0.2, roof + 1.65, 3)), target.ToWorld(new Double3(0, roof + 1.2, -40)), 75),
            // (Not one of Names, which the perf budgets walk.) Close on the roof crew (Staging.Crew), and what's behind them.
            "crew" => Camera.LookAt(target.ToWorld(new Double3(0.6, roof + 1.75, -1.6)), target.ToWorld(new Double3(-0.6, roof + 1.45, -6.2)), 50),
            // Off the car's left side on the ballast, level with it: what it is from the lineside (note 184: the crew car's lit
            // windows and stovepipe, an armoured car's plate, a guard car's gun).
            "side" => Camera.LookAt(target.ToWorld(new Double3(-9, 2.6, 5)), target.ToWorld(new Double3(0, 2.4, -1)), 60),
            // Broadside off the car's left or right (broadsider) on the ballast, the whole car in frame and its underframe against
            // the sleepers: how far gone it is (note 779, with --integrity and --apart).
            "broadside" or "broadsider" => Camera.LookAt(target.ToWorld(new Double3((name == "broadside" ? -1 : 1) * (target.Shape.HalfWidth + 12), 1.5, 2.5)),
                target.ToWorld(new Double3(0, 1.4, 0)), 55),
            // Inside the car, at its front end, looking back down the aisle past the cargo.
            // Note 301: a battered car's dent on its left wall and a breach in its rear end wall, their callouts (with
            // --integrity and --breached).
            // Slice 2: the engine's dent on its boiler's left flank, from out beside the running board (with --integrity).
            "mendengine" => Repairs.DentAt(train, 0) is { } ed
                ? Camera.LookAt(engine.ToWorld(ed + new Double3(-3.2, 0.9, 2.6)), engine.ToWorld(ed), 60)
                : Camera.LookAt(engine.ToWorld(new Double3(-4, 2.5, 3)), engine.ToWorld(new Double3(0, 2, 0)), 60),
            "mend" => Camera.LookAt(target.ToWorld(new Double3(0.4, Floor(train) + 1.65, -0.5)), target.ToWorld(new Double3(-1.0, Floor(train) + 1.1, target.Shape.HalfLength * 0.62)), 70),
            // (Not one of Names.) Up close on the car's front hanging lantern, at arm's length below it: how it's textured (the
            // director's playtest of 16 Oct, line 12, note 900: "the texture wrapping on the lamps").
            "carlamp" when target.Shape.Interior is { } lit && Art.SceneArt.LampPositions(lit).First() is var lamp =>
                Camera.LookAt(target.ToWorld(new Double3(lamp.X - 0.45, lamp.Y - 0.12, lamp.Z + 0.5)), target.ToWorld(new Double3(lamp.X, lamp.Y - 0.05, lamp.Z)), 45),
            "inside" => Camera.LookAt(target.ToWorld(new Double3(-0.5, Floor(train) + 1.65, -target.Shape.HalfLength + 0.6)), target.ToWorld(new Double3(0, Floor(train) + 1.3, 2)), 70),
            // From inside the car behind, through both open end doors at this car's rear doorway (note 110: who comes through).
            "door" => DoorCamera(train, car),
            // On the ballast beside the gap behind this car, looking in under the plate (what checks a gap: the Whistler's).
            "gapside" => GapSideCamera(train, car),
            // Off the target car's right, low, on its rear bogie: a hot axle box smoking (note 331; dt screenshot --hotbox s).
            "hotbox" => Camera.LookAt(target.ToWorld(new Double3(target.Shape.HalfWidth + 4.2, 1.5, target.Shape.HalfLength - 5.5)),
                target.ToWorld(new Double3(target.Shape.HalfWidth, 1.1, target.Shape.HalfLength - 1.6)), 50),
            // Low off the right of the target car, on the ballast, along its wheels (the flange sparks: --strain).
            // Side on to the staged row of the film's crew at their work (dt screenshot --wreck-poses), off the target car's right.
            "poses" => Camera.LookAt(target.ToWorld(new Double3(7.4, 1.3, -target.Shape.HalfLength * 0.6 + 1.95)),
                target.ToWorld(new Double3(2.6, 0.85, -target.Shape.HalfLength * 0.6 + 1.95)), 55),
            "flanges" => Camera.LookAt(target.ToWorld(new Double3(4.5, 0.9, -target.Shape.HalfLength - 3)), target.ToWorld(new Double3(0.6, 0.3, 0)), 60),
            // On the line behind the last car, low, looking up the train: how far the cars lean out on a bend taken too fast
            // (note 370: dt screenshot --view lean --strain x), their ends against the rails and the horizon.
            "lean" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(0.3, 1.3, train.Frames[^1].Shape.HalfLength + 8)),
                train.Frames[^1].ToWorld(new Double3(0, 1.9, -12)), 45),
            // From the left of the middle car's gap (the staged Whistler's), out along its trail to the nest (--whistler nest).
            "trail" => TrailCamera(train),
            // Behind the rear car and off its side, a little over its roof, looking at the roof's end and down the car's end: where
            // the Cinder Hounds come up (their board, --board s).
            "board" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(-(train.Frames[^1].Shape.HalfWidth + 3.5), train.Frames[^1].Shape.RoofHeight + 1.4, train.Frames[^1].Shape.HalfLength + 7)),
                train.Frames[^1].ToWorld(new Double3(0, train.Frames[^1].Shape.RoofHeight - 0.8, train.Frames[^1].Shape.HalfLength - 1.5)), 55),
            // From the rear car's roof at its end, a gunner's eye, back down the line at the staged Cinder Hound pack running
            // it down 14 to 26 m behind (Staging.Threats): what has to read at night (note 209's "not yet").
            "hounds" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(0.4, train.Frames[^1].Shape.RoofHeight + 1.6, train.Frames[^1].Shape.HalfLength - 1.2)),
                train.Frames[^1].ToWorld(new Double3(0, 0.6, train.Frames[^1].Shape.HalfLength + 18)), 60),
            // Close on the nest at the trail's end, the Whistler crouched over its catch.
            "nest" => NestCamera(train),
            // Off the staged Whistler's side on its run and a little ahead, at a chaser's eye, looking at it going with
            // its catch (--whistler carry).
            "carry" => CarryCamera(train),
            // Inside the rear car, a few steps from its end door, looking at it: what the Car Hugger's mouth is over, and
            // whoever it has there (--hugger swallow).
            "swallow" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(0.7, Floor(train) + 1.6, train.Frames[^1].Shape.HalfLength - 3.6)),
                train.Frames[^1].ToWorld(new Double3(-0.45, Floor(train) + 1.0, train.Frames[^1].Shape.HalfLength)), 60),
            // Off the second car's left, over the shoulder of crewmate 4 (Staging.Lone) at the Ribbit pack beyond them.
            // (Not one of Names.) The pack eating crewmate 4 (note 558): dt screenshot --ribbits devour puts the camera on them
            // (Staging.FeastCamera); without it, the pack's view.
            "feast" or "eaten" => Get("pack", train, car),
            "pack" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 0.4), 2.1, 1.2)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 5.2), 0.4, -1.6)), 55),
            // Up off the second car's left, looking along its roof to the gap behind it (the hounds' patrol, --patrol).
            "patrol" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 6.5), train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.RoofHeight + 2.2, -2)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(0, train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.RoofHeight - 0.6, 2)), 60),
            // Low along the second car's left, side on to crewmate 4 and what's on them (the Ribbits' devour, --ribbits devour).
            "packside" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.6), 1.1, 3.4)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.8), 0.6, -1.5)), 50),
            // Off the second car's left, over crewmate 4's shoulder, up at what's stood in front of them (the staged Gaunt).
            "gaunt" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.2), 1.75, 0.2)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.4), 1.9, -1.5)), 55),
            // The same, from in front of it, close, up at its face.
            "gauntface" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.0), 1.6, -3.0)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.9), 2.45, -1.6)), 50),
            // Off the second car's left, over crewmate 4's shoulder, down at what's low on the ground in front of them (the
            // staged Grumbler).
            "grumbler" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.3), 1.35, 0.1)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.4), 0.4, -1.6)), 55),
            // Off the second car's left, over crewmate 4's shoulder, at what's squatted in the ash out there (the staged Soot
            // Child), or on them.
            "soot" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.0), 1.55, 0.3)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.6), 0.6, -1.5)), 50),
            // From the side, at crewmate 4 and what's on them (the staged Soot Child drinking).
            "sootside" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.4), 1.45, 0.4)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.45), 1.2, -1.5)), 45),
            // Close behind crewmate 4 (Staging.Lone), at their back: what their friends see there and they can't (the
            // staged Follower).
            "follower" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.1), 1.6, -1.25)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.2), 1.25, -1.5)), 50),
            // (Not one of Names.) A crewmate's eye on the ground beside the stopped engine's front, out at the staged Moose 15 m
            // off up the line in the headlamp's spill (Staging.Moose: graze, listen, warn).
            "moose" => MooseCamera(train),
            // (Not one of Names.) Dave (note 570): over his shoulder at his canvas, close; from the engine's front as the crew
            // would first see him, 30 m off in the dark with his lantern; and side on to him with crewmate 4 (--dave warn, grab).
            "dave" => DaveCamera(train, 0),
            "davefar" => DaveCamera(train, 1),
            "davewarn" => DaveCamera(train, 2),
            "daveface" => DaveCamera(train, 3),
            // (Not one of Names.) Jacob (note 572): side on to him and his rod; from the engine's roof, the train blessed.
            "jacob" => Camera.LookAt(Staging.JacobAt(train) + new Double3(0, 1.6, 0) + (Staging.Lineside(train, 26, 14) - Staging.JacobAt(train)).Normalized * 3.5
                - ((Staging.Lineside(train, 20, 30) - Staging.JacobAt(train)) with { Y = 0 }).Normalized * 1.0, Staging.JacobAt(train) + Double3.Up * 1.4
                + ((Staging.Lineside(train, 20, 30) - Staging.JacobAt(train)) with { Y = 0 }).Normalized * 1.5, 60),
            // ... and face to face with him, from out over the water's edge (his look, after the director's photographs).
            "jacobface" => Camera.LookAt(Staging.JacobAt(train) + Double3.Up * 1.62 + ((Staging.Lineside(train, 20, 30) - Staging.JacobAt(train)) with { Y = 0 }).Normalized * 1.5
                + ((Staging.Lineside(train, 26, 14) - Staging.JacobAt(train)) with { Y = 0 }).Normalized * 0.4, Staging.JacobAt(train) + Double3.Up * 1.5, 55),
            // (Not one of Names.) Dawn's Wakers (note 588): back down the line from the guard van at one coming (the screenshot
            // stages it and puts the camera on it; this is only where it looks without one).
            "waker" or "wakerlift" or "wakerinside" or "wakerrise" or "wakerwall" or "wakerpick" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(0.6, 2.4, train.Frames[^1].Shape.HalfLength + 1.2)),
                train.Frames[^1].ToWorld(new Double3(0, 9, 120)), 70),
            "jacobblessed" => Camera.LookAt(train.Frames[0].ToWorld(new Double3(6, 4.5, -6)), train.Frames[Math.Min(3, train.Frames.Count - 1)].ToWorld(new Double3(0, 2, 0)), 60),
            // Over crewmate 4's shoulder out in front of the engine, at the staged Moose squaring up to them, coming at them,
            // or on them (Staging.Moose: squareup, charge, pin...).
            "moosecharge" => MooseChargeCamera(train),
            // (Not one of Names.) A crewmate's eye on the second car's roof over the moving train, at the staged Gannet
            // (Staging.Gannet): up at it soaring in the engine's smoke; diving at the walker ahead; its beak in the planks;
            // stood on someone, mantled.
            "gannet" or "gannetfold" or "gannetstuck" or "gannetpin" => GannetCamera(train, name),
            // (Not one of Names.) Out off the line's left at the staged Mourners round a body, dragging it off (Staging.Mourners),
            // and at the staged Freight Beetle shoving a crate (Staging.Beetle); at Tower Jaw's coaling tower (Staging.TowerJaw:
            // the screenshot puts the camera there, at the night's tower; this is only where it looks without one).
            "mourners" => Staging.MournersCamera(train),
            // (Not one of Names.) The sense table (note 728): a crewmate's eye beside the engine, out at a music box playing in
            // the field and the Moose come over to it (Staging.MusicBox); side on to the extinguisher's jet on a Cinder Hound
            // backing off down a roof (Staging.ExtinguisherHound).
            "musicbox" => Staging.MusicBoxCamera(train),
            "extinguisherhound" => Staging.ExtinguisherHoundCamera(train),
            "beetle" => Staging.BeetleCamera(train),
            // (Not one of Names.) The staged Pickers in the headlamp's spill off the line's left (Staging.Pickers), and close to
            // the ones under their crates; the Lodger folded in its crouch and shrieking (Staging.Lodger); the Householder at
            // its table (Staging.Householder: in a night, its house's).
            "pickers" or "pickerscarry" => Staging.PickersCamera(train, name),
            // (Not one of Names.) The staged Mouse Moose in the headlamp's spill off the line's left (Staging.MouseMoose), and close
            // to the one tugging its load off (note 790).
            "mousemoose" or "mousemoosedrag" or "mousemooseface" or "mousemoosehole" => Staging.MouseMooseCamera(train, name),
            // (Not one of Names.) The staged Bruin (Staging.Bruin; note 1007): coming out of the trees to its car, and risen in it.
            "bruin" or "bruincar" => Staging.BruinCamera(train, name),
            // (Not one of Names.) The staged Head Waiter on the BBQ deck (Staging.HeadWaiter, note 1008): off the deck's front
            // corner back at the chair and it, or close on it ringing the glass.
            "headwaiter" or "headwaiterdeck" => Staging.HeadWaiterCamera(train, name),
            "lodger" or "lodgershriek" => Staging.LodgerCamera(train, name),
            "householder" => Staging.HouseholderCamera(train),
            "towerjaw" or "towerjawfall" => Camera.LookAt(engine.ToWorld(new Double3(-6, 2, -engineHalf - 20)), engine.ToWorld(new Double3(4, 4, -engineHalf - 40)), 66),
            // (Not one of Names.) G1's house creatures in a night's open house (Staging.House: the screenshot puts the camera in the
            // house; this is only where it looks without one): the Hanger, the Hollow House, the Householder's house.
            "hanger" or "hangergrab" or "hollowhouse" or "hollowcollapse" or "householderhouse" => Camera.LookAt(engine.ToWorld(new Double3(-8, 2, 0)), engine.ToWorld(new Double3(-30, 2, 0)), 66),
            // (Not one of Names.) The train's own (Staging.Trainfolk): the Brakeman winding a wheel on the second car's roof, and
            // cornered there; the Knotter taut across the gap behind it at speed from the next car's roof, and coiled round
            // someone; Hotbox glowing in its truck from beside the car, and out on the ballast at a stand.
            "brakeman" or "brakemancorner" or "knotter" or "knotterslip" or "hotboxbug" or "hotboxout" => Staging.TrainfolkCamera(train, name),
            // (Not one of Names.) From the engine's roof back along the train to the horizon behind it: where the Wakers' stir
            // shows before dawn (note 599; --stir t).
            "stir" => Camera.LookAt(train.Frames[0].ToWorld(new Double3(1.0, train.Frames[0].Shape.RoofHeight + 1.7, 0)),
                train.Frames[train.Dynamics.Consist.Vehicles[^1].Id] is var rear ? rear.ToWorld(new Double3(0, 6, rear.Shape.HalfLength + 300)) : default, 62),
            // (Not one of Names.) Across the line, close, side on to crewmate 4 pinned under its rack (--moose pin).
            "moosepin" => Camera.LookAt(Staging.Lineside(train, 7, 1.6) + Double3.Up * EyeHeight,
                Staging.MooseCrewmate(train, "pin").Feet + (Staging.MooseAt(train, "pin").At - Staging.MooseCrewmate(train, "pin").Feet) * 0.4 + Double3.Up * 0.9, 55),
            // Off the last car's side, looking up at what's over its roof (the staged Choir besieging the guard van).
            "choir" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(4.6, train.Frames[^1].Shape.RoofHeight + 0.6, 4.5)),
                train.Frames[^1].ToWorld(new Double3(0, train.Frames[^1].Shape.RoofHeight + 2.0, 0)), 55),
            // In car 2's aisle, close in front of what's stood in it (the staged Passenger, Staging.Passenger), a little off
            // to one side to see what it's dragging behind it.
            "passenger" => Camera.LookAt(target.ToWorld(new Double3(0.2, Floor(train) + 1.6, -target.Shape.HalfLength + 3.3)),
                target.ToWorld(new Double3(-0.35, Floor(train) + 1.25, -target.Shape.HalfLength + 5.9)), 55),
            // In the middle car, under its forward lantern, up at what's round it (the staged Fire Flies).
            "flies" => FliesCamera(train),
            // Note 717: horseplay on car 2's roof (Staging.Horseplay): a shove over the side, from along the roof ahead; a
            // crewmate's sooted face, close.
            "shove" => Camera.LookAt(target.ToWorld(new Double3(-0.6, roof + 1.7, -4.4)), target.ToWorld(new Double3(target.Shape.HalfWidth - 0.4, roof + 0.9, 0)), 55),
            "sooted" => Camera.LookAt(target.ToWorld(new Double3(0.5, roof + 1.7, -1.9)), target.ToWorld(new Double3(0, roof + 1.35, 0)), 45),
            // Note 723 (Staging.Rescue): out on the ballast ahead of a cargo car's open side door, back at the crewmate in it hauling
            // up the friend running beside it; and close along car 2's roof at a friend carried over the shoulder.
            "handup" => HandUpCamera(train),
            // (Not one of Names.) Note 722 (Staging.Emotes): off the train's side about 6 m out, at the emotes for two on car 2's
            // roof, a dance-off there, a roof edge and a door sill sat on, and a doorway leant out of.
            "highfive" or "danceoff" or "sitedge" or "leanout" => Staging.EmoteCamera(train, name),
            "carryfriend" => Camera.LookAt(target.ToWorld(new Double3(1.6, roof + 1.9, -4.4)), target.ToWorld(new Double3(-0.3, roof + 1.2, -1.0)), 50),
            "crewside" => Camera.LookAt(target.ToWorld(new Double3(2.6, roof + 1.9, -4.2)), target.ToWorld(new Double3(-0.8, roof + 1.45, -5.6)), 45),
            // (Not one of Names.) Down on the roof round the first two of the staged crew (dt screenshot --act lantern,...), close
            // over their feet: what the hand lamp's light falls on and the shadows it throws (note 436).
            "lampshadow" => Camera.LookAt(target.ToWorld(new Double3(2.2, roof + 2.3, -2.2)), target.ToWorld(new Double3(-0.2, roof + 0.3, -5.6)), 60),
            "cab" => CabCamera(engine),
            // (Not one of Names.) Across the cab at the speaking tube's mouthpiece on its left wall (note 720), and in the guard
            // van at its other end, the front right corner.
            "tube" => TubeCamera(train, 0),
            "tubevan" => TubeCamera(train, 1),
            // (Not one of Names.) Close on the whistle codes' card by the cord (note 720), as the driver turns to read it.
            "whistlecard" => Art.SceneArt.WhistleCardAt(engine.Shape) is { } card
                ? Camera.LookAt(engine.ToWorld(card + new Double3(-0.62, -0.06, 0.05)), engine.ToWorld(card + new Double3(0, -0.1, 0)), 45)
                : CabCamera(engine),
            // (Not one of Names.) At the lineside off car 1, at the staged crew's lamp signals (dt screenshot --signal stop,go,back;
            // note 720), the train behind them.
            "lampsignal" => Camera.LookAt(Staging.Lineside(train, Staging.SignalAhead, Staging.SignalCamera) + Double3.Up * 1.55,
                Staging.Lineside(train, Staging.SignalAhead, Staging.SignalOut) + Double3.Up * 1.25, 55),
            // (Not one of Names.) Note 718: from the cab coming into a stop with a comet fragment lying out (the screenshot puts
            // the train short of it); and stood beside the engine looking down at the creatures' trophies laid out on the
            // ground off the line's left.
            "comet" => CabCamera(engine),
            // (Not one of Names.) Note 731: the staged row of painted masks (Staging.Masks) from 5 m, as a crewmate sees the crew
            // by the hand lamp; and close on the middle two, their paint; and the sigil on the earflap.
            "masks" => Camera.LookAt(Staging.Lineside(train, Staging.MaskAhead, Staging.MaskCamera) + Double3.Up * 1.6,
                Staging.Lineside(train, Staging.MaskAhead, Staging.MaskOut) + Double3.Up * 1.25, 55),
            // (Not one of Names.) Note 731: the crew's sigil on the engine's left side (dt screenshot --view sigil [--sigil key]),
            // from the ballast 6 m out and a little ahead.
            "sigil" => Art.MaskKit.SigilSpot(engine.Shape, -1) is var (spot, _)
                ? Camera.LookAt(engine.ToWorld(new Double3(spot.X - 6, 1.9, spot.Z - 2.5)), engine.ToWorld(new Double3(spot.X, spot.Y, spot.Z)), 55)
                : Camera.LookAt(engine.ToWorld(new Double3(-8.5, 2.2, 1.5)), engine.ToWorld(new Double3(0, 2.0, 0)), 60),
            // ... and from beside the fifth (the crew's sigil on its earflap).
            "maskside" => Camera.LookAt(Staging.Lineside(train, Staging.MaskAhead + 0.5 * Staging.MaskSpacing + 1.7, Staging.MaskOut + 0.9) + Double3.Up * 1.65,
                Staging.Lineside(train, Staging.MaskAhead + 0.5 * Staging.MaskSpacing, Staging.MaskOut) + Double3.Up * 1.6, 35),
            "maskface" => Camera.LookAt(Staging.Lineside(train, Staging.MaskAhead - 0.1, Staging.MaskOut + 1.25) + Double3.Up * 1.62,
                Staging.Lineside(train, Staging.MaskAhead, Staging.MaskOut) + Double3.Up * 1.55, 40),
            // (Not one of Names.) Note 721: the crew photo at the gate (dt screenshot --view crewphoto stages it and takes its
            // own lens, CrewPhoto.Stage) and the town's board of lost crews (--view lostcrews, Staging.TownCamera); off the
            // engine's side until then.
            "crewphoto" or "lostcrews" => Camera.LookAt(engine.ToWorld(new Double3(9, 1.6, -4)), engine.ToWorld(new Double3(3, 1.4, -2)), 40),
            // (Not one of Names.) Note 729: the handcar on the line at night, two at its handle, from up the line off its lamp end;
            // and lifted down off the guard van's back, from the ballast behind it (Staging.Handcar).
            "handcar" or "handcarlift" => Staging.HandcarCamera(train, name),
            // (Not one of Names.) Note 730: a stop's odd thing (dt screenshot --view oddities stages it and stands its own
            // camera off it); off the engine's side until then.
            "oddities" => Camera.LookAt(engine.ToWorld(new Double3(9, 1.6, -4)), engine.ToWorld(new Double3(3, 1.4, -2)), 40),
            // (Not one of Names.) Note 726: a friend on the crane's hook; the screenshot puts the camera on the ground under it
            // (dt screenshot --view hoist stops at a crane's facility); until then, off the engine's side.
            "hoist" => Camera.LookAt(engine.ToWorld(new Double3(9, 1.6, -4)), engine.ToWorld(new Double3(3, 3, -2)), 60),
            "trophies" => Camera.LookAt(Staging.Lineside(train, 4.2, -1.6) + Double3.Up * 1.55, Staging.Lineside(train, 7.2, -4.1) + Double3.Up * 0.05, 40),
            // (Not one of Names.) Note 719, the big finds: two crewmates carrying a safe off the line's left (Staging.Jackpot puts
            // the camera beside them), a jackpot house from its street (Staging.JackpotHouse), and the finds slid in a car after
            // a bend, from its front end (the screenshot frames the car it simulates); until then, off the engine's left.
            "jackpot" => Camera.LookAt(Staging.Lineside(train, 4, -8) + Double3.Up * 1.55, Staging.Lineside(train, 9, -4.2) + Double3.Up * 0.8, 52),
            "jackpothouse" => Camera.LookAt(engine.ToWorld(new Double3(-9, 2.6, 5)), engine.ToWorld(new Double3(-30, 2, 0)), 60),
            "looseloot" => Camera.LookAt(target.ToWorld(new Double3(-0.3, Floor(train) + 1.75, -target.Shape.HalfLength + 0.7)), target.ToWorld(new Double3(0, Floor(train) + 0.2, 1.2)), 70),
            // Crouched where the fireman shovels, at the firebox door (what's seen when it's open: the staged Stoker).
            "firebox" => FireboxCamera(engine),
            // The work at the cab's front (note 280): from behind where one person runs it all, forward at the coal on the left,
            // the fire door, the console and the dials on the right, and the line through the windows over them.
            "fireman" => FiremanCamera(engine),
            // T109: across the cab from the driver's place to the vent on its left side, and from the fireman's to the
            // engineering kit's rack on the right.
            "vent" => SideCamera(engine, InteractableKind.Vent, 1),
            "rack" => SideCamera(engine, InteractableKind.ToolRack, -1),
            // (Not one of Names.) From the fireman's side across to the powered switch thrower's lever behind the rack (note
            // 196; dt screenshot --upgrades poweredSwitchThrower --view points).
            "points" => SideCamera(engine, InteractableKind.Points, -1),
            "chase" => ChaseCamera(train),
            // (Not one of Names.) The dead's views (App. D.10, note 743): the wide of the whole train from beside the line;
            // over the shoulder of the first of the staged crew on car 2's roof (--crew); over the staged body on car 3's (--bodies).
            "deadwide" => DeadViews.Wide(Train(train.Frames, train.StandingCar)),
            "deadshoulder" => StagedShoulder(train),
            "deadbody" => DeadViews.OverBody(train.Frames[Math.Min(3, train.Frames.Count - 1)].ToWorld(new Double3(0, train.Frames[Math.Min(3, train.Frames.Count - 1)].Shape.RoofHeight, -2)),
                train.Frames[0].Back * -1),
            // (Not one of Names.) Off the engine's right side ahead of it, the whole of it three-quarters on (note 276, the cab
            // forward): the cab and its lamp leading, the boiler and the stack behind, car 1 coupled on.
            // The boiler's left flank, where it tears when it ruptures (TrainKit.RuptureSeam; dt screenshot --ruptured).
            "rupture" => Camera.LookAt(engine.ToWorld(new Double3(-14, 2.4, -engineHalf + 1)), engine.ToWorld(new Double3(-0.7, 3.0, 0.5)), 60),
            // (Not one of Names.) Close on the seam itself, 5 m off it (note 991: the tear, or the plate bolted over it once mended;
            // dt screenshot --view seam --patched).
            "seam" => Art.TrainKit.RuptureSeam(engine.Shape) is var seam
                ? Camera.LookAt(engine.ToWorld(new Double3(seam.X - 4.5, seam.Y + 0.6, seam.Z - 2)), engine.ToWorld(new Double3(seam.X, seam.Y, seam.Z)), 45)
                : default,
            "engine" => Camera.LookAt(engine.ToWorld(new Double3(8.5, 3.2, -engineHalf - 6)), engine.ToWorld(new Double3(0, 2.2, 1)), 55),
            // (Not one of Names.) On the cab floor, up at the ladder and the hatch it goes up to (the director, 8 Oct: "ladder
            // in cab to nowhere").
            "cabhatch" => engine.Shape.Ladders.FirstOrDefault(d => d.Foot.Y > 0.2 && d.Inward.Z > 0) is { Top: > 0 } hatchLadder
                ? Camera.LookAt(engine.ToWorld(hatchLadder.Foot + new Double3(1.2, 1.5, -1.3)), engine.ToWorld(hatchLadder.Foot + new Double3(0, 2.7, -0.33)), 70)
                : Camera.LookAt(engine.ToWorld(new Double3(0, 2.5, 0)), engine.ToWorld(new Double3(0, 2.5, -1)), 60),
            // (Not one of Names.) The side doors at the cab's front (note 856): from across the cab at the one beside the coal
            // (cabdoor) and at the right-hand one (cabdoorright); and from the ballast off the engine's front quarter at each, its
            // landing and steps (cabdoorout, cabdoorrightout). --doors-open slides them open.
            "cabdoor" => CabDoorCamera(engine, -1, inside: true),
            "cabdoorright" => CabDoorCamera(engine, 1, inside: true),
            "cabdoorout" => CabDoorCamera(engine, -1, inside: false),
            "cabdoorrightout" => CabDoorCamera(engine, 1, inside: false),
            // (Not one of Names.) From over car 1's front end, a crewmate's eye up on the roofs, forward along the hood to the
            // whistle on it: its valve lever pulled down by its rod from the cab while a crewmate blows it (note 445).
            "whistlepull" => Camera.LookAt(engine.ToWorld(new Double3(1.4, engine.Shape.Bounds.Max.Y + 1.3, Art.TrainKit.WhistleZ(engine.Shape) + 5.5)),
                engine.ToWorld(new Double3(0.3, engine.Shape.Bounds.Max.Y + 0.3, Art.TrainKit.WhistleZ(engine.Shape))), 45),
            // (Not one of Names.) Close on the whistle's lever and its rod forward along the roof (note 445).
            "whistlelever" => Camera.LookAt(engine.ToWorld(new Double3(1.5, engine.Shape.Bounds.Max.Y + 0.8, Art.TrainKit.WhistleZ(engine.Shape) + 1.4)),
                engine.ToWorld(new Double3(0.4, engine.Shape.Bounds.Max.Y + 0.4, Art.TrainKit.WhistleZ(engine.Shape) - 0.6)), 50),
            // (Not one of Names.) The staged Gaunt leaving with what it took (--gaunt leave), side on as it walks off from the
            // train: the load under it in its mouth (note 505).
            "gauntcarry" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.6), 1.6, -6.2)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.4), 1.6, -1.5)), 60),
            // (Not one of Names.) A breath from the driver's window, looking out through it (note 485): the frost in from its
            // frame, and with --breathe the eye's own breath fogging it.
            "glass" => Art.TrainKit.CabPanes(engine.Shape).Skip(1).FirstOrDefault() is { U.X: > 0 } pane
                ? Camera.LookAt(engine.ToWorld(D(pane.Centre + pane.In * 0.45f - new Vector3(0.12f, 0.05f, 0))), engine.ToWorld(D(pane.Centre - pane.In * 6 - new Vector3(0.1f, 0.25f, 0))), 70)
                : Camera.LookAt(engine.ToWorld(new Double3(0, 2.5, 0)), engine.ToWorld(new Double3(0, 2.5, -1)), 60),
            // (Not one of Names.) In the cab's front corner on the driver's side, back at the driver, the brake and the reverser
            // (dt screenshot --driver [--reverser s], note 445): the hand on the reverser as it's thrown.
            "driverside" => engine.Shape.Levers is { } driverLevers
                ? Camera.LookAt(engine.ToWorld(driverLevers.Reverser + new Double3(0.1, 0.95, -0.75)), engine.ToWorld(driverLevers.Reverser + new Double3(-0.35, 0.05, 0.3)), 70)
                : Camera.LookAt(engine.ToWorld(new Double3(0, 2.5, 0)), engine.ToWorld(new Double3(0, 2.5, -1)), 60),
            // The engine's front (note 311): low off its front quarter, the prow, the brow and the eye; and square off its
            // left side, the cab's run into the boiler.
            // (Not one of Names.) Close on the headlamp from up the line, a little off its axis: the Stella Maris in its cage (note 338).
            "headlamp" => Camera.LookAt(engine.ToWorld(new Double3(0.8, 2.2, -engineHalf - 3.4)), engine.ToWorld(new Double3(0, 1.95, -engineHalf)), 40),
            // The BBQ deck (note 698; `--upgrades bbqDeck`): from ahead and to the right, over its rail, the chairs and the grill;
            // and from the left-hand chair, sat, down the line.
            "bbq" => Camera.LookAt(engine.ToWorld(new Double3(4.6, 3.3, -engineHalf - 7.5)), engine.ToWorld(new Double3(-0.3, 1.8, -engineHalf - 1.4)), 55),
            "bbqparty" => Camera.LookAt(engine.ToWorld(new Double3(2.6, 3.0, -engineHalf - 4.6)), engine.ToWorld(new Double3(-0.4, 1.9, -engineHalf - 1.4)), 62),
            "bbqseat" => Camera.LookAt(engine.ToWorld(new Double3(-0.95, 2.45, -engineHalf - 2.0)), engine.ToWorld(new Double3(-0.6, 2.0, -engineHalf - 40)), 75),
            // The train's dressing (note 701; `--upgrades fuzzyDice,flames,...`): the back of the guard van from behind, low (the
            // stickers, the ribbon, the tiny caboose on its string); the engine's side (the flames down the hood, the hubcaps);
            // inside the guard van, across to the towels' rail with the disco ball turning over the aisle.
            "dressback" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(1.3, 2.0, train.Frames[^1].Shape.HalfLength + 5.0)),
                train.Frames[^1].ToWorld(new Double3(-0.1, 1.6, train.Frames[^1].Shape.HalfLength + 0.8)), 55),
            // The cab's side (note 714): the nameplate under its windows, the livery's lining past it.
            "nameplate" => engine.Shape.Cab is { } plateCab
                ? Camera.LookAt(engine.ToWorld(new Double3(4.2, 2.6, (plateCab.Min.Z + plateCab.Max.Z) / 2 - 1.5)), engine.ToWorld(new Double3(1.5, 2.3, (plateCab.Min.Z + plateCab.Max.Z) / 2)), 50)
                : Camera.LookAt(engine.ToWorld(new Double3(0, 2.5, 0)), engine.ToWorld(new Double3(0, 2.5, -1)), 60),
            // The yard photographer (note 714) and his picture: framed from where he stands past the gate, which only the
            // night knows (dt screenshot puts them there, YardPhotographer.Stand and .Lens); from here, off the engine's front.
            "photographer" or "yardphoto" => Camera.LookAt(engine.ToWorld(new Double3(5, 1.6, -engineHalf - 14)), engine.ToWorld(new Double3(0, 2.4, -engineHalf + 6)), 52),
            // The whimsies (note 715; `--upgrades gerald,gardenGnomes,canary,gramophone,calliope`): Gerald on the guard van's
            // roof from behind it; the gnomes down car 1's roof; the canary's cage in car 1; the gramophone in the guard van;
            // the calliope's pipes on the boiler.
            // The Gannet climbing off car 2's roof with Gerald (note 715; dt screenshot --gannet carry), from off the car's side.
            "gannetgerald" => Camera.LookAt(train.Frames[2].ToWorld(new Double3(-9, train.Frames[2].Shape.RoofHeight + 3, 4)),
                train.Frames[2].ToWorld(new Double3(0.3, train.Frames[2].Shape.RoofHeight + 2.5, -2.5)), 60),
            // The hammock and the hot tub (note 744; `--upgrades hammock,hotTub --lounge`): across the guard van's back end from
            // its middle; the tub on the engine's hood from over the cab's roof.
            "hammock" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(0.9, Floor(train) + 1.6, train.Frames[^1].Shape.HalfLength - 4.2)),
                train.Frames[^1].ToWorld(new Double3(-0.2, Floor(train) + 0.6, train.Frames[^1].Shape.HalfLength - 1.0)), 65),
            "hottub" => Camera.LookAt(engine.ToWorld(new Double3(2.6, engine.Shape.RoofHeight + 2.4, Sim.Train.CarShape.TubZ - 3.6)),
                engine.ToWorld(new Double3(0, engine.Shape.RoofHeight + 0.6, Sim.Train.CarShape.TubZ)), 55),
            // The yard cat and the dog (note 745; `--upgrades yardCat,dog --stocked [--pet-mood warn|sleep]`): the cab from its back
            // corner, the cat by the backhead; the guard van from its middle down to its back end, the dog there; the launcher on
            // the van's platform from behind, off its side (`--upgrades tennisBalls`).
            "cat" => engine.Shape.Cab is { } catCab && Sim.Train.Whimsy.Footplate(engine.Shape, catCab) is var plate
                ? Camera.LookAt(engine.ToWorld(new Double3(plate.Min.X + 0.5, catCab.Min.Y + 1.6, plate.Max.Z + 0.6)), engine.ToWorld(new Double3(plate.Max.X - 0.6, catCab.Min.Y + 0.2, plate.Max.Z - 1.2)), 70)
                : Camera.LookAt(engine.ToWorld(new Double3(0, 3, 0)), engine.ToWorld(new Double3(0, 2, -1)), 60),
            "dog" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(0.8, Floor(train) + 1.3, train.Frames[^1].Shape.HalfLength - 4.6)),
                train.Frames[^1].ToWorld(new Double3(-0.4, Floor(train) + 0.3, train.Frames[^1].Shape.HalfLength - 2.0)), 60),
            // The rear searchlight (note 899; `--upgrades rearSearchlight`): from the guard van's roof back down the line it
            // lights, as the rear gunner sees it; the lamp close, on the roof's back corner; and from off the line's side behind
            // the train, the van with its beam on the track.
            "searchlight" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(-0.6, train.Frames[^1].Shape.RoofHeight + 1.7, train.Frames[^1].Shape.HalfLength - 2.6)),
                train.Frames[^1].ToWorld(new Double3(0.4, 0.5, train.Frames[^1].Shape.HalfLength + 26)), 65),
            "searchlightlamp" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(train.Frames[^1].Shape.HalfWidth + 2.2, train.Frames[^1].Shape.RoofHeight + 1.2, train.Frames[^1].Shape.HalfLength - 2.4)),
                train.Frames[^1].ToWorld(new Double3(train.Frames[^1].Shape.HalfWidth - 0.4, train.Frames[^1].Shape.RoofHeight + 0.5, train.Frames[^1].Shape.HalfLength - 0.3)), 50),
            "searchlightside" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(-14, 6, train.Frames[^1].Shape.HalfLength + 22)),
                train.Frames[^1].ToWorld(new Double3(0, 1.5, train.Frames[^1].Shape.HalfLength + 8)), 60),
            "launcher" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(3.2, 2.4, train.Frames[^1].Shape.HalfLength + 4.5)),
                train.Frames[^1].ToWorld(new Double3(0.2, 1.6, train.Frames[^1].Shape.HalfLength + 0.6)), 50),
            "gerald" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(3.0, train.Frames[^1].Shape.RoofHeight + 1.6, -train.Frames[^1].Shape.HalfLength + 5.5)),
                train.Frames[^1].ToWorld(new Double3(0.8, train.Frames[^1].Shape.RoofHeight + 0.9, -train.Frames[^1].Shape.HalfLength + 1.6)), 50),
            "gnomes" => Camera.LookAt(train.Frames[1].ToWorld(new Double3(-2.4, train.Frames[1].Shape.RoofHeight + 1.6, -train.Frames[1].Shape.HalfLength - 1.5)),
                train.Frames[1].ToWorld(new Double3(0, train.Frames[1].Shape.RoofHeight + 0.2, 1.5)), 60),
            "canary" => Camera.LookAt(train.Frames[Sim.Train.Whimsy.CanaryCar(train)].ToWorld(Sim.Train.Whimsy.Cage(train) + new Double3(1.1, -0.35, 0.9)),
                train.Frames[Sim.Train.Whimsy.CanaryCar(train)].ToWorld(Sim.Train.Whimsy.Cage(train) + new Double3(0, -0.3, 0)), 55),
            "gramophone" => Camera.LookAt(train.Frames[^1].ToWorld(Sim.Train.Whimsy.Gramophone(train.Frames[^1].Shape) + new Double3(1.7, 1.55, 1.3)),
                train.Frames[^1].ToWorld(Sim.Train.Whimsy.Gramophone(train.Frames[^1].Shape) + new Double3(0.1, 0.95, 0)), 60),
            "calliope" => Camera.LookAt(engine.ToWorld(new Double3(2.6, engine.Shape.RoofHeight + 1.4, -4.6)), engine.ToWorld(new Double3(0, engine.Shape.RoofHeight + 0.4, -2)), 50),
            "dresside" => Camera.LookAt(engine.ToWorld(new Double3(6.5, 2.0, -2.5)), engine.ToWorld(new Double3(1.5, 1.7, 0.2)), 60),
            "dressvan" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(-1.0, 2.3, -2.2)), train.Frames[^1].ToWorld(new Double3(1.3, 2.4, 1.4)), 70),
            // The band (note 700; `--band`): from over the roof's back end, down between the two rows playing.
            "band" => Camera.LookAt(train.Frames[Math.Clamp(car, 1, train.Frames.Count - 1)].ToWorld(new Double3(1.6, train.Frames[Math.Clamp(car, 1, train.Frames.Count - 1)].Shape.RoofHeight + 2.4, 4.6)),
                train.Frames[Math.Clamp(car, 1, train.Frames.Count - 1)].ToWorld(new Double3(-0.2, train.Frames[Math.Clamp(car, 1, train.Frames.Count - 1)].Shape.RoofHeight + 1.0, -0.6)), 60),
            "prow" => Camera.LookAt(engine.ToWorld(new Double3(4.2, 1.9, -engineHalf - 6.5)), engine.ToWorld(new Double3(0, 2.5, -engineHalf + 2.2)), 52),
            // The way in from the train (the director, 7 Oct): on the left running board beside the boiler, a crewmate's eye,
            // looking forward to the cab's doorway.
            // In the corridor beside the boiler under the hood (note 338), forward to the cab through its back wall's opening.
            "wayin" => Camera.LookAt(engine.ToWorld(new Double3(-1.05, 3.0, -engineHalf + 13)), engine.ToWorld(new Double3(-1.0, 2.6, -engineHalf + 4)), 70),
            "prowside" => Camera.LookAt(engine.ToWorld(new Double3(-9, 2.8, -engineHalf + 3.5)), engine.ToWorld(new Double3(0, 2.6, -engineHalf + 4.5)), 55),
            // (Not one of Names.) Up off the engine's left side behind the cab, down onto the running board where it runs on in
            // front of the cab's doorway (the director, 9 Oct: "the platform ... should extend to be in front of the cab doors").
            "cabboard" => CabBoardCamera(train),
            // The run map, square on and close enough to read its figures (note 671); on the chart table, from over its driver's
            // side (note 716).
            "cabmap" => MapCamera(engine),
            // (Not one of Names.) The climber's eye going up the cab's hatch ladder (note 734), at the roof: up through the
            // opening, not through the roof (the director, 14 Oct: "it looks like we're actually going through").
            "cabclimb" => CabHatch(engine) is { } climb
                ? Camera.LookAt(engine.ToWorld(new Double3(climb.Foot.X, climb.Top - 0.1, climb.Foot.Z)), engine.ToWorld(new Double3(climb.Foot.X, climb.Top + 0.6, climb.Foot.Z + 1.5)), 75)
                : CabCamera(engine),
            // (Not one of Names.) The big cab, the crew car (note 716): from the back wall, a crewmate's eye, forward over the
            // gallery's benches and the chart table to the footplate and the front windows; dt screenshot --cabcrew for its crew.
            "cabhall" => CabHallCamera(engine),
            // Stood at the front of the gallery, a step up, forward over the chart table and the heads at the footplate, out of
            // the front windows down the line (the director: "Everybody wants to be where they can see the tracks").
            "gallery" => GalleryCamera(engine),
            // At the back wall (--kettle s, --cuckoo c): the kettle on its shelf, the cuckoo clock, the photo rail, the trophy
            // shelf and the crew's hooks, from the gallery's back tread.
            "backwall" => BackWallCamera(engine),
            // Square off the engine's left side, the whole of it: the short boiler still a steam engine's (smokebox, stack,
            // domes), the long cab with its gallery windows.
            "engineside" => Camera.LookAt(engine.ToWorld(new Double3(-17, 2.6, 0.5)), engine.ToWorld(new Double3(0, 2.4, 0.5)), 62),
            // (Not one of Names.) The cab's hatch from its roof (note 704; the director: "this ladder inside the cab should
            // actually go up to the roof"): standing open, its lid thrown back, the way down into the cab.
            "cabhatchroof" => CabHatch(engine) is { } hatch
                ? Camera.LookAt(engine.ToWorld(new Double3(hatch.Foot.X + 0.9, hatch.Top + 2.6, hatch.Foot.Z - 1.9)), engine.ToWorld(new Double3(hatch.Foot.X, hatch.Top, hatch.Foot.Z - 0.33)), 60)
                : CabCamera(engine),
            "ahead" => Camera.LookAt(engine.ToWorld(new Double3(1.5, 2.2, -engineHalf - 70)), engine.ToWorld(new Double3(0, 2.2, 0)), 55),
            "gap" => GapCamera(train, car),
            // (Not one of Names.) From a cargo car's right-hand side doorway, down at its steps (the director, 8 Oct: "an
            // awkward step"): the landing outside the door and the treads up to it from the front.
            "sidedoor" => SideDoorCamera(train, car),
            // (Not one of Names.) Off a car's right side at a crewman's eye on the ballast, a few metres out, its side door
            // ahead (the comet's green out of its seams, note 482).
            "carside" => CarSideCamera(train, car),
            // (Not one of Names.) Coming down the engine's rear ladder to the coupler plate onto car 1, looking down at it.
            "rearstep" => RearStepCamera(train),
            // (Not one of Names.) Off the train's left side, low, square on to the gap behind the engine (note 905): the ramp up
            // its coupler plate from car 1's floor to the engine's deck.
            "rampside" => RampSideCamera(train),
            // (Not one of Names.) Low off the side behind the engine's half of a cut train (dt screenshot --cut n), at the
            // coupler that was let go: its knuckle swung open, its hose hanging parted (T91).
            "cut" => CutCamera(train),
            // (Not one of Names.) Out on the ground off the last car of a cut train, behind it and to its left, at its end
            // and the train it's fallen behind (and what's riding it off: dt screenshot --cut n --hugger ride).
            "cutoff" => CutOffCamera(train),
            // (Not one of Names.) In this car's aisle, looking across and along its load side: a livestock car's pen and its
            // sheep (dt screenshot --cargo livestock), or whatever cases its cargo comes in.
            "pen" => Camera.LookAt(target.ToWorld(new Double3(-0.9, Floor(train) + 1.45, -target.Shape.HalfLength + 4.6)),
                target.ToWorld(new Double3(0.9, Floor(train) + 0.45, -target.Shape.HalfLength + 2.2)), 70),
            // (Not one of Names.) Crouched in the aisle at the pen's rail, at the sheep's faces: who they're looking at (note
            // 455; --unseen for them not looking).
            "penclose" => Camera.LookAt(target.ToWorld(new Double3(-0.55, Floor(train) + 1.1, -target.Shape.HalfLength + 3.6)),
                target.ToWorld(new Double3(0.7, Floor(train) + 0.6, -target.Shape.HalfLength + 3.0)), 75),
            // From over the car behind, down at a cargo car's roof hatch (T99): its lid, shut, or open down the side.
            "hatch" => Camera.LookAt(target.ToWorld(new Double3(4.2, roof + 2.2, 9.5)), target.ToWorld(new Double3(0.6, roof - 1.4, 3.2)), 70),
            // Over the last car's roof, looking back at its gun on its rail (T93).
            "gun" => GunCamera(train),
            // (Not one of Names.) Sat in the cannon's seat (note 137), the gunner's eye over the breech, along the barrel;
            // and off its side, close, the whole of it.
            "cannon" => CannonCamera(train, side: false),
            // (Not one of Names.) The engine's forward gun from its seat (note 596): what the gunner sees ahead of the train.
            "cannonfront" => CannonCamera(train, side: false, engine: true),
            // From the rear gun's seat, back down the line at the staged hound run (note 328, --run).
            "run" => RunCamera(train),
            "cannonside" => CannonCamera(train, side: true),
            // (Not one of Names.) The human cannonball (note 746; --upgrades humanCannonball --cannonball): the guard van's gun
            // off its side, someone crouched in its mouth and its gunner behind.
            "cannonball" => Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].HasGun, -1) is var cv and >= 0
                && Sim.Combat.Guns.Mount(train, cv) is { } cm
                ? Camera.LookAt(train.Frames[cv].ToWorld(cm.Position + new Double3(5.5, 1.2, cm.Facing.Z * 3.5)),
                    train.Frames[cv].ToWorld(cm.Position + new Double3(0, -0.2, cm.Facing.Z * 0.9)), 50)
                : CannonCamera(train, side: true),
            // (Not one of Names.) From the engine's forward gun's seat, down the line ahead in the headlamp: the lane ahead
            // (note 405; dt screenshot --run-ahead --view lane).
            "lane" => LaneCamera(train),
            // (Not one of Names.) From the forward gun's seat, out abeam the engine: its flank lane (note 443; dt screenshot
            // --run-flank --view laneside).
            "laneside" => LaneCamera(train, side: true),
            // (Not one of Names.) Down the aisle of the first cargo car at the face of its load, where the staged fire
            // burns (dt screenshot --threats --view fire: Staging.Threats' car fire, Effects.CarFire).
            "fire" => FireCamera(train),
            // (Not one of Names.) The dining car (note 959; dt screenshot --view dining, which fits it): from its front end
            // door down the aisle, the table laid under the chandelier and the bar at the far end; diningbar, at the counter;
            // diningtable (note 974), from the aisle across the places laid under the chandelier (--diners: the crew at it);
            // diningdrunk, close on whoever's at the counter (--diners: a drinker four in, lolling; --clock moves it on).
            "dining" or "diningbar" or "diningtable" or "diningdrunk" => DiningCamera(train, name),
            // (Not one of Names.) In the guard van at its back end, down at the stores World.Stock puts there: the crates,
            // the lamp and radios, the toys (App. C.4), and its extinguisher on the wall beyond them.
            "stores" => StoresCamera(train),
            // (Not one of Names.) In the gun car (the guard van) at its front end: the powder and shot locker in the corner
            // ahead of the tool lockers on the left (SceneArt.Fittings).
            "locker" => LockerCamera(train),
            // (Not one of Names.) In car 1, at its repair kit in the fitter's locker (World.RepairKitStowage; dt screenshot
            // --stocked; the locker's shut unless it's opened, as the lockers view does).
            "kit" => KitCamera(train),
            // (Not one of Names.) In car 1's aisle at the crew lockers (note 173): the row along the left wall, their grades on
            // their doors, the fitter's open on the repair kit (dt screenshot --stocked --view lockers opens it).
            "lockers" => LockersCamera(train),
            // (Not one of Names.) In this car, across the aisle at its extinguisher stood on its board (World.ExtinguisherMount;
            // with dt screenshot --stocked, its charge in the glass).
            "mount" => Camera.LookAt(target.ToWorld(new Double3(-0.05, Floor(train) + 1.3, -target.Shape.HalfLength + 2.95)),
                target.ToWorld(new Double3(-1.15, Floor(train) + 0.45, -target.Shape.HalfLength + 2.05)), 55),
            // (Not one of Names.) Off the engine's side, up at the tender under a coaling tower's spout (dt screenshot
            // --route tier:seed --coaling --view coaling: the chute pouring, Effects.CoalPour).
            // (Cab forward, note 276: the bunker's in the cab, its hatch in the cab roof.)
            "coaling" => Camera.LookAt(engine.ToWorld(new Double3(9, 3.5, BunkerZ(engine) + 2)), engine.ToWorld(new Double3(0, 5.5, BunkerZ(engine))), 60),
            // On the plate behind the engine, looking forward along the boiler's left side to the cab, and at the ladders
            // up the back onto the boiler and the cab roof (T90; note 276).
            "gangway" => Camera.LookAt(engine.ToWorld(new Double3(-0.2, 2.9, engineHalf + 1.4)), engine.ToWorld(new Double3(-1.5, 1.6, engineHalf - 10)), 75),
            _ => throw new ArgumentException($"unknown view '{name}' (known: {string.Join(", ", Names)})"),
        };
    }

    /// <summary>Standing in the cab beside the boiler, looking past it down the line.</summary>
    static Camera FliesCamera(TrainOnLine train)
    {
        var car = train.Frames[Math.Max(0, (train.Frames.Count - 1) / 2)];
        if (car.Shape.Interior is not { } room)
            return Camera.LookAt(car.ToWorld(new Double3(0, 2, 0)), car.ToWorld(new Double3(0, 2, -5)), 60);
        var lamp = Art.SceneArt.LampPositions(room).First();
        var at = new Double3(lamp.X, lamp.Y, lamp.Z);
        return Camera.LookAt(car.ToWorld(at + new Double3(0.8, -0.55, 0.75)), car.ToWorld(at + new Double3(0, -0.04, 0)), 45);
    }

    static Camera FireboxCamera(in CarFrame engine)
    {
        // Out from the door into the cab, in the firebox's frame (it faces back into the cab from the front wall: note 280).
        var frame = Art.TrainKit.BackheadFrame(engine.Shape);
        var door = Art.TrainKit.FireDoorLocal(engine.Shape);
        var eye = System.Numerics.Vector3.Transform(door + new System.Numerics.Vector3(0.12f, 0.22f, 0.8f), frame);
        var at = System.Numerics.Vector3.Transform(door, frame);
        return Camera.LookAt(engine.ToWorld(new Double3(eye.X, eye.Y, eye.Z)), engine.ToWorld(new Double3(at.X, at.Y, at.Z)), 60);
    }

    /// <summary>From behind the cab's work (note 280), forward at the coal, the fire door, the console and the line beyond.</summary>
    static Camera FiremanCamera(in CarFrame engine)
    {
        var cab = engine.Shape.Cab!.Value;
        var fire = engine.Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        var eye = new Double3(0.25, cab.Min.Y + 1.8, fire.Z + 1.7);
        return Camera.LookAt(engine.ToWorld(eye), engine.ToWorld(new Double3(0.05, cab.Min.Y + 0.95, fire.Z - 0.7)), 75);
    }

    /// <summary>The coal bunker's middle along the engine (its frame's Z).</summary>
    static double BunkerZ(in CarFrame engine) => engine.Shape.Solids.First(s => s.Part == PartKind.Tender).Box.Centre.Z;

    /// <summary>From one side of the cab at a standing eye, across at an interactable on the other side.</summary>
    static Camera SideCamera(in CarFrame engine, InteractableKind kind, int from)
    {
        var at = engine.Shape.Interactables.First(i => i.Kind == kind).Position;
        var eye = CabEye(engine.Shape, from) with { Z = at.Z + 0.6 };
        // Not stood in the coal (note 276: the bunker's along the left wall): at its face instead.
        foreach (var bunker in engine.Shape.Solids.Where(s => s.Part == PartKind.Tender))
            if (bunker.Box.ContainsXZ(eye))
                eye = eye with { X = bunker.Box.Max.X + 0.35 };
        return Camera.LookAt(engine.ToWorld(eye), engine.ToWorld(at + new Double3(0, 1.1, 0)), 70);
    }

    /// <summary>At a side door at the cab's front (note 856), from inside across the cab or from the ballast outside it.</summary>
    static Camera CabDoorCamera(in CarFrame engine, int side, bool inside)
    {
        var doors = engine.Shape.DoorList.Where(d => Math.Sign(d.Box.Centre.X) == side && d.Box.Max.Z - d.Box.Min.Z > d.Box.Max.X - d.Box.Min.X).ToList();
        if (doors.Count == 0)
            return CabCamera(engine);
        var c = doors[0].Box.Centre;
        double deck = doors[0].Box.Min.Y;
        return inside
            ? Camera.LookAt(engine.ToWorld(new Double3(-side * 0.9, deck + 1.65, c.Z + 1.7)), engine.ToWorld(new Double3(side * 1.3, deck + 0.95, c.Z - 0.7)), 75)
            : Camera.LookAt(engine.ToWorld(new Double3(side * 5.5, deck + 0.5, c.Z - 3.5)), engine.ToWorld(new Double3(side * 1.6, deck + 0.4, c.Z)), 55);
    }

    /// <summary>The cab's hatch ladder, up through the cab roof (note 704), if the engine has one.</summary>
    static Ladder? CabHatch(in CarFrame engine)
    {
        var shape = engine.Shape;
        return shape.Ladders.Where(l => shape.Cab is { } c && c.ContainsXZ(l.Foot) && DarkTerritory.Sim.Player.PlayerMotor.IsHatchLadder(shape, l))
            .Select(l => (Ladder?)l).FirstOrDefault();
    }

    static Camera CabCamera(in CarFrame engine, int side = 1)
    {
        var eye = CabEye(engine.Shape, side);
        return Camera.LookAt(engine.ToWorld(eye), engine.ToWorld(eye + new Double3(-side * 3, -2.5, -60)), 75);
    }

    /// <summary>
    /// A standing eye at the driver's place (T101), in the engine's frame: by the brake and the reverser on the right
    /// (<paramref name="side"/> 1), behind them at the front windows (note 276); −1 is across the cab from it.
    /// </summary>
    public static Double3 CabEye(CarShape engine, int side = 1)
    {
        var cab = engine.Cab!.Value;
        var levers = engine.Levers!.Value;
        // As far out as a player stands: against the cab side (its wall 0.1 thick, a player 0.3 round).
        return new Double3(side * (engine.HalfWidth - 0.4), cab.Min.Y + 1.75, levers.Reverser.Z + 0.55);
    }

    /// <summary>
    /// Standing in the coupling gap behind <paramref name="car"/>, on the car behind's side of it, looking at the end door the
    /// plate leads to (GDD §32 "readable gap and coupling danger"): the plate underfoot, the doorway, the ballast either side.
    /// </summary>
    static Camera GapCamera(TrainOnLine train, int car)
    {
        var ahead = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 2)];
        double l = ahead.Shape.HalfLength;
        return Camera.LookAt(ahead.ToWorld(new Double3(0.35, 2.75, l + 1.3)), ahead.ToWorld(new Double3(-0.4, 1.4, l)), 80);
    }

    static Camera RampSideCamera(TrainOnLine train)
    {
        var engine = train.Frames[0];
        double l = engine.Shape.HalfLength, gap = train.Dynamics.Tuning.Geometry.CouplingGap;
        return Camera.LookAt(engine.ToWorld(new Double3(-3.2, 1.9, l + gap / 2)), engine.ToWorld(new Double3(-0.3, 1.2, l + gap / 2)), 55);
    }

    static Camera RearStepCamera(TrainOnLine train)
    {
        var engine = train.Frames[0];
        double l = engine.Shape.HalfLength, gap = train.Dynamics.Tuning.Geometry.CouplingGap;
        var foot = engine.Shape.Ladders.Where(x => x.Foot.Z > 0 && Math.Abs(x.Inward.Z) > 0).Select(x => x.Foot).DefaultIfEmpty(new Double3(0.6, 0, l)).MinBy(f => f.Y);
        // Up the ladder at a hand's height over the deck, looking down and across at the footplate and the plate.
        return Camera.LookAt(engine.ToWorld(new Double3(foot.X + 0.1, 2.9, l + 0.3)), engine.ToWorld(new Double3(foot.X - 1.6, 1.1, l + gap * 0.4)), 75);
    }

    /// <summary>
    /// The left running board at the cab's doorway (note 559): from up off the engine's side behind the cab, at a crewmate's
    /// eye over the hood's eaves, looking forward and down onto the board where it passes the doorway and the steps under it.
    /// </summary>
    static Camera MapCamera(CarFrame engine)
    {
        var (corner, w, h) = Art.TrainKit.MapPlate(engine.Shape);
        var onCab = Art.TrainKit.MapToCab(engine.Shape);
        var plate = new Vector3(corner.X + w / 2, corner.Y + h / 2, corner.Z);
        var centre = D(Vector3.Transform(plate, onCab));
        // Out of the sheet (on the table, up) and down off its bottom edge (on the table, the driver's side).
        var outOf = D(Vector3.TransformNormal(Vector3.UnitZ, onCab));
        var down = D(Vector3.TransformNormal(-Vector3.UnitY, onCab));
        double back = Art.TrainKit.ChartTable(engine.Shape) is null ? 0.85 : 0.75;
        return Camera.LookAt(engine.ToWorld(centre + outOf * back + down * (Art.TrainKit.ChartTable(engine.Shape) is null ? 0.06 : 0.45)), engine.ToWorld(centre), 70);
    }

    /// <summary>The cab's eye height over a floor (m): a crewmate's.</summary>
    const double Eye = 1.7;

    static Camera CabHallCamera(CarFrame engine)
    {
        var cab = engine.Shape.Cab!.Value;
        double deck = cab.Min.Y + 0.1;
        return Camera.LookAt(engine.ToWorld(new Double3(0.55, deck + Eye + 0.05, cab.Max.Z - 0.45)), engine.ToWorld(new Double3(0, deck + 1.35, cab.Min.Z + 0.6)), 72);
    }

    static Camera GalleryCamera(CarFrame engine)
    {
        var cab = engine.Shape.Cab!.Value;
        double deck = cab.Min.Y + 0.1;
        var floor = Art.TrainKit.GalleryFloor(engine.Shape);
        double top = floor?.Max.Y ?? deck, z = floor is { } f ? f.Min.Z + 0.55 : cab.Max.Z - 1;
        return Camera.LookAt(engine.ToWorld(new Double3(0.25, top + Eye, z)), engine.ToWorld(new Double3(0, deck + 1.2, cab.Min.Z - 30)), 75);
    }

    static Camera BackWallCamera(CarFrame engine)
    {
        var cab = engine.Shape.Cab!.Value;
        double deck = cab.Min.Y + 0.1;
        double z = Art.TrainKit.GalleryFloor(engine.Shape)?.Max.Z ?? cab.Max.Z - 2.5;
        return Camera.LookAt(engine.ToWorld(new Double3(-0.35, deck + Eye + 0.1, z - 0.2)), engine.ToWorld(new Double3(0.1, deck + 1.55, cab.Max.Z - 0.15)), 80);
    }

    static Camera CabBoardCamera(TrainOnLine train)
    {
        var engine = train.Frames[0];
        var g = train.Dynamics.Tuning.Geometry;
        var plan = EnginePlan.Of(g);
        double w = engine.Shape.HalfWidth, deck = g.Engine.DeckHeight;
        return Camera.LookAt(engine.ToWorld(new Double3(-w - 3.2, deck + 2.6, plan.CabBack + 3.2)), engine.ToWorld(new Double3(-w - 0.3, deck - 0.2, plan.DoorFront)), 62);
    }

    static Camera CarSideCamera(TrainOnLine train, int car)
    {
        var at = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 1)];
        double w = at.Shape.HalfWidth;
        return Camera.LookAt(at.ToWorld(new Double3(w + 5.5, 1.7, 3.5)), at.ToWorld(new Double3(w, 1.9, -0.5)), 60);
    }

    static Camera SideDoorCamera(TrainOnLine train, int car)
    {
        var at = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 1)];
        double w = at.Shape.HalfWidth, floor = Floor(train);
        return Camera.LookAt(at.ToWorld(new Double3(w + 0.3, floor + 1.65, 0.5)), at.ToWorld(new Double3(w + 0.35, floor - 0.6, -1.3)), 75);
    }

    static Camera GapSideCamera(TrainOnLine train, int car)
    {
        var at = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 2)];
        double z = at.Shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap / 2, w = at.Shape.HalfWidth;
        return Camera.LookAt(at.ToWorld(new Double3(w + 2.6, 1.7, z + 0.6)), at.ToWorld(new Double3(0, 1.0, z)), 55);
    }

    // A crewmate's eye, over the ground they stand on (m).
    const double EyeHeight = 1.65;

    static Camera DaveCamera(TrainOnLine train, int shot)
    {
        var dave = Staging.DaveAt(train);
        var outward = ((Staging.Lineside(train, 18, -20) - dave) with { Y = 0 }).Normalized;
        var across = new Double3(outward.Z, 0, -outward.X);
        return shot switch
        {
            0 => Camera.LookAt(dave - outward * 1.7 + across * 0.9 + Double3.Up * 1.75, dave + outward * 0.8 + Double3.Up * 1.25, 55),
            1 => Camera.LookAt(Staging.Lineside(train, 2, -2.2) + Double3.Up * EyeHeight, dave + Double3.Up * 1.3, 50),
            3 => Camera.LookAt(dave + outward * 1.1 + across * 1.1 + Double3.Up * 1.65, dave + Double3.Up * 1.45, 40),
            _ => Camera.LookAt(dave + across * 3.6 - outward * 0.4 + Double3.Up * 1.6, dave + Double3.Up * 1.2 - outward * 0.3, 55),
        };
    }

    /// <summary>Note 723's hand-up (Staging.Rescue): from the ballast behind the runner and out, at the two against the lit door.</summary>
    static Camera HandUpCamera(TrainOnLine train)
    {
        var at = train.Frames[Staging.SideDoorCar(train)];
        double w = at.Shape.HalfWidth;
        return Camera.LookAt(at.ToWorld(new Double3(w + 4.2, 1.5, 3.6)), at.ToWorld(new Double3(w + 0.2, 1.5, -0.4)), 55);
    }

    static Camera MooseCamera(TrainOnLine train)
    {
        var eye = Staging.MooseWatcher(train) + Double3.Up * EyeHeight;
        var (at, _) = Staging.MooseAt(train, "warn");
        // At it, turned a little toward the engine and its lamp behind it.
        var engine = Staging.Lineside(train, 0, 0);
        return Camera.LookAt(eye, at + (engine - at) * 0.3 + Double3.Up * 1.7, 55);
    }

    static Camera GannetCamera(TrainOnLine train, string view)
    {
        var (eye, look) = Staging.GannetEye(train, view);
        return Camera.LookAt(eye, look, view switch { "gannet" => 64, "gannetfold" => 62, _ => 62 });
    }

    static Camera MooseChargeCamera(TrainOnLine train)
    {
        var them = Staging.MooseCrewmate(train, "charge");
        var (at, _) = Staging.MooseAt(train, "charge");
        // Across the line from them, side on to the charge between them and what's coming at them: its flank toward the
        // headlamp, the rack levelled in front of it, the gallop, and the one it's after.
        var eye = Staging.Lineside(train, 14, 3.5) + Double3.Up * EyeHeight;
        return Camera.LookAt(eye, them.Feet + (at - them.Feet) * 0.5 + Double3.Up * 1.4, 55);
    }

    static Camera TrailCamera(TrainOnLine train)
    {
        var at = train.Frames[Math.Max(0, (train.Frames.Count - 1) / 2)];
        double z = at.Shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap / 2, w = at.Shape.HalfWidth;
        return Camera.LookAt(at.ToWorld(new Double3(-w - 0.6, 3.4, z - 6)), at.ToWorld(new Double3(-Staging.NestOut, 0.3, z)), 50);
    }

    static Camera NestCamera(TrainOnLine train)
    {
        var at = train.Frames[Math.Max(0, (train.Frames.Count - 1) / 2)];
        double z = at.Shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap / 2;
        return Camera.LookAt(at.ToWorld(new Double3(-Staging.NestOut + 4.2, 2.4, z - 3.4)), at.ToWorld(new Double3(-Staging.NestOut, 0.0, z)), 55);
    }

    static Camera CarryCamera(TrainOnLine train)
    {
        var at = train.Frames[Math.Max(0, (train.Frames.Count - 1) / 2)];
        double z = at.Shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap / 2;
        return Camera.LookAt(at.ToWorld(new Double3(-Staging.CarryOut - 1.8, 1.4, z - 5)), at.ToWorld(new Double3(-Staging.CarryOut - 0.2, 1.05, z)), 55);
    }

    static double Floor(TrainOnLine train) => train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 1.1;

    static Camera DoorCamera(TrainOnLine train, int car)
    {
        var at = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 2)];
        double l = at.Shape.HalfLength, floor = Floor(train), x = train.Dynamics.Tuning.Geometry.PlateX;
        double gap = train.Dynamics.Tuning.Geometry.CouplingGap;
        return Camera.LookAt(at.ToWorld(new Double3(x + 0.1, floor + 1.6, l + gap + 1.6)), at.ToWorld(new Double3(x, floor + 1.15, l - 0.4)), 60);
    }

    static Camera GunCamera(TrainOnLine train)
    {
        var last = train.Frames[^1];
        double roof = last.Shape.RoofHeight;
        return Camera.LookAt(last.ToWorld(new Double3(1.6, roof + 2.2, -2)), last.ToWorld(new Double3(0, roof + 0.4, last.Shape.HalfLength - 2.5)), 60);
    }

    static Camera DiningCamera(TrainOnLine train, string name)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).FirstOrDefault(i => train.Vehicles[i].Kind == Sim.Train.VehicleKind.Dining, 1);
        var f = train.Frames[v];
        if (f.Shape.Dining is not { } d)
            return FireCamera(train);
        double floor = d.Table.Min.Y, z0 = f.Shape.Interior!.Value.Min.Z, mid = (d.Table.Min.Z + d.Table.Max.Z) / 2;
        return name switch
        {
            "diningbar" => Camera.LookAt(f.ToWorld(new Double3(-0.6, floor + 1.6, d.Counter.Min.Z - 1.6)), f.ToWorld(new Double3(0.8, floor + 1.2, d.Counter.Max.Z + 0.6)), 60),
            "diningdrunk" => Camera.LookAt(f.ToWorld(d.Bar + new Double3(-1.15, 1.7, 0.5)), f.ToWorld(d.Bar + new Double3(0, 1.45, -0.05)), 40),
            "diningtable" => Camera.LookAt(f.ToWorld(new Double3(-0.95, floor + 1.7, mid + 1.9)), f.ToWorld(new Double3(0.5, floor + 1.05, mid - 0.5)), 62),
            _ => Camera.LookAt(f.ToWorld(new Double3(-0.6, floor + 1.65, z0 + 0.9)), f.ToWorld(new Double3(0.3, floor + 1.1, d.Counter.Min.Z)), 70),
        };
    }

    static Camera FireCamera(TrainOnLine train)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).FirstOrDefault(i => train.Vehicles[i].Kind == Sim.Train.VehicleKind.Cargo, 1);
        var f = train.Frames[v];
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 1.1;
        return Camera.LookAt(f.ToWorld(new Double3(-0.55, floor + 1.6, 5.2)), f.ToWorld(new Double3(0.5, floor + 0.9, 1.5)), 70);
    }

    /// <summary>The speaking tube's end <paramref name="end"/> (note 720), from a couple of metres into the room, a little along.</summary>
    static Camera TubeCamera(TrainOnLine train, int end)
    {
        var ends = Sim.Train.SpeakingTube.Ends(train);
        if (end >= ends.Count)
            return CabCamera(train.Frames[0]);
        var e = ends[end];
        var f = train.Frames[e.Car];
        var into = e.Facing * 0.8;
        return Camera.LookAt(f.ToWorld(e.Mouth + into + new Double3(0, 0.2, 1.0)), f.ToWorld(e.Mouth + new Double3(0, -0.1, 0)), 55);
    }

    static Camera StoresCamera(TrainOnLine train)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].Kind == Sim.Train.VehicleKind.Guard, train.Vehicles.Count - 1);
        var f = train.Frames[v];
        double floor = Floor(train), l = f.Shape.HalfLength;
        return Camera.LookAt(f.ToWorld(new Double3(-0.2, floor + 1.55, l - 0.5)), f.ToWorld(new Double3(0.2, floor + 0.2, l - 2.6)), 75);
    }

    static Camera CutCamera(TrainOnLine train)
    {
        var rake = train.Rakes.FirstOrDefault(r => r.Consist.HasEngine) ?? train.Rakes[0];
        var f = train.Frames[rake.Consist.Vehicles[^1].Id];
        double l = f.Shape.HalfLength;
        return Camera.LookAt(f.ToWorld(new Double3(0.9, 1.35, l + 1.5)), f.ToWorld(new Double3(0.05, 0.9, l + 0.45)), 45);
    }

    static Camera CutOffCamera(TrainOnLine train)
    {
        var gone = train.Frames[^1];
        double l = gone.Shape.HalfLength;
        return Camera.LookAt(gone.ToWorld(new Double3(-5.5, 1.7, l + 7.5)), gone.ToWorld(new Double3(0, 1.3, l - 2.5)), 50);
    }

    static Camera KitCamera(TrainOnLine train)
    {
        if (Sim.World.RepairKitCar(train) is not { } v || train.Frames[v].Shape.Interior is not { } room)
            return LockerCamera(train);
        var f = train.Frames[v];
        var kit = Sim.World.RepairKitStowage(f.Shape, room);
        // In its locker: from the aisle, close, down into it.
        if (Sim.World.KitLocker(f.Shape) is { } bay)
            return Camera.LookAt(f.ToWorld(kit + new Double3(1.25, 1.3, 0.35)), f.ToWorld(kit + new Double3(0, 0.25, 0)), 55);
        // From the aisle at the load's front end, looking across and down at it in its corner by the door.
        return Camera.LookAt(f.ToWorld(kit + new Double3(-1.45, 1.35, 0.5)), f.ToWorld(kit + new Double3(0, 0.1, -0.05)), 60);
    }

    /// <summary>
    /// The crew lockers (note 173): from the aisle at the load's face, square on to the fitter's open locker, so its door
    /// stands out of the way to the left and its neighbours' plates read either side of it.
    /// </summary>
    public static Camera LockersCamera(TrainOnLine train)
    {
        if (Sim.World.KitLocker(train) is not { } at)
            return LockerCamera(train);
        var f = train.Frames[at.Car];
        var box = at.Bay.Box;
        double floor = box.Min.Y;
        return Camera.LookAt(f.ToWorld(new Double3(0.22, floor + 1.5, box.Centre.Z + 0.12)), f.ToWorld(new Double3(box.Max.X, floor + 1.0, box.Centre.Z - 0.02)), 72);
    }

    static Camera LockerCamera(TrainOnLine train)
    {
        int v = Enumerable.Range(1, train.Vehicles.Count - 1).FirstOrDefault(i => train.Vehicles[i].HasGun && train.Frames[i].Shape.Interior is not null, train.Vehicles.Count - 1);
        var f = train.Frames[v];
        double floor = Floor(train), l = f.Shape.HalfLength;
        return Camera.LookAt(f.ToWorld(new Double3(0.1, floor + 1.5, -l + 1.5)), f.ToWorld(new Double3(-0.95, floor + 0.2, -l + 0.6)), 65);
    }

    /// <summary>
    /// The hound run's view (note 328, --run): off the line's left, back past the last of the staged runners, looking up the
    /// line at them closing on the train's rear and its gun.
    /// </summary>
    static Camera RunCamera(TrainOnLine train)
    {
        Double3 At(double behind, double lateral, double height)
        {
            var t = train.Line.Sample(train.Dynamics.Path, train.Dynamics.RearDistance - behind);
            return t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral + Double3.Up * height;
        }
        return Camera.LookAt(At(48, -9, 3.2), At(4, 1, 1.6), 60);
    }

    static Camera LaneCamera(TrainOnLine train, bool side = false)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).FirstOrDefault(i => train.Vehicles[i].Gun is { Mounted: true, Facing: < 0 }, -1);
        if (v < 0 || Sim.Combat.Guns.Mount(train, v) is not { } mount)
            return GunCamera(train);
        var f = train.Frames[v];
        var p = mount.Position;
        var seat = Art.TrainKit.CannonSeat;
        var eye = new Double3(p.X, p.Y + seat.Y + 0.78, p.Z + seat.Z);
        if (side)
        {
            // Abeam, a little back toward the first car, where the lane runs in.
            var from = f.ToWorld(eye + new Double3(0, 0.9, 0));
            var t = train.Line.Sample(train.Dynamics.Path, train.Dynamics.Distance - 14);
            return Camera.LookAt(from, t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 30 + Double3.Up * 0.4, 70);
        }
        var ahead = train.Line.Sample(train.Dynamics.Path, train.Dynamics.Distance + 95).Position;
        return Camera.LookAt(f.ToWorld(eye + new Double3(0, 0.9, 0)), ahead + Double3.Up * 0.6, 20);
    }

    static Camera CannonCamera(TrainOnLine train, bool side, bool engine = false)
    {
        int v = engine ? 0 : Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].HasGun, -1);
        if (v < 0 || Sim.Combat.Guns.Mount(train, v) is not { } mount)
            return GunCamera(train);
        var f = train.Frames[v];
        var p = mount.Position;
        double dir = mount.Facing.Z;             // the barrel's way along the car (−1: towards the engine)
        var seat = Art.TrainKit.CannonSeat;
        // The seat is behind the breech: back along the car from the pivot, its height under it.
        // The seated eye (Eyes.Seated over the roof the seat stands on; note 596: over the shield, not through its slot).
        var eye = new Double3(p.X, p.Y - 0.9 + Eyes.Seated, p.Z - dir * seat.Z);
        return side
            ? Camera.LookAt(f.ToWorld(new Double3(p.X + 2.2, p.Y + 0.6, p.Z + dir * 0.4)), f.ToWorld(new Double3(p.X, p.Y - 0.25, p.Z + dir * 0.1)), 50)
            : Camera.LookAt(f.ToWorld(eye), f.ToWorld(new Double3(p.X, p.Y + 0.1, p.Z + dir * 12)), 65);
    }

    /// <summary>
    /// The derailment's camera (T117): pulled out and up over the wreck, turning slowly round it, close at first and drawing
    /// back as it spreads, looking at the middle of the pile with the engine in it.
    /// </summary>
    public static Camera Wreck(Sim.Train.Wreck wreck, double seconds)
    {
        var centre = Double3.Zero;
        foreach (var b in wreck.Bodies)
            centre += b.Centre;
        centre *= 1.0 / Math.Max(1, wreck.Bodies.Count);
        double spread = wreck.Bodies.Max(b => (b.Centre - centre).Length);
        var engine = wreck.Bodies[0].Centre;
        // Close in on the engine end, where it's worst; through the fog a wide shot of the whole train reads as nothing.
        var look = Double3.Lerp(centre, engine, 0.55);
        double angle = 0.9 + seconds * 0.11, distance = 12 + spread * 0.4 + Math.Min(seconds, 8) * 0.8;
        var eye = look + new Double3(Math.Sin(angle) * distance, 5 + spread * 0.1 + Math.Min(seconds, 6) * 0.5, Math.Cos(angle) * distance);
        return Camera.LookAt(eye, look, 55);
    }

    /// <summary>
    /// GDD v1.4 App. E.9, the Stranded outro: on where the repair kit should be (E.9's "empty engineering-kit rack": the
    /// fitter's locker in car 1, standing open on its empty shelf, ARCHITECTURE notes 150, 169), then up and back over the
    /// stopped consist to a high wide. "§6's small glowing machine in an enormous black world, going dark."
    /// </summary>
    public static Camera Stranded(TrainOnLine train, Sim.Train.StrandedOutroTuning t, double seconds)
    {
        Double3 rackAt, rackEye;
        if (Sim.World.KitLocker(train) is { } locker)
        {
            // From the aisle, a step back from its open door (GreyboxScene.KitLockerOpen), into the bare locker.
            var frame = train.Frames[locker.Car];
            var box = locker.Bay.Box;
            rackAt = frame.ToWorld(new Double3(box.Centre.X, box.Min.Y + 0.95, box.Centre.Z));
            rackEye = frame.ToWorld(new Double3(box.Max.X + 1.25, box.Min.Y + 1.7, box.Max.Z + 0.75));
        }
        else if (Sim.World.RepairKitCar(train) is { } car && train.Frames[car].Shape.Interior is { } room)
        {
            // From across the aisle by the front door (the load is behind it), looking down at the bare boards where it's kept.
            var frame = train.Frames[car];
            var spot = Sim.World.RepairKitStowage(frame.Shape, room);
            rackAt = frame.ToWorld(spot + new Double3(0, 0.1, 0));
            rackEye = frame.ToWorld(spot + new Double3(-Math.Sign(spot.X == 0 ? 1 : spot.X) * 1.1, 1.5, -0.45));
        }
        else
        {
            // No kit's car: the cab's tool rack, across the cab at a standing eye (TrainKit.ToolRack: the pegs at 0.7).
            var cab = train.Frames[0];
            var rack = cab.Shape.Interactables.Where(i => i.Kind == InteractableKind.ToolRack).Select(i => (Double3?)i.Position).FirstOrDefault() ?? new Double3(0.8, 1.4, 0);
            double side = Math.Sign(rack.X == 0 ? 1 : rack.X);
            rackAt = cab.ToWorld(rack + new Double3(side * 0.12, 0.72, 0));
            rackEye = cab.ToWorld(rack + new Double3(-side * 1.25, 1.45, 0.55));
        }
        if (seconds <= t.RackSeconds)
            return Camera.LookAt(rackEye, rackAt, 50);
        double u = Math.Clamp((seconds - t.RackSeconds) / t.PullBackSeconds, 0, 1);
        u = u * u * (3 - 2 * u);
        var mid = Middle(train);
        var middle = mid.ToWorld(new Double3(0, 1, 0));
        var wide = middle + mid.Back * t.BackM + mid.Right * (t.BackM * 0.6) + Double3.Up * t.HeightM;
        return Camera.LookAt(Double3.Lerp(rackEye, wide, u), Double3.Lerp(rackAt, middle, Math.Min(1, u * 1.6)), (float)(50 + 10 * u));
    }

    /// <summary>
    /// The middle of the crew's own train, the engine's rake (note 661): not of every vehicle on the line, which counts a
    /// yard's standing cars and the derelicts on sidings and put the director's stranded outro on a train standing in a town.
    /// </summary>
    static Sim.Train.CarFrame Middle(TrainOnLine train)
    {
        var rake = train.Dynamics.Consist.Vehicles;
        return train.Frames[rake[rake.Count / 2].Id];
    }

    /// <summary>
    /// App. E.4 O12, the cinematic's light rig: the fog pushed out to at least twice the shot's distance, so what it's of
    /// reads through the night.
    /// </summary>
    public static void CinematicFog(ref FrameLighting light, double shotDistance) =>
        light.FogDensity = Math.Min(light.FogDensity, (float)(1 / (2 * Math.Max(1, shotDistance))));

    /// <summary>E.9's shot distance at <paramref name="seconds"/>: the camera to the middle of the train.</summary>
    public static double StrandedDistance(TrainOnLine train, Sim.Train.StrandedOutroTuning t, double seconds) =>
        (Stranded(train, t, seconds).Position - Middle(train).Origin).Length;

    /// <summary>E.9: how many of the train's lamps are out, from the last car forward (the engine's last of all).</summary>
    public static int StrandedLampsOut(int cars, Sim.Train.StrandedOutroTuning t, double seconds) =>
        seconds <= t.RackSeconds ? 0 : (int)Math.Floor(Math.Clamp((seconds - t.RackSeconds) / t.PullBackSeconds, 0, 1) * (cars + 0.999));

    // Over the shoulder of Staging.Crew's first crewmate, on car 2's roof facing back down it.
    static Camera StagedShoulder(TrainOnLine train)
    {
        var car = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var s = new Sim.Player.PlayerState { Parent = car.Index, Position = new Double3(-0.9, car.Shape.RoofHeight, -5), Yaw = Math.PI + 0.3 };
        var (feet, yaw) = Eyes.World(s, train.Frames);
        return DeadViews.Shoulder(feet, yaw);
    }

    static Camera ChaseCamera(TrainOnLine train) => Chase(Train(train.Frames, train.StandingCar));

    /// <summary>
    /// The train's own frames: not a switchyard's cars standing on their sidings (note 187), which sit at the end of the
    /// frames kilometres off, where a "last car" would put the camera.
    /// </summary>
    public static CarFrame[] Train(IEnumerable<CarFrame> frames, Func<int, bool> standing) => [.. frames.Where(f => !standing(f.Index))];

    /// <summary>The chase view over these frames (the train's own: <see cref="Train"/>): up and back off the last car, on the middle of the train.</summary>
    public static Camera Chase(IReadOnlyList<CarFrame> frames)
    {
        var last = frames[^1];
        var mid = frames[frames.Count / 2];
        return Camera.LookAt(last.ToWorld(new Double3(-12, 14, last.Shape.HalfLength + 30)), mid.ToWorld(new Double3(0, 2, 0)), 60);
    }

    public static FrameLighting Lighting(TrainOnLine train, Look? look = null, float dawn = 0, float stir = 0) =>
        Lighting(train.Frames[0], look, dawn, stir, train.Frames[train.Dynamics.Consist.Vehicles[^1].Id].Back with { Y = 0 },
            train.Dynamics.Tuning.HeadlampReach);

    /// <summary>
    /// The night's fog where the engine is (linegen plan §14; note 313): the route's density times the stretch's factor,
    /// thicker in the low ground and by water, thinner on a crest, blended along the line so it comes and goes. The crew
    /// are all within a train's length of it, or out at a stop it's standing at.
    /// </summary>
    public static float FogDensity(Sim.Route.Route route, TrainOnLine train) =>
        (float)(route.Weather.FogDensity * (train.Line.Conditions?.Fog(train.Dynamics.Path, train.Dynamics.Distance) ?? 1));

    /// <param name="look">The art pass's atmosphere (look.json) over the night's defaults, when there is one.</param>
    /// <param name="dawn">How far the dawn's come up (0..1, <see cref="Look.DawnOf"/>).</param>
    /// <param name="stir">How far the Wakers' stir has come (0..1, <see cref="DawnStir.Sky"/>), lying toward <paramref name="stirFrom"/>.</param>
    /// <param name="reach">The train's headlamp reach (train.json <c>headlampReach</c>, note 506): the lamp brightness upgrade's
    /// beam goes further down the line, and brighter.</param>
    public static FrameLighting Lighting(in CarFrame engine, Look? look = null, float dawn = 0, float stir = 0, Double3 stirFrom = default,
        double reach = 1)
    {
        var light = look?.Apply(FrameLighting.Night) ?? FrameLighting.Night;
        if (look is not null)
            light = look.Stir(look.Dawn(light, dawn), stir, new Vector3((float)stirFrom.X, 0, (float)stirFrom.Z));
        light.LampRange *= (float)reach;
        light.LampIntensity *= (float)reach;
        light.LampPosition = Sim.World.LampPosition(engine);
        var fwd = engine.Back * -1;
        light.LampDirection = Vector3.Normalize(new Vector3((float)fwd.X, (float)fwd.Y - 0.04f, (float)fwd.Z));
        return light;
    }

    static Double3 D(Vector3 v) => new(v.X, v.Y, v.Z);
}
