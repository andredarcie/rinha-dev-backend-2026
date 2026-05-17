# Performance

## Design Constraints

The Rinha de Backend 2026 challenge enforces strict resource limits:

| Service | CPU | Memory |
|---|---|---|
| Nginx | 0.1 | 20 MB |
| api1 | 0.45 | 165 MB |
| api2 | 0.45 | 165 MB |
| **Total** | **1.0** | **350 MB** |

## Optimizations

### Memory: float16 Vector Storage

Reference vectors are stored as `Half` (IEEE 754 float16) instead of `float` (float32).
This halves the memory footprint of the vector array: ~87 MB per instance for 3M × 14-dimensional vectors.

Precision loss from float16 is negligible because all values are already normalized to [0, 1].

### Zero-Allocation Hot Path

`FraudDetectionService` uses a `[ThreadStatic]` `float[]` buffer that is allocated once per thread
and reused across all requests. No heap allocation occurs during scoring.

The KNN search itself uses `stackalloc` for the top-K distance and label arrays (5 elements each),
keeping them entirely on the stack.

### Unsafe Ref Arithmetic in KNN

`ReferenceDataStore.ComputeFraudScore` accesses the vector array via `MemoryMarshal.GetArrayDataReference`
and `Unsafe.Add` instead of indexed reads. Combined with `[MethodImpl(AggressiveInlining)]` on
`SquaredDistance`, this eliminates bounds checks and enables the JIT to produce tighter inner-loop code.

### Binary Cache for Reference Data

On first startup (or during the Docker build preprocessor stage), `references.json.gz` is parsed and
written as a compact binary file (`references.bin`):

- Header: 4-byte magic `RB26` + 4-byte version + 4-byte count
- Vectors: raw `Half` bytes (`count × 14 × 2` bytes)
- Labels: raw `byte` array (`count` bytes)

Subsequent startups read the binary file directly, skipping JSON deserialization entirely.

### GC Tuning

The runtime is configured for low-memory containers via environment variables:

```
DOTNET_GCConserveMemory=9          # most aggressive GC memory conservation
DOTNET_GCHeapHardLimitPercent=75   # cap heap at 75% of container memory
DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0  # reduce spin on thread pool semaphore
```

### JIT Warmup

At startup, `FraudDetectionService` is resolved from DI and called once with a synthetic request.
This forces JIT compilation of the hot path before the first real request arrives.

## Binary Cache Format

```
Offset  Size       Description
------  ---------  -----------
0       4 bytes    Magic: "RB26"
4       4 bytes    Version (int32, currently 1)
8       4 bytes    Entry count N (int32)
12      N×14×2 B   Vectors as raw Half bytes
12+N×28 N bytes    Labels (0 = legit, 1 = fraud)
```
