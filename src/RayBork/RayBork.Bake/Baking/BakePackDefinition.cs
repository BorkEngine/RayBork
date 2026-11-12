namespace RayBork.Bake.Baking;

/// <summary>One configured pack: its output path and its asset root.</summary>
/// <param name="PackPath">The output-root-relative pack path with <c>/</c> separators.</param>
/// <param name="Root">The project-root-relative asset root with <c>/</c> separators.</param>
internal sealed record BakePackDefinition(string PackPath, string Root);
