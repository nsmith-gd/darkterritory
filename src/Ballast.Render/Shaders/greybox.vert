#version 450
// Greybox forward pass. Positions arrive camera-relative (floating origin at the eye),
// so fog distance is just the length of the position.

layout(push_constant) uniform Frame {
    mat4 viewProj;
    vec4 fog;        // rgb colour, a = density
    vec4 moon;       // xyz direction towards the moon, w = ambient
    vec4 lampPos;    // xyz position, w = range
    vec4 lampDir;    // xyz direction, w = cos(cone half-angle)
} frame;

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inColor;
layout(location = 3) in float inEmissive;

layout(location = 0) out vec3 vPos;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec3 vColor;
layout(location = 3) out float vEmissive;

void main() {
    vPos = inPos;
    vNormal = inNormal;
    vColor = inColor;
    vEmissive = inEmissive;
    gl_Position = frame.viewProj * vec4(inPos, 1.0);
}
