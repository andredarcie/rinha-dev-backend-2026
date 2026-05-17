using System.Text.Json;
using RinhaBackend;

// ── Preprocessing mode ─────────────────────────────────────────────
// Called during Docker build: dotnet Api.dll --preprocess /data
if (args.Length == 2 && args[0] == "--preprocess")
{
    string dataDir = args[1];
    Console.WriteLine($"Preprocessing reference data in {dataDir}...");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await ReferenceDataStore.LoadAsync(dataDir);
    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F1}s");
    return;
}

// ── Normal API startup ──────────────────────────────────────────────
string dataPath = Environment.GetEnvironmentVariable("DATA_PATH") ?? "/app/data";

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://*:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

// Load reference data and config before registering services
Console.WriteLine("Starting up...");
var sw2 = System.Diagnostics.Stopwatch.StartNew();

var mccRiskTask = LoadMccRiskAsync(Path.Combine(dataPath, "mcc_risk.json"));
var storeTask = ReferenceDataStore.LoadAsync(dataPath);

await Task.WhenAll(mccRiskTask, storeTask);

Console.WriteLine($"Data loaded in {sw2.Elapsed.TotalSeconds:F1}s");

builder.Services.AddSingleton<IReferenceDataStore>(storeTask.Result);
builder.Services.AddSingleton<IReadOnlyDictionary<string, float>>(mccRiskTask.Result);
builder.Services.AddSingleton<FraudDetectionService>();

var app = builder.Build();

// Warm up the service to ensure JIT compilation before first request
var warmupService = app.Services.GetRequiredService<FraudDetectionService>();

app.MapGet("/ready", () => Results.Ok());

app.MapPost("/fraud-score", (FraudScoreRequest request, FraudDetectionService service) =>
    Results.Ok(service.Score(request)));

app.Run();

// ── Helpers ──────────────────────────────────────────────────────────
static async Task<IReadOnlyDictionary<string, float>> LoadMccRiskAsync(string path)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"Warning: {path} not found, using empty MCC risk table");
        return new Dictionary<string, float>();
    }

    await using var fs = File.OpenRead(path);
    var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, float>>(fs);
    return dict ?? new Dictionary<string, float>();
}
