#version 450
#define BLOOM_PASS_PREFILTER
#include "BloomAbi.glslinc"

// Threshold and soft-knee prefilter of the fast dual-filter bloom pyramid. The first pass writes the
// half-resolution level; brightness below Threshold - knee is discarded and the knee blends the
// discarded range in smoothly. Threshold 0 keeps every lit pixel.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 oColor;

void main() {
    vec3 color = texture(bloomSource, vUv).rgb;
    float brightness = max(color.r, max(color.g, color.b));
    float threshold = max(uThreshold, 0.0);
    float knee = max(threshold * max(uSoftKnee, 0.0), 1e-5);
    float soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee);
    float contribution = max(soft, brightness - threshold) / max(brightness, 1e-5);
    oColor = vec4(color * max(contribution, 0.0), 1.0);
}
