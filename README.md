# Rinha Backend 2026 — Fraud Detection API

A high-performance fraud detection scoring service built for the Rinha de Backend 2026 challenge. Written in .NET 9.0, it scores financial transactions in real time using a K-Nearest Neighbors (KNN) algorithm against a dataset of ~3 million reference transactions.

## How It Works

Each incoming transaction is converted into a **14-dimensional feature vector** and compared against pre-indexed reference data using KNN (K=5). The fraud score is the ratio of fraudulent neighbors found:

```
fraud_score = fraud_neighbors / 5
approved    = fraud_score < 0.6
```

### Feature Vector (14 dimensions)

| # | Feature | Normalization |
|---|---------|---------------|
| 0 | Transaction amount | Clamped to 10,000 |
| 1 | Installments | Clamped to 12 |
| 2 | Amount vs customer average | Ratio / 10, clamped to 1 |
| 3 | Hour of day | / 23 |
| 4 | Day of week (Mon=0, Sun=6) | / 6 |
| 5 | Minutes since last transaction | / 1440 |
| 6 | Km from last transaction | / 1000, clamped to 1 |
| 7 | Km from home | / 1000, clamped to 1 |
| 8 | Transaction count in last 24h | / 20, clamped to 1 |
| 9 | Is online terminal | Binary 0/1 |
| 10 | Card present | Binary 0/1 |
| 11 | Unknown merchant | Binary 0/1 |
| 12 | MCC risk coefficient | From `mcc_risk.json` |
| 13 | Merchant average amount | / 10,000, clamped to 1 |

## API

### `GET /ready`
Health check. Returns `200 OK` when the service is ready.

### `POST /fraud-score`

**Request:**
```json
{
  "id": "txn-123",
  "transaction": {
    "amount": 250.00,
    "installments": 1,
    "requested_at": "2026-05-17T14:30:00Z"
  },
  "customer": {
    "avg_amount": 180.00,
    "tx_count_24h": 3,
    "known_merchants": ["merchant-abc"]
  },
  "merchant": {
    "id": "merchant-xyz",
    "mcc": "5411",
    "avg_amount": 120.00
  },
  "terminal": {
    "is_online": false,
    "card_present": true,
    "km_from_home": 2.5
  },
  "last_transaction": {
    "timestamp": "2026-05-17T13:00:00Z",
    "km_from_current": 1.2
  }
}
```

**Response:**
```json
{
  "approved": true,
  "fraud_score": 0.2
}
```

## Infrastructure

```
               ┌─────────────┐
               │    nginx    │  port 9999
               │ (0.1 CPU,   │
               │   20 MB)    │
               └──────┬──────┘
              ┌───────┴───────┐
         ┌────▼────┐     ┌────▼────┐
         │  api1   │     │  api2   │  port 8080
         │ 0.45CPU │     │ 0.45CPU │
         │  165MB  │     │  165MB  │
         └─────────┘     └─────────┘
```

Total resource budget: **1.0 CPU / 350 MB RAM** (plus 20 MB for nginx).

## Performance Optimizations

- **Binary cache**: Reference data is pre-processed from `references.json.gz` into a compact `.bin` format during Docker build, storing vectors as `Half` (float16) — ~87 MB per instance at runtime.
- **Zero-allocation hot path**: Thread-local `float[]` buffers reused across requests to avoid heap allocations.
- **Unsafe KNN search**: Squared Euclidean distance computed via pointer arithmetic and inlining.
- **JIT warmup**: Service is called once at startup to ensure compilation before the first real request.
- **Tuned GC**: `DOTNET_GCConserveMemory=9` and `DOTNET_GCHeapHardLimitPercent=75` for tight memory containers.

## Running Locally

**Requirements:** Docker and Docker Compose.

```bash
docker compose up --build
```

The API will be available at `http://localhost:9999`.

## Running Tests

**Unit tests** (89 tests — fast, no I/O):
```bash
dotnet test src/Api.Tests/Api.Tests.csproj
```

**Integration tests** (9 tests — spins up the real `WebApplication` against synthetic data):
```bash
dotnet test src/Api.IntegrationTests/Api.IntegrationTests.csproj
```

To generate an HTML coverage report (requires [ReportGenerator](https://github.com/danielpalme/ReportGenerator)):

```powershell
./coverage.ps1
```

Reports are written to `coverage-report/` and `coverage-results/`.

## Running Benchmarks

Benchmarks use [BenchmarkDotNet](https://benchmarkdotnet.org/) and must run in Release mode:

```bash
# all benchmarks
dotnet run -c Release --project src/Api.Benchmarks

# specific benchmark class
dotnet run -c Release --project src/Api.Benchmarks -- --filter "*Knn*"
```

| Benchmark | What it measures |
|---|---|
| `VectorNormalizerBenchmarks` | Cost of normalizing one request into a 14D float vector |
| `KnnSearchBenchmarks` | KNN search latency at 10K / 100K / 1M / 3M vectors |
| `ScoringPipelineBenchmarks` | Full per-request pipeline at 3M vectors (production scenario) |

After the run, HTML reports are generated in `BenchmarkDotNet.Artifacts/results/`. To view them in a browser:

```powershell
# Windows
start BenchmarkDotNet.Artifacts/results/Api.Benchmarks.KnnSearchBenchmarks-report.html
```

Or serve all reports locally:
```bash
npx serve BenchmarkDotNet.Artifacts/results
```

## Project Structure

```
├── src/
│   ├── Api/
│   │   ├── Program.cs                    # Entry point & startup
│   │   ├── Program.Partial.cs            # Exposes Program to WebApplicationFactory
│   │   ├── Models.cs                     # Request/response DTOs
│   │   ├── FraudDetectionService.cs      # Scoring orchestration
│   │   ├── VectorNormalizer.cs           # Feature engineering (14D)
│   │   └── ReferenceDataStore.cs         # KNN search engine
│   ├── Api.Tests/                        # 89 unit tests (xUnit)
│   ├── Api.IntegrationTests/             # 9 integration tests (WebApplicationFactory)
│   └── Api.Benchmarks/                   # BenchmarkDotNet — normalizer, KNN, pipeline
├── docs/
│   ├── docfx.json                        # DocFX configuration
│   ├── Api.Docs.csproj                   # Docs-only project (no Razor SDK)
│   └── docs/                             # Architecture, API reference, performance guides
├── resources/
│   ├── references.json.gz                # ~3M reference transactions
│   ├── mcc_risk.json                     # MCC → fraud risk mapping
│   └── normalization.json                # Feature normalization bounds
├── Dockerfile                            # Multi-stage build
├── docker-compose.yml                    # nginx + 2x api
└── nginx.conf                            # Load balancer config
```

## CI

Two jobs run in parallel on every push and pull request to `main`:

| Job | What it runs |
|---|---|
| **Unit Tests** | `Api.Tests` — 89 unit tests with Cobertura coverage uploaded to Codecov |
| **Integration Tests** | `Api.IntegrationTests` — 9 tests against a real `WebApplication` with synthetic reference data |

A third job builds and deploys the [DocFX documentation](docs/) to GitHub Pages on every push to `main`.
