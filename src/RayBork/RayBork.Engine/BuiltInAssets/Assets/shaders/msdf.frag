#version 450

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    vec3 distances = texture(mainTexture, vUv).rgb;
    // A glyph texture stores a normalized distance field: 0.5 is the glyph edge, the authored distance
    // range maps to [0, 1], and a value above 0.5 is inside the glyph. An MSDF texel stores three signed
    // distances; their median reconstructs the edge without the artifacts a single channel produces at
    // the corners.
    float distance = max(min(distances.r, distances.g), min(max(distances.r, distances.g), distances.b));
    // Screen-space derivatives give the antialiasing band without a range or pixel-size uniform: half of
    // the per-pixel distance gradient puts the two smoothstep edges half a pixel either side of the
    // crossing, so the coverage ramp fits inside one pixel. A constant texel has no gradient, so a tiny
    // floor keeps the smoothstep edges distinct.
    float band = max(0.5 * fwidth(distance), 1e-5);
    float coverage = smoothstep(0.5 - band, 0.5 + band, distance);
    vec4 color = vColor;
    // The alpha blend multiplies the source color by the source alpha, so coverage scales alpha only.
    // Scaling the rgb as well would apply coverage twice and darken every glyph edge.
    oColor = vec4(color.rgb, color.a * coverage);
}
