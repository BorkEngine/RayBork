# Rendering: Camera

[API overview](../../api.md) · [document map](../../api.md#document-map)

## 7. Rendering

### 7.1 `Camera`

```csharp
public sealed class Camera : IDisposable {
    public Transform Transform { get; }
    public CameraProjection Projection { get; }
    public float FieldOfView { get; }
    public float OrthographicHeight { get; }
    public float Near { get; }
    public float Far { get; }
    public ClearMode ClearMode { get; }
    public Color ClearColor { get; }
    public RenderTexture? Target { get; }
    public Rect Viewport { get; }
    public Matrix4x4 ViewMatrix { get; }
    public Matrix4x4 ProjectionMatrix { get; }
    public Matrix4x4 ViewProjectionMatrix { get; }
    public BoundingFrustum Frustum { get; }
    public bool IsDisposed { get; }

    public void DrawModel(Model model, Transform transform, Color? tint = null);

    public void DrawWireModel(Model model, Transform transform, Color? tint = null);

    public void DrawMesh(
        Mesh mesh,
        Transform transform,
        Shader shader,
        RenderState? renderState = null,
        Color? tint = null);

    public void DrawInstanced(
        Mesh mesh,
        Shader shader,
        ReadOnlySpan<Transform> transforms,
        RenderState? renderState = null,
        Color? tint = null);

    public void DrawModelInstanced(
        Model model,
        ReadOnlySpan<Transform> transforms,
        Color? tint = null);

    public void DrawLine(Vector3 start, Vector3 end, Color color);
    public void DrawLines(ReadOnlySpan<Vector3> points, Color color);
    public void DrawPoint(Vector3 position, Color color, float size = 1f);
    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color);
    public void DrawTriangleStrip(ReadOnlySpan<Vector3> points, Color color);
    public void DrawRay(Ray ray, Color color);
    public void DrawBoundingBox(BoundingBox box, Color color);

    public void DrawCube(Vector3 position, Vector3 size, Color color);
    public void DrawCube(Vector3 position, float width, float height, float length, Color color);
    public void DrawCube(Transform transform, Vector3 size, Color color);
    public void DrawWireCube(Vector3 position, Vector3 size, Color color);
    public void DrawWireCube(Vector3 position, float width, float height, float length, Color color);
    public void DrawWireCube(Transform transform, Vector3 size, Color color);

    public void DrawSphere(Vector3 center, float radius, Color color);
    public void DrawSphere(Vector3 center, float radius, int rings, int slices, Color color);
    public void DrawSphere(Transform transform, float radius, Color color);
    public void DrawSphere(Transform transform, float radius, int rings, int slices, Color color);
    public void DrawWireSphere(Vector3 center, float radius, Color color);
    public void DrawWireSphere(Vector3 center, float radius, int rings, int slices, Color color);
    public void DrawWireSphere(Transform transform, float radius, Color color);
    public void DrawWireSphere(Transform transform, float radius, int rings, int slices, Color color);

    public void DrawCylinder(Vector3 position, float radius, float height, Color color);
    public void DrawCylinder(Vector3 position, float radius, float height, Color color, int sides);
    public void DrawCylinder(
        Vector3 position,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color);
    public void DrawCylinder(
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        int sides,
        Color color);
    public void DrawCylinder(
        Transform transform,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color);
    public void DrawWireCylinder(
        Vector3 position,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color);
    public void DrawWireCylinder(
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        int sides,
        Color color);
    public void DrawWireCylinder(
        Transform transform,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color);

    public void DrawCapsule(
        Vector3 start,
        Vector3 end,
        float radius,
        int rings,
        int slices,
        Color color);
    public void DrawCapsule(
        Transform transform,
        float radius,
        float height,
        int rings,
        int slices,
        Color color);
    public void DrawWireCapsule(
        Vector3 start,
        Vector3 end,
        float radius,
        int rings,
        int slices,
        Color color);
    public void DrawWireCapsule(
        Transform transform,
        float radius,
        float height,
        int rings,
        int slices,
        Color color);

    public void DrawPlane(Vector3 center, Vector2 size, Color color);
    public void DrawPlane(Transform transform, Vector2 size, Color color);
    public void DrawCircle(Vector3 center, float radius, Vector3 rotationAxis, float rotationAngle, Color color);
    public void DrawCircle(Transform transform, float radius, Color color);
    public void DrawGrid(int slices, float spacing, Color color);
    public void DrawBillboard(Texture texture, Vector3 position, Vector2 size, Color color);
    public void DrawBillboard(Texture texture, Vector3 position, Vector2 size, Color color, bool faceCamera);
    public void DrawBillboard(Texture texture, Transform transform, Vector2 size, Color color);
    public void DrawBillboard(Texture texture, Transform transform, Vector2 size, Color color, bool faceCamera);
    public void DrawBillboard(Texture texture, Rect source, Vector3 position, Vector2 size, Color color);
    public void DrawBillboard(Texture texture, Rect source, Vector3 position, Vector2 size, Color color, bool faceCamera);
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color);
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color,
        bool faceCamera);
    public void DrawTexture(Texture texture, Rect destination, Color? tint = null);
    public void DrawText(
        Font font,
        ReadOnlySpan<char> text,
        Transform transform,
        float size,
        Color color,
        TextAlignment alignment = TextAlignment.Left,
        TextWrapping wrapping = TextWrapping.None,
        float maxWidth = 0f);
    public void DrawTextBillboard(
        Font font,
        ReadOnlySpan<char> text,
        Vector3 position,
        float size,
        Color color,
        TextAlignment alignment = TextAlignment.Left);

    public Ray ScreenToWorldRay(Vector2 screenPosition);
    public Vector2 WorldToScreen(Vector3 worldPosition);

    public void Dispose();
}
```

A camera is a **frame-scoped recorder**, not a GPU object and not a reusable scene camera.

- Created on the tick thread during `Tick`; must be disposed before `Tick` returns.
- `Dispose` seals the pass and submits it to the render thread. It is idempotent.
- After `Dispose`, any draw or property access throws `ObjectDisposedException`.
- Creating a camera while another camera is active throws `InvalidOperationException`; cameras are
  sequential, not nested. Multiple cameras in one tick are created and disposed one after another.
- `CreateCamera` and `CreateOrthographicCamera` require a frame that is being recorded, so they are
  legal only on the tick thread during `Tick`. Setup, Shutdown, any other thread, and a second active
  camera are `InvalidOperationException`.
- If a camera is not disposed when `Tick` returns, the engine throws `InvalidOperationException`
  naming the leaked camera and the frame is **not** published. The tick loop stops with that
  exception; `Shutdown` still runs.
- The camera does not copy the whole scene. It records value snapshots of the draws and shader
  state at each call. The renderer never reads live game objects.
- The pass records the clamped pixel viewport, the clear mode and color, the view, projection, and
  view-projection matrices, and the camera position. Every recorded frame retains the mesh, internal
  shader program, and bound textures it recorded until the render thread releases the frame slot; a
  model or material object is not an engine asset and adds no hold.
- MSAA is a property of the target. The swapchain uses the builder's `WithMSAA` sample count. There
  is no `RenderTexture` yet: every camera records into the app's linear HDR scene target.

> **M5 subset.** `Camera` ships the raylib-style helper surface: the line, point, and triangle helpers
> (`DrawLine`, `DrawLines`, `DrawPoint`, `DrawTriangle`), the twelve shape helpers (`DrawCube`,
> `DrawSphere`, `DrawPlane`, `DrawCylinder`/`DrawWireCylinder`, `DrawCapsule`/`DrawWireCapsule`,
> `DrawCircle`, `DrawTriangleStrip`, `DrawRay`, `DrawBoundingBox`, `DrawWireCube`, `DrawWireSphere`,
> and `DrawGrid`), and the eight `DrawBillboard` overloads, with the shape, position, tessellation,
> start/end, and `Transform` overloads listed above. Their geometry, programs, render states,
> validation, and batching are specified in §7.9. The model helpers are `DrawModel`,
> `DrawModelInstanced`, and `DrawWireModel`; the wireframe helper is specified below. The `Target`
> property and the `target` parameter arrive with `RenderTexture` in M8; `DrawText` and
> `DrawTextBillboard` arrive with runtime text in M6. `DrawTexture` is not part of M5: it moves to M7
> with the orthographic/sprite contract, where the destination rectangle is in render pixels and the
> texture is sampled with its own point/linear filter. The camera always records into the app's linear
> HDR scene target, and the present pass applies AgX after all cameras finish.

Properties:

| Property | Meaning |
| --- | --- |
| `Transform` | World-space camera transform. The camera looks down local `-Z`. |
| `FieldOfView` | Vertical field of view in degrees; perspective only (`0` for an orthographic camera). |
| `OrthographicHeight` | Visible world-space height; orthographic only (`0` for a perspective camera). |
| `Near`, `Far` | Clip distances. `Far` may be `+infinity` for perspective, never for orthographic. |
| `ClearMode` | Which buffers this pass clears. |
| `ClearColor` | Linear color used when `ClearMode.Color` is set. |
| `Viewport` | Pixel rectangle in the target, clamped when the camera was created. Defaults to the full target. |
| `ViewMatrix` | Inverse of `Transform.ToMatrix()`. |
| `ProjectionMatrix` | Reverse-Z `[0, 1]` projection with the Vulkan Y-flip already applied. |
| `ViewProjectionMatrix` | `ViewMatrix * ProjectionMatrix` under the row-vector convention. |
| `Frustum` | World-space `BoundingFrustum` derived from the view-projection matrix; useful for custom culling. |

`CreateCamera` sets the aspect ratio from the clamped viewport. The projection is built with
reverse-Z; the `ProjectionMatrix` already contains Vulkan's Y-flip. Custom code that builds its own
projection must preserve both conventions if it wants to match the engine's depth and screen-space
helpers.

`clearColor: null` means linear **opaque black** (`Color.Black`). HDR components above `1` are
allowed; a non-finite component is `ArgumentException`. `near` must be positive and finite, `far`
must be greater than `near`, and a perspective `far` may be `float.PositiveInfinity`. `fieldOfView`
must be finite and inside `(0, 180)` degrees. An orthographic camera requires a finite `far`.
`CreateOrthographicCamera` takes the visible `height`; `CreateCamera` is the perspective entry point
and rejects `CameraProjection.Orthographic` with `ArgumentException` because it has no height
parameter.

`ScreenToWorldRay` takes **render pixels** with a top-left origin. `Input.MousePosition` is in
window coordinates, so convert it with `ctx.Window.RenderWidth / (float)ctx.Window.Width` when
`Width` is nonzero. `DpiScale` is the OS display scale, not the pixel ratio, and is not the
conversion factor for this call. The ray starts at the camera position for a perspective camera and
at the near-plane point for an orthographic camera, and its direction is normalized. A non-finite
input is `ArgumentException`.

`WorldToScreen` returns render pixels in the camera viewport with a top-left origin. A point behind
the camera (`w <= 0`) has no finite screen position, so it returns `(float.NaN, float.NaN)`. A
non-finite input is `ArgumentException`. A point outside the viewport still projects to a finite
value; the caller decides whether to clip it.

`DrawModel(model, transform, tint)` uses each part's **material**. At the call it snapshots that
material's current uniform cursor and texture bindings and uses the part's `ModelPart.RenderState`
(a `null` constructor argument resolved to `Shader.DefaultRenderState`). Different parts may therefore
use different programs, render states, uniforms, and textures in one model draw, which is the normal
path for an imported multi-material model.

`DrawModelInstanced(model, transforms, tint)` does the same for every part and applies the part's
local transform before each instance transform. The frame retains each part material's internal
program and every bound texture; the material object itself is not an engine asset and is not
retained.

`DrawWireModel(model, transform, tint)` is the raylib `DrawModelWires`/`DrawModelWiresEx`
equivalent. It records exactly the parts, material snapshots, and tint of `DrawModel`, but draws them
through a polygon-mode-line variant of the geometry pipeline (`VK_POLYGON_MODE_LINE`). It is
Transform-only by design: the transform supplies the position, rotation, and scale, there are no
position, scale, or axis-angle overloads, and there is no `Ex`/`Wires` suffix name. Culling, depth
test, depth write, and blending still come from the part's `ModelPart.RenderState`, so back faces stay
culled and only the visible triangle edges of front-facing geometry are drawn; a triangle split into
two triangles draws the diagonal it shares with its sibling. The tint, textures, and alpha behave as
in `DrawModel`: the built-in shaders multiply `rbInstance.Color` into the line fragment and sample the
material's textures there, so a textured wireframe still reads its texture. The instance matrix is
`world = part.LocalTransform.ToMatrix() * transform.ToMatrix()` under the row-vector convention, and
the transform's scale and rotation apply to the part geometry.

The wireframe pipeline needs the device's core **`fillModeNonSolid`** feature. The renderer enables
it at device creation when the physical device advertises it and includes the polygon mode in the
geometry pipeline cache key, so a wireframe draw never reuses a filled pipeline; the extra pipeline is
created on first use per shader, render state, and topology and cached for the rest of the scene
target's lifetime. A device without `fillModeNonSolid` reports the draw as an unsupported
`GraphicsException` with diagnostic code `RB3001` instead of silently drawing nothing: the error names
the device and the feature, the frame is dropped, and the app stops with that exception. M5 has no
CPU line-geometry fallback. Filled model draws never need the feature.

`tint` writes the per-instance linear color field. Built-in shaders multiply it into the output. A
custom shader sees it as `rbInstance.Color` and must use it explicitly. `null` means opaque white.
The general draw API does not expose a UV rectangle override, so `DrawMesh`, `DrawInstanced`, and
every primitive helper except the billboard source-rectangle overloads record `(0, 0, 1, 1)`.
`DrawBillboard` with a `source` rectangle records that normalized rectangle as `rbInstance.UvRect`;
see **Billboards** below.

For `DrawMesh` and `DrawInstanced`, `renderState` overrides the material's default render state for
this draw only. `null` uses `Shader.DefaultRenderState`. The override is part of the batching key.
`DrawModel` and `DrawModelInstanced` have no per-draw state override; they use each part's
`ModelPart.RenderState`.

**Primitive helpers.** The primitive helpers record engine primitive meshes through the built-in
`/shaders/primitive` and `/shaders/mesh` programs; §7.9 has the exact geometry, program, render-state,
and batching contract. They are tick-thread only and validate before recording: a non-finite argument
is `ArgumentException`, a negative radius, height, size, extent, or point size is
`ArgumentOutOfRangeException`, an exactly zero extent, radius, height, or start/end segment records
nothing, `sides < 3`, `rings < 2`, `slices < 3`, `slices < 1` for `DrawGrid`, and `spacing <= 0` are
`ArgumentOutOfRangeException`, a zero `rotationAxis` is `ArgumentException`, a null texture is
`ArgumentNullException`, and a disposed camera, texture, or primitive asset is
`ObjectDisposedException`. A billboard source rectangle with a non-finite component is
`ArgumentException`; a non-positive width or height, or a rectangle outside the normalized `[0, 1]`
texture rectangle, is `ArgumentOutOfRangeException`. A non-finite billboard origin is
`ArgumentException`; the origin is not clamped. Colors are linear; each helper writes its color as
the per-instance tint. Lines are one pixel wide, wire shapes are line geometry, and `DrawCylinder`
records two instanced draws for equal radii. The four-argument `DrawCylinder` convenience overload
uses the default 16 sides; the five-argument form takes `sides` explicitly.

**Primitive overloads and Transform composition.** The primitive families use ordinary C# overloads;
the engine has no `Ex`, `Rec`, `Pro`, or `V` suffixes. Every transformable solid or wire shape (cube,
sphere, plane, cylinder, capsule, circle, wire cube, and wire sphere) has a raylib-compatible
shape/position overload and a `Transform` overload in addition to its simple overload. The line,
point, triangle, strip, ray, bounding box, and grid helpers are world-space draws without a `Transform`
form. The transform supplies the world position, rotation, and scale; the shape dimensions stay
explicit arguments. The instance matrix is `local * transform.ToMatrix()` under the row-vector
convention, where `local` builds the shape dimensions in local space. The transform scale therefore
multiplies the shape dimensions, and an identity rotation with unit scale reproduces the matching
simple overload at `Transform.Position`. A zero transform scale component records nothing; a negative
scale or a non-finite transform component is rejected like any other invalid argument. The billboard
is the one exception: its non-facing `Transform` overloads orient the quad in world space, while the
non-facing simple overloads keep the camera-relative vertical billboard, so an identity transform
reproduces the simple facing overload but not the simple non-facing one.

Anchors: the cube, wire cube, sphere, wire sphere, plane, and circle are centered at the transform
origin; the cylinder and wire cylinder put their bottom cap/ring at the transform origin with local
`+Y` as their axis; the capsule and wire capsule are centered at the transform origin with local `+Y`
as their axis and their cap centers at `±height/2`. The circle lies in the local `XZ` plane with local
`+Y` as its normal, matching `DrawPlane` and `DrawGrid`. The axis-angle circle form rotates that same
base circle around `rotationAxis` by `rotationAngle` degrees before translating it to `center`.

`DrawCylinder(start, end, startRadius, endRadius, sides, color)` treats `start` and `end` as the cap
centers and orients the local `+Y` axis between them; `DrawCapsule(start, end, radius, rings, slices,
color)` treats `start` and `end` as the spherical cap centers, so the cap-center distance is the
cylinder height. The capsule `Transform` form instead takes that distance explicitly as `height`.
The full cylinder and wire-cylinder overloads accept unequal radii; a zero radius at one end makes a
cone and omits the degenerate cap. The tessellated sphere and wire-sphere overloads compose the
requested `rings` by `slices` surface instead of instancing the fixed engine mesh; the fixed engine
sphere and wire-sphere meshes remain the default for the shorter forms. A triangle strip is
decomposed into `points.Length - 2` triangles with alternating winding; a zero-area triangle records
nothing.

**Billboards.** `DrawBillboard` records one engine quad instance through the built-in
`/shaders/mesh` program with `RenderState.AlphaBlend`. The four-argument convenience form is the
camera-facing, full-texture, centered quad. The other forms add a normalized source rectangle, a
`Transform`, an explicit origin pivot, or a combination:

- `source` is a normalized rectangle in the texture: `X` and `Y` are the top-left corner and
  `Width`/`Height` are the positive extents, all inside `[0, 1]`. It is recorded as the per-instance
  UV rectangle `rbInstance.UvRect = (X, Y, Width, Height)` and the mesh shader maps the quad's
  authored UVs through it; the default `(0, 0, 1, 1)` reproduces the authored UVs. The rectangle is
  per-instance data, so two billboards with different source rectangles still batch together.
- `origin` is the normalized pivot in the billboard's local quad: `(0, 0)` is the bottom-left corner
  and `(1, 1)` is the top-right corner. The pivot is placed at the transform position;
  `(0.5, 0.5)` is the centered default of the overloads without an explicit origin. The origin is not
  clamped; only its finiteness is validated, so a pivot outside the quad is legal.
- `size` is the world-space width and height before the transform scale, and the transform scale
  multiplies it on the local X and Y axes. A zero size component or a zero transform scale component
  records nothing.
- With `faceCamera: true` the quad faces the camera: the camera basis supplies the plane and the
  transform rotation supplies only the in-plane roll, its twist about the billboard's local `+Z`
  plane normal. Pitch and yaw components of the rotation do not tilt the plane; the transform
  position and scale apply.
- With `faceCamera: false` the quad does not face the camera. The `Vector3` overloads keep the
  existing vertical billboard: world up plus the camera's yaw. The `Transform` overloads are
  world-oriented by the transform (`local * transform.ToMatrix()`). An identity transform therefore
  gives a world-space quad in the local XY plane facing world `+Z`, which is intentionally different
  from the `Vector3` overloads' vertical orientation because the `Vector3` overloads have no
  transform rotation to orient the plane.
- The engine material's texture binding is set immediately before the recorded call and cleared
  immediately afterwards, so the shared material never keeps a game texture. The recorded frame
  retains the texture until the renderer releases the slot, so a texture disposed after the call
  stays alive for the recorded frame.

**DrawTexture (M7).** The screen-space texture helper is not part of M5. It returns in M7 as the
sprite helper for an orthographic camera: `destination` is a render-pixel rectangle relative to the
camera viewport (top-left origin, Y downward), `tint` is linear, and the texture's own sampler
selects point or linear filtering. It draws an alpha-blended textured quad on the normal draw path;
the M5 unprojected-overlay implementation is not part of the shipped surface.

**Ray/quad collision.** `Collision.GetRayQuad(ray, a, b, c, d)` splits the quad into the triangles
`(a, b, c)` and `(a, c, d)`, reuses the double-sided triangle test, and returns the nearest
non-negative hit. Its normal is the normalized geometric triangle normal and is not flipped toward
the ray; a hit on the shared diagonal reports the `(a, b, c)` result. A degenerate quad, a
non-finite input, or a hit behind the ray origin reports a miss, and the ray direction is not
required to be normalized.

**Error model.**

- A `null` model, mesh, or material argument is `ArgumentNullException`.
- A disposed camera, model, mesh, part material, or bound texture is `ObjectDisposedException`.
- A material that does not declare both the vertex and fragment stages is
  `InvalidOperationException`.
- A camera draw or `Dispose` from a thread other than the tick thread is
  `InvalidOperationException`.
- A `DrawWireModel` frame on a device without `fillModeNonSolid` fails with an unsupported-feature
  `GraphicsException` (`RB3001`); this is reported by the renderer, not by the tick-thread call,
  because the camera does not query device capabilities.
- A model part's `LocalTransform` is applied as
  `world = part.LocalTransform.ToMatrix() * instanceTransform.ToMatrix()`; the engine never composes
  it through `Transform.WithParent`, which can throw on shear.

### 7.2 Projection, clear, HDR, and tone mapping

```csharp
public enum CameraProjection {
    Perspective,
    Orthographic,
}

[Flags]
public enum ClearMode {
    None = 0,
    Color = 1 << 0,
    Depth = 1 << 1,
    Stencil = 1 << 2,
    ColorAndDepth = Color | Depth,
    All = Color | Depth | Stencil,
}

public enum TonemappingMode {
    AgX,
    None,
}

public sealed class TonemappingSettings {
    public float Exposure { get; set; }
    public float AgxWhite { get; set; }
    public float AgxContrast { get; set; }
}
```

- Default camera clear is `ClearMode.ColorAndDepth`. A 3D frame that uses only `ClearMode.Color`
  will reuse the previous frame's depth buffer and must be treated as an overlay pass.
- Clears are scoped to the camera viewport, not the whole target. The renderer clears the clamped
  viewport rectangle with the camera's clear state; a pass with a smaller viewport leaves the pixels
  outside it untouched.
- Depth clears to `0.0`. Color clears to `ClearColor`. `ClearMode.Stencil` is a no-op because the
  scene target has no stencil attachment; the flag is accepted and does not fail.
- The scene target's render pass starts with undefined color and depth content. A first camera that
  wants defined content must clear; a first camera with `ClearMode.None` leaves undefined content,
  and later cameras see the previous pass' pixels.
- The depth test defaults to `GreaterOrEqual`; depth writes are on for opaque draws.
- Perspective supports an infinite far plane. The projection maps the near plane to `1.0` and
  infinity to `0.0`.
- Orthographic supports reverse-Z too, but requires a finite far plane.
- The viewport is in pixels, top-left origin, and is clamped to the target. An invalid or empty
  viewport throws `ArgumentException`.
- Multiple cameras can clear different passes sequentially in one tick. The first camera usually
  clears; later cameras choose `ClearMode.None` or `ClearMode.Depth` for overlays. The engine shares
  one frame uniform block per frame slot across every camera pass: a frame with more than one camera
  pass submits the same `rbFrame` values (the last recorded pass') to every pass. Split-screen
  cameras in one frame are therefore a known limitation rather than a supported layout.
- A camera that targets a `RenderTexture` and a later camera that samples that texture in the same
  tick are ordered by creation order; the renderer inserts the required image barrier. Sampling a
  target inside the same pass that writes it is invalid and is reported as a validation error. This
  applies from M8; the current renderer has no render target.

There is no builder-level clear color: `WithClearColor` is removed and each camera's `ClearColor` is
the scene clear control for its pass. A camera that does not clear color leaves the previous pass'
color in place, which is the documented overlay behavior.

**Projection math.** The engine is right-handed and Y-up and the camera looks down local `-Z`.
`ViewMatrix` is the inverse of `Transform.ToMatrix()`. `ProjectionMatrix` uses Vulkan's `[0, 1]`
reverse-Z range and includes the Y-flip that maps world `+Y` to the top of the screen;
`ViewProjectionMatrix = ViewMatrix * ProjectionMatrix` under the row-vector convention.

For a perspective camera, with `f = 1 / tan(fieldOfView / 2)` and `aspect = viewport.Width /
viewport.Height`, the conventional column-vector form is:

```text
[ f/aspect   0        0        0 ]
[ 0          f        0        0 ]
[ 0          0        a        b ]
[ 0          0       -1        0 ]
```

An infinite far plane uses `a = 0` and `b = near`. A finite far plane uses
`a = near / (far - near)` and `b = near * far / (far - near)`. This maps the near plane to `1.0` and
the far plane (or infinity) to `0.0`. The engine applies Vulkan's Y-flip by negating the second row,
so a world-space point above the camera projects to negative NDC Y, which is the top of the screen.
`Camera.ProjectionMatrix` stores the row-vector transpose of this formula because `System.Numerics`
multiplies row vectors.

For an orthographic camera with a finite `far`, `width = height * aspect`, and the conventional
column-vector form is:

```text
[ 2/width    0          0             0               ]
[ 0         -2/height   0             0               ]
[ 0          0          1/(far-near)  far/(far-near)  ]
[ 0          0          0             1               ]
```

The orthographic projection maps the near plane to `1.0`, the far plane to `0.0`, and includes the
same Y-flip. `BoundingFrustum` extracts its planes from the engine's Y-flipped NDC, so `Top` and
`Bottom` follow the flipped screen convention rather than the mathematical world axes.

**HDR chain.** The scene target is linear `R16G16B16A16_SFLOAT` color with a `D32_SFLOAT` depth
attachment. The present pass applies exposure, `TonemappingMode`, the HDR-aware AgX curve, and the
output encoding. The default is `AgX` at `0` EV. `TonemappingMode.None` skips the tone curve and
still writes through the selected output encoding. Exposure is applied as `2^EV` before tone mapping.
The runtime controls are described under **Runtime tone mapping** below.

**Swapchain selection.** The cached SDL3 window HDR state drives the choice. When the window has HDR
enabled, the renderer selects `R16G16B16A16_SFLOAT` with
`VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT` (scRGB) when the surface advertises that pair. If the
surface advertises the float format only with `VK_COLOR_SPACE_SRGB_NONLINEAR_KHR`, that pair is used
with a warning. If the surface advertises no scRGB float pair, the renderer falls back to the SDR
order and reports `HdrOutput = false`.

The SDR order is `B8G8R8A8_SRGB`, `R8G8B8A8_SRGB`, `B8G8R8A8_UNORM`, `R8G8B8A8_UNORM` (manual sRGB
encode in the present shader), then `R16G16B16A16_SFLOAT` (linear clamp). HDR10
(`A2B10G10R10_UNORM_PACK32`, PQ) is deliberately deferred and is never selected. The selected
format token is reported by `EngineInfo.SwapchainFormat`; `HdrOutput` is true only when the window
reports HDR and the selected pair presents through the scRGB HDR path.

**HDR output and AgX.** The present pass applies Godot 4.7's HDR-aware AgX tone mapper. The curve
itself has an output maximum: SDR uses `1`, and the scRGB HDR path uses the reported display headroom
(`peak / paper white`), so the curve alone sets the HDR peak.

The tone map starts from the exposed scene-linear Rec.709 color:

1. The combined Rec.709 to Rec.2020 and AgX inset matrix transforms the exposed color into the AgX
   working space. All coefficients are positive, so a non-negative scene color stays non-negative,
   and the combined matrices preserve neutral gray: a neutral input stays neutral through the inset,
   curve, and outset.
2. Each channel goes through the allenwp curve. Its crossover is `0.18` (18% middle gray): a
   power-function toe below the crossover and a Reinhard-like shoulder above it, with matching value
   and slope at the crossover. The curve maps the AgX white to the output maximum.
3. The curve result is clamped to the output maximum; the clamp is required for a very bright cyan
   response.
4. The combined inverse AgX outset and Rec.2020 to Rec.709 matrix returns the display-linear color.
   Saturated colors may have small negative channels; the SDR path clamps the final result to
   `[0, 1]`, and the scRGB path clamps the scaled result to zero.

The curve parameters are derived on the CPU from `AgxWhite` and `AgxContrast` and passed to the
shader, so the shader and the CPU reference use the same formula:

```text
toe_a  = (1 / 0.18 - 1) * 0.18^contrast
slope  = contrast * 0.18^(contrast - 1) * toe_a / (0.18^contrast + toe_a)^2
white  = max(2, AgxWhite) * outputMax
w      = (white - 0.18)^2 / (outputMax - 0.18) * slope
```

`AgxWhite` defaults to `16.29` and `AgxContrast` to `1.25`. On SDR, `outputMax` is `1`: the curve
maps the AgX white to display white, and the present pass then applies the optional manual sRGB
encode and clamps to `[0, 1]`. On scRGB HDR, `outputMax` is the reported display headroom, so the
curve output is in paper-white units up to the display peak; the present pass multiplies it by paper
white (the window's reported SDR white level, for example `2.0`) and keeps the headroom unclamped,
so the curve, not the encoder, sets the peak. Because the curve's white scales with the output
maximum, a scene value of `AgxWhite` maps to display white on SDR, and `AgxWhite * headroom` maps to
the reported display peak on HDR. The toe and the `0.18` crossover do not depend on the output
maximum, so the dark-to-mid response is the same on both paths and neutral gray stays neutral.

`TonemappingMode.None` bypasses the curve: the scRGB path writes `scene * paperWhite` with no upper
clamp, and the SDR paths write the exposed scene through the encoding and clamp. `None` remains the
direct HDR authoring path. HDR10/PQ output is still deferred. An SDR window uses paper white `1`, so
its image is the SDR AgX output. The cached HDR state also carries the headroom for diagnostics. A
window HDR state change, or a change to the SDR white level while HDR is active, recreates the
presentation path.

**Runtime tone mapping.** `WithExposure`, `WithAgxWhite`, and `WithAgxContrast` set the startup
defaults for `EngineContext.Tonemapping`, one app-lifetime settings object that a game can change
while it runs:

```csharp
public void Tick(EngineContext ctx) {
    ctx.Tonemapping.Exposure = 0f;       // stops; the present pass applies 2^EV before AgX
    ctx.Tonemapping.AgxWhite = 16.29f;   // scene-linear white; the curve's white scales with the HDR headroom
    ctx.Tonemapping.AgxContrast = 1.25f; // AgX curve contrast
}
```

| Member | Default | Contract |
| --- | --- | --- |
| `Exposure` | `0` EV | Finite. The present pass multiplies the scene by `2^Exposure` before the curve. |
| `AgxWhite` | `16.29` | Finite and at least `2`. The scene-linear white the curve maps to the output maximum. On the scRGB HDR path the curve's white is this value multiplied by the display headroom, so it maps to the reported display peak; on SDR it maps to `1.0`. |
| `AgxContrast` | `1.25` | Finite and at least `1`. The AgX curve contrast. |

- `TonemappingMode` is fixed at startup by `WithTonemapping`; only the exposure, white, and contrast
  are runtime-mutable.
- The settings are tick-thread only. A valid write from another thread is
  `InvalidOperationException`; an out-of-range value is `ArgumentOutOfRangeException` and is rejected
  before the thread check. Reads are tick-thread reads too.
- The present pass is frame-global: every camera pass in a frame shares one configuration. The tick
  thread copies the values into the frame packet when it seals the frame after `Tick` returns, so a
  write during `Tick` applies to that frame's present. A frame that was already published keeps the
  snapshot it was sealed with, and the render thread never reads the live settings object.
- The object lives for the app lifetime and is not disposable. Values written in `Setup` are in
  effect for the first frame.

**MSAA.** The requested sample count comes from `WithMSAA` (default 4). The effective count is the
highest device-supported count that does not exceed the request, or `1` when none is supported, and
a fallback logs a warning. The scene target color and depth attachments use the effective count;
the swapchain images are single-sample. Scene color is resolved into a single-sample sampled image
before the present pass. Depth is cleared to `0.0` and the reverse-Z state is defined; multisampled
depth is not resolved because nothing samples depth until M8. A `RenderTexture` uses its
own `SampleCount` (M8).

**Present mode.** The default is `Mailbox`, with automatic fallback to `Fifo` when the surface does
not advertise it; a requested `Fifo` falls back to `Mailbox` when FIFO is unavailable. `Immediate` is
used only when explicitly selected, and an unavailable `Immediate` mode fails startup with `RB3001`
instead of falling back. Mailbox is not a guarantee of tearing-free presentation on every driver;
the selected mode is reported by `EngineInfo.PresentMode`.

**Reverse-Z.** The scene target clears depth to `0.0`. Depth-tested pipelines use `GreaterOrEqual`,
enable depth test and depth write, and use the `[0, 1]` depth range. The present triangle is not
depth-tested; the geometry pipelines consume the state.

Reverse-Z is a whole-pipeline convention. Custom projection code must not mix in a
`[-1,1]`-style OpenGL projection or the depth test will be wrong. The engine exposes no
`CreatePerspective` helper in v1; custom projections are the caller's responsibility.
