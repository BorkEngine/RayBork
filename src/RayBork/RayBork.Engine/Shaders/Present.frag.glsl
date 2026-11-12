#version 450

// Present pass: samples the resolved linear HDR scene color, applies exposure and the configured
// tone mapping, and writes the display-referred value.
//
// This source is compiled to Present.frag.spv by scripts/compile-shaders.ps1 and the committed
// blob is embedded in RayBork.Engine. The runtime never compiles shaders.
//
// The SDR paths write display-referred [0,1], byte for byte as M2 did. The scRGB HDR path keeps AgX
// for the SDR range, normalizes AgX's neutral white to display luminance 1.0, scales it to the
// window's reported SDR white level (paper white), and extends scene luminance above the AgX white
// point into the reported HDR headroom at constant chromaticity. HDR10/PQ presentation is
// deliberately deferred: it would be another value of `pc.encoding` instead of a change to the
// tone-mapping path below.

layout(set = 0, binding = 0) uniform sampler2D uSceneColor;

layout(push_constant) uniform PresentConstants {
    float exposureMultiplier;      // 0  = pow(2.0, exposureEv), computed on the CPU
    float hdrWhiteScale;           // 4  = paper white in scRGB units; 1.0 on the SDR paths
    float hdrPeakScale;            // 8  = paper white * HDR headroom; 1.0 on the SDR paths
    float agxWhiteSceneLuminance;  // 12 = AGX_WHITE_SCENE_LUMINANCE, the highlight knee
    int tonemapping;               // 16 = 0 AgX, 1 none
    int encoding;                  // 20 = 0 linear [0,1], 1 manual sRGB encode, 2 scRGB HDR linear
    int hdrHighlight;              // 24 = 1 when the HDR highlight extension is active
    int reserved;                  // 28 = 0, keeps the block 32 bytes
} pc;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

// AgX base transform in linear display space: inset -> log2 range -> contrast polynomial -> outset.
// There is no look transform. The sRGB transfer is applied by the encoding step below or by the
// sRGB swapchain format, never inside this function. The constants are from the published minimal
// AgX implementation.
const mat3 AGX_INSET = mat3(
    0.842479062253094, 0.0423282422610123, 0.0423756549057051,
    0.0784335999999992, 0.878468636469772, 0.0784336,
    0.0792237451477643, 0.0791661274605434, 0.879142973793104);
const mat3 AGX_OUTSET = mat3(
    1.19687900512017,  -0.0980208811401368, -0.0990297440797205,
    -0.0528968517574562, 1.15190312990417,  -0.0989611768448433,
    -0.0529716355144438, -0.0980434501171241, 1.15107367264116);
const float AGX_MIN_EV = -12.47393;
const float AGX_MAX_EV = 4.026069;

// The Rec.709 luminance of AgX's neutral-white response for a very large scene luminance. The scRGB
// path divides by it so AgX's display white becomes exactly 1.0 before the paper-white and highlight
// scaling, which is about 1.7% brighter than the raw M2 AgX white response. This value must match
// AgxToneMap.AGX_OUTPUT_WHITE_LUMINANCE in AgxToneMap.cs.
const float AGX_OUTPUT_WHITE_LUMINANCE = 0.9829795037669735;
const vec3 REC709_LUMINANCE_WEIGHTS = vec3(0.2126, 0.7152, 0.0722);

vec3 AgXDefaultContrastApprox(vec3 x) {
    vec3 x2 = x * x;
    vec3 x4 = x2 * x2;
    return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
}

vec3 AgX(vec3 color) {
    color = AGX_INSET * color;
    color = clamp(log2(max(color, 1e-10)), AGX_MIN_EV, AGX_MAX_EV);
    color = (color - AGX_MIN_EV) / (AGX_MAX_EV - AGX_MIN_EV);
    color = AgXDefaultContrastApprox(color);
    color = AGX_OUTSET * color;
    return max(color, 0.0);
}

float AgxLuminance(vec3 color) {
    return dot(color, REC709_LUMINANCE_WEIGHTS);
}

// Standard piecewise sRGB encode for a swapchain format with no hardware transfer function.
float SrgbEncodeChannel(float channel) {
    return channel <= 0.0031308 ? 12.92 * channel : 1.055 * pow(channel, 1.0 / 2.4) - 0.055;
}

vec3 SrgbEncode(vec3 color) {
    return vec3(SrgbEncodeChannel(color.r), SrgbEncodeChannel(color.g), SrgbEncodeChannel(color.b));
}

void main() {
    vec3 scene = max(texture(uSceneColor, vUv).rgb, vec3(0.0)) * pc.exposureMultiplier;

    if (pc.encoding == 2) {
        vec3 display;
        if (pc.tonemapping == 0) {
            // AgX stays the SDR-referred curve. Normalize its white response to 1.0, then either
            // scale to paper white or, above the knee, extend the scene luminance into the headroom.
            vec3 tone = AgX(scene) / AGX_OUTPUT_WHITE_LUMINANCE;
            float sceneLuminance = AgxLuminance(scene);
            if (pc.hdrHighlight != 0 && pc.hdrPeakScale > pc.hdrWhiteScale
                && sceneLuminance > pc.agxWhiteSceneLuminance) {
                float ratio = sceneLuminance / pc.agxWhiteSceneLuminance;
                float headroom = pc.hdrPeakScale / pc.hdrWhiteScale;
                float progress = clamp((ratio - 1.0) / (headroom - 1.0), 0.0, 1.0);
                float outputLuminance = pc.hdrWhiteScale + (pc.hdrPeakScale - pc.hdrWhiteScale) * progress;
                // Tone the chromaticity by the ratio of the target luminance to the tone-mapped
                // luminance, so the displayed luminance is exact and the channel ratios are kept.
                float displayLuminance = max(AgxLuminance(tone), 1e-5);
                display = tone * (outputLuminance / displayLuminance);
            } else {
                display = tone * pc.hdrWhiteScale;
            }
        } else {
            // Direct HDR authoring: no tone curve and no clamp, so scene values above paper white
            // use the display headroom literally.
            display = scene * pc.hdrWhiteScale;
        }

        outColor = vec4(max(display, 0.0), 1.0);
    } else {
        // SDR encodings keep the M2 transform exactly: raw AgX (or the raw scene for no tone
        // mapping), the optional manual sRGB encode, and a clamp to [0, 1]. The AgX normalization
        // and the highlight extension are scRGB-only.
        vec3 color = pc.tonemapping == 0 ? AgX(scene) : scene;
        if (pc.encoding == 1) {
            color = SrgbEncode(color);
        }

        outColor = vec4(clamp(color, 0.0, 1.0), 1.0);
    }
}
