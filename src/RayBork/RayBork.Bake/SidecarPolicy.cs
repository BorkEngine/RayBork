namespace RayBork.Bake;

/// <summary>
/// Selects whether a processor source has an authoring sidecar.
/// </summary>
public enum SidecarPolicy {
    /// <summary>The processor has no sidecar.</summary>
    None,

    /// <summary>
    /// The processor may have a sidecar. A missing sidecar is replaced in memory by the processor's
    /// default sidecar; no source file is created.
    /// </summary>
    Optional,

    /// <summary>The processor requires a sidecar; a missing file is a binding error.</summary>
    Required,
}
