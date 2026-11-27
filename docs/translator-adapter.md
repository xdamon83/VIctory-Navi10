# Meshlet packet interface v1

A translator can submit a file to `Navi10.exe --meshlet-packet FILE --meshlet-prefix --frames-in-flight 2 --out runs/import`. The core consumer is `MeshletPacket.Read`. The renderer uploads the geometry once, records a compute dispatch and issues an indexed indirect draw. A separate raster draw uses independently flattened CPU indices and materials. The process returns zero only on success and writes counters, exact image comparisons and native GPU timestamps to `scale.json`. The PowerShell simulator in `tests/translator.ps1` implements the producer independently and includes seven malformed packet tests.

All fields are little-endian. Arrays immediately follow the 84-byte header. Floating-point fields are finite IEEE 754 binary32. Counts are in elements.

| Part | Type and layout |
| --- | --- |
| Header | Four ASCII bytes `VNM1`, uint32 version 1, three positive int32 counts for meshlets, vertices and primitives, then 16 row-major floats for a matrix multiplying a column position. |
| Meshlets | 20 bytes per record. Five uint32 values for vertex offset/count, primitive offset/count, visibility (0 or 1). |
| Vertices | 40 bytes. Position XYZ, linear color RGBA, normal XYZ. The normal is used in the lab's lighting space. |
| Primitives | 16 bytes. Three uint32 local vertex indices, then a uint32 material. |

Primitive ranges must form an ordered partition starting at zero and cover the full primitive array. Vertex ranges may overlap. A hidden meshlet skips its entire range. Visible indices are checked against the meshlet's vertex count before upload. Bounds are 1,000,000 meshlets, 6,000,000 vertices and 5,000,000 primitives. Trailing bytes, truncated records, nonfinite transforms and out-of-range visible references are rejected. DXGI budget checks precede GPU geometry allocation. External packets are accepted in full or rejected.

The adapter derives dispatch dimensions and output capacity from the validated packet. In prefix mode one 32-thread group processes each meshlet, using additional 32-primitive blocks as needed. Large dispatches span two dimensions. A group builds a visibility mask, computes each lane's rank with a prefix population count and reserves space once per nonempty block. The partition proof bounds total emitted indices to three times the primitive count. The fallback for independently constrained capacities uses compare-exchange and retains the v0.2 overflow and sentinel tests.

The backend owns its D3D12 resources and command queue. The 20-byte draw record is `IndexCountPerInstance, InstanceCount, StartIndexLocation, BaseVertexLocation, StartInstanceLocation` with the latter four fields set to 1, 0, 0, 0. A successful frame reports the observed triangle count after reading and verifying this record. Primitive order can vary. Version 1 accepts triangle lists with a fixed attribute layout and opaque, order-independent shading. General shader payloads, amplification work generation, arbitrary attribute semantics, order-dependent blending and live cross-device handle sharing require a richer interface.

The upstream comparison and licensing boundary are documented in [integration notes](integration-notes.md). The packet format, simulator and renderer are original MIT code.
