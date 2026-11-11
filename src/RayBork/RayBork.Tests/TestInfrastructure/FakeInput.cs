using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// An empty <see cref="IInput"/> snapshot. Every level query reports up, every edge query reports
/// absent, and no gamepads are connected.
/// </summary>
internal sealed class FakeInput : IInput {
    public string TextInput => string.Empty;

    public Vector2 MousePosition => Vector2.Zero;

    public Vector2 MouseDelta => Vector2.Zero;

    public float MouseWheel => 0f;

    public IReadOnlyList<IGamepad> Gamepads => [];

    public bool IsKeyDown(Key key) => false;

    public bool IsKeyPressed(Key key) => false;

    public bool IsKeyPressedRepeat(Key key) => false;

    public bool IsKeyReleased(Key key) => false;

    public bool IsKeyUp(Key key) => true;

    public bool IsMouseButtonDown(MouseButton button) => false;

    public bool IsMouseButtonPressed(MouseButton button) => false;

    public bool IsMouseButtonReleased(MouseButton button) => false;

    public bool IsMouseButtonUp(MouseButton button) => true;

    public IGamepad? GetGamepad(int index) => null;
}
