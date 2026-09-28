#version 450
// Late-PS2 lighting model (art sheet: "stylized lighting carries the mood"):
// cold moon + warm practical lamp + exponential fog, then ordered dither and a reduced
// colour depth so gradients band slightly, the way a 2006 framebuffer did.

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

layout(location = 0) out vec4 outColor;

const vec3 MOON_COLOUR = vec3(0.55, 0.62, 0.78);
const vec3 LAMP_COLOUR = vec3(1.0, 0.72, 0.38);
const float COLOUR_LEVELS = 48.0;

float bayer4(ivec2 p) {
    const float m[16] = float[16](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
    return m[(p.x & 3) + (p.y & 3) * 4] / 16.0 - 0.5;
}

void main() {
    vec3 n = normalize(vNormal);
    vec3 albedo = vColor;

    vec3 light = MOON_COLOUR * max(dot(n, normalize(frame.moon.xyz)), 0.0) * 0.6 + vec3(frame.moon.w);

    vec3 toLamp = frame.lampPos.xyz - vPos;
    float lampDist = length(toLamp);
    vec3 l = toLamp / max(lampDist, 1e-4);
    float cone = smoothstep(frame.lampDir.w, mix(frame.lampDir.w, 1.0, 0.35), dot(-l, normalize(frame.lampDir.xyz)));
    float falloff = clamp(1.0 - lampDist / frame.lampPos.w, 0.0, 1.0);
    light += LAMP_COLOUR * cone * falloff * falloff * max(dot(n, l), 0.0) * 3.0;

    vec3 colour = mix(albedo * light, albedo * 2.0, vEmissive);

    float dist = length(vPos);
    float fogAmount = 1.0 - exp(-frame.fog.a * dist);
    // Light sources punch through fog further than lit surfaces: the lamp is the last thing you lose.
    colour = mix(colour, frame.fog.rgb, fogAmount * (1.0 - 0.6 * vEmissive));

    colour = pow(clamp(colour, 0.0, 1.0), vec3(1.0 / 2.2));
    colour += bayer4(ivec2(gl_FragCoord.xy)) / COLOUR_LEVELS;
    colour = floor(colour * COLOUR_LEVELS + 0.5) / COLOUR_LEVELS;
    outColor = vec4(colour, 1.0);
}
