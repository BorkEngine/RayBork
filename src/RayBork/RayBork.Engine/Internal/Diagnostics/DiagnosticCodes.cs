namespace RayBork.Internal;

/// <summary>
/// Stable diagnostic codes for engine exceptions. Codes are stable within a major version.
/// </summary>
internal static class DiagnosticCodes {
    /// <summary>
    /// A game callback threw and the app stopped with <see cref="GameException"/>.
    /// </summary>
    public const string GAME_CALLBACK_FAILED = "RB0001";

    /// <summary>
    /// Platform initialization failed.
    /// </summary>
    public const string PLATFORM_INITIALIZATION_FAILED = "RB2001";

    /// <summary>
    /// Window creation failed.
    /// </summary>
    public const string WINDOW_CREATION_FAILED = "RB2002";

    /// <summary>
    /// SDL3 Vulkan surface creation or loader failure.
    /// </summary>
    public const string VULKAN_SURFACE_CREATION_FAILED = "RB2003";

    /// <summary>
    /// Graphics initialization or device creation failed.
    /// </summary>
    public const string GRAPHICS_INITIALIZATION_FAILED = "RB3001";

    /// <summary>
    /// The graphics device was lost after recovery failed.
    /// </summary>
    public const string GRAPHICS_DEVICE_LOST = "RB3002";

    /// <summary>
    /// An unrecoverable graphics failure stopped the app.
    /// </summary>
    public const string GRAPHICS_UNRECOVERABLE = "RB3003";

    /// <summary>
    /// A pack could not be opened or is truncated.
    /// </summary>
    public const string ASSET_PACK_READ_FAILED = "RB1001";

    /// <summary>
    /// A pack has an unsupported magic or version.
    /// </summary>
    public const string ASSET_PACK_UNSUPPORTED_VERSION = "RB1002";

    /// <summary>
    /// A pack index is structurally invalid.
    /// </summary>
    public const string ASSET_PACK_INVALID_INDEX = "RB1003";

    /// <summary>
    /// The pack index hash does not match its bytes.
    /// </summary>
    public const string ASSET_PACK_INDEX_HASH_MISMATCH = "RB1004";

    /// <summary>
    /// A payload hash does not match its bytes.
    /// </summary>
    public const string ASSET_PACK_PAYLOAD_HASH_MISMATCH = "RB1005";

    /// <summary>
    /// The requested asset path is not in any mounted pack.
    /// </summary>
    public const string ASSET_NOT_FOUND = "RB1006";

    /// <summary>
    /// The requested asset path was baked as a different type.
    /// </summary>
    public const string ASSET_TYPE_MISMATCH = "RB1007";

    /// <summary>
    /// An asset payload could not be decoded or uploaded.
    /// </summary>
    public const string ASSET_LOAD_FAILED = "RB1008";

    /// <summary>
    /// An asset path is not in the canonical grammar.
    /// </summary>
    public const string ASSET_PATH_INVALID = "RB1009";

    /// <summary>
    /// A GPU asset upload failed.
    /// </summary>
    public const string ASSET_GPU_UPLOAD_FAILED = "RB1010";

    /// <summary>
    /// The bake configuration is invalid.
    /// </summary>
    public const string BAKE_CONFIGURATION_INVALID = "RB4001";

    /// <summary>
    /// A TOML source or sidecar could not be parsed or bound.
    /// </summary>
    public const string BAKE_TOML_INVALID = "RB4002";

    /// <summary>
    /// A source file or its canonical path is invalid.
    /// </summary>
    public const string BAKE_SOURCE_INVALID = "RB4003";

    /// <summary>
    /// A pack could not be laid out or written.
    /// </summary>
    public const string BAKE_PACK_WRITE_FAILED = "RB4004";

    /// <summary>
    /// The incremental manifest could not be read or written.
    /// </summary>
    public const string BAKE_INCREMENTAL_FAILED = "RB4005";

    /// <summary>
    /// A processor failed while processing a source.
    /// </summary>
    public const string BAKE_PROCESSOR_FAILED = "RB4006";

    /// <summary>
    /// A texture source could not be decoded or encoded.
    /// </summary>
    public const string BAKE_TEXTURE_FAILED = "RB4007";

    /// <summary>
    /// A shader source could not be compiled or reflected.
    /// </summary>
    public const string BAKE_SHADER_FAILED = "RB4008";

    /// <summary>
    /// A model source could not be parsed or converted.
    /// </summary>
    public const string BAKE_MODEL_FAILED = "RB4009";

    /// <summary>
    /// A font source could not be read or packaged.
    /// </summary>
    public const string BAKE_FONT_FAILED = "RB4010";

    /// <summary>
    /// A baked reference is missing or invalid.
    /// </summary>
    public const string BAKE_REFERENCE_INVALID = "RB4011";

    /// <summary>
    /// A semantic asset validator reported an error.
    /// </summary>
    public const string BAKE_VALIDATION_FAILED = "RB4012";
}
