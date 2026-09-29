#version 450
// The end of the post stack (pipeline order: fog is in the scene, then bloom, the colour grade by 16^3 LUT, vignette,
// film grain), then what makes it a 2006 framebuffer: ordered dither and reduced colour depth, so gradients band.
layout(set = 0, binding = 0) uniform sampler2D scene;
layout(set = 0, binding = 1) uniform sampler2D bloom;
layout(set = 0, binding = 2) uniform sampler3D grade;
// a: x = bloom strength, y = vignette, z = grain, w = colour levels. b: x = frame seed, y = aspect, z = PS2 look.
layout(push_constant) uniform Post { vec4 a; vec4 b; } post;
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

float bayer4(ivec2 p) {
    const float m[16] = float[16](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
    return m[(p.x & 3) + (p.y & 3) * 4] / 16.0 - 0.5;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

// A soft shoulder instead of a hard clip, so a lamp's core rolls off rather than flattening to a white block.
vec3 shoulder(vec3 c) {
    vec3 over = max(c - 0.8, 0.0);
    return min(c, 0.8) + 0.2 * (1.0 - exp(-over / 0.2));
}

void main() {
    bool ps2 = post.b.z > 0.5;
    vec3 c = texture(scene, vUv).rgb;
    if (!ps2)
        c += texture(bloom, vUv).rgb * post.a.x;
    c = pow(shoulder(max(c, 0.0)), vec3(1.0 / 2.2));
    c = texture(grade, c * (15.0 / 16.0) + 0.5 / 16.0).rgb;
    vec2 centred = (vUv - 0.5) * vec2(post.b.y, 1.0);
    c *= 1.0 - post.a.y * smoothstep(0.35, 1.05, length(centred) * 1.2);
    float luma = dot(c, vec3(0.299, 0.587, 0.114));
    c += (hash(gl_FragCoord.xy + post.b.x * 17.13) - 0.5) * post.a.z * (1.0 - luma * 0.6);
    float levels = ps2 ? 24.0 : post.a.w;
    c += bayer4(ivec2(gl_FragCoord.xy)) / levels;
    c = floor(clamp(c, 0.0, 1.0) * levels + 0.5) / levels;
    outColor = vec4(c, 1.0);
}
