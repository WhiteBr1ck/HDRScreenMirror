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
    float AblReferencePeakNits;
    float3 AblPadding;
};

Texture2D<float4> SourceTexture : register(t0);
RWStructuredBuffer<uint> GamutData : register(u0);

static const uint HistogramWidth = 128;
static const uint HistogramHeight = 128;
static const uint HistogramCount = HistogramWidth * HistogramHeight;
static const uint CounterCount = 5;
static const uint SliceCount = 8;
static const uint SliceStride = HistogramCount + CounterCount;
static const uint TotalElementCount = SliceCount * SliceStride;
static const float DiagramMaximum = 0.65;
static const float MinimumGamutLuminanceNits = 0.01;

float3 Rec709ToDciP3(float3 value)
{
    return float3(
        0.822461962 * value.r + 0.177538037 * value.g,
        0.0331941992 * value.r + 0.966805815 * value.g,
        0.0170826315 * value.r + 0.0723974406 * value.g + 0.910519957 * value.b);
}

float3 Rec709ToRec2020(float3 value)
{
    return float3(
        0.627403914 * value.r + 0.329283028 * value.g + 0.0433130674 * value.b,
        0.0690972879 * value.r + 0.919540405 * value.g + 0.0113623151 * value.b,
        0.0163914393 * value.r + 0.0880133062 * value.g + 0.895595252 * value.b);
}

float3 Rec2020ToDciP3(float3 value)
{
    return float3(
         1.34357821 * value.r - 0.282179683 * value.g - 0.0613985806 * value.b,
        -0.0652974545 * value.r + 1.07578790 * value.g - 0.0104904631 * value.b,
         0.00282178726 * value.r - 0.0195984952 * value.g + 1.01677668 * value.b);
}

float3 Rec709ToXYZ(float3 value)
{
    return float3(
        0.412390798 * value.r + 0.357584327 * value.g + 0.180480793 * value.b,
        0.212639003 * value.r + 0.715168654 * value.g + 0.0721923187 * value.b,
        0.0193308182 * value.r + 0.119194783 * value.g + 0.950532138 * value.b);
}

float3 Rec2020ToXYZ(float3 value)
{
    return float3(
        0.636958062 * value.r + 0.144616901 * value.g + 0.168880969 * value.b,
        0.262700200 * value.r + 0.677998065 * value.g + 0.0593017153 * value.b,
        0.0280726924 * value.g + 1.06098508 * value.b);
}

float3 DecodeLinearRgb(float4 source)
{
    if (InputMode == 0)
        return source.rgb;
    if (InputMode == 2)
    {
        return float3(
            PqEotf(source.r),
            PqEotf(source.g),
            PqEotf(source.b)) / 10000.0;
    }
    return SrgbToLinear(saturate(source.rgb));
}

bool IsInsideGamut(float3 value)
{
    return all(value >= -0.0001);
}

[numthreads(256, 1, 1)]
void CSClearGamut(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (dispatchThreadId.x < TotalElementCount)
        GamutData[dispatchThreadId.x] = 0;
}

[numthreads(16, 16, 1)]
void CSAnalyzeGamut(uint3 dispatchThreadId : SV_DispatchThreadID, uint3 groupId : SV_GroupID)
{
    uint2 sourcePosition = dispatchThreadId.xy * 2;
    if (sourcePosition.x >= FrameWidth || sourcePosition.y >= FrameHeight)
        return;

    float4 source = SourceTexture.Load(int3(sourcePosition, 0));
    float luminance = CalculateLuminanceNits(
        DecodeInputToNits(source, InputMode, PaperWhiteNits),
        InputMode);
    if (luminance < MinimumGamutLuminanceNits)
        return;

    float3 linearRgb = DecodeLinearRgb(source);
    float3 rec709 = InputMode == 2 ? Rec2020ToRec709(linearRgb) : linearRgb;
    float3 dciP3 = InputMode == 2 ? Rec2020ToDciP3(linearRgb) : Rec709ToDciP3(linearRgb);
    float3 rec2020 = InputMode == 2 ? linearRgb : Rec709ToRec2020(linearRgb);

    uint category = IsInsideGamut(rec709) ? 0 :
                    IsInsideGamut(dciP3) ? 1 :
                    IsInsideGamut(rec2020) ? 2 : 3;
    uint slice = (groupId.x + groupId.y * 131) & (SliceCount - 1);
    uint sliceOffset = slice * SliceStride;
    InterlockedAdd(GamutData[sliceOffset + HistogramCount + category], 1);
    InterlockedAdd(GamutData[sliceOffset + HistogramCount + 4], 1);

    float3 xyz = InputMode == 2 ? Rec2020ToXYZ(linearRgb) : Rec709ToXYZ(linearRgb);
    float denominator = xyz.x + 15.0 * xyz.y + 3.0 * xyz.z;
    if (denominator <= 1e-8)
        return;

    float2 uv = float2(4.0 * xyz.x, 9.0 * xyz.y) / denominator;
    if (any(uv < 0.0) || any(uv > DiagramMaximum))
        return;

    uint2 bin = min(
        uint2(uv / DiagramMaximum * float2(HistogramWidth, HistogramHeight)),
        uint2(HistogramWidth - 1, HistogramHeight - 1));
    InterlockedAdd(GamutData[sliceOffset + bin.y * HistogramWidth + bin.x], 1);
}
