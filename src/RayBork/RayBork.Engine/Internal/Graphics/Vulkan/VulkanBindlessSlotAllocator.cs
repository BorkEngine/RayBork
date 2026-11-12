namespace RayBork.Internal;

/// <summary>
/// The pure slot allocator behind <see cref="VulkanBindlessTextureTable"/>.
/// </summary>
/// <remarks>
/// Slot 0 is reserved for the renderer's white fallback texture and is always live once
/// <see cref="ReserveWhiteSlot"/> runs. Allocations take the lowest free slot so a frame's indices
/// are deterministic. The allocator owns no GPU object and is not thread-safe; the render thread
/// calls it while it services texture uploads and destructions.
/// </remarks>
internal sealed class VulkanBindlessSlotAllocator {
    private readonly bool[] _used;
    private int _liveCount;

    /// <summary>
    /// Initializes an allocator with a fixed capacity.
    /// </summary>
    /// <param name="capacity">The number of slots, including the reserved white slot.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is zero.</exception>
    internal VulkanBindlessSlotAllocator(uint capacity) {
        ArgumentOutOfRangeException.ThrowIfZero(capacity);
        if (capacity > int.MaxValue) {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "The bindless slot capacity must fit in an index array.");
        }

        _used = new bool[capacity];
    }

    /// <summary>Gets the total number of slots, including the reserved white slot.</summary>
    internal uint Capacity => (uint)_used.Length;

    /// <summary>Gets the number of live slots, including the reserved white slot.</summary>
    internal int LiveCount => _liveCount;

    /// <summary>
    /// Reserves slot 0 for the renderer's white fallback texture. Idempotent.
    /// </summary>
    internal void ReserveWhiteSlot() {
        if (_used[0]) {
            return;
        }

        _used[0] = true;
        _liveCount++;
    }

    /// <summary>
    /// Tries to allocate the lowest free texture slot.
    /// </summary>
    /// <param name="slot">The allocated slot, or <c>0</c> when the table is full.</param>
    /// <returns><see langword="true"/> when a slot was allocated.</returns>
    internal bool TryAllocate(out uint slot) {
        for (int index = 1; index < _used.Length; index++) {
            if (_used[index]) {
                continue;
            }

            _used[index] = true;
            _liveCount++;
            slot = (uint)index;
            return true;
        }

        slot = 0;
        return false;
    }

    /// <summary>
    /// Releases one texture slot. Releasing the white slot, an out-of-range slot, or an unused slot is
    /// a no-op.
    /// </summary>
    /// <param name="slot">The slot to release.</param>
    internal void Release(uint slot) {
        if (slot == 0 || slot >= (uint)_used.Length || !_used[slot]) {
            return;
        }

        _used[slot] = false;
        _liveCount--;
    }

    /// <summary>
    /// Releases every slot including the white slot. Used before a device recreation.
    /// </summary>
    internal void Reset() {
        Array.Clear(_used);
        _liveCount = 0;
    }
}
