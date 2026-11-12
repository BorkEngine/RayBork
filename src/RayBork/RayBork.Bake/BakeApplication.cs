using System.Reflection;
using RayBork.Bake.Baking;
using RayBork.Bake.Commands;

namespace RayBork.Bake;

/// <summary>
/// A configured build-time assets executable.
/// </summary>
/// <remarks>
/// A single application may run several commands. Configuration is revalidated per invocation.
/// </remarks>
public sealed class BakeApplication {
    private readonly BakeProjectDefinition _definition;

    /// <summary>Initializes an application from the builder configuration.</summary>
    /// <param name="definition">The builder configuration.</param>
    internal BakeApplication(BakeProjectDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }

    /// <summary>Runs one command against the process standard streams.</summary>
    /// <param name="args">The command-line arguments, excluding the executable name.</param>
    /// <returns>The documented process exit code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    public Task<int> RunAsync(string[] args) {
        ArgumentNullException.ThrowIfNull(args);
        return RunAsync(args, Console.Out, Console.Error, EntryAssemblyName());
    }

    /// <summary>Runs one command against explicit writers and a CLI prefix.</summary>
    /// <param name="args">The command-line arguments, excluding the executable name.</param>
    /// <param name="output">The summary stream.</param>
    /// <param name="error">The diagnostic stream.</param>
    /// <param name="programName">The CLI prefix.</param>
    /// <returns>The documented process exit code.</returns>
    internal Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        string programName) {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(programName);

        return Task.FromResult(
            BakeCommandLine.Run(_definition, args, output, error, programName));
    }

    private static string EntryAssemblyName()
        => Assembly.GetEntryAssembly()?.GetName().Name ?? "raybork-bake";
}
