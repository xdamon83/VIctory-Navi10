# Triangle meshlet data and synchronization

The synthetic triangle path consumes a row-major world-view-projection matrix, 16-byte meshlet ranges, 40-byte vertex records and 16-byte primitive records. A vertex carries position, color and normal. A primitive carries three local indices and a 32-bit material. The projection uses Direct3D homogeneous clip planes. Compute rejects a triangle only when its three corners lie outside the same plane, allowing a small numeric margin. The hardware rasterizer clips triangles that remain.

The vertex stream preserves original offsets and is copied once if the complete stream fits. Each accepted triangle owns a consecutive index triple and the corresponding material slot. A compare-exchange advances the draw's index count by three only within the smallest applicable capacity. The reservation order is unspecified. The path issues one opaque draw. Its pixel shader resolves the flat material using the draw-local `SV_PrimitiveID`. Avoid order-dependent overlap in fixtures.

| Step | Resource states and ordering |
| --- | --- |
| Prepare | Inputs and direct reference are upload buffers in GENERIC_READ. Output buffers move from their preceding read states to COPY_DEST for sentinel initialization, then to UNORDERED_ACCESS. The argument buffer starts in COPY_DEST for the reset. |
| Expand | One dispatch writes vertices, indexed triangles, materials and the argument counters. Each vertex owns its source ordinal. Each successful atomic reservation owns its index triple and material slot. No consuming shader runs during the dispatch. |
| Draw | UAV-to-read transitions order the writes before the draw. Vertices enter NON_PIXEL_SHADER_RESOURCE, indices INDEX_BUFFER, materials PIXEL_SHADER_RESOURCE, and arguments INDIRECT_ARGUMENT. There is no intervening UAV consumer that would require a separate UAV barrier. |
| Read back | Outputs move to COPY_SOURCE after drawing and return to their draw states after the copies. Arguments return to COPY_DEST. Timestamp queries resolve to the same readback buffer in disjoint ranges. |
| Reuse | One direct queue executes both passes in order. The CPU waits on the signaled fence before mapping results, resetting the allocator or releasing temporary resources. The reference draw uses an independent upload stream and another fenced submission. |

The first 20 argument bytes form `D3D12_DRAW_INDEXED_ARGUMENTS`. The next five 32-bit words count invalid records, capacity drops, vertices written, valid visible triangles requested and complete triangle records written. The host derives accepted triangles from the indirect index count and checks it against complete writes. It checks vertex words, index triples, material IDs and untouched sentinel words. Invalid records and certainly clipped triangles do not reserve output.

The reference machine lacked the optional Direct3D debug layer. Hardware and WARP fixture results, GPU readback, fence completion and device removal checks are available in its place. [Direct3D 12 resource barriers](https://learn.microsoft.com/en-us/windows/win32/direct3d12/using-resource-barriers-to-synchronize-resource-states-in-direct3d-12).
