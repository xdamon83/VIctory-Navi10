# Triangle meshlet fixtures

Run from the repository root on the reference adapter.

```powershell
$exe = .\build.ps1
.\tests\verify.ps1 -Executable $exe
& $exe --test-meshlets --out .\runs\triangle-meshlets
```

The fixture mode reads three structured buffers. `MeshletInput` is 16 bytes and holds a vertex range and a primitive range. `VertexInput` is 28 bytes with a clip-space position (implicit w = 1) and an interpolated RGBA color. `PrimitiveInput` is 16 bytes and holds three meshlet-local indices and a material ID reserved for a later milestone.

The compute pass copies the shared vertex stream once if it fits. It validates each range before reading records and validates local indices before accessing vertices. A triangle entirely outside one clip plane is culled. Each accepted triangle reserves exactly three consecutive indices with a compare-exchange on the indirect index count. Entire triangles are dropped when either output capacity is exhausted. Triangle order can vary across groups. This mode verifies opaque fixtures with no order-dependent overlap.

UAV transitions put the vertices, indices and arguments in their respective read states before the indexed draw. The same command list copies test results to readback after the draw. The CPU waits for the submission fence before reading bytes or releasing temporary resources.

`triangle-meshlets.json` records adapter details, exact pixel and index comparisons, expected and actual counters, output guards and GPU timestamps. The direct reference draws a CPU-authored triangle list independently of the compute output. The timings describe small verification fixtures and do not measure performance relative to the city benchmark.
