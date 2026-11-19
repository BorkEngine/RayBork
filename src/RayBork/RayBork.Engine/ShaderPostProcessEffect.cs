using RayBork.Internal;

namespace RayBork;

/// <summary>
/// A custom single-pass camera post-process effect over a loaded <see cref="Shader"/>.
/// </summary>
/// <remarks>
/// The engine runs the shader as one fullscreen pass, binding the reserved <c>sourceColor</c> and
/// optional <c>sourceDepth</c> textures instead of the material values; other bindings are frozen
/// when the camera is created. The effect does not own or dispose the shader.
/// </remarks>
public sealed class ShaderPostProcessEffect : PostProcessEffect
{
    private const ShaderStageMask REQUIRED_STAGES =
        ShaderStageMask.Vertex | ShaderStageMask.Fragment;

    private readonly Shader _shader;

    /// <summary>Creates an effect over a loaded shader; the effect does not own it.</summary>
    public ShaderPostProcessEffect(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        _shader = shader;
    }

    /// <summary>Gets the shader the effect runs; the effect never disposes it.</summary>
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
