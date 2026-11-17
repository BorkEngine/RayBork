# Rendering: Post-processing

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.13 Camera post-processing

Camera post-processing is an app-lifetime stack of effects that runs after a camera's geometry and
resolve pass and before the next camera or the present pass. M8 ships the extensible core, the
custom-shader effect, and the built-in SSAO and bloom effects.

```csharp
public sealed class PostProcessStack {
    public PostProcessStack();
    public bool Enabled { get; set; }
    public IReadOnlyList<PostProcessEffect> Effects { get; }
    public void Add(PostProcessEffect effect);
    public bool Remove(PostProcessEffect effect);
    public void Clear();
}

public abstract class PostProcessEffect {
    public bool Enabled { get; set; }
}

public enum SsaoQuality {
    Performance,
    Low,
    Medium,
    High,
    Ultra
}

public sealed class SsaoEffect : PostProcessEffect {
    public SsaoEffect();
    public SsaoQuality Quality { get; set; } = SsaoQuality.Medium;
    public float Radius { get; set; } = 1.5f;
    public float DistanceFalloff { get; set; } = 1f;
    public float Intensity { get; set; } = 2f;
    public Color Color { get; set; } = Color.Black;
    public int DenoiseRadius { get; set; } = 12;
    public bool ScreenSpaceRadius { get; set; }
    public float BiasOffset { get; set; }
    public float BiasMultiplier { get; set; } = 1f;
}

public sealed class BloomEffect : PostProcessEffect {
    public BloomEffect();
    public float Threshold { get; set; } = 1f;
    public float SoftKnee { get; set; } = 0.5f;
    public float Intensity { get; set; } = 0.7f;
    public float Scatter { get; set; } = 0.7f;
    public Color Tint { get; set; } = Color.White;
}

public sealed class ShaderPostProcessEffect : PostProcessEffect {
    public ShaderPostProcessEffect(Shader shader);
    public Shader Shader { get; }
}
```

```csharp
public Camera CreateCamera(
    Transform transform,
    CameraProjection projection,
    float fieldOfView = 0f,
    float orthographicHeight = 0f,
    ClearMode clearMode = ClearMode.ColorAndDepth,
    Color? clearColor = null,
    float near = 0.01f,
    float far = 1000f,
    Rect? viewport = null,
    RenderTexture? target = null,
    PostProcessStack? postProcess = null);
```

```csharp
private readonly PostProcessStack _post = new();
private Shader? _invert;

public async Task Setup(EngineContext ctx) {
    _invert = await ctx.Assets.Load<Shader>("/shaders/invert");
    _post.Add(new ShaderPostProcessEffect(_invert));
}

public void Tick(EngineContext ctx) {
    using (Camera camera = ctx.CreateCamera(
        Transform.Identity,
        CameraProjection.Perspective,
        fieldOfView: 60f,
        postProcess: _post)) {
        camera.DrawModel(_model, Transform.Identity);
    }
}

public Task Shutdown() {
    // The stack and the effect never dispose the shader; the caller owns it.
    _invert?.Dispose();
    return Task.CompletedTask;
}
```

**The stack.** One stack lives for the app lifetime and is owned by the caller; it is not an asset and
is not disposable. `Effects` is a read-only view in run order. `Add` rejects `null` and allows
duplicates, so adding the same effect twice runs it twice. `Remove` removes the first entry that is the
same object and reports whether it removed one. `Clear` removes every effect and leaves the stack
usable. Effects run in stack order: each enabled custom effect draws one fullscreen pass over the
previous result, so two effects are two passes and a full chain is multi-pass.

**Threading and snapshot timing.** The stack, `Enabled`, and every effect are tick-thread only; a
mutation from any other thread is `InvalidOperationException`. A camera snapshots the stack when it is
created: `PostProcessStack.Enabled`, the effect order, each `PostProcessEffect.Enabled` value, and each
custom effect's shader cursor (uniforms) and texture bindings are frozen for that camera. Later stack,
effect, or material changes apply to later cameras only; an already recorded frame keeps what it
captured. A disabled stack captures no effects, and a disabled effect captures a skipped entry, so it
does not shift the captured order. Disposing the effect's shader after the camera was created does not
corrupt the recorded frame. Snapshots are pooled per effect and reused after the recorded frame is
released, so steady-state camera creation does not allocate a new snapshot, uniform buffer, or texture
array.

**Built-in SSAO.** `SsaoEffect` is the engine's N8AO 2.1.0-derived, full-resolution SSAO effect. It
reconstructs view-space positions and normals from the camera target's resolved reverse-Z depth (no
normal buffer), rotates a golden-angle hemisphere kernel with the N8AO blue-noise texture, applies a
range check and bias, runs two bilateral denoise iterations, and composites the AO over its chain
input. That input is the original scene color when SSAO is first and the previous effect's output
otherwise. Perspective and orthographic cameras use the same path; `near`/`far` and the projection
kind come from the camera's projection matrix.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Quality` | `Medium` | Selects a baked AO/denoise variant. |
| `Radius` | `1.5` | World-space AO radius, or a pixel radius when `ScreenSpaceRadius` is `true`. Must be finite and greater than zero. |
| `DistanceFalloff` | `1` | How quickly AO fades with distance relative to the radius. Must be finite and greater than zero. World-radius mode uses `0.2 * radius * DistanceFalloff`; screen-space mode uses `radius * DistanceFalloff`. |
| `Intensity` | `2` | Composite exponent: `pow(ao, Intensity)`. `0` leaves the scene unchanged. Must be finite and non-negative. |
| `Color` | `Color.Black` | AO tint multiplied into occluded scene color. All components must be finite. |
| `DenoiseRadius` | `12` | Denoise tap radius in screen pixels, independent of quality. `0` leaves each denoise pass at the center pixel. Must be non-negative. |
| `ScreenSpaceRadius` | `false` | Treats `Radius` as pixels and reconstructs its world size from the center depth. |
| `BiasOffset` | `0` | Constant bias added to the depth comparison. Must be finite. |
| `BiasMultiplier` | `1` | Multiplier on the normal-derivative bias heuristic. Must be finite. |

`DenoiseRadius` is independent of `Quality` and keeps the approved default `12`. N8AO's own High and
Ultra presets also switch the denoise radius to `6`; RayBork does not apply that preset value, so the
public setting remains the single source of truth.

`Quality` maps to compile-time shader variants, never a runtime sample loop:

| Quality | AO samples | Denoise samples per iteration | Denoise iterations |
| --- | ---: | ---: | ---: |
| `Performance` | 8 | 4 | 2 |
| `Low` | 16 | 4 | 2 |
| `Medium` | 16 | 8 | 2 |
| `High` | 64 | 8 | 2 |
| `Ultra` | 64 | 16 | 2 |

The AO kernels, denoise Poisson disk, and sample counts are GLSL constants in the committed SPIR-V
variants. There is one AO variant for 8, 16, and 64 samples, one denoise variant for 4, 8, and 16
samples, and one composite variant. The seven shader assets and the N8AO 128x128 RGBA8 blue-noise
texture are engine-owned built-in assets preloaded from the built-in pack before `IGame.Setup`; the
effect has no public `Shader` parameter and never compiles or loads an asset on the render thread.
Every setting and `Quality` is frozen when the camera is created, so later changes affect later
cameras only.

The effect **requires a camera target with a depth attachment**. A `RenderTexture` created with
`Depth: false` is rejected from `CreateCamera` with `InvalidOperationException` before the pass is
recorded. The app scene target always has depth. SSAO preserves the depth image, so a later camera
can still sample it; the AO, denoise, and composite passes rent two camera-target-sized
`Rgba16Float` post targets and release them through the engine post-target pool.

SSAO is several fullscreen passes over the full target, so its cost scales with target pixels:
`Ultra` does 64 AO samples and two 16-tap denoise iterations per pixel. Prefer `Performance` or `Low`
on integrated GPUs and `Medium` as the balanced default. Shader modules are compiled ahead of time,
but the engine creates an SSAO or denoise graphics pipeline lazily on the render thread the first time
a quality variant runs; the pipeline cache then retains it. The effect is on by default in the Castle
and PrimitiveGallery samples; `A` toggles `SsaoEffect.Enabled` and the status line reports the state.
The HUD camera in each sample has no post-process stack. Disabling the effect or the stack skips the
whole chain and renders the geometry result unchanged.

**Built-in bloom.** `BloomEffect` is the engine's fast dual-filter bloom. It thresholds and soft-knees
the chain input into a half-resolution level, downsamples through the quarter, eighth, 1/16, and 1/32
extents (each clamped to at least one pixel), tent-upsamples back to half resolution with `Scatter` as
the coarse-level contribution, and adds the result to the chain input. The composite target is the
camera target's size, so the output is scene-linear HDR and flows through the same store-back, later
camera sample, and readback path as every other effect. A normal target runs up to ten fullscreen
passes (one prefilter, four downsamples, four upsamples, and one composite); a small target whose
clamped extent stops shrinking skips the repeated level and runs fewer passes.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Threshold` | `1` | Scene-linear brightness the bloom starts from. Must be finite and non-negative; `0` keeps every lit pixel. |
| `SoftKnee` | `0.5` | Knee width as a fraction of `Threshold`. Must be finite and non-negative; `0` is a hard threshold and a larger value blends the discarded range in smoothly. |
| `Intensity` | `0.7` | How much bloom is added to the chain input. Must be finite and non-negative; `0` leaves the scene unchanged. |
| `Scatter` | `0.7` | Weight of each coarser level in the tent upsample. Must be finite and non-negative; `0` keeps only the half-resolution prefilter level and therefore a tight halo. |
| `Tint` | `Color.White` | Color multiplied into the bloom before it is added. All components must be finite. |

The prefilter, downsample, upsample, and composite programs and their shared fullscreen vertex payload
are engine-owned built-in assets preloaded from the built-in pack before `IGame.Setup`. The effect has
no public `Shader` parameter and never compiles or loads an asset on the render thread. The downsample
and upsample shaders use fixed unrolled taps, so no pass contains a dynamic loop. Every setting is
frozen when the camera is created, so later changes apply to later cameras only. The effect has no
depth requirement: it works on a depth-bearing target and on a `RenderTexture` created with `Depth:
false`.

The effect rents from the same three-target, single-sample `Rgba16Float` per-extent post set as SSAO
and the custom path, and the rent skips the current chain source. The prefilter and every pyramid level
are auxiliary targets released with `CompleteAuxiliaryTarget` after their pass; the camera-target-sized
composite target is completed with `CompleteColorTarget` and becomes the chain source. A custom effect
or SSAO before bloom therefore keeps its output as bloom's input, and running bloom twice chains two
pyramids while the engine store-back runs once. The effect costs one prefilter, up to four downsamples,
up to four upsamples, and one composite pass per camera with the effect; the passes at 1/4 to 1/32 of
the target keep the coarse levels cheap, and the half-resolution prefilter and composite dominate the
cost. The pyramid's per-extent target sets and the camera-target-sized set are pooled per frame ring
slot and extent, so the steady state allocates no targets, images, or descriptors, but the pool holds
three targets for every extent the effect uses (five pyramid levels plus the composite extent) on each
frame ring slot. Disabling the effect or the stack skips the whole chain and its target rental.

`Pong` enables bloom on its scene camera only and toggles it with `B`; its HUD camera has no stack, so
the scores and text are never bloomed. The sample flashes scene-linear HDR ball, paddle, and wall
colors on collision; the values are above the default threshold, so the flash blooms and the ordinary
playfield colors stay below it.

**Custom shader contract.** A `ShaderPostProcessEffect` takes an ordinary baked engine shader with
both the vertex and fragment stages. It is drawn once as a fullscreen pass through the engine's
fullscreen quad:

- The sidecar must declare the reserved texture binding `sourceColor`. The engine binds it to the
  camera target's current resolved color, or to the previous effect's output in a chain.
- The sidecar may declare the optional reserved texture binding `sourceDepth`. The engine binds it to
  the camera target's resolved single-sample reverse-Z depth image. A target created with
  `RenderTextureDescription.Depth` `false` has no depth, so a shader that declares `sourceDepth` is
  rejected when the camera is created.
- The engine always overrides both reserved names. A value the material cursor holds for `sourceColor`
  or `sourceDepth` is ignored: it is never sampled, validated, retained for the frame, or prepared as a
  sampled render target. Every other uniform and texture binding comes from the shader material and is
  frozen at camera creation.
- The shader's baked default render state is ignored. The post pass forces opaque blending, no culling,
  and no depth test or depth write, because it covers the whole target and has no depth attachment.
- The fragment output is scene-linear HDR color with alpha. The engine applies no exposure, AgX, gamma,
  or sRGB encoding to the chain; the present pass owns display conversion. Values above `1` survive
  intermediate passes because the post targets are `Rgba16Float`. A render target whose color format is
  `Rgba8Srgb` still applies the fixed-function sRGB encode when the store-back writes its attachment,
  exactly like any other write to that target; an `Rgba8Unorm` target stores the linear values.

The engine fullscreen quad has positions in `[-0.5, 0.5]` in local XY and UVs in `[0, 1]` with
`(0, 0)` at the image top-left. The vertex stage must map it with:

```glsl
#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aUv;

layout(location = 0) out vec2 vUv;

void main_vertex() {
    vUv = aUv;
    gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);
}
```

A fragment stage samples the reserved bindings through the standard shader ABI macros:

```glsl
#version 450

layout(location = 0) in vec2 vUv;

layout(location = 0) out vec4 oColor;

void main_fragment() {
    vec3 scene = texture(sourceColor, vUv).rgb;
    oColor = vec4(vec3(1.0) - scene, 1.0);
}
```

The shader sidecar declares the reserved names like any other texture binding:

```toml
# /shaders/invert.toml
[shader]
vertex = "invert.vert"
fragment = "invert.frag"
vertex_entry = "main_vertex"
fragment_entry = "main_fragment"

[[shader.textures]]
name = "sourceColor"
```

**Errors.** A camera whose enabled custom effect does not declare `sourceColor`, whose shader is
missing the vertex or fragment stage, or whose shader declares `sourceDepth` for a depth-less target
throws `InvalidOperationException` from `CreateCamera` before the pass is recorded; the message names
the shader and the missing requirement. A disposed shader or a disposed texture bound to a non-reserved
binding is `ObjectDisposedException`; a stale value on a reserved binding is ignored.
`EngineContext.CreateCamera` throws the same way for the camera's own arguments.

**Execution and content.** Effects run after the camera's geometry pass and its color/depth resolve,
and before the next camera pass or the present pass. A camera with `ClearMode.None` sees the earlier
target content plus its own draws, and its chain processes exactly that resolved content; a camera with
a color clear processes the new content. Several cameras with effects on the same target run in
creation order, and each chain processes the target content at that point in the frame. A later HUD
camera without a stack is unaffected; a HUD camera with a stack processes the current target content,
including the earlier scene and the HUD draw, and stores the result back for its viewport.

**Target-sized chain, viewport store-back, and M8 cross-viewport behavior.** The chain is always the
size of the camera target, not the size of the camera viewport:

1. The camera target's resolved color and depth are the source.
2. Each enabled effect rents a target from the three-target, single-sample `Rgba16Float` post set for
   the target extent, skipping the current chain source. A custom chain therefore ping-pongs through
   two of the three targets with one fullscreen pass per effect; a four-pass built-in can use the two
   non-source targets while the prior output stays intact.
3. The final result is stored back into the target's color attachment for the camera viewport only: the
   target's MSAA color image plus its resolve, or the single-sample color image. The store-back samples
   the matching sub-rectangle of the final post target, so a viewport camera's result is aligned with
   the target pixels. Pixels outside the camera viewport are untouched.
4. After the store-back the target's resolved color is `ShaderReadOnlyOptimal` again for a later
   camera or the present pass, and the target's depth is unchanged. The store-back writes color, so it
   sets the target's "written" flag and tone snapshot: `ReadColorsAsync` and `ReadPixelsAsync` include
   the post-processed output.

Because the chain is target-sized, a viewport camera's effect samples the target's full content,
including pixels outside its own viewport. A blur or similar neighborhood effect can therefore read
across the viewport boundary and bleed into its result near the edge. This is documented M8 behavior,
not a bug: per-viewport source copies are a follow-up. A viewport camera with effects is supported and
not rejected; only the store-back is viewport-scoped.

**Targets and readback.** Effects work identically on the app scene target and on a
`RenderTexture` target. The chain is sized to the target the camera writes, and one frame can run
chains on targets of different sizes: the post-target pool keeps a target set per target extent, so a camera
targeting the scene target and another targeting a render texture in the same frame each process their
own content. A render texture written by a camera with a stack can be sampled by a later
camera through its `ColorTexture`, and `ReadColorsAsync`/`ReadPixelsAsync` read the post-processed
color through the same resolved image and tone snapshot as a non-post-processed write. A target's
depth is never modified by the chain, so `DepthTexture` and the built-in SSAO effect still see the
geometry depth.

**Ownership and retention.** A stack, an effect, and a `ShaderPostProcessEffect` own no engine asset.
They never dispose the shader. The recorded frame retains the effect's internal shader program and every
non-reserved texture the material had bound, exactly like a draw record, so disposing the material or a
texture after camera creation leaves the recorded frame valid until the frame completes.

**Built-in effects.** `SsaoEffect` and `BloomEffect` are built-in effects: engine code with an approved
public effect type, backed by an internal snapshot rather than the custom-shader path. Their snapshots
subclass the internal effect snapshot and use the internal per-pass context. The shipped core gives a
multi-pass effect everything it needs without touching the public stack, the camera snapshot path, or
the store-back:

- `RentColorTarget()` for a camera-target-sized target and `RentColorTarget(Extent2D)` for an
  intermediate from the same per-extent pool (three targets per extent, rotating and skipping the
  current chain source);
- the Vulkan device, the color-only post render pass, the engine pipeline layout, the forced post render
  state, the shared frame descriptor set, the engine fullscreen quad, and
  `GetOrCreateFullscreenPipeline` for a built-in shader;
- `ReserveInstanceRecord`/`GetInstanceRecordSpan` for one shader-ABI instance record per pass and
  `RecordFullscreenPass` to draw it into a target;
- `CompleteAuxiliaryTarget` to release an intermediate and `CompleteColorTarget` to make a
  camera-target-sized result the chain source, after which the engine runs the same store-back.

SSAO records four passes through the three-target per-extent set, skipping the current chain source:
AO into a non-source target, denoise into the next, the second denoise back into the first target,
and the composite into the last target. The two non-source targets alternate, so the prior effect's
completed image is never overwritten. The AO and denoise targets are auxiliary and become readable
before the next pass samples them; the composite target is completed as the chain source and the
engine's single store-back writes it to the camera viewport. The chain-input color slot is captured
before the first completion, so the composite samples the previous effect's output (or the original
scene color when SSAO is first). SSAO reports four ordered instance-record entries (AO, denoise,
denoise, composite) so the frame's instance buffer is sized for every pass even though the denoise
entry repeats.

The effect snapshot reports its ordered instance-record layout (per-pass stride and count), so a
built-in can append passes with different shaders and uniform sizes and the frame's instance buffer is
sized for all of them. A built-in supplies its own shaders, effect type, and pass scheduling. The
fullscreen pipeline cache is keyed by the baked shader asset and the forced post render state, so the
per-quality AO and denoise programs and the four bloom programs are cached without a second selector.
Bloom reports its maximum ten-entry layout (prefilter, four downsample, four upsample, and composite
strides) so a small target that runs fewer passes still fits the same frame buffer.
