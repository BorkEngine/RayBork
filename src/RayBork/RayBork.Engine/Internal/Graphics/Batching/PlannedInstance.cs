namespace RayBork.Internal;

/// <summary>
/// One instance inside a planned batch.
/// </summary>
/// <remarks>
/// <para>
/// The instance data is read from the recorded draw:
/// <c>Record.GetWorldMatrices()[InstanceIndex]</c>, <c>Record.Tint</c>, <c>Record.UvRect</c>,
/// <c>Record.InstanceTemplate</c>, and the record's texture bindings. A draw with a per-instance set
/// reads its own run from <c>Record.GetInstanceTextureBindings()</c> at
/// <c>InstanceIndex * Record.TextureBindingCount</c>; a shared-set draw repeats
/// <c>Record.GetTextureBindings()</c>. The record and the packet it belongs to are valid until the
/// render thread releases the frame slot.
/// </para>
/// <para>
/// <see cref="InstanceIndex"/> is the index inside the draw record, not the position inside the planned
/// batch: a transparent batch reorders its instances, so a writer that used the plan position would pair
/// one instance's matrix with another instance's texture bindings.
/// </para>
/// </remarks>
/// <param name="Record">The draw record that supplies the per-instance data.</param>
/// <param name="InstanceIndex">The zero-based index into the record's instance arrays.</param>
/// <param name="Depth">The view-space depth of the instance origin; larger values are farther away.</param>
internal readonly record struct PlannedInstance(RenderDrawRecord Record, int InstanceIndex, float Depth);
