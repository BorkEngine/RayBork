using RayBork.Bake;
using RayBork.Internal;

namespace RayBork.Tests.Bake.TestInfrastructure;

/// <summary>A processor that emits a shaped asset, a raw payload, and counts invocations.</summary>
internal sealed class CountingProcessor : IAssetProcessor {
    internal CountingProcessor(string name = "counter", string extension = "count") {
        Name = name;
        Extensions = [extension];
    }

    public string Name { get; }

    public IReadOnlyList<string> Extensions { get; }

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    internal int ProcessCount { get; private set; }

    public void Process(AssetProcessorContext context) {
        ProcessCount++;
        context.Outputs.AddAsset(
            context.BaseAssetPath,
            new TestPayload { Name = context.SourceFileName, Value = ProcessCount });
        context.Outputs.AddData(
            context.BaseAssetPath + "-raw",
            [1, 2, 3, (byte)ProcessCount],
            alignment: 64);
    }
}

/// <summary>A processor with an optional sidecar.</summary>
internal sealed class OptionalSidecarProcessor : AssetProcessor<TestSidecar> {
    public override string Name => "optional";

    public override IReadOnlyList<string> Extensions => ["opt"];

    public override SidecarPolicy Policy => SidecarPolicy.Optional;

    internal int ProcessCount { get; private set; }

    protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new TestSidecar { Value = -1, Label = "default" };

    protected override void Process(AssetProcessorContext context, TestSidecar sidecar) {
        ProcessCount++;
        context.Outputs.AddAsset(
            context.BaseAssetPath,
            new TestPayload { Name = sidecar.Label, Value = sidecar.Value });
    }
}

/// <summary>A processor with an optional sidecar and a compound extension.</summary>
internal sealed class CompoundOptionalProcessor : AssetProcessor<TestSidecar> {
    public override string Name => "compound-optional";

    public override IReadOnlyList<string> Extensions => ["bundle.opt"];

    public override SidecarPolicy Policy => SidecarPolicy.Optional;

    protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new TestSidecar { Value = -1, Label = "default" };

    protected override void Process(AssetProcessorContext context, TestSidecar sidecar)
        => context.Outputs.AddAsset(
            context.BaseAssetPath,
            new TestPayload { Name = sidecar.Label, Value = sidecar.Value });
}

/// <summary>A processor with a required sidecar.</summary>
internal sealed class RequiredSidecarProcessor : AssetProcessor<TestSidecar> {
    public override string Name => "required";

    public override IReadOnlyList<string> Extensions => ["req"];

    protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new();

    protected override void Process(AssetProcessorContext context, TestSidecar sidecar)
        => context.Outputs.AddAsset(
            context.BaseAssetPath,
            new TestPayload { Name = sidecar.Label, Value = sidecar.Value });
}

/// <summary>A processor that can be selected by a table-named TOML source.</summary>
internal sealed class TableSourceProcessor : AssetProcessor<TestSidecar> {
    public override string Name => "shader";

    public override IReadOnlyList<string> Extensions => ["glsl"];

    protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new();

    protected override void Process(AssetProcessorContext context, TestSidecar sidecar)
        => context.Outputs.AddAsset(
            context.BaseAssetPath,
            new TestPayload { Name = sidecar.Label, Value = sidecar.Value });
}

/// <summary>A processor that reports an error after emitting outputs.</summary>
internal sealed class ErrorProcessor : IAssetProcessor {
    public string Name => "error";

    public IReadOnlyList<string> Extensions => ["err"];

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context) {
        context.Outputs.AddAsset(context.BaseAssetPath, new TestPayload { Name = "discarded" });
        context.Diagnostics.Error("RB4006", "deliberate source error");
    }
}

/// <summary>A processor that throws.</summary>
internal sealed class ThrowProcessor : IAssetProcessor {
    public string Name => "throw";

    public IReadOnlyList<string> Extensions => ["boom"];

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context)
        => throw new InvalidOperationException("deliberate processor failure");
}

/// <summary>A table-named processor that registers an additional tracked input.</summary>
internal sealed class InputProcessor : AssetProcessor<TestSidecar> {
    public override string Name => "input";

    public override IReadOnlyList<string> Extensions => ["inp"];

    internal int ProcessCount { get; private set; }

    protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new();

    protected override void Process(AssetProcessorContext context, TestSidecar sidecar) {
        ProcessCount++;
        context.AddInput(sidecar.File);
        context.Outputs.AddAsset(context.BaseAssetPath, new TestPayload { Name = "input" });
    }
}

/// <summary>A processor that emits an engine descriptor and a payload.</summary>
internal sealed class DescriptorProcessor : IAssetProcessor {
    public string Name => "descriptor";

    public IReadOnlyList<string> Extensions => ["descriptor"];

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context) {
        context.Outputs.AddAsset(
            context.BaseAssetPath,
            new TextureAssetDescriptor {
                Payload = context.BaseAssetPath + "-ktx2",
                Width = 1,
                Height = 1,
                MipLevels = 1,
                Format = TextureFormat.Rgba8Srgb,
                IsSrgb = true,
                Filter = TextureFilter.Linear,
                Wrap = TextureWrap.Repeat,
                MaxAnisotropy = 1,
            });
        context.Outputs.AddData(context.BaseAssetPath + "-ktx2", [0xAB, 0x4B, 0x54, 0x58], 16);
    }
}

/// <summary>A processor that reports a warning.</summary>
internal sealed class WarningProcessor : IAssetProcessor {
    public string Name => "warning";

    public IReadOnlyList<string> Extensions => ["warn"];

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context) {
        context.Diagnostics.Warning("RB4006", "deliberate warning");
        context.Outputs.AddAsset(context.BaseAssetPath, new TestPayload { Name = "warning" });
    }
}

/// <summary>A processor that emits a non-canonical output path.</summary>
internal sealed class InvalidOutputProcessor : IAssetProcessor {
    public string Name => "invalid-output";

    public IReadOnlyList<string> Extensions => ["badpath"];

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context)
        => context.Outputs.AddData("Bad/Path", [1]);
}

/// <summary>A processor that emits an invalid alignment.</summary>
internal sealed class BadAlignmentProcessor : IAssetProcessor {
    public string Name => "bad-alignment";

    public IReadOnlyList<string> Extensions => ["badalign"];

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context)
        => context.Outputs.AddData(context.BaseAssetPath, [1], alignment: 32);
}

/// <summary>A processor that emits the same path twice.</summary>
internal sealed class DuplicateOutputProcessor : IAssetProcessor {
    public string Name => "duplicate-output";

    public IReadOnlyList<string> Extensions => ["dupout"];

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context) {
        context.Outputs.AddData(context.BaseAssetPath, [1]);
        context.Outputs.AddData(context.BaseAssetPath, [2]);
    }
}

/// <summary>A table-named processor that registers a missing additional input.</summary>
internal sealed class MissingInputProcessor : AssetProcessor<TestSidecar> {
    public override string Name => "missing-input";

    public override IReadOnlyList<string> Extensions => ["badinput"];

    protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new();

    protected override void Process(AssetProcessorContext context, TestSidecar sidecar) {
        context.AddInput(sidecar.File);
        context.Outputs.AddAsset(context.BaseAssetPath, new TestPayload());
    }
}

/// <summary>A processor whose configuration property throws, forcing the CLI internal-error path.</summary>
internal sealed class ThrowingExtensionsProcessor : IAssetProcessor {
    public string Name => "throwing-extensions";

    public IReadOnlyList<string> Extensions
        => throw new InvalidOperationException("deliberate configuration failure");

    public SidecarPolicy Policy => SidecarPolicy.None;

    public PolyType.ITypeShape? SidecarShape => null;

    public void Process(AssetProcessorContext context) {
    }
}
