namespace RayBork;

/// <summary>
/// Thrown when a pack is missing, truncated, has a bad magic or version, fails an integrity hash, or
/// has an invalid index.
/// </summary>
/// <remarks>
/// A pack passed to <c>RayBorkAppBuilder.WithAssetsPack</c> is validated when the app starts.
/// A corrupt entry inside an otherwise valid pack is an <see cref="AssetLoadException"/> for that
/// path instead.
/// </remarks>
public sealed class AssetPackException : AssetException {
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetPackException"/> class.
    /// </summary>
    public AssetPackException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetPackException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AssetPackException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetPackException"/> class with a message and an
    /// inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public AssetPackException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal AssetPackException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
