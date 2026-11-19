namespace RayBork.Internal;

/// <summary>One shader-ABI instance record an effect appends: stride in bytes and record count.</summary>
/// <remarks>
/// Entries preserve pass order and start on their own stride boundary, so effects with different
/// shaders or uniform sizes can share one instance buffer.
/// </remarks>
/// <param name="Stride">The instance-record size in bytes, including the fixed instance header.</param>
/// <param name="Count">The number of consecutive records of this stride.</param>
internal readonly record struct PostProcessInstanceRecord(int Stride, int Count);
