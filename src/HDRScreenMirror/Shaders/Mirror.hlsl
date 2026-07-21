cbuffer MirrorConstants : register(b0)
{
    float PaperWhiteNits;
    uint InputMode;
    uint Rotation;
    float Padding;
};

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

float3 SrgbToLinear(float3 value)
{
    float3 low = value / 12.92;
    float3 high = pow((value + 0.055) / 1.055, 2.4);
    return lerp(low, high, step(0.04045, value));
}

float PqEotf(float value)
{
    const float m1 = 2610.0 / 16384.0;
    const float m2 = 2523.0 / 32.0;
    const float c1 = 3424.0 / 4096.0;
    const float c2 = 2413.0 / 128.0;
    const float c3 = 2392.0 / 128.0;
    float p = pow(saturate(value), 1.0 / m2);
    float numerator = max(p - c1, 0.0);
    float denominator = max(c2 - c3 * p, 1e-6);
    return 10000.0 * pow(numerator / denominator, 1.0 / m1);
}

float3 Rec2020ToRec709(float3 value)
{
    return float3(
        1.660491 * value.r - 0.587641 * value.g - 0.072850 * value.b,
       -0.124550 * value.r + 1.132900 * value.g - 0.008349 * value.b,
       -0.018151 * value.r - 0.100579 * value.g + 1.118730 * value.b);
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float4 source = SourceTexture.Sample(LinearSampler, RotateUV(input.UV));
    float3 scRgb;

    if (InputMode == 0)
    {
        scRgb = source.rgb;
    }
    else if (InputMode == 2)
    {
        float3 nits = float3(PqEotf(source.r), PqEotf(source.g), PqEotf(source.b));
        scRgb = Rec2020ToRec709(nits) / 80.0;
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
