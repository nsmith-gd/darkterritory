#version 450
// Effects: a flipbook cell (or, with no texture, a soft round blob), tinted, fogged like the scene. No soft
// particles (pipeline: "soft particles off"): where smoke meets the ground, it cuts, as it did in 2006.
#include "frame.glsl"

layout(set = 0, binding = 1) uniform sampler2DArray diffuseMaps;
layout(push_constant) uniform Draw {
    mat4 model;
    vec4 tint;    // a = 1 for the additive pass
} draw;

layout(location = 0) in vec3 vPos;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec4 vColour;
layout(location = 3) flat in float vLayer;
layout(location = 0) out vec4 outColor;

float fogAmount(vec3 p) {
    float optical = frame.fog.a * length(p) * mix(frame.fogHeight.z, 1.0, exp(-frame.fogHeight.y * max(-frame.fogHeight.x, 0.0)));
    return 1.0 - exp(-pow(optical, frame.sky2.y));
}

void main() {
    vec4 t;
    if (vLayer >= 0.0 && vLayer < frame.params.w)
        t = texture(diffuseMaps, vec3(vUv, vLayer));
    else {
        // A soft blob: brightest in the middle, gone at the edge of the quad.
        float r = length(vUv * 2.0 - 1.0);
        t = vec4(1.0, 1.0, 1.0, smoothstep(1.0, 0.0, r));
    }
    vec4 c = t * vColour;
    float f = fogAmount(vPos);
    if (draw.tint.a > 0.5) {
        // Additive: light fades into the fog rather than turning grey, and the lamp still reaches further.
        outColor = vec4(c.rgb * c.a * (1.0 - f * 0.7), 1.0);
    } else {
        if (c.a < 0.004)
            discard;
        outColor = vec4(mix(c.rgb, frame.fog.rgb, f), c.a);
    }
}
