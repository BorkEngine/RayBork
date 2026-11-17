#version 450
#define BLOOM_PASS_DOWNSAMPLE
#include "BloomAbi.glslinc"

// One pyramid downsample: a fixed 3x3 tent over the finer level. The bilinear sampler already averages
// a 2x2 footprint at each tap, so the result is smooth without a dynamic loop.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 oColor;

void main() {
    vec2 texel = 1.0 / vec2(textureSize(bloomSource, 0));
    vec3 color = texture(bloomSource, vUv).rgb * 4.0;
    color += texture(bloomSource, vUv + (vec2(-1.0, 0.0) * texel)).rgb * 2.0;
    color += texture(bloomSource, vUv + (vec2(1.0, 0.0) * texel)).rgb * 2.0;
    color += texture(bloomSource, vUv + (vec2(0.0, -1.0) * texel)).rgb * 2.0;
    color += texture(bloomSource, vUv + (vec2(0.0, 1.0) * texel)).rgb * 2.0;
    color += texture(bloomSource, vUv + (vec2(-1.0, -1.0) * texel)).rgb;
    color += texture(bloomSource, vUv + (vec2(1.0, -1.0) * texel)).rgb;
    color += texture(bloomSource, vUv + (vec2(-1.0, 1.0) * texel)).rgb;
    color += texture(bloomSource, vUv + (vec2(1.0, 1.0) * texel)).rgb;
    oColor = vec4(color * (1.0 / 16.0), 1.0);
}
