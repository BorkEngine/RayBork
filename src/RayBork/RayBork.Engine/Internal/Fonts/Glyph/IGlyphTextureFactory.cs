namespace RayBork.Internal;

/// <summary>Creates the GPU texture of one generated MSDF glyph bitmap.</summary>
/// <remarks>
/// Called only on the <c>RayBork.Font</c> thread; the task must complete on a thread that never
/// waits for <c>RayBork.Font</c> and must honor the stop token. The caller owns the returned
/// texture, and a factory must not cache or dispose it.
/// </remarks>
internal interface IGlyphTextureFactory
{
    /// <summary>Creates one RGBA8 glyph texture from a generated MSDF bitmap.</summary>
    /// <param name="width">The padded bitmap width in px.</param>
    /// <param name="height">The padded bitmap height in px.</param>
    /// <param name="rgba">The row-major RGBA8 bitmap, exactly <c>width × height × 4</c> bytes.</param>
    /// <param name="cancellationToken">The generation service stop token.</param>
    /// <returns>The uploaded texture, owned by the caller.</returns>
    Task<Texture> CreateAsync(
        int width,
        int height,
        ReadOnlyMemory<byte> rgba,
        CancellationToken cancellationToken
    );
}
