namespace RayBork.Internal;

/// <summary>
/// One reflected uniform member of the generated per-instance shader record.
/// </summary>
/// <param name="Name">The GLSL field name, which is the sidecar uniform name.</param>
/// <param name="Type">The reflected uniform type.</param>
/// <param name="Offset">The absolute byte offset from the start of the instance record.</param>
/// <param name="Size">The member size in bytes, excluding trailing alignment padding.</param>
/// <param name="ComponentCount">The number of 32-bit components in the value.</param>
internal readonly record struct ShaderAbiUniform(
    string Name,
    ShaderUniformType Type,
    int Offset,
    int Size,
    int ComponentCount);
