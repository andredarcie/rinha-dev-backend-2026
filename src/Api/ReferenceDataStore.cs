using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RinhaBackend;

/// <summary>
/// Abstraction over the KNN reference store, used to allow test doubles without loading production data.
/// </summary>
public interface IReferenceDataStore
{
    /// <summary>
    /// Computes the fraud score for a normalized feature vector by running a KNN search.
    /// Returns a value in [0, 1]: the fraction of the K nearest neighbors labeled as fraud.
    /// </summary>
    float ComputeFraudScore(Span<float> query);
}

/// <summary>
/// Immutable in-memory KNN store that holds ~3 million pre-indexed reference transactions.
/// Vectors are stored as <see cref="Half"/> (float16) for memory efficiency (~87 MB per instance).
/// On first load the source <c>references.json.gz</c> is converted to a compact binary cache
/// (<c>references.bin</c>) so subsequent startups skip JSON parsing entirely.
/// </summary>
/// <remarks>
/// Internal layout: <c>_vectors[i * Dim .. i * Dim + Dim - 1]</c> = 14-dimensional vector for entry i.
/// <c>_labels[i]</c> = 1 (fraud) or 0 (legit).
/// </remarks>
public sealed class ReferenceDataStore : IReferenceDataStore
{
    private const int Dim = VectorNormalizer.Dimensions;
    private const int K = 5;
    private const string BinMagic = "RB26";
    private const int BinVersion = 1;

    private readonly Half[] _vectors;
    private readonly byte[] _labels;
    private readonly int _count;

    private ReferenceDataStore(Half[] vectors, byte[] labels, int count)
    {
        _vectors = vectors;
        _labels = labels;
        _count = count;
    }

    // For unit testing only
    internal static ReferenceDataStore CreateForTesting(Half[] vectors, byte[] labels, int count) =>
        new(vectors, labels, count);

    /// <summary>
    /// Loads the reference store from <paramref name="dataDir"/>.
    /// Prefers the binary cache (<c>references.bin</c>); falls back to <c>references.json.gz</c>
    /// and writes the cache on first use.
    /// </summary>
    public static async Task<ReferenceDataStore> LoadAsync(string dataDir, CancellationToken ct = default)
    {
        var binPath = Path.Combine(dataDir, "references.bin");
        var jsonGzPath = Path.Combine(dataDir, "references.json.gz");

        if (File.Exists(binPath))
        {
            Console.WriteLine("Loading binary reference data...");
            return await LoadBinaryAsync(binPath, ct);
        }

        if (!File.Exists(jsonGzPath))
            throw new FileNotFoundException($"Reference data not found in {dataDir}");

        Console.WriteLine("Loading and converting reference data from JSON (first run)...");
        var store = await LoadJsonGzAsync(jsonGzPath, ct);
        await store.SaveBinaryAsync(binPath, ct);
        Console.WriteLine("Binary cache saved.");
        return store;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Performs a brute-force K=5 nearest-neighbor search using squared Euclidean distance.
    /// Uses stack-allocated buffers and unsafe ref arithmetic to stay off the heap on the hot path.
    /// </remarks>
    public float ComputeFraudScore(Span<float> query)
    {
        Span<float> topDists = stackalloc float[K];
        Span<byte> topLabels = stackalloc byte[K];
        topDists.Fill(float.MaxValue);

        float worstBest = float.MaxValue;
        int count = _count;
        ref Half vectorsRef = ref MemoryMarshal.GetArrayDataReference(_vectors);
        ref byte labelsRef = ref MemoryMarshal.GetArrayDataReference(_labels);

        for (int i = 0; i < count; i++)
        {
            float dist = SquaredDistance(ref vectorsRef, i * Dim, query);

            if (dist < worstBest)
            {
                int worst = 0;
                for (int j = 1; j < K; j++)
                    if (topDists[j] > topDists[worst]) worst = j;

                topDists[worst] = dist;
                topLabels[worst] = Unsafe.Add(ref labelsRef, i);

                worstBest = topDists[0];
                for (int j = 1; j < K; j++)
                    if (topDists[j] > worstBest) worstBest = topDists[j];
            }
        }

        int fraudCount = 0;
        for (int j = 0; j < K; j++)
            fraudCount += topLabels[j];

        return fraudCount / (float)K;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SquaredDistance(ref Half vectorsRef, int baseIdx, Span<float> query)
    {
        float sum = 0f;
        for (int d = 0; d < Dim; d++)
        {
            float diff = query[d] - (float)Unsafe.Add(ref vectorsRef, baseIdx + d);
            sum += diff * diff;
        }
        return sum;
    }

    private static async Task<ReferenceDataStore> LoadJsonGzAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 17);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = await JsonSerializer.DeserializeAsync<List<ReferenceEntry>>(gz, options, ct)
            ?? throw new InvalidDataException("Failed to deserialize references.json.gz");

        int count = entries.Count;
        var vectors = new Half[count * Dim];
        var labels = new byte[count];

        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];
            var vec = entry.Vector;
            int baseIdx = i * Dim;
            for (int d = 0; d < Dim; d++)
                vectors[baseIdx + d] = (Half)vec[d];
            labels[i] = entry.Label == "fraud" ? (byte)1 : (byte)0;
        }

        return new ReferenceDataStore(vectors, labels, count);
    }

    private static async Task<ReferenceDataStore> LoadBinaryAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 17);
        using var reader = new BinaryReader(fs);

        string magic = new string(reader.ReadChars(4));
        if (magic != BinMagic) throw new InvalidDataException("Invalid binary cache format");
        int version = reader.ReadInt32();
        if (version != BinVersion) throw new InvalidDataException($"Unsupported binary cache version {version}");

        int count = reader.ReadInt32();
        var vectors = new Half[count * Dim];
        var labels = new byte[count];

        byte[] rawVectors = reader.ReadBytes(count * Dim * 2);
        MemoryMarshal.Cast<byte, Half>(rawVectors).CopyTo(vectors);

        int read = 0;
        while (read < count)
            read += reader.Read(labels, read, count - read);

        return new ReferenceDataStore(vectors, labels, count);
    }

    private async Task SaveBinaryAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 17);
        using var writer = new BinaryWriter(fs);

        writer.Write(BinMagic.ToCharArray());
        writer.Write(BinVersion);
        writer.Write(_count);

        var rawVectors = MemoryMarshal.Cast<Half, byte>(_vectors.AsSpan(0, _count * Dim));
        writer.Write(rawVectors);
        writer.Write(_labels, 0, _count);
    }

    private sealed class ReferenceEntry
    {
        public float[] Vector { get; set; } = [];
        public string Label { get; set; } = "legit";
    }
}
