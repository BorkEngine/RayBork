using RayBork.Bake.Baking;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Paths;
using RayBork.Bake.Toml;
using Tomlyn.Model;

namespace RayBork.Bake.Sources;

/// <summary>Binds a <c>[raybork]</c> data source and runs its semantic validators.</summary>
internal static class BakeDataBinder {
    private const string METADATA_TABLE = "raybork";
    private const string TYPE_KEY = "type";

    /// <summary>Binds one data source.</summary>
    /// <param name="project">The validated project.</param>
    /// <param name="source">The data source.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="entry">The emitted data entry, when successful.</param>
    /// <returns><see langword="true"/> when the source bound and validated.</returns>
    internal static bool TryBind(
        BakeProject project,
        BakeSource source,
        BakeDiagnostics diagnostics,
        out BakedEntry? entry) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        entry = null;
        if (source.Document is null) {
            return false;
        }

        string diagnosticPath = BakePath.ForDiagnostic(project.ProjectRoot, source.FullPath);
        var context = new BakeBindContext(
            source.Document.Root,
            source.Document.Syntax,
            diagnostics,
            diagnosticPath,
            source.BaseAssetPath);

        if (!source.Document.Root.TryGetValue(METADATA_TABLE, out object? metadata)
            || metadata is not TomlTable metadataTable) {
            context.AddError(
                "The data source has no root '[raybork]' table.",
                memberPath: METADATA_TABLE);
            return false;
        }

        if (!metadataTable.TryGetValue(TYPE_KEY, out object? typeValue)
            || typeValue is not string declaredType
            || string.IsNullOrWhiteSpace(declaredType)) {
            context.AddError(
                "The '[raybork]' table must declare a non-empty string 'type' naming a registered "
                    + "CLR type.",
                TomlPath.Root.Child(METADATA_TABLE).Child(TYPE_KEY),
                memberPath: $"{METADATA_TABLE}.{TYPE_KEY}");
            return false;
        }

        if (!project.AssetTypes.TryGetValue(declaredType, out BakeAssetTypeRegistration? registration)) {
            context.AddError(
                $"The data type '{declaredType}' is not registered. Call RegisterAsset<{declaredType}>() "
                    + "in the assets executable.",
                TomlPath.Root.Child(METADATA_TABLE).Child(TYPE_KEY),
                memberPath: $"{METADATA_TABLE}.{TYPE_KEY}");
            return false;
        }

        // The documented TOML example places data keys after the [raybork] header, so they are
        // members of the metadata table in TOML grammar. Root keys before the header are also
        // supported. Both forms merge into one bind root; only "type" is reserved metadata.
        var filteredRoot = new TomlTable();
        foreach (KeyValuePair<string, object> pair in source.Document.Root) {
            if (!string.Equals(pair.Key, METADATA_TABLE, StringComparison.Ordinal)) {
                filteredRoot[pair.Key] = pair.Value;
            }
        }

        foreach (KeyValuePair<string, object> pair in metadataTable) {
            if (string.Equals(pair.Key, TYPE_KEY, StringComparison.Ordinal)) {
                continue;
            }

            if (!filteredRoot.TryAdd(pair.Key, pair.Value)) {
                context.AddError(
                    $"The data key '{pair.Key}' is declared both at the document root and in the "
                        + "'[raybork]' table.",
                    TomlPath.Root.Child(METADATA_TABLE).Child(pair.Key),
                    memberPath: pair.Key);
                return false;
            }

            source.Document.Syntax.AddAlias(
                TomlPath.Root.Child(pair.Key),
                TomlPath.Root.Child(METADATA_TABLE).Child(pair.Key));
        }

        var valueContext = new BakeBindContext(
            filteredRoot,
            source.Document.Syntax,
            diagnostics,
            diagnosticPath,
            source.BaseAssetPath);

        int before = diagnostics.Count;
        object? value;
        try {
            value = registration.Bind(valueContext);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                $"Binding '{source.BaseAssetPath}' through '{declaredType}' failed unexpectedly: "
                    + exception.Message,
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        if (diagnostics.Count != before || value is null) {
            return false;
        }

        var validatorSink = new Processors.BakeProcessorDiagnosticSink(
            registration.Type.Name,
            diagnosticPath,
            source.BaseAssetPath,
            BakeDiagnosticCodes.VALIDATION_FAILED,
            diagnostics);

        foreach (IBakeAssetValidator validator in registration.Validators) {
            try {
                validator.Validate(source.BaseAssetPath, value, validatorSink);
            } catch (Exception exception) when (exception is not OutOfMemoryException) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.VALIDATION_FAILED,
                    $"The validator '{validator.ValidatorType.FullName}' failed for "
                        + $"'{source.BaseAssetPath}': {exception.Message}",
                    diagnosticPath,
                    assetPath: source.BaseAssetPath);
            }
        }

        if (validatorSink.HasError) {
            return false;
        }

        byte[] payload;
        try {
            payload = registration.Serialize(value);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                $"Serializing '{source.BaseAssetPath}' through '{declaredType}' failed: "
                    + exception.Message,
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        entry = new BakedEntry(
            source.BaseAssetPath,
            AssetEntryType.Data,
            payload,
            alignment: 1,
            source.SourceId);
        return true;
    }
}
