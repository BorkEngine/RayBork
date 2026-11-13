#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 6) in vec4 aColor;

layout(location = 0) out vec4 vColor;

void main_vertex() {
    gl_Position = rbFrame.ViewProjection * (rbInstance.Transform * vec4(aPosition, 1.0));
    vColor = aColor * rbInstance.Color;
}
