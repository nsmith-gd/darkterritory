#version 450
#include "view.glsl"
// The end of the post stack: bloom (two scales), exposure and a filmic tonemap, the colour grade by 16^3 LUT, vignette,
// a touch of lens fringing, film grain. The benchmarks are 2008-2012's (BioShock 2, Dead Space, RE Revelations): HDR
// that rolls off like film instead of clipping, and a clean 8-bit frame (a triangular dither of one step, no bands).
// The PS2 comparison mode (b.z) keeps the old 2006 framebuffer: a hard shoulder, ordered dither, reduced colour depth.
layout(set = 0, binding = 0) uniform EYE_SAMPLER scene;
layout(set = 0, binding = 1) uniform EYE_SAMPLER bloom;
layout(set = 0, binding = 2) uniform sampler3D grade;
layout(set = 0, binding = 3) uniform EYE_SAMPLER bloomWide;
// The half-resolution ambient occlusion (ssao.frag), blurred here.
layout(set = 0, binding = 4) uniform EYE_SAMPLER occlusion;
// a: x = bloom strength, y = vignette, z = grain, w = colour levels (PS2 mode).
// b: x = frame seed, y = aspect, z = PS2 look, w = exposure. c: x = wide bloom share, y = lens fringe, z = occlusion
// strength, w = the occlusion target's texel (u; v by aspect).
layout(push_constant) uniform Post { vec4 a; vec4 b; vec4 c; } post;
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

float bayer4(ivec2 p) {
    const float m[16] = float[16](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
    return m[(p.x & 3) + (p.y & 3) * 4] / 16.0 - 0.5;
}

// (Without sin: exact at a screen's pixel coordinates and a long night's clock on any GPU; note 140.)
float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// The PS2 mode's soft shoulder instead of a hard clip.
vec3 shoulder(vec3 c) {
    vec3 over = max(c - 0.8, 0.0);
    return min(c, 0.8) + 0.2 * (1.0 - exp(-over / 0.2));
}

// Filmic: Narkowicz's fit of the ACES curve. A toe that keeps the blacks black and a long shoulder, so a lamp's core
// burns to warm white while what it lights keeps its colour.
vec3 filmic(vec3 x) {
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

vec3 toSrgb(vec3 c) {
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

void main() {
    bool ps2 = post.b.z > 0.5;
    vec2 centred = (vUv - 0.5) * vec2(post.b.y, 1.0);
    float edge = dot(centred, centred);
    vec3 c;
    if (ps2) {
        c = texture(scene, EYE_UV(vUv)).rgb;
        c = pow(shoulder(max(c, 0.0)), vec3(1.0 / 2.2));
    } else {
        // Lens fringing: red and blue a hair apart toward the corners, as the era's cameras-in-games had it.
        vec2 shift = (vUv - 0.5) * post.c.y * edge;
        c = vec3(texture(scene, EYE_UV(vUv + shift)).r, texture(scene, EYE_UV(vUv)).g, texture(scene, EYE_UV(vUv - shift)).b);
        // The occlusion, blurred over a few of its texels (four bilinear taps: a 4x4 box), darkening the scene, but not a
        // light's core: a lamp burning in a corner is still a lamp.
        vec2 ot = vec2(post.c.w, post.c.w * post.b.y);
        float ao = 0.25 * (texture(occlusion, EYE_UV(vUv + ot * vec2(-0.75, -0.75))).r + texture(occlusion, EYE_UV(vUv + ot * vec2(0.75, -0.75))).r
                         + texture(occlusion, EYE_UV(vUv + ot * vec2(-0.75, 0.75))).r + texture(occlusion, EYE_UV(vUv + ot * vec2(0.75, 0.75))).r);
        float lit = clamp(dot(c, vec3(0.299, 0.587, 0.114)) - 0.8, 0.0, 1.0);
        c *= mix(1.0, ao, post.c.z * (1.0 - lit));
        c += (texture(bloom, EYE_UV(vUv)).rgb * (1.0 - post.c.x) + texture(bloomWide, EYE_UV(vUv)).rgb * post.c.x) * post.a.x;
        c = toSrgb(filmic(max(c, 0.0) * post.b.w));
    }
    c = texture(grade, c * (15.0 / 16.0) + 0.5 / 16.0).rgb;
    c *= 1.0 - post.a.y * smoothstep(0.35, 1.05, length(centred) * 1.2);
    float luma = dot(c, vec3(0.299, 0.587, 0.114));
    // Grain: finer and fainter in the shadows' favour, so the dark has texture without the frame crawling.
    float g = hash(gl_FragCoord.xy + post.b.x * 17.13) + hash(gl_FragCoord.xy * 1.37 + post.b.x * 3.1) - 1.0;
    c += g * post.a.z * (1.0 - luma * 0.7);
    if (ps2) {
        c += bayer4(ivec2(gl_FragCoord.xy)) / 24.0;
        c = floor(clamp(c, 0.0, 1.0) * 24.0 + 0.5) / 24.0;
    } else {
        // One step of triangular dither: no banding in the fog's long gradients, and nothing you can see.
        c += (hash(gl_FragCoord.xy * 0.73 + 5.1) + hash(gl_FragCoord.xy * 1.91 + 2.3) - 1.0) / 255.0;
    }
    c = clamp(c, 0.0, 1.0);
    outColor = vec4(c, dot(c, vec3(0.299, 0.587, 0.114))); // luma in alpha, for FXAA
}
