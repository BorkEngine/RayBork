using System.Numerics;
using RayBork.Bake.Diagnostics;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake;

public sealed class TomlShapeBinderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScalarArrayEnumVectorAndColorBind() {
        const string toml = """
            name = "castle"
            Count = 7
            ratio = 1.5
            enabled = true
            direction = "greater_or_equal"
            numbers = [1, 2, 3]
            offset = [1.0, 2.0, 3.0]
            tint = [0.1, 0.2, 0.3, 0.4]
            """;

        (object? value, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<ScalarConfig>(toml);

        Assert.False(diagnostics.HasErrors, BakeBindTestHarness.FirstError(diagnostics));
        var config = Assert.IsType<ScalarConfig>(value);
        Assert.Equal("castle", config.Name);
        Assert.Equal(7, config.Count);
        Assert.Equal(1.5f, config.Ratio);
        Assert.True(config.Enabled);
        Assert.Equal(TestDirection.GreaterOrEqual, config.Direction);
        Assert.Equal([1, 2, 3], config.Numbers);
        Assert.Equal(new Vector3(1f, 2f, 3f), config.Offset);
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), config.Tint);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SnakeCaseAndTheJsonPropertyNameOverrideAreDefaultKeys() {
        const string toml = """
            name = "The Castle"
            difficulty = 2
            paddle_speed = 3.5
            max_players = 4
            """;

        (object? value, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<GameSettings>(toml);

        Assert.False(diagnostics.HasErrors, BakeBindTestHarness.FirstError(diagnostics));
        var settings = Assert.IsType<GameSettings>(value);
        Assert.Equal("The Castle", settings.Name);
        Assert.Equal(2, settings.Difficulty);
        Assert.Equal(3.5f, settings.PaddleSpeed);
        Assert.Equal(4, settings.MaxPlayers);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NestedTablesAndTableArraysBind() {
        const string toml = """
            [inner]
            name = "root"
            count = 1

            [[items]]
            name = "first"
            count = 2

            [[items]]
            name = "second"
            count = 3
            """;

        (object? value, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<NestedConfig>(toml);

        Assert.False(diagnostics.HasErrors, BakeBindTestHarness.FirstError(diagnostics));
        var config = Assert.IsType<NestedConfig>(value);
        Assert.Equal("root", config.Inner.Name);
        Assert.Equal(1, config.Inner.Count);
        Assert.Equal(2, config.Items.Length);
        Assert.Equal("first", config.Items[0].Name);
        Assert.Equal(3, config.Items[1].Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullableMembersAcceptValuesAndMayBeAbsent() {
        (object? present, BakeDiagnostics presentDiagnostics) = BakeBindTestHarness.Bind<NullableConfig>(
            """
            count = 5
            label = "set"
            """);
        (object? absent, BakeDiagnostics absentDiagnostics) = BakeBindTestHarness.Bind<NullableConfig>(
            string.Empty);

        Assert.False(presentDiagnostics.HasErrors, BakeBindTestHarness.FirstError(presentDiagnostics));
        Assert.False(absentDiagnostics.HasErrors, BakeBindTestHarness.FirstError(absentDiagnostics));
        var presentConfig = Assert.IsType<NullableConfig>(present);
        Assert.Equal(5, presentConfig.Count);
        Assert.Equal("set", presentConfig.Label);
        var absentConfig = Assert.IsType<NullableConfig>(absent);
        Assert.Null(absentConfig.Count);
        Assert.Null(absentConfig.Label);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorParametersBindByNameIncludingADefault() {
        (object? value, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<ConstructorConfig>(
            """
            name = "constructor"
            count = 9
            direction = "greater_or_equal"
            """);

        Assert.False(diagnostics.HasErrors, BakeBindTestHarness.FirstError(diagnostics));
        var config = Assert.IsType<ConstructorConfig>(value);
        Assert.Equal("constructor", config.Name);
        Assert.Equal(9, config.Count);
        Assert.Equal(TestDirection.GreaterOrEqual, config.Direction);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void JsonPropertyNameAndJsonIgnoreAreHonored() {
        (object? value, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<AttributeConfig>(
            """
            custom_name = "renamed"
            required = "present"
            """);

        Assert.False(diagnostics.HasErrors, BakeBindTestHarness.FirstError(diagnostics));
        var config = Assert.IsType<AttributeConfig>(value);
        Assert.Equal("renamed", config.Name);
        Assert.Equal("present", config.Required);
        Assert.Equal("ignored", config.Ignored);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownKeysAreErrorsWithALocation() {
        (_, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<GameSettings>(
            """
            name = "x"
            does_not_exist = 1
            """);

        string error = BakeBindTestHarness.FirstError(diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains("RB4002", error);
        Assert.Contains("Unknown key 'does_not_exist'", error);
        Assert.Contains("test.toml(2,1)", error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingRequiredMembersAreErrors() {
        (_, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<GameSettings>(
            """
            difficulty = 1
            """);

        string error = BakeBindTestHarness.FirstError(diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains("RB4002", error);
        Assert.Contains("Missing required key 'name'", error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingCSharpRequiredMemberIsAnError() {
        (_, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<RequiredPropertyConfig>(
            string.Empty);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains("RB4002", BakeBindTestHarness.FirstError(diagnostics));
        Assert.Contains("name", BakeBindTestHarness.FirstError(diagnostics));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingJsonRequiredMemberIsAnError() {
        (_, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<AttributeConfig>(
            """
            custom_name = "renamed"
            """);

        string error = BakeBindTestHarness.FirstError(diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains("RB4002", error);
        Assert.Contains("required", error);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TypeMismatchesAndNumericEnumsAreRejected() {
        (_, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<ScalarConfig>(
            """
            count = "many"
            direction = 1
            """);

        Assert.True(diagnostics.HasErrors);
        string formatted = string.Join(
            "\n",
            diagnostics.GetSorted().Select(diagnostic => diagnostic.Format()));
        Assert.Contains("must be an integer", formatted);
        Assert.Contains("numeric enum value is not accepted", formatted);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IntegerRangeIsChecked() {
        (_, BakeDiagnostics diagnostics) = BakeBindTestHarness.Bind<ScalarConfig>(
            """
            count = 999999999999
            """);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains("between", BakeBindTestHarness.FirstError(diagnostics));
        Assert.Contains("RB4002", BakeBindTestHarness.FirstError(diagnostics));
    }
}
