namespace RayBork.Internal;

/// <summary>
/// Creates the GPU texture of one generated MSDF glyph bitmap.
/// </summary>
/// <remarks>
/// <para>
/// The glyph generation service calls this interface only on the <c>RayBork.Font</c> thread. The call
/// may complete asynchronously, as the production implementation uploads through the procedural asset
/// service and the render thread; the service blocks the generation thread on the returned task so one
/// glyph is uploaded at a time. The task must therefore complete on a thread that never waits for
/// <c>RayBork.Font</c>; the production path is the GPU upload bridge, which the render thread
/// completes. It must also honor the cancellation token, because
/// <see cref="GlyphGenerationService.Stop"/> cancels an in-flight upload and then joins the font thread
/// without a bounded wait.
/// </para>
/// <para>
/// The returned texture is owned by the caller: the pipeline holds one owner reference and releases it
/// when it evicts the glyph or disposes. A factory must not cache or dispose the texture it returns.
/// </para>
/// </remarks>
internal interface IGlyphTextureFactory
{
    /// <summary>
    /// Creates one RGBA8 glyph texture from a generated MSDF bitmap.
    /// </summary>
    /// <param name="width">The bitmap width in pixels; it is the padded texture width.</param>
    /// <param name="height">The bitmap height in pixels; it is the padded texture height.</param>
    /// <param name="rgba">
    /// The row-major RGBA8 MSDF bitmap, exactly <c>width × height × 4</c> bytes.
    /// </param>
    /// <param name="cancellationToken">The generation service stop token.</param>
    /// <returns>The uploaded texture, owned by the caller.</returns>
    Task<Texture> CreateAsync(
        int width,
        int height,
        ReadOnlyMemory<byte> rgba,
        CancellationToken cancellationToken
    );
}
