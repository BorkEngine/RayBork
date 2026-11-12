using RayBork.Bake;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("castle.pck", "Assets")
    .UseDefaultAssets()
    .UseDefaultProcessors()
    .Build()
    .RunAsync(args);
