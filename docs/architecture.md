# Rendering architecture

`src/Renderer.cs` owns the DXGI adapter, Direct3D 12 device, queue, command list, pipeline state objects, and frame resources. `shaders/City.hlsl` generates deterministic geometry and defines the three shader entry paths. `shaders/MeshExpansion.hlsl` contains the geometry stream layout, the atomic reservation, and the quad index writer.

## Expanded path

1. The renderer resets the indexed indirect arguments with a GPU copy.
2. A compute invocation evaluates a meshlet and tests its bounds against the orthographic frustum. It also rejects whole quads whose projected bounds are outside the clip region.
3. The invocation reserves room by incrementing the output argument's index count once for the surviving quads. Each output quad consumes four positions, one attribute record, and six indices. Every scene object emits at most six quads. The allocations hold that upper bound for every input.
4. The renderer transitions the geometry and argument buffers out of UAV state, binds the index buffer and vertex-stage SRVs, and issues an indexed indirect draw.
5. The renderer reads the GPU timestamps and geometry counters after a fence. The same allocation is reused for the next frame.

The direct path shares the scene generator and pixel shader. The verification command compares RGBA bytes with both indirect paths for two populated views and an empty view. The expanded output shares only attributes that are constant per quad in this fixture. A translator would need a separate format and interpolation contract for arbitrary per-vertex and per-primitive outputs.

[Microsoft indirect drawing reference](https://learn.microsoft.com/en-us/windows/win32/direct3d12/indirect-drawing) · [Khronos Vulkan headers](https://github.com/KhronosGroup/Vulkan-Headers)
