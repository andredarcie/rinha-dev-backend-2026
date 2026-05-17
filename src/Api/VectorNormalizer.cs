namespace RinhaBackend;

/// <summary>
/// Converts a <see cref="FraudScoreRequest"/> into a normalized 14-dimensional float vector in [0, 1].
/// Each dimension captures a distinct behavioral or contextual signal used by the KNN classifier.
/// </summary>
public static class VectorNormalizer
{
    /// <summary>Number of features in each normalized transaction vector.</summary>
    public const int Dimensions = 14;

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    /// <summary>
    /// Fills <paramref name="output"/> with the normalized feature vector for <paramref name="req"/>.
    /// The caller must supply a buffer of at least <see cref="Dimensions"/> elements.
    /// </summary>
    /// <param name="req">Incoming transaction request.</param>
    /// <param name="mccRisk">MCC → risk coefficient lookup (0–1). Falls back to 0.5 for unknown MCCs.</param>
    /// <param name="output">Destination span; written in place.</param>
    public static void Normalize(FraudScoreRequest req, IReadOnlyDictionary<string, float> mccRisk, Span<float> output)
    {
        var tx = req.Transaction;
        var customer = req.Customer;
        var merchant = req.Merchant;
        var terminal = req.Terminal;
        var lastTx = req.LastTransaction;

        var requestedAt = DateTimeOffset.Parse(tx.RequestedAt).UtcDateTime;

        float minutesSinceLastTx = -1f;
        float kmFromLastTx = -1f;

        if (lastTx is not null)
        {
            var lastTxTime = DateTimeOffset.Parse(lastTx.Timestamp).UtcDateTime;
            double minutes = (requestedAt - lastTxTime).TotalMinutes;
            minutesSinceLastTx = minutes >= 0 ? Clamp01((float)minutes / 1440f) : 0f;
            kmFromLastTx = Clamp01((float)lastTx.KmFromCurrent / 1000f);
        }

        float unknownMerchant = customer.KnownMerchants.Contains(merchant.Id) ? 0f : 1f;
        float mccRiskValue = mccRisk.TryGetValue(merchant.Mcc, out float risk) ? risk : 0.5f;

        // day_of_week: Monday=0, Sunday=6
        int csharpDow = (int)requestedAt.DayOfWeek; // Sunday=0, Saturday=6
        int mondayBasedDow = csharpDow == 0 ? 6 : csharpDow - 1;

        output[0]  = Clamp01((float)tx.Amount / 10000f);
        output[1]  = Clamp01(tx.Installments / 12f);
        output[2]  = customer.AvgAmount > 0 ? Clamp01((float)tx.Amount / (float)customer.AvgAmount / 10f) : 0f;
        output[3]  = requestedAt.Hour / 23f;
        output[4]  = mondayBasedDow / 6f;
        output[5]  = minutesSinceLastTx;
        output[6]  = kmFromLastTx;
        output[7]  = Clamp01((float)terminal.KmFromHome / 1000f);
        output[8]  = Clamp01(customer.TxCount24h / 20f);
        output[9]  = terminal.IsOnline ? 1f : 0f;
        output[10] = terminal.CardPresent ? 1f : 0f;
        output[11] = unknownMerchant;
        output[12] = mccRiskValue;
        output[13] = Clamp01((float)merchant.AvgAmount / 10000f);
    }
}
