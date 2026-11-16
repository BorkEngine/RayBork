namespace RayBork.Internal;

/// <summary>
/// The fixed sample counts and built-in shader paths of each <see cref="SsaoQuality"/> variant.
/// </summary>
/// <remarks>
/// The mapping is the N8AO 2.0 standard-denoise preset table. The shader path selects a committed
/// SPIR-V module with a compile-time sample count, so no quality setting creates a dynamic loop or a
/// runtime shader variant.
/// </remarks>
internal static class SsaoQualityInfo {
    internal static int AoSampleCount(SsaoQuality quality) => quality switch {
        SsaoQuality.Performance => 8,
        SsaoQuality.Low => 16,
        SsaoQuality.Medium => 16,
        SsaoQuality.High => 64,
        SsaoQuality.Ultra => 64,
        _ => throw new ArgumentOutOfRangeException(
            nameof(quality),
            quality,
            "The SSAO quality is not a defined value."),
    };

    internal static int DenoiseSampleCount(SsaoQuality quality) => quality switch {
        SsaoQuality.Performance => 4,
        SsaoQuality.Low => 4,
        SsaoQuality.Medium => 8,
        SsaoQuality.High => 8,
        SsaoQuality.Ultra => 16,
        _ => throw new ArgumentOutOfRangeException(
            nameof(quality),
            quality,
            "The SSAO quality is not a defined value."),
    };

    internal static string AoShaderPath(SsaoQuality quality) => quality switch {
        SsaoQuality.Performance => SsaoShaderContract.AO_8_PATH,
        SsaoQuality.Low or SsaoQuality.Medium => SsaoShaderContract.AO_16_PATH,
        SsaoQuality.High or SsaoQuality.Ultra => SsaoShaderContract.AO_64_PATH,
        _ => throw new ArgumentOutOfRangeException(
            nameof(quality),
            quality,
            "The SSAO quality is not a defined value."),
    };

    internal static string DenoiseShaderPath(SsaoQuality quality) => quality switch {
        SsaoQuality.Performance or SsaoQuality.Low => SsaoShaderContract.DENOISE_4_PATH,
        SsaoQuality.Medium or SsaoQuality.High => SsaoShaderContract.DENOISE_8_PATH,
        SsaoQuality.Ultra => SsaoShaderContract.DENOISE_16_PATH,
        _ => throw new ArgumentOutOfRangeException(
            nameof(quality),
            quality,
            "The SSAO quality is not a defined value."),
    };
}
