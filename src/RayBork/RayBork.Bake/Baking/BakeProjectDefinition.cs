using PolyType;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Processors;

namespace RayBork.Bake.Baking;

/// <summary>
/// The mutable builder-side configuration model that the bake application validates into a
/// <see cref="BakeProject"/>.
/// </summary>
internal sealed class BakeProjectDefinition {
    private readonly List<BakePackDefinition> _packs = [];
    private readonly Dictionary<string, BakeAssetTypeRegistration> _assetTypes =
        new(StringComparer.Ordinal);
    private readonly List<IAssetProcessor> _processors = [];
    private readonly List<BakeDiagnostic> _issues = [];
    private bool _defaultAssetsUsed;
    private bool _defaultProcessorsUsed;

    /// <summary>Gets the configured project root, or <see langword="null"/> when unset.</summary>
    internal string? ProjectRoot { get; private set; }

    /// <summary>Gets the configured packs in order.</summary>
    internal IReadOnlyList<BakePackDefinition> Packs => _packs;

    /// <summary>Gets the registered data types keyed by full type name.</summary>
    internal IReadOnlyDictionary<string, BakeAssetTypeRegistration> AssetTypes => _assetTypes;

    /// <summary>Gets the registered processors in registration order.</summary>
    internal IReadOnlyList<IAssetProcessor> Processors => _processors;

    /// <summary>Gets the texture defaults.</summary>
    internal RayBork.TextureFilter TextureFilter { get; private set; } = RayBork.TextureFilter.Linear;

    /// <summary>Gets the texture wrap default.</summary>
    internal RayBork.TextureWrap TextureWrap { get; private set; } = RayBork.TextureWrap.Repeat;

    /// <summary>Gets the builder-side configuration issues.</summary>
    internal IReadOnlyList<BakeDiagnostic> Issues => _issues;

    /// <summary>Gets the test-only tool version override, or <see langword="null"/>.</summary>
    internal string? ToolVersionOverride { get; private set; }

    /// <summary>Sets the base for relative roots, pack paths, and diagnostics.</summary>
    /// <param name="path">The project root path.</param>
    internal void SetProjectRoot(string path) => ProjectRoot = path;

    /// <summary>Adds one configured pack.</summary>
    /// <param name="packPath">The output-root-relative pack path.</param>
    /// <param name="root">The project-root-relative asset root.</param>
    internal void AddPack(string packPath, string root)
        => _packs.Add(new BakePackDefinition(packPath, root));

    /// <summary>Registers one explicit data type.</summary>
    /// <typeparam name="T">The shapeable data type.</typeparam>
    internal void AddAsset<T>()
        where T : IShapeable<T> {
        BakeAssetTypeRegistration candidate = BakeAssetTypeRegistration.Create<T>();
        if (_assetTypes.TryAdd(candidate.TypeName, candidate)) {
            return;
        }

        AddIssue(
            BakeDiagnosticCodes.CONFIGURATION_INVALID,
            $"The data type '{candidate.TypeName}' is registered more than once; call "
                + "RegisterAsset<T>() exactly once per type.");
    }

    /// <summary>Registers one explicit data type and appends a validator.</summary>
    /// <typeparam name="T">The shapeable data type.</typeparam>
    /// <param name="validator">The validator.</param>
    internal void AddAssetValidator<T>(IAssetValidator<T> validator)
        where T : IShapeable<T> {
        ArgumentNullException.ThrowIfNull(validator);
        GetOrAddAsset<T>().AddValidator(validator);
    }

    /// <summary>Registers the engine authoring descriptor types; idempotent.</summary>
    internal void UseDefaultAssets() {
        if (_defaultAssetsUsed) {
            return;
        }

        _defaultAssetsUsed = true;
        GetOrAddAsset<RayBork.Internal.TextureAssetDescriptor>();
        GetOrAddAsset<RayBork.Internal.MeshAssetDescriptor>();
        GetOrAddAsset<RayBork.Internal.ModelAssetDescriptor>();
        GetOrAddAsset<RayBork.Internal.ModelPartAsset>();
        GetOrAddAsset<RayBork.Internal.ShaderAssetDescriptor>();
        GetOrAddAsset<RayBork.Internal.ShaderUniformAsset>();
        GetOrAddAsset<RayBork.Internal.FontAssetDescriptor>();
    }

    /// <summary>Requests the built-in processors; idempotent.</summary>
    internal void RequestDefaultProcessors() {
        if (_defaultProcessorsUsed) {
            return;
        }

        _defaultProcessorsUsed = true;
        BuiltInProcessors.AddTo(this);
    }

    /// <summary>Adds one processor.</summary>
    /// <param name="processor">The processor.</param>
    internal void AddProcessor(IAssetProcessor processor) {
        ArgumentNullException.ThrowIfNull(processor);
        _processors.Add(processor);
    }

    /// <summary>Sets the texture defaults.</summary>
    /// <param name="filter">The default filter.</param>
    /// <param name="wrap">The default wrap mode.</param>
    internal void SetTextureDefaults(RayBork.TextureFilter filter, RayBork.TextureWrap wrap) {
        if (filter is not RayBork.TextureFilter.Nearest
            and not RayBork.TextureFilter.Linear
            and not RayBork.TextureFilter.Anisotropic) {
            throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown texture filter.");
        }

        if (wrap is not RayBork.TextureWrap.Repeat
            and not RayBork.TextureWrap.Clamp
            and not RayBork.TextureWrap.Mirror) {
            throw new ArgumentOutOfRangeException(nameof(wrap), wrap, "Unknown texture wrap mode.");
        }

        TextureFilter = filter;
        TextureWrap = wrap;
    }

    /// <summary>Sets the test-only tool version override.</summary>
    /// <param name="version">The version to record and compare in the manifest.</param>
    internal void SetToolVersionOverride(string version) {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ToolVersionOverride = version;
    }

    /// <summary>Records one semantic configuration issue.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message.</param>
    internal void AddIssue(string code, string message)
        => _issues.Add(new BakeDiagnostic(
            BakeSeverity.Error,
            code,
            message,
            FilePath: null,
            Line: null,
            Column: null,
            AssetPath: null,
            MemberPath: null));

    private BakeAssetTypeRegistration GetOrAddAsset<T>()
        where T : IShapeable<T> {
        BakeAssetTypeRegistration candidate = BakeAssetTypeRegistration.Create<T>();
        if (_assetTypes.TryGetValue(candidate.TypeName, out BakeAssetTypeRegistration? existing)) {
            return existing;
        }

        _assetTypes.Add(candidate.TypeName, candidate);
        return candidate;
    }
}
