#version 450
#define BLOOM_PASS_COMPOSITE
#include "BloomAbi.glslinc"

// Adds the finished bloom pyramid to the chain input. The output stays scene-linear HDR; the engine
// store-back and the present pass own the target conversion and display tone mapping.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 oColor;

void main() {
    vec4 source = texture(sourceColor, vUv);
    vec3 bloom = texture(bloomTexture, vUv).rgb;
    vec3 result = source.rgb + (uTint.rgb * bloom * max(uIntensity, 0.0));
    oColor = vec4(result, source.a);
}
