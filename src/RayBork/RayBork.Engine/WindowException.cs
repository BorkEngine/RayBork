namespace RayBork;

/// <summary>Thrown when window creation or another window operation fails.</summary>
public sealed class WindowException : RayBorkException
{
    /// <summary>Initializes a new instance of <see cref="WindowException"/>.</summary>
    public WindowException() { }

    /// <summary>Initializes a new instance of <see cref="WindowException"/>.</summary>
    public WindowException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="WindowException"/>.</summary>
    public WindowException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal WindowException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) { }
}
