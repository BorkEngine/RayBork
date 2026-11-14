#version 450

// Present pass: samples the resolved linear HDR scene color, applies exposure and the configured
// tone mapping, and writes the display-referred value.
//
// This source is compiled to Present.frag.spv by scripts/compile-shaders.ps1 and the committed
// blob is embedded in RayBork.Engine. The runtime never compiles shaders.
//
// The AgX implementation and the allenwp curve are derived from Godot 4.7's tonemap.glsl (MIT): the
// combined Rec.709 to Rec.2020 + AgX inset matrix, the combined inverse outset matrix, and the curve
// are ported from it. The AgX base transform follows EaryChow's AgX LUT generator that Blender uses
// (https://github.com/EaryChow/AgX_LUT_Gen), and the curve is allenwp's tonemapping curve
// (https://allenwp.com/blog/2025/05/29/allenwp-tonemapping-curve/). HDR10/PQ presentation is
// deliberately deferred: it would be another value of `pc.encoding` instead of a change to the
// tone-mapping path below.

layout(set = 0, binding = 0) uniform sampler2D uSceneColor;

layout(push_constant) uniform PresentConstants {
    float exposureMultiplier;  // 0  = pow(2.0, exposureEv), computed on the CPU
    float hdrWhiteScale;       // 4  = paper white in scRGB units; 1.0 on the SDR paths
    float outputMaxValue;      // 8  = 1.0 SDR, or the HDR headroom (peak / paper white)
    float awpContrast;         // 12 = allenwp curve contrast
    float awpToeA;             // 16 = allenwp toe intersection parameter
    float awpSlope;            // 20 = allenwp shoulder slope at the crossover
    float awpW;                // 24 = allenwp shoulder width
    int tonemapping;           // 28 = 0 AgX, 1 none
    int encoding;              // 32 = 0 linear [0,1], 1 manual sRGB encode, 2 scRGB HDR linear
    int reserved;              // 36 = 0, keeps the block 40 bytes
} pc;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

// Combined Rec.709 to Rec.2020 and Blender AgX inset matrices, in GLSL mat3 column-major constructor
// order. All coefficients are positive, so a non-negative scene color stays non-negative.
const mat3 REC709_TO_REC2020_AGX_INSET = mat3(
    0.544814746488245, 0.140416948464053, 0.0888104196149096,
    0.373787398372697, 0.754137554567394, 0.178871756420858,
    0.0813978551390581, 0.105445496968552, 0.732317823964232);

// Combined inverse AgX outset matrix and Rec.2020 to Rec.709 matrices, in column-major order.
const mat3 AGX_OUTSET_REC2020_TO_REC709 = mat3(
    1.96488741169489, -0.299313364904742, -0.164352742528393,
    -0.855988495690215, 1.32639796461980, -0.238183969428088,
    -0.108898916004672, -0.0270845997150571, 1.40253671195648);

// 18% "middle gray" is perceptually 50% of the brightness of reference white. The toe below this
// point and the Reinhard-like shoulder above it meet with matching value and slope.
const float AWP_CROSSOVER_POINT = 0.18;

// allenwp tonemapping curve. The toe is a power function through the crossover point, and the
// shoulder reaches outputMaxValue at the AgX white. The parameters are derived on the CPU so the
// shader and AgxToneMap use the same curve.
vec3 AllenwpCurve(vec3 x) {
    float shoulderMax = pc.outputMaxValue - AWP_CROSSOVER_POINT;
    vec3 s = x - vec3(AWP_CROSSOVER_POINT);
    vec3 slopeS = pc.awpSlope * s;
    s = slopeS * (1.0 + (s / pc.awpW)) / (1.0 + (slopeS / shoulderMax));
    s += vec3(AWP_CROSSOVER_POINT);

    vec3 t = pow(x, vec3(pc.awpContrast));
    t = t / (t + vec3(pc.awpToeA));

    return mix(s, t, lessThan(x, vec3(AWP_CROSSOVER_POINT)));
}

vec3 AgX(vec3 color) {
    color = REC709_TO_REC2020_AGX_INSET * color;
    color = AllenwpCurve(color);
    // Clipping to the output maximum is required to address a cyan colour that occurs with very
    // bright inputs.
    color = min(vec3(pc.outputMaxValue), color);
    color = AGX_OUTSET_REC2020_TO_REC709 * color;
    return color;
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
        // scRGB HDR: AgX uses the display headroom as its output maximum, so the curve output is in
        // paper-white units up to the peak. Scale it to scRGB paper white and keep the headroom
        // unclamped so the tone curve, not the encoder, sets the peak.
        vec3 display = pc.tonemapping == 0 ? AgX(scene) : scene;
        outColor = vec4(max(display * pc.hdrWhiteScale, 0.0), 1.0);
    } else {
        // SDR encodings: AgX with an output maximum of 1.0, the optional manual sRGB encode, and a
        // clamp to [0, 1]. The None path skips the curve but keeps the same clamp and encoding.
        vec3 color = pc.tonemapping == 0 ? AgX(scene) : scene;
        if (pc.encoding == 1) {
            color = SrgbEncode(color);
        }

        outColor = vec4(clamp(color, 0.0, 1.0), 1.0);
    }
}
