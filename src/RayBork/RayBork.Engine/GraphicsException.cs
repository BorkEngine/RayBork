namespace RayBork;

/// <summary>
/// Thrown when the graphics backend fails in a way the app cannot recover from, such as device
/// creation, an unrecoverable device loss, or a pipeline or resource allocation failure.
/// </summary>
public class GraphicsException : RayBorkException {
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsException"/> class.
    /// </summary>
    public GraphicsException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GraphicsException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsException"/> class with a message and an
    /// inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public GraphicsException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal GraphicsException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
