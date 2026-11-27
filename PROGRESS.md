# v0.3.0 candidate

- Base: public MIT v0.2.0, `0921ad1`. Local development branch: `v0.3-scaling`.
- Packet adapter: shared source and versioned binary format, synthetic external producer, explicit bounded dispatch/draw commands, host visibility and matrix input. Both versions passed 12 frames each. Fifteen malformed packets were rejected. A 1 MiB synthetic budget reduced 100,000 candidates to 5,825 and preserved image equivalence.
- Geometry: resident buffers, vertex reuse, 2D dispatch, bounded batched reservations, 32-thread prefix compaction and 1–3 fenced frame slots. At 1M/720p, 38.52 to 4.59 ms mean compute; static vertex reuse reduced peak process-local memory from 67.71 to 25.71 MiB in its separate A/B.
- Verification: 15 workload/resolution cells, four reference captures each, 10,000 measured frames at 100k/540p, exact legacy fixtures on hardware/WARP, city smoke suite. Raw output: `benchmarks/raw/v0.3.0/`.
- Scope: opaque triangle lists and one resident packet per renderer. Async queue overlap, hardware wave forcing, amplification payloads, in-process upstream integration and lighting remain open. Release publication requires owner approval.
