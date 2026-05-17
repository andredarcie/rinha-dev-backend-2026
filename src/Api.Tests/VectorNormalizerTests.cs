using RinhaBackend;
using Xunit;

namespace Api.Tests;

public class VectorNormalizerTests
{
    private static readonly IReadOnlyDictionary<string, float> MccRisk = new Dictionary<string, float>
    {
        ["5912"] = 0.20f,
        ["7995"] = 0.85f,
    };

    private static FraudScoreRequest BuildRequest(
        decimal amount = 500m,
        int installments = 1,
        decimal avgAmount = 500m,
        string requestedAt = "2024-06-17T14:00:00Z",  // Monday, 14h UTC
        int txCount24h = 5,
        string merchantId = "MERC-001",
        string mcc = "5912",
        decimal merchantAvgAmount = 300m,
        bool isOnline = false,
        bool cardPresent = true,
        decimal kmFromHome = 10m,
        string? lastTxTimestamp = "2024-06-17T13:00:00Z",  // 60 min before
        decimal kmFromCurrent = 5m,
        List<string>? knownMerchants = null)
    {
        return new FraudScoreRequest(
            Id: "tx-test",
            Transaction: new TransactionInfo(amount, installments, requestedAt),
            Customer: new CustomerInfo(avgAmount, txCount24h, knownMerchants ?? [merchantId]),
            Merchant: new MerchantInfo(merchantId, mcc, merchantAvgAmount),
            Terminal: new TerminalInfo(isOnline, cardPresent, kmFromHome),
            LastTransaction: lastTxTimestamp is null ? null
                : new LastTransactionInfo(lastTxTimestamp, kmFromCurrent)
        );
    }

    private static Span<float> Normalize(FraudScoreRequest req)
    {
        var buf = new float[VectorNormalizer.Dimensions];
        VectorNormalizer.Normalize(req, MccRisk, buf);
        return buf;
    }

    [Fact]
    public void Output_has_14_dimensions()
    {
        var v = Normalize(BuildRequest());
        Assert.Equal(14, v.Length);
    }

    [Theory]
    [InlineData(0,      0f)]      // amount=0
    [InlineData(5000,   0.5f)]    // amount=5000
    [InlineData(10000,  1f)]      // amount=10000 (max)
    [InlineData(20000,  1f)]      // amount=20000 → clamped to 1
    public void Dim0_amount_normalized(decimal amount, float expected)
    {
        var v = Normalize(BuildRequest(amount: amount));
        Assert.Equal(expected, v[0], precision: 4);
    }

    [Theory]
    [InlineData(1,  1f / 12f)]
    [InlineData(6,  0.5f)]
    [InlineData(12, 1f)]
    [InlineData(24, 1f)]   // clamped
    public void Dim1_installments_normalized(int installments, float expected)
    {
        var v = Normalize(BuildRequest(installments: installments));
        Assert.Equal(expected, v[1], precision: 4);
    }

    [Fact]
    public void Dim2_amount_vs_avg_ratio()
    {
        // amount=1000, avgAmount=500 → ratio = 1000/500/10 = 0.2
        var v = Normalize(BuildRequest(amount: 1000m, avgAmount: 500m));
        Assert.Equal(0.2f, v[2], precision: 4);
    }

    [Fact]
    public void Dim2_amount_vs_avg_zero_avg_returns_zero()
    {
        var v = Normalize(BuildRequest(avgAmount: 0m));
        Assert.Equal(0f, v[2]);
    }

    [Fact]
    public void Dim3_hour_of_day()
    {
        // 14h UTC → 14/23
        var v = Normalize(BuildRequest(requestedAt: "2024-06-17T14:00:00Z"));
        Assert.Equal(14f / 23f, v[3], precision: 4);
    }

    [Theory]
    [InlineData("2024-06-17T00:00:00Z", 0f)]         // Monday → 0/6
    [InlineData("2024-06-18T00:00:00Z", 1f / 6f)]    // Tuesday → 1/6
    [InlineData("2024-06-22T00:00:00Z", 5f / 6f)]    // Saturday → 5/6
    [InlineData("2024-06-23T00:00:00Z", 1f)]          // Sunday → 6/6
    public void Dim4_day_of_week(string requestedAt, float expected)
    {
        var v = Normalize(BuildRequest(requestedAt: requestedAt));
        Assert.Equal(expected, v[4], precision: 4);
    }

    [Fact]
    public void Dim5_minutes_since_last_tx_null_returns_sentinel()
    {
        var v = Normalize(BuildRequest(lastTxTimestamp: null));
        Assert.Equal(-1f, v[5]);
    }

    [Fact]
    public void Dim5_minutes_since_last_tx_60_minutes()
    {
        // 60 min / 1440 = 0.0417
        var v = Normalize(BuildRequest(
            requestedAt: "2024-06-17T14:00:00Z",
            lastTxTimestamp: "2024-06-17T13:00:00Z"));
        Assert.Equal(60f / 1440f, v[5], precision: 4);
    }

    [Fact]
    public void Dim5_minutes_clamped_at_1440()
    {
        var v = Normalize(BuildRequest(
            requestedAt: "2024-06-17T14:00:00Z",
            lastTxTimestamp: "2024-06-16T00:00:00Z")); // >1440 min
        Assert.Equal(1f, v[5]);
    }

    [Fact]
    public void Dim6_km_from_last_tx_null_returns_sentinel()
    {
        var v = Normalize(BuildRequest(lastTxTimestamp: null));
        Assert.Equal(-1f, v[6]);
    }

    [Fact]
    public void Dim6_km_from_last_tx_normalized()
    {
        // 500km / 1000 = 0.5
        var v = Normalize(BuildRequest(kmFromCurrent: 500m));
        Assert.Equal(0.5f, v[6], precision: 4);
    }

    [Fact]
    public void Dim7_km_from_home_normalized()
    {
        // 200km / 1000 = 0.2
        var v = Normalize(BuildRequest(kmFromHome: 200m));
        Assert.Equal(0.2f, v[7], precision: 4);
    }

    [Fact]
    public void Dim8_tx_count_24h_normalized()
    {
        // 10 / 20 = 0.5
        var v = Normalize(BuildRequest(txCount24h: 10));
        Assert.Equal(0.5f, v[8], precision: 4);
    }

    [Theory]
    [InlineData(true,  1f)]
    [InlineData(false, 0f)]
    public void Dim9_is_online(bool isOnline, float expected)
    {
        var v = Normalize(BuildRequest(isOnline: isOnline));
        Assert.Equal(expected, v[9]);
    }

    [Theory]
    [InlineData(true,  1f)]
    [InlineData(false, 0f)]
    public void Dim10_card_present(bool cardPresent, float expected)
    {
        var v = Normalize(BuildRequest(cardPresent: cardPresent));
        Assert.Equal(expected, v[10]);
    }

    [Fact]
    public void Dim11_unknown_merchant_is_zero_when_known()
    {
        var v = Normalize(BuildRequest(merchantId: "MERC-001", knownMerchants: ["MERC-001"]));
        Assert.Equal(0f, v[11]);
    }

    [Fact]
    public void Dim11_unknown_merchant_is_one_when_unknown()
    {
        var v = Normalize(BuildRequest(merchantId: "MERC-999", knownMerchants: ["MERC-001"]));
        Assert.Equal(1f, v[11]);
    }

    [Fact]
    public void Dim12_mcc_risk_from_table()
    {
        var v = Normalize(BuildRequest(mcc: "5912"));
        Assert.Equal(0.20f, v[12], precision: 4);
    }

    [Fact]
    public void Dim12_mcc_risk_default_when_not_found()
    {
        var v = Normalize(BuildRequest(mcc: "9999"));
        Assert.Equal(0.5f, v[12]);
    }

    [Fact]
    public void Dim13_merchant_avg_amount_normalized()
    {
        // 2500 / 10000 = 0.25
        var v = Normalize(BuildRequest(merchantAvgAmount: 2500m));
        Assert.Equal(0.25f, v[13], precision: 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void All_non_sentinel_dimensions_are_in_range_0_to_1(int dim)
    {
        var v = Normalize(BuildRequest());
        Assert.InRange(v[dim], 0f, 1f);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Sentinel_dimensions_are_minus1_when_no_last_tx(int dim)
    {
        var v = Normalize(BuildRequest(lastTxTimestamp: null));
        Assert.Equal(-1f, v[dim]);
    }
}
