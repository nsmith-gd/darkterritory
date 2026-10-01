#version 450
// The moon's shadow map: depth only, orthographic along the moonlight, over the ground round the camera, for everything
// the scene draws (shadow.vert's twin).
#include "frame.glsl"

layout(push_constant) uniform Draw {
    mat4 model;
    vec4 tint;
    vec4 scar;
    vec4 skin;
    vec4 bite;    // MeshInstance.Bite (bite.glsl); scar.z its floor
} draw;
#include "skin.glsl"

layout(location = 0) in vec3 inPos;
layout(location = 7) in vec2 inUv;
layout(location = 8) in float inLayer;
layout(location = 0) out vec2 vUv;
layout(location = 1) flat out float vLayer;
layout(location = 2) out vec3 vObj;
layout(location = 3) flat out vec4 vBite;
layout(location = 4) flat out vec2 vBiteFloor;

void main() {
    vUv = inUv;
    vLayer = inLayer;
    vObj = inPos;
    vBite = draw.bite;
    vBiteFloor = vec2(draw.scar.z, draw.scar.y);
    mat4 model = draw.model;
#ifdef SKINNED
    model = model * skinOf(draw.skin.w);
#endif
    gl_Position = frame.moonViewProj * model * vec4(inPos, 1.0);
}
