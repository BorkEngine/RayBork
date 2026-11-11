using RayBork;
using SDL3;

namespace HelloInterop;

internal static class Program {
    private static int Main() {
        Console.WriteLine($"RayBork {EngineVersion.Current}");

        if (!SDL.Init((SDL.InitFlags)0)) {
            Console.Error.WriteLine($"SDL initialization failed: {SDL.GetError()}");
            return 1;
        }

        try {
            int version = SDL.GetVersion();
            int major = version / 1_000_000;
            int minor = version / 1_000 % 1_000;
            int patch = version % 1_000;

            Console.WriteLine($"SDL {major}.{minor}.{patch}");
            Console.WriteLine($"SDL revision {SDL.GetRevision()}");
        } finally {
            SDL.Quit();
        }

        return 0;
    }
}
