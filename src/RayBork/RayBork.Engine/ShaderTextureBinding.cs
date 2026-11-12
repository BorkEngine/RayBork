namespace RayBork;

/// <summary>
/// One baked texture binding of a shader.
/// </summary>
/// <param name="Name">The GLSL sampler name.</param>
/// <param name="Index">The bindless texture slot index reserved for the binding.</param>
public readonly record struct ShaderTextureBinding(string Name, int Index);
