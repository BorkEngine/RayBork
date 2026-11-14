namespace RayBork.Internal;

/// <summary>
/// The outcome of one glyph generation enqueue request.
/// </summary>
internal enum GlyphEnqueueResult {
    /// <summary>The request was added to the FIFO queue.</summary>
    Queued,

    /// <summary>The pair was already queued or in flight; no second request was added.</summary>
    AlreadyPending,

    /// <summary>The service was never started, so the caller violated the start ordering.</summary>
    RejectedNotStarted,

    /// <summary>The service is stopping or the pipeline is disposed; shutdown already dropped the work.</summary>
    RejectedStopping,
}
