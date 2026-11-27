using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed class TriangleScaleFrame
{
    public int Frame { get; set; }
    public int Meshlets { get; set; }
    public int CandidateTriangles { get; set; }
    public uint VisibleTriangles { get; set; }
    public uint DroppedTriangles { get; set; }
    public double TotalMs { get; set; }
    public double CpuSubmitMs { get; set; }
    public double GpuComputeMs { get; set; }
    public double GpuRasterMs { get; set; }
    public double? GpuReferenceMs { get; set; }
    public long WorkingSetBytes { get; set; }
    public ulong? LocalVideoBytes { get; set; }
    public ulong? LocalVideoBudgetBytes { get; set; }
    public bool? ImagesMatch { get; set; }
}

internal sealed unsafe partial class Renderer
{
    private readonly int scaleTriangles;
    private readonly bool batchedTriangles;
    private MeshletPacket incomingPacket;
    private bool externalCamera;
    private readonly bool fastReservation, reuseInputVertices;
    private int scaleVertices, scaleMeshlets;
    private ulong scaleMeshletAddress, scalePrimitiveAddress;
    private IntPtr scaleReset, scaleReferencePixels;
    private TriangleSlot[] scaleSlots;
    private D3D.IndexView scaleReferenceIndex;
    internal long ScaleUploadBytes { get; private set; }
    internal int FramesInFlight { get; private set; }

    private sealed class TriangleSlot
    {
        internal IntPtr Allocator, Stats;
        internal ulong PendingFence;
        internal int Frame;
        internal long Start;
        internal double CpuSubmitMs;
        internal bool Compare;
    }

    private static int TriangleCountForMeshlet(int id, int remaining)
    {
        return Math.Min(remaining, 8 + id % 25);
    }

    private void InitializeScaleBuffers()
    {
        for (int remaining = scaleTriangles; remaining > 0; scaleMeshlets++)
            remaining -= TriangleCountForMeshlet(scaleMeshlets, remaining);
        scaleVertices = checked(scaleTriangles + scaleMeshlets * 2);
        if (incomingPacket != null)
        {
            scaleMeshlets = incomingPacket.Meshlets.Length;
            scaleVertices = incomingPacket.Vertices.Length;
            externalCamera = true;
        }
        int indexBytes = checked(scaleTriangles * 3 * sizeof(uint));
        int vertexBytes = checked(scaleVertices * sizeof(VertexInput));
        int materialBytes = checked(scaleTriangles * sizeof(uint));
        ScaleUploadBytes = (long)vertexBytes + indexBytes + materialBytes +
            (long)scaleMeshlets * sizeof(MeshletInput) + (long)scaleTriangles * sizeof(PrimitiveInput);
        D3D.VideoMemory memory;
        ulong outputBytes = (ulong)vertexBytes + (ulong)indexBytes + (ulong)materialBytes;
        if (TryGetVideoMemory(out memory) && memory.Budget != 0 &&
            (memory.Usage >= memory.Budget || (ulong)ScaleUploadBytes + outputBytes > (memory.Budget - memory.Usage) / 2))
            throw new InvalidOperationException("Triangle workload exceeds half the remaining DXGI local memory budget. Use a smaller workload.");

        var meshlets = incomingPacket == null ? new MeshletInput[scaleMeshlets] : incomingPacket.Meshlets;
        var input = incomingPacket == null ? new VertexInput[scaleVertices] : incomingPacket.Vertices;
        var primitives = incomingPacket == null ? new PrimitiveInput[scaleTriangles] : incomingPacket.Primitives;
        var reference = new uint[scaleTriangles * 3];
        var materials = new uint[scaleTriangles];
        int edge = (int)Math.Ceiling(Math.Sqrt(scaleMeshlets));
        float step = 5.8f / edge;
        int v = 0, t = 0;
        for (int m = 0; incomingPacket == null && m < scaleMeshlets; m++)
        {
            int count = TriangleCountForMeshlet(m, scaleTriangles - t);
            float x = -2.9f + step * (m % edge + 0.5f);
            float y = -2.9f + step * (m / edge + 0.5f);
            // Alternate depth by row. A planar fan shares its center and perimeter indices.
            float z = 0.9f + 0.02f * (m / edge % 7);
            meshlets[m] = new MeshletInput((uint)v, (uint)(count + 2), (uint)t, (uint)count);
            input[v] = new VertexInput(x, y, 0.4f, 0.6f, 0.8f);
            input[v].Z = z;
            for (int j = 0; j <= count; j++)
            {
                double angle = (2 * Math.PI * j) / Math.Max(3, count);
                int vertex = v + j + 1;
                input[vertex] = new VertexInput(x + step * 0.44f * (float)Math.Cos(angle),
                    y + step * 0.44f * (float)Math.Sin(angle), (j % 7) / 6f, (m % 13) / 12f, 0.75f);
                input[vertex].Z = z;
            }
            for (int j = 0; j < count; j++, t++)
            {
                uint material = (uint)((m + j) % 7 + 1);
                primitives[t] = new PrimitiveInput(0, (uint)j + 1, (uint)j + 2, material);
                reference[t * 3] = (uint)v;
                reference[t * 3 + 1] = (uint)(v + j + 1);
                reference[t * 3 + 2] = (uint)(v + j + 2);
                materials[t] = material;
            }
            v += count + 2;
        }

        if (incomingPacket != null)
        {
            foreach (var m in meshlets)
                for (uint i = 0; i < m.PrimitiveCount; i++, t++)
                {
                    var tri = primitives[m.PrimitiveOffset + i];
                    reference[3 * t] = m.VertexOffset + tri.A;
                    reference[3 * t + 1] = m.VertexOffset + tri.B;
                    reference[3 * t + 2] = m.VertexOffset + tri.C;
                    materials[t] = tri.Material;
                }
        }
        fixed (MeshletInput* m = meshlets)
        fixed (VertexInput* source = input)
        fixed (PrimitiveInput* p = primitives)
        fixed (uint* index = reference, material = materials)
        {
            scaleMeshletAddress = Address(Upload(m, meshlets.Length * sizeof(MeshletInput)));
            referenceAddress = Address(Upload(source, vertexBytes));
            scalePrimitiveAddress = Address(Upload(p, primitives.Length * sizeof(PrimitiveInput)));
            scaleReferenceIndex = new D3D.IndexView
            {
                Address = Address(Upload(index, indexBytes)), Bytes = (uint)indexBytes, Format = 42
            };
            referenceMaterialsAddress = Address(Upload(material, materialBytes));
        }
        referenceCount = (uint)t * 3;
        vertices = Buffer(reuseInputVertices ? sizeof(VertexInput) : vertexBytes, D3D.DefaultHeap,
            reuseInputVertices ? D3D.UnorderedAccess : D3D.NonPixelShaderResource, "Create scale vertices", true);
        indices = Buffer(indexBytes, D3D.DefaultHeap, D3D.IndexBuffer, "Create scale indices", true);
        triangleMaterials = Buffer(materialBytes, D3D.DefaultHeap, D3D.PixelShaderResource, "Create scale materials", true);
        vertexAddress = reuseInputVertices ? referenceAddress : Address(vertices);
        indexView = new D3D.IndexView { Address = Address(indices), Bytes = (uint)indexBytes, Format = 42 };
        arguments = Buffer(TriangleArgumentBytes, D3D.DefaultHeap, D3D.CopyDest, "Create scale arguments", true);
        argumentsAddress = Address(arguments);
        uint* initial = stackalloc uint[TriangleArgumentBytes / 4];
        for (int i = 0; i < TriangleArgumentBytes / 4; i++) initial[i] = 0;
        initial[1] = 1;
        scaleReset = Upload(initial, TriangleArgumentBytes);
        scaleSlots = new TriangleSlot[FramesInFlight];
        for (int i = 0; i < scaleSlots.Length; i++)
        {
            IntPtr frameAllocator = allocator;
            if (i > 0)
            {
                var iid = D3D.AllocatorId;
                D3D.Check(D3D.Call<D3D.MakeAllocator>(device, D3D.Device.CreateAllocator)(
                    device, 0, ref iid, out frameAllocator), "Create scale frame allocator");
                Own(frameAllocator);
            }
            scaleSlots[i] = new TriangleSlot { Allocator = frameAllocator, Stats =
                Buffer(40 + TriangleArgumentBytes, D3D.ReadbackHeap, D3D.CopyDest, "Create scale statistics") };
        }
        scaleReferencePixels = Buffer(rowPitch * height, D3D.ReadbackHeap, D3D.CopyDest, "Create reference capture");
        triangleTransform = incomingPacket != null ? incomingPacket.Projection : new float[]
        {
            1.25f * height / width, 0, 0, 0,
            0, 1.25f, 0, 0,
            0, 0, 8f / 7.75f, 0.75f * 8f / 7.75f,
            0, 0, 1, 1
        };
        incomingPacket = null;
    }

    internal TriangleScaleFrame RenderScaledTriangles(int frame, byte[] capture, byte[] reference)
    {
        QueueScaledTriangles(frame, 0, capture != null);
        return ReadScaledTriangles(0, capture, reference);
    }

    internal void QueueScaledTriangles(int frame, int slotIndex, bool compare = false)
    {
        TriangleSlot slot = scaleSlots[slotIndex];
        if (slot.PendingFence != 0) throw new InvalidOperationException("Triangle frame slot is still occupied.");
        if (!externalCamera)
        {
            triangleTransform[3] = 0.72f * (float)Math.Sin(frame * 0.037);
            triangleTransform[7] = 0.52f * (float)Math.Cos(frame * 0.041);
        }
        slot.Start = Stopwatch.GetTimestamp();
        submit.Restart();
        var constants = new FrameConstants();
        BeginTriangleFrame(slot.Allocator);
        Copy(arguments, 0, scaleReset, 0, TriangleArgumentBytes);
        Transition(arguments, D3D.CopyDest, D3D.UnorderedAccess);
        if (!reuseInputVertices) Transition(vertices, D3D.NonPixelShaderResource, D3D.UnorderedAccess);
        Transition(indices, D3D.IndexBuffer, D3D.UnorderedAccess);
        Transition(triangleMaterials, D3D.PixelShaderResource, D3D.UnorderedAccess);
        RecordTriangleExpansion(scaleMeshletAddress, referenceAddress, scalePrimitiveAddress,
            scaleMeshlets, scaleVertices, scaleTriangles, scaleVertices, scaleTriangles * 3, scaleTriangles, scaleTriangles);
        RecordRaster(&constants, RenderPath.Expanded);
        Timestamp(2);
        if (compare)
        {
            RecordCapture();
            Timestamp(3);
            RecordRaster(&constants, RenderPath.Direct);
            Timestamp(4);
            RecordCapture(scaleReferencePixels);
        }
        D3D.Call<D3D.ResolveQuery>(list, D3D.List.ResolveQuery)(list, queries,
            D3D.QueryTimestamp, 0, compare ? 5u : 3u, slot.Stats, 0);
        Transition(arguments, D3D.IndirectArgument, D3D.CopySource);
        Copy(slot.Stats, 40, arguments, 0, TriangleArgumentBytes);
        Transition(arguments, D3D.CopySource, D3D.CopyDest);
        QueueTriangleFrame();
        slot.PendingFence = sequence;
        slot.Frame = frame;
        slot.Compare = compare;
        slot.CpuSubmitMs = submit.Elapsed.TotalMilliseconds;
    }

    internal TriangleScaleFrame ReadScaledTriangles(int slotIndex, byte[] capture = null, byte[] reference = null)
    {
        TriangleSlot slot = scaleSlots[slotIndex];
        if (slot.PendingFence == 0 || slot.Compare != (capture != null))
            throw new InvalidOperationException("Invalid triangle frame collection.");
        WaitForSequence(slot.PendingFence);
        slot.PendingFence = 0;
        var result = new TriangleScaleFrame { Frame = slot.Frame, Meshlets = scaleMeshlets,
            CandidateTriangles = scaleTriangles, TotalMs = (Stopwatch.GetTimestamp() - slot.Start) *
                (1000.0 / Stopwatch.Frequency), CpuSubmitMs = slot.CpuSubmitMs };
        var range = new D3D.Range(0, 40 + TriangleArgumentBytes);
        IntPtr mapped;
        D3D.Check(D3D.Call<D3D.Map>(slot.Stats, 8)(slot.Stats, 0, &range, out mapped), "Map scale statistics");
        try
        {
            ulong* gpu = (ulong*)mapped;
            uint* state = (uint*)(gpu + 5);
            uint visible = state[0] / 3;
            if (state[0] % 3 != 0 || visible > scaleTriangles ||
                state[1] != 1 || state[2] != 0 || state[3] != 0 || state[4] != 0 || state[5] != 0 ||
                state[6] != 0 || state[7] != (reuseInputVertices ? 0 : scaleVertices) || state[8] != visible || state[9] != visible)
                throw new InvalidOperationException("Scale counters failed validation at frame " + slot.Frame);
            result.VisibleTriangles = visible;
            result.DroppedTriangles = state[6];
            double milliseconds = 1000.0 / timestampFrequency;
            result.GpuComputeMs = (gpu[1] - gpu[0]) * milliseconds;
            result.GpuRasterMs = (gpu[2] - gpu[1]) * milliseconds;
            if (slot.Compare) result.GpuReferenceMs = (gpu[4] - gpu[3]) * milliseconds;
        }
        finally
        {
            range = new D3D.Range(0, 0);
            D3D.Call<D3D.Unmap>(slot.Stats, 9)(slot.Stats, 0, &range);
        }
        if (slot.Compare)
        {
            ReadPixels(capture);
            ReadPixels(reference, scaleReferencePixels);
            bool match = true;
            for (int i = 0; i < capture.Length; i++) match &= capture[i] == reference[i];
            result.ImagesMatch = match;
        }
        D3D.VideoMemory memory;
        if (TryGetVideoMemory(out memory))
        {
            result.LocalVideoBytes = memory.Usage;
            result.LocalVideoBudgetBytes = memory.Budget;
        }
        process.Refresh();
        result.WorkingSetBytes = process.WorkingSet64;
        return result;
    }
}
