using RinhaBackend;
using Xunit;

namespace Api.Tests;

// Tests the KNN search logic using a small in-memory ReferenceDataStore
// built via the internal factory for testing.
public class KnnSearchTests
{
    // Builds an in-memory store with hand-crafted vectors and labels.
    // Each vector is 14D; we control only the first dimension for simplicity.
    private static IReferenceDataStore BuildStore(params (float[] vector, bool fraud)[] entries)
    {
        int n = entries.Length;
        var vectors = new Half[n * VectorNormalizer.Dimensions];
        var labels = new byte[n];

        for (int i = 0; i < n; i++)
        {
            var vec = entries[i].vector;
            int baseIdx = i * VectorNormalizer.Dimensions;
            for (int d = 0; d < VectorNormalizer.Dimensions; d++)
                vectors[baseIdx + d] = d < vec.Length ? (Half)vec[d] : (Half)0f;
            labels[i] = entries[i].fraud ? (byte)1 : (byte)0;
        }

        return ReferenceDataStore.CreateForTesting(vectors, labels, n);
    }

    private static float[] Vec(float v0) =>
        [v0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

    [Fact]
    public void All_legit_neighbors_returns_score_zero()
    {
        var store = BuildStore(
            (Vec(0.1f), false),
            (Vec(0.2f), false),
            (Vec(0.3f), false),
            (Vec(0.4f), false),
            (Vec(0.5f), false)
        );

        float score = store.ComputeFraudScore(Vec(0.1f));

        Assert.Equal(0f, score);
    }

    [Fact]
    public void All_fraud_neighbors_returns_score_one()
    {
        var store = BuildStore(
            (Vec(0.1f), true),
            (Vec(0.2f), true),
            (Vec(0.3f), true),
            (Vec(0.4f), true),
            (Vec(0.5f), true)
        );

        float score = store.ComputeFraudScore(Vec(0.1f));

        Assert.Equal(1f, score);
    }

    [Fact]
    public void Picks_5_nearest_not_5_furthest()
    {
        // First 5 are very close and all legit; last 5 are far and all fraud.
        // KNN should pick the close legit ones → score = 0.
        var store = BuildStore(
            (Vec(0.01f), false),
            (Vec(0.02f), false),
            (Vec(0.03f), false),
            (Vec(0.04f), false),
            (Vec(0.05f), false),
            (Vec(0.90f), true),
            (Vec(0.91f), true),
            (Vec(0.92f), true),
            (Vec(0.93f), true),
            (Vec(0.94f), true)
        );

        float score = store.ComputeFraudScore(Vec(0.0f));

        Assert.Equal(0f, score);
    }

    [Fact]
    public void Mixed_neighbors_returns_correct_ratio()
    {
        // 3 fraud + 2 legit among the 5 nearest → score = 3/5 = 0.6
        var store = BuildStore(
            (Vec(0.1f), true),
            (Vec(0.2f), true),
            (Vec(0.3f), true),
            (Vec(0.4f), false),
            (Vec(0.5f), false)
        );

        float score = store.ComputeFraudScore(Vec(0.0f));

        Assert.Equal(0.6f, score, precision: 4);
    }

    [Fact]
    public void Handles_sentinel_minus1_in_query()
    {
        // Sentinel values (-1) at dims 5 and 6 must not crash the search.
        var store = BuildStore(
            (Vec(0.0f), false),
            (Vec(0.1f), false),
            (Vec(0.2f), false),
            (Vec(0.3f), false),
            (Vec(0.4f), false)
        );

        float[] queryWithSentinels = [0.0f, 0f, 0f, 0f, 0f, -1f, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

        var ex = Record.Exception(() => store.ComputeFraudScore(queryWithSentinels));
        Assert.Null(ex);
    }
}
