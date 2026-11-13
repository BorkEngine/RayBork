using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The built-in loader that composes one packed model descriptor into a caller-owned
/// <see cref="Model"/> over the shared cached part meshes and programs.
/// </summary>
/// <remarks>
/// <para>
/// A model is not an engine asset: it has no cache entry, no shared identity, and no lifetime of its
/// own. Every successful call resolves the packed descriptor and returns a new <see cref="Model"/> that
/// owns one material per part and one mesh reference per distinct part mesh. The part loads themselves
/// still use the shared engine cache, so repeated model loads share the underlying meshes and programs
/// without sharing the model or its materials.
/// </para>
/// <para>
/// The loader requests every part through the catalog's <see cref="AssetLoadContext"/>, so the loader
/// thread keeps serving the part loads while this load is incomplete. A failed or canceled load
/// releases every part mesh reference and material it already acquired.
/// </para>
/// <para>
/// Every part names a canonical shader path. The bake supplies the built-in <c>shaders/mesh</c>
/// default when the sidecar omits one; the runtime has no default and no legacy mapping, so a pack
/// with a null or stale path fails to load.
/// </para>
/// </remarks>
internal sealed class ModelAssetLoader : IAssetLoader<Model> {
    /// <summary>
    /// The shared loader instance.
    /// </summary>
    internal static readonly ModelAssetLoader Instance = new();

    private ModelAssetLoader() {
    }

    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Model;

    /// <inheritdoc/>
    public async Task<Model> LoadAsync(AssetLoadContext context) {
        ArgumentNullException.ThrowIfNull(context);

        string canonical = context.CanonicalPath;
        CancellationToken cancellationToken = context.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        ModelAssetDescriptor descriptor;
        try {
            byte[] descriptorPayload = await context.LoadPayloadAsync().ConfigureAwait(false);
            descriptor = AssetSerialization.Deserialize<ModelAssetDescriptor>(descriptorPayload);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"the model descriptor could not be decoded: {exception.Message}");
        }

        ValidateModelDescriptor(descriptor, canonical);

        List<Mesh> meshes = [];
        List<Shader> materials = [];
        try {
            ModelPart[] parts = new ModelPart[descriptor.Parts.Length];
            Dictionary<string, Mesh> meshesByPath = new(StringComparer.Ordinal);
            for (int index = 0; index < descriptor.Parts.Length; index++) {
                cancellationToken.ThrowIfCancellationRequested();
                ModelPartAsset part = descriptor.Parts[index];
                string meshPath = AssetPath.Normalize(part.MeshPath);
                if (!meshesByPath.TryGetValue(meshPath, out Mesh? mesh)) {
                    mesh = await context
                        .LoadAsync<Mesh>(meshPath, cancellationToken)
                        .ConfigureAwait(false);
                    meshesByPath.Add(meshPath, mesh);
                    meshes.Add(mesh);
                }

                cancellationToken.ThrowIfCancellationRequested();
                // Every part gets its own material even when two parts share a program path. The
                // program is cached and reference-counted, so the parts still share one GPU program.
                string shaderPath = part.ShaderPath;
                Shader material = await context
                    .LoadAsync<Shader>(shaderPath, cancellationToken)
                    .ConfigureAwait(false);
                materials.Add(material);

                parts[index] = new ModelPart(
                    part.Name,
                    mesh,
                    material,
                    renderState: null,
                    localTransform: CreateLocalTransform(part.LocalTransform));
            }

            cancellationToken.ThrowIfCancellationRequested();
            Model model = new(parts);
            if (cancellationToken.IsCancellationRequested) {
                // The loader stopped while the model was being constructed. The model already owns
                // its part references, so dispose it before reporting the cancellation instead of
                // abandoning a fully constructed model.
                model.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The model constructor acquired its own mesh references. This load's references are the
            // transfer mechanism and are released now, so the model remains the only owner.
            ReleaseMeshReferences(meshes);
            materials.Clear();
            return model;
        } catch {
            ReleaseMeshReferences(meshes);
            DisposeMaterials(materials);
            throw;
        }
    }

    private static Transform CreateLocalTransform(float[] local) => new(
        new Vector3(local[0], local[1], local[2]),
        new Quaternion(local[3], local[4], local[5], local[6]),
        new Vector3(local[7], local[8], local[9]));

    private static void ReleaseMeshReferences(List<Mesh> meshes) {
        foreach (Mesh mesh in meshes) {
            try {
                mesh.Lifetime.ReleaseOwnerReference();
            } catch (ObjectDisposedException) {
                // Shutdown force-disposed the part after the load started.
            }
        }

        meshes.Clear();
    }

    private static void DisposeMaterials(List<Shader> materials) {
        foreach (Shader material in materials) {
            try {
                material.Dispose();
            } catch (ObjectDisposedException) {
                // Shutdown force-disposed the program or material after the load started.
            }
        }

        materials.Clear();
    }

    private static void ValidateModelDescriptor(ModelAssetDescriptor descriptor, string canonical) {
        if (descriptor.Parts is null || descriptor.Parts.Length == 0) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the model descriptor must carry at least one part");
        }

        ValidateBounds(descriptor.Bounds, canonical, "the model");
        foreach (ModelPartAsset? part in descriptor.Parts) {
            if (part is null) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, "the model descriptor contains a null part");
            }

            if (string.IsNullOrWhiteSpace(part.Name)) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, "a model part has an empty name");
            }

            if (!AssetPath.TryValidateCanonical(part.MeshPath, out string? meshReason)) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"the mesh path '{part.MeshPath}' of model part '{part.Name}' is not canonical: "
                        + meshReason);
            }

            if (string.IsNullOrWhiteSpace(part.ShaderPath)) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, $"model part '{part.Name}' has no shader path");
            }

            if (!AssetPath.TryValidateCanonical(part.ShaderPath, out string? shaderReason)) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"the shader path '{part.ShaderPath}' of model part '{part.Name}' is not canonical: "
                        + shaderReason);
            }

            if (part.LocalTransform is null || part.LocalTransform.Length != 10) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"model part '{part.Name}' must carry exactly ten local-transform floats "
                        + "(position XYZ, rotation XYZW, and scale XYZ)");
            }

            foreach (float component in part.LocalTransform) {
                if (!float.IsFinite(component)) {
                    throw AssetLoadErrors.CreateLoadFailure(
                        canonical,
                        $"model part '{part.Name}' has a non-finite local-transform component");
                }
            }

            ValidateBounds(part.Bounds, canonical, $"model part '{part.Name}'");
        }
    }

    private static void ValidateBounds(float[]? bounds, string canonical, string owner) {
        if (bounds is null || bounds.Length != 6) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"{owner} must carry exactly six bounds floats (minimum XYZ, maximum XYZ)");
        }

        for (int index = 0; index < bounds.Length; index++) {
            if (!float.IsFinite(bounds[index])) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, $"{owner} has a non-finite bounds component");
            }
        }

        if (bounds[0] > bounds[3] || bounds[1] > bounds[4] || bounds[2] > bounds[5]) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, $"{owner} bounds minimum must not exceed the maximum");
        }
    }
}
