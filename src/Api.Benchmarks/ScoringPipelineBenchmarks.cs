using BenchmarkDotNet.Attributes;
using RinhaBackend;

namespace Api.Benchmarks;

/// <summary>
/// Measures the full per-request scoring pipeline: normalization + KNN search.
/// This is the critical path executed on every POST /fraud-score call.
/// Store size is fixed at 3M vectors to match the production dataset.
/// </summary>
[MemoryDiagnoser]
public class ScoringPipelineBenchmarks
{
    private const int StoreSize = 3_000_000;

    private FraudDetectionService _service = null!;
    private FraudScoreRequest _request = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        var vectors = new Half[StoreSize * VectorNormalizer.Dimensions];
        var labels  = new byte[StoreSize];

        for (int i = 0; i < StoreSize; i++)
        {
            for (int d = 0; d < VectorNormalizer.Dimensions; d++)
                vectors[i * VectorNormalizer.Dimensions + d] = (Half)rng.NextSingle();
            labels[i] = (byte)(i % 2);
        }

        var store   = ReferenceDataStore.CreateForTesting(vectors, labels, StoreSize);
        var mccRisk = new Dictionary<string, float> { ["5411"] = 0.15f };

        _service = new FraudDetectionService(store, mccRisk);

        _request = new FraudScoreRequest(
            Id: "bench-tx",
            Transaction: new TransactionInfo(384.88m, 3, "2024-06-15T14:30:00Z"),
            Customer: new CustomerInfo(769.76m, 3, ["MERC-001"]),
            Merchant: new MerchantInfo("MERC-001", "5411", 298.95m),
            Terminal: new TerminalInfo(false, true, 13.7m),
            LastTransaction: new LastTransactionInfo("2024-06-15T12:00:00Z", 18.8m)
        );
    }

    [Benchmark]
    public FraudScoreResponse Score() => _service.Score(_request);
}
