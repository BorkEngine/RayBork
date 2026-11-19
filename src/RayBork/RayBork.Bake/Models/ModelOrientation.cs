using System.Numerics;

namespace RayBork.Bake.Models;

/// <summary>Resolves the sidecar front axis to the rotation that maps it onto <c>-Z</c>.</summary>
/// <remarks>
/// RayBork is right-handed and Y-up with <c>-Z</c> forward. The six signed axes map onto it with
/// the rotation implied by <see cref="Quaternion.CreateFromAxisAngle"/>; a positive turn about
/// <c>+Y</c> sends <c>+X</c> to <c>-Z</c>.
/// </remarks>
internal static class ModelOrientation
{
    /// <summary>The sidecar default: the source faces <c>+Z</c>.</summary>
    internal const string DEFAULT_FRONT = "+z";

    /// <summary>Resolves one front-axis token.</summary>
    internal static bool TryResolve(string front, out Quaternion rotation, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(front);

        switch (front.Trim().ToLowerInvariant())
        {
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
