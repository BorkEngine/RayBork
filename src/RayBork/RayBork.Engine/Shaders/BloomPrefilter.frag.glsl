#version 450
#define BLOOM_PASS_PREFILTER
#include "BloomAbi.glslinc"

// Threshold and soft-knee prefilter of the fast dual-filter bloom pyramid. The first pass writes the
// half-resolution level. The knee starts at Threshold - effectiveKnee and ramps up to
// Threshold + effectiveKnee; the effective knee is capped at Threshold so the ramp never extends below
// zero and near-black pixels are not bloomed. Threshold 0 keeps every lit pixel and leaves black black.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 oColor;

void main() {
    vec3 color = texture(bloomSource, vUv).rgb;
    float brightness = max(color.r, max(color.g, color.b));
    if (brightness <= 0.0) {
        oColor = vec4(0.0);
        return;
    }

    float threshold = max(uThreshold, 0.0);
    float knee = clamp(threshold * max(uSoftKnee, 0.0), 1e-5, max(threshold, 1e-5));
    float offset = (brightness - threshold) / knee;
    float ramp = clamp((offset * 0.5) + 0.5, 0.0, 1.0);
    float soft = ramp * ramp * knee;
    float contribution = clamp(
        max(soft, brightness - threshold) / max(brightness, 1e-5),
        0.0,
        1.0);
    oColor = vec4(color * contribution, 1.0);
}
