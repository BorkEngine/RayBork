using System.Buffers.Binary;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Assets;

public sealed class PackBuilderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyPackWritesAHeaderOnly() {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);

        byte[] pack = builder.Build();

        Assert.Equal(PackFormat.PREFIX_LENGTH, pack.Length);
        AssertHeader(pack, entryCount: 0, indexLength: 0);

        using PackArchive archive = PackArchive.Open(
            pack.AsMemory(),
            PackTestData.DISPLAY_PATH);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IndexEntryLengthMatchesTheWireLayout() {
        Assert.Equal(34, PackFormat.INDEX_ENTRY_FIXED_LENGTH);
        Assert.Equal(
            PackFormat.INDEX_ENTRY_FIXED_LENGTH + Encoding.UTF8.GetByteCount("data/settings"),
            PackIndex.GetEntryLength("data/settings"));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HeaderAndIndexMatchTheDocumentedLayout() {
        byte[] pack = PackTestData.BuildStandardPack();
        int indexLength = checked((int)ReadUInt32(pack, PackFormat.INDEX_LENGTH_OFFSET));

        AssertHeader(pack, entryCount: 3, indexLength);
        Assert.Equal(3u, ReadUInt32(pack, PackFormat.ENTRY_COUNT_OFFSET));

        string[] expectedPaths = [
            PackTestData.SETTINGS_PATH,
            PackTestData.MESH_PATH,
            PackTestData.TEXTURE_PATH,
        ];
        AssetEntryType[] expectedTypes = [
            AssetEntryType.Data,
            AssetEntryType.Mesh,
            AssetEntryType.Texture,
        ];
        int[] expectedAlignments = [1, 16, 256];
        ushort[] expectedFlags = [
            0,
            PackFormat.ENTRY_FLAG_ALIGNED,
            PackFormat.ENTRY_FLAG_ALIGNED,
        ];
        byte[][] expectedPayloads = [
            PackTestData.SettingsPayload,
            PackTestData.Pattern(1000, 7),
            PackTestData.Repeated(4096, 0xFE),
        ];

        int position = PackFormat.PREFIX_LENGTH;
        for (int i = 0; i < expectedPaths.Length; i++) {
            int pathLength = BinaryPrimitives.ReadUInt16LittleEndian(pack.AsSpan(position));
            position += 2;
            Assert.Equal(Encoding.UTF8.GetByteCount(expectedPaths[i]), pathLength);
            Assert.Equal(expectedPaths[i], Encoding.UTF8.GetString(pack, position, pathLength));
            position += pathLength;

            AssetEntryType type = (AssetEntryType)ReadUInt16(pack, position);
            position += 2;
            ushort flags = ReadUInt16(pack, position);
            position += 2;
            ulong offset = ReadUInt64(pack, position);
            position += 8;
            uint storedLength = ReadUInt32(pack, position);
            position += 4;
            uint uncompressedSize = ReadUInt32(pack, position);
            position += 4;
            uint alignment = ReadUInt32(pack, position);
            position += 4;
            ulong storedHash = ReadUInt64(pack, position);
            position += 8;

            Assert.Equal(expectedTypes[i], type);
            Assert.Equal(expectedFlags[i], flags);
            Assert.Equal((uint)expectedPayloads[i].Length, storedLength);
            Assert.Equal((uint)expectedPayloads[i].Length, uncompressedSize);
            Assert.Equal((uint)expectedAlignments[i], alignment);
            Assert.Equal(0ul, offset % alignment);
            Assert.True(offset >= (ulong)(PackFormat.PREFIX_LENGTH + indexLength));
            Assert.Equal(
                PackCompression.ComputePayloadHash(expectedPayloads[i]),
                storedHash);
            Assert.Equal(expectedPayloads[i], pack.AsSpan((int)offset, (int)storedLength).ToArray());
        }

        Assert.Equal(position, PackFormat.PREFIX_LENGTH + indexLength);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EntriesAreStoredInOrdinalPathOrder() {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);
        builder.Add("textures/white", AssetEntryType.Texture, [1], 1);
        builder.Add("data/settings", AssetEntryType.Data, [2], 1);
        builder.Add("models/cube", AssetEntryType.Mesh, [3], 1);

        Assert.Equal(
            ["textures/white", "data/settings", "models/cube"],
            builder.Entries.Select(entry => entry.Path));

        using PackArchive archive = PackArchive.Open(
            builder.Build().AsMemory(),
            PackTestData.DISPLAY_PATH);
        Assert.Equal(
            ["data/settings", "models/cube", "textures/white"],
            archive.Entries.Select(entry => entry.Path));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DebugPolicyStoresPayloadsUncompressed() {
        byte[] payload = PackTestData.Pattern(257, 11);
        var builder = new PackBuilder("debug.pck", PackCompressionPolicy.Debug);
        builder.Add("data/pattern", AssetEntryType.Data, payload, 16);

        using PackArchive archive = PackArchive.Open(
            builder.Build().AsMemory(),
            "debug.pck");

        PackEntry entry = Assert.Single(archive.Entries);
        Assert.False(entry.IsCompressed);
        Assert.True(entry.IsAligned);
        Assert.Equal((uint)payload.Length, entry.StoredLength);
        Assert.Equal((uint)payload.Length, entry.UncompressedSize);
        Assert.Equal(payload, archive.ReadPayload(entry));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReleasePolicyCompressesOnlyWhenStrictlySmaller() {
        var builder = new PackBuilder("release.pck", PackCompressionPolicy.Release);
        builder.Add("data/tiny", AssetEntryType.Data, [0x2A], 1);
        builder.Add("data/zeros", AssetEntryType.Data, new byte[4096], 1);

        using PackArchive archive = PackArchive.Open(
            builder.Build().AsMemory(),
            "release.pck");

        PackEntry tiny = archive.Entries[0];
        Assert.Equal("data/tiny", tiny.Path);
        Assert.False(tiny.IsCompressed);
        Assert.Equal(1u, tiny.StoredLength);
        Assert.Equal([0x2A], archive.ReadPayload(tiny));

        PackEntry zeros = archive.Entries[1];
        Assert.Equal("data/zeros", zeros.Path);
        Assert.True(zeros.IsCompressed);
        Assert.True(zeros.StoredLength < 4096);
        Assert.Equal(4096u, zeros.UncompressedSize);
        Assert.Equal(new byte[4096], archive.ReadPayload(zeros));
    }

    [Theory]
    [InlineData(1u, false)]
    [InlineData(16u, true)]
    [InlineData(64u, true)]
    [InlineData(256u, true)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AlignedFlagMatchesTheRequestedAlignment(uint alignment, bool expectedAligned) {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);
        builder.Add("data/payload", AssetEntryType.Data, [1, 2, 3], alignment);

        using PackArchive archive = PackArchive.Open(
            builder.Build().AsMemory(),
            PackTestData.DISPLAY_PATH);

        PackEntry entry = Assert.Single(archive.Entries);
        Assert.Equal(expectedAligned, entry.IsAligned);
        Assert.Equal(alignment, entry.Alignment);
        Assert.Equal(0ul, entry.Offset % alignment);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PayloadOffsetsAreAlignedAndPaddingIsZero() {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);
        builder.Add("a/first", AssetEntryType.Raw, PackTestData.Pattern(37, 1), 1);
        builder.Add("b/second", AssetEntryType.Raw, PackTestData.Pattern(11, 2), 64);
        builder.Add("c/third", AssetEntryType.Raw, PackTestData.Pattern(5, 3), 256);
        builder.Add("d/fourth", AssetEntryType.Raw, PackTestData.Pattern(3, 4), 16);
        byte[] pack = builder.Build();

        using PackArchive archive = PackArchive.Open(
            pack.AsMemory(),
            PackTestData.DISPLAY_PATH);

        long previousEnd = PackFormat.PREFIX_LENGTH
            + ReadUInt32(pack, PackFormat.INDEX_LENGTH_OFFSET);
        foreach (PackEntry entry in archive.Entries) {
            Assert.Equal(0ul, entry.Offset % entry.Alignment);
            Assert.True(entry.Offset >= (ulong)previousEnd);

            for (long position = previousEnd; position < (long)entry.Offset; position++) {
                Assert.Equal(0, pack[position]);
            }

            previousEnd = checked((long)entry.Offset + entry.StoredLength);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroLengthPayloadIsStoredAndRead() {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Release);
        builder.Add("data/empty", AssetEntryType.Data, ReadOnlySpan<byte>.Empty, 256);
        byte[] pack = builder.Build();

        using PackArchive archive = PackArchive.Open(
            pack.AsMemory(),
            PackTestData.DISPLAY_PATH);

        PackEntry entry = Assert.Single(archive.Entries);
        Assert.Equal(0u, entry.StoredLength);
        Assert.Equal(0u, entry.UncompressedSize);
        Assert.Equal(0ul, entry.Offset % 256);
        Assert.Empty(archive.ReadPayload(entry));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicatePathIsRejected() {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);
        builder.Add("data/settings", AssetEntryType.Data, [1], 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.Add("data/settings", AssetEntryType.Raw, [2], 1));

        Assert.Contains(PackTestData.DISPLAY_PATH, exception.Message);
        Assert.Contains("data/settings", exception.Message);
    }

    [Theory]
    [InlineData("/data/settings")]
    [InlineData("Data/Settings")]
    [InlineData("data//settings")]
    [InlineData("data/settings/")]
    [InlineData("data/./settings")]
    [InlineData("data/settings.json")]
    [InlineData("data\\settings")]
    [InlineData("")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonCanonicalPathIsRejected(string path) {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.Add(path, AssetEntryType.Data, [1], 1));

        Assert.Contains(PackTestData.DISPLAY_PATH, exception.Message);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(32u)]
    [InlineData(128u)]
    [InlineData(512u)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidAlignmentIsRejected(uint alignment) {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.Add("data/settings", AssetEntryType.Data, [1], alignment));

        Assert.Contains("alignment", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownEntryTypeIsRejected() {
        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.Add("data/settings", (AssetEntryType)99, [1], 1));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownCompressionPolicyIsRejected() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackBuilder(PackTestData.DISPLAY_PATH, (PackCompressionPolicy)99));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullArgumentsAreRejected() {
        Assert.Throws<ArgumentNullException>(
            () => new PackBuilder(null!, PackCompressionPolicy.Debug));
        Assert.Throws<ArgumentException>(
            () => new PackBuilder(" ", PackCompressionPolicy.Debug));
        Assert.Throws<ArgumentNullException>(
            () => new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug, null!));

        var builder = new PackBuilder(PackTestData.DISPLAY_PATH, PackCompressionPolicy.Debug);
        Assert.Throws<ArgumentNullException>(
            () => builder.Add(null!, AssetEntryType.Data, [1], 1));
        Assert.Throws<ArgumentNullException>(() => builder.Add(null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorQueuesEntriesAndValidatesThem() {
        PackSourceEntry[] entries = [
            new("data/a", AssetEntryType.Data, [1], 1),
            new("data/b", AssetEntryType.Raw, [2], 16),
        ];

        var builder = new PackBuilder(
            PackTestData.DISPLAY_PATH,
            PackCompressionPolicy.Debug,
            entries);

        Assert.Equal(entries, builder.Entries);

        Assert.Throws<ArgumentException>(
            () => new PackBuilder(
                PackTestData.DISPLAY_PATH,
                PackCompressionPolicy.Debug,
                [entries[0], entries[0]]));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WriteToLeavesTheStreamOpen() {
        var builder = PackTestData.CreateStandardBuilder();
        using var stream = new MemoryStream();

        builder.WriteTo(stream);

        Assert.True(stream.CanWrite);
        Assert.Equal(stream.Length, stream.Position);

        stream.Position = 0;
        byte[] written = stream.ToArray();
        using PackArchive archive = PackArchive.Open(
            written.AsMemory(),
            PackTestData.DISPLAY_PATH);
        AssertRoundTrip(archive);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WriteCreatesAnOpenableFile() {
        using var directory = new TempPackDirectory();
        string path = directory.GetPath("assets.pck");
        PackTestData.CreateStandardBuilder().Write(path);

        Assert.True(File.Exists(path));
        using PackArchive archive = PackArchive.Open(path);
        Assert.Equal(Path.GetFullPath(path), archive.DisplayPath);
        Assert.Equal(new FileInfo(path).Length, archive.Length);
        AssertRoundTrip(archive);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WriteRejectsAnEmptyOrInvalidPath() {
        var builder = PackTestData.CreateStandardBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.Write(null!));
        Assert.Throws<ArgumentException>(() => builder.Write(" "));
    }

    private static void AssertRoundTrip(PackArchive archive) {
        Assert.Equal(3, archive.Entries.Count);
        Assert.Equal(
            [PackTestData.SETTINGS_PATH, PackTestData.MESH_PATH, PackTestData.TEXTURE_PATH],
            archive.Entries.Select(entry => entry.Path));
        Assert.Equal(PackTestData.SettingsPayload, archive.ReadPayload(archive.Entries[0]));
        Assert.Equal(PackTestData.Pattern(1000, 7), archive.ReadPayload(archive.Entries[1]));
        Assert.Equal(PackTestData.Repeated(4096, 0xFE), archive.ReadPayload(archive.Entries[2]));
    }

    private static void AssertHeader(byte[] pack, uint entryCount, int indexLength) {
        Assert.Equal(PackFormat.MAGIC_TEXT, Encoding.ASCII.GetString(pack, 0, PackFormat.MAGIC_LENGTH));
        Assert.Equal(PackFormat.CURRENT_VERSION, ReadUInt32(pack, PackFormat.VERSION_OFFSET));
        Assert.Equal(0u, ReadUInt32(pack, PackFormat.FLAGS_OFFSET));
        Assert.Equal(entryCount, ReadUInt32(pack, PackFormat.ENTRY_COUNT_OFFSET));
        Assert.Equal((ulong)PackFormat.PREFIX_LENGTH, ReadUInt64(pack, PackFormat.INDEX_OFFSET_OFFSET));
        Assert.Equal((uint)indexLength, ReadUInt32(pack, PackFormat.INDEX_LENGTH_OFFSET));
        Assert.Equal(0u, ReadUInt32(pack, PackFormat.RESERVED_OFFSET));
        Assert.Equal(
            PackCompression.ComputeIndexHash(pack.AsSpan(PackFormat.PREFIX_LENGTH, indexLength)),
            ReadUInt32(pack, PackFormat.INDEX_HASH_OFFSET));
    }

    private static ushort ReadUInt16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));

    private static uint ReadUInt32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private static ulong ReadUInt64(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
}
