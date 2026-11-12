#version 450

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec4 vColor;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    oColor = texture(mainTexture, vUv) * vColor * uTint * rbInstance.Color;
}
