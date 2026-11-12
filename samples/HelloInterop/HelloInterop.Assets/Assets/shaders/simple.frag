#version 450

layout(location = 0) in vec2 inUv;
layout(location = 1) in float inShade;

layout(location = 0) out vec4 outColor;

void main_fragment() {
    outColor = vec4(inUv, inShade, 1.0);
}
