using System.Reflection;
using RayBork.Bake.Baking;
using RayBork.Bake.Commands;

namespace RayBork.Bake;

/// <summary>A configured build-time assets executable.</summary>
/// <remarks>Configuration is revalidated on every invocation.</remarks>
public sealed class BakeApplication
{
    private readonly BakeProjectDefinition _definition;

    /// <summary>Initializes an application from the builder configuration.</summary>
    internal BakeApplication(BakeProjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }

    /// <summary>Runs one command against standard streams and returns its exit code.</summary>
    /// <param name="args">The command-line arguments, excluding the executable name.</param>
    public Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return RunAsync(args, Console.Out, Console.Error, EntryAssemblyName());
    }

    /// <summary>Runs one command against explicit writers.</summary>
    internal Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        string programName
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(programName);

        return Task.FromResult(BakeCommandLine.Run(_definition, args, output, error, programName));
    }

    private static string EntryAssemblyName() =>
        Assembly.GetEntryAssembly()?.GetName().Name ?? "raybork-bake";
}
