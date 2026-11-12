#version 450

// Unlit model shader matching raylib's default model pipeline: the OBJ is drawn with its authored
// coordinates (the sidecar's front = "-z" is the identity orientation) and the albedo texture.
layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aUv;

layout(location = 0) out vec2 vUv;

void main_vertex() {
    gl_Position = rbFrame.ViewProjection * rbInstance.Transform * vec4(aPosition, 1.0);
    vUv = aUv;
}
