using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>Set pieces for looking at and listening to things headless (screenshots, audio renders, tests).</summary>
public static class Staging
{
    /// <summary>
    /// Where to stand to talk to, read or look at a town's <paramref name="what"/> (person, board, paper, fixture, door; the
    /// <paramref name="which"/>-th of them), and the point to look at (`dt screenshot --hud --talk`): in front of it, as close
    /// as a crewmate would come.
    /// </summary>
    public static (Double3 Feet, Double3 Look) TownStand(Sim.Towns.Town town, string what, int which)
    {
        var plan = town.Plan;
        Double3 Out(double s, double faceS, double faceD, double by) => town.Direction(s, faceS, faceD) * by;
        switch (what)
        {
            case "person":
                {
                    var p = plan.People[Math.Clamp(which, 0, plan.People.Count - 1)];
                    var feet = town.Feet(p);
                    return (feet + Out(p.S, p.FaceS, p.FaceD, 1.5) with { Y = 0 }, feet + Double3.Up * 1.5);
                }
            case "board":
                {
                    var b = plan.Fixtures.First(f => f.Kind == "board");
                    return (town.World(b.S, b.D) + Out(b.S, b.FaceS, b.FaceD, 1.6), town.World(b.S, b.D, 1.55));
                }
            case "paper":
                {
                    var loose = plan.Papers.Where(p => !p.OnBoard).ToList();
                    var p = loose[Math.Clamp(which, 0, loose.Count - 1)];
                    int side = plan.Square.Side;
                    var at = town.World(p.S, p.D, p.Height);
                    return (town.World(p.S - 0.9, p.D - side * 0.9), at);
                }
            case "door":
                {
                    var b = plan.Buildings[Math.Clamp(which, 0, plan.Buildings.Count - 1)];
                    var door = town.Door(b);
                    return (door with { Y = door.Y - 1.2 } + Out(b.S, 0, -Math.Sign(b.D), 2.0), door);
                }
            default:
                {
                    var f = plan.Fixtures[Math.Clamp(which, 0, plan.Fixtures.Count - 1)];
                    double back = Math.Max(f.SolidS, f.SolidD) + 1.4;
                    return (town.World(f.S, f.D) + Out(f.S, f.FaceS, f.FaceD, back), town.World(f.S, f.D, Math.Max(0.3, f.Height * 0.6)));
                }
        }
    }

    /// <summary>
    /// Standing in a fortress town (note 281, `dt screenshot --town`): "square" at the way in from the engine, looking over
    /// the centrepiece to the hall; "centre" at the centrepiece, close; "board" before the notice board; "hall" at the
    /// hall's door; "gate" by the gatekeeper, looking back down the yard; "street" out on the track side, the square across
    /// the train.
    /// </summary>
    public static Ballast.Render.Camera TownCamera(Sim.Towns.Town town, string where)
    {
        var plan = town.Plan;
        var sq = plan.Square;
        int side = sq.Side;
        double mid = (sq.S0 + sq.S1) / 2;
        var centre = plan.Fixtures[0];
        var board = plan.Fixtures.First(f => f.Kind == "board");
        var hall = plan.Buildings.First(b => b.Kind == "hall");
        // A walled town (queue #74): from over the gate looking back over its roofs, down its first street, and from
        // outside the gate as the train leaves, its front wall either side of the gatehouse.
        if (plan.Bounds is { } wall && where is "over" or "lane" or "outside")
        {
            var st = wall.Streets.OrderBy(x => Math.Abs(x.D)).ThenBy(x => x.D).First();
            return where switch
            {
                "over" => Ballast.Render.Camera.LookAt(town.World(wall.Gate + 40, 0, 70), town.World(wall.Gate - 260, 0, 0), 70),
                "outside" => Ballast.Render.Camera.LookAt(town.World(wall.Gate + 90, wall.Right * 0.35, 4), town.World(wall.Gate, -wall.Left * 0.3, 6), 75),
                _ => Ballast.Render.Camera.LookAt(town.World(mid + 30, st.D, 1.7), town.World(mid - 40, st.D, 1.6), 72),
            };
        }
        // The houses (note 281): the first open one, its front, its kitchen from the door, its parlour through the partition.
        var home = plan.Houses.FirstOrDefault(h => h.Layout is not null) ?? plan.Houses.FirstOrDefault();
        if (home is not null && where is "houses" or "house" or "kitchen" or "parlour")
        {
            var l = home.Layout;
            int k = l?.Kitchen ?? 1;
            double du = l?.DoorU ?? 0, pv = l?.PassV ?? home.Depth / 2, w = home.Width;
            Double3 At(double u, double v, double up) => home.Rail(u, v) is var (s, d) ? town.World(s, d, up) : default;
            return where switch
            {
                // On the houses' side of the train (it stands at the gate, beside them), looking down their street.
                "houses" => Ballast.Render.Camera.LookAt(town.World(home.S + 12, home.Side * 4.2, 1.8), town.World(home.S - 26, home.Side * 9.5, 2.8), 70),
                "house" => Ballast.Render.Camera.LookAt(At(du + 5.5, -3.6, 1.7), At(0, 0, 2.7), 75),
                "kitchen" => Ballast.Render.Camera.LookAt(At(du - k * 0.1, 0.35, 1.65), At(k * w / 2, home.Depth - 0.6, 0.9), 75),
                _ => Ballast.Render.Camera.LookAt(At(k * 1.0, pv - 0.4, 1.65), At(-k * w / 2, pv + 0.9, 1.1), 75),
            };
        }
        return where switch
        {
            "board" => Ballast.Render.Camera.LookAt(town.World(board.S - 1.2, board.D - side * 2.2, 1.65), town.World(board.S, board.D, 1.5), 60),
            "hall" => Ballast.Render.Camera.LookAt(town.World(hall.S - 7, hall.D - side * (hall.Depth / 2 + 9), 1.7), town.World(hall.S, hall.D, 3), 65),
            "gate" => Ballast.Render.Camera.LookAt(town.World(plan.People[0].S + 2.5, plan.People[0].D + 1.6, 1.7), town.World(plan.People[0].S - 30, side * 8, 1.5), 65),
            "centre" => Ballast.Render.Camera.LookAt(town.World(centre.S + 5, centre.D - side * 5, 1.8), town.World(centre.S, centre.D, 1.2), 60),
            "street" => Ballast.Render.Camera.LookAt(town.World(mid + 18, -side * 6, 2.2), town.World(mid - 10, side * 18, 2.0), 70),
            _ => Ballast.Render.Camera.LookAt(town.World(sq.S1 - 3, side * 4.5, 1.7), town.World(centre.S - 6, centre.D, 1.6), 70),
        };
    }

    /// <summary>The sim tick staged impacts and hits land on (`dt screenshot --impact`, `--hit-flash`): the scene's clock is set after it.</summary>
    public const uint StrikeTick = 100;

    /// <summary>
    /// A cannonball come down (T121, `dt screenshot --impact`): <paramref name="ahead"/> metres up the line from the engine's
    /// front and <paramref name="lateral"/> to its right, on the ground there (water: as if over it; train and structure: a
    /// metre and a half up a face; creature: a body's height up, "creature:gaunt" a kind's, for its insides; note 290). "doll"
    /// is the staged Track Doll's spot, shattered.
    /// </summary>
    public static Sim.Combat.CannonImpact Impact(TrainOnLine train, string surface, double ahead, double lateral, IReadOnlyList<Enemy>? staged = null)
    {
        EnemyKind struck = 0;
        if (surface.Split(':') is [var on, var what])
            (surface, struck) = (on, Enum.Parse<EnemyKind>(what, ignoreCase: true));
        bool doll = surface.Equals("doll", StringComparison.OrdinalIgnoreCase);
        if (doll && staged?.FirstOrDefault(e => e is TrackDoll { Attached: < 0 }) is { } d)
            ahead = d.LineDistance - train.Dynamics.Distance;
        var kind = doll ? Sim.Combat.ImpactSurface.Creature : Enum.Parse<Sim.Combat.ImpactSurface>(surface, ignoreCase: true);
        var sample = train.Line.Sample(train.Dynamics.Distance + ahead);
        var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
        double hint = train.Dynamics.Distance + ahead;
        var at = sample.Position + right * lateral;
        at = at with { Y = Sim.Player.PlayerMotor.GroundAt(at, train.Line, ref hint) };
        if (kind is Sim.Combat.ImpactSurface.Train or Sim.Combat.ImpactSurface.Structure)
            at += Double3.Up * 1.5;
        else if (kind == Sim.Combat.ImpactSurface.Creature)
            at += Double3.Up * (doll ? 0.7 : 0.8);
        // Fired from the engine's gun, behind and above: the way the ball was going.
        var muzzle = train.Frames[0].ToWorld(new Double3(0, 5.3, -train.Frames[0].Shape.HalfLength + 12.8));
        return new Sim.Combat.CannonImpact(1, StrikeTick, at, (at - muzzle).Normalized, kind, 1, doll ? EnemyKind.TrackDoll : struck);
    }

    /// <summary>
    /// The dark answering a draw (note 287, `dt screenshot --answer left`): eyes <paramref name="ahead"/> m out from the engine's nose and
    /// <paramref name="lateral"/> m to its right (enemies.json director.draw's answerDistance and answerLateral), at an animal's
    /// eye height off the ground, <paramref name="left"/> seconds still to show.
    /// </summary>
    /// <summary>A point by line coordinates (as `dt screenshot --cam`): <paramref name="s"/> along the line, <paramref name="lateral"/> m to its right, <paramref name="height"/> m up.</summary>
    public static Double3 LineAt(Sim.Rail.RailLine line, double s, double lateral, double height)
    {
        var t = line.Sample(s);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return t.Position + right * lateral + Double3.Up * height;
    }

    public static DrawAnswer Answer(TrainOnLine train, double left, double ahead = 60, double lateral = 12, double height = 0.7) =>
        new(left, DrawCause.Whistle, Sim.World.DrawAnswerAt(train, ahead, lateral, height), 0);

    /// <summary>
    /// The roof warning (note 260, `dt screenshot --hud --roof-warning tunnel|bend`): a solo night on <paramref name="route"/>
    /// with the train <paramref name="seconds"/> short of its first tunnel's mouth (or its first posted bend, taken fast
    /// enough to throw roof riders off), the player up on car 2's roof facing ahead, warned.
    /// </summary>
    public static PrototypeSession RoofWarning(string content, Sim.Route.Route route, int cars, string kind, double seconds = 6)
    {
        var sight = DataFile.Load<Sim.Route.SightTuning>(Path.Combine(content, Sim.Route.SightTuning.File));
        bool tunnel = !kind.Equals("bend", StringComparison.OrdinalIgnoreCase);
        var signs = Sim.Route.Lineside.Boards(sight, route);
        var sign = signs.FirstOrDefault(s => s.Kind == (tunnel ? Sim.Route.SignKind.LowClearance : Sim.Route.SignKind.SpeedLimit) && s.Start > 2000)
            ?? throw new InvalidOperationException($"{route.Name} has no {(tunnel ? "tunnel" : "posted bend")} past its yard");
        double speed = tunnel ? 14 : sign.Limit + sight.ThrowOver + 0.5;
        // Settled a second before the moment, so the frames have a previous tick to draw from.
        var session = new PrototypeSession(content, route, cars, enemies: false, at: sign.Start - speed * (seconds + 1));
        session.Controls = new Sim.Train.TrainControls { Reverser = 1 };
        if (!tunnel && session.World.Lineside is { } lineside)
            speed = Math.Max(speed, lineside.ThrowsAbove(sign, session.Train) + 0.5);
        session.Respawn(Math.Min(2, session.Train.Frames.Count - 1));
        for (int i = 0; i < Sim.SimConstants.TickRate; i++)
        {
            session.Train.Dynamics.Velocity = speed;
            session.Step(default);
        }
        return session;
    }

    /// <summary>
    /// The cab's bend warning (note 265, `dt screenshot --hud --bend-warning [s]`): a solo night on <paramref name="route"/>
    /// with the train <paramref name="seconds"/> short of its first bend that can derail it, a quarter over that bend's
    /// derailing speed, the player at the controls, warned. <paramref name="seconds"/> 0: on the bend (the flanges' line).
    /// </summary>
    public static PrototypeSession BendWarning(string content, Sim.Route.Route route, int cars, double seconds = 6)
    {
        var plan = route.Plan ?? throw new InvalidOperationException($"{route.Name} isn't a generated line");
        var line = route.Build();
        double maxSpeed = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(content, Sim.Train.TrainTuning.File)).MaxSpeed;
        double at = -1, k = 0;
        for (double s = route.Gate + 2000; s < line.Length - 500 && at < 0; s += 5)
            if (Math.Abs(line.Sample(s).Curvature) is var c and > 1e-9 && Math.Sqrt(plan.Rules.ADerail / c) < maxSpeed * 0.9)
                (at, k) = (s, c);
        if (at < 0)
            throw new InvalidOperationException($"{route.Name} has no bend that can derail a train past its yard");
        double speed = Math.Min(maxSpeed, 1.25 * Math.Sqrt(plan.Rules.ADerail / k));
        var session = new PrototypeSession(content, route, cars, enemies: false, at: seconds <= 0 ? at + 60 : at - speed * (seconds + 1));
        session.Controls = new Sim.Train.TrainControls { Reverser = 1 };
        for (int i = 0; i < Sim.SimConstants.TickRate; i++)
        {
            session.Train.Dynamics.Velocity = speed;
            session.Step(default);
        }
        return session;
    }

    /// <summary>
    /// A blow landed on each staged creature (T121, `dt screenshot --hit-flash`), struck from the camera's side so its flinch
    /// is seen: the hit at its middle, a melee blow.
    /// </summary>
    public static List<Sim.Combat.HitConfirm> HitsOn(IReadOnlyList<Enemy> staged, TrainOnLine train, Double3 eye)
    {
        var hits = new List<Sim.Combat.HitConfirm>();
        foreach (var e in staged)
        {
            if (e.Gone || e.Kind is EnemyKind.Sleepers or EnemyKind.Drift or EnemyKind.CarFire)
                continue;
            var at = GreyboxPosition(e, train) + Double3.Up * 0.8;
            var from = (at - eye) with { Y = 0 };
            hits.Add(new Sim.Combat.HitConfirm(hits.Count + 1, StrikeTick, e.Id, e.Kind, 1, Sim.Combat.HitSource.Melee, at,
                from.Length > 1e-6 ? from.Normalized : new Double3(0, 0, -1), false));
        }
        return hits;
    }

    static Double3 GreyboxPosition(Enemy e, TrainOnLine train) =>
        e.Attached >= train.Frames.Count ? e.Local : e.WorldPosition(train);
    /// <summary>Crates, a lamp and a rescued child on car 2's roof and a body on car 3's, dropped and left to settle.</summary>
    public static Sim.Physics.Bodies Bodies(TrainOnLine train, string content)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var bodies = new Sim.Physics.Bodies();
        double roof = tuning.Geometry.CarHeight;
        bodies.SpawnCrate(train, 2, new Double3(0.5, roof + 0.3, 1.0)).Yaw = 0.4;
        bodies.SpawnCrate(train, 2, new Double3(-0.4, roof + 0.6, 2.2)).Yaw = -0.3;
        bodies.SpawnCrate(train, 2, new Double3(0.1, roof + 0.2, -1.5), Sim.Physics.BodyKind.Lamp);
        // A rescued child (GDD §19), set down on the roof by the lamp.
        bodies.SpawnCrate(train, 2, new Double3(-0.6, roof + 0.4, -0.4), Sim.Physics.BodyKind.Child);
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 3, -2, player) with { Health = 0, Yaw = 1.2 };
        bodies.SpawnRagdoll(train, 9, dead);
        for (int i = 0; i < 90; i++)
            bodies.Step(train, tuning, _ => null);
        return bodies;
    }

    /// <summary>
    /// A crewmate on car 2's roof with a body over their shoulder (App. C.4), carried a second and a half in the sim's
    /// fireman's carry (Bodies.Shoulder) so it's hanging as it would; <paramref name="walking"/>, stepping along the roof.
    /// </summary>
    /// <param name="child">The rescued child instead, in their arms (Bodies.ChildAt, the cradle).</param>
    public static (Sim.Physics.Bodies Bodies, Crewmate Carrier) Shouldered(TrainOnLine train, string content, bool walking = false, bool child = false)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var bodies = new Sim.Physics.Bodies();
        var carrier = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, -3, player) with { Yaw = Math.PI - 0.6 };
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, -2, player) with { Health = 0, Yaw = 0.4 };
        var body = child ? bodies.SpawnCrate(train, 2, new Double3(0, tuning.Geometry.CarHeight + 0.4, -2.6), Sim.Physics.BodyKind.Child)
            : bodies.SpawnRagdoll(train, 9, dead);
        body.Carrier = 1;
        for (int i = 0; i < 90; i++)
            bodies.Step(train, tuning, id => id == 1 ? carrier : null);
        var (feet, yaw) = (Sim.Player.PlayerMotor.WorldPosition(carrier, train), Sim.Player.PlayerMotor.WorldYaw(carrier, train));
        var act = child ? walking ? Art.CrewPose.CradleWalk : Art.CrewPose.Cradle : walking ? Art.CrewPose.ShoulderWalk : Art.CrewPose.Shoulder;
        return (bodies, new Crewmate(1, feet, yaw, true, Act: act));
    }

    /// <summary>
    /// The train as it leaves (World.Stock, Guns.Arm): the guard van's stores, lamp, radios and toys, every car's
    /// extinguisher on its mount, each at <paramref name="charge"/> (its sight glass, App. C.5), settled where they lie, and
    /// the guns' full stock of powder and shot.
    /// </summary>
    public static Sim.Physics.Bodies Stocked(TrainOnLine train, string content, double charge = 1)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var world = new Sim.World(train);
        world.Stock();
        Sim.Combat.Guns.Arm(train, DataFile.Load<Sim.Combat.CombatTuning>(Path.Combine(content, Sim.Combat.CombatTuning.File)).Guns);
        foreach (var b in world.Bodies.All)
            if (b.Kind == Sim.Physics.BodyKind.Extinguisher)
                b.Charge = charge;
        for (int i = 0; i < 90; i++)
            world.Bodies.Step(train, tuning, _ => null);
        return world.Bodies;
    }

    /// <summary>
    /// Three crewmates on car 2's roof (T47): a keyboard player, arms at their sides; a headset player pointing down the line
    /// ahead with one hand; and one holding something out in front with both.
    /// </summary>
    public static List<Crewmate> Crew(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        Crewmate At(byte id, double x, double z, double yaw, Double3 hand = default, Double3 other = default)
        {
            var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, z, player, x) with { Yaw = yaw };
            return new Crewmate(id, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true, hand, other);
        }
        return
        [
            // Facing back down the roof, towards the roof view's camera.
            At(1, -0.9, -5, Math.PI + 0.3),
            // (Pointing down the line ahead, low: an arm raised high read as a salute.)
            At(2, 0.1, -5.5, Math.PI - 0.4, hand: new Double3(0.3, 1.05, -0.55)),
            At(3, 1.0, -4.5, Math.PI + 0.35, hand: new Double3(0.22, 1.15, -0.45), other: new Double3(-0.22, 1.2, -0.42)),
        ];
    }

    /// <summary>
    /// Three headset crewmates on car 2's roof (T82, <c>dt screenshot --view crew --vr-body</c>, or crewside): one leaning
    /// over to reach down ahead of them, looking down at it; one crouched right down, both hands low by the roof; and one
    /// who has turned their head well past the hips' deadzone, the hips come round after it and a foot halfway through
    /// its step to catch up (the stride lived up to that moment, since a screenshot is one frame).
    /// </summary>
    public static List<Crewmate> Headsets(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var body = DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File)).Body;
        Crewmate At(byte id, double x, double z, double yaw, double head, double pitch, Double3 hand, Double3 other = default, VrStride? stride = null)
        {
            var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, z, player, x) with { Yaw = yaw, Pitch = pitch, Head = head };
            return new Crewmate(id, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true, hand, other,
                Headset: new HeadsetBody(head, pitch, s.Parent, s.Position, s.Yaw, stride));
        }
        // Side by side across the roof, far enough down it for the crew view to have them head to foot, and facing across
        // it (a little towards the camera), so it sees them side on.
        // The stepper: stood square facing `from`, then the head turned round to `to` and the hips shuffled a little aside.
        double across = -Math.PI / 2 - 0.3, to = across, from = to + 100 * Math.PI / 180;
        var stand = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, -5.9, player, 0.95);
        var stride = VrBody.Stand(stand.Position + new Double3(-0.08, 0, 0), from, body);
        for (int i = 0; i < 600 && !(stride.Stepping != 0 && stride.Progress >= 0.5); i++)
            stride = VrBody.Step(stride, stand.Position, to, 1 / 120.0, body);
        return
        [
            At(31, -1.0, -5.5, across, head: 1.42, pitch: -0.55, hand: new Double3(0.2, 0.9, -0.6)),
            At(32, 0.0, -5.7, across, head: 1.0, pitch: -0.35, hand: new Double3(0.25, 0.35, -0.4), other: new Double3(-0.22, 0.4, -0.35)),
            At(33, 0.95, -5.9, to, head: 1.62, pitch: 0, hand: new Double3(0.3, 1.1, -0.5), stride: stride),
        ];
    }

    /// <summary>
    /// The crew at work (X1, <c>dt screenshot --working</c>; their acts are <see cref="Art.CrewActs"/>' and the clips
    /// crew_clips.py's): on car 2's roof one carrying a crate, one heaving at a hatch, one winding the brake wheel at its
    /// end, one standing by with a crowbar; and on the last gun car, a gunner sat in the cannon's seat.
    /// </summary>
    /// <summary>
    /// A row of the crew down car 2's roof, each at one of <paramref name="acts"/> (CrewPose names, dt screenshot --act
    /// smash,pry,...: the roof view looks at them), facing the camera; the smash and pry with a crowbar in hand, the lantern
    /// with the hand lamp hung from the fist; each swing further into its blow, with the shovel, crowbar and wrench in turn.
    /// </summary>
    /// <param name="survivor">Who they all are (dt screenshot --survivor prisoner|wildlander): freed survivors' figures (App. D.8).</param>
    public static List<Crewmate> Acts(TrainOnLine train, string content, IEnumerable<string> acts, Art.Survivor survivor = Art.Survivor.None)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var crew = new List<Crewmate>();
        int i = 0, swings = 0;
        foreach (var name in acts)
        {
            var act = Enum.Parse<Art.CrewPose>(name.Replace("_", ""), ignoreCase: true);
            var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, -6 + 1.5 * i, player, i % 2 == 0 ? -0.5 : 0.5) with { Yaw = Math.PI + (i % 2 == 0 ? 0.5 : -0.5) };
            var tool = act is Art.CrewPose.Smash or Art.CrewPose.Pry ? Sim.Player.Tool.Crowbar : Sim.Player.Tool.None;
            // A swing (note 275): each one further into its blow than the last, with the shovel, the crowbar, the wrench in turn.
            double swing = 0;
            if (act == Art.CrewPose.Swing)
            {
                tool = (swings % 3) switch { 0 => Sim.Player.Tool.Shovel, 1 => Sim.Player.Tool.Crowbar, _ => Sim.Player.Tool.Wrench };
                swing = 0.12 + 0.18 * swings++;
            }
            crew.Add(new Crewmate((byte)(20 + i), Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true,
                Act: act, Holding: tool, Lamp: act is Art.CrewPose.Lantern or Art.CrewPose.LanternWalk, Survivor: survivor, Phase: swing));
            i++;
        }
        return crew;
    }

    public static List<Crewmate> Working(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        Crewmate At(byte id, int car, double x, double z, double yaw, Art.CrewPose? act, Sim.Player.Tool tool = Sim.Player.Tool.None)
        {
            var s = Sim.Player.PlayerMotor.SpawnOnRoof(train, car, z, player, x) with { Yaw = yaw };
            return new Crewmate(id, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true, Act: act, Holding: tool,
                Lamp: act == Art.CrewPose.Lantern);
        }
        var crew = new List<Crewmate>
        {
            At(5, 2, -0.8, -5.2, Math.PI + 0.4, Art.CrewPose.Carry),
            At(6, 2, 0.5, -6.6, Math.PI - 0.6, Art.CrewPose.Hatch),
            At(7, 2, 0.2, -train.Frames[2].Shape.HalfLength + 0.6, 0.3, Art.CrewPose.Handbrake),
            // Standing by with the crowbar (T108's hotbar), the tool in their fist.
            At(9, 2, -0.3, -3.6, Math.PI - 0.2, null, Sim.Player.Tool.Crowbar),
            // On the next roof back: a hand lamp held out, and the extinguisher on the hip (L0a).
            At(10, 3, 0.4, -2.4, Math.PI + 0.2, Art.CrewPose.Lantern),
            At(11, 3, -0.4, -4.4, Math.PI - 0.3, Art.CrewPose.Extinguish),
        };
        int gun = Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].HasGun, -1);
        if (gun >= 0 && Sim.Combat.Guns.Mount(train, gun) is { } mount)
        {
            var s = Art.CrewActs.Seated(Sim.Player.PlayerMotor.SpawnOnRoof(train, gun, mount.Position.Z, player), mount);
            crew.Add(new Crewmate(8, Sim.Player.PlayerMotor.WorldPosition(s, train), Sim.Player.PlayerMotor.WorldYaw(s, train), true, Act: Art.CrewPose.Gunner));
        }
        return crew;
    }

    /// <summary>
    /// The staged Ribbit pack as it goes (<c>dt screenshot --ribbits</c>): <c>hop</c> after crewmate 4 (alone on the ground
    /// off the train's left, <see cref="Lone"/>), <c>swell</c> lined up on them (App. A.6 TELEGRAPH), <c>tongue</c> on them
    /// (GRAB) and creeping in, <c>devour</c> the leader on them (GRAB, where the hop stops 0.8 m short). Without a mode,
    /// they're as <see cref="Threats"/> has them.
    /// </summary>
    public static List<Enemy> Ribbits(List<Enemy> threats, string mode, TrainOnLine train)
    {
        if (mode.Length == 0)
            return threats;
        var phase = mode switch
        {
            "hop" => SpinePhase.Dormant,
            "swell" => SpinePhase.Telegraph,
            "tongue" or "devour" => SpinePhase.Grab,
            _ => throw new ArgumentException($"--ribbits {mode}: hop, swell, tongue or devour"),
        };
        var pack = threats.OfType<Ribbit>().ToList();
        foreach (var r in pack)
        {
            var at = r.Local;
            if (mode == "devour" && r == pack.MinBy(m => m.Id))
            {
                var them = Lone(train).Feet;
                at = them + ((at - them) with { Y = 0 }).Normalized * 0.8;
            }
            r.Restore(phase, 0.3 + 0.21 * (r.Id - 60), r.Health, r.Attached, at, 0, 0, 0, LoneId, 0);
        }
        return threats;
    }

    /// <summary>Crewmate 4, alone on the ground off the second car's left, facing the Ribbits there (they're after them).</summary>
    public static Crewmate Lone(TrainOnLine train)
    {
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var at = side.ToWorld(new Double3(-(side.Shape.HalfWidth + 2.2), 0, -1.5));
        var toward = side.ToWorld(new Double3(-(side.Shape.HalfWidth + 5), 0, -1.5)) - at;
        return new Crewmate(LoneId, at, Math.Atan2(-toward.X, -toward.Z), true, default, default);
    }

    public const byte LoneId = 4;

    /// <summary>
    /// The staged track debris as the kind its id makes it (<c>dt screenshot --threats --debris k</c>, Art/DebrisKit: 0 a
    /// fallen pine, 1 a rockfall, 2 a heap of old ties and a rail), where it lies 40 m up the line in the lamp.
    /// </summary>
    public static List<Enemy> Debris(List<Enemy> threats, string kind)
    {
        if (kind.Length == 0 || threats.OfType<Sleepers>().FirstOrDefault() is not { } was)
            return threats;
        var debris = new Sleepers(int.Parse(kind));
        debris.Restore(was.Phase, was.PhaseSeconds, was.Health, was.Attached, was.Local, was.LineDistance, was.Lateral, was.Height, was.Extra, was.Extra2);
        threats[threats.IndexOf(was)] = debris;
        return threats;
    }
    /// <summary>How far out from its gap the staged Whistler's nest is (a camera on the trail: <c>--whistler nest --view trail</c>).</summary>
    public const double NestOut = 24;

    /// <summary>How far out from its gap the staged Whistler is on its run, carrying its catch (<c>--whistler carry</c>).</summary>
    public const double CarryOut = 7;

    /// <summary>
    /// The staged Whistler as it goes (<c>dt screenshot --whistler</c>): <c>fold</c> hidden in its gap (App. A.4 HIDE),
    /// <c>whistle</c> pulling the cord, <c>watch</c> watching the gap's mouth after (WAIT). The <c>gapside</c> view looks in.
    /// <c>carry</c>: on its run out to its nest with crewmate <see cref="LoneId"/> (<see cref="Carried"/>), the <c>carry</c> view
    /// off its side.
    /// </summary>
    public static List<Enemy> Whistler(List<Enemy> threats, string mode, TrainOnLine? train = null)
    {
        if (mode.Length == 0 || threats.OfType<Whistler>().FirstOrDefault() is not { } w)
            return threats;
        if (mode == "carry" && train is not null && w.Attached >= 0)
        {
            // Carrying its catch off (App. A.4 GRAB): loose, off the train's left, a second into the run out from its gap.
            var f = train.Frames[w.Attached];
            var at = f.ToWorld(w.Local + new Double3(-CarryOut, 0, 0)) with { Y = f.ToWorld(Double3.Zero).Y };
            w.Restore(SpinePhase.Grab, 1.0, w.Health, Enemy.Loose, at, 0, 0, 0, 0, 0, holding: LoneId);
            return threats;
        }
        if (mode == "nest" && train is not null && w.Attached >= 0)
        {
            // At its nest with its catch (App. A.4): loose, off the train's left, the run out from its gap done (GreyboxScene
            // sets it on the land, its trail behind it).
            var f = train.Frames[w.Attached];
            var at = f.ToWorld(w.Local + new Double3(-NestOut, 0, 0)) with { Y = f.ToWorld(Double3.Zero).Y };
            w.Restore(SpinePhase.Grab, 18, w.Health, Enemy.Loose, at, 0, 0, 0, 0, 0, holding: LoneId);
            return threats;
        }
        var (phase, extra) = mode switch
        {
            "fold" => (SpinePhase.Dormant, 0.0),
            "whistle" => (SpinePhase.Telegraph, 1.0),
            "watch" => (SpinePhase.Commit, 0.0),
            _ => throw new ArgumentException($"--whistler {mode}: fold, whistle, watch, carry or nest"),
        };
        w.Restore(phase, 0.4, w.Health, w.Attached, w.Local, 0, 0, 0, extra, 0);
        return threats;
    }

    /// <summary>
    /// The staged Tippy Toesie elsewhere (<c>dt screenshot --tippy</c>): <c>behind</c> crewmate 1, over their shoulder
    /// from the <c>crew</c> view (Crew: they face back down the roof, at its camera); <c>grab</c> on them, its hand over
    /// their mouth (App. A.5 GRAB); <c>in</c> stalking down car 2's aisle, stooped under its roof (the <c>inside</c> view);
    /// <c>door</c> ducking out through car 2's rear end door onto the plate (the <c>door</c> view: note 110).
    /// </summary>
    public static List<Enemy> Tippy(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<TippyToesie>().FirstOrDefault() is not { } tippy)
            return threats;
        int car = Math.Min(2, train.Frames.Count - 1);
        var shape = train.Frames[car].Shape;
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0, x = train.Dynamics.Tuning.Geometry.PlateX;
        switch (mode)
        {
            case "behind" or "grab":
                tippy.Restore(mode == "grab" ? SpinePhase.Grab : SpinePhase.Telegraph, tippy.PhaseSeconds, tippy.Health, tippy.Attached,
                    tippy.Local with { X = -0.7, Z = -6.7 }, 0, 0, 0, tippy.Extra, tippy.Extra2);
                break;
            case var r when r.StartsWith("recoil", StringComparison.Ordinal):
                // Pulled off crewmate 1 s seconds ago (recoil:s, 0.15 by default): hidden again in the sim, but where it was.
                double ago = r.Length > 7 ? double.Parse(r[7..], System.Globalization.CultureInfo.InvariantCulture) : 0.15;
                tippy.Restore(SpinePhase.Dormant, ago, tippy.Health, tippy.Attached, tippy.Local with { X = -0.7, Z = -6.7 }, 0, 0, 0, -1, tippy.Extra2);
                break;
            case "in":
                tippy.Restore(SpinePhase.Telegraph, 5, 3, car, new Double3(-0.35, floor, -3.6), 0, 0, 0, -1, 0);
                break;
            case "door":
                tippy.Restore(SpinePhase.Telegraph, 5, 3, car, new Double3(x, floor, shape.HalfLength - 0.3), 0, 0, 0, -1, 0);
                break;
            default:
                throw new ArgumentException($"--tippy {mode}: behind, grab, recoil[:s], in or door");
        }
        return threats;
    }

    /// <summary>
    /// The staged Gaunt as it goes (<c>dt screenshot --gaunt</c>), on crewmate 4 (alone off the train's left,
    /// <see cref="Lone"/>; the Ribbits put away): <c>sleep</c> heaped up in front of them (App. A.6 ASLEEP), <c>stir</c>
    /// waking as they near it, <c>listen</c> woken and stood over them, <c>angry</c> leant right in (three points of anger),
    /// <c>attack</c> striking at them; <c>in</c> squatted in car 2's aisle, listening (the <c>inside</c> view: note 118). The
    /// <c>gaunt</c> view looks over crewmate 4's shoulder up at it.
    /// </summary>
    public static List<Enemy> Gaunt(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Gaunt>().FirstOrDefault() is not { } gaunt)
            return threats;
        threats.RemoveAll(e => e is Ribbit);
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var before = side.ToWorld(new Double3(-(side.Shape.HalfWidth + GauntOut), 0, -1.5));
        switch (mode)
        {
            case "sleep" or "stir" or "listen" or "angry" or "attack":
                var (phase, extra, anger) = mode switch
                {
                    "sleep" => (SpinePhase.Dormant, -1, 0),
                    "stir" => (SpinePhase.Alert, -1, 0),
                    "listen" => (SpinePhase.Telegraph, LoneId, 0),
                    "angry" => (SpinePhase.Telegraph, LoneId, 3),
                    _ => (SpinePhase.Commit, LoneId, 4),
                };
                gaunt.Restore(phase, 2.2, gaunt.Health, Enemy.Loose, before, 0, 0, 0, extra, anger);
                break;
            case "in":
                int car = Math.Min(2, train.Frames.Count - 1);
                double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
                gaunt.Restore(SpinePhase.Telegraph, 2.2, gaunt.Health, car, new Double3(-0.45, floor, -train.Frames[car].Shape.HalfLength + 4.2), 0, 0, 0, -1, 1);
                break;
            // Leaving with what it took (App. A.6): off from the car's side on the ground, or still aboard making for the
            // door (GauntLoad gives it the body it's carrying).
            case "leave":
                gaunt.Restore(SpinePhase.BreakOff, 2.2, gaunt.Health, Enemy.Loose, before, 0, 0, 0, -1, 0);
                break;
            case "leavein":
                int from = Math.Min(2, train.Frames.Count - 1);
                double deck = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
                gaunt.Restore(SpinePhase.BreakOff, 2.2, gaunt.Health, from, new Double3(0.2, deck, -train.Frames[from].Shape.HalfLength + 3.2), 0, 0, 0, -1, 0);
                break;
            default:
                throw new ArgumentException($"--gaunt {mode}: sleep, stir, listen, angry, attack, in, leave or leavein");
        }
        return threats;
    }

    /// <summary>
    /// A body for a staged Gaunt leaving (<c>--gaunt leave|leavein</c>) to carry off, held under it as Gaunt.Leave holds
    /// it (Bodies.TakeAlong: by the hips, high as it stalks, low as it creeps aboard) and settled there for 1.5 s.
    /// </summary>
    public static Sim.Physics.Bodies GauntLoad(TrainOnLine train, string content, Gaunt gaunt)
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var g = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).Gaunt;
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var bodies = new Sim.Physics.Bodies();
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, 0, player) with { Health = 0 };
        var body = bodies.SpawnRagdoll(train, 6, dead);
        gaunt.Extra = body.Id;
        bool aboard = gaunt.Attached >= 0;
        // Facing where it's going: aboard, toward the car's front door; off it, away from the car.
        double yaw;
        if (aboard)
            yaw = 0;
        else
        {
            var frame = train.Frames[Math.Min(2, train.Frames.Count - 1)];
            var away = frame.Right * -1;
            yaw = Math.Atan2(-away.X, -away.Z);
        }
        for (int i = 0; i < 90; i++)
        {
            bodies.TakeAlong(body, train, gaunt.Id, aboard ? gaunt.Attached : Sim.Player.PlayerState.World,
                gaunt.Local + Double3.Up * (aboard ? g.CarryLow : g.CarryHigh), yaw);
            bodies.Step(train, tuning, _ => null);
        }
        return bodies;
    }

    /// <summary>
    /// The staged Grumbler (<c>dt screenshot --grumbler</c>), in front of crewmate 4 off the train's left (the Ribbits and
    /// the Gaunt put away): <c>gnaw</c> at a crate (App. A.8 TELEGRAPH), <c>rear</c> hit and feral, reared up at them,
    /// <c>bite</c> on them, <c>maul</c> on them beaten down (GRAB). The <c>grumbler</c> view looks over their shoulder down
    /// at it.
    /// </summary>
    public static List<Enemy> Grumbler(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Grumbler>().FirstOrDefault() is not { } g)
            return threats;
        threats.RemoveAll(e => e is Ribbit or Sim.Enemies.Gaunt);
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var before = side.ToWorld(new Double3(-(side.Shape.HalfWidth + GrumblerOut), 0, -1.5));
        var (phase, feral) = mode switch
        {
            "gnaw" => (SpinePhase.Telegraph, 0),
            "rear" => (SpinePhase.Telegraph, 1),
            "bite" => (SpinePhase.Commit, 1),
            "maul" => (SpinePhase.Grab, 1),
            _ => throw new ArgumentException($"--grumbler {mode}: gnaw, rear, bite or maul"),
        };
        g.Restore(phase, 0.6, g.Health, Enemy.Loose, before, 0, 0, 0, -1, feral);
        return threats;
    }

    /// <summary>
    /// The staged Stoker (<c>dt screenshot --stoker</c>, which opens the firebox door): <c>peer</c> watching out of the door
    /// (App. A.5, the soot falling), <c>reach</c> feeding, an arm out after whoever opened it. The <c>firebox</c> view looks
    /// at the door.
    /// </summary>
    public static List<Enemy> Stoker(List<Enemy> threats, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Sim.Enemies.Stoker>().FirstOrDefault() is not { } s)
            return threats;
        var phase = mode switch
        {
            "peer" => SpinePhase.Telegraph,
            "reach" => SpinePhase.Commit,
            _ => throw new ArgumentException($"--stoker {mode}: peer or reach"),
        };
        s.Restore(phase, 1.2, s.Health, s.Attached, s.Local, 0, 0, 0, s.Extra, 0);
        return threats;
    }

    /// <summary>
    /// The staged Follower (<c>dt screenshot --follower</c>): <c>back</c> on crewmate 4's back (App. A.6 RIDE; <see cref="Lone"/>,
    /// whose shoulder the <c>pack</c> view looks over), <c>crawl</c> off them on the ground making for the train, <c>nest</c>
    /// built in car 2's aisle (the <c>inside</c> view). The Ribbits are put away.
    /// </summary>
    public static List<Enemy> Follower(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Sim.Enemies.Follower>().FirstOrDefault() is not { } f)
            return threats;
        threats.RemoveAll(e => e is Ribbit);
        int car = Math.Min(2, train.Frames.Count - 1);
        var side = train.Frames[car];
        switch (mode)
        {
            case "back":
                f.Restore(SpinePhase.Telegraph, 2, f.Health, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 2.2), 1.35, -1.5)), 0, 0, 0, LoneId, 0);
                break;
            case "crawl":
                f.Restore(SpinePhase.Commit, 2, f.Health, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 1.6), 0, -0.9)), 0, 0, 0, -1, 0);
                break;
            case "nest":
                double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
                f.Restore(SpinePhase.Punish, 30, f.Health, car, new Double3(-0.45, floor, -side.Shape.HalfLength + 3.0), 0, 0, 0, -1, 1);
                break;
            default:
                throw new ArgumentException($"--follower {mode}: back, crawl or nest");
        }
        return threats;
    }

    /// <summary>
    /// The staged Climber (<c>dt screenshot --climber</c>), at car 2: <c>run</c> pacing the train beside it (App. A.4),
    /// <c>scrabble</c> up in the gap behind it (MOUNT; the <c>gapside</c> view), <c>walk</c> on its roof for the engine,
    /// <c>crouch</c> waiting in its aisle (the <c>inside</c> view).
    /// </summary>
    public static List<Enemy> Climber(List<Enemy> threats, TrainOnLine train, string mode)
    {
        var c = threats.OfType<Sim.Enemies.Climber>().FirstOrDefault();
        if (mode.Length == 0 || c is null)
            return threats;
        threats.RemoveAll(e => e is Sim.Enemies.Climber && e != c);
        int car = Math.Min(2, train.Frames.Count - 2);
        var at = train.Frames[car];
        var shape = at.Shape;
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0, gap = train.Dynamics.Tuning.Geometry.CouplingGap;
        switch (mode)
        {
            case "run":
                c.Restore(SpinePhase.Dormant, 1, c.Health, Enemy.Loose, at.ToWorld(new Double3(shape.HalfWidth + 1.3, 0, shape.HalfLength - 1.5)), 0, 0, 0, car, 1);
                break;
            case "scrabble":
                c.Restore(SpinePhase.Telegraph, 1, c.Health, car, new Double3(shape.HalfWidth - 0.2, 1.0, shape.HalfLength + gap * 0.5), 0, 0, 0, car, 1);
                break;
            case "walk":
                c.Restore(SpinePhase.Commit, 1, c.Health, car, new Double3(0, shape.RoofHeight, -2), 0, 0, 0, car, 1);
                break;
            case "crouch":
                c.Restore(SpinePhase.Commit, 1, c.Health, car, new Double3(-0.45, floor, -shape.HalfLength + 3.0), 0, 0, 0, -1, 1);
                break;
            default:
                throw new ArgumentException($"--climber {mode}: run, scrabble, walk or crouch");
        }
        return threats;
    }

    /// <summary>
    /// The staged Passenger (<c>dt screenshot --passenger</c>), in car 2's aisle facing the <c>inside</c> view (App. A.8):
    /// <c>stand</c> hanging about passing for crew (BLEND), <c>walk</c> going down the car, <c>drag</c> hauling someone
    /// (GRAB; <see cref="Dragged"/> is them), <c>pin</c> stopped at the coupling heaving at the pin (UNCOUPLE). Its paces
    /// (<see cref="PassengerPace"/>) are what a still frame can't measure.
    /// </summary>
    public static List<Enemy> Passenger(List<Enemy> threats, TrainOnLine train, string mode)
    {
        var p = threats.OfType<Sim.Enemies.Passenger>().FirstOrDefault();
        if (mode.Length == 0 || p is null)
            return threats;
        int car = Math.Min(2, train.Frames.Count - 2);
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
        var at = PassengerAt(train);
        var phase = mode switch
        {
            "stand" or "walk" => SpinePhase.Telegraph,
            "drag" or "pin" => SpinePhase.Grab,
            _ => throw new ArgumentException($"--passenger {mode}: stand, walk, drag or pin"),
        };
        p.Restore(phase, 2.4, p.Health, car, at with { Y = floor }, 0, 0, 0, p.Extra, 0);
        return threats;
    }

    /// <summary>How fast the staged Passenger is going (m/s) for <paramref name="mode"/>: walking and dragging at the sim's paces.</summary>
    public static float PassengerPace(string mode) => mode switch
    {
        "walk" => (float)new PassengerTuning().WalkSpeed,
        "drag" => (float)new PassengerTuning().DragSpeed,
        _ => 0,
    };

    /// <summary>The one the staged Whistler's carrying off (<c>--whistler carry</c>) or has at its nest (<c>nest</c>): where the
    /// sim has them, at its middle (the scene hangs them in its forelegs, or lays them in the nest: GreyboxScene.Hung).</summary>
    public static Crewmate Carried(Whistler w) => new(LoneId, w.Local, 0, true, Act: Art.CrewPose.HeldCarried);

    /// <summary>
    /// The staged Car Hugger swallowing (<c>dt screenshot --hugger swallow</c>, App. A.3 GRAB): crewmate <see cref="LoneId"/>
    /// (<see cref="Swallowed"/>) caught at the rear car's end door in front of its mouth. The <c>swallow</c> view looks at it.
    /// </summary>
    public static List<Enemy> Hugger(List<Enemy> threats, string mode)
    {
        if (mode != "swallow" || threats.OfType<CarHugger>().FirstOrDefault(h => h.Attached >= 0) is not { } h)
            return threats;
        h.Restore(SpinePhase.Grab, 1.2, h.Health, h.Attached, h.Local, 0, 0, 0, 0, 0, holding: LoneId);
        return threats;
    }

    /// <summary>The one the staged Car Hugger has (<c>--hugger swallow</c>): where the sim caught and holds them, in reach
    /// of its mouth on the rear car's floor, off to one side (the scene stands them bent into the mouth: GreyboxScene.Hung).</summary>
    public static Crewmate Swallowed(TrainOnLine train)
    {
        var rear = train.Frames[train.Dynamics.Consist.Vehicles[^1].Id];
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 1.1;
        var at = rear.ToWorld(new Double3(0.3, floor, rear.Shape.HalfLength - 1.4));
        var facing = rear.DirToWorld(new Double3(0, 0, 1));
        return new Crewmate(LoneId, at, Math.Atan2(-facing.X, -facing.Z), true, Act: Art.CrewPose.HeldMouth);
    }

    /// <summary>The one the staged Passenger is dragging (<c>--passenger drag</c>): down on the floor at its feet, where the sim has them.</summary>
    public static Crewmate Dragged(TrainOnLine train)
    {
        int car = Math.Min(2, train.Frames.Count - 2);
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 0;
        return new Crewmate(LoneId, train.Frames[car].ToWorld(PassengerAt(train) with { Y = floor }), Math.PI * 0.5, false);
    }

    static Double3 PassengerAt(TrainOnLine train) =>
        new(-0.35, 0, -train.Frames[Math.Min(2, train.Frames.Count - 2)].Shape.HalfLength + 5.6);

    /// <summary>
    /// The staged Soot Child (<c>dt screenshot --soot</c>), off car 2's left in front of crewmate 4 (<see cref="Lone"/>; the
    /// Ribbits put away): <c>huddle</c> squatted in the ash, <c>call</c> calling to them (App. A.6 CALL; black eyes, black
    /// hands), <c>real</c> the same, a real child (ROLL), <c>drink</c> on them (GRAB). The <c>soot</c> view looks over their
    /// shoulder at it.
    /// </summary>
    public static List<Enemy> Soot(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0 || threats.OfType<SootChildren>().FirstOrDefault() is not { } c)
            return threats;
        threats.RemoveAll(e => e is Ribbit);
        var side = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        var before = side.ToWorld(new Double3(-(side.Shape.HalfWidth + SootOut), 0, -1.5));
        switch (mode)
        {
            case "huddle" or "call" or "real":
                c.Restore(SpinePhase.Telegraph, 1.1, c.Health, Enemy.Loose, before, 0, 0, 0, mode == "huddle" ? 0 : 1, mode == "real" ? 0 : 1);
                break;
            case "drink":
                c.Restore(SpinePhase.Grab, 2.5, c.Health, Enemy.Loose, Lone(train).Feet, 0, 0, 0, 0, 1);
                break;
            default:
                throw new ArgumentException($"--soot {mode}: huddle, call, real or drink");
        }
        return threats;
    }

    // How far off the second car's side the staged Soot Child is (m): in front of crewmate 4, a few metres out.
    const double SootOut = 4.6;

    /// <summary>
    /// The staged Switchman (<c>dt screenshot --switchman</c>), at its lever 55 m up the line (App. A.7): <c>wait</c> by it
    /// (TELEGRAPH), <c>grip</c> its hand on it, waiting to throw it under the train (COMMIT, the derailer's tell),
    /// <c>throw</c> it thrown (PUNISH). The <c>switchman</c> view is on the line short of it.
    /// </summary>
    public static List<Enemy> Switchman(List<Enemy> threats, string mode)
    {
        if (mode.Length == 0 || threats.OfType<Sim.Enemies.Switchman>().FirstOrDefault() is not { } s)
            return threats;
        var (phase, derail) = mode switch
        {
            "wait" => (SpinePhase.Telegraph, 0.0),
            "grip" => (SpinePhase.Commit, 1.0),
            "throw" => (SpinePhase.Punish, 1.0),
            _ => throw new ArgumentException($"--switchman {mode}: wait, grip or throw"),
        };
        s.Restore(phase, 0.4, s.Health, s.Attached, s.Local, s.LineDistance, s.Lateral, s.Height, s.Extra, derail);
        return threats;
    }

    /// <summary>
    /// The staged Moose (<c>dt screenshot --moose</c>; note 339, docs/design/creatures/moose.md), on the ground off the
    /// stopped engine's left ahead of it, where its headlamp reaches (beside the line, never on it: well outside the track's
    /// clearance). Its ears and posture are its meter: <c>graze</c> unbothered, <c>listen</c> its head up (aggro past
    /// listenAt), <c>warn</c> ears flat and the sac ridge up (past warnAt); riled at crewmate 4 (<see cref="MooseCrewmate"/>,
    /// out on the ground in front of the engine), <c>squareup</c> the rack levelled at them, <c>charge</c> coming at them,
    /// <c>wheel</c> skidding past, <c>snag</c> its rack jammed, <c>search</c> casting about, <c>pin</c> them under the rack.
    /// The <c>moose</c> view is a crewmate's eye beside the engine; <c>moosecharge</c> over crewmate 4's shoulder.
    /// </summary>
    public static List<Enemy> Moose(List<Enemy> threats, TrainOnLine train, string mode)
    {
        if (mode.Length == 0)
            return threats;
        threats.RemoveAll(e => e is Sim.Enemies.Moose);
        var (phase, doing, aggro, seconds) = mode switch
        {
            "graze" => (SpinePhase.Dormant, MooseMode.Graze, 0.0, 2.0),
            "listen" => (SpinePhase.Alert, MooseMode.Graze, 35.0, 1.2),
            "warn" => (SpinePhase.Alert, MooseMode.Graze, 75.0, 1.45),
            "squareup" => (SpinePhase.Telegraph, MooseMode.SquareUp, 100.0, 1.95),
            "charge" => (SpinePhase.Commit, MooseMode.Charge, 100.0, 0.42),
            "wheel" => (SpinePhase.Commit, MooseMode.Wheel, 100.0, 1.05),
            "snag" => (SpinePhase.Commit, MooseMode.Snag, 100.0, 0.55),
            "search" => (SpinePhase.Alert, MooseMode.Search, 100.0, 4.0),
            "pin" => (SpinePhase.Grab, MooseMode.Pin, 100.0, 1.6),
            _ => throw new ArgumentException($"--moose {mode}: graze, listen, warn, squareup, charge, wheel, snag, search or pin"),
        };
        var (at, yaw) = MooseAt(train, mode);
        var moose = new Sim.Enemies.Moose(MooseId);
        bool riled = aggro >= 100;
        moose.Restore(phase, seconds, 1, Enemy.Loose, at, train.Dynamics.Distance, yaw, (double)doing, riled ? LoneId : -1, aggro,
            holding: phase == SpinePhase.Grab ? LoneId : -1);
        threats.Add(moose);
        return threats;
    }

    public const int MooseId = 311;

    /// <summary>
    /// Where the staged Moose stands and its heading (a player's yaw): grazing, listening or warning, 22 m up the line from
    /// the engine's front and 4.6 m off its left (in the headlamp's cone, clear of the track's clearance), three-quarters on
    /// to the crewmate whose eye the <c>moose</c> view is (<see cref="MooseWatcher"/>); riled, 10 m beyond crewmate 4 and
    /// coming straight at them (pinning, stood over them).
    /// </summary>
    public static (Double3 At, double Yaw) MooseAt(TrainOnLine train, string mode)
    {
        bool riled = mode is not ("graze" or "listen" or "warn" or "");
        var at = riled ? Lineside(train, 19, -4.6) : Lineside(train, 22, -4.6);
        var toward = (riled ? MooseCrewmate(train, mode).Feet : MooseWatcher(train)) - at;
        double yaw = Math.Atan2(-toward.X, -toward.Z);
        // Grazing it's side on to them, browsing; listening and warning its head's come round to them.
        yaw += mode switch { "graze" => 1.2, "listen" or "warn" => 0.6, "wheel" => 2.3, "search" => 0.9, _ => 0 };
        if (mode == "pin")
            at = OnGround(train, MooseCrewmate(train, mode).Feet - new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw)) * Art.CreatureArt.MoosePinReach);
        return (at, yaw);
    }

    /// <summary>The crewmate the <c>moose</c> view looks out of: on the ground out off the line's left ahead of the engine,
    /// 15 m from the Moose, across the headlamp's beam from it (so it's seen side-lit, the engine and its lamp beyond).</summary>
    public static Double3 MooseWatcher(TrainOnLine train) => Lineside(train, 30, -15);

    /// <summary>
    /// Crewmate 4 out on the ground in front of the engine off the line's left (the staged Moose riled at them, <c>--moose</c>
    /// squareup and on), facing it; pinned, on their back under its rack (held_pinned).
    /// </summary>
    public static Crewmate MooseCrewmate(TrainOnLine train, string mode)
    {
        var at = Lineside(train, 9, -3.8);
        var toward = Lineside(train, 19, -4.6) - at;
        return new Crewmate(LoneId, at, Math.Atan2(-toward.X, -toward.Z), true, Act: mode == "pin" ? Art.CrewPose.HeldPinned : null);
    }

    /// <summary>A point on the ground <paramref name="ahead"/> m up the line from the engine's front and <paramref name="lateral"/>
    /// m off it (its right +), wherever the line bends.</summary>
    public static Double3 Lineside(TrainOnLine train, double ahead, double lateral)
    {
        var t = train.Line.Sample(train.Dynamics.Distance + ahead);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return OnGround(train, t.Position + right * lateral);
    }

    static Double3 OnGround(TrainOnLine train, Double3 at)
    {
        double hint = train.Dynamics.Distance;
        return at with { Y = Sim.Player.PlayerMotor.GroundAt(at, train.Line, ref hint) };
    }

    // How far off the second car's side the staged Grumbler is (m): in front of crewmate 4, a lunge from them.
    const double GrumblerOut = 3.3;

    // How far off the second car's side the staged Gaunt stands (m): in front of crewmate 4, at its arm's length from them.
    const double GauntOut = 3.4;

    /// <summary>
    /// A roster for the screenshot (T69): you in the cab and three crewmates, named; crew 1 is speaking. A Passenger is aboard
    /// wearing crewmate 2's face, and (GDD v1.4: roll call is verbal) the roster doesn't give it away.
    /// </summary>
    public static (IReadOnlyList<RosterLine> Lines, Func<byte, double?> Heard) Roster(TrainOnLine train, string content)
    {
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var world = new Sim.World(train, null);
        var p = new Passenger(48);
        p.Restore(SpinePhase.Telegraph, 20, 1, Math.Min(3, train.Frames.Count - 1), default, 0, 0, 0, 2, 0);
        world.MirrorEnemies([p]);
        foreach (var (id, name) in new[] { (1, "Dunmore"), (2, "Okafor"), (3, "Reyes"), (4, "Dave") })
            world.Names[id] = name;
        var cab = Sim.Player.PlayerMotor.SpawnInCab(train, player);
        var roof = Sim.Player.PlayerMotor.SpawnOnRoof(train, Math.Min(2, train.Frames.Count - 1), 0, player);
        var lines = NetPlaySession.RosterOf(4, cab, [(1, roof), (2, roof with { Parent = 1 }), (3, cab)], world);
        return (lines, id => id switch { 1 => 0.5, 2 => 14, _ => null });
    }

    /// <summary>
    /// The staged night's crew for <see cref="Report"/>: Dave and Dunmore in the cab, Okafor on car 3's roof, Priya on the line
    /// 180 m behind.
    /// </summary>
    public static List<(int Id, Sim.Player.PlayerState State)> ReportCrew(TrainOnLine train)
    {
        var cab = Sim.Player.PlayerMotor.SpawnInCab(train, DefaultPlayer);
        var beside = Sim.Player.PlayerMotor.SpawnInCab(train, DefaultPlayer, 0.6) with { Yaw = 0.9 };
        var roof = Sim.Player.PlayerMotor.SpawnOnRoof(train, Math.Min(3, train.Frames.Count - 1), 0, DefaultPlayer) with { Yaw = Math.PI, Pitch = -0.25 };
        var line = Sim.Player.PlayerMotor.SpawnOnGround(train.Line.Sample(train.Dynamics.RearDistance - 180).Position, train.Line, train.Dynamics.RearDistance - 180, DefaultPlayer);
        return [(0, cab with { Yaw = -0.5, Pitch = -0.1 }), (1, beside), (2, roof), (3, line)];
    }

    /// <summary>
    /// A night's incident report (GDD v1.4 App. D.12) staged through the real log, formatter and bookmarks: a grab and its
    /// punish beside the death they ended in, a grab somebody got away from, the derailment's crew stills (derailed), and two
    /// of the dead's own bookmarks. The clock is moved between them (<see cref="Sim.Run.Run.Resume"/>) so each has its time.
    /// </summary>
    public static Sim.Run.RunReport Report(Sim.World world, Sim.Run.RunEnd end)
    {
        foreach (var (id, name) in new[] { (0, "Dave"), (1, "Dunmore"), (2, "Okafor"), (3, "Priya") })
            world.Names[id] = name;
        var train = world.Train;
        var log = world.Attribution;
        var marks = world.Bookmarks;
        var run = world.Run!;
        void At(double seconds) => run.Resume(seconds, -1, train.Boiler.Tender, 0);
        log.Drove(0);
        log.Fired(1, 100);
        var crew = ReportCrew(train);
        var (cab, beside, roof, line) = (crew[0].State, crew[1].State, crew[2].State, crew[3].State);
        At(312);
        log.Add(Sim.Run.IncidentLog.Grab(world, 1, beside, "Grabbed by the Dragger"));
        marks.Grab(world, 1, "Grabbed by the Dragger", crew);
        At(604);
        log.Add(Sim.Run.IncidentLog.Grab(world, 2, roof, "Grabbed by the Car Hugger"));
        marks.Grab(world, 2, "Grabbed by the Car Hugger", crew);
        At(610);
        marks.Punish(world, 46, "CarHugger", 2, Sim.Player.PlayerMotor.WorldPosition(roof, train), crew);
        log.Add(Sim.Run.IncidentLog.Death(world, 2, roof with { Death = Sim.Player.DeathCause.Eaten }, null, crew));
        log.Add(new Sim.Run.Incident(Sim.Run.IncidentKind.Rescue, 900, 2, "Freed from the Holdout", "at Hollin Halt", 0, "Broken out by {actor}."));
        At(1012);
        log.Add(Sim.Run.IncidentLog.Death(world, 3, line with { Death = Sim.Player.DeathCause.Cold }, null, crew));
        At(1104);
        marks.Manual(world, 3, 0, cab);
        At(1290);
        marks.Manual(world, 3, 1, beside);
        log.Add(new Sim.Run.Incident(Sim.Run.IncidentKind.Rupture, 1300, -1, "Boiler ruptured", "at km 14", 1, "Last fired: {actor}. At 100 for 20 s."));
        At(1500);
        log.Add(Sim.Run.IncidentLog.Death(world, 1, beside with { Death = Sim.Player.DeathCause.Seized }, null, crew));
        var lines = Sim.Run.IncidentLog.Lines(world, 350, 263, body => false);
        lines.Add(new Sim.Run.ReportLine(Sim.Run.IncidentKind.CarLost, "", "Car 5 finished by the Car Hugger at km 9. Inside: freight, 0.6 car-loads, the body of Okafor."));
        At(1720);
        if (end == Sim.Run.RunEnd.Derailed)
        {
            lines.Add(new Sim.Run.ReportLine(Sim.Run.IncidentKind.Derailed, "", "Consist derailed, 68 km/h at km 17. Took the 50 km/h bend at 68 km/h, 18 km/h too fast. Throttle: Dave.") { Seconds = 1720 });
            // Only Dave's still aboard by then: the derailment's stills are of whoever it took.
            marks.End(world, end, [crew[0], crew[1] with { State = beside with { Death = Sim.Player.DeathCause.Seized } }]);
        }
        var (marked, shown) = Sim.Run.Run.MarkBookmarks(world, lines);
        return new Sim.Run.RunReport(end, 1720, 17.2, 4, 1, 2.1, 0, 120, 6, 40, -1416, 1, 3, Deaths: 3, CrewLossFees: 1050) { Lines = marked, Bookmarks = shown };
    }

    static readonly Sim.Player.PlayerTuning DefaultPlayer = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(DataFile.FindContentRoot(), Sim.Player.PlayerTuning.File));

    /// <summary>One of each enemy (GDD v1.1 §21) mid-telegraph or mid-commit around the train, where a view can see it.</summary>
    /// <param name="dollAhead">How far up the line the Track Doll stands (App. A.2's reveal is 200 m in the lamp).</param>
    /// <param name="lurkAhead">If given, a second Car Hugger lurking beside the line this far ahead (App. A.3 LURK).</param>
    /// <summary>The staged car fire smouldering instead (its TELEGRAPH, App. C.5: smoke before flame): dt screenshot --threats --smoulder.</summary>
    public static List<Enemy> Smoulder(List<Enemy> threats)
    {
        foreach (var e in threats)
            if (e is CarFire fire)
            {
                fire.Restore(SpinePhase.Telegraph, 8, 1, fire.Attached, fire.Local, 0, 0, 0, 0.15, 0);
                if (fire.Heat.Length > 0)
                {
                    // One cell of the floor going, where it was set.
                    var heat = new double[fire.Heat.Length];
                    heat[Array.IndexOf(fire.Heat, fire.Heat.Max())] = 0.25;
                    fire.RestoreHeat(heat);
                }
            }
        return threats;
    }

    /// <summary>
    /// The staged car fire on its way to jumping the coupling (App. A.5; <c>dt screenshot --threats --spread f</c>): alight,
    /// its blaze <paramref name="fraction"/> of the way to enemies.json carFire.spreadSeconds.
    /// </summary>
    public static List<Enemy> Spread(List<Enemy> threats, double fraction, double spreadSeconds)
    {
        foreach (var e in threats)
            if (e is CarFire fire)
                fire.Restore(fire.Phase, fire.PhaseSeconds, fire.Health, fire.Attached, fire.Local, 0, 0, 0, fire.Extra, fraction * spreadSeconds);
        return threats;
    }

    /// <summary>
    /// A staged fire's cells (GDD App. F.1; note 267), as one left a while burns: hottest where it was set, the walls and roof
    /// over it caught, cooling away along the car, out toward the ends as far as its <paramref name="spread"/>; and its car's
    /// char under the worst of it.
    /// </summary>
    public static void Cells(TrainOnLine train, CarFire fire, double extra, double spread)
    {
        if (FireGrid.Of(train, fire.Attached, new CarFireTuning().CellSize) is not { } grid)
            return;
        var heat = new double[grid.Count];
        var burnt = new byte[grid.Count];
        double reach = 1.5 + 2.5 * extra + 4 * spread;
        for (int i = 0; i < grid.Count; i++)
        {
            double d = Math.Abs(grid.Centre[i].Z - fire.Local.Z) + (grid.Face[i] == FireFace.Floor ? 0 : 0.6) + 0.15 * (i * 7 % 5);
            heat[i] = Math.Clamp(1.1 * (1 - d / reach), 0, 1);
            burnt[i] = (byte)Math.Clamp((int)(heat[i] * 18) - 3, 0, 15);
        }
        fire.RestoreHeat(heat);
        train.Vehicles[fire.Attached].Char = burnt;
    }

    /// <summary>
    /// A hound run coming up behind a fast train (note 328; <c>dt screenshot --run --view run</c>): three pairs of runners on
    /// alternating flanks, the nearest closing on the rear car, the furthest still howling. Only them, so the gun's view of
    /// the line behind is clear.
    /// </summary>
    public static List<Enemy> Run(TrainOnLine train)
    {
        double rear = train.Dynamics.RearDistance;
        var runners = new List<Enemy>();
        (double Behind, double Side, SpinePhase Phase)[] pairs = [(8, 1, SpinePhase.Commit), (20, -1, SpinePhase.Commit), (34, 1, SpinePhase.Telegraph)];
        int id = 60;
        foreach (var (behind, side, phase) in pairs)
            for (int k = 0; k < 2; k++)
            {
                var hound = new CinderHound(id, 60) { Runner = true };
                hound.Restore(phase, 1.5 + k * 0.4, 3, -1, default, rear - behind - k * 3, side * (k == 0 ? 4 : 8), 0.6, 60, 0);
                runners.Add(hound);
                id++;
            }
        return runners;
    }

    public static List<Enemy> Threats(TrainOnLine train, double dollAhead = 22, double? lurkAhead = null)
    {
        var d = train.Dynamics;
        int rear = d.Consist.Vehicles[^1].Id;
        var rearShape = train.Frames[rear].Shape;
        var threats = new List<Enemy>();
        var sleepers = new Sleepers(1);
        sleepers.Restore(SpinePhase.Telegraph, 1.2, 1, -1, default, d.Distance + 40, 0, 0.2, 0, 0);
        threats.Add(sleepers);
        for (int i = 0; i < 3; i++)
        {
            var hound = new CinderHound(10 + i, 10);
            // At the sim's own height off the rail (Rear.cs: -0.3), on the ground: not the 0.6 they were staged at, which ran
            // them a metre up in the air over the ballast's shoulder (their own light showed it, note 337).
            hound.Restore(SpinePhase.Commit, 2, 60, -1, default, d.RearDistance - 14 - i * 6, (i % 2 == 0 ? 1 : -1) * (2.5 + i), -0.3, 10, 0);
            threats.Add(hound);
        }
        var boarded = new CinderHound(13, 10);
        boarded.Restore(SpinePhase.Punish, 0.4, 60, rear, new Double3(0.6, rearShape.RoofHeight, rearShape.HalfLength - 2.5), 0, 0, 0, 10, 0);
        threats.Add(boarded);
        // Latched on the guard van's rear end, grinding (GDD v1.1 A.3): its head on the rear platform, its mouth on the door.
        var hugger = new CarHugger(46);
        hugger.Restore(SpinePhase.Commit, 4, 12, rear, new Double3(0, 1.0, rearShape.HalfLength + 0.4), 0, 0, 0, 0, 0);
        threats.Add(hugger);
        int cargo = d.Consist.Vehicles.First(v => v.Kind == VehicleKind.Cargo).Id;
        var cargoShape = train.Frames[cargo].Shape;
        // Under the cargo car's edge, a limb up over the lip for someone walking too near it.
        var dragger = new Dragger(21);
        dragger.Restore(SpinePhase.Telegraph, 0.6, 1, cargo, new Double3(-(cargoShape.HalfWidth + 0.1), cargoShape.RoofHeight - 0.35, 3), 0, 0, 0, 1, 0);
        threats.Add(dragger);
        // Hidden in the coupling behind the middle car (A.4): it waits there for the next stop's whistle.
        int middle = Math.Max(0, (train.Frames.Count - 1) / 2);
        var whistler = new Whistler(22);
        whistler.Restore(SpinePhase.Alert, 1, 6, middle, CrewSense.GapLocal(train, middle), 0, 0, 0, 0, 0);
        threats.Add(whistler);
        // The first cargo car alight, and Fire Flies swarming the lamp in the middle car (A.5, C.5).
        var fire = CarFire.In(24, train, cargo, 1.5, new CarFireTuning());
        fire.Restore(SpinePhase.Punish, 5, 1, cargo, fire.Local, 0, 0, 0, 0.7, 0);
        Cells(train, fire, 0.7, 0);
        threats.Add(fire);
        if (train.Frames[middle].Shape.Interior is { } room)
        {
            var flies = new FireFlies(25);
            flies.Restore(SpinePhase.Telegraph, 4, 1, middle, new Double3(0, room.Max.Y - 0.4, room.Centre.Z), 0, 0, 0, 0, 0);
            threats.Add(flies);
        }
        // On the rails further up, white face in the lamp (A.2).
        var doll = new TrackDoll(23);
        doll.Restore(SpinePhase.Telegraph, 3, 1, -1, default, d.Distance + dollAhead, 0, 0, 0, 0);
        threats.Add(doll);
        // Haunting the cab at the controls (A.2 TAMPER), where it's heard giggling: left alone to her last stage (note 268).
        var haunting = new TrackDoll(26);
        haunting.Restore(SpinePhase.Punish, 3, 1, 0, train.Frames[0].Shape.Cab!.Value.Centre + new Double3(0.4, -1.35, 0.3), 0, 0, 0, 0, 3);
        threats.Add(haunting);
        // On the ground by the first cargo car, gnawing a crate it's come off (A.8).
        var grumbler = new Grumbler(27);
        grumbler.Restore(SpinePhase.Telegraph, 2, 8, Enemy.Loose, train.Frames[cargo].ToWorld(new Double3(cargoShape.HalfWidth + 3, 0, -2)), 0, 0, 0, -1, 0);
        threats.Add(grumbler);
        // In the firebox, fed (A.5).
        var stoker = Sim.Enemies.Stoker.InFirebox(32, train, false, new StokerTuning());
        stoker.Restore(SpinePhase.Commit, 6, 1, 0, stoker.Local, 0, 0, 0, 0, 0);
        threats.Add(stoker);
        // Tiptoeing up on crewmate 1 on the second car's roof (A.5). (AudioTests hears its tiptoeing from here: Tippy moves
        // it for the camera.)
        int beside = Math.Min(2, train.Frames.Count - 1);
        double roof = train.Frames[beside].Shape.RoofHeight;
        var tippy = new TippyToesie(30);
        tippy.Restore(SpinePhase.Telegraph, 5, 3, beside, new Double3(-0.9, roof, -2.8), 0, 0, 0, 1, Math.PI);
        threats.Add(tippy);
        // Scrabbling up the gap behind the second car on its right, and one already walking the third car's roof (A.4).
        int gapCar = Math.Min(2, train.Frames.Count - 2);
        var gapShape = train.Frames[gapCar].Shape;
        var climbing = new Climber(44);
        climbing.Restore(SpinePhase.Telegraph, 1.2, 1, gapCar, new Double3(gapShape.HalfWidth - 0.2, 1.0, gapShape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap * 0.5), 0, 0, 0, gapCar, 1);
        threats.Add(climbing);
        int roofCar = Math.Min(3, train.Frames.Count - 1);
        var walking = new Climber(45);
        walking.Restore(SpinePhase.Commit, 3, 1, roofCar, new Double3(0, train.Frames[roofCar].Shape.RoofHeight, 2), 0, 0, 0, roofCar, -1);
        threats.Add(walking);
        // On the ground off the train's left: a Ribbit pack lined up, throats swelling (A.6), and a woken Gaunt leaning in.
        var side = train.Frames[beside];
        for (int i = 0; i < 3; i++)
        {
            var ribbit = new Ribbit(60 + i, 60);
            ribbit.Restore(SpinePhase.Telegraph, 1, 3, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 5 + i * 0.3), 0, -3 + i * 1.6)), 0, 0, 0, -1, 0);
            threats.Add(ribbit);
        }
        var gaunt = new Gaunt(47);
        gaunt.Restore(SpinePhase.Telegraph, 5, 8, Enemy.Loose, side.ToWorld(new Double3(side.Shape.HalfWidth + 2.5, 0, 4)), 0, 0, 0, 1, 0.8);
        threats.Add(gaunt);
        // Two of the Choir's ghosts over the guard van's roof (A.7), besieging.
        for (int i = 0; i < 2; i++)
        {
            var ghost = new ChoirGhost(70 + i);
            ghost.Restore(SpinePhase.Commit, 2, 6, Enemy.Loose, train.Frames[rear].ToWorld(new Double3(i == 0 ? -1.2 : 1.3, rearShape.RoofHeight + 1.6 + i * 0.4, -1 + i * 2)), 0, 0, 0, -1, 0);
            threats.Add(ghost);
        }
        // At the side of the line ahead, where a switch stand would be, lantern out.
        var switchman = new Switchman(40);
        switchman.Restore(SpinePhase.Telegraph, 3, 1, -1, default, d.Distance + 55, 3.8, 0, -1, 0);
        threats.Add(switchman);
        // Out in the dark off the second car's side, calling for help: black eyes, from five metres (A.6).
        var soot = new SootChildren(41);
        soot.Restore(SpinePhase.Telegraph, 4, 1, Enemy.Loose, side.ToWorld(new Double3(-(side.Shape.HalfWidth + 6), 0, 6)), 0, 0, 0, 1, 1);
        threats.Add(soot);
        // Among the three on the second car's roof, a fourth (T61): crewmate 2 again, the same cap, the same coat. It lives in
        // the cars' rooms, which no view looks into; staged up here so the art review sees it stand beside the one it copies.
        var passenger = new Passenger(48);
        passenger.Restore(SpinePhase.Telegraph, 20, 1, beside, new Double3(0.55, train.Frames[beside].Shape.RoofHeight, -7.0), 0, 0, 0, 2, Math.PI - 0.3);
        threats.Add(passenger);
        // Nesting in the cargo car's loot (A.6): halfway built.
        if (cargoShape.Interior is { } hold)
        {
            var follower = new Follower(49);
            follower.Restore(SpinePhase.Punish, 30, 2, cargo, new Double3(0.4, hold.Min.Y + 0.05, hold.Max.Z - 1), 0, 0, 0, -1, 0.5);
            threats.Add(follower);
        }
        // Over the first car, spread wide and surging at someone on its roof (T63): the dark coming up over the roof's edge.
        int driftCar = Math.Min(1, train.Frames.Count - 1);
        var drift = new Drift(50);
        drift.Restore(SpinePhase.Telegraph, 2, 1, driftCar, new Double3(0.8, train.Frames[driftCar].Shape.RoofHeight, 2), 0, 0, 0, 6, 1);
        threats.Add(drift);
        if (lurkAhead is { } lurk)
        {
            // Where CarHugger.Lurking puts one: down on the low ground by the line, waiting for the rear car.
            var lurker = new CarHugger(51);
            lurker.Restore(SpinePhase.Dormant, 3, 16, -1, default, d.Distance + lurk, 2.6, -0.3, 0, 0);
            threats.Add(lurker);
        }
        return threats;
    }
}
