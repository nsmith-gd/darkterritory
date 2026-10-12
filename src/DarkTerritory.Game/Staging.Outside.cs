using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The Mourners, the Freight Beetle and Tower Jaw staged for a look (notes 362, 366, 363); the Pickers, the Lodger and
/// the Householder (notes 574, 583, 584).</summary>
public static partial class Staging
{
    public const int MournerId = 3620, BeetleId = 366, TowerJawId = 363, PickerId = 5740, LodgerId = 583, HouseholderId = 584;

    /// <summary>
    /// The staged Pickers (<c>dt screenshot --pickers</c>; note 574, docs/design/creatures/pickers.md) in the headlamp's spill
    /// off the line's left, 15-22 m up from the engine: <c>yard</c> two carrying crates off over their heads, one scuttling
    /// for a third crate on the ground, one coming up out of its drain and one crouched by it; <c>carry</c> close to, one
    /// with a crate and two under a heavy one (the lead, and its second beside it hoisting at its edge). The crates are in
    /// <paramref name="bodies"/>, held where the sim's TakeAlong holds them (enemies.json pickers holdHeight); the paces in
    /// <paramref name="paces"/>.
    /// </summary>
    public static List<Enemy> Pickers(List<Enemy> threats, TrainOnLine train, string content, string mode, out Sim.Physics.Bodies bodies,
        out Dictionary<int, float> paces)
    {
        threats.RemoveAll(e => e is Picker);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).Pickers;
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var made = new Sim.Physics.Bodies();
        var paced = new Dictionary<int, float>();
        var tangent = train.Line.Sample(train.Dynamics.Distance + 18).Tangent;
        var away = Double3.Cross(tangent, Double3.Up).Normalized * -1;
        double Yaw(Double3 way) => Math.Atan2(-way.X, -way.Z);
        var carried = new List<(Sim.Physics.Body Crate, int By, Double3 Hold, double Yaw)>();
        int n = 0;
        Picker Add(Double3 at, double yaw, PickerMode m, SpinePhase phase, double seconds, int crate, float pace)
        {
            var p = Picker.Up(PickerId + n, at, n, train.Dynamics.Distance, yaw, t);
            p.Restore(phase, seconds, t.Health, Enemy.Loose, at, train.Dynamics.Distance, yaw, (double)m, crate, n);
            paced[PickerId + n] = pace;
            n++;
            threats.Add(p);
            return p;
        }
        Sim.Physics.Body Crate(Double3 at, double? heavy = null) => made.SpawnCargo(at, train.Dynamics.Distance + 18, heavy);
        void Carry(Picker p, Sim.Physics.Body crate) => carried.Add((crate, p.Id, p.Local + Double3.Up * t.HoldHeight, p.Lateral));
        if (mode == "carry")
        {
            // One under a crate, coming off toward the camera's side; and a heavy crate up between two, the second at the lead's
            // right, under the crate's edge.
            var at = Lineside(train, 17.5, -5.0);
            var one = Add(at, Yaw(away + tangent * 0.6), PickerMode.Carry, SpinePhase.Telegraph, 0.9, -1, (float)t.Carry);
            var crate = Crate(at);
            one.Restore(one.Phase, one.PhaseSeconds, t.Health, Enemy.Loose, at, one.LineDistance, one.Lateral, one.Height, crate.Id, one.Extra2);
            Carry(one, crate);
            var lead = Lineside(train, 20.5, -6.2);
            double yaw = Yaw(away - tangent * 0.4);
            var leader = Add(lead, yaw, PickerMode.Carry, SpinePhase.Telegraph, 1.3, -1, (float)t.CarryHeavy);
            var heavy = Crate(lead, 0.55);
            leader.Restore(leader.Phase, leader.PhaseSeconds, t.Health, Enemy.Loose, lead, leader.LineDistance, yaw, leader.Height, heavy.Id, leader.Extra2);
            Carry(leader, heavy);
            var right = Double3.Cross(new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw)), Double3.Up).Normalized;
            Add(OnGround(train, lead + right * t.HeaveBeside), yaw, PickerMode.Heave, SpinePhase.Telegraph, 0.7, heavy.Id, (float)t.CarryHeavy);
        }
        else
        {
            // The yard: two off with crates, one racing for a third, one up out of its drain, one crouched by the drain.
            var a = Lineside(train, 15.5, -4.0);
            var b = Lineside(train, 19.0, -5.0);
            var c = Lineside(train, 17.2, -2.9);
            var drain = Lineside(train, 18.5, -7.6);
            foreach (var (at, delay) in new[] { (a, 0.9), (b, 1.7) })
            {
                var p = Add(at, Yaw(away + tangent * (at == a ? -0.3 : 0.3)), PickerMode.Carry, SpinePhase.Telegraph, delay, -1, (float)t.Carry);
                var crate = Crate(at);
                p.Restore(p.Phase, p.PhaseSeconds, t.Health, Enemy.Loose, at, p.LineDistance, p.Lateral, p.Height, crate.Id, p.Extra2);
                Carry(p, crate);
            }
            var loose = Crate(c);
            var runner = OnGround(train, c + away * 2.6 + tangent * 0.8);
            Add(runner, Yaw(c - runner), PickerMode.Scuttle, SpinePhase.Telegraph, 0.6, loose.Id, (float)t.Run);
            Add(drain, Yaw(away * -1), PickerMode.Emerge, SpinePhase.Telegraph, 0.42, -1, 0);
            Add(OnGround(train, drain + tangent * 1.6 + away * 0.4), Yaw(away * -1 + tangent * 0.5), PickerMode.Wait, SpinePhase.Telegraph, 1.1, -1, 0);
        }
        for (int i = 0; i < 40; i++)
        {
            foreach (var (crate, by, hold, yaw) in carried)
                made.TakeAlong(crate, train, by, Sim.Player.PlayerState.World, hold, yaw);
            made.Step(train, tuning, _ => null);
        }
        (bodies, paces) = (made, paced);
        return threats;
    }

    /// <summary>The <c>pickers</c> and <c>pickerscarry</c> views: from beside the engine, out at the staged Pickers in the
    /// headlamp's spill (<c>pickers</c>), or crouched close to the ones under their crates (<c>pickerscarry</c>).</summary>
    public static Camera PickersCamera(TrainOnLine train, string view)
    {
        if (view == "pickerscarry")
            return Camera.LookAt(Lineside(train, 22.6, -3.4) + Double3.Up * 0.6, Lineside(train, 18.8, -5.6) + Double3.Up * 0.55, 52);
        return Camera.LookAt(Lineside(train, 12.0, -1.2) + Double3.Up * 1.45, Lineside(train, 17.8, -5.0) + Double3.Up * 0.3, 56);
    }

    /// <summary>
    /// The staged Lodger (<c>dt screenshot --lodger</c>; note 583) 14 m up the line from the engine and 4 m off its left, turned
    /// to the line: <c>hidden</c> folded into a crouch; <c>shriek</c> reared, the jaw dropped open (0.6 s into it); <c>lunge</c>
    /// mid-leap; <c>chase</c> loping; <c>break</c> bashing; <c>return</c> walking back.
    /// </summary>
    public static List<Enemy> Lodger(List<Enemy> threats, TrainOnLine train, string mode, out float pace)
    {
        threats.RemoveAll(e => e is Lodger);
        var at = Lineside(train, 14, -4.0);
        var toLine = (Lineside(train, 14, 0) - at) with { Y = 0 };
        double yaw = Math.Atan2(-toLine.X, -toLine.Z) + 0.35;
        var (m, phase, seconds) = mode switch
        {
            "hidden" => (LodgerMode.Hidden, SpinePhase.Dormant, 1.2),
            "shriek" => (LodgerMode.Shriek, SpinePhase.Telegraph, 0.62),
            "lunge" => (LodgerMode.Lunge, SpinePhase.Commit, 0.24),
            "chase" => (LodgerMode.Chase, SpinePhase.Grab, 0.3),
            "break" => (LodgerMode.Break, SpinePhase.Grab, 0.47),
            "return" => (LodgerMode.Return, SpinePhase.BreakOff, 0.5),
            _ => throw new ArgumentException($"--lodger {mode}: hidden, shriek, lunge, chase, break or return"),
        };
        pace = m is LodgerMode.Chase ? 5f : m is LodgerMode.Return ? 1f : 0f;
        var lodger = new Lodger(LodgerId);
        lodger.Restore(phase, seconds, 1, Enemy.Loose, at, train.Dynamics.Distance, yaw, (double)m, -1, 0);
        threats.Add(lodger);
        return threats;
    }

    /// <summary>The <c>lodger</c> and <c>lodgershriek</c> views: a crewmate's eye on the ballast a few metres off it.</summary>
    public static Camera LodgerCamera(TrainOnLine train, string view)
    {
        var at = Lineside(train, 14, -4.0);
        return view == "lodgershriek"
            ? Camera.LookAt(Lineside(train, 16.6, -0.6) + Double3.Up * 1.6, at + Double3.Up * 1.45, 56)
            : Camera.LookAt(Lineside(train, 16.2, -1.4) + Double3.Up * 1.5, at + Double3.Up * 0.5, 50);
    }

    /// <summary>
    /// The staged Householder (<c>dt screenshot --householder</c>; note 584) 15 m up the line and 5 m off its left, at a chair
    /// and its set table (RansackKit's: in a night, its house's own), its back to the dark: <c>watch</c> sat, its head turned to
    /// whoever's looking; <c>sit</c>; <c>rise</c> half up; <c>hunt</c> walking at the line; <c>grab</c>; <c>return</c>. The
    /// furniture's in <paramref name="clutter"/>.
    /// </summary>
    public static List<Enemy> Householder(List<Enemy> threats, TrainOnLine train, string mode, out List<(Sim.Run.Clutter, Double3)> clutter,
        out float pace)
    {
        threats.RemoveAll(e => e is Householder);
        var chair = Lineside(train, 15, -5.0);
        var toLine = (Lineside(train, 15, 0) - chair) with { Y = 0 };
        double yaw = Math.Atan2(-toLine.X, -toLine.Z);
        var forward = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        var (m, phase, seconds) = mode switch
        {
            "watch" => (HouseholderMode.Watch, SpinePhase.Alert, 0.6),
            "sit" => (HouseholderMode.Sit, SpinePhase.Dormant, 1.0),
            "rise" => (HouseholderMode.Rise, SpinePhase.Telegraph, 1.5),
            "hunt" => (HouseholderMode.Hunt, SpinePhase.Commit, 0.4),
            "grab" => (HouseholderMode.Grab, SpinePhase.Grab, 0.3),
            "return" => (HouseholderMode.Return, SpinePhase.BreakOff, 0.5),
            _ => throw new ArgumentException($"--householder {mode}: watch, sit, rise, hunt, grab or return"),
        };
        bool sat = m is HouseholderMode.Watch or HouseholderMode.Sit or HouseholderMode.Rise;
        var at = sat ? chair : OnGround(train, chair + forward * 2.4 + toLine.Normalized * 0.0);
        pace = m is HouseholderMode.Hunt ? 1.4f : m is HouseholderMode.Return ? 1f : 0f;
        var householder = new Householder(HouseholderId);
        householder.Restore(phase, seconds, 1, Enemy.Loose, at, train.Dynamics.Distance, yaw, (double)m, -1, 0);
        threats.Add(householder);
        // Its chair, and its table Householder.TableOut in front, square to it (TownKit's frame for a piece: x along its yaw).
        double piece = -(Math.PI / 2 + yaw);
        clutter = [(new Sim.Run.Clutter(Sim.Run.ClutterKind.Chair, 0, 0, 0.2, 0.2, piece, false), chair),
            (new Sim.Run.Clutter(Sim.Run.ClutterKind.Table, 0, 0, 0.6, 0.38, piece, false), OnGround(train, chair + forward * Sim.Enemies.Householder.TableOut))];
        return threats;
    }

    /// <summary>The <c>householder</c> view: across its table from it, a crewmate stood in its doorway's light.</summary>
    public static Camera HouseholderCamera(TrainOnLine train)
    {
        var chair = Lineside(train, 15, -5.0);
        return Camera.LookAt(Lineside(train, 13.4, -1.6) + Double3.Up * 1.65, chair + Double3.Up * 1.0, 50);
    }

    /// <summary>
    /// The staged Mourners (<c>dt screenshot --mourners</c>; note 362, docs/design/creatures/mourners.md) round a crewmate's
    /// body lying 17 m up the line from the engine and 3.4 m off its left: <c>drag</c> two of them taking it straight off from
    /// the line (one hauling it by the clothes, leant back, the other beside), the rest hanging back; <c>wait</c> all four in
    /// a loose ring off it, waiting their chance; <c>creep</c> two edging in on it; <c>startle</c> two starting back from it.
    /// The body's in <paramref name="bodies"/> (as the sim's TakeAlong holds it, dragged).
    /// </summary>
    public static List<Enemy> Mourners(List<Enemy> threats, TrainOnLine train, string content, string mode, out Sim.Physics.Bodies bodies)
    {
        threats.RemoveAll(e => e is Mourner);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).Mourners;
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<Sim.Player.PlayerTuning>(Path.Combine(content, Sim.Player.PlayerTuning.File));
        var lying = MournersBody(train);
        var tangent = train.Line.Sample(train.Dynamics.Distance + 17).Tangent;
        var away = Double3.Cross(tangent, Double3.Up).Normalized * -1;
        var across = Double3.Cross(away, Double3.Up).Normalized;
        bodies = new Sim.Physics.Bodies();
        var dead = Sim.Player.PlayerMotor.SpawnOnRoof(train, 2, 0, player) with { Health = 0 };
        var body = bodies.SpawnRagdoll(train, 7, dead);
        bool dragging = mode == "drag";
        // The hauler stands HoldAt beyond the body's hips, facing back at the line; its partner beside it.
        var hauler = OnGround(train, lying + away * (t.HoldAt + 0.5));
        var spots = new List<(Double3 At, double Yaw, MournerMode Mode, SpinePhase Phase, double Seconds)>();
        double Yaw(Double3 way) => Math.Atan2(-way.X, -way.Z);
        if (dragging)
        {
            spots.Add((hauler, Yaw(away * -1), MournerMode.Drag, SpinePhase.Commit, 0.55));
            spots.Add((OnGround(train, hauler + across * 0.8 - away * 0.15), Yaw(away * -1 - across * 0.3), MournerMode.Drag, SpinePhase.Commit, 0.95));
        }
        // The rest: a loose ring off it on the far side from the line (or nearer in, creeping; or starting back).
        double[] arc = dragging ? [-1.1, 1.3] : [-1.2, -0.4, 0.4, 1.2];
        for (int i = 0; i < arc.Length; i++)
        {
            double r = mode switch { "creep" => i % 2 == 0 ? 2.2 : 7.5, "startle" => i % 2 == 0 ? 4.5 : 8, _ => 6.5 + 1.5 * (i % 2) };
            var dir = away * Math.Cos(arc[i]) + across * Math.Sin(arc[i]);
            var at = OnGround(train, lying + dir * r);
            var (m, phase, seconds) = mode switch
            {
                "creep" when i % 2 == 0 => (MournerMode.Creep, SpinePhase.Telegraph, 0.4 + 0.3 * i),
                "startle" when i % 2 == 0 => (MournerMode.Startle, SpinePhase.Telegraph, 0.12),
                _ => (MournerMode.Wait, SpinePhase.Telegraph, 0.3 + 0.55 * i),
            };
            spots.Add((at, Yaw(lying - at), m, phase, seconds));
        }
        for (int i = 0; i < spots.Count; i++)
        {
            var (at, yaw, m, phase, seconds) = spots[i];
            var e = new Mourner(MournerId + i);
            e.Restore(phase, seconds, 1, Enemy.Loose, at, train.Dynamics.Distance, yaw, (double)m, body.Id, MournerId);
            threats.Add(e);
        }
        // The body: dragged (held by the hips at the hauler's hold, the rest trailing back toward the line), or lying there.
        for (int i = 0; i < 90; i++)
        {
            if (dragging)
                bodies.TakeAlong(body, train, MournerId, Sim.Player.PlayerState.World, hauler - away * t.HoldAt + Double3.Up * t.HoldHeight, Yaw(away));
            else
                bodies.TakeAlong(body, train, -1, Sim.Player.PlayerState.World, lying + Double3.Up * 0.25, Yaw(across));
            bodies.Step(train, tuning, _ => null);
        }
        if (!dragging)
            bodies.LetGo(body);
        for (int i = 0; !dragging && i < 40; i++)
            bodies.Step(train, tuning, _ => null);
        return threats;
    }

    /// <summary>Where the staged Mourners' body lies: 17 m up the line from the engine's front, 3.4 m off its left (in the headlamp's edge).</summary>
    public static Double3 MournersBody(TrainOnLine train) => Lineside(train, 17, -3.4);

    /// <summary>The <c>mourners</c> view: a crewmate's eye beside the engine, looking out at them round the body.</summary>
    public static Camera MournersCamera(TrainOnLine train)
    {
        var body = MournersBody(train);
        var eye = Lineside(train, 20.6, -1.5) + Double3.Up * 1.4;
        return Camera.LookAt(eye, body + (Lineside(train, 17, -6.5) - body) * 0.4 + Double3.Up * 0.35, 46);
    }

    /// <summary>
    /// The staged Freight Beetle (<c>dt screenshot --beetle</c>; note 366, docs/design/creatures/freight-beetle.md) at a
    /// cargo crate on the ground 18 m up the line from the engine and 4.2 m off its left: <c>idle</c> settled beside it,
    /// <c>walk</c> going round it, <c>brace</c> behind it head down, <c>push</c> shoving it on up the line, <c>startle</c>
    /// reared back off it. The crate's in <paramref name="bodies"/>.
    /// </summary>
    public static List<Enemy> Beetle(List<Enemy> threats, TrainOnLine train, string content, string mode, out Sim.Physics.Bodies bodies)
    {
        threats.RemoveAll(e => e is FreightBeetle);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).FreightBeetle;
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        bodies = new Sim.Physics.Bodies();
        var at = BeetleCrate(train);
        var crate = bodies.SpawnCargo(at, train.Dynamics.Distance + 18);
        for (int i = 0; i < 30; i++)
            bodies.Step(train, tuning, _ => null);
        var push = train.Line.Sample(train.Dynamics.Distance + 18).Tangent with { Y = 0 };
        push = push.Normalized;
        double radius = crate.Pbd.Particles[0].Radius;
        var (where, way, m, phase, seconds) = mode switch
        {
            "idle" => (at - push * (radius + 1.3) + Double3.Cross(push, Double3.Up).Normalized * 1.4, push, BeetleMode.Idle, SpinePhase.Dormant, 1.3),
            "walk" => (at + Double3.Cross(push, Double3.Up).Normalized * (radius + 1.4), push * -1, BeetleMode.Walk, SpinePhase.Alert, 0.6),
            "brace" => (at - push * (radius + t.HeadAt), push, BeetleMode.Brace, SpinePhase.Telegraph, 0.5),
            "startle" => (at - push * (radius + t.HeadAt + 0.2), push, BeetleMode.Startle, SpinePhase.Dormant, 0.18),
            "push" => (at - push * (radius + t.HeadAt), push, BeetleMode.Push, SpinePhase.Commit, 0.7),
            _ => throw new ArgumentException($"--beetle {mode}: idle, walk, brace, push or startle"),
        };
        var beetle = FreightBeetle.At(BeetleId, OnGround(train, where), train.Dynamics.Distance, Math.Atan2(-way.X, -way.Z), t);
        beetle.Restore(phase, seconds, t.Health, Enemy.Loose, OnGround(train, where), train.Dynamics.Distance, Math.Atan2(-way.X, -way.Z), (double)m,
            mode == "walk" ? -1 : crate.Id, 0);
        threats.Add(beetle);
        return threats;
    }

    /// <summary>Where the staged Freight Beetle's crate stands: 18 m up the line from the engine's front, 4.2 m off its left
    /// (at the headlamp's edge).</summary>
    public static Double3 BeetleCrate(TrainOnLine train) => Lineside(train, 18, -4.2);

    /// <summary>The <c>beetle</c> view: off the line's left, side on to it and a little in front as it shoves the crate up the line.</summary>
    public static Camera BeetleCamera(TrainOnLine train)
    {
        var crate = BeetleCrate(train);
        var eye = Lineside(train, 19.6, -9.6) + Double3.Up * 1.5;
        return Camera.LookAt(eye, crate + (Lineside(train, 15.5, -4.2) - crate) * 0.55 + Double3.Up * 0.6, 50);
    }

    /// <summary>
    /// The staged Tower Jaw (<c>dt screenshot --towerjaw</c>; note 363, docs/design/creatures/tower-jaw.md) at the night's
    /// first coaling tower (<paramref name="tower"/>): <c>gnaw</c> at its leg on the line's side, a third through; <c>lean</c>
    /// nine tenths through, the tower leaning over the line; <c>threat</c> reared at someone come close; <c>lunge</c>; <c>away</c>
    /// loping off; <c>wreck</c> the tower down across the line (the creature gone, this its wreck).
    /// </summary>
    public static List<Enemy> TowerJaw(List<Enemy> threats, TrainOnLine train, string content, Sim.Run.Run run, RouteFeature tower, string mode)
    {
        threats.RemoveAll(e => e is TowerJaw);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).TowerJaw;
        var (spout, _) = run.ChuteAt(tower, train.Line);
        var on = train.Line.Sample(spout);
        var right = Double3.Cross(on.Tangent, Double3.Up).Normalized;
        double side = tower.Side < 0 ? -1 : 1;
        var leg = on.Position + right * (side * t.TowerLegOut);
        var away = ((leg - on.Position) with { Y = 0 }).Normalized;
        var at = OnGround(train, leg + away * t.GnawAt);
        double facing = Math.Atan2(away.X, away.Z);
        var (m, phase, seconds, gnawed) = mode switch
        {
            "gnaw" => (TowerJawMode.Gnaw, SpinePhase.Alert, 0.7, 0.3),
            "lean" => (TowerJawMode.Gnaw, SpinePhase.Alert, 1.3, 0.92),
            "threat" => (TowerJawMode.Threat, SpinePhase.Telegraph, 1.0, 0.6),
            "lunge" => (TowerJawMode.Lunge, SpinePhase.Telegraph, 0.27, 0.6),
            "away" => (TowerJawMode.Away, SpinePhase.BreakOff, 0.5, 0.6),
            "wreck" => (TowerJawMode.Wreck, SpinePhase.Punish, 3.0, 1.0),
            _ => throw new ArgumentException($"--towerjaw {mode}: gnaw, lean, threat, lunge, away or wreck"),
        };
        var jaw = new TowerJaw(TowerJawId);
        if (m == TowerJawMode.Wreck)
            jaw.Restore(phase, seconds, t.Health, Enemy.Loose, on.Position, spout, facing, (double)m, 1, 0);
        else
        {
            // Threatening, lunging or off, it's turned from the post: to the line (whoever's come), or away from it.
            double yaw = m switch { TowerJawMode.Threat or TowerJawMode.Lunge => facing + 0.5 * side, TowerJawMode.Away => facing + Math.PI * 0.8, _ => facing };
            var where = m == TowerJawMode.Away ? OnGround(train, at + away * 6) : at;
            jaw.Restore(phase, seconds, t.Health, Enemy.Loose, where, spout, yaw, (double)m, gnawed, 0);
        }
        threats.Add(jaw);
        return threats;
    }

    /// <summary>
    /// The <c>towerjaw</c> and <c>towerjawfall</c> views: stood on the line short of the coaling tower, across from it, up at
    /// it leaning over the line with the beaver at its leg; or along the line at its wreck lying across the rails.
    /// </summary>
    public static Camera TowerJawCamera(TrainOnLine train, Sim.Run.Run run, RouteFeature tower, string view)
    {
        var (spout, _) = run.ChuteAt(tower, train.Line);
        var on = train.Line.Sample(spout);
        var right = Double3.Cross(on.Tangent, Double3.Up).Normalized;
        double side = tower.Side < 0 ? -1 : 1;
        if (view == "towerjawfall")
        {
            var stand = train.Line.Sample(spout - 21);
            return Camera.LookAt(stand.Position + right * (side * 2.4) + Double3.Up * 2.6, on.Position + right * (-side * 1.5) + Double3.Up * 1.0, 62);
        }
        var near = train.Line.Sample(spout - 14);
        return Camera.LookAt(near.Position + right * (-side * 1.6) + Double3.Up * 1.3, on.Position + right * (side * 4.4) + Double3.Up * 5.5, 74);
    }
}
