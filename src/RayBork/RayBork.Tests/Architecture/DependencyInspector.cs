using System.Xml.Linq;

namespace RayBork.Tests;

internal static class DependencyInspector {
    private const string BAKE_PROJECT_NAME = "RayBork.Bake";

    public static XDocument Load(string path) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return XDocument.Load(path);
    }

    public static IReadOnlyList<string> FindProjectFiles(string rootDirectory) {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);

        List<string> projectFiles = [];
        foreach (string projectFile in Directory.EnumerateFiles(rootDirectory, "*.csproj", SearchOption.AllDirectories)) {
            if (!IsGeneratedPath(projectFile)) {
                projectFiles.Add(projectFile);
            }
        }

        projectFiles.Sort(StringComparer.Ordinal);
        return projectFiles;
    }

    public static IReadOnlyList<string> GetProjectReferenceIncludes(XDocument project) {
        ArgumentNullException.ThrowIfNull(project);

        List<string> includes = [];
        foreach (XElement projectReference in project.Descendants("ProjectReference")) {
            string? include = (string?)projectReference.Attribute("Include");
            if (!string.IsNullOrWhiteSpace(include)) {
                includes.Add(include);
            }
        }

        return includes;
    }

    public static bool HasProjectReferenceTo(XDocument project, string projectName) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(projectName);

        foreach (string projectReference in GetProjectReferenceIncludes(project)) {
            if (IsProjectReferenceTo(projectReference, projectName)) {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> GetBakeProjectReferences(XDocument project) {
        ArgumentNullException.ThrowIfNull(project);

        List<string> bakeReferences = [];
        foreach (string projectReference in GetProjectReferenceIncludes(project)) {
            if (IsProjectReferenceTo(projectReference, BAKE_PROJECT_NAME)) {
                bakeReferences.Add(projectReference);
            }
        }

        return bakeReferences;
    }

    public static IReadOnlyList<string> GetPackageReferenceIds(XDocument project) {
        ArgumentNullException.ThrowIfNull(project);

        List<string> packageIds = [];
        foreach (XElement packageReference in project.Descendants("PackageReference")) {
            string? packageId = (string?)packageReference.Attribute("Include");
            if (!string.IsNullOrWhiteSpace(packageId)) {
                packageIds.Add(packageId);
            }
        }

        return packageIds;
    }

    public static IReadOnlyList<string> GetPackageReferencesWithInlineVersion(XDocument project) {
        ArgumentNullException.ThrowIfNull(project);

        List<string> versionedPackages = [];
        foreach (XElement packageReference in project.Descendants("PackageReference")) {
            string? packageId = (string?)packageReference.Attribute("Include");
            if (string.IsNullOrWhiteSpace(packageId)) {
                continue;
            }
            if (packageReference.Attribute("Version") is not null || packageReference.Element("Version") is not null) {
                versionedPackages.Add(packageId);
            }
        }

        return versionedPackages;
    }

    public static string? GetPackageReferenceMetadataValue(XDocument project, string packageId, string metadataName) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentException.ThrowIfNullOrEmpty(metadataName);

        foreach (XElement packageReference in project.Descendants("PackageReference")) {
            string? include = (string?)packageReference.Attribute("Include");
            if (!string.Equals(include, packageId, StringComparison.Ordinal)) {
                continue;
            }

            string? value = (string?)packageReference.Attribute(metadataName) ?? packageReference.Element(metadataName)?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return null;
    }

    public static string? GetPropertyValue(XDocument project, string propertyName) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        foreach (XElement propertyGroup in project.Descendants("PropertyGroup")) {
            XElement? property = propertyGroup.Element(propertyName);
            if (property is not null) {
                return property.Value.Trim();
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> GetPackageVersionPins(XDocument project) {
        ArgumentNullException.ThrowIfNull(project);

        Dictionary<string, string> pins = new(StringComparer.Ordinal);
        foreach (XElement packageVersion in project.Descendants("PackageVersion")) {
            string? packageId = (string?)packageVersion.Attribute("Include") ?? (string?)packageVersion.Attribute("Update");
            string? version = (string?)packageVersion.Attribute("Version") ?? packageVersion.Element("Version")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(version)) {
                pins[packageId] = version;
            }
        }

        return pins;
    }

    private static bool IsProjectReferenceTo(string projectReference, string projectName) {
        string normalizedReference = projectReference.Replace('\\', '/');
        string? fileName = Path.GetFileNameWithoutExtension(normalizedReference);
        return string.Equals(fileName, projectName, StringComparison.Ordinal);
    }

    private static bool IsGeneratedPath(string path) {
        string[] segments = path.Replace('\\', '/').Split('/');
        foreach (string segment in segments) {
            if (string.Equals(segment, "bin", StringComparison.Ordinal)
                || string.Equals(segment, "obj", StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
