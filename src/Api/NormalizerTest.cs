#if DEBUG
// Quick smoke test: run with `dotnet run --test`
// Verifies vector dimensions and known values from the spec example.
namespace RinhaBackend;

internal static class NormalizerTest
{
    internal static void Run()
    {
        var req = new FraudScoreRequest(
            Id: "tx-test",
            Transaction: new TransactionInfo(Amount: 384.88m, Installments: 3, RequestedAt: "2024-06-15T14:30:00Z"),
            Customer: new CustomerInfo(AvgAmount: 769.76m, TxCount24h: 3, KnownMerchants: ["MERC-001"]),
            Merchant: new MerchantInfo(Id: "MERC-001", Mcc: "5912", AvgAmount: 298.95m),
            Terminal: new TerminalInfo(IsOnline: false, CardPresent: true, KmFromHome: 13.7m),
            LastTransaction: new LastTransactionInfo(Timestamp: "2024-06-15T12:00:00Z", KmFromCurrent: 18.8m)
        );

        var mccRisk = new Dictionary<string, float> { ["5912"] = 0.20f };
        Span<float> v = stackalloc float[VectorNormalizer.Dimensions];
        VectorNormalizer.Normalize(req, mccRisk, v);

        Console.WriteLine("Vector dimensions: " + v.Length);
        for (int i = 0; i < v.Length; i++)
            Console.WriteLine($"  [{i:D2}] = {v[i]:F6}");

        // Assertions
        System.Diagnostics.Debug.Assert(v.Length == 14);
        System.Diagnostics.Debug.Assert(v[9] == 0f, "is_online should be 0");    // IsOnline = false
        System.Diagnostics.Debug.Assert(v[10] == 1f, "card_present should be 1"); // CardPresent = true
        System.Diagnostics.Debug.Assert(v[11] == 0f, "unknown_merchant should be 0"); // MERC-001 is known
        System.Diagnostics.Debug.Assert(Math.Abs(v[12] - 0.20f) < 0.001f, "mcc_risk should be 0.20");

        Console.WriteLine("All assertions passed.");
    }
}
#endif
