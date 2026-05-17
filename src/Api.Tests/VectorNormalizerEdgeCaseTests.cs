using RinhaBackend;
using Xunit;

namespace Api.Tests;

public class VectorNormalizerEdgeCaseTests
{
    private static readonly IReadOnlyDictionary<string, float> EmptyMcc =
        new Dictionary<string, float>();

    private static float[] Normalize(FraudScoreRequest req, IReadOnlyDictionary<string, float>? mcc = null)
    {
        var buf = new float[VectorNormalizer.Dimensions];
        VectorNormalizer.Normalize(req, mcc ?? EmptyMcc, buf);
        return buf;
    }

    private static FraudScoreRequest Base(
        string requestedAt = "2024-06-17T10:00:00Z",
        string? lastTxTimestamp = null,
        decimal kmFromCurrent = 0m,
        List<string>? knownMerchants = null,
        string merchantId = "MERC-1") =>
        new(
            Id: "tx-1",
            Transaction: new TransactionInfo(100m, 1, requestedAt),
            Customer: new CustomerInfo(100m, 1, knownMerchants ?? [merchantId]),
            Merchant: new MerchantInfo(merchantId, "9999", 100m),
            Terminal: new TerminalInfo(false, true, 0m),
            LastTransaction: lastTxTimestamp is null ? null
                : new LastTransactionInfo(lastTxTimestamp, kmFromCurrent)
        );

    // ── Dim3: hour_of_day boundaries ─────────────────────────────────────────

    [Fact]
    public void Dim3_hour_midnight_returns_zero()
    {
        // hour 0 → 0/23 = 0
        var v = Normalize(Base(requestedAt: "2024-06-17T00:00:00Z"));
        Assert.Equal(0f, v[3]);
    }

    [Fact]
    public void Dim3_hour_23_returns_one()
    {
        // hour 23 → 23/23 = 1.0
        var v = Normalize(Base(requestedAt: "2024-06-17T23:00:00Z"));
        Assert.Equal(1f, v[3], precision: 4);
    }

    // ── Dim5: negative time difference (lastTx após requestedAt) ─────────────

    [Fact]
    public void Dim5_when_last_tx_is_after_current_tx_returns_zero()
    {
        // lastTx 1h DEPOIS da transação atual (clock skew / timezone issue)
        // O código trata diferença negativa como 0f (não como sentinela -1f)
        var v = Normalize(Base(
            requestedAt: "2024-06-17T10:00:00Z",
            lastTxTimestamp: "2024-06-17T11:00:00Z")); // 1h no futuro

        Assert.Equal(0f, v[5]);
    }

    [Fact]
    public void Dim5_when_last_tx_is_after_current_tx_does_not_return_sentinel()
    {
        var v = Normalize(Base(
            requestedAt: "2024-06-17T10:00:00Z",
            lastTxTimestamp: "2024-06-17T11:00:00Z"));

        // Diferença negativa NÃO deve produzir o sentinela -1
        Assert.NotEqual(-1f, v[5]);
    }

    // ── Timestamps com fuso horário não-UTC ───────────────────────────────────

    [Fact]
    public void Dim3_non_utc_timestamp_is_converted_to_utc_for_hour()
    {
        // "14:00-03:00" = 17:00 UTC → hour = 17
        var v = Normalize(Base(requestedAt: "2024-06-17T14:00:00-03:00"));
        Assert.Equal(17f / 23f, v[3], precision: 4);
    }

    [Fact]
    public void Dim4_non_utc_timestamp_is_converted_to_utc_for_day_of_week()
    {
        // Domingo 23:00 em UTC-3 = Segunda-feira 02:00 UTC (Monday=0 → 0/6=0)
        var v = Normalize(Base(requestedAt: "2024-06-16T23:00:00-03:00")); // Sun 23h BRT = Mon 02h UTC
        Assert.Equal(0f, v[4]); // Monday = 0/6 = 0
    }

    [Fact]
    public void Dim5_non_utc_last_tx_is_correctly_converted()
    {
        // requestedAt = 14:00 UTC; lastTx = 12:00 BRT (= 15:00 UTC) → futura → 0f
        var v = Normalize(Base(
            requestedAt: "2024-06-17T14:00:00Z",
            lastTxTimestamp: "2024-06-17T12:00:00-03:00")); // = 15:00 UTC, 1h depois

        Assert.Equal(0f, v[5]); // futuro → 0
    }

    // ── Dim11: lista de merchants conhecidos vazia ────────────────────────────

    [Fact]
    public void Dim11_empty_known_merchants_list_marks_merchant_as_unknown()
    {
        var v = Normalize(Base(merchantId: "MERC-X", knownMerchants: []));
        Assert.Equal(1f, v[11]);
    }

    // ── Dim12: mcc_risk value passado diretamente como float ─────────────────

    [Fact]
    public void Dim12_mcc_risk_high_value_preserved()
    {
        var mcc = new Dictionary<string, float> { ["7995"] = 0.85f };
        var req = Base();
        var reqWithHighRisk = new FraudScoreRequest(
            req.Id, req.Transaction, req.Customer,
            new MerchantInfo(req.Merchant.Id, "7995", req.Merchant.AvgAmount),
            req.Terminal, req.LastTransaction);

        var v = Normalize(reqWithHighRisk, mcc);
        Assert.Equal(0.85f, v[12], precision: 4);
    }
}
