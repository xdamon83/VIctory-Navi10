# Scaling the RDNA 1 triangle path

## Audit of v0.2.0

The city renderer keeps its buffers resident. The triangle correctness harness in `RenderTriangles` instead builds upload buffers, guards and readbacks for each fixture, and synchronously submits the expansion and direct reference. It copies complete output streams to RAM and compares every pixel. Its dispatch limit, geometry buffers sized for small fixtures, per-triangle compare-exchange loop and per-vertex global instrumentation make it unsuitable for performance measurements at millions of triangles. These tests remain the bounded-output oracle. The existing Vulkan path probes capabilities. It does not render the benchmark.

## Resource lifetime

The scaled renderer allocates a workload-sized set of persistent input, output and staging buffers. Meshlets contain fans of varying sizes and seven materials. A moving camera changes visibility. Optional tile-local animation rotates individual fans and is shared by the direct and compute projections. Input upload bytes are recorded once. The vertex-reuse option binds the immutable input to the raster stage directly when expansion preserves positions and attributes. Geometry with generated or modified vertex attributes needs a writable output. The direct reference uses hardware indexed rendering with a CPU-built index and material stream.

The renderer reuses outputs in submission order on one direct queue. UAV-to-read transitions order each draw. The following submission moves the outputs back to their write states. Only the small statistics buffers and command allocators are duplicated per pending frame. An allocator and its statistics are recycled after their slot fence completes. Capture/reference submissions run separately and wait before mapping images. Timed submissions reset 40 argument bytes and resolve a small timestamp/counter record. Geometry data stays resident.

The synthetic producer can reduce the requested count to fit the DXGI budget. Reports include requested and actual counts. A conservative budget estimate counts even the upload data, whose physical residency depends on Windows. Working set and DXGI process-local usage are sampled once per collected frame. The initial geometry upload records logical bytes submitted to upload heaps. Physical PCIe traffic is unmeasured.

## Protocol

Run `benchmarks/scale.ps1` with modes `matrix`, `ab`, `memory`, `prefix`, `overlap` or `stability`. For the release matrix use `-Prefix -AnimateTiles -Frames 24 -Warmup 8`. The 10,000-frame run uses `-Mode stability -Prefix -AnimateTiles -Frames 10000 -FramesInFlight 2 -Warmup 8`. JSON includes four capture comparisons (frames 0, 23, 61 and the final sample), counters on every frame and GPU timestamps. Direct raster timing is sampled in reference frames. Timed frames exclude pixel capture and direct raster work. CSV summarizes measured latency distribution, throughput, counters and memory. Use the same variant within a comparison. The timings concern the synthetic workload.

Latency measures submission-to-collection including queue waiting. FPS uses measured throughput across the entire timed interval. With multiple slots its reciprocal differs from mean per-frame latency. The tests measure CPU/GPU pipelining on the direct queue. Although the adapter exposes a timestamp-capable compute queue, independent async compute requires producer/consumer fences and separately owned scratch outputs. Concurrent compute and graphics execution is unmeasured. Threadgroup sizes do not force hardware wave size. Lighting is deferred.
