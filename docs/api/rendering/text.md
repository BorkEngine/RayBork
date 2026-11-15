# Rendering: Text

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.10 Fonts and text

```csharp
public sealed class Font : IAsset {
    public string Path { get; }
    public float LineHeight { get; }
    public float Ascent { get; }
    public float Descent { get; }
    public int GlyphCount { get; }
    public int CachedGlyphCount { get; }
    public bool IsDisposed { get; }

    public bool TryGetGlyph(Rune rune, out Glyph glyph);
    public Vector2 Measure(ReadOnlySpan<char> text, float size);
    public void Dispose();
}

public readonly struct Glyph {
    public Rune Rune { get; }
    public Vector2 Advance { get; }
    public Vector2 Bearing { get; }
    public Vector2 Size { get; }
    public Vector2 Uv0 { get; }
    public Vector2 Uv1 { get; }
}

public enum TextAlignment {
    Left,
    Center,
    Right,
}

public enum TextWrapping {
    None,
    Word,
    Character,
}
```

Fonts are TTF/OTF assets. The bake copies the whole face file and records the MSDF settings; the runtime
parses the face once and generates MSDF glyph textures on demand. There is no preload range and no
`ranges` sidecar field: every glyph the face's cmap maps can be generated at runtime.

- One glyph is one small texture, 32 px on its longest side by default, with a baked distance range and
  padding. There is no atlas. Glyph textures are bindless, full-texture quads sampled `0..1`, and are
  referenced per text instance.
- The texture side is the complete padded texture: the default `msdf_pixel_size` is `32`, `msdf_range`
  is `4`, and `msdf_padding` is `2`. The glyph box is fitted into `pixelSize - 2 * padding` pixels and
  the authored range maps to the normalized field, so generation is deterministic and the field is
  the exact nearest-edge distance per channel.
- The shader reconstructs the glyph edge from the median of the three sampled distance channels. The
  field is normalized with `0.5` as the edge and values above `0.5` inside; the antialiasing band
  comes from the screen-space derivative, so there is no range, pixel-size, or per-font uniform. The
  generator does not extend an edge past its endpoints (no pseudo-distance corner extension), so
  bilinear filtering can round a sharp corner at very large sizes. That is the documented MSDF
  quality limitation.
- Each font hard-caches 512 generated glyph textures and evicts the least-recently-used entries when
  it inserts a new one. Every glyph texture is a bindless slot, so the per-font cache is also a
  descriptor budget within the engine's fixed 2048-slot table; a full table fails the upload with
  `AssetLoadException` `RB1010`.
- `Load<Font>` is a CPU-only engine asset load: it parses the face and generates no glyph texture, so it
  performs no GPU upload and the font object that load creates reports `CachedGlyphCount` `0`. A shared
  font keeps one cache for its lifetime, so a repeated load returns the shared object, whose count
  reflects the glyphs earlier draws already requested.
- Glyph generation is asynchronous: the engine's `RayBork.Font` thread generates and uploads textures
  while the tick thread keeps running. `DrawText` never waits: a glyph whose texture is not ready is
  skipped for that call and appears on a later frame, while the pen still advances from the face
  metrics. `Acquire` adds the frame hold for a cached glyph before it returns, so a recorded draw keeps
  its glyph texture's GPU resource alive through the frame.
- An unmapped rune falls back to glyph id 0 (`.notdef`), so every rune still advances the pen and draws
  the missing-glyph quad once that texture is ready.
- `GlyphCount` is the number of Unicode codepoints the face's cmap maps to a real glyph; it counts
  mapped codepoints, not distinct glyph ids. `CachedGlyphCount` is the number with a generated MSDF
  texture; it grows as draws request glyphs and can fall when the per-font texture cache evicts its
  least recently used entries.
- `TryGetGlyph` returns only cached glyphs; an empty glyph such as a space has no texture and is not
  returned. `Measure` works from face metrics and kerning without generating textures.
- `Glyph` values are in em units. `Advance` is the horizontal pen advance. `Bearing` is the padded
  quad's bottom-left corner relative to the pen position on the baseline. `Size` is the padded quad
  size. For a glyph returned by `TryGetGlyph`, `Uv0` is `(0, 0)` and `Uv1` is `(1, 1)` because one
  glyph is one whole texture. `Rune` echoes the rune that produced the value; for an unmapped rune it
  is the requested rune while the geometry comes from `.notdef`. A `default(Glyph)` value is not a
  usable glyph: it has a zero rune, zero vectors, and zero UVs.
- Metrics (`LineHeight`, `Ascent`, `Descent`) are in em units:
  `(ascender - descender + lineGap) / unitsPerEm`, `ascender / unitsPerEm`, and
  `-descender / unitsPerEm` from the `hhea`/`head` tables. `Measure` and `DrawText` multiply them by
  the requested `size`.
- Kerning is applied. Ligatures, contextual shaping (Arabic, Indic), bidi, font fallback, color
  fonts, and emoji are post-1.0. Latin/Cyrillic/Greek text maps codepoint-to-glyph directly.
- There is no hinting. MSDF antialiasing uses screen-space derivatives.
- `size` is the em height in world units. A `size` of `1` means one world unit per em.
- Text is world space. `DrawText` lays the text in the transform's local XY plane, with +X to the
  right and +Y up; the readable face points toward local +Z.
- `maxWidth` is the alignment box width whenever it is positive, in every wrapping mode; it is in
  world units at the requested `size`. `Word` and `Character` require `maxWidth > 0`. `None` with
  `maxWidth == 0` aligns lines against the widest line.
- `DrawTextBillboard` lays out the text in a camera-facing plane. It is the same layout and
  batching path with a billboard transform.
- Text batching uses the built-in MSDF shader. All text with the same quad, program, and render state
  can be one instanced draw, regardless of font, size, or string; each glyph instance carries its
  bindless texture index as its per-instance texture data, and the shader samples the whole glyph
  texture without a per-instance UV rectangle.
- Fonts are shared assets. The built-in Fira Sans Regular face is available as
  `EngineContext.Defaults.DefaultFont` and the built-in JetBrains Mono variable face (default instance
  Regular) as
  `EngineContext.Defaults.DefaultMonoFont`; the engine owns both and the game must not dispose them.
  Disposing a font while a frame references it is safe; using the public object after `Dispose` is
  not.

Limitations:

- There is no complex shaping, ligature substitution, bidi ordering, font fallback, color font, or
  emoji support. A cmap-mapped rune maps directly to one glyph; an unmapped rune is `.notdef`.
- Coverage is per face. The built-in Fira Sans Regular and JetBrains Mono (default instance Regular)
  faces cover Latin,
  Cyrillic, and Greek; a different face covers whatever its cmap maps.
- A variable font loads and renders its default instance; named variation axes are not exposed.
- CFF2 outlines are rejected: only TrueType `glyf` and CFF 1.0 outlines are supported.
- There is no preload and no synchronous glyph generation. The first draw of a glyph may skip it for
  that call while the `RayBork.Font` thread generates its texture.
- Text is world-space geometry. Screen-space text uses `Camera.ScreenToWorldTransform` and the
  `Transform` `DrawText` overload with a pixel-sized `size`; there is no `DrawText(Vector2)` overload
  and no 2D text draw. The HUD is same-pass, screen-aligned, alpha/no-depth-write text, so geometry
  closer than the overlay plane can occlude it, and a separate overlay pass is M8.
- One texture per glyph means a font's glyph cache consumes bindless slots: 512 cached per font, and
  the fixed bindless texture table is table-wide 2048 slots.
- MSDF sharp corners are not extended, so a small artifact can appear at very large sizes.
- A world-space text draw can be occluded by geometry closer to the camera, including a status
  overlay placed one unit in front of the camera.

`Camera` also exposes:

```csharp
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
```

**Screen-space text.** There is no `DrawText(Vector2)` screen overload; the `Transform` overload is
world-space, and a HUD passes the transform from `Camera.ScreenToWorldTransform`. The screen
transform's rotation keeps the block screen-aligned, and its uniform scale is world units per render
pixel, so a pixel-sized `size` (for example `18f`) is 18 render pixels at the chosen plane:

```csharp
Transform hud = camera.ScreenToWorldTransform(new Vector2(margin, margin), 1f);
camera.DrawText(font, "HUD", hud, 18f, Color.White);
```

This is same-pass geometry, not a separate overlay pass. The text render state is alpha blending,
no culling, `greater_equal`, and no depth write, so the glyphs do not disturb 3D depth, but geometry
closer than the overlay plane can still occlude them. Castle and PrimitiveGallery use this pattern
for their status lines, and Pong uses it for its scores, status, and overlays. A dedicated
multi-camera overlay pass with per-pass frame data is M8.
