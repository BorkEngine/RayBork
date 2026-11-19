using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>Resolves a public <see cref="Texture"/> binding to its bindless slot.</summary>
/// <remarks>A <see langword="null"/> binding uses the white slot. Render-thread only.</remarks>
internal static class VulkanTextureBinding
{
    /// <summary>Resolves one texture binding to its bindless slot.</summary>
    /// <exception cref="GraphicsException">
    /// The texture is not a Vulkan texture or has no bindless slot yet (<c>RB3001</c>).
    /// </exception>
    internal static uint ResolveSlot(Texture? texture)
    {
        if (texture is null)
        {
            return VulkanBindlessTextureTable.WHITE_SLOT;
        }

        ITextureBindingResource? binding = texture.BindingResource;
        if (binding is null)
        {
            throw new GraphicsException(
                $"{Describe(texture)} is not a Vulkan GPU texture and cannot be sampled by a shader.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        if (binding.BindlessSlot == 0)
        {
            throw new GraphicsException(
                $"{Describe(texture)} has no bindless slot. The texture must finish its GPU upload "
                    + "before a draw or post-process pass can reference it.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        return binding.BindlessSlot;
    }

    /// <summary>Describes a texture for a binding diagnostic.</summary>
    internal static string Describe(Texture texture) =>
        texture.Path.Length == 0
            ? "The render target texture view"
            : $"The texture '{texture.Path}'";
}
