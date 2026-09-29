#version 450
// The effects pass (pipeline "VFX": billboards and trails from flipbook atlases, alpha for smoke and dust, additive
// for fire, sparks and glows). Camera-relative positions, like the scene.
#include "frame.glsl"

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUv;
layout(location = 2) in vec4 inColour;
layout(location = 3) in float inLayer;

layout(location = 0) out vec3 vPos;
layout(location = 1) out vec2 vUv;
layout(location = 2) out vec4 vColour;
layout(location = 3) flat out float vLayer;

void main() {
    vPos = inPos;
    vUv = inUv;
    vColour = inColour;
    vLayer = inLayer;
    gl_Position = frame.viewProj * vec4(inPos, 1.0);
}
