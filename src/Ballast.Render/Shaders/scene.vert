#version 450
// The scene pass. Positions arrive camera-relative (floating origin at the eye), so fog distance is just the length
// of the position. Kit pieces come with a model matrix (object to camera-relative); the per-frame soup with identity.
#include "frame.glsl"

layout(push_constant) uniform Draw {
    mat4 model;
    vec4 tint;    // rgb multiplies the albedo, a = glow (scales emissive surfaces: a lamp dimmed in a Vigil)
    vec4 scar;    // x how scarred 0..1, y the pattern's seed (MeshInstance.Scar)
} draw;

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

void main() {
    vec4 p = draw.model * vec4(inPos, 1.0);
    vPos = p.xyz;
    vNormal = mat3(draw.model) * inNormal;
    vColor = inColor * draw.tint.rgb;
    vEmissive = inEmissive;
    vSurface = inSurface;
    vWear = inWear;
    vShine = inShine;
    vUv = inUv;
    vLayer = inLayer;
    vGlow = draw.tint.a;
    vLayer2 = inLayer2;
    vBlend = inBlend;
    vScar = draw.scar.xy;
    gl_Position = frame.viewProj * p;
}
