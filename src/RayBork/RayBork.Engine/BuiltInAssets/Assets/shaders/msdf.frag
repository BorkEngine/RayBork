#version 450

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    vec3 distances = texture(mainTexture, vUv).rgb;
    // An MSDF texel stores three signed distances; their median reconstructs the glyph edge without
    // the artifacts a single channel produces at the corners.
    float distance = max(min(distances.r, distances.g), min(max(distances.r, distances.g), distances.b));
    // The screen-space derivative of the reconstructed distance is the antialiasing band, so the glyph
    // stays sharp at any size without a range or pixel-size uniform. A face-on quad has a zero
    // derivative, so a tiny floor keeps the smoothstep edges distinct.
    float band = max(fwidth(distance), 1e-5);
    float coverage = smoothstep(0.5 - band, 0.5 + band, distance);
    vec4 color = vColor;
    oColor = vec4(color.rgb * coverage, color.a * coverage);
}
