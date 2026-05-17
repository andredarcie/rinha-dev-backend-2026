using System.IO.Compression;
using System.Text.Json;
using RinhaBackend;
using Xunit;

namespace Api.Tests;

public class ReferenceDataStoreLoadEdgeCaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rinha-edge-{Guid.NewGuid()}");

    public ReferenceDataStoreLoadEdgeCaseTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task WriteJsonGzAsync(object payload)
    {
        var path = Path.Combine(_dir, "references.json.gz");
        await using var fs = File.Create(path);
        await using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        await JsonSerializer.SerializeAsync(gz, payload);
    }

    // ── JSON vazio (array []) ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_from_empty_json_array_creates_empty_store()
    {
        await WriteJsonGzAsync(Array.Empty<object>());

        var store = await ReferenceDataStore.LoadAsync(_dir);

        // Store vazio → nenhum vizinho → score = 0
        float score = store.ComputeFraudScore(
            [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);
        Assert.Equal(0f, score);
    }

    // ── Label desconhecido → tratado como legit ───────────────────────────────

    [Fact]
    public async Task LoadAsync_unknown_label_is_treated_as_legit()
    {
        // Labels "suspicious", "unknown", "maybe" são desconhecidos
        // O código usa `label == "fraud"`, então qualquer outro valor = legit
        await WriteJsonGzAsync(new[]
        {
            new { vector = new float[14], label = "suspicious" },
            new { vector = new float[14], label = "unknown"    },
            new { vector = new float[14], label = "legit"      },
            new { vector = new float[14], label = "legit"      },
            new { vector = new float[14], label = "legit"      },
        });

        var store = await ReferenceDataStore.LoadAsync(_dir);

        // Nenhum dos 5 é "fraud" → score = 0
        float score = store.ComputeFraudScore(new float[14]);
        Assert.Equal(0f, score);
    }

    [Fact]
    public async Task LoadAsync_fraud_label_is_case_sensitive()
    {
        // "Fraud" com F maiúsculo NÃO é reconhecido como fraude
        await WriteJsonGzAsync(new[]
        {
            new { vector = new float[14], label = "Fraud" },
            new { vector = new float[14], label = "FRAUD" },
            new { vector = new float[14], label = "legit" },
            new { vector = new float[14], label = "legit" },
            new { vector = new float[14], label = "legit" },
        });

        var store = await ReferenceDataStore.LoadAsync(_dir);

        float score = store.ComputeFraudScore(new float[14]);
        Assert.Equal(0f, score); // "Fraud"/"FRAUD" tratados como legit
    }

    // ── Vetor com sentinela -1 no JSON é preservado ───────────────────────────

    [Fact]
    public async Task LoadAsync_preserves_sentinel_minus1_in_vectors()
    {
        var vecWithSentinel = new float[14];
        vecWithSentinel[5] = -1f;
        vecWithSentinel[6] = -1f;

        await WriteJsonGzAsync(new[]
        {
            new { vector = vecWithSentinel, label = "legit" },
            new { vector = new float[14],   label = "legit" },
            new { vector = new float[14],   label = "legit" },
            new { vector = new float[14],   label = "legit" },
            new { vector = new float[14],   label = "legit" },
        });

        var store = await ReferenceDataStore.LoadAsync(_dir);

        // Query com sentinelas deve funcionar sem exceção
        var query = new float[14];
        query[5] = -1f;
        query[6] = -1f;

        var ex = Record.Exception(() => store.ComputeFraudScore(query));
        Assert.Null(ex);
    }
}
