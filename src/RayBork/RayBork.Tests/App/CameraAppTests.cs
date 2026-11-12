using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

public sealed class CameraAppTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TickThreadIsAttachedForSetupTickAndShutdown() {
        bool setupAttached = false;
        bool tickAttached = false;
        bool shutdownAttached = false;
        ScriptedGame game = new() {
            SetupHandler = _ => {
                setupAttached = TickThread.IsCurrent;
                return Task.CompletedTask;
            },
            TickHandler = context => {
                tickAttached = TickThread.IsCurrent;
                context.RequestExit();
            },
            ShutdownHandler = () => {
                shutdownAttached = TickThread.IsCurrent;
                return Task.CompletedTask;
            },
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.True(setupAttached);
        Assert.True(tickAttached);
        Assert.True(shutdownAttached);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CameraCreationIsRejectedInSetup() {
        Exception? failure = null;
        ScriptedGame game = new() {
            SetupHandler = context => {
                try {
                    context.CreateCamera(Transform.Identity, CameraProjection.Perspective, 60f);
                } catch (Exception exception) {
                    failure = exception;
                }

                return Task.CompletedTask;
            },
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("during IGame.Tick", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CameraCreationFromAnotherThreadIsRejected() {
        Exception? failure = null;
        ScriptedGame game = new() {
            TickHandler = context => {
                Thread thread = new(() => {
                    try {
                        context.CreateCamera(Transform.Identity, CameraProjection.Perspective, 60f);
                    } catch (Exception exception) {
                        failure = exception;
                    }
                }) {
                    IsBackground = true,
                    Name = "RayBork.Tests.CameraThread",
                };
                thread.Start();
                thread.Join();
                context.RequestExit();
            },
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("tick-thread", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CamerasAreRecordedIntoThePublishedPacket() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        Color clearColor = new(0.1f, 0.2f, 0.3f, 1f);
        List<SubmittedFrame> frames = [];
        Lock gate = new();
        try {
            ScriptedGame game = new();
            using AppTestHost host = new(game);
            host.FakeRenderer.OnSubmit = packet => {
                if (packet.PassCount == 0) {
                    return;
                }

                CameraPass pass = packet.Passes[0];
                lock (gate) {
                    frames.Add(new SubmittedFrame(
                        packet.FrameIndex,
                        packet.PassCount,
                        pass.DrawCount,
                        pass.ClearMode,
                        pass.ClearColor,
                        pass.ViewportWidth,
                        pass.ViewportHeight));
                }
            };
            game.TickHandler = context => {
                using Camera camera = context.CreateCamera(
                    Transform.Identity,
                    CameraProjection.Perspective,
                    60f,
                    clearMode: ClearMode.ColorAndDepth,
                    clearColor: clearColor,
                    near: 0.1f,
                    far: 100f);
                camera.DrawMesh(mesh, Transform.Identity, shader);
                if (context.FrameCount >= 3) {
                    context.RequestExit();
                }
            };

            host.App.Run();

            SubmittedFrame[] submitted;
            lock (gate) {
                submitted = [.. frames];
            }

            Assert.NotEmpty(submitted);
            Assert.All(submitted, frame => {
                Assert.Equal(1, frame.PassCount);
                Assert.Equal(1, frame.DrawCount);
                Assert.Equal(ClearMode.ColorAndDepth, frame.ClearMode);
                Assert.Equal(clearColor, frame.ClearColor);
                Assert.Equal(1280, frame.ViewportWidth);
                Assert.Equal(720, frame.ViewportHeight);
            });
        } finally {
            mesh.Dispose();
            shader.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SequentialCamerasInOneTickKeepCreationOrder() {
        List<(ClearMode First, ClearMode Second)> frames = [];
        Lock gate = new();
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        host.FakeRenderer.OnSubmit = packet => {
            if (packet.PassCount == 2) {
                lock (gate) {
                    frames.Add((packet.Passes[0].ClearMode, packet.Passes[1].ClearMode));
                }
            }
        };
        game.TickHandler = context => {
            Camera first = context.CreateCamera(
                Transform.Identity,
                CameraProjection.Perspective,
                60f,
                clearMode: ClearMode.ColorAndDepth,
                near: 0.1f,
                far: 100f);
            first.Dispose();
            Camera second = context.CreateCamera(
                Transform.Identity,
                CameraProjection.Perspective,
                60f,
                clearMode: ClearMode.None,
                near: 0.1f,
                far: 100f);
            second.Dispose();
            context.RequestExit();
        };

        host.App.Run();

        lock (gate) {
            Assert.NotEmpty(frames);
            Assert.All(frames, frame => {
                Assert.Equal(ClearMode.ColorAndDepth, frame.First);
                Assert.Equal(ClearMode.None, frame.Second);
            });
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ALeakedCameraStopsTheLoopWithoutPublishingTheFrame() {
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.TickHandler = context => context.CreateCamera(
            Transform.Identity,
            CameraProjection.Perspective,
            60f,
            near: 0.1f,
            far: 100f);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(host.App.Run);

        Assert.Contains("Camera 1", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("not disposed", thrown.Message, StringComparison.Ordinal);
        Assert.Empty(host.FakeRenderer.SubmitFrames);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateCameraRejectsTheOrthographicProjection() {
        Exception? failure = null;
        ScriptedGame game = new() {
            TickHandler = context => {
                try {
                    context.CreateCamera(
                        Transform.Identity,
                        CameraProjection.Orthographic,
                        60f,
                        near: 0.1f,
                        far: 100f);
                } catch (Exception exception) {
                    failure = exception;
                }

                context.RequestExit();
            },
        };
        using AppTestHost host = new(game);

        host.App.Run();

        ArgumentException thrown = Assert.IsType<ArgumentException>(failure);
        Assert.Equal("projection", thrown.ParamName);
    }

    private readonly record struct SubmittedFrame(
        long FrameIndex,
        int PassCount,
        int DrawCount,
        ClearMode ClearMode,
        Color ClearColor,
        int ViewportWidth,
        int ViewportHeight);
}
