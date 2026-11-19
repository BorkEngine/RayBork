namespace RayBork;

/// <summary>Thrown when the graphics backend fails unrecoverably.</summary>
public class GraphicsException : RayBorkException
{
    /// <summary>Initializes a new instance of <see cref="GraphicsException"/>.</summary>
    public GraphicsException() { }

    /// <summary>Initializes a new instance of <see cref="GraphicsException"/>.</summary>
    public GraphicsException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="GraphicsException"/>.</summary>
    public GraphicsException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal GraphicsException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) { }
}
