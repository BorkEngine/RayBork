using System.Diagnostics.CodeAnalysis;

// Deliberate policy suppressions for the engine assembly. Each entry names the exact type or member
// the analyzer flagged; nothing here suppresses a whole namespace or a whole rule. Mechanical fixes
// are applied in the source instead, and CA1031/CA2000 are suppressed per method at the call site
// because their justifications are specific to each boundary.

// --- Lifecycle: explicit Stop/Close/Destroy instead of IDisposable ------------------------------

[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The channel owns one wait handle that is closed with Close() and is released when the app tears down the surface; an IDisposable here would duplicate Close.",
    Scope = "type",
    Target = "~T:RayBork.Internal.MainThreadSurfaceChannel")]
[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tick thread is stopped with Stop() and joined with Join(); its stop source and stop signal live exactly one app run and are not disposable while the thread may still observe them.",
    Scope = "type",
    Target = "~T:RayBork.Internal.TickRunner")]
[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "AssetCatalog.Stop() disposes its glyph service, stop source, and worker signal; the catalog itself is started and stopped once per app and exposes no public Dispose.",
    Scope = "type",
    Target = "~T:RayBork.Internal.AssetCatalog")]
[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The bridge is closed with Close() on the render thread after the frame ring drains; the worker signal it owns is part of that render-thread teardown, not of a caller-facing lifetime.",
    Scope = "type",
    Target = "~T:RayBork.Internal.GpuAssetBridge")]
[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The ring's wait handles live for the whole app run and are released with the process; the tick and render threads observe the ring until they exit, so there is no safe point to dispose them.",
    Scope = "type",
    Target = "~T:RayBork.Internal.FrameRing")]
[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The asset worker owns its sampler cache for the render-thread lifetime and destroys it through the backend's Destroy path; it has no caller-facing Dispose.",
    Scope = "type",
    Target = "~T:RayBork.Internal.VulkanAssetWorker")]

// --- Value-type equality and operator surface ---------------------------------------------------

[assembly: SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "Value equality for the math and frame structs is a deliberate, separate public API decision; this task does not add it.",
    Scope = "type",
    Target = "~T:RayBork.BoundingFrustum")]
[assembly: SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "Color equality operators are a deliberate, separate public API decision; this task does not add them.",
    Scope = "type",
    Target = "~T:RayBork.Color")]
[assembly: SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "Glyph equality is a deliberate, separate public API decision; this task does not add it.",
    Scope = "type",
    Target = "~T:RayBork.Glyph")]
[assembly: SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "ModelPart equality is a deliberate, separate public API decision; this task does not add it.",
    Scope = "type",
    Target = "~T:RayBork.ModelPart")]
[assembly: SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "RayCollision equality is a deliberate, separate public API decision; this task does not add it.",
    Scope = "type",
    Target = "~T:RayBork.RayCollision")]
[assembly: SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "Transform equality is a deliberate, separate public API decision; this task does not add it.",
    Scope = "type",
    Target = "~T:RayBork.Transform")]
[assembly: SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "Vertex equality is a deliberate, separate public API decision; this task does not add it.",
    Scope = "type",
    Target = "~T:RayBork.Vertex")]
[assembly: SuppressMessage(
    "Usage",
    "CA2231:Overload operator equals on overriding value type Equals",
    Justification = "Color overrides Equals for interpolation and lookup; adding the equality operators is a deliberate, separate public API decision.",
    Scope = "type",
    Target = "~T:RayBork.Color")]

// --- Color operator alternates ------------------------------------------------------------------

[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color arithmetic operators are the intended API; named Add/Subtract/Multiply/Divide alternates would add public surface without adding capability.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Addition(RayBork.Color,RayBork.Color)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color arithmetic operators are the intended API; named alternates would add public surface without adding capability.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Subtraction(RayBork.Color,RayBork.Color)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color arithmetic operators are the intended API; named alternates would add public surface without adding capability.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Multiply(RayBork.Color,RayBork.Color)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color arithmetic operators are the intended API; named alternates would add public surface without adding capability.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Multiply(RayBork.Color,System.Single)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color arithmetic operators are the intended API; named alternates would add public surface without adding capability.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Multiply(System.Single,RayBork.Color)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color arithmetic operators are the intended API; named alternates would add public surface without adding capability.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Division(RayBork.Color,System.Single)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color<->Vector4 conversions are the intended API; named ToVector4/FromColor alternates would duplicate an operator the engine uses everywhere.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Implicit(RayBork.Color)~System.Numerics.Vector4")]
[assembly: SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The Color<->Vector4 conversions are the intended API; named ToColor/FromVector4 alternates would duplicate an operator the engine uses everywhere.",
    Scope = "member",
    Target = "~M:RayBork.Color.op_Implicit(System.Numerics.Vector4)~RayBork.Color")]

// --- Builder and wire-format shapes -------------------------------------------------------------

[assembly: SuppressMessage(
    "Design",
    "CA1002:Do not expose generic lists",
    Justification = "MeshBuilder intentionally exposes mutable List<Vertex>/List<uint> so callers can build geometry raylib-style with Add and Clear; the builder is a construction helper, not a data contract.",
    Scope = "member",
    Target = "~P:RayBork.MeshBuilder.Vertices")]
[assembly: SuppressMessage(
    "Design",
    "CA1002:Do not expose generic lists",
    Justification = "MeshBuilder intentionally exposes mutable List<Vertex>/List<uint> so callers can build geometry raylib-style with Add and Clear; the builder is a construction helper, not a data contract.",
    Scope = "member",
    Target = "~P:RayBork.MeshBuilder.Indices")]
[assembly: SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "AssetEntryType is the ushort entry type of the RAYBORKP v1 pack wire format; widening it would change the on-disk contract.",
    Scope = "type",
    Target = "~T:RayBork.AssetEntryType")]
[assembly: SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Float, Int, and UInt are the shader ABI names of the uniform types and must match the GLSL declarations.",
    Scope = "type",
    Target = "~T:RayBork.ShaderUniformType")]

// --- Internal implementation exceptions ---------------------------------------------------------

[assembly: SuppressMessage(
    "Design",
    "CA1064:Exceptions should be public",
    Justification = "FontFormatException is an internal implementation exception; callers see AssetLoadException or a diagnostic code.",
    Scope = "type",
    Target = "~T:RayBork.Internal.FontFormatException")]
[assembly: SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "FontFormatException is only constructed by the font decoder with a message, so the parameterless and inner-exception constructors have no caller.",
    Scope = "type",
    Target = "~T:RayBork.Internal.FontFormatException")]
[assembly: SuppressMessage(
    "Design",
    "CA1064:Exceptions should be public",
    Justification = "FontUnsupportedException is an internal implementation exception; callers see AssetLoadException or a diagnostic code.",
    Scope = "type",
    Target = "~T:RayBork.Internal.FontUnsupportedException")]
[assembly: SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "FontUnsupportedException is only constructed by the font decoder with a message, so the parameterless and inner-exception constructors have no caller.",
    Scope = "type",
    Target = "~T:RayBork.Internal.FontUnsupportedException")]
[assembly: SuppressMessage(
    "Design",
    "CA1064:Exceptions should be public",
    Justification = "MsdfLimitException is an internal implementation exception; callers see AssetLoadException or a diagnostic code.",
    Scope = "type",
    Target = "~T:RayBork.Internal.MsdfLimitException")]
[assembly: SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "MsdfLimitException is only constructed by the MSDF generator with a message, so the parameterless and inner-exception constructors have no caller.",
    Scope = "type",
    Target = "~T:RayBork.Internal.MsdfLimitException")]

// --- Paths and app lifetime ---------------------------------------------------------------------

[assembly: SuppressMessage(
    "Globalization",
    "CA1308:Normalize strings to uppercase",
    Justification = "Asset paths are canonical lowercase by contract, so ToLowerInvariant is the deliberate normalization and uppercasing would break every mounted pack lookup.",
    Scope = "member",
    Target = "~M:RayBork.Internal.AssetPath.Normalize(System.String)")]
[assembly: SuppressMessage(
    "Design",
    "CA1065:Do not raise exceptions in unexpected locations",
    Justification = "RayBorkApp.Dispose documents that calling it on the tick thread is a usage error: waiting for the tick thread from inside a game callback would deadlock.",
    Scope = "member",
    Target = "~M:RayBork.RayBorkApp.Dispose")]
[assembly: SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "Disposing the run-completed signal can race a concurrent idempotent Dispose waiter; the signal belongs to the process-lifetime app and is reclaimed when the app is collected.",
    Scope = "member",
    Target = "~F:RayBork.RayBorkApp._runCompletedSignal")]
