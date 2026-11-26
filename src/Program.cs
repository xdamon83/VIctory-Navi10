using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class Options
{
    internal int Objects = 10000, Width = 960, Height = 540, Frames = 120, Warmup = 20;
    internal bool Warp, Test, Probe, View, TestTriangles;
    internal bool StabilityTriangles;
    internal string Out;
    internal RenderPath[] Paths = { RenderPath.Direct, RenderPath.Indirect, RenderPath.Expanded };

    internal static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--warp": options.Warp = true; break;
                case "--test": options.Test = true; break;
                case "--test-meshlets": options.TestTriangles = true; break;
                case "--meshlet-stability": options.TestTriangles = options.StabilityTriangles = true; break;
                case "--probe": options.Probe = true; break;
                case "--view": options.View = true; break;
                case "--objects": options.Objects = Number(args[i], args, ref i, 1, 100000); break;
                case "--width": options.Width = Number(args[i], args, ref i, 64, 3840); break;
                case "--height": options.Height = Number(args[i], args, ref i, 64, 2160); break;
                case "--frames": options.Frames = Number(args[i], args, ref i, 1, 50000); break;
                case "--warmup": options.Warmup = Number(args[i], args, ref i, 0, 1000); break;
                case "--out": options.Out = Value(args[i], args, ref i); break;
                case "--path":
                    string mode = Value(args[i], args, ref i);
                    if (mode == "direct") options.Paths = new[] { RenderPath.Direct };
                    else if (mode == "indirect") options.Paths = new[] { RenderPath.Indirect };
                    else if (mode == "expanded") options.Paths = new[] { RenderPath.Expanded };
                    else throw new ArgumentException("Invalid rendering path: " + mode);
                    break;
                default: throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        return options;
    }

    private static string Value(string name, string[] args, ref int index)
    {
        if (++index >= args.Length) throw new ArgumentException("Missing value for " + name);
        return args[index];
    }

    private static int Number(string name, string[] args, ref int index, int min, int max)
    {
        int value;
        if (!Int32.TryParse(Value(name, args, ref index), NumberStyles.None, CultureInfo.InvariantCulture, out value) ||
            value < min || value > max)
            throw new ArgumentException(String.Format("{0} must be between {1} and {2}.", name, min, max));
        return value;
    }
}

internal static class Program
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--help"))
        {
            Console.WriteLine("Navi10 [--probe | --test | --test-meshlets | --meshlet-stability | --view] [--warp] [--objects N] [--width N] [--height N]");
            Console.WriteLine("       [--frames N] [--warmup N] [--path direct|indirect|expanded] [--out DIRECTORY]");
            return 0;
        }
        try
        {
            Options options = Options.Parse(args);
            if (options.TestTriangles && (options.Probe || options.Test || options.View))
                throw new ArgumentException("--test-meshlets is a separate rendering mode.");
            string shader = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                options.TestTriangles ? "TriangleMeshlets.hlsl" : "City.hlsl");
            using (var renderer = new Renderer(options.Width, options.Height,
                options.TestTriangles ? 64 : options.Objects, options.Warp, shader, options.Probe, options.TestTriangles))
            {
                if (options.Probe)
                {
                    var capabilities = new { Direct3D12 = renderer.Hardware,
                        Vulkan = VulkanProbe.Read(renderer.Hardware.VendorId, renderer.Hardware.DeviceId) };
                    if (options.Out != null)
                    {
                        Directory.CreateDirectory(options.Out);
                        WriteJson(Path.Combine(options.Out, "capabilities.json"), capabilities);
                    }
                    Console.WriteLine(Json.Serialize(capabilities));
                    return 0;
                }
                Console.WriteLine(Json.Serialize(renderer.Hardware));
                if (options.View)
                {
                    using (var preview = new Preview(renderer)) System.Windows.Forms.Application.Run(preview);
                    return 0;
                }
                string folder = options.Out ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "runs",
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", Invariant));
                folder = Path.GetFullPath(folder);
                Directory.CreateDirectory(folder);
                if (options.TestTriangles)
                {
                    var report = options.StabilityTriangles
                        ? TriangleFixtures.RunStability(renderer, options.Frames, options.Warmup)
                        : TriangleFixtures.Run(renderer);
                    WriteJson(Path.Combine(folder, "triangle-meshlets.json"), report);
                    if (!report.Passed) return 1;
                }
                else if (options.Test) Test(renderer, folder);
                else Benchmark(renderer, options, folder);
                Console.WriteLine(folder);
                return 0;
            }
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }
    }

    private static void Test(Renderer renderer, string output)
    {
        var camera = View.Default();
        var offset = camera;
        offset.X += 21;
        offset.Yaw += 0.53;
        var empty = camera;
        empty.X = 100000;
        var views = new[] { camera, offset, empty };
        var tests = new List<object>();
        bool passed = true;
        var reference = new byte[renderer.Width * renderer.Height * 4];
        var actual = new byte[reference.Length];
        for (int index = 0; index < views.Length; index++)
        {
            float time = index * 0.77f;
            renderer.Render(views[index], RenderPath.Direct, time, true);
            renderer.ReadPixels(reference);
            string expectedHash = Hash(reference);
            if (index == 0) SavePng(reference, renderer, Path.Combine(output, "direct.png"));
            if (index != 2 && PixelDifference(reference, Blank(reference)) == 0) passed = false;
            foreach (RenderPath path in new[] { RenderPath.Indirect, RenderPath.Expanded })
            {
                FrameStats row = renderer.Render(views[index], path, time, true);
                renderer.ReadPixels(actual);
                int pixels = PixelDifference(reference, actual);
                var check = new { View = index, row.Path, ChangedPixels = pixels, Visible = row.SubmittedMeshlets,
                    row.SubmittedTriangles, row.GeneratedVertices, row.GeneratedIndices,
                    DirectSha256 = expectedHash, ActualSha256 = Hash(actual) };
                tests.Add(check);
                Console.WriteLine(Json.Serialize(check));
                if (index == 0) SavePng(actual, renderer, Path.Combine(output, row.Path + ".png"));
                if (pixels != 0 || (index == 2 ? row.SubmittedMeshlets != 0 : row.SubmittedMeshlets <= 0))
                    passed = false;
            }
        }
        WriteJson(Path.Combine(output, "verification.json"), new { renderer.Hardware, Passed = passed, Checks = tests });
        if (!passed) throw new InvalidDataException("One or more image or geometry checks failed.");
    }

    private static byte[] Blank(byte[] source)
    {
        var blank = new byte[source.Length];
        for (int offset = 0; offset < blank.Length; offset += 4)
            Buffer.BlockCopy(source, 0, blank, offset, 4);
        return blank;
    }

    private static void Benchmark(Renderer renderer, Options options, string output)
    {
        var camera = View.Default();
        int count = options.Paths.Length;
        var rows = new List<FrameStats>(options.Frames * count);
        for (int index = -options.Warmup; index < options.Frames; index++)
        {
            float time = index < 0 ? 0 : index / 60.0f;
            for (int pathIndex = 0; pathIndex < count; pathIndex++)
            {
                RenderPath path = options.Paths[(pathIndex + Math.Abs(index % count)) % count];
                FrameStats row = renderer.Render(camera, path, time, false);
                if (index < 0) continue;
                row.Frame = index;
                rows.Add(row);
            }
            if ((index + 1) % 500 == 0 && index >= 0)
                Console.WriteLine("Frames per path: {0}/{1}", index + 1, options.Frames);
        }
        var summary = options.Paths.Select(path => Summarize(rows.Where(row =>
            row.Path.Equals(path.ToString(), StringComparison.OrdinalIgnoreCase)))).ToArray();
        var result = new
        {
            CapturedUtc = DateTime.UtcNow.ToString("o", Invariant), renderer.Hardware,
            options.Width, options.Height, options.Warmup, options.Objects,
            FramesPerPath = options.Frames, Summary = summary, Frames = rows
        };
        WriteCsv(Path.Combine(output, "frames.csv"), rows);
        WriteJson(Path.Combine(output, "benchmark.json"), result);
        Console.WriteLine(Json.Serialize(summary));
    }

    internal static object Summarize(IEnumerable<FrameStats> source)
    {
        FrameStats[] rows = source.ToArray();
        double[] times = rows.Select(row => row.FrameMs).OrderBy(value => value).ToArray();
        var video = rows.Where(row => row.LocalVideoBytes.HasValue).Select(row => row.LocalVideoBytes).ToArray();
        return new
        {
            Path = rows[0].Path, Count = rows.Length, AverageFps = 1000.0 / times.Average(),
            MinimumFps = 1000.0 / times[times.Length - 1],
            Low1PercentFps = TailFps(times, 0.01), LowPoint1PercentFps = TailFps(times, 0.001),
            AverageFrameMs = times.Average(), P99FrameMs = times[(int)Math.Ceiling(times.Length * 0.99) - 1],
            AverageCpuSubmitMs = rows.Average(row => row.CpuSubmitMs),
            AverageWaitMs = rows.Average(row => row.FrameMs - row.CpuSubmitMs),
            AverageGpuCullMs = rows.Average(row => row.GpuCullMs), AverageGpuRasterMs = rows.Average(row => row.GpuRasterMs),
            AverageSubmittedMeshlets = rows.Average(row => row.SubmittedMeshlets),
            AverageSubmittedTriangles = rows.Average(row => row.SubmittedTriangles),
            AverageGeneratedVertices = rows.Average(row => row.GeneratedVertices),
            AverageGeneratedIndices = rows.Average(row => row.GeneratedIndices),
            PeakWorkingSetBytes = rows.Max(row => row.ProcessWorkingSetBytes),
            PeakLocalVideoBytes = video.Length == 0 ? null : video.Max()
        };
    }

    private static double TailFps(double[] sortedMs, double fraction)
    {
        int count = Math.Max(1, (int)Math.Ceiling(fraction * sortedMs.Length));
        return 1000.0 / sortedMs.Skip(sortedMs.Length - count).Average();
    }

    private static void WriteCsv(string file, IEnumerable<FrameStats> rows)
    {
        using (var writer = new StreamWriter(file, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("frame,path,frame_ms,cpu_submit_ms,cpu_wait_ms,gpu_cull_ms,gpu_raster_ms,total_meshlets,submitted_meshlets,submitted_triangles,generated_vertices,generated_indices,draw_calls,working_set_bytes,local_video_bytes,local_video_budget_bytes");
            foreach (FrameStats row in rows)
                writer.WriteLine(String.Format(Invariant, "{0},{1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7},{8},{9},{10},{11},{12},{13},{14},{15}",
                    row.Frame, row.Path, row.FrameMs, row.CpuSubmitMs, row.FrameMs - row.CpuSubmitMs, row.GpuCullMs,
                    row.GpuRasterMs, row.TotalMeshlets, row.SubmittedMeshlets, row.SubmittedTriangles,
                    row.GeneratedVertices, row.GeneratedIndices, row.DrawCalls,
                    row.ProcessWorkingSetBytes, row.LocalVideoBytes, row.LocalVideoBudgetBytes));
        }
    }

    private static void WriteJson(string file, object result)
    {
        File.WriteAllText(file, Json.Serialize(result), new UTF8Encoding(false));
    }

    private static int PixelDifference(byte[] first, byte[] second)
    {
        int different = 0;
        for (int offset = 0; offset < first.Length; offset += 4)
            if (first[offset] != second[offset] || first[offset + 1] != second[offset + 1] ||
                first[offset + 2] != second[offset + 2] || first[offset + 3] != second[offset + 3]) different++;
        return different;
    }

    private static string Hash(byte[] image)
    {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(image)).Replace("-", "").ToLowerInvariant();
    }

    private static void SavePng(byte[] pixels, Renderer renderer, string file)
    {
        using (var image = new Bitmap(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb))
        {
            CopyToBitmap(pixels, image);
            image.Save(file, ImageFormat.Png);
        }
    }

    internal static unsafe void CopyToBitmap(byte[] rgba, Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            for (int row = 0; row < bitmap.Height; row++)
            {
                byte* dest = (byte*)data.Scan0 + row * data.Stride;
                for (int col = 0; col < bitmap.Width; col++)
                {
                    int source = (row * bitmap.Width + col) * 4;
                    dest[col * 4] = rgba[source + 2];
                    dest[col * 4 + 1] = rgba[source + 1];
                    dest[col * 4 + 2] = rgba[source];
                    dest[col * 4 + 3] = rgba[source + 3];
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
    }
}
