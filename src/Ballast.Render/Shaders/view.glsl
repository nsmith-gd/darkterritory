// Which eye this invocation draws (ARCHITECTURE section 8, note 221). A multiview renderer compiles every scene and
// post shader with MULTIVIEW defined (and GL_EXT_multiview enabled, ahead of this): one pass draws both eyes into the
// layers of an array target, and gl_ViewIndex says which layer an invocation is for. Without it, the same source draws
// one view into a plain 2D target, as it always has. (ASCII only in this file: a section sign this early in a shader
// broke glslang's parse.)
#ifndef VIEW_GLSL
#define VIEW_GLSL
#ifdef MULTIVIEW
#define EYE gl_ViewIndex
// A per-eye target, sampled at this eye's layer.
#define EYE_SAMPLER sampler2DArray
#define EYE_UV(uv) vec3((uv), float(gl_ViewIndex))
#else
#define EYE 0
#define EYE_SAMPLER sampler2D
#define EYE_UV(uv) (uv)
#endif

// A colour for (or from) the half-float scene target made safe (note 1017): NaN to nought, and held inside half float's
// range (65504) so it never stores as infinity. A real driver may round an overflow to infinity where lavapipe saturates,
// or make a NaN lavapipe doesn't; the bloom's blurs then spread each one into a square block, which the tone curve draws
// black: a Linux player's screen of black squares. Tested by the bits, so no driver's fast maths can fold it away.
vec3 finite(vec3 c) {
    bvec3 nan = greaterThan(floatBitsToUint(c) & uvec3(0x7fffffffu), uvec3(0x7f800000u));
    return mix(clamp(c, vec3(-65000.0), vec3(65000.0)), vec3(0.0), nan);
}
#endif
