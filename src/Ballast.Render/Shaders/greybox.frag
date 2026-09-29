#version 450
// Late-PS2 lighting model (art sheet: "stylized lighting carries the mood"):
// cold moon + warm practical lamp + exponential fog, then ordered dither and a reduced
// colour depth so gradients band slightly, the way a 2006 framebuffer did.
// Surfaces stand in for textures (T39, GDD s27): blocky grain per texel, broad soot and rust fields, oil
// streaks running down, and a harsh specular on metal. "If it looks like a pristine Substance demo, it is off target."

layout(push_constant) uniform Frame {
    mat4 viewProj;
    vec4 fog;
    vec4 moon;
    vec4 lampPos;
    vec4 lampDir;
} frame;

layout(location = 0) in vec3 vPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec3 vColor;
layout(location = 3) in float vEmissive;
layout(location = 4) in vec3 vSurface;
layout(location = 5) in float vWear;
layout(location = 6) in float vShine;

layout(location = 0) out vec4 outColor;

const vec3 MOON_COLOUR = vec3(0.55, 0.62, 0.78);
const vec3 LAMP_COLOUR = vec3(1.0, 0.72, 0.38);
const float COLOUR_LEVELS = 48.0;

float bayer4(ivec2 p) {
    const float m[16] = float[16](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
    return m[(p.x & 3) + (p.y & 3) * 4] / 16.0 - 0.5;
}

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

// Grain, grime and staining in texel space: what a hand-painted 128 px/m texture would carry.
vec3 weathered(vec3 albedo, vec3 s, float wear) {
    // Blocky per-texel grain: the low-res texture look, and what crawls a little as the camera moves.
    float grain = hash(floor(s)) - 0.5;
    // Broad fields of soot and wear (about half a metre across at 128 px/m).
    float field = noise(s / 64.0) - 0.5;
    // Rust and chipped patches: warm, darker blotches with hard-ish edges.
    float patches = smoothstep(0.58, 0.72, noise(s / 24.0 + vec3(7.1, 3.3, 1.7)));
    // Oil and soot streaks, stretched down the surface.
    float streak = smoothstep(0.62, 0.85, noise(vec3(s.x / 5.0, s.y / 80.0, s.z / 5.0) + vec3(2.9)));
    vec3 c = albedo * (1.0 + wear * (grain * 0.4 + field * 0.45));
    c = mix(c, c * vec3(0.72, 0.5, 0.38), wear * patches * 0.55);
    c *= 1.0 - wear * streak * 0.3;
    return c;
}

void main() {
    vec3 n = normalize(vNormal);
    vec3 albedo = vWear > 0.0 ? weathered(vColor, vSurface, vWear) : vColor;

    vec3 light = MOON_COLOUR * max(dot(n, normalize(frame.moon.xyz)), 0.0) * 0.6 + vec3(frame.moon.w);

    vec3 toLamp = frame.lampPos.xyz - vPos;
    float lampDist = length(toLamp);
    vec3 l = toLamp / max(lampDist, 1e-4);
    float cone = smoothstep(frame.lampDir.w, mix(frame.lampDir.w, 1.0, 0.35), dot(-l, normalize(frame.lampDir.xyz)));
    float falloff = clamp(1.0 - lampDist / frame.lampPos.w, 0.0, 1.0);
    light += LAMP_COLOUR * cone * falloff * falloff * max(dot(n, l), 0.0) * 3.0;

    vec3 colour = mix(albedo * light, albedo * 2.0, vEmissive);

    // Harsh speculars on metal (GDD s25 "harsh speculars on metal"): the headlamp's, and a colder glint of the moon.
    if (vShine > 0.0) {
        vec3 v = normalize(-vPos);
        float lampSpec = pow(max(dot(n, normalize(l + v)), 0.0), 40.0) * cone * falloff * falloff;
        float moonSpec = pow(max(dot(n, normalize(normalize(frame.moon.xyz) + v)), 0.0), 24.0);
        // Grime dulls it in patches, the way oil and soot do.
        float dull = vWear > 0.0 ? 0.55 + 0.45 * noise(vSurface / 16.0) : 1.0;
        colour += vShine * dull * (LAMP_COLOUR * lampSpec * 2.2 + MOON_COLOUR * moonSpec * 0.25);
    }

    float dist = length(vPos);
    float fogAmount = 1.0 - exp(-frame.fog.a * dist);
    // Light sources punch through fog further than lit surfaces: the lamp is the last thing you lose.
    colour = mix(colour, frame.fog.rgb, fogAmount * (1.0 - 0.6 * vEmissive));

    colour = pow(clamp(colour, 0.0, 1.0), vec3(1.0 / 2.2));
    colour += bayer4(ivec2(gl_FragCoord.xy)) / COLOUR_LEVELS;
    colour = floor(colour * COLOUR_LEVELS + 0.5) / COLOUR_LEVELS;
    outColor = vec4(colour, 1.0);
}
