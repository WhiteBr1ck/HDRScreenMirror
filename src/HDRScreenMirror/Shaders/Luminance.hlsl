#include "LuminanceCommon.hlsli"

cbuffer MirrorConstants : register(b0)
{
    float PaperWhiteNits;
    uint InputMode;
    uint Rotation;
    uint FalseColorEnabled;
    uint FrameWidth;
    uint FrameHeight;
    int PointerX;
    int PointerY;
};

Texture2D<float4> SourceTexture : register(t0);

struct LuminanceStats
{
    float Sum;
    float Minimum;
    float Maximum;
    uint Count;
    uint MinimumX;
    uint MinimumY;
    uint MaximumX;
    uint MaximumY;
};

RWStructuredBuffer<LuminanceStats> Results : register(u0);

groupshared float SharedSum[256];
groupshared float SharedMinimum[256];
groupshared float SharedMaximum[256];
groupshared uint SharedCount[256];
groupshared uint SharedMinimumX[256];
groupshared uint SharedMinimumY[256];
groupshared uint SharedMaximumX[256];
groupshared uint SharedMaximumY[256];
groupshared float SharedLuminanceTile[400];
groupshared uint SharedValidityTile[400];

[numthreads(16, 16, 1)]
void CSFrameStats(
    uint3 dispatchThreadId : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 groupThreadId : SV_GroupThreadID,
    uint groupIndex : SV_GroupIndex)
{
    for (uint tileIndex = groupIndex; tileIndex < 400; tileIndex += 256)
    {
        int tileX = tileIndex % 20;
        int tileY = tileIndex / 20;
        int2 sourcePosition = int2(groupId.xy * 16) + int2(tileX - 2, tileY - 2);
        bool tileValid = sourcePosition.x >= 0 && sourcePosition.y >= 0 &&
                         sourcePosition.x < (int)FrameWidth && sourcePosition.y < (int)FrameHeight;
        float tileLuminance = 0.0;
        if (tileValid)
        {
            float4 source = SourceTexture.Load(int3(sourcePosition, 0));
            tileLuminance = CalculateLuminanceNits(
                DecodeInputToNits(source, InputMode, PaperWhiteNits),
                InputMode);
        }
        SharedLuminanceTile[tileIndex] = tileLuminance;
        SharedValidityTile[tileIndex] = tileValid ? 1 : 0;
    }
    GroupMemoryBarrierWithGroupSync();

    bool valid = dispatchThreadId.x < FrameWidth && dispatchThreadId.y < FrameHeight;
    uint centerIndex = (groupThreadId.y + 2) * 20 + groupThreadId.x + 2;
    float luminance = valid ? SharedLuminanceTile[centerIndex] : 0.0;
    float regionAverage = 0.0;
    if (valid)
    {
        float regionSum = 0.0;
        uint regionCount = 0;
        for (uint y = 0; y < 5; y++)
        {
            for (uint x = 0; x < 5; x++)
            {
                uint tileIndex = (groupThreadId.y + y) * 20 + groupThreadId.x + x;
                regionSum += SharedLuminanceTile[tileIndex];
                regionCount += SharedValidityTile[tileIndex];
            }
        }
        regionAverage = regionCount > 0 ? regionSum / regionCount : 0.0;
    }

    SharedSum[groupIndex] = valid ? luminance : 0.0;
    SharedMinimum[groupIndex] = valid ? regionAverage : 3.402823466e+38;
    SharedMaximum[groupIndex] = valid ? regionAverage : 0.0;
    SharedCount[groupIndex] = valid ? 1 : 0;
    SharedMinimumX[groupIndex] = dispatchThreadId.x;
    SharedMinimumY[groupIndex] = dispatchThreadId.y;
    SharedMaximumX[groupIndex] = dispatchThreadId.x;
    SharedMaximumY[groupIndex] = dispatchThreadId.y;
    GroupMemoryBarrierWithGroupSync();

    for (uint stride = 128; stride > 0; stride >>= 1)
    {
        if (groupIndex < stride)
        {
            SharedSum[groupIndex] += SharedSum[groupIndex + stride];
            if (SharedMinimum[groupIndex + stride] < SharedMinimum[groupIndex])
            {
                SharedMinimum[groupIndex] = SharedMinimum[groupIndex + stride];
                SharedMinimumX[groupIndex] = SharedMinimumX[groupIndex + stride];
                SharedMinimumY[groupIndex] = SharedMinimumY[groupIndex + stride];
            }
            if (SharedMaximum[groupIndex + stride] > SharedMaximum[groupIndex])
            {
                SharedMaximum[groupIndex] = SharedMaximum[groupIndex + stride];
                SharedMaximumX[groupIndex] = SharedMaximumX[groupIndex + stride];
                SharedMaximumY[groupIndex] = SharedMaximumY[groupIndex + stride];
            }
            SharedCount[groupIndex] += SharedCount[groupIndex + stride];
        }
        GroupMemoryBarrierWithGroupSync();
    }

    if (groupIndex == 0)
    {
        uint groupCountX = (FrameWidth + 15) / 16;
        uint resultIndex = groupId.y * groupCountX + groupId.x;
        LuminanceStats result;
        result.Sum = SharedSum[0];
        result.Minimum = SharedMinimum[0];
        result.Maximum = SharedMaximum[0];
        result.Count = SharedCount[0];
        result.MinimumX = SharedMinimumX[0];
        result.MinimumY = SharedMinimumY[0];
        result.MaximumX = SharedMaximumX[0];
        result.MaximumY = SharedMaximumY[0];
        Results[resultIndex] = result;
    }
}

[numthreads(1, 1, 1)]
void CSPointerProbe(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    LuminanceStats result;
    result.Sum = 0.0;
    result.Minimum = 3.402823466e+38;
    result.Maximum = 0.0;
    result.Count = 0;
    result.MinimumX = 0;
    result.MinimumY = 0;
    result.MaximumX = 0;
    result.MaximumY = 0;

    for (int y = -2; y <= 2; y++)
    {
        for (int x = -2; x <= 2; x++)
        {
            int2 position = int2(PointerX + x, PointerY + y);
            if (position.x < 0 || position.y < 0 ||
                position.x >= (int)FrameWidth || position.y >= (int)FrameHeight)
                continue;

            float4 source = SourceTexture.Load(int3(position, 0));
            float luminance = CalculateLuminanceNits(
                DecodeInputToNits(source, InputMode, PaperWhiteNits),
                InputMode);
            result.Sum += luminance;
            result.Minimum = min(result.Minimum, luminance);
            result.Maximum = max(result.Maximum, luminance);
            result.Count++;
        }
    }

    Results[0] = result;
}
