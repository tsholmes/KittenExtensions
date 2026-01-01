#version 450 core

#include "CrtCommon.glsl"

layout(location = 0) out vec4 outColor;

layout(set=0, binding=0) uniform sampler2D imguiTex;

layout(location = 0) in struct {
  vec2 Px;
  vec2 Uv;
} In;
layout(location = 4) flat in vec4 PxRect;
layout(location = 8) flat in vec4 UvRect;

CrtConfig crt = CrtConfig(
  vec2(2), // warpPow
  mat2(vec2(0,0.3),vec2(0.4,0)), // warpMat
  vec2(0,-1.5), // abR
  vec2(0), // abG
  vec2(0,1.5), // abB
  vec3(0,1,0), // alphaDot
  vec2(0,1), // scanDir
  5, // scanSep
  0.3, // scanMag
  vec4(0), // scanCr
  0 // scanAlpha
);


void main()
{
  CoordSpace cs = aaCoords(UvRect, UvRect, 1/(PxRect.zw-PxRect.xy));
  vec4 ccr = sampleCrt(imguiTex, In.Uv, crt, cs);
  if (ccr.a == 0)
    discard;
  outColor = ccr;
}