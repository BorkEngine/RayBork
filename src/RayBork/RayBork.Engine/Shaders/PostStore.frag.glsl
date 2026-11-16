#version 450

// Store-back pass for the camera post-process chain: copies the final single-sample Rgba16Float post
// target into the camera target's color attachment. The render pass owns the attachment format, so a
// Rgba8Unorm or Rgba8Srgb target converts in the fixed-function write path. There is no tone mapping
// and no gamma here; the post chain is scene-linear HDR and the present pass applies AgX later.
//
// The chain is target-sized, while the store-back covers only the camera viewport. uvRect selects the
// matching sub-rectangle of the post target: xy is the normalized origin and zw the normalized size.
//
// This source is compiled to PostStore.frag.spv by scripts/compile-shaders.ps1 and the committed blob
// is embedded in RayBork.Engine. The runtime never compiles shaders.

layout(set = 0, binding = 0) uniform sampler2D uSourceColor;

layout(push_constant) uniform StoreBackConstants {
    vec4 uvRect;
} pc;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main() {
    vec2 uv = pc.uvRect.xy + (vUv * pc.uvRect.zw);
    outColor = texture(uSourceColor, uv);
}
