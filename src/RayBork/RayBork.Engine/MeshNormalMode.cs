namespace RayBork;

/// <summary>
/// Selects how <see cref="MeshBuilder.ComputeNormals"/> derives the vertex normals of a triangle mesh.
/// </summary>
public enum MeshNormalMode {
    /// <summary>
    /// Area-weighted face normals accumulated per shared position and normalized per vertex. Vertices
    /// that share a position receive the same smoothed normal.
    /// </summary>
    Smooth = 0,

    /// <summary>
    /// The face normal of each triangle, with the triangle split into three vertices so every face
    /// keeps its hard edge.
    /// </summary>
    Flat = 1,
}
