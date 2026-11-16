using RayBork.Bake;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("rendertarget.pck", "Assets")
    .UseDefaultAssets()
    .UseDefaultProcessors()
    .Build()
    .RunAsync(args);
