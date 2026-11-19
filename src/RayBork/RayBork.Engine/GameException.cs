namespace RayBork;

/// <summary>Thrown when a game callback throws; the inner exception is the game's.</summary>
public sealed class GameException : RayBorkException
{
    /// <summary>Initializes a new instance of <see cref="GameException"/>.</summary>
    public GameException() { }

    /// <summary>Initializes a new instance of <see cref="GameException"/>.</summary>
    public GameException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="GameException"/>.</summary>
    public GameException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal GameException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) { }
}
