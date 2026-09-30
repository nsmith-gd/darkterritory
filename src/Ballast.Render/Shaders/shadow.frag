#version 450
// Cut-out cards (pine boughs, grass, grating) cast their shapes, not their rectangles.
// What's been eaten away (bite.glsl) casts nothing.
#include "frame.glsl"
#include "bite.glsl"

layout(set = 0, binding = 1) uniform sampler2DArray diffuseMaps;
layout(location = 0) in vec2 vUv;
layout(location = 1) flat in float vLayer;
layout(location = 2) in vec3 vObj;
layout(location = 3) flat in vec4 vBite;
layout(location = 4) flat in vec2 vBiteFloor;

void main() {
    if (vBite.y > 0.0 && biteInto(vObj, vBite, vBiteFloor.x, vBiteFloor.y) > 0.0)
        discard;
    if (vLayer >= 0.0 && vLayer < frame.params.w && texture(diffuseMaps, vec3(vUv, vLayer)).a < 0.5)
        discard;
}
