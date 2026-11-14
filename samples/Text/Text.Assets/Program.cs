using RayBork.Bake;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("text.pck", "Assets")
    .UseDefaultAssets()
    .UseDefaultProcessors()
    .Build()
    .RunAsync(args);
