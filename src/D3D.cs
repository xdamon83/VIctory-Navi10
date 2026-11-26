using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Win64 COM ABI for the subset of DXGI and D3D12 used by the lab.
internal static unsafe class D3D
{
    internal const int Rgba8 = 28, D32 = 40;
    internal const int RenderTarget = 4, DepthWrite = 16, UnorderedAccess = 8;
    internal const int IndexBuffer = 2, NonPixelShaderResource = 64, IndirectArgument = 512;
    internal const int CopyDest = 1024, CopySource = 2048, GenericRead = 2755;
    internal const int DefaultHeap = 1, UploadHeap = 2, ReadbackHeap = 3;
    internal const int QueryTimestamp = 2;

    internal static readonly Guid Factory1Id = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
    internal static readonly Guid Adapter3Id = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
    internal static readonly Guid DeviceId = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
    internal static readonly Guid QueueId = new Guid("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
    internal static readonly Guid AllocatorId = new Guid("6102dee4-af59-4b09-b999-b44d73f09b24");
    internal static readonly Guid ListId = new Guid("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
    internal static readonly Guid FenceId = new Guid("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");
    internal static readonly Guid ResourceId = new Guid("696442be-a72e-4059-bc79-5b5c98040fad");
    internal static readonly Guid RootId = new Guid("c54a6b66-72df-4ee8-8be5-a946a1429214");
    internal static readonly Guid PipelineId = new Guid("765a30f3-f624-4c6f-a828-ace948622445");
    internal static readonly Guid DescriptorHeapId = new Guid("8efb471d-616c-4f49-90f7-127bb763fa51");
    internal static readonly Guid QueryHeapId = new Guid("0d9658ae-ed45-469e-a61d-970ec583cab4");
    internal static readonly Guid SignatureId = new Guid("c36a797c-ec80-4f0a-8985-a7b2475082d1");

    [DllImport("dxgi.dll")]
    internal static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("d3d12.dll")]
    internal static extern int D3D12CreateDevice(IntPtr adapter, int feature, ref Guid iid, out IntPtr device);
    [DllImport("d3d12.dll")]
    internal static extern int D3D12SerializeRootSignature(RootDescription* desc, int version,
        out IntPtr blob, out IntPtr error);
    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int D3DCompileFromFile(string source, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags, uint effects, out IntPtr blob, out IntPtr error);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateEventW(IntPtr attributes, int manualReset, int initialState, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    internal static extern int CloseHandle(IntPtr handle);

    internal static class Device
    {
        internal const int CreateQueue = 8, CreateAllocator = 9, CreateGraphicsPipeline = 10,
            CreateComputePipeline = 11, CreateList = 12, CheckFeature = 13, CreateDescriptorHeap = 14,
            CreateRootSignature = 16, CreateRtv = 20, CreateDsv = 21, CreateResource = 27,
            CreateFence = 36, RemovedReason = 37, CreateQueryHeap = 39, CreateSignature = 41;
    }

    internal static class List
    {
        internal const int Close = 9, Reset = 10, Draw = 12, Dispatch = 14, CopyBuffer = 15,
            CopyTexture = 16, PrimitiveTopology = 20, SetViewports = 21, SetScissors = 22,
            Pipeline = 25, Barriers = 26, ComputeRoot = 29, GraphicsRoot = 30,
            ComputeConstants = 35, GraphicsConstants = 36, GraphicsSrv = 40, ComputeUav = 41,
            SetIndex = 43, RenderTargets = 46, ClearDepth = 47, ClearColor = 48, EndQuery = 53,
            ResolveQuery = 54, ExecuteIndirect = 59;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct AdapterDescription
    {
        internal fixed char Name[128];
        internal uint Vendor, Device, Subsystem, Revision;
        internal ulong DedicatedVideo, DedicatedSystem, SharedSystem, Luid;
        internal uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct VideoMemory
    {
        internal ulong Budget, Usage, AvailableForReservation, Reservation;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct QueueDescription
    {
        internal int Type, Priority, Flags, NodeMask;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct DescriptorHeapDescription
    {
        internal int Type, Count, Flags, NodeMask;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct QueryHeapDescription
    {
        internal int Type, Count, NodeMask;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct HeapProperties
    {
        internal int Type, CpuPageProperty, MemoryPool, CreationNodeMask, VisibleNodeMask;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ResourceDescription
    {
        internal int Dimension;
        internal ulong Alignment, Width;
        internal uint Height;
        internal ushort DepthOrArraySize, MipLevels;
        internal int Format, SampleCount, SampleQuality, Layout, Flags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RootParameter
    {
        internal int Type, Padding;
        internal int Register, Space, Constants, UnionPadding;
        internal int Visibility, EndPadding;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RootDescription
    {
        internal int Count;
        internal RootParameter* Parameters;
        internal int SamplerCount;
        internal IntPtr Samplers;
        internal int Flags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Shader
    {
        internal IntPtr Bytes;
        internal UIntPtr Length;
    }
    [StructLayout(LayoutKind.Explicit, Size = 656)] internal struct GraphicsPipeline
    {
        [FieldOffset(0)] internal IntPtr Root;
        [FieldOffset(8)] internal Shader VS;
        [FieldOffset(24)] internal Shader PS;
        [FieldOffset(120)] internal fixed int Blend[82];
        [FieldOffset(448)] internal uint SampleMask;
        [FieldOffset(452)] internal int FillMode;
        [FieldOffset(456)] internal int CullMode;
        [FieldOffset(476)] internal int DepthClip;
        [FieldOffset(496)] internal int DepthEnable;
        [FieldOffset(500)] internal int DepthWriteMask;
        [FieldOffset(504)] internal int DepthFunc;
        [FieldOffset(516)] internal fixed int StencilOps[8];
        [FieldOffset(572)] internal int Topology;
        [FieldOffset(576)] internal int RenderTargetCount;
        [FieldOffset(580)] internal int ColorFormat;
        [FieldOffset(612)] internal int DepthFormat;
        [FieldOffset(616)] internal int SampleCount;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ComputePipeline
    {
        internal IntPtr Root;
        internal Shader CS;
        internal int NodeMask;
        internal Shader Cached;
        internal int Flags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct IndirectArgumentDescription
    {
        internal int Type, Argument0, Argument1, Argument2;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct SignatureDescription
    {
        internal int Stride, Count;
        internal IndirectArgumentDescription* Arguments;
        internal int NodeMask;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct TransitionBarrier
    {
        internal int Type, Flags;
        internal IntPtr Resource;
        internal uint Subresource;
        internal int Before, After;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Viewport
    {
        internal float X, Y, Width, Height, MinDepth, MaxDepth;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        internal int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct IndexView
    {
        internal ulong Address;
        internal uint Bytes, Format;
    }
    [StructLayout(LayoutKind.Explicit, Size = 48)] internal struct TextureCopyLocation
    {
        [FieldOffset(0)] internal IntPtr Resource;
        [FieldOffset(8)] internal int Type;
        [FieldOffset(16)] internal int Subresource;
        [FieldOffset(16)] internal ulong Offset;
        [FieldOffset(24)] internal int Format;
        [FieldOffset(28)] internal uint Width;
        [FieldOffset(32)] internal uint Height;
        [FieldOffset(36)] internal uint Depth;
        [FieldOffset(40)] internal uint RowPitch;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Range
    {
        internal UIntPtr Begin, End;
        internal Range(uint begin, uint end) { Begin = (UIntPtr)begin; End = (UIntPtr)end; }
    }

    internal delegate int EnumAdapter(IntPtr self, uint index, out IntPtr adapter);
    internal delegate int DescribeAdapter(IntPtr self, void* desc);
    internal delegate int QueryVideoMemory(IntPtr self, uint node, int segment, out VideoMemory info);
    internal delegate int DescribeDriver(IntPtr self, ref Guid iid, out ulong version);
    internal delegate int Make(IntPtr self, void* desc, ref Guid iid, out IntPtr result);
    internal delegate int MakeAllocator(IntPtr self, int type, ref Guid iid, out IntPtr result);
    internal delegate int MakeList(IntPtr self, uint node, int type, IntPtr allocator, IntPtr pso, ref Guid iid, out IntPtr result);
    internal delegate int MakeRoot(IntPtr self, uint node, IntPtr bytes, UIntPtr length, ref Guid iid, out IntPtr result);
    internal delegate int MakeFence(IntPtr self, ulong value, int flags, ref Guid iid, out IntPtr result);
    internal delegate int MakeResource(IntPtr self, HeapProperties* heap, int heapFlags, ResourceDescription* desc,
        int initialState, IntPtr clear, ref Guid iid, out IntPtr result);
    internal delegate int MakeSignature(IntPtr self, SignatureDescription* desc, IntPtr root, ref Guid iid, out IntPtr result);
    internal delegate int Feature(IntPtr self, int feature, void* value, uint size);
    internal delegate void MakeView(IntPtr self, IntPtr resource, IntPtr desc, ulong destination);
    internal delegate IntPtr GetCpuHandle(IntPtr self, ulong* handle);
    internal delegate int NoArgs(IntPtr self);
    internal delegate IntPtr BlobPointer(IntPtr self);
    internal delegate UIntPtr BlobSize(IntPtr self);
    internal delegate int Map(IntPtr self, uint subresource, Range* range, out IntPtr data);
    internal delegate void Unmap(IntPtr self, uint subresource, Range* range);
    internal delegate ulong GetU64(IntPtr self);
    internal delegate int Frequency(IntPtr self, out ulong value);
    internal delegate int SetEventWhenComplete(IntPtr self, ulong value, IntPtr handle);
    internal delegate int Signal(IntPtr self, IntPtr fence, ulong value);
    internal delegate void Execute(IntPtr self, uint count, IntPtr* lists);
    internal delegate int ResetList(IntPtr self, IntPtr allocator, IntPtr pso);
    internal delegate void SetObject(IntPtr self, IntPtr resource);
    internal delegate void SetUInt(IntPtr self, uint value);
    internal delegate void SetViewports(IntPtr self, uint count, Viewport* viewports);
    internal delegate void SetScissors(IntPtr self, uint count, Rect* scissor);
    internal delegate void SetIndexBuffer(IntPtr self, IndexView* view);
    internal delegate void SetRenderTargets(IntPtr self, uint count, ulong* targets, int contiguous, ulong* depth);
    internal delegate void SetConstants(IntPtr self, uint index, uint count, void* constants, uint offset);
    internal delegate void SetAddress(IntPtr self, uint index, ulong address);
    internal delegate void ClearRtv(IntPtr self, ulong handle, float* color, uint count, Rect* rects);
    internal delegate void ClearDsv(IntPtr self, ulong handle, int flags, float depth, byte stencil, uint count, Rect* rects);
    internal delegate void DrawInstanced(IntPtr self, uint vertices, uint instances, uint vertex, uint instance);
    internal delegate void Dispatch(IntPtr self, uint x, uint y, uint z);
    internal delegate void Barriers(IntPtr self, uint count, TransitionBarrier* barriers);
    internal delegate void ExecuteIndirect(IntPtr self, IntPtr signature, uint count, IntPtr arguments,
        ulong offset, IntPtr countBuffer, ulong countOffset);
    internal delegate void CopyBuffer(IntPtr self, IntPtr dest, ulong destOffset, IntPtr source, ulong offset, ulong bytes);
    internal delegate void CopyTexture(IntPtr self, TextureCopyLocation* dest, uint x, uint y, uint z,
        TextureCopyLocation* source, IntPtr box);
    internal delegate void EndQuery(IntPtr self, IntPtr heap, int type, uint index);
    internal delegate void ResolveQuery(IntPtr self, IntPtr heap, int type, uint first, uint count, IntPtr dest, ulong offset);

    private static class Methods<T> where T : class
    {
        [ThreadStatic] internal static Dictionary<IntPtr, T> Cache;
    }

    internal static T Call<T>(IntPtr instance, int index) where T : class
    {
        IntPtr address = (*(IntPtr**)instance)[index];
        Dictionary<IntPtr, T> entries = Methods<T>.Cache;
        if (entries == null)
            Methods<T>.Cache = entries = new Dictionary<IntPtr, T>();
        T method;
        if (!entries.TryGetValue(address, out method))
        {
            method = (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
            entries.Add(address, method);
        }
        return method;
    }

    internal static void Check(int result, string operation)
    {
        if (result < 0)
            throw new InvalidOperationException(String.Format("{0} (HRESULT 0x{1:X8})", operation, result));
    }

    internal static Shader Blob(IntPtr pointer)
    {
        return new Shader { Bytes = Call<BlobPointer>(pointer, 3)(pointer), Length = Call<BlobSize>(pointer, 4)(pointer) };
    }

    internal static void CheckCompiler(int result, IntPtr error, string operation)
    {
        try
        {
            if (result < 0 && error != IntPtr.Zero)
                operation += Environment.NewLine + Marshal.PtrToStringAnsi(Blob(error).Bytes);
            Check(result, operation);
        }
        finally
        {
            if (error != IntPtr.Zero) Marshal.Release(error);
        }
    }
}
