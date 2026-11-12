# Space, math, and color

[API overview](../api.md) · [document map](../api.md#document-map)

## 5. Space, math, and color

### 5.1 Coordinate system

| Direction | Vector |
| --- | --- |
| Right | `+X` |
| Up | `+Y` |
| Forward | `-Z` |
| Camera at origin, identity rotation | Looks toward `-Z` |

This is the raylib/OpenGL/System.Numerics convention. A camera that should see the origin from
"behind" the scene sits at positive `Z` and keeps `Quaternion.Identity`. A camera at negative `Z`
must rotate 180 degrees around `Y`.

`Transform.Forward`, `Right`, and `Up` are derived from the rotation and follow this convention.
`Vector3.UnitZ` is **backward**, not forward.

glTF import is handled by the bake. glTF is right-handed and Y-up, so vertex positions and
normals pass through without an axis swap. A glTF asset whose authored "front" faces `+Z` may
need a 180-degree `Y` rotation at the model root; the bake can apply that per asset when the
sidecar requests it. Z-up sources such as Blender exports are converted to Y-up at bake time.

### 5.2 `Transform`

```csharp
public struct Transform {
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Scale { get; set; }

    public static Transform Identity { get; }

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale);
    public Transform(Vector3 position);

    public readonly Matrix4x4 ToMatrix();
    public readonly Transform WithParent(Transform parent);
    public readonly Transform WithPosition(Vector3 position);
    public readonly Transform WithRotation(Quaternion rotation);
    public readonly Transform WithScale(Vector3 scale);

    public readonly Vector3 TransformPoint(Vector3 point);
    public readonly Vector3 TransformDirection(Vector3 direction);
    public readonly Transform Inverse();
    public static Transform Lerp(Transform a, Transform b, float t);

    public readonly Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);
    public readonly Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);
    public readonly Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);
}
```

Semantics:

- `Transform` is **world space** by default. It is not a node in a retained hierarchy.
- `WithParent(parent)` treats the receiver as parent-local and returns the world transform:
  `world = local * parent` under the row-vector convention. This matches
  `Matrix4x4.Multiply(localMatrix, parentMatrix)`.
- `ToMatrix()` is `Scale * Rotation * Translation`, the System.Numerics row-vector order.
- `WithParent` composes matrices and decomposes the result back to TRS. A parent with non-uniform
  scale plus a rotated child produces shear that TRS cannot represent exactly; the method throws
  `InvalidOperationException` rather than silently returning a wrong transform. Decompose-friendly
  hierarchies work as expected.
- `Rotation` is expected to be normalized. The constructor and `WithRotation` normalize it; direct
  property assignment is the caller's responsibility.
- `Transform` is a mutable value type. Copy semantics apply. Use the `With*` methods when a new
  value is intended.
- `Inverse()` throws `InvalidOperationException` when the scale has a zero component or the matrix
  is singular. It also throws when the matrix inverse contains shear and cannot be represented as a
  translation, rotation, and scale, for example a non-uniform scale combined with a rotation.
- Rotation helpers in §5.6 use degrees. The raw `Quaternion.CreateFromAxisAngle` and other
  `System.Numerics` methods use radians.

Example:

```csharp
var parent = new Transform {
    Position = new Vector3(0f, 1f, 0f),
    Rotation = Quaternion.Identity,
    Scale = Vector3.One,
};

var local = new Transform {
    Position = new Vector3(1f, 0f, 0f),
    Rotation = Quaternion.Identity,
    Scale = Vector3.One,
};

Transform world = local.WithParent(parent); // Position == (1, 1, 0)
```

### 5.3 `Color`

`Color` is RayBork's port of the linear `Color` type from
`Bork2d.Engine/Rendering/Color.cs`. It is an immutable linear RGBA value stored as a `Vector4`.
Components are normally in `[0, 1]`, but HDR rendering deliberately allows values above `1`.
There is no 8-bit sRGB `Color` and no implicit gamma conversion; the present pass applies AgX and
the output transfer function.

```csharp
public readonly struct Color : IEquatable<Color>, IFormattable {
    public Color(float r, float g, float b, float a);
    public Color(float r, float g, float b);

    public float R { get; }
    public float G { get; }
    public float B { get; }
    public float A { get; }

    public float this[int index] { get; } // 0..3; out of range throws
    public float Grayscale { get; }
    public float MaxColorComponent { get; }

    public static Color Lerp(Color a, Color b, float t);
    public static Color LerpUnclamped(Color a, Color b, float t);

    public void ToOkLab(out float l, out float a, out float b);
    public static Color FromOkLab(float l, float a, float b, float alpha = 1f);
    public static Color LerpOkLab(Color a, Color b, float t);
    public static Color LerpOkLabUnclamped(Color a, Color b, float t);

    public static Color HSVToRGB(float h, float s, float v);
    public static Color HSVToRGB(float h, float s, float v, bool hdr = true);
    public static void RGBToHSV(Color rgb, out float h, out float s, out float v);

    public static Color operator +(Color a, Color b);
    public static Color operator -(Color a, Color b);
    public static Color operator *(Color a, Color b);
    public static Color operator *(Color a, float b);
    public static Color operator *(float a, Color b);
    public static Color operator /(Color a, float b);

    public static implicit operator Vector4(Color color);
    public static implicit operator Color(Vector4 value);

    public static readonly Color Clear;
    public static readonly Color Black;
    public static readonly Color White;
    public static readonly Color Red;
    public static readonly Color Green;
    public static readonly Color Blue;
    public static readonly Color Yellow;
    public static readonly Color DarkCyan;
    public static readonly Color Transparent;
    // The full Unity palette is provided as static readonly fields.
}
```

- Equality is exact per-component `float` equality, matching the Bork2d type.
- `Color` serializes as four linear floats. The Bork2d `ColorMarshaler` is reused.
- `HSVToRGB` uses Unity-style HSV components with `h`, `s`, and `v` in `[0, 1]`. Hue wraps around
  the color wheel, saturation is clamped to `[0, 1]`, and `v` is clamped to `[0, 1]` only when
  `hdr` is false. With `hdr: true`, values above `1` are preserved for HDR colors.
- `Color` values are **authored in linear space**; every preset field is a linear value. For example
  `Color.DarkCyan` is `(0, 0.5, 0.5, 1)`, not an sRGB byte triple.
- The final output path applies AgX tone mapping and then the swapchain transfer function. Sampling
  an sRGB texture decodes to linear before lighting. Sampling a linear texture stays linear.

### 5.4 `Rect`

```csharp
public readonly record struct Rect(float X, float Y, float Width, float Height) {
    public float Right { get; }
    public float Bottom { get; }
    public Vector2 Position { get; }
    public Vector2 Size { get; }

    public bool Contains(Vector2 point);
    public static Rect FromSize(float width, float height);
}
```

`Rect` is used for viewports, destination rectangles, monitor bounds, and scissor-like regions.
Screen rectangles use a top-left origin.

### 5.5 Rays, bounds, and collision

RayBork uses `System.Numerics.Plane` and the engine's own `RayBork.BoundingBox` value type directly,
and adds a small `Collision` helper plus a `RayCollision` result. M4 ships `Ray` and
`BoundingFrustum`; the M5 collision milestone adds the ray and sphere helpers and any additional
bounds types it needs.

```csharp
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max);

public readonly record struct Ray(Vector3 Origin, Vector3 Direction);

public readonly struct BoundingFrustum {
    public BoundingFrustum(Matrix4x4 viewProjection);
    public Plane Left { get; }
    public Plane Right { get; }
    public Plane Top { get; }
    public Plane Bottom { get; }
    public Plane Near { get; }
    public Plane Far { get; }
    public bool Contains(Vector3 point);
    public bool Intersects(BoundingBox bounds);
}
```

- `Ray.Direction` is expected to be normalized. `Camera.ScreenToWorldRay` returns a normalized
  direction.
- `BoundingFrustum` extracts six inward-facing, normalized planes from a view-projection matrix in
  the engine's row-vector convention. `Contains` requires every signed plane distance to be
  non-negative. `Intersects` uses the positive-vertex test and is conservative: a box that only
  touches a plane counts as intersecting.
- The plane names follow the engine's Y-flipped NDC and reverse-Z `[0, 1]` depth range: `Near` is the
  `z >= 0` plane, `Far` is the `z <= w` plane, and `Top`/`Bottom` are inverted relative to a
  non-flipped clip space. See [Rendering: Camera](rendering/camera.md#72-projection-clear-hdr-and-tone-mapping).
- A default `BoundingFrustum` has six zero planes and accepts every point; construct it from a real
  projection before culling with it.

The M5 collision contract adds the ray and sphere helpers. `Ray` is the shared ray value type.

```csharp
public readonly struct RayCollision {
    public bool Hit { get; }
    public float Distance { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
}

public static class Collision {
    public static bool CheckSpheres(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB);
    public static bool CheckBoxes(BoundingBox a, BoundingBox b);
    public static bool CheckBoxSphere(BoundingBox box, Vector3 center, float radius);

    public static RayCollision GetRaySphere(Ray ray, Vector3 center, float radius);
    public static RayCollision GetRayBox(Ray ray, BoundingBox box);
    public static RayCollision GetRayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c);
    public static RayCollision GetRayPlane(Ray ray, Plane plane);
    public static RayCollision GetRayMesh(Ray ray, Mesh mesh, Matrix4x4 transform);
}
```

`GetRayMesh` requires the mesh's CPU positions. v1 retains CPU positions for loaded meshes;
procedural primitives may expose bounds only until their CPU data is wired in. If a mesh has no CPU
positions, `GetRayMesh` throws `NotSupportedException` rather than returning a false miss.

Collision helpers are pure math and thread-safe. They do not query the renderer or the scene.

### 5.6 Vector and quaternion extensions

RayBork extends `System.Numerics.Vector3` and `Quaternion` with Unity-style members using the C#
`extension` syntax. The extensions live in the flat `RayBork` namespace and are available through
the normal `using RayBork;`.

User-facing angle arguments are **degrees**. The extension methods convert to radians internally;
raw `System.Numerics` methods remain radians.

```csharp
public static class Vector3Extensions {
    extension(Vector3 value) {
        public float Magnitude => value.Length();
        public float SqrMagnitude => value.LengthSquared();
        public Vector3 Normalized => Vector3.Normalize(value);

        public Vector3 WithX(float x) => new(x, value.Y, value.Z);
        public Vector3 WithY(float y) => new(value.X, y, value.Z);
        public Vector3 WithZ(float z) => new(value.X, value.Y, z);
        public Vector3 Xz => new(value.X, 0f, value.Z);

        public float Angle(Vector3 other);                 // degrees
        public float SignedAngle(Vector3 other, Vector3 axis); // degrees
        public Vector3 Project(Vector3 normal);
        public Vector3 ProjectOnPlane(Vector3 normal);
        public Vector3 Reflect(Vector3 normal);
        public Vector3 ClampMagnitude(float maxLength);
        public Vector3 Rotated(Quaternion rotation);
    }
}

public static class QuaternionExtensions {
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float RADIANS_TO_DEGREES = 180f / MathF.PI;

    extension(Quaternion value) {
        public static Quaternion Euler(float x, float y, float z);
        public static Quaternion AngleAxis(float degrees, Vector3 axis);
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards);
        public static Quaternion FromToRotation(Vector3 from, Vector3 to);

        public Quaternion Normalized => Quaternion.Normalize(value);
    }
}
```

Additional members follow the same pattern: `EulerAngles` returns degrees, `Angle(Quaternion)`
returns degrees, `ToAngleAxis` returns a degree angle and axis, and `RotateTowards`/`Slerp`
interpolate with degree-based angular limits. There is no `Inverse` extension property; use the
static `Quaternion.Inverse(q)` for quaternion inversion.

Unity-style static `Vector3` members are also provided through extension properties: `Up`, `Down`,
`Left`, `Right`, `Forward`, and `Back`. `Forward` is `-Z` to match the world convention; `Back` is
`+Z`. `Vector3.Zero` and `Vector3.One` come from `System.Numerics` and are not shadowed.

Example:

```csharp
Quaternion turn = Quaternion.Euler(0f, 90f, 0f);
Vector3 forward = Vector3.Transform(-Vector3.UnitZ, turn);
Vector3 euler = turn.EulerAngles;    // degrees
float angle = a.Angle(b);            // degrees
```
