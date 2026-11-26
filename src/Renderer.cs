using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal enum RenderPath { Direct, Indirect, Expanded }

internal sealed class FrameStats
{
    public int Frame { get; set; }
    public string Path { get; set; }
    public int TotalMeshlets { get; set; }
    public int SubmittedMeshlets { get; set; }
    public int SubmittedTriangles { get; set; }
    public int GeneratedVertices { get; set; }
    public int GeneratedIndices { get; set; }
    public int DrawCalls { get { return 1; } }
    public double FrameMs { get; set; }
    public double CpuSubmitMs { get; set; }
    public double GpuCullMs { get; set; }
    public double GpuRasterMs { get; set; }
    public ulong? LocalVideoBytes { get; set; }
    public ulong? LocalVideoBudgetBytes { get; set; }
    public long ProcessWorkingSetBytes { get; set; }
}

internal sealed unsafe partial class Renderer : IDisposable
{
    private readonly List<IntPtr> owned = new List<IntPtr>();
    private IntPtr device, adapter3, queue, allocator, list, fence, completed;
    private IntPtr root, rasterPipeline, cullPipeline, expandPipeline, signature, indexedSignature, queries;
    private IntPtr color, depth, visible, arguments, resetArguments, statsReadback, pixelReadback;
    private IntPtr vertices, indices, quadAttributes;
    private ulong rtv, dsv, visibleAddress, argumentsAddress, vertexAddress, attributesAddress, timestampFrequency, sequence;
    private D3D.IndexView indexView;
    private readonly int width, height, rowPitch, grid, meshletCount;
    private readonly Process process = Process.GetCurrentProcess();
    private readonly Stopwatch timer = new Stopwatch();
    private readonly Stopwatch submit = new Stopwatch();
    private bool inFlight;
    internal int Width { get { return width; } }
    internal int Height { get { return height; } }
    internal int MeshletCount { get { return meshletCount; } }
    internal readonly HardwareReport Hardware;

    internal Renderer(int width, int height, int objects, bool warp, string shaderFile, bool probeOnly = false,
        bool triangleTest = false)
    {
        this.width = width;
        this.height = height;
        meshletCount = objects;
        grid = (int)Math.Ceiling(Math.Sqrt((objects + 7) / 8));
        rowPitch = (width * 4 + 255) & ~255;
        this.triangleTest = triangleTest;
        try
        {
            IntPtr adapter = OpenAdapter(warp);
            var iid = D3D.DeviceId;
            D3D.Check(D3D.D3D12CreateDevice(adapter, 0xb000, ref iid, out device), "D3D12CreateDevice");
            Own(device);
            var adapter3Id = D3D.Adapter3Id;
            if (Marshal.QueryInterface(adapter, ref adapter3Id, out adapter3) >= 0) Own(adapter3);
            Hardware = HardwareReport.Detect(adapter, device);
            D3D.VideoMemory memory;
            if (TryGetVideoMemory(out memory))
            {
                Hardware.LocalVideoBudgetBytes = memory.Budget;
                Hardware.LocalVideoUsageBytes = memory.Usage;
            }
            if (probeOnly) return;
            InitializeQueue();
            InitializePipelines(shaderFile);
            InitializeBuffers();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private IntPtr Own(IntPtr pointer)
    {
        owned.Add(pointer);
        return pointer;
    }

    private IntPtr OpenAdapter(bool warp)
    {
        var iid = D3D.Factory1Id;
        IntPtr factory;
        D3D.Check(D3D.CreateDXGIFactory1(ref iid, out factory), "CreateDXGIFactory1");
        Own(factory);
        if (warp)
        {
            var factory4Id = new Guid("1bc6ea02-ef36-464f-bf0c-21ca39e5168a");
            IntPtr factory4;
            D3D.Check(Marshal.QueryInterface(factory, ref factory4Id, out factory4), "IDXGIFactory4");
            Own(factory4);
            var adapter1Id = new Guid("29038f61-3839-4626-91fd-086879011a05");
            IntPtr adapter;
            D3D.Check(D3D.Call<EnumWarp>(factory4, 27)(factory4, ref adapter1Id, out adapter), "EnumWarpAdapter");
            return Own(adapter);
        }

        IntPtr fallback = IntPtr.Zero;
        for (uint index = 0; ; index++)
        {
            IntPtr adapter;
            int result = D3D.Call<D3D.EnumAdapter>(factory, 12)(factory, index, out adapter);
            if (result == unchecked((int)0x887A0002)) break; // DXGI_ERROR_NOT_FOUND
            D3D.Check(result, "EnumAdapters1");
            Own(adapter);
            D3D.AdapterDescription description;
            D3D.Check(D3D.Call<D3D.DescribeAdapter>(adapter, 10)(adapter, &description), "GetDesc1");
            if ((description.Flags & 2) != 0) continue;
            var deviceId = D3D.DeviceId;
            IntPtr deviceProbe;
            if (D3D.D3D12CreateDevice(adapter, 0xb000, ref deviceId, out deviceProbe) < 0) continue;
            Marshal.Release(deviceProbe);
            if (description.Vendor == 0x1002 && description.Device == 0x731f) return adapter;
            if (fallback == IntPtr.Zero) fallback = adapter;
        }
        if (fallback == IntPtr.Zero)
            throw new InvalidOperationException("No D3D12 hardware adapter is available. Use --warp to test the renderer in software.");
        return fallback;
    }

    private delegate int EnumWarp(IntPtr self, ref Guid iid, out IntPtr adapter);

    private IntPtr Create(int slot, void* desc, Guid iid, string name)
    {
        IntPtr result;
        D3D.Check(D3D.Call<D3D.Make>(device, slot)(device, desc, ref iid, out result), name);
        return Own(result);
    }

    private void InitializeQueue()
    {
        var queueDesc = new D3D.QueueDescription();
        queue = Create(D3D.Device.CreateQueue, &queueDesc, D3D.QueueId, "CreateCommandQueue");
        D3D.Check(D3D.Call<D3D.Frequency>(queue, 16)(queue, out timestampFrequency), "GetTimestampFrequency");
        if (timestampFrequency == 0) throw new InvalidOperationException("Invalid GPU timestamp frequency.");

        var iid = D3D.AllocatorId;
        D3D.Check(D3D.Call<D3D.MakeAllocator>(device, D3D.Device.CreateAllocator)(device, 0, ref iid, out allocator), "CreateCommandAllocator");
        Own(allocator);
        iid = D3D.ListId;
        D3D.Check(D3D.Call<D3D.MakeList>(device, D3D.Device.CreateList)(device, 0, 0, allocator,
            IntPtr.Zero, ref iid, out list), "CreateCommandList");
        Own(list);
        D3D.Check(D3D.Call<D3D.NoArgs>(list, D3D.List.Close)(list), "Close initial command list");
        iid = D3D.FenceId;
        D3D.Check(D3D.Call<D3D.MakeFence>(device, D3D.Device.CreateFence)(device, 0, 0, ref iid, out fence), "CreateFence");
        Own(fence);
        completed = D3D.CreateEventW(IntPtr.Zero, 0, 0, IntPtr.Zero);
        if (completed == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void InitializePipelines(string source)
    {
        D3D.RootParameter* parameters = stackalloc D3D.RootParameter[12];
        parameters[0] = new D3D.RootParameter { Type = 1, Constants = 16 };
        parameters[1] = new D3D.RootParameter { Type = 3, Visibility = 1 }; // vertex SRV
        parameters[2] = new D3D.RootParameter { Type = 4 };
        parameters[3] = new D3D.RootParameter { Type = 4, Register = 1 };
        parameters[4] = new D3D.RootParameter { Type = 3, Register = 1, Visibility = 1 };
        parameters[5] = new D3D.RootParameter { Type = 4, Register = 2 };
        parameters[6] = new D3D.RootParameter { Type = 4, Register = 3 };
        parameters[7] = new D3D.RootParameter { Type = 3, Register = 2, Visibility = 1 };
        parameters[8] = new D3D.RootParameter { Type = 4, Register = 4 };
        parameters[9] = new D3D.RootParameter { Type = 3, Register = 3 };
        parameters[10] = new D3D.RootParameter { Type = 3, Register = 4 };
        parameters[11] = new D3D.RootParameter { Type = 3, Register = 5 };
        var description = new D3D.RootDescription { Count = triangleTest ? 12 : 9, Parameters = parameters };
        IntPtr serialized, error;
        D3D.CheckCompiler(D3D.D3D12SerializeRootSignature(&description, 1, out serialized, out error),
            error, "D3D12SerializeRootSignature");
        Own(serialized);
        var blob = D3D.Blob(serialized);
        var iid = D3D.RootId;
        D3D.Check(D3D.Call<D3D.MakeRoot>(device, D3D.Device.CreateRootSignature)(device, 0, blob.Bytes,
            blob.Length, ref iid, out root), "CreateRootSignature");
        Own(root);

        var raster = new D3D.GraphicsPipeline
        {
            Root = root, VS = Compile(source, "VS", "vs_5_1"), PS = Compile(source, "PS", "ps_5_1"),
            SampleMask = UInt32.MaxValue, FillMode = 3, CullMode = 1, DepthClip = 1, DepthEnable = 1,
            DepthWriteMask = 1, DepthFunc = 2, Topology = 3, RenderTargetCount = 1, ColorFormat = D3D.Rgba8,
            DepthFormat = D3D.D32, SampleCount = 1
        };
        for (int index = 2; index < 82; index += 10)
        {
            raster.Blend[index + 2] = raster.Blend[index + 3] = raster.Blend[index + 4] = 1;
            raster.Blend[index + 5] = raster.Blend[index + 6] = raster.Blend[index + 7] = 1;
            raster.Blend[index + 9] = 15;
        }
        for (int index = 0; index < 8; index++) raster.StencilOps[index] = (index & 3) == 3 ? 8 : 1;
        rasterPipeline = Create(D3D.Device.CreateGraphicsPipeline, &raster, D3D.PipelineId, "CreateGraphicsPipelineState");
        if (!triangleTest)
        {
            var cull = new D3D.ComputePipeline { Root = root, CS = Compile(source, "Cull", "cs_5_1") };
            cullPipeline = Create(D3D.Device.CreateComputePipeline, &cull, D3D.PipelineId, "CreateComputePipelineState");
        }
        var expand = new D3D.ComputePipeline { Root = root, CS = Compile(source, "Expand", "cs_5_1") };
        expandPipeline = Create(D3D.Device.CreateComputePipeline, &expand, D3D.PipelineId, "Create expansion pipeline");
        signature = MakeSignature(0, 16);
        indexedSignature = MakeSignature(1, 20);
    }

    private IntPtr MakeSignature(int kind, int stride)
    {
        var argument = new D3D.IndirectArgumentDescription { Type = kind };
        var desc = new D3D.SignatureDescription { Stride = stride, Count = 1, Arguments = &argument };
        var iid = D3D.SignatureId;
        IntPtr result;
        D3D.Check(D3D.Call<D3D.MakeSignature>(device, D3D.Device.CreateSignature)(device, &desc,
            IntPtr.Zero, ref iid, out result), "CreateCommandSignature");
        return Own(result);
    }

    private D3D.Shader Compile(string source, string entry, string profile)
    {
        IntPtr shader, error;
        int result = D3D.D3DCompileFromFile(source, IntPtr.Zero, (IntPtr)1,
            entry, profile, (1u << 15) | (1u << 11), 0, out shader, out error);
        D3D.CheckCompiler(result, error, entry + " / " + profile);
        Own(shader);
        return D3D.Blob(shader);
    }

    private ulong CreateView(int type, IntPtr texture)
    {
        var heapDesc = new D3D.DescriptorHeapDescription { Type = type, Count = 1 };
        IntPtr heap = Create(D3D.Device.CreateDescriptorHeap, &heapDesc, D3D.DescriptorHeapId, "CreateDescriptorHeap");
        ulong handle = 0;
        D3D.Call<D3D.GetCpuHandle>(heap, 9)(heap, &handle);
        int viewMethod = type == 2 ? D3D.Device.CreateRtv : D3D.Device.CreateDsv;
        D3D.Call<D3D.MakeView>(device, viewMethod)(device, texture, IntPtr.Zero, handle);
        return handle;
    }

    private IntPtr Resource(D3D.ResourceDescription description, int heapType, int initialState, string name)
    {
        var heap = new D3D.HeapProperties { Type = heapType, CreationNodeMask = 1, VisibleNodeMask = 1 };
        var iid = D3D.ResourceId;
        IntPtr resource;
        D3D.Check(D3D.Call<D3D.MakeResource>(device, D3D.Device.CreateResource)(device, &heap, 0,
            &description, initialState, IntPtr.Zero, ref iid, out resource), name);
        return Own(resource);
    }

    private IntPtr Buffer(int size, int heap, int state, string name, bool uav = false)
    {
        return Resource(new D3D.ResourceDescription
        {
            Dimension = 1, Width = (ulong)size, Height = 1, DepthOrArraySize = 1, MipLevels = 1,
            SampleCount = 1, Layout = 1, Flags = uav ? 4 : 0
        }, heap, state, name);
    }

    private IntPtr Texture(int format, int state, int flags, string name)
    {
        return Resource(new D3D.ResourceDescription
        {
            Dimension = 3, Width = (ulong)width, Height = (uint)height, DepthOrArraySize = 1,
            MipLevels = 1, SampleCount = 1, Format = format, Flags = flags
        }, D3D.DefaultHeap, state, name);
    }

    private void InitializeBuffers()
    {
        var queryDesc = new D3D.QueryHeapDescription { Type = 1, Count = 3 };
        queries = Create(D3D.Device.CreateQueryHeap, &queryDesc, D3D.QueryHeapId, "CreateQueryHeap");
        color = Texture(D3D.Rgba8, D3D.RenderTarget, 1, "Create color texture");
        depth = Texture(D3D.D32, D3D.DepthWrite, 2, "Create depth texture");
        rtv = CreateView(2, color);
        dsv = CreateView(3, depth);
        visible = Buffer(MeshletCount * 4, D3D.DefaultHeap, D3D.NonPixelShaderResource, "Create meshlet index buffer", true);
        arguments = Buffer(triangleTest ? 28 : 24, D3D.DefaultHeap, D3D.CopyDest, "Create indirect arguments", true);
        resetArguments = Buffer(48, D3D.UploadHeap, D3D.GenericRead, "Create initial arguments");
        statsReadback = Buffer(32, D3D.ReadbackHeap, D3D.CopyDest, "Create statistics readback");
        pixelReadback = Buffer(rowPitch * height, D3D.ReadbackHeap, D3D.CopyDest, "Create pixel readback");
        vertices = Buffer(MeshletCount * 24 * 12, D3D.DefaultHeap, D3D.NonPixelShaderResource, "Create expanded positions", true);
        quadAttributes = Buffer(MeshletCount * 6 * 16, D3D.DefaultHeap, D3D.NonPixelShaderResource, "Create quad attributes", true);
        indices = Buffer(MeshletCount * 36 * 4, D3D.DefaultHeap, D3D.IndexBuffer, "Create expanded indices", true);
        vertexAddress = D3D.Call<D3D.GetU64>(vertices, 11)(vertices);
        attributesAddress = D3D.Call<D3D.GetU64>(quadAttributes, 11)(quadAttributes);
        indexView = new D3D.IndexView { Address = D3D.Call<D3D.GetU64>(indices, 11)(indices),
            Bytes = (uint)(MeshletCount * 36 * 4), Format = 42 };
        visibleAddress = D3D.Call<D3D.GetU64>(visible, 11)(visible);
        argumentsAddress = D3D.Call<D3D.GetU64>(arguments, 11)(arguments);
        var range = new D3D.Range(0, 0);
        IntPtr data;
        D3D.Check(D3D.Call<D3D.Map>(resetArguments, 8)(resetArguments, 0, &range, out data), "Map initial arguments");
        uint* words = (uint*)data;
        for (int index = 0; index < 12; index++) words[index] = 0;
        words[0] = 36; // DrawInstanced.VertexCountPerInstance
        words[7] = 1;  // DrawIndexedInstanced.InstanceCount
        range = new D3D.Range(0, 48);
        D3D.Call<D3D.Unmap>(resetArguments, 9)(resetArguments, 0, &range);
    }

    internal FrameStats Render(View view, RenderPath path, float time, bool capture)
    {
        bool indirect = path != RenderPath.Direct;
        bool expanded = path == RenderPath.Expanded;
        timer.Restart();
        submit.Restart();
        var constants = view.ForFrame(grid, MeshletCount, width, height, path, time);
        D3D.Check(D3D.Call<D3D.NoArgs>(allocator, 8)(allocator), "Reset command allocator");
        D3D.Check(D3D.Call<D3D.ResetList>(list, D3D.List.Reset)(list, allocator, IntPtr.Zero), "Reset command list");
        Timestamp(0);
        if (indirect) RecordCull(&constants, expanded);
        Timestamp(1);
        RecordRaster(&constants, path);
        Timestamp(2);
        D3D.Call<D3D.ResolveQuery>(list, D3D.List.ResolveQuery)(list, queries, D3D.QueryTimestamp, 0, 3, statsReadback, 0);
        if (indirect)
        {
            Transition(arguments, D3D.IndirectArgument, D3D.CopySource);
            Copy(statsReadback, 24, arguments, expanded ? 20u : 4u, 4);
            if (expanded) Copy(statsReadback, 28, arguments, 0, 4);
            Transition(arguments, D3D.CopySource, D3D.CopyDest);
        }
        if (capture) RecordCapture();
        D3D.Check(D3D.Call<D3D.NoArgs>(list, D3D.List.Close)(list), "Close command list");
        IntPtr submittedList = list;
        D3D.Call<D3D.Execute>(queue, 10)(queue, 1, &submittedList);
        inFlight = true;
        D3D.Check(D3D.Call<D3D.Signal>(queue, 14)(queue, fence, ++sequence), "Signal GPU fence");
        submit.Stop();
        WaitForFrame();
        timer.Stop();

        var range = new D3D.Range(0, expanded ? 32u : indirect ? 28u : 24u);
        IntPtr stats;
        D3D.Check(D3D.Call<D3D.Map>(statsReadback, 8)(statsReadback, 0, &range, out stats), "Map frame statistics");
        ulong* stamps = (ulong*)stats;
        int submitted = indirect ? *(int*)((byte*)stats + 24) : MeshletCount;
        int generated = expanded ? *(int*)((byte*)stats + 28) : 0;
        var row = new FrameStats
        {
            Path = path.ToString().ToLowerInvariant(), TotalMeshlets = MeshletCount,
            SubmittedMeshlets = submitted, SubmittedTriangles = expanded ? generated / 3 : submitted * 12,
            GeneratedIndices = generated, GeneratedVertices = generated / 6 * 4,
            FrameMs = timer.Elapsed.TotalMilliseconds, CpuSubmitMs = submit.Elapsed.TotalMilliseconds,
            GpuCullMs = indirect ? (stamps[1] - stamps[0]) * (1000.0 / timestampFrequency) : 0.0,
            GpuRasterMs = (stamps[2] - stamps[1]) * (1000.0 / timestampFrequency)
        };
        range = new D3D.Range(0, 0);
        D3D.Call<D3D.Unmap>(statsReadback, 9)(statsReadback, 0, &range);
        if (submitted < 0 || submitted > MeshletCount || generated < 0 || generated > submitted * 36 ||
            generated % 6 != 0 || (expanded && ((generated == 0) != (submitted == 0))))
            throw new InvalidOperationException("GPU produced invalid indirect geometry counts.");
        D3D.VideoMemory video;
        if (TryGetVideoMemory(out video))
        {
            row.LocalVideoBytes = video.Usage;
            row.LocalVideoBudgetBytes = video.Budget;
        }
        process.Refresh();
        row.ProcessWorkingSetBytes = process.WorkingSet64;
        return row;
    }

    private bool TryGetVideoMemory(out D3D.VideoMemory memory)
    {
        memory = new D3D.VideoMemory();
        return adapter3 != IntPtr.Zero &&
            D3D.Call<D3D.QueryVideoMemory>(adapter3, 14)(adapter3, 0, 0, out memory) >= 0;
    }

    private void RecordCull(FrameConstants* constants, bool expanded)
    {
        Copy(arguments, 0, resetArguments, expanded ? 24u : 0u, 24);
        Transition(arguments, D3D.CopyDest, D3D.UnorderedAccess);
        if (expanded)
        {
            Transition(vertices, D3D.NonPixelShaderResource, D3D.UnorderedAccess);
            Transition(quadAttributes, D3D.NonPixelShaderResource, D3D.UnorderedAccess);
            Transition(indices, D3D.IndexBuffer, D3D.UnorderedAccess);
        }
        else Transition(visible, D3D.NonPixelShaderResource, D3D.UnorderedAccess);
        D3D.Call<D3D.SetObject>(list, D3D.List.Pipeline)(list, expanded ? expandPipeline : cullPipeline);
        D3D.Call<D3D.SetObject>(list, D3D.List.ComputeRoot)(list, root);
        D3D.Call<D3D.SetConstants>(list, D3D.List.ComputeConstants)(list, 0, 16, constants, 0);
        var uav = D3D.Call<D3D.SetAddress>(list, D3D.List.ComputeUav);
        uav(list, 3, argumentsAddress);
        if (expanded)
        {
            uav(list, 5, vertexAddress);
            uav(list, 6, indexView.Address);
            uav(list, 8, attributesAddress);
        }
        else uav(list, 2, visibleAddress);
        D3D.Call<D3D.Dispatch>(list, D3D.List.Dispatch)(list, (uint)((MeshletCount + 63) / 64), 1, 1);
        if (expanded)
        {
            Transition(vertices, D3D.UnorderedAccess, D3D.NonPixelShaderResource);
            Transition(quadAttributes, D3D.UnorderedAccess, D3D.NonPixelShaderResource);
            Transition(indices, D3D.UnorderedAccess, D3D.IndexBuffer);
        }
        else Transition(visible, D3D.UnorderedAccess, D3D.NonPixelShaderResource);
        Transition(arguments, D3D.UnorderedAccess, D3D.IndirectArgument);
    }

    private void RecordRaster(FrameConstants* constants, RenderPath path)
    {
        D3D.Call<D3D.SetObject>(list, D3D.List.Pipeline)(list, rasterPipeline);
        D3D.Call<D3D.SetObject>(list, D3D.List.GraphicsRoot)(list, root);
        D3D.Call<D3D.SetConstants>(list, D3D.List.GraphicsConstants)(list, 0, 16, constants, 0);
        D3D.Call<D3D.SetAddress>(list, D3D.List.GraphicsSrv)(list, 1, visibleAddress);
        D3D.Call<D3D.SetAddress>(list, D3D.List.GraphicsSrv)(list, 4,
            triangleTest && path == RenderPath.Direct ? referenceAddress : vertexAddress);
        D3D.Call<D3D.SetAddress>(list, D3D.List.GraphicsSrv)(list, 7, attributesAddress);
        var viewport = new D3D.Viewport { Width = width, Height = height, MaxDepth = 1 };
        var scissor = new D3D.Rect { Right = width, Bottom = height };
        D3D.Call<D3D.SetViewports>(list, D3D.List.SetViewports)(list, 1, &viewport);
        D3D.Call<D3D.SetScissors>(list, D3D.List.SetScissors)(list, 1, &scissor);
        ulong colorHandle = rtv, depthHandle = dsv;
        D3D.Call<D3D.SetRenderTargets>(list, D3D.List.RenderTargets)(list, 1, &colorHandle, 0, &depthHandle);
        float* background = stackalloc float[4];
        background[0] = 0.077f; background[1] = 0.108f; background[2] = 0.154f; background[3] = 1.0f;
        D3D.Call<D3D.ClearRtv>(list, D3D.List.ClearColor)(list, rtv, background, 0, null);
        D3D.Call<D3D.ClearDsv>(list, D3D.List.ClearDepth)(list, dsv, 1, 1.0f, 0, 0, null);
        D3D.Call<D3D.SetUInt>(list, D3D.List.PrimitiveTopology)(list, 4);
        if (path != RenderPath.Direct)
        {
            bool expanded = path == RenderPath.Expanded;
            if (expanded)
            {
                var index = indexView;
                D3D.Call<D3D.SetIndexBuffer>(list, D3D.List.SetIndex)(list, &index);
            }
            D3D.Call<D3D.ExecuteIndirect>(list, D3D.List.ExecuteIndirect)(list, expanded ? indexedSignature : signature,
                1, arguments, 0, IntPtr.Zero, 0);
        }
        else
            D3D.Call<D3D.DrawInstanced>(list, D3D.List.Draw)(list,
                triangleTest ? referenceCount : 36u, triangleTest ? 1u : (uint)MeshletCount, 0, 0);
    }

    private void RecordCapture()
    {
        Transition(color, D3D.RenderTarget, D3D.CopySource);
        var source = new D3D.TextureCopyLocation { Resource = color };
        var destination = new D3D.TextureCopyLocation
        {
            Resource = pixelReadback, Type = 1, Format = D3D.Rgba8, Width = (uint)width,
            Height = (uint)height, Depth = 1, RowPitch = (uint)rowPitch
        };
        D3D.Call<D3D.CopyTexture>(list, D3D.List.CopyTexture)(list, &destination, 0, 0, 0, &source, IntPtr.Zero);
        Transition(color, D3D.CopySource, D3D.RenderTarget);
    }

    internal void ReadPixels(byte[] destination)
    {
        if (destination.Length != width * height * 4) throw new ArgumentException("Invalid RGBA buffer length.");
        var range = new D3D.Range(0, (uint)(rowPitch * height));
        IntPtr data;
        D3D.Check(D3D.Call<D3D.Map>(pixelReadback, 8)(pixelReadback, 0, &range, out data), "Map color capture");
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(IntPtr.Add(data, rowPitch * y), destination, width * 4 * y, width * 4);
        }
        finally
        {
            range = new D3D.Range(0, 0);
            D3D.Call<D3D.Unmap>(pixelReadback, 9)(pixelReadback, 0, &range);
        }
    }

    private void WaitForFrame()
    {
        if (D3D.Call<D3D.GetU64>(fence, 8)(fence) < sequence)
        {
            D3D.Check(D3D.Call<D3D.SetEventWhenComplete>(fence, 9)(fence, sequence, completed), "SetEventOnCompletion");
            uint status = D3D.WaitForSingleObject(completed, 15000);
            if (status != 0)
                throw new InvalidOperationException(String.Format("GPU fence wait failed (status 0x{0:X8})", status));
        }
        inFlight = false;
        D3D.Check(D3D.Call<D3D.NoArgs>(device, D3D.Device.RemovedReason)(device), "GetDeviceRemovedReason");
    }

    private void Timestamp(uint index)
    {
        D3D.Call<D3D.EndQuery>(list, D3D.List.EndQuery)(list, queries, D3D.QueryTimestamp, index);
    }

    private void Copy(IntPtr target, ulong targetOffset, IntPtr source, ulong sourceOffset, ulong bytes)
    {
        D3D.Call<D3D.CopyBuffer>(list, D3D.List.CopyBuffer)(list, target, targetOffset, source, sourceOffset, bytes);
    }

    private void Transition(IntPtr resource, int before, int after)
    {
        var barrier = new D3D.TransitionBarrier
        {
            Resource = resource, Before = before, After = after, Subresource = UInt32.MaxValue
        };
        D3D.Call<D3D.Barriers>(list, D3D.List.Barriers)(list, 1, &barrier);
    }

    public void Dispose()
    {
        if (inFlight)
        {
            try { WaitForFrame(); }
            catch (InvalidOperationException) { } // Preserve the original rendering failure.
        }
        if (completed != IntPtr.Zero) D3D.CloseHandle(completed);
        completed = IntPtr.Zero;
        for (int index = owned.Count - 1; index >= 0; index--) Marshal.Release(owned[index]);
        owned.Clear();
        process.Dispose();
    }
}
