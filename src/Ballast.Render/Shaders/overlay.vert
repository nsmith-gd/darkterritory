#version 450
#include "view.glsl"
// Overlay: 2D quads in the frame's pixels (top-left origin) to Vulkan clip space (Y down).
// Drawing both eyes at once (multiview), the buffer holds the left eye's vertices then the right's, split at `split`:
// each eye keeps its own and pushes the other's out of the clip volume. A negative split: both eyes see all of it.
layout(push_constant) uniform Screen {
    vec2 size;
    float split;
} screen;

layout(location = 0) in vec2 inPos;
layout(location = 1) in vec4 inColour;
layout(location = 0) out vec4 vColour;

void main() {
    gl_Position = vec4(inPos / screen.size * 2.0 - 1.0, 0.0, 1.0);
    vColour = inColour;
#ifdef MULTIVIEW
    if (screen.split >= 0.0 && (gl_VertexIndex < int(screen.split)) != (gl_ViewIndex == 0))
        gl_Position = vec4(0.0, 0.0, -1.0, 1.0);
#endif
}
