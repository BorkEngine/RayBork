using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Resolves a public <see cref="Texture"/> binding to the bindless slot a shader record writes.
/// </summary>
/// <remarks>
/// A packed, procedural, or render-target-view texture resolves the same way: the frame's retention set
/// already kept its engine asset or target alive, so the slot is live. A <see langword="null"/> binding
/// uses the reserved white slot. Render-thread only; the methods allocate nothing.
/// </remarks>
internal static class VulkanTextureBinding {
    /// <summary>
    /// Resolves one texture binding to its bindless slot.
    /// </summary>
    /// <param name="texture">The texture, or <see langword="null"/> for the white fallback.</param>
    /// <returns>The bindless slot.</returns>
    /// <exception cref="GraphicsException">
    /// The texture is not a Vulkan GPU texture or has no bindless slot yet. The diagnostic code is
    /// <c>RB3001</c>.
    /// </exception>
    internal static uint ResolveSlot(Texture? texture) {
        if (texture is null) {
            return VulkanBindlessTextureTable.WHITE_SLOT;
        }

        ITextureBindingResource? binding = texture.BindingResource;
        if (binding is null) {
            throw new GraphicsException(
                $"{Describe(texture)} is not a Vulkan GPU texture and cannot be sampled by a shader.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        if (binding.BindlessSlot == 0) {
            throw new GraphicsException(
                $"{Describe(texture)} has no bindless slot. The texture must finish its GPU upload "
                    + "before a draw or post-process pass can reference it.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return binding.BindlessSlot;
    }

    /// <summary>Describes a texture for a binding diagnostic.</summary>
    /// <param name="texture">The texture with the failing binding.</param>
    /// <returns>A path or render-target-view description.</returns>
    internal static string Describe(Texture texture) =>
        texture.Path.Length == 0 ? "The render target texture view" : $"The texture '{texture.Path}'";
}
