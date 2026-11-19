namespace RayBork;

/// <summary>Thrown when SDL3 initialization or a native library fails.</summary>
public sealed class PlatformException : RayBorkException
{
    /// <summary>Initializes a new instance of <see cref="PlatformException"/>.</summary>
    public PlatformException() { }

    /// <summary>Initializes a new instance of <see cref="PlatformException"/>.</summary>
    public PlatformException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="PlatformException"/>.</summary>
    public PlatformException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal PlatformException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, diagnosticCode, innerException) { }
}
