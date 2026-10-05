// Which eye this invocation draws (ARCHITECTURE section 8, note 213). A multiview renderer compiles every scene and
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
#endif
