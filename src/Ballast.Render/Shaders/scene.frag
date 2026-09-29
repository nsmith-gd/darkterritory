#version 450
// Late-PS2 / early-PS3 lighting (pipeline plan, "Lighting, VFX and post"; GDD s28): one legacy Blinn-Phong model for
// everything. Cold moon and a hemisphere of ambient, the headlamp's spot, the practical lights per pixel (lanterns,
// car lamps, the firebox: unshadowed), hard speculars on metal from a spec map, and exponential height fog that
// lies in the valleys. Textures carry the colour, the grime and the baked light ("texture does the lighting"); the
// shader's own grime breaks up their tiling, and stands in for a texture on anything untextured.
#include "frame.glsl"

layout(set = 0, binding = 1) uniform sampler2DArray diffuseMaps;
layout(set = 0, binding = 2) uniform sampler2DArray specMaps;

layout(location = 0) in vec3 vPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec3 vColor;
layout(location = 3) in float vEmissive;
layout(location = 4) in vec3 vSurface;
layout(location = 5) in float vWear;
layout(location = 6) in float vShine;
layout(location = 7) in vec2 vUv;
layout(location = 8) flat in float vLayer;
layout(location = 9) flat in float vGlow;
layout(location = 10) flat in float vLayer2;
layout(location = 11) in float vBlend;

layout(location = 0) out vec4 outColor;

const vec3 GROUND_BOUNCE = vec3(0.55, 0.5, 0.45);
const vec3 SKY_FILL = vec3(0.75, 0.85, 1.05);

float hash(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.71, 0.113, 0.419));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Value noise on a texel lattice: smooth between lattice points, so broad fields don't show the grid.
float noise(vec3 x) {
    vec3 i = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

// Grain, grime and staining in texel space (T39): what a hand-painted 128 px/m texture would carry. Over a real
// texture only the broad fields and streaks apply (the texture has its own grain), so no two cars wear alike.
vec3 weathered(vec3 albedo, vec3 s, float wear, bool textured) {
    float grain = textured ? 0.0 : hash(floor(s)) - 0.5;
    float field = noise(s / 64.0) - 0.5;
    float patches = smoothstep(0.58, 0.72, noise(s / 24.0 + vec3(7.1, 3.3, 1.7)));
    float streak = smoothstep(0.62, 0.85, noise(vec3(s.x / 5.0, s.y / 80.0, s.z / 5.0) + vec3(2.9)));
    vec3 c = albedo * (1.0 + wear * (grain * 0.4 + field * 0.45));
    c = mix(c, c * vec3(0.72, 0.5, 0.38), wear * patches * (textured ? 0.3 : 0.55));
    c *= 1.0 - wear * streak * 0.3;
    return c;
}

// Exponential height fog: thick in the low ground and the valleys under bridges, thinner up on the roofs, never gone.
float fogAmount(vec3 p) {
    float dist = length(p);
    float k = frame.fogHeight.y;
    float above = -frame.fogHeight.x;  // the eye's height over the fog's base
    float thickness;
    if (k <= 0.0 || abs(p.y) < 0.01)
        thickness = exp(-k * max(above, 0.0));
    else
        thickness = exp(-k * above) * (1.0 - exp(-k * p.y)) / (k * p.y);
    thickness = mix(frame.fogHeight.z, 1.0, clamp(thickness, 0.0, 1.0));
    // The curve: past the 1/e distance (1 / density) the exponent closes the fog faster, and short of it clears the
    // near ground, so the weather's visibility (spec) is where it was, and the ground at your feet reads.
    float optical = frame.fog.a * dist * thickness;
    return 1.0 - exp(-pow(optical, frame.sky2.y));
}

void main() {
    vec3 n = normalize(vNormal);
    if (!gl_FrontFacing)
        n = -n; // two-sided cards (pine boughs, grass) light from whichever side you see
    // A layer past what's loaded (a renderer not given the look's textures) draws as flat colour, not garbage.
    bool textured = vLayer >= 0.0 && vLayer < frame.params.w;
    bool ps2 = frame.params.y > 0.5;

    vec4 tex = vec4(1.0);
    vec3 specMap = vec3(vShine, 0.3, 0.0);
    if (textured) {
        tex = texture(diffuseMaps, vec3(vUv, vLayer));
        if (tex.a < 0.5)
            discard; // alpha test, never blend (pipeline: "alpha test at 0.5")
        specMap = texture(specMaps, vec3(vUv, vLayer)).rgb;
        if (vLayer2 >= 0.0 && vLayer2 < frame.params.w) {
            // The terrain blend (pipeline shader set, "terrain layer blend"): the second layer shows through by the
            // vertex weight, broken up by the first's own brightness and a blocky noise, so the edge is crunchy and
            // follows the texture (grass fills the low spots of the mud first), not a smooth crossfade.
            vec4 tex2 = texture(diffuseMaps, vec3(vUv, vLayer2));
            float breakup = dot(tex.rgb, vec3(0.3, 0.59, 0.11)) * 1.4 + (noise(floor(vSurface / 3.0)) - 0.5) * 0.5;
            float w = smoothstep(0.0, 0.18, vBlend * 1.4 - 0.2 - breakup * 0.6 + 0.3);
            tex = mix(tex, tex2, w);
            specMap = mix(specMap, texture(specMaps, vec3(vUv, vLayer2)).rgb, w);
        }
        if (ps2)
            specMap = vec3(specMap.r * 0.5, 0.2, specMap.b);
    }
    vec3 albedo = tex.rgb * vColor;
    if (vWear > 0.0)
        albedo = weathered(albedo, vSurface, vWear * (textured ? frame.params.z : 1.0), textured);

    vec3 v = normalize(-vPos);
    vec3 moonDir = normalize(frame.moon.xyz);
    vec3 light = frame.moon.w * mix(GROUND_BOUNCE, SKY_FILL, n.y * 0.5 + 0.5);
    light += frame.moonColour.rgb * frame.moonColour.a * max(dot(n, moonDir), 0.0);

    // Phong exponent from gloss: 4..128, clamped so nothing mirror-polishes (pipeline "Gloss").
    float shininess = textured ? mix(4.0, 128.0, specMap.g * specMap.g) : 40.0;
    float specStrength = specMap.r;
    vec3 spec = vec3(0.0);

    // The headlamp: the one light that reaches out into the dark.
    vec3 toLamp = frame.lampPos.xyz - vPos;
    float lampDist = length(toLamp);
    vec3 l = toLamp / max(lampDist, 1e-4);
    float cone = smoothstep(frame.lampDir.w, mix(frame.lampDir.w, 1.0, 0.35), dot(-l, normalize(frame.lampDir.xyz)));
    float falloff = clamp(1.0 - lampDist / frame.lampPos.w, 0.0, 1.0);
    float lampLit = cone * falloff * falloff;
    vec3 lampC = frame.lampColour.rgb * frame.lampColour.a;
    light += lampC * lampLit * max(dot(n, l), 0.0);
    spec += lampC * lampLit * pow(max(dot(n, normalize(l + v)), 0.0), shininess) * 0.75;
    spec += frame.moonColour.rgb * pow(max(dot(n, normalize(moonDir + v)), 0.0), shininess * 0.6) * 0.08;

    // Practical lights, per pixel and unshadowed: warm pools the crew work in.
    int count = int(frame.params.x);
    for (int i = 0; i < count; i++) {
        vec4 lp = frame.lights[i * 2];
        vec3 lc = frame.lights[i * 2 + 1].rgb;
        vec3 d = lp.xyz - vPos;
        float dist = length(d);
        if (dist >= lp.w)
            continue;
        vec3 ld = d / max(dist, 1e-4);
        float att = 1.0 - dist / lp.w;
        att *= att;
        light += lc * att * max(dot(n, ld), 0.0) * 1.6;
        spec += lc * att * pow(max(dot(n, normalize(ld + v)), 0.0), shininess) * 0.6;
    }

    vec3 colour = albedo * light + spec * specStrength * (vWear > 0.0 ? 0.55 + 0.45 * noise(vSurface / 16.0) : 1.0);
    float emissive = max(vEmissive, specMap.b);
    colour = mix(colour, albedo * 2.0 * vGlow, emissive);

    // Light sources punch through fog further than lit surfaces: the lamp is the last thing you lose.
    colour = mix(colour, frame.fog.rgb, fogAmount(vPos) * (1.0 - 0.6 * emissive * min(vGlow, 1.0)));
    outColor = vec4(colour, 1.0);
}
