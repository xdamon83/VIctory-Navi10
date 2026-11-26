using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal static unsafe class VulkanProbe
{
    private const uint Api11 = (1u << 22) | (1u << 12);
    private const uint Api13 = (1u << 22) | (3u << 12);

    [StructLayout(LayoutKind.Sequential)]
    private struct ApplicationInfo
    {
        internal int Type;
        internal IntPtr Next, Name;
        internal uint Version;
        internal IntPtr Engine;
        internal uint EngineVersion, Api;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceInfo
    {
        internal int Type;
        internal IntPtr Next;
        internal uint Flags;
        internal ApplicationInfo* App;
        internal uint LayerCount;
        internal IntPtr Layers;
        internal uint ExtensionCount;
        internal IntPtr Extensions;
    }

    // Explicit Win64 offsets for the members consumed from the core Vulkan 1.x structures.
    [StructLayout(LayoutKind.Explicit, Size = 1024)]
    private struct Properties
    {
        [FieldOffset(0)] internal uint Api;
        [FieldOffset(4)] internal uint Driver;
        [FieldOffset(8)] internal uint Vendor;
        [FieldOffset(12)] internal uint Device;
        [FieldOffset(20)] internal fixed byte Name[256];
        [FieldOffset(300)] internal uint MaxImage2D;
        [FieldOffset(316)] internal uint MaxTexelBufferElements;
        [FieldOffset(320)] internal uint MaxUniformBufferRange;
        [FieldOffset(324)] internal uint MaxStorageBufferRange;
        [FieldOffset(360)] internal uint MaxBoundDescriptorSets;
        [FieldOffset(372)] internal uint MaxPerStageStorageBuffers;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Properties2
    {
        internal int Type;
        internal void* Next;
        internal Properties Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Features2
    {
        internal int Type;
        internal void* Next;
        internal fixed uint Feature[55];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Extension
    {
        internal fixed byte Name[256];
        internal uint Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Queue
    {
        internal uint Flags, Count, TimestampBits, GranularityX, GranularityY, GranularityZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyChain
    {
        internal int Type;
        internal void* Next;
        internal uint First, Second, Third, Fourth;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FeatureChain
    {
        internal int Type;
        internal void* Next;
        internal uint First, Second, Third, Fourth, Fifth;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Maintenance4
    {
        internal int Type;
        internal void* Next;
        internal ulong MaximumBufferSize;
    }

    [StructLayout(LayoutKind.Explicit, Size = 536)]
    private struct Memory
    {
        [FieldOffset(0)] internal int Type;
        [FieldOffset(8)] internal void* Next;
        [FieldOffset(276)] internal uint HeapCount;
        [FieldOffset(280)] internal fixed ulong Heap[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Budget
    {
        internal int Type;
        internal void* Next;
        internal fixed ulong HeapBudget[16];
        internal fixed ulong HeapUsage[16];
    }

    [DllImport("vulkan-1.dll")] private static extern int vkEnumerateInstanceVersion(out uint version);
    [DllImport("vulkan-1.dll")] private static extern int vkCreateInstance(InstanceInfo* info, IntPtr allocator, out IntPtr instance);
    [DllImport("vulkan-1.dll")] private static extern void vkDestroyInstance(IntPtr instance, IntPtr allocator);
    [DllImport("vulkan-1.dll")] private static extern int vkEnumeratePhysicalDevices(IntPtr instance, uint* count, IntPtr* devices);
    [DllImport("vulkan-1.dll")] private static extern void vkGetPhysicalDeviceProperties(IntPtr gpu, Properties* properties);
    [DllImport("vulkan-1.dll")] private static extern void vkGetPhysicalDeviceProperties2(IntPtr gpu, Properties2* properties);
    [DllImport("vulkan-1.dll")] private static extern void vkGetPhysicalDeviceFeatures2(IntPtr gpu, Features2* features);
    [DllImport("vulkan-1.dll")] private static extern void vkGetPhysicalDeviceQueueFamilyProperties(IntPtr gpu, uint* count, Queue* queues);
    [DllImport("vulkan-1.dll")] private static extern int vkEnumerateDeviceExtensionProperties(IntPtr gpu, IntPtr layer, uint* count, Extension* extensions);
    [DllImport("vulkan-1.dll")] private static extern void vkGetPhysicalDeviceMemoryProperties2(IntPtr gpu, Memory* memory);

    internal static object Read(uint vendor, uint device)
    {
        IntPtr instance = IntPtr.Zero;
        try
        {
            uint loader;
            Check(vkEnumerateInstanceVersion(out loader), "vkEnumerateInstanceVersion");
            if (loader < Api11) return new { Error = "Vulkan 1.1 loader required", Loader = Version(loader) };
            var app = new ApplicationInfo { Api = Math.Min(loader, Api13) };
            var info = new InstanceInfo { Type = 1, App = &app };
            Check(vkCreateInstance(&info, IntPtr.Zero, out instance), "vkCreateInstance");
            uint count = 0;
            Check(vkEnumeratePhysicalDevices(instance, &count, null), "vkEnumeratePhysicalDevices");
            if (count == 0 || count > 64) return new { Error = "Unexpected Vulkan device count", Count = count };
            var devices = new IntPtr[count];
            fixed (IntPtr* values = devices)
                Check(vkEnumeratePhysicalDevices(instance, &count, values), "vkEnumeratePhysicalDevices");
            for (int index = 0; index < count; index++)
            {
                var properties = new Properties();
                vkGetPhysicalDeviceProperties(devices[index], &properties);
                if (properties.Vendor == vendor && properties.Device == device) return ReadAdapter(devices[index], loader, properties);
            }
            return new { Error = "The DXGI adapter was not enumerated by Vulkan", Count = count };
        }
        catch (DllNotFoundException) { return new { Error = "Vulkan loader was not found" }; }
        catch (EntryPointNotFoundException ex) { return new { Error = ex.Message }; }
        catch (InvalidOperationException ex) { return new { Error = ex.Message }; }
        finally { if (instance != IntPtr.Zero) vkDestroyInstance(instance, IntPtr.Zero); }
    }

    private static object ReadAdapter(IntPtr gpu, uint loader, Properties core)
    {
        var extensions = ReadExtensions(gpu);
        bool sizeControl = core.Api >= Api13 || extensions.Contains("VK_EXT_subgroup_size_control");
        bool maintenance4 = core.Api >= Api13 || extensions.Contains("VK_KHR_maintenance4");
        bool meshAvailable = extensions.Contains("VK_EXT_mesh_shader");
        bool raysAvailable = extensions.Contains("VK_KHR_ray_tracing_pipeline");
        bool budgetAvailable = extensions.Contains("VK_EXT_memory_budget");

        var maxBuffer = new Maintenance4 { Type = 1000413001 };
        var waveSize = new PropertyChain { Type = 1000225000 };
        if (maintenance4) waveSize.Next = &maxBuffer;
        var subgroup = new PropertyChain { Type = 1000094000 };
        if (sizeControl) subgroup.Next = &waveSize;
        else if (maintenance4) subgroup.Next = &maxBuffer;
        var properties = new Properties2 { Type = 1000059001, Next = &subgroup };
        vkGetPhysicalDeviceProperties2(gpu, &properties);

        var waveFeature = new FeatureChain { Type = 1000225002 };
        var mesh = new FeatureChain { Type = 1000328000 };
        var rays = new FeatureChain { Type = 1000347000 };
        var features = new Features2 { Type = 1000059000 };
        if (raysAvailable) features.Next = &rays;
        if (meshAvailable) { mesh.Next = features.Next; features.Next = &mesh; }
        if (sizeControl) { waveFeature.Next = features.Next; features.Next = &waveFeature; }
        vkGetPhysicalDeviceFeatures2(gpu, &features);

        var memory = new Memory { Type = 1000059006 };
        var budget = new Budget { Type = 1000237000 };
        if (budgetAvailable) memory.Next = &budget;
        vkGetPhysicalDeviceMemoryProperties2(gpu, &memory);
        var heaps = new List<object>();
        for (int index = 0; index < Math.Min(memory.HeapCount, 16); index++)
            heaps.Add(new
            {
                Heap = index, SizeBytes = memory.Heap[index * 2], DeviceLocal = (memory.Heap[index * 2 + 1] & 1) != 0,
                BudgetBytes = budgetAvailable ? (ulong?)budget.HeapBudget[index] : null,
                UsageBytes = budgetAvailable ? (ulong?)budget.HeapUsage[index] : null
            });

        return new
        {
            Adapter = Marshal.PtrToStringAnsi((IntPtr)core.Name), LoaderVersion = Version(loader),
            ApiVersion = Version(core.Api), DriverVersionRaw = core.Driver, VendorId = core.Vendor, DeviceId = core.Device,
            MeshShader = meshAvailable && mesh.Second != 0, TaskShader = meshAvailable && mesh.First != 0,
            RayTracingPipeline = raysAvailable && rays.First != 0,
            MultiDrawIndirect = features.Feature[9] != 0, DrawIndirectFirstInstance = features.Feature[10] != 0,
            SubgroupSize = subgroup.First, SubgroupStages = subgroup.Second, SubgroupOperations = subgroup.Third,
            SubgroupSizeControl = sizeControl && waveFeature.First != 0,
            MinSubgroupSize = sizeControl ? (uint?)waveSize.First : null, MaxSubgroupSize = sizeControl ? (uint?)waveSize.Second : null,
            RequiredSubgroupSizeStages = sizeControl ? (uint?)waveSize.Fourth : null,
            properties.Device.MaxImage2D, properties.Device.MaxTexelBufferElements, properties.Device.MaxUniformBufferRange,
            properties.Device.MaxStorageBufferRange, properties.Device.MaxBoundDescriptorSets, properties.Device.MaxPerStageStorageBuffers,
            MaxBufferSize = maintenance4 ? (ulong?)maxBuffer.MaximumBufferSize : null,
            Queues = ReadQueues(gpu), Heaps = heaps
        };
    }

    private static HashSet<string> ReadExtensions(IntPtr gpu)
    {
        uint count = 0;
        Check(vkEnumerateDeviceExtensionProperties(gpu, IntPtr.Zero, &count, null), "vkEnumerateDeviceExtensionProperties");
        if (count > 2048) throw new InvalidOperationException("Unexpected Vulkan extension count.");
        var values = new Extension[count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        fixed (Extension* records = values)
        {
            Check(vkEnumerateDeviceExtensionProperties(gpu, IntPtr.Zero, &count, records), "vkEnumerateDeviceExtensionProperties");
            for (int index = 0; index < count; index++)
                names.Add(Marshal.PtrToStringAnsi((IntPtr)records[index].Name));
        }
        return names;
    }

    private static List<object> ReadQueues(IntPtr gpu)
    {
        uint count = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(gpu, &count, null);
        if (count > 128) throw new InvalidOperationException("Unexpected Vulkan queue family count.");
        var values = new Queue[count];
        fixed (Queue* records = values) vkGetPhysicalDeviceQueueFamilyProperties(gpu, &count, records);
        var families = new List<object>();
        for (int index = 0; index < count; index++)
        {
            Queue q = values[index];
            families.Add(new { Family = index, q.Flags, q.Count, q.TimestampBits,
                Graphics = (q.Flags & 1) != 0, Compute = (q.Flags & 2) != 0, Transfer = (q.Flags & 4) != 0 });
        }
        return families;
    }

    private static string Version(uint value)
    {
        return String.Format("{0}.{1}.{2}", (value >> 22) & 127, (value >> 12) & 1023, value & 4095);
    }

    private static void Check(int result, string operation)
    {
        if (result != 0) throw new InvalidOperationException(operation + " returned VkResult " + result);
    }
}
