using RayBork.Internal;
using SDL3;

namespace RayBork.Tests;

[Collection("SDL")]
public sealed class InputMappingTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryKeyValueRoundTripsThroughItsSdlScancode() {
        foreach (Key key in Enum.GetValues<Key>()) {
            int value = (int)key;
            Assert.InRange(value, 0, InputMappings.KEY_COUNT - 1);
            SDL.Scancode scancode = (SDL.Scancode)value;
            Assert.True(Enum.IsDefined(scancode), $"Key.{key} ({value}) has no matching SDL scancode.");
            Assert.Equal(key, InputMappings.ToKey(scancode));
        }
    }

    [Theory]
    [InlineData(Key.W, SDL.Scancode.W)]
    [InlineData(Key.A, SDL.Scancode.A)]
    [InlineData(Key.Escape, SDL.Scancode.Escape)]
    [InlineData(Key.Enter, SDL.Scancode.Return)]
    [InlineData(Key.LeftShift, SDL.Scancode.LShift)]
    [InlineData(Key.F12, SDL.Scancode.F12)]
    [InlineData(Key.Keypad9, SDL.Scancode.Kp9)]
    [InlineData(Key.EndCall, SDL.Scancode.EndCall)]
    [InlineData(Key.Unknown, SDL.Scancode.Unknown)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RepresentativeScancodesMapToTheirKeys(Key expected, SDL.Scancode scancode) {
        Assert.Equal(expected, InputMappings.ToKey(scancode));
    }

    [Theory]
    [InlineData(1, MouseButton.Left)]
    [InlineData(2, MouseButton.Middle)]
    [InlineData(3, MouseButton.Right)]
    [InlineData(4, MouseButton.X1)]
    [InlineData(5, MouseButton.X2)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdlMouseButtonsMapToRayBorkButtons(byte sdlButton, MouseButton expected) {
        Assert.True(InputMappings.TryGetMouseButton(sdlButton, out MouseButton button));
        Assert.Equal(expected, button);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(255)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedSdlMouseButtonsAreRejected(byte sdlButton) {
        Assert.False(InputMappings.TryGetMouseButton(sdlButton, out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryGamepadButtonMapsFromItsSharedSdlValue() {
        foreach (GamepadButton button in Enum.GetValues<GamepadButton>()) {
            Assert.True(
                InputMappings.TryGetGamepadButton((SDL.GamepadButton)(int)button, out GamepadButton mapped),
                $"GamepadButton.{button} has no SDL mapping.");
            Assert.Equal(button, mapped);
        }
    }

    [Theory]
    [InlineData(SDL.GamepadButton.RightPaddle1)]
    [InlineData(SDL.GamepadButton.LeftPaddle1)]
    [InlineData(SDL.GamepadButton.RightPaddle2)]
    [InlineData(SDL.GamepadButton.LeftPaddle2)]
    [InlineData(SDL.GamepadButton.Misc4)]
    [InlineData(SDL.GamepadButton.Misc5)]
    [InlineData(SDL.GamepadButton.Misc6)]
    [InlineData(SDL.GamepadButton.Invalid)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedGamepadButtonsAreRejected(SDL.GamepadButton sdlButton) {
        Assert.False(InputMappings.TryGetGamepadButton(sdlButton, out _));
    }

    [Theory]
    [InlineData(0, GamepadAxis.LeftX)]
    [InlineData(1, GamepadAxis.LeftY)]
    [InlineData(2, GamepadAxis.RightX)]
    [InlineData(3, GamepadAxis.RightY)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdlStickAxesMapToGamepadAxes(byte sdlAxis, GamepadAxis expected) {
        Assert.True(InputMappings.TryGetGamepadAxis(sdlAxis, out GamepadAxis axis));
        Assert.Equal(expected, axis);
        Assert.False(InputMappings.TryGetGamepadTrigger(sdlAxis, out _));
    }

    [Theory]
    [InlineData(4, GamepadTrigger.Left)]
    [InlineData(5, GamepadTrigger.Right)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdlTriggerAxesMapToGamepadTriggers(byte sdlAxis, GamepadTrigger expected) {
        Assert.True(InputMappings.TryGetGamepadTrigger(sdlAxis, out GamepadTrigger trigger));
        Assert.Equal(expected, trigger);
        Assert.False(InputMappings.TryGetGamepadAxis(sdlAxis, out _));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(255)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedSdlAxesAreRejected(byte sdlAxis) {
        Assert.False(InputMappings.TryGetGamepadAxis(sdlAxis, out _));
        Assert.False(InputMappings.TryGetGamepadTrigger(sdlAxis, out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StickAxisValuesNormalizeToMinusOneThroughOne() {
        Assert.Equal(0f, InputMappings.NormalizeStickAxis(0));
        Assert.Equal(1f, InputMappings.NormalizeStickAxis(short.MaxValue));
        Assert.Equal(-1f, InputMappings.NormalizeStickAxis(short.MinValue));
        Assert.Equal(0.5f, InputMappings.NormalizeStickAxis(16384), 3);
        Assert.Equal(-0.5f, InputMappings.NormalizeStickAxis(-16384), 3);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TriggerValuesNormalizeToZeroThroughOne() {
        Assert.Equal(0f, InputMappings.NormalizeTrigger(0));
        Assert.Equal(0f, InputMappings.NormalizeTrigger(short.MinValue));
        Assert.Equal(1f, InputMappings.NormalizeTrigger(short.MaxValue));
        Assert.Equal(0.5f, InputMappings.NormalizeTrigger(16384), 3);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AxisAndTriggerSlotsAreDistinct() {
        Assert.Equal(0, InputMappings.AxisSlot(GamepadAxis.LeftX));
        Assert.Equal(3, InputMappings.AxisSlot(GamepadAxis.RightY));
        Assert.Equal(4, InputMappings.TriggerSlot(GamepadTrigger.Left));
        Assert.Equal(5, InputMappings.TriggerSlot(GamepadTrigger.Right));
    }
}
