using System.Reflection;
using System.Xml.Linq;

namespace RayBork.Tests;

public sealed class ArchitectureTests {
    private const string ENGINE_PROJECT_NAME = "RayBork.Engine";
    private const string BAKE_PROJECT_NAME = "RayBork.Bake";
    private const string TESTS_PROJECT_NAME = "RayBork.Tests";
    private const string SOURCE_ROOT = "src/RayBork";
    private const string ENGINE_PROJECT_PATH = "src/RayBork/RayBork.Engine/RayBork.Engine.csproj";
    private const string BAKE_PROJECT_PATH = "src/RayBork/RayBork.Bake/RayBork.Bake.csproj";
    private const string TESTS_PROJECT_PATH = "src/RayBork/RayBork.Tests/RayBork.Tests.csproj";
    private const string HELLO_INTEROP_PROJECT_PATH = "samples/HelloInterop/HelloInterop.csproj";
    private const string DIRECTORY_PACKAGES_PATH = "Directory.Packages.props";
    private const string BUILD_PROPS_PATH = "Directory.Build.props";
    private const string NUGET_CONFIG_PATH = "NuGet.config";
    private const string PUBLIC_API_SHIPPED_PATH = "src/RayBork/RayBork.Engine/PublicAPI.Shipped.txt";
    private const string PUBLIC_API_UNSHIPPED_PATH = "src/RayBork/RayBork.Engine/PublicAPI.Unshipped.txt";

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EngineDoesNotReferenceBake() {
        Assembly engineAssembly = typeof(EngineVersion).Assembly;
        string[] referencedAssemblyNames = [.. engineAssembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)];
        Assert.DoesNotContain(BAKE_PROJECT_NAME, referencedAssemblyNames);

        XDocument engineProject = DependencyInspector.Load(RepositoryLayout.PathOf(ENGINE_PROJECT_PATH));
        Assert.Empty(DependencyInspector.GetBakeProjectReferences(engineProject));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RuntimeProjectsDoNotReferenceBake() {
        string[] runtimeProjectPaths = [.. DependencyInspector.FindProjectFiles(RepositoryLayout.PathOf(SOURCE_ROOT)).Where(IsRuntimeProject)];
        Assert.NotEmpty(runtimeProjectPaths);

        foreach (string runtimeProjectPath in runtimeProjectPaths) {
            XDocument runtimeProject = DependencyInspector.Load(runtimeProjectPath);
            Assert.Empty(DependencyInspector.GetBakeProjectReferences(runtimeProject));
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BakeReferencesEngineAndTestsReferenceBoth() {
        XDocument bakeProject = DependencyInspector.Load(RepositoryLayout.PathOf(BAKE_PROJECT_PATH));
        XDocument testsProject = DependencyInspector.Load(RepositoryLayout.PathOf(TESTS_PROJECT_PATH));
        XDocument helloInteropProject = DependencyInspector.Load(RepositoryLayout.PathOf(HELLO_INTEROP_PROJECT_PATH));

        Assert.True(DependencyInspector.HasProjectReferenceTo(bakeProject, ENGINE_PROJECT_NAME));
        Assert.True(DependencyInspector.HasProjectReferenceTo(testsProject, ENGINE_PROJECT_NAME));
        Assert.True(DependencyInspector.HasProjectReferenceTo(testsProject, BAKE_PROJECT_NAME));
        Assert.True(DependencyInspector.HasProjectReferenceTo(helloInteropProject, ENGINE_PROJECT_NAME));
        Assert.False(DependencyInspector.HasProjectReferenceTo(helloInteropProject, BAKE_PROJECT_NAME));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EnginePackagesMatchDependencyMatrix() {
        string[] expectedPackageIds = [
            "Microsoft.CodeAnalysis.PublicApiAnalyzers",
            "Microsoft.Extensions.Logging",
            "Nerdbank.MessagePack",
            "PolyType",
            "SDL3-CS",
            "SDL3-CS.Native",
            "Silk.NET.Vulkan",
            "System.IO.Hashing",
            "ZLogger",
        ];
        Dictionary<string, string> expectedPins = new(StringComparer.Ordinal) {
            ["BCnEncoder.Net"] = "2.3.0",
            ["Microsoft.CodeAnalysis.PublicApiAnalyzers"] = "5.6.0",
            ["Microsoft.Extensions.Logging"] = "11.0.0",
            ["Microsoft.NET.Test.Sdk"] = "18.11.1",
            ["Moq"] = "4.21.0",
            ["Nerdbank.MessagePack"] = "1.4.104",
            ["PolyType"] = "1.4.1",
            ["SDL3-CS"] = "3.4.20",
            ["SDL3-CS.Native"] = "3.4.2",
            ["SharpGLTF.Core"] = "1.0.7",
            ["Silk.NET.Shaderc"] = "2.23.0",
            ["Silk.NET.Shaderc.Native"] = "2.23.0",
            ["Silk.NET.Vulkan"] = "2.23.0",
            ["StbImageSharp"] = "2.30.16",
            ["System.IO.Hashing"] = "11.0.0",
            ["Tomlyn"] = "2.11.1",
            ["xunit.runner.visualstudio"] = "3.1.5",
            ["xunit.v3"] = "3.2.2",
            ["ZLogger"] = "2.6.1",
        };

        XDocument engineProject = DependencyInspector.Load(RepositoryLayout.PathOf(ENGINE_PROJECT_PATH));
        string expectedPackageList = string.Join(", ", expectedPackageIds.Order(StringComparer.Ordinal));
        string actualPackageList = string.Join(", ", DependencyInspector.GetPackageReferenceIds(engineProject).Order(StringComparer.Ordinal));
        Assert.Equal(expectedPackageList, actualPackageList);
        Assert.Empty(DependencyInspector.GetPackageReferencesWithInlineVersion(engineProject));

        XDocument centralPackages = DependencyInspector.Load(RepositoryLayout.PathOf(DIRECTORY_PACKAGES_PATH));
        string? managePackageVersionsCentrally = DependencyInspector.GetPropertyValue(centralPackages, "ManagePackageVersionsCentrally");
        Assert.True(
            string.Equals(managePackageVersionsCentrally, "true", StringComparison.OrdinalIgnoreCase),
            $"Expected central package management but found '{managePackageVersionsCentrally ?? "<missing>"}'.");

        IReadOnlyDictionary<string, string> actualPins = DependencyInspector.GetPackageVersionPins(centralPackages);
        Assert.Equal(expectedPins.Count, actualPins.Count);
        foreach ((string packageId, string expectedVersion) in expectedPins) {
            Assert.True(actualPins.TryGetValue(packageId, out string? actualVersion), $"Package '{packageId}' is not pinned.");
            Assert.Equal(expectedVersion, actualVersion);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RestoreUsesPinnedNuGetSource() {
        XDocument nugetConfig = DependencyInspector.Load(RepositoryLayout.PathOf(NUGET_CONFIG_PATH));
        XElement? packageSources = nugetConfig.Root?.Element("packageSources");
        Assert.NotNull(packageSources);
        XElement sources = packageSources!;

        Assert.Single(sources.Elements("clear"));
        XElement[] sourceAdds = [.. sources.Elements("add")];
        Assert.Single(sourceAdds);
        Assert.Equal("nuget.org", (string?)sourceAdds[0].Attribute("key"));
        Assert.Equal("https://api.nuget.org/v3/index.json", (string?)sourceAdds[0].Attribute("value"));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EnginePublicSurfaceExposesNoBackendTypes() {
        Assembly engineAssembly = typeof(EngineVersion).Assembly;
        IReadOnlyList<string> violations = PublicSurfaceInspector.FindViolations(engineAssembly);

        string message = "Engine public surface exposes backend types:" + Environment.NewLine + string.Join(Environment.NewLine, violations);
        Assert.True(violations.Count == 0, message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EngineHasNoPublicMaterialType() {
        Type[] exportedTypes = typeof(EngineVersion).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exportedTypes, exportedType => string.Equals(exportedType.Name, "Material", StringComparison.Ordinal));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EngineIsAotCompatible() {
        XDocument engineProject = DependencyInspector.Load(RepositoryLayout.PathOf(ENGINE_PROJECT_PATH));

        AssertProperty(engineProject, "IsAotCompatible", "true");
        AssertProperty(engineProject, "GenerateDocumentationFile", "true");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublicApiAnalyzerIsWired() {
        XDocument engineProject = DependencyInspector.Load(RepositoryLayout.PathOf(ENGINE_PROJECT_PATH));
        IReadOnlyList<string> packageIds = DependencyInspector.GetPackageReferenceIds(engineProject);
        Assert.Contains("Microsoft.CodeAnalysis.PublicApiAnalyzers", packageIds);

        string? privateAssets = DependencyInspector.GetPackageReferenceMetadataValue(
            engineProject,
            "Microsoft.CodeAnalysis.PublicApiAnalyzers",
            "PrivateAssets");
        Assert.True(
            string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase),
            $"Expected PublicApiAnalyzers PrivateAssets 'all' but found '{privateAssets ?? "<missing>"}'.");

        string shippedPath = RepositoryLayout.PathOf(PUBLIC_API_SHIPPED_PATH);
        string unshippedPath = RepositoryLayout.PathOf(PUBLIC_API_UNSHIPPED_PATH);
        Assert.True(File.Exists(shippedPath), $"Missing public API file '{PUBLIC_API_SHIPPED_PATH}'.");
        Assert.True(File.Exists(unshippedPath), $"Missing public API file '{PUBLIC_API_UNSHIPPED_PATH}'.");
        Assert.Equal("#nullable enable", ReadFirstLine(shippedPath));
        Assert.Equal("#nullable enable", ReadFirstLine(unshippedPath));

        string[] unshippedLines = File.ReadAllLines(unshippedPath);
        Assert.Contains("RayBork.EngineVersion", unshippedLines);
        Assert.Contains("static RayBork.EngineVersion.Current.get -> System.Version!", unshippedLines);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildBaselineIsEnforced() {
        XDocument buildProps = DependencyInspector.Load(RepositoryLayout.PathOf(BUILD_PROPS_PATH));

        AssertProperty(buildProps, "TargetFramework", "net11.0");
        AssertProperty(buildProps, "Version", "0.1.0");
        AssertProperty(buildProps, "Nullable", "enable");
        AssertProperty(buildProps, "ImplicitUsings", "enable");
        AssertProperty(buildProps, "TreatWarningsAsErrors", "true");
        AssertProperty(buildProps, "Deterministic", "true");
        AssertProperty(buildProps, "EnableNETAnalyzers", "true");
        AssertProperty(buildProps, "EnforceCodeStyleInBuild", "true");
    }

    private static bool IsRuntimeProject(string projectPath) {
        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        return !string.Equals(projectName, TESTS_PROJECT_NAME, StringComparison.Ordinal)
            && !string.Equals(projectName, BAKE_PROJECT_NAME, StringComparison.Ordinal);
    }

    private static void AssertProperty(XDocument project, string propertyName, string expectedValue) {
        string? actualValue = DependencyInspector.GetPropertyValue(project, propertyName);
        Assert.True(
            string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase),
            $"Expected MSBuild property '{propertyName}' to be '{expectedValue}' but found '{actualValue ?? "<missing>"}'.");
    }

    private static string ReadFirstLine(string path) {
        string? firstLine = File.ReadLines(path).FirstOrDefault();
        return firstLine ?? string.Empty;
    }
}
