using System.Numerics;
using System.Text;
using Silk.NET.OpenXR;
using XrAction = Silk.NET.OpenXR.Action;

namespace Ballast.Xr;

/// <summary>A tracked hand: where the grip is in the tracking space, when the runtime knows.</summary>
public readonly record struct XrHand(bool Tracked, Vector3 Position, Quaternion Orientation);

/// <summary>What the controllers say this frame, in engine terms the game maps to intent.</summary>
public readonly record struct XrControllerState
{
    /// <summary>Left stick: walk. Right stick: turn (x).</summary>
    public Vector2 Move { get; init; }
    public Vector2 Turn { get; init; }
    /// <summary>Either grip: take hold of things (Use).</summary>
    public bool Grip { get; init; }
    /// <summary>Either trigger: fire.</summary>
    public bool Trigger { get; init; }
    /// <summary>A / X: jump.</summary>
    public bool Primary { get; init; }
    /// <summary>B / Y: throw.</summary>
    public bool Secondary { get; init; }
    /// <summary>Left stick click: run.</summary>
    public bool Run { get; init; }
    public bool Menu { get; init; }
    public XrHand Left { get; init; }
    public XrHand Right { get; init; }
    /// <summary>The interaction profile the runtime bound (the left hand's, else the right's), e.g. /interaction_profiles/oculus/touch_controller.</summary>
    public string? Profile { get; init; }
}

/// <summary>
/// OpenXR actions for the game (roadmap M4): one action set with suggested bindings for Quest Touch, Valve Index and
/// the Khronos simple controller, so any runtime binds something. Engine-level names only: the game decides that
/// grip means Use and a primary button means Jump.
/// </summary>
public sealed unsafe class XrControls : IDisposable
{
    readonly XR _xr;
    readonly Instance _instance;
    readonly Session _session;
    readonly ActionSet _set;
    readonly ulong _left, _right;
    readonly XrAction _move, _turn, _grip, _trigger, _primary, _secondary, _run, _menu, _pose;
    readonly Space _leftSpace, _rightSpace;
    string? _profile;
    bool _profileStale = true;

    internal XrControls(XR xr, Instance instance, Session session)
    {
        _xr = xr;
        _instance = instance;
        _session = session;
        var setInfo = new ActionSetCreateInfo { Type = StructureType.ActionSetCreateInfo, Priority = 0 };
        Copy("gameplay", setInfo.ActionSetName, 64);
        Copy("Gameplay", setInfo.LocalizedActionSetName, 128);
        ActionSet set;
        XrHeadset.Check(xr.CreateActionSet(instance, &setInfo, &set), "xrCreateActionSet");
        _set = set;
        _left = Path("/user/hand/left");
        _right = Path("/user/hand/right");

        _move = Create("move", "Move", ActionType.Vector2fInput);
        _turn = Create("turn", "Turn", ActionType.Vector2fInput);
        _grip = Create("grip", "Grab / use", ActionType.BooleanInput, both: true);
        _trigger = Create("trigger", "Fire", ActionType.BooleanInput, both: true);
        _primary = Create("primary", "Jump", ActionType.BooleanInput);
        _secondary = Create("secondary", "Throw", ActionType.BooleanInput);
        _run = Create("run", "Run", ActionType.BooleanInput);
        _menu = Create("menu", "Menu", ActionType.BooleanInput);
        _pose = Create("hand", "Hand pose", ActionType.PoseInput, both: true);

        Suggest("/interaction_profiles/oculus/touch_controller",
        [
            (_move, "/user/hand/left/input/thumbstick"), (_turn, "/user/hand/right/input/thumbstick"),
            (_grip, "/user/hand/left/input/squeeze/value"), (_grip, "/user/hand/right/input/squeeze/value"),
            (_trigger, "/user/hand/left/input/trigger/value"), (_trigger, "/user/hand/right/input/trigger/value"),
            (_primary, "/user/hand/right/input/a/click"), (_secondary, "/user/hand/right/input/b/click"),
            (_run, "/user/hand/left/input/thumbstick/click"), (_menu, "/user/hand/left/input/menu/click"),
            (_pose, "/user/hand/left/input/grip/pose"), (_pose, "/user/hand/right/input/grip/pose"),
        ]);
        Suggest("/interaction_profiles/valve/index_controller",
        [
            (_move, "/user/hand/left/input/thumbstick"), (_turn, "/user/hand/right/input/thumbstick"),
            (_grip, "/user/hand/left/input/squeeze/value"), (_grip, "/user/hand/right/input/squeeze/value"),
            (_trigger, "/user/hand/left/input/trigger/click"), (_trigger, "/user/hand/right/input/trigger/click"),
            (_primary, "/user/hand/right/input/a/click"), (_secondary, "/user/hand/right/input/b/click"),
            (_run, "/user/hand/left/input/thumbstick/click"), (_menu, "/user/hand/left/input/system/click"),
            (_pose, "/user/hand/left/input/grip/pose"), (_pose, "/user/hand/right/input/grip/pose"),
        ]);
        // The fallback every runtime has: select is Use on the left and Fire on the right; no sticks.
        Suggest("/interaction_profiles/khr/simple_controller",
        [
            (_grip, "/user/hand/left/input/select/click"), (_trigger, "/user/hand/right/input/select/click"),
            (_menu, "/user/hand/left/input/menu/click"),
            (_pose, "/user/hand/left/input/grip/pose"), (_pose, "/user/hand/right/input/grip/pose"),
        ]);

        var attach = new SessionActionSetsAttachInfo { Type = StructureType.SessionActionSetsAttachInfo, CountActionSets = 1, ActionSets = &set };
        XrHeadset.Check(xr.AttachSessionActionSets(session, &attach), "xrAttachSessionActionSets");
        _leftSpace = HandSpace(_left);
        _rightSpace = HandSpace(_right);
    }

    ulong Path(string path)
    {
        ulong p;
        XrHeadset.Check(_xr.StringToPath(_instance, path, &p), "xrStringToPath");
        return p;
    }

    XrAction Create(string name, string label, ActionType type, bool both = false)
    {
        var hands = stackalloc ulong[2] { _left, _right };
        var info = new ActionCreateInfo
        {
            Type = StructureType.ActionCreateInfo,
            ActionType = type,
            CountSubactionPaths = both ? 2u : 0u,
            SubactionPaths = both ? hands : null,
        };
        Copy(name, info.ActionName, 64);
        Copy(label, info.LocalizedActionName, 128);
        XrAction action;
        XrHeadset.Check(_xr.CreateAction(_set, &info, &action), $"xrCreateAction({name})");
        return action;
    }

    void Suggest(string profile, (XrAction Action, string Path)[] bindings)
    {
        var list = stackalloc ActionSuggestedBinding[bindings.Length];
        for (int i = 0; i < bindings.Length; i++)
            list[i] = new ActionSuggestedBinding { Action = bindings[i].Action, Binding = Path(bindings[i].Path) };
        var suggested = new InteractionProfileSuggestedBinding
        {
            Type = StructureType.InteractionProfileSuggestedBinding,
            InteractionProfile = Path(profile),
            CountSuggestedBindings = (uint)bindings.Length,
            SuggestedBindings = list,
        };
        // A runtime that doesn't know a profile says so; the others still bind.
        var result = _xr.SuggestInteractionProfileBinding(_instance, &suggested);
        if (result < 0 && result != Result.ErrorPathUnsupported)
            XrHeadset.Check(result, $"xrSuggestInteractionProfileBindings({profile})");
    }

    Space HandSpace(ulong hand)
    {
        var info = new ActionSpaceCreateInfo
        {
            Type = StructureType.ActionSpaceCreateInfo,
            Action = _pose,
            SubactionPath = hand,
            PoseInActionSpace = new Posef { Orientation = new Quaternionf { W = 1 } },
        };
        Space space;
        XrHeadset.Check(_xr.CreateActionSpace(_session, &info, &space), "xrCreateActionSpace");
        return space;
    }

    /// <summary>Reads the controllers at <paramref name="time"/>, hands located in <paramref name="baseSpace"/>.</summary>
    internal XrControllerState Sync(long time, Space baseSpace)
    {
        var active = new ActiveActionSet { ActionSet = _set, SubactionPath = 0 };
        var sync = new ActionsSyncInfo { Type = StructureType.ActionsSyncInfo, CountActiveActionSets = 1, ActiveActionSets = &active };
        // Not focused (a system menu is up) is a success code: every action reads inactive.
        XrHeadset.Check(_xr.SyncAction(_session, &sync), "xrSyncActions");
        return new XrControllerState
        {
            Move = Vector(_move),
            Turn = Vector(_turn),
            Grip = Bool(_grip),
            Trigger = Bool(_trigger),
            Primary = Bool(_primary),
            Secondary = Bool(_secondary),
            Run = Bool(_run),
            Menu = Bool(_menu),
            Left = Locate(_leftSpace, baseSpace, time),
            Right = Locate(_rightSpace, baseSpace, time),
            Profile = CurrentProfile(),
        };
    }

    /// <summary>The runtime rebound the controllers (they were switched on, or swapped): ask again what they are.</summary>
    internal void ProfileChanged() => _profileStale = true;

    string? CurrentProfile()
    {
        if (_profileStale)
        {
            _profile = Profile(_left) ?? Profile(_right);
            // Nothing bound yet is worth asking again next frame; a binding holds until the runtime says it changed.
            _profileStale = _profile is null;
        }
        return _profile;
    }

    Vector2 Vector(XrAction action)
    {
        var get = new ActionStateGetInfo { Type = StructureType.ActionStateGetInfo, Action = action };
        var state = new ActionStateVector2f { Type = StructureType.ActionStateVector2f };
        XrHeadset.Check(_xr.GetActionStateVector2(_session, &get, &state), "xrGetActionStateVector2f");
        return state.IsActive != 0 ? new Vector2(state.CurrentState.X, state.CurrentState.Y) : Vector2.Zero;
    }

    bool Bool(XrAction action)
    {
        var get = new ActionStateGetInfo { Type = StructureType.ActionStateGetInfo, Action = action };
        var state = new ActionStateBoolean { Type = StructureType.ActionStateBoolean };
        XrHeadset.Check(_xr.GetActionStateBoolean(_session, &get, &state), "xrGetActionStateBoolean");
        return state.IsActive != 0 && state.CurrentState != 0;
    }

    XrHand Locate(Space hand, Space baseSpace, long time)
    {
        var location = new SpaceLocation { Type = StructureType.SpaceLocation };
        if (_xr.LocateSpace(hand, baseSpace, time, &location) < 0)
            return default;
        const SpaceLocationFlags Valid = SpaceLocationFlags.PositionValidBit | SpaceLocationFlags.OrientationValidBit;
        var p = location.Pose;
        return (location.LocationFlags & Valid) == Valid
            ? new XrHand(true, new Vector3(p.Position.X, p.Position.Y, p.Position.Z), new Quaternion(p.Orientation.X, p.Orientation.Y, p.Orientation.Z, p.Orientation.W))
            : default;
    }

    string? Profile(ulong hand)
    {
        var state = new InteractionProfileState { Type = StructureType.InteractionProfileState };
        if (_xr.GetCurrentInteractionProfile(_session, hand, &state) < 0 || state.InteractionProfile == 0)
            return null;
        uint length;
        var buffer = stackalloc byte[256];
        if (_xr.PathToString(_instance, state.InteractionProfile, 256, &length, buffer) < 0 || length == 0)
            return null;
        return Encoding.UTF8.GetString(buffer, (int)length - 1);
    }

    static void Copy(string s, byte* into, int capacity)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        int n = Math.Min(bytes.Length, capacity - 1);
        for (int i = 0; i < n; i++)
            into[i] = bytes[i];
        into[n] = 0;
    }

    public void Dispose()
    {
        _xr.DestroySpace(_leftSpace);
        _xr.DestroySpace(_rightSpace);
        _xr.DestroyActionSet(_set);
    }
}
