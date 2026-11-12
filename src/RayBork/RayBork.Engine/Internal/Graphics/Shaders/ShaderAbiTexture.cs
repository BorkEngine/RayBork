namespace RayBork.Internal;

/// <summary>
/// One texture binding member of the generated per-instance shader record.
/// </summary>
/// <param name="Name">The GLSL sampler name, which is the sidecar texture name.</param>
/// <param name="Slot">The zero-based texture declaration order.</param>
/// <param name="IndexOffset">The absolute byte offset of the <c>uint</c> index slot.</param>
internal readonly record struct ShaderAbiTexture(string Name, int Slot, int IndexOffset);
