namespace RayBork;

/// <summary>
/// Thrown when a requested asset path is not present in any mounted pack.
/// </summary>
public sealed class AssetNotFoundException : AssetException {
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetNotFoundException"/> class.
    /// </summary>
    public AssetNotFoundException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetNotFoundException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AssetNotFoundException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetNotFoundException"/> class with a message and
    /// an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public AssetNotFoundException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal AssetNotFoundException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
