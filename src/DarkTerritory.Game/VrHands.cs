using System.Numerics;
using Ballast.Render;
using Ballast.Xr;

namespace DarkTerritory.Game;

/// <summary>
/// A headset player's own hands (roadmap M4): a gloved fist and a cuff where each controller is, so a player can see
/// what they're reaching with. Drawn on this machine only; the crew see the body, not the controllers.
/// </summary>
public static class VrHands
{
    static readonly Vector3 Glove = new(0.30f, 0.24f, 0.17f);
    static readonly Vector3 Cuff = new(0.14f, 0.13f, 0.12f);

    /// <summary>Adds both hands to a mesh built round <paramref name="body"/>'s eye point (as the eyes see it).</summary>
    public static void Build(MeshBuilder mesh, in Camera body, in XrControllerState controllers)
    {
        // The tracking space sits at the eye point, turned to the body's yaw: hands move with it, as the eyes do.
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)body.Yaw);
        Hand(mesh, yaw, controllers.Left);
        Hand(mesh, yaw, controllers.Right);
    }

    /// <summary>Where a hand is in the mesh: relative to the eye point, turned with the body.</summary>
    public static Vector3 Place(Quaternion bodyYaw, in XrHand hand) => Vector3.Transform(hand.Position, bodyYaw);

    static void Hand(MeshBuilder mesh, Quaternion yaw, in XrHand hand)
    {
        if (!hand.Tracked)
            return;
        var q = Quaternion.Concatenate(hand.Orientation, yaw);
        var right = Vector3.Transform(Vector3.UnitX, q);
        var up = Vector3.Transform(Vector3.UnitY, q);
        var back = Vector3.Transform(Vector3.UnitZ, q);
        var at = Place(yaw, hand);
        // OpenXR's grip pose is the centre of the closed fist, −Z out through the thumb end of it.
        mesh.Box(at, right, up, back, new Vector3(0.045f, 0.05f, 0.055f), Glove);
        mesh.Box(at + back * 0.085f, right, up, back, new Vector3(0.04f, 0.04f, 0.03f), Cuff);
    }
}
