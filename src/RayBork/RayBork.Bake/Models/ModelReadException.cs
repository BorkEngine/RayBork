namespace RayBork.Bake.Models;

/// <summary>A controlled source-format or geometry failure raised by a model reader.</summary>
/// <remarks>The model processor turns this exception into one <c>RB4009</c> error.</remarks>
internal sealed class ModelReadException : Exception
{
    /// <summary>Initializes a model read failure.</summary>
    internal ModelReadException(string message)
        : base(message) { }
}
