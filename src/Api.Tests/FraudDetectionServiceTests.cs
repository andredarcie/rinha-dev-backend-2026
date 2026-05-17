using RinhaBackend;
using Xunit;

namespace Api.Tests;

public class FraudDetectionServiceTests
{
    private static readonly IReadOnlyDictionary<string, float> EmptyMcc =
        new Dictionary<string, float>();

    private static FraudScoreRequest MinimalRequest(string requestedAt = "2024-06-17T10:00:00Z") =>
        new(
            Id: "tx-1",
            Transaction: new TransactionInfo(100m, 1, requestedAt),
            Customer: new CustomerInfo(100m, 1, ["MERC-A"]),
            Merchant: new MerchantInfo("MERC-A", "5411", 100m),
            Terminal: new TerminalInfo(false, true, 1m),
            LastTransaction: null
        );

    [Theory]
    [InlineData(0,  0f,   true)]   // 0/5 fraud → 0.0 → approved
    [InlineData(2,  0.4f, true)]   // 2/5 fraud → 0.4 < 0.6 → approved
    [InlineData(3,  0.6f, false)]  // 3/5 fraud → 0.6 = threshold → denied
    [InlineData(4,  0.8f, false)]  // 4/5 fraud → 0.8 → denied
    [InlineData(5,  1.0f, false)]  // 5/5 fraud → 1.0 → denied
    public void Approved_reflects_threshold(int fraudNeighbors, float expectedScore, bool expectedApproved)
    {
        var store = new StubStore(fraudNeighbors / 5f);
        var service = new FraudDetectionService(store, EmptyMcc);

        var result = service.Score(MinimalRequest());

        Assert.Equal(expectedScore, result.FraudScore, precision: 4);
        Assert.Equal(expectedApproved, result.Approved);
    }

    [Fact]
    public void Score_is_between_0_and_1()
    {
        var store = new StubStore(0.6f);
        var service = new FraudDetectionService(store, EmptyMcc);

        var result = service.Score(MinimalRequest());

        Assert.InRange(result.FraudScore, 0f, 1f);
    }

    [Fact]
    public void Does_not_throw_on_null_last_transaction()
    {
        var store = new StubStore(0f);
        var service = new FraudDetectionService(store, EmptyMcc);

        var ex = Record.Exception(() => service.Score(MinimalRequest()));
        Assert.Null(ex);
    }

    [Fact]
    public void Passes_normalized_vector_to_store()
    {
        float[]? capturedQuery = null;
        var store = new CapturingStore(q => capturedQuery = q.ToArray());
        var service = new FraudDetectionService(store, EmptyMcc);

        service.Score(MinimalRequest());

        Assert.NotNull(capturedQuery);
        Assert.Equal(VectorNormalizer.Dimensions, capturedQuery!.Length);
    }

    // ── Test doubles ───────────────────────────────────────────────────────────

    private sealed class StubStore(float score) : IReferenceDataStore
    {
        public float ComputeFraudScore(Span<float> query) => score;
    }

    private sealed class CapturingStore(Action<Span<float>> capture) : IReferenceDataStore
    {
        public float ComputeFraudScore(Span<float> query)
        {
            capture(query);
            return 0f;
        }
    }
}
