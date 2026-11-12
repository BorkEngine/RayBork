#version 450

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec3 vWorldPosition;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    // One directional key light and a small height-tinted ambient fill keep the castle readable
    // without a lighting system.
    vec3 lightDirection = normalize(vec3(0.35, 0.8, 0.45));
    vec3 lightColor = vec3(1.0, 0.96, 0.9);

    vec4 albedo = texture(mainTexture, vUv);
    vec3 normal = normalize(vNormal);
    float diffuse = max(dot(normal, lightDirection), 0.0);

    float height = clamp(vWorldPosition.y / 8.0, 0.0, 1.0);
    vec3 ambient = mix(vec3(0.12, 0.14, 0.18), vec3(0.2, 0.22, 0.26), height);

    vec3 light = ambient + (lightColor * diffuse);
    vec3 linear = albedo.rgb * light * uTint.rgb * rbInstance.Color.rgb;
    oColor = vec4(linear, albedo.a * uTint.a * rbInstance.Color.a);
}
