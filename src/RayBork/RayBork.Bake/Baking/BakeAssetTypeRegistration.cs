using PolyType;
using RayBork.Bake.Toml;

namespace RayBork.Bake.Baking;

/// <summary>
/// One registered data type: its compiled shape binding, serialization delegate, and validators.
/// </summary>
internal sealed class BakeAssetTypeRegistration {
    private readonly Func<BakeBindContext, object?> _bind;
    private readonly Func<object, byte[]> _serialize;
    private readonly List<IBakeAssetValidator> _validators = [];

    private BakeAssetTypeRegistration(
        Type type,
        string typeName,
        Func<BakeBindContext, object?> bind,
        Func<object, byte[]> serialize) {
        Type = type;
        TypeName = typeName;
        _bind = bind;
        _serialize = serialize;
    }

    /// <summary>Gets the registered CLR type.</summary>
    internal Type Type { get; }

    /// <summary>Gets the type's full name used by <c>[raybork].type</c>.</summary>
    internal string TypeName { get; }

    /// <summary>Gets the semantic validators in registration order.</summary>
    internal IReadOnlyList<IBakeAssetValidator> Validators => _validators;

    /// <summary>Binds a parsed document into the registered type.</summary>
    /// <param name="context">The binding context.</param>
    /// <returns>The bound value, or a default value when a diagnostic was reported.</returns>
    internal object? Bind(BakeBindContext context) => _bind(context);

    /// <summary>Serializes a bound value to MessagePack bytes.</summary>
    /// <param name="value">The bound value.</param>
    /// <returns>The payload bytes.</returns>
    internal byte[] Serialize(object value) => _serialize(value);

    /// <summary>Appends one semantic validator.</summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="validator">The validator.</param>
    internal void AddValidator<T>(IAssetValidator<T> validator)
        where T : IShapeable<T>
        => _validators.Add(new BakeAssetValidatorRegistration<T>(validator));

    /// <summary>Creates the registration for a compiled-shape type.</summary>
    /// <typeparam name="T">The shapeable type.</typeparam>
    /// <returns>The registration.</returns>
    internal static BakeAssetTypeRegistration Create<T>()
        where T : IShapeable<T> {
        string typeName = typeof(T).FullName
            ?? throw new InvalidOperationException($"The registered type '{typeof(T)}' has no full name.");

        return new BakeAssetTypeRegistration(
            typeof(T),
            typeName,
            context => TomlShapeBinder.Bind<T>(context),
            value => RayBork.Internal.AssetSerialization.Serialize((T)value));
    }
}

/// <summary>The non-generic seam over one semantic validator.</summary>
internal interface IBakeAssetValidator {
    /// <summary>Gets the validator CLR type for diagnostics.</summary>
    Type ValidatorType { get; }

    /// <summary>Validates a bound value.</summary>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <param name="asset">The bound value.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    void Validate(string assetPath, object asset, IAssetProcessorDiagnostics diagnostics);
}

/// <summary>The typed registration over one public validator.</summary>
/// <typeparam name="T">The validated data type.</typeparam>
internal sealed class BakeAssetValidatorRegistration<T> : IBakeAssetValidator
    where T : IShapeable<T> {
    private readonly IAssetValidator<T> _validator;

    /// <summary>Initializes the validator registration.</summary>
    /// <param name="validator">The public validator.</param>
    internal BakeAssetValidatorRegistration(IAssetValidator<T> validator) {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
        ValidatorType = validator.GetType();
    }

    /// <inheritdoc/>
    public Type ValidatorType { get; }

    /// <inheritdoc/>
    public void Validate(string assetPath, object asset, IAssetProcessorDiagnostics diagnostics)
        => _validator.Validate(assetPath, (T)asset, diagnostics);
}
