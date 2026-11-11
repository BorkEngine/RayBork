namespace RayBork;

/// <summary>
/// The base class for engine failures that can carry a stable diagnostic code.
/// </summary>
/// <remarks>
/// Engine failures that need a stable diagnostic code derive from this type. Lifecycle and argument
/// errors use the standard BCL exception types instead.
/// </remarks>
public class RayBorkException : Exception {
    /// <summary>
    /// Initializes a new instance of the <see cref="RayBorkException"/> class.
    /// </summary>
    public RayBorkException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RayBorkException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public RayBorkException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RayBorkException"/> class with a message and an
    /// inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public RayBorkException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal RayBorkException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, innerException) {
        DiagnosticCode = diagnosticCode;
    }

    /// <summary>
    /// Gets the stable <c>RB</c>-prefixed diagnostic code, or <see langword="null"/> when the
    /// exception was created without one.
    /// </summary>
    public string? DiagnosticCode { get; }
}
