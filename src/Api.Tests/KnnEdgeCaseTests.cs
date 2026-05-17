using RinhaBackend;
using Xunit;

namespace Api.Tests;

public class KnnEdgeCaseTests
{
    private static float[] Vec(float v) =>
        [v, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

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

    // ── Store vazio ───────────────────────────────────────────────────────────

    [Fact]
    public void Empty_store_returns_score_zero()
    {
        // Com 0 vetores o loop não executa; topLabels fica zerado → score = 0/5 = 0
        var store = ReferenceDataStore.CreateForTesting([], [], 0);

        float score = store.ComputeFraudScore(Vec(0.5f));

        Assert.Equal(0f, score);
    }

    // ── Store com menos de K=5 vetores ────────────────────────────────────────

    [Fact]
    public void Fewer_than_k_fraud_vectors_dilutes_score()
    {
        // 3 vetores, todos fraude — os 2 slots sobrando em topLabels ficam como 0 (legit)
        // Score esperado: 3/5 = 0.6, não 1.0
        var store = BuildStore(
            (Vec(0.1f), true),
            (Vec(0.2f), true),
            (Vec(0.3f), true)
        );

        float score = store.ComputeFraudScore(Vec(0.0f));

        Assert.Equal(0.6f, score, precision: 4);
    }

    [Fact]
    public void One_fraud_vector_out_of_one_returns_0_point_2()
    {
        // 1 vetor de fraude → score = 1/5 = 0.2
        var store = BuildStore((Vec(0.0f), true));

        float score = store.ComputeFraudScore(Vec(0.0f));

        Assert.Equal(0.2f, score, precision: 4);
    }

    [Fact]
    public void Fewer_than_k_legit_vectors_returns_zero()
    {
        // 2 vetores legit → os 3 slots sobrando são 0 → score = 0/5 = 0
        var store = BuildStore(
            (Vec(0.1f), false),
            (Vec(0.2f), false)
        );

        float score = store.ComputeFraudScore(Vec(0.0f));

        Assert.Equal(0f, score);
    }

    // ── Distâncias iguais ─────────────────────────────────────────────────────

    [Fact]
    public void Vectors_at_identical_distance_are_both_considered()
    {
        // Dois vetores equidistantes da query (0.5 e -0.5 no dim0, ambos a 0.5 de 0.0)
        // O algoritmo pode incluir qualquer um — o importante é não travar
        var store = BuildStore(
            (Vec(0.5f),  true),
            (Vec(-0.5f), true),  // -0.5 no Half é preservado
            (Vec(0.9f),  false),
            (Vec(0.8f),  false),
            (Vec(0.7f),  false)
        );

        var ex = Record.Exception(() => store.ComputeFraudScore(Vec(0.0f)));
        Assert.Null(ex);
    }

    // ── Query com valores sentinela -1 ────────────────────────────────────────

    [Fact]
    public void Query_with_sentinel_minus1_at_dims_5_and_6_produces_valid_score()
    {
        var store = BuildStore(
            (Vec(0.1f), false),
            (Vec(0.2f), false),
            (Vec(0.3f), false),
            (Vec(0.4f), false),
            (Vec(0.5f), false)
        );

        float[] queryWithSentinels = [0.1f, 0f, 0f, 0f, 0f, -1f, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
        float score = store.ComputeFraudScore(queryWithSentinels);

        Assert.InRange(score, 0f, 1f);
    }
}
