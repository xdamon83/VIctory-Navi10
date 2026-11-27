# Benchmark protocol

The v0.3.0 dataset contains the full 15-cell scaling matrix, group size and frame-slot comparisons, 10,000-frame stability, adapter tests, hardware and WARP fixture results, and same-protocol v0.2/v0.3 baselines. CSV metrics in the archived PowerShell exports use the reference machine's decimal comma. JSON numbers are locale independent. The `v030-ab` series used the initial serial and bounded batched kernels before the frame-slot ring was introduced. The `v030-release-prefix` series isolates the final group-local prefix implementation. [Scaling protocol](../docs/scaling.md) · [Raw archive and checksum](raw/v0.3.0/).

The recorded runs use one RX 5700 XT at 960 × 540 and a fixed orthographic camera. The simulation uses the same time step for every path. The path order rotates between images. Each comparison run records 150 frames per path after 20 warmup frames. The stability run records 2,000 frames per path after warmup. Preview rendering and screenshot readback are outside the benchmark.

The frame time covers command recording, submission, and completion of one GPU frame. GPU queue timestamps separate culling or generation from rasterization. The compute measurement includes its resource transitions and argument reset. The CPU submission measurement covers command recording and enqueue. The difference between frame and submission time includes the fence wait and post-fence checks. Process working set comes from Windows. Local GPU usage and budget come from DXGI and apply to the complete process, including buffers allocated for the other paths.

## Files

- `raw/compact/capabilities.json` records the Direct3D 12 and Vulkan capability probes.
- `raw/compact/objects-*/` contains four runs at 1,000, 10,000, 50,000, and 100,000 candidate objects. Each includes per-frame `benchmark.json` and `frames.csv`, plus `verification.json` for the matching raster comparisons.
- `raw/compact/stability/` contains the 6,000-frame run.
- `raw/ab/` contains the before and after runs in A1, B1, B2, A2 order. Each captures the expanded path for 1,000 frames after 120 warmup frames. A stores 32 bytes per vertex. B separates 12-byte positions and 16-byte per-quad attributes. Both retain 32-bit indices. The maximum geometry buffers total 91.2 MB in A and 52.8 MB in B at 100,000 objects. The alpha source reproduces B. The captured A results preserve the comparison.
- `SHA256SUMS` identifies the original captured bytes retained in this repository.

For each layout, the README reports the mean compute and frame times across its two equal-length runs and the highest recorded process-local memory usage. Times are rounded after combining the runs.

Run `benchmarks/run.ps1` to reproduce the compact series. The script stores new runs under `runs/`.

## Triangle fixtures in v0.2.0

`meshlets.ps1` reproduces the group-size study and stability run. The target is 64 × 64. Each frame submits 128 overlapping candidates that compete for capacity for one triangle, then compares with a direct reference. The script discards 20 warmup frames per run. The capture order is A1 (1 thread), B32 (32), B64 (64) and A2 (1), 300 frames each, followed by 2,000 stability frames at 1 thread per group. Group size counts threads in a dispatch group. Hardware wave size was left to the driver.

| RX 5700 XT run | Mean GPU compute time (ms) | P99 (ms) |
| --- | ---: | ---: |
| A1 | 0.00870 | 0.01180 |
| B32 | 0.00776 | 0.01196 |
| B64 | 0.00735 | 0.01184 |
| A2 | 0.00862 | 0.01184 |
| Stability | 0.01083 | 0.01200 |

GPU compute time covers dispatch and its output transitions. Render and readback follow. These timings characterize a deliberately contended, small workload. The results support keeping all sizes available and retaining the 1-thread validation default until larger scenes are tested. [Raw v0.2.0 records](raw/v0.2.0/).
