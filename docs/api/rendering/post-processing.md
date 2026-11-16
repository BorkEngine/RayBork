# Rendering: Post-processing

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.13 Camera post-processing

Camera post-processing is an app-lifetime stack of effects that runs after a camera's geometry and
resolve pass and before the next camera or the present pass. M8 ships the extensible core and the
custom-shader effect; SSAO and bloom are later M8 tasks built on the same core.

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
corrupt the recorded frame.

**Custom shader contract.** A `ShaderPostProcessEffect` takes an ordinary baked engine shader with
both the vertex and fragment stages. It is drawn once as a fullscreen pass through the engine's
fullscreen quad:

- The sidecar must declare the reserved texture binding `sourceColor`. The engine binds it to the
  camera target's current resolved color, or to the previous effect's output in a chain.
- The sidecar may declare the optional reserved texture binding `sourceDepth`. The engine binds it to
  the camera target's resolved single-sample reverse-Z depth image. A target created with
  `RenderTextureDescription.Depth` `false` has no depth, so a shader that declares `sourceDepth` is
  rejected when the camera is created.
- The engine always overrides both reserved names. A texture the material cursor holds for
  `sourceColor` or `sourceDepth` is ignored, not sampled; every other uniform and texture binding comes
  from the shader material and is frozen at camera creation.
- The shader's baked default render state is ignored. The post pass forces opaque blending, no culling,
  and no depth test or depth write, because it covers the whole target and has no depth attachment.
- The fragment output is scene-linear HDR color with alpha. The engine applies no exposure, AgX, gamma,
  or sRGB encoding to the chain; the present pass owns display conversion. Values above `1` survive
  intermediate passes because the post targets are `Rgba16Float`.

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
the shader and the missing requirement. A disposed shader or a disposed texture binding is
`ObjectDisposedException`. `EngineContext.CreateCamera` throws the same way for the camera's own
arguments.

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
2. Each enabled custom effect ping-pongs through two target-sized, single-sample `Rgba16Float` post
   color targets with a fullscreen pass.
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
chains on targets of different sizes: the post-target pool keeps a pair per target extent, so a camera
targeting the scene target and another targeting a render texture in the same frame each process their
own content. A render texture written by a camera with a stack can be sampled by a later
camera through its `ColorTexture`, and `ReadColorsAsync`/`ReadPixelsAsync` read the post-processed
color through the same resolved image and tone snapshot as a non-post-processed write. A target's
depth is never modified by the chain, so `DepthTexture` and a later SSAO-style consumer still see the
geometry depth.

**Ownership and retention.** A stack, an effect, and a `ShaderPostProcessEffect` own no engine asset.
They never dispose the shader. The recorded frame retains the effect's internal shader program and every
texture the material had bound, exactly like a draw record, so disposing the material or a texture after
camera creation leaves the recorded frame valid until the frame completes.

**Built-in effects.** SSAO and bloom are later M8 tasks. They use the same stack, snapshot,
and store-back core: an internal effect runner rents target-sized post targets from the pool, records
its own pipeline passes into them, and completes the final target, after which the engine runs the same
store-back. This core is generic in the number and order of effects; it does not special-case a custom
shader effect beyond the one fullscreen pass it records.
