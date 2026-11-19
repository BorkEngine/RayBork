namespace RayBork;

/// <summary>Thrown when the graphics device was lost and could not be recreated.</summary>
public sealed class GraphicsDeviceLostException : GraphicsException
{
    /// <summary>Initializes a new instance of <see cref="GraphicsDeviceLostException"/>.</summary>
    public GraphicsDeviceLostException() { }

    /// <summary>Initializes a new instance of <see cref="GraphicsDeviceLostException"/>.</summary>
    public GraphicsDeviceLostException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="GraphicsDeviceLostException"/>.</summary>
    public GraphicsDeviceLostException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal GraphicsDeviceLostException(
        string message,
        string? diagnosticCode,
        Exception? innerException
    )
        : base(message, diagnosticCode, innerException) { }
}
