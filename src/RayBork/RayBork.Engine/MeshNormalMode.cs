namespace RayBork;

/// <summary>Selects how <see cref="MeshBuilder.ComputeNormals"/> derives vertex normals.</summary>
public enum MeshNormalMode
{
    /// <summary>Area-weighted face normals shared by vertices at the same position.</summary>
    Smooth = 0,

    /// <summary>A face normal per triangle, with each triangle split into three vertices.</summary>
    Flat = 1,
}
