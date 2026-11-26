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
    public bool ArgumentsValid { get; set; }
    public bool VerticesValid { get; set; }
    public bool IndexGuardIntact { get; set; }
    public bool MaterialGuardIntact { get; set; }
    public double GpuComputeMs { get; set; }
    public double GpuRasterMs { get; set; }
}

internal static class TriangleFixtures
{
    internal sealed class Report
    {
        public bool Passed { get; set; }
        public HardwareReport Hardware { get; set; }
        public List<object> Cases { get; set; }
        public bool MaterialChangesImage { get; set; }
        public bool NormalChangesImage { get; set; }
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

        var report = new Report { Passed = true, Hardware = renderer.Hardware, Cases = new List<object>() };
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

        var contenders = Enumerable.Repeat(single, 128).ToArray();
        for (int repeat = 0; repeat < 16; repeat++)
            Verify(report, renderer, "concurrent-index-overflow-" + repeat, contenders,
                input, primitives, 10, 5, 0, 127, 2, 0, 1, 1);
        return report;
    }

    private static byte[] Verify(Report report, Renderer renderer, string name, MeshletInput[] meshlets,
        VertexInput[] input, PrimitiveInput[] primitives, int vertexCapacity, int indexCapacity,
        uint invalid, uint dropped, params uint[] expected)
    {
        var expectedIndices = expected.Where((word, i) => i % 4 != 3).ToArray();
        var expectedMaterials = expected.Where((word, i) => i % 4 == 3).ToArray();
        var direct = expectedIndices.Select(i => input[i]).ToArray();
        TriangleResult result;
        try { result = renderer.RenderTriangles(meshlets, input, primitives, vertexCapacity, indexCapacity, direct, expectedMaterials); }
        catch (Exception error) { throw new InvalidOperationException("Triangle fixture failed: " + name, error); }
        bool indices = result.Indices.Length % 3 == 0 &&
            SortTriangles(result.Indices, null).SequenceEqual(SortTriangles(expectedIndices, null));
        bool materials = indices && SortTriangles(result.Indices, result.Materials)
            .SequenceEqual(SortTriangles(expectedIndices, expectedMaterials));
        bool images = result.Pixels.SequenceEqual(result.Reference);
        bool passed = materials && images && result.ArgumentsValid && result.VerticesValid &&
            result.IndexGuardIntact && result.MaterialGuardIntact &&
            result.Invalid == invalid && result.Dropped == dropped;
        report.Cases.Add(new { Name = name, Passed = passed, VertexCapacity = vertexCapacity, IndexCapacity = indexCapacity,
            ExpectedInvalid = invalid, ExpectedDropped = dropped, IndicesMatch = indices,
            MaterialsMatch = materials, ImagesMatch = images, Gpu = result });
        report.Passed &= passed;
        return result.Pixels;
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
