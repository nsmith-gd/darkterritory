#version 450
// Late-PS2 / early-PS3 lighting (pipeline plan, "Lighting, VFX and post"; GDD s28): one legacy Blinn-Phong model for
// everything. Cold moon and a hemisphere of ambient, the headlamp's spot, the practical lights per pixel (lanterns,
// car lamps, the firebox: unshadowed), hard speculars on metal from a spec map, and exponential height fog that
// lies in the valleys. Textures carry the colour, the grime and the baked light ("texture does the lighting"); the
// shader's own grime breaks up their tiling, and stands in for a texture on anything untextured.
#include "frame.glsl"

layout(set = 0, binding = 1) uniform sampler2DArray diffuseMaps;
layout(set = 0, binding = 2) uniform sampler2DArray specMaps;
layout(set = 0, binding = 4) uniform sampler2DShadow lampShadow;
layout(set = 0, binding = 5) uniform sampler2DArray normalMaps;

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
layout(location = 12) flat in vec2 vScar;

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

// Persistent scars (pipeline shader set: "damage-mask blend for persistent car scars"). A mask in the piece's own texel
// space, seeded per car, so a car scars in the same places every time it's drawn, run to run. As the amount grows the
// scorched patches spread out from their worst points, blistered to rust round the edge; plate near them is scraped
// bright in streaks along the car; and a few punctures go through (the breaches proper are geometry: DamageKit).
// Stepped at the texel, like a painted damage layer. Returns x scorch, y rust, z bare metal, w hole.
vec4 scarAt(vec3 s, vec2 scar) {
    vec3 seed = vec3(scar.y * 7.31, scar.y * 3.17, scar.y * 5.53);
    // Sampled on 2-texel blocks: the edges step like a painted mask's pixels instead of blurring.
    vec3 q = floor(s / 2.0) * (2.0 / 128.0 * 1.3) + seed;
    float m = noise(q) * 0.64 + noise(q * 3.1 + vec3(11.0)) * 0.36;
    // Value noise piles up round 0.5: at 0.8 almost nothing is scarred, at 0.6 about a fifth of the surface.
    float t = 0.8 - 0.2 * scar.x;
    float scorch = step(t, m) * smoothstep(t, t + 0.06, m);
    float rust = step(t - 0.05, m) * (1.0 - step(t, m));
    float near = step(t - 0.09, m);
    float streak = step(0.74, noise(floor(s / 2.0) * vec3(1.0 / 36.0, 1.0 / 1.4, 1.0 / 36.0) + seed));
    float bare = streak * near * (1.0 - step(t, m));
    // Punctures: one 8-texel cell in so many, near the worst, a round hole of 2-3 texels.
    vec3 cell = floor(s / 8.0);
    float pick = step(1.0 - 0.18 * scar.x, hash(cell + seed));
    float hole = pick * step(t + 0.02, m) * step(length(fract(s / 8.0) - 0.5), 0.2 + 0.1 * hash(cell.zxy));
    return vec4(scorch, rust * (1.0 - hole), bare * (1.0 - hole), hole);
}

// The surface's normal bent by its normal map (x along +u, y along +v: tools/art's convention). No tangents in the
// vertex: the frame comes from the screen-space derivatives of position and texture coordinate (Schüler's cotangent
// frame), so every kit piece, skinned model and terrain cell gets relief without a vertex format change.
vec3 perturb(vec3 n, vec3 p, vec2 uv, vec3 mapped) {
    vec3 dp1 = dFdx(p), dp2 = dFdy(p);
    vec2 duv1 = dFdx(uv), duv2 = dFdy(uv);
    vec3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
    vec3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 b = dp2perp * duv1.y + dp1perp * duv2.y;
    float len = max(dot(t, t), dot(b, b));
    if (len < 1e-20)
        return n;
    float inv = inversesqrt(len);
    return normalize(t * inv * mapped.x + b * inv * mapped.y + n * mapped.z);
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

// How much of the headlamp reaches this point past whatever's in front of it: four taps of the shadow map, so the
// edges are soft by a texel or two (pipeline: "1024^2 shadow map, 4-tap PCF").
float lampShadowAt(vec3 p, vec3 n) {
    vec4 ls = frame.lampViewProj * vec4(p + n * 0.04, 1.0);
    if (ls.w <= 0.0)
        return 1.0;
    vec3 c = ls.xyz / ls.w;
    vec2 uv = c.xy * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || c.z >= 1.0)
        return 1.0;
    float texel = 1.0 / 1024.0;
    float z = c.z - 0.0006;
    return 0.25 * (texture(lampShadow, vec3(uv + vec2(-0.6, -0.6) * texel, z)) + texture(lampShadow, vec3(uv + vec2(0.6, -0.6) * texel, z))
                 + texture(lampShadow, vec3(uv + vec2(-0.6, 0.6) * texel, z)) + texture(lampShadow, vec3(uv + vec2(0.6, 0.6) * texel, z)));
}

// 1 inside an enclosed space (a car's interior), fading to 0 over its last 15 cm, so a doorway isn't a hard line.
float indoors(vec3 p) {
    float best = 0.0;
    for (int i = 0; i < int(frame.counts.x); i++) {
        vec4 c = frame.rooms[i * 3], r = frame.rooms[i * 3 + 1], b = frame.rooms[i * 3 + 2];
        vec3 u = cross(b.xyz, r.xyz);
        vec3 d = p - c.xyz;
        vec3 l = abs(vec3(dot(d, r.xyz), dot(d, u), dot(d, b.xyz)));
        vec3 edge = clamp((vec3(c.w, r.w, b.w) - l) / 0.15, 0.0, 1.0);
        best = max(best, edge.x * edge.y * edge.z);
    }
    return best;
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
            // Anti-tiling (the 2008 terrain trick): the second layer at an off-ratio scale so the two never repeat
            // together, and both modulated by the first at a much larger scale (brightness only, over its own mean
            // from the last mip), fading in with distance, where a hillside of five-metre tiles would read as a quilt.
            vec2 uv2 = vUv * 0.63;
            vec4 tex2 = texture(diffuseMaps, vec3(uv2, vLayer2));
            vec3 lw = vec3(0.3, 0.59, 0.11);
            float far = smoothstep(8.0, 45.0, length(vPos));
            float macro = dot(texture(diffuseMaps, vec3(vUv * 0.173 + 0.37, vLayer)).rgb, lw)
                / max(dot(textureLod(diffuseMaps, vec3(0.5, 0.5, vLayer), 12.0).rgb, lw), 0.02);
            float macro2 = dot(texture(diffuseMaps, vec3(vUv * 0.117 + 0.71, vLayer2)).rgb, lw)
                / max(dot(textureLod(diffuseMaps, vec3(0.5, 0.5, vLayer2), 12.0).rgb, lw), 0.02);
            tex.rgb *= mix(1.0, clamp(macro, 0.45, 1.7), 0.3 + 0.35 * far);
            tex2.rgb *= mix(1.0, clamp(macro2, 0.45, 1.7), 0.3 + 0.35 * far);
            float breakup = dot(tex.rgb, vec3(0.3, 0.59, 0.11)) * 1.4 + (noise(floor(vSurface / 3.0)) - 0.5) * 0.5;
            float w = smoothstep(0.0, 0.18, vBlend * 1.4 - 0.2 - breakup * 0.6 + 0.3);
            tex = mix(tex, tex2, w);
            specMap = mix(specMap, texture(specMaps, vec3(vUv * 0.63, vLayer2)).rgb, w);
        }
        if (ps2)
            specMap = vec3(specMap.r * 0.5, 0.2, specMap.b);
        else {
            vec3 mapped = texture(normalMaps, vec3(vUv, vLayer)).xyz * 2.0 - 1.0;
            if (vLayer2 >= 0.0 && vLayer2 < frame.params.w) {
                vec3 mapped2 = texture(normalMaps, vec3(vUv * 0.63, vLayer2)).xyz * 2.0 - 1.0;
                float w2 = smoothstep(0.0, 0.18, vBlend * 1.4 - 0.2 - (dot(tex.rgb, vec3(0.3, 0.59, 0.11)) * 1.4) * 0.6 + 0.3);
                mapped = mix(mapped, mapped2, w2);
            }
            n = perturb(n, vPos, vUv, normalize(mapped));
        }
    }
    vec3 albedo = tex.rgb * vColor;
    if (vWear > 0.0)
        albedo = weathered(albedo, vSurface, vWear * (textured ? frame.params.z : 1.0), textured);

    vec4 scar = vec4(0.0);
    if (vScar.x > 0.0 && vEmissive < 0.5) {
        scar = scarAt(vSurface, vScar);
        albedo = mix(albedo, albedo * 0.6 * vec3(1.05, 0.7, 0.5) + vec3(0.008, 0.004, 0.002), scar.y);
        albedo = mix(albedo, albedo * vec3(0.3, 0.27, 0.25), scar.x);
        albedo = mix(albedo, max(albedo, vec3(0.075, 0.075, 0.08)), scar.z);
        albedo = mix(albedo, vec3(0.003), scar.w);
    }

    // Rain: darker surfaces, and a sheen on everything that faces the sky (ballast, roofs, puddles in the mud).
    float inside = frame.counts.x > 0.0 ? indoors(vPos) : 0.0;
    float night = 1.0 - inside;
    float wet = frame.sky2.w * (vWear > 0.0 || textured ? 1.0 : 0.0) * night;
    albedo *= 1.0 - 0.3 * wet;

    vec3 v = normalize(-vPos);
    vec3 moonDir = normalize(frame.moon.xyz);
    // Indoors the fill is low and warm (lamplight off the boards), and the moon doesn't get in.
    vec3 light = frame.moon.w * mix(mix(GROUND_BOUNCE, SKY_FILL, n.y * 0.5 + 0.5), vec3(0.55, 0.42, 0.3), inside);
    light += frame.moonColour.rgb * frame.moonColour.a * max(dot(n, moonDir), 0.0) * night;

    // Phong exponent from gloss: 4..128, clamped so nothing mirror-polishes (pipeline "Gloss").
    float shininess = textured ? mix(4.0, 128.0, specMap.g * specMap.g) : 40.0;
    float specStrength = specMap.r;
    float up = smoothstep(0.5, 0.95, n.y) * wet;
    specStrength = max(specStrength, 0.16 * up);
    shininess = mix(shininess, 70.0, up);
    // Torn edges catch the light; scorch doesn't; a hole throws nothing back.
    specStrength = mix(mix(specStrength, specStrength * 0.3, scar.x), 0.4, scar.z) * (1.0 - scar.w);
    shininess = mix(shininess, 56.0, scar.z);
    vec3 spec = vec3(0.0);

    // The headlamp: the one light that reaches out into the dark.
    vec3 toLamp = frame.lampPos.xyz - vPos;
    float lampDist = length(toLamp);
    vec3 l = toLamp / max(lampDist, 1e-4);
    float cone = smoothstep(frame.lampDir.w, mix(frame.lampDir.w, 1.0, 0.35), dot(-l, normalize(frame.lampDir.xyz)));
    float falloff = clamp(1.0 - lampDist / frame.lampPos.w, 0.0, 1.0);
    float lampLit = cone * falloff * falloff * night;
    if (lampLit > 0.0)
        lampLit *= lampShadowAt(vPos, n);
    vec3 lampC = frame.lampColour.rgb * frame.lampColour.a;
    light += lampC * lampLit * max(dot(n, l), 0.0);
    spec += lampC * lampLit * pow(max(dot(n, normalize(l + v)), 0.0), shininess) * 0.75;
    spec += frame.moonColour.rgb * pow(max(dot(n, normalize(moonDir + v)), 0.0), shininess * 0.6) * 0.08 * night;

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
