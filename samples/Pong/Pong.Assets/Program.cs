using Pong;
using RayBork.Bake;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("pong.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<PongSettings>()
    .UseDefaultProcessors()
    .Build()
    .RunAsync(args);
