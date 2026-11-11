using RayBork.Bake;

namespace RayBork.Tests.Bake.TestInfrastructure;

/// <summary>A temporary project, asset root, and output root for one bake test.</summary>
internal sealed class TempBakeWorkspace : IDisposable {
    internal TempBakeWorkspace() {
        Root = Path.Combine(
            Path.GetTempPath(),
            "raybork-bake-tests",
            Guid.NewGuid().ToString("N"));
        ProjectRoot = Path.Combine(Root, "project");
        AssetRoot = Path.Combine(ProjectRoot, "Assets");
        OutputRoot = Path.Combine(Root, "output");
        Directory.CreateDirectory(AssetRoot);
        Directory.CreateDirectory(OutputRoot);
    }

    internal string Root { get; }

    internal string ProjectRoot { get; }

    internal string AssetRoot { get; }

    internal string OutputRoot { get; }

    internal string ManifestPath => Path.Combine(
        ProjectRoot,
        "obj",
        "raybork",
        "bake",
        "manifest.json");

    internal string GetPackPath(string fileName = "assets.pck")
        => Path.Combine(OutputRoot, fileName);

    internal string WriteAsset(string relativePath, string content)
        => WriteFile(Path.Combine(AssetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)), content);

    internal string WriteAssetBytes(string relativePath, byte[] content)
        => WriteFileBytes(
            Path.Combine(AssetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            content);

    internal string ReadAsset(string relativePath)
        => File.ReadAllText(
            Path.Combine(AssetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    internal string WriteFile(string fullPath, string content) => WriteFileBytes(
        fullPath,
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));

    internal string WriteFileBytes(string fullPath, byte[] content) {
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    internal BakeApplicationBuilder Builder() => new BakeApplicationBuilder()
        .WithProjectRoot(ProjectRoot)
        .WithPack("assets.pck", "Assets");

    internal BakeRunResult Run(BakeApplication application, params string[] args) {
        string[] effectiveArgs = args.Length == 0
            ? ["bake", "--output-root", OutputRoot]
            : args;
        return RunRaw(application, effectiveArgs);
    }

    internal BakeRunResult RunRaw(BakeApplication application, params string[] args) {
        var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        int exitCode = application
            .RunAsync(args, output, error, "test-bake")
            .GetAwaiter()
            .GetResult();
        return new BakeRunResult(exitCode, output.ToString(), error.ToString());
    }

    public void Dispose() {
        try {
            if (Directory.Exists(Root)) {
                Directory.Delete(Root, recursive: true);
            }
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}

/// <summary>The observable outcome of one test bake invocation.</summary>
internal sealed record BakeRunResult(int ExitCode, string Output, string Error);
