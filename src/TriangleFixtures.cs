using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct MeshletInput
{
    internal uint VertexOffset, VertexCount, PrimitiveOffset, PrimitiveCount;

    internal MeshletInput(uint vertexOffset, uint vertexCount, uint primitiveOffset, uint primitiveCount)
    {
        VertexOffset = vertexOffset;
        VertexCount = vertexCount;
        PrimitiveOffset = primitiveOffset;
        PrimitiveCount = primitiveCount;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct VertexInput
{
    internal float X, Y, Z, R, G, B, A;
    internal float Nx, Ny, Nz;

    internal VertexInput(float x, float y, float r, float g, float b)
    {
        X = x; Y = y; Z = 0.5f;
        R = r; G = g; B = b; A = 1;
        Nx = r - b; Ny = g - r; Nz = 0.8f;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PrimitiveInput
{
    internal uint A, B, C, Material;

    internal PrimitiveInput(uint a, uint b, uint c, uint material)
    {
        A = a; B = b; C = c; Material = material;
    }
}

internal sealed class TriangleResult
{
    internal byte[] Pixels, Reference;
    public uint[] Indices { get; set; }
    public uint[] Materials { get; set; }
    public uint Invalid { get; set; }
    public uint Dropped { get; set; }
    public uint RequestedVertices { get; set; }
    public uint AcceptedVertices { get; set; }
    public uint WrittenVertices { get; set; }
    public uint RequestedTriangles { get; set; }
    public uint AcceptedTriangles { get; set; }
    public uint WrittenTriangles { get; set; }
    public bool ArgumentsValid { get; set; }
    public bool VerticesValid { get; set; }
    public bool IndexGuardIntact { get; set; }
    public bool MaterialGuardIntact { get; set; }
    public double GpuComputeMs { get; set; }
    public double GpuRasterMs { get; set; }
}

internal static class TriangleFixtures
{
    private static readonly float[] Identity =
    {
        1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1
    };

    internal sealed class Report
    {
        public bool Passed { get; set; }
        public HardwareReport Hardware { get; set; }
        public List<object> Cases { get; set; }
        public bool MaterialChangesImage { get; set; }
        public bool NormalChangesImage { get; set; }
        public int ThreadGroupSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Warmup { get; set; }
    }

    internal static Report Run(Renderer renderer)
    {
        var input = new[]
        {
            new VertexInput(-0.90f, -0.50f, 1, 0, 0), new VertexInput(-0.35f, -0.55f, 0, 1, 0),
            new VertexInput(-0.60f,  0.50f, 0, 0, 1), new VertexInput( 0.00f,  0.65f, 1, 1, 0),
            new VertexInput( 0.20f, -0.35f, 1, 0, 1), new VertexInput( 1.20f, -0.38f, 0, 1, 1),
            new VertexInput( 0.52f,  0.75f, 1, 1, 1),
            new VertexInput( 1.40f, -0.36f, 1, 0, 0), new VertexInput( 1.90f, -0.40f, 0, 1, 0),
            new VertexInput( 1.50f,  0.80f, 0, 0, 1)
        };
        var primitives = new[]
        {
            new PrimitiveInput(2, 0, 1, 1), new PrimitiveInput(2, 1, 3, 0x12345672),
            new PrimitiveInput(1, 2, 0, 3), new PrimitiveInput(0, 2, 1, 4),
            new PrimitiveInput(0, 2, UInt32.MaxValue, 5)
        };
        var single = new MeshletInput(0, 3, 0, 1);
        var shared = new MeshletInput(0, 4, 0, 2);
        var clipped = new MeshletInput(4, 3, 2, 1);
        var outside = new MeshletInput(7, 3, 3, 1);
        var empty = new MeshletInput(10, 0, 5, 0);
        var invalid = new MeshletInput(7, 3, 4, 1);

        var report = NewReport(renderer, 0);
        Verify(report, renderer, "empty-input", new MeshletInput[0],
            new VertexInput[0], new PrimitiveInput[0], 0, 0, 0, 0);
        Verify(report, renderer, "single", new[] { single }, input, primitives, 10, 3, 0, 0, 2, 0, 1, 1);
        var mixedMeshlets = new[] { shared, clipped, outside, empty };
        byte[] mixed = Verify(report, renderer, "shared-variable-partially-clipped", mixedMeshlets,
            input, primitives, 10, 9, 0, 0, 2, 0, 1, 1, 2, 1, 3, 0x12345672, 5, 6, 4, 3);
        var sameMaterial = (PrimitiveInput[])primitives.Clone();
        sameMaterial[1].Material = sameMaterial[0].Material;
        byte[] uniformMaterialImage = Verify(report, renderer, "shared-same-material", mixedMeshlets,
            input, sameMaterial, 10, 9, 0, 0, 2, 0, 1, 1, 2, 1, 3, 1, 5, 6, 4, 3);
        var normals = (VertexInput[])input.Clone();
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i].Nx = 0; normals[i].Ny = 0; normals[i].Nz = 1;
        }
        byte[] uniformNormalImage = Verify(report, renderer, "uniform-normals", mixedMeshlets,
            normals, primitives, 10, 9, 0, 0, 2, 0, 1, 1, 2, 1, 3, 0x12345672, 5, 6, 4, 3);
        report.MaterialChangesImage = !mixed.SequenceEqual(uniformMaterialImage);
        report.NormalChangesImage = !mixed.SequenceEqual(uniformNormalImage);
        report.Passed &= report.MaterialChangesImage && report.NormalChangesImage;
        Verify(report, renderer, "empty-and-offscreen", new[] { empty, outside }, input, primitives, 10, 6, 0, 0);
        Verify(report, renderer, "bad-indices-and-spans",
            new[] { invalid, new MeshletInput(UInt32.MaxValue, 1, 0, 1), new MeshletInput(0, 3, 4, 2), single },
            input, primitives, 10, 3, 3, 0, 2, 0, 1, 1);
        Verify(report, renderer, "zero-capacities", new[] { single }, input, primitives, 0, 0, 0, 1);
        Verify(report, renderer, "zero-index-capacity", new[] { single }, input, primitives, 10, 0, 0, 1);
        Verify(report, renderer, "vertex-overflow", new[] { single }, input, primitives, 9, 3, 0, 1);
        Verify(report, renderer, "zero-primitive-capacity", new[] { single }, input, primitives, 10, 3, 0, 1,
            new uint[0], primitiveLimit: 0);
        Verify(report, renderer, "zero-material-capacity", new[] { single }, input, primitives, 10, 3, 0, 1,
            new uint[0], materialLimit: 0);

        var contenders = Enumerable.Repeat(single, 128).ToArray();
        for (int repeat = 0; repeat < 16; repeat++)
            Verify(report, renderer, "concurrent-index-overflow-" + repeat, contenders,
                input, primitives, 10, 5, 0, 127, 2, 0, 1, 1);
        VerifyPerspective(report, renderer);
        return report;
    }

    internal static Report RunStability(Renderer renderer, int frames, int warmup)
    {
        var report = NewReport(renderer, warmup);
        var vertices = new[]
        {
            new VertexInput(-0.7f, -0.6f, 1, 0, 0), new VertexInput(0.7f, -0.6f, 0, 1, 0),
            new VertexInput(0, 0.7f, 0, 0, 1)
        };
        var meshlets = Enumerable.Repeat(new MeshletInput(0, 3, 0, 1), 128).ToArray();
        var primitives = new[] { new PrimitiveInput(0, 1, 2, 7) };
        for (int frame = -warmup; frame < frames; frame++)
        {
            Verify(report, renderer, "stability-" + frame, meshlets, vertices, primitives, 3, 5, 0, 127, 0, 1, 2, 7);
            if (frame < 0) report.Cases.Clear();
            if (!report.Passed) break;
        }
        return report;
    }

    private static Report NewReport(Renderer renderer, int warmup)
    {
        return new Report { Passed = true, Hardware = renderer.Hardware, Cases = new List<object>(),
            ThreadGroupSize = renderer.TriangleGroupSize, Width = renderer.Width, Height = renderer.Height, Warmup = warmup };
    }

    private static void VerifyPerspective(Report report, Renderer renderer)
    {
        // World +0.25 X, camera +0.25 X (net zero) and camera -0.5 Z.
        // Left-handed 90-degree perspective, with near 1 and far 8.
        const float depthScale = 8f / 7f;
        var matrix = new[]
        {
            (float)renderer.Height / renderer.Width, 0, 0, 0,
            0, 1f, 0, 0,
            0, 0, depthScale, -0.5f * depthScale,
            0, 0, 1f, 0.5f
        };
        PerspectiveCase(report, renderer, matrix, "perspective-inside", 2, 2, 3, 0, true, true);
        PerspectiveCase(report, renderer, matrix, "perspective-near-crossing", 2, 2, 0.25f, 0, true, true);
        PerspectiveCase(report, renderer, matrix, "perspective-far-crossing", 2, 2, 9, 0, true, true);
        PerspectiveCase(report, renderer, matrix, "perspective-side-crossing", 2, 2, 3, 10, true, true);
        PerspectiveCase(report, renderer, matrix, "perspective-crossing-eye", 2, 2, -1, 0, true, true);
        PerspectiveCase(report, renderer, matrix, "perspective-behind-eye", -2, -2, -2, 0, false, false);
        PerspectiveCase(report, renderer, matrix, "perspective-before-near", 0, 0, 0.25f, 0, false, false);
        PerspectiveCase(report, renderer, matrix, "perspective-after-far", 9, 9, 10, 0, false, false);
    }

    private static void PerspectiveCase(Report report, Renderer renderer, float[] matrix, string name,
        float z0, float z1, float z2, float x2, bool retained, bool visible)
    {
        var vertices = new[]
        {
            new VertexInput(-1, -0.6f, 1, 0, 0), new VertexInput(1, -0.6f, 0, 1, 0),
            new VertexInput(x2, 1, 0, 0, 1)
        };
        vertices[0].Z = z0; vertices[1].Z = z1; vertices[2].Z = z2;
        var gpu = RunCase(renderer, name, new[] { new MeshletInput(0, 3, 0, 1) }, vertices,
            new[] { new PrimitiveInput(0, 1, 2, 7) }, 3, 3, vertices, new[] { 7u }, matrix);
        bool images = gpu.Pixels.SequenceEqual(gpu.Reference);
        bool coverage = HasCoverage(gpu.Reference);
        bool geometry = retained
            ? gpu.Indices.SequenceEqual(new[] { 0u, 1u, 2u }) && gpu.Materials.SequenceEqual(new[] { 7u })
            : gpu.Indices.Length == 0 && gpu.Materials.Length == 0;
        bool passed = images && coverage == visible && geometry && OutputValid(gpu) && gpu.Invalid == 0 && gpu.Dropped == 0;
        report.Cases.Add(new { Name = name, Passed = passed, ImagesMatch = images, HasCoverage = coverage,
            ExpectedCoverage = visible, ExpectedRetained = retained, Gpu = gpu });
        report.Passed &= passed;
    }

    private static bool HasCoverage(byte[] pixels)
    {
        // The first pixel is outside every perspective fixture.
        for (int i = 4; i < pixels.Length; i += 4)
            if (pixels[i] != pixels[0] || pixels[i + 1] != pixels[1] || pixels[i + 2] != pixels[2])
                return true;
        return false;
    }

    private static byte[] Verify(Report report, Renderer renderer, string name, MeshletInput[] meshlets,
        VertexInput[] input, PrimitiveInput[] primitives, int vertexCapacity, int indexCapacity,
        uint invalid, uint dropped, params uint[] expected)
    {
        return Verify(report, renderer, name, meshlets, input, primitives, vertexCapacity, indexCapacity,
            invalid, dropped, expected, null, null);
    }

    private static byte[] Verify(Report report, Renderer renderer, string name, MeshletInput[] meshlets,
        VertexInput[] input, PrimitiveInput[] primitives, int vertexCapacity, int indexCapacity,
        uint invalid, uint dropped, uint[] expected, int? primitiveLimit = null, int? materialLimit = null)
    {
        var expectedIndices = expected.Where((word, i) => i % 4 != 3).ToArray();
        var expectedMaterials = expected.Where((word, i) => i % 4 == 3).ToArray();
        var direct = expectedIndices.Select(i => input[i]).ToArray();
        var result = RunCase(renderer, name, meshlets, input, primitives, vertexCapacity, indexCapacity,
            direct, expectedMaterials, Identity, primitiveLimit, materialLimit);
        bool indices = result.Indices.Length % 3 == 0 &&
            SortTriangles(result.Indices, null).SequenceEqual(SortTriangles(expectedIndices, null));
        bool materials = indices && SortTriangles(result.Indices, result.Materials)
            .SequenceEqual(SortTriangles(expectedIndices, expectedMaterials));
        bool images = result.Pixels.SequenceEqual(result.Reference);
        bool passed = materials && images && OutputValid(result) &&
            result.Invalid == invalid && result.Dropped == dropped;
        report.Cases.Add(new { Name = name, Passed = passed, VertexCapacity = vertexCapacity, IndexCapacity = indexCapacity,
            PrimitiveCapacity = primitiveLimit ?? indexCapacity / 3, MaterialCapacity = materialLimit ?? indexCapacity / 3,
            ExpectedInvalid = invalid, ExpectedDropped = dropped, IndicesMatch = indices,
            MaterialsMatch = materials, ImagesMatch = images, Gpu = result });
        report.Passed &= passed;
        return result.Pixels;
    }

    private static bool OutputValid(TriangleResult result)
    {
        return result.ArgumentsValid && result.VerticesValid && result.IndexGuardIntact && result.MaterialGuardIntact &&
            result.WrittenVertices == result.AcceptedVertices && result.WrittenTriangles == result.AcceptedTriangles &&
            result.RequestedTriangles == result.AcceptedTriangles + result.Dropped;
    }

    private static TriangleResult RunCase(Renderer renderer, string name, MeshletInput[] meshlets, VertexInput[] input,
        PrimitiveInput[] primitives, int vertexCapacity, int indexCapacity, VertexInput[] reference,
        uint[] materials, float[] matrix, int? primitiveLimit = null, int? materialLimit = null)
    {
        try { return renderer.RenderTriangles(meshlets, input, primitives, vertexCapacity, indexCapacity,
            reference, materials, matrix, primitiveLimit, materialLimit); }
        catch (Exception error) { throw new InvalidOperationException("Triangle fixture failed: " + name, error); }
    }

    private static IEnumerable<Tuple<uint, uint, uint, uint>> SortTriangles(uint[] indices, uint[] materials)
    {
        var triangles = new List<Tuple<uint, uint, uint, uint>>();
        for (int i = 0; i < indices.Length; i += 3)
            triangles.Add(Tuple.Create(indices[i], indices[i + 1], indices[i + 2], materials == null ? 0u : materials[i / 3]));
        triangles.Sort();
        return triangles;
    }
}
