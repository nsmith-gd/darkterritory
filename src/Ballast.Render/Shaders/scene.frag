#version 450
// Late-PS2 / early-PS3 lighting (pipeline plan, "Lighting, VFX and post"; GDD s28): one legacy Blinn-Phong model for
// everything. Cold moon and a hemisphere of ambient, the headlamp's spot, the practical lights per pixel (lanterns,
// car lamps, the firebox: unshadowed), hard speculars on metal from a spec map, and exponential height fog that
// lies in the valleys. Textures carry the colour, the grime and the baked light ("texture does the lighting"); the
// shader's own grime breaks up their tiling, and stands in for a texture on anything untextured.
#include "frame.glsl"
#include "bite.glsl"

layout(set = 0, binding = 1) uniform sampler2DArray diffuseMaps;
layout(set = 0, binding = 2) uniform sampler2DArray specMaps;
layout(set = 0, binding = 4) uniform sampler2DShadow lampShadow;
layout(set = 0, binding = 5) uniform sampler2DArray normalMaps;
layout(set = 0, binding = 6) uniform sampler2DShadow moonShadow;
// The hero layers (RenderAssets.HeroSize): the characters' and creatures' atlases at full size.
layout(set = 0, binding = 7) uniform sampler2DArray heroDiffuse;
layout(set = 0, binding = 8) uniform sampler2DArray heroSpec;
layout(set = 0, binding = 9) uniform sampler2DArray heroNormal;
// ...and the big ones (RenderAssets.BigHeroSize): a slot from 64 up (GreyboxRenderer.BigHero) is in these.
layout(set = 0, binding = 11) uniform sampler2DArray bigDiffuse;
layout(set = 0, binding = 12) uniform sampler2DArray bigSpec;
layout(set = 0, binding = 13) uniform sampler2DArray bigNormal;
layout(set = 0, binding = 14) uniform sampler2DArrayShadow handShadow;

float heroSlot(float layer) {
    int l = int(layer + 0.5);
    return l >= 0 && l < 512 ? frame.heroOf[l >> 2][l & 3] : -1.0;
}

// How a layer moves (GreyboxRenderer.Motion): 1 the foliage, 2 water.
float layerMotion(float layer) {
    int l = int(layer + 0.5);
    return l >= 0 && l < 512 ? frame.motionOf[l >> 2][l & 3] : 0.0;
}

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
layout(location = 13) in vec3 vObj;
layout(location = 14) flat in vec4 vBite;
layout(location = 15) flat in float vBiteFloor;

layout(location = 0) out vec4 outColor;

const vec3 GROUND_BOUNCE = vec3(0.55, 0.5, 0.45);
const vec3 SKY_FILL = vec3(0.75, 0.85, 1.05);

// The night as a surface sees it in reflection: no cubemap, the sky's own gradient (the haze at the horizon, the zenith
// over it, the dark fogged ground under it). What the benchmarks' wet metal and glass shone with.
vec3 envAt(vec3 r) {
    vec3 horizon = frame.fog.rgb * frame.sky2.z;
    vec3 c = mix(horizon, frame.sky.rgb, smoothstep(0.02, 0.6, r.y));
    c = mix(c, frame.fog.rgb * 0.45, smoothstep(0.0, -0.25, r.y));
    // (The moon itself is left to the direct specular: in a reflection, off a bumpy normal map, it scatters into sparkle.)
    // What a reflection sees along the horizon and under it is mostly the world, not the sky: the other cars, the
    // cab's own walls, trees. Without a probe to say so, it's darker there.
    return c * mix(0.3, 1.0, smoothstep(-0.05, 0.45, r.y));
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

// Texture bombing for the terrain (after Quilez, "texture repetition", technique 3): a slow noise picks one of eight
// offsets of the same tile, and neighbouring offsets cross-fade through its fractional part, weighted by where the
// two samples differ so the seam follows the texture's own features. Offsets only translate, so the normal map's
// tangent frame still holds. The same Tiling drives a layer's diffuse, spec and normal maps.
struct Tiling { vec2 uv, a, b, dx, dy; float f; };

Tiling tiling(vec2 uv) {
    float l = noise(vec3(uv * 0.29, 0.5)) * 8.0 + noise(vec3(uv * 0.061, 3.5)) * 6.0;
    float i = floor(l);
    return Tiling(uv, sin(vec2(3.0, 7.0) * i), sin(vec2(3.0, 7.0) * (i + 1.0)), dFdx(uv), dFdy(uv), fract(l));
}

vec4 bombed(sampler2DArray m, inout Tiling t, float layer, bool weigh) {
    vec4 a = textureGrad(m, vec3(t.uv + t.a, layer), t.dx, t.dy);
    vec4 b = textureGrad(m, vec3(t.uv + t.b, layer), t.dx, t.dy);
    if (weigh)
        t.f = smoothstep(0.2, 0.8, t.f - 0.1 * dot(a.rgb - b.rgb, vec3(1.0)));
    return mix(a, b, t.f);
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

// The water (ARCHITECTURE s8 note 424). A water layer's vertices say where it lies and how it moves (WorldArt.WaterSheet):
// its texture coordinates are the world's own in metres (wrapped at WRAP, as the grime's are), its surface coordinates its
// current (xy, m/s along the world's x and z) and how open it lies to the wind (z: 0 a marsh's pool, 1 the open sea), its
// blend how near the land (1 at the waterline). Its relief is all in the normal: the ripple map twice, turned to the
// night's wind and running downwind and down the current, over a long swell.
const float WRAP = 4096.0;

// A tiling turned to face dir, about tile metres a repeat, seamless across the wrap: its axes are whole-number lattice
// directions, so the wrap is a whole number of repeats along both of the world's axes. Returns its coordinates; tile is
// left at the repeat it settled on, along and across at its axes (along dir, and a quarter turn from it).
vec2 turned(vec2 at, vec2 dir, inout float tile, out vec2 along, out vec2 across) {
    vec2 n = floor(dir * (WRAP / tile) + 0.5);
    float len = max(length(n), 1.0);
    tile = WRAP / len;
    along = n / len;
    across = vec2(-along.y, along.x);
    return vec2(dot(at, vec2(-n.y, n.x)), dot(at, n)) / WRAP;
}

vec2 rotated(vec2 d, float a) {
    float c = cos(a), s = sin(a);
    return vec2(c * d.x - s * d.y, s * d.x + c * d.y);
}

// The water's normal at a point, from its ripples and its swell; crest is how near a swell's crest it is (0..1).
vec3 waterNormal(vec2 at, vec2 current, float open, float t, out float crest) {
    vec2 wind = frame.wind.xz;
    float speed = length(wind);
    vec2 dir = speed > 0.05 ? wind / speed : vec2(0.8, 0.6);
    // How rough: a still night leaves it near glass, a gale chops it up; the fetch it lies open to scales that.
    float windy = smoothstep(1.0, 14.0, speed);
    float rough = mix(0.35, 1.0, windy) * mix(0.55, 1.0, open);
    vec2 tilt = vec2(0.0);
    // Ripples: the map at two scales, a little either side of the wind, each running downwind at a pace of its own (a
    // ripple's phase speed in deep water, sqrt(g L / 2 pi), slowed: it's a pattern of them, not one) and down the current.
    for (int i = 0; i < 2; i++) {
        float tile = i == 0 ? 2.7 : 7.9;
        vec2 along, across;
        vec2 s = turned(at, rotated(dir, i == 0 ? 0.38 : -0.31), tile, along, across);
        float pace = sqrt(9.81 * tile / 6.2831853) * 0.35;
        vec2 drift = vec2(dot(current, across), dot(current, along) + pace);
        vec3 m = textureGrad(normalMaps, vec3(s - fract(drift * t / tile), vLayer), dFdx(s), dFdy(s)).xyz * 2.0 - 1.0;
        tilt += (across * m.x + along * m.y) / max(m.z, 0.35) * (i == 0 ? 0.55 : 0.8);
    }
    tilt *= rough;
    // Swell: three long waves near downwind (each on a whole-number lattice, so the wrap is seamless), each at deep
    // water's own speed (w^2 = g k); none on a still pool, most on the open sea, which has some even on a still night
    // (a storm's, far off); faded out before they're finer than a pixel.
    float foot = length(fwidth(at));
    crest = 0.0;
    for (int k = 0; k < 3; k++) {
        float len = k == 0 ? 23.0 : k == 1 ? 37.0 : 61.0;
        vec2 kv = floor(rotated(dir, k == 0 ? 0.33 : k == 1 ? -0.21 : 0.06) * (WRAP / len) + 0.5) * (6.2831853 / WRAP);
        float kk = max(length(kv), 1e-4);
        float ph = dot(kv, at) - sqrt(9.81 * kk) * t + float(k) * 1.7;
        // Steepness (a k): 0.025 on the open sea on a still night, 0.07 in a gale.
        float a = (0.025 + 0.045 * windy) / kk * open * (1.0 - smoothstep(len * 0.08, len * 0.3, foot));
        tilt -= a * kv * cos(ph);
        crest = max(crest, smoothstep(0.6, 1.0, sin(ph)) * open);
    }
    return normalize(vec3(tilt.x, 1.0, tilt.y));
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

// How much of the hand lamp reaches this point (MeshBuilder.ShadowLight): its cube's face the point lies along (the
// axis it's furthest out on from the flame), four taps of that face's layer, as the headlamp's.
float handShadowAt(vec3 p, vec3 n) {
    if (frame.handColour.a < 0.5)
        return 1.0;
    vec3 d = p - frame.handPos.xyz;
    vec3 a = abs(d);
    int face = a.x >= a.y && a.x >= a.z ? (d.x > 0.0 ? 0 : 1) : a.y >= a.z ? (d.y > 0.0 ? 2 : 3) : (d.z > 0.0 ? 4 : 5);
    vec4 ls = frame.handViewProj[face] * vec4(p + n * 0.025, 1.0);
    if (ls.w <= 0.0)
        return 1.0;
    vec3 c = ls.xyz / ls.w;
    vec2 uv = c.xy * 0.5 + 0.5;
    if (c.z >= 1.0)
        return 1.0;
    float texel = 1.0 / 512.0;
    float z = c.z - 0.0003;
    float layer = float(face);
    return 0.25 * (texture(handShadow, vec4(uv + vec2(-0.75, -0.75) * texel, layer, z)) + texture(handShadow, vec4(uv + vec2(0.75, -0.75) * texel, layer, z))
                 + texture(handShadow, vec4(uv + vec2(-0.75, 0.75) * texel, layer, z)) + texture(handShadow, vec4(uv + vec2(0.75, 0.75) * texel, layer, z)));
}

// The moon's shadow: an orthographic map over the ground round the camera, filtered over a few texels (moonlight through
// cloud has a soft edge), fading out toward the map's edge so its end isn't a line across the ground.
float moonShadowAt(vec3 p, vec3 n) {
    if (frame.counts.y < 0.5)
        return 1.0;
    vec3 c = (frame.moonViewProj * vec4(p + n * 0.06, 1.0)).xyz;
    vec2 uv = c.xy * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || c.z >= 1.0 || c.z <= 0.0)
        return 1.0;
    float texel = frame.counts.z;
    float z = c.z - 0.0008;
    float s = 0.0;
    for (int i = 0; i < 8; i++) {
        float a = float(i) * 2.3999632;
        vec2 o = vec2(cos(a), sin(a)) * (0.8 + 1.2 * float(i) / 8.0) * texel;
        s += texture(moonShadow, vec3(uv + o, z));
    }
    s /= 8.0;
    vec2 edge = abs(uv - 0.5) * 2.0;
    return mix(s, 1.0, smoothstep(0.85, 1.0, max(edge.x, edge.y)));
}

// The 4x4 ordered-dither threshold at a pixel (0..1): a ghost's screen-door transparency, as the era's games drew one.
float bayer2(vec2 q) {
    return q.y < 0.5 ? (q.x < 0.5 ? 0.0 : 2.0) : (q.x < 0.5 ? 3.0 : 1.0);
}
float bayer4(vec2 px) {
    vec2 q = mod(floor(px), 4.0);
    return (4.0 * bayer2(mod(q, 2.0)) + bayer2(floor(q / 2.0)) + 0.5) / 16.0;
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
    // Eaten away (bite.glsl): gone behind the frontier, and gnawed along it.
    float bitten = vBite.y > 0.0 ? biteInto(vObj, vBite, vBiteFloor, vScar.y) : -1e3;
    if (bitten > 0.0)
        discard;
    vec3 n = normalize(vNormal);
    // A ghost's membrane (wear under -0.5: a ghost material, CreatureArt.Resolve): screen-door transparent by the
    // view, thin and see-through face on, dense at its rim where the eye looks through more of it, and there it gives
    // off a faint cold of its own (GDD 26: the corruption palette, never neon).
    float ghostRim = -1.0;
    if (vWear < -0.5) {
        ghostRim = 1.0 - abs(dot(n, normalize(-vPos)));
        if (mix(0.2, 1.0, pow(ghostRim, 1.4)) < bayer4(gl_FragCoord.xy))
            discard;
    }
    if (!gl_FrontFacing)
        n = -n; // two-sided cards (pine boughs, grass) light from whichever side you see
    // A layer past what's loaded (a renderer not given the look's textures) draws as flat colour, not garbage.
    bool textured = vLayer >= 0.0 && vLayer < frame.params.w;
    bool ps2 = frame.params.y > 0.5;
    bool terrain = textured && vLayer2 >= 0.0 && vLayer2 < frame.params.w;
    bool water = textured && !terrain && layerMotion(vLayer) > 1.5;
    float waterCrest = 0.0;

    vec4 tex = vec4(1.0);
    vec3 specMap = vec3(vShine, 0.3, 0.0);
    if (textured && terrain) {
        // The terrain blend (pipeline shader set, "terrain layer blend"): the second layer shows through by the
        // vertex weight, broken up by the first's own brightness and a blocky noise, so the edge is crunchy and
        // follows the texture (grass fills the low spots of the mud first), not a smooth crossfade.
        // Each layer is bombed (Tiling), the second at an off-ratio scale so the two never line up, and both are
        // modulated by a much larger copy of themselves (brightness over the layer's mean from its last mip), more
        // so with distance, where a hillside of five-metre tiles would otherwise read as a quilt.
        vec2 uv2 = vUv * 0.63;
        Tiling t1 = tiling(vUv), t2 = tiling(uv2 + 13.7);
        vec4 tex2;
        tex = bombed(diffuseMaps, t1, vLayer, true);
        tex2 = bombed(diffuseMaps, t2, vLayer2, true);
        vec3 lw = vec3(0.3, 0.59, 0.11);
        float far = smoothstep(8.0, 45.0, length(vPos));
        float macro = dot(texture(diffuseMaps, vec3(vUv * 0.173 + 0.37, vLayer)).rgb, lw)
            / max(dot(textureLod(diffuseMaps, vec3(0.5, 0.5, vLayer), 12.0).rgb, lw), 0.02);
        float macro2 = dot(texture(diffuseMaps, vec3(vUv * 0.117 + 0.71, vLayer2)).rgb, lw)
            / max(dot(textureLod(diffuseMaps, vec3(0.5, 0.5, vLayer2), 12.0).rgb, lw), 0.02);
        tex.rgb *= mix(1.0, clamp(macro, 0.45, 1.7), 0.3 + 0.35 * far);
        tex2.rgb *= mix(1.0, clamp(macro2, 0.45, 1.7), 0.3 + 0.35 * far);
        float breakup = dot(tex.rgb, lw) * 1.4 + (noise(floor(vSurface / 3.0)) - 0.5) * 0.5;
        // (None of the second where its weight is nought: the dark of the first's texels let it through before, so a shore's
        // shingle came up the bank wherever the ground was dark; note 424.)
        float w = smoothstep(0.0, 0.18, vBlend * 1.4 - 0.2 - breakup * 0.6 + 0.3) * smoothstep(0.0, 0.12, vBlend);
        tex = mix(tex, tex2, w);
        specMap = mix(bombed(specMaps, t1, vLayer, false).rgb, bombed(specMaps, t2, vLayer2, false).rgb, w);
        if (ps2)
            specMap = vec3(specMap.r * 0.5, 0.2, specMap.b);
        else {
            vec3 mapped = mix(bombed(normalMaps, t1, vLayer, false).xyz, bombed(normalMaps, t2, vLayer2, false).xyz, w) * 2.0 - 1.0;
            n = perturb(n, vPos, vUv, normalize(mapped));
        }
    } else if (water) {
        // Its colour from its own map, at its tile; its relief all in its moving normal (waterNormal).
        n = waterNormal(vUv, vSurface.xy, vSurface.z, frame.fogHeight.w, waterCrest);
        tex = texture(diffuseMaps, vec3(vUv / 8.0, vLayer));
        specMap = vec3(1.0, 1.0, 0.0);
    } else if (textured) {
        float hero = ps2 ? -1.0 : heroSlot(vLayer);
        tex = hero >= 64.0 ? texture(bigDiffuse, vec3(vUv, hero - 64.0)) : hero >= 0.0 ? texture(heroDiffuse, vec3(vUv, hero)) : texture(diffuseMaps, vec3(vUv, vLayer));
        if (tex.a < 0.5)
            discard; // alpha test, never blend (pipeline: "alpha test at 0.5")
        specMap = (hero >= 64.0 ? texture(bigSpec, vec3(vUv, hero - 64.0)) : hero >= 0.0 ? texture(heroSpec, vec3(vUv, hero)) : texture(specMaps, vec3(vUv, vLayer))).rgb;
        if (ps2)
            specMap = vec3(specMap.r * 0.5, 0.2, specMap.b);
        else
            n = perturb(n, vPos, vUv, normalize((hero >= 64.0 ? texture(bigNormal, vec3(vUv, hero - 64.0)) : hero >= 0.0 ? texture(heroNormal, vec3(vUv, hero)) : texture(normalMaps, vec3(vUv, vLayer))).xyz * 2.0 - 1.0));
    }
    vec3 albedo = tex.rgb * vColor;
    // The water's edge: shallower toward the land, the bottom showing through browner and lighter; and the lap, a band of
    // broken foam along the waterline that runs up and back with the swell, more of it in a wind, and whitecaps in a gale.
    float foam = 0.0;
    if (water) {
        float shore = clamp(vBlend, 0.0, 1.0), wind = length(frame.wind.xz), t = frame.fogHeight.w;
        // Deep water scatters next to nothing back (a lamp on it is a glare, not a lit patch); the shallows more. (A silty
        // water, Fundy's, carries its murk in its tint: WorldArt.Water.)
        albedo *= mix(0.35, 1.5, shore * shore);
        float reach = 0.5 + 0.25 * sin(t * 0.8 + dot(vUv, vec2(0.043, 0.071))) + 0.25 * waterCrest;
        float froth = noise(vec3(vUv * 0.9, t * 0.35)) * 0.6 + noise(vec3(vUv * 3.1, t * 0.8)) * 0.4;
        foam = smoothstep(1.0 - reach * 0.6, 1.0, shore) * smoothstep(0.42, 0.62, froth) * mix(0.35, 1.0, smoothstep(2.0, 12.0, wind));
        foam += waterCrest * smoothstep(0.66, 0.8, froth) * smoothstep(8.0, 16.0, wind) * 0.6;
        foam = clamp(foam, 0.0, 1.0);
        albedo = mix(albedo, vec3(0.42, 0.44, 0.45), foam);
    }
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
    // Gnawed along the frontier: the torn edge raw and dark, wet with what it slavers, scored by the teeth in grooves
    // that run back from the edge; scraped pale in the grooves where it bit into plate.
    float gnawed = 0.0;
    if (bitten > -0.6) {
        gnawed = smoothstep(-0.6, -0.02, bitten);
        float grooves = step(0.72, fract(vObj.y * 11.0 + 0.35 * sin(vObj.x * 9.0 + vObj.z * 3.0))) * smoothstep(-0.45, -0.1, bitten);
        albedo = mix(albedo, albedo * vec3(0.3, 0.2, 0.18) + vec3(0.03, 0.006, 0.005), gnawed * 0.85);
        albedo = mix(albedo, vec3(0.012, 0.004, 0.003), smoothstep(-0.07, -0.01, bitten));
        albedo = mix(albedo, max(albedo, vec3(0.06, 0.05, 0.045)), grooves * (1.0 - gnawed * 0.5));
    }

    // Rain: darker surfaces, and a sheen on everything that faces the sky (ballast, roofs, puddles in the mud).
    float inside = frame.counts.x > 0.0 ? indoors(vPos) : 0.0;
    float night = 1.0 - inside;
    float wet = frame.sky2.w * (vWear > 0.0 || textured ? 1.0 : 0.0) * night * (water ? 0.0 : 1.0);
    albedo *= 1.0 - 0.3 * wet;
    // Frost: a pale rime over what's out in the night, thick on what faces the sky (roofs, ballast, the tops of
    // things), thin on the walls, broken up by the surface's own grain so it lies in the texture's hollows and edges.
    // (Pitch, worn under 0.015 (PlanArt.PitchWear: the tar ponds), stays black and wet: it doesn't rime.)
    float frost = frame.counts.w * night * (textured ? 1.0 : 0.6) * (vWear > 0.0 && vWear < 0.015 || water ? 0.0 : 1.0);
    float rime = frost * (0.22 + 0.6 * smoothstep(0.2, 0.9, n.y)) * (0.55 + 0.45 * smoothstep(0.02, 0.2, dot(albedo, vec3(0.33))));
    albedo = mix(albedo, vec3(0.5, 0.54, 0.6), clamp(rime, 0.0, 0.8));

    vec3 v = normalize(-vPos);
    vec3 moonDir = normalize(frame.moon.xyz);
    // Indoors the fill is low and warm (lamplight off the boards), and the moon doesn't get in.
    vec3 light = frame.moon.w * mix(mix(GROUND_BOUNCE, SKY_FILL, n.y * 0.5 + 0.5), vec3(0.55, 0.42, 0.3), inside);
    float moonLit = night * moonShadowAt(vPos, n);
    light += frame.moonColour.rgb * frame.moonColour.a * max(dot(n, moonDir), 0.0) * moonLit;

    // Phong exponent from gloss: 4..128, clamped so nothing mirror-polishes (pipeline "Gloss").
    float shininess = textured ? mix(4.0, 128.0, specMap.g * specMap.g) : 40.0;
    float specStrength = specMap.r * (1.0 - 0.8 * foam);
    float up = smoothstep(0.5, 0.95, n.y) * wet;
    // Standing water where the surface dips (the ground's hollows, a roof's sag, the ballast between the ties): a mirror
    // of the sky in patches, the rest only filmed over.
    float puddle = up * smoothstep(0.58, 0.72, noise(vSurface / 3.0)) * (textured ? 1.0 : 0.0);
    albedo *= 1.0 - 0.35 * puddle;
    specStrength = max(specStrength, 0.3 * up + 0.4 * puddle);
    shininess = mix(shininess, 70.0, up);
    shininess = mix(shininess, 110.0, puddle);
    // Its crystals glint.
    specStrength = max(specStrength, 0.22 * rime);
    shininess = mix(shininess, 90.0, rime);
    // Torn edges catch the light; scorch doesn't; a hole throws nothing back.
    specStrength = mix(mix(specStrength, specStrength * 0.3, scar.x), 0.4, scar.z) * (1.0 - scar.w);
    shininess = mix(shininess, 56.0, scar.z);
    // (The slaver on a gnawed edge shines like a wet mouth.)
    specStrength = mix(specStrength, 0.5, gnawed * 0.8);
    shininess = mix(shininess, 70.0, gnawed);
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
    spec += frame.moonColour.rgb * pow(max(dot(n, normalize(moonDir + v)), 0.0), shininess * 0.6) * (0.08 + 0.5 * up) * moonLit;
    // The moon's road on the water: a glitter of it on every facet of the ripples that turns it to the eye.
    if (water)
        spec += frame.moonColour.rgb * frame.moonColour.a * pow(max(dot(n, normalize(moonDir + v)), 0.0), 420.0) * 4.0 * moonLit;

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

    // The hand lamp, as the practical lights but shadowed: what's between it and a surface throws its shadow there, and
    // the shadows swing as the lamp swings in its carrier's fist (GDD 31).
    if (frame.handPos.w > 0.0) {
        vec3 d = frame.handPos.xyz - vPos;
        float dist = length(d);
        if (dist < frame.handPos.w) {
            vec3 ld = d / max(dist, 1e-4);
            float att = 1.0 - dist / frame.handPos.w;
            att *= att * handShadowAt(vPos, n);
            light += frame.handColour.rgb * att * max(dot(n, ld), 0.0) * 1.6;
            spec += frame.handColour.rgb * att * pow(max(dot(n, normalize(ld + v)), 0.0), shininess) * 0.6;
        }
    }

    vec3 colour = albedo * light + spec * specStrength * (vWear > 0.0 ? 0.55 + 0.45 * noise(vSurface / 16.0) : 1.0);
    // Reflection, by Schlick's Fresnel: every surface picks up the sky at a grazing angle, the glossy ones (brass, glass,
    // wet steel, a puddle) head on too; the rough ones hardly at all. Indoors it's the lamplit room, warm and dim.
    if (!ps2 || water) {
        float gloss = textured ? specMap.g : 0.45;
        gloss = mix(gloss, 0.85, up);
        gloss = mix(gloss, 0.97, puddle);
        float f0 = water ? 0.02 : mix(0.02, 0.3, clamp(specStrength * 1.4, 0.0, 1.0)); // glass and water ~0.02-0.04, worn metal more
        gloss *= 1.0 - foam;
        // (A face seen from behind, a card or a thin plate, reflects off the side that faces the eye.)
        vec3 nf = dot(n, v) < 0.0 ? -n : n;
        float fres = f0 + (1.0 - f0) * pow(1.0 - dot(nf, v), 5.0);
        vec3 env = mix(envAt(reflect(-v, nf)), vec3(0.03, 0.022, 0.014), inside);
        // Open water sees the haze along the horizon in it, not a world close round it: brighter low down, so its ripples
        // read even where the moon doesn't reach.
        if (water) {
            vec3 r = reflect(-v, nf);
            env = mix(frame.fog.rgb * frame.sky2.z * 1.15, frame.sky.rgb, smoothstep(0.0, 0.5, r.y));
        }
        colour += env * fres * gloss * gloss * smoothstep(0.25, 0.7, gloss) * (1.0 - scar.w) * 1.6;
    }
    float emissive = max(vEmissive, specMap.b);
    colour = mix(colour, albedo * 2.0 * vGlow, emissive);
    if (ghostRim >= 0.0)
        colour += vec3(0.32, 0.4, 0.5) * pow(ghostRim, 2.5) * 0.35 * vGlow;

    // Light sources punch through fog further than lit surfaces: the lamp is the last thing you lose.
    colour = mix(colour, frame.fog.rgb, fogAmount(vPos) * (1.0 - 0.6 * emissive * min(vGlow, 1.0)));
    outColor = vec4(colour, 1.0);
}
