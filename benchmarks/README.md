# Benchmark protocol

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
