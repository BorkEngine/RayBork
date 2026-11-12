namespace RayBork.Internal;

/// <summary>
/// One instance inside a planned batch.
/// </summary>
/// <remarks>
/// The instance data is read from the recorded draw:
/// <c>Record.GetWorldMatrices()[InstanceIndex]</c>, <c>Record.Tint</c>, <c>Record.UvRect</c>,
/// <c>Record.InstanceTemplate</c>, and <c>Record.GetTextureBindings()</c>. The record and the packet it
/// belongs to are valid until the render thread releases the frame slot.
/// </remarks>
/// <param name="Record">The draw record that supplies the per-instance data.</param>
/// <param name="InstanceIndex">The zero-based index into the record's instance arrays.</param>
/// <param name="Depth">The view-space depth of the instance origin; larger values are farther away.</param>
internal readonly record struct PlannedInstance(RenderDrawRecord Record, int InstanceIndex, float Depth);
