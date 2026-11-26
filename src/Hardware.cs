using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal sealed unsafe class HardwareReport
{
    public string Adapter { get; set; }
    public uint VendorId { get; set; }
    public uint DeviceId { get; set; }
    public ulong DedicatedVideoBytes { get; set; }
    public bool SoftwareAdapter { get; set; }
    public string DriverUmdVersion { get; set; }
    public string FeatureLevel { get; set; }
    public string ShaderModel { get; set; }
    public uint? MeshShaderTier { get; set; }
    public uint? RayTracingTier { get; set; }
    public uint? ResourceBindingTier { get; set; }
    public uint? ResourceHeapTier { get; set; }
    public uint? MaxGpuVirtualAddressBitsPerResource { get; set; }
    public uint? MaxGpuVirtualAddressBitsPerProcess { get; set; }
    public bool? WaveOperations { get; set; }
    public uint? WaveLaneCountMin { get; set; }
    public uint? WaveLaneCountMax { get; set; }
    public uint? TotalLaneCount { get; set; }
    public bool ComputeQueueCreated { get; set; }
    public ulong? ComputeQueueTimestampHz { get; set; }
    public ulong? LocalVideoBudgetBytes { get; set; }
    public ulong? LocalVideoUsageBytes { get; set; }
    public string Cpu { get; set; }
    public int? PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }
    public ulong? UsablePhysicalMemoryBytes { get; set; }
    public string WindowsBuild { get; set; }

    [StructLayout(LayoutKind.Sequential)] private struct FeatureLevels
    {
        internal uint Count;
        internal uint* Levels;
        internal uint MaxSupported;
    }

    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        internal uint Length, Load;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    internal static HardwareReport Detect(IntPtr adapter, IntPtr device)
    {
        D3D.AdapterDescription gpu;
        D3D.Check(D3D.Call<D3D.DescribeAdapter>(adapter, 10)(adapter, &gpu), "GetDesc1");
        var report = new HardwareReport
        {
            Adapter = new String(gpu.Name).Trim(), VendorId = gpu.Vendor, DeviceId = gpu.Device,
            DedicatedVideoBytes = gpu.DedicatedVideo, SoftwareAdapter = (gpu.Flags & 2) != 0,
            Cpu = ReadRegistry(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString"),
            PhysicalCores = CountCores(), LogicalProcessors = Environment.ProcessorCount,
            WindowsBuild = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")
        };
        var memory = new MemoryStatus { Length = (uint)sizeof(MemoryStatus) };
        if (GlobalMemoryStatusEx(ref memory)) report.UsablePhysicalMemoryBytes = memory.TotalPhysical;
        var dxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        ulong driver;
        if (D3D.Call<D3D.DescribeDriver>(adapter, 9)(adapter, ref dxgiDevice, out driver) >= 0)
            report.DriverUmdVersion = String.Format("{0}.{1}.{2}.{3}", driver >> 48, (driver >> 32) & 65535,
                (driver >> 16) & 65535, driver & 65535);
        uint* levels = stackalloc uint[5];
        levels[0] = 0xc200; levels[1] = 0xc100; levels[2] = 0xc000; levels[3] = 0xb100; levels[4] = 0xb000;
        var feature = D3D.Call<D3D.Feature>(device, D3D.Device.CheckFeature);
        var support = new FeatureLevels { Count = 5, Levels = levels };
        if (feature(device, 2, &support, (uint)sizeof(FeatureLevels)) >= 0)
            report.FeatureLevel = String.Format("{0}_{1}", support.MaxSupported >> 12, (support.MaxSupported >> 8) & 15);
        for (uint target = 0x69; target >= 0x60; target--)
        {
            uint shader = target;
            if (feature(device, 7, &shader, 4) < 0) continue;
            report.ShaderModel = String.Format("{0}.{1}", shader >> 4, shader & 15);
            break;
        }
        uint* options = stackalloc uint[15];
        if (feature(device, 0, options, 60) >= 0)
        {
            report.ResourceBindingTier = options[4];
            report.ResourceHeapTier = options[14];
        }
        if (feature(device, 6, options, 8) >= 0)
        {
            report.MaxGpuVirtualAddressBitsPerResource = options[0];
            report.MaxGpuVirtualAddressBitsPerProcess = options[1];
        }
        if (feature(device, 8, options, 24) >= 0)
        {
            report.WaveOperations = options[0] != 0;
            report.WaveLaneCountMin = options[1];
            report.WaveLaneCountMax = options[2];
            report.TotalLaneCount = options[3];
        }
        if (feature(device, 27, options, 12) >= 0) report.RayTracingTier = options[2];
        if (feature(device, 32, options, 8) >= 0) report.MeshShaderTier = options[0];
        var compute = new D3D.QueueDescription { Type = 2 };
        var queueId = D3D.QueueId;
        IntPtr queue;
        if (D3D.Call<D3D.Make>(device, D3D.Device.CreateQueue)(device, &compute, ref queueId, out queue) >= 0)
        {
            report.ComputeQueueCreated = true;
            ulong frequency;
            if (D3D.Call<D3D.Frequency>(queue, 16)(queue, out frequency) >= 0)
                report.ComputeQueueTimestampHz = frequency;
            Marshal.Release(queue);
        }
        return report;
    }

    private static string ReadRegistry(string key, string name)
    {
        try
        {
            using (RegistryKey path = Registry.LocalMachine.OpenSubKey(key))
                return path == null ? null : path.GetValue(name) as string;
        }
        catch (System.Security.SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static int? CountCores()
    {
        uint bytes = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref bytes);
        if (bytes == 0 || bytes > Int32.MaxValue) return null;
        IntPtr memory = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, memory, ref bytes)) return null;
            int count = 0;
            for (int offset = 0; offset < bytes;)
            {
                if (bytes - offset < 8) return null;
                int entrySize = Marshal.ReadInt32(memory, offset + 4);
                if (entrySize < 8 || entrySize > bytes - offset) return null;
                count++;
                offset += entrySize;
            }
            return count;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
