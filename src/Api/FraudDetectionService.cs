using System.Runtime.InteropServices;

namespace RinhaBackend;

/// <summary>
/// Orchestrates fraud scoring for a single transaction request.
/// Normalizes the request into a feature vector, runs KNN search via <see cref="IReferenceDataStore"/>,
/// and applies the approval threshold to produce a <see cref="FraudScoreResponse"/>.
/// </summary>
public sealed class FraudDetectionService
{
    private const float ApprovalThreshold = 0.6f;

    private readonly IReferenceDataStore _store;
    private readonly IReadOnlyDictionary<string, float> _mccRisk;

    // Thread-local buffer to avoid heap allocations per request
    [ThreadStatic]
    private static float[]? _queryBuffer;

    public FraudDetectionService(IReferenceDataStore store, IReadOnlyDictionary<string, float> mccRisk)
    {
        _store = store;
        _mccRisk = mccRisk;
    }

    /// <summary>
    /// Scores a transaction and returns the fraud score along with the approval decision.
    /// </summary>
    public FraudScoreResponse Score(FraudScoreRequest request)
    {
        _queryBuffer ??= new float[VectorNormalizer.Dimensions];
        Span<float> query = _queryBuffer;

        VectorNormalizer.Normalize(request, _mccRisk, query);

        float fraudScore = _store.ComputeFraudScore(query);
        bool approved = fraudScore < ApprovalThreshold;

        return new FraudScoreResponse(approved, fraudScore);
    }
}
