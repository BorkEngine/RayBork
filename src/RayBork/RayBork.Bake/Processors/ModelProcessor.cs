using System.Numerics;
using RayBork.Bake.Models;
using RayBork.Internal;

namespace RayBork.Bake.Processors;

/// <summary>The built-in model processor: converts glTF 2.0 and OBJ sources into a
/// <see cref="ModelAssetDescriptor"/> and one <see cref="MeshAssetDescriptor"/> per part.</summary>
/// <remarks>
/// The optional <c>[model]</c> sidecar sets <c>scale</c>, the <c>front</c> axis (rotated onto
/// <c>-Z</c>), a <c>shader</c> path (default <c>shaders/mesh</c>), and <c>collisions</c>. A
/// collision payload is the sibling <c>&lt;mesh path&gt;-collision</c> entry, and every error
/// is <c>RB4009</c> with no outputs.
/// </remarks>
internal sealed class ModelProcessor : AssetProcessor<ModelSidecar>
{
    private const float DEFAULT_SCALE = 1f;

    /// <summary>The collision payload size above which the processor warns, in bytes.</summary>
    internal const int COLLISION_PAYLOAD_WARNING_BYTES = 4 * 1024 * 1024;

    /// <summary>The built-in shader path a part uses when the sidecar names no shader.</summary>
    internal const string DEFAULT_SHADER_PATH = "shaders/mesh";

    private static readonly string[] _extensions = ["gltf", "glb", "obj"];

    /// <inheritdoc/>
    public override string Name => "model";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public override SidecarPolicy Policy => SidecarPolicy.Optional;

    /// <inheritdoc/>
    protected override ModelSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ModelSidecar();
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, ModelSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        if (
            !TryResolveSettings(
                context,
                sidecar,
                out float scale,
                out Quaternion orientation,
                out string shaderPath
            )
        )
        {
            return;
        }

        IReadOnlyList<ModelPartSource> parts;
        try
        {
            parts = ModelReader.Read(
                context.SourcePath,
                context.SourceFileNameWithoutExtension,
                message => context.Diagnostics.Warning(ModelDiagnosticCodes.MODEL_INVALID, message),
                context.AddInput
            );
        }
        catch (ModelReadException exception)
        {
            context.Diagnostics.Error(ModelDiagnosticCodes.MODEL_INVALID, exception.Message);
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' could not be read: {exception.Message}"
            );
            return;
        }

        if (
            !PartSlugResolver.TryCreate(
                context.BaseAssetPath,
                out PartSlugResolver? resolver,
                out string? pathReason
            ) || resolver is null
        )
        {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' cannot produce part paths: {pathReason}"
            );
            return;
        }

        try
        {
            Emit(
                context,
                parts,
                resolver,
                scale,
                orientation,
                shaderPath,
                sidecar.Collisions ?? true
            );
        }
        catch (ModelReadException exception)
        {
            context.Diagnostics.Error(ModelDiagnosticCodes.MODEL_INVALID, exception.Message);
        }
    }

    private static void Emit(
        AssetProcessorContext context,
        IReadOnlyList<ModelPartSource> parts,
        PartSlugResolver resolver,
        float scale,
        Quaternion orientation,
        string shaderPath,
        bool generateCollisions
    )
    {
        var partAssets = new List<ModelPartAsset>(parts.Count);
        var partBounds = new List<float[]>(parts.Count);

        foreach (ModelPartSource part in parts)
        {
            string slug = resolver.Allocate(
                part.Name,
                out string meshPath,
                out string payloadPath,
                out string collisionPath
            );
            MeshData mesh = ModelGeometryTransform.ApplyAuthoring(part.Mesh, orientation, scale);
            float[] meshBounds;
            float[] bounds;
            float[] localTransform;
            if (ModelGeometryTransform.TryDecompose(part.LocalTransform, out float[] decomposed))
            {
                localTransform = decomposed;
                meshBounds = MeshBounds.FromVertices(mesh.Vertices);
                bounds = MeshBounds.FromTransformedVertices(mesh.Vertices, part.LocalTransform);
            }
            else
            {
                context.Diagnostics.Warning(
                    ModelDiagnosticCodes.MODEL_INVALID,
                    $"Model part '{slug}' has a node transform that cannot be represented as "
                        + "position, rotation, and scale; the transform is baked into its mesh."
                );
                mesh = ModelGeometryTransform.ApplyToModelSpace(mesh, part.LocalTransform);
                localTransform = ModelGeometryTransform.IdentityTransform();
                meshBounds = MeshBounds.FromVertices(mesh.Vertices);
                bounds = meshBounds;
            }

            byte[] payload = VertexLayoutWriter.Write(mesh);
            context.Outputs.AddData(payloadPath, payload, alignment: 16);

            string? collisionEntryPath = null;
            if (generateCollisions && IsTriangleTopology(mesh.Topology))
            {
                MeshCollisionData? collision = BuildCollision(mesh);
                if (collision is not null)
                {
                    byte[] collisionPayload = MeshCollisionCodec.Encode(collision);
                    context.Outputs.AddData(collisionPath, collisionPayload, alignment: 16);
                    collisionEntryPath = collisionPath;
                    if (collisionPayload.Length > COLLISION_PAYLOAD_WARNING_BYTES)
                    {
                        context.Diagnostics.Warning(
                            ModelDiagnosticCodes.MODEL_INVALID,
                            $"Model part '{slug}' baked a {collisionPayload.Length}-byte ray-mesh "
                                + $"collision payload at '{collisionPath}'; set 'collisions = false' "
                                + "in the [model] sidecar to skip collision baking for this source."
                        );
                    }
                }
            }

            context.Outputs.AddAsset(
                meshPath,
                new MeshAssetDescriptor
                {
                    Payload = payloadPath,
                    CollisionPayload = collisionEntryPath,
                    VertexCount = (uint)mesh.Vertices.Count,
                    IndexCount = (uint)mesh.Indices.Count,
                    Bounds = meshBounds,
                    Topology = mesh.Topology,
                }
            );

            partAssets.Add(
                new ModelPartAsset
                {
                    Name = slug,
                    MeshPath = meshPath,
                    ShaderPath = shaderPath,
                    LocalTransform = localTransform,
                    Bounds = bounds,
                }
            );
            partBounds.Add(bounds);
        }

        context.Outputs.AddAsset(
            context.BaseAssetPath,
            new ModelAssetDescriptor
            {
                Parts = [.. partAssets],
                Bounds = MeshBounds.Union(partBounds),
            }
        );
    }

    private static bool IsTriangleTopology(PrimitiveTopology topology) =>
        topology is PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip;

    private static MeshCollisionData? BuildCollision(MeshData mesh)
    {
        Vector3[] positions = new Vector3[mesh.Vertices.Count];
        for (int index = 0; index < positions.Length; index++)
        {
            positions[index] = mesh.Vertices[index].Position;
        }

        uint[] indices = [.. mesh.Indices];
        return MeshCollisionBuilder.Build(positions, indices, mesh.Topology);
    }

    private static bool TryResolveSettings(
        AssetProcessorContext context,
        ModelSidecar sidecar,
        out float scale,
        out Quaternion orientation,
        out string shaderPath
    )
    {
        scale = sidecar.Scale ?? DEFAULT_SCALE;
        orientation = Quaternion.Identity;
        shaderPath = sidecar.Shader ?? DEFAULT_SHADER_PATH;

        if (!float.IsFinite(scale) || scale <= 0f)
        {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' declares scale {scale}; it must be a "
                    + "positive finite number."
            );
            return false;
        }

        if (
            !ModelOrientation.TryResolve(
                sidecar.Front ?? ModelOrientation.DEFAULT_FRONT,
                out orientation,
                out string? frontReason
            )
        )
        {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' has an invalid front axis: {frontReason}."
            );
            return false;
        }

        if (!AssetPath.TryValidateCanonical(shaderPath, out string? shaderReason))
        {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' declares the shader path "
                    + $"'{shaderPath}', which is not a canonical asset path without a leading "
                    + $"slash: {shaderReason}."
            );
            return false;
        }

        return true;
    }
}
