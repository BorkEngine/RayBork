#version 450

layout(location = 0) in vec2 vUv;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    // raylib's default model shader is unlit: albedo times the tint and the per-instance color.
    oColor = texture(mainTexture, vUv) * uTint * rbInstance.Color;
}
