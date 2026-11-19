namespace RayBork.Internal;

/// <summary>One instance inside a planned batch.</summary>
/// <remarks>Data comes from <c>Record</c>; <c>InstanceIndex</c> is the record index, not the possibly
/// reordered batch position. The record is valid while the frame slot is held.</remarks>
/// <param name="Record">The draw record that supplies the per-instance data.</param>
/// <param name="InstanceIndex">The zero-based index into the record's instance arrays.</param>
/// <param name="Depth">The view-space depth of the instance origin; larger values are farther away.</param>
internal readonly record struct PlannedInstance(
    RenderDrawRecord Record,
    int InstanceIndex,
    float Depth
);
