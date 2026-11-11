using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Serialization;
using PolyType;

namespace RayBork.Tests.Bake.TestInfrastructure;

/// <summary>One shaped custom asset emitted by test processors.</summary>
[GenerateShape]
internal sealed partial class TestPayload {
    [DefaultValue("")]
    public string Name { get; set; } = string.Empty;

    public int Value { get; set; }
}

/// <summary>A custom-data type used by the data TOML tests.</summary>
[GenerateShape]
internal sealed partial class GameSettings {
    public required string Name { get; set; }

    public int Difficulty { get; set; }

    public float PaddleSpeed { get; set; }

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }
}

/// <summary>A binder enum.</summary>
internal enum TestDirection {
    Less,
    GreaterOrEqual,
}

/// <summary>A scalar/enum/vector/color binder fixture.</summary>
[GenerateShape]
internal sealed partial class ScalarConfig {
    [DefaultValue("")]
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    public float Ratio { get; set; }

    public bool Enabled { get; set; }

    public TestDirection Direction { get; set; }

    public int[] Numbers { get; set; } = [];

    public Vector3 Offset { get; set; }

    public Color Tint { get; set; }
}

/// <summary>A nested-table binder fixture.</summary>
[GenerateShape]
internal sealed partial class NestedConfig {
    public ScalarConfig Inner { get; set; } = new();

    public ScalarConfig[] Items { get; set; } = [];
}

/// <summary>A nullable-member binder fixture.</summary>
[GenerateShape]
internal sealed partial class NullableConfig {
    public int? Count { get; set; }

    public string? Label { get; set; }
}

/// <summary>A constructor-binding fixture.</summary>
[GenerateShape]
internal sealed partial class ConstructorConfig {
    public ConstructorConfig(string name, int count, TestDirection direction = TestDirection.Less) {
        Name = name;
        Count = count;
        Direction = direction;
    }

    public string Name { get; }

    public int Count { get; }

    public TestDirection Direction { get; }
}

/// <summary>A name-attribute and ignore fixture.</summary>
[GenerateShape]
internal sealed partial class AttributeConfig {
    [JsonPropertyName("custom_name")]
    [DefaultValue("")]
    public string Name { get; set; } = string.Empty;

    [JsonIgnore]
    [DefaultValue("ignored")]
    public string Ignored { get; set; } = "ignored";

    [JsonRequired]
    public string? Required { get; set; }
}

/// <summary>A property-initializer binder fixture with a C# required member.</summary>
[GenerateShape]
internal sealed partial class RequiredPropertyConfig {
    public required string Name { get; set; }
}

/// <summary>A sidecar shape for the optional/required processor tests.</summary>
[GenerateShape]
internal sealed partial class TestSidecar {
    public int Value { get; set; }

    [DefaultValue("")]
    public string Label { get; set; } = string.Empty;

    [DefaultValue("")]
    public string File { get; set; } = string.Empty;
}
