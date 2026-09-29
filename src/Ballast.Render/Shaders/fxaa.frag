#version 450
// FXAA (Lottes, 2009: the PS3/360-era's post anti-aliasing): the composite's edges, found by luma (in alpha) and
// blended along them, so the kits' long thin edges (rails, wires, roof lines) stop stair-stepping. a.xy = one texel,
// a.z = on (the PS2 comparison mode keeps its jaggies).
layout(set = 0, binding = 0) uniform sampler2D frame;
layout(push_constant) uniform Post { vec4 a; vec4 b; } post;
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main() {
    vec2 px = post.a.xy;
    vec4 centre = texture(frame, vUv);
    if (post.a.z < 0.5) {
        outColor = vec4(centre.rgb, 1.0);
        return;
    }
    float nw = texture(frame, vUv + vec2(-1, -1) * px).a;
    float ne = texture(frame, vUv + vec2(1, -1) * px).a;
    float sw = texture(frame, vUv + vec2(-1, 1) * px).a;
    float se = texture(frame, vUv + vec2(1, 1) * px).a;
    float m = centre.a;
    float lo = min(m, min(min(nw, ne), min(sw, se)));
    float hi = max(m, max(max(nw, ne), max(sw, se)));
    // Too little contrast to be an edge: leave it (the grain, the fog).
    if (hi - lo < max(0.0312, hi * 0.125)) {
        outColor = vec4(centre.rgb, 1.0);
        return;
    }
    vec2 dir = vec2(-((nw + ne) - (sw + se)), (nw + sw) - (ne + se));
    float reduce = max((nw + ne + sw + se) * 0.25 * (1.0 / 8.0), 1.0 / 128.0);
    float scale = 1.0 / (min(abs(dir.x), abs(dir.y)) + reduce);
    dir = clamp(dir * scale, vec2(-8.0), vec2(8.0)) * px;
    vec3 a = 0.5 * (texture(frame, vUv + dir * (1.0 / 3.0 - 0.5)).rgb + texture(frame, vUv + dir * (2.0 / 3.0 - 0.5)).rgb);
    vec3 b = a * 0.5 + 0.25 * (texture(frame, vUv + dir * -0.5).rgb + texture(frame, vUv + dir * 0.5).rgb);
    float lb = dot(b, vec3(0.299, 0.587, 0.114));
    outColor = vec4(lb < lo || lb > hi ? a : b, 1.0);
}
