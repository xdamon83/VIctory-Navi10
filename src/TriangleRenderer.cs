using System;
using System.Runtime.InteropServices;

internal sealed unsafe partial class Renderer
{
    private const uint Guard = 0xdeadbeef;
    private readonly bool triangleTest;
    private ulong referenceAddress;
    private ulong referenceMaterialsAddress;
    private IntPtr triangleMaterials;
    private uint referenceCount;

    internal TriangleResult RenderTriangles(MeshletInput[] meshlets, VertexInput[] input,
        PrimitiveInput[] primitives, int vertexCapacity, int indexCapacity, VertexInput[] reference, uint[] materials)
    {
        if (!triangleTest || vertexCapacity < 0 || indexCapacity < 0 ||
            vertexCapacity >= MeshletCount * 24 * 12 / sizeof(VertexInput) ||
            indexCapacity >= MeshletCount * 36 || Math.Max(meshlets.Length, input.Length) > 65535 ||
            reference.Length % 3 != 0 || reference.Length / 3 != materials.Length)
            throw new ArgumentException("Invalid triangle fixture size or capacity.");

        var constants = new FrameConstants();
        var result = new TriangleResult { Pixels = new byte[width * height * 4] };
        int firstTemporary = owned.Count;
        try
        {
            fixed (MeshletInput* data = meshlets)
            fixed (VertexInput* source = input, direct = reference)
            fixed (PrimitiveInput* triangles = primitives)
            fixed (uint* directMaterials = materials)
            {
                ulong meshletAddress = Address(Upload(data, checked(meshlets.Length * sizeof(MeshletInput))));
                ulong inputAddress = Address(Upload(source, checked(input.Length * sizeof(VertexInput))));
                ulong primitiveAddress = Address(Upload(triangles, checked(primitives.Length * sizeof(PrimitiveInput))));
                referenceAddress = Address(Upload(direct, checked(reference.Length * sizeof(VertexInput))));
                referenceMaterialsAddress = Address(Upload(directMaterials, checked(materials.Length * sizeof(uint))));
                referenceCount = (uint)reference.Length;

                int vertexBytes = (vertexCapacity + 1) * sizeof(VertexInput);
                int indexBytes = (indexCapacity + 1) * sizeof(uint);
                int materialBytes = (indexCapacity / 3 + 1) * sizeof(uint);
                int materialOffset = 52 + vertexBytes + indexBytes;
                int readbackBytes = materialOffset + materialBytes;
                var seed = new uint[Math.Max(vertexBytes, indexBytes) / 4];
                for (int i = 0; i < seed.Length; i++) seed[i] = Guard;
                IntPtr canaries;
                fixed (uint* pointer = seed) canaries = Upload(pointer, seed.Length * 4);

                uint* initialArgs = stackalloc uint[7];
                for (int i = 0; i < 7; i++) initialArgs[i] = 0;
                initialArgs[1] = 1;
                IntPtr initial = Upload(initialArgs, 28);
                IntPtr readback = Buffer(readbackBytes,
                    D3D.ReadbackHeap, D3D.CopyDest, "Create triangle readback");

                BeginTriangleFrame();
                Copy(arguments, 0, initial, 0, 28);
                Transition(arguments, D3D.CopyDest, D3D.UnorderedAccess);
                PrepareTriangleOutput(vertices, D3D.NonPixelShaderResource, canaries, vertexBytes);
                PrepareTriangleOutput(indices, D3D.IndexBuffer, canaries, indexBytes);
                PrepareTriangleOutput(triangleMaterials, D3D.PixelShaderResource, canaries, materialBytes);
                D3D.Call<D3D.SetObject>(list, D3D.List.Pipeline)(list, expandPipeline);
                D3D.Call<D3D.SetObject>(list, D3D.List.ComputeRoot)(list, root);
                uint* limits = stackalloc uint[5];
                limits[0] = (uint)meshlets.Length;
                limits[1] = (uint)input.Length;
                limits[2] = (uint)primitives.Length;
                limits[3] = (uint)indexCapacity;
                limits[4] = (uint)vertexCapacity;
                D3D.Call<D3D.SetConstants>(list, D3D.List.ComputeConstants)(list, 0, 5, limits, 0);
                var srv = D3D.Call<D3D.SetAddress>(list, D3D.List.ComputeSrv);
                srv(list, 9, meshletAddress);
                srv(list, 10, inputAddress);
                srv(list, 11, primitiveAddress);
                var uav = D3D.Call<D3D.SetAddress>(list, D3D.List.ComputeUav);
                uav(list, 3, argumentsAddress);
                uav(list, 5, vertexAddress);
                uav(list, 6, indexView.Address);
                uav(list, 8, Address(triangleMaterials));
                Timestamp(0);
                D3D.Call<D3D.Dispatch>(list, D3D.List.Dispatch)(list,
                    (uint)Math.Max(1, Math.Max(meshlets.Length, input.Length)), 1, 1);
                Transition(arguments, D3D.UnorderedAccess, D3D.IndirectArgument);
                Transition(vertices, D3D.UnorderedAccess, D3D.NonPixelShaderResource);
                Transition(indices, D3D.UnorderedAccess, D3D.IndexBuffer);
                Transition(triangleMaterials, D3D.UnorderedAccess, D3D.PixelShaderResource);
                Timestamp(1);
                RecordRaster(&constants, RenderPath.Expanded);
                Timestamp(2);
                D3D.Call<D3D.ResolveQuery>(list, D3D.List.ResolveQuery)(list, queries,
                    D3D.QueryTimestamp, 0, 3, readback, 0);
                Transition(arguments, D3D.IndirectArgument, D3D.CopySource);
                Copy(readback, 24, arguments, 0, 28);
                Transition(arguments, D3D.CopySource, D3D.CopyDest);
                ReadTriangleOutput(vertices, D3D.NonPixelShaderResource, readback, 52, vertexBytes);
                ReadTriangleOutput(indices, D3D.IndexBuffer, readback, 52 + vertexBytes, indexBytes);
                ReadTriangleOutput(triangleMaterials, D3D.PixelShaderResource, readback, materialOffset, materialBytes);
                RecordCapture();
                SubmitTriangleFrame();
                ReadPixels(result.Pixels);

                var range = new D3D.Range(0, (uint)readbackBytes);
                IntPtr mapped;
                D3D.Check(D3D.Call<D3D.Map>(readback, 8)(readback, 0, &range, out mapped), "Map triangle readback");
                try
                {
                    ulong* timestamps = (ulong*)mapped;
                    uint* args = (uint*)(timestamps + 3);
                    result.GpuComputeMs = (timestamps[1] - timestamps[0]) * (1000.0 / timestampFrequency);
                    result.GpuRasterMs = (timestamps[2] - timestamps[1]) * (1000.0 / timestampFrequency);
                    result.ArgumentsValid = args[0] <= indexCapacity && args[0] % 3 == 0 &&
                        args[1] == 1 && args[2] == 0 && args[3] == 0 && args[4] == 0;
                    result.Invalid = args[5];
                    result.Dropped = args[6];
                    uint* actualVertices = args + 7;
                    uint* actualIndices = actualVertices + vertexBytes / 4;
                    int writtenWords = input.Length <= vertexCapacity ? input.Length * sizeof(VertexInput) / 4 : 0;
                    result.VerticesValid = true;
                    for (int i = 0; i < vertexBytes / 4; i++)
                        result.VerticesValid &= actualVertices[i] == (i < writtenWords ? ((uint*)source)[i] : Guard);
                    int count = (int)Math.Min(args[0], (uint)indexCapacity);
                    result.Indices = new uint[count];
                    for (int i = 0; i < count; i++) result.Indices[i] = actualIndices[i];
                    result.IndexGuardIntact = true;
                    for (int i = count; i <= indexCapacity; i++) result.IndexGuardIntact &= actualIndices[i] == Guard;
                    uint* actualMaterials = actualIndices + indexBytes / 4;
                    result.Materials = new uint[count / 3];
                    for (int i = 0; i < result.Materials.Length; i++) result.Materials[i] = actualMaterials[i];
                    result.MaterialGuardIntact = true;
                    for (int i = result.Materials.Length; i <= indexCapacity / 3; i++)
                        result.MaterialGuardIntact &= actualMaterials[i] == Guard;
                }
                finally
                {
                    range = new D3D.Range(0, 0);
                    D3D.Call<D3D.Unmap>(readback, 9)(readback, 0, &range);
                }
            }
            result.Reference = new byte[result.Pixels.Length];
            BeginTriangleFrame();
            RecordRaster(&constants, RenderPath.Direct);
            RecordCapture();
            SubmitTriangleFrame();
            ReadPixels(result.Reference);
            return result;
        }
        finally
        {
            // If a fence wait fails, let Dispose own the resources through its final wait.
            if (!inFlight)
            {
                for (int i = owned.Count - 1; i >= firstTemporary; i--)
                    Marshal.Release(owned[i]);
                owned.RemoveRange(firstTemporary, owned.Count - firstTemporary);
            }
        }
    }

    private ulong Address(IntPtr resource) { return D3D.Call<D3D.GetU64>(resource, 11)(resource); }

    private IntPtr Upload(void* source, int size)
    {
        IntPtr upload = Buffer(Math.Max(size, 4), D3D.UploadHeap, D3D.GenericRead, "Create triangle fixture upload");
        if (size == 0) return upload;
        var range = new D3D.Range(0, 0);
        IntPtr data;
        D3D.Check(D3D.Call<D3D.Map>(upload, 8)(upload, 0, &range, out data), "Map triangle fixture upload");
        System.Buffer.MemoryCopy(source, (void*)data, size, size);
        range = new D3D.Range(0, (uint)size);
        D3D.Call<D3D.Unmap>(upload, 9)(upload, 0, &range);
        return upload;
    }

    private void PrepareTriangleOutput(IntPtr buffer, int state, IntPtr seed, int size)
    {
        Transition(buffer, state, D3D.CopyDest);
        Copy(buffer, 0, seed, 0, (uint)size);
        Transition(buffer, D3D.CopyDest, D3D.UnorderedAccess);
    }

    private void ReadTriangleOutput(IntPtr buffer, int state, IntPtr readback, int offset, int size)
    {
        Transition(buffer, state, D3D.CopySource);
        Copy(readback, (uint)offset, buffer, 0, (uint)size);
        Transition(buffer, D3D.CopySource, state);
    }

    private void BeginTriangleFrame()
    {
        D3D.Check(D3D.Call<D3D.NoArgs>(allocator, 8)(allocator), "Reset triangle allocator");
        D3D.Check(D3D.Call<D3D.ResetList>(list, D3D.List.Reset)(list, allocator, IntPtr.Zero), "Reset triangle list");
    }

    private void SubmitTriangleFrame()
    {
        D3D.Check(D3D.Call<D3D.NoArgs>(list, D3D.List.Close)(list), "Close triangle list");
        IntPtr command = list;
        D3D.Call<D3D.Execute>(queue, 10)(queue, 1, &command);
        inFlight = true;
        D3D.Check(D3D.Call<D3D.Signal>(queue, 14)(queue, fence, ++sequence), "Signal triangle fence");
        WaitForFrame();
    }
}
