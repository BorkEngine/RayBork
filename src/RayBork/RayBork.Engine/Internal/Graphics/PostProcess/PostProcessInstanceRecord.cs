namespace RayBork.Internal;

/// <summary>
/// One ordered shader-ABI instance record an effect's run appends: the record stride in bytes and how
/// many consecutive records of that stride it writes.
/// </summary>
/// <remarks>
/// <para>
/// The frame's instance-byte simulation and the effect's recorder walk the same layout, so an effect can
/// append passes with different shaders, uniform sizes, and therefore different strides. Every entry
/// starts on its own stride boundary; the records inside one entry are contiguous because a stride is a
/// multiple of itself.
/// </para>
/// <para>
/// A custom shader effect reports one entry with its frozen record size and a count of one. A built-in
/// multi-pass effect reports one entry per pass it records, in the same order its
/// <see cref="PostProcessEffectSnapshot.Run"/> reserves them.
/// </para>
/// </remarks>
/// <param name="Stride">The instance-record size in bytes, including the fixed instance header.</param>
/// <param name="Count">The number of consecutive records of this stride.</param>
internal readonly record struct PostProcessInstanceRecord(int Stride, int Count);
