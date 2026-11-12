using System.Globalization;
using System.Numerics;

namespace RayBork.Bake.Models;

/// <summary>
/// Reads a static Wavefront OBJ mesh into parts.
/// </summary>
/// <remarks>
/// <para>
/// Supported statements are <c>v</c>, <c>vn</c>, <c>vt</c>, <c>f</c>, <c>o</c>, and <c>g</c>.
/// Polygons are fan-triangulated. Positive and negative face indices are resolved against the
/// vertices declared so far. A face without normals gets a flat geometric normal per triangle; a
/// face corner without a <c>vt</c> index keeps the zero UV default. Wavefront authors V from the
/// bottom of the texture, while the engine samples from a top-left origin, so every parsed
/// <c>vt</c> has its V flipped (<c>v = 1 - v</c>) and keeps U unchanged; this matches raylib's OBJ
/// loader. <c>mtllib</c> is ignored with a warning because M3 does not import materials. Other
/// statements, including <c>usemtl</c>, <c>s</c>, and comments, are ignored.
/// </para>
/// <para>
/// Each <c>o</c> or <c>g</c> statement starts a new part. Faces before the first one belong to a
/// part named after the source file. A file with no faces is an error.
/// </para>
/// </remarks>
internal static class ObjModelReader {
    /// <summary>Reads one OBJ source.</summary>
    /// <param name="sourcePath">The absolute OBJ path.</param>
    /// <param name="defaultPartName">The part name for faces before any group statement.</param>
    /// <param name="warning">The warning sink.</param>
    /// <returns>The non-empty part list.</returns>
    /// <exception cref="ModelReadException">The file cannot be read or contains invalid geometry.</exception>
    internal static IReadOnlyList<ModelPartSource> Read(
        string sourcePath,
        string defaultPartName,
        Action<string> warning) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPartName);
        ArgumentNullException.ThrowIfNull(warning);

        string[] lines;
        try {
            lines = File.ReadAllLines(sourcePath);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            throw new ModelReadException(
                $"The OBJ source '{Path.GetFileName(sourcePath)}' could not be read: {exception.Message}");
        }

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var parts = new List<ObjPartBuilder>();
        ObjPartBuilder? current = null;
        var filePart = new ObjPartBuilder(defaultPartName);
        string fileName = Path.GetFileName(sourcePath);

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
            string line = StripComment(lines[lineIndex]).Trim();
            if (line.Length == 0) {
                continue;
            }

            int separator = line.IndexOfAny([' ', '\t']);
            string keyword = separator < 0 ? line : line[..separator];
            string rest = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();

            switch (keyword) {
                case "v":
                    positions.Add(ParsePosition(rest, fileName, lineIndex + 1));
                    break;

                case "vn":
                    normals.Add(ParseNormal(rest, fileName, lineIndex + 1));
                    break;

                case "vt":
                    uvs.Add(ParseTextureCoordinate(rest, fileName, lineIndex + 1));
                    break;

                case "f":
                    current ??= filePart;
                    current.Faces.Add(ParseFace(rest, positions.Count, uvs.Count, normals.Count,
                        fileName, lineIndex + 1));
                    break;

                case "o":
                case "g":
                    current = new ObjPartBuilder(rest.Length == 0 ? "part" : rest);
                    parts.Add(current);
                    break;

                case "mtllib":
                    warning(
                        $"OBJ source '{fileName}' references the material library '{rest}'; "
                            + "materials are not imported and the statement is ignored.");
                    break;
            }
        }

        if (!filePart.Faces.Any() && parts.All(part => part.Faces.Count == 0)) {
            throw new ModelReadException($"The OBJ source '{fileName}' contains no faces.");
        }

        var result = new List<ModelPartSource>(parts.Count + 1);
        if (filePart.Faces.Count > 0) {
            result.Add(new ModelPartSource(
                filePart.Name,
                BuildMesh(positions, normals, uvs, filePart.Faces, fileName),
                Matrix4x4.Identity));
        }

        foreach (ObjPartBuilder part in parts) {
            if (part.Faces.Count == 0) {
                continue;
            }

            result.Add(new ModelPartSource(
                part.Name,
                BuildMesh(positions, normals, uvs, part.Faces, fileName),
                Matrix4x4.Identity));
        }

        return result;
    }

    private static MeshData BuildMesh(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uvs,
        IReadOnlyList<ObjFace> faces,
        string fileName) {
        var mesh = new MeshData(PrimitiveTopology.TriangleList);
        var byKey = new Dictionary<VertexKey, uint>();

        foreach (ObjFace face in faces) {
            bool hasNormals = true;
            foreach (ObjCorner corner in face.Corners) {
                if (corner.Normal < 0) {
                    hasNormals = false;
                    break;
                }
            }

            for (int index = 1; index + 1 < face.Corners.Length; index++) {
                ObjCorner first = face.Corners[0];
                ObjCorner second = face.Corners[index];
                ObjCorner third = face.Corners[index + 1];
                Vector3 firstPosition = positions[first.Position];
                Vector3 secondPosition = positions[second.Position];
                Vector3 thirdPosition = positions[third.Position];

                if (hasNormals) {
                    mesh.Indices.Add(GetOrAddVertex(
                        mesh,
                        byKey,
                        first,
                        positions,
                        normals,
                        uvs));
                    mesh.Indices.Add(GetOrAddVertex(
                        mesh,
                        byKey,
                        second,
                        positions,
                        normals,
                        uvs));
                    mesh.Indices.Add(GetOrAddVertex(
                        mesh,
                        byKey,
                        third,
                        positions,
                        normals,
                        uvs));
                    continue;
                }

                Vector3 normal = ComputeNormal(firstPosition, secondPosition, thirdPosition, fileName);
                mesh.Indices.Add(AddFlatVertex(mesh, first, positions, uvs, normal));
                mesh.Indices.Add(AddFlatVertex(mesh, second, positions, uvs, normal));
                mesh.Indices.Add(AddFlatVertex(mesh, third, positions, uvs, normal));
            }
        }

        return mesh;
    }

    private static uint GetOrAddVertex(
        MeshData mesh,
        Dictionary<VertexKey, uint> byKey,
        ObjCorner corner,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uvs) {
        var key = new VertexKey(corner.Position, corner.Uv, corner.Normal);
        if (byKey.TryGetValue(key, out uint existing)) {
            return existing;
        }

        MeshVertex vertex = CreateVertex(corner, positions, uvs);
        Vector3 normal = normals[corner.Normal];
        vertex.Normal = normal.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(normal);
        uint index = AddVertex(mesh, vertex);
        byKey.Add(key, index);
        return index;
    }

    private static uint AddFlatVertex(
        MeshData mesh,
        ObjCorner corner,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2> uvs,
        Vector3 normal) {
        MeshVertex vertex = CreateVertex(corner, positions, uvs);
        vertex.Normal = normal;
        return AddVertex(mesh, vertex);
    }

    private static MeshVertex CreateVertex(
        ObjCorner corner,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2> uvs) {
        MeshVertex vertex = MeshVertex.Create(positions[corner.Position]);
        if (corner.Uv >= 0) {
            vertex.Uv = uvs[corner.Uv];
        }

        return vertex;
    }

    private static uint AddVertex(MeshData mesh, MeshVertex vertex) {
        if (mesh.Vertices.Count == int.MaxValue) {
            throw new ModelReadException("An OBJ part has too many vertices.");
        }

        uint index = (uint)mesh.Vertices.Count;
        mesh.Vertices.Add(vertex);
        return index;
    }

    private static Vector3 ComputeNormal(
        Vector3 first,
        Vector3 second,
        Vector3 third,
        string fileName) {
        Vector3 normal = Vector3.Cross(second - first, third - first);
        float lengthSquared = normal.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-20f) {
            throw new ModelReadException(
                $"OBJ source '{fileName}' contains a degenerate triangle whose normal cannot be computed.");
        }

        return normal / MathF.Sqrt(lengthSquared);
    }

    private static ObjFace ParseFace(
        string rest,
        int positionCount,
        int uvCount,
        int normalCount,
        string fileName,
        int lineNumber) {
        string[] tokens = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3) {
            throw new ModelReadException(
                $"OBJ source '{fileName}' line {lineNumber} declares a face with fewer than three "
                    + "vertices.");
        }

        var corners = new ObjCorner[tokens.Length];
        for (int index = 0; index < tokens.Length; index++) {
            string[] fields = tokens[index].Split('/');
            if (fields.Length is < 1 or > 3) {
                throw new ModelReadException(
                    $"OBJ source '{fileName}' line {lineNumber} has the invalid face vertex "
                        + $"'{tokens[index]}'.");
            }

            int position = ParseIndex(fields[0], positionCount, "position", fileName, lineNumber);
            int uv = fields.Length >= 2 && fields[1].Length > 0
                ? ParseIndex(fields[1], uvCount, "texture coordinate", fileName, lineNumber)
                : -1;
            int normal = fields.Length == 3 && fields[2].Length > 0
                ? ParseIndex(fields[2], normalCount, "normal", fileName, lineNumber)
                : -1;
            corners[index] = new ObjCorner(position, uv, normal);
        }

        return new ObjFace(corners);
    }

    private static int ParseIndex(
        string field,
        int count,
        string kind,
        string fileName,
        int lineNumber) {
        if (!int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            || parsed == 0) {
            throw new ModelReadException(
                $"OBJ source '{fileName}' line {lineNumber} has the invalid {kind} index '{field}'.");
        }

        int resolved = parsed > 0 ? parsed - 1 : count + parsed;
        if (resolved < 0 || resolved >= count) {
            throw new ModelReadException(
                $"OBJ source '{fileName}' line {lineNumber} references the {kind} index '{field}' "
                    + $"outside the {count} declared values.");
        }

        return resolved;
    }

    private static Vector3 ParsePosition(string rest, string fileName, int lineNumber) {
        float[] values = ParseFloats(rest, 3, "position", fileName, lineNumber);
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector3 ParseNormal(string rest, string fileName, int lineNumber) {
        float[] values = ParseFloats(rest, 3, "normal", fileName, lineNumber);
        Vector3 normal = new(values[0], values[1], values[2]);
        if (normal.LengthSquared() < 1e-12f) {
            return Vector3.UnitY;
        }

        return Vector3.Normalize(normal);
    }

    /// <summary>Parses one <c>vt</c> statement into the engine's top-left UV space.</summary>
    /// <remarks>
    /// Wavefront OBJ places the V origin at the bottom of the texture and the engine samples from a
    /// top-left origin, so V is flipped here: an authored V of 0 maps to the top of the texture and
    /// an authored V of 1 maps to the bottom. U is unchanged. A <c>vt</c> with only U means an
    /// authored V of 0, so it flips to 1 like any other.
    /// </remarks>
    private static Vector2 ParseTextureCoordinate(string rest, string fileName, int lineNumber) {
        float[] values = ParseFloats(rest, 1, "texture coordinate", fileName, lineNumber);
        float v = values.Length >= 2 ? values[1] : 0f;
        return new Vector2(values[0], 1f - v);
    }

    private static float[] ParseFloats(
        string rest,
        int minimum,
        string kind,
        string fileName,
        int lineNumber) {
        string[] tokens = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < minimum) {
            throw new ModelReadException(
                $"OBJ source '{fileName}' line {lineNumber} has too few {kind} components.");
        }

        var values = new float[tokens.Length];
        for (int index = 0; index < tokens.Length; index++) {
            if (!float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float value)
                || !float.IsFinite(value)) {
                throw new ModelReadException(
                    $"OBJ source '{fileName}' line {lineNumber} has the invalid {kind} value "
                        + $"'{tokens[index]}'.");
            }

            values[index] = value;
        }

        return values;
    }

    private static string StripComment(string line) {
        int comment = line.IndexOf('#');
        return comment < 0 ? line : line[..comment];
    }

    private readonly struct ObjCorner {
        internal ObjCorner(int position, int uv, int normal) {
            Position = position;
            Uv = uv;
            Normal = normal;
        }

        internal int Position { get; }

        internal int Uv { get; }

        internal int Normal { get; }
    }

    private sealed class ObjFace {
        internal ObjFace(ObjCorner[] corners) => Corners = corners;

        internal ObjCorner[] Corners { get; }
    }

    private sealed class ObjPartBuilder {
        internal ObjPartBuilder(string? name) => Name = name;

        internal string? Name { get; }

        internal List<ObjFace> Faces { get; } = [];
    }

    private readonly record struct VertexKey(int Position, int Uv, int Normal);
}
