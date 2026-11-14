#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aUv;

layout(location = 0) out vec2 vUv;
layout(location = 1) out vec4 vColor;

void main_vertex() {
    gl_Position = rbFrame.ViewProjection * (rbInstance.Transform * vec4(aPosition, 1.0));
    // The glyph quad's own UVs sample its per-instance glyph texture; the per-instance texture index
    // makes mainTexture resolve to this instance's texture.
    vUv = aUv;
    vColor = rbInstance.Color;
}
