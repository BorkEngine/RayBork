using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The CFF FontMatrix: the 2x3 affine map from charstring coordinates to em-relative text space.
/// </summary>
/// <remarks>
/// <para>
/// The default matrix is <c>[0.001 0 0 0.001 0 0]</c>, which is the reciprocal of the em square for a
/// face with 1,000 font units per em. A point is mapped to font units as
/// <c>(a*x + c*y + e, b*x + d*y + f) * unitsPerEm</c>; with the default matrix and
/// <c>unitsPerEm == 1000</c> the result is the identity, so ordinary CFF faces are unaffected. The
/// matrix is applied after charstring interpretation, including to a <c>seac</c> composite, so the
/// standard-encoding offsets stay in charstring space and the translation is scaled exactly once.
/// </para>
/// <para>
/// The instance is immutable. Values that overflow the single-precision outline coordinates are
/// reported by the outline reader as <see cref="FontFormatException"/>, not by this struct.
/// </para>
/// </remarks>
internal readonly struct CffFontMatrix {
    private const double DEFAULT_SCALE = 0.001;

    private readonly double _a;
    private readonly double _b;
    private readonly double _c;
    private readonly double _d;
    private readonly double _e;
    private readonly double _f;

    /// <summary>Initializes a FontMatrix from its six affine coefficients.</summary>
    /// <param name="a">The x scale.</param>
    /// <param name="b">The x y-shear.</param>
    /// <param name="c">The y x-shear.</param>
    /// <param name="d">The y scale.</param>
    /// <param name="e">The x translation.</param>
    /// <param name="f">The y translation.</param>
    internal CffFontMatrix(double a, double b, double c, double d, double e, double f) {
        _a = a;
        _b = b;
        _c = c;
        _d = d;
        _e = e;
        _f = f;
    }

    /// <summary>Gets the default <c>[0.001 0 0 0.001 0 0]</c> FontMatrix.</summary>
    internal static CffFontMatrix Default { get; } = new(
        DEFAULT_SCALE,
        0,
        0,
        DEFAULT_SCALE,
        0,
        0);

    /// <summary>Gets a value indicating whether the matrix maps charstring units to font units unchanged.</summary>
    /// <param name="unitsPerEm">The face's <c>head</c> units per em.</param>
    /// <returns><see langword="true"/> when the matrix is the identity at this scale.</returns>
    internal bool IsIdentity(int unitsPerEm) =>
        (_a * unitsPerEm) == 1
            && (_d * unitsPerEm) == 1
            && _b == 0
            && _c == 0
            && _e == 0
            && _f == 0;

    /// <summary>Maps one charstring-space point to font units.</summary>
    /// <param name="point">The point in charstring coordinates.</param>
    /// <param name="unitsPerEm">The face's <c>head</c> units per em.</param>
    /// <returns>The point in font units.</returns>
    internal Vector2 Transform(Vector2 point, int unitsPerEm) => new(
        (float)((((_a * point.X) + (_c * point.Y)) + _e) * unitsPerEm),
        (float)((((_b * point.X) + (_d * point.Y)) + _f) * unitsPerEm));
}
