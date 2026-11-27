# Changelog

## v0.3.0

- Added validated external meshlet packets, explicit bounded dispatch and indexed draw templates, a simulator and negative tests.
- Added a resident geometry benchmark with animated meshlets, conventional indexed reference draws and one to three fenced frame slots.
- Added bounded batch reservations, group-local prefix compaction and immutable vertex reuse. Added a synthetic geometry budget with graceful size reduction.
- Measured five triangle counts across three resolutions, conducted controlled A/B studies and collected 10,000 measured frames on the reference Radeon.

## v0.2.0

- Added synthetic variable-size triangle meshlets with indexed indirect draws, interpolated color and normals, and flat per-triangle material IDs.
- Added a world-view-projection transform and conservative homogeneous culling. The rasterizer handles partial clipping.
- Added independent output capacity checks, atomic reservations, readback counters and sentinels.
- Added CPU reference comparisons, a fenced stability test and GPU timestamp studies for three compute group sizes.

## v0.1.0-alpha

- Added an x64 Windows Direct3D 12 lab with a procedural city and a movable camera.
- Added direct instancing, compute culling with indirect drawing, and compute geometry generation with indexed indirect drawing.
- Reduced the expanded geometry allocation by separating positions from constant per-quad attributes.
- Added adapter capability probes for Direct3D 12 and Vulkan, GPU timestamps, and per-frame JSON and CSV exports.
- Included the RX 5700 XT benchmark captures, image comparisons, and a Windows verification script.
