using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.IntegrationTests;

// Shared across all integration test classes via IntegrationCollection.
// Reference data: 5 legit vectors at [0,...,0] and 5 fraud vectors at [1,...,1].
// This makes KNN results deterministic based on how close a query is to each pole.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _dataPath = string.Empty;

    async Task IAsyncLifetime.InitializeAsync()
    {
        _dataPath = Path.Combine(Path.GetTempPath(), $"rinha-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataPath);

        await WriteMccRiskAsync();
        await WriteReferencesAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set DATA_PATH here — right before the app starts — to avoid race conditions
        // between concurrent fixture initializations that share the global env var.
        Environment.SetEnvironmentVariable("DATA_PATH", _dataPath);
        builder.UseEnvironment("Testing");
    }

    private Task WriteMccRiskAsync() =>
        File.WriteAllTextAsync(
            Path.Combine(_dataPath, "mcc_risk.json"),
            """{"5411":0.15,"5812":0.30,"5912":0.20}""");

    private async Task WriteReferencesAsync()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(_ => new { vector = new float[14], label = "legit" })
            .Concat(Enumerable.Range(0, 5)
                .Select(_ => new { vector = Enumerable.Repeat(1f, 14).ToArray(), label = "fraud" }))
            .ToList();

        var path = Path.Combine(_dataPath, "references.json.gz");
        await using var fs = File.Create(path);
        await using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        await JsonSerializer.SerializeAsync(gz, entries);
    }
}

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<ApiFactory> { }
