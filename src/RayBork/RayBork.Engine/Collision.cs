using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Allocation-free collision checks and ray casts over the engine's math types.
/// </summary>
/// <remarks>
/// <para>
/// The helpers are pure static math. They never query the renderer or the scene, never allocate,
/// never lock, and are safe to call from any thread. <see cref="GetRayMesh"/> is the only member
/// that reads an asset; every other member accepts arbitrary values and returns a result.
/// </para>
/// <para>
/// The helpers never throw for numeric input, including negative and non-finite values. A NaN
/// input reports no collision or a miss, and the ray queries treat every non-finite input as a
/// miss. Ray results use the ray parameter <c>t</c>, so
/// <see cref="RayCollision.Distance"/> is a world distance only when <see cref="Ray.Direction"/> is
/// normalized.
/// </para>
/// </remarks>
public static class Collision {
    private const float PARALLEL_EPSILON = 1e-6f;
    private const float PARALLEL_EPSILON_SQUARED = PARALLEL_EPSILON * PARALLEL_EPSILON;

    /// <summary>
    /// Tests two spheres for overlap.
    /// </summary>
    /// <param name="centerA">The center of the first sphere.</param>
    /// <param name="radiusA">The radius of the first sphere.</param>
    /// <param name="centerB">The center of the second sphere.</param>
    /// <param name="radiusB">The radius of the second sphere.</param>
    /// <returns>
    /// <c>true</c> when the distance between the centers is at most
    /// <paramref name="radiusA"/> + <paramref name="radiusB"/>; touching spheres count as a hit.
    /// </returns>
    /// <remarks>
    /// A negative radius on either sphere or a NaN input reports <c>false</c>.
    /// </remarks>
    public static bool CheckSpheres(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB) {
        if (!(radiusA >= 0f) || !(radiusB >= 0f)) {
            return false;
        }

        float radiusSum = radiusA + radiusB;
        return Vector3.DistanceSquared(centerA, centerB) <= radiusSum * radiusSum;
    }

    /// <summary>
    /// Tests two axis-aligned boxes for overlap.
    /// </summary>
    /// <param name="a">The first box.</param>
    /// <param name="b">The second box.</param>
    /// <returns>
    /// <c>true</c> when the boxes overlap or touch on every axis.
    /// </returns>
    /// <remarks>
    /// The test is inclusive, so boxes that only share a face, edge, or corner count as overlapping.
    /// A NaN input reports <c>false</c>.
    /// </remarks>
    public static bool CheckBoxes(BoundingBox a, BoundingBox b) {
        return a.Min.X <= b.Max.X
            && a.Max.X >= b.Min.X
            && a.Min.Y <= b.Max.Y
            && a.Max.Y >= b.Min.Y
            && a.Min.Z <= b.Max.Z
            && a.Max.Z >= b.Min.Z;
    }

    /// <summary>
    /// Tests an axis-aligned box and a sphere for overlap.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="center">The sphere center.</param>
    /// <param name="radius">The sphere radius.</param>
    /// <returns>
    /// <c>true</c> when the squared distance from the sphere center to the closest point of the box
    /// is at most <paramref name="radius"/> squared.
    /// </returns>
    /// <remarks>
    /// The test clamps the center to the box, so a center inside the box always collides. It is
    /// inclusive: a sphere that only touches the box counts as overlapping. A zero radius is a
    /// point sphere. A negative radius or a NaN input reports <c>false</c>.
    /// </remarks>
    public static bool CheckBoxSphere(BoundingBox box, Vector3 center, float radius) {
        // Reject NaN bounds up front: the min/max result of a NaN bound depends on the operand order
        // and the hardware path, and a NaN input must report no collision.
        if (!(radius >= 0f) || IsNaN(center) || IsNaN(box.Min) || IsNaN(box.Max)) {
            return false;
        }

        Vector3 closest = Vector3.Clamp(center, box.Min, box.Max);
        return Vector3.DistanceSquared(closest, center) <= radius * radius;
    }

    /// <summary>
    /// Casts a ray against a sphere and returns the nearest non-negative intersection.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="center">The sphere center.</param>
    /// <param name="radius">The sphere radius.</param>
    /// <returns>
    /// The nearest hit, or <c>default(RayCollision)</c> when the ray misses. When the ray origin is
    /// inside the sphere, the result is the exit intersection with its outward normal.
    /// </returns>
    /// <remarks>
    /// A zero or negative radius, a non-finite input, or no non-negative root reports a miss.
    /// Tangency counts as a hit. The direction is not required to be normalized.
    /// </remarks>
    public static RayCollision GetRaySphere(Ray ray, Vector3 center, float radius) {
        if (!(radius > 0f) || !float.IsFinite(radius) || !IsFinite(ray) || !IsFinite(center)) {
            return default;
        }

        Vector3 offset = ray.Origin - center;
        float a = ray.Direction.LengthSquared();
        if (a == 0f) {
            return default;
        }

        float b = 2f * Vector3.Dot(offset, ray.Direction);
        float c = offset.LengthSquared() - (radius * radius);
        float discriminant = (b * b) - (4f * a * c);
        if (discriminant < 0f) {
            return default;
        }

        float root = MathF.Sqrt(discriminant);
        float inverse = 1f / (2f * a);
        float near = (-b - root) * inverse;
        float far = (-b + root) * inverse;
        float distance;
        if (near >= 0f) {
            distance = near;
        } else if (far >= 0f) {
            distance = far;
        } else {
            return default;
        }

        Vector3 point = ray.Origin + (ray.Direction * distance);
        return new RayCollision(distance, point, (point - center) / radius);
    }

    /// <summary>
    /// Casts a ray against an axis-aligned box and returns the nearest non-negative intersection.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="box">The box.</param>
    /// <returns>
    /// The nearest hit, or <c>default(RayCollision)</c> when the ray misses. When the ray origin is
    /// inside the box, the result is the exit face with its outward normal.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The slab method is used. A ray parallel to a slab is a miss when it is outside that slab and
    /// continues when it is inside. Touching a face at <c>t = 0</c> counts as a hit if the box is
    /// in front of the ray.
    /// </para>
    /// <para>
    /// An inverted box (any <see cref="BoundingBox.Min"/> component greater than the matching
    /// <see cref="BoundingBox.Max"/> component), a non-finite input, or a zero direction reports a
    /// miss. The direction is not required to be normalized.
    /// </para>
    /// </remarks>
    public static RayCollision GetRayBox(Ray ray, BoundingBox box) {
        if (!IsFinite(ray) || !IsFinite(box.Min) || !IsFinite(box.Max)) {
            return default;
        }

        if (box.Min.X > box.Max.X || box.Min.Y > box.Max.Y || box.Min.Z > box.Max.Z) {
            return default;
        }

        if (ray.Direction.LengthSquared() == 0f) {
            return default;
        }

        float entry = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        Vector3 entryNormal = Vector3.Zero;
        Vector3 exitNormal = Vector3.Zero;
        if (!IntersectSlab(
                ray.Origin.X,
                ray.Direction.X,
                box.Min.X,
                box.Max.X,
                Vector3.UnitX,
                ref entry,
                ref exit,
                ref entryNormal,
                ref exitNormal)) {
            return default;
        }

        if (!IntersectSlab(
                ray.Origin.Y,
                ray.Direction.Y,
                box.Min.Y,
                box.Max.Y,
                Vector3.UnitY,
                ref entry,
                ref exit,
                ref entryNormal,
                ref exitNormal)) {
            return default;
        }

        if (!IntersectSlab(
                ray.Origin.Z,
                ray.Direction.Z,
                box.Min.Z,
                box.Max.Z,
                Vector3.UnitZ,
                ref entry,
                ref exit,
                ref entryNormal,
                ref exitNormal)) {
            return default;
        }

        if (exit < 0f) {
            return default;
        }

        float distance = entry >= 0f ? entry : exit;
        if (!float.IsFinite(distance)) {
            return default;
        }

        Vector3 normal = entry >= 0f ? entryNormal : exitNormal;
        return new RayCollision(distance, ray.Origin + (ray.Direction * distance), normal);
    }

    /// <summary>
    /// Casts a ray against a triangle and returns the nearest non-negative intersection.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="a">The first triangle corner.</param>
    /// <param name="b">The second triangle corner.</param>
    /// <param name="c">The third triangle corner.</param>
    /// <returns>
    /// The nearest hit, or <c>default(RayCollision)</c> when the ray misses.
    /// </returns>
    /// <remarks>
    /// The test uses the double-sided Möller–Trumbore algorithm. <see cref="RayCollision.Normal"/>
    /// is the normalized geometric normal <c>cross(b - a, c - a)</c> and is not flipped toward the
    /// ray, so a back-face hit reports the same normal as a front-face hit. A degenerate
    /// (zero-area) triangle, a parallel ray, a non-finite input, or a hit behind the ray origin
    /// reports a miss. The direction is not required to be normalized.
    /// </remarks>
    public static RayCollision GetRayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c) {
        if (!TryGetTriangleIntersection(ray, a, b, c, out float distance, out Vector3 normal)) {
            return default;
        }

        return new RayCollision(distance, ray.Origin + (ray.Direction * distance), normal);
    }

    /// <summary>
    /// Tests the double-sided Möller–Trumbore ray/triangle intersection the public queries share.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="a">The first triangle corner.</param>
    /// <param name="b">The second triangle corner.</param>
    /// <param name="c">The third triangle corner.</param>
    /// <param name="distance">The ray parameter of the hit, or zero on a miss.</param>
    /// <param name="normal">The normalized geometric normal, or zero on a miss.</param>
    /// <returns><c>true</c> when the ray hits the triangle in front of its origin.</returns>
    /// <remarks>
    /// The validation and numeric behavior match <see cref="GetRayTriangle"/> exactly so the
    /// brute-force and accelerated mesh paths select and report the same triangle.
    /// </remarks>
    internal static bool TryGetTriangleIntersection(
        Ray ray,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance,
        out Vector3 normal) {
        distance = 0f;
        normal = Vector3.Zero;
        if (!IsFinite(ray) || !IsFinite(a) || !IsFinite(b) || !IsFinite(c)) {
            return false;
        }

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 faceNormal = Vector3.Cross(edge1, edge2);
        float normalLengthSquared = faceNormal.LengthSquared();
        if (!(normalLengthSquared > 0f)) {
            return false;
        }

        Vector3 pvec = Vector3.Cross(ray.Direction, edge2);
        float determinant = Vector3.Dot(edge1, pvec);
        float scale = PARALLEL_EPSILON_SQUARED * normalLengthSquared * ray.Direction.LengthSquared();
        if (!((determinant * determinant) > scale)) {
            return false;
        }

        float inverse = 1f / determinant;
        Vector3 tvec = ray.Origin - a;
        float u = Vector3.Dot(tvec, pvec) * inverse;
        if (!(u >= 0f && u <= 1f)) {
            return false;
        }

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(ray.Direction, qvec) * inverse;
        if (!(v >= 0f && (u + v) <= 1f)) {
            return false;
        }

        float hitDistance = Vector3.Dot(edge2, qvec) * inverse;
        if (!(hitDistance >= 0f)) {
            return false;
        }

        distance = hitDistance;
        normal = Vector3.Normalize(faceNormal);
        return true;
    }

    /// <summary>
    /// Casts a ray against a plane and returns the nearest non-negative intersection.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="plane">The plane.</param>
    /// <returns>
    /// The nearest hit, or <c>default(RayCollision)</c> when the ray misses.
    /// </returns>
    /// <remarks>
    /// The plane is double-sided: both faces hit. <see cref="RayCollision.Normal"/> is
    /// <see cref="Plane.Normal"/> normalized and is not flipped toward the ray. A ray parallel to
    /// the plane, including one that lies in it, a non-finite input, or a hit behind the ray origin
    /// reports a miss. The plane normal and ray direction are not required to be normalized.
    /// </remarks>
    public static RayCollision GetRayPlane(Ray ray, Plane plane) {
        if (!IsFinite(ray) || !IsFinite(plane.Normal) || !float.IsFinite(plane.D)) {
            return default;
        }

        float denominator = Vector3.Dot(plane.Normal, ray.Direction);
        float scale = PARALLEL_EPSILON_SQUARED * plane.Normal.LengthSquared() * ray.Direction.LengthSquared();
        if (!((denominator * denominator) > scale)) {
            return default;
        }

        float distance = -(Vector3.Dot(plane.Normal, ray.Origin) + plane.D) / denominator;
        if (!(distance >= 0f)) {
            return default;
        }

        Vector3 point = ray.Origin + (ray.Direction * distance);
        return new RayCollision(distance, point, Vector3.Normalize(plane.Normal));
    }

    /// <summary>
    /// Casts a ray against a mesh's CPU triangles and returns the nearest non-negative intersection.
    /// </summary>
    /// <param name="ray">The world-space ray.</param>
    /// <param name="mesh">The mesh. Its <see cref="Mesh.Vertices"/> and <see cref="Mesh.Indices"/> are read.</param>
    /// <param name="transform">The mesh-to-world transform.</param>
    /// <returns>
    /// The nearest world-space hit, or <c>default(RayCollision)</c> when the ray misses.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The transform is treated as mesh-to-world under the row-vector convention:
    /// <c>world = Vector3.Transform(meshPoint, transform)</c>. The query inverts the transform,
    /// moves the ray into mesh space, finds the nearest triangle, and maps the result back to world
    /// space. <see cref="RayCollision.Point"/> is the transformed mesh-space point and
    /// <see cref="RayCollision.Normal"/> is
    /// <c>normalize(Vector3.TransformNormal(meshNormal, Matrix4x4.Transpose(inverse)))</c>.
    /// <see cref="RayCollision.Distance"/> is the mesh-space ray parameter, which equals the world
    /// parameter along the given world direction.
    /// </para>
    /// <para>
    /// <see cref="PrimitiveTopology.TriangleList"/> uses the indices in groups of three.
    /// <see cref="PrimitiveTopology.TriangleStrip"/> uses the sliding window with the first two
    /// indices swapped on odd triangles, matching <see cref="MeshBuilder"/>. Degenerate triangles are
    /// skipped.
    /// </para>
    /// <para>
    /// When the mesh carries a collision acceleration structure (see
    /// <see cref="MeshBuilder.GenerateCollisions"/>), the query traverses it; otherwise it tests every
    /// triangle. Both paths return the same nearest hit.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="mesh"/> is disposed.</exception>
    /// <exception cref="NotSupportedException">
    /// The mesh topology is not <see cref="PrimitiveTopology.TriangleList"/> or
    /// <see cref="PrimitiveTopology.TriangleStrip"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="transform"/> is not invertible.</exception>
    public static RayCollision GetRayMesh(Ray ray, Mesh mesh, Matrix4x4 transform) {
        ArgumentNullException.ThrowIfNull(mesh);
        ObjectDisposedException.ThrowIf(mesh.IsDisposed, mesh);

        PrimitiveTopology topology = mesh.Topology;
        if (topology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)) {
            throw new NotSupportedException(
                $"Ray-mesh collision supports only triangle topologies; the mesh topology is {topology}.");
        }

        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse)) {
            throw new ArgumentException("The mesh transform is not invertible.", nameof(transform));
        }

        Ray meshRay = new(
            Vector3.Transform(ray.Origin, inverse),
            Vector3.TransformNormal(ray.Direction, inverse));

        ReadOnlySpan<Vertex> vertices = mesh.Vertices.Span;
        ReadOnlySpan<uint> indices = mesh.Indices.Span;
        MeshCollisionData? collision = mesh.Collision;
        RayCollision nearest = default;
        if (collision is not null
            && collision.Topology == topology
            && collision.TriangleCount == MeshCollisionData.GetTriangleCount(topology, indices.Length)) {
            if (collision.TryFindNearest(meshRay, vertices, indices, out int triangleId, out _)
                && MeshCollisionData.TryGetTriangleIndices(
                    topology,
                    indices,
                    triangleId,
                    out uint index0,
                    out uint index1,
                    out uint index2)
                && TryGetTriangleIntersection(
                    meshRay,
                    vertices[(int)index0].Position,
                    vertices[(int)index1].Position,
                    vertices[(int)index2].Position,
                    out float distance,
                    out Vector3 triangleNormal)) {
                nearest = new RayCollision(
                    distance,
                    meshRay.Origin + (meshRay.Direction * distance),
                    triangleNormal);
            }
        } else if (topology == PrimitiveTopology.TriangleList) {
            for (int offset = 0; offset + 2 < indices.Length; offset += 3) {
                ConsiderTriangle(
                    meshRay,
                    vertices,
                    indices[offset],
                    indices[offset + 1],
                    indices[offset + 2],
                    ref nearest);
            }
        } else {
            for (int triangle = 0; triangle + 2 < indices.Length; triangle++) {
                uint index0 = indices[triangle];
                uint index1 = indices[triangle + 1];
                uint index2 = indices[triangle + 2];
                if ((triangle & 1) != 0) {
                    (index0, index1) = (index1, index0);
                }

                ConsiderTriangle(meshRay, vertices, index0, index1, index2, ref nearest);
            }
        }

        if (!nearest.Hit) {
            return default;
        }

        Vector3 normal = Vector3.Normalize(
            Vector3.TransformNormal(nearest.Normal, Matrix4x4.Transpose(inverse)));
        return new RayCollision(
            nearest.Distance,
            Vector3.Transform(nearest.Point, transform),
            normal);
    }

    private static bool IntersectSlab(
        float origin,
        float direction,
        float minimum,
        float maximum,
        Vector3 axis,
        ref float entry,
        ref float exit,
        ref Vector3 entryNormal,
        ref Vector3 exitNormal) {
        if (direction == 0f) {
            return origin >= minimum && origin <= maximum;
        }

        float inverse = 1f / direction;
        float near = (minimum - origin) * inverse;
        float far = (maximum - origin) * inverse;
        Vector3 nearNormal = -axis;
        Vector3 farNormal = axis;
        if (direction < 0f) {
            (near, far) = (far, near);
            nearNormal = axis;
            farNormal = -axis;
        }

        if (near > entry) {
            entry = near;
            entryNormal = nearNormal;
        }

        if (far < exit) {
            exit = far;
            exitNormal = farNormal;
        }

        return entry <= exit;
    }

    private static void ConsiderTriangle(
        Ray ray,
        ReadOnlySpan<Vertex> vertices,
        uint index0,
        uint index1,
        uint index2,
        ref RayCollision nearest) {
        RayCollision hit = GetRayTriangle(
            ray,
            vertices[(int)index0].Position,
            vertices[(int)index1].Position,
            vertices[(int)index2].Position);
        if (hit.Hit && (!nearest.Hit || hit.Distance < nearest.Distance)) {
            nearest = hit;
        }
    }

    private static bool IsFinite(Ray ray) => IsFinite(ray.Origin) && IsFinite(ray.Direction);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsNaN(Vector3 value) =>
        float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z);
}
