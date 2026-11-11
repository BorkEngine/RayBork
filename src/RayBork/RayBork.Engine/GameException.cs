namespace RayBork;

/// <summary>
/// Thrown when a game callback throws. The <see cref="Exception.InnerException"/> is the game's
/// exception.
/// </summary>
public sealed class GameException : RayBorkException {
    /// <summary>
    /// Initializes a new instance of the <see cref="GameException"/> class.
    /// </summary>
    public GameException() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GameException(string message) : base(message) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameException"/> class with a message and an inner
    /// exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public GameException(string message, Exception? innerException) : base(message, innerException) {
    }

    internal GameException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) {
    }
}
