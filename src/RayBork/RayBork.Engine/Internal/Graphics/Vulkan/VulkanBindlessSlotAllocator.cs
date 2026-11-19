namespace RayBork.Internal;

/// <summary>The pure slot allocator behind <see cref="VulkanBindlessTextureTable"/>.</summary>
/// <remarks>
/// Slot 0 is reserved for the white fallback and allocations take the lowest free slot. The render
/// thread owns it and it is not thread-safe.
/// </remarks>
internal sealed class VulkanBindlessSlotAllocator
{
    private readonly bool[] _used;
    private int _liveCount;

    /// <summary>Initializes an allocator with a fixed capacity, including the white slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Invalid capacity.</exception>
    internal VulkanBindlessSlotAllocator(uint capacity)
    {
        ArgumentOutOfRangeException.ThrowIfZero(capacity);
        if (capacity > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "The bindless slot capacity must fit in an index array."
            );
        }

        _used = new bool[capacity];
    }

    /// <summary>Gets the total number of slots, including the reserved white slot.</summary>
    internal uint Capacity => (uint)_used.Length;

    /// <summary>Gets the number of live slots, including the reserved white slot.</summary>
    internal int LiveCount => _liveCount;

    /// <summary>Reserves slot 0 for the white fallback. The call is idempotent.</summary>
    internal void ReserveWhiteSlot()
    {
        if (_used[0])
        {
            return;
        }

        _used[0] = true;
        _liveCount++;
    }

    /// <summary>Tries to allocate the lowest free slot above slot 0.</summary>
    /// <param name="slot">The allocated slot, or <c>0</c> when the table is full.</param>
    /// <returns><see langword="true"/> when a slot was allocated.</returns>
    internal bool TryAllocate(out uint slot)
    {
        for (int index = 1; index < _used.Length; index++)
        {
            if (_used[index])
            {
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

    /// <summary>Releases one slot.</summary>
    /// <remarks>The white slot, an unused slot, or an out-of-range slot is ignored.</remarks>
    internal void Release(uint slot)
    {
        if (slot == 0 || slot >= (uint)_used.Length || !_used[slot])
        {
            return;
        }

        _used[slot] = false;
        _liveCount--;
    }

    /// <summary>Releases every slot, including the white slot, before a device recreation.</summary>
    internal void Reset()
    {
        Array.Clear(_used);
        _liveCount = 0;
    }
}
