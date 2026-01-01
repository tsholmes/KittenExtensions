
// adapted from https://www.shadertoy.com/view/WsVSzV

struct CrtConfig {
  // warpUv = compmul(uv, 1+matmul(warpMat, pow(centerOff, warpPow)))
  vec2 warpPow;
  mat2 warpMat;
  // pixel offsets of channels for chromatic aberration
  vec2 abR;
  vec2 abG;
  vec2 abB;
  vec3 alphaDot; // srcAlpha = dot(alphaDot, vec3(pxR.a, pxG.a, pxB.a))
  vec2 scanDir; // vector perpendicular to scanlines
  float scanSep; // distance between scanline peaks in pixels
  float scanMag; // max magnitude of scanline effect (0-1)
  vec4 scanCr;
  float scanAlpha; // alpha = mix(srcAlpha, scanCr.a, scan*scanAlpha)
};

struct CoordSpace {
  mat3 screen2uv;
  mat3 uv2tex;
  vec2 px2uv;
};

CoordSpace aaCoords(vec4 screenBounds, vec4 texBounds, vec2 px2uv)
{
  vec2 screenSz = screenBounds.zw-screenBounds.xy;
  vec2 texSz = texBounds.zw-texBounds.xy;
  vec2 relSz = (texBounds.zw-texBounds.xy)/screenSz;

  return CoordSpace(
    mat3(
      vec3(1/screenSz.x, 0, 0),
      vec3(0, 1/screenSz.y, 0),
      vec3(-screenBounds.xy/screenSz, 1)
    ),
    mat3(
      vec3(texSz.x, 0, 0),
      vec3(0, texSz.y, 0),
      vec3(texBounds.xy, 1)
    ),
    px2uv
  );
}

vec2 csScreen2Uv(CoordSpace cs, vec2 screen) { return (cs.screen2uv*vec3(screen, 1)).xy; }
vec2 csUv2Tex(CoordSpace cs, vec2 uv) { return (cs.uv2tex*vec3(uv, 1)).xy; }

vec2 crtScreenToUv(CrtConfig crt, CoordSpace cs, vec2 screen, vec2 uvOff)
{
  // in uv coords (0-1)
  vec2 uv = csScreen2Uv(cs, screen) + uvOff;
  // centered uv coords (-0.5,-0.5)
  vec2 cuv = uv-0.5;
  // warp multiplier
  vec2 warp = crt.warpMat * pow(abs(0.5-uv), crt.warpPow);
  // warped uv (0-1)
  return cuv*(1+warp) + 0.5;
}

vec4 sampleCrt(sampler2D tex, vec2 screen, CrtConfig crt, CoordSpace cs)
{
  vec2 ruv = crtScreenToUv(crt, cs, screen, crt.abR*cs.px2uv);
  vec2 guv = crtScreenToUv(crt, cs, screen, crt.abG*cs.px2uv);
  vec2 buv = crtScreenToUv(crt, cs, screen, crt.abB*cs.px2uv);

  if (guv.x < 0 || guv.x > 1 || guv.y < 0 || guv.y > 1)
    return vec4(0);
  
  ruv = clamp(ruv, vec2(0), vec2(1));
  buv = clamp(buv, vec2(0), vec2(1));

  vec2 rtex = csUv2Tex(cs, ruv);
  vec2 gtex = csUv2Tex(cs, guv);
  vec2 btex = csUv2Tex(cs, buv);

  vec4 rcr = textureLod(tex, rtex, 0);
  vec4 gcr = textureLod(tex, gtex, 0);
  vec4 bcr = textureLod(tex, btex, 0);

  float scanPos = dot(guv/cs.px2uv, crt.scanDir)/crt.scanSep;
  float scan = crt.scanMag*abs(sin(scanPos * 3.1415926535));

  vec3 rgb = vec3(rcr.r, gcr.g, bcr.b);
  float srcAlpha = dot(vec3(rcr.a, gcr.a, bcr.a), crt.alphaDot);

  return vec4(
    mix(rgb, crt.scanCr.rgb, scan),
    mix(srcAlpha, crt.scanCr.a, scan*crt.scanAlpha)
  );
}
