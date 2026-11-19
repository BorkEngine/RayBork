namespace RayBork;

/// <summary>A baked texture binding of a shader.</summary>
/// <param name="Name">The GLSL sampler name.</param>
/// <param name="Index">The reserved bindless texture slot.</param>
public readonly record struct ShaderTextureBinding(string Name, int Index);
