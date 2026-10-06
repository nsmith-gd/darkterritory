#version 450
#include "view.glsl"
// Bloom, steps two and three: a 9-tap Gaussian along one axis (a.xy = one texel along it), in five linear taps.
layout(set = 0, binding = 0) uniform EYE_SAMPLER source;
layout(push_constant) uniform Post { vec4 a; vec4 b; } post;
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main() {
    vec2 step = post.a.xy;
    vec3 c = texture(source, EYE_UV(vUv)).rgb * 0.227027;
    c += (texture(source, EYE_UV(vUv + step * 1.3846154)).rgb + texture(source, EYE_UV(vUv - step * 1.3846154)).rgb) * 0.3162162;
    c += (texture(source, EYE_UV(vUv + step * 3.2307692)).rgb + texture(source, EYE_UV(vUv - step * 3.2307692)).rgb) * 0.0702703;
    outColor = vec4(c, 1.0);
}
