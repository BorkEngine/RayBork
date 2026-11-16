#version 450

// Fullscreen post-process vertex stage. The engine quad has positions in [-0.5, 0.5] and UVs in
// [0, 1] with (0, 0) at the image top-left, so it covers clip space after the * 2.0 scale.

layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aUv;

layout(location = 0) out vec2 vUv;

void main_vertex() {
    vUv = aUv;
    gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);
}
