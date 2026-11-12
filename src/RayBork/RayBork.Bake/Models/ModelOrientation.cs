using System.Numerics;

namespace RayBork.Bake.Models;

/// <summary>
/// Resolves the sidecar's authored front axis to the rotation that maps it onto RayBork's forward
/// direction, <c>-Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// RayBork is right-handed and Y-up with <c>-Z</c> forward. The table lists the rotation applied to
/// the source geometry, using the signed-axis convention of
/// <see cref="Quaternion.CreateFromAxisAngle"/> and <see cref="Vector3.Transform(Vector3, Quaternion)"/>
/// (a positive turn about <c>+Y</c> sends <c>+X</c> to <c>-Z</c>):
/// </para>
/// <list type="table">
/// <listheader><term>front</term><description>correction</description></listheader>
/// <item><term><c>-z</c></term><description>identity</description></item>
/// <item><term><c>+z</c></term><description>180 degrees about <c>+Y</c></description></item>
/// <item><term><c>+x</c></term><description>+90 degrees about <c>+Y</c>; maps <c>+X</c> to <c>-Z</c></description></item>
/// <item><term><c>-x</c></term><description>-90 degrees about <c>+Y</c>; maps <c>-X</c> to <c>-Z</c></description></item>
/// <item><term><c>+y</c></term><description>-90 degrees about <c>+X</c>; maps <c>+Y</c> to <c>-Z</c></description></item>
/// <item><term><c>-y</c></term><description>+90 degrees about <c>+X</c>; maps <c>-Y</c> to <c>-Z</c></description></item>
/// </list>
/// </remarks>
internal static class ModelOrientation {
    /// <summary>The sidecar default: the source faces <c>+Z</c>.</summary>
    internal const string DEFAULT_FRONT = "+z";

    /// <summary>Resolves one front-axis token.</summary>
    /// <param name="front">The sidecar token.</param>
    /// <param name="rotation">The correction rotation when the token is valid.</param>
    /// <param name="reason">The failure reason when the token is invalid.</param>
    /// <returns><see langword="true"/> when the token is one of the six supported axes.</returns>
    internal static bool TryResolve(string front, out Quaternion rotation, out string? reason) {
        ArgumentNullException.ThrowIfNull(front);

        switch (front.Trim().ToLowerInvariant()) {
            case "-z":
                rotation = Quaternion.Identity;
                reason = null;
                return true;

            case "+z":
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
                reason = null;
                return true;

            case "+x":
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
                reason = null;
                return true;

            case "-x":
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);
                reason = null;
                return true;

            case "+y":
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);
                reason = null;
                return true;

            case "-y":
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
                reason = null;
                return true;

            default:
                rotation = Quaternion.Identity;
                reason = $"the front axis '{front}' is not one of +x, -x, +y, -y, +z, or -z";
                return false;
        }
    }
}
