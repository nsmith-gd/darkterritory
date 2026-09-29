#version 450
// Overlay colours are display values already (like the scene's output), blended over the frame.
layout(location = 0) in vec4 vColour;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vColour;
}
