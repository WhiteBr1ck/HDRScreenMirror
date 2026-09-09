#include "LuminanceCommon.hlsli"

cbuffer MirrorConstants : register(b0)
{
    float PaperWhiteNits;
    uint InputMode;
    uint Rotation;
    uint FalseColorMode;
    uint FrameWidth;
    uint FrameHeight;
    int PointerX;
    int PointerY;
    float AblReferencePeakNits;
    uint AblCustomEotfEnabled;
    float AblClipPq;
    float AblPadding;
    float4 AblEotfLut[16];
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
    float ClippedSum;
    float ClippedMinimum;
    float ClippedMaximum;
    uint ClippedPadding;
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
groupshared float SharedClippedSum[256];
groupshared float SharedClippedMinimum[256];
groupshared float SharedClippedMaximum[256];
groupshared float SharedLuminanceTile[400];
groupshared float SharedClippedLuminanceTile[400];
groupshared uint SharedValidityTile[400];

float PqOetfFromNits(float luminanceNits)
{
    const float m1 = 2610.0 / 16384.0;
    const float m2 = 2523.0 / 32.0;
    const float c1 = 3424.0 / 4096.0;
    const float c2 = 2413.0 / 128.0;
    const float c3 = 2392.0 / 128.0;
    float normalized = saturate(max(luminanceNits, 0.0) / 10000.0);
    float power = pow(normalized, m1);
    return pow((c1 + c2 * power) / (1.0 + c3 * power), m2);
}

float LoadAblEotfLut(uint index)
{
    return AblEotfLut[index >> 2][index & 3];
}

float ApplyAblEotf(float luminanceNits)
{
    if (AblCustomEotfEnabled == 0)
        return min(max(luminanceNits, 0.0), max(AblReferencePeakNits, 0.001));
    if (luminanceNits <= 0.0)
        return LoadAblEotfLut(0);

    float pq = PqOetfFromNits(luminanceNits);
    if (pq >= saturate(AblClipPq))
        return max(AblReferencePeakNits, 0.001);
    float position = pq * 63.0;
    uint lowerIndex = min((uint)floor(position), 62u);
    uint upperIndex = lowerIndex + 1;
    float amount = position - lowerIndex;
    return lerp(LoadAblEotfLut(lowerIndex), LoadAblEotfLut(upperIndex), amount);
}

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
        SharedClippedLuminanceTile[tileIndex] = tileValid ? ApplyAblEotf(tileLuminance) : 0.0;
        SharedValidityTile[tileIndex] = tileValid ? 1 : 0;
    }
    GroupMemoryBarrierWithGroupSync();

    bool valid = dispatchThreadId.x < FrameWidth && dispatchThreadId.y < FrameHeight;
    uint centerIndex = (groupThreadId.y + 2) * 20 + groupThreadId.x + 2;
    float luminance = valid ? SharedLuminanceTile[centerIndex] : 0.0;
    float clippedLuminance = valid ? SharedClippedLuminanceTile[centerIndex] : 0.0;
    float regionAverage = 0.0;
    float clippedRegionAverage = 0.0;
    if (valid)
    {
        float regionSum = 0.0;
        float clippedRegionSum = 0.0;
        uint regionCount = 0;
        for (uint y = 0; y < 5; y++)
        {
            for (uint x = 0; x < 5; x++)
            {
                uint tileIndex = (groupThreadId.y + y) * 20 + groupThreadId.x + x;
                regionSum += SharedLuminanceTile[tileIndex];
                clippedRegionSum += SharedClippedLuminanceTile[tileIndex];
                regionCount += SharedValidityTile[tileIndex];
            }
        }
        regionAverage = regionCount > 0 ? regionSum / regionCount : 0.0;
        clippedRegionAverage = regionCount > 0 ? clippedRegionSum / regionCount : 0.0;
    }

    SharedSum[groupIndex] = valid ? luminance : 0.0;
    SharedMinimum[groupIndex] = valid ? regionAverage : 3.402823466e+38;
    SharedMaximum[groupIndex] = valid ? regionAverage : 0.0;
    SharedCount[groupIndex] = valid ? 1 : 0;
    SharedMinimumX[groupIndex] = dispatchThreadId.x;
    SharedMinimumY[groupIndex] = dispatchThreadId.y;
    SharedMaximumX[groupIndex] = dispatchThreadId.x;
    SharedMaximumY[groupIndex] = dispatchThreadId.y;
    SharedClippedSum[groupIndex] = valid ? clippedLuminance : 0.0;
    SharedClippedMinimum[groupIndex] = valid ? clippedRegionAverage : 3.402823466e+38;
    SharedClippedMaximum[groupIndex] = valid ? clippedRegionAverage : 0.0;
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
            SharedClippedSum[groupIndex] += SharedClippedSum[groupIndex + stride];
            SharedClippedMinimum[groupIndex] = min(
                SharedClippedMinimum[groupIndex],
                SharedClippedMinimum[groupIndex + stride]);
            SharedClippedMaximum[groupIndex] = max(
                SharedClippedMaximum[groupIndex],
                SharedClippedMaximum[groupIndex + stride]);
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
        result.ClippedSum = SharedClippedSum[0];
        result.ClippedMinimum = SharedClippedMinimum[0];
        result.ClippedMaximum = SharedClippedMaximum[0];
        result.ClippedPadding = 0;
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
    result.ClippedSum = 0.0;
    result.ClippedMinimum = 3.402823466e+38;
    result.ClippedMaximum = 0.0;
    result.ClippedPadding = 0;

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
            float clippedLuminance = ApplyAblEotf(luminance);
            result.ClippedSum += clippedLuminance;
            result.ClippedMinimum = min(result.ClippedMinimum, clippedLuminance);
            result.ClippedMaximum = max(result.ClippedMaximum, clippedLuminance);
            result.Count++;
        }
    }

    Results[0] = result;
}
