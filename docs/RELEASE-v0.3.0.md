# VIctory-Navi10 v0.3.0

The MIT laboratory now consumes portable geometry packets emitted by an external program. The simulator exercises host visibility, projection, 2D dispatch and indexed indirect draws. The renderer keeps geometry resident and uses fences to retire one, two or three CPU submissions on a single direct queue.

On one RX 5700 XT, a 1-million-triangle animated 720p workload took 38.52 ms in the serial meshlet compute pass and 4.59 ms with 32-lane group-local compaction (A/B/B/A, 40 frames per run). Hardware wave width was selected by the driver. A separate static-input experiment reduced peak process-local usage from 67.71 to 25.71 MiB by sharing the input vertex buffer. These comparisons use the v0.3 scaling harness and identical inputs within each experiment.

The 15-cell matrix spans 10,000 to 5 million candidate triangles and 540p, 720p and 1080p. Each cell passed four byte-exact raster comparisons. At 5 million triangles and 1080p the sampled mean throughput was 31.01 FPS and P95 latency was 32.75 ms, with around half of the candidates visible in the first frame. At 540p and 720p the P95 exceeded 33.33 ms for this count. The 10,000-frame run used 100,000 candidates, 540p and two frame slots. It checked counters on every frame and imagery separately.

The v0.2 renderer had no million-triangle benchmark. A small-fixture rerun on each version gave group-64 means of 9.11 and 8.45 microseconds respectively. At one thread the mean of A1/A2 increased from 8.29 to 9.93 microseconds. Results on such small inputs varied between repeats. The scaling A/B gains should not be applied to arbitrary workloads.

The release includes source, scripts, captured images and raw results. The renderer uses one Direct3D 12 direct queue. The Vulkan component probes capabilities. The laboratory is independent and GTA VI has not been tested.

VIctory developers with RDNA 1 hardware are invited to run the minimal packet and share their results. [Integration guide and example](https://github.com/xdamon83/VIctory-Navi10/blob/v0.3.0/docs/quick-integration.md) · [Raw JSON and CSV](https://github.com/xdamon83/VIctory-Navi10/tree/v0.3.0/benchmarks/raw/v0.3.0).
