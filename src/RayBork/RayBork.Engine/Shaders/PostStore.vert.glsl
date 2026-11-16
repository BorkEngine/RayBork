#version 450

// Store-back fullscreen triangle for the camera post-process chain. The triangle is generated from
// gl_VertexIndex and samples the final single-sample post target; the fragment output is written into
// the camera target's color attachment and resolved exactly like any other camera-pass color output.
//
// Vulkan's framebuffer origin is top-left and clip-space Y points down, so clip-space y = -1 is the
// top edge of the image. The interpolated vUv is therefore (0,0) at the image top-left and (1,1) at
// the image bottom-right, matching the post target's top-left UV origin.

layout(location = 0) out vec2 vUv;

void main() {
    vec2 corner = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
    gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
    vUv = corner;
}
