using System.Diagnostics.CodeAnalysis;

namespace RayBork.Bake;

/// <summary>The diagnostic sink for one processor source or semantic validator.</summary>
/// <remarks>
/// A processor error discards that source's outputs; any error fails the pack, and warnings fail
/// under <c>--warnings-as-errors</c>.
/// </remarks>
public interface IAssetProcessorDiagnostics
{
    /// <summary>Reports an error with a stable <c>RBxxxx</c> code.</summary>
    [SuppressMessage(
        "Naming",
        "CA1716",
        Justification = "Error is the deliberate member name of the public bake diagnostics contract and pairs with Warning and Info; renaming it would break every processor."
    )]
    void Error(string code, string message);

    /// <summary>Reports a warning with a stable <c>RBxxxx</c> code.</summary>
    void Warning(string code, string message);

    /// <summary>Reports informational output with a stable <c>RBxxxx</c> code.</summary>
    void Info(string code, string message);
}
