using System;
using System.IO;

// Portable little-endian exchange format. Pointer-free and independent of the D3D12 COM bindings.
internal sealed class MeshletPacket
{
    internal MeshletInput[] Meshlets;
    internal VertexInput[] Vertices;
    internal PrimitiveInput[] Primitives;
    internal float[] Projection;

    internal static MeshletPacket Read(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream))
        {
            if (reader.ReadUInt32() != 0x314d4e56 || reader.ReadUInt32() != 1)
                throw new InvalidDataException("Unsupported meshlet packet signature or version.");
            int meshes = reader.ReadInt32(), vertices = reader.ReadInt32(), primitives = reader.ReadInt32();
            if (meshes < 1 || meshes > 1000000 || vertices < 1 || vertices > 6000000 ||
                primitives < 1 || primitives > 5000000 ||
                stream.Length != 84L + 20L * meshes + 40L * vertices + 16L * primitives)
                throw new InvalidDataException("Invalid packet length or counts.");
            var packet = new MeshletPacket { Projection = new float[16], Vertices = new VertexInput[vertices],
                Meshlets = new MeshletInput[meshes], Primitives = new PrimitiveInput[primitives] };
            for (int i = 0; i < 16; i++) packet.Projection[i] = Finite(reader);
            uint next = 0;
            for (int i = 0; i < meshes; i++)
            {
                var m = new MeshletInput(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
                uint visible = reader.ReadUInt32();
                if (m.VertexOffset > vertices || m.VertexCount > vertices - m.VertexOffset ||
                    m.PrimitiveOffset != next || m.PrimitiveCount > primitives - next || visible > 1)
                    throw new InvalidDataException("Invalid meshlet range or visibility.");
                next += m.PrimitiveCount;
                packet.Meshlets[i] = m;
                if (visible == 0) packet.Meshlets[i].PrimitiveCount = 0;
            }
            if (next != primitives) throw new InvalidDataException("Incomplete primitive ranges.");
            for (int i = 0; i < vertices; i++)
                packet.Vertices[i] = new VertexInput { X = Finite(reader), Y = Finite(reader), Z = Finite(reader),
                    R = Finite(reader), G = Finite(reader), B = Finite(reader), A = Finite(reader),
                    Nx = Finite(reader), Ny = Finite(reader), Nz = Finite(reader) };
            for (int i = 0; i < primitives; i++)
                packet.Primitives[i] = new PrimitiveInput(reader.ReadUInt32(), reader.ReadUInt32(),
                    reader.ReadUInt32(), reader.ReadUInt32());
            foreach (var m in packet.Meshlets)
                for (uint j = 0; j < m.PrimitiveCount; j++)
                {
                    var tri = packet.Primitives[m.PrimitiveOffset + j];
                    if (tri.A >= m.VertexCount || tri.B >= m.VertexCount || tri.C >= m.VertexCount)
                        throw new InvalidDataException("Invalid local vertex index.");
                }
            return packet;
        }
    }

    private static float Finite(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidDataException("Non-finite packet value.");
        return value;
    }
}
