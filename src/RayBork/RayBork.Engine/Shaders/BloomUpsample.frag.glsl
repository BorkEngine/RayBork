#version 450
#define BLOOM_PASS_UPSAMPLE
#include "BloomAbi.glslinc"

// One pyramid upsample: a 4-tap tent over the coarser level added to the finer level with Scatter as
// the coarse contribution. The explicit read-add keeps the post render pass load-op-free instead of
// relying on an additive-blend pipeline.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 oColor;

void main() {
    vec2 texel = 1.0 / vec2(textureSize(bloomCoarse, 0));
    vec3 coarse = texture(bloomCoarse, vUv + (vec2(-1.0, -1.0) * texel)).rgb;
    coarse += texture(bloomCoarse, vUv + (vec2(1.0, -1.0) * texel)).rgb;
    coarse += texture(bloomCoarse, vUv + (vec2(-1.0, 1.0) * texel)).rgb;
    coarse += texture(bloomCoarse, vUv + (vec2(1.0, 1.0) * texel)).rgb;
    coarse *= 0.25;
    vec3 fine = texture(bloomFine, vUv).rgb;
    oColor = vec4(fine + (coarse * max(uScatter, 0.0)), 1.0);
}
