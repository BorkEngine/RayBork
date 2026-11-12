#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;

layout(location = 0) out vec2 outUv;
layout(location = 1) out float outShade;

void main_vertex() {
    outUv = inUv;
    outShade = 0.5 + (0.5 * inNormal.y);
    gl_Position = vec4(inPosition, 1.0);
}
