using System.Numerics;
using System.Text.Json;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using GltfMesh = SharpGLTF.Schema2.Mesh;

namespace RayBork.Bake.Models;

/// <summary>
/// Reads a static glTF 2.0 or GLB source into parts with their composed node transforms.
/// </summary>
/// <remarks>
/// <para>
/// The default scene is flattened depth-first. Every mesh primitive becomes one part; a mesh with
/// several primitives appends the one-based primitive index to the part name. The part transform is
/// the node's composed world matrix, decomposed to position, rotation, and scale. When a node matrix
/// contains shear and cannot be represented as TRS, the matrix is baked into the mesh, the part
/// transform becomes identity, and a warning is reported.
/// </para>
/// <para>
/// Skins and morph targets are rejected because their rest-pose geometry is ambiguous without an
/// animation system. Animations, cameras, and lights are non-mesh data and are ignored with one
/// warning each. External <c>.gltf</c> buffers and images are tracked inputs when they exist; a
/// missing buffer is an error and a missing image is a warning.
/// </para>
/// </remarks>
internal static class GltfModelReader {
    /// <summary>Reads one glTF or GLB source.</summary>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="warning">The warning sink.</param>
    /// <param name="addInput">The tracked-input sink.</param>
    /// <returns>The non-empty part list.</returns>
    /// <exception cref="ModelReadException">The source is invalid or contains unsupported data.</exception>
    internal static IReadOnlyList<ModelPartSource> Read(
        string sourcePath,
        Action<string> warning,
        Action<string> addInput) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(warning);
        ArgumentNullException.ThrowIfNull(addInput);

        ModelRoot root;
        try {
            root = ModelRoot.Load(sourcePath);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            throw new ModelReadException(
                $"The glTF source '{Path.GetFileName(sourcePath)}' could not be loaded: "
                    + $"{exception.Message}");
        }

        try {
            ValidateSupportedFeatures(root, warning);
            RegisterExternalInputs(sourcePath, warning, addInput);
            return CollectParts(root, warning);
        } catch (ModelReadException) {
            throw;
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            throw new ModelReadException(
                $"The glTF source '{Path.GetFileName(sourcePath)}' could not be converted: "
                    + $"{exception.Message}");
        }
    }

    private static void ValidateSupportedFeatures(ModelRoot root, Action<string> warning) {
        if (root.LogicalSkins.Count > 0) {
            throw new ModelReadException(
                $"The glTF source declares {root.LogicalSkins.Count} skin(s); skinned meshes are not "
                    + "supported because the rest pose cannot be baked without animation.");
        }

        if (root.LogicalAnimations.Count > 0) {
            warning(
                $"The glTF source declares {root.LogicalAnimations.Count} animation(s); animations are "
                    + "ignored and only the static rest pose is baked.");
        }

        if (root.LogicalCameras.Count > 0) {
            warning(
                $"The glTF source declares {root.LogicalCameras.Count} camera(s); cameras are ignored.");
        }

        if (root.LogicalPunctualLights.Count > 0) {
            warning(
                $"The glTF source declares {root.LogicalPunctualLights.Count} light(s); lights are "
                    + "ignored.");
        }

        if (root.LogicalScenes.Count > 1) {
            warning(
                $"The glTF source declares {root.LogicalScenes.Count} scenes; only the default scene "
                    + "is baked.");
        }
    }

    private static IReadOnlyList<ModelPartSource> CollectParts(
        ModelRoot root,
        Action<string> warning) {
        var parts = new List<ModelPartSource>();
        Scene? scene = root.DefaultScene ?? root.LogicalScenes.FirstOrDefault();
        if (scene is not null) {
            foreach (Node node in scene.VisualChildren) {
                CollectNode(node, parts, warning);
            }
        } else {
            foreach (Node node in root.LogicalNodes) {
                if (node.VisualParent is null) {
                    CollectNode(node, parts, warning);
                }
            }
        }

        if (parts.Count == 0) {
            throw new ModelReadException(
                "The glTF source contains no mesh primitives in its default scene.");
        }

        return parts;
    }

    private static void CollectNode(
        Node node,
        List<ModelPartSource> parts,
        Action<string> warning) {
        if (node.Skin is not null) {
            throw new ModelReadException(
                $"The glTF node '{node.Name}' is skinned; skinned meshes are not supported.");
        }

        if (node.Mesh is not null) {
            GltfMesh mesh = node.Mesh;
            if (mesh.Primitives.Count == 0) {
                warning($"The glTF mesh '{mesh.Name}' has no primitives and is ignored.");
            }

            if (mesh.MorphWeights.Count > 0) {
                throw new ModelReadException(
                    $"The glTF mesh '{mesh.Name}' declares morph weights; morph targets are not "
                        + "supported.");
            }

            string meshName = string.IsNullOrWhiteSpace(mesh.Name) ? string.Empty : mesh.Name;
            string nodeName = string.IsNullOrWhiteSpace(node.Name) ? string.Empty : node.Name;
            string baseName = meshName.Length > 0
                ? meshName
                : nodeName.Length > 0 ? nodeName : "part";
            int primitiveCount = mesh.Primitives.Count;
            foreach (MeshPrimitive primitive in mesh.Primitives) {
                if (primitive.MorphTargetsCount > 0) {
                    throw new ModelReadException(
                        $"The glTF primitive '{baseName}' declares {primitive.MorphTargetsCount} morph "
                            + "target(s); morph targets are not supported.");
                }

                string partName = primitiveCount == 1
                    ? baseName
                    : $"{baseName}-{primitive.LogicalIndex + 1}";
                parts.Add(new ModelPartSource(
                    partName,
                    ReadPrimitive(primitive, partName),
                    node.WorldMatrix));
            }
        }

        foreach (Node child in node.VisualChildren) {
            CollectNode(child, parts, warning);
        }
    }

    private static MeshData ReadPrimitive(MeshPrimitive primitive, string partName) {
        Accessor? positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor is null || positionAccessor.Count == 0) {
            throw new ModelReadException(
                $"The glTF primitive '{partName}' has no POSITION attribute.");
        }

        int vertexCount = positionAccessor.Count;
        IAccessorArray<Vector3> positions = positionAccessor.AsVector3Array();
        IAccessorArray<Vector3>? normals = ReadAttribute(primitive, "NORMAL", vertexCount)?.AsVector3Array();
        IAccessorArray<Vector2>? uv0 = ReadAttribute(primitive, "TEXCOORD_0", vertexCount)?.AsVector2Array();
        IAccessorArray<Vector2>? uv1 = ReadAttribute(primitive, "TEXCOORD_1", vertexCount)?.AsVector2Array();
        IAccessorArray<Vector2>? uv2 = ReadAttribute(primitive, "TEXCOORD_2", vertexCount)?.AsVector2Array();
        IAccessorArray<Vector2>? uv3 = ReadAttribute(primitive, "TEXCOORD_3", vertexCount)?.AsVector2Array();
        IAccessorArray<Vector4>? colors = ReadAttribute(primitive, "COLOR_0", vertexCount)?.AsColorArray(1f);
        IAccessorArray<Vector4>? tangents = ReadAttribute(primitive, "TANGENT", vertexCount)?.AsVector4Array();

        var mesh = new MeshData(MapTopology(primitive.DrawPrimitiveType, partName));
        for (int index = 0; index < vertexCount; index++) {
            MeshVertex vertex = MeshVertex.Create(positions[index]);
            if (normals is not null) {
                Vector3 normal = normals[index];
                if (!IsFinite(normal)) {
                    throw new ModelReadException(
                        $"The glTF primitive '{partName}' has a non-finite normal.");
                }

                vertex.Normal = normal.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(normal);
            }

            if (uv0 is not null) {
                vertex.Uv = RequireFinite(uv0[index], partName, "texture coordinate");
            }

            if (uv1 is not null) {
                vertex.Uv1 = RequireFinite(uv1[index], partName, "texture coordinate 1");
            }

            if (uv2 is not null) {
                vertex.Uv2 = RequireFinite(uv2[index], partName, "texture coordinate 2");
            }

            if (uv3 is not null) {
                vertex.Uv3 = RequireFinite(uv3[index], partName, "texture coordinate 3");
            }

            if (colors is not null) {
                vertex.Color = RequireFinite(colors[index], partName, "color");
            }

            if (tangents is not null) {
                Vector4 tangent = tangents[index];
                if (!IsFinite(tangent)) {
                    throw new ModelReadException(
                        $"The glTF primitive '{partName}' has a non-finite tangent.");
                }

                vertex.Tangent = tangent;
            }

            if (!IsFinite(vertex.Position)) {
                throw new ModelReadException(
                    $"The glTF primitive '{partName}' has a non-finite position.");
            }

            mesh.Vertices.Add(vertex);
        }

        IList<uint>? sourceIndices = primitive.GetIndices();
        if (sourceIndices is null) {
            for (uint index = 0; index < (uint)vertexCount; index++) {
                mesh.Indices.Add(index);
            }
        } else {
            mesh.Indices.AddRange(sourceIndices);
        }

        ConvertTopology(primitive.DrawPrimitiveType, mesh, partName);
        ValidateIndices(mesh, partName);
        return mesh;
    }

    private static Accessor? ReadAttribute(
        MeshPrimitive primitive,
        string attribute,
        int vertexCount) {
        Accessor? accessor = primitive.GetVertexAccessor(attribute);
        if (accessor is not null && accessor.Count != vertexCount) {
            throw new ModelReadException(
                $"The glTF primitive attribute '{attribute}' has {accessor.Count} values, but "
                    + $"POSITION has {vertexCount}.");
        }

        return accessor;
    }

    private static PrimitiveTopology MapTopology(PrimitiveType type, string partName) {
        switch (type) {
            case PrimitiveType.POINTS:
                return PrimitiveTopology.PointList;

            case PrimitiveType.LINES:
                return PrimitiveTopology.LineList;

            case PrimitiveType.LINE_LOOP:
            case PrimitiveType.LINE_STRIP:
                return PrimitiveTopology.LineStrip;

            case PrimitiveType.TRIANGLES:
            case PrimitiveType.TRIANGLE_FAN:
                return PrimitiveTopology.TriangleList;

            case PrimitiveType.TRIANGLE_STRIP:
                return PrimitiveTopology.TriangleStrip;

            default:
                throw new ModelReadException(
                    $"The glTF primitive '{partName}' uses the unsupported mode '{type}'.");
        }
    }

    private static void ConvertTopology(
        PrimitiveType type,
        MeshData mesh,
        string partName) {
        switch (type) {
            case PrimitiveType.TRIANGLE_FAN:
                mesh.Indices.Clear();
                mesh.Indices.AddRange(FanToTriangleList(mesh, partName));
                break;

            case PrimitiveType.LINE_LOOP:
                if (mesh.Indices.Count >= 2) {
                    mesh.Indices.Add(mesh.Indices[0]);
                }

                break;
        }
    }

    private static IReadOnlyList<uint> FanToTriangleList(MeshData mesh, string partName) {
        if (mesh.Indices.Count < 3) {
            throw new ModelReadException(
                $"The glTF triangle-fan primitive '{partName}' has fewer than three indices.");
        }

        var result = new List<uint>((mesh.Indices.Count - 2) * 3);
        for (int index = 1; index + 1 < mesh.Indices.Count; index++) {
            result.Add(mesh.Indices[0]);
            result.Add(mesh.Indices[index]);
            result.Add(mesh.Indices[index + 1]);
        }

        return result;
    }

    private static void ValidateIndices(MeshData mesh, string partName) {
        int vertexCount = mesh.Vertices.Count;
        foreach (uint index in mesh.Indices) {
            if (index >= (uint)vertexCount) {
                throw new ModelReadException(
                    $"The glTF primitive '{partName}' has the index {index} outside its "
                        + $"{vertexCount} vertices.");
            }
        }

        int indexCount = mesh.Indices.Count;
        bool valid = mesh.Topology switch {
            PrimitiveTopology.TriangleList => indexCount >= 3 && indexCount % 3 == 0,
            PrimitiveTopology.TriangleStrip => indexCount >= 3,
            PrimitiveTopology.LineList => indexCount >= 2 && indexCount % 2 == 0,
            PrimitiveTopology.LineStrip => indexCount >= 2,
            PrimitiveTopology.PointList => indexCount >= 1,
            _ => false,
        };

        if (!valid) {
            throw new ModelReadException(
                $"The glTF primitive '{partName}' has {indexCount} indices, which is invalid for "
                    + $"the topology {mesh.Topology}.");
        }
    }

    private static void RegisterExternalInputs(
        string sourcePath,
        Action<string> warning,
        Action<string> addInput) {
        if (!sourcePath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        string directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var bufferUris = ReadExternalUris(sourcePath, "buffers");
        var imageUris = ReadExternalUris(sourcePath, "images");
        if (bufferUris.Count == 0 && imageUris.Count == 0) {
            return;
        }

        foreach (string uri in bufferUris) {
            string fullPath = ResolveUri(directory, uri);
            if (!File.Exists(fullPath)) {
                throw new ModelReadException(
                    $"The glTF source references the external buffer '{uri}', which does not exist.");
            }

            addInput(fullPath);
        }

        foreach (string uri in imageUris) {
            string fullPath = ResolveUri(directory, uri);
            if (File.Exists(fullPath)) {
                addInput(fullPath);
                warning(
                    $"The glTF source references the image '{uri}'; glTF materials and images are "
                        + "not imported in M3.");
            } else {
                warning(
                    $"The glTF source references the image '{uri}', which does not exist; the image "
                        + "is not imported.");
            }
        }
    }

    private static List<string> ReadExternalUris(string sourcePath, string propertyName) {
        var uris = new List<string>();
        string json;
        try {
            json = File.ReadAllText(sourcePath);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            throw new ModelReadException(
                $"The glTF source '{Path.GetFileName(sourcePath)}' could not be read: "
                    + $"{exception.Message}");
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array) {
            return uris;
        }

        foreach (JsonElement element in array.EnumerateArray()) {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("uri", out JsonElement uriElement)
                && uriElement.ValueKind == JsonValueKind.String) {
                string? uri = uriElement.GetString();
                if (!string.IsNullOrWhiteSpace(uri)
                    && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) {
                    uris.Add(uri);
                }
            }
        }

        return uris;
    }

    private static string ResolveUri(string directory, string uri) {
        string decoded = Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(directory, decoded));
    }

    private static Vector2 RequireFinite(Vector2 value, string partName, string attribute) {
        if (!IsFinite(value)) {
            throw new ModelReadException(
                $"The glTF primitive '{partName}' has a non-finite {attribute}.");
        }

        return value;
    }

    private static Vector4 RequireFinite(Vector4 value, string partName, string attribute) {
        if (!IsFinite(value)) {
            throw new ModelReadException(
                $"The glTF primitive '{partName}' has a non-finite {attribute}.");
        }

        return value;
    }

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
