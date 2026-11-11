#version 450

// Present pass: samples the resolved linear HDR scene color, applies exposure and the configured
// tone mapping, and writes the display-referred value.
//
// This source is compiled to Present.frag.spv by scripts/compile-shaders.ps1 and the committed
// blob is embedded in RayBork.Engine. The runtime never compiles shaders.
//
// The SDR paths write display-referred [0,1]. The scRGB HDR path writes linear extended-sRGB
// values where 1.0 is the SDR white point scaled by the window's SDL SDR white level, so values
// above that use the display headroom. HDR10/PQ presentation is deliberately deferred: it would be
// another value of `pc.encoding` instead of a change to the tone-mapping path below.

layout(set = 0, binding = 0) uniform sampler2D uSceneColor;

layout(push_constant) uniform PresentConstants {
    float exposureMultiplier; // pow(2.0, exposureEv), computed on the CPU
    float hdrWhiteScale;      // SDL SDR white level for scRGB; 1.0 on the SDR paths
    int tonemapping;          // 0 = AgX, 1 = none
    int encoding;             // 0 = linear [0,1], 1 = manual sRGB encode, 2 = scRGB HDR linear
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

// Standard piecewise sRGB encode for a swapchain format with no hardware transfer function.
float SrgbEncodeChannel(float channel) {
    return channel <= 0.0031308 ? 12.92 * channel : 1.055 * pow(channel, 1.0 / 2.4) - 0.055;
}

vec3 SrgbEncode(vec3 color) {
    return vec3(SrgbEncodeChannel(color.r), SrgbEncodeChannel(color.g), SrgbEncodeChannel(color.b));
}

void main() {
    vec3 color = max(texture(uSceneColor, vUv).rgb, vec3(0.0));
    color *= pc.exposureMultiplier;

    if (pc.tonemapping == 0) {
        color = AgX(color);
    }

    // Encoding 0 leaves the display value linear so the sRGB swapchain transfer function, the
    // scRGB float surface, or a linear offscreen target can handle it. Encoding 1 writes sRGB
    // bytes, which is required by an UNORM swapchain format. Encoding 2 is the scRGB HDR path: the
    // display-linear value is scaled into extended-sRGB units using the window's SDR white level and
    // is deliberately not clamped, so values above the SDR white point use the display headroom.
    if (pc.encoding == 2) {
        outColor = vec4(max(color, 0.0) * pc.hdrWhiteScale, 1.0);
    } else {
        if (pc.encoding == 1) {
            color = SrgbEncode(color);
        }

        outColor = vec4(clamp(color, 0.0, 1.0), 1.0);
    }
}
