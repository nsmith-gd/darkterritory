#version 450
// The scene pass. Positions arrive camera-relative (floating origin at the eye), so fog distance is just the length
// of the position. Kit pieces come with a model matrix (object to camera-relative); the per-frame soup with identity.
#include "frame.glsl"

layout(push_constant) uniform Draw {
    mat4 model;
    vec4 tint;    // rgb multiplies the albedo, a = glow (scales emissive surfaces: a lamp dimmed in a Vigil)
    vec4 scar;    // x how scarred 0..1, y the pattern's seed (MeshInstance.Scar)
    vec4 skin;    // xyz added to the surface's texel coordinates (MeshInstance.SurfaceOffset), w the bone palette's base
    vec4 bite;    // MeshInstance.Bite (bite.glsl): the piece eaten away from its back end; scar.z its floor
} draw;
#include "skin.glsl"

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inColor;
layout(location = 3) in float inEmissive;
layout(location = 4) in vec3 inSurface;
layout(location = 5) in float inWear;
layout(location = 6) in float inShine;
layout(location = 7) in vec2 inUv;
layout(location = 8) in float inLayer;
layout(location = 9) in float inLayer2;
layout(location = 10) in float inBlend;

layout(location = 0) out vec3 vPos;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec3 vColor;
layout(location = 3) out float vEmissive;
layout(location = 4) out vec3 vSurface;
layout(location = 5) out float vWear;
layout(location = 6) out float vShine;
layout(location = 7) out vec2 vUv;
layout(location = 8) flat out float vLayer;
layout(location = 9) flat out float vGlow;
layout(location = 10) flat out float vLayer2;
layout(location = 11) out float vBlend;
layout(location = 12) flat out vec2 vScar;
layout(location = 13) out vec3 vObj;
layout(location = 14) flat out vec4 vBite;
layout(location = 15) flat out float vBiteFloor;

void main() {
    mat4 model = draw.model;
#ifdef SKINNED
    model = model * skinOf(draw.skin.w);
#endif
    vec4 p = model * vec4(inPos, 1.0);
    // The wind in the foliage: a card or bough bends from its root (v 1, its foot) to its tip (v 0), swaying on a
    // phase of its own place so a stand doesn't move in step, harder in the gusts that roll through.
    int li = int(inLayer + 0.5);
    float motion = inLayer >= 0.0 && li < 512 ? frame.motionOf[li >> 2][li & 3] : 0.0;
    if (abs(motion - 1.0) < 0.5) {
        float flex = clamp(1.0 - fract(inUv.y), 0.0, 1.0);
        flex *= flex;
        float t = frame.fogHeight.w;
        float place = dot(inSurface, vec3(0.31, 0.11, 0.23));
        float gust = 1.0 + frame.wind.w * (0.5 + 0.5 * sin(t * 0.45 + dot(inSurface, vec3(0.013, 0.0, 0.009)) * 6.0));
        float sway = 0.6 + 0.4 * sin(t * 1.9 + place) + 0.15 * sin(t * 4.7 + place * 2.3);
        p.xyz += frame.wind.xyz * (0.012 * flex * sway * gust);
    } else if (abs(motion - 3.0) < 0.5) {
        // A drip: v 1 at its root, 1 - fract(v) a quarter of how far down it hangs (metres), so it sags in proportion,
        // the tip furthest. It stretches slowly and draws back up on a phase of its own: which drip it is in the whole
        // part of its u (the layer tiles, so the texture never sees it) and which tree in the tint the tree's drawn in,
        // the same at every vertex of it, so a drip moves as one. Two sins a vertex, nothing on the CPU, nothing more
        // drawn (the tar tree's, note 696).
        float down = clamp(1.0 - fract(inUv.y), 0.0, 1.0);
        float t = frame.fogHeight.w;
        float phase = floor(inUv.x) * 2.39996 + dot(inColor, vec3(17.0, 23.0, 29.0));
        float sag = 0.5 + 0.5 * sin(t * 0.55 + phase) * (0.75 + 0.25 * sin(t * 1.3 + phase * 1.7));
        p.y -= down * down * 1.2 * sag;
    }
    vPos = p.xyz;
    vNormal = mat3(model) * inNormal;
    vColor = inColor * draw.tint.rgb;
    vEmissive = inEmissive;
    vSurface = inSurface + draw.skin.xyz;
    vWear = inWear;
    vShine = inShine;
    vUv = inUv;
    vLayer = inLayer;
    vGlow = draw.tint.a;
    vLayer2 = inLayer2;
    vBlend = inBlend;
    vScar = draw.scar.xy;
    vObj = inPos;
    vBite = draw.bite;
    vBiteFloor = draw.scar.z;
    gl_Position = eyeViewProj() * p;
}
