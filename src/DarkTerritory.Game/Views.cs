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
            // Inside the car, at its front end, looking back down the aisle past the cargo.
            // Note 301: a battered car's dent on its left wall and a breach in its rear end wall, their callouts (with
            // --integrity and --breached).
            // Slice 2: the engine's dent on its boiler's left flank, from out beside the running board (with --integrity).
            "mendengine" => Repairs.DentAt(train, 0) is { } ed
                ? Camera.LookAt(engine.ToWorld(ed + new Double3(-3.2, 0.9, 2.6)), engine.ToWorld(ed), 60)
                : Camera.LookAt(engine.ToWorld(new Double3(-4, 2.5, 3)), engine.ToWorld(new Double3(0, 2, 0)), 60),
            "mend" => Camera.LookAt(target.ToWorld(new Double3(0.4, Floor(train) + 1.65, -0.5)), target.ToWorld(new Double3(-1.0, Floor(train) + 1.1, target.Shape.HalfLength * 0.62)), 70),
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
            // (Not one of Names.) Dave (note 487): over his shoulder at his canvas, close; from the engine's front as the crew
            // would first see him, 30 m off in the dark with his lantern; and side on to him with crewmate 4 (--dave warn, grab).
            "dave" => DaveCamera(train, 0),
            "davefar" => DaveCamera(train, 1),
            "davewarn" => DaveCamera(train, 2),
            "daveface" => DaveCamera(train, 3),
            // Over crewmate 4's shoulder out in front of the engine, at the staged Moose squaring up to them, coming at them,
            // or on them (Staging.Moose: squareup, charge, pin...).
            "moosecharge" => MooseChargeCamera(train),
            // (Not one of Names.) A crewmate's eye on the second car's roof over the moving train, at the staged Gannet
            // (Staging.Gannet): up at it soaring in the engine's smoke; diving at the walker ahead; its beak in the planks;
            // stood on someone, mantled.
            "gannet" or "gannetfold" or "gannetstuck" or "gannetpin" => GannetCamera(train, name),
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
            "crewside" => Camera.LookAt(target.ToWorld(new Double3(2.6, roof + 1.9, -4.2)), target.ToWorld(new Double3(-0.8, roof + 1.45, -5.6)), 45),
            // (Not one of Names.) Down on the roof round the first two of the staged crew (dt screenshot --act lantern,...), close
            // over their feet: what the hand lamp's light falls on and the shadows it throws (note 436).
            "lampshadow" => Camera.LookAt(target.ToWorld(new Double3(2.2, roof + 2.3, -2.2)), target.ToWorld(new Double3(-0.2, roof + 0.3, -5.6)), 60),
            "cab" => CabCamera(engine),
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
            // On the line 47 m ahead of the engine, at the staged Switchman by its lever 8 m on (Staging.Threats).
            "switchman" => Camera.LookAt(engine.ToWorld(new Double3(1.6, 1.8, -engineHalf - 50.5)), engine.ToWorld(new Double3(3.8, 1.1, -engineHalf - 55)), 50),
            // (Not one of Names.) Off the engine's right side ahead of it, the whole of it three-quarters on (note 276, the cab
            // forward): the cab and its lamp leading, the boiler and the stack behind, car 1 coupled on.
            // The boiler's left flank, where it tears when it ruptures (TrainKit.RuptureSeam; dt screenshot --ruptured).
            "rupture" => Camera.LookAt(engine.ToWorld(new Double3(-14, 2.4, -engineHalf + 1)), engine.ToWorld(new Double3(-0.7, 3.0, 0.5)), 60),
            "engine" => Camera.LookAt(engine.ToWorld(new Double3(8.5, 3.2, -engineHalf - 6)), engine.ToWorld(new Double3(0, 2.2, 1)), 55),
            // (Not one of Names.) On the cab floor, up at the ladder and the hatch it goes up to (the director, 8 Oct: "ladder
            // in cab to nowhere").
            "cabhatch" => engine.Shape.Ladders.FirstOrDefault(d => d.Foot.Y > 0.2 && d.Inward.Z > 0) is { Top: > 0 } hatchLadder
                ? Camera.LookAt(engine.ToWorld(hatchLadder.Foot + new Double3(1.2, 1.5, -1.3)), engine.ToWorld(hatchLadder.Foot + new Double3(0, 2.7, -0.33)), 70)
                : Camera.LookAt(engine.ToWorld(new Double3(0, 2.5, 0)), engine.ToWorld(new Double3(0, 2.5, -1)), 60),
            // (Not one of Names.) From over car 1's front end, a crewmate's eye up on the roofs, forward along the hood to the
            // whistle on it: its valve lever pulled down by its rod from the cab while a crewmate blows it (note 445).
            "whistlepull" => Camera.LookAt(engine.ToWorld(new Double3(1.4, engine.Shape.Bounds.Max.Y + 1.3, Art.TrainKit.WhistleZ(engine.Shape) + 5.5)),
                engine.ToWorld(new Double3(0.3, engine.Shape.Bounds.Max.Y + 0.3, Art.TrainKit.WhistleZ(engine.Shape))), 45),
            // (Not one of Names.) Close on the whistle's lever and its rod forward along the roof (note 445).
            "whistlelever" => Camera.LookAt(engine.ToWorld(new Double3(1.5, engine.Shape.Bounds.Max.Y + 0.8, Art.TrainKit.WhistleZ(engine.Shape) + 1.4)),
                engine.ToWorld(new Double3(0.4, engine.Shape.Bounds.Max.Y + 0.4, Art.TrainKit.WhistleZ(engine.Shape) - 0.6)), 50),
            // (Not one of Names.) In the cab's front corner on the driver's side, back at the driver, the brake and the reverser
            // (dt screenshot --driver [--reverser s], note 445): the hand on the reverser as it's thrown.
            "driverside" => engine.Shape.Levers is { } driverLevers
                ? Camera.LookAt(engine.ToWorld(driverLevers.Reverser + new Double3(0.1, 0.95, -0.75)), engine.ToWorld(driverLevers.Reverser + new Double3(-0.35, 0.05, 0.3)), 70)
                : Camera.LookAt(engine.ToWorld(new Double3(0, 2.5, 0)), engine.ToWorld(new Double3(0, 2.5, -1)), 60),
            // The engine's front (note 311): low off its front quarter, the prow, the brow and the eye; and square off its
            // left side, the cab's run into the boiler.
            // (Not one of Names.) Close on the headlamp from up the line, a little off its axis: the Stella Maris in its cage (note 338).
            "headlamp" => Camera.LookAt(engine.ToWorld(new Double3(0.8, 2.2, -engineHalf - 3.4)), engine.ToWorld(new Double3(0, 1.95, -engineHalf)), 40),
            "prow" => Camera.LookAt(engine.ToWorld(new Double3(4.2, 1.9, -engineHalf - 6.5)), engine.ToWorld(new Double3(0, 2.5, -engineHalf + 2.2)), 52),
            // The way in from the train (the director, 7 Oct): on the left running board beside the boiler, a crewmate's eye,
            // looking forward to the cab's doorway.
            // In the corridor beside the boiler under the hood (note 338), forward to the cab through its back wall's opening.
            "wayin" => Camera.LookAt(engine.ToWorld(new Double3(-1.05, 3.0, -engineHalf + 13)), engine.ToWorld(new Double3(-1.0, 2.6, -engineHalf + 4)), 70),
            "prowside" => Camera.LookAt(engine.ToWorld(new Double3(-9, 2.8, -engineHalf + 3.5)), engine.ToWorld(new Double3(0, 2.6, -engineHalf + 4.5)), 55),
            "ahead" => Camera.LookAt(engine.ToWorld(new Double3(1.5, 2.2, -engineHalf - 70)), engine.ToWorld(new Double3(0, 2.2, 0)), 55),
            "gap" => GapCamera(train, car),
            // (Not one of Names.) From a cargo car's right-hand side doorway, down at its steps (the director, 8 Oct: "an
            // awkward step"): the landing outside the door and the treads up to it from the front.
            "sidedoor" => SideDoorCamera(train, car),
            // (Not one of Names.) Coming down the engine's rear ladder to the coupler plate onto car 1, looking down at it.
            "rearstep" => RearStepCamera(train),
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
            // From the rear gun's seat, back down the line at the staged hound run (note 328, --run).
            "run" => RunCamera(train),
            "cannonside" => CannonCamera(train, side: true),
            // (Not one of Names.) From the engine's forward gun's seat, down the line ahead in the headlamp: the lane ahead
            // (note 405; dt screenshot --run-ahead --view lane).
            "lane" => LaneCamera(train),
            // (Not one of Names.) From the forward gun's seat, out abeam the engine: its flank lane (note 443; dt screenshot
            // --run-flank --view laneside).
            "laneside" => LaneCamera(train, side: true),
            // (Not one of Names.) Down the aisle of the first cargo car at the face of its load, where the staged fire
            // burns (dt screenshot --threats --view fire: Staging.Threats' car fire, Effects.CarFire).
            "fire" => FireCamera(train),
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

    static Camera RearStepCamera(TrainOnLine train)
    {
        var engine = train.Frames[0];
        double l = engine.Shape.HalfLength, gap = train.Dynamics.Tuning.Geometry.CouplingGap;
        var foot = engine.Shape.Ladders.Where(x => x.Foot.Z > 0 && Math.Abs(x.Inward.Z) > 0).Select(x => x.Foot).DefaultIfEmpty(new Double3(0.6, 0, l)).MinBy(f => f.Y);
        // Up the ladder at a hand's height over the deck, looking down and across at the footplate and the plate.
        return Camera.LookAt(engine.ToWorld(new Double3(foot.X + 0.1, 2.9, l + 0.3)), engine.ToWorld(new Double3(foot.X - 1.6, 1.1, l + gap * 0.4)), 75);
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

    static Camera FireCamera(TrainOnLine train)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).FirstOrDefault(i => train.Vehicles[i].Kind == Sim.Train.VehicleKind.Cargo, 1);
        var f = train.Frames[v];
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 1.1;
        return Camera.LookAt(f.ToWorld(new Double3(-0.55, floor + 1.6, 5.2)), f.ToWorld(new Double3(0.5, floor + 0.9, 1.5)), 70);
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

    static Camera CannonCamera(TrainOnLine train, bool side)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].HasGun, -1);
        if (v < 0 || Sim.Combat.Guns.Mount(train, v) is not { } mount)
            return GunCamera(train);
        var f = train.Frames[v];
        var p = mount.Position;
        double dir = mount.Facing.Z;             // the barrel's way along the car (−1: towards the engine)
        var seat = Art.TrainKit.CannonSeat;
        // The seat is behind the breech: back along the car from the pivot, its height under it.
        var eye = new Double3(p.X, p.Y + seat.Y + 0.78, p.Z - dir * seat.Z);
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
        var mid = train.Frames[train.Frames.Count / 2];
        var middle = mid.ToWorld(new Double3(0, 1, 0));
        var wide = middle + mid.Back * t.BackM + mid.Right * (t.BackM * 0.6) + Double3.Up * t.HeightM;
        return Camera.LookAt(Double3.Lerp(rackEye, wide, u), Double3.Lerp(rackAt, middle, Math.Min(1, u * 1.6)), (float)(50 + 10 * u));
    }

    /// <summary>
    /// App. E.4 O12, the cinematic's light rig: the fog pushed out to at least twice the shot's distance, so what it's of
    /// reads through the night.
    /// </summary>
    public static void CinematicFog(ref FrameLighting light, double shotDistance) =>
        light.FogDensity = Math.Min(light.FogDensity, (float)(1 / (2 * Math.Max(1, shotDistance))));

    /// <summary>E.9's shot distance at <paramref name="seconds"/>: the camera to the middle of the train.</summary>
    public static double StrandedDistance(TrainOnLine train, Sim.Train.StrandedOutroTuning t, double seconds) =>
        (Stranded(train, t, seconds).Position - train.Frames[train.Frames.Count / 2].Origin).Length;

    /// <summary>E.9: how many of the train's lamps are out, from the last car forward (the engine's last of all).</summary>
    public static int StrandedLampsOut(int cars, Sim.Train.StrandedOutroTuning t, double seconds) =>
        seconds <= t.RackSeconds ? 0 : (int)Math.Floor(Math.Clamp((seconds - t.RackSeconds) / t.PullBackSeconds, 0, 1) * (cars + 0.999));

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

    public static FrameLighting Lighting(TrainOnLine train, Look? look = null, float dawn = 0) => Lighting(train.Frames[0], look, dawn);

    /// <summary>
    /// The night's fog where the engine is (linegen plan §14; note 313): the route's density times the stretch's factor,
    /// thicker in the low ground and by water, thinner on a crest, blended along the line so it comes and goes. The crew
    /// are all within a train's length of it, or out at a stop it's standing at.
    /// </summary>
    public static float FogDensity(Sim.Route.Route route, TrainOnLine train) =>
        (float)(route.Weather.FogDensity * (train.Line.Conditions?.Fog(train.Dynamics.Path, train.Dynamics.Distance) ?? 1));

    /// <param name="look">The art pass's atmosphere (look.json) over the night's defaults, when there is one.</param>
    /// <param name="dawn">How far the dawn's come up (0..1, <see cref="Look.DawnOf"/>).</param>
    public static FrameLighting Lighting(in CarFrame engine, Look? look = null, float dawn = 0)
    {
        var light = look?.Apply(FrameLighting.Night) ?? FrameLighting.Night;
        if (look is not null)
            light = look.Dawn(light, dawn);
        light.LampPosition = Sim.World.LampPosition(engine);
        var fwd = engine.Back * -1;
        light.LampDirection = Vector3.Normalize(new Vector3((float)fwd.X, (float)fwd.Y - 0.04f, (float)fwd.Z));
        return light;
    }
}
