#version 450
// Overlay: 2D quads in the frame's pixels (top-left origin) to Vulkan clip space (Y down).
layout(push_constant) uniform Screen {
    vec2 size;
} screen;

layout(location = 0) in vec2 inPos;
layout(location = 1) in vec4 inColour;
layout(location = 0) out vec4 vColour;

void main() {
    gl_Position = vec4(inPos / screen.size * 2.0 - 1.0, 0.0, 1.0);
    vColour = inColour;
}
