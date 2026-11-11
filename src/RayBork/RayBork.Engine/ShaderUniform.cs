namespace RayBork;

/// <summary>
/// One reflected shader uniform.
/// </summary>
/// <param name="Name">The GLSL uniform name.</param>
/// <param name="Type">The reflected value type.</param>
public readonly record struct ShaderUniform(string Name, ShaderUniformType Type);
