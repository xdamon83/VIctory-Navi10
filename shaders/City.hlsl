#include "MeshExpansion.hlsl"

cbuffer Frame : register(b0)
{
    float4 CameraX;
    float4 CameraY;
    float4 CameraZ;
    uint GridSize;
    uint RenderPath;
    float SimulationTime;
    uint MeshletCount;
};

StructuredBuffer<uint> Visible : register(t0);
RWStructuredBuffer<uint> VisibleOut : register(u0);
RWByteAddressBuffer DrawArguments : register(u1);

struct Meshlet
{
    float3 center;
    float3 halfSize;
    float3 color;
    uint material;
};

float Random(uint value)
{
    value = (value ^ (value >> 16)) * 0x7feb352du;
    value = (value ^ (value >> 15)) * 0x846ca68bu;
    return float((value ^ (value >> 16)) & 65535u) / 65535.0;
}

Meshlet CityObject(uint id)
{
    uint tile = id / 8u;
    uint part = id & 7u;
    float2 block = (float2(tile % GridSize, tile / GridSize) - (GridSize - 1.0) * 0.5) * 10.0;
    float district = Random(tile + 1300u);
    float height = 2.3 + 12.0 * pow(Random(tile + 1u), 2.7);
    float lowerHeight = 1.3 + 4.8 * Random(tile + 410u);
    float3 facade = lerp(float3(0.27, 0.44, 0.57), float3(0.80, 0.66, 0.49), district);
    float roadPosition = -4.1 + 8.2 * frac(SimulationTime * 0.23 + Random(tile + 5000u));
    float3 car = lerp(float3(0.80, 0.25, 0.18), float3(0.20, 0.70, 0.59), district);
    Meshlet result;
    result.material = 0u;
    if (part == 0u)
    {
        // Keep a microscopic gap to avoid coplanar overlap at tile boundaries.
        result.center = float3(block.x, -0.2, block.y);
        result.halfSize = float3(4.9995, 0.2, 4.9995);
        result.color = float3(0.19, 0.23, 0.29);
        result.material = 1u;
    }
    else if (part == 1u)
    {
        result.center = float3(block.x - 1.5, height * 0.5, block.y - 1.3);
        result.halfSize = float3(1.7, height * 0.5, 1.6);
        result.color = facade;
        result.material = 2u;
    }
    else if (part == 2u)
    {
        result.center = float3(block.x + 1.7, lowerHeight * 0.5, block.y + 0.8);
        result.halfSize = float3(1.35, lowerHeight * 0.5, 1.8);
        result.color = facade * float3(0.95, 0.86, 0.88);
        result.material = 2u;
    }
    else if (part == 3u)
    {
        result.center = float3(block.x - 1.5, height + 0.38, block.y - 1.3);
        result.halfSize = float3(1.05, 0.38, 0.92);
        result.color = facade * 0.72;
    }
    else if (part == 4u)
    {
        result.center = float3(block.x - 1.4, 0.76, block.y + 2.45);
        result.halfSize = float3(0.12, 0.76, 0.12);
        result.color = float3(0.48, 0.36, 0.23);
    }
    else if (part == 5u)
    {
        result.center = float3(block.x - 1.4, 1.63, block.y + 2.45);
        result.halfSize = float3(0.70, 0.62, 0.70);
        result.color = lerp(float3(0.24, 0.51, 0.38), float3(0.48, 0.67, 0.31), district);
    }
    else if (part == 6u)
    {
        result.center = float3(block.x + roadPosition, 0.24, block.y + 4.0);
        result.halfSize = float3(0.65, 0.18, 0.29);
        result.color = car;
    }
    else
    {
        result.center = float3(block.x + roadPosition - 0.1, 0.48, block.y + 4.0);
        result.halfSize = float3(0.34, 0.13, 0.24);
        result.color = car * float3(0.58, 0.68, 0.78);
    }
    return result;
}

float4 Project(float3 position)
{
    float4 homogeneous = float4(position, 1.0);
    return float4(dot(CameraX, homogeneous), dot(CameraY, homogeneous), dot(CameraZ, homogeneous), 1.0);
}

bool KeepMeshlet(Meshlet meshlet)
{
    float3 projected = Project(meshlet.center).xyz;
    float3 radius = float3(dot(abs(CameraX.xyz), meshlet.halfSize),
                           dot(abs(CameraY.xyz), meshlet.halfSize),
                           dot(abs(CameraZ.xyz), meshlet.halfSize));
    const float margin = 0.0001;
    return all(abs(projected.xy) - radius.xy <= 1.0 + margin) &&
           projected.z + radius.z >= -margin && projected.z - radius.z <= 1.0 + margin;
}

[numthreads(64, 1, 1)]
void Cull(uint3 tid : SV_DispatchThreadID)
{
    uint id = tid.x;
    if (id >= MeshletCount || !KeepMeshlet(CityObject(id)))
        return;
    uint slot;
    DrawArguments.InterlockedAdd(4, 1u, slot); // D3D12_DRAW_ARGUMENTS.InstanceCount
    VisibleOut[slot] = id;
}

static const float2 Corners[4] = {
    float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, 1)
};
static const float3 Normals[6] = {
    float3(1,0,0), float3(-1,0,0), float3(0,1,0),
    float3(0,-1,0), float3(0,0,1), float3(0,0,-1)
};

struct Vertex
{
    float4 position : SV_Position;
    float3 world : TEXCOORD0;
    nointerpolation float3 normal : TEXCOORD1;
    nointerpolation float3 color : COLOR0;
    nointerpolation uint material : TEXCOORD2;
};

float3 FacePosition(Meshlet meshlet, uint face, uint cornerId)
{
    float3 normal = Normals[face];
    float3 tangent = face < 2u ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 bitangent = cross(normal, tangent);
    float2 corner = Corners[cornerId];
    precise float3 position = meshlet.center + meshlet.halfSize *
        (normal + tangent * corner.x + bitangent * corner.y);
    return position;
}

Vertex RasterVertex(ExpandedVertex source)
{
    Vertex output;
    output.world = source.position;
    output.position = Project(output.world);
    output.normal = Normals[source.normalId];
    output.color = source.color;
    output.material = source.material;
    return output;
}

ExpandedVertex GenerateVertex(Meshlet meshlet, uint face, uint cornerId)
{
    ExpandedVertex vertex;
    vertex.position = FacePosition(meshlet, face, cornerId);
    vertex.normalId = face;
    vertex.color = meshlet.color;
    vertex.material = meshlet.material;
    return vertex;
}

Vertex VS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    [branch] if (RenderPath == 2u)
        return RasterVertex(ReadExpandedVertex(vertexId));
    uint objectId = instanceId;
    [branch] if (RenderPath == 1u)
        objectId = Visible[instanceId];
    return RasterVertex(GenerateVertex(CityObject(objectId), vertexId / 6u, QuadIndices[vertexId % 6u]));
}

[numthreads(64, 1, 1)]
void Expand(uint3 tid : SV_DispatchThreadID)
{
    uint id = tid.x;
    if (id >= MeshletCount)
        return;
    Meshlet meshlet = CityObject(id);
    if (!KeepMeshlet(meshlet))
        return;

    uint keep = 0u;
    [unroll]
    for (uint face = 0u; face < 6u; face++)
    {
        if (KeepQuad(Project(FacePosition(meshlet, face, 0u)),
                     Project(FacePosition(meshlet, face, 1u)),
                     Project(FacePosition(meshlet, face, 2u)),
                     Project(FacePosition(meshlet, face, 3u))))
            keep |= 1u << face;
    }
    if (keep == 0u)
        return;

    uint quad = ReserveQuads(DrawArguments, countbits(keep));
    uint unused;
    DrawArguments.InterlockedAdd(20, 1u, unused); // Nonempty meshlets after primitive culling.
    [unroll]
    for (uint emitFace = 0u; emitFace < 6u; emitFace++)
    {
        if ((keep & (1u << emitFace)) == 0u)
            continue;
        WriteQuadAttributes(quad, meshlet.color, meshlet.material, emitFace);
        [unroll]
        for (uint cornerId = 0u; cornerId < 4u; cornerId++)
            OutPositions[quad * 4u + cornerId] = FacePosition(meshlet, emitFace, cornerId);
        WriteQuadIndices(quad++);
    }
}

float4 PS(Vertex input) : SV_Target
{
    float3 color = input.color;
    if (input.material == 1u)
    {
        float2 tile = abs(frac((input.world.xz + (GridSize % 2u) * 5.0) / 10.0) - 0.5) * 2.0;
        if (max(tile.x, tile.y) < 0.72)
            color *= float3(1.20, 1.17, 1.12);
        if (abs(tile.y - 0.8) < 0.012 && frac(input.world.x * 0.8) < 0.45)
            color = float3(0.65, 0.58, 0.43);
    }
    if (input.material == 2u && abs(input.normal.y) < 0.5)
    {
        float facadeAxis = abs(input.normal.x) > 0.5 ? input.world.z : input.world.x;
        float2 cell = frac(float2(facadeAxis * 1.8, input.world.y * 1.15));
        if (cell.x > 0.22 && cell.x < 0.76 && cell.y > 0.22 && cell.y < 0.75)
            color = color * 0.48 + float3(0.02, 0.045, 0.075);
    }
    float light = 0.57 + 0.43 * saturate(dot(input.normal, float3(-0.31, 0.83, -0.45)));
    float fog = saturate((input.position.z - 0.44) * 0.85);
    return float4(lerp(color * light, float3(0.115, 0.16, 0.22), fog), 1.0);
}
