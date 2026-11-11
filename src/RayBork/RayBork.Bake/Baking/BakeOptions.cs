namespace RayBork.Bake.Baking;

/// <summary>The invocation inputs of one bake or check.</summary>
internal sealed class BakeOptions {
    /// <summary>Initializes invocation options.</summary>
    /// <param name="outputRoot">The absolute output root.</param>
    /// <param name="mode">The payload compression mode.</param>
    /// <param name="check">Whether the run writes nothing and always processes every source.</param>
    /// <param name="force">Whether the run bypasses the incremental cache.</param>
    /// <param name="warningsAsErrors">Whether warnings fail the bake.</param>
    internal BakeOptions(
        string outputRoot,
        BakeMode mode,
        bool check,
        bool force,
        bool warningsAsErrors) {
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
