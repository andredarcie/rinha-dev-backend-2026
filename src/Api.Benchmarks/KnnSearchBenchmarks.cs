using BenchmarkDotNet.Attributes;
using RinhaBackend;

namespace Api.Benchmarks;

/// <summary>
/// Measures KNN search performance at different store sizes.
/// Shows how latency scales with the number of reference vectors — the dominant cost at runtime.
/// Production stores ~3M vectors; the 3M param reflects the real-world scenario.
/// </summary>
[MemoryDiagnoser]
public class KnnSearchBenchmarks
{
    private ReferenceDataStore _store = null!;
    private float[] _query = null!;

    [Params(10_000, 100_000, 1_000_000, 3_000_000)]
    public int VectorCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        var vectors = new Half[VectorCount * VectorNormalizer.Dimensions];
        var labels  = new byte[VectorCount];

        for (int i = 0; i < VectorCount; i++)
        {
            for (int d = 0; d < VectorNormalizer.Dimensions; d++)
                vectors[i * VectorNormalizer.Dimensions + d] = (Half)rng.NextSingle();
            labels[i] = (byte)(i % 2); // alternating legit/fraud
        }

        _store = ReferenceDataStore.CreateForTesting(vectors, labels, VectorCount);

        _query = new float[VectorNormalizer.Dimensions];
        for (int d = 0; d < VectorNormalizer.Dimensions; d++)
            _query[d] = rng.NextSingle();
    }

    [Benchmark]
    public float ComputeFraudScore() => _store.ComputeFraudScore(_query);
}
