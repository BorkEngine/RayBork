#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aUv;
layout(location = 6) in vec4 aColor;

layout(location = 0) out vec2 vUv;
layout(location = 1) out vec4 vColor;

void main_vertex() {
    vec4 world = rbInstance.Transform * vec4(aPosition, 1.0);
    gl_Position = rbFrame.ViewProjection * world;
    // UvRect is the per-instance normalized texture origin (xy) and size (zw). The default
    // (0, 0, 1, 1) reproduces the mesh's authored UVs exactly; a billboard source rectangle
    // selects a normalized sub-rectangle of the same quad.
    vUv = rbInstance.UvRect.xy + (aUv * rbInstance.UvRect.zw);
    vColor = aColor;
}
