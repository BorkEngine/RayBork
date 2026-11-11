using System.Xml.Linq;

namespace RayBork.Tests;

public sealed class DependencyInspectorTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BakeProjectReferenceIsFlagged() {
        XDocument project = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
                <ItemGroup>
                    <ProjectReference Include="../RayBork.Bake/RayBork.Bake.csproj" />
                </ItemGroup>
            </Project>
            """);

        IReadOnlyList<string> bakeReferences = DependencyInspector.GetBakeProjectReferences(project);

        Assert.Single(bakeReferences);
        Assert.Contains("../RayBork.Bake/RayBork.Bake.csproj", bakeReferences);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InlinePackageVersionIsFlagged() {
        XDocument project = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
                <ItemGroup>
                    <PackageReference Include="PolyType" Version="1.4.1" />
                    <PackageReference Include="Nerdbank.MessagePack" />
                </ItemGroup>
            </Project>
            """);

        IReadOnlyList<string> versionedPackages = DependencyInspector.GetPackageReferencesWithInlineVersion(project);

        Assert.Single(versionedPackages);
        Assert.Equal("PolyType", versionedPackages[0]);
    }
}
