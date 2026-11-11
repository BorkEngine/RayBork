using RayBork.Internal;

namespace RayBork.Tests.Assets;

/// <summary>
/// Deterministic, runtime-generated pack inputs shared by the pack tests.
/// </summary>
internal static class PackTestData {
    internal const string DISPLAY_PATH = "fixture.pck";
    internal const string SETTINGS_PATH = "data/settings";
    internal const string MESH_PATH = "models/cube";
    internal const string TEXTURE_PATH = "textures/white";

    internal static byte[] SettingsPayload => [0x81, 0xA2, 0x69, 0x64, 0x2A];

    internal static byte[] Pattern(int length, int seed) {
        byte[] payload = new byte[length];
        new Random(seed).NextBytes(payload);
        return payload;
    }

    internal static byte[] Repeated(int length, byte value) {
        byte[] payload = new byte[length];
        Array.Fill(payload, value);
        return payload;
    }

    internal static PackBuilder CreateStandardBuilder(
        PackCompressionPolicy policy = PackCompressionPolicy.Debug,
        string displayPath = DISPLAY_PATH) {
        var builder = new PackBuilder(displayPath, policy);
        builder.Add(SETTINGS_PATH, AssetEntryType.Data, SettingsPayload, 1);
        builder.Add(MESH_PATH, AssetEntryType.Mesh, Pattern(1000, 7), 16);
        builder.Add(TEXTURE_PATH, AssetEntryType.Texture, Repeated(4096, 0xFE), 256);
        return builder;
    }

    internal static byte[] BuildStandardPack(
        PackCompressionPolicy policy = PackCompressionPolicy.Debug)
        => CreateStandardBuilder(policy).Build();

    internal static byte[] BuildFuzzPack() {
        var builder = new PackBuilder("fuzz.pck", PackCompressionPolicy.Release);
        builder.Add("data/blob", AssetEntryType.Data, Pattern(40, 3), 1);
        builder.Add("meshes/quad", AssetEntryType.Mesh, Repeated(200, 0x7F), 16);
        builder.Add("textures/error", AssetEntryType.Texture, Repeated(512, 0xFF), 64);
        return builder.Build();
    }
}
