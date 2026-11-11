#version 450

// Fullscreen triangle generated from gl_VertexIndex. There is no vertex buffer and no push
// constant, so the present pass draws three vertices and covers the whole viewport.
//
// Vulkan's framebuffer origin is top-left and clip-space Y points down, so clip-space y = -1 is
// the top edge of the image. The interpolated vUv is therefore (0,0) at the image top-left and
// (1,1) at the image bottom-right. The engine projection owns the Y-flip; this shader must not
// flip again.

layout(location = 0) out vec2 vUv;

void main() {
    // Vertex indices 0, 1, and 2 produce corners (0,0), (2,0), and (0,2). Scaled to clip space
    // they become (-1,-1), (3,-1), and (-1,3), a triangle that covers the viewport.
    vec2 corner = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
    gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
    vUv = corner;
}
