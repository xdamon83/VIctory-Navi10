struct MeshletInput
{
    uint vertexOffset, vertexCount, primitiveOffset, primitiveCount;
};

struct VertexInput
{
    float3 position;
    float4 color;
    float3 normal;
};

struct PrimitiveInput
{
    uint3 corners;
    uint material;
};

cbuffer Limits : register(b0)
{
    uint MeshletCount, VertexCount, PrimitiveCount, IndexCapacity;
    uint VertexCapacity, PrimitiveCapacity, MaterialCapacity;
};

cbuffer Camera : register(b1)
{
    row_major float4x4 WorldViewProjection;
};

float4 Project(float3 position)
{
    return mul(WorldViewProjection, float4(position, 1));
}

bool OutsideClip(float4 a, float4 b, float4 c)
{
    // A triangle can be rejected only if every corner violates the same plane.
    // Keep a small conservative margin around planes.
    const float epsilon = 0.00001;
    float3 margin = epsilon * max(1.0, float3(abs(a.w), abs(b.w), abs(c.w)));
    return all(float3(a.x + a.w, b.x + b.w, c.x + c.w) < -margin) ||
           all(float3(a.w - a.x, b.w - b.x, c.w - c.x) < -margin) ||
           all(float3(a.y + a.w, b.y + b.w, c.y + c.w) < -margin) ||
           all(float3(a.w - a.y, b.w - b.y, c.w - c.y) < -margin) ||
           all(float3(a.z, b.z, c.z) < -margin) ||
           all(float3(a.w - a.z, b.w - b.z, c.w - c.z) < -margin);
}

StructuredBuffer<MeshletInput> Meshlets : register(t3);
StructuredBuffer<VertexInput> InputVertices : register(t4);
StructuredBuffer<PrimitiveInput> Primitives : register(t5);
StructuredBuffer<VertexInput> RasterVertices : register(t1);
StructuredBuffer<uint> RasterMaterials : register(t6);
RWStructuredBuffer<VertexInput> OutputVertices : register(u2);
RWStructuredBuffer<uint> OutputIndices : register(u3);
RWStructuredBuffer<uint> OutputMaterials : register(u4);
RWByteAddressBuffer Arguments : register(u1);

bool ReserveTriangle(out uint start)
{
    uint capacity = min(IndexCapacity / 3, min(PrimitiveCapacity, MaterialCapacity)) * 3;
    Arguments.InterlockedAdd(0, 0, start);
    [allow_uav_condition]
    while (start <= capacity && capacity - start >= 3)
    {
        uint observed;
        Arguments.InterlockedCompareExchange(0, start, start + 3, observed);
        if (observed == start) return true;
        start = observed;
    }
    return false;
}

[numthreads(1, 1, 1)]
void Expand(uint id : SV_DispatchThreadID)
{
    if (VertexCount <= VertexCapacity && id < VertexCount)
    {
        OutputVertices[id] = InputVertices[id];
        Arguments.InterlockedAdd(28, 1); // vertices written
    }

    if (id >= MeshletCount) return;
    MeshletInput mesh = Meshlets[id];
    if (mesh.vertexOffset > VertexCount || mesh.vertexCount > VertexCount - mesh.vertexOffset ||
        mesh.primitiveOffset > PrimitiveCount || mesh.primitiveCount > PrimitiveCount - mesh.primitiveOffset)
    {
        Arguments.InterlockedAdd(20, 1);
        return;
    }
    for (uint i = 0; i < mesh.primitiveCount; ++i)
    {
        PrimitiveInput primitive = Primitives[mesh.primitiveOffset + i];
        uint3 local = primitive.corners;
        if (any(local >= mesh.vertexCount))
        {
            Arguments.InterlockedAdd(20, 1);
            continue;
        }
        uint3 indices = local + mesh.vertexOffset;
        float4 a = Project(InputVertices[indices.x].position);
        float4 b = Project(InputVertices[indices.y].position);
        float4 c = Project(InputVertices[indices.z].position);
        if (OutsideClip(a, b, c))
            continue;
        Arguments.InterlockedAdd(32, 1); // valid visible triangles requested
        // HLSL 5.1 logical operators may evaluate both operands.
        // Reject vertex overflow before the reservation can change the draw count.
        if (VertexCount > VertexCapacity)
        {
            Arguments.InterlockedAdd(24, 1);
            continue;
        }
        uint start;
        if (!ReserveTriangle(start))
        {
            Arguments.InterlockedAdd(24, 1);
            continue;
        }
        OutputIndices[start] = indices.x;
        OutputIndices[start + 1] = indices.y;
        OutputIndices[start + 2] = indices.z;
        OutputMaterials[start / 3] = primitive.material;
        Arguments.InterlockedAdd(36, 1); // complete triangle records written
    }
}

struct Varyings
{
    float4 position : SV_Position;
    float4 color : COLOR0;
    float3 normal : NORMAL0;
};

Varyings VS(uint id : SV_VertexID)
{
    VertexInput vertex = RasterVertices[id];
    Varyings output;
    output.position = Project(vertex.position);
    output.color = vertex.color;
    output.normal = vertex.normal;
    return output;
}

float4 PS(Varyings input, uint primitiveId : SV_PrimitiveID) : SV_Target
{
    uint material = RasterMaterials[primitiveId];
    float3 tint = float3((material & 1u) != 0, (material & 2u) != 0, (material & 4u) != 0);
    float brightness = 0.35 + 0.65 * saturate(normalize(input.normal).z);
    return float4(input.color.rgb * lerp(0.5, 1.0, tint) * brightness, input.color.a);
}
