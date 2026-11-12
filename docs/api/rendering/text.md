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
    public void Preload(ReadOnlySpan<char> characters);
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

Fonts are TTF/OTF assets. The bake copies the font file and its face metadata; the runtime parses
the face and generates MSDF glyph textures on demand.

- One glyph is one small texture, 32 px on its longest side, with a baked distance range and
  padding. There is no atlas. Glyph textures are bindless and are referenced per text instance.
- `Load<Font>` parses the face and preloads ASCII (`U+0020..U+007E`) plus the replacement glyph.
  `Preload` generates any additional characters. `DrawText` generates missing glyphs on the tick
  thread before recording; the first draw of a new script may hitch. Preload known text in
  `Setup`.
- `GlyphCount` is the number of mapped glyphs in the face. `CachedGlyphCount` is the number with a
  generated MSDF texture.
- `TryGetGlyph` returns only cached glyphs. `Measure` works from face metrics and kerning without
  generating textures.
- Metrics (`LineHeight`, `Ascent`, `Descent`) are in em units; `Measure` and `DrawText` multiply by
  the requested `size`.
- Kerning is applied. Ligatures, contextual shaping (Arabic, Indic), bidi, font fallback, color
  fonts, and emoji are post-1.0. Latin/Cyrillic/Greek text maps codepoint-to-glyph directly.
- There is no hinting. MSDF antialiasing uses screen-space derivatives.
- `size` is the em height in world units. A `size` of `1` means one world unit per em.
- Text is world space. `DrawText` lays the text in the transform's local XY plane, with +X to the
  right and +Y up; the readable face points toward local +Z.
- Alignment and wrapping are applied within `maxWidth` when `wrapping` is not `None`. `maxWidth`
  is in world units.
- `DrawTextBillboard` lays out the text in a camera-facing plane. It is the same layout and
  batching path with a billboard transform.
- Text batching uses the built-in MSDF shader. All text with the same font and render state can be
  one instanced draw; each glyph instance carries its bindless texture index and UV rectangle.
- Fonts are shared assets. Disposing a font while a frame references it is safe; using the public
  object after `Dispose` is not.

`Camera` also exposes:

```csharp
public void DrawTextBillboard(
    Font font,
    ReadOnlySpan<char> text,
    Vector3 position,
    float size,
    Color color,
    TextAlignment alignment = TextAlignment.Left);
```
