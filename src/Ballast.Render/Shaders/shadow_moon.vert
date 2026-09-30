#version 450
// The moon's shadow map: depth only, orthographic along the moonlight, over the ground round the camera, for everything
// the scene draws (shadow.vert's twin).
#include "frame.glsl"

layout(push_constant) uniform Draw {
    mat4 model;
    vec4 tint;
} draw;

layout(location = 0) in vec3 inPos;
layout(location = 7) in vec2 inUv;
layout(location = 8) in float inLayer;
layout(location = 0) out vec2 vUv;
layout(location = 1) flat out float vLayer;

void main() {
    vUv = inUv;
    vLayer = inLayer;
    gl_Position = frame.moonViewProj * draw.model * vec4(inPos, 1.0);
}
