using BenchmarkDotNet.Attributes;
using RinhaBackend;

namespace Api.Benchmarks;

/// <summary>
/// Measures the cost of converting a raw request into a normalized 14-dimensional feature vector.
/// Covers timestamp parsing, arithmetic normalization, and dictionary lookup for MCC risk.
/// </summary>
[MemoryDiagnoser]
[DisassemblyDiagnoser]
public class VectorNormalizerBenchmarks
{
    private FraudScoreRequest _request = null!;
    private FraudScoreRequest _requestNoLastTx = null!;
    private IReadOnlyDictionary<string, float> _mccRisk = null!;
    private float[] _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mccRisk = new Dictionary<string, float> { ["5411"] = 0.15f, ["5812"] = 0.30f };
        _buffer  = new float[VectorNormalizer.Dimensions];

        _request = new FraudScoreRequest(
            Id: "bench-tx",
            Transaction: new TransactionInfo(384.88m, 3, "2024-06-15T14:30:00Z"),
            Customer: new CustomerInfo(769.76m, 3, ["MERC-001"]),
            Merchant: new MerchantInfo("MERC-001", "5411", 298.95m),
            Terminal: new TerminalInfo(false, true, 13.7m),
            LastTransaction: new LastTransactionInfo("2024-06-15T12:00:00Z", 18.8m)
        );

        _requestNoLastTx = _request with { LastTransaction = null };
    }

    [Benchmark(Baseline = true)]
    public void WithLastTransaction()
        => VectorNormalizer.Normalize(_request, _mccRisk, _buffer);

    [Benchmark]
    public void WithoutLastTransaction()
        => VectorNormalizer.Normalize(_requestNoLastTx, _mccRisk, _buffer);
}
