# API Cheatsheet

Everything lives in the `RayBork` namespace. `Vector2`, `Vector3`, `Vector4`, `Quaternion`,
`Matrix4x4`, and `Plane` come from `System.Numerics`. `Task`, `CancellationToken`, `ReadOnlySpan`,
and `IReadOnlyList` come from the BCL.

Unless stated otherwise, drawing, cameras, shaders, and effect settings are tick-thread only.

## App

### RayBorkAppBuilder

`new RayBorkAppBuilder()` creates a single-use builder. Chain the options, then call `Build()`.

| Member | Description |
| --- | --- |
| `WithGame(IGame game)` | Sets the game callbacks. Required. |
| `WithTitle(string title)` | Sets the window title. Default `"RayBork"`. |
| `WithSize(int width, int height)` | Sets the logical client size. Default `1280x720`. |
| `WithResizable(bool resizable = true)` | Allows user resizing. Default `false`. |
| `WithWindowState(WindowState state)` | Sets the initial window state. |
| `WithMaxDeltaTime(double seconds)` | Clamps `DeltaTime`. Default `0.25`. |
| `WithTargetFps(int framesPerSecond)` | Caps the tick rate; `0` is uncapped. |
| `WithPresentMode(PresentMode mode = PresentMode.Mailbox)` | Sets the presentation mode. |
| `WithMSAA(int sampleCount = 4)` | Requests MSAA; the device picks the best supported count. |
| `WithTonemapping(TonemappingMode mode = TonemappingMode.AgX)` | Sets the tone-mapping mode for the app lifetime. |
| `WithExposure(float ev = 0)` | Sets exposure in stops; `+1` doubles linear exposure. |
| `WithAgxWhite(float white = 16.29)` | Sets the AgX white point. |
| `WithAgxContrast(float contrast = 1.25)` | Sets the AgX curve contrast. |
| `WithValidation(bool enabled = true)` | Requests the Vulkan validation layer. Default is on in Debug. |
| `WithAssetsPack(string path)` | Adds a user pack; later packs shadow earlier ones. |
| `WithAssetsPacks(params string[] paths)` | Adds several packs in order. |
| `WithAssetType<T>()` | Registers a typed data asset; `T` needs a compiled `[GenerateShape]`. |
| `WithAssetLoader<T>(IAssetLoader<T> loader)` | Registers a custom loader for `T`. |
| `WithLoggerFactory(ILoggerFactory factory)` | Uses a caller-owned logger factory, not disposed by the app. |
| `WithLogLevel(LogLevel level)` | Sets the level of the built-in console logger. |
| `Build()` | Validates and creates the app. Does not open a window yet. |

### RayBorkApp

| Member | Description |
| --- | --- |
| `Run()` | Runs the app on the process main thread. Call once. |
| `RequestExit()` | Requests a graceful exit from any thread. |
| `Dispose()` | Requests exit and waits for `Run` to finish. |
| `IsRunning` | Whether `Run` is active. |
| `IsDisposed` | Whether the app was disposed. |

### IGame

| Member | Description |
| --- | --- |
| `Task Setup(EngineContext context)` | Runs once before the first tick. Load assets and create targets here. |
| `void Tick(EngineContext context)` | Runs once per frame. Must be synchronous and non-blocking. |
| `Task Shutdown()` | Runs once at the end, including after a tick exception. |

### EngineContext

| Member | Description |
| --- | --- |
| `IAssetCatalog Assets` | The mounted asset catalog; safe from any thread. |
| `EngineDefaults Defaults` | Engine-owned default shader, textures, and fonts. |
| `IInput Input` | The input snapshot for the current tick. |
| `IWindow Window` | The window; getters are snapshots, setters are queued. |
| `ILoggerFactory LoggerFactory` | The app logger factory. |
| `EngineInfo Info` | Renderer, device, and swapchain information. |
| `RenderStats Stats` | Statistics from the last completed frame. |
| `TonemappingSettings Tonemapping` | App-lifetime exposure and AgX settings. |
| `double Time` | Monotonic seconds since app start. |
| `double DeltaTime` | Clamped seconds since the previous tick. |
| `double UnscaledDeltaTime` | Unclamped seconds since the previous tick. |
| `double Fps` | Smoothed tick rate, not presented frames per second. |
| `long FrameCount` | Number of completed ticks. |
| `bool IsFirstTick` | True only during the first tick. |
| `bool IsExiting` | True after an exit request or window close. |
| `double MaxDeltaTime` | The configured delta clamp. |
| `void RequestExit()` | Requests a graceful exit. |
| `Camera CreateCamera(...)` | Creates a camera pass. Must be disposed before the tick ends. |
| `RenderTexture CreateRenderTextureAsync(...)` | Creates a render target; await it in `Setup`. |
| `MeshBuilder CreateMeshBuilder(...)` | Creates a builder for a procedural mesh. |
| `TextureBuilder CreateTextureBuilder(...)` | Creates a builder for a procedural texture. |

`CreateCamera` takes the camera transform, projection, field of view or orthographic height, clear
mode and color, near/far planes, viewport, render target, and post-process stack. Only the size
argument that matches the projection may be set.

```text
Camera CreateCamera(
    Transform transform, CameraProjection projection,
    float fieldOfView = 0, float orthographicHeight = 0,
    ClearMode clearMode = ClearMode.ColorAndDepth, Color? clearColor = null,
    float near = 0.01, float far = 1000, Rect? viewport = null,
    RenderTexture? target = null, PostProcessStack? postProcess = null)

Task<RenderTexture> CreateRenderTextureAsync(RenderTextureDescription description,
                                             CancellationToken cancellationToken = default)
MeshBuilder CreateMeshBuilder(PrimitiveTopology topology = PrimitiveTopology.TriangleList)
TextureBuilder CreateTextureBuilder(int width, int height,
    TextureFormat format = TextureFormat.Rgba8Srgb, bool generateMips = true,
    SamplerState? sampler = null)
```

## Window

### IWindow

| Member | Description |
| --- | --- |
| `Title`, `Width`, `Height` | Title and logical client size. |
| `RenderWidth`, `RenderHeight` | Framebuffer size in physical pixels. |
| `DpiScale` | OS display scale, such as `1.5` at 150%. |
| `Bounds` | Window rectangle in desktop coordinates. |
| `IsResizable`, `IsFullscreen`, `IsMinimized`, `IsFocused` | Window state flags. |
| `IsCursorOnScreen` | Whether the pointer is over the client area. |
| `WasResized` | True for the first tick after a resize. |
| `CloseRequested` | Whether the user asked to close. There is no veto. |
| `State` | Current `WindowState`. |
| `Monitors`, `CurrentMonitor` | Monitor snapshots; indices are stable. |
| `SetTitle(string)`, `SetSize(int, int)`, `SetPosition(int, int)` | Queue window changes. |
| `SetFullscreen(bool)`, `SetBorderless(bool)` | Queue fullscreen or border changes. |
| `SetMinSize(int, int)`, `SetMaxSize(int, int)` | Queue size limits; `0` means no limit. |
| `SetMonitor(int)` | Moves the window to a monitor index. |
| `Minimize()`, `Maximize()`, `Restore()` | Queue a window-state change. |
| `SetCursor(CursorMode)` | Sets cursor visibility and capture. |
| `SetTextInputArea(Rect, int cursorOffset)` | Places the IME candidate window. |
| `GetClipboardText()` / `SetClipboardText(string)` | Read or replace clipboard text. |
| `Close()` | Requests a graceful close. |

### Monitor

`Monitor` is a value snapshot with `Index`, `Name`, `Bounds`, `WorkArea`, `DpiScale`, and
`RefreshRate`. Create one with `new Monitor(int Index, string Name, Rect Bounds, Rect WorkArea,
float DpiScale, int RefreshRate)`.

### WindowState

`Normal = 0`, `Minimized = 1`, `Maximized = 2`, `Fullscreen = 3`, `BorderlessFullscreen = 4`,
`Hidden = 5`.

### CursorMode

`Normal = 0`, `Hidden = 1`, `Disabled = 2` (hidden and captured, for mouse look).

## Input

### IInput

| Member | Description |
| --- | --- |
| `IsKeyDown(Key)`, `IsKeyUp(Key)` | Key held or not held. |
| `IsKeyPressed(Key)` | First press during this tick; OS repeats do not count. |
| `IsKeyPressedRepeat(Key)` | An OS key-repeat event during this tick. |
| `IsKeyReleased(Key)` | Release during this tick. |
| `IsMouseButtonDown(MouseButton)`, `IsMouseButtonUp(MouseButton)` | Mouse button held or not held. |
| `IsMouseButtonPressed(MouseButton)`, `IsMouseButtonReleased(MouseButton)` | Mouse edge for this tick. |
| `TextInput` | UTF-16 text entered during this tick. |
| `MousePosition` | Mouse position in render pixels, top-left origin. |
| `MouseDelta` | Mouse movement in render pixels since the previous tick. |
| `MouseWheel` | Vertical wheel delta for this tick. |
| `Gamepads` | Connected gamepads for this tick. |
| `GetGamepad(int index)` | Gamepad at an index, or `null`. |

### IGamepad

| Member | Description |
| --- | --- |
| `Index`, `InstanceId`, `Name`, `IsConnected` | Pad identity and connection state. |
| `IsButtonDown(GamepadButton)`, `IsButtonPressed(...)`, `IsButtonReleased(...)` | Button held and edge states. |
| `GetAxis(GamepadAxis)` | Stick axis in `[-1, 1]`. |
| `GetTrigger(GamepadTrigger)` | Trigger value in `[0, 1]`. |
| `SetVibration(float low, float high, double seconds)` | Starts rumble for a duration. |
| `StopVibration()` | Stops rumble. |

### GamepadAxis

`LeftX = 0`, `LeftY = 1`, `RightX = 2`, `RightY = 3`.

### GamepadTrigger

`Left = 0`, `Right = 1`.

### MouseButton

`Left = 0`, `Middle = 1`, `Right = 2`, `X1 = 3`, `X2 = 4`.

### GamepadButton

`South = 0`, `East = 1`, `West = 2`, `North = 3`, `Back = 4`, `Guide = 5`, `Start = 6`,
`LeftStick = 7`, `RightStick = 8`, `LeftShoulder = 9`, `RightShoulder = 10`, `DpadUp = 11`,
`DpadDown = 12`, `DpadLeft = 13`, `DpadRight = 14`, `Misc1 = 15`, `Touchpad = 20`,
`Misc2 = 21`, `Misc3 = 22`.

### Key

`Unknown = 0`, letters `A`-`Z`, digits `D0`-`D9`, `F1`-`F24`, and:

`Apostrophe`, `Application`, `Backslash`, `Backspace`, `CapsLock`, `Comma`, `Delete`, `Down`, `End`,
`Enter`, `Equal`, `Escape`, `Grave`, `Home`, `Insert`, `Left`, `LeftAlt`, `LeftBracket`,
`LeftControl`, `LeftShift`, `LeftSuper`, `Menu`, `Minus`, `NonUsBackslash`, `NonUsHash`, `NumLock`,
`PageDown`, `PageUp`, `Pause`, `Period`, `PrintScreen`, `Right`, `RightAlt`, `RightBracket`,
`RightControl`, `RightShift`, `RightSuper`, `ScrollLock`, `Semicolon`, `Slash`, `Space`, `Tab`, `Up`,
`Keypad0`-`Keypad9`, `Keypad00`, `Keypad000`, `KeypadA`-`KeypadF`, `KeypadAdd`, `KeypadSubtract`,
`KeypadMultiply`, `KeypadDivide`, `KeypadDecimal`, `KeypadEnter`, `KeypadEqual`, `KeypadComma`,
`KeypadBackspace`, `KeypadClear`, `KeypadClearEntry`, `KeypadLeftParen`, `KeypadRightParen`,
`KeypadLeftBrace`, `KeypadRightBrace`, `KeypadTab`, `KeypadSpace`, `KeypadColon`,
`KeypadDecimalSeparator`, `KeypadPlusMinus`, `KeypadPercent`,
`KeypadPower`, `KeypadExclamation`, `KeypadAmpersand`, `KeypadDoubleAmpersand`, `KeypadVerticalBar`,
`KeypadDoubleVerticalBar`, `KeypadLess`, `KeypadGreater`, `KeypadHash`, `KeypadAt`, `KeypadBinary`,
`KeypadOctal`, `KeypadHexadecimal`, `KeypadXor`, `KeypadMemoryClear`, `KeypadMemoryRecall`, `KeypadMemoryStore`,
`KeypadMemoryAdd`, `KeypadMemorySubtract`, `KeypadMemoryMultiply`, `KeypadMemoryDivide`,
`KeypadEqualsAs400`, `MediaPlay`, `MediaPause`, `MediaPlayPause`, `MediaStop`, `MediaNextTrack`,
`MediaPreviousTrack`, `MediaRecord`, `MediaRewind`, `MediaFastForward`, `MediaEject`,
`MediaSelect`, `VolumeUp`, `VolumeDown`, `Mute`, `Mode`, `Sleep`, `Wake`, `Power`, `ChannelIncrement`,
`ChannelDecrement`, `Clear`, `ClearAgain`, `CrSel`, `ExSel`, `Oper`, `Out`, `Prior`, `Return2`,
`Separator`, `Select`, `Stop`, `Again`, `Undo`, `Cut`, `Copy`, `Paste`, `Find`, `Help`, `Execute`,
`Cancel`, `SysReq`, `AltErase`, `CurrencyUnit`, `CurrencySubunit`, `DecimalSeparator`,
`ThousandsSeparator`, `International1`-`International9`, `Lang1`-`Lang9`, `AccessibilityNew`,
`AccessibilityOpen`, `AccessibilityClose`, `AccessibilityExit`, `AccessibilitySave`,
`AccessibilityPrint`, `AccessibilityProperties`, `AccessibilitySearch`, `AccessibilityHome`,
`AccessibilityBack`, `AccessibilityForward`, `AccessibilityStop`, `AccessibilityRefresh`,
`AccessibilityBookmarks`, `SoftLeft`, `SoftRight`, `Call`, `EndCall`.

## Assets

Asset paths are case-insensitive, start with an optional `/`, use `/` separators, and omit the
extension: `/textures/paddle`. The catalog is thread-safe. Engine assets are shared and
reference-counted; a registered custom loader returns a fresh value per call.

### IAssetCatalog

| Member | Description |
| --- | --- |
| `Task<T> Load<T>(string path, CancellationToken token = default)` | Loads a built-in asset, a registered loader type, or registered typed data. |
| `Task<byte[]> LoadData(string path, CancellationToken token = default)` | Returns the decompressed payload bytes of any entry. |
| `bool Exists(string path)` | Tests whether a mounted pack contains the path. |

### IAssetLoader&lt;T&gt;

| Member | Description |
| --- | --- |
| `AssetEntryType EntryType` | The entry type the loader accepts. |
| `Task<T> LoadAsync(AssetLoadContext context)` | Creates one new caller-owned value. Runs on the asset thread; must not block. |

### IAsset

`IAsset` is implemented by `Mesh`, `Texture`, `Shader`, `Model`, and `Font`. It exposes
`string Path` and `bool IsDisposed`. Asset objects are reference-counted by the engine; disposing the
caller's reference is safe after recording a frame that uses it.

### AssetLoadContext

| Member | Description |
| --- | --- |
| `Path` | The current entry path, with a leading `/`. |
| `CancellationToken` | Canceled when the caller cancels or the catalog stops. |
| `Exists(string path)` | Tests another path in the same packs. |
| `LoadAsync<T>(string path, ...)` | Loads another asset as part of this load. |
| `LoadDataAsync(string path, ...)` | Loads another entry's payload bytes. |
| `LoadPayloadAsync()` | Returns this entry's complete packed payload. |

### AssetEntryType

`Raw = 0`, `Mesh = 1`, `Model = 2`, `Texture = 3`, `Shader = 4`, `Font = 5`, `Data = 6`.

### Exceptions

| Type | Meaning |
| --- | --- |
| `AssetException` | Base class for asset failures. |
| `AssetNotFoundException` | No mounted pack has the path. |
| `AssetTypeMismatchException` | The path exists with a different entry type. |
| `AssetLoadException` | A payload is corrupt or a GPU upload failed. |
| `AssetPackException` | A pack is corrupt or unreadable. |
| `RayBorkException` | Base engine exception; has an optional `DiagnosticCode`. |
| `GraphicsException` | A graphics operation failed. |
| `GraphicsDeviceLostException` | The device was lost and recovery failed. |
| `PlatformException` | SDL or platform initialization failed. |
| `WindowException` | A window operation failed. |
| `GameException` | The game callback failed. |

### EngineDefaults

| Member | Description |
| --- | --- |
| `CreateMeshShader()` | Creates a new material over the built-in mesh program. Dispose it. |
| `White` | Engine-owned 1×1 white sRGB texture. |
| `Error` | Engine-owned 4×4 magenta/black checkerboard. |
| `DefaultFont` | Engine-owned default sans font. |
| `DefaultMonoFont` | Engine-owned default monospaced font. |

Never dispose a `Defaults` asset. They are released when the app shuts down.

### Texture

| Member | Description |
| --- | --- |
| `Width`, `Height` | Base-level size in pixels. A render-target view reports the current target size. |
| `Format` | The GPU `TextureFormat`. |
| `IsSrgb` | Whether samples are sRGB-encoded. |
| `MipLevels` | Number of uploaded mip levels. |
| `Sampler` | The `SamplerState`. |
| `Path`, `IsProcedural`, `IsDisposed` | Identity and state. |
| `Dispose()` | Releases the caller's reference. |

### TextureBuilder

Create with `context.CreateTextureBuilder(width, height, format, generateMips, sampler)`. Fill the
base level, then build.

| Member | Description |
| --- | --- |
| `SetPixels(ReadOnlySpan<byte>)` | Sets tightly packed pixels: 4 bytes per pixel for RGBA8, 1 for R8. |
| `SetPixels(ReadOnlySpan<Color>)` | Sets one linear `Color` per pixel. |
| `GenerateMips()` | Regenerates the full mip chain from the current base level. |
| `BuildAsync(CancellationToken)` | Uploads and returns an immutable `Texture`. |
| `Width`, `Height`, `Format`, `Sampler`, `GenerateMipsEnabled` | Read-only description. |

### TextureFormat

`Rgba8Unorm = 0`, `Rgba8Srgb = 1`, `Bc7Unorm = 2`, `Bc7Srgb = 3`, `Bc5Unorm = 4`, `R8Unorm = 5`,
`Rgba16Float = 6`, `Depth32Float = 7`.

### TextureFilter

`Nearest = 0`, `Linear = 1`, `Anisotropic = 2`.

### TextureWrap

`Repeat = 0`, `Clamp = 1`, `Mirror = 2`.

### SamplerState

`new SamplerState(TextureFilter Filter, TextureWrap Wrap, float MaxAnisotropy = 1)`. Members:
`Filter`, `Wrap`, `MaxAnisotropy`. Anisotropy is clamped to the device limit.

### Shader

A `Shader` is a material: one program plus this material's uniform cursor and texture bindings.
Materials over the same program batch together.

| Member | Description |
| --- | --- |
| `SetUniform(string name, float/Vector2/Vector3/Vector4/Quaternion/Matrix4x4/Color/int/uint/bool value)` | Writes one uniform in the cursor used by the next draw. |
| `SetUniform(string name, Texture? texture)` | Binds a texture to a sampler, or `null` for white. |
| `ResetUniforms()` | Restores bake defaults and clears texture bindings. |
| `Uniforms`, `Textures`, `Stages` | Baked declarations. Read-only. |
| `DefaultRenderState` | The render state baked for the program. |
| `Path`, `IsDisposed`, `Dispose()` | Identity and lifetime. |

### ShaderUniform

`Name`, `Type` (`ShaderUniformType`). Create with `new ShaderUniform(string Name,
ShaderUniformType Type)`.

### ShaderUniformType

`Float = 0`, `Int = 1`, `UInt = 2`, `Bool = 3`, `Vector2 = 4`, `Vector3 = 5`, `Vector4 = 6`,
`Matrix4x4 = 7`, `Texture2D = 8`.

### ShaderTextureBinding

`Name`, `Index` (bindless slot). Create with `new ShaderTextureBinding(string Name, int Index)`.

### ShaderStageMask

`None = 0`, `Vertex = 1`, `Fragment = 2`.

### Mesh

| Member | Description |
| --- | --- |
| `VertexCount`, `IndexCount`, `Topology` | Geometry shape. |
| `Vertices`, `Indices` | CPU copies retained for collision and inspection. |
| `Bounds` | Mesh-space bounds. |
| `HasCollisionAcceleration` | Whether baked collision data exists. |
| `Path`, `IsProcedural`, `IsDisposed`, `Dispose()` | Identity and lifetime. |

### MeshBuilder

Create with `context.CreateMeshBuilder(topology)`. Reuse one builder for several builds.

| Member | Description |
| --- | --- |
| `Vertices`, `Indices` | Mutable geometry lists. |
| `SetVertices(ReadOnlySpan<Vertex>)`, `SetIndices(ReadOnlySpan<uint>)` | Replace geometry with a copy. |
| `Topology` | Primitive topology. |
| `ComputeNormals(MeshNormalMode mode = Smooth)` | Recomputes normals; triangle topologies only. |
| `ComputeTangents()` | Recomputes tangents from normals and UV0. |
| `ComputeBounds()` | Stores bounds for the next build. |
| `GenerateCollisions()` | Builds a ray-collision acceleration structure at build time. |
| `BuildAsync(CancellationToken)` | Uploads and returns an immutable `Mesh`. |
| `GenerateCube(width, height, length)` | Centered box. |
| `GenerateSphere(radius, rings, slices)` | UV sphere. |
| `GenerateHemiSphere(radius, rings, slices)` | Upper hemisphere; open equator. |
| `GenerateCylinder(radius, height, slices)` | Capped cylinder. |
| `GenerateCone(radius, height, slices)` | Capped cone. |
| `GenerateTorus(radius, size, radSeg, sides)` | Torus. |
| `GenerateKnot(radius, size, radSeg, sides)` | Trefoil-knot tube. |
| `GeneratePlane(width, length, resX, resZ)` | Subdivided XZ plane. |
| `GeneratePoly(sides, radius)` | Regular polygon disc in XZ. |

Each `Generate*` replaces the builder geometry with a new primitive.

### PrimitiveTopology

`TriangleList = 0`, `TriangleStrip = 1`, `LineList = 2`, `LineStrip = 3`, `PointList = 4`.

### MeshNormalMode

`Smooth = 0`, `Flat = 1`.

### Vertex

`new Vertex(Vector3 position, Vector3? normal = null, Vector2? uv = null, ..., Color? color = null,
Vector4? tangent = null)`. Fields: `Position`, `Normal`, `Uv`, `Uv1`, `Uv2`, `Uv3`, `Color`,
`Tangent`.

### Model

| Member | Description |
| --- | --- |
| `new Model(params ModelPart[] parts)` | Groups parts into one model. |
| `Parts` | Flat part list. |
| `Bounds` | Union of part bounds in model space. |
| `Dispose()` | Disposes part materials and releases part meshes. |

### ModelPart

`new ModelPart(string name, Mesh mesh, Shader shader, RenderState? renderState = null,
Transform? localTransform = null)`. Members: `Name`, `Mesh`, `Shader`, `RenderState`,
`LocalTransform`, `Bounds`. The model owns the material.

### Font

| Member | Description |
| --- | --- |
| `Measure(ReadOnlySpan<char> text, float size)` | Measures a text block at a size in world units. |
| `TryGetGlyph(Rune rune, out Glyph glyph)` | Gets an already-generated glyph. |
| `GlyphCount`, `CachedGlyphCount` | Mapped codepoints and cached glyph textures. |
| `Ascent`, `Descent`, `LineHeight` | Font metrics in em units. |
| `Path`, `IsDisposed`, `Dispose()` | Identity and lifetime. |

### Glyph

`Rune`, `Advance`, `Bearing`, `Size`, `Uv0`, `Uv1`. Metrics and UVs are in em units and glyph-local
texture space. `Glyph` is a struct; its default value is empty.

## Camera

`Camera` is a frame-scoped recorder. Create it with `EngineContext.CreateCamera`, record draws, and
dispose it before the tick returns. Cameras are sequential and tick-thread only. The camera looks
down local `-Z` in a right-handed, Y-up world; depth is Vulkan reverse-Z and the projection includes
the Y-flip.

| Member | Description |
| --- | --- |
| `Transform` | World-space camera transform. Must have unit scale. |
| `Projection` | `Perspective` or `Orthographic`. |
| `FieldOfView` | Vertical FOV in degrees for a perspective camera, otherwise `0`. |
| `OrthographicHeight` | Visible world height for an orthographic camera, otherwise `0`. |
| `Near`, `Far` | Clip distances; a perspective far plane may be positive infinity. |
| `ClearMode`, `ClearColor` | Buffers cleared by the pass and the linear clear color. |
| `Viewport` | Pixel viewport, clamped to the target. |
| `Target` | Render texture written by the pass, or `null` for the scene target. |
| `ViewMatrix`, `ProjectionMatrix`, `ViewProjectionMatrix` | Camera matrices. |
| `Frustum` | World-space `BoundingFrustum`. |
| `WorldToScreen(Vector3)` | Projects a world position to render pixels, top-left origin. |
| `ScreenToWorldRay(Vector2)` | Converts render pixels to a world-space ray. |
| `ScreenToWorldTransform(Vector2 point, float distance)` | Places a transform on the plane at a distance along the view axis. |
| `IsDisposed`, `Dispose()` | Disposal state and pass sealing. |

### Mesh and model draws

```text
DrawMesh(Mesh mesh, Transform transform, Shader shader, RenderState? renderState = null, Color? tint = null)
DrawInstanced(Mesh mesh, Shader shader, ReadOnlySpan<Transform> transforms, RenderState? renderState = null, Color? tint = null)
DrawModel(Model model, Transform transform, Color? tint = null)
DrawModelInstanced(Model model, ReadOnlySpan<Transform> transforms, Color? tint = null)
DrawWireModel(Model model, Transform transform, Color? tint = null)
```

Records a mesh, an instanced mesh, or every model part with its own material and local transform.
The shader cursor is snapshotted at the call.

### Primitive draws

```text
DrawCube(Vector3 position, Vector3 size, Color color)
DrawCube(Vector3 position, float width, float height, float length, Color color)
DrawCube(Transform transform, Vector3 size, Color color)
DrawWireCube(Vector3 position, Vector3 size, Color color)
DrawWireCube(Vector3 position, float width, float height, float length, Color color)
DrawWireCube(Transform transform, Vector3 size, Color color)
DrawSphere(Vector3 center, float radius, Color color)
DrawSphere(Vector3 center, float radius, int rings, int slices, Color color)
DrawSphere(Transform transform, float radius, Color color)
DrawSphere(Transform transform, float radius, int rings, int slices, Color color)
DrawWireSphere(Vector3 center, float radius, Color color)
DrawWireSphere(Vector3 center, float radius, int rings, int slices, Color color)
DrawWireSphere(Transform transform, float radius, Color color)
DrawWireSphere(Transform transform, float radius, int rings, int slices, Color color)
DrawCylinder(Vector3 position, float radius, float height, Color color)
DrawCylinder(Vector3 position, float radius, float height, Color color, int sides)
DrawCylinder(Vector3 position, float radiusTop, float radiusBottom, float height, int sides, Color color)
DrawCylinder(Vector3 start, Vector3 end, float startRadius, float endRadius, int sides, Color color)
DrawCylinder(Transform transform, float radiusTop, float radiusBottom, float height, int sides, Color color)
DrawWireCylinder(Vector3 position, float radiusTop, float radiusBottom, float height, int sides, Color color)
DrawWireCylinder(Vector3 start, Vector3 end, float startRadius, float endRadius, int sides, Color color)
DrawWireCylinder(Transform transform, float radiusTop, float radiusBottom, float height, int sides, Color color)
DrawCapsule(Vector3 start, Vector3 end, float radius, int rings, int slices, Color color)
DrawCapsule(Transform transform, float radius, float height, int rings, int slices, Color color)
DrawWireCapsule(Vector3 start, Vector3 end, float radius, int rings, int slices, Color color)
DrawWireCapsule(Transform transform, float radius, float height, int rings, int slices, Color color)
DrawPlane(Vector3 center, Vector2 size, Color color)
DrawPlane(Transform transform, Vector2 size, Color color)
DrawCircle(Vector3 center, float radius, Vector3 rotationAxis, float rotationAngle, Color color)
DrawCircle(Transform transform, float radius, Color color)
DrawDisc(Transform transform, float radius, Color color)
DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
DrawTriangleStrip(ReadOnlySpan<Vector3> points, Color color)
DrawLine(Vector3 start, Vector3 end, Color color)
DrawLines(ReadOnlySpan<Vector3> points, Color color)
DrawPoint(Vector3 position, Color color, float size = 1)
DrawRay(Ray ray, Color color)
DrawGrid(int slices, float spacing, Color color)
DrawGrid(Transform transform, int slices, float spacing, Color color)
DrawBoundingBox(BoundingBox box, Color color)
```

Records one solid or wire primitive. Cylinder bases sit at the given position; capsules and circles
use the transform's local axes. Wireframe drawing needs non-solid fill support.

### Quads, billboards, and text

```text
DrawQuad(Transform transform, Vector2 size, Color color, RenderState? renderState = null)
DrawQuad(Transform transform, Vector2 size, Texture texture, Color? tint = null, RenderState? renderState = null)
DrawQuad(Transform transform, Vector2 size, Texture texture, Rect source, Color? tint = null, RenderState? renderState = null)
DrawBillboard(Texture texture, Vector3 position, Vector2 size, Color color)
DrawBillboard(Texture texture, Vector3 position, Vector2 size, Color color, bool faceCamera)
DrawBillboard(Texture texture, Rect source, Vector3 position, Vector2 size, Color color)
DrawBillboard(Texture texture, Rect source, Vector3 position, Vector2 size, Color color, bool faceCamera)
DrawBillboard(Texture texture, Transform transform, Vector2 size, Color color)
DrawBillboard(Texture texture, Transform transform, Vector2 size, Color color, bool faceCamera)
DrawBillboard(Texture texture, Transform transform, Rect source, Vector2 size, Vector2 origin, Color color)
DrawBillboard(Texture texture, Transform transform, Rect source, Vector2 size, Vector2 origin, Color color, bool faceCamera)
DrawText(Font font, ReadOnlySpan<char> text, Transform transform, float size, Color color,
         TextAlignment alignment = TextAlignment.Left, TextWrapping wrapping = TextWrapping.None, float maxWidth = 0)
DrawTextBillboard(Font font, ReadOnlySpan<char> text, Vector3 position, float size, Color color,
                  TextAlignment alignment = TextAlignment.Left)
```

Quads lie in the transform's local XY plane. Billboards face the camera unless `faceCamera` is
false. Text is laid out through the built-in MSDF program; `size` is in world units.

### CameraProjection

`Perspective = 0`, `Orthographic = 1`.

### ClearMode

`None = 0`, `Color = 1`, `Depth = 2`, `Stencil = 4`, `ColorAndDepth = 3`, `All = 7`. The engine has
no stencil attachment, so stencil bits have no effect.

### TextAlignment

`Left = 0`, `Center = 1`, `Right = 2`.

### TextWrapping

`None = 0`, `Word = 1`, `Character = 2`.

## Render state

### RenderState

`new RenderState { Blend, Cull, DepthTest, DepthWrite }`. Static presets: `Opaque`, `AlphaBlend`,
`Additive`, `Multiply`.

### BlendMode

`Opaque = 0`, `Alpha = 1`, `Additive = 2`, `Multiply = 3`.

### CullMode

`None = 0`, `Front = 1`, `Back = 2`.

### DepthTest

`Disabled = 0`, `Greater = 1`, `GreaterOrEqual = 2`, `Less = 3`, `LessOrEqual = 4`, `Always = 5`.
The default opaque preset uses `GreaterOrEqual` for reverse-Z.

## Render textures

### RenderTexture

Create with `context.CreateRenderTextureAsync(description)`. A camera that targets it must be disposed
before the render texture is disposed.

| Member | Description |
| --- | --- |
| `ColorTexture` | Resolved single-sample color view. Owned by the render texture. |
| `DepthTexture` | Resolved depth view, or `null` when there is no depth. |
| `Width`, `Height`, `SampleCount` | Current size and effective MSAA count. |
| `IsHdr` | Whether the color format is `Rgba16Float`. |
| `Resize(int width, int height)` | Recreates the images in place; contents become undefined. |
| `ReadColorsAsync(CancellationToken)` | Reads the resolved color image as `Color[y, x]`. |
| `ReadPixelsAsync(CancellationToken)` | Reads the resolved color image as tightly packed bytes, top row first. |
| `IsDisposed`, `Dispose()` | Lifetime. |

### RenderTextureDescription

`new RenderTextureDescription(int Width, int Height, TextureFormat ColorFormat = Rgba16Float,
bool Depth = true, int SampleCount = 4)`. Members: `Width`, `Height`, `ColorFormat`, `Depth`,
`SampleCount`. Supported color formats: `Rgba16Float`, `Rgba8Unorm`, `Rgba8Srgb`. Sample count may be
1, 2, 4, or 8; the device may choose a lower supported count.

## Post-processing

### PostProcessStack

`new PostProcessStack()` creates an empty stack.

| Member | Description |
| --- | --- |
| `Add(PostProcessEffect effect)` | Appends an effect. |
| `Remove(PostProcessEffect effect)` | Removes an effect; returns whether it was present. |
| `Clear()` | Removes every effect. |
| `Effects` | Read-only effect list in execution order. |
| `Enabled` | Enables or disables the whole stack. |

A camera snapshots the stack when it is created. Later changes affect later cameras.

### PostProcessEffect

Base class for stack effects. Exposes `bool Enabled`.

### ShaderPostProcessEffect

`new ShaderPostProcessEffect(Shader shader)`. Runs a custom full-screen fragment shader. The shader
must declare `sourceColor`, may declare `sourceDepth`, and must output scene-linear HDR. Exposes
`Shader`.

### SsaoEffect

`new SsaoEffect()` creates ambient occlusion with default settings. Needs a depth attachment.

| Member | Description |
| --- | --- |
| `Quality` | `SsaoQuality` preset. |
| `Radius` | Sample radius in world units. |
| `ScreenSpaceRadius` | Interprets `Radius` in screen space. |
| `Intensity` | Occlusion strength. |
| `BiasOffset`, `BiasMultiplier` | Depth bias to avoid self-occlusion. |
| `DistanceFalloff` | Falloff with distance. |
| `DenoiseRadius` | Bilateral denoise radius. |
| `Color` | Occlusion tint. |

### SsaoQuality

`Performance = 0`, `Low = 1`, `Medium = 2`, `High = 3`, `Ultra = 4`.

### BloomEffect

`new BloomEffect()` creates bloom with default settings.

| Member | Description |
| --- | --- |
| `Threshold` | HDR brightness where bloom starts. |
| `SoftKnee` | Soft threshold width in `[0, 1]`. |
| `Intensity` | Bloom contribution. |
| `Scatter` | Pyramid scatter. |
| `Tint` | Bloom color. |

### TonemappingMode

`AgX = 0`, `None = 1`. The mode is fixed when the app is built.

### TonemappingSettings

`Exposure` (stops), `AgxWhite` (scene-linear white point), `AgxContrast` (curve contrast). Values are
snapshotted per frame.

## Math

### Transform

| Member | Description |
| --- | --- |
| `new Transform(Vector3 position)` | Translation only. |
| `new Transform(Vector3 position, Quaternion rotation, Vector3 scale)` | Full transform. |
| `Identity` | No translation, rotation, or scale. |
| `Position`, `Rotation`, `Scale` | Local components. |
| `Forward`, `Up`, `Right` | Rotated unit axes. |
| `ToMatrix()` | Local-to-parent matrix. |
| `TransformPoint(Vector3)`, `TransformDirection(Vector3)` | Apply the transform. |
| `Inverse()` | Inverse transform. |
| `WithPosition(Vector3)`, `WithRotation(Quaternion)`, `WithScale(Vector3)` | Copies with one component changed. |
| `WithParent(Transform parent)` | Composes this transform with a parent. |
| `Lerp(Transform a, Transform b, float t)` | Interpolates position, rotation, and scale. |

### Color

`Color` is a linear RGBA color stored as a `Vector4`. Components may exceed 1 for HDR.

| Member | Description |
| --- | --- |
| `new Color(float r, float g, float b)` | Linear RGB with alpha 1. |
| `new Color(float r, float g, float b, float a)` | Linear RGBA. |
| `R`, `G`, `B`, `A` | Components. |
| `this[int index]` | Component by index 0-3. |
| `Grayscale` | Luminance using Rec. 601 weights. |
| `MaxColorComponent` | Largest of the four components. |
| `FromSrgb(float r, float g, float b, float a = 1)` | Converts sRGB-encoded components in `[0, 1]` to linear. |
| `FromSrgbBytes(byte r, byte g, byte b, byte a = 255)` | Converts 8-bit sRGB to linear. |
| `ToSrgb()` | Returns the sRGB-encoded `Vector4`. |
| `FromOkLab(float l, float a, float b, float alpha = 1)`, `ToOkLab(out l, out a, out b)` | OkLab conversion. |
| `Lerp(Color a, Color b, float t)` | Clamped component lerp. |
| `LerpUnclamped(...)` | Unclamped component lerp. |
| `LerpOkLab(...)`, `LerpOkLabUnclamped(...)` | OkLab lerp, clamped or not. |
| `HSVToRGB(float h, float s, float v, bool hdr = false)` | HSV to linear color. |
| `RGBToHSV(Color color, out h, out s, out v)` | Linear color to HSV. |
| `operator +`, `-`, `*`, `/` | Component-wise color arithmetic; division takes a scalar, multiplication accepts a scalar or color. |
| `implicit operator Vector4(Color)` / `implicit operator Color(Vector4)` | Converts to and from a raw `Vector4`. |

Named presets are display-authored sRGB colors converted to linear:

```text
AliceBlue AntiqueWhite Aquamarine Azure Beige Bisque Black BlanchedAlmond Blue BlueViolet Brown
Burlywood CadetBlue Chartreuse Chocolate Clear Coral CornflowerBlue Cornsilk Crimson Cyan DarkBlue
DarkCyan DarkGoldenRod DarkGray DarkGreen DarkKhaki DarkMagenta DarkOliveGreen DarkOrange DarkOrchid
DarkRed DarkSalmon DarkSeaGreen DarkSlateBlue DarkSlateGray DarkTurquoise DarkViolet DeepPink
DeepSkyBlue DimGray DodgerBlue Firebrick FloralWhite ForestGreen Gainsboro GhostWhite Gold GoldenRod
Gray Gray1 Gray2 Gray3 Gray4 Gray5 Gray6 Gray7 Gray8 Gray9 Green GreenYellow Grey Honeydew HotPink
IndianRed Indigo Ivory Khaki Lavender LavenderBlush LawnGreen LemonChiffon LightBlue LightCoral
LightCyan LightGoldenRod LightGoldenRodYellow LightGray LightGreen LightPink LightSalmon LightSeaGreen
LightSkyBlue LightSlateBlue LightSlateGray LightSteelBlue LightYellow LimeGreen Linen Magenta Maroon
MediumAquamarine MediumBlue MediumOrchid MediumPurple MediumSeaGreen MediumSlateBlue
MediumSpringGreen MediumTurquoise MediumVioletRed MidnightBlue MintCream MistyRose Moccasin
NavajoWhite NavyBlue OldLace Olive OliveDrab Orange OrangeRed Orchid PaleGoldenRod PaleGreen
PaleTurquoise PaleVioletRed PapayaWhip PeachPuff Peru Pink Plum PowderBlue Purple RebeccaPurple Red
RosyBrown RoyalBlue SaddleBrown Salmon SandyBrown SeaGreen Seashell Sienna Silver SkyBlue SlateBlue
SlateGray Snow SoftBlue SoftGreen SoftRed SoftYellow SpringGreen SteelBlue Tan Teal Thistle Tomato
Transparent Turquoise Violet VioletRed Wheat White WhiteSmoke Yellow YellowGreen YellowNice
```

### Rect

`new Rect(float x, float y, float width, float height)`, `Rect.FromSize(width, height)`. Members:
`X`, `Y`, `Width`, `Height`, `Position`, `Size`, `Right`, `Bottom`, `Contains(Vector2)`.

### Ray

`new Ray(Vector3 origin, Vector3 direction)`. Members: `Origin`, `Direction`. The engine normalizes
directions where needed.

### RayCollision

`Hit`, `Distance`, `Point`, `Normal`. A miss has `Hit = false` and a zero distance.

### BoundingBox

`new BoundingBox(Vector3 min, Vector3 max)`. Members: `Min`, `Max`.

### BoundingFrustum

`new BoundingFrustum(Matrix4x4 viewProjection)`. Members: `Left`, `Right`, `Top`, `Bottom`, `Near`,
`Far` planes, `Contains(Vector3 point)`, `Intersects(BoundingBox box)`.

### Collision

| Member | Description |
| --- | --- |
| `CheckBoxes(BoundingBox a, BoundingBox b)` | Axis-aligned box overlap. |
| `CheckBoxSphere(BoundingBox box, Vector3 center, float radius)` | Box/sphere overlap. |
| `CheckSpheres(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB)` | Sphere/sphere overlap. |
| `GetRayBox(Ray ray, BoundingBox box)` | Ray/box hit. |
| `GetRayMesh(Ray ray, Mesh mesh, Matrix4x4 transform)` | Ray/mesh hit using baked or generated acceleration data. |
| `GetRayPlane(Ray ray, Plane plane)` | Ray/plane hit. |
| `GetRaySphere(Ray ray, Vector3 center, float radius)` | Ray/sphere hit. |
| `GetRayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c)` | Ray/triangle hit. |
| `GetRayQuad(Ray ray, Vector3 a, Vector3 b, Vector3 c, Vector3 d)` | Ray/quad hit. |

### QuaternionExtensions

| Member | Description |
| --- | --- |
| `Euler(float x, float y, float z)` | Quaternion from Euler degrees. |
| `AngleAxis(float degrees, Vector3 axis)` | Quaternion from an axis and angle. |
| `LookRotation(Vector3 forward, Vector3 upwards)` | Rotation looking along forward. |
| `FromToRotation(Vector3 from, Vector3 to)` | Shortest rotation between directions. |
| `Slerp(Quaternion target, float t)` | Spherical interpolation. |
| `RotateTowards(Quaternion target, float maxDegreesDelta)` | Rotates toward target with a step limit. |
| `Angle(Quaternion other)` | Angle in degrees. |
| `ToAngleAxis(out float angle, out Vector3 axis)` | Decomposes into axis and angle. |
| `EulerAngles` | Euler angles in degrees. |
| `Normalized` | Normalized quaternion. |

### Vector3Extensions

| Member | Description |
| --- | --- |
| `Forward`, `Back`, `Up`, `Down`, `Left`, `Right` | Unit direction constants. |
| `Magnitude`, `SqrMagnitude` | Length and squared length. |
| `Normalized` | Unit vector. |
| `Xz` | `(x, 0, z)`. |
| `WithX(float)`, `WithY(float)`, `WithZ(float)` | Copies with one component changed. |
| `Angle(Vector3 other)` | Unsigned angle in degrees. |
| `SignedAngle(Vector3 other, Vector3 axis)` | Signed angle around an axis. |
| `ClampMagnitude(float maxLength)` | Limits length. |
| `Project(Vector3 normal)` | Projects onto a normal. |
| `ProjectOnPlane(Vector3 normal)` | Projects onto a plane. |
| `Reflect(Vector3 normal)` | Reflects around a normal. |
| `Rotated(Quaternion rotation)` | Rotates the vector. |

## Diagnostics

### EngineInfo

`new EngineInfo(string Name, Version Version, string RendererName, string GpuName,
uint VulkanApiVersion, string SwapchainFormat, bool HdrOutput, int SampleCount,
PresentMode PresentMode, bool ValidationEnabled)`. Members: `Name`, `Version`, `RendererName`,
`GpuName`, `VulkanApiVersion`, `SwapchainFormat`, `HdrOutput`, `SampleCount`, `PresentMode`,
`ValidationEnabled`.

### EngineVersion

`Current` returns the engine `Version`.

### RenderStats

`new RenderStats(...)` takes every field in declaration order. Members: `FrameIndex`,
`PresentedFrames`, `DrawCalls`, `Instances`, `Triangles`, `CulledInstances`, `PipelineCount`,
`TextureCount`, `SampleCount`, `HdrOutput`, `CpuFrameMilliseconds`, `GpuFrameMilliseconds`,
`FramesInFlight`, `RecoveryCount`.

### PresentMode

`Mailbox = 0`, `Fifo = 1`, `Immediate = 2`.

