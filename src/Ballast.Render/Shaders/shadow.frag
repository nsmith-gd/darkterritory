#version 450
// Cut-out cards (pine boughs, grass, grating) cast their shapes, not their rectangles.
#include "frame.glsl"

layout(set = 0, binding = 1) uniform sampler2DArray diffuseMaps;
layout(location = 0) in vec2 vUv;
layout(location = 1) flat in float vLayer;

void main() {
    if (vLayer >= 0.0 && vLayer < frame.params.w && texture(diffuseMaps, vec3(vUv, vLayer)).a < 0.5)
        discard;
}
