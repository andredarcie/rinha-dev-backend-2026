using System.IO.Compression;
using System.Text.Json;
using RinhaBackend;
using Xunit;

namespace Api.Tests;

public class ReferenceDataStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"rinha-test-{Guid.NewGuid()}");

    public ReferenceDataStoreTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static float[] Vec(float v) =>
        [v, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

    private async Task<string> WriteJsonGzAsync(params (float[] vector, bool fraud)[] entries)
    {
        var path = Path.Combine(_tempDir, "references.json.gz");
        var payload = entries.Select(e => new
        {
            vector = e.vector,
            label = e.fraud ? "fraud" : "legit"
        });

        await using var fs = File.Create(path);
        await using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        await JsonSerializer.SerializeAsync(gz, payload);
        return path;
    }

    private string BinPath => Path.Combine(_tempDir, "references.bin");

    // ── LoadAsync — arquivo não encontrado ────────────────────────────────────

    [Fact]
    public async Task LoadAsync_throws_when_no_files_exist()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => ReferenceDataStore.LoadAsync(_tempDir));
    }

    // ── LoadAsync via JSON gzip ───────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_from_json_gz_returns_correct_count_and_labels()
    {
        await WriteJsonGzAsync(
            (Vec(0.1f), false),
            (Vec(0.5f), true),
            (Vec(0.9f), false));

        var store = await ReferenceDataStore.LoadAsync(_tempDir);

        // Score 0.0: os 3 vizinhos mais próximos de 0.1 são os 3 presentes; 1 deles é fraude
        float score = store.ComputeFraudScore(Vec(0.1f));
        Assert.True(score > 0f && score <= 1f);
    }

    [Fact]
    public async Task LoadAsync_from_json_gz_creates_binary_cache()
    {
        await WriteJsonGzAsync((Vec(0.0f), false));

        await ReferenceDataStore.LoadAsync(_tempDir);

        Assert.True(File.Exists(BinPath), "binary cache deve ser criado");
    }

    [Fact]
    public async Task LoadAsync_all_legit_json_gz_returns_score_zero()
    {
        await WriteJsonGzAsync(
            (Vec(0.1f), false),
            (Vec(0.2f), false),
            (Vec(0.3f), false),
            (Vec(0.4f), false),
            (Vec(0.5f), false));

        var store = await ReferenceDataStore.LoadAsync(_tempDir);

        Assert.Equal(0f, store.ComputeFraudScore(Vec(0.1f)));
    }

    [Fact]
    public async Task LoadAsync_all_fraud_json_gz_returns_score_one()
    {
        await WriteJsonGzAsync(
            (Vec(0.1f), true),
            (Vec(0.2f), true),
            (Vec(0.3f), true),
            (Vec(0.4f), true),
            (Vec(0.5f), true));

        var store = await ReferenceDataStore.LoadAsync(_tempDir);

        Assert.Equal(1f, store.ComputeFraudScore(Vec(0.0f)));
    }

    // ── LoadAsync via binary cache ────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_loads_from_binary_when_bin_exists()
    {
        // Primeira carga: JSON → gera o .bin
        await WriteJsonGzAsync(
            (Vec(0.0f), false),
            (Vec(0.2f), false),
            (Vec(0.4f), false),
            (Vec(0.6f), false),
            (Vec(0.8f), false));
        await ReferenceDataStore.LoadAsync(_tempDir);
        Assert.True(File.Exists(BinPath));

        // Segunda carga: deve ler do .bin
        var store = await ReferenceDataStore.LoadAsync(_tempDir);
        Assert.Equal(0f, store.ComputeFraudScore(Vec(0.0f)));
    }

    [Fact]
    public async Task LoadAsync_binary_and_json_produce_same_scores()
    {
        await WriteJsonGzAsync(
            (Vec(0.1f), false),
            (Vec(0.3f), true),
            (Vec(0.5f), false),
            (Vec(0.7f), true),
            (Vec(0.9f), false));

        var storeFromJson = await ReferenceDataStore.LoadAsync(_tempDir);
        float scoreFromJson = storeFromJson.ComputeFraudScore(Vec(0.2f));

        // Deleta o json.gz para forçar leitura do .bin
        File.Delete(Path.Combine(_tempDir, "references.json.gz"));

        var storeFromBin = await ReferenceDataStore.LoadAsync(_tempDir);
        float scoreFromBin = storeFromBin.ComputeFraudScore(Vec(0.2f));

        Assert.Equal(scoreFromJson, scoreFromBin);
    }

    // ── LoadBinaryAsync — formato inválido ────────────────────────────────────

    [Fact]
    public async Task LoadAsync_throws_on_invalid_binary_magic()
    {
        // Escreve um .bin com magic errado
        await File.WriteAllBytesAsync(BinPath,
            [0x00, 0x00, 0x00, 0x00,   // magic errado
             0x01, 0x00, 0x00, 0x00,   // version 1
             0x00, 0x00, 0x00, 0x00]); // count 0

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReferenceDataStore.LoadAsync(_tempDir));
    }

    [Fact]
    public async Task LoadAsync_throws_on_invalid_binary_version()
    {
        // Magic correto "RB26" + version errada
        var bytes = new byte[12];
        System.Text.Encoding.ASCII.GetBytes("RB26").CopyTo(bytes, 0);
        BitConverter.GetBytes(999).CopyTo(bytes, 4);  // versão inválida
        BitConverter.GetBytes(0).CopyTo(bytes, 8);    // count 0
        await File.WriteAllBytesAsync(BinPath, bytes);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ReferenceDataStore.LoadAsync(_tempDir));
    }
}
