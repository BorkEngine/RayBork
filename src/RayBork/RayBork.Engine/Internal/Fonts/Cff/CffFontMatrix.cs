using System.Numerics;

namespace RayBork.Internal;

/// <summary>The CFF FontMatrix: the 2x3 affine map from charstring to em space.</summary>
/// <remarks>
/// The default <c>[0.001 0 0 0.001 0 0]</c> is the identity at 1,000 font units per em; a point
/// maps to font units as <c>(a*x + c*y + e, b*x + d*y + f) * unitsPerEm</c>, applied once after
/// interpretation including <c>seac</c> components. The instance is immutable.
/// </remarks>
internal readonly struct CffFontMatrix
{
    private const double DEFAULT_SCALE = 0.001;

    private readonly double _a;
    private readonly double _b;
    private readonly double _c;
    private readonly double _d;
    private readonly double _e;
    private readonly double _f;

    /// <summary>Initializes a FontMatrix from its six affine coefficients.</summary>
    internal CffFontMatrix(double a, double b, double c, double d, double e, double f)
    {
        _a = a;
        _b = b;
        _c = c;
        _d = d;
        _e = e;
        _f = f;
    }

    /// <summary>Gets the default <c>[0.001 0 0 0.001 0 0]</c> FontMatrix.</summary>
    internal static CffFontMatrix Default { get; } = new(DEFAULT_SCALE, 0, 0, DEFAULT_SCALE, 0, 0);

    /// <summary>Gets a value indicating whether the matrix is the identity.</summary>
    internal bool IsIdentity(int unitsPerEm) =>
        (_a * unitsPerEm) == 1
        && (_d * unitsPerEm) == 1
        && _b == 0
        && _c == 0
        && _e == 0
        && _f == 0;

    /// <summary>Maps one charstring-space point to font units.</summary>
    internal Vector2 Transform(Vector2 point, int unitsPerEm) =>
        new(
            (float)((((_a * point.X) + (_c * point.Y)) + _e) * unitsPerEm),
            (float)((((_b * point.X) + (_d * point.Y)) + _f) * unitsPerEm)
        );
}
