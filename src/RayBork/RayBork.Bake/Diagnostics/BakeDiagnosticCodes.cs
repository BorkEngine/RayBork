namespace RayBork.Bake.Diagnostics;

/// <summary>
/// The stable diagnostic codes used by the bake tooling.
/// </summary>
internal static class BakeDiagnosticCodes {
    /// <summary>The builder configuration or a registered processor is invalid.</summary>
    internal const string CONFIGURATION_INVALID = "RB4001";

    /// <summary>A TOML source, sidecar, or bound value is invalid.</summary>
    internal const string TOML_INVALID = "RB4002";

    /// <summary>A source path, discovery rule, or canonical path is invalid.</summary>
    internal const string SOURCE_INVALID = "RB4003";

    /// <summary>A pack could not be laid out or written.</summary>
    internal const string PACK_WRITE_FAILED = "RB4004";

    /// <summary>The incremental manifest or cache state is invalid.</summary>
    internal const string INCREMENTAL_FAILED = "RB4005";

    /// <summary>A processor failed while processing a source.</summary>
    internal const string PROCESSOR_FAILED = "RB4006";

    /// <summary>A semantic asset validator reported an error.</summary>
    internal const string VALIDATION_FAILED = "RB4012";
}
