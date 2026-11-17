using RayBork.Internal;

namespace RayBork;

/// <summary>
/// A custom single-pass camera post-process effect over a loaded <see cref="Shader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine draws the shader as one fullscreen pass over its fullscreen quad after the camera's
/// geometry pass. The shader is an ordinary baked shader with the standard shader ABI and one custom
/// vertex-stage contract: the engine quad has positions in <c>[-0.5, 0.5]</c> and UVs in
/// <c>[0, 1]</c> with <c>(0, 0)</c> at the image top-left, and the vertex stage maps it with
/// <c>vUv = aUv; gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);</c>. The fragment stage writes
/// scene-linear HDR output; the engine does not apply AgX or gamma to the post chain.
/// </para>
/// <para>
/// The shader sidecar must declare the reserved <c>sourceColor</c> texture binding, which the engine
/// binds to the camera target's current resolved color (or to the previous effect's output in a chain).
/// It may declare the optional <c>sourceDepth</c> binding, which the engine binds to the target's
/// resolved reverse-Z depth; a target without depth rejects it. The engine overrides the material's
/// values for both reserved names. Every other uniform and texture binding comes from the shader
/// material and is frozen when the camera is created.
/// </para>
/// <para>
/// The shader's baked default render state is ignored: the post pass forces no depth test, no depth
/// write, no culling, and no blending. The effect, its <see cref="PostProcessStack"/>, and this object
/// do not own the shader and never dispose it. Chaining effects in the stack gives multi-pass
/// post-processing.
/// </para>
/// </remarks>
public sealed class ShaderPostProcessEffect : PostProcessEffect
{
    private const ShaderStageMask REQUIRED_STAGES =
        ShaderStageMask.Vertex | ShaderStageMask.Fragment;

    private readonly Shader _shader;

    /// <summary>
    /// Creates one custom post-process effect over a loaded shader. The effect does not own the
    /// shader; dispose the shader separately.
    /// </summary>
    /// <param name="shader">
    /// The baked shader program. Its sidecar must declare the reserved <c>sourceColor</c> texture
    /// binding and may declare <c>sourceDepth</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is <see langword="null"/>.</exception>
    public ShaderPostProcessEffect(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        _shader = shader;
    }

    /// <summary>
    /// Gets the shader the effect runs. The effect does not own it and never disposes it.
    /// </summary>
    public Shader Shader => _shader;

    /// <inheritdoc/>
    internal override PostProcessEffectSnapshot Capture(PostProcessCaptureContext context)
    {
        Shader shader = _shader;
        ObjectDisposedException.ThrowIf(shader.IsDisposed, shader);

        ShaderProgram program = shader.Program;
        if ((program.Stages & REQUIRED_STAGES) != REQUIRED_STAGES)
        {
            throw new InvalidOperationException(
                $"The post-process shader '{shader.Path}' must declare both the vertex and fragment "
                    + $"stages (it declares {program.Stages}). Add a vertex stage that maps the engine "
                    + "fullscreen quad and a fragment stage that writes the post-processed color."
            );
        }

        if (
            !program.TexturesByName.TryGetValue(
                PostProcessNames.SOURCE_COLOR,
                out ShaderAbiTexture sourceColor
            )
        )
        {
            throw new InvalidOperationException(
                $"The post-process shader '{shader.Path}' does not declare the required reserved "
                    + $"'{PostProcessNames.SOURCE_COLOR}' texture binding. Declare it in the shader sidecar "
                    + "so the engine can bind the camera target's current color to it."
            );
        }

        int sourceDepthBinding = -1;
        if (
            program.TexturesByName.TryGetValue(
                PostProcessNames.SOURCE_DEPTH,
                out ShaderAbiTexture sourceDepth
            )
        )
        {
            if (!context.TargetHasDepth)
            {
                throw new InvalidOperationException(
                    $"The post-process shader '{shader.Path}' declares the reserved "
                        + $"'{PostProcessNames.SOURCE_DEPTH}' texture binding, but {context.TargetDescription} "
                        + "has no depth attachment to bind to it. Use a target created with Depth: true, or "
                        + "remove the sourceDepth binding."
                );
            }

            sourceDepthBinding = sourceDepth.Slot;
        }

        ReadOnlySpan<Texture?> textures = shader.TextureValues;
        for (int index = 0; index < textures.Length; index++)
        {
            if (index == sourceColor.Slot || index == sourceDepthBinding)
            {
                // The engine overrides the reserved bindings, so a material value there is ignored.
                continue;
            }

            Texture? texture = textures[index];
            if (texture is not null)
            {
                ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
            }
        }

        return ShaderPostProcessEffectSnapshot.Rent(
            this,
            program,
            shader.InstanceData,
            textures,
            sourceColor.Slot,
            sourceDepthBinding,
            context.TargetHasDepth,
            context.TargetDescription
        );
    }
}
