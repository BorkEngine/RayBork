namespace RayBork;

/// <summary>
/// Thrown when the graphics device was lost and the renderer could not recreate it. Recovery failure
/// is fatal: the app stops and <see cref="RayBorkApp.Run"/> rethrows this exception.
/// </summary>
public sealed class GraphicsDeviceLostException : GraphicsException {
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsDeviceLostException"/> class.
    /// </summary>
    public GraphicsDeviceLostException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsDeviceLostException"/> class with a
    /// message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GraphicsDeviceLostException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsDeviceLostException"/> class with a
    /// message and an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public GraphicsDeviceLostException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal GraphicsDeviceLostException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
