#version 450 core

// ImGui post-processing vertex shader
//   receives push_constants of pixel and uv bounding rects for all imgui draw commands
//   produces 4 triangle-strip vertices

layout(push_constant) uniform uPushConstant {
  vec4 PxRect;
  vec4 UvRect;
} pc;

out gl_PerVertex {
  vec4 gl_Position;
};

layout(location = 0) out struct {
  vec2 Px;
  vec2 Uv;
} Out;
layout(location = 4) flat out vec4 PxRect;
layout(location = 8) flat out vec4 UvRect;

void main()
{
  int ix = 0;
  int iy = 1;
  if (gl_VertexIndex >= 2)
    ix = 2;
  if (gl_VertexIndex%2 == 1)
    iy = 3;

  vec2 uv = vec2(pc.UvRect[ix], pc.UvRect[iy]);
  Out.Px = vec2(pc.PxRect[ix], pc.PxRect[iy]);
  Out.Uv = uv;
  PxRect = pc.PxRect;
  UvRect = pc.UvRect;

  gl_Position = vec4(uv * 2.0 - 1.0, 0, 1);
}