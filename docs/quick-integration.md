# Integrate in three steps

1. Run `$exe = .\build.ps1` and `.\examples\triangle-packet.ps1 -Executable $exe` from the repository root. The example writes a version 2 packet, renders one triangle and verifies `scale.json` against the indexed raster reference. The output directory also contains `compute.png` and `direct.png`.
2. Replace the example's packet writer with an exporter from your translator. Begin with one opaque draw, immutable vertices, local triangle indices and a visibility bit per meshlet. Pack the projection matrix and dispatch/draw records according to the [binary contract](translator-adapter.md).
3. Invoke `Navi10.exe --meshlet-packet scene.vnm --frames 1 --out runs/import`. Compare the result with your reference and inspect the visible-triangle count, draw counters and image comparisons. For an in-process port, preserve the resource states and fence ownership described in the [synchronization contract](data-sync-contract.md) and [frame-slot notes](scaling.md#resource-lifetime).

| File to reuse | Role |
| --- | --- |
| [examples/triangle-packet.ps1](../examples/triangle-packet.ps1) | Minimal standalone packet producer |
| [src/MeshletPacket.cs](../src/MeshletPacket.cs) | Versioned parser and host validation |
| [shaders/TriangleMeshlets.hlsl](../shaders/TriangleMeshlets.hlsl) | `Compact32`, output layout, and bounded fallback kernels |
| [src/TriangleRenderer.cs](../src/TriangleRenderer.cs) | Compute bindings and UAV-to-read transitions |
| [src/TriangleScale.cs](../src/TriangleScale.cs) | Resident buffers, frame slots and counter readback |
| [src/Renderer.cs](../src/Renderer.cs) | Indexed reference and indirect raster draws, root signature and fence waits |
| [tests/translator.ps1](../tests/translator.ps1) | Both packet versions, visibility checks and rejection cases |

VIctory developers with an RDNA 1 card are invited to try the one-triangle packet, then a small redistributable meshlet set. Please share the GPU and driver, packet version, visible-triangle count and comparison result in a [Navi10 issue](https://github.com/xdamon83/VIctory-Navi10/issues).
