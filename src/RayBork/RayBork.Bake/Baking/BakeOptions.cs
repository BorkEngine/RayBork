namespace RayBork.Bake.Baking;

/// <summary>The invocation inputs of one bake or check.</summary>
internal sealed class BakeOptions
{
    /// <summary>Initializes invocation options.</summary>
    internal BakeOptions(
        string outputRoot,
        BakeMode mode,
        bool check,
        bool force,
        bool warningsAsErrors
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        OutputRoot = outputRoot;
        Mode = mode;
        Check = check;
        Force = force;
        WarningsAsErrors = warningsAsErrors;
    }

    /// <summary>Gets the absolute output root.</summary>
    internal string OutputRoot { get; }

    /// <summary>Gets the payload compression mode.</summary>
    internal BakeMode Mode { get; }

    /// <summary>Gets a value indicating whether the run validates without writing.</summary>
    internal bool Check { get; }

    /// <summary>Gets a value indicating whether the incremental cache is bypassed.</summary>
    internal bool Force { get; }

    /// <summary>Gets a value indicating whether warnings fail the bake.</summary>
    internal bool WarningsAsErrors { get; }
}
