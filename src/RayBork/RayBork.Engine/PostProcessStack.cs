using System.Diagnostics.CodeAnalysis;
using RayBork.Internal;

namespace RayBork;

/// <summary>An app-lifetime, caller-owned ordered list of camera post-process effects.</summary>
/// <remarks>
/// Effects run in list order, one fullscreen pass each, and the stack is tick-thread only. A camera
/// snapshots the order, the stack's <see cref="Enabled"/> flag, and each effect's
/// <see cref="PostProcessEffect.Enabled"/> flag at creation; it owns no effects or shaders.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "The approved public API names the post-process stack; the type is an ordered effect list, not a collection implementation."
)]
public sealed class PostProcessStack
{
    private readonly List<PostProcessEffect> _effects = [];
    private readonly IReadOnlyList<PostProcessEffect> _effectsView;
    private bool _enabled = true;

    /// <summary>Initializes an enabled, empty stack.</summary>
    public PostProcessStack()
    {
        _effectsView = _effects.AsReadOnly();
    }

    /// <summary>Gets or sets whether the stack runs; enabled by default.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            TickThread.RequireCurrent(nameof(Enabled));
            _enabled = value;
        }
    }

    /// <summary>Gets the effects in run order; the view is read-only.</summary>
    public IReadOnlyList<PostProcessEffect> Effects => _effectsView;

    /// <summary>Appends an effect; duplicates are allowed and run once per entry.</summary>
    public void Add(PostProcessEffect effect)
    {
        TickThread.RequireCurrent(nameof(Add));
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
    }

    /// <summary>Removes the first entry that is the same object as the given effect.</summary>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public bool Remove(PostProcessEffect effect)
    {
        TickThread.RequireCurrent(nameof(Remove));
        ArgumentNullException.ThrowIfNull(effect);
        return _effects.Remove(effect);
    }

    /// <summary>Removes every effect; the stack keeps its enabled state and stays usable.</summary>
    public void Clear()
    {
        TickThread.RequireCurrent(nameof(Clear));
        _effects.Clear();
    }
}
