#version 450
// Screen-space ambient occlusion, at half resolution (the benchmarks' contact darkening: the dark where a crate meets the
// floor, down a coupling's throat, under a roof's eave; Crysis's, then BioShock 2's and Dead Space's). From the scene's
// depth alone: each pixel's view-space position and normal are rebuilt from it, and a dozen points in the hemisphere over
// the normal are tested against the depth they land on. The composite blurs it and darkens the scene by it.
layout(set = 0, binding = 0) uniform sampler2D depthTex;
// a: the projection's M33, M43, M11, M22 (System.Numerics, Vulkan's clip space: M22 flipped).
// b: x = radius (m), y = intensity, z, w = one full-resolution texel. c: x = fade-out distance (m).
layout(push_constant) uniform Post { vec4 a; vec4 b; vec4 c; } post;
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

vec3 viewAt(vec2 uv, float d) {
    // Right-handed, depth 0..1: z_ndc = -M33 - M43 / z, so z = -M43 / (d + M33) (negative in front).
    float z = -post.a.y / (d + post.a.x);
    vec2 ndc = uv * 2.0 - 1.0;
    return vec3(ndc.x * -z / post.a.z, ndc.y * -z / post.a.w, z);
}

vec3 viewAt(vec2 uv) { return viewAt(uv, texture(depthTex, uv).r); }

vec2 project(vec3 p) {
    return vec2(p.x * post.a.z / -p.z, p.y * post.a.w / -p.z) * 0.5 + 0.5;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void main() {
    float d = texture(depthTex, vUv).r;
    if (d >= 0.99999) {
        outColor = vec4(1.0);
        return;
    }
    vec3 p = viewAt(vUv, d);
    float dist = -p.z;
    if (dist > post.c.x) {
        outColor = vec4(1.0);
        return;
    }
    // The normal from the nearer of each pair of neighbours (so a silhouette edge doesn't bend it).
    vec2 t = post.b.zw * 2.0;
    vec3 px0 = viewAt(vUv - vec2(t.x, 0)), px1 = viewAt(vUv + vec2(t.x, 0));
    vec3 py0 = viewAt(vUv - vec2(0, t.y)), py1 = viewAt(vUv + vec2(0, t.y));
    vec3 dx = abs(px1.z - p.z) < abs(p.z - px0.z) ? px1 - p : p - px0;
    vec3 dy = abs(py1.z - p.z) < abs(p.z - py0.z) ? py1 - p : p - py0;
    vec3 n = normalize(cross(dx, dy));
    if (dot(n, -p) < 0.0)
        n = -n;
    // A per-pixel turn of the sample pattern (the blur in the composite smooths it out).
    float spin = hash(floor(gl_FragCoord.xy)) * 6.2831853;
    vec3 helper = abs(n.y) < 0.9 ? vec3(0, 1, 0) : vec3(1, 0, 0);
    vec3 tx = normalize(cross(helper, n));
    vec3 ty = cross(n, tx);
    float r = post.b.x * clamp(dist / 6.0, 0.35, 1.0);
    float occluded = 0.0;
    const int N = 12;
    for (int i = 0; i < N; i++) {
        float fi = float(i) + 0.5;
        float a = fi * 2.3999632 + spin;                 // the golden angle round the normal
        float h = sqrt(fi / float(N));                   // spread over the hemisphere
        vec3 dir = normalize(tx * cos(a) * h + ty * sin(a) * h + n * sqrt(max(0.05, 1.0 - h * h)));
        float k = mix(0.15, 1.0, fract(fi * 0.618034 + spin * 0.1));
        vec3 s = p + dir * r * k;
        vec2 uv = project(s);
        if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
            continue;
        float sceneZ = viewAt(uv).z;
        // Occluded when the surface there is in front of the sample, and near enough to count (no halo off a far wall).
        float inFront = step(s.z + 0.02 * r, sceneZ);
        occluded += inFront * smoothstep(0.0, 1.0, r / max(abs(p.z - sceneZ), 1e-3));
    }
    float ao = 1.0 - occluded / float(N) * post.b.y;
    ao = mix(ao, 1.0, smoothstep(post.c.x * 0.6, post.c.x, dist));
    outColor = vec4(vec3(clamp(ao, 0.0, 1.0)), 1.0);
}
