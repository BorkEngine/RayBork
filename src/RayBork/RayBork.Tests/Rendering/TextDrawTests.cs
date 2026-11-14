using System.Numerics;
using System.Text;
using RayBork.Internal;
using RayBork.Tests.Fonts;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the camera text draws: the layout anchor, asynchronous glyph skipping, per-instance glyph
/// textures, validation, batching, and the billboard centering and alignment.
/// </summary>
/// <remarks>
/// Every test records through a real <see cref="CameraService"/> frame and inspects the sealed packet.
/// The fonts are real <see cref="Font"/> instances over synthetic faces, so the layout metrics and the
/// glyph quads are deterministic without a GPU or a font file.
/// </remarks>
public sealed class TextDrawTests {
    private const float TOLERANCE = 1e-5f;

    private static readonly Color _color = new(0.25f, 0.5f, 0.75f, 0.5f);

    private static readonly RenderState _textRenderState = new() {
        Blend = BlendMode.Alpha,
        Cull = CullMode.None,
        DepthTest = DepthTest.GreaterOrEqual,
        DepthWrite = false,
    };

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task EmptyTextRecordsNothing() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, ReadOnlySpan<char>.Empty, Transform.Identity, 1f, _color);
            camera.DrawTextBillboard(font, ReadOnlySpan<char>.Empty, Vector3.Zero, 1f, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
        Assert.Equal(0, harness.Factory.CreateCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TextWithOnlyPendingGlyphsRecordsNothing() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "AB", new Transform(new Vector3(0f, 0f, -5f)), 1f, _color);
        }

        harness.Host.EndFrame();

        // Both glyphs are still being generated, so the call skips both instances and records nothing;
        // the frame must not retain the mesh, the program, or a texture.
        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task APendingGlyphIsSkippedAndTheRestOfTheLineKeepsItsLayoutPositions() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        Transform transform = new(new Vector3(0f, 0f, -5f));
        await harness.SettleGlyphsAsync(font, "A");
        Assert.False(font.TryGetGlyph(new Rune('B'), out _));

        // "BA" with B pending: A is still placed after B's advance, so the pending glyph's advance is
        // kept and only its own quad is skipped.
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "BA", transform, 1f, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(1, record.InstanceCount);
        float advanceB = font.Measure("B", 1f).X;
        AssertMatrixNear(
            ExpectGlyphQuad(ExpectPosition(font, advanceB, GlyphOf(font, 'A')), GlyphOf(font, 'A').Size)
                * transform.ToMatrix(),
            record.GetWorldMatrices()[0]);
        Assert.Same(GetGlyphTexture(font, 'A'), record.GetInstanceTextureBindings()[0]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AReadyGlyphAfterThePendingOneRecordsBothInLayoutOrder() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        Transform transform = new(new Vector3(0f, 0f, -5f));
        await harness.SettleGlyphsAsync(font, "AB");
        float advanceB = font.Measure("B", 1f).X;

        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "BA", transform, 1f, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(2, record.InstanceCount);
        AssertMatrixNear(
            ExpectGlyphQuad(ExpectPosition(font, 0f, GlyphOf(font, 'B')), GlyphOf(font, 'B').Size)
                * transform.ToMatrix(),
            record.GetWorldMatrices()[0]);
        AssertMatrixNear(
            ExpectGlyphQuad(
                ExpectPosition(font, advanceB, GlyphOf(font, 'A')),
                GlyphOf(font, 'A').Size) * transform.ToMatrix(),
            record.GetWorldMatrices()[1]);
        Assert.Same(GetGlyphTexture(font, 'B'), record.GetInstanceTextureBindings()[0]);
        Assert.Same(GetGlyphTexture(font, 'A'), record.GetInstanceTextureBindings()[1]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AZeroSizeSpaceRecordsNothingAndStillAdvancesThePen() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateSpaceFace());
        await harness.SettleGlyphsAsync(font, "A");

        RenderFramePacket spacePacket = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, " ", new Transform(new Vector3(0f, 0f, -5f)), 1f, _color);
        }

        harness.Host.EndFrame();

        // A mapped glyph without an outline has a zero-size quad: it records no instance and acquires
        // no texture.
        Assert.Equal(0, spacePacket.Passes[0].DrawCount);
        Assert.Equal(0, spacePacket.Retention.Count);
        Assert.Equal(1, harness.Factory.CreateCount);

        RenderFramePacket linePacket = harness.Host.BeginFrame(frameIndex: 2);
        Transform transform = new(new Vector3(0f, 0f, -5f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "A A", transform, 1f, _color);
        }

        harness.Host.EndFrame();

        Matrix4x4 transformMatrix = transform.ToMatrix();
        RenderDrawRecord record = linePacket.Passes[0].Draws[0];
        Assert.Equal(2, record.InstanceCount);
        float advanceA = font.Measure("A", 1f).X;
        float advanceSpace = font.Measure(" ", 1f).X;
        AssertMatrixNear(
            ExpectGlyphQuad(ExpectPosition(font, 0f, GlyphOf(font, 'A')), GlyphOf(font, 'A').Size)
                * transformMatrix,
            record.GetWorldMatrices()[0]);
        AssertMatrixNear(
            ExpectGlyphQuad(
                ExpectPosition(font, advanceA + advanceSpace, GlyphOf(font, 'A')),
                GlyphOf(font, 'A').Size) * transformMatrix,
            record.GetWorldMatrices()[1]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AZeroTransformScaleComponentRecordsNothing() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        await harness.SettleGlyphsAsync(font, "A");
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Transform flattened = new(
                new Vector3(0f, 0f, -5f),
                Quaternion.Identity,
                new Vector3(0f, 1f, 1f));
            camera.DrawText(font, "A", flattened, 1f, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
        Assert.Equal(0, harness.Host.Service.TextGlyphTextures.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ThePerCallGlyphScratchDropsItsTextureReferences() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        await harness.SettleGlyphsAsync(font, "AB");
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "AB", new Transform(new Vector3(0f, 0f, -5f)), 1f, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(2, packet.Passes[0].Draws[0].InstanceCount);
        // The scratch must not keep the recorded glyph textures reachable after the call.
        Assert.Equal(0, harness.Host.Service.TextGlyphTextures.Length);
        Assert.Empty(harness.Host.Service.TextGlyphTextures.ToArray());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ReadyGlyphsRecordOneDrawWithPerInstanceTexturesTintAndMatrices() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        await harness.SettleGlyphsAsync(font, "AB");
        Transform transform = new(
            new Vector3(1f, 2f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f),
            new Vector3(2f, 2f, 2f));
        Vector2 glyphASize = GlyphOf(font, 'A').Size * 0.5f;
        Vector2 glyphBSize = GlyphOf(font, 'B').Size * 0.5f;
        float advanceA = font.Measure("A", 0.5f).X;
        Vector2 glyphAPosition = ExpectPosition(font, 0f, GlyphOf(font, 'A'), size: 0.5f);
        Vector2 glyphBPosition = ExpectPosition(font, advanceA, GlyphOf(font, 'B'), size: 0.5f);
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "AB", transform, 0.5f, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(1, pass.DrawCount);
        RenderDrawRecord record = pass.Draws[0];
        Assert.Same(harness.Primitives.Quad.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.MsdfMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, record.Topology);
        Assert.Equal(6, record.IndexCount);
        Assert.Equal(_textRenderState, record.RenderState);
        // The recorded state must stay the MSDF program's declared state; a shader-state change would
        // otherwise silently change the text batch key and depth/blend behavior.
        Assert.Equal(harness.Primitives.MsdfMaterial.DefaultRenderState, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(2, record.InstanceCount);
        Assert.True(record.HasPerInstanceTextureBindings);
        Assert.Equal(1, record.TextureBindingCount);
        Assert.Empty(record.GetTextureBindings().ToArray());
        Assert.Equal(new Vector4(0f, 0f, 1f, 1f), record.UvRect);

        Matrix4x4 transformMatrix = transform.ToMatrix();
        Matrix4x4 glyphAMatrix = record.GetWorldMatrices()[0];
        Matrix4x4 glyphBMatrix = record.GetWorldMatrices()[1];
        AssertMatrixNear(ExpectGlyphQuad(glyphAPosition, glyphASize) * transformMatrix, glyphAMatrix);
        AssertMatrixNear(ExpectGlyphQuad(glyphBPosition, glyphBSize) * transformMatrix, glyphBMatrix);

        // The engine quad's lower-left and upper-right corners map to the layout rect's corners under the
        // transform, which is the documented glyph-quad mapping.
        AssertVector(
            Vector3.Transform(new Vector3(glyphAPosition, 0f), transformMatrix),
            Vector3.Transform(new Vector3(-0.5f, -0.5f, 0f), glyphAMatrix));
        AssertVector(
            Vector3.Transform(new Vector3(glyphAPosition + glyphASize, 0f), transformMatrix),
            Vector3.Transform(new Vector3(0.5f, 0.5f, 0f), glyphAMatrix));

        ReadOnlySpan<Texture?> textures = record.GetInstanceTextureBindings();
        Assert.Same(GetGlyphTexture(font, 'A'), textures[0]);
        Assert.Same(GetGlyphTexture(font, 'B'), textures[1]);

        // The frame holds the quad, the MSDF program, and each distinct glyph texture.
        Assert.Equal(4, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NullFontThrowsArgumentNullException() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Assert.Equal(
                "font",
                Assert.Throws<ArgumentNullException>(
                    () => camera.DrawText(null!, "A", Transform.Identity, 1f, _color)).ParamName);
            Assert.Equal(
                "font",
                Assert.Throws<ArgumentNullException>(
                    () => camera.DrawTextBillboard(null!, "A", Vector3.Zero, 1f, _color)).ParamName);
        }

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposedFontThrowsObjectDisposedException() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        font.Dispose();
        harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Assert.Throws<ObjectDisposedException>(
                () => camera.DrawText(font, "A", Transform.Identity, 1f, _color));
            Assert.Throws<ObjectDisposedException>(
                () => camera.DrawTextBillboard(font, "A", Vector3.Zero, 1f, _color));
        }

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposedCameraThrowsObjectDisposedException() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        harness.Host.BeginFrame();
        Camera camera = harness.Host.CreatePerspectiveCamera();
        camera.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawText(font, "A", Transform.Identity, 1f, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawTextBillboard(font, "A", Vector3.Zero, 1f, _color));

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NonFiniteTransformColorOrPositionThrowsArgumentException() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Transform badPosition = new(new Vector3(float.NaN, 0f, 0f));
            Assert.Equal(
                "transform",
                Assert.Throws<ArgumentException>(
                    () => camera.DrawText(font, "A", badPosition, 1f, _color)).ParamName);
            Transform badRotation = new(
                Vector3.Zero,
                new Quaternion(float.NaN, 0f, 0f, 1f),
                Vector3.One);
            Assert.Equal(
                "transform",
                Assert.Throws<ArgumentException>(
                    () => camera.DrawText(font, "A", badRotation, 1f, _color)).ParamName);
            Transform badScale = new(Vector3.Zero, Quaternion.Identity, new Vector3(1f, float.NaN, 1f));
            Assert.Equal(
                "transform",
                Assert.Throws<ArgumentException>(
                    () => camera.DrawText(font, "A", badScale, 1f, _color)).ParamName);
            Assert.Equal(
                "position",
                Assert.Throws<ArgumentException>(
                    () => camera.DrawTextBillboard(
                        font,
                        "A",
                        new Vector3(0f, float.PositiveInfinity, 0f),
                        1f,
                        _color)).ParamName);
            Assert.Equal(
                "color",
                Assert.Throws<ArgumentException>(
                    () => camera.DrawText(font, "A", Transform.Identity, 1f, new Color(float.NaN, 0f, 0f, 1f))).ParamName);
        }

        harness.Host.EndFrame();
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public async Task NonFiniteOrNonPositiveSizeThrowsArgumentOutOfRangeException(float size) {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Assert.Equal(
                "size",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => camera.DrawText(font, "A", Transform.Identity, size, _color)).ParamName);
            Assert.Equal(
                "size",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => camera.DrawTextBillboard(font, "A", Vector3.Zero, size, _color)).ParamName);
        }

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NegativeTransformScaleThrowsArgumentOutOfRangeException() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Transform mirrored = new(Vector3.Zero, Quaternion.Identity, new Vector3(-1f, 1f, 1f));
            Assert.Equal(
                "transform",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => camera.DrawText(font, "A", mirrored, 1f, _color)).ParamName);
        }

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task UndefinedAlignmentOrWrappingThrowsArgumentOutOfRangeException() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Assert.Equal(
                "alignment",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => camera.DrawText(
                        font,
                        "A",
                        Transform.Identity,
                        1f,
                        _color,
                        alignment: (TextAlignment)99)).ParamName);
            Assert.Equal(
                "wrapping",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => camera.DrawText(
                        font,
                        "A",
                        Transform.Identity,
                        1f,
                        _color,
                        wrapping: (TextWrapping)99)).ParamName);
            Assert.Equal(
                "alignment",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => camera.DrawTextBillboard(
                        font,
                        "A",
                        Vector3.Zero,
                        1f,
                        _color,
                        alignment: (TextAlignment)99)).ParamName);
        }

        harness.Host.EndFrame();
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(-1f, TextWrapping.None)]
    [InlineData(float.NaN, TextWrapping.None)]
    [InlineData(float.PositiveInfinity, TextWrapping.None)]
    [InlineData(0f, TextWrapping.Word)]
    [InlineData(0f, TextWrapping.Character)]
    public async Task InvalidMaxWidthThrowsArgumentOutOfRangeException(float maxWidth, TextWrapping wrapping) {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Assert.Equal(
                "maxWidth",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => camera.DrawText(
                        font,
                        "A B",
                        Transform.Identity,
                        1f,
                        _color,
                        wrapping: wrapping,
                        maxWidth: maxWidth)).ParamName);
        }

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task RejectedCallsLeaveThePacketAndRetentionUntouched() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => camera.DrawText(font, "A", Transform.Identity, 0f, _color));
            Assert.Throws<ArgumentException>(
                () => camera.DrawText(font, "A", Transform.Identity, 1f, new Color(0f, 0f, 0f, float.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => camera.DrawText(
                    font,
                    "AB",
                    Transform.Identity,
                    1f,
                    _color,
                    alignment: (TextAlignment)99));
            Assert.Throws<ArgumentException>(
                () => camera.DrawTextBillboard(
                    font,
                    "A",
                    new Vector3(float.NaN, 0f, 0f),
                    1f,
                    _color));
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
        Assert.Equal(0, harness.Factory.CreateCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ADrawFromAnotherThreadThrowsInvalidOperationException() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        harness.Host.BeginFrame();
        Camera camera = harness.Host.CreatePerspectiveCamera();
        Exception? thrown = null;
        Thread worker = new(() => {
            try {
                camera.DrawText(font, "A", Transform.Identity, 1f, _color);
            } catch (Exception exception) {
                thrown = exception;
            }
        });
        worker.Start();
        try {
            // Join synchronously so the test thread keeps the tick-thread attachment; an await here
            // could resume the test on another thread and leave the attached thread poisoned.
            Assert.True(worker.Join(TimeSpan.FromSeconds(30)), "The worker thread did not finish.");
            Assert.IsType<InvalidOperationException>(thrown);
        } finally {
            camera.Dispose();
        }

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TwoDrawTextCallsOverOneFontProduceOneBatch() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        await harness.SettleGlyphsAsync(font, "AB");
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "AB", new Transform(new Vector3(0f, 0f, -5f)), 1f, _color);
            camera.DrawText(font, "AB", new Transform(new Vector3(1f, 0f, -5f)), 1f, _color);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(4, plan.Instances);
        Assert.Equal(8, plan.Triangles);
        Assert.Equal(0, plan.CulledInstances);
        Assert.Equal(2, packet.Passes[0].DrawCount);
        Assert.Single(plan.Passes[0].Batches);
        Assert.Equal(4, plan.Passes[0].Batches[0].InstanceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DifferentSizesAndDifferentFontsStillBatchTogether() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font firstFont = harness.CreateFont(CreateTwoGlyphFace());
        Font secondFont = harness.CreateFont(CreateAlternateFace());
        await harness.SettleGlyphsAsync(firstFont, "AB");
        await harness.SettleGlyphsAsync(secondFont, "AB");
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(firstFont, "AB", new Transform(new Vector3(0f, 1.5f, -5f)), 1f, _color);
            camera.DrawText(firstFont, "AB", new Transform(new Vector3(0f, 0f, -5f)), 0.5f, _color);
            camera.DrawText(secondFont, "AB", new Transform(new Vector3(0f, -1.5f, -5f)), 1f, _color);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(6, plan.Instances);
        Assert.Equal(12, plan.Triangles);
        Assert.Single(plan.Passes[0].Batches);
        Assert.Equal(6, plan.Passes[0].Batches[0].InstanceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TextAndBillboardDoNotMerge() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        await harness.SettleGlyphsAsync(font, "A");
        using Texture texture = RenderingTestAssets.CreateTexture();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawText(font, "A", new Transform(new Vector3(0f, 0f, -5f)), 1f, _color);
            camera.DrawBillboard(texture, new Vector3(1f, 0f, -5f), Vector2.One, _color);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(2, plan.Instances);
        Assert.Equal(4, plan.Triangles);
        IReadOnlyList<PlannedBatch> batches = plan.Passes[0].Batches;
        Assert.Equal(2, batches.Count);
        Assert.NotSame(batches[0].Program, batches[1].Program);
        Assert.Equal(_textRenderState, batches[0].RenderState);
        Assert.Equal(RenderState.AlphaBlend, batches[1].RenderState);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTextBillboardCentersTheBlockOnThePositionAndFacesTheCamera() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        await harness.SettleGlyphsAsync(font, "AB");
        Glyph glyphA = GlyphOf(font, 'A');
        Glyph glyphB = GlyphOf(font, 'B');
        ExpectedLayout layout = ExpectLayout(font, [[glyphA, glyphB]], 0.5f, TextAlignment.Left);
        Vector2 blockCenter = (layout.BoundsMin + layout.BoundsMax) * 0.5f;
        Vector3 position = new(1.5f, 2f, -6f);
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTextBillboard(font, "AB", position, 0.5f, _color);
        }

        // A yawed camera at the world origin: the camera-facing plane uses its right/up basis.
        Transform yawed = new(
            new Vector3(-6f, 0f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f),
            Vector3.One);
        Vector3 yawedPosition = new(-3f, 0f, 0f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera(transform: yawed)) {
            camera.DrawTextBillboard(font, "AB", yawedPosition, 0.5f, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(2, packet.Passes.Length);
        AssertBillboardRecord(
            packet.Passes[0].Draws[0],
            layout,
            blockCenter,
            position,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f));
        AssertBillboardRecord(
            packet.Passes[1].Draws[0],
            layout,
            blockCenter,
            yawedPosition,
            yawed.Right,
            yawed.Up,
            -yawed.Forward);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTextBillboardAlignmentPositionsLinesInsideTheBlockWidth() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateTwoGlyphFace());
        await harness.SettleGlyphsAsync(font, "AB");
        Glyph glyphA = GlyphOf(font, 'A');
        Glyph glyphB = GlyphOf(font, 'B');
        ExpectedLayout leftLayout = ExpectLayout(font, [[glyphA], [glyphB]], 1f, TextAlignment.Left);
        ExpectedLayout centerLayout = ExpectLayout(font, [[glyphA], [glyphB]], 1f, TextAlignment.Center);
        ExpectedLayout rightLayout = ExpectLayout(font, [[glyphA], [glyphB]], 1f, TextAlignment.Right);
        Vector3 leftPosition = new(-4f, 0f, -10f);
        Vector3 centerPosition = new(0f, 0f, -10f);
        Vector3 rightPosition = new(4f, 0f, -10f);
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTextBillboard(font, "A\nB", leftPosition, 1f, _color, TextAlignment.Left);
            camera.DrawTextBillboard(font, "A\nB", centerPosition, 1f, _color, TextAlignment.Center);
            camera.DrawTextBillboard(font, "A\nB", rightPosition, 1f, _color, TextAlignment.Right);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(3, pass.DrawCount);
        AssertBillboardRecord(
            pass.Draws[0],
            leftLayout,
            (leftLayout.BoundsMin + leftLayout.BoundsMax) * 0.5f,
            leftPosition,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f));
        AssertBillboardRecord(
            pass.Draws[1],
            centerLayout,
            (centerLayout.BoundsMin + centerLayout.BoundsMax) * 0.5f,
            centerPosition,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f));
        AssertBillboardRecord(
            pass.Draws[2],
            rightLayout,
            (rightLayout.BoundsMin + rightLayout.BoundsMax) * 0.5f,
            rightPosition,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f));

        // The second line moves right by half the leftover width for Center and by the whole leftover
        // width for Right; Left leaves both lines at the block's left edge.
        float leftover = font.Measure("A", 1f).X - font.Measure("B", 1f).X;
        Assert.Equal(0f, XOffset(centerLayout, 0, leftLayout), TOLERANCE);
        Assert.Equal(leftover * 0.5f, XOffset(centerLayout, 1, leftLayout), TOLERANCE);
        Assert.Equal(0f, XOffset(rightLayout, 0, leftLayout), TOLERANCE);
        Assert.Equal(leftover, XOffset(rightLayout, 1, leftLayout), TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTextBillboardCentersTheGlyphOverhangOutsideTheLayoutBox() {
        using TextDrawTestHarness harness = await TextDrawTestHarness.CreateAsync();
        Font font = harness.CreateFont(CreateOverhangFace());
        await harness.SettleGlyphsAsync(font, "AC");
        Glyph glyphA = GlyphOf(font, 'A');
        Glyph glyphC = GlyphOf(font, 'C');
        ExpectedLayout layout = ExpectLayout(font, [[glyphA, glyphC]], 1f, TextAlignment.Left);
        Vector2 unionCenter = (layout.BoundsMin + layout.BoundsMax) * 0.5f;
        // C's outline rises above the ascent line, so the glyph-quad union is taller than the layout
        // box; ignoring the overhang would center the block on the box instead.
        Vector2 boxCenter = new(font.Measure("AC", 1f).X * 0.5f, -font.LineHeight * 0.5f);
        Assert.True(
            Vector2.Distance(unionCenter, boxCenter) > 0.03f,
            $"The overhang must move the bounds center clear of the box center; got {unionCenter} and {boxCenter}.");
        Vector3 position = new(0.5f, 0.25f, -8f);
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTextBillboard(font, "AC", position, 1f, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        AssertBillboardRecord(
            record,
            layout,
            unionCenter,
            position,
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f));
        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        ExpectedGlyph first = layout.Glyphs[0];
        AssertVector(position, MapLayoutPoint(matrix, first, unionCenter), 1e-4f);
        Assert.True(
            Vector3.Distance(position, MapLayoutPoint(matrix, first, boxCenter)) > 0.03f,
            "The recorded block must not center on the layout box alone.");
    }

    /// <summary>
    /// Asserts one billboard record's matrices: each glyph quad maps through the centered block offset
    /// and the camera-facing basis, the block center maps to <paramref name="position"/>, and the quad
    /// normal faces the camera.
    /// </summary>
    private static void AssertBillboardRecord(
        RenderDrawRecord record,
        ExpectedLayout layout,
        Vector2 blockCenter,
        Vector3 position,
        Vector3 right,
        Vector3 up,
        Vector3 front) {
        Matrix4x4 block = Matrix4x4.CreateTranslation(-blockCenter.X, -blockCenter.Y, 0f)
            * new Matrix4x4(
                right.X,
                right.Y,
                right.Z,
                0f,
                up.X,
                up.Y,
                up.Z,
                0f,
                front.X,
                front.Y,
                front.Z,
                0f,
                position.X,
                position.Y,
                position.Z,
                1f);
        Assert.Equal(layout.Glyphs.Length, record.InstanceCount);
        for (int index = 0; index < layout.Glyphs.Length; index++) {
            ExpectedGlyph glyph = layout.Glyphs[index];
            Matrix4x4 matrix = record.GetWorldMatrices()[index];
            AssertMatrixNear(ExpectGlyphQuad(glyph.Position, glyph.Size) * block, matrix);

            // The layout bounds center is the block origin, so the recorded glyph matrix maps it to the
            // billboard position.
            AssertVector(position, MapLayoutPoint(matrix, glyph, blockCenter), 1e-4f);

            // The quad's local +Z is the camera-facing front.
            Vector3 normal = Vector3.Normalize(
                Vector3.Cross(Axis(matrix, 0), Axis(matrix, 1)));
            AssertVector(front, normal);
        }
    }

    /// <summary>
    /// Maps one point of the local layout plane through a recorded glyph matrix.
    /// </summary>
    private static Vector3 MapLayoutPoint(Matrix4x4 matrix, ExpectedGlyph glyph, Vector2 layoutPoint) {
        Vector2 unit = new(
            ((layoutPoint.X - glyph.Position.X) / glyph.Size.X) - 0.5f,
            ((layoutPoint.Y - glyph.Position.Y) / glyph.Size.Y) - 0.5f);
        return Vector3.Transform(new Vector3(unit, 0f), matrix);
    }

    private static float XOffset(ExpectedLayout layout, int glyphIndex, ExpectedLayout baseline) =>
        layout.Glyphs[glyphIndex].Position.X - baseline.Glyphs[glyphIndex].Position.X;

    private static Vector2 ExpectPosition(Font font, float pen, Glyph glyph, float size = 1f) =>
        new(
            pen + (glyph.Bearing.X * size),
            (-font.Ascent * size) + (glyph.Bearing.Y * size));

    private static Matrix4x4 ExpectGlyphQuad(Vector2 position, Vector2 size) =>
        Matrix4x4.CreateScale(size.X, size.Y, 1f)
        * Matrix4x4.CreateTranslation(
            position.X + (size.X * 0.5f),
            position.Y + (size.Y * 0.5f),
            0f);

    /// <summary>
    /// Rebuilds the layout of a small block from the font's public metrics: advances, baselines,
    /// alignment offsets, and the layout box union with the glyph quads.
    /// </summary>
    private static ExpectedLayout ExpectLayout(
        Font font,
        IReadOnlyList<Glyph[]> lines,
        float size,
        TextAlignment alignment) {
        int lineCount = lines.Count;
        float boxHeight = lineCount * font.LineHeight * size;
        float[] advances = new float[lineCount];
        for (int line = 0; line < lineCount; line++) {
            float advance = 0f;
            foreach (Glyph glyph in lines[line]) {
                advance += glyph.Advance.X * size;
            }

            advances[line] = advance;
        }

        float boxWidth = advances.Max();
        Vector2 min = new(0f, -boxHeight);
        Vector2 max = new(boxWidth, 0f);
        List<ExpectedGlyph> glyphs = [];
        for (int line = 0; line < lineCount; line++) {
            float offset = alignment switch {
                TextAlignment.Center => (boxWidth - advances[line]) * 0.5f,
                TextAlignment.Right => boxWidth - advances[line],
                _ => 0f,
            };
            float baseline = -(font.Ascent + (line * font.LineHeight)) * size;
            float pen = offset;
            foreach (Glyph glyph in lines[line]) {
                Vector2 position = new(
                    pen + (glyph.Bearing.X * size),
                    baseline + (glyph.Bearing.Y * size));
                Vector2 glyphSize = glyph.Size * size;
                glyphs.Add(new ExpectedGlyph(position, glyphSize));
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position + glyphSize);
                pen += glyph.Advance.X * size;
            }
        }

        return new ExpectedLayout(min, max, [.. glyphs]);
    }

    private static Glyph GlyphOf(Font font, char value) => TextDrawTestHarness.GetGlyph(font, value);

    private static Texture? GetGlyphTexture(Font font, char value) => TextDrawTestHarness.GetGlyphTexture(font, value);

    private static Vector3 Axis(Matrix4x4 matrix, int index) => index switch {
        0 => new Vector3(matrix.M11, matrix.M12, matrix.M13),
        1 => new Vector3(matrix.M21, matrix.M22, matrix.M23),
        _ => new Vector3(matrix.M31, matrix.M32, matrix.M33),
    };

    private static void AssertMatrixNear(
        Matrix4x4 expected,
        Matrix4x4 actual,
        float tolerance = TOLERANCE) {
        Assert.Equal(expected.M11, actual.M11, tolerance);
        Assert.Equal(expected.M12, actual.M12, tolerance);
        Assert.Equal(expected.M13, actual.M13, tolerance);
        Assert.Equal(expected.M14, actual.M14, tolerance);
        Assert.Equal(expected.M21, actual.M21, tolerance);
        Assert.Equal(expected.M22, actual.M22, tolerance);
        Assert.Equal(expected.M23, actual.M23, tolerance);
        Assert.Equal(expected.M24, actual.M24, tolerance);
        Assert.Equal(expected.M31, actual.M31, tolerance);
        Assert.Equal(expected.M32, actual.M32, tolerance);
        Assert.Equal(expected.M33, actual.M33, tolerance);
        Assert.Equal(expected.M34, actual.M34, tolerance);
        Assert.Equal(expected.M41, actual.M41, tolerance);
        Assert.Equal(expected.M42, actual.M42, tolerance);
        Assert.Equal(expected.M43, actual.M43, tolerance);
        Assert.Equal(expected.M44, actual.M44, tolerance);
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = TOLERANCE) {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }

    private static SfntFace CreateTwoGlyphFace() {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        int glyphA = builder.AddSimple(600, 0, Square(500));
        int glyphB = builder.AddSimple(400, 0, Square(300));
        return SfntFace.Parse(builder.WithCmap(4, (0x41, glyphA), (0x42, glyphB)).Build());
    }

    private static SfntFace CreateAlternateFace() {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        int glyphA = builder.AddSimple(500, 0, Square(450));
        int glyphB = builder.AddSimple(300, 0, Square(250));
        return SfntFace.Parse(builder.WithCmap(4, (0x41, glyphA), (0x42, glyphB)).Build());
    }

    private static SfntFace CreateSpaceFace() {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        int glyphA = builder.AddSimple(600, 0, Square(500));
        int space = builder.AddEmpty(250);
        return SfntFace.Parse(builder.WithCmap(4, (0x41, glyphA), (0x20, space)).Build());
    }

    /// <summary>
    /// Builds a face whose second glyph rises above the ascent line, so its padded quad extends outside
    /// the layout box and contributes a real overhang to the block bounds.
    /// </summary>
    private static SfntFace CreateOverhangFace() {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        int glyphA = builder.AddSimple(600, 0, Square(500));
        int glyphC = builder.AddSimple(
            300,
            0,
            new SyntheticPoint(0, 200),
            new SyntheticPoint(300, 200),
            new SyntheticPoint(300, 900),
            new SyntheticPoint(0, 900));
        return SfntFace.Parse(builder.WithCmap(4, (0x41, glyphA), (0x43, glyphC)).Build());
    }

    private static SyntheticPoint[] Square(int extent) => [
        new(0, 0),
        new(extent, 0),
        new(extent, extent),
        new(0, extent),
    ];

    private sealed record ExpectedGlyph(Vector2 Position, Vector2 Size);

    private sealed record ExpectedLayout(Vector2 BoundsMin, Vector2 BoundsMax, ExpectedGlyph[] Glyphs);
}
