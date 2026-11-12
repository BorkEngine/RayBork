using System.Numerics;
using RayBork.Bake.Models;
using RayBork.Internal;

namespace RayBork.Bake.Processors;

/// <summary>
/// The built-in model processor: converts glTF 2.0 and OBJ sources into a
/// <see cref="ModelAssetDescriptor"/> plus one <see cref="MeshAssetDescriptor"/> and mesh payload per
/// part.
/// </summary>
/// <remarks>
/// <para>
/// The optional <c>[model]</c> sidecar supplies a positive uniform <c>scale</c>, the source's
/// <c>front</c> axis, and an optional canonical <c>shader</c> path. <c>front</c> is the source's
/// forward direction; the processor applies the rotation that maps it to RayBork's <c>-Z</c> forward
/// direction. The exact axis table is documented on <see cref="ModelOrientation"/>: <c>-z</c> is
/// identity, <c>+z</c> is 180 degrees about <c>+Y</c>, <c>+x</c> is +90 degrees about <c>+Y</c>,
/// <c>-x</c> is -90 degrees about <c>+Y</c>, <c>+y</c> is -90 degrees about <c>+X</c>, and
/// <c>-y</c> is +90 degrees about <c>+X</c>.
/// </para>
/// <para>
/// The scale and rotation correct the mesh geometry, normals, and tangents before bounds and payloads
/// are written. Node-chain transforms remain on each part as ten floats (position, rotation
/// quaternion, scale), so <c>world = part.LocalTransform.ToMatrix() * instanceTransform.ToMatrix()</c>
/// reproduces the authored placement. Part and payload paths are canonical and deterministic;
/// duplicate names receive numeric suffixes. A part with no <c>[model] shader</c> defaults to the
/// built-in canonical path <c>shaders/mesh</c>. Every error is <c>RB4009</c> and emits no outputs.
/// </para>
/// </remarks>
internal sealed class ModelProcessor : AssetProcessor<ModelSidecar> {
    private const float DEFAULT_SCALE = 1f;

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
    protected override ModelSidecar CreateDefaultSidecar(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return new ModelSidecar();
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, ModelSidecar sidecar) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        if (!TryResolveSettings(context, sidecar, out float scale, out Quaternion orientation,
                out string shaderPath)) {
            return;
        }

        IReadOnlyList<ModelPartSource> parts;
        try {
            parts = ModelReader.Read(
                context.SourcePath,
                context.SourceFileNameWithoutExtension,
                message => context.Diagnostics.Warning(ModelDiagnosticCodes.MODEL_INVALID, message),
                context.AddInput);
        } catch (ModelReadException exception) {
            context.Diagnostics.Error(ModelDiagnosticCodes.MODEL_INVALID, exception.Message);
            return;
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' could not be read: {exception.Message}");
            return;
        }

        if (!PartSlugResolver.TryCreate(context.BaseAssetPath, out PartSlugResolver? resolver,
                out string? pathReason)
            || resolver is null) {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' cannot produce part paths: {pathReason}");
            return;
        }

        try {
            Emit(context, parts, resolver, scale, orientation, shaderPath);
        } catch (ModelReadException exception) {
            context.Diagnostics.Error(ModelDiagnosticCodes.MODEL_INVALID, exception.Message);
        }
    }

    private static void Emit(
        AssetProcessorContext context,
        IReadOnlyList<ModelPartSource> parts,
        PartSlugResolver resolver,
        float scale,
        Quaternion orientation,
        string shaderPath) {
        var partAssets = new List<ModelPartAsset>(parts.Count);
        var partBounds = new List<float[]>(parts.Count);

        foreach (ModelPartSource part in parts) {
            string slug = resolver.Allocate(part.Name, out string meshPath, out string payloadPath);
            MeshData mesh = ModelGeometryTransform.ApplyAuthoring(part.Mesh, orientation, scale);
            float[] meshBounds;
            float[] bounds;
            float[] localTransform;
            if (ModelGeometryTransform.TryDecompose(part.LocalTransform, out float[] decomposed)) {
                localTransform = decomposed;
                meshBounds = MeshBounds.FromVertices(mesh.Vertices);
                bounds = MeshBounds.FromTransformedVertices(mesh.Vertices, part.LocalTransform);
            } else {
                context.Diagnostics.Warning(
                    ModelDiagnosticCodes.MODEL_INVALID,
                    $"Model part '{slug}' has a node transform that cannot be represented as "
                        + "position, rotation, and scale; the transform is baked into its mesh.");
                mesh = ModelGeometryTransform.ApplyToModelSpace(mesh, part.LocalTransform);
                localTransform = ModelGeometryTransform.IdentityTransform();
                meshBounds = MeshBounds.FromVertices(mesh.Vertices);
                bounds = meshBounds;
            }

            byte[] payload = VertexLayoutWriter.Write(mesh);
            context.Outputs.AddData(payloadPath, payload, alignment: 16);
            context.Outputs.AddAsset(meshPath, new MeshAssetDescriptor {
                Payload = payloadPath,
                VertexCount = (uint)mesh.Vertices.Count,
                IndexCount = (uint)mesh.Indices.Count,
                Bounds = meshBounds,
                Topology = mesh.Topology,
            });

            partAssets.Add(new ModelPartAsset {
                Name = slug,
                MeshPath = meshPath,
                ShaderPath = shaderPath,
                LocalTransform = localTransform,
                Bounds = bounds,
            });
            partBounds.Add(bounds);
        }

        context.Outputs.AddAsset(context.BaseAssetPath, new ModelAssetDescriptor {
            Parts = [.. partAssets],
            Bounds = MeshBounds.Union(partBounds),
        });
    }

    private static bool TryResolveSettings(
        AssetProcessorContext context,
        ModelSidecar sidecar,
        out float scale,
        out Quaternion orientation,
        out string shaderPath) {
        scale = sidecar.Scale ?? DEFAULT_SCALE;
        orientation = Quaternion.Identity;
        shaderPath = sidecar.Shader ?? DEFAULT_SHADER_PATH;

        if (!float.IsFinite(scale) || scale <= 0f) {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' declares scale {scale}; it must be a "
                    + "positive finite number.");
            return false;
        }

        if (!ModelOrientation.TryResolve(
                sidecar.Front ?? ModelOrientation.DEFAULT_FRONT,
                out orientation,
                out string? frontReason)) {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' has an invalid front axis: {frontReason}.");
            return false;
        }

        if (!AssetPath.TryValidateCanonical(shaderPath, out string? shaderReason)) {
            context.Diagnostics.Error(
                ModelDiagnosticCodes.MODEL_INVALID,
                $"Model source '{context.RelativePath}' declares the shader path "
                    + $"'{shaderPath}', which is not a canonical asset path without a leading "
                    + $"slash: {shaderReason}.");
            return false;
        }

        return true;
    }
}
