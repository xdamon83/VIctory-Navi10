using System;
using System.IO;

// Portable little-endian exchange format. Pointer-free and independent of the D3D12 COM bindings.
internal sealed class MeshletPacket
{
    internal MeshletInput[] Meshlets;
    internal VertexInput[] Vertices;
    internal PrimitiveInput[] Primitives;
    internal float[] Projection;
    internal uint Version, GroupsX, GroupsY, GroupsZ;
    internal uint[] DrawTemplate;

    internal static MeshletPacket Read(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream))
        {
            uint magic = reader.ReadUInt32(), version = reader.ReadUInt32();
            if (magic != 0x314d4e56 || (version != 1 && version != 2))
                throw new InvalidDataException("Unsupported meshlet packet signature or version.");
            int meshes = reader.ReadInt32(), vertices = reader.ReadInt32(), primitives = reader.ReadInt32();
            if (meshes < 1 || meshes > 1000000 || vertices < 1 || vertices > 6000000 ||
                primitives < 1 || primitives > 5000000 ||
                stream.Length != (version == 1 ? 84L : 116L) + 20L * meshes + 40L * vertices + 16L * primitives)
                throw new InvalidDataException("Invalid packet length or counts.");
            var packet = new MeshletPacket { Version = version, Projection = new float[16], Vertices = new VertexInput[vertices],
                Meshlets = new MeshletInput[meshes], Primitives = new PrimitiveInput[primitives] };
            for (int i = 0; i < 16; i++) packet.Projection[i] = Finite(reader);
            if (version == 2)
            {
                packet.GroupsX = reader.ReadUInt32(); packet.GroupsY = reader.ReadUInt32(); packet.GroupsZ = reader.ReadUInt32();
                if (packet.GroupsX < 1 || packet.GroupsX > 65535 || packet.GroupsY < 1 || packet.GroupsY > 65535 ||
                    packet.GroupsZ != 1 || (ulong)packet.GroupsX * packet.GroupsY != (ulong)meshes)
                    throw new InvalidDataException("Dispatch must cover exactly one group per meshlet.");
                packet.DrawTemplate = new uint[5];
                for (int i = 0; i < 5; i++)
                {
                    packet.DrawTemplate[i] = reader.ReadUInt32();
                    if (packet.DrawTemplate[i] != (i == 1 ? 1u : 0u))
                        throw new InvalidDataException("Unsupported indexed indirect draw template.");
                }
            }
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
