#version 450

// Fullscreen vertex stage for every SSAO pass. The engine quad has local positions in [-0.5, 0.5]
// and top-left UVs; the fragment stages consume the UV and the engine instance index for the
// shader-ABI instance record.

layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aUv;

layout(location = 0) out vec2 vUv;
layout(location = 15) flat out uint rbInstanceIndexOut;

void main() {
    vUv = aUv;
    rbInstanceIndexOut = uint(gl_InstanceIndex);
    gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);
}
