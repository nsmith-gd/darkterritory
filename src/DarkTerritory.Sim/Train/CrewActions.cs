using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// What a player does with their hands, all by holding Use (without pushing forward, which is
/// how you grab a ladder instead):
/// <list type="bullet">
/// <item>at the firebox, shovel coal: one unit per 1.2 s (spec B.6), the shovel in hand (<see cref="TakeShovel"/>);</item>
/// <item>at the vent valve, hold it open;</item>
/// <item>on a coupler plate, nothing but the doors: the coupling is cut by <see cref="Uncoupling"/> (T91);</item>
/// <item>at a car's brake wheel, wind its rake's handbrakes on or off;</item>
/// <item>at a cargo car's roof hatch, open or shut it (T99);</item>
/// <item>at a sandbox on the engine's running boards, sand the rail (Grease's counter, App. A.2);</item>
/// <item>inside a breached car at the hole, board it up (decided 1 Oct, <see cref="Breaches"/>; with the wrench, note 301);</item>
/// <item>the wrench in hand at a burst boiler or a battered car's dent, mend it (note 301, <see cref="Repairs"/>);</item>
/// <item>at a crew locker, open or shut its door (a tap there is the hands': <see cref="Lockers"/>);</item>
/// <item>at a car's hot axle box, from the coupling gap behind it or the ground beside it, grease it (note 331, <see cref="HotBoxes"/>);</item>
/// <item>the wrench in hand at a loose coupling, in its gap, tighten it (note 356, <see cref="Couplings"/>).</item>
/// </list>
/// A VR player's reaching hand (T29) picks what's worked by where it is, not where they stand, and shovels by the
/// stroke: coal onto the shovel at the tender, then into the firebox (<see cref="ShovelByHand"/>).
/// Runs on the host for everyone and on a client for its own player, before the train steps.
/// </summary>
public static class CrewActions
{
    /// <param name="hand">The hand tuning, when hands are reported at all (<see cref="World.Hand"/>).</param>
    public static void Apply(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, double dt, HandTuning? hand = null)
    {
        if (Uncoupling(s, intent, train, hand))
        {
            var c = train.Dynamics.Tuning.Couplings;
            double needed = train.CouplingUnderLoad(s.Parent) ? c.UncoupleUnderLoadSeconds : c.UncoupleSeconds;
            double was = s.ActionProgress;
            s.ActionProgress += dt;
            if (was < needed && s.ActionProgress >= needed)
                train.Uncouple(s.Parent);
            return;
        }
        // The vent's own key (note 264): held anywhere in the cab, the blow-off's open, whatever Use is doing.
        if (VentHeld(s, intent, train))
            train.Boiler.Venting = true;
        // A hot box (note 331): greased with Use held at it, from a car or the ground beside it. Done, the count's left
        // where it got to, as at a breach.
        if (s.Alive && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5 && train.HotBoxTuning is { Enabled: true } hbt
            && HotBoxes.Within(s, train, hbt) is { } box)
        {
            s.Flags &= ~PlayerFlags.Shovelful;
            if (s.ActionProgress >= hbt.GreaseSeconds)
                s.ActionProgress = 0;
            s.ActionProgress += dt;
            if (s.ActionProgress >= hbt.GreaseSeconds)
                train.Vehicles[box].HotBox = 0;
            return;
        }
        // A loose coupling (note 356): the wrench in the gap, Use held, and it's tight. Before the plate's doors: at a loose
        // pin with the wrench in hand, it's the pin that's worked.
        if (s.Alive && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5 && train.Loose is { Enabled: true } lt
            && Couplings.Tightens(s, train) && Couplings.Within(s, train, lt) is { } pin)
        {
            s.Flags &= ~PlayerFlags.Shovelful;
            if (s.ActionProgress >= lt.TightenSeconds)
                s.ActionProgress = 0;
            s.ActionProgress += dt;
            if (s.ActionProgress >= lt.TightenSeconds)
                train.Vehicles[pin].Loose = 0;
            return;
        }
        if (!s.Alive || !intent.Has(PlayerButtons.Use) || intent.MoveZ > 0.5 || s.Parent == PlayerState.World)
        {
            // Let go of the shovel and what's on it is spilled. A mend under way with the wrench is kept while you're at it
            // (note 301: a few presses do it, Sea of Thieves style).
            if (!(s.Alive && !intent.Has(PlayerButtons.Use) && Repairs.Keeps(s, train, hand)))
                s.ActionProgress = 0;
            s.Flags &= ~PlayerFlags.Shovelful;
            return;
        }
        // A breach in the car's shell (decided 1 Oct): boarded up from inside, Use held at the hole. It's the one thing worked
        // there (the end door is in the wall the Car Hugger ate). Done, the count's left where it got to, so a hand still
        // held on doesn't go on to work the door beside it; still held as a fresh hole opens, it starts that one over.
        if (Breaches.Within(s, train, hand) is { } breached)
        {
            double board = train.Dynamics.Tuning.Breach.BoardSeconds;
            s.Flags &= ~PlayerFlags.Shovelful;
            if (s.ActionProgress >= board)
                s.ActionProgress = 0;
            s.ActionProgress += dt;
            if (s.ActionProgress >= board)
                train.Vehicles[breached].Breached = false;
            return;
        }
        // At the hole without what boards it up (note 301: the wrench): nothing, not the end door beside it either.
        if (Repairs.ByWrench(train) && Breaches.AtHole(s, train, hand) is not null)
        {
            s.ActionProgress = 0;
            s.Flags &= ~PlayerFlags.Shovelful;
            return;
        }
        double before = s.ActionProgress;
        var couplings = train.Dynamics.Tuning.Couplings;
        var near = NearestInteractable(s, train, hand);
        // The smashed forward lamp from the front of the cab, the wrench in hand (note 301, slice 2): the glass goes in. It's
        // mended from where the fire and the coal are worked (note 280's cab), so the wrench in hand there is the lamp's, not
        // the shovel's; a ruptured boiler's fire door is still the rupture's, and the vent still vents.
        if (Repairs.Lamp(s, train) && near?.Thing.Kind is InteractableKind.Coal or InteractableKind.Firebox or null
            && !(near?.Thing.Kind == InteractableKind.Firebox && train.Boiler.Ruptured))
        {
            s.ActionProgress += dt;
            train.MendLamp?.Invoke(dt);
            return;
        }
        if (Hand(s, hand) && train.BoilerTuning is { } boiler && PlayerMotor.InCab(s, train)
            && near?.Thing.Kind is InteractableKind.Coal or InteractableKind.Firebox or null)
        {
            if (boiler.ShovelInHand && !TakeShovel(ref s, train))
            {
                s.ActionProgress = 0;
                s.Flags &= ~PlayerFlags.Shovelful;
                return;
            }
            ShovelByHand(ref s, near?.Thing.Kind, train, boiler, dt);
            return;
        }
        s.Flags &= ~PlayerFlags.Shovelful;

        // Use on the coupler plate works a door, never the coupling (T91: that's Uncouple, looking down).
        if (s.Surface == Surface.Coupler && near?.Thing.Kind != InteractableKind.Door)
        {
            s.ActionProgress = 0;
            return;
        }

        switch (near?.Thing.Kind)
        {
            case InteractableKind.Door:
                double doorSeconds = train.Dynamics.Tuning.Geometry.Interior?.DoorSeconds ?? 0.4;
                s.ActionProgress += dt;
                if (before < doorSeconds && s.ActionProgress >= doorSeconds)
                    train.Vehicles[near.Value.Vehicle].ToggleDoor(near.Value.Thing.Index);
                break;
            // A crew locker's door (note 173): held, opened or shut, like a car's door. (A tap's the hands', in Bodies.Handle.)
            case InteractableKind.Locker:
                double lockerSeconds = Lockers.DoorSeconds(train);
                s.ActionProgress += dt;
                if (before < lockerSeconds && s.ActionProgress >= lockerSeconds)
                    train.Vehicles[near.Value.Vehicle].ToggleLocker(near.Value.Thing.Index);
                break;
            // A ruptured boiler, the wrench in hand (note 301; the repair kit where the wrench isn't the tool, T109, GDD §12):
            // worked there, it's mended.
            case InteractableKind.Firebox when train.BoilerTuning is { } rt && train.Boiler.Ruptured && Repairs.MendsBoiler(s, train) && PlayerMotor.InCab(s, train):
                s.ActionProgress += dt;
                if (s.ActionProgress >= rt.RepairSeconds)
                {
                    train.Boiler.Repair();
                    s.ActionProgress = 0;
                }
                break;
            case InteractableKind.Firebox when train.BoilerTuning is { } bt && PlayerMotor.InCab(s, train):
                // The shovel into hand first (note 275): no shovel, no coal.
                if (bt.ShovelInHand && !TakeShovel(ref s, train))
                {
                    s.ActionProgress = 0;
                    break;
                }
                s.ActionProgress += dt;
                if (s.ActionProgress >= bt.ShovelSeconds)
                {
                    train.Boiler.Shovel(bt);
                    s.ActionProgress -= bt.ShovelSeconds;
                }
                break;
            // Out on the running boards at a sandbox: sand going down under the drivers while it's held (App. A.2).
            case InteractableKind.Sandbox when s.Parent == 0 && s.Surface == Surface.Deck:
                train.Sanding = true;
                s.ActionProgress = 0;
                break;
            // The vent, in the cab (T97, T109).
            case InteractableKind.Vent when PlayerMotor.InCab(s, train):
                train.Boiler.Venting = true;
                s.ActionProgress = 0;
                break;
            // The cab's tool rack (T109): a press takes the wrench, into the first free slot and into hand; with it in hand,
            // a press puts it back. It's a tool to swing, and mends (note 301: everyone carries one now). The shovel in hand is hung
            // back on it the same way (note 275).
            case InteractableKind.ToolRack when PlayerMotor.InCab(s, train):
                s.ActionProgress += dt;
                if (before < RackSeconds && s.ActionProgress >= RackSeconds)
                    Rack(ref s, train);
                break;
            // A cargo car's roof hatch (T99), from the roof: opened or shut like a door, but not shut down onto a casting the
            // crane has hanging in it.
            case InteractableKind.Hatch when s.Surface == Surface.Roof || Hand(s, hand):
                double hatchSeconds = train.Dynamics.Tuning.Geometry.Interior?.DoorSeconds ?? 0.4;
                s.ActionProgress += dt;
                var hv = train.Vehicles[near.Value.Vehicle];
                if (before < hatchSeconds && s.ActionProgress >= hatchSeconds
                    && !(hv.DoorOpen(CarShape.HatchBit) && train.HatchBlocked?.Invoke(near.Value.Vehicle) == true))
                    hv.ToggleDoor(CarShape.HatchBit);
                break;
            // A hand reaches the wheel from the top of the end ladder, too.
            case InteractableKind.Handbrake when s.Surface == Surface.Roof || Hand(s, hand):
                s.ActionProgress += dt;
                if (before < couplings.HandbrakeSeconds && s.ActionProgress >= couplings.HandbrakeSeconds)
                    train.SetHandbrake(s.Parent, !train.RakeOf(s.Parent).Handbrake);
                break;
            // A battered car's dent, the wrench in hand (note 301): its shell comes back while it's worked.
            case null when Repairs.Dent(s, train, hand) is { } dented:
                s.ActionProgress += dt;
                Repairs.MendDent(train, dented, dt);
                break;
            default:
                s.ActionProgress = 0;
                break;
        }
    }

    /// <summary>
    /// A shovelful by hand (T29): a hand gripping at the tender's coal face puts coal on the shovel; carried to the
    /// firebox, it goes in. Never faster than spec B.6's one unit per <see cref="BoilerTuning.ShovelSeconds"/>: the
    /// time counts from the grip, or from the last shovelful, so a steady swing shovels at the keyboard's rate and a
    /// frantic one doesn't beat it.
    /// </summary>
    static void ShovelByHand(ref PlayerState s, InteractableKind? at, TrainOnLine train, BoilerTuning boiler, double dt)
    {
        s.ActionProgress += dt;
        if (at == InteractableKind.Coal)
            s.Flags |= PlayerFlags.Shovelful;
        else if (at == InteractableKind.Firebox && s.Has(PlayerFlags.Shovelful) && s.ActionProgress >= boiler.ShovelSeconds)
        {
            train.Boiler.Shovel(boiler);
            s.Flags &= ~PlayerFlags.Shovelful;
            s.ActionProgress = 0;
        }
    }

    static bool Hand(in PlayerState s, HandTuning? hand) => hand is not null && s.Hand != default;

    /// <summary>The vent key held in the cab (note 264): the blow-off open while it's down. Never on a coupler (that's Uncouple's).</summary>
    public static bool VentHeld(in PlayerState s, in PlayerIntent intent, TrainOnLine train) =>
        s.Alive && intent.Has(PlayerActions.Vent) && s.Surface != Surface.Coupler && train.BoilerTuning is not null && PlayerMotor.InCab(s, train);

    /// <summary>At the firebox of a ruptured boiler, in the cab: where the wrench in hand mends it (note 301; the repair kit, T109).</summary>
    public static bool AtTheRupture(in PlayerState s, TrainOnLine train, HandTuning? hand = null) =>
        s.Alive && train.BoilerTuning is not null && train.Boiler.Ruptured && PlayerMotor.InCab(s, train)
        && Nearest(s, train, hand) == InteractableKind.Firebox;

    /// <summary>How long Use is held at the rack to take the wrench or put it back.</summary>
    const double RackSeconds = 0.4;

    /// <summary>The wrench out of its rack into hand, or back into it (T109); or the shovel in hand back onto it (note 275). There's the one of each.</summary>
    static void Rack(ref PlayerState s, TrainOnLine train)
    {
        if (Kit.Held(s) == Tool.Shovel)
        {
            s.Kit = Kit.With(s.Kit, s.HeldSlot, Tool.None);
            train.Boiler.ShovelOut = false;
            return;
        }
        if (Kit.Held(s) == Tool.Wrench)
        {
            s.Kit = Kit.With(s.Kit, s.HeldSlot, Tool.None);
            train.Boiler.WrenchOut = false;
            return;
        }
        if (train.Boiler.WrenchOut || Kit.Has(s.Kit, Tool.Wrench))
            return;
        for (int i = 0; i < Kit.Slots; i++)
            if (Kit.At(s.Kit, i) == Tool.None)
            {
                s.Kit = Kit.With(s.Kit, i, Tool.Wrench);
                s.HeldSlot = (byte)i;
                train.Boiler.WrenchOut = true;
                return;
            }
    }

    /// <summary>
    /// The shovel into hand to fire with (App. C.2, GDD §12; note 275): already there; else out of your own kit; else off the
    /// cab's tool rack into the first free slot, if it's on it. False when it's out with someone else (or your hands are
    /// full): the fire waits for whoever has it. Worked out alike everywhere, so a client predicts the take.
    /// </summary>
    public static bool TakeShovel(ref PlayerState s, TrainOnLine train)
    {
        if (Kit.Held(s) == Tool.Shovel)
            return true;
        for (int i = 0; i < Kit.Slots; i++)
            if (Kit.At(s.Kit, i) == Tool.Shovel)
            {
                s.HeldSlot = (byte)i;
                return true;
            }
        if (train.Boiler.ShovelOut)
            return false;
        for (int i = 0; i < Kit.Slots; i++)
            if (Kit.At(s.Kit, i) == Tool.None)
            {
                s.Kit = Kit.With(s.Kit, i, Tool.Shovel);
                s.HeldSlot = (byte)i;
                train.Boiler.ShovelOut = true;
                return true;
            }
        return false;
    }

    /// <summary>Whether <paramref name="s"/> could fire now, as far as the shovel goes (<see cref="TakeShovel"/>, without taking it).</summary>
    public static bool HasShovel(in PlayerState s, TrainOnLine train)
    {
        if (train.BoilerTuning is not { ShovelInHand: true } || Kit.Has(s.Kit, Tool.Shovel))
            return true;
        ulong spare = s.Kit;
        return !train.Boiler.ShovelOut && Kit.TryAdd(ref spare, Tool.Shovel);
    }

    /// <summary>
    /// Working the coupling you stand on (T91 playtest): Uncouple held, standing still, looking down at the coupler (well
    /// below level); or, in a headset, gripping with a hand reached down to it. Deliberate, and nothing else's button.
    /// </summary>
    public static bool Uncoupling(in PlayerState s, in PlayerIntent intent, TrainOnLine train, HandTuning? hand = null)
    {
        if (!s.Alive || s.Surface != Surface.Coupler || s.Parent == PlayerState.World || s.Has(PlayerFlags.Held)
            || Math.Abs(intent.MoveX) > 0.1 || Math.Abs(intent.MoveZ) > 0.1)
            return false;
        if (Hand(s, hand))
            return intent.Has(PlayerButtons.Use) && s.Hand.Y < ReachedDown;
        double down = train.Dynamics.Tuning.Couplings.UncoupleLookDownDegrees * Math.PI / 180;
        return intent.Has(PlayerActions.Uncouple) && s.Pitch <= -down;
    }

    /// <summary>A headset hand this low (from the feet) is down at the coupler, not at a door handle.</summary>
    const double ReachedDown = 0.7;

    /// <summary>The interactable on the player's own vehicle within reach, if any.</summary>
    public static InteractableKind? Nearest(in PlayerState s, TrainOnLine train, HandTuning? hand = null) => NearestInteractable(s, train, hand)?.Thing.Kind;

    /// <summary>
    /// Within reach across and about the same height: the roof's brake wheel isn't in reach from the floor
    /// under it. A door is also in reach from the coupler plate or next car's end, so it can be opened from
    /// outside (Soot Children: "the failure is opening a door").
    /// <para>
    /// A reaching hand (T29) is tested instead of the feet: within the thing's reach across and between knee and head
    /// height over its footing. It picks by where it is, so it wants no facing, and the brake wheel is in reach of a
    /// hand from the top of the end ladder. The tender's coal face is only for hands (the keyboard shovels at the
    /// firebox, as it always has).
    /// </para>
    /// </summary>
    public static (Interactable Thing, int Vehicle)? NearestInteractable(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (s.Parent == PlayerState.World)
            return null;
        var reaching = Hand(s, hand) ? PlayerMotor.HandAt(s) : null;
        (Interactable, int)? best = null, looked = null;
        double bestD = double.MaxValue;
        int own = s.Parent;
        // Doors want facing: the coupler plate is in reach of two of them, and Use there also cuts the coupling. So do the
        // crew lockers: a row of them, and you're at the one you face.
        double fx = -DMath.Sin(s.Yaw), fz = -DMath.Cos(s.Yaw);
        // Note 267: where several are in reach (a row of lockers a hand wide, the firebox under the whistle cord), the one
        // looked at is the one worked: the least angle off the view's centre, within pick.lookDegrees. Only then the nearest.
        var pick = train.Dynamics.Tuning.Pick;
        double cp = DMath.Cos(s.Pitch);
        var view = new Double3(fx * cp, DMath.Sin(s.Pitch), fz * cp);
        var eye = s.Position + Double3.Up * pick.EyeHeight;
        // Compared as cosines (no Acos: what's picked changes the train, so it's worked out alike on every machine).
        double bestCos = DMath.Cos(pick.LookDegrees * Math.PI / 180);
        void Search(int vehicle, Double3 at, bool doorsOnly)
        {
            foreach (var i in train.Frames[vehicle].Shape.Interactables)
            {
                if (doorsOnly && i.Kind != InteractableKind.Door || reaching is null && i.Kind == InteractableKind.Coal)
                    continue;
                double dx = at.X - i.Position.X, dz = at.Z - i.Position.Z;
                double d = dx * dx + dz * dz;
                if (reaching is null && i.Kind is InteractableKind.Door or InteractableKind.Locker && -(dx * fx + dz * fz) < 0.6 * Math.Sqrt(d))
                    continue;
                bool height = reaching is null ? Math.Abs(at.Y - i.Position.Y) < 1.2 : at.Y - i.Position.Y is >= 0.2 and <= 1.8;
                if (d > i.Radius * i.Radius || !height)
                    continue;
                if (reaching is null && vehicle == own)
                {
                    var to = i.Position + Double3.Up * i.Aim - eye;
                    double cos = Double3.Dot(view, to.Normalized);
                    if (cos >= bestCos)
                    {
                        bestCos = cos;
                        looked = (i, vehicle);
                    }
                }
                // The whistle cord is only ever the one looked at: never pulled by standing near it.
                if (reaching is null && i.Kind == InteractableKind.Whistle)
                    continue;
                if (d < bestD)
                {
                    bestD = d;
                    best = (i, vehicle);
                }
            }
        }
        var from = reaching ?? s.Position;
        Search(s.Parent, from, doorsOnly: false);
        // On a coupler plate, the next car's front door is in reach too.
        if (s.Surface == Surface.Coupler && train.VehicleBehind(s.Parent) is var behind and >= 0)
        {
            var world = train.Frames[s.Parent].ToWorld(from);
            Search(behind, train.Frames[behind].ToLocal(world), doorsOnly: true);
        }
        return looked ?? best;
    }
}
