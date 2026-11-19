using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Allocation-free collision checks and world-space ray casts over the engine's math types.
/// </summary>
/// <remarks>
/// Pure static math that never allocates, touches the scene, or locks; only
/// <see cref="GetRayMesh"/> reads an asset. Queries never throw for numeric input — NaN or a
/// zero-direction <c>default(Ray)</c> reports a miss — and hit distances are world distances
/// because ray directions are unit length.
/// </remarks>
public static class Collision
{
    private const float PARALLEL_EPSILON = 1e-6f;
    private const float PARALLEL_EPSILON_SQUARED = PARALLEL_EPSILON * PARALLEL_EPSILON;

    /// <summary>Tests two spheres for overlap; touching spheres count as a hit.</summary>
    /// <remarks>
    /// A negative radius or NaN input reports <c>false</c>.
    /// </remarks>
    public static bool CheckSpheres(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB)
    {
        if (!(radiusA >= 0f) || !(radiusB >= 0f))
        {
            return false;
        }

        float radiusSum = radiusA + radiusB;
        return Vector3.DistanceSquared(centerA, centerB) <= radiusSum * radiusSum;
    }

    /// <summary>Tests two axis-aligned boxes for overlap; touching counts as a hit.</summary>
    /// <remarks>
    /// The test is inclusive on every axis, and a NaN input reports <c>false</c>.
    /// </remarks>
    public static bool CheckBoxes(BoundingBox a, BoundingBox b)
    {
        return a.Min.X <= b.Max.X
            && a.Max.X >= b.Min.X
            && a.Min.Y <= b.Max.Y
            && a.Max.Y >= b.Min.Y
            && a.Min.Z <= b.Max.Z
            && a.Max.Z >= b.Min.Z;
    }

    /// <summary>Tests an axis-aligned box against a sphere; touching counts as a hit.</summary>
    /// <remarks>
    /// A center inside the box always collides. A negative radius or NaN input reports <c>false</c>.
    /// </remarks>
    public static bool CheckBoxSphere(BoundingBox box, Vector3 center, float radius)
    {
        // Reject NaN bounds up front: the min/max result of a NaN bound depends on the operand order
        // and the hardware path, and a NaN input must report no collision.
        if (!(radius >= 0f) || IsNaN(center) || IsNaN(box.Min) || IsNaN(box.Max))
        {
            return false;
        }

        Vector3 closest = Vector3.Clamp(center, box.Min, box.Max);
        return Vector3.DistanceSquared(closest, center) <= radius * radius;
    }

    /// <summary>Casts a ray against a sphere and returns the nearest non-negative hit.</summary>
    /// <remarks>
    /// Inside the sphere returns the exit hit with its outward normal; a zero or negative radius,
    /// non-finite input, or no non-negative root misses, and tangency counts as a hit.
    /// </remarks>
    public static RayCollision GetRaySphere(Ray ray, Vector3 center, float radius)
    {
        if (!(radius > 0f) || !float.IsFinite(radius) || !IsFinite(ray) || !IsFinite(center))
        {
            return default;
        }

        Vector3 offset = ray.Origin - center;
        float a = ray.Direction.LengthSquared();
        if (a == 0f)
        {
            return default;
        }

        float b = 2f * Vector3.Dot(offset, ray.Direction);
        float c = offset.LengthSquared() - (radius * radius);
        float discriminant = (b * b) - (4f * a * c);
        if (discriminant < 0f)
        {
            return default;
        }

        float root = MathF.Sqrt(discriminant);
        float inverse = 1f / (2f * a);
        float near = (-b - root) * inverse;
        float far = (-b + root) * inverse;
        float distance;
        if (near >= 0f)
        {
            distance = near;
        }
        else if (far >= 0f)
        {
            distance = far;
        }
        else
        {
            return default;
        }

        Vector3 point = ray.Origin + (ray.Direction * distance);
        return new RayCollision(distance, point, (point - center) / radius);
    }

    /// <summary>Casts a ray against an axis-aligned box and returns the nearest hit.</summary>
    /// <remarks>
    /// An origin inside the box returns the exit face with its outward normal; an inverted box,
    /// non-finite input, or zero direction misses. Touching a face at <c>t = 0</c> counts as a hit.
    /// </remarks>
    public static RayCollision GetRayBox(Ray ray, BoundingBox box)
    {
        if (!IsFinite(ray) || !IsFinite(box.Min) || !IsFinite(box.Max))
        {
            return default;
        }

        if (box.Min.X > box.Max.X || box.Min.Y > box.Max.Y || box.Min.Z > box.Max.Z)
        {
            return default;
        }

        if (ray.Direction.LengthSquared() == 0f)
        {
            return default;
        }

        float entry = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        Vector3 entryNormal = Vector3.Zero;
        Vector3 exitNormal = Vector3.Zero;
        if (
            !IntersectSlab(
                ray.Origin.X,
                ray.Direction.X,
                box.Min.X,
                box.Max.X,
                Vector3.UnitX,
                ref entry,
                ref exit,
                ref entryNormal,
                ref exitNormal
            )
        )
        {
            return default;
        }

        if (
            !IntersectSlab(
                ray.Origin.Y,
                ray.Direction.Y,
                box.Min.Y,
                box.Max.Y,
                Vector3.UnitY,
                ref entry,
                ref exit,
                ref entryNormal,
                ref exitNormal
            )
        )
        {
            return default;
        }

        if (
            !IntersectSlab(
                ray.Origin.Z,
                ray.Direction.Z,
                box.Min.Z,
                box.Max.Z,
                Vector3.UnitZ,
                ref entry,
                ref exit,
                ref entryNormal,
                ref exitNormal
            )
        )
        {
            return default;
        }

        if (exit < 0f)
        {
            return default;
        }

        float distance = entry >= 0f ? entry : exit;
        if (!float.IsFinite(distance))
        {
            return default;
        }

        Vector3 normal = entry >= 0f ? entryNormal : exitNormal;
        return new RayCollision(distance, ray.Origin + (ray.Direction * distance), normal);
    }

    /// <summary>Casts a ray against a triangle and returns the nearest non-negative hit.</summary>
    /// <remarks>
    /// The test is double-sided and the normal is the geometric normal, not flipped toward the ray.
    /// A degenerate triangle, parallel ray, hit behind the origin, or non-finite input misses.
    /// </remarks>
    public static RayCollision GetRayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c)
    {
        if (!TryGetTriangleIntersection(ray, a, b, c, out float distance, out Vector3 normal))
        {
            return default;
        }

        return new RayCollision(distance, ray.Origin + (ray.Direction * distance), normal);
    }

    /// <summary>Casts a ray against a quad and returns the nearest non-negative hit.</summary>
    /// <remarks>
    /// The quad splits into <c>(a, b, c)</c> and <c>(a, c, d)</c> triangles and the nearer double-sided
    /// hit wins; a hit on the shared diagonal reports the first. Degenerate, behind-origin, or
    /// non-finite inputs miss.
    /// </remarks>
    public static RayCollision GetRayQuad(Ray ray, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        RayCollision first = GetRayTriangle(ray, a, b, c);
        RayCollision second = GetRayTriangle(ray, a, c, d);
        if (!first.Hit)
        {
            return second;
        }

        if (!second.Hit)
        {
            return first;
        }

        return second.Distance < first.Distance ? second : first;
    }

    /// <summary>Tests the double-sided ray/triangle intersection shared by the queries.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="a">The first triangle vertex.</param>
    /// <param name="b">The second triangle vertex.</param>
    /// <param name="c">The third triangle vertex.</param>
    /// <param name="distance">The hit ray parameter, or zero on a miss.</param>
    /// <param name="normal">The normalized geometric normal, or zero on a miss.</param>
    /// <returns><c>true</c> when the ray hits the triangle in front of its origin.</returns>
    internal static bool TryGetTriangleIntersection(
        Ray ray,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance,
        out Vector3 normal
    )
    {
        distance = 0f;
        normal = Vector3.Zero;
        if (!IsFinite(ray) || !IsFinite(a) || !IsFinite(b) || !IsFinite(c))
        {
            return false;
        }

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 faceNormal = Vector3.Cross(edge1, edge2);
        float normalLengthSquared = faceNormal.LengthSquared();
        if (!(normalLengthSquared > 0f))
        {
            return false;
        }

        Vector3 pvec = Vector3.Cross(ray.Direction, edge2);
        float determinant = Vector3.Dot(edge1, pvec);
        float scale =
            PARALLEL_EPSILON_SQUARED * normalLengthSquared * ray.Direction.LengthSquared();
        if (!((determinant * determinant) > scale))
        {
            return false;
        }

        float inverse = 1f / determinant;
        Vector3 tvec = ray.Origin - a;
        float u = Vector3.Dot(tvec, pvec) * inverse;
        if (!(u >= 0f && u <= 1f))
        {
            return false;
        }

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(ray.Direction, qvec) * inverse;
        if (!(v >= 0f && (u + v) <= 1f))
        {
            return false;
        }

        float hitDistance = Vector3.Dot(edge2, qvec) * inverse;
        if (!(hitDistance >= 0f))
        {
            return false;
        }

        distance = hitDistance;
        normal = Vector3.Normalize(faceNormal);
        return true;
    }

    /// <summary>Casts a ray against a double-sided plane and returns the nearest hit.</summary>
    /// <remarks>
    /// <see cref="RayCollision.Normal"/> is <see cref="Plane.Normal"/> normalized and not flipped. A
    /// parallel ray, including one in the plane, a behind-origin hit, or non-finite input misses.
    /// </remarks>
    public static RayCollision GetRayPlane(Ray ray, Plane plane)
    {
        if (!IsFinite(ray) || !IsFinite(plane.Normal) || !float.IsFinite(plane.D))
        {
            return default;
        }

        float denominator = Vector3.Dot(plane.Normal, ray.Direction);
        float scale =
            PARALLEL_EPSILON_SQUARED * plane.Normal.LengthSquared() * ray.Direction.LengthSquared();
        if (!((denominator * denominator) > scale))
        {
            return default;
        }

        float distance = -(Vector3.Dot(plane.Normal, ray.Origin) + plane.D) / denominator;
        if (!(distance >= 0f))
        {
            return default;
        }

        Vector3 point = ray.Origin + (ray.Direction * distance);
        return new RayCollision(distance, point, Vector3.Normalize(plane.Normal));
    }

    /// <summary>Casts a world-space ray against a mesh and returns the nearest hit.</summary>
    /// <remarks>
    /// The transform is mesh-to-world in the row-vector convention, and the hit point and normal are
    /// mapped back to world space. Triangle list and strip topologies are supported, using a collision
    /// acceleration structure when present.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="mesh"/> is disposed.</exception>
    /// <exception cref="NotSupportedException">
    /// The mesh topology is not a triangle list or strip.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="transform"/> is not invertible.</exception>
    public static RayCollision GetRayMesh(Ray ray, Mesh mesh, Matrix4x4 transform)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ObjectDisposedException.ThrowIf(mesh.IsDisposed, mesh);

        PrimitiveTopology topology = mesh.Topology;
        if (topology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip))
        {
            throw new NotSupportedException(
                $"Ray-mesh collision supports only triangle topologies; the mesh topology is {topology}."
            );
        }

        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse))
        {
            throw new ArgumentException("The mesh transform is not invertible.", nameof(transform));
        }

        if (!IsFinite(ray) || !(ray.Direction.LengthSquared() > 0f))
        {
            return default;
        }

        Vector3 meshOrigin = Vector3.Transform(ray.Origin, inverse);
        Vector3 meshDirection = Vector3.TransformNormal(ray.Direction, inverse);
        double meshDirectionLength = Math.Sqrt(
            ((double)meshDirection.X * meshDirection.X)
                + ((double)meshDirection.Y * meshDirection.Y)
                + ((double)meshDirection.Z * meshDirection.Z)
        );
        if (
            !IsFinite(meshOrigin)
            || !IsFinite(meshDirection)
            || !(meshDirectionLength > 0d)
            || !double.IsFinite(meshDirectionLength)
        )
        {
            return default;
        }

        Ray meshRay = new(meshOrigin, meshDirection);

        ReadOnlySpan<Vertex> vertices = mesh.Vertices.Span;
        ReadOnlySpan<uint> indices = mesh.Indices.Span;
        MeshCollisionData? collision = mesh.Collision;
        RayCollision nearest = default;
        if (
            collision is not null
            && collision.Topology == topology
            && collision.TriangleCount
                == MeshCollisionData.GetTriangleCount(topology, indices.Length)
        )
        {
            if (
                collision.TryFindNearest(meshRay, vertices, indices, out int triangleId, out _)
                && MeshCollisionData.TryGetTriangleIndices(
                    topology,
                    indices,
                    triangleId,
                    out uint index0,
                    out uint index1,
                    out uint index2
                )
                && TryGetTriangleIntersection(
                    meshRay,
                    vertices[(int)index0].Position,
                    vertices[(int)index1].Position,
                    vertices[(int)index2].Position,
                    out float distance,
                    out Vector3 triangleNormal
                )
            )
            {
                nearest = new RayCollision(
                    distance,
                    meshRay.Origin + (meshRay.Direction * distance),
                    triangleNormal
                );
            }
        }
        else if (topology == PrimitiveTopology.TriangleList)
        {
            for (int offset = 0; offset + 2 < indices.Length; offset += 3)
            {
                ConsiderTriangle(
                    meshRay,
                    vertices,
                    indices[offset],
                    indices[offset + 1],
                    indices[offset + 2],
                    ref nearest
                );
            }
        }
        else
        {
            for (int triangle = 0; triangle + 2 < indices.Length; triangle++)
            {
                uint index0 = indices[triangle];
                uint index1 = indices[triangle + 1];
                uint index2 = indices[triangle + 2];
                if ((triangle & 1) != 0)
                {
                    (index0, index1) = (index1, index0);
                }

                ConsiderTriangle(meshRay, vertices, index0, index1, index2, ref nearest);
            }
        }

        if (!nearest.Hit)
        {
            return default;
        }

        Vector3 normal = Vector3.Normalize(
            Vector3.TransformNormal(nearest.Normal, Matrix4x4.Transpose(inverse))
        );
        // The mesh ray carries the normalized image of the unit world direction, so its parameter is
        // the world parameter times the image's length; dividing reports the world-space distance.
        return new RayCollision(
            (float)(nearest.Distance / meshDirectionLength),
            Vector3.Transform(nearest.Point, transform),
            normal
        );
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
        ref Vector3 exitNormal
    )
    {
        if (direction == 0f)
        {
            return origin >= minimum && origin <= maximum;
        }

        float inverse = 1f / direction;
        float near = (minimum - origin) * inverse;
        float far = (maximum - origin) * inverse;
        Vector3 nearNormal = -axis;
        Vector3 farNormal = axis;
        if (direction < 0f)
        {
            (near, far) = (far, near);
            nearNormal = axis;
            farNormal = -axis;
        }

        if (near > entry)
        {
            entry = near;
            entryNormal = nearNormal;
        }

        if (far < exit)
        {
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
        ref RayCollision nearest
    )
    {
        RayCollision hit = GetRayTriangle(
            ray,
            vertices[(int)index0].Position,
            vertices[(int)index1].Position,
            vertices[(int)index2].Position
        );
        if (hit.Hit && (!nearest.Hit || hit.Distance < nearest.Distance))
        {
            nearest = hit;
        }
    }

    private static bool IsFinite(Ray ray) => IsFinite(ray.Origin) && IsFinite(ray.Direction);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsNaN(Vector3 value) =>
        float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z);
}
