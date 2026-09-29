#version 450
// Bloom, step one (pipeline post stack: "bloom, half-res, 1 threshold, 2 blur passes"): what's brighter than the
// threshold, at half resolution. Lamps, the firebox and lit windows; never the fogged sky.
layout(set = 0, binding = 0) uniform sampler2D scene;
layout(push_constant) uniform Post { vec4 a; vec4 b; } post; // a.x = threshold
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main() {
    vec3 c = texture(scene, vUv).rgb;
    float luma = dot(c, vec3(0.299, 0.587, 0.114));
    outColor = vec4(c * max(luma - post.a.x, 0.0) / max(luma, 1e-4), 1.0);
}
