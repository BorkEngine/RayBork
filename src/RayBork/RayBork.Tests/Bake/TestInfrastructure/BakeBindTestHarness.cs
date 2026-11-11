using RayBork.Bake;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Toml;

namespace RayBork.Tests.Bake.TestInfrastructure;

/// <summary>Binds a TOML document through the internal shape binder for binder tests.</summary>
internal static class BakeBindTestHarness {
    internal static (object? Value, BakeDiagnostics Diagnostics) Bind<T>(string toml)
        where T : PolyType.IShapeable<T> {
        var diagnostics = new BakeDiagnostics();
        Assert.True(
            TomlDocument.TryParse(toml, "test.toml", diagnostics, out TomlDocument? document),
            string.Join("\n", diagnostics.GetSorted().Select(diagnostic => diagnostic.Format())));
        ArgumentNullException.ThrowIfNull(document);

        var context = new BakeBindContext(
            document.Root,
            document.Syntax,
            diagnostics,
            "test.toml",
            "data/test");
        object? value = TomlShapeBinder.Bind<T>(context);
        return (value, diagnostics);
    }

    internal static string FirstError(BakeDiagnostics diagnostics)
        => diagnostics.GetSorted().FirstOrDefault(diagnostic => diagnostic.Severity == BakeSeverity.Error)
            .Format() ?? string.Empty;
}
