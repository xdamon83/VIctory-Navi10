# VIctory-Navi10 v0.2.0

The second experimental release adds variable-sized triangle meshlets to the independent Direct3D 12 lab. Compute emits indexed geometry and flat per-triangle material IDs. The rasterizer interpolates vertex colors and normals and handles partial clipping after conservative homogeneous rejection in compute.

- Independent vertex, index, primitive and material limits constrain each draw. Readback validates counts, indices, material associations and buffer sentinels.
- All 36 fixtures passed on the Radeon RX 5700 XT and on WARP at each of three threadgroup sizes. The RX 5700 XT passed a 2,000-frame contention run. The three city paths also passed their regression suite.
- Raw JSON and CSV results, reproduction scripts and the data and synchronization contract are included.

This release is distributed as source. Build and test commands are in the README. The triangle path is a synthetic fixture with one opaque draw and one frame in flight. The project is independent. GTA VI has not been tested.
