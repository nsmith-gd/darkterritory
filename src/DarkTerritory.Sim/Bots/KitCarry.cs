using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// The repair kit brought up the train to a ruptured boiler from further back than car 1 (GDD §12 "when they die on the
/// roofs it's lying in car four and someone has to go and get it", §23's cascade table "the kit left in car four"; note
/// 186, the cascade audit). <see cref="KitRun"/> is the cab's: it fetches the kit from the engine or car 1. This is a
/// crewmate's, out on the train: one of them goes down off the roofs onto the plate in front of the kit's car, opens the
/// way, takes the kit, and carries it forward through the cars and across the coupler plates to the cab, where it holds it
/// at the firebox itself (§12: the engineer is whoever has it). Mended, it puts the kit down in the cab, for the cab's crew
/// next time, and goes back out onto the train.
/// <para>
/// <b>Never Use with the kit in hand outside a car.</b> A press with something in hand puts it down (<see cref="Bodies.Handle"/>),
/// and a door wants Use: put down on a plate a metre wide, the kit can go off it onto the ballast. So the doors on the way
/// to the next car are opened first, empty-handed, with the kit left on the floor clear of them, and the kit's only carried
/// through doors that stand open. It's light enough to take up a ladder (it isn't freight: <c>World</c>'s heavy flag), but
/// carried along the roofs, the jumps over the gaps put it in the air at a curve; through the cars it's never off a floor.
/// </para>
/// <para>
/// <b>Who goes</b> is worked out alike by every bot from what each sees, with nobody calling it: anyone already down on the
/// floors and plates (the lowest player id of them, which doesn't change as they move about, so two never take turns at
/// it), or else whoever's fewest cars from it up on the roofs, and they keep on till someone's down. Never the cab's crew (car 1 and the engine are
/// theirs), nor a gun's crew at their gun, unless there's nobody else.
/// </para>
/// Worked out afresh each tick from where the bot is and what's open, like <see cref="KitRun"/>, so it never waits on a step
/// it's no longer at; taps by the bot's own tick, once every <see cref="KitRun.TapEvery"/> (note 186).
/// </summary>
public static class KitCarry
{
    const int Front = 0, Rear = 1; // a car's end doors, front (−Z) then rear (+Z)

    /// <summary>m: a walk along a car's floor longer than this is run.</summary>
    const double RunFrom = 3;

    /// <summary>m: how far behind its front end wall the kit's put down to go and open the doors ahead (well clear of the door's reach).</summary>
    const double DropBack = 2.2;

    /// <summary>
    /// The kit lying further back than car 1 for a crewmate to bring: on a coupled car's floor or in one of its lockers, every
    /// car between there and the engine one to walk through. Null if a kit's in someone's hands, or one's in the engine or
    /// car 1 (the cab's to fetch: <see cref="KitRun"/>), or none lies anywhere it can be brought from. Of several, the nearest
    /// the engine.
    /// </summary>
    public static Body? Lying(World world)
    {
        var train = world.Train;
        var consist = train.Dynamics.Consist;
        if (consist.Vehicles.Count < 3 || !consist.Vehicles[0].IsEngine)
            return null;
        Body? best = null;
        int bestAt = int.MaxValue;
        foreach (var b in world.Bodies.All)
        {
            // (Not Body.Claimed: that's the host's. A kit in a coupled car is the crew's anyway: it was stocked, or brought aboard.)
            if (b.Kind != BodyKind.RepairKit)
                continue;
            if (b.Carrier >= 0)
                return null;
            int at = b.Parent >= 0 ? consist.IndexOf(b.Parent) : -1;
            if (at is 0 or 1)
                return null;
            if (at < 2 || at >= bestAt || train.Frames[b.Parent].Shape.Interior is not { } room || !b.Stowed && !room.Contains(b.Centre))
                continue;
            bool through = true;
            for (int i = 1; i <= at && through; i++)
                through = train.Frames[consist.Vehicles[i].Id].Shape is { Interior: not null, DoorList.Count: >= 2 };
            if (through)
            {
                best = b;
                bestAt = at;
            }
        }
        return best;
    }

    /// <summary>
    /// Whether it's this bot's to bring: of the living crew off the engine, anyone down on the floors and plates (the lowest
    /// id), else whoever's on the roofs fewest cars from it (the kit's car or the one ahead, whose plate is the way in: none),
    /// the lowest id of them; at a gun only if there's nobody else.
    /// <para>
    /// <b>Reading:</b> cars, not metres. Each bot sees itself where it is and the rest a snapshot late, so two walkers side by
    /// side on one roof each saw the other nearer, by a hair, and neither went (or both did, by turns): the cascade audit's
    /// first runs lost 25 s to it. A car's the same car to all of them but for the moment someone's in the air over a gap.
    /// </para>
    /// </summary>
    /// <param name="going">It was on its way down for it last tick: up on the roofs, it keeps on unless someone's got down
    /// first. As the others move from car to car, the one fewest cars off changes, and without this a walker turned back
    /// from the end of a roof each time, all the way (the audit at 8 s in). Two may go; the first down has it.</param>
    public static bool Mine(in PlayerState self, int me, IReadOnlyList<(int Id, PlayerState State)> crew, TrainOnLine train, Body kit, bool going = false)
    {
        int k = train.Dynamics.Consist.IndexOf(kit.Parent);
        for (int pass = 0; pass < 2; pass++)
        {
            (bool Down, int Off, int Id)? best = null;
            foreach (var (id, s) in crew.Prepend((me, self)))
            {
                if (!Eligible(s, train, pass > 0))
                    continue;
                bool down = Slot(s, train) >= 2;
                int car = down ? k : NearestCar(train, PlayerMotor.WorldPosition(s, train));
                int off = down ? 0 : car < k ? Math.Max(0, k - 1 - car) : car - k;
                if (best is not { } b || down && !b.Down || down == b.Down && (off < b.Off || off == b.Off && id < b.Id))
                    best = (down, off, id);
            }
            if (best is { } chosen)
                return chosen.Id == me || going && !chosen.Down && Eligible(self, train, pass > 0);
        }
        return false;
    }

    /// <summary>One who might go: alive, free, aboard a car (not the engine), and not in a gun's seat unless <paramref name="seated"/>.</summary>
    static bool Eligible(in PlayerState s, TrainOnLine train, bool seated) =>
        s.Alive && !s.Has(PlayerFlags.Held) && (seated || !s.Has(PlayerFlags.Seated)) && OnTheTrain(s, train);

    /// <summary>The train's car (its index in the consist, the engine left out) whose middle is nearest along the line to a world point.</summary>
    static int NearestCar(TrainOnLine train, Double3 world)
    {
        double along = ConductorBot.AlongTheTrain(train, world);
        var vehicles = train.Dynamics.Consist.Vehicles;
        int best = 1;
        double bestOff = double.MaxValue;
        for (int i = 1; i < vehicles.Count; i++)
        {
            var pose = train.Cars[vehicles[i].Id];
            double off = Math.Abs(pose.FrontDistance - pose.Length / 2 - along);
            if (off < bestOff)
                (best, bestOff) = (i, off);
        }
        return best;
    }

    /// <summary>
    /// This tick's intent, or null when there's nothing of the kit's for this bot to do. Null with <paramref name="down"/>
    /// set: it's this bot's to bring, and it's up on the roofs; get down off them onto the plate behind that car (in by its
    /// rear door: the walker's own way in, <see cref="WarmUp"/>). <paramref name="brought"/>: it carried the kit to the cab,
    /// and hasn't yet gone back out onto the train. <paramref name="going"/>: on its way down for it last tick (<see cref="Mine"/>).
    /// </summary>
    public static PlayerIntent? Decide(in PlayerState self, World world, int me, IReadOnlyList<(int Id, PlayerState State)> crew, uint tick,
        bool brought, bool going, out int? down)
    {
        down = null;
        var train = world.Train;
        if (!self.Alive || train.BoilerTuning is null || self.Has(PlayerFlags.Held))
            return null;
        bool carrying = self.Has(PlayerFlags.RepairKit);
        if (!train.Boiler.Ruptured)
        {
            if (!brought)
                return null;
            // Mended: the kit down in the cab, where the cab's crew find it next time (KitRun takes it off the engine's
            // floor), and back out onto the train by car 1.
            if (carrying)
                return self.Parent == 0 ? new PlayerIntent { Buttons = tick % KitRun.TapEvery == 0 ? PlayerButtons.Use : PlayerButtons.None } : null;
            return KitRun.BackToTheTrain(self, train);
        }
        if (carrying)
            return Carry(self, world, tick);
        if (Lying(world) is not { } kit || !Mine(self, me, crew, train, kit, going))
            return null;
        var consist = train.Dynamics.Consist;
        int k = kit.Parent;
        if (Slot(self, train) < 2)
        {
            down = train.VehicleAhead(k);
            return null;
        }
        // The way to the next car opened first, then the kit taken.
        foreach (var (car, door) in Hop(train, k))
            if (!train.Vehicles[car].DoorOpen(door))
                return Open(self, train, car, door);
        int at = 2 * consist.IndexOf(k);
        return Slot(self, train) == at ? Running(self, KitRun.Take(self, train, kit, tick), kit.Centre) : Toward(self, train, at);
    }

    /// <summary>With the kit in hand: forward a car at a time through open doors; a door shut on the way, put it down and go and open it.</summary>
    static PlayerIntent? Carry(in PlayerState self, World world, uint tick)
    {
        var train = world.Train;
        var vehicles = train.Dynamics.Consist.Vehicles;
        if (vehicles.Count < 2 || !vehicles[0].IsEngine)
            return null;
        // Car 1 and the engine: the cab's own way to the firebox (KitRun), and held there. At the middle of the backhead,
        // between the cab's crew at their sides.
        if (self.Parent == 0 || self.Parent == vehicles[1].Id)
        {
            var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
            return KitRun.Decide(self, world, ConductorBot.FiringSpot(firebox, 0), tick);
        }
        int slot = Slot(self, train), x = self.Parent;
        if (slot < 2)
            return null;
        double doorX = DoorX(train), l = L(train, x);
        if (slot % 2 == 1)
        {
            // On the plate behind car x: in through its rear door if it's open; if not, back into the car behind to put it down.
            if (train.Vehicles[x].DoorOpen(Rear))
                return Walk(self, new Double3(doorX, 0, l - 1.0), 0);
            int behind = train.VehicleBehind(x);
            return Walk(self, Into(train, behind, new Double3(doorX, 0, -L(train, behind) + DropBack), x), Math.PI);
        }
        if (Hop(train, x).All(d => train.Vehicles[d.Car].DoorOpen(d.Door)))
            return Exit(self, train, x, -1);
        // Put down off the aisle on the load's side, clear of the door, facing it: a press with it in hand puts it down. Not
        // by the left wall: the car's extinguisher stands there, 2 m in (World.ExtinguisherMount), and the hands took that up
        // instead, and put it down, and took it up, all night.
        var spot = new Double3(doorX, 0, -l + DropBack);
        var (step, there) = WarmUp.Steer(self, spot, -Math.PI / 2);
        return there ? new PlayerIntent { Buttons = tick % KitRun.TapEvery == 0 ? PlayerButtons.Use : PlayerButtons.None } : Running(self, step, spot);
    }

    /// <summary>
    /// The doors the kit goes through from car <paramref name="car"/> to the one ahead: its front door and the next car's
    /// rear, and when the next is car 1, its front door too (so the kit is never put down in car 1, which is the cab's).
    /// </summary>
    static IEnumerable<(int Car, int Door)> Hop(TrainOnLine train, int car)
    {
        int ahead = train.VehicleAhead(car);
        yield return (car, Front);
        yield return (ahead, Rear);
        if (train.VehicleAhead(ahead) == 0)
            yield return (ahead, Front);
    }

    /// <summary>
    /// To a car's end door from this side of it (inside the car, or on the plate outside it), facing it, and Use held till
    /// it's open (CrewActions: once the hold's long enough, so holding on doesn't shut it again).
    /// </summary>
    static PlayerIntent Open(in PlayerState self, TrainOnLine train, int car, int door)
    {
        var consist = train.Dynamics.Consist;
        int slot = Slot(self, train), i = consist.IndexOf(car);
        double doorX = DoorX(train), l = L(train, car);
        int stand;
        Double3 at;
        double yaw;
        if (door == Front)
        {
            // From inside, or from the plate behind the car ahead (in that car's frame).
            int ahead = train.VehicleAhead(car);
            int plate = 2 * consist.IndexOf(ahead) + 1;
            if (slot <= plate)
                (stand, at, yaw) = (plate, new Double3(doorX, 0, L(train, ahead) + Gap(train) - 0.45), Math.PI);
            else
                (stand, at, yaw) = (2 * i, new Double3(doorX, 0, -l + 0.5), 0);
        }
        else if (slot <= 2 * i)
            (stand, at, yaw) = (2 * i, new Double3(doorX, 0, l - 0.5), Math.PI);
        else
            (stand, at, yaw) = (2 * i + 1, new Double3(doorX, 0, l + 0.45), 0);
        if (slot != stand)
            return Toward(self, train, stand);
        var (step, there) = WarmUp.Steer(self, at, yaw);
        if (!there)
            return Running(self, step, at);
        return CrewActions.Nearest(self, train) == InteractableKind.Door ? new PlayerIntent { Buttons = PlayerButtons.Use }
            : WarmUp.Steer(self, at + new Double3(0, 0, yaw == 0 ? -0.2 : 0.2), yaw).Step;
    }

    /// <summary>A step along the floors and plates towards <paramref name="goal"/> (a <see cref="Slot"/>), opening what's shut on the way.</summary>
    static PlayerIntent Toward(in PlayerState self, TrainOnLine train, int goal)
    {
        int slot = Slot(self, train), x = self.Parent;
        double doorX = DoorX(train), l = L(train, x);
        bool back = goal > slot;
        if (slot % 2 == 0)
        {
            int door = back ? Rear : Front;
            return train.Vehicles[x].DoorOpen(door) ? Exit(self, train, x, back ? 1 : -1) : Open(self, train, x, door);
        }
        // On the plate behind car x.
        if (back)
        {
            int behind = train.VehicleBehind(x);
            return train.Vehicles[behind].DoorOpen(Front)
                ? Walk(self, Into(train, behind, new Double3(doorX, 0, -L(train, behind) + 1.0), x), Math.PI)
                : Open(self, train, behind, Front);
        }
        return train.Vehicles[x].DoorOpen(Rear) ? Walk(self, new Double3(doorX, 0, l - 1.0), 0) : Open(self, train, x, Rear);
    }

    /// <summary>
    /// Out of car <paramref name="car"/> by its front (−1) or rear (+1) door, which is open, onto the plate: onto the aisle's
    /// line short of the end wall first (straight at the doorway from across the car, it's the wall beside it you meet).
    /// </summary>
    static PlayerIntent Exit(in PlayerState self, TrainOnLine train, int car, int end)
    {
        double doorX = DoorX(train), l = L(train, car);
        if (Math.Abs(self.Position.X - doorX) > 0.2)
            return Walk(self, new Double3(doorX, 0, end * Math.Min(end * self.Position.Z + 1, l - 0.8)), end > 0 ? Math.PI : 0);
        if (end > 0)
            return Walk(self, new Double3(doorX, 0, l + Gap(train) / 2), Math.PI);
        int ahead = train.VehicleAhead(car);
        return Walk(self, Into(train, ahead, new Double3(doorX, 0, L(train, ahead) + Gap(train) / 2), car), 0);
    }

    /// <summary>Steered there, running along a car's floor when it's far.</summary>
    static PlayerIntent Walk(in PlayerState self, Double3 target, double yaw) => Running(self, WarmUp.Steer(self, target, yaw).Step, target);

    /// <summary>A step towards <paramref name="target"/>, run when it's along a car's floor and far.</summary>
    static PlayerIntent Running(in PlayerState self, PlayerIntent step, Double3 target)
    {
        double dx = target.X - self.Position.X, dz = target.Z - self.Position.Z;
        if (self.Surface == Surface.Deck && dx * dx + dz * dz > RunFrom * RunFrom)
            step.Buttons |= PlayerButtons.Run;
        return step;
    }

    /// <summary>
    /// Where along the train's floors and plates a player is: 2i on the floor of the train's vehicle i (the engine's is 0),
    /// 2i + 1 on the plate behind it (which is in its frame); −1 anywhere else (a roof, a ladder, the air, the ground).
    /// </summary>
    static int Slot(in PlayerState s, TrainOnLine train)
    {
        if (s.Parent < 0 || s.Parent >= train.Frames.Count)
            return -1;
        int i = train.Dynamics.Consist.IndexOf(s.Parent);
        if (i < 0)
            return -1;
        double l = L(train, s.Parent), w = train.Dynamics.Tuning.Geometry.RoofWidth / 2;
        if (s.Surface == Surface.Deck && Math.Abs(s.Position.Z) <= l + 0.05 && Math.Abs(s.Position.X) < w)
            return 2 * i;
        if (s.Surface == Surface.Coupler && s.Position.Z > l - 0.05)
            return 2 * i + 1;
        return -1;
    }

    /// <summary>Aboard a car of the train (not the engine, nor the ground, nor a car left standing), or in the air over it.</summary>
    static bool OnTheTrain(in PlayerState s, TrainOnLine train) =>
        s.Parent == PlayerState.World ? s.Surface == Surface.Air
            : s.Parent > 0 && s.Parent < train.Frames.Count && train.Dynamics.Consist.IndexOf(s.Parent) >= 1;

    static double L(TrainOnLine train, int car) => train.Frames[car].Shape.HalfLength;
    static double Gap(TrainOnLine train) => train.Dynamics.Tuning.Geometry.CouplingGap;
    static double DoorX(TrainOnLine train) => train.Dynamics.Tuning.Geometry.Interior!.DoorX;

    /// <summary>A point on car <paramref name="from"/>'s floor, in car <paramref name="to"/>'s frame (flat: the steering's).</summary>
    static Double3 Into(TrainOnLine train, int from, Double3 local, int to) =>
        train.Frames[to].ToLocal(train.Frames[from].ToWorld(local));
}
