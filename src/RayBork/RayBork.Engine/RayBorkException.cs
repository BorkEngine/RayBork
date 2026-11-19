namespace RayBork;

/// <summary>The base class for engine exceptions with a stable diagnostic code.</summary>
/// <remarks>Lifecycle and argument errors use the standard BCL exception types.</remarks>
public class RayBorkException : Exception
{
    /// <summary>Initializes a new instance of <see cref="RayBorkException"/>.</summary>
    public RayBorkException() { }

    /// <summary>Initializes a new instance of <see cref="RayBorkException"/>.</summary>
    public RayBorkException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="RayBorkException"/>.</summary>
    public RayBorkException(string message, Exception? innerException)
        : base(message, innerException) { }

    internal RayBorkException(string message, string? diagnosticCode, Exception? innerException)
        : base(message, innerException)
    {
        DiagnosticCode = diagnosticCode;
    }

    /// <summary>Gets the stable <c>RB</c>-prefixed diagnostic code, if any.</summary>
    public string? DiagnosticCode { get; }
}
