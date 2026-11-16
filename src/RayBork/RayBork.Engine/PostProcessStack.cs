using System.Diagnostics.CodeAnalysis;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// An app-lifetime, caller-owned ordered list of camera post-process effects.
/// </summary>
/// <remarks>
/// <para>
/// Create one stack, keep it for the app lifetime, and pass it to
/// <see cref="EngineContext.CreateCamera"/> as <c>postProcess:</c>. Effects run in list order: a custom
/// effect draws one fullscreen pass over the previous result, so two effects chain into two passes. The
/// stack is not disposable and owns neither the effects nor their shaders.
/// </para>
/// <para>
/// The stack is tick-thread only. A camera snapshots <see cref="Enabled"/>, the effect order, each
/// effect's <see cref="PostProcessEffect.Enabled"/> value, and each custom effect's shader cursor and
/// texture bindings when the camera is created. Later changes apply to later cameras only, and a
/// disabled stack or a disabled effect is skipped.
/// </para>
/// <para>
/// <see cref="Effects"/> is a read-only view. <see cref="Add"/> rejects <see langword="null"/> and
/// allows duplicates, so the same effect can run twice; <see cref="Remove"/> removes the first matching
/// entry by reference and reports whether it removed one.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "The approved public API names the post-process stack; the type is an ordered effect list, not a collection implementation.")]
public sealed class PostProcessStack {
    private readonly List<PostProcessEffect> _effects = [];
    private readonly IReadOnlyList<PostProcessEffect> _effectsView;
    private bool _enabled = true;

    /// <summary>Initializes an enabled, empty stack.</summary>
    public PostProcessStack() {
        _effectsView = _effects.AsReadOnly();
    }

    /// <summary>
    /// Gets or sets whether the stack runs. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The setter is tick-thread only. A camera snapshots the value at creation; disabling the stack
    /// afterwards does not remove the effects from an already recorded frame.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public bool Enabled {
        get => _enabled;
        set {
            TickThread.RequireCurrent(nameof(Enabled));
            _enabled = value;
        }
    }

    /// <summary>Gets the effects in run order. The view is read-only.</summary>
    public IReadOnlyList<PostProcessEffect> Effects => _effectsView;

    /// <summary>
    /// Appends an effect. Duplicates are allowed and run once per entry.
    /// </summary>
    /// <param name="effect">The effect to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void Add(PostProcessEffect effect) {
        TickThread.RequireCurrent(nameof(Add));
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
    }

    /// <summary>
    /// Removes the first entry that is the same object as <paramref name="effect"/>.
    /// </summary>
    /// <param name="effect">The effect to remove.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public bool Remove(PostProcessEffect effect) {
        TickThread.RequireCurrent(nameof(Remove));
        ArgumentNullException.ThrowIfNull(effect);
        return _effects.Remove(effect);
    }

    /// <summary>
    /// Removes every effect. The stack keeps its <see cref="Enabled"/> value and remains usable.
    /// </summary>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void Clear() {
        TickThread.RequireCurrent(nameof(Clear));
        _effects.Clear();
    }
}
