# Performance Improvement Plan

## Baseline (benchmark, 3M vectors, single-thread, .NET 9 / AVX2)

| Vectors | Latency/request | Scaling |
|---------|-----------------|---------|
| 10K     | 0.4 ms          | —       |
| 100K    | 3.9 ms          | linear  |
| 1M      | 40 ms           | linear  |
| **3M**  | **102 ms**      | linear  |

At 3M vectors (production size) and a 2-CPU Docker limit: **~20 req/s** — too slow for competition throughput.
Allocations on the hot path are effectively zero (ThreadStatic + stackalloc working correctly).

## Goal
Bring single-request latency at 3M vectors from ~102 ms to ~3–6 ms via SIMD and parallelism, targeting ~200+ req/s.

## Changes

### 1. Parallel KNN scan
- Split the 3M-entry loop across available CPU cores using `Parallel.For`
- Each thread keeps its own thread-local top-K buffer
- Merge thread-local results into the final top-K under a lock (or lockless with Interlocked)
- File: `src/Api/ReferenceDataStore.cs` — `ComputeFraudScore`

### 2. SIMD distance computation
- Replace the scalar `SquaredDistance` loop with `Vector256<float>` (8 floats/cycle)
- 14 dims → pad stored vectors to 16 dims (2 SIMD iterations instead of 14 scalar)
- File: `src/Api/ReferenceDataStore.cs` — `SquaredDistance`

### 3. Switch storage from `Half` to `float32`
- Eliminate the `(float)Unsafe.Add(...)` conversion on every element of the hot path (42M conversions/request)
- Memory cost: ~84 MB → ~168 MB, acceptable given competition constraints
- Required before SIMD can work cleanly (no native SIMD for Half arithmetic in .NET)
- Files: `src/Api/ReferenceDataStore.cs` — `_vectors` array and binary cache format (bump `BinVersion`)

## Order of implementation
1. `float32` storage (unblocks SIMD, straightforward change)
2. SIMD `SquaredDistance` (requires float32 to be done first)
3. Parallel KNN scan (independent, can be done at any point)

## Expected gains

| Change              | Estimated speedup | Latency @ 3M  |
|---------------------|-------------------|---------------|
| float32 storage     | ~1.5×             | ~68 ms        |
| + SIMD (AVX2)       | ~4–8× on top      | ~10–17 ms     |
| + Parallel (4 cores)| ~4× on top        | **~3–6 ms**   |

Combined target: ~102 ms → ~3–6 ms per request → **~200+ req/s** under 2-CPU Docker limit.

## Non-goals
- No change to normalization formulas or feature dimensions
- No change to K=5, threshold=0.6, or the scoring logic
- No approximate index (HNSW/IVF) — exact KNN is kept
