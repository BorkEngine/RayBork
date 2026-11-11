namespace RayBork;

/// <summary>
/// Thrown when the platform layer, including SDL3 initialization or a native library, fails.
/// </summary>
public sealed class PlatformException : RayBorkException {
    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformException"/> class.
    /// </summary>
    public PlatformException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PlatformException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformException"/> class with a message and an
    /// inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public PlatformException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal PlatformException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
