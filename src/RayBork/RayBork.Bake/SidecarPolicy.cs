namespace RayBork.Bake;

/// <summary>Selects whether a processor source has an authoring sidecar.</summary>
public enum SidecarPolicy
{
    /// <summary>The processor has no sidecar.</summary>
    None,

    /// <summary>A missing sidecar uses the in-memory default; no file is created.</summary>
    Optional,

    /// <summary>A sidecar is required; a missing file is a binding error.</summary>
    Required,
}
