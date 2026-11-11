namespace RayBork.Bake.Models;

/// <summary>
/// A controlled source-format or geometry failure raised by a model reader.
/// </summary>
/// <remarks>
/// The model processor turns this exception into one <c>RB4009</c> error. It never escapes the
/// processor.
/// </remarks>
internal sealed class ModelReadException : Exception {
    /// <summary>Initializes a model read failure.</summary>
    /// <param name="message">The actionable failure message.</param>
    internal ModelReadException(string message)
        : base(message) {
    }
}
