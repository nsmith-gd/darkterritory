// The frame's constants, shared by every scene shader (std140; mirrors FrameData in GreyboxRenderer.cs).
layout(set = 0, binding = 0) uniform Frame {
    mat4 viewProj;
    mat4 invViewProj;
    mat4 lampViewProj; // camera-relative to the headlamp's shadow map
    vec4 fog;          // rgb colour, a = density per metre
    vec4 fogHeight;    // x = fog base height (camera-relative), y = falloff per metre up, z = density kept up high, w = seconds
    vec4 moon;         // xyz towards the moon, w = ambient
    vec4 moonColour;   // rgb, a = direct strength
    vec4 lampPos;      // xyz headlamp (camera-relative), w = range
    vec4 lampDir;      // xyz its direction, w = cos(cone half-angle)
    vec4 lampColour;   // rgb, a = intensity
    vec4 sky;          // rgb zenith, a = how far the backdrop sinks into the fog
    vec4 params;       // x = point lights, y = 1 for the PS2 look, z = shader grime over textures, w = layers loaded
    vec4 sky2;         // x = the backdrop band's height in radians, y = fog curve exponent, z = horizon haze over the fog, w = wetness
    vec4 lights[64];   // pairs: xyz position (camera-relative) + range, rgb colour
    vec4 rooms[48];    // triples: centre + half x, right axis + half y, back axis + half z (up = back x right)
    vec4 counts;       // x = rooms, y = 1 when the moon casts a shadow, z = one texel of its map, w = frost
    mat4 moonViewProj; // camera-relative to the moon's shadow map (orthographic)
    vec4 heroOf[64];   // per layer (4 a vec4): its slot in the hero arrays, or -1
    vec4 dawn;         // xyz = the glow low on the dawn's horizon, w = how far it's up
} frame;
