/** GLSL for the composite (upscale + post-processing) pass and the bloom bright pass. */

export const compositeVert = /* glsl */ `
in vec2 aPosition;
in vec2 aUV;
out vec2 vUV;
uniform mat3 uProjectionMatrix;
uniform mat3 uWorldTransformMatrix;
uniform mat3 uTransformMatrix;
void main() {
  mat3 mvp = uProjectionMatrix * uWorldTransformMatrix * uTransformMatrix;
  gl_Position = vec4((mvp * vec3(aPosition, 1.0)).xy, 0.0, 1.0);
  vUV = aUV;
}
`;

export const MAX_WAVES = 4;

export const compositeFrag = /* glsl */ `
precision highp float;
in vec2 vUV;
out vec4 finalColor;
uniform sampler2D uWorld;
uniform sampler2D uLight;
uniform sampler2D uBloom;
uniform vec2 uViewSize;   // visible world area, in RT pixels
uniform vec2 uRTSize;     // render texture size
uniform vec2 uOffset;     // fractional camera offset, in RT pixels
uniform vec2 uScale;      // screen pixels per world pixel
uniform vec3 uAmbient;
uniform float uLightPulse;
uniform float uBloomStrength;
uniform vec4 uWaves[${MAX_WAVES}]; // x, y (view px), radius, strength
uniform float uAberration;
uniform vec3 uGradeMul;
uniform vec3 uGradeLift;
uniform float uContrast;
uniform float uSaturation;
uniform float uVignette;
uniform vec4 uFlash;      // rgb, alpha
uniform float uHaze;
uniform float uTime;

vec2 sharpUV(vec2 texel) {
  vec2 fl = floor(texel);
  vec2 s = fract(texel);
  vec2 range = 0.5 - 0.5 / uScale;
  vec2 cd = s - 0.5;
  vec2 f = (cd - clamp(cd, -range, range)) * uScale + 0.5;
  return (fl + f) / uRTSize;
}

void main() {
  vec2 texel = vUV * uViewSize + uOffset;
  // Shockwaves push pixels outward along a thin ring.
  for (int i = 0; i < ${MAX_WAVES}; i++) {
    vec4 w = uWaves[i];
    if (w.w <= 0.0) continue;
    vec2 d = texel - w.xy;
    float dist = length(d);
    float band = 1.0 - clamp(abs(dist - w.z) / 18.0, 0.0, 1.0);
    if (dist > 0.001) texel += (d / dist) * band * w.w * 7.0;
  }
  if (uHaze > 0.0) {
    texel.x += sin(texel.y * 0.09 + uTime * 3.0) * 0.6 * uHaze;
    texel.y += sin(texel.x * 0.07 + uTime * 2.3) * 0.4 * uHaze;
  }
  vec2 uv = sharpUV(texel);
  vec3 c;
  if (uAberration > 0.001) {
    vec2 dir = (vUV - 0.5) * uAberration * 3.0;
    c.r = texture(uWorld, sharpUV(texel + dir)).r;
    c.g = texture(uWorld, uv).g;
    c.b = texture(uWorld, sharpUV(texel - dir)).b;
  } else {
    c = texture(uWorld, uv).rgb;
  }
  vec3 light = texture(uLight, uv).rgb;
  vec3 lit = c * (uAmbient * uLightPulse + light * 1.3);
  lit += texture(uBloom, uv).rgb * uBloomStrength;
  // Colour grade.
  float luma = dot(lit, vec3(0.299, 0.587, 0.114));
  lit = mix(vec3(luma), lit, uSaturation);
  lit = (lit - 0.5) * uContrast + 0.5;
  lit = lit * uGradeMul + uGradeLift;
  // Vignette.
  vec2 q = vUV - 0.5;
  float v = smoothstep(0.35, 0.95, length(q * vec2(1.0, 0.75)) * 1.25);
  lit *= 1.0 - v * uVignette;
  lit = mix(lit, uFlash.rgb, uFlash.a);
  finalColor = vec4(clamp(lit, 0.0, 1.0), 1.0);
}
`;

export const brightFrag = /* glsl */ `
in vec2 vTextureCoord;
out vec4 finalColor;
uniform sampler2D uTexture;
uniform float uThreshold;
void main() {
  vec4 c = texture(uTexture, vTextureCoord);
  float l = max(c.r, max(c.g, c.b));
  finalColor = vec4(c.rgb * smoothstep(uThreshold, 1.0, l), 1.0);
}
`;
