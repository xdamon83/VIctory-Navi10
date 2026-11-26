#ifndef NAVI10_MESH_EXPANSION
#define NAVI10_MESH_EXPANSION

struct ExpandedVertex
{
    float3 position;
    uint normalId;
    float3 color;
    uint material;
};

struct QuadAttributes
{
    float3 color;
    uint materialAndNormal;
};

StructuredBuffer<float3> ExpandedPositions : register(t1);
StructuredBuffer<QuadAttributes> ExpandedQuads : register(t2);
RWStructuredBuffer<float3> OutPositions : register(u2);
RWStructuredBuffer<uint> OutIndices : register(u3);
RWStructuredBuffer<QuadAttributes> OutQuads : register(u4);

static const uint QuadIndices[6] = { 0, 1, 2, 0, 2, 3 };

bool KeepQuad(float4 p0, float4 p1, float4 p2, float4 p3)
{
    float4 low = min(min(p0, p1), min(p2, p3));
    float4 high = max(max(p0, p1), max(p2, p3));
    // This implementation assumes the lab's orthographic clip transform, so w = 1.
    return all(low.xy <= 1.0001) && all(high.xy >= -1.0001) &&
           low.z <= 1.0001 && high.z >= -0.0001;
}

uint ReserveQuads(RWByteAddressBuffer arguments, uint quads)
{
    uint startIndex;
    arguments.InterlockedAdd(0, quads * 6u, startIndex);
    return startIndex / 6u;
}

void WriteQuadAttributes(uint quad, float3 color, uint material, uint normal)
{
    QuadAttributes attributes;
    attributes.color = color;
    attributes.materialAndNormal = (material << 8u) | normal;
    OutQuads[quad] = attributes;
}

ExpandedVertex ReadExpandedVertex(uint id)
{
    QuadAttributes attributes = ExpandedQuads[id / 4u];
    ExpandedVertex vertex;
    vertex.position = ExpandedPositions[id];
    vertex.color = attributes.color;
    vertex.normalId = attributes.materialAndNormal & 255u;
    vertex.material = attributes.materialAndNormal >> 8u;
    return vertex;
}

void WriteQuadIndices(uint quad)
{
    [unroll]
    for (uint i = 0u; i < 6u; i++)
        OutIndices[quad * 6u + i] = quad * 4u + QuadIndices[i];
}

#endif
