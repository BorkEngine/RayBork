#version 450

// Gentle vignette plus a warm tint. The engine binds sourceColor to the camera target's current
// resolved color, and this stage writes scene-linear HDR; the present pass applies AgX later.

layout(location = 0) in vec2 vUv;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    vec3 scene = texture(sourceColor, vUv).rgb;

    // Normalized distance squared: 0 at the center and 2 at a corner.
    vec2 offset = (vUv * 2.0) - 1.0;
    float falloff = max(1.0 - (uVignette * dot(offset, offset)), 0.0);

    oColor = vec4(scene * uTint.rgb * falloff, 1.0);
}
