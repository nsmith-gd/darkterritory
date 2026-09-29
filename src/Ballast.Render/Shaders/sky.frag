#version 450
// The night sky behind everything (GDD s28 "cool ambient night"): a gradient from the fog at the horizon to near
// black overhead, a moon smothered in haze, slow cloud banks, and a 360-degree band of far silhouettes (mountains,
// a fortified town on its crag, a viaduct) sinking into the fog: the "world that already lost", always out of reach.
#include "frame.glsl"

layout(set = 0, binding = 3) uniform sampler2D backdrop;
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

const float PI = 3.14159265;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void main() {
    vec4 far = frame.invViewProj * vec4(vUv * 2.0 - 1.0, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w);
    // The horizon's haze is a little brighter than the fog: fogged geometry converges to the fog colour, and the sky
    // behind it has to be paler still, or depth reads backwards (far things darker than near).
    vec3 horizon = frame.fog.rgb * frame.sky2.z;
    float up = clamp(dir.y, 0.0, 1.0);
    vec3 colour = mix(horizon, frame.sky.rgb, smoothstep(0.0, 0.55, up));

    // Cloud banks: broad and slow, a little lighter where the moon is behind them.
    vec2 cp = dir.xz / max(dir.y + 0.12, 0.05) * 1.2 + vec2(frame.fogHeight.w * 0.004, 0.0);
    float cloud = smoothstep(0.35, 0.8, noise(cp) * 0.65 + noise(cp * 2.7) * 0.35);
    vec3 moonDir = normalize(frame.moon.xyz);
    float toMoon = max(dot(dir, moonDir), 0.0);
    colour = mix(colour, horizon * 1.15, cloud * 0.5 * smoothstep(0.0, 0.3, up));
    // The moon: a hazy disc and a wide glow, dimmed by the cloud.
    float disc = smoothstep(0.9993, 0.9997, toMoon);
    float glow = pow(toMoon, 64.0) * 0.5 + pow(toMoon, 8.0) * 0.15;
    colour += frame.moonColour.rgb * (disc * 1.4 + glow) * (1.0 - cloud * 0.7);

    // The backdrop band: 360 degrees across, sky2.x tall, the horizon 85 % of the way down.
    float azimuth = atan(dir.x, -dir.z) / (2.0 * PI) + 0.5;
    float elevation = asin(clamp(dir.y, -1.0, 1.0));
    float v = 0.85 - elevation / frame.sky2.x;
    if (v >= 0.0 && v <= 1.0) {
        vec4 band = texture(backdrop, vec2(azimuth, v));
        // Aerial perspective, anchored to the fog: the band's darkest layer (the forest line) is exactly the fog colour,
        // continuous with fogged geometry in front of it, and its paler, farther ridges lift toward the haze. The band's
        // own values only say which layer is which. Lit windows cut through.
        float l = dot(band.rgb, vec3(0.299, 0.587, 0.114));
        float lit = smoothstep(0.35, 0.6, max(band.r, max(band.g, band.b)));
        vec3 layered = frame.fog.rgb * mix(1.0, frame.sky2.z * 0.97, clamp(l / 0.06, 0.0, 1.0));
        vec3 silhouette = mix(layered, band.rgb * 2.5, lit);
        colour = mix(colour, silhouette, band.a * (1.0 - frame.sky.a * (1.0 - lit)));
    }
    // Below the horizon: the ground's haze.
    if (dir.y < 0.0)
        colour = mix(colour, horizon * 0.85, smoothstep(0.0, -0.08, dir.y));
    outColor = vec4(colour, 1.0);
}
