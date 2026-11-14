using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>One bitmap the fake factory received.</summary>
internal readonly record struct GlyphTextureCreation(int Width, int Height, byte[] Rgba);

/// <summary>
/// An <see cref="IGlyphTextureFactory"/> that records every bitmap, creates a detached
/// <see cref="Texture"/> over a <see cref="FakeGpuAssetResource"/>, and can inject an upload failure,
/// hold a creation, or replace the creation behavior.
/// </summary>
internal sealed class FakeGlyphTextureFactory : IGlyphTextureFactory {
    private readonly Lock _gate = new();
    private readonly List<GlyphTextureCreation> _creations = [];
    private readonly List<GpuAssetResource> _destroyed = [];
    private readonly TaskCompletionSource _creationStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long _nextId;
    private Exception? _uploadException;
    private Func<int, int, ReadOnlyMemory<byte>, CancellationToken, Task<Texture>>? _handler;

    /// <summary>Gets or sets an exception every call fails with before creating a texture.</summary>
    internal Exception? UploadException {
        get {
            lock (_gate) {
                return _uploadException;
            }
        }

        set {
            lock (_gate) {
                _uploadException = value;
            }
        }
    }

    /// <summary>Gets or sets a replacement for the default synchronous texture creation.</summary>
    internal Func<int, int, ReadOnlyMemory<byte>, CancellationToken, Task<Texture>>? Handler {
        get {
            lock (_gate) {
                return _handler;
            }
        }

        set {
            lock (_gate) {
                _handler = value;
            }
        }
    }

    /// <summary>Gets the number of <see cref="CreateAsync"/> calls.</summary>
    internal int CreateCount {
        get {
            lock (_gate) {
                return _creations.Count;
            }
        }
    }

    /// <summary>Gets the bitmaps passed to <see cref="CreateAsync"/>, in call order.</summary>
    internal IReadOnlyList<GlyphTextureCreation> Creations {
        get {
            lock (_gate) {
                return [.. _creations];
            }
        }
    }

    /// <summary>Gets the GPU resources the pipeline released, in release order.</summary>
    internal IReadOnlyList<GpuAssetResource> Destroyed {
        get {
            lock (_gate) {
                return [.. _destroyed];
            }
        }
    }

    /// <summary>Gets a task that completes when the first <see cref="CreateAsync"/> call starts.</summary>
    internal Task WhenCreateStartedAsync() => _creationStarted.Task;

    /// <inheritdoc/>
    public Task<Texture> CreateAsync(
        int width,
        int height,
        ReadOnlyMemory<byte> rgba,
        CancellationToken cancellationToken) {
        byte[] bitmap = rgba.ToArray();
        Exception? uploadException;
        Func<int, int, ReadOnlyMemory<byte>, CancellationToken, Task<Texture>>? handler;
        lock (_gate) {
            _creations.Add(new GlyphTextureCreation(width, height, bitmap));
            uploadException = _uploadException;
            handler = _handler;
            _creationStarted.TrySetResult();
        }

        if (uploadException is not null) {
            return Task.FromException<Texture>(uploadException);
        }

        if (handler is not null) {
            return handler(width, height, rgba, cancellationToken);
        }

        return Task.FromResult(CreateTexture(width, height));
    }

    /// <summary>Creates one detached texture with the requested dimensions.</summary>
    /// <param name="width">The texture width.</param>
    /// <param name="height">The texture height.</param>
    /// <returns>The texture, with one owner reference owned by the caller.</returns>
    internal Texture CreateTexture(int width, int height) {
        long id = Interlocked.Increment(ref _nextId);
        string path = $"glyph/texture-{id}";
        TextureAssetDescriptor descriptor = new() {
            Payload = path + "-msdf",
            Width = checked((uint)width),
            Height = checked((uint)height),
            MipLevels = 1,
            Format = TextureFormat.Rgba8Unorm,
            IsSrgb = false,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
            MaxAnisotropy = 1f,
        };
        TextureGpuUpload upload = new(path, descriptor, ReadOnlyMemory<byte>.Empty);
        FakeGpuAssetResource resource = new(upload, id);
        EngineAsset asset = new(path, resource, QueueDestroyed, static () => { });
        asset.AddOwnerReference();
        return new Texture(asset, descriptor, isProcedural: true);
    }

    private void QueueDestroyed(GpuAssetResource resource) {
        lock (_gate) {
            _destroyed.Add(resource);
        }
    }
}
