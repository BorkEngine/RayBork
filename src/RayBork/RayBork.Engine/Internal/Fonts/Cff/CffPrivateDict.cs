namespace RayBork.Internal;

/// <summary>The parts of one CFF Private DICT that glyph interpretation needs.</summary>
/// <remarks>
/// The Subrs offset is resolved by the face parser into an INDEX, and a DICT without a Subrs
/// operator resolves to <see cref="CffIndex.Empty"/>. The width values are validated but not
/// retained because advances come from <c>hmtx</c>.
/// </remarks>
internal readonly struct CffPrivateDict
{
    /// <summary>Initializes a private dictionary with its local subroutine INDEX.</summary>
    internal CffPrivateDict(CffIndex localSubrs)
    {
        LocalSubrs = localSubrs;
    }

    /// <summary>Gets an empty private dictionary with no local subroutines.</summary>
    internal static CffPrivateDict Empty { get; } = new(CffIndex.Empty);

    /// <summary>Gets the local subroutine INDEX.</summary>
    internal CffIndex LocalSubrs { get; }
}
