namespace RayBork.Tests.Assets;

/// <summary>
/// A fresh temporary directory for pack file tests, removed on disposal.
/// </summary>
internal sealed class TempPackDirectory : IDisposable {
    internal TempPackDirectory() {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "raybork-pack-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    internal string DirectoryPath { get; }

    internal string GetPath(string fileName) => Path.Combine(DirectoryPath, fileName);

    public void Dispose() {
        try {
            if (Directory.Exists(DirectoryPath)) {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}
