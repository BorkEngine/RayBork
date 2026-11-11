using System.Collections.Concurrent;
using RayBork.Internal;

namespace RayBork.Tests.Assets;

public sealed class PackArchiveTests {
    private const string DISPLAY_PATH = "corrupt.pck";
    private const int THREAD_COUNT = 8;
    private const int READ_COUNT = 25;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OpenFromMemoryExposesEntryMetadata() {
        byte[] pack = PackTestData.BuildStandardPack();

        using PackArchive archive = Open(pack);

        Assert.Equal(DISPLAY_PATH, archive.DisplayPath);
        Assert.Equal(pack.Length, archive.Length);
        Assert.Equal(3, archive.Entries.Count);

        PackEntry settings = archive.Entries[0];
        Assert.Equal(PackTestData.SETTINGS_PATH, settings.Path);
        Assert.Equal(AssetEntryType.Data, settings.Type);
        Assert.Equal((ushort)0, settings.Flags);
        Assert.False(settings.IsCompressed);
        Assert.False(settings.IsAligned);
        Assert.Equal(1u, settings.Alignment);
        Assert.Equal(5u, settings.StoredLength);
        Assert.Equal(5u, settings.UncompressedSize);
        Assert.Equal(
            PackCompression.ComputePayloadHash(PackTestData.SettingsPayload),
            settings.StoredHash);

        PackEntry mesh = archive.Entries[1];
        Assert.Equal(PackTestData.MESH_PATH, mesh.Path);
        Assert.Equal(AssetEntryType.Mesh, mesh.Type);
        Assert.True(mesh.IsAligned);
        Assert.Equal(16u, mesh.Alignment);
        Assert.Equal(1000u, mesh.UncompressedSize);

        PackEntry texture = archive.Entries[2];
        Assert.Equal(PackTestData.TEXTURE_PATH, texture.Path);
        Assert.Equal(AssetEntryType.Texture, texture.Type);
        Assert.Equal(256u, texture.Alignment);
        Assert.Equal(4096u, texture.UncompressedSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TryGetEntryFindsKnownAndAbsentPaths() {
        using PackArchive archive = Open(PackTestData.BuildStandardPack());

        Assert.True(archive.TryGetEntry(PackTestData.SETTINGS_PATH, out PackEntry? settings));
        Assert.Same(archive.Entries[0], settings);
        Assert.False(archive.TryGetEntry("data/missing", out PackEntry? missing));
        Assert.Null(missing);
        Assert.Throws<ArgumentNullException>(() => archive.TryGetEntry(null!, out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadPayloadReturnsAFreshArrayEachTime() {
        byte[] pack = PackTestData.BuildStandardPack(PackCompressionPolicy.Release);
        using PackArchive archive = Open(pack);
        PackEntry entry = archive.Entries[2];
        byte[] expected = PackTestData.Repeated(4096, 0xFE);

        byte[] first = archive.ReadPayload(entry);
        Assert.Equal(expected, first);

        first[0] = 0x00;
        byte[] second = archive.ReadPayload(entry);
        Assert.Equal(expected, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadPayloadRejectsForeignEntries() {
        byte[] pack = PackTestData.BuildStandardPack();
        using PackArchive archive = Open(pack);
        using PackArchive other = PackArchive.Open(pack.AsMemory(), "other.pck");
        PackEntry real = archive.Entries[0];
        var copy = new PackEntry(
            real.Path,
            real.Type,
            real.Flags,
            real.Offset,
            real.StoredLength,
            real.UncompressedSize,
            real.Alignment,
            real.StoredHash);

        Assert.Throws<ArgumentException>(() => archive.ReadPayload(copy));
        Assert.Throws<ArgumentException>(() => other.ReadPayload(real));
        Assert.Throws<ArgumentNullException>(() => archive.ReadPayload(null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConcurrentPayloadReadsAreSafe() {
        byte[] pack = PackTestData.BuildStandardPack(PackCompressionPolicy.Release);
        using PackArchive archive = Open(pack);
        PackEntry[] entries = archive.Entries.ToArray();
        byte[][] expected = entries.Select(archive.ReadPayload).ToArray();

        var failures = new ConcurrentQueue<Exception>();
        var threads = new Thread[THREAD_COUNT];
        for (int i = 0; i < threads.Length; i++) {
            threads[i] = new Thread(() => {
                try {
                    for (int read = 0; read < READ_COUNT; read++) {
                        for (int entry = 0; entry < entries.Length; entry++) {
                            byte[] payload = archive.ReadPayload(entries[entry]);
                            if (!payload.AsSpan().SequenceEqual(expected[entry])) {
                                throw new InvalidOperationException(
                                    $"Concurrent read of '{entries[entry].Path}' returned different bytes.");
                            }
                        }
                    }
                } catch (Exception exception) {
                    failures.Enqueue(exception);
                }
            });
        }

        foreach (Thread thread in threads) {
            thread.Start();
        }

        foreach (Thread thread in threads) {
            thread.Join();
        }

        Assert.Empty(failures);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeIsIdempotentAndRejectsUseAfterDispose() {
        PackArchive archive = Open(PackTestData.BuildStandardPack());
        PackEntry entry = archive.Entries[0];
        archive.Dispose();
        archive.Dispose();

        Assert.Throws<ObjectDisposedException>(() => archive.ReadPayload(entry));
        Assert.Throws<ObjectDisposedException>(
            () => archive.TryGetEntry(PackTestData.SETTINGS_PATH, out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OpenFromFileReadsThroughTheMemoryMap() {
        using var directory = new TempPackDirectory();
        string path = directory.GetPath("assets.pck");
        PackTestData.CreateStandardBuilder(PackCompressionPolicy.Release).Write(path);

        using PackArchive archive = PackArchive.Open(path);

        Assert.Equal(Path.GetFullPath(path), archive.DisplayPath);
        Assert.Equal(new FileInfo(path).Length, archive.Length);
        Assert.Equal(3, archive.Entries.Count);
        Assert.Equal(PackTestData.SettingsPayload, archive.ReadPayload(archive.Entries[0]));
        Assert.Equal(PackTestData.Pattern(1000, 7), archive.ReadPayload(archive.Entries[1]));
        Assert.Equal(PackTestData.Repeated(4096, 0xFE), archive.ReadPayload(archive.Entries[2]));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingOrUnreadableFileIsRb1001() {
        using var directory = new TempPackDirectory();
        string missing = directory.GetPath("missing.pck");
        string empty = directory.GetPath("empty.pck");
        string truncated = directory.GetPath("truncated.pck");
        File.WriteAllBytes(empty, []);
        File.WriteAllBytes(truncated, new byte[20]);

        AssetPackException missingException = Assert.Throws<AssetPackException>(
            () => PackArchive.Open(missing));
        Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, missingException.DiagnosticCode);
        Assert.Contains(missing, missingException.Message);

        AssetPackException emptyException = Assert.Throws<AssetPackException>(
            () => PackArchive.Open(empty));
        Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, emptyException.DiagnosticCode);

        AssetPackException truncatedException = Assert.Throws<AssetPackException>(
            () => PackArchive.Open(truncated));
        Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, truncatedException.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OpenFileRejectsNullEmptyAndWhitespacePaths() {
        Assert.Throws<ArgumentNullException>(() => PackArchive.Open((string)null!));
        Assert.Throws<ArgumentException>(() => PackArchive.Open(" "));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OpenMemoryRequiresADisplayPath() {
        byte[] pack = PackTestData.BuildStandardPack();

        Assert.Throws<ArgumentNullException>(() => PackArchive.Open(pack.AsMemory(), null!));
        Assert.Throws<ArgumentException>(() => PackArchive.Open(pack.AsMemory(), ""));
        Assert.Throws<ArgumentException>(() => PackArchive.Open(pack.AsMemory(), " "));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryTruncatedHeaderLengthIsRb1001() {
        byte[] pack = PackTestData.BuildStandardPack();

        for (int length = 0; length < PackFormat.PREFIX_LENGTH; length++) {
            byte[] truncated = pack[..length];
            AssetPackException exception = AssertOpenFails(
                truncated,
                DiagnosticCodes.ASSET_PACK_READ_FAILED);
            Assert.Contains(DISPLAY_PATH, exception.Message);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TruncatedIndexIsRb1001() {
        var editor = new PackTestPack(PackTestData.BuildStandardPack());
        editor.SetHeaderUInt32(PackFormat.INDEX_LENGTH_OFFSET, (uint)editor.Bytes.Length);

        AssertOpenFails(editor.Bytes, DiagnosticCodes.ASSET_PACK_READ_FAILED);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BadMagicOrVersionIsRb1002() {
        byte[] badMagic = PackTestData.BuildStandardPack();
        badMagic[0] = (byte)'X';
        AssetPackException magicException = AssertOpenFails(
            badMagic,
            DiagnosticCodes.ASSET_PACK_UNSUPPORTED_VERSION);
        Assert.Contains("magic", magicException.Message);

        var versionEditor = new PackTestPack(PackTestData.BuildStandardPack());
        versionEditor.SetHeaderUInt32(PackFormat.VERSION_OFFSET, PackFormat.CURRENT_VERSION + 1);
        AssertOpenFails(versionEditor.Bytes, DiagnosticCodes.ASSET_PACK_UNSUPPORTED_VERSION);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonZeroHeaderFlagsOrReservedIsRb1002() {
        var flagsEditor = new PackTestPack(PackTestData.BuildStandardPack());
        flagsEditor.SetHeaderUInt32(PackFormat.FLAGS_OFFSET, 1);
        AssetPackException flagsException = AssertOpenFails(
            flagsEditor.Bytes,
            DiagnosticCodes.ASSET_PACK_UNSUPPORTED_VERSION);
        Assert.Contains("flags", flagsException.Message);

        var reservedEditor = new PackTestPack(PackTestData.BuildStandardPack());
        reservedEditor.SetHeaderUInt32(PackFormat.RESERVED_OFFSET, 1);
        AssertOpenFails(reservedEditor.Bytes, DiagnosticCodes.ASSET_PACK_UNSUPPORTED_VERSION);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HeaderEntryCountAndIndexFieldsAreValidated() {
        var countEditor = new PackTestPack(PackTestData.BuildStandardPack());
        countEditor.SetHeaderUInt32(
            PackFormat.ENTRY_COUNT_OFFSET,
            PackFormat.MAX_ENTRY_COUNT + 1);
        AssertOpenFails(countEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var offsetEditor = new PackTestPack(PackTestData.BuildStandardPack());
        offsetEditor.SetHeaderUInt64(PackFormat.INDEX_OFFSET_OFFSET, PackFormat.PREFIX_LENGTH + 1);
        AssertOpenFails(offsetEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var lengthEditor = new PackTestPack(PackTestData.BuildStandardPack());
        lengthEditor.SetHeaderUInt32(
            PackFormat.INDEX_LENGTH_OFFSET,
            PackFormat.MAX_INDEX_LENGTH + 1);
        AssertOpenFails(lengthEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IndexHashMismatchIsRb1004() {
        var editor = new PackTestPack(PackTestData.BuildStandardPack());
        editor.CorruptIndexHash();

        AssetPackException exception = AssertOpenFails(
            editor.Bytes,
            DiagnosticCodes.ASSET_PACK_INDEX_HASH_MISMATCH);
        Assert.Contains("index hash", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EntryCountMismatchIsRb1003() {
        byte[] pack = BuildSmallPathPack();

        var larger = new PackTestPack((byte[])pack.Clone());
        larger.SetHeaderUInt32(PackFormat.ENTRY_COUNT_OFFSET, 4);
        AssertOpenFails(larger.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var smaller = new PackTestPack((byte[])pack.Clone());
        smaller.SetHeaderUInt32(PackFormat.ENTRY_COUNT_OFFSET, 2);
        AssetPackException exception = AssertOpenFails(
            smaller.Bytes,
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
        Assert.Contains("trailing bytes", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonCanonicalIndexPathIsRb1003() {
        var editor = new PackTestPack(BuildSmallPathPack());
        editor.SetPath("aaa", "AAA");

        AssetPackException exception = AssertOpenFails(
            editor.Bytes,
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
        Assert.Contains("not canonical", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsortedIndexIsRb1003() {
        var editor = new PackTestPack(BuildSmallPathPack());
        editor.SetPath("aaa", "zzz");

        AssetPackException exception = AssertOpenFails(
            editor.Bytes,
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
        Assert.Contains("sorts before", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MalformedUtf8IndexPathIsRb1003() {
        var editor = new PackTestPack(BuildSmallPathPack());
        PackTestEntryLocation location = editor.FindEntry("aaa");
        editor.Bytes[location.PathOffset + 2] = 0xFF;
        editor.RehashIndex();

        AssetPackException exception = AssertOpenFails(
            editor.Bytes,
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
        Assert.Contains("UTF-8", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownEntryTypeMessageNamesTheEntryPath() {
        var editor = new PackTestPack(BuildSmallPathPack());
        editor.SetField("bbb", PackTestEntryField.Type, 99);

        AssetPackException exception = AssertOpenFails(
            editor.Bytes,
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
        Assert.Contains("bbb", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateIndexPathIsRb1003() {
        var editor = new PackTestPack(BuildSmallPathPack());
        editor.SetPath("bbb", "aaa");

        AssetPackException exception = AssertOpenFails(
            editor.Bytes,
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
        Assert.Contains("duplicate asset path", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownTypeFlagAndAlignmentFieldsAreRb1003() {
        var typeEditor = new PackTestPack(BuildSmallPathPack());
        typeEditor.SetField("aaa", PackTestEntryField.Type, 99);
        AssertOpenFails(typeEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var flagEditor = new PackTestPack(BuildSmallPathPack());
        flagEditor.SetField("aaa", PackTestEntryField.Flags, 0x0004);
        AssertOpenFails(flagEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var alignedEditor = new PackTestPack(BuildSmallPathPack());
        alignedEditor.SetField("aaa", PackTestEntryField.Flags, PackFormat.ENTRY_FLAG_ALIGNED);
        AssertOpenFails(alignedEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var alignmentEditor = new PackTestPack(BuildSmallPathPack());
        alignmentEditor.SetField("aaa", PackTestEntryField.Alignment, 3);
        AssertOpenFails(alignmentEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UncompressedLengthMismatchIsRb1003() {
        var editor = new PackTestPack(BuildSmallPathPack());
        editor.SetField("aaa", PackTestEntryField.UncompressedSize, 2);

        AssetPackException exception = AssertOpenFails(
            editor.Bytes,
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
        Assert.Contains("stored length", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PayloadOffsetsAndBoundsAreValidated() {
        byte[] pack = BuildSmallPathPack();

        var beforeData = new PackTestPack((byte[])pack.Clone());
        beforeData.SetField("aaa", PackTestEntryField.Offset, 0);
        AssertOpenFails(beforeData.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var pastEnd = new PackTestPack((byte[])pack.Clone());
        pastEnd.SetField(
            "aaa",
            PackTestEntryField.Offset,
            (ulong)pack.Length + 1);
        AssertOpenFails(pastEnd.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var alignedBuilder = new PackBuilder("paths.pck", PackCompressionPolicy.Debug);
        alignedBuilder.Add("aaa", AssetEntryType.Raw, [1], 16);
        var alignmentEditor = new PackTestPack(alignedBuilder.Build());
        ulong currentOffset = alignmentEditor.GetField("aaa", PackTestEntryField.Offset);
        alignmentEditor.SetField("aaa", PackTestEntryField.Offset, currentOffset + 1);
        AssertOpenFails(alignmentEditor.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);

        var storedPastEnd = new PackTestPack(
            PackTestData.BuildStandardPack(PackCompressionPolicy.Release));
        ulong textureOffset = storedPastEnd.GetField(
            PackTestData.TEXTURE_PATH,
            PackTestEntryField.Offset);
        storedPastEnd.SetField(
            PackTestData.TEXTURE_PATH,
            PackTestEntryField.StoredLength,
            (uint)(storedPastEnd.Bytes.Length - (int)textureOffset + 1));
        AssertOpenFails(storedPastEnd.Bytes, DiagnosticCodes.ASSET_PACK_INVALID_INDEX);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PayloadHashMismatchIsRb1005() {
        var editor = new PackTestPack(PackTestData.BuildStandardPack());
        editor.FlipPayloadByte(PackTestData.SETTINGS_PATH, 0);
        using PackArchive archive = Open(editor.Bytes);

        AssetLoadException exception = AssertPayloadFails(
            archive,
            archive.Entries[0],
            DiagnosticCodes.ASSET_PACK_PAYLOAD_HASH_MISMATCH);
        Assert.Contains(PackTestData.SETTINGS_PATH, exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompressedPayloadHashMismatchIsRb1005() {
        var editor = new PackTestPack(PackTestData.BuildStandardPack(PackCompressionPolicy.Release));
        using PackArchive probe = Open(editor.Bytes);
        PackEntry compressed = Assert.Single(probe.Entries, entry => entry.IsCompressed);
        editor.FlipPayloadByte(compressed.Path, 0);

        using PackArchive archive = Open(editor.Bytes);
        AssertPayloadFails(
            archive,
            archive.Entries.Single(entry => entry.IsCompressed),
            DiagnosticCodes.ASSET_PACK_PAYLOAD_HASH_MISMATCH);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CorruptCompressedPayloadIsRb1008() {
        var editor = new PackTestPack(PackTestData.BuildStandardPack(PackCompressionPolicy.Release));
        using PackArchive probe = Open(editor.Bytes);
        PackEntry compressed = Assert.Single(probe.Entries, entry => entry.IsCompressed);
        editor.SetPayloadByte(compressed.Path, 0, 0x00);
        editor.RepairPayloadHash(compressed.Path);

        using PackArchive archive = Open(editor.Bytes);
        PackEntry corrupt = archive.Entries.Single(entry => entry.IsCompressed);
        AssetLoadException exception = AssertPayloadFails(
            archive,
            corrupt,
            DiagnosticCodes.ASSET_LOAD_FAILED);
        Assert.Contains(corrupt.Path, exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DeclaredDecompressedSizeMismatchIsRb1008() {
        var shorter = new PackTestPack(
            PackTestData.BuildStandardPack(PackCompressionPolicy.Release));
        using PackArchive probe = Open(shorter.Bytes);
        PackEntry compressed = Assert.Single(probe.Entries, entry => entry.IsCompressed);
        shorter.SetField(
            compressed.Path,
            PackTestEntryField.UncompressedSize,
            compressed.UncompressedSize - 1);
        using PackArchive shortArchive = Open(shorter.Bytes);
        AssertPayloadFails(
            shortArchive,
            shortArchive.Entries.Single(entry => entry.IsCompressed),
            DiagnosticCodes.ASSET_LOAD_FAILED);

        var longer = new PackTestPack(
            PackTestData.BuildStandardPack(PackCompressionPolicy.Release));
        longer.SetField(
            compressed.Path,
            PackTestEntryField.UncompressedSize,
            compressed.UncompressedSize + 1);
        using PackArchive longArchive = Open(longer.Bytes);
        AssertPayloadFails(
            longArchive,
            longArchive.Entries.Single(entry => entry.IsCompressed),
            DiagnosticCodes.ASSET_LOAD_FAILED);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FailedPayloadReadLeavesTheArchiveUsable() {
        var editor = new PackTestPack(PackTestData.BuildStandardPack());
        editor.FlipPayloadByte(PackTestData.SETTINGS_PATH, 0);
        using PackArchive archive = Open(editor.Bytes);

        AssertPayloadFails(
            archive,
            archive.Entries[0],
            DiagnosticCodes.ASSET_PACK_PAYLOAD_HASH_MISMATCH);

        Assert.Equal(PackTestData.Pattern(1000, 7), archive.ReadPayload(archive.Entries[1]));
        Assert.Equal(PackTestData.Repeated(4096, 0xFE), archive.ReadPayload(archive.Entries[2]));
    }

    private static PackArchive Open(byte[] pack)
        => PackArchive.Open(pack.AsMemory(), DISPLAY_PATH);

    private static byte[] BuildSmallPathPack() {
        var builder = new PackBuilder("paths.pck", PackCompressionPolicy.Debug);
        builder.Add("aaa", AssetEntryType.Raw, [1], 1);
        builder.Add("bbb", AssetEntryType.Raw, [2], 1);
        builder.Add("ccc", AssetEntryType.Raw, [3], 1);
        return builder.Build();
    }

    private static AssetPackException AssertOpenFails(byte[] pack, string expectedCode) {
        AssetPackException exception = Assert.Throws<AssetPackException>(
            () => PackArchive.Open(pack.AsMemory(), DISPLAY_PATH));

        Assert.Equal(expectedCode, exception.DiagnosticCode);
        Assert.Contains(DISPLAY_PATH, exception.Message);
        return exception;
    }

    private static AssetLoadException AssertPayloadFails(
        PackArchive archive,
        PackEntry entry,
        string expectedCode) {
        AssetLoadException exception = Assert.Throws<AssetLoadException>(
            () => archive.ReadPayload(entry));

        Assert.Equal(expectedCode, exception.DiagnosticCode);
        Assert.Contains(DISPLAY_PATH, exception.Message);
        return exception;
    }
}
