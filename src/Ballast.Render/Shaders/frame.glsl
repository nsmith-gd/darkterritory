#include "view.glsl"
// The frame's constants, shared by every scene shader (std140; mirrors FrameData in GreyboxRenderer.cs).
layout(set = 0, binding = 0) uniform Frame {
    mat4 viewProj;
    mat4 invViewProj;
    mat4 lampViewProj; // camera-relative to the headlamp's shadow map
    vec4 fog;          // rgb colour, a = density per metre
    vec4 fogHeight;    // x = fog base height (camera-relative), y = falloff per metre up, z = density kept up high, w = seconds
    vec4 moon;         // xyz towards the moon, w = ambient
    vec4 moonColour;   // rgb, a = direct strength
    vec4 lampPos;      // xyz headlamp (camera-relative), w = range
    vec4 lampDir;      // xyz its direction, w = cos(cone half-angle)
    vec4 lampColour;   // rgb, a = intensity
    vec4 sky;          // rgb zenith, a = how far the backdrop sinks into the fog
    vec4 params;       // x = point lights, y = 1 for the PS2 look, z = shader grime over textures, w = layers loaded
    vec4 sky2;         // x = the backdrop band's height in radians, y = fog curve exponent, z = horizon haze over the fog, w = wetness
    vec4 lights[64];   // pairs: xyz position (camera-relative) + range, rgb colour
    vec4 rooms[48];    // triples: centre + half x, right axis + half y, back axis + half z (up = back x right)
    vec4 counts;       // x = rooms, y = 1 when the moon casts a shadow, z = one texel of its map, w = frost
    mat4 moonViewProj; // camera-relative to the moon's shadow map (orthographic)
    vec4 heroOf[128];  // per layer (4 a vec4; GreyboxRenderer.LayerTable): its slot in the hero arrays, or -1
    vec4 dawn;         // xyz = the glow low on the dawn's horizon, w = how far it's up
    vec4 wind;         // xyz = the wind (m/s, world axes), w = gustiness 0..1
    vec4 motionOf[128]; // per layer (4 a vec4): 1 bends in the wind (foliage cards and boughs), 2 water (note 424), else 0
    mat4 viewProj1;    // the right eye's, when one pass draws both (multiview: view.glsl)
    mat4 invViewProj1;
    vec4 handPos;      // the shadowed hand lamp (MeshBuilder.ShadowLight): xyz camera-relative, w = range (0: none)
    vec4 handColour;   // rgb, a = 1 when its cube shadow is drawn
    mat4 handViewProj[6]; // its cube's faces (+X, -X, +Y, -Y, +Z, -Z), one layer each of handShadow
    vec4 indoor;       // rgb = the fill inside a room, in the moon's place (FrameLighting.IndoorFill)
    vec4 probe;        // x < 0: the sky painted magenta where it shows steeper below the horizon than this; y = 1: untextured surfaces cyan (dt holes, note 433)
} frame;

// This invocation's eye's view (the only one, drawing a single view).
mat4 eyeViewProj() {
#ifdef MULTIVIEW
    return gl_ViewIndex == 0 ? frame.viewProj : frame.viewProj1;
#else
    return frame.viewProj;
#endif
}
mat4 eyeInvViewProj() {
#ifdef MULTIVIEW
    return gl_ViewIndex == 0 ? frame.invViewProj : frame.invViewProj1;
#else
    return frame.invViewProj;
#endif
}
