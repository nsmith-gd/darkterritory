// GPU skinning (MeshBuilder.Skinned). The skinned pipelines define SKINNED: a vertex is then posed by up to four bones
// of its draw's palette (linear blend, as Skinner.Emit did on the CPU), read from the frame's bone buffer at the draw's
// base. The palette's matrices take the model's bind space to its object space; the draw's model matrix does the rest.
#ifdef SKINNED
layout(location = 11) in vec4 inJoints;
layout(location = 12) in vec4 inWeights;
layout(std430, set = 0, binding = 10) readonly buffer Bones { mat4 bones[]; };

mat4 skinOf(float base) {
    int b = int(base + 0.5);
    return bones[b + int(inJoints.x + 0.5)] * inWeights.x + bones[b + int(inJoints.y + 0.5)] * inWeights.y
        + bones[b + int(inJoints.z + 0.5)] * inWeights.z + bones[b + int(inJoints.w + 0.5)] * inWeights.w;
}
#endif
