# Architecture

## Overview

The system exposes a single scoring endpoint behind an Nginx load balancer that distributes traffic
across two identical API instances. All state is read-only after startup, so no coordination between
instances is needed.

```
               ┌─────────────┐
               │    nginx    │  :9999
               │ (0.1 CPU,   │
               │   20 MB)    │
               └──────┬──────┘
              ┌───────┴───────┐
         ┌────▼────┐     ┌────▼────┐
         │  api1   │     │  api2   │  :8080
         │ 0.45CPU │     │ 0.45CPU │
         │  165 MB │     │  165 MB │
         └─────────┘     └─────────┘
```

**Total resource budget:** 1.0 CPU / 350 MB RAM + 20 MB for Nginx.

## Request Lifecycle

```
POST /fraud-score
      │
      ▼
FraudDetectionService.Score()
      │
      ├─► VectorNormalizer.Normalize()   → float[14]
      │
      ├─► ReferenceDataStore.ComputeFraudScore()   → KNN (K=5)
      │
      └─► FraudScoreResponse { approved, fraud_score }
```

## Component Responsibilities

| Class | Responsibility |
|---|---|
| `FraudDetectionService` | Orchestrates the scoring pipeline; owns the approval threshold (0.6) |
| `VectorNormalizer` | Converts a raw request into a normalized 14-dimensional feature vector |
| `ReferenceDataStore` | Stores ~3M reference vectors and runs the KNN search |

## Data Flow at Startup

The Docker image uses a **multi-stage build** to pre-process reference data:

1. **Build stage** — compiles the .NET application.
2. **Preprocessor stage** — runs `Api.dll --preprocess /data`, converting `references.json.gz`
   into a compact binary cache (`references.bin`).
3. **Runtime stage** — copies only the compiled DLL and pre-processed data; JSON is never loaded at runtime.

This ensures that cold-start time is dominated by binary deserialization (~87 MB) rather than JSON parsing of a ~50 MB gzip file.
