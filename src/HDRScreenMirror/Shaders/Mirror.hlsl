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

#include "LuminanceCommon.hlsli"

Texture2D<float4> SourceTexture : register(t0);
Texture2D<float4> CursorTexture : register(t1);
SamplerState LinearSampler : register(s0);

cbuffer CursorConstants : register(b1)
{
    float4 CursorRect;
    float2 OutputSize;
    float2 CursorPadding;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    VertexOutput output;
    float2 position = float2((vertexId << 1) & 2, vertexId & 2);
    output.UV = position;
    output.Position = float4(position * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return output;
}

float2 RotateUV(float2 uv)
{
    if (Rotation == 1)
        return float2(uv.y, 1.0 - uv.x);
    if (Rotation == 2)
        return 1.0 - uv;
    if (Rotation == 3)
        return float2(1.0 - uv.y, uv.x);
    return uv;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float4 source = SourceTexture.Sample(LinearSampler, RotateUV(input.UV));
    float3 rgbNits = DecodeInputToNits(source, InputMode, PaperWhiteNits);

    if (FalseColorEnabled != 0)
    {
        float luminanceNits = CalculateLuminanceNits(rgbNits, InputMode);
        float3 falseColorSrgb = MapNitsToFalseColor(luminanceNits);
        return float4(SrgbToLinear(falseColorSrgb) * (PaperWhiteNits / 80.0), 1.0);
    }

    float3 scRgb;

    if (InputMode == 0)
    {
        scRgb = source.rgb;
    }
    else if (InputMode == 2)
    {
        scRgb = Rec2020ToRec709(rgbNits) / 80.0;
    }
    else
    {
        scRgb = SrgbToLinear(saturate(source.rgb)) * (PaperWhiteNits / 80.0);
    }

    return float4(scRgb, 1.0);
}

VertexOutput CursorVSMain(uint vertexId : SV_VertexID)
{
    VertexOutput output;
    float2 uv = float2((vertexId == 1 || vertexId == 3) ? 1.0 : 0.0,
                       (vertexId >= 2) ? 1.0 : 0.0);
    float2 pixelPosition = CursorRect.xy + uv * CursorRect.zw;
    float2 ndc = float2(
        pixelPosition.x / OutputSize.x * 2.0 - 1.0,
        1.0 - pixelPosition.y / OutputSize.y * 2.0);
    output.Position = float4(ndc, 0.0, 1.0);
    output.UV = uv;
    return output;
}

float4 CursorPSMain(VertexOutput input) : SV_Target
{
    float4 cursor = CursorTexture.Sample(LinearSampler, input.UV);
    float3 scRgb = SrgbToLinear(saturate(cursor.rgb)) * (PaperWhiteNits / 80.0);
    return float4(scRgb, cursor.a);
}
