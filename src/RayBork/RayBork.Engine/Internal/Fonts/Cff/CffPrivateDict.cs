namespace RayBork.Internal;

/// <summary>
/// The parts of one CFF Private DICT that glyph interpretation needs.
/// </summary>
/// <remarks>
/// The Subrs offset is relative to the Private DICT start and is resolved by the face parser into an
/// INDEX. <c>defaultWidthX</c> and <c>nominalWidthX</c> are validated but intentionally not retained:
/// advances come from <c>hmtx</c> and a charstring's optional leading width operand is consumed and
/// discarded. A Private DICT without a Subrs operator resolves to <see cref="CffIndex.Empty"/>.
/// </remarks>
internal readonly struct CffPrivateDict
{
    /// <summary>Initializes a private dictionary with its local subroutine INDEX.</summary>
    /// <param name="localSubrs">The local subroutine INDEX, empty when the DICT has no Subrs key.</param>
    internal CffPrivateDict(CffIndex localSubrs)
    {
        LocalSubrs = localSubrs;
    }

    /// <summary>Gets an empty private dictionary with no local subroutines.</summary>
    internal static CffPrivateDict Empty { get; } = new(CffIndex.Empty);

    /// <summary>Gets the local subroutine INDEX.</summary>
    internal CffIndex LocalSubrs { get; }
}
