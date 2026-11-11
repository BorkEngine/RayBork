namespace RayBork;

/// <summary>
/// Thrown when SDL3 window creation or another platform window operation fails.
/// </summary>
public sealed class WindowException : RayBorkException {
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowException"/> class.
    /// </summary>
    public WindowException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public WindowException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowException"/> class with a message and an
    /// inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public WindowException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal WindowException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
