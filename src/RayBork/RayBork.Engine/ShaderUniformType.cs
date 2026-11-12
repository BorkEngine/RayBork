namespace RayBork;

/// <summary>
/// The reflected value type of a shader uniform.
/// </summary>
public enum ShaderUniformType {
    /// <summary>A single 32-bit float.</summary>
    Float = 0,

    /// <summary>A 32-bit signed integer.</summary>
    Int = 1,

    /// <summary>A 32-bit unsigned integer.</summary>
    UInt = 2,

    /// <summary>A boolean stored as a 32-bit unsigned integer.</summary>
    Bool = 3,

    /// <summary>A two-component float vector.</summary>
    Vector2 = 4,

    /// <summary>A three-component float vector.</summary>
    Vector3 = 5,

    /// <summary>A four-component float vector.</summary>
    Vector4 = 6,

    /// <summary>A 4x4 float matrix.</summary>
    Matrix4x4 = 7,

    /// <summary>A bindless texture reference.</summary>
    Texture2D = 8,
}
