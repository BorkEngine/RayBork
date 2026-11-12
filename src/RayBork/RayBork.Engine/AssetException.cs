namespace RayBork;

/// <summary>
/// The base class for asset pipeline failures that carry a stable diagnostic code.
/// </summary>
/// <remarks>
/// Lifecycle and argument errors use the standard BCL exception types instead. Catch a derived type
/// when the failure category matters, or catch this type for any asset pipeline failure.
/// </remarks>
public class AssetException : RayBorkException {
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetException"/> class.
    /// </summary>
    public AssetException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AssetException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetException"/> class with a message and an
    /// inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public AssetException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal AssetException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
