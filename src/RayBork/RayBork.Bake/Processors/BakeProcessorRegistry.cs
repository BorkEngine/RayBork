using RayBork.Bake.Diagnostics;

namespace RayBork.Bake.Processors;

/// <summary>
/// The immutable extension-to-processor registry and raw fallback selected by the builder.
/// </summary>
internal sealed class BakeProcessorRegistry {
    private const string FALLBACK_EXTENSION = "*";
    private const string RESERVED_EXTENSION = "toml";

    private readonly Dictionary<string, BakeProcessorRegistration> _byExtension;
    private readonly string[] _extensionsByMatchLength;
    private readonly BakeProcessorRegistration[] _processors;

    private BakeProcessorRegistry(
        Dictionary<string, BakeProcessorRegistration> byExtension,
        BakeProcessorRegistration? fallback,
        BakeProcessorRegistration[] processors) {
        _byExtension = byExtension;
        Fallback = fallback;
        _processors = processors;
        _extensionsByMatchLength = [.. byExtension.Keys
            .OrderByDescending(extension => extension.Length)
            .ThenBy(extension => extension, StringComparer.Ordinal)];
    }

    /// <summary>Gets the processor registered for unclaimed files, or <see langword="null"/>.</summary>
    internal BakeProcessorRegistration? Fallback { get; }

    /// <summary>Gets the registrations in builder order.</summary>
    internal IReadOnlyList<BakeProcessorRegistration> Processors => _processors;

    /// <summary>Validates the processor declarations and builds the registry.</summary>
    /// <param name="processors">The processors in builder order.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="registry">The registry when every declaration is valid.</param>
    /// <returns><see langword="true"/> when the registry is valid.</returns>
    internal static bool TryCreate(
        IReadOnlyList<IAssetProcessor> processors,
        BakeDiagnostics diagnostics,
        out BakeProcessorRegistry? registry) {
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(diagnostics);

        registry = null;
        var byExtension = new Dictionary<string, BakeProcessorRegistration>(StringComparer.Ordinal);
        var registrations = new List<BakeProcessorRegistration>(processors.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        BakeProcessorRegistration? fallback = null;
        bool valid = true;

        foreach (IAssetProcessor? processor in processors) {
            if (processor is null) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    "A processor registration is null.");
                valid = false;
                continue;
            }

            var registration = new BakeProcessorRegistration(processor);
            registrations.Add(registration);
            string display = string.IsNullOrWhiteSpace(registration.Name)
                ? registration.ProcessorType.FullName ?? "<unnamed processor>"
                : registration.Name;

            if (string.IsNullOrWhiteSpace(registration.Name)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The processor '{display}' has no name.");
                valid = false;
            } else if (!names.Add(registration.Name)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The processor name '{registration.Name}' is registered more than once; a "
                        + "processor Name is the sidecar table name and must be unique.");
                valid = false;
            }

            if (registration.Policy is not SidecarPolicy.None
                and not SidecarPolicy.Optional
                and not SidecarPolicy.Required) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The processor '{display}' has an unknown sidecar policy '{registration.Policy}'.");
                valid = false;
            } else if (registration.Policy == SidecarPolicy.None
                && registration.SidecarShape is not null) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The processor '{display}' has SidecarPolicy.None but exposes a sidecar shape.");
                valid = false;
            } else if (registration.Policy != SidecarPolicy.None
                && registration.SidecarShape is null) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The processor '{display}' declares a sidecar policy but exposes no sidecar shape.");
                valid = false;
            } else if (registration.Policy != SidecarPolicy.None
                && registration.Processor is not ITypedAssetProcessor) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The processor '{display}' declares a sidecar policy but does not derive from "
                        + "AssetProcessor<TSidecar>.");
                valid = false;
            }

            if (registration.Extensions is null || registration.Extensions.Count == 0) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The processor '{display}' registers no extensions and is not a raw fallback.");
                valid = false;
                continue;
            }

            var ownExtensions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? declared in registration.Extensions) {
                if (!TryNormalizeExtension(declared, out string extension, out string? reason)) {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.CONFIGURATION_INVALID,
                        $"The processor '{display}' declares the invalid extension '{declared}': "
                            + $"{reason}.");
                    valid = false;
                    continue;
                }

                if (!ownExtensions.Add(extension)) {
                    continue;
                }

                if (extension == FALLBACK_EXTENSION) {
                    if (fallback is not null) {
                        diagnostics.Add(
                            BakeSeverity.Info,
                            BakeDiagnosticCodes.CONFIGURATION_INVALID,
                            $"The raw fallback '{display}' replaces '{fallback.Name}'.");
                    }

                    fallback = registration;
                    continue;
                }

                if (byExtension.TryGetValue(extension, out BakeProcessorRegistration? existing)) {
                    diagnostics.Add(
                        BakeSeverity.Info,
                        BakeDiagnosticCodes.CONFIGURATION_INVALID,
                        $"The extension '{extension}' is overridden: '{display}' replaces "
                            + $"'{existing.Name}'.");
                }

                byExtension[extension] = registration;
            }
        }

        if (!valid) {
            return false;
        }

        registry = new BakeProcessorRegistry(byExtension, fallback, [.. registrations]);
        return true;
    }

    /// <summary>Finds the processor for a non-TOML source by its longest registered suffix.</summary>
    /// <param name="relativePath">The pack-root-relative path.</param>
    /// <param name="match">The matched processor and normalized extension.</param>
    /// <returns><see langword="true"/> when a registered extension matches.</returns>
    internal bool TryMatch(string relativePath, out BakeProcessorMatch match) {
        if (!string.IsNullOrEmpty(relativePath)) {
            foreach (string extension in _extensionsByMatchLength) {
                int suffixLength = extension.Length + 1;
                if (relativePath.Length >= suffixLength
                    && relativePath[relativePath.Length - suffixLength] == '.'
                    && relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    match = new BakeProcessorMatch(_byExtension[extension], extension);
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    private static bool TryNormalizeExtension(
        string? declared,
        out string extension,
        out string? reason) {
        extension = string.Empty;
        if (string.IsNullOrWhiteSpace(declared)) {
            reason = "the extension is empty";
            return false;
        }

        string normalized = declared.Trim();
        if (normalized[0] == '.') {
            normalized = normalized[1..];
        }

        normalized = normalized.ToLowerInvariant();
        if (normalized.Length == 0) {
            reason = "the extension is empty after removing the leading dot";
            return false;
        }

        if (string.Equals(normalized, RESERVED_EXTENSION, StringComparison.Ordinal)) {
            reason = "the plain 'toml' extension is reserved for data sources and sidecars";
            return false;
        }

        foreach (char character in normalized) {
            if (char.IsWhiteSpace(character)) {
                reason = "the extension contains whitespace";
                return false;
            }

            if (character is '/' or '\\' or '?' or ':') {
                reason = $"the extension contains the unsupported character '{character}'";
                return false;
            }

            if (character == '*' && normalized != FALLBACK_EXTENSION) {
                reason = "the '*' extension is reserved for the raw fallback";
                return false;
            }
        }

        if (normalized[0] == '.' || normalized[^1] == '.' || normalized.Contains("..", StringComparison.Ordinal)) {
            reason = "the extension has an empty '.' segment";
            return false;
        }

        extension = normalized;
        reason = null;
        return true;
    }
}
